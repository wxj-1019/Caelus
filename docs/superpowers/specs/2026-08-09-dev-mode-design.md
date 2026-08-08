# 开发模式：编译/调试时自动优化资源

## 目标

检测到编译/调试进程启动时,自动把后台降到低优先级 + 暂停资源大户服务;编译进程退出后自动恢复。让 Caelus 从纯游戏优化扩展到程序员开发场景。

## 背景：可复用的现有机制

Caelus 已有的内核完全可复用,本功能的核心是"新增一种触发源":

| 机制 | 现有实现 | 复用方式 |
|------|---------|---------|
| 进程事件监听 | `ProcNotify.BatchChanged` → `ProcessChange{Name, Kind, Pid}` | 挂同样的钩子,检查新进程名是否匹配编译器目录 |
| 后台压制 | `SuppressionCore.Acquire(pid, name, reason, level)` | 编译期间对后台进程降优先级(温和档,不冻结) |
| 服务暂停 | `SvcPause.Activate()` / `SvcPause.Restore()` | 编译期间暂停资源大户服务 |
| 优先级提升 | `Native.SetPriorityClass` / `SetProcessIoPriority` | 编译进程自身提优 |
| 托盘气泡 | `gameMode.SessionEnded += msg => icon.ShowBalloonTip(...)` | 新增 `BuildSessionChanged` 事件,同样订阅模式 |

## 触发流程

```
ProcNotify.BatchChanged
  → BuildWatch.NotifyProcessChanges(batch)
    → 遍历 batch,检查 ProcessChange.Name 是否匹配 BuildCatalog
      → 检测到编译进程启动(Started):
          → 标记"编译中"(加入活跃编译进程集合)
          → 激活压制:后台降优先级 + SvcPause.Activate() + 编译进程提优
          → 触发 BuildSessionChanged("编译优化中") → 托盘气泡
      → 检测到编译进程退出(Stopped):
          → 从活跃集合移除
          → 集合空了 → 恢复:取消压制 + SvcPause.Restore()
          → 触发 BuildSessionChanged("已恢复") → 托盘气泡
```

## 组件设计

### 1. `src/Core/BuildCatalog.cs`(新文件)— 编译/调试进程名目录

静态目录,`IsMatch(string processName)` 判断进程名是否为编译/调试信号。进程名比较去掉扩展名、忽略大小写。

```
.NET:     msbuild, dotnet, csc, vbc, fsc, roslyn, vbc, msbuildsdp
JVM:      javac, gradle, mvn, sbt, kotlinc
C/C++/Rust: gcc, g++, clang, clang++, cmake, make, ninja, ld, lld, rustc, cargo
Node/前端: tsc, webpack, vite, esbuild, rollup, gulp, babel
调试器:   gdb, lldb, msvsmon
```

注意:`node`、`java`、`dotnet` 这类**既是运行时又是编译器**的进程不直接匹配(太宽泛,会把运行中的 dev server 也当编译)。只匹配明确的编译/构建工具名。`dotnet build` 和 `npm run build` 会派生子进程(msbuild/tsc),由子进程触发。

### 2. `src/Core/DevServiceWhitelist.cs`(新文件)— 开发服务豁免目录

编译期间**不压**的开发常驻服务进程名。压制后台时排除这些:

```
Docker:   docker, dockerd, com.docker.backend, vpnkit, docker-proxy
数据库:   postgres, mysqld, redis-server, mongod, sqlservr, oracle
ES/日志:  elasticsearch, kibana, logstash, filebeat, metricbeat
```

压制时 `SuppressionCore.Acquire` 调用前过滤——如果进程名在 DevServiceWhitelist 中,跳过。

### 3. `src/Core/BuildWatch.cs`(新文件)— 编译会话检测器

核心状态机,管理"编译中/空闲"状态:

- `NotifyProcessChanges(ProcessChangeBatch)` — 接收进程事件,更新活跃编译进程集合
- `bool IsActive` — 当前是否在编译会话中
- `event Action<string> SessionChanged` — 状态变化通知(用于托盘气泡)
- 内部:`HashSet<int> activeBuildPids`(活跃编译进程 PID),激活/恢复的幂等控制(不重复激活)

激活时:
- `SuppressionCore` 对后台降优先级(温和档 `SuppressionLevel.Restrained`,排除 DevServiceWhitelist)
- `SvcPause.Activate()`
- 编译进程自身:`Native.SetPriorityClass`(高) + IO 优先级(高)

恢复时:
- 取消后台压制
- `SvcPause.Restore()`

### 4. `src/Program.cs` — 接线

- 在 `procNotify.BatchChanged` 里,除了 `gameMode.NotifyProcessChanges` 和 `tamer.NotifyProcessChanges`,新增 `buildWatch.NotifyProcessChanges`
- 订阅 `buildWatch.SessionChanged`,用 `icon.ShowBalloonTip` 显示气泡
- 与游戏模式互斥:`gameMode.IsActive` 为 true 时不激活编译压制(游戏优先)

### 5. `src/Platform/Lang.cs` — 新增文案

```
dev.title       开发模式
dev.n           检测到编译/调试时自动把资源让给编译器
bal.buildstart  编译优化中：后台已降级，{0} 个编译进程在跑
bal.buildend     编译结束，已恢复后台资源
set.dev          开发模式
set.dev.n        检测到编译/调试进程（msbuild、gcc、npm 等）时自动优化，编译完恢复
```

### 6. `src/Ui/Pages/PanelForm.SettingsPage.cs` — 设置页开关

新增"开发模式"开关卡片(默认开),读写 `Settings.Save("DevModeOn", value)`。`BuildWatch` 启动前检查此开关。

## 安全边界

- **开发服务豁免**:Docker/数据库/ES 等(DevServiceWhitelist)编译期间不压
- **前台 IDE 不压**:当前前台程序及其子进程沿用现有豁免
- **游戏优先**:`gameMode.IsActive` 时编译压制不介入
- **反作弊/系统核心**:沿用现有安全边界
- **幂等**:重复的激活/恢复调用不会叠加(多次 Activate 只生效一次)

## 验证方式

1. `build.cmd` 编译通过
2. `dev.cmd test` 152 项自测 PASS(可能新增 1-2 项 BuildCatalog 匹配测试)
3. 手动验证:启动一个编译进程(如 `msbuild` 或 `tsc --watch`)→ 确认托盘气泡 + 后台降级 + 服务暂停;编译进程退出 → 确认恢复
4. 确认 Docker/数据库进程在编译期间未被压制

## 不在本次范围

- 自定义编译进程列表(用户自己加进程名)——内置目录已覆盖主流,自定义后续再加
- 编译耗时/收益统计——复用游戏会话报告机制后续再加
- 热重载守护进程(dotnet watch / nodemon)的智能识别——当前按"明确编译工具名"匹配,不匹配运行时
