// @author zenjiro 18967498922@163.com
// 文件用途 开发服务守护：跟踪已注册服务进程，最后一个实例退出且存活超过阈值时通知；
//           可选自动拉起——按实例启动时捕获的原命令行重启，连败熔断

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace CaelusApp
{
    internal sealed class DevServiceGuard
    {
        private sealed class ServiceLaunch
        {
            public string ExePath;
            public string Args;
            public string WorkDir;
        }

        private readonly object sync = new object();
        private readonly Dictionary<int, string> live = new Dictionary<int, string>();
        // PID → 进程创建时间：同 PID 新建实例（创建时间不同）视为旧实例已死，
        // 防 PID 复用后旧条目张冠李戴、counts 永久虚高
        private readonly Dictionary<int, long> creations = new Dictionary<int, long>();
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> firstSeen = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        // 自动拉起记账：按服务名（去 .exe）存最近实例的启动命令与连败预算
        private readonly Dictionary<string, ServiceLaunch> launches = new Dictionary<string, ServiceLaunch>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> consecFails = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool stopped;

        /// <summary>最后一个实例退出且存活超过此阈值才触发（短命子进程不误报）。测试可覆写。</summary>
        internal static long MinAliveTicks = 3L * TimeSpan.TicksPerSecond;

        /// <summary>稳定存活阈值：实例存活超过它，该服务的连败预算重置。测试可覆写。</summary>
        internal static long StableAliveTicks = 60L * TimeSpan.TicksPerSecond;

        /// <summary>自动拉起连败熔断上限。</summary>
        internal const int MaxConsecutiveRestarts = 5;

        /// <summary>某个已注册开发服务的最后一个实例退出时触发，参数为服务名（去 .exe 后缀）。</summary>
        public event Action<string> ServiceStopped;

        /// <summary>自动拉起结果。reason：ok=已拉起 / fail=本次失败将重试 / giveup=连败熔断放弃 / nocmd=未取回启动命令。</summary>
        public event Action<string, string> RestartAttempted;

        /// <summary>自动拉起开关（两宿主注入 Settings 键读取）。null/返回 false 时不拉起。</summary>
        public Func<bool> RestartEnabled;

        public int LiveCount { get { lock (sync) return live.Count; } }

        /// <summary>连败预算判定（纯逻辑，可单测）：连续失败达到上限即熔断。</summary>
        internal static bool BudgetAllows(int consecutiveFails)
        {
            return consecutiveFails < MaxConsecutiveRestarts;
        }

        /// <summary>命令行拆分（纯逻辑，可单测）：首段（支持引号路径）为 exe，其余为参数。</summary>
        internal static void SplitCommandLine(string commandLine, out string exe, out string args)
        {
            exe = "";
            args = "";
            if (string.IsNullOrEmpty(commandLine)) return;
            string s = commandLine.Trim();
            if (s.Length == 0) return;
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = s.IndexOf('"', 1);
                if (end < 0) { exe = s.Substring(1); return; }
                exe = s.Substring(1, end - 1);
                if (end + 1 < s.Length) args = s.Substring(end + 1).TrimStart();
            }
            else
            {
                int sp = s.IndexOf(' ');
                if (sp < 0) exe = s;
                else { exe = s.Substring(0, sp); args = s.Substring(sp + 1).TrimStart(); }
            }
        }

        public void NotifyProcessChanges(ProcessChangeBatch batch)
        {
            if (batch == null || batch.Changes == null) return;
            var toNotify = new List<string>();
            List<KeyValuePair<int, string>> toCapture = null;
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
                        string existingName;
                        bool exists = live.TryGetValue(pc.Pid, out existingName);
                        if (exists)
                        {
                            long recorded;
                            creations.TryGetValue(pc.Pid, out recorded);
                            if (pc.Creation > 0 && recorded > 0 && pc.Creation != recorded)
                            {
                                // PID 已复用：旧实例的 Stopped 事件丢失，先按退出走移除逻辑
                                RemoveLocked(pc.Pid, now, toNotify);
                                exists = false;
                            }
                        }
                        if (!exists)
                        {
                            live[pc.Pid] = bare;
                            if (pc.Creation > 0) creations[pc.Pid] = pc.Creation;
                            else creations.Remove(pc.Pid);
                            int c;
                            counts.TryGetValue(bare, out c);
                            counts[bare] = c + 1;
                            if (c == 0) firstSeen[bare] = now;
                            if (toCapture == null) toCapture = new List<KeyValuePair<int, string>>();
                            toCapture.Add(new KeyValuePair<int, string>(pc.Pid, bare));
                        }
                    }
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                    {
                        RemoveLocked(pc.Pid, now, toNotify);
                    }
                }
            }

            // 命令行捕获在锁外异步执行（WMI 查询不能压住事件线程）
            if (toCapture != null)
                foreach (KeyValuePair<int, string> c in toCapture) CaptureLaunchAsync(c.Key, c.Value);

            if (toNotify.Count > 0)
            {
                var handler = ServiceStopped;
                if (handler != null)
                    foreach (string name in toNotify)
                    {
                        try { handler(name); } catch { }
                    }
                // 自动拉起在锁外线程池执行：TryStart 有 IO，不能压住事件线程
                foreach (string name in toNotify)
                    ThreadPool.QueueUserWorkItem(delegate { MaybeRestart(name); });
            }
        }

        /// <summary>启动补捕：对名录内已在运行的进程补一次命令行捕获
        /// （Caelus 晚于服务启动的场景，等它退出时才有得拉）。宿主在初始扫描后调用。</summary>
        public void CaptureSnapshot()
        {
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
            foreach (Process p in all)
            {
                try
                {
                    string nm;
                    try { nm = p.ProcessName; } catch { continue; }
                    if (!DevServiceCatalog.IsMatch(nm)) continue;
                    CaptureLaunchAsync(p.Id, nm);
                }
                catch { }
                finally { p.Dispose(); }
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

        /// <summary>锁内：移除一个已退出实例，计数归零且存活够久时加入通知。
        /// 存活跨过稳定阈值时重置该服务的连败预算（上一轮实例是被认可的长命实例）。</summary>
        private void RemoveLocked(int pid, long now, List<string> toNotify)
        {
            string existing;
            if (!live.TryGetValue(pid, out existing)) return;
            live.Remove(pid);
            creations.Remove(pid);
            int c;
            counts.TryGetValue(existing, out c);
            if (c <= 1)
            {
                counts.Remove(existing);
                long seen;
                firstSeen.TryGetValue(existing, out seen);
                if (now - seen >= MinAliveTicks)
                    toNotify.Add(existing);
                if (now - seen >= StableAliveTicks)
                    consecFails.Remove(existing);
                firstSeen.Remove(existing);
            }
            else counts[existing] = c - 1;
        }

        /// <summary>自动拉起决策与执行（线程池线程）：预算内且有捕获命令行才拉起；
        /// 无命令行按熔断处理不再重试；Stop() 后不动作。</summary>
        private void MaybeRestart(string name)
        {
            try
            {
                Func<bool> en = RestartEnabled;
                if (en == null || !en()) return;
                ServiceLaunch l;
                string report = null;
                lock (sync)
                {
                    if (stopped) return;
                    if (!launches.TryGetValue(name, out l))
                    {
                        consecFails[name] = MaxConsecutiveRestarts;   // 无命令行：无从拉起，按熔断处理
                        report = "nocmd";
                    }
                }
                if (report != null) { ReportRestart(name, report); return; }

                int fails;
                lock (sync) consecFails.TryGetValue(name, out fails);
                if (!BudgetAllows(fails)) return;   // 已熔断：后续退出静默（giveup 只报一次）

                string exe, args, workDir;
                lock (sync) { exe = l.ExePath; args = l.Args; workDir = l.WorkDir; }
                bool ok = TryStart(exe, args, workDir);
                lock (sync)
                {
                    if (stopped) return;
                    if (ok)
                    {
                        consecFails.Remove(name);
                        report = "ok";
                    }
                    else
                    {
                        int f;
                        consecFails.TryGetValue(name, out f);
                        consecFails[name] = f + 1;
                        report = f + 1 >= MaxConsecutiveRestarts ? "giveup" : "fail";
                    }
                }
                ReportRestart(name, report);
            }
            catch { }
        }

        private void ReportRestart(string name, string reason)
        {
            // fail 静默重试不弹泡，但留日志（连败可查）
            if (reason == "fail") try { Logger.Log("开发服务自动拉起失败（预算内将重试）：" + name); } catch { }
            var h = RestartAttempted;
            if (h != null) try { h(name, reason); } catch { }
        }

        private static bool TryStart(string exe, string args, string workDir)
        {
            try
            {
                if (string.IsNullOrEmpty(exe)) return false;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
                if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
                psi.UseShellExecute = false;
                return Process.Start(psi) != null;
            }
            catch { return false; }
        }

        /// <summary>异步捕获实例启动命令：WMI 查 ExecutablePath/CommandLine，按服务名存最近一次。
        /// 进程退出前没查到就放弃该实例的拉起能力（有后续实例会覆盖）。</summary>
        private void CaptureLaunchAsync(int pid, string bare)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string exe, args, dir;
                    if (!QueryCommandLine(pid, out exe, out args, out dir)) return;
                    ServiceLaunch l = new ServiceLaunch();
                    l.ExePath = exe;
                    l.Args = args;
                    l.WorkDir = dir;
                    lock (sync) launches[bare] = l;
                }
                catch { }
            });
        }

        /// <summary>WMI 查询进程命令行。ExecutablePath 缺失时退回命令行首段拆分。失败返回 false。</summary>
        private static bool QueryCommandLine(int pid, out string exe, out string args, out string dir)
        {
            exe = null;
            args = null;
            dir = null;
            try
            {
                using (System.Management.ManagementObjectSearcher searcher =
                    new System.Management.ManagementObjectSearcher(
                        "SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
                {
                    foreach (System.Management.ManagementBaseObject mo in searcher.Get())
                    {
                        string path = mo["ExecutablePath"] as string;
                        string cl = mo["CommandLine"] as string;
                        string splitExe, splitArgs;
                        SplitCommandLine(cl, out splitExe, out splitArgs);
                        if (string.IsNullOrEmpty(path)) path = splitExe;
                        if (string.IsNullOrEmpty(args)) args = splitArgs;
                        if (!string.IsNullOrEmpty(path))
                        {
                            exe = path;
                            try { dir = System.IO.Path.GetDirectoryName(path); } catch { }
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public void Stop()
        {
            lock (sync)
            {
                stopped = true;
                live.Clear();
                creations.Clear();
                counts.Clear();
                firstSeen.Clear();
            }
        }
    }
}
