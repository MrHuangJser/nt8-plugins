#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum ETHDirection
	{
		[Display(Name = "做多")]
		Long,
		[Display(Name = "做空")]
		Short
	}

	public class ETHSessionMES : Strategy
	{
		#region Private State

		private bool   _initialized;
		private DateTime _nextEntryTime;
		private DateTime _nextUSOpenTime;
		private DateTime _nextUSCloseTime;
		private bool   _usOpenChecked;
		private bool   _hasAddedOn;
		private bool   _breakEvenMode;
		private int    _breakEvenCount;
		private double _entry1FillPrice;
		private Order  _entry1Order;
		private Order  _entry2Order;

		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"ETH时段MES日内交易策略 - 美盘开盘浮亏加仓";
				Name						= "ETHSessionMES";
				Calculate					= Calculate.OnPriceChange;
				EntriesPerDirection			= 2;
				EntryHandling				= EntryHandling.UniqueEntries;
				IsExitOnSessionCloseStrategy = false;
				IsFillLimitOnTouch			= false;
				TraceOrders					= true;
				BarsRequiredToTrade			= 1;
				IsInstantiatedOnEachOptimizationIteration = true;
				RealtimeErrorHandling		= RealtimeErrorHandling.IgnoreAllErrors;

				// 策略参数
				TradeDirection		= ETHDirection.Long;
				EntryTimeUTC8		= 80000;		// 08:00:00 UTC+8
				InitialQty			= 1;
				AddOnQty			= 1;
				AddOnThreshold		= 0;
				BreakEvenThreshold	= 0;
				TakeProfitDollars	= 150;
				StopLossDollars		= 2000;

				// 时段参数
				USOpenTimeUTC8		= 213000;		// 21:30:00 UTC+8 (夏令时)
				USCloseTimeUTC8		= 40000;		// 04:00:00 UTC+8 次日 (夏令时)
				UtcOffsetHours		= 8;

				// 回测
				EnableBacktest		= true;
			}
			else if (State == State.DataLoaded)
			{
				_initialized = false;
				_breakEvenCount = 0;
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
				return;

			if (!EnableBacktest && State == State.Historical)
				return;

			if (!IsFirstTickOfBar)
				return;

			// 首次初始化：计算第一个入场时间
			if (!_initialized)
			{
				_initialized = true;
				_nextEntryTime = ToNextExchangeTime(Time[0].AddSeconds(-1), EntryTimeUTC8);
				Print(string.Format("[{0}] 策略初始化 | 下次入场={1}", Time[0], _nextEntryTime));
			}

			// === 入场检查 ===
			if (Position.MarketPosition == MarketPosition.Flat && Time[0] >= _nextEntryTime)
			{
				PlaceEntry();
			}

			// === 美盘开盘：加仓判断 ===
			if (Position.MarketPosition != MarketPosition.Flat
				&& !_usOpenChecked
				&& Time[0] >= _nextUSOpenTime)
			{
				_usOpenChecked = true;
				CheckAddOn();
			}

			// === 美盘收盘：强制平仓 ===
			if (Position.MarketPosition != MarketPosition.Flat
				&& Time[0] >= _nextUSCloseTime)
			{
				ForceClose();
			}

			// === 图表统计 ===
			Draw.TextFixed(this, "BEStats",
				string.Format("打平次数: {0}", _breakEvenCount),
				TextPosition.TopRight);
		}

		#region Entry

		private void PlaceEntry()
		{
			// MES: TickSize=0.25, PointValue=5, 每tick价值=$1.25
			double tickValue = TickSize * Instrument.MasterInstrument.PointValue;
			double tpTicks   = TakeProfitDollars / (InitialQty * tickValue);
			double slTicks   = StopLossDollars / (InitialQty * tickValue);

			string entryName = TradeDirection == ETHDirection.Long ? "ETH_Long" : "ETH_Short";

			SetProfitTarget(entryName, CalculationMode.Ticks, tpTicks);
			SetStopLoss(entryName, CalculationMode.Ticks, slTicks, false);

			if (TradeDirection == ETHDirection.Long)
				_entry1Order = EnterLong(InitialQty, entryName);
			else
				_entry1Order = EnterShort(InitialQty, entryName);

			// 计算美盘时间节点
			_nextUSOpenTime  = ToNextExchangeTime(Time[0], USOpenTimeUTC8);
			_nextUSCloseTime = ToNextExchangeTime(_nextUSOpenTime.AddSeconds(-1), USCloseTimeUTC8);
			_usOpenChecked   = false;
			_hasAddedOn      = false;

			Print(string.Format("[{0}] Bar#{1} | 开仓 {2} {3}手 | TP=${4} SL=${5} | USOpen={6} USClose={7}",
				Time[0], CurrentBar, TradeDirection, InitialQty,
				TakeProfitDollars, StopLossDollars,
				_nextUSOpenTime, _nextUSCloseTime));
		}

		#endregion

		#region Add-On at US Open

		private void CheckAddOn()
		{
			double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

			Print(string.Format("[{0}] Bar#{1} | 美盘开盘 | 浮动盈亏=${2:F2}",
				Time[0], CurrentBar, unrealizedPnL));

			if (unrealizedPnL >= 0 || Math.Abs(unrealizedPnL) < AddOnThreshold)
			{
				Print(string.Format("[{0}] Bar#{1} | 浮盈/持平或浮亏${2:F2}未达门槛${3} → 不加仓",
					Time[0], CurrentBar, Math.Abs(unrealizedPnL), AddOnThreshold));
				return;
			}

			// === 浮亏 → 加仓 ===
			_hasAddedOn = true;

			// 判断打平模式：浮亏超过 BreakEvenThreshold 时 TP=均价
			_breakEvenMode = BreakEvenThreshold > 0 && Math.Abs(unrealizedPnL) >= BreakEvenThreshold;

			// 以当前价格估算加仓均价，计算新TP/SL价位
			double estAddOnPrice = Close[0];
			double avgPrice      = (_entry1FillPrice + estAddOnPrice) / 2.0;
			int    totalQty      = InitialQty + AddOnQty;
			double pointValue    = Instrument.MasterInstrument.PointValue;
			double tpPoints      = _breakEvenMode ? 0 : TakeProfitDollars / (totalQty * pointValue);
			double slPoints      = StopLossDollars / (totalQty * pointValue);

			double tpPrice, slPrice;
			string entryName, addOnName;

			if (TradeDirection == ETHDirection.Long)
			{
				entryName = "ETH_Long";
				addOnName = "ETH_Long_AddOn";
				tpPrice   = RoundToTick(avgPrice + tpPoints);
				slPrice   = RoundToTick(avgPrice - slPoints);
			}
			else
			{
				entryName = "ETH_Short";
				addOnName = "ETH_Short_AddOn";
				tpPrice   = RoundToTick(avgPrice - tpPoints);
				slPrice   = RoundToTick(avgPrice + slPoints);
			}

			// 先更新首仓TP/SL到新价位
			SetProfitTarget(entryName, CalculationMode.Price, tpPrice);
			SetStopLoss(entryName, CalculationMode.Price, slPrice, false);

			// 设置加仓TP/SL并下单
			SetProfitTarget(addOnName, CalculationMode.Price, tpPrice);
			SetStopLoss(addOnName, CalculationMode.Price, slPrice, false);

			if (TradeDirection == ETHDirection.Long)
				_entry2Order = EnterLong(AddOnQty, addOnName);
			else
				_entry2Order = EnterShort(AddOnQty, addOnName);

			Print(string.Format("[{0}] Bar#{1} | 加仓 {2} {3}手 | 模式={4} | 估算均价={5:F2} | TP={6:F2} SL={7:F2}",
				Time[0], CurrentBar, TradeDirection, AddOnQty,
				_breakEvenMode ? "打平" : "止盈", avgPrice, tpPrice, slPrice));
		}

		#endregion

		#region Force Close

		private void ForceClose()
		{
			Print(string.Format("[{0}] Bar#{1} | 美盘收盘强制平仓 | {2} {3}手",
				Time[0], CurrentBar, Position.MarketPosition, Position.Quantity));

			if (Position.MarketPosition == MarketPosition.Long)
			{
				ExitLong("ForceClose", "ETH_Long");
				if (_hasAddedOn)
					ExitLong("ForceClose", "ETH_Long_AddOn");
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				ExitShort("ForceClose", "ETH_Short");
				if (_hasAddedOn)
					ExitShort("ForceClose", "ETH_Short_AddOn");
			}
		}

		#endregion

		#region Order & Execution Events

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
			int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time,
			ErrorCode error, string comment)
		{
			if (order.Name == "ETH_Long" || order.Name == "ETH_Short")
				_entry1Order = order;
			else if (order.Name == "ETH_Long_AddOn" || order.Name == "ETH_Short_AddOn")
				_entry2Order = order;

			if (orderState == OrderState.Rejected)
			{
				Print(string.Format("[{0}] 订单被拒绝 | {1} | error={2} | comment={3}",
					time, order.Name, error, comment));
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId,
			double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			string name = execution.Order.Name;

			Print(string.Format("[{0}] 成交 | {1} @ {2:F2} x{3} | 方向={4}",
				time, name, price, quantity, marketPosition));

			// 首仓成交 → 记录成交价
			if ((name == "ETH_Long" || name == "ETH_Short")
				&& execution.Order.OrderState == OrderState.Filled)
			{
				_entry1FillPrice = price;
				Print(string.Format("[{0}] 首仓成交 @ {1:F2}", time, price));
			}

			// 加仓成交 → 用精确成交价重算TP/SL
			if ((name == "ETH_Long_AddOn" || name == "ETH_Short_AddOn")
				&& execution.Order.OrderState == OrderState.Filled)
			{
				double avgPrice   = (_entry1FillPrice + price) / 2.0;
				int    totalQty   = InitialQty + AddOnQty;
				double pointValue = Instrument.MasterInstrument.PointValue;
				double tpPoints   = _breakEvenMode ? 0 : TakeProfitDollars / (totalQty * pointValue);
				double slPoints   = StopLossDollars / (totalQty * pointValue);

				double tpPrice, slPrice;

				if (TradeDirection == ETHDirection.Long)
				{
					tpPrice = RoundToTick(avgPrice + tpPoints);
					slPrice = RoundToTick(avgPrice - slPoints);

					SetProfitTarget("ETH_Long", CalculationMode.Price, tpPrice);
					SetProfitTarget("ETH_Long_AddOn", CalculationMode.Price, tpPrice);
					SetStopLoss("ETH_Long", CalculationMode.Price, slPrice, false);
					SetStopLoss("ETH_Long_AddOn", CalculationMode.Price, slPrice, false);
				}
				else
				{
					tpPrice = RoundToTick(avgPrice - tpPoints);
					slPrice = RoundToTick(avgPrice + slPoints);

					SetProfitTarget("ETH_Short", CalculationMode.Price, tpPrice);
					SetProfitTarget("ETH_Short_AddOn", CalculationMode.Price, tpPrice);
					SetStopLoss("ETH_Short", CalculationMode.Price, slPrice, false);
					SetStopLoss("ETH_Short_AddOn", CalculationMode.Price, slPrice, false);
				}

				Print(string.Format("[{0}] 加仓成交 @ {1:F2} | 均价={2:F2} | TP={3:F2} SL={4:F2}",
					time, price, avgPrice, tpPrice, slPrice));
			}

			// 止盈/止损成交日志
			if (name == "Profit target")
			{
				string fromEntry = execution.Order.FromEntrySignal;
				Print(string.Format("[{0}] 止盈成交 | Entry={1} @ {2:F2}", time, fromEntry, price));

				// 首仓止盈且有未成交加仓单 → 取消加仓
				if (_hasAddedOn && (fromEntry == "ETH_Long" || fromEntry == "ETH_Short"))
				{
					if (_entry2Order != null
						&& (_entry2Order.OrderState == OrderState.Working
							|| _entry2Order.OrderState == OrderState.Accepted))
					{
						CancelOrder(_entry2Order);
					}
				}
			}
			else if (name == "Stop loss")
			{
				string fromEntry = execution.Order.FromEntrySignal;
				Print(string.Format("[{0}] 止损成交 | Entry={1} @ {2:F2}", time, fromEntry, price));

				if (_hasAddedOn && (fromEntry == "ETH_Long" || fromEntry == "ETH_Short"))
				{
					if (_entry2Order != null
						&& (_entry2Order.OrderState == OrderState.Working
							|| _entry2Order.OrderState == OrderState.Accepted))
					{
						CancelOrder(_entry2Order);
					}
				}
			}
		}

		protected override void OnPositionUpdate(Position position, double averagePrice,
			int quantity, MarketPosition marketPosition)
		{
			if (marketPosition == MarketPosition.Flat && _initialized)
			{
				// 统计打平次数
				if (_breakEvenMode)
					_breakEvenCount++;

				// 计算下次入场时间
				_nextEntryTime = ToNextExchangeTime(Time[0], EntryTimeUTC8);
				_hasAddedOn    = false;
				_breakEvenMode = false;
				_usOpenChecked = false;
				_entry1Order   = null;
				_entry2Order   = null;

				Print(string.Format("[{0}] Bar#{1} | 全部平仓 | 下次入场={2}",
					Time[0], CurrentBar, _nextEntryTime));
			}
		}

		#endregion

		#region Helpers

		/// <summary>
		/// 将 UTC+8 的 HHMMSS 时间转换为 afterExchange 之后的下一个交易所本地时间 DateTime。
		/// 正确处理跨午夜的情况。
		/// </summary>
		private DateTime ToNextExchangeTime(DateTime afterExchange, int utc8HHMMSS)
		{
			int offset = 8 - UtcOffsetHours;

			// 将交易所时间转换为 UTC+8
			DateTime afterUtc8 = afterExchange.AddHours(offset);

			// 在 UTC+8 时间轴上构建目标时刻
			int hh = utc8HHMMSS / 10000;
			int mm = (utc8HHMMSS % 10000) / 100;
			int ss = utc8HHMMSS % 100;
			DateTime targetUtc8 = afterUtc8.Date.AddHours(hh).AddMinutes(mm).AddSeconds(ss);

			// 如果目标已过，推到下一天
			if (targetUtc8 <= afterUtc8)
				targetUtc8 = targetUtc8.AddDays(1);

			// 转回交易所本地时间
			return targetUtc8.AddHours(-offset);
		}

		private double RoundToTick(double price)
		{
			return Instrument.MasterInstrument.RoundToTickSize(price);
		}

		#endregion

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "交易方向", Description = "做多或做空", Order = 1, GroupName = "策略配置")]
		public ETHDirection TradeDirection { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "入场时间(UTC+8)", Description = "东八区入场时间(HHMMSS，如080000=08:00)", Order = 2, GroupName = "策略配置")]
		public int EntryTimeUTC8 { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "初始手数", Description = "ETH时段初始开仓手数", Order = 3, GroupName = "策略配置")]
		public int InitialQty { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "加仓手数", Description = "美盘开盘浮亏时加仓手数", Order = 4, GroupName = "策略配置")]
		public int AddOnQty { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "加仓浮亏门槛($)", Description = "浮亏超过此金额才在美盘开盘时加仓(0=任何浮亏即加仓)", Order = 5, GroupName = "策略配置")]
		public double AddOnThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "打平浮亏门槛($)", Description = "浮亏超过此金额时加仓只求打平(0=禁用，始终追求止盈)", Order = 6, GroupName = "策略配置")]
		public double BreakEvenThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "止盈金额($)", Description = "止盈总金额(美元)", Order = 6, GroupName = "策略配置")]
		public double TakeProfitDollars { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "止损金额($)", Description = "止损总金额(美元)", Order = 6, GroupName = "策略配置")]
		public double StopLossDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "美盘开盘(UTC+8)", Description = "美盘开盘时间(HHMMSS，夏令时213000)", Order = 1, GroupName = "时段配置")]
		public int USOpenTimeUTC8 { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "美盘收盘(UTC+8)", Description = "美盘收盘时间(HHMMSS，夏令时040000次日)", Order = 2, GroupName = "时段配置")]
		public int USCloseTimeUTC8 { get; set; }

		[NinjaScriptProperty]
		[Range(-12, 12)]
		[Display(Name = "交易所UTC偏移", Description = "交易所时区UTC偏移(EST=-5, EDT=-4, CST=-6, CDT=-5)", Order = 3, GroupName = "时段配置")]
		public int UtcOffsetHours { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "启用回测", Description = "是否在历史数据上执行策略", Order = 1, GroupName = "回测配置")]
		public bool EnableBacktest { get; set; }

		#endregion
	}
}
