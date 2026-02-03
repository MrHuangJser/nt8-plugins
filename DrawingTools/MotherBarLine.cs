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
    /// Mother Bar Line Drawing Tool - TradingView Style Fibonacci Extension
    /// 母线绘图工具 - TradingView风格斐波那契扩展
    /// </summary>
    public class MotherBarLine : DrawingTool
    {
        #region Variables
        private const int cursorSensitivity = 15;
        private ChartAnchor editingAnchor;

        // Fibonacci levels configuration
        private List<FibLevel> fibLevels;
        #endregion

        #region FibLevel Class
        private class FibLevel
        {
            public double Level { get; set; }
            public Brush Color { get; set; }
            public DashStyle DashStyle { get; set; }
            public double Thickness { get; set; }
            public bool IsExtension { get; set; }

            public FibLevel(double level, Brush color, DashStyle dashStyle = null, double thickness = 1, bool isExtension = false)
            {
                Level = level;
                Color = color;
                DashStyle = dashStyle ?? DashStyles.Dash;
                Thickness = thickness;
                IsExtension = isExtension;
            }
        }
        #endregion

        [Display(Order = 1)]
        public ChartAnchor StartAnchor { get; set; }

        [Display(Order = 2)]
        public ChartAnchor EndAnchor { get; set; }

        [Display(Name = "Label Position", Description = "Display labels on left or right side", Order = 3, GroupName = "Parameters")]
        public LabelPositionType LabelPosition { get; set; }

        public enum LabelPositionType
        {
            Left,
            Right
        }

        public override IEnumerable<ChartAnchor> Anchors
        {
            get { return new[] { StartAnchor, EndAnchor }; }
        }

        public override object Icon
        {
            get
            {
                return null;
            }
        }

        private void InitializeFibLevels()
        {
            // Colors matching TradingView style
            Brush extensionBlue = Brushes.DodgerBlue;
            Brush innerOrange = Brushes.Orange;
            Brush innerYellow = Brushes.Gold;
            Brush negativeBlue = Brushes.DodgerBlue;
            Brush negativeCyan = Brushes.Cyan;

            fibLevels = new List<FibLevel>
            {
                // Positive extensions (above 100%)
                new FibLevel(3.0000, extensionBlue, DashStyles.Dash, 1, true),      // 300.00%
                new FibLevel(2.6180, extensionBlue, DashStyles.Dash, 1, true),      // 261.80%
                new FibLevel(2.2300, extensionBlue, DashStyles.Dash, 1, true),      // 223.00%
                new FibLevel(2.0000, extensionBlue, DashStyles.Dash, 1, true),      // 200.00%
                new FibLevel(1.6180, extensionBlue, DashStyles.Dash, 1, true),      // 161.80%

                // Upper retracement levels
                new FibLevel(1.2300, extensionBlue, DashStyles.Dash, 1, true),      // 123.00%
                new FibLevel(1.1100, innerYellow, DashStyles.Dash, 1, false),       // 111.00%
                new FibLevel(1.0000, innerOrange, DashStyles.Dash, 1.5, false),     // 100.00%
                new FibLevel(0.8900, innerYellow, DashStyles.Dash, 1, false),       // 89.00%
                new FibLevel(0.7900, innerOrange, DashStyles.Dash, 1, false),       // 79.00%
                new FibLevel(0.6600, innerOrange, DashStyles.Dash, 1, false),       // 66.00%
                new FibLevel(0.5000, innerOrange, DashStyles.Dash, 1.5, false),     // 50.00%
                new FibLevel(0.3300, innerOrange, DashStyles.Dash, 1, false),       // 33.00%
                new FibLevel(0.2100, innerOrange, DashStyles.Dash, 1, false),       // 21.00%
                new FibLevel(0.1100, innerYellow, DashStyles.Dash, 1, false),       // 11.00%
                new FibLevel(0.0000, innerOrange, DashStyles.Dash, 1.5, false),     // 0.00%

                // Negative extensions
                new FibLevel(-0.1100, innerYellow, DashStyles.Dash, 1, true),       // -11.00%
                new FibLevel(-0.2300, extensionBlue, DashStyles.Dash, 1, true),     // -23.00%
                new FibLevel(-0.6180, extensionBlue, DashStyles.Dash, 1, true),     // -61.80%
                new FibLevel(-1.0000, extensionBlue, DashStyles.Dash, 1, true),     // -100.00%
                new FibLevel(-1.2300, extensionBlue, DashStyles.Dash, 1, true),     // -123.00%
                new FibLevel(-1.6180, extensionBlue, DashStyles.Dash, 1, true),     // -161.80%
                new FibLevel(-2.0000, negativeCyan, DashStyles.Dash, 1, true),      // -200.00%
            };
        }

        public override bool SupportsAlerts { get { return false; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Mother Bar Line - Extended Fibonacci Tool (TradingView Style)";
                Name = "MotherBarLine";
                DrawingState = DrawingState.Building;
                StartAnchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                EndAnchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                LabelPosition = LabelPositionType.Right;

                InitializeFibLevels();
            }
            else if (State == State.Configure)
            {
                if (fibLevels == null)
                    InitializeFibLevels();
            }
            else if (State == State.Terminated)
            {
            }
        }

        public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
        {
            switch (DrawingState)
            {
                case DrawingState.Building: return Cursors.Pen;
                case DrawingState.Moving: return IsLocked ? Cursors.No : Cursors.SizeAll;
                case DrawingState.Editing: return IsLocked ? Cursors.No : Cursors.SizeNESW;
                default:
                    Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
                    Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

                    if (!IsLocked && (Math.Abs(point.X - startPoint.X) <= cursorSensitivity &&
                        Math.Abs(point.Y - startPoint.Y) <= cursorSensitivity))
                        return Cursors.SizeNESW;
                    if (!IsLocked && (Math.Abs(point.X - endPoint.X) <= cursorSensitivity &&
                        Math.Abs(point.Y - endPoint.Y) <= cursorSensitivity))
                        return Cursors.SizeNESW;

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
            Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
            return new[] { startPoint, endPoint };
        }

        public override bool IsAlertConditionTrue(AlertConditionItem alertConditionItem, Condition condition,
            ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
        {
            return false;
        }

        public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
        {
            return DrawingState == DrawingState.Building ||
                   StartAnchor.Time >= firstTimeOnChart && StartAnchor.Time <= lastTimeOnChart ||
                   EndAnchor.Time >= firstTimeOnChart && EndAnchor.Time <= lastTimeOnChart;
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;

            if (!IsVisible) return;

            double startPrice = StartAnchor.Price;
            double endPrice = EndAnchor.Price;
            double range = startPrice - endPrice;

            foreach (var level in fibLevels)
            {
                double price = endPrice + range * level.Level;
                MinValue = Math.Min(MinValue, price);
                MaxValue = Math.Max(MaxValue, price);
            }
        }

        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            switch (DrawingState)
            {
                case DrawingState.Building:
                    if (StartAnchor.IsEditing)
                    {
                        dataPoint.CopyDataValues(StartAnchor);
                        dataPoint.CopyDataValues(EndAnchor);
                        StartAnchor.IsEditing = false;
                    }
                    else if (EndAnchor.IsEditing)
                    {
                        dataPoint.CopyDataValues(EndAnchor);
                        EndAnchor.IsEditing = false;
                        DrawingState = DrawingState.Normal;
                        IsSelected = false;
                    }
                    break;
                case DrawingState.Normal:
                    Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
                    Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
                    Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

                    if (Math.Abs(point.X - startPoint.X) <= cursorSensitivity &&
                        Math.Abs(point.Y - startPoint.Y) <= cursorSensitivity)
                    {
                        editingAnchor = StartAnchor;
                        DrawingState = DrawingState.Editing;
                    }
                    else if (Math.Abs(point.X - endPoint.X) <= cursorSensitivity &&
                             Math.Abs(point.Y - endPoint.Y) <= cursorSensitivity)
                    {
                        editingAnchor = EndAnchor;
                        DrawingState = DrawingState.Editing;
                    }
                    else
                    {
                        DrawingState = DrawingState.Moving;
                    }
                    break;
            }
        }

        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (IsLocked && DrawingState != DrawingState.Building) return;

            switch (DrawingState)
            {
                case DrawingState.Building:
                    if (EndAnchor.IsEditing)
                        dataPoint.CopyDataValues(EndAnchor);
                    break;
                case DrawingState.Editing:
                    if (editingAnchor != null)
                        dataPoint.CopyDataValues(editingAnchor);
                    break;
                case DrawingState.Moving:
                    foreach (ChartAnchor anchor in Anchors)
                        anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
                    break;
            }
        }

        public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Moving || DrawingState == DrawingState.Editing)
                DrawingState = DrawingState.Normal;
            editingAnchor = null;
        }

        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (fibLevels == null)
                InitializeFibLevels();

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];

            Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

            double startPrice = StartAnchor.Price;
            double endPrice = EndAnchor.Price;
            double range = startPrice - endPrice;

            // Use anchor X positions for line segment (not full width)
            float minX = (float)Math.Min(startPoint.X, endPoint.X);
            float maxX = (float)Math.Max(startPoint.X, endPoint.X);

            // Draw each Fibonacci level
            foreach (var level in fibLevels)
            {
                double price = endPrice + range * level.Level;
                float y = chartScale.GetYByValue(price);

                // Create pen for this level
                SharpDX.Direct2D1.Brush levelBrush = level.Color.ToDxBrush(RenderTarget);
                SharpDX.Direct2D1.StrokeStyle strokeStyle = new SharpDX.Direct2D1.StrokeStyle(
                    Core.Globals.D2DFactory,
                    new SharpDX.Direct2D1.StrokeStyleProperties
                    {
                        DashStyle = SharpDX.Direct2D1.DashStyle.Dash
                    });

                // Draw the horizontal line segment between anchors
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(minX, y),
                    new SharpDX.Vector2(maxX, y),
                    levelBrush,
                    (float)level.Thickness,
                    strokeStyle);

                // Draw the level text
                string levelText = (level.Level * 100).ToString("F2") + "%";

                SharpDX.DirectWrite.TextFormat textFormat = new SharpDX.DirectWrite.TextFormat(
                    Core.Globals.DirectWriteFactory,
                    chartControl.Properties.LabelFont.Family.ToString(),
                    SharpDX.DirectWrite.FontWeight.Normal,
                    SharpDX.DirectWrite.FontStyle.Normal,
                    11);

                SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(
                    Core.Globals.DirectWriteFactory,
                    levelText,
                    textFormat,
                    100,
                    20);

                float textX;
                if (LabelPosition == LabelPositionType.Right)
                    textX = maxX + 5;
                else
                    textX = minX - textLayout.Metrics.Width - 5;

                float textY = y - textLayout.Metrics.Height / 2;

                RenderTarget.DrawTextLayout(
                    new SharpDX.Vector2(textX, textY),
                    textLayout,
                    levelBrush);

                textLayout.Dispose();
                textFormat.Dispose();
                strokeStyle.Dispose();
                levelBrush.Dispose();
            }

            // Draw anchor points
            if (IsSelected)
            {
                SharpDX.Direct2D1.Brush anchorBrush = Brushes.DodgerBlue.ToDxBrush(RenderTarget);
                float anchorRadius = 6;

                // Start anchor (hollow circle)
                RenderTarget.DrawEllipse(
                    new SharpDX.Direct2D1.Ellipse(
                        new SharpDX.Vector2((float)startPoint.X, (float)startPoint.Y),
                        anchorRadius, anchorRadius),
                    anchorBrush,
                    2);

                // End anchor (hollow circle)
                RenderTarget.DrawEllipse(
                    new SharpDX.Direct2D1.Ellipse(
                        new SharpDX.Vector2((float)endPoint.X, (float)endPoint.Y),
                        anchorRadius, anchorRadius),
                    anchorBrush,
                    2);

                anchorBrush.Dispose();
            }
        }
    }
}