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
    }
}
