using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Core;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Services;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.UI;

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Group Trade AddOn 入口类
    /// 实现多账户联动下单功能
    /// </summary>
    public class GroupTradeAddOn : AddOnBase
    {
        #region Fields

        private CopyEngine _copyEngine;
        private ConfigManager _configManager;
        private GroupTradeWindow _window;
        private NTMenuItem _menuItem;
        private NTMenuItem _existingMenuItem;

        #endregion

        #region AddOnBase Overrides

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Group Trade";
                Description = "多账户联动下单插件 - 订单复制器";
                NinjaTrader.Code.Output.Process("[GroupTrade] State.SetDefaults", PrintTo.OutputTab1);
            }
            else if (State == State.Configure)
            {
                // 初始化组件
                _copyEngine = new CopyEngine();
                _configManager = new ConfigManager();
                NinjaTrader.Code.Output.Process("[GroupTrade] State.Configure - 组件已初始化", PrintTo.OutputTab1);
            }
            else if (State == State.Terminated)
            {
                // 清理资源
                NinjaTrader.Code.Output.Process("[GroupTrade] State.Terminated - 开始清理", PrintTo.OutputTab1);
                CleanupResources();
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            NinjaTrader.Code.Output.Process($"[GroupTrade] OnWindowCreated 触发: 窗口类型={window?.GetType().Name}", PrintTo.OutputTab1);
            AddMenuItemToWindow(window);
        }

        protected override void OnWindowDestroyed(Window window)
        {
            // 窗口销毁时移除菜单
            if (window?.GetType().Name == "ControlCenter" && _existingMenuItem != null && _menuItem != null)
            {
                if (_existingMenuItem.Items.Contains(_menuItem))
                {
                    _existingMenuItem.Items.Remove(_menuItem);
                    NinjaTrader.Code.Output.Process("[GroupTrade] OnWindowDestroyed: 菜单已移除", PrintTo.OutputTab1);
                }
            }
        }

        private void AddMenuItemToWindow(Window window)
        {
            // 只在 Control Center 窗口添加菜单
            if (window?.GetType().Name != "ControlCenter")
            {
                return;
            }

            try
            {
                // 查找 "New" 菜单
                var newMenuItem = window.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
                if (newMenuItem == null)
                {
                    NinjaTrader.Code.Output.Process("[GroupTrade] 错误: 未找到 ControlCenterMenuItemNew", PrintTo.OutputTab1);
                    return;
                }

                // ===== 第一轮：初始诊断 =====
                NinjaTrader.Code.Output.Process($"[GroupTrade] 清理前：New菜单中共有 {newMenuItem.Items.Count} 个子项", PrintTo.OutputTab1);

                // ===== 强力清理所有 "Group Trade" 菜单（不管 _menuItem 状态）=====
                var itemsToRemove = newMenuItem.Items.Cast<object>()
                    .Where(item =>
                    {
                        var ntMenuItem = item as NTMenuItem;
                        if (ntMenuItem == null) return false;

                        var header = ntMenuItem.Header?.ToString();
                        return header == "Group Trade";
                    })
                    .Cast<NTMenuItem>()
                    .ToList();

                NinjaTrader.Code.Output.Process($"[GroupTrade] 检测到 {itemsToRemove.Count} 个 'Group Trade' 菜单项需要清理", PrintTo.OutputTab1);

                if (itemsToRemove.Count > 0)
                {
                    foreach (var item in itemsToRemove)
                    {
                        try
                        {
                            // 尝试多种方式取消事件订阅
                            var clickEvent = typeof(NTMenuItem).GetEvent("Click");
                            if (clickEvent != null)
                            {
                                var removeMethod = clickEvent.GetRemoveMethod();
                                try { removeMethod.Invoke(item, new object[] { (RoutedEventHandler)OnMenuItemClick }); } catch { }
                            }

                            newMenuItem.Items.Remove(item);
                            NinjaTrader.Code.Output.Process($"[GroupTrade] ✓ 已移除一个旧菜单项", PrintTo.OutputTab1);
                        }
                        catch (Exception ex)
                        {
                            NinjaTrader.Code.Output.Process($"[GroupTrade] 移除菜单项失败: {ex.Message}", PrintTo.OutputTab1);
                        }
                    }
                }

                // ===== 检查是否需要添加新菜单 =====
                if (_menuItem != null)
                {
                    NinjaTrader.Code.Output.Process("[GroupTrade] 本实例的菜单已存在，跳过重复添加", PrintTo.OutputTab1);
                    return;
                }

                // ===== 创建新的 Group Trade 菜单项 =====
                _menuItem = new NTMenuItem
                {
                    Header = "Group Trade",
                    Style = Application.Current.TryFindResource("MainMenuItem") as Style
                };
                _menuItem.Click += OnMenuItemClick;

                // 添加到 New 菜单
                newMenuItem.Items.Add(_menuItem);
                _existingMenuItem = newMenuItem;

                NinjaTrader.Code.Output.Process($"[GroupTrade] ✓ 新菜单已添加 (当前菜单总数: {newMenuItem.Items.Count})", PrintTo.OutputTab1);

                // ===== 第二轮：添加后验证 =====
                var groupTradeCount = newMenuItem.Items.Cast<object>()
                    .OfType<NTMenuItem>()
                    .Count(item => item.Header?.ToString() == "Group Trade");

                NinjaTrader.Code.Output.Process($"[GroupTrade] 添加后验证：菜单中现有 {groupTradeCount} 个 'Group Trade' 项", PrintTo.OutputTab1);

                if (groupTradeCount > 1)
                {
                    NinjaTrader.Code.Output.Process($"[GroupTrade] ⚠️ 警告：检测到 {groupTradeCount} 个 'Group Trade' 菜单！", PrintTo.OutputTab1);
                }
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"[GroupTrade] 添加菜单失败: {ex.Message}\n{ex.StackTrace}", PrintTo.OutputTab1);
            }
        }

        #endregion

        #region Event Handlers

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果窗口已存在，激活它
                if (_window != null && _window.IsLoaded)
                {
                    _window.Activate();
                    return;
                }

                // 创建并显示窗口
                _window = new GroupTradeWindow(_copyEngine, _configManager);
                _window.Closed += (s, args) => _window = null;
                _window.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开 Group Trade 窗口失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Private Methods

        private void CleanupResources()
        {
            try
            {
                NinjaTrader.Code.Output.Process("[GroupTrade] 开始清理资源...", PrintTo.OutputTab1);

                // 停止复制引擎（非UI对象，可以直接清理）
                _copyEngine?.Stop();
                _copyEngine = null;
                _configManager = null;

                // ===== 修复：同步方式清理UI对象 =====
                if (_menuItem != null || _existingMenuItem != null)
                {
                    var dispatcher = Application.Current?.Dispatcher;

                    if (dispatcher != null && !dispatcher.CheckAccess())
                    {
                        // 如果不在UI线程，使用Invoke同步等待
                        dispatcher.Invoke(() =>
                        {
                            CleanupMenuItems();
                        });
                    }
                    else
                    {
                        // 已经在UI线程，直接清理
                        CleanupMenuItems();
                    }
                }

                NinjaTrader.Code.Output.Process("[GroupTrade] ✓ 资源清理完成", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"[GroupTrade] 清理资源异常: {ex.Message}", PrintTo.OutputTab1);
            }
        }

        private void CleanupMenuItems()
        {
            try
            {
                if (_menuItem != null)
                {
                    _menuItem.Click -= OnMenuItemClick;

                    if (_existingMenuItem != null && _existingMenuItem.Items.Contains(_menuItem))
                    {
                        _existingMenuItem.Items.Remove(_menuItem);
                    }

                    _menuItem = null;
                }

                _existingMenuItem = null;
                NinjaTrader.Code.Output.Process("[GroupTrade] ✓ UI菜单项已清理", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"[GroupTrade] UI清理异常: {ex.Message}", PrintTo.OutputTab1);
            }
        }

        #endregion
    }
}
