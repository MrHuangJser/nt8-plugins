# MotherBar 自动交易策略 - NinjaTrader 8 实施计划

## 一、策略概述

基于MotherBar（内包K线）规则的自动交易策略。当检测到内包K线形态时，在MB的关键价格水平挂单交易，支持加仓、自动止盈止损、MB失效管理。

- **交易品种**：ES、NQ（通过合约自动适配TickSize和PointValue）
- **时间周期**：跟随图表当前周期（无需额外数据序列）
- **最大亏损**：含加仓整体不超过 $500

---

## 二、核心数据结构

### 2.1 MB状态对象

```csharp
private class MBState
{
    public bool   IsActive;          // MB是否有效
    public int    FormBar;           // MB形成时的BarIndex
    public double BodyHigh;          // MB主体高点 (100%)
    public double BodyLow;           // MB主体低点 (0%)
    public double Range;             // MB主体振幅
    public int    TradeCount;        // 当前MB已完成的交易笔数（最多2笔）
    public bool   HasLongEntry;      // 是否已有多单入场
    public bool   HasShortEntry;     // 是否已有空单入场
    public bool   IsAddOnPending;    // 加仓单是否待触发
}
```

### 2.2 价格水平计算

所有价格水平基于 MB 主体的 High/Low 计算：

```
Level(pct) = BodyLow + Range * (pct / 100)
```

| 水平 | 百分比 | 用途 |
|------|--------|------|
| 300% | 3.0 | 空单止损 |
| 200% | 2.0 | MB失效（上方） |
| 161.8% | 1.618 | 加仓后空单止盈 |
| 123% | 1.23 | 限价卖出区域 |
| 111% | 1.11 | 参考线 |
| 100% | 1.0 | MB高点 / 空单加仓触发 |
| 89% | 0.89 | 参考线 |
| 79% | 0.79 | 参考线 |
| 66% | 0.66 | 参考线 |
| 50% | 0.5 | 止盈位（初始） |
| 33% | 0.33 | 参考线 |
| 21% | 0.21 | 参考线 |
| 11% | 0.11 | 参考线 |
| 0% | 0.0 | MB低点 |
| -23% | -0.23 | 限价买入区域 |
| -61.8% | -0.618 | 加仓后多单止盈 |
| -100% | -1.0 | MB失效（下方） / 多单加仓触发 |
| -200% | -2.0 | 多单止损 |

---

## 三、策略生命周期与状态机

### 3.1 MB生命周期状态流转

```
[无MB] → 检测到内包K线 → [MB激活/双向监控]
  ↓
[双向监控] 同时独立监控 123% 和 -23% 两个水平
  │
  ├─ K线High首次触及123% → 标记sellTouched（不可逆）→ 等该K线收盘
  │    → 收盘价 > 123%: 挂 Stop Market Sell @ 123%
  │    → 收盘价 ≤ 123%: 挂 Limit Sell @ 123%
  │
  ├─ K线Low首次触及-23% → 标记buyTouched（不可逆）→ 等该K线收盘
  │    → 收盘价 < -23%: 挂 Stop Market Buy @ -23%
  │    → 收盘价 ≥ -23%: 挂 Limit Buy @ -23%
  │
  ↓ （两个方向可各自独立触及并挂单，互不影响）

[挂单中] 一个或两个方向各有一个挂单等待成交
  ↓
  某方向入场单成交 → 取消对向挂单（若有）→ 同时挂该方向的加仓单
  ↓
[持仓中] 等待止盈/止损/加仓
  │
  ├─ 加仓成交 → 调整止盈位
  │
  ├─ 止盈成交 → 取消加仓单 → 该方向标记已完成(xxxDone)
  │    → 若对向未被触及过(xxxTouched==false)，恢复对向监控
  │    → 若对向已触及但挂单已被取消，不再重挂（该方向机会已用完）
  │
  ├─ 止损成交 → 该方向标记已完成(xxxDone)，同上逻辑
  │
  ↓
[MB失效] 任一条件触发 → 取消所有挂单 → 回到 [无MB]
```

**关键约束：每个方向最多交易1次，一个MB最多交易2笔（1多+1空）**

### 3.2 MB失效条件（任一触发）

1. 价格（K线High）达到 200% 水平
2. 价格（K线Low）达到 -100% 水平
3. 两个方向均已完成交易（sellDone && buyDone）
4. 超出交易时段

### 3.3 新MB出现的处理

- 当前MB有效时，忽略新出现的内包K线
- 仅在当前MB失效后，才识别新的MB

---

## 四、交易逻辑详细设计

### 4.1 MB检测逻辑（每根K线收盘时）

```
条件：
  High[0] <= High[1] AND Low[0] >= Low[1]
  AND NOT (High[0] == High[1] AND Low[0] == Low[1])

若满足且当前无有效MB：
  创建新MB，BodyHigh = High[1], BodyLow = Low[1]
  进入挂单流程
```

### 4.2 挂单逻辑

**核心原则：观察123%和-23%被哪根K线「第一次」触及，触及后等该K线收盘再挂单。每个方向最多交易1次。**

**触发条件检查（每根K线收盘时）：**

#### 卖出方向（做空入场）
1. 前提：该方向尚未被触及过（`_sellTouched == false`）
2. 检查当前K线 High 是否 >= Level(123%)
3. 若是，标记 `_sellTouched = true`（不可逆），K线收盘后判断挂单类型：
   - 收盘价 > Level(123%) → 提交 **Stop Market Sell** @ Level(123%)
   - 收盘价 <= Level(123%) → 提交 **Limit Sell** @ Level(123%)

#### 买入方向（做多入场）
1. 前提：该方向尚未被触及过（`_buyTouched == false`）
2. 检查当前K线 Low 是否 <= Level(-23%)
3. 若是，标记 `_buyTouched = true`（不可逆），K线收盘后判断挂单类型：
   - 收盘价 < Level(-23%) → 提交 **Stop Market Buy** @ Level(-23%)
   - 收盘价 >= Level(-23%) → 提交 **Limit Buy** @ Level(-23%)

#### 双向管理规则
- MB形成后**同时独立监控**两个方向
- 两个方向可以在不同K线上各自触及并各自挂单
- 一个方向的入场单**成交后**，取消另一方向的**挂单**（若有）
- 止盈/止损后，若另一方向**尚未被触及**（`_xxxTouched == false`），恢复对该方向的监控
- 若另一方向**已被触及但挂单已被取消**，不再重新挂单（每个方向只有一次机会）
- 一个MB最多交易2笔（1多 + 1空），每个方向最多1笔

### 4.3 止盈止损设置

**所有止盈止损均为整体平仓（全部仓位一次性离场），不存在部分平仓。**

#### 初始入场（未加仓时）

| | 做多 | 做空 |
|---|---|---|
| 止盈 | Level(50%)，全部平仓 | Level(50%)，全部平仓 |
| 止损 | Level(-200%)，全部平仓 | Level(300%)，全部平仓 |

#### 加仓后（两仓共存时）

| | 做多（第一仓@-23% + 加仓@-100%） | 做空（第一仓@123% + 加仓@100%） |
|---|---|---|
| 整体止盈 | Level(-61.8%)，**全部**平仓 | Level(161.8%)，**全部**平仓 |
| 整体止损 | Level(-200%)，**全部**平仓（不变） | Level(300%)，**全部**平仓（不变） |

### 4.4 加仓逻辑

#### 做多方向
- **时机**：第一仓入场成交后，挂 Limit Buy @ Level(-100%)
- **仓位**：与第一仓相同数量
- **加仓成交后**：
  - 将**所有仓位**的止盈从 Level(50%) 移动到 Level(-61.8%)
  - 止损保持 Level(-200%) 不变（所有仓位共用一个止损）
- **取消条件**：第一仓止盈成交时（价格到50%时加仓单不可能成交），立即取消加仓挂单

#### 做空方向
- **时机**：第一仓入场成交后，挂 Limit Sell @ Level(100%)
- **仓位**：与第一仓相同数量
- **加仓成交后**：
  - 将**所有仓位**的止盈从 Level(50%) 移动到 Level(161.8%)
  - 止损保持 Level(300%) 不变（所有仓位共用一个止损）
- **取消条件**：第一仓止盈成交时，立即取消加仓挂单

### 4.5 仓位计算

以加仓后的最坏情况反推仓位：

```
做多：
  风险点数 = Level(-61.8%) - Level(-200%) = 1.382 × Range
  （即加仓后止盈位到止损位的距离，代表单手最大亏损点数）
  单手风险金额 = 风险点数 × PointValue
  总手数 = Floor($500 / 单手风险金额)
  单仓手数 = 总手数 / 2
  若总手数 < 2，则单仓手数 = 1（即最少各开1手，接受可能超$500的风险）

做空：
  风险点数 = Level(300%) - Level(161.8%) = 1.382 × Range
  计算方式同上

加仓手数 = 与第一仓相同
```

---

## 五、用户可配置参数

### 5.1 参数列表

| 参数名 | 类型 | 默认值 | 分组 | 说明 |
|--------|------|--------|------|------|
| StrategyDirection | Enum | Both | 策略配置 | 交易方向（做多/做空/双向） |
| TradeStartTime | int | 210000 | 时段配置 | 交易开始时间（东八区 HHMMSS） |
| TradeEndTime | int | 060000 | 时段配置 | 交易结束时间（东八区 HHMMSS） |
| UtcOffsetHours | int | -5 | 时段配置 | 交易所相对UTC偏移（EST=-5） |
| MaxTotalLoss | int | 500 | 仓位管理 | 含加仓的最大总亏损（美元） |
| EnableAddOn | bool | true | 仓位管理 | 是否启用加仓 |
| MaxTradesPerMB | int | 2 | 交易限制 | 单个MB最大交易笔数 |
| EnableBacktest | bool | true | 回测配置 | 是否在历史数据上执行 |
| ShowMBLines | bool | true | 可视化 | 是否显示MB价格水平线 |
| MBLineLength | int | 20 | 可视化 | MB线段向右延伸的K线数量 |
| BullColor | Brush | Green | 可视化 | 多头相关线段颜色 |
| BearColor | Brush | Red | 可视化 | 空头相关线段颜色 |
| NeutralColor | Brush | Gray | 可视化 | 中性线段颜色 |

### 5.2 时区转换逻辑

```
用户输入东八区时间 → 转换为交易所本地时间（EST/EDT）
偏移量 = 8 - (-5) = 13小时（EST）或 8 - (-4) = 12小时（EDT）

示例：东八区 21:00 = EST 08:00（次日推算）

实现方式：
  exchangeTime = utc8Time - TimeSpan.FromHours(8 - UtcOffsetHours)
  处理跨日情况
```

---

## 六、可视化设计

### 6.1 线段绘制

使用 `Draw.Line()` 绘制有限长度的水平线段：

```csharp
Draw.Line(this, tag, false,
    startBarAgo, price, endBarAgo, price,
    color, dashStyle, width);
```

- **起点**：MB形成时的K线位置
- **终点**：起点 + MBLineLength 根K线（或到MB失效时的K线）
- **MB失效时**：停止延伸，线段固定

### 6.2 线段层级与样式

| 水平 | 颜色 | 线型 | 宽度 | 标签 |
|------|------|------|------|------|
| 300% | BearColor | Dash | 1 | "SL Short 300%" |
| 200% | BearColor | Solid | 2 | "Invalid 200%" |
| 161.8% | BearColor | Dot | 1 | "AddOn TP 161.8%" |
| 123% | BearColor | Solid | 2 | "**Sell Zone 123%**" |
| 100% | NeutralColor | Solid | 2 | "MB High" |
| 50% | NeutralColor | DashDot | 2 | "**TP 50%**" |
| 0% | NeutralColor | Solid | 2 | "MB Low" |
| -23% | BullColor | Solid | 2 | "**Buy Zone -23%**" |
| -61.8% | BullColor | Dot | 1 | "AddOn TP -61.8%" |
| -100% | BullColor | Solid | 2 | "Invalid -100%" |
| -200% | BullColor | Dash | 1 | "SL Long -200%" |

### 6.3 动态更新

- 每根新K线时，若MB仍有效，延伸线段终点
- MB失效时，线段变为固定（不再延伸）
- 可选：失效后线段变灰/降低透明度

---

## 七、代码架构

### 7.1 文件结构

单文件策略：`MotherBarStrategy.cs`

### 7.2 类结构

```csharp
namespace NinjaTrader.NinjaScript.Strategies
{
    public class MotherBarStrategy : Strategy
    {
        // ===== 私有状态 =====
        private MBState _currentMB;
        private bool    _waitingForSellSignal;   // 等待K线触及123%
        private bool    _waitingForBuySignal;    // 等待K线触及-23%
        private string  _activeDirection;        // "Long" / "Short" / null
        private bool    _addOnFilled;            // 加仓是否已成交
        private int     _entryQty;               // 入场数量

        // ===== 核心方法 =====
        OnStateChange()          // 初始化参数
        OnBarUpdate()            // 主逻辑入口

        // ===== 辅助方法 =====
        DetectMotherBar()        // MB检测
        CalcLevel(double pct)    // 价格水平计算
        CheckEntrySignals()      // 检查入场信号触发
        PlaceSellOrder()         // 挂卖单逻辑
        PlaceBuyOrder()          // 挂买单逻辑
        ManageAddOnOrders()      // 加仓单管理
        CheckMBInvalidation()    // MB失效检查
        CalculateQuantity()      // 仓位计算
        UpdateVisualization()    // 更新线段
        ConvertTimeZone()        // 时区转换
        IsInTradeSession()       // 交易时段判断

        // ===== 事件处理 =====
        OnOrderUpdate()          // 订单状态变化
        OnExecutionUpdate()      // 成交回报
    }
}
```

### 7.3 OnBarUpdate 主流程

```
1. 检查最低K线数量
2. 检查是否启用回测（EnableBacktest）
3. 时段判断（时区转换后）
   - 超出时段：平仓 + 取消挂单 + 重置MB
4. 检查MB失效条件
   - 失效：取消所有挂单 + 重置状态
5. 若无有效MB：
   - 检测新MB → 若发现则激活并初始化
6. 若有有效MB且无持仓：
   - 检查入场信号（触及区域判断）
   - 满足条件则挂单
7. 若有持仓：
   - 监控加仓单状态
   - 加仓成交后调整止盈
8. 更新可视化线段
```

### 7.4 OnExecutionUpdate 事件处理

```
1. 入场成交：
   - 记录方向和数量
   - 取消另一方向的监控
   - 若启用加仓，挂加仓单

2. 加仓成交：
   - 调整止盈到新水平
   - 标记加仓已成交

3. 止盈成交：
   - TradeCount++
   - 取消加仓挂单（若有）
   - 若 TradeCount < MaxTradesPerMB：重新启动双向监控
   - 否则：MB完成，等待失效

4. 止损成交：
   - TradeCount++
   - 取消加仓挂单（若有）
   - 同上逻辑
```

---

## 八、实施步骤（开发顺序）

### Phase 1：基础框架（先跑通）
1. 创建策略文件，定义所有参数
2. 实现MB检测逻辑
3. 实现价格水平计算
4. 实现基础可视化（仅MB高低点和50%线）
5. **验证点**：图表上能正确识别并标记MB

### Phase 2：单向交易（先做多）
1. 实现买入方向的触及检测
2. 实现Limit Buy / Stop Market Buy 挂单
3. 实现止盈止损设置
4. 实现仓位计算
5. 实现MB失效逻辑
6. **验证点**：能正确做多并止盈/止损

### Phase 3：双向交易
1. 添加卖出方向逻辑
2. 实现双向挂单互斥管理
3. 实现交易计数和重复挂单
4. **验证点**：双向挂单，一侧触发后取消另一侧

### Phase 4：加仓逻辑
1. 实现加仓挂单
2. 实现加仓后止盈调整
3. 实现止盈后取消加仓单
4. 验证整体风险不超$500
5. **验证点**：加仓流程完整运行

### Phase 5：时段与回测
1. 实现东八区时区转换
2. 实现交易时段控制
3. 移除/保留历史数据过滤开关
4. **验证点**：回测结果合理

### Phase 6：完善可视化
1. 绘制所有价格水平线段
2. 实现线段动态延伸与固定
3. 失效后视觉区分
4. **验证点**：图表清晰展示所有MB信息

### Phase 7：测试与优化
1. ES/NQ分别回测验证
2. 边界条件测试（跨日MB、连续内包等）
3. 日志完善（关键决策点输出Print）
4. 参数优化建议

---

## 九、风险点与注意事项

1. **NinjaTrader订单管理**：NT8的 `EntriesPerDirection` 需设为2（支持加仓），`EntryHandling` 用 `UniqueEntries`
2. **订单命名**：每个订单需唯一命名以便追踪（如 "MB_Long_1", "MB_Long_AddOn", "MB_Short_1"）
3. **挂单取消时机**：使用 `OnOrderUpdate` 确认取消成功后再执行后续操作
4. **内包K线连续出现**：可能出现多根连续内包线，仅取第一组
5. **止盈止损价格精度**：需对齐到 TickSize（使用 `Instrument.MasterInstrument.RoundToTickSize()`）
6. **加仓风险验证**：入场前需预计算加仓场景的总风险，确保不超$500
7. **实时 vs 回测差异**：挂单在回测中的Fill逻辑与实时不同，需注意 `IsFillLimitOnTouch` 设置
