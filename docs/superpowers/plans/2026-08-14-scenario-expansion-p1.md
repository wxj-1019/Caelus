# 场景扩展 P1：场景仲裁器 + DevFocus 编译深化 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立场景仲裁基础设施（ScenarioArbiter），把现有 BuildWatch 改造为 DevFocus 场景（实现 IScenario），并启用 `SuppressReason.Build` 空位实现编译期真后台压制——游戏模式零逻辑改动。

**Architecture:** 仲裁器只仲裁"副作用掌权资格"（游戏 100 > 开发 50），检测永远运行；被抢占场景还原式挂起。GameMode 通过新增的 `ActiveChanged` 事件接线到仲裁器，本体不感知仲裁器存在。

**Tech Stack:** C# 5（csc.exe v4.0.30319，**禁止** `?.`、字符串插值、`out var`、`nameof`、expression-bodied 成员）、.NET Framework 4.x、WinForms。构建用 `cmd.exe //c "build.cmd"`，自测用 `cmd.exe //c "dev.cmd test"`（Git Bash 下必须双斜杠 `//c`）。

**Spec:** `docs/superpowers/specs/2026-08-14-scenario-expansion-design.md`

---

## 文件结构

| 操作 | 文件 | 职责 |
|---|---|---|
| Create | `src/Core/Scenario/ScenarioKind.cs` | 场景枚举（Game/DevFocus/DailyCare） |
| Create | `src/Core/Scenario/IScenario.cs` | 场景接口（Kind/Priority/Grant/Suspend） |
| Create | `src/Core/Scenario/ScenarioArbiter.cs` | 仲裁器：活跃集 + 优先级求胜 + 锁外副作用 |
| Create | `src/Core/Scenario/DevFocus.cs` | 开发专注场景（BuildWatch 的场景化替代） |
| Delete | `src/Core/BuildWatch.cs` | 被 DevFocus.cs 替代 |
| Modify | `src/Core/GameMode.cs:435` 附近 | 加 `ActiveChanged` 事件 + 激活点（L966）触发 + `SimulateActiveForTest`（L438）触发 |
| Modify | `src/Core/GameMode.Boost.cs:1022` `Deactivate` | 解除点触发 `ActiveChanged(false)` |
| Modify | `src/Core/GameMode.Whitelist.cs` | 加 `IsProcessWhitelisted(name, path)` 规则级查询 |
| Modify | `src/Program.cs` | 仲裁器实例化与全部接线（L232/L269/L319/L354/L382） |
| Create | `tests/SelfTests.Arbiter.cs` | 仲裁器测试（FakeScenario 调用序列断言） |
| Create | `tests/SelfTests.DevFocus.cs` | DevFocus 仲裁集成 + 压制决策测试 |
| Modify | `tests/SelfTests.cs` `Run()` | 注册新测试 |

**构建事实（已核实）**：`build.cmd` 用 `-recurse:src\*.cs` 与 `-recurse:tests\*.cs` 通配编译，新增/删除文件**无需登记**。WPF 项目（`wpf/Caelus.Wpf.csproj`）不引用 `src/` 任何文件，仅经注册表键 `DevModeOn` 共享设置——本计划对 WPF 构建零影响。

## 对 spec 的两处细化（已按代码核实修正）

1. **不建 `ScenarioBase`**（spec 原列于 P1）：代码核实后发现豁免计算器可直接复用 `GameMode.BasicBackgroundEligible`（`GameMode.Sweep.cs:83`，internal static，内部已含反作弊/平台/加速器/系统目录/前台/会话检查），P1 只有 DevFocus 一个场景实现 IScenario——此时抽基类是投机抽象。推迟到 P3 DailyCare 落地时，有了真实第二消费者再抽取。
2. **豁免"受控重复"改为"零重复复用"**：spec 原接受复制一份豁免逻辑；核实后确认 `BasicBackgroundEligible` 就是常规档豁免计算器，DevFocus 直接调用，GameMode 完全不动。

## 已核实的代码事实（计划代码的依据）

- `SuppressReason`：`None=0, AntiCheat=1, Background=2, Build=4`（`SuppressionCore.cs:9-16`，Build 位已存在，**无需改枚举**）
- `SuppressionCore`：`Acquire(pid, name, reason, group, level) → AcquireResult`（L218）；`ReleaseReason(reason) → int`（L413）；`HasReason(pid, reason)`（L584）；`Release(pid, reason)`（L396）
- `SuppressionLevel.Eco=1`（常规档，见 `GameMode.Sweep.cs:79-80`）
- `GameMode.BasicBackgroundEligible(pid, self, name, path, session, ownerSession, foreground, userFacingFamily, windowsRoot, gameHostAncestor=false, activeGameRoot=null, aggressive=false)`（`GameMode.Sweep.cs:83`）
- `GameSessionDetector.ForegroundPid()` internal static（L665）；`GameSessionDetector.VisibleWindowPids(bool includeMinimized)` internal static（L630）
- `Native.ImagePath(IntPtr h)` public static（`Native.cs:265`）；`Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, ...)`、`Native.StillActive`、`Native.CloseHandle`（BuildWatch 已用）
- `SvcPause.Activate()/Restore()` 静态（`src/Core/Tweaks/SvcPause.cs:17,73`）
- `Settings.Load(name, def)/Save(name, val)`；`Logger.Log(msg)/LogFailure(context, ex)`
- `WhitelistRule.NormalizeName(name)` / `NormalizeImagePath(path)` 静态；`MatchesNormalized(nn, np)` internal（`WhitelistRule.cs:106`）
- `GameMode` active 写入点：`GameMode.cs:966`（激活）、`GameMode.Boost.cs:1025`（`Deactivate` 内解除）、`GameMode.cs:438` `SimulateActiveForTest`
- 测试注册：`tests/SelfTests.cs` `Run()` 内 `test("名称", () => {...})` lambda；断言 `Eq<T>(expected, actual)`（`SelfTests.Infrastructure.cs:139`）；探针 `StartProbe(beatFile)` / `WaitAdvance(beat, prev, ms)` / `StopOwned(proc)`
- `ProcessChangeBatch(ProcessChange[] changes, bool overflowed)`；`ProcessChange` 公开字段 `Pid/ParentPid/Session/Name/Path/Creation`；`ProcessChangeKind.Started/Stopped`
- 自测基线：**TOTAL 178, PASS 176, FAIL 0, SKIP 2**（2 个 SKIP 是机器环境，非回归）

---

### Task 1: ScenarioKind + IScenario + ScenarioArbiter（仲裁器核心）

**Files:**
- Create: `src/Core/Scenario/ScenarioKind.cs`
- Create: `src/Core/Scenario/IScenario.cs`
- Create: `src/Core/Scenario/ScenarioArbiter.cs`
- Test: `tests/SelfTests.Arbiter.cs`
- Modify: `tests/SelfTests.cs`（注册测试）

- [ ] **Step 1: 写失败测试**

新建 `tests/SelfTests.Arbiter.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 场景仲裁器的自测：优先级求胜、抢占顺序、挂起补位、事件通知

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private sealed class FakeScenario : IScenario
        {
            public ScenarioKind Kind { get; private set; }
            public int Priority { get; private set; }
            public readonly List<string> Calls = new List<string>();

            public FakeScenario(ScenarioKind kind, int priority)
            {
                Kind = kind;
                Priority = priority;
            }

            public void Grant() { Calls.Add("G:" + Kind); }
            public void Suspend() { Calls.Add("S:" + Kind); }
        }

        private static ScenarioArbiter NewArbiterWithAll(
            out FakeScenario game, out FakeScenario dev, out FakeScenario daily)
        {
            var arbiter = new ScenarioArbiter();
            game = new FakeScenario(ScenarioKind.Game, 100);
            dev = new FakeScenario(ScenarioKind.DevFocus, 50);
            daily = new FakeScenario(ScenarioKind.DailyCare, 10);
            arbiter.Register(game);
            arbiter.Register(dev);
            arbiter.Register(daily);
            return arbiter;
        }

        // 注意：与 CurrentGranted（ScenarioKind?）比较必须显式 Eq<ScenarioKind?>——
        // Eq<T> 两个参数类型不同（ScenarioKind vs ScenarioKind?）会让泛型推断失败（CS0411）
        private static void TestArbiterSingleActivation()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            Eq<ScenarioKind?>(null, arbiter.CurrentGranted);

            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
            Eq(1, dev.Calls.Count);
            Eq("G:DevFocus", dev.Calls[0]);
        }

        private static void TestArbiterPreemptionOrder()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);

            arbiter.ReportActivity(ScenarioKind.Game, true);
            Eq<ScenarioKind?>(ScenarioKind.Game, arbiter.CurrentGranted);
            // 先挂起旧掌权者，再授权新掌权者（顺序是要害）
            Eq(2, dev.Calls.Count);
            Eq("S:DevFocus", dev.Calls[1]);
            Eq(1, game.Calls.Count);
            Eq("G:Game", game.Calls[0]);
        }

        private static void TestArbiterResumeAfterPreemption()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.Game, true);

            arbiter.ReportActivity(ScenarioKind.Game, false);
            // 游戏退出后开发场景补位恢复（它的检测状态仍在）
            Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
            Eq("G:DevFocus", dev.Calls[2]);
        }

        private static void TestArbiterLowPriorityNoPreempt()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);

            arbiter.ReportActivity(ScenarioKind.DailyCare, true);
            Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
            Eq(0, daily.Calls.Count);
        }

        private static void TestArbiterEmptyGrantsNull()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.DevFocus, false);
            Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            Eq("S:DevFocus", dev.Calls[1]);
        }

        private static void TestArbiterDuplicateReportNoOp()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.Game, false);
            Eq(1, dev.Calls.Count);
            Eq(0, game.Calls.Count);
        }

        private static void TestArbiterGrantedChangedEvent()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            var seen = new List<ScenarioKind?>();
            arbiter.GrantedChanged += k => seen.Add(k);

            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.DevFocus, false);
            Eq(2, seen.Count);
            Eq<ScenarioKind?>(ScenarioKind.DevFocus, seen[0]);
            Eq<ScenarioKind?>(null, seen[1]);
        }
    }
}
```

在 `tests/SelfTests.cs` 的 `Run()` 方法里注册。锚点：找到行 `test("进程事件：延迟启动不会接上过期的父进程身份", TestProcNotifyParentIdentity);`，在其**后**插入：

```csharp
            test("场景仲裁：单场景激活即掌权", TestArbiterSingleActivation);
            test("场景仲裁：高优先级抢占先挂起后授权", TestArbiterPreemptionOrder);
            test("场景仲裁：抢占解除后低优先级补位", TestArbiterResumeAfterPreemption);
            test("场景仲裁：低优先级激活不抢占", TestArbiterLowPriorityNoPreempt);
            test("场景仲裁：全部解除后掌权者为空", TestArbiterEmptyGrantsNull);
            test("场景仲裁：重复报告无副作用", TestArbiterDuplicateReportNoOp);
            test("场景仲裁：掌权者变更事件", TestArbiterGrantedChangedEvent);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误 `CS0246: 未能找到类型或命名空间 IScenario / ScenarioArbiter / ScenarioKind`。

- [ ] **Step 3: 实现仲裁器三文件**

新建 `src/Core/Scenario/ScenarioKind.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 场景标识：游戏 / 开发专注 / 日常优化

namespace CaelusApp
{
    internal enum ScenarioKind
    {
        Game = 0,
        DevFocus = 1,
        DailyCare = 2
    }
}
```

新建 `src/Core/Scenario/IScenario.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 场景契约：仲裁器只仲裁副作用掌权资格，检测由各场景自行持续运行

namespace CaelusApp
{
    internal interface IScenario
    {
        ScenarioKind Kind { get; }
        int Priority { get; }

        /// <summary>获得副作用掌职权，施加本场景的全部系统副作用。在仲裁器锁外调用。</summary>
        void Grant();

        /// <summary>还原本场景的全部系统副作用并挂起；检测状态必须继续维护。在仲裁器锁外调用。</summary>
        void Suspend();
    }
}
```

新建 `src/Core/Scenario/ScenarioArbiter.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 场景仲裁器：按严格优先级决定哪个场景持有系统副作用掌权资格

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class ScenarioArbiter
    {
        private readonly object sync = new object();
        private readonly Dictionary<ScenarioKind, IScenario> scenarios =
            new Dictionary<ScenarioKind, IScenario>();
        private readonly HashSet<ScenarioKind> active = new HashSet<ScenarioKind>();
        private ScenarioKind? granted;

        /// <summary>掌权者变化时触发，参数是新掌权者（无人掌权为 null）。在锁外触发。</summary>
        public event Action<ScenarioKind?> GrantedChanged;

        public ScenarioKind? CurrentGranted { get { lock (sync) return granted; } }

        public void Register(IScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException("scenario");
            lock (sync) scenarios[scenario.Kind] = scenario;
        }

        /// <summary>纯逻辑：给定活跃集合，返回优先级最高的场景；无活跃返回 null。</summary>
        internal static ScenarioKind? Evaluate(
            HashSet<ScenarioKind> activeSet, Dictionary<ScenarioKind, IScenario> map)
        {
            ScenarioKind? winner = null;
            int best = int.MinValue;
            foreach (ScenarioKind kind in activeSet)
            {
                IScenario s;
                if (!map.TryGetValue(kind, out s)) continue;
                if (!winner.HasValue || s.Priority > best)
                {
                    winner = kind;
                    best = s.Priority;
                }
            }
            return winner;
        }

        /// <summary>场景报告自身活性。锁内只记账，Grant/Suspend 在锁外执行。</summary>
        public void ReportActivity(ScenarioKind kind, bool isActive)
        {
            IScenario toSuspend = null;
            IScenario toGrant = null;
            ScenarioKind? nowGranted;
            lock (sync)
            {
                if (isActive) active.Add(kind);
                else active.Remove(kind);

                nowGranted = Evaluate(active, scenarios);
                if (nowGranted.Equals(granted)) return;

                IScenario s;
                if (granted.HasValue && scenarios.TryGetValue(granted.Value, out s))
                    toSuspend = s;
                granted = nowGranted;
                if (granted.HasValue && scenarios.TryGetValue(granted.Value, out s))
                    toGrant = s;
            }

            // 先还原旧掌权者，再授权新掌权者（顺序是要害：系统状态任一时刻只反映一个场景）
            if (toSuspend != null)
            {
                try { toSuspend.Suspend(); }
                catch (Exception ex) { Logger.LogFailure("场景挂起失败（" + toSuspend.Kind + "）", ex); }
            }
            if (toGrant != null)
            {
                try { toGrant.Grant(); }
                catch (Exception ex) { Logger.LogFailure("场景授权失败（" + toGrant.Kind + "）", ex); }
            }
            var handler = GrantedChanged;
            if (handler != null)
            {
                try { handler(nowGranted); } catch { }
            }
        }
    }
}
```

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 185  PASS 183  FAIL 0  SKIP 2`（基线 178 + 新增 7）。FAIL 必须为 0。

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/ tests/SelfTests.Arbiter.cs tests/SelfTests.cs
git commit -m "feat: 场景仲裁器 ScenarioArbiter——严格优先级仲裁副作用掌权资格（7 项自测）"
```

---

### Task 2: GameMode 最小接线（ActiveChanged 事件 + IsProcessWhitelisted 查询）

**Files:**
- Modify: `src/Core/GameMode.cs`（L435 附近加事件、L966 激活点触发、L438 `SimulateActiveForTest` 触发）
- Modify: `src/Core/GameMode.Boost.cs`（`Deactivate` 解除点触发）
- Modify: `src/Core/GameMode.Whitelist.cs`（加查询方法）
- Test: `tests/SelfTests.Arbiter.cs`（追加 GameMode 接线测试）
- Modify: `tests/SelfTests.cs`（注册）

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.Arbiter.cs` **类内**追加：

```csharp
        private static void TestGameModeActiveChangedEvent()
        {
            string dir = NewTempDir("arbiter-gm");
            try
            {
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                var gm = new GameMode(dir, core);
                var seen = new List<bool>();
                gm.ActiveChanged += on => seen.Add(on);

                gm.SimulateActiveForTest(true);
                gm.SimulateActiveForTest(false);
                Eq(2, seen.Count);
                Eq(true, seen[0]);
                Eq(false, seen[1]);
            }
            finally { DeleteTempDir(dir); }
        }

        private static void TestGameModeWhitelistQueryEmpty()
        {
            string dir = NewTempDir("arbiter-wl");
            try
            {
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                var gm = new GameMode(dir, core);
                // 空白名单下任何进程都不被豁免
                Eq(false, gm.IsProcessWhitelisted("chrome", @"C:\Apps\chrome.exe"));
            }
            finally { DeleteTempDir(dir); }
        }
```

**检查测试辅助方法**：`NewTempDir`/`DeleteTempDir` 是否已存在于 `tests/SelfTests.Infrastructure.cs`。运行：

```bash
grep -n "NewTempDir\|DeleteTempDir\|static.*TempDir" tests/SelfTests.Infrastructure.cs | head -5
```

- 若**已存在**同名辅助：直接用，跳过下面的新建。
- 若**不存在**：在 `tests/SelfTests.Infrastructure.cs` 的 `Eq<T>` 方法附近追加（先 `Read` 该文件确认 using 与结构）：

```csharp
        private static string NewTempDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "Caelus.test." + tag + "." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void DeleteTempDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
```

同时确认该文件有 `using System.IO;`（无则补）。

在 `tests/SelfTests.cs` 的仲裁器注册块后追加：

```csharp
            test("场景仲裁：游戏激活事件驱动仲裁报告", TestGameModeActiveChangedEvent);
            test("场景仲裁：空白名单查询不误豁免", TestGameModeWhitelistQueryEmpty);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误 `CS0117/CS1061: GameMode 不包含 ActiveChanged / IsProcessWhitelisted 的定义`。

- [ ] **Step 3: 实现 GameMode 接线（3 处触发 + 1 个查询）**

**改动 1** — `src/Core/GameMode.cs` L435 附近，找到：

```csharp
        public bool IsActive { get { lock (sync) return active; } }
```

在其后插入事件声明：

```csharp
        public bool IsActive { get { lock (sync) return active; } }

        /// <summary>游戏会话活性变化时触发（true=激活 false=解除）。供场景仲裁器接线，UI 轮询 IsActive 不受影响。</summary>
        public event Action<bool> ActiveChanged;
```

**改动 2** — `src/Core/GameMode.cs` L438 的 `SimulateActiveForTest`，找到：

```csharp
        internal void SimulateActiveForTest(bool value) { lock (sync) active = value; }
```

替换为：

```csharp
        internal void SimulateActiveForTest(bool value)
        {
            lock (sync) active = value;
            var h = ActiveChanged;
            if (h != null) h(value);
        }
```

**改动 3** — `src/Core/GameMode.cs` L966 附近激活点，找到：

```csharp
                                    if (!active)
                                    {
                                        lock (sync) { active = true; activeGame = running; firstSweep = true; }
                                        Logger.Log("游戏模式激活：检测到 " + running);
                                        ReportBegin(running);
                                    }
```

替换为（事件触发放在 lock 外、日志之后）：

```csharp
                                    if (!active)
                                    {
                                        lock (sync) { active = true; activeGame = running; firstSweep = true; }
                                        Logger.Log("游戏模式激活：检测到 " + running);
                                        var h = ActiveChanged;
                                        if (h != null) { try { h(true); } catch { } }
                                        ReportBegin(running);
                                    }
```

**改动 4** — `src/Core/GameMode.Boost.cs` 的 `Deactivate(string reason)` 方法（约 L1022），找到：

```csharp
        private bool Deactivate(string reason)
        {
            lock (sync)
            {
                active = false;
                activeGame = null;
                firstSweep = true;
            }
            gameGoneSinceTicks = 0;
```

在 `gameGoneSinceTicks = 0;` 行之后插入：

```csharp
            var activeChangedHandler = ActiveChanged;
            if (activeChangedHandler != null) { try { activeChangedHandler(false); } catch { } }
```

**改动 5** — `src/Core/GameMode.Whitelist.cs`，在 `NeedsWhitelistParentIdentity` 方法（L47）之后插入：

```csharp
        /// <summary>规则级白名单查询：名称/精确路径规则是否命中该进程（家族规则的子进程扩展不展开）。
        /// 供 DevFocus 等场景的压制豁免复用；匹配前内部做规范化。</summary>
        internal bool IsProcessWhitelisted(string name, string imagePath)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string nn = WhitelistRule.NormalizeName(name);
            string np = string.IsNullOrEmpty(imagePath)
                ? null : WhitelistRule.NormalizeImagePath(imagePath);
            lock (whiteEvalSync)
            {
                for (int i = 0; i < whiteRules.Count; i++)
                    if (whiteRules[i].MatchesNormalized(nn, np)) return true;
            }
            return false;
        }
```

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 187  PASS 185  FAIL 0  SKIP 2`。

- [ ] **Step 5: Commit**

```bash
git add src/Core/GameMode.cs src/Core/GameMode.Boost.cs src/Core/GameMode.Whitelist.cs tests/
git commit -m "feat: GameMode 场景接线——ActiveChanged 事件 + 白名单规则级查询（零逻辑改动）"
```

---

### Task 3: BuildWatch → DevFocus 场景化改造（含 Program.cs 接线）

**Files:**
- Create: `src/Core/Scenario/DevFocus.cs`
- Delete: `src/Core/BuildWatch.cs`
- Modify: `src/Program.cs`（L232/L269/L319/L354/L382 五处）
- Test: `tests/SelfTests.DevFocus.cs`
- Modify: `tests/SelfTests.cs`（注册）

- [ ] **Step 1: 写失败测试**

新建 `tests/SelfTests.DevFocus.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 DevFocus 场景的自测：仲裁集成、活性报告、开关语义、抢占挂起

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        /// <summary>把当前自测 exe 复制为指定文件名并启动心跳探针——
        /// 得到一个"进程名匹配 BuildCatalog"的真实活进程。</summary>
        private static Process StartNamedProbe(string dir, string exeName, out string beat)
        {
            beat = Path.Combine(dir, exeName + ".beat");
            string copy = Path.Combine(dir, exeName);
            File.Copy(Application.ExecutablePath, copy, true);
            var psi = new ProcessStartInfo(copy, "--test-heartbeat-probe \"" + beat + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            return Process.Start(psi);
        }

        private static DevFocus NewDevFocus(ScenarioArbiter arbiter, SuppressionCore core,
            Func<bool> enabled)
        {
            return new DevFocus(arbiter, core, enabled, (name, path) => false);
        }

        private static ProcessChange MakeChange(int pid, string name, ProcessChangeKind kind)
        {
            var pc = new ProcessChange();
            pc.Pid = pid;
            pc.Name = name;
            pc.Kind = kind;
            return pc;
        }

        // 注意：Grant 会真实调 SvcPause.Activate（暂停 SysMain/WSearch）——
        // 每个测试的 finally 必须 dev.Stop() 兜底还原，断言失败也不能把服务留在暂停态
        private static void TestDevFocusGrantAndRelease()
        {
            string dir = NewTempDir("devfocus-grant");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = NewDevFocus(arbiter, core, () => true);

                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                WaitAdvance(beat, -1, 4000);

                // 编译进程启动 → DevFocus 报告活跃 → 仲裁器授权
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsActive);
                Eq(true, dev.IsGranted);
                Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);

                // 编译进程退出 → 报告不活跃 → 仲裁器收回授权
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Stopped) }, false));
                Eq(false, dev.IsActive);
                Eq(false, dev.IsGranted);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) StopOwned(probe);
                DeleteTempDir(dir);
            }
        }

        private static void TestDevFocusPreemptedByGame()
        {
            string dir = NewTempDir("devfocus-preempt");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = NewDevFocus(arbiter, core, () => true);

                string beat;
                probe = StartNamedProbe(dir, "csc.exe", out beat);
                WaitAdvance(beat, -1, 4000);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "csc", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsGranted);

                // 游戏激活 → DevFocus 被挂起（副作用还原），但活性检测保留
                arbiter.ReportActivity(ScenarioKind.Game, true);
                Eq(false, dev.IsGranted);
                Eq(true, dev.IsActive);
                Eq<ScenarioKind?>(ScenarioKind.Game, arbiter.CurrentGranted);

                // 游戏退出 → DevFocus 补位恢复
                arbiter.ReportActivity(ScenarioKind.Game, false);
                Eq(true, dev.IsGranted);
                Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) StopOwned(probe);
                DeleteTempDir(dir);
            }
        }

        private static void TestDevFocusDisabledSwitch()
        {
            string dir = NewTempDir("devfocus-off");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                bool on = false;
                dev = NewDevFocus(arbiter, core, () => on);

                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                WaitAdvance(beat, -1, 4000);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                // 开关关闭：不报告、不掌权
                Eq(false, dev.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);

                // 开关打开后事件到达：正常激活；再关闭：立即解除
                on = true;
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsGranted);
                on = false;
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new ProcessChange[0], false));
                Eq(false, dev.IsGranted);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) StopOwned(probe);
                DeleteTempDir(dir);
            }
        }
    }
}
```

**确认测试辅助方法签名**：`StartProbe`/`WaitAdvance`/`StopOwned` 在 `tests/SelfTests.Infrastructure.cs`（或相邻文件）。运行确认：

```bash
grep -n "static.*StartProbe\|static.*WaitAdvance\|static.*StopOwned" tests/SelfTests*.cs | head -5
```

若 `WaitAdvance` 签名不是 `(string beat, long prev, int ms)`，按实际签名调整测试调用。

在 `tests/SelfTests.cs` 注册（追加在 Task 2 注册块后）：

```csharp
            test("开发专注：编译进程激活掌权与退出还原", TestDevFocusGrantAndRelease);
            test("开发专注：游戏激活抢占挂起与补位恢复", TestDevFocusPreemptedByGame);
            test("开发专注：开关关闭不激活且立即解除", TestDevFocusDisabledSwitch);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误 `CS0246: 未能找到类型或命名空间 DevFocus`。

- [ ] **Step 3: 实现 DevFocus.cs 并删除 BuildWatch.cs**

新建 `src/Core/Scenario/DevFocus.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 开发专注场景：检测编译/调试进程，掌权时暂停索引、提优编译器并压制后台

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class DevFocus : IScenario
    {
        private readonly object sync = new object();
        private readonly ScenarioArbiter arbiter;
        private readonly SuppressionCore core;
        private readonly Func<bool> enabled;
        private readonly Func<string, string, bool> isWhitelisted;
        private readonly HashSet<int> activeBuildPids = new HashSet<int>();
        private bool granted;
        private bool reported;
        private long sessionStartTicks;

        /// <summary>编译会话状态变化时触发，参数是文案 key（bal.buildstart / bal.buildend）</summary>
        public event Action<string> SessionChanged;

        public ScenarioKind Kind { get { return ScenarioKind.DevFocus; } }
        public int Priority { get { return 50; } }

        /// <summary>检测状态：是否存在活跃的编译进程（与是否掌权无关）</summary>
        public bool IsActive { get { lock (sync) return activeBuildPids.Count > 0; } }

        /// <summary>仲裁器授权状态：副作用是否已施加</summary>
        public bool IsGranted { get { lock (sync) return granted; } }

        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core,
            Func<bool> enabled, Func<string, string, bool> isWhitelisted)
        {
            if (arbiter == null) throw new ArgumentNullException("arbiter");
            this.arbiter = arbiter;
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            this.isWhitelisted = isWhitelisted;
            arbiter.Register(this);
        }

        public void NotifyProcessChanges(ProcessChangeBatch batch)
        {
            if (batch == null || batch.Changes == null) return;

            // 开关关闭：撤销活性报告（仲裁器会回调 Suspend 还原副作用），避免服务被永久暂停
            if (!enabled())
            {
                bool wasReported;
                lock (sync)
                {
                    wasReported = reported;
                    reported = false;
                    activeBuildPids.Clear();
                }
                if (wasReported) arbiter.ReportActivity(Kind, false);
                return;
            }

            bool becameActive = false;
            bool becameIdle = false;

            lock (sync)
            {
                foreach (ProcessChange pc in batch.Changes)
                {
                    if (string.IsNullOrEmpty(pc.Name)) continue;
                    if (!BuildCatalog.IsMatch(pc.Name)) continue;

                    if (pc.Kind == ProcessChangeKind.Started)
                        activeBuildPids.Add(pc.Pid);
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                        activeBuildPids.Remove(pc.Pid);
                }

                // 兜底清理：短命编译进程的 Stopped 事件可能因进程已退出而丢失，
                // PID 会永远留在集合里导致会话悬挂。每次事件到达时清理已死的 PID。
                if (activeBuildPids.Count > 0)
                {
                    var dead = new List<int>();
                    foreach (int pid in activeBuildPids)
                    {
                        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                        if (h == IntPtr.Zero)
                        {
                            dead.Add(pid);
                            continue;
                        }
                        try
                        {
                            if (!Native.StillActive(h)) dead.Add(pid);
                        }
                        finally { Native.CloseHandle(h); }
                    }
                    foreach (int pid in dead) activeBuildPids.Remove(pid);
                }

                bool nowActive = activeBuildPids.Count > 0;
                if (nowActive && !reported)
                {
                    reported = true;
                    becameActive = true;
                    sessionStartTicks = DateTime.UtcNow.Ticks;
                }
                else if (!nowActive && reported)
                {
                    reported = false;
                    becameIdle = true;
                }
            }

            // 活性变化只向仲裁器报告；副作用由仲裁器经 Grant/Suspend 回调控制
            if (becameActive)
            {
                try { var h = SessionChanged; if (h != null) h("bal.buildstart"); } catch { }
                arbiter.ReportActivity(Kind, true);
            }
            if (becameIdle)
            {
                long elapsedMs = (DateTime.UtcNow.Ticks - sessionStartTicks) / TimeSpan.TicksPerMillisecond;
                if (elapsedMs >= 0)
                    Logger.Log(string.Format("开发专注：本次编译 {0:0.#} 秒", elapsedMs / 1000.0));
                try { var h = SessionChanged; if (h != null) h("bal.buildend"); } catch { }
                arbiter.ReportActivity(Kind, false);
            }
        }

        /// <summary>IScenario：获得掌职权——暂停索引服务、提优编译进程、压制后台（Task 4 加入）</summary>
        public void Grant()
        {
            lock (sync)
            {
                if (granted) return;
                granted = true;
            }
            try
            {
                SvcPause.Activate();
                BoostBuildProcesses();
                Logger.Log("开发专注：获得掌职权，已暂停索引服务并提优编译进程");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注掌权失败", ex); }
        }

        /// <summary>IScenario：挂起——还原全部副作用，检测状态保留</summary>
        public void Suspend()
        {
            lock (sync)
            {
                if (!granted) return;
                granted = false;
            }
            try
            {
                SvcPause.Restore();
                Logger.Log("开发专注：挂起，索引服务已恢复（编译检测继续）");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注挂起失败", ex); }
        }

        private void BoostBuildProcesses()
        {
            int[] pids;
            lock (sync)
            {
                pids = new int[activeBuildPids.Count];
                activeBuildPids.CopyTo(pids);
            }
            foreach (int pid in pids)
            {
                try
                {
                    IntPtr h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        Native.SetPriorityClass(h, Native.HIGH_PRIORITY_CLASS);
                        Native.TrySetIoPriority(h, 3);
                    }
                    finally { Native.CloseHandle(h); }
                }
                catch { }
            }
        }

        /// <summary>程序退出时调用，确保还原。仅在 ProcNotify 停止后调用（退出路径单线程）</summary>
        public void Stop()
        {
            bool wasReported;
            lock (sync)
            {
                wasReported = reported;
                reported = false;
                activeBuildPids.Clear();
            }
            // 走仲裁器单一路径还原（若正掌权会回调 Suspend）
            if (wasReported) arbiter.ReportActivity(Kind, false);
        }
    }
}
```

删除旧文件：

```bash
git rm src/Core/BuildWatch.cs
```

**同步修改 `src/Program.cs`（五处，否则编译不过）**：

**改动 1** — L232 附近，找到：

```csharp
            var buildWatch = new BuildWatch(() => gameMode.IsActive);
```

替换为：

```csharp
            var arbiter = new ScenarioArbiter();
            var devFocus = new DevFocus(arbiter, core,
                () => Settings.Load("DevModeOn", true),
                gameMode.IsProcessWhitelisted);
            gameMode.ActiveChanged += on => arbiter.ReportActivity(ScenarioKind.Game, on);
```

**改动 2** — L269 附近（`procNotify.BatchChanged` 内），找到：

```csharp
                buildWatch.NotifyProcessChanges(batch);
```

替换为：

```csharp
                devFocus.NotifyProcessChanges(batch);
```

**改动 3** — L319 附近（退出路径），找到：

```csharp
                buildWatch.Stop();
```

替换为：

```csharp
                devFocus.Stop();
```

**改动 4** — L354 附近（`SessionEnded` 路径），找到：

```csharp
                try { buildWatch.Stop(); } catch { }
```

替换为：

```csharp
                try { devFocus.Stop(); } catch { }
```

**改动 5** — L382 附近，找到：

```csharp
            buildWatch.SessionChanged += key =>
```

替换为：

```csharp
            devFocus.SessionChanged += key =>
```

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 190  PASS 188  FAIL 0  SKIP 2`。

**行为说明（供审查）**：原 `BuildWatch` 在游戏活跃时跳过 `SvcPause` 操作（`isGameActive` 委托）；新架构下游戏活跃时仲裁器不会 Grant DevFocus，互斥语义由仲裁器自然覆盖，行为等价。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: BuildWatch 场景化为 DevFocus——实现 IScenario 接入仲裁器，删除 isGameActive 布尔委托"
```

---

### Task 4: 编译真后台压制（SuppressReason.Build 空位启用）

**Files:**
- Modify: `src/Core/Scenario/DevFocus.cs`（`Grant`/`Suspend` 接入压制 + 两个新方法）
- Test: `tests/SelfTests.DevFocus.cs`（追加纯逻辑与集成测试）
- Modify: `tests/SelfTests.cs`（注册）

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.DevFocus.cs` **类内**追加：

```csharp
        private static void TestDevFocusSuppressionDecision()
        {
            string winRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            int self = Process.GetCurrentProcess().Id;
            int session = Process.GetCurrentProcess().SessionId;
            var noWindows = new HashSet<int>();
            Func<string, string, bool> noWhitelist = (n, p) => false;

            // 普通用户后台进程：应压制
            Eq(true, DevFocus.ShouldSuppressBackground(
                5000, self, "someapp", @"C:\Apps\someapp.exe",
                session, session, 0, noWindows, winRoot, noWhitelist));

            // 前台程序：豁免
            Eq(false, DevFocus.ShouldSuppressBackground(
                5000, self, "someapp", @"C:\Apps\someapp.exe",
                session, session, 5000, noWindows, winRoot, noWhitelist));

            // 有可见窗口的程序：豁免（常规档不动带窗口程序）
            var visible = new HashSet<int>(); visible.Add(5000);
            Eq(false, DevFocus.ShouldSuppressBackground(
                5000, self, "someapp", @"C:\Apps\someapp.exe",
                session, session, 0, visible, winRoot, noWhitelist));

            // 反作弊进程：豁免（任何强度不动摇）
            Eq(false, DevFocus.ShouldSuppressBackground(
                5001, self, "vgc", @"C:\Riot\vgc.exe",
                session, session, 0, noWindows, winRoot, noWhitelist));

            // 别的登录账户的进程：豁免
            Eq(false, DevFocus.ShouldSuppressBackground(
                5002, self, "someapp", @"C:\Apps\someapp.exe",
                session + 1, session, 0, noWindows, winRoot, noWhitelist));

            // 白名单命中：豁免
            Eq(false, DevFocus.ShouldSuppressBackground(
                5003, self, "mytool", @"C:\Tools\mytool.exe",
                session, session, 0, noWindows, winRoot, (n, p) => true));
        }

        private static void TestDevFocusBuildReasonIsolation()
        {
            string dir = NewTempDir("devfocus-buildbit");
            Process probe = null;
            try
            {
                string beat = Path.Combine(dir, "p.beat");
                probe = StartProbe(beat);
                WaitAdvance(beat, -1, 4000);

                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                try
                {
                    // 同一进程先被游戏位压制、再被编译位压制（引用计数语义）
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Background, null, SuppressionLevel.Eco);
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Build, "devfocus", SuppressionLevel.Eco);
                    Eq(true, core.HasReason(probe.Id, SuppressReason.Background));
                    Eq(true, core.HasReason(probe.Id, SuppressReason.Build));

                    // 按编译位还原：游戏位仍在，进程仍被压制
                    core.ReleaseReason(SuppressReason.Build);
                    Eq(false, core.HasReason(probe.Id, SuppressReason.Build));
                    Eq(true, core.HasReason(probe.Id, SuppressReason.Background));
                    Eq(true, core.IsThrottled(probe.Id));

                    // 按游戏位还原后彻底解除
                    core.ReleaseReason(SuppressReason.Background);
                    Eq(false, core.IsThrottled(probe.Id));
                }
                finally { core.ReleaseReason(SuppressReason.Background | SuppressReason.Build); }
            }
            finally
            {
                if (probe != null) StopOwned(probe);
                DeleteTempDir(dir);
            }
        }
```

在 `tests/SelfTests.cs` 注册（追加在 Task 3 注册块后）：

```csharp
            test("开发专注：压制决策覆盖前台/窗口/反作弊/他账户/白名单豁免", TestDevFocusSuppressionDecision);
            test("开发专注：编译压制位与游戏压制位引用计数隔离", TestDevFocusBuildReasonIsolation);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误 `CS0117: DevFocus 不包含 ShouldSuppressBackground 的定义`。

- [ ] **Step 3: 实现压制逻辑**

在 `src/Core/Scenario/DevFocus.cs` 的 `BoostBuildProcesses` 方法之后插入两个方法：

```csharp
        /// <summary>常规档压制决策（纯逻辑，可单测）：复用游戏模式的常规档豁免计算器，
        /// 再叠加可见窗口与白名单。activeGameRoot/游戏宿主祖先在游戏不活跃时无意义，不传入。</summary>
        internal static bool ShouldSuppressBackground(int pid, int selfPid, string name, string path,
            int session, int ownerSession, int foregroundPid, HashSet<int> visibleWindowPids,
            string windowsRoot, Func<string, string, bool> isWhitelisted)
        {
            bool userFacing = visibleWindowPids != null && visibleWindowPids.Contains(pid);
            if (!GameMode.BasicBackgroundEligible(pid, selfPid, name, path, session, ownerSession,
                foregroundPid, userFacing, windowsRoot)) return false;
            if (isWhitelisted != null && isWhitelisted(name, path)) return false;
            return true;
        }

        /// <summary>全量扫描后台进程并按编译位压制。在 ProcNotify 事件线程同步执行（沿用
        /// BuildWatch 既定模式）；扫描耗时与 SvcPause 同量级，若实测阻塞事件流再改异步+代数校验。</summary>
        private void SweepBuildSuppression()
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

            int suppressed = 0;
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
            foreach (Process p in all)
            {
                try
                {
                    int pid = p.Id;
                    if (pid <= 4 || pid == selfPid) continue;
                    // 编译进程本身是提优对象（HIGH），绝不被后台压制——否则先提后压自相矛盾
                    lock (sync) { if (activeBuildPids.Contains(pid)) continue; }

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

                    if (!ShouldSuppressBackground(pid, selfPid, nm, ipath, session, ownerSession,
                        foregroundPid, visible, windowsRoot, whitelist)) continue;

                    AcquireResult r = core.Acquire(pid, nm, SuppressReason.Build, "devfocus",
                        SuppressionLevel.Eco);
                    if (r == AcquireResult.NewlyThrottled) suppressed++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (suppressed > 0)
                Logger.Log("开发专注：编译期间压制 " + suppressed + " 个后台进程（编译位，退出即还原）");
        }
```

同时修改 `Grant()` 与 `Suspend()` 接入压制：

`Grant()` 找到：

```csharp
            try
            {
                SvcPause.Activate();
                BoostBuildProcesses();
                Logger.Log("开发专注：获得掌职权，已暂停索引服务并提优编译进程");
            }
```

替换为：

```csharp
            try
            {
                SvcPause.Activate();
                BoostBuildProcesses();
                SweepBuildSuppression();
                Logger.Log("开发专注：获得掌职权，已暂停索引服务并提优编译进程");
            }
```

`Suspend()` 找到：

```csharp
            try
            {
                SvcPause.Restore();
                Logger.Log("开发专注：挂起，索引服务已恢复（编译检测继续）");
            }
```

替换为：

```csharp
            try
            {
                if (core != null) core.ReleaseReason(SuppressReason.Build);
                SvcPause.Restore();
                Logger.Log("开发专注：挂起，被压后台与索引服务已恢复（编译检测继续）");
            }
```

在文件顶部 using 区补 `using System.Diagnostics;`（`Process`/`ProcessStartInfo` 需要）：

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
```

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 192  PASS 190  FAIL 0  SKIP 2`。

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/DevFocus.cs tests/
git commit -m "feat: 编译期真后台压制——启用 SuppressReason.Build 空位（常规档+引用计数隔离还原）"
```

---

### Task 5: 全量回归 + 双构建验证

**Files:** 无新增（验证任务）

- [ ] **Step 1: 全量自测**

```bash
cmd.exe //c "dev.cmd test"
```

预期输出末行：`TOTAL 192  PASS 190  FAIL 0  SKIP 2`。FAIL 必须为 0；SKIP 保持 2（机器环境固有，见 memory/build-test-workflow）。报告全文在 `%TEMP%\Caelus.selftest.txt`。

- [ ] **Step 2: 发布构建验证**

```bash
cmd.exe //c "build.cmd"
```

预期：`Build OK -> Caelus.exe`（无测试代码编入发布构建）。

- [ ] **Step 3: WPF 构建回归（确认零影响）**

```bash
cmd.exe //c "build-wpf.cmd"
```

预期：`WPF Build OK -> wpf\bin\Release\CaelusWpf.exe`。WPF 不引用 src/ 源码，应无变化——此步是回归确认。

- [ ] **Step 4: 冒烟验证（手动）**

启动 `Caelus.exe`，触发一次真实编译（任意 `msbuild`/`csc` 调用），确认：
1. 托盘气球"编译优化中"弹出
2. `%AppData%\Caelus\Caelus.log` 出现"开发专注：获得掌职权"与压制计数日志
3. 编译结束后气球"编译结束"且日志出现"挂起/还原"记录

- [ ] **Step 5: Commit（如有遗留改动）**

```bash
git add -A && git commit -m "test: P1 全量回归通过——192 项自测 0 失败"
```

---

## Self-Review 记录

**Spec 覆盖**：P1 范围 = 仲裁器（Task 1）+ GameMode 接线（Task 2）+ DevFocus 场景化（Task 3）+ 编译压制深化（Task 4）。spec 中 P1 的 ScenarioBase 已按"对 spec 的两处细化"说明推迟。专注模式/IDE 优化（P2）、DailyCare（P3）、UI（P4）不在本计划。

**类型一致性**：`ScenarioKind.Game/DevFocus/DailyCare`、`IScenario.Kind/Priority/Grant/Suspend`、`ScenarioArbiter.Register/ReportActivity/CurrentGranted/GrantedChanged/Evaluate`、`DevFocus(arbiter, core, enabled, isWhitelisted)`、`DevFocus.IsActive/IsGranted/NotifyProcessChanges/Grant/Suspend/Stop/ShouldSuppressBackground`、`GameMode.ActiveChanged/IsProcessWhitelisted/SimulateActiveForTest`——全部任务间引用一致。

**已知取舍**：
- 编译会话中途新启动的后台进程不追压（Grant 时全量扫一次；编译会话短，可接受）
- 白名单豁免用规则级匹配（`MatchesNormalized`），家族规则的子进程扩展不在编译场景展开（常规档已豁免带窗口程序，家族增量价值低）
- Sweep 在事件线程同步执行（沿用 BuildWatch 模式；实测阻塞再改异步）
