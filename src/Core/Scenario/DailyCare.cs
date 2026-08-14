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
                // TODO: Task 3 fills in SweepDailySuppression + BoostVisibleFamily
                StartReconcileTimer();
                MaybeShowBatteryBalloon();
                Logger.Log("日常优化：获得掌职权（家族窗口/电池）");
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
                // TODO: Task 3 fills in RestoreFamilyBoost + ReleaseReason(Daily)
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
                // TODO: Task 3 fills in SweepDailySuppression + BoostVisibleFamily
            }
            catch { }
        }

        // Stub methods for Task 3
        private void SweepDailySuppression() { }
        private void BoostVisibleFamily() { }
        internal void RestoreFamilyBoost() { }
    }
}
