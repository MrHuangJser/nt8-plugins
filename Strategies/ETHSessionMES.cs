#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
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
		private DateTime _nextAICheckTime;
		private DateTime _nextUSOpenTime;
		private DateTime _nextUSCloseTime;
		private bool   _usOpenChecked;
		private bool   _hasAddedOn;
		private bool   _breakEvenMode;
		private int    _breakEvenCount;
		private double _entry1FillPrice;
		private Order  _entry1Order;
		private Order  _entry2Order;

		// AI 状态
		private bool   _aiChecked;
		private string _aiRecommendation;
		private string _aiReason;
		private ETHDirection _effectiveDirection;
		private bool   _aiGaveDirection;     // 本次交易 AI 是否给出了 LONG/SHORT
		private int    _aiTotal;             // AI 给出方向的总次数
		private int    _aiCorrect;           // AI 方向正确的次数（交易盈利）

		// 指标
		private ATR _atr;
		private EMA _ema;

		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"ETH时段MES日内交易策略 - 美盘开盘浮亏加仓 + AI方向分析";
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

				// AI 参数
				EnableAI			= false;
				AIApiKey			= "";
				AIModel				= "deepseek-chat";
				AILookbackDays		= 2;
				UseAIDirection		= false;

				// 回测
				EnableBacktest		= true;
			}
			else if (State == State.DataLoaded)
			{
				_initialized    = false;
				_breakEvenCount = 0;
				_aiRecommendation = "";
				_aiReason       = "";
				_aiTotal        = 0;
				_aiCorrect      = 0;

				// 初始化指标（AI 分析用）
				_atr = ATR(14);
				_ema = EMA(20);
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

			// 首次初始化
			if (!_initialized)
			{
				_initialized = true;
				_nextEntryTime   = ToNextExchangeTime(Time[0].AddSeconds(-1), EntryTimeUTC8);
				_nextAICheckTime = _nextEntryTime.AddMinutes(-5);
				_aiChecked       = false;
				_effectiveDirection = TradeDirection;
				Print(string.Format("[{0}] 策略初始化 | 下次入场={1} | AI检查={2}",
					Time[0], _nextEntryTime, EnableAI ? _nextAICheckTime.ToString() : "禁用"));
			}

			// === AI 分析（入场前5分钟触发） ===
			if (EnableAI
				&& Position.MarketPosition == MarketPosition.Flat
				&& !_aiChecked
				&& Time[0] >= _nextAICheckTime)
			{
				RunAIAnalysis();
			}

			// === 入场检查 ===
			if (Position.MarketPosition == MarketPosition.Flat && Time[0] >= _nextEntryTime)
			{
				// AI 建议跳过
				if (EnableAI && _aiChecked && _aiRecommendation == "SKIP")
				{
					Print(string.Format("[{0}] Bar#{1} | AI建议不交易 → 跳过今日",
						Time[0], CurrentBar));
					_nextEntryTime   = ToNextExchangeTime(Time[0], EntryTimeUTC8);
					_nextAICheckTime = _nextEntryTime.AddMinutes(-5);
					_aiChecked       = false;
					return;
				}

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
			string statsText = string.Format("打平次数: {0}", _breakEvenCount);
			if (EnableAI && _aiRecommendation.Length > 0)
			{
				statsText += string.Format("\nAI建议: {0}", _aiRecommendation);
				if (_aiTotal > 0)
					statsText += string.Format("\nAI正确率: {0}/{1} ({2:P1})",
						_aiCorrect, _aiTotal, (double)_aiCorrect / _aiTotal);
			}
			Draw.TextFixed(this, "BEStats", statsText, TextPosition.TopRight);
		}

		#region AI Analysis

		private void RunAIAnalysis()
		{
			_aiChecked = true;

			if (string.IsNullOrEmpty(AIApiKey))
			{
				_aiRecommendation = "ERROR";
				_aiReason = "未设置API Key";
				Print(string.Format("[{0}] AI分析跳过: 未设置API Key", Time[0]));
				return;
			}

			// 构建数据
			string prompt = BuildPrompt();

			Print(string.Format("[{0}] Bar#{1} | 调用AI分析...", Time[0], CurrentBar));

			// 调用 DeepSeek API
			string aiContent = CallDeepSeekAPI(prompt);

			// 解析结果
			ParseAIResponse(aiContent);

			// 记录 AI 是否给出了方向建议
			_aiGaveDirection = (_aiRecommendation == "LONG" || _aiRecommendation == "SHORT");

			// 根据 AI 结果决定方向
			if (UseAIDirection && _aiGaveDirection)
				_effectiveDirection = _aiRecommendation == "LONG" ? ETHDirection.Long : ETHDirection.Short;
			else
				_effectiveDirection = TradeDirection;

			Print(string.Format("[{0}] Bar#{1} | AI建议={2} | 执行方向={3} | 原因={4}",
				Time[0], CurrentBar, _aiRecommendation, _effectiveDirection, _aiReason));
		}

		private string BuildPrompt()
		{
			StringBuilder sb = new StringBuilder();

			// K线数据
			sb.AppendLine(string.Format("## MES K线数据（最近{0}天）", AILookbackDays));
			sb.AppendLine("时间 | 开 | 高 | 低 | 收 | 成交量");
			sb.AppendLine("--- | --- | --- | --- | --- | ---");

			// 计算需要回看的 bar 数量
			DateTime cutoff = Time[0].AddDays(-AILookbackDays);
			int maxBars = Math.Min(CurrentBar, 500); // 最多500根K线

			for (int i = maxBars; i >= 0; i--)
			{
				if (Time[i] < cutoff)
					continue;

				sb.AppendLine(string.Format("{0} | {1:F2} | {2:F2} | {3:F2} | {4:F2} | {5}",
					Time[i].ToString("MM/dd HH:mm"),
					Open[i], High[i], Low[i], Close[i], Volume[i]));
			}

			// 技术指标
			sb.AppendLine();
			sb.AppendLine("## 技术指标（当前值）");
			sb.AppendLine(string.Format("- ATR(14): {0:F2}", _atr[0]));
			sb.AppendLine(string.Format("- EMA(20): {0:F2}", _ema[0]));
			sb.AppendLine(string.Format("- 当前价格: {0:F2}", Close[0]));
			sb.AppendLine(string.Format("- 价格相对EMA20: {0}",
				Close[0] > _ema[0] ? "上方" : "下方"));

			sb.AppendLine();
			sb.AppendLine("请分析并给出今日交易方向建议。");

			return sb.ToString();
		}

		private string CallDeepSeekAPI(string userPrompt)
		{
			try
			{
				string systemPrompt = "你是一个专业的期货交易分析师。根据提供的MES（标普500微型期货）K线数据和技术指标，"
					+ "分析当前市场状态，给出今日交易方向建议。"
					+ "你必须在回复的第一行只输出以下三个词之一：LONG（做多）、SHORT（做空）、SKIP（不交易）。"
					+ "之后换行给出简短的分析理由（不超过50字）。";

				string jsonBody = BuildJsonBody(systemPrompt, userPrompt);

				var request = (HttpWebRequest)WebRequest.Create("https://api.deepseek.com/chat/completions");
				request.Method      = "POST";
				request.ContentType = "application/json; charset=utf-8";
				request.Headers.Add("Authorization", "Bearer " + AIApiKey);
				request.Timeout     = 30000;

				byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
				request.ContentLength = bodyBytes.Length;

				using (Stream reqStream = request.GetRequestStream())
				{
					reqStream.Write(bodyBytes, 0, bodyBytes.Length);
				}

				using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
				using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
				{
					string result = reader.ReadToEnd();
					return ExtractContentFromJson(result);
				}
			}
			catch (WebException ex)
			{
				string detail = "";
				if (ex.Response != null)
				{
					using (StreamReader r = new StreamReader(ex.Response.GetResponseStream()))
						detail = r.ReadToEnd();
				}
				Print(string.Format("[{0}] AI API错误: {1} | {2}", Time[0], ex.Message, detail));
				return "ERROR: " + ex.Message;
			}
			catch (Exception ex)
			{
				Print(string.Format("[{0}] AI调用异常: {1}", Time[0], ex.Message));
				return "ERROR: " + ex.Message;
			}
		}

		private string BuildJsonBody(string systemPrompt, string userPrompt)
		{
			// 手动构建 JSON，避免依赖外部 JSON 库
			StringBuilder sb = new StringBuilder();
			sb.Append("{");
			sb.Append("\"model\":\""); sb.Append(EscapeJson(AIModel)); sb.Append("\",");
			sb.Append("\"messages\":[");
			sb.Append("{\"role\":\"system\",\"content\":\""); sb.Append(EscapeJson(systemPrompt)); sb.Append("\"},");
			sb.Append("{\"role\":\"user\",\"content\":\""); sb.Append(EscapeJson(userPrompt)); sb.Append("\"}");
			sb.Append("],");
			sb.Append("\"stream\":false,");
			sb.Append("\"temperature\":0.3");
			sb.Append("}");
			return sb.ToString();
		}

		private string EscapeJson(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "";
			return s
				.Replace("\\", "\\\\")
				.Replace("\"", "\\\"")
				.Replace("\n", "\\n")
				.Replace("\r", "\\r")
				.Replace("\t", "\\t");
		}

		private string ExtractContentFromJson(string json)
		{
			// 从 DeepSeek/OpenAI 响应 JSON 中提取 content 字段
			// 格式: ..."content":"<text>"...
			string marker = "\"content\":\"";
			int start = json.LastIndexOf(marker);
			if (start < 0)
				return "ERROR: 无法解析响应";

			start += marker.Length;
			StringBuilder sb = new StringBuilder();
			bool escaped = false;

			for (int i = start; i < json.Length; i++)
			{
				char c = json[i];
				if (escaped)
				{
					if (c == 'n') sb.Append('\n');
					else if (c == 'r') sb.Append('\r');
					else if (c == 't') sb.Append('\t');
					else sb.Append(c);
					escaped = false;
				}
				else if (c == '\\')
				{
					escaped = true;
				}
				else if (c == '"')
				{
					break;
				}
				else
				{
					sb.Append(c);
				}
			}

			return sb.ToString();
		}

		private void ParseAIResponse(string content)
		{
			if (string.IsNullOrEmpty(content) || content.StartsWith("ERROR"))
			{
				_aiRecommendation = "ERROR";
				_aiReason = content ?? "空响应";
				return;
			}

			// 第一行应该是 LONG/SHORT/SKIP
			string[] lines = content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			string firstLine = lines[0].Trim().ToUpperInvariant();

			if (firstLine.Contains("LONG"))
				_aiRecommendation = "LONG";
			else if (firstLine.Contains("SHORT"))
				_aiRecommendation = "SHORT";
			else if (firstLine.Contains("SKIP"))
				_aiRecommendation = "SKIP";
			else
				_aiRecommendation = "ERROR";

			// 后续行作为理由
			_aiReason = lines.Length > 1 ? lines[1].Trim() : "";
			if (_aiReason.Length > 80)
				_aiReason = _aiReason.Substring(0, 80);
		}

		#endregion

		#region Entry

		private void PlaceEntry()
		{
			// 如果未经 AI 分析，使用默认方向
			if (!EnableAI || !_aiChecked)
				_effectiveDirection = TradeDirection;

			// MES: TickSize=0.25, PointValue=5, 每tick价值=$1.25
			double tickValue = TickSize * Instrument.MasterInstrument.PointValue;
			double tpTicks   = TakeProfitDollars / (InitialQty * tickValue);
			double slTicks   = StopLossDollars / (InitialQty * tickValue);

			string entryName = _effectiveDirection == ETHDirection.Long ? "ETH_Long" : "ETH_Short";

			SetProfitTarget(entryName, CalculationMode.Ticks, tpTicks);
			SetStopLoss(entryName, CalculationMode.Ticks, slTicks, false);

			if (_effectiveDirection == ETHDirection.Long)
				_entry1Order = EnterLong(InitialQty, entryName);
			else
				_entry1Order = EnterShort(InitialQty, entryName);

			// 计算美盘时间节点
			_nextUSOpenTime  = ToNextExchangeTime(Time[0], USOpenTimeUTC8);
			_nextUSCloseTime = ToNextExchangeTime(_nextUSOpenTime.AddSeconds(-1), USCloseTimeUTC8);
			_usOpenChecked   = false;
			_hasAddedOn      = false;

			Print(string.Format("[{0}] Bar#{1} | 开仓 {2} {3}手 | TP=${4} SL=${5} | AI={6} | USOpen={7} USClose={8}",
				Time[0], CurrentBar, _effectiveDirection, InitialQty,
				TakeProfitDollars, StopLossDollars,
				EnableAI ? _aiRecommendation : "禁用",
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

			// 判断打平模式
			_breakEvenMode = BreakEvenThreshold > 0 && Math.Abs(unrealizedPnL) >= BreakEvenThreshold;

			double estAddOnPrice = Close[0];
			double avgPrice      = (_entry1FillPrice + estAddOnPrice) / 2.0;
			int    totalQty      = InitialQty + AddOnQty;
			double pointValue    = Instrument.MasterInstrument.PointValue;
			double tpPoints      = _breakEvenMode ? 0 : TakeProfitDollars / (totalQty * pointValue);
			double slPoints      = StopLossDollars / (totalQty * pointValue);

			double tpPrice, slPrice;
			string entryName, addOnName;

			if (_effectiveDirection == ETHDirection.Long)
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

			SetProfitTarget(entryName, CalculationMode.Price, tpPrice);
			SetStopLoss(entryName, CalculationMode.Price, slPrice, false);
			SetProfitTarget(addOnName, CalculationMode.Price, tpPrice);
			SetStopLoss(addOnName, CalculationMode.Price, slPrice, false);

			if (_effectiveDirection == ETHDirection.Long)
				_entry2Order = EnterLong(AddOnQty, addOnName);
			else
				_entry2Order = EnterShort(AddOnQty, addOnName);

			Print(string.Format("[{0}] Bar#{1} | 加仓 {2} {3}手 | 模式={4} | 估算均价={5:F2} | TP={6:F2} SL={7:F2}",
				Time[0], CurrentBar, _effectiveDirection, AddOnQty,
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

			// 首仓成交
			if ((name == "ETH_Long" || name == "ETH_Short")
				&& execution.Order.OrderState == OrderState.Filled)
			{
				_entry1FillPrice = price;
				Print(string.Format("[{0}] 首仓成交 @ {1:F2}", time, price));
			}

			// 加仓成交 → 精确重算 TP/SL
			if ((name == "ETH_Long_AddOn" || name == "ETH_Short_AddOn")
				&& execution.Order.OrderState == OrderState.Filled)
			{
				double avgPrice   = (_entry1FillPrice + price) / 2.0;
				int    totalQty   = InitialQty + AddOnQty;
				double pointValue = Instrument.MasterInstrument.PointValue;
				double tpPoints   = _breakEvenMode ? 0 : TakeProfitDollars / (totalQty * pointValue);
				double slPoints   = StopLossDollars / (totalQty * pointValue);

				double tpPrice, slPrice;

				if (_effectiveDirection == ETHDirection.Long)
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

			// 止盈/止损成交
			if (name == "Profit target")
			{
				string fromEntry = execution.Order.FromEntrySignal;
				Print(string.Format("[{0}] 止盈成交 | Entry={1} @ {2:F2}", time, fromEntry, price));

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

				// 统计 AI 方向正确率
				if (EnableAI && _aiGaveDirection)
				{
					_aiTotal++;
					double realizedPnL = SystemPerformance.AllTrades.Count > 0
						? SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitCurrency
						: 0;
					// 盈利（含打平）视为方向正确
					if (realizedPnL >= 0)
						_aiCorrect++;

					Print(string.Format("[{0}] AI统计 | 建议={1} | 本次盈亏=${2:F2} | 正确率={3}/{4}={5:P1}",
						Time[0], _aiRecommendation, realizedPnL,
						_aiCorrect, _aiTotal, _aiTotal > 0 ? (double)_aiCorrect / _aiTotal : 0));
				}

				// 计算下次入场和 AI 检查时间
				_nextEntryTime   = ToNextExchangeTime(Time[0], EntryTimeUTC8);
				_nextAICheckTime = _nextEntryTime.AddMinutes(-5);
				_aiChecked       = false;
				_aiGaveDirection = false;
				_hasAddedOn      = false;
				_breakEvenMode   = false;
				_usOpenChecked   = false;
				_entry1Order     = null;
				_entry2Order     = null;

				Print(string.Format("[{0}] Bar#{1} | 全部平仓 | 下次入场={2}",
					Time[0], CurrentBar, _nextEntryTime));
			}
		}

		#endregion

		#region Helpers

		private DateTime ToNextExchangeTime(DateTime afterExchange, int utc8HHMMSS)
		{
			int offset = 8 - UtcOffsetHours;
			DateTime afterUtc8 = afterExchange.AddHours(offset);

			int hh = utc8HHMMSS / 10000;
			int mm = (utc8HHMMSS % 10000) / 100;
			int ss = utc8HHMMSS % 100;
			DateTime targetUtc8 = afterUtc8.Date.AddHours(hh).AddMinutes(mm).AddSeconds(ss);

			if (targetUtc8 <= afterUtc8)
				targetUtc8 = targetUtc8.AddDays(1);

			return targetUtc8.AddHours(-offset);
		}

		private double RoundToTick(double price)
		{
			return Instrument.MasterInstrument.RoundToTickSize(price);
		}

		#endregion

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "交易方向", Description = "做多或做空(AI禁用时使用)", Order = 1, GroupName = "策略配置")]
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
		[Display(Name = "打平浮亏门槛($)", Description = "浮亏超过此金额时加仓只求打平(0=禁用)", Order = 6, GroupName = "策略配置")]
		public double BreakEvenThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "止盈金额($)", Description = "止盈总金额(美元)", Order = 7, GroupName = "策略配置")]
		public double TakeProfitDollars { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "止损金额($)", Description = "止损总金额(美元)", Order = 8, GroupName = "策略配置")]
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
		[Display(Name = "启用AI分析", Description = "是否在入场前调用DeepSeek AI分析方向", Order = 1, GroupName = "AI配置")]
		public bool EnableAI { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "API Key", Description = "DeepSeek API Key", Order = 2, GroupName = "AI配置")]
		public string AIApiKey { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "模型", Description = "DeepSeek模型名称(deepseek-chat或deepseek-reasoner)", Order = 3, GroupName = "AI配置")]
		public string AIModel { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "回看天数", Description = "发送给AI的K线数据天数", Order = 4, GroupName = "AI配置")]
		public int AILookbackDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "AI决定方向", Description = "true=AI决定做多做空, false=仅显示建议", Order = 5, GroupName = "AI配置")]
		public bool UseAIDirection { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "启用回测", Description = "是否在历史数据上执行策略", Order = 1, GroupName = "回测配置")]
		public bool EnableBacktest { get; set; }

		#endregion
	}
}
