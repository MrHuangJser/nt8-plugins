using System;
using System.Xml.Serialization;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Models
{
    /// <summary>
    /// 从账户配置
    /// </summary>
    [Serializable]
    public class FollowerAccountConfig
    {
        /// <summary>
        /// 账户名称
        /// </summary>
        public string AccountName { get; set; }

        /// <summary>
        /// 是否启用此从账户
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 比例计算模式
        /// </summary>
        public RatioMode RatioMode { get; set; } = RatioMode.ExactQuantity;

        /// <summary>
        /// 固定比例值（RatioMode.Ratio 时使用）
        /// 支持 -100 到 100，负数表示反向交易
        /// </summary>
        public double FixedRatio { get; set; } = 1.0;

        /// <summary>
        /// 预分配固定手数（RatioMode.PreAllocation 时使用）
        /// </summary>
        public int PreAllocatedQuantity { get; set; } = 1;

        /// <summary>
        /// 百分比变化值（RatioMode.PercentageChange 时使用）
        /// </summary>
        public double PercentageChange { get; set; } = 100;

        /// <summary>
        /// 最小手数限制
        /// </summary>
        public int MinQuantity { get; set; } = 1;

        /// <summary>
        /// 最大手数限制（0 表示不限制）
        /// </summary>
        public int MaxQuantity { get; set; } = 0;

        /// <summary>
        /// 跨合约目标 Symbol（如 "MNQ" 表示 NQ → MNQ）
        /// 为空表示不进行跨合约转换
        /// </summary>
        public string CrossOrderTarget { get; set; } = "";

        /// <summary>
        /// 是否为网络节点（非本地账户）
        /// </summary>
        public bool IsNetworkNode { get; set; } = false;

        /// <summary>
        /// 网络节点地址（IP:Port 格式）
        /// </summary>
        public string NetworkAddress { get; set; } = "";

        /// <summary>
        /// 备注
        /// </summary>
        public string Notes { get; set; } = "";

        /// <summary>
        /// 创建时间
        /// </summary>
        [XmlIgnore]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 获取显示名称
        /// </summary>
        [XmlIgnore]
        public string DisplayName => IsNetworkNode ? $"[NET] {NetworkAddress}" : AccountName;

        /// <summary>
        /// 获取比例/手数的显示值
        /// </summary>
        [XmlIgnore]
        public string RatioDisplayValue
        {
            get
            {
                switch (RatioMode)
                {
                    case RatioMode.ExactQuantity:
                        return "1:1";
                    case RatioMode.EqualQuantity:
                        return "Equal";
                    case RatioMode.Ratio:
                        return FixedRatio.ToString("F2");
                    case RatioMode.NetLiquidation:
                        return "NLV";
                    case RatioMode.AvailableMoney:
                        return "Avail";
                    case RatioMode.PercentageChange:
                        return $"{PercentageChange:F0}%";
                    case RatioMode.PreAllocation:
                        return PreAllocatedQuantity.ToString();
                    default:
                        return "-";
                }
            }
        }
    }
}
