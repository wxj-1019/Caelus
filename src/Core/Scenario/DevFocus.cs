// @author zenjiro 18967498922@163.com
// 文件用途 开发专注场景：检测编译/调试进程，掌权时暂停索引、提优编译器并压制后台

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CaelusApp
{
    /// <summary>分心应用命中时的动作分级（分心策略纯函数的返回值）。</summary>
    internal enum DistractAction
    {
        None,           // 未掌权或专注模式未开：不动作
        NotifyOnly,     // 首次命中：一次性托盘提醒
        NotifyAndBlock, // 首次命中且阻断开：提醒 + 阻断关闭
        BlockAgain      // 已提醒过且阻断开：不重复提醒，仍阻断关闭
    }

    internal sealed class DevFocus : ScenarioBase
    {
        private readonly SuppressionCore core;
        private readonly Func<bool> enabled;
        private readonly Func<string, string, bool> isWhitelisted;
        private readonly Func<string, bool> isDistract;
        // 游戏接管感知（交接直通用）：挂起时若游戏正要同一共享效果，则把占用权直接
        // 移交给游戏侧，避免「还原再重做」的服务停启/通知开关抖动。未接线时恒走普通还原。
        private readonly Func<bool> gameKeepsSvcPause;
        private readonly Func<bool> gameKeepsNotifQuiet;
        private readonly HashSet<int> activeBuildPids = new HashSet<int>();
        private readonly HashSet<int> activeIdePids = new HashSet<int>();
        private readonly HashSet<string> distractNotified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 阻断气球按名限频：被阻断的自启循环应用不该刷屏（阻断照常执行，只是不重复弹泡）
        private readonly Dictionary<string, long> blockBalloonTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private bool granted;
        private bool quietApplied;
        private bool svcPauseApplied;    // 收敛器记账：SvcPause 升沿已施加（规格 §4.1）
        private bool suppressApplied;    // 收敛器记账：后台压制 sweep 已施加
        private Timer reconcileTimer;
        private long sessionStartTicks;
        private long grantStartTicks;
        private long buildStartTicks;   // 编译集合 空→非空 的真实起点（统计用，取代场景会话起点近似）
        private readonly Dictionary<int, uint> ideBoosted = new Dictionary<int, uint>();
        private readonly Dictionary<int, long> ideBoostedCreation = new Dictionary<int, long>();
        private readonly Dictionary<int, string> ideBoostedName = new Dictionary<int, string>();
        private readonly Dictionary<int, int> ideBoostedIo = new Dictionary<int, int>();
        private readonly Dictionary<int, uint> buildBoosted = new Dictionary<int, uint>();
        private readonly Dictionary<int, long> buildBoostedCreation = new Dictionary<int, long>();
        private readonly Dictionary<int, string> buildBoostedName = new Dictionary<int, string>();
        private readonly Dictionary<int, int> buildBoostedIo = new Dictionary<int, int>();

        // —— 编译活性门（规格 2026-10-02 §3.2）：CPU 进度驻留，空转常驻节点不维持掌权 ——
        private readonly Dictionary<int, long> buildPidLastProgress = new Dictionary<int, long>(); // pid → 最近 CPU 前进时刻
        private readonly Dictionary<int, long> buildPidCpuMs = new Dictionary<int, long>();       // pid → 上次采样累计 CPU 毫秒
        private readonly Dictionary<int, string> buildPidName = new Dictionary<int, string>();    // pid → 进程名（复活重入的合成事件用）
        private readonly HashSet<int> buildPidUnreadable = new HashSet<int>(); // 最近一拍 CPU 不可读且在世的 pid（本拍不参与静默淘汰）
        private Timer buildSampleTimer;
        private int samplingFlag;   // 采样重入护栏：0=空闲 1=采样中（重叠拍直接跳过）

        /// <summary>编译 pid 静默淘汰阈值秒数（默认 20；0=关闭）。internal static 供自测覆写。</summary>
        internal static int BuildIdleDropSeconds = 20;

        /// <summary>编译 pid 的累计 CPU 读数注入（返回 null=不可读，保守保留）。null=生产实现。</summary>
        internal static Func<int, TimeSpan?> CpuProbe;

        /// <summary>静默淘汰判定（纯逻辑，可单测）：未起钟（&lt;=0）不淘汰；
        /// 静默超过 BuildIdleDropSeconds 秒（0=关闭）淘汰。</summary>
        internal static bool ShouldDropForIdle(long lastProgressTicks, long nowTicks)
        {
            if (lastProgressTicks <= 0) return false;
            if (BuildIdleDropSeconds <= 0) return false;
            return nowTicks - lastProgressTicks >= BuildIdleDropSeconds * TimeSpan.TicksPerSecond;
        }

        /// <summary>读编译 pid 的累计 CPU（可注入）。不可读（提权/跨会话/已退出）返回 null：
        /// 保守保留——不更新进度也不参与静默淘汰（SampleBuildCpu 标记后 RemoveIdleBuilds 跳过，
        /// 规格 §3.2「不可读不淘汰」），防误杀真编译；死进程由 Stopped 事件、CleanDeadPids
        /// 兜底与采样节拍的句柄死活甄别清出。</summary>
        private static TimeSpan? ReadBuildCpu(int pid)
        {
            Func<int, TimeSpan?> probe = CpuProbe;
            if (probe != null) return probe(pid);
            try
            {
                Process p = Process.GetProcessById(pid);
                try { return p.TotalProcessorTime; }
                finally { p.Dispose(); }
            }
            catch { return null; }
        }

        /// <summary>编译会话状态变化时触发，参数是文案 key（bal.buildstart / bal.buildend）</summary>
        public event Action<string> SessionChanged;

        public override ScenarioKind Kind { get { return ScenarioKind.DevFocus; } }
        public override int Priority { get { return 50; } }

        /// <summary>专注模式开关状态。实时读注册表——WPF 宿主（独立进程）修改后本进程下次评估即生效</summary>
        public bool FocusModeOn { get { return Settings.Load("DevFocusModeOn", false); } }

        /// <summary>IDE 优化开关（默认开）。关闭后 IDE 家族不再提优、也不再作为活性来源。</summary>
        public bool IdeOn { get { return Settings.Load("DevFocusIdeOn", true); } }

        /// <summary>分心阻断开关（默认关）。开启后掌权期间命中的分心应用会被自动关闭。</summary>
        public bool BlockDistractOn { get { return Settings.Load("DevFocusDistractBlock", false); } }

        /// <summary>IDE 进程有可见窗口才计入活性（后台挂起的常驻程序不激活场景）。</summary>
        private bool ideVisible;
        private long lastIdeWindowCheckTicks;

        /// <summary>三来源任一活跃：编译进程、专注模式、IDE 进程（须有可见窗口）</summary>
        private bool AnyActive
        {
            get { return activeBuildPids.Count > 0 || FocusModeOn || (IdeOn && ideVisible); }
        }

        protected override bool WantsActiveLocked
        {
            get { return enabled() && (activeBuildPids.Count > 0 || FocusModeOn || (IdeOn && ideVisible)); }
        }

        /// <summary>检测状态：是否存在任一活性来源（与是否掌权无关）</summary>
        public bool IsActive { get { lock (sync) return AnyActive; } }

        /// <summary>仲裁器授权状态：副作用是否已施加</summary>
        public bool IsGranted { get { lock (sync) return granted; } }

        /// <summary>实时监控页：当前编译/IDE 活性进程数（锁内计数）。</summary>
        public int BuildActivityCount { get { lock (sync) return activeBuildPids.Count; } }
        public int IdeActivityCount { get { lock (sync) return activeIdePids.Count; } }

        /// <summary>实时监控页：当前提优中的进程描述（IDE/编译两档）。</summary>
        internal List<string> DescribeBoosts()
        {
            var rows = new List<string>();
            lock (sync)
            {
                foreach (var kv in ideBoostedName) rows.Add(kv.Value + "（IDE 提优 AboveNormal）");
                foreach (var kv in buildBoostedName) rows.Add(kv.Value + "（编译提优 High）");
            }
            return rows;
        }

        /// <summary>测试钩子：校正定时器是否运行中（应只在掌权期间为 true）</summary>
        internal bool FocusTimerRunning { get { lock (sync) return reconcileTimer != null; } }

        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core, Func<bool> enabled,
            Func<string, string, bool> isWhitelisted, Func<string, bool> isDistract)
            : this(arbiter, core, enabled, isWhitelisted, isDistract, null, null)
        {
        }

        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core, Func<bool> enabled,
            Func<string, string, bool> isWhitelisted, Func<string, bool> isDistract,
            Func<bool> gameKeepsSvcPause, Func<bool> gameKeepsNotifQuiet)
            : base(arbiter)
        {
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            this.isWhitelisted = isWhitelisted;
            this.isDistract = isDistract;
            this.gameKeepsSvcPause = gameKeepsSvcPause;
            this.gameKeepsNotifQuiet = gameKeepsNotifQuiet;
        }

        /// <summary>专注模式开关（托盘菜单/设置页调用）。写注册表 + 活性重算。</summary>
        public void SetFocusMode(bool on)
        {
            Settings.Save("DevFocusModeOn", on);
            if (!on) { lock (sync) { distractNotified.Clear(); blockBalloonTicks.Clear(); } }
            RecomputeActivity();
            // 掌权中开关翻转的升降沿（锁内只读布尔，收敛体含全进程枚举不进 sync）
            bool recon; lock (sync) recon = granted;
            if (recon) ReconcileSideEffects();
        }

        /// <summary>IDE 优化开关（设置页调用）。关闭时清空已追踪的 IDE 集合，避免 Grant 仍提优。</summary>
        public void SetIdeOn(bool on)
        {
            Settings.Save("DevFocusIdeOn", on);
            if (!on)
            {
                lock (sync)
                {
                    activeIdePids.Clear();
                    ideVisible = false;
                }
            }
            RecomputeActivity();
        }

        /// <summary>场景总开关（设置页/场景总览调用）。关闭时立即退出仲裁器活性集合并还原副作用，
        /// 避免「被游戏抢占后关闭开关、游戏退出时场景又恢复掌权」的残留。
        /// IDE/编译提优快照由 Suspend→RestoreIdeBoost 在 ForceReportInactive 内同步还原。</summary>
        public void SetEnabled(bool on)
        {
            Settings.Save("DevModeOn", on);
            if (!on)
            {
                lock (sync)
                {
                    activeBuildPids.Clear();
                    activeIdePids.Clear();
                    ideVisible = false;
                    distractNotified.Clear();
                    blockBalloonTicks.Clear();
                    buildPidLastProgress.Clear();
                    buildPidCpuMs.Clear();
                    buildPidName.Clear();
                    buildPidUnreadable.Clear();
                }
                Timer st;
                lock (sync) { st = buildSampleTimer; buildSampleTimer = null; }
                if (st != null) st.Dispose();
                ForceReportInactive();
            }
            else
            {
                RecomputeActivity();
            }
        }

        public void NotifyProcessChanges(ProcessChangeBatch batch)
        {
            if (batch == null || batch.Changes == null) return;

            // 开关关闭：撤销活性报告（仲裁器会回调 Suspend 还原副作用），避免服务被永久暂停
            if (!enabled())
            {
                bool wasReported;
                lock (sync)
                {
                    wasReported = reported;
                    reported = false;
                    activeBuildPids.Clear();
                    activeIdePids.Clear();
                    buildPidLastProgress.Clear();
                    buildPidCpuMs.Clear();
                    buildPidName.Clear();
                    buildPidUnreadable.Clear();
                }
                Timer st;
                lock (sync) { st = buildSampleTimer; buildSampleTimer = null; }
                if (st != null) st.Dispose();
                if (wasReported) arbiter.ReportActivity(Kind, false);
                return;
            }

            bool buildActivity = false;  // 仅编译来源变化时触发文案/日志
            bool becameActive = false;
            bool becameIdle = false;
            bool buildSetEmptied = false;   // 编译集合 空←非空（锁内判定，锁外收尾——防 TOCTOU）
            bool wasBuildActive;
            bool ideChanged = false;
            List<int> newlyBuilt = null;
            List<int> toBlock = null;

            lock (sync)
            {
                wasBuildActive = activeBuildPids.Count > 0;

                foreach (ProcessChange pc in batch.Changes)
                {
                    if (string.IsNullOrEmpty(pc.Name)) continue;

                    // 编译进程匹配
                    if (BuildCatalog.IsMatch(pc.Name))
                    {
                        if (pc.Kind == ProcessChangeKind.Started)
                        {
                            if (activeBuildPids.Add(pc.Pid))
                            {
                                if (newlyBuilt == null) newlyBuilt = new List<int>();
                                newlyBuilt.Add(pc.Pid);
                                EnterBuildPidLocked(pc.Pid, pc.Name, DateTime.UtcNow.Ticks);
                            }
                        }
                        else if (pc.Kind == ProcessChangeKind.Stopped)
                            activeBuildPids.Remove(pc.Pid);   // 记忆字典保留供复活重入
                    }

                    // IDE 进程匹配（Task 4 接线：名称预筛 + 安装目录双重校验，见 IsIdeProcess）
                    if (pc.Kind == ProcessChangeKind.Started && IdeOn && IsIdeProcess(pc.Pid, pc.Name, pc.Path))
                    {
                        if (activeIdePids.Add(pc.Pid)) ideChanged = true;
                    }
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                    {
                        if (activeIdePids.Remove(pc.Pid)) ideChanged = true;
                    }

                    // 专注模式下的分心应用：策略分级动作——提醒按名去重、阻断不去重；
                    // 关闭进程在锁外执行（CloseMainWindow 最多等 1 秒，不能压住事件线程）
                    if (pc.Kind == ProcessChangeKind.Started && isDistract != null && isDistract(pc.Name))
                    {
                        DistractAction act = DecideDistractAction(
                            granted, FocusModeOn, distractNotified.Contains(pc.Name), BlockDistractOn,
                            DevServiceCatalog.IsMatch(pc.Name));
                        if (act != DistractAction.None)
                        {
                            bool blocked = act == DistractAction.NotifyAndBlock || act == DistractAction.BlockAgain;
                            if (act != DistractAction.BlockAgain) distractNotified.Add(pc.Name);
                            try { FocusStats.RecordDistract(blocked, BareProcessName(pc.Name), DateTime.Now); } catch { }
                            if (blocked)
                            {
                                if (toBlock == null) toBlock = new List<int>();
                                toBlock.Add(pc.Pid);
                            }
                            // 阻断气球 30 秒/名限频：阻断照常，弹泡不刷屏
                            bool balloon = true;
                            if (blocked)
                            {
                                long last;
                                blockBalloonTicks.TryGetValue(pc.Name, out last);
                                balloon = BlockBalloonReady(last, DateTime.UtcNow.Ticks);
                                if (balloon) blockBalloonTicks[pc.Name] = DateTime.UtcNow.Ticks;
                            }
                            if (balloon)
                            {
                                try
                                {
                                    var h = SessionChanged;
                                    if (h != null) h(blocked ? "bal.distract.block" : "bal.distract");
                                }
                                catch { }
                            }
                        }
                    }
                }

                // 兜底清理：短命进程的 Stopped 事件可能因进程已退出而丢失
                CleanDeadPids(activeBuildPids);
                CleanDeadPids(activeIdePids);

                bool nowActive = AnyActive;
                if (nowActive && !reported)
                {
                    reported = true;
                    becameActive = true;
                    sessionStartTicks = DateTime.UtcNow.Ticks;
                }
                else if (!nowActive && reported)
                {
                    reported = false;
                    becameIdle = true;
                }

                buildActivity = wasBuildActive != (activeBuildPids.Count > 0);
                buildSetEmptied = wasBuildActive && activeBuildPids.Count == 0;
                // 编译统计真实起止：集合 空→非空 记起点并起采样器；非空→空 的时长记
                // 收归 BuildSetBecameEmpty 单一记账点（事件 Stopped 与活性门淘汰共用）
                if (activeBuildPids.Count > 0 && !wasBuildActive)
                {
                    buildStartTicks = DateTime.UtcNow.Ticks;
                    if (buildSampleTimer == null)
                        buildSampleTimer = new Timer(delegate(object s) { try { SampleBuildCpu(); } catch { } },
                            null, 5000, 5000);
                }
            }

            // 编译集合 空←非空 的统一收尾（统计落点 + 停采样 + 收敛副作用降沿）；
            // 门控用锁内判定的 buildSetEmptied，不在锁外重读集合——防与并发 Started 批次
            // 交错时清掉新会话起点钟/Dispose 新采样器（TOCTOU）
            if (buildSetEmptied) BuildSetBecameEmpty();

            // 分心阻断在锁外执行：优雅关闭最多等 1 秒，不能压住进程事件线程
            if (toBlock != null)
                foreach (int pid in toBlock) CloseDistractProcess(pid);

            // 掌权期间新启动的编译进程同步提优（Grant 只提当时已存在的进程）；
            // 未掌权时不在此提优，交给随后的 Grant 统一处理
            if (newlyBuilt != null)
            {
                bool isGranted;
                lock (sync) isGranted = granted;
                if (isGranted)
                    foreach (int pid in newlyBuilt)
                        BoostOneWithSnapshot(() => granted, pid, Native.HIGH_PRIORITY_CLASS,
                            buildBoosted, buildBoostedCreation, buildBoostedName, buildBoostedIo);
            }

            // IDE 集合变化时复查可见窗口（节流）：无窗口的常驻 IDE 不激活场景
            if (ideChanged) RefreshIdeVisible(false);

            // 仍掌权时收敛副作用：编译中途起始的服务暂停/压制升沿在此补做（P1-2 主诉）
            // 锁内只读布尔，收敛体含全进程枚举不进 sync
            bool recon; lock (sync) recon = granted;
            if (recon) ReconcileSideEffects();

            // 活性变化只向仲裁器报告；副作用由仲裁器经 Grant/Suspend 回调控制
            if (becameActive)
            {
                if (buildActivity)
                    try { var h = SessionChanged; if (h != null) h("bal.buildstart"); } catch { }
                arbiter.ReportActivity(Kind, true);
            }
            if (becameIdle)
            {
                if (buildActivity)
                {
                    // 编译时长统计已在 BuildSetBecameEmpty 单一记账点落盘（见上），
                    // 此处只发场景结束气球
                    try { var h = SessionChanged; if (h != null) h("bal.buildend"); } catch { }
                }
                arbiter.ReportActivity(Kind, false);
            }
        }

        /// <summary>初始扫描发现的进程：与进程事件逻辑一致，直接加入追踪集合。</summary>
        protected override void OnInitialProcess(ProcessChange change)
        {
            if (string.IsNullOrEmpty(change.Name)) return;
            if (BuildCatalog.IsMatch(change.Name))
            {
                // 初始扫描加入的编译进程也要起钟：否则它在第一批事件里就结束时，
                // 转换逻辑以 buildStartTicks==0 计出天文数字时长，写爆今日统计。
                // 活性门入场悲观（规格 §3.2）：宽限 = 阈值 − 采样周期（下限 0，默认 20−5=15 秒）
                // 证明自己在编译——开机残留的 nodeReuse 节点最多一个采样周期假阳性，
                // 真编译最迟一个采样周期收权，首拍即完成甄别
                lock (sync)
                {
                    if (activeBuildPids.Add(change.Pid))
                    {
                        if (buildStartTicks == 0) buildStartTicks = DateTime.UtcNow.Ticks;
                        long graceSec = BuildIdleDropSeconds > 5 ? BuildIdleDropSeconds - 5 : 0;
                        EnterBuildPidLocked(change.Pid, change.Name,
                            DateTime.UtcNow.Ticks - graceSec * TimeSpan.TicksPerSecond);
                        if (buildSampleTimer == null)
                            buildSampleTimer = new Timer(delegate(object s) { try { SampleBuildCpu(); } catch { } },
                                null, 5000, 5000);
                    }
                }
            }
            if (IdeOn && IsIdeProcess(change.Pid, change.Name, change.Path))
            {
                lock (sync) activeIdePids.Add(change.Pid);
            }
        }

        /// <summary>清理 PID 集合中已死进程。</summary>
        private static void CleanDeadPids(HashSet<int> pids)
        {
            if (pids.Count == 0) return;
            var dead = new List<int>();
            foreach (int pid in pids)
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) { dead.Add(pid); continue; }
                try { if (!Native.StillActive(h)) dead.Add(pid); }
                finally { Native.CloseHandle(h); }
            }
            foreach (int pid in dead) pids.Remove(pid);
        }

        // —— 编译活性门内部 ——

        /// <summary>乐观/悲观入场：记录名字与起钟时刻，CPU 基线由下次采样重建。</summary>
        private void EnterBuildPidLocked(int pid, string name, long progressTicks)
        {
            buildPidName[pid] = name ?? "";
            buildPidLastProgress[pid] = progressTicks;
            buildPidCpuMs.Remove(pid);
        }

        /// <summary>活性门淘汰（采样节拍调用；自测可直接传人造时刻）：静默超阈的 pid
        /// 移出活性集合，集合因此 空←非空 时走与 Stopped 相同的收尾。</summary>
        internal void RemoveIdleBuilds(long nowTicks)
        {
            bool wasBuildActive;
            lock (sync)
            {
                wasBuildActive = activeBuildPids.Count > 0;
                if (!wasBuildActive) return;
                var dropped = new List<int>();
                foreach (int pid in activeBuildPids)
                {
                    if (buildPidUnreadable.Contains(pid)) continue;   // 本拍不可读：保守保留（规格 §3.2）
                    long last;
                    buildPidLastProgress.TryGetValue(pid, out last);
                    if (ShouldDropForIdle(last, nowTicks)) dropped.Add(pid);
                }
                if (dropped.Count == 0) return;
                foreach (int pid in dropped) activeBuildPids.Remove(pid);
                if (activeBuildPids.Count > 0) return;   // 仍有编译在场：不收尾（采样器继续跑）
            }
            BuildSetBecameEmpty();
        }

        /// <summary>编译集合 空←非空 的统一收尾（事件 Stopped 与活性门淘汰共用的单一记账点）：
        /// 统计落点（FocusStats + 日志）+ 复位编译起点钟 + 停采样器 + 仍掌权时收敛副作用降沿。</summary>
        private void BuildSetBecameEmpty()
        {
            long elapsed;
            lock (sync)
            {
                elapsed = BuildEndedElapsed(buildStartTicks, DateTime.UtcNow.Ticks);
                buildStartTicks = 0;
            }
            if (elapsed > 0)
            {
                try { FocusStats.RecordBuild(elapsed, DateTime.Now); } catch { }
                try { Logger.Log(string.Format("开发专注：本次编译 {0:0.#} 秒",
                    elapsed / (double)TimeSpan.TicksPerSecond)); } catch { }
            }
            Timer t;
            lock (sync)
            {
                // 防御性复查：收尾期间有新编译入场（并发 Started 批次）则不停表、也不向
                // 空闲重算（RemoveIdleBuilds/deadActive 调用点天然满足，此为兜底）
                if (activeBuildPids.Count > 0) return;
                t = buildSampleTimer;
                buildSampleTimer = null;
            }
            if (t != null) t.Dispose();
            // 编译来源消失后活性重算：无其他来源时向仲裁器报告不活跃（收权/还原服务暂停）。
            // 事件 Stopped 路径在 NotifyProcessChanges 锁内已翻 reported，此处重算为幂等空转，
            // 只有活性门淘汰路径（无事件批次）靠这里收权。
            RecomputeActivity();
            // 编译排空仍掌权：降沿回收（服务恢复/压制解除）。锁内只读布尔，收敛体不进 sync
            bool recon; lock (sync) recon = granted;
            if (recon) ReconcileSideEffects();
        }

        /// <summary>CPU 采样节拍（5 秒）：推进度、不可读保守标记（句柄甄别清死）、
        /// 清死记忆、复活重入、静默淘汰。重叠拍直接跳过（回调链含 Grant→全进程枚举
        /// 压制，可超一个周期；跳过拍不影响正确性，下拍补齐）。</summary>
        internal void SampleBuildCpu()
        {
            // 重入护栏：前拍未完时本拍直接跳过——否则 B 拍开局清空 buildPidUnreadable
            // 会击穿 A 拍的不可读保护，A 拍尾部 RemoveIdleBuilds 按无标记误杀提权真编译
            if (Interlocked.CompareExchange(ref samplingFlag, 1, 0) != 0) return;
            try
            {
                int[] active;
                int[] remembered;
                lock (sync)
                {
                    active = new int[activeBuildPids.Count];
                    activeBuildPids.CopyTo(active);
                    // 记忆全集以名字表为准：入场/重入场必写名字，清死才删——
                    // 只遍历 cpu 表会漏掉「已入场未采样」与「被淘汰待重入」的 pid（它们恰恰是本节拍要看的）
                    remembered = new int[buildPidName.Count];
                    buildPidName.Keys.CopyTo(remembered, 0);
                    buildPidUnreadable.Clear();   // 标记只活一拍：本拍重新甄别
                }
                long now = DateTime.UtcNow.Ticks;
                var alive = new HashSet<int>();
                var reenter = new List<int>();
                var deadActive = new List<int>();
                foreach (int pid in remembered)
                {
                    TimeSpan? cpu = ReadBuildCpu(pid);
                    if (cpu == null)
                    {
                        // 不可读（提权/跨会话/已退出）：句柄死活甄别——确认死者清出，
                        // 其余（含句柄都拿不到的受保护进程）保守保留：本拍标记不可读，
                        // RemoveIdleBuilds 对其跳过（规格 §3.2「不可读不淘汰」）。
                        // 已知缝隙：句柄完全打不开的 PPL 类活跃 pid 活性门保守保留，但下一
                        // 事件批的 CleanDeadPids（h==Zero 判死）会把它移出活性集合——既有
                        // 交互缝隙，此类进程几乎不在编译名录，记录不改行为
                        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                        if (h != IntPtr.Zero)
                        {
                            bool isAlive;
                            try { isAlive = Native.StillActive(h); }
                            finally { Native.CloseHandle(h); }
                            if (!isAlive)
                            {
                                deadActive.Add(pid);   // 句柄可开且已退出：确认死，清出活性集合与记忆
                                continue;
                            }
                        }
                        lock (sync) buildPidUnreadable.Add(pid);
                        continue;
                    }
                    alive.Add(pid);
                    long ms = (long)cpu.Value.TotalMilliseconds;
                    long prev;
                    lock (sync) buildPidCpuMs.TryGetValue(pid, out prev);
                    if (ms <= prev) continue;
                    lock (sync)
                    {
                        buildPidCpuMs[pid] = ms;
                        buildPidLastProgress[pid] = now;
                    }
                    bool inSet = Array.IndexOf(active, pid) >= 0;
                    if (!inSet) reenter.Add(pid);            // 被淘汰的 pid 复烧 CPU → 重入场
                }
                // 甄别出的死者：清出活性集合与记忆；活性集合因此 空←非空 时走与
                // RemoveIdleBuilds 同款收尾（统计落点 + 停采样 + 活性重算收权）
                if (deadActive.Count > 0)
                {
                    bool activeBecameEmpty;
                    lock (sync)
                    {
                        bool wasActive = activeBuildPids.Count > 0;
                        foreach (int pid in deadActive)
                        {
                            activeBuildPids.Remove(pid);
                            buildPidCpuMs.Remove(pid);
                            buildPidLastProgress.Remove(pid);
                            buildPidName.Remove(pid);
                        }
                        activeBecameEmpty = wasActive && activeBuildPids.Count == 0;
                    }
                    if (activeBecameEmpty) BuildSetBecameEmpty();
                }
                // 记忆清死：确认死亡的 pid 才清（不可读 ≠ 死，规格 §3.2）。清死范围与记忆全集
                // 一致——否则短命编译器（csc 数秒即退、从未被采样）的名字条目永久滞留，
                // PID 复用后会以「复烧 CPU」假触发重入
                lock (sync)
                {
                    var dead = new List<int>();
                    foreach (int pid in buildPidName.Keys)
                    {
                        if (activeBuildPids.Contains(pid) || alive.Contains(pid)) continue;
                        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                        if (h == IntPtr.Zero) { dead.Add(pid); continue; }
                        try { if (!Native.StillActive(h)) dead.Add(pid); }
                        finally { Native.CloseHandle(h); }
                    }
                    foreach (int pid in dead)
                    {
                        buildPidCpuMs.Remove(pid);
                        buildPidLastProgress.Remove(pid);
                        buildPidName.Remove(pid);
                    }
                }
                if (reenter.Count > 0)
                {
                    // 复活重入走事件正路（活性上报/掌权/提优/气球全部复用）
                    var changes = new List<ProcessChange>();
                    foreach (int pid in reenter)
                    {
                        string nm;
                        lock (sync) buildPidName.TryGetValue(pid, out nm);
                        changes.Add(MakeReentryChange(pid, string.IsNullOrEmpty(nm) ? "msbuild" : nm));
                    }
                    NotifyProcessChanges(new ProcessChangeBatch(changes.ToArray(), false));
                }
                RemoveIdleBuilds(now);
            }
            finally { Interlocked.Exchange(ref samplingFlag, 0); }
        }

        private static ProcessChange MakeReentryChange(int pid, string name)
        {
            var pc = new ProcessChange();
            pc.Pid = pid;
            pc.Name = name;
            pc.Kind = ProcessChangeKind.Started;
            return pc;
        }

        /// <summary>编译结束时长计算（纯逻辑，可单测）：无起点（初始扫描外的边界）一律记 0，
        /// 杜绝 buildStartTicks==0 时计出天文时长写爆统计。</summary>
        internal static long BuildEndedElapsed(long startTicks, long nowTicks)
        {
            if (startTicks <= 0) return 0;
            long e = nowTicks - startTicks;
            return e > 0 ? e : 0;
        }

        /// <summary>开发专注/日常场景的压制豁免组合（两宿主共用，改动只此一处）：
        /// 游戏白名单 OR 守护服务 OR 日常家族（浏览器/Office/会议）。
        /// 日常家族入列的理由：编译位压制会降所有无窗口后台进程的优先级——浏览器的 GPU/解码
        /// 进程正是无窗口的，看视频/开会时会被误伤卡顿（2026-09-12 实机报告：压制 191 进程后
        /// 视频卡）；与日常场景「家族豁免压制」的既有语义一致，代价是编译期前台应用少让少量 CPU。
        /// 注：日常家族走名称+路径双校验，路径取不到（受保护进程）时按未命中处理（这类进程
        /// 本就被反作弊通道豁免）。</summary>
        internal static Func<string, string, bool> ComposeWhitelist(Func<string, string, bool> gameWhitelist)
        {
            return (name, path) =>
                (gameWhitelist != null && gameWhitelist(name, path))
                || DevServiceCatalog.IsMatch(name)
                || DailyCatalog.IsMatch(name, path);
        }

        /// <summary>分心动作策略（纯逻辑，可单测）：只在「掌权且专注模式开」时动作；
        /// 提醒按名去重（alreadyNotified），阻断开关把动作升级为阻断且不去重。
        /// 守护服务清单优先：已注册开发服务不是分心应用（与后台压制豁免同序），
        /// 否则「自动拉起 vs 专注阻断」会对同一进程形成拉起→关闭→再拉起的死循环。</summary>
        internal static DistractAction DecideDistractAction(bool granted, bool focusOn, bool alreadyNotified, bool blockOn, bool isDevService)
        {
            if (isDevService) return DistractAction.None;
            if (!granted || !focusOn) return DistractAction.None;
            if (blockOn) return alreadyNotified ? DistractAction.BlockAgain : DistractAction.NotifyAndBlock;
            return alreadyNotified ? DistractAction.None : DistractAction.NotifyOnly;
        }

        /// <summary>阻断气球限频判定（纯逻辑，可单测）：距上次弹泡不足 30 秒不再弹。</summary>
        internal static bool BlockBalloonReady(long lastTicks, long nowTicks)
        {
            if (lastTicks <= 0) return true;
            return nowTicks - lastTicks >= 30L * TimeSpan.TicksPerSecond;
        }

        /// <summary>进程名去 .exe 后缀（分心按名统计的归一键）。</summary>
        private static string BareProcessName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - 4);
            return name;
        }

        /// <summary>阻断关闭分心应用：先发优雅关闭消息，1 秒未退再强杀，全程故障隔离。
        /// PID 来自毫秒级新鲜的 Started 事件，复用窗口可忽略。</summary>
        private static void CloseDistractProcess(int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                try
                {
                    bool closed = p.CloseMainWindow();
                    if (!closed) p.Kill();
                    else if (!p.WaitForExit(1000)) p.Kill();
                }
                finally { p.Dispose(); }
                Logger.Log("开发专注：分心应用已阻断关闭（PID " + pid + "）");
                ActivityLog.Add("分心应用已阻断关闭");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注：阻断关闭分心应用失败", ex); }
        }

        /// <summary>判断进程是否为 IDE 进程。名称预筛 + 安装目录双重校验。</summary>
        private bool IsIdeProcess(int pid, string name, string path)
        {
            if (!IdeCatalog.NameMatches(name)) return false;
            string p = path;
            if (string.IsNullOrEmpty(p))
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return false;
                try { p = Native.ImagePath(h); }
                finally { Native.CloseHandle(h); }
            }
            return IdeCatalog.IsMatch(name, p);
        }

        /// <summary>IScenario：获得掌职权。副作用不再按 Grant 时刻快照施加——
        /// 收敛器按 build/focus 当前态升降沿收敛（规格 §4），节拍无条件启动
        /// （IDE-only 授予也要跑活性重算与收敛）。</summary>
        public override void Grant()
        {
            lock (sync)
            {
                if (granted) return;
                granted = true;
                grantStartTicks = DateTime.UtcNow.Ticks;
            }
            try
            {
                bool build; bool focus; bool ide;
                lock (sync)
                {
                    build = activeBuildPids.Count > 0;
                    focus = FocusModeOn;
                    ide = activeIdePids.Count > 0;
                }
                StartReconcileTimer();
                ReconcileSideEffects();
                if (ide) ReconcileIdeBoost();
                Logger.Log("开发专注：获得掌职权（编译=" + build + " 专注=" + focus + " IDE=" + ide + "）");
                ActivityLog.Add("开发专注掌权（编译=" + build + " 专注=" + focus + " IDE=" + ide + "）");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注掌权失败", ex); }
        }

        /// <summary>副作用收敛器（规格 §4.1 期望态表）：掌权期间让 服务暂停=build、
        /// 通知静默=focus、后台压制=build||focus 持续成立——Grant/进程事件/开关翻转/
        /// 校正节拍/活性门淘汰全部收敛到期望态。升降沿幂等：SvcPause/Notif 走 owner
        /// 引用计数（游戏叠加时最后一个占用方离开才真还原），sweep 的 Acquire 对已压
        /// 进程返回 AlreadyThrottled，boost 有快照字典防重。</summary>
        private void ReconcileSideEffects()
        {
            bool build; bool focus; bool grantedNow;
            lock (sync)
            {
                grantedNow = granted;
                build = activeBuildPids.Count > 0;
                focus = FocusModeOn;
            }
            if (!grantedNow) return;   // 已挂起：不施加升沿，全量还原交给 Suspend

            try
            {
                if (build && !svcPauseApplied)
                    svcPauseApplied = SvcPause.Activate(SvcPause.OwnerDevFocus);
                else if (!build && svcPauseApplied)
                {
                    try { SvcPause.Restore(SvcPause.OwnerDevFocus); } catch { }
                    svcPauseApplied = false;
                }

                if (focus && !quietApplied)
                {
                    try { quietApplied = Notif.Quiet(Notif.OwnerDevFocus); } catch { }
                }
                else if (!focus && quietApplied)
                {
                    try { Notif.Restore(Notif.OwnerDevFocus); } catch { }
                    quietApplied = false;
                }

                bool wantSuppress = build || focus;
                if (wantSuppress && !suppressApplied)
                {
                    SweepBuildSuppression();
                    suppressApplied = true;
                }
                else if (!wantSuppress && suppressApplied)
                {
                    if (core != null) { try { core.ReleaseReason(SuppressReason.Build); } catch { } }
                    suppressApplied = false;
                }

                if (build) BoostBuildProcesses();   // 幂等：快照字典 ContainsKey 跳过

                // 竞态护栏（与 ReconcileTick 同款）：收敛期间挂起到达时，已施加升沿立即回收
                lock (sync) grantedNow = granted;
                if (!grantedNow)
                {
                    if (svcPauseApplied) { try { SvcPause.Restore(SvcPause.OwnerDevFocus); } catch { } svcPauseApplied = false; }
                    if (quietApplied) { try { Notif.Restore(Notif.OwnerDevFocus); } catch { } quietApplied = false; }
                    if (suppressApplied && core != null) { try { core.ReleaseReason(SuppressReason.Build); } catch { } suppressApplied = false; }
                }
            }
            catch (Exception ex) { Logger.LogFailure("开发专注：副作用收敛失败", ex); }
        }

        /// <summary>IScenario：挂起——还原全部副作用，检测状态保留。
        /// 还原链逐步故障隔离：granted 已翻 false、仲裁器不会重试，单步抛异常
        /// 若跳过后续步骤，残留只能等启动自愈——每步独立 try，失败计数并在日志明示。</summary>
        public override void Suspend()
        {
            bool wasQuiet;
            long elapsed = 0;
            lock (sync)
            {
                if (!granted) return;
                granted = false;
                wasQuiet = quietApplied;
                quietApplied = false;
                svcPauseApplied = false;
                suppressApplied = false;
                elapsed = DateTime.UtcNow.Ticks - grantStartTicks;
            }
            if (elapsed > 0) FocusStats.RecordSession(elapsed);

            int failed = 0;
            try { StopReconcileTimer(); }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：停止校正节拍失败", ex); }
            try { RestoreIdeBoost(); }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：还原 IDE 提优失败", ex); }
            try { RestoreBuildBoost(); }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：还原编译提优失败", ex); }
            try { if (core != null) core.ReleaseReason(SuppressReason.Build); }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：解除后台压制失败", ex); }
            // 共享效果交接直通：游戏正要同一效果时只移交占用权，不还原再重做
            if (wasQuiet)
            {
                try
                {
                    if (gameKeepsNotifQuiet != null && gameKeepsNotifQuiet())
                        Notif.HandoffOwner(Notif.OwnerDevFocus, Notif.OwnerGame);
                    else
                        Notif.Restore(Notif.OwnerDevFocus);
                }
                catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：还原通知静默失败", ex); }
            }
            try
            {
                if (gameKeepsSvcPause != null && gameKeepsSvcPause())
                    SvcPause.HandoffOwner(SvcPause.OwnerDevFocus, SvcPause.OwnerGame);
                else
                    SvcPause.Restore(SvcPause.OwnerDevFocus);
            }
            catch (Exception ex) { failed++; Logger.LogFailure("开发专注挂起：还原服务暂停失败", ex); }

            if (failed == 0) Logger.Log("开发专注：挂起，全部副作用已还原（检测继续）");
            else Logger.Log("开发专注：挂起完成，但 " + failed + " 个还原步骤失败（残留由下次启动自愈兜底）");
            ActivityLog.Add(failed == 0 ? "开发专注挂起（副作用已还原）" : "开发专注挂起（" + failed + " 个还原步骤失败）");
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
                BoostOneWithSnapshot(() => granted, pid, Native.HIGH_PRIORITY_CLASS,
                    buildBoosted, buildBoostedCreation, buildBoostedName, buildBoostedIo);
        }

        internal void RestoreBuildBoost()
        {
            RestoreBoostSnapshot(buildBoosted, buildBoostedCreation, buildBoostedName, buildBoostedIo);
        }

        /// <summary>常规档压制决策（纯逻辑，可单测）：复用游戏模式的常规档豁免计算器，
        /// 再叠加白名单。activeGameRoot/游戏宿主祖先在游戏不活跃时无意义，不传入。</summary>
        internal static bool ShouldSuppressBackground(int pid, int selfPid, string name, string path,
            int session, int ownerSession, int foregroundPid, HashSet<int> visibleWindowPids,
            string windowsRoot, Func<string, string, bool> isWhitelisted)
        {
            bool userFacing = visibleWindowPids != null && visibleWindowPids.Contains(pid);
            if (!GameMode.BasicBackgroundEligible(pid, selfPid, name, path, session, ownerSession,
                foregroundPid, userFacing, windowsRoot)) return false;
            if (isWhitelisted != null && isWhitelisted(name, path)) return false;
            return true;
        }

        /// <summary>全量扫描后台进程并按编译位压制。在场景事件泵线程执行（ScenarioEventPump），
        /// 不占用 ProcNotify 事件线程——游戏启动/退出检测不被本扫描拖延。</summary>
        private void SweepBuildSuppression()
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

            // 两段式（与游戏模式 Sweep 一致）：先枚举收集候选，再在批窗口内只做
            // Acquire——BeginBatch 持有全局锁直到 EndBatch，进程枚举/句柄查询
            // 不能压进锁窗口，否则 Tamer 等线程整个扫描期读到冻结状态
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
            var candidates = new List<KeyValuePair<int, string>>();
            foreach (Process p in all)
            {
                try
                {
                    int pid = p.Id;
                    if (pid <= 4 || pid == selfPid) continue;
                    // 编译进程本身是提优对象（HIGH），绝不被后台压制——否则先提后压自相矛盾
                    lock (sync) { if (activeBuildPids.Contains(pid)) continue; }

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

                    if (!ShouldSuppressBackground(pid, selfPid, nm, ipath, session, ownerSession,
                        foregroundPid, visible, windowsRoot, isWhitelisted)) continue;

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
                    AcquireResult r = core.Acquire(c.Key, c.Value, SuppressReason.Build, "devfocus",
                        SuppressionLevel.Eco);
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
                // 批量涂写在 EndBatch 才落盘：记账的"新压制"数按实际生效结果校正
                if (batch != null && newlyThrottled != null)
                    foreach (int pid in newlyThrottled)
                        if (!batch.WasApplied(pid)) suppressed--;
            }
            if (suppressed > 0)
                Logger.Log("开发专注：编译期间压制 " + suppressed + " 个后台进程（编译位，退出即还原）");
            if (suppressed > 0) ActivityLog.Add("编译位压制 " + suppressed + " 个后台进程");
        }

        private void StartReconcileTimer()
        {
            lock (sync)
            {
                if (reconcileTimer != null) return;
                reconcileTimer = new Timer(
                    _ => ReconcileTick(), null, 30000, 30000);
            }
        }

        private void StopReconcileTimer()
        {
            Timer t;
            lock (sync)
            {
                t = reconcileTimer;
                reconcileTimer = null;
            }
            if (t != null) t.Dispose();
        }

        /// <summary>校正节拍：收敛副作用 + IDE 窗口条件复查。回调到达时可能已挂起，先检查。</summary>
        private void ReconcileTick()
        {
            lock (sync) { if (!granted) return; }
            try
            {
                // IDE 窗口条件复查（节流），无可见窗口的 IDE 不再维持场景活性
                RefreshIdeVisible(false);
                // 专注开关可能已被 WPF 宿主跨进程关闭：本进程没有进程事件时，靠节拍重算活性
                // 触发仲裁器挂起（还原 Notif 静默等副作用），避免开关失效延迟到下一个进程事件。
                RecomputeActivity();
                lock (sync) { if (!granted) return; }
                ReconcileSideEffects();   // 收敛器内含压制 sweep/服务暂停/静默的全部升降沿
                // 长编译/长专注期间增量追压新后台（旧节拍行为，收敛器升沿一次性 sweep 之外的持续追压）：
                // sweep 幂等（Acquire 对已压进程返回 AlreadyThrottled），与升沿共用 SuppressReason.Build
                bool buildNow; bool focusNow;
                lock (sync) { buildNow = activeBuildPids.Count > 0; }
                focusNow = FocusModeOn;
                if (buildNow || focusNow) SweepBuildSuppression();
                ReconcileIdeBoost();
                // 竞态护栏：挂起可能在收敛期间到达（granted 已翻 false），泄漏的压制立即回收；
                // 若挂起在护栏之后到达，Suspend 自带的 ReleaseReason(Build) 会兜底。
                lock (sync) { if (!granted && core != null) core.ReleaseReason(SuppressReason.Build); }
            }
            catch { }
        }

        /// <summary>IDE 可见窗口复查（2 秒节流）：有可见窗口的 IDE 才计入场景活性。
        /// 在进程事件、初始扫描完成与校正节拍中调用。</summary>
        private void RefreshIdeVisible(bool force)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (sync)
            {
                if (!force && now - lastIdeWindowCheckTicks < 2L * TimeSpan.TicksPerSecond) return;
                lastIdeWindowCheckTicks = now;
            }
            if (activeIdePids.Count == 0)
            {
                lock (sync) ideVisible = false;
                return;
            }
            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { return; }
            bool anyVisible = false;
            lock (sync)
            {
                foreach (int pid in activeIdePids)
                    if (visible.Contains(pid)) { anyVisible = true; break; }
                ideVisible = anyVisible;
            }
        }

        /// <summary>初始扫描完成后补算 IDE 可见窗口，避免无窗口的常驻 IDE 立即激活场景。</summary>
        protected override void OnInitialScanComplete()
        {
            RefreshIdeVisible(true);
        }

        private void ReconcileIdeBoost()
        {
            int[] ides;
            lock (sync)
            {
                ides = new int[activeIdePids.Count];
                activeIdePids.CopyTo(ides);
            }
            if (ides.Length == 0) { RestoreIdeBoost(); return; }

            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { visible = new HashSet<int>(); }

            bool anyVisible = false;
            foreach (int pid in ides) if (visible.Contains(pid)) { anyVisible = true; break; }
            lock (sync) { ideVisible = anyVisible; }
            if (!anyVisible) { RestoreIdeBoost(); return; }

            foreach (int pid in ides) BoostOneIde(pid);
        }

        private void BoostOneIde(int pid)
        {
            BoostOneWithSnapshot(() => granted, pid, Native.ABOVE_NORMAL_PRIORITY_CLASS,
                ideBoosted, ideBoostedCreation, ideBoostedName, ideBoostedIo);
        }

        internal void RestoreIdeBoost()
        {
            RestoreBoostSnapshot(ideBoosted, ideBoostedCreation, ideBoostedName, ideBoostedIo);
        }

        /// <summary>测试钩子：绕过窗口条件与掌权检查直接提优单个进程（返回是否入快照）</summary>
        internal bool BoostIdeForTest(int pid)
        {
            BoostOneWithSnapshot(() => true, pid, Native.ABOVE_NORMAL_PRIORITY_CLASS,
                ideBoosted, ideBoostedCreation, ideBoostedName, ideBoostedIo);
            lock (sync) return ideBoosted.ContainsKey(pid);
        }

        /// <summary>程序退出时调用，确保还原。仅在 ProcNotify 停止后调用（退出路径单线程）</summary>
        public void Stop()
        {
            bool wasReported;
            lock (sync)
            {
                wasReported = reported;
                reported = false;
                activeBuildPids.Clear();
                activeIdePids.Clear();
                distractNotified.Clear();
                blockBalloonTicks.Clear();
                buildPidLastProgress.Clear();
                buildPidCpuMs.Clear();
                buildPidName.Clear();
                buildPidUnreadable.Clear();
            }
            Timer st;
            lock (sync) { st = buildSampleTimer; buildSampleTimer = null; }
            if (st != null) st.Dispose();
            // 走仲裁器单一路径还原（若正掌权会回调 Suspend）
            if (wasReported) arbiter.ReportActivity(Kind, false);
        }
    }
}
