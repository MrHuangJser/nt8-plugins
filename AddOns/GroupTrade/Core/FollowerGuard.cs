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
    /// 从账户保护服务：监控从账户状态，在异常情况下触发保护动作
    /// </summary>
    public class FollowerGuard
    {
        #region Fields

        private readonly Dictionary<string, FollowerGuardState> _followerStates;
        private readonly object _syncLock = new object();
        private GuardConfiguration _config;
        private bool _isEnabled;

        #endregion

        #region Events

        /// <summary>
        /// 保护触发事件
        /// </summary>
        public event Action<GuardTriggerEventArgs> OnGuardTriggered;

        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<string, GtLogLevel> OnLog;

        #endregion

        #region Constructor

        public FollowerGuard()
        {
            _followerStates = new Dictionary<string, FollowerGuardState>(StringComparer.OrdinalIgnoreCase);
            _config = GuardConfiguration.CreateDefault();
        }

        #endregion

        #region Properties

        public bool IsEnabled => _isEnabled;

        #endregion

        #region Public Methods

        /// <summary>
        /// 启用保护
        /// </summary>
        public void Enable(GuardConfiguration config)
        {
            _config = config ?? GuardConfiguration.CreateDefault();
            _isEnabled = true;
            Log($"Follower Guard 已启用", GtLogLevel.Info);
        }

        /// <summary>
        /// 禁用保护
        /// </summary>
        public void Disable()
        {
            _isEnabled = false;
            Log($"Follower Guard 已禁用", GtLogLevel.Info);
        }

        /// <summary>
        /// 注册从账户
        /// </summary>
        public void RegisterFollower(string accountName, Account account)
        {
            if (string.IsNullOrEmpty(accountName) || account == null)
                return;

            lock (_syncLock)
            {
                if (!_followerStates.ContainsKey(accountName))
                {
                    var state = new FollowerGuardState
                    {
                        AccountName = accountName,
                        Account = account,
                        StartingEquity = GetAccountEquity(account),
                        DailyStartEquity = GetAccountEquity(account),
                        LastResetDate = DateTime.Today
                    };
                    _followerStates[accountName] = state;
                }
            }
        }

        /// <summary>
        /// 移除从账户
        /// </summary>
        public void UnregisterFollower(string accountName)
        {
            lock (_syncLock)
            {
                _followerStates.Remove(accountName);
            }
        }

        /// <summary>
        /// 记录交易结果（用于连续亏损检测）
        /// </summary>
        public void RecordTradeResult(string accountName, double pnl)
        {
            if (!_isEnabled || string.IsNullOrEmpty(accountName))
                return;

            lock (_syncLock)
            {
                if (!_followerStates.TryGetValue(accountName, out var state))
                    return;

                state.TotalTrades++;

                if (pnl < 0)
                {
                    state.ConsecutiveLosses++;
                    state.DailyLoss += Math.Abs(pnl);
                    Log($"{accountName}: 亏损 ${Math.Abs(pnl):F2}, 连续亏损 {state.ConsecutiveLosses} 次", GtLogLevel.Warning);
                }
                else
                {
                    state.ConsecutiveLosses = 0; // 重置连续亏损计数
                }

                // 检查保护规则
                CheckGuardRules(state);
            }
        }

        /// <summary>
        /// 记录订单被拒
        /// </summary>
        public void RecordOrderRejected(string accountName)
        {
            if (!_isEnabled || string.IsNullOrEmpty(accountName))
                return;

            lock (_syncLock)
            {
                if (!_followerStates.TryGetValue(accountName, out var state))
                    return;

                state.ConsecutiveRejections++;
                Log($"{accountName}: 订单被拒, 连续 {state.ConsecutiveRejections} 次", GtLogLevel.Warning);

                CheckGuardRules(state);
            }
        }

        /// <summary>
        /// 记录订单成功
        /// </summary>
        public void RecordOrderSuccess(string accountName)
        {
            if (!_isEnabled || string.IsNullOrEmpty(accountName))
                return;

            lock (_syncLock)
            {
                if (_followerStates.TryGetValue(accountName, out var state))
                {
                    state.ConsecutiveRejections = 0; // 重置连续拒绝计数
                }
            }
        }

        /// <summary>
        /// 更新持仓时间
        /// </summary>
        public void UpdatePositionTime(string accountName, DateTime entryTime)
        {
            if (!_isEnabled || string.IsNullOrEmpty(accountName))
                return;

            lock (_syncLock)
            {
                if (_followerStates.TryGetValue(accountName, out var state))
                {
                    state.PositionEntryTime = entryTime;
                }
            }
        }

        /// <summary>
        /// 清除持仓时间
        /// </summary>
        public void ClearPositionTime(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
                return;

            lock (_syncLock)
            {
                if (_followerStates.TryGetValue(accountName, out var state))
                {
                    state.PositionEntryTime = null;
                }
            }
        }

        /// <summary>
        /// 定期检查（应由定时器调用）
        /// </summary>
        public void PeriodicCheck()
        {
            if (!_isEnabled)
                return;

            lock (_syncLock)
            {
                // 检查是否需要重置日统计
                if (DateTime.Today > _followerStates.Values.FirstOrDefault()?.LastResetDate)
                {
                    foreach (var state in _followerStates.Values)
                    {
                        state.DailyLoss = 0;
                        state.DailyStartEquity = GetAccountEquity(state.Account);
                        state.LastResetDate = DateTime.Today;
                    }
                }

                // 检查所有从账户
                foreach (var state in _followerStates.Values.ToList())
                {
                    if (!state.IsProtected)
                    {
                        CheckGuardRules(state);
                    }
                }
            }
        }

        /// <summary>
        /// 检查账户是否已触发保护
        /// </summary>
        public bool IsProtected(string accountName)
        {
            lock (_syncLock)
            {
                if (_followerStates.TryGetValue(accountName, out var state))
                {
                    return state.IsProtected;
                }
            }
            return false;
        }

        /// <summary>
        /// 重置账户保护状态
        /// </summary>
        public void ResetProtection(string accountName)
        {
            lock (_syncLock)
            {
                if (_followerStates.TryGetValue(accountName, out var state))
                {
                    state.IsProtected = false;
                    state.ProtectionReason = null;
                    state.ConsecutiveLosses = 0;
                    state.ConsecutiveRejections = 0;
                    state.DailyLoss = 0;
                    Log($"{accountName}: 保护状态已重置", GtLogLevel.Info);
                }
            }
        }

        /// <summary>
        /// 获取从账户状态
        /// </summary>
        public FollowerGuardState GetState(string accountName)
        {
            lock (_syncLock)
            {
                _followerStates.TryGetValue(accountName, out var state);
                return state;
            }
        }

        /// <summary>
        /// 清空所有状态
        /// </summary>
        public void Clear()
        {
            lock (_syncLock)
            {
                _followerStates.Clear();
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 检查保护规则
        /// </summary>
        private void CheckGuardRules(FollowerGuardState state)
        {
            if (state.IsProtected)
                return;

            GuardTriggerReason? reason = null;
            string details = "";

            // 规则1: 连续亏损
            if (_config.EnableConsecutiveLossGuard &&
                state.ConsecutiveLosses >= _config.ConsecutiveLossCount)
            {
                reason = GuardTriggerReason.ConsecutiveLoss;
                details = $"连续亏损 {state.ConsecutiveLosses} 次，超过阈值 {_config.ConsecutiveLossCount}";
            }

            // 规则2: 日亏损限额
            if (reason == null && _config.EnableDailyLossGuard &&
                state.DailyLoss >= _config.DailyLossLimit)
            {
                reason = GuardTriggerReason.DailyLossLimit;
                details = $"日亏损 ${state.DailyLoss:F2}，超过限额 ${_config.DailyLossLimit:F2}";
            }

            // 规则3: 权益跌幅
            if (reason == null && _config.EnableEquityDrawdownGuard)
            {
                double currentEquity = GetAccountEquity(state.Account);
                double drawdown = (state.StartingEquity - currentEquity) / state.StartingEquity * 100;

                if (drawdown >= _config.EquityDrawdownPercent)
                {
                    reason = GuardTriggerReason.EquityDrawdown;
                    details = $"权益跌幅 {drawdown:F1}%，超过阈值 {_config.EquityDrawdownPercent:F1}%";
                }
            }

            // 规则4: 持仓超时
            if (reason == null && _config.EnablePositionTimeoutGuard &&
                state.PositionEntryTime.HasValue)
            {
                var holdingTime = DateTime.Now - state.PositionEntryTime.Value;
                if (holdingTime.TotalMinutes >= _config.PositionTimeoutMinutes)
                {
                    reason = GuardTriggerReason.PositionTimeout;
                    details = $"持仓时间 {holdingTime.TotalMinutes:F0} 分钟，超过阈值 {_config.PositionTimeoutMinutes} 分钟";
                }
            }

            // 规则5: 订单连续被拒
            if (reason == null && _config.EnableOrderRejectedGuard &&
                state.ConsecutiveRejections >= _config.OrderRejectedCount)
            {
                reason = GuardTriggerReason.OrderRejected;
                details = $"订单连续被拒 {state.ConsecutiveRejections} 次，超过阈值 {_config.OrderRejectedCount}";
            }

            // 触发保护
            if (reason.HasValue)
            {
                TriggerProtection(state, reason.Value, details);
            }
        }

        /// <summary>
        /// 触发保护动作
        /// </summary>
        private void TriggerProtection(FollowerGuardState state, GuardTriggerReason reason, string details)
        {
            state.IsProtected = true;
            state.ProtectionReason = reason;
            state.ProtectionTime = DateTime.Now;

            Log($"[GUARD] {state.AccountName}: 触发保护 - {reason} - {details}", GtLogLevel.Warning);

            // 触发事件
            var args = new GuardTriggerEventArgs
            {
                AccountName = state.AccountName,
                Account = state.Account,
                Reason = reason,
                Details = details,
                FlattenPosition = _config.FlattenOnTrigger,
                DisableFollower = _config.DisableFollowerOnTrigger,
                SendEmailAlert = _config.SendEmailOnTrigger
            };

            OnGuardTriggered?.Invoke(args);

            // 执行平仓操作
            if (_config.FlattenOnTrigger)
            {
                FlattenAccount(state.Account);
            }
        }

        /// <summary>
        /// 平仓账户所有持仓
        /// </summary>
        private void FlattenAccount(Account account)
        {
            if (account == null)
                return;

            try
            {
                account.Flatten(new Instrument[0]);
                Log($"{account.Name}: 已执行平仓操作", GtLogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"{account.Name}: 平仓失败 - {ex.Message}", GtLogLevel.Error);
            }
        }

        /// <summary>
        /// 获取账户权益
        /// </summary>
        private double GetAccountEquity(Account account)
        {
            if (account == null)
                return 0;

            try
            {
                return account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        private void Log(string message, GtLogLevel level)
        {
            OnLog?.Invoke(message, level);
            NinjaTrader.Code.Output.Process($"[GroupTrade] [Guard] {message}", PrintTo.OutputTab1);
        }

        #endregion
    }

    #region Models

    /// <summary>
    /// 从账户保护状态
    /// </summary>
    public class FollowerGuardState
    {
        public string AccountName { get; set; }
        public Account Account { get; set; }

        // 统计数据
        public int TotalTrades { get; set; }
        public int ConsecutiveLosses { get; set; }
        public int ConsecutiveRejections { get; set; }
        public double DailyLoss { get; set; }
        public double StartingEquity { get; set; }
        public double DailyStartEquity { get; set; }
        public DateTime LastResetDate { get; set; }

        // 持仓时间
        public DateTime? PositionEntryTime { get; set; }

        // 保护状态
        public bool IsProtected { get; set; }
        public GuardTriggerReason? ProtectionReason { get; set; }
        public DateTime? ProtectionTime { get; set; }
    }

    /// <summary>
    /// 保护配置
    /// </summary>
    public class GuardConfiguration
    {
        // 连续亏损保护
        public bool EnableConsecutiveLossGuard { get; set; } = true;
        public int ConsecutiveLossCount { get; set; } = 3;

        // 日亏损限额保护
        public bool EnableDailyLossGuard { get; set; } = true;
        public double DailyLossLimit { get; set; } = 500.0;

        // 权益跌幅保护
        public bool EnableEquityDrawdownGuard { get; set; } = true;
        public double EquityDrawdownPercent { get; set; } = 5.0;

        // 持仓超时保护
        public bool EnablePositionTimeoutGuard { get; set; } = false;
        public int PositionTimeoutMinutes { get; set; } = 60;

        // 订单被拒保护
        public bool EnableOrderRejectedGuard { get; set; } = true;
        public int OrderRejectedCount { get; set; } = 5;

        // 触发动作
        public bool FlattenOnTrigger { get; set; } = true;
        public bool DisableFollowerOnTrigger { get; set; } = true;
        public bool SendEmailOnTrigger { get; set; } = false;

        public static GuardConfiguration CreateDefault()
        {
            return new GuardConfiguration();
        }
    }

    /// <summary>
    /// 保护触发原因
    /// </summary>
    public enum GuardTriggerReason
    {
        ConsecutiveLoss,     // 连续亏损
        DailyLossLimit,      // 日亏损限额
        EquityDrawdown,      // 权益跌幅
        PositionTimeout,     // 持仓超时
        OrderRejected        // 订单被拒
    }

    /// <summary>
    /// 保护触发事件参数
    /// </summary>
    public class GuardTriggerEventArgs : EventArgs
    {
        public string AccountName { get; set; }
        public Account Account { get; set; }
        public GuardTriggerReason Reason { get; set; }
        public string Details { get; set; }
        public bool FlattenPosition { get; set; }
        public bool DisableFollower { get; set; }
        public bool SendEmailAlert { get; set; }
    }

    #endregion
}
