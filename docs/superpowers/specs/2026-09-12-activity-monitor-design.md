# 实时监控页设计（Caelus 实时活动图形化检视）

日期：2026-09-12
状态：实施中

## 0. 目标

用户能在一个页面实时看到 **Caelus 此刻在做什么**：谁在掌权、正在压制/提优哪些进程、最近做了哪些动作。三区布局，2 秒自刷新。

## 1. 决策记录

- **数据源三件套**：`ActivityLog`（核心侧动作环形日志，60 条）+ `SuppressionCore.SnapshotRows()`（实时压制清单）+ 场景活性计数/提优描述（DevFocus/DailyCare 公开只读快照）。不引入图表库，图形化 = 状态徽章 + 色块行 + 时长列。
- **ActivityLog 常驻核心**（不走 Logger 文件）：内存环形缓冲、锁保护、故障静默；UI 2 秒读一次零 IO。记录点收敛在「用户可感知的动作」：掌权/挂起、压制批结果、阻断、服务拉起结果、维护执行、游戏会话开始/结束。
- **压制清单带时长**：Entry 增加 `AcquiredTicks`（首次实际压制落盘时置位），行显示「已压制 X 分钟」；快照在 core 锁内构建，UI 只读副本。
- **提优清单 v1 入列**：DevFocus/DailyCare 各暴露 `DescribeBoosts()`（名称 + AboveNormal/IO 档位），游戏提优体现在状态瓦片。
- **预览/探针模式**：ActivityView.InjectSampleData 注入样例行（对齐既有探针模式）；真实实例不可用时（预览宿主）计数显示「—」。
- **新页面接线成本全包**：导航（诊断组，日志之后）、RunShot 双 pages 数组、RunSingleShot 注入开关、UIA 交互脚本 13→14 页列表、三语 README、矩阵截图。

## 2. 核心改动

### 2.1 ActivityLog（新文件 src/Core/ActivityLog.cs）
- `internal static void Add(string)`；`internal static List<ActivityEntry> Recent(int max)`（新→旧，ActivityEntry{Ticks, Text}）。
- 上限 60，满则挤最旧；全部 try/catch 静默。

### 2.2 记录点（每处一行 Add）
- DevFocus：Grant（带来源构成）、Suspend、编译位压制批结果（N 个）、分心阻断关闭（含进程名）。
- DailyCare：Grant、Suspend、压制批结果。
- DevServiceGuard：RestartAttempted（ok/giveup/nocmd → 中文一句）。
- HealthCare.RunIfDue：自动执行完成（一句）。
- GameMode.Session：ReportBegin（游戏名）/ReportFinish（时长+压制数）。

### 2.3 SuppressionCore 快照
- Entry 加 `AcquiredTicks`（Applied false→true 时置位）。
- `internal sealed class SuppressedRow { Pid, Name, LevelText, ReasonsText, AcquiredTicks }`
- `internal List<SuppressedRow> SnapshotRows()`：锁内复制 map（仅 Applied 行），Reasons 位 → 「编译/日常/后台/反作弊」拼接。

### 2.4 场景只读快照
- DevFocus：`BuildActivityCount`/`IdeActivityCount`（锁内计数）、`DescribeBoosts()`（ideBoosted/buildBoosted 名称+档位）。
- DailyCare：`FamilyVisibleNow`/`OnBatteryNow`、`DescribeBoosts()`。

## 3. WPF

- `ActivityViewModel`（DispatcherTimer 2s）：状态瓦片（三场景：徽章+来源明细）、今日统计条（专注/日常/编译/分心）、压制清单行集合（表头含计数）、提优清单行集合、动作流行集合（HH:mm:ss + 文本）。全量重建行集合（量级 ≤ 几百，2 秒一次可接受，不做增量 diff）。
- `ActivityView.xaml`：四区 SettingsGroup 卡：①实时状态 ②今日 ③压制中（ItemsControl 表） ④提优中 + ⑤最近动作流。静态 InjectSampleData。
- MainWindow：NavActivity（诊断组尾部）+ 构造 + 切换 + ForShot 映射 "activity"；Lang 键 `nav.activity`/`wpf.activity.sub`。

## 4. 测试（293→295）

| 用例 | 覆盖 |
|---|---|
| 监控日志：环形截断与新→旧序 | Add 70 条 → Recent(60)/Recent(5) 边界 |
| 压制快照：行内容与释放清空 | Acquire(Build,Eco) → 行含名称/原因/级别/AcquiredTicks；ReleaseReason → 空 |

## 5. 实施顺序

1. `feat(core): 实时监控数据源——ActivityLog 环形动作日志 + SuppressionCore.SnapshotRows + 场景活性/提优快照`
2. `feat(ui): 实时监控页——状态瓦片/今日条/压制与提优清单/动作流 + 导航接线`
3. `chore: 矩阵截图 14 页 + UIA 脚本 14 页 + README 三语 + 计数 295 + 规格回写`

## 6. 明确不做

- 不做历史曲线/图表（日志页与健康历史已有文本态）。
- 不做压制行的手动操作（还原/释放）——只读检视，防误触。
- 不做 WMI 实时 CPU 列（开销大、价值低）。
- WinForms 旧宿主不加此页（WPF 专属，对齐近三轮取舍）。
