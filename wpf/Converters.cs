// @author zenjiro 18967498922@163.com
// 文件用途 概览页绑定用的轻量值转换器

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
    // "Success" 等语义键 → 当前主题的画刷（DynamicResource 求值）
    internal sealed class KeyBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            string key = (value as string) ?? "Info";
            object found = Application.Current.TryFindResource(key + "Brush");
            return found ?? Brushes.Gray;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    // 语义键 → 同色系 15% 透明底（用于结论图标圆形底）
    internal sealed class KeySoftBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            string key = (value as string) ?? "Info";
            SolidColorBrush brush = Application.Current.TryFindResource(key + "Brush") as SolidColorBrush;
            if (brush == null) return new SolidColorBrush(Color.FromArgb(30, 128, 128, 128));
            Color col = brush.Color;
            return new SolidColorBrush(Color.FromArgb(38, col.R, col.G, col.B));
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class DetailButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return value is bool && (bool)value ? "收起详情" : "查看详情";
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class FractionGridLengthConverter : IValueConverter
    {
        // 比例 f∈[0,1] → Grid 列宽。默认返回 f* 作为「填充列」，
        // 第二列需绑定同一 Fraction 并传 ConverterParameter="rest"，
        // 返回 (1-f)*，两者合计恒为 1*，故填充列占比恰为 f（与布局宽度无关）。
        // 直接用单个 star 列 + 另一个 1* 列会得到 f/(f+1)，是错误的。
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            double f = value is double ? (double)value : 0;
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            bool rest = p != null && p.ToString() == "rest";
            double stars = rest ? 1.0 - f : f;
            return new GridLength(stars, GridUnitType.Star);
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    // 语义键 → 当前主题的画刷颜色（供发光 Effect 的 Color 绑定）
    internal sealed class KeyColorConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            string key = (value as string) ?? "Info";
            SolidColorBrush brush = Application.Current.TryFindResource(key + "Brush") as SolidColorBrush;
            return brush == null ? Colors.Gray : brush.Color;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
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
            toggle.IsChecked = !toggle.IsChecked;
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
