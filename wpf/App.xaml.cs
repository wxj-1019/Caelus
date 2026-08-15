// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主入口：正常启动与 --wpf-shot 截图探针

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaelusApp.WpfHost
{
    public partial class App : Application
    {
        private System.Windows.Forms.NotifyIcon tray;

        protected override void OnExit(ExitEventArgs e)
        {
            Motion.PolicyChanged -= OnMotionPolicyChanged;
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
            Motion.PolicyChanged += OnMotionPolicyChanged;
            // 预览宿主不因单个绑定/布局异常静默丢窗口：记日志后标记已处理，界面保持存活
            DispatcherUnhandledException += (s, ex) =>
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(Path.GetTempPath(), "CaelusWpf.crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + ex.Exception + Environment.NewLine);
                }
                catch { }
                ex.Handled = true;
            };
            if (e.Args.Length >= 2 && e.Args[0] == "--wpf-shot")
            {
                int code = RunShot(e.Args[1]);
                Shutdown(code);
                return;
            }
            if (e.Args.Length >= 2 && e.Args[0] == "--wpf-motion-stress")
            {
                int code = RunMotionStress(e.Args[1]);
                Shutdown(code);
                return;
            }
            AppMode initial = ModeController.LoadPersisted();
            UiTone tone = Settings.Load("UiLight", false) ? UiTone.Light : UiTone.Dark;
            ThemeManager.Apply(this, tone, initial);
            ThemeManager.TryApplyUserTheme(this);
            Paths.Init();
            var gameCore = new SuppressionCore();
            var gameMode = new GameMode(Paths.Data, gameCore);
            MainWindow w = new MainWindow(gameMode);
            w.ApplyPersistedMode(initial);
            w.Show();
            // 托盘图标延迟到消息循环运行后创建（OnStartup 阶段 Dispatcher 尚未泵消息，
            // 此时创建的 NotifyIcon 不会在通知区域显示）
            Dispatcher.BeginInvoke(new Action(CreateTray));
        }

        private void OnMotionPolicyChanged(object sender, EventArgs e)
        {
            ThemeManager.Apply(this, ThemeManager.CurrentTone, ThemeManager.CurrentMode);
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
                Paths.Init();
                UiTone[] tones = new UiTone[] { UiTone.Dark, UiTone.Dark, UiTone.Dark, UiTone.Light };
                AppMode[] modes = new AppMode[] { AppMode.Standard, AppMode.Competitive, AppMode.Custom, AppMode.Standard };
                string[] names = new string[] { "dark-cruise", "dark-combat", "dark-custom", "light-cruise" };
                for (int i = 0; i < tones.Length; i++)
                {
                    ThemeManager.Apply(this, tones[i], modes[i]);
                    MainWindow w = new MainWindow(null);
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
                // 全部工作区页面的深色常规模式截图。
                ThemeManager.Apply(this, UiTone.Dark, AppMode.Standard);
                string[] pages = new string[]
                {
                    "library", "policy", "graphics", "anticheat", "environment",
                    "whitelist", "audit", "log", "settings", "about"
                };
                for (int i = 0; i < pages.Length; i++)
                    CapturePage(dir, pages[i]);
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(dir, "wpf-shot.error.txt"), ex.ToString()); } catch { }
                return 1;
            }
        }

        private int RunMotionStress(string outputPath)
        {
            try
            {
                Paths.Init();
                ThemeManager.Apply(this, UiTone.Dark, AppMode.Standard);
                MainWindow window = new MainWindow(new GameMode(Paths.Data, new SuppressionCore()));
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20000;
                window.Top = -20000;
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.Show();

                string[] pages = new string[]
                {
                    "overview", "library", "policy", "graphics", "anticheat",
                    "environment", "whitelist", "audit", "log", "settings", "about"
                };

                // Warm every cached page and theme before taking the baseline.
                for (int warm = 0; warm < 2; warm++)
                {
                    for (int i = 0; i < pages.Length; i++) window.NavigateToForShot(pages[i]);
                    window.SwitchModeForStress(AppMode.Competitive);
                    window.SwitchModeForStress(AppMode.Custom);
                    window.SwitchModeForStress(AppMode.Standard);
                }
                window.UpdateLayout();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Process process = Process.GetCurrentProcess();
                process.Refresh();
                long managedStart = GC.GetTotalMemory(true);
                long privateStart = process.PrivateMemorySize64;

                RunStressRound(window, pages);
                CollectAfterMotion();
                process.Refresh();
                long managedMid = GC.GetTotalMemory(true);
                long privateMid = process.PrivateMemorySize64;

                RunStressRound(window, pages);
                CollectAfterMotion();
                process.Refresh();
                long managedEnd = GC.GetTotalMemory(true);
                long privateEnd = process.PrivateMemorySize64;

                string report = "NAVIGATION_SWITCHES_PER_ROUND=110" + Environment.NewLine
                    + "MODE_SWITCHES_PER_ROUND=30" + Environment.NewLine
                    + "MANAGED_START_MB=" + Mb(managedStart) + Environment.NewLine
                    + "MANAGED_ROUND1_MB=" + Mb(managedMid) + Environment.NewLine
                    + "MANAGED_ROUND2_MB=" + Mb(managedEnd) + Environment.NewLine
                    + "MANAGED_ROUND1_DELTA_MB=" + Mb(managedMid - managedStart) + Environment.NewLine
                    + "MANAGED_ROUND2_DELTA_MB=" + Mb(managedEnd - managedMid) + Environment.NewLine
                    + "PRIVATE_START_MB=" + Mb(privateStart) + Environment.NewLine
                    + "PRIVATE_ROUND1_MB=" + Mb(privateMid) + Environment.NewLine
                    + "PRIVATE_ROUND2_MB=" + Mb(privateEnd) + Environment.NewLine
                    + "PRIVATE_ROUND1_DELTA_MB=" + Mb(privateMid - privateStart) + Environment.NewLine
                    + "PRIVATE_ROUND2_DELTA_MB=" + Mb(privateEnd - privateMid) + Environment.NewLine;
                File.WriteAllText(outputPath, report);
                window.Close();
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(outputPath + ".error.txt", ex.ToString()); } catch { }
                return 1;
            }
        }

        private static void RunStressRound(MainWindow window, string[] pages)
        {
            for (int round = 0; round < 10; round++)
                for (int i = 0; i < pages.Length; i++) window.NavigateToForShot(pages[i]);
            for (int round = 0; round < 10; round++)
            {
                window.SwitchModeForStress(AppMode.Competitive);
                window.SwitchModeForStress(AppMode.Custom);
                window.SwitchModeForStress(AppMode.Standard);
            }
            window.NavigateToForShot("overview");
            window.SwitchModeForStress(AppMode.Standard);
            window.UpdateLayout();
        }

        private static void CollectAfterMotion()
        {
            PumpDispatcher(UiMotion.SuccessPopMs + 150);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static string Mb(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0");
        }

        private static void PumpDispatcher(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds)
            };
            timer.Tick += delegate
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private void CapturePage(string dir, string page)
        {
            // 游戏库/策略/体检在探针下无真实数据，注入样例以捕获实机图（仅此路径生效）
            Views.LibraryView.InjectSampleData = (page == "library");
            Views.PolicyView.InjectSampleData = (page == "policy");
            Views.AuditView.InjectSampleData = (page == "audit");
            Views.GraphicsView.InjectSampleData = (page == "graphics");
            MainWindow window = new MainWindow(new GameMode(Paths.Data, new SuppressionCore()));
            window.ApplyPersistedMode(AppMode.Standard);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Show();
            FrameworkElement shown = window.NavigateToForShot(page);
            // 体检结果态无真实探测数据，导航到位后显式注入样例（避免 OnLoaded 静态标志时序问题）
            Views.AuditView auditShown = shown as Views.AuditView;
            if (auditShown != null) auditShown.ApplySampleResult();
            Size size = new Size(1196, 768);
            window.Measure(size);
            window.Arrange(new Rect(size));
            window.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(1196, 768, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string file = Path.Combine(dir, "wpf-" + page + "-dark-cruise.png");
            using (FileStream stream = File.Create(file)) encoder.Save(stream);
            window.Close();
        }
    }
}
