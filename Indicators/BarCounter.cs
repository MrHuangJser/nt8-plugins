#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    public class BarCounter : Indicator
    {
        private int _barCntr = 0;
        private Brush _textColorDefinedbyUser = Brushes.Orange;
        private int _pixelsAboveBelowBar = 50;
        private bool _textIsBelowBars = true;
        private int _fontSize = 14;
        private int _everyNBars = 2;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Labels Bars by number from session start, showing every Nth bar";
                Name = "BarCounter";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                TextColor = Brushes.Orange;
                EveryNBars = 2;
                TextFontSize = 14;
                PixelsAboveBelow = 50;
                TextIsBelowBars = true;
            }
            else if (State == State.Configure)
            {
            }
        }

        protected override void OnBarUpdate()
        {
            double _textYStartingPoint = High[0];
            int pixelOffset = _pixelsAboveBelowBar;

            if (_textIsBelowBars)
            {
                _textYStartingPoint = Low[0];
                pixelOffset = -Math.Abs(_pixelsAboveBelowBar);
            }

            NinjaTrader.Gui.Tools.SimpleFont myFont = new NinjaTrader.Gui.Tools.SimpleFont("Courier New", 14) { Size = _fontSize, Bold = false };

            if (Bars.IsFirstBarOfSession)
            {
                _barCntr = 1;
            }
            else
            {
                _barCntr++;
            }

            // Show label every N bars
            if (_barCntr % _everyNBars == 0)
            {
                Draw.Text(this, CurrentBar.ToString(), true, _barCntr.ToString(), 0, _textYStartingPoint, pixelOffset, _textColorDefinedbyUser, myFont, TextAlignment.Center, null, null, 1);
            }
        }

        #region Properties
        [Range(1, 100)]
        [NinjaScriptProperty]
        [Display(Name = "EveryNBars", Description = "Show label every N bars", Order = 1, GroupName = "Visual")]
        public int EveryNBars
        {
            get { return _everyNBars; }
            set { _everyNBars = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "TextColor", Description = "Text Color", Order = 2, GroupName = "Visual")]
        public Brush TextColor
        {
            get { return _textColorDefinedbyUser; }
            set { _textColorDefinedbyUser = value; }
        }

        [Range(4, 40)]
        [NinjaScriptProperty]
        [Display(Name = "TextFontSize", Description = "Text Font Size", Order = 3, GroupName = "Visual")]
        public int TextFontSize
        {
            get { return _fontSize; }
            set { _fontSize = value; }
        }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        [Range(0, 2000)]
        [NinjaScriptProperty]
        [Display(Name = "PixelsAboveBelow", Description = "Text Offset in Pixels from Bar", Order = 4, GroupName = "Visual")]
        public int PixelsAboveBelow
        {
            get { return _pixelsAboveBelowBar; }
            set { _pixelsAboveBelowBar = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "TextIsBelowBars", Description = "Display Text Below Bars", Order = 5, GroupName = "Visual")]
        public bool TextIsBelowBars
        {
            get { return _textIsBelowBars; }
            set { _textIsBelowBars = value; }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BarCounter[] cacheBarCounter;
		public BarCounter BarCounter(int everyNBars, Brush textColor, int textFontSize, int pixelsAboveBelow, bool textIsBelowBars)
		{
			return BarCounter(Input, everyNBars, textColor, textFontSize, pixelsAboveBelow, textIsBelowBars);
		}

		public BarCounter BarCounter(ISeries<double> input, int everyNBars, Brush textColor, int textFontSize, int pixelsAboveBelow, bool textIsBelowBars)
		{
			if (cacheBarCounter != null)
				for (int idx = 0; idx < cacheBarCounter.Length; idx++)
					if (cacheBarCounter[idx] != null && cacheBarCounter[idx].EveryNBars == everyNBars && cacheBarCounter[idx].TextColor == textColor && cacheBarCounter[idx].TextFontSize == textFontSize && cacheBarCounter[idx].PixelsAboveBelow == pixelsAboveBelow && cacheBarCounter[idx].TextIsBelowBars == textIsBelowBars && cacheBarCounter[idx].EqualsInput(input))
						return cacheBarCounter[idx];
			return CacheIndicator<BarCounter>(new BarCounter(){ EveryNBars = everyNBars, TextColor = textColor, TextFontSize = textFontSize, PixelsAboveBelow = pixelsAboveBelow, TextIsBelowBars = textIsBelowBars }, input, ref cacheBarCounter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BarCounter BarCounter(int everyNBars, Brush textColor, int textFontSize, int pixelsAboveBelow, bool textIsBelowBars)
		{
			return indicator.BarCounter(Input, everyNBars, textColor, textFontSize, pixelsAboveBelow, textIsBelowBars);
		}

		public Indicators.BarCounter BarCounter(ISeries<double> input , int everyNBars, Brush textColor, int textFontSize, int pixelsAboveBelow, bool textIsBelowBars)
		{
			return indicator.BarCounter(input, everyNBars, textColor, textFontSize, pixelsAboveBelow, textIsBelowBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BarCounter BarCounter(int everyNBars, Brush textColor, int textFontSize, int pixelsAboveBelow, bool textIsBelowBars)
		{
			return indicator.BarCounter(Input, everyNBars, textColor, textFontSize, pixelsAboveBelow, textIsBelowBars);
		}

		public Indicators.BarCounter BarCounter(ISeries<double> input , int everyNBars, Brush textColor, int textFontSize, int pixelsAboveBelow, bool textIsBelowBars)
		{
			return indicator.BarCounter(input, everyNBars, textColor, textFontSize, pixelsAboveBelow, textIsBelowBars);
		}
	}
}

#endregion
