using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Core;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.UI
{
    public class GuardRuleDialog : Window
    {
        private GuardConfiguration _guardConfig;
        private CopyConfiguration _mainConfig; // 保留主配置引用用于 EnableFollowerGuard

        private CheckBox EnableGuardCheck;
        private TextBox MaxDailyLossText;
        private TextBox MaxDrawdownText;
        private TextBox MaxConsecutiveLossText;
        private TextBox MaxRejectedText;
        private CheckBox FlattenOnGuardCheck;

        public GuardRuleDialog(CopyConfiguration config)
        {
            _mainConfig = config;
            _guardConfig = config.GuardConfiguration;
            InitializeComponent();
            LoadValues();
        }

        private void InitializeComponent()
        {
            Title = "Follower Guard 配置";
            Width = 400;
            Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Form
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // 1. Header
            var header = new TextBlock
            {
                Text = "从账户风险保护设置",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 15)
            };
            grid.Children.Add(header);

            // 2. Form Panel
            var formPanel = new StackPanel();
            Grid.SetRow(formPanel, 1);

            // Enable Guard
            EnableGuardCheck = new CheckBox
            {
                Content = "启用 Follower Guard 保护系统",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 15)
            };
            formPanel.Children.Add(EnableGuardCheck);

            // Daily Loss
            formPanel.Children.Add(CreateInputRow("日内亏损限额 ($):", out MaxDailyLossText));

            // Drawdown
            formPanel.Children.Add(CreateInputRow("最大权益回撤 (%):", out MaxDrawdownText));

            // Consecutive Loss
            formPanel.Children.Add(CreateInputRow("最大连续亏损次数:", out MaxConsecutiveLossText));

            // Rejected
            formPanel.Children.Add(CreateInputRow("最大拒单次数:", out MaxRejectedText));

            // Flatten Action
            FlattenOnGuardCheck = new CheckBox
            {
                Content = "触发保护时立即平仓 (Flatten)",
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = Brushes.DarkRed
            };
            formPanel.Children.Add(FlattenOnGuardCheck);

            grid.Children.Add(formPanel);

            // 4. Buttons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Grid.SetRow(btnPanel, 3);

            var saveBtn = new Button { Content = "保存", Width = 80, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(5) };
            saveBtn.Click += Save_Click;

            var cancelBtn = new Button { Content = "取消", Width = 80, Padding = new Thickness(5) };
            cancelBtn.Click += (s, e) => DialogResult = false;

            btnPanel.Children.Add(saveBtn);
            btnPanel.Children.Add(cancelBtn);
            grid.Children.Add(btnPanel);

            Content = grid;
        }

        private Grid CreateInputRow(string label, out TextBox textBox)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

            textBox = new TextBox { Padding = new Thickness(2) };
            Grid.SetColumn(textBox, 1);
            row.Children.Add(textBox);

            return row;
        }

        private void LoadValues()
        {
            EnableGuardCheck.IsChecked = _mainConfig.EnableFollowerGuard;
            FlattenOnGuardCheck.IsChecked = _guardConfig.FlattenOnTrigger;

            MaxDailyLossText.Text = _guardConfig.DailyLossLimit.ToString("F2");
            MaxDrawdownText.Text = _guardConfig.EquityDrawdownPercent.ToString("F2");
            MaxConsecutiveLossText.Text = _guardConfig.ConsecutiveLossCount.ToString();
            MaxRejectedText.Text = _guardConfig.OrderRejectedCount.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate and Save
                if (!double.TryParse(MaxDailyLossText.Text, out double dailyLoss) || dailyLoss < 0)
                    throw new Exception("日内亏损限额必须为非负数字");

                if (!double.TryParse(MaxDrawdownText.Text, out double drawdown) || drawdown < 0 || drawdown > 100)
                    throw new Exception("权益回撤百分比必须在 0-100 之间");

                if (!int.TryParse(MaxConsecutiveLossText.Text, out int consecLoss) || consecLoss < 1)
                    throw new Exception("连续亏损次数必须大于 0");

                if (!int.TryParse(MaxRejectedText.Text, out int rejected) || rejected < 1)
                    throw new Exception("拒单次数必须大于 0");

                _mainConfig.EnableFollowerGuard = EnableGuardCheck.IsChecked ?? false;

                _guardConfig.FlattenOnTrigger = FlattenOnGuardCheck.IsChecked ?? false;
                _guardConfig.DailyLossLimit = dailyLoss;
                _guardConfig.EquityDrawdownPercent = drawdown;
                _guardConfig.ConsecutiveLossCount = consecLoss;
                _guardConfig.OrderRejectedCount = rejected;

                // 同时也启用/禁用具体的规则开关
                _guardConfig.EnableDailyLossGuard = true;
                _guardConfig.EnableEquityDrawdownGuard = true;
                _guardConfig.EnableConsecutiveLossGuard = true;
                _guardConfig.EnableOrderRejectedGuard = true;

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
