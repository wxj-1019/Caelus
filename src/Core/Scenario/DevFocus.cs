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
        private readonly HashSet<int> activeBuildPids = new HashSet<int>();
        private readonly HashSet<int> activeIdePids = new HashSet<int>();
        private readonly HashSet<string> distractNotified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private long lastWindowCheckTicks;
        private bool granted;
        private bool quietApplied;
        private Timer reconcileTimer;
        private long sessionStartTicks;
        private readonly Dictionary<int, uint> ideBoosted = new Dictionary<int, uint>();
        private readonly Dictionary<int, long> ideBoostedCreation = new Dictionary<int, long>();

        /// <summary>编译会话状态变化时触发，参数是文案 key（bal.buildstart / bal.buildend）</summary>
        public event Action<string> SessionChanged;

        public override ScenarioKind Kind { get { return ScenarioKind.DevFocus; } }
        public override int Priority { get { return 50; } }

        /// <summary>专注模式开关状态。实时读注册表——WPF 宿主（独立进程）修改后本进程下次评估即生效</summary>
        public bool FocusModeOn { get { return Settings.Load("DevFocusModeOn", false); } }

        /// <summary>三来源任一活跃：编译进程、专注模式、IDE 进程</summary>
        private bool AnyActive
        {
            get { return activeBuildPids.Count > 0 || FocusModeOn || activeIdePids.Count > 0; }
        }

        protected override bool WantsActiveLocked
        {
            get { return enabled() && (activeBuildPids.Count > 0 || FocusModeOn || activeIdePids.Count > 0); }
        }

        /// <summary>检测状态：是否存在任一活性来源（与是否掌权无关）</summary>
        public bool IsActive { get { lock (sync) return AnyActive; } }

        /// <summary>仲裁器授权状态：副作用是否已施加</summary>
        public bool IsGranted { get { lock (sync) return granted; } }

        /// <summary>测试钩子：校正定时器是否运行中（应只在掌权期间为 true）</summary>
        internal bool FocusTimerRunning { get { lock (sync) return reconcileTimer != null; } }

        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core, Func<bool> enabled,
            Func<string, string, bool> isWhitelisted, Func<string, bool> isDistract)
            : base(arbiter)
        {
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            this.isWhitelisted = isWhitelisted;
            this.isDistract = isDistract;
        }

        /// <summary>专注模式开关（托盘菜单/设置页调用）。写注册表 + 活性重算。</summary>
        public void SetFocusMode(bool on)
        {
            Settings.Save("DevFocusModeOn", on);
            if (!on) { lock (sync) { distractNotified.Clear(); } }
            RecomputeActivity();
        }

        /// <summary>统一重算活性：三来源任一 + 开关 → 报告仲裁器。</summary>
        private void RecomputeActivity()
        {
            bool becameActive = false;
            bool becameIdle = false;
            lock (sync)
            {
                bool nowActive = enabled() && AnyActive;
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
            }
            if (becameActive) arbiter.ReportActivity(Kind, true);
            if (becameIdle) arbiter.ReportActivity(Kind, false);
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
                            activeBuildPids.Add(pc.Pid);
                        else if (pc.Kind == ProcessChangeKind.Stopped)
                            activeBuildPids.Remove(pc.Pid);
                    }

                    // IDE 进程匹配（Task 4 接线，当前 IsIdeProcess 为 stub）
                    if (pc.Kind == ProcessChangeKind.Started && IsIdeProcess(pc.Pid, pc.Name, pc.Path))
                        activeIdePids.Add(pc.Pid);
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                        activeIdePids.Remove(pc.Pid);

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

                // TODO: SvcPause 引用计数——当 GameMode (Custom preset + svcPauseOn) 退出时，
                // RestoreEnv() 会在 arbiter 回调 Grant 之后调 SvcPause.Restore()，覆盖此处。
                // 当前仅在 Custom preset 用户手动开启 svcPauseOn 且游戏退出时编译还在跑时触发。
                // 根治需要 SvcPause 引用计数化（Activate 递增/Restore 递减/计数归零才真 Restore），
                // 或 GameMode 成为 IScenario 后 SvcPause 控制权完全归仲裁器。
                if (build)
                {
                    SvcPause.Activate();
                    BoostBuildProcesses();
                }
                // 编译深化与专注模式共用同一套常规档压制（Build 位）
                if (build || focus) SweepBuildSuppression();
                if (focus)
                {
                    try { if (Notif.Quiet()) { lock (sync) quietApplied = true; } } catch { }
                    StartReconcileTimer();
                }
                if (ide) ReconcileIdeBoost();

                Logger.Log("开发专注：获得掌职权（编译=" + build + " 专注=" + focus + " IDE=" + ide + "）");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注掌权失败", ex); }
        }

        /// <summary>IScenario：挂起——还原全部副作用，检测状态保留</summary>
        public override void Suspend()
        {
            bool wasQuiet;
            lock (sync)
            {
                if (!granted) return;
                granted = false;
                wasQuiet = quietApplied;
                quietApplied = false;
            }
            try
            {
                StopReconcileTimer();
                RestoreIdeBoost();
                if (core != null) core.ReleaseReason(SuppressReason.Build);
                if (wasQuiet) Notif.Restore();
                SvcPause.Restore();
                Logger.Log("开发专注：挂起，全部副作用已还原（检测继续）");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注挂起失败", ex); }
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
            {
                try
                {
                    IntPtr h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        Native.SetPriorityClass(h, Native.HIGH_PRIORITY_CLASS);
                        Native.TrySetIoPriority(h, 3);
                    }
                    finally { Native.CloseHandle(h); }
                }
                catch { }
            }
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

        /// <summary>全量扫描后台进程并按编译位压制。在 ProcNotify 事件线程同步执行（沿用
        /// BuildWatch 既定模式）；扫描耗时与 SvcPause 同量级，若实测阻塞事件流再改异步+代数校验。</summary>
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

            int suppressed = 0;
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
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

                    AcquireResult r = core.Acquire(pid, nm, SuppressReason.Build, "devfocus",
                        SuppressionLevel.Eco);
                    if (r == AcquireResult.NewlyThrottled) suppressed++;
                }
                catch { }
                finally { p.Dispose(); }
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
                bool focus;
                focus = FocusModeOn;
                if (focus) SweepBuildSuppression();   // Acquire 对已压进程返回 AlreadyThrottled，幂等
                ReconcileIdeBoost();
            }
            catch { }
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
            if (!anyVisible) { RestoreIdeBoost(); return; }

            foreach (int pid in ides) BoostOneIde(pid);
        }

        private void BoostOneIde(int pid)
        {
            lock (sync) { if (ideBoosted.ContainsKey(pid)) return; }

            long creation = 0;
            try { using (var p = Process.GetProcessById(pid)) creation = p.StartTime.Ticks; }
            catch { }

            IntPtr h = Native.OpenProcess(
                Native.PROCESS_SET_INFORMATION | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return;
            try
            {
                uint orig = Native.GetPriorityClass(h);
                if (orig == 0) return;
                if (orig == Native.HIGH_PRIORITY_CLASS || orig == 0x100) return;
                if (orig == Native.ABOVE_NORMAL_PRIORITY_CLASS) return;

                Native.SetPriorityClass(h, Native.ABOVE_NORMAL_PRIORITY_CLASS);
                if (Native.GetPriorityClass(h) != Native.ABOVE_NORMAL_PRIORITY_CLASS) return;
                Native.TrySetIoPriority(h, 3);

                lock (sync)
                {
                    ideBoosted[pid] = orig;
                    ideBoostedCreation[pid] = creation;
                }
            }
            catch { }
            finally { Native.CloseHandle(h); }
        }

        internal void RestoreIdeBoost()
        {
            KeyValuePair<int, uint>[] snap;
            KeyValuePair<int, long>[] snapCreation;
            lock (sync)
            {
                if (ideBoosted.Count == 0) return;
                snap = new KeyValuePair<int, uint>[ideBoosted.Count];
                ((ICollection<KeyValuePair<int, uint>>)ideBoosted).CopyTo(snap, 0);
                ideBoosted.Clear();
                snapCreation = new KeyValuePair<int, long>[ideBoostedCreation.Count];
                ((ICollection<KeyValuePair<int, long>>)ideBoostedCreation).CopyTo(snapCreation, 0);
                ideBoostedCreation.Clear();
            }
            var creationMap = new Dictionary<int, long>();
            foreach (var kv in snapCreation) creationMap[kv.Key] = kv.Value;

            foreach (var kv in snap)
            {
                try
                {
                    long expectCreation;
                    if (creationMap.TryGetValue(kv.Key, out expectCreation))
                    {
                        long nowCreation;
                        try { nowCreation = Process.GetProcessById(kv.Key).StartTime.Ticks; }
                        catch { continue; }
                        if (nowCreation != expectCreation) continue;
                    }
                    IntPtr h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION, false, kv.Key);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        Native.SetPriorityClass(h, kv.Value);
                        Native.TrySetIoPriority(h, 2);
                    }
                    finally { Native.CloseHandle(h); }
                }
                catch { }
            }
        }

        /// <summary>测试钩子：绕过窗口条件直接提优单个进程（返回是否入快照）</summary>
        internal bool BoostIdeForTest(int pid)
        {
            BoostOneIde(pid);
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
