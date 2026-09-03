// @author zenjiro 18967498922@163.com
// 文件用途 设计沙盒 tokens.css 与 wpf/Themes 的令牌一致性自测：漂移即 FAIL

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static string ReadSandboxTokens()
        {
            // 自测 exe 在仓库根（dev.cmd 以 OutputPath=..\ 构建），沙盒在 design-sandbox/tokens.css
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "design-sandbox", "tokens.css");
            if (!File.Exists(path)) Skip("沙盒 tokens.css 不存在");
            return File.ReadAllText(path);
        }

        private static string ReadThemeFile(string name)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wpf", "Themes", name);
            if (!File.Exists(path)) throw new Exception("主题文件不存在: " + name);
            return File.ReadAllText(path);
        }

        // tokens.css 解析：取 某段（dark/light/:root）里的 --name: value
        private static string CssVar(string css, string sectionSelector, string name)
        {
            // 选择器必须紧跟它所在规则块的 '{'（允许空白），防止命中
            // 文件头注释里出现的同名选择器（tokens.css 头部映射表就写了 [data-theme="dark"]）
            Match sec = Regex.Match(css, Regex.Escape(sectionSelector) + "\\s*\\{",
                RegexOptions.CultureInvariant);
            if (!sec.Success) return null;
            int brace = sec.Index + sec.Length - 1;
            int end = css.IndexOf('}', brace);
            if (end < 0) return null;
            string body = css.Substring(brace + 1, end - brace - 1);
            Match m = Regex.Match(body,
                Regex.Escape(name) + "\\s*:\\s*([^;]+);", RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        // '#RRGGBB' → 'RRGGBB'（大写）：值级比对前归一；Eq 失败消息直接给出两侧值便于排障
        private static string HexNorm(string value)
        {
            return value == null ? null : value.TrimStart('#').ToUpperInvariant();
        }

        // LinearGradientBrush 定义块内最后一个字面量 Color（#RRGGBB[B]）：
        // 夜空烟花 Hero 渐变首 Stop 以 StaticResource 引用 TextPrimaryColor（无字面量），
        // 末 Stop 是字面量——定位 x:Key 后取至 </LinearGradientBrush> 的片段再取末个匹配
        private static string LastLiteralGradientStop(string xamlText, string key)
        {
            int start = xamlText.IndexOf("x:Key=\"" + key + "\"", StringComparison.Ordinal);
            if (start < 0) return null;
            int end = xamlText.IndexOf("</LinearGradientBrush>", start, StringComparison.Ordinal);
            if (end < 0) return null;
            MatchCollection stops = Regex.Matches(
                xamlText.Substring(start, end - start),
                "Color=\"(#[0-9A-Fa-f]{6,8})\"", RegexOptions.CultureInvariant);
            return stops.Count == 0 ? null : stops[stops.Count - 1].Groups[1].Value;
        }

        private static void TestDesignTokenParity()
        {
            string css = ReadSandboxTokens();
            string dark = ReadThemeFile("Colors.Dark.xaml");
            string light = ReadThemeFile("Colors.Light.xaml");
            string tokens = ReadThemeFile("Tokens.xaml");

            // 1) 主色板（dark 段 ↔ Colors.Dark.xaml）
            string cssBg = CssVar(css, "[data-theme=\"dark\"]", "--background");
            string xamlBg = ThemeContract.ExtractColorValue(dark, "BackgroundColor");
            Eq(true, xamlBg != null && cssBg != null
                && xamlBg.EndsWith(cssBg.Substring(1), StringComparison.OrdinalIgnoreCase));

            // 2) 字号令牌：CSS px 值 == Tokens.xaml 数值（":root" 经 \s*\{ 锚定后
            //    不会误命中 ":root," 合并选择器行，其后是逗号不是块开括号）
            string cssHero = CssVar(css, ":root", "--font-size-hero");
            Eq(true, tokens.Contains("x:Key=\"FontSizeHero\">" + cssHero.Replace("px", "") + "<"));

            // 3) 圆角签名：--radius-lg == RadiusLg
            string cssRad = CssVar(css, ":root", "--radius-lg");
            Eq(true, tokens.Contains("x:Key=\"RadiusLg\">" + cssRad.Replace("px", "") + "<"));

            // 4) 区域标题字号：--font-size-section == FontSizeSection
            string cssSection = CssVar(css, ":root", "--font-size-section");
            Eq(true, tokens.Contains("x:Key=\"FontSizeSection\">" + cssSection.Replace("px", "") + "<"));

            // 5) 夜空烟花令牌：沙盒 CSS ↔ XAML 值级比对（同第 1 组去 # 前缀，漂移即 FAIL，
            //    失败消息含两侧值）；Hero 渐变末 Stop / 场景卡日常色是唯一字面量来源
            Eq(HexNorm(CssVar(css, "[data-theme=\"dark\"]", "--hero-title-from")),
               HexNorm(ThemeContract.ExtractColorValue(dark, "TextPrimaryColor")));
            Eq(HexNorm(CssVar(css, "[data-theme=\"dark\"]", "--hero-title-to")),
               HexNorm(LastLiteralGradientStop(dark, "HeroTitleBrush")));
            Eq(HexNorm(CssVar(css, "[data-theme=\"dark\"]", "--scenario-daily")),
               HexNorm(ThemeContract.ExtractColorValue(dark, "ScenarioDailyBrush")));
            Eq(HexNorm(CssVar(css, "[data-theme=\"light\"]", "--hero-title-from")),
               HexNorm(ThemeContract.ExtractColorValue(light, "TextPrimaryColor")));
            Eq(HexNorm(CssVar(css, "[data-theme=\"light\"]", "--hero-title-to")),
               HexNorm(LastLiteralGradientStop(light, "HeroTitleBrush")));
            Eq(HexNorm(CssVar(css, "[data-theme=\"light\"]", "--scenario-daily")),
               HexNorm(ThemeContract.ExtractColorValue(light, "ScenarioDailyBrush")));
            // 其余场景卡画刷（开发=绿引用、双柔色）与展示字号仍按存在性守护
            Eq(true, dark.Contains("x:Key=\"ScenarioDevBrush\"")
                && dark.Contains("x:Key=\"ScenarioDailyBrush\"")
                && dark.Contains("x:Key=\"ScenarioDevSoftBrush\"")
                && dark.Contains("x:Key=\"ScenarioDailySoftBrush\""));
            Eq(true, CssVar(css, ":root", "--font-size-showcase") != null
                && tokens.Contains("x:Key=\"FontSizeShowcase\">36<"));
        }
    }
}
