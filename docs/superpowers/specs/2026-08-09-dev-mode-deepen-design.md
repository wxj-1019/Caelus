# 深化开发模式：自定义进程 + 收益报告 + 热重载 + IDE 保护 + Git/Docker 识别

## 目标

补全开发模式的五个短板:用户无法加自己的编译进程名、看不到编译优化的实际效果、热重载守护进程会被误压、IDE 语言服务器没保护、Git/Docker build/测试运行没覆盖。

## 背景

当前开发模式(`BuildCatalog` + `BuildWatch`)已工作:检测编译进程 → 压制后台 + 暂停索引服务 → 恢复。五项深化都在此基础上扩展,不改变核心架构。

## 改动总览

| # | 项 | 改什么 | 复杂度 |
|---|-----|--------|--------|
| 1 | 自定义编译进程列表 | BuildCatalog 注册表加载 + 设置页文本输入 | 中 |
| 2 | 编译收益报告 | BuildWatch 计时 + 压制统计 + 报告日志 | 低 |
| 3 | 热重载守护识别 | DevServiceWhitelist 加 dotnet-watch/nodemon/vite 等 | 极低 |
| 4 | IDE 语言服务器豁免 | DevServiceWhitelist 加 omnisharp/jdtls/tsserver 等 | 极低 |
| 5 | Git/Docker build/测试运行识别 | BuildCatalog 加 git/docker/nunit3-console/jest 等 | 低 |

## 1. 自定义编译进程列表

### 问题
内置 `BuildCatalog.Names` 覆盖主流编译器,但用户的内部工具/冷门构建器不在里面,无法被识别。

### 设计
- 存储注册表键 `CustomBuildProcs`(分号分隔的进程名,如 `mybuilder;internal-compiler`)
- `BuildCatalog.IsMatch(name)` 改为:内置 `Names` 命中 **或** 自定义列表命中
- 自定义列表缓存在静态字段,首次访问时从注册表加载;提供 `Reload()` 在设置变更后刷新
- 设置页:开发模式卡片下方加一个文本输入区,用户填进程名(一行一个或分号分隔)

### 改动
- `BuildCatalog.cs`:加 `LoadCustom()` / `ReloadCustom()` / `IsMatch` 扩展
- `SettingsPage.cs`:加文本输入 + 保存按钮
- `Lang.cs`:文案 `set.dev.custom` / `set.dev.custom.n` / `set.dev.custom.ph`

## 2. 编译耗时/收益报告

### 问题
用户看不到编译优化实际做了什么、效果如何。

### 设计
`BuildWatch` 在编译会话期间记录:
- 会话开始时间(`sessionStartTicks`)
- 压制的后台进程数(`suppressedCount`)——在 `ActivateSuppression` 时从 `SuppressionCore` 获取当前被压进程数
- 编译结束时计算会话时长,写一条报告日志

报告格式(复用游戏会话报告的措辞风格):
```
开发模式：本次编译 12.3 秒，期间压制 8 个后台进程（占用 3.2% CPU），索引服务已暂停
```

### 改动
- `BuildWatch.cs`:加 `sessionStartTicks` 字段、`suppressedCount` 统计、`DeactivateSuppression` 里写报告
- `Logger.Log` 已有,直接用

## 3. 热重载守护进程识别

### 问题
`dotnet watch`、`nodemon`、`webpack --watch` 这类热重载守护进程在编译期间会被当普通后台压制,导致热重载失效卡顿。

### 设计
- `BuildCatalog` 加 `IsWatchProcess(name)` 方法 + `WatchNames` 集合:
  `dotnet-watch`(dotnet watch 的子进程名)、`nodemon`、`webpack-dev-server`、`vite`、`concurrently`
- 注意:这些进程**不触发编译会话启停**(它们是常驻的,不是编译器),只是在编译期间**不被当后台压**
- `BuildWatch.BoostBuildProcesses()`:除了提优编译进程,也遍历当前运行的进程,对 `IsWatchProcess` 命中的提优(高优先级)
- 实际上更简单的做法:把这些加入 `DevServiceWhitelist`(编译期间不压的列表)。语义一致——它们是开发期间不该压的常驻进程

### 简化决策
热重载守护进程本质上是"开发常驻服务",归入 `DevServiceWhitelist` 最合适——不需要单独的 `IsWatchProcess` 逻辑,只需在 `DevServiceWhitelist.Names` 里加上它们。

### 改动
- `DevServiceWhitelist.cs`:加 `dotnet-watch`、`nodemon`、`webpack-dev-server`、`vite`、`concurrently`、`pm2`

## 4. IDE 子进程保护(语言服务器豁免)

### 问题
IDE(VSCode/IDEA/VS)编码时的智能提示、补全、实时错误检查依赖后台语言服务器进程(Roslyn/JDTLS/PylS)。这些子进程不在豁免列表里,开发模式激活时可能被压,导致写代码时智能提示卡顿。

### 设计
`DevServiceWhitelist.Names` 增加语言服务器进程名:

```
.NET:     omnisharp, roslyn, roslyn-codeanalysis
Java:     jdtls, jdt, language-server, eclipse-jdt
Python:   pyls, pyright, pylance, jedi-language-server
JS/TS:    tsserver, vscode-language-server
Go:       gopls
Rust:     rust-analyzer
通用:     clangd, ccls, language-server, lsp
```

这些进程在编译期间**不压**,保证 IDE 智能功能不被干扰。

### 改动
- `DevServiceWhitelist.cs`:增加上述进程名

## 5. Git / Docker build 识别

### 问题
`git rebase`/`git gc`/`git pack` 是大规模 IO 操作,`docker build` 是镜像构建——都是开发高频操作,当前完全没覆盖。

### 设计
**Git**:`BuildCatalog.Names` 加 `git`。Git 的所有子命令(rebase/gc/pack/clone/fetch)都会派生 `git.exe` 子进程,进程名匹配即可触发。

**Docker build**:这是一个特例——`docker` 进程既是运行时(不该压)又是构建信号(docker build 该触发)。由于 `ProcessChange` 只有进程名没有命令行参数,无法区分 `docker build` 和 `docker run`。折衷方案:
- `docker` 加入 `BuildCatalog`(任何 docker 命令都触发编译会话,包括 `docker run`——误触发无害,压制很快恢复)
- `dockerd`、`com.docker.backend`、`containerd` 仍在 `DevServiceWhitelist`(Docker 守护进程不压)
- 结果:`docker build` 时触发压制(Docker 守护不动),`docker run` 也会短暂触发(无害)

### 改动
- `BuildCatalog.cs`:加 `git`、`docker`、`git-bash`、`git-cmd`、`hub`、`gh`
- `DevServiceWhitelist.cs`:保持 `dockerd`、`com.docker.backend` 在豁免列表(已有)

## 6. 测试运行识别(补充)

### 问题
`dotnet test`、`pytest`、`jest`、`nunit` 等测试框架运行时不在 BuildCatalog,跑测试时后台可能干扰。

### 设计
`BuildCatalog.Names` 加测试运行器进程名:`nunit3-console`、`vstest.console`、`pytest`、`jest`、`mocha`、`go-test`

### 改动
- `BuildCatalog.cs`:加上述测试进程名

## 验证方式

1. `build.cmd` 编译通过
2. `dev.cmd test` 自测无回归
3. 手动验证:
   - 自定义进程:设置页加一个进程名 → 触发该进程 → 确认开发模式激活
   - 收益报告:触发编译 → 确认日志有编译耗时和压制统计
   - 热重载:启动 nodemon → 触发编译 → 确认 nodemon 未被压制(日志无压制记录)
   - Git:运行 `git gc` → 确认开发模式激活
   - Docker build:运行 `docker build` → 确认开发模式激活且 Docker 守护未被压
   - IDE 语言服务器:编码时触发编译 → 确认 omnisharp/tsserver 等未被压

## 不在本次范围(后续迭代)

- 按命令行参数精确区分 `docker build` vs `docker run`(需要 WMI 查询命令行,当前进程事件不带)
- 调试会话的专门保护(IDE 内嵌调试器不触发编译会话)
- IDE 发热/风扇优化(DisplayAwake/电源计划开发模式)
