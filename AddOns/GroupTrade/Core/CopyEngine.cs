using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;
// Alias to resolve LogLevel ambiguity with NinjaTrader.Cbi.LogLevel
using GtLogLevel = NinjaTrader.NinjaScript.AddOns.GroupTrade.Models.LogLevel;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Core
{
    /// <summary>
    /// 复制引擎：核心订单复制逻辑
    /// </summary>
    public class CopyEngine
    {
        #region Constants

        /// <summary>
        /// 复制订单标记（用于防循环）
        /// </summary>
        public const string COPY_TAG = "[GT]";

        /// <summary>
        /// 隐身模式标记（不可见）
        /// </summary>
        private const string STEALTH_TAG = "";

        #endregion

        #region Fields

        private Account _leaderAccount;
        private readonly List<Account> _followerAccounts = new List<Account>();
        private readonly OrderTracker _orderTracker;
        private readonly QuantityCalculator _quantityCalculator;
        private readonly FollowerGuard _followerGuard;
        private CopyConfiguration _config;
        private CopyStatus _status;

        private readonly object _syncLock = new object();
        private readonly HashSet<string> _processedOrderStates = new HashSet<string>();

        private bool _isRunning;

        #endregion

        #region Events

        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<LogEntry> OnLog;

        /// <summary>
        /// 状态变化事件
        /// </summary>
        public event Action<CopyStatus> OnStatusChanged;

        #endregion

        #region Constructor

        public CopyEngine()
        {
            _orderTracker = new OrderTracker();
            _quantityCalculator = new QuantityCalculator();
            _followerGuard = new FollowerGuard();
            _status = new CopyStatus();

            // 订阅 FollowerGuard 事件
            _followerGuard.OnGuardTriggered += OnGuardTriggered;
            _followerGuard.OnLog += OnGuardLog;
        }

        #endregion

        #region Properties

        public bool IsRunning => _isRunning;
        public CopyStatus Status => _status;
        public CopyConfiguration Configuration => _config;
        public FollowerGuard FollowerGuard => _followerGuard;

        #endregion

        #region Public Methods

        /// <summary>
        /// 启动复制引擎
        /// </summary>
        public bool Start(CopyConfiguration config)
        {
            if (_isRunning)
            {
                Log(GtLogLevel.Warning, "ENGINE", "复制引擎已在运行中");
                return false;
            }

            _config = config ?? throw new ArgumentNullException(nameof(config));

            // 验证配置
            if (string.IsNullOrEmpty(config.LeaderAccountName))
            {
                Log(GtLogLevel.Error, "ENGINE", "未配置主账户");
                return false;
            }

            if (config.EnabledFollowerCount == 0)
            {
                Log(GtLogLevel.Error, "ENGINE", "未配置启用的从账户");
                return false;
            }

            // 获取主账户
            _leaderAccount = GetAccountByName(config.LeaderAccountName);
            if (_leaderAccount == null)
            {
                Log(GtLogLevel.Error, "ENGINE", $"找不到主账户: {config.LeaderAccountName}");
                return false;
            }

            // 获取从账户
            _followerAccounts.Clear();
            foreach (var followerConfig in config.FollowerAccounts.Where(f => f.IsEnabled))
            {
                var account = GetAccountByName(followerConfig.AccountName);
                if (account != null)
                {
                    _followerAccounts.Add(account);
                    Log(GtLogLevel.Info, "ENGINE", $"已添加从账户: {followerConfig.AccountName}");
                }
                else
                {
                    Log(GtLogLevel.Warning, "ENGINE", $"找不到从账户: {followerConfig.AccountName}");
                }
            }

            if (_followerAccounts.Count == 0)
            {
                Log(GtLogLevel.Error, "ENGINE", "没有可用的从账户");
                return false;
            }

            // 订阅主账户订单更新事件
            _leaderAccount.OrderUpdate += OnLeaderOrderUpdate;

            // 重置状态
            _status.Reset();
            _status.IsRunning = true;
            _status.StartTime = DateTime.Now;
            _processedOrderStates.Clear();
            _orderTracker.Clear();

            _isRunning = true;

            // 启用 Follower Guard
            if (_config.EnableFollowerGuard)
            {
                _followerGuard.Enable(_config.GuardConfiguration);
                foreach (var followerConfig in config.FollowerAccounts.Where(f => f.IsEnabled))
                {
                    var account = _followerAccounts.FirstOrDefault(a => a.Name == followerConfig.AccountName);
                    if (account != null)
                    {
                        _followerGuard.RegisterFollower(followerConfig.AccountName, account);
                    }
                }
            }

            Log(GtLogLevel.Info, "ENGINE", $"复制引擎已启动 - 主账户: {config.LeaderAccountName}, 从账户: {_followerAccounts.Count} 个");
            OnStatusChanged?.Invoke(_status);

            return true;
        }

        /// <summary>
        /// 停止复制引擎
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            // 取消订阅事件
            if (_leaderAccount != null)
            {
                _leaderAccount.OrderUpdate -= OnLeaderOrderUpdate;
            }

            // 禁用 Follower Guard
            _followerGuard.Disable();
            _followerGuard.Clear();

            _isRunning = false;
            _status.IsRunning = false;

            // 清理
            _orderTracker.Clear();
            _processedOrderStates.Clear();
            _followerAccounts.Clear();
            _leaderAccount = null;

            Log(GtLogLevel.Info, "ENGINE", "复制引擎已停止");
            OnStatusChanged?.Invoke(_status);
        }

        #endregion

        #region Order Event Handlers

        /// <summary>
        /// 主账户订单更新事件处理
        /// </summary>
        private void OnLeaderOrderUpdate(object sender, OrderEventArgs e)
        {
            if (!_isRunning || e.Order == null)
                return;

            try
            {
                var order = e.Order;

                // 防循环：检查是否为复制订单
                if (IsCopiedOrder(order))
                    return;

                // 防重复：检查是否已处理过此状态
                string stateKey = $"{order.OrderId}_{e.OrderState}";
                lock (_syncLock)
                {
                    if (_processedOrderStates.Contains(stateKey))
                        return;
                    _processedOrderStates.Add(stateKey);
                }

                // 根据订单状态处理
                switch (e.OrderState)
                {
                    case OrderState.Submitted:
                    case OrderState.Accepted:
                        // 新订单：仅在 Submitted 时处理，避免重复
                        if (e.OrderState == OrderState.Submitted)
                        {
                            HandleNewOrder(order);
                        }
                        break;

                    case OrderState.Cancelled:
                        HandleOrderCancelled(order);
                        break;

                    case OrderState.ChangeSubmitted:
                        HandleOrderModified(order);
                        break;

                    case OrderState.Filled:
                        HandleOrderFilled(order, e.Quantity, e.AverageFillPrice);
                        break;

                    case OrderState.Rejected:
                        Log(GtLogLevel.Warning, "LEADER", $"主账户订单被拒绝: {order.Name}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log(GtLogLevel.Error, "ENGINE", $"处理订单事件异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理新订单
        /// </summary>
        private void HandleNewOrder(Order leaderOrder)
        {
            Log(GtLogLevel.Info, "COPY", $"检测到主账户新订单: {leaderOrder.OrderAction} {leaderOrder.Quantity} {leaderOrder.Instrument.FullName}");

            int enabledCount = _config.EnabledFollowerCount;

            foreach (var followerConfig in _config.FollowerAccounts.Where(f => f.IsEnabled))
            {
                var followerAccount = _followerAccounts.FirstOrDefault(a => a.Name == followerConfig.AccountName);
                if (followerAccount == null)
                    continue;

                try
                {
                    // 计算手数
                    var (quantity, reverseDirection) = _quantityCalculator.Calculate(
                        leaderOrder.Quantity,
                        followerConfig,
                        _leaderAccount,
                        followerAccount,
                        enabledCount
                    );

                    // 确定订单方向
                    OrderAction orderAction = leaderOrder.OrderAction;
                    if (reverseDirection)
                    {
                        orderAction = ReverseOrderAction(orderAction);
                    }

                    // 使用主账户相同的合约
                    Instrument instrument = leaderOrder.Instrument;

                    // 确定订单名称
                    string orderName = _config.StealthMode ? STEALTH_TAG : $"{COPY_TAG}{leaderOrder.OrderId}";

                    // 创建复制订单
                    Order copyOrder = followerAccount.CreateOrder(
                        instrument,
                        orderAction,
                        leaderOrder.OrderType,
                        OrderEntry.Manual,
                        leaderOrder.TimeInForce,
                        quantity,
                        leaderOrder.LimitPrice,
                        leaderOrder.StopPrice,
                        "", // OCO
                        orderName,
                        NinjaTrader.Core.Globals.MaxDate,
                        null
                    );

                    // 提交订单
                    followerAccount.Submit(new[] { copyOrder });

                    // 注册映射
                    var mapping = new OrderMapping
                    {
                        MasterOrderId = leaderOrder.OrderId,
                        MasterOrderName = leaderOrder.Name,
                        FollowerOrderId = copyOrder.OrderId,
                        FollowerAccountName = followerAccount.Name,
                        FollowerAccount = followerAccount,
                        FollowerOrder = copyOrder,
                        LastKnownState = OrderState.Submitted,
                        MasterQuantity = leaderOrder.Quantity,
                        FollowerQuantity = quantity,
                        InstrumentName = instrument.FullName,
                        OrderAction = orderAction
                    };

                    _orderTracker.RegisterMapping(mapping);

                    // 更新状态
                    _status.TotalCopiedOrders++;
                    _status.SuccessfulOrders++;
                    _status.ActiveMappings = _orderTracker.GetActiveCount();
                    _status.LastCopyTime = DateTime.Now;

                    Log(GtLogLevel.Info, "COPY", $"{followerAccount.Name}: {orderAction} {quantity} {instrument.FullName}");
                }
                catch (Exception ex)
                {
                    _status.FailedOrders++;
                    Log(GtLogLevel.Error, "COPY", $"复制到 {followerConfig.AccountName} 失败: {ex.Message}");
                }
            }

            OnStatusChanged?.Invoke(_status);
        }

        /// <summary>
        /// 处理订单取消
        /// </summary>
        private void HandleOrderCancelled(Order leaderOrder)
        {
            if (!_config.SyncPositionClose)
                return;

            var mappings = _orderTracker.GetFollowerMappings(leaderOrder.OrderId);
            if (mappings.Count == 0)
                return;

            Log(GtLogLevel.Info, "SYNC", $"主订单取消 → 同步取消 {mappings.Count} 个从订单");

            foreach (var mapping in mappings)
            {
                if (mapping.FollowerOrder != null && !Order.IsTerminalState(mapping.LastKnownState))
                {
                    try
                    {
                        mapping.FollowerAccount.Cancel(new[] { mapping.FollowerOrder });
                    }
                    catch (Exception ex)
                    {
                        Log(GtLogLevel.Error, "SYNC", $"取消从订单失败: {ex.Message}");
                    }
                }
            }

            _orderTracker.RemoveMapping(leaderOrder.OrderId);
            _status.ActiveMappings = _orderTracker.GetActiveCount();
            OnStatusChanged?.Invoke(_status);
        }

        /// <summary>
        /// 处理订单修改
        /// </summary>
        private void HandleOrderModified(Order leaderOrder)
        {
            if (!_config.SyncOrderModify)
                return;

            var mappings = _orderTracker.GetFollowerMappings(leaderOrder.OrderId);
            if (mappings.Count == 0)
                return;

            Log(GtLogLevel.Info, "SYNC", $"主订单改价 → 同步修改 {mappings.Count} 个从订单");

            foreach (var mapping in mappings)
            {
                if (mapping.FollowerOrder != null && !Order.IsTerminalState(mapping.LastKnownState))
                {
                    try
                    {
                        // 更新价格
                        mapping.FollowerOrder.LimitPriceChanged = leaderOrder.LimitPrice;
                        mapping.FollowerOrder.StopPriceChanged = leaderOrder.StopPrice;

                        mapping.FollowerAccount.Change(new[] { mapping.FollowerOrder });
                    }
                    catch (Exception ex)
                    {
                        Log(GtLogLevel.Error, "SYNC", $"修改从订单失败: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 处理订单成交
        /// </summary>
        private void HandleOrderFilled(Order leaderOrder, int filledQty, double avgPrice)
        {
            Log(GtLogLevel.Info, "FILL", $"主订单成交: {leaderOrder.OrderAction} {filledQty} @ {avgPrice:F2}");

            // 清理已完成的映射
            _orderTracker.CleanupCompletedMappings();
            _status.ActiveMappings = _orderTracker.GetActiveCount();
            OnStatusChanged?.Invoke(_status);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 检查是否为复制订单（防循环）
        /// </summary>
        private bool IsCopiedOrder(Order order)
        {
            if (order == null || string.IsNullOrEmpty(order.Name))
                return false;

            // 检查标记
            if (order.Name.StartsWith(COPY_TAG))
                return true;

            // 检查是否在从订单映射中
            return _orderTracker.IsFollowerOrder(order.OrderId);
        }

        /// <summary>
        /// 反转订单方向
        /// </summary>
        private OrderAction ReverseOrderAction(OrderAction action)
        {
            switch (action)
            {
                case OrderAction.Buy:
                    return OrderAction.SellShort;
                case OrderAction.BuyToCover:
                    return OrderAction.Sell;
                case OrderAction.Sell:
                    return OrderAction.BuyToCover;
                case OrderAction.SellShort:
                    return OrderAction.Buy;
                default:
                    return action;
            }
        }

        /// <summary>
        /// 根据名称获取账户
        /// </summary>
        private Account GetAccountByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            lock (Account.All)
            {
                return Account.All.FirstOrDefault(a => a.Name == name);
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        private void Log(GtLogLevel level, string category, string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message
            };

            // 写入 NinjaTrader 输出窗口
            NinjaTrader.Code.Output.Process($"[GroupTrade] [{category}] {message}", PrintTo.OutputTab1);

            // 触发事件
            OnLog?.Invoke(entry);
        }

        #endregion

        #region Guard Event Handlers

        /// <summary>
        /// 处理 Guard 触发事件
        /// </summary>
        private void OnGuardTriggered(GuardTriggerEventArgs args)
        {
            Log(GtLogLevel.Warning, "GUARD", $"{args.AccountName}: 保护触发 - {args.Reason} - {args.Details}");

            // 更新状态
            _status.GuardTriggerCount++;

            // 如果配置禁用从账户，从列表中移除
            if (args.DisableFollower)
            {
                var followerConfig = _config.FollowerAccounts.FirstOrDefault(f => f.AccountName == args.AccountName);
                if (followerConfig != null)
                {
                    followerConfig.IsEnabled = false;
                    Log(GtLogLevel.Info, "GUARD", $"{args.AccountName}: 已禁用跟随");
                }
            }

            OnStatusChanged?.Invoke(_status);
        }

        /// <summary>
        /// 处理 Guard 日志事件
        /// </summary>
        private void OnGuardLog(string message, GtLogLevel level)
        {
            Log(level, "GUARD", message);
        }

        #endregion
    }
}
