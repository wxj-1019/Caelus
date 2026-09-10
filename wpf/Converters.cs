// @author zenjiro 18967498922@163.com
// 文件用途 设置行整行点击转发的附加行为（RowToggle）+ 计数→可见性转换器

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CaelusApp.WpfHost
{
    /// <summary>集合计数→可见性：0 → Collapsed，&gt;0 → Visible（如「已禁用」分组头）。</summary>
    internal sealed class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int n = 0;
            if (value is int) n = (int)value;
            else if (value != null) int.TryParse(value.ToString(), out n);
            return n > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>集合计数→可见性（反向）：0 → Visible，&gt;0 → Collapsed（如「暂无新发现」空态行）。</summary>
    internal sealed class ZeroToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int n = 0;
            if (value is int) n = (int)value;
            else if (value != null) int.TryParse(value.ToString(), out n);
            return n == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // 整行点击切开关：把落在设置行空白处的点击转发为行内开关的一次翻转，
    // 复用开关自身的确认/回滚/双向绑定逻辑。点击落在开关/按钮/输入框上时跳过，避免二次触发。
    internal static class RowToggle
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(RowToggle), new PropertyMetadata(false, OnEnabledChanged));

        public static void SetEnabled(DependencyObject obj, bool value) { obj.SetValue(EnabledProperty, value); }
        public static bool GetEnabled(DependencyObject obj) { return (bool)obj.GetValue(EnabledProperty); }

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            FrameworkElement element = d as FrameworkElement;
            if (element == null) return;
            if ((bool)e.NewValue)
            {
                element.Cursor = Cursors.Hand;
                element.MouseLeftButtonUp += OnRowClick;
            }
            else
            {
                element.Cursor = null;
                element.MouseLeftButtonUp -= OnRowClick;
            }
        }

        private static void OnRowClick(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement row = sender as FrameworkElement;
            if (row == null) return;
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (source != null && (IsWithin<ButtonBase>(source) || IsWithin<TextBox>(source))) return;
            ToggleButton toggle = FindChild<ToggleButton>(row);
            if (toggle == null || !toggle.IsEnabled) return;
            // SetCurrentValue 而非 SetValue：裸 SetValue 会直接摧毁 IsChecked 的绑定
            // （OneWay 从此失联、外部状态变化不再刷新视觉），SetCurrentValue 保留绑定
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, !toggle.IsChecked);
            // 部分开关把确认/刷新逻辑挂在 Click 事件（而非 IsChecked 绑定）上，手动转发一次。
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private static bool IsWithin<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null)
            {
                if (node is T) return true;
                node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
            }
            return false;
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T) return (T)child;
                T found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
