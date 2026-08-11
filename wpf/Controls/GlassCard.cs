// @author zenjiro 18967498922@163.com
// 文件用途 Aurora 玻璃卡片：渐变描边 + 顶部高光线 + 悬停描边点亮（规格 §5）

using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Controls
{
    internal sealed class GlassCard : ContentControl
    {
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
            "CornerRadius", typeof(CornerRadius), typeof(GlassCard),
            new FrameworkPropertyMetadata(new CornerRadius(14)));

        public CornerRadius CornerRadius
        {
            get { return (CornerRadius)GetValue(CornerRadiusProperty); }
            set { SetValue(CornerRadiusProperty, value); }
        }
    }
}
