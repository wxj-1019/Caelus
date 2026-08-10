// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主入口：正常启动与 --wpf-shot 截图探针

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaelusApp.WpfHost
{
    public partial class App : Application
    {
        private System.Windows.Forms.NotifyIcon tray;

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            }
            catch { }
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (e.Args.Length >= 2 && e.Args[0] == "--wpf-shot")
            {
                int code = RunShot(e.Args[1]);
                Shutdown(code);
                return;
            }
            AppMode initial = ModeController.LoadPersisted();
            ThemeManager.Apply(this, UiTone.Dark, initial);
            MainWindow w = new MainWindow();
            w.ApplyPersistedMode(initial);
            w.Show();
            tray = new System.Windows.Forms.NotifyIcon
            {
                Text = "Caelus",
                Visible = true,
                Icon = System.Drawing.SystemIcons.Application
            };
        }

        // 离屏渲染深浅两个主题的概览页 PNG，供视觉验收
        private int RunShot(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                // 离屏渲染捕获最终视觉态：禁用进入动效，避免捕获到淡入起始帧（Opacity=0）
                Motion.Enabled = false;
                foreach (UiTone tone in new UiTone[] { UiTone.Light, UiTone.Dark })
                {
                    ThemeManager.Apply(this, tone, AppMode.Standard);
                    MainWindow w = new MainWindow(new SampleOverviewSource());
                    w.WindowStartupLocation = WindowStartupLocation.Manual;
                    w.Left = -20000;
                    w.Top = -20000;
                    w.ShowInTaskbar = false;
                    w.ShowActivated = false;
                    w.Show();
                    w.UpdateLayout();
                    Size size = new Size(1196, 768);
                    w.Measure(size);
                    w.Arrange(new Rect(size));
                    w.UpdateLayout();
                    RenderTargetBitmap rtb = new RenderTargetBitmap(1196, 768, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(w);
                    PngBitmapEncoder enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    string file = Path.Combine(dir, "wpf-overview-" +
                        (tone == UiTone.Light ? "light" : "dark") + ".png");
                    using (FileStream fs = File.Create(file)) enc.Save(fs);
                    w.Close();
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(dir, "wpf-shot.error.txt"), ex.ToString()); } catch { }
                return 1;
            }
        }
    }
}
