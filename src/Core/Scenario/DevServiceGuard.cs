// @author zenjiro 18967498922@163.com
// 文件用途 开发服务守护：跟踪已注册服务进程，最后一个实例退出且存活超过阈值时通知

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class DevServiceGuard
    {
        private readonly object sync = new object();
        private readonly Dictionary<int, string> live = new Dictionary<int, string>();
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> firstSeen = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>最后一个实例退出且存活超过此阈值才触发（短命子进程不误报）。测试可覆写。</summary>
        internal static long MinAliveTicks = 3L * TimeSpan.TicksPerSecond;

        /// <summary>某个已注册开发服务的最后一个实例退出时触发，参数为服务名（去 .exe 后缀）。</summary>
        public event Action<string> ServiceStopped;

        public int LiveCount { get { lock (sync) return live.Count; } }

        public void NotifyProcessChanges(ProcessChangeBatch batch)
        {
            if (batch == null || batch.Changes == null) return;
            var toNotify = new List<string>();
            lock (sync)
            {
                long now = DateTime.UtcNow.Ticks;

                // 死 PID 兜底清理：短命进程的 Stopped 事件可能丢失，否则计数会永久虚高、
                // 最后一个实例退出也永远不触发。清理同样走 RemoveLocked（会触发通知）。
                PruneDeadLocked(now, toNotify);

                foreach (ProcessChange pc in batch.Changes)
                {
                    if (string.IsNullOrEmpty(pc.Name) || !DevServiceCatalog.IsMatch(pc.Name)) continue;
                    string bare = pc.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? pc.Name.Substring(0, pc.Name.Length - 4) : pc.Name;

                    if (pc.Kind == ProcessChangeKind.Started)
                    {
                        if (!live.ContainsKey(pc.Pid))
                        {
                            live[pc.Pid] = bare;
                            int c;
                            counts.TryGetValue(bare, out c);
                            counts[bare] = c + 1;
                            if (c == 0) firstSeen[bare] = now;
                        }
                    }
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                    {
                        RemoveLocked(pc.Pid, now, toNotify);
                    }
                }
            }

            if (toNotify.Count > 0)
            {
                var handler = ServiceStopped;
                if (handler != null)
                    foreach (string name in toNotify)
                    {
                        try { handler(name); } catch { }
                    }
            }
        }

        /// <summary>锁内：清理已死亡的跟踪进程（进程不存在或已退出）。</summary>
        private void PruneDeadLocked(long now, List<string> toNotify)
        {
            if (live.Count == 0) return;
            var dead = new List<int>();
            foreach (KeyValuePair<int, string> kv in live)
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, kv.Key);
                if (h == IntPtr.Zero) { dead.Add(kv.Key); continue; }
                try { if (!Native.StillActive(h)) dead.Add(kv.Key); }
                finally { Native.CloseHandle(h); }
            }
            foreach (int pid in dead) RemoveLocked(pid, now, toNotify);
        }

        /// <summary>锁内：移除一个已退出实例，计数归零且存活够久时加入通知。</summary>
        private void RemoveLocked(int pid, long now, List<string> toNotify)
        {
            string existing;
            if (!live.TryGetValue(pid, out existing)) return;
            live.Remove(pid);
            int c;
            counts.TryGetValue(existing, out c);
            if (c <= 1)
            {
                counts.Remove(existing);
                long seen;
                firstSeen.TryGetValue(existing, out seen);
                if (now - seen >= MinAliveTicks)
                    toNotify.Add(existing);
                firstSeen.Remove(existing);
            }
            else counts[existing] = c - 1;
        }

        public void Stop()
        {
            lock (sync) { live.Clear(); counts.Clear(); firstSeen.Clear(); }
        }
    }
}
