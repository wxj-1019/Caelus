// @author zenjiro 18967498922@163.com
// 文件用途 场景基类：仲裁器对接、活性重算、死 PID 清理（DevFocus/DailyCare 共用）

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal abstract class ScenarioBase : IScenario
    {
        protected readonly object sync = new object();
        protected readonly ScenarioArbiter arbiter;
        protected bool reported;

        protected ScenarioBase(ScenarioArbiter arbiter)
        {
            if (arbiter == null) throw new ArgumentNullException("arbiter");
            this.arbiter = arbiter;
            arbiter.Register(this);
        }

        public abstract ScenarioKind Kind { get; }
        public abstract int Priority { get; }
        public abstract void Grant();
        public abstract void Suspend();

        /// <summary>锁内判定：场景此刻是否想活跃（含自身开关检查）。子类实现。</summary>
        protected abstract bool WantsActiveLocked { get; }

        /// <summary>已向仲裁器报告的活性状态</summary>
        protected bool Reported { get { lock (sync) return reported; } }

        /// <summary>统一活性重算：任一活性来源变化后调用。锁内记账，锁外向仲裁器报告翻转。</summary>
        protected void RecomputeActivity()
        {
            bool becameActive = false;
            bool becameIdle = false;
            lock (sync)
            {
                bool nowActive = WantsActiveLocked;
                if (nowActive && !reported)
                {
                    reported = true;
                    becameActive = true;
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

        /// <summary>强制撤销活性报告（开关关闭/退出路径用）：若已报告则向仲裁器报告不活跃</summary>
        protected void ForceReportInactive()
        {
            bool wasReported;
            lock (sync)
            {
                wasReported = reported;
                reported = false;
            }
            if (wasReported) arbiter.ReportActivity(Kind, false);
        }

        /// <summary>在已打开的句柄上取 FILETIME 创建时间与映像名。创建时间必须用
        /// QueryProcessSample 的 FILETIME 纪元——CrashGuard 自愈的 Identify 用它比对，
        /// 存成 DateTime.Ticks 会差一个固定常数导致崩溃后永远匹配不上。</summary>
        protected static void QueryBoostIdentity(IntPtr h, out long creation, out string name)
        {
            long cpu;
            ulong io;
            Native.QueryProcessSample(h, out creation, out cpu, out io);
            name = Native.ImageName(h);
        }

        /// <summary>还原前的身份校验：创建时间（FILETIME）优先比对，缺失时退回映像名，
        /// 防 PID 复用把原值还原到无关的新进程。</summary>
        protected static bool SameBoostedIdentity(IntPtr h, long creation, string name)
        {
            if (creation > 0)
            {
                long c, cpu;
                ulong io;
                if (!Native.QueryProcessSample(h, out c, out cpu, out io)) return false;
                return c == creation;
            }
            if (!string.IsNullOrEmpty(name))
                return string.Equals(Native.ImageName(h), name, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        /// <summary>提优单个进程到目标优先级 + IO 3，带原值快照与崩溃自愈凭据。
        /// 幂等：已提优的 PID 直接跳过。掌权检查（stillGranted，在场景锁内求值）与
        /// 快照登记在同一把锁内——挂起插在提优中途时当场按快照回滚，不产生泄漏。
        /// 创建时间用 FILETIME 纪元采集（与 CrashGuard 自愈校验一致）。</summary>
        protected void BoostOneWithSnapshot(Func<bool> stillGranted, int pid, uint targetPriority,
            Dictionary<int, uint> boosted, Dictionary<int, long> creations,
            Dictionary<int, string> names, Dictionary<int, int> ios)
        {
            lock (sync) { if (boosted.ContainsKey(pid)) return; }

            IntPtr h = Native.OpenProcess(
                Native.PROCESS_SET_INFORMATION | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return;
            try
            {
                long creation;
                string name;
                QueryBoostIdentity(h, out creation, out name);

                uint orig = Native.GetPriorityClass(h);
                if (orig == 0) return;
                if (orig == Native.HIGH_PRIORITY_CLASS || orig == 0x100) return;
                if (orig >= Native.ABOVE_NORMAL_PRIORITY_CLASS) return;

                int origIo = Native.QueryIoPriority(h);
                Native.SetPriorityClass(h, targetPriority);
                if (Native.GetPriorityClass(h) != targetPriority) return;
                // IO 优先级写入需要 SeIncreaseBasePriorityPrivilege（与 GameMode 提优同要求）
                try { Native.EnsureBoostPrivilege(); } catch { }
                Native.TrySetIoPriority(h, 3);

                bool registered;
                lock (sync)
                {
                    registered = stillGranted() && !boosted.ContainsKey(pid);
                    if (registered)
                    {
                        boosted[pid] = orig;
                        creations[pid] = creation;
                        names[pid] = name;
                        ios[pid] = origIo;
                    }
                }
                if (!registered)
                {
                    // 挂起在提优途中到达：立即按快照还原，不留泄漏
                    Native.SetPriorityClass(h, orig);
                    Native.TrySetIoPriority(h, origIo >= 0 ? origIo : 2);
                    return;
                }
                // 崩溃自愈：快照持久化到 CrashGuard 日志，Caelus 崩溃后下次启动自动还原
                try
                {
                    if (creation > 0 && !string.IsNullOrEmpty(name))
                        CrashGuard.MarkBoostProcess(pid, creation, name, orig, 0, origIo, 0, 0, null);
                }
                catch { }
            }
            catch { }
            finally { Native.CloseHandle(h); }
        }

        /// <summary>按快照还原全部提优（优先级 + IO 原值）。身份凭据：创建时间
        /// （FILETIME）优先、映像名兜底，防 PID 复用把原值还原到无关的新进程。</summary>
        protected void RestoreBoostSnapshot(
            Dictionary<int, uint> boosted, Dictionary<int, long> creations,
            Dictionary<int, string> names, Dictionary<int, int> ios)
        {
            KeyValuePair<int, uint>[] snap;
            KeyValuePair<int, long>[] snapCreation;
            KeyValuePair<int, string>[] snapName;
            KeyValuePair<int, int>[] snapIo;
            lock (sync)
            {
                if (boosted.Count == 0) return;
                snap = new KeyValuePair<int, uint>[boosted.Count];
                ((ICollection<KeyValuePair<int, uint>>)boosted).CopyTo(snap, 0);
                boosted.Clear();
                snapCreation = new KeyValuePair<int, long>[creations.Count];
                ((ICollection<KeyValuePair<int, long>>)creations).CopyTo(snapCreation, 0);
                creations.Clear();
                snapName = new KeyValuePair<int, string>[names.Count];
                ((ICollection<KeyValuePair<int, string>>)names).CopyTo(snapName, 0);
                names.Clear();
                snapIo = new KeyValuePair<int, int>[ios.Count];
                ((ICollection<KeyValuePair<int, int>>)ios).CopyTo(snapIo, 0);
                ios.Clear();
            }
            var creationMap = new Dictionary<int, long>();
            foreach (var kv in snapCreation) creationMap[kv.Key] = kv.Value;
            var nameMap = new Dictionary<int, string>();
            foreach (var kv in snapName) nameMap[kv.Key] = kv.Value;
            var ioMap = new Dictionary<int, int>();
            foreach (var kv in snapIo) ioMap[kv.Key] = kv.Value;

            foreach (var kv in snap)
            {
                try
                {
                    long creation;
                    creationMap.TryGetValue(kv.Key, out creation);
                    string name;
                    nameMap.TryGetValue(kv.Key, out name);
                    int ioSnapshot;
                    int origIo = ioMap.TryGetValue(kv.Key, out ioSnapshot) && ioSnapshot >= 0
                        ? ioSnapshot : 2;

                    IntPtr h = Native.OpenProcess(
                        Native.PROCESS_SET_INFORMATION | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, kv.Key);
                    if (h == IntPtr.Zero) continue;
                    bool restored = false;
                    try
                    {
                        if (!SameBoostedIdentity(h, creation, name)) continue;
                        Native.SetPriorityClass(h, kv.Value);
                        Native.TrySetIoPriority(h, origIo);
                        restored = true;
                    }
                    finally { Native.CloseHandle(h); }
                    // 清除崩溃自愈快照（已正常还原）
                    if (restored && creation > 0)
                        CrashGuard.ReleaseBoostProcess(kv.Key, creation);
                }
                catch { }
            }
        }

        /// <summary>死 PID 兜底清理：短命进程的 Stopped 事件可能丢失，每次事件到达时清理</summary>
        protected static void PruneDeadPids(HashSet<int> pids)
        {
            if (pids.Count == 0) return;
            var dead = new List<int>();
            foreach (int pid in pids)
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) { dead.Add(pid); continue; }
                try
                {
                    if (!Native.StillActive(h)) dead.Add(pid);
                }
                finally { Native.CloseHandle(h); }
            }
            foreach (int pid in dead) pids.Remove(pid);
        }

        /// <summary>初始全量扫描：枚举当前运行中的进程，匹配的场景进程当作 Started 事件处理。
        /// 解决「启动前已运行的进程（如已开的 VS Code）不被检测」的问题。</summary>
        public void InitialScan()
        {
            try
            {
                var all = System.Diagnostics.Process.GetProcesses();
                foreach (var p in all)
                {
                    try
                    {
                        string name = p.ProcessName;
                        string path = null;
                        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                        if (h != IntPtr.Zero)
                        {
                            try { path = Native.ImagePath(h); }
                            finally { Native.CloseHandle(h); }
                        }
                        var change = new ProcessChange
                        {
                            Pid = p.Id,
                            Name = name,
                            Path = path,
                            Kind = ProcessChangeKind.Started
                        };
                        OnInitialProcess(change);
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                OnInitialScanComplete();
                RecomputeActivity();
            }
            catch (Exception ex) { Logger.LogFailure("初始全量扫描失败", ex); }
        }

        /// <summary>子类实现：处理初始扫描发现的进程（与进程事件的逻辑一致）。</summary>
        protected abstract void OnInitialProcess(ProcessChange change);

        /// <summary>初始扫描完成钩子：子类可在此补算派生状态（如可见窗口），默认空实现。</summary>
        protected virtual void OnInitialScanComplete() { }
    }
}
