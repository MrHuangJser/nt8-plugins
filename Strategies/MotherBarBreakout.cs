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
	public enum MBBOState
	{
		Idle,
		WaitingConfirmation,
		StopPending,
		EntryFilled
	}

	public enum MBBODirection
	{
		[Display(Name = "双向")]
		Both,
		[Display(Name = "只做多")]
		LongOnly,
		[Display(Name = "只做空")]
		ShortOnly
	}

	public class MotherBarBreakout : Strategy
	{
		#region Private State

		private MBBOState _state;

		// MB
		private double _mbHigh;
		private double _mbLow;
		private double _mbRange;
		private int    _mbFormBar;
		private int    _mbId;

		// 确认与方向
		private string _direction;          // "Long" / "Short" / null
		private double _confirmBarHigh;
		private double _confirmBarLow;

		// 订单引用
		private Order _stopOrder;
		private Order _addOnOrder;
		private Order _addOn2Order;

		// 成交状态
		private double _stopFillPrice;
		private bool   _addOn1Filled;
		private bool   _addOn2Filled;

		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"MotherBar突破确认策略 - 基于Confirmation Signal #1的Stop+Limit双腿入场";
				Name						= "MotherBarBreakout";
				Calculate					= Calculate.OnBarClose;
				EntriesPerDirection			= 3;
				EntryHandling				= EntryHandling.UniqueEntries;
				IsExitOnSessionCloseStrategy = false;
				IsFillLimitOnTouch			= false;
				TraceOrders					= true;
				BarsRequiredToTrade			= 3;
				IsInstantiatedOnEachOptimizationIteration = true;
				RealtimeErrorHandling		= RealtimeErrorHandling.IgnoreAllErrors;

				// 策略配置
				StrategyDirection			= MBBODirection.Both;

				// 时段配置
				TradeStartTime				= 063000;
				TradeEndTime				= 210000;
				UtcOffsetHours				= 8;

				// 止盈配置
				LongTPPct					= 1.618;
				ShortTPPct					= -0.618;

				// 加仓配置
				EnableAddOn					= true;
				LongAddOnPct				= 0.725;
				ShortAddOnPct				= 0.27;

				// 加仓2配置（保本目标）
				EnableAddOn2				= true;
				LongAddOn2Pct				= 0.27;
				ShortAddOn2Pct				= 0.725;

				// 回测配置
				EnableBacktest				= true;

				// 可视化
				ShowMBLines					= true;
				MBLineLength				= 100;
				BullColor					= Brushes.Green;
				BearColor					= Brushes.Red;
				NeutralColor				= Brushes.Gray;
			}
			else if (State == State.DataLoaded)
			{
				ResetAll();
				_mbId = 0;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return;

			if (!EnableBacktest && State == State.Historical)
				return;

			bool inSession = IsInTradeSession();

			// 时段外处理
			if (!inSession)
			{
				HandleOutOfSession();
				return;
			}

			// 状态驱动逻辑
			switch (_state)
			{
				case MBBOState.Idle:
					DetectMotherBar();
					break;

				case MBBOState.WaitingConfirmation:
					// 检查更大MB替换
					CheckLargerMBReplacement();
					// 检查失效
					if (_state == MBBOState.WaitingConfirmation)
						CheckInvalidation();
					// 检查确认信号
					if (_state == MBBOState.WaitingConfirmation)
						CheckConfirmationSignal();
					break;

				case MBBOState.StopPending:
					// 检查更大MB替换（无持仓）
					CheckLargerMBReplacement();
					// 检查失效
					if (_state == MBBOState.StopPending)
						CheckInvalidation();
					// 检查方向翻转
					if (_state == MBBOState.StopPending)
						CheckDirectionFlip();
					break;

				case MBBOState.EntryFilled:
					// 有持仓，由TP/SL自动处理，不检查新MB或失效
					break;
			}

			// 可视化
			if (ShowMBLines && _state != MBBOState.Idle)
				UpdateVisualization();
		}

		#region MB Detection

		private void DetectMotherBar()
		{
			bool isInsideBar = High[0] <= High[1] && Low[0] >= Low[1]
				&& !(High[0] == High[1] && Low[0] == Low[1]);

			if (isInsideBar)
			{
				_mbHigh    = High[1];
				_mbLow     = Low[1];
				_mbRange   = _mbHigh - _mbLow;
				_mbFormBar = CurrentBar;
				_mbId++;
				_state     = MBBOState.WaitingConfirmation;
				_direction = null;

				Print(string.Format("[{0}] Bar#{1} | *** MB#{2} 检测到 *** | High={3:F2} Low={4:F2} Range={5:F2}",
					Time[0], CurrentBar, _mbId, _mbHigh, _mbLow, _mbRange));

				// 当前bar本身也可能是确认K
				CheckConfirmationSignal();
			}
		}

		#endregion

		#region Confirmation Signal

		private void CheckConfirmationSignal()
		{
			if (_state != MBBOState.WaitingConfirmation)
				return;

			double level111  = CalcLevel(1.11);
			double levelN11  = CalcLevel(-0.11);

			bool canLong  = StrategyDirection == MBBODirection.Both || StrategyDirection == MBBODirection.LongOnly;
			bool canShort = StrategyDirection == MBBODirection.Both || StrategyDirection == MBBODirection.ShortOnly;

			if (canLong && Close[0] > level111)
			{
				_direction       = "Long";
				_confirmBarHigh  = High[0];
				_confirmBarLow   = Low[0];
				_state           = MBBOState.StopPending;

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 多头确认 | Close={3:F2} > 111%={4:F2} | ConfirmHigh={5:F2}",
					Time[0], CurrentBar, _mbId, Close[0], level111, _confirmBarHigh));

				PlaceStopEntry();
			}
			else if (canShort && Close[0] < levelN11)
			{
				_direction       = "Short";
				_confirmBarHigh  = High[0];
				_confirmBarLow   = Low[0];
				_state           = MBBOState.StopPending;

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 空头确认 | Close={3:F2} < -11%={4:F2} | ConfirmLow={5:F2}",
					Time[0], CurrentBar, _mbId, Close[0], levelN11, _confirmBarLow));

				PlaceStopEntry();
			}
		}

		private void CheckDirectionFlip()
		{
			if (_state != MBBOState.StopPending)
				return;

			double level111 = CalcLevel(1.11);
			double levelN11 = CalcLevel(-0.11);

			bool canLong  = StrategyDirection == MBBODirection.Both || StrategyDirection == MBBODirection.LongOnly;
			bool canShort = StrategyDirection == MBBODirection.Both || StrategyDirection == MBBODirection.ShortOnly;

			// 当前做多等待，出现空头确认
			if (_direction == "Long" && canShort && Close[0] < levelN11)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 方向翻转 Long→Short | Close={3:F2} < -11%={4:F2}",
					Time[0], CurrentBar, _mbId, Close[0], levelN11));

				CancelStopOrder();
				_direction       = "Short";
				_confirmBarHigh  = High[0];
				_confirmBarLow   = Low[0];
				PlaceStopEntry();
			}
			// 当前做空等待，出现多头确认
			else if (_direction == "Short" && canLong && Close[0] > level111)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 方向翻转 Short→Long | Close={3:F2} > 111%={4:F2}",
					Time[0], CurrentBar, _mbId, Close[0], level111));

				CancelStopOrder();
				_direction       = "Long";
				_confirmBarHigh  = High[0];
				_confirmBarLow   = Low[0];
				PlaceStopEntry();
			}
		}

		#endregion

		#region Entry Orders

		private void PlaceStopEntry()
		{
			double slPrice;

			if (_direction == "Long")
			{
				double stopPrice = RoundToTick(_confirmBarHigh + TickSize);
				slPrice = RoundToTick(CalcLevel(-0.23));

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂多头Stop Buy @ {3:F2} | SL={4:F2}",
					Time[0], CurrentBar, _mbId, stopPrice, slPrice));

				SetStopLoss("MB_BO_Long", CalculationMode.Price, slPrice, false);
				SetProfitTarget("MB_BO_Long", CalculationMode.Price, FloorToTick(CalcLevel(LongTPPct)));
				_stopOrder = EnterLongStopMarket(0, true, 1, stopPrice, "MB_BO_Long");
			}
			else
			{
				double stopPrice = RoundToTick(_confirmBarLow - TickSize);
				slPrice = RoundToTick(CalcLevel(1.23));

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂空头Stop Sell @ {3:F2} | SL={4:F2}",
					Time[0], CurrentBar, _mbId, stopPrice, slPrice));

				SetStopLoss("MB_BO_Short", CalculationMode.Price, slPrice, false);
				SetProfitTarget("MB_BO_Short", CalculationMode.Price, CeilToTick(CalcLevel(ShortTPPct)));
				_stopOrder = EnterShortStopMarket(0, true, 1, stopPrice, "MB_BO_Short");
			}
		}

		private void PlaceAddOnOrder()
		{
			if (!EnableAddOn)
				return;

			if (_direction == "Long")
			{
				double addOnPrice = RoundToTick(CalcLevel(LongAddOnPct));
				double slPrice    = RoundToTick(CalcLevel(-0.23));

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂多头加仓 Limit Buy @ {3:F2} (79%) | SL={4:F2}",
					Time[0], CurrentBar, _mbId, addOnPrice, slPrice));

				SetStopLoss("MB_BO_Long_AddOn", CalculationMode.Price, slPrice, false);
				SetProfitTarget("MB_BO_Long_AddOn", CalculationMode.Price, FloorToTick(CalcLevel(LongTPPct)));
				_addOnOrder = EnterLongLimit(0, true, 1, addOnPrice, "MB_BO_Long_AddOn");
			}
			else
			{
				double addOnPrice = RoundToTick(CalcLevel(ShortAddOnPct));
				double slPrice    = RoundToTick(CalcLevel(1.23));

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂空头加仓 Limit Sell @ {3:F2} (21%) | SL={4:F2}",
					Time[0], CurrentBar, _mbId, addOnPrice, slPrice));

				SetStopLoss("MB_BO_Short_AddOn", CalculationMode.Price, slPrice, false);
				SetProfitTarget("MB_BO_Short_AddOn", CalculationMode.Price, CeilToTick(CalcLevel(ShortTPPct)));
				_addOnOrder = EnterShortLimit(0, true, 1, addOnPrice, "MB_BO_Short_AddOn");
			}
		}

		private void PlaceAddOn2Order()
		{
			if (!EnableAddOn2)
				return;

			if (_direction == "Long")
			{
				double addOn1Price = RoundToTick(CalcLevel(LongAddOnPct));
				double addOn2Price = RoundToTick(CalcLevel(LongAddOn2Pct));
				double slPrice     = RoundToTick(CalcLevel(-0.23));
				double estBE       = RoundToTick((_stopFillPrice + addOn1Price + addOn2Price) / 3.0);

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂多头加仓2 Limit Buy @ {3:F2} ({4}%) | SL={5:F2} estBE={6:F2}",
					Time[0], CurrentBar, _mbId, addOn2Price, LongAddOn2Pct * 100, slPrice, estBE));

				SetStopLoss("MB_BO_Long_AddOn2", CalculationMode.Price, slPrice, false);
				SetProfitTarget("MB_BO_Long_AddOn2", CalculationMode.Price, estBE);
				_addOn2Order = EnterLongLimit(0, true, 1, addOn2Price, "MB_BO_Long_AddOn2");
			}
			else
			{
				double addOn1Price = RoundToTick(CalcLevel(ShortAddOnPct));
				double addOn2Price = RoundToTick(CalcLevel(ShortAddOn2Pct));
				double slPrice     = RoundToTick(CalcLevel(1.23));
				double estBE       = RoundToTick((_stopFillPrice + addOn1Price + addOn2Price) / 3.0);

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 挂空头加仓2 Limit Sell @ {3:F2} ({4}%) | SL={5:F2} estBE={6:F2}",
					Time[0], CurrentBar, _mbId, addOn2Price, ShortAddOn2Pct * 100, slPrice, estBE));

				SetStopLoss("MB_BO_Short_AddOn2", CalculationMode.Price, slPrice, false);
				SetProfitTarget("MB_BO_Short_AddOn2", CalculationMode.Price, estBE);
				_addOn2Order = EnterShortLimit(0, true, 1, addOn2Price, "MB_BO_Short_AddOn2");
			}
		}

		private void UpdateAllTPsToBreakEven()
		{
			double bePrice = RoundToTick(Position.AveragePrice);

			Print(string.Format("[{0}] Bar#{1} | MB#{2} *** 保本模式 *** | BE={3:F2} (Position.AveragePrice)",
				Time[0], CurrentBar, _mbId, bePrice));

			if (_direction == "Long")
			{
				SetProfitTarget("MB_BO_Long", CalculationMode.Price, bePrice);
				SetProfitTarget("MB_BO_Long_AddOn", CalculationMode.Price, bePrice);
				SetProfitTarget("MB_BO_Long_AddOn2", CalculationMode.Price, bePrice);
			}
			else
			{
				SetProfitTarget("MB_BO_Short", CalculationMode.Price, bePrice);
				SetProfitTarget("MB_BO_Short_AddOn", CalculationMode.Price, bePrice);
				SetProfitTarget("MB_BO_Short_AddOn2", CalculationMode.Price, bePrice);
			}
		}

		#endregion

		#region Order & Execution Events

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
			int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time,
			ErrorCode error, string comment)
		{
			if (order.Name == "MB_BO_Long" || order.Name == "MB_BO_Short")
				_stopOrder = order;
			else if (order.Name == "MB_BO_Long_AddOn" || order.Name == "MB_BO_Short_AddOn")
				_addOnOrder = order;
			else if (order.Name == "MB_BO_Long_AddOn2" || order.Name == "MB_BO_Short_AddOn2")
				_addOn2Order = order;

			if (orderState == OrderState.Rejected)
			{
				Print(string.Format("[{0}] 订单被拒绝 | 名称={1} | comment={2} | error={3}",
					time, order.Name, comment, error));

				if (order == _stopOrder && _state == MBBOState.StopPending)
				{
					// Stop单被拒绝：废弃当前MB，回到Idle等待下一个MB
					_stopOrder = null;
					TransitionToIdle("Stop单被拒绝");
				}
				else
				{
					// 非预期的拒绝：平仓并终止策略
					FatalError(string.Format("非预期订单拒绝: {0}", order.Name));
				}
				return;
			}

			if (orderState == OrderState.Cancelled)
			{
				Print(string.Format("[{0}] 订单取消 | 名称={1} | comment={2} | error={3}",
					time, order.Name, comment, error));

				if (order == _stopOrder)   _stopOrder = null;
				if (order == _addOnOrder)  _addOnOrder = null;
				if (order == _addOn2Order) _addOn2Order = null;
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId,
			double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			string name = execution.Order.Name;
			string fromEntry = execution.Order.FromEntrySignal;

			Print(string.Format("[{0}] *** 成交 *** | 名称={1} 价格={2:F2} 数量={3} 方向={4} | FromEntry={5}",
				time, name, price, quantity, marketPosition, fromEntry));

			// ── 入场成交 ──

			// Stop主入场成交
			if (name == "MB_BO_Long" || name == "MB_BO_Short")
			{
				if (execution.Order.OrderState == OrderState.Filled && _state == MBBOState.StopPending)
				{
					_stopFillPrice = price;
					_state = MBBOState.EntryFilled;

					Print(string.Format("[{0}] Bar#{1} | MB#{2} Stop入场成交 @ {3:F2} | 方向={4}",
						time, CurrentBar, _mbId, price, _direction));

					PlaceAddOnOrder();
					PlaceAddOn2Order();
				}
				return;
			}

			// 加仓1成交
			if (name == "MB_BO_Long_AddOn" || name == "MB_BO_Short_AddOn")
			{
				if (execution.Order.OrderState == OrderState.Filled)
				{
					_addOn1Filled = true;

					Print(string.Format("[{0}] Bar#{1} | MB#{2} 加仓1成交 @ {3:F2}",
						time, CurrentBar, _mbId, price));
				}
				return;
			}

			// 加仓2成交
			if (name == "MB_BO_Long_AddOn2" || name == "MB_BO_Short_AddOn2")
			{
				if (execution.Order.OrderState == OrderState.Filled)
				{
					_addOn2Filled = true;

					Print(string.Format("[{0}] Bar#{1} | MB#{2} 加仓2成交 @ {3:F2}",
						time, CurrentBar, _mbId, price));

					UpdateAllTPsToBreakEven();
				}
				return;
			}

			// ── 出场成交（Profit target / Stop loss 由 managed approach 自动生成） ──

			if (name == "Profit target")
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 止盈成交 | Entry={3} @ {4:F2}",
					time, CurrentBar, _mbId, fromEntry, price));

				// 主入场止盈且加仓1未成交 → 取消加仓1和加仓2
				if ((fromEntry == "MB_BO_Long" || fromEntry == "MB_BO_Short") && !_addOn1Filled)
				{
					CancelAddOnOrder();
					CancelAddOn2Order();
				}
				// 加仓1止盈且加仓2未成交 → 取消加仓2
				if ((fromEntry == "MB_BO_Long_AddOn" || fromEntry == "MB_BO_Short_AddOn") && !_addOn2Filled)
					CancelAddOn2Order();
			}
			else if (name == "Stop loss")
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 止损成交 | Entry={3} @ {4:F2}",
					time, CurrentBar, _mbId, fromEntry, price));

				// 主入场止损且加仓1未成交 → 取消加仓1和加仓2
				if ((fromEntry == "MB_BO_Long" || fromEntry == "MB_BO_Short") && !_addOn1Filled)
				{
					CancelAddOnOrder();
					CancelAddOn2Order();
				}
				// 加仓1止损且加仓2未成交 → 取消加仓2
				if ((fromEntry == "MB_BO_Long_AddOn" || fromEntry == "MB_BO_Short_AddOn") && !_addOn2Filled)
					CancelAddOn2Order();
			}
		}

		protected override void OnPositionUpdate(Position position, double averagePrice,
			int quantity, MarketPosition marketPosition)
		{
			if (marketPosition == MarketPosition.Flat && _state == MBBOState.EntryFilled)
			{
				Print(string.Format("[{0}] Bar#{1} | MB#{2} 全部平仓完成",
					Time[0], CurrentBar, _mbId));

				// 加仓单已在OnExecutionUpdate中取消，此处仅重置状态
				TransitionToIdle("全部平仓");
			}
		}

		#endregion

		#region MB Invalidation

		private void CheckInvalidation()
		{
			double level161_8 = CalcLevel(1.618);
			double levelN61_8 = CalcLevel(-0.618);

			bool invalidUp   = High[0] >= level161_8;
			bool invalidDown = Low[0] <= levelN61_8;

			if (invalidUp || invalidDown)
			{
				string reason = invalidUp
					? string.Format("价格到达161.8%={0:F2}", level161_8)
					: string.Format("价格到达-61.8%={0:F2}", levelN61_8);

				Print(string.Format("[{0}] Bar#{1} | MB#{2} 失效 | {3}", Time[0], CurrentBar, _mbId, reason));

				CancelStopOrder();
				TransitionToIdle(reason);
			}
		}

		private void CheckLargerMBReplacement()
		{
			// 当前bar是否形成了更大的MB？
			bool isInsideBar = High[0] <= High[1] && Low[0] >= Low[1]
				&& !(High[0] == High[1] && Low[0] == Low[1]);

			if (!isInsideBar)
				return;

			double newHigh  = High[1];
			double newLow   = Low[1];
			double newRange = newHigh - newLow;

			if (newRange > _mbRange)
			{
				Print(string.Format("[{0}] Bar#{1} | 更大MB出现 | 旧MB#{2} Range={3:F2} → 新Range={4:F2}",
					Time[0], CurrentBar, _mbId, _mbRange, newRange));

				// 取消现有挂单
				CancelStopOrder();

				// 替换MB
				_mbHigh    = newHigh;
				_mbLow     = newLow;
				_mbRange   = newRange;
				_mbFormBar = CurrentBar;
				_mbId++;
				_state     = MBBOState.WaitingConfirmation;
				_direction = null;
				_stopOrder = null;

				Print(string.Format("[{0}] Bar#{1} | *** MB#{2} 替换 *** | High={3:F2} Low={4:F2} Range={5:F2}",
					Time[0], CurrentBar, _mbId, _mbHigh, _mbLow, _mbRange));

				// 新MB的确认K也可能在当前bar
				CheckConfirmationSignal();
			}
		}

		#endregion

		#region Session & Cleanup

		private void HandleOutOfSession()
		{
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				Print(string.Format("[{0}] Bar#{1} | 时段外平仓 | 方向={2} 数量={3}",
					Time[0], CurrentBar, Position.MarketPosition, Position.Quantity));

				if (Position.MarketPosition == MarketPosition.Long)
				{
					ExitLong("SessionClose", "MB_BO_Long");
					ExitLong("SessionClose", "MB_BO_Long_AddOn");
					ExitLong("SessionClose", "MB_BO_Long_AddOn2");
				}
				else
				{
					ExitShort("SessionClose", "MB_BO_Short");
					ExitShort("SessionClose", "MB_BO_Short_AddOn");
					ExitShort("SessionClose", "MB_BO_Short_AddOn2");
				}
			}

			CancelAllOrders();

			if (_state != MBBOState.Idle)
			{
				Print(string.Format("[{0}] Bar#{1} | 时段外MB#{2}失效", Time[0], CurrentBar, _mbId));
				TransitionToIdle("时段结束");
			}
		}

		private void FatalError(string reason)
		{
			Print(string.Format("[{0}] *** 策略因异常终止 *** | 原因: {1}", Time[0], reason));

			CancelAllOrders();

			if (Position.MarketPosition == MarketPosition.Long)
			{
				ExitLong("ErrorClose", "MB_BO_Long");
				ExitLong("ErrorClose", "MB_BO_Long_AddOn");
				ExitLong("ErrorClose", "MB_BO_Long_AddOn2");
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				ExitShort("ErrorClose", "MB_BO_Short");
				ExitShort("ErrorClose", "MB_BO_Short_AddOn");
				ExitShort("ErrorClose", "MB_BO_Short_AddOn2");
			}

			ResetAll();
			CloseStrategy(reason);
		}

		private void TransitionToIdle(string reason)
		{
			Print(string.Format("[{0}] Bar#{1} | MB#{2} → Idle | 原因: {3}",
				Time[0], CurrentBar, _mbId, reason));
			ResetAll();
		}

		private void ResetAll()
		{
			_state          = MBBOState.Idle;
			_mbHigh         = 0;
			_mbLow          = 0;
			_mbRange        = 0;
			_mbFormBar      = 0;
			_direction      = null;
			_confirmBarHigh = 0;
			_confirmBarLow  = 0;
			_stopOrder       = null;
			_addOnOrder      = null;
			_addOn2Order     = null;
			_stopFillPrice   = 0;
			_addOn1Filled    = false;
			_addOn2Filled    = false;
		}

		#endregion

		#region Order Management

		private void CancelStopOrder()
		{
			if (_stopOrder != null && (_stopOrder.OrderState == OrderState.Working
				|| _stopOrder.OrderState == OrderState.Accepted))
			{
				string name = _stopOrder.Name;
				CancelOrder(_stopOrder);
				Print(string.Format("[{0}] Bar#{1} | 取消Stop单 {2}", Time[0], CurrentBar, name));
			}
			_stopOrder = null;
		}

		private void CancelAddOnOrder()
		{
			if (_addOnOrder != null && (_addOnOrder.OrderState == OrderState.Working
				|| _addOnOrder.OrderState == OrderState.Accepted))
			{
				string name = _addOnOrder.Name;
				CancelOrder(_addOnOrder);
				Print(string.Format("[{0}] Bar#{1} | 取消加仓1单 {2}", Time[0], CurrentBar, name));
			}
			_addOnOrder = null;
		}

		private void CancelAddOn2Order()
		{
			if (_addOn2Order != null && (_addOn2Order.OrderState == OrderState.Working
				|| _addOn2Order.OrderState == OrderState.Accepted))
			{
				string name = _addOn2Order.Name;
				CancelOrder(_addOn2Order);
				Print(string.Format("[{0}] Bar#{1} | 取消加仓2单 {2}", Time[0], CurrentBar, name));
			}
			_addOn2Order = null;
		}

		private void CancelAllOrders()
		{
			CancelStopOrder();
			CancelAddOnOrder();
			CancelAddOn2Order();
		}

		#endregion

		#region Helpers

		private double CalcLevel(double pct)
		{
			return _mbLow + _mbRange * pct;
		}

		private double RoundToTick(double price)
		{
			return Instrument.MasterInstrument.RoundToTickSize(price);
		}

		private double FloorToTick(double price)
		{
			return Math.Floor(price / TickSize) * TickSize;
		}

		private double CeilToTick(double price)
		{
			return Math.Ceiling(price / TickSize) * TickSize;
		}

		private bool IsInTradeSession()
		{
			int offset = 8 - UtcOffsetHours;

			int startHour   = TradeStartTime / 10000;
			int startMinute = (TradeStartTime % 10000) / 100;
			int startSecond = TradeStartTime % 100;

			int endHour   = TradeEndTime / 10000;
			int endMinute = (TradeEndTime % 10000) / 100;
			int endSecond = TradeEndTime % 100;

			DateTime baseDate     = DateTime.Today;
			DateTime startUtc8    = baseDate.AddHours(startHour).AddMinutes(startMinute).AddSeconds(startSecond);
			DateTime endUtc8      = baseDate.AddHours(endHour).AddMinutes(endMinute).AddSeconds(endSecond);
			DateTime startExchange = startUtc8.AddHours(-offset);
			DateTime endExchange   = endUtc8.AddHours(-offset);

			int exchangeStart = startExchange.Hour * 10000 + startExchange.Minute * 100 + startExchange.Second;
			int exchangeEnd   = endExchange.Hour * 10000 + endExchange.Minute * 100 + endExchange.Second;
			int currentTime   = ToTime(Time[0]);

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
			string prefix = "MBBO" + _mbId + "_";

			// MB主体
			DrawLevel(prefix, 1.0,   "MB_High_100",  NeutralColor, DashStyleHelper.Solid,   2, startBar, endBar);
			DrawLevel(prefix, 0.0,   "MB_Low_0",     NeutralColor, DashStyleHelper.Solid,   2, startBar, endBar);
			DrawLevel(prefix, 0.5,   "MB_Mid_50",    NeutralColor, DashStyleHelper.DashDot, 1, startBar, endBar);

			// Trap Zone
			DrawLevel(prefix, 1.11,  "Trap_111",     Brushes.Orange, DashStyleHelper.Dash, 1, startBar, endBar);
			DrawLevel(prefix, 0.89,  "Trap_89",      Brushes.Orange, DashStyleHelper.Dash, 1, startBar, endBar);
			DrawLevel(prefix, 0.11,  "Trap_11",      Brushes.Orange, DashStyleHelper.Dash, 1, startBar, endBar);
			DrawLevel(prefix, -0.11, "Trap_N11",     Brushes.Orange, DashStyleHelper.Dash, 1, startBar, endBar);

			// LMT Zone
			DrawLevel(prefix, 0.79,  "LMT_79",       Brushes.DodgerBlue, DashStyleHelper.Dot, 1, startBar, endBar);
			DrawLevel(prefix, 0.66,  "LMT_66",       Brushes.DodgerBlue, DashStyleHelper.Dot, 1, startBar, endBar);
			DrawLevel(prefix, 0.33,  "LMT_33",       Brushes.DodgerBlue, DashStyleHelper.Dot, 1, startBar, endBar);
			DrawLevel(prefix, 0.21,  "LMT_21",       Brushes.DodgerBlue, DashStyleHelper.Dot, 1, startBar, endBar);

			// Shallow Target & SL
			DrawLevel(prefix, 1.23,  "SL_Short_123", BearColor, DashStyleHelper.Solid, 2, startBar, endBar);
			DrawLevel(prefix, -0.23, "SL_Long_N23",  BullColor, DashStyleHelper.Solid, 2, startBar, endBar);

			// TP Levels
			DrawLevel(prefix, 1.618, "TP_161",       BearColor, DashStyleHelper.DashDot, 1, startBar, endBar);
			DrawLevel(prefix, 2.0,   "TP_200",       BearColor, DashStyleHelper.DashDot, 1, startBar, endBar);
			DrawLevel(prefix, -0.618,"TP_N61",       BullColor, DashStyleHelper.DashDot, 1, startBar, endBar);
			DrawLevel(prefix, -1.0,  "TP_N100",      BullColor, DashStyleHelper.DashDot, 1, startBar, endBar);
		}

		private void DrawLevel(string prefix, double pct, string label, Brush color,
			DashStyleHelper dashStyle, int width, int startBar, int endBar)
		{
			double price = RoundToTick(CalcLevel(pct));
			string tag   = prefix + label;
			Draw.Line(this, tag, false, startBar, price, endBar, price, color, dashStyle, width);
		}

		#endregion

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "策略方向", Description = "选择做多、做空或双向交易", Order = 1, GroupName = "策略配置")]
		public MBBODirection StrategyDirection { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "交易开始时间", Description = "东八区交易开始时间(HHMMSS格式)", Order = 1, GroupName = "时段配置")]
		public int TradeStartTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "交易结束时间", Description = "东八区交易结束时间(HHMMSS格式)", Order = 2, GroupName = "时段配置")]
		public int TradeEndTime { get; set; }

		[NinjaScriptProperty]
		[Range(-12, 12)]
		[Display(Name = "交易所UTC偏移", Description = "交易所相对UTC的小时偏移(EST=-5, EDT=-4)", Order = 3, GroupName = "时段配置")]
		public int UtcOffsetHours { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "多头止盈", Description = "多头止盈百分比(默认1.618=161.8%)", Order = 1, GroupName = "止盈配置")]
		public double LongTPPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "空头止盈", Description = "空头止盈百分比(默认-0.618=-61.8%)", Order = 2, GroupName = "止盈配置")]
		public double ShortTPPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "启用加仓", Description = "是否启用LMT Zone加仓", Order = 1, GroupName = "加仓配置")]
		public bool EnableAddOn { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "多头加仓位", Description = "多头加仓百分比(默认0.79=79%)", Order = 2, GroupName = "加仓配置")]
		public double LongAddOnPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "空头加仓位", Description = "空头加仓百分比(默认0.21=21%)", Order = 3, GroupName = "加仓配置")]
		public double ShortAddOnPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "启用加仓2", Description = "是否启用第二加仓(保本目标)", Order = 4, GroupName = "加仓配置")]
		public bool EnableAddOn2 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "多头加仓2位", Description = "多头加仓2百分比(默认0.21=21%)", Order = 5, GroupName = "加仓配置")]
		public double LongAddOn2Pct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "空头加仓2位", Description = "空头加仓2百分比(默认0.79=79%)", Order = 6, GroupName = "加仓配置")]
		public double ShortAddOn2Pct { get; set; }

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
		[Display(Name = "多头颜色", Order = 3, GroupName = "可视化")]
		public Brush BullColor { get; set; }

		[Browsable(false)]
		public string BullColorSerializable
		{
			get { return Serialize.BrushToString(BullColor); }
			set { BullColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "空头颜色", Order = 4, GroupName = "可视化")]
		public Brush BearColor { get; set; }

		[Browsable(false)]
		public string BearColorSerializable
		{
			get { return Serialize.BrushToString(BearColor); }
			set { BearColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "中性颜色", Order = 5, GroupName = "可视化")]
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
