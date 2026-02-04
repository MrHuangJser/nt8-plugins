namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Models
{
    /// <summary>
    /// 手数比例计算模式（对标 Replikanto 7种模式）
    /// </summary>
    public enum RatioMode
    {
        /// <summary>
        /// 精确数量：从账户手数 = 主账户手数
        /// </summary>
        ExactQuantity,

        /// <summary>
        /// 均分数量：从账户手数 = 主账户手数 / 启用的从账户数量
        /// </summary>
        EqualQuantity,

        /// <summary>
        /// 固定比例：从账户手数 = 主账户手数 × 比例值（支持负数反向）
        /// </summary>
        Ratio,

        /// <summary>
        /// 净清算值比例：从账户手数 = 主账户手数 × (从账户净值 / 主账户净值)
        /// </summary>
        NetLiquidation,

        /// <summary>
        /// 可用资金比例：从账户手数 = 主账户手数 × (从账户可用 / 主账户可用)
        /// </summary>
        AvailableMoney,

        /// <summary>
        /// 百分比变化：增加/减少当前持仓的指定百分比
        /// </summary>
        PercentageChange,

        /// <summary>
        /// 预分配：使用预设的固定手数，忽略主账户手数
        /// </summary>
        PreAllocation
    }

    /// <summary>
    /// 复制模式
    /// </summary>
    public enum CopyMode
    {
        /// <summary>
        /// 复制所有订单类型
        /// </summary>
        AllOrders,

        /// <summary>
        /// 仅复制市价单成交（忽略限价/止损挂单）
        /// </summary>
        MarketOnly,

        /// <summary>
        /// 使用主账户的 ATM 策略管理从账户出场
        /// </summary>
        ATMCopy
    }
}
