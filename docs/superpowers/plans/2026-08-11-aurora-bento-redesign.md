# Aurora Bento 视觉重构实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Caelus WPF 界面整体重构为 Aurora Bento 风格（深底极光光晕 + 玻璃卡片 2.0 + 超大细字重排印 + 克制微动效 + 三模式品牌配色），配色系统契约化/可插拔。

**Architecture:** 演进式——沿用现有 ResourceDictionary 双轴分层（明暗 tone × 模式 mode），新增主题契约 v2 语义 key；视觉资产（图标/动效/CaelusCore/Sparkline/GlassCard）各自独立文件；概览页作为示例案例先行，其余页面靠样式字典全局换肤 + 逐页精修。

**Tech Stack:** WPF / .NET Framework 4.0（无 targeting pack，inbox MSBuild + HintPaths）；构建 `cmd.exe //c build-wpf.cmd` → `wpf\bin\Release\CaelusWpf.exe`；自测 `cmd.exe //c "dev.cmd test"`，当前基线 **TOTAL 175 / PASS 172 / FAIL 0 / SKIP 3**（3 个 SKIP 是环境所致，非回归）。

**规格文档:** `docs/superpowers/specs/2026-08-11-aurora-bento-redesign-design.md`（§章节号下文引用）

**关键环境约束（必须遵守）：**
- `DropShadowEffect` 在本环境完全不渲染——**禁止使用**，层次感一律用边框/填充表达
- Style Setter 中的 `Effect` 在 net4 不渲染（Freezable 冻结共享）——同样禁止
- net4 无 backdrop-blur、无字距 API；动画只用 `RenderTransform`/`Opacity`（渲染线程，开销极低）
- 新 `.cs` 文件放入 `wpf/` 或 `src/UiShared/` 后：UiShared 由 csproj glob 自动包含（两个 exe 共享）；`wpf/` 下的新文件**必须登记进 `wpf/Caelus.Wpf.csproj`**（Page 或 Compile 显式列表）
- Git Bash 中调用 .cmd 必须 `cmd.exe //c "..."`（双斜杠）；重建前若 CaelusWpf.exe 在运行需先关闭（文件锁）

---

## M1 · 主题契约 v2 + 三模式预设 + 环境光层

### Task 1: ThemeContract 校验器 + 契约自测（TDD，先失败）

**Files:**
- Create: `src/UiShared/ThemeContract.cs`
- Create: `tests/SelfTests.ThemeContract.cs`
- Modify: `tests/SelfTests.cs`（Run() 注册 3 个用例，约在 855 行文案键用例之后）

- [ ] **Step 1: 编写校验器**

`src/UiShared/ThemeContract.cs`（UiShared 由 csproj glob `..\src\UiShared\*.cs` 自动进入两个 exe，无需登记）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 Aurora Bento 主题契约 v2：主题字典必须实现的 key 集合与文本级校验（规格 §3.1）

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CaelusApp
{
    internal static class ThemeContract
    {
        // 色板档（明暗轴）：Colors.Dark.xaml / Colors.Light.xaml 必须全部实现
        public static readonly string[] ToneKeys = new[]
        {
            "BackgroundColor", "BackgroundBrush",
            "Surface0Color", "Surface0Brush", "Surface1Color", "Surface1Brush", "Surface2Color", "Surface2Brush",
            "BorderSubtleBrush", "BorderStrongBrush", "TopHighlightBrush", "CardEdgeBrush",
            "SegSelectedBrush", "SegSelectedTextBrush",
            "TextPrimaryColor", "TextPrimaryBrush", "TextSecondaryColor", "TextSecondaryBrush",
            "TextTertiaryColor", "TextTertiaryBrush",
            "SuccessColor", "SuccessBrush", "WarningColor", "WarningBrush",
            "DangerColor", "DangerBrush", "InfoColor", "InfoBrush",
            "BrandColor", "BrandBrush",
        };

        // 模式档（模式轴）：Mode.Standard/Competitive/Custom.xaml 与用户主题 Caelus.theme.xaml 必须全部实现
        public static readonly string[] ModeKeys = new[]
        {
            "AuroraPrimaryColor", "AuroraPrimaryFadeColor",
            "AuroraSecondaryColor", "AuroraSecondaryFadeColor",
            "AuroraTertiaryColor", "AuroraTertiaryFadeColor",
            "AmbientPrimaryBrush", "AmbientSecondaryBrush", "AmbientTertiaryBrush",
            "AuroraPrimaryOpacity", "AuroraSecondaryOpacity", "AuroraTertiaryOpacity",
            "AuroraDriftSeconds",
            "AccentPrimaryColor", "AccentSecondaryColor",
            "AccentPrimaryBrush", "AccentSecondaryBrush", "AccentGradientBrush",
            "AccentSoftBrush", "AccentEdgeBrush", "AccentGlowColor", "OnAccentBrush",
        };

        private static readonly Regex KeyAttr =
            new Regex("x:Key\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);

        // 从 XAML 文本提取全部资源 key（与 LangKeys 自测同款文本扫描思路）
        public static HashSet<string> ExtractKeys(string xamlText)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (xamlText == null) return keys;
            foreach (Match m in KeyAttr.Matches(xamlText))
                keys.Add(m.Groups[1].Value);
            return keys;
        }

        // 返回缺失的契约 key；空数组 = 通过
        public static string[] MissingKeys(string xamlText, string[] contract)
        {
            HashSet<string> have = ExtractKeys(xamlText);
            var missing = new List<string>();
            foreach (string key in contract)
                if (!have.Contains(key)) missing.Add(key);
            return missing.ToArray();
        }
    }
}
```

- [ ] **Step 2: 编写失败自测**

`tests/SelfTests.ThemeContract.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 Aurora Bento 主题契约自测：色板档/模式档字典 key 完整性 + 校验器正反样例

using System;
using System.IO;
using System.Text;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestThemeContractToneFiles()
        {
            string src = LocateSourceRoot();
            if (src == null) throw new TestSkippedException("找不到源码目录，发布构建下跳过");
            CheckThemeFile(Path.Combine(src, "wpf", "Themes", "Colors.Dark.xaml"), ThemeContract.ToneKeys);
            CheckThemeFile(Path.Combine(src, "wpf", "Themes", "Colors.Light.xaml"), ThemeContract.ToneKeys);
        }

        private static void TestThemeContractModeFiles()
        {
            string src = LocateSourceRoot();
            if (src == null) throw new TestSkippedException("找不到源码目录，发布构建下跳过");
            CheckThemeFile(Path.Combine(src, "wpf", "Themes", "Mode.Standard.xaml"), ThemeContract.ModeKeys);
            CheckThemeFile(Path.Combine(src, "wpf", "Themes", "Mode.Competitive.xaml"), ThemeContract.ModeKeys);
            CheckThemeFile(Path.Combine(src, "wpf", "Themes", "Mode.Custom.xaml"), ThemeContract.ModeKeys);
        }

        private static void TestThemeContractValidator()
        {
            // 正样例：按契约拼一份完整字典，不应报缺
            var sb = new StringBuilder("<ResourceDictionary>");
            foreach (string k in ThemeContract.ModeKeys)
                sb.Append("<Color x:Key=\"" + k + "\"/>");
            string complete = sb.ToString();
            if (ThemeContract.MissingKeys(complete, ThemeContract.ModeKeys).Length != 0)
                throw new Exception("完整字典被误报缺 key");
            // 反样例：抽掉 AccentGlowColor，必须恰好检出它
            string broken = complete.Replace("<Color x:Key=\"AccentGlowColor\"/>", "");
            string[] missing = ThemeContract.MissingKeys(broken, ThemeContract.ModeKeys);
            if (missing.Length != 1 || missing[0] != "AccentGlowColor")
                throw new Exception("缺 key 未被正确检出：" + string.Join(",", missing));
        }

        private static void CheckThemeFile(string path, string[] contract)
        {
            if (!File.Exists(path)) throw new Exception("主题字典不存在：" + path);
            string[] missing = ThemeContract.MissingKeys(File.ReadAllText(path), contract);
            if (missing.Length > 0)
                throw new Exception(Path.GetFileName(path) + " 缺少契约 key：" + string.Join("、", missing));
        }
    }
}
```

在 `tests/SelfTests.cs` 的 `Run` 方法中找到 `test("文案：源码里引用到的键全部有定义", TestEveryLangKeyIsDefined);`（约 855 行），在其后追加三行：

```csharp
            test("主题契约：色板档字典 key 完整", TestThemeContractToneFiles);
            test("主题契约：模式档字典 key 完整", TestThemeContractModeFiles);
            test("主题契约：校验器正反样例", TestThemeContractValidator);
```

- [ ] **Step 3: 跑自测确认前两个用例失败（第三个应通过）**

Run: `cmd.exe //c "dev.cmd test"`
Expected: 输出含 `FAIL ... 主题契约：色板档字典 key 完整`（缺 Surface0Color 等新 key）与 `FAIL ... 主题契约：模式档字典 key 完整`（缺 Aurora* 等新 key）；`主题契约：校验器正反样例` PASS。

- [ ] **Step 4: Commit**

```bash
git add src/UiShared/ThemeContract.cs tests/SelfTests.ThemeContract.cs tests/SelfTests.cs
git commit -m "test: 主题契约 v2 校验器与自测（先失败，驱动色板/模式档重写）"
```

---

### Task 2: Colors.Dark / Colors.Light 重写为契约 v2

**Files:**
- Modify: `wpf/Themes/Colors.Dark.xaml`（整体替换）
- Modify: `wpf/Themes/Colors.Light.xaml`（整体替换）

**设计要点（规格 §3.3）**：新语义 key + 兼容别名层（旧视图继续编译渲染，逐页精修后回收别名）。浅色本期不重构，但补齐契约 key 保持可用。

- [ ] **Step 1: 整体替换 `wpf/Themes/Colors.Dark.xaml`**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ===== 语义色（沿用） ===== -->
  <Color x:Key="SuccessColor">#4ADE80</Color>
  <Color x:Key="WarningColor">#FBBF24</Color>
  <Color x:Key="DangerColor">#F87171</Color>
  <Color x:Key="InfoColor">#60A5FA</Color>
  <Color x:Key="BrandColor">#D4A847</Color>

  <!-- ===== 表面梯度（规格 §3.3：越高越亮；Surface0 近实色半透明叠光晕等效 backdrop-blur） ===== -->
  <Color x:Key="BackgroundColor">#07080D</Color>
  <Color x:Key="Surface0Color">#B40F121B</Color>
  <Color x:Key="Surface1Color">#CC161A26</Color>
  <Color x:Key="Surface2Color">#D91D2230</Color>

  <!-- ===== 文字（正文对背景对比度 ≥4.5:1） ===== -->
  <Color x:Key="TextPrimaryColor">#F6F8FC</Color>
  <Color x:Key="TextSecondaryColor">#99A0AE</Color>
  <Color x:Key="TextTertiaryColor">#6B7280</Color>

  <!-- ===== 画刷 ===== -->
  <SolidColorBrush x:Key="SuccessBrush" Color="{StaticResource SuccessColor}"/>
  <SolidColorBrush x:Key="WarningBrush" Color="{StaticResource WarningColor}"/>
  <SolidColorBrush x:Key="DangerBrush" Color="{StaticResource DangerColor}"/>
  <SolidColorBrush x:Key="InfoBrush" Color="{StaticResource InfoColor}"/>
  <SolidColorBrush x:Key="BrandBrush" Color="{StaticResource BrandColor}"/>
  <SolidColorBrush x:Key="BackgroundBrush" Color="{StaticResource BackgroundColor}"/>
  <SolidColorBrush x:Key="Surface0Brush" Color="{StaticResource Surface0Color}"/>
  <SolidColorBrush x:Key="Surface1Brush" Color="{StaticResource Surface1Color}"/>
  <SolidColorBrush x:Key="Surface2Brush" Color="{StaticResource Surface2Color}"/>
  <SolidColorBrush x:Key="TextPrimaryBrush" Color="{StaticResource TextPrimaryColor}"/>
  <SolidColorBrush x:Key="TextSecondaryBrush" Color="{StaticResource TextSecondaryColor}"/>
  <SolidColorBrush x:Key="TextTertiaryBrush" Color="{StaticResource TextTertiaryColor}"/>

  <!-- ===== 描边体系（玻璃 2.0：渐变描边上亮下隐 + 卡顶高光线） ===== -->
  <SolidColorBrush x:Key="BorderSubtleBrush" Color="#14FFFFFF"/>
  <SolidColorBrush x:Key="BorderStrongBrush" Color="#2EFFFFFF"/>
  <LinearGradientBrush x:Key="TopHighlightBrush" StartPoint="0,0" EndPoint="1,0">
    <GradientStop Color="#00FFFFFF" Offset="0"/>
    <GradientStop Color="#59FFFFFF" Offset="0.5"/>
    <GradientStop Color="#00FFFFFF" Offset="1"/>
  </LinearGradientBrush>
  <LinearGradientBrush x:Key="CardEdgeBrush" StartPoint="0.5,0" EndPoint="0.5,1">
    <GradientStop Color="#38FFFFFF" Offset="0"/>
    <GradientStop Color="#0DFFFFFF" Offset="0.45"/>
    <GradientStop Color="#05FFFFFF" Offset="1"/>
  </LinearGradientBrush>

  <!-- ===== 分段控件选中态（深色旗舰：浅底深字，规格 §6） ===== -->
  <SolidColorBrush x:Key="SegSelectedBrush" Color="#F2F5FA"/>
  <SolidColorBrush x:Key="SegSelectedTextBrush" Color="#0B0E16"/>

  <!-- ===== 兼容别名层（旧视图/样式引用，逐页精修后回收；勿在新代码中使用） ===== -->
  <SolidColorBrush x:Key="SurfaceBrush" Color="#161C22"/>
  <SolidColorBrush x:Key="SurfaceRaisedBrush" Color="#1A2028"/>
  <SolidColorBrush x:Key="BorderBrush" Color="#26313B"/>
  <SolidColorBrush x:Key="GlassNavBrush" Color="#0FFFFFFF"/>
  <SolidColorBrush x:Key="GlassCardBrush" Color="#14FFFFFF"/>
  <SolidColorBrush x:Key="GlassHeroBrush" Color="#1AFFFFFF"/>
  <SolidColorBrush x:Key="GlassRaisedBrush" Color="#1FFFFFFF"/>
  <SolidColorBrush x:Key="GlassBorderBrush" Color="#24FFFFFF"/>
  <SolidColorBrush x:Key="GlassBorderHiBrush" Color="#2EFFFFFF"/>
  <SolidColorBrush x:Key="InnerHighlightBrush" Color="#29FFFFFF"/>
  <SolidColorBrush x:Key="TrackBrush" Color="#1AFFFFFF"/>
  <SolidColorBrush x:Key="ModeAccentBrush" Color="{DynamicResource ModeAccentOnDarkColor}"/>
</ResourceDictionary>
```

注意：旧文件中的 `AmbientPrimaryOpacity`/`AmbientSecondaryOpacity` 已迁移到模式档（改名 `Aurora*Opacity`），此处删除是有意的——AmbientLayer v2（Task 4）改读新 key，旧引用随之清除。

- [ ] **Step 2: 整体替换 `wpf/Themes/Colors.Light.xaml`**

浅色本期不精修，但补齐契约 key（沿用日间磨砂配方的观感）：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ===== 语义色（沿用浅色版） ===== -->
  <Color x:Key="SuccessColor">#2F9E5F</Color>
  <Color x:Key="WarningColor">#D97706</Color>
  <Color x:Key="DangerColor">#DC2626</Color>
  <Color x:Key="InfoColor">#2563EB</Color>
  <Color x:Key="BrandColor">#D4A847</Color>

  <!-- ===== 表面梯度（浅色：白色 alpha 递进） ===== -->
  <Color x:Key="BackgroundColor">#F5F7F9</Color>
  <Color x:Key="Surface0Color">#A6FFFFFF</Color>
  <Color x:Key="Surface1Color">#BFFFFFFF</Color>
  <Color x:Key="Surface2Color">#F2FFFFFF</Color>

  <!-- ===== 文字 ===== -->
  <Color x:Key="TextPrimaryColor">#141F29</Color>
  <Color x:Key="TextSecondaryColor">#61727E</Color>
  <Color x:Key="TextTertiaryColor">#848F96</Color>

  <!-- ===== 画刷 ===== -->
  <SolidColorBrush x:Key="SuccessBrush" Color="{StaticResource SuccessColor}"/>
  <SolidColorBrush x:Key="WarningBrush" Color="{StaticResource WarningColor}"/>
  <SolidColorBrush x:Key="DangerBrush" Color="{StaticResource DangerColor}"/>
  <SolidColorBrush x:Key="InfoBrush" Color="{StaticResource InfoColor}"/>
  <SolidColorBrush x:Key="BrandBrush" Color="{StaticResource BrandColor}"/>
  <SolidColorBrush x:Key="BackgroundBrush" Color="{StaticResource BackgroundColor}"/>
  <SolidColorBrush x:Key="Surface0Brush" Color="{StaticResource Surface0Color}"/>
  <SolidColorBrush x:Key="Surface1Brush" Color="{StaticResource Surface1Color}"/>
  <SolidColorBrush x:Key="Surface2Brush" Color="{StaticResource Surface2Color}"/>
  <SolidColorBrush x:Key="TextPrimaryBrush" Color="{StaticResource TextPrimaryColor}"/>
  <SolidColorBrush x:Key="TextSecondaryBrush" Color="{StaticResource TextSecondaryColor}"/>
  <SolidColorBrush x:Key="TextTertiaryBrush" Color="{StaticResource TextTertiaryColor}"/>

  <!-- ===== 描边体系（浅色用黑色 alpha） ===== -->
  <SolidColorBrush x:Key="BorderSubtleBrush" Color="#0F000000"/>
  <SolidColorBrush x:Key="BorderStrongBrush" Color="#1A000000"/>
  <LinearGradientBrush x:Key="TopHighlightBrush" StartPoint="0,0" EndPoint="1,0">
    <GradientStop Color="#00FFFFFF" Offset="0"/>
    <GradientStop Color="#99FFFFFF" Offset="0.5"/>
    <GradientStop Color="#00FFFFFF" Offset="1"/>
  </LinearGradientBrush>
  <LinearGradientBrush x:Key="CardEdgeBrush" StartPoint="0.5,0" EndPoint="0.5,1">
    <GradientStop Color="#1F000000" Offset="0"/>
    <GradientStop Color="#0A000000" Offset="0.45"/>
    <GradientStop Color="#05000000" Offset="1"/>
  </LinearGradientBrush>

  <!-- ===== 分段控件选中态 ===== -->
  <SolidColorBrush x:Key="SegSelectedBrush" Color="#FFFFFF"/>
  <SolidColorBrush x:Key="SegSelectedTextBrush" Color="#141F29"/>

  <!-- ===== 兼容别名层 ===== -->
  <SolidColorBrush x:Key="SurfaceBrush" Color="#FFFFFF"/>
  <SolidColorBrush x:Key="SurfaceRaisedBrush" Color="#FAFBFC"/>
  <SolidColorBrush x:Key="BorderBrush" Color="#D8E0E6"/>
  <SolidColorBrush x:Key="GlassNavBrush" Color="#8CFFFFFF"/>
  <SolidColorBrush x:Key="GlassCardBrush" Color="#A6FFFFFF"/>
  <SolidColorBrush x:Key="GlassHeroBrush" Color="#BFFFFFFF"/>
  <SolidColorBrush x:Key="GlassRaisedBrush" Color="#BFFFFFFF"/>
  <SolidColorBrush x:Key="GlassBorderBrush" Color="#0F000000"/>
  <SolidColorBrush x:Key="GlassBorderHiBrush" Color="#1A000000"/>
  <SolidColorBrush x:Key="InnerHighlightBrush" Color="#99FFFFFF"/>
  <SolidColorBrush x:Key="TrackBrush" Color="#14000000"/>
  <SolidColorBrush x:Key="ModeAccentBrush" Color="{DynamicResource ModeAccentOnLightColor}"/>
</ResourceDictionary>
```

注意：旧文件中的 `BorderSubtleColor`/`BorderSubtleBrush`(#E8EDF1) 被契约版 `#0F000000` 取代——有意为之（v2 语义统一）；`Ambient*Opacity` 同样迁往模式档。

- [ ] **Step 3: Commit**

```bash
git add wpf/Themes/Colors.Dark.xaml wpf/Themes/Colors.Light.xaml
git commit -m "feat: 色板档契约 v2——表面梯度/渐变描边/高光线 + 兼容别名层"
```

---

### Task 3: Mode.Standard / Competitive / Custom 重写为 Aurora 预设

**Files:**
- Modify: `wpf/Themes/Mode.Standard.xaml`（整体替换）
- Modify: `wpf/Themes/Mode.Competitive.xaml`（整体替换）
- Modify: `wpf/Themes/Mode.Custom.xaml`（整体替换）

**设计要点（规格 §3.2）**：每份模式字典 = 三层极光色 + 径向渐变画刷 + 强度/漂移参数 + Accent 梯度全套 + 兼容别名（`ModeAccent*` 旧 key）。

- [ ] **Step 1: 整体替换 `wpf/Themes/Mode.Standard.xaml`（常规 · 青紫极光）**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">

  <!-- ===== Aurora 三层光晕（规格 §3.2：紫罗兰/蓝/青） ===== -->
  <Color x:Key="AuroraPrimaryColor">#5B3BE8</Color>
  <Color x:Key="AuroraPrimaryFadeColor">#005B3BE8</Color>
  <Color x:Key="AuroraSecondaryColor">#2563EB</Color>
  <Color x:Key="AuroraSecondaryFadeColor">#002563EB</Color>
  <Color x:Key="AuroraTertiaryColor">#0891B2</Color>
  <Color x:Key="AuroraTertiaryFadeColor">#000891B2</Color>
  <sys:Double x:Key="AuroraPrimaryOpacity">0.28</sys:Double>
  <sys:Double x:Key="AuroraSecondaryOpacity">0.22</sys:Double>
  <sys:Double x:Key="AuroraTertiaryOpacity">0.15</sys:Double>
  <sys:Double x:Key="AuroraDriftSeconds">26</sys:Double>

  <!-- 径向渐变画刷：光域中心渐变提前渐隐（Offset 0.55），避免硬边裁剪 -->
  <RadialGradientBrush x:Key="AmbientPrimaryBrush" Center="0.5,0.5" RadiusX="0.5" RadiusY="0.5">
    <GradientStop Color="{DynamicResource AuroraPrimaryColor}" Offset="0"/>
    <GradientStop Color="{DynamicResource AuroraPrimaryFadeColor}" Offset="0.55"/>
    <GradientStop Color="{DynamicResource AuroraPrimaryFadeColor}" Offset="1"/>
  </RadialGradientBrush>
  <RadialGradientBrush x:Key="AmbientSecondaryBrush" Center="0.5,0.5" RadiusX="0.5" RadiusY="0.5">
    <GradientStop Color="{DynamicResource AuroraSecondaryColor}" Offset="0"/>
    <GradientStop Color="{DynamicResource AuroraSecondaryFadeColor}" Offset="0.55"/>
    <GradientStop Color="{DynamicResource AuroraSecondaryFadeColor}" Offset="1"/>
  </RadialGradientBrush>
  <RadialGradientBrush x:Key="AmbientTertiaryBrush" Center="0.5,0.5" RadiusX="0.5" RadiusY="0.5">
    <GradientStop Color="{DynamicResource AuroraTertiaryColor}" Offset="0"/>
    <GradientStop Color="{DynamicResource AuroraTertiaryFadeColor}" Offset="0.55"/>
    <GradientStop Color="{DynamicResource AuroraTertiaryFadeColor}" Offset="1"/>
  </RadialGradientBrush>

  <!-- ===== Accent 梯度（青→紫） ===== -->
  <Color x:Key="AccentPrimaryColor">#67E8F9</Color>
  <Color x:Key="AccentSecondaryColor">#818CF8</Color>
  <SolidColorBrush x:Key="AccentPrimaryBrush" Color="{StaticResource AccentPrimaryColor}"/>
  <SolidColorBrush x:Key="AccentSecondaryBrush" Color="{StaticResource AccentSecondaryColor}"/>
  <LinearGradientBrush x:Key="AccentGradientBrush" StartPoint="0,0" EndPoint="1,0">
    <GradientStop Color="{StaticResource AccentPrimaryColor}" Offset="0"/>
    <GradientStop Color="{StaticResource AccentSecondaryColor}" Offset="1"/>
  </LinearGradientBrush>
  <SolidColorBrush x:Key="AccentSoftBrush" Color="#2467E8F9"/>
  <SolidColorBrush x:Key="AccentEdgeBrush" Color="#5467E8F9"/>
  <Color x:Key="AccentGlowColor">#67E8F9</Color>
  <SolidColorBrush x:Key="OnAccentBrush" Color="#0B0E16"/>

  <!-- ===== 兼容别名层（旧 NavItem/按钮引用，逐页精修后回收） ===== -->
  <Color x:Key="ModeAccentOnDarkColor">#67E8F9</Color>
  <Color x:Key="ModeAccentOnLightColor">#0E7490</Color>
  <SolidColorBrush x:Key="ModeAccentOnDarkBrush" Color="{StaticResource ModeAccentOnDarkColor}"/>
  <SolidColorBrush x:Key="ModeAccentOnLightBrush" Color="{StaticResource ModeAccentOnLightColor}"/>
  <SolidColorBrush x:Key="ModeAccentSoftBrush" Color="#2467E8F9"/>
  <SolidColorBrush x:Key="ModeAccentEdgeBrush" Color="#5467E8F9"/>
</ResourceDictionary>
```

- [ ] **Step 2: 整体替换 `wpf/Themes/Mode.Competitive.xaml`（竞技 · 品红战意，漂移更快）**

与 Step 1 同构，仅替换以下值（结构、key 全集完全一致）：

| key | 值 |
|---|---|
| `AuroraPrimaryColor` / Fade | `#E11D48` / `#00E11D48` |
| `AuroraSecondaryColor` / Fade | `#F97316` / `#00F97316` |
| `AuroraTertiaryColor` / Fade | `#7C2D12` / `#007C2D12` |
| `AuroraPrimaryOpacity` / Secondary / Tertiary | `0.26` / `0.20` / `0.14` |
| `AuroraDriftSeconds` | `22` |
| `AccentPrimaryColor` | `#FB7185` |
| `AccentSecondaryColor` | `#F97316` |
| `AccentSoftBrush` / `AccentEdgeBrush` | `#24FB7185` / `#54FB7185` |
| `AccentGlowColor` | `#FB7185` |
| `OnAccentBrush` | `#14090C` |
| 别名 `ModeAccentOnDarkColor` / `OnLightColor` | `#FB7185` / `#CC2020` |
| 别名 `ModeAccentSoftBrush` / `ModeAccentEdgeBrush` | `#24FB7185` / `#54FB7185` |

- [ ] **Step 3: 整体替换 `wpf/Themes/Mode.Custom.xaml`（自定义 · 琥珀金，漂移最慢；致敬 v14）**

| key | 值 |
|---|---|
| `AuroraPrimaryColor` / Fade | `#D4A847` / `#00D4A847` |
| `AuroraSecondaryColor` / Fade | `#7C3AED` / `#007C3AED` |
| `AuroraTertiaryColor` / Fade | `#8A5A18` / `#008A5A18` |
| `AuroraPrimaryOpacity` / Secondary / Tertiary | `0.24` / `0.16` / `0.14` |
| `AuroraDriftSeconds` | `32` |
| `AccentPrimaryColor` | `#E9C46A` |
| `AccentSecondaryColor` | `#D4A847` |
| `AccentSoftBrush` / `AccentEdgeBrush` | `#24E9C46A` / `#54E9C46A` |
| `AccentGlowColor` | `#E9C46A` |
| `OnAccentBrush` | `#171208` |
| 别名 `ModeAccentOnDarkColor` / `OnLightColor` | `#E9C46A` / `#8A5A18` |
| 别名 `ModeAccentSoftBrush` / `ModeAccentEdgeBrush` | `#24E9C46A` / `#54E9C46A` |

- [ ] **Step 4: 跑契约自测确认全绿**

Run: `cmd.exe //c "dev.cmd test"`
Expected: `TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`（基线 175 + 新增 3 个契约用例全过；FAIL 必须为 0）。

- [ ] **Step 5: Commit**

```bash
git add wpf/Themes/Mode.Standard.xaml wpf/Themes/Mode.Competitive.xaml wpf/Themes/Mode.Custom.xaml
git commit -m "feat: 三模式 Aurora 预设——常规青紫/竞技品红/自定义琥珀金"
```

---

### Task 4: AmbientLayer v2——三层光晕 + 漂移动画

**Files:**
- Modify: `wpf/Controls/AmbientLayer.xaml`（整体替换）
- Modify: `wpf/Controls/AmbientLayer.xaml.cs`（整体替换）

**设计要点（规格 §4.2 AuroraDrift）**：保持公开 API `Show()` / `TransitionTo(bool)` 不变（MainWindow/ModeController 零改动）；前后两对各扩为 3 个光域；漂移只动 `RenderTransform`，`Motion.Enabled=false`（截图探针）或系统降级（`Motion.Reduced`）时不启动。

- [ ] **Step 1: 整体替换 `wpf/Controls/AmbientLayer.xaml`**

```xml
<UserControl x:Class="CaelusApp.WpfHost.Controls.AmbientLayer"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             IsHitTestVisible="False">
  <Grid>
    <!-- 光域中心偏出窗口角落，渐变提前渐隐（Offset 0.55），窗口边缘已透明，避免硬边裁剪 -->
    <Ellipse x:Name="FrontPrimary" Width="620" Height="620" HorizontalAlignment="Right"
             VerticalAlignment="Top" Margin="0,-260,-120,0" Opacity="0"/>
    <Ellipse x:Name="FrontSecondary" Width="520" Height="520" HorizontalAlignment="Right"
             VerticalAlignment="Top" Margin="0,-150,-240,0" Opacity="0"/>
    <Ellipse x:Name="FrontTertiary" Width="460" Height="460" HorizontalAlignment="Left"
             VerticalAlignment="Bottom" Margin="20,0,0,-180" Opacity="0"/>
    <Ellipse x:Name="BackPrimary" Width="620" Height="620" HorizontalAlignment="Right"
             VerticalAlignment="Top" Margin="0,-260,-120,0" Opacity="0"/>
    <Ellipse x:Name="BackSecondary" Width="520" Height="520" HorizontalAlignment="Right"
             VerticalAlignment="Top" Margin="0,-150,-240,0" Opacity="0"/>
    <Ellipse x:Name="BackTertiary" Width="460" Height="460" HorizontalAlignment="Left"
             VerticalAlignment="Bottom" Margin="20,0,0,-180" Opacity="0"/>
  </Grid>
</UserControl>
```

- [ ] **Step 2: 整体替换 `wpf/Controls/AmbientLayer.xaml.cs`**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 Aurora 环境光层 v2：三层光晕两对交替（模式切换交叉淡入）+ 无限漂移（规格 §4.2）

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CaelusApp.WpfHost.Controls
{
    public partial class AmbientLayer : UserControl
    {
        // frontVisible=true 表示 Front 组当前可见
        private bool frontVisible;
        private bool driftStarted;

        public AmbientLayer()
        {
            InitializeComponent();
        }

        // 立即显示当前主题的氛围（启动时用，无动画）
        public void Show()
        {
            ApplyBrushes(FrontPrimary, FrontSecondary, FrontTertiary);
            FrontPrimary.Opacity = Target("AuroraPrimaryOpacity");
            FrontSecondary.Opacity = Target("AuroraSecondaryOpacity");
            FrontTertiary.Opacity = Target("AuroraTertiaryOpacity");
            BackPrimary.Opacity = 0;
            BackSecondary.Opacity = 0;
            BackTertiary.Opacity = 0;
            frontVisible = true;
            StartDrift();
        }

        // 模式切换后的氛围过渡：后组绑定新画刷淡入，前组淡出
        public void TransitionTo(bool animate)
        {
            Ellipse newPrimary = frontVisible ? BackPrimary : FrontPrimary;
            Ellipse newSecondary = frontVisible ? BackSecondary : FrontSecondary;
            Ellipse newTertiary = frontVisible ? BackTertiary : FrontTertiary;
            Ellipse oldPrimary = frontVisible ? FrontPrimary : BackPrimary;
            Ellipse oldSecondary = frontVisible ? FrontSecondary : BackSecondary;
            Ellipse oldTertiary = frontVisible ? FrontTertiary : BackTertiary;

            ApplyBrushes(newPrimary, newSecondary, newTertiary);

            int ms = UiMotion.Duration(UiMotion.NumberRollMs, Motion.Reduced);
            if (!animate || Motion.Reduced || !Motion.Enabled)
            {
                newPrimary.Opacity = Target("AuroraPrimaryOpacity");
                newSecondary.Opacity = Target("AuroraSecondaryOpacity");
                newTertiary.Opacity = Target("AuroraTertiaryOpacity");
                oldPrimary.Opacity = 0;
                oldSecondary.Opacity = 0;
                oldTertiary.Opacity = 0;
                frontVisible = !frontVisible;
                return;
            }

            FadeTo(newPrimary, Target("AuroraPrimaryOpacity"), ms);
            FadeTo(newSecondary, Target("AuroraSecondaryOpacity"), ms);
            FadeTo(newTertiary, Target("AuroraTertiaryOpacity"), ms);
            FadeTo(oldPrimary, 0, ms);
            FadeTo(oldSecondary, 0, ms);
            FadeTo(oldTertiary, 0, ms);
            frontVisible = !frontVisible;
        }

        private static void ApplyBrushes(Ellipse primary, Ellipse secondary, Ellipse tertiary)
        {
            primary.Fill = (Brush)Application.Current.FindResource("AmbientPrimaryBrush");
            secondary.Fill = (Brush)Application.Current.FindResource("AmbientSecondaryBrush");
            tertiary.Fill = (Brush)Application.Current.FindResource("AmbientTertiaryBrush");
        }

        private static double Target(string opacityKey)
        {
            return (double)Application.Current.FindResource(opacityKey);
        }

        private static void FadeTo(Ellipse el, double target, int ms)
        {
            DoubleAnimation anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms));
            anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // 漂移：只动 RenderTransform（渲染线程，开销极低）；
        // 截图探针（Motion.Enabled=false）与系统降级时不启动
        private void StartDrift()
        {
            if (driftStarted) return;
            driftStarted = true;
            if (!Motion.Enabled || Motion.Reduced) return;
            double s = (double)Application.Current.FindResource("AuroraDriftSeconds");
            BeginDrift(FrontPrimary, 40, 30, s, 0);
            BeginDrift(FrontSecondary, -46, 26, s * 1.23, s * 0.3);
            BeginDrift(FrontTertiary, 36, -28, s * 0.85, s * 0.6);
            BeginDrift(BackPrimary, 40, 30, s, 0);
            BeginDrift(BackSecondary, -46, 26, s * 1.23, s * 0.3);
            BeginDrift(BackTertiary, 36, -28, s * 0.85, s * 0.6);
        }

        private static void BeginDrift(Ellipse el, double dx, double dy, double seconds, double beginDelay)
        {
            var tt = new TranslateTransform();
            el.RenderTransform = tt;
            BeginAxis(tt, TranslateTransform.XProperty, dx, seconds, beginDelay);
            BeginAxis(tt, TranslateTransform.YProperty, dy, seconds * 1.13, beginDelay);
        }

        private static void BeginAxis(TranslateTransform tt, DependencyProperty prop,
            double target, double seconds, double beginDelay)
        {
            var anim = new DoubleAnimation(0, target, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginDelay),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            tt.BeginAnimation(prop, anim);
        }
    }
}
```

- [ ] **Step 3: 构建验证**

Run: `cmd.exe //c build-wpf.cmd`
Expected: `WPF Build OK -> wpf\bin\Release\CaelusWpf.exe`（MainWindow/ModeController 未改动，API 不变应直接通过）。

- [ ] **Step 4: Commit**

```bash
git add wpf/Controls/AmbientLayer.xaml wpf/Controls/AmbientLayer.xaml.cs
git commit -m "feat: 环境光层 v2——三层极光光晕 + 无限漂移动画"
```

---

### Task 5: 用户自定义主题加载（Caelus.theme.xaml）

**Files:**
- Modify: `wpf/ThemeManager.cs`（新增 TryApplyUserTheme + using System.IO）
- Modify: `wpf/App.xaml.cs`（OnStartup 中 ThemeManager.Apply 之后调用）

**设计要点（规格 §3.4）**：本期只做加载机制——应用目录存在 `Caelus.theme.xaml` 且通过模式档契约校验，则并入为覆盖层（DynamicResource 全局生效）；校验失败/解析异常记日志忽略，不影响启动。设置页编辑/导出 UI 属后续项。

- [ ] **Step 1: ThemeManager 追加方法**

在 `wpf/ThemeManager.cs` 的 `modeUriFor` 方法之后追加（文件顶部 `using System;` `using System.Windows;` 之外需补 `using System.IO;`）：

```csharp
        // 规格 §3.4：应用目录 Caelus.theme.xaml 通过模式档契约校验则并入覆盖层；
        // 缺 key 或解析失败记日志忽略，绝不影响启动
        public static void TryApplyUserTheme(Application app)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caelus.theme.xaml");
                if (!File.Exists(path)) return;
                string[] missing = ThemeContract.MissingKeys(File.ReadAllText(path), ThemeContract.ModeKeys);
                if (missing.Length > 0)
                {
                    LogUserTheme("用户主题缺 key 已忽略：" + string.Join("、", missing));
                    return;
                }
                using (FileStream fs = File.OpenRead(path))
                {
                    var dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(fs);
                    app.Resources.MergedDictionaries.Add(dict);
                }
            }
            catch (Exception ex)
            {
                LogUserTheme("用户主题加载失败：" + ex.Message);
            }
        }

        private static void LogUserTheme(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "CaelusWpf.crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch { }
        }
```

- [ ] **Step 2: App.xaml.cs 接入**

`wpf/App.xaml.cs` 的 `OnStartup` 中，在 `ThemeManager.Apply(this, UiTone.Dark, initial);`（约 43 行）之后插入一行：

```csharp
            ThemeManager.TryApplyUserTheme(this);
```

- [ ] **Step 3: 构建 + 自测**

Run: `cmd.exe //c build-wpf.cmd` 然后 `cmd.exe //c "dev.cmd test"`
Expected: `WPF Build OK`；自测 `TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`（本任务不新增用例——校验逻辑已由契约正反样例覆盖，XamlReader 部分由构建+后续实机验证覆盖）。

- [ ] **Step 4: Commit**

```bash
git add wpf/ThemeManager.cs wpf/App.xaml.cs
git commit -m "feat: 用户自定义主题入口——Caelus.theme.xaml 契约校验后并入"
```

---

## M2 · 视觉资产

### Task 6: 几何线性图标体系（Icons.xaml + IconView）

**Files:**
- Create: `wpf/Themes/Icons.xaml`
- Create: `wpf/Controls/IconView.cs`
- Modify: `wpf/App.xaml`（合并 Icons.xaml）
- Modify: `wpf/Caelus.Wpf.csproj`（登记 Page + Compile）

**设计要点（规格 §4.1）**：24×24 网格 StreamGeometry 线条路径；IconView 按 key 查找资源描边绘制，`Foreground` 随父级继承（选中态变色自动生效）。

- [ ] **Step 1: 创建 `wpf/Themes/Icons.xaml`**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- 几何线性图标体系：24x24 网格，2px 线宽，圆角接头（规格 §4.1） -->
  <StreamGeometry x:Key="IconOverview">M12 2l3 7h7l-5.5 4.5L18 21l-6-4-6 4 1.5-7.5L2 9h7z</StreamGeometry>
  <StreamGeometry x:Key="IconLibrary">M4 7l8-4 8 4-8 4-8-4z M4 7v10l8 4 8-4V7</StreamGeometry>
  <StreamGeometry x:Key="IconPolicy">M4 8h9 M17 8h3 M4 16h3 M11 16h9 M13 8a2 2 0 1 1 4 0 2 2 0 1 1-4 0 M7 16a2 2 0 1 1 4 0 2 2 0 1 1-4 0</StreamGeometry>
  <StreamGeometry x:Key="IconShield">M12 3l7 3v5c0 4.5-3 8.5-7 10-4-1.5-7-5.5-7-10V6z</StreamGeometry>
  <StreamGeometry x:Key="IconWhitelist">M8 6h13 M8 12h13 M8 18h13 M3 6l1 1 2-2 M3 12l1 1 2-2 M3 18l1 1 2-2</StreamGeometry>
  <StreamGeometry x:Key="IconLog">M4 6h16 M4 12h16 M4 18h10</StreamGeometry>
  <StreamGeometry x:Key="IconGpu">M7 5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M9 1v4 M15 1v4 M9 19v4 M15 19v4 M1 9h4 M1 15h4 M19 9h4 M19 15h4</StreamGeometry>
  <StreamGeometry x:Key="IconEnvironment">M9 12a3 3 0 1 1 6 0 3 3 0 1 1-6 0 M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M19.1 4.9L17 7 M7 17l-2.1 2.1</StreamGeometry>
  <StreamGeometry x:Key="IconAudit">M3 12h4l3-8 4 16 3-8h4</StreamGeometry>
  <StreamGeometry x:Key="IconSettings">M3 12a9 9 0 1 1 18 0 9 9 0 1 1-18 0 M12 5v2 M12 17v2 M5 12h2 M17 12h2 M12 12h0.01</StreamGeometry>
  <StreamGeometry x:Key="IconInfo">M3 12a9 9 0 1 1 18 0 9 9 0 1 1-18 0 M12 11v5 M12 8h0.01</StreamGeometry>
  <!-- 品牌核心闪电（实心填充用） -->
  <StreamGeometry x:Key="IconBolt">M13 2L4 14h6l-1 8 9-12h-6z</StreamGeometry>
</ResourceDictionary>
```

- [ ] **Step 2: 创建 `wpf/Controls/IconView.cs`**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 几何线性图标宿主：按 key 从主题资源取 StreamGeometry 描边绘制（规格 §4.1）

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Controls
{
    internal sealed class IconView : Control
    {
        public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
            "Key", typeof(string), typeof(IconView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Key
        {
            get { return (string)GetValue(KeyProperty); }
            set { SetValue(KeyProperty, value); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            string key = Key;
            if (key == null) return;
            Geometry geo = Application.Current.TryFindResource(key) as Geometry;
            if (geo == null) return;
            double size = Math.Min(RenderSize.Width, RenderSize.Height);
            if (size <= 0) size = 16;
            dc.PushTransform(new ScaleTransform(size / 24.0, size / 24.0));
            var pen = new Pen(Foreground, 2.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
            dc.Pop();
        }
    }
}
```

- [ ] **Step 3: App.xaml 合并 + csproj 登记**

`wpf/App.xaml` 的 `MergedDictionaries` 中在 `<ResourceDictionary Source="Themes/Tokens.xaml"/>` 之后加一行：

```xml
        <ResourceDictionary Source="Themes/Icons.xaml"/>
```

`wpf/Caelus.Wpf.csproj`：在 `<Page Include="Themes\Styles.xaml" />` 之后加 `<Page Include="Themes\Icons.xaml" />`；在 `<Compile Include="Motion.cs" />` 之后加 `<Compile Include="Controls\IconView.cs" />`。

- [ ] **Step 4: 构建验证**

Run: `cmd.exe //c build-wpf.cmd`
Expected: `WPF Build OK`。

- [ ] **Step 5: Commit**

```bash
git add wpf/Themes/Icons.xaml wpf/Controls/IconView.cs wpf/App.xaml wpf/Caelus.Wpf.csproj
git commit -m "feat: 几何线性图标体系——11 枚 StreamGeometry 图标 + IconView 控件"
```

---

### Task 7: Motion 扩展（Lift 附加属性 / Pulse / Spin）+ ThemeManager.ModeChanged

**Files:**
- Modify: `wpf/Motion.cs`（追加 Lift/Pulse/Spin，补 using）
- Modify: `wpf/ThemeManager.cs`（新增 ModeChanged 事件）

**设计要点（规格 §4.2）**：全部走 `RenderTransform`/`Opacity`；`Motion.Enabled=false`（截图探针）与系统降级（`Motion.Reduced`）时静默跳过。本期**不做**应用内「减弱动效」开关（用户已确认）。

- [ ] **Step 1: ThemeManager 增加 ModeChanged 事件**

`wpf/ThemeManager.cs`：`CurrentMode` 属性之后加事件声明；`Apply` 方法末尾（`Native.LightModeQuery = ...` 之前）触发：

```csharp
        // 模式（或明暗）换槽完成事件：CaelusCore 等随模式换肤的控件订阅
        public static event EventHandler ModeChanged;
```

```csharp
            Native.LightModeQuery = () => tone == UiTone.Light;
            var handler = ModeChanged;
            if (handler != null) handler(null, EventArgs.Empty);
```

- [ ] **Step 2: Motion.cs 追加三个动效**

`wpf/Motion.cs` 顶部补 `using System.Windows.Input;`，然后在 `FadeIn` 方法之后追加：

```csharp
        // ===== Lift 附加属性：悬停浮起（规格 §4.2 HoverLift，TranslateY 0→-3，250ms）=====
        public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached(
            "Lift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnLiftChanged));

        public static bool GetLift(DependencyObject d) { return (bool)d.GetValue(LiftProperty); }
        public static void SetLift(DependencyObject d, bool value) { d.SetValue(LiftProperty, value); }

        private static void OnLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            UIElement el = d as UIElement;
            if (el == null || !(bool)e.NewValue) return;
            el.MouseEnter += OnLiftEnter;
            el.MouseLeave += OnLiftLeave;
        }

        private static void OnLiftEnter(object sender, MouseEventArgs e) { LiftTo((UIElement)sender, -3); }
        private static void OnLiftLeave(object sender, MouseEventArgs e) { LiftTo((UIElement)sender, 0); }

        private static void LiftTo(UIElement el, double y)
        {
            if (!Enabled) return;
            FrameworkElement fe = el as FrameworkElement;
            if (fe == null) return;
            TranslateTransform tt = fe.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform();
                fe.RenderTransform = tt;
            }
            if (Reduced) { tt.Y = y; return; }
            var anim = new DoubleAnimation(y, TimeSpan.FromMilliseconds(UiMotion.PageFadeMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            tt.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        // ===== READY 脉冲：透明度呼吸 2.4s 无限（规格 §4.2 ReadyPulse）=====
        public static void Pulse(UIElement el)
        {
            if (el == null || !Enabled || Reduced) return;
            var anim = new DoubleAnimation(1, 0.45, TimeSpan.FromSeconds(2.4))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // ===== 无限旋转：CaelusCore 双环（规格 §4.2 CoreSpin，RotateTransform 比 dash 动画稳）=====
        public static void Spin(FrameworkElement el, double seconds, bool reverse)
        {
            if (el == null || !Enabled || Reduced) return;
            RotateTransform rt = el.RenderTransform as RotateTransform;
            if (rt == null)
            {
                rt = new RotateTransform();
                el.RenderTransformOrigin = new Point(0.5, 0.5);
                el.RenderTransform = rt;
            }
            var anim = new DoubleAnimation(0, reverse ? -360 : 360, TimeSpan.FromSeconds(seconds))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            rt.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
```

- [ ] **Step 3: 构建验证**

Run: `cmd.exe //c build-wpf.cmd`
Expected: `WPF Build OK`。

- [ ] **Step 4: Commit**

```bash
git add wpf/Motion.cs wpf/ThemeManager.cs
git commit -m "feat: 动效三件套——悬停浮起 Lift/READY 脉冲 Pulse/无限旋转 Spin + ModeChanged 事件"
```

---

### Task 8: CaelusCore 品牌核心控件

**Files:**
- Create: `wpf/Controls/CaelusCore.xaml`
- Create: `wpf/Controls/CaelusCore.xaml.cs`
- Modify: `wpf/Caelus.Wpf.csproj`（登记 Page + Compile）

**设计要点（规格 §4.3）**：132×132 组合体；外虚线环 14s 正转、中双弧环 22s 反转（`StrokeDashArray` 截弧 + RotateTransform，net4 最稳方案）；颜色全部 DynamicResource 随模式换肤；**仅概览页 Hero 使用**（Task 11 接入）。中层弧长：r=46 圆周≈289，四分之一弧≈72 → `StrokeDashArray="72 217"`，两条弧相位差 180°。

- [ ] **Step 1: 创建 `wpf/Controls/CaelusCore.xaml`**

```xml
<UserControl x:Class="CaelusApp.WpfHost.Controls.CaelusCore"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="132" Height="132" IsHitTestVisible="False">
  <Grid>
    <!-- 品牌辉光底（AccentGlow 径向渐隐；本环境 DropShadowEffect 不可用，用渐变等效） -->
    <Ellipse Width="120" Height="120" Opacity="0.35">
      <Ellipse.Fill>
        <RadialGradientBrush>
          <GradientStop Color="{DynamicResource AccentGlowColor}" Offset="0"/>
          <GradientStop Color="#00000000" Offset="0.7"/>
        </RadialGradientBrush>
      </Ellipse.Fill>
    </Ellipse>

    <!-- 外虚线环 + 顶点 Accent 亮点（正转 14s） -->
    <Grid x:Name="RingOuter">
      <Ellipse Width="112" Height="112" Stroke="{DynamicResource BorderStrongBrush}"
               StrokeThickness="1" StrokeDashArray="3 7"/>
      <Ellipse Width="5" Height="5" Fill="{DynamicResource AccentPrimaryBrush}"
               VerticalAlignment="Top" Margin="0,8,0,0"/>
    </Grid>

    <!-- 中双弧环（反转 22s）：渐变弧 + 180° 相位 AccentEdge 弧 -->
    <Grid x:Name="RingMid">
      <Ellipse Width="92" Height="92" StrokeThickness="3" StrokeDashArray="72 217" StrokeDashCap="Round">
        <Ellipse.Stroke>
          <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="{DynamicResource AccentPrimaryColor}" Offset="0"/>
            <GradientStop Color="{DynamicResource AccentSecondaryColor}" Offset="1"/>
          </LinearGradientBrush>
        </Ellipse.Stroke>
      </Ellipse>
      <Ellipse Width="92" Height="92" Stroke="{DynamicResource AccentEdgeBrush}"
               StrokeThickness="3" StrokeDashArray="72 217" StrokeDashCap="Round"
               RenderTransformOrigin="0.5,0.5">
        <Ellipse.RenderTransform>
          <RotateTransform Angle="180"/>
        </Ellipse.RenderTransform>
      </Ellipse>
    </Grid>

    <!-- 内刻度环（静止） -->
    <Ellipse Width="72" Height="72" Stroke="{DynamicResource BorderSubtleBrush}"
             StrokeThickness="4" StrokeDashArray="2 4"/>

    <!-- 中心盘 -->
    <Ellipse Width="60" Height="60" Fill="{DynamicResource Surface1Brush}"
             Stroke="{DynamicResource AccentEdgeBrush}" StrokeThickness="1"/>

    <!-- 闪电 + 模式名 -->
    <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
      <Path Width="20" Height="20" Stretch="Uniform" HorizontalAlignment="Center"
            Fill="{DynamicResource AccentPrimaryBrush}" Data="{DynamicResource IconBolt}"/>
      <TextBlock x:Name="ModeLabel" Text="常规" FontSize="9" Margin="0,2,0,0"
                 HorizontalAlignment="Center"
                 Foreground="{DynamicResource TextSecondaryBrush}"/>
    </StackPanel>
  </Grid>
</UserControl>
```

- [ ] **Step 2: 创建 `wpf/Controls/CaelusCore.xaml.cs`**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 Caelus Core 品牌核心：双环反向旋转 + 模式名随换肤（规格 §4.3，仅概览页 Hero 使用）

using System;
using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Controls
{
    public partial class CaelusCore : UserControl
    {
        public CaelusCore()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.Spin(RingOuter, 14, false);
            Motion.Spin(RingMid, 22, true);
            ThemeManager.ModeChanged += OnModeChanged;
            RefreshModeLabel();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ModeChanged -= OnModeChanged;
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            RefreshModeLabel();
        }

        private void RefreshModeLabel()
        {
            ModeLabel.Text = ThemeManager.CurrentMode == AppMode.Competitive ? "竞技"
                : ThemeManager.CurrentMode == AppMode.Custom ? "自定义" : "常规";
        }
    }
}
```

- [ ] **Step 3: csproj 登记 + 构建**

`wpf/Caelus.Wpf.csproj`：`<Page Include="Controls\AmbientLayer.xaml" />` 之后加 `<Page Include="Controls\CaelusCore.xaml" />`；`<Compile Include="Controls\AmbientLayer.xaml.cs">...</Compile>` 块之后加：

```xml
    <Compile Include="Controls\CaelusCore.xaml.cs">
      <DependentUpon>Controls\CaelusCore.xaml</DependentUpon>
    </Compile>
```

Run: `cmd.exe //c build-wpf.cmd`
Expected: `WPF Build OK`。

- [ ] **Step 4: Commit**

```bash
git add wpf/Controls/CaelusCore.xaml wpf/Controls/CaelusCore.xaml.cs wpf/Caelus.Wpf.csproj
git commit -m "feat: CaelusCore 品牌核心——双环反向旋转 + 模式换肤（仅概览 Hero 用）"
```

---

### Task 9: Sparkline 趋势线控件 + GPU 温度序列数据源

**Files:**
- Create: `wpf/Controls/Sparkline.cs`
- Modify: `wpf/Caelus.Wpf.csproj`（登记 Compile）
- Modify: `src/UiShared/OverviewViewModel.cs`（接口与 VM 增加 TempHistory/GpuTempSeries；SampleOverviewSource 生成示例序列）

**设计要点（规格 §4.4）**：`IList<double>` 归一化折线 + 描边色 9% 面积淡填充；真实遥测序列属遗留项「实时指标」，本期由 SampleOverviewSource 产出确定性随机游走示例（固定种子，截图可复现）。

- [ ] **Step 1: 创建 `wpf/Controls/Sparkline.cs`**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 迷你趋势线：数据点归一化折线 + 描边色 9% 面积淡填充（规格 §4.4）

using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Controls
{
    internal sealed class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            "Values", typeof(IList<double>), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IList<double> Values
        {
            get { return (IList<double>)GetValue(ValuesProperty); }
            set { SetValue(ValuesProperty, value); }
        }

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            "Stroke", typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke
        {
            get { return (Brush)GetValue(StrokeProperty); }
            set { SetValue(StrokeProperty, value); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            IList<double> values = Values;
            if (values == null || values.Count < 2) return;
            double w = RenderSize.Width, h = RenderSize.Height;
            if (w < 4 || h < 4) return;

            double min = double.MaxValue, max = double.MinValue;
            foreach (double v in values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            double range = max - min;
            if (range < 0.001) range = 1;

            // 面积淡填充（描边色取 9% 透明度）
            Brush stroke = Stroke;
            var scb = stroke as SolidColorBrush;
            if (scb != null)
            {
                Color c = scb.Color;
                var area = new StreamGeometry();
                using (StreamGeometryContext ctx = area.Open())
                {
                    ctx.BeginFigure(Pt(values, 0, w, h, min, range), true, true);
                    for (int i = 1; i < values.Count; i++)
                        ctx.LineTo(Pt(values, i, w, h, min, range), true, false);
                    ctx.LineTo(new Point(w, h), true, false);
                    ctx.LineTo(new Point(0, h), true, false);
                }
                area.Freeze();
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(23, c.R, c.G, c.B)), null, area);
            }

            var line = new StreamGeometry();
            using (StreamGeometryContext ctx = line.Open())
            {
                ctx.BeginFigure(Pt(values, 0, w, h, min, range), false, false);
                for (int i = 1; i < values.Count; i++)
                    ctx.LineTo(Pt(values, i, w, h, min, range), true, false);
            }
            line.Freeze();
            if (stroke != null)
            {
                var pen = new Pen(stroke, 1.6) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, line);
            }
        }

        // 数据点 → 画布坐标（留 1.5px 描边余量；框架自带 csc 只认 C# 5，勿用局部函数）
        private static Point Pt(IList<double> values, int i, double w, double h, double min, double range)
        {
            double x = w * i / (values.Count - 1);
            double y = h - 1.5 - (h - 3) * (values[i] - min) / range;
            return new Point(x, y);
        }

- [ ] **Step 2: csproj 登记**

`wpf/Caelus.Wpf.csproj`：`<Compile Include="Controls\IconView.cs" />` 之后加 `<Compile Include="Controls\Sparkline.cs" />`。

- [ ] **Step 3: ViewModel 数据源**

`src/UiShared/OverviewViewModel.cs`（该文件同时含数据源接口与 `SampleOverviewSource`，两 exe 共享）：

a) 数据源接口（含 `GpuTempC` 的接口，约 14-16 行）追加一个成员：

```csharp
        System.Collections.Generic.IList<double> TempHistory { get; }
```

b) `OverviewViewModel` 类中（`Metrics` 属性附近）追加：

```csharp
        private System.Collections.Generic.IList<double> gpuTempSeries;
        public System.Collections.Generic.IList<double> GpuTempSeries
        {
            get { return gpuTempSeries; }
            private set { SetProperty(ref gpuTempSeries, value, "GpuTempSeries"); }
        }
```

`Refresh()` 方法末尾（`ConclusionColorKey = ...` 之后）追加：

```csharp
            GpuTempSeries = source.TempHistory;
```

c) `SampleOverviewSource` 类中追加（示例序列：固定种子随机游走，截图可复现；真实遥测序列属遗留项「实时指标」）：

```csharp
        private System.Collections.Generic.IList<double> tempHistory;
        public System.Collections.Generic.IList<double> TempHistory
        {
            get
            {
                if (tempHistory == null)
                {
                    var rng = new System.Random(20260811);
                    var list = new System.Collections.Generic.List<double>(24);
                    double v = 58;
                    for (int i = 0; i < 24; i++)
                    {
                        v += rng.NextDouble() * 4 - 2;
                        if (v < 54) v = 54;
                        if (v > 66) v = 66;
                        list.Add(v);
                    }
                    tempHistory = list;
                }
                return tempHistory;
            }
        }
```

- [ ] **Step 4: 构建 + 自测**

Run: `cmd.exe //c build-wpf.cmd` 然后 `cmd.exe //c "dev.cmd test"`
Expected: `WPF Build OK`；自测 `TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`。

- [ ] **Step 5: Commit**

```bash
git add wpf/Controls/Sparkline.cs wpf/Caelus.Wpf.csproj src/UiShared/OverviewViewModel.cs
git commit -m "feat: Sparkline 趋势线控件 + GPU 温度示例序列（固定种子）"
```

---

## M3 · 卡片排印升级 + 概览页重构（示例案例）

### Task 10: Tokens/Styles v2——GlassCard、渐变描边、排印样式

**Files:**
- Create: `wpf/Controls/GlassCard.cs`
- Modify: `wpf/Caelus.Wpf.csproj`（登记 Compile）
- Modify: `wpf/Themes/Tokens.xaml`（新增 3 个字号 token）
- Modify: `wpf/Themes/Styles.xaml`（追加新样式；CardBorder/HeroCardBorder 描边升级为渐变）

**设计要点（规格 §5）**：GlassCard = ContentControl 子类 + 模板（渐变描边 + 顶部高光线 + 悬停 HoverEdge 点亮）；不用 DropShadowEffect（环境不渲染）。`CardBorder` 等旧样式描边直接升级为 `CardEdgeBrush` 渐变——全部旧视图自动受益。

- [ ] **Step 1: 创建 `wpf/Controls/GlassCard.cs` + csproj 登记**

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 Aurora 玻璃卡片：渐变描边 + 顶部高光线 + 悬停描边点亮（规格 §5）

using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Controls
{
    internal sealed class GlassCard : ContentControl
    {
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
            "CornerRadius", typeof(CornerRadius), typeof(GlassCard),
            new FrameworkPropertyMetadata(new CornerRadius(14)));

        public CornerRadius CornerRadius
        {
            get { return (CornerRadius)GetValue(CornerRadiusProperty); }
            set { SetValue(CornerRadiusProperty, value); }
        }
    }
}
```

csproj：`<Compile Include="Controls\Sparkline.cs" />` 之后加 `<Compile Include="Controls\GlassCard.cs" />`。

- [ ] **Step 2: Tokens.xaml 追加字号**

`wpf/Themes/Tokens.xaml` 的 `<sys:Double x:Key="FontSizeMono">13</sys:Double>` 之后追加：

```xml
  <sys:Double x:Key="FontSizeSection">26</sys:Double>
  <sys:Double x:Key="FontSizeHero">48</sys:Double>
  <sys:Double x:Key="FontSizeMetric">28</sys:Double>
```

- [ ] **Step 3: Styles.xaml 追加 v2 样式**

`wpf/Themes/Styles.xaml`：文件尾 `</ResourceDictionary>` 之前追加（需先在根元素补命名空间声明 `xmlns:controls="clr-namespace:CaelusApp.WpfHost.Controls"`）：

```xml
  <!-- ===== Aurora Bento v2 ===== -->

  <!-- 玻璃卡片 v2：渐变描边 + 顶部高光线 + 悬停描边点亮（规格 §5；不用 Effect——环境不渲染） -->
  <Style x:Key="GlassCard" TargetType="controls:GlassCard">
    <Setter Property="Background" Value="{DynamicResource Surface0Brush}"/>
    <Setter Property="Padding" Value="14"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="controls:GlassCard">
          <Grid>
            <Border CornerRadius="{TemplateBinding CornerRadius}"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{DynamicResource CardEdgeBrush}" BorderThickness="1"/>
            <Border x:Name="HoverEdge" CornerRadius="{TemplateBinding CornerRadius}"
                    BorderBrush="{DynamicResource BorderStrongBrush}" BorderThickness="1" Opacity="0"/>
            <Rectangle Height="1" VerticalAlignment="Top" Margin="14,0"
                       RadiusX="2" RadiusY="2" Fill="{DynamicResource TopHighlightBrush}"/>
            <ContentPresenter Margin="{TemplateBinding Padding}"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="HoverEdge" Storyboard.TargetProperty="Opacity"
                                     To="1" Duration="0:0:0.15"/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="HoverEdge" Storyboard.TargetProperty="Opacity"
                                     To="0" Duration="0:0:0.2"/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 排印（规格 §5）：超大细字重 + 等宽数字 -->
  <Style x:Key="DisplayNumber" TargetType="TextBlock">
    <Setter Property="FontSize" Value="{DynamicResource FontSizeHero}"/>
    <Setter Property="FontWeight" Value="Light"/>
    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
    <Setter Property="Typography.NumeralAlignment" Value="Tabular"/>
  </Style>
  <Style x:Key="MetricNumber" TargetType="TextBlock">
    <Setter Property="FontSize" Value="{DynamicResource FontSizeMetric}"/>
    <Setter Property="FontWeight" Value="Light"/>
    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
    <Setter Property="Typography.NumeralAlignment" Value="Tabular"/>
  </Style>
  <Style x:Key="CardLabel" TargetType="TextBlock">
    <Setter Property="FontSize" Value="10"/>
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
  </Style>
  <Style x:Key="SectionTitle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="{DynamicResource FontSizeSection}"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
  </Style>
  <Style x:Key="SectionSubtitle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
  </Style>
```

- [ ] **Step 4: 旧卡片样式描边升级（全部旧视图自动受益）**

`wpf/Themes/Styles.xaml` 中三处替换（`CardBorder`、`HeroCardBorder`、`PolicyCard`、`LibraryItemCard` 共四处）：
- `Value="{DynamicResource GlassBorderBrush}"`（CardBorder / PolicyCard / LibraryItemCard 的 BorderBrush setter）→ `Value="{DynamicResource CardEdgeBrush}"`
- `Value="{DynamicResource GlassBorderHiBrush}"`（HeroCardBorder 的 BorderBrush setter）→ `Value="{DynamicResource CardEdgeBrush}"`
- 四处样式的 `CornerRadius` setter 由 `{DynamicResource RadiusMd}` → `{DynamicResource RadiusLg}`（14px，规格 §5）

- [ ] **Step 5: 构建 + 自测**

Run: `cmd.exe //c build-wpf.cmd` 然后 `cmd.exe //c "dev.cmd test"`
Expected: `WPF Build OK`；`TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`。

- [ ] **Step 6: Commit**

```bash
git add wpf/Controls/GlassCard.cs wpf/Caelus.Wpf.csproj wpf/Themes/Tokens.xaml wpf/Themes/Styles.xaml
git commit -m "feat: 卡片 v2——GlassCard 渐变描边+高光线+悬停点亮；排印样式；旧卡片描边升级"
```

---

### Task 11: 概览页重构（Bento 布局 + Hero + CaelusCore + Sparkline）

**Files:**
- Modify: `wpf/Views/OverviewView.xaml`（整体替换）
- Modify: `wpf/Views/OverviewView.xaml.cs`（OnLoaded 增加 Pulse）

**设计要点（规格 §6）**：ViewModel 零改动（`ConclusionTitle/ConclusionDetail/DetailVisible/ToggleDetailCommand/Metrics/GpuTempSeries`）；GPU 大卡聚光灯绑定 `Metrics[0]`（Refresh 固定首项为 GPU 温度——顺序契约，改动时需同步）；R2 指标行用 `CollectionViewSource.Filter` 过滤掉首项避免与大卡重复（纯视图层过滤，VM 不动）。

- [ ] **Step 1: 整体替换 `wpf/Views/OverviewView.xaml`**

```xml
<UserControl x:Class="CaelusApp.WpfHost.Views.OverviewView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:CaelusApp.WpfHost"
             xmlns:controls="clr-namespace:CaelusApp.WpfHost.Controls">
  <UserControl.Resources>
    <local:KeyBrushConverter x:Key="KeyBrush"/>
    <BooleanToVisibilityConverter x:Key="BoolVis"/>
    <local:DetailButtonTextConverter x:Key="DetailText"/>
    <local:FractionGridLengthConverter x:Key="FracLen"/>
    <!-- R2 指标行过滤掉首项（GPU 温度已在大卡聚光灯展示，避免重复） -->
    <CollectionViewSource x:Key="MetricsRest" Source="{Binding Metrics}" Filter="OnMetricsFilter"/>
  </UserControl.Resources>
  <StackPanel>

    <!-- 页头（规格 §6） -->
    <StackPanel Margin="0,0,0,14">
      <TextBlock Text="系统状态" Style="{DynamicResource SectionTitle}"/>
      <TextBlock Text="上次检查 2 分钟前 · 管理员权限" Style="{DynamicResource SectionSubtitle}" Margin="0,3,0,0"/>
    </StackPanel>

    <!-- Bento R1：Hero（1.9fr）│ GPU 温度（1fr） -->
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="1.9*"/>
        <ColumnDefinition Width="14"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>

      <controls:GlassCard Grid.Column="0" Style="{DynamicResource GlassCard}">
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <StackPanel VerticalAlignment="Center">
            <!-- READY 脉冲徽章 -->
            <Border HorizontalAlignment="Left" CornerRadius="99" Padding="10,4" Margin="0,0,0,10"
                    Background="{DynamicResource AccentSoftBrush}"
                    BorderBrush="{DynamicResource AccentEdgeBrush}" BorderThickness="1">
              <StackPanel Orientation="Horizontal">
                <Ellipse x:Name="ReadyDot" Width="6" Height="6" VerticalAlignment="Center"
                         Fill="{DynamicResource AccentPrimaryBrush}"/>
                <TextBlock Text="READY · 已就绪" FontSize="10" FontWeight="SemiBold" Margin="6,0,0,0"
                           Foreground="{DynamicResource AccentPrimaryBrush}"/>
              </StackPanel>
            </Border>
            <TextBlock Text="{Binding ConclusionTitle}" FontSize="20" FontWeight="SemiBold"
                       Foreground="{DynamicResource TextPrimaryBrush}"/>
            <TextBlock Text="{Binding ConclusionDetail}" FontSize="12" Margin="0,5,0,12"
                       Foreground="{DynamicResource TextSecondaryBrush}"/>
            <Button Style="{DynamicResource GhostButton}" HorizontalAlignment="Left"
                    AutomationProperties.Name="查看或收起诊断详情"
                    Content="{Binding DetailVisible, Converter={StaticResource DetailText}}"
                    Command="{Binding ToggleDetailCommand}"/>
          </StackPanel>
          <controls:CaelusCore Grid.Column="1" VerticalAlignment="Center" Margin="14,0,0,0"/>
        </Grid>
      </controls:GlassCard>

      <!-- GPU 温度大卡：48px 细字重 + 趋势线（Metrics[0] = GPU 温度，顺序契约见 Task 11 说明） -->
      <controls:GlassCard Grid.Column="2" Style="{DynamicResource GlassCard}">
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
          </Grid.RowDefinitions>
          <DockPanel>
            <TextBlock Text="GPU 温度" Style="{DynamicResource CardLabel}"/>
            <TextBlock Text="● 正常" FontSize="9" DockPanel.Dock="Right"
                       Foreground="{DynamicResource SuccessBrush}"/>
          </DockPanel>
          <TextBlock Grid.Row="1" Text="{Binding Metrics[0].ValueText}"
                     Style="{DynamicResource DisplayNumber}" VerticalAlignment="Center"/>
          <controls:Sparkline Grid.Row="2" Height="30"
                              Values="{Binding GpuTempSeries}"
                              Stroke="{DynamicResource AccentPrimaryBrush}"/>
        </Grid>
      </controls:GlassCard>
    </Grid>

    <!-- 详情区（渐进披露：默认收起） -->
    <controls:GlassCard Style="{DynamicResource GlassCard}" Margin="0,10,0,0"
            Visibility="{Binding DetailVisible, Converter={StaticResource BoolVis}}">
      <TextBlock Text="诊断详情将在后续阶段接入实时数据"
                 Foreground="{DynamicResource TextTertiaryBrush}"
                 FontSize="{DynamicResource FontSizeCaption}"/>
    </controls:GlassCard>

    <!-- Bento R2：指标行（已过滤 GPU 温度首项，剩 2 项 → 2 列；ItemsControl 右 Margin -14 抵消末卡右间距，对齐右缘） -->
    <ItemsControl ItemsSource="{Binding Source={StaticResource MetricsRest}}" Margin="0,14,-14,0">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
          <UniformGrid Columns="2"/>
        </ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <controls:GlassCard Style="{DynamicResource GlassCard}" Margin="0,0,14,0"
                              Padding="14,12" local:Motion.Lift="True">
            <StackPanel>
              <TextBlock Text="{Binding Label}" Style="{DynamicResource CardLabel}"/>
              <TextBlock Text="{Binding ValueText}" Style="{DynamicResource MetricNumber}" Margin="0,6,0,0"/>
              <Grid Height="3" Margin="0,10,0,0">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="{Binding Fraction, Converter={StaticResource FracLen}}"/>
                  <ColumnDefinition Width="{Binding Fraction, Converter={StaticResource FracLen}, ConverterParameter=rest}"/>
                </Grid.ColumnDefinitions>
                <Border Grid.ColumnSpan="2" CornerRadius="2" Background="{DynamicResource TrackBrush}"/>
                <Border Grid.Column="0" CornerRadius="2" HorizontalAlignment="Stretch"
                        Background="{Binding ColorKey, Converter={StaticResource KeyBrush}}"/>
              </Grid>
            </StackPanel>
          </controls:GlassCard>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>

    <!-- Bento R3：最近活动（1.4fr）│ 硬件摘要（1fr） -->
    <Grid Margin="0,14,0,0">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="1.4*"/>
        <ColumnDefinition Width="14"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>

      <controls:GlassCard Grid.Column="0" Style="{DynamicResource GlassCard}">
        <StackPanel>
          <TextBlock Text="最近活动" Style="{DynamicResource CardLabel}" FontWeight="Bold"
                     Foreground="{DynamicResource TextPrimaryBrush}"/>
          <Grid Margin="0,8,0,0">
            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <TextBlock Text="竞技模式已启用" FontSize="11" Foreground="{DynamicResource TextSecondaryBrush}"/>
            <TextBlock Grid.Column="1" Text="刚刚" FontSize="11" Foreground="{DynamicResource TextTertiaryBrush}"/>
          </Grid>
          <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <TextBlock Text="系统体检完成" FontSize="11" Foreground="{DynamicResource TextSecondaryBrush}"/>
            <TextBlock Grid.Column="1" Text="2 分钟前" FontSize="11" Foreground="{DynamicResource TextTertiaryBrush}"/>
          </Grid>
          <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <TextBlock Text="游戏检测就绪" FontSize="11" Foreground="{DynamicResource TextSecondaryBrush}"/>
            <TextBlock Grid.Column="1" Text="已开启" FontSize="11" Foreground="{DynamicResource SuccessBrush}"/>
          </Grid>
        </StackPanel>
      </controls:GlassCard>

      <controls:GlassCard Grid.Column="2" Style="{DynamicResource GlassCard}">
        <StackPanel>
          <TextBlock Text="硬件摘要" Style="{DynamicResource CardLabel}" FontWeight="Bold"
                     Foreground="{DynamicResource TextPrimaryBrush}"/>
          <TextBlock Text="i5-10300H · 8 逻辑核" FontSize="11" Margin="0,8,0,0"
                     Foreground="{DynamicResource TextSecondaryBrush}"/>
          <TextBlock Text="GTX 1650 Ti" FontSize="11" Margin="0,4,0,0"
                     Foreground="{DynamicResource TextSecondaryBrush}"/>
          <TextBlock Text="16 GB DDR4" FontSize="11" Margin="0,4,0,0"
                     Foreground="{DynamicResource TextSecondaryBrush}"/>
        </StackPanel>
      </controls:GlassCard>
    </Grid>
  </StackPanel>
</UserControl>
```

- [ ] **Step 2: OverviewView.xaml.cs 增加 READY 脉冲 + 指标过滤器**

`wpf/Views/OverviewView.xaml.cs` 整体替换：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CaelusApp.WpfHost.Views
{
    public partial class OverviewView : UserControl
    {
        public OverviewView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.FadeIn(this);
            Motion.Pulse(ReadyDot);
        }

        // R2 指标行过滤掉首项（GPU 温度已在大卡聚光灯展示，避免重复）
        private void OnMetricsFilter(object sender, FilterEventArgs e)
        {
            var vm = DataContext as OverviewViewModel;
            var item = e.Item as MetricViewModel;
            e.Accepted = vm == null || item == null || vm.Metrics.IndexOf(item) != 0;
        }
    }
}
```

- [ ] **Step 3: 构建 + 自测**

Run: `cmd.exe //c build-wpf.cmd` 然后 `cmd.exe //c "dev.cmd test"`
Expected: `WPF Build OK`；`TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`。

- [ ] **Step 4: Commit**

```bash
git add wpf/Views/OverviewView.xaml wpf/Views/OverviewView.xaml.cs
git commit -m "feat: 概览页 Bento 重构——Hero(CaelusCore+READY 徽章)+GPU 大数字卡+三指标+活动/硬件"
```

---

### Task 12: 示例案例视觉验证（--wpf-shot 矩阵）

**Files:**
- 无代码改动；产物：`%TEMP%/aurora-shot/*.png` + 提交一张代表性截图到 `docs/`

**背景**：`App.xaml.cs` 内置 `--wpf-shot <dir>` 离屏渲染探针（模式×主题矩阵 PNG，`Motion.Enabled=false` 时渲染最终视觉态，漂移/脉冲/旋转不影响截图）。

- [ ] **Step 1: 离屏渲染**

Run: `cmd.exe //c "wpf\bin\Release\CaelusWpf.exe --wpf-shot %TEMP%\aurora-shot"`（Git Bash 中用 `$TEMP` 或显式路径：`cmd.exe //c "wpf\bin\Release\CaelusWpf.exe --wpf-shot C:\Users\Administrator\AppData\Local\Temp\aurora-shot"`）
Expected: 退出码 0；目录下生成模式×主题矩阵 PNG。

- [ ] **Step 2: 视觉检查**

用 Read 工具查看概览页深色截图，对照基准稿 `.superpowers/brainstorm/1874-1786395437/content/aurora-v3-motion-brand.html` 检查：
- 三层光晕可见（右上紫/蓝、左下青）且边缘无硬裁剪
- Hero 卡：READY 徽章、结论文字、CaelusCore（双环+闪电+模式名）齐全
- GPU 卡：48px 大数字 + 趋势线 + 面积淡填充
- 渐变描边（上亮下隐）与顶部高光线可见
- 三指标卡 + 活动/硬件卡排布整齐，右缘对齐

发现偏差则回到对应任务调整色值/布局（只允许改 Task 2/3/10/11 的产物），然后重跑 Step 1。

- [ ] **Step 3: 留档 + Commit**

```bash
cp "$TEMP/aurora-shot/<概览深色常规>.png" docs/aurora-overview-v2.png
git add docs/aurora-overview-v2.png
git commit -m "test: Aurora Bento 概览页视觉验证留档（--wpf-shot）"
```

---

## M4 · 全局换肤

### Task 13: MainWindow 外壳重构——图标导航 + 分组 + 瘦页头

**Files:**
- Modify: `wpf/MainWindow.xaml`（整体替换）

**设计要点**：导航项 Content 改为 `StackPanel(IconView + TextBlock)`（IconView 的 Foreground 随 RadioButton 继承，选中变色自动生效）；分两组（总览 / 硬件与系统）+ 底部固定（设置/关于）；页头只留模式分段控件（页面标题副标题由 Task 14 下沉到各视图）；`x:Name` 全部保留，code-behind 零改动。

- [ ] **Step 1: 整体替换 `wpf/MainWindow.xaml`**

```xml
<Window x:Class="CaelusApp.WpfHost.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:CaelusApp.WpfHost.Views"
        xmlns:controls="clr-namespace:CaelusApp.WpfHost.Controls"
        Title="Caelus" Width="1196" Height="768"
        WindowStartupLocation="CenterScreen"
        WindowStyle="None" AllowsTransparency="False" ResizeMode="CanResize"
        Background="{DynamicResource BackgroundBrush}"
        FontFamily="{DynamicResource FontUi}">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="38"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <!-- 环境光层：z 序最底层，跨越两行 -->
    <controls:AmbientLayer x:Name="Ambient" Grid.RowSpan="2"/>

    <!-- 标题栏（字距用空格手工近似——WPF 无字距 API，规格 §5） -->
    <Border Grid.Row="0" Background="{DynamicResource SurfaceBrush}"
            BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="0,0,0,1"
            MouseLeftButtonDown="TitleBarDrag">
      <DockPanel LastChildFill="False">
        <TextBlock Text="C A E L U S" VerticalAlignment="Center" Margin="14,0,0,0"
                   Foreground="{DynamicResource TextTertiaryBrush}" FontSize="10"/>
        <Button x:Name="CloseBtn" DockPanel.Dock="Right" Content="X" Width="46"
                AutomationProperties.Name="关闭窗口"
                Click="CloseClick" Background="Transparent" BorderThickness="0"
                Foreground="{DynamicResource TextSecondaryBrush}"/>
        <Button x:Name="MinBtn" DockPanel.Dock="Right" Content="_" Width="40"
                AutomationProperties.Name="最小化窗口"
                Click="MinClick" Background="Transparent" BorderThickness="0"
                Foreground="{DynamicResource TextSecondaryBrush}"/>
      </DockPanel>
    </Border>

    <Grid Grid.Row="1">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="200"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>

      <!-- NavRail：图标 + 分组 + 底部固定 -->
      <Border Grid.Column="0" Background="Transparent"
              BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="0,0,1,0">
        <DockPanel>
          <!-- 底部固定：设置 / 关于 -->
          <StackPanel DockPanel.Dock="Bottom" Margin="0,0,0,10">
            <RadioButton x:Name="NavSettings" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：设置">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconSettings" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="设置" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
            <RadioButton x:Name="NavAbout" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：关于">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconInfo" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="关于" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
          </StackPanel>

          <StackPanel Margin="0,14,0,0">
            <TextBlock Text="caelus" FontWeight="Bold" FontSize="18" Margin="18,0,0,16"
                       Foreground="{DynamicResource TextPrimaryBrush}"/>

            <TextBlock Text="总览" Style="{DynamicResource NavGroupLabel}"/>
            <RadioButton x:Name="NavOverview" Style="{DynamicResource NavItem}" IsChecked="True" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：概览">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconOverview" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="概览" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
            <RadioButton x:Name="NavLibrary" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：游戏库">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconLibrary" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="游戏库" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
            <RadioButton x:Name="NavPolicy" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：优化策略">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconPolicy" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="优化策略" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
            <RadioButton x:Name="NavAntiCheat" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：反作弊">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconShield" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="反作弊专项" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
            <RadioButton x:Name="NavWhitelist" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：白名单">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconWhitelist" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="白名单" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
            <RadioButton x:Name="NavLog" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：日志">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconLog" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="日志" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>

            <TextBlock Text="硬件与系统" Style="{DynamicResource NavGroupLabel}"/>
            <RadioButton x:Name="NavGraphics" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：显卡">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconGpu" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="显卡" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
            <RadioButton x:Name="NavEnvironment" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：系统环境">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconEnvironment" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="系统环境" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
            <RadioButton x:Name="NavAudit" Style="{DynamicResource NavItem}" GroupName="nav" Checked="NavChecked"
                         AutomationProperties.Name="导航：系统体检">
              <StackPanel Orientation="Horizontal">
                <controls:IconView Key="IconAudit" Width="15" Height="15" VerticalAlignment="Center"/>
                <TextBlock Text="系统体检" Margin="9,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </RadioButton>
          </StackPanel>
        </DockPanel>
      </Border>

      <!-- 内容区：瘦页头（仅模式分段）+ 页面宿主 -->
      <Grid Grid.Column="1" Margin="22,14,22,16">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Style="{DynamicResource SegmentHost}" HorizontalAlignment="Right"
                Margin="0,0,0,12">
          <StackPanel Orientation="Horizontal">
            <RadioButton x:Name="SegStandard" Style="{DynamicResource SegmentItem}" Content="常规"
                         IsChecked="True" GroupName="mode" Checked="ModeChecked"
                         AutomationProperties.Name="模式：常规"/>
            <RadioButton x:Name="SegCompetitive" Style="{DynamicResource SegmentItem}" Content="竞技" GroupName="mode"
                         Checked="ModeChecked" AutomationProperties.Name="模式：竞技"/>
            <RadioButton x:Name="SegCustom" Style="{DynamicResource SegmentItem}" Content="自定义" GroupName="mode"
                         Checked="ModeChecked" AutomationProperties.Name="模式：自定义"/>
          </StackPanel>
        </Border>

        <ContentControl x:Name="PageHost" Grid.Row="1"/>
      </Grid>
    </Grid>
  </Grid>
</Window>
```

- [ ] **Step 2: Styles.xaml 补 NavGroupLabel 样式**

`wpf/Themes/Styles.xaml` 的 Aurora Bento v2 区块内追加：

```xml
  <!-- 导航分组小标签 -->
  <Style x:Key="NavGroupLabel" TargetType="TextBlock">
    <Setter Property="FontSize" Value="9"/>
    <Setter Property="Foreground" Value="{DynamicResource TextTertiaryBrush}"/>
    <Setter Property="Margin" Value="18,14,0,6"/>
  </Style>
```

- [ ] **Step 3: 构建 + 自测**

Run: `cmd.exe //c build-wpf.cmd` 然后 `cmd.exe //c "dev.cmd test"`
Expected: `WPF Build OK`；`TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`（code-behind 的 `NavChecked`/`ModeChecked`/`ApplyPersistedMode` 依赖的 x:Name 全部保留）。

- [ ] **Step 4: Commit**

```bash
git add wpf/MainWindow.xaml wpf/Themes/Styles.xaml
git commit -m "feat: 外壳重构——图标导航+总览/硬件与系统分组+底部固定设置关于+瘦页头"
```

---

### Task 14: 页头下沉——10 个视图各加 SectionTitle 页头

**Files:**
- Modify: `wpf/Views/PolicyView.xaml`、`LibraryView.xaml`、`AntiCheatView.xaml`、`GraphicsView.xaml`、`EnvironmentView.xaml`、`AuditView.xaml`、`LogView.xaml`、`WhitelistView.xaml`、`SettingsView.xaml`、`AboutView.xaml`（各插入同一模式的页头块）

**设计要点（规格 §7 5a）**：概览页已在 Task 11 完成；其余 10 页在**根容器第一个子元素之前**插入统一页头块。若某视图的根容器是带行定义的 `Grid`，则插入到第一行（`Grid.Row="0"`）并把原首行内容下移一行——先读该文件确认结构再插入。

页头块模板（各视图仅文字不同）：

```xml
    <StackPanel Margin="0,0,0,12">
      <TextBlock Text="【标题】" Style="{DynamicResource SectionTitle}"/>
      <TextBlock Text="【副标题】" Style="{DynamicResource SectionSubtitle}" Margin="0,3,0,0"/>
    </StackPanel>
```

各视图文字：

| 视图 | 标题 | 副标题 |
|---|---|---|
| PolicyView | 优化策略 | 调度与压制的生效策略 |
| LibraryView | 游戏库 | 已纳管的游戏与进程证据 |
| AntiCheatView | 反作弊专项 | 反作弊兼容性守护 |
| GraphicsView | 显卡 | GPU 状态与调优 |
| EnvironmentView | 系统环境 | 影响游戏表现的系统项 |
| AuditView | 系统体检 | 一键检查系统健康度 |
| LogView | 日志 | 运行记录与诊断 |
| WhitelistView | 白名单 | 永不触碰的进程 |
| SettingsView | 设置 | 偏好与恢复 |
| AboutView | 关于 | 版本与致谢 |

- [ ] **Step 1: 逐视图插入页头块（10 个文件）**

按上表逐文件插入。注意：部分视图可能已有自己的标题 TextBlock（迁移期样式），保留页内容不动、只在最顶部加统一页头；若已有标题与新页头重复，删除旧标题 TextBlock。

- [ ] **Step 2: 构建 + 自测**

Run: `cmd.exe //c build-wpf.cmd` 然后 `cmd.exe //c "dev.cmd test"`
Expected: `WPF Build OK`；`TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`。

- [ ] **Step 3: Commit**

```bash
git add wpf/Views/
git commit -m "feat: 页头下沉——10 视图统一 SectionTitle + 副标题"
```

---

### Task 15: 控件换肤——NavItem / PrimaryButton / PolicyToggle / 分段控件 / GhostButton

**Files:**
- Modify: `wpf/Themes/Styles.xaml`（五个既有样式整体替换）

**设计要点（规格 §7 5a）**：样式 key 不变（所有引用点零改动）；选中/开态统一走 Accent 梯度资源。

- [ ] **Step 1: 替换 `NavItem` 样式**

```xml
  <!-- 导航项 v2：选中 = AccentSoft 底 + AccentEdge 描边 + Accent 文字（规格 §7） -->
  <Style x:Key="NavItem" TargetType="RadioButton">
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
    <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}"/>
    <Setter Property="Padding" Value="10,7"/>
    <Setter Property="Margin" Value="8,1"/>
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
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource AccentSoftBrush}"/>
              <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource AccentEdgeBrush}"/>
              <Setter Property="Foreground" Value="{DynamicResource AccentPrimaryBrush}"/>
              <Setter Property="FontWeight" Value="SemiBold"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource GlassNavBrush}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

- [ ] **Step 2: 替换 `PrimaryButton` / `GhostButton` 样式**

```xml
  <!-- 主按钮 v2：Accent 渐变填充（规格 §7），不发光 -->
  <Style x:Key="PrimaryButton" TargetType="Button">
    <Setter Property="Background" Value="{DynamicResource AccentGradientBrush}"/>
    <Setter Property="Foreground" Value="{DynamicResource OnAccentBrush}"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="12,7"/>
    <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
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

  <!-- 幽灵按钮 v2：Accent 文字 -->
  <Style x:Key="GhostButton" TargetType="Button">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{DynamicResource AccentPrimaryBrush}"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="6,4"/>
    <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
    <Setter Property="Cursor" Value="Hand"/>
  </Style>
```

- [ ] **Step 3: 替换分段控件（胶囊化 + 浅底深字选中，规格 §6）**

```xml
  <!-- 分段控件项 v2：选中 = SegSelected 浅底 + 深字（深色旗舰反差点） -->
  <Style x:Key="SegmentItem" TargetType="RadioButton">
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="Padding" Value="16,5"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="bd" CornerRadius="99"
                  Padding="{TemplateBinding Padding}"
                  Background="Transparent">
            <ContentPresenter HorizontalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource SegSelectedBrush}"/>
              <Setter Property="Foreground" Value="{DynamicResource SegSelectedTextBrush}"/>
              <Setter Property="FontWeight" Value="SemiBold"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 分段控件容器 v2：胶囊 -->
  <Style x:Key="SegmentHost" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource GlassNavBrush}"/>
    <Setter Property="CornerRadius" Value="99"/>
    <Setter Property="Padding" Value="3"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderSubtleBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
  </Style>
```

- [ ] **Step 4: 替换 `PolicyToggle` 开态轨道为 Accent 渐变**

`PolicyToggle` 样式模板触发器中：
`<Setter TargetName="track" Property="Background" Value="{DynamicResource SuccessBrush}"/>` →
`<Setter TargetName="track" Property="Background" Value="{DynamicResource AccentGradientBrush}"/>`

- [ ] **Step 5: 构建 + 自测**

Run: `cmd.exe //c build-wpf.cmd` 然后 `cmd.exe //c "dev.cmd test"`
Expected: `WPF Build OK`；`TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`。

- [ ] **Step 6: Commit**

```bash
git add wpf/Themes/Styles.xaml
git commit -m "feat: 控件换肤——导航 Accent 选中/主按钮渐变/分段胶囊浅底深字/开关渐变轨道"
```

---

## M5 · 逐页精修 + 全量验证

### Task 16: 交互卡片 GlassCard 化 + 对话框换肤

**Files:**
- Modify: `wpf/Views/LibraryView.xaml`、`PolicyView.xaml`、`AuditView.xaml`、`WhitelistView.xaml`（可交互卡片换 GlassCard + Lift）
- Modify: `wpf/Dialogs/AddGameDialogWpf.xaml`（按钮/输入框换肤）

**设计要点（规格 §7 5b）**：静态卡片已由 Task 10 Step 4 的描边升级自动受益；本任务只给**可交互卡片**（游戏库列表项、策略开关卡、体检项、白名单项）换 GlassCard 并挂悬停浮起。

- [ ] **Step 1: 四个视图的卡片机械化替换**

逐文件执行（先读文件确认实际结构）：
1. 根元素补充命名空间（如缺失）：`xmlns:controls="clr-namespace:CaelusApp.WpfHost.Controls"` 与 `xmlns:local="clr-namespace:CaelusApp.WpfHost"`
2. 列表/策略卡片：`<Border Style="{DynamicResource LibraryItemCard}" ...>` → `<controls:GlassCard Style="{DynamicResource GlassCard}" local:Motion.Lift="True" ...>`；对应闭合标签 `</Border>` → `</controls:GlassCard>`
   - LibraryView：`LibraryItemCard` 样式的卡片
   - PolicyView：`PolicyCard` 样式的卡片
   - AuditView / WhitelistView：使用 `CardBorder` 且整体可点的卡片
3. 纯展示卡片（信息行、说明块）保持 `<Border Style="{DynamicResource CardBorder}">` 不动

替换示例（PolicyView 一处）：

改前：
```xml
<Border Style="{DynamicResource PolicyCard}" MouseLeftButtonUp="...">
```
改后：
```xml
<controls:GlassCard Style="{DynamicResource GlassCard}" local:Motion.Lift="True" Padding="14,10" MouseLeftButtonUp="...">
```

注意 GlassCard 默认 `Padding="14"`，原 PolicyCard 为 `14,10`——替换时把原 Padding 显式带上，保持版式不变。

- [ ] **Step 2: AddGameDialogWpf 换肤**

`wpf/Dialogs/AddGameDialogWpf.xaml` 三处：
- `BtnAdd`（添加按钮）加 `Style="{DynamicResource PrimaryButton}"`，去掉 `FontWeight="Bold"`（样式已含 SemiBold）
- 「取消」按钮加 `Style="{DynamicResource GhostButton}"`，`Foreground` 改为 `{DynamicResource TextSecondaryBrush}`（Setter 覆盖样式默认 Accent 文字——取消不应是强调色）
- `TbFilter` 的 `Background="{DynamicResource GlassCardBrush}"` → `{DynamicResource Surface0Brush}`，`BorderBrush="{DynamicResource GlassBorderBrush}"` → `{DynamicResource BorderSubtleBrush}`

- [ ] **Step 3: 构建 + 自测**

Run: `cmd.exe //c build-wpf.cmd` 然后 `cmd.exe //c "dev.cmd test"`
Expected: `WPF Build OK`；`TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`。

- [ ] **Step 4: Commit**

```bash
git add wpf/Views/ wpf/Dialogs/
git commit -m "feat: 交互卡片 GlassCard 化（库/策略/体检/白名单悬停浮起）+ 添加游戏对话框换肤"
```

---

### Task 17: 全量验证 + Phase 5 验收报告

**Files:**
- Create: `docs/phase5-verification.md`

- [ ] **Step 1: 自测基线确认**

Run: `cmd.exe //c "dev.cmd test"`
Expected: `TOTAL 178 / PASS 175 / FAIL 0 / SKIP 3`。**FAIL > 0 即真回归，必须修复后才能继续。**

- [ ] **Step 2: 三模式离屏截图矩阵**

Run: `cmd.exe //c "wpf\bin\Release\CaelusWpf.exe --wpf-shot C:\Users\Administrator\AppData\Local\Temp\aurora-final"`
用 Read 工具检查：常规（青紫）/竞技（品红）/自定义（琥珀金）三套 Aurora + Accent 整体换肤正确；浅色主题仍可渲染（不精修但不破）。

- [ ] **Step 3: 实机 GUI 验证（11 页导航 + 交互）**

沿用 `docs/phase4-gui-test-report.md` 的方法（UIA Select 触发导航 + PrintWindow 截图，**不用真实鼠标点击**；注意 LOL 客户端可能遮挡；UIA 客户端缓存每步重取；BoundingRectangle 是物理像素勿再乘 DPI）。清单：
- 11 个导航页全部可达且渲染（每页一张 PrintWindow 截图）
- 模式分段切换：光晕/导航选中/按钮/Core 文字随模式换肤
- 概览页「查看详情」按钮展开/收起正常
- 添加游戏对话框打开渲染正常
- 异常观测点 `%TEMP%\CaelusWpf.crash.log` 无新增异常

- [ ] **Step 4: 性能抽查（动画全开空闲 60s）**

先启动 `wpf\bin\Release\CaelusWpf.exe` 停在概览页（漂移+脉冲+双环旋转全开），然后：

Run: `powershell -NoProfile -Command "$a=(Get-Process CaelusWpf).CPU; Start-Sleep -Seconds 60; $b=(Get-Process CaelusWpf).CPU; '{0:N2}s CPU / 60s wall' -f ($b-$a)"`
Expected: ≤ 1.2s CPU / 60s（≈2% 以内；纯 RenderTransform/Opacity 动画应接近 0）。超标则检查是否有布局动画混入（只允许 RenderTransform/Opacity）。

- [ ] **Step 5: 撰写验收报告并 Commit**

`docs/phase5-verification.md`：基线结果、截图矩阵结论、11 页导航清单勾选、交互清单勾选、性能数据、已知遗留（浅色主题精修/减弱动效开关/主题编辑 UI/实时遥测序列 → 归入既有遗留项清单）。

```bash
git add docs/phase5-verification.md
git commit -m "test: Phase 5 Aurora Bento 全量验收（11 页导航 + 三模式换肤 + 性能抽查）"
```

---

## 附：任务-里程碑对照

| 任务 | 里程碑 | 主要内容 |
|---|---|---|
| 1-5 | M1 | 契约校验器+自测、色板档 v2、模式档三预设、环境光层 v2、用户主题加载 |
| 6-9 | M2 | 图标体系、动效三件套、CaelusCore、Sparkline |
| 10-12 | M3 | Tokens/Styles v2、概览页 Bento 重构（示例案例）、视觉验证 |
| 13-15 | M4 | 外壳导航、页头下沉、控件换肤 |
| 16-17 | M5 | 逐页精修+对话框、全量验证+验收报告 |
