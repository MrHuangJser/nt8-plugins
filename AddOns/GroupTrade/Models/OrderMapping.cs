using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Models
{
    /// <summary>
    /// 订单映射记录：主账户订单与从账户订单的对应关系
    /// </summary>
    public class OrderMapping
    {
        /// <summary>
        /// 主账户订单 ID
        /// </summary>
        public string MasterOrderId { get; set; }

        /// <summary>
        /// 主账户订单名称
        /// </summary>
        public string MasterOrderName { get; set; }

        /// <summary>
        /// 从账户订单 ID
        /// </summary>
        public string FollowerOrderId { get; set; }

        /// <summary>
        /// 从账户名称
        /// </summary>
        public string FollowerAccountName { get; set; }

        /// <summary>
        /// 从账户引用
        /// </summary>
        public Account FollowerAccount { get; set; }

        /// <summary>
        /// 从账户订单引用
        /// </summary>
        public Order FollowerOrder { get; set; }

        /// <summary>
        /// 最后已知的订单状态
        /// </summary>
        public OrderState LastKnownState { get; set; }

        /// <summary>
        /// 主账户订单手数
        /// </summary>
        public int MasterQuantity { get; set; }

        /// <summary>
        /// 从账户订单手数
        /// </summary>
        public int FollowerQuantity { get; set; }

        /// <summary>
        /// 合约名称
        /// </summary>
        public string InstrumentName { get; set; }

        /// <summary>
        /// 订单方向
        /// </summary>
        public OrderAction OrderAction { get; set; }

        /// <summary>
        /// 是否为跨合约复制
        /// </summary>
        public bool IsCrossOrder { get; set; }

        /// <summary>
        /// 跨合约目标 Symbol
        /// </summary>
        public string CrossOrderTarget { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdatedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 是否已完成（终态）
        /// </summary>
        public bool IsCompleted => Order.IsTerminalState(LastKnownState);

        /// <summary>
        /// 获取日志描述
        /// </summary>
        public string ToLogString()
        {
            string cross = IsCrossOrder ? $" → {CrossOrderTarget}" : "";
            return $"{FollowerAccountName}: {OrderAction} {FollowerQuantity} {InstrumentName}{cross} [{LastKnownState}]";
        }
    }
}
