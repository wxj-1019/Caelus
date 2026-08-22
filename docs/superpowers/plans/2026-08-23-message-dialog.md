# 消息弹窗主题化（MessageDialogWpf）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用主题化无边框模态弹窗 `MessageDialogWpf` 替换 WPF 宿主全部 29 处原生 MessageBox（规格：`docs/superpowers/specs/2026-08-23-message-dialog-design.md`）。

**Architecture:** 纯逻辑（级别/按钮/文案拆分映射）独立成 `MsgSeverity.cs` 可自测；弹窗是 `WindowStyle=None` 模态小窗，静态 API 返回 `MessageBoxResult` 保证调用点机械替换；29 处按文件分四批替换，每批构建+自测+提交。

**Tech Stack:** .NET Framework 4.0 WPF（32 位 MSBuild）、C# 5 语法、仓库内建 selftest 框架（`dev.cmd test` 把 tests/ 编进 WPF exe 运行）。

---

## 环境硬约束（执行前必读，违反=返工）

1. **C# 5 语法上限**：禁止 `?.`、`$""` 插值、`out var`、`nameof`、表达式体成员、字典初始化器 `{ {k,v} }`。事件写 `var h = Ev; if (h != null) h(x);`。
2. **构建命令**（Git Bash，`.cmd` 必须经 `cmd //c` 双斜杠）：
   - WPF 构建：`cmd //c build-wpf.cmd` → 期望尾行 `WPF Build OK -> wpf\bin\Release\CaelusWpf.exe`
   - 全量自测：`cmd //c dev.cmd test` → 期望 `FAIL 0`（基线 0 失败；SKIP 为环境项非回归）。注意 dev.cmd 会先杀运行中的 Caelus 实例。
3. **DropShadowEffect 在本机完全不渲染**（已实测结论）——禁止使用，卡片层次靠边框+遮罩。
4. **无限动画必须 8fps 节流**：只用 `Motion.BreathPulse`（内部已 `Timeline.SetDesiredFrameRate` 节流），不要手写 `RepeatBehavior.Forever` 动画。
5. **csproj 是显式逐文件清单**（规格 §9 "免登记"表述有误）：wpf/ 下每个新增 .cs/.xaml 都必须补 `<Compile>`/`<Page>` 行；只有 `..\src\` 和 `..\tests\` 是递归 glob。
6. **WPF XAML `Path` 不支持 `StrokeLineCap`**（MC3072），如需线帽用 `StrokeStartLineCap`/`StrokeEndLineCap`；本计划图标走 `IconView` 控件无此问题。
7. 提交信息风格照仓库惯例（中文、`类型: 摘要——细节`）。
8. **owner 参数传 `Window`**（Task 2 审查修正）：视图（SettingsView 等）是 UserControl 不是 Window，直接传 `this` 会 CS1503——统一写 `Window.GetWindow(this)`（仓库既有惯例，如 SettingsView.xaml.cs:305）；AddGameDialogWpf 自身是 Window 同样写法成立；无窗口上下文（ViewModel/PolicyRuntime/托盘）传 null 或显式 Window 变量。

## 文件结构总览

| 文件 | 动作 | 职责 |
|---|---|---|
| `wpf/Dialogs/MsgSeverity.cs` | 新建 | 枚举 + 纯映射逻辑（可自测，无 UI 依赖） |
| `wpf/Dialogs/MessageDialogWpf.xaml(.cs)` | 新建 | 弹窗本体（无边框模态小窗） |
| `wpf/Caelus.Wpf.csproj` | 修改 | 登记上面三个文件 |
| `tests/SelfTests.MessageDialog.cs` | 新建 | 纯逻辑自测 |
| `tests/SelfTests.cs` | 修改 | Run() 注册 3 条新测试 |
| `wpf/Views/SettingsView.xaml.cs` 等 9 个文件 | 修改 | 29 处调用替换（Task 3-6） |
| `docs/superpowers/specs/2026-08-23-message-dialog-design.md` | 修改 | Task 7 回写执行偏差 |

---

### Task 1: 纯逻辑 MsgSeverity.cs + 自测（TDD）

**Files:**
- Create: `wpf/Dialogs/MsgSeverity.cs`
- Create: `tests/SelfTests.MessageDialog.cs`
- Modify: `tests/SelfTests.cs`（Run() 内注册）
- Modify: `wpf/Caelus.Wpf.csproj`（Compile 登记）

- [ ] **Step 1.1: 写失败测试**

新建 `tests/SelfTests.MessageDialog.cs`（tests/ 是 glob 编入，无需 csproj 登记）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 消息弹窗纯逻辑自测：级别映射 / 按钮组解析 / 标题正文拆分（规格 2026-08-23 §7）

using System.Windows;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestMsgDialogSeverityMaps()
        {
            Eq("IconInfo", MsgDialogMaps.IconKey(MsgSeverity.Info));
            Eq("IconCheck", MsgDialogMaps.IconKey(MsgSeverity.Success));
            Eq("IconWarn", MsgDialogMaps.IconKey(MsgSeverity.Warning));
            Eq("IconError", MsgDialogMaps.IconKey(MsgSeverity.Danger));
            Eq("InfoBrush", MsgDialogMaps.BrushKey(MsgSeverity.Info));
            Eq("SuccessBrush", MsgDialogMaps.BrushKey(MsgSeverity.Success));
            Eq("DangerEdgeBrush", MsgDialogMaps.EdgeKey(MsgSeverity.Danger));
            Eq("WarningSoftBrush", MsgDialogMaps.SoftKey(MsgSeverity.Warning));
            Eq("PrimaryButton", MsgDialogMaps.PrimaryStyleKey(MsgSeverity.Warning));
            Eq("PrimaryButton", MsgDialogMaps.PrimaryStyleKey(MsgSeverity.Success));
            Eq("DangerButton", MsgDialogMaps.PrimaryStyleKey(MsgSeverity.Danger));
        }

        private static void TestMsgDialogButtonSets()
        {
            MsgButtonSet ok = MsgDialogMaps.ResolveButtons(MsgButtons.Ok, null);
            Eq<bool>(false, ok.HasSecondary);
            Eq("知道了", ok.PrimaryText);
            Eq(MessageBoxResult.OK, ok.PrimaryResult);
            Eq(MessageBoxResult.OK, ok.EscResult);

            MsgButtonSet named = MsgDialogMaps.ResolveButtons(MsgButtons.Ok, "好");
            Eq("好", named.PrimaryText);

            MsgButtonSet okCancel = MsgDialogMaps.ResolveButtons(MsgButtons.OkCancel, "全部取消");
            Eq<bool>(true, okCancel.HasSecondary);
            Eq("全部取消", okCancel.PrimaryText);
            Eq("取消", okCancel.SecondaryText);
            Eq(MessageBoxResult.OK, okCancel.PrimaryResult);
            Eq(MessageBoxResult.Cancel, okCancel.SecondaryResult);
            Eq(MessageBoxResult.Cancel, okCancel.EscResult);

            MsgButtonSet yesNo = MsgDialogMaps.ResolveButtons(MsgButtons.YesNo, null);
            Eq<bool>(true, yesNo.HasSecondary);
            Eq("是", yesNo.PrimaryText);
            Eq("否", yesNo.SecondaryText);
            Eq(MessageBoxResult.Yes, yesNo.PrimaryResult);
            Eq(MessageBoxResult.No, yesNo.SecondaryResult);
            Eq(MessageBoxResult.No, yesNo.EscResult);
        }

        private static void TestMsgDialogSplitTitleBody()
        {
            // 标准两段式：\r\n\r\n 拆分 + 去定前缀 + 去吗字尾
            string[] r = MsgDialogMaps.SplitTitleBody(
                "确定取消全部 3 个 Defender 排除吗？\r\n\r\n手工添加的排除不会被修改。");
            Eq("取消全部 3 个 Defender 排除？", r[0]);
            Eq("手工添加的排除不会被修改。", r[1]);

            // \n\n 分隔 + 多段正文合并
            string[] r2 = MsgDialogMaps.SplitTitleBody("A\n\nB\n\nC");
            Eq("A", r2[0]);
            Eq("B\n\nC", r2[1]);

            // 无空行：整句为标题、正文为空
            string[] r3 = MsgDialogMaps.SplitTitleBody("这是系统必需的内置项，不能删。");
            Eq("这是系统必需的内置项，不能删。", r3[0]);
            Eq("", r3[1]);

            // 超长无句末标点：硬截 24 字加省略号
            string long32 = "一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二";
            string[] r4 = MsgDialogMaps.SplitTitleBody(long32);
            Eq<bool>(true, r4[0].Length == 25);
            Eq<bool>(true, r4[0].EndsWith("…"));

            // 超长含句末标点：截到最后一个句末标点（保留至少 8 字）
            string longP = "第一句要表达的意思完整。第二句继续补充一些内容再补充一些内容。";
            string[] r5 = MsgDialogMaps.SplitTitleBody(longP);
            Eq("第一句要表达的意思完整。", r5[0]);

            // null 安全
            string[] r6 = MsgDialogMaps.SplitTitleBody(null);
            Eq("", r6[0]);
            Eq("", r6[1]);
        }
    }
}
```

- [ ] **Step 1.2: 注册测试**

`tests/SelfTests.cs` Run() 内，`test("主题契约：校验器正反样例", TestThemeContractValidator);` 行后插入：

```csharp
            test("消息弹窗：级别→图标/画刷/样式映射", TestMsgDialogSeverityMaps);
            test("消息弹窗：按钮组解析与默认文案", TestMsgDialogButtonSets);
            test("消息弹窗：单字符串标题正文拆分", TestMsgDialogSplitTitleBody);
```

- [ ] **Step 1.3: 验证失败（编译错误即红灯）**

Run: `cmd //c dev.cmd test 2>&1 | tail -5`
Expected: 构建失败，错误含 `MsgSeverity`/`MsgDialogMaps` 未定义（类型尚不存在）。

- [ ] **Step 1.4: 实现 MsgSeverity.cs**

新建 `wpf/Dialogs/MsgSeverity.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 消息弹窗纯逻辑：级别/按钮映射 + 单字符串标题正文拆分（规格 2026-08-23 §2/§4）

using System;
using System.Windows;

namespace CaelusApp.WpfHost.Dialogs
{
    internal enum MsgSeverity { Info, Success, Warning, Danger }
    internal enum MsgButtons { Ok, OkCancel, YesNo }

    internal sealed class MsgButtonSet
    {
        internal string PrimaryText;
        internal string SecondaryText;
        internal bool HasSecondary;
        internal MessageBoxResult PrimaryResult;
        internal MessageBoxResult SecondaryResult;
        internal MessageBoxResult EscResult;
    }

    internal static class MsgDialogMaps
    {
        internal static string IconKey(MsgSeverity s)
        {
            switch (s)
            {
                case MsgSeverity.Success: return "IconCheck";
                case MsgSeverity.Warning: return "IconWarn";
                case MsgSeverity.Danger: return "IconError";
                default: return "IconInfo";
            }
        }

        internal static string BrushKey(MsgSeverity s) { return s.ToString() + "Brush"; }
        internal static string EdgeKey(MsgSeverity s) { return s.ToString() + "EdgeBrush"; }
        internal static string SoftKey(MsgSeverity s) { return s.ToString() + "SoftBrush"; }

        internal static string PrimaryStyleKey(MsgSeverity s)
        {
            return s == MsgSeverity.Danger ? "DangerButton" : "PrimaryButton";
        }

        internal static MsgButtonSet ResolveButtons(MsgButtons buttons, string okText)
        {
            MsgButtonSet set = new MsgButtonSet();
            if (buttons == MsgButtons.Ok)
            {
                set.PrimaryText = string.IsNullOrEmpty(okText) ? "知道了" : okText;
                set.PrimaryResult = MessageBoxResult.OK;
                set.EscResult = MessageBoxResult.OK;
                return set;
            }
            if (buttons == MsgButtons.OkCancel)
            {
                set.PrimaryText = string.IsNullOrEmpty(okText) ? "确定" : okText;
                set.SecondaryText = "取消";
                set.HasSecondary = true;
                set.PrimaryResult = MessageBoxResult.OK;
                set.SecondaryResult = MessageBoxResult.Cancel;
                set.EscResult = MessageBoxResult.Cancel;
                return set;
            }
            set.PrimaryText = string.IsNullOrEmpty(okText) ? "是" : okText;
            set.SecondaryText = "否";
            set.HasSecondary = true;
            set.PrimaryResult = MessageBoxResult.Yes;
            set.SecondaryResult = MessageBoxResult.No;
            set.EscResult = MessageBoxResult.No;
            return set;
        }

        // 单字符串 → {标题, 正文}：首个空行（\r\n\r\n 或 \n\n）拆分；无空行则全部为标题。
        internal static string[] SplitTitleBody(string message)
        {
            if (message == null) message = "";
            string title = message;
            string body = "";
            int i = message.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            int j = message.IndexOf("\n\n", StringComparison.Ordinal);
            int cut = -1, cutLen = 0;
            if (i >= 0 && (j < 0 || i <= j)) { cut = i; cutLen = 4; }
            else if (j >= 0) { cut = j; cutLen = 2; }
            if (cut >= 0)
            {
                title = message.Substring(0, cut);
                body = message.Substring(cut + cutLen).Trim();
            }
            return new string[] { NormalizeTitle(title), body };
        }

        // 标题规整：去"确定"前缀；"吗？"→"？"；超 24 字截断（句末标点优先，至少留 8 字）
        internal static string NormalizeTitle(string title)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0) return "";
            if (title.StartsWith("确定", StringComparison.Ordinal) && title.Length > 3)
                title = title.Substring(2);
            if (title.EndsWith("吗？", StringComparison.Ordinal))
                title = title.Substring(0, title.Length - 2) + "？";
            else if (title.EndsWith("吗?", StringComparison.Ordinal))
                title = title.Substring(0, title.Length - 2) + "?";
            if (title.Length <= 24) return title;
            string puncts = "。！？!?)）";
            int last = -1;
            for (int k = 23; k >= 8; k--)
            {
                if (puncts.IndexOf(title[k]) >= 0) { last = k; break; }
            }
            if (last >= 8) return title.Substring(0, last + 1);
            return title.Substring(0, 24) + "…";
        }
    }
}
```

- [ ] **Step 1.5: csproj 登记**

`wpf/Caelus.Wpf.csproj` 的 Compile ItemGroup（`<Compile Include="Dialogs\ReleaseNotesDialogWpf.xaml.cs">` 块之后、`..\src\Core` glob 之前）插入一行：

```xml
    <Compile Include="Dialogs\MsgSeverity.cs" />
```

- [ ] **Step 1.6: 跑测试到绿**

Run: `cmd //c dev.cmd test 2>&1 | tail -6`
Expected: `FAIL 0`，TOTAL 较基线 +3。

- [ ] **Step 1.7: 提交**

```bash
git add wpf/Dialogs/MsgSeverity.cs tests/SelfTests.MessageDialog.cs tests/SelfTests.cs wpf/Caelus.Wpf.csproj
git commit -m "wpf: 消息弹窗纯逻辑——级别/按钮映射与标题正文拆分（+3 自测）"
```

---

### Task 2: MessageDialogWpf 弹窗本体

**Files:**
- Create: `wpf/Dialogs/MessageDialogWpf.xaml`
- Create: `wpf/Dialogs/MessageDialogWpf.xaml.cs`
- Modify: `wpf/Caelus.Wpf.csproj`（Page + Compile + DependentUpon）

- [ ] **Step 2.1: XAML**

新建 `wpf/Dialogs/MessageDialogWpf.xaml`：

```xml
<Window x:Class="CaelusApp.WpfHost.Dialogs.MessageDialogWpf"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:CaelusApp.WpfHost.Controls"
        x:ClassModifier="internal"
        Title="Caelus" Width="400" SizeToContent="Height"
        WindowStyle="None" ResizeMode="NoResize" ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner" AllowsTransparency="False"
        Background="{DynamicResource Surface1Brush}"
        FontFamily="{DynamicResource FontUi}"
        PreviewKeyDown="OnWindowKeyDown"
        AutomationProperties.Name="消息弹窗">
  <!-- 无 DropShadowEffect（本机不渲染）：层次=1px 严重级描边 + owner 遮罩；圆角由 DWM 补齐（同主窗做法） -->
  <Border x:Name="Card" CornerRadius="{DynamicResource RadiusMd}"
          Background="{DynamicResource Surface1Brush}"
          BorderBrush="{DynamicResource InfoEdgeBrush}" BorderThickness="1"
          Padding="26,24,22,20" Margin="1">
    <StackPanel>
      <Grid Width="58" Height="58" HorizontalAlignment="Center" Margin="0,2,0,0">
        <Ellipse x:Name="Glow" Width="58" Height="58"/>
        <controls:IconView x:Name="Icon" Width="32" Height="32" Key="IconInfo"
                           Foreground="{DynamicResource InfoBrush}"
                           HorizontalAlignment="Center" VerticalAlignment="Center"/>
      </Grid>
      <TextBlock x:Name="LblTitle" TextWrapping="Wrap" TextAlignment="Center" MaxHeight="56"
                 Margin="4,12,4,0" FontSize="{DynamicResource FontSizeTitle}" FontWeight="SemiBold"
                 Foreground="{DynamicResource TextPrimaryBrush}" AutomationProperties.Name="弹窗标题"/>
      <TextBlock x:Name="LblBody" TextWrapping="Wrap" TextAlignment="Center" Margin="4,8,4,0"
                 FontSize="13" LineHeight="20"
                 Foreground="{DynamicResource TextSecondaryBrush}" AutomationProperties.Name="弹窗正文"/>
      <Button x:Name="BtnDetail" Margin="0,14,0,0" Visibility="Collapsed"
              Style="{DynamicResource GhostButton}" FontSize="{DynamicResource FontSizeCaption}"
              Click="OnDetailClick" AutomationProperties.Name="技术详情开关"
              Content="技术详情 ▾"/>
      <Border x:Name="DetailHost" Visibility="Collapsed" Margin="0,8,0,0" MaxHeight="200"
              Background="{DynamicResource Surface0Brush}" CornerRadius="{DynamicResource RadiusSm}"
              BorderBrush="{DynamicResource TextTertiaryBrush}" BorderThickness="1" Opacity="0.95">
        <ScrollViewer VerticalScrollBarVisibility="Auto">
          <TextBlock x:Name="LblDetailText" FontFamily="{DynamicResource FontMono}"
                     FontSize="{DynamicResource FontSizeMono}" TextWrapping="Wrap"
                     Foreground="{DynamicResource TextSecondaryBrush}"
                     Margin="10,8" HorizontalAlignment="Left"/>
        </ScrollViewer>
      </Border>
      <Grid Margin="0,20,0,0">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <Button x:Name="BtnSecondary" Grid.Column="0" Margin="0,0,5,0" Visibility="Collapsed"
                Style="{DynamicResource GhostButton}" Click="OnSecondaryClick"
                AutomationProperties.Name="取消按钮"/>
        <Button x:Name="BtnPrimary" Grid.Column="1" Margin="5,0,0,0"
                Style="{DynamicResource PrimaryButton}" Click="OnPrimaryClick"
                AutomationProperties.Name="主按钮"/>
      </Grid>
    </StackPanel>
  </Border>
</Window>
```

- [ ] **Step 2.2: 代码后置**

新建 `wpf/Dialogs/MessageDialogWpf.xaml.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 主题化消息弹窗：无边框模态小窗（规格 2026-08-23 §2/§3）

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CaelusApp.WpfHost.Dialogs
{
    internal partial class MessageDialogWpf : Window
    {
        private MsgButtonSet buttons;
        private MessageBoxResult chosen;
        private MessageBoxResult enterResult;
        private bool choseExplicitly;
        private bool closing;
        private MaskAdorner mask;

        private MessageDialogWpf(Window owner, string title, string body,
            MsgSeverity severity, MsgButtons buttonSet, string detail, string okText,
            MessageBoxResult defaultResult)
        {
            InitializeComponent();
            buttons = MsgDialogMaps.ResolveButtons(buttonSet, okText);

            Brush sevBrush = TryFindResource(MsgDialogMaps.BrushKey(severity)) as Brush;
            if (sevBrush == null) sevBrush = Brushes.White;
            Icon.Key = MsgDialogMaps.IconKey(severity);
            Icon.Foreground = sevBrush;
            Brush edge = TryFindResource(MsgDialogMaps.EdgeKey(severity)) as Brush;
            if (edge != null) Card.BorderBrush = edge;
            Glow.Fill = BuildGlowBrush(sevBrush);

            LblTitle.Text = title;
            if (string.IsNullOrEmpty(body)) LblBody.Visibility = Visibility.Collapsed;
            else LblBody.Text = body;

            if (string.IsNullOrEmpty(detail)) BtnDetail.Visibility = Visibility.Collapsed;
            else LblDetailText.Text = detail;

            Style primaryStyle = TryFindResource(MsgDialogMaps.PrimaryStyleKey(severity)) as Style;
            if (primaryStyle != null) BtnPrimary.Style = primaryStyle;
            BtnPrimary.Content = buttons.PrimaryText;
            if (buttons.HasSecondary)
            {
                BtnSecondary.Visibility = Visibility.Visible;
                BtnSecondary.Content = buttons.SecondaryText;
            }
            else
            {
                Grid.SetColumn(BtnPrimary, 0);
                Grid.SetColumnSpan(BtnPrimary, 2);
                BtnPrimary.Margin = new Thickness(0);
            }

            if (owner != null && owner.IsLoaded) Owner = owner;
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Loaded += delegate
            {
                ScaleTransform st = new ScaleTransform(0.96, 0.96);
                Card.RenderTransform = st;
                Card.RenderTransformOrigin = new Point(0.5, 0.5);
                DoubleAnimation grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180));
                grow.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                Motion.BreathPulse(Glow); // 内部已 8fps 节流
                mask = AttachMask(owner);
                FocusDefault(defaultResult);
            };
            Closed += delegate
            {
                if (!choseExplicitly) chosen = buttons.EscResult;
                DetachMask();
            };
        }

        // 光晕：严重级色 18% → 70% 处透明的径向渐变
        private Brush BuildGlowBrush(Brush source)
        {
            SolidColorBrush solid = source as SolidColorBrush;
            RadialGradientBrush radial = new RadialGradientBrush();
            radial.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x2E, solid.Color.R, solid.Color.G, solid.Color.B), 0.0));
            radial.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.7));
            if (radial.CanFreeze) radial.Freeze();
            return radial;
        }

        private void FocusDefault(MessageBoxResult defaultResult)
        {
            if (buttons.HasSecondary && defaultResult == buttons.SecondaryResult)
            {
                BtnSecondary.Focus();
                enterResult = buttons.SecondaryResult;
            }
            else
            {
                BtnPrimary.Focus();
                enterResult = buttons.PrimaryResult;
            }
        }

        private MaskAdorner AttachMask(Window owner)
        {
            if (owner == null || owner.Visibility != Visibility.Visible) return null;
            UIElement adornable = owner.Content as UIElement;
            if (adornable == null) return null;
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(adornable);
            if (layer == null) return null;
            MaskAdorner a = new MaskAdorner(adornable);
            layer.Add(a);
            return a;
        }

        private void DetachMask()
        {
            if (mask == null) return;
            UIElement target = mask.AdornedElement;
            AdornerLayer layer = VisualTreeHelper.GetAdornerLayer(target);
            if (layer != null) layer.Remove(mask);
            mask = null;
        }

        private void OnPrimaryClick(object sender, RoutedEventArgs e) { CloseWith(buttons.PrimaryResult); }
        private void OnSecondaryClick(object sender, RoutedEventArgs e) { CloseWith(buttons.SecondaryResult); }

        private void CloseWith(MessageBoxResult r)
        {
            if (closing) return;
            closing = true;
            choseExplicitly = true;
            chosen = r;
            DoubleAnimation fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += delegate { Close(); };
            Card.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private bool detailOpen;
        private void OnDetailClick(object sender, RoutedEventArgs e)
        {
            detailOpen = !detailOpen;
            DetailHost.Visibility = detailOpen ? Visibility.Visible : Visibility.Collapsed;
            BtnDetail.Content = detailOpen ? "技术详情 ▴" : "技术详情 ▾";
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseWith(buttons.EscResult); }
            else if (e.Key == Key.Enter) { e.Handled = true; CloseWith(enterResult); }
        }

        // —— 静态入口 ——

        internal static MessageBoxResult Show(Window owner, string title, string body,
            MsgSeverity severity, MsgButtons buttonSet, string detail, string okText,
            MessageBoxResult defaultResult)
        {
            try
            {
                MessageDialogWpf dlg = new MessageDialogWpf(owner, title, body, severity,
                    buttonSet, detail, okText, defaultResult);
                dlg.ShowDialog();
                return dlg.chosen;
            }
            catch (Exception ex)
            {
                Logger.Log("MessageDialogWpf 异常，降级原生弹窗：" + ex.Message);
                MessageBoxButton native = buttonSet == MsgButtons.Ok ? MessageBoxButton.OK
                    : (buttonSet == MsgButtons.OkCancel ? MessageBoxButton.OKCancel : MessageBoxButton.YesNo);
                MessageBoxImage img = severity == MsgSeverity.Danger ? MessageBoxImage.Error
                    : (severity == MsgSeverity.Warning ? MessageBoxImage.Warning : MessageBoxImage.Information);
                MessageBox.Show(owner, title + "\r\n\r\n" + body, "Caelus", native, img);
                return MessageBoxResult.Cancel;
            }
        }

        // 便捷重载：无详情/无自定义文案/默认焦点在主按钮
        internal static MessageBoxResult Show(Window owner, string title, string body,
            MsgSeverity severity, MsgButtons buttonSet)
        {
            return Show(owner, title, body, severity, buttonSet, null, null, MessageBoxResult.None);
        }

        // 单字符串自动拆分（动态 ConfirmKey 等场景）
        internal static MessageBoxResult Show(Window owner, string message,
            MsgSeverity severity, MsgButtons buttonSet, MessageBoxResult defaultResult)
        {
            string[] parts = MsgDialogMaps.SplitTitleBody(message);
            return Show(owner, parts[0], parts[1], severity, buttonSet, null, null, defaultResult);
        }

        private sealed class MaskAdorner : Adorner
        {
            public MaskAdorner(UIElement adorned) : base(adorned) { }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                SolidColorBrush b = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
                if (b.CanFreeze) b.Freeze();
                dc.DrawRectangle(b, null, new Rect(AdornedElement.RenderSize));
            }
        }
    }
}
```

**注意**：`defaultResult` 传 `MessageBoxResult.None` 时 FocusDefault 走 else 分支聚焦主按钮，行为正确；`SoftKey` 映射在 Task 1 已实现（自测引用）。

- [ ] **Step 2.3: csproj 登记**

`wpf/Caelus.Wpf.csproj`：Page ItemGroup 里 `<Page Include="Dialogs\ReleaseNotesDialogWpf.xaml" />` 后加：

```xml
    <Page Include="Dialogs\MessageDialogWpf.xaml" />
```

Compile ItemGroup 里 Task 1 加过的 `<Compile Include="Dialogs\MsgSeverity.cs" />` 后加：

```xml
    <Compile Include="Dialogs\MessageDialogWpf.xaml.cs">
      <DependentUpon>MessageDialogWpf.xaml</DependentUpon>
    </Compile>
```

- [ ] **Step 2.4: 构建验证**

Run: `cmd //c build-wpf.cmd 2>&1 | tail -3`
Expected: `WPF Build OK -> wpf\bin\Release\CaelusWpf.exe`（零新增警告）

Run: `cmd //c dev.cmd test 2>&1 | tail -6`
Expected: `FAIL 0`（弹窗本体无新测试，确认无回归）

- [ ] **Step 2.5: 提交**

```bash
git add wpf/Dialogs/MessageDialogWpf.xaml wpf/Dialogs/MessageDialogWpf.xaml.cs wpf/Caelus.Wpf.csproj
git commit -m "wpf: MessageDialogWpf 主题化弹窗本体——无边框小窗/光晕图标/折叠详情/遮罩"
```

---

### Task 3: SettingsView 12 处替换

**Files:**
- Modify: `wpf/Views/SettingsView.xaml.cs`

替换原则：**按钮组合与结果比较保持原样**（比较逻辑一行不改），只换调用本身。所有调用已按规格 §5 定级。

- [ ] **Step 3.1: 文件头加 using**

`using` 区加：

```csharp
using CaelusApp.WpfHost.Dialogs;
```

- [ ] **Step 3.2: 12 处逐条替换**

① L206 附近（恢复配色确认）：

```csharp
            if (MessageDialogWpf.Show(Window.GetWindow(this), "恢复三模式默认配色？",
                    "自定义强调色将被清除，恢复为靛蓝/蜜桃橙/暗金。",
                    MsgSeverity.Warning, MsgButtons.YesNo, null, "恢复默认", MessageBoxResult.No)
                != MessageBoxResult.Yes) return;
```

② L266 附近（OnRestore 确认）：

```csharp
            if (MessageDialogWpf.Show(Window.GetWindow(this), "恢复所有已记录的系统项？",
                    "这会退出当前优化状态，并尝试撤销 Caelus 记录的相关修改。",
                    MsgSeverity.Warning, MsgButtons.YesNo, null, "恢复", MessageBoxResult.No)
                != MessageBoxResult.Yes) return;
```

③ L300 附近（def.unavailable）：

```csharp
                MessageDialogWpf.Show(Window.GetWindow(this), "无法读取 Defender 设置",
                    "可能未安装、已被第三方杀软接管，或当前权限不足。",
                    MsgSeverity.Danger, MsgButtons.Ok);
```

④ L332 附近（shader.confirm）：

```csharp
            if (MessageDialogWpf.Show(Window.GetWindow(this), "清空显卡着色器缓存？",
                    "怀疑缓存损坏可清一次排查，不保证更流畅。清理前先退出游戏；之后每个游戏首次启动要重新编译，开头可能更卡。",
                    MsgSeverity.Warning, MsgButtons.OkCancel, null, "清理", MessageBoxResult.Cancel)
                != MessageBoxResult.OK) return;
```

⑤ L740 附近（def.notours）：

```csharp
                MessageDialogWpf.Show(Window.GetWindow(this), "这条排除不是 Caelus 添加的",
                    "可能是你在 Windows 安全中心手动加的，Caelus 不会去动它。需要取消请到 Windows 安全中心操作。",
                    MsgSeverity.Info, MsgButtons.Ok);
```

⑥ L744 附近（def.confirm，路径进技术详情）：

```csharp
            if (want && MessageDialogWpf.Show(Window.GetWindow(this), "把《" + row.Name + "》排除出实时扫描？",
                    "该目录下的文件将不再被查杀。只有确信游戏来源可靠时才继续。",
                    MsgSeverity.Warning, MsgButtons.OkCancel, row.Root, "排除", MessageBoxResult.Cancel)
                != MessageBoxResult.OK) return;
```

⑦ L770 附近（def.failed）：

```csharp
                    if (!ok) MessageDialogWpf.Show(Window.GetWindow(this), "Defender 操作失败",
                        "系统未接受这次修改，可能被安全策略或第三方杀软阻止。",
                        MsgSeverity.Danger, MsgButtons.Ok);
```

⑧ L781 附近（def.clearall.none）：

```csharp
                MessageDialogWpf.Show(Window.GetWindow(this), "没有可取消的排除项",
                    "Caelus 目前没有添加过任何排除。",
                    MsgSeverity.Info, MsgButtons.Ok);
```

⑨ L785 附近（全部取消确认）：

```csharp
            if (MessageDialogWpf.Show(Window.GetWindow(this), "取消全部 " + count + " 个 Defender 排除？",
                    "仅移除由 Caelus 添加的项目，手工添加的排除不受影响。",
                    MsgSeverity.Warning, MsgButtons.OkCancel, null, "全部取消", MessageBoxResult.Cancel)
                != MessageBoxResult.OK) return;
```

⑩ L810 附近（def.clearall.done → Success）：

```csharp
                    MessageDialogWpf.Show(Window.GetWindow(this), "已取消 " + removed + " 个排除项",
                        "你手工添加的不受影响。",
                        MsgSeverity.Success, MsgButtons.Ok, null, "好", MessageBoxResult.OK);
```

⑪ L812 附近（def.unavailable 二次弹）：

```csharp
                    if (fresh == null) MessageDialogWpf.Show(Window.GetWindow(this), "无法读取 Defender 设置",
                        "可能未安装、已被第三方杀软接管，或当前权限不足。",
                        MsgSeverity.Danger, MsgButtons.Ok);
```

⑫ L1095 附近（addon.confirm → Danger，路径进技术详情）：

```csharp
            if (MessageDialogWpf.Show(Window.GetWindow(this), "删除附加层目录？",
                    "删除不可撤销。游戏本体、登录链路和更新器不在删除范围。",
                    MsgSeverity.Danger, MsgButtons.YesNo, resolvedRoot, "删除", MessageBoxResult.No)
                != MessageBoxResult.Yes) return;
```

- [ ] **Step 3.3: 构建验证**

Run: `cmd //c build-wpf.cmd 2>&1 | tail -3`
Expected: `WPF Build OK`（确认该文件已无 `MessageBox.Show` 残留：`grep -c "MessageBox.Show" wpf/Views/SettingsView.xaml.cs` → 0）

- [ ] **Step 3.4: 提交**

```bash
git add wpf/Views/SettingsView.xaml.cs
git commit -m "wpf: 设置页 12 处弹窗换用 MessageDialogWpf（Defender/配色/着色器/附加层）"
```

---

### Task 4: WhitelistView 7 处替换

**Files:**
- Modify: `wpf/Views/WhitelistView.xaml.cs`

- [ ] **Step 4.1: using 同 Task 3.1**

- [ ] **Step 4.2: 7 处替换**

L178/198/207/216 四处错误弹（模式相同，error 变量名随现场）：

```csharp
                MessageDialogWpf.Show(Window.GetWindow(this), "白名单操作失败", "详细信息见技术详情。",
                    MsgSeverity.Danger, MsgButtons.Ok, error, null, MessageBoxResult.OK);
```

L192（white.required.locked）：

```csharp
                MessageDialogWpf.Show(Window.GetWindow(this), "系统内置项不可删除",
                    "这是系统必需的内置项。",
                    MsgSeverity.Info, MsgButtons.Ok);
```

L223（white.reset.confirm → Danger，原比较 `!= MessageBoxResult.Yes` 保留）：

```csharp
            MessageBoxResult r = MessageDialogWpf.Show(Window.GetWindow(this), "恢复默认白名单预设？",
                "会删除全部自定义白名单规则，且无法撤销。",
                MsgSeverity.Danger, MsgButtons.YesNo, null, "恢复默认", MessageBoxResult.No);
```

L228（重置失败）：

```csharp
                MessageDialogWpf.Show(Window.GetWindow(this), "白名单重置失败", "详细信息见技术详情。",
                    MsgSeverity.Danger, MsgButtons.Ok, error, null, MessageBoxResult.OK);
```

- [ ] **Step 4.3: 构建验证**

Run: `cmd //c build-wpf.cmd 2>&1 | tail -3`；`grep -c "MessageBox.Show" wpf/Views/WhitelistView.xaml.cs` → 0

- [ ] **Step 4.4: 提交**

```bash
git add wpf/Views/WhitelistView.xaml.cs
git commit -m "wpf: 白名单页 7 处弹窗换用 MessageDialogWpf（错误详情进折叠区）"
```

---

### Task 5: EnvironmentView 3 + GraphicsViewModel 1 + PolicyRuntime 1

**Files:**
- Modify: `wpf/Views/EnvironmentView.xaml.cs`
- Modify: `wpf/GraphicsViewModel.cs`
- Modify: `wpf/PolicyRuntime.cs`

- [ ] **Step 5.1: EnvironmentView（using 同前）**

L43（vbs.needadmin）：

```csharp
                MessageDialogWpf.Show(Window.GetWindow(this), "需要管理员权限",
                    "修改 VBS / hypervisor 需要管理员身份运行 Caelus。",
                    MsgSeverity.Warning, MsgButtons.Ok);
```

L54（vbs.warn → Danger，原按钮 OkCancel、比较 `!= MessageBoxResult.OK` 保留）：

```csharp
                    MessageBoxResult result = MessageDialogWpf.Show(Window.GetWindow(this), "关闭 VBS / 内存完整性？",
                        "系统安全性会下降，WSL2 / Docker / Hyper-V / 沙盒将不可用。重启后生效，将来恢复需再重启一次。",
                        MsgSeverity.Danger, MsgButtons.OkCancel, null, "关闭 VBS", MessageBoxResult.Cancel);
```

L69（VBS 操作失败，message 进技术详情）：

```csharp
                MessageDialogWpf.Show(Window.GetWindow(this), "VBS 设置失败", "系统设置保持原样。",
                    MsgSeverity.Danger, MsgButtons.Ok, message, null, MessageBoxResult.OK);
```

- [ ] **Step 5.2: GraphicsViewModel（ViewModel 无窗口，owner 传 null）**

L242 附近：

```csharp
                    MessageDialogWpf.Show(null, "窗口化优化写入失败",
                        "系统设置保持原样。",
                        MsgSeverity.Danger, MsgButtons.Ok);
```

（文件顶部若已 `using System.Windows;` 则只需加 `using CaelusApp.WpfHost.Dialogs;`；替换后该处 `System.Windows.MessageBox` 全限定调用一并删除。）

- [ ] **Step 5.3: PolicyRuntime（动态 ConfirmKey，单字符串自动拆分重载）**

L56 附近：

```csharp
                    MessageBoxResult r = MessageDialogWpf.Show(null, Lang.T(item.ConfirmKey),
                        MsgSeverity.Warning, MsgButtons.OkCancel, MessageBoxResult.Cancel);
```

（比较 `if (r != MessageBoxResult.OK)` 原样保留。）

- [ ] **Step 5.4: 构建验证**

Run: `cmd //c build-wpf.cmd 2>&1 | tail -3`；三文件 `grep -c "MessageBox.Show"` 均 → 0

- [ ] **Step 5.5: 提交**

```bash
git add wpf/Views/EnvironmentView.xaml.cs wpf/GraphicsViewModel.cs wpf/PolicyRuntime.cs
git commit -m "wpf: 环境页/VBS/窗口优化/策略确认 5 处弹窗换用 MessageDialogWpf"
```

---

### Task 6: WpfRuntime 2 + LogView 1 + LibraryView 1 + AddGameDialog 1

**Files:**
- Modify: `wpf/WpfRuntime.cs`
- Modify: `wpf/Views/LogView.xaml.cs`
- Modify: `wpf/Views/LibraryView.xaml.cs`
- Modify: `wpf/Dialogs/AddGameDialogWpf.xaml.cs`

- [ ] **Step 6.1: WpfRuntime 托盘两处**

ResetDefaults() 方法内，方法体开头取 owner（该类无窗口引用）：

```csharp
            Window owner = System.Windows.Application.Current == null
                ? null : System.Windows.Application.Current.MainWindow;
```

L438（tray.resetask）：

```csharp
            if (MessageDialogWpf.Show(owner, "恢复默认配置？",
                    "所有开关、反作弊选择和白名单恢复为默认，游戏列表保留。",
                    MsgSeverity.Warning, MsgButtons.OkCancel, null, "恢复默认", MessageBoxResult.Cancel)
                != MessageBoxResult.OK) return;
```

L466（白名单写入失败）：

```csharp
                System.Windows.MessageBox.Show(gameMode.WhitelistLastError, "Caelus",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
```

替换为：

```csharp
                MessageDialogWpf.Show(owner, "白名单写入失败", "默认配置已部分恢复。",
                    MsgSeverity.Danger, MsgButtons.Ok, gameMode.WhitelistLastError,
                    null, MessageBoxResult.OK);
```

（加 `using CaelusApp.WpfHost.Dialogs;`。托盘命令在共享 UI 线程执行；若实测出现跨线程异常，按规格 §9 降级保留原生并回写规格。）

- [ ] **Step 6.2: LogView L81（原按钮 YesNo、比较 Yes 保留）**

```csharp
            MessageBoxResult r = MessageDialogWpf.Show(Window.GetWindow(this), "清空运行日志？",
                "Caelus.log 将被清空；已归档的 Caelus.log.old 不受影响。",
                MsgSeverity.Warning, MsgButtons.YesNo, null, "清空", MessageBoxResult.No);
```

- [ ] **Step 6.3: LibraryView L183**

```csharp
            if (MessageDialogWpf.Show(Window.GetWindow(this), "从游戏库移除《" + item.Name + "》？",
                    "移除后可随时重新添加。",
                    MsgSeverity.Warning, MsgButtons.YesNo, null, "移除", MessageBoxResult.No)
                != MessageBoxResult.Yes) return;
```

- [ ] **Step 6.4: AddGameDialogWpf L363**

```csharp
                    MessageDialogWpf.Show(Window.GetWindow(this), "添加游戏失败", "详细信息见技术详情。",
                        MsgSeverity.Danger, MsgButtons.Ok, error, null, MessageBoxResult.OK);
```

- [ ] **Step 6.5: 构建验证 + 残留清点**

Run: `cmd //c build-wpf.cmd 2>&1 | tail -3`
Run: `grep -rn "MessageBox.Show" wpf --include="*.cs" | wc -l` → **0**

- [ ] **Step 6.6: 提交**

```bash
git add wpf/WpfRuntime.cs wpf/Views/LogView.xaml.cs wpf/Views/LibraryView.xaml.cs wpf/Dialogs/AddGameDialogWpf.xaml.cs
git commit -m "wpf: 托盘/日志/游戏库/添加游戏 5 处弹窗换用 MessageDialogWpf——29 处原生弹窗清零"
```

---

### Task 7: 全量验证 + 手工验收 + 规格回写

**Files:**
- Modify: `docs/superpowers/specs/2026-08-23-message-dialog-design.md`

- [ ] **Step 7.1: 全量自测**

Run: `cmd //c dev.cmd test 2>&1 | tail -8`
Expected: `FAIL 0`，TOTAL = 基线 + 3

- [ ] **Step 7.2: 手工验收（GUI 不可自动化，人工跑清单）**

启动：`wpf/bin/Release/CaelusWpf.exe`，逐项核对（触发入口 → 期望）：

1. 设置页 →「恢复默认配色」→ Warning 弹窗、光晕橙、主按钮"恢复默认"（强调色）、Esc=取消
2. 设置页 → Defender 排除区空态点「全部取消」→ Info"没有可取消的排除项"
3. 有排除时点某游戏「排除」→ Warning、技术详情含目录路径、展开/收起正常
4. 白名单页点内置项删除 → Info"系统内置项不可删除"
5. 日志页「清空」→ Warning"清空运行日志？"主按钮"清空"
6. 游戏库移除某游戏 → Warning、主按钮"移除"
7. 环境页 VBS 开关（如可）→ Danger、主按钮"关闭 VBS"（红色 DangerButton）
8. 亮色主题切换后再跑 1/7 → 图标/描边/按钮对比度正常
9. 模式切到 竞技/暗金 → 弹窗主按钮颜色跟随模式强调色
10. Enter 直接触发默认按钮；Tab 在次按钮/主按钮/技术详情间循环

任一项不符：修完重跑本清单。全部通过才进 Step 7.3。

- [ ] **Step 7.3: 规格回写（执行偏差）**

修改 `docs/superpowers/specs/2026-08-23-message-dialog-design.md`：

1. §9 第 2 条改为："csproj 为显式逐文件清单——Dialogs 下新增文件需补 `<Compile>`/`<Page>` 行（本计划 Task 1/2 已登记）"
2. §5 表 #25 按钮组改为 `YesNo / "清空"`（原为 OkCancel，与代码实际不符，已按代码保留 YesNo）
3. §5 表 #21 按钮组改为 `OkCancel / "关闭 VBS"`（原为 YesNo，同上按代码实际保留 OkCancel）
4. §3 窗体行的 DropShadowEffect 表述改为"不使用 DropShadowEffect（本机不渲染，已实测），层次=描边+遮罩"
5. 托盘 2 处如遇线程问题降级保留原生，则在 §5 表 #23/#24 标注

- [ ] **Step 7.4: 提交**

```bash
git add docs/superpowers/specs/2026-08-23-message-dialog-design.md
git commit -m "docs: 消息弹窗规格回写执行偏差（csproj 登记/按钮组合按代码实际）"
```

---

## 自审记录（writing-plans Self-Review）

1. **Spec coverage**：规格 §2 API（Task 1/2）、§3 视觉（Task 2）、§4 内容规则（Task 1 拆分逻辑 + 各调用点文案）、§5 全部 29 处（Task 3-6 逐条）、§6 健壮性（Task 2 catch 降级 + owner null 分支）、§7 测试（Task 1 自测 + Task 7 验收）——无缺口。
2. **Placeholder scan**：全文无 TBD/TODO/含糊步骤；代码块均为可直抄的最终代码。
3. **Type consistency**：`MsgDialogMaps`/`MsgButtonSet`/`MessageDialogWpf.Show` 三个重载签名在 Task 1/2 定义、Task 3-6 使用的参数序完全一致（owner, title, body, severity, buttons, detail, okText, defaultResult）；测试引用的 `SoftKey` 已在 Task 1 实现。
