# NinjaTrader 8 Plugins

NinjaTrader 8 自定义指标和绘图工具集合。

## 项目结构

```
├── Indicators/          # 自定义指标
│   ├── MidPriceLine.cs  # 中点线指标
│   └── DojiMarker.cs    # Doji标记指标
├── DrawingTools/        # 绘图工具
└── NinjaTraderIndicators.csproj  # VSCode 智能感知支持
```

## 安装方法

1. 将 `.cs` 文件复制到 NinjaTrader 对应目录:
   - 指标: `Documents\NinjaTrader 8\bin\Custom\Indicators\`
   - 绘图工具: `Documents\NinjaTrader 8\bin\Custom\DrawingTools\`
2. 在 NinjaTrader 中按 F5 编译

## 开发环境

使用 VSCode + C# Dev Kit 扩展可获得智能感知支持。
