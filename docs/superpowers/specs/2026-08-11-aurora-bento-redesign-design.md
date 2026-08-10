# Caelus Aurora Bento 视觉重构设计

日期：2026-08-11 · 状态：已获用户确认 · 路线：A（演进式）

## 1. 背景与目标

当前 WPF 界面（Phase 1-4 产物）为通用深色玻璃风格：纯色底 + 白透明度卡片 + 单强调色，功能完整但视觉平淡、无品牌个性。用户要求以前沿设计理念整体重构。

**目标方向：Aurora Bento**——深底极光光晕 + Bento 错落卡片 + 超大细字重排印 + 克制微动效 + 三模式品牌配色身份。

调研依据（2025-2026 趋势）：Bento Grid 成仪表盘默认布局；Glassmorphism 2.0（渐变描边 + 顶部高光 + 克制模糊）；深色优先、层级越高表面越亮、正文对比度 ≥4.5:1；辉光只给焦点态/徽章/CTA；等宽数字 + 大展示字号。标杆：Linear、Raycast、visionOS（质感）；NVIDIA App（工具克制感）；Riot Client（品牌个性）；SignalRGB（遥测密度）。

视觉基准稿：`.superpowers/brainstorm/1874-1786395437/content/aurora-v3-motion-brand.html`（用户已确认的实时预览）。

## 2. 已确认设计决策

| 决策 | 结论 |
|---|---|
| 总方向 | Aurora Bento（C）+ OLED 风的微光克制（D 局部融入） |
| 动效 | **全量保留**，本期不做「减弱动效」开关 |
| 浅色主题 | 本期**不重构**（现有浅色保持可用），深色为旗舰，后续完善 |
| Caelus Core 品牌核心 | **仅概览页 Hero 区**使用 |
| 配色 | **组件化**：主题契约 + 三模式预设 + 用户自定义主题文件入口 |
| 图标 | 几何线性体系（24 网格、2px 线宽、圆角接头），替代字符/Emoji 图标 |
| 实现路线 | 演进式：在现有 ResourceDictionary 分层架构上升级，零新依赖，不动 .NET 4.0 工具链 |

## 3. 主题契约 v2（配色组件化核心）

### 3.1 契约 key 分层（所有主题字典必须实现的完整集合）

| 层 | Key | 用途 |
|---|---|---|
| 光晕 | `AuroraPrimaryColor` / `AuroraSecondaryColor` / `AuroraTertiaryColor`（各带 `*FadeColor` 透明端） | AmbientLayer 三层径向渐变 |
| 光晕参数 | `AuroraPrimaryOpacity` / `AuroraSecondaryOpacity` / `AuroraTertiaryOpacity`、`AuroraDriftSeconds` | 强度与漂移节奏 |
| 强调梯度 | `AccentPrimaryColor` / `AccentSecondaryColor` | 渐变对：进度条、按钮、Core 弧环 |
| 强调派生 | `AccentSoftBrush`（底）/ `AccentEdgeBrush`（描边）/ `AccentGlowColor`（辉光）/ `OnAccentBrush`（强调色上的文字） | 徽章、选中态、主按钮 |
| 表面 | `BackgroundColor` / `Surface0Color` / `Surface1Color` / `Surface2Color` | 越高越亮的四级表面 |
| 描边 | `BorderSubtleBrush` / `BorderStrongBrush` / `TopHighlightBrush`（卡顶高光线） | 卡片层次 |
| 语义 | `Success/Warning/Danger/Info`（Color+Brush） | 沿用现有，不动 |
| 文字 | `TextPrimary/Secondary/Tertiary`（Color+Brush） | 值微调见 §3.3 |

旧 key（`ModeAccentBrush`、`Glass*` 系列、`Ambient*`）保留为**别名层**指向新契约，保证 11 个现有视图在过渡期不炸；视图逐页精修时再换用新 key。

### 3.2 三模式预设

| 模式 | Aurora 三层 | Accent 渐变对 | Glow | 气质 |
|---|---|---|---|---|
| 常规 Standard | `#5B3BE8` / `#2563EB` / `#0891B2` | `#67E8F9 → #818CF8` | `#67E8F9` | 冷静青紫极光 |
| 竞技 Competitive | `#E11D48` / `#F97316` / `#7C2D12` | `#FB7185 → #F97316` | `#FB7185` | 品红战斗 |
| 自定义 Custom | `#D4A847` / `#7C3AED` / `#8A5A18` | `#E9C46A → #D4A847` | `#E9C46A` | 琥珀金（致敬 v14） |

### 3.3 深色基底（Dark 字典）

- 背景 `#07080D`；窗口级径向层次（`#12172B → #0A0C14 → #07080D`）由 AmbientLayer v2 绘制
- 表面：`Surface0 #B40F121B`（卡片底，近实色深半透明，叠在光晕上等效 backdrop-blur）/ `Surface1 #CC161A26` / `Surface2 #D91D2230`
- 描边：`BorderSubtle #14FFFFFF` / `BorderStrong #2EFFFFFF` / `TopHighlight` 渐变 `#59FFFFFF → 透明`
- 文字：Primary `#F6F8FC` / Secondary `#99A0AE` / Tertiary `#6B7280`（正文对背景对比度 ≥4.5:1）

### 3.4 用户自定义主题入口

- 启动时探测应用目录 `Caelus.theme.xaml`；存在且通过契约完整性校验（§3.1 全部 key 齐全）则并入「我的配色」预设，在设置页可选
- 校验失败：记日志、忽略文件、回退默认，不崩启动
- 本期只做加载机制；设置页的导出/编辑 UI 属后续项

## 4. 视觉资产

### 4.1 `Themes/Icons.xaml` + `IconView` 控件

- 11 枚图标：概览(星)、游戏库(层叠)、优化策略(滑杆)、反作弊(盾)、白名单(勾选清单)、日志(横线)、显卡(芯片)、系统环境(圆点辐条)、系统体检(脉搏线)、设置(靶心)、关于(信息)
- 形式：24×24 网格的 `StreamGeometry` 线条路径资源；`IconView` 轻控件（`Key` / `Size` / 继承 `Foreground`），内部 `Path` + `StrokeThickness=2` + 圆角线帽
- 导航项、`LibraryView` 等逐页换用；Emoji/字符图标全部清除

### 4.2 `Themes/Motion.xaml` + `MotionHelper.cs`

全部基于 `RenderTransform`/`Opacity`（渲染线程，不影响布局，开销极低）：

| 动效 | 参数 | 目标 |
|---|---|---|
| AuroraDrift | 26s/32s/22s EaseInOut 交替，平移 ±40px + 缩放 1↔1.12 + 透明度呼吸 | AmbientLayer 三层光晕 |
| HoverLift | 250ms CubicEase Out，TranslateY 0→-3 + BorderStrong 提亮 | 所有可交互卡片 |
| ReadyPulse | 2.4s EaseInOut，Opacity 1↔0.45 | READY 徽章圆点 |
| CoreSpin | 14s 正向 / 22s 反向，Linear 无限 | CaelusCore 双环 |
| SparklineIn | 600ms 淡入 | 趋势线入场 |

明确不做：实时模糊动画（net4 无 GPU 模糊且违背性能红线）、数字滚动计数（改淡入）、粒子堆叠。

### 4.3 `Controls/CaelusCore.xaml`（仅概览页 Hero）

132×132 组合体：外虚线环（RotateTransform 14s，顶点一颗 Accent 亮点）+ 中层双弧环（反向 22s，`AccentPrimary→AccentSecondary` 渐变弧）+ 内刻度环 + 中心深色圆盘 + 闪电 `Path`（AccentGlow 辉光）+ 模式名小字。颜色全部 `DynamicResource`，随模式换肤。

### 4.4 `Controls/Sparkline.xaml`

`IList<double>` 数据点归一化 → `Polyline`；描边 Accent 渐变，面积填充 `AccentPrimary` 9%；供 GPU 温度卡等使用。

## 5. 卡片与排印升级

- **卡片 v2（`GlassCard` 样式）**：`BorderBrush` 直接用 `LinearGradientBrush`（顶部 22% 白 → 45% 处 5% → 底部 2%，WPF 原生支持渐变画刷描边）；模板内置顶部 1px 高光线（`Rectangle` 水平渐变，中段 `TopHighlight`）；圆角 14；底 `Surface0`；HoverLift 通过附加属性 `MotionHelper.Lift="True"` 挂载
- 已知老坑沿用：Style setter 里的 `Effect` 在 net4 不渲染——卡片悬停层次感靠**边框提亮 + 位移**表达，不用 DropShadow
- **排印**：`DisplayNumber`（`FontSizeHero=48`、`FontWeight.Light`、`Typography.NumeralAlignment=Tabular`）；`CardLabel`（9-10px Secondary 小标签）；`SectionTitle`（26px Semibold 页头）
- WPF 无字距 API：拉丁装饰标签用空格手工近似（如 `C A E L U S`），中文不处理——诚实降级

## 6. 概览页重构（示例案例）

布局（`OverviewView.xaml` 重写，ViewModel/绑定零改动）：

```
页头：SectionTitle「系统状态」+ 副标题 │ 胶囊分段控件（常规/竞技/自定义）
Bento R1：Hero 卡(1.9fr)〔READY 脉冲徽章 + 结论 + 详情按钮 + CaelusCore〕│ GPU 温度卡(1fr)〔48px 大数字 + Sparkline〕
Bento R2：目标帧率 │ 已用内存 │ 后台边界（各含 Accent 渐变进度条）
Bento R3：最近活动(1.4fr) │ 硬件摘要(1fr)
```

分段控件换肤：胶囊形容器，选中项 `#F2F5FA` 浅底 + `#0B0E16` 深字（以基准稿为准）。

## 7. 其余页面 Rollout

**5a 全局换肤**（改样式字典，全部页面自动受益）：
- 导航：IconView 图标 + 分组标题（「总览」「硬件与系统」）+ 选中态 Accent 渐变底 + Edge 描边 + 微辉光
- `PrimaryButton` 改 Accent 渐变填充；`PolicyToggle` 开态改 Accent 渐变轨道；`SegmentHost` 胶囊化；`LibraryItemCard`/`PolicyCard` 换 GlassCard v2
- 各页页头统一 SectionTitle + 副标题格式

**5b 逐页精修**：游戏库（列表卡密度与状态徽章）、体检报告（分数可视化）、设置（分组卡）、反作弊/环境/显卡（遥测行对齐），3 个对话框统一换肤。

## 8. 约束与边界

- net4 技术边界：无 backdrop-blur（用 Surface0 近实色半透明等效）、无字距 API（手工近似）、Style-Effect 不渲染（边框表达层次）、Stroke dash 动画不稳（Core 用 RotateTransform）
- 性能红线：动画全开后台 CPU ≈ 0-1%；禁止 BlurEffect 动画；动画全部走 RenderTransform
- 对比度：正文 ≥4.5:1；装饰性光晕不参与文字承载
- 浅色主题：现有字典保持可用，`ModeAccentOnLight*` 别名不动；本期不做浅色 Aurora 适配
- 本期不做：减弱动效开关、浅色重构、主题编辑 UI、托盘/实时指标（沿用既有遗留项清单）

## 9. 验证

- 自测：保持基线 172/0/3；新增契约完整性用例（扫描 Colors/Mode 各字典，§3.1 key 必须齐全；`Caelus.theme.xaml` 合法/非法样例各一）
- GUI 实机验证：11 页导航 + 渲染 + 关键交互（沿用 Phase 4 方法，规避已知陷阱：DropShadowEffect 不渲染截图、UIA/PrintWindow 限制、LOL/托盘干扰）
- 动效目测：漂移/脉冲/旋转/浮起流畅无跳帧；模式切换整套换肤正确
- 性能抽查：动画全开空闲 5 分钟，任务管理器 CPU 占用记录

## 10. 里程碑

| 里程碑 | 内容 | 验收 |
|---|---|---|
| M1 | 主题契约 v2 + Dark 基底 + 三模式预设 + AmbientLayer v2 + 契约自测 | 自测通过，三模式换肤正确 |
| M2 | Icons/IconView、Motion、CaelusCore、Sparkline | 控件在测试页渲染正确 |
| M3 | 卡片 v2 + 排印 + **概览页重构**（示例案例交付点） | 与基准稿视觉一致，GUI 验证 |
| M4 | 5a 全局换肤（导航/按钮/开关/分段/页头） | 全 11 页渲染 + 自测基线 |
| M5 | 5b 逐页精修 + 对话框 + 实机验证 + 性能抽查 | Phase 5 验收报告 |

## 附：调研来源

midrocket UI 趋势 2026、onething/atmos 深色模式最佳实践、fluent2.microsoft.design（材质）、learn.microsoft.com WPF .NET 9 Fluent、lepoco/wpfui、iNKORE UI.WPF.Modern、NVIDIA App FAQ、Riot Client UI Kit（Behance）、FanControl 主题机制、Pantone 2025/2026 年度色。
