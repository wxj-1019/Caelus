// @author zenjiro 18967498922@163.com
// 文件用途 概览页 ViewModel 与数据源抽象（规格 §5.1 结论优先 + 渐进披露）

using System;
using System.Collections.ObjectModel;

namespace CaelusApp
{
    internal interface IOverviewSource
    {
        bool GuardEnabled { get; }
        bool GameActive { get; }
        bool HasWarning { get; }
        bool HasCritical { get; }
        double? GpuTempC { get; }
        double? MemoryUsedPct { get; }
        string MemoryUsedText { get; }
        string ModeText { get; }
        string LastCheckText { get; }
        System.Collections.Generic.IList<double> TempHistory { get; }
        // 状态图块（游戏 / 后台压制 / 环境）
        string ActiveGameText { get; }
        int SuppressedCount { get; }
        string PowerText { get; }
        string ThrottleText { get; }
        bool HasThrottleText { get; }
        string GrantSuffix { get; }
    }

    // 指标行支持原地更新（集合不重建，进度条元素不重播入场动画）
    internal sealed class MetricViewModel : ViewModelBase
    {
        private string label;
        private string valueText;
        private double fraction;
        private string colorKey;

        public string Label { get { return label; } set { SetProperty(ref label, value, "Label"); } }
        public string ValueText { get { return valueText; } set { SetProperty(ref valueText, value, "ValueText"); } }
        public double Fraction { get { return fraction; } set { SetProperty(ref fraction, value, "Fraction"); } }      // 0..1，进度条；不可用时为 0
        public string ColorKey { get { return colorKey; } set { SetProperty(ref colorKey, value, "ColorKey"); } }      // Success / Warning / Danger / Info
    }

    internal sealed class OverviewViewModel : ViewModelBase
    {
        private readonly IOverviewSource source;
        private readonly GameMode gameMode;
        private string conclusionTitle = "";
        private string conclusionDetail = "";
        private string conclusionGlyph = "";
        private string conclusionColorKey = "Info";
        private string modeText = "";
        private string lastCheckText = "";
        private bool detailVisible;
        private string cpuText = "…";
        private string gpuText = "…";
        private string memoryText = "…";
        private string osText = "";

        public OverviewViewModel(IOverviewSource source) : this(source, null) { }

        public OverviewViewModel(IOverviewSource source, GameMode gameMode)
        {
            this.source = source;
            this.gameMode = gameMode;
            Metrics = new ObservableCollection<MetricViewModel>();
            ToggleDetailCommand = new RelayCommand(
                () => { DetailVisible = !DetailVisible; });
            ToggleGuardCommand = new RelayCommand(
                delegate
                {
                    if (gameMode == null) return;
                    gameMode.Enabled = !gameMode.Enabled;
                    Settings.Save("GameModeOn", gameMode.Enabled);
                    Raise("GuardEnabled");
                    Refresh();
                });
            LoadDeviceInfo();
        }

        public ObservableCollection<MetricViewModel> Metrics { get; private set; }
        public RelayCommand ToggleDetailCommand { get; private set; }
        public RelayCommand ToggleGuardCommand { get; private set; }

        // UI 线程调度钩子：WPF 宿主注入（WinForms/自测构建不设置，后台结果直接丢弃）。
        // UiShared 不直接引用 PresentationFramework，保证旧 WinForms 构建（纯 csc）仍可编译。
        public static Action<Action> PostToUi;

        // 概览页守护总开关（与旧 WinForms 概览页 swGame 一致）
        public bool GuardEnabled
        {
            get { return gameMode != null ? gameMode.Enabled : source.GuardEnabled; }
        }

        // 是否示例数据源（截图探针）；正式运行时为 false，用于切换界面上的预览文案
        public bool IsSample { get { return source is SampleOverviewSource; } }

        // 游戏提优加速状态（与旧 WinForms 的 lblOverviewBoost 一致）
        public string BoostText
        {
            get { return gameMode == null ? null : gameMode.BoostStatusText; }
        }
        public bool HasBoostText { get { return !string.IsNullOrEmpty(BoostText); } }

        // —— 状态图块（游戏 / 后台压制 / 环境）——
        public string ActiveGameText { get { return source.ActiveGameText; } }
        public bool InGame { get { return source.GameActive; } }
        public string SuppressedCountText { get { return source.SuppressedCount.ToString(); } }
        public string PowerText { get { return source.PowerText; } }
        public string ThrottleText { get { return source.ThrottleText; } }
        public bool HasThrottleText { get { return source.HasThrottleText; } }
        public string GrantSuffix { get { return source.GrantSuffix; } }

        // CPU 拓扑摘要（多重群组 / 大小核 / X3D / 通用核数）
        public string CpuTopologyText
        {
            get
            {
                if (CpuTopology.MultiGroup) return Lang.T("v14.cpu.multigroup");
                if (CpuTopology.Hybrid) return Lang.T("v14.cpu.hybrid");
                if (CpuTopology.AsymCache) return Lang.T("v14.cpu.x3d");
                return Lang.F("v14.cpu.generic", Environment.ProcessorCount);
            }
        }

        // 硬件摘要（DeviceInfo.Specs：CPU / GPU / 内存 / HAGS）
        public string CpuText { get { return cpuText; } private set { SetProperty(ref cpuText, value, "CpuText"); } }
        public string GpuText { get { return gpuText; } private set { SetProperty(ref gpuText, value, "GpuText"); } }
        public string MemoryText { get { return memoryText; } private set { SetProperty(ref memoryText, value, "MemoryText"); } }
        public string OsText { get { return osText; } private set { SetProperty(ref osText, value, "OsText"); } }

        private void LoadDeviceInfo()
        {
            try
            {
                string[] fast = DeviceInfo.Specs();
                if (fast != null && fast.Length >= 4)
                {
                    CpuText = fast[0]; GpuText = fast[1]; MemoryText = fast[2]; OsText = fast[3];
                }
                if (fast == null || fast.Length < 4 || fast[1] == "—")
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            string[] full = DeviceInfo.SpecsWithSlowFallback();
                            if (full == null || full.Length < 4) return;
                            Action<Action> post = PostToUi;
                            if (post == null) return;
                            post(delegate
                            {
                                CpuText = full[0]; GpuText = full[1]; MemoryText = full[2]; OsText = full[3];
                            });
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        private System.Collections.Generic.IList<double> gpuTempSeries;
        public System.Collections.Generic.IList<double> GpuTempSeries
        {
            get { return gpuTempSeries; }
            private set { SetProperty(ref gpuTempSeries, value, "GpuTempSeries"); }
        }

        public string ConclusionTitle
        {
            get { return conclusionTitle; }
            private set { SetProperty(ref conclusionTitle, value, "ConclusionTitle"); }
        }

        public string ConclusionDetail
        {
            get { return conclusionDetail; }
            private set { SetProperty(ref conclusionDetail, value, "ConclusionDetail"); }
        }

        public string ConclusionGlyph
        {
            get { return conclusionGlyph; }
            private set { SetProperty(ref conclusionGlyph, value, "ConclusionGlyph"); }
        }

        public string ConclusionColorKey
        {
            get { return conclusionColorKey; }
            private set { SetProperty(ref conclusionColorKey, value, "ConclusionColorKey"); }
        }

        public string ModeText
        {
            get { return modeText; }
            private set { SetProperty(ref modeText, value, "ModeText"); }
        }

        public string LastCheckText
        {
            get { return lastCheckText; }
            private set { SetProperty(ref lastCheckText, value, "LastCheckText"); }
        }

        public bool DetailVisible
        {
            get { return detailVisible; }
            private set { SetProperty(ref detailVisible, value, "DetailVisible"); }
        }

        public void Refresh()
        {
            StatusConclusion c = OverviewStatus.Conclude(
                source.GuardEnabled, source.GameActive, source.HasWarning, source.HasCritical);
            ConclusionTitle = c.Title;
            ConclusionDetail = c.Detail;
            ConclusionGlyph = c.Glyph;
            ConclusionColorKey = OverviewStatus.ColorKey(c.Level);
            ModeText = source.ModeText ?? "";
            LastCheckText = source.LastCheckText ?? "";

            // 指标行只建一次，之后原地更新数值：重建集合会让进度条元素
            // 重新触发 Loaded 入场生长动画，表现为进度条每 2 秒闪跳一次。
            MetricViewModel temp = TempMetric("GPU 温度", source.GpuTempC, "°",
                OverviewStatus.GpuWarnC, OverviewStatus.GpuCritC, 110.0);
            MetricViewModel fps = new MetricViewModel
            {
                Label = "目标帧率",
                ValueText = TargetFpsText(),
                Fraction = 0,
                ColorKey = "Info"
            };
            MetricViewModel memory = MemoryMetric(source);
            if (Metrics.Count == 0)
            {
                Metrics.Add(temp);
                Metrics.Add(fps);
                Metrics.Add(memory);
            }
            else if (Metrics.Count >= 3)
            {
                CopyMetric(temp, Metrics[0]);
                CopyMetric(fps, Metrics[1]);
                CopyMetric(memory, Metrics[2]);
            }
            GpuTempSeries = source.TempHistory;
            Raise("BoostText");
            Raise("HasBoostText");
            Raise("CpuTopologyText");
            Raise("ActiveGameText");
            Raise("InGame");
            Raise("SuppressedCountText");
            Raise("PowerText");
            Raise("ThrottleText");
            Raise("HasThrottleText");
            Raise("GrantSuffix");
        }

        private static void CopyMetric(MetricViewModel from, MetricViewModel to)
        {
            to.Label = from.Label;
            to.ValueText = from.ValueText;
            to.Fraction = from.Fraction;
            to.ColorKey = from.ColorKey;
        }

        // 目标帧率：读当前模式生效的 NVIDIA 帧率上限 / AMD Chill 档位
        private string TargetFpsText()
        {
            if (gameMode == null) return "—";
            string mode = gameMode.NvFrlMode;
            bool nvOn = !string.IsNullOrEmpty(mode) && mode != "off";
            if (!nvOn) mode = gameMode.AmdChillMode;
            if (string.IsNullOrEmpty(mode) || mode == "off") return "无上限";
            if (mode == "screen") return "屏幕刷新率";
            if (mode == "screen-3") return "屏幕刷新率 − 3";
            return mode + " FPS";
        }

        private static MetricViewModel TempMetric(
            string label, double? value, string unit,
            double warnAt, double critAt, double scaleMax)
        {
            if (!value.HasValue)
                return new MetricViewModel { Label = label, ValueText = "—", Fraction = 0, ColorKey = "Info" };
            double v = value.Value;
            MetricLevel lv = OverviewStatus.LevelFor(v, warnAt, critAt);
            return new MetricViewModel
            {
                Label = label,
                ValueText = ((int)System.Math.Round(v)) + unit,
                Fraction = v / scaleMax > 1 ? 1 : v / scaleMax,
                ColorKey = lv == MetricLevel.Ok ? "Success"
                    : lv == MetricLevel.Warning ? "Warning" : "Danger"
            };
        }

        private static MetricViewModel MemoryMetric(IOverviewSource src)
        {
            if (!src.MemoryUsedPct.HasValue || src.MemoryUsedText == null)
                return new MetricViewModel { Label = "已用内存", ValueText = "—", Fraction = 0, ColorKey = "Info" };
            double pct = src.MemoryUsedPct.Value;
            // 取整百分比：读数微抖（如 53.4→53.6）不再引起进度条每 2 秒微调
            double roundedPct = System.Math.Round(pct);
            MetricLevel lv = OverviewStatus.LevelFor(
                roundedPct, OverviewStatus.MemWarnPct, OverviewStatus.MemCritPct);
            return new MetricViewModel
            {
                Label = "已用内存",
                ValueText = src.MemoryUsedText,
                Fraction = roundedPct / 100.0 > 1 ? 1 : roundedPct / 100.0,
                ColorKey = lv == MetricLevel.Ok ? "Success"
                    : lv == MetricLevel.Warning ? "Warning" : "Danger"
            };
        }
    }

    // 示例数据源：供 --wpf-shot 截图与手动预览使用
    internal sealed class SampleOverviewSource : IOverviewSource
    {
        private string modeText = "常规";

        public bool GuardEnabled { get { return true; } }
        public bool GameActive { get { return false; } }
        public bool HasWarning { get { return false; } }
        public bool HasCritical { get { return false; } }
        public double? GpuTempC { get { return 62; } }
        public double? MemoryUsedPct { get { return 53; } }
        public string MemoryUsedText { get { return "8.4 GB"; } }
        public string ModeText { get { return modeText; } }
        public string LastCheckText { get { return "上次检查 2 分钟前 · 没有需要处理的问题"; } }
        public string ActiveGameText { get { return "未在游戏中"; } }
        public int SuppressedCount { get { return 3; } }
        public string PowerText { get { return "市电"; } }
        public string ThrottleText { get { return null; } }
        public bool HasThrottleText { get { return false; } }
        public string GrantSuffix { get { return ""; } }

        private System.Collections.Generic.IList<double> tempHistory;
        public System.Collections.Generic.IList<double> TempHistory
        {
            get
            {
                if (tempHistory == null)
                {
                    var rng = new System.Random(20260811);
                    var list = new System.Collections.Generic.List<double>(24);
                    double v = 58;
                    for (int i = 0; i < 24; i++)
                    {
                        v += rng.NextDouble() * 4 - 2;
                        if (v < 54) v = 54;
                        if (v > 66) v = 66;
                        list.Add(v);
                    }
                    tempHistory = list;
                }
                return tempHistory;
            }
        }

        // 预览宿主模式切换时更新显示文案（竞技/自定义下示例结论同步变化）
        public void SetMode(AppMode mode)
        {
            modeText = ModePalette.DisplayName(mode);
        }
    }
}
