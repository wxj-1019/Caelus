// @author zenjiro 18967498922@163.com
// 文件用途 启动程序并处理单实例 自愈和命令行入口

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class Program
    {
        private const string PendingPanelKey = "ShowPanelOnNextStart";

        [STAThread]
        private static void Main(string[] args)
        {

            if (LegacyFreezeRecovery.TryHandle(args)) return;
#if CAELUS_SELFTEST
            if (SelfTests.TryHandleRuntimeMode(args)) return;
#endif

            if (args.Length > 0 && args[0] == "--genicon")
            {
                try { IcoWriter.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Caelus.ico"), new[] { 16, 20, 24, 32, 48, 64, 128, 256 }); }
                catch { }
                return;
            }

            if (args.Length >= 2 && args[0] == "--geniconpng")
            {
                PerformancePreset mode = args.Length >= 3 && args[2] == "competitive" ? PerformancePreset.Competitive
                    : (args.Length >= 3 && args[2] == "custom" ? PerformancePreset.Custom : PerformancePreset.Standard);
                try { using (Bitmap bitmap = IconArt.Render(256, mode, true)) bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); }
                catch { Environment.ExitCode = 1; }
                return;
            }

            if (args.Length >= 2 && args[0] == "--screenshot")
            {
                Dpi.Init();
                Paths.Init();
                Lang.Init();
                if (args.Length >= 4) Lang.Cur = args[3] == "en" ? 1 : (args[3] == "ja" ? 2 : 0);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string sdir = Path.Combine(Path.GetTempPath(), "CaelusShot_" + Process.GetCurrentProcess().Id);
                Directory.CreateDirectory(sdir);
                Logger.LogPath = Path.Combine(sdir, "Caelus.log");
                int idx = args.Length >= 3 ? int.Parse(args[2]) : 0;
                var scCore = new SuppressionCore();
                var scTamer = new Tamer(scCore);
                try
                {
                    var scMode = new GameMode(sdir, scCore);
                    if (idx == (int)PageId.Library)
                    {
                        string demoDir = Path.Combine(sdir, "NebulaStrike", "Binaries", "Win64");
                        Directory.CreateDirectory(demoDir);
                        string demoExe = Path.Combine(demoDir, "NebulaStrike-Win64-Shipping.exe");
                        File.Copy(Application.ExecutablePath, demoExe, true);
                        scMode.AddGameExecutable("NEBULA STRIKE", demoExe);
                    }
                    var scArbiter = new ScenarioArbiter();
                    scArbiter.Register(new GameScenario());
                    var scDevFocus = new DevFocus(scArbiter, scCore, () => false, (a, b) => false, c => false);
                    using (var f = new PanelForm(scTamer, scMode, scDevFocus, IconArt.MakeIcon(Dpi.S(24)), true))
                    {
                        IntPtr hShot = f.Handle;
                        GC.KeepAlive(hShot);
                        f.RenderTo(args[1], idx,
                            args.Length >= 5 && args[4] == "anticheat",
                            args.Length >= 5 && args[4] == "mode",
                            args.Length >= 5 ? args[4] : null);
                    }
                }
                finally { try { Directory.Delete(sdir, true); } catch { } }
                return;
            }

            if (args.Length > 0 && args[0] == "--ui-preview")
            {
                Dpi.Init(); Paths.Init(); Lang.Init();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Logger.LogPath = Path.Combine(Paths.Data, "Caelus.preview.log");
                var previewCore = new SuppressionCore();
                var previewTamer = new Tamer(previewCore);
                var previewMode = new GameMode(Paths.Data, previewCore);
                previewMode.Enabled = Settings.Load("GameModeOn", true);
                var previewArbiter = new ScenarioArbiter();
                previewArbiter.Register(new GameScenario());
                var previewDevFocus = new DevFocus(previewArbiter, previewCore, () => false, (a, b) => false, c => false);
                using (Icon previewIcon = IconArt.MakeMultiIcon(previewMode.ActivePreset, previewMode.Enabled))
                using (var preview = new PanelForm(previewTamer, previewMode, previewDevFocus, previewIcon, true))
                {
                    preview.RealExit = true;
                    preview.ShowPanel();
                    Application.Run(preview);
                }
                return;
            }

            Dpi.Init();
            try { Native.SetPreferredAppMode(1); } catch { }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    string cdir = Paths.Data ?? Path.GetDirectoryName(Application.ExecutablePath);
                    File.AppendAllText(
                        Path.Combine(cdir, "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e.ExceptionObject + Environment.NewLine);
                }
                catch { }
            };

            bool created = false;
            Mutex mtx = null;
            try { mtx = new Mutex(true, "Global\\Caelus_SingleInstance", out created); }
            catch { created = false; }
            if (!created && TryReplaceOlderInstance())
                try { mtx = new Mutex(true, "Global\\Caelus_SingleInstance", out created); }
                catch { created = false; }
            if (!created)
            {
                try { EventWaitHandle.OpenExisting("Global\\Caelus_ShowPanel").Set(); } catch { }
                return;
            }

            bool autoStarted = false;
            if (args != null)
                foreach (string a in args)
                    if (string.Equals(a, TaskHelper.AutostartArgument, StringComparison.OrdinalIgnoreCase))
                        autoStarted = true;

            bool elevated = IsElevated();

            if (!elevated && TaskHelper.TaskExists())
            {
                mtx.ReleaseMutex();
                mtx.Close();
                Settings.Save(PendingPanelKey, true);
                if (TaskHelper.Run("/Run /TN " + TaskHelper.TaskName) == 0) return;
                Settings.Save(PendingPanelKey, false);
                try { mtx = new Mutex(true, "Global\\Caelus_SingleInstance", out created); }
                catch { created = false; }
                if (!created)
                {
                    try { EventWaitHandle.OpenExisting("Global\\Caelus_ShowPanel").Set(); } catch { }
                    return;
                }
            }

            // 与 exitEvt 相同的每用户 ACL（安全审查 S1）：默认 ACL 允许任意本地用户 Set
            // 本事件，在管理员桌面上强制弹出面板（干扰/诱导）。同名对象已存在时安全描述符
            // 被忽略，同用户重复启动（OpenExisting 路径）不受影响。
            bool showCreated;
            EventWaitHandle showEvt;
            try
            {
                var showSec = new EventWaitHandleSecurity();
                showSec.AddAccessRule(new EventWaitHandleAccessRule(
                    WindowsIdentity.GetCurrent().User,
                    EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                    AccessControlType.Allow));
                showEvt = new EventWaitHandle(false, EventResetMode.AutoReset,
                    "Global\\Caelus_ShowPanel", out showCreated, showSec);
            }
            catch
            {
                showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\Caelus_ShowPanel");
            }
            EventWaitHandle exitEvt;
            try
            {
                var exitSec = new EventWaitHandleSecurity();
                exitSec.AddAccessRule(new EventWaitHandleAccessRule(
                    WindowsIdentity.GetCurrent().User,
                    EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                    AccessControlType.Allow));
                bool exitCreated;
                exitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\Caelus_Exit",
                    out exitCreated, exitSec);
            }
            catch
            {
                exitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\Caelus_Exit");
            }

            Paths.Init();
            Lang.Init();
            try { Theme.SetLight(Settings.Load("UiLight", false)); } catch { }
            string dir = Paths.Data;
            Logger.LogPath = Path.Combine(dir, "Caelus.log");
            Settings.Remove("EvidenceMode");
            LegacyFreezeRecovery.BeginHeal(Path.Combine(dir, LegacyFreezeRecovery.StateFileName));
            int healedSuppression = SuppressionCore.HealFromCrash(Path.Combine(dir, SuppressionCore.StateFileName));
            if (healedSuppression > 0) Logger.Log("检测到上次未还原的分级后台控制，已恢复 " + healedSuppression + " 个进程");
            PowerPlan.HealFromCrash();
            try { UpdatePause.HealFromCrash(); } catch { }
            try { FgBoost.PurgeLegacy(); } catch { }
            GameDvr.HealFromCrash();
            try { Mmcss.PurgeLegacy(); } catch { }
            Notif.HealFromCrash();
            VisualFx.HealFromCrash();
            DisplayGuard.HealFromCrash();
            try { NvGlobalTweaks.HealFromCrash(); } catch { }
            try { AdlxTweaks.HealFromCrash(); } catch { }
            try { PresenceQos.HealFromCrash(); } catch { }
            try { PowerOverlay.HealFromCrash(); } catch { }
            RenderLane.HealFromCrash();
            CrashGuard.HealFromCrash();

            try { LegacyPurge.RunOnce(dir); } catch { }

            if (Settings.Load("GmIfeoBoost", false))
                try
                {
                    int preArmed = IfeoBoost.PreArmAll();
                    if (preArmed > 0)
                        Logger.Log("后备提优：已预置 " + preArmed + " 个游戏");
                }
                catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    File.AppendAllText(Path.Combine(dir, "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [UI] " + e.Exception + Environment.NewLine);
                }
                catch { }
            };

            var core = new SuppressionCore(Path.Combine(dir, SuppressionCore.StateFileName));
            var tamer = new Tamer(core);
            tamer.Paused = !Settings.Load("TameOn", true);

            var gameMode = new GameMode(dir, core);
            gameMode.Enabled = Settings.Load("GameModeOn", true);

            var arbiter = new ScenarioArbiter();
            // 游戏占仲裁器最高优先级席位：GameMode 自己管理游戏副作用，
            // 这里保证游戏活跃时开发/日常场景被还原式挂起。
            arbiter.Register(new GameScenario());
            // 开发服务在 DevFocus/DailyCare 的压制扫描中被豁免（白名单 OR 已注册开发服务）
            Func<string, string, bool> devWhitelist = (name, path) =>
                gameMode.IsProcessWhitelisted(name, path) || DevServiceCatalog.IsMatch(name);
            var devFocus = new DevFocus(arbiter, core,
                () => Settings.Load("DevModeOn", true),
                devWhitelist,
                DistractCatalog.IsMatch,
                // 与 WPF 宿主一致的共享效果交接直通：游戏活跃且同样要该效果时，
                // 开发场景挂起只移交占用权，避免服务停启/通知开关的还原再重做
                () => gameMode.ActiveGame != null && gameMode.ServicePauseWantedNow,
                () => gameMode.ActiveGame != null && gameMode.NotifQuiet);
            var dailyCare = new DailyCare(arbiter, core,
                () => Settings.Load("DailyCareOn", true),
                devWhitelist);
            var devServiceGuard = new DevServiceGuard();
            // 线程池定时器而非 UI 定时器：电源翻转触发的活性重算会在同线程同步跑
            // 场景 Grant/Suspend（含全量扫描），不能压上 UI 线程（与 WPF 宿主一致）
            var powerPollTimer = new System.Threading.Timer(delegate
            {
                try { dailyCare.RefreshPowerState(); } catch { }
            }, null, 5000, 5000);
            gameMode.ActiveChanged += on => arbiter.ReportActivity(ScenarioKind.Game, on);

            var startGate = new object();
            bool exiting = false;
            var bootThread = new Thread(() =>
            {
                try { SvcPause.HealFromCrash(); } catch { }
                try { DoTweak.HealFromCrash(); } catch { }
                lock (startGate)
                {
                    if (exiting) return;
                    tamer.Start();
                    gameMode.Start();
                }
            });
            bootThread.IsBackground = true;
            bootThread.Start();

            var procNotify = new ProcNotify();
            // 场景活性评估（DevFocus/DailyCare）走专职泵线程：掌权交接内含 SCM 停服务、
            // 全量进程扫描、逐进程提优等秒级操作，在进程事件线程同步执行会拖延随后的
            // 游戏启动/退出检测；游戏模式/守护类轻量记账仍留在事件线程。
            var scenarioPump = new ScenarioEventPump();
            scenarioPump.Batch += delegate(ProcessChangeBatch b)
            {
                // 逐场景隔离：单场景异常不得连带丢另一场景的同一批次
                try { devFocus.NotifyProcessChanges(b); }
                catch (Exception ex) { Logger.LogFailure("开发专注批次处理失败", ex); }
                try { dailyCare.NotifyProcessChanges(b); }
                catch (Exception ex) { Logger.LogFailure("日常优化批次处理失败", ex); }
            };
            procNotify.CaptureStartIdentity = delegate(string name, int session)
            {
                return gameMode.NeedsWhitelistParentIdentity(session)
                    || gameMode.NeedsGameProcessIdentity(name, session)
                    || BuildCatalog.IsMatch(name)
                    || DevServiceCatalog.IsMatch(name)
                    ;
            };
            procNotify.CaptureParentIdentity =
                delegate(int parentPid, string name, int session)
                {
                    return gameMode.NeedsWhitelistParentIdentity(session)
                        || gameMode.NeedsLauncherChildParentIdentity(
                            parentPid, name, session);
                };
            procNotify.BatchChanged += batch =>
            {
                gameMode.NotifyProcessChanges(batch);
                tamer.NotifyProcessChanges(batch);
                scenarioPump.Post(batch);
                devServiceGuard.NotifyProcessChanges(batch);
            };
            procNotify.Start();
            gameMode.ProcessEventsAvailable = procNotify.IsActive;
            tamer.ProcessEventsAvailable = procNotify.IsActive;

            // 初始全量扫描：检测启动前已运行的场景进程（如已开的 VS Code、浏览器）
            try { devFocus.InitialScan(); } catch { }
            try { dailyCare.InitialScan(); } catch { }

            // 健康维护独立调度：与 DailyCare 掌权解耦，任何使用形态下到点即执行；
            // 游戏进行中让路（着色器缓存是游戏热用文件，对局中不清理）
            HealthCare.ShouldDefer = () => gameMode.IsActive;
            try { HealthCare.StartAuto(); } catch { }

            if (elevated)
                ThreadPool.QueueUserWorkItem(_ => TaskHelper.RefreshStartupTask());

            PerformancePreset runtimeIconMode = gameMode.ActivePreset;
            bool runtimeIconEnabled = gameMode.Enabled;
            Icon appIcon = IconArt.MakeMultiIcon(runtimeIconMode, runtimeIconEnabled);
            var panel = new PanelForm(tamer, gameMode, devFocus, appIcon, elevated, dailyCare);
            GC.KeepAlive(panel.Handle);

            bool pendingPanel = Settings.Load(PendingPanelKey, false);
            if (pendingPanel) Settings.Save(PendingPanelKey, false);
            bool showingPanel = !autoStarted || pendingPanel;
            if (showingPanel && ReleaseNotes.HasUnseen)
                try { using (var dlg = new ReleaseNotesDialog()) dlg.ShowDialog(); }
                catch { }
            if (showingPanel) panel.ShowPanel();

            var evtThread = new Thread(() =>
            {
                while (true)
                {
                    showEvt.WaitOne();
                    try { panel.ShowPanel(); } catch { }
                }
            });
            evtThread.IsBackground = true;
            evtThread.Start();

            var icon = new NotifyIcon();
            icon.Icon = (Icon)appIcon.Clone();
            appIcon.Dispose();
            icon.Text = elevated ? Lang.T("tray.idle") : Lang.T("tray.noelev");

            System.Windows.Forms.Timer trayTip = null;
            Action doExit = () =>
            {

                try { trayTip.Stop(); trayTip.Dispose(); } catch { }
                try { powerPollTimer.Dispose(); } catch { }
                try { HealthCare.StopAuto(); } catch { }
                icon.Visible = false;
                icon.Dispose();
                lock (startGate) exiting = true;
                try { procNotify.Stop(); } catch { }
                // 泵先停收：积压批次若在场景 Stop 之后到达会把场景重新激活
                try { scenarioPump.Stop(); } catch { }
                // 场景先于 GameMode 停止：避免游戏退出事件在退出途中重新授权场景又被拆掉
                devFocus.Stop();
                dailyCare.Stop();
                devServiceGuard.Stop();
                tamer.Stop();
                gameMode.Stop();
                panel.RealExit = true;
                Application.Exit();
            };

            var trayMenu = new TrayMenu(tamer, gameMode, devFocus,
                () => panel.ShowPanel(),
                doExit,
                () => panel.SyncAllToggles());

            var exitThread = new Thread(() =>
            {
                exitEvt.WaitOne();
                try { panel.BeginInvoke(doExit); } catch { }
            });
            exitThread.IsBackground = true;
            exitThread.Start();
            icon.ContextMenuStrip = trayMenu.Strip;
            icon.Visible = true;
            SystemEvents.SessionEnded += (s, e) =>
            {
                // 关机预算有限：持久还原（重启后仍生效的项）必须最先做——泵停止的
                // Join 排前面会在途场景交接（SCM 停服务秒级）把预算吃光，电源计划
                // 还原可能被系统强杀跳过。场景/游戏侧只影响易失状态，残留由下次
                // 启动自愈兜底；场景仍先于 GameMode 关闭退出仲裁器。
                try { HealthCare.StopAuto(); } catch { }
                try { PowerPlan.Restore(); } catch { }
                try { GameDvr.Restore(); } catch { }
                try { Notif.Restore(); } catch { }
                try { Mmcss.Restore(); } catch { }
                try { FgBoost.Restore(); } catch { }
                try { VisualFx.Restore(); } catch { }
                try { NvGlobalTweaks.Restore(); } catch { }
                try { AdlxTweaks.RestoreAntiLag(); } catch { }
                try { AdlxTweaks.RestoreChill(); } catch { }
                try { AdlxTweaks.RestoreEnhancedSync(); } catch { }
                try { AdlxTweaks.RestoreRis(); } catch { }
                try { PresenceQos.Restore(); } catch { }
                try { PowerOverlay.Restore(); } catch { }
                // 游戏侧服务暂停的还原在异步 Deactivate 里，这里按标志同步兜底（幂等）
                try { SvcPause.Restore(); } catch { }
                try { scenarioPump.Stop(1500); } catch { }
                try { devFocus.Stop(); } catch { }
                try { dailyCare.Stop(); } catch { }
                try { devServiceGuard.Stop(); } catch { }
                try { gameMode.Enabled = false; } catch { }
            };
            gameMode.SessionEnded += msg =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { icon.ShowBalloonTip(10000, App.DisplayName, msg, ToolTipIcon.Info); } catch { }
                    }));
                }
                catch { }
            };
            gameMode.GameAutoAdded += name =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { icon.ShowBalloonTip(10000, App.DisplayName, Lang.F("bal.autoadd", name), ToolTipIcon.Info); } catch { }
                    }));
                }
                catch { }
            };
            gameMode.NvSwitchAutoDisabled += label =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { icon.ShowBalloonTip(8000, App.DisplayName, "「" + label + "」连续写入失败，已自动关闭该开关；重新打开即恢复尝试", ToolTipIcon.Warning); } catch { }
                    }));
                }
                catch { }
            };
            gameMode.LibraryChanged += () =>
            {
                try { panel.NotifyLibraryChanged(); } catch { }
            };
            devFocus.SessionChanged += key =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { icon.ShowBalloonTip(5000, App.DisplayName, Lang.T(key), ToolTipIcon.Info); } catch { }
                    }));
                }
                catch { }
            };
            dailyCare.SessionChanged += key =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { icon.ShowBalloonTip(5000, App.DisplayName, Lang.T(key), ToolTipIcon.Info); } catch { }
                    }));
                }
                catch { }
            };
            devServiceGuard.ServiceStopped += name =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { icon.ShowBalloonTip(6000, App.DisplayName, Lang.F("bal.devsvc", name), ToolTipIcon.Warning); } catch { }
                    }));
                }
                catch { }
            };
            arbiter.GrantedChanged += kind =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { panel.SetGrantedScenario(kind); } catch { }
                    }));
                }
                catch { }
            };

            trayTip = new System.Windows.Forms.Timer();
            trayTip.Interval = 1500;
            trayTip.Tick += (s, e) =>
            {
                PerformancePreset nextIconMode = gameMode.ActivePreset;
                bool nextIconEnabled = gameMode.Enabled;
                if (nextIconMode != runtimeIconMode || nextIconEnabled != runtimeIconEnabled)
                {
                    runtimeIconMode = nextIconMode; runtimeIconEnabled = nextIconEnabled;
                    using (Icon next = IconArt.MakeMultiIcon(nextIconMode, nextIconEnabled))
                    {
                        Icon old = icon.Icon;
                        icon.Icon = (Icon)next.Clone();
                        panel.SetRuntimeIcon(next);
                        if (old != null) old.Dispose();
                    }
                }
                string txt;
                if (!elevated) txt = Lang.T("tray.noelev");
                else
                {
                    string g = gameMode.ActiveGame;
                    string a = g == null ? gameMode.ArmedGame : null;
                    txt = g != null ? Lang.F("tray.active", g)
                        : (a != null ? Lang.F("tray.armed", a) : Lang.T("tray.idle"));
                }
                if (txt.Length > 63) txt = txt.Substring(0, 62) + "…";
                if (icon.Text != txt) icon.Text = txt;
            };
            trayTip.Start();

            var updTimer = new System.Windows.Forms.Timer();
            updTimer.Interval = 6000;
            updTimer.Tick += (s, e) =>
            {
                updTimer.Stop();
                updTimer.Dispose();
                UpdateChecker.CheckAsync(r =>
                {
                    if (r.Ok && r.Newer)
                    {
                        Logger.Log("启动检查更新：发现新版本 " + r.Latest + "（当前 " + App.VersionTag + "）");
                        try
                        {
                            panel.BeginInvoke((MethodInvoker)(() =>
                            {
                                try { icon.ShowBalloonTip(8000, App.DisplayName, Lang.F("bal.update", r.Latest), ToolTipIcon.Info); } catch { }
                            }));
                        }
                        catch { }
                    }
                    else if (r.Ok) Logger.Log("启动检查更新：已是最新版本（" + App.VersionTag + "）");
                    else Logger.Log("启动检查更新失败：" + r.Error);
                });
            };
            updTimer.Start();

            if (!elevated)
                icon.ShowBalloonTip(8000, App.DisplayName, Lang.T("bal.noelev"), ToolTipIcon.Warning);

            icon.DoubleClick += (s, e) => panel.ShowPanel();

            Application.Run();
            GC.KeepAlive(mtx);
        }

        private static bool TryReplaceOlderInstance()
        {
            Process older = null;
            try
            {
                int self = Process.GetCurrentProcess().Id;
                foreach (Process p in Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(Application.ExecutablePath)))
                {
                    if (p.Id == self) { p.Dispose(); continue; }
                    string version = null;
                    try { version = p.MainModule.FileVersionInfo.FileVersion; }
                    catch { }
                    if (version != null && App.CompareVersions(App.Version, version) > 0 && older == null) older = p;
                    else p.Dispose();
                }
                if (older == null) return false;

                try { EventWaitHandle.OpenExisting("Global\\Caelus_Exit").Set(); }
                catch { return false; }
                if (!older.WaitForExit(20000)) return false;
                return true;
            }
            catch { return false; }
            finally { if (older != null) older.Dispose(); }
        }

        private static bool IsElevated()
        {
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

    }

}
