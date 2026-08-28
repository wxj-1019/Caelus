// @author zenjiro 18967498922@163.com
// 文件用途 开发专注场景：检测编译/调试进程，掌权时暂停索引、提优编译器并压制后台

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CaelusApp
{
    internal sealed class DevFocus : ScenarioBase
    {
        private readonly SuppressionCore core;
        private readonly Func<bool> enabled;
        private readonly Func<string, string, bool> isWhitelisted;
        private readonly Func<string, bool> isDistract;
        // 游戏接管感知（交接直通用）：挂起时若游戏正要同一共享效果，则把占用权直接
        // 移交给游戏侧，避免「还原再重做」的服务停启/通知开关抖动。未接线时恒走普通还原。
        private readonly Func<bool> gameKeepsSvcPause;
        private readonly Func<bool> gameKeepsNotifQuiet;
        private readonly HashSet<int> activeBuildPids = new HashSet<int>();
        private readonly HashSet<int> activeIdePids = new HashSet<int>();
        private readonly HashSet<string> distractNotified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool granted;
        private bool quietApplied;
        private Timer reconcileTimer;
        private long sessionStartTicks;
        private long grantStartTicks;
        private readonly Dictionary<int, uint> ideBoosted = new Dictionary<int, uint>();
        private readonly Dictionary<int, long> ideBoostedCreation = new Dictionary<int, long>();
        private readonly Dictionary<int, string> ideBoostedName = new Dictionary<int, string>();
        private readonly Dictionary<int, int> ideBoostedIo = new Dictionary<int, int>();
        private readonly Dictionary<int, uint> buildBoosted = new Dictionary<int, uint>();
        private readonly Dictionary<int, long> buildBoostedCreation = new Dictionary<int, long>();
        private readonly Dictionary<int, string> buildBoostedName = new Dictionary<int, string>();
        private readonly Dictionary<int, int> buildBoostedIo = new Dictionary<int, int>();

        /// <summary>编译会话状态变化时触发，参数是文案 key（bal.buildstart / bal.buildend）</summary>
        public event Action<string> SessionChanged;

        public override ScenarioKind Kind { get { return ScenarioKind.DevFocus; } }
        public override int Priority { get { return 50; } }

        /// <summary>专注模式开关状态。实时读注册表——WPF 宿主（独立进程）修改后本进程下次评估即生效</summary>
        public bool FocusModeOn { get { return Settings.Load("DevFocusModeOn", false); } }

        /// <summary>IDE 优化开关（默认开）。关闭后 IDE 家族不再提优、也不再作为活性来源。</summary>
        public bool IdeOn { get { return Settings.Load("DevFocusIdeOn", true); } }

        /// <summary>IDE 进程有可见窗口才计入活性（后台挂起的常驻程序不激活场景）。</summary>
        private bool ideVisible;
        private long lastIdeWindowCheckTicks;

        /// <summary>三来源任一活跃：编译进程、专注模式、IDE 进程（须有可见窗口）</summary>
        private bool AnyActive
        {
            get { return activeBuildPids.Count > 0 || FocusModeOn || (IdeOn && ideVisible); }
        }

        protected override bool WantsActiveLocked
        {
            get { return enabled() && (activeBuildPids.Count > 0 || FocusModeOn || (IdeOn && ideVisible)); }
        }

        /// <summary>检测状态：是否存在任一活性来源（与是否掌权无关）</summary>
        public bool IsActive { get { lock (sync) return AnyActive; } }

        /// <summary>仲裁器授权状态：副作用是否已施加</summary>
        public bool IsGranted { get { lock (sync) return granted; } }

        /// <summary>测试钩子：校正定时器是否运行中（应只在掌权期间为 true）</summary>
        internal bool FocusTimerRunning { get { lock (sync) return reconcileTimer != null; } }

        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core, Func<bool> enabled,
            Func<string, string, bool> isWhitelisted, Func<string, bool> isDistract)
            : this(arbiter, core, enabled, isWhitelisted, isDistract, null, null)
        {
        }

        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core, Func<bool> enabled,
            Func<string, string, bool> isWhitelisted, Func<string, bool> isDistract,
            Func<bool> gameKeepsSvcPause, Func<bool> gameKeepsNotifQuiet)
            : base(arbiter)
        {
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            this.isWhitelisted = isWhitelisted;
            this.isDistract = isDistract;
            this.gameKeepsSvcPause = gameKeepsSvcPause;
            this.gameKeepsNotifQuiet = gameKeepsNotifQuiet;
        }

        /// <summary>专注模式开关（托盘菜单/设置页调用）。写注册表 + 活性重算。</summary>
        public void SetFocusMode(bool on)
        {
            Settings.Save("DevFocusModeOn", on);
            if (!on) { lock (sync) { distractNotified.Clear(); } }
            RecomputeActivity();
        }

        /// <summary>IDE 优化开关（设置页调用）。关闭时清空已追踪的 IDE 集合，避免 Grant 仍提优。</summary>
        public void SetIdeOn(bool on)
        {
            Settings.Save("DevFocusIdeOn", on);
            if (!on)
            {
                lock (sync)
                {
                    activeIdePids.Clear();
                    ideVisible = false;
                }
            }
            RecomputeActivity();
        }

        /// <summary>场景总开关（设置页/场景总览调用）。关闭时立即退出仲裁器活性集合并还原副作用，
        /// 避免「被游戏抢占后关闭开关、游戏退出时场景又恢复掌权」的残留。
        /// IDE/编译提优快照由 Suspend→RestoreIdeBoost 在 ForceReportInactive 内同步还原。</summary>
        public void SetEnabled(bool on)
        {
            Settings.Save("DevModeOn", on);
            if (!on)
            {
                lock (sync)
                {
                    activeBuildPids.Clear();
                    activeIdePids.Clear();
                    ideVisible = false;
                    distractNotified.Clear();
                }
                ForceReportInactive();
            }
            else
            {
                RecomputeActivity();
            }
        }

        public void NotifyProcessChanges(ProcessChangeBatch batch)
        {
            if (batch == null || batch.Changes == null) return;

            // 开关关闭：撤销活性报告（仲裁器会回调 Suspend 还原副作用），避免服务被永久暂停
            if (!enabled())
            {
                bool wasReported;
                lock (sync)
                {
                    wasReported = reported;
                    reported = false;
                    activeBuildPids.Clear();
                    activeIdePids.Clear();
                }
                if (wasReported) arbiter.ReportActivity(Kind, false);
                return;
            }

            bool buildActivity = false;  // 仅编译来源变化时触发文案/日志
            bool becameActive = false;
            bool becameIdle = false;
            bool wasBuildActive;
            bool ideChanged = false;
            List<int> newlyBuilt = null;

            lock (sync)
            {
                wasBuildActive = activeBuildPids.Count > 0;

                foreach (ProcessChange pc in batch.Changes)
                {
                    if (string.IsNullOrEmpty(pc.Name)) continue;

                    // 编译进程匹配
                    if (BuildCatalog.IsMatch(pc.Name))
                    {
                        if (pc.Kind == ProcessChangeKind.Started)
                        {
                            if (activeBuildPids.Add(pc.Pid))
                            {
                                if (newlyBuilt == null) newlyBuilt = new List<int>();
                                newlyBuilt.Add(pc.Pid);
                            }
                        }
                        else if (pc.Kind == ProcessChangeKind.Stopped)
                            activeBuildPids.Remove(pc.Pid);
                    }

                    // IDE 进程匹配（Task 4 接线：名称预筛 + 安装目录双重校验，见 IsIdeProcess）
                    if (pc.Kind == ProcessChangeKind.Started && IdeOn && IsIdeProcess(pc.Pid, pc.Name, pc.Path))
                    {
                        if (activeIdePids.Add(pc.Pid)) ideChanged = true;
                    }
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                    {
                        if (activeIdePids.Remove(pc.Pid)) ideChanged = true;
                    }

                    // 专注模式下新进程的分心提醒
                    if (pc.Kind == ProcessChangeKind.Started && granted && FocusModeOn
                        && isDistract != null && isDistract(pc.Name)
                        && !distractNotified.Contains(pc.Name))
                    {
                        distractNotified.Add(pc.Name);
                        try
                        {
                            var h = SessionChanged;
                            if (h != null) h("bal.distract");
                        }
                        catch { }
                    }
                }

                // 兜底清理：短命进程的 Stopped 事件可能因进程已退出而丢失
                CleanDeadPids(activeBuildPids);
                CleanDeadPids(activeIdePids);

                bool nowActive = AnyActive;
                if (nowActive && !reported)
                {
                    reported = true;
                    becameActive = true;
                    sessionStartTicks = DateTime.UtcNow.Ticks;
                }
                else if (!nowActive && reported)
                {
                    reported = false;
                    becameIdle = true;
                }

                buildActivity = wasBuildActive != (activeBuildPids.Count > 0);
            }

            // 掌权期间新启动的编译进程同步提优（Grant 只提当时已存在的进程）；
            // 未掌权时不在此提优，交给随后的 Grant 统一处理
            if (newlyBuilt != null)
            {
                bool isGranted;
                lock (sync) isGranted = granted;
                if (isGranted)
                    foreach (int pid in newlyBuilt)
                        BoostOneWithSnapshot(() => granted, pid, Native.HIGH_PRIORITY_CLASS,
                            buildBoosted, buildBoostedCreation, buildBoostedName, buildBoostedIo);
            }

            // IDE 集合变化时复查可见窗口（节流）：无窗口的常驻 IDE 不激活场景
            if (ideChanged) RefreshIdeVisible(false);

            // 活性变化只向仲裁器报告；副作用由仲裁器经 Grant/Suspend 回调控制
            if (becameActive)
            {
                if (buildActivity)
                    try { var h = SessionChanged; if (h != null) h("bal.buildstart"); } catch { }
                arbiter.ReportActivity(Kind, true);
            }
            if (becameIdle)
            {
                if (buildActivity)
                {
                    long elapsedMs = (DateTime.UtcNow.Ticks - sessionStartTicks) / TimeSpan.TicksPerMillisecond;
                    Logger.Log(string.Format("开发专注：本次编译 {0:0.#} 秒", elapsedMs / 1000.0));
                    try { var h = SessionChanged; if (h != null) h("bal.buildend"); } catch { }
                }
                arbiter.ReportActivity(Kind, false);
            }
        }

        /// <summary>初始扫描发现的进程：与进程事件逻辑一致，直接加入追踪集合。</summary>
        protected override void OnInitialProcess(ProcessChange change)
        {
            if (string.IsNullOrEmpty(change.Name)) return;
            if (BuildCatalog.IsMatch(change.Name))
            {
                lock (sync) activeBuildPids.Add(change.Pid);
            }
            if (IdeOn && IsIdeProcess(change.Pid, change.Name, change.Path))
            {
                lock (sync) activeIdePids.Add(change.Pid);
            }
        }

        /// <summary>清理 PID 集合中已死进程。</summary>
        private static void CleanDeadPids(HashSet<int> pids)
        {
            if (pids.Count == 0) return;
            var dead = new List<int>();
            foreach (int pid in pids)
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) { dead.Add(pid); continue; }
                try { if (!Native.StillActive(h)) dead.Add(pid); }
                finally { Native.CloseHandle(h); }
            }
            foreach (int pid in dead) pids.Remove(pid);
        }

        /// <summary>判断进程是否为 IDE 进程。名称预筛 + 安装目录双重校验。</summary>
        private bool IsIdeProcess(int pid, string name, string path)
        {
            if (!IdeCatalog.NameMatches(name)) return false;
            string p = path;
            if (string.IsNullOrEmpty(p))
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return false;
                try { p = Native.ImagePath(h); }
                finally { Native.CloseHandle(h); }
            }
            return IdeCatalog.IsMatch(name, p);
        }

        /// <summary>IScenario：获得掌职权——暂停索引服务、提优编译进程（后台压制在 Task 4 加入）</summary>
        public override void Grant()
        {
            lock (sync)
            {
                if (granted) return;
                granted = true;
                grantStartTicks = DateTime.UtcNow.Ticks;
            }
            try
            {
                bool build;
                bool focus;
                bool ide;
                lock (sync)
                {
                    build = activeBuildPids.Count > 0;
                    focus = FocusModeOn;
                    ide = activeIdePids.Count > 0;
                }

                // 注：GameMode.Deactivate 的 ActiveChanged(false) 已移到 RestoreEnv 之后触发
                // （审查迭代 2026-08），本场景 Activate 不再被游戏还原路径覆盖。
                // SvcPause/Notif 现为多占用方引用计数（SharedEffectClaim），游戏与本场景
                // 叠加时最后一个占用方离开才还原；交接直通见 Suspend。
                if (build)
                {
                    SvcPause.Activate(SvcPause.OwnerDevFocus);
                    BoostBuildProcesses();
                }
                // 编译深化与专注模式共用同一套常规档压制（Build 位）
                if (build || focus) SweepBuildSuppression();
                if (focus)
                {
                    try { if (Notif.Quiet(Notif.OwnerDevFocus)) { lock (sync) quietApplied = true; } } catch { }
                }
                // 校正节拍在编译/专注任一来源下都运行：长编译期间增量追压新后台，
                // 专注模式还需节拍感知 WPF 宿主跨进程的开关翻转（无进程事件时也能解除）。
                if (build || focus) StartReconcileTimer();
                if (ide) ReconcileIdeBoost();

                Logger.Log("开发专注：获得掌职权（编译=" + build + " 专注=" + focus + " IDE=" + ide + "）");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注掌权失败", ex); }
        }

        /// <summary>IScenario：挂起——还原全部副作用，检测状态保留。
        /// 还原链逐步故障隔离：granted 已翻 false、仲裁器不会重试，单步抛异常
        /// 若跳过后续步骤，残留只能等启动自愈——每步独立 try，失败计数并在日志明示。</summary>
        public override void Suspend()
        {
            bool wasQuiet;
            long elapsed = 0;
            lock (sync)
            {
                if (!granted) return;
                granted = false;
                wasQuiet = quietApplied;
                quietApplied = false;
                elapsed = DateTime.UtcNow.Ticks - grantStartTicks;
            }
            if (elapsed > 0) FocusStats.RecordSession(elapsed);

            int failed = 0;
            try { StopReconcileTimer(); }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：停止校正节拍失败", ex); }
            try { RestoreIdeBoost(); }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：还原 IDE 提优失败", ex); }
            try { RestoreBuildBoost(); }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：还原编译提优失败", ex); }
            try { if (core != null) core.ReleaseReason(SuppressReason.Build); }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：解除后台压制失败", ex); }
            // 共享效果交接直通：游戏正要同一效果时只移交占用权，不还原再重做
            if (wasQuiet)
            {
                try
                {
                    if (gameKeepsNotifQuiet != null && gameKeepsNotifQuiet())
                        Notif.HandoffOwner(Notif.OwnerDevFocus, Notif.OwnerGame);
                    else
                        Notif.Restore(Notif.OwnerDevFocus);
                }
                catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：还原通知静默失败", ex); }
            }
            try
            {
                if (gameKeepsSvcPause != null && gameKeepsSvcPause())
                    SvcPause.HandoffOwner(SvcPause.OwnerDevFocus, SvcPause.OwnerGame);
                else
                    SvcPause.Restore(SvcPause.OwnerDevFocus);
            }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：还原服务暂停失败", ex); }

            if (failed == 0) Logger.Log("开发专注：挂起，全部副作用已还原（检测继续）");
            else Logger.Log("开发专注：挂起完成，但 " + failed + " 个还原步骤失败（残留由下次启动自愈兜底）");
        }

        private void BoostBuildProcesses()
        {
            int[] pids;
            lock (sync)
            {
                pids = new int[activeBuildPids.Count];
                activeBuildPids.CopyTo(pids);
            }
            foreach (int pid in pids)
                BoostOneWithSnapshot(() => granted, pid, Native.HIGH_PRIORITY_CLASS,
                    buildBoosted, buildBoostedCreation, buildBoostedName, buildBoostedIo);
        }

        internal void RestoreBuildBoost()
        {
            RestoreBoostSnapshot(buildBoosted, buildBoostedCreation, buildBoostedName, buildBoostedIo);
        }

        /// <summary>常规档压制决策（纯逻辑，可单测）：复用游戏模式的常规档豁免计算器，
        /// 再叠加白名单。activeGameRoot/游戏宿主祖先在游戏不活跃时无意义，不传入。</summary>
        internal static bool ShouldSuppressBackground(int pid, int selfPid, string name, string path,
            int session, int ownerSession, int foregroundPid, HashSet<int> visibleWindowPids,
            string windowsRoot, Func<string, string, bool> isWhitelisted)
        {
            bool userFacing = visibleWindowPids != null && visibleWindowPids.Contains(pid);
            if (!GameMode.BasicBackgroundEligible(pid, selfPid, name, path, session, ownerSession,
                foregroundPid, userFacing, windowsRoot)) return false;
            if (isWhitelisted != null && isWhitelisted(name, path)) return false;
            return true;
        }

        /// <summary>全量扫描后台进程并按编译位压制。在场景事件泵线程执行（ScenarioEventPump），
        /// 不占用 ProcNotify 事件线程——游戏启动/退出检测不被本扫描拖延。</summary>
        private void SweepBuildSuppression()
        {
            if (core == null) return;
            int selfPid = Process.GetCurrentProcess().Id;
            int ownerSession;
            try { ownerSession = Process.GetCurrentProcess().SessionId; } catch { ownerSession = -1; }
            int foregroundPid;
            try { foregroundPid = GameSessionDetector.ForegroundPid(); } catch { foregroundPid = 0; }
            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { visible = new HashSet<int>(); }
            string windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            // 两段式（与游戏模式 Sweep 一致）：先枚举收集候选，再在批窗口内只做
            // Acquire——BeginBatch 持有全局锁直到 EndBatch，进程枚举/句柄查询
            // 不能压进锁窗口，否则 Tamer 等线程整个扫描期读到冻结状态
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
            var candidates = new List<KeyValuePair<int, string>>();
            foreach (Process p in all)
            {
                try
                {
                    int pid = p.Id;
                    if (pid <= 4 || pid == selfPid) continue;
                    // 编译进程本身是提优对象（HIGH），绝不被后台压制——否则先提后压自相矛盾
                    lock (sync) { if (activeBuildPids.Contains(pid)) continue; }

                    string nm;
                    try { nm = p.ProcessName; } catch { continue; }
                    int session;
                    try { session = p.SessionId; } catch { session = -1; }

                    string ipath = null;
                    IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (h != IntPtr.Zero)
                    {
                        try { ipath = Native.ImagePath(h); }
                        finally { Native.CloseHandle(h); }
                    }

                    if (!ShouldSuppressBackground(pid, selfPid, nm, ipath, session, ownerSession,
                        foregroundPid, visible, windowsRoot, isWhitelisted)) continue;

                    candidates.Add(new KeyValuePair<int, string>(pid, nm));
                }
                catch { }
                finally { p.Dispose(); }
            }

            int suppressed = 0;
            List<int> newlyThrottled = null;
            SuppressionCore.BatchResult batch;
            core.BeginBatch();
            try
            {
                foreach (KeyValuePair<int, string> c in candidates)
                {
                    AcquireResult r = core.Acquire(c.Key, c.Value, SuppressReason.Build, "devfocus",
                        SuppressionLevel.Eco);
                    if (r == AcquireResult.NewlyThrottled)
                    {
                        suppressed++;
                        if (newlyThrottled == null) newlyThrottled = new List<int>();
                        newlyThrottled.Add(c.Key);
                    }
                }
            }
            finally
            {
                batch = core.EndBatch();
                // 批量涂写在 EndBatch 才落盘：记账的"新压制"数按实际生效结果校正
                if (batch != null && newlyThrottled != null)
                    foreach (int pid in newlyThrottled)
                        if (!batch.WasApplied(pid)) suppressed--;
            }
            if (suppressed > 0)
                Logger.Log("开发专注：编译期间压制 " + suppressed + " 个后台进程（编译位，退出即还原）");
        }

        private void StartReconcileTimer()
        {
            lock (sync)
            {
                if (reconcileTimer != null) return;
                reconcileTimer = new Timer(
                    _ => ReconcileTick(), null, 30000, 30000);
            }
        }

        private void StopReconcileTimer()
        {
            Timer t;
            lock (sync)
            {
                t = reconcileTimer;
                reconcileTimer = null;
            }
            if (t != null) t.Dispose();
        }

        /// <summary>校正节拍：增量追压新后台 + IDE 窗口条件复查。回调到达时可能已挂起，先检查。</summary>
        private void ReconcileTick()
        {
            lock (sync) { if (!granted) return; }
            try
            {
                bool build;
                bool focus;
                lock (sync) { build = activeBuildPids.Count > 0; }
                focus = FocusModeOn;
                // IDE 窗口条件复查（节流），无可见窗口的 IDE 不再维持场景活性
                RefreshIdeVisible(false);
                // 专注开关可能已被 WPF 宿主跨进程关闭：本进程没有进程事件时，靠节拍重算活性
                // 触发仲裁器挂起（还原 Notif 静默等副作用），避免开关失效延迟到下一个进程事件。
                RecomputeActivity();
                lock (sync) { if (!granted) return; }
                if (build || focus) SweepBuildSuppression();   // Acquire 对已压进程返回 AlreadyThrottled，幂等
                ReconcileIdeBoost();
                // 竞态护栏：挂起可能在扫描期间到达（granted 已翻 false），泄漏的压制立即回收；
                // 若挂起在护栏之后到达，Suspend 自带的 ReleaseReason(Build) 会兜底。
                lock (sync) { if (!granted && core != null) core.ReleaseReason(SuppressReason.Build); }
            }
            catch { }
        }

        /// <summary>IDE 可见窗口复查（2 秒节流）：有可见窗口的 IDE 才计入场景活性。
        /// 在进程事件、初始扫描完成与校正节拍中调用。</summary>
        private void RefreshIdeVisible(bool force)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (sync)
            {
                if (!force && now - lastIdeWindowCheckTicks < 2L * TimeSpan.TicksPerSecond) return;
                lastIdeWindowCheckTicks = now;
            }
            if (activeIdePids.Count == 0)
            {
                lock (sync) ideVisible = false;
                return;
            }
            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { return; }
            bool anyVisible = false;
            lock (sync)
            {
                foreach (int pid in activeIdePids)
                    if (visible.Contains(pid)) { anyVisible = true; break; }
                ideVisible = anyVisible;
            }
        }

        /// <summary>初始扫描完成后补算 IDE 可见窗口，避免无窗口的常驻 IDE 立即激活场景。</summary>
        protected override void OnInitialScanComplete()
        {
            RefreshIdeVisible(true);
        }

        private void ReconcileIdeBoost()
        {
            int[] ides;
            lock (sync)
            {
                ides = new int[activeIdePids.Count];
                activeIdePids.CopyTo(ides);
            }
            if (ides.Length == 0) { RestoreIdeBoost(); return; }

            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { visible = new HashSet<int>(); }

            bool anyVisible = false;
            foreach (int pid in ides) if (visible.Contains(pid)) { anyVisible = true; break; }
            lock (sync) { ideVisible = anyVisible; }
            if (!anyVisible) { RestoreIdeBoost(); return; }

            foreach (int pid in ides) BoostOneIde(pid);
        }

        private void BoostOneIde(int pid)
        {
            BoostOneWithSnapshot(() => granted, pid, Native.ABOVE_NORMAL_PRIORITY_CLASS,
                ideBoosted, ideBoostedCreation, ideBoostedName, ideBoostedIo);
        }

        internal void RestoreIdeBoost()
        {
            RestoreBoostSnapshot(ideBoosted, ideBoostedCreation, ideBoostedName, ideBoostedIo);
        }

        /// <summary>测试钩子：绕过窗口条件与掌权检查直接提优单个进程（返回是否入快照）</summary>
        internal bool BoostIdeForTest(int pid)
        {
            BoostOneWithSnapshot(() => true, pid, Native.ABOVE_NORMAL_PRIORITY_CLASS,
                ideBoosted, ideBoostedCreation, ideBoostedName, ideBoostedIo);
            lock (sync) return ideBoosted.ContainsKey(pid);
        }

        /// <summary>程序退出时调用，确保还原。仅在 ProcNotify 停止后调用（退出路径单线程）</summary>
        public void Stop()
        {
            bool wasReported;
            lock (sync)
            {
                wasReported = reported;
                reported = false;
                activeBuildPids.Clear();
                activeIdePids.Clear();
                distractNotified.Clear();
            }
            // 走仲裁器单一路径还原（若正掌权会回调 Suspend）
            if (wasReported) arbiter.ReportActivity(Kind, false);
        }
    }
}
