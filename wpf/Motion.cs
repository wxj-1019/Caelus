// @author zenjiro 18967498922@163.com
// 文件用途 WPF 动效执行：Apple 式连续反馈、生命周期和系统策略降级

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CaelusApp.WpfHost
{
    internal static class Motion
    {
        public static bool Enabled = true;
        private const int InfiniteFps = 8;

        public static event EventHandler PolicyChanged;

        static Motion()
        {
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        }

        public static bool Reduced
        {
            get { return !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast; }
        }

        public static bool HighContrast
        {
            get { return SystemParameters.HighContrast; }
        }

        private static void OnSystemParametersChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "ClientAreaAnimation" && e.PropertyName != "HighContrast") return;
            EventHandler handler = PolicyChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        public static void Throttle(DoubleAnimation anim)
        {
            Timeline.SetDesiredFrameRate(anim, InfiniteFps);
        }

        public static void FadeIn(FrameworkElement element)
        {
            Reveal(element);
        }

        public static void Reveal(FrameworkElement element)
        {
            if (element == null) return;
            element.Opacity = 1;
            TranslateTransform translate = TranslateOf(element);
            translate.X = 0;
            if (!Enabled) return;

            int ms = UiMotion.Duration(UiMotion.PageFadeMs, Reduced);
            Animate(element, UIElement.OpacityProperty, 0, 1, ms);
            if (UiMotion.AllowsOffset(Reduced))
                Animate(translate, TranslateTransform.XProperty, 4, 0, ms);
        }

        public static void CrossFade(FrameworkElement element)
        {
            if (element == null || !Enabled) return;
            int ms = UiMotion.Duration(UiMotion.ModeChangeMs, Reduced);
            Animate(element, UIElement.OpacityProperty, 0.78, 1, ms);
        }

        public static void Emphasize(FrameworkElement element)
        {
            if (element == null || !Enabled) return;
            int ms = UiMotion.Duration(UiMotion.SuccessPopMs, Reduced);
            Animate(element, UIElement.OpacityProperty, 0.58, 1, ms);
            if (!UiMotion.AllowsScale(Reduced)) return;
            ScaleTransform scale = ScaleOf(element);
            Animate(scale, ScaleTransform.ScaleXProperty, 0.92, 1, ms);
            Animate(scale, ScaleTransform.ScaleYProperty, 0.92, 1, ms);
        }

        public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached(
            "Lift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnLiftChanged));

        public static bool GetLift(DependencyObject d) { return (bool)d.GetValue(LiftProperty); }
        public static void SetLift(DependencyObject d, bool value) { d.SetValue(LiftProperty, value); }

        private static void OnLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            UIElement element = d as UIElement;
            if (element == null) return;
            if ((bool)e.NewValue)
            {
                element.MouseEnter += OnLiftEnter;
                element.MouseLeave += OnLiftLeave;
            }
            else
            {
                element.MouseEnter -= OnLiftEnter;
                element.MouseLeave -= OnLiftLeave;
            }
        }

        private static void OnLiftEnter(object sender, MouseEventArgs e) { LiftTo((UIElement)sender, -2); }
        private static void OnLiftLeave(object sender, MouseEventArgs e) { LiftTo((UIElement)sender, 0); }

        private static void LiftTo(UIElement element, double y)
        {
            FrameworkElement fe = element as FrameworkElement;
            if (fe == null) return;
            TranslateTransform translate = TranslateOf(fe);
            if (!Enabled || Reduced)
            {
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
                return;
            }
            Animate(translate, TranslateTransform.YProperty, translate.Y, y, UiMotion.PageFadeMs);
        }

        public static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached(
            "Press", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnPressChanged));

        public static bool GetPress(DependencyObject d) { return (bool)d.GetValue(PressProperty); }
        public static void SetPress(DependencyObject d, bool value) { d.SetValue(PressProperty, value); }

        private static void OnPressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ButtonBase button = d as ButtonBase;
            if (button == null) return;
            if ((bool)e.NewValue)
            {
                button.PreviewMouseLeftButtonDown += OnPressDown;
                button.PreviewMouseLeftButtonUp += OnPressUp;
                button.MouseLeave += OnPressLeave;
                button.PreviewKeyDown += OnPressKeyDown;
                button.PreviewKeyUp += OnPressKeyUp;
            }
            else
            {
                button.PreviewMouseLeftButtonDown -= OnPressDown;
                button.PreviewMouseLeftButtonUp -= OnPressUp;
                button.MouseLeave -= OnPressLeave;
                button.PreviewKeyDown -= OnPressKeyDown;
                button.PreviewKeyUp -= OnPressKeyUp;
            }
        }

        private static void OnPressDown(object sender, MouseButtonEventArgs e) { PressTo((FrameworkElement)sender, 0.98); }
        private static void OnPressUp(object sender, MouseButtonEventArgs e) { PressTo((FrameworkElement)sender, 1); }
        private static void OnPressLeave(object sender, MouseEventArgs e) { PressTo((FrameworkElement)sender, 1); }
        private static void OnPressKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Enter) PressTo((FrameworkElement)sender, 0.98);
        }
        private static void OnPressKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Enter) PressTo((FrameworkElement)sender, 1);
        }

        private static void PressTo(FrameworkElement element, double value)
        {
            ScaleTransform scale = ScaleOf(element);
            if (!Enabled || Reduced)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                return;
            }
            Animate(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, value, UiMotion.ButtonPressMs);
            Animate(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, value, UiMotion.ButtonPressMs);
        }

        public static readonly DependencyProperty SmoothToggleProperty = DependencyProperty.RegisterAttached(
            "SmoothToggle", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnSmoothToggleChanged));

        public static bool GetSmoothToggle(DependencyObject d) { return (bool)d.GetValue(SmoothToggleProperty); }
        public static void SetSmoothToggle(DependencyObject d, bool value) { d.SetValue(SmoothToggleProperty, value); }

        private static void OnSmoothToggleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ToggleButton toggle = d as ToggleButton;
            if (toggle == null) return;
            if ((bool)e.NewValue)
            {
                toggle.Loaded += OnToggleLoaded;
                toggle.Checked += OnToggleStateChanged;
                toggle.Unchecked += OnToggleStateChanged;
            }
            else
            {
                toggle.Loaded -= OnToggleLoaded;
                toggle.Checked -= OnToggleStateChanged;
                toggle.Unchecked -= OnToggleStateChanged;
            }
        }

        private static void OnToggleLoaded(object sender, RoutedEventArgs e) { MoveToggleThumb((ToggleButton)sender, false); }
        private static void OnToggleStateChanged(object sender, RoutedEventArgs e) { MoveToggleThumb((ToggleButton)sender, true); }

        private static void MoveToggleThumb(ToggleButton toggle, bool animate)
        {
            toggle.ApplyTemplate();
            FrameworkElement thumb = toggle.Template.FindName("thumb", toggle) as FrameworkElement;
            if (thumb == null) return;
            TranslateTransform translate = thumb.RenderTransform as TranslateTransform;
            if (translate == null || translate.IsFrozen)
            {
                translate = new TranslateTransform();
                thumb.RenderTransform = translate;
            }
            double target = toggle.IsChecked == true ? 16 : 0;
            if (!animate || !Enabled || Reduced)
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.X = target;
                return;
            }
            Animate(translate, TranslateTransform.XProperty, translate.X, target, UiMotion.ToggleMs);
        }

        // Compatibility API: now a one-shot acknowledgement rather than a permanent pulse.
        public static void Pulse(UIElement element)
        {
            Emphasize(element as FrameworkElement);
        }

        public static void Spin(FrameworkElement element, double seconds, bool reverse)
        {
            if (element == null || !Enabled || Reduced) return;
            RotateTransform rotate = RotateOf(element);
            var animation = new DoubleAnimation(0, reverse ? -360 : 360, TimeSpan.FromSeconds(seconds))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            Throttle(animation);
            rotate.BeginAnimation(RotateTransform.AngleProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        public static void StopSpin(FrameworkElement element)
        {
            if (element == null) return;
            RotateTransform rotate = FindRotate(element.RenderTransform);
            if (rotate == null) return;
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = 0;
        }

        private static void Animate(UIElement target, DependencyProperty property,
            double from, double to, int milliseconds)
        {
            if (!Enabled || milliseconds <= 0)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
                return;
            }
            DoubleAnimation animation = BuildAnimation(from, to, milliseconds);
            animation.Completed += delegate
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
            };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void Animate(Animatable target, DependencyProperty property,
            double from, double to, int milliseconds)
        {
            if (!Enabled || milliseconds <= 0)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
                return;
            }
            DoubleAnimation animation = BuildAnimation(from, to, milliseconds);
            animation.Completed += delegate
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
            };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static DoubleAnimation BuildAnimation(double from, double to, int milliseconds)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
        }

        private static TransformGroup GroupOf(FrameworkElement element)
        {
            TransformGroup group = element.RenderTransform as TransformGroup;
            if (group != null) return group;
            group = new TransformGroup();
            Transform existing = element.RenderTransform;
            if (existing != null && existing != Transform.Identity) group.Children.Add(existing);
            element.RenderTransform = group;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            return group;
        }

        private static TranslateTransform TranslateOf(FrameworkElement element)
        {
            TransformGroup group = GroupOf(element);
            foreach (Transform transform in group.Children)
            {
                TranslateTransform translate = transform as TranslateTransform;
                if (translate != null) return translate;
            }
            var next = new TranslateTransform();
            group.Children.Add(next);
            return next;
        }

        private static ScaleTransform ScaleOf(FrameworkElement element)
        {
            TransformGroup group = GroupOf(element);
            foreach (Transform transform in group.Children)
            {
                ScaleTransform scale = transform as ScaleTransform;
                if (scale != null) return scale;
            }
            var next = new ScaleTransform(1, 1);
            group.Children.Insert(0, next);
            return next;
        }

        private static RotateTransform RotateOf(FrameworkElement element)
        {
            RotateTransform direct = element.RenderTransform as RotateTransform;
            if (direct != null) return direct;
            TransformGroup group = GroupOf(element);
            RotateTransform existing = FindRotate(group);
            if (existing != null) return existing;
            var next = new RotateTransform();
            group.Children.Add(next);
            return next;
        }

        private static RotateTransform FindRotate(Transform transform)
        {
            RotateTransform rotate = transform as RotateTransform;
            if (rotate != null) return rotate;
            TransformGroup group = transform as TransformGroup;
            if (group == null) return null;
            foreach (Transform child in group.Children)
            {
                rotate = child as RotateTransform;
                if (rotate != null) return rotate;
            }
            return null;
        }
    }
}
