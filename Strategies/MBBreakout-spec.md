# MotherBar Breakout Strategy (MBBreakout) - 策略设计文档

## 一、策略概述

基于Mother Bar突破确认信号的自动交易策略。当MB形成后，等待K线收盘突破Trap Zone（Confirmation Signal #1），然后使用Stop Order入场 + Limit Order在LMT Zone加仓，分批止盈管理。

**核心特征：**
- 每次开仓/加仓固定1手
- 最多同时持有2手（入场1手 + 加仓1手）
- 基于收盘价确认（OnBarClose模式）

---

## 二、策略状态机

```
[Idle] ──(内包K检测)──→ [WaitingConfirmation]
                              │
                    ┌─────────┼─────────┐
                    │         │         │
              (确认出现)  (失效条件)  (更大MB出现)
                    │         │         │
                    ▼         ▼         ▼
            [StopPending]   [Idle]   [替换MB→WaitingConfirmation]
                    │
              ┌─────┼─────┐
              │     │     │
         (Stop成交) │ (反向确认出现)
              │     │     │
              ▼     │     ▼
        [EntryFilled]│  [翻转方向→StopPending]
              │     │
        (TP/SL触发) │(失效/更大MB)
              │     │
              ▼     ▼
            [Idle] [Idle]
```

**说明：** 主仓与加仓共享同一TP水平（161.8%/-61.8%），到达后全部平仓 → Idle

---

## 三、MB检测

与现有逻辑一致：
- 当前K线High ≤ 前一根High 且 Low ≥ 前一根Low（含相等，但不能高低点都相等）
- 前一根K线作为MB主体，定义100%（High）和0%（Low）

---

## 四、确认信号（Confirmation Signal #1）

| 方向 | 条件 | 含义 |
|------|------|------|
| 多头 | 收盘价 > MB 111% 水平 | K线收在上方Trap Zone之外 |
| 空头 | 收盘价 < MB -11% 水平 | K线收在下方Trap Zone之外 |

**注意：** 仅在`WaitingConfirmation`和`StopPending`状态下检测。

---

## 五、入场逻辑

### 5.1 Stop Order（主入场）

| 方向 | Stop价格 | 数量 |
|------|----------|------|
| 多头 | 确认K线 High + 1 tick | 1手 |
| 空头 | 确认K线 Low - 1 tick | 1手 |

### 5.2 Limit Order（加仓，仅在Stop成交后下单）

| 方向 | Limit价格 | 数量 |
|------|-----------|------|
| 多头 | MB 79% 水平（上方LMT Zone） | 1手 |
| 空头 | MB 21% 水平（下方LMT Zone） | 1手 |

### 5.3 方向翻转规则

在`StopPending`状态（Stop未成交），若出现反向确认信号：
1. 取消当前Stop单
2. 按新方向下Stop单
3. 更新方向标记

---

## 六、止损

| 方向 | 止损价格 | 适用于 |
|------|----------|--------|
| 多头 | MB -23% 水平 | 主仓 + 加仓（初始） |
| 空头 | MB 123% 水平 | 主仓 + 加仓（初始） |

---

## 七、止盈与仓位管理

主仓和加仓共享同一止盈水平，到达后全部平仓。

### 7.1 仅主仓成交（加仓未触发）

| 事件 | 操作 |
|------|------|
| 价格到达TP | 平仓1手，取消加仓单，MB结束 → Idle |
| 价格到达SL | 止损1手，取消加仓单，MB结束 → Idle |

### 7.2 主仓+加仓均成交（共2手）

| 事件 | 操作 |
|------|------|
| 价格到达TP | 全部平仓2手，MB结束 → Idle |
| 价格到达SL | 全部止损2手，MB结束 → Idle |

### 7.3 止盈目标水平

| 方向 | TP |
|------|-----|
| 多头 | 161.8%（可配置） |
| 空头 | -61.8%（可配置） |

---

## 八、MB失效条件

| 条件 | 当前状态 | 操作 |
|------|----------|------|
| 价格到达161.8%且无持仓、无Stop挂单 | WaitingConfirmation | MB失效 → Idle |
| 价格到达-61.8%且无持仓、无Stop挂单 | WaitingConfirmation | MB失效 → Idle |
| 更大MB出现且无持仓 | WaitingConfirmation / StopPending | 取消挂单，替换为新MB |
| 时段结束 | 任意 | 平仓 + 取消挂单 → Idle |

**关于"更大MB"替换：**
- 新检测到的内包K线形成的MB，其Range > 当前MB Range
- 仅在无持仓时替换（StopPending状态需先取消Stop单）
- 有持仓时忽略新MB

**注意：** StopPending状态下价格到达161.8%/-61.8%意味着stop单也应该已经无法成交（价格已远离），此时取消stop单，MB失效。

---

## 九、可配置参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| StrategyDirection | Both | 双向/只做多/只做空 |
| TradeStartTime | 210000 | 交易开始时间（东八区HHMMSS） |
| TradeEndTime | 060000 | 交易结束时间（东八区HHMMSS） |
| UtcOffsetHours | -5 | 交易所UTC偏移 |
| LongTP1Level | 161.8% | 多头第一止盈 |
| LongTP2Level | 200% | 多头第二止盈（Runner） |
| ShortTP1Level | -61.8% | 空头第一止盈 |
| ShortTP2Level | -100% | 空头第二止盈（Runner） |
| LongAddOnLevel | 79% | 多头加仓价位 |
| ShortAddOnLevel | 21% | 空头加仓价位 |
| EnableAddOn | true | 是否启用加仓 |
| ShowMBLines | true | 显示MB水平线 |
| MBLineLength | 20 | 线段长度 |
| EnableBacktest | true | 回测开关 |

---

## 十、日志输出要点

每个关键事件打印：
- MB检测：编号、High/Low/Range
- 确认信号：方向、收盘价、确认K高低点
- Stop下单/成交：方向、价格
- 加仓下单/成交：价格
- 方向翻转：旧方向→新方向
- TP/SL触发：级别、平仓数量、剩余仓位
- BE移动：新SL价格
- MB失效：原因
