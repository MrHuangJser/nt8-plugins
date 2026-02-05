using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Core;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Services;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.UI
{
    /// <summary>
    /// GroupTradeWindow - 主窗口
    /// 使用纯代码构建 UI（NinjaTrader 不支持独立 XAML 编译）
    /// </summary>
    public partial class GroupTradeWindow : Window
    {
        #region Fields

        private readonly CopyEngine _copyEngine;
        private readonly ConfigManager _configManager;
        private CopyConfiguration _config;
        private ObservableCollection<FollowerAccountConfig> _followerConfigs;

        #endregion

        #region UI Controls

        private Border StatusIndicator;
        private TextBlock StatusText;
        private TextBlock CopiedCountText;
        private TextBlock SuccessRateText;
        private ComboBox LeaderAccountCombo;
        private TextBlock LeaderEquityText;
        private TextBlock LeaderPositionText;
        private DataGrid FollowerGrid;
        private CheckBox SyncStopLoss;
        private CheckBox SyncTakeProfit;
        private CheckBox SyncClose;
        private CheckBox SyncModify;
        private CheckBox SyncOCO;
        private CheckBox StealthMode;
        private CheckBox FollowerGuard;
        private Button StartButton;
        private Button StopButton;

        #endregion

        #region Constructor

        public GroupTradeWindow(CopyEngine copyEngine, ConfigManager configManager)
        {
            _copyEngine = copyEngine ?? throw new ArgumentNullException(nameof(copyEngine));
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));

            InitializeComponent();

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

        private void InitializeComponent()
        {
            Title = "Group Trade - 多账户联动下单";
            Width = 850;
            Height = 700;
            MinWidth = 750;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // 不设置 Background，让系统/NinjaTrader 自动应用主题

            var mainGrid = new Grid { Margin = new Thickness(10) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Title bar
            mainGrid.Children.Add(CreateTitleBar());

            // Row 1: Leader account config
            var leaderGroup = CreateLeaderSection();
            Grid.SetRow(leaderGroup, 1);
            mainGrid.Children.Add(leaderGroup);

            // Row 2: Follower accounts
            var followerGroup = CreateFollowerSection();
            Grid.SetRow(followerGroup, 2);
            mainGrid.Children.Add(followerGroup);

            // Row 3: Copy options
            var optionsGroup = CreateOptionsSection();
            Grid.SetRow(optionsGroup, 3);
            mainGrid.Children.Add(optionsGroup);

            // Row 4: Control buttons
            var controlBar = CreateControlBar();
            Grid.SetRow(controlBar, 4);
            mainGrid.Children.Add(controlBar);

            Content = mainGrid;
        }

        private Border CreateTitleBar()
        {
            var border = new Border
            {
                BorderBrush = SystemColors.ControlDarkBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left side - title and status
            var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftPanel.Children.Add(new TextBlock { Text = "Group Trade", FontSize = 20, FontWeight = FontWeights.Bold });
            leftPanel.Children.Add(new TextBlock { Text = "v1.0", FontSize = 12, Foreground = SystemColors.GrayTextBrush, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2) });
            
            StatusIndicator = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Brushes.Gray, Margin = new Thickness(20, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            leftPanel.Children.Add(StatusIndicator);
            
            StatusText = new TextBlock { Text = "已停止", Foreground = SystemColors.GrayTextBrush, VerticalAlignment = VerticalAlignment.Center };
            leftPanel.Children.Add(StatusText);

            grid.Children.Add(leftPanel);

            // Right side - stats
            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal };
            rightPanel.Children.Add(new TextBlock { Text = "已复制: ", Foreground = SystemColors.GrayTextBrush, VerticalAlignment = VerticalAlignment.Center });
            CopiedCountText = new TextBlock { Text = "0", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            rightPanel.Children.Add(CopiedCountText);
            rightPanel.Children.Add(new TextBlock { Text = " 单", Foreground = SystemColors.GrayTextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) });
            rightPanel.Children.Add(new TextBlock { Text = "成功率: ", Foreground = SystemColors.GrayTextBrush, VerticalAlignment = VerticalAlignment.Center });
            SuccessRateText = new TextBlock { Text = "0%", Foreground = Brushes.Green, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            rightPanel.Children.Add(SuccessRateText);
            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);

            border.Child = grid;
            return border;
        }

        private GroupBox CreateLeaderSection()
        {
            var group = new GroupBox
            {
                Header = "Leader 账户 (主账户)",
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock { Text = "账户:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });

            LeaderAccountCombo = new ComboBox();
            LeaderAccountCombo.SelectionChanged += LeaderAccountCombo_SelectionChanged;
            Grid.SetColumn(LeaderAccountCombo, 1);
            grid.Children.Add(LeaderAccountCombo);

            var refreshBtn = new Button { Content = "刷新", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(10, 0, 0, 0) };
            refreshBtn.Click += RefreshAccounts_Click;
            Grid.SetColumn(refreshBtn, 2);
            grid.Children.Add(refreshBtn);

            var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            infoPanel.Children.Add(new TextBlock { Text = "净值: ", Foreground = SystemColors.GrayTextBrush, VerticalAlignment = VerticalAlignment.Center });
            LeaderEquityText = new TextBlock { Text = "$0.00", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
            infoPanel.Children.Add(LeaderEquityText);
            infoPanel.Children.Add(new TextBlock { Text = "持仓: ", Foreground = SystemColors.GrayTextBrush, VerticalAlignment = VerticalAlignment.Center });
            LeaderPositionText = new TextBlock { Text = "-", VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(LeaderPositionText);
            Grid.SetColumn(infoPanel, 3);
            grid.Children.Add(infoPanel);

            group.Content = grid;
            return group;
        }

        private GroupBox CreateFollowerSection()
        {
            var group = new GroupBox
            {
                Header = "Follower 账户 (从账户)",
                Margin = new Thickness(5),
                Padding = new Thickness(10)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            FollowerGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
            };

            FollowerGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new System.Windows.Data.Binding("IsEnabled"), Width = 50 });
            FollowerGrid.Columns.Add(new DataGridTextColumn { Header = "账户名", Binding = new System.Windows.Data.Binding("AccountName"), Width = 120, IsReadOnly = true });
            FollowerGrid.Columns.Add(new DataGridTextColumn { Header = "比例/手数", Binding = new System.Windows.Data.Binding("RatioDisplayValue"), Width = 80, IsReadOnly = true });
            FollowerGrid.Columns.Add(new DataGridTextColumn { Header = "最小", Binding = new System.Windows.Data.Binding("MinQuantity"), Width = 50 });
            FollowerGrid.Columns.Add(new DataGridTextColumn { Header = "最大", Binding = new System.Windows.Data.Binding("MaxQuantity"), Width = 50 });
            FollowerGrid.Columns.Add(new DataGridTextColumn { Header = "备注", Binding = new System.Windows.Data.Binding("Notes"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            grid.Children.Add(FollowerGrid);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var addBtn = new Button { Content = "添加账户", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 5, 0) }; addBtn.Click += AddFollower_Click; buttonPanel.Children.Add(addBtn);
            var editBtn = new Button { Content = "编辑", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 5, 0) }; editBtn.Click += EditFollower_Click; buttonPanel.Children.Add(editBtn);
            var delBtn = new Button { Content = "删除", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 20, 0) }; delBtn.Click += DeleteFollower_Click; buttonPanel.Children.Add(delBtn);
            var importBtn = new Button { Content = "导入", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 5, 0) }; importBtn.Click += ImportConfig_Click; buttonPanel.Children.Add(importBtn);
            var exportBtn = new Button { Content = "导出", Padding = new Thickness(10, 5, 10, 5) }; exportBtn.Click += ExportConfig_Click; buttonPanel.Children.Add(exportBtn);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            group.Content = grid;
            return group;
        }

        private GroupBox CreateOptionsSection()
        {
            var group = new GroupBox
            {
                Header = "复制选项",
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column
            var leftPanel = new StackPanel();

            // Row 1: Basic Sync
            var syncPanel1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            SyncStopLoss = new CheckBox { Content = "同步止损", IsChecked = true, Margin = new Thickness(0, 0, 15, 0) };
            SyncTakeProfit = new CheckBox { Content = "同步止盈", IsChecked = true, Margin = new Thickness(0, 0, 15, 0) };
            SyncClose = new CheckBox { Content = "同步平仓", IsChecked = true };
            syncPanel1.Children.Add(SyncStopLoss);
            syncPanel1.Children.Add(SyncTakeProfit);
            syncPanel1.Children.Add(SyncClose);
            leftPanel.Children.Add(syncPanel1);

            // Row 2: Advanced Sync
            var syncPanel2 = new StackPanel { Orientation = Orientation.Horizontal };
            SyncModify = new CheckBox { Content = "同步改单", IsChecked = true, Margin = new Thickness(0, 0, 15, 0) };
            SyncOCO = new CheckBox { Content = "同步OCO", IsChecked = true };
            syncPanel2.Children.Add(SyncModify);
            syncPanel2.Children.Add(SyncOCO);
            leftPanel.Children.Add(syncPanel2);

            grid.Children.Add(leftPanel);

            // Right column
            var rightPanel = new StackPanel();
            rightPanel.Children.Add(new TextBlock { Text = "高级选项:", Foreground = SystemColors.GrayTextBrush, Margin = new Thickness(0, 0, 0, 5) });

            var advPanel = new StackPanel { Orientation = Orientation.Horizontal };
            StealthMode = new CheckBox { Content = "Stealth Mode (隐身)", Margin = new Thickness(0, 0, 15, 0) };
            FollowerGuard = new CheckBox { Content = "Follower Guard (保护)" };

            var configGuardBtn = new Button { Content = "⚙️", Width = 25, Height = 20, Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(0) };
            configGuardBtn.Click += ConfigureGuard_Click;

            advPanel.Children.Add(StealthMode);
            advPanel.Children.Add(FollowerGuard);
            advPanel.Children.Add(configGuardBtn);
            rightPanel.Children.Add(advPanel);

            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);

            group.Content = grid;
            return group;
        }

        private Border CreateControlBar()
        {
            var border = new Border
            {
                BorderBrush = SystemColors.ControlDarkBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(15, 10, 15, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftPanel = new StackPanel { Orientation = Orientation.Horizontal };
            StartButton = new Button { Content = "▶ 启动复制", Padding = new Thickness(15, 8, 15, 8), Width = 120, Margin = new Thickness(0, 0, 10, 0) };
            StartButton.Click += Start_Click;
            StopButton = new Button { Content = "■ 停止", Padding = new Thickness(15, 8, 15, 8), Width = 100, IsEnabled = false };
            StopButton.Click += Stop_Click;
            leftPanel.Children.Add(StartButton);
            leftPanel.Children.Add(StopButton);
            grid.Children.Add(leftPanel);

            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var saveBtn = new Button { Content = "保存配置", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 10, 0) };
            saveBtn.Click += SaveConfig_Click;
            var resetBtn = new Button { Content = "重置", Padding = new Thickness(10, 5, 10, 5) };
            resetBtn.Click += Reset_Click;
            rightPanel.Children.Add(saveBtn);
            rightPanel.Children.Add(resetBtn);
            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);

            border.Child = grid;
            return border;
        }

        #region Configuration

        private void LoadConfiguration()
        {
            _config = _configManager.Load();

            // 绑定从账户列表
            _followerConfigs = new ObservableCollection<FollowerAccountConfig>(_config.FollowerAccounts);
            FollowerGrid.ItemsSource = _followerConfigs;

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

        private void ConfigureGuard_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GuardRuleDialog(_config)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                // 更新 UI 状态以反映配置变化
                FollowerGuard.IsChecked = _config.EnableFollowerGuard;
                SaveConfiguration(); // 立即保存更改
            }
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
            StatusIndicator.Background = isRunning ? Brushes.Green : Brushes.Gray;
            StatusText.Text = isRunning ? "运行中" : "已停止";

            // 更新按钮状态
            StartButton.IsEnabled = !isRunning;
            StopButton.IsEnabled = isRunning;

            // 运行时禁用配置修改
            LeaderAccountCombo.IsEnabled = !isRunning;
            FollowerGrid.IsEnabled = !isRunning;

            // 更新统计
            var status = _copyEngine.Status;
            CopiedCountText.Text = status.TotalCopiedOrders.ToString();
            SuccessRateText.Text = $"{status.SuccessRate:F1}%";
        }

        #endregion

        #region Event Handlers

        private void LeaderAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
                    Notes = dialog.Notes
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
            dialog.NotesText.Text = selected.Notes;

            if (dialog.ShowDialog() == true)
            {
                selected.RatioMode = dialog.SelectedRatioMode;
                selected.FixedRatio = dialog.RatioValue;
                selected.PreAllocatedQuantity = dialog.PreAllocatedQty;
                selected.MinQuantity = dialog.MinQty;
                selected.MaxQuantity = dialog.MaxQty;
                selected.Notes = dialog.Notes;

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
