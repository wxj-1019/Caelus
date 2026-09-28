// @author zenjiro 18967498922@163.com
// 文件用途 扫描全部 WPF 页面 XAML，确保界面上不会显示漏定义的文案键
//           （原实现构建 WinForms 面板遍历控件树；单一 WPF 界面后改为静态扫描 XAML，
//           文本只可能来自 XAML 字面量或 ViewModel 的 Lang.T，后者由 LangKeys 测试覆盖）

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static readonly Regex UntranslatedKey =
            new Regex(@"^[a-z][a-z0-9]*(\.[a-z0-9]+)*\.[a-z][a-z0-9]*(\.[a-z0-9]+)*$",
                RegexOptions.CultureInvariant);

        private static readonly Regex XamlTextAttribute =
            new Regex("(?:Text|Content|Header|ToolTip)\\s*=\\s*\"([^\"]+)\"",
                RegexOptions.CultureInvariant);

        private static void TestNoUntranslatedKeysOnScreen()
        {
            string src = LocateSourceRoot();
            if (src == null) throw new TestSkippedException("找不到源码目录，发布构建下跳过");

            // LocateSourceRoot 返回 src 目录，wpf 与其同级，需上溯一层
            string wpfDir = Path.GetFullPath(Path.Combine(src, "..", "wpf"));
            if (!Directory.Exists(wpfDir)) throw new TestSkippedException("找不到 wpf 目录");

            Lang.Init();
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(wpfDir, "*.xaml", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(file, Encoding.UTF8); }
                catch { continue; }
                foreach (Match m in XamlTextAttribute.Matches(text))
                {
                    string value = m.Groups[1].Value.Trim();
                    if (value.Length == 0 || value.StartsWith("{")) continue;
                    if (!UntranslatedKey.IsMatch(value)) continue;
                    if (Lang.Row(value) != null) continue;
                    string entry = value + "  ←  " + Path.GetFileName(file);
                    if (!offenders.Contains(entry)) offenders.Add(entry);
                }
            }
            if (offenders.Count > 0)
                throw new Exception("界面上出现未定义的文案键：" + Environment.NewLine
                    + "   " + string.Join(Environment.NewLine + "   ", offenders.ToArray()));
        }
    }
}
