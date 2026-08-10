// @author zenjiro 18967498922@163.com
// 文件用途 Aurora 环境光层 v2：三层光晕两对交替（模式切换交叉淡入）+ 无限漂移（规格 §4.2）

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
        // frontVisible=true 表示 Front 组当前可见
        private bool frontVisible;
        private bool driftStarted;

        public AmbientLayer()
        {
            InitializeComponent();
        }

        // 立即显示当前主题的氛围（启动时用，无动画）
        public void Show()
        {
            ApplyBrushes(FrontPrimary, FrontSecondary, FrontTertiary);
            FrontPrimary.Opacity = Target("AuroraPrimaryOpacity");
            FrontSecondary.Opacity = Target("AuroraSecondaryOpacity");
            FrontTertiary.Opacity = Target("AuroraTertiaryOpacity");
            BackPrimary.Opacity = 0;
            BackSecondary.Opacity = 0;
            BackTertiary.Opacity = 0;
            frontVisible = true;
            StartDrift();
        }

        // 模式切换后的氛围过渡：后组绑定新画刷淡入，前组淡出
        public void TransitionTo(bool animate)
        {
            Ellipse newPrimary = frontVisible ? BackPrimary : FrontPrimary;
            Ellipse newSecondary = frontVisible ? BackSecondary : FrontSecondary;
            Ellipse newTertiary = frontVisible ? BackTertiary : FrontTertiary;
            Ellipse oldPrimary = frontVisible ? FrontPrimary : BackPrimary;
            Ellipse oldSecondary = frontVisible ? FrontSecondary : BackSecondary;
            Ellipse oldTertiary = frontVisible ? FrontTertiary : BackTertiary;

            ApplyBrushes(newPrimary, newSecondary, newTertiary);

            int ms = UiMotion.Duration(UiMotion.NumberRollMs, Motion.Reduced);
            if (!animate || Motion.Reduced || !Motion.Enabled)
            {
                newPrimary.Opacity = Target("AuroraPrimaryOpacity");
                newSecondary.Opacity = Target("AuroraSecondaryOpacity");
                newTertiary.Opacity = Target("AuroraTertiaryOpacity");
                oldPrimary.Opacity = 0;
                oldSecondary.Opacity = 0;
                oldTertiary.Opacity = 0;
                frontVisible = !frontVisible;
                return;
            }

            FadeTo(newPrimary, Target("AuroraPrimaryOpacity"), ms);
            FadeTo(newSecondary, Target("AuroraSecondaryOpacity"), ms);
            FadeTo(newTertiary, Target("AuroraTertiaryOpacity"), ms);
            FadeTo(oldPrimary, 0, ms);
            FadeTo(oldSecondary, 0, ms);
            FadeTo(oldTertiary, 0, ms);
            frontVisible = !frontVisible;
        }

        private static void ApplyBrushes(Ellipse primary, Ellipse secondary, Ellipse tertiary)
        {
            primary.Fill = (Brush)Application.Current.FindResource("AmbientPrimaryBrush");
            secondary.Fill = (Brush)Application.Current.FindResource("AmbientSecondaryBrush");
            tertiary.Fill = (Brush)Application.Current.FindResource("AmbientTertiaryBrush");
        }

        private static double Target(string opacityKey)
        {
            return (double)Application.Current.FindResource(opacityKey);
        }

        private static void FadeTo(Ellipse el, double target, int ms)
        {
            DoubleAnimation anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms));
            anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // 漂移：只动 RenderTransform（渲染线程，开销极低）；
        // 截图探针（Motion.Enabled=false）与系统降级时不启动
        private void StartDrift()
        {
            if (driftStarted) return;
            driftStarted = true;
            if (!Motion.Enabled || Motion.Reduced) return;
            double s = (double)Application.Current.FindResource("AuroraDriftSeconds");
            BeginDrift(FrontPrimary, 40, 30, s, 0);
            BeginDrift(FrontSecondary, -46, 26, s * 1.23, s * 0.3);
            BeginDrift(FrontTertiary, 36, -28, s * 0.85, s * 0.6);
            BeginDrift(BackPrimary, 40, 30, s, 0);
            BeginDrift(BackSecondary, -46, 26, s * 1.23, s * 0.3);
            BeginDrift(BackTertiary, 36, -28, s * 0.85, s * 0.6);
        }

        private static void BeginDrift(Ellipse el, double dx, double dy, double seconds, double beginDelay)
        {
            // 平移 + 缩放同属 RenderTransform，用 TransformGroup 组合（渲染线程，开销极低）
            var translate = new TranslateTransform();
            var scale = new ScaleTransform();
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            el.RenderTransform = group;
            el.RenderTransformOrigin = new Point(0.5, 0.5);

            // 平移：±dx/±dy，去同步（Y 时长 = X ×1.13）
            BeginAxis(translate, TranslateTransform.XProperty, dx, seconds, beginDelay);
            BeginAxis(translate, TranslateTransform.YProperty, dy, seconds * 1.13, beginDelay);

            // 缩放：1 ↔ 1.12 呼吸（规格 §4.2），用与平移略不同的时长再次去同步
            var scaleAnim = new DoubleAnimation(1.0, 1.12, TimeSpan.FromSeconds(seconds * 0.9))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginDelay),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            // 透明度呼吸（规格 §4.2 文中第三件套）不做：Opacity 已由 Show/TransitionTo
            // 管理（模式切换的交叉淡入淡出），再叠一个呼吸振荡会覆盖切换动画（WPF 同一属性
            // 只能有一个动画，后设的覆盖先设的）。平移 + 缩放已足以实现「活的云团」效果。
        }

        private static void BeginAxis(TranslateTransform tt, DependencyProperty prop,
            double target, double seconds, double beginDelay)
        {
            var anim = new DoubleAnimation(0, target, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginDelay),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            tt.BeginAnimation(prop, anim);
        }
    }
}
