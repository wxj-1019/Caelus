// @author zenjiro 18967498922@163.com
// 文件用途 维护动作框架的自测：目录注册、Runner 编排与故障隔离、历史 TSV 往返

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private sealed class FakeAction : IHealthAction
        {
            private readonly string id;
            private readonly bool allowAuto;
            public int AnalyzeCalls;
            public string[] LastSelected;
            public bool ThrowOnAnalyze;
            public bool ThrowOnExecute;
            public FakeAction(string id, bool allowAuto) { this.id = id; this.allowAuto = allowAuto; }
            public string Id { get { return id; } }
            public string TitleKey { get { return "t"; } }
            public string DescKey { get { return "d"; } }
            public bool AllowAuto { get { return allowAuto; } }
            public bool CanUndo { get { return true; } }
            public HealthReport Analyze()
            {
                AnalyzeCalls++;
                if (ThrowOnAnalyze) throw new InvalidOperationException("boom-analyze");
                var r = new HealthReport { ActionId = id };
                r.Findings.Add(new HealthFinding { Id = "f1", Label = "项目一", Detail = "详情" });
                return r;
            }
            public HealthResult Execute(string[] selectedIds)
            {
                if (ThrowOnExecute) throw new InvalidOperationException("boom-exec");
                LastSelected = selectedIds;
                return new HealthResult { ActionId = id, Outcome = HealthOutcome.Success,
                    ItemCount = 1, Summary = id + " 完成" };
            }
            public bool Undo(string undoPayload, out string error) { error = null; return true; }
            public List<HealthFinding> ListDisabled() { return new List<HealthFinding>(); }
        }

        private static string NewHistoryFile(string tag)
        {
            string dir = NewTempDir(tag);
            return Path.Combine(dir, "history.tsv");
        }

        private static void TestHealthCatalogRegister()
        {
            var c = new HealthActionCatalog();
            c.Register(new FakeAction("a-one", true));
            c.Register(new FakeAction("a-two", false));
            Eq(2, c.All.Count);
            Eq(true, c.Find("a-two") != null);
            Eq(true, c.Find("missing") == null);
        }

        private static void TestHealthRunnerAutoSkipsManualOnly()
        {
            string file = NewHistoryFile("hr-auto");
            string old = HealthHistory.FilePath;
            HealthHistory.FilePath = file;
            try
            {
                var auto = new FakeAction("auto-a", true);
                var manual = new FakeAction("man-a", false);
                var c = new HealthActionCatalog();
                c.Register(auto); c.Register(manual);
                var rs = HealthRunner.Run(HealthTrigger.Auto, c, null);
                Eq(2, rs.Count);
                Eq(HealthOutcome.Success, rs[0].Outcome);
                Eq(HealthOutcome.Skipped, rs[1].Outcome);
                Eq(1, manual.AnalyzeCalls);
                Eq(true, manual.LastSelected == null);
                Eq(2, HealthHistory.LoadAll().Count);
            }
            finally { HealthHistory.FilePath = old; DeleteTempDir(Path.GetDirectoryName(file)); }
        }

        private static void TestHealthRunnerManualPassSelection()
        {
            string file = NewHistoryFile("hr-man");
            string old = HealthHistory.FilePath;
            HealthHistory.FilePath = file;
            try
            {
                var manual = new FakeAction("man-b", false);
                var c = new HealthActionCatalog();
                c.Register(manual);
                var sel = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                sel["man-b"] = new[] { "HKCU\\Run|X" };
                var rs = HealthRunner.Run(HealthTrigger.Manual, c, sel);
                Eq(1, rs.Count);
                Eq(HealthOutcome.Success, rs[0].Outcome);
                Eq(1, manual.LastSelected.Length);
                Eq("HKCU\\Run|X", manual.LastSelected[0]);
            }
            finally { HealthHistory.FilePath = old; DeleteTempDir(Path.GetDirectoryName(file)); }
        }

        private static void TestHealthRunnerFaultIsolation()
        {
            string file = NewHistoryFile("hr-iso");
            string old = HealthHistory.FilePath;
            HealthHistory.FilePath = file;
            try
            {
                var bad = new FakeAction("bad", true); bad.ThrowOnExecute = true;
                var badScan = new FakeAction("badscan", true); badScan.ThrowOnAnalyze = true;
                var good = new FakeAction("good", true);
                var c = new HealthActionCatalog();
                c.Register(bad); c.Register(badScan); c.Register(good);
                var rs = HealthRunner.Run(HealthTrigger.Auto, c, null);
                Eq(3, rs.Count);
                Eq(HealthOutcome.Failed, rs[0].Outcome);
                Eq(HealthOutcome.Failed, rs[1].Outcome);
                Eq(HealthOutcome.Success, rs[2].Outcome);
            }
            finally { HealthHistory.FilePath = old; DeleteTempDir(Path.GetDirectoryName(file)); }
        }

        private static void TestHealthHistoryRoundtrip()
        {
            string file = NewHistoryFile("hh-rt");
            string old = HealthHistory.FilePath;
            HealthHistory.FilePath = file;
            try
            {
                for (int i = 0; i < 55; i++)
                    HealthHistory.Append(new HealthRecord
                    {
                        Time = new DateTime(2026, 9, 11).AddMinutes(i),
                        Trigger = "Auto", ActionId = "a" + (i % 3),
                        Outcome = HealthOutcome.Success, FreedBytes = i,
                        ItemCount = i, Summary = "含制表\t与反斜\\t混合 " + i,
                        UndoPayload = i == 54 ? "HKCU\\Run\tX\tC:\\a\tb" : ""
                    });
                var all = HealthHistory.LoadAll();
                Eq(50, all.Count);
                Eq(54, all[49].ItemCount);
                Eq("含制表\t与反斜\\t混合 54", all[49].Summary);
                Eq("HKCU\\Run\tX\tC:\\a\tb", all[49].UndoPayload);
                Eq(true, HealthHistory.Find(all[49].Id) != null);
                Eq(true, HealthHistory.Find("no-such-id") == null);
            }
            finally { HealthHistory.FilePath = old; DeleteTempDir(Path.GetDirectoryName(file)); }
        }

        private static void TestHealthRunnerUndoRecord()
        {
            string file = NewHistoryFile("hr-undo");
            string old = HealthHistory.FilePath;
            HealthHistory.FilePath = file;
            try
            {
                var c = new HealthActionCatalog();
                c.Register(new FakeAction("u-a", false));
                string err;
                Eq(true, HealthRunner.UndoSingle(c, "u-a", "HKCU\\Run\tX", out err));
                Eq(true, err == null);
                var all = HealthHistory.LoadAll();
                Eq(1, all.Count);
                Eq("Undo", all[0].Trigger);
                Eq(HealthOutcome.Success, all[0].Outcome);
                Eq(false, HealthRunner.UndoSingle(c, "ghost", "x", out err));
                Eq(true, err != null);
            }
            finally { HealthHistory.FilePath = old; DeleteTempDir(Path.GetDirectoryName(file)); }
        }
    }
}
