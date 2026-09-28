# 苹果动效升级 + 亮色抹茶棉花糖主题 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> 注意：本环境用户约束禁止 Agent 子代理，实际执行走 executing-plans 内联路径。

**Goal:** 双轨 UI/UX 优化——动效升级（弹簧三档/侧栏滑动指示器/数字滚动，双轨共享）+ 亮色主题抹茶棉花糖重做，暗色零改动。

**Architecture:** 依据 `docs/superpowers/specs/2026-09-29-matcha-marshmallow-uiux-design.md`。动效走 `Motion.cs`/`UiMotion.cs` 扩展；主题走资源字典 token 深化——模式档 accent 家族按既有 `ModeAccentBrush` 桥接范式做明暗分离（画刷迁色板槽、色值留模式槽加 OnLight/OnDark 键），控件模板与布局零改动。

**Tech Stack:** WPF on .NET Framework 4.x（C# 5 语法上限）、MSBuild 32 位构建、内置 selftest 门禁、design-sandbox/tokens.css 令牌镜像、shoot-all 截图矩阵。

## Global Constraints

- **C# 5 语法上限**：无字符串插值、无表达式体成员、无 null 条件运算符（规格 §7.4）
- **自测门禁**：`dev.cmd test` FAIL 0 才算过；Git Bash 里必须 `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
- **tests 免登记**：csproj 以 `..\tests\**\*.cs` 通配编入（CaelusSelfTest=true），新建测试文件无需登记
- **wpf csproj 无 glob**：新增 XAML 必须 `<Page Include="..." />` 逐文件登记，且在 `App.xaml` MergedDictionaries 登记合并
- **主题契约自测**：`ThemeContract.ToneKeys/ModeKeys` 是文本级键完整性校验（tests/SelfTests.ThemeContract.cs），键迁移必须同步改契约数组，否则 FAIL
- **令牌一致性自测**：`design-sandbox/tokens.css` 的 `[data-theme="light"]` `--hero-title-from/to` 与 Colors.Light.xaml 值级绑定（tests/SelfTests.DesignTokens.cs），改 HeroTitleBrush 必须同步 tokens.css
- **暗色零改动**：Colors.Dark.xaml 只允许「桥接迁键、值不变」的改动；Mode 文件 `Accent*Color`（暗色值）不动
- **Reduced 策略**：所有新动画在 `Motion.Reduced` 或 `Motion.Enabled=false` 时直出终值
- **探针路径**：截图探针（--wpf-shot / ApplySampleResult）下数字滚动直出终值，防矩阵截图拍到中间值
- **ModeChanged 静态事件**：订阅者必须 Unloaded 退订（ThemeManager.cs 既有约束）
- **绑定类成员必须是属性**（c67b14b 坑）：新增绑定类成员一律 get/set 属性
- **自测计数同步**：计数变化时三语 README + `plan/milestones/M12-构建打包与自测验证.md` 同步
- **提交节奏**：每 Task 一个 commit；批 1 完成 push 一次，批 2 完成 push 一次

## 规格偏差预告（实施完成后回写规格 §8）

1. §3.3 错落入场：12/13 页已存在（LibraryView.xaml.cs:75-78 等），本次仅补 ActivityView
2. §3.4 NumberRoll 落点仅体检 Score（概览 Hero 为 GrantedTitle 字符串大字，滚动需 VM 改造，YAGNI 移出）
3. §4.1 `TextOnAccentColor` 不新增：复用既有 `OnAccentBrush` 桥接（`OnAccentOnLightColor`）
4. §4.1 `BrandGradientBrush` 不新增：渐变直接落在桥接后的色板槽 `AccentPrimaryBrush`
5. §4.2「只动一行」修正为 accent 家族明暗桥接：`AccentPrimaryBrush` 等 6 画刷定义在模式槽（明暗共享），亮色独立换肤必须先把画刷迁色板槽；竞技/自定义 OnLight 值=现值，视觉不变
6. §5.2 体检达标文案：新增 `IsExcellent` 触发位（插画+一行 caption），`HealthLabel`（优秀/良好/需优化）不动

---

# 批 1：动效升级（双轨共享）

### Task 1: UiMotion 弹簧三档预设 + 映射自测

**Files:**
- Modify: `src/UiShared/UiMotion.cs`（类内追加，见下）
- Create: `tests/SelfTests.Motion.cs`
- Modify: `tests/SelfTests.cs:711`（注册行）

**Interfaces:**
- Produces: `UiMotion.SpringPreset { Gentle, Snappy, Bouncy }`；`UiMotion.SpringParams(SpringPreset preset, out int milliseconds, out double amplitude)`——Task 2 的 `Motion.Spring` 与全部弹簧调用方消费

- [ ] **Step 1: 写失败自测**

创建 `tests/SelfTests.Motion.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 动效纯逻辑自测：弹簧预设映射 / 插值边界 / NumberRoll 禁用直出（规格 2026-09-29 §3）

using System;
using System.Windows.Controls;
using CaelusApp.WpfHost;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestSpringPresetMap()
        {
            int ms; double amp;
            UiMotion.SpringParams(UiMotion.SpringPreset.Gentle, out ms, out amp);
            Eq(300, ms); Eq(0.3, amp);
            UiMotion.SpringParams(UiMotion.SpringPreset.Snappy, out ms, out amp);
            Eq(240, ms); Eq(0.45, amp);
            UiMotion.SpringParams(UiMotion.SpringPreset.Bouncy, out ms, out amp);
            Eq(180, ms); Eq(0.5, amp);
        }
    }
}
```

在 `tests/SelfTests.cs` 的 `test("版本说明弹窗：行成员必须是属性（WPF 绑定不支持字段）", TestReleaseNoteRowBindable);` 行后追加：

```csharp
            test("动效：弹簧三档预设映射（规格 2026-09-29 §3.1）", TestSpringPresetMap);
```

- [ ] **Step 2: 跑自测确认编译失败**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: 编译错误 CS0117/CS0103（`UiMotion` 不含 `SpringPreset`/`SpringParams`）——证明测试在真跑

- [ ] **Step 3: 实现弹簧预设**

`src/UiShared/UiMotion.cs`：在 `UiMotion` **类内**追加嵌套枚举与方法（嵌套枚举才能以 `UiMotion.SpringPreset` 被引用，static class 允许含嵌套类型）：

```csharp
        // 弹簧三档预设（规格 2026-09-29 §3.1）：net4 无原生 SpringAnimation，以 BackEase 参数表近似
        public enum SpringPreset { Gentle, Snappy, Bouncy }

        // Gentle=页面/卡片入场 300ms/0.3；Snappy=指示器/开关 240ms/0.45；Bouncy=按压释放 180ms/0.5
        public static void SpringParams(SpringPreset preset, out int milliseconds, out double amplitude)
        {
            switch (preset)
            {
                case SpringPreset.Snappy: milliseconds = 240; amplitude = 0.45; return;
                case SpringPreset.Bouncy: milliseconds = 180; amplitude = 0.5; return;
                default: milliseconds = 300; amplitude = 0.3; return; // Gentle
            }
        }
```

- [ ] **Step 4: 跑自测确认通过**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 291` 且 `FAIL 0`（290+1）

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/UiMotion.cs tests/SelfTests.Motion.cs tests/SelfTests.cs
git commit -m "feat(motion): 弹簧三档预设 SpringParams（Gentle/Snappy/Bouncy）+ 映射自测（290→291）"
```

### Task 2: Motion.Spring 统一入口 + 既有弹簧收口

**Files:**
- Modify: `wpf/Motion.cs`（新增 `Spring`；改 `BuildSpringAnimation`/`BuildPressSpring`/`AnimateSpringDelayed`/`RiseIn` 内部）

**Interfaces:**
- Consumes: `UiMotion.SpringParams` / `UiMotion.SpringPreset`（Task 1）
- Produces: `Motion.Spring(Animatable target, DependencyProperty property, double from, double to, UiMotion.SpringPreset preset)`——Task 5 侧栏指示器消费

- [ ] **Step 1: 新增 Motion.Spring 公共入口**

`wpf/Motion.cs` 类内追加（`Emphasize` 方法之后即可）：

```csharp
        // 弹簧统一入口（规格 2026-09-29 §3.1）：三档预设参数化；opacity 勿走弹簧（过冲被 clamp 无意义）
        public static void Spring(Animatable target, DependencyProperty property,
            double from, double to, UiMotion.SpringPreset preset)
        {
            int ms; double amp;
            UiMotion.SpringParams(preset, out ms, out amp);
            if (!Enabled || Reduced || ms <= 0)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
                return;
            }
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new BackEase { Amplitude = amp, EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += delegate
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
            };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
```

- [ ] **Step 2: 收口既有弹簧实现（行为对照）**

三处改造，全部走 `SpringParams` 取参，消除散落的魔法数字：

1. `BuildPressSpring`（现 `:412-419`）：时长/振幅改取 `SpringParams(Bouncy)`——参数值相同（180/0.5），**行为不变**：

```csharp
        // 棉花糖按压回弹：Bouncy 档（180ms/0.5），松手时「啵」地弹回
        private static DoubleAnimation BuildPressSpring(double from, double to)
        {
            int ms; double amp;
            UiMotion.SpringParams(UiMotion.SpringPreset.Bouncy, out ms, out amp);
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new BackEase { Amplitude = amp, EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
        }
```

2. `BuildSpringAnimation`（现 `:545-552`）：振幅改取 `SpringParams(Gentle)` 的 0.3，时长保留形参（由调用方定）：

```csharp
        // 弹性回弹变体：仅用于入场位移（Gentle 振幅 0.3）；时长由调用方指定
        private static DoubleAnimation BuildSpringAnimation(double from, double to, int milliseconds)
        {
            int presetMs; double amp;
            UiMotion.SpringParams(UiMotion.SpringPreset.Gentle, out presetMs, out amp);
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new BackEase { Amplitude = amp, EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
        }
```

3. `RiseIn`（现 `:151`）：`int ms = UiMotion.PageFadeMs;` 保持（opacity 180ms 不变）；位移弹簧时长升级为 Gentle 300ms——把 `:167` 的 `AnimateSpringDelayed(translate, TranslateTransform.YProperty, 10, 0, ms, delayMs);` 改为：

```csharp
            int springMs; double springAmp;
            UiMotion.SpringParams(UiMotion.SpringPreset.Gentle, out springMs, out springAmp);
            AnimateSpringDelayed(translate, TranslateTransform.YProperty, 10, 0, springMs, delayMs);
```

（这是唯一有意的行为变更：入场位移 180→300ms，弹簧尾更长、更接近 iOS  settle；规格 §3.1 Gentle 档定义。）

- [ ] **Step 3: 跑自测确认无回归**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 291` / `FAIL 0`

- [ ] **Step 4: 构建正式版并人工抽查动效**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd"`
Expected: 构建成功并启动 Caelus.dev.exe；人工：切页入场有弹簧手感、按钮按压回弹与之前一致

- [ ] **Step 5: Commit**

```bash
git add wpf/Motion.cs
git commit -m "refactor(motion): Spring 统一入口 + 既有弹簧收口 SpringParams（RiseIn 位移升级 Gentle 300ms）"
```

### Task 3: NumberRoll 数字滚动 + 插值自测

**Files:**
- Modify: `wpf/Motion.cs`（追加 `NumberRoll`/`Interpolate` 与 4 个附加属性）
- Modify: `tests/SelfTests.Motion.cs`（+2 自测）
- Modify: `tests/SelfTests.cs`（+2 注册行）

**Interfaces:**
- Produces: `Motion.NumberRoll(TextBlock target, double from, double to, string format)`；`Motion.Interpolate(double from, double to, double t)`——Task 4 体检分数滚动消费

- [ ] **Step 1: 写失败自测**

`tests/SelfTests.Motion.cs` 类内追加：

```csharp
        private static void TestMotionInterpolate()
        {
            Eq(10d, Motion.Interpolate(10, 20, 0));
            Eq(20d, Motion.Interpolate(10, 20, 1));
            Eq(15d, Motion.Interpolate(10, 20, 0.5));
        }

        private static void TestNumberRollDisabledSetsFinal()
        {
            var tb = new TextBlock();
            bool prev = Motion.Enabled;
            try
            {
                Motion.Enabled = false;
                Motion.NumberRoll(tb, 0, 87, "0");
                Eq("87", tb.Text);
            }
            finally { Motion.Enabled = prev; }
        }
```

`tests/SelfTests.cs` 在 Task 1 注册行后追加：

```csharp
            test("动效：数字滚动插值边界", TestMotionInterpolate);
            test("动效：NumberRoll 禁用时直出终值", TestNumberRollDisabledSetsFinal);
```

- [ ] **Step 2: 跑自测确认编译失败**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: 编译错误（`Motion` 不含 `Interpolate`/`NumberRoll`）

- [ ] **Step 3: 实现 NumberRoll**

`wpf/Motion.cs` 类内追加：

```csharp
        // 数字滚动（规格 2026-09-29 §3.4）：220ms QuinticEase 驱动 0→1 进度附加属性，
        // 帧回调里 Interpolate 插值并格式化落文本；Reduced/禁用直出终值
        public static double Interpolate(double from, double to, double t)
        {
            return from + (to - from) * t;
        }

        private static readonly DependencyProperty RollFromProperty = DependencyProperty.RegisterAttached(
            "RollFrom", typeof(double), typeof(Motion), new PropertyMetadata(0d));
        private static readonly DependencyProperty RollToProperty = DependencyProperty.RegisterAttached(
            "RollTo", typeof(double), typeof(Motion), new PropertyMetadata(0d));
        private static readonly DependencyProperty RollFormatProperty = DependencyProperty.RegisterAttached(
            "RollFormat", typeof(string), typeof(Motion), new PropertyMetadata(null));
        private static readonly DependencyProperty RollProgressProperty = DependencyProperty.RegisterAttached(
            "RollProgress", typeof(double), typeof(Motion), new PropertyMetadata(0d, OnRollProgress));

        public static void NumberRoll(TextBlock target, double from, double to, string format)
        {
            if (target == null) return;
            if (!Enabled || Reduced)
            {
                target.Text = string.Format(format, to);
                return;
            }
            target.SetValue(RollFromProperty, from);
            target.SetValue(RollToProperty, to);
            target.SetValue(RollFormatProperty, format);
            target.SetValue(RollProgressProperty, 0d);
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(UiMotion.NumberRollMs))
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += delegate
            {
                target.BeginAnimation(RollProgressProperty, null);
                target.Text = string.Format(format, to);
            };
            target.BeginAnimation(RollProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void OnRollProgress(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            TextBlock target = d as TextBlock;
            if (target == null) return;
            string format = (string)target.GetValue(RollFormatProperty);
            if (format == null) return;
            double from = (double)target.GetValue(RollFromProperty);
            double to = (double)target.GetValue(RollToProperty);
            target.Text = string.Format(format, Interpolate(from, to, (double)e.NewValue));
        }
```

- [ ] **Step 4: 跑自测确认通过**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 293` / `FAIL 0`（291+2）

- [ ] **Step 5: Commit**

```bash
git add wpf/Motion.cs tests/SelfTests.Motion.cs tests/SelfTests.cs
git commit -m "feat(motion): NumberRoll 数字滚动 + Interpolate 纯函数 + 边界自测（291→293）"
```

### Task 4: 体检分数数字滚动接线

**Files:**
- Modify: `wpf/Views/AuditView.xaml:212`（Score TextBlock 去绑定、加 x:Name）
- Modify: `wpf/Views/AuditView.xaml.cs`（`sampleMode` 标志 + `UpdateScoreText` + 两处接线）

**Interfaces:**
- Consumes: `Motion.NumberRoll` / `Motion.Interpolate`（Task 3）
- Produces: 无（终端接线任务）

- [ ] **Step 1: XAML 改命名元素**

`wpf/Views/AuditView.xaml:212`，把

```xml
              <TextBlock Text="{Binding Score, Mode=OneWay}" FontSize="{DynamicResource FontSizeScore}" FontWeight="SemiBold"
```

改为（去绑定加 x:Name，Text 由代码后置接管；其余属性行不动）：

```xml
              <TextBlock x:Name="TxtScore" FontSize="{DynamicResource FontSizeScore}" FontWeight="SemiBold"
```

- [ ] **Step 2: 代码后置接线**

`wpf/Views/AuditView.xaml.cs`：

1. 字段区追加：

```csharp
        // 探针样例路径（ApplySampleResult）：数字直出，防矩阵截图拍到滚动中间值（Global Constraints）
        private bool sampleMode;
```

2. `ApplySampleResult()` 方法体第一行（`AuditViewModel m = DataContext as AuditViewModel;` 之前）加：

```csharp
            sampleMode = true;
```

3. `OnVmPropertyChanged` 方法内追加一个分支（放在现有分支之后）：

```csharp
            if (e.PropertyName == "Score") UpdateScoreText();
```

4. 类内追加方法：

```csharp
        // 体检分数滚动（规格 2026-09-29 §3.4）：变化时从当前显示值滚到新值；探针/Reduced/禁用直出
        private void UpdateScoreText()
        {
            AuditViewModel m = DataContext as AuditViewModel;
            if (m == null || TxtScore == null) return;
            if (sampleMode || !Motion.Enabled || Motion.Reduced)
            {
                TxtScore.Text = m.Score.ToString();
                return;
            }
            int from;
            if (!int.TryParse(TxtScore.Text, out from)) from = 0;
            if (from == m.Score) { TxtScore.Text = m.Score.ToString(); return; }
            Motion.NumberRoll(TxtScore, from, m.Score, "0");
        }
```

5. `OnLoaded` 方法内（现有逻辑之后）加一行，保证首屏有值：

```csharp
            UpdateScoreText();
```

- [ ] **Step 3: 跑自测确认无回归**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 293` / `FAIL 0`

- [ ] **Step 4: 人工验证滚动**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd"`
启动后切到「系统体检」页跑一次体检，观察分数从旧值滚到新值（220ms）

- [ ] **Step 5: Commit**

```bash
git add wpf/Views/AuditView.xaml wpf/Views/AuditView.xaml.cs
git commit -m "feat(ui): 体检分数 NumberRoll 滚动（探针/Reduced 直出终值）"
```

### Task 5: 侧栏滑动指示器

**Files:**
- Modify: `wpf/MainWindow.xaml:78-80`（导航列 Border 内包 Grid + pill）与 `:105`（ScrollViewer 加名）
- Modify: `wpf/MainWindow.xaml.cs`（`SlideNavPill` + `currentNav` 字段 + 三处接线）

**Interfaces:**
- Consumes: `Motion.Spring` / `UiMotion.SpringPreset.Snappy`（Task 2）
- Produces: 无

- [ ] **Step 1: XAML 包 Grid + 放 pill**

`wpf/MainWindow.xaml:78-80`，现状：

```xml
      <Border Grid.Column="0" Background="{DynamicResource SurfaceBrush}"
              BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="0,0,1,0">
        <DockPanel LastChildFill="True">
```

改为（DockPanel 外层包 Grid，pill 声明在 DockPanel 之前=衬在导航项下层）：

```xml
      <Border Grid.Column="0" Background="{DynamicResource SurfaceBrush}"
              BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="0,0,1,0">
        <Grid x:Name="NavRail">
          <Border x:Name="NavPill" VerticalAlignment="Top" Height="0" Margin="12,0,12,0"
                  Background="{DynamicResource AccentSoftBrush}" CornerRadius="{DynamicResource RadiusSm}"
                  Visibility="Collapsed" IsHitTestVisible="False">
            <Border.RenderTransform><TranslateTransform Y="0"/></Border.RenderTransform>
          </Border>
          <DockPanel LastChildFill="True">
```

同时把该 DockPanel 对应的闭合标签 `</DockPanel>`（在 `</ScrollViewer>` 之后）之后补 `</Grid>`：

```xml
        </DockPanel>
        </Grid>
      </Border>
```

并把 `:105` 的 `<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">` 改为：

```xml
          <ScrollViewer x:Name="NavScroll" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
```

- [ ] **Step 2: 代码后置实现滑动**

`wpf/MainWindow.xaml.cs`：

1. 字段区追加：

```csharp
        // 侧栏滑动指示器（规格 2026-09-29 §3.2）：当前选中导航项，供滚动/尺寸变化时复位 pill
        private RadioButton currentNav;
```

2. 类内追加：

```csharp
        // iOS 分段控件式滑动指示：Snappy 弹簧（240ms/0.45）；首次/滚动/Reduced 直落位
        private void SlideNavPill(RadioButton item, bool animate)
        {
            if (NavPill == null || NavRail == null || item == null || !item.IsLoaded) return;
            Point origin = item.TransformToVisual(NavRail).Transform(new Point(0, 0));
            double y = origin.Y;
            if (double.IsNaN(y) || y < 0) { NavPill.Visibility = Visibility.Collapsed; return; }
            NavPill.Height = item.ActualHeight;
            NavPill.Visibility = Visibility.Visible;
            TranslateTransform slide = NavPill.RenderTransform as TranslateTransform;
            if (slide == null) { slide = new TranslateTransform(); NavPill.RenderTransform = slide; }
            if (!animate || !Motion.Enabled || Motion.Reduced)
            {
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                slide.Y = y;
                return;
            }
            Motion.Spring(slide, TranslateTransform.YProperty, slide.Y, y, UiMotion.SpringPreset.Snappy);
        }
```

3. `NavChecked` 方法在 `RadioButton rb = sender as RadioButton; if (rb == null ...) return;` 之后第一行加：

```csharp
            currentNav = rb;
            SlideNavPill(rb, true);
```

4. 构造/初始化接线：先 `Grep -n "Loaded +=" wpf/MainWindow.xaml.cs` 找到现有 Loaded 订阅处，在该处（或构造函数末尾）追加：

```csharp
            NavScroll.ScrollChanged += delegate { SlideNavPill(currentNav, false); };
            NavRail.SizeChanged += delegate { SlideNavPill(currentNav, false); };
            Loaded += delegate { currentNav = NavOverview; SlideNavPill(NavOverview, false); };
```

注意：这三个订阅都是窗口级元素事件（非 ThemeManager.ModeChanged 静态事件），随窗口析构自然释放，不违反 Global Constraints 的退订约束。

- [ ] **Step 3: 跑自测确认无回归**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 293` / `FAIL 0`

- [ ] **Step 4: 人工验证滑动手感**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd"`
逐一点击侧栏 14 个导航项（含底部设置/关于），pill 应以弹簧手感滑动跟随；滚动导航区 pill 不漂移。
**Fallback**：若 pill 与 NavItem 选中态底色叠加显脏，把 pill 改为 3px 左指示条变体（`Width="3"` `HorizontalAlignment="Left"` `Margin="6,0,0,0"`，其余逻辑不变）。

- [ ] **Step 5: Commit**

```bash
git add wpf/MainWindow.xaml wpf/MainWindow.xaml.cs
git commit -m "feat(ui): 侧栏滑动指示器——Snappy 弹簧跟随（iOS 分段控件手感）"
```

### Task 6: ActivityView 错落入场补齐 + 动效压测 + 批 1 推送

**Files:**
- Modify: `wpf/Views/ActivityView.xaml.cs`（OnLoaded 补 RiseIn）

**Interfaces:**
- Consumes: `Motion.RiseIn`（既有，Task 2 已收口）
- Produces: 无

- [ ] **Step 1: 补 RiseIn 接线**

`Grep -n "OnLoaded" wpf/Views/ActivityView.xaml.cs` 找到 OnLoaded，按 LibraryView.xaml.cs:75-78 的既有范式，在其内追加（ActivityView 只有 ZoneHeader/ZoneStatus 两个顶层区块，见 XAML）：

```csharp
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneStatus, 100);
```

- [ ] **Step 2: 跑自测确认无回归**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 293` / `FAIL 0`

- [ ] **Step 3: 动效压测**

Run: `MSYS_NO_PATHCONV=1 cmd /c "powershell -ExecutionPolicy Bypass -File scripts\wpf-motion-stress.ps1"`
Expected: 脚本跑完无异常退出；输出文本确认动画期间无错误（既有基线：无限动画 15fps 节流不变）

- [ ] **Step 4: Commit + 推送批 1**

```bash
git add wpf/Views/ActivityView.xaml.cs
git commit -m "feat(ui): ActivityView 补 RiseIn 错落入场（13 页全覆盖）"
git push origin main
```

---

# 批 2：亮色抹茶棉花糖主题

### Task 7: accent 家族明暗桥接（契约迁移 + 三模式档重组 + 双色板迁键，视觉不变）

**背景**：`AccentPrimaryBrush` 等 6 个画刷定义在模式槽（`Mode.*.xaml`，明暗共享，合并顺序晚于色板槽=同键必赢），亮色无法独立换肤。按既有 `ModeAccentBrush` 桥接范式（Colors.Light.xaml:101 用 `DynamicResource` 指模式档明暗分离色键）：**画刷迁色板槽、色值留模式槽并加 OnLight/OnDark 键**。本 Task 全部填现值，视觉零变化；抹茶值在 Task 8 落。

**Files:**
- Modify: `src/UiShared/ThemeContract.cs:30-41`（ModeKeys −6 画刷 +9 色键）与 `:13-27`（ToneKeys +6 画刷）
- Modify: `wpf/Themes/Mode.Standard.xaml` / `Mode.Competitive.xaml` / `Mode.Custom.xaml`（−6 画刷定义，+9 色键）
- Modify: `wpf/Themes/Colors.Dark.xaml` / `Colors.Light.xaml`（+6 画刷桥接定义）

**Interfaces:**
- Consumes: 无（自包含重构）
- Produces: 模式档 9 个新色键——Task 8 消费 `Accent*OnLightColor` 六键改抹茶值

- [ ] **Step 1: 确认 ModeKeys 消费方（用户主题校验风险排查）**

Run: `Grep -rn "ModeKeys" src/ wpf/ tests/ --include="*.cs"`
Expected: 仅 `ThemeContract.cs` 定义 + `tests/SelfTests.ThemeContract.cs` 三处校验。
**若发现用户主题（Caelus.theme.xaml）校验也消费 ModeKeys**：在 `ThemeContract` 增加 `UserThemeKeys`（=迁移前旧 ModeKeys 22 键快照），把该校验改为引用 `UserThemeKeys`，防止老用户主题因新增 9 键被判不完整。

- [ ] **Step 2: 迁移契约数组**

`src/UiShared/ThemeContract.cs`：

1. `ToneKeys` 数组末尾（`"ScenarioDevSoftBrush", "ScenarioDailySoftBrush",` 之后）追加：

```csharp
            "AccentPrimaryBrush", "AccentSecondaryBrush", "AccentGradientBrush",
            "AccentSoftBrush", "AccentEdgeBrush", "OnAccentBrush",
```

2. `ModeKeys` 数组：删除 `"AccentPrimaryBrush", "AccentSecondaryBrush", "AccentGradientBrush",` 与 `"AccentSoftBrush", "AccentEdgeBrush", "AccentGlowColor", "OnAccentBrush",` 两行中的 6 个画刷键（**保留** `"AccentGlowColor"`——它是色键不是画刷），改为：

```csharp
            "AccentPrimaryColor", "AccentSecondaryColor",
            "AccentGlowColor",
            "AccentPrimaryOnLightColor", "AccentSecondaryOnLightColor",
            "AccentSoftOnLightColor", "AccentEdgeOnLightColor",
            "AccentGlowOnLightColor", "OnAccentOnLightColor",
            "AccentSoftOnDarkColor", "AccentEdgeOnDarkColor", "OnAccentOnDarkColor",
```

（`TestThemeContractValidator` 正反样例仍用 `AccentGlowColor`，仍在 ModeKeys，无需改测试。）

- [ ] **Step 3: Mode.Standard.xaml 重组（另两个模式档照此范式）**

删除这 6 行画刷定义（现 `:37-44` 区域）：

```xml
  <SolidColorBrush x:Key="AccentPrimaryBrush" .../>
  <SolidColorBrush x:Key="AccentSecondaryBrush" .../>
  <LinearGradientBrush x:Key="AccentGradientBrush" ...>...</LinearGradientBrush>
  <SolidColorBrush x:Key="AccentSoftBrush" .../>
  <SolidColorBrush x:Key="AccentEdgeBrush" .../>
  <SolidColorBrush x:Key="OnAccentBrush" .../>
```

在 `AccentGlowColor` 行后追加 9 个色键（本 Task 全部填现值=视觉不变；注释标注 Task 8 落点）：

```xml
  <!-- 明暗桥接色键（规格 2026-09-29 §4.2 偏差修正）：画刷迁色板槽，此处只留色值。
       OnDark=现共享值；Standard 的 OnLight 六键由 Task 8 改抹茶值 -->
  <Color x:Key="AccentSoftOnDarkColor">#168B7CF6</Color>
  <Color x:Key="AccentEdgeOnDarkColor">#408B7CF6</Color>
  <Color x:Key="OnAccentOnDarkColor">#2B1F1A</Color>
  <Color x:Key="AccentPrimaryOnLightColor">#8B7CF6</Color>
  <Color x:Key="AccentSecondaryOnLightColor">#A78BFA</Color>
  <Color x:Key="AccentSoftOnLightColor">#168B7CF6</Color>
  <Color x:Key="AccentEdgeOnLightColor">#408B7CF6</Color>
  <Color x:Key="AccentGlowOnLightColor">#8B7CF6</Color>
  <Color x:Key="OnAccentOnLightColor">#2B1F1A</Color>
```

同时删除旧的 `ModeAccentSoftBrush`/`ModeAccentEdgeBrush` 兼容别名两行？——**不删**：它们不在本次契约迁移范围，保留防旧引用断裂（YAGNI 逆向：删除无收益）。

- [ ] **Step 4: Mode.Competitive.xaml / Mode.Custom.xaml 同步重组**

同样删 6 画刷、加 9 色键，**用各文件自己的现值平移**。先 `Grep -n "AccentPrimaryColor\|AccentSecondaryColor\|AccentSoftBrush\|AccentEdgeBrush\|AccentGlowColor\|OnAccentBrush" wpf/Themes/Mode.Competitive.xaml wpf/Themes/Mode.Custom.xaml` 取现值。预期值（以实读为准）：
- Competitive：Primary `#FF8A5C` / Secondary 实读 / Soft `#16FF8A5C` / Edge `#40FF8A5C` / Glow `#FF8A5C` / OnAccent `#2B1F1A`；OnLight 六键填相同现值
- Custom：Primary `#E6B84C` / Secondary 实读 / Soft `#16E6B84C` / Edge `#40E6B84C` / Glow `#E6B84C` / OnAccent `#2B1F1A`；OnLight 六键填相同现值

- [ ] **Step 5: 两个色板档加 6 画刷桥接**

`wpf/Themes/Colors.Dark.xaml` 末尾（`</ResourceDictionary>` 前）追加——全部 DynamicResource 指模式档 OnDark/现有色键，视觉与原定义逐一相等：

```xml
  <!-- accent 画刷桥接（规格 2026-09-29 §4.2 偏差修正）：暗色保持现值，键从模式槽迁入 -->
  <SolidColorBrush x:Key="AccentPrimaryBrush" Color="{DynamicResource AccentPrimaryColor}"/>
  <SolidColorBrush x:Key="AccentSecondaryBrush" Color="{DynamicResource AccentSecondaryColor}"/>
  <LinearGradientBrush x:Key="AccentGradientBrush" StartPoint="0,0" EndPoint="1,0">
    <GradientStop Color="{DynamicResource AccentPrimaryColor}" Offset="0"/>
    <GradientStop Color="{DynamicResource AccentSecondaryColor}" Offset="1"/>
  </LinearGradientBrush>
  <SolidColorBrush x:Key="AccentSoftBrush" Color="{DynamicResource AccentSoftOnDarkColor}"/>
  <SolidColorBrush x:Key="AccentEdgeBrush" Color="{DynamicResource AccentEdgeOnDarkColor}"/>
  <SolidColorBrush x:Key="OnAccentBrush" Color="{DynamicResource OnAccentOnDarkColor}"/>
```

`wpf/Themes/Colors.Light.xaml` 末尾追加——同构，指 OnLight 键（本 Task OnLight=现值，视觉不变）：

```xml
  <!-- accent 画刷桥接（规格 2026-09-29 §4.2 偏差修正）：亮色走 OnLight 键，Task 8 落抹茶 -->
  <SolidColorBrush x:Key="AccentPrimaryBrush" Color="{DynamicResource AccentPrimaryOnLightColor}"/>
  <SolidColorBrush x:Key="AccentSecondaryBrush" Color="{DynamicResource AccentSecondaryOnLightColor}"/>
  <LinearGradientBrush x:Key="AccentGradientBrush" StartPoint="0,0" EndPoint="1,0">
    <GradientStop Color="{DynamicResource AccentPrimaryOnLightColor}" Offset="0"/>
    <GradientStop Color="{DynamicResource AccentSecondaryOnLightColor}" Offset="1"/>
  </LinearGradientBrush>
  <SolidColorBrush x:Key="AccentSoftBrush" Color="{DynamicResource AccentSoftOnLightColor}"/>
  <SolidColorBrush x:Key="AccentEdgeBrush" Color="{DynamicResource AccentEdgeOnLightColor}"/>
  <SolidColorBrush x:Key="OnAccentBrush" Color="{DynamicResource OnAccentOnLightColor}"/>
```

- [ ] **Step 6: 跑自测确认契约与全量绿**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 293` / `FAIL 0`（主题契约三项 + 令牌一致性全过即桥接正确）

- [ ] **Step 7: 暗色视觉抽查（桥接零漂移证据）**

Run: `MSYS_NO_PATHCONV=1 cmd /c "powershell -ExecutionPolicy Bypass -File scripts\shoot-all.ps1"`
然后 `git status --short docs/shots`：76 张应**全部无差异**（本 Task 视觉不变；若有差异文件，打开排查是哪一档桥接值错了）
确认无差异后 `git checkout -- docs/shots` 还原（不把无变化重摄混进提交）。

- [ ] **Step 8: Commit**

```bash
git add src/UiShared/ThemeContract.cs wpf/Themes/Mode.Standard.xaml wpf/Themes/Mode.Competitive.xaml wpf/Themes/Mode.Custom.xaml wpf/Themes/Colors.Dark.xaml wpf/Themes/Colors.Light.xaml
git commit -m "refactor(theme): accent 家族明暗桥接——6 画刷迁色板槽 + 模式档 OnLight/OnDark 九键（视觉不变，契约同步）"
```

### Task 8: 抹茶色板落地（Colors.Light + Standard OnLight + tokens.css 同步）

**Files:**
- Modify: `wpf/Themes/Colors.Light.xaml`（§4.1 全表 + HeroTitleBrush 末 Stop + AccentPrimaryBrush 渐变化）
- Modify: `wpf/Themes/Mode.Standard.xaml`（OnLight 六键改抹茶值）
- Modify: `design-sandbox/tokens.css:138-139` 与 light 段 `--success`（令牌一致性自测强制项 + 卫生项）

**Interfaces:**
- Consumes: Task 7 的桥接键
- Produces: 亮色抹茶视觉（批 2 矩阵重摄在 Task 10）

- [ ] **Step 1: Colors.Light.xaml 色值替换**

按下表逐键替换（只改值，不动键名与结构）：

| 键 | 旧值 | 新值 |
|---|---|---|
| `BackgroundColor` | `#FFFBF4EE` | `#FFFAF8F1` |
| `Surface1Color` | `#FFFFF7F2` | `#FFFAF6EE` |
| `Surface2Color` | `#FFF5EDE7` | `#FFF3EFE4` |
| `TextPrimaryColor` | `#2B1F1A` | `#26332A` |
| `TextSecondaryColor` | `#6B5D55` | `#5E7064` |
| `TextTertiaryColor` | `#7F6E64` | `#66766A` |
| `BrandColor` | `#8B7CF6` | `#6FAF88` |
| `SuccessColor` | `#248A3D` | `#27755C` |
| `SuccessSoftBrush` | `#16248A3D` | `#1627755C` |
| `SuccessEdgeBrush` | `#4D248A3D` | `#4D27755C` |
| `SegSelectedTextBrush` | `#2B1F1A` | `#26332A` |
| `HeroTitleBrush` 末 Stop | `#6D5CE0` | `#3F7A58` |
| `ScenarioDevSoftBrush` | `#248A3D26` | `#27755C26` |

`Surface0Color`/`ScenarioDailyBrush`(#B84518)/`InfoColor`/`WarningColor`/`DangerColor`/滚动条灰/描边 alpha/兼容别名层：**不动**。

- [ ] **Step 2: 主按钮渐变化**

`Colors.Light.xaml` 中 Task 7 加的桥接行里，把亮色 `AccentPrimaryBrush` 从纯色改为渐变（135°，规格 §4.1——模板零改动，按钮/进度条自动渐变）：

```xml
  <LinearGradientBrush x:Key="AccentPrimaryBrush" StartPoint="0,0" EndPoint="1,1">
    <GradientStop Color="{DynamicResource AccentPrimaryOnLightColor}" Offset="0"/>
    <GradientStop Color="{DynamicResource AccentSecondaryOnLightColor}" Offset="1"/>
  </LinearGradientBrush>
```

- [ ] **Step 3: Mode.Standard.xaml OnLight 六键改抹茶**

把 Task 7 填的现值替换为（规格 §4.2 + WCAG 实测 §4.3）：

```xml
  <Color x:Key="AccentPrimaryOnLightColor">#7CB68F</Color>
  <Color x:Key="AccentSecondaryOnLightColor">#A8D5B5</Color>
  <Color x:Key="AccentSoftOnLightColor">#147CB68F</Color>
  <Color x:Key="AccentEdgeOnLightColor">#407CB68F</Color>
  <Color x:Key="AccentGlowOnLightColor">#7CB68F</Color>
  <Color x:Key="OnAccentOnLightColor">#1E3328</Color>
```

同时 `ModeAccentOnLightColor`（现 `#6D5CE0`，:51）改为 `#3F7A58`（规格 §4.2 原定的唯一一行变更，实测 5.08:1 全场景 AA）。
**Competitive/Custom 的 OnLight 六键保持 Task 7 的现值，不动**（规格 §2 不做清单）。

- [ ] **Step 4: tokens.css 同步（自测强制）**

`design-sandbox/tokens.css`：
- `:138` `--hero-title-from: #2B1F1A;` → `--hero-title-from: #26332A;      /* light: 墨绿（2026-09-29 抹茶） */`
- `:139` `--hero-title-to: #6D5CE0;` → `--hero-title-to: #3F7A58;        /* light: 深抹茶（2026-09-29 抹茶） */`
- light 段（:104 起）`--success: #248A3D;` → `--success: #27755C;`（卫生同步，非自测强制）
- dark 段一律不动

- [ ] **Step 5: 跑自测确认全量绿**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 293` / `FAIL 0`（`TestDesignTokenParity` 与主题契约三项必过；若 FAIL 按消息比对两侧值）

- [ ] **Step 6: 人工看亮色效果**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd"`，设置页切亮色模式：米白底 + 抹茶渐变按钮 + 墨绿文字；切常规/竞技/自定义三模式确认竞技=焙茶橙、自定义=金不变；切回暗色确认零变化。

- [ ] **Step 7: Commit**

```bash
git add wpf/Themes/Colors.Light.xaml wpf/Themes/Mode.Standard.xaml design-sandbox/tokens.css
git commit -m "feat(theme): 亮色抹茶棉花糖落地——色板/渐变按钮/Standard 强调色抹茶化 + tokens.css 同步（WCAG 实测全 AA）"
```

### Task 9: 空态插画 3 处 + 微文案

**Files:**
- Create: `wpf/Themes/Illustrations.xaml`（3 幅 Canvas 插画，`x:Shared="False"`）
- Modify: `wpf/Caelus.Wpf.csproj`（`<Page>` 登记）与 `wpf/App.xaml`（MergedDictionaries 登记）
- Modify: `wpf/Views/LibraryView.xaml:26`（空态插画 + 文案）、`wpf/Views/WhitelistView.xaml:58-61`（空态插画）
- Modify: `wpf/WhitelistViewModel.cs:31`（EmptyTitle 文案）
- Modify: `wpf/AuditViewModel.cs`（+`IsExcellent`）、`wpf/Views/AuditView.xaml`（ZoneHealth 达标插画 + caption）
- Modify: `src/Platform/Lang.cs`（`white.page.empty` 三语调味）

**Interfaces:**
- Consumes: 主题 token（`ModeAccentBrush`/`SuccessSoftBrush` 等 DynamicResource）
- Produces: `AuditViewModel.IsExcellent`（bool，Score≥85）

- [ ] **Step 1: 新建 Illustrations.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- 空态扁平插画（规格 2026-09-29 §5.1）：96 视窗，颜色全走 DynamicResource，双主题通用。
       x:Shared=False：每处引用独立实例，可安全进多个可视化树 -->

  <!-- 游戏库空态：棉花糖盒子 + 飘出的圆点 -->
  <Canvas x:Key="IllusLibrary" x:Shared="False" Width="96" Height="96">
    <Path Canvas.Left="18" Canvas.Top="34" Fill="{DynamicResource AccentSoftBrush}"
          Stroke="{DynamicResource AccentPrimaryBrush}" StrokeThickness="2.5"
          Data="M0,10 H60 A6,6 0 0 1 66,16 V52 A6,6 0 0 1 60,58 H6 A6,6 0 0 1 0,52 V16 A6,6 0 0 1 6,10 Z"/>
    <Path Canvas.Left="18" Canvas.Top="34" Stroke="{DynamicResource AccentPrimaryBrush}" StrokeThickness="2.5"
          StrokeStartLineCap="Round" Data="M0,22 H66"/>
    <Ellipse Canvas.Left="30" Canvas.Top="12" Width="12" Height="12" Fill="{DynamicResource AccentPrimaryBrush}" Opacity="0.85"/>
    <Ellipse Canvas.Left="50" Canvas.Top="4" Width="8" Height="8" Fill="{DynamicResource AccentSecondaryBrush}" Opacity="0.6"/>
    <Ellipse Canvas.Left="64" Canvas.Top="16" Width="6" Height="6" Fill="{DynamicResource AccentSecondaryBrush}" Opacity="0.45"/>
    <Path Canvas.Left="36" Canvas.Top="66" Stroke="{DynamicResource TextTertiaryBrush}" StrokeThickness="2.5"
          StrokeStartLineCap="Round" StrokeEndLineCap="Round" Data="M0,4 Q12,14 24,4"/>
  </Canvas>

  <!-- 白名单空态：圆盾 + 勾 -->
  <Canvas x:Key="IllusWhitelist" x:Shared="False" Width="96" Height="96">
    <Path Canvas.Left="24" Canvas.Top="10" Fill="{DynamicResource AccentSoftBrush}"
          Stroke="{DynamicResource AccentPrimaryBrush}" StrokeThickness="2.5"
          Data="M24,0 L44,8 V26 C44,40 36,50 24,56 C12,50 4,40 4,26 V8 Z"/>
    <Path Canvas.Left="24" Canvas.Top="10" Stroke="{DynamicResource AccentPrimaryBrush}" StrokeThickness="3"
          StrokeStartLineCap="Round" StrokeEndLineCap="Round" Data="M15,27 L22,34 L34,20"/>
    <Ellipse Canvas.Left="70" Canvas.Top="18" Width="8" Height="8" Fill="{DynamicResource AccentSecondaryBrush}" Opacity="0.5"/>
    <Ellipse Canvas.Left="16" Canvas.Top="70" Width="6" Height="6" Fill="{DynamicResource AccentSecondaryBrush}" Opacity="0.4"/>
  </Canvas>

  <!-- 体检达标：奖章 + 星星 -->
  <Canvas x:Key="IllusAuditWin" x:Shared="False" Width="96" Height="96">
    <Ellipse Canvas.Left="28" Canvas.Top="8" Width="40" Height="40" Fill="{DynamicResource AccentSoftBrush}"
             Stroke="{DynamicResource AccentPrimaryBrush}" StrokeThickness="2.5"/>
    <Path Canvas.Left="40" Canvas.Top="19" Fill="{DynamicResource AccentPrimaryBrush}"
          Data="M8,0 L10.5,5.5 L16,6 L12,10 L13,16 L8,13 L3,16 L4,10 L0,6 L5.5,5.5 Z"/>
    <Path Canvas.Left="36" Canvas.Top="46" Stroke="{DynamicResource AccentPrimaryBrush}" StrokeThickness="2.5"
          StrokeStartLineCap="Round" Data="M4,0 L0,18 M20,0 L24,18"/>
    <Ellipse Canvas.Left="14" Canvas.Top="30" Width="6" Height="6" Fill="{DynamicResource AccentSecondaryBrush}" Opacity="0.5"/>
    <Ellipse Canvas.Left="76" Canvas.Top="24" Width="8" Height="8" Fill="{DynamicResource AccentSecondaryBrush}" Opacity="0.4"/>
  </Canvas>
</ResourceDictionary>
```

- [ ] **Step 2: csproj + App.xaml 登记**

`wpf/Caelus.Wpf.csproj`：在 `<Page Include="Themes\Colors.Light.xaml" />`（或相邻 Theme Page 行）后加：

```xml
    <Page Include="Themes\Illustrations.xaml" />
```

`wpf/App.xaml`：`Grep -n "Colors.Light\|Themes/" wpf/App.xaml` 找到主题合并区，按既有行格式加：

```xml
        <ResourceDictionary Source="Themes/Illustrations.xaml"/>
```

- [ ] **Step 3: 三处落点接线**

1. `wpf/Views/LibraryView.xaml`：读 `:21-40` 空态区，把 `EmptyHeroIcon` 那个 48×48 Border 整体替换为（保留 x:Name 防代码后置引用断裂）：

```xml
          <ContentControl x:Name="EmptyHeroIcon" Content="{DynamicResource IllusLibrary}"
                          Width="96" Height="96" HorizontalAlignment="Center"/>
```

并把 `:35` 的「添加你的第一个游戏」改为「还没有游戏哦，拖个 exe 进来养一只」。

2. `wpf/Views/WhitelistView.xaml:58-61`：EmptyPanel 内、EmptyTitle TextBlock 之前插入：

```xml
                <ContentControl Content="{DynamicResource IllusWhitelist}" Width="96" Height="96"
                                HorizontalAlignment="Center" Margin="0,0,0,10"/>
```

3. `wpf/AuditViewModel.cs`：`HealthLabel` 属性后追加（C# 5 语法）：

```csharp
        // 达标态（规格 2026-09-29 §5.2）：插画 + caption 的触发位；HealthLabel 文案不动
        public bool IsExcellent { get { return Score >= 85; } }
```

并在 `:186` 的 `Raise("ConcernCount"); Raise("Score"); Raise("HealthLabel");` 后追加 `Raise("IsExcellent");`。

4. `wpf/Views/AuditView.xaml`：`Grep -n "ZoneHealth" wpf/Views/AuditView.xaml` 定位健康分区，在分数 TextBlock（TxtScore）所在容器内追加（DataTrigger 范式照 WhitelistView.xaml:37 的 IsEmpty 写法）：

```xml
              <StackPanel x:Name="ExcellentPanel" HorizontalAlignment="Center" Margin="0,10,0,0">
                <StackPanel.Style>
                  <Style TargetType="StackPanel">
                    <Setter Property="Visibility" Value="Collapsed"/>
                    <Style.Triggers>
                      <DataTrigger Binding="{Binding IsExcellent}" Value="True">
                        <Setter Property="Visibility" Value="Visible"/>
                      </DataTrigger>
                    </Style.Triggers>
                  </Style>
                </StackPanel.Style>
                <ContentControl Content="{DynamicResource IllusAuditWin}" Width="96" Height="96" HorizontalAlignment="Center"/>
                <TextBlock Text="状态满分，去尽情玩耍吧" FontSize="{DynamicResource FontSizeCaption}"
                           Foreground="{DynamicResource TextSecondaryBrush}" HorizontalAlignment="Center" Margin="0,4,0,0"/>
              </StackPanel>
```

- [ ] **Step 4: 微文案（Lang 三语 + ViewModel）**

1. `wpf/WhitelistViewModel.cs:31`：`public string EmptyTitle { get { return "CAELUS SHIELD"; } }` → `public string EmptyTitle { get { return "白名单空空的"; } }`
2. `Grep -n "white.page.empty" src/Platform/Lang.cs` 定位三语值，改为：
   - zh：`加一条规则试试，把要保护的进程放进来`
   - en：`Add a rule to protect the processes you care about`
   - ja：`ルールを追加して、守りたいプロセスを入れましょう`
   （只改值不改键，`TestEveryLangKeyIsDefined` 的引用-定义闭环不受影响）

- [ ] **Step 5: 跑自测确认全量绿**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd test"`
Expected: `TOTAL 293` / `FAIL 0`

- [ ] **Step 6: 人工看三处空态**

Run: `MSYS_NO_PATHCONV=1 cmd /c "dev.cmd"`：清空游戏库/白名单看空态插画与文案；体检满分样本看达标插画（可用 ApplySampleResult 探针路径验证，样例综合评估 82 分<85 不触发，需真跑满分或临时构造）。

- [ ] **Step 7: Commit**

```bash
git add wpf/Themes/Illustrations.xaml wpf/Caelus.Wpf.csproj wpf/App.xaml wpf/Views/LibraryView.xaml wpf/Views/WhitelistView.xaml wpf/Views/AuditView.xaml wpf/WhitelistViewModel.cs wpf/AuditViewModel.cs src/Platform/Lang.cs
git commit -m "feat(ui): 空态插画 3 处（游戏库/白名单/体检达标）+ 微文案软糯化（三语）"
```

### Task 10: 矩阵重摄 + 冒烟 + 规格偏差回写 + 计数同步 + 推送

**Files:**
- Regenerate: `docs/shots/*.png`（76 张）
- Modify: `docs/superpowers/specs/2026-09-29-matcha-marshmallow-uiux-design.md`（§8 追加偏差回写）
- Modify: `README.md` / `README.en.md` / `README.ja.md` / `plan/milestones/M12-构建打包与自测验证.md`（290→293）

**Interfaces:**
- Consumes: 批 1 + 批 2 全部产物
- Produces: 无（验收收尾任务）

- [ ] **Step 1: 重摄全矩阵**

Run: `MSYS_NO_PATHCONV=1 cmd /c "powershell -ExecutionPolicy Bypass -File scripts\shoot-all.ps1"`
Expected: 76 张 PNG 输出到 `docs/shots/`，脚本无异常

- [ ] **Step 2: 人工核对矩阵**

`git status --short docs/shots` 列出变化文件：
- **亮色 38 张**：必须变化——逐张或抽查关键页（概览/设置/游戏库空态/白名单空态/体检），确认米白底、抹茶渐变按钮、墨绿文字、插画呈现、竞技=橙/自定义=金不变
- **暗色 38 张**：应无变化；`git diff --stat docs/shots | grep -i dark` 若有暗色文件变化，打开逐张排查（只允许时间戳/日志内容类噪声），发现真漂移回 Task 7/8 排桥接值

- [ ] **Step 3: 真机冒烟**

Run: `MSYS_NO_PATHCONV=1 cmd /c "powershell -ExecutionPolicy Bypass -File scripts\app-smoke-test.ps1"`
Expected: 冒烟全过（与 2026-09-29 早些时候基线一致）

- [ ] **Step 4: 规格偏差回写**

在 `docs/superpowers/specs/2026-09-29-matcha-marshmallow-uiux-design.md` §8 末尾追加：

```markdown
- **2026-09-29 实施偏差回写**（随落地 commit 同步）：
  1. §3.3 错落入场 12/13 页已存在，仅补 ActivityView
  2. §3.4 NumberRoll 落点仅体检 Score（概览 Hero 为 GrantedTitle 字符串大字，滚动需 VM 改造，YAGNI 移出）
  3. §4.1 TextOnAccentColor 未新增，复用既有 OnAccentBrush 桥接（OnAccentOnLightColor #1E3328）
  4. §4.1 BrandGradientBrush 未新增，渐变直接落在色板槽 AccentPrimaryBrush（StartPoint 0,0 EndPoint 1,1）
  5. §4.2「只动一行」修正为 accent 家族明暗桥接：6 画刷迁色板槽 + 模式档 9 色键（竞技/自定义 OnLight=现值，视觉不变）；ModeAccentOnLightColor #6D5CE0→#3F7A58 按原案落地
  6. §5.2 体检达标走新增 IsExcellent 触发位（插画+caption），HealthLabel 不动
```

- [ ] **Step 5: 自测计数同步 290→293**

三语 README 与 M12 里程碑参照 c67b14b 的做法替换计数（zh 4 处、en 2 处、ja 2 处、M12 验收行 1 处）：
`Grep -n "290" README.md README.en.md README.ja.md plan/milestones/M12-构建打包与自测验证.md` 逐处改 293；M12 完成记录区追加一行：

```markdown
- 2026-09-29 双轨 UI/UX 优化（苹果动效+亮色抹茶棉花糖）：动效自测 +3（弹簧预设映射/插值边界/NumberRoll 禁用直出），自测基线 290 → 293 项，dev.cmd test 门禁 FAIL 0 维持
```

- [ ] **Step 6: Commit + 推送**

```bash
git add docs/shots docs/superpowers/specs/2026-09-29-matcha-marshmallow-uiux-design.md README.md README.en.md README.ja.md plan/milestones/M12-构建打包与自测验证.md
git commit -m "docs+shots: 双轨 UI/UX 验收——76 张矩阵重摄（亮色抹茶/暗色零漂移）+ 规格偏差回写 + 自测计数 293"
git push origin main
```
