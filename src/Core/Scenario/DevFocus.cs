// @author zenjiro 18967498922@163.com
// 文件用途 开发专注场景：检测编译/调试进程，掌权时暂停索引、提优编译器并压制后台

using System;
using System.Collections.Generic;

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
                if (elapsedMs >= 0)
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
                SvcPause.Activate();
                BoostBuildProcesses();
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
                SvcPause.Restore();
                Logger.Log("开发专注：挂起，索引服务已恢复（编译检测继续）");
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
