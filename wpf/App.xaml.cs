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
            // 迭代测试期间的异常观测点（问题解决后评估保留）
            DispatcherUnhandledException += (s, ex) =>
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(Path.GetTempPath(), "CaelusWpf.crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + ex.Exception + Environment.NewLine);
                }
                catch { }
            };
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
            // 托盘图标延迟到消息循环运行后创建（OnStartup 阶段 Dispatcher 尚未泵消息，
            // 此时创建的 NotifyIcon 不会在通知区域显示）
            Dispatcher.BeginInvoke(new Action(CreateTray));
        }

        private void CreateTray()
        {
            tray = new System.Windows.Forms.NotifyIcon
            {
                Text = "Caelus",
                Visible = true,
                Icon = System.Drawing.SystemIcons.Application
            };
        }

        // 离屏渲染模式×主题矩阵 PNG，供视觉验收与回归基线（规格 §7.5）
        private int RunShot(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                // 离屏渲染捕获最终视觉态：禁用进入动效，避免捕获到淡入起始帧（Opacity=0）
                Motion.Enabled = false;
                UiTone[] tones = new UiTone[] { UiTone.Dark, UiTone.Dark, UiTone.Dark, UiTone.Light };
                AppMode[] modes = new AppMode[] { AppMode.Standard, AppMode.Competitive, AppMode.Custom, AppMode.Standard };
                string[] names = new string[] { "dark-cruise", "dark-combat", "dark-custom", "light-cruise" };
                for (int i = 0; i < tones.Length; i++)
                {
                    ThemeManager.Apply(this, tones[i], modes[i]);
                    MainWindow w = new MainWindow(new SampleOverviewSource());
                    w.ApplyPersistedMode(modes[i]);
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
                    string file = Path.Combine(dir, "wpf-overview-" + names[i] + ".png");
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
