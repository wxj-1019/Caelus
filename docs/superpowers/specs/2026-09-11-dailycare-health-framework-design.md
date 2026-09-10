# 日常养护补全：维护动作框架（HealthAction Framework）

日期：2026-09-11
状态：已实施（2026-09-11，main 分支 8dbc9bb..dddafce + 收口提交；自测 254→276 全绿）

## 0. 现状与目标

**现状**：日常养护是三场景中最轻量的一个——DailyCare（家族/电池双触发 + Eco/Restrained 双档压制 + 家族提优）加 HealthCare（独立定时调度：着色器缓存 >64MB 清理 + 启动项 diff）。最大的体验黑洞是**做了事用户看不见**：清理量、启动项新发现只进日志，无面板、无历史、无「立即执行」；启动项审查只读不能处理；日常名录硬编码不可自定义；电池档只压后台+弹文字建议，不真正联动电源。

**目标**：把维护能力升级为「维护动作框架」，并在其上落地四件事——

1. **结果可见 + 立即执行**：详情页维护中心面板（最近结果 + 历史 50 条 + 手动触发按钮）
2. **启动项可处理**：新发现启动项面板勾选 → 禁用（只禁不删）→ 可一键还原
3. **名录可自定义**：`DailyCatalog.CustomList`，照搬 `BuildCatalog.CustomList` 注册表模式
4. **电池联动电源**：脱电切电源滑块 DC 侧到续航档，回电/挂起/崩溃自愈还原

**范围**：仅 WPF 宿主界面（核心逻辑保持宿主无关，WinForms 宿主照旧能用场景本身，但新面板/按钮不做 WinForms 版）。游戏模式、开发专注模式本次不动。

## 1. 决策记录（brainstorm 确认项）

| 决策点 | 选择 | 落选项 |
|---|---|---|
| 优先方向 | **日常养护优先**——缺口最大，功能完善与体验提升在此是同一件事 | 开发专注优先 / 体验优先 / 三者整体规划 |
| 能力范围 | **四项全要**（结果可见+立即执行 / 启动项可处理 / 名录可自定义 / 电池联动电源） | 任选子集 |
| 界面范围 | **只做 WPF**——核心逻辑宿主无关，新 UI 不做 WinForms | 双界面同步（+30~40% 工作量） |
| 推进方式 | **方案 3 · 维护动作框架化**——抽象 `IHealthAction`，动作注册制，调度/UI/历史与具体动作解耦 | 方案 1 单 spec 平铺 / 方案 2 拆两轮 |
| 电池联动档位 | **A · 滑块级**——脱电切 DC 侧滑块到「更长的续航」，跟随现有「电池增强」开关（默认开） | B 滑块+温和计划参数 / C 完整省电档（含 Isolated 压制） |
| 启动项安全模型 | **只禁不删 + 面板手动勾选才执行**——自动调度只 Analyze 报告；系统/微软签名项打「不建议」标且默认不勾选 | 自动禁用 / 物理删除 |
| 详情页布局 | **A · 单页分区**——维护中心卡 + 启动项审查卡纵向堆叠进现有详情页 | B 页签分段 / C 摘要条+对话框 |

## 2. 框架架构（`src/Core/Scenario/Health/` 新目录）

> 注意：wpf 与 src 的 csproj 均为逐文件清单，新文件需补 `<Compile>` 登记（沿袭 message-dialog spec §9 的教训）。

### 2.1 动作接口（C# 5 语法，无新语言特性）

```csharp
internal interface IHealthAction
{
    string Id { get; }            // "shader-cache" / "startup-audit"
    string TitleKey { get; }      // Lang 键（标题）
    string DescKey { get; }       // Lang 键（说明）
    bool AllowAuto { get; }       // 定时调度是否允许自动 Execute
    bool CanUndo { get; }         // 是否可还原
    HealthReport Analyze();                       // 只读扫描：发现项/体量/明细行
    HealthResult Execute(string[] selectedIds);   // 处理：释放字节/处理条数/跳过原因
    bool Undo(string recordId, out string error); // 可逆动作还原
}
```

- `HealthReport`：动作 Id、发现条数、体量（字节，无则 0）、明细行列表（启动项名/命令/来源等，纯文本+结构化 Id）
- `HealthResult`：动作 Id、结局（success / failed / skipped）、释放字节、处理条数、失败原因（null 表示无）
- `selectedIds`：手动执行时用户勾选的明细 Id；`null` 表示全部（仅 AllowAuto 动作的自动路径使用）

### 2.2 三个框架服务

- **`HealthActionCatalog`**——动作注册表。`Register(IHealthAction)` + `All` 枚举。当前注册 2 个；以后加动作 = 新类 + 注册一行，调度器/历史/UI 零改动。
- **`HealthRunner`**——唯一执行入口。`Run(HealthTrigger trigger)`：逐个动作 `Analyze()` → Auto 路径仅 `AllowAuto=true` 的动作继续 `Execute(null)`，Manual 路径全部动作执行（启动项动作只执行面板已勾选项，经 `selectedIds` 传入）→ 每条结果写 `HealthHistory` → 聚合返回供 UI 刷新。**单动作异常故障隔离**：try/catch 标记该动作 failed，不中断其他动作与调度节拍。
- **`HealthHistory`**——追加式 TSV，`Paths.Data` 目录下 `health-history.tsv`（`Paths.Init` 已知会便携/漫游两种落点）。复用 `StartupAudit` 的 `Esc/Unesc` 单趟转义（抽成内部共享助手，StartupAudit 改调它，行为不变——有现有转义回归自测保底）。保留**最近 50 条**，超出截断；写入走 `AtomicFile` 防半截文件。记录字段：时间戳、触发源（Auto/Manual/Undo）、动作 Id、结局、释放字节、处理条数、明细摘要、还原负载（启动项禁用的完整原值快照，见 §3.2）。

### 2.3 调度接线

现有 `HealthCare` 的定时逻辑（启动 2 分钟首判 + 每 30 分钟到点 + `IsDue` + 游戏进行中 `ShouldDefer` 让路 + 开删前二次让路）改造为 `HealthRunner.Run(Auto)` 的一个触发源，调度骨架与让路语义不变。手动「立即执行」是第二个触发源，**不受 `IsDue` 与频率限制**，但仍遵守游戏让路（游戏掌权时按钮禁用并提示原因）。

## 3. 首批两个动作

### 3.1 ShaderCacheAction（迁移）

- 包装现有「着色器缓存 >64MB 才清」逻辑（`HealthCare.cs:94-102` 区域），行为阈值不变
- `AllowAuto = true`（维持现状的自动清理），`CanUndo = false`
- Analyze 返回当前缓存体量；Execute 返回释放字节

### 3.2 StartupAuditAction（升级）

- Analyze = 现有 diff（HKCU/HKLM Run 键 + 启动文件夹 vs 基线），产出新发现项明细（每项一个稳定 Id：来源+值名/文件名的哈希）
- `AllowAuto = false`——自动到点**只 Analyze 报告**，新发现照旧写 `HealthStartupNews` 并上详情页
- Execute(selectedIds) = **禁用勾选项**：
  - Run 键项：键值移到备份键 `HKCU\Software\Caelus\DisabledStartup`（保留原键路径/值名/数据三元组到历史记录的还原负载）
  - 启动文件夹项：.lnk 移到 `Paths.Data\DisabledStartup\` 备份目录
  - 完整原值快照写入历史记录本体——备份键/目录被外部清掉时仍可从历史还原信息
- Undo(recordId) = 按还原负载放回原位；目标位置被占用（同名值已存在）则失败并给出原因，不覆盖不崩溃
- **系统保护**：微软签名/系统项标「不建议禁用」，UI 默认不勾选；`Execute` 内部再校验一次，即使 UI 被绕过也禁不了
- 幂等：对同一项重复禁用，第二次返回 skipped

## 4. DailyCare 侧两项增强（不走框架）

### 4.1 名录自定义（DailyCatalog.CustomList）

- 照搬 `BuildCatalog.CustomList` 模式：注册表 `CustomDailyProcs`，一行一个进程名，坏行容错跳过
- 与内置 13 项名录合并生效；内置名录不变
- 设置页 ZoneDaily 加编辑入口（复用现有分心清单/开发服务清单的编辑 UI 模式）

### 4.2 电池联动电源滑块

- 扩展 `PowerOverlay`：新增「续航档」写入方向（现状只有「最佳性能」档）；DC 侧滑块脱电时写续航档值，**快照格式沿用现有 "AC|DC" 约定**（DC 为原始串，缺失记空 → 还原时删值归位）
- 触发：DailyCare 检测到脱电且「电池增强」开关开（`DailyCareBatteryOn`，现状默认开）→ 激活；回电立即还原（沿用现有「插拔电立即重扫」通道，不等 30 秒节拍）
- **互斥**：占用登记 `SharedEffectClaim(OwnerDailyCare)`；游戏掌权时 DailyCare 被仲裁器抢占挂起 → 滑块随挂起还原，游戏模式的 PowerOverlay 使用不受影响
- **崩溃自愈**：复用 `PowerOverlay.HealFromCrash` 现有链路，启动时还原
- DailyCare 总开关关闭或电池开关关闭时不动作

## 5. WPF UI（布局 A · 单页分区）

### 5.1 日常养护详情页（`ScenarioDetailView.xaml` Daily 页）

在现有「状态横幅 + 触发源卡」之下、调度提示卡之上，插入两张新卡：

- **维护中心卡**：上次维护结果摘要（清理量/新发现数 + 时间 + 触发源）+「立即执行」按钮（执行中显示进行态；**60 秒节流**防连点；游戏掌权时禁用并提示）+ 历史列表（最近在前，最多 50 条，含 Undo 记录）
- **启动项审查卡**：新发现列表（勾选框；系统/微软项带「不建议」标且默认不勾）+「禁用所选」按钮 + 已禁用分组（一键还原）；空态显示「暂无新发现」

`ScenarioViewModel` 增加对应绑定（维护摘要文本、历史列表、启动项两组列表、按钮命令与可用态）。空历史/空发现均有空态文案，不显示半空卡片。

### 5.2 设置页 ZoneDaily（`SettingsView.xaml`）

- 日常名录自定义编辑框（紧邻现有日常场景/电池增强开关）
- 电池增强开关的说明文案补充「脱电时电源滑块切续航档」

### 5.3 多语言

新文案键全部登记 `src/Platform/Lang.cs`；`SelfTests.LangKeys` / `SelfTests.LangCoverage` 会卡住「被引用但未定义」的键。键命名沿用现有 `scn.daily.*` / 对话框 `bal.*` 前缀风格。

## 6. 数据流

```
定时：HealthCare 调度（IsDue + 游戏让路 + 开删前二次让路）
手动：详情页「立即执行」按钮（60s 节流；游戏掌权禁用）
        │                │
        └───────┬────────┘
                ▼
        HealthRunner.Run(trigger)
          逐动作 Analyze()
          Auto → 仅 AllowAuto 动作 Execute(null)
          Manual → 全动作执行（启动项仅执行 selectedIds）
                ▼
        HealthHistory 追加（TSV，50 条截断，AtomicFile）
                ▼
        详情页面板刷新（摘要 + 历史 + 启动项新发现）

启动项禁用：面板勾选 → StartupAuditAction.Execute(ids)
  → 移键值/移 lnk → 历史记录（含完整还原负载）→ 已禁用分组可 Undo
```

## 7. 错误处理与安全

| 风险 | 对策 |
|---|---|
| 单动作抛异常 | Runner 故障隔离，标记 failed 继续其余动作，调度节拍不受影响 |
| 历史文件半截/并发写 | `AtomicFile` 原子写；追加+截断在同一写操作中完成 |
| 还原信息丢失 | 启动项禁用的完整原值快照随历史记录本体持久化，不依赖备份键存活 |
| Undo 目标被占用 | 失败并报告原因，不覆盖现有值、不崩溃 |
| 误禁系统项 | UI 默认不勾 + 「不建议」标；Action 内部二次校验兜底 |
| 游戏/日常滑块打架 | `SharedEffectClaim` 互斥 + 仲裁器抢占挂起即还原 |
| 崩溃后滑块残留 | `PowerOverlay.HealFromCrash` 启动还原（现有链路） |
| 按钮连点 | 60 秒节流 + 执行中进行态禁用 |

## 8. 测试策略（TDD，对齐 254 项自测门禁）

| 测试组 | 覆盖点 |
|---|---|
| `SelfTests.HealthAction`（新） | Catalog 注册/枚举；Runner 聚合顺序；单动作 failed 隔离；Auto 只跑 AllowAuto、Manual 全跑；selectedIds 过滤；历史追加/50 条截断/TSV 特殊字符往返/Undo 记录 |
| `SelfTests.HealthCare`（扩充） | StartupAuditAction：禁用快照完整性、还原往返、系统项拒绝、lnk 移动还原、重复禁用幂等；调度改接 Runner 后 IsDue/让路语义不变 |
| `SelfTests.DailyCare`（扩充） | CustomDailyProcs 合并生效与坏行容错；电池联动：脱电切档→回电还原、抢占挂起还原、开关关闭不动作、快照 "AC|DC" 格式兼容 |
| `SelfTests.LangKeys/Coverage` | 新键登记完整性（现有测试自动覆盖） |
| 真机验证 | dev.cmd 门禁全绿后：手动立即执行一次；启动项禁用→还原往返一次；脱电/回电滑块切换观察一次 |

## 9. 实施顺序（供 writing-plans 参考）

1. 框架三件套（Catalog/Runner/History + Esc 助手抽取）+ `SelfTests.HealthAction`
2. ShaderCacheAction 迁移（HealthCare 调度改接 Runner）
3. StartupAuditAction 禁用/还原 + 自测扩充
4. 详情页两张新卡 + ScenarioViewModel 绑定 + Lang 键
5. DailyCatalog.CustomList + 设置页编辑入口
6. PowerOverlay 续航档扩展 + DailyCare 接线 + 自测
7. 真机验证三项 + README 三语自测计数同步

## 10. 明确不做（YAGNI）

- 启动项**物理删除**（只禁不删是安全底线）
- 框架动作的市场/插件化（注册表硬编码注册即可，不做发现机制）
- WinForms 侧新 UI（核心逻辑宿主无关已够用）
- 开发专注模式的缺口补齐（IDE 名录自定义、专注统计历史等）——下一轮单独立项
- 电池档的电源计划参数写入（方案 B/C 落选项，观察滑块档效果再说）

## 11. 实施偏差记录

- Undo 接口为单行负载 `Undo(string, out string)`；历史为不可变事件日志；「已禁用」真值来自备份存储枚举（IHealthAction.ListDisabled）
- Analyze 纯函数；新闻+基线提交走 IHealthAutoCycle.OnAutoCycle（Runner Auto 路径调用）
- HealthHistory.FilePath getter 在 Paths.Data 为 null 时退 Path.GetTempPath()（自测进程不调 Paths.Init）
- HealthRunner.UndoSingle 负载行缺 TAB 护栏；ShaderCacheAction Skipped 摘要用 FmtBytes(Threshold)
- StartupAuditAction：Undo 来源白名单 + lnk 名字防目录逸出；死代码 BackupRead 未建
- VM 属性名 HealthHistoryRows（避开静态类撞名）；「不建议」徽标用既有 WarnTag 模式；门控 RefreshHealthRunGate + detail VM 自挂 2s DispatcherTimer（source 静默时 Changed 不触发）；历史失败行 Style Setter + DataTrigger（本地值压触发器）
- DailyCare：RefreshPowerStateCore(bool) 抽离供测试注入电池源；SetFamilyVisibleForTest 钩子；bal.daily.batt 气球文案对齐自动化行为
- PowerOverlay：HealFromCrash 续航档段 lock(lk) 纪律；ApplyBatterySaverIfNeeded 锁内复读 grantedFlag
- 续航档快照实际用独立键 `PowerOverlayDcSaverSnap` + `\x1f` 哨兵，而非 §4.2 字面的「沿用 AC\|DC 约定、缺失记空」（空串无法区分无快照/原本无值，哨兵是正确选择）
- 启动项发现项 Id 实际用明文 `Source|Name`，而非 §3.2 说的哈希（更可读、TSV 经 Esc 安全）
