# Phase 1.5 实现计划：Radium 座舱化（视觉材质层演进）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把已验收的 WPF 预览（Phase 1）从扁平 Apple 克制风升级为 Radium 驾驶舱材质——三层材质（环境光/玻璃面板/语义发光）、模式氛围联动、双轴主题，不改信息架构与任何自测逻辑。

**Architecture:** 主题系统从单轴（tone）升级为双轴（tone × mode），MergedDictionaries 四槽：Tokens + Styles + Colors.{Light|Dark} + Mode.{Standard|Competitive|Custom}。环境层是 `AmbientLayer` UserControl（两对 Ellipse 交替交叉淡入）。玻璃材质用 alpha 分层实现（无 backdrop blur）。模式切换经 `ModeController` 编排：ThemeManager 换槽 + 氛围过渡 + Settings 持久化 + ViewModel 刷新。

**Tech Stack:** C# / WPF（net40 目标，C# 5 语法）、系统 MSBuild 4.0、既有自测框架（`dev.cmd test`，当前基线 163 PASS / 0 FAIL / 3 SKIP）。

**规格文档:** `docs/superpowers/specs/2026-08-10-radium-cockpit-design.md`（本计划逐条落实其 §4-§9）

---

## 环境事实（执行前必读）

- 工作目录是 worktree：`E:/A_Project/Pavise-Game/.worktrees/ui-redesign-phase1`，分支 `feature/ui-redesign-phase1`。**不要**在主仓库目录工作。
- 自测：`cmd //c "dev.cmd test"`，当前基线 `TOTAL 166  PASS 163  FAIL 0  SKIP 3`。
- WPF 构建：`cmd //c build-wpf.cmd` → `wpf\bin\Release\CaelusWpf.exe`。
- 截图探针：`./wpf/bin/Release/CaelusWpf.exe --wpf-shot <dir>`，当前输出深浅 2 张 PNG（本计划扩展为 4 张矩阵）。
- WPF 项目是 net40 + 旧式 csc：**C# 5 语法上限**（无字符串插值、无表达式体成员、无 null 条件运算符、无 `nameof`）。对象初始化器、`var`、Lambda 可用。
- 模式持久化：预览宿主不构造 GameMode 实例，直接用 `Settings.SaveStr("PerformancePreset", <int>)` 持久化——这与 `GameMode.Preset` setter（`src/Core/GameMode.cs:443-453`）的持久化行为完全一致；`RequestPolicyApply` 只在 GameMode 运行时才有意义，预览宿主不运行它。正式宿主接管时换成 `gameMode.Preset = ...`。
- `PerformancePreset` 枚举（`src/Core/Detection/GameProfiles.cs:11`）：Standard=0, Competitive=1, Custom=2（执行时先读文件确认顺序再写映射）。
- `Settings.LoadStr(key, default)` / `Settings.SaveStr(key, value)` 在 `src/Platform/Settings.cs`，WPF 项目已链接编译。
- 现有 WPF 类型（Phase 1 已建）：`ThemeManager.Apply(Application, UiTone)`、`Motion.FadeIn`/`Motion.Reduced`/`Motion.Enabled`、`KeyBrushConverter`/`KeySoftBrushConverter`/`FractionGridLengthConverter`（`wpf/Converters.cs`）、`OverviewViewModel`/`SampleOverviewSource`/`IOverviewSource`（`src/UiShared/OverviewViewModel.cs`）、`UiMotion`（`src/UiShared/UiMotion.cs`）。
- 现有样式键（Phase 1 已建）：`CardBorder`、`PrimaryButton`、`GhostButton`、`NavItem`、`SegmentItem`、`SegmentHost`（`wpf/Themes/Styles.xaml`）。
- 所有 `.cmd` 文件 ASCII ONLY。

## 文件结构

| 文件 | 责任 |
|------|------|
| `src/UiShared/ModePalette.cs` | 新建：AppMode 枚举、三模式氛围色表、DisplayName、FromPreset 映射 |
| `tests/SelfTests.UiShared.cs` | 追加 3 个 ModePalette 测试 |
| `tests/SelfTests.cs` | 注册 3 个新测试（锚点：Task 6 注册的最后一行概览 VM 测试之后） |
| `wpf/Themes/Mode.Standard.xaml` / `Mode.Competitive.xaml` / `Mode.Custom.xaml` | 新建：模式氛围色资源（Ambient 两色 + 零透明度孪生 + ModeAccent 深浅两档） |
| `wpf/Themes/Colors.Dark.xaml` | 追加：玻璃画刷（Glass*）、环境透明度 Double、ModeAccentBrush 别名 |
| `wpf/Themes/Colors.Light.xaml` | 追加：同上（浅色配方） |
| `wpf/ThemeManager.cs` | 升级：双轴 Apply(app, tone, mode) + CurrentTone/CurrentMode 状态 |
| `wpf/Controls/AmbientLayer.xaml(.cs)` | 新建：两对 Ellipse 的环境光控件，TransitionTo 交叉淡入 |
| `wpf/MainWindow.xaml` | 修改：根 Grid 底层放 AmbientLayer；模式分段控件加 x:Name 与 Checked 事件 |
| `wpf/MainWindow.xaml.cs` | 修改：启动时初始化氛围层；ModeChecked 处理器 |
| `wpf/ModeController.cs` | 新建：模式切换编排（持久化 + 主题 + 氛围 + VM） |
| `wpf/Themes/Styles.xaml` | 修改：玻璃化升级（CardBorder/NavItem/SegmentItem/SegmentHost/PrimaryButton） |
| `wpf/Views/OverviewView.xaml` | 修改：结论图标发光、进度条发光（材质替换，布局不变） |
| `wpf/Converters.cs` | 追加 KeyColorConverter（语义键 → Color，供发光 Effect 绑定） |
| `src/UiShared/OverviewViewModel.cs` | SampleOverviewSource 增加 SetMode（仅改示例数据源） |
| `wpf/App.xaml.cs` | 修改：启动读持久化模式；RunShot 输出 4 张矩阵 |
| `wpf/Caelus.Wpf.csproj` | 注册新文件 |

---

### Task 1: ModePalette 纯逻辑与自测

**Files:**
- Create: `src/UiShared/ModePalette.cs`
- Test: `tests/SelfTests.UiShared.cs`（追加）
- Modify: `tests/SelfTests.cs`（在概览 VM 三个测试注册行之后追加）

- [ ] **Step 1: 写失败测试**

`tests/SelfTests.UiShared.cs` 追加（文件末尾 class 内）：

```csharp
        private static void TestModePaletteCompleteness()
        {
            foreach (AppMode mode in new[] { AppMode.Standard, AppMode.Competitive, AppMode.Custom })
            {
                ModeColors c = ModePalette.For(mode);
                string[] all = { c.AmbientPrimary, c.AmbientSecondary, c.ModeAccentOnDark, c.ModeAccentOnLight };
                foreach (string hex in all)
                {
                    if (String.IsNullOrEmpty(hex)) throw new Exception("empty mode token in " + mode);
                    Eq(7, hex.Length);
                    Eq('#', hex[0]);
                }
            }
            Eq("常规", ModePalette.DisplayName(AppMode.Standard));
            Eq("竞技", ModePalette.DisplayName(AppMode.Competitive));
            Eq("自定义", ModePalette.DisplayName(AppMode.Custom));
            // 与 PerformancePreset 的映射往返一致
            Eq(AppMode.Standard, ModePalette.FromPreset(PerformancePreset.Standard));
            Eq(AppMode.Competitive, ModePalette.FromPreset(PerformancePreset.Competitive));
            Eq(AppMode.Custom, ModePalette.FromPreset(PerformancePreset.Custom));
        }

        private static void TestModePaletteDistinct()
        {
            ModeColors a = ModePalette.For(AppMode.Standard);
            ModeColors b = ModePalette.For(AppMode.Competitive);
            ModeColors c = ModePalette.For(AppMode.Custom);
            if (a.ModeAccentOnDark == b.ModeAccentOnDark || b.ModeAccentOnDark == c.ModeAccentOnDark
                || a.ModeAccentOnDark == c.ModeAccentOnDark)
                throw new Exception("mode accents must be mutually distinct");
            if (a.AmbientPrimary == b.AmbientPrimary || b.AmbientPrimary == c.AmbientPrimary
                || a.AmbientPrimary == c.AmbientPrimary)
                throw new Exception("ambient primaries must be mutually distinct");
            // 巡航青与战备红必须色相距足够远，避免氛围混淆
            int[] cyan = Rgb(a.AmbientPrimary);
            int[] red = Rgb(b.AmbientPrimary);
            int dist = Math.Abs(cyan[0] - red[0]) + Math.Abs(cyan[1] - red[1]) + Math.Abs(cyan[2] - red[2]);
            if (dist < 200) throw new Exception("cruise/combat ambient too close: " + dist);
        }

        private static void TestModeAccentContrast()
        {
            // ModeAccent 用于选中导航文字：深浅两档对各自主题 Background 均需 ≥4.5:1
            ThemeColors dark = Palette.For(UiTone.Dark);
            ThemeColors light = Palette.For(UiTone.Light);
            foreach (AppMode mode in new[] { AppMode.Standard, AppMode.Competitive, AppMode.Custom })
            {
                ModeColors c = ModePalette.For(mode);
                double d = Contrast(c.ModeAccentOnDark, dark.Background);
                if (d < 4.5) throw new Exception(mode + " accent/dark-bg contrast " + d.ToString("0.00"));
                double l = Contrast(c.ModeAccentOnLight, light.Background);
                if (l < 4.5) throw new Exception(mode + " accent/light-bg contrast " + l.ToString("0.00"));
            }
        }

        private static int[] Rgb(string hex)
        {
            return new int[]
            {
                Convert.ToInt32(hex.Substring(1, 2), 16),
                Convert.ToInt32(hex.Substring(3, 2), 16),
                Convert.ToInt32(hex.Substring(5, 2), 16)
            };
        }
```

`tests/SelfTests.cs` 注册（在 `test("概览 VM：查看详情命令往返切换", ...)` 行之后）：

```csharp
            test("模式色板：三模式 Token 齐全、显示名与预设映射正确", TestModePaletteCompleteness);
            test("模式色板：三模式互异且巡航/战备色相距足够远", TestModePaletteDistinct);
            test("模式色板：ModeAccent 深浅两档对比度达到 AA", TestModeAccentContrast);
```

- [ ] **Step 2: 运行测试确认失败**

`cmd //c "dev.cmd test"`
预期：FAIL（`AppMode`/`ModeColors`/`ModePalette` 不存在，编译错误）。

- [ ] **Step 3: 最小实现**

新建 `src/UiShared/ModePalette.cs`（色值严格按规格 §4.3）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 驾驶舱模式氛围色板：巡航青 / 战备红 / 工程紫（规格 §4.3）

using System;

namespace CaelusApp
{
    internal enum AppMode
    {
        Standard,
        Competitive,
        Custom
    }

    internal sealed class ModeColors
    {
        public string AmbientPrimary;
        public string AmbientSecondary;
        public string ModeAccentOnDark;
        public string ModeAccentOnLight;
    }

    internal static class ModePalette
    {
        private static readonly ModeColors standard = new ModeColors
        {
            AmbientPrimary = "#1FB6D6",
            AmbientSecondary = "#2E7DD1",
            ModeAccentOnDark = "#3EC9FF",
            ModeAccentOnLight = "#0E7490"
        };

        private static readonly ModeColors competitive = new ModeColors
        {
            AmbientPrimary = "#E5484D",
            AmbientSecondary = "#C22E3E",
            ModeAccentOnDark = "#FF6B74",
            ModeAccentOnLight = "#DC2626"
        };

        private static readonly ModeColors custom = new ModeColors
        {
            AmbientPrimary = "#8B5CF6",
            AmbientSecondary = "#6D4AC8",
            ModeAccentOnDark = "#A78BFA",
            ModeAccentOnLight = "#7C3AED"
        };

        public static ModeColors For(AppMode mode)
        {
            if (mode == AppMode.Competitive) return competitive;
            if (mode == AppMode.Custom) return custom;
            return standard;
        }

        public static string DisplayName(AppMode mode)
        {
            if (mode == AppMode.Competitive) return "竞技";
            if (mode == AppMode.Custom) return "自定义";
            return "常规";
        }

        public static AppMode FromPreset(PerformancePreset preset)
        {
            if (preset == PerformancePreset.Competitive) return AppMode.Competitive;
            if (preset == PerformancePreset.Custom) return AppMode.Custom;
            return AppMode.Standard;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

`cmd //c "dev.cmd test"`
预期：`TOTAL 169  PASS 166  FAIL 0  SKIP 3`。
若 `TestModeAccentContrast` 失败（某档对比度不足 4.5:1），按 Phase 1 Task 2 先例处理：同色相微调加深/提亮至达标，同步更新本文件与规格 §4.3 表，报告 DONE_WITH_CONCERNS 并注明新色值。

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/ModePalette.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "feat: ModePalette 模式氛围色板（巡航/战备/工程，规格 §4.3）"
```

---

### Task 2: 模式资源字典与双轴 ThemeManager

**Files:**
- Create: `wpf/Themes/Mode.Standard.xaml`、`wpf/Themes/Mode.Competitive.xaml`、`wpf/Themes/Mode.Custom.xaml`
- Modify: `wpf/Themes/Colors.Dark.xaml`、`wpf/Themes/Colors.Light.xaml`
- Modify: `wpf/ThemeManager.cs`
- Modify: `wpf/Caelus.Wpf.csproj`

- [ ] **Step 1: 三个模式字典**

`wpf/Themes/Mode.Standard.xaml`（Fade 孪生 = 同 RGB、alpha 00，供渐变末端的透明停靠点；net4 的 GradientStop 无 Opacity 属性，必须用带 alpha 的 Color）：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Color x:Key="AmbientPrimaryColor">#1FB6D6</Color>
  <Color x:Key="AmbientPrimaryFadeColor">#001FB6D6</Color>
  <Color x:Key="AmbientSecondaryColor">#2E7DD1</Color>
  <Color x:Key="AmbientSecondaryFadeColor">#002E7DD1</Color>
  <Color x:Key="ModeAccentOnDarkColor">#3EC9FF</Color>
  <Color x:Key="ModeAccentOnLightColor">#0E7490</Color>
  <RadialGradientBrush x:Key="AmbientPrimaryBrush" Center="0.5,0.5" RadiusX="0.5" RadiusY="0.5">
    <GradientStop Color="{DynamicResource AmbientPrimaryColor}" Offset="0"/>
    <GradientStop Color="{DynamicResource AmbientPrimaryFadeColor}" Offset="1"/>
  </RadialGradientBrush>
  <RadialGradientBrush x:Key="AmbientSecondaryBrush" Center="0.5,0.5" RadiusX="0.5" RadiusY="0.5">
    <GradientStop Color="{DynamicResource AmbientSecondaryColor}" Offset="0"/>
    <GradientStop Color="{DynamicResource AmbientSecondaryFadeColor}" Offset="1"/>
  </RadialGradientBrush>
  <SolidColorBrush x:Key="ModeAccentOnDarkBrush" Color="{DynamicResource ModeAccentOnDarkColor}"/>
  <SolidColorBrush x:Key="ModeAccentOnLightBrush" Color="{DynamicResource ModeAccentOnLightColor}"/>
</ResourceDictionary>
```

`wpf/Themes/Mode.Competitive.xaml`：同构，色值替换为 `AmbientPrimary=#E5484D`（Fade `#00E5484D`）、`AmbientSecondary=#C22E3E`（Fade `#00C22E3E`）、`ModeAccentOnDark=#FF6B74`、`ModeAccentOnLight=#DC2626`。

`wpf/Themes/Mode.Custom.xaml`：同构，色值替换为 `AmbientPrimary=#8B5CF6`（Fade `#008B5CF6`）、`AmbientSecondary=#6D4AC8`（Fade `#006D4AC8`）、`ModeAccentOnDark=#A78BFA`、`ModeAccentOnLight=#7C3AED`。

- [ ] **Step 2: Colors.Dark.xaml 追加玻璃配方键**

在 `wpf/Themes/Colors.Dark.xaml` 的画刷区末尾（`TextTertiaryBrush` 之后）追加（alpha 已换算为 hex，注释标注意图）：

```xml
  <!-- 玻璃面板填充（规格 §4.1 深色配方）：导航 5% / 卡片 6% / 主卡片 8% / 浮起 10% -->
  <SolidColorBrush x:Key="GlassNavBrush" Color="#0DFFFFFF"/>
  <SolidColorBrush x:Key="GlassCardBrush" Color="#0FFFFFFF"/>
  <SolidColorBrush x:Key="GlassHeroBrush" Color="#14FFFFFF"/>
  <SolidColorBrush x:Key="GlassRaisedBrush" Color="#1AFFFFFF"/>
  <!-- 玻璃边框：常规 10% / 高层 14% / 顶部内高光 12% / 进度条轨道 8% -->
  <SolidColorBrush x:Key="GlassBorderBrush" Color="#1AFFFFFF"/>
  <SolidColorBrush x:Key="GlassBorderHiBrush" Color="#24FFFFFF"/>
  <SolidColorBrush x:Key="InnerHighlightBrush" Color="#1FFFFFFF"/>
  <SolidColorBrush x:Key="TrackBrush" Color="#14FFFFFF"/>
  <!-- 环境光强度（规格 §4.1：深色 主 13% / 次 8%） -->
  <sys:Double x:Key="AmbientPrimaryOpacity" xmlns:sys="clr-namespace:System;assembly=mscorlib">0.13</sys:Double>
  <sys:Double x:Key="AmbientSecondaryOpacity" xmlns:sys="clr-namespace:System;assembly=mscorlib">0.08</sys:Double>
  <!-- 模式强调色别名：深色主题指向 OnDark 档（DynamicResource 运行时解析） -->
  <SolidColorBrush x:Key="ModeAccentBrush" Color="{DynamicResource ModeAccentOnDarkColor}"/>
```

- [ ] **Step 3: Colors.Light.xaml 追加浅色配方**

在 `wpf/Themes/Colors.Light.xaml` 画刷区末尾追加（规格 §4.1 浅色配方：填充白 55-75%、边框黑 6-10%）：

```xml
  <!-- 玻璃面板填充（浅色日间磨砂配方） -->
  <SolidColorBrush x:Key="GlassNavBrush" Color="#8CFFFFFF"/>
  <SolidColorBrush x:Key="GlassCardBrush" Color="#A6FFFFFF"/>
  <SolidColorBrush x:Key="GlassHeroBrush" Color="#BFFFFFFF"/>
  <SolidColorBrush x:Key="GlassRaisedBrush" Color="#BFFFFFFF"/>
  <!-- 浅色边框用黑色 alpha：常规 6% / 高层 10% / 内高光白 60% / 轨道黑 8% -->
  <SolidColorBrush x:Key="GlassBorderBrush" Color="#0F000000"/>
  <SolidColorBrush x:Key="GlassBorderHiBrush" Color="#1A000000"/>
  <SolidColorBrush x:Key="InnerHighlightBrush" Color="#99FFFFFF"/>
  <SolidColorBrush x:Key="TrackBrush" Color="#14000000"/>
  <!-- 环境光强度（浅色 主 16% / 次 10%） -->
  <sys:Double x:Key="AmbientPrimaryOpacity" xmlns:sys="clr-namespace:System;assembly=mscorlib">0.16</sys:Double>
  <sys:Double x:Key="AmbientSecondaryOpacity" xmlns:sys="clr-namespace:System;assembly=mscorlib">0.10</sys:Double>
  <SolidColorBrush x:Key="ModeAccentBrush" Color="{DynamicResource ModeAccentOnLightColor}"/>
```

注意：`xmlns:sys` 内联声明与 Tokens.xaml 的做法一致（旧 XAML 编译器对内联命名空间兼容良好）。

- [ ] **Step 4: ThemeManager 双轴升级**

`wpf/ThemeManager.cs` 替换为：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 宿主主题切换：双轴（明暗 tone × 飞行模式 mode）四槽资源字典

using System;
using System.Windows;

namespace CaelusApp.WpfHost
{
    internal static class ThemeManager
    {
        private static ResourceDictionary colors;
        private static ResourceDictionary mode;

        public static UiTone CurrentTone { get; private set; }
        public static AppMode CurrentMode { get; private set; }

        public static void Apply(Application app, UiTone tone, AppMode appMode)
        {
            var merged = app.Resources.MergedDictionaries;

            string colorsUri = tone == UiTone.Light
                ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml";
            var nextColors = new ResourceDictionary
            {
                Source = new Uri(colorsUri, UriKind.Relative)
            };
            if (colors != null) merged.Remove(colors);
            merged.Add(nextColors);
            colors = nextColors;
            CurrentTone = tone;

            string modeUri = modeUriFor(appMode);
            var nextMode = new ResourceDictionary
            {
                Source = new Uri(modeUri, UriKind.Relative)
            };
            if (mode != null) merged.Remove(mode);
            merged.Add(nextMode);
            mode = nextMode;
            CurrentMode = appMode;

            Native.LightModeQuery = () => tone == UiTone.Light;
        }

        private static string modeUriFor(AppMode appMode)
        {
            if (appMode == AppMode.Competitive) return "Themes/Mode.Competitive.xaml";
            if (appMode == AppMode.Custom) return "Themes/Mode.Custom.xaml";
            return "Themes/Mode.Standard.xaml";
        }
    }
}
```

注意：本任务先不改调用方（`App.xaml.cs` 仍调旧签名 `Apply(this, UiTone.Light)`），下一任务统一改。因此本 Step 完成后**编译会暂时失败**（旧签名不存在）——同时把 `wpf/App.xaml.cs` 中两处调用临时改为 `ThemeManager.Apply(this, UiTone.Light, AppMode.Standard)`（正常启动分支与 RunShot 内的 `ThemeManager.Apply(this, tone)`），保证编译通过；Task 3/6/7 再做完整接线。

- [ ] **Step 5: csproj 注册**

`wpf/Caelus.Wpf.csproj` 的 Page 列表追加：

```xml
    <Page Include="Themes\Mode.Standard.xaml" />
    <Page Include="Themes\Mode.Competitive.xaml" />
    <Page Include="Themes\Mode.Custom.xaml" />
```

- [ ] **Step 6: 构建验证**

`cmd //c build-wpf.cmd`
预期：`WPF Build OK`。

- [ ] **Step 7: 自测回归**

`cmd //c "dev.cmd test"`
预期：`TOTAL 169  PASS 166  FAIL 0  SKIP 3`（UiShared 未动，与 Task 1 后一致）。

- [ ] **Step 8: Commit**

```bash
git add wpf/
git commit -m "feat: 模式资源字典与双轴 ThemeManager（四槽主题架构）"
```

---

### Task 3: AmbientLayer 环境光控件

**Files:**
- Create: `wpf/Controls/AmbientLayer.xaml`、`wpf/Controls/AmbientLayer.xaml.cs`
- Modify: `wpf/MainWindow.xaml`（根 Grid 底层放置）
- Modify: `wpf/Caelus.Wpf.csproj`

- [ ] **Step 1: AmbientLayer 控件**

`wpf/Controls/AmbientLayer.xaml`：

```xml
<UserControl x:Class="CaelusApp.WpfHost.Controls.AmbientLayer"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             IsHitTestVisible="False">
  <Grid>
    <!-- 两对光域交替：前对显示当前氛围，后对承载新模式淡入 -->
    <Ellipse x:Name="FrontPrimary" Width="680" Height="680" HorizontalAlignment="Right"
             VerticalAlignment="Top" Margin="0,-260,-180,0" Opacity="0"/>
    <Ellipse x:Name="FrontSecondary" Width="520" Height="520" HorizontalAlignment="Left"
             VerticalAlignment="Bottom" Margin="40,0,0,-200" Opacity="0"/>
    <Ellipse x:Name="BackPrimary" Width="680" Height="680" HorizontalAlignment="Right"
             VerticalAlignment="Top" Margin="0,-260,-180,0" Opacity="0"/>
    <Ellipse x:Name="BackSecondary" Width="520" Height="520" HorizontalAlignment="Left"
             VerticalAlignment="Bottom" Margin="40,0,0,-200" Opacity="0"/>
  </Grid>
</UserControl>
```

`wpf/Controls/AmbientLayer.xaml.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 驾驶舱环境光层：两对光域交替的模式氛围交叉淡入（规格 §4.1/§6）

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CaelusApp.WpfHost.Controls
{
    public partial class AmbientLayer : UserControl
    {
        // front=true 表示 Front 对当前可见
        private bool frontVisible;

        public AmbientLayer()
        {
            InitializeComponent();
        }

        // 立即显示当前主题的氛围（启动时用，无动画）
        public void Show()
        {
            ApplyBrushes(FrontPrimary, FrontSecondary);
            FrontPrimary.Opacity = TargetPrimary();
            FrontSecondary.Opacity = TargetSecondary();
            BackPrimary.Opacity = 0;
            BackSecondary.Opacity = 0;
            frontVisible = true;
        }

        // 模式切换后的氛围过渡：后对绑定新画刷淡入，前对淡出
        public void TransitionTo(bool animate)
        {
            Ellipse newPrimary = frontVisible ? BackPrimary : FrontPrimary;
            Ellipse newSecondary = frontVisible ? BackSecondary : FrontSecondary;
            Ellipse oldPrimary = frontVisible ? FrontPrimary : BackPrimary;
            Ellipse oldSecondary = frontVisible ? FrontSecondary : BackSecondary;

            ApplyBrushes(newPrimary, newSecondary);

            if (!animate || Motion.Reduced || !Motion.Enabled)
            {
                newPrimary.Opacity = TargetPrimary();
                newSecondary.Opacity = TargetSecondary();
                oldPrimary.Opacity = 0;
                oldSecondary.Opacity = 0;
                frontVisible = !frontVisible;
                return;
            }

            int ms = UiMotion.Duration(UiMotion.NumberRollMs, Motion.Reduced);
            FadeTo(newPrimary, TargetPrimary(), ms);
            FadeTo(newSecondary, TargetSecondary(), ms);
            FadeTo(oldPrimary, 0, ms);
            FadeTo(oldSecondary, 0, ms);
            frontVisible = !frontVisible;
        }

        private static void ApplyBrushes(Ellipse primary, Ellipse secondary)
        {
            primary.Fill = (Brush)Application.Current.FindResource("AmbientPrimaryBrush");
            secondary.Fill = (Brush)Application.Current.FindResource("AmbientSecondaryBrush");
        }

        private static double TargetPrimary()
        {
            return (double)Application.Current.FindResource("AmbientPrimaryOpacity");
        }

        private static double TargetSecondary()
        {
            return (double)Application.Current.FindResource("AmbientSecondaryOpacity");
        }

        private static void FadeTo(Ellipse el, double target, int ms)
        {
            DoubleAnimation anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms));
            anim.EasingFunction = new CubicEase();
            ((CubicEase)anim.EasingFunction).EasingMode = EasingMode.EaseOut;
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
```

注意：`UiMotion.NumberRollMs`（400ms）复用为氛围过渡时长，与规格 §6 一致。`Motion.Reduced`/`Motion.Enabled` 来自 Phase 1 的 `wpf/Motion.cs`。

- [ ] **Step 2: MainWindow 集成**

`wpf/MainWindow.xaml`：在根 Grid 内、标题栏 Border **之前**插入（保持在视觉最底层），并给根 Grid 的 AmbientLayer 跨两行：

```xml
  <Grid>
    <controls:AmbientLayer x:Name="Ambient" Grid.RowSpan="2"/>
    <Grid.RowDefinitions>
```

Grid 顶部需要声明控件命名空间。Window 根元素的 xmlns 区追加：

```xml
        xmlns:controls="clr-namespace:CaelusApp.WpfHost.Controls"
```

注意：Grid 的 RowDefinitions 必须在子元素之前声明——把 `<controls:AmbientLayer .../>` 放在 `</Grid.RowDefinitions>` **之后**、标题栏 Border 之前。

`wpf/MainWindow.xaml.cs`：在 `MainWindow(IOverviewSource source)` 构造函数末尾（`PageHost.Content = ...` 之后）追加：

```csharp
            Loaded += OnLoadedAmbient;
```

并新增方法：

```csharp
        private void OnLoadedAmbient(object sender, RoutedEventArgs e)
        {
            Ambient.Show();
        }
```

- [ ] **Step 3: csproj 注册**

Page 追加：

```xml
    <Page Include="Controls\AmbientLayer.xaml" />
```

Compile 追加：

```xml
    <Compile Include="Controls\AmbientLayer.xaml.cs">
      <DependentUpon>Controls\AmbientLayer.xaml</DependentUpon>
    </Compile>
```

- [ ] **Step 4: 构建 + 冒烟**

`cmd //c build-wpf.cmd`，然后运行 `./wpf/bin/Release/CaelusWpf.exe --wpf-shot "$TEMP/AmbientCheck"`。
预期：构建 OK；截图正常生成且右上角可见淡青色光域（常规模式默认）。

- [ ] **Step 5: Commit**

```bash
git add wpf/
git commit -m "feat: AmbientLayer 环境光控件（两对光域交替交叉淡入）"
```

---

### Task 4: 玻璃样式升级

**Files:**
- Modify: `wpf/Themes/Styles.xaml`

- [ ] **Step 1: 升级样式字典**

`wpf/Themes/Styles.xaml` 整体替换为（变更点：CardBorder 用玻璃画刷 + 投影；NavItem 选中态用 ModeAccent；SegmentHost/SegmentItem 玻璃化；PrimaryButton 用 ModeAccentBrush）：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- 玻璃卡片（规格 §4.1 普通卡片档：白 6% 填充 + 白 10% 边框 + 内高光 + 投影） -->
  <Style x:Key="CardBorder" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource GlassCardBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource GlassBorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusMd}"/>
    <Setter Property="Padding" Value="12"/>
    <Setter Property="Effect">
      <Setter.Value>
        <DropShadowEffect Color="Black" Opacity="0.3" BlurRadius="16" ShadowDepth="4"/>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 结论主卡片档（白 8% 填充 + 白 14% 边框 + 更强投影） -->
  <Style x:Key="HeroCardBorder" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource GlassHeroBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource GlassBorderHiBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusMd}"/>
    <Setter Property="Padding" Value="12"/>
    <Setter Property="Effect">
      <Setter.Value>
        <DropShadowEffect Color="Black" Opacity="0.4" BlurRadius="32" ShadowDepth="8"/>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 主按钮：ModeAccent 填充（规格 §5），不发光 -->
  <Style x:Key="PrimaryButton" TargetType="Button">
    <Setter Property="Background" Value="{DynamicResource ModeAccentBrush}"/>
    <Setter Property="Foreground" Value="#FF0A0F14"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="12,7"/>
    <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="bd" Background="{TemplateBinding Background}"
                  CornerRadius="{DynamicResource RadiusSm}"
                  Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter Property="Opacity" Value="0.92"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter Property="Opacity" Value="0.85"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.4"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 幽灵按钮：ModeAccent 文字 -->
  <Style x:Key="GhostButton" TargetType="Button">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{DynamicResource ModeAccentBrush}"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="6,4"/>
    <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
    <Setter Property="Cursor" Value="Hand"/>
  </Style>

  <!-- 导航项（规格 §5）：选中 = ModeAccent 12% 填充 + ModeAccent 文字 + 1px 内描边，不发光 -->
  <Style x:Key="NavItem" TargetType="RadioButton">
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
    <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
    <Setter Property="Padding" Value="10,8"/>
    <Setter Property="Margin" Value="6,1"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="bd" CornerRadius="{DynamicResource RadiusSm}"
                  Padding="{TemplateBinding Padding}"
                  Background="Transparent"
                  BorderThickness="1" BorderBrush="Transparent">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource ModeAccentSoftBrush}"/>
              <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource ModeAccentEdgeBrush}"/>
              <Setter Property="Foreground" Value="{DynamicResource ModeAccentBrush}"/>
              <Setter Property="FontWeight" Value="Bold"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource GlassNavBrush}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 分段控件项（规格 §5）：容器白 5%，选中项白 8% + 主文字色 -->
  <Style x:Key="SegmentItem" TargetType="RadioButton">
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
    <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
    <Setter Property="Padding" Value="14,6"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="bd" CornerRadius="{DynamicResource RadiusSm}"
                  Padding="{TemplateBinding Padding}"
                  Background="Transparent">
            <ContentPresenter HorizontalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource GlassHeroBrush}"/>
              <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
              <Setter Property="FontWeight" Value="Bold"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 分段控件容器：玻璃导航档 -->
  <Style x:Key="SegmentHost" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource GlassNavBrush}"/>
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}"/>
    <Setter Property="Padding" Value="3"/>
    <Setter Property="BorderBrush" Value="{DynamicResource GlassBorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
  </Style>
</ResourceDictionary>
```

上表引用了两个新别名键 `ModeAccentSoftBrush`（ModeAccent 12% 底）与 `ModeAccentEdgeBrush`（ModeAccent 33% 描边），它们随模式变化，必须放在模式字典里。

- [ ] **Step 2: 模式字典追加软底/描边画刷**

三个 `wpf/Themes/Mode.*.xaml` 各自追加（Standard 示例，Competitive/Custom 用各自 OnDark 色换算；12%≈1F、33%≈54）：

```xml
  <!-- 导航选中软底与内描边：基于 OnDark 档（深色为主；浅色档在浅色验收时如需再细化） -->
  <SolidColorBrush x:Key="ModeAccentSoftBrush" Color="#1F3EC9FF"/>
  <SolidColorBrush x:Key="ModeAccentEdgeBrush" Color="#543EC9FF"/>
```

Competitive：`#1FFF6B74` / `#54FF6B74`。Custom：`#1FA78BFA` / `#54A78BFA`。

注意：浅色主题下 OnDark 色过亮，选中态可读性会受影响——浅色日间模式本次只做巡航浅验证图，若发现导航选中文字在浅色下不清，记录为遗留项（规格 §8 已声明浅色完整打磨不在本阶段范围）。

- [ ] **Step 3: 构建 + 截图目检**

```bash
cmd //c build-wpf.cmd
./wpf/bin/Release/CaelusWpf.exe --wpf-shot "$TEMP/GlassCheck"
```

预期：构建 OK；截图中卡片呈半透明玻璃质感、导航选中有青色软底。

- [ ] **Step 4: Commit**

```bash
git add wpf/
git commit -m "feat: 玻璃样式升级（面板 alpha 分层 + 内高光 + 投影，ModeAccent 导航选中）"
```

---

### Task 5: 概览页语义发光与玻璃化

**Files:**
- Modify: `wpf/Converters.cs`（追加 KeyColorConverter）
- Modify: `wpf/Views/OverviewView.xaml`
- Modify: `wpf/Caelus.Wpf.csproj`（如 Converters 已注册则无需动）

- [ ] **Step 1: KeyColorConverter**

`wpf/Converters.cs` 追加（语义键 → Color，供 DropShadowEffect.Color 绑定）：

```csharp
    // 语义键 → 当前主题的画刷颜色（供发光 Effect 的 Color 绑定）
    internal sealed class KeyColorConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            string key = (value as string) ?? "Info";
            SolidColorBrush brush = Application.Current.TryFindResource(key + "Brush") as SolidColorBrush;
            return brush == null ? Colors.Gray : brush.Color;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }
```

- [ ] **Step 2: OverviewView 玻璃化与语义发光**

`wpf/Views/OverviewView.xaml` 修改点（布局结构不动，只换材质）：

1. UserControl.Resources 追加：

```xml
    <local:KeyColorConverter x:Key="KeyColor"/>
```

2. 结论卡片：`Style="{DynamicResource CardBorder}"` 改为 `Style="{DynamicResource HeroCardBorder}"`。

3. 结论图标圆形底 Border 加发光 Effect（规格 §4.1：状态色发光 BlurRadius 18 / Opacity 0.35）：

```xml
        <Border Width="32" Height="32" CornerRadius="16" VerticalAlignment="Center"
                Background="{Binding ConclusionColorKey, Converter={StaticResource KeySoftBrush}}">
          <Border.Effect>
            <DropShadowEffect BlurRadius="18" ShadowDepth="0" Opacity="0.35"
                              Color="{Binding ConclusionColorKey, Converter={StaticResource KeyColor}}"/>
          </Border.Effect>
          <TextBlock Text="{Binding ConclusionGlyph}" HorizontalAlignment="Center"
                     VerticalAlignment="Center" FontSize="15"
                     Foreground="{Binding ConclusionColorKey, Converter={StaticResource KeyBrush}}"/>
        </Border>
```

4. 指标进度条：轨道 `BorderSubtleBrush` 改为 `TrackBrush`；填充 Border 加发光（规格 §4.1：BlurRadius 8 / Opacity 0.45）：

```xml
              <Grid Height="4" Margin="0,9,0,0">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="{Binding Fraction, Converter={StaticResource FracLen}}"/>
                  <ColumnDefinition Width="{Binding Fraction, Converter={StaticResource FracLen}, ConverterParameter=rest}"/>
                </Grid.ColumnDefinitions>
                <Border Grid.ColumnSpan="2" CornerRadius="2"
                        Background="{DynamicResource TrackBrush}"/>
                <Border Grid.Column="0" CornerRadius="2" HorizontalAlignment="Stretch"
                        Background="{Binding ColorKey, Converter={StaticResource KeyBrush}}">
                  <Border.Effect>
                    <DropShadowEffect BlurRadius="8" ShadowDepth="0" Opacity="0.45"
                                      Color="{Binding ColorKey, Converter={StaticResource KeyColor}}"/>
                  </Border.Effect>
                </Border>
              </Grid>
```

注意：进度条列宽保持 Task 10 修复后的互补 star 写法（`FracLen` + `ConverterParameter=rest`），不要回退。

- [ ] **Step 3: 构建 + 截图目检**

```bash
cmd //c build-wpf.cmd
./wpf/bin/Release/CaelusWpf.exe --wpf-shot "$TEMP/GlowCheck"
```

预期：构建 OK；结论图标有柔和状态色光晕、进度条填充有发光、战备语义不被模式色污染（当前仅常规模式，绿灯/绿条）。

- [ ] **Step 4: 自测回归**

`cmd //c "dev.cmd test"`
预期：`TOTAL 169  PASS 166  FAIL 0  SKIP 3`。

- [ ] **Step 5: Commit**

```bash
git add wpf/
git commit -m "feat: 概览页语义发光（状态图标+进度条）与玻璃主卡片"
```

---

### Task 6: 模式切换接线（ModeController）

**Files:**
- Create: `wpf/ModeController.cs`
- Modify: `src/UiShared/OverviewViewModel.cs`（SampleOverviewSource 增加 SetMode）
- Modify: `wpf/MainWindow.xaml`（分段控件加 x:Name + Checked 事件）
- Modify: `wpf/MainWindow.xaml.cs`（ModeChecked 处理器 + 构造时应用持久化模式）
- Modify: `wpf/App.xaml.cs`（启动读持久化模式）
- Modify: `wpf/Caelus.Wpf.csproj`

- [ ] **Step 1: SampleOverviewSource 支持模式切换**

`src/UiShared/OverviewViewModel.cs` 中 `SampleOverviewSource` 替换为：

```csharp
    // 示例数据源：供 --wpf-shot 截图与手动预览使用
    internal sealed class SampleOverviewSource : IOverviewSource
    {
        private string modeText = "常规";

        public bool GuardEnabled { get { return true; } }
        public bool GameActive { get { return false; } }
        public bool HasWarning { get { return false; } }
        public bool HasCritical { get { return false; } }
        public double? GpuTempC { get { return 62; } }
        public double? MemoryUsedPct { get { return 53; } }
        public string MemoryUsedText { get { return "8.4 GB"; } }
        public string ModeText { get { return modeText; } }
        public string LastCheckText { get { return "上次检查 2 分钟前 · 没有需要处理的问题"; } }

        // 预览宿主模式切换时更新显示文案（竞技/自定义下示例结论同步变化）
        public void SetMode(AppMode mode)
        {
            modeText = ModePalette.DisplayName(mode);
        }
    }
```

- [ ] **Step 2: ModeController**

新建 `wpf/ModeController.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 驾驶舱模式切换编排：持久化 + 主题换槽 + 氛围过渡 + 视图刷新

using System.Windows;
using CaelusApp.WpfHost.Controls;

namespace CaelusApp.WpfHost
{
    internal static class ModeController
    {
        // 启动时读取持久化模式（与 GameMode.Preset 使用同一 Settings 键；
        // 预览宿主不运行 GameMode，正式宿主接管时改用 gameMode.Preset 赋值）
        public static AppMode LoadPersisted()
        {
            int raw;
            if (int.TryParse(Settings.LoadStr("PerformancePreset", "0"), out raw)
                && raw >= 0 && raw <= 2)
                return ModePalette.FromPreset((PerformancePreset)raw);
            return AppMode.Standard;
        }

        public static void SwitchTo(Application app, AppMode mode, AmbientLayer ambient,
            SampleOverviewSource source, OverviewViewModel vm, bool animate)
        {
            Settings.SaveStr("PerformancePreset", ((int)ToPreset(mode)).ToString());
            ThemeManager.Apply(app, ThemeManager.CurrentTone, mode);
            if (ambient != null) ambient.TransitionTo(animate);
            if (source != null) source.SetMode(mode);
            if (vm != null) vm.Refresh();
        }

        private static PerformancePreset ToPreset(AppMode mode)
        {
            if (mode == AppMode.Competitive) return PerformancePreset.Competitive;
            if (mode == AppMode.Custom) return PerformancePreset.Custom;
            return PerformancePreset.Standard;
        }
    }
}
```

注意：`Settings`/`PerformancePreset` 来自链接编译的 `src/Platform/Settings.cs` 与 `src/Core/Detection/GameProfiles.cs`，同在 `CaelusApp` 命名空间，直接可用。

- [ ] **Step 3: MainWindow 分段控件接线**

`wpf/MainWindow.xaml`：三个模式 RadioButton 加 x:Name 和 Checked 事件：

```xml
              <RadioButton x:Name="SegStandard" Style="{DynamicResource SegmentItem}" Content="常规"
                           IsChecked="True" GroupName="mode" Checked="ModeChecked"/>
              <RadioButton x:Name="SegCompetitive" Style="{DynamicResource SegmentItem}" Content="竞技" GroupName="mode" Checked="ModeChecked"/>
              <RadioButton x:Name="SegCustom" Style="{DynamicResource SegmentItem}" Content="自定义" GroupName="mode" Checked="ModeChecked"/>
```

`wpf/MainWindow.xaml.cs` 替换为：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主主窗口外壳：标题栏 / NavRail / 内容宿主 / 模式切换

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaelusApp.WpfHost.Views;

namespace CaelusApp.WpfHost
{
    public partial class MainWindow : Window
    {
        private readonly SampleOverviewSource source;
        private readonly OverviewViewModel vm;

        public MainWindow() : this(null) { }

        public MainWindow(IOverviewSource overviewSource)
        {
            InitializeComponent();
            source = overviewSource as SampleOverviewSource ?? new SampleOverviewSource();
            vm = new OverviewViewModel(source);
            vm.Refresh();
            DataContext = vm;
            PageHost.Content = new OverviewView { DataContext = vm };
            Loaded += OnLoadedAmbient;
        }

        // 启动时应用持久化模式的主题与氛围（无动画）
        public void ApplyPersistedMode(AppMode mode)
        {
            if (mode == AppMode.Competitive) SegCompetitive.IsChecked = true;
            else if (mode == AppMode.Custom) SegCustom.IsChecked = true;
            else SegStandard.IsChecked = true;
            source.SetMode(mode);
            vm.Refresh();
        }

        private void OnLoadedAmbient(object sender, RoutedEventArgs e)
        {
            Ambient.Show();
        }

        private void ModeChecked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            AppMode mode = sender == SegCompetitive ? AppMode.Competitive
                : sender == SegCustom ? AppMode.Custom : AppMode.Standard;
            ModeController.SwitchTo(Application.Current, mode, Ambient, source, vm, true);
        }

        private void TitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void MinClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void NavChecked(object sender, RoutedEventArgs e)
        {
            RadioButton rb = sender as RadioButton;
            if (rb == null || PageHost == null) return;
            PageHost.Content = rb == NavOverview
                ? (object)new OverviewView { DataContext = DataContext }
                : new PlaceholderView();
        }
    }
}
```

- [ ] **Step 4: App.xaml.cs 启动读模式 + 默认深色**

`wpf/App.xaml.cs` 正常启动分支改为（规格 §2 决策 3：深色为默认主题）：

```csharp
            AppMode initial = ModeController.LoadPersisted();
            ThemeManager.Apply(this, UiTone.Dark, initial);
            MainWindow w = new MainWindow();
            w.ApplyPersistedMode(initial);
            w.Show();
```

RunShot 中的 `ThemeManager.Apply(this, tone)` 改为 `ThemeManager.Apply(this, tone, AppMode.Standard)`（Task 7 扩展矩阵）。

- [ ] **Step 5: csproj 注册**

```xml
    <Compile Include="ModeController.cs" />
```

- [ ] **Step 6: 构建 + 实机验证**

`cmd //c build-wpf.cmd`，然后正常运行 `./wpf/bin/Release/CaelusWpf.exe`：
- 默认深色启动，巡航青氛围
- 点击「竞技」：氛围 400ms 过渡为战备红，导航选中态变红，模式文字变「竞技」
- 点击「自定义」：过渡为工程紫
- 关闭重开：保持上次模式
- 系统「减少动画」开启时：切换瞬时完成

- [ ] **Step 7: 自测回归**

`cmd //c "dev.cmd test"`
预期：`TOTAL 169  PASS 166  FAIL 0  SKIP 3`。

- [ ] **Step 8: Commit**

```bash
git add wpf/ src/UiShared/OverviewViewModel.cs
git commit -m "feat: 模式分段控件真实切换——氛围过渡 + Settings 持久化 + VM 刷新"
```

---

### Task 7: 截图矩阵与验收文档

**Files:**
- Modify: `wpf/App.xaml.cs`（RunShot 输出 4 张矩阵）
- Create: `docs/wpf-phase1_5/`（截图存档）
- Create: `docs/wpf-phase1_5-verification.md`

- [ ] **Step 1: RunShot 矩阵化**

`wpf/App.xaml.cs` 的 `RunShot` 方法替换为：

```csharp
        // 离屏渲染模式×主题矩阵 PNG，供视觉验收与回归基线（规格 §7.5）
        private int RunShot(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                UiTone[] tones = new UiTone[] { UiTone.Dark, UiTone.Dark, UiTone.Dark, UiTone.Light };
                AppMode[] modes = new AppMode[] { AppMode.Standard, AppMode.Competitive, AppMode.Custom, AppMode.Standard };
                string[] names = new string[] { "dark-cruise", "dark-combat", "dark-custom", "light-cruise" };
                for (int i = 0; i < tones.Length; i++)
                {
                    ThemeManager.Apply(this, tones[i], modes[i]);
                    MainWindow w = new MainWindow(new SampleOverviewSource());
                    w.ApplyPersistedMode(modes[i]);
                    w.WindowStartupLocation = WindowStartupLocation.Manual;
                    w.Left = -20000;
                    w.Top = -20000;
                    w.ShowInTaskbar = false;
                    w.ShowActivated = false;
                    w.Show();
                    w.UpdateLayout();
                    Size size = new Size(1196, 768);
                    w.Measure(size);
                    w.Arrange(new Rect(size));
                    w.UpdateLayout();
                    RenderTargetBitmap rtb = new RenderTargetBitmap(1196, 768, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(w);
                    PngBitmapEncoder enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    string file = Path.Combine(dir, "wpf-overview-" + names[i] + ".png");
                    using (FileStream fs = File.Create(file)) enc.Save(fs);
                    w.Close();
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(dir, "wpf-shot.error.txt"), ex.ToString()); } catch { }
                return 1;
            }
        }
```

注意：RunShot 顶部已有的 `Motion.Enabled = false`（Task 11 的门控）必须保留，它在 `OnStartup` 的 `--wpf-shot` 分支里设置，不在本方法内——确认勿删。

- [ ] **Step 2: 构建 + 生成矩阵**

```bash
cmd //c build-wpf.cmd
./wpf/bin/Release/CaelusWpf.exe --wpf-shot docs/wpf-phase1_5
```

预期：退出码 0，生成 4 张 PNG，无 `wpf-shot.error.txt`。

- [ ] **Step 3: 视觉验收**

用 Read 工具逐张查看 `docs/wpf-phase1_5/` 四张 PNG，对照规格 §4/§5：
- `dark-cruise`：青蓝环境光域、玻璃面板、绿灯绿条、导航选中青色软底
- `dark-combat`：红色环境光域、导航选中红色软底，**进度条与结论图标保持绿色语义**（规格 §4.2 零例外）
- `dark-custom`：紫色环境光域
- `light-cruise`：浅色磨砂面板 + 低饱和青蓝光域，文字清晰可读

不符则修正后重新生成，直至符合。

- [ ] **Step 4: 端到端回归**

```bash
cmd //c "build.cmd"
cmd //c build-wpf.cmd
cmd //c "dev.cmd test"
```

预期：两个 Build OK；`TOTAL 169  PASS 166  FAIL 0  SKIP 3`。

- [ ] **Step 5: 写验收记录并提交**

创建 `docs/wpf-phase1_5-verification.md`，记录：构建结果、自测 TOTAL、4 张截图核对结论（逐项对照规格 §4.1 三层材质、§4.2 色彩宪法、§4.3 模式色表）、模式切换实机验证结论、遗留项（浅色日间模式完整打磨、真毛玻璃、其余页面迁移）。

```bash
git add docs/wpf-phase1_5/ docs/wpf-phase1_5-verification.md
git commit -m "docs: Phase 1.5 验收记录与模式×主题截图矩阵"
```

---

## 自检记录

**规格覆盖：** §4.1 三层材质→Task 2（玻璃画刷/环境透明度）+ Task 3（AmbientLayer）+ Task 4/5（面板与发光配方）；§4.2 色彩宪法→Task 4（ModeAccent 导航）+ Task 5（状态色发光不随模式变）+ Task 7 验收核对；§4.3 模式色表→Task 1；§5 组件材质→Task 4/5；§6 动效（400ms 交叉淡入 + 性能纪律）→Task 3/6；§7.1 双轴四槽→Task 2；§7.2 两对 Ellipse 交替→Task 3；§7.3 ModePalette+3 自测→Task 1；§7.4 模式切换接线→Task 6；§7.5 截图矩阵→Task 7；§8 范围 9 项→Task 1-7 全覆盖；§9 验收（TOTAL 169/166/0/3）→Task 7 Step 4。§7.6 DWM acrylic 为 stretch goal，本计划不含。

**类型一致性：** `AppMode`/`ModeColors`/`ModePalette.For/DisplayName/FromPreset`（Task 1）→ Task 2 ThemeManager、Task 6 ModeController 使用；`AmbientLayer.Show/TransitionTo`（Task 3）→ Task 6 MainWindow/ModeController 使用；`ThemeManager.Apply(Application, UiTone, AppMode)`/`CurrentTone`（Task 2）→ Task 6/7 使用；`SampleOverviewSource.SetMode`（Task 6 Step 1）→ Task 6 ModeController/MainWindow 使用；`ModeAccentSoftBrush`/`ModeAccentEdgeBrush`（Task 4 Step 2 模式字典）→ Task 4 Step 1 NavItem 样式引用；`GlassHeroBrush`/`TrackBrush`/`KeyColorConverter`（Task 2/5）→ Task 5 OverviewView 引用。

**已知取舍：** 预览宿主用 `Settings.SaveStr` 持久化模式（等价于 GameMode.Preset 的持久化部分）；浅色主题仅巡航浅一张验证图；导航选中软底画刷基于 OnDark 档，浅色下可读性列为遗留核对项；DWM acrylic 不在本阶段。
