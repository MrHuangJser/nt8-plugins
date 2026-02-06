using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Core
{
    /// <summary>
    /// 手数计算器：实现7种比例计算模式
    /// </summary>
    public class QuantityCalculator
    {
        /// <summary>
        /// 计算从账户应下的手数
        /// </summary>
        /// <param name="leaderQuantity">主账户手数</param>
        /// <param name="config">从账户配置</param>
        /// <param name="leaderAccount">主账户</param>
        /// <param name="followerAccount">从账户</param>
        /// <param name="enabledFollowerCount">启用的从账户总数（EqualQuantity模式使用）</param>
        /// <param name="currentPosition">当前持仓（PercentageChange模式使用）</param>
        /// <returns>计算后的手数</returns>
        public int Calculate(
            int leaderQuantity,
            FollowerAccountConfig config,
            Account leaderAccount,
            Account followerAccount,
            int enabledFollowerCount = 1,
            int currentPosition = 0)
        {
            double rawQuantity;

            switch (config.RatioMode)
            {
                case RatioMode.ExactQuantity:
                    // 精确数量：1:1 复制
                    rawQuantity = leaderQuantity;
                    break;

                case RatioMode.EqualQuantity:
                    // 均分数量：平均分配到所有从账户
                    if (enabledFollowerCount <= 0) enabledFollowerCount = 1;
                    rawQuantity = (double)leaderQuantity / enabledFollowerCount;
                    break;

                case RatioMode.Ratio:
                    // 固定比例：强制使用正数，防止反向下单导致对冲
                    rawQuantity = leaderQuantity * Math.Max(0.01, Math.Abs(config.FixedRatio));
                    break;

                case RatioMode.NetLiquidation:
                    // 净清算值比例
                    rawQuantity = CalculateByNetLiquidation(leaderQuantity, leaderAccount, followerAccount);
                    break;

                case RatioMode.AvailableMoney:
                    // 可用资金比例
                    rawQuantity = CalculateByAvailableMoney(leaderQuantity, leaderAccount, followerAccount);
                    break;

                case RatioMode.PercentageChange:
                    // 百分比变化：基于当前持仓
                    rawQuantity = Math.Abs(currentPosition * config.PercentageChange / 100.0);
                    break;

                case RatioMode.PreAllocation:
                    // 预分配：使用固定手数
                    rawQuantity = config.PreAllocatedQuantity;
                    break;

                default:
                    rawQuantity = leaderQuantity;
                    break;
            }

            // 四舍五入
            int quantity = (int)Math.Round(rawQuantity);

            // 应用最小手数限制
            if (quantity < config.MinQuantity)
            {
                quantity = config.MinQuantity;
            }

            // 应用最大手数限制
            if (config.MaxQuantity > 0 && quantity > config.MaxQuantity)
            {
                quantity = config.MaxQuantity;
            }

            // 确保至少为1
            if (quantity < 1)
            {
                quantity = 1;
            }

            return quantity;
        }

        /// <summary>
        /// 按净清算值比例计算
        /// </summary>
        private double CalculateByNetLiquidation(int leaderQuantity, Account leaderAccount, Account followerAccount)
        {
            if (leaderAccount == null || followerAccount == null)
                return leaderQuantity;

            try
            {
                double leaderNLV = leaderAccount.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                double followerNLV = followerAccount.Get(AccountItem.NetLiquidation, Currency.UsDollar);

                if (leaderNLV <= 0)
                {
                    // 主账户净值为0，回退到1:1
                    return leaderQuantity;
                }

                double ratio = followerNLV / leaderNLV;
                return leaderQuantity * ratio;
            }
            catch
            {
                // 获取账户信息失败，回退到1:1
                return leaderQuantity;
            }
        }

        /// <summary>
        /// 按可用资金比例计算
        /// </summary>
        private double CalculateByAvailableMoney(int leaderQuantity, Account leaderAccount, Account followerAccount)
        {
            if (leaderAccount == null || followerAccount == null)
                return leaderQuantity;

            try
            {
                double leaderAvail = leaderAccount.Get(AccountItem.BuyingPower, Currency.UsDollar);
                double followerAvail = followerAccount.Get(AccountItem.BuyingPower, Currency.UsDollar);

                if (leaderAvail <= 0)
                {
                    // 主账户可用资金为0，回退到1:1
                    return leaderQuantity;
                }

                double ratio = followerAvail / leaderAvail;
                return leaderQuantity * ratio;
            }
            catch
            {
                // 获取账户信息失败，回退到1:1
                return leaderQuantity;
            }
        }
    }
}
