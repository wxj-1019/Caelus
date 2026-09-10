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

        // —— 启动项禁用/还原：注册表走内存假店、lnk 走临时目录 ——
        private static System.Collections.Generic.Dictionary<string, string> fakeReg;

        private static void InstallFakeStartupStore(string dir)
        {
            fakeReg = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            StartupAuditAction.ScanCurrentHook = delegate
            {
                var list = new System.Collections.Generic.List<StartupAudit.Entry>();
                foreach (var kv in fakeReg)
                {
                    if (kv.Key.IndexOf("bak|") == 0) continue;
                    int bar = kv.Key.IndexOf('|');
                    list.Add(new StartupAudit.Entry(kv.Key.Substring(0, bar), kv.Key.Substring(bar + 1), kv.Value));
                }
                foreach (string f in Directory.GetFiles(StartupAuditAction.StartupFolderOverride))
                    list.Add(new StartupAudit.Entry("StartupFolder", Path.GetFileName(f), ""));
                return list;
            };
            StartupAuditAction.ReadRunValueHook = delegate(string hive, string name)
            {
                string v; return fakeReg.TryGetValue(hive + "|" + name, out v) ? v : null;
            };
            StartupAuditAction.WriteRunValueHook = delegate(string hive, string name, string data)
            {
                string key = hive + "|" + name;
                if (fakeReg.ContainsKey(key)) return "目标位置已有同名值";
                fakeReg[key] = data; return null;
            };
            StartupAuditAction.DeleteRunValueHook = delegate(string hive, string name)
            {
                return fakeReg.Remove(hive + "|" + name) ? null : "值不存在";
            };
            StartupAuditAction.BackupReadHook = delegate(string hive, string name)
            {
                string v; return fakeReg.TryGetValue("bak|" + hive + "|" + name, out v) ? v : null;
            };
            StartupAuditAction.BackupWriteHook = delegate(string hive, string name, string data)
            {
                fakeReg["bak|" + hive + "|" + name] = data; return null;
            };
            StartupAuditAction.BackupDeleteHook = delegate(string hive, string name)
            {
                fakeReg.Remove("bak|" + hive + "|" + name); return null;
            };
            StartupAuditAction.BackupEnumHook = delegate(string hive)
            {
                var l = new System.Collections.Generic.List<KeyValuePair<string, string>>();
                foreach (var kv in fakeReg)
                {
                    string prefix = "bak|" + hive + "|";
                    if (kv.Key.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) == 0)
                        l.Add(new KeyValuePair<string, string>(kv.Key.Substring(prefix.Length), kv.Value));
                }
                return l;
            };
            StartupAuditAction.StartupFolderOverride = Path.Combine(dir, "startup");
            StartupAuditAction.BackupDirOverride = Path.Combine(dir, "backup");
            Directory.CreateDirectory(StartupAuditAction.StartupFolderOverride);
        }

        private static void UninstallFakeStartupStore()
        {
            StartupAuditAction.ScanCurrentHook = null;
            StartupAuditAction.ReadRunValueHook = null;
            StartupAuditAction.WriteRunValueHook = null;
            StartupAuditAction.DeleteRunValueHook = null;
            StartupAuditAction.BackupReadHook = null;
            StartupAuditAction.BackupWriteHook = null;
            StartupAuditAction.BackupDeleteHook = null;
            StartupAuditAction.BackupEnumHook = null;
            StartupAuditAction.StartupFolderOverride = null;
            StartupAuditAction.BackupDirOverride = null;
            fakeReg = null;
        }

        private static void TestStartupDisablePlanFilter()
        {
            Eq(true, StartupAuditAction.IsSystemItem(new StartupAudit.Entry("HKLM\\Run", "Audio", "C:\\Windows\\svc.exe")));
            Eq(true, StartupAuditAction.IsSystemItem(new StartupAudit.Entry("HKCU\\Run", "OneDrive", "C:\\Program Files\\Microsoft\\OneDrive\\x.exe")));
            Eq(false, StartupAuditAction.IsSystemItem(new StartupAudit.Entry("HKCU\\Run", "MyApp", "C:\\apps\\my.exe")));
        }

        private static void TestStartupDisableRegistryRoundtrip()
        {
            string dir = NewTempDir("sa-reg");
            InstallFakeStartupStore(dir);
            try
            {
                fakeReg["HKCU\\Run|MyApp"] = "C:\\apps\\my.exe /q";
                var a = new StartupAuditAction();
                Eq(HealthOutcome.Skipped, a.Execute(null).Outcome);
                Eq(HealthOutcome.Skipped, a.Execute(new string[0]).Outcome);

                HealthResult r = a.Execute(new[] { "HKCU\\Run|MyApp" });
                Eq(HealthOutcome.Success, r.Outcome);
                Eq(1, r.ItemCount);
                Eq(false, fakeReg.ContainsKey("HKCU\\Run|MyApp"));
                Eq("C:\\apps\\my.exe /q", fakeReg["bak|HKCU\\Run|MyApp"]);
                Eq(true, r.UndoPayload.Length > 0);

                Eq(1, a.ListDisabled().Count);
                Eq("MyApp", a.ListDisabled()[0].Label);

                string err;
                Eq(true, a.Undo(a.ListDisabled()[0].Id, out err));
                Eq("C:\\apps\\my.exe /q", fakeReg["HKCU\\Run|MyApp"]);
                Eq(false, fakeReg.ContainsKey("bak|HKCU\\Run|MyApp"));
                Eq(0, a.ListDisabled().Count);

                HealthResult dup = a.Execute(new[] { "HKCU\\Run|MyApp" });
                Eq(HealthOutcome.Success, dup.Outcome);
                Eq(true, a.Undo(a.ListDisabled()[0].Id, out err));
            }
            finally { UninstallFakeStartupStore(); DeleteTempDir(dir); }
        }

        private static void TestStartupDisableSystemItemRefused()
        {
            string dir = NewTempDir("sa-sys");
            InstallFakeStartupStore(dir);
            try
            {
                fakeReg["HKLM\\Run|Audio"] = "C:\\Windows\\audio.exe";
                var a = new StartupAuditAction();
                HealthResult r = a.Execute(new[] { "HKLM\\Run|Audio" });
                Eq(HealthOutcome.Skipped, r.Outcome);
                Eq(true, fakeReg.ContainsKey("HKLM\\Run|Audio"));
            }
            finally { UninstallFakeStartupStore(); DeleteTempDir(dir); }
        }

        private static void TestStartupDisableLnkRoundtrip()
        {
            string dir = NewTempDir("sa-lnk");
            InstallFakeStartupStore(dir);
            try
            {
                string lnk = Path.Combine(StartupAuditAction.StartupFolderOverride, "tool.lnk");
                File.WriteAllText(lnk, "shortcut");
                var a = new StartupAuditAction();
                HealthResult r = a.Execute(new[] { "StartupFolder|tool.lnk" });
                Eq(HealthOutcome.Success, r.Outcome);
                Eq(false, File.Exists(lnk));
                Eq(1, a.ListDisabled().Count);

                string err;
                Eq(true, a.Undo(a.ListDisabled()[0].Id, out err));
                Eq(true, File.Exists(lnk));
                Eq(0, a.ListDisabled().Count);
            }
            finally { UninstallFakeStartupStore(); DeleteTempDir(dir); }
        }

        private static void TestStartupUndoTargetOccupied()
        {
            string dir = NewTempDir("sa-occ");
            InstallFakeStartupStore(dir);
            try
            {
                fakeReg["HKCU\\Run|App"] = "C:\\a.exe";
                var a = new StartupAuditAction();
                a.Execute(new[] { "HKCU\\Run|App" });
                fakeReg["HKCU\\Run|App"] = "C:\\new.exe";
                string err;
                Eq(false, a.Undo(a.ListDisabled()[0].Id, out err));
                Eq(true, err != null);
                Eq("C:\\new.exe", fakeReg["HKCU\\Run|App"]);
            }
            finally { UninstallFakeStartupStore(); DeleteTempDir(dir); }
        }

        private static void TestStartupAutoCycleNewsAndBaseline()
        {
            string dir = NewTempDir("sa-cycle");
            InstallFakeStartupStore(dir);
            string oldBaseline = StartupAuditAction.BaselinePathOverride;
            StartupAuditAction.BaselinePathOverride = Path.Combine(dir, "base.tsv");
            try
            {
                fakeReg["HKCU\\Run|NewSpy"] = "C:\\spy\\new.exe";
                var a = new StartupAuditAction();
                HealthReport r1 = a.Analyze();
                Eq(true, r1.Findings.Count >= 0);
                ((IHealthAutoCycle)a).OnAutoCycle();
                Eq("", Settings.LoadStr("HealthStartupNews", ""));

                fakeReg["HKCU\\Run|BrandNew"] = "C:\\new\\b.exe";
                ((IHealthAutoCycle)a).OnAutoCycle();
                string news = Settings.LoadStr("HealthStartupNews", "");
                Eq(true, news.IndexOf("BrandNew") >= 0);
            }
            finally
            {
                StartupAuditAction.BaselinePathOverride = oldBaseline;
                Settings.Remove("HealthStartupNews");
                UninstallFakeStartupStore(); DeleteTempDir(dir);
            }
        }
    }
}
