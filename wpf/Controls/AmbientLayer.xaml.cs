// @author zenjiro 18967498922@163.com
// 文件用途 驾驶舱环境光层：两对光域交替的模式氛围交叉淡入（规格 §4.1/§6）

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CaelusApp.WpfHost.Controls
{
    public partial class AmbientLayer : UserControl
    {
        // frontVisible=true 表示 Front 对当前可见
        private bool frontVisible;

        public AmbientLayer()
        {
            InitializeComponent();
        }

        // 立即显示当前主题的氛围（启动时用，无动画）
        public void Show()
        {
            ApplyBrushes(FrontPrimary, FrontSecondary);
            FrontPrimary.Opacity = TargetPrimary();
            FrontSecondary.Opacity = TargetSecondary();
            BackPrimary.Opacity = 0;
            BackSecondary.Opacity = 0;
            frontVisible = true;
        }

        // 模式切换后的氛围过渡：后对绑定新画刷淡入，前对淡出
        public void TransitionTo(bool animate)
        {
            Ellipse newPrimary = frontVisible ? BackPrimary : FrontPrimary;
            Ellipse newSecondary = frontVisible ? BackSecondary : FrontSecondary;
            Ellipse oldPrimary = frontVisible ? FrontPrimary : BackPrimary;
            Ellipse oldSecondary = frontVisible ? FrontSecondary : BackSecondary;

            ApplyBrushes(newPrimary, newSecondary);

            if (!animate || Motion.Reduced || !Motion.Enabled)
            {
                newPrimary.Opacity = TargetPrimary();
                newSecondary.Opacity = TargetSecondary();
                oldPrimary.Opacity = 0;
                oldSecondary.Opacity = 0;
                frontVisible = !frontVisible;
                return;
            }

            int ms = UiMotion.Duration(UiMotion.NumberRollMs, Motion.Reduced);
            FadeTo(newPrimary, TargetPrimary(), ms);
            FadeTo(newSecondary, TargetSecondary(), ms);
            FadeTo(oldPrimary, 0, ms);
            FadeTo(oldSecondary, 0, ms);
            frontVisible = !frontVisible;
        }

        private static void ApplyBrushes(Ellipse primary, Ellipse secondary)
        {
            primary.Fill = (Brush)Application.Current.FindResource("AmbientPrimaryBrush");
            secondary.Fill = (Brush)Application.Current.FindResource("AmbientSecondaryBrush");
        }

        private static double TargetPrimary()
        {
            return (double)Application.Current.FindResource("AmbientPrimaryOpacity");
        }

        private static double TargetSecondary()
        {
            return (double)Application.Current.FindResource("AmbientSecondaryOpacity");
        }

        private static void FadeTo(Ellipse el, double target, int ms)
        {
            DoubleAnimation anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms));
            anim.EasingFunction = new CubicEase();
            ((CubicEase)anim.EasingFunction).EasingMode = EasingMode.EaseOut;
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
