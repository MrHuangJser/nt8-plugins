using System;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.UI
{
    /// <summary>
    /// AddFollowerDialog - 添加从账户对话框
    /// 使用纯代码构建 UI（NinjaTrader 不支持独立 XAML 编译）
    /// </summary>
    public partial class AddFollowerDialog : Window
    {
        #region UI Controls

        public ComboBox AccountCombo { get; private set; }
        public ComboBox RatioModeCombo { get; private set; }
        public TextBox RatioValueText { get; private set; }
        public TextBox PreAllocText { get; private set; }
        public TextBox MinQtyText { get; private set; }
        public TextBox MaxQtyText { get; private set; }
        public TextBox NotesText { get; private set; }

        #endregion

        public AddFollowerDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Title = "添加从账户";
            Width = 450;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            // 不设置 Background，让系统/NinjaTrader 自动应用主题

            var mainGrid = new Grid { Margin = new Thickness(20) };
            
            // Define rows
            for (int i = 0; i < 9; i++)
            {
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = i == 7 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            }
            
            // Define columns
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Row 0: Account selection
            AddRow(mainGrid, 0, "账户:", AccountCombo = new ComboBox { Margin = new Thickness(0, 5, 0, 5) });

            // Row 1: Ratio mode
            RatioModeCombo = new ComboBox { Margin = new Thickness(0, 5, 0, 5) };
            RatioModeCombo.Items.Add(new ComboBoxItem { Content = "精确跟随 (1:1)", Tag = RatioMode.ExactQuantity, IsSelected = true });
            RatioModeCombo.Items.Add(new ComboBoxItem { Content = "均分数量", Tag = RatioMode.EqualQuantity });
            RatioModeCombo.Items.Add(new ComboBoxItem { Content = "固定比例", Tag = RatioMode.Ratio });
            RatioModeCombo.Items.Add(new ComboBoxItem { Content = "净值比例", Tag = RatioMode.NetLiquidation });
            RatioModeCombo.Items.Add(new ComboBoxItem { Content = "可用资金比例", Tag = RatioMode.AvailableMoney });
            RatioModeCombo.Items.Add(new ComboBoxItem { Content = "百分比变化", Tag = RatioMode.PercentageChange });
            RatioModeCombo.Items.Add(new ComboBoxItem { Content = "预分配手数", Tag = RatioMode.PreAllocation });
            RatioModeCombo.SelectionChanged += RatioModeCombo_SelectionChanged;
            AddRow(mainGrid, 1, "比例模式:", RatioModeCombo);

            // Row 2: Ratio value
            AddRow(mainGrid, 2, "比例值:", RatioValueText = new TextBox { Text = "1.0", Margin = new Thickness(0, 5, 0, 5) });

            // Row 3: Pre-allocated quantity
            AddRow(mainGrid, 3, "预分配手数:", PreAllocText = new TextBox { Text = "1", Margin = new Thickness(0, 5, 0, 5) });

            // Row 4: Min quantity
            AddRow(mainGrid, 4, "最小手数:", MinQtyText = new TextBox { Text = "1", Margin = new Thickness(0, 5, 0, 5) });

            // Row 5: Max quantity
            AddRow(mainGrid, 5, "最大手数:", MaxQtyText = new TextBox { Text = "0", Margin = new Thickness(0, 5, 0, 5) });

            // Row 6: Notes
            AddRow(mainGrid, 6, "备注:", NotesText = new TextBox { Margin = new Thickness(0, 5, 0, 5) });

            // Row 8: Buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            
            var okButton = new Button
            {
                Content = "确定",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += OK_Click;
            
            var cancelButton = new Button
            {
                Content = "取消",
                Padding = new Thickness(20, 8, 20, 8),
                IsCancel = true
            };
            cancelButton.Click += Cancel_Click;
            
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 8);
            Grid.SetColumn(buttonPanel, 0);
            Grid.SetColumnSpan(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private void AddRow(Grid grid, int row, string label, Control control)
        {
            var textBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(textBlock, row);
            Grid.SetColumn(textBlock, 0);
            grid.Children.Add(textBlock);

            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        #region Properties

        public RatioMode SelectedRatioMode
        {
            get
            {
                var selectedItem = RatioModeCombo.SelectedItem as ComboBoxItem;
                if (selectedItem?.Tag is RatioMode mode)
                    return mode;
                return RatioMode.ExactQuantity;
            }
        }

        public double RatioValue
        {
            get
            {
                if (double.TryParse(RatioValueText.Text, out var value))
                    return Math.Max(0.01, Math.Abs(value));  // 强制正数
                return 1.0;
            }
        }

        public int PreAllocatedQty
        {
            get
            {
                if (int.TryParse(PreAllocText.Text, out var value))
                    return value;
                return 1;
            }
        }

        public int MinQty
        {
            get
            {
                if (int.TryParse(MinQtyText.Text, out var value))
                    return value;
                return 1;
            }
        }

        public int MaxQty
        {
            get
            {
                if (int.TryParse(MaxQtyText.Text, out var value))
                    return value;
                return 0;
            }
        }

        public string Notes => NotesText.Text?.Trim() ?? "";

        #endregion

        #region Event Handlers

        private void RatioModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RatioValueText == null || PreAllocText == null)
                return;

            var selectedItem = RatioModeCombo.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is not RatioMode mode)
                return;

            // 根据模式启用/禁用相关输入框
            switch (mode)
            {
                case RatioMode.Ratio:
                case RatioMode.PercentageChange:
                    RatioValueText.IsEnabled = true;
                    PreAllocText.IsEnabled = false;
                    break;
                case RatioMode.PreAllocation:
                    RatioValueText.IsEnabled = false;
                    PreAllocText.IsEnabled = true;
                    break;
                default:
                    RatioValueText.IsEnabled = false;
                    PreAllocText.IsEnabled = false;
                    break;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // 验证
            if (AccountCombo.SelectedItem == null)
            {
                MessageBox.Show("请选择账户", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion
    }
}
