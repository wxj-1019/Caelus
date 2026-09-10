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
            if (LolAceClientRunning())
                Skip("检测到 LOL/ACE 客户端家族运行，其驱动干扰非游戏进程优先级操作");
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
                if (ioBefore < 2)
                    // 环境敏感：父进程链被压制后 IO 优先级被子进程继承（如强退的
                    // 宿主残留），此时往返仍验证「提升到 3 → 还原到原值」，跳过门槛
                    Skip("探针 IO 优先级被环境压制（继承自父进程链），基线低于常态");

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
        /// <summary>LOL/ACE 客户端家族在场时其反作弊驱动会干扰非游戏进程的
        /// 优先级操作（提优往返断言不稳定），相关测试应跳过。</summary>
        private static bool LolAceClientRunning()
        {
            string[] markers = { "LeagueClient", "League of Legends", "SGuard64", "SGuardSvc", "wegame", "tcls_core" };
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    using (p)
                    {
                        string n = p.ProcessName;
                        foreach (string m in markers)
                            if (n.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void TestDailyCatalogCustomList()
        {
            string old = DailyCatalog.CustomList;
            try
            {
                DailyCatalog.CustomList = "notepad; ;\r\nmytool.exe\r\nBad Row  ";
                Eq(true, DailyCatalog.NameMatches("notepad"));
                Eq(true, DailyCatalog.NameMatches("mytool"));        // .exe 后缀归一
                Eq(true, DailyCatalog.IsMatch("notepad", @"C:\ anywhere\notepad.exe")); // 自定义无目录锚点
                Eq(false, DailyCatalog.NameMatches(""));             // 空行容错
                Eq(true, DailyCatalog.NameMatches("Bad Row"));       // 大小写不敏感、Trim
                Eq(true, DailyCatalog.NameMatches("chrome"));        // 内置名录不受影响
            }
            finally { DailyCatalog.CustomList = old; }
        }

        private static void TestDailyCareSaverApplyAndRestore()
        {
            string dir = NewTempDir("daily-saver");
            DailyCare daily = null;
            var calls = new List<string>();
            DailyCare.BatterySaverApplyHook = delegate { calls.Add("apply"); return true; };
            DailyCare.BatterySaverRestoreHook = delegate { calls.Add("restore"); return true; };
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                daily.SetBatteryForTest(true);          // 脱电掌权 → 激活续航档
                Eq(true, daily.IsGranted);
                Eq(1, calls.Count); Eq("apply", calls[0]);

                daily.SetBatteryForTest(false);         // 回电 → 还原
                Eq(true, calls.Contains("restore"));

                calls.Clear();
                daily.SetBatteryForTest(true);
                Eq(1, calls.Count);                      // 再次脱电再激活
                daily.Stop();                            // 挂起/停止 → 还原兜底
                Eq(true, calls.Contains("restore"));
            }
            finally
            {
                DailyCare.BatterySaverApplyHook = null;
                DailyCare.BatterySaverRestoreHook = null;
                if (daily != null) try { daily.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDailyCareSaverOffNoop()
        {
            string dir = NewTempDir("daily-saveroff");
            DailyCare daily = null;
            int applied = 0;
            DailyCare.BatterySaverApplyHook = delegate { applied++; return true; };
            DailyCare.BatterySaverRestoreHook = delegate { return true; };
            bool oldBatt = Settings.Load("DailyCareBatteryOn", true);
            Settings.Save("DailyCareBatteryOn", false);   // 电池开关关 → 不动作
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);
                daily.SetBatteryForTest(true);
                Eq(0, applied);
            }
            finally
            {
                Settings.Save("DailyCareBatteryOn", oldBatt);
                DailyCare.BatterySaverApplyHook = null;
                DailyCare.BatterySaverRestoreHook = null;
                if (daily != null) try { daily.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDailyCareSaverPowerSwapWhileGranted()
        {
            string dir = NewTempDir("daily-swap");
            DailyCare daily = null;
            var calls = new List<string>();
            DailyCare.BatterySaverApplyHook = delegate { calls.Add("apply"); return true; };
            DailyCare.BatterySaverRestoreHook = delegate { calls.Add("restore"); return true; };
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                // 家族可见钉住 → 市电下也掌权；市电掌权不应触发续航档
                daily.SetFamilyVisibleForTest(true);
                Eq(true, daily.IsGranted);
                Eq(0, calls.Count);

                // 仍掌权，AC→DC：RefreshPowerStateCore 即时激活续航档（不走 Suspend/Grant 链）
                daily.RefreshPowerStateCore(true);
                Eq(true, daily.IsGranted);
                Eq(1, calls.Count); Eq("apply", calls[0]);

                // 仍掌权，DC→AC：即时还原；全程未挂起（若走了 Suspend 链会追加第二个 restore）
                daily.RefreshPowerStateCore(false);
                Eq(true, daily.IsGranted);
                Eq(2, calls.Count); Eq("restore", calls[1]);

                // 同态无变化：分支不进
                daily.RefreshPowerStateCore(false);
                Eq(2, calls.Count);
            }
            finally
            {
                DailyCare.BatterySaverApplyHook = null;
                DailyCare.BatterySaverRestoreHook = null;
                if (daily != null) try { daily.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDailyCareSaverOffWhileGrantedRestores()
        {
            string dir = NewTempDir("daily-saveroff-granted");
            DailyCare daily = null;
            var calls = new List<string>();
            DailyCare.BatterySaverApplyHook = delegate { calls.Add("apply"); return true; };
            DailyCare.BatterySaverRestoreHook = delegate { calls.Add("restore"); return true; };
            bool oldBatt = Settings.Load("DailyCareBatteryOn", true);
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                // 家族可见钉住：关电池开关后场景仍掌权（不走 Suspend 链）
                daily.SetFamilyVisibleForTest(true);
                daily.RefreshPowerStateCore(true);          // 掌权中脱电 → 激活续航档
                Eq(true, daily.IsGranted);
                Eq(1, calls.Count); Eq("apply", calls[0]);

                // 中途关电池开关：续航档必须立即还原（总数恰 1 次 restore，
                // 若经 Suspend 链还原则会伴随丢掌权，IsGranted 断言可区分）
                daily.SetBatteryOn(false);
                Eq(true, daily.IsGranted);
                Eq(2, calls.Count); Eq("restore", calls[1]);
            }
            finally
            {
                Settings.Save("DailyCareBatteryOn", oldBatt);
                DailyCare.BatterySaverApplyHook = null;
                DailyCare.BatterySaverRestoreHook = null;
                if (daily != null) try { daily.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }
    }
}
