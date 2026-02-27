下面分两部分回答你的问题：

1. 先讲「如何从逻辑层面把这个 Pine Script 映射到 NinjaTrader」
2. 再给出一份「接近完整的 NinjaTrader 策略/指标代码结构示例」和关键注意事项

---

## 一、TradingView → NinjaTrader 的逻辑映射

先把你这段 Pine Script 的核心拆开：

### 1. 参数与基本概念

- 策略方向：都做 / 只做多 / 只做空  
- 交易时段：ETH 时段 18:00–09:30（美东），用于过滤 inETH
- 仓位管理：
  - 最大单仓亏损 `maxLossValue`
  - 多/空最小利润点数
- 入场条件：
  - IBS 过滤：`ibs = (close - low) / (high - low) * 100`
  - ATR 过滤：`atr / atr[1] >= minAtrValue`
- K 线信息：
  - 多头柱：`close > open`
  - 空头柱：`close < open`
- 空头趋势函数 `isBearTrend()`：最近两根 K 线的低点逐根走弱

### 2. 进出场逻辑要点

**多头进场 longCondition**（并且当前无仓位）：

- 只在 inETH 内
- 当前 K 线是阳线
- `ibs > bullIBSTarget`
- `high - close >= longMinProfitPoint`（预期有足够回撤空间）
- 策略方向允许做多
- ATR 增幅满足 `atr / atr[1] >= minAtrValue`（如果开启过滤）

价格与仓位：

```pinescript
limitPrice = (high - low) / 2 + high  // 实际上是 high + range/2，上方挂限价
stopPrice  = low
qty        = floor(maxLossValue / (close - low) / syminfo.pointvalue)
if (limitPrice - close) / (close - stopPrice) >= 0.25
    strategy.entry(...)
    strategy.exit(... limit = limitPrice, stop = stopPrice - syminfo.mintick)
```

**空头进场 shortCondition** 类似，只是方向相反，止损在 high，上方 IBSTarget/ATR 过滤 & `isBearTrend()`。

**强制平仓：**

- 当 `session.islastbar`（收盘）  
- 或不在 inETH 且有持仓  
- 执行 `strategy.cancel_all()` + `strategy.close_all(immediately = true)`

---

## 二、在 NinjaTrader 中应该怎么写（思路 + 代码结构）

### 1. 准备工作：语言与对象差异

- NinjaTrader 使用 C#，脚本分为：
  - 指标：继承 `Indicator`
  - 策略：继承 `Strategy`
- 你原脚本里有个 IBS 指标，NinjaTrader 没有内置，需要**自定义一个 Indicator**
- ATR 在 NinjaTrader 中有内置 `ATR(int period)`
- 仓位、点值替代：
  - `strategy.position_size == 0` → `Position.Quantity == 0`
  - `syminfo.pointvalue` → `Instrument.MasterInstrument.PointValue`
  - `syminfo.mintick` → `TickSize`（或 `Instrument.MasterInstrument.TickSize`）
- 回调：
  - 所有逻辑放到 `OnBarUpdate()` 里
  - 初始化配置在 `OnStateChange()` 不同 State 下完成

---

### 2. 先实现一个 IBS 指标（Pine → Indicator）

你的 Pine 里：

```pinescript
ibs = math.round((close - low) / (high - low) * 100)
```

对应 NinjaTrader 指标伪代码：

```csharp
namespace NinjaTrader.NinjaScript.Indicators
{
    public class InternalBarStrength : Indicator
    {
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> IBS { get; private set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name       = "Internal Bar Strength";
                IsOverlay  = false;
                Calculate  = Calculate.OnBarClose;
                AddPlot(Brushes.Blue, "IBS");
            }
            else if (State == State.DataLoaded)
            {
                IBS = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                Value[0] = 0;
                IBS[0]   = 0;
                return;
            }

            double range = High[0] - Low[0];
            if (range == 0)
            {
                Value[0] = 0;
                IBS[0]   = 0;
                return;
            }

            double v = (Close[0] - Low[0]) / range * 100.0;
            v        = Math.Round(v);   // 和 Pine 对齐
            Value[0] = v;
            IBS[0]   = v;
        }
    }
}
```

在策略里可直接用：`var ibs = InternalBarStrength();` 然后 `ibs[0]` 就相当于 Pine 的 `ibs`。

---

### 3. 在策略中实现时段过滤（ETH 18:00–09:30）

Pine 中用 `input.session` + `time()` + `session.islastbar`，  
NinjaTrader 中常用 `Bars[0].Time.TimeOfDay` 自己写判断：

```csharp
private TimeSpan ethStart; // 18:00
private TimeSpan ethEnd;   // 09:30

protected override void OnStateChange()
{
    if (State == State.SetDefaults)
    {
        // 默认参数
        ethStart = new TimeSpan(18, 0, 0);
        ethEnd   = new TimeSpan(9, 30, 0);
        Calculate = Calculate.OnBarClose;
        IsOverlay = true;
    }
}

private bool IsInEthSession()
{
    TimeSpan t = Times[0][0].TimeOfDay;   // 或 Bars.GetTime(0).TimeOfDay

    // 跨天时段：18:00–次日 09:30
    if (ethEnd <= ethStart)
        return t >= ethStart || t <= ethEnd;
    else
        return t >= ethStart && t <= ethEnd;
}

// 近似 Pine 的 session.islastbar（粗略：当前时间刚越过 ethEnd）
private bool IsEthSessionEnd()
{
    TimeSpan t = Times[0][0].TimeOfDay;
    return t >= ethEnd && t < ethEnd.Add(TimeSpan.FromMinutes(BarPeriod.Value)); // 简单近似
}
```

策略中替代：

- `inETH` → `bool inETH = IsInEthSession();`
- `session.islastbar` → `IsEthSessionEnd()`（精度与 SessionTemplate 略有差异，实盘再调）

---

### 4. ATR 过滤与空头趋势 isBearTrend()

Pine：

```pinescript
atr = ta.atr(atrLength)
...
enableAtrFilter ? atr / atr[1] >= minAtrValue : true
```

NinjaTrader：

```csharp
private ATR atr;

// OnStateChange State.Configure:
atr = ATR(atrLength);

// OnBarUpdate 里使用
bool atrOK = !enableAtrFilter || (atr[0] / atr[1] >= minAtrValue);
```

空头趋势函数 Pine：

```pinescript
isBearTrend() =>
    bool flag = true
    for i = 0 to (2 - 1) by 1
        if flag and low[i] < low[i+1]
            flag := true
        else
            flag := false
    flag
```

注意你这里实际上就是 **最近两根 K 线 low 逐根降低**，在 NT 中简化为：

```csharp
private bool IsBearTrend()
{
    if (CurrentBar < 1) return false;
    return Low[0] < Low[1];
}
```

---

### 5. 信号条件：多头/空头 longCondition / shortCondition

Pine 多头条件：

```pinescript
longCondition = inETH and isBullBar and ibs > bullIBSTarget and 
                high - close >= longMinProfitPoint and
                (strategyDirection == '都做' or strategyDirection == '只做多') and
                (enableAtrFilter ? atr / atr[1] >= minAtrValue : true)
```

NT 对应伪代码：

```csharp
bool inETH     = IsInEthSession();
bool isBullBar = Close[0] > Open[0];

bool longCondition =
    inETH &&
    isBullBar &&
    ibs[0] > bullIBSTarget &&
    (High[0] - Close[0]) >= longMinProfitPoint &&
    (strategyDirection == "都做" || strategyDirection == "只做多") &&
    (!enableAtrFilter || atr[0] / atr[1] >= minAtrValue);
```

空头条件同理：

```csharp
bool isBearBar = Close[0] < Open[0];

bool shortCondition =
    inETH &&
    isBearBar &&
    IsBearTrend() &&
    ibs[0] < bearIBSTarget &&
    (Close[0] - Low[0]) >= shortMinProfitPoint &&
    (strategyDirection == "都做" || strategyDirection == "只做空") &&
    (!enableAtrFilter || atr[0] / atr[1] >= minAtrValue);
```

---

### 6. 策略主体 OnBarUpdate 中的仓位、挂单与平仓

Pine 结构：

```pinescript
if strategy.position_size == 0
    strategy.cancel_all()
    if longCondition
        ... 计算limit/stop/qty ...
        strategy.entry(...)
        strategy.exit(... limit=..., stop=...)
    if shortCondition
        ... 类似 ...

if session.islastbar or (not inETH and strategy.position_size != 0)
    strategy.cancel_all()
    strategy.close_all(immediately = true)
```

NinjaTrader 中建议用 **托管订单模式**（Managed Approach）：`EnterLongLimit/EnterShortLimit` + `ExitLongStopLimit/SetStopLoss/SetProfitTarget`。

一个接近实现的结构如下（重点逻辑，不是完整可编译代码，但方向正确）：

```csharp
protected override void OnBarUpdate()
{
    if (CurrentBar < 2) return;  // 需要 atr[1], Low[1] 等

    bool inETH = IsInEthSession();

    // 1. 无仓位 → 寻找新信号
    if (Position.MarketPosition == MarketPosition.Flat)
    {
        // 清掉之前没成交的挂单
        CancelAllOrders();

        bool isBullBar      = Close[0] > Open[0];
        bool isBearBar      = Close[0] < Open[0];
        bool longCondition  = ... // 如上所示
        bool shortCondition = ... // 如上所示

        if (longCondition)
        {
            double limitPrice = High[0] + (High[0] - Low[0]) / 2.0;   // 对应 Pine: (high-low)/2 + high
            double stopPrice  = Low[0];

            // 注意 Pine 的 qty = floor(maxLossValue / (close - low) / syminfo.pointvalue)
            int qty = CalcQty(maxLossValue, Close[0] - Low[0]);

            double rr = (limitPrice - Close[0]) / (Close[0] - stopPrice);
            if (rr >= 0.25)
            {
                EnterLongLimit(qty, limitPrice, "Long");  // 信号名 "Long"
                // 平仓条件可以用 SetStopLoss/SetProfitTarget 或 ExitLongStopLimit
                SetStopLoss("Long", CalculationMode.Price, stopPrice - TickSize, false);
                SetProfitTarget("Long", CalculationMode.Price, limitPrice);
            }
        }
        else if (shortCondition)
        {
            double limitPrice = Low[0] - (High[0] - Low[0]) / 2.0; // 对应 Pine: low - (high-low)/2
            double stopPrice  = High[0];

            int qty = CalcQty(maxLossValue, High[0] - Close[0]);

            double rr = (Close[0] - limitPrice) / (stopPrice - Close[0]);
            if (rr >= 0.25)
            {
                EnterShortLimit(qty, limitPrice, "Short");
                SetStopLoss("Short", CalculationMode.Price, stopPrice + TickSize, false);
                SetProfitTarget("Short", CalculationMode.Price, limitPrice);
            }
        }
    }
    // 2. 有仓位 → 时段结束或出 ETH 就强平
    else
    {
        if (IsEthSessionEnd() || (!inETH && Position.MarketPosition != MarketPosition.Flat))
        {
            CancelAllOrders();       // 类似 strategy.cancel_all()
            ExitLong("ForceExit");   // 或 ExitShort，根据仓位判断
            ExitShort("ForceExit");
        }
    }
}

// 仓位计算函数，模仿 Pine 的 math.floor(maxLossValue / priceRange / pointvalue)
private int CalcQty(double maxLoss, double priceRange)
{
    double pointValue = Instrument.MasterInstrument.PointValue;
    if (priceRange <= 0 || pointValue <= 0) return 0;
    int qty = (int)Math.Floor(maxLoss / priceRange / pointValue);
    return Math.Max(qty, 1);  // 至少 1 手
}
```

---

## 三、注意事项总结（务必留意）

### 1. 时间与时区

- TradingView 的 `input.session("1800-0930")` 以交易所时区为准  
- NinjaTrader 的 `Bars[0].Time` 取决于你 **平台设置的时区** + **交易小时模板**  
- 为了回测对齐：
  - 确认 NinjaTrader 的时区与你在 TradingView 上用的时区一致（多为美东）
  - 或在代码中假定 `Bars[0].Time` 就是美东，手动调试一下 ETH 边界

### 2. 订单管理机制差异

- Pine 的 `strategy.entry/exit` 会帮你自动管理订单状态，策略只有一个“虚拟账户仓位”
- NinjaTrader：
  - 有真实账户仓位 + 策略内部仓位，两者可能会**不同步**
  - 建议一开始只在模拟账户或回测环境中运行，确认逻辑后再上实盘
  - 保证使用统一的信号名，如 `"Long"` 搭配 `SetStopLoss("Long", ...)` / `SetProfitTarget("Long", ...)`

### 3. 指标差异与回测偏差

- ATR：TradingView 默认使用 Wilder 平滑，NinjaTrader 默认是简单移动平均（Simple）。如果你发现 ATR 比例不一致，要调整计算方式以对齐。
- IBS：自己实现时要注意：
  - 除零保护（`high == low` 时怎么处理）
  - 是否要 `round()`，和你 Pine 完全一致

### 4. 回测频率 / OnBarClose vs OnEachTick

- 你的 Pine 策略明显是按 **bar 结束** 的逻辑写的
- 在 NinjaTrader 里必须设置：

  ```csharp
  Calculate = Calculate.OnBarClose;
  ```

  否则 OnBarUpdate 会在每个 tick 都调用，逻辑与 Pine 不符，回测结果会完全不同。

### 5. 强制平仓的「时段最后一根 K」问题

- Pine 有 `session.islastbar` 这种语义很强的条件
- NinjaTrader 没有完全等价的内置函数，只能：
  - 要么用 `Time[0]` 与 ETH 结束时间比较（近似）
  - 要么利用 TradingHours 模板，写更复杂的 session 结束检测逻辑  
- 实践中通常做法：**时间 ≥ 收盘时间 - N 分钟，就禁止开新仓，并强制平现有仓位**

---

## 四、建议的动手顺序

1. **先只做指标对齐**  
   - 写 IBS 指标 + 调用内置 ATR  
   - 在 NinjaTrader 图表上画出来，对比 TradingView 上的值是否完全一致

2. **再实现最小版本策略**（不带仓位大小计算，不带强平，只验证进场条件）  
   - 固定 qty = 1  
   - 只做多 or 只做空，方便对照

3. **最后再加上：**
   - 基于最大单仓亏损的仓位计算
   - ETH 跨天时段 + 强制平仓逻辑
   - 风险回报比过滤

---
