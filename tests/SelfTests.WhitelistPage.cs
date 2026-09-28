// @author zenjiro 18967498922@163.com
// 文件用途 白名单自动判定作用域 拖放目标解析与逐条范围调整的自测

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestWhitelistAutoScope()
        {
            Eq(WhitelistRuleKind.ApplicationFamily, GameMode.ResolveAutoKind(@"D:\Tencent\WeChat\WeChat.exe"));
            Eq(WhitelistRuleKind.ApplicationFamily, GameMode.ResolveAutoKind(@"C:\Program Files\Google\Chrome\chrome.exe"));
            Eq(WhitelistRuleKind.ApplicationFamily, GameMode.ResolveAutoKind(@"D:\OBS\obs64.exe"));

            Eq(WhitelistRuleKind.ExactPath, GameMode.ResolveAutoKind(@"C:\Windows\explorer.exe"));
            Eq(WhitelistRuleKind.ExactPath, GameMode.ResolveAutoKind(@"C:\Windows\System32\cmd.exe"));
            Eq(WhitelistRuleKind.ExactPath, GameMode.ResolveAutoKind(@"D:\Py\python.exe"));
            Eq(WhitelistRuleKind.ExactPath, GameMode.ResolveAutoKind(@"D:\Py\python3.11.exe"));
            Eq(WhitelistRuleKind.ExactPath, GameMode.ResolveAutoKind(@"D:\Node\node.exe"));
            Eq(WhitelistRuleKind.ExactPath, GameMode.ResolveAutoKind(@"C:\Java\javaw.exe"));
            Eq(WhitelistRuleKind.ExactPath, GameMode.ResolveAutoKind(@"C:\Windows\System32\svchost.exe"));
        }

        private static void TestWhitelistDropTargets()
        {
            Eq(@"D:\a\game.exe", WhitelistViewModel.ResolveWhitelistTarget(@"D:\a\game.exe"));
            Eq(@"D:\a\game.exe", WhitelistViewModel.ResolveWhitelistTarget("  \"D:\\a\\game.exe\"  "));
            Eq(null, WhitelistViewModel.ResolveWhitelistTarget(@"D:\a\readme.txt"));
            Eq(null, WhitelistViewModel.ResolveWhitelistTarget(@"D:\a\folder"));
            Eq(null, WhitelistViewModel.ResolveWhitelistTarget(""));
            Eq(null, WhitelistViewModel.ResolveWhitelistTarget(null));
        }

        private static void TestWhitelistAutoAddAndReshape()
        {
            string dir = Path.Combine(
                Path.GetTempPath(), "CaelusWl_" + Process.GetCurrentProcess().Id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string previousLog = Logger.LogPath;
            try
            {
                Logger.LogPath = Path.Combine(dir, "wl.log");
                var mode = new GameMode(dir, new SuppressionCore());

                string app = Path.Combine(dir, "MyApp.exe");
                File.WriteAllText(app, "stub");
                if (!mode.AddWhitelistAuto(app))
                    throw new Exception("普通程序自动加入失败：" + mode.WhitelistLastError);

                WhitelistRuleView added = FindRule(mode, app);
                if (added == null) throw new Exception("加进去的规则没出现在列表里");
                Eq(WhitelistRuleKind.ApplicationFamily, added.Rule.Kind);

                if (!mode.NarrowWhitelistRule(added.Rule.Key))
                    throw new Exception("收窄为仅此程序失败");
                WhitelistRuleView narrowed = FindRule(mode, app);
                if (narrowed == null) throw new Exception("收窄后规则丢失");
                Eq(WhitelistRuleKind.ExactPath, narrowed.Rule.Kind);

                if (!mode.WidenWhitelistRule(narrowed.Rule.Key))
                    throw new Exception("放宽回家族失败");
                WhitelistRuleView widened = FindRule(mode, app);
                if (widened == null) throw new Exception("放宽后规则丢失");
                Eq(WhitelistRuleKind.ApplicationFamily, widened.Rule.Kind);

                string host = Path.Combine(dir, "python.exe");
                File.WriteAllText(host, "stub");
                if (!mode.AddWhitelistAuto(host))
                    throw new Exception("脚本宿主自动加入失败：" + mode.WhitelistLastError);
                WhitelistRuleView hostRule = FindRule(mode, host);
                if (hostRule == null) throw new Exception("脚本宿主规则没出现");
                Eq(WhitelistRuleKind.ExactPath, hostRule.Rule.Kind);

                if (mode.WidenWhitelistRule(hostRule.Rule.Key))
                    throw new Exception("脚本宿主竟然被放宽成了家族豁免");
                Eq(WhitelistRuleKind.ExactPath, FindRule(mode, host).Rule.Kind);
            }
            finally
            {
                Logger.LogPath = previousLog;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static WhitelistRuleView FindRule(GameMode mode, string path)
        {
            foreach (WhitelistRuleView view in mode.GetWhitelistRulesFast())
                if (view.Rule.Kind != WhitelistRuleKind.LegacyName
                    && WhitelistRule.PathEquals(view.Rule.Value, path)) return view;
            return null;
        }

        private static void TestRunningPickerHidesSystemAndDuplicates()
        {
            List<WpfHost.Dialogs.RunningPickerDialogWpf.Entry> found;
            try { found = WpfHost.Dialogs.RunningPickerDialogWpf.Scan(null); }
            catch (Exception ex) { throw new TestSkippedException("无法枚举进程：" + ex.GetType().Name); }
            if (found.Count == 0) throw new TestSkippedException("当前没有带窗口的用户程序");

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string prefix = string.IsNullOrEmpty(windows) ? @"C:\Windows\" : windows.TrimEnd('\\') + "\\";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WpfHost.Dialogs.RunningPickerDialogWpf.Entry entry in found)
            {
                if (entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("选取列表里出现了 Windows 目录下的进程：" + entry.Path);
                if (GameSessionDetector.IsAntiCheatLikeName(Path.GetFileNameWithoutExtension(entry.Path)))
                    throw new Exception("选取列表里出现了反作弊：" + entry.Path);
                if (!seen.Add(entry.Path))
                    throw new Exception("同一个程序被列了多次：" + entry.Path);
            }

            var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { found[0].Path };
            foreach (WpfHost.Dialogs.RunningPickerDialogWpf.Entry entry in WpfHost.Dialogs.RunningPickerDialogWpf.Scan(exclude))
                if (WhitelistRule.PathEquals(entry.Path, found[0].Path))
                    throw new Exception("已在白名单的程序仍然出现在选取列表里");
        }

        private static void TestMemoryFormatting()
        {
            Eq("—", WpfHost.Dialogs.RunningPickerDialogWpf.FormatMemory(0));
            Eq("512 MB", WpfHost.Dialogs.RunningPickerDialogWpf.FormatMemory(512L * 1024 * 1024));
            Eq("1.5 GB", WpfHost.Dialogs.RunningPickerDialogWpf.FormatMemory(1610612736L));
        }
    }
}
