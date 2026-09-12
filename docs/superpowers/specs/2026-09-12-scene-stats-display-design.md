# 开发/日常双场景扩展与显示优化设计（场景统计统一 + 显示打磨）

日期：2026-09-12
状态：已实施（自测 287→291 全绿；偏差见 §11）
上游：2026-09-11-devfocus-deepen-design.md（开发专注深化 + 迭代轮 + E2E，287 项自测）

## 0. 现状与目标

上轮把开发侧四件套落地（IDE 名录/专注趋势/分心统计阻断/服务拉起，真机 E2E 验证）。本轮把「按日统计 + 趋势显示」体系扩展到日常场景，并补开发侧缺的编译统计、专注目标，再统一两边的详情页显示质量。

| 子项 | 现状 | 目标 |
|---|---|---|
| 统计 TSV | `focus-history.tsv` 5 列只管开发侧 | 8 列统一（尾部追加日常/编译列），旧 5 列行无损读取 |
| 日常统计 | 无——日常场景掌权时长不记录 | `DailyStats` 镜像 FocusStats，详情页日常趋势卡 |
| 编译统计 | 只在日志打一句「本次编译 X 秒」（用场景会话起点近似） | 真实编译起止跟踪，今日编译行 + 趋势合计 |
| 专注目标 | 无 | 每日专注目标（默认 240 分钟，可设 30-1440），趋势卡显示进度 |
| 维护到期 | 日常详情卡只有历史，不知下次何时 | 「距下次自动维护约 X 小时 / 已到期」倒计时行 |
| 显示质量 | 趋势卡无合计行；日常页无趋势卡 | 两边对称：标题+目标/合计行；设置页今日分心一致性 |

## 1. 决策记录

- **统计存储统一在一张 TSV**（`focus-history.tsv`，类名保留 `FocusHistory`）：按日一行、同一日历天多场景并存，追加列在尾部，旧文件 5 列行读为新字段 0——不做文件迁移（原子写安全模式优先）。
- **列序**：`day \t focusSec \t focusSessions \t distract \t blocked \t dailySec \t dailySessions \t buildSec`。分心/阻断列语义不变（旧文件直接兼容）。
- **日常场景统计口径 = 掌权时长**（与开发侧一致）：日常家族可见的每一天几乎都是整天掌权，柱图表达「日常家族活跃时长」——诚实标注卡片说明。
- **编译统计用真实起止**：`activeBuildPids` 空→非空记起点、非空→空记时长（替换原来用场景会话起点的近似日志）。
- **专注目标只影响显示**（不做提醒/强推）：默认 240 分钟，范围 30-1440，设置页开发区编辑。
- **维护到期只做纯日期计算**（HealthLastRun + HealthIntervalDays），不动调度语义。
- **日常趋势卡不复制分心列**：分心统计是专注专属概念。

## 2. FocusHistory 8 列扩展

- `FocusDayRecord` 扩为 8 字段；`AppendOrUpdate(FocusDayRecord delta)` 合并所有非零字段；`LoadAll` 5 列旧行兼容（尾部补零）；`LastDays` 零填充不变。
- 纯数值列，无需转义器；Keep=60 不变。

## 3. DailyStats（新文件 src/Core/Scenario/DailyStats.cs）

- 注册表 `DailyStatsDay/Seconds/Sessions`（日历天归零，模式同 FocusStats）；`RecordSession(long, DateTime)` 写今日键 + TSV `dailySec/dailySessions` 列；`TodaySeconds/TodaySessions`；`ResetForTest`。
- `DailyCare` 增加 `grantStartTicks`：Grant 置位、Suspend 读差值并 `DailyStats.RecordSession`（故障隔离，不阻塞还原链）。

## 4. 编译统计（FocusStats 扩展 + DevFocus 起止跟踪）

- `FocusStats` 增加 `RecordBuild(long, DateTime)`：今日键 `FocusStatsBuildSec/FocusStatsBuildN`（EnsureDayLocked 同清）+ TSV `buildSec` 列；`TodayBuildSeconds/TodayBuildSessions`。
- `DevFocus`：`buildStartTicks` 真实起止；编译结束时 `RecordBuild` + 日志「本次编译 X 秒」移到编译结束点（场景仍在运行时也记账，原 becameIdle 近似日志被替换）。

## 5. WPF 显示

### 5.1 开发详情页
- 专注模式卡第三行 `BuildStatsText`：「编译 X 次 · Y 分钟」（空串近零高）。
- 趋势卡标题下两行：`FocusTrendGoalText`（「今日 141 / 240 分钟（59%）」）与 `FocusTrendTotalsText`（「近 7 日合计 21 小时 18 分钟 · 分心 8 次 · 阻断 2 次」）。刷新随趋势签名（今日口径变化）重算。

### 5.2 日常详情页
- 新增「近 7 日日常家族活跃」趋势卡（`DailyTrendRows`/`DailyTrendVisible`）：柱条按日常活跃秒数归一，标题下合计行「近 7 日合计 X 小时 Y 分钟」。
- 维护中心卡标题下新增 `HealthNextDueText`：「距下次自动维护约 23 小时 / 维护已到期（满足条件自动执行）/ 尚未跑过第一次维护」。

### 5.3 设置页
- 开发区「今日开发专注」行保持；新增「每日专注目标（分钟）」输入行（对齐健康频率行的模式，1-1440 校验，键 `FocusGoalMinutes` 默认 240）。

## 6. 数据流

- 日常会话结束：`DailyCare.Suspend` → `DailyStats.RecordSession` → 今日键 + TSV。
- 编译结束：`DevFocus.NotifyProcessChanges`（编译集合 1→0）→ `FocusStats.RecordBuild` → 今日键 + TSV。
- 显示：详情页 Refresh（dev/daily 分支）按今日口径签名重载各自趋势卡与合计/目标行。

## 7. 错误处理与安全

- 统计写入全部故障隔离，不得影响 Suspend 还原主链。
- TSV 旧格式读取兼容 + 坏行跳过（既有语义）。
- 写 Settings 的测试注册在临时存储启用之后；TSV 测试 FilePath 覆写。

## 8. 测试策略（287→~291）

| 用例 | 覆盖 |
|---|---|
| 专注历史：8 列合并/截断/旧 5 列兼容/近 7 日补零（扩充既有用例） | 新列读写、旧文件无损 |
| 场景统计：日常/编译列写入与双场景同日合并 | DailyStats + RecordBuild + TSV 当日行 |
| 日常统计：掌权时长记录（DailyCare 集成） | Grant→Suspend 周期记录 elapsed |
| 专注目标：解析校验边界 | 30/1440/非法回落 240 |

## 9. 实施顺序（每步一提交）

1. `feat(scene): 统计统一化——FocusHistory 8 列兼容扩展 + DailyStats + DailyCare 掌权记录`
2. `feat(dev): 编译统计真实起止——DevFocus buildStartTicks + 今日编译行 + 趋势合计/目标进度`
3. `feat(daily): 详情页日常趋势卡 + 维护到期倒计时`
4. `ux(set): 设置页每日专注目标输入行 + 三语文案`
5. 收口：矩阵重摄 + demo 图刷新 + 三语 README 计数同步 + 规格回写

## 10. 明确不做（YAGNI）

- 不做趋势导出/CSV 复制（Read 工具与文件本身可读）。
- 不做专注目标达成提醒/桌面通知（目标只是参照线）。
- 不做日常侧分心概念（语义不成立）。
- 不做维护到点的前台提醒（既有 30 分钟轮询自动执行语义不变）。
- 不重命名 focus-history.tsv（类名/文件名保留，避免迁移；语义在注释与规格标明）。

## 11. 实施偏差记录（实施完成后回写）

（暂空）

## 11. 实施偏差记录（实施完成后回写）

- **自测 287→291（+4）**：8 列历史合并兼容（扩充既有用例）、日常掌权记录、编译时长记账、目标解析边界、编译起止集成。
- **DailyTrendTotalsText 初版用了无通知自动属性**——WPF 绑定不刷新，已改为字段 + SetProperty（开发侧 FocusTrendRow 系列一致）。
- **FormatSeconds 补零分钟省略**：「26 小时 0 分钟」→「26 小时」，全局共用格式化。
- **日常趋势标签用小时格式**：日常家族整天掌权时分钟数过大（558m），改用 9h18m 式标签；专注侧保持分钟（通常 <8h）。
- **维护倒计时从未运行时不显示行**（空串隐藏），损坏时间戳同样隐藏——避免误导性文案。
- **重摄收口**：矩阵 76 张 + demo-dev-focus 刷新 + 新增 demo-daily-trend（8 列种子数据：专注/日常/编译三场景同文件）；演示数据截完即还原。

## 12. 审查迭代（2026-09-12 同日）

代码审查发现 **1 个真缺陷 + 2 个体验问题**，全部修复并过门禁（291→292，+1 守卫纯函数测试）：

1. **【缺陷】编译起钟边界**：`OnInitialProcess`（初始扫描）加入编译进程不设 `buildStartTicks`，该进程在第一批事件里结束时以起点 0 计出天文时长（距今约 2000 年的 tick），写爆今日编译统计与 TSV。修复：初始扫描起钟 + 时长计算提取纯函数 `BuildEndedElapsed`（无起点/回拨一律 0）。
2. **【体验】目标进度滞后**：趋势刷新签名未含 `GoalMinutes()`，设置页改目标后详情页进度行要等下一次口径变化。修复：签名纳入目标（注册表读，频率同今日键）。
3. **【体验】维护倒计时滞后**：`HealthNextDueText` 在 `healthZoneLoaded` 门内，页面停留期间不随时间推进。修复：移到门前每次刷新重算（纯注册表读）。

**测试环境教训**：本机常驻约 11 个编译类进程（msbuild 等），「初始扫描 + 同批结束触发 1→0 转换」的集成测试假设不成立（其他编译进程撑住集合）——环境相关集成断言一律换成确定性纯函数测试；多条目集合语义（并发编译并存）由实现天然保证。
