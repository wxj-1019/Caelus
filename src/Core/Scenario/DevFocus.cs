// @author zenjiro 18967498922@163.com
// 文件用途 开发专注场景：检测编译/调试进程，掌权时暂停索引、提优编译器并压制后台

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CaelusApp
{
    internal sealed class DevFocus : IScenario
    {
        private readonly object sync = new object();
        private readonly ScenarioArbiter arbiter;
        private readonly SuppressionCore core;
        private readonly Func<bool> enabled;
        private readonly HashSet<int> activeBuildPids = new HashSet<int>();
        private bool granted;
        private bool reported;
        private long sessionStartTicks;

        /// <summary>编译会话状态变化时触发，参数是文案 key（bal.buildstart / bal.buildend）</summary>
        public event Action<string> SessionChanged;

        public ScenarioKind Kind { get { return ScenarioKind.DevFocus; } }
        public int Priority { get { return 50; } }

        /// <summary>检测状态：是否存在活跃的编译进程（与是否掌权无关）</summary>
        public bool IsActive { get { lock (sync) return activeBuildPids.Count > 0; } }

        /// <summary>仲裁器授权状态：副作用是否已施加</summary>
        public bool IsGranted { get { lock (sync) return granted; } }

        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core, Func<bool> enabled)
        {
            if (arbiter == null) throw new ArgumentNullException("arbiter");
            this.arbiter = arbiter;
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            arbiter.Register(this);
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
                }
                if (wasReported) arbiter.ReportActivity(Kind, false);
                return;
            }

            bool becameActive = false;
            bool becameIdle = false;

            lock (sync)
            {
                foreach (ProcessChange pc in batch.Changes)
                {
                    if (string.IsNullOrEmpty(pc.Name)) continue;
                    if (!BuildCatalog.IsMatch(pc.Name)) continue;

                    if (pc.Kind == ProcessChangeKind.Started)
                        activeBuildPids.Add(pc.Pid);
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                        activeBuildPids.Remove(pc.Pid);
                }

                // 兜底清理：短命编译进程的 Stopped 事件可能因进程已退出而丢失，
                // PID 会永远留在集合里导致会话悬挂。每次事件到达时清理已死的 PID。
                if (activeBuildPids.Count > 0)
                {
                    var dead = new List<int>();
                    foreach (int pid in activeBuildPids)
                    {
                        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                        if (h == IntPtr.Zero)
                        {
                            dead.Add(pid);
                            continue;
                        }
                        try
                        {
                            if (!Native.StillActive(h)) dead.Add(pid);
                        }
                        finally { Native.CloseHandle(h); }
                    }
                    foreach (int pid in dead) activeBuildPids.Remove(pid);
                }

                bool nowActive = activeBuildPids.Count > 0;
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

            // 活性变化只向仲裁器报告；副作用由仲裁器经 Grant/Suspend 回调控制
            if (becameActive)
            {
                try { var h = SessionChanged; if (h != null) h("bal.buildstart"); } catch { }
                arbiter.ReportActivity(Kind, true);
            }
            if (becameIdle)
            {
                long elapsedMs = (DateTime.UtcNow.Ticks - sessionStartTicks) / TimeSpan.TicksPerMillisecond;
                Logger.Log(string.Format("开发专注：本次编译 {0:0.#} 秒", elapsedMs / 1000.0));
                try { var h = SessionChanged; if (h != null) h("bal.buildend"); } catch { }
                arbiter.ReportActivity(Kind, false);
            }
        }

        /// <summary>IScenario：获得掌职权——暂停索引服务、提优编译进程（后台压制在 Task 4 加入）</summary>
        public void Grant()
        {
            lock (sync)
            {
                if (granted) return;
                granted = true;
            }
            try
            {
                // TODO: SvcPause 引用计数——当 GameMode (Custom preset + svcPauseOn) 退出时，
                // RestoreEnv() 会在 arbiter 回调 Grant 之后调 SvcPause.Restore()，覆盖此处。
                // 当前仅在 Custom preset 用户手动开启 svcPauseOn 且游戏退出时编译还在跑时触发。
                // 根治需要 SvcPause 引用计数化（Activate 递增/Restore 递减/计数归零才真 Restore），
                // 或 GameMode 成为 IScenario 后 SvcPause 控制权完全归仲裁器。
                SvcPause.Activate();
                BoostBuildProcesses();
                SweepBuildSuppression();
                Logger.Log("开发专注：获得掌职权，已暂停索引服务并提优编译进程");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注掌权失败", ex); }
        }

        /// <summary>IScenario：挂起——还原全部副作用，检测状态保留</summary>
        public void Suspend()
        {
            lock (sync)
            {
                if (!granted) return;
                granted = false;
            }
            try
            {
                if (core != null) core.ReleaseReason(SuppressReason.Build);
                SvcPause.Restore();
                Logger.Log("开发专注：挂起，编译压制已释放、索引服务已恢复（编译检测继续）");
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

            // TODO: 注入 gameMode.IsProcessWhitelisted 委托（DevFocus 构造第 4 参），
            // P2 Task 3 时接线。当前 passing null——白名单豁免暂缺，仅影响编译场景。

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
                        foregroundPid, visible, windowsRoot, null)) continue;

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

        /// <summary>程序退出时调用，确保还原。仅在 ProcNotify 停止后调用（退出路径单线程）</summary>
        public void Stop()
        {
            bool wasReported;
            lock (sync)
            {
                wasReported = reported;
                reported = false;
                activeBuildPids.Clear();
            }
            // 走仲裁器单一路径还原（若正掌权会回调 Suspend）
            if (wasReported) arbiter.ReportActivity(Kind, false);
        }
    }
}
