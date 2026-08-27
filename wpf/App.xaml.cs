// @author zenjiro 18967498922@163.com
// 文件用途 WPF 宿主入口：正式运行时启动（单实例/提权/自愈/运行时/托盘）与 --wpf-shot 截图探针

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CaelusApp.WpfHost
{
    public partial class App : Application
    {
        private const string PendingPanelKey = "ShowPanelOnNextStart";

        private System.Windows.Forms.NotifyIcon tray;
        private Mutex mutex;
        private WpfRuntimeHost host;
        private MainWindow window;
        private EventWaitHandle showEvt;
        private EventWaitHandle exitEvt;
        private bool elevated;
        private DispatcherTimer trayIconTimer;
        private bool realExit;

        protected override void OnExit(ExitEventArgs e)
        {
            Motion.PolicyChanged -= OnMotionPolicyChanged;
            try
            {
                if (trayIconTimer != null) { trayIconTimer.Stop(); trayIconTimer = null; }
            }
            catch { }
            try
            {
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            }
            catch { }
            if (!realExit && host != null)
            {
                try { host.Shutdown(); } catch { }
            }
            try { if (mutex != null) mutex.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Motion.PolicyChanged += OnMotionPolicyChanged;
            // 非 UI 线程未捕获异常（procNotify 回调等）：与 WinForms 版同记 crash.log（进程仍按系统默认终止）
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                string cdir;
                try { cdir = Paths.Data; } catch { cdir = null; }
                if (string.IsNullOrEmpty(cdir)) cdir = Path.GetTempPath();
                try
                {
                    File.AppendAllText(Path.Combine(cdir, "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [WPF] " + ex.ExceptionObject + Environment.NewLine);
                }
                catch { }
            };
            // 宿主不因单个绑定/布局异常静默丢窗口：记日志后标记已处理，界面保持存活
            DispatcherUnhandledException += (s, ex) =>
            {
                // 与 WinForms 版同一 crash.log（数据目录）；数据目录未就绪时回退临时目录
                string logDir;
                try { logDir = Paths.Data ?? Path.GetTempPath(); } catch { logDir = Path.GetTempPath(); }
                try
                {
                    File.AppendAllText(Path.Combine(logDir, "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [WPF] " + ex.Exception + Environment.NewLine);
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
            if (e.Args.Length >= 3 && e.Args[0] == "--screenshot")
            {
                int code = RunSingleShot(e.Args[1], e.Args[2]);
                Shutdown(code);
                return;
            }
            if (e.Args.Length >= 1 && e.Args[0] == "--ui-preview")
            {
                RunUiPreview();
                return;
            }

            // 工具参数（--genicon/--geniconpng/--freeze-watchdog）与自测入口（自测版编译）
            if (WpfRuntimeHost.HandleEarlyExit(e.Args))
            {
                Shutdown();
                return;
            }

            // 单实例：已有实例时触发其面板并退出
            mutex = WpfRuntimeHost.AcquireSingleInstance();
            if (mutex == null)
            {
                Shutdown();
                return;
            }

            bool autoStarted = false;
            if (e.Args != null)
                foreach (string a in e.Args)
                    if (string.Equals(a, TaskHelper.AutostartArgument, StringComparison.OrdinalIgnoreCase))
                        autoStarted = true;
            SetAutoStarted(autoStarted);

            elevated = WpfRuntimeHost.IsElevated();

            // 未提权且有开机自启任务：走计划任务提升重启（与 src/Program.cs 一致）
            if (!elevated && TaskHelper.TaskExists())
            {
                try { mutex.ReleaseMutex(); mutex.Close(); } catch { }
                mutex = null;
                Settings.Save(PendingPanelKey, true);
                if (TaskHelper.Run("/Run /TN " + TaskHelper.TaskName) == 0)
                {
                    Shutdown();
                    return;
                }
                Settings.Save(PendingPanelKey, false);
                mutex = WpfRuntimeHost.AcquireSingleInstance();
                if (mutex == null)
                {
                    Shutdown();
                    return;
                }
            }

            Paths.Init();
            Lang.Init();
            string dir = Paths.Data;
            Logger.LogPath = Path.Combine(dir, "Caelus.log");
            Settings.Remove("EvidenceMode");

            // UiShared 的概览 ViewModel 通过该钩子把后台结果调度回 UI 线程
            OverviewViewModel.PostToUi = a => Dispatcher.BeginInvoke(new Action(a));

            AppMode initial = ModeController.LoadPersisted();
            UiTone tone = ThemeManager.ResolveTone();
            ThemeManager.Apply(this, tone, initial);
            ThemeManager.TryApplyUserTheme(this);

            // 棉花糖启动屏：强制首帧渲染后立即把消息循环还给调度器，启动动画全速滚动；
            // 自愈链 + GameMode/Tamer 启动在 host.Boot 的后台线程里完成，全部就绪才
            // 构建主窗口（边构建边泵送）并淡出启动屏。
            var splash = new SplashWindow();
            splash.Show();
            splash.UpdateLayout();
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));

            host = new WpfRuntimeHost(dir);
            host.Boot(delegate
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    try { BuildMainWindow(initial, splash); }
                    catch (Exception ex)
                    {
                        try { File.AppendAllText(Path.Combine(dir, "crash.log"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [WPF boot] " + ex + Environment.NewLine); } catch { }
                        // 主窗口构建失败时不能带着已启动的压制引擎隐身常驻（无窗口、无托盘、
                        // 启动屏卡死，用户没有任何入口发现或退出）——关闭启动屏并完整退出
                        try { splash.Close(); } catch { }
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                        {
                            realExit = true;
                            try { if (host != null) host.Shutdown(); } catch { }
                            try { Shutdown(); } catch { }
                        }));
                    }
                }));
            });

            // ShowPanel/Exit 全局事件（每用户 ACL，防其它本地用户诱导弹窗/退出）
            showEvt = WpfRuntimeHost.CreateGuardedEvent("Global\\Caelus_ShowPanel");
            exitEvt = WpfRuntimeHost.CreateGuardedEvent("Global\\Caelus_Exit");

            Thread evtThread = new Thread(new ThreadStart(delegate
            {
                while (true)
                {
                    showEvt.WaitOne();
                    try { Dispatcher.BeginInvoke(new Action(ShowPanel)); } catch { }
                }
            }));
            evtThread.IsBackground = true;
            evtThread.Start();

            Thread exitThread = new Thread(new ThreadStart(delegate
            {
                exitEvt.WaitOne();
                try { Dispatcher.BeginInvoke(new Action(DoExit)); } catch { }
            }));
            exitThread.IsBackground = true;
            exitThread.Start();

            SystemEvents.SessionEnded += OnSessionEnded;

            if (elevated)
                ThreadPool.QueueUserWorkItem(delegate { TaskHelper.RefreshStartupTask(); });

            // 启动后延迟检查一次更新（不阻塞启动）
            DispatcherTimer updTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(6)
            };
            updTimer.Tick += delegate
            {
                updTimer.Stop();
                UpdateChecker.CheckAsync(r =>
                {
                    if (r.Ok && r.Newer)
                    {
                        Logger.Log("启动检查更新：发现新版本 " + r.Latest + "（当前 " + CaelusApp.App.VersionTag + "）");
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                try { ShowBalloon(Lang.F("bal.update", r.Latest), 8000); } catch { }
                            }));
                        }
                        catch { }
                    }
                    else if (r.Ok) Logger.Log("启动检查更新：已是最新版本（" + CaelusApp.App.VersionTag + "）");
                    else Logger.Log("启动检查更新失败：" + r.Error);
                });
            };
            updTimer.Start();
        }

        private void BuildMainWindow(AppMode initial, SplashWindow splash)
        {
            CaelusApp.WpfHost.MainWindow.ProgressPump = delegate
            {
                Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
            };
            window = new MainWindow(host.GameMode, host.Tamer,
                new ScenarioStatusSource(host.GameMode, host.Tamer, host.Core,
                    host.Arbiter, host.DevFocus, host.DailyCare),
                host.DevFocus, host.DailyCare);
            CaelusApp.WpfHost.MainWindow.ProgressPump = null;
            window.ApplyPersistedMode(initial);
            window.Show();
            // 启动屏先 Show，会成为 Application.MainWindow；归位给真正的主窗口
            MainWindow = window;
            splash.CloseAnimated();
            CreateTray();
            SubscribeRuntimeEvents();

            bool pendingPanel = Settings.Load(PendingPanelKey, false);
            if (pendingPanel) Settings.Save(PendingPanelKey, false);
            bool showingPanel = !WasAutoStarted || pendingPanel;
            if (!showingPanel && window != null)
            {
                // 开机自启：静默驻留托盘，不弹主窗口
                window.Hide();
            }
            else if (window != null && !window.IsVisible)
            {
                ShowPanel();
            }
            // 新版本首次启动弹发布说明（显示即标记已读）；刻意差异：WPF 在窗口显示后带 owner 弹出
            // （居中于主窗口、不抢占前台），WinForms 在 ShowPanel 前无 owner，功能等价
            if (showingPanel && ReleaseNotes.HasUnseen && window != null && window.IsVisible)
            {
                try
                {
                    var notes = new Dialogs.ReleaseNotesDialogWpf();
                    notes.Owner = window;
                    notes.ShowDialog();
                    try { window.SyncAllToggles(); } catch { }
                }
                catch { }
            }
        }

        private bool WasAutoStarted;
        internal void SetAutoStarted(bool value) { WasAutoStarted = value; }

        private void CreateTray()
        {
            if (tray != null) return;
            tray = new System.Windows.Forms.NotifyIcon();
            RefreshTrayIcon(true);
            tray.Text = elevated ? Lang.T("tray.idle") : Lang.T("tray.noelev");
            tray.ContextMenuStrip = host.BuildTrayMenu(
                ShowPanel, DoExit, delegate { try { if (window != null) window.SyncAllToggles(); } catch { } });
            tray.DoubleClick += (s, e) => ShowPanel();
            tray.Visible = true;
            if (!elevated)
                tray.ShowBalloonTip(8000, CaelusApp.App.DisplayName, Lang.T("bal.noelev"), System.Windows.Forms.ToolTipIcon.Warning);

            trayIconTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            trayIconTimer.Tick += delegate { RefreshTrayIcon(false); };
            trayIconTimer.Start();
        }

        private void RefreshTrayIcon(bool force)
        {
            if (tray == null || host == null) return;
            // 托盘文本依赖 ActiveGame/ArmedGame（不依赖 preset），每 tick 必重算（对齐 WinForms）；
            // 图标仅在 preset/开关变化时重建
            string text;
            if (!elevated) text = Lang.T("tray.noelev");
            else
            {
                string g = host.GameMode.ActiveGame;
                string a = g == null ? host.GameMode.ArmedGame : null;
                text = g != null ? Lang.F("tray.active", g)
                    : (a != null ? Lang.F("tray.armed", a) : Lang.T("tray.idle"));
            }
            if (text.Length > 63) text = text.Substring(0, 62) + "…";
            if (tray.Text != text) tray.Text = text;

            PerformancePreset mode = host.GameMode.ActivePreset;
            bool enabled = host.GameMode.Enabled;
            if (!force && mode == runtimeIconMode && enabled == runtimeIconEnabled) return;
            runtimeIconMode = mode;
            runtimeIconEnabled = enabled;
            using (System.Drawing.Icon next = IconArt.MakeMultiIcon(mode, enabled))
            {
                System.Drawing.Icon old = tray.Icon;
                tray.Icon = (System.Drawing.Icon)next.Clone();
                if (old != null) old.Dispose();
                // 任务栏/Alt-Tab 图标随 preset/开关同步（对齐 WinForms panel.SetRuntimeIcon）
                try
                {
                    if (window != null)
                    {
                        ImageSource src = WindowIcon.Create(mode, enabled);
                        if (src != null) window.Icon = src;
                    }
                }
                catch { }
            }
        }

        private PerformancePreset runtimeIconMode;
        private bool runtimeIconEnabled;

        private void SubscribeRuntimeEvents()
        {
            host.GameMode.SessionEnded += msg => Dispatcher.BeginInvoke(new Action(delegate
            {
                try { ShowBalloon(msg, 10000); } catch { }
            }));
            host.GameMode.GameAutoAdded += name => Dispatcher.BeginInvoke(new Action(delegate
            {
                try { ShowBalloon(Lang.F("bal.autoadd", name), 10000); } catch { }
            }));
            host.GameMode.LibraryChanged += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try { if (window != null) window.NotifyLibraryChanged(); } catch { }
                }));
            };
            host.DevFocus.SessionChanged += key => Dispatcher.BeginInvoke(new Action(delegate
            {
                try { ShowBalloon(Lang.T(key), 5000); } catch { }
            }));
            host.DailyCare.SessionChanged += key => Dispatcher.BeginInvoke(new Action(delegate
            {
                try { ShowBalloon(Lang.T(key), 5000); } catch { }
            }));
            host.DevServiceGuard.ServiceStopped += name => Dispatcher.BeginInvoke(new Action(delegate
            {
                // 服务停止属告警，与 WinForms 一致用警告图标
                try { ShowBalloon(Lang.F("bal.devsvc", name), 6000, System.Windows.Forms.ToolTipIcon.Warning); } catch { }
            }));
        }

        private void ShowBalloon(string text, int ms)
        {
            ShowBalloon(text, ms, System.Windows.Forms.ToolTipIcon.Info);
        }

        private void ShowBalloon(string text, int ms, System.Windows.Forms.ToolTipIcon icon)
        {
            if (tray != null)
                tray.ShowBalloonTip(ms, CaelusApp.App.DisplayName, text, icon);
        }

        private void ShowPanel()
        {
            if (window == null) { Settings.Save(PendingPanelKey, true); return; }
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        // 退出/还原链静默容错：单项失败记日志，不中断退出流程
        private static void Quiet(string what, Exception ex)
        {
            try { Logger.Log(what + " 失败：" + ex.GetType().Name + " - " + ex.Message); } catch { }
        }

        private void DoExit()
        {
            realExit = true;
            try { if (window != null) window.RealExit = true; } catch (Exception ex) { Quiet("退出：窗口标记", ex); }
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } } catch (Exception ex) { Quiet("退出：托盘释放", ex); }
            try { if (host != null) host.Shutdown(); } catch (Exception ex) { Quiet("退出：运行时停止", ex); }
            Shutdown();
        }

        private void OnSessionEnded(object sender, SessionEndedEventArgs e)
        {
            realExit = true;
            try { if (window != null) window.RealExit = true; } catch (Exception ex) { Quiet("会话结束：窗口标记", ex); }
            try { if (host != null) host.RestorePersistentChanges(); } catch (Exception ex) { Quiet("会话结束：持久改动还原", ex); }
            try { if (host != null) host.Shutdown(); } catch (Exception ex) { Quiet("会话结束：运行时停止", ex); }
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } } catch (Exception ex) { Quiet("会话结束：托盘释放", ex); }
            try { Shutdown(); } catch (Exception ex) { Quiet("会话结束：应用退出", ex); }
        }

        private void OnMotionPolicyChanged(object sender, EventArgs e)
        {
            ThemeManager.Apply(this, ThemeManager.CurrentTone, ThemeManager.CurrentMode);
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
                Views.OverviewView.InjectSampleData = true;
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
                    w.RealExit = true;
                    w.Close();
                }
                Views.OverviewView.InjectSampleData = false;
                // 全部工作区页面的深色常规模式截图。
                ThemeManager.Apply(this, UiTone.Dark, AppMode.Standard);
                string[] pages = new string[]
                {
                    "library", "policy", "graphics", "anticheat", "environment",
                    "whitelist", "audit", "log", "settings", "dev", "daily", "about"
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
                    "environment", "whitelist", "audit", "log", "settings", "dev", "daily", "about"
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

                string report = "NAVIGATION_SWITCHES_PER_ROUND=130" + Environment.NewLine
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
                window.RealExit = true;
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
            RunSingleShot(Path.Combine(dir, "wpf-" + page + "-dark-cruise.png"), page);
        }

        // --screenshot <png> <page>：离屏渲染单页存 PNG（对齐 WinForms 同名开发入口；WPF 页以名称指定）
        private int RunSingleShot(string pngPath, string page)
        {
            try
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Motion.Enabled = false;
                Paths.Init();
                Lang.Init();
                ThemeManager.Apply(this, UiTone.Dark, AppMode.Standard);
                // 游戏库/策略/体检在探针下无真实数据，注入样例以捕获实机图（仅此路径生效）；
                // 场景页面注入“游戏掌权 / 开发活跃待命”的三场景构图。
                Views.OverviewView.InjectSampleData = (page == "overview");
                Views.LibraryView.InjectSampleData = (page == "library");
                Views.PolicyView.InjectSampleData = (page == "policy");
                Views.AuditView.InjectSampleData = (page == "audit");
                Views.GraphicsView.InjectSampleData = (page == "graphics");
                Views.ScenarioDetailView.InjectSampleData = (page == "dev" || page == "daily");
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
                string dirName = Path.GetDirectoryName(Path.GetFullPath(pngPath));
                if (!string.IsNullOrEmpty(dirName)) Directory.CreateDirectory(dirName);
                using (FileStream stream = File.Create(pngPath)) encoder.Save(stream);
                window.RealExit = true;
                window.Close();
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(pngPath + ".error.txt", ex.ToString()); } catch { }
                return 1;
            }
        }

        // --ui-preview：轻量 UI 预览（对齐 WinForms 同名入口）：真实 GameMode/Core，
        // 无单实例锁/托盘/更新检查；关闭窗口即退出
        private void RunUiPreview()
        {
            try
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Paths.Init();
                Lang.Init();
                AppMode initial = ModeController.LoadPersisted();
                ThemeManager.Apply(this, ThemeManager.ResolveTone(), initial);
                var previewMode = new GameMode(Paths.Data, new SuppressionCore());
                previewMode.Enabled = Settings.Load("GameModeOn", true);
                var window = new MainWindow(previewMode);
                window.ApplyPersistedMode(initial);
                window.RealExit = true;
                window.Closed += delegate { Shutdown(0); };
                MainWindow = window;
                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Paths.Data ?? Path.GetTempPath(), "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [WPF ui-preview] " + ex + Environment.NewLine);
                }
                catch { }
                Shutdown(1);
            }
        }
    }
}
