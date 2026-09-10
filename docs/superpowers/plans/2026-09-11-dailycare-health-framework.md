# 日常养护维护动作框架 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把日常养护的维护能力升级为动作框架（IHealthAction + Runner + History），落地四件事：结果可见+立即执行、启动项禁用/还原、名录自定义、电池联动电源滑块。

**Architecture:** 新目录 `src/Core/Scenario/Health/`（csproj 对 `src/Core/**/*.cs` 与 `tests/**/*.cs` 是递归通配，新文件免登记）。动作注册制；定时调度与手动按钮同走 `HealthRunner`；历史追加式 TSV（50 条截断，AtomicFile 写）。启动项「只禁不删」：Run 键值移备份键、lnk 移备份目录，历史记录存还原负载冗余。电池联动扩展 `PowerOverlay` 加 DC 续航档，与游戏档快照槽分离。

**Tech Stack:** C# 5（.NET Framework 4.x，**禁用字符串插值/表达式成员等新语法**）、WPF（无 ICommand 模式，按钮走 Click 事件）、自测门禁 `dev.cmd test`（Git Bash 里写 `cmd //c dev.cmd test`）。

**规格：** `docs/superpowers/specs/2026-09-11-dailycare-health-framework-design.md`

**对规格的细化/偏差（实施完成后回写规格）：**
1. `Undo` 接口签名改为 `Undo(string undoPayload, out string error)`——负载单行（`Esc(Hive)\tEsc(Name)\tEsc(Data)\tEsc(Extra)`），还原粒度=单条；历史是不可变事件日志，不再设计 MarkUndone。
2. 「已禁用」分组的当前真值来自备份存储枚举（备份键+备份目录），历史负载是冗余备份；接口加 `ListDisabled()`。
3. Analyze 保持纯函数（不写新闻、不动基线，供 UI 反复调用）；新闻+基线提交走新接口 `IHealthAutoCycle.OnAutoCycle()`，由 Runner 自动路径调用。

**通用约定（每个任务都适用，不再重复）：**
- 新 .cs 文件头两行固定为 `// @author zenjiro 18967498922@163.com` 和 `// 文件用途 …`。
- 命名空间：src 下 `CaelusApp`；wpf 视图 `CaelusApp.WpfHost.Views`。
- 自测基建：`Eq(a,b)`、`NewTempDir("tag")`、`DeleteTempDir(dir)`、`TestSkippedException` 已存在；测试进程内 `Settings` 是瞬时存储（不写真实注册表）。
- 跑门禁：`cmd //c dev.cmd test`，期望输出无 `FAIL` 行、`TOTAL` 计数通过（本机 SKIP 3~5 浮动属正常）。报告全文在 `%TEMP%\Caelus.selftest.txt`。
- 每步 commit 只 add 本任务涉及的文件，**永远不要提交 `Caelus.ico`**（工作区有一个无关的本地改动）。

---

### Task 1: 框架三件套（类型/历史/目录/Runner）+ 自测

**Files:**
- Create: `src/Core/Scenario/Health/HealthTypes.cs`
- Create: `src/Core/Scenario/Health/HealthEsc.cs`
- Create: `src/Core/Scenario/Health/HealthHistory.cs`
- Create: `src/Core/Scenario/Health/HealthActionCatalog.cs`
- Create: `src/Core/Scenario/Health/HealthRunner.cs`
- Create: `src/Core/Scenario/Health/HealthCatalog.cs`
- Test: `tests/SelfTests.HealthAction.cs`（新建）、`tests/SelfTests.cs`（注册）

- [ ] **Step 1: 写失败的测试**

新建 `tests/SelfTests.HealthAction.cs`：

```csharp
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
                Eq(HealthOutcome.Skipped, rs[1].Outcome);   // AllowAuto=false 只扫描
                Eq(1, manual.AnalyzeCalls);
                Eq(true, manual.LastSelected == null);       // 未执行
                Eq(2, HealthHistory.LoadAll().Count);        // 两个动作都进历史
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
                Eq(3, rs.Count);                              // 全部有结果，不中断
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
                Eq(50, all.Count);                             // 截断到最近 50
                Eq(54, all[49].ItemCount);                     // 最新在最后
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
```

在 `tests/SelfTests.cs` 的 `Run` 方法里注册（放在「健康维护」相关 test 行附近）：

```csharp
            test("维护框架：目录注册与查找", TestHealthCatalogRegister);
            test("维护框架：自动路径跳过手动动作", TestHealthRunnerAutoSkipsManualOnly);
            test("维护框架：手动路径透传勾选项", TestHealthRunnerManualPassSelection);
            test("维护框架：单动作异常隔离不中断", TestHealthRunnerFaultIsolation);
            test("维护框架：历史截断与特殊字符往返", TestHealthHistoryRoundtrip);
            test("维护框架：单条还原追加 Undo 记录", TestHealthRunnerUndoRecord);
```

- [ ] **Step 2: 跑门禁确认失败**

Run: `cmd //c dev.cmd test`
Expected: 构建失败（`IHealthAction` 等类型不存在），或测试 FAIL。

- [ ] **Step 3: 实现框架文件**

`src/Core/Scenario/Health/HealthEsc.cs`（从 StartupAudit 抽出的共享转义，行为不变）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 TSV 转义共享助手：StartupAudit 基线与维护历史共用的单趟转义/还原

using System.Text;

namespace CaelusApp
{
    internal static class HealthEsc
    {
        public static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        /// <summary>转义是歧义的（"\\t" 既可能是字面反斜杠+t，也可能是转义后的 TAB），
        /// 连续 Replace 无法正确处理，必须单趟从左到右扫描。</summary>
        public static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                    if (next == 't') { sb.Append('\t'); i++; continue; }
                    if (next == 'r') { sb.Append('\r'); i++; continue; }
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
```

`src/Core/Scenario/Health/HealthTypes.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 维护动作框架的类型：动作契约、扫描报告/执行结果、历史记录

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal enum HealthTrigger { Auto, Manual }
    internal enum HealthOutcome { Success, Failed, Skipped }

    /// <summary>一条可处理的发现项（如一个新启动项）。Id 在动作内稳定唯一。</summary>
    internal sealed class HealthFinding
    {
        public string Id;
        public string Label;
        public string Detail;
        public bool Risky;      // 系统/微软项：UI 默认不勾选
    }

    internal sealed class HealthReport
    {
        public string ActionId;
        public long Bytes;
        public readonly List<HealthFinding> Findings = new List<HealthFinding>();
    }

    internal sealed class HealthResult
    {
        public string ActionId;
        public HealthOutcome Outcome;
        public long FreedBytes;
        public int ItemCount;
        public string Error;
        public string Summary;
        public string UndoPayload;   // 可逆动作：还原负载（多行，\n 分隔单行负载）
    }

    internal sealed class HealthRecord
    {
        public string Id;
        public DateTime Time;
        public string Trigger;       // Auto / Manual / Undo
        public string ActionId;
        public HealthOutcome Outcome;
        public long FreedBytes;
        public int ItemCount;
        public string Summary;
        public string UndoPayload;
    }

    internal interface IHealthAction
    {
        string Id { get; }
        string TitleKey { get; }
        string DescKey { get; }
        bool AllowAuto { get; }      // 定时调度是否允许自动 Execute
        bool CanUndo { get; }
        HealthReport Analyze();                          // 纯只读扫描，UI 可反复调用
        HealthResult Execute(string[] selectedIds);      // null=不限定（仅 AllowAuto 自动路径）
        bool Undo(string undoPayload, out string error); // 单行负载还原一条
        List<HealthFinding> ListDisabled();              // 当前禁用态可还原项；不支持则空
    }

    /// <summary>自动维护周期到点时的附带职责（如启动项新闻与基线提交），保持 Analyze 纯。</summary>
    internal interface IHealthAutoCycle
    {
        void OnAutoCycle();
    }
}
```

`src/Core/Scenario/Health/HealthHistory.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 维护历史：追加式 TSV（最近 50 条截断），AtomicFile 原子写

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static class HealthHistory
    {
        internal const int Keep = 50;
        private static readonly object lk = new object();
        private static int seq;
        private static string filePath;

        /// <summary>历史文件路径；测试可覆盖。默认 Paths.Data 下。</summary>
        internal static string FilePath
        {
            get { return filePath ?? Path.Combine(Paths.Data, "health-history.tsv"); }
            set { filePath = value; }
        }

        public static void Append(HealthRecord r)
        {
            if (r == null) return;
            lock (lk)
            {
                if (string.IsNullOrEmpty(r.Id))
                    r.Id = DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + (++seq);
                var all = LoadAll();
                all.Add(r);
                if (all.Count > Keep) all.RemoveRange(0, all.Count - Keep);
                Save(all);
            }
        }

        public static List<HealthRecord> LoadAll()
        {
            lock (lk)
            {
                var list = new List<HealthRecord>();
                try
                {
                    string p = FilePath;
                    if (!File.Exists(p)) return list;
                    foreach (string line in File.ReadAllLines(p))
                    {
                        string[] f = line.Split('\t');
                        if (f.Length < 9) continue;
                        HealthRecord r = new HealthRecord();
                        r.Id = f[0];
                        DateTime t; DateTime.TryParse(f[1], out t); r.Time = t;
                        r.Trigger = f[2];
                        r.ActionId = f[3];
                        HealthOutcome oc; r.Outcome = Enum.TryParse(f[4], out oc) ? oc : HealthOutcome.Failed;
                        long fb; long.TryParse(f[5], out fb); r.FreedBytes = fb;
                        int ic; int.TryParse(f[6], out ic); r.ItemCount = ic;
                        r.Summary = HealthEsc.Unesc(f[7]);
                        r.UndoPayload = HealthEsc.Unesc(f[8]);
                        list.Add(r);
                    }
                }
                catch { }
                return list;
            }
        }

        public static HealthRecord Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (HealthRecord r in LoadAll())
                if (string.Equals(r.Id, id, StringComparison.Ordinal)) return r;
            return null;
        }

        private static void Save(List<HealthRecord> all)
        {
            try
            {
                var lines = new List<string>();
                foreach (HealthRecord r in all)
                    lines.Add(r.Id + "\t" + r.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\t"
                        + r.Trigger + "\t" + r.ActionId + "\t" + r.Outcome + "\t"
                        + r.FreedBytes + "\t" + r.ItemCount + "\t"
                        + HealthEsc.Esc(r.Summary) + "\t" + HealthEsc.Esc(r.UndoPayload));
                AtomicFile.WriteLines(FilePath, lines.ToArray(), "HealthHistory");
            }
            catch { }
        }
    }
}
```

`src/Core/Scenario/Health/HealthActionCatalog.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 维护动作注册表：动作注册制，调度/历史/UI 与具体动作解耦

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class HealthActionCatalog
    {
        private readonly List<IHealthAction> all = new List<IHealthAction>();

        public void Register(IHealthAction action)
        {
            if (action == null) throw new ArgumentNullException("action");
            all.Add(action);
        }

        public IList<IHealthAction> All { get { return all; } }

        public IHealthAction Find(string id)
        {
            if (id == null) return null;
            foreach (IHealthAction a in all)
                if (string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }
    }
}
```

`src/Core/Scenario/Health/HealthRunner.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 维护动作统一执行入口：定时调度与手动按钮同走此处，逐动作故障隔离

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class HealthRunner
    {
        /// <summary>跑一轮。Auto：仅 AllowAuto 动作执行，其余只扫描（实现 IHealthAutoCycle
        /// 的顺带跑自动周期职责）；Manual：全部执行，selected 传动作 Id→勾选明细。</summary>
        public static List<HealthResult> Run(HealthTrigger trigger, HealthActionCatalog catalog,
            IDictionary<string, string[]> selected)
        {
            var results = new List<HealthResult>();
            foreach (IHealthAction action in catalog.All)
            {
                HealthReport report = null;
                try { report = action.Analyze(); }
                catch (Exception ex)
                {
                    results.Add(Failure(action.Id, "扫描异常：" + ex.GetType().Name));
                    continue;
                }
                if (trigger == HealthTrigger.Auto && !action.AllowAuto)
                {
                    results.Add(new HealthResult
                    {
                        ActionId = action.Id, Outcome = HealthOutcome.Skipped,
                        ItemCount = report != null ? report.Findings.Count : 0,
                        Summary = "仅扫描，发现 " + (report != null ? report.Findings.Count : 0) + " 项"
                    });
                    var cycle = action as IHealthAutoCycle;
                    if (cycle != null)
                    {
                        try { cycle.OnAutoCycle(); }
                        catch (Exception ex) { Logger.LogFailure("维护自动周期：" + action.Id, ex); }
                    }
                    continue;
                }
                string[] ids = null;
                if (trigger == HealthTrigger.Manual && selected != null)
                    selected.TryGetValue(action.Id, out ids);
                HealthResult r;
                try { r = action.Execute(ids); }
                catch (Exception ex) { r = Failure(action.Id, "执行异常：" + ex.GetType().Name); }
                results.Add(r);
            }
            foreach (HealthResult r in results)
                HealthHistory.Append(ToRecord(trigger, r));
            return results;
        }

        /// <summary>手动禁用类入口：只执行指定动作（带勾选项），其他动作不受影响。</summary>
        public static HealthResult RunSelected(HealthActionCatalog catalog, string actionId, string[] selectedIds)
        {
            IHealthAction action = catalog.Find(actionId);
            HealthResult r;
            if (action == null) r = Failure(actionId, "动作未注册");
            else
            {
                try { r = action.Execute(selectedIds); }
                catch (Exception ex) { r = Failure(actionId, "执行异常：" + ex.GetType().Name); }
            }
            HealthHistory.Append(ToRecord(HealthTrigger.Manual, r));
            return r;
        }

        /// <summary>单条还原：payloadLine 为一行负载。成功追加一条 Undo 历史记录。</summary>
        public static bool UndoSingle(HealthActionCatalog catalog, string actionId, string payloadLine, out string error)
        {
            error = null;
            IHealthAction action = catalog.Find(actionId);
            if (action == null || !action.CanUndo) { error = "该动作不支持还原"; return false; }
            bool ok;
            try { ok = action.Undo(payloadLine, out error); }
            catch (Exception ex) { error = ex.GetType().Name; ok = false; }
            if (ok)
            {
                HealthHistory.Append(new HealthRecord
                {
                    Time = DateTime.Now, Trigger = "Undo", ActionId = actionId,
                    Outcome = HealthOutcome.Success, ItemCount = 1,
                    Summary = "已还原：" + HealthEsc.Unesc(payloadLine.Split('\t')[1])
                });
            }
            return ok;
        }

        private static HealthRecord ToRecord(HealthTrigger trigger, HealthResult r)
        {
            return new HealthRecord
            {
                Time = DateTime.Now,
                Trigger = trigger == HealthTrigger.Auto ? "Auto" : "Manual",
                ActionId = r.ActionId, Outcome = r.Outcome,
                FreedBytes = r.FreedBytes, ItemCount = r.ItemCount,
                Summary = r.Outcome == HealthOutcome.Failed ? (r.Error ?? "失败") : (r.Summary ?? ""),
                UndoPayload = r.UndoPayload ?? ""
            };
        }

        private static HealthResult Failure(string actionId, string error)
        {
            return new HealthResult { ActionId = actionId, Outcome = HealthOutcome.Failed, Error = error };
        }
    }
}
```

`src/Core/Scenario/Health/HealthCatalog.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 默认维护动作目录：当前注册着色器缓存与启动项审查；新动作在此加一行

namespace CaelusApp
{
    internal static class HealthCatalog
    {
        private static readonly object lk = new object();
        private static HealthActionCatalog shared;

        public static HealthActionCatalog Shared
        {
            get
            {
                lock (lk)
                {
                    if (shared == null)
                    {
                        var c = new HealthActionCatalog();
                        c.Register(new ShaderCacheAction());
                        c.Register(new StartupAuditAction());
                        shared = c;
                    }
                    return shared;
                }
            }
        }
    }
}
```

注意：此步骤先注释掉 `HealthCatalog` 里两行 Register（`ShaderCacheAction`/`StartupAuditAction` 在 Task 2/3 才存在），或把本文件留到 Task 2 再建。**推荐：本任务不建 `HealthCatalog.cs`**，Task 2 建。

- [ ] **Step 4: 跑门禁确认通过**

Run: `cmd //c dev.cmd test`
Expected: 无 FAIL 行；TOTAL 计数 +6（254→260）。

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/Health/ tests/SelfTests.HealthAction.cs tests/SelfTests.cs
git commit -m "feat(health): 维护动作框架三件套——IHealthAction 契约 + Runner 编排（Auto/Manual 双路径+故障隔离）+ 追加式 TSV 历史（50 条截断）"
```

---

### Task 2: ShaderCacheAction + HealthCare 调度改接 Runner

**Files:**
- Create: `src/Core/Scenario/Health/ShaderCacheAction.cs`、`src/Core/Scenario/Health/HealthCatalog.cs`
- Modify: `src/Core/Scenario/HealthCare.cs`（RunIfDue 重写）
- Modify: `src/Core/Scenario/StartupAudit.cs`（Esc/Unesc 改为委托 HealthEsc）
- Test: `tests/SelfTests.HealthCare.cs`（扩充）、`tests/SelfTests.cs`（注册）

- [ ] **Step 1: 写失败的测试**

在 `tests/SelfTests.HealthCare.cs` 末尾（命名空间闭合前）加：

```csharp
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
```

（`FakeAction` 在 `tests/SelfTests.HealthAction.cs` 已定义，同 namespace 直接可用。）

在 `tests/SelfTests.cs` 的 `Run` 注册：

```csharp
            test("维护动作：着色器缓存阈值跳过与清理", TestShaderCacheActionThreshold);
            test("维护调度：RunIfDue 经 Runner 的到点/让路语义", TestHealthCareRunIfDueViaRunner);
```

- [ ] **Step 2: 跑门禁确认失败**

Run: `cmd //c dev.cmd test`
Expected: 构建失败（`ShaderCacheAction`、`HealthCare.CatalogOverride` 不存在）。

- [ ] **Step 3: 实现**

新建 `src/Core/Scenario/Health/ShaderCacheAction.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 维护动作·着色器缓存清理：包原有 >64MB 才清的阈值逻辑

namespace CaelusApp
{
    internal sealed class ShaderCacheAction : IHealthAction
    {
        internal const long Threshold = 64L * 1024 * 1024;

        /// <summary>测试挂钩：隔离真实文件系统（生产为 null 走 ShaderCache 真实实现）</summary>
        internal static System.Func<long> MeasureHook;
        internal static System.Func<CacheSweep.Result> CleanHook;

        public string Id { get { return "shader-cache"; } }
        public string TitleKey { get { return "health.shader.title"; } }
        public string DescKey { get { return "health.shader.desc"; } }
        public bool AllowAuto { get { return true; } }
        public bool CanUndo { get { return false; } }

        public HealthReport Analyze()
        {
            long bytes = Measure();
            var r = new HealthReport { ActionId = Id, Bytes = bytes };
            if (bytes > Threshold)
                r.Findings.Add(new HealthFinding { Id = "cache", Label = "着色器缓存", Detail = CacheSweep.FmtBytes(bytes) });
            return r;
        }

        public HealthResult Execute(string[] selectedIds)
        {
            long before = Measure();
            if (before <= Threshold)
                return new HealthResult { ActionId = Id, Outcome = HealthOutcome.Skipped, Summary = "着色器缓存不足 64MB，无需清理" };
            CacheSweep.Result r = Clean();
            return new HealthResult
            {
                ActionId = Id, Outcome = HealthOutcome.Success, FreedBytes = r.FreedBytes,
                Summary = "着色器缓存清理 " + CacheSweep.FmtBytes(before) + "（释放 " + CacheSweep.FmtBytes(r.FreedBytes) + "）"
            };
        }

        public bool Undo(string undoPayload, out string error) { error = "清理不可还原"; return false; }
        public System.Collections.Generic.List<HealthFinding> ListDisabled() { return new System.Collections.Generic.List<HealthFinding>(); }

        private static long Measure() { return MeasureHook != null ? MeasureHook() : ShaderCache.MeasureBytes(); }
        private static CacheSweep.Result Clean() { return CleanHook != null ? CleanHook() : ShaderCache.Clean(); }
    }
}
```

新建 `src/Core/Scenario/Health/HealthCatalog.cs`（内容见 Task 1 Step 3，两行 Register 现在真实启用）。

重写 `src/Core/Scenario/HealthCare.cs` 的 `RunIfDue`（保留 StartAuto/StopAuto/TickAuto/IsDue/IntervalDays 原样）：

```csharp
        /// <summary>测试挂钩：替换动作目录（生产为 null 用 HealthCatalog.Shared）</summary>
        internal static HealthActionCatalog CatalogOverride;

        /// <summary>到点则经维护框架执行一轮。由独立调度（StartAuto）调用。</summary>
        public static void RunIfDue()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!IsDue(Settings.LoadStr("HealthLastRun", ""), IntervalDays(), DateTime.Now)) return;

            // 到点判定后、执行前再让路一次：大缓存清理可持续数十秒，
            // 恰在此间隙启动的游戏不能撞上着色器全量重编译
            Func<bool> defer = ShouldDefer;
            if (defer != null && defer())
            {
                Logger.Log("健康维护：游戏进行中，本轮顺延到下个周期");
                return;   // 不写 HealthLastRun：下个 30 分钟周期继续尝试
            }

            try { HealthRunner.Run(HealthTrigger.Auto, CatalogOverride ?? HealthCatalog.Shared, null); }
            catch (Exception ex) { Logger.LogFailure("健康维护执行异常", ex); }

            Settings.SaveStr("HealthLastRun", today);
        }
```

同时删除 HealthCare 顶部不再用的 `using System.Collections.Generic;`（若编译警告）——注意保留 `using System;` 等。启动项新闻与基线提交已移交 `StartupAuditAction.OnAutoCycle`（Task 3 落地；**本任务期间新闻行为暂时消失，Task 3 补回**，两个任务同批合入不改行为）。

`src/Core/Scenario/StartupAudit.cs`：删私有 `Esc`/`Unesc`，两处调用改为 `HealthEsc.Esc(...)` / `HealthEsc.Unesc(...)`（`LoadBaseline` 与 `SaveBaseline` 内各一处，方法体内替换）。现有 `TestStartupAuditEscapingRoundtrip` 是回归保底。

- [ ] **Step 4: 跑门禁确认通过**

Run: `cmd //c dev.cmd test`
Expected: 无 FAIL；TOTAL +2（260→262）。

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/Health/ src/Core/Scenario/HealthCare.cs src/Core/Scenario/StartupAudit.cs tests/SelfTests.HealthCare.cs tests/SelfTests.cs
git commit -m "feat(health): 着色器清理迁入动作框架，HealthCare 到点判定改接 HealthRunner（让路/间隔语义不变）"
```

---

### Task 3: StartupAuditAction（禁用/还原）+ 自测

**Files:**
- Create: `src/Core/Scenario/Health/StartupAuditAction.cs`
- Test: `tests/SelfTests.HealthCare.cs`（扩充）、`tests/SelfTests.cs`（注册）

设计要点（规格 §3.2 + 计划头偏差 1/2/3）：
- Analyze 纯：diff 返回发现项；**不写新闻不动基线**
- `IHealthAutoCycle.OnAutoCycle()`：diff → 有新增写 `HealthStartupNews`（沿用 HealthCare 旧逻辑）→ 提交基线
- Execute(selectedIds)：null/空 → Skipped；系统项二次拒绝；逐项禁用，负载入历史与备份存储
- 备份存储：注册表值移 `HKCU\Software\Caelus\DisabledStartup\<HKCU|HKLM>` 子键；lnk 移 `Paths.Data\DisabledStartup\`
- 单行负载：`Esc(Hive)\tEsc(Name)\tEsc(Data)\tEsc(Extra)`（lnk 的 Extra=原完整路径，注册表 Extra 空）
- 测试隔离：注册表三个操作 + 启动文件夹/备份目录全部可挂钩

- [ ] **Step 1: 写失败的测试**

`tests/SelfTests.HealthCare.cs` 追加：

```csharp
        // —— 启动项禁用/还原：注册表走内存假店、lnk 走临时目录 ——
        private static System.Collections.Generic.Dictionary<string, string> fakeReg;

        private static void InstallFakeStartupStore(string dir)
        {
            fakeReg = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            StartupAuditAction.StartupFolderOverride = Path.Combine(dir, "startup");
            StartupAuditAction.BackupDirOverride = Path.Combine(dir, "backup");
            Directory.CreateDirectory(StartupAuditAction.StartupFolderOverride);
        }

        private static void UninstallFakeStartupStore()
        {
            StartupAuditAction.ReadRunValueHook = null;
            StartupAuditAction.WriteRunValueHook = null;
            StartupAuditAction.DeleteRunValueHook = null;
            StartupAuditAction.BackupReadHook = null;
            StartupAuditAction.BackupWriteHook = null;
            StartupAuditAction.BackupDeleteHook = null;
            StartupAuditAction.StartupFolderOverride = null;
            StartupAuditAction.BackupDirOverride = null;
            fakeReg = null;
        }

        private static void TestStartupDisablePlanFilter()
        {
            var a = new StartupAuditAction();
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
                Eq(HealthOutcome.Skipped, a.Execute(null).Outcome);          // 未勾选
                Eq(HealthOutcome.Skipped, a.Execute(new string[0]).Outcome);

                HealthResult r = a.Execute(new[] { "HKCU\\Run|MyApp" });
                Eq(HealthOutcome.Success, r.Outcome);
                Eq(1, r.ItemCount);
                Eq(false, fakeReg.ContainsKey("HKCU\\Run|MyApp"));           // 原值已删
                Eq("C:\\apps\\my.exe /q", fakeReg["bak|HKCU\\Run|MyApp"]);   // 进备份
                Eq(true, r.UndoPayload.Length > 0);

                Eq(1, a.ListDisabled().Count);
                Eq("MyApp", a.ListDisabled()[0].Label);

                string err;
                Eq(true, a.Undo(a.ListDisabled()[0].Id, out err));
                Eq("C:\\apps\\my.exe /q", fakeReg["HKCU\\Run|MyApp"]);       // 还原
                Eq(false, fakeReg.ContainsKey("bak|HKCU\\Run|MyApp"));
                Eq(0, a.ListDisabled().Count);

                HealthResult dup = a.Execute(new[] { "HKCU\\Run|MyApp" });   // 重复禁用幂等
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
                Eq(HealthOutcome.Skipped, r.Outcome);                        // 系统项被拒
                Eq(true, fakeReg.ContainsKey("HKLM\\Run|Audio"));            // 原值未动
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
                Eq(true, File.Exists(lnk));                                  // 移回
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
                fakeReg["HKCU\\Run|App"] = "C:\\new.exe";                    // 外部写回同名值
                string err;
                Eq(false, a.Undo(a.ListDisabled()[0].Id, out err));          // 不覆盖、报错
                Eq(true, err != null);
                Eq("C:\\new.exe", fakeReg["HKCU\\Run|App"]);
            }
            finally { UninstallFakeStartupStore(); DeleteTempDir(dir); }
        }

        private static void TestStartupAutoCycleNewsAndBaseline()
        {
            // 自动周期：新发现写 HealthStartupNews + 提交基线（Analyze 保持纯，不动基线）
            string dir = NewTempDir("sa-cycle");
            InstallFakeStartupStore(dir);
            string oldBaseline = StartupAuditAction.BaselinePathOverride;
            StartupAuditAction.BaselinePathOverride = Path.Combine(dir, "base.tsv");
            try
            {
                fakeReg["HKCU\\Run|NewSpy"] = "C:\\spy\\new.exe";
                var a = new StartupAuditAction();
                // 首轮：基线为空 → 静默建基线，不报新闻
                HealthReport r1 = a.Analyze();
                Eq(true, r1.Findings.Count >= 0);   // 纯扫描不报错即可
                ((IHealthAutoCycle)a).OnAutoCycle();
                Eq("", Settings.LoadStr("HealthStartupNews", ""));

                // 次轮：新增一个 → 新闻出现
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
```

注意：Analyze/OnAutoCycle 需要扫描「当前启动项」——为可测，`StartupAuditAction` 内部不直接调 `StartupAudit.ScanCurrent()`，而是走可挂钩的 `ScanCurrentHook`（生产 null → 真实扫描；测试喂 fakeReg 派生列表）。在 `InstallFakeStartupStore` 里补：

```csharp
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
```

`UninstallFakeStartupStore` 里补 `StartupAuditAction.ScanCurrentHook = null;`。

注册（`tests/SelfTests.cs`）：

```csharp
            test("启动项禁用：系统项判定", TestStartupDisablePlanFilter);
            test("启动项禁用：注册表项禁用与还原往返", TestStartupDisableRegistryRoundtrip);
            test("启动项禁用：系统项拒绝禁用", TestStartupDisableSystemItemRefused);
            test("启动项禁用：启动文件夹 lnk 移动与还原", TestStartupDisableLnkRoundtrip);
            test("启动项禁用：还原目标被占用不覆盖", TestStartupUndoTargetOccupied);
            test("启动项审查：自动周期写新闻并提交基线", TestStartupAutoCycleNewsAndBaseline);
```

- [ ] **Step 2: 跑门禁确认失败**

Run: `cmd //c dev.cmd test`
Expected: 构建失败（`StartupAuditAction` 不存在）。

- [ ] **Step 3: 实现 `src/Core/Scenario/Health/StartupAuditAction.cs`**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 维护动作·启动项审查：新发现报告 + 只禁不删（备份可还原）

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CaelusApp
{
    internal sealed class StartupAuditAction : IHealthAction, IHealthAutoCycle
    {
        public string Id { get { return "startup-audit"; } }
        public string TitleKey { get { return "health.startup.title"; } }
        public string DescKey { get { return "health.startup.desc"; } }
        public bool AllowAuto { get { return false; } }   // 自动周期只扫描报告
        public bool CanUndo { get { return true; } }

        // —— 测试挂钩（生产全为 null 走真实注册表/文件）——
        internal static Func<List<StartupAudit.Entry>> ScanCurrentHook;
        internal static Func<string, string, string> ReadRunValueHook;         // (hive,name)→data|null
        internal static Func<string, string, string, string> WriteRunValueHook; // (hive,name,data)→error|null（同名已存在须报错）
        internal static Func<string, string, string> DeleteRunValueHook;        // →error|null
        internal static Func<string, string, string> BackupReadHook;
        internal static Func<string, string, string, string> BackupWriteHook;   // 备份允许覆盖
        internal static Func<string, string, string> BackupDeleteHook;
        internal static string StartupFolderOverride;
        internal static string BackupDirOverride;
        internal static string BaselinePathOverride;

        private const string BackupKeyPath = @"SOFTWARE\Caelus\DisabledStartup";

        public HealthReport Analyze()
        {
            var current = Scan();
            var baseline = StartupAudit.LoadBaseline(BaselinePath());
            var added = StartupAudit.DiffNew(current, baseline);
            var report = new HealthReport { ActionId = Id };
            foreach (StartupAudit.Entry e in added)
                report.Findings.Add(new HealthFinding
                {
                    Id = e.Source + "|" + e.Name,
                    Label = e.Name,
                    Detail = e.Source == "StartupFolder" ? "启动文件夹" : e.Command,
                    Risky = IsSystemItem(e)
                });
            return report;
        }

        /// <summary>自动周期职责：新发现写新闻（沿用旧 HealthCare 行为）+ 提交基线。</summary>
        public void OnAutoCycle()
        {
            var current = Scan();
            string bp = BaselinePath();
            var baseline = StartupAudit.LoadBaseline(bp);
            var added = StartupAudit.DiffNew(current, baseline);
            if (baseline.Count > 0 && added.Count > 0)
            {
                var names = new List<string>();
                foreach (StartupAudit.Entry e in added) names.Add(e.Name + "（" + e.Source + "）");
                string news = string.Join("、", names.ToArray());
                if (news.Length > 300) news = news.Substring(0, 300) + "...";
                Settings.SaveStr("HealthStartupNews", news);
                Logger.Log("健康维护：发现 " + added.Count + " 个新启动项：" + news);
            }
            StartupAudit.SaveBaseline(bp, current);
        }

        public HealthResult Execute(string[] selectedIds)
        {
            if (selectedIds == null || selectedIds.Length == 0)
                return new HealthResult { ActionId = Id, Outcome = HealthOutcome.Skipped, Summary = "未勾选启动项（自动维护从不禁用启动项）" };
            var want = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
            int done = 0;
            var payloadLines = new List<string>();
            var names = new List<string>();
            foreach (StartupAudit.Entry e in Scan())
            {
                string id = e.Source + "|" + e.Name;
                if (!want.Contains(id)) continue;
                if (IsSystemItem(e)) continue;   // 二次校验：UI 被绕过也禁不了系统项
                string payload;
                string err;
                if (!DisableOne(e, out payload, out err))
                    return new HealthResult { ActionId = Id, Outcome = HealthOutcome.Failed, Error = "禁用 " + e.Name + " 失败：" + err };
                done++;
                payloadLines.Add(payload);
                names.Add(e.Name);
            }
            if (done == 0)
                return new HealthResult { ActionId = Id, Outcome = HealthOutcome.Skipped, Summary = "勾选项均已不存在或受系统保护" };
            return new HealthResult
            {
                ActionId = Id, Outcome = HealthOutcome.Success, ItemCount = done,
                Summary = "已禁用 " + done + " 个启动项：" + string.Join("、", names.ToArray()),
                UndoPayload = string.Join("\n", payloadLines.ToArray())
            };
        }

        public List<HealthFinding> ListDisabled()
        {
            var list = new List<HealthFinding>();
            foreach (string hive in new[] { "HKCU\\Run", "HKLM\\Run" })
            {
                foreach (KeyValuePair<string, string> kv in BackupEnum(hive))
                {
                    list.Add(new HealthFinding
                    {
                        Id = HealthEsc.Esc(hive) + "\t" + HealthEsc.Esc(kv.Key) + "\t" + HealthEsc.Esc(kv.Value) + "\t",
                        Label = kv.Key, Detail = kv.Value
                    });
                }
            }
            try
            {
                string dir = BackupDir();
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        string orig = Path.Combine(StartupFolder(), Path.GetFileName(f));
                        list.Add(new HealthFinding
                        {
                            Id = HealthEsc.Esc("StartupFolder") + "\t" + HealthEsc.Esc(Path.GetFileName(f)) + "\t\t" + HealthEsc.Esc(orig),
                            Label = Path.GetFileName(f), Detail = "启动文件夹"
                        });
                    }
            }
            catch { }
            return list;
        }

        public bool Undo(string undoPayload, out string error)
        {
            error = null;
            string[] f = (undoPayload ?? "").Split('\t');
            if (f.Length < 4) { error = "还原负载损坏"; return false; }
            string source = HealthEsc.Unesc(f[0]);
            string name = HealthEsc.Unesc(f[1]);
            string data = HealthEsc.Unesc(f[2]);
            string extra = HealthEsc.Unesc(f[3]);
            if (source == "StartupFolder")
            {
                string bak = Path.Combine(BackupDir(), name);
                if (!File.Exists(bak)) { error = "备份文件已不存在"; return false; }
                if (File.Exists(extra)) { error = "启动文件夹已存在同名文件"; return false; }
                try
                {
                    Directory.CreateDirectory(StartupFolder());
                    File.Move(bak, extra);
                    return true;
                }
                catch (Exception ex) { error = ex.GetType().Name; return false; }
            }
            // 注册表还原：目标已有同名值不覆盖
            if (ReadRunValue(source, name) != null) { error = "目标位置已有同名值，未覆盖"; return false; }
            string werr = WriteRunValue(source, name, data);
            if (werr != null) { error = werr; return false; }
            BackupDelete(source, name);
            return true;
        }

        /// <summary>系统/微软项判定（纯逻辑可单测）：命令路径在 Windows 目录或含 Microsoft 目录段。</summary>
        internal static bool IsSystemItem(StartupAudit.Entry e)
        {
            string c = e != null ? e.Command ?? "" : "";
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (win.Length > 0 && c.IndexOf(win, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return c.IndexOf("\\Microsoft\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // —— 以下为效果层：真实注册表/文件操作，测试经挂钩替换 ——

        private static bool DisableOne(StartupAudit.Entry e, out string payload, out string error)
        {
            payload = null; error = null;
            if (e.Source == "StartupFolder")
            {
                string src = Path.Combine(StartupFolder(), e.Name);
                string dst = Path.Combine(BackupDir(), e.Name);
                if (!File.Exists(src)) { error = "文件已不存在"; return false; }
                try
                {
                    Directory.CreateDirectory(BackupDir());
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                }
                catch (Exception ex) { error = ex.GetType().Name; return false; }
                payload = HealthEsc.Esc(e.Source) + "\t" + HealthEsc.Esc(e.Name) + "\t\t" + HealthEsc.Esc(src);
                return true;
            }
            string data = ReadRunValue(e.Source, e.Name);
            if (data == null) { error = "值已不存在"; return false; }
            error = BackupWrite(e.Source, e.Name, data);
            if (error != null) return false;
            error = DeleteRunValue(e.Source, e.Name);
            if (error != null) { BackupDelete(e.Source, e.Name); return false; }   // 删失败回滚备份
            payload = HealthEsc.Esc(e.Source) + "\t" + HealthEsc.Esc(e.Name) + "\t" + HealthEsc.Esc(data) + "\t";
            return true;
        }

        private static List<StartupAudit.Entry> Scan()
        {
            if (ScanCurrentHook != null) return ScanCurrentHook();
            return StartupAudit.ScanCurrent();
        }

        private static string BaselinePath() { return BaselinePathOverride ?? StartupAudit.BaselinePath; }
        private static string StartupFolder()
        {
            return StartupFolderOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        }
        private static string BackupDir()
        {
            return BackupDirOverride ?? Path.Combine(Paths.Data, "DisabledStartup");
        }

        private static string ReadRunValue(string hive, string name)
        {
            if (ReadRunValueHook != null) return ReadRunValueHook(hive, name);
            try
            {
                using (RegistryKey k = OpenRunKey(hive, false))
                {
                    if (k == null) return null;
                    object v = k.GetValue(name, null);
                    return v == null ? null : Convert.ToString(v);
                }
            }
            catch { return null; }
        }

        private static string WriteRunValue(string hive, string name, string data)
        {
            if (WriteRunValueHook != null) return WriteRunValueHook(hive, name, data);
            try
            {
                using (RegistryKey k = OpenRunKey(hive, true))
                {
                    if (k == null) return "无法打开 Run 键";
                    if (k.GetValue(name, null) != null) return "目标位置已有同名值";
                    k.SetValue(name, data);
                    return null;
                }
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static string DeleteRunValue(string hive, string name)
        {
            if (DeleteRunValueHook != null) return DeleteRunValueHook(hive, name);
            try
            {
                using (RegistryKey k = OpenRunKey(hive, true))
                {
                    if (k == null) return "无法打开 Run 键";
                    k.DeleteValue(name, false);
                    return null;
                }
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static RegistryKey OpenRunKey(string hive, bool writable)
        {
            const string runPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            if (hive == "HKLM\\Run") return Registry.LocalMachine.OpenSubKey(runPath, writable);
            return Registry.CurrentUser.OpenSubKey(runPath, writable);
        }

        private static string BackupRead(string hive, string name)
        {
            if (BackupReadHook != null) return BackupReadHook(hive, name);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(BackupKeyPath + "\\" + HiveTag(hive)))
                    return k == null ? null : Convert.ToString(k.GetValue(name, null));
            }
            catch { return null; }
        }

        private static string BackupWrite(string hive, string name, string data)
        {
            if (BackupWriteHook != null) return BackupWriteHook(hive, name, data);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(BackupKeyPath + "\\" + HiveTag(hive)))
                {
                    if (k == null) return "无法创建备份键";
                    k.SetValue(name, data);
                    return null;
                }
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static string BackupDelete(string hive, string name)
        {
            if (BackupDeleteHook != null) return BackupDeleteHook(hive, name);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(BackupKeyPath + "\\" + HiveTag(hive), true))
                {
                    if (k != null) k.DeleteValue(name, false);
                    return null;
                }
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static List<KeyValuePair<string, string>> BackupEnum(string hive)
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(BackupKeyPath + "\\" + HiveTag(hive)))
                {
                    if (k == null) return list;
                    foreach (string n in k.GetValueNames())
                        list.Add(new KeyValuePair<string, string>(n, Convert.ToString(k.GetValue(n, ""))));
                }
            }
            catch { }
            return list;
        }

        private static string HiveTag(string hive) { return hive == "HKLM\\Run" ? "HKLM" : "HKCU"; }
    }
}
```

测试的 `ListDisabled` 假注册表走 `BackupEnum`——注意 `BackupEnum` 目前直接读真实注册表，测试挂钩没盖住。补一个 `BackupEnumHook`：

```csharp
        internal static Func<string, List<KeyValuePair<string, string>>> BackupEnumHook;
```
`BackupEnum` 开头加 `if (BackupEnumHook != null) return BackupEnumHook(hive);`，`InstallFakeStartupStore` 加：

```csharp
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
```
`UninstallFakeStartupStore` 加 `StartupAuditAction.BackupEnumHook = null;`。

同时 `HealthCatalog.cs` 已在 Task 2 注册本动作，无需再改。`Program.cs`/`WpfRuntime.cs` 的 HealthCare 接线不变（RunIfDue 内部已改接 Runner）。

- [ ] **Step 4: 跑门禁确认通过**

Run: `cmd //c dev.cmd test`
Expected: 无 FAIL；TOTAL +6（262→268）。

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/Health/StartupAuditAction.cs tests/SelfTests.HealthCare.cs tests/SelfTests.cs
git commit -m "feat(health): 启动项审查升级为可禁用动作——只禁不删（备份键/备份目录）、系统项二次拒绝、还原不覆盖同名值"
```

---

### Task 4: 详情页维护中心卡 + 启动项审查卡（WPF）

**Files:**
- Modify: `wpf/Views/ScenarioDetailView.xaml`（插入两张卡，Daily 可见）
- Modify: `wpf/Views/ScenarioDetailView.xaml.cs`（Click 处理器 + RiseIn）
- Modify: `wpf/ScenarioViewModel.cs`（ScenarioDetailViewModel 健康区）
- Modify: `src/Platform/Lang.cs`（新键）
- Test: 无新逻辑单测（VM 组成文本为视图层）；门禁回归

- [ ] **Step 1: ScenarioDetailViewModel 加健康区**

`wpf/ScenarioViewModel.cs` 的 `ScenarioDetailViewModel` 内加字段与属性（沿用现有 SetProperty/Raise 模式）：

```csharp
        // —— 维护中心（仅 Daily 页）——
        private string healthSummaryText = "—";
        private string healthRunHint = "";
        private bool healthRunEnabled = true;
        private long lastHealthRunTicks;
        private bool healthZoneLoaded;

        public bool HealthZoneVisible { get { return !isDev; } }
        public string HealthSummaryText { get { return healthSummaryText; } private set { SetProperty(ref healthSummaryText, value, "HealthSummaryText"); } }
        public string HealthRunHint { get { return healthRunHint; } private set { SetProperty(ref healthRunHint, value, "HealthRunHint"); } }
        public bool HealthRunEnabled { get { return healthRunEnabled; } private set { SetProperty(ref healthRunEnabled, value, "HealthRunEnabled"); } }
        public ObservableCollection<HealthHistoryRow> HealthHistory { get; private set; }
        public ObservableCollection<StartupFindingRow> StartupFindings { get; private set; }
        public ObservableCollection<StartupFindingRow> StartupDisabled { get; private set; }
```

构造函数 `SourceRows = new ObservableCollection<...>()` 旁补：

```csharp
            HealthHistory = new ObservableCollection<HealthHistoryRow>();
            StartupFindings = new ObservableCollection<StartupFindingRow>();
            StartupDisabled = new ObservableCollection<StartupFindingRow>();
```

`Refresh()` 末尾（`RebuildRows();` 之后）加：

```csharp
            if (!isDev) RefreshHealthZone(false);
```

健康区方法（注意 ObservableCollection 只能在 UI 线程碰——`Refresh` 由 DispatcherTimer 驱动在 UI 线程；后台动作完成后经 Dispatcher 调 `RefreshHealthZone(true)`）：

```csharp
        /// <summary>维护区刷新。force=false 且已加载过则跳过（2 秒轮询不重复读 TSV/注册表）。</summary>
        public void RefreshHealthZone(bool force)
        {
            if (isDev) return;
            if (healthZoneLoaded && !force) return;
            healthZoneLoaded = true;

            bool gameHolds = source.Granted == ScenarioKind.Game;
            long now = DateTime.UtcNow.Ticks;
            bool cooldown = now - lastHealthRunTicks < 60L * TimeSpan.TicksPerSecond;
            HealthRunEnabled = !gameHolds && !cooldown;
            HealthRunHint = gameHolds ? "游戏进行中，维护自动顺延，结束后可手动执行"
                : cooldown ? "刚刚执行过，60 秒内可再次手动执行前请稍候" : "";

            var all = HealthHistory.LoadAll();
            HealthHistory.Clear();
            for (int i = all.Count - 1; i >= 0; i--)   // 最近在前
            {
                HealthRecord r = all[i];
                HealthHistory.Add(new HealthHistoryRow
                {
                    TimeText = r.Time.ToString("MM-dd HH:mm"),
                    TriggerText = r.Trigger == "Auto" ? "自动" : r.Trigger == "Undo" ? "还原" : "手动",
                    SummaryText = r.Summary ?? "",
                    Failed = r.Outcome == HealthOutcome.Failed
                });
            }
            if (all.Count == 0)
                HealthSummaryText = "还没有维护记录，点「立即执行」跑第一轮";
            else
            {
                HealthRecord last = all[all.Count - 1];
                HealthSummaryText = "上次维护 " + last.Time.ToString("MM-dd HH:mm") + "（"
                    + (last.Trigger == "Auto" ? "自动" : "手动") + "）：" + (last.Summary ?? "");
            }

            StartupFindings.Clear();
            try
            {
                var action = HealthCatalog.Shared.Find("startup-audit");
                if (action != null)
                {
                    HealthReport report = action.Analyze();
                    foreach (HealthFinding f0 in report.Findings)
                        StartupFindings.Add(new StartupFindingRow
                        {
                            Id = f0.Id, Label = f0.Label, Detail = f0.Detail,
                            Risky = f0.Risky, IsChecked = !f0.Risky
                        });
                }
            }
            catch { }
            StartupDisabled.Clear();
            try
            {
                var action = HealthCatalog.Shared.Find("startup-audit");
                if (action != null)
                    foreach (HealthFinding d in action.ListDisabled())
                        StartupDisabled.Add(new StartupFindingRow
                        {
                            Id = d.Id, Label = d.Label, Detail = d.Detail
                        });
            }
            catch { }
        }

        /// <summary>立即执行（代码后置在后台线程调用）。返回 true 表示本轮真的跑了。</summary>
        public bool RunHealthNowCore()
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - lastHealthRunTicks < 60L * TimeSpan.TicksPerSecond) return false;
            if (source.Granted == ScenarioKind.Game) return false;
            lastHealthRunTicks = now;
            try { HealthRunner.Run(HealthTrigger.Manual, HealthCatalog.Shared, null); return true; }
            catch (Exception ex) { Logger.LogFailure("手动维护执行失败", ex); return false; }
        }

        /// <summary>禁用所选启动项（后台线程调用）。返回结果摘要。</summary>
        public string DisableSelectedStartupCore()
        {
            var ids = new List<string>();
            foreach (StartupFindingRow r in StartupFindings)
                if (r.IsChecked) ids.Add(r.Id);
            if (ids.Count == 0) return "未勾选任何启动项";
            HealthResult r = HealthRunner.RunSelected(HealthCatalog.Shared, "startup-audit", ids.ToArray());
            return r.Outcome == HealthOutcome.Failed ? ("失败：" + r.Error) : r.Summary;
        }

        /// <summary>还原单条已禁用启动项（后台线程调用）。</summary>
        public string UndoStartupCore(string payloadLine)
        {
            string err;
            bool ok = HealthRunner.UndoSingle(HealthCatalog.Shared, "startup-audit", payloadLine, out err);
            return ok ? "已还原" : ("还原失败：" + err);
        }
```

两个行模型（放在 `ScenarioSourceRowViewModel` 旁边）：

```csharp
    internal sealed class HealthHistoryRow
    {
        public string TimeText { get; set; }
        public string TriggerText { get; set; }
        public string SummaryText { get; set; }
        public bool Failed { get; set; }
    }

    internal sealed class StartupFindingRow : ViewModelBase
    {
        private bool isChecked;
        public string Id { get; set; }
        public string Label { get; set; }
        public string Detail { get; set; }
        public bool Risky { get; set; }
        public bool IsChecked { get { return isChecked; } set { SetProperty(ref isChecked, value, "IsChecked"); } }
    }
```

注意 `DisableSelectedStartupCore` 在后台线程读 `StartupFindings`（UI 集合）——集合只读遍历在 Caelus 现有代码里与 DispatcherTimer 写入并发风险低，但稳妥起见先经 Dispatcher 取快照。代码后置处理器这样写（Step 3）。

- [ ] **Step 2: XAML 两张卡**

`wpf/Views/ScenarioDetailView.xaml`：在 `ZoneFocus` 之后、`ZoneNote` 之前插入（沿用现有 SettingsGroup/PolicyRow/PolicyToggle/GhostButton 样式令牌）：

```xml
      <StackPanel x:Name="ZoneHealth" Visibility="{Binding HealthZoneVisible, Converter={StaticResource BoolVis}}">
        <Border Style="{DynamicResource SettingsGroup}" Padding="16,14" Margin="0,0,0,16"
                AutomationProperties.Name="维护中心">
          <StackPanel>
            <DockPanel Margin="0,0,0,6">
              <Button DockPanel.Dock="Right" Content="立即执行" Click="OnHealthRunNow"
                      Style="{DynamicResource GhostButton}" Padding="12,6" VerticalAlignment="Center"
                      Margin="16,0,0,0" IsEnabled="{Binding HealthRunEnabled}"
                      AutomationProperties.Name="立即执行健康维护"/>
              <StackPanel>
                <TextBlock Text="维护中心" FontSize="{DynamicResource FontSizeCaption}" FontWeight="SemiBold"
                           Foreground="{DynamicResource TextPrimaryBrush}"/>
                <TextBlock Text="{Binding HealthSummaryText}" Margin="0,3,0,0"
                           FontSize="{DynamicResource FontSizeSmall}" TextWrapping="Wrap"
                           Foreground="{DynamicResource TextSecondaryBrush}"/>
              </StackPanel>
            </DockPanel>
            <TextBlock Text="{Binding HealthRunHint}" FontSize="{DynamicResource FontSizeXs}"
                       Foreground="{DynamicResource TextTertiaryBrush}" TextWrapping="Wrap"/>
            <ItemsControl ItemsSource="{Binding HealthHistory}" Margin="0,8,0,0">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <Border Style="{DynamicResource SettingsRow}" BorderThickness="0,0,0,1" Padding="0,8">
                    <DockPanel>
                      <TextBlock DockPanel.Dock="Left" Text="{Binding TimeText}" Width="84"
                                 FontSize="{DynamicResource FontSizeXs}" VerticalAlignment="Center"
                                 Foreground="{DynamicResource TextTertiaryBrush}"/>
                      <TextBlock DockPanel.Dock="Left" Text="{Binding TriggerText}" Width="40"
                                 FontSize="{DynamicResource FontSizeXs}" VerticalAlignment="Center"
                                 Foreground="{DynamicResource TextTertiaryBrush}"/>
                      <TextBlock Text="{Binding SummaryText}" FontSize="{DynamicResource FontSizeSmall}"
                                 VerticalAlignment="Center" TextWrapping="Wrap"
                                 Foreground="{DynamicResource TextSecondaryBrush}"/>
                    </DockPanel>
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </StackPanel>
        </Border>

        <Border Style="{DynamicResource SettingsGroup}" Padding="16,14" Margin="0,0,0,16"
                AutomationProperties.Name="启动项审查">
          <StackPanel>
            <DockPanel Margin="0,0,0,6">
              <Button DockPanel.Dock="Right" Content="禁用所选" Click="OnStartupDisable"
                      Style="{DynamicResource GhostButton}" Padding="12,6" VerticalAlignment="Center"
                      Margin="16,0,0,0" AutomationProperties.Name="禁用所选启动项"/>
              <StackPanel>
                <TextBlock Text="启动项审查" FontSize="{DynamicResource FontSizeCaption}" FontWeight="SemiBold"
                           Foreground="{DynamicResource TextPrimaryBrush}"/>
                <TextBlock Text="只禁不删：禁用项可随时还原。标记「不建议」的是系统组件。"
                           Margin="0,3,0,0" FontSize="{DynamicResource FontSizeXs}"
                           Foreground="{DynamicResource TextTertiaryBrush}" TextWrapping="Wrap"/>
              </StackPanel>
            </DockPanel>
            <ItemsControl ItemsSource="{Binding StartupFindings}">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <Border Style="{DynamicResource SettingsRow}" BorderThickness="0,0,0,1" Padding="0,8">
                    <DockPanel>
                      <CheckBox DockPanel.Dock="Left" IsChecked="{Binding IsChecked}"
                                VerticalAlignment="Center" Margin="0,0,10,0"
                                AutomationProperties.Name="{Binding Label}"/>
                      <TextBlock DockPanel.Dock="Right" Text="不建议" Margin="8,0,0,0"
                                 Visibility="{Binding Risky, Converter={StaticResource BoolVis}}"
                                 FontSize="{DynamicResource FontSizeXs}" VerticalAlignment="Center"
                                 Foreground="{DynamicResource WarningEdgeBrush}"/>
                      <StackPanel>
                        <TextBlock Text="{Binding Label}" FontSize="{DynamicResource FontSizeSmall}"
                                   Foreground="{DynamicResource TextPrimaryBrush}"/>
                        <TextBlock Text="{Binding Detail}" FontSize="{DynamicResource FontSizeXs}"
                                   Foreground="{DynamicResource TextTertiaryBrush}" TextWrapping="Wrap"/>
                      </StackPanel>
                    </DockPanel>
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock Text="已禁用" Margin="0,10,0,4" FontSize="{DynamicResource FontSizeSmall}"
                       FontWeight="SemiBold" Foreground="{DynamicResource TextSecondaryBrush}"
                       Visibility="{Binding StartupDisabled.Count, Converter={StaticResource BoolVis}}"/>
            <ItemsControl ItemsSource="{Binding StartupDisabled}">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <Border Style="{DynamicResource SettingsRow}" BorderThickness="0,0,0,1" Padding="0,8">
                    <DockPanel>
                      <Button DockPanel.Dock="Right" Content="还原" Click="OnStartupUndo" Tag="{Binding Id}"
                              Style="{DynamicResource GhostButton}" Padding="10,4"
                              FontSize="{DynamicResource FontSizeXs}" VerticalAlignment="Center" Margin="8,0,0,0"
                              AutomationProperties.Name="还原启动项"/>
                      <TextBlock Text="{Binding Label}" FontSize="{DynamicResource FontSizeSmall}"
                                 VerticalAlignment="Center" Foreground="{DynamicResource TextSecondaryBrush}"/>
                    </DockPanel>
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </StackPanel>
        </Border>
      </StackPanel>
```

`StartupDisabled.Count` 绑定 int→Visibility 需要转换器：BoolVis 不适用。改用 `Converters.cs` 里已有的非零转换器（查 `Converters.cs` 是否有 `IntToVisibility`/`CountToVisibility`；没有就在 Converters.cs 加 `ZeroToCollapsedConverter`：0→Collapsed，否则 Visible，并登记到本 UserControl.Resources）。**实施时先查 `wpf/Converters.cs` 复用现有转换器。**

- [ ] **Step 3: 代码后置处理器**

`wpf/Views/ScenarioDetailView.xaml.cs` 加（后台线程跑核心，Dispatcher 回 UI 刷新）：

```csharp
        private void OnHealthRunNow(object sender, RoutedEventArgs e)
        {
            ScenarioDetailViewModel vm = DataContext as ScenarioDetailViewModel;
            if (vm == null) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { vm.RunHealthNowCore(); } catch { }
                Dispatcher.BeginInvoke(new Action(delegate { vm.RefreshHealthZone(true); }));
            });
        }

        private void OnStartupDisable(object sender, RoutedEventArgs e)
        {
            ScenarioDetailViewModel vm = DataContext as ScenarioDetailViewModel;
            if (vm == null) return;
            // 勾选快照在 UI 线程取，后台线程只做效果层
            var ids = new System.Collections.Generic.List<string>();
            foreach (StartupFindingRow r in vm.StartupFindings)
                if (r.IsChecked) ids.Add(r.Id);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { vm.DisableSelectedStartupCore(ids); } catch { }
                Dispatcher.BeginInvoke(new Action(delegate { vm.RefreshHealthZone(true); }));
            });
        }

        private void OnStartupUndo(object sender, RoutedEventArgs e)
        {
            FrameworkElement fe = sender as FrameworkElement;
            ScenarioDetailViewModel vm = DataContext as ScenarioDetailViewModel;
            if (fe == null || vm == null) return;
            string payload = fe.Tag as string;
            if (string.IsNullOrEmpty(payload)) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { vm.UndoStartupCore(payload); } catch { }
                Dispatcher.BeginInvoke(new Action(delegate { vm.RefreshHealthZone(true); }));
            });
        }
```

`ScenarioDetailViewModel.DisableSelectedStartupCore` 签名改为接受快照：`public string DisableSelectedStartupCore(List<string> ids)`（去掉方法内对 StartupFindings 的遍历，开头 `if (ids == null || ids.Count == 0) return "未勾选任何启动项";`）。

`OnLoaded` 里 ZoneNote 之前补：

```csharp
            if (ZoneHealth != null && ZoneHealth.Visibility == Visibility.Visible)
                Motion.RiseIn(ZoneHealth, 280);
```

（ZoneFocus 原 280、ZoneNote 原 340；ZoneHealth 插入后 Dev 页 ZoneFocus 保持 280，Daily 页 ZoneHealth 用 280，ZoneNote 不变。）

- [ ] **Step 4: Lang 键登记**

`src/Platform/Lang.cs` 字典追加（对齐现有 `new[]{ "…" }` 单行格式）：

```csharp
            { "health.shader.title", new[]{ "着色器缓存清理" } },
            { "health.shader.desc", new[]{ "超过 64MB 时清理显卡着色器缓存，释放磁盘" } },
            { "health.startup.title", new[]{ "启动项审查" } },
            { "health.startup.desc", new[]{ "发现新增开机启动项；可勾选的项只禁不删、随时还原" } },
```

（IHealthAction 的 TitleKey/DescKey 当前主要供将来面板/多语言使用；详情页静态文案与相邻卡片一致直接硬编码中文——`TestEveryLangKeyIsDefined` 只卡 `Lang.T("…")` 引用，这四键先登记防后续引用漏登记。）

- [ ] **Step 5: 跑门禁 + 截图目检**

Run: `cmd //c dev.cmd test`
Expected: 无 FAIL（TOTAL 不变 268）。再 `cmd //c dev.cmd` 启动应用，打开「日常」详情页目检两张新卡（或 `--wpf-shot` 截图，见 README/scripts）。

- [ ] **Step 6: Commit**

```bash
git add wpf/Views/ScenarioDetailView.xaml wpf/Views/ScenarioDetailView.xaml.cs wpf/ScenarioViewModel.cs wpf/Converters.cs src/Platform/Lang.cs
git commit -m "feat(daily-ui): 详情页维护中心卡（结果/历史/立即执行/游戏让路禁用）+ 启动项审查卡（勾选禁用/已禁用还原）"
```

---

### Task 5: 日常名录自定义（DailyCatalog.CustomList + 设置页）

**Files:**
- Modify: `src/Core/Scenario/DailyCatalog.cs`
- Modify: `wpf/Views/SettingsView.xaml`（ZoneDaily 加编辑行）
- Modify: `wpf/Views/SettingsView.xaml.cs`（OnDailyCustomSave）
- Modify: `wpf/SettingsViewModel.cs`（DailyCustomInitial/SaveDailyCustom）
- Modify: `src/Platform/Lang.cs`
- Test: `tests/SelfTests.DailyCare.cs`、`tests/SelfTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/SelfTests.DailyCare.cs` 末尾追加：

```csharp
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
                Eq(false, DailyCatalog.NameMatches("bad row"));      // 原名样存储、大小写不敏感
                Eq(true, DailyCatalog.NameMatches("Bad Row"));
                // 内置名录不受影响
                Eq(true, DailyCatalog.NameMatches("chrome"));
            }
            finally { DailyCatalog.CustomList = old; }
        }
```

`tests/SelfTests.cs` 注册：

```csharp
            test("日常名录：自定义名录合并与坏行容错", TestDailyCatalogCustomList);
```

- [ ] **Step 2: 跑门禁确认失败**

Run: `cmd //c dev.cmd test` → 构建失败（`DailyCatalog.CustomList` 不存在）。

- [ ] **Step 3: 实现**

`DailyCatalog.cs` 加（照搬 `BuildCatalog` 模式，`Map()` 旁）：

```csharp
        private const string CustomKey = "CustomDailyProcs";
        private static HashSet<string> customNames;

        /// <summary>自定义日常进程名（分号/换行分隔），存注册表，设置页可编辑。无安装目录锚点，按名匹配。</summary>
        public static string CustomList
        {
            get { return Settings.LoadStr(CustomKey, ""); }
            set { Settings.SaveStr(CustomKey, value ?? ""); lock (Sync) customNames = null; }
        }

        private static HashSet<string> LoadCustom()
        {
            lock (Sync)
            {
                if (customNames != null) return customNames;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = Settings.LoadStr(CustomKey, "");
                if (raw != null)
                    foreach (string part in raw.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = part.Trim();
                        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
                        if (t.Length > 0) set.Add(t);
                    }
                customNames = set;
                return set;
            }
        }
```

`NameMatches` 改为：

```csharp
        public static bool NameMatches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string bare = StripExe(name);
            return Map().ContainsKey(bare) || LoadCustom().Contains(bare);
        }
```

`IsMatch` 改为：

```csharp
        public static bool IsMatch(string name, string path)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string bare = StripExe(name);
            Entry e;
            if (Map().TryGetValue(bare, out e))
            {
                if (string.IsNullOrEmpty(path)) return false;
                string full = path;
                try { full = Path.GetFullPath(path); } catch { }
                foreach (string prefix in e.Roots)
                    if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            return LoadCustom().Contains(bare);   // 自定义名录：无目录锚点，按名匹配
        }
```

设置页：`wpf/Views/SettingsView.xaml` ZoneDaily 的 `StartupNews` 行之前加一行（复制 DevCustom 行结构）：

```xml
            <Border Style="{DynamicResource PolicyRow}" BorderThickness="0,0,0,1"><StackPanel><DockPanel Margin="0,0,0,8"><Button x:Name="BtnDailyCustomSave" Content="{Binding DevCustomSaveText}" Click="OnDailyCustomSave" Style="{DynamicResource GhostButton}" Padding="10,5" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="保存自定义日常进程"/><TextBlock Text="{Binding DailyCustomTitle}" FontSize="{DynamicResource FontSizeCaption}" FontWeight="SemiBold" Foreground="{DynamicResource TextPrimaryBrush}" VerticalAlignment="Center"/></DockPanel><TextBox x:Name="TbDailyCustom" Text="{Binding DailyCustomInitial, Mode=OneTime}" Style="{DynamicResource InputBox}" TextWrapping="Wrap" AcceptsReturn="True" VerticalScrollBarVisibility="Auto" MinHeight="48" MaxHeight="112" FontFamily="{DynamicResource FontMono}" FontSize="{DynamicResource FontSizeMono}" AutomationProperties.Name="自定义日常进程列表"/><TextBlock Text="{Binding DailyCustomNote}" FontSize="{DynamicResource FontSizeSmall}" Foreground="{DynamicResource TextSecondaryBrush}" TextWrapping="Wrap" Margin="0,7,0,0"/></StackPanel></Border>
```

`wpf/Views/SettingsView.xaml.cs` 加：

```csharp
        private void OnDailyCustomSave(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            vm.SaveDailyCustom(TbDailyCustom.Text);
            Motion.Emphasize(PageFeedbackBanner);
        }
```

`wpf/SettingsViewModel.cs` 加（找 `DevCustomInitial` 相邻位置）：

```csharp
        public string DailyCustomTitle { get { return Lang.T("set.daily.custom.title"); } }
        public string DailyCustomNote { get { return Lang.T("set.daily.custom.note"); } }
        public string DailyCustomInitial { get { return DailyCatalog.CustomList; } }
        public void SaveDailyCustom(string text)
        {
            DailyCatalog.CustomList = text ?? "";
            ShowFeedback(Lang.T("set.daily.custom.saved"), "Success");
        }
```

`src/Platform/Lang.cs` 加：

```csharp
            { "set.daily.custom.title", new[]{ "自定义日常进程" } },
            { "set.daily.custom.note", new[]{ "内置名录之外的浏览器/办公/会议软件，一行或分号分隔一个进程名（不带 .exe 也行）。自定义项只按进程名匹配，不校验安装目录。" } },
            { "set.daily.custom.saved", new[]{ "自定义日常进程已保存。" } },
```

- [ ] **Step 4: 跑门禁确认通过**

Run: `cmd //c dev.cmd test`
Expected: 无 FAIL；TOTAL +1（268→269）。

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/DailyCatalog.cs wpf/Views/SettingsView.xaml wpf/Views/SettingsView.xaml.cs wpf/SettingsViewModel.cs src/Platform/Lang.cs tests/SelfTests.DailyCare.cs tests/SelfTests.cs
git commit -m "feat(daily): 日常家族名录开放自定义（CustomDailyProcs，对齐编译名录模式）+ 设置页编辑入口"
```

---

### Task 6: 电池联动电源滑块（PowerOverlay DC 续航档 + DailyCare 接线）

**Files:**
- Modify: `src/Core/Tweaks/PowerOverlay.cs`
- Modify: `src/Core/Scenario/DailyCare.cs`
- Test: `tests/SelfTests.DailyCare.cs`、`tests/SelfTests.PowerPlan.cs` 或新组、`tests/SelfTests.cs`

续航档 GUID：`961cc777-2547-4f9d-8174-7d86181b8a7a`（「更长的续航」overlay）。

- [ ] **Step 1: 写失败的测试**

`tests/SelfTests.DailyCare.cs` 追加：

```csharp
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
```

`tests/SelfTests.PowerPlan.cs` 追加（或就近放 PowerPlan 组）：

```csharp
        private static void TestPowerOverlaySaverConstants()
        {
            Eq("961cc777-2547-4f9d-8174-7d86181b8a7a", PowerOverlay.SaverGuidText);
            Eq(true, PowerOverlay.IsSaverGuid("961CC777-2547-4F9D-8174-7D86181B8A7A"));  // 大小写不敏感
            Eq(false, PowerOverlay.IsSaverGuid("ded574b5-45a0-4f42-8737-46345c09c238")); // 最佳性能 ≠ 续航
            Eq(false, PowerOverlay.IsSaverGuid(null));
            Eq(false, PowerOverlay.IsSaverGuid("garbage"));
        }
```

`tests/SelfTests.cs` 注册：

```csharp
            test("电池联动：脱电掌权激活续航档、回电与停止还原", TestDailyCareSaverApplyAndRestore);
            test("电池联动：电池开关关闭不动作", TestDailyCareSaverOffNoop);
            test("电源滑块：续航档 GUID 常量与判定", TestPowerOverlaySaverConstants);
```

- [ ] **Step 2: 跑门禁确认失败**

Run: `cmd //c dev.cmd test` → 构建失败（`BatterySaverApplyHook`、`PowerOverlay.SaverGuidText` 等不存在）。

- [ ] **Step 3: 实现**

`src/Core/Tweaks/PowerOverlay.cs` 加（`Max` 字段旁及类尾前）：

```csharp
        private static readonly Guid Saver = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private const string SaverSnapKey = "PowerOverlayDcSaverSnap";
        private const string SaverSnapAbsent = "\x1f";   // 快照哨兵：原本无 DC 值
        private static readonly HashSet<string> saverOwners = new HashSet<string>(StringComparer.Ordinal);

        internal const string OwnerDailyCare = "dailycare";

        /// <summary>续航档 GUID 文本（自测锚定，防误改）</summary>
        internal static string SaverGuidText { get { return Saver.ToString(); } }

        /// <summary>原始注册表串是否为续航档（纯逻辑可单测）</summary>
        internal static bool IsSaverGuid(string raw)
        {
            return ParseGuidOrNull(raw) == Saver;
        }

        /// <summary>日常养护·电池联动：DC 侧滑块切「更长的续航」。多占用方引用计数；
        /// 游戏档快照在位时让位不动（游戏优先）。</summary>
        public static bool ActivateDcSaver(string owner)
        {
            if (!Supported()) return false;
            lock (lk)
            {
                if (!SharedEffectClaim.Acquire(saverOwners, owner)) return true;
                if (Settings.LoadStr(SnapKey, "").Length > 0)
                {
                    SharedEffectClaim.Release(saverOwners, owner);
                    Logger.Log("电源滑块：游戏档占用中，续航档本轮让位");
                    return false;
                }
                string dcBefore = TryReadDcRaw();
                if (IsSaverGuid(dcBefore)) return true;   // 已是续航档：无快照也视为成功
                if (!Settings.SaveStr(SaverSnapKey, dcBefore ?? SaverSnapAbsent))
                {
                    SharedEffectClaim.Release(saverOwners, owner);
                    Logger.Log("电源滑块：续航档快照无法持久化，本轮未切换");
                    return false;
                }
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(SchemeKey, true))
                    {
                        if (key == null) throw new System.IO.IOException("SchemeKey unavailable");
                        key.SetValue(DcValue, Saver.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Settings.SaveStr(SaverSnapKey, "");
                    SharedEffectClaim.Release(saverOwners, owner);
                    Logger.Log("电源滑块：续航档写入失败（" + ex.GetType().Name + "）");
                    return false;
                }
                Logger.Log("电源滑块：电池档已切到「更长的续航」，回电或场景挂起时还原");
                return true;
            }
        }

        /// <summary>还原 DC 续航档。最后一个占用方释放才真正还原；快照保留失败则下次再试。</summary>
        public static bool RestoreDcSaver(string owner)
        {
            lock (lk)
            {
                if (!SharedEffectClaim.Release(saverOwners, owner)) return true;
                return RestoreDcSaverCore();
            }
        }

        private static bool RestoreDcSaverCore()
        {
            string saved = Settings.LoadStr(SaverSnapKey, "");
            if (saved.Length == 0) return true;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(SchemeKey, true))
                {
                    if (key == null) return false;
                    if (saved == SaverSnapAbsent) key.DeleteValue(DcValue, false);
                    else key.SetValue(DcValue, saved);
                }
            }
            catch { return false; }   // 快照保留，下次启动/释放继续
            Settings.SaveStr(SaverSnapKey, "");
            Logger.Log("电源滑块：电池档续航已还原");
            return true;
        }
```

`HealFromCrash` 改为：

```csharp
        public static void HealFromCrash()
        {
            if (Settings.LoadStr(SnapKey, "").Length > 0)
                if (Restore()) Logger.Log("检测到上次未还原的电源滑块设置，已恢复");
            // 续航档崩溃自愈：进程已死，占用记账无从谈起，清空后直接还原
            if (Settings.LoadStr(SaverSnapKey, "").Length > 0)
            {
                lock (lk) { SharedEffectClaim.ReleaseAll(saverOwners); }
                if (RestoreDcSaverCore()) Logger.Log("检测到上次未还原的电池续航档设置，已恢复");
            }
        }
```

文件顶部 `using System.Collections.Generic;` 若无则补。

`src/Core/Scenario/DailyCare.cs` 加：

```csharp
        /// <summary>测试挂钩：隔离真实注册表（生产为 null 走 PowerOverlay 真实实现）</summary>
        internal static Func<bool> BatterySaverApplyHook;
        internal static Func<bool> BatterySaverRestoreHook;

        private static bool SaverApply()
        {
            if (BatterySaverApplyHook != null) return BatterySaverApplyHook();
            try { return PowerOverlay.ActivateDcSaver(PowerOverlay.OwnerDailyCare); } catch { return false; }
        }

        private static bool SaverRestore()
        {
            if (BatterySaverRestoreHook != null) return BatterySaverRestoreHook();
            try { return PowerOverlay.RestoreDcSaver(PowerOverlay.OwnerDailyCare); } catch { return false; }
        }

        /// <summary>掌权且电池供电且开关开 → 续航档在位</summary>
        private void ApplyBatterySaverIfNeeded()
        {
            bool batt;
            lock (sync) batt = onBattery;
            if (!batt || !BatteryOn) return;
            SaverApply();
        }
```

接线三处：
1. `Grant()` 的 try 内（`MaybeShowBatteryBalloon();` 之后）加 `ApplyBatterySaverIfNeeded();`
2. `RefreshPowerState()` 的 `if (wasGranted)` 块内（QueueUserWorkItem 之前）加：

```csharp
            if (wasGranted)
            {
                if (batt) ApplyBatterySaverIfNeeded();
                else SaverRestore();
            }
```
（注意 `batt` 是该方法局部变量。）
3. `Suspend()` 故障隔离链加一步（`RestoreFamilyBoost` 那步之后）：

```csharp
            try { SaverRestore(); }
            catch (Exception ex) { failed++; Logger.LogFailure("日常优化挂起：还原电池续航档失败", ex); }
```

`MaybeShowBatteryBalloon` 的日志文案 `建议电源模式调至更长续航` 改为 `电池档电源滑块已切到更长续航`（行为已自动化）。

- [ ] **Step 4: 跑门禁确认通过**

Run: `cmd //c dev.cmd test`
Expected: 无 FAIL；TOTAL +3（269→272）。

- [ ] **Step 5: Commit**

```bash
git add src/Core/Tweaks/PowerOverlay.cs src/Core/Scenario/DailyCare.cs tests/SelfTests.DailyCare.cs tests/SelfTests.PowerPlan.cs tests/SelfTests.cs
git commit -m "feat(daily): 电池联动电源滑块——脱电切 DC 续航档（快照哨兵/游戏档让位/崩溃自愈），回电与挂起还原"
```

---

### Task 7: 门禁收口 + README 三语计数同步 + 真机验证

**Files:**
- Modify: `README.md`、`README.en.md`、`README.ja.md`
- Modify: `docs/superpowers/specs/2026-09-11-dailycare-health-framework-design.md`（偏差回写）

- [ ] **Step 1: 全量门禁**

Run: `cmd //c dev.cmd test`
Expected: 无 FAIL 行；记下 TOTAL 数（预期 272，以实际为准）。

- [ ] **Step 2: README 三语同步**

把三份 README 中的自测计数（254）全部替换为实际 TOTAL：badge（`自测-254 项 0 失败`）、正文「内置 254 项自测」等处。英文版 `254 self-tests`、日文版对应文案一并改。用 `grep -n "254" README*.md` 找全。

- [ ] **Step 3: 规格偏差回写**

在规格文档「状态」行更新为「已实施」，并把计划头部的三条偏差（Undo 单行负载签名 / ListDisabled 与备份存储枚举 / IHealthAutoCycle）追加一节「实施偏差记录」。

- [ ] **Step 4: 真机验证（手动三步）**

1. `cmd //c dev.cmd` 启动 → 日常详情页 → 「立即执行」→ 维护中心出现本轮记录（着色器跳过或清理 + 启动项仅扫描）
2. 若启动项审查卡有新发现：勾一项非系统项 → 禁用所选 → 已禁用分组出现 → 还原 → 消失
3. （笔记本才可行）拔电源：日志出现「电池档已切到更长的续航」；插回：还原日志。台式机此项 SKIP 并在提交说明里注明

- [ ] **Step 5: Commit + 推送**

```bash
git add README.md README.en.md README.ja.md docs/superpowers/specs/2026-09-11-dailycare-health-framework-design.md
git commit -m "docs: 维护框架收口——三语 README 自测计数同步 + 规格实施偏差回写"
```

---

## 自审记录

- 规格覆盖：§2 框架→Task 1/2；§3.1→Task 2；§3.2→Task 3；§4.1→Task 5；§4.2→Task 6；§5 UI→Task 4/5；§7 安全→各任务内联（故障隔离 Runner、快照冗余、系统项二次校验、让位/自愈、60s 节流）；§8 测试→各 Task Step 1 + Task 7
- 类型一致性：`HealthFinding/HealthReport/HealthResult/HealthRecord`、`IHealthAction.Undo(payload, out error)`、`ListDisabled()`、`HealthRunner.Run/RunSelected/UndoSingle`、`HealthHistory.FilePath/Append/LoadAll/Find`、`HealthCatalog.Shared`、`DailyCare.BatterySaverApplyHook/RestoreHook`、`PowerOverlay.SaverGuidText/IsSaverGuid/ActivateDcSaver/RestoreDcSaver/OwnerDailyCare`、`StartupAuditAction` 九个挂钩字段——全文已交叉核对一致
- 已知留白（实施时按指引处理）：Task 4 Step 2 的 Count→Visibility 转换器需先查 `wpf/Converters.cs` 复用；Task 4 按钮反馈复用 `Motion.Emphasize` 与否由实施者按相邻视图惯例决定
