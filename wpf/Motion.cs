// @author zenjiro 18967498922@163.com
// 文件用途 WPF 动效执行：按系统设置降级（规格 §6.3）

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CaelusApp.WpfHost
{
    internal static class Motion
    {
        // 全局开关：截图探针（App.RunShot）会置 false，使离屏捕获拿到最终视觉态
        // （Opacity=1、transform=0），而非动效起始帧。实时 UI 始终为 true。
        public static bool Enabled = true;

        // 无限动画帧率上限：net4 WPF 无硬件合成时 RenderTransform/Opacity 动画走 UI 线程
        // 软件渲染，60fps 下大量 Ellipse 会吃满 CPU。10fps 对慢动画（14-32s 周期）视觉无损，
        // CPU 降低约 80%。T17 性能验证发现并修复。
        private const int InfiniteFps = 8;

        public static bool Reduced
        {
            get { return !SystemParameters.ClientAreaAnimation; }
        }

        // 对无限动画应用帧率节流（仅 Pulse/Spin/漂移等 RepeatBehavior.Forever 动画）
        public static void Throttle(DoubleAnimation anim)
        {
            Timeline.SetDesiredFrameRate(anim, InfiniteFps);
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

        // ===== Lift 附加属性：悬停浮起（规格 §4.2 HoverLift，TranslateY 0→-3，250ms）=====
        public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached(
            "Lift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnLiftChanged));

        public static bool GetLift(DependencyObject d) { return (bool)d.GetValue(LiftProperty); }
        public static void SetLift(DependencyObject d, bool value) { d.SetValue(LiftProperty, value); }

        private static void OnLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            UIElement el = d as UIElement;
            if (el == null) return;
            bool enabled = (bool)e.NewValue;
            if (enabled)
            {
                el.MouseEnter += OnLiftEnter;
                el.MouseLeave += OnLiftLeave;
            }
            else
            {
                el.MouseEnter -= OnLiftEnter;
                el.MouseLeave -= OnLiftLeave;
            }
        }

        private static void OnLiftEnter(object sender, MouseEventArgs e) { LiftTo((UIElement)sender, -3); }
        private static void OnLiftLeave(object sender, MouseEventArgs e) { LiftTo((UIElement)sender, 0); }

        private static void LiftTo(UIElement el, double y)
        {
            if (!Enabled) return;
            FrameworkElement fe = el as FrameworkElement;
            if (fe == null) return;
            // 前提：元素无其他 RenderTransform（as 转换失败会覆盖）。GlassCard 等卡片用 Border 描边表达层次，不占 RenderTransform，安全。
            TranslateTransform tt = fe.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform();
                fe.RenderTransform = tt;
            }
            if (Reduced) { tt.Y = y; return; }
            var anim = new DoubleAnimation(y, TimeSpan.FromMilliseconds(UiMotion.PageFadeMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            tt.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        // ===== READY 脉冲：透明度呼吸 2.4s 无限（规格 §4.2 ReadyPulse）=====
        public static void Pulse(UIElement el)
        {
            if (el == null || !Enabled || Reduced) return;
            var anim = new DoubleAnimation(1, 0.45, TimeSpan.FromSeconds(2.4))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Throttle(anim);
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // ===== 无限旋转：CaelusCore 双环（规格 §4.2 CoreSpin，RotateTransform 比 dash 动画稳）=====
        public static void Spin(FrameworkElement el, double seconds, bool reverse)
        {
            if (el == null || !Enabled || Reduced) return;
            // 前提：元素无其他 RenderTransform（CaelusCore 双环各自独立的 Grid，无既有 transform）
            RotateTransform rt = el.RenderTransform as RotateTransform;
            if (rt == null)
            {
                rt = new RotateTransform();
                el.RenderTransformOrigin = new Point(0.5, 0.5);
                el.RenderTransform = rt;
            }
            var anim = new DoubleAnimation(0, reverse ? -360 : 360, TimeSpan.FromSeconds(seconds))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            Throttle(anim);
            rt.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
    }
}
