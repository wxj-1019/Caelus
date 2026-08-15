// @author zenjiro 18967498922@163.com
// 文件用途 棉花糖启动屏：大胆有趣的启动动画编排，全部走渲染线程（变换/透明度），主线程泵消息时依旧顺滑

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CaelusApp.WpfHost
{
    public partial class SplashWindow : Window
    {
        private bool _closing;
        private bool _started;

        public SplashWindow()
        {
            InitializeComponent();
            // 按持久化主题着色：浅色=奶油底暖可可，深色=梅子夜奶油字（XAML 默认即深色）
            bool light = false;
            try { light = CaelusApp.Settings.Load("UiLight", false); } catch { }
            if (light)
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 251, 244, 238));
                LblHint.Foreground = new SolidColorBrush(Color.FromArgb(255, 107, 93, 85));
                BlobA.Opacity = 0.42;
                BlobB.Opacity = 0.38;
                BlobC.Opacity = 0.34;
            }
            BuildTitleLetters(light);
            Loaded += delegate { StartShow(); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int corner = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
            }
            catch { }
        }

        // 主窗口就绪后调用：软淡出再真正关闭，杜绝"闪断"
        public void CloseAnimated()
        {
            if (_closing) return;
            _closing = true;
            try
            {
                var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(240));
                fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                fade.Completed += delegate { SafeClose(); };
                Root.BeginAnimation(UIElement.OpacityProperty, fade);
                // 兜底：动画因任何原因未走完也要关窗，避免残留置顶透明窗
                var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                guard.Tick += delegate { guard.Stop(); SafeClose(); };
                guard.Start();
            }
            catch { SafeClose(); }
        }

        private void SafeClose()
        {
            try { Close(); } catch { }
        }

        private void StartShow()
        {
            if (_started) return;
            _started = true;

            // 整体快速淡入
            Root.Opacity = 0;
            Root.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));

            // 徽标弹性入场
            var logoOp = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260));
            logoOp.BeginTime = TimeSpan.FromMilliseconds(120);
            LogoStage.BeginAnimation(UIElement.OpacityProperty, logoOp);
            var logoScale = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(560));
            logoScale.BeginTime = TimeSpan.FromMilliseconds(120);
            logoScale.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 };
            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, logoScale);
            var logoScaleY = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(560));
            logoScaleY.BeginTime = TimeSpan.FromMilliseconds(120);
            logoScaleY.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 };
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, logoScaleY);

            // 三环差速反向旋转 + 轨道糖豆公转
            Spin(RingOuterRot, 0, -360, 14);
            Spin(RingARot, 0, 360, 8);
            Spin(RingBRot, 180, -180, 11);
            Spin(RingInnerRot, 0, 360, 20);
            Spin(OrbitRot, 0, 360, 7);

            // 棉花糖呼吸挤压（挤压与摇摆叠加，果冻感）
            Squish(MallowScale, 1.5);
            var wob = new DoubleAnimation(-4, 4, TimeSpan.FromSeconds(2.3));
            wob.AutoReverse = true;
            wob.RepeatBehavior = RepeatBehavior.Forever;
            wob.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            MallowRot.BeginAnimation(RotateTransform.AngleProperty, wob);

            // 极光软糖团低频漂移
            Drift(BlobAXf, 60, 40, 9);
            Drift(BlobBXf, -70, 30, 11);
            Drift(BlobCXf, 50, -35, 13);

            // 纸屑星星闪烁（错峰）
            Twinkle(SparkAXf, SparkA, 1.6, 0.5);
            Twinkle(SparkBXf, SparkB, 2.2, 1.1);
            Twinkle(SparkCXf, SparkC, 2.8, 1.7);
            Twinkle(SparkDXf, SparkD, 2.0, 0.8);

            // 提示语上浮淡入 + 慢呼吸
            var hintOp = new DoubleAnimation(0, 0.85, TimeSpan.FromMilliseconds(400));
            hintOp.BeginTime = TimeSpan.FromMilliseconds(750);
            LblHint.BeginAnimation(UIElement.OpacityProperty, hintOp);
            var hintY = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(450));
            hintY.BeginTime = TimeSpan.FromMilliseconds(750);
            hintY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            HintXf.BeginAnimation(TranslateTransform.YProperty, hintY);

            // 加载糖豆错峰蹦跳（带落地挤压）
            Hop(HopA, HopAXf, HopAScale, 0.00);
            Hop(HopB, HopBXf, HopBScale, 0.14);
            Hop(HopC, HopCXf, HopCScale, 0.28);
        }

        // 标题逐字构建 + 弹跳入场（约 70ms 错峰）
        private void BuildTitleLetters(bool light)
        {
            string word = "CAELUS";
            Color fg = light ? Color.FromRgb(43, 31, 26) : Color.FromRgb(247, 241, 234);
            TitleHost.Children.Clear();
            for (int i = 0; i < word.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = word.Substring(i, 1),
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(fg),
                    Margin = new Thickness(4, 0, 4, 0),
                    Opacity = 0,
                    RenderTransformOrigin = new Point(0.5, 1)
                };
                var g = new TransformGroup();
                var sc = new ScaleTransform(0.5, 0.5);
                var tr = new TranslateTransform(0, 16);
                g.Children.Add(sc);
                g.Children.Add(tr);
                tb.RenderTransform = g;
                TitleHost.Children.Add(tb);

                double begin = 0.42 + i * 0.07;
                var op = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280));
                op.BeginTime = TimeSpan.FromSeconds(begin);
                var ty = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(420));
                ty.BeginTime = TimeSpan.FromSeconds(begin);
                ty.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };
                var sx = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(420));
                sx.BeginTime = TimeSpan.FromSeconds(begin);
                sx.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };
                var sy = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(420));
                sy.BeginTime = TimeSpan.FromSeconds(begin);
                sy.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };

                int idx = i;
                Loaded += delegate
                {
                    tb.BeginAnimation(UIElement.OpacityProperty, op);
                    tr.BeginAnimation(TranslateTransform.YProperty, ty);
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
                };
            }
        }

        private static void Spin(RotateTransform rot, double from, double to, double seconds)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds));
            a.RepeatBehavior = RepeatBehavior.Forever;
            rot.BeginAnimation(RotateTransform.AngleProperty, a);
        }

        private static void Squish(ScaleTransform sc, double seconds)
        {
            var sx = new DoubleAnimation(1, 1.10, TimeSpan.FromSeconds(seconds));
            sx.AutoReverse = true;
            sx.RepeatBehavior = RepeatBehavior.Forever;
            sx.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            var sy = new DoubleAnimation(1, 0.90, TimeSpan.FromSeconds(seconds));
            sy.AutoReverse = true;
            sy.RepeatBehavior = RepeatBehavior.Forever;
            sy.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }

        private static void Drift(TranslateTransform xf, double dx, double dy, double seconds)
        {
            var ax = new DoubleAnimation(0, dx, TimeSpan.FromSeconds(seconds));
            ax.AutoReverse = true;
            ax.RepeatBehavior = RepeatBehavior.Forever;
            ax.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            xf.BeginAnimation(TranslateTransform.XProperty, ax);
            var ay = new DoubleAnimation(0, dy, TimeSpan.FromSeconds(seconds));
            ay.AutoReverse = true;
            ay.RepeatBehavior = RepeatBehavior.Forever;
            ay.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            xf.BeginAnimation(TranslateTransform.YProperty, ay);
        }

        private static void Twinkle(ScaleTransform sc, UIElement el, double seconds, double begin)
        {
            var s = new DoubleAnimation(0.3, 1.15, TimeSpan.FromSeconds(seconds));
            s.AutoReverse = true;
            s.RepeatBehavior = RepeatBehavior.Forever;
            s.BeginTime = TimeSpan.FromSeconds(begin);
            s.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, s);
            var s2 = new DoubleAnimation(0.3, 1.15, TimeSpan.FromSeconds(seconds));
            s2.AutoReverse = true;
            s2.RepeatBehavior = RepeatBehavior.Forever;
            s2.BeginTime = TimeSpan.FromSeconds(begin);
            s2.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, s2);

            var o = new DoubleAnimation(0, 0.9, TimeSpan.FromSeconds(seconds));
            o.AutoReverse = true;
            o.RepeatBehavior = RepeatBehavior.Forever;
            o.BeginTime = TimeSpan.FromSeconds(begin);
            o.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            el.BeginAnimation(UIElement.OpacityProperty, o);
        }

        private void Hop(UIElement dot, TranslateTransform xf, ScaleTransform sc, double begin)
        {
            var appear = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260));
            appear.BeginTime = TimeSpan.FromSeconds(1.0 + begin);
            dot.BeginAnimation(UIElement.OpacityProperty, appear);

            var up = new DoubleAnimation(0, -13, TimeSpan.FromMilliseconds(360));
            up.AutoReverse = true;
            up.RepeatBehavior = RepeatBehavior.Forever;
            up.BeginTime = TimeSpan.FromSeconds(1.0 + begin);
            up.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            xf.BeginAnimation(TranslateTransform.YProperty, up);

            // 起跳拉伸、落地回弹的纵向呼吸
            var stretch = new DoubleAnimation(1, 1.18, TimeSpan.FromMilliseconds(360));
            stretch.AutoReverse = true;
            stretch.RepeatBehavior = RepeatBehavior.Forever;
            stretch.BeginTime = TimeSpan.FromSeconds(1.0 + begin);
            stretch.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, stretch);
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
