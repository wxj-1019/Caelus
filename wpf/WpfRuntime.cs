// @author zenjiro 18967498922@163.com
// 文件用途 WPF 正式运行时主机：把主程序（原 src/Program.cs）的完整运行时接线搬到 WPF 宿主，
//           包括单实例/提权/崩溃自愈链/压制与场景运行时/托盘菜单/真实概览数据源。
//           托盘部分用 WinForms 的 NotifyIcon + ContextMenuStrip（WPF 没有等价的托盘菜单），
//           颜色取自 wpf/Themes 的 XAML 色板（与主界面同一事实源）。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using CaelusApp.WpfHost.Dialogs;
using Microsoft.Win32;

namespace CaelusApp.WpfHost
{
    // ---- WPF 版托盘菜单：结构与 WinForms TrayMenu 一致，渲染色来自 WPF 主题 XAML ----
    internal sealed class WpfTrayMenu
    {
        private readonly Tamer tamer;
        private readonly GameMode gameMode;
        private readonly DevFocus devFocus;
        private readonly Action openPanel;
        private readonly Action exitApp;
        private readonly Action afterChange;
        private readonly ContextMenuStrip strip;

        public ContextMenuStrip Strip { get { return strip; } }

        public WpfTrayMenu(Tamer tamer, GameMode gameMode, DevFocus devFocus,
            Action openPanel, Action exitApp, Action afterChange)
        {
            this.tamer = tamer;
            this.gameMode = gameMode;
            this.devFocus = devFocus;
            this.openPanel = openPanel;
            this.exitApp = exitApp;
            this.afterChange = afterChange;

            ToolStripManager.Renderer = new WpfMenuRenderer();
            strip = new ContextMenuStrip();
            StyleDropDown(strip);
            strip.Opening += (s, e) => Rebuild();
            Rebuild();
        }

        private static ThemeColors cachedColors;
        private static ThemeColors Colors()
        {
            // 托盘菜单固定深色渲染，与品牌暗色一致；色板按需读取一次
            if (cachedColors == null) cachedColors = Palette.For(UiTone.Dark);
            return cachedColors;
        }

        private static Color C(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.Gray;
            try
            {
                string text = hex.Trim().TrimStart('#');
                if (text.Length == 6)
                    return Color.FromArgb(255,
                        Convert.ToInt32(text.Substring(0, 2), 16),
                        Convert.ToInt32(text.Substring(2, 2), 16),
                        Convert.ToInt32(text.Substring(4, 2), 16));
                if (text.Length == 8)
                    return Color.FromArgb(
                        Convert.ToInt32(text.Substring(0, 2), 16),
                        Convert.ToInt32(text.Substring(2, 2), 16),
                        Convert.ToInt32(text.Substring(4, 2), 16),
                        Convert.ToInt32(text.Substring(6, 2), 16));
            }
            catch { }
            return Color.Gray;
        }

        private sealed class WpfMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return C(Colors().Surface); } }
            public override Color ImageMarginGradientBegin { get { return C(Colors().Surface); } }
            public override Color ImageMarginGradientMiddle { get { return C(Colors().Surface); } }
            public override Color ImageMarginGradientEnd { get { return C(Colors().Surface); } }
            public override Color MenuBorder { get { return C(Colors().Border); } }
            public override Color MenuItemBorder { get { return C(Colors().SurfaceRaised); } }
            public override Color MenuItemSelected { get { return C(Colors().SurfaceRaised); } }
            public override Color MenuItemSelectedGradientBegin { get { return C(Colors().SurfaceRaised); } }
            public override Color MenuItemSelectedGradientEnd { get { return C(Colors().SurfaceRaised); } }
            public override Color MenuItemPressedGradientBegin { get { return C(Colors().SurfaceRaised); } }
            public override Color MenuItemPressedGradientEnd { get { return C(Colors().SurfaceRaised); } }
            public override Color SeparatorDark { get { return C(Colors().Border); } }
            public override Color SeparatorLight { get { return C(Colors().Border); } }
            public override Color CheckBackground { get { return C(Colors().Brand); } }
            public override Color CheckSelectedBackground { get { return C(Colors().Brand); } }
            public override Color CheckPressedBackground { get { return C(Colors().Brand); } }
        }

        private sealed class WpfMenuRenderer : ToolStripProfessionalRenderer
        {
            public WpfMenuRenderer() : base(new WpfMenuColors()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? C(Colors().TextPrimary) : C(Colors().TextTertiary);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (Pen pen = new Pen(C(Colors().Border))) 
                    e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
            }
        }

        private static void StyleDropDown(ToolStripDropDown dd)
        {
            dd.Font = SystemFonts.MessageBoxFont;
            ToolStripDropDownMenu menu = dd as ToolStripDropDownMenu;
            if (menu != null)
            {
                menu.ShowImageMargin = true;
                menu.ShowCheckMargin = false;
            }
            dd.Opened += (s, e) => { try { Native.RoundCorners(dd.Handle); } catch { } };
        }

        private ToolStripMenuItem Item(string text, EventHandler onClick)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.ForeColor = C(Colors().TextPrimary);
            if (onClick != null) mi.Click += onClick;
            return mi;
        }

        private ToolStripMenuItem SubMenu(string text)
        {
            ToolStripMenuItem mi = Item(text, null);
            StyleDropDown(mi.DropDown);
            return mi;
        }

        private ToolStripMenuItem Check(string text, bool on, EventHandler onClick)
        {
            ToolStripMenuItem mi = Item(text + Reserve(), onClick);
            mi.Checked = on;
            return mi;
        }

        private static string reserve;
        private static string Reserve()
        {
            if (reserve == null)
            {
                Font f = SystemFonts.MessageBoxFont;
                int w = TextRenderer.MeasureText(" ", f, Size.Empty, TextFormatFlags.NoPadding).Width;
                int n = w > 0 ? (int)Math.Ceiling(30.0 / w) : 6;
                reserve = new string(' ', Math.Max(7, n));
            }
            return reserve;
        }

        private void Rebuild()
        {
            while (strip.Items.Count > 0)
            {
                ToolStripItem it = strip.Items[0];
                strip.Items.RemoveAt(0);
                it.Dispose();
            }

            strip.Items.Add(Item(Lang.T("tray.open"), (s, e) => openPanel()));
            strip.Items.Add(new ToolStripSeparator());

            strip.Items.Add(Check(Lang.T("v14.master"), gameMode.Enabled, (s, e) =>
            {
                gameMode.Enabled = !gameMode.Enabled;
                Settings.Save("GameModeOn", gameMode.Enabled);
                Changed();
            }));
            ToolStripMenuItem currentMode = Item(
                Lang.F("mode.tray.current", ModeDisplayName(gameMode.ActivePreset)),
                (s, e) => openPanel());
            currentMode.ForeColor = ModeAccent(gameMode.ActivePreset);
            strip.Items.Add(currentMode);
            strip.Items.Add(Check(Lang.T("nav.tame"), !tamer.Paused, (s, e) =>
            {
                tamer.Paused = !tamer.Paused;
                Settings.Save("TameOn", !tamer.Paused);
                Changed();
            }));
            strip.Items.Add(Check(Lang.T("tray.focus"), devFocus.FocusModeOn, (s, e) =>
            {
                devFocus.SetFocusMode(!devFocus.FocusModeOn);
                Changed();
            }));

            ToolStripMenuItem ac = SubMenu(Lang.T("tray.aclist"));
            foreach (AcGroup g in AntiCheatCatalog.Groups)
            {
                string key = g.Key;
                ac.DropDownItems.Add(Check(Lang.T("ac." + key + ".n"), tamer.IsGroupEnabled(key), (s, e) =>
                {
                    tamer.SetGroupEnabled(key, !tamer.IsGroupEnabled(key));
                    Changed();
                }));
            }
            strip.Items.Add(ac);

            ToolStripMenuItem set = SubMenu(Lang.T("nav.set"));
            set.DropDownItems.Add(Check(Lang.T("tm.gpu"), gameMode.GpuHighPerf, (s, e) => { gameMode.GpuHighPerf = !gameMode.GpuHighPerf; Changed(); }));

            bool dvrForced = gameMode.ActivePreset != PerformancePreset.Custom;
            ToolStripMenuItem dvr = Check(Lang.T("tm.dvr") + (dvrForced ? " · " + Lang.T("v14.preset.forced") : ""), EffectiveDvr(),
                (s, e) => { gameMode.KillGameDvr = !gameMode.KillGameDvr; Changed(); });
            dvr.Enabled = !dvrForced;
            set.DropDownItems.Add(dvr);
            set.DropDownItems.Add(Check(Lang.T("tm.fso"), gameMode.DisableFso, (s, e) => { gameMode.DisableFso = !gameMode.DisableFso; Changed(); }));
            set.DropDownItems.Add(new ToolStripSeparator());
            set.DropDownItems.Add(Check(Lang.T("tm.notif"), gameMode.NotifQuiet, (s, e) => { gameMode.NotifQuiet = !gameMode.NotifQuiet; Changed(); }));
            set.DropDownItems.Add(Check(Lang.T("tm.trim"), gameMode.TrimWorkingSet, (s, e) => { gameMode.TrimWorkingSet = !gameMode.TrimWorkingSet; Changed(); }));
            set.DropDownItems.Add(Check(Lang.T("tm.plan"), gameMode.PowerPlanSwitch, (s, e) => { gameMode.PowerPlanSwitch = !gameMode.PowerPlanSwitch; Changed(); }));
            set.DropDownItems.Add(new ToolStripSeparator());
            set.DropDownItems.Add(Check(Lang.T("tm.autostart"), TaskHelper.TaskExistsCached(), (s, e) => { ToggleAutostart(); Changed(); }));

            strip.Items.Add(set);

            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(Item(Lang.T("tray.reset"), (s, e) => ResetDefaults()));
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(Item(Lang.T("tray.exit"), (s, e) => exitApp()));
        }

        private void Changed()
        {
            Action a = afterChange;
            if (a != null) { try { a(); } catch { } }
        }

        private static string ModeDisplayName(PerformancePreset preset)
        {
            return preset == PerformancePreset.Competitive ? Lang.T("preset.competitive")
                : preset == PerformancePreset.Custom ? Lang.T("preset.custom")
                : Lang.T("preset.standard");
        }

        private static Color ModeAccent(PerformancePreset preset)
        {
            ModeColors colors = ModePalette.For(ModePalette.FromPreset(preset));
            return C(colors.ModeAccentOnDark);
        }

        private bool EffectiveDvr()
        {
            PerformancePreset mode = gameMode.ActivePreset;
            return mode == PerformancePreset.Custom
                ? gameMode.KillGameDvr
                : mode == PerformancePreset.Competitive;
        }

        private void ToggleAutostart()
        {
            if (!TaskHelper.TaskExists())
                TaskHelper.CreateStartupTask();
            else
                TaskHelper.DeleteStartupTask();
        }

        private void ResetDefaults()
        {
            System.Windows.Window owner = System.Windows.Application.Current == null
                ? null : System.Windows.Application.Current.MainWindow;
            if (MessageDialogWpf.Show(owner, "恢复默认配置？",
                    "所有开关、反作弊选择和白名单恢复为默认，游戏列表保留。",
                    MsgSeverity.Warning, MsgButtons.OkCancel, null, "恢复默认", System.Windows.MessageBoxResult.Cancel)
                != System.Windows.MessageBoxResult.OK) return;

            gameMode.SuppressBackground = true;
            gameMode.BoostGame = true;
            gameMode.PowerPlanSwitch = true;
            gameMode.PauseDownloads = true;
            gameMode.PauseSvcIndex = false;
            gameMode.GpuHighPerf = true;
            gameMode.KillGameDvr = true;
            gameMode.DisableFso = false;
            gameMode.NotifQuiet = false;
            gameMode.TrimWorkingSet = false;
            gameMode.HzGuard = false;
            gameMode.StrictCoreIsolation = false;
            gameMode.AggressiveSuppression = false;
            gameMode.IdleStateDisable = true;
            gameMode.VisualFxDowngrade = false;
            gameMode.Enabled = true; Settings.Save("GameModeOn", true);
            gameMode.Preset = PerformancePreset.Standard;
            bool whitelistReset = gameMode.ResetWhitelist();

            foreach (AcGroup g in AntiCheatCatalog.Groups) tamer.SetGroupEnabled(g.Key, g.Default);
            tamer.Paused = false; Settings.Save("TameOn", true);

            Changed();
            if (!whitelistReset)
            {
                MessageDialogWpf.Show(owner, "白名单写入失败", "默认配置已部分恢复。",
                    MsgSeverity.Danger, MsgButtons.Ok, gameMode.WhitelistLastError,
                    null, System.Windows.MessageBoxResult.OK);
                Logger.Log("默认配置已部分恢复，但白名单写入失败");
            }
            else Logger.Log("已恢复默认配置");
        }
    }

    // ---- WPF 运行时主机：进程级接线（单实例/提权/自愈/运行时启停） ----
    internal sealed class WpfRuntimeHost
    {
        private const string PendingPanelKey = "ShowPanelOnNextStart";

        private readonly string dataDir;
        private readonly SuppressionCore core;
        private readonly Tamer tamer;
        private readonly GameMode gameMode;
        private readonly ScenarioArbiter arbiter;
        private readonly DevFocus devFocus;
        private readonly DailyCare dailyCare;
        private readonly DevServiceGuard devServiceGuard;
        private readonly ProcNotify procNotify;
        private readonly ScenarioEventPump scenarioPump;
        private readonly System.Threading.Timer powerPollTimer;
        private Thread bootThread;
        private readonly object startGate = new object();
        private volatile bool exiting;
        private bool booted;

        public GameMode GameMode { get { return gameMode; } }
        public Tamer Tamer { get { return tamer; } }
        public DevFocus DevFocus { get { return devFocus; } }
        public DailyCare DailyCare { get { return dailyCare; } }
        public DevServiceGuard DevServiceGuard { get { return devServiceGuard; } }
        public ScenarioArbiter Arbiter { get { return arbiter; } }
        public SuppressionCore Core { get { return core; } }

        // ---- 进程早期处理：返回 true 表示进程应退出（工具参数/自测/已有实例） ----
        public static bool HandleEarlyExit(string[] args)
        {
            if (LegacyFreezeRecovery.TryHandle(args)) return true;
#if CAELUS_SELFTEST
            if (SelfTests.TryHandleRuntimeMode(args)) return true;
#endif
            if (args == null || args.Length == 0) return false;

            if (args[0] == "--genicon")
            {
                try
                {
                    IcoWriter.Save(Path.Combine(
                        Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath),
                        "Caelus.ico"), new[] { 16, 20, 24, 32, 48, 64, 128, 256 });
                }
                catch { }
                return true;
            }
            if (args.Length >= 2 && args[0] == "--geniconpng")
            {
                PerformancePreset mode = args.Length >= 3 && args[2] == "competitive" ? PerformancePreset.Competitive
                    : (args.Length >= 3 && args[2] == "custom" ? PerformancePreset.Custom : PerformancePreset.Standard);
                try
                {
                    using (Bitmap bitmap = IconArt.Render(256, mode, true))
                        bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                }
                catch { Environment.ExitCode = 1; }
                return true;
            }
            return false;
        }

        // ---- 单实例：成功返回持有 Mutex（调用方保活），已有实例返回 null ----
        public static Mutex AcquireSingleInstance()
        {
            bool created = false;
            Mutex mtx = null;
            try { mtx = new Mutex(true, "Global\\Caelus_SingleInstance", out created); }
            catch { created = false; }
            if (!created && TryReplaceOlderInstance())
                try { mtx = new Mutex(true, "Global\\Caelus_SingleInstance", out created); }
                catch { created = false; }
            if (!created)
            {
                if (mtx != null) { try { mtx.Close(); } catch { } mtx = null; }
                try { EventWaitHandle.OpenExisting("Global\\Caelus_ShowPanel").Set(); } catch { }
                return null;
            }
            return mtx;
        }

        private static bool TryReplaceOlderInstance()
        {
            Process older = null;
            try
            {
                int self = Process.GetCurrentProcess().Id;
                foreach (Process p in Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(System.Windows.Forms.Application.ExecutablePath)))
                {
                    if (p.Id == self) { p.Dispose(); continue; }
                    string version = null;
                    try { version = p.MainModule.FileVersionInfo.FileVersion; }
                    catch { }
                    if (version != null && CaelusApp.App.CompareVersions(CaelusApp.App.Version, version) > 0 && older == null) older = p;
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

        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ---- 创建每用户 ACL 的全局事件（防其它本地用户诱导弹面板/退出） ----
        public static EventWaitHandle CreateGuardedEvent(string name)
        {
            try
            {
                EventWaitHandleSecurity sec = new EventWaitHandleSecurity();
                sec.AddAccessRule(new EventWaitHandleAccessRule(
                    WindowsIdentity.GetCurrent().User,
                    EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                    AccessControlType.Allow));
                bool created;
                return new EventWaitHandle(false, EventResetMode.AutoReset,
                    name, out created, sec);
            }
            catch
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, name);
            }
        }

        public WpfRuntimeHost(string dataDir)
        {
            this.dataDir = dataDir;
            core = new SuppressionCore(Path.Combine(dataDir, SuppressionCore.StateFileName));
            tamer = new Tamer(core);
            tamer.Paused = !Settings.Load("TameOn", true);

            gameMode = new GameMode(dataDir, core);
            gameMode.Enabled = Settings.Load("GameModeOn", true);

            arbiter = new ScenarioArbiter();
            // 游戏占仲裁器最高优先级席位：GameMode 自己管理游戏副作用，
            // 这里保证游戏活跃时开发/日常场景被还原式挂起。
            arbiter.Register(new GameScenario());
            // 开发服务在 DevFocus/DailyCare 的压制扫描中被豁免（白名单 OR 已注册开发服务）
            Func<string, string, bool> devWhitelist = (name, path) =>
                gameMode.IsProcessWhitelisted(name, path) || DevServiceCatalog.IsMatch(name);
            devFocus = new DevFocus(arbiter, core,
                () => Settings.Load("DevModeOn", true),
                devWhitelist,
                DistractCatalog.IsMatch,
                // 共享效果交接直通：游戏活跃且同样要该效果时，开发场景挂起只移交占用权
                delegate { return gameMode.ActiveGame != null && gameMode.ServicePauseWantedNow; },
                delegate { return gameMode.ActiveGame != null && gameMode.NotifQuiet; });
            dailyCare = new DailyCare(arbiter, core,
                () => Settings.Load("DailyCareOn", true),
                devWhitelist);
            devServiceGuard = new DevServiceGuard();
            procNotify = new ProcNotify();
            // 场景活性评估走专职泵线程：掌权交接内含 SCM 停服务/全量扫描等秒级操作，
            // 不能阻塞进程事件线程上的游戏检测（与 WinForms 宿主同一接线模式）
            scenarioPump = new ScenarioEventPump();
            scenarioPump.Batch += delegate(ProcessChangeBatch b)
            {
                devFocus.NotifyProcessChanges(b);
                dailyCare.NotifyProcessChanges(b);
            };

            gameMode.ActiveChanged += on => arbiter.ReportActivity(ScenarioKind.Game, on);

            powerPollTimer = new System.Threading.Timer(delegate
            {
                try { dailyCare.RefreshPowerState(); } catch { }
            }, null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool IsBooted { get { return booted; } }

        // ---- 启动运行时：后台线程串行执行自愈链 + tamer/gameMode 启动（不阻塞启动屏动画）；
        //     完成后在后台线程回调 onBooted（参数=引擎是否成功启动；调用方自行切回 UI 线程）。
        //     启动失败（booted=false）时主窗口照常打开但压制/检测已死——调用方必须向用户明示，
        //     不能静默降级成"界面一切如常但功能全无"的隐身故障。 ----
        public void Boot(Action<bool> onBooted)
        {
            bootThread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    try { HealChain(); }
                    catch (Exception ex) { try { Logger.Log("自愈链异常：" + ex); } catch { } }
                    // 与 WinForms 的 lock(startGate) 同一模式：串行化"退出 vs 启动"，避免 Shutdown 与 tamer.Start 交错
                    lock (startGate)
                    {
                        if (exiting) return;
                        tamer.Start();
                        gameMode.Start();
                        booted = true;
                    }
                }
                catch (Exception ex)
                {
                    try { Logger.Log("WPF 运行时启动失败：" + ex); } catch { }
                }
                if (onBooted != null)
                {
                    try { onBooted(booted); } catch { }
                }
            }));
            bootThread.IsBackground = true;
            bootThread.Start();

            procNotify.CaptureStartIdentity = delegate(string name, int session)
            {
                return gameMode.NeedsWhitelistParentIdentity(session)
                    || gameMode.NeedsGameProcessIdentity(name, session)
                    || BuildCatalog.IsMatch(name)
                    || DevServiceCatalog.IsMatch(name);
            };
            procNotify.CaptureParentIdentity = delegate(int parentPid, string name, int session)
            {
                return gameMode.NeedsWhitelistParentIdentity(session)
                    || gameMode.NeedsLauncherChildParentIdentity(parentPid, name, session);
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
            HealthCare.ShouldDefer = delegate { return gameMode.IsActive; };
            try { HealthCare.StartAuto(); } catch { }

            powerPollTimer.Change(5000, 5000);
        }

        // ---- 崩溃自愈链：与 src/Program.cs 的启动顺序一致，逐项还原上次未完成的状态 ----
        // ---- 静默容错执行：单项失败记日志但不中断后续项（自愈/还原/停止链专用） ----
        private static void Run(string what, Action act)
        {
            try { act(); }
            catch (Exception ex)
            {
                try { Logger.Log(what + " 失败：" + ex.GetType().Name + " - " + ex.Message); } catch { }
            }
        }

        private void HealChain()
        {
            Run("LegacyFreezeRecovery 自愈", delegate { LegacyFreezeRecovery.BeginHeal(Path.Combine(dataDir, LegacyFreezeRecovery.StateFileName)); });
            Run("SuppressionCore 自愈", delegate
            {
                int healedSuppression = SuppressionCore.HealFromCrash(Path.Combine(dataDir, SuppressionCore.StateFileName));
                if (healedSuppression > 0)
                    Logger.Log("检测到上次未还原的分级后台控制，已恢复 " + healedSuppression + " 个进程");
            });
            Run("PowerPlan 自愈", PowerPlan.HealFromCrash);
            Run("UpdatePause 自愈", UpdatePause.HealFromCrash);
            Run("FgBoost 清理", FgBoost.PurgeLegacy);
            Run("GameDvr 自愈", GameDvr.HealFromCrash);
            Run("Mmcss 清理", Mmcss.PurgeLegacy);
            Run("Notif 自愈", Notif.HealFromCrash);
            Run("VisualFx 自愈", VisualFx.HealFromCrash);
            Run("DisplayGuard 自愈", DisplayGuard.HealFromCrash);
            Run("NvGlobalTweaks 自愈", NvGlobalTweaks.HealFromCrash);
            Run("AdlxTweaks 自愈", AdlxTweaks.HealFromCrash);
            Run("PresenceQos 自愈", PresenceQos.HealFromCrash);
            Run("PowerOverlay 自愈", PowerOverlay.HealFromCrash);
            Run("RenderLane 自愈", RenderLane.HealFromCrash);
            Run("CrashGuard 自愈", CrashGuard.HealFromCrash);
            Run("LegacyPurge", delegate { LegacyPurge.RunOnce(dataDir); });
            if (Settings.Load("GmIfeoBoost", false))
            {
                Run("IfeoBoost 预置", delegate
                {
                    int preArmed = IfeoBoost.PreArmAll();
                    if (preArmed > 0) Logger.Log("后备提优：已预置 " + preArmed + " 个游戏");
                });
            }
            Run("SvcPause 自愈", SvcPause.HealFromCrash);
            Run("DoTweak 自愈", DoTweak.HealFromCrash);
        }

        // ---- 关机/注销时的还原链（重启后仍生效的改动优先还原） ----
        public void RestorePersistentChanges()
        {
            // 场景先退出仲裁器：随后的游戏关闭（异步 Deactivate）点 ActiveChanged(false)
            // 时已无候选掌权者，不会在退出途中把开发/日常场景重新授权一遍再拆掉。
            Run("场景事件泵停止", scenarioPump.Stop);
            Run("健康维护调度停止", HealthCare.StopAuto);
            Run("DevFocus 停止", devFocus.Stop);
            Run("DailyCare 停止", dailyCare.Stop);
            Run("DevServiceGuard 停止", devServiceGuard.Stop);
            Run("GameMode 关闭", delegate { gameMode.Enabled = false; });
            Run("PowerPlan 还原", delegate { PowerPlan.Restore(); });
            Run("GameDvr 还原", delegate { GameDvr.Restore(); });
            Run("Notif 还原", delegate { Notif.Restore(); });
            Run("Mmcss 还原", delegate { Mmcss.Restore(); });
            Run("FgBoost 还原", delegate { FgBoost.Restore(); });
            Run("VisualFx 还原", delegate { VisualFx.Restore(); });
            Run("NvGlobalTweaks 还原", delegate { NvGlobalTweaks.Restore(); });
            Run("Adlx AntiLag 还原", delegate { AdlxTweaks.RestoreAntiLag(); });
            Run("Adlx Chill 还原", delegate { AdlxTweaks.RestoreChill(); });
            Run("Adlx EnhancedSync 还原", delegate { AdlxTweaks.RestoreEnhancedSync(); });
            Run("Adlx Ris 还原", delegate { AdlxTweaks.RestoreRis(); });
            Run("PresenceQos 还原", delegate { PresenceQos.Restore(); });
            Run("PowerOverlay 还原", delegate { PowerOverlay.Restore(); });
            // 游戏侧的 SvcPause 还原在工作线程的 Deactivate 里，进程可能在此之前被终结：
            // 这里按注册表标志同步兜底（幂等，无标志时零开销）
            Run("SvcPause 还原", delegate { SvcPause.Restore(); });
        }

        public void Shutdown()
        {
            lock (startGate)
            {
                if (exiting) return;
                exiting = true;
            }
            Run("电源轮询定时器释放", delegate { powerPollTimer.Dispose(); });
            Run("健康维护调度停止", HealthCare.StopAuto);
            Run("ProcNotify 停止", procNotify.Stop);
            // 泵先停收：积压批次若在场景 Stop 之后到达会把场景重新激活
            Run("场景事件泵停止", scenarioPump.Stop);
            // 场景先于 GameMode 停止：否则游戏退出事件会在退出途中重新授权开发/日常场景，
            // 紧接着又被 Stop 还原——白做一轮服务暂停/通知静默/压制扫描，还污染专注时长统计
            Run("DevFocus 停止", devFocus.Stop);
            Run("DailyCare 停止", dailyCare.Stop);
            Run("DevServiceGuard 停止", devServiceGuard.Stop);
            Run("Tamer 停止", tamer.Stop);
            Run("GameMode 停止", gameMode.Stop);
        }

        public ContextMenuStrip BuildTrayMenu(Action openPanel, Action exitApp, Action afterChange)
        {
            return new WpfTrayMenu(tamer, gameMode, devFocus, openPanel, exitApp, afterChange).Strip;
        }
    }
}
