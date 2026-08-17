// @author zenjiro 18967498922@163.com
// 文件用途 WPF 三场景调度 ViewModel：场景总览 / 场景详情 / 只读状态源
//           预览宿主用「只读探测 + 无副作用代理」镜像生产仲裁器的优先级语义，
//           不创建真实 DevFocus/DailyCare 实例，因此不会暂停服务或压制后台。

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using System.Windows.Threading;

namespace CaelusApp
{
    internal sealed class ScenarioSourceRowViewModel : ViewModelBase
    {
        private string label = "";
        private string valueText = "";
        private bool live;

        public string Label { get { return label; } set { SetProperty(ref label, value, "Label"); } }
        public string ValueText { get { return valueText; } set { SetProperty(ref valueText, value, "ValueText"); } }
        public bool Live { get { return live; } set { SetProperty(ref live, value, "Live"); } }
    }

    internal sealed class ScenarioCardViewModel : ViewModelBase
    {
        private readonly ScenarioOverviewViewModel owner;
        private bool enabled;
        private bool isActive;
        private bool isGranted;
        private string stateText = "";
        private string stateKey = "Neutral";

        public ScenarioCardViewModel(ScenarioOverviewViewModel owner, ScenarioKind kind)
        {
            this.owner = owner;
            Kind = kind;
            switch (kind)
            {
                case ScenarioKind.Game:
                    Name = "游戏";
                    SubName = "游戏场景";
                    IconKey = "IconGame";
                    PriorityText = "优先级 1 · 最高";
                    TriggerText = "识别到游戏本体活跃（启动器换壳、临时目录真身均可认定）";
                    ActionText = "游戏优先拿 CPU / 磁盘 / 显卡调度，后台压制，退出后完整还原";
                    break;
                case ScenarioKind.DevFocus:
                    Name = "开发专注";
                    SubName = "开发场景";
                    IconKey = "IconCode";
                    PriorityText = "优先级 2";
                    TriggerText = "编译进程、IDE 家族或专注模式开关任一激活";
                    ActionText = "暂停索引服务、提优编译器与 IDE、静默通知，结束后还原";
                    break;
                default:
                    Name = "日常优化";
                    SubName = "日常场景";
                    IconKey = "IconDaily";
                    PriorityText = "优先级 3";
                    TriggerText = "浏览器 / Office / 会议家族活跃，或切换到电池供电";
                    ActionText = "常规档后台压制并提优前台家族；电池供电自动升档";
                    break;
            }
        }

        public ScenarioKind Kind { get; private set; }
        public string Name { get; private set; }
        public string SubName { get; private set; }
        public string IconKey { get; private set; }
        public string PriorityText { get; private set; }
        public string TriggerText { get; private set; }
        public string ActionText { get; private set; }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (!SetProperty(ref enabled, value, "Enabled")) return;
                Raise("EnabledText");
                owner.SetScenarioEnabled(Kind, value);
            }
        }

        public bool IsActive { get { return isActive; } private set { SetProperty(ref isActive, value, "IsActive"); } }
        public bool IsGranted { get { return isGranted; } private set { SetProperty(ref isGranted, value, "IsGranted"); } }
        public string EnabledText { get { return enabled ? "已启用" : "已关闭"; } }

        public string StateText
        {
            get { return stateText; }
            private set { SetProperty(ref stateText, value, "StateText"); }
        }

        public string StateKey
        {
            get { return stateKey; }
            private set { SetProperty(ref stateKey, value, "StateKey"); }
        }

        public void Apply(bool on, bool active, bool granted)
        {
            // 直接回填字段，避免把“来源刷新”误当成用户操作再写一次设置。
            SetProperty(ref enabled, on, "Enabled");
            Raise("EnabledText");
            IsActive = active;
            IsGranted = granted;
            if (!on)
            {
                StateText = "已关闭";
                StateKey = "Neutral";
            }
            else if (granted)
            {
                StateText = "掌权中";
                StateKey = "Success";
            }
            else if (active)
            {
                StateText = "活跃 · 等待掌权";
                StateKey = "Warning";
            }
            else
            {
                StateText = "待机";
                StateKey = "Neutral";
            }
        }
    }

    internal sealed class ScenarioOverviewViewModel : ViewModelBase
    {
        private readonly ScenarioStatusSource source;
        private AppMode mode;
        private string grantedTitle = "";
        private string grantedDetail = "";
        private string grantedIconKey = "IconInfo";
        private string grantedStateKey = "Neutral";
        private string activeSummary = "";
        private string modeText = "";
        private string refreshedText = "";
        private string cpuText = "—";
        private string gpuText = "—";
        private string memoryText = "—";
        private string hagsText = "—";

        public ScenarioOverviewViewModel(ScenarioStatusSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            this.source = source;
            mode = source.Mode;
            Cards = new ObservableCollection<ScenarioCardViewModel>();
            Cards.Add(new ScenarioCardViewModel(this, ScenarioKind.Game));
            Cards.Add(new ScenarioCardViewModel(this, ScenarioKind.DevFocus));
            Cards.Add(new ScenarioCardViewModel(this, ScenarioKind.DailyCare));
            source.Changed += OnSourceChanged;
        }

        public ObservableCollection<ScenarioCardViewModel> Cards { get; private set; }

        public string GrantedTitle { get { return grantedTitle; } private set { SetProperty(ref grantedTitle, value, "GrantedTitle"); } }
        public string GrantedDetail { get { return grantedDetail; } private set { SetProperty(ref grantedDetail, value, "GrantedDetail"); } }
        public string GrantedIconKey { get { return grantedIconKey; } private set { SetProperty(ref grantedIconKey, value, "GrantedIconKey"); } }
        public string GrantedStateKey { get { return grantedStateKey; } private set { SetProperty(ref grantedStateKey, value, "GrantedStateKey"); } }
        public string ActiveSummary { get { return activeSummary; } private set { SetProperty(ref activeSummary, value, "ActiveSummary"); } }
        public string ModeText { get { return modeText; } private set { SetProperty(ref modeText, value, "ModeText"); } }
        public string RefreshedText { get { return refreshedText; } private set { SetProperty(ref refreshedText, value, "RefreshedText"); } }

        public string CpuText { get { return cpuText; } private set { SetProperty(ref cpuText, value, "CpuText"); } }
        public string GpuText { get { return gpuText; } private set { SetProperty(ref gpuText, value, "GpuText"); } }
        public string MemoryText { get { return memoryText; } private set { SetProperty(ref memoryText, value, "MemoryText"); } }
        public string HagsText { get { return hagsText; } private set { SetProperty(ref hagsText, value, "HagsText"); } }

        public void SetMode(AppMode value)
        {
            mode = value;
            Refresh();
        }

        internal void SetScenarioEnabled(ScenarioKind kind, bool value)
        {
            source.SetEnabled(kind, value);
        }

        private void OnSourceChanged()
        {
            Refresh();
        }

        public void Refresh()
        {
            ScenarioCardViewModel game = Cards[0];
            ScenarioCardViewModel dev = Cards[1];
            ScenarioCardViewModel daily = Cards[2];
            game.Apply(source.GameEnabled, source.GameActive, source.Granted == ScenarioKind.Game);
            dev.Apply(source.DevEnabled, source.DevActive, source.Granted == ScenarioKind.DevFocus);
            daily.Apply(source.DailyEnabled, source.DailyActive, source.Granted == ScenarioKind.DailyCare);

            int active = (source.GameActive ? 1 : 0) + (source.DevActive ? 1 : 0) + (source.DailyActive ? 1 : 0);
            int enabled = (source.GameEnabled ? 1 : 0) + (source.DevEnabled ? 1 : 0) + (source.DailyEnabled ? 1 : 0);
            ActiveSummary = enabled + " 个场景已启用，" + active + " 个当前活跃";

            ScenarioKind? granted = source.Granted;
            if (granted.HasValue)
            {
                GrantedIconKey = IconKey(granted.Value);
                GrantedStateKey = "Success";
                if (granted.Value == ScenarioKind.Game)
                {
                    GrantedTitle = "当前掌权 · 游戏";
                    GrantedDetail = "游戏优先级最高。开发专注与日常优化如处于活跃状态，会被还原式挂起；游戏退出后自动补位。";
                }
                else if (granted.Value == ScenarioKind.DevFocus)
                {
                    GrantedTitle = "当前掌权 · 开发专注";
                    GrantedDetail = "编译 / IDE / 专注模式活跃。索引暂停、编译器提优与通知静默已生效，结束后按记录还原。";
                }
                else
                {
                    GrantedTitle = "当前掌权 · 日常优化";
                    GrantedDetail = "日常家族活跃或电池供电。后台按常规档压制，前台家族提优；高优先级场景出现时立即让位。";
                }
            }
            else
            {
                GrantedIconKey = "IconInfo";
                GrantedStateKey = "Neutral";
                GrantedTitle = "当前无场景掌权";
                GrantedDetail = "三个场景都在待机，系统保持默认状态。游戏、编译 / IDE、日常家族任一出现，最高优先级场景立即接管。";
            }

            ModeText = ModePalette.DisplayName(mode);
            RefreshedText = source.LastProbeText;
            LoadDeviceSpecs();
        }

        private static string IconKey(ScenarioKind kind)
        {
            return kind == ScenarioKind.Game ? "IconGame"
                : kind == ScenarioKind.DevFocus ? "IconCode" : "IconDaily";
        }

        private void LoadDeviceSpecs()
        {
            try
            {
                string[] specs = DeviceInfo.Specs();
                if (specs != null && specs.Length >= 4)
                {
                    CpuText = string.IsNullOrEmpty(specs[0]) ? "—" : specs[0];
                    GpuText = string.IsNullOrEmpty(specs[1]) ? "—" : specs[1];
                    MemoryText = string.IsNullOrEmpty(specs[2]) ? "—" : specs[2];
                    HagsText = string.IsNullOrEmpty(specs[3]) ? "—" : specs[3];
                }
            }
            catch { }
        }
    }

    internal sealed class ScenarioDetailViewModel : ViewModelBase
    {
        private readonly ScenarioStatusSource source;
        private readonly bool isDev;
        private bool enabled;
        private bool focusMode;
        private string stateText = "";
        private string stateKey = "Neutral";
        private string stateDetail = "";
        private string focusStatsText = "—";

        public ScenarioDetailViewModel(ScenarioStatusSource source, ScenarioKind kind)
        {
            if (source == null) throw new ArgumentNullException("source");
            this.source = source;
            isDev = kind == ScenarioKind.DevFocus;
            Kind = kind;
            if (isDev)
            {
                Title = "开发专注";
                Subtitle = "编译提速 · 专注免打扰 · 开发服务守护";
                IconKey = "IconCode";
                PriorityText = "优先级 2 / 3";
                TriggerText = "编译进程、IDE 家族或专注模式开关任一激活。";
                ActionText = "暂停索引服务、提优编译器与 IDE、静默通知；离开后按快照还原。";
            }
            else
            {
                Title = "日常优化";
                Subtitle = "浏览器 / Office / 会议 · 电池供电 · 计划维护";
                IconKey = "IconDaily";
                PriorityText = "优先级 3 / 3";
                TriggerText = "浏览器 / Office / 会议家族活跃，或切换到电池供电。";
                ActionText = "常规档后台压制并提优前台家族；电池供电自动升档；到点执行健康维护。";
            }
            SourceRows = new ObservableCollection<ScenarioSourceRowViewModel>();
            source.Changed += OnSourceChanged;
            Refresh();
        }

        public ScenarioKind Kind { get; private set; }
        public string Title { get; private set; }
        public string Subtitle { get; private set; }
        public string IconKey { get; private set; }
        public string PriorityText { get; private set; }
        public string TriggerText { get; private set; }
        public string ActionText { get; private set; }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (!SetProperty(ref enabled, value, "Enabled")) return;
                source.SetEnabled(Kind, value);
            }
        }

        public bool FocusMode
        {
            get { return focusMode; }
            set
            {
                if (!SetProperty(ref focusMode, value, "FocusMode")) return;
                source.SetFocusMode(value);
            }
        }

        public bool FocusModeVisible { get { return isDev; } }
        public bool FocusModeOn { get { return FocusMode; } }

        public string StateText { get { return stateText; } private set { SetProperty(ref stateText, value, "StateText"); } }
        public string StateKey { get { return stateKey; } private set { SetProperty(ref stateKey, value, "StateKey"); } }
        public string StateDetail { get { return stateDetail; } private set { SetProperty(ref stateDetail, value, "StateDetail"); } }
        public string FocusStatsText { get { return focusStatsText; } private set { SetProperty(ref focusStatsText, value, "FocusStatsText"); } }
        public ObservableCollection<ScenarioSourceRowViewModel> SourceRows { get; private set; }

        private void OnSourceChanged()
        {
            Refresh();
        }

        public void Refresh()
        {
            bool on;
            bool active;
            bool granted;
            if (isDev)
            {
                on = source.DevEnabled;
                active = source.DevActive;
                granted = source.Granted == ScenarioKind.DevFocus;
                focusMode = source.DevFocusSwitch;
                Raise("FocusMode");
                Raise("FocusModeOn");
                long seconds = FocusStats.TodaySeconds(DateTime.Now);
                int sessions = FocusStats.TodaySessions(DateTime.Now);
                FocusStatsText = sessions <= 0 && seconds <= 0
                    ? "今天还没有专注记录"
                    : "今天专注 " + FormatSeconds(seconds) + " · " + sessions + " 次会话";
            }
            else
            {
                on = source.DailyEnabled;
                active = source.DailyActive;
                granted = source.Granted == ScenarioKind.DailyCare;
            }

            enabled = on;
            Raise("Enabled");
            if (!on)
            {
                StateText = "已关闭";
                StateKey = "Neutral";
                StateDetail = "场景不会参与仲裁，也不会产生任何系统副作用。";
            }
            else if (granted)
            {
                StateText = "掌权中";
                StateKey = "Success";
                StateDetail = isDev
                    ? "索引已暂停、编译器与 IDE 已提优、通知已静默；活跃来源消失后按快照还原。"
                    : "后台按常规档压制、前台家族已提优；出现游戏或开发场景时立即还原式挂起。";
            }
            else if (active)
            {
                StateText = "活跃 · 等待掌权";
                StateKey = "Warning";
                StateDetail = "检测状态已保留，但被更高优先级场景抢占；高优先级场景退出后自动补位。";
            }
            else
            {
                StateText = "待机";
                StateKey = "Neutral";
                StateDetail = isDev
                    ? "等待编译进程、IDE 家族或专注模式开关出现。"
                    : "等待浏览器 / Office / 会议家族活跃或电池供电。";
            }

            RebuildRows();
        }

        private void RebuildRows()
        {
            SourceRows.Clear();
            if (isDev)
            {
                SourceRows.Add(Row("编译进程", source.DevBuildActive ? "检测到" : "未检测到", source.DevBuildActive));
                SourceRows.Add(Row("IDE 家族", source.DevIdeActive ? "检测到" : "未检测到", source.DevIdeActive));
                SourceRows.Add(Row("专注模式开关", source.DevFocusSwitch ? "开启" : "关闭", source.DevFocusSwitch));
            }
            else
            {
                string family = source.DailyFamilyActive && source.DailyFamilyNames.Length > 0
                    ? source.DailyFamilyNames : "未检测到";
                SourceRows.Add(Row("日常家族进程", family, source.DailyFamilyActive));
                SourceRows.Add(Row("电池供电", source.DailyOnBattery ? "是 · 自动升档" : "否", source.DailyOnBattery));
            }
        }

        private static ScenarioSourceRowViewModel Row(string label, string value, bool live)
        {
            return new ScenarioSourceRowViewModel { Label = label, ValueText = value, Live = live };
        }

        private static string FormatSeconds(long seconds)
        {
            long h = seconds / 3600;
            long m = (seconds % 3600) / 60;
            if (h > 0) return h + " 小时 " + m + " 分钟";
            return m + " 分钟";
        }
    }

    /// <summary>
    /// 只读场景状态源：每 2 秒读设置 + 进程名预筛 + 电池状态，
    /// 通过无副作用的 ProxyScenario 镜像生产仲裁器的优先级结果。
    /// </summary>
    internal sealed class ScenarioStatusSource : IDisposable
    {
        private sealed class ProxyScenario : IScenario
        {
            private readonly ScenarioKind kind;
            private readonly int priority;

            public ProxyScenario(ScenarioKind kind, int priority)
            {
                this.kind = kind;
                this.priority = priority;
            }

            public ScenarioKind Kind { get { return kind; } }
            public int Priority { get { return priority; } }
            public void Grant() { }
            public void Suspend() { }
        }

        private readonly GameMode gameMode;
        private readonly Tamer tamer;
        private readonly SuppressionCore core;
        private readonly DevFocus devFocus;
        private readonly DailyCare dailyCare;
        private readonly bool runtimeMode;
        private readonly ScenarioArbiter arbiter;
        private readonly bool[] reported = new bool[3];
        private readonly DispatcherTimer timer;
        private bool disposed;

        // 最近一次探测结果
        private bool gameEnabled;
        private bool gameActive;
        private bool devEnabled;
        private bool devActive;
        private bool dailyEnabled;
        private bool dailyActive;
        private bool devBuildActive;
        private bool devIdeActive;
        private bool devFocusSwitch;
        private bool dailyFamilyActive;
        private bool dailyOnBattery;
        private string dailyFamilyNames = "";
        private ScenarioKind? granted;
        private AppMode mode;
        private DateTime lastProcessScan = DateTime.MinValue;
        private bool scanDirty = true;

        // 截图/演示覆盖：只作用于活性，不写任何设置
        private bool? demoGame;
        private bool? demoDev;
        private bool? demoDaily;

        public event Action Changed;

        public ScenarioStatusSource(GameMode gameMode)
            : this(gameMode, null, null, null, null, null)
        {
        }

        /// <summary>正式运行时构造：传入 WpfRuntimeHost 的真实仲裁器/DevFocus/DailyCare，
        /// 场景状态直接读运行时结果；预览/截图路径传 null 时使用无副作用代理镜像。</summary>
        public ScenarioStatusSource(GameMode gameMode, Tamer tamer, SuppressionCore core,
            ScenarioArbiter arbiter, DevFocus devFocus, DailyCare dailyCare)
        {
            this.gameMode = gameMode;
            this.tamer = tamer;
            this.core = core;
            this.devFocus = devFocus;
            this.dailyCare = dailyCare;
            runtimeMode = arbiter != null;
            if (runtimeMode)
            {
                this.arbiter = arbiter;
            }
            else
            {
                this.arbiter = new ScenarioArbiter();
                this.arbiter.Register(new ProxyScenario(ScenarioKind.Game, 100));
                this.arbiter.Register(new ProxyScenario(ScenarioKind.DevFocus, 50));
                this.arbiter.Register(new ProxyScenario(ScenarioKind.DailyCare, 10));
            }
            try
            {
                mode = ModePalette.FromPreset(gameMode != null
                    ? gameMode.ActivePreset : PerformancePreset.Standard);
            }
            catch { mode = AppMode.Standard; }

            Poll();
            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += delegate { Poll(); };
            timer.Start();
        }

        public AppMode Mode { get { return mode; } }
        public ScenarioKind? Granted { get { return granted; } }

        public bool GameEnabled { get { return gameEnabled; } }
        public bool GameActive { get { return gameActive; } }
        public bool DevEnabled { get { return devEnabled; } }
        public bool DevActive { get { return devActive; } }
        public bool DailyEnabled { get { return dailyEnabled; } }
        public bool DailyActive { get { return dailyActive; } }

        public bool DevBuildActive { get { return devBuildActive; } }
        public bool DevIdeActive { get { return devIdeActive; } }
        public bool DevFocusSwitch { get { return devFocusSwitch; } }
        public bool DailyFamilyActive { get { return dailyFamilyActive; } }
        public bool DailyOnBattery { get { return dailyOnBattery; } }
        public string DailyFamilyNames { get { return dailyFamilyNames; } }

        public string LastProbeText
        {
            get
            {
                return runtimeMode
                    ? "状态每 2 秒刷新 · 已接入 WPF 运行时（真实仲裁器 / 进程事件 / 电池）"
                    : "状态每 2 秒刷新 · 只读探测（设置 + 进程名 + 电池），预览宿主不施加系统副作用";
            }
        }

        public void SetMode(AppMode value)
        {
            if (mode == value) return;
            mode = value;
            RaiseChanged();
        }

        public void SetEnabled(ScenarioKind kind, bool value)
        {
            if (kind == ScenarioKind.Game)
            {
                if (gameMode != null) gameMode.Enabled = value;
                Settings.Save("GameModeOn", value);
            }
            else if (kind == ScenarioKind.DevFocus)
            {
                Settings.Save("DevModeOn", value);
            }
            else
            {
                Settings.Save("DailyCareOn", value);
            }
            scanDirty = true;
            Poll();
        }

        public void SetFocusMode(bool value)
        {
            if (runtimeMode && devFocus != null)
            {
                devFocus.SetFocusMode(value);
            }
            else
            {
                Settings.Save("DevFocusModeOn", value);
            }
            scanDirty = true;
            Poll();
        }

        public void SetDemo(bool? gameActive, bool? devActive, bool? dailyActive)
        {
            // 正式运行时状态由真实仲裁器决定，不允许演示覆盖。
            if (runtimeMode) return;
            demoGame = gameActive;
            demoDev = devActive;
            demoDaily = dailyActive;
            Poll();
        }

        public void Poll()
        {
            if (disposed) return;

            bool prevGameEnabled = gameEnabled;
            bool prevGameActive = gameActive;
            bool prevDevEnabled = devEnabled;
            bool prevDevActive = devActive;
            bool prevDailyEnabled = dailyEnabled;
            bool prevDailyActive = dailyActive;
            bool prevDevBuild = devBuildActive;
            bool prevDevIde = devIdeActive;
            bool prevDevFocus = devFocusSwitch;
            bool prevDailyFamily = dailyFamilyActive;
            bool prevDailyBattery = dailyOnBattery;
            string prevNames = dailyFamilyNames;
            ScenarioKind? prevGranted = granted;

            if (runtimeMode)
            {
                // 正式运行时：直接读真实仲裁器与场景实例，不重复报告活性。
                gameEnabled = gameMode == null ? Settings.Load("GameModeOn", true) : gameMode.Enabled;
                devEnabled = Settings.Load("DevModeOn", true);
                dailyEnabled = Settings.Load("DailyCareOn", true);
                devFocusSwitch = devFocus != null && devFocus.FocusModeOn;

                try
                {
                    dailyOnBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
                }
                catch { dailyOnBattery = false; }

                bool needScan = scanDirty || (DateTime.UtcNow - lastProcessScan).TotalSeconds >= 3;
                if (needScan) ScanProcesses();

                gameActive = gameEnabled && gameMode != null && gameMode.IsActive;
                devActive = devEnabled && devFocus != null && devFocus.IsActive;
                dailyActive = dailyEnabled && dailyCare != null && dailyCare.IsActive;
                granted = arbiter.CurrentGranted;
            }
            else
            {
                gameEnabled = gameMode == null ? Settings.Load("GameModeOn", true) : gameMode.Enabled;
                devEnabled = Settings.Load("DevModeOn", true);
                dailyEnabled = Settings.Load("DailyCareOn", true);
                devFocusSwitch = Settings.Load("DevFocusModeOn", false);

                try
                {
                    dailyOnBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
                }
                catch { dailyOnBattery = false; }

                bool needScan = scanDirty || (DateTime.UtcNow - lastProcessScan).TotalSeconds >= 3;
                if (needScan) ScanProcesses();

                gameActive = gameEnabled && (demoGame.HasValue
                    ? demoGame.Value
                    : (gameMode != null && gameMode.IsActive));
                devActive = devEnabled && (demoDev.HasValue
                    ? demoDev.Value
                    : (devFocusSwitch || devBuildActive || devIdeActive));
                dailyActive = dailyEnabled && (demoDaily.HasValue
                    ? demoDaily.Value
                    : (dailyFamilyActive || dailyOnBattery));

                Report(0, ScenarioKind.Game, gameActive);
                Report(1, ScenarioKind.DevFocus, devActive);
                Report(2, ScenarioKind.DailyCare, dailyActive);
                granted = arbiter.CurrentGranted;
            }

            if (prevGameEnabled != gameEnabled || prevGameActive != gameActive
                || prevDevEnabled != devEnabled || prevDevActive != devActive
                || prevDailyEnabled != dailyEnabled || prevDailyActive != dailyActive
                || prevDevBuild != devBuildActive || prevDevIde != devIdeActive
                || prevDevFocus != devFocusSwitch || prevDailyFamily != dailyFamilyActive
                || prevDailyBattery != dailyOnBattery || prevNames != dailyFamilyNames
                || !Eq(prevGranted, granted))
                RaiseChanged();
        }

        private void Report(int index, ScenarioKind kind, bool active)
        {
            if (reported[index] == active) return;
            arbiter.ReportActivity(kind, active);
            reported[index] = active;
        }

        private void ScanProcesses()
        {
            scanDirty = false;
            lastProcessScan = DateTime.UtcNow;
            bool build = false;
            bool ide = false;
            bool family = false;
            var names = new StringBuilder();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    string name = null;
                    try { name = p.ProcessName; }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                    if (string.IsNullOrEmpty(name)) continue;

                    if (BuildCatalog.IsMatch(name)) build = true;
                    if (IdeCatalog.NameMatches(name)) ide = true;
                    if (DailyCatalog.NameMatches(name))
                    {
                        family = true;
                        if (names.Length > 0) names.Append(" · ");
                        names.Append(name);
                    }
                }
            }
            catch { }
            devBuildActive = build;
            devIdeActive = ide;
            dailyFamilyActive = family;
            dailyFamilyNames = names.ToString();
        }

        private static bool Eq(ScenarioKind? a, ScenarioKind? b)
        {
            return a.HasValue ? (b.HasValue && a.Value == b.Value) : !b.HasValue;
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                try { handler(); }
                catch { }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (timer != null)
            {
                try { timer.Stop(); } catch { }
            }
        }
    }
}
