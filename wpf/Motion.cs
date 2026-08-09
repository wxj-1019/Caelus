// @author zenjiro 18967498922@163.com
// 文件用途 WPF 动效执行：按系统设置降级（规格 §6.3）

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CaelusApp.WpfHost
{
    internal static class Motion
    {
        // 全局开关：截图探针（App.RunShot）会置 false，使离屏捕获拿到最终视觉态
        // （Opacity=1、transform=0），而非动效起始帧。实时 UI 始终为 true。
        public static bool Enabled = true;

        public static bool Reduced
        {
            get { return !SystemParameters.ClientAreaAnimation; }
        }

        // 页面进入：透明度淡入 +（未降级时）20px 上浮，250ms ease-out
        public static void FadeIn(FrameworkElement el)
        {
            if (el == null) return;
            if (!Enabled) return;
            int ms = UiMotion.Duration(UiMotion.PageFadeMs, Reduced);
            DoubleAnimation opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            el.BeginAnimation(UIElement.OpacityProperty, opacity);

            if (!UiMotion.AllowsOffset(Reduced)) return;
            TranslateTransform tt = el.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform();
                el.RenderTransform = tt;
            }
            DoubleAnimation slide = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            tt.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }
}
