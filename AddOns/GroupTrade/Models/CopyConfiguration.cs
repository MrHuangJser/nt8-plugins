using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Core;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Services;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Models
{
    /// <summary>
    /// Group Trade 完整配置
    /// </summary>
    [Serializable]
    [XmlRoot("GroupTradeConfiguration")]
    public class CopyConfiguration
    {
        /// <summary>
        /// 配置版本号
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// 主账户（Leader）名称
        /// </summary>
        public string LeaderAccountName { get; set; } = "";

        /// <summary>
        /// 从账户配置列表
        /// </summary>
        public List<FollowerAccountConfig> FollowerAccounts { get; set; } = new List<FollowerAccountConfig>();

        /// <summary>
        /// 是否启用复制
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// 复制模式
        /// </summary>
        public CopyMode CopyMode { get; set; } = CopyMode.AllOrders;

        /// <summary>
        /// 默认比例模式（用于新添加的从账户）
        /// </summary>
        public RatioMode DefaultRatioMode { get; set; } = RatioMode.ExactQuantity;

        #region 同步选项

        /// <summary>
        /// 是否同步止损单
        /// </summary>
        public bool SyncStopLoss { get; set; } = true;

        /// <summary>
        /// 是否同步止盈单
        /// </summary>
        public bool SyncTakeProfit { get; set; } = true;

        /// <summary>
        /// 是否同步平仓操作
        /// </summary>
        public bool SyncPositionClose { get; set; } = true;

        /// <summary>
        /// 是否同步改单操作
        /// </summary>
        public bool SyncOrderModify { get; set; } = true;

        /// <summary>
        /// 是否同步 OCO 订单组
        /// </summary>
        public bool SyncOCO { get; set; } = true;

        #endregion

        #region 高级选项

        /// <summary>
        /// 隐身模式：隐藏订单中的复制标记
        /// </summary>
        public bool StealthMode { get; set; } = false;

        /// <summary>
        /// 启用 Follower Guard 保护
        /// </summary>
        public bool EnableFollowerGuard { get; set; } = false;

        /// <summary>
        /// Follower Guard 详细配置
        /// </summary>
        public GuardConfiguration GuardConfiguration { get; set; } = new GuardConfiguration();

        /// <summary>
        /// 连续亏损保护阈值
        /// </summary>
        public int GuardConsecutiveLossCount { get; set; } = 3;

        /// <summary>
        /// 日亏损限额 (USD)
        /// </summary>
        public double GuardDailyLossLimit { get; set; } = 500.0;

        /// <summary>
        /// 权益跌幅保护阈值 (%)
        /// </summary>
        public double GuardEquityDrawdownPercent { get; set; } = 5.0;

        #endregion

        #region 邮件通知

        /// <summary>
        /// 启用邮件通知
        /// </summary>
        public bool EnableEmailNotification { get; set; } = false;

        /// <summary>
        /// 邮件配置
        /// </summary>
        public EmailConfiguration EmailConfiguration { get; set; } = new EmailConfiguration();

        #endregion

        #region 网络设置

        /// <summary>
        /// 启用 Network Mode（局域网）
        /// </summary>
        public bool EnableNetworkMode { get; set; } = false;

        /// <summary>
        /// Network Mode 监听端口
        /// </summary>
        public int NetworkPort { get; set; } = 5678;

        /// <summary>
        /// 启用 Remote Mode（互联网）
        /// </summary>
        public bool EnableRemoteMode { get; set; } = false;

        /// <summary>
        /// Remote ID
        /// </summary>
        public string RemoteId { get; set; } = "";

        #endregion

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.Now;

        /// <summary>
        /// 获取启用的从账户数量
        /// </summary>
        [XmlIgnore]
        public int EnabledFollowerCount
        {
            get
            {
                int count = 0;
                foreach (var f in FollowerAccounts)
                {
                    if (f.IsEnabled) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 创建默认配置
        /// </summary>
        public static CopyConfiguration CreateDefault()
        {
            return new CopyConfiguration
            {
                Version = "1.0",
                LeaderAccountName = "",
                FollowerAccounts = new List<FollowerAccountConfig>(),
                IsEnabled = false,
                CopyMode = CopyMode.AllOrders,
                DefaultRatioMode = RatioMode.ExactQuantity,
                SyncStopLoss = true,
                SyncTakeProfit = true,
                SyncPositionClose = true,
                SyncOrderModify = true,
                SyncOCO = true,
                StealthMode = false,
                EnableFollowerGuard = false,
                GuardConfiguration = GuardConfiguration.CreateDefault(),
                EnableEmailNotification = false,
                EmailConfiguration = new EmailConfiguration(),
                LastModified = DateTime.Now
            };
        }
    }
}
