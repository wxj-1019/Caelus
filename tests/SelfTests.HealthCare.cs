// @author zenjiro 18967498922@163.com
// 文件用途 系统健康维护的自测：启动项基线对比、到点判定

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestStartupAuditDiffNew()
        {
            var baseline = new List<StartupAudit.Entry>
            {
                new StartupAudit.Entry("HKCU\\Run", "OneDrive", "C:\\old\\onedrive.exe"),
                new StartupAudit.Entry("HKLM\\Run", "Audio", "C:\\rtk\\audiodg.exe")
            };
            var current = new List<StartupAudit.Entry>
            {
                new StartupAudit.Entry("HKCU\\Run", "OneDrive", "C:\\new\\onedrive.exe"),
                new StartupAudit.Entry("HKLM\\Run", "Audio", "C:\\rtk\\audiodg.exe"),
                new StartupAudit.Entry("HKCU\\Run", "NewSpy", "C:\\spy\\new.exe"),
                new StartupAudit.Entry("StartupFolder", "tool.lnk", "")
            };
            var added = StartupAudit.DiffNew(current, baseline);
            Eq(2, added.Count);
            Eq("NewSpy", added[0].Name);
            Eq("tool.lnk", added[1].Name);
        }

        private static void TestStartupAuditBaselineRoundtrip()
        {
            string dir = NewTempDir("startup-rt");
            try
            {
                string file = Path.Combine(dir, "baseline.txt");
                var entries = new List<StartupAudit.Entry>
                {
                    new StartupAudit.Entry("HKCU\\Run", "App|special", "C:\\a|b.exe /arg")
                };
                StartupAudit.SaveBaseline(file, entries);
                var loaded = StartupAudit.LoadBaseline(file);
                Eq(1, loaded.Count);
                Eq("HKCU\\Run", loaded[0].Source);
                Eq("App|special", loaded[0].Name);
                Eq("C:\\a|b.exe /arg", loaded[0].Command);

                Eq(0, StartupAudit.LoadBaseline(Path.Combine(dir, "missing.txt")).Count);
            }
            finally { DeleteTempDir(dir); }
        }

        private static void TestHealthCareIsDue()
        {
            Eq(true, HealthCare.IsDue("", 1, new DateTime(2026, 8, 14)));
            Eq(true, HealthCare.IsDue("2026-08-13", 1, new DateTime(2026, 8, 14)));
            Eq(false, HealthCare.IsDue("2026-08-14", 1, new DateTime(2026, 8, 14)));
            Eq(false, HealthCare.IsDue("2026-08-13", 7, new DateTime(2026, 8, 14)));
            Eq(true, HealthCare.IsDue("2026-08-07", 7, new DateTime(2026, 8, 14)));
            Eq(true, HealthCare.IsDue("garbage", 1, new DateTime(2026, 8, 14)));
        }

        private static void TestStartupAuditEscapingRoundtrip()
        {
            string dir = NewTempDir("startup-esc");
            try
            {
                // 回归：含 "\\t" 序列的路径（C:\\tools\\t.exe）必须原样往返。
                // 修复前 Unesc 用连续 Replace，先还原 "\\\\" 再还原 "\\t" 会把
                // 反斜杠后的 't' 误当制表符转义，损坏为 C:\\<TAB>ools\\...
                var tricky = new List<StartupAudit.Entry>
                {
                    new StartupAudit.Entry("HKLM\\Run", "Backup",
                        "C:\\tools\\backup\\t.exe /x")
                };
                string file2 = Path.Combine(dir, "baseline2.txt");
                StartupAudit.SaveBaseline(file2, tricky);
                var loaded2 = StartupAudit.LoadBaseline(file2);
                Eq(1, loaded2.Count);
                Eq("Backup", loaded2[0].Name);
                Eq("C:\\tools\\backup\\t.exe /x", loaded2[0].Command);

                // 真实制表符也必须能逃逸往返（Split('\\t') 分隔符不冲突）
                var tabby = new List<StartupAudit.Entry>
                {
                    new StartupAudit.Entry("HKCU\\Run", "Tabbed", "cmd.exe /c \"a\tb\"")
                };
                string file3 = Path.Combine(dir, "baseline3.txt");
                StartupAudit.SaveBaseline(file3, tabby);
                var loaded3 = StartupAudit.LoadBaseline(file3);
                Eq(1, loaded3.Count);
                Eq("cmd.exe /c \"a\tb\"", loaded3[0].Command);
                Eq("Tabbed", loaded3[0].Name);
            }
            finally { DeleteTempDir(dir); }
        }

        private static void TestShaderCacheActionThreshold()
        {
            // 阈值逻辑：不足 64MB 跳过，超阈值清理（挂钩隔离真实文件系统）
            long fakeBytes = 10L * 1024 * 1024;
            var freed = new CacheSweep.Result();
            ShaderCacheAction.MeasureHook = delegate { return fakeBytes; };
            ShaderCacheAction.CleanHook = delegate { freed.FreedBytes = fakeBytes / 2; return freed; };
            try
            {
                var a = new ShaderCacheAction();
                Eq(HealthOutcome.Skipped, a.Execute(null).Outcome);
                fakeBytes = 200L * 1024 * 1024;
                HealthResult r = a.Execute(null);
                Eq(HealthOutcome.Success, r.Outcome);
                Eq(100L * 1024 * 1024, r.FreedBytes);
                Eq(true, r.Summary.IndexOf("释放") >= 0);
                Eq(false, a.CanUndo);
                Eq(true, a.AllowAuto);
            }
            finally { ShaderCacheAction.MeasureHook = null; ShaderCacheAction.CleanHook = null; }
        }

        private static void TestHealthCareRunIfDueViaRunner()
        {
            // RunIfDue 改接 Runner 后：到点才执行、执行后写 HealthLastRun、动作结果进历史
            string dir = NewTempDir("hc-runner");
            string oldHist = HealthHistory.FilePath;
            HealthHistory.FilePath = Path.Combine(dir, "h.tsv");
            var probe = new FakeAction("probe-a", true);
            var c = new HealthActionCatalog();
            c.Register(probe);
            HealthActionCatalog oldCat = HealthCare.CatalogOverride;
            HealthCare.CatalogOverride = c;
            try
            {
                Settings.SaveStr("HealthLastRun", "2999-01-01");   // 强制不到点
                HealthCare.RunIfDue();
                Eq(0, probe.AnalyzeCalls);

                Settings.SaveStr("HealthLastRun", "2000-01-01");   // 强制到点
                HealthCare.ShouldDefer = delegate { return true; };// 游戏让路
                HealthCare.RunIfDue();
                Eq(0, probe.AnalyzeCalls);
                HealthCare.ShouldDefer = null;

                HealthCare.RunIfDue();
                Eq(1, probe.AnalyzeCalls);
                Eq(1, HealthHistory.LoadAll().Count);
                Eq(DateTime.Now.ToString("yyyy-MM-dd"), Settings.LoadStr("HealthLastRun", ""));
            }
            finally
            {
                HealthCare.CatalogOverride = oldCat;
                HealthCare.ShouldDefer = null;
                HealthHistory.FilePath = oldHist;
                Settings.Remove("HealthLastRun");
                DeleteTempDir(dir);
            }
        }
    }
}
