// @author zenjiro 18967498922@163.com
// 文件用途 日常优化场景：日常家族活跃时压制后台并提优家族，电池供电自动升档

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace CaelusApp
{
    internal sealed class DailyCare : ScenarioBase
    {
        private readonly SuppressionCore core;
        private readonly Func<bool> enabled;
        private readonly Func<string, string, bool> isWhitelisted;
        private readonly HashSet<int> dailyPids = new HashSet<int>();
        private readonly Dictionary<int, uint> dailyBoosted = new Dictionary<int, uint>();
        private bool familyVisible;
        private bool onBattery;
        private bool batteryBalloonShown;
        private long lastWindowCheckTicks;
        private System.Threading.Timer reconcileTimer;
        private bool grantedFlag;

        public override ScenarioKind Kind { get { return ScenarioKind.DailyCare; } }
        public override int Priority { get { return 10; } }

        public bool IsActive { get { lock (sync) return WantsActiveLocked; } }
        public bool IsGranted { get { lock (sync) return grantedFlag; } }

        /// <summary>场景气球（bal.daily.batt 等文案 key）</summary>
        public event Action<string> SessionChanged;

        protected override bool WantsActiveLocked
        {
            get { return enabled() && (familyVisible || onBattery); }
        }

        public DailyCare(ScenarioArbiter arbiter, SuppressionCore core,
            Func<bool> enabled, Func<string, string, bool> isWhitelisted)
            : base(arbiter)
        {
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            this.isWhitelisted = isWhitelisted;
            RefreshPowerState();
        }

        /// <summary>程序启动与 PowerLineStatusChanged 事件调用</summary>
        public void RefreshPowerState()
        {
            bool batt;
            try { batt = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline; }
            catch { batt = false; }
            bool changed;
            lock (sync)
            {
                changed = onBattery != batt;
                onBattery = batt;
                if (!batt) batteryBalloonShown = false;
            }
            if (changed) RecomputeActivity();
        }

        /// <summary>测试钩子：直接设置电池状态</summary>
        internal void SetBatteryForTest(bool batt)
        {
            lock (sync)
            {
                onBattery = batt;
                if (!batt) batteryBalloonShown = false;
            }
            RecomputeActivity();
        }

        public void NotifyProcessChanges(ProcessChangeBatch batch)
        {
            if (batch == null || batch.Changes == null) return;
            if (!enabled())
            {
                lock (sync) { dailyPids.Clear(); familyVisible = false; }
                ForceReportInactive();
                return;
            }

            lock (sync)
            {
                foreach (ProcessChange pc in batch.Changes)
                {
                    if (string.IsNullOrEmpty(pc.Name)) continue;
                    if (pc.Kind == ProcessChangeKind.Started)
                    {
                        if (IsDailyProcess(pc.Pid, pc.Name, pc.Path))
                            dailyPids.Add(pc.Pid);
                    }
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                    {
                        dailyPids.Remove(pc.Pid);
                    }
                }
                PruneDeadPids(dailyPids);
            }

            RefreshFamilyVisible(false);
            RecomputeActivity();
        }

        /// <summary>节流窗口复查：进程事件驱动，最多 5 秒一次全量枚举</summary>
        private void RefreshFamilyVisible(bool force)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (sync)
            {
                if (!force && now - lastWindowCheckTicks < 5L * TimeSpan.TicksPerSecond) return;
                lastWindowCheckTicks = now;
                if (dailyPids.Count == 0)
                {
                    familyVisible = false;
                    return;
                }
            }
            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { return; }
            lock (sync)
            {
                familyVisible = false;
                foreach (int pid in dailyPids)
                    if (visible.Contains(pid)) { familyVisible = true; break; }
            }
        }

        private bool IsDailyProcess(int pid, string name, string path)
        {
            if (!DailyCatalog.NameMatches(name)) return false;
            string p = path;
            if (string.IsNullOrEmpty(p))
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return false;
                try { p = Native.ImagePath(h); }
                finally { Native.CloseHandle(h); }
            }
            return DailyCatalog.IsMatch(name, p);
        }

        /// <summary>压制级别：市电 Eco，电池 Restrained（纯逻辑可单测）</summary>
        internal static SuppressionLevel ResolveDailyLevel(bool onBattery)
        {
            return onBattery ? SuppressionLevel.Restrained : SuppressionLevel.Eco;
        }

        public override void Grant()
        {
            lock (sync)
            {
                if (grantedFlag) return;
                grantedFlag = true;
            }
            try
            {
                SweepDailySuppression();
                BoostVisibleFamily();
                StartReconcileTimer();
                MaybeShowBatteryBalloon();
                Logger.Log("日常优化：获得掌职权（家族窗口/电池），后台转入常规档压制");
            }
            catch (Exception ex) { Logger.LogFailure("日常优化掌权失败", ex); }
        }

        public override void Suspend()
        {
            lock (sync)
            {
                if (!grantedFlag) return;
                grantedFlag = false;
            }
            try
            {
                StopReconcileTimer();
                RestoreFamilyBoost();
                if (core != null) core.ReleaseReason(SuppressReason.Daily);
                Logger.Log("日常优化：挂起，全部副作用已还原（检测继续）");
            }
            catch (Exception ex) { Logger.LogFailure("日常优化挂起失败", ex); }
        }

        public void Stop()
        {
            lock (sync) { dailyPids.Clear(); familyVisible = false; }
            ForceReportInactive();
        }

        private void MaybeShowBatteryBalloon()
        {
            bool show;
            lock (sync)
            {
                show = onBattery && !batteryBalloonShown;
                if (show) batteryBalloonShown = true;
            }
            if (!show) return;
            try { var h = SessionChanged; if (h != null) h("bal.daily.batt"); } catch { }
            Logger.Log("日常优化：电池供电，后台压制已升档；建议电源模式调至更长续航");
        }

        private void StartReconcileTimer()
        {
            lock (sync)
            {
                if (reconcileTimer != null) return;
                reconcileTimer = new System.Threading.Timer(
                    _ => ReconcileTick(), null, 30000, 30000);
            }
        }

        private void StopReconcileTimer()
        {
            System.Threading.Timer t;
            lock (sync)
            {
                t = reconcileTimer;
                reconcileTimer = null;
            }
            if (t != null) t.Dispose();
        }

        private void ReconcileTick()
        {
            lock (sync) { if (!grantedFlag) return; }
            try
            {
                RefreshFamilyVisible(true);
                RecomputeActivity();
                lock (sync) { if (!grantedFlag) return; }
                SweepDailySuppression();
                BoostVisibleFamily();
                HealthCare.RunIfDue();   // 到点判定内部做，未到期零开销
            }
            catch { }
        }

        private void SweepDailySuppression()
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
            bool batt;
            lock (sync) batt = onBattery;
            SuppressionLevel level = ResolveDailyLevel(batt);

            int suppressed = 0;
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
            foreach (Process p in all)
            {
                try
                {
                    int pid = p.Id;
                    if (pid <= 4 || pid == selfPid) continue;
                    lock (sync) { if (dailyPids.Contains(pid)) continue; }

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

                    if (!DevFocus.ShouldSuppressBackground(pid, selfPid, nm, ipath, session,
                        ownerSession, foregroundPid, visible, windowsRoot, isWhitelisted)) continue;

                    AcquireResult r = core.Acquire(pid, nm, SuppressReason.Daily, "dailycare", level);
                    if (r == AcquireResult.NewlyThrottled) suppressed++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (suppressed > 0)
                Logger.Log("日常优化：压制 " + suppressed + " 个后台进程（"
                    + (batt ? "电池档" : "常规档") + "）");
        }

        private void BoostVisibleFamily()
        {
            int[] family;
            lock (sync)
            {
                family = new int[dailyPids.Count];
                dailyPids.CopyTo(family);
            }
            if (family.Length == 0) return;
            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { return; }
            foreach (int pid in family)
            {
                if (!visible.Contains(pid)) continue;
                BoostOne(pid);
            }
        }

        private void BoostOne(int pid)
        {
            lock (sync) { if (dailyBoosted.ContainsKey(pid)) return; }
            IntPtr h = Native.OpenProcess(
                Native.PROCESS_SET_INFORMATION | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return;
            try
            {
                uint orig = Native.GetPriorityClass(h);
                if (orig == 0) return;
                if (orig == Native.HIGH_PRIORITY_CLASS || orig == 0x100) return;
                if (orig >= Native.ABOVE_NORMAL_PRIORITY_CLASS) return;

                Native.SetPriorityClass(h, Native.ABOVE_NORMAL_PRIORITY_CLASS);
                if (Native.GetPriorityClass(h) != Native.ABOVE_NORMAL_PRIORITY_CLASS) return;
                Native.TrySetIoPriority(h, 3);
                lock (sync) dailyBoosted[pid] = orig;
            }
            catch { }
            finally { Native.CloseHandle(h); }
        }

        internal void RestoreFamilyBoost()
        {
            KeyValuePair<int, uint>[] snap;
            lock (sync)
            {
                if (dailyBoosted.Count == 0) return;
                snap = new KeyValuePair<int, uint>[dailyBoosted.Count];
                ((ICollection<KeyValuePair<int, uint>>)dailyBoosted).CopyTo(snap, 0);
                dailyBoosted.Clear();
            }
            foreach (var kv in snap)
            {
                try
                {
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
    }
}
