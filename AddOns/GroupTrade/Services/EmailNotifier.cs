using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Core;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Services
{
    /// <summary>
    /// 邮件通知服务：发送保护触发、订单状态等通知邮件
    /// </summary>
    public class EmailNotifier : IDisposable
    {
        #region Fields

        private EmailConfiguration _config;
        private SmtpClient _smtpClient;
        private bool _isConfigured;
        private bool _disposed;

        #endregion

        #region Events

        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<string, LogLevel> OnLog;

        #endregion

        #region Constructor

        public EmailNotifier()
        {
            _config = new EmailConfiguration();
        }

        #endregion

        #region Properties

        public bool IsConfigured => _isConfigured;

        #endregion

        #region Public Methods

        /// <summary>
        /// 配置邮件服务
        /// </summary>
        public void Configure(EmailConfiguration config)
        {
            if (config == null)
                return;

            _config = config;

            try
            {
                _smtpClient?.Dispose();
                _smtpClient = new SmtpClient(_config.SmtpServer, _config.SmtpPort)
                {
                    EnableSsl = _config.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_config.Username, _config.Password),
                    Timeout = 30000 // 30 seconds
                };

                _isConfigured = true;
                Log("邮件服务已配置", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _isConfigured = false;
                Log($"邮件服务配置失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 发送保护触发通知
        /// </summary>
        public async Task SendGuardAlertAsync(GuardTriggerEventArgs args)
        {
            if (!_isConfigured || args == null)
                return;

            string subject = $"[GroupTrade] 保护触发警报 - {args.AccountName}";
            string body = BuildGuardAlertBody(args);

            await SendEmailAsync(subject, body);
        }

        /// <summary>
        /// 发送订单复制失败通知
        /// </summary>
        public async Task SendCopyFailedAlertAsync(string followerAccount, string reason, OrderMapping mapping)
        {
            if (!_isConfigured)
                return;

            string subject = $"[GroupTrade] 订单复制失败 - {followerAccount}";
            string body = BuildCopyFailedBody(followerAccount, reason, mapping);

            await SendEmailAsync(subject, body);
        }

        /// <summary>
        /// 发送每日统计报告
        /// </summary>
        public async Task SendDailyReportAsync(DailyReport report)
        {
            if (!_isConfigured || report == null)
                return;

            string subject = $"[GroupTrade] 每日报告 - {report.Date:yyyy-MM-dd}";
            string body = BuildDailyReportBody(report);

            await SendEmailAsync(subject, body);
        }

        /// <summary>
        /// 发送测试邮件
        /// </summary>
        public async Task<bool> SendTestEmailAsync()
        {
            if (!_isConfigured)
                return false;

            try
            {
                string subject = "[GroupTrade] 测试邮件";
                string body = "这是一封测试邮件。如果您收到这封邮件，说明邮件配置正确。\n\n" +
                              $"发送时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                await SendEmailAsync(subject, body);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 发送邮件
        /// </summary>
        private async Task SendEmailAsync(string subject, string body)
        {
            if (!_isConfigured || _smtpClient == null)
                return;

            try
            {
                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(_config.FromEmail, "GroupTrade Notifier");
                    message.To.Add(_config.ToEmail);
                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = false;

                    await _smtpClient.SendMailAsync(message);
                    Log($"邮件已发送: {subject}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Log($"邮件发送失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 构建保护触发邮件内容
        /// </summary>
        private string BuildGuardAlertBody(GuardTriggerEventArgs args)
        {
            return $@"GroupTrade 保护触发警报
========================================

账户: {args.AccountName}
触发原因: {GetReasonDescription(args.Reason)}
详细信息: {args.Details}
触发时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

执行动作:
- 平仓: {(args.FlattenPosition ? "是" : "否")}
- 禁用跟随: {(args.DisableFollower ? "是" : "否")}

请及时检查账户状态。

---
此邮件由 GroupTrade 自动发送";
        }

        /// <summary>
        /// 构建复制失败邮件内容
        /// </summary>
        private string BuildCopyFailedBody(string followerAccount, string reason, OrderMapping mapping)
        {
            string orderInfo = mapping != null
                ? $@"
原始订单信息:
- 主订单ID: {mapping.MasterOrderId}
- 合约: {mapping.Instrument}
- 方向: {mapping.Action}
- 数量: {mapping.MasterQuantity}"
                : "";

            return $@"GroupTrade 订单复制失败警报
========================================

从账户: {followerAccount}
失败原因: {reason}
发生时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
{orderInfo}

请检查账户状态和配置。

---
此邮件由 GroupTrade 自动发送";
        }

        /// <summary>
        /// 构建每日报告邮件内容
        /// </summary>
        private string BuildDailyReportBody(DailyReport report)
        {
            return $@"GroupTrade 每日交易报告
========================================

日期: {report.Date:yyyy-MM-dd}

交易统计:
- 复制订单总数: {report.TotalCopiedOrders}
- 成功: {report.SuccessfulOrders}
- 失败: {report.FailedOrders}
- 成功率: {report.SuccessRate:P1}

盈亏统计:
- 总盈亏: ${report.TotalPnL:F2}
- 盈利交易: {report.WinningTrades}
- 亏损交易: {report.LosingTrades}
- 胜率: {report.WinRate:P1}

保护触发: {report.GuardTriggerCount} 次

---
此邮件由 GroupTrade 自动发送";
        }

        /// <summary>
        /// 获取触发原因描述
        /// </summary>
        private string GetReasonDescription(GuardTriggerReason reason)
        {
            switch (reason)
            {
                case GuardTriggerReason.ConsecutiveLoss:
                    return "连续亏损";
                case GuardTriggerReason.DailyLossLimit:
                    return "日亏损限额";
                case GuardTriggerReason.EquityDrawdown:
                    return "权益跌幅过大";
                case GuardTriggerReason.PositionTimeout:
                    return "持仓时间超时";
                case GuardTriggerReason.OrderRejected:
                    return "订单连续被拒";
                default:
                    return reason.ToString();
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        private void Log(string message, LogLevel level)
        {
            OnLog?.Invoke(message, level);
            NinjaTrader.Code.Output.Process($"[GroupTrade] [Email] {message}", PrintTo.OutputTab1);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _smtpClient?.Dispose();
                _smtpClient = null;
            }

            _disposed = true;
        }

        #endregion
    }

    #region Models

    /// <summary>
    /// 邮件配置
    /// </summary>
    public class EmailConfiguration
    {
        public string SmtpServer { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromEmail { get; set; } = "";
        public string ToEmail { get; set; } = "";

        /// <summary>
        /// 是否启用保护触发通知
        /// </summary>
        public bool NotifyOnGuardTrigger { get; set; } = true;

        /// <summary>
        /// 是否启用复制失败通知
        /// </summary>
        public bool NotifyOnCopyFailed { get; set; } = true;

        /// <summary>
        /// 是否启用每日报告
        /// </summary>
        public bool EnableDailyReport { get; set; } = false;

        /// <summary>
        /// 每日报告发送时间 (24小时制)
        /// </summary>
        public int DailyReportHour { get; set; } = 17;

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(SmtpServer) &&
                   SmtpPort > 0 &&
                   !string.IsNullOrEmpty(Username) &&
                   !string.IsNullOrEmpty(Password) &&
                   !string.IsNullOrEmpty(FromEmail) &&
                   !string.IsNullOrEmpty(ToEmail);
        }
    }

    /// <summary>
    /// 每日报告数据
    /// </summary>
    public class DailyReport
    {
        public DateTime Date { get; set; }

        // 订单统计
        public int TotalCopiedOrders { get; set; }
        public int SuccessfulOrders { get; set; }
        public int FailedOrders { get; set; }
        public double SuccessRate => TotalCopiedOrders > 0
            ? (double)SuccessfulOrders / TotalCopiedOrders
            : 0;

        // 盈亏统计
        public double TotalPnL { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public double WinRate => (WinningTrades + LosingTrades) > 0
            ? (double)WinningTrades / (WinningTrades + LosingTrades)
            : 0;

        // 保护统计
        public int GuardTriggerCount { get; set; }
    }

    #endregion
}
