# NinjaTrader 8 Plugins

NinjaTrader 8 自定义指标、绘图工具和插件集合。

## 项目结构

```
├── AddOns/                  # 插件 (AddOns)
│   └── GroupTrade/          # 多账户联动交易插件
│       ├── Core/            # 核心引擎 (复制引擎, 风险控制, 手数计算)
│       ├── Models/          # 数据模型
│       ├── Services/        # 服务 (配置管理)
│       └── UI/              # WPF 界面 (纯代码构建)
├── Indicators/              # 自定义指标
│   ├── MidPriceLine.cs      # 中点线指标 - 显示每根K线的 (High+Low)/2
│   ├── DojiMarker.cs        # Doji标记指标 - 标记十字星形态，支持自定义颜色和字体
│   └── BarCounter.cs        # K线计数指标 - 显示K线编号
├── DrawingTools/            # 绘图工具
│   ├── MotherBarLine.cs     # 母线工具 - TradingView风格斐波那契扩展线
│   ├── RushMagnet.cs        # 急赴磁体 - 显示0%, 12.5%, 25%, 100%水平线
│   ├── RangeZone.cs         # 区间划线 - 显示0%, 33%, 50%, 66%, 100%水平线
│   ├── MeasureMove.cs       # 测量运动 - 显示-100%到400%扩展水平线
│   ├── FiftyPercent.cs      # 50%中点线 - 显示两点之间的50%水平线
│   ├── LongPosition.cs      # 多头仓位工具 - 风险回报比计算 (盈亏比)
│   └── ShortPosition.cs     # 空头仓位工具 - 风险回报比计算 (盈亏比)
└── NinjaTraderIndicators.csproj  # VSCode 智能感知支持
```

## 插件说明 (AddOns)

### Group Trade (多账户跟单系统)
一个功能强大的本地多账户交易复制插件，对标 Replikanto。

**核心功能：**
- **本地极速复制**：毫秒级延迟，将主账户订单复制到多个从账户。
- **7种手数模式**：
  - **Exact**: 1:1 精确复制
  - **Equal**: 均分主账户手数
  - **Ratio**: 按固定比例缩放
  - **Net Liquidation**: 按账户净值比例自动计算
  - **Available Money**: 按可用资金比例自动计算
  - **Percentage Change**: 按百分比调整仓位
  - **Pre Allocation**: 使用预设固定手数
- **同步控制**：支持同步止损、止盈、平仓、改单和 OCO 订单组。
- **Stealth Mode (隐身模式)**：隐藏订单标记，降低被 Prop Firm 识别风险。

**高级风控 (Follower Guard)：**
为从账户提供自动保护，触发规则后自动停止复制并平仓：
- **连续亏损保护**：连续亏损 N 次自动停止
- **日亏损限额**：日内亏损超过 $X 自动停止
- **权益回撤保护**：净值回撤超过 N% 自动停止
- **拒单保护**：订单连续被拒 N 次自动停止

## 绘图工具说明

### Long/Short Position (多头/空头仓位工具)
TradingView 风格的风险回报比（盈亏比）计算工具：
- 可拖动调整开仓价、止损价、止盈价
- 实时显示盈亏比 (Risk/Reward Ratio)
- 显示止损/止盈的具体金额和百分比
- 自动计算风险手数（基于账户风险模型）

### MotherBarLine
TradingView 风格的扩展斐波那契工具：
- 完整的斐波那契水平线（300% 到 -200%）
- 线段模式（只在两个锚点之间绘制）
- 可配置标签显示位置（左侧/右侧）
- 颜色区分：蓝色=扩展区域，橙色/黄色=核心回撤区域

### RushMagnet
急赴磁体划线工具：
- 显示 0%, 12.5%, 25%, 100% 四个水平线
- 辅助判断价格运动的关键回撤位

### RangeZone
区间划线工具：
- 显示 0%, 33%, 50%, 66%, 100% 五个水平线
- 用于划分盘整区间或趋势段

### MeasureMove
测量运动划线工具：
- 显示 -100% 到 400% 的关键扩展位
- 用于测量价格运动的倍数关系

### FiftyPercent
50%中点线工具：
- 快速标记两个价格点之间的 50% 位置

## 指标说明

### MidPriceLine
显示每根K线最高价和最低价的中点。

### DojiMarker
识别并标记Doji（十字星）形态，支持自定义判定比例和颜色。

### BarCounter
在K线下方显示编号，方便回测和分析。

## 安装方法

1. 将文件复制到 NinjaTrader 对应目录:
   - 插件: `Documents\NinjaTrader 8\bin\Custom\AddOns\`
   - 指标: `Documents\NinjaTrader 8\bin\Custom\Indicators\`
   - 绘图工具: `Documents\NinjaTrader 8\bin\Custom\DrawingTools\`
2. 在 NinjaTrader 中按 F5 编译 (Tools > New > NinjaScript Editor > F5)

## 开发环境

- .NET Framework 4.8
- VSCode + C# Dev Kit
