// @author zenjiro 18967498922@163.com
// 文件用途 识别游戏的帧关键线程并单独抬高其调度权重 一次识别 全程不再轮询

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CaelusApp
{
    internal static class RenderLane
    {
        internal const double MinDominantShare = 0.35;
        private const int SampleGapMs = 800;
        private const int MaxThreads = 512;

        private static readonly object sync = new object();
        private static int lanePid;
        private static long laneCreation;
        private static int laneTid;
        private static long laneThreadCreation;
        private static int laneOriginalPriority;
        private static bool laneApplied;
        private static int triedPid;
        private static long triedCreation;
        // 预热期（菜单/加载阶段）负载分散、对局开始后才出现主导线程是常态：
        // Share 不足类软失败按有限退避重试，硬失败（采样不到/句柄被拒）保持单次
        private static int softTriesLeft;
        private static long softNextTryTicks;
        internal const int SoftRetryMax = 3;
        internal const long SoftRetryGapTicks = 30L * TimeSpan.TicksPerSecond;

        internal struct Candidate
        {
            public int Tid;
            public double Share;
            public int ThreadCount;
        }

        internal static bool TryIdentify(int pid, out Candidate best)
        {
            best = new Candidate();
            var first = new Dictionary<int, long>();
            if (!SampleThreads(pid, first)) return false;
            System.Threading.Thread.Sleep(SampleGapMs);
            var second = new Dictionary<int, long>();
            if (!SampleThreads(pid, second)) return false;

            long total = 0, bestDelta = -1;
            int bestTid = 0;
            foreach (KeyValuePair<int, long> kv in second)
            {
                long before;
                if (!first.TryGetValue(kv.Key, out before)) continue;
                long delta = kv.Value - before;
                if (delta <= 0) continue;
                total += delta;
                if (delta > bestDelta) { bestDelta = delta; bestTid = kv.Key; }
            }
            if (bestTid == 0 || total <= 0) return false;
            best.Tid = bestTid;
            best.Share = bestDelta / (double)total;
            best.ThreadCount = second.Count;
            return true;
        }

        private static bool SampleThreads(int pid, Dictionary<int, long> into)
        {
            try
            {
                using (Process target = Process.GetProcessById(pid))
                {
                    ProcessThreadCollection threads = target.Threads;
                    int seen = 0;
                    foreach (ProcessThread t in threads)
                    {
                        if (++seen > MaxThreads) break;
                        IntPtr h = Native.OpenThread(Native.THREAD_QUERY_LIMITED_INFORMATION, false, t.Id);
                        if (h == IntPtr.Zero) continue;
                        try
                        {
                            long c, e, k, u;
                            if (Native.GetThreadTimes(h, out c, out e, out k, out u)) into[t.Id] = k + u;
                        }
                        finally { Native.CloseHandle(h); }
                    }
                    return into.Count > 0;
                }
            }
            catch { return false; }
        }

        public static bool IsActiveFor(int pid, long creation)
        {
            lock (sync) return laneApplied && lanePid == pid && laneCreation == creation;
        }

        public static void EnsureForGame(int pid, long creation, string gameName)
        {
            lock (sync)
            {
                if (laneApplied && lanePid == pid && laneCreation == creation) return;
                if (triedPid == pid && triedCreation == creation)
                {
                    long now = DateTime.UtcNow.Ticks;
                    if (softTriesLeft <= 0 || now < softNextTryTicks) return;
                    softTriesLeft--;
                    softNextTryTicks = now + SoftRetryGapTicks;
                }
                else
                {
                    triedPid = pid; triedCreation = creation;
                    softTriesLeft = SoftRetryMax;   // 首试之后还允许 SoftRetryMax 次重试
                    softNextTryTicks = DateTime.UtcNow.Ticks + SoftRetryGapTicks;
                }
            }
            Candidate best;
            if (!TryIdentify(pid, out best))
            {
                lock (sync) softTriesLeft = 0;   // 采样不到=硬失败，保持单次
                Logger.Log("渲染主权域：无法采样 " + (gameName ?? "?") + " (pid " + pid
                    + ") 的线程（进程可能刚退出或是启动器壳），该进程不再尝试；若真身另有进程会继续探测");
                return;
            }
            if (best.Share < MinDominantShare)
            {
                int left;
                lock (sync) left = softTriesLeft;
                if (left > 0)
                    Logger.Log("渲染主权域：" + (gameName ?? "?") + " 负载尚分散（主导仅 "
                        + (best.Share * 100).ToString("F0") + "%，共 " + best.ThreadCount
                        + " 线程），可能仍在预热，30 秒后重试（剩 " + left + " 次）");
                else
                    Logger.Log("渲染主权域：" + (gameName ?? "?") + " 的负载分散在多线程（主导仅 "
                        + (best.Share * 100).ToString("F0") + "%，共 " + best.ThreadCount
                        + " 线程），重试 " + SoftRetryMax + " 次后仍无单一帧关键路径，本局不介入");
                return;
            }
            IntPtr h = Native.OpenThread(
                Native.THREAD_SET_LIMITED_INFORMATION | Native.THREAD_QUERY_LIMITED_INFORMATION,
                false, best.Tid);
            if (h == IntPtr.Zero)
            {
                Logger.Log("渲染主权域：线程写句柄被拒（多半被反作弊保护），本局跳过");
                return;
            }
            try
            {
                int original = Native.GetThreadPriority(h);
                if (original == Native.THREAD_PRIORITY_ERROR_RETURN)
                {
                    Logger.Log("渲染主权域：读不到线程原优先级，未做任何写入");
                    return;
                }
                if (original >= Native.THREAD_PRIORITY_ABOVE_NORMAL)
                {
                    lock (sync) softTriesLeft = 0;
                    Logger.Log("渲染主权域：" + (gameName ?? "?") + " 的帧关键线程已自带高于常规的权重，无需介入");
                    return;
                }
                // 记录线程创建时间：长期存活的游戏里 TID 会被线程池轮转复用，
                // 还原时比对创建时间，防止把原优先级写到复用 TID 的无关线程上
                long threadCreation, tExit, tKernel, tUser;
                if (!Native.GetThreadTimes(h, out threadCreation, out tExit, out tKernel, out tUser))
                    threadCreation = 0;
                if (!SaveJournal(pid, creation, best.Tid, original, threadCreation))
                {
                    Logger.Log("渲染主权域：记账无法持久化，已放弃写入");
                    return;
                }
                if (!Native.SetThreadPriority(h, Native.THREAD_PRIORITY_ABOVE_NORMAL))
                {
                    ClearJournal();
                    Logger.Log("渲染主权域：写入线程优先级失败，已清账");
                    return;
                }
                int actual = Native.GetThreadPriority(h);
                if (actual != Native.THREAD_PRIORITY_ABOVE_NORMAL)
                {
                    Native.SetThreadPriority(h, original);
                    ClearJournal();
                    Logger.Log("渲染主权域：回读不符（" + actual + "），已还原");
                    return;
                }
                lock (sync)
                {
                    lanePid = pid; laneCreation = creation;
                    laneTid = best.Tid; laneThreadCreation = threadCreation;
                    laneOriginalPriority = original; laneApplied = true;
                }
                Logger.Log("渲染主权域已建立：" + (gameName ?? "?") + " 帧关键线程 " + best.Tid
                    + "（占进程 CPU " + (best.Share * 100).ToString("F0") + "%，共 " + best.ThreadCount
                    + " 线程）优先级 " + original + " → " + Native.THREAD_PRIORITY_ABOVE_NORMAL);
            }
            finally { Native.CloseHandle(h); }
        }

        public static bool Release()
        {
            int pid, tid, original;
            long creation, threadCreation;
            lock (sync)
            {
                if (!laneApplied) { ClearJournal(); return true; }
                pid = lanePid; creation = laneCreation; tid = laneTid; original = laneOriginalPriority;
                threadCreation = laneThreadCreation;
            }
            bool ok = RestoreThread(pid, creation, tid, original, threadCreation);
            if (ok)
            {
                lock (sync)
                {
                    laneApplied = false; lanePid = 0; laneCreation = 0; laneTid = 0;
                    laneThreadCreation = 0; triedPid = 0; triedCreation = 0;
                }
                ClearJournal();
                Logger.Log("渲染主权域已撤销：线程 " + tid + " 优先级还原为 " + original);
            }
            else Logger.Log("渲染主权域：线程 " + tid + " 还原失败，记账保留待下次重试");
            return ok;
        }

        public static void HealFromCrash()
        {
            string raw = Settings.LoadStr("RenderLane", "");
            int pid, tid, original;
            long creation, threadCreation;
            if (!ParseJournal(raw, out pid, out creation, out tid, out original, out threadCreation))
            { ClearJournal(); return; }
            if (RestoreThread(pid, creation, tid, original, threadCreation))
            {
                ClearJournal();
                Logger.Log("渲染主权域：崩溃前的线程 " + tid + " 优先级已还原为 " + original);
            }
        }

        /// <summary>返回 true 表示"无需还原/已还原"（可清账），false 表示暂不可还原（保留记账重试）。
        /// 身份校验：进程级用与记账一致的 FILETIME（QueryProcessSample）——此前与 DateTime.Ticks
        /// 比较永不相等，还原沦为空操作却报告成功并清账；线程级比对创建时间防 TID 被线程池
        /// 复用后误写。线程句柄打不开时区分"已不存在"（视为完成）与"被拒绝"（保留待重试）。</summary>
        private static bool RestoreThread(int pid, long creation, int tid, int original, long threadCreation)
        {
            if (creation > 0)
            {
                IntPtr hq = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hq == IntPtr.Zero)
                    return Native.LastOpenProcessFailureWasNoSuchProcess();
                long c, cpu;
                ulong io;
                bool sampled;
                try { sampled = Native.QueryProcessSample(hq, out c, out cpu, out io); }
                finally { Native.CloseHandle(hq); }
                if (sampled && c != creation) return true;   // PID 已被复用：原线程随旧进程消失
            }
            IntPtr h = Native.OpenThread(
                Native.THREAD_SET_LIMITED_INFORMATION | Native.THREAD_QUERY_LIMITED_INFORMATION, false, tid);
            if (h == IntPtr.Zero)
                return Native.LastOpenThreadFailureWasNoSuchProcess();
            try
            {
                if (threadCreation > 0)
                {
                    long tc, te, tk, tu;
                    if (Native.GetThreadTimes(h, out tc, out te, out tk, out tu) && tc != threadCreation)
                    {
                        Logger.Log("渲染主权域：线程 " + tid + " 已被新线程复用（创建时间不符），跳过还原");
                        return true;
                    }
                }
                if (!Native.SetThreadPriority(h, original)) return false;
                int actual = Native.GetThreadPriority(h);
                return actual == original || actual == Native.THREAD_PRIORITY_ERROR_RETURN;
            }
            finally { Native.CloseHandle(h); }
        }

        private static bool SaveJournal(int pid, long creation, int tid, int original, long threadCreation)
        {
            string line = pid + "|" + creation + "|" + tid + "|" + original + "|" + threadCreation;
            return Settings.SaveStr("RenderLane", line) && Settings.LoadStr("RenderLane", "") == line;
        }

        private static void ClearJournal() { Settings.SaveStr("RenderLane", ""); }

        /// <summary>解析记账。第 5 段（线程创建时间）为新增，兼容旧 4 段格式（无线程身份校验）。</summary>
        internal static bool ParseJournal(string raw, out int pid, out long creation,
            out int tid, out int original, out long threadCreation)
        {
            pid = 0; creation = 0; tid = 0; original = 0; threadCreation = 0;
            if (string.IsNullOrEmpty(raw)) return false;
            string[] parts = raw.Split('|');
            if (parts.Length != 4 && parts.Length != 5) return false;
            if (!int.TryParse(parts[0], out pid) || pid <= 0) return false;
            if (!long.TryParse(parts[1], out creation)) return false;
            if (!int.TryParse(parts[2], out tid) || tid <= 0) return false;
            if (!int.TryParse(parts[3], out original)) return false;
            if (parts.Length == 5 && !long.TryParse(parts[4], out threadCreation)) return false;
            return true;
        }
    }
}
