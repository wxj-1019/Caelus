// @author zenjiro 18967498922@163.com
// 文件用途 驾驶舱模式氛围色板：巡航青 / 战备红 / 工程紫（规格 §4.3）

using System;

namespace CaelusApp
{
    internal enum AppMode
    {
        Standard,
        Competitive,
        Custom
    }

    internal sealed class ModeColors
    {
        public string AmbientPrimary;
        public string AmbientSecondary;
        public string ModeAccentOnDark;
        public string ModeAccentOnLight;
    }

    internal static class ModePalette
    {
        private static readonly ModeColors standard = new ModeColors
        {
            AmbientPrimary = "#1FB6D6",
            AmbientSecondary = "#2E7DD1",
            ModeAccentOnDark = "#3EC9FF",
            ModeAccentOnLight = "#0E7490"
        };

        private static readonly ModeColors competitive = new ModeColors
        {
            AmbientPrimary = "#E5484D",
            AmbientSecondary = "#C22E3E",
            ModeAccentOnDark = "#FF6B74",
            // 注：原规格 #DC2626 对浅底 #F5F7F9 对比度仅 4.497，未达 AA；
            // 同红相加深至 #CC2020（对比度 5.15），规格 §4.3 表已同步。
            ModeAccentOnLight = "#CC2020"
        };

        private static readonly ModeColors custom = new ModeColors
        {
            AmbientPrimary = "#8B5CF6",
            AmbientSecondary = "#6D4AC8",
            ModeAccentOnDark = "#A78BFA",
            ModeAccentOnLight = "#7C3AED"
        };

        public static ModeColors For(AppMode mode)
        {
            if (mode == AppMode.Competitive) return competitive;
            if (mode == AppMode.Custom) return custom;
            return standard;
        }

        public static string DisplayName(AppMode mode)
        {
            if (mode == AppMode.Competitive) return "竞技";
            if (mode == AppMode.Custom) return "自定义";
            return "常规";
        }

        public static AppMode FromPreset(PerformancePreset preset)
        {
            if (preset == PerformancePreset.Competitive) return AppMode.Competitive;
            if (preset == PerformancePreset.Custom) return AppMode.Custom;
            return AppMode.Standard;
        }
    }
}
