// @author zenjiro 18967498922@163.com
// 文件用途 驾驶舱模式氛围色板：Standard 紫罗兰 / Competitive 红 / Custom 金（Aurora 规格 §3.2/§4.3）
// 同步 wpf/Themes/Mode.*.xaml 的 AuroraPrimary/AuroraSecondary/ModeAccentOnDark/ModeAccentOnLight。

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
            // Aurora：紫罗兰主光晕 + 青色 Accent（Mode.Standard.xaml）
            AmbientPrimary = "#5B3BE8",
            AmbientSecondary = "#2563EB",
            ModeAccentOnDark = "#67E8F9",
            ModeAccentOnLight = "#0E7490"
        };

        private static readonly ModeColors competitive = new ModeColors
        {
            // Aurora：战备红主光晕 + 橙红辅光晕 + 粉红 Accent（Mode.Competitive.xaml）
            AmbientPrimary = "#E11D48",
            AmbientSecondary = "#F97316",
            ModeAccentOnDark = "#FB7185",
            // 注：原规格 #DC2626 对浅底 #F5F7F9 对比度仅 4.497，未达 AA；
            // 同红相加深至 #CC2020（对比度 5.15），规格 §4.3 表已同步。
            ModeAccentOnLight = "#CC2020"
        };

        private static readonly ModeColors custom = new ModeColors
        {
            // Aurora：工程金主光晕 + 紫罗兰辅光晕 + 金黄 Accent（Mode.Custom.xaml）
            AmbientPrimary = "#D4A847",
            AmbientSecondary = "#7C3AED",
            ModeAccentOnDark = "#E9C46A",
            ModeAccentOnLight = "#8A5A18"
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
