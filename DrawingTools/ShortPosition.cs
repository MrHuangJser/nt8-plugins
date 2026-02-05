#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
    /// <summary>
    /// ShortPosition - 空头画图工具
    /// 类似 TradingView 的空头工具，支持三点绘制（Entry/Stop/Target）
    /// 自动计算仓位并更新 Chart Trader 手数
    /// </summary>
    public class ShortPosition : DrawingTool
    {
        #region Variables

        private const int cursorSensitivity = 15;
        private ChartAnchor editingAnchor;

        // 品种信息
        private double tickSize;
        private double tickValue;
        private bool instrumentInfoInitialized;

        // Chart Trader QTY 控件
        private QuantityUpDown qtyField;
        private bool qtyFieldSearched;

        // 计算结果缓存
        private double riskTicks;
        private double rewardTicks;
        private double riskDollars;
        private double rewardDollars;
        private double riskRewardRatio;
        private int calculatedQty;
        private int lastUpdatedQty;

        #endregion

        #region Properties - Anchors

        [Display(Order = 1)]
        public ChartAnchor EntryAnchor { get; set; }

        [Display(Order = 2)]
        public ChartAnchor StopAnchor { get; set; }

        [Display(Order = 3)]
        public ChartAnchor TargetAnchor { get; set; }

        public override IEnumerable<ChartAnchor> Anchors
        {
            get { return new[] { EntryAnchor, StopAnchor, TargetAnchor }; }
        }

        #endregion

        #region Properties - User Parameters

        [NinjaScriptProperty]
        [Display(Name = "Fixed Risk ($)", Description = "固定风险金额",
                 GroupName = "1. Risk Settings", Order = 1)]
        public double FixedRiskAmount { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto Update QTY", Description = "自动更新 Chart Trader 手数",
                 GroupName = "1. Risk Settings", Order = 2)]
        public bool AutoUpdateQty { get; set; }

        [Display(Name = "Entry Color", GroupName = "2. Colors", Order = 1)]
        public Brush EntryColor { get; set; }

        [Browsable(false)]
        public string EntryColorSerialize
        {
            get { return Serialize.BrushToString(EntryColor); }
            set { EntryColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Stop Color", GroupName = "2. Colors", Order = 2)]
        public Brush StopColor { get; set; }

        [Browsable(false)]
        public string StopColorSerialize
        {
            get { return Serialize.BrushToString(StopColor); }
            set { StopColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Target Color", GroupName = "2. Colors", Order = 3)]
        public Brush TargetColor { get; set; }

        [Browsable(false)]
        public string TargetColorSerialize
        {
            get { return Serialize.BrushToString(TargetColor); }
            set { TargetColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Zone Opacity (%)", GroupName = "2. Colors", Order = 4)]
        [Range(0, 100)]
        public int ZoneOpacity { get; set; }

        [Display(Name = "Line Width", GroupName = "3. Style", Order = 1)]
        [Range(1, 5)]
        public int LineWidth { get; set; }

        [Display(Name = "Show Info Panel", GroupName = "3. Style", Order = 2)]
        public bool ShowInfoPanel { get; set; }

        [Display(Name = "Font Size", GroupName = "3. Style", Order = 3)]
        [Range(8, 20)]
        public int FontSize { get; set; }

        #endregion

        #region DrawingTool Overrides

        public override object Icon
        {
            get
            {
                // 返回一个简单的向下箭头图标表示空头
                return "▼";
            }
        }

        public override bool SupportsAlerts { get { return false; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"空头画图工具 - 类似 TradingView Short Position Tool";
                Name = "ShortPosition";
                DrawingState = DrawingState.Building;

                // 初始化锚点
                EntryAnchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                StopAnchor = new ChartAnchor { IsEditing = false, DrawingTool = this };
                TargetAnchor = new ChartAnchor { IsEditing = false, DrawingTool = this };

                // 默认参数
                FixedRiskAmount = 200;
                AutoUpdateQty = true;

                // 颜色
                EntryColor = Brushes.DodgerBlue;
                StopColor = Brushes.Red;
                TargetColor = Brushes.LimeGreen;
                ZoneOpacity = 20;

                // 样式
                LineWidth = 2;
                ShowInfoPanel = true;
                FontSize = 11;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.Terminated)
            {
            }
        }

        public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel,
                                          ChartScale chartScale, Point point)
        {
            switch (DrawingState)
            {
                case DrawingState.Building:
                    return Cursors.Pen;
                case DrawingState.Moving:
                    return IsLocked ? Cursors.No : Cursors.SizeAll;
                case DrawingState.Editing:
                    return IsLocked ? Cursors.No : Cursors.SizeNS;
                default:
                    // 检查是否在锚点附近
                    if (!IsLocked)
                    {
                        foreach (ChartAnchor anchor in Anchors)
                        {
                            Point anchorPoint = anchor.GetPoint(chartControl, chartPanel, chartScale);
                            if (Math.Abs(point.Y - anchorPoint.Y) <= cursorSensitivity &&
                                point.X >= Math.Min(EntryAnchor.GetPoint(chartControl, chartPanel, chartScale).X,
                                                    StopAnchor.GetPoint(chartControl, chartPanel, chartScale).X) &&
                                point.X <= Math.Max(EntryAnchor.GetPoint(chartControl, chartPanel, chartScale).X,
                                                    TargetAnchor.GetPoint(chartControl, chartPanel, chartScale).X))
                            {
                                return Cursors.SizeNS;
                            }
                        }
                    }
                    return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
            }
        }

        public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
        {
            yield break;
        }

        public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
        {
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];
            Point entryPoint = EntryAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point stopPoint = StopAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point targetPoint = TargetAnchor.GetPoint(chartControl, chartPanel, chartScale);
            return new[] { entryPoint, stopPoint, targetPoint };
        }

        public override bool IsAlertConditionTrue(AlertConditionItem alertConditionItem,
            Condition condition, ChartAlertValue[] values,
            ChartControl chartControl, ChartScale chartScale)
        {
            return false;
        }

        public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale,
            DateTime firstTimeOnChart, DateTime lastTimeOnChart)
        {
            if (DrawingState == DrawingState.Building)
                return true;

            // 检查任意锚点是否在可见范围内
            foreach (ChartAnchor anchor in Anchors)
            {
                if (anchor.Time >= firstTimeOnChart && anchor.Time <= lastTimeOnChart)
                    return true;
            }
            return false;
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;

            if (!IsVisible)
                return;

            foreach (ChartAnchor anchor in Anchors)
            {
                MinValue = Math.Min(MinValue, anchor.Price);
                MaxValue = Math.Max(MaxValue, anchor.Price);
            }
        }

        #endregion

        #region Mouse Events

        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel,
                                          ChartScale chartScale, ChartAnchor dataPoint)
        {
            switch (DrawingState)
            {
                case DrawingState.Building:
                    if (EntryAnchor.IsEditing)
                    {
                        // 第一次点击 - 设置 Entry
                        dataPoint.CopyDataValues(EntryAnchor);
                        EntryAnchor.IsEditing = false;

                        // 初始化 Stop 和 Target 到同一位置
                        dataPoint.CopyDataValues(StopAnchor);
                        dataPoint.CopyDataValues(TargetAnchor);
                        StopAnchor.IsEditing = true;
                    }
                    else if (StopAnchor.IsEditing)
                    {
                        // 第二次点击 - 设置 Stop
                        dataPoint.CopyDataValues(StopAnchor);
                        StopAnchor.IsEditing = false;
                        TargetAnchor.IsEditing = true;

                        // 空头: 自动设置 Target 为对称位置（2R）
                        double entryPrice = EntryAnchor.Price;
                        double stopPrice = StopAnchor.Price;
                        double risk = stopPrice - entryPrice; // 空头: Stop 在上方
                        TargetAnchor.Price = entryPrice - risk * 2; // 默认 2R，Target 在下方
                    }
                    else if (TargetAnchor.IsEditing)
                    {
                        // 第三次点击 - 设置 Target，完成绘制
                        dataPoint.CopyDataValues(TargetAnchor);
                        TargetAnchor.IsEditing = false;
                        DrawingState = DrawingState.Normal;
                        IsSelected = false;
                    }
                    break;

                case DrawingState.Normal:
                    Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
                    editingAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, point);

                    if (editingAnchor != null)
                    {
                        DrawingState = DrawingState.Editing;
                    }
                    else
                    {
                        DrawingState = DrawingState.Moving;
                    }
                    break;
            }
        }

        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel,
                                          ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (IsLocked && DrawingState != DrawingState.Building)
                return;

            switch (DrawingState)
            {
                case DrawingState.Building:
                    if (StopAnchor.IsEditing)
                    {
                        // 空头: Stop 跟随鼠标，但限制在 Entry 上方
                        double entryPrice = EntryAnchor.Price;
                        double mousePrice = dataPoint.Price;

                        // 空头: Stop 必须在 Entry 上方
                        if (mousePrice > entryPrice)
                        {
                            dataPoint.CopyDataValues(StopAnchor);
                        }
                        else
                        {
                            // 如果鼠标在 Entry 下方，将 Stop 设置为略高于 Entry
                            StopAnchor.Time = dataPoint.Time;
                            StopAnchor.Price = entryPrice + tickSize;
                        }
                    }
                    else if (TargetAnchor.IsEditing)
                    {
                        // 空头: Target 跟随鼠标，但限制在 Entry 下方
                        double entryPrice = EntryAnchor.Price;
                        double mousePrice = dataPoint.Price;

                        // 空头: Target 必须在 Entry 下方
                        if (mousePrice < entryPrice)
                        {
                            dataPoint.CopyDataValues(TargetAnchor);
                        }
                        else
                        {
                            // 如果鼠标在 Entry 上方，将 Target 设置为略低于 Entry
                            TargetAnchor.Time = dataPoint.Time;
                            TargetAnchor.Price = entryPrice - tickSize;
                        }
                    }
                    break;

                case DrawingState.Editing:
                    if (editingAnchor != null)
                    {
                        // 编辑单个锚点，但保持约束
                        if (editingAnchor == StopAnchor)
                        {
                            // 空头: Stop 必须在 Entry 上方
                            if (dataPoint.Price > EntryAnchor.Price)
                                dataPoint.CopyDataValues(editingAnchor);
                        }
                        else if (editingAnchor == TargetAnchor)
                        {
                            // 空头: Target 必须在 Entry 下方
                            if (dataPoint.Price < EntryAnchor.Price)
                                dataPoint.CopyDataValues(editingAnchor);
                        }
                        else
                        {
                            // Entry 可以自由移动，但需要调整 Stop 和 Target 保持相对位置
                            double oldEntry = EntryAnchor.Price;
                            double newEntry = dataPoint.Price;
                            double delta = newEntry - oldEntry;

                            dataPoint.CopyDataValues(EntryAnchor);
                            StopAnchor.Price += delta;
                            TargetAnchor.Price += delta;
                        }
                    }
                    break;

                case DrawingState.Moving:
                    foreach (ChartAnchor anchor in Anchors)
                    {
                        anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint,
                                          chartControl, chartPanel, chartScale, this);
                    }
                    break;
            }
        }

        public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel,
                                        ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Moving || DrawingState == DrawingState.Editing)
            {
                DrawingState = DrawingState.Normal;
            }
            editingAnchor = null;
        }

        private ChartAnchor GetClosestAnchor(ChartControl chartControl, ChartPanel chartPanel,
                                              ChartScale chartScale, Point point)
        {
            foreach (ChartAnchor anchor in Anchors)
            {
                Point anchorPoint = anchor.GetPoint(chartControl, chartPanel, chartScale);
                if (Math.Abs(point.Y - anchorPoint.Y) <= cursorSensitivity)
                {
                    return anchor;
                }
            }
            return null;
        }

        #endregion

        #region Rendering

        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // 初始化品种信息
            if (!instrumentInfoInitialized)
            {
                InitializeInstrumentInfo();
            }

            // 查找 Chart Trader QTY 控件
            if (!qtyFieldSearched && AutoUpdateQty)
            {
                FindChartTraderQtyField(chartControl);
                qtyFieldSearched = true;
            }

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];

            // 获取锚点屏幕位置
            Point entryPoint = EntryAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point stopPoint = StopAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point targetPoint = TargetAnchor.GetPoint(chartControl, chartPanel, chartScale);

            // 计算区域边界
            float minX = (float)Math.Min(Math.Min(entryPoint.X, stopPoint.X), targetPoint.X);
            float maxX = (float)Math.Max(Math.Max(entryPoint.X, stopPoint.X), targetPoint.X);

            // 确保有最小宽度
            if (maxX - minX < 50)
            {
                float center = (minX + maxX) / 2;
                minX = center - 100;
                maxX = center + 100;
            }

            // 计算值
            CalculateValues();

            // 1. 渲染填充区域
            RenderZones(chartScale, minX, maxX, entryPoint, stopPoint, targetPoint);

            // 2. 渲染价格线
            RenderPriceLines(minX, maxX, entryPoint, stopPoint, targetPoint);

            // 3. 渲染价格标签
            RenderPriceLabels(chartControl, maxX, entryPoint, stopPoint, targetPoint);

            // 4. 渲染信息面板
            if (ShowInfoPanel && DrawingState != DrawingState.Building)
            {
                RenderInfoPanel(chartControl, chartPanel, minX, (float)stopPoint.Y);
            }

            // 5. 更新 Chart Trader QTY (实时更新，包括拖拽过程中)
            if (AutoUpdateQty)
            {
                UpdateChartTraderQty(chartControl);
            }
        }

        private void RenderZones(ChartScale chartScale, float minX, float maxX,
                                  Point entryPoint, Point stopPoint, Point targetPoint)
        {
            float entryY = (float)entryPoint.Y;
            float stopY = (float)stopPoint.Y;
            float targetY = (float)targetPoint.Y;

            // Stop Zone (红色) - Entry 到 Stop 之间 (空头: Stop 在上方)
            using (var stopZoneBrush = CreateSemiTransparentBrush(StopColor, ZoneOpacity))
            {
                float top = Math.Min(entryY, stopY);
                float height = Math.Abs(entryY - stopY);
                var stopRect = new SharpDX.RectangleF(minX, top, maxX - minX, height);
                RenderTarget.FillRectangle(stopRect, stopZoneBrush);
            }

            // Target Zone (绿色) - Entry 到 Target 之间 (空头: Target 在下方)
            using (var targetZoneBrush = CreateSemiTransparentBrush(TargetColor, ZoneOpacity))
            {
                float top = Math.Min(entryY, targetY);
                float height = Math.Abs(entryY - targetY);
                var targetRect = new SharpDX.RectangleF(minX, top, maxX - minX, height);
                RenderTarget.FillRectangle(targetRect, targetZoneBrush);
            }
        }

        private void RenderPriceLines(float minX, float maxX,
                                       Point entryPoint, Point stopPoint, Point targetPoint)
        {
            float entryY = (float)entryPoint.Y;
            float stopY = (float)stopPoint.Y;
            float targetY = (float)targetPoint.Y;

            // Entry Line (蓝色)
            using (var entryBrush = EntryColor.ToDxBrush(RenderTarget))
            {
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(minX, entryY),
                    new SharpDX.Vector2(maxX, entryY),
                    entryBrush, LineWidth);
            }

            // Stop Line (红色)
            using (var stopBrush = StopColor.ToDxBrush(RenderTarget))
            {
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(minX, stopY),
                    new SharpDX.Vector2(maxX, stopY),
                    stopBrush, LineWidth);
            }

            // Target Line (绿色)
            using (var targetBrush = TargetColor.ToDxBrush(RenderTarget))
            {
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(minX, targetY),
                    new SharpDX.Vector2(maxX, targetY),
                    targetBrush, LineWidth);
            }
        }

        private void RenderPriceLabels(ChartControl chartControl, float maxX,
                                        Point entryPoint, Point stopPoint, Point targetPoint)
        {
            float labelX = maxX + 5;

            // Entry 标签
            string entryText = $"Entry: {EntryAnchor.Price:F2}  |  Qty: {calculatedQty}";
            RenderLabel(chartControl, labelX, (float)entryPoint.Y, entryText, EntryColor);

            // Stop 标签 (空头: Stop 在上方)
            string stopText = $"Stop: {StopAnchor.Price:F2}  |  -{riskTicks:F0} ticks  |  -${riskDollars:F0}";
            RenderLabel(chartControl, labelX, (float)stopPoint.Y, stopText, StopColor);

            // Target 标签 (空头: Target 在下方)
            string targetText = $"Target: {TargetAnchor.Price:F2}  |  +{rewardTicks:F0} ticks  |  +${rewardDollars:F0}  |  {riskRewardRatio:F1}R";
            RenderLabel(chartControl, labelX, (float)targetPoint.Y, targetText, TargetColor);
        }

        private void RenderLabel(ChartControl chartControl, float x, float y, string text, Brush color)
        {
            using (var textFormat = new SharpDX.DirectWrite.TextFormat(
                Core.Globals.DirectWriteFactory,
                chartControl.Properties.LabelFont.Family.ToString(),
                SharpDX.DirectWrite.FontWeight.Normal,
                SharpDX.DirectWrite.FontStyle.Normal,
                FontSize))
            {
                using (var textLayout = new SharpDX.DirectWrite.TextLayout(
                    Core.Globals.DirectWriteFactory,
                    text,
                    textFormat,
                    500,
                    20))
                {
                    float textY = y - textLayout.Metrics.Height / 2;

                    using (var brush = color.ToDxBrush(RenderTarget))
                    {
                        RenderTarget.DrawTextLayout(
                            new SharpDX.Vector2(x, textY),
                            textLayout,
                            brush);
                    }
                }
            }
        }

        private void RenderInfoPanel(ChartControl chartControl, ChartPanel chartPanel,
                                      float x, float y)
        {
            string[] lines = new string[]
            {
                "═══ SHORT ═══",
                $"Risk:   ${riskDollars:F0}",
                $"Reward: ${rewardDollars:F0}",
                $"R:R:    1:{riskRewardRatio:F1}",
                $"Qty:    {calculatedQty}"
            };

            float lineHeight = FontSize + 4;
            float panelHeight = lines.Length * lineHeight + 10;
            float panelWidth = 120;

            // 背景
            using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                new SharpDX.Color(0, 0, 0, 180)))
            {
                var rect = new SharpDX.RectangleF(x, y - panelHeight - 10, panelWidth, panelHeight);
                RenderTarget.FillRectangle(rect, bgBrush);
            }

            // 边框
            using (var borderBrush = EntryColor.ToDxBrush(RenderTarget))
            {
                var rect = new SharpDX.RectangleF(x, y - panelHeight - 10, panelWidth, panelHeight);
                RenderTarget.DrawRectangle(rect, borderBrush, 1);
            }

            // 文字
            using (var textFormat = new SharpDX.DirectWrite.TextFormat(
                Core.Globals.DirectWriteFactory,
                "Consolas",
                SharpDX.DirectWrite.FontWeight.Normal,
                SharpDX.DirectWrite.FontStyle.Normal,
                FontSize))
            {
                using (var textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    SharpDX.Color.White))
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        using (var textLayout = new SharpDX.DirectWrite.TextLayout(
                            Core.Globals.DirectWriteFactory,
                            lines[i],
                            textFormat,
                            panelWidth - 10,
                            lineHeight))
                        {
                            RenderTarget.DrawTextLayout(
                                new SharpDX.Vector2(x + 5, y - panelHeight - 5 + i * lineHeight),
                                textLayout,
                                textBrush);
                        }
                    }
                }
            }
        }

        private SharpDX.Direct2D1.SolidColorBrush CreateSemiTransparentBrush(Brush wpfBrush, int opacity)
        {
            var solidBrush = wpfBrush as SolidColorBrush;
            if (solidBrush == null)
                solidBrush = new SolidColorBrush(Colors.Gray);

            var color = solidBrush.Color;
            var dxColor = new SharpDX.Color(color.R, color.G, color.B, (byte)(255 * opacity / 100));
            return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor);
        }

        #endregion

        #region Calculations

        private void InitializeInstrumentInfo()
        {
            if (AttachedTo?.Instrument?.MasterInstrument == null)
                return;

            tickSize = AttachedTo.Instrument.MasterInstrument.TickSize;
            double pointValue = AttachedTo.Instrument.MasterInstrument.PointValue;
            tickValue = pointValue * tickSize;
            instrumentInfoInitialized = true;
        }

        private void CalculateValues()
        {
            double entryPrice = EntryAnchor.Price;
            double stopPrice = StopAnchor.Price;
            double targetPrice = TargetAnchor.Price;

            // 风险距离 (空头: Stop 在上方)
            double riskDistance = Math.Abs(stopPrice - entryPrice);
            riskTicks = tickSize > 0 ? riskDistance / tickSize : 0;

            // 回报距离 (空头: Target 在下方)
            double rewardDistance = Math.Abs(entryPrice - targetPrice);
            rewardTicks = tickSize > 0 ? rewardDistance / tickSize : 0;

            // R:R 比例
            riskRewardRatio = riskTicks > 0 ? rewardTicks / riskTicks : 0;

            // 风险金额
            riskDollars = riskTicks * tickValue;

            // 回报金额
            rewardDollars = rewardTicks * tickValue;

            // 计算仓位
            if (riskDollars > 0 && FixedRiskAmount > 0)
            {
                calculatedQty = (int)Math.Floor(FixedRiskAmount / riskDollars);
                if (calculatedQty < 1)
                    calculatedQty = 1;

                // 重新计算实际风险和回报金额（基于实际手数）
                riskDollars = riskTicks * tickValue * calculatedQty;
                rewardDollars = rewardTicks * tickValue * calculatedQty;
            }
            else
            {
                calculatedQty = 1;
            }
        }

        #endregion

        #region Chart Trader Integration

        private void FindChartTraderQtyField(ChartControl chartControl)
        {
            if (chartControl == null)
                return;

            chartControl.Dispatcher.InvokeAsync((Action)(() =>
            {
                try
                {
                    var window = Window.GetWindow(chartControl.Parent);
                    if (window != null)
                    {
                        qtyField = window.FindFirst("ChartTraderControlQuantitySelector") as QuantityUpDown;
                    }
                }
                catch (Exception)
                {
                    // 忽略错误
                }
            }));
        }

        private void UpdateChartTraderQty(ChartControl chartControl)
        {
            if (qtyField == null || !AutoUpdateQty)
                return;

            if (calculatedQty != lastUpdatedQty && calculatedQty > 0)
            {
                chartControl.Dispatcher.InvokeAsync((Action)(() =>
                {
                    try
                    {
                        qtyField.Value = calculatedQty;
                        lastUpdatedQty = calculatedQty;
                    }
                    catch (Exception)
                    {
                        // 忽略错误
                    }
                }));
            }
        }

        #endregion
    }
}
