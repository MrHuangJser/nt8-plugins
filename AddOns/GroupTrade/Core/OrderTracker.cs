using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Core
{
    /// <summary>
    /// 订单追踪器：维护主从订单映射关系
    /// </summary>
    public class OrderTracker
    {
        // 主订单ID → 从订单映射列表
        private readonly ConcurrentDictionary<string, List<OrderMapping>> _masterToFollowers;

        // 从订单ID → 主订单ID（反向查找）
        private readonly ConcurrentDictionary<string, string> _followerToMaster;

        // 同步锁
        private readonly object _syncLock = new object();

        public OrderTracker()
        {
            _masterToFollowers = new ConcurrentDictionary<string, List<OrderMapping>>();
            _followerToMaster = new ConcurrentDictionary<string, string>();
        }

        /// <summary>
        /// 注册订单映射
        /// </summary>
        public void RegisterMapping(OrderMapping mapping)
        {
            if (mapping == null || string.IsNullOrEmpty(mapping.MasterOrderId))
                return;

            lock (_syncLock)
            {
                // 添加到主→从映射
                if (!_masterToFollowers.TryGetValue(mapping.MasterOrderId, out var list))
                {
                    list = new List<OrderMapping>();
                    _masterToFollowers[mapping.MasterOrderId] = list;
                }

                // 检查是否已存在
                var existing = list.FirstOrDefault(m =>
                    m.FollowerAccountName == mapping.FollowerAccountName);

                if (existing != null)
                {
                    // 更新现有映射
                    existing.FollowerOrderId = mapping.FollowerOrderId;
                    existing.FollowerOrder = mapping.FollowerOrder;
                    existing.LastKnownState = mapping.LastKnownState;
                    existing.LastUpdatedTime = DateTime.Now;
                }
                else
                {
                    list.Add(mapping);
                }

                // 添加反向映射
                if (!string.IsNullOrEmpty(mapping.FollowerOrderId))
                {
                    _followerToMaster[mapping.FollowerOrderId] = mapping.MasterOrderId;
                }
            }
        }

        /// <summary>
        /// 获取主订单对应的所有从订单映射
        /// </summary>
        public List<OrderMapping> GetFollowerMappings(string masterOrderId)
        {
            if (string.IsNullOrEmpty(masterOrderId))
                return new List<OrderMapping>();

            if (_masterToFollowers.TryGetValue(masterOrderId, out var list))
            {
                lock (_syncLock)
                {
                    return new List<OrderMapping>(list);
                }
            }

            return new List<OrderMapping>();
        }

        /// <summary>
        /// 根据从订单ID获取主订单ID
        /// </summary>
        public string GetMasterOrderId(string followerOrderId)
        {
            if (string.IsNullOrEmpty(followerOrderId))
                return null;

            _followerToMaster.TryGetValue(followerOrderId, out var masterId);
            return masterId;
        }

        /// <summary>
        /// 更新订单状态
        /// </summary>
        public void UpdateOrderState(string orderId, OrderState newState, bool isMasterOrder)
        {
            lock (_syncLock)
            {
                if (isMasterOrder)
                {
                    // 更新主订单状态（可选择同步更新从订单）
                    // 目前暂不处理
                }
                else
                {
                    // 更新从订单状态
                    var masterId = GetMasterOrderId(orderId);
                    if (masterId != null && _masterToFollowers.TryGetValue(masterId, out var list))
                    {
                        var mapping = list.FirstOrDefault(m => m.FollowerOrderId == orderId);
                        if (mapping != null)
                        {
                            mapping.LastKnownState = newState;
                            mapping.LastUpdatedTime = DateTime.Now;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 移除主订单的所有映射
        /// </summary>
        public void RemoveMapping(string masterOrderId)
        {
            if (string.IsNullOrEmpty(masterOrderId))
                return;

            lock (_syncLock)
            {
                if (_masterToFollowers.TryRemove(masterOrderId, out var list))
                {
                    // 移除反向映射
                    foreach (var mapping in list)
                    {
                        if (!string.IsNullOrEmpty(mapping.FollowerOrderId))
                        {
                            _followerToMaster.TryRemove(mapping.FollowerOrderId, out _);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 移除已完成的映射（终态订单）
        /// </summary>
        public int CleanupCompletedMappings()
        {
            int removed = 0;

            lock (_syncLock)
            {
                var keysToRemove = new List<string>();

                foreach (var kvp in _masterToFollowers)
                {
                    // 移除已完成的从订单映射
                    kvp.Value.RemoveAll(m =>
                    {
                        if (m.IsCompleted)
                        {
                            if (!string.IsNullOrEmpty(m.FollowerOrderId))
                            {
                                _followerToMaster.TryRemove(m.FollowerOrderId, out _);
                            }
                            removed++;
                            return true;
                        }
                        return false;
                    });

                    // 如果该主订单已没有从订单映射，标记删除
                    if (kvp.Value.Count == 0)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                // 移除空的主订单映射
                foreach (var key in keysToRemove)
                {
                    _masterToFollowers.TryRemove(key, out _);
                }
            }

            return removed;
        }

        /// <summary>
        /// 清空所有映射
        /// </summary>
        public void Clear()
        {
            lock (_syncLock)
            {
                _masterToFollowers.Clear();
                _followerToMaster.Clear();
            }
        }

        /// <summary>
        /// 获取活跃映射数量
        /// </summary>
        public int GetActiveCount()
        {
            int count = 0;
            lock (_syncLock)
            {
                foreach (var kvp in _masterToFollowers)
                {
                    count += kvp.Value.Count(m => !m.IsCompleted);
                }
            }
            return count;
        }

        /// <summary>
        /// 获取所有活跃映射
        /// </summary>
        public List<OrderMapping> GetAllActiveMappings()
        {
            var result = new List<OrderMapping>();

            lock (_syncLock)
            {
                foreach (var kvp in _masterToFollowers)
                {
                    result.AddRange(kvp.Value.Where(m => !m.IsCompleted));
                }
            }

            return result;
        }

        /// <summary>
        /// 检查主订单是否已有映射
        /// </summary>
        public bool HasMapping(string masterOrderId)
        {
            if (string.IsNullOrEmpty(masterOrderId))
                return false;

            return _masterToFollowers.ContainsKey(masterOrderId);
        }

        /// <summary>
        /// 检查是否为从订单（复制订单）
        /// </summary>
        public bool IsFollowerOrder(string orderId)
        {
            if (string.IsNullOrEmpty(orderId))
                return false;

            return _followerToMaster.ContainsKey(orderId);
        }
    }
}
