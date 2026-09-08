// @author zenjiro 18967498922@163.com
// 文件用途 统计单局游戏的压制成效并写入运行日志

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CaelusApp
{
    internal partial class GameMode
    {
        private readonly Dictionary<int, long> repCpu = new Dictionary<int, long>();
        private readonly Dictionary<int, long> repCreation = new Dictionary<int, long>();
        private readonly Dictionary<int, string> repProc = new Dictionary<int, string>();
        private readonly Dictionary<int, long> repSealed = new Dictionary<int, long>();
        private long repStart;
        private string repGame;
        private long repCaelusCpuStart;

        public event Action<string> SessionEnded;

        private void ReportBegin(string game)
        {
            GpuThrottleProbe.Reset();
            long caelusCpu = CurrentProcessCpuTicks();
            lock (sync)
            {
                repCpu.Clear();
                repCreation.Clear();
                repProc.Clear();
                repSealed.Clear();
                repGame = game;
                repStart = Stopwatch.GetTimestamp();
                repCaelusCpuStart = caelusCpu;
            }
        }

        private void ReportUntrack(int pid)
        {
            lock (sync)
            {
                repCpu.Remove(pid);
                repCreation.Remove(pid);
                repProc.Remove(pid);
                repSealed.Remove(pid);
            }
        }

        private void ReportSeal(int pid)
        {
            long start, creation;
            lock (sync)
            {
                if (!repCpu.TryGetValue(pid, out start)) return;
                repCreation.TryGetValue(pid, out creation);
                repCpu.Remove(pid);
                repCreation.Remove(pid);
            }
            long now, nowCreation, delta = 0;
            if (CpuTicks(pid, out now, out nowCreation)
                && nowCreation == creation && now > start)
                delta = now - start;
            lock (sync)
            {
                long prev;
                repSealed.TryGetValue(pid, out prev);
                repSealed[pid] = prev + delta;
            }
        }

        private void ReportTrack(int pid, string name)
        {
            lock (sync) { if (repGame == null || repCpu.ContainsKey(pid)) return; }
            long t, creation;
            if (!CpuTicks(pid, out t, out creation)) return;
            lock (sync)
            {
                if (!repCpu.ContainsKey(pid))
                {
                    repCpu[pid] = t; repCreation[pid] = creation; repProc[pid] = name;
                }
            }
        }

        private void ReportFinish()
        {
            Dictionary<int, long> cpu;
            Dictionary<int, string> names;
            Dictionary<int, long> creations;
            Dictionary<int, long> used;
            string game;
            long t0;
            long caelusCpuStart;
            lock (sync)
            {
                game = repGame;
                t0 = repStart;
                cpu = new Dictionary<int, long>(repCpu);
                names = new Dictionary<int, string>(repProc);
                creations = new Dictionary<int, long>(repCreation);
                used = new Dictionary<int, long>(repSealed);
                caelusCpuStart = repCaelusCpuStart;
                repCpu.Clear();
                repCreation.Clear();
                repProc.Clear();
                repSealed.Clear();
                repGame = null;
                repCaelusCpuStart = 0;
            }
            if (game == null) return;

            TimeSpan dur = TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - t0) / Stopwatch.Frequency);
            foreach (var kv in cpu)
            {
                long prev;
                if (!used.TryGetValue(kv.Key, out prev)) { prev = 0; used[kv.Key] = 0; }
                long now, creation;
                if (!CpuTicks(kv.Key, out now, out creation)) continue;
                long expectedCreation;
                if (!creations.TryGetValue(kv.Key, out expectedCreation) || creation != expectedCreation) continue;
                long d = now - kv.Value;
                if (d < 0) continue;
                used[kv.Key] = prev + d;
            }

            long total = 0, top = 0;
            string topName = null;
            foreach (var kv in used)
            {
                total += kv.Value;
                if (kv.Value > top)
                {
                    top = kv.Value;
                    string nm;
                    if (names.TryGetValue(kv.Key, out nm)) topName = nm;
                }
            }

            string core = Lang.F("rep.done", DisplayNameFor(game), FmtDur(dur), used.Count, FmtCpu(total));
            long caelusCpuEnd = CurrentProcessCpuTicks();
            long caelusCpuDelta = caelusCpuStart > 0
                && caelusCpuEnd >= caelusCpuStart
                ? caelusCpuEnd - caelusCpuStart : 0;
            double caelusCpuPercent = AverageCpuPercent(
                caelusCpuDelta, dur);

            // 明细（占用最高的具体进程 / Caelus 自身开销）只进日志；气泡留给
            // 用户关心的结论——此前整段塞进气泡又长又全是内部指标
            string detail = "";
            if (topName != null && top >= TimeSpan.TicksPerSecond)
                detail += Lang.F("rep.top", topName, FmtCpu(top));
            detail += Lang.F(
                "rep.caelus.cpu",
                caelusCpuPercent.ToString("0.00", CultureInfo.InvariantCulture));

            string throttle = GpuThrottleProbe.Summarize();
            string throttleText = throttle != null ? Lang.F("rep.gputhrottle", throttle) : "";
            string protectedText = "";
            // 受保护本体（内核反作弊拒开句柄）开不了 CPU 统计句柄——报告不含本体
            // 占用，数字天然偏低，注明免得用户以为工具没干活
            lock (sync)
            {
                if (activeDetection != null && activeDetection.RendererPid > 0
                    && boostDenied.Contains(activeDetection.RendererPid))
                    protectedText = "；游戏本体受反作弊保护，未计入统计";
            }

            Logger.Log("本局结束：" + core + detail + throttleText + protectedText);

            string msg = core + throttleText + protectedText;
            if (dur.TotalSeconds >= 60)
            {
                var h = SessionEnded;
                if (h != null) { try { h(msg); } catch { } }
            }
        }

        /// <summary>对局报告的游戏显示名：优先用游戏库里的档案名（用户可见的
        /// 中文名），匹配不到再退回检测到的进程名（如带 (TM) Client 后缀的
        /// 内部名）。名字裸传进程名会让弹窗可读性很差。</summary>
        private string DisplayNameFor(string running)
        {
            if (string.IsNullOrEmpty(running)) return running;
            try
            {
                lock (sync)
                {
                    foreach (GameProfile p in profiles)
                    {
                        if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                        if (string.Equals(p.Name, running, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.ExecutablePath, running, StringComparison.OrdinalIgnoreCase)
                            || (p.Entries != null && p.Entries.Contains(running)))
                            return p.Name;
                    }
                }
            }
            catch { }
            return running;
        }

        private static bool CpuTicks(int pid, out long ticks, out long creation)
        {
            ticks = 0; creation = 0;
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                long e, k, u;
                if (!GetProcessTimes(h, out creation, out e, out k, out u)) return false;
                ticks = k + u;
                return true;
            }
            finally { Native.CloseHandle(h); }
        }

        private static long CurrentProcessCpuTicks()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                    return process.TotalProcessorTime.Ticks;
            }
            catch { return 0; }
        }

        internal static double AverageCpuPercent(
            long cpuTicks, TimeSpan duration)
        {
            if (cpuTicks <= 0 || duration.Ticks <= 0) return 0;
            int processors = Math.Max(1, Environment.ProcessorCount);
            double percent = cpuTicks * 100.0
                / (duration.Ticks * (double)processors);
            return Math.Max(0, Math.Min(100, percent));
        }

        private static string FmtDur(TimeSpan t)
        {
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h" + t.Minutes.ToString("00") + "m";
            if (t.TotalMinutes >= 1) return t.Minutes + "m" + t.Seconds.ToString("00") + "s";
            return t.Seconds + "s";
        }

        private static string FmtCpu(long ticks)
        {
            TimeSpan t = TimeSpan.FromTicks(ticks);
            if (t.TotalSeconds < 1) return "<1s";
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "m" + t.Seconds.ToString("00") + "s";
            return t.TotalSeconds.ToString("0.0") + "s";
        }

#if CAELUS_SELFTEST
        internal void ProbeSessionBegin(string game) { ReportBegin(game); }

        internal void ProbeSessionTrack(int pid, string name) { ReportTrack(pid, name); }

        internal void ProbeSessionSeal(int pid) { ReportSeal(pid); }

        internal void ProbeSessionUntrack(int pid) { ReportUntrack(pid); }

        internal void ProbeSessionFinish() { ReportFinish(); }
#endif

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);
    }
}
