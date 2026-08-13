# 场景扩展 P4：系统健康维护 + UI 完善 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补齐场景扩展的最后两块：系统健康维护（每日到点、仅 DailyCare 掌权期执行：着色器缓存清理 + 启动项审查基线对比）与 UI 完善（WinForms/WPF 双侧设置页、WinForms 概览页掌权场景指示）。

**Architecture:** 健康维护为静态类 `HealthCare` + `StartupAudit`（注册表 Run 键 + 启动文件夹枚举），由 `DailyCare.ReconcileTick` 在掌权期调用 `RunIfDue()`。UI 侧：WinForms 设置页经 PanelForm 注入 DevFocus（专注开关即时生效）；WPF 宿主无运行时核心，设置全部读写注册表键——**这要求 P2 的 `focusOn` 从构造缓存改为实时读注册表**（见"P2 修正"节，本计划 Task 0）。

**Tech Stack:** 同 P1-P3。WPF 侧 XAML 改动用 `build-wpf.cmd` 验证。

**Spec:** `docs/superpowers/specs/2026-08-14-scenario-expansion-design.md`；**前置：** P1-P3 计划已全部落地。

---

## P2 修正（跨进程设置同步缺陷）

**问题**：P2 的 `focusOn` 是构造时从注册表读入的缓存字段，只有 `SetFocusMode` 能改。WPF 宿主（独立进程，不引用 src/Core）修改注册表 `DevFocusModeOn` 后，运行中的 Caelus.exe 永远不感知。现有 `DevModeOn` 无此问题——`enabled()` 委托每次进程事件实时读注册表。

**修正**（Task 0）：`focusOn` 字段删除，`FocusModeOn` 属性实时读注册表；`SetFocusMode` 只负责写注册表 + 清分心集合 + 活性重算。

## 文件结构

| 操作 | 文件 | 职责 |
|---|---|---|
| Modify | `src/Core/Scenario/DevFocus.cs` | Task 0：focusOn 实时读修正 |
| Create | `src/Core/Scenario/StartupAudit.cs` | 启动项枚举 + 基线快照 + 新增对比 |
| Create | `src/Core/Scenario/HealthCare.cs` | 到点判定 + 维护执行编排 |
| Modify | `src/Core/Scenario/DailyCare.cs` | ReconcileTick 挂载 `HealthCare.RunIfDue()` |
| Modify | `src/Ui/PanelForm.cs:83,521` | 构造注入 DevFocus + lblStatus 合成场景后缀 |
| Modify | `src/Ui/Pages/PanelForm.SettingsPage.cs` | 开发区扩展 + 日常区 |
| Modify | `wpf/SettingsViewModel.cs` / `wpf/Views/SettingsView.xaml` | WPF 双侧同步 |
| Modify | `src/Program.cs` | PanelForm 5 参实参 + GrantedChanged 接线 |
| Modify | `src/Platform/Lang.cs` | P4 文案键 |
| Create | `tests/SelfTests.HealthCare.cs` | 健康维护测试 |
| Modify | `tests/SelfTests.cs` | 注册 |

## 已核实的代码事实（本计划依据）

- `Paths.Data` 静态字符串（`Paths.cs:20`）——数据目录，HealthCare 直接用
- `Logger` 已内建 512KB 轮转（`Logger.cs:21-30`）——spec 的"日志轮转"项**已存在，本计划不重复实现**
- `ShaderCache.Clean()` 返回 `CacheSweep.Result`（`ShaderCache.cs:43`）
- 启动项枚举无现成工具（全项目仅 `GameExeTweaks.cs` 一处 `GetValueNames`）——StartupAudit 新实现
- WinForms 设置页模式：`Section()` + `MakeSwitch(初值, 回调)` + `MakeAutoCard(...)` + `CardLabel` + `PillButton`（`PanelForm.SettingsPage.cs:30-80`，开发模式卡片 swDev 在 L42-46）
- WinForms 概览页状态合成点：`PanelForm.cs:521` `if (lblStatus != null) lblStatus.Text = gameMode.StatusText;`
- `PanelForm(Tamer, GameMode, Icon, bool)` 4 参构造（`PanelForm.cs:83`）；Program.cs L282 `new PanelForm(tamer, gameMode, appIcon, elevated)`
- WPF `SettingsViewModel(GameMode, Tamer)`（`SettingsViewModel.cs:25`）；XAML 用 `PolicyRow`/`PolicyToggle` 样式 + `Binding`（`SettingsView.xaml:80` 开发区）
- 托盘气球转发模式（Program.cs `devFocus.SessionChanged += key => ...`）

---

### Task 0: P2 修正——focusOn 实时读注册表

**Files:**
- Modify: `src/Core/Scenario/DevFocus.cs`
- Modify: `tests/SelfTests.DevFocus.cs`（两个测试加注册表恢复）

- [ ] **Step 1: DevFocus.cs 修正**

**改动 1** — 删除字段 `private bool focusOn;`，属性替换为实时读：

```csharp
        /// <summary>专注模式开关状态。实时读注册表——WPF 宿主（独立进程）修改后本进程下次评估即生效</summary>
        public bool FocusModeOn { get { return Settings.Load("DevFocusModeOn", false); } }
```

**改动 2** — `SetFocusMode` 替换为：

```csharp
        /// <summary>专注模式开关（托盘菜单/设置页调用）。写注册表 + 活性重算。</summary>
        public void SetFocusMode(bool on)
        {
            Settings.Save("DevFocusModeOn", on);
            if (!on) { lock (sync) distractNotified.Clear(); }
            RecomputeActivity();
        }
```

**改动 3** — 构造中删除 `this.focusOn = Settings.Load("DevFocusModeOn", false);`。

**改动 4** — 全文 `focusOn` 引用替换为 `FocusModeOn`（`WantsActiveLocked`、`Grant` 内的快照、`NotifyProcessChanges` 分心判定）：

```csharp
        protected override bool WantsActiveLocked
        {
            get { return enabled() && (activeBuildPids.Count > 0 || FocusModeOn || activeIdePids.Count > 0); }
        }
```

`Grant()` 内：

```csharp
                lock (sync)
                {
                    build = activeBuildPids.Count > 0;
                    focus = FocusModeOn;
                    ide = activeIdePids.Count > 0;
                }
```

`NotifyProcessChanges` 分心判定内：`granted && focusOn` → `granted && FocusModeOn`。

`ReconcileTick` 内 `lock (sync) focus = focusOn;` → `focus = FocusModeOn;`（注册表读不需要锁，但保留锁内读取无害——改为锁外读更干净：`bool focus = FocusModeOn;`）。

- [ ] **Step 2: 测试加注册表恢复**

以下**三个**测试都通过 `SetFocusMode` 写真实注册表，finally 块在 `dev.Stop()` 后追加恢复（防污染）：

- `TestDevFocusActivitySources`
- `TestDevFocusFocusGrantEffects`
- `TestDevFocusDistractOnce`（P2 Task 3，易漏——它同样 SetFocusMode(true)）

```csharp
                try { Settings.Save("DevFocusModeOn", false); } catch { }
```

- [ ] **Step 3: 全量自测回归**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 203  PASS 201  FAIL 0  SKIP 2`（与 P3 终态一致）。

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "fix: 专注开关实时读注册表——WPF 宿主跨进程修改即时生效（P2 修正）"
```

---

### Task 1: StartupAudit（启动项枚举 + 基线对比）

**Files:**
- Create: `src/Core/Scenario/StartupAudit.cs`
- Test: `tests/SelfTests.HealthCare.cs`
- Modify: `tests/SelfTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `tests/SelfTests.HealthCare.cs`：

```csharp
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
                new StartupAudit.Entry("HKCU\\Run", "OneDrive", "C:\\new\\onedrive.exe"),  // 命令变了不算新增
                new StartupAudit.Entry("HKLM\\Run", "Audio", "C:\\rtk\\audiodg.exe"),
                new StartupAudit.Entry("HKCU\\Run", "NewSpy", "C:\\spy\\new.exe"),          // 新增
                new StartupAudit.Entry("StartupFolder", "tool.lnk", "")                     // 新增
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
                    new StartupAudit.Entry("HKCU\\Run", "App|特殊", "C:\\a|b.exe /arg")
                };
                StartupAudit.SaveBaseline(file, entries);
                var loaded = StartupAudit.LoadBaseline(file);
                Eq(1, loaded.Count);
                Eq("HKCU\\Run", loaded[0].Source);
                Eq("App|特殊", loaded[0].Name);          // 竖线转义往返
                Eq("C:\\a|b.exe /arg", loaded[0].Command);

                Eq(0, StartupAudit.LoadBaseline(Path.Combine(dir, "missing.txt")).Count);
            }
            finally { DeleteTempDir(dir); }
        }

        private static void TestHealthCareIsDue()
        {
            Eq(true, HealthCare.IsDue("", 1, new DateTime(2026, 8, 14)));               // 从未运行
            Eq(true, HealthCare.IsDue("2026-08-13", 1, new DateTime(2026, 8, 14)));     // 超过间隔
            Eq(false, HealthCare.IsDue("2026-08-14", 1, new DateTime(2026, 8, 14)));    // 今日已跑
            Eq(false, HealthCare.IsDue("2026-08-13", 7, new DateTime(2026, 8, 14)));    // 间隔未到
            Eq(true, HealthCare.IsDue("2026-08-07", 7, new DateTime(2026, 8, 14)));     // 整 7 天到期
            Eq(true, HealthCare.IsDue("垃圾数据", 1, new DateTime(2026, 8, 14)));        // 损坏视为到期
        }
    }
}
```

在 `tests/SelfTests.cs` 注册（P4 注册块）：

```csharp
            test("健康维护：启动项基线对比只报新增", TestStartupAuditDiffNew);
            test("健康维护：基线快照存储往返与转义", TestStartupAuditBaselineRoundtrip);
            test("健康维护：到点判定覆盖从未运行与损坏数据", TestHealthCareIsDue);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误（`StartupAudit`/`HealthCare` 不存在）。

- [ ] **Step 3: 实现 StartupAudit.cs**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 启动项审查：枚举 Run 键与启动文件夹，与基线快照对比，只报告新增项（不自动删除）

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class StartupAudit
    {
        internal sealed class Entry
        {
            public string Source;
            public string Name;
            public string Command;

            public Entry(string source, string name, string command)
            {
                Source = source;
                Name = name;
                Command = command;
            }
        }

        public static string BaselinePath { get { return Path.Combine(Paths.Data, "Caelus.startup.baseline"); } }

        /// <summary>只读枚举当前启动项：HKCU/HKLM Run 键 + 当前用户启动文件夹</summary>
        public static List<Entry> ScanCurrent()
        {
            var list = new List<Entry>();
            ScanRunKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run", list);
            ScanRunKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run", list);
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(folder))
                    foreach (string f in Directory.GetFiles(folder))
                        list.Add(new Entry("StartupFolder", Path.GetFileName(f), ""));
            }
            catch { }
            return list;
        }

        private static void ScanRunKey(RegistryKey hive, string subKey, string source, List<Entry> list)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(subKey))
                {
                    if (k == null) return;
                    foreach (string name in k.GetValueNames())
                    {
                        string cmd = "";
                        try { cmd = Convert.ToString(k.GetValue(name, "")); } catch { }
                        list.Add(new Entry(source, name, cmd));
                    }
                }
            }
            catch { }
        }

        /// <summary>新增项 = 当前有而基线没有（Source+Name 为键；命令变化不算新增）。纯逻辑可单测。</summary>
        internal static List<Entry> DiffNew(List<Entry> current, List<Entry> baseline)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in baseline) known.Add(KeyOf(e));
            var added = new List<Entry>();
            foreach (Entry e in current)
                if (known.Add(KeyOf(e))) added.Add(e);
            return added;
        }

        private static string KeyOf(Entry e) { return e.Source + "|" + e.Name; }

        public static List<Entry> LoadBaseline(string path)
        {
            var list = new List<Entry>();
            try
            {
                if (!File.Exists(path)) return list;
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    list.Add(new Entry(Unesc(parts[0]), Unesc(parts[1]), Unesc(parts[2])));
                }
            }
            catch { }
            return list;
        }

        public static void SaveBaseline(string path, List<Entry> entries)
        {
            try
            {
                var lines = new List<string>();
                foreach (Entry e in entries)
                    lines.Add(Esc(e.Source) + "\t" + Esc(e.Name) + "\t" + Esc(e.Command));
                AtomicFile.WriteAllLines(path, lines.ToArray());
            }
            catch { }
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\t", "\t").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\\", "\\");
        }
    }
}
```

**核实 `AtomicFile.WriteAllLines` 是否存在**：运行 `grep -n "public static" src/Platform/AtomicFile.cs`——若只有 `WriteAllText`，改用 `AtomicFile.WriteAllText(path, string.Join(Environment.NewLine, lines.ToArray()))`（先读文件确认签名再落地）。

**注意**：测试中基线文件行含 `|` 字符——上述实现用 **TAB 分隔 + 反斜杠转义**，竖线无需转义。测试 `TestStartupAuditBaselineRoundtrip` 的断言（"App|特殊" 往返）对此成立。若落地时改用其他分隔符，同步调整测试。

- [ ] **Step 4: 运行自测确认通过（部分）**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误仅剩 `HealthCare.IsDue` 不存在；`StartupAudit` 两项测试应已通过（报告里 PASS）。

- [ ] **Step 5: 实现 HealthCare.cs**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 系统健康维护：到点判定与执行编排（仅 DailyCare 掌权期调用）

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class HealthCare
    {
        /// <summary>到点判定（纯逻辑可单测）：从未运行/损坏数据/超过间隔 → true</summary>
        internal static bool IsDue(string lastRunStamp, int intervalDays, DateTime today)
        {
            if (intervalDays < 1) intervalDays = 1;
            DateTime last;
            if (!DateTime.TryParse(lastRunStamp, out last)) return true;
            return (today.Date - last.Date).TotalDays >= intervalDays;
        }

        /// <summary>读取配置的维护间隔（天），默认 1</summary>
        public static int IntervalDays()
        {
            int days;
            if (!int.TryParse(Settings.LoadStr("HealthIntervalDays", "1"), out days) || days < 1) return 1;
            return days > 30 ? 30 : days;
        }

        /// <summary>到点则执行：着色器缓存清理 + 启动项审查。只在 DailyCare 掌权期被调用。</summary>
        public static void RunIfDue()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!IsDue(Settings.LoadStr("HealthLastRun", ""), IntervalDays(), DateTime.Now)) return;

            try
            {
                long beforeBytes = ShaderCache.MeasureBytes();
                if (beforeBytes > 64L * 1024 * 1024)   // 小于 64MB 不值得清
                {
                    CacheSweep.Result r = ShaderCache.Clean();
                    Logger.Log("健康维护：着色器缓存清理 " + CacheSweep.FmtBytes(beforeBytes)
                        + "（删除 " + r.Deleted + " 项）");
                }
            }
            catch (Exception ex) { Logger.LogFailure("健康维护：着色器清理失败", ex); }

            try
            {
                var current = StartupAudit.ScanCurrent();
                var baseline = StartupAudit.LoadBaseline(StartupAudit.BaselinePath);
                var added = StartupAudit.DiffNew(current, baseline);
                if (baseline.Count > 0 && added.Count > 0)   // 首次建基线不报告
                {
                    var names = new List<string>();
                    foreach (var e in added) names.Add(e.Name + "（" + e.Source + "）");
                    string news = string.Join("、", names.ToArray());
                    if (news.Length > 300) news = news.Substring(0, 300) + "…";
                    Settings.SaveStr("HealthStartupNews", news);
                    Logger.Log("健康维护：发现 " + added.Count + " 个新启动项：" + news);
                }
                StartupAudit.SaveBaseline(StartupAudit.BaselinePath, current);
            }
            catch (Exception ex) { Logger.LogFailure("健康维护：启动项审查失败", ex); }

            Settings.SaveStr("HealthLastRun", today);
        }
    }
}
```

**核实 `CacheSweep.Result` 的字段名**（上述用了 `r.Deleted`）：运行 `grep -n "class Result" -A 8 src/Core/CacheSweep.cs`——按实际字段名调整（如 `Files/Dirs/Freed`）。

在 `tests/SelfTests.cs` 注册已在 Step 1 完成。

- [ ] **Step 6: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 206  PASS 204  FAIL 0  SKIP 2`。

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: 系统健康维护——启动项基线审查 + 着色器缓存到点清理（3 项自测）"
```

---

### Task 2: DailyCare 挂载健康维护 + WinForms 设置页

**Files:**
- Modify: `src/Core/Scenario/DailyCare.cs`
- Modify: `src/Ui/PanelForm.cs`（构造注入 DevFocus）
- Modify: `src/Ui/Pages/PanelForm.SettingsPage.cs`
- Modify: `src/Program.cs`
- Modify: `src/Platform/Lang.cs`

- [ ] **Step 1: DailyCare 挂载**

找到 `DailyCare.cs` 的 `ReconcileTick`（P3 终态）：

```csharp
        private void ReconcileTick()
        {
            lock (sync) { if (!grantedFlag) return; }
            try
            {
                RefreshFamilyVisible(true);
                RecomputeActivity();   // 窗口全关可能让场景失活
                lock (sync) { if (!grantedFlag) return; }   // 失活后仲裁器已回调 Suspend
                SweepDailySuppression();
                BoostVisibleFamily();
            }
            catch { }
        }
```

在 `BoostVisibleFamily();` 后插入一行：

```csharp
                HealthCare.RunIfDue();   // 到点判定内部做，未到期零开销
```

- [ ] **Step 2: PanelForm 注入 DevFocus**

找到 `src/Ui/PanelForm.cs` L83 构造签名：

```csharp
        public PanelForm(Tamer t, GameMode gm, Icon icon, bool isElevated)
```

替换为：

```csharp
        public PanelForm(Tamer t, GameMode gm, DevFocus devFocus, Icon icon, bool isElevated)
```

构造体内找到 `this.tamer = t;`（或等价赋值行），在其后加：

```csharp
            this.devFocus = devFocus;
```

字段区（构造上方）加：

```csharp
        private readonly DevFocus devFocus;
```

**Program.cs 同步**：找到 L282 附近：

```csharp
            var panel = new PanelForm(tamer, gameMode, appIcon, elevated);
```

替换为：

```csharp
            var panel = new PanelForm(tamer, gameMode, devFocus, appIcon, elevated);
```

（若代码库还有其他 `new PanelForm(` 调用点——如测试或预览探针——运行 `grep -rn "new PanelForm(" src/ tests/ | grep -v "PanelForm.cs"` 全部同步。）

- [ ] **Step 3: WinForms 设置页——开发区扩展**

找到 `src/Ui/Pages/PanelForm.SettingsPage.cs` 的自定义编译进程块结束处（`sy += 114;` 之后、`Section(scroll, Lang.T("sec.maint"), ...)` 之前），插入：

```csharp
            swFocus = MakeSwitch(devFocus.FocusModeOn, delegate
            {
                devFocus.SetFocusMode(swFocus.Checked);
            });
            MakeAutoCard(scroll, 6, sy, ScrollContentW, 76, Lang.T("set.focus"), Lang.T("set.focus.n"), swFocus, out cardH);
            sy += cardH + 8;

            // 分心应用清单输入（仿自定义编译进程块）
            CardLabel(scroll, Lang.T("set.distract"), 14, sy + 6, ScrollContentW - 28, 18, 8f, true, Theme.Fg);
            var tbDistract = Theme.MakeTextBox(Theme.S(14), Theme.S(sy + 26), Theme.S(ScrollContentW - 110));
            tbDistract.Text = Settings.LoadStr("DevFocusDistractList", "");
            tbDistract.Height = Theme.S(44);
            tbDistract.Multiline = true;
            tbDistract.ScrollBars = ScrollBars.Vertical;
            tbDistract.ForeColor = Theme.Fg;
            tbDistract.BackColor = Theme.Inset;
            scroll.Controls.Add(tbDistract);
            var btnDistractSave = new PillButton(Lang.T("set.dev.custom.save"), BtnKind.Normal);
            btnDistractSave.Bg = Theme.Card;
            btnDistractSave.Size = new Size(Theme.S(80), Theme.S(30));
            btnDistractSave.Location = new Point(Theme.S(ScrollContentW - 96), Theme.S(sy + 30));
            btnDistractSave.Click += delegate
            {
                Settings.SaveStr("DevFocusDistractList", tbDistract.Text);
                DistractCatalog.Reload();
                btnDistractSave.Text = Lang.T("set.dev.custom.saved");
                var revert2 = new System.Windows.Forms.Timer();
                revert2.Interval = 1500;
                revert2.Tick += (s3, e3) => { revert2.Stop(); revert2.Dispose(); btnDistractSave.Text = Lang.T("set.dev.custom.save"); };
                revert2.Start();
            };
            scroll.Controls.Add(btnDistractSave);
            CardLabel(scroll, Lang.T("set.distract.n"), 14, sy + 76, ScrollContentW - 28, 32, 7.4f, false, Theme.Dim);
            sy += 114;
```

字段区加 `private Toggle swFocus;`——先 `grep -n "private Toggle swDev" src/Ui/Pages/PanelForm.SettingsPage.cs` 确认 swDev 的声明类型与位置，照抄声明。

- [ ] **Step 4: WinForms 设置页——日常区**

在 Step 3 插入块之后继续插入（仍在 `sec.maint` 之前）：

```csharp
            sy += 10;
            Section(scroll, Lang.T("sec.daily"), 6, sy); sy += 24;

            swDaily = MakeSwitch(Settings.Load("DailyCareOn", true), delegate
            {
                Settings.Save("DailyCareOn", swDaily.Checked);
            });
            MakeAutoCard(scroll, 6, sy, ScrollContentW, 76, Lang.T("set.daily"), Lang.T("set.daily.n"), swDaily, out cardH);
            sy += cardH + 8;

            // 启动项新报告（只读展示 + 立即审查）
            string news = Settings.LoadStr("HealthStartupNews", "");
            CardLabel(scroll, Lang.T("set.startup.news"), 14, sy + 4, ScrollContentW - 28, 18, 8f, true, Theme.Fg);
            CardLabel(scroll, news.Length == 0 ? Lang.T("set.startup.none") : news,
                14, sy + 24, ScrollContentW - 28, 44, 8f, false, Theme.Dim);
            var btnAudit = new PillButton(Lang.T("set.startup.scan"), BtnKind.Normal);
            btnAudit.Bg = Theme.Card;
            btnAudit.Size = new Size(Theme.S(120), Theme.S(30));
            btnAudit.Location = new Point(Theme.S(14), Theme.S(sy + 70));
            btnAudit.Click += delegate
            {
                var added = StartupAudit.ScanCurrent();
                var baseline = StartupAudit.LoadBaseline(StartupAudit.BaselinePath);
                var diff = StartupAudit.DiffNew(added, baseline);
                Logger.Log("健康维护：手动审查启动项，当前 " + added.Count + " 项，新增 " + diff.Count + " 项");
                btnAudit.Text = Lang.F("set.startup.scanned", diff.Count);
            };
            scroll.Controls.Add(btnAudit);
            sy += 110;
```

字段区加 `private Toggle swDaily;`。

**确认 `Lang.F` 存在**（带格式参数）：`grep -n "public static string F(" src/Platform/Lang.cs`——现有用法 `Lang.F("mode.tray.current", ...)`（TrayMenu.cs）已证实存在。

- [ ] **Step 5: Lang.cs 加键**

在 `bal.daily.batt` 行后插入：

```csharp
            { "set.focus", new[]{ "专注模式" } },
            { "set.focus.n", new[]{ "开启后静默通知、持续压制后台；可配分心应用清单，清单程序启动时提醒一次" } },
            { "set.distract", new[]{ "分心应用清单（分号分隔）" } },
            { "set.distract.n", new[]{ "专注模式期间这些程序启动时托盘提醒一次（如 discord、steam），只提醒不处理" } },
            { "sec.daily", new[]{ "日常优化" } },
            { "set.daily", new[]{ "日常场景调度" } },
            { "set.daily.n", new[]{ "浏览器/Office/会议软件活跃时压制后台并提优前台家族；电池供电自动加强压制" } },
            { "set.startup.news", new[]{ "新出现的启动项" } },
            { "set.startup.none", new[]{ "未发现新启动项（每日健康维护自动审查）" } },
            { "set.startup.scan", new[]{ "立即审查" } },
            { "set.startup.scanned", new[]{ "审查完成：{0} 项新增" } },
```

- [ ] **Step 6: 全量自测 + 构建**

```bash
cmd.exe //c "dev.cmd test"
cmd.exe //c "build.cmd"
```

预期：`TOTAL 206  PASS 204  FAIL 0  SKIP 2`；构建 OK。

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: DailyCare 挂载健康维护 + WinForms 设置页开发区/日常区"
```

---

### Task 3: WPF 设置页同步

**Files:**
- Modify: `wpf/SettingsViewModel.cs`
- Modify: `wpf/Views/SettingsView.xaml`

- [ ] **Step 1: SettingsViewModel 加属性**

找到 `wpf/SettingsViewModel.cs` 的 `devMode` 属性区域（L31 附近字段、L109-131 属性），在其后追加：

```csharp
        private bool focusMode;
        private bool dailyCare;

        public bool FocusMode
        {
            get { return focusMode; }
            set
            {
                if (focusMode == value) return;
                focusMode = value;
                Settings.Save("DevFocusModeOn", value);   // Caelus.exe 侧实时读注册表，即时生效
                OnPropertyChanged("FocusMode");
            }
        }

        public bool DailyCare
        {
            get { return dailyCare; }
            set
            {
                if (dailyCare == value) return;
                dailyCare = value;
                Settings.Save("DailyCareOn", value);
                OnPropertyChanged("DailyCare");
            }
        }

        public string FocusModeTitle { get { return Lang.T("set.focus"); } }
        public string FocusModeNote { get { return Lang.T("set.focus.n"); } }
        public string DailyCareTitle { get { return Lang.T("set.daily"); } }
        public string DailyCareNote { get { return Lang.T("set.daily.n"); } }
        public string StartupNews
        {
            get
            {
                string news = Settings.LoadStr("HealthStartupNews", "");
                return news.Length == 0 ? Lang.T("set.startup.none") : news;
            }
        }
```

构造中（`devMode = Settings.Load("DevModeOn", true);` 行后）追加：

```csharp
            focusMode = Settings.Load("DevFocusModeOn", false);
            dailyCare = Settings.Load("DailyCareOn", true);
```

**确认 ViewModel 基类的属性通知方法名**：`grep -n "OnPropertyChanged\|RaiseProperty" src/UiShared/ViewModelBase.cs | head -3`——按实际方法名调整（WPF 的 SettingsViewModel 继承关系见其类声明；若它直接用 `SetField` 模式则照抄现有 DevMode 属性的写法）。

- [ ] **Step 2: SettingsView.xaml 加行**

找到开发区 `Border` 块中 `BtnDevSave` 所在 `PolicyRow` 的结束标签之后（`ZoneMaint` 的 `StackPanel` 开始之前），插入专注开关行：

```xml
            <Border Style="{DynamicResource PolicyRow}" local:RowToggle.Enabled="True"><DockPanel><ToggleButton Style="{DynamicResource PolicyToggle}" IsChecked="{Binding FocusMode}" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="专注模式"/><StackPanel VerticalAlignment="Center"><TextBlock Text="{Binding FocusModeTitle}" FontSize="{DynamicResource FontSizeCaption}" FontWeight="SemiBold" Foreground="{DynamicResource TextPrimaryBrush}"/><TextBlock Text="{Binding FocusModeNote}" FontSize="{DynamicResource FontSizeSmall}" Foreground="{DynamicResource TextSecondaryBrush}" TextWrapping="Wrap" Margin="0,3,0,0"/></StackPanel></DockPanel></Border>
```

在 `ZoneMaint` 之前插入日常区：

```xml
      <StackPanel x:Name="ZoneDaily">
        <TextBlock Text="{Binding DailyCareTitle}" FontSize="{DynamicResource FontSizeBody}" FontWeight="SemiBold" Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,0,8"/>
        <Border Style="{DynamicResource SettingsGroup}" Margin="0,0,0,16">
          <StackPanel>
            <Border Style="{DynamicResource PolicyRow}" BorderThickness="0,0,0,1" local:RowToggle.Enabled="True"><DockPanel><ToggleButton Style="{DynamicResource PolicyToggle}" IsChecked="{Binding DailyCare}" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="日常场景调度"/><StackPanel VerticalAlignment="Center"><TextBlock Text="{Binding DailyCareTitle}" FontSize="{DynamicResource FontSizeCaption}" FontWeight="SemiBold" Foreground="{DynamicResource TextPrimaryBrush}"/><TextBlock Text="{Binding DailyCareNote}" FontSize="{DynamicResource FontSizeSmall}" Foreground="{DynamicResource TextSecondaryBrush}" TextWrapping="Wrap" Margin="0,3,0,0"/></StackPanel></DockPanel></Border>
            <Border Style="{DynamicResource PolicyRow}"><StackPanel><TextBlock Text="{Binding StartupNewsTitle}" FontSize="{DynamicResource FontSizeCaption}" FontWeight="SemiBold" Foreground="{DynamicResource TextPrimaryBrush}"/><TextBlock Text="{Binding StartupNews}" FontSize="{DynamicResource FontSizeSmall}" Foreground="{DynamicResource TextSecondaryBrush}" TextWrapping="Wrap" Margin="0,3,0,0"/></StackPanel></Border>
          </StackPanel>
        </Border>
      </StackPanel>
```

ViewModel 补 `public string StartupNewsTitle { get { return Lang.T("set.startup.news"); } }`。

**注意 XAML 注释陷阱（memory）**：WPF XAML 不支持 `StrokeLineCap`，用 `StrokeStartLineCap`/`StrokeEndLineCap`——本任务不涉及 Path 图标，无需处理。

- [ ] **Step 3: WPF 构建验证**

```bash
cmd.exe //c "build-wpf.cmd"
```

预期：`WPF Build OK`。

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: WPF 设置页同步——专注模式/日常调度开关 + 启动项新报告展示"
```

---

### Task 4: WinForms 概览页掌权场景指示

**Files:**
- Modify: `src/Ui/PanelForm.cs`
- Modify: `src/Program.cs`
- Modify: `src/Platform/Lang.cs`
- Test: `tests/SelfTests.Arbiter.cs`（追加场景名映射纯逻辑测试）
- Modify: `tests/SelfTests.cs`

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.Arbiter.cs` 类内追加：

```csharp
        private static void TestScenarioStatusSuffix()
        {
            Eq("", PanelForm.ScenarioStatusSuffix(null));
            Eq("游戏", PanelForm.ScenarioStatusSuffix(ScenarioKind.Game));
            Eq("开发", PanelForm.ScenarioStatusSuffix(ScenarioKind.DevFocus));
            Eq("日常", PanelForm.ScenarioStatusSuffix(ScenarioKind.DailyCare));
        }
```

注册：

```csharp
            test("场景仲裁：掌权场景状态后缀映射", TestScenarioStatusSuffix);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误（`PanelForm.ScenarioStatusSuffix` 不存在）。

- [ ] **Step 3: 实现**

**改动 1 — PanelForm.cs 加静态映射与实例方法**（放在 `SyncAllToggles` 附近）：

```csharp
        /// <summary>掌权场景的状态后缀（纯逻辑可单测）</summary>
        internal static string ScenarioStatusSuffix(ScenarioKind? kind)
        {
            if (!kind.HasValue) return "";
            switch (kind.Value)
            {
                case ScenarioKind.Game: return "游戏";
                case ScenarioKind.DevFocus: return "开发";
                case ScenarioKind.DailyCare: return "日常";
                default: return "";
            }
        }

        private ScenarioKind? grantedScenario;

        /// <summary>仲裁器 GrantedChanged 接线（UI 线程调用）：更新概览页守护状态</summary>
        public void SetGrantedScenario(ScenarioKind? kind)
        {
            grantedScenario = kind;
            if (lblStatus != null)
                lblStatus.Text = gameMode.StatusText + ScenarioStatusSuffix(kind);
        }
```

**改动 2 — 状态合成点**。找到 `PanelForm.cs:521`：

```csharp
            if (lblStatus != null) lblStatus.Text = gameMode.StatusText;
```

替换为：

```csharp
            if (lblStatus != null) lblStatus.Text = gameMode.StatusText + ScenarioStatusSuffix(grantedScenario);
```

**改动 3 — Program.cs 接线**。找到 P3 终态的 `dailyCare.SessionChanged += key =>` 订阅块，在其后插入：

```csharp
            arbiter.GrantedChanged += kind =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { panel.SetGrantedScenario(kind); } catch { }
                    }));
                }
                catch { }
            };
```

**改动 4 — 状态文本语义**。后缀直接拼在 `gameMode.StatusText` 后过于简陋，用间隔符——上面的实现直接拼接；若视觉拥挤，改为 `" · " + suffix`：在 `ScenarioStatusSuffix` 返回值前加 `" · "`（此时 null 返回 `""` 保持不变）。落地时按概览页实际观感二选一，保持测试断言同步。

- [ ] **Step 4: 全量回归**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 207  PASS 205  FAIL 0  SKIP 2`。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: 概览页掌权场景指示——GrantedChanged 驱动守护状态后缀"
```

---

### Task 5: 全量回归 + 双构建 + 冒烟

- [ ] **Step 1: 全量自测**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 207  PASS 205  FAIL 0  SKIP 2`。

- [ ] **Step 2: 双构建**

```bash
cmd.exe //c "build.cmd"
cmd.exe //c "build-wpf.cmd"
```

- [ ] **Step 3: 冒烟验证（手动）**

1. 设置页：开发区出现"专注模式"开关与分心清单输入；日常区出现"日常场景调度"开关与"新出现的启动项"
2. 概览页：打开浏览器 → 守护状态出现"日常"后缀；启动游戏 → 变"游戏"
3. 健康维护：删除注册表 `HealthLastRun` 值，保持浏览器打开等 30 秒（掌权 Tick）→ 日志出现"健康维护：…"；%AppData%\Caelus\Caelus.startup.baseline 生成
4. WPF 侧：CaelusWpf.exe 设置页切换"专注模式"→ Caelus.exe 日志 30 秒内出现掌权记录（跨进程注册表生效）

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "test: P4 全量回归——207 项自测 0 失败，场景扩展四期收官"
```

---

## Self-Review 记录

**Spec 覆盖**：健康维护（每日一次频率可配/仅掌权期执行/缓存清理/启动项审查只报告）✓；UI（WinForms 设置页开发区+日常区 ✓、WPF 设置页同步 ✓、概览页掌权指示 ✓、托盘快速开关在 P2 ✓）。日志轮转已内建于 Logger，声明不重复实现 ✓。

**类型一致性**：`StartupAudit.Entry(source, name, command)` 构造、`ScanCurrent/LoadBaseline/SaveBaseline/DiffNew` 签名、`HealthCare.IsDue(string, int, DateTime)`、`PanelForm.ScenarioStatusSuffix(ScenarioKind?)→string`、`SetGrantedScenario(ScenarioKind?)`、PanelForm 5 参构造——跨任务引用一致。

**已知取舍**：
- WPF 概览页不做掌权场景指示（WPF 宿主无运行时核心，显示只能是假数据；待 WPF 宿主接入真实核心后实现）
- 健康维护间隔用注册表字符串存取（Settings 只有 bool/string API），范围钳制 1-30 天
- 启动项审查只覆盖 HKCU/HKLM Run 键 + 当前用户启动文件夹（StartupApproved、计划任务、服务类启动项不在范围）
- 着色器缓存清理阈值 64MB（低于此不值得清）
