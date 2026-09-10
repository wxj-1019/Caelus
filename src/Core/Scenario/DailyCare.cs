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
        private readonly Dictionary<int, long> dailyBoostedCreation = new Dictionary<int, long>();
        private readonly Dictionary<int, string> dailyBoostedName = new Dictionary<int, string>();
        private readonly Dictionary<int, int> dailyBoostedIo = new Dictionary<int, int>();
        private bool familyVisible;
        private bool familyVisibleTestPin;   // 测试钩子：钉住家族可见性，窗口复查不改写
        private bool onBattery;
        private bool batteryBalloonShown;
        private long lastWindowCheckTicks;
        private System.Threading.Timer reconcileTimer;
        private bool grantedFlag;

        /// <summary>测试挂钩：隔离真实注册表（生产为 null 走 PowerOverlay 真实实现）</summary>
        internal static Func<bool> BatterySaverApplyHook;
        internal static Func<bool> BatterySaverRestoreHook;

        private static bool SaverApply()
        {
            if (BatterySaverApplyHook != null) return BatterySaverApplyHook();
            try { return PowerOverlay.ActivateDcSaver(PowerOverlay.OwnerDailyCare); } catch { return false; }
        }

        private static bool SaverRestore()
        {
            if (BatterySaverRestoreHook != null) return BatterySaverRestoreHook();
            try { return PowerOverlay.RestoreDcSaver(PowerOverlay.OwnerDailyCare); } catch { return false; }
        }

        /// <summary>掌权且电池供电且开关开 → 续航档在位</summary>
        private void ApplyBatterySaverIfNeeded()
        {
            bool batt;
            bool granted;
            lock (sync) { batt = onBattery; granted = grantedFlag; }
            if (!granted || !batt || !BatteryOn) return;
            SaverApply();
        }

        public override ScenarioKind Kind { get { return ScenarioKind.DailyCare; } }
        public override int Priority { get { return 10; } }

        public bool IsActive { get { lock (sync) return WantsActiveLocked; } }
        public bool IsGranted { get { lock (sync) return grantedFlag; } }

        /// <summary>场景气球（bal.daily.batt 等文案 key）</summary>
        public event Action<string> SessionChanged;

        /// <summary>电池供电优化开关（默认开）。关闭后电池不再作为活性来源，也不升档。</summary>
        public bool BatteryOn { get { return Settings.Load("DailyCareBatteryOn", true); } }

        protected override bool WantsActiveLocked
        {
            get { return enabled() && (familyVisible || (BatteryOn && onBattery)); }
        }

        /// <summary>电池优化开关（设置页调用）。写注册表 + 活性重算。</summary>
        public void SetBatteryOn(bool on)
        {
            Settings.Save("DailyCareBatteryOn", on);
            RecomputeActivity();
        }

        /// <summary>场景总开关（设置页/场景总览调用）。关闭时立即退出仲裁器活性集合并还原副作用，
        /// 避免「被游戏抢占后关闭开关、游戏退出时场景又恢复掌权」的残留。
        /// 提优快照由 Suspend→RestoreFamilyBoost 在 ForceReportInactive 内同步还原，此处不提前清空。</summary>
        public void SetEnabled(bool on)
        {
            Settings.Save("DailyCareOn", on);
            if (!on)
            {
                lock (sync)
                {
                    dailyPids.Clear();
                    familyVisible = false;
                }
                ForceReportInactive();
            }
            else
            {
                RecomputeActivity();
            }
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
            RefreshPowerStateCore(batt);
        }

        /// <summary>电源状态换档核心：电池源可注入，插拔电时序可单测</summary>
        internal void RefreshPowerStateCore(bool batt)
        {
            bool changed;
            bool wasGranted;
            lock (sync)
            {
                changed = onBattery != batt;
                onBattery = batt;
                if (!batt) batteryBalloonShown = false;
                wasGranted = grantedFlag;
            }
            if (!changed) return;
            RecomputeActivity();
            if (wasGranted)
            {
                if (batt) ApplyBatterySaverIfNeeded();
                else SaverRestore();
            }
            // 掌权期间插拔电立即重扫换档（Eco↔Restrained），不再等最长 30 秒的校正节拍；
            // 线程池执行——WinForms 宿主的电源轮询在 UI 线程上，全量扫描不能压上去
            if (wasGranted)
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { ReconcileTick(); } catch { }
                });
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

        /// <summary>测试钩子：钉住家族可见性（窗口复查不再改写），
        /// 用于验证掌权期间 RefreshPowerStateCore 的插拔电即时切换分支</summary>
        internal void SetFamilyVisibleForTest(bool visible)
        {
            lock (sync)
            {
                familyVisible = visible;
                familyVisibleTestPin = visible;
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

        /// <summary>初始扫描发现的进程：与进程事件逻辑一致，直接加入追踪集合。</summary>
        protected override void OnInitialProcess(ProcessChange change)
        {
            if (string.IsNullOrEmpty(change.Name)) return;
            if (IsDailyProcess(change.Pid, change.Name, change.Path))
            {
                lock (sync) dailyPids.Add(change.Pid);
            }
        }

        /// <summary>初始扫描完成后补算家族可见性，否则已运行的浏览器/Office 不会激活场景。</summary>
        protected override void OnInitialScanComplete()
        {
            RefreshFamilyVisible(true);
        }

        /// <summary>节流窗口复查：进程事件驱动，最多 5 秒一次全量枚举</summary>
        private void RefreshFamilyVisible(bool force)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (sync)
            {
                if (familyVisibleTestPin) return;   // 测试钉住：跳过窗口复查
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
                ApplyBatterySaverIfNeeded();
                Logger.Log("日常优化：获得掌职权（家族窗口/电池），后台转入常规档压制");
            }
            catch (Exception ex) { Logger.LogFailure("日常优化掌权失败", ex); }
        }

        /// <summary>挂起：还原链逐步故障隔离——grantedFlag 已翻 false、仲裁器不会重试，
        /// 单步抛异常若跳过后续步骤，残留只能等启动自愈。每步独立 try，失败计数并明示。</summary>
        public override void Suspend()
        {
            lock (sync)
            {
                if (!grantedFlag) return;
                grantedFlag = false;
            }
            int failed = 0;
            try { StopReconcileTimer(); }
            catch (Exception ex) { failed++; Logger.LogFailure("日常优化挂起：停止校正节拍失败", ex); }
            try { RestoreFamilyBoost(); }
            catch (Exception ex) { failed++; Logger.LogFailure("日常优化挂起：还原家族提优失败", ex); }
            try { SaverRestore(); }
            catch (Exception ex) { failed++; Logger.LogFailure("日常优化挂起：还原电池续航档失败", ex); }
            try { if (core != null) core.ReleaseReason(SuppressReason.Daily); }
            catch (Exception ex) { failed++; Logger.LogFailure("日常优化挂起：解除后台压制失败", ex); }
            if (failed == 0) Logger.Log("日常优化：挂起，全部副作用已还原（检测继续）");
            else Logger.Log("日常优化：挂起完成，但 " + failed + " 个还原步骤失败（残留由下次启动自愈兜底）");
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
            Logger.Log("日常优化：电池供电，后台压制已升档；电池档电源滑块已切到更长续航");
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
                // 健康维护已改独立定时调度（HealthCare.StartAuto），不再依赖本场景掌权
                // 竞态护栏：挂起可能在扫描期间到达（grantedFlag 已翻 false），泄漏的压制立即回收；
                // 若挂起在护栏之后到达，Suspend 自带的 ReleaseReason(Daily) 会兜底。
                lock (sync) { if (!grantedFlag && core != null) core.ReleaseReason(SuppressReason.Daily); }
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

            // 两段式（与游戏模式 Sweep 一致）：先枚举收集候选，批窗口只包 Acquire，
            // 不让 BeginBatch 的锁窗口罩住整个进程枚举
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
            var candidates = new List<KeyValuePair<int, string>>();
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

                    candidates.Add(new KeyValuePair<int, string>(pid, nm));
                }
                catch { }
                finally { p.Dispose(); }
            }

            int suppressed = 0;
            List<int> newlyThrottled = null;
            SuppressionCore.BatchResult batch;
            core.BeginBatch();
            try
            {
                foreach (KeyValuePair<int, string> c in candidates)
                {
                    AcquireResult r = core.Acquire(c.Key, c.Value, SuppressReason.Daily, "dailycare", level);
                    if (r == AcquireResult.NewlyThrottled)
                    {
                        suppressed++;
                        if (newlyThrottled == null) newlyThrottled = new List<int>();
                        newlyThrottled.Add(c.Key);
                    }
                }
            }
            finally
            {
                batch = core.EndBatch();
                if (batch != null && newlyThrottled != null)
                    foreach (int pid in newlyThrottled)
                        if (!batch.WasApplied(pid)) suppressed--;
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

        /// <summary>提优单个家族进程（AboveNormal + IO 3）：走共享快照引擎——掌权检查与
        /// 快照登记同一把锁，挂起插在提优中途时当场回滚；此前无此护栏且创建时间
        /// 存 DateTime.Ticks，崩溃自愈永远匹配不上。</summary>
        private void BoostOne(int pid)
        {
            BoostOneWithSnapshot(() => grantedFlag, pid, Native.ABOVE_NORMAL_PRIORITY_CLASS,
                dailyBoosted, dailyBoostedCreation, dailyBoostedName, dailyBoostedIo);
        }

        internal void RestoreFamilyBoost()
        {
            RestoreBoostSnapshot(dailyBoosted, dailyBoostedCreation, dailyBoostedName, dailyBoostedIo);
        }

        /// <summary>测试钩子：绕过窗口条件与掌权检查直接提优单个家族进程（返回是否入快照）</summary>
        internal bool BoostFamilyForTest(int pid)
        {
            BoostOneWithSnapshot(() => true, pid, Native.ABOVE_NORMAL_PRIORITY_CLASS,
                dailyBoosted, dailyBoostedCreation, dailyBoostedName, dailyBoostedIo);
            lock (sync) return dailyBoosted.ContainsKey(pid);
        }
    }
}
