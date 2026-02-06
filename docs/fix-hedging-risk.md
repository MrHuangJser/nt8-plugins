# GroupTrade 对冲风险修复方案

## 概述

本文档描述 GroupTrade 跟单插件中三个可能导致账户对冲的风险问题及其修复方案。

| 问题 | 风险等级 | 修复优先级 |
|------|----------|------------|
| 反向比例配置 | 🔴 高 | P0 |
| 主从账户相同 | 🔴 高 | P1 |
| 映射丢失/挂单残留 | 🟡 中 | P2 |

---

## P0: 禁止反向比例配置

### 问题描述

`QuantityCalculator.cs` 中 `RatioMode.Ratio` 模式允许负数比例，当 `FixedRatio < 0` 时会触发 `reverseDirection = true`，导致从账户下单方向与主账户相反，形成对冲。

**问题代码** (`QuantityCalculator.cs:46-53`):
```csharp
case RatioMode.Ratio:
    rawQuantity = leaderQuantity * Math.Abs(config.FixedRatio);
    if (config.FixedRatio < 0)
    {
        reverseDirection = true;  // ← 危险：反向下单
    }
    break;
```

### 修复方案

1. **移除反向逻辑**: 删除 `reverseDirection` 相关代码
2. **强制正数比例**: 在计算时使用 `Math.Abs(config.FixedRatio)`
3. **UI 层校验**: 在 `AddFollowerDialog` 中限制 `FixedRatio` 输入范围 > 0
4. **配置加载校验**: 加载配置时自动修正负数为正数

### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `Core/QuantityCalculator.cs` | 移除 `reverseDirection` 逻辑，强制使用绝对值 |
| `Core/CopyEngine.cs` | 移除 `reverseDirection` 参数处理，删除 `ReverseOrderAction` 方法 |
| `UI/AddFollowerDialog.xaml.cs` | 添加 `FixedRatio > 0` 输入校验 |
| `Models/FollowerAccountConfig.cs` | 属性 setter 中强制正数 |

### 详细实现

#### QuantityCalculator.cs

```csharp
// 修改前
public (int quantity, bool reverseDirection) Calculate(...)

// 修改后
public int Calculate(...)  // 移除 reverseDirection 返回值
```

```csharp
// 修改前
case RatioMode.Ratio:
    rawQuantity = leaderQuantity * Math.Abs(config.FixedRatio);
    if (config.FixedRatio < 0)
    {
        reverseDirection = true;
    }
    break;

// 修改后
case RatioMode.Ratio:
    // 强制使用正数比例，防止反向下单导致对冲
    rawQuantity = leaderQuantity * Math.Max(0.01, Math.Abs(config.FixedRatio));
    break;
```

#### CopyEngine.cs

```csharp
// 修改前
var (quantity, reverseDirection) = _quantityCalculator.Calculate(...);
OrderAction orderAction = leaderOrder.OrderAction;
if (reverseDirection)
{
    orderAction = ReverseOrderAction(orderAction);
}

// 修改后
int quantity = _quantityCalculator.Calculate(...);
OrderAction orderAction = leaderOrder.OrderAction;
// 不再支持反向，直接使用主账户方向
```

---

## P1: 禁止主从账户相同

### 问题描述

当前代码没有校验主账户和从账户是否相同，用户可能误将主账户添加为从账户，导致：
- 同一账户收到自己订单的复制
- 产生双倍仓位或自我对冲

### 修复方案

1. **启动时校验**: `CopyEngine.Start()` 中检查从账户列表不包含主账户
2. **UI 层阻止**: 添加从账户时过滤掉已选为主账户的账户
3. **配置加载时清理**: 自动移除与主账户同名的从账户配置

### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `Core/CopyEngine.cs` | `Start()` 方法添加主从账户相同校验 |
| `UI/AddFollowerDialog.xaml.cs` | 账户列表过滤掉主账户 |
| `UI/GroupTradeWindow.xaml.cs` | 切换主账户时检查并移除冲突的从账户 |

### 详细实现

#### CopyEngine.cs - Start() 方法

```csharp
// 在获取从账户循环前添加
foreach (var followerConfig in config.FollowerAccounts.Where(f => f.IsEnabled))
{
    // 新增：跳过与主账户相同的配置
    if (followerConfig.AccountName == config.LeaderAccountName)
    {
        Log(GtLogLevel.Warning, "ENGINE",
            $"从账户 {followerConfig.AccountName} 与主账户相同，已自动跳过");
        followerConfig.IsEnabled = false;  // 自动禁用
        continue;
    }

    var account = GetAccountByName(followerConfig.AccountName);
    // ... 现有逻辑
}
```

#### AddFollowerDialog.xaml.cs

```csharp
// 加载账户列表时过滤
private void LoadAvailableAccounts()
{
    var accounts = Account.All
        .Where(a => a.Name != _leaderAccountName)  // 排除主账户
        .Where(a => !_existingFollowers.Contains(a.Name))  // 排除已添加的
        .ToList();

    AccountComboBox.ItemsSource = accounts;
}
```

---

## P2: 引擎停止时清理从账户挂单

### 问题描述

当前 `CopyEngine.Stop()` 仅清理内存中的映射关系，不会取消从账户的未成交挂单。这导致：
- 主账户可能已平仓或反向操作
- 从账户挂单后续成交，形成与主账户相反的仓位

### 修复方案

1. **停止时取消挂单**: 遍历所有活跃映射，取消对应的从账户订单
2. **添加配置选项**: `CancelFollowerOrdersOnStop` 控制是否自动取消（默认 true）
3. **日志记录**: 记录取消了哪些订单

### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `Core/CopyEngine.cs` | `Stop()` 方法添加取消挂单逻辑 |
| `Models/CopyConfiguration.cs` | 添加 `CancelFollowerOrdersOnStop` 配置项 |

### 详细实现

#### CopyEngine.cs - Stop() 方法

```csharp
public void Stop()
{
    if (!_isRunning)
        return;

    // 新增：取消所有从账户的活跃订单
    if (_config?.CancelFollowerOrdersOnStop ?? true)
    {
        CancelAllFollowerOrders();
    }

    // 取消订阅事件
    if (_leaderAccount != null)
    {
        _leaderAccount.OrderUpdate -= OnLeaderOrderUpdate;
    }

    // ... 现有清理逻辑
}

/// <summary>
/// 取消所有从账户的活跃订单
/// </summary>
private void CancelAllFollowerOrders()
{
    var activeMappings = _orderTracker.GetAllActiveMappings();
    if (activeMappings.Count == 0)
    {
        Log(GtLogLevel.Info, "ENGINE", "没有活跃的从订单需要取消");
        return;
    }

    Log(GtLogLevel.Info, "ENGINE", $"正在取消 {activeMappings.Count} 个从账户订单...");

    foreach (var mapping in activeMappings)
    {
        try
        {
            if (mapping.FollowerAccount == null)
                continue;

            // 通过订单名称查找最新的订单对象
            string expectedName = $"{COPY_TAG}{mapping.MasterOrderId}";
            Order orderToCancel = null;

            foreach (var order in mapping.FollowerAccount.Orders)
            {
                if (order.Name == expectedName && !Order.IsTerminalState(order.OrderState))
                {
                    orderToCancel = order;
                    break;
                }
            }

            if (orderToCancel != null)
            {
                mapping.FollowerAccount.Cancel(new[] { orderToCancel });
                Log(GtLogLevel.Info, "ENGINE",
                    $"已取消 {mapping.FollowerAccountName} 订单: {orderToCancel.OrderId}");
            }
        }
        catch (Exception ex)
        {
            Log(GtLogLevel.Error, "ENGINE",
                $"取消 {mapping.FollowerAccountName} 订单失败: {ex.Message}");
        }
    }
}
```

#### CopyConfiguration.cs

```csharp
#region 高级选项

/// <summary>
/// 停止引擎时是否取消从账户的所有挂单
/// </summary>
public bool CancelFollowerOrdersOnStop { get; set; } = true;

// ... 现有配置项

#endregion
```

---

## 测试用例

### P0: 反向比例配置

| 测试场景 | 预期结果 |
|----------|----------|
| 配置 `FixedRatio = -1.0` | 自动转为 `1.0`，正常同向复制 |
| 配置 `FixedRatio = -0.5` | 自动转为 `0.5`，正常同向复制 |
| UI 输入负数比例 | 输入框校验失败，提示错误 |

### P1: 主从账户相同

| 测试场景 | 预期结果 |
|----------|----------|
| 添加从账户时选择主账户 | 下拉列表中不显示主账户 |
| 切换主账户为已存在的从账户 | 自动禁用/移除该从账户配置 |
| 启动时检测到主从相同 | 日志警告，自动跳过该从账户 |

### P2: 停止时清理挂单

| 测试场景 | 预期结果 |
|----------|----------|
| 有活跃挂单时停止引擎 | 所有从账户挂单被取消，日志记录 |
| 无活跃订单时停止引擎 | 正常停止，日志显示无需取消 |
| 配置 `CancelFollowerOrdersOnStop = false` | 挂单保留不取消 |

---

## 实施顺序

1. **第一步**: 修复 P0（反向比例配置）
   - 修改 `QuantityCalculator.cs`
   - 修改 `CopyEngine.cs`
   - 修改 `FollowerAccountConfig.cs`

2. **第二步**: 修复 P1（主从账户相同）
   - 修改 `CopyEngine.cs`
   - 修改 `AddFollowerDialog.xaml.cs`
   - 修改 `GroupTradeWindow.xaml.cs`

3. **第三步**: 修复 P2（停止时清理挂单）
   - 修改 `CopyEngine.cs`
   - 修改 `CopyConfiguration.cs`

4. **第四步**: 测试验证
   - 按测试用例逐一验证
   - 提交代码
