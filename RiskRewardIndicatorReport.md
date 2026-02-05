下面是基于目前已收集信息整理出的**可落地研发方案**，专门针对你想做的 NinjaTrader 8 自动仓位计算 Addon，参考了 MAS Capital 的 Risk Reward Indicator 思路，以及几个开源 / 免费方案的实现方式。

---

# 1. 功能与目标确认

结合你给的 MAS Capital Risk Reward Indicator 及其他类似工具的情况，可以把你要做的 Addon 功能拆成两层：

1. **风险/回报可视化层（Risk/Reward 可视化）**
   - 在图表上用线或扩展（extensions）标出：
     - 入场价（Entry）
     - 止损价（Stop）
     - 目标价（Target1、Target2……如 0.5R / 1R / 2R）
   - 实时显示 R:R 比例、止损距离、目标盈利距离等。
   - 支持通过鼠标点击或拖拽快速调整位置。
   - MAS Capital 的工具就是主要做这一层：一键在当前 K 线基础上自动画 R/R 扩展，Stop 放在 K 线高/低点，0.5R/1R/2R 投射出去，适合快速目测行情是否「值得做」[1]。

2. **仓位自动计算 & 下单辅助层（Position Sizing）**
   - 用户配置风险参数：
     - 按账户百分比：如「每笔亏损不超过账户的 1%」
     - 按固定金额：如「每笔亏损不超过 \$200」
   - 根据 Entry–Stop 的距离，结合品种的 TickSize / TickValue，自动算出**应下多少合约/股数**。
   - 把算出的数量自动写入 Chart Trader 的 **Quantity** 输入框，实现「一拖线就出仓位」的体验。
   - 这一层的思路，在 GitHub 的开源项目 **ninjatrader-fixrisk**（FixedRiskSizer 指标）中有完整实现，可以直接借鉴[2]。

你要做的 Addon，本质上是把这两层合在一起：  
**可视化 R/R + 自动仓位计算 + 自动写入 Chart Trader QTY**。

---

# 2. 核心原理（数学与交易逻辑）

## 2.1 仓位计算统一公式

无论是 FixRisk 还是 a1RiskReward_v2，核心都是同一套公式：

1. **价格与 Tick 转换**

- 假设：
  - `entryPrice`：入场价
  - `stopPrice`：止损价
  - `TickSize`：该合约最小跳动价位（从 NinjaTrader Instrument 对象取）
  - `PointValue`：每一个整点价值，比如某些期货 1 点 = \$50（同样从 Instrument 取）
- 则单个 Tick 的价值：

  ```csharp
  double tickValue = Instrument.MasterInstrument.PointValue * Instrument.MasterInstrument.TickSize;
  ```

1. **止损距离（Tick 数）**

- 对多空统一处理，a1RiskReward_v2 的做法是：

  ```csharp
  int tradeSide = (stopPrice <= entryPrice) ? 1 : -1; // 多 or 空
  double risk  = (entryPrice - stopPrice) * tradeSide;
  int tickStop = (int) Math.Round(risk / TickSize);
  ```

1. **每笔风险金额**

有两种常见模式（a1RiskReward_v2 也是这么做的）：

- 按账户百分比：

  ```csharp
  double dollarRisk = accountSize * (accountRiskPercent / 100.0);
  ```

- 按固定金额：

  ```csharp
  double dollarRisk = userDollarRisk;
  ```

也可以像 FixRisk 那样只支持「固定风险金额」，由用户直接输入，如 `RiskAmount = 200` 就表示每笔最多亏 \$200[2]。

1. **仓位大小（合约数）**

- a1RiskReward_v2 的核心公式：

  ```csharp
  shares = (int)((dollarRisk * sharesMultiplier) 
                 / (tickStop * dollarsPerTick));
  ```

- 对于普通期货 / 股票，`sharesMultiplier = 1`；
- 对外汇，会根据标准/迷你/微型账户模式设置 multiplier（例如 10、100 等）[3]。

1. **风险回报（Risk-Reward Ratio）**

若你也要像 MAS Capital 那样显示 0.5R / 1R / 2R：

```csharp
double reward     = (targetPrice - entryPrice) * tradeSide;
int    tickTarget = (int)Math.Round(reward / TickSize);
double riskReward = risk != 0 ? reward / risk : 0;
```

## 2.2 实时价格的选择（Bid / Ask）

FixRisk 的处理方式值得直接照抄：

- 多头：使用 **Ask** 作为入场价，止损在价格下方；
- 空头：使用 **Bid** 作为入场价，止损在价格上方。

```csharp
double entry = GetCurrentBid();
if (Close[0] > slLine.StartAnchor.Price)
    entry = GetCurrentAsk();
```

这样你的仓位计算会更加接近真实成交价，而不是简单用当前 K 线收盘价。

---

# 3. NinjaTrader 8 实现思路（技术方案）

## 3.1 开发形态选择

你可以从以下两种形态入手：

1. **以「Indicator」形式实现（推荐起步方案）**
   - 好处：
     - 可以直接画线、画文字、响应拖拽；
     - 可以直接找到图表上的 Chart Trader 控件，把数量写进去；
     - 部署时用户只需在图表上加载一个 Indicator。
   - FixRisk 就是一个纯 Indicator，已经实现了：
     - 拖拽 Stop Line；
     - 自动更新 Chart Trader 的 QTY；
     - 文本在图表上显示风险信息[2]。

2. **以「AddOn / Panel」形式实现**
   - 在 NinjaTrader 里开一个独立的面板窗口（如侧边栏 GUI），统一管理风险参数、品种、模板，较复杂。
   - 手工查找每个 Chart / Strategy 上的控件并交互。
   - 适合产品化后期，但不建议第一次就上来做这个。

根据你现在的目标，**建议第一步做一个「扩展版 FixRisk + R/R 可视化 的 Indicator」**，稳定后再考虑做完整 AddOn。

## 3.2 结构设计（基于 Indicator 的方案）

### 核心类：`RiskRewardSizer : Indicator`

主要成员建议如下：

```csharp
// 用户参数
[NinjaScriptProperty]
public double RiskPercent { get; set; }    // 按账户百分比风险
[NinjaScriptProperty]
public double FixedRiskAmount { get; set; }// 固定亏损金额
[NinjaScriptProperty]
public bool UseFixedRisk { get; set; }     // 切换模式
[NinjaScriptProperty]
public double TargetRR { get; set; }       // 默认目标 R:R（如 2.0）可选

// 绘图对象
private HorizontalLine entryLine;
private HorizontalLine stopLine;
private HorizontalLine targetLine;   // 或多个 targetLineX
private TextFixed      infoText;

// 环境数据
private QuantityUpDown qtyField;
private double tickSize;
private double tickValue;

// 状态缓存（避免重复计算）
private double lastEntryPrice;
private double lastStopPrice;
private int    lastQty;
```

### 生命周期方法

- `OnStateChange()`
  - `State.SetDefaults`：设置描述、名称、是否 overlay、Calculate 模式等。
  - `State.Realtime`：
    - 初始化 `tickSize`、`tickValue`；
    - 创建 Entry / Stop / Target 三条水平线；
    - 创建 `TextFixed` 做信息显示；
    - 通过 `ChartControl.Dispatcher` 拿到 Chart Trader 的 `QuantityUpDown` 控件（参考 FixRisk 的写法）[2]。

- `OnBarUpdate()`
  - 检查是否 Realtime；
  - 调用 `RecalculateIfNeeded()`：
    - 对比当前 Entry / Stop 与上一次是否变化；
    - 若变了，则重新计算仓位、R:R 并更新 UI & QTY。

- `OnRender()`
  - FixRisk 中在 `OnRender()` 里也调用 `calc()`，主要是为了拖动线后及时更新；
  - 你也可以一样做：在 `OnRender()` 内重复调用你的计算函数。

### 获取 Chart Trader QTY 控件（关键）

FixRisk 的做法示例（已简化成伪代码）：

```csharp
ChartControl.Dispatcher.InvokeAsync((Action)(() =>
{
    qtyField = Window
        .GetWindow(ChartControl.Parent)
        .FindFirst("ChartTraderControlQuantitySelector") 
        as QuantityUpDown;
}));
```

之后你就可以直接写入：

```csharp
qtyField.Value = qty;   // qty 是你算出来的合约数
```

---

# 4. 详细算法与伪代码

以下用一个更接近你目标的完整流程说明（在 Indicator 中实现）：

```csharp
private void RecalculateIfNeeded()
{
    if (entryLine == null || stopLine == null || qtyField == null)
        return;

    double entryPrice = GetDynamicEntryPrice(); // 用 Bid/Ask 决定多空
    double stopPrice  = stopLine.StartAnchor.Price;

    // 如果价格没变且线位置没变，不重复计算
    if (entryPrice.ApproxCompare(lastEntryPrice) == 0 &&
        stopPrice.ApproxCompare(lastStopPrice) == 0)
        return;

    lastEntryPrice = entryPrice;
    lastStopPrice  = stopPrice;

    // 1. 计算止损 Tick 数
    double diff       = Math.Abs(entryPrice - stopPrice);
    double ticksToSL  = diff / tickSize;
    if (ticksToSL <= 0)
        ticksToSL = 1;

    // 2. 计算本次风险金额
    double riskAmount = UseFixedRisk && FixedRiskAmount > 0
        ? FixedRiskAmount
        : GetAccountBalance() * RiskPercent / 100.0;

    // 3. 计算可开合约数
    double riskPerContract = ticksToSL * tickValue;
    int qty = (int)Math.Floor(riskAmount / riskPerContract);
    if (qty < 1) qty = 1;

    // 4. 计算 R:R，更新 Target 线
    double rDist  = diff;                       // 1R 的价格距离
    double tPrice = entryPrice + rDist * TargetRR * GetSideSign(entryPrice, stopPrice);
    targetLine.StartAnchor.Price = tPrice;

    // 5. 更新图表上显示
    UpdateInfoText(entryPrice, stopPrice, tPrice, qty, riskAmount, ticksToSL);

    // 6. 写入 Chart Trader QTY
    if (qtyField.Value != qty)
        qtyField.Value = qty;

    lastQty = qty;
}
```

其中关键辅助函数包括：

- `GetDynamicEntryPrice()`：根据 Entry/Stop 相对位置决定多空，选用 Bid/Ask。
- `GetAccountBalance()`：从相关 Account 对象获取当前余额（具体 API 需在 NinjaTrader 文档中查找）。
- `GetSideSign(entry, stop)`：返回 +1（多头）或 –1（空头）。

---

# 5. 与 MAS Capital Risk Reward Indicator 的差异与借鉴

通过对该产品公开介绍页的信息归纳，可看出其特点[1]：

- 一键点击（通常是鼠标中键）在图表上生成 R/R 扩展：
  - Stop 自动放在当前 K 线低点（多头）或高点（空头）；
  - Target 自动给 0.5R、1R、2R 等固定比例扩展；
  - Entry 与 Stop/Target 是基于当前 K 线实体/影线高度计算，不能随意独立修改。
- 偏重**视觉规划**而非**仓位自动填充**。
- 更多是「快速评估这根 K 的结构适不适合做」——例如有用户评价，如果 K 线太大，0.5R 就已经 40 ticks，那这种 setup 就会被自动过滤掉[1]。

和你要做的 Addon 相比：

- 你可以借鉴它：
  - 单击一次自动基于当前 K 线生成一个 R/R 模板；
  - Stop 自动贴在 K 线高/低点；
  - 0.5R / 1R / 2R 等扩展线自动画出。
- 然后在此基础上，你再加上：
  - 仓位自动计算（按百分比/固定金额）；
  - 自动写入 Chart Trader QTY；
  - 可选地允许拖动 Stop/Entry 自由调整，再动态重算仓位。

---

# 6. 参考开源实现的实践要点

从目前调研到的两个重点代码思路中，可以直观看到你可以照搬的关键点：

1. **开源 FixRisk（FixedRiskSizer 指标）**[2]
   - 用一个可拖拽的 `HorizontalLine` 做虚拟止损线；
   - 每次线位置变化 or 价格变化就：
     - 计算 Stop–Entry Tick 数；
     - 计算合适仓位；
     - 把结果直接写入 `ChartTraderControlQuantitySelector`；
     - 在图表上用 `TextFixed` 显示「风险金额 + 推荐合约数」。
   - 这是一个非常纯粹的「固定金额风险 → 自动仓位 → 自动填 QTY」实现，可以视为你仓位计算部分的**蓝本**。

2. **a1RiskReward_v2 Risk/Reward Indicator**[3]
   - 利用用户手动画的 Entry / Stop / Target 线来算 Risk、Reward、R:R；
   - 用户既可按账户百分比风险，也可以输入固定金额；
   - 另外还有外汇时的手数 multiplier（Standard/Mini/Micro）；
   - 在图表上同时显示：
     - Shares（仓位大小）
     - Risk（风险金额）
     - TickStop / TickTarget
     - RiskReward 比例
   - 虽然它没有自动写 QTY，但你可以把它的 R:R 逻辑和 FixRisk 的自动填 QTY 逻辑**组合在一起**。

---

# 7. 推荐实现路线（从 MVP 到产品）

结合上面所有信息，给你一个**循序渐进**的具体落地路线：

## 阶段 1：做一个「FixRisk 增强版」指标（MVP）

功能：

- 仅支持**固定风险金额**（如 \$200），和 FixRisk 一样；
- 只有一条 Stop 线（虚拟止损）：
  - 入场自动用当前 Bid/Ask；
  - 拖动 Stop 线 → 实时改变止损距离；
- 自动计算合约数并写入 Chart Trader QTY；
- 在图表左上/左下角显示：
  - 风险金额；
  - 止损距离（ticks + 价格差）；
  - 推荐合约数。

实现方式：

- 代码结构高度类似 FixRisk；
- 只改 UI 文本和参数名，先跑通流程。

## 阶段 2：加入 Entry / Target 线 + R:R 可视化

在阶段 1 成功基础上增加：

- 用三条线：Entry / Stop / Target；
  - Entry 默认 = 当前价格；
  - Stop 默认 = 最近 K 线高/低（随多空自动选择）；
  - Target 默认 = 2R（可以改参数）。
- 拖动 Entry 或 Stop 时：
  - 重算 TickStop、TickTarget、Risk、Reward、R:R；
- 图表显示：
  - Risk:Reward 比例；
  - 各目标价位（0.5R / 1R / 2R）；
  - 对应潜在盈利金额。

此时，你的功能已经非常接近 MAS Capital 的 Risk Reward Indicator（从视觉角度），但又多了「自动仓位写入」这一独特点。

## 阶段 3：支持账户百分比 + 策略联动（高级）

进一步完善：

- 风险模式支持：
  - 固定 \$ 风险；
  - 账户百分比风险；
- 读取账户余额（需要在 NinjaTrader 账户 API 中查找正确接口）；
- 对接 Strategy：
  - 指标暴露计算结果（如 PositionSize plot）；
  - Strategy Builder 中可以读取这个 plot，按你计算的仓位下单。

---

# 8. 结论：你的 Addon 的实现“公式”

**概念公式：**

> 交互线条（Entry/Stop/Target） + 品种 Tick 信息  
> → 计算止损距离（Ticks）  
> → 结合风险参数（账户百分比/固定 \$）  
> → 计算仓位大小  
> → 更新图表文字 + Chart Trader QTY  
> →（可选）为策略提供统一的仓位输入

**技术公式：**

- 开发形态：从 **Indicator** 入手；
- 可视化：借鉴 MAS Capital 的单击绘制 & 基于当前 K 线的 R/R 扩展思想[1]；
- 计算逻辑：综合使用 a1RiskReward_v2 的 risk/reward 公式[3] 和 FixRisk 的 Chart Trader 交互实现[2]；
- 逐步演进到真正的 AddOn 及策略联动。

如果你接下来希望，我可以帮你按 NinjaTrader 官方工程模板，把上述结构整理成一个**可以直接导入 NT8 的 .cs 指标文件骨架**，你只需在编辑器里粘上、编译即可测试。  

---

### References

[1] NinjaTrader 8 Risk Reward Indicator. <https://www.mascapital.uk/shop/ninjatrader-8-indicators/risk-reward-indicator/>  
[2] szabonorbert/ninjatrader-fixrisk（FixedRiskSizer 源码与说明）. <https://github.com/szabonorbert/ninjatrader-fixrisk>  
[3] a1RiskReward_v2.cs 核心风险回报与仓位计算实现. <https://github.com/magols/NinjaTraderDev/blob/master/Indicator/a1RiskReward_v2.cs>
