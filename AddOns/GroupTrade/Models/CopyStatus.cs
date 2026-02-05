using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Models
{
    /// <summary>
    /// 复制状态（用于 UI 绑定）
    /// </summary>
    public class CopyStatus : INotifyPropertyChanged
    {
        private bool _isRunning;
        private int _totalCopiedOrders;
        private int _successfulOrders;
        private int _failedOrders;
        private int _activeMappings;
        private string _lastError;
        private DateTime? _lastCopyTime;
        private DateTime? _startTime;
        private int _guardTriggerCount;

        /// <summary>
        /// 保护触发次数
        /// </summary>
        public int GuardTriggerCount
        {
            get => _guardTriggerCount;
            set { _guardTriggerCount = value; OnPropertyChanged(nameof(GuardTriggerCount)); }
        }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(StatusText)); }
        }

        /// <summary>
        /// 总复制订单数
        /// </summary>
        public int TotalCopiedOrders
        {
            get => _totalCopiedOrders;
            set { _totalCopiedOrders = value; OnPropertyChanged(nameof(TotalCopiedOrders)); OnPropertyChanged(nameof(SuccessRate)); }
        }

        /// <summary>
        /// 成功订单数
        /// </summary>
        public int SuccessfulOrders
        {
            get => _successfulOrders;
            set { _successfulOrders = value; OnPropertyChanged(nameof(SuccessfulOrders)); OnPropertyChanged(nameof(SuccessRate)); }
        }

        /// <summary>
        /// 失败订单数
        /// </summary>
        public int FailedOrders
        {
            get => _failedOrders;
            set { _failedOrders = value; OnPropertyChanged(nameof(FailedOrders)); OnPropertyChanged(nameof(SuccessRate)); }
        }

        /// <summary>
        /// 活跃映射数
        /// </summary>
        public int ActiveMappings
        {
            get => _activeMappings;
            set { _activeMappings = value; OnPropertyChanged(nameof(ActiveMappings)); }
        }

        /// <summary>
        /// 最后错误信息
        /// </summary>
        public string LastError
        {
            get => _lastError;
            set { _lastError = value; OnPropertyChanged(nameof(LastError)); }
        }

        /// <summary>
        /// 最后复制时间
        /// </summary>
        public DateTime? LastCopyTime
        {
            get => _lastCopyTime;
            set { _lastCopyTime = value; OnPropertyChanged(nameof(LastCopyTime)); OnPropertyChanged(nameof(LastCopyTimeText)); }
        }

        /// <summary>
        /// 启动时间
        /// </summary>
        public DateTime? StartTime
        {
            get => _startTime;
            set { _startTime = value; OnPropertyChanged(nameof(StartTime)); OnPropertyChanged(nameof(RunningTimeText)); }
        }

        /// <summary>
        /// 成功率
        /// </summary>
        public double SuccessRate
        {
            get
            {
                if (TotalCopiedOrders == 0) return 0;
                return (double)SuccessfulOrders / TotalCopiedOrders * 100;
            }
        }

        /// <summary>
        /// 状态文本
        /// </summary>
        public string StatusText => IsRunning ? "运行中" : "已停止";

        /// <summary>
        /// 最后复制时间文本
        /// </summary>
        public string LastCopyTimeText => LastCopyTime?.ToString("HH:mm:ss") ?? "-";

        /// <summary>
        /// 运行时间文本
        /// </summary>
        public string RunningTimeText
        {
            get
            {
                if (!StartTime.HasValue || !IsRunning) return "-";
                var span = DateTime.Now - StartTime.Value;
                if (span.TotalHours >= 1)
                    return $"{(int)span.TotalHours}h {span.Minutes}m";
                else
                    return $"{span.Minutes}m {span.Seconds}s";
            }
        }

        /// <summary>
        /// 从账户状态列表
        /// </summary>
        public ObservableCollection<FollowerStatus> FollowerStatuses { get; set; } = new ObservableCollection<FollowerStatus>();

        /// <summary>
        /// 日志消息列表
        /// </summary>
        public ObservableCollection<LogEntry> LogEntries { get; set; } = new ObservableCollection<LogEntry>();

        /// <summary>
        /// 重置状态
        /// </summary>
        public void Reset()
        {
            TotalCopiedOrders = 0;
            SuccessfulOrders = 0;
            FailedOrders = 0;
            ActiveMappings = 0;
            LastError = null;
            LastCopyTime = null;
            StartTime = null;
            FollowerStatuses.Clear();
            LogEntries.Clear();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 从账户状态
    /// </summary>
    public class FollowerStatus : INotifyPropertyChanged
    {
        private string _accountName;
        private int _copiedOrderCount;
        private int _successCount;
        private int _failedCount;
        private double _latencyMs;
        private string _connectionStatus;
        private string _lastOrderInfo;
        private bool _isProtected;

        public string AccountName
        {
            get => _accountName;
            set { _accountName = value; OnPropertyChanged(nameof(AccountName)); }
        }

        public int CopiedOrderCount
        {
            get => _copiedOrderCount;
            set { _copiedOrderCount = value; OnPropertyChanged(nameof(CopiedOrderCount)); }
        }

        public int SuccessCount
        {
            get => _successCount;
            set { _successCount = value; OnPropertyChanged(nameof(SuccessCount)); }
        }

        public int FailedCount
        {
            get => _failedCount;
            set { _failedCount = value; OnPropertyChanged(nameof(FailedCount)); }
        }

        public double LatencyMs
        {
            get => _latencyMs;
            set { _latencyMs = value; OnPropertyChanged(nameof(LatencyMs)); OnPropertyChanged(nameof(LatencyText)); }
        }

        public string LatencyText => LatencyMs > 0 ? $"{LatencyMs:F0}ms" : "-";

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(nameof(ConnectionStatus)); }
        }

        public string LastOrderInfo
        {
            get => _lastOrderInfo;
            set { _lastOrderInfo = value; OnPropertyChanged(nameof(LastOrderInfo)); }
        }

        public bool IsProtected
        {
            get => _isProtected;
            set { _isProtected = value; OnPropertyChanged(nameof(IsProtected)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 日志条目
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Category { get; set; } = "COPY";
        public string Message { get; set; }

        public string TimestampText => Timestamp.ToString("HH:mm:ss");

        public string FullText => $"[{TimestampText}] [{Category}] {Message}";
    }

    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
