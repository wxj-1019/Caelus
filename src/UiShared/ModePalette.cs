// @author zenjiro 18967498922@163.com
// 文件用途 模式氛围色板读取：从 wpf/Themes/Mode.*.xaml 提取（XAML 是唯一事实源，
//           旧硬编码曾与主题分叉——cyan/red 巡航战备时代遗留，已删除）

using System;
using System.IO;

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
        private static string ModeFile(AppMode mode)
        {
            string name = mode == AppMode.Competitive ? "Mode.Competitive.xaml"
                : (mode == AppMode.Custom ? "Mode.Custom.xaml" : "Mode.Standard.xaml");
            // 与 Palette 相同的仓库根定位：向上找 src/Ui
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(dir); depth++)
            {
                string candidate = Path.Combine(dir, "src");
                if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "Ui")))
                    return Path.Combine(dir, "wpf", "Themes", name);
                DirectoryInfo parent = Directory.GetParent(dir.TrimEnd(Path.DirectorySeparatorChar));
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        /// <summary>从模式 XAML 实时提取色板；XAML 缺失时返回空值（调用方测试跳过）。</summary>
        public static ModeColors For(AppMode mode)
        {
            var c = new ModeColors();
            string path = ModeFile(mode);
            if (path == null || !File.Exists(path)) return c;
            string text;
            try { text = File.ReadAllText(path); }
            catch { return c; }

            c.AmbientPrimary = ThemeContract.ExtractColorValue(text, "AuroraPrimaryColor");
            c.AmbientSecondary = ThemeContract.ExtractColorValue(text, "AuroraSecondaryColor");
            c.ModeAccentOnDark = ThemeContract.ExtractColorValue(text, "ModeAccentOnDarkColor");
            c.ModeAccentOnLight = ThemeContract.ExtractColorValue(text, "ModeAccentOnLightColor");
            return c;
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
