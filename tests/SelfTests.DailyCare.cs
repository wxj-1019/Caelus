// @author zenjiro 18967498922@163.com
// 文件用途 DailyCare 日常场景的自测：家族识别、活性判定、电池切换

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestDailyCatalogMatch()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            Eq(true, DailyCatalog.IsMatch("chrome",
                System.IO.Path.Combine(pf, @"Google\Chrome\Application\chrome.exe")));
            Eq(true, DailyCatalog.IsMatch("winword",
                System.IO.Path.Combine(pf, @"Microsoft Office\root\Office16\WINWORD.EXE")));
            // 名称命中目录不对：不认
            Eq(false, DailyCatalog.IsMatch("chrome", @"C:\Temp\chrome.exe"));
            // 目录对名称不对：不认
            Eq(false, DailyCatalog.IsMatch("notepad",
                System.IO.Path.Combine(pf, @"Google\Chrome\Application\notepad.exe")));
            // 空值安全
            Eq(false, DailyCatalog.IsMatch(null, null));
            Eq(false, DailyCatalog.IsMatch("chrome", null));
        }

        private static void TestDailyCareBatteryActivates()
        {
            string dir = NewTempDir("daily-batt");
            DailyCare daily = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(System.IO.Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                Eq(false, daily.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);

                // 电池供电 → 活跃并掌权
                daily.SetBatteryForTest(true);
                Eq(true, daily.IsActive);
                Eq(true, daily.IsGranted);
                Eq<ScenarioKind?>(ScenarioKind.DailyCare, arbiter.CurrentGranted);

                // 恢复市电 → 解除
                daily.SetBatteryForTest(false);
                Eq(false, daily.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            }
            finally
            {
                if (daily != null) try { daily.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDailyCareNoWindowNoActivate()
        {
            string dir = NewTempDir("daily-nowin");
            DailyCare daily = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(System.IO.Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                // 名称匹配但无可见窗口（fake PID）→ 不激活
                daily.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(43001, "chrome", ProcessChangeKind.Started) }, false));
                Eq(false, daily.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            }
            finally
            {
                if (daily != null) try { daily.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }
    }
}
