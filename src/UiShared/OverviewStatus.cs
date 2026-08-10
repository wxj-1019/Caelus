// @author zenjiro 18967498922@163.com
// 文件用途 概览页状态结论与指标分级的纯逻辑（规格 §5.1）

namespace CaelusApp
{
    internal enum StatusLevel
    {
        Ready,       // 守护开启且空闲：游戏环境已准备好
        Optimizing,  // 守护开启且游戏运行中
        Attention,   // 有警告项
        Critical,    // 有危险项
        Off          // 守护关闭
    }

    internal enum MetricLevel
    {
        Ok,
        Warning,
        Critical
    }

    internal sealed class StatusConclusion
    {
        public StatusLevel Level;
        public string Title;
        public string Detail;
        public string Glyph;
    }

    internal static class OverviewStatus
    {
        public const double GpuWarnC = 75;
        public const double GpuCritC = 85;
        public const double MemWarnPct = 80;
        public const double MemCritPct = 90;

        public static StatusConclusion Conclude(
            bool guardEnabled, bool gameActive, bool hasWarning, bool hasCritical)
        {
            if (!guardEnabled)
                return new StatusConclusion
                {
                    Level = StatusLevel.Off,
                    Title = "守护已关闭",
                    Detail = "打开守护后，Caelus 才能在你玩游戏时自动优化",
                    Glyph = "○"
                };
            if (hasCritical)
                return new StatusConclusion
                {
                    Level = StatusLevel.Critical,
                    Title = "需要处理",
                    Detail = "存在影响游戏体验的问题，展开详情查看",
                    Glyph = "✕"
                };
            if (hasWarning)
                return new StatusConclusion
                {
                    Level = StatusLevel.Attention,
                    Title = "需要注意",
                    Detail = "有项目值得关注，展开详情查看",
                    Glyph = "⚠"
                };
            if (gameActive)
                return new StatusConclusion
                {
                    Level = StatusLevel.Optimizing,
                    Title = "游戏优化中",
                    Detail = "检测到游戏正在运行，优化策略已生效",
                    Glyph = "✓"
                };
            return new StatusConclusion
            {
                Level = StatusLevel.Ready,
                Title = "游戏环境已准备好",
                Detail = "启动游戏后 Caelus 会自动接管",
                Glyph = "✓"
            };
        }

        public static MetricLevel LevelFor(double value, double warnAt, double critAt)
        {
            if (value >= critAt) return MetricLevel.Critical;
            if (value >= warnAt) return MetricLevel.Warning;
            return MetricLevel.Ok;
        }

        public static string ColorKey(StatusLevel level)
        {
            switch (level)
            {
                case StatusLevel.Ready:
                case StatusLevel.Optimizing:
                    return "Success";
                case StatusLevel.Attention:
                case StatusLevel.Off:
                    return "Warning";
                default:
                    return "Danger";
            }
        }
    }
}
