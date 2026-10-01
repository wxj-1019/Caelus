# 开发专注：编译活性门 + 副作用收敛器 设计规格

- 日期：2026-10-02
- 状态：待评审
- 来源：2026-10-01 功能审计 Core P1-1（编译检测误报）+ P1-2（副作用快照式执行），合并立项
- 涉案主文件：`src/Core/Scenario/DevFocus.cs`、`src/Core/BuildCatalog.cs`

## 1. 背景与问题

### 1.1 P1-1 编译活性 = 纯进程名匹配

`BuildCatalog.IsMatch(name)` 是唯一判定（`DevFocus.cs:209`、`:347`），Started 事件即入
`activeBuildPids`，无 CPU 活性、无空闲超时校验（对比：IDE 来源有可见窗口门槛）。两类用户可见症状：

1. **常驻空转节点长期误掌权**：VS 的 nodeReuse MSBuild 节点编译后存活约 15 分钟零 CPU，
   托盘长期显示「编译优化中」，SysMain/WSearch 整段被停。
2. **通用 CLI 高频误触发**：默认名录含 `git/git-bash/git-cmd/docker/docker-buildx`
   （`BuildCatalog.cs:26-29`，注释自述「Git 大规模 IO 操作」「Docker build/run 都触发」）——
   但名称匹配无法区分 `git clone` 与 `git status`：终端用户每敲一条 git 命令就产生一次
   「编译开始→结束」气球 + 服务停启 + 一条编译会话统计（`DevFocus.cs:291-303` →
   `FocusStats.RecordBuild`），统计爆表。

### 1.2 P1-2 副作用是 Grant 时刻的快照式执行

`Grant()`（`DevFocus.cs:489-503`）按**当时**的 build/focus/ide 快照决定是否施加副作用；
仲裁器只在活性翻转时调 Grant（`ScenarioBase.RecomputeActivity` / `ScenarioArbiter`），
IDE 可见使 reported 持续 true。后果：

1. 「开着 VS Code → 再跑大编译」：`SvcPause.Activate`、`SweepBuildSuppression` 不生效
   （`ReconcileTick` 只补压制 sweep 与 IDE 提优，`:699-721`；且 IDE-only 授予时
   `StartReconcileTimer` 根本没启动，`:502` 条件 `build || focus`）。
2. 「开着 IDE 再点开专注模式」：`Notif.Quiet` 不生效——功能像随机失灵。
3. 反向同样成立：编译结束但 IDE 仍开着（场景不失活、不 Suspend）→ 服务暂停/后台压制
   一直保持到 IDE 关闭，超出「编译期」语义。

## 2. 目标与非目标

**目标**

- G1 编译活性判定反映「真实编译进行中」：空转常驻节点不再维持掌权与统计。
- G2 通用 CLI（git/docker 家族）不再作为默认触发信号；依赖者有显式补回路径。
- G3 副作用与活性来源的关系在掌权全程持续收敛：任何来源的起止都即时反映到
  服务暂停/通知静默/后台压制/校正节拍。
- G4 全程可单测：判定逻辑纯函数化 + CPU 采样/超时可注入，沿用现有测试钩子文化。

**非目标（YAGNI）**

- 不做磁盘/网络 IO 采样（CPU 进度已足够区分空转节点；IO 活性的误报面更大）。
- 不做编译进程树/父子归属分析（csc 等子进程本就在名录，集合语义即"或"）。
- 不动仲裁器与优先级模型、不动 SharedEffectClaim/交接直通协议。
- 不改 FocusStats 存储格式（仅受益于误报消失，不新增字段）。
- 不处理「游戏抢占期间编译集合继续计时」的统计口径（另行观察，见 §7）。

## 3. 设计 A：编译活性门

### 3.1 名录收紧（治 P1-1 症状 2）

从 `BuildCatalog.Names` 默认名录移除：`git`、`git-bash`、`git-cmd`、`docker`、`docker-buildx`。

- 理由：git 家族的绝大多数调用（status/diff/log）秒级返回，却被判为编译会话；
  docker CLI 本身只是守护进程的瘦客户端，build 的真实工作发生在 dockerd/容器内，
  CLI 进程的 CPU 与编译无关。两家的「重 IO 场景」（clone/gc、镜像构建）收益远小于
  高频误触发代价。
- 补回路径：既有 `CustomList`（`CustomBuildProcs` 注册表键，设置页可编辑）零代码补回，
  在设置页自定义编译进程输入框的提示文案中点名「如需 git/docker 触发请自行加入」。
- 保留 `pnpm/yarn/bun/nx/lerna/just/uv/poetry` 与测试运行器：install/test 本就是
  「开发专注」的合理时刻，且真实构建的子编译器进程独立命中名录。

### 3.2 CPU 进度驻留门（治 P1-1 症状 1）

每个编译 pid 维护 `lastProgressTicks`（最近一次观测到 CPU 前进的时刻）与上次采样值：

- **采样**：专用轻量 `Timer`，周期 5 秒，仅当 `activeBuildPids` 非空时运行（空↔非空
  转换启停，不与 ReconcileTimer 合并——后者只在掌权期运行，本门必须在未掌权时也能
  收掉空转集合，否则「残留节点 → 场景永不失活」死锁）。
- **读数**：`Process.TotalProcessorTime`；**可注入** `internal static Func<int, TimeSpan?>
  CpuProbe`（生产实现 try/catch 返回 null）。**不可读进程保守保留**（不更新进度也不
  淘汰——防误杀提权/跨会话真编译；本应用常态提权，不可读是少数）。
- **淘汰判定（纯函数，可单测）**：
  `internal static bool ShouldDropForIdle(long lastProgressTicks, long nowTicks)` ——
  `lastProgressTicks <= 0` 恒 false（未起钟不淘汰）；否则静默超过
  `BuildIdleDropSeconds`（默认 20 的 `internal static int`，测试可覆写——既有探针测试
  置 0 关闭淘汰，finally 还原）即淘汰。
- **入场策略**：事件 Started → `lastProgressTicks = now`（乐观：真编译不该为 20 秒的
  确认延迟失活）；**初始扫描**（`OnInitialProcess`）→ `lastProgressTicks = now − 15s`
  （悲观：只给 5 秒证明自己在编译——开机残留的 nodeReuse 节点最多造成 5 秒假阳性，
  而真实进行中的开机编译延迟 ≤5 秒收权，两头代价都封顶）。
- **淘汰路径**：`internal void RemoveIdleBuilds(long nowTicks)`——锁内按判定收集淘汰
  pid、移出集合，若集合因此 空←非空，走与 `NotifyProcessChanges` 相同的收尾：
  `buildEndedElapsed` 统计、`bal.buildend` 气球、`AnyActive` 翻转时向仲裁器报告、
  若仍掌权调 §4 收敛器（降沿）。被淘汰 pid 若随后真的又开始烧 CPU（Started 不会再来，
  进程没死），由采样器把它按「新进 CPU 的已知 pid」重新入场——即：**pid 不从采样
  记忆中删除，只从活性集合中移出**；采样器发现非集合内 pid 有 CPU 前进且仍在世，
  重新走乐观入场。这覆盖「节点复用被唤醒」的场景。

### 3.3 行为对照表

| 场景 | 现状 | 改后 |
|---|---|---|
| VS 关闭后 nodeReuse 节点存活 15 分钟 | 整段误掌权+停服务+计时长 | ≤20 秒后集合清空、场景失活、服务恢复 |
| 每条 `git status` | 气球+服务停启+统计一条 | 完全不触发（不在名录） |
| 开机残留 msbuild 节点被初始扫描 | 立即误掌权 | ≤5 秒宽限后淘汰 |
| 真编译（csc/msbuild 持续烧 CPU） | 即时掌权 | 即时掌权（不变） |
| 编译中 IO 长等待（还原/网络拉包） | 不受影响 | 全家族 pid 20 秒零 CPU 才收（误杀需全集合静默） |

## 4. 设计 B：副作用收敛器

### 4.1 期望态表（掌权期间持续成立）

| 副作用 | 期望条件 | 升沿动作 | 降沿动作 | 现状缺陷 |
|---|---|---|---|---|
| 服务暂停 SvcPause | `build`（活性门后集合非空） | `Activate(OwnerDevFocus)` | `Restore(OwnerDevFocus)` | Grant 后 build 起始不补做；build 结束 IDE 仍在时不释放 |
| 通知静默 Notif | `focus` | `Quiet(OwnerDevFocus)` | `Restore(OwnerDevFocus)` | Grant 后 focus 打开不补做 |
| 后台压制 suppression | `build || focus` | `SweepBuildSuppression()` | `core.ReleaseReason(Build)` | 升沿已由 tick 补（但 IDE-only 授予时无 tick）；降沿不存在（拖到 Suspend） |
| 校正节拍 timer | `granted`（无条件） | `StartReconcileTimer()` | （Suspend 停，不变） | IDE-only 授予时不启动 |
| 编译提优 boost | `build` | `BoostBuildProcesses()`（幂等） | （Suspend 统一还原，不变） | 已有 newlyBuilt 补做，收敛器再兜一层 |

### 4.2 实现

- 新私有方法 `ReconcileSideEffects()`：锁内读 `granted/build/focus` 快照，与
  `svcPauseApplied/quietApplied/suppressApplied` 标志位比对，锁外按上表收敛。
  `SvcPause/Notif` 的 owner 语义使 Activate/Restore 幂等安全（`SharedEffectClaim`）；
  suppression 升沿 sweep 本身幂等（Acquire 对已压进程返回 AlreadyThrottled）。
- **调用点**：`Grant()`（替换现有快照式四段为一次收敛调用 + 日志）、
  `NotifyProcessChanges()`（集合变化且仍掌权时）、`SetFocusMode()`（掌权时）、
  `ReconcileTick()`（替换现有部分逻辑）、`RemoveIdleBuilds()`（淘汰致降沿时）。
- `StartReconcileTimer` 在 Grant 无条件启动（IDE-only 也跑：30 秒一次的
  RefreshIdeVisible/RecomputeActivity/收敛 no-op 成本可忽略）。
- **行为改进声明**：编译结束 IDE 仍在 → 服务恢复、后台压制解除（原要等场景失活）；
  focus 关闭编译继续 → 通知恢复。这是语义修正而非回归：副作用的挂靠条件本来
  就是 build/focus，不是「场景活着」。
- **Suspend 语义不变**：全量还原 + 交接直通（gameKeepsSvcPause/NotifQuiet）照旧，
  收敛器标志位在 Suspend 复位。收敛器的降沿释放走 owner 参数，不碰交接路径。

## 5. 测试策略（自测门禁，沿用现有范式）

1. **纯函数**：`ShouldDropForIdle` 真值表（未起钟/恰在阈值/超阈值）；名录负断言
   （git/docker 五名不再 IsMatch；CustomList 补回后恢复命中）——扩
   `TestBuildCatalogExpandedTools`。
2. **活性门集成**：构造 DevFocus + `CpuProbe` 钩子（脚本化返回值）+ 伪进程事件批——
   断言：事件入场即活；`RemoveIdleBuilds(now+21s)` 后集合空、FocusStats 记一条
   （FocusStats 用临时目录重定向，参照 `HealthHistory.FilePath` 模式）；不可读（probe
   返回 null）不淘汰；已淘汰 pid 复烧 CPU 重新入场。
3. **收敛器**：真机往返模式（参照 `TestDevFocusGrantAndRelease` 的服务保存/还原与
   LOL/ACE 守卫）：IDE-only 授予 → 喂 build 事件 → `SvcPause.HeldBy(devfocus)` 为真、
   服务真实暂停（不可用环境 Skip）；build 清空 IDE 仍在 → HeldBy 翻 false、服务恢复；
   focus 中途开 → `Notif.HeldBy` 为真。
4. **既有测试适配**：所有用 `StartNamedProbe` 起 msbuild 探针的测试，setup 置
   `internal static int BuildIdleDropSecondsOverride`（0=关闭淘汰；finally 还原）——
   心跳探针近零 CPU，不关会被 20 秒门误杀。此插桩同时是新测试的节流阀。
5. **真机验收**：常驻假 msbuild（`--test-heartbeat-probe`）空转 → ≤20 秒托盘收权、
   服务恢复；真跑一次编译 → 即时掌权、结束即恢复；git status 全程无气球。

## 6. 风险与取舍

- **20 秒阈值的误杀面**：需全家族 pid 连续 20 秒零 CPU 才收权——Roslyn 编译期 csc
  节点持续有 CPU；纯 msbuild 编排器空转期由子进程顶住集合。极端冷缓存恢复场景
  （>20 秒零 CPU 后编译继续）会被拆成两段会话，统计略碎但无功能损失。
- **乐观入场的 20 秒假阳性**（事件路径）：一次误触发的代价收敛为 ≤20 秒的服务暂停
  （引用计数保证游戏侧不受影响）+ 一条短统计；对比现状的 15 分钟级误掌权是数量级改善。
- **git/docker 移除的迁移**：依赖 git 触发的用户（若有）需在设置页 CustomList 补回；
  发布说明写明。属行为变更，版本说明条目必须写。
- **收敛器与 Suspend 的竞态**：收敛器全程锁内取快照、锁外动系统（与现有 Grant/Suspend
  同纪律）；Suspend 先翻 granted=false，收敛器在读到 false 后不再施加升沿；降沿动作
  幂等（owner 语义），Suspend 的全量还原兜底。

## 7. 验收标准

- A1 名录负断言 + CustomList 补回断言过；全部既有自测过（含适配）。
- A2 CpuProbe 脚本化集成测试过：空转淘汰/不可读保留/复活重入/统计落点全断言。
- A3 收敛器真机往返测试过（或环境性 Skip 且本机真机验收覆盖）。
- A4 真机验收三场景（§5.5）通过，`dev.cmd test` 门禁全绿、计数同步三语 README。
- A5 规格/计划偏差随落地 commit 回写（含本节 §2 非目标里「游戏抢占期编译计时」
  若实施期发现需处理，单独立项）。

## 8. 实施偏差回写

- **2026-10-02 落地回写**（执行提交 60bcde3..[HEAD]，子代理驱动 6 任务 + 每任务规格/质量双审）：
  1. §3.1 CustomList 往返测试从 TestBuildCatalogExpandedTools 抽为独立注册测试 TestBuildCatalogCustomList——质量审查发现原计划内联位置在 `Settings.UseTransientStoreForCurrentProcess()` 之前注册，往返写真实 HKCU；抽出后注册于瞬态存储区，计数基线 295→296（后续期望随之 +1）。
  2. §3.2 统计落点收归 `BuildSetBecameEmpty` 单一记账点——原计划空转淘汰路径漏记统计（测试断言 FocusStatsBuildN==1 与实现矛盾）；同时该收尾末尾需 `RecomputeActivity()`（原计划缺失则淘汰路径永不向仲裁器报失活），收敛器调用因依赖顺序挪至 Task 3 落地。
  3. Task 2 三处计划文本修正（实现者发现、审查核准）：CPU 采样与清死遍历 `buildPidName.Keys` 而非 `buildPidCpuMs.Keys`（入场时 cpu 表被 Remove，遍历它=新入场 pid 永不被采样、20 秒全灭）；`RemoveIdleBuilds` 加部分淘汰守卫（多 pid 并存时不得提前收尾）。
  4. §3.2 第 2 条「不可读不淘汰」的确定性实现：`buildPidUnreadable` 标记集（每拍锁内重建）+ 句柄死活甄别（OpenProcess 可开且 !StillActive 才判死清出，句柄拿不到=受保护进程保守标记），`RemoveIdleBuilds` 跳过标记 pid——规格审查发现原计划仅「不更新进度」仍会被静默淘汰。
  5. Task 2 并发加固（质量审查三轮）：锁内 `buildSetEmptied` 布尔消 TOCTOU；采样 Timer 回调包 try/catch（对齐 ReconcileTick 纪律）；`samplingFlag` Interlocked 重入护栏（重叠拍清标记竞态）；初始扫描宽限改 `graceSec = max(阈值−5, 0)` 去魔法数。
  6. §4.1 补充：校正节拍在收敛器之外保留**长编译期增量追压**（每 30 秒幂等重扫）——期望态表只写了升沿一次性 sweep，旧 ReconcileTick 的持续追压行为经规格审查点名后恢复，避免长编译中途新起的后台进程逃逸。
  7. Task 3 质量修补：三处收敛调用点从 `lock(sync){...}` 解包为「锁内读布尔、锁外收敛」（全进程枚举不进场景锁）；SvcPause 升沿补 try 对称故障隔离；记账位加「最终一致快照 + 三层兜底」契约注释；测试补 Notif 降沿（focus 关、build 在场）与 Suspend 后 `PidsWith(Build)==0` 确定性断言。
  8. §5.4 空满足：计划为 9 处探针测试预备的 `BuildIdleDropSeconds=0` 插桩实际不需要——心跳探针（--test-heartbeat-probe）80ms 一跳持续烧 CPU，活性门判定其「在编译」，既有测试零改动全绿；插桩仍作为 CpuProbe/阈值覆写机制的存在依据保留在实现里。
  9. §5.5 真机验收三场景全过：①空转 msbuild（cmd 副本跑 ping）——事件入场秒级掌权+WSearch 暂停+压制 77 进程，**25 秒后活性门精确淘汰**（乐观 20s+采样对齐）、服务恢复、空转进程存活不再持权（nodeReuse 语义正确）；②真实 MSBuild 编排编译——5 秒内掌权（含全量 sweep）、9.1 秒会话记账、结束即恢复无残留；③git status/log/diff 三连——零编译反应。
  10. 验收附发现（既有行为，非本项目回归）：**裸 csc 亚秒级编译不可见**——对新旧构建 A/B（83e5e95 基线 worktree 同机同法同现象）：BuildCatalog 成员的事件走 ProcNotify 身份核验（WpfRuntime.cs:549 对名录成员 CaptureStartIdentity），短命进程在核验/交付窗口内退出则事件名留空被跳过；编排器型构建（msbuild/dotnet 等 orchestrator 全程存活）不受影响。留待后续观察，不在本规格范围。
  11. 质量审查遗留 Minor（未修，后续清理清单）：SampleBuildCpu 记忆清死段锁内系统调用（应锁内快照锁外甄别）、三处停用路径清理去重抽方法、`deadActive` 更名 `deadConfirmed`、测试 FocusHistory 重定向看齐 TestFocusBuildRecorded、§3.2 淘汰路径不弹 bal.buildend 气球（事件路径会弹，淘汰路径静默收权）。
  12. 门禁：TOTAL 299 / PASS 294 / FAIL 0 / SKIP 5（SKIP 为 LOL/ACE 运行与 CPU Sets 环境项浮动）；三语 README 计数 295→299 同步。
