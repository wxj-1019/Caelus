// @author zenjiro 18967498922@163.com
// 文件用途 Spotlight 聚光卡片（React Bits Spotlight Card 移植）：鼠标跟随的径向柔光。
// 用 Adorner 实现，对现有卡片样式零模板侵入；颜色跟随模式 AccentGlowColor，
// 深色峰值 16% / 浅色 10%，只作"贵气"提示，不干扰阅读。

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CaelusApp.WpfHost.Controls;

namespace CaelusApp.WpfHost
{
    internal static class Spotlight
    {
        public static readonly DependencyProperty GlowProperty = DependencyProperty.RegisterAttached(
            "Glow", typeof(bool), typeof(Spotlight), new PropertyMetadata(false, OnGlowChanged));

        public static bool GetGlow(DependencyObject d) { return (bool)d.GetValue(GlowProperty); }
        public static void SetGlow(DependencyObject d, bool value) { d.SetValue(GlowProperty, value); }

        // 每个宿主元素对应的 Adorner（私有附加属性槽）
        private static readonly DependencyProperty AdornerSlotProperty = DependencyProperty.RegisterAttached(
            "AdornerSlot", typeof(SpotlightAdorner), typeof(Spotlight), new PropertyMetadata(null));

        private static void OnGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            UIElement el = d as UIElement;
            if (el == null) return;
            FrameworkElement host = d as FrameworkElement; // Unloaded 事件在 FrameworkElement 上
            if ((bool)e.NewValue)
            {
                el.MouseEnter += OnEnter;
                el.MouseLeave += OnLeave;
                el.MouseMove += OnMove;
                if (host != null) host.Unloaded += OnHostUnloaded;
            }
            else
            {
                el.MouseEnter -= OnEnter;
                el.MouseLeave -= OnLeave;
                el.MouseMove -= OnMove;
                if (host != null) host.Unloaded -= OnHostUnloaded;
            }
        }

        private static void OnEnter(object sender, MouseEventArgs e)
        {
            UIElement el = (UIElement)sender;
            SpotlightAdorner adorner = EnsureAdorner(el);
            if (adorner == null) return;
            adorner.Position = e.GetPosition(el);
            FadeTo(adorner, Motion.HighContrast ? 0 : 1, 200);
        }

        private static void OnMove(object sender, MouseEventArgs e)
        {
            UIElement el = (UIElement)sender;
            SpotlightAdorner adorner = el.GetValue(AdornerSlotProperty) as SpotlightAdorner;
            if (adorner == null) return;
            adorner.Position = e.GetPosition(el);
            if (adorner.Opacity > 0.01) adorner.InvalidateVisual();
        }

        private static void OnLeave(object sender, MouseEventArgs e)
        {
            UIElement el = (UIElement)sender;
            SpotlightAdorner adorner = el.GetValue(AdornerSlotProperty) as SpotlightAdorner;
            if (adorner != null) FadeTo(adorner, 0, 320);
        }

        private static void OnHostUnloaded(object sender, RoutedEventArgs e)
        {
            UIElement el = (UIElement)sender;
            SpotlightAdorner adorner = el.GetValue(AdornerSlotProperty) as SpotlightAdorner;
            if (adorner == null) return;
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(el);
            if (layer != null) layer.Remove(adorner);
            el.ClearValue(AdornerSlotProperty);
        }

        private static SpotlightAdorner EnsureAdorner(UIElement el)
        {
            SpotlightAdorner adorner = el.GetValue(AdornerSlotProperty) as SpotlightAdorner;
            if (adorner != null) return adorner;
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(el);
            if (layer == null) return null; // 尚未上树（MouseEnter 时必有层，兜底防御）
            adorner = new SpotlightAdorner(el);
            layer.Add(adorner);
            el.SetValue(AdornerSlotProperty, adorner);
            return adorner;
        }

        private static void FadeTo(SpotlightAdorner adorner, double target, int ms)
        {
            if (Motion.HighContrast) target = 0;
            if (!Motion.Enabled || Motion.Reduced || ms <= 0)
            {
                adorner.BeginAnimation(UIElement.OpacityProperty, null);
                adorner.Opacity = target;
                return;
            }
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            adorner.BeginAnimation(UIElement.OpacityProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }
    }

    internal sealed class SpotlightAdorner : Adorner
    {
        public Point Position;
        private readonly double radius;

        public SpotlightAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            Opacity = 0;
            radius = ResolveRadius(adornedElement);
        }

        private static double ResolveRadius(UIElement el)
        {
            Border border = el as Border;
            if (border != null) return border.CornerRadius.TopLeft;
            GlassCard card = el as GlassCard;
            if (card != null) return card.CornerRadius.TopLeft;
            return 10;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Opacity <= 0.01) return;
            Rect rect = new Rect(RenderSize);
            if (rect.Width < 2 || rect.Height < 2) return;

            object raw = Application.Current.TryFindResource("AccentGlowColor");
            Color c = raw is Color ? (Color)raw : Colors.White;
            double peak = ThemeManager.CurrentTone == UiTone.Light ? 0.10 : 0.16;

            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = Position,
                GradientOrigin = Position,
                RadiusX = 170,
                RadiusY = 170
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * peak), c.R, c.G, c.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            brush.Freeze();

            dc.PushClip(new RectangleGeometry(rect, radius, radius));
            dc.DrawRectangle(brush, null, rect);
            dc.Pop();
        }
    }
}
