# 场景扩展：游戏优化 → 游戏 + 开发专注 + 日常优化

## 目标

把 Caelus 从单一游戏优化工具扩展为三场景调度工具：

1. **开发专注（DevFocus）**：编译资源调度深化（真后台压制）+ 开发者专注模式（免打扰）+ IDE 性能优化
2. **日常优化（DailyCare）**：日常应用场景调度（浏览器/Office/会议）+ 系统健康维护 + 电池/能效优化
3. **三场景严格优先级仲裁**：游戏 > 开发专注 > 日常优化，高优先级激活时低优先级挂起（还原副作用），退出后恢复

## 背景

### 现状架构

- `GameMode`（1379 行 × 8 文件 partial class）：硬编码游戏会话，自有 worker 线程，会话检测深度耦合游戏机制（GPU 证据选举、启动器家族、渲染器 PID 追踪）。**稳定核心，本设计零改动**
- `BuildWatch`（175 行事件驱动窄类）：检测编译进程 → `SvcPause` 暂停索引服务 + 编译器提至 HIGH + IO 3。**不做 SuppressionCore 后台压制**（文件头注释与日志文案声称"压制后台"，实际代码未实现；`SuppressReason.Build=4` 位已定义但全项目无人使用，是预留扩展点）
- 互斥现状：GameMode ↔ BuildWatch 靠注入的 `isGameActive` 布尔委托，三方以上场景并存需要仲裁器
- 共享基础设施：`ProcNotify.BatchChanged` 事件广播、`SvcPause`（自带防重入 + 注册表崩溃恢复标志）、`SuppressionCore`（位标志多原因引用计数）、`BackgroundPressureController`（纯算法，与模式无耦合）、`Notif`、`Settings`、`Logger`

### 需求确认（2026-08-14 用户确认）

- 开发专注：编译调度深化 + 专注模式 + IDE 优化，三项全要
- 日常优化：场景调度 + 健康维护 + 电池能效，三项全要
- 互斥策略：严格优先级仲裁，低优先级挂起而非关闭

## 方案选择

选定**方案 A：场景仲裁器 + 独立场景模块**。

- 新增 `ScenarioArbiter` 统一仲裁"系统副作用掌权资格"；三场景各自独立模块，沿用 `BuildWatch` 的事件驱动模板；`GameMode` 零改动仅接线
- 拒绝方案 B（通用场景框架重构）：需拆解 GameMode 的稳定核心路径，176 项自测回归风险不可接受
- 拒绝方案 C（手动 Profile 切换）：与已确认的"严格优先级自动仲裁"需求矛盾

## 架构设计

### 整体结构

```
                    ProcNotify.BatchChanged (进程事件广播)
                                      │
          ┌───────────────────────────┼───────────────────────────┐
          ▼                           ▼                           ▼
   ┌─────────────┐            ┌──────────────┐            ┌──────────────┐
   │  GameMode   │            │   DevFocus   │            │  DailyCare   │
   │  (游戏·现有) │            │  (开发·改造)  │            │  (日常·新建)  │
   └──────┬──────┘            └──────┬───────┘            └──────┬───────┘
          │ 申请掌权                  │ 申请掌权                   │ 申请掌权
          ▼                           ▼                           ▼
   ┌──────────────────────────────────────────────────────────────────┐
   │                    ScenarioArbiter (仲裁器·新建)                    │
   │   优先级: Game(100) > DevFocus(50) > DailyCare(10)                 │
   └──────────────────────────────────────────────────────────────────┘
```

**基石决策：仲裁器只仲裁"副作用掌权资格"，不仲裁检测**——三个场景的检测器永远同时运行，被挂起的场景不产生副作用但持续维护检测状态，从挂起恢复到掌权是瞬时的。

### ScenarioArbiter

```csharp
interface IScenario {
    ScenarioKind Kind { get; }        // Game / DevFocus / DailyCare
    int Priority { get; }             // 100 / 50 / 10
    void Grant();                     // 获得副作用掌职权 → 施加副作用
    void Suspend();                   // 还原全部副作用 → 挂起（检测继续）
}
```

- 内部状态：`Dictionary<ScenarioKind, bool> activityMap` + `currentGranted`，无持久化
- 每次 `ReportActivity(kind, isActive)` 后重算：`winner = active 中优先级最高者`；winner 变化时旧掌权者 `Suspend()`、新掌权者 `Grant()`
- `GrantedChanged` 事件供 UI 显示当前掌权场景

### 挂起语义：还原式（关键决策）

被抢占的场景**立即还原其全部副作用**（恢复被压后台、恢复服务、解除通知静默、IDE 降回原优先级），不冻结保持。理由：

- 系统状态始终只反映掌权者一个场景的意图，无叠加权属混乱
- `SvcPause` 这类单一开关型副作用不支持叠加
- 恢复管线全部复用既有 `SuppressionCore.Recovery` / `SvcPause.Restore`，无新机制

**抖动防护**：GameMode 会话建立本身要证据选举（不会瞬间闪烁）；DevFocus/DailyCare 检测器各带迟滞（编译会话沿用现有语义；IDE/日常家族需有可见窗口）。仲裁器不额外加防抖。

### GameMode 接入方式

`Program.cs` 订阅 GameMode 现有 `SessionChanged` 事件转发为 `arbiter.ReportActivity(Game, ...)`。GameMode 是最高优先级、永远不会被 Suspend，不实现挂起逻辑，对仲裁器无感知。BuildWatch 现有的 `isGameActive` 委托删除，互斥职责移交仲裁器。

### 线程与崩溃模型

- `ReportActivity` 在 `ProcNotify` 事件线程调用；仲裁器锁内只记账、锁外执行 `Grant()`/`Suspend()`（沿用 BuildWatch 既定模式）
- 仲裁器崩溃无残留；各场景副作用的崩溃自愈由既有三管线兜底（`SuppressionCore.Recovery` + `SvcPause` 注册表标志 + `CrashGuard`），新场景共用同一管线

## DevFocus 场景详设（改造自 BuildWatch，~400 行）

### 编译资源调度深化（自动触发）

- 启用 `SuppressReason.Build=4` 空位：编译会话活跃期间扫描后台进程（无窗口、非前台、非豁免），按**常规档**压制到低优先级/EcoQoS——与游戏模式常规档同语义，绝不动带窗口程序
- 保留现有动作：`SvcPause` 暂停索引 + 编译器 HIGH + IO 3；`BuildCatalog` 进程表、`CustomBuildProcs` 自定义表保留
- 会话结束或被挂起时按 Build 位走既有 Recovery 管线还原
- 会话报告补充压制统计（补上旧设计未实现的 suppressedCount）：`开发模式：本次编译 12.3 秒，期间压制 8 个后台进程，索引服务已暂停`

### 开发者专注模式（手动开关）

自动检测"深度工作"误报率高，采用**用户手动开关**（设置页 + 托盘菜单双入口，注册表持久化）：

- 通知静默：复用现有 `Notif` 机制（会话级可还原）
- 持续常规档后台压制：与编译深化同一套压制机制，不依赖编译进程
- 分心应用提醒（可选，默认关）：用户配置分心清单，清单进程启动时托盘提示一次，不强制杀进程

### IDE 性能优化（自动触发）

新增 `IdeCatalog`（进程名 + 安装目录双校验，仿 `GamePlatformCatalog`）：devenv / Rider / Code / idea64 / webstorm / goland / clion / pycharm / cursor 等。

- 触发条件：IDE 家族进程存在**且有可见窗口**（后台挂起一天的 IDE 不提升）
- 动作：IDE 家族（含子进程）提至 **AboveNormal** + IO 优先级提升（不用 HIGH，避免 IDE 后台索引抢编译 CPU）
- 窗口全关或进程退出 → 还原

### 活性判定

编译会话活跃 **OR** 专注模式开启 **OR** IDE 会话活跃 → 报告 active。三块副作用统一纳入仲裁：游戏激活时 DevFocus 整体 Suspend。

## DailyCare 场景详设（新建，~500 行）

### 日常场景调度（自动触发）

- 新增 `DailyCatalog`：浏览器家族（chrome/edge/firefox/brave，含多进程结构）、Office/WPS、会议软件（Zoom/Teams/Webex/飞书/钉钉）
- 家族活跃（有可见窗口）时：常规档后台压制 + 活跃家族提至 **AboveNormal**
- 视频会议不做 QoS 特殊照顾（YAGNI，v2 再评估）

### 电池/能效优化（自动触发）

- `SystemEvents.PowerLineStatus` 事件驱动
- 电池供电且 DailyCare 掌权期间：后台压制升到中等档（介于常规与竞技之间）+ 建议电源滑块到"更好电池"（不强制）
- 插回电源自动还原

### 系统健康维护（定时任务）

- 每日一次（频率可配），**只在 DailyCare 掌权期间执行**——游戏中绝不清理；到点但无掌权资格则推迟到下次掌权
- 内容：复用 `CacheSweep`/`ShaderCache` 缓存清理、Caelus 自身日志轮转、**启动项审查**（与基线快照对比，新出现的启动项只在 UI 报告，不自动删——保持"用户知情"原则）

### 活性判定

日常家族活跃 **OR** 电池供电 → 报告 active。

## ScenarioBase 共用基础设施（新建，~80 行）

抽取三场景共用样板：

- PID 集合维护 + 死 PID 兜底清理（`OpenProcess` + `StillActive`，沿用 BuildWatch 模式）
- 锁内记账、锁外执行副作用
- Grant/Suspend 模板方法
- **常规档豁免计算器**：系统核心 / 反作弊 / 别的账户 / 前台程序 / 白名单。**不改 GameMode**——其豁免逻辑分散在多文件，为保稳定核心不动，接受这一处受控重复
- 通用压制执行器（常规档 Sweep）

## SuppressReason 扩展

```csharp
[Flags] enum SuppressReason {
    AntiCheat = 1,   // 现有
    Background = 2,  // 现有（游戏）
    Build = 4,       // 空位启用（DevFocus）
    Daily = 8,       // 新增位（DailyCare）
}
```

## UI 设计

WPF + WinForms 双侧同步（项目惯例）：

- **设置页新增"开发专注"区**：总开关（复用 `DevModeOn` 语义兼容）、专注模式开关、IDE 优化开关、分心清单输入、自定义编译进程（现有保留）
- **设置页新增"日常优化"区**：总开关（`DailyCareOn` 默认开）、健康维护频率、电池优化开关、启动项审查入口
- **托盘菜单**：专注模式快速开关
- **概览页**：当前掌权场景指示（`GrantedChanged` 事件驱动）
- **文案**：`Lang.cs` 新增键，简体中文；多语言机制保留

## 错误处理与崩溃自愈

- 所有副作用写入回读核验，失败记失败并保留恢复凭据（项目既有惯例）
- 新场景压制位与现有 Background 位共用同一 Recovery 管线，崩溃自愈零新机制
- 注销/关机走既有 `SystemEvents.SessionEnded` 路径，新场景挂入
- 各场景开关关闭时若正在压制立即解除（沿用 BuildWatch 防永久暂停语义）

## 测试计划

新增自测 ~20 项（基线 176 PASS / 0 FAIL / 2 SKIP 之上）：

- `SelfTests.Arbiter.cs`：优先级仲裁、抢占还原、挂起期间检测继续、无掌权者、GameMode 永不挂起
- `SelfTests.DevFocus.cs`：编译会话压制/还原（Build 位）、专注模式开关语义、IDE 家族识别、IDE 可见窗口条件、与游戏互斥
- `SelfTests.DailyCare.cs`：日常家族识别、电池切换与还原、健康维护定时与掌权门控、启动项基线对比

## 实施分期

| 期 | 内容 | 依赖 |
|---|------|------|
| P1 | ScenarioBase + ScenarioArbiter + BuildWatch→DevFocus 编译深化 | 无 |
| P2 | 专注模式 + IDE 优化 | P1 |
| P3 | DailyCare 场景调度 + 电池能效 | P1 |
| P4 | 系统健康维护 + UI 完善 + 托盘/概览 | P3 |

P1 是架构奠基，P2/P3 可并行，P4 收尾。

## 设计决策记录

| 决策 | 选择 | 理由 |
|------|------|------|
| 挂起语义 | 还原式（立即还原全部副作用） | 单一开关型副作用不可叠加；系统状态只反映掌权者意图 |
| 仲裁粒度 | 只仲裁副作用，检测永远运行 | 挂起→恢复瞬时，无需重新检测 |
| GameMode 接入 | SessionChanged 事件转发，本体零改动 | 稳定核心不动，176 项自测零回归风险 |
| 专注模式触发 | 手动开关 | 自动检测"深度工作"误报率高 |
| IDE 提优档位 | AboveNormal 而非 HIGH | 避免 IDE 后台索引抢编译 CPU |
| IDE/日常触发条件 | 有可见窗口才激活 | 后台挂起的常驻程序不浪费提优 |
| 豁免计算器 | ScenarioBase 新建，不动 GameMode | 保稳定核心，接受受控重复 |
| 启动项审查 | 只报告不自动删 | 用户知情原则 |
| 视频会议 QoS | 本期不做 | YAGNI，v2 评估 |

## 非目标（本期不做）

- GameMode 重构为场景框架（方案 B，风险过高）
- 视频会议网络 QoS 标记
- 分心应用强制关闭/屏蔽（只做提醒）
- 启动项自动清理（只审查报告）
- 多语言文案补全（机制保留，仅简中）
- 手动 Profile 切换界面（与自动仲裁矛盾）
