// @author zenjiro 18967498922@163.com
// 文件用途 Aurora Bento 主题契约 v2：主题字典必须实现的 key 集合与文本级校验（规格 §3.1）

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CaelusApp
{
    internal static class ThemeContract
    {
        // 色板档（明暗轴）：Colors.Dark.xaml / Colors.Light.xaml 必须全部实现
        public static readonly string[] ToneKeys = new[]
        {
            "BackgroundColor", "BackgroundBrush",
            "Surface0Color", "Surface0Brush", "Surface1Color", "Surface1Brush", "Surface2Color", "Surface2Brush",
            "BorderSubtleBrush", "BorderStrongBrush", "TopHighlightBrush", "CardEdgeBrush",
            "SegSelectedBrush", "SegSelectedTextBrush",
            "TextPrimaryColor", "TextPrimaryBrush", "TextSecondaryColor", "TextSecondaryBrush",
            "TextTertiaryColor", "TextTertiaryBrush",
            "SuccessColor", "SuccessBrush", "WarningColor", "WarningBrush",
            "DangerColor", "DangerBrush", "InfoColor", "InfoBrush",
            "BrandColor", "BrandBrush",
        };

        // 模式档（模式轴）：Mode.Standard/Competitive/Custom.xaml 与用户主题 Caelus.theme.xaml 必须全部实现
        public static readonly string[] ModeKeys = new[]
        {
            "AuroraPrimaryColor", "AuroraPrimaryFadeColor",
            "AuroraSecondaryColor", "AuroraSecondaryFadeColor",
            "AuroraTertiaryColor", "AuroraTertiaryFadeColor",
            "AmbientPrimaryBrush", "AmbientSecondaryBrush", "AmbientTertiaryBrush",
            "AuroraPrimaryOpacity", "AuroraSecondaryOpacity", "AuroraTertiaryOpacity",
            "AuroraDriftSeconds",
            "AccentPrimaryColor", "AccentSecondaryColor",
            "AccentPrimaryBrush", "AccentSecondaryBrush", "AccentGradientBrush",
            "AccentSoftBrush", "AccentEdgeBrush", "AccentGlowColor", "OnAccentBrush",
        };

        private static readonly Regex KeyAttr =
            new Regex("x:Key\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);

        // 从 XAML 文本提取全部资源 key（与 LangKeys 自测同款文本扫描思路）
        public static HashSet<string> ExtractKeys(string xamlText)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (xamlText == null) return keys;
            foreach (Match m in KeyAttr.Matches(xamlText))
                keys.Add(m.Groups[1].Value);
            return keys;
        }

        // 返回缺失的契约 key；空数组 = 通过
        public static string[] MissingKeys(string xamlText, string[] contract)
        {
            HashSet<string> have = ExtractKeys(xamlText);
            var missing = new List<string>();
            foreach (string key in contract)
                if (!have.Contains(key)) missing.Add(key);
            return missing.ToArray();
        }

        private static readonly Regex ColorValue =
            new Regex("#[0-9A-Fa-f]{6,8}", RegexOptions.CultureInvariant);

        /// <summary>从 XAML 文本提取指定 key 的颜色值（#RRGGBB 或 #AARRGGBB）。
        /// 行级扫描：<Color x:Key="K">VALUE</Color> 与 <SolidColorBrush x:Key="K" Color="VALUE"/>
        /// 都按行解析；找不到返回 null。XAML 是唯一事实源，禁止再硬编码色板副本。</summary>
        public static string ExtractColorValue(string xamlText, string key)
        {
            if (string.IsNullOrEmpty(xamlText)) return null;
            string[] lines = xamlText.Split('\n');
            foreach (string line in lines)
            {
                if (line.IndexOf("x:Key=\"" + key + "\"", StringComparison.Ordinal) < 0) continue;
                Match m = ColorValue.Match(line);
                return m.Success ? m.Value : null;
            }
            return null;
        }
    }
}
