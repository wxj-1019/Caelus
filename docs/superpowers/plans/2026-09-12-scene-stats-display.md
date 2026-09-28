# 开发/日常双场景扩展与显示优化——实施计划

**Goal:** 场景统计 TSV 统一 8 列（日常/编译入列）、日常趋势卡、编译统计、专注目标进度、维护到期倒计时，两侧详情页显示质量对齐。

**Architecture:** FocusHistory 尾部追加 3 列（旧 5 列无损）；DailyStats 镜像 FocusStats；DevFocus 编译真实起止；VM/XAML 对称扩展（dev 趋势卡带目标/合计，daily 趋势卡 + 到期倒计时）。

**Tech Stack:** C# 5、WPF Click 事件、既有模式（AtomicFile/注册表键/Lang 键/自测）。

**规格：** docs/superpowers/specs/2026-09-12-scene-stats-display-design.md

**通用约定:** 同前轮（两行文件头、临时存储注册点之后、dev.cmd test 门禁、每 Task 一提交）。

---

### Task 1: 统计统一化（FocusHistory 8 列 + DailyStats + DailyCare 记录）

**Files:** Modify `src/Core/Scenario/FocusHistory.cs`；Create `src/Core/Scenario/DailyStats.cs`；Modify `src/Core/Scenario/DailyCare.cs`、`src/Core/Scenario/FocusStats.cs`（AppendOrUpdate 签名）、`tests/SelfTests.DevFocus.cs`、`tests/SelfTests.DailyCare.cs`、`tests/SelfTests.cs`

- [ ] **Step 1: 测试**——扩充 `TestFocusHistoryMergeAndTrim`（8 列断言 + 旧 5 列行 LoadAll 兼容）；新 `TestSceneStatsDailyAndBuild`（daily/编译 delta 写入、同日合并）；新 `TestDailyCareRecordsSession`（DailyCare Grant→Suspend 记录 DailyStats 与 TSV）。
- [ ] **Step 2: 门禁红**。
- [ ] **Step 3: 实现**——FocusDayRecord 8 字段；AppendOrUpdate(delta)；LoadAll 5 列兼容；DailyStats 新文件；DailyCare grantStartTicks + Suspend 记录。
- [ ] **Step 4: 门禁绿 + 提交**：`feat(scene): 统计统一化——FocusHistory 8 列兼容扩展 + DailyStats + DailyCare 掌权记录`。

### Task 2: 编译统计 + 开发页目标/合计行

**Files:** Modify `src/Core/Scenario/FocusStats.cs`（RecordBuild）、`src/Core/Scenario/DevFocus.cs`（buildStartTicks）、`wpf/ScenarioViewModel.cs`、`wpf/Views/ScenarioDetailView.xaml`、`tests/SelfTests.DevFocus.cs`、`tests/SelfTests.cs`

- [ ] **Step 1: 测试**——`TestFocusBuildRecorded`（RecordBuild 双写 + 今日键）；目标解析纯函数测试（30/1440/非法→240）。
- [ ] **Step 2: 红**。
- [ ] **Step 3: 实现**——RecordBuild 键与 TSV；DevFocus 编译起止（替换 becameIdle 近似日志）；VM `BuildStatsText/FocusTrendGoalText/FocusTrendTotalsText`；XAML 三行。
- [ ] **Step 4: 绿 + 提交**：`feat(dev): 编译统计真实起止 + 今日编译行 + 趋势合计与专注目标进度`。

### Task 3: 日常趋势卡 + 维护到期倒计时

**Files:** Modify `wpf/ScenarioViewModel.cs`、`wpf/Views/ScenarioDetailView.xaml`、`src/Core/Scenario/HealthCare.cs`（如缺到期计算辅助）、`tests/SelfTests.cs`（如有新增）

- [ ] **Step 1: 实现**——`DailyTrendRows/DailyTrendVisible/RefreshDailyTrend`（复用 FocusTrendRow）；维护中心 `HealthNextDueText`（HealthLastRun+IntervalDays 纯日期计算）。
- [ ] **Step 2: 绿 + 提交**：`feat(daily): 详情页近 7 日日常家族活跃趋势卡 + 维护到期倒计时`。

### Task 4: 设置页专注目标输入行

**Files:** Modify `wpf/SettingsViewModel.cs`、`wpf/Views/SettingsView.xaml(.cs)`、`src/Platform/Lang.cs`

- [ ] **Step 1: 实现**——Lang 3 键；VM 输入/保存/校验（30-1440，非法提示）；XAML 行对齐健康频率行模式。
- [ ] **Step 2: 绿 + 提交**：`ux(set): 每日专注目标输入行——趋势卡进度参照线（默认 240 分钟，范围 30-1440）`。

### Task 5: 收口——重摄 + 计数同步 + 规格回写

- [ ] 矩阵重摄（种子演示数据 → 跑 → 还原）；demo-dev-focus 刷新。
- [ ] 三语 README 计数同步（287→实数）+ 特性句补记。
- [ ] 规格 §11 偏差回写。
- [ ] `chore: 收口——...`。

## 自审记录

- （实施过程中追加）
