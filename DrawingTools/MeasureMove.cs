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
    /// Measure Move Drawing Tool - 测量运动划线工具
    /// Displays horizontal lines at -100%, -50%, 0%, 50%, 100%, 150%, 200%, 250%, 300%, 400% levels
    /// </summary>
    public class MeasureMove : DrawingTool
    {
        #region Variables
        private const int cursorSensitivity = 15;
        private ChartAnchor editingAnchor;

        // Level configuration
        private List<double> moveLevels;
        #endregion

        [Display(Order = 1)]
        public ChartAnchor StartAnchor { get; set; }

        [Display(Order = 2)]
        public ChartAnchor EndAnchor { get; set; }

        [Display(Name = "Line Color", Description = "Color of the level lines", Order = 3, GroupName = "Parameters")]
        public Brush LineColor { get; set; }

        [Browsable(false)]
        public string LineColorSerialize
        {
            get { return Serialize.BrushToString(LineColor); }
            set { LineColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Line Width", Description = "Width of the level lines", Order = 4, GroupName = "Parameters")]
        [Range(1, 10)]
        public int LineWidth { get; set; }

        [Display(Name = "Label Position", Description = "Display labels on left or right side", Order = 5, GroupName = "Parameters")]
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
            get { return null; }
        }

        private void InitializeMoveLevels()
        {
            moveLevels = new List<double>
            {
                -1.0000,    // -100.00%
                -0.5000,    // -50.00%
                 0.0000,    // 0.00%
                 0.5000,    // 50.00%
                 1.0000,    // 100.00%
                 1.5000,    // 150.00%
                 2.0000,    // 200.00%
                 2.5000,    // 250.00%
                 3.0000,    // 300.00%
                 4.0000,    // 400.00%
            };
        }

        public override bool SupportsAlerts { get { return false; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Measure Move - 测量运动划线工具";
                Name = "MeasureMove";
                DrawingState = DrawingState.Building;
                StartAnchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                EndAnchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                LabelPosition = LabelPositionType.Right;
                LineColor = Brushes.Indigo;
                LineWidth = 1;

                InitializeMoveLevels();
            }
            else if (State == State.Configure)
            {
                if (moveLevels == null)
                    InitializeMoveLevels();
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
            double range = endPrice - startPrice;

            foreach (var level in moveLevels)
            {
                double price = endPrice + range * level;
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
            if (moveLevels == null)
                InitializeMoveLevels();

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];

            Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

            double startPrice = StartAnchor.Price;
            double endPrice = EndAnchor.Price;
            double range = endPrice - startPrice;

            // Use anchor X positions for line segment
            float minX = (float)Math.Min(startPoint.X, endPoint.X);
            float maxX = (float)Math.Max(startPoint.X, endPoint.X);

            // Create brush
            SharpDX.Direct2D1.Brush lineBrush = LineColor.ToDxBrush(RenderTarget);

            // Draw each level
            foreach (var level in moveLevels)
            {
                double price = endPrice + range * level;
                float y = chartScale.GetYByValue(price);

                // Draw the horizontal line (solid line)
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(minX, y),
                    new SharpDX.Vector2(maxX, y),
                    lineBrush,
                    (float)LineWidth);

                // Draw the level text
                string levelText = (level * 100).ToString("F2") + "%";

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
                    textX = maxX + 10;
                else
                    textX = minX - textLayout.Metrics.Width - 10;

                float textY = y - textLayout.Metrics.Height / 2;

                RenderTarget.DrawTextLayout(
                    new SharpDX.Vector2(textX, textY),
                    textLayout,
                    lineBrush);

                textLayout.Dispose();
                textFormat.Dispose();
            }

            lineBrush.Dispose();
        }
    }
}
