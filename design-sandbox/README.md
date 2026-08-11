# Caelus 设计沙盒（HTML Design Sandbox）

WPF Aurora Bento 设计系统的浏览器镜像。**AI 在这里秒级迭代视觉，定稿后翻译回 XAML**，
解决"改 XAML → 编译 → 启动 → 截图"分钟级反馈循环的瓶颈。

## 快速开始

直接用浏览器打开（无需构建、无需服务器）：

```
design-sandbox/index.html      — 组件展示页（所有核心组件 + 状态）
design-sandbox/overview.html   — 应用壳 + 概览页骨架
```

右下角工具条实时切换 **主题（深/浅）× 模式（巡航/竞技/自定义）**，选择持久化在 localStorage。

## AI 迭代工作流

```
1. 迭代   AI 改 tokens.css / sandbox.css / *.html → 浏览器刷新 → 截图自检（秒级）
2. 定稿   视觉确认后，按下方映射表把改动翻译回 wpf/Themes/*.xaml 与视图 XAML
3. 验证   build-wpf.cmd → 现有截图/自测流水线跑一次（一次即可，不再反复）
```

**纪律：XAML 是源头。** 视觉实验可以在沙盒里随意做，但定稿必须回写 `wpf/Themes/`，
并同步更新沙盒。两边只改一边 = 沙盒腐化失效。

## 文件职责

| 文件 | 对应 WPF 源头 | 说明 |
|---|---|---|
| `tokens.css` | `wpf/Themes/Tokens.xaml` + `Colors.*.xaml` + `Mode.*.xaml` | 全部设计令牌，CSS variables |
| `sandbox.css` | `wpf/Themes/Styles.xaml` + `Controls/*.xaml` | 组件库，类名 ↔ Style 键一一对应 |
| `sandbox.js` | `ThemeManager.cs` / `SegmentedControl.xaml.cs` | 主题/模式切换 + 演示交互 |
| `index.html` | 各控件样式 | 组件展示页 |
| `overview.html` | `MainWindow.xaml` + `Views/OverviewView.xaml` | 应用壳 + 概览骨架 |

## 主题机制映射

| WPF | CSS |
|---|---|
| ThemeManager 合并 Colors.Dark/Light | `<html data-theme="dark\|light">` |
| ThemeManager 合并 Mode.Standard/Competitive/Custom | `<html data-mode="standard\|competitive\|custom">` |
| `ModeAccentBrush`（DynamicResource 按主题解析 OnDark/OnLight） | `--mode-accent`（按 `[data-theme]` 解析到 `--mode-accent-on-*`） |
| XAML `#AARRGGBB` | CSS `#RRGGBBAA`（Alpha 移到末尾） |
| WPF px @96DPI | CSS px（1:1） |

## 组件映射表（翻译回 XAML 时对照）

| CSS 类 | XAML Style 键 | 备注 |
|---|---|---|
| `.card` | `CardBorder` | Surface0 + BorderSubtle + RadiusMd |
| `.card-hero` | `HeroCardBorder` | Surface1 |
| `.glass-card` | `GlassCard` | 渐变描边用 mask 环形实现 |
| `.settings-group` / `.settings-row` | `SettingsGroup` / `SettingsRow` | 行间距细分隔线 |
| `.metric-panel` | `MetricPanel` | |
| `.status-badge` / `.status-banner` | `StatusBadge` / `StatusBanner` | |
| `.btn-primary` / `.btn-ghost` / `.btn-danger` | `PrimaryButton` / `GhostButton` / `DangerButton` | |
| `.win-btn` / `.win-btn-close` | `WindowButton` / `CloseWindowButton` | |
| `.nav-item.is-checked` | `NavItem`（IsChecked 触发器） | |
| `.nav-group-label` | `NavGroupLabel` | |
| `.segment-host` / `.segment-item` / `.segment-indicator` | `SegmentedControl`（滑动指示器） | 选中 = accent-soft 底 + mode-accent 字 |
| `input.toggle` | `PolicyToggle` | 38×22，滑块 16px |
| `.input` | `InputBox` | |
| `.list-item` | `ListItem` | |
| `.display-number` / `.metric-number` | `DisplayNumber` / `MetricNumber` | tabular-nums |
| `.page-header` / `.page-subtitle` / `.card-label` | `PageHeader` / `PageSubtitle` / `CardLabel` | |
| `.caelus-core` | `Controls/CaelusCore.xaml` | SVG 复刻三环 + 辉光 |
| `.aurora` / `.aurora-blob-*` | `Controls/AmbientLayer` + Mode 光晕 | blur(64px) 等效径向渐变 |
| `:focus-visible` | `FocusRing` | 2px mode-accent |
| `::-webkit-scrollbar` | `ScrollBar` 样式 | 10px 细条 |

## 已知差异（浏览器 vs WPF 渲染）

- Aurora 光晕：CSS 用 `blur(64px)` 近似 WPF 径向渐变画刷，边缘略柔，属正常差异。
- 半透明表面在浏览器直接合成；WPF 的 `SnapsToDevicePixels` 无对应物，1px 描边在
  非整数 DPI 下可能比 WPF 略细。
- 字体度量：浏览器与 WPF 的行高/字距有微小差异，翻译间距时以令牌值为准，不要照抄
  浏览器里"看起来差不多"的 magic number。
- **渐变文字（回写陷阱，已踩过）**：CSS `background-clip:text` 的渐变标题不能翻译成
  "Colors 字典画刷 + GradientStop 嵌套 DynamicResource"——字典实例被缓存后嵌套停止
  不会随换槽重解析（标题会卡在首次解析的模式色）。正确做法：在各 `Mode.*.xaml` 定义
  `HeroTitleOnDarkBrush`/`HeroTitleOnLightBrush` 静态双档画刷，视图 code-behind 按
  `ThemeManager.CurrentTone` 选用并订阅 `ModeChanged`（参照 OverviewView.ApplyHeroTitle）。
  嵌套 DynamicResource 只在 SolidColorBrush.Color（如 ModeAccentBrush）或控件模板
  内联渐变停止（如 HeroGlassCard 角部光晕）两种形态下可靠。

## 扩展新页面

复制 `overview.html` 的壳（aurora + app-shell + toolbar），替换 `.content-inner`
内容即可。新组件先在 `sandbox.css` 按令牌实现，确认后再回写 XAML。
