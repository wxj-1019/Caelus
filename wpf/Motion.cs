// @author zenjiro 18967498922@163.com
// 文件用途 WPF 动效执行：Apple 式连续反馈、生命周期和系统策略降级

using System;
using System.Collections.Generic;
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
        private const int InfiniteFps = 15;
        private static readonly List<WeakReference> pulseTargets = new List<WeakReference>();
        private static readonly List<ScaleBreathTarget> scaleBreathTargets = new List<ScaleBreathTarget>();

        private sealed class ScaleBreathTarget
        {
            public WeakReference Element;
            public double From;
            public double To;
            public int Seconds;
        }

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

        // .NET 4 参考程序集不含 LiveSetting；支持该 UIA API 的系统上用反射启用，旧系统安全降级。
        public static void SetPoliteLiveSetting(DependencyObject element)
        {
            if (element == null) return;
            try
            {
                Type propertiesType = typeof(System.Windows.Automation.AutomationProperties);
                System.Reflection.MethodInfo setter = propertiesType.GetMethod("SetLiveSetting");
                if (setter == null) return;
                Type enumType = setter.GetParameters()[1].ParameterType;
                object polite = Enum.Parse(enumType, "Polite");
                setter.Invoke(null, new object[] { element, polite });
            }
            catch { }
        }

        private static void OnSystemParametersChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "ClientAreaAnimation" && e.PropertyName != "HighContrast") return;
            RefreshInfiniteAnimations();
            EventHandler handler = PolicyChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static void RefreshInfiniteAnimations()
        {
            for (int i = pulseTargets.Count - 1; i >= 0; i--)
            {
                UIElement element = pulseTargets[i].Target as UIElement;
                if (element == null) { pulseTargets.RemoveAt(i); continue; }
                StartBreathPulse(element);
            }
            for (int i = scaleBreathTargets.Count - 1; i >= 0; i--)
            {
                FrameworkElement element = scaleBreathTargets[i].Element.Target as FrameworkElement;
                if (element == null) { scaleBreathTargets.RemoveAt(i); continue; }
                ScaleBreathTarget target = scaleBreathTargets[i];
                StartBreathScale(element, target.From, target.To, target.Seconds);
            }
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
            // 从当前 Opacity 开始，避免已可见元素重复调用时闪黑
            double fromOpacity = element.Opacity;
            element.Opacity = 1;
            TranslateTransform translate = TranslateOf(element);
            translate.X = 0;
            if (!Enabled) return;

            int ms = UiMotion.Duration(UiMotion.PageFadeMs, Reduced);
            Animate(element, UIElement.OpacityProperty, fromOpacity, 1, ms);
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

        // 分区入场：透明度 + 上浮 10px，可带延迟做 staggered 编排（reduced 时直接落位）
        public static void RiseIn(FrameworkElement element, int delayMs)
        {
            if (element == null) return;
            if (!Enabled || Reduced)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                TranslateTransform settled = TranslateOf(element);
                settled.BeginAnimation(TranslateTransform.YProperty, null);
                settled.Y = 0;
                ScaleTransform settledScale = ScaleOf(element);
                settledScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                settledScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                settledScale.ScaleX = 1;
                settledScale.ScaleY = 1;
                return;
            }
            int ms = UiMotion.PageFadeMs;
            element.Opacity = 0;
            AnimateDelayed(element, UIElement.OpacityProperty, 0, 1, ms, delayMs);
            TranslateTransform translate = TranslateOf(element);
            translate.Y = 10;
            // 位移走弹簧曲线（轻微过冲回正 = iOS 入场手感）；opacity 仍走标准减速
            AnimateSpringDelayed(translate, TranslateTransform.YProperty, 10, 0, ms, delayMs);
            // 轻微缩放落定（0.96→1，标准减速不过冲）= 材质感入场；reduced 时上方已置 1
            ScaleTransform scale = ScaleOf(element);
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.96;
            AnimateDelayed(scale, ScaleTransform.ScaleXProperty, 0.96, 1, ms, delayMs);
            AnimateDelayed(scale, ScaleTransform.ScaleYProperty, 0.96, 1, ms, delayMs);
        }

        // 占比条入场生长：左端点为原点 scaleX 0→1（reduced 时直接满宽）
        public static void GrowX(FrameworkElement element, int delayMs)
        {
            if (element == null) return;
            ScaleTransform scale = ScaleOf(element);
            element.RenderTransformOrigin = new Point(0, 0.5);
            if (!Enabled || Reduced)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.ScaleX = 1;
                return;
            }
            scale.ScaleX = 0;
            AnimateDelayed(scale, ScaleTransform.ScaleXProperty, 0, 1, 800, delayMs);
        }

        // 状态点呼吸脉冲：2s 往返透明度，无限动画按惯例限帧 8fps
        public static void BreathPulse(UIElement element)
        {
            if (element == null) return;
            RegisterPulse(element);
            StartBreathPulse(element);
        }

        private static void RegisterPulse(UIElement element)
        {
            foreach (WeakReference reference in pulseTargets)
                if (ReferenceEquals(reference.Target, element)) return;
            pulseTargets.Add(new WeakReference(element));
        }

        private static void StartBreathPulse(UIElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (!Enabled || Reduced)
            {
                element.Opacity = 1;
                return;
            }
            var animation = new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Throttle(animation);
            element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        // 缩放呼吸（空态主图标邀请感）：fromScale↔toScale 往返，居中缩放
        public static void BreathScale(FrameworkElement element, double fromScale, double toScale, int seconds)
        {
            if (element == null) return;
            RegisterScaleBreath(element, fromScale, toScale, seconds);
            StartBreathScale(element, fromScale, toScale, seconds);
        }

        private static void RegisterScaleBreath(FrameworkElement element, double fromScale, double toScale, int seconds)
        {
            foreach (ScaleBreathTarget target in scaleBreathTargets)
            {
                if (!ReferenceEquals(target.Element.Target, element)) continue;
                target.From = fromScale; target.To = toScale; target.Seconds = seconds;
                return;
            }
            scaleBreathTargets.Add(new ScaleBreathTarget
            {
                Element = new WeakReference(element), From = fromScale, To = toScale, Seconds = seconds
            });
        }

        private static void StartBreathScale(FrameworkElement element, double fromScale, double toScale, int seconds)
        {
            ScaleTransform scale = ScaleOf(element);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            if (!Enabled || Reduced)
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                return;
            }
            var animation = new DoubleAnimation(fromScale, toScale, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Throttle(animation);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
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

        private static void OnLiftEnter(object sender, MouseEventArgs e) { LiftTo((UIElement)sender, -2, 1.02); }
        private static void OnLiftLeave(object sender, MouseEventArgs e) { LiftTo((UIElement)sender, 0, 1.0); }

        // 悬停：上浮 y px + 轻微放大 scale（90ms 减速，平滑过渡）。reduced 时直接复位。
        private static void LiftTo(UIElement element, double y, double scale)
        {
            FrameworkElement fe = element as FrameworkElement;
            if (fe == null) return;
            TranslateTransform translate = TranslateOf(fe);
            ScaleTransform sc = ScaleOf(fe);
            if (!Enabled || Reduced)
            {
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                sc.ScaleX = 1;
                sc.ScaleY = 1;
                return;
            }
            Animate(translate, TranslateTransform.YProperty, translate.Y, y, UiMotion.ButtonPressMs);
            Animate(sc, ScaleTransform.ScaleXProperty, sc.ScaleX, scale, UiMotion.ButtonPressMs);
            Animate(sc, ScaleTransform.ScaleYProperty, sc.ScaleY, scale, UiMotion.ButtonPressMs);
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
                button.MouseEnter += OnPressEnter;
                button.MouseLeave += OnPressLeave;
                button.PreviewKeyDown += OnPressKeyDown;
                button.PreviewKeyUp += OnPressKeyUp;
            }
            else
            {
                button.PreviewMouseLeftButtonDown -= OnPressDown;
                button.PreviewMouseLeftButtonUp -= OnPressUp;
                button.MouseEnter -= OnPressEnter;
                button.MouseLeave -= OnPressLeave;
                button.PreviewKeyDown -= OnPressKeyDown;
                button.PreviewKeyUp -= OnPressKeyUp;
            }
        }

        private static void OnPressDown(object sender, MouseButtonEventArgs e) { PressTo((FrameworkElement)sender, 0.98); }
        private static void OnPressUp(object sender, MouseButtonEventArgs e) { PressTo((FrameworkElement)sender, 1); }
        private static void OnPressEnter(object sender, MouseEventArgs e)
        {
            ButtonBase button = sender as ButtonBase;
            if (button == null || !Enabled || Reduced) return;
            button.ApplyTemplate();
            FrameworkElement sheen = button.Template.FindName("sheen", button) as FrameworkElement;
            if (sheen == null) return; // Ghost/Danger/WindowButton 没有扫光层
            TranslateTransform translate = sheen.RenderTransform as TranslateTransform;
            if (translate == null) return;
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            sheen.BeginAnimation(UIElement.OpacityProperty, null);
            translate.X = -200;
            sheen.Opacity = 0;

            var move = new DoubleAnimation(-200, 360, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.Zero));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(120)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(500)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(700)));
            translate.BeginAnimation(TranslateTransform.XProperty, move, HandoffBehavior.SnapshotAndReplace);
            sheen.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
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
            // QuinticEase EaseOut：比 CubicEase 更陡的"快起长收"，接近 iOS 默认减速曲线，
            // 让入场/交互动画更有苹果质感；无过冲，对占比条/缩放等也安全。
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
        }

        // 弹性回弹变体：仅用于入场位移（RiseIn 的 translate），轻微过冲后回正 = iOS 弹簧手感。
        // 不用于 opacity（过冲被 clamp 无意义）与占比条（过冲宽度显怪）。
        private static DoubleAnimation BuildSpringAnimation(double from, double to, int milliseconds)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
        }

        // 带 BeginTime 延迟的 Animate 变体：延迟期间目标保持调用方预设的初值
        private static void AnimateDelayed(UIElement target, DependencyProperty property,
            double from, double to, int milliseconds, int delayMs)
        {
            if (!Enabled || milliseconds <= 0)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
                return;
            }
            DoubleAnimation animation = BuildAnimation(from, to, milliseconds);
            if (delayMs > 0) animation.BeginTime = TimeSpan.FromMilliseconds(delayMs);
            animation.Completed += delegate
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
            };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateDelayed(Animatable target, DependencyProperty property,
            double from, double to, int milliseconds, int delayMs)
        {
            if (!Enabled || milliseconds <= 0)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
                return;
            }
            DoubleAnimation animation = BuildAnimation(from, to, milliseconds);
            if (delayMs > 0) animation.BeginTime = TimeSpan.FromMilliseconds(delayMs);
            animation.Completed += delegate
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
            };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        // 弹性版（入场位移用）：BuildSpringAnimation 轻微过冲后回正
        private static void AnimateSpringDelayed(Animatable target, DependencyProperty property,
            double from, double to, int milliseconds, int delayMs)
        {
            if (!Enabled || milliseconds <= 0)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
                return;
            }
            DoubleAnimation animation = BuildSpringAnimation(from, to, milliseconds);
            if (delayMs > 0) animation.BeginTime = TimeSpan.FromMilliseconds(delayMs);
            animation.Completed += delegate
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
            };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
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
