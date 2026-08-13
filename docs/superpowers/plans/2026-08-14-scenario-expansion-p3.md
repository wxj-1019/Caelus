# 场景扩展 P3：DailyCare 日常场景调度 + 电池能效 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地第三场景 DailyCare（日常优化）：浏览器/Office/会议家族活跃时常规档压制 + 家族提优；电池供电时自动升档压制并建议省电——同时兑现 P1 推迟的 ScenarioBase 抽取（DevFocus 改造为继承，消除双份仲裁对接样板）。

**Architecture:** DailyCare 继承 ScenarioBase（P3 新建抽象基类：仲裁对接 + 活性重算 + 死 PID 清理），优先级 10（最低）。活性 = 日常家族有可见窗口 OR 电池供电；窗口状态无进程事件可订阅，用"进程事件驱动的节流窗口复查"刷新，不引入检测用定时器。压制用 `SuppressReason.Daily=8` 新位（本计划改枚举）。

**Tech Stack:** 同 P1/P2（C# 5、.NET 4.x、`cmd.exe //c "dev.cmd test"`）。

**Spec:** `docs/superpowers/specs/2026-08-14-scenario-expansion-design.md`；**前置：** P1、P2 计划已全部落地。

---

## 文件结构

| 操作 | 文件 | 职责 |
|---|---|---|
| Modify | `src/Core/Suppression/SuppressionCore.cs:9-16` | `SuppressReason` 加 `Daily = 8` |
| Create | `src/Core/Scenario/ScenarioBase.cs` | 抽象基类：仲裁对接 + 活性重算 + PruneDeadPids |
| Modify | `src/Core/Scenario/DevFocus.cs` | 改为继承 ScenarioBase（删自有仲裁对接样板） |
| Create | `src/Core/Scenario/DailyCatalog.cs` | 日常家族双校验（浏览器/Office/会议） |
| Create | `src/Core/Scenario/DailyCare.cs` | 日常场景：家族检测 + 压制 + 提优 + 电池 |
| Modify | `src/Program.cs` | DailyCare 实例化 + 事件接线 + 退出路径 |
| Modify | `src/Platform/Lang.cs` | 新增文案键（bal.daily.batt 等） |
| Create | `tests/SelfTests.DailyCare.cs` | DailyCare 测试 |
| Modify | `tests/SelfTests.cs` | 注册 |

## 已核实的代码事实（本计划依据）

- `SuppressReason` 现值 `None=0, AntiCheat=1, Background=2, Build=4`——`Daily=8` 按位无冲突（`SuppressionCore.cs:9-16`）
- `SystemEvents.PowerLineStatusChanged`（Microsoft.Win32）事件可用——项目已有 `SystemEvents.SessionEnded` 用法（`Program.cs:338`）
- `SystemInformation.PowerStatus.PowerLineStatus`（System.Windows.Forms 命名空间）：`Offline`=电池供电
- `DevFocus.ShouldSuppressBackground(...)` 是 `internal static`（P1 Task 4）——DailyCare 直接复用，同程序集可见
- `GameSessionDetector.VisibleWindowPids(true)` / `ForegroundPid()` internal static 可复用
- IDE 提优还原模式（P2 Task 4：内存快照 + StartTime 校验 + 回读生效才入快照）——DailyCare 家族提优照抄
- `Logger` 已自带 512KB 轮转（`Logger.cs:21-30`）——健康维护**不需要**再做日志轮转（P4 减项）
- `PowerOverlay.Activate()` 是"最佳性能"方向（`PowerOverlay.cs:59`）——电池场景**不调用**，按 spec 只做气球建议

## 关键设计决策

1. **ScenarioBase 只抽三样**：仲裁器注册/注销、`RecomputeActivity`（活性翻转 → ReportActivity）、`PruneDeadPids`。`Grant/Suspend` 各场景内容不同，留在子类——不抽空模板方法之上的共用逻辑（没有）
2. **活性判定属性 `WantsActiveLocked`**：base 不知道各场景的活性条件与开关，定义抽象只读属性由子类实现（含 `enabled()` 检查）
3. **DailyCare 无检测定时器**：窗口复查挂在进程事件上（节流 5 秒一次）；真实 Windows 上进程事件近乎持续发生，系统静默时延迟解除无害。掌权期 Timer（30s）只做增量压制/提优复查
4. **家族提优范围**：只提优"有可见窗口的家族进程"（浏览器渲染进程等无窗口同名进程不提优）
5. **电池升档**：掌权期间电池供电 → 压制级别 Eco→Restrained 并在气球建议一次；恢复市电 → 新压制回 Eco（已压进程不追溯调档，下一轮增量压制自然生效——避免全量重扫抖动）

---

### Task 1: SuppressReason.Daily 位 + ScenarioBase 抽取（DevFocus 改造继承）

**Files:**
- Modify: `src/Core/Suppression/SuppressionCore.cs`
- Create: `src/Core/Scenario/ScenarioBase.cs`
- Modify: `src/Core/Scenario/DevFocus.cs`
- Test: 无新增（P1/P2 既有测试即回归网）

- [ ] **Step 1: 枚举加位**

找到 `src/Core/Suppression/SuppressionCore.cs` L9-16：

```csharp
    [Flags]
    internal enum SuppressReason
    {
        None = 0,
        AntiCheat = 1,
        Background = 2,
        Build = 4
    }
```

替换为：

```csharp
    [Flags]
    internal enum SuppressReason
    {
        None = 0,
        AntiCheat = 1,
        Background = 2,
        Build = 4,
        Daily = 8
    }
```

- [ ] **Step 2: 新建 ScenarioBase.cs**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 场景基类：仲裁器对接、活性重算、死 PID 清理（DevFocus/DailyCare 共用）

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal abstract class ScenarioBase : IScenario
    {
        protected readonly object sync = new object();
        protected readonly ScenarioArbiter arbiter;
        private bool reported;

        protected ScenarioBase(ScenarioArbiter arbiter)
        {
            if (arbiter == null) throw new ArgumentNullException("arbiter");
            this.arbiter = arbiter;
            arbiter.Register(this);
        }

        public abstract ScenarioKind Kind { get; }
        public abstract int Priority { get; }
        public abstract void Grant();
        public abstract void Suspend();

        /// <summary>锁内判定：场景此刻是否想活跃（含自身开关检查）。子类实现。</summary>
        protected abstract bool WantsActiveLocked { get; }

        /// <summary>已向仲裁器报告的活性状态</summary>
        protected bool Reported { get { lock (sync) return reported; } }

        /// <summary>统一活性重算：任一活性来源变化后调用。锁内记账，锁外向仲裁器报告翻转。</summary>
        protected void RecomputeActivity()
        {
            bool becameActive = false;
            bool becameIdle = false;
            lock (sync)
            {
                bool nowActive = WantsActiveLocked;
                if (nowActive && !reported)
                {
                    reported = true;
                    becameActive = true;
                }
                else if (!nowActive && reported)
                {
                    reported = false;
                    becameIdle = true;
                }
            }
            if (becameActive) arbiter.ReportActivity(Kind, true);
            if (becameIdle) arbiter.ReportActivity(Kind, false);
        }

        /// <summary>死 PID 兜底清理：短命进程的 Stopped 事件可能丢失，每次事件到达时清理</summary>
        protected static void PruneDeadPids(HashSet<int> pids)
        {
            if (pids.Count == 0) return;
            var dead = new List<int>();
            foreach (int pid in pids)
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) { dead.Add(pid); continue; }
                try
                {
                    if (!Native.StillActive(h)) dead.Add(pid);
                }
                finally { Native.CloseHandle(h); }
            }
            foreach (int pid in dead) pids.Remove(pid);
        }
    }
}
```

- [ ] **Step 3: DevFocus 改造为继承**

**改动 1 — 类声明**。找到：

```csharp
    internal sealed class DevFocus : IScenario
    {
        private readonly object sync = new object();
        private readonly ScenarioArbiter arbiter;
        private readonly SuppressionCore core;
```

替换为：

```csharp
    internal sealed class DevFocus : ScenarioBase
    {
        private readonly SuppressionCore core;
```

**改动 2 — 构造**。找到 P2 终态构造（5 参），替换头部：

```csharp
        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core,
            Func<bool> enabled, Func<string, string, bool> isWhitelisted,
            Func<string, bool> isDistract)
            : base(arbiter)
        {
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            this.isWhitelisted = isWhitelisted;
            this.isDistract = isDistract;
            this.focusOn = Settings.Load("DevFocusModeOn", false);
        }
```

（删除 `this.arbiter = arbiter;` 与 `arbiter.Register(this);`——基类已做；保留 `ArgumentNullException` 由基类抛出。）

**改动 3 — 属性改 override**。找到：

```csharp
        public ScenarioKind Kind { get { return ScenarioKind.DevFocus; } }
        public int Priority { get { return 50; } }
```

替换为：

```csharp
        public override ScenarioKind Kind { get { return ScenarioKind.DevFocus; } }
        public override int Priority { get { return 50; } }

        protected override bool WantsActiveLocked
        {
            get { return enabled() && (activeBuildPids.Count > 0 || focusOn || activeIdePids.Count > 0); }
        }
```

`Grant()`/`Suspend()` 签名加 `override`（`public void Grant()` → `public override void Grant()`，Suspend 同）。

**改动 4 — 删除 DevFocus 自有样板**：删除整个 `RecomputeActivity()` 方法、整个 `PruneDeadPids` 方法、`private bool reported;` 字段、P2 的 `AnyActiveLocked` 属性（由 `WantsActiveLocked` 取代）。

**改动 5 — 调用点适配**：
- DevFocus 内所有 `RecomputeActivity(nowActive)` / `RecomputeActivity()` 调用保持方法名（基类无参版）——P2 的 `RecomputeActivity()` 无参签名与基类一致，调用点不动
- `reported` 字段读写处：`NotifyProcessChanges` 开关关闭分支里的 `wasReported = reported; reported = false;`——基类 reported 是 private！该分支需要直接操作。改造为基类提供的保护方法。**在 ScenarioBase 加**：

```csharp
        /// <summary>强制撤销活性报告（开关关闭/退出路径用）：若已报告则向仲裁器报告不活跃</summary>
        protected void ForceReportInactive()
        {
            bool wasReported;
            lock (sync)
            {
                wasReported = reported;
                reported = false;
            }
            if (wasReported) arbiter.ReportActivity(Kind, false);
        }
```

DevFocus `NotifyProcessChanges` 开关关闭分支替换为：

```csharp
            if (!enabled())
            {
                lock (sync)
                {
                    activeBuildPids.Clear();
                    activeIdePids.Clear();
                    distractNotified.Clear();
                }
                ForceReportInactive();
                return;
            }
```

`Stop()` 同样替换为：

```csharp
        public void Stop()
        {
            lock (sync)
            {
                activeBuildPids.Clear();
                activeIdePids.Clear();
            }
            ForceReportInactive();
        }
```

- [ ] **Step 4: 全量自测回归**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 197  PASS 195  FAIL 0  SKIP 2`——与 P2 终态完全一致（纯重构，测试数不变）。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: ScenarioBase 抽取——DevFocus 改继承，SuppressionReason 加 Daily=8 位"
```

---

### Task 2: DailyCatalog + DailyCare 骨架（家族检测与仲裁对接）

**Files:**
- Create: `src/Core/Scenario/DailyCatalog.cs`
- Create: `src/Core/Scenario/DailyCare.cs`
- Test: `tests/SelfTests.DailyCare.cs`
- Modify: `tests/SelfTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `tests/SelfTests.DailyCare.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 DailyCare 日常场景的自测：家族识别、活性判定、电池切换、压制位隔离

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestDailyCatalogMatch()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            // 名称+目录双命中
            Eq(true, DailyCatalog.IsMatch("chrome",
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe")));
            Eq(true, DailyCatalog.IsMatch("winword",
                Path.Combine(pf, @"Microsoft Office\root\Office16\WINWORD.EXE")));
            // 名称命中目录不对：不认
            Eq(false, DailyCatalog.IsMatch("chrome", @"C:\Temp\chrome.exe"));
            // 目录对名称不对：不认
            Eq(false, DailyCatalog.IsMatch("notepad",
                Path.Combine(pf, @"Google\Chrome\Application\notepad.exe")));
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
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                // 无家族窗口且非电池：不活跃
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
            Process probe = null;
            DailyCare daily = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                // chrome 名称+路径双命中但无可见窗口（探针无窗口）→ 不激活
                string beat;
                probe = StartNamedProbe(dir, "chrome.exe", out beat);
                WaitAdvance(beat, -1, 4000);
                string fakePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Google\Chrome\Application\chrome.exe");
                daily.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "chrome", fakePath, ProcessChangeKind.Started) }, false));

                Eq(false, daily.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            }
            finally
            {
                if (daily != null) try { daily.Stop(); } catch { }
                if (probe != null) StopOwned(probe);
                DeleteTempDir(dir);
            }
        }
    }
}
```

在 `tests/SelfTests.cs` 注册（P3 注册块，追加在 P2 注册块后）：

```csharp
            test("日常优化：家族双校验防同名误伤", TestDailyCatalogMatch);
            test("日常优化：电池供电激活与市电解除", TestDailyCareBatteryActivates);
            test("日常优化：家族进程无可见窗口不激活", TestDailyCareNoWindowNoActivate);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误（`DailyCatalog`/`DailyCare`/`SetBatteryForTest` 不存在）。

- [ ] **Step 3: 实现 DailyCatalog.cs**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 日常应用家族识别：浏览器/Office/会议，进程名 + 安装目录双重校验

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static class DailyCatalog
    {
        private sealed class Entry
        {
            public readonly string Name;
            public readonly string[] Roots;
            public Entry(string name, string[] roots) { Name = name; Roots = roots; }
        }

        private static string Pf { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } }
        private static string Pf86 { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86); } }
        private static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        private static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }

        private static Entry[] BuildEntries()
        {
            return new[]
            {
                // 浏览器
                new Entry("chrome", new[] { Path.Combine(Pf, @"Google\Chrome\Application\"), Path.Combine(Pf86, @"Google\Chrome\Application\") }),
                new Entry("msedge", new[] { Path.Combine(Pf86, @"Microsoft\Edge\Application\"), Path.Combine(Pf, @"Microsoft\Edge\Application\") }),
                new Entry("firefox", new[] { Path.Combine(Pf, @"Mozilla Firefox\"), Path.Combine(Pf86, @"Mozilla Firefox\") }),
                new Entry("brave", new[] { Path.Combine(Pf, @"BraveSoftware\Brave-Browser\Application\"), Path.Combine(Pf86, @"BraveSoftware\Brave-Browser\Application\") }),
                // Office / WPS
                new Entry("winword", new[] { Path.Combine(Pf, @"Microsoft Office\"), Path.Combine(Pf86, @"Microsoft Office\") }),
                new Entry("excel", new[] { Path.Combine(Pf, @"Microsoft Office\"), Path.Combine(Pf86, @"Microsoft Office\") }),
                new Entry("powerpnt", new[] { Path.Combine(Pf, @"Microsoft Office\"), Path.Combine(Pf86, @"Microsoft Office\") }),
                new Entry("outlook", new[] { Path.Combine(Pf, @"Microsoft Office\"), Path.Combine(Pf86, @"Microsoft Office\") }),
                new Entry("wps", new[] { Path.Combine(Local, @"Kingsoft\"), Path.Combine(Pf, @"Kingsoft\"), Path.Combine(Pf86, @"Kingsoft\") }),
                // 会议
                new Entry("zoom", new[] { Path.Combine(Roaming, @"Zoom\bin\") }),
                new Entry("teams", new[] { Path.Combine(Local, @"Microsoft\Teams\") }),
                new Entry("feishu", new[] { Path.Combine(Local, @"Feishu\"), Path.Combine(Pf, @"Feishu\") }),
                new Entry("dingtalk", new[] { Path.Combine(Pf86, @"DingDing\"), Path.Combine(Pf, @"DingDing\") })
            };
        }

        private static readonly object Sync = new object();
        private static Dictionary<string, Entry> byName;

        private static Dictionary<string, Entry> Map()
        {
            lock (Sync)
            {
                if (byName != null) return byName;
                var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (Entry e in BuildEntries()) map[e.Name] = e;
                byName = map;
                return map;
            }
        }

        public static bool NameMatches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Map().ContainsKey(StripExe(name));
        }

        public static bool IsMatch(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return false;
            Entry e;
            if (!Map().TryGetValue(StripExe(name), out e)) return false;
            string full = path;
            try { full = Path.GetFullPath(path); } catch { }
            foreach (string prefix in e.Roots)
                if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string StripExe(string name)
        {
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - 4);
            return name;
        }
    }
}
```

- [ ] **Step 4: 实现 DailyCare.cs 骨架**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 日常优化场景：日常家族活跃时压制后台并提优家族，电池供电自动升档

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace CaelusApp
{
    internal sealed class DailyCare : ScenarioBase
    {
        private readonly SuppressionCore core;
        private readonly Func<bool> enabled;
        private readonly Func<string, string, bool> isWhitelisted;
        private readonly HashSet<int> dailyPids = new HashSet<int>();
        private readonly Dictionary<int, uint> boosted = new Dictionary<int, uint>();
        private readonly Dictionary<int, long> boostedCreation = new Dictionary<int, long>();
        private bool familyVisible;
        private bool onBattery;
        private bool batteryBalloonShown;
        private long lastWindowCheckTicks;
        private System.Threading.Timer reconcileTimer;

        public override ScenarioKind Kind { get { return ScenarioKind.DailyCare; } }
        public override int Priority { get { return 10; } }

        public bool IsActive { get { lock (sync) return WantsActiveLocked; } }
        public bool IsGranted
        {
            get { lock (sync) return grantedFlag; }
        }

        private bool grantedFlag;

        protected override bool WantsActiveLocked
        {
            get { return enabled() && (familyVisible || onBattery); }
        }

        public DailyCare(ScenarioArbiter arbiter, SuppressionCore core,
            Func<bool> enabled, Func<string, string, bool> isWhitelisted)
            : base(arbiter)
        {
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            this.isWhitelisted = isWhitelisted;
            RefreshPowerState();
        }

        /// <summary>程序启动与 PowerLineStatusChanged 事件调用</summary>
        public void RefreshPowerState()
        {
            bool batt;
            try { batt = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline; }
            catch { batt = false; }
            bool changed;
            lock (sync)
            {
                changed = onBattery != batt;
                onBattery = batt;
                if (!batt) batteryBalloonShown = false;
            }
            if (changed) RecomputeActivity();
        }

        /// <summary>测试钩子：直接设置电池状态</summary>
        internal void SetBatteryForTest(bool batt)
        {
            lock (sync)
            {
                onBattery = batt;
                if (!batt) batteryBalloonShown = false;
            }
            RecomputeActivity();
        }

        public void NotifyProcessChanges(ProcessChangeBatch batch)
        {
            if (batch == null || batch.Changes == null) return;
            if (!enabled())
            {
                lock (sync) { dailyPids.Clear(); familyVisible = false; }
                ForceReportInactive();
                return;
            }

            lock (sync)
            {
                foreach (ProcessChange pc in batch.Changes)
                {
                    if (string.IsNullOrEmpty(pc.Name)) continue;
                    if (pc.Kind == ProcessChangeKind.Started)
                    {
                        if (IsDailyProcess(pc.Pid, pc.Name, pc.Path))
                            dailyPids.Add(pc.Pid);
                    }
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                    {
                        dailyPids.Remove(pc.Pid);
                    }
                }
                PruneDeadPids(dailyPids);
            }

            RefreshFamilyVisible(false);
            RecomputeActivity();
        }

        /// <summary>节流窗口复查：进程事件驱动，最多 5 秒一次全量枚举</summary>
        private void RefreshFamilyVisible(bool force)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (sync)
            {
                if (!force && now - lastWindowCheckTicks < 5L * TimeSpan.TicksPerSecond) return;
                lastWindowCheckTicks = now;
                if (dailyPids.Count == 0)
                {
                    familyVisible = false;
                    return;
                }
            }
            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { return; }
            lock (sync)
            {
                familyVisible = false;
                foreach (int pid in dailyPids)
                    if (visible.Contains(pid)) { familyVisible = true; break; }
            }
        }

        private bool IsDailyProcess(int pid, string name, string path)
        {
            if (!DailyCatalog.NameMatches(name)) return false;
            string p = path;
            if (string.IsNullOrEmpty(p))
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return false;
                try { p = Native.ImagePath(h); }
                finally { Native.CloseHandle(h); }
            }
            return DailyCatalog.IsMatch(name, p);
        }

        public override void Grant()
        {
            lock (sync)
            {
                if (grantedFlag) return;
                grantedFlag = true;
            }
            try
            {
                SweepDailySuppression();
                BoostVisibleFamily();
                StartReconcileTimer();
                MaybeShowBatteryBalloon();
                Logger.Log("日常优化：获得掌职权（家族窗口/电池），后台转入常规档压制");
            }
            catch (Exception ex) { Logger.LogFailure("日常优化掌权失败", ex); }
        }

        public override void Suspend()
        {
            lock (sync)
            {
                if (!grantedFlag) return;
                grantedFlag = false;
            }
            try
            {
                StopReconcileTimer();
                RestoreFamilyBoost();
                if (core != null) core.ReleaseReason(SuppressReason.Daily);
                Logger.Log("日常优化：挂起，全部副作用已还原（检测继续）");
            }
            catch (Exception ex) { Logger.LogFailure("日常优化挂起失败", ex); }
        }

        public void Stop()
        {
            lock (sync) { dailyPids.Clear(); familyVisible = false; }
            ForceReportInactive();
        }

        // ---- 以下方法在 Task 3 / Task 4 实现，本任务先给空调实现保证编译 ----
        private void SweepDailySuppression() { }
        private void BoostVisibleFamily() { }
        private void RestoreFamilyBoost() { }
        private void MaybeShowBatteryBalloon() { }
        private void StartReconcileTimer() { }
        private void StopReconcileTimer() { }
    }
}
```

**注意**：`TestDailyCareBatteryActivates` 断言 `IsGranted`——骨架的 Grant/Suspend 由仲裁器回调，空副作用实现已满足该测试；`TestDailyCareNoWindowNoActivate` 依赖 `RefreshFamilyVisible` 真实逻辑（骨架已含）。

- [ ] **Step 5: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 200  PASS 198  FAIL 0  SKIP 2`。

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: DailyCare 骨架——家族双校验 + 电池活性 + 仲裁对接（3 项自测）"
```

---

### Task 3: 日常压制 Sweep（Daily 位）+ 家族提优

**Files:**
- Modify: `src/Core/Scenario/DailyCare.cs`
- Test: `tests/SelfTests.DailyCare.cs`
- Modify: `tests/SelfTests.cs`

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.DailyCare.cs` 类内追加：

```csharp
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
                if (probe != null) StopOwned(probe);
                DeleteTempDir(dir);
            }
        }

        private static void TestDailyCareLevelChoice()
        {
            // 市电常规档 Eco，电池升 Restrained（纯逻辑）
            Eq(SuppressionLevel.Eco, DailyCare.ResolveDailyLevel(false));
            Eq(SuppressionLevel.Restrained, DailyCare.ResolveDailyLevel(true));
        }
```

在 `tests/SelfTests.cs` 注册：

```csharp
            test("日常优化：Daily 压制位与游戏位引用计数隔离", TestDailyCareReasonIsolation);
            test("日常优化：电池升档压制级别选择", TestDailyCareLevelChoice);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误（`DailyCare.ResolveDailyLevel` 不存在）。

- [ ] **Step 3: 实现压制与提优**

替换 Task 2 的空方法占位，在 `DailyCare.cs` 实现：

```csharp
        /// <summary>压制级别：市电 Eco，电池 Restrained（纯逻辑可单测）</summary>
        internal static SuppressionLevel ResolveDailyLevel(bool onBattery)
        {
            return onBattery ? SuppressionLevel.Restrained : SuppressionLevel.Eco;
        }

        /// <summary>全量扫描后台按 Daily 位压制。复用 DevFocus 的常规档豁免决策。</summary>
        private void SweepDailySuppression()
        {
            if (core == null) return;
            int selfPid = Process.GetCurrentProcess().Id;
            int ownerSession;
            try { ownerSession = Process.GetCurrentProcess().SessionId; } catch { ownerSession = -1; }
            int foregroundPid;
            try { foregroundPid = GameSessionDetector.ForegroundPid(); } catch { foregroundPid = 0; }
            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { visible = new HashSet<int>(); }
            string windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Func<string, string, bool> whitelist = isWhitelisted;
            bool batt;
            lock (sync) batt = onBattery;
            SuppressionLevel level = ResolveDailyLevel(batt);

            int suppressed = 0;
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
            foreach (Process p in all)
            {
                try
                {
                    int pid = p.Id;
                    if (pid <= 4 || pid == selfPid) continue;
                    lock (sync) { if (dailyPids.Contains(pid)) continue; }   // 家族本身不压

                    string nm;
                    try { nm = p.ProcessName; } catch { continue; }
                    int session;
                    try { session = p.SessionId; } catch { session = -1; }
                    string ipath = null;
                    IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (h != IntPtr.Zero)
                    {
                        try { ipath = Native.ImagePath(h); }
                        finally { Native.CloseHandle(h); }
                    }

                    if (!DevFocus.ShouldSuppressBackground(pid, selfPid, nm, ipath, session,
                        ownerSession, foregroundPid, visible, windowsRoot, whitelist)) continue;

                    AcquireResult r = core.Acquire(pid, nm, SuppressReason.Daily, "dailycare", level);
                    if (r == AcquireResult.NewlyThrottled) suppressed++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (suppressed > 0)
                Logger.Log("日常优化：压制 " + suppressed + " 个后台进程（"
                    + (batt ? "电池档" : "常规档") + "）");
        }

        /// <summary>提优有可见窗口的家族进程到 AboveNormal（快照+回读+PID 复用防护，同 IDE 模式）</summary>
        private void BoostVisibleFamily()
        {
            int[] family;
            lock (sync)
            {
                family = new int[dailyPids.Count];
                dailyPids.CopyTo(family);
            }
            if (family.Length == 0) return;
            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { return; }
            foreach (int pid in family)
            {
                if (!visible.Contains(pid)) continue;
                BoostOne(pid);
            }
        }

        private void BoostOne(int pid)
        {
            lock (sync) { if (boosted.ContainsKey(pid)) return; }
            long creation = 0;
            try { creation = Process.GetProcessById(pid).StartTime.Ticks; } catch { return; }
            IntPtr h = Native.OpenProcess(
                Native.PROCESS_SET_INFORMATION | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return;
            try
            {
                uint orig = Native.GetPriorityClass(h);
                if (orig == 0) return;
                if (orig == Native.HIGH_PRIORITY_CLASS || orig == 0x100) return;   // 更高不动
                if (orig == Native.ABOVE_NORMAL_PRIORITY_CLASS) return;

                Native.SetPriorityClass(h, Native.ABOVE_NORMAL_PRIORITY_CLASS);
                if (Native.GetPriorityClass(h) != Native.ABOVE_NORMAL_PRIORITY_CLASS) return;   // 回读
                Native.TrySetIoPriority(h, 3);
                lock (sync)
                {
                    boosted[pid] = orig;
                    boostedCreation[pid] = creation;
                }
            }
            catch { }
            finally { Native.CloseHandle(h); }
        }

        private void RestoreFamilyBoost()
        {
            KeyValuePair<int, uint>[] snap;
            KeyValuePair<int, long>[] snapCreation;
            lock (sync)
            {
                if (boosted.Count == 0) return;
                snap = new KeyValuePair<int, uint>[boosted.Count];
                boosted.CopyTo(snap, 0);
                boosted.Clear();
                snapCreation = new KeyValuePair<int, long>[boostedCreation.Count];
                boostedCreation.CopyTo(snapCreation, 0);
                boostedCreation.Clear();
            }
            var creationMap = new Dictionary<int, long>();
            foreach (var kv in snapCreation) creationMap[kv.Key] = kv.Value;

            foreach (var kv in snap)
            {
                try
                {
                    long expectCreation;
                    if (creationMap.TryGetValue(kv.Key, out expectCreation))
                    {
                        long nowCreation;
                        try { nowCreation = Process.GetProcessById(kv.Key).StartTime.Ticks; }
                        catch { continue; }
                        if (nowCreation != expectCreation) continue;   // PID 复用，不动
                    }
                    IntPtr h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION, false, kv.Key);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        Native.SetPriorityClass(h, kv.Value);
                        Native.TrySetIoPriority(h, 2);
                    }
                    finally { Native.CloseHandle(h); }
                }
                catch { }
            }
        }
```

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 202  PASS 200  FAIL 0  SKIP 2`。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: 日常压制 Sweep（Daily 位）+ 家族窗口提优（AboveNormal 快照回读还原）"
```

---

### Task 4: 电池能效（升档气球 + Timer 增量）+ Program.cs 接线

**Files:**
- Modify: `src/Core/Scenario/DailyCare.cs`（Timer 与气球实现）
- Modify: `src/Program.cs`（实例化 + 事件接线 + 退出路径）
- Modify: `src/Platform/Lang.cs`（bal.daily.batt）
- Test: `tests/SelfTests.DailyCare.cs`
- Modify: `tests/SelfTests.cs`

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.DailyCare.cs` 类内追加：

```csharp
        private static void TestDailyCareBatteryBalloonOnce()
        {
            string dir = NewTempDir("daily-balloon");
            DailyCare daily = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                daily = new DailyCare(arbiter, core, () => true, (n, p) => false);

                var balloons = new List<string>();
                daily.SessionChanged += key => balloons.Add(key);

                // 电池掌权 → 气球一次
                daily.SetBatteryForTest(true);
                Eq(true, daily.IsGranted);
                Eq(1, balloons.Count);
                Eq("bal.daily.batt", balloons[0]);

                // 市电再上电池 → 又报一次（标志在市电时清零）
                daily.SetBatteryForTest(false);
                daily.SetBatteryForTest(true);
                Eq(2, balloons.Count);
            }
            finally
            {
                if (daily != null) try { daily.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }
```

在 `tests/SelfTests.cs` 注册：

```csharp
            test("日常优化：电池气球每次脱电只报一次", TestDailyCareBatteryBalloonOnce);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误（`DailyCare.SessionChanged` 事件不存在）。

- [ ] **Step 3: 实现 Timer、气球与接线**

**改动 1 — DailyCare.cs 加事件声明**（字段区后）：

```csharp
        /// <summary>场景气球（bal.daily.batt 等文案 key）</summary>
        public event Action<string> SessionChanged;
```

**改动 2 — 实现 MaybeShowBatteryBalloon / Timer 方法**（替换 Task 2 空实现）：

```csharp
        /// <summary>电池掌权时建议一次电源模式（spec：建议不强制，不调 PowerOverlay）</summary>
        private void MaybeShowBatteryBalloon()
        {
            bool show;
            lock (sync)
            {
                show = onBattery && !batteryBalloonShown;
                if (show) batteryBalloonShown = true;
            }
            if (!show) return;
            try { var h = SessionChanged; if (h != null) h("bal.daily.batt"); } catch { }
            Logger.Log("日常优化：电池供电，后台压制已升档；建议电源模式调至更长续航");
        }

        private void StartReconcileTimer()
        {
            lock (sync)
            {
                if (reconcileTimer != null) return;
                reconcileTimer = new System.Threading.Timer(
                    _ => ReconcileTick(), null, 30000, 30000);
            }
        }

        private void StopReconcileTimer()
        {
            System.Threading.Timer t;
            lock (sync)
            {
                t = reconcileTimer;
                reconcileTimer = null;
            }
            if (t != null) t.Dispose();
        }

        /// <summary>掌权期校正：增量追压 + 家族提优复查 + 窗口活性刷新</summary>
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

**改动 3 — Grant 中电池气球位置确认**：Task 2 骨架的 `Grant()` 已调 `MaybeShowBatteryBalloon()`，无需改动。

**改动 4 — Lang.cs 加键**。找到 bal.distract 行（P2 所加），在其后插入：

```csharp
            { "bal.daily.batt", new[]{ "电池供电：后台压制已加强，建议电源模式调至更长续航" } },
```

**改动 5 — Program.cs 接线（四处）**：

**a) 实例化**——找到 P1 终态的 DevFocus 创建块：

```csharp
            gameMode.ActiveChanged += on => arbiter.ReportActivity(ScenarioKind.Game, on);
```

在其后插入：

```csharp
            var dailyCare = new DailyCare(arbiter, core,
                () => Settings.Load("DailyCareOn", true),
                gameMode.IsProcessWhitelisted);
            SystemEvents.PowerLineStatusChanged += (s2, e2) =>
            {
                try { dailyCare.RefreshPowerState(); } catch { }
            };
```

**b) 事件广播**——找到：

```csharp
                devFocus.NotifyProcessChanges(batch);
```

在其后插入：

```csharp
                dailyCare.NotifyProcessChanges(batch);
```

**c) 退出路径**——找到：

```csharp
                devFocus.Stop();
```

在其后插入：

```csharp
                dailyCare.Stop();
```

**d) SessionEnded 路径**——找到：

```csharp
                try { devFocus.Stop(); } catch { }
```

在其后插入：

```csharp
                try { dailyCare.Stop(); } catch { }
```

**e) 气球转发**——找到：

```csharp
            devFocus.SessionChanged += key =>
```

在该整段订阅块结束后插入同款订阅：

```csharp
            dailyCare.SessionChanged += key =>
            {
                try
                {
                    panel.BeginInvoke((MethodInvoker)(() =>
                    {
                        try { icon.ShowBalloonTip(5000, App.DisplayName, Lang.T(key), ToolTipIcon.Info); } catch { }
                    }));
                }
                catch { }
            };
```

- [ ] **Step 4: 全量回归 + 双构建**

```bash
cmd.exe //c "dev.cmd test"
cmd.exe //c "build.cmd"
cmd.exe //c "build-wpf.cmd"
```

预期：`TOTAL 203  PASS 201  FAIL 0  SKIP 2`；双构建 OK。

- [ ] **Step 5: 冒烟验证（手动）**

1. 启动 `Caelus.exe`，打开 Chrome/Edge 浏览网页 → 日志出现"日常优化：获得掌职权"
2. 笔记本拔电源（或电源选项模拟）→ 气球建议一次 + 日志"电池供电，后台压制已升档"
3. 启动任意游戏 → 日志"日常优化：挂起"（游戏抢占）；退出游戏 → 日常场景补位恢复
4. 关闭浏览器全部窗口 → 30 秒内日志"挂起，全部副作用已还原"

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: DailyCare 电池能效 + 全接线——三场景仲裁闭环（203 项自测 0 失败）"
```

---

## Self-Review 记录

**Spec 覆盖**：日常场景调度（家族双校验/常规档压制/家族 AboveNormal 提优）✓；电池能效（PowerLineStatus 驱动/升档 Restrained/气球建议不强制/插回还原）✓；ScenarioBase 抽取兑现 P1 承诺 ✓。健康维护属 P4。

**类型一致性**：`DailyCare(arbiter, core, enabled, isWhitelisted)` 4 参构造与 Program.cs 接线一致；`WantsActiveLocked/ForceReportInactive` 基类成员在 DevFocus/DailyCare 两侧引用一致；`ResolveDailyLevel(bool)→SuppressionLevel` 静态纯逻辑可测；`SetBatteryForTest/SessionChanged/IsGranted/Stop` 测试与实现一致。

**已知取舍**：
- 家族活性检测无自有定时器（进程事件驱动 + 5 秒节流）；系统静默时窗口关闭的解除有延迟，无害
- 电池升档不追溯调档已压进程（下一轮增量压制自然生效，避免全量重扫抖动）
- 新版 Teams（MSIX/WindowsApps 路径）识别率受限——目录前缀匹配不到时退化为不识别，不误伤
- DailyCare 的 `IsGranted`/`grantedFlag` 未复用 DevFocus 的 `granted` 命名（两场景独立演进，语义相同）——若后续统一可在 ScenarioBase 上收编
