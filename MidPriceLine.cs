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
    /// 中点线指标 - 显示每根K线的最高价和最低价的中点
    /// Mid Price Line - Displays the midpoint of High and Low for each bar
    /// </summary>
    public class MidPriceLine : Indicator
    {
        #region Variables
        private Brush plotBrush;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"显示每根K线的中点价格 (High + Low) / 2";
                Name = "MidPriceLine";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;  // 叠加在主图上
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // 添加绘图线
                AddPlot(new Stroke(Brushes.Black, DashStyleHelper.Solid, 2), PlotStyle.Cross, "MidPrice");
            }
            else if (State == State.Configure)
            {
                // 配置阶段
            }
        }

        protected override void OnBarUpdate()
        {
            // 计算中点价格: (最高价 + 最低价) / 2
            double midPrice = (High[0] + Low[0]) / 2.0;

            // 设置绘图值
            MidPrice[0] = midPrice;
        }

        #region Properties
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> MidPrice
        {
            get { return Values[0]; }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MidPriceLine[] cacheMidPriceLine;
		public MidPriceLine MidPriceLine()
		{
			return MidPriceLine(Input);
		}

		public MidPriceLine MidPriceLine(ISeries<double> input)
		{
			if (cacheMidPriceLine != null)
				for (int idx = 0; idx < cacheMidPriceLine.Length; idx++)
					if (cacheMidPriceLine[idx] != null && cacheMidPriceLine[idx].EqualsInput(input))
						return cacheMidPriceLine[idx];
			return CacheIndicator<MidPriceLine>(new MidPriceLine(), input, ref cacheMidPriceLine);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MidPriceLine MidPriceLine()
		{
			return indicator.MidPriceLine(Input);
		}

		public Indicators.MidPriceLine MidPriceLine(ISeries<double> input)
		{
			return indicator.MidPriceLine(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MidPriceLine MidPriceLine()
		{
			return indicator.MidPriceLine(Input);
		}

		public Indicators.MidPriceLine MidPriceLine(ISeries<double> input)
		{
			return indicator.MidPriceLine(input);
		}
	}
}

#endregion
