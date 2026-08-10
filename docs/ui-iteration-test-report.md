# UI 迭代测试报告（Phase 1 + 1.5）

**日期**: 2026-08-10  
**范围**: 已完成内容（Phase 1 WPF 骨架 + Phase 1.5 Radium 座舱化）的迭代测试（两轮）  
**结论**: **应用零缺陷**（唯一未实机验证项：托盘图标，已隔离确认为环境问题）

---

## 测试层次

| 层次 | 内容 | 结果 |
|------|------|------|
| 1. 自动化基线 | WinForms 构建 + WPF 构建 + 166 自测 | ✅ 全通过 |
| 2. 截图矩阵 | `--wpf-shot` 4 张（模式×主题） | ✅ 与验收基线**字节级一致** |
| 3. 实机交互 | UIA 驱动：模式切换/导航/持久化/关闭 | ✅ 7/7 通过 |

## 实机交互测试明细（7 项）

测试方法：UIA SelectionItemPattern.Select() 触发（不移动真实鼠标、不干扰用户），每步刷新 RootElement（UIA 客户端缓存怪癖），窗口作用域查询验证，PrintWindow API 渲染窗口内容做视觉验证（无视屏幕遮挡），注册表验证持久化。

| # | 验证点 | 结果 |
|---|--------|------|
| T1 | 启动即显示概览页 + 巡航青环境光 (RGB 27,46,54) | ✅ |
| T2 | 切竞技：注册表 PerformancePreset=1 + 环境光变红 (47,35,40) | ✅ |
| T3 | 切自定义：注册表=2 + 环境光变紫 (38,37,57) | ✅ |
| T4 | 导航游戏库：占位页出现 | ✅ |
| T5 | 导航回概览：结论卡片恢复 | ✅ |
| T6 | 关闭窗口：进程正常退出 | ✅ |
| T7 | 重启：模式持久化（紫色环境光 + 分段「自定义」选中 + 启动即概览） | ✅ |

## 测试中发现的问题（均为测试环境问题，非应用缺陷）

### 1. 屏幕遮挡：LOL 客户端全屏覆盖
- **现象**：CopyFromScreen 截图内容显示英雄联盟界面（装备合成页）；真实鼠标点击无效果
- **根因**：用户桌面运行着 League of Legends 客户端（全屏 1536x864 覆盖一切），拦截点击、遮挡截图
- **应对**：改用 PrintWindow API（直接渲染目标窗口内容，无视遮挡）+ 放弃真实鼠标点击（UIA Select 不依赖屏幕）

### 2. UIA 客户端缓存失效（PowerShell UIA）
- **现象**：`SelectionItemPattern.Select()` 后，RootElement 作用域的 FindFirst 全部返回 null；窗口元素句柄查询失败
- **根因**：WPF 在 Select() 时触发大量属性变更事件，PowerShell UIA 客户端（COM 封装）缓存失效——**应用侧 UIA 树完全正常**（窗口作用域 FindAll 可枚举 29-40 个元素，应用无异常、无崩溃）
- **应对**：每步操作后重新获取 RootElement 与窗口引用（`Get-Win` 刷新）

### 3. 坐标双重缩放陷阱
- **现象**：窗口 rect 1495x960 与声明尺寸 1196x768 的关系（125% DPI）导致坐标换算混乱
- **根因**：UIA BoundingRectangle 返回物理像素；曾误乘 DPI 缩放导致点击落空
- **应对**：用 GetWindowRect P/Invoke 获取真实物理坐标做交叉验证

## 过程中加固的代码（已提交）

| 变更 | 理由 |
|------|------|
| `App.xaml.cs`：DispatcherUnhandledException 处理器（写 %TEMP%\CaelusWpf.crash.log） | 生产健壮性：未处理异常不再无声崩溃，可诊断 |
| 临时调试日志（ModeController/AmbientLayer/MainWindow） | 已移除（定位完成后清理） |

## 第二轮迭代测试（边界场景）

### 测试结果

| # | 场景 | 结果 |
|---|------|------|
| T1 | **减少动画模式**（SPI_SETCLIENTANIMATION 关闭系统动画，测完恢复）：模式切换瞬时完成 | ✅ PASS |
| T2 | **快速连点**（100ms 间隔连切竞技→自定义→竞技→自定义）：最终注册表=2 正确、进程存活、概览页完好 | ✅ PASS |
| T3 | **最小化按钮**：窗口 rect 变 (-32000,-32000) 确认最小化 | ✅ PASS |
| T4 | **托盘图标**：见下方专项调查 | ⚠️ 环境问题 |
| T5 | **关闭/重启循环 3 轮**：每轮启动+关闭干净，无残留进程 | ✅ PASS |

### T4 托盘图标专项调查（结论：环境问题，非应用缺陷）

**证据链**：
1. Caelus 启动前后任务栏通知区域（500x48 像素）像素对比 diff=0——图标未显示
2. PowerShell 直接创建 NotifyIcon（有/无 DoEvents 消息泵）——均不显示
3. **独立编译的 WinForms 探针应用**（完整 `Application.Run` 消息循环 + NotifyIcon）——**同样不显示**（diff=0）
4. 系统图标（OneDrive、时钟等）正常显示——说明通知区域本身工作，**只有新注册的图标不显示**
5. 根因：`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\TrayNotify` 注册表键**缺失**——本机 Windows 会话（10.0.26200.0）的托盘配置状态异常

**结论**：托盘图标不显示是**本机环境问题**（独立 WinForms 应用同样失败），Caelus 的 NotifyIcon 代码逻辑本身无缺陷。**需在正常桌面环境人工确认托盘显示**。

**顺带改进**（已提交）：`App.xaml.cs` 的托盘创建从 OnStartup 改为 `Dispatcher.BeginInvoke` 延迟创建——WPF+NotifyIcon 最佳实践（消息循环运行后创建，避免隐藏窗口/消息时机问题），在正常环境下更稳妥。

## 测试方法论沉淀（供后续阶段复用）

1. **GUI 交互测试用 UIA Select + 注册表/树状态验证**，不用真实鼠标（不干扰用户、不受遮挡）
2. **视觉验证用 PrintWindow**（无视屏幕遮挡），不用 CopyFromScreen
3. **每步刷新 UIA 客户端引用**（RootElement/窗口句柄），规避缓存失效
4. **交互前后读注册表/持久化状态**做逻辑验证，与视觉验证解耦
5. **测试前检查**：`Get-Process CaelusWpf` 残留进程、顶层窗口遮挡（EnumWindows）
6. **DPI 处理**：GetDpiForWindow 或 GetWindowRect 交叉验证，勿假设缩放
7. **托盘图标验证**：像素对比（任务栏区域前后差异）比 UIA 枚举可靠；UIA 枚举 Windows 11 通知区域不稳定（TrayNotifyWnd 空）
8. **环境隔离**：托盘不显示时先编译独立 WinForms 探针验证环境，勿急于判应用缺陷
