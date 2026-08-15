// @author zenjiro 18967498922@163.com
// 文件用途 棉花糖启动屏：独立 STA 线程自绘，主线程重载期间动画不冻结

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CaelusApp.WpfHost
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            // 按持久化主题着色：浅色=奶油底暖可可，深色=梅子夜奶油字（XAML 默认即深色）
            bool light = false;
            try { light = CaelusApp.Settings.Load("UiLight", false); } catch { }
            if (light)
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 251, 244, 238));
                LblTitle.Foreground = new SolidColorBrush(Color.FromArgb(255, 43, 31, 26));
                LblHint.Foreground = new SolidColorBrush(Color.FromArgb(255, 107, 93, 85));
            }
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

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
