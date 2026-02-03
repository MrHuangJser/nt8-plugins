# NinjaTrader 8 Plugins

NinjaTrader 8 自定义指标和绘图工具集合。

## 项目结构

```
├── Indicators/              # 自定义指标
│   ├── MidPriceLine.cs      # 中点线指标 - 显示每根K线的 (High+Low)/2
│   ├── DojiMarker.cs        # Doji标记指标 - 标记十字星形态，支持自定义颜色和字体
│   └── BarCounter.cs        # K线计数指标 - 显示K线编号
├── DrawingTools/            # 绘图工具
│   ├── MotherBarLine.cs     # 母线工具 - TradingView风格斐波那契扩展线
│   ├── RushMagnet.cs        # 急赴磁体 - 显示0%, 12.5%, 25%, 100%水平线
│   ├── RangeZone.cs         # 区间划线 - 显示0%, 33%, 50%, 66%, 100%水平线
│   ├── MeasureMove.cs       # 测量运动 - 显示-100%到400%扩展水平线
│   └── FiftyPercent.cs      # 50%中点线 - 显示两点之间的50%水平线
└── NinjaTraderIndicators.csproj  # VSCode 智能感知支持
```

## 指标说明

### MidPriceLine
显示每根K线最高价和最低价的中点。

### DojiMarker
识别并标记Doji（十字星）形态：
- 可配置 Doji 判定比例
- 可配置标记偏移距离
- 可分别设置上涨/下跌 Doji 的颜色
- 可自定义标记字体大小

### BarCounter
在K线下方显示编号，方便回测和分析。

## 绘图工具说明

### MotherBarLine
TradingView 风格的扩展斐波那契工具：
- 完整的斐波那契水平线（300% 到 -200%）
- 线段模式（只在两个锚点之间绘制）
- 可配置标签显示位置（左侧/右侧）
- 可配置线宽和线条样式（实线/虚线/点线/点划线）
- 颜色区分：蓝色=扩展区域，橙色/黄色=核心回撤区域

### RushMagnet
急赴磁体划线工具：
- 显示 0%, 12.5%, 25%, 100% 四个水平线
- 可配置线条颜色和线宽
- 可配置标签显示位置

### RangeZone
区间划线工具：
- 显示 0%, 33%, 50%, 66%, 100% 五个水平线
- 可配置线条颜色和线宽
- 可配置标签显示位置

### MeasureMove
测量运动划线工具：
- 显示 -100%, -50%, 0%, 50%, 100%, 150%, 200%, 250%, 300%, 400% 水平线
- 用于测量价格运动的倍数关系
- 可配置线条颜色和线宽

### FiftyPercent
50%中点线工具：
- 显示两个锚点之间的 50% 中点水平线
- 快速标记价格中点位置
- 可配置线条颜色和线宽

## 安装方法

1. 将 `.cs` 文件复制到 NinjaTrader 对应目录:
   - 指标: `Documents\NinjaTrader 8\bin\Custom\Indicators\`
   - 绘图工具: `Documents\NinjaTrader 8\bin\Custom\DrawingTools\`
2. 在 NinjaTrader 中按 F5 编译

## 开发环境

使用 VSCode + C# Dev Kit 扩展可获得智能感知支持。
