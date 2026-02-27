#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum StrategyDirectionType
	{
		[Display(Name = "都做")]
		Both,
		[Display(Name = "只做多")]
		LongOnly,
		[Display(Name = "只做空")]
		ShortOnly
	}

	public class SingleBarFastMaget : Strategy
	{
		private ATR _atr;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"单根K线快速磁吸策略 - 基于IBS和ATR的ETH时段交易策略";
				Name						= "SingleBarFastMaget";
				Calculate					= Calculate.OnBarClose;
				EntriesPerDirection			= 1;
				EntryHandling				= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds	= 30;
				IsFillLimitOnTouch			= false;
				TraceOrders					= false;
				BarsRequiredToTrade			= 20;
				IsInstantiatedOnEachOptimizationIteration = true;

				// 策略方向
				StrategyDirection			= StrategyDirectionType.Both;

				// 时段配置 (HHMMSS格式，默认18:00-09:30，注意需与图表时区一致)
				EthStartTime				= 180000;
				EthEndTime					= 093000;

				// 仓位管理
				MaxLossValue				= 100;
				LongMinProfitPoint			= 1.0;
				ShortMinProfitPoint			= 1.0;

				// 入场配置
				BullIBSTarget				= 70;
				BearIBSTarget				= 30;
				EnableAtrFilter				= true;
				AtrLength					= 5;
				MinAtrValue					= 1.3;
			}
			else if (State == State.DataLoaded)
			{
				_atr = ATR(AtrLength);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Math.Max(AtrLength + 1, 3))
				return;

			// 跳过历史数据，仅在实时K线上执行交易逻辑
			if (State == State.Historical)
				return;

			// 时段判断 (支持跨午夜时段如 180000-093000)
			int currentTime = ToTime(Time[0]);
			bool inETH;
			if (EthStartTime > EthEndTime)
				inETH = currentTime >= EthStartTime || currentTime <= EthEndTime;
			else
				inETH = currentTime >= EthStartTime && currentTime <= EthEndTime;

			// ETH时段外有持仓则强制平仓
			if (!inETH && Position.MarketPosition != MarketPosition.Flat)
			{
				Print(string.Format("[{0}] Bar#{1} | ETH时段外持仓强制平仓 | 持仓方向={2} 数量={3}",
					Time[0], CurrentBar, Position.MarketPosition, Position.Quantity));
				if (Position.MarketPosition == MarketPosition.Long)
					ExitLong("ETH Close", "Long");
				else if (Position.MarketPosition == MarketPosition.Short)
					ExitShort("ETH Close", "Short");
				return;
			}

			// 不在ETH时段内，不寻找入场机会
			if (!inETH)
				return;

			// 基本K线判断
			bool isBullBar = Close[0] > Open[0];
			bool isBearBar = Close[0] < Open[0];
			double barRange = High[0] - Low[0];

			// 避免除零 (十字星)
			if (barRange <= 0)
			{
				Print(string.Format("[{0}] Bar#{1} | 十字星(range=0)，跳过", Time[0], CurrentBar));
				return;
			}

			// IBS (Internal Bar Strength) = (收盘价 - 最低价) / (最高价 - 最低价) * 100
			double ibs = Math.Round((Close[0] - Low[0]) / barRange * 100);

			// ATR过滤条件: 当前ATR / 前一根ATR >= 最小增幅
			double atrCurrent  = _atr[0];
			double atrPrevious = _atr[1];
			double atrRatio    = atrPrevious > 0 ? atrCurrent / atrPrevious : 0;
			bool atrCondition  = !EnableAtrFilter || (atrPrevious > 0 && atrRatio >= MinAtrValue);

			// 做多条件逐项判断
			bool longDirOk    = StrategyDirection == StrategyDirectionType.Both || StrategyDirection == StrategyDirectionType.LongOnly;
			double longProfit = High[0] - Close[0];
			bool longCondition = isBullBar && ibs > BullIBSTarget && longProfit >= LongMinProfitPoint && longDirOk && atrCondition;

			// 做空条件逐项判断
			bool shortDirOk    = StrategyDirection == StrategyDirectionType.Both || StrategyDirection == StrategyDirectionType.ShortOnly;
			double shortProfit = Close[0] - Low[0];
			bool shortCondition = isBearBar && ibs < BearIBSTarget && shortProfit >= ShortMinProfitPoint && shortDirOk && atrCondition;

			// 每根K线输出基础判断信息
			Print(string.Format("[{0}] Bar#{1} | O={2} H={3} L={4} C={5} | Range={6:F2} IBS={7} | ATR={8:F4} ATR[1]={9:F4} Ratio={10:F2} AtrOK={11} | 阳线={12} 阴线={13} | 持仓={14}",
				Time[0], CurrentBar, Open[0], High[0], Low[0], Close[0],
				barRange, ibs, atrCurrent, atrPrevious, atrRatio, atrCondition,
				isBullBar, isBearBar, Position.MarketPosition));

			if (longCondition || shortCondition)
				Print(string.Format("  -> 信号: Long={0}(阳线={1} IBS{2}>{3} 利润空间{4:F2}>={5} 方向OK={6} ATR OK={7}) | Short={8}(阴线={9} IBS{10}<{11} 利润空间{12:F2}>={13} 方向OK={14} ATR OK={15})",
					longCondition, isBullBar, ibs, BullIBSTarget, longProfit, LongMinProfitPoint, longDirOk, atrCondition,
					shortCondition, isBearBar, ibs, BearIBSTarget, shortProfit, ShortMinProfitPoint, shortDirOk, atrCondition));

			// 仅在空仓时寻找入场机会
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				if (longCondition || shortCondition)
					Print(string.Format("  -> 有信号但当前持仓({0})，跳过入场", Position.MarketPosition));
				return;
			}

			if (longCondition)
			{
				double limitPrice = barRange / 2.0 + High[0];
				double stopPrice  = Low[0];
				double risk       = Close[0] - Low[0];

				if (risk > 0)
				{
					int qty = (int)Math.Floor(MaxLossValue / risk / Instrument.MasterInstrument.PointValue);
					if (qty < 1)
					{
						Print(string.Format("  -> 多头: 计算qty=0(风险={0:F2} 点值={1})，风险超限跳过",
							risk, Instrument.MasterInstrument.PointValue));
						return;
					}

					double rewardRiskRatio = (limitPrice - Close[0]) / (Close[0] - stopPrice);
					Print(string.Format("  -> 多头计算: 目标={0:F2} 止损={1:F2} 风险={2:F2} 盈亏比={3:F2} 数量={4}",
						limitPrice, stopPrice - TickSize, risk, rewardRiskRatio, qty));

					if (rewardRiskRatio >= 0.25)
					{
						Print(string.Format("  -> [入场] 做多 qty={0} limit={1:F2} stop={2:F2}", qty, limitPrice, stopPrice - TickSize));
						SetProfitTarget("Long", CalculationMode.Price, limitPrice);
						SetStopLoss("Long", CalculationMode.Price, stopPrice - TickSize, false);
						EnterLong(qty, "Long");
					}
					else
					{
						Print(string.Format("  -> 多头: 盈亏比{0:F2}<0.25，放弃入场", rewardRiskRatio));
					}
				}
			}

			if (shortCondition)
			{
				double limitPrice = Low[0] - barRange / 2.0;
				double stopPrice  = High[0];
				double risk       = High[0] - Close[0];

				if (risk > 0)
				{
					int qty = (int)Math.Floor(MaxLossValue / risk / Instrument.MasterInstrument.PointValue);
					if (qty < 1)
					{
						Print(string.Format("  -> 空头: 计算qty=0(风险={0:F2} 点值={1})，风险超限跳过",
							risk, Instrument.MasterInstrument.PointValue));
						return;
					}

					double rewardRiskRatio = (Close[0] - limitPrice) / (stopPrice - Close[0]);
					Print(string.Format("  -> 空头计算: 目标={0:F2} 止损={1:F2} 风险={2:F2} 盈亏比={3:F2} 数量={4}",
						limitPrice, stopPrice + TickSize, risk, rewardRiskRatio, qty));

					if (rewardRiskRatio >= 0.25)
					{
						Print(string.Format("  -> [入场] 做空 qty={0} limit={1:F2} stop={2:F2}", qty, limitPrice, stopPrice + TickSize));
						SetProfitTarget("Short", CalculationMode.Price, limitPrice);
						SetStopLoss("Short", CalculationMode.Price, stopPrice + TickSize, false);
						EnterShort(qty, "Short");
					}
					else
					{
						Print(string.Format("  -> 空头: 盈亏比{0:F2}<0.25，放弃入场", rewardRiskRatio));
					}
				}
			}
		}

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "策略方向", Description = "选择做多、做空或双向交易", Order = 1, GroupName = "测试配置")]
		public StrategyDirectionType StrategyDirection { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "ETH开始时间", Description = "ETH时段开始时间 (HHMMSS格式，如180000=18:00)，需与图表时区一致", Order = 1, GroupName = "时段配置")]
		public int EthStartTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "ETH结束时间", Description = "ETH时段结束时间 (HHMMSS格式，如093000=09:30)，需与图表时区一致", Order = 2, GroupName = "时段配置")]
		public int EthEndTime { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "最大单仓亏损", Description = "单笔交易最大亏损金额(美元)", Order = 1, GroupName = "仓位管理")]
		public int MaxLossValue { get; set; }

		[NinjaScriptProperty]
		[Range(0.25, double.MaxValue)]
		[Display(Name = "多头最小利润点数", Description = "多头入场要求的最小利润空间(价格点数)", Order = 2, GroupName = "仓位管理")]
		public double LongMinProfitPoint { get; set; }

		[NinjaScriptProperty]
		[Range(0.25, double.MaxValue)]
		[Display(Name = "空头最小利润点数", Description = "空头入场要求的最小利润空间(价格点数)", Order = 3, GroupName = "仓位管理")]
		public double ShortMinProfitPoint { get; set; }

		[NinjaScriptProperty]
		[Range(1, 99)]
		[Display(Name = "多头信号最低IBS值", Description = "多头入场要求IBS必须大于此值", Order = 1, GroupName = "入场配置")]
		public int BullIBSTarget { get; set; }

		[NinjaScriptProperty]
		[Range(1, 99)]
		[Display(Name = "空头信号最大IBS值", Description = "空头入场要求IBS必须小于此值", Order = 2, GroupName = "入场配置")]
		public int BearIBSTarget { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "开启ATR过滤", Description = "是否启用ATR增幅过滤条件", Order = 3, GroupName = "入场配置")]
		public bool EnableAtrFilter { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ATR长度", Description = "ATR指标计算周期", Order = 4, GroupName = "入场配置")]
		public int AtrLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, double.MaxValue)]
		[Display(Name = "最小ATR增幅", Description = "当前ATR / 前一根ATR 的最小比值", Order = 5, GroupName = "入场配置")]
		public double MinAtrValue { get; set; }

		#endregion
	}
}
