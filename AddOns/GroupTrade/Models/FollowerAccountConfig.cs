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
        /// 必须为正数，防止反向交易导致对冲
        /// </summary>
        private double _fixedRatio = 1.0;
        public double FixedRatio
        {
            get => _fixedRatio;
            set => _fixedRatio = Math.Max(0.01, Math.Abs(value));
        }

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
        public string DisplayName => AccountName;

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
