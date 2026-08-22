# WPF 消息弹窗主题化：MessageDialogWpf

日期：2026-08-23
状态：已实施（feature/message-dialog 分支，2026-08-23；29 处调用全部迁移，执行偏差已回写本文）

## 0. 现状与目标

**现状**：WPF 宿主（`wpf/`）共 **29 处**原生 `MessageBox.Show` 调用（WPF `System.Windows.MessageBox` 24 处 + WinForms 2 处托盘 + 其他 3 处）。原生弹窗带系统标题栏和老式 Win32 图标，与应用 Aurora Bento 暗色设计语言完全割裂；信息组织平铺，机器信息（路径/错误原因）与人话结论混排。

**目标**：一套主题化消息弹窗组件替换全部 29 处——

1. 图标、配色、圆角、字体全部跟随应用设计令牌（明暗主题 × 三模式强调色自动适配）
2. 内容分层：标题定性 / 正文讲后果 / 技术详情折叠
3. 静态 API 模拟 `MessageBox.Show` 习惯，调用点机械替换、判断逻辑零改动

**范围**：仅 WPF 宿主。src/ WinForms 生产宿主的 36 处原生 MessageBox 本次不动（两套 UI 技术栈隔离，后续另行决策）。

## 1. 决策记录（brainstorm 确认项）

| 决策点 | 选择 | 落选项 |
|---|---|---|
| 布局 | **B · 居中纵向**——大图标居顶、标题/正文/按钮全居中、按钮等宽并排 | 经典横排 / 顶部色带+摘要卡 |
| 图标 | **裸图标 + 径向光晕**——无容器，32px 线性图标 + 严重级色径向柔光 | 软色方块 / 软色圆 / 实心徽章 |
| 按钮配色 | **强调色 + 仅 Danger 变红**——主按钮 PrimaryButton 随模式强调色，Danger 级换 DangerButton，次按钮 GhostButton | 主按钮跟随严重级 / 全描边轻量 |
| 内容显示 | **可折叠技术详情**——标题+正文分层，机器信息进等宽折叠区（默认收起） | 详情默认展开 / 现状平铺 |
| 实现方案 | **无边框模态小窗**——`WindowStyle=None` 圆角卡片 + ShowDialog | 主窗覆盖层（托盘场景不可用）/ 钩子换肤原生弹窗 |
| 改造范围 | 仅 WPF 宿主 29 处 | WinForms 一并改 / 仅 WinForms |

## 2. 组件与 API

新增文件（`wpf/Dialogs/`；执行修正：wpf csproj 为逐文件清单，新文件需补 `<Compile>`/`<Page>` 登记，见 §9）：

- **`MessageDialogWpf.xaml` + `.xaml.cs`** —— 弹窗本体
- **`MsgSeverity.cs`** —— `enum MsgSeverity { Info, Success, Warning, Danger }`、`enum MsgButtons { Ok, OkCancel, YesNo }`（含级别→图标/画刷/按钮样式映射纯函数，供自测）

### 2.1 静态入口（C# 5 语法，无新语言特性）

```csharp
// 形式一：显式标题+正文（新写文案用）
public static MessageBoxResult Show(Window owner, string title, string body,
    MsgSeverity severity, MsgButtons buttons,
    string detail, string okText, MessageBoxResult defaultResult)

// 形式二：单字符串自动拆分（drop-in 替换现有 MessageBox.Show）
public static MessageBoxResult Show(Window owner, string message,
    MsgSeverity severity, MsgButtons buttons, MessageBoxResult defaultResult)
```

- 返回值直接复用 `System.Windows.MessageBoxResult`，调用点只改调用本身，后续 `!= MessageBoxResult.Yes` 等判断原样保留
- `detail` 传 null → 技术详情折叠区整体隐藏；`okText` 传 null → 按钮组默认文案（Ok=“知道了”/OkCancel=“取消|确定”/YesNo=“否|是”）；`defaultResult` 决定 Enter 键触发哪个按钮，同时该按钮获得初始焦点
- **自动拆分规则**（形式二）：按首个空行（`\r\n\r\n` 或 `\n\n`）拆分——首段为标题，其余段合并为正文；无空行则整句为标题、正文区隐藏。首段超 24 字时截到段内最后一个句末标点（。！？!?)），无标点则硬截 24 字加省略号；首段以"确定/继续吗？"收尾的疑问句去掉该后缀改以"？"结尾。PolicyRuntime 的动态 `ConfirmKey` 文案因此免登记直接生效
- WinForms 托盘 2 处（`WpfRuntime.cs`）：owner 传主窗口引用，主窗隐藏时 CenterScreen 兜底

### 2.2 与现有对话框的关系

`AddGameDialogWpf` 等内容型对话框保持现状（系统 chrome + 主题化内容）；MessageDialogWpf 是**提示型浮层**，与主窗/启动屏的无边框语言（`WindowStyle=None`）一致。

## 3. 视觉规格

| 元素 | 规格 |
|---|---|
| 窗体 | `WindowStyle=None` + `ResizeMode=NoResize` + `SizeToContent=Height`，宽 400；卡片 Surface1 底、圆角 RadiusMd(18)、1px 严重级 Edge 描边；不使用 DropShadowEffect（本机实测完全不渲染），卡片层次 = 1px 严重级描边 + owner 遮罩（Adorner） |
| 图标 | 复用 `Icons.xaml` 现有四枚状态几何（IconInfo/IconCheck/IconWarn/IconError），32px、Stroke 1.8、圆角接头、严重级颜色，不新画图标 |
| 光晕 | 58px `RadialGradientBrush`（严重级色 ~18% → 70% 处透明），入场完成后 `Motion.BreathPulse` 微呼吸 |
| 标题 | FontSizeTitle(17) SemiBold TextPrimary，最多 2 行截断省略 |
| 正文 | 13px TextSecondary，行高 1.55，自动换行，居中 |
| 技术详情 | 折叠行（中性描边、圆角 10px、“技术详情 ▾/▴”）+ 展开区 Surface0 底 + FontMono 12px 左对齐，最高 200px 内部 ScrollViewer |
| 按钮区 | 等宽并排铺满；主按钮 PrimaryButton（随模式强调色）/ Danger 级换 DangerButton；次按钮 GhostButton；高 34 |
| 遮罩 | Show 前 owner 内容根部叠半透明黑 ~40% 遮罩（拦截鼠标），Closed 后移除；无 owner 不遮罩 |
| 动效 | 入场 FadeIn + 0.96→1 缩放 180ms EaseOut；退场 120ms 淡出后 Close；复用 Motion 现有方法 |
| 键盘 | Esc=取消/No（无取消键则=OK）；Enter=defaultResult 对应按钮；Tab 在按钮+折叠行间循环 |
| 无障碍 | 全元素 AutomationProperties.Name；弹窗容器设 AutomationProperties.HelpText=级别名 |
| 主题 | Soft/Edge/语义色画刷全部已有；Light/Dark × 三模式强调色自动跟随（DynamicResource），无需新增画刷 |

### 3.1 实施精修（评审循环中落地的细化）

- null owner 自动回落 `Application.Current.MainWindow`，恢复模态与遮罩；主窗隐藏（托盘）时遮罩自动跳过；
- 正文区限高 220px 内部滚动（ScrollViewer，动态长文案安全）；
- `SplitTitleBody` 追加规则：尾段仅为「继续吗？/确定吗？」等重复问句时移除；
- 光晕刷非纯色时兜底 Brushes.White；原生降级路径返回真实 MessageBox 结果；
- 主按钮字段/图标元素命名 `SeverityIcon`（避开 Window.Icon 冲突）。

## 4. 内容改写规则

1. **级别映射**：`Error→Danger`、`Warning/Question→Warning`、`Information/None→Info`；**定级规则**——后果不可逆（白名单重置、附加层删除）或重大安全受损（关闭 VBS）升 Danger；可逆操作（移除游戏条目、清着色器缓存）与"可恢复的安全换性能提示"（Defender 排除确认）维持 Warning；完成反馈用 Success。
2. **两段式文案**：首段（疑问句）提炼为标题；第二段降为正文补充说明。
3. **机器信息进 detail**：路径清单、错误原文（`WhitelistLastError`、`vm.AddFiles` 返回串等）进折叠区，正文只留人话结论。
4. **按钮动词化**：主按钮用具体动词（“全部取消”“恢复默认”“移除”“删除”“清理”“关闭 VBS”），不再一律“确定”；次按钮统一“取消”。
5. **i18n**：现有 Lang key 文案基本不变（拆分由运行时完成）；仅个别需要精修标题的调用点在调用处传显式标题+正文，不新增 Lang 机制。

## 5. 调用点映射总表（29 处）

### SettingsView（12 处）

| # | 位置 | 现文案/Key | 级别 | 按钮组 / 主按钮文案 | detail |
|---|---|---|---|---|---|
| 1 | L206 | 恢复三模式默认配色（硬编码） | Warning | YesNo / “恢复默认” | — |
| 2 | L266 | OnRestore ask（恢复已记录系统项） | Warning | YesNo / “恢复” | — |
| 3 | L300 | `def.unavailable` | Danger | Ok / “知道了” | — |
| 4 | L332 | `shader.confirm` | Warning | OkCancel / “清理” | — |
| 5 | L740 | `def.notours` | Info | Ok / “知道了” | — |
| 6 | L744 | `def.confirm`（游戏排除） | Warning | OkCancel / “排除” | 目录路径 |
| 7 | L770 | `def.failed` | Danger | Ok / “知道了” | — |
| 8 | L781 | `def.clearall.none` | Info | Ok / “知道了” | — |
| 9 | L785 | 全部取消 ask（硬编码） | Warning | OkCancel / “全部取消” | — |
| 10 | L810 | `def.clearall.done` | **Success** | Ok / “好” | — |
| 11 | L812 | `def.unavailable`（二次弹） | Danger | Ok / “知道了” | — |
| 12 | L1095 | `addon.confirm`（删附加层，不可撤销） | **Danger** | YesNo / “删除” | 目录路径 |

### WhitelistView（7 处）

| # | 位置 | 现文案 | 级别 | 按钮组 / 主按钮文案 | detail |
|---|---|---|---|---|---|
| 13-16 | L178/198/207/216 | `vm.AddFiles` 等返回的 error 串 | Danger | Ok / “知道了” | error 全文 |
| 17 | L192 | `white.required.locked` | Info | Ok / “知道了” | — |
| 18 | L223 | `white.reset.confirm`（无法撤销） | **Danger** | YesNo / “恢复默认” | — |
| 19 | L228 | 重置失败 error | Danger | Ok / “知道了” | error 全文 |

### EnvironmentView（3 处）

| # | 位置 | 现文案 | 级别 | 按钮组 / 主按钮文案 | detail |
|---|---|---|---|---|---|
| 20 | L43 | `vbs.needadmin`（权限不足） | Warning | Ok / “知道了” | — |
| 21 | L54 | `vbs.warn`（四段长文案） | **Danger** | OkCancel / “关闭 VBS”（执行修正：原代码即 OkCancel，草稿误写 YesNo，实现保留原按钮组） | —（多段正文合并显示） |
| 22 | L69 | VBS 操作失败 message | Danger | Ok / “知道了” | 失败原因 |

### 其他（7 处）

| # | 位置 | 现文案 | 级别 | 按钮组 / 主按钮文案 | detail |
|---|---|---|---|---|---|
| 23 | WpfRuntime L438 | `tray.resetask`（托盘恢复默认） | Warning | OkCancel / “恢复默认” | — |
| 24 | WpfRuntime L466 | `WhitelistLastError` | Danger | Ok / “知道了” | LastError 原文 |
| 25 | LogView L81 | `rep.clear.ask`（清空日志，归档保留） | Warning | YesNo / “清空”（执行修正：原代码即 YesNo，草稿误写 OkCancel，实现保留原按钮组） | — |
| 26 | LibraryView L183 | 移除游戏（可重新添加） | Warning | YesNo / “移除” | — |
| 27 | PolicyRuntime L56 | 动态 `ConfirmKey`（数据驱动） | Warning | OkCancel / “继续” | —（自动拆分） |
| 28 | GraphicsViewModel L242 | `winopt.failed` | Danger | Ok / “知道了” | — |
| 29 | AddGameDialogWpf L363 | 添加游戏 error | Danger | Ok / “知道了” | error 全文 |

注：行号为 2026-08-23 main（c34d140）快照，实施时以就近语义定位为准。

### 5.1 文案与交互策略备注（实施定稿）

- 弹窗文案采用调用点硬编码中文（与仓库现状一致；Lang key 仍由 WinForms 宿主使用，接受双份维护，后续如做多语言再统一收编）；
- YesNo 弹窗 Esc=No（原生 YesNo 无 Esc 行为，此为安全向差异）；
- 失败类弹窗标题按页面泛化（“白名单操作失败/环境项设置失败”），机器详情进折叠区。

## 6. 健壮性

- `detail` null → 折叠区隐藏；owner null → 无遮罩 + CenterScreen
- 仅 UI 线程调用（与现状 MessageBox 相同约束）；弹窗内部异常 catch 后记日志，不允许冒泡中断调用方流程
- 遮罩 Border 在 dialog Closed 事件里移除（含异常路径 finally）
- 光晕/呼吸动画用 compositor 安全的 RenderTransform + DoubleAnimation（与 Motion 现有实现同源），不做逐帧 CPU 动画

## 7. 测试与验收

- **自测新增**（selftest 框架，全量保持 0 失败基线）：
  - 级别→图标几何/画刷/按钮样式映射函数
  - 按钮组合→返回值/默认焦点映射
  - 单字符串自动拆分（有空行/无空行/超长首段/多段正文/空串）
  - **实际结果**：上述 3 项（+3 断言含尾问句裁剪）随 Task 1 落地，全量 TOTAL 232 / FAIL 0 / SKIP 3；启动冒烟与人工 10 项视觉清单见实施计划 Task 7（人工清单由人工执行）
- **编译**：`build-wpf.cmd` 零警告基线（沿用既有构建约束：32 位 MSBuild）
- **视觉人工验收清单**（GUI 渲染不可自动化——UIA/PrintWindow 对本程序不渲染的既有结论）：
  - 4 级别 × 3 按钮组合 × 明暗主题 × 三模式强调色矩阵抽查（至少 Info/Danger × 亮暗 × 靛蓝/暗金 全交叉）
  - 折叠区展开/收起、Enter/Esc/Tab、托盘无主窗弹出、长文案截断、detail 超高滚动

## 8. 不在本次范围

- src/ WinForms 宿主 36 处原生 MessageBox（技术栈隔离，后续单独决策）
- 内容型对话框（AddGame/DefenderExclusion/ReleaseNotes/RunningPicker/LolAddon）的窗口 chrome 主题化
- Toast/横幅类非模态反馈（页面内 FeedbackBanner 已覆盖，不重叠）

## 9. 风险与备注

- **C# 5 语法上限**（无字符串插值/表达式体/null 条件运算符），API 与实现均按 C# 5 编写
- **csproj 登记**（执行修正）：wpf csproj 为逐文件显式清单（无 glob），"免登记"判断有误；新增 Dialogs 文件均以 `<Compile>`/`<Page>` 行登记（7df20d5/3b4abcb），后续同目录新增文件同样需补登记
- **已解决**——WinForms 托盘线程调用 WPF 窗口：托盘菜单创建与事件泵均在 WPF UI 线程（App.xaml.cs `CreateTray`/`BuildTrayMenu` 在 UI 线程构造，既有托盘处理器已直接触碰 WPF UI），无跨线程风险，托盘 2 处已正常迁移
- 模态遮罩叠放：owner 根 Grid 动态追加/移除 Border，不与现有 GlassCard/DropShadow 冲突（遮罩无 Effect）
