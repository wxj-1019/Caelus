// @author zenjiro 18967498922@163.com
// 文件用途 概览页 ViewModel 与数据源抽象（规格 §5.1 结论优先 + 渐进披露）

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
    }

    internal sealed class MetricViewModel
    {
        public string Label;
        public string ValueText;
        public double Fraction;      // 0..1，进度条；不可用时为 0
        public string ColorKey;      // Success / Warning / Danger / Info
    }

    internal sealed class OverviewViewModel : ViewModelBase
    {
        private readonly IOverviewSource source;
        private string conclusionTitle = "";
        private string conclusionDetail = "";
        private string conclusionGlyph = "";
        private string conclusionColorKey = "Info";
        private string modeText = "";
        private string lastCheckText = "";
        private bool detailVisible;

        public OverviewViewModel(IOverviewSource source)
        {
            this.source = source;
            Metrics = new ObservableCollection<MetricViewModel>();
            ToggleDetailCommand = new RelayCommand(
                () => { DetailVisible = !DetailVisible; });
        }

        public ObservableCollection<MetricViewModel> Metrics { get; private set; }
        public RelayCommand ToggleDetailCommand { get; private set; }

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

            Metrics.Clear();
            Metrics.Add(TempMetric("GPU 温度", source.GpuTempC, "°",
                OverviewStatus.GpuWarnC, OverviewStatus.GpuCritC, 110.0));
            Metrics.Add(new MetricViewModel
            {
                Label = "目标帧率",
                ValueText = "—",
                Fraction = 0,
                ColorKey = "Info"
            });
            Metrics.Add(MemoryMetric(source));
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
            MetricLevel lv = OverviewStatus.LevelFor(
                pct, OverviewStatus.MemWarnPct, OverviewStatus.MemCritPct);
            return new MetricViewModel
            {
                Label = "已用内存",
                ValueText = src.MemoryUsedText,
                Fraction = pct / 100.0 > 1 ? 1 : pct / 100.0,
                ColorKey = lv == MetricLevel.Ok ? "Success"
                    : lv == MetricLevel.Warning ? "Warning" : "Danger"
            };
        }
    }

    // 示例数据源：供 --wpf-shot 截图与手动预览使用
    internal sealed class SampleOverviewSource : IOverviewSource
    {
        public bool GuardEnabled { get { return true; } }
        public bool GameActive { get { return false; } }
        public bool HasWarning { get { return false; } }
        public bool HasCritical { get { return false; } }
        public double? GpuTempC { get { return 62; } }
        public double? MemoryUsedPct { get { return 53; } }
        public string MemoryUsedText { get { return "8.4 GB"; } }
        public string ModeText { get { return "常规"; } }
        public string LastCheckText { get { return "上次检查 2 分钟前 · 没有需要处理的问题"; } }
    }
}
