// @author zenjiro 18967498922@163.com
// 文件用途 Aurora Bento 主题契约自测：色板档/模式档字典 key 完整性 + 校验器正反样例

using System;
using System.IO;
using System.Text;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestThemeContractToneFiles()
        {
            string src = LocateSourceRoot();
            if (src == null) throw new TestSkippedException("找不到源码目录，发布构建下跳过");
            // wpf/Themes 与 src 同级（仓库根下），LocateSourceRoot 返回 src 目录，故经 ".." 上溯一层
            CheckThemeFile(Path.Combine(src, "..", "wpf", "Themes", "Colors.Dark.xaml"), ThemeContract.ToneKeys);
            CheckThemeFile(Path.Combine(src, "..", "wpf", "Themes", "Colors.Light.xaml"), ThemeContract.ToneKeys);
        }

        private static void TestThemeContractModeFiles()
        {
            string src = LocateSourceRoot();
            if (src == null) throw new TestSkippedException("找不到源码目录，发布构建下跳过");
            CheckThemeFile(Path.Combine(src, "..", "wpf", "Themes", "Mode.Standard.xaml"), ThemeContract.ModeKeys);
            CheckThemeFile(Path.Combine(src, "..", "wpf", "Themes", "Mode.Competitive.xaml"), ThemeContract.ModeKeys);
            CheckThemeFile(Path.Combine(src, "..", "wpf", "Themes", "Mode.Custom.xaml"), ThemeContract.ModeKeys);
        }

        private static void TestThemeContractValidator()
        {
            // 正样例：按契约拼一份完整字典，不应报缺
            var sb = new StringBuilder("<ResourceDictionary>");
            foreach (string k in ThemeContract.ModeKeys)
                sb.Append("<Color x:Key=\"" + k + "\"/>");
            string complete = sb.ToString();
            if (ThemeContract.MissingKeys(complete, ThemeContract.ModeKeys).Length != 0)
                throw new Exception("完整字典被误报缺 key");
            // 反样例：抽掉 AccentGlowColor，必须恰好检出它
            string broken = complete.Replace("<Color x:Key=\"AccentGlowColor\"/>", "");
            string[] missing = ThemeContract.MissingKeys(broken, ThemeContract.ModeKeys);
            if (missing.Length != 1 || missing[0] != "AccentGlowColor")
                throw new Exception("缺 key 未被正确检出：" + string.Join(",", missing));
        }

        private static void CheckThemeFile(string path, string[] contract)
        {
            if (!File.Exists(path)) throw new Exception("主题字典不存在：" + path);
            string[] missing = ThemeContract.MissingKeys(File.ReadAllText(path), contract);
            if (missing.Length > 0)
                throw new Exception(Path.GetFileName(path) + " 缺少契约 key：" + string.Join("、", missing));
        }
    }
}
