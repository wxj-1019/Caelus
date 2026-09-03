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
        }
    }
}
