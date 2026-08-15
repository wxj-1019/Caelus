# 开发者体验扩展：工具链目录 + 开发服务守护 + 编译提速实测

> 目标：把 Caelus 的"程序开发"场景从「编译/IDE 提优 + 专注模式」扩展为对开发者日常更实用的三个能力——覆盖更多工具链、守护本地开发服务、用数据证明编译提速。

## 背景

现状（已核实）：

- 「BuildCatalog」识别编译/构建/调试进程名，作为 DevFocus 编译来源触发信号；已覆盖 .NET/JVM/C/C++/Rust/前端/调试器/Git/Docker/测试运行器，**刻意排除通用运行时 node/java/dotnet**（防误触发）
- 「IdeCatalog」用「进程名 + 安装目录前缀」双校验识别 IDE，覆盖 VS/Rider/VS Code/Cursor/JetBrains 系；**缺数据库客户端、Android Studio**
- DevFocus/DailyCare 的压制扫描都经 isWhitelisted 委托做豁免（当前只有用户白名单）
- SelfTests.ContentionLab.cs 已有成熟的 A/B 争抢台架（FrameVictim 定量负载 + --cpu-burn 抢占进程 + 中位数汇总），可复用
- 自测基线：**213 项，0 失败**

## REQ / AC

### REQ-1 工具链与数据库客户端目录扩展

- REQ-1.1 BuildCatalog 新增编译/任务编排工具：pnpm、yarn、bun、nx、lerna、just、uv、poetry
- REQ-1.2 BuildCatalog 继续排除 node/npm/npx/dotnet/java 等通用运行时（防误触发）
- REQ-1.3 IdeCatalog 新增数据库客户端/移动 IDE：ssms、datagrip64、dbeaver、studio64、azuredatastudio、mysqlworkbench（各自带安装目录前缀）

AC：
- GIVEN 进程名 "pnpm" WHEN BuildCatalog.IsMatch("pnpm") THEN 返回 true
- GIVEN 进程名 "node" / "npm" WHEN IsMatch THEN 返回 false（运行时仍排除）
- GIVEN 路径落在 DataGrip 安装目录 WHEN IdeCatalog.IsMatch("datagrip64", path) THEN true；路径不在安装目录 THEN false

### REQ-2 开发服务守护（DevServiceGuard）

- REQ-2.1 新增 DevServiceCatalog：注册表 DevServiceList（分号/换行分隔），IsMatch(name) 匹配（去 .exe 后缀、忽略大小写），Reload()
- REQ-2.2 新增 DevServiceGuard：经 ProcNotify 事件跟踪已注册服务进程，按名字计数去重；**最后一个实例退出且存活超过阈值**时触发 ServiceStopped(name) 事件
- REQ-2.3 已注册开发服务在 DevFocus/DailyCare 的压制扫描中**被豁免**（不被后台压制）
- REQ-2.4 设置页新增「开发服务」输入框（复用现有 distract 输入模式），保存写 DevServiceList 并 Reload

AC：
- GIVEN DevServiceList="node;redis-server" WHEN IsMatch("node.exe") THEN true，IsMatch("NODE") true，IsMatch("python") false
- GIVEN 一个 node 进程启动后退出（存活≥阈值）WHEN 触发 NotifyProcessChanges(Stopped) THEN ServiceStopped 事件以 "node" 触发一次；多个实例只有最后一个退出才触发
- GIVEN 注册服务名命中 WHEN DevFocus.ShouldSuppressBackground(..., isWhitelisted=服务名) THEN 返回 false（豁免）
- GIVEN 多个同名实例存活，退出其中一个 WHEN 事件 THEN 不触发（计数未归零）

### REQ-3 编译提速实测台架（--build-probe）

- REQ-3.1 新增 --build-probe <output> [work] [hogs] [rounds] 运行时诊断（仅 SELFTEST 构建）
- REQ-3.2 受害者 = 单线程定量负载（固定工作量，测量总墙钟时间，模拟编译）；抢占者 = --cpu-burn 满载进程（复用）
- REQ-3.3 每轮 A/B：放任 vs 压制（SuppressionCore Eco 档），输出中位数墙钟与提速百分比

AC：
- GIVEN --build-probe out.txt 4 2 3 WHEN 运行 THEN 输出含「放任 / 压制」两臂中位墙钟、提速百分比，且无异常

## 非目标

- 不做通用运行时（node/java/dotnet）触发 DevFocus——保持防误触发
- 不 boost 长驻开发服务（避免对服务器进程长期提权）
- 不做命令托盘（命令注入风险，另立专项）

## 文件结构

| 操作 | 文件 | 职责 |
|---|---|---|
| Modify | src/Core/BuildCatalog.cs | REQ-1.1/1.2 工具链名录 |
| Modify | src/Core/Scenario/IdeCatalog.cs | REQ-1.3 数据库/移动 IDE |
| Create | src/Core/Scenario/DevServiceCatalog.cs | REQ-2.1 名录 |
| Create | src/Core/Scenario/DevServiceGuard.cs | REQ-2.2 守护 |
| Modify | src/Program.cs | REQ-2.3 豁免委托包装 + Guard 接线 + 托盘气泡 |
| Modify | src/Ui/Pages/PanelForm.SettingsPage.cs | REQ-2.4 设置页输入 |
| Modify | src/Platform/Lang.cs | 文案键 |
| Modify | tests/SelfTests.cs + 相关测试文件 | 注册测试 |
| Create | tests/SelfTests.DevService.cs | REQ-2 测试 |
| Modify | tests/SelfTests.ContentionLab.cs | REQ-3 台架 |

---

## 追加（2026-08-15 第二批）

### REQ-4 专注时长统计

- REQ-4.1 新增 FocusStats：累计「开发专注掌权时长」与「会话次数」，按日历天归零，持久化到注册表
- REQ-4.2 DevFocus 在 Grant 记起点、Suspend 把本次掌权时长计入 FocusStats
- REQ-4.3 设置页开发模式下显示「今日开发专注：X 分钟 · Y 次会话」

AC：
- GIVEN 同一天记录 10s + 5s WHEN TodaySeconds THEN 15，TodaySessions THEN 2
- GIVEN 跨天再记录 5s WHEN TodaySeconds THEN 5（归零重计），TodaySessions THEN 1
- GIVEN DevFocus 专注开→关 WHEN 会话 THEN TodaySessions 递增

### REQ-5 开发环境体检

- REQ-5.1 新增 DevEnvAudit：只读检测开发工具链版本（dotnet/node/npm/git/python/java/cargo/go）与 Windows 开发者模式
- REQ-5.2 探测用固定参数数组起进程，3 秒超时；未安装/超时记「(未安装)」
- REQ-5.3 设置页开发模式新增「开发环境体检」区：检测按钮 + 只读结果列表

AC：
- GIVEN Run() WHEN 完成 THEN 返回稳定顺序的工具名列表（dotnet 第一、node 第二、git 第三…），每项 Name/Detail 非空，Found 与 Detail 一致
- GIVEN 版本输出到 stderr（python/java 常见）WHEN ParseVersion(stdout, stderr) THEN 取首个非空行
