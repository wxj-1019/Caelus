# 开发专注：编译活性门 + 副作用收敛器 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 编译活性判定从纯进程名匹配升级为「CPU 进度驻留门」，DevFocus 副作用从 Grant 快照式执行改为掌权全程收敛——空转 MSBuild 节点/通用 CLI 不再误掌权，「先开 IDE 再编译/再开专注」的副作用即时补做与回收。

**Architecture:** 三层：① `BuildCatalog` 默认名录收紧（git/docker 家族移出，CustomList 补回）；② DevFocus 内建活性门（per-pid CPU 进度字典 + 5 秒采样 Timer + 纯函数淘汰判定 + 复活重入）；③ `ReconcileSideEffects()` 收敛器按期望态表（SvcPause=build / Notif=focus / 压制=build||focus / 节拍=granted）在全部状态变化调用点升降沿收敛，Suspend 全量还原语义不变。

**Tech Stack:** C# 5 / .NET 4（无插值字符串、显式 delegate）；自研自测框架（`dev.cmd test` 门禁，`tests/SelfTests.*.cs` partial，`test("中文名", Method)` 注册于 `tests/SelfTests.cs` RunAll，`Eq`/`Skip`/`NewTempDir`/`StartNamedProbe`/`StopOwned` 辅助）；真机探针 = 自测 exe 复制改名进程。

**规格文档:** `docs/superpowers/specs/2026-10-02-devfocus-build-activity-and-reconciler-design.md`（§3 活性门 / §4 收敛器 / §5 测试策略 / §7 验收）

**基线:** TOTAL 295 / PASS 291 / FAIL 0 / SKIP 4（83e5e95）。完成后预期 298。

---

### Task 1: BuildCatalog 名录收紧

**Files:**
- Modify: `src/Core/BuildCatalog.cs:12-32`
- Test: `tests/SelfTests.DevFocus.cs`（扩 `TestBuildCatalogExpandedTools`，约 :659）
- Modify: `src/Platform/Lang.cs:497`（`set.dev.custom` 提示文案点名补回路径）

- [ ] **Step 1: 写失败测试**

在 `tests/SelfTests.DevFocus.cs` 的 `TestBuildCatalogExpandedTools` 内追加断言（保留原有全部断言）：

```csharp
            // 2026-10-02 §3.1：通用 CLI 家族移出默认名录（每条 git status 都算编译会话的误报源）
            Eq(false, BuildCatalog.IsMatch("git"));
            Eq(false, BuildCatalog.IsMatch("git-bash"));
            Eq(false, BuildCatalog.IsMatch("git-cmd"));
            Eq(false, BuildCatalog.IsMatch("docker"));
            Eq(false, BuildCatalog.IsMatch("docker-buildx"));
            // 补回路径：CustomList 加回后恢复命中
            string oldCustom = BuildCatalog.CustomList;
            try
            {
                BuildCatalog.CustomList = "git;docker";
                Eq(true, BuildCatalog.IsMatch("git"));
                Eq(true, BuildCatalog.IsMatch("docker"));
            }
            finally { BuildCatalog.CustomList = oldCustom; }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: `FAIL  开发专注：编译工具名录覆盖扩展工具链`（git/docker 当前在名录，负断言失败）

- [ ] **Step 3: 改名录**

`src/Core/BuildCatalog.cs` 的 `Names` 集合：删除 `// Git 大规模 IO 操作…` 与 `// Docker…` 两段共 5 个名字（`"git", "git-bash", "git-cmd"` 与 `"docker", "docker-buildx"`），原位置替换为一行注释：

```csharp
            // Git/Docker 家族已移出默认名录（2026-10-02 §3.1）：名称匹配无法区分
            // git clone 与 git status，docker CLI 只是守护进程瘦客户端——依赖者经
            // CustomList（设置页「自定义编译进程」）显式补回
```

同文件 `Lang.cs:497` 的 `set.dev.custom` 值改为：

```csharp
            { "set.dev.custom", new[]{ "自定义编译进程（分号分隔；如需 git/docker 触发请在此加入）" } },
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: `TOTAL 295 … FAIL 0`（计数未变，本轮只扩断言）

- [ ] **Step 5: Commit**

```bash
git add src/Core/BuildCatalog.cs src/Platform/Lang.cs tests/SelfTests.DevFocus.cs
git commit -m "fix(dev): 编译名录移出 git/docker 家族——通用 CLI 不再误触发开发专注（CustomList 可补回）"
```

---

### Task 2: 编译活性门（CPU 进度驻留）

**Files:**
- Modify: `src/Core/Scenario/DevFocus.cs`（字段区 :31-49 / `NotifyProcessChanges` :172-341 / `OnInitialProcess` :344-361 / `SetEnabled` :151-170 / `Stop` :798-812）
- Test: `tests/SelfTests.DevFocus.cs`（新增两测试）+ `tests/SelfTests.cs`（注册）

- [ ] **Step 1: 写失败测试（纯函数真值表）**

`tests/SelfTests.DevFocus.cs` 追加：

```csharp
        // —— 编译活性门（规格 2026-10-02 §3.2）——
        private static void TestBuildIdleDropDecision()
        {
            int old = DevFocus.BuildIdleDropSeconds;
            try
            {
                DevFocus.BuildIdleDropSeconds = 20;
                long now = DateTime.UtcNow.Ticks;
                Eq(false, DevFocus.ShouldDropForIdle(0, now));                                      // 未起钟不淘汰
                Eq(false, DevFocus.ShouldDropForIdle(now - 19L * TimeSpan.TicksPerSecond, now));   // 静默 19 秒保留
                Eq(true, DevFocus.ShouldDropForIdle(now - 20L * TimeSpan.TicksPerSecond, now));    // 满 20 秒淘汰
                DevFocus.BuildIdleDropSeconds = 0;
                Eq(false, DevFocus.ShouldDropForIdle(now - 3600L * TimeSpan.TicksPerSecond, now)); // 0=关闭淘汰
            }
            finally { DevFocus.BuildIdleDropSeconds = old; }
        }
```

`tests/SelfTests.cs` RunAll 的 DevFocus 组（`TestBuildCatalogExpandedTools` 注册行附近）追加：

```csharp
            test("编译活性门：静默淘汰判定真值表", TestBuildIdleDropDecision);
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: 编译错误 `DevFocus 未定义 BuildIdleDropSeconds/ShouldDropForIdle`（自测源与产品源同构建，符号缺失即构建失败）——以此确认测试就位。

- [ ] **Step 3: 实现纯函数与钩子字段**

`src/Core/Scenario/DevFocus.cs` 字段区（`buildBoostedIo` 声明之后）追加：

```csharp
        // —— 编译活性门（规格 2026-10-02 §3.2）：CPU 进度驻留，空转常驻节点不维持掌权 ——
        private readonly Dictionary<int, long> buildPidLastProgress = new Dictionary<int, long>(); // pid → 最近 CPU 前进时刻
        private readonly Dictionary<int, long> buildPidCpuMs = new Dictionary<int, long>();       // pid → 上次采样累计 CPU 毫秒
        private readonly Dictionary<int, string> buildPidName = new Dictionary<int, string>();    // pid → 进程名（复活重入的合成事件用）
        private Timer buildSampleTimer;

        /// <summary>编译 pid 静默淘汰阈值秒数（默认 20；0=关闭）。internal static 供自测覆写。</summary>
        internal static int BuildIdleDropSeconds = 20;

        /// <summary>编译 pid 的累计 CPU 读数注入（返回 null=不可读，保守保留）。null=生产实现。</summary>
        internal static Func<int, TimeSpan?> CpuProbe;

        /// <summary>静默淘汰判定（纯逻辑，可单测）：未起钟（&lt;=0）不淘汰；
        /// 静默超过 BuildIdleDropSeconds 秒（0=关闭）淘汰。</summary>
        internal static bool ShouldDropForIdle(long lastProgressTicks, long nowTicks)
        {
            if (lastProgressTicks <= 0) return false;
            if (BuildIdleDropSeconds <= 0) return false;
            return nowTicks - lastProgressTicks >= BuildIdleDropSeconds * TimeSpan.TicksPerSecond;
        }

        /// <summary>读编译 pid 的累计 CPU（可注入）。不可读（提权/跨会话/已退出）返回 null：
        /// 保守保留——不更新进度也不淘汰，防误杀真编译；死进程由 Stopped/CleanDeadPids 走。</summary>
        private static TimeSpan? ReadBuildCpu(int pid)
        {
            Func<int, TimeSpan?> probe = CpuProbe;
            if (probe != null) return probe(pid);
            try
            {
                Process p = Process.GetProcessById(pid);
                try { return p.TotalProcessorTime; }
                finally { p.Dispose(); }
            }
            catch { return null; }
        }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: `TOTAL 296 … FAIL 0`

- [ ] **Step 5: 写集成失败测试（淘汰/重入/统计落点）**

`tests/SelfTests.DevFocus.cs` 追加：

```csharp
        private static void TestBuildIdleDropAndReenter()
        {
            string dir = NewTempDir("devfocus-idle");
            Process probe = null;
            DevFocus dev = null;
            int oldIdle = DevFocus.BuildIdleDropSeconds;
            Func<int, TimeSpan?> oldProbe = DevFocus.CpuProbe;
            string oldN = Settings.LoadStr("FocusStatsBuildN", "");
            string oldSec = Settings.LoadStr("FocusStatsBuildSec", "");
            DevFocus.BuildIdleDropSeconds = 20;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                DevFocus.CpuProbe = pid => TimeSpan.FromMilliseconds(1000);   // 恒定读数 = 无 CPU 前进
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsActive);
                Eq(true, dev.IsGranted);   // 事件入场乐观计活 → 掌权（SvcPause 真实往返，finally 还原）

                // 静默超阈 → 淘汰收权，编译统计落一条
                dev.RemoveIdleBuilds(DateTime.UtcNow.Ticks + 30L * TimeSpan.TicksPerSecond);
                Eq(false, dev.IsActive);
                Eq(false, dev.IsGranted);
                Eq("1", Settings.LoadStr("FocusStatsBuildN", "0"));

                // 复活重入：被淘汰的 pid 复烧 CPU（无 Started 事件，nodeReuse 场景）→ 采样器重入场
                DevFocus.CpuProbe = pid => TimeSpan.FromMilliseconds(5000);
                dev.SampleBuildCpu();
                Eq(true, dev.IsActive);
            }
            finally
            {
                DevFocus.BuildIdleDropSeconds = oldIdle;
                DevFocus.CpuProbe = oldProbe;
                Settings.SaveStr("FocusStatsBuildN", oldN);
                Settings.SaveStr("FocusStatsBuildSec", oldSec);
                try { if (dev != null) dev.Stop(); } catch { }   // Grant 真实停过服务，必须还原
                StopOwned(probe);
                DeleteTempDir(dir);
            }
        }
```

`tests/SelfTests.cs` 追加注册：

```csharp
            test("编译活性门：空转淘汰/复活重入/统计落点", TestBuildIdleDropAndReenter);
```

- [ ] **Step 6: 跑测试确认失败**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: 编译错误 `DevFocus 未定义 RemoveIdleBuilds/SampleBuildCpu`

- [ ] **Step 7: 实现活性门接线**

`src/Core/Scenario/DevFocus.cs` 四处修改：

(a) `NotifyProcessChanges` 的编译匹配分支（原 :209-221）改为：

```csharp
                    // 编译进程匹配
                    if (BuildCatalog.IsMatch(pc.Name))
                    {
                        if (pc.Kind == ProcessChangeKind.Started)
                        {
                            if (activeBuildPids.Add(pc.Pid))
                            {
                                if (newlyBuilt == null) newlyBuilt = new List<int>();
                                newlyBuilt.Add(pc.Pid);
                                EnterBuildPidLocked(pc.Pid, pc.Name, DateTime.UtcNow.Ticks);
                            }
                        }
                        else if (pc.Kind == ProcessChangeKind.Stopped)
                            activeBuildPids.Remove(pc.Pid);   // 记忆字典保留供复活重入
                    }
```

(b) 同方法锁内「空→非空起钟」处（原 :291-292）追加采样器启动：

```csharp
                if (activeBuildPids.Count > 0 && !wasBuildActive)
                {
                    buildStartTicks = DateTime.UtcNow.Ticks;
                    if (buildSampleTimer == null)
                        buildSampleTimer = new Timer(_ => SampleBuildCpu(), null, 5000, 5000);
                }
```

(c) 同方法锁外统计段（原 :297-303 的 `if (buildEndedElapsed > 0)` 块）整块替换为统一收尾调用：

```csharp
            // 编译集合 空←非空 的统一收尾（统计落点 + 停采样 + 仍掌权时收敛副作用降沿）
            if (buildEndedElapsed > 0)
            {
                // 保持原统计行为（buildEndedElapsed 语义不变），收尾细节见 BuildSetBecameEmpty
                try { FocusStats.RecordBuild(buildEndedElapsed, DateTime.Now); } catch { }
                try { Logger.Log(string.Format("开发专注：本次编译 {0:0.#} 秒",
                    buildEndedElapsed / (double)TimeSpan.TicksPerSecond)); } catch { }
            }
            if (buildActivity && activeBuildPids.Count == 0) BuildSetBecameEmpty();
```

（锁内计算 `buildEndedElapsed` 的原逻辑保留不动；`BuildSetBecameEmpty` 负责停采样与收敛，不重复统计——见 (e)。）

(d) `OnInitialProcess`（原 :344-361）改用悲观入场：

```csharp
        protected override void OnInitialProcess(ProcessChange change)
        {
            if (string.IsNullOrEmpty(change.Name)) return;
            if (BuildCatalog.IsMatch(change.Name))
            {
                // 初始扫描加入的编译进程也要起钟：否则它在第一批事件里就结束时，
                // 转换逻辑以 buildStartTicks==0 计出天文数字时长，写爆今日统计。
                // 活性门入场悲观（规格 §3.2）：只给 5 秒宽限证明自己在编译——
                // 开机残留的 nodeReuse 节点最多 5 秒假阳性，真编译延迟 ≤5 秒收权
                lock (sync)
                {
                    if (activeBuildPids.Add(change.Pid))
                    {
                        if (buildStartTicks == 0) buildStartTicks = DateTime.UtcNow.Ticks;
                        EnterBuildPidLocked(change.Pid, change.Name,
                            DateTime.UtcNow.Ticks - 15L * TimeSpan.TicksPerSecond);
                        if (buildSampleTimer == null)
                            buildSampleTimer = new Timer(_ => SampleBuildCpu(), null, 5000, 5000);
                    }
                }
            }
            if (IdeOn && IsIdeProcess(change.Pid, change.Name, change.Path))
            {
                lock (sync) activeIdePids.Add(change.Pid);
            }
        }
```

(e) 新增成员（放在 `CleanDeadPids` 之后）：

```csharp
        // —— 编译活性门内部 ——

        /// <summary>乐观/悲观入场：记录名字与起钟时刻，CPU 基线由下次采样重建。</summary>
        private void EnterBuildPidLocked(int pid, string name, long progressTicks)
        {
            buildPidName[pid] = name ?? "";
            buildPidLastProgress[pid] = progressTicks;
            buildPidCpuMs.Remove(pid);
        }

        /// <summary>活性门淘汰（采样节拍调用；自测可直接传人造时刻）：静默超阈的 pid
        /// 移出活性集合，集合因此 空←非空 时走与 Stopped 相同的收尾。</summary>
        internal void RemoveIdleBuilds(long nowTicks)
        {
            bool wasBuildActive;
            lock (sync)
            {
                wasBuildActive = activeBuildPids.Count > 0;
                if (!wasBuildActive) return;
                var dropped = new List<int>();
                foreach (int pid in activeBuildPids)
                {
                    long last;
                    buildPidLastProgress.TryGetValue(pid, out last);
                    if (ShouldDropForIdle(last, nowTicks)) dropped.Add(pid);
                }
                if (dropped.Count == 0) return;
                foreach (int pid in dropped) activeBuildPids.Remove(pid);
            }
            BuildSetBecameEmpty();
        }

        /// <summary>编译集合 空←非空 的统一收尾：停采样器 + 仍掌权时收敛副作用降沿
        /// （统计落点由调用方的事件路径完成，这里不重复记——两条路径共用保证见规格 §5.2）。</summary>
        private void BuildSetBecameEmpty()
        {
            Timer t;
            lock (sync)
            {
                t = buildSampleTimer;
                buildSampleTimer = null;
            }
            if (t != null) t.Dispose();
            lock (sync) { if (granted) ReconcileSideEffects(); }
        }

        /// <summary>CPU 采样节拍（5 秒）：推进度、清死记忆、复活重入、静默淘汰。</summary>
        internal void SampleBuildCpu()
        {
            int[] active;
            int[] remembered;
            lock (sync)
            {
                active = new int[activeBuildPids.Count];
                activeBuildPids.CopyTo(active);
                remembered = new int[buildPidCpuMs.Count];
                buildPidCpuMs.Keys.CopyTo(remembered, 0);
            }
            long now = DateTime.UtcNow.Ticks;
            var alive = new HashSet<int>();
            var reenter = new List<int>();
            foreach (int pid in remembered)
            {
                TimeSpan? cpu = ReadBuildCpu(pid);
                if (cpu == null) continue;               // 不可读保守保留（不淘汰、不重入、不清记忆）
                alive.Add(pid);
                long ms = (long)cpu.Value.TotalMilliseconds;
                long prev;
                lock (sync) buildPidCpuMs.TryGetValue(pid, out prev);
                if (ms <= prev) continue;
                lock (sync)
                {
                    buildPidCpuMs[pid] = ms;
                    buildPidLastProgress[pid] = now;
                }
                bool inSet = Array.IndexOf(active, pid) >= 0;
                if (!inSet) reenter.Add(pid);            // 被淘汰的 pid 复烧 CPU → 重入场
            }
            // 记忆清死：确认死亡的 pid 才清（不可读 ≠ 死，规格 §3.2）
            lock (sync)
            {
                var dead = new List<int>();
                foreach (int pid in buildPidCpuMs.Keys)
                {
                    if (activeBuildPids.Contains(pid) || alive.Contains(pid)) continue;
                    IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) { dead.Add(pid); continue; }
                    try { if (!Native.StillActive(h)) dead.Add(pid); }
                    finally { Native.CloseHandle(h); }
                }
                foreach (int pid in dead)
                {
                    buildPidCpuMs.Remove(pid);
                    buildPidLastProgress.Remove(pid);
                    buildPidName.Remove(pid);
                }
            }
            if (reenter.Count > 0)
            {
                // 复活重入走事件正路（活性上报/掌权/提优/气球全部复用）
                var changes = new List<ProcessChange>();
                foreach (int pid in reenter)
                {
                    string nm;
                    lock (sync) buildPidName.TryGetValue(pid, out nm);
                    changes.Add(MakeReentryChange(pid, string.IsNullOrEmpty(nm) ? "msbuild" : nm));
                }
                NotifyProcessChanges(new ProcessChangeBatch(changes.ToArray(), false));
            }
            RemoveIdleBuilds(now);
        }

        private static ProcessChange MakeReentryChange(int pid, string name)
        {
            var pc = new ProcessChange();
            pc.Pid = pid;
            pc.Name = name;
            pc.Kind = ProcessChangeKind.Started;
            return pc;
        }
```

(f) `SetEnabled(false)` 与 `Stop()` 与 `NotifyProcessChanges` 的 disabled 分支三处清集合处，同步清活性门状态（在 `activeBuildPids.Clear()` 行后追加）：

```csharp
                    buildPidLastProgress.Clear();
                    buildPidCpuMs.Clear();
                    buildPidName.Clear();
```

并在 `SetEnabled(false)`/`Stop()` 的 Clear 之后（锁外或收集后）停采样器——复用 `BuildSetBecameEmpty` 的停法太重（会触发收敛器），直接：

```csharp
                Timer st;
                lock (sync) { st = buildSampleTimer; buildSampleTimer = null; }
                if (st != null) st.Dispose();
```

- [ ] **Step 8: 跑测试确认通过**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: `TOTAL 297 … FAIL 0`（注意：既有探针测试可能因活性门淘汰心跳探针而 FAIL——若出现，先做 Task 4 的插桩再回到本步；正常顺序应无 FAIL，因为心跳探针 20 秒内测试已完成）

- [ ] **Step 9: Commit**

```bash
git add src/Core/Scenario/DevFocus.cs tests/SelfTests.DevFocus.cs tests/SelfTests.cs
git commit -m "feat(dev): 编译活性门——CPU 进度驻留淘汰空转节点 + 复活重入（nodeReuse 无事件唤醒）"
```

---

### Task 3: 副作用收敛器

**Files:**
- Modify: `src/Core/Scenario/DevFocus.cs`（`Grant` :465-509 / `Suspend` :514-561 / `SetFocusMode` :126-131 / `ReconcileTick` :699-721 / `NotifyProcessChanges` 锁外段）
- Test: `tests/SelfTests.DevFocus.cs` + `tests/SelfTests.cs`

- [ ] **Step 1: 写失败测试（真机往返，守卫 + 保存还原）**

`tests/SelfTests.DevFocus.cs` 追加（放在 `TestDevFocusSharedEffectHandoff` 附近）：

```csharp
        // —— 副作用收敛器（规格 2026-10-02 §4）：升沿补做与降沿回收 ——

        private static void TestDevFocusSideEffectReconcile()
        {
            if (SvcState.Query("SysMain") == 0 && SvcState.Query("WSearch") == 0)
                Skip("SysMain/WSearch 均未运行");
            string dir = NewTempDir("devfocus-reconcile");
            Process probe = null;
            DevFocus dev = null;
            int oldIdle = DevFocus.BuildIdleDropSeconds;
            DevFocus.BuildIdleDropSeconds = 0;   // 心跳探针近零 CPU，防活性门误杀（规格 §5.4）
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                // 1) focus-only 掌权（无编译进程）：静默施加，服务不动
                dev.SetFocusMode(true);
                Eq(true, dev.IsGranted);
                Eq(true, Notif.HeldBy(Notif.OwnerDevFocus));
                Eq(false, SvcPause.HeldBy(SvcPause.OwnerDevFocus));

                // 2) 掌权期间编译起始（P1-2 主诉）：服务暂停升沿即时补做
                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, SvcPause.HeldBy(SvcPause.OwnerDevFocus));

                // 3) 编译结束、focus 仍在（IDE 常驻同款）：服务降沿回收，静默保持
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Stopped) }, false));
                Eq(false, SvcPause.HeldBy(SvcPause.OwnerDevFocus));
                Eq(true, Notif.HeldBy(Notif.OwnerDevFocus));

                // 4) focus 关闭：整体失活 → Suspend 全量还原
                dev.SetFocusMode(false);
                Eq(false, dev.IsGranted);
                Eq(false, Notif.HeldBy(Notif.OwnerDevFocus));
            }
            finally
            {
                DevFocus.BuildIdleDropSeconds = oldIdle;
                try { if (dev != null) dev.SetFocusMode(false); } catch { }
                try { if (dev != null) dev.Stop(); } catch { }
                StopOwned(probe);
                DeleteTempDir(dir);
            }
        }
```

`tests/SelfTests.cs` 追加注册：

```csharp
            test("开发专注：副作用收敛器升降沿（掌权中途编译起止/中途开关）", TestDevFocusSideEffectReconcile);
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: `FAIL  开发专注：副作用收敛器升降沿…`（步骤 1 断言 `SvcPause.HeldBy` 为 false——现状快照式 Grant 不会补做升沿）

- [ ] **Step 3: 实现收敛器**

`src/Core/Scenario/DevFocus.cs`：

(a) 字段区追加（`quietApplied` 旁）：

```csharp
        private bool svcPauseApplied;    // 收敛器记账：SvcPause 升沿已施加（规格 §4.1）
        private bool suppressApplied;    // 收敛器记账：后台压制 sweep 已施加
```

(b) `Grant()` 全方法替换：

```csharp
        /// <summary>IScenario：获得掌职权。副作用不再按 Grant 时刻快照施加——
        /// 收敛器按 build/focus 当前态升降沿收敛（规格 §4），节拍无条件启动
        /// （IDE-only 授予也要跑活性重算与收敛）。</summary>
        public override void Grant()
        {
            lock (sync)
            {
                if (granted) return;
                granted = true;
                grantStartTicks = DateTime.UtcNow.Ticks;
            }
            try
            {
                bool build; bool focus; bool ide;
                lock (sync)
                {
                    build = activeBuildPids.Count > 0;
                    focus = FocusModeOn;
                    ide = activeIdePids.Count > 0;
                }
                StartReconcileTimer();
                ReconcileSideEffects();
                if (ide) ReconcileIdeBoost();
                Logger.Log("开发专注：获得掌职权（编译=" + build + " 专注=" + focus + " IDE=" + ide + "）");
                ActivityLog.Add("开发专注掌权（编译=" + build + " 专注=" + focus + " IDE=" + ide + "）");
            }
            catch (Exception ex) { Logger.LogFailure("开发专注掌权失败", ex); }
        }
```

(c) 新增收敛器方法（放在 `Grant` 之后）：

```csharp
        /// <summary>副作用收敛器（规格 §4.1 期望态表）：掌权期间让 服务暂停=build、
        /// 通知静默=focus、后台压制=build||focus 持续成立——Grant/进程事件/开关翻转/
        /// 校正节拍/活性门淘汰全部收敛到期望态。升降沿幂等：SvcPause/Notif 走 owner
        /// 引用计数（游戏叠加时最后一个占用方离开才真还原），sweep 的 Acquire 对已压
        /// 进程返回 AlreadyThrottled，boost 有快照字典防重。</summary>
        private void ReconcileSideEffects()
        {
            bool build; bool focus; bool grantedNow;
            lock (sync)
            {
                grantedNow = granted;
                build = activeBuildPids.Count > 0;
                focus = FocusModeOn;
            }
            if (!grantedNow) return;   // 已挂起：不施加升沿，全量还原交给 Suspend

            try
            {
                if (build && !svcPauseApplied)
                    svcPauseApplied = SvcPause.Activate(SvcPause.OwnerDevFocus);
                else if (!build && svcPauseApplied)
                {
                    try { SvcPause.Restore(SvcPause.OwnerDevFocus); } catch { }
                    svcPauseApplied = false;
                }

                if (focus && !quietApplied)
                {
                    try { quietApplied = Notif.Quiet(Notif.OwnerDevFocus); } catch { }
                }
                else if (!focus && quietApplied)
                {
                    try { Notif.Restore(Notif.OwnerDevFocus); } catch { }
                    quietApplied = false;
                }

                bool wantSuppress = build || focus;
                if (wantSuppress && !suppressApplied)
                {
                    SweepBuildSuppression();
                    suppressApplied = true;
                }
                else if (!wantSuppress && suppressApplied)
                {
                    if (core != null) { try { core.ReleaseReason(SuppressReason.Build); } catch { } }
                    suppressApplied = false;
                }

                if (build) BoostBuildProcesses();   // 幂等：快照字典 ContainsKey 跳过

                // 竞态护栏（与 ReconcileTick 同款）：收敛期间挂起到达时，已施加升沿立即回收
                lock (sync) grantedNow = granted;
                if (!grantedNow)
                {
                    if (svcPauseApplied) { try { SvcPause.Restore(SvcPause.OwnerDevFocus); } catch { } svcPauseApplied = false; }
                    if (quietApplied) { try { Notif.Restore(Notif.OwnerDevFocus); } catch { } quietApplied = false; }
                    if (suppressApplied && core != null) { try { core.ReleaseReason(SuppressReason.Build); } catch { } suppressApplied = false; }
                }
            }
            catch (Exception ex) { Logger.LogFailure("开发专注：副作用收敛失败", ex); }
        }
```

(d) `SetFocusMode` 末尾追加：

```csharp
            RecomputeActivity();
            lock (sync) { if (granted) ReconcileSideEffects(); }   // 掌权中开关翻转的升降沿
```

(e) `NotifyProcessChanges` 锁外段（`if (ideChanged) RefreshIdeVisible(false);` 之后、活性上报之前）追加：

```csharp
            // 仍掌权时收敛副作用：编译中途起始的服务暂停/压制升沿在此补做（P1-2 主诉）
            lock (sync) { if (granted) ReconcileSideEffects(); }
```

(f) `ReconcileTick()` 全方法替换：

```csharp
        /// <summary>校正节拍：收敛副作用 + IDE 窗口条件复查。回调到达时可能已挂起，先检查。</summary>
        private void ReconcileTick()
        {
            lock (sync) { if (!granted) return; }
            try
            {
                // IDE 窗口条件复查（节流），无可见窗口的 IDE 不再维持场景活性
                RefreshIdeVisible(false);
                // 专注开关可能已被 WPF 宿主跨进程关闭：本进程没有进程事件时，靠节拍重算活性
                // 触发仲裁器挂起（还原 Notif 静默等副作用），避免开关失效延迟到下一个进程事件。
                RecomputeActivity();
                lock (sync) { if (!granted) return; }
                ReconcileSideEffects();   // 收敛器内含压制 sweep/服务暂停/静默的全部升降沿
                ReconcileIdeBoost();
                // 竞态护栏：挂起可能在收敛期间到达（granted 已翻 false），泄漏的压制立即回收；
                // 若挂起在护栏之后到达，Suspend 自带的 ReleaseReason(Build) 会兜底。
                lock (sync) { if (!granted && core != null) core.ReleaseReason(SuppressReason.Build); }
            }
            catch { }
        }
```

(g) `Suspend()` 的锁内标志复位段（`quietApplied = false;` 旁）追加：

```csharp
                svcPauseApplied = false;
                suppressApplied = false;
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: `TOTAL 298 … FAIL 0`（若既有 DevFocus 测试 FAIL，多为快照语义被收敛器改变所致——逐个核对断言意图后修正测试或实现，不得跳过）

- [ ] **Step 5: Commit**

```bash
git add src/Core/Scenario/DevFocus.cs tests/SelfTests.DevFocus.cs tests/SelfTests.cs
git commit -m "feat(dev): 副作用收敛器——SvcPause=build/Notif=focus/压制=build||focus 掌权全程升降沿收敛，修「先开 IDE 再编译/再开专注」失灵"
```

---

### Task 4: 既有探针测试适配活性门

**Files:**
- Modify: `tests/SelfTests.DevFocus.cs`（9 处 `StartNamedProbe` 调用点所在测试）

- [ ] **Step 1: 定位全部探针测试**

Run: `grep -n "StartNamedProbe" tests/SelfTests.DevFocus.cs`
Expected 9 处：:60、:98、:136、:210、:453、:506、:537、:903、:954（行号随前序任务漂移，以测试方法名为准）

- [ ] **Step 2: 每个测试方法插入关断插桩**

每个使用 `StartNamedProbe` 的测试方法，在 `string dir = NewTempDir(...)` 行后插入：

```csharp
            int oldIdle = DevFocus.BuildIdleDropSeconds;
            DevFocus.BuildIdleDropSeconds = 0;   // 心跳探针近零 CPU，活性门会误杀（规格 §5.4）
```

并在该方法既有 `finally` 块末尾（`DeleteTempDir(dir);` 之前）插入：

```csharp
                DevFocus.BuildIdleDropSeconds = oldIdle;
```

说明：Task 3 新增的 `TestDevFocusSideEffectReconcile` 已带同款插桩，勿重复。

- [ ] **Step 3: 全量门禁确认无回归**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: `TOTAL 298 / FAIL 0`（SKIP 2~5 浮动属常态）

- [ ] **Step 4: Commit**

```bash
git add tests/SelfTests.DevFocus.cs
git commit -m "test(dev): 既有探针测试适配编译活性门（心跳探针关断静默淘汰）"
```

---

### Task 5: 三语 README 计数同步 + 真机验收

**Files:**
- Modify: `README.md` / `README.en.md` / `README.ja.md`（295 → 298，各自徽章行 + 正文计数行，共 8 处）

- [ ] **Step 1: 计数同步**

Run: `sed -i 's/295/298/g' README.md README.en.md README.ja.md`
核对：`grep -n "298" README.md README.en.md README.ja.md` 应命中 4+2+2 处且语义均为自测计数。

- [ ] **Step 2: 真机验收三场景（规格 §5.5）**

用 `.tmp-live/` 现有 UIA/探针模式（本任务不进仓库）：

1. **空转淘汰**：复制自测 exe 为 `msbuild.exe --test-heartbeat-probe` 常驻 → 启动 `dev.cmd` 应用 → 观察 ≤20 秒托盘「编译优化中」消失、SysMain/WSearch 恢复运行（`sc query` 前后对照）。
2. **真编译即时掌权**：`build.cmd` 跑一次真实构建 → 编译期托盘显示编译优化中、结束即恢复。
3. **git 无感**：连跑 `git status` / `git log` 数次 → 全程无「编译」气球、无服务停启。

Expected: 三场景全过；失败则按 systematic-debugging 定位（优先查活性门阈值/收敛器调用点），修后重走本步。

- [ ] **Step 3: 最终门禁**

Run: `cmd //c "dev.cmd test" 2>&1 | tail -5`
Expected: `TOTAL 298 / FAIL 0`

- [ ] **Step 4: Commit**

```bash
git add README.md README.en.md README.ja.md
git commit -m "docs: 三语 README 自测计数 295→298（活性门 + 收敛器测试）"
```

---

### Task 6: 规格偏差回写与收口

**Files:**
- Modify: `docs/superpowers/specs/2026-10-02-devfocus-build-activity-and-reconciler-design.md`（§8）

- [ ] **Step 1: 回写实施偏差**

§8 追加编号条目，逐条记录实施期与规格的出入（阈值调整/调用点增减/测试环境守卫差异/真机验收结果），格式仿 matcha 规格 §197-209 的偏差回写区。

- [ ] **Step 2: 收口提交与推送**

```bash
git add docs/superpowers/specs/2026-10-02-devfocus-build-activity-and-reconciler-design.md
git commit -m "docs: 编译活性门+收敛器规格偏差回写（含真机验收记录）"
git push origin main
```

（发版说明条目——git/docker 名录收紧属用户可见行为变更——留待下次发版时随版本条目书写，不在本计划内。）

---

## 自审记录

- 规格覆盖：§3.1→Task 1；§3.2→Task 2；§4→Task 3；§5.4 既有适配→Task 4；§5.5/A4→Task 5；§7 A5→Task 6。§2 非目标无对应任务（正确）。
- 类型一致性：`ShouldDropForIdle(long,long)`/`RemoveIdleBuilds(long)`/`SampleBuildCpu()`/`BuildIdleDropSeconds`/`CpuProbe(Func<int,TimeSpan?>)` 各任务引用一致；`ProcessChangeBatch(ProcessChange[], bool)` 构造与 `ProcNotify.cs:31` 一致；`Notif.OwnerDevFocus`/`SvcPause.OwnerDevFocus` 与源码常量名一致。
- 已知交由实施期核对的点（非占位）：Task 2 Step 7(c) 中 `buildActivity`/`buildEndedElapsed` 为方法内既有局部变量，重构时保持锁内计算不变仅替换锁外消费段；Task 3 Step 4 若既有测试因语义变化 FAIL，按断言意图逐个裁决。
