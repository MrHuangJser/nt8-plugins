#region Using declarations
using System;
using System.Collections.Generic;
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
	public enum MBStrategyDirection
	{
		[Display(Name = "双向")]
		Both,
		[Display(Name = "只做多")]
		LongOnly,
		[Display(Name = "只做空")]
		ShortOnly
	}

	public enum MBLevel
	{
		[Display(Name = "300%")]
		Pct300,
		[Display(Name = "200%")]
		Pct200,
		[Display(Name = "161.8%")]
		Pct161_8,
		[Display(Name = "123%")]
		Pct123,
		[Display(Name = "111%")]
		Pct111,
		[Display(Name = "100%")]
		Pct100,
		[Display(Name = "89%")]
		Pct89,
		[Display(Name = "79%")]
		Pct79,
		[Display(Name = "66%")]
		Pct66,
		[Display(Name = "50%")]
		Pct50,
		[Display(Name = "33%")]
		Pct33,
		[Display(Name = "21%")]
		Pct21,
		[Display(Name = "11%")]
		Pct11,
		[Display(Name = "0%")]
		Pct0,
		[Display(Name = "-11%")]
		PctN11,
		[Display(Name = "-23%")]
		PctN23,
		[Display(Name = "-61.8%")]
		PctN61_8,
		[Display(Name = "-100%")]
		PctN100,
		[Display(Name = "-200%")]
		PctN200
	}

	public class MotherBarStrategy : Strategy
	{
		#region Private State

		// MB状态
		private bool   _mbActive;
		private int    _mbFormBar;        // MB形成时的CurrentBar
		private double _mbBodyHigh;       // MB主体高点 (100%)
		private double _mbBodyLow;        // MB主体低点 (0%)
		private double _mbRange;          // MB主体振幅

		// 触及与方向完成标记
		private bool _sellTouched;        // K线是否已触及123%（不可逆）
		private bool _buyTouched;         // K线是否已触及-23%（不可逆）
		private bool _sellDone;           // 空方向交易是否已完成
		private bool _buyDone;            // 多方向交易是否已完成

		// 持仓状态
		private string _activeDirection;  // "Long" / "Short" / null
		private bool   _addOnFilled;      // 加仓是否已成交
		private int    _entryQty;         // 单仓入场数量
		private int    _tradeCount;       // 当前MB已完成的交易笔数

		// 订单引用（双向各自独立）
		private Order _longEntryOrder;
		private Order _shortEntryOrder;
		private Order _addOnOrder;

		// 可视化
		private int _mbId;                // MB编号，用于绘图tag唯一性

		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"MotherBar(内包K线)自动交易策略 - 基于MB价格水平的双向挂单策略";
				Name						= "MotherBarStrategy";
				Calculate					= Calculate.OnBarClose;
				EntriesPerDirection			= 2;
				EntryHandling				= EntryHandling.UniqueEntries;
				IsExitOnSessionCloseStrategy = false;
				IsFillLimitOnTouch			= false;
				TraceOrders					= true;
				BarsRequiredToTrade			= 3;
				IsInstantiatedOnEachOptimizationIteration = true;

				// 策略配置
				StrategyDirection			= MBStrategyDirection.Both;

				// 时段配置
				TradeStartTime				= 210000;
				TradeEndTime				= 060000;
				UtcOffsetHours				= -5;

				// 仓位管理
				MaxTotalLoss				= 500;
				EnableAddOn					= true;

				// 止盈配置
				LongTakeProfit				= MBLevel.Pct50;
				ShortTakeProfit				= MBLevel.Pct50;
				LongAddOnTakeProfit			= MBLevel.PctN61_8;
				ShortAddOnTakeProfit		= MBLevel.Pct161_8;

				// 交易限制
				MaxTradesPerMB				= 2;

				// 回测配置
				EnableBacktest				= true;

				// 可视化
				ShowMBLines					= true;
				MBLineLength				= 20;
				BullColor					= Brushes.Green;
				BearColor					= Brushes.Red;
				NeutralColor				= Brushes.Gray;
			}
			else if (State == State.DataLoaded)
			{
				ResetMBState();
				_mbId = 0;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return;

			// 回测开关
			if (!EnableBacktest && State == State.Historical)
				return;

			// 时段判断
			bool inSession = IsInTradeSession();

			// 时段外：平仓 + 取消挂单 + 重置MB
			if (!inSession)
			{
				if (Position.MarketPosition != MarketPosition.Flat)
				{
					Print(string.Format("[{0}] Bar#{1} | 时段外平仓 | 方向={2} 数量={3}",
						Time[0], CurrentBar, Position.MarketPosition, Position.Quantity));
					if (Position.MarketPosition == MarketPosition.Long)
					{
						ExitLong("SessionClose", "MB_Long");
						ExitLong("SessionClose", "MB_Long_AddOn");
					}
					else
					{
						ExitShort("SessionClose", "MB_Short");
						ExitShort("SessionClose", "MB_Short_AddOn");
					}
				}
				CancelAllPendingOrders();
				if (_mbActive)
				{
					Print(string.Format("[{0}] Bar#{1} | 时段外MB失效", Time[0], CurrentBar));
					InvalidateMB();
				}
				return;
			}

			// MB失效条件检查
			if (_mbActive)
				CheckMBInvalidation();

			// 若无有效MB，尝试检测新MB
			if (!_mbActive)
			{
				DetectMotherBar();
			}

			// 有有效MB且空仓时，检查入场信号
			if (_mbActive && Position.MarketPosition == MarketPosition.Flat && _activeDirection == null)
			{
				CheckEntrySignals();
			}

			// 更新可视化
			if (ShowMBLines && _mbActive)
				UpdateVisualization();
		}

		#region MB Detection

		private void DetectMotherBar()
		{
			// 内包K线条件：当前K线的High<=前一根High，Low>=前一根Low
			// 且不能高低点完全相等
			bool isInsideBar = High[0] <= High[1] && Low[0] >= Low[1]
				&& !(High[0] == High[1] && Low[0] == Low[1]);

			if (isInsideBar)
			{
				_mbActive    = true;
				_mbFormBar   = CurrentBar;
				_mbBodyHigh  = High[1];  // 前一根K线（母线）的高点
				_mbBodyLow   = Low[1];   // 前一根K线（母线）的低点
				_mbRange     = _mbBodyHigh - _mbBodyLow;
				_sellTouched = false;
				_buyTouched  = false;
				_sellDone    = false;
				_buyDone     = false;
				_tradeCount  = 0;
				_activeDirection = null;
				_addOnFilled = false;
				_entryQty    = 0;
				_mbId++;

				// 计算入场数量
				_entryQty = CalculateQuantity();

				Print(string.Format("[{0}] Bar#{1} | *** 新MB#{2}检测到 *** | BodyHigh={3:F2} BodyLow={4:F2} Range={5:F2} | 123%={6:F2} -23%={7:F2} | 入场数量={8}",
					Time[0], CurrentBar, _mbId, _mbBodyHigh, _mbBodyLow, _mbRange,
					CalcLevel(1.23), CalcLevel(-0.23), _entryQty));
			}
		}

		#endregion

		#region Entry Signals

		private void CheckEntrySignals()
		{
			if (_entryQty < 1)
				return;

			double level123 = CalcLevel(1.23);
			double levelN23 = CalcLevel(-0.23);

			// 卖出方向检测
			bool canSell = !_sellTouched && !_sellDone
				&& (StrategyDirection == MBStrategyDirection.Both || StrategyDirection == MBStrategyDirection.ShortOnly);

			if (canSell && High[0] >= level123)
			{
				_sellTouched = true;
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 卖出方向触及123% | High={3:F2} >= {4:F2}",
					Time[0], CurrentBar, _mbId, High[0], level123));

				PlaceSellOrder(level123);
			}

			// 买入方向检测
			bool canBuy = !_buyTouched && !_buyDone
				&& (StrategyDirection == MBStrategyDirection.Both || StrategyDirection == MBStrategyDirection.LongOnly);

			if (canBuy && Low[0] <= levelN23)
			{
				_buyTouched = true;
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 买入方向触及-23% | Low={3:F2} <= {4:F2}",
					Time[0], CurrentBar, _mbId, Low[0], levelN23));

				PlaceBuyOrder(levelN23);
			}
		}

		private void PlaceSellOrder(double level123)
		{
			double entryPrice = RoundToTick(level123);

			if (Close[0] > level123)
			{
				// 收盘价在123%上方，价格需要回落到123%才入场 → Stop Market
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂 Stop Sell @ {3:F2} (收盘价{4:F2} > 123%)",
					Time[0], CurrentBar, _mbId, entryPrice, Close[0]));
				_shortEntryOrder = EnterShortStopMarket(0, true, _entryQty, entryPrice, "MB_Short");
			}
			else
			{
				// 收盘价在123%下方，价格需要上涨到123%才入场 → Limit
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂 Limit Sell @ {3:F2} (收盘价{4:F2} <= 123%)",
					Time[0], CurrentBar, _mbId, entryPrice, Close[0]));
				_shortEntryOrder = EnterShortLimit(0, true, _entryQty, entryPrice, "MB_Short");
			}

			// 设置止盈止损
			SetProfitTarget("MB_Short", CalculationMode.Price, RoundToTick(CalcLevel(MBLevelToPct(ShortTakeProfit))));
			SetStopLoss("MB_Short", CalculationMode.Price, RoundToTick(CalcLevel(3.0)), false);
		}

		private void PlaceBuyOrder(double levelN23)
		{
			double entryPrice = RoundToTick(levelN23);

			if (Close[0] < levelN23)
			{
				// 收盘价在-23%下方，价格需要反弹到-23%才入场 → Stop Market
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂 Stop Buy @ {3:F2} (收盘价{4:F2} < -23%)",
					Time[0], CurrentBar, _mbId, entryPrice, Close[0]));
				_longEntryOrder = EnterLongStopMarket(0, true, _entryQty, entryPrice, "MB_Long");
			}
			else
			{
				// 收盘价在-23%上方，价格需要下跌到-23%才入场 → Limit
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂 Limit Buy @ {3:F2} (收盘价{4:F2} >= -23%)",
					Time[0], CurrentBar, _mbId, entryPrice, Close[0]));
				_longEntryOrder = EnterLongLimit(0, true, _entryQty, entryPrice, "MB_Long");
			}

			// 设置止盈止损
			SetProfitTarget("MB_Long", CalculationMode.Price, RoundToTick(CalcLevel(MBLevelToPct(LongTakeProfit))));
			SetStopLoss("MB_Long", CalculationMode.Price, RoundToTick(CalcLevel(-2.0)), false);
		}

		#endregion

		#region Order & Execution Events

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
			int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time,
			ErrorCode error, string comment)
		{
			// 追踪订单引用
			if (order.Name == "MB_Long")
				_longEntryOrder = order;
			else if (order.Name == "MB_Short")
				_shortEntryOrder = order;
			else if (order.Name == "MB_Long_AddOn" || order.Name == "MB_Short_AddOn")
				_addOnOrder = order;

			if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
			{
				Print(string.Format("[{0}] 订单{1} {2} | 名称={3}",
					time, orderState, error, order.Name));

				if (order == _longEntryOrder)  _longEntryOrder = null;
				if (order == _shortEntryOrder) _shortEntryOrder = null;
				if (order == _addOnOrder)      _addOnOrder = null;
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId,
			double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			string orderName = execution.Order.Name;

			Print(string.Format("[{0}] *** 成交 *** | 名称={1} 价格={2:F2} 数量={3} 方向={4}",
				time, orderName, price, quantity, marketPosition));

			// 入场成交
			if (orderName == "MB_Long")
			{
				_activeDirection = "Long";
				HandleEntryFilled("Long");
			}
			else if (orderName == "MB_Short")
			{
				_activeDirection = "Short";
				HandleEntryFilled("Short");
			}
			// 加仓成交
			else if (orderName == "MB_Long_AddOn")
			{
				_addOnFilled = true;
				AdjustTakeProfitAfterAddOn("Long");
			}
			else if (orderName == "MB_Short_AddOn")
			{
				_addOnFilled = true;
				AdjustTakeProfitAfterAddOn("Short");
			}
		}

		protected override void OnPositionUpdate(Position position, double averagePrice,
			int quantity, MarketPosition marketPosition)
		{
			// 从持仓变为空仓 = 止盈或止损成交
			if (marketPosition == MarketPosition.Flat && _activeDirection != null)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 平仓完成 | 方向={3}",
					Time[0], CurrentBar, _mbId, _activeDirection));

				HandlePositionClosed();
			}
		}

		private void HandleEntryFilled(string direction)
		{
			Print(string.Format("[{0}] Bar#{1} | MB#{2} {3}入场成交",
				Time[0], CurrentBar, _mbId, direction));

			// 取消对向的挂单
			CancelOppositeOrders(direction);

			// 挂加仓单
			if (EnableAddOn)
				PlaceAddOnOrder(direction);
		}

		private void PlaceAddOnOrder(string direction)
		{
			if (direction == "Long")
			{
				double addOnPrice = RoundToTick(CalcLevel(-1.0));
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂多头加仓 Limit Buy @ {3:F2} (Level -100%)",
					Time[0], CurrentBar, _mbId, addOnPrice));
				_addOnOrder = EnterLongLimit(0, true, _entryQty, addOnPrice, "MB_Long_AddOn");

				// 为加仓设置止盈止损（初始同主仓）
				SetProfitTarget("MB_Long_AddOn", CalculationMode.Price, RoundToTick(CalcLevel(MBLevelToPct(LongTakeProfit))));
				SetStopLoss("MB_Long_AddOn", CalculationMode.Price, RoundToTick(CalcLevel(-2.0)), false);
			}
			else
			{
				double addOnPrice = RoundToTick(CalcLevel(1.0));
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂空头加仓 Limit Sell @ {3:F2} (Level 100%)",
					Time[0], CurrentBar, _mbId, addOnPrice));
				_addOnOrder = EnterShortLimit(0, true, _entryQty, addOnPrice, "MB_Short_AddOn");

				SetProfitTarget("MB_Short_AddOn", CalculationMode.Price, RoundToTick(CalcLevel(MBLevelToPct(ShortTakeProfit))));
				SetStopLoss("MB_Short_AddOn", CalculationMode.Price, RoundToTick(CalcLevel(3.0)), false);
			}
		}

		private void AdjustTakeProfitAfterAddOn(string direction)
		{
			if (direction == "Long")
			{
				double newTP = RoundToTick(CalcLevel(MBLevelToPct(LongAddOnTakeProfit)));
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 多头加仓成交，止盈移至 {3} = {4:F2}",
					Time[0], CurrentBar, _mbId, LongAddOnTakeProfit, newTP));

				// 调整两个仓位的止盈
				SetProfitTarget("MB_Long", CalculationMode.Price, newTP);
				SetProfitTarget("MB_Long_AddOn", CalculationMode.Price, newTP);
			}
			else
			{
				double newTP = RoundToTick(CalcLevel(MBLevelToPct(ShortAddOnTakeProfit)));
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 空头加仓成交，止盈移至 {3} = {4:F2}",
					Time[0], CurrentBar, _mbId, ShortAddOnTakeProfit, newTP));

				SetProfitTarget("MB_Short", CalculationMode.Price, newTP);
				SetProfitTarget("MB_Short_AddOn", CalculationMode.Price, newTP);
			}
		}

		private void HandlePositionClosed()
		{
			_tradeCount++;

			// 取消加仓挂单
			if (_addOnOrder != null && (_addOnOrder.OrderState == OrderState.Working
				|| _addOnOrder.OrderState == OrderState.Accepted))
			{
				CancelOrder(_addOnOrder);
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 取消加仓单", Time[0], CurrentBar, _mbId));
			}

			// 标记方向完成
			if (_activeDirection == "Long")
				_buyDone = true;
			else if (_activeDirection == "Short")
				_sellDone = true;

			string closedDir = _activeDirection;
			_activeDirection = null;
			_addOnFilled = false;
			_addOnOrder = null;
			if (closedDir == "Long")
				_longEntryOrder = null;
			else
				_shortEntryOrder = null;

			Print(string.Format("[{0}] Bar#{1} | MB#{2} {3}方向完成 | 交易计数={4}/{5} | buyDone={6} sellDone={7}",
				Time[0], CurrentBar, _mbId, closedDir, _tradeCount, MaxTradesPerMB, _buyDone, _sellDone));

			// 检查是否两个方向都已完成
			if (_buyDone && _sellDone)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 双向均已完成，MB结束", Time[0], CurrentBar, _mbId));
				InvalidateMB();
				return;
			}

			// 达到最大交易次数
			if (_tradeCount >= MaxTradesPerMB)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 达到最大交易次数{3}，MB结束",
					Time[0], CurrentBar, _mbId, MaxTradesPerMB));
				InvalidateMB();
				return;
			}

			// 若对向未被触及过，恢复对向监控（不需要做什么，CheckEntrySignals会自动检查）
			if (closedDir == "Long" && !_sellTouched)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 多头完成，空头方向未触及，继续监控",
					Time[0], CurrentBar, _mbId));
			}
			else if (closedDir == "Short" && !_buyTouched)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 空头完成，多头方向未触及，继续监控",
					Time[0], CurrentBar, _mbId));
			}
		}

		private void CancelOppositeOrders(string filledDirection)
		{
			if (filledDirection == "Long")
			{
				// 多头入场，取消空头挂单
				if (_shortEntryOrder != null && (_shortEntryOrder.OrderState == OrderState.Working
					|| _shortEntryOrder.OrderState == OrderState.Accepted))
				{
					Print(string.Format("[{0}] Bar#{1} | MB#{2} 多头入场，取消空头挂单",
						Time[0], CurrentBar, _mbId));
					CancelOrder(_shortEntryOrder);
				}
			}
			else
			{
				// 空头入场，取消多头挂单
				if (_longEntryOrder != null && (_longEntryOrder.OrderState == OrderState.Working
					|| _longEntryOrder.OrderState == OrderState.Accepted))
				{
					Print(string.Format("[{0}] Bar#{1} | MB#{2} 空头入场，取消多头挂单",
						Time[0], CurrentBar, _mbId));
					CancelOrder(_longEntryOrder);
				}
			}
		}

		#endregion

		#region MB Invalidation

		private void CheckMBInvalidation()
		{
			double level200  = CalcLevel(2.0);
			double levelN100 = CalcLevel(-1.0);

			// 上方失效：High >= 200%
			if (High[0] >= level200)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 上方失效 | High={3:F2} >= 200%={4:F2}",
					Time[0], CurrentBar, _mbId, High[0], level200));
				CancelAllPendingOrders();
				if (Position.MarketPosition != MarketPosition.Flat)
				{
					// 价格到200%意味着空单止损应该已触发，多单不应该存在于此价位
					// 但保险起见检查
					Print(string.Format("[{0}] Bar#{1} | MB#{2} 失效时仍有持仓，等待止损执行",
						Time[0], CurrentBar, _mbId));
				}
				InvalidateMB();
				return;
			}

			// 下方失效：Low <= -100%
			if (Low[0] <= levelN100)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 下方失效 | Low={3:F2} <= -100%={4:F2}",
					Time[0], CurrentBar, _mbId, Low[0], levelN100));
				CancelAllPendingOrders();
				if (Position.MarketPosition != MarketPosition.Flat)
				{
					Print(string.Format("[{0}] Bar#{1} | MB#{2} 失效时仍有持仓，等待止损执行",
						Time[0], CurrentBar, _mbId));
				}
				InvalidateMB();
				return;
			}
		}

		private void InvalidateMB()
		{
			Print(string.Format("[{0}] Bar#{1} | MB#{2} 失效/结束", Time[0], CurrentBar, _mbId));
			ResetMBState();
		}

		private void ResetMBState()
		{
			_mbActive    = false;
			_mbFormBar   = 0;
			_mbBodyHigh  = 0;
			_mbBodyLow   = 0;
			_mbRange     = 0;
			_sellTouched = false;
			_buyTouched  = false;
			_sellDone    = false;
			_buyDone     = false;
			_tradeCount  = 0;
			_activeDirection = null;
			_addOnFilled = false;
			_entryQty    = 0;
			_longEntryOrder  = null;
			_shortEntryOrder = null;
			_addOnOrder  = null;
		}

		private void CancelAllPendingOrders()
		{
			if (_longEntryOrder != null && (_longEntryOrder.OrderState == OrderState.Working
				|| _longEntryOrder.OrderState == OrderState.Accepted))
			{
				CancelOrder(_longEntryOrder);
				_longEntryOrder = null;
			}
			if (_shortEntryOrder != null && (_shortEntryOrder.OrderState == OrderState.Working
				|| _shortEntryOrder.OrderState == OrderState.Accepted))
			{
				CancelOrder(_shortEntryOrder);
				_shortEntryOrder = null;
			}
			if (_addOnOrder != null && (_addOnOrder.OrderState == OrderState.Working
				|| _addOnOrder.OrderState == OrderState.Accepted))
			{
				CancelOrder(_addOnOrder);
				_addOnOrder = null;
			}
		}

		#endregion

		#region Position Sizing

		private int CalculateQuantity()
		{
			if (_mbRange <= 0)
				return 0;

			// 以加仓后最坏情况计算：风险点数 = 1.382 × Range
			double riskPoints = 1.382 * _mbRange;
			double pointValue = Instrument.MasterInstrument.PointValue;
			double singleHandRisk = riskPoints * pointValue;

			if (singleHandRisk <= 0)
				return 1;

			int totalQty = (int)Math.Floor(MaxTotalLoss / singleHandRisk);

			// 总手数需被2整除（主仓+加仓各一半）
			int perSide = totalQty / 2;
			if (perSide < 1)
				perSide = 1;  // 最少1手，接受可能超限

			Print(string.Format("[{0}] Bar#{1} | 仓位计算: Range={2:F2} 风险点数={3:F2} 点值={4} 单手风险=${5:F2} 总手数={6} 单仓={7}",
				Time[0], CurrentBar, _mbRange, riskPoints, pointValue, singleHandRisk, totalQty, perSide));

			return perSide;
		}

		#endregion

		#region Helpers

		private double CalcLevel(double pct)
		{
			return _mbBodyLow + _mbRange * pct;
		}

		private double MBLevelToPct(MBLevel level)
		{
			switch (level)
			{
				case MBLevel.Pct300:   return 3.0;
				case MBLevel.Pct200:   return 2.0;
				case MBLevel.Pct161_8: return 1.618;
				case MBLevel.Pct123:   return 1.23;
				case MBLevel.Pct111:   return 1.11;
				case MBLevel.Pct100:   return 1.0;
				case MBLevel.Pct89:    return 0.89;
				case MBLevel.Pct79:    return 0.79;
				case MBLevel.Pct66:    return 0.66;
				case MBLevel.Pct50:    return 0.5;
				case MBLevel.Pct33:    return 0.33;
				case MBLevel.Pct21:    return 0.21;
				case MBLevel.Pct11:    return 0.11;
				case MBLevel.Pct0:     return 0.0;
				case MBLevel.PctN11:   return -0.11;
				case MBLevel.PctN23:   return -0.23;
				case MBLevel.PctN61_8: return -0.618;
				case MBLevel.PctN100:  return -1.0;
				case MBLevel.PctN200:  return -2.0;
				default:               return 0.5;
			}
		}

		private double RoundToTick(double price)
		{
			return Instrument.MasterInstrument.RoundToTickSize(price);
		}

		private bool IsInTradeSession()
		{
			// 将东八区时间转换为交易所时间，再与图表时间比较
			// 由于NT8的Time[0]已经是交易所本地时间，我们将用户输入的东八区时间转换为交易所时间
			int offset = 8 - UtcOffsetHours;  // 东八区与交易所的时差

			int startHour   = TradeStartTime / 10000;
			int startMinute = (TradeStartTime % 10000) / 100;
			int startSecond = TradeStartTime % 100;

			int endHour   = TradeEndTime / 10000;
			int endMinute = (TradeEndTime % 10000) / 100;
			int endSecond = TradeEndTime % 100;

			// 转换为交易所时间（减去时差）
			DateTime baseDate = DateTime.Today;
			DateTime startUtc8 = baseDate.AddHours(startHour).AddMinutes(startMinute).AddSeconds(startSecond);
			DateTime endUtc8   = baseDate.AddHours(endHour).AddMinutes(endMinute).AddSeconds(endSecond);

			DateTime startExchange = startUtc8.AddHours(-offset);
			DateTime endExchange   = endUtc8.AddHours(-offset);

			int exchangeStart = startExchange.Hour * 10000 + startExchange.Minute * 100 + startExchange.Second;
			int exchangeEnd   = endExchange.Hour * 10000 + endExchange.Minute * 100 + endExchange.Second;

			int currentTime = ToTime(Time[0]);

			if (exchangeStart > exchangeEnd)
				return currentTime >= exchangeStart || currentTime <= exchangeEnd;
			else
				return currentTime >= exchangeStart && currentTime <= exchangeEnd;
		}

		#endregion

		#region Visualization

		private void UpdateVisualization()
		{
			int startBar = CurrentBar - _mbFormBar;
			int endBar   = Math.Max(startBar - MBLineLength, 0);

			string prefix = "MB" + _mbId + "_";

			// 定义所有水平线 { 百分比, 标签, 颜色, 线型, 宽度 }
			DrawLevel(prefix, 3.0,    "SL_Short_300",    BearColor,    DashStyleHelper.Dash,    1, startBar, endBar);
			DrawLevel(prefix, 2.0,    "Invalid_200",     BearColor,    DashStyleHelper.Solid,   2, startBar, endBar);
			DrawLevel(prefix, 1.618,  "AddTP_161",       BearColor,    DashStyleHelper.Dot,     1, startBar, endBar);
			DrawLevel(prefix, 1.23,   "Sell_123",        BearColor,    DashStyleHelper.Solid,   2, startBar, endBar);
			DrawLevel(prefix, 1.0,    "MB_High",         NeutralColor, DashStyleHelper.Solid,   2, startBar, endBar);
			DrawLevel(prefix, 0.5,    "TP_50",           NeutralColor, DashStyleHelper.DashDot, 2, startBar, endBar);
			DrawLevel(prefix, 0.0,    "MB_Low",          NeutralColor, DashStyleHelper.Solid,   2, startBar, endBar);
			DrawLevel(prefix, -0.23,  "Buy_N23",         BullColor,    DashStyleHelper.Solid,   2, startBar, endBar);
			DrawLevel(prefix, -0.618, "AddTP_N61",       BullColor,    DashStyleHelper.Dot,     1, startBar, endBar);
			DrawLevel(prefix, -1.0,   "Invalid_N100",    BullColor,    DashStyleHelper.Solid,   2, startBar, endBar);
			DrawLevel(prefix, -2.0,   "SL_Long_N200",    BullColor,    DashStyleHelper.Dash,    1, startBar, endBar);
		}

		private void DrawLevel(string prefix, double pct, string label, Brush color,
			DashStyleHelper dashStyle, int width, int startBar, int endBar)
		{
			double price = RoundToTick(CalcLevel(pct));
			string tag = prefix + label;
			Draw.Line(this, tag, false, startBar, price, endBar, price, color, dashStyle, width);
		}

		#endregion

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "策略方向", Description = "选择做多、做空或双向交易", Order = 1, GroupName = "策略配置")]
		public MBStrategyDirection StrategyDirection { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "交易开始时间", Description = "东八区交易开始时间(HHMMSS格式，如210000=21:00)", Order = 1, GroupName = "时段配置")]
		public int TradeStartTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "交易结束时间", Description = "东八区交易结束时间(HHMMSS格式，如060000=06:00)", Order = 2, GroupName = "时段配置")]
		public int TradeEndTime { get; set; }

		[NinjaScriptProperty]
		[Range(-12, 12)]
		[Display(Name = "交易所UTC偏移", Description = "交易所相对UTC的小时偏移(EST=-5, EDT=-4)", Order = 3, GroupName = "时段配置")]
		public int UtcOffsetHours { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "最大总亏损", Description = "含加仓的最大总亏损金额(美元)", Order = 1, GroupName = "仓位管理")]
		public int MaxTotalLoss { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "启用加仓", Description = "是否启用加仓逻辑", Order = 2, GroupName = "仓位管理")]
		public bool EnableAddOn { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "多头止盈位", Description = "多头初始止盈的MB水平", Order = 3, GroupName = "仓位管理")]
		public MBLevel LongTakeProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "空头止盈位", Description = "空头初始止盈的MB水平", Order = 4, GroupName = "仓位管理")]
		public MBLevel ShortTakeProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "多头加仓止盈位", Description = "多头加仓后整体止盈的MB水平", Order = 5, GroupName = "仓位管理")]
		public MBLevel LongAddOnTakeProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "空头加仓止盈位", Description = "空头加仓后整体止盈的MB水平", Order = 6, GroupName = "仓位管理")]
		public MBLevel ShortAddOnTakeProfit { get; set; }

		[NinjaScriptProperty]
		[Range(1, 4)]
		[Display(Name = "单MB最大交易数", Description = "单个MB允许的最大交易笔数", Order = 1, GroupName = "交易限制")]
		public int MaxTradesPerMB { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "启用回测", Description = "是否在历史数据上执行策略", Order = 1, GroupName = "回测配置")]
		public bool EnableBacktest { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "显示MB线段", Description = "是否在图表上显示MB价格水平线", Order = 1, GroupName = "可视化")]
		public bool ShowMBLines { get; set; }

		[NinjaScriptProperty]
		[Range(5, 100)]
		[Display(Name = "MB线段长度", Description = "MB线段向右延伸的K线数量", Order = 2, GroupName = "可视化")]
		public int MBLineLength { get; set; }

		[XmlIgnore]
		[Display(Name = "多头颜色", Description = "多头相关线段颜色", Order = 3, GroupName = "可视化")]
		public Brush BullColor { get; set; }

		[Browsable(false)]
		public string BullColorSerializable
		{
			get { return Serialize.BrushToString(BullColor); }
			set { BullColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "空头颜色", Description = "空头相关线段颜色", Order = 4, GroupName = "可视化")]
		public Brush BearColor { get; set; }

		[Browsable(false)]
		public string BearColorSerializable
		{
			get { return Serialize.BrushToString(BearColor); }
			set { BearColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "中性颜色", Description = "中性线段颜色", Order = 5, GroupName = "可视化")]
		public Brush NeutralColor { get; set; }

		[Browsable(false)]
		public string NeutralColorSerializable
		{
			get { return Serialize.BrushToString(NeutralColor); }
			set { NeutralColor = Serialize.StringToBrush(value); }
		}

		#endregion
	}
}
