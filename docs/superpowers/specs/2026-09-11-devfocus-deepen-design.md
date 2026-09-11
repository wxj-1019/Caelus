# 开发专注深化设计（IDE 名录自定义 / 专注趋势 / 分心统计阻断 / 服务自动拉起）

日期：2026-09-11
状态：已实施（自测 276→282 全绿；规格偏差见 §11）

上一轮规格（2026-09-11-dailycare-health-framework-design.md）遗留缺口：「开发专注模式的缺口补齐（IDE 名录自定义、专注统计历史等）——下一轮单独立项」。本轮即该轮。

## 0. 现状与目标

| 子项 | 现状 | 目标 |
|---|---|---|
| IDE 名录 | `IdeCatalog` 15 项编译名录 + 安装目录双校验，**不可自定义**（唯一没有自定义段的开发目录） | 注册表 `CustomIdeProcs`，对齐 `CustomDailyProcs`/`CustomBuildProcs` 模式；设置页编辑入口 |
| 专注统计 | `FocusStats` 三个注册表键只存「今日」聚合，历史即丢 | 按日 TSV 历史（60 天），详情页近 7 日趋势条 |
| 分心应用 | `DistractCatalog` 命中后仅一次性气球提醒（仅提醒，不处理） | 命中计数入当日统计/趋势；新增可选「专注阻断」（默认关）：优雅关闭→超时强杀 |
| 开发服务 | `DevServiceGuard` 跟踪名录服务，最后实例退出仅托盘提醒 | 可选「自动拉起」（默认关）：按捕获的原命令行重启，连败熔断 |

## 1. 决策记录

- **IDE 自定义名录对齐既有模式**，不发明新格式：注册表字符串键 `CustomIdeProcs`，分号/换行分隔，`.exe` 后缀归一，坏行容错跳过，大小写不敏感。自定义项**无目录锚点按名匹配**（编译名录项仍走双校验）——与 DailyCatalog 自定义段完全一致。
- **专注历史用 TSV 文件而非注册表**：按日一行、60 天截断，模式对齐 `HealthHistory`（AtomicFile 全量重写 + `FilePath` 测试可覆写 + `Paths.Data` 兜底 TempPath）。列内全是日期/数字，**不需要转义器**。
- **今日聚合（注册表）与历史（TSV）双写**：今日键是既有读路径（FocusStatsText），保留不动；历史文件是趋势数据源。同一事件双写，字段口径一致。
- **分心阻断默认关**：关闭用户进程是激进动作，默认必须用户显式开启。阻断策略 = 先 `CloseMainWindow()` 优雅关闭，1 秒不退再 `Kill()`，全程故障隔离。
- **阻断不去重、提醒去重**：`distractNotified` 集合继续只管气球（每名一次）；阻断每次命中都执行——用户手滑再开分心应用仍会被关回去，但不会被气球刷屏。
- **服务自动拉起默认关 + 连败熔断**：不区分「人为关闭/崩溃」（无法可靠区分），靠默认关 + 每名连续失败 5 次熔断 + 存活≥60s 重置预算 + 每次拉起气球告知来兜底。
- **命令行捕获用 WMI**（`Win32_Process.ExecutablePath/CommandLine`）：`System.Management` 已在 csproj 引用。实例启动时异步捕获；Caelus 启动时对名录内已运行实例做一次快照捕获。捕获不到命令行的服务退出时只提醒不拉起。
- **UI 只做 WPF 宿主**：WinForms 托盘宿主保持只读统计展示现状（与上轮 CustomDailyProcs 设置入口同一取舍）。

## 2. IDE 名录自定义（IdeCatalog）

- `IdeCatalog` 增加 `CustomKey = "CustomIdeProcs"`、`CustomList` 属性（setter 清缓存）、`LoadCustom()` 惰性 HashSet（分号/换行拆分、Trim、去 .exe、OrdinalIgnoreCase）。
- `NameMatches`：内置 Map 或自定义集命中即真（事件热路径预筛，自定义项零 IO）。
- `IsMatch`：内置项照旧双校验（路径前缀）；未命中内置 → 自定义集按名即真。
- 消费方（`DevFocus.IsIdeProcess`、`ScenarioStatusSource.ScanProcesses`）零改动。
- 设置页 ZoneDev 加编辑行（对齐 DailyCustom 行：标题/多行 TextBox/保存按钮/说明），VM 三件套 + `OnIdeCustomSave`，Lang 键 `set.ide.custom.title/note/saved`。

## 3. 专注统计历史与趋势

### 3.1 FocusHistory（新文件 src/Core/Scenario/FocusHistory.cs）

- 文件：`<Paths.Data>\focus-history.tsv`；`internal static string FilePath` 测试可覆写。
- 行格式（5 列，无表头）：`yyyy-MM-dd \t seconds \t sessions \t distract \t blocked`。
- API：
  - `AppendOrUpdate(day, dSeconds, dSessions, dDistract, dBlocked)`：同日合并增量（无则新建行），超 `Keep=60` 天截掉最旧，AtomicFile 重写。
  - `LoadAll()`：按日升序；坏行（列数≠5/解析失败）跳过。
  - `LastDays(int n, DateTime today)`：补零输出最近 n 天（含今日），老→新。
- 全程 lock 单写者；写失败静默（统计不得反噬场景挂起路径）。

### 3.2 FocusStats 扩展

- `RecordSession` 在写今日键的同时 `FocusHistory.AppendOrUpdate(today, sec, 1, 0, 0)`（try/catch 包裹，Suspend 故障隔离链不受影响）。
- 新增 `RecordDistract(bool wasBlocked)`：今日键 `FocusStatsDistract`/`FocusStatsBlocked`（沿用日历天归零模式）+ 历史当日行增量。
- 新增 `TodayDistract/TodayBlocked`；`ResetForTest` 同步清新键。

### 3.3 详情页趋势（ScenarioDetailView dev 段）

- ZoneFocus 卡下新增「近 7 日专注趋势」组卡（`FocusTrendVisible` = isDev 门控）。
- `FocusTrendRow`：`DayText(MM-dd)`、`MinutesText`、`BarHeight`（相对当日最大分钟数，56px 高度上限）、`DistractText`（命中次数，0 不显示）、`IsToday`（DataTrigger 换强调色刷）。
- 刷新时机：首次加载 + `FocusStatsText` 变化时（会话结束/新命中触发重载），不做 2 秒定时重读文件。

## 4. 分心应用统计与可选阻断

- 策略纯函数（可单测，对齐 `ShouldSuppressBackground` 先例）：
  `internal static DistractAction DecideDistractAction(bool granted, bool focusOn, bool alreadyNotified, bool blockOn)`
  → `None`（未掌权/未开专注）/ `NotifyOnly`（命中首次）/ `NotifyAndBlock`（首次+阻断开）/ 新增 `BlockAgain`（已提醒过但阻断开——静默再阻断）。枚举：`None, NotifyOnly, NotifyAndBlock, BlockAgain`。
- 统计：命中即 `FocusStats.RecordDistract(blocked)`（命中必计数；阻断与否不影响命中计数，blocked 只进 blocked 列）。
- 阻断执行（锁外、故障隔离）：`Process.GetProcessById(pid)` → `CloseMainWindow()` → 最多 1 秒未退 → `Kill()`。PID 复用风险可接受（Started 事件新鲜度毫秒级）。
- 开关键 `DevFocusDistractBlock`（默认 false），设置页 ZoneDev 分心清单编辑行下加 PolicyToggle 行。DevFocus 内每次命中时读键（命中频率低，注册表读可接受）。
- 气球：提醒路径原键 `bal.distract`；阻断路径新键 `bal.distract.block`（SessionChanged 文案键机制，两宿主通用映射零改动）。

## 5. 开发服务自动拉起（DevServiceGuard 扩展）

- 注入：`public Func<bool> RestartEnabled`（两宿主接 `() => Settings.Load("DevSvcRestartOn", false)`）。
- 捕获（异步、故障隔离）：
  - Started 记账时收集 `(pid, bare)`，锁外 ThreadPool 里 WMI 查 `ExecutablePath/CommandLine`，按名存 `launches`（最新实例覆盖）。
  - 新增 `CaptureSnapshot()`：启动时对名录内已运行进程补捕（两宿主在初始扫描后调用）。
- 拉起决策（纯函数可单测）：
  - `BudgetAllows(consecFails)`：`consecFails < MaxConsecutiveRestarts(5)`。
  - 退出的实例存活 ≥ `StableAliveTicks(60s)` → 该名 `consecFails` 归零（存活时长近似用 firstSeen，多实例场景取首实例，文档化取舍）。
- 拉起执行（锁外 ThreadPool）：`SplitCommandLine` 拆出 exe/参数（引号感知），`UseShellExecute=false` + `WorkingDirectory=exe 目录`；失败 `consecFails++`；无捕获命令行 → 直接按熔断处理（不再重试该名）。
- 结果事件：`RestartAttempted(string name, string reason)`，reason ∈ `ok / fail / giveup / nocmd`；`ok→bal.devsvc.restart`、`giveup→bal.devsvc.giveup`、`nocmd→bal.devsvc.nocmd` 气球，`fail` 只记日志静默重试。`Stop()` 后不再拉起。

## 6. 数据流

- 专注会话结束：`DevFocus.Suspend` → `FocusStats.RecordSession` → 今日键 + TSV 当日行 →（下次 Refresh）趋势条。
- 分心命中：ProcNotify → 场景泵 → `DevFocus.NotifyProcessChanges` → 策略函数 → （提醒气球）+（阻断）+ `FocusStats.RecordDistract` → 趋势当日分心计数。
- 服务退出：ProcNotify → `DevServiceGuard` → `ServiceStopped`（提醒气球，原样）→ 预算判定 → 拉起 → `RestartAttempted`（结果气球）。

## 7. 错误处理与安全

- 统计/历史/捕获/拉起全部故障隔离（try/catch + Logger），任何失败不得影响 Grant/Suspend 主链。
- 阻断只对 `DistractCatalog` 显式命中项生效（用户自己加的名单），且默认关；绝不波及 IDE/编译/服务/白名单名录。
- 拉起用捕获的原路径原参数，不拼命令解释器；`Stop()` 后与熔断后不动作。
- 自测写 Settings 的新用例一律注册在 `UseTransientStoreForCurrentProcess()`（SelfTests.cs 中段）之后，不污染真实注册表。

## 8. 测试策略（TDD，276→282 项门禁）

| 用例（SelfTests 注册名） | 覆盖 |
|---|---|
| IDE 目录：自定义名录合并与坏行容错 | CustomIdeProcs 合并/去 .exe/Trim/大小写；内置双校验不受影响 |
| 专注历史：按日合并、截断与近 7 日补零 | AppendOrUpdate 合并、Keep 截断、坏行跳过、LastDays 补零排序 |
| 专注历史：会话与分心计数写入当日趋势 | RecordSession/RecordDistract → 注册表 + TSV 双写、日切归零 |
| 分心策略：掌权且专注才动作，阻断开关分级 | DecideDistractAction 四态真值表 |
| 服务拉起：预算熔断与稳定重置 | BudgetAllows、MaxConsecutiveRestarts、StableAliveTicks 重置语义 |
| 服务拉起：命令行拆分引号感知 | SplitCommandLine 引号/无引号/空串 |

## 9. 实施顺序（每步一提交）

1. `feat(dev): IDE 名录自定义（CustomIdeProcs，对齐编译名录模式）+ 设置页编辑入口`
2. `feat(dev): 专注统计按日历史（TSV）+ 详情页近 7 日趋势`
3. `feat(dev): 分心应用命中统计 + 可选专注阻断（默认关）`
4. `feat(dev): 开发服务退出自动拉起——命令行捕获 + 连败熔断`
5. `docs: 三语 README 自测计数 276→282 同步 + 本规格偏差回写`（真机截图重摄另轮）

## 10. 明确不做（YAGNI）

- 不做趋势图表控件/折线图——ItemsControl 柱条足够，不引图表库。
- 不做分心应用按名细分统计排行——当日命中总数 + blocked 列够用。
- 不做服务拉起的持久化队列（跨 Caelus 重启补拉）——退出期间错过即错过。
- 不做人为关闭/崩溃区分（退出码启发式）——误判成本高于收益。
- 不做 WinForms 托盘宿主的新 UI——统计展示沿用现状。
- 不做 WMI 以外的命令行兜底（PEB 读取等）——捕获不到就诚实不拉起。

## 11. 实施偏差记录（实施完成后回写）

- **自测基线漂移**：拉取时套件实际 277 项（README 记 276 为上一轮遗留漂移），本轮 +6 → **283**。三语 README 一并修正到 283。
- **分心阻断气球策略放宽**：`BlockAgain`（已提醒过的再次命中）也发 `bal.distract.block`——用户必须知道自己的应用被关了；「去重」只适用于普通提醒（`bal.distract`）。
- **服务拉起 nocmd 即熔断**：无捕获命令行的服务退出时，直接把连败计数置满（无从重试），`bal.devsvc.nocmd` 只报一次；`giveup` 气球同样只在熔断瞬间报一次，后续退出静默。
- **趋势刷新签名含日期**：`RefreshFocusTrend` 的变更签名 = 秒/会话/分心/日期四元组，防止页面跨零点滞留「昨天当今天」的窗口。
- **IDE 自定义编辑行位置**：放在 ZoneDev 的 IDE 优化开关行之后（按主题就近），而非全部编辑行集中尾部。
- **现有分心用例扩展**：`TestDevFocusDistractOnce` 追加阻断段（假 PID 优雅关闭失败被故障隔离吞掉，不产生真实进程操作），并补 `FocusStats.ResetForTest()` 清理（该用例注册在临时存储启用之前，沿用既有 pre-switch 用例写真实注册表的口径，finally 兜底清键）。

## 12. 迭代轮（2026-09-11 同日）

自测 283→**286**（+3：预算重置、分心按名 Top8、阻断气球限频判定）。处置记录：

- **已修：拉起预算重置路径**——捕获到新启动命令（新健康实例，多为用户手动拉起）即清除该服务连败计数；熔断不再需要等稳定 60 秒。
- **已修：阻断气球 30 秒/名限频**——被阻断的自启循环应用不再刷屏（阻断照常执行）；限频表随专注开关关闭/场景关闭/Stop 清空。
- **新增：分心按名统计**——`FocusStatsDistractNames` 注册表键（`name:count;...` 按次数降序 Top8），存储层做 `.exe`/Trim/大小写归一；详情页专注模式卡新增「今日分心：discord×2 · steam×1」行。
- **保留（评估后不改）**：`FocusHistory` 按日全量原子重写——60 行的文件，AtomicFile 是仓库的安全写模式，追加+fsync 复杂度不值；分心阻断对无主窗口进程直接强杀——窗口进程才能「优雅关闭」，无窗进程强杀是唯一选项，且名单与开关都在用户手里；趋势卡刷新 1-2 秒延迟——挂签名刷新机制（今日口径变化才重读），2 秒轮询不读文件已达目的。
- **性能审计**：新增热路径均为事件级低频（分心命中/服务启停才走），注册表读仅在命中时发生；`DevServiceGuard.PruneDeadLocked` 每批全量开句柄探测为存量行为（v1.9.1 语义），本轮不动、留观。
- **演示图口径**：`demo-*.png` 为种入 7 天演示数据的探针/实机产物，截图后现场已还原；demo-dev-top/demo-dev-trend（交互版）被 demo-dev-focus（探针全页版）取代删除。
