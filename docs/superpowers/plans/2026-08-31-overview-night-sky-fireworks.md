# 概览页「夜空烟花」+ 改样式工作流强化 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 概览页换「夜空烟花」皮肤（骨架不动）并补齐改样式工作流三强化，全部经自测与截图矩阵验收。

**Architecture:** 沙盒先行——design-sandbox 里 CSS 定稿 → 映射回 wpf/Themes + OverviewView.xaml → 令牌对比自测长期防漂移 → --wpf-shot 截图矩阵验收。设计源头是 `docs/superpowers/specs/2026-08-31-overview-night-sky-fireworks-design.md`。

**Tech Stack:** C# / .NET Framework 4 / WPF 4 / HTML+CSS（沙盒）/ 自研 SelfTests（dev.cmd test 门禁）。

**关键既有资产（不要重造）：**
- `wpf/Views/OverviewView.xaml`：ZoneHero（场景仲裁卡，GrantedTitle 是结论标题）、ZoneCards（场景卡 UniformGrid×3，卡内 36×36 图标 chip）
- `wpf/Themes/Tokens.xaml`（FontSize*/Space*/Radius*）、`Colors.Dark/Light.xaml`（明暗轴色板+画刷）、`Mode.Standard/Competitive/Custom.xaml`（模式轴：AuroraPrimary/Secondary/TertiaryColor+Fade、Ambient*Brush、Accent*Brush）
- `src/UiShared/ThemeContract.cs`：ToneKeys/ModeKeys 键集契约自测（文本扫描）
- `wpf/App.xaml.cs`：`RunShot`（--wpf-shot <dir>，现出概览×4组合+其余12页深色常规）、`RunSingleShot`（--screenshot <png> <page>）
- 自测入口注册：`tests/SelfTests.cs` 的 `test("名称", 方法)`；断言 `Eq(a,b)`；SKip 用 `Skip("原因")`

---

### Task 1: 沙盒定稿——夜空烟花版概览页

**Files:**
- Modify: `design-sandbox/tokens.css`（新增 hero 令牌）
- Modify: `design-sandbox/overview.html`（应用夜空烟花版 Hero 与场景卡）

- [ ] **Step 1: tokens.css 新增令牌**

在 `:root,[data-theme="dark"]` 段（主色板后）与 `[data-theme="light"]` 段各加：

```css
  /* ---- 夜空烟花（2026-08-31 强化）---- */
  /* Hero 渐变标题（双色 Stop；XAML 侧是 LinearGradientBrush） */
  --hero-title-from: #F7F1EA;      /* dark: 奶油白 */
  --hero-title-to: #C8B0FA;        /* dark: 薰衣草紫 */
  /* 场景卡图标着色：游戏=模式主色、开发=绿、日常=蜜桃橙 */
  --scenario-dev: var(--success);
  --scenario-daily: #FFA07A;
  /* Hero 超大展示字号 */
  --font-size-showcase: 36px;
```
浅色段对应：`--hero-title-from: #2B1F1A; --hero-title-to: #6D5CE0; --scenario-daily: #B84518;`（dev 复用 --success）。

- [ ] **Step 2: overview.html 应用夜空烟花**

Hero 卡改：标题 `font-size:var(--font-size-showcase); font-weight:800;` + `background:linear-gradient(120deg,var(--hero-title-from) 30%,var(--hero-title-to)); -webkit-background-clip:text; background-clip:text; color:transparent;`；卡内叠加两个光晕层（右上 `radial-gradient(circle, var(--mode-accent) 55% 透明度, transparent 70%)`、左下用 `--aurora-2`/次色——与模式联动，因为 mode-accent 本就按 [data-mode] 换槽）。场景卡图标 chip：游戏卡保持 `--mode-accent`，开发卡 `color:var(--scenario-dev)`、日常卡 `color:var(--scenario-daily)`（背景换同色 20% 透明底）。

- [ ] **Step 3: 目检定稿**

浏览器打开 `design-sandbox/overview.html`，右下角工具条过一遍 深/浅 × 巡航/竞技/自定义 共 6 态，确认渐变可读、光晕不过曝（浅色版光晕透明度若过强，在浅色段把光晕再降 10%）。用手机截图或沙盒 shots 存档留底到 `design-sandbox/shots/`。

- [ ] **Step 4: Commit**

```bash
git add design-sandbox/tokens.css design-sandbox/overview.html design-sandbox/shots
git commit -m "feat(design): 沙盒定稿夜空烟花版概览——Hero 渐变标题+双模式光晕+场景卡图标着色"
```

---

### Task 2: 令牌对比自测（先守护既有令牌集，绿了再做 Task 3）

**Files:**
- Create: `tests/SelfTests.DesignTokens.cs`
- Modify: `tests/SelfTests.cs`（注册两行 test）

- [ ] **Step 1: 写对比测试**

新建 `tests/SelfTests.DesignTokens.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 设计沙盒 tokens.css 与 wpf/Themes 的令牌一致性自测：漂移即 FAIL

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static string ReadSandboxTokens()
        {
            // 自测 exe 在仓库根，沙盒在 design-sandbox/tokens.css
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "design-sandbox", "tokens.css");
            if (!File.Exists(path)) Skip("沙盒 tokens.css 不存在");
            return File.ReadAllText(path);
        }

        private static string ReadThemeFile(string name)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wpf", "Themes", name);
            if (!File.Exists(path)) throw new Exception("主题文件不存在: " + name);
            return File.ReadAllText(path);
        }

        // tokens.css 解析：取 某段（dark/light）里的 --name: value
        private static string CssVar(string css, string sectionSelector, string name)
        {
            int start = css.IndexOf(sectionSelector, StringComparison.Ordinal);
            if (start < 0) return null;
            int brace = css.IndexOf('{', start);
            int end = css.IndexOf('}', brace);
            if (brace < 0 || end < 0) return null;
            string body = css.Substring(brace + 1, end - brace - 1);
            var m = Regex.Match(body,
                Regex.Escape(name) + "\\s*:\\s*([^;]+);", RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static void TestDesignTokenParity()
        {
            string css = ReadSandboxTokens();
            string dark = ReadThemeFile("Colors.Dark.xaml");
            string light = ReadThemeFile("Colors.Light.xaml");
            string tokens = ReadThemeFile("Tokens.xaml");

            // 1) 主色板（dark 段 ↔ Colors.Dark.xaml）
            string cssBg = CssVar(css, "[data-theme=\"dark\"]", "--background");
            string xamlBg = ThemeContract.ExtractColorValue(dark, "BackgroundColor");
            Eq(true, xamlBg != null && xamlBg.EndsWith(cssBg.Substring(1),
                StringComparison.OrdinalIgnoreCase));

            // 2) 字号令牌：CSS px 值 == Tokens.xaml 数值
            string cssHero = CssVar(css, ":root", "--font-size-hero");
            Eq(true, tokens.Contains("x:Key=\"FontSizeHero\">" + cssHero.Replace("px", "") + "<"));

            // 3) 圆角签名：--radius-lg == RadiusLg
            string cssRad = CssVar(css, ":root", "--radius-lg");
            Eq(true, tokens.Contains("x:Key=\"RadiusLg\">" + cssRad.Replace("px", "") + "<"));
        }
    }
}
```

注意：CSS 段选择器 `:root,` 与 `[data-theme="dark"]` 在 tokens.css 里是同一行两个选择器（`:root,\n[data-theme="dark"] {`）——`CssVar` 的 IndexOf 用 `[data-theme="dark"]` 会命中同一规则块，正确。`:root` 段选择器用 `:root {`（注意空格）以防误命中 `:root,`。

- [ ] **Step 2: 注册测试**

`tests/SelfTests.cs` 在 ThemeContract 相关注册行附近加：

```csharp
            test("设计令牌：沙盒 tokens.css 与 wpf/Themes 主色板/字号/圆角一致", TestDesignTokenParity);
```

- [ ] **Step 3: 运行验证 GREEN**

Run: `cmd /c dev.cmd test`
Expected: TOTAL 254，本项 PASS（两侧现有令牌本应一致；若 FAIL 说明既有漂移，先修漂移再进 Task 3）

- [ ] **Step 4: Commit**

```bash
git add tests/SelfTests.DesignTokens.cs tests/SelfTests.cs
git commit -m "test: 设计令牌沙盒↔XAML 一致性自测（主色板/字号/圆角对比，漂移即 FAIL）"
```

---

### Task 3: XAML 翻译——夜空烟花落地

**Files:**
- Modify: `wpf/Themes/Tokens.xaml`（新增 FontSizeShowcase）
- Modify: `wpf/Themes/Colors.Dark.xaml`、`wpf/Themes/Colors.Light.xaml`（HeroTitleBrush + ScenarioDevBrush/ScenarioDailyBrush + Soft 变体）
- Modify: `wpf/Views/OverviewView.xaml`（Hero 渐变标题+双光晕层、场景卡图标着色）
- Modify: `src/UiShared/ThemeContract.cs`（ToneKeys 扩进新键）

- [ ] **Step 1: Tokens.xaml 加展示字号**

`FontSizeHero` 行后加：

```xml
  <!-- 夜空烟花：概览 Hero 超大展示标题 -->
  <sys:Double x:Key="FontSizeShowcase">36</sys:Double>
```

- [ ] **Step 2: 明暗色板加新画刷**

`Colors.Dark.xaml` 的 BrandBrush 附近加：

```xml
  <!-- 夜空烟花：Hero 渐变标题（奶油白 → 薰衣草紫） -->
  <LinearGradientBrush x:Key="HeroTitleBrush" StartPoint="0,0.5" EndPoint="1,0.5">
    <GradientStop Color="#F7F1EA" Offset="0.3"/>
    <GradientStop Color="#C8B0FA" Offset="1"/>
  </LinearGradientBrush>
  <!-- 场景卡图标着色：开发=成功绿、日常=蜜桃橙 -->
  <SolidColorBrush x:Key="ScenarioDevBrush" Color="#3DD68C"/>
  <SolidColorBrush x:Key="ScenarioDailyBrush" Color="#FFA07A"/>
  <SolidColorBrush x:Key="ScenarioDevSoftBrush" Color="#3DD68C33"/>
  <SolidColorBrush x:Key="ScenarioDailySoftBrush" Color="#FFA07A38"/>
```
`Colors.Light.xaml` 对应：`HeroTitleBrush` 停 #2B1F1A/#6D5CE0；`ScenarioDevBrush #248A3D`、`ScenarioDailyBrush #B84518`、Soft 用 #248A3D26/#B8451824。

- [ ] **Step 3: OverviewView.xaml Hero 夜空烟花化**

`ZoneHero` 的 `<Grid>` 内最底层插两个光晕（Grid.ColumnSpan="3" 盖住整卡）：

```xml
          <!-- 夜空烟花：双径向光晕随模式换槽（Aurora 色本就按模式轴变化） -->
          <Border Grid.ColumnSpan="3" IsHitTestVisible="False"
                  HorizontalAlignment="Right" VerticalAlignment="Top"
                  Width="360" Height="220" Margin="0,-40,-30,0">
            <Border.Background>
              <RadialGradientBrush RadiusX="0.8" RadiusY="0.8">
                <GradientStop Color="{DynamicResource AuroraPrimaryColor}" Offset="0"/>
                <GradientStop Color="{DynamicResource AuroraPrimaryFadeColor}" Offset="1"/>
              </RadialGradientBrush>
            </Border.Background>
          </Border>
          <Border Grid.ColumnSpan="3" IsHitTestVisible="False"
                  HorizontalAlignment="Left" VerticalAlignment="Bottom"
                  Width="280" Height="180" Margin="-30,0,0,-36">
            <Border.Background>
              <RadialGradientBrush RadiusX="0.8" RadiusY="0.8">
                <GradientStop Color="{DynamicResource AuroraTertiaryColor}" Offset="0"/>
                <GradientStop Color="{DynamicResource AuroraTertiaryFadeColor}" Offset="1"/>
              </RadialGradientBrush>
            </Border.Background>
          </Border>
```
（若 Aurora*FadeColor 是带透明度端点则光晕自然淡出；光晕过强时改用各 Mode.*.xaml 已有的 Aurora*Opacity 令牌值校验后再调。）

GrantedTitle 的 TextBlock 改：

```xml
            <TextBlock Text="{Binding GrantedTitle}"
                       FontSize="{DynamicResource FontSizeShowcase}"
                       FontWeight="ExtraBold"
                       Foreground="{DynamicResource HeroTitleBrush}"
                       TextWrapping="Wrap"
                       AutomationProperties.Name="{Binding GrantedTitle}"/>
```

场景卡图标 chip（ZoneCards 的 ItemTemplate 里 36×36 Border）：Background/BorderBrush/IconView.Foreground 从 Accent* 改为触发器——在 `Border` 上加 `Style.Triggers`（ScenarioCard 是 StaticResource Style，改静态 Style 加 DataTrigger 更干净，但卡片共用同一 Style；改为就地给 chip 的 `Border.Style` 新建带触发器的 Style 或在 chip Border 内联 `Style` 子元素）：

```xml
                  <Border DockPanel.Dock="Left" Width="36" Height="36" CornerRadius="{DynamicResource RadiusSm}"
                          Background="{DynamicResource AccentSoftBrush}"
                          BorderBrush="{DynamicResource AccentEdgeBrush}" BorderThickness="1"
                          VerticalAlignment="Top" Margin="0,0,10,0">
                    <Border.Style>
                      <Style TargetType="Border">
                        <Style.Triggers>
                          <DataTrigger Binding="{Binding IconKey}" Value="IconCode">
                            <Setter Property="Background" Value="{DynamicResource ScenarioDevSoftBrush}"/>
                          </DataTrigger>
                          <DataTrigger Binding="{Binding IconKey}" Value="IconDaily">
                            <Setter Property="Background" Value="{DynamicResource ScenarioDailySoftBrush}"/>
                          </DataTrigger>
                        </Style.Triggers>
                      </Style>
                    </Border.Style>
                    <controls:IconView Key="{Binding IconKey}" Width="18" Height="18"
                                       HorizontalAlignment="Center" VerticalAlignment="Center"
                                       Foreground="{DynamicResource AccentPrimaryBrush}">
                      <controls:IconView.Style>
                        <Style TargetType="controls:IconView">
                          <Style.Triggers>
                            <DataTrigger Binding="{Binding IconKey}" Value="IconCode">
                              <Setter Property="Foreground" Value="{DynamicResource ScenarioDevBrush}"/>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding IconKey}" Value="IconDaily">
                              <Setter Property="Foreground" Value="{DynamicResource ScenarioDailyBrush}"/>
                            </DataTrigger>
                          </Style.Triggers>
                        </Style>
                      </controls:IconView.Style>
                    </controls:IconView>
                  </Border>
```

- [ ] **Step 4: ThemeContract 扩键**

`src/UiShared/ThemeContract.cs` 的 `ToneKeys` 数组末尾（`"BrandBrush",` 后）加：

```csharp
            "HeroTitleBrush",
            "ScenarioDevBrush", "ScenarioDailyBrush",
            "ScenarioDevSoftBrush", "ScenarioDailySoftBrush",
```

- [ ] **Step 5: 令牌对比自测扩进新令牌**

`tests/SelfTests.DesignTokens.cs` 的 `TestDesignTokenParity` 末尾加：

```csharp
            // 4) 夜空烟花新令牌：沙盒与 XAML 双侧都存在
            Eq(true, CssVar(css, "[data-theme=\"dark\"]", "--hero-title-to") != null);
            Eq(true, dark.Contains("HeroTitleBrush"));
            Eq(true, CssVar(css, ":root {", "--font-size-showcase") != null
                && tokens.Contains("x:Key=\"FontSizeShowcase\">36<"));
```

- [ ] **Step 6: 构建 + 单页截图目检**

Run: `cmd /c build.cmd`（Expected: Build OK）
Run: `Caelus.exe --screenshot docs\shots\overview-nightsky-dark.png overview`（Expected: 出图，渐变标题与双光晕可见；`--screenshot` 默认深色巡航）
若渐变标题文字看不见（透明 bug）：检查 LinearGradientBrush 的 Stop 是否带 Alpha=00——不允许；回到 Step 2 修正。

- [ ] **Step 7: 全量自测**

Run: `cmd /c dev.cmd test`
Expected: 全绿（ThemeContract 新键两侧齐全、令牌对比通过）

- [ ] **Step 8: Commit**

```bash
git add wpf/Themes wpf/Views/OverviewView.xaml src/UiShared/ThemeContract.cs tests/SelfTests.DesignTokens.cs docs/shots
git commit -m "feat(ui): 概览页夜空烟花落地——Hero 超大渐变标题+Aurora 双光晕随模式换槽+场景卡图标按场景着色/FontSizeShowcase 与场景着色令牌入明暗双轴/ThemeContract 与令牌对比自测扩进新键"
```

---

### Task 4: 截图验收矩阵（13 页 × 明暗 × 三模式）

**Files:**
- Modify: `wpf/App.xaml.cs`（RunShot 扩展矩阵）
- Create: `scripts/shoot-all.ps1`

- [ ] **Step 1: RunShot 扩展为全矩阵**

`wpf/App.xaml.cs` 的 `RunShot` 方法：把"其余 12 页只出深色常规"的段改为循环矩阵。现段（`string[] pages = ...; for ... CapturePage(dir, pages[i]);`）替换为：

```csharp
                // 全页矩阵：全部页面 × 明暗 × 三模式（概览 4 组合已在上面出过，跳过重复）
                string[] pageList = new string[]
                {
                    "library", "policy", "graphics", "anticheat", "environment",
                    "whitelist", "audit", "log", "settings", "dev", "daily", "about"
                };
                var toneModes = new[]
                {
                    new { Tone = UiTone.Dark, Mode = AppMode.Standard, Tag = "dark-cruise" },
                    new { Tone = UiTone.Dark, Mode = AppMode.Competitive, Tag = "dark-combat" },
                    new { Tone = UiTone.Dark, Mode = AppMode.Custom, Tag = "dark-custom" },
                    new { Tone = UiTone.Light, Mode = AppMode.Standard, Tag = "light-cruise" },
                    new { Tone = UiTone.Light, Mode = AppMode.Competitive, Tag = "light-combat" },
                    new { Tone = UiTone.Light, Mode = AppMode.Custom, Tag = "light-custom" },
                };
                foreach (var tm in toneModes)
                {
                    ThemeManager.Apply(this, tm.Tone, tm.Mode);
                    foreach (string p in pageList) CapturePage(dir, p + "-" + tm.Tag);
                }
```
确认 `CapturePage(dir, name)` 现有签名是按名字导航+出图（看它当前实现，若内部用 RunSingleShot 同款流程则名字直接用于文件名；文件命名带 tag 即可）。若 CapturePage 内部固定了文件名规则，调整为文件名 `wpf-<name>.png`。

- [ ] **Step 2: scripts/shoot-all.ps1 一键脚本**

新建：

```powershell
# 一键全页截图验收：构建 dev 版 → --wpf-shot 全矩阵出图到 docs\shots\
param([string]$OutDir = "$PSScriptRoot\..\docs\shots")
$ErrorActionPreference = "Stop"
& "$PSScriptRoot\..\dev.cmd" build | Out-Null
$exe = "$PSScriptRoot\..\Caelus.dev.exe"
if (!(Test-Path $exe)) { $exe = "$PSScriptRoot\..\Caelus.exe" }
& $exe --wpf-shot $OutDir
if ($LASTEXITCODE -ne 0) { throw "截图探针失败" }
Write-Host "全矩阵截图已输出到 $OutDir"
```
（dev.cmd 若不支持 `build` 参数则直接 `build.cmd`；以仓库现有脚本参数为准微调。）

- [ ] **Step 3: 出图并目检**

Run: `powershell -File scripts\shoot-all.ps1`
Expected: `docs/shots/` 下出现 12 页 × 6 组合 + 概览 4 组合共 76 张 PNG；抽竞技橙与浅色巡航各一张目检光晕/渐变

- [ ] **Step 4: Commit**

```bash
git add wpf/App.xaml.cs scripts/shoot-all.ps1 docs/shots
git commit -m "feat(dev): --wpf-shot 扩展 13 页×明暗×三模式全矩阵 + scripts/shoot-all.ps1 一键验收"
```

---

### Task 5: 沙盒组件库补齐

**Files:**
- Modify: `design-sandbox/index.html`
- Modify: `design-sandbox/sandbox.css`（缺的状态类补类名，与 XAML Style 键同名注释）

- [ ] **Step 1: 补齐组件清单**

index.html 按 XAML `Styles.xaml`/`Icons.xaml` 的 Style 键逐一给出对应组件（每个含 默认/悬停/禁用 三态）：主按钮、次按钮、危险按钮、开关（PolicyToggle）、分段选择器（SegmentedControl）、玻璃卡（GlassCard）、组卡（SettingsGroup）、状态徽章（StatusBadge/NeutralStatusBadge）、场景卡（ScenarioCard）、进度环（ProgressRing）、危险确认对话框、输入框、滚动区。每节标题注明对应 Style 键名。

- [ ] **Step 2: 目检 + 存档**

浏览器过一遍 index.html 明暗×三模式；缺样式的组件在 sandbox.css 补齐。

- [ ] **Step 3: Commit**

```bash
git add design-sandbox/index.html design-sandbox/sandbox.css
git commit -m "feat(design): 沙盒组件库补齐——全核心组件×状态与 XAML Style 键一一对应"
```

---

### Task 6: README 截图换新 + 收尾

**Files:**
- Modify: `README.md`、`README.en.md`、`README.ja.md`（截图引用换新）
- Regenerate: `docs/overview-v15.png` 等被引用的截图（或改用新文件名并同步引用）

- [ ] **Step 1: 从 docs/shots 矩阵挑最终图**

概览页取 `wpf-overview-dark-cruise.png`（或矩阵里对应新图），按 README.md 现有 `<img src="docs/...">` 引用名逐张换新（保留文件名直接覆盖最省事：矩阵图复制覆盖 docs/overview-v15.png 等；其余页面本次未改版则截图不变可不动）。

- [ ] **Step 2: 最终验证**

Run: `cmd /c dev.cmd test`（Expected: 全绿）
Run: `cmd /c build.cmd`（Expected: Build OK）
Run: `cmd /c build-winforms.cmd`（Expected: Build OK——WinForms 回退宿主不参与本次视觉，但必须仍可构建）

- [ ] **Step 3: Commit**

```bash
git add README.md README.en.md README.ja.md docs/*.png
git commit -m "docs: README 概览截图换夜空烟花版"
```
````

## 自审记录

- 规格覆盖：视觉（Task 1/3）、令牌对比自测（Task 2/3-Step5）、截图矩阵（Task 4）、组件库（Task 5）、README 截图（Task 6）均有任务；骨架不动/其余页不动/动效不动与规格"明确不做"一致。
- 类型一致性：`FontSizeShowcase`/`HeroTitleBrush`/`ScenarioDevBrush` 等键名在 Task 2/3 与 ThemeContract 扩展间一致；`CapturePage(dir, name)` 依赖现 App.xaml.cs 实现，Step 1 已注明按现状微调。
- 占位扫描：无 TBD；Task 1 Step 3 的目检是有人参与的验收步，非占位。
