// @author zenjiro 18967498922@163.com
// 文件用途 新 UI 的颜色 Token 读取：从 wpf/Themes XAML 提取（XAML 是唯一事实源，
//           不再维护硬编码副本——旧硬编码曾与实际主题分叉，测试给虚假信心）

using System;
using System.IO;

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
        /// <summary>定位仓库根（向上找 src/Ui），找不到返回 null。</summary>
        private static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(dir); depth++)
            {
                string candidate = Path.Combine(dir, "src");
                if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "Ui")))
                    return dir;
                DirectoryInfo parent = Directory.GetParent(dir.TrimEnd(Path.DirectorySeparatorChar));
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        private static string ReadTone(UiTone tone, out string text)
        {
            text = null;
            string root = FindRepoRoot();
            if (root == null) return null;
            string path = Path.Combine(root, "wpf", "Themes",
                tone == UiTone.Light ? "Colors.Light.xaml" : "Colors.Dark.xaml");
            if (!File.Exists(path)) return null;
            text = File.ReadAllText(path);
            return path;
        }

        /// <summary>从主题 XAML 实时提取色板；XAML 缺失时返回空值（调用方测试跳过）。</summary>
        public static ThemeColors For(UiTone tone)
        {
            var c = new ThemeColors();
            string text;
            if (ReadTone(tone, out text) == null || text == null) return c;

            c.Success = ThemeContract.ExtractColorValue(text, "SuccessColor");
            c.Warning = ThemeContract.ExtractColorValue(text, "WarningColor");
            c.Danger = ThemeContract.ExtractColorValue(text, "DangerColor");
            c.Info = ThemeContract.ExtractColorValue(text, "InfoColor");
            c.Brand = ThemeContract.ExtractColorValue(text, "BrandColor");
            c.Background = ThemeContract.ExtractColorValue(text, "BackgroundColor");
            c.Surface = ThemeContract.ExtractColorValue(text, "Surface0Color");
            c.SurfaceRaised = ThemeContract.ExtractColorValue(text, "Surface1Color");
            c.Border = ThemeContract.ExtractColorValue(text, "BorderStrongBrush");
            c.BorderSubtle = ThemeContract.ExtractColorValue(text, "BorderSubtleBrush");
            c.TextPrimary = ThemeContract.ExtractColorValue(text, "TextPrimaryColor");
            c.TextSecondary = ThemeContract.ExtractColorValue(text, "TextSecondaryColor");
            c.TextTertiary = ThemeContract.ExtractColorValue(text, "TextTertiaryColor");
            return c;
        }
    }
}
