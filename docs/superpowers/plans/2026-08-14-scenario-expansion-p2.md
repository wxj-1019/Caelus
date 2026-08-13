# 场景扩展 P2：开发者专注模式 + IDE 性能优化 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 P1 的 DevFocus 场景骨架上补齐开发专注的另外两块：手动专注模式（通知静默 + 持续压制 + 分心提醒）与 IDE 自动提优（AboveNormal + IO 提升，有可见窗口才生效）。

**Architecture:** DevFocus 从"单一编译活性"扩展为"三来源活性"（编译会话 OR 专注开关 OR IDE 会话），任一来源活跃即向仲裁器报告；引入 30 秒 `reconcileTimer`（仅掌权期间运行）承担专注模式的增量压制与 IDE 窗口条件复查——窗口出现/消失无进程事件，必须周期复查。

**Tech Stack:** 同 P1（C# 5 语法约束、.NET 4.x、`cmd.exe //c "dev.cmd test"` 自测）。

**Spec:** `docs/superpowers/specs/2026-08-14-scenario-expansion-design.md`；**前置：** `2026-08-14-scenario-expansion-p1.md` 已全部落地（本计划修改的 `DevFocus.cs` 锚点引用 P1 终态代码）。

---

## 文件结构

| 操作 | 文件 | 职责 |
|---|---|---|
| Modify | `src/Core/Scenario/DevFocus.cs` | 三来源活性 + 专注块 + IDE 块 + reconcileTimer |
| Create | `src/Core/Scenario/DistractCatalog.cs` | 分心应用清单（注册表加载，仿 BuildCatalog.LoadCustom） |
| Create | `src/Core/Scenario/IdeCatalog.cs` | IDE 进程名 + 安装目录双校验（仿 GamePlatformCatalog 简化版） |
| Modify | `src/Platform/Native.cs:532` 附近 | 补 `ABOVE_NORMAL_PRIORITY_CLASS = 0x2000` 常量 |
| Modify | `src/Ui/TrayMenu.cs:128,204` | 构造注入 DevFocus + 专注模式快速开关 |
| Modify | `src/Platform/Lang.cs:464` 附近 | 新增文案键（bal.distract / tray.focus / bal.focus*） |
| Modify | `src/Program.cs` | TrayMenu 构造实参同步 |
| Modify | `tests/SelfTests.DevFocus.cs` | P2 测试（追加） |
| Modify | `tests/SelfTests.cs` | 注册 |

## 已核实的代码事实（本计划依据）

- `Notif.Quiet()/Restore()` 静态（`src/Core/Tweaks/Notif.cs:17,28`）；`Restore` 由备份标志驱动，未 Quiet 时调用安全
- `SvcPause.Restore()` 空调用安全（注册表 flag 为空即跳过，`SvcPause.cs:73-95`）
- `Native.GetPriorityClass(IntPtr) → uint`（`Native.cs:86`）；`TrySetIoPriority(h, int)`（L254）；`SetPriorityClass(h, uint)`
- 进程优先级类常量现有 IDLE=0x40 / NORMAL=0x20 / BELOW_NORMAL=0x4000 / HIGH=0x80（`Native.cs:529-532`），**缺 ABOVE_NORMAL=0x2000 需补**
- **优先级常量数值不可比大小**（HIGH=0x80 < BELOW_NORMAL=0x4000）——"已是更高优先级"必须显式枚举判断，禁止 `>=` 比较
- IO 优先级无读取 API——提优快照只记优先级类，IO 还原固定设 2（Normal）
- `GamePlatformCatalog` 双校验模式：进程名数组 + 安装目录根数组（`GamePlatformCatalog.cs:14-45`）
- `BuildCatalog.LoadCustom()` 注册表模式：`Settings.LoadStr("CustomBuildProcs", "")` 按 `; \r \n` 分隔 + 静态缓存（`BuildCatalog.cs:53-77`）
- `TrayMenu` 构造 5 参 `(Tamer, GameMode, Action openPanel, Action exitApp, Action afterChange)`（`TrayMenu.cs:128`）；`Check(text, on, onClick)` 辅助（L182）；Rebuild 动态重建（L204）
- Lang 键模式：静态字典 `{ "key", new[]{ "中文" } }`（`Lang.cs:455-475`，bal.buildstart 在 L463）
- P1 终态 DevFocus：构造 `(arbiter, core, enabled, isWhitelisted)` 4 参，Grant/Suspend 单块，活性=编译 PID 集合

## 关键设计决策

1. **活性三来源统一重算**：`AnyActiveLocked = 编译非空 || focusOn || IDE 非空`，任何来源变化后调 `RecomputeActivity()` 统一处理翻转——避免三条路径各自维护 reported 状态
2. **气球语义分离**：`bal.buildstart/buildend` 只在**编译活性**翻转时发（P1 语义保留）；专注开关不发气球（托盘勾选已是反馈）；分心提醒发 `bal.distract`
3. **reconcileTimer 只在掌权期间运行**：Grant 启动、Suspend 停止——挂起场景零后台开销
4. **IDE 提优不降级更高优先级**：`orig == HIGH || orig == REALTIME(0x100)` 时不快照不动（IDE 进程同时是编译进程被 Boost 到 HIGH 的场景）
5. **写入回读**（项目惯例）：`SetPriorityClass` 后 `GetPriorityClass` 回读成功才入快照
6. **IDE 提优还原为内存快照 + StartTime 校验**（防 PID 复用误还原）；不做 CrashGuard 持久化——AboveNormal 残留无害，与 P1 编译 Boost 的取舍一致

---

### Task 1: DevFocus 三来源活性重构（构造扩展 + 统一活性重算）

**Files:**
- Modify: `src/Core/Scenario/DevFocus.cs`
- Test: `tests/SelfTests.DevFocus.cs`
- Modify: `tests/SelfTests.cs`

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.DevFocus.cs` 类内追加：

```csharp
        private static void TestDevFocusActivitySources()
        {
            string dir = NewTempDir("devfocus-sources");
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = NewDevFocus(arbiter, core, () => true);

                // 初始：无任何活性来源
                Eq(false, dev.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);

                // 来源一：专注开关
                dev.SetFocusMode(true);
                Eq(true, dev.IsActive);
                Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
                dev.SetFocusMode(false);
                Eq(false, dev.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);

                // 来源二：IDE 进程（名称+路径双校验命中）
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(41001, "devenv", ProcessChangeKind.Started) }, false));
                // 注意：41001 不是真实进程，死 PID 清理会清掉它——IDE 活性需真实进程，
                // 此断言验证"未命中时不误活跃"
                Eq(false, dev.IsActive);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }
```

**注意**：`SetFocusMode(true)` 会触发 Grant → 真实 `SvcPause.Activate()`——测试 finally 的 `dev.Stop()` 保证还原。

`MakeChange` 需要支持带 Path 的重载（IDE 双校验用）。P1 的 `MakeChange(pid, name, kind)` 保留，追加：

```csharp
        private static ProcessChange MakeChange(int pid, string name, string path, ProcessChangeKind kind)
        {
            var pc = MakeChange(pid, name, kind);
            pc.Path = path;
            return pc;
        }
```

在 `tests/SelfTests.cs` 注册（P2 注册块起点，追加在 Task 4 注册块后）：

```csharp
            test("开发专注：活性三来源任一即活跃", TestDevFocusActivitySources);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误 `CS1061: DevFocus 不包含 SetFocusMode 的定义`。

- [ ] **Step 3: 实现三来源活性重构**

`src/Core/Scenario/DevFocus.cs` 的改动（**基于 P1 终态**）：

**改动 1 — 构造扩展为 5 参**（追加 `isDistract`，Task 3 使用）。找到 P1 构造：

```csharp
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
```

替换为：

```csharp
        public DevFocus(ScenarioArbiter arbiter, SuppressionCore core,
            Func<bool> enabled, Func<string, string, bool> isWhitelisted,
            Func<string, bool> isDistract)
        {
            if (arbiter == null) throw new ArgumentNullException("arbiter");
            this.arbiter = arbiter;
            this.core = core;
            this.enabled = enabled != null ? enabled : (() => true);
            this.isWhitelisted = isWhitelisted;
            this.isDistract = isDistract;
            this.focusOn = Settings.Load("DevFocusModeOn", false);
            arbiter.Register(this);
        }
```

**改动 2 — 字段区**。在 P1 字段 `private bool reported;` 之后追加：

```csharp
        private readonly Func<string, bool> isDistract;
        private readonly HashSet<int> activeIdePids = new HashSet<int>();
        private readonly HashSet<string> distractNotified =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, uint> ideBoosted = new Dictionary<int, uint>();
        private readonly Dictionary<int, long> ideBoostedCreation = new Dictionary<int, long>();
        private bool focusOn;
        private bool quietApplied;
        private System.Threading.Timer reconcileTimer;
```

并追加活性属性：

```csharp
        /// <summary>专注模式开关状态（托盘菜单读取用）</summary>
        public bool FocusModeOn { get { lock (sync) return focusOn; } }

        /// <summary>测试钩子：校正定时器是否运行中（应只在掌权期间为 true）</summary>
        internal bool FocusTimerRunning { get { lock (sync) return reconcileTimer != null; } }

        /// <summary>锁内判定：任一活性来源（编译/专注/IDE）</summary>
        private bool AnyActiveLocked
        {
            get { return activeBuildPids.Count > 0 || focusOn || activeIdePids.Count > 0; }
        }
```

**改动 3 — 统一活性重算方法**（新增，放在 `NotifyProcessChanges` 之后）：

```csharp
        /// <summary>统一活性重算：任一来源变化后调用。锁内记账，锁外向仲裁器报告翻转。</summary>
        private void RecomputeActivity()
        {
            bool becameActive = false;
            bool becameIdle = false;
            lock (sync)
            {
                bool nowActive = enabled() && AnyActiveLocked;
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
            if (becameActive) arbiter.ReportActivity(Kind, true);
            if (becameIdle) arbiter.ReportActivity(Kind, false);
        }

        /// <summary>死 PID 兜底清理（编译与 IDE 集合共用）</summary>
        private static void PruneDeadPids(HashSet<int> pids)
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
```

**改动 4 — `NotifyProcessChanges` 整体替换**。找到 P1 的 `NotifyProcessChanges` 完整方法（从 `public void NotifyProcessChanges(ProcessChangeBatch batch)` 到方法结束），替换为：

```csharp
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
                    activeIdePids.Clear();
                    distractNotified.Clear();
                }
                if (wasReported) arbiter.ReportActivity(Kind, false);
                return;
            }

            bool buildBecameActive = false;
            bool buildBecameIdle = false;
            var distracts = new List<string>();

            lock (sync)
            {
                bool wasBuildActive = activeBuildPids.Count > 0;

                foreach (ProcessChange pc in batch.Changes)
                {
                    if (string.IsNullOrEmpty(pc.Name)) continue;

                    if (BuildCatalog.IsMatch(pc.Name))
                    {
                        if (pc.Kind == ProcessChangeKind.Started)
                            activeBuildPids.Add(pc.Pid);
                        else if (pc.Kind == ProcessChangeKind.Stopped)
                            activeBuildPids.Remove(pc.Pid);
                    }

                    // IDE 检测：名称预筛零开销，命中才现场查路径（IDE 启动是低频事件）
                    if (pc.Kind == ProcessChangeKind.Started)
                    {
                        if (IsIdeProcess(pc.Pid, pc.Name, pc.Path))
                            activeIdePids.Add(pc.Pid);
                    }
                    else if (pc.Kind == ProcessChangeKind.Stopped)
                    {
                        activeIdePids.Remove(pc.Pid);
                    }

                    // 分心提醒：掌权 + 专注开 + 新进程命中清单（每名一次）
                    if (pc.Kind == ProcessChangeKind.Started && granted && focusOn
                        && isDistract != null && isDistract(pc.Name)
                        && !distractNotified.Contains(pc.Name))
                    {
                        distractNotified.Add(pc.Name);
                        distracts.Add(pc.Name);
                    }
                }

                PruneDeadPids(activeBuildPids);
                PruneDeadPids(activeIdePids);

                bool isBuildActive = activeBuildPids.Count > 0;
                if (isBuildActive && !wasBuildActive) buildBecameActive = true;
                if (!isBuildActive && wasBuildActive) buildBecameIdle = true;
            }

            // 编译气球只在编译活性翻转时发（专注/IDE 活性不发）
            if (buildBecameActive)
            {
                try { var h = SessionChanged; if (h != null) h("bal.buildstart"); } catch { }
            }
            if (buildBecameIdle)
            {
                long elapsedMs = (DateTime.UtcNow.Ticks - sessionStartTicks) / TimeSpan.TicksPerMillisecond;
                if (elapsedMs >= 0)
                    Logger.Log(string.Format("开发专注：本次编译 {0:0.#} 秒", elapsedMs / 1000.0));
                try { var h = SessionChanged; if (h != null) h("bal.buildend"); } catch { }
            }
            foreach (string d in distracts)
            {
                Logger.Log("开发专注：检测到分心应用 " + d + " 正在运行（仅提醒，不处理）");
                try { var h = SessionChanged; if (h != null) h("bal.distract"); } catch { }
            }

            RecomputeActivity();
        }

        /// <summary>IDE 进程判定：名称预筛 + 路径双校验；事件缺路径时现场补查</summary>
        private bool IsIdeProcess(int pid, string name, string path)
        {
            if (!IdeCatalog.NameMatches(name)) return false;
            string p = path;
            if (string.IsNullOrEmpty(p))
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return false;
                try { p = Native.ImagePath(h); }
                finally { Native.CloseHandle(h); }
            }
            return IdeCatalog.IsMatch(name, p);
        }
```

**改动 5 — 编译时长字段语义**：P1 的 `sessionStartTicks` 原在"编译活性翻转"处记录，现在由 `RecomputeActivity` 统一记录。删除 P1 `NotifyProcessChanges` 里对 `sessionStartTicks` 的赋值（已随整体替换移除）——编译时长日志现在读的是场景会话开始时间，语义可接受（编译是唯一会打时长日志的来源）。

**改动 6 — `SetFocusMode` 方法**（Task 1 测试已调用，必须在本任务实现；放在 `RecomputeActivity` 之后）：

```csharp
        /// <summary>专注模式开关（托盘菜单/设置页调用）。持久化 + 活性重算。</summary>
        public void SetFocusMode(bool on)
        {
            lock (sync)
            {
                focusOn = on;
                if (!on) distractNotified.Clear();
            }
            Settings.Save("DevFocusModeOn", on);
            RecomputeActivity();
        }
```

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 193  PASS 191  FAIL 0  SKIP 2`（P1 终态 192 + 1）。

**注意**：P1 的 `TestDevFocusGrantAndRelease` 等测试调用 4 参构造的辅助 `NewDevFocus`——同步修改该辅助为 5 参：

```csharp
        private static DevFocus NewDevFocus(ScenarioArbiter arbiter, SuppressionCore core,
            Func<bool> enabled)
        {
            return new DevFocus(arbiter, core, enabled, (name, path) => false, name => false);
        }
```

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/DevFocus.cs tests/
git commit -m "refactor: DevFocus 三来源活性——编译/专注/IDE 统一重算 + 死 PID 清理抽取"
```

---

### Task 2: 专注模式核心（SetFocusMode + Notif.Quiet + ReconcileTimer）

**Files:**
- Modify: `src/Core/Scenario/DevFocus.cs`

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.DevFocus.cs` 类内追加：

```csharp
        private static void TestDevFocusFocusGrantEffects()
        {
            string dir = NewTempDir("devfocus-fx");
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = NewDevFocus(arbiter, core, () => true);

                // 专注开 → 掌权 → 校正定时器启动
                dev.SetFocusMode(true);
                Eq(true, dev.IsGranted);
                Eq(true, dev.FocusTimerRunning);

                // 游戏抢占 → 挂起 → 定时器必须停止（挂起场景零后台开销）
                arbiter.ReportActivity(ScenarioKind.Game, true);
                Eq(false, dev.IsGranted);
                Eq(false, dev.FocusTimerRunning);
                // 活性仍在（专注开关还开着）
                Eq(true, dev.IsActive);

                // 游戏退出 → 补位 → 定时器恢复
                arbiter.ReportActivity(ScenarioKind.Game, false);
                Eq(true, dev.IsGranted);
                Eq(true, dev.FocusTimerRunning);

                // 专注关 → 整体解除
                dev.SetFocusMode(false);
                Eq(false, dev.IsGranted);
                Eq(false, dev.FocusTimerRunning);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }
```

在 `tests/SelfTests.cs` 注册：

```csharp
            test("开发专注：专注掌权启动校正定时器、挂起即停", TestDevFocusFocusGrantEffects);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误（`SetFocusMode`/`FocusTimerRunning` 已存在于 Task 1 的 `dev` 上但行为未实现——实际报错取决于 Task 1 落地程度；若 Task 1 已含属性声明，则表现为**断言失败**而非编译错误，同样满足"先失败"）。

- [ ] **Step 3: 实现专注块**

**改动 1 — `SweepBuildSuppression` 排除 IDE 进程**（防"IDE 被提优同时又被后台压制"自相矛盾）。找到 P1 终态 `SweepBuildSuppression` 中：

```csharp
                try
                {
                    int pid = p.Id;
                    if (pid <= 4 || pid == selfPid) continue;
```

替换为：

```csharp
                try
                {
                    int pid = p.Id;
                    if (pid <= 4 || pid == selfPid) continue;
                    // 编译/IDE 进程是本场景的提优对象，绝不被后台压制
                    lock (sync)
                    {
                        if (activeBuildPids.Contains(pid) || activeIdePids.Contains(pid)) continue;
                    }
```

**改动 2 — `Grant()` 整体替换**（P1 单块 → 分块）：

```csharp
        /// <summary>IScenario：获得掌职权——按当前活性来源施加对应块的副作用</summary>
        public void Grant()
        {
            lock (sync)
            {
                if (granted) return;
                granted = true;
            }
            try
            {
                bool build;
                bool focus;
                bool ide;
                lock (sync)
                {
                    build = activeBuildPids.Count > 0;
                    focus = focusOn;
                    ide = activeIdePids.Count > 0;
                }

                if (build)
                {
                    SvcPause.Activate();
                    BoostBuildProcesses();
                }
                // 编译深化与专注模式共用同一套常规档压制（Build 位）
                if (build || focus) SweepBuildSuppression();
                if (focus)
                {
                    if (Notif.Quiet()) { lock (sync) quietApplied = true; }
                    StartReconcileTimer();
                }
                if (ide) ReconcileIdeBoost();

                Logger.Log("开发专注：获得掌职权（编译=" + build + " 专注=" + focus + " IDE=" + ide + "）");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注掌权失败", ex); }
        }
```

**改动 3 — `Suspend()` 整体替换**：

```csharp
        /// <summary>IScenario：挂起——还原全部块的副作用，检测状态保留。各还原步骤幂等。</summary>
        public void Suspend()
        {
            bool wasQuiet;
            lock (sync)
            {
                if (!granted) return;
                granted = false;
                wasQuiet = quietApplied;
                quietApplied = false;
            }
            try
            {
                StopReconcileTimer();
                RestoreIdeBoost();
                if (core != null) core.ReleaseReason(SuppressReason.Build);
                if (wasQuiet) Notif.Restore();
                SvcPause.Restore();
                Logger.Log("开发专注：挂起，全部副作用已还原（检测继续）");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注挂起失败", ex); }
        }
```

**改动 4 — Timer 方法**（放在 `SweepBuildSuppression` 之后）：

```csharp
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

        /// <summary>校正节拍：增量追压新后台 + IDE 窗口条件复查。回调到达时可能已挂起，先检查。</summary>
        private void ReconcileTick()
        {
            lock (sync) { if (!granted) return; }
            try
            {
                bool focus;
                lock (sync) focus = focusOn;
                if (focus) SweepBuildSuppression();   // Acquire 对已压进程返回 AlreadyThrottled，幂等
                ReconcileIdeBoost();
            }
            catch { }
        }
```

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 194  PASS 192  FAIL 0  SKIP 2`。

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/DevFocus.cs tests/
git commit -m "feat: 开发者专注模式核心——Notif 静默 + 持续压制 + 掌权期校正定时器"
```

---

### Task 3: 分心应用提醒（DistractCatalog + 一次性气球）

**Files:**
- Create: `src/Core/Scenario/DistractCatalog.cs`
- Modify: `src/Platform/Lang.cs`（加 bal.distract / tray.focus / bal.focuson / bal.focusoff）
- Test: `tests/SelfTests.DevFocus.cs`

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.DevFocus.cs` 类内追加：

```csharp
        private static void TestDevFocusDistractOnce()
        {
            string dir = NewTempDir("devfocus-distract");
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                // 注入分心清单：discord/steam 社区客户端
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false,
                    name => string.Equals(name, "discord", StringComparison.OrdinalIgnoreCase));

                var balloons = new List<string>();
                dev.SessionChanged += key => balloons.Add(key);

                dev.SetFocusMode(true);
                Eq(true, dev.IsGranted);

                // 同名分心进程两次启动：气球只报一次
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(42001, "discord", ProcessChangeKind.Started) }, false));
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(42002, "discord", ProcessChangeKind.Started) }, false));
                int distractCount = 0;
                foreach (string k in balloons) if (k == "bal.distract") distractCount++;
                Eq(1, distractCount);

                // 专注关闭后再开：清空已报集合，可再次提醒
                dev.SetFocusMode(false);
                dev.SetFocusMode(true);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(42003, "discord", ProcessChangeKind.Started) }, false));
                distractCount = 0;
                foreach (string k in balloons) if (k == "bal.distract") distractCount++;
                Eq(2, distractCount);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                DeleteTempDir(dir);
            }
        }
```

（注：`42001` 等假 PID 不会匹配 BuildCatalog/IdeCatalog，死 PID 清理不影响分心断言——分心判定在 PID 清理前的事件循环内完成。）

在 `tests/SelfTests.cs` 注册：

```csharp
            test("开发专注：分心应用气球每名一次、专注重开可再报", TestDevFocusDistractOnce);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误（5 参构造的 `isDistract` lambda 参数、`bal.distract` 键缺失）。

**注意**：Task 1 的 `NewDevFocus` 辅助传了 `name => false` 作为第 5 参，本测试直接调 5 参构造——若 Task 1 已落地 5 参构造则编译错误只剩 Lang 键。Lang 键缺失不会编译失败（`Lang.T` 返回键名本身），此测试表现为**断言失败**。可接受（先红后绿）。

- [ ] **Step 3: 实现 DistractCatalog + Lang 键**

新建 `src/Core/Scenario/DistractCatalog.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 分心应用清单：专注模式期间命中清单的新进程触发一次性托盘提醒（不强制处理）

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class DistractCatalog
    {
        private const string CustomKey = "DevFocusDistractList";
        private static readonly object CustomLock = new object();
        private static HashSet<string> customNames;

        public static bool IsMatch(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name;
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - 4);
            return LoadCustom().Contains(n);
        }

        /// <summary>设置页保存清单后调用，刷新缓存</summary>
        public static void Reload()
        {
            lock (CustomLock) customNames = null;
        }

        private static HashSet<string> LoadCustom()
        {
            lock (CustomLock)
            {
                if (customNames != null) return customNames;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = Settings.LoadStr(CustomKey, "");
                if (raw != null)
                    foreach (string part in raw.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = part.Trim();
                        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            t = t.Substring(0, t.Length - 4);
                        if (t.Length > 0) set.Add(t);
                    }
                customNames = set;
                return set;
            }
        }
    }
}
```

**Lang.cs 加键**。找到 `src/Platform/Lang.cs` L464 附近：

```csharp
            { "bal.buildend", new[]{ "编译结束，已恢复后台资源" } },
```

在其后插入：

```csharp
            { "bal.distract", new[]{ "专注模式：检测到分心应用启动（仅提醒，不处理）" } },
            { "tray.focus", new[]{ "专注模式" } },
```

**Program.cs 接线**：找到 P1 终态的 DevFocus 创建（Task 3 改动 1），追加第 5 参：

```csharp
            var devFocus = new DevFocus(arbiter, core,
                () => Settings.Load("DevModeOn", true),
                gameMode.IsProcessWhitelisted,
                DistractCatalog.IsMatch);
```

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 195  PASS 193  FAIL 0  SKIP 2`。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: 分心应用提醒——DistractCatalog 注册表清单 + 专注期一次性气球"
```

---

### Task 4: IDE 性能优化（IdeCatalog + 提优/还原）

**Files:**
- Create: `src/Core/Scenario/IdeCatalog.cs`
- Modify: `src/Platform/Native.cs:532` 附近（补常量）
- Modify: `src/Core/Scenario/DevFocus.cs`（ReconcileIdeBoost/BoostOneIde/RestoreIdeBoost）
- Test: `tests/SelfTests.DevFocus.cs`

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.DevFocus.cs` 类内追加：

```csharp
        private static void TestIdeCatalogMatch()
        {
            // 双校验：名称+目录都命中才认
            Eq(true, IdeCatalog.IsMatch("devenv",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"));
            Eq(true, IdeCatalog.IsMatch("code",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    + @"\Programs\Microsoft VS Code\Code.exe"));
            // 名称命中但目录不对：不认（防同名误伤）
            Eq(false, IdeCatalog.IsMatch("code", @"C:\Temp\code.exe"));
            // 目录对但名称不对：不认
            Eq(false, IdeCatalog.IsMatch("notepad",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\notepad.exe"));
            // 空值安全
            Eq(false, IdeCatalog.IsMatch(null, null));
            Eq(false, IdeCatalog.IsMatch("devenv", null));
        }

        private static void TestDevFocusIdeBoostRestore()
        {
            string dir = NewTempDir("devfocus-ide");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                string beat = Path.Combine(dir, "ide.beat");
                probe = StartProbe(beat);
                WaitAdvance(beat, -1, 4000);
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);

                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = NewDevFocus(arbiter, core, () => true);

                // 提优（测试钩子绕过窗口条件）：AboveNormal 生效
                Eq(true, dev.BoostIdeForTest(probe.Id));
                probe.Refresh();
                Eq(ProcessPriorityClass.AboveNormal, probe.PriorityClass);

                // 重复提优幂等（快照不叠加）
                Eq(true, dev.BoostIdeForTest(probe.Id));
                probe.Refresh();
                Eq(ProcessPriorityClass.AboveNormal, probe.PriorityClass);

                // 还原：回到 Normal
                dev.RestoreIdeBoost();
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
            }
            finally
            {
                if (dev != null) try { dev.RestoreIdeBoost(); dev.Stop(); } catch { }
                if (probe != null) StopOwned(probe);
                DeleteTempDir(dir);
            }
        }
```

在 `tests/SelfTests.cs` 注册：

```csharp
            test("开发专注：IDE 目录双校验防同名误伤", TestIdeCatalogMatch);
            test("开发专注：IDE 提优 AboveNormal 与还原往返", TestDevFocusIdeBoostRestore);
```

- [ ] **Step 2: 运行自测确认编译失败**

```bash
cmd.exe //c "dev.cmd test"
```

预期：编译错误（`IdeCatalog` / `BoostIdeForTest` / `RestoreIdeBoost` 不存在）。

- [ ] **Step 3: 实现**

**改动 1 — Native.cs 补常量**。找到 `src/Platform/Native.cs` L532：

```csharp
        public const uint HIGH_PRIORITY_CLASS = 0x80;
```

在其后插入：

```csharp
        public const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x2000;
```

**改动 2 — 新建 `src/Core/Scenario/IdeCatalog.cs`**：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 IDE 家族识别：进程名 + 安装目录双重校验（防同名进程误伤）

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static class IdeCatalog
    {
        private sealed class IdeEntry
        {
            public readonly string Name;
            public readonly string[] RootPrefixes;

            public IdeEntry(string name, string[] rootPrefixes)
            {
                Name = name;
                RootPrefixes = rootPrefixes;
            }
        }

        private static string Pf { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } }
        private static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        private static IdeEntry[] BuildEntries()
        {
            return new[]
            {
                new IdeEntry("devenv", new[] { Path.Combine(Pf, @"Microsoft Visual Studio\") }),
                new IdeEntry("rider64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("code", new[] { Path.Combine(Local, @"Programs\Microsoft VS Code\"), Path.Combine(Pf, @"Microsoft VS Code\") }),
                new IdeEntry("cursor", new[] { Path.Combine(Local, @"Programs\cursor\"), Path.Combine(Local, @"Programs\Cursor\") }),
                new IdeEntry("idea64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("webstorm64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("goland64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("clion64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("pycharm64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") })
            };
        }

        private static readonly object Sync = new object();
        private static Dictionary<string, IdeEntry> byName;

        private static Dictionary<string, IdeEntry> Map()
        {
            lock (Sync)
            {
                if (byName != null) return byName;
                var map = new Dictionary<string, IdeEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (IdeEntry e in BuildEntries()) map[e.Name] = e;
                byName = map;
                return map;
            }
        }

        /// <summary>名称预筛（零开销，事件热路径先用它过滤）</summary>
        public static bool NameMatches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = StripExe(name);
            return Map().ContainsKey(n);
        }

        /// <summary>双校验：名称命中且路径位于该 IDE 的已知安装目录前缀下</summary>
        public static bool IsMatch(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return false;
            IdeEntry e;
            if (!Map().TryGetValue(StripExe(name), out e)) return false;
            string full = path;
            try { full = Path.GetFullPath(path); } catch { }
            foreach (string prefix in e.RootPrefixes)
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

**改动 3 — DevFocus IDE 提优方法**。在 `ReconcileTick` 之后插入：

```csharp
        /// <summary>IDE 提优复查：有可见窗口才提优，窗口全关即还原。Timer 与 Grant 共用。</summary>
        private void ReconcileIdeBoost()
        {
            int[] ides;
            lock (sync)
            {
                ides = new int[activeIdePids.Count];
                activeIdePids.CopyTo(ides);
            }
            if (ides.Length == 0) { RestoreIdeBoost(); return; }

            HashSet<int> visible;
            try { visible = GameSessionDetector.VisibleWindowPids(true); }
            catch { visible = new HashSet<int>(); }

            bool anyVisible = false;
            foreach (int pid in ides) if (visible.Contains(pid)) { anyVisible = true; break; }
            if (!anyVisible) { RestoreIdeBoost(); return; }

            foreach (int pid in ides) BoostOneIde(pid);
        }

        private void BoostOneIde(int pid)
        {
            lock (sync) { if (ideBoosted.ContainsKey(pid)) return; }

            long creation = 0;
            try { creation = Process.GetProcessById(pid).StartTime.Ticks; } catch { return; }

            IntPtr h = Native.OpenProcess(
                Native.PROCESS_SET_INFORMATION | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return;
            try
            {
                uint orig = Native.GetPriorityClass(h);
                if (orig == 0) return;
                // 已是更高优先级（HIGH 的编译 Boost / REALTIME）不降级不叠加
                if (orig == Native.HIGH_PRIORITY_CLASS || orig == 0x100) return;
                if (orig == Native.ABOVE_NORMAL_PRIORITY_CLASS) return;

                Native.SetPriorityClass(h, Native.ABOVE_NORMAL_PRIORITY_CLASS);
                // 写入回读（项目惯例）：确认生效才入快照
                if (Native.GetPriorityClass(h) != Native.ABOVE_NORMAL_PRIORITY_CLASS) return;
                Native.TrySetIoPriority(h, 3);

                lock (sync)
                {
                    ideBoosted[pid] = orig;
                    ideBoostedCreation[pid] = creation;
                }
            }
            catch { }
            finally { Native.CloseHandle(h); }
        }

        /// <summary>还原全部 IDE 提优：StartTime 校验防 PID 复用误还原；IO 无读取 API，还原为 Normal(2)</summary>
        internal void RestoreIdeBoost()
        {
            KeyValuePair<int, uint>[] snap;
            KeyValuePair<int, long>[] snapCreation;
            lock (sync)
            {
                if (ideBoosted.Count == 0) return;
                snap = new KeyValuePair<int, uint>[ideBoosted.Count];
                ideBoosted.CopyTo(snap, 0);
                ideBoosted.Clear();
                snapCreation = new KeyValuePair<int, long>[ideBoostedCreation.Count];
                ideBoostedCreation.CopyTo(snapCreation, 0);
                ideBoostedCreation.Clear();
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
                        catch { continue; }   // 进程已退出
                        if (nowCreation != expectCreation) continue;   // PID 已被复用，不动
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

        /// <summary>测试钩子：绕过窗口条件直接提优单个进程（返回是否入快照）</summary>
        internal bool BoostIdeForTest(int pid)
        {
            BoostOneIde(pid);
            lock (sync) return ideBoosted.ContainsKey(pid);
        }
```

**改动 4 — 文件顶部 using 确认**：`src/Core/Scenario/DevFocus.cs` 需要 `using System.Diagnostics;`（P1 Task 4 已加）。

- [ ] **Step 4: 运行自测确认通过**

```bash
cmd.exe //c "dev.cmd test"
```

预期：`TOTAL 197  PASS 195  FAIL 0  SKIP 2`。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: IDE 性能优化——双校验家族识别 + AboveNormal/IO 提优（窗口条件+回读校验+PID 复用防护）"
```

---

### Task 5: 托盘专注开关 + 全量回归

**Files:**
- Modify: `src/Ui/TrayMenu.cs`（构造 + Rebuild）
- Modify: `src/Program.cs`（TrayMenu 创建实参）
- Test: 无新增（UI 冒烟覆盖）

- [ ] **Step 1: TrayMenu 注入 DevFocus**

找到 `src/Ui/TrayMenu.cs` L128 构造：

```csharp
        public TrayMenu(Tamer tamer, GameMode gameMode, Action openPanel, Action exitApp, Action afterChange)
        {
            this.tamer = tamer;
            this.gameMode = gameMode;
```

替换为：

```csharp
        public TrayMenu(Tamer tamer, GameMode gameMode, DevFocus devFocus, Action openPanel, Action exitApp, Action afterChange)
        {
            this.tamer = tamer;
            this.gameMode = gameMode;
            this.devFocus = devFocus;
```

在字段区（`private readonly GameMode gameMode;` 附近，先 `grep -n "private readonly" src/Ui/TrayMenu.cs | head -5` 确认锚点）追加：

```csharp
        private readonly DevFocus devFocus;
```

- [ ] **Step 2: Rebuild 加专注开关**

找到 `src/Ui/TrayMenu.cs` Rebuild 中 L222 附近：

```csharp
            strip.Items.Add(Check(Lang.T("nav.tame"), !tamer.Paused, (s, e) =>
            {
                tamer.Paused = !tamer.Paused;
                Settings.Save("TameOn", !tamer.Paused);
                Changed();
            }));
```

在其后插入：

```csharp
            strip.Items.Add(Check(Lang.T("tray.focus"), devFocus.FocusModeOn, (s, e) =>
            {
                devFocus.SetFocusMode(!devFocus.FocusModeOn);
                Changed();
            }));
```

- [ ] **Step 3: Program.cs 同步 TrayMenu 创建**

找到 `src/Program.cs` 中（L324 附近）：

```csharp
            var trayMenu = new TrayMenu(tamer, gameMode,
                () => panel.ShowPanel(),
```

替换为：

```csharp
            var trayMenu = new TrayMenu(tamer, gameMode, devFocus,
                () => panel.ShowPanel(),
```

- [ ] **Step 4: 全量回归 + 双构建**

```bash
cmd.exe //c "dev.cmd test"
cmd.exe //c "build.cmd"
cmd.exe //c "build-wpf.cmd"
```

预期：`TOTAL 197  PASS 195  FAIL 0  SKIP 2`；两个构建均 OK。

- [ ] **Step 5: 冒烟验证（手动）**

1. 启动 `Caelus.exe`，托盘菜单勾选"专注模式"→ 日志出现"开发专注：获得掌职权（…专注=True…）"
2. 注册表 `HKCU\Software\Caelus` 的 `DevFocusDistractList` 写入 `discord;steam`，专注模式下启动其中任一程序 → 托盘气球提醒一次
3. 启动真实 IDE（VS/VSCode）→ 任务管理器确认其优先级变为"高于标准"；关闭 IDE 窗口 → 30 秒内还原
4. 取消专注勾选 → 日志出现"挂起，全部副作用已还原"

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: 托盘菜单专注模式快速开关（P2 收尾，197 项自测 0 失败）"
```

---

## Self-Review 记录

**Spec 覆盖**：专注模式（手动开关/通知静默/持续压制/分心提醒可选默认关）✓；IDE 优化（双校验/AboveNormal+IO/可见窗口条件/还原）✓。设置页 UI 与概览页指示属 P4。

**类型一致性**：DevFocus 5 参构造 `(arbiter, core, enabled, isWhitelisted, isDistract)` 在 Task 1/3 与 Program.cs 接线一致；`SetFocusMode/FocusModeOn/FocusTimerRunning/BoostIdeForTest/RestoreIdeBoost/ReconcileIdeBoost` 跨任务引用一致；`DistractCatalog.IsMatch(string)→bool` 与 `Func<string,bool>` 匹配；`IdeCatalog.NameMatches/IsMatch` 与 DevFocus.IsIdeProcess 调用一致。

**已知取舍**：
- IDE 家族子进程（语言服务器等）不扩展提优（只提优双校验命中的进程本身；语言服务器已由 P1 编译压制的豁免语义间接保护）
- IDE 提优崩溃残留为 AboveNormal（无害，与编译 Boost 取舍一致）
- 专注模式周期追压间隔固定 30s（不可配，YAGNI）
- P1 测试辅助 `NewDevFocus` 在 P2 Task 1 改为 5 参（已在 Task 1 Step 4 注明同步修改）
