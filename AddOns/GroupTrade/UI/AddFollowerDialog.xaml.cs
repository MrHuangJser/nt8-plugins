using System;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.UI
{
    /// <summary>
    /// AddFollowerDialog.xaml 的交互逻辑
    /// </summary>
    public partial class AddFollowerDialog : Window
    {
        public AddFollowerDialog()
        {
            InitializeComponent();
        }

        #region Properties

        public RatioMode SelectedRatioMode
        {
            get
            {
                var selected = (RatioModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (Enum.TryParse<RatioMode>(selected, out var mode))
                    return mode;
                return RatioMode.ExactQuantity;
            }
        }

        public double RatioValue
        {
            get
            {
                if (double.TryParse(RatioValueText.Text, out var value))
                    return value;
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

        public string CrossOrderTarget => CrossOrderText.Text?.Trim() ?? "";

        #endregion

        #region Event Handlers

        private void RatioModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RatioValueText == null || PreAllocText == null)
                return;

            var selected = (RatioModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();

            // 根据模式启用/禁用相关输入框
            switch (selected)
            {
                case "Ratio":
                case "PercentageChange":
                    RatioValueText.IsEnabled = true;
                    PreAllocText.IsEnabled = false;
                    break;
                case "PreAllocation":
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
