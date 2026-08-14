// @author zenjiro 18967498922@163.com
// 文件用途 DailyCare 日常场景的自测：家族识别、活性判定、电池切换、压制位隔离

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

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

        private static void TestDailyCareReasonIsolation()
        {
            string dir = NewTempDir("daily-bit");
            Process probe = null;
            try
            {
                string beat = Path.Combine(dir, "p.beat");
                probe = StartProbe(beat);
                WaitAdvance(beat, -1, 4000);

                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                try
                {
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Background, null, SuppressionLevel.Eco);
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Daily, "dailycare", SuppressionLevel.Eco);
                    Eq(true, core.HasReason(probe.Id, SuppressReason.Daily));

                    core.ReleaseReason(SuppressReason.Daily);
                    Eq(false, core.HasReason(probe.Id, SuppressReason.Daily));
                    Eq(true, core.HasReason(probe.Id, SuppressReason.Background));
                    Eq(true, core.IsThrottled(probe.Id));

                    core.ReleaseReason(SuppressReason.Background);
                    Eq(false, core.IsThrottled(probe.Id));
                }
                finally { core.ReleaseReason(SuppressReason.Background | SuppressReason.Daily); }
            }
            finally
            {
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDailyCareLevelChoice()
        {
            Eq(SuppressionLevel.Eco, DailyCare.ResolveDailyLevel(false));
            Eq(SuppressionLevel.Restrained, DailyCare.ResolveDailyLevel(true));
        }

        private static void TestDailyCareLevelDowngradeOnAc()
        {
            string dir = NewTempDir("daily-level");
            Process probe = null;
            try
            {
                string beat = Path.Combine(dir, "p.beat");
                probe = StartProbe(beat);
                WaitAdvance(beat, -1, 4000);
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);

                var core = new SuppressionCore(System.IO.Path.Combine(dir, "s.state"));
                try
                {
                    // 电池档（Restrained）：BelowNormal 生效，Daily 级别元数据可查
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Daily, "dailycare", SuppressionLevel.Restrained);
                    Eq(SuppressionLevel.Restrained, core.LevelOf(probe.Id, SuppressReason.Daily));
                    probe.Refresh();
                    Eq(ProcessPriorityClass.BelowNormal, probe.PriorityClass);

                    // 同一掌权期内电池→市电：重扫以 Eco 再 Acquire，级别与优先级都应降回常规档
                    // （修复前 Build/Daily 位没有级别槽位，EffectiveLevel 不感知，降档不生效）
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Daily, "dailycare", SuppressionLevel.Eco);
                    Eq(SuppressionLevel.Eco, core.LevelOf(probe.Id, SuppressReason.Daily));
                    probe.Refresh();
                    Eq(ProcessPriorityClass.Normal, probe.PriorityClass);

                    // 还原后彻底解除
                    core.ReleaseReason(SuppressReason.Daily);
                    Eq(false, core.IsThrottled(probe.Id));
                }
                finally { core.ReleaseReason(SuppressReason.Daily); }
            }
            finally
            {
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDailyCareReasonLevelMax()
        {
            string dir = NewTempDir("daily-max");
            Process probe = null;
            try
            {
                string beat = Path.Combine(dir, "p.beat");
                probe = StartProbe(beat);
                WaitAdvance(beat, -1, 4000);

                var core = new SuppressionCore(System.IO.Path.Combine(dir, "s.state"));
                try
                {
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Background, null, SuppressionLevel.Eco);
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Daily, "dailycare", SuppressionLevel.Restrained);
                    // 双原因并存：生效级别取最强
                    Eq(SuppressionLevel.Restrained, core.LevelOf(probe.Id));

                    // 释放游戏位：Daily 位仍在，级别保持 Restrained
                    core.ReleaseReason(SuppressReason.Background);
                    Eq(SuppressionLevel.Restrained, core.LevelOf(probe.Id));
                    Eq(SuppressionLevel.Restrained, core.LevelOf(probe.Id, SuppressReason.Daily));

                    // 释放 Daily 位后彻底解除
                    core.ReleaseReason(SuppressReason.Daily);
                    Eq(false, core.IsThrottled(probe.Id));
                }
                finally { core.ReleaseReason(SuppressReason.Background | SuppressReason.Daily); }
            }
            finally
            {
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDailyCareFamilyBoostRoundtrip()
        {
            string dir = NewTempDir("daily-boost");
            Process probe = null;
            DailyCare daily = null;
            try
            {
                string beat = Path.Combine(dir, "d.beat");
                probe = StartProbe(beat);
                WaitAdvance(beat, -1, 4000);
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
                int ioBefore = QueryIoOf(probe.Id);
                Eq(true, ioBefore >= 2);

                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(System.IO.Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                // 测试钩子绕过窗口条件直接提优：AboveNormal + IO 3 生效
                Eq(true, daily.BoostFamilyForTest(probe.Id));
                probe.Refresh();
                Eq(ProcessPriorityClass.AboveNormal, probe.PriorityClass);
                Eq(3, QueryIoOf(probe.Id));

                // 还原：优先级与 IO 都回到快照原值（不是写死的 2）
                daily.RestoreFamilyBoost();
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
                Eq(ioBefore, QueryIoOf(probe.Id));
            }
            finally
            {
                if (daily != null) try { daily.RestoreFamilyBoost(); } catch { }
                if (daily != null) try { daily.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }
    }
}
