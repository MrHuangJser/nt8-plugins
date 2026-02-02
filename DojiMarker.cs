#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Doji标记指标 - 在Doji蜡烛上方或下方显示标记
    /// Doji Marker - Displays markers above/below Doji candles
    /// </summary>
    public class DojiMarker : Indicator
    {
        private double dojiSize = 0.15;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"在Doji蜡烛上方或下方显示✖标记";
                Name = "DojiMarker";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // 用户可调参数
                DojiSizeRatio = 0.15;
                OffsetTicks = 10;  // 默认偏移10个tick
            }
            else if (State == State.Configure)
            {
            }
        }

        protected override void OnBarUpdate()
        {
            // 计算K线实体和影线
            double bodySize = Math.Abs(Open[0] - Close[0]);
            double range = High[0] - Low[0];

            // 避免除以零
            if (range == 0)
                return;

            // 判断是否为Doji: 实体 <= 影线 * DojiSize
            bool isDoji = bodySize <= range * DojiSizeRatio;

            if (!isDoji)
                return;

            // 判断方向: 收盘 > 开盘 为上涨Doji，否则为下跌Doji
            bool isUpDoji = Close[0] > Open[0];
            bool isDownDoji = Close[0] < Open[0];

            // 绘制标记
            if (isUpDoji)
            {
                // 上涨Doji - 标记在K线上方
                Draw.Text(this, "UpDoji" + CurrentBar, "✖", 0, High[0] + TickSize * OffsetTicks, Brushes.Black);
            }
            else if (isDownDoji)
            {
                // 下跌Doji - 标记在K线下方
                Draw.Text(this, "DownDoji" + CurrentBar, "✖", 0, Low[0] - TickSize * OffsetTicks, Brushes.Black);
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(0.01, 1.0)]
        [Display(Name = "Doji Size", Description = "Doji判定比例 (实体/影线)", Order = 1, GroupName = "Parameters")]
        public double DojiSizeRatio
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Offset Ticks", Description = "标记与K线的距离 (Tick数)", Order = 2, GroupName = "Parameters")]
        public int OffsetTicks
        { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private DojiMarker[] cacheDojiMarker;
        public DojiMarker DojiMarker(double dojiSizeRatio)
        {
            return DojiMarker(Input, dojiSizeRatio);
        }

        public DojiMarker DojiMarker(ISeries<double> input, double dojiSizeRatio)
        {
            if (cacheDojiMarker != null)
                for (int idx = 0; idx < cacheDojiMarker.Length; idx++)
                    if (cacheDojiMarker[idx] != null && cacheDojiMarker[idx].DojiSizeRatio == dojiSizeRatio && cacheDojiMarker[idx].EqualsInput(input))
                        return cacheDojiMarker[idx];
            return CacheIndicator<DojiMarker>(new DojiMarker() { DojiSizeRatio = dojiSizeRatio }, input, ref cacheDojiMarker);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.DojiMarker DojiMarker(double dojiSizeRatio)
        {
            return indicator.DojiMarker(Input, dojiSizeRatio);
        }

        public Indicators.DojiMarker DojiMarker(ISeries<double> input, double dojiSizeRatio)
        {
            return indicator.DojiMarker(input, dojiSizeRatio);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.DojiMarker DojiMarker(double dojiSizeRatio)
        {
            return indicator.DojiMarker(Input, dojiSizeRatio);
        }

        public Indicators.DojiMarker DojiMarker(ISeries<double> input, double dojiSizeRatio)
        {
            return indicator.DojiMarker(input, dojiSizeRatio);
        }
    }
}

#endregion
