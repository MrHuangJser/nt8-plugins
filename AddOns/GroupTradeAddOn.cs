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
            }
            else if (State == State.Configure)
            {
                // 初始化组件
                _copyEngine = new CopyEngine();
                _configManager = new ConfigManager();
            }
            else if (State == State.Terminated)
            {
                // 清理资源
                CleanupResources();
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            // 只在 Control Center 窗口添加菜单
            ControlCenter controlCenter = window as ControlCenter;
            if (controlCenter == null)
                return;

            try
            {
                // 查找 "New" 菜单
                _existingMenuItem = controlCenter.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
                if (_existingMenuItem == null)
                    return;

                // 创建 Group Trade 菜单项
                _menuItem = new NTMenuItem
                {
                    Header = "Group Trade",
                    Style = Application.Current.TryFindResource("MainMenuItem") as Style
                };
                _menuItem.Click += OnMenuItemClick;

                // 添加到 New 菜单
                _existingMenuItem.Items.Add(_menuItem);

                NinjaTrader.Code.Output.Process("[GroupTrade] 菜单已添加到 Control Center", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"[GroupTrade] 添加菜单失败: {ex.Message}", PrintTo.OutputTab1);
            }
        }

        protected override void OnWindowDestroyed(Window window)
        {
            // 移除菜单项
            if (_existingMenuItem != null && _menuItem != null)
            {
                if (_existingMenuItem.Items.Contains(_menuItem))
                {
                    _existingMenuItem.Items.Remove(_menuItem);
                }
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
                // 停止复制引擎
                _copyEngine?.Stop();
                _copyEngine = null;

                // 清理菜单
                if (_menuItem != null)
                {
                    _menuItem.Click -= OnMenuItemClick;
                    _menuItem = null;
                }

                _existingMenuItem = null;
                _configManager = null;

                NinjaTrader.Code.Output.Process("[GroupTrade] 资源已清理", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"[GroupTrade] 清理资源异常: {ex.Message}", PrintTo.OutputTab1);
            }
        }

        #endregion
    }
}
