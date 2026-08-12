// @author zenjiro 18967498922@163.com
// 文件用途 星点微光层（Aceternity Sparkles Core 移植）：低密度粒子闪烁 + 缓慢上浮，
// 颜色跟随模式 AccentGlowColor；尊重 Motion 降级策略（截图探针/系统减弱动效时静止）

using System;
using System.Windows;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Controls
{
    public sealed class SparkleLayer : FrameworkElement
    {
        private const int Count = 26;
        private const double FrameMs = 33; // ~30fps：闪烁不需要满帧率，省 UI 线程

        private sealed class Spark
        {
            public double X, Y;      // 0..1 相对位置（Y 随上浮递减）
            public double Radius;
            public double Phase;     // 闪烁相位
            public double Speed;     // 闪烁频率
            public double Drift;     // 上浮速度（相对高度/秒）
            public double Wobble;    // 水平摆动幅度（相对宽度）
        }

        private Spark[] sparks;
        private readonly Random rng = new Random();
        private Color color = Colors.White;
        private double maxAlpha = 0.5;
        private SolidColorBrush[] brushCache; // 16 档透明度缓存，避免每帧分配
        private TimeSpan lastFrame = TimeSpan.Zero;
        private double clock;
        private Window hostWindow;
        private bool renderingAttached;

        public SparkleLayer()
        {
            IsHitTestVisible = false;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sparks == null) InitSparks();
            RefreshColor();
            ThemeManager.ModeChanged += OnModeChanged;
            Motion.PolicyChanged += OnMotionPolicyChanged;
            AttachWindow(Window.GetWindow(this));
            UpdateRenderingState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopRendering();
            ThemeManager.ModeChanged -= OnModeChanged;
            Motion.PolicyChanged -= OnMotionPolicyChanged;
            AttachWindow(null);
        }

        private void OnMotionPolicyChanged(object sender, EventArgs e)
        {
            UpdateRenderingState();
            InvalidateVisual();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateRenderingState();
        }

        private void OnWindowActivityChanged(object sender, EventArgs e)
        {
            UpdateRenderingState();
        }

        private void AttachWindow(Window window)
        {
            if (hostWindow == window) return;
            if (hostWindow != null)
            {
                hostWindow.Activated -= OnWindowActivityChanged;
                hostWindow.Deactivated -= OnWindowActivityChanged;
                hostWindow.StateChanged -= OnWindowActivityChanged;
            }
            hostWindow = window;
            if (hostWindow != null)
            {
                hostWindow.Activated += OnWindowActivityChanged;
                hostWindow.Deactivated += OnWindowActivityChanged;
                hostWindow.StateChanged += OnWindowActivityChanged;
            }
        }

        private void UpdateRenderingState()
        {
            bool shouldRender = IsLoaded && IsVisible && hostWindow != null
                && hostWindow.IsActive && hostWindow.WindowState != WindowState.Minimized
                && Motion.Enabled && !Motion.Reduced;
            if (shouldRender) StartRendering(); else StopRendering();
        }

        private void StartRendering()
        {
            if (renderingAttached) return;
            lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
            renderingAttached = true;
        }

        private void StopRendering()
        {
            if (!renderingAttached) return;
            CompositionTarget.Rendering -= OnRendering;
            renderingAttached = false;
            lastFrame = TimeSpan.Zero;
            InvalidateVisual();
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            RefreshColor();
            InvalidateVisual();
        }

        private void RefreshColor()
        {
            object raw = Application.Current.TryFindResource("AccentGlowColor");
            if (raw is Color) color = (Color)raw;
            // 浅色下收敛星点亮度，避免白底显脏
            maxAlpha = ThemeManager.CurrentTone == UiTone.Light ? 0.30 : 0.5;
            brushCache = null;
        }

        private void InitSparks()
        {
            sparks = new Spark[Count];
            for (int i = 0; i < Count; i++)
            {
                sparks[i] = new Spark
                {
                    X = rng.NextDouble(),
                    Y = rng.NextDouble(),
                    Radius = 0.7 + rng.NextDouble() * 1.1,
                    Phase = rng.NextDouble() * Math.PI * 2,
                    Speed = 0.5 + rng.NextDouble() * 0.9,
                    Drift = 0.006 + rng.NextDouble() * 0.012,
                    Wobble = 0.004 + rng.NextDouble() * 0.01
                };
            }
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (!Motion.Enabled || Motion.Reduced || !IsVisible)
            {
                lastFrame = TimeSpan.Zero;
                return;
            }
            RenderingEventArgs re = e as RenderingEventArgs;
            TimeSpan now = re != null ? re.RenderingTime : TimeSpan.Zero;
            if (lastFrame != TimeSpan.Zero && (now - lastFrame).TotalMilliseconds < FrameMs) return;
            double dt = lastFrame == TimeSpan.Zero ? 0.033 : (now - lastFrame).TotalSeconds;
            lastFrame = now;
            clock += dt;
            foreach (Spark s in sparks)
            {
                s.Y -= s.Drift * dt;
                if (s.Y < -0.02) { s.Y = 1.02; s.X = rng.NextDouble(); }
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (!Motion.Enabled || Motion.Reduced || sparks == null) return;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            if (brushCache == null) brushCache = new SolidColorBrush[16];
            foreach (Spark s in sparks)
            {
                double twinkle = 0.5 + 0.5 * Math.Sin(clock * s.Speed * 2 + s.Phase);
                int step = (int)(15.99 * maxAlpha * (0.15 + 0.85 * twinkle));
                SolidColorBrush brush = brushCache[step];
                if (brush == null)
                {
                    byte a = (byte)(step * 16 + 8);
                    brush = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
                    brush.Freeze();
                    brushCache[step] = brush;
                }
                double x = (s.X + Math.Sin(clock * 0.6 + s.Phase) * s.Wobble) * w;
                dc.DrawEllipse(brush, null, new Point(x, s.Y * h), s.Radius, s.Radius);
            }
        }
    }
}
