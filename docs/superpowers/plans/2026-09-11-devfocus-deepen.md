# 开发专注深化——实施计划

**Goal:** IDE 名录自定义、专注统计历史趋势、分心应用统计/阻断、开发服务停了自动拉起，四件套全部落地并对齐既有模式。

**Architecture:** 全部复用现有挂点——`IdeCatalog`（编译名录 + 自定义段）、`FocusStats`（今日键）+ 新 `FocusHistory`（TSV，仿 `HealthHistory`）、`DevFocus.NotifyProcessChanges` 分心钩子（策略纯函数 + 阻断执行）、`DevServiceGuard`（捕获/预算/拉起扩展）。UI 只动 WPF 宿主（`ScenarioDetailView` dev 段 + `SettingsView` ZoneDev）。

**Tech Stack:** C# 5（.NET Framework 4.x，禁字符串插值/out-var/表达式成员）、WPF（无 ICommand，按钮走 Click）、WMI（System.Management 已引用）。

**规格：** docs/superpowers/specs/2026-09-11-devfocus-deepen-design.md

**对规格的细化/偏差（实施完成后回写规格 §11）**

**通用约定:** 新文件两行头（`// @author zenjiro 18967498922@163.com` + `// 文件用途 …`）；命名空间 `CaelusApp` / `CaelusApp.WpfHost.Views`；csproj 通配无需注册新文件；每 Task 一提交；写 Settings 的测试注册在 `UseTransientStoreForCurrentProcess()` 之后；`dev.cmd test` 门禁 0 FAIL。

---

### Task 1: IDE 名录自定义（IdeCatalog.CustomList + 设置页）

**Files:** Modify `src/Core/Scenario/IdeCatalog.cs`、`src/Platform/Lang.cs`、`wpf/SettingsViewModel.cs`、`wpf/Views/SettingsView.xaml`、`wpf/Views/SettingsView.xaml.cs`、`tests/SelfTests.DevFocus.cs`、`tests/SelfTests.cs`

- [ ] **Step 1: 写失败的测试**（SelfTests.DevFocus.cs 增加 `TestIdeCatalogCustomList`，注册行加在 `UseTransientStoreForCurrentProcess()` 之后的自定义清单区）：快照旧值 → `CustomList = "notepad; ;\r\nmyide.exe\r\nBad Row "` → 断言 NameMatches/IsMatch 按名命中、内置 `code` 路径校验不变、空行容错 → finally 还原。
- [ ] **Step 2: 跑门禁确认失败**：`cmd //c dev.cmd test`。
- [ ] **Step 3: 实现**：`IdeCatalog` 加 `CustomKey/CustomList/LoadCustom()`（逐字对齐 DailyCatalog.cs:47-74），`NameMatches` 并入自定义集，`IsMatch` 内置未命中落到自定义按名即真。
- [ ] **Step 4: UI 接线**：Lang 三键 `set.ide.custom.title/note/saved`；SettingsViewModel `IdeCustomTitle/Note/Initial + SaveIdeCustom`；SettingsView ZoneDev 编译自定义行前插同构编辑行；code-behind `OnIdeCustomSave`。
- [ ] **Step 5: 门禁绿 + 提交**：`git commit -m "feat(dev): IDE 名录自定义（CustomIdeProcs，对齐编译名录模式）+ 设置页编辑入口"`。

### Task 2: 专注统计按日历史 + 近 7 日趋势

**Files:** Create `src/Core/Scenario/FocusHistory.cs`；Modify `src/Core/Scenario/FocusStats.cs`、`wpf/ScenarioViewModel.cs`、`wpf/Views/ScenarioDetailView.xaml`、`tests/SelfTests.DevFocus.cs`、`tests/SelfTests.cs`

- [ ] **Step 1: 写失败的测试**：`TestFocusHistoryMergeAndTrim`（同日两次 AppendOrUpdate 合并、Keep=60 截断、坏行跳过、LastDays 补零升序；FilePath 覆写 + NewTempDir）；`TestFocusStatsFeedsHistory`（RecordSession/RecordDistract 后注册表键与 TSV 当日行一致、日切归零；ResetForTest 起步）。
- [ ] **Step 2: 门禁红**。
- [ ] **Step 3: 实现 FocusHistory**（5 列 TSV、Keep=60、AtomicFile、lock 单写、FilePath 可覆写、Paths.Data 兜底 TempPath）+ `FocusStats` 双写扩展（RecordSession 历史合并 try/catch；RecordDistract(bool) 写今日两键 + 历史；TodayDistract/TodayBlocked）。
- [ ] **Step 4: 趋势 UI**：`FocusTrendRow`（DayText/MinutesText/BarHeight/DistractText/IsToday）+ `FocusTrendVisible/RefreshFocusTrend()`（首次 + FocusStatsText 变化触发）；XAML ZoneFocus 下「近 7 日专注趋势」组卡，ItemsControl 横排柱条（56px 上限、IsToday 换色 DataTrigger、分心计数小字）。
- [ ] **Step 5: 门禁绿 + 提交**：`git commit -m "feat(dev): 专注统计按日历史（TSV）+ 详情页近 7 日趋势"`。

### Task 3: 分心应用统计 + 可选阻断

**Files:** Modify `src/Core/Scenario/DevFocus.cs`、`src/Core/Scenario/FocusStats.cs`（Task 2 已带）、`src/Platform/Lang.cs`、`wpf/SettingsViewModel.cs`、`wpf/Views/SettingsView.xaml(.cs)`、`tests/SelfTests.DevFocus.cs`、`tests/SelfTests.cs`

- [ ] **Step 1: 写失败的测试**：`TestDistractActionPolicy` 真值表——未掌权/未开专注 → None；掌权+专注首遇 → NotifyOnly；blockOn → NotifyAndBlock；已提醒过+blockOn → BlockAgain；已提醒过+无阻断 → None。
- [ ] **Step 2: 门禁红**。
- [ ] **Step 3: 实现**：`DistractAction` 枚举 + `DecideDistractAction` 纯函数；NotifyProcessChanges 分心钩子改走策略（提醒气球去重照旧、阻断不去重）；阻断执行 `CloseMainWindow`→1s→`Kill` 故障隔离；命中即 `FocusStats.RecordDistract(blocked)`；键 `DevFocusDistractBlock` 默认 false。
- [ ] **Step 4: UI/文案**：Lang `bal.distract.block` + `set.distract.block(.n)`；Settings 页分心清单行下 PolicyToggle 行 `DistractBlockOn`（VM 属性 + ShowFeedback）。
- [ ] **Step 5: 门禁绿 + 提交**：`git commit -m "feat(dev): 分心应用命中统计 + 可选专注阻断（默认关）"`。

### Task 4: 开发服务退出自动拉起

**Files:** Modify `src/Core/Scenario/DevServiceGuard.cs`、`src/Program.cs`、`wpf/WpfRuntime.cs`、`wpf/App.xaml.cs`、`src/Platform/Lang.cs`、`wpf/SettingsViewModel.cs`、`wpf/Views/SettingsView.xaml(.cs)`、`tests/SelfTests.DevService.cs`、`tests/SelfTests.cs`

- [ ] **Step 1: 写失败的测试**：`TestDevSvcRestartBudget`（BudgetAllows 边界、MaxConsecutiveRestarts=5、稳定存活≥StableAliveTicks 重置语义）；`TestSplitCommandLine`（带引号 exe+参数/无引号/空串/仅引号对）。
- [ ] **Step 2: 门禁红**。
- [ ] **Step 3: 实现 DevServiceGuard 扩展**：`launches/consecFails/lastAttempt` 记账；Started 锁外 ThreadPool WMI 捕获（ExecutablePath/CommandLine → launches[name]）；`CaptureSnapshot()` 补捕已运行实例；RemoveLocked 通知路径接预算判定 → `SplitCommandLine` 拉起（UseShellExecute=false + WorkingDirectory）→ `RestartAttempted(name, reason)`；`Stop()` 停拉起。
- [ ] **Step 4: 双宿主 + UI**：Program.cs / WpfRuntime.cs 接 `RestartEnabled` 注入与 `CaptureSnapshot()` 调用；App.xaml.cs / Program.cs 气球映射 ok/giveup/nocmd；Lang `bal.devsvc.restart/giveup/nocmd` + `set.devsvc.restart(.n)`；Settings 页守护清单行下 PolicyToggle `DevSvcRestartOn`（默认关）。
- [ ] **Step 5: 门禁绿 + 提交**：`git commit -m "feat(dev): 开发服务退出自动拉起——命令行捕获 + 连败熔断"`。

### Task 5: 门禁收口 + README 三语计数同步 + 规格回写

**Files:** Modify `README.md`、`README.en.md`、`README.ja.md`、规格 §11；真机截图（settings/dev 两页）另轮重摄

- [ ] `cmd //c dev.cmd test` 全绿，取 TOTAL 实数。
- [ ] 三语 README badge/正文 4+2+2 处计数同步（276→实数）。
- [ ] 规格 §11 回写实施偏差（如有）。
- [ ] `git commit -m "docs: 开发专注深化收口——三语 README 自测计数同步 + 规格偏差回写"`。

## 自审记录

- （实施过程中追加）
