// @author zenjiro 18967498922@163.com
// 文件用途 检测编译/调试进程的启动与退出，自动压制后台并暂停索引服务

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CaelusApp
{
    internal sealed class BuildWatch
    {
        private readonly SuppressionCore core;
        private readonly object sync = new object();
        private readonly HashSet<int> activeBuildPids = new HashSet<int>();
        private bool suppressing;
        private long sessionStartTicks;

        public bool IsActive { get { lock (sync) return activeBuildPids.Count > 0; } }

        /// <summary>编译会话状态变化时触发，参数是文案 key（bal.buildstart / bal.buildend）</summary>
        public event Action<string> SessionChanged;

        public BuildWatch(SuppressionCore core)
        {
            this.core = core;
        }

        public void NotifyProcessChanges(ProcessChangeBatch batch)
        {
            if (batch == null || batch.Changes == null) return;
            if (!Settings.Load("DevModeOn", true)) return;

            bool becameActive = false;
            bool becameIdle = false;
            int activeCount = 0;

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
                // PID 会永远留在集合里导致会话不结束。每次事件到达时清理已死的 PID。
                if (activeBuildPids.Count > 0)
                {
                    var dead = new List<int>();
                    foreach (int pid in activeBuildPids)
                    {
                        try
                        {
                            using (var p = Process.GetProcessById(pid)) { }
                        }
                        catch (ArgumentException) { dead.Add(pid); }
                        catch (InvalidOperationException) { dead.Add(pid); }
                    }
                    foreach (int pid in dead) activeBuildPids.Remove(pid);
                }

                activeCount = activeBuildPids.Count;

                if (activeCount > 0 && !suppressing)
                {
                    suppressing = true;
                    becameActive = true;
                }
                else if (activeCount == 0 && suppressing)
                {
                    suppressing = false;
                    becameIdle = true;
                }
            }

            // 锁外执行副作用（耗时操作不持锁）
            if (becameActive) ActivateSuppression(activeCount);
            if (becameIdle) DeactivateSuppression();
        }

        private void ActivateSuppression(int activeCount)
        {
            try
            {
                sessionStartTicks = DateTime.UtcNow.Ticks;
                SvcPause.Activate();
                BoostBuildProcesses();
                Logger.Log("开发模式：检测到编译/调试进程（" + activeCount + " 个活跃），已暂停索引服务并提优编译进程");
                try { SessionChanged("bal.buildstart"); } catch { }
            }
            catch (Exception ex) { Logger.LogFailure("开发模式激活失败", ex); }
        }

        private void DeactivateSuppression()
        {
            try
            {
                SvcPause.Restore();
                long elapsedMs = (DateTime.UtcNow.Ticks - sessionStartTicks) / TimeSpan.TicksPerMillisecond;
                if (elapsedMs >= 0)
                    Logger.Log(string.Format("开发模式：本次编译 {0:0.#} 秒，索引服务已恢复",
                        elapsedMs / 1000.0));
                Logger.Log("开发模式：编译/调试进程已退出，恢复后台资源");
                try { SessionChanged("bal.buildend"); } catch { }
            }
            catch (Exception ex) { Logger.LogFailure("开发模式恢复失败", ex); }
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

        /// <summary>程序退出时调用，确保恢复</summary>
        public void Stop()
        {
            lock (sync)
            {
                if (!suppressing) return;
                suppressing = false;
                activeBuildPids.Clear();
            }
            DeactivateSuppression();
        }
    }
}
