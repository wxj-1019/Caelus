// @author zenjiro 18967498922@163.com
// 文件用途 开发者体验第二批自测：专注时长统计、开发环境体检

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestFocusStatsAccumulateAndReset()
        {
            FocusStats.ResetForTest();
            try
            {
                var d1 = new DateTime(2026, 8, 15, 10, 0, 0);
                var d2 = new DateTime(2026, 8, 16, 10, 0, 0);
                FocusStats.RecordSession(10L * TimeSpan.TicksPerSecond, d1);
                FocusStats.RecordSession(5L * TimeSpan.TicksPerSecond, d1);
                Eq(15L, FocusStats.TodaySeconds(d1));
                Eq(2, FocusStats.TodaySessions(d1));

                // 跨天归零重计
                FocusStats.RecordSession(5L * TimeSpan.TicksPerSecond, d2);
                Eq(5L, FocusStats.TodaySeconds(d2));
                Eq(1, FocusStats.TodaySessions(d2));
                Eq(0L, FocusStats.TodaySeconds(d1));   // 旧日期已过期
            }
            finally { FocusStats.ResetForTest(); }
        }

        private static void TestDevFocusRecordsFocusStats()
        {
            FocusStats.ResetForTest();
            string dir = NewTempDir("focus-stats");
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);
                int before = FocusStats.TodaySessions(DateTime.Now);

                dev.SetFocusMode(true);
                dev.SetFocusMode(false);
                Eq(before + 1, FocusStats.TodaySessions(DateTime.Now));
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                try { Settings.Save("DevFocusModeOn", false); } catch { }
                DeleteTempDir(dir);
                FocusStats.ResetForTest();
            }
        }

        private static void TestDevEnvParseVersion()
        {
            Eq("8.0.100", DevEnvAudit.ParseVersion("8.0.100\n", ""));
            Eq("1.22.3", DevEnvAudit.ParseVersion("", "1.22.3\n"));
            Eq("", DevEnvAudit.ParseVersion("", ""));
            Eq("", DevEnvAudit.ParseVersion(null, null));
            Eq("git version 2.43.0", DevEnvAudit.ParseVersion("git version 2.43.0\r\n", ""));
        }

        private static void TestDevEnvAuditNames()
        {
            List<DevEnvAudit.DevEnvItem> items = DevEnvAudit.Run();
            Eq(true, items.Count >= 9);
            Eq("dotnet", items[0].Name);
            Eq("node", items[1].Name);
            Eq("npm", items[2].Name);
            Eq("git", items[3].Name);
            // 每个项：Name/Detail 非空，Found 与 Detail 一致
            foreach (DevEnvAudit.DevEnvItem it in items)
            {
                Eq(true, !string.IsNullOrEmpty(it.Name));
                Eq(true, !string.IsNullOrEmpty(it.Detail));
                if (it.Found) Eq(false, it.Detail.Contains("未安装"));
            }
        }
    }
}
