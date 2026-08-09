// @author zenjiro 18967498922@163.com
// 文件用途 新 UI 的颜色 Token：语义色与中性色，深浅双主题（规格 §3.1）

namespace CaelusApp
{
    internal enum UiTone
    {
        Light,
        Dark
    }

    internal sealed class ThemeColors
    {
        public string Success;
        public string Warning;
        public string Danger;
        public string Info;
        public string Brand;
        public string Background;
        public string Surface;
        public string SurfaceRaised;
        public string Border;
        public string BorderSubtle;
        public string TextPrimary;
        public string TextSecondary;
        public string TextTertiary;
    }

    internal static class Palette
    {
        private static readonly ThemeColors light = new ThemeColors
        {
            Success = "#2F9E5F",
            Warning = "#D97706",
            Danger = "#DC2626",
            Info = "#2563EB",
            Brand = "#D4A847",
            Background = "#F5F7F9",
            Surface = "#FFFFFF",
            SurfaceRaised = "#FAFBFC",
            Border = "#D8E0E6",
            BorderSubtle = "#E8EDF1",
            TextPrimary = "#141F29",
            TextSecondary = "#61727E",
            TextTertiary = "#848F96"
        };

        private static readonly ThemeColors dark = new ThemeColors
        {
            Success = "#4ADE80",
            Warning = "#FBBF24",
            Danger = "#F87171",
            Info = "#60A5FA",
            Brand = "#D4A847",
            Background = "#0F1419",
            Surface = "#161C22",
            SurfaceRaised = "#1A2028",
            Border = "#26313B",
            BorderSubtle = "#2E3A44",
            TextPrimary = "#E8EEF2",
            TextSecondary = "#9AA6AE",
            TextTertiary = "#6E7D89"
        };

        public static ThemeColors For(UiTone tone)
        {
            return tone == UiTone.Light ? light : dark;
        }
    }
}
