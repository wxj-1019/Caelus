// @author zenjiro 18967498922@163.com
// 文件用途 实时监控页 ViewModel：状态瓦片 / 今日统计 / 实时压制与提优清单 / 最近动作流，2 秒自刷新

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace CaelusApp
{
    internal sealed class ActivityStatusRow
    {
        public string Label { get; set; }
        public string StateText { get; set; }
        public string StateKey { get; set; }   // Success/Warning/Neutral → XAML 徽章色
        public string Detail { get; set; }
    }

    internal sealed class SuppressedRowVm
    {
        public string Name { get; set; }
        public string PidText { get; set; }
        public string LevelText { get; set; }
        public string LevelKey { get; set; }       // Neutral/Info/Warning/Error → 档位胶囊色
        public List<string> ReasonList { get; set; }
        public string DurationText { get; set; }
    }

    internal sealed class ActivityStatCell
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    internal sealed class ActivityFeedRow
    {
        public string TimeText { get; set; }
        public string Text { get; set; }
    }

    internal sealed class ActivityViewModel : ViewModelBase
    {
        private readonly ScenarioStatusSource source;
        private readonly GameMode gameMode;
        private readonly DevFocus devFocus;    // 预览/探针模式为 null
        private readonly DailyCare dailyCare;  // 同上
        private readonly SuppressionCore core;
        private readonly DispatcherTimer timer;

        public ObservableCollection<ActivityStatusRow> StatusRows { get; private set; }
        public ObservableCollection<SuppressedRowVm> SuppressedRows { get; private set; }
        public ObservableCollection<string> BoostRows { get; private set; }
        public ObservableCollection<ActivityFeedRow> FeedRows { get; private set; }

        private string suppressedHeader = "";
        private string boostHeader = "";

        public ObservableCollection<ActivityStatCell> StatCells { get; private set; }
        public string SuppressedHeader { get { return suppressedHeader; } private set { SetProperty(ref suppressedHeader, value, "SuppressedHeader"); } }
        public string BoostHeader { get { return boostHeader; } private set { SetProperty(ref boostHeader, value, "BoostHeader"); } }
        private bool suppressedEmpty;
        private bool boostEmpty;
        public bool SuppressedEmpty { get { return suppressedEmpty; } private set { SetProperty(ref suppressedEmpty, value, "SuppressedEmpty"); } }
        public bool BoostEmpty { get { return boostEmpty; } private set { SetProperty(ref boostEmpty, value, "BoostEmpty"); } }
        public bool SuppressedAny { get { return !suppressedEmpty; } }
        public bool BoostAny { get { return !boostEmpty; } }

        public ActivityViewModel(ScenarioStatusSource source, GameMode gameMode,
            DevFocus devFocus, DailyCare dailyCare)
        {
            if (source == null) throw new ArgumentNullException("source");
            this.source = source;
            this.gameMode = gameMode;
            this.devFocus = devFocus;
            this.dailyCare = dailyCare;
            this.core = gameMode != null ? gameMode.Core : null;
            StatusRows = new ObservableCollection<ActivityStatusRow>();
            StatCells = new ObservableCollection<ActivityStatCell>();
            SuppressedRows = new ObservableCollection<SuppressedRowVm>();
            BoostRows = new ObservableCollection<string>();
            FeedRows = new ObservableCollection<ActivityFeedRow>();

            if (WpfHost.Views.ActivityView.InjectSampleData)
            {
                LoadSample();
                return;   // 探针下不挂计时器
            }
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += delegate { Refresh(false); };
            timer.Start();
            Refresh(true);
        }

        /// <summary>导航到本页时由窗口调用：立即刷新一帧（计时器之外的首次进入也即时）。</summary>
        public void Refresh(bool force)
        {
            // 探针样例态不重建行——导航会触发 Refresh(true)，否则样例数据被真实（空）数据覆盖
            if (WpfHost.Views.ActivityView.InjectSampleData) return;
            DateTime now = DateTime.Now;

            // —— 状态瓦片 ——
            StatusRows.Clear();
            bool gameOn = gameMode != null && gameMode.ActiveGame != null;
            bool gameGranted = source.Granted == ScenarioKind.Game;
            StatusRows.Add(new ActivityStatusRow
            {
                Label = "游戏",
                StateText = gameGranted ? "掌权中" : gameOn ? "运行中" : "未检测",
                StateKey = gameGranted ? "Success" : gameOn ? "Warning" : "Neutral",
                Detail = gameOn ? "游戏会话进行中" : "等待游戏启动"
            });
            StatusRows.Add(new ActivityStatusRow
            {
                Label = "开发专注",
                StateText = !source.DevEnabled ? "已关闭" : source.Granted == ScenarioKind.DevFocus ? "掌权中"
                    : source.DevActive ? "活跃 · 等待掌权" : "待机",
                StateKey = source.Granted == ScenarioKind.DevFocus ? "Success"
                    : source.DevActive ? "Warning" : "Neutral",
                Detail = devFocus == null ? "预览模式"
                    : "编译 " + devFocus.BuildActivityCount + " · IDE " + devFocus.IdeActivityCount
                        + " · 专注" + (devFocus.FocusModeOn ? "开" : "关")
            });
            StatusRows.Add(new ActivityStatusRow
            {
                Label = "日常优化",
                StateText = !source.DailyEnabled ? "已关闭" : source.Granted == ScenarioKind.DailyCare ? "掌权中"
                    : source.DailyActive ? "活跃 · 等待掌权" : "待机",
                StateKey = source.Granted == ScenarioKind.DailyCare ? "Success"
                    : source.DailyActive ? "Warning" : "Neutral",
                Detail = dailyCare == null ? "预览模式"
                    : "家族窗口 " + (dailyCare.FamilyVisibleNow ? "可见" : "无") + " · 电池 " + (dailyCare.OnBatteryNow ? "供电" : "市电")
            });

            // —— 今日统计（大数字格）——
            StatCells.Clear();
            StatCells.Add(new ActivityStatCell { Label = "今日专注", Value = FormatDur(FocusStats.TodaySeconds(now)) });
            StatCells.Add(new ActivityStatCell { Label = "编译会话", Value = FocusStats.TodayBuildSessions(now) + " 次" });
            StatCells.Add(new ActivityStatCell { Label = "日常家族", Value = FormatDur(DailyStats.TodaySeconds(now)) });
            StatCells.Add(new ActivityStatCell { Label = "分心命中", Value = FocusStats.TodayDistract(now) + " 次" });

            // —— 实时压制清单 ——
            SuppressedRows.Clear();
            int supCount = 0;
            if (core != null)
            {
                List<SuppressionCore.SuppressedRow> rows = core.SnapshotRows();
                rows.Sort(delegate (SuppressionCore.SuppressedRow a, SuppressionCore.SuppressedRow b)
                {
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                supCount = rows.Count;
                long nowTicks = DateTime.UtcNow.Ticks;
                foreach (SuppressionCore.SuppressedRow r in rows)
                {
                    var reasons = new List<string>(r.ReasonsText.Split('·'));
                    SuppressedRows.Add(new SuppressedRowVm
                    {
                        Name = r.Name,
                        PidText = r.Pid.ToString(),
                        LevelText = r.LevelText,
                        LevelKey = LevelKeyOf(r.LevelText),
                        ReasonList = reasons,
                        DurationText = r.AcquiredTicks > 0 ? FormatDur((nowTicks - r.AcquiredTicks) / TimeSpan.TicksPerSecond) : "—"
                    });
                }
            }
            SuppressedHeader = "实时压制 · " + supCount + " 个进程";
            SuppressedEmpty = supCount == 0;
            Raise("SuppressedAny");

            // —— 实时提优清单 ——
            BoostRows.Clear();
            if (devFocus != null)
                foreach (string s in devFocus.DescribeBoosts()) BoostRows.Add(s);
            if (dailyCare != null)
                foreach (string s in dailyCare.DescribeBoosts()) BoostRows.Add(s);
            if (gameOn && gameMode.ActiveGame != null) BoostRows.Add(gameMode.ActiveGame + "（游戏提优 High）");
            BoostHeader = "实时提优 · " + BoostRows.Count + " 个进程";
            BoostEmpty = BoostRows.Count == 0;
            Raise("BoostAny");

            // —— 最近动作流 ——
            FeedRows.Clear();
            foreach (ActivityEntry e in ActivityLog.Recent(30))
            {
                FeedRows.Add(new ActivityFeedRow
                {
                    TimeText = new DateTime(e.Ticks).ToLocalTime().ToString("HH:mm:ss"),
                    Text = e.Text
                });
            }
        }

        private static string LevelKeyOf(string levelText)
        {
            switch (levelText)
            {
                case "克制": return "Info";
                case "隔离": return "Warning";
                case "冻结": return "Error";
                default: return "Neutral";
            }
        }

        private static string FormatDur(long sec)
        {
            if (sec < 60) return sec + " 秒";
            long h = sec / 3600;
            long m = (sec % 3600) / 60;
            if (h > 0) return m > 0 ? h + " 小时 " + m + " 分钟" : h + " 小时";
            return m + " 分钟";
        }

        /// <summary>截图探针样例：与真实布局一致的演示数据。</summary>
        private void LoadSample()
        {
            StatusRows.Add(new ActivityStatusRow { Label = "游戏", StateText = "未检测", StateKey = "Neutral", Detail = "等待游戏启动" });
            StatusRows.Add(new ActivityStatusRow { Label = "开发专注", StateText = "掌权中", StateKey = "Success", Detail = "编译 2 · IDE 1 · 专注开" });
            StatusRows.Add(new ActivityStatusRow { Label = "日常优化", StateText = "待机", StateKey = "Neutral", Detail = "家族窗口 无 · 电池 市电" });
            StatCells.Add(new ActivityStatCell { Label = "今日专注", Value = "2 小时 21 分" });
            StatCells.Add(new ActivityStatCell { Label = "编译会话", Value = "4 次" });
            StatCells.Add(new ActivityStatCell { Label = "日常家族", Value = "5 小时 30 分" });
            StatCells.Add(new ActivityStatCell { Label = "分心命中", Value = "3 次" });
            SuppressedHeader = "实时压制 · 4 个进程";
            SuppressedEmpty = false;
            SuppressedRows.Add(new SuppressedRowVm { Name = "SearchIndexer", PidText = "8124", LevelText = "常规", LevelKey = "Neutral", ReasonList = new List<string> { "编译" }, DurationText = "12 分钟" });
            SuppressedRows.Add(new SuppressedRowVm { Name = "OneDrive", PidText = "6612", LevelText = "常规", LevelKey = "Neutral", ReasonList = new List<string> { "编译", "后台" }, DurationText = "3 分钟" });
            SuppressedRows.Add(new SuppressedRowVm { Name = "msiexec", PidText = "9900", LevelText = "克制", LevelKey = "Info", ReasonList = new List<string> { "后台" }, DurationText = "1 分钟" });
            SuppressedRows.Add(new SuppressedRowVm { Name = "svchost", PidText = "4212", LevelText = "常规", LevelKey = "Neutral", ReasonList = new List<string> { "后台" }, DurationText = "38 秒" });
            BoostHeader = "实时提优 · 3 个进程";
            BoostEmpty = false;
            BoostRows.Add("devenv（IDE 提优 AboveNormal）");
            BoostRows.Add("msbuild（编译提优 High）");
            BoostRows.Add("chrome（日常家族提优 AboveNormal）");
            FeedRows.Add(new ActivityFeedRow { TimeText = "14:02:11", Text = "开发专注掌权（编译=True 专注=True IDE=False）" });
            FeedRows.Add(new ActivityFeedRow { TimeText = "14:01:47", Text = "编译位压制 4 个后台进程" });
            FeedRows.Add(new ActivityFeedRow { TimeText = "13:58:03", Text = "分心应用已阻断关闭" });
            FeedRows.Add(new ActivityFeedRow { TimeText = "13:30:00", Text = "健康维护自动执行完成（成功 2 项）" });
        }
    }
}
