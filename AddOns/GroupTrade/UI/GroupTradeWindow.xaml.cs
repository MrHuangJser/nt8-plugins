using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Core;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Services;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.UI
{
    /// <summary>
    /// GroupTradeWindow.xaml 的交互逻辑
    /// </summary>
    public partial class GroupTradeWindow : Window
    {
        #region Fields

        private readonly CopyEngine _copyEngine;
        private readonly ConfigManager _configManager;
        private CopyConfiguration _config;
        private ObservableCollection<FollowerAccountConfig> _followerConfigs;

        #endregion

        #region Constructor

        public GroupTradeWindow(CopyEngine copyEngine, ConfigManager configManager)
        {
            InitializeComponent();

            _copyEngine = copyEngine ?? throw new ArgumentNullException(nameof(copyEngine));
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));

            // 订阅引擎事件
            _copyEngine.OnLog += OnEngineLog;
            _copyEngine.OnStatusChanged += OnEngineStatusChanged;

            // 加载配置
            LoadConfiguration();

            // 刷新账户列表
            RefreshAccountList();

            // 更新 UI 状态
            UpdateUIState();
        }

        #endregion

        #region Configuration

        private void LoadConfiguration()
        {
            _config = _configManager.Load();

            // 绑定从账户列表
            _followerConfigs = new ObservableCollection<FollowerAccountConfig>(_config.FollowerAccounts);
            FollowerGrid.ItemsSource = _followerConfigs;

            // 设置复制模式
            switch (_config.CopyMode)
            {
                case CopyMode.AllOrders:
                    ModeAllOrders.IsChecked = true;
                    break;
                case CopyMode.MarketOnly:
                    ModeMarketOnly.IsChecked = true;
                    break;
                case CopyMode.ATMCopy:
                    ModeATMCopy.IsChecked = true;
                    break;
            }

            // 设置同步选项
            SyncStopLoss.IsChecked = _config.SyncStopLoss;
            SyncTakeProfit.IsChecked = _config.SyncTakeProfit;
            SyncClose.IsChecked = _config.SyncPositionClose;
            SyncModify.IsChecked = _config.SyncOrderModify;
            SyncOCO.IsChecked = _config.SyncOCO;

            // 设置高级选项
            StealthMode.IsChecked = _config.StealthMode;
            FollowerGuard.IsChecked = _config.EnableFollowerGuard;
        }

        private void SaveConfiguration()
        {
            // 更新配置对象
            _config.LeaderAccountName = LeaderAccountCombo.SelectedItem?.ToString() ?? "";
            _config.FollowerAccounts = _followerConfigs.ToList();

            // 复制模式
            if (ModeAllOrders.IsChecked == true)
                _config.CopyMode = CopyMode.AllOrders;
            else if (ModeMarketOnly.IsChecked == true)
                _config.CopyMode = CopyMode.MarketOnly;
            else if (ModeATMCopy.IsChecked == true)
                _config.CopyMode = CopyMode.ATMCopy;

            // 同步选项
            _config.SyncStopLoss = SyncStopLoss.IsChecked == true;
            _config.SyncTakeProfit = SyncTakeProfit.IsChecked == true;
            _config.SyncPositionClose = SyncClose.IsChecked == true;
            _config.SyncOrderModify = SyncModify.IsChecked == true;
            _config.SyncOCO = SyncOCO.IsChecked == true;

            // 高级选项
            _config.StealthMode = StealthMode.IsChecked == true;
            _config.EnableFollowerGuard = FollowerGuard.IsChecked == true;

            // 保存到文件
            _configManager.Save(_config);
        }

        #endregion

        #region Account Management

        private void RefreshAccountList()
        {
            LeaderAccountCombo.Items.Clear();

            lock (Account.All)
            {
                foreach (var account in Account.All)
                {
                    LeaderAccountCombo.Items.Add(account.Name);
                }
            }

            // 选中之前保存的主账户
            if (!string.IsNullOrEmpty(_config.LeaderAccountName))
            {
                LeaderAccountCombo.SelectedItem = _config.LeaderAccountName;
            }
            else if (LeaderAccountCombo.Items.Count > 0)
            {
                LeaderAccountCombo.SelectedIndex = 0;
            }
        }

        private void UpdateLeaderAccountInfo()
        {
            string accountName = LeaderAccountCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(accountName))
            {
                LeaderEquityText.Text = "$0.00";
                LeaderPositionText.Text = "-";
                return;
            }

            try
            {
                Account account;
                lock (Account.All)
                {
                    account = Account.All.FirstOrDefault(a => a.Name == accountName);
                }

                if (account != null)
                {
                    double equity = account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                    LeaderEquityText.Text = $"${equity:N2}";

                    // 获取持仓信息
                    var positions = account.Positions;
                    if (positions != null && positions.Count > 0)
                    {
                        var posInfo = string.Join(", ", positions.Select(p => $"{p.Quantity} {p.Instrument.MasterInstrument.Name}"));
                        LeaderPositionText.Text = posInfo;
                    }
                    else
                    {
                        LeaderPositionText.Text = "无持仓";
                    }
                }
            }
            catch
            {
                LeaderEquityText.Text = "$0.00";
                LeaderPositionText.Text = "-";
            }
        }

        #endregion

        #region UI State

        private void UpdateUIState()
        {
            bool isRunning = _copyEngine.IsRunning;

            // 更新状态指示
            StatusIndicator.Background = isRunning
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))   // Green
                : new SolidColorBrush(Color.FromRgb(128, 128, 128)); // Gray
            StatusText.Text = isRunning ? "运行中" : "已停止";

            // 更新按钮状态
            StartButton.IsEnabled = !isRunning;
            StopButton.IsEnabled = isRunning;

            // 运行时禁用配置修改
            LeaderAccountCombo.IsEnabled = !isRunning;
            FollowerGrid.IsEnabled = !isRunning;
            ModeAllOrders.IsEnabled = !isRunning;
            ModeMarketOnly.IsEnabled = !isRunning;

            // 更新统计
            var status = _copyEngine.Status;
            CopiedCountText.Text = status.TotalCopiedOrders.ToString();
            SuccessRateText.Text = $"{status.SuccessRate:F1}%";
        }

        #endregion

        #region Event Handlers

        private void LeaderAccountCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateLeaderAccountInfo();
        }

        private void RefreshAccounts_Click(object sender, RoutedEventArgs e)
        {
            RefreshAccountList();
            UpdateLeaderAccountInfo();
        }

        private void AddFollower_Click(object sender, RoutedEventArgs e)
        {
            // 创建添加账户对话框
            var dialog = new AddFollowerDialog();
            dialog.Owner = this;

            // 填充可用账户
            lock (Account.All)
            {
                foreach (var account in Account.All)
                {
                    // 排除已添加的账户和主账户
                    if (account.Name != LeaderAccountCombo.SelectedItem?.ToString() &&
                        !_followerConfigs.Any(f => f.AccountName == account.Name))
                    {
                        dialog.AccountCombo.Items.Add(account.Name);
                    }
                }
            }

            if (dialog.AccountCombo.Items.Count == 0)
            {
                MessageBox.Show("没有可添加的账户", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            dialog.AccountCombo.SelectedIndex = 0;

            if (dialog.ShowDialog() == true)
            {
                var newConfig = new FollowerAccountConfig
                {
                    AccountName = dialog.AccountCombo.SelectedItem.ToString(),
                    IsEnabled = true,
                    RatioMode = dialog.SelectedRatioMode,
                    FixedRatio = dialog.RatioValue,
                    PreAllocatedQuantity = dialog.PreAllocatedQty,
                    MinQuantity = dialog.MinQty,
                    MaxQuantity = dialog.MaxQty,
                    CrossOrderTarget = dialog.CrossOrderTarget
                };

                _followerConfigs.Add(newConfig);
            }
        }

        private void EditFollower_Click(object sender, RoutedEventArgs e)
        {
            var selected = FollowerGrid.SelectedItem as FollowerAccountConfig;
            if (selected == null)
            {
                MessageBox.Show("请先选择要编辑的账户", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new AddFollowerDialog();
            dialog.Owner = this;
            dialog.Title = "编辑从账户";

            // 填充现有值
            dialog.AccountCombo.Items.Add(selected.AccountName);
            dialog.AccountCombo.SelectedIndex = 0;
            dialog.AccountCombo.IsEnabled = false;

            dialog.RatioModeCombo.SelectedItem = selected.RatioMode.ToString();
            dialog.RatioValueText.Text = selected.FixedRatio.ToString();
            dialog.PreAllocText.Text = selected.PreAllocatedQuantity.ToString();
            dialog.MinQtyText.Text = selected.MinQuantity.ToString();
            dialog.MaxQtyText.Text = selected.MaxQuantity.ToString();
            dialog.CrossOrderText.Text = selected.CrossOrderTarget;

            if (dialog.ShowDialog() == true)
            {
                selected.RatioMode = dialog.SelectedRatioMode;
                selected.FixedRatio = dialog.RatioValue;
                selected.PreAllocatedQuantity = dialog.PreAllocatedQty;
                selected.MinQuantity = dialog.MinQty;
                selected.MaxQuantity = dialog.MaxQty;
                selected.CrossOrderTarget = dialog.CrossOrderTarget;

                FollowerGrid.Items.Refresh();
            }
        }

        private void DeleteFollower_Click(object sender, RoutedEventArgs e)
        {
            var selected = FollowerGrid.SelectedItem as FollowerAccountConfig;
            if (selected == null)
            {
                MessageBox.Show("请先选择要删除的账户", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"确定要删除从账户 '{selected.AccountName}' 吗？",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _followerConfigs.Remove(selected);
            }
        }

        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "XML 配置文件|*.xml",
                Title = "导入配置"
            };

            if (dialog.ShowDialog() == true)
            {
                var imported = _configManager.Import(dialog.FileName);
                if (imported != null)
                {
                    _config = imported;
                    _followerConfigs.Clear();
                    foreach (var f in imported.FollowerAccounts)
                    {
                        _followerConfigs.Add(f);
                    }
                    LoadConfiguration();
                    MessageBox.Show("配置导入成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("导入配置失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            SaveConfiguration();

            var dialog = new SaveFileDialog
            {
                Filter = "XML 配置文件|*.xml",
                Title = "导出配置",
                FileName = $"GroupTrade_Config_{DateTime.Now:yyyyMMdd}.xml"
            };

            if (dialog.ShowDialog() == true)
            {
                if (_configManager.Export(_config, dialog.FileName))
                {
                    MessageBox.Show("配置导出成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("导出配置失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            // 保存当前配置
            SaveConfiguration();

            // 验证配置
            if (string.IsNullOrEmpty(_config.LeaderAccountName))
            {
                MessageBox.Show("请选择主账户", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_config.EnabledFollowerCount == 0)
            {
                MessageBox.Show("请至少启用一个从账户", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 启动复制引擎
            if (_copyEngine.Start(_config))
            {
                UpdateUIState();
            }
            else
            {
                MessageBox.Show("启动复制引擎失败，请检查输出日志", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _copyEngine.Stop();
            UpdateUIState();
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            SaveConfiguration();
            MessageBox.Show("配置已保存", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要重置所有配置吗？", "确认重置",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _config = CopyConfiguration.CreateDefault();
                _followerConfigs.Clear();
                LoadConfiguration();
                RefreshAccountList();
            }
        }

        #endregion

        #region Engine Events

        private void OnEngineLog(LogEntry entry)
        {
            // 在 UI 线程更新
            Dispatcher.InvokeAsync(() =>
            {
                // 可以添加日志显示逻辑
            });
        }

        private void OnEngineStatusChanged(CopyStatus status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateUIState();
            });
        }

        #endregion

        #region Window Events

        protected override void OnClosed(EventArgs e)
        {
            // 取消订阅事件
            _copyEngine.OnLog -= OnEngineLog;
            _copyEngine.OnStatusChanged -= OnEngineStatusChanged;

            base.OnClosed(e);
        }

        #endregion
    }
}
