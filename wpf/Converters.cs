// @author zenjiro 18967498922@163.com
// 文件用途 概览页绑定用的轻量值转换器

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
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
}
