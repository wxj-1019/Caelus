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
                            if (c == 0) firstSeen[bare] = DateTime.UtcNow.Ticks;
                        }
                    }
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                    {
                        string existing;
                        if (live.TryGetValue(pc.Pid, out existing))
                        {
                            live.Remove(pc.Pid);
                            int c;
                            counts.TryGetValue(existing, out c);
                            if (c <= 1)
                            {
                                counts.Remove(existing);
                                long seen;
                                firstSeen.TryGetValue(existing, out seen);
                                if (DateTime.UtcNow.Ticks - seen >= MinAliveTicks)
                                    toNotify.Add(existing);
                                firstSeen.Remove(existing);
                            }
                            else counts[existing] = c - 1;
                        }
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

        public void Stop()
        {
            lock (sync) { live.Clear(); counts.Clear(); firstSeen.Clear(); }
        }
    }
}
