# UI 重构 Phase 1 实现计划：WPF 骨架 + 设计系统 + 概览页

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可编译的 WPF 预览外壳（设计系统 Token、主窗口、概览页），与现有 WinForms 应用并存，互不回归。

**Architecture:** 新增 `src/UiShared/`（纯 C# 表现层逻辑：调色板、动效策略、MVVM 基座、概览状态计算），同时编译进现有 WinForms exe 与新的 WPF exe；`wpf/` 目录存放 WPF 项目（XAML 薄渲染层）。现有 `build.cmd` 保持不变、持续可用；新增 `build-wpf.cmd` 用系统自带 MSBuild 编译 WPF 预览 exe（`CaelusWpf.exe`）。

**Tech Stack:** C# / WPF（.NET Framework v4.0 目标、运行于 4.8 运行时）、系统自带 MSBuild 4.0 + Microsoft.WinFX.targets（无 dotnet SDK、无 targeting pack，全部引用走显式 HintPath）、既有自测框架（`dev.cmd test`，基线 149 PASS / 0 FAIL / 3 SKIP）。

**范围说明：** 本计划只覆盖规格 §9 的 Phase 1。Phase 2（游戏库+优化策略）、Phase 3（日志+设置+其余页面）、Phase 4（动效+可访问性打磨）在 Phase 1 验收后各自单独出计划。Phase 1 包含基础动效与可访问性的“最小可用集”，完整打磨留给 Phase 4。

**规格文档:** `docs/superpowers/specs/2026-08-09-ui-redesign-design.md`

---

## 环境事实（执行前必读）

- 构建：`build.cmd [输出名] [--selftest]`，直接调用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，无 csproj。
- 自测：`dev.cmd test`（Git Bash 中 `cmd //c dev.cmd test`），报告写到 `%TEMP%\Caelus.selftest.txt`，末行形如 `TOTAL 149/0/3`。任何任务完成后 FAIL 必须为 0，SKIP 保持 3（环境相关，非回归）。
- WPF 工具链：`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe` 与 `Microsoft.WinFX.targets` 存在；`WPF\` 子目录含 PresentationFramework/PresentationCore/WindowsBase/PresentationBuildTasks。本机**没有** targeting pack（RedistList 为空），因此 csproj 必须：`TargetFrameworkVersion=v4.0`（与现有 csc 输出一致，运行在 4.8 运行时上），WPF 程序集用显式 HintPath 指向 framework 目录。
- 代码结构：`src/Core/`、`src/Platform/` 与 UI 无关（唯一例外见 Task 1）；`src/Ui/`、`src/Program.cs`、`src/AssemblyInfo.cs` 不进入 WPF 编译。
- 所有 `.cmd` 文件**只能含 ASCII**（build.cmd 头部注释说明了代码页陷阱）。
- 版本号：WPF 预览 exe 沿用 `1.7.0.0`，避免触发 UpdateChecker 误判。

## 文件结构

| 文件 | 责任 |
|------|------|
| `src/Platform/Native.Desktop.cs` | （修改）解除对 `Theme.LightMode` 的引用 |
| `src/Ui/Theme.cs` | （修改）静态构造中向 Native 注入主题查询钩子 |
| `src/UiShared/Palette.cs` | 规格 §3.1 全部颜色 Token（语义色 + 中性色，深浅双主题） |
| `src/UiShared/UiMotion.cs` | 规格 §6 动效时长/缓动 Token 与减少动态效果策略 |
| `src/UiShared/ViewModelBase.cs` | INotifyPropertyChanged 基类 |
| `src/UiShared/RelayCommand.cs` | ICommand 实现 |
| `src/UiShared/OverviewStatus.cs` | 规格 §5.1 状态结论与指标分级的纯逻辑 |
| `src/UiShared/OverviewViewModel.cs` | 概览页 ViewModel + `IOverviewSource` 抽象 + 示例数据源 |
| `tests/SelfTests.UiShared.cs` | 上述全部逻辑的自测 |
| `tests/SelfTests.cs` | （修改）注册新测试 |
| `wpf/Caelus.Wpf.csproj` | WPF 项目定义（链接 Core/Platform/UiShared） |
| `wpf/Properties/AssemblyInfo.cs` | WPF 程序集元数据（含 ThemeInfo） |
| `wpf/app.manifest` | requireAdministrator + PerMonitorV2（与 build.cmd 生成的一致） |
| `wpf/App.xaml` / `App.xaml.cs` | 入口、主题装载、`--wpf-shot` 截图探针 |
| `wpf/Themes/Colors.Light.xaml` / `Colors.Dark.xaml` | 颜色资源字典 |
| `wpf/Themes/Tokens.xaml` | 字体/间距/圆角/阴影资源 |
| `wpf/Themes/Styles.xaml` | 按钮、开关（以 ToggleButton 样式）、卡片、导航、分段控件样式 |
| `wpf/ThemeManager.cs` | 运行时切换深浅主题字典 |
| `wpf/MainWindow.xaml` / `.cs` | 外壳：自定义标题栏、NavRail、分段控件、内容宿主、托盘图标 |
| `wpf/Views/OverviewView.xaml` / `.cs` | 概览页视图 |
| `wpf/Views/PlaceholderView.xaml` / `.cs` | 未迁移页面的占位视图 |
| `build-wpf.cmd` | WPF 构建脚本（ASCII only） |

---

### Task 1: 解除 Native.Desktop 对 Theme 的依赖

WPF 项目不会编译 `src/Ui/`，但 `src/Platform/Native.Desktop.cs:91` 引用了 `Theme.LightMode`，不解耦则 WPF 链接编译失败。

**Files:**
- Modify: `src/Platform/Native.Desktop.cs`（第 91 行附近）
- Modify: `src/Ui/Theme.cs`（`Theme` 静态类）
- Test: `tests/SelfTests.UiShared.cs`（新建）
- Modify: `tests/SelfTests.cs`（注册测试，锚点：`test("模式配色：…` 之后）

- [ ] **Step 1: 写失败测试**

新建 `tests/SelfTests.UiShared.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 UiShared 表现层逻辑与 WPF 解耦点的自测

using System;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestNativeLightModeHook()
        {
            Func<bool> prev = Native.LightModeQuery;
            try
            {
                Native.LightModeQuery = null;
                Eq(false, Native.QueryLightMode());
                Native.LightModeQuery = () => true;
                Eq(true, Native.QueryLightMode());
                Native.LightModeQuery = () => false;
                Eq(false, Native.QueryLightMode());
            }
            finally { Native.LightModeQuery = prev; }
        }
    }
}
```

在 `tests/SelfTests.cs` 的 `Run` 方法中，锚点 `test("模式配色：底色固定，常规 / 竞技 / 自定义强调色各不相同", ...)` 整块**之后**插入：

```csharp
            test("桌面主题钩子：未注入时安全回退，注入后跟随应用主题", TestNativeLightModeHook);
```

- [ ] **Step 2: 运行测试确认失败**

Git Bash 执行：`cmd //c dev.cmd test`
预期：FAIL 1 条，`Native` 不包含 `LightModeQuery` 定义（编译错误也算失败）。

- [ ] **Step 3: 最小实现**

`src/Platform/Native.Desktop.cs`：该文件整体是 `internal static partial class Native`（无嵌套 Desktop 类，`Dark()` 直接挂在 `Native` 上）。在 `Dark` 方法旁新增钩子成员，并替换 `Theme.LightMode` 引用：

```csharp
        // 应用主题查询钩子：由 UI 层注入；未注入时回退为深色（false），
        // 使 Platform 层不依赖任何 UI 程序集。
        public static Func<bool> LightModeQuery;

        public static bool QueryLightMode()
        {
            Func<bool> q = LightModeQuery;
            return q != null && q();
        }
```

把第 91 行的 `Theme.LightMode ? "Explorer" : "DarkMode_Explorer"` 改为：

```csharp
                        QueryLightMode() ? "Explorer" : "DarkMode_Explorer", null);
```

`src/Ui/Theme.cs`：在 `Theme` 静态类中加入静态构造函数，把查询接到当前应用主题：

```csharp
        static Theme()
        {
            Native.LightModeQuery = () => light;
        }
```

- [ ] **Step 4: 运行测试确认通过**

`cmd //c dev.cmd test`
预期：`TOTAL 150/0/3`（基线 149 + 新增 1），FAIL=0。

- [ ] **Step 5: Commit**

```bash
git add src/Platform/Native.Desktop.cs src/Ui/Theme.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "refactor: 解除 Platform 对 Ui.Theme 的依赖（主题查询钩子）"
```

---

### Task 2: UiShared 调色板

实现规格 §3.1 的全部颜色 Token。纯 C#，不依赖 WPF/WinForms，两个 exe 共用。

**Files:**
- Create: `src/UiShared/Palette.cs`
- Test: `tests/SelfTests.UiShared.cs`
- Modify: `tests/SelfTests.cs`（在 Task 1 注册行之后追加）

- [ ] **Step 1: 写失败测试**

`tests/SelfTests.UiShared.cs` 追加：

```csharp
        private static void TestPaletteCompleteness()
        {
            foreach (UiTone tone in new[] { UiTone.Light, UiTone.Dark })
            {
                ThemeColors c = Palette.For(tone);
                string[] all =
                {
                    c.Success, c.Warning, c.Danger, c.Info, c.Brand,
                    c.Background, c.Surface, c.SurfaceRaised,
                    c.Border, c.BorderSubtle,
                    c.TextPrimary, c.TextSecondary, c.TextTertiary
                };
                foreach (string hex in all)
                {
                    if (String.IsNullOrEmpty(hex)) throw new Exception("empty token in " + tone);
                    Eq(7, hex.Length);
                    Eq('#', hex[0]);
                }
            }
        }

        private static void TestPaletteSemantics()
        {
            // 语义色必须互不相同，且深浅主题的品牌色一致（规格 §3.1.1）
            ThemeColors l = Palette.For(UiTone.Light);
            if (l.Success == l.Warning || l.Warning == l.Danger || l.Danger == l.Info)
                throw new Exception("semantic colors must be distinct");
            Eq(Palette.For(UiTone.Light).Brand, Palette.For(UiTone.Dark).Brand);
            Eq("#D4A847", l.Brand);
        }

        private static void TestPaletteContrast()
        {
            // 正文与背景的对比度至少 4.5:1（WCAG AA 正文标准）
            foreach (UiTone tone in new[] { UiTone.Light, UiTone.Dark })
            {
                ThemeColors c = Palette.For(tone);
                double ratio = Contrast(c.TextPrimary, c.Background);
                if (ratio < 4.5) throw new Exception(tone + " text/background contrast " + ratio.ToString("0.00"));
                double sub = Contrast(c.TextSecondary, c.Surface);
                if (sub < 4.5) throw new Exception(tone + " secondary/surface contrast " + sub.ToString("0.00"));
            }
        }

        private static double Contrast(string hexA, string hexB)
        {
            double la = RelLum(hexA), lb = RelLum(hexB);
            if (la < lb) { double t = la; la = lb; lb = t; }
            return (la + 0.05) / (lb + 0.05);
        }

        private static double RelLum(string hex)
        {
            double r = Channel(hex.Substring(1, 2));
            double g = Channel(hex.Substring(3, 2));
            double b = Channel(hex.Substring(5, 2));
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double Channel(string hh)
        {
            double v = Convert.ToInt32(hh, 16) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
```

`tests/SelfTests.cs` 注册（Task 1 那行之后）：

```csharp
            test("调色板：深浅主题 13 个 Token 齐全且为合法 hex", TestPaletteCompleteness);
            test("调色板：语义色互异，品牌色跨主题固定为 #D4A847", TestPaletteSemantics);
            test("调色板：正文/次级文字与底色对比度达到 AA", TestPaletteContrast);
```

- [ ] **Step 2: 运行测试确认失败**

`cmd //c dev.cmd test`
预期：FAIL（`UiTone`/`ThemeColors`/`Palette` 不存在，编译错误）。

- [ ] **Step 3: 最小实现**

新建 `src/UiShared/Palette.cs`（色值严格按规格 §3.1）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 新 UI 的颜色 Token：语义色与中性色，深浅双主题（规格 §3.1）

namespace CaelusApp
{
    internal enum UiTone
    {
        Light,
        Dark
    }

    internal sealed class ThemeColors
    {
        public string Success;
        public string Warning;
        public string Danger;
        public string Info;
        public string Brand;
        public string Background;
        public string Surface;
        public string SurfaceRaised;
        public string Border;
        public string BorderSubtle;
        public string TextPrimary;
        public string TextSecondary;
        public string TextTertiary;
    }

    internal static class Palette
    {
        private static readonly ThemeColors light = new ThemeColors
        {
            Success = "#2F9E5F",
            Warning = "#D97706",
            Danger = "#DC2626",
            Info = "#2563EB",
            Brand = "#D4A847",
            Background = "#F5F7F9",
            Surface = "#FFFFFF",
            SurfaceRaised = "#FAFBFC",
            Border = "#D8E0E6",
            BorderSubtle = "#E8EDF1",
            TextPrimary = "#141F29",
            TextSecondary = "#6E7D89",
            TextTertiary = "#9AA6AE"
        };

        private static readonly ThemeColors dark = new ThemeColors
        {
            Success = "#4ADE80",
            Warning = "#FBBF24",
            Danger = "#F87171",
            Info = "#60A5FA",
            Brand = "#D4A847",
            Background = "#0F1419",
            Surface = "#161C22",
            SurfaceRaised = "#1A2028",
            Border = "#26313B",
            BorderSubtle = "#2E3A44",
            TextPrimary = "#E8EEF2",
            TextSecondary = "#9AA6AE",
            TextTertiary = "#6E7D89"
        };

        public static ThemeColors For(UiTone tone)
        {
            return tone == UiTone.Light ? light : dark;
        }
    }
}
```

注意：返回共享只读实例（字段为值语义使用，调用方不得修改；如需防止误改，后续可改只读属性，Phase 1 保持简单）。

- [ ] **Step 4: 运行测试确认通过**

`cmd //c dev.cmd test`
预期：`TOTAL 153/0/3`。

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/Palette.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "feat: UiShared 调色板（规格 §3.1 语义色+中性色，深浅双主题）"
```

---

### Task 3: UiMotion 动效 Token 与降级策略

实现规格 §6 的时长/缓动参数和减少动态效果策略。是否减少动效由宿主读取系统设置后传入（WPF 读 `SystemParameters.ClientAreaAnimation`），UiShared 只做纯策略。

**Files:**
- Create: `src/UiShared/UiMotion.cs`
- Test: `tests/SelfTests.UiShared.cs`
- Modify: `tests/SelfTests.cs`（追加注册）

- [ ] **Step 1: 写失败测试**

`tests/SelfTests.UiShared.cs` 追加：

```csharp
        private static void TestMotionTokens()
        {
            Eq(250, UiMotion.PageFadeMs);
            Eq(300, UiMotion.CardExpandMs);
            Eq(400, UiMotion.NumberRollMs);
            Eq(200, UiMotion.ToggleMs);
            Eq(250, UiMotion.ModalMs);
            Eq(400, UiMotion.SuccessPopMs);
        }

        private static void TestMotionReducedPolicy()
        {
            Eq(250, UiMotion.Duration(UiMotion.PageFadeMs, false));
            Eq(125, UiMotion.Duration(UiMotion.PageFadeMs, true));
            Eq(true, UiMotion.AllowsOffset(false));
            Eq(false, UiMotion.AllowsOffset(true));
        }
```

`tests/SelfTests.cs` 注册：

```csharp
            test("动效：规格 §6 六档时长 Token 固定", TestMotionTokens);
            test("动效：减少动态效果时时长减半且禁用位移", TestMotionReducedPolicy);
```

- [ ] **Step 2: 运行测试确认失败**

`cmd //c dev.cmd test`
预期：FAIL（`UiMotion` 不存在）。

- [ ] **Step 3: 最小实现**

新建 `src/UiShared/UiMotion.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 新 UI 的动效 Token 与减少动态效果策略（规格 §6）

namespace CaelusApp
{
    internal static class UiMotion
    {
        public const int PageFadeMs = 250;
        public const int CardExpandMs = 300;
        public const int NumberRollMs = 400;
        public const int ToggleMs = 200;
        public const int ModalMs = 250;
        public const int SuccessPopMs = 400;

        // 减少动态效果：时长减半（规格 §6.3 允许“时长×0.5 或直接禁用”）
        public static int Duration(int baseMs, bool reduced)
        {
            return reduced ? baseMs / 2 : baseMs;
        }

        // 位移动画在减少动态效果模式下禁用，仅保留透明度渐变
        public static bool AllowsOffset(bool reduced)
        {
            return !reduced;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

`cmd //c dev.cmd test`
预期：`TOTAL 155/0/3`。

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/UiMotion.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "feat: UiMotion 动效 Token 与减少动态效果策略（规格 §6）"
```

---

### Task 4: MVVM 基础设施

ViewModelBase 与 RelayCommand 只依赖 `System.ComponentModel` 与 `System.Windows.Input`（ICommand 在 .NET 4.x 的 System.dll 中），因此可以放在 UiShared 供两个 exe 共用。

**Files:**
- Create: `src/UiShared/ViewModelBase.cs`
- Create: `src/UiShared/RelayCommand.cs`
- Test: `tests/SelfTests.UiShared.cs`
- Modify: `tests/SelfTests.cs`（追加注册）

- [ ] **Step 1: 写失败测试**

`tests/SelfTests.UiShared.cs` 追加：

```csharp
        private sealed class ProbeVm : ViewModelBase
        {
            private int count;
            public int Count
            {
                get { return count; }
                set { SetProperty(ref count, value, "Count"); }
            }
        }

        private static void TestViewModelBase()
        {
            var vm = new ProbeVm();
            var changed = new System.Collections.Generic.List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
            vm.Count = 1;
            vm.Count = 1; // 同值不应重复触发
            vm.Count = 2;
            Eq(2, changed.Count);
            Eq("Count", changed[0]);
            Eq(2, vm.Count);
        }

        private static void TestRelayCommand()
        {
            int runs = 0;
            var can = new RelayCommand(() => runs++, () => false);
            Eq(false, can.CanExecute(null));
            can.Execute(null);
            Eq(0, runs);
            var go = new RelayCommand(() => runs++);
            Eq(true, go.CanExecute(null));
            go.Execute(null);
            Eq(1, runs);
        }
```

`tests/SelfTests.cs` 注册：

```csharp
            test("MVVM：SetProperty 同值静默、异值通知一次", TestViewModelBase);
            test("MVVM：RelayCommand 尊重 CanExecute 并执行委托", TestRelayCommand);
```

- [ ] **Step 2: 运行测试确认失败**

`cmd //c dev.cmd test`
预期：FAIL（类型不存在）。

- [ ] **Step 3: 最小实现**

新建 `src/UiShared/ViewModelBase.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 MVVM 基座：属性变更通知（供 WPF 绑定与自测共用）

using System.ComponentModel;

namespace CaelusApp
{
    internal abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, string name)
        {
            if (object.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
```

新建 `src/UiShared/RelayCommand.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 MVVM 基座：无参 ICommand 实现

using System;
using System.Windows.Input;

namespace CaelusApp
{
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action run;
        private readonly Func<bool> can;

        public RelayCommand(Action run) : this(run, null) { }

        public RelayCommand(Action run, Func<bool> can)
        {
            if (run == null) throw new ArgumentNullException("run");
            this.run = run;
            this.can = can;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return can == null || can();
        }

        public void Execute(object parameter)
        {
            if (CanExecute(parameter)) run();
        }

        public void RaiseCanExecuteChanged()
        {
            EventHandler h = CanExecuteChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
```

注意：WinForms 编译已引用 `System.dll`，`System.Windows.Input.ICommand` 自 .NET 4.0 起在 System.dll 中，无需新增程序集引用。

- [ ] **Step 4: 运行测试确认通过**

`cmd //c dev.cmd test`
预期：`TOTAL 157/0/3`。

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/ViewModelBase.cs src/UiShared/RelayCommand.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "feat: MVVM 基座（ViewModelBase / RelayCommand，双宿主共用）"
```

---

### Task 5: OverviewStatus 状态结论纯逻辑

实现规格 §5.1 的状态结论表与指标分级阈值。这是概览页“结论优先”的核心。

**Files:**
- Create: `src/UiShared/OverviewStatus.cs`
- Test: `tests/SelfTests.UiShared.cs`
- Modify: `tests/SelfTests.cs`（追加注册）

- [ ] **Step 1: 写失败测试**

`tests/SelfTests.UiShared.cs` 追加：

```csharp
        private static void TestOverviewConclusionRules()
        {
            // 规格 §5.1：守护关闭优先级最高，其次是危险、警告，再区分游戏中/就绪
            Eq(StatusLevel.Off, OverviewStatus.Conclude(false, false, false, false).Level);
            Eq(StatusLevel.Off, OverviewStatus.Conclude(false, true, true, true).Level);
            Eq(StatusLevel.Critical, OverviewStatus.Conclude(true, false, true, true).Level);
            Eq(StatusLevel.Attention, OverviewStatus.Conclude(true, false, true, false).Level);
            Eq(StatusLevel.Optimizing, OverviewStatus.Conclude(true, true, false, false).Level);
            Eq(StatusLevel.Ready, OverviewStatus.Conclude(true, false, false, false).Level);

            Eq("游戏环境已准备好", OverviewStatus.Conclude(true, false, false, false).Title);
            Eq("守护已关闭", OverviewStatus.Conclude(false, false, false, false).Title);
            Eq("游戏优化中", OverviewStatus.Conclude(true, true, false, false).Title);
        }

        private static void TestMetricLevels()
        {
            // GPU 温度阈值 75/85（规格 §5.1）
            Eq(MetricLevel.Ok, OverviewStatus.LevelFor(62, 75, 85));
            Eq(MetricLevel.Warning, OverviewStatus.LevelFor(75, 75, 85));
            Eq(MetricLevel.Warning, OverviewStatus.LevelFor(80, 75, 85));
            Eq(MetricLevel.Critical, OverviewStatus.LevelFor(85, 75, 85));
            Eq(MetricLevel.Critical, OverviewStatus.LevelFor(96, 75, 85));
            // 内存占用阈值 80/90（百分比）
            Eq(MetricLevel.Ok, OverviewStatus.LevelFor(53, 80, 90));
            Eq(MetricLevel.Warning, OverviewStatus.LevelFor(80, 80, 90));
            Eq(MetricLevel.Critical, OverviewStatus.LevelFor(90, 80, 90));
        }

        private static void TestConclusionColorKeys()
        {
            // Level → 语义色 Token 键名（XAML 用 DynamicResource 解析）
            Eq("Success", OverviewStatus.ColorKey(StatusLevel.Ready));
            Eq("Success", OverviewStatus.ColorKey(StatusLevel.Optimizing));
            Eq("Warning", OverviewStatus.ColorKey(StatusLevel.Attention));
            Eq("Warning", OverviewStatus.ColorKey(StatusLevel.Off));
            Eq("Danger", OverviewStatus.ColorKey(StatusLevel.Critical));
        }
```

`tests/SelfTests.cs` 注册：

```csharp
            test("概览结论：守护/危险/警告/游戏中共五种状态的优先级与文案", TestOverviewConclusionRules);
            test("概览指标：GPU 温度与内存占用的分级阈值", TestMetricLevels);
            test("概览结论：状态等级映射到语义色 Token 键", TestConclusionColorKeys);
```

- [ ] **Step 2: 运行测试确认失败**

`cmd //c dev.cmd test`
预期：FAIL（类型不存在）。

- [ ] **Step 3: 最小实现**

新建 `src/UiShared/OverviewStatus.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 概览页状态结论与指标分级的纯逻辑（规格 §5.1）

namespace CaelusApp
{
    internal enum StatusLevel
    {
        Ready,       // 守护开启且空闲：游戏环境已准备好
        Optimizing,  // 守护开启且游戏运行中
        Attention,   // 有警告项
        Critical,    // 有危险项
        Off          // 守护关闭
    }

    internal enum MetricLevel
    {
        Ok,
        Warning,
        Critical
    }

    internal sealed class StatusConclusion
    {
        public StatusLevel Level;
        public string Title;
        public string Detail;
        public string Glyph;
    }

    internal static class OverviewStatus
    {
        public const double GpuWarnC = 75;
        public const double GpuCritC = 85;
        public const double MemWarnPct = 80;
        public const double MemCritPct = 90;

        public static StatusConclusion Conclude(
            bool guardEnabled, bool gameActive, bool hasWarning, bool hasCritical)
        {
            if (!guardEnabled)
                return new StatusConclusion
                {
                    Level = StatusLevel.Off,
                    Title = "守护已关闭",
                    Detail = "打开守护后，Caelus 才能在你玩游戏时自动优化",
                    Glyph = "○"
                };
            if (hasCritical)
                return new StatusConclusion
                {
                    Level = StatusLevel.Critical,
                    Title = "需要处理",
                    Detail = "存在影响游戏体验的问题，展开详情查看",
                    Glyph = "✕"
                };
            if (hasWarning)
                return new StatusConclusion
                {
                    Level = StatusLevel.Attention,
                    Title = "需要注意",
                    Detail = "有项目值得关注，展开详情查看",
                    Glyph = "⚠"
                };
            if (gameActive)
                return new StatusConclusion
                {
                    Level = StatusLevel.Optimizing,
                    Title = "游戏优化中",
                    Detail = "检测到游戏正在运行，优化策略已生效",
                    Glyph = "✓"
                };
            return new StatusConclusion
            {
                Level = StatusLevel.Ready,
                Title = "游戏环境已准备好",
                Detail = "启动游戏后 Caelus 会自动接管",
                Glyph = "✓"
            };
        }

        public static MetricLevel LevelFor(double value, double warnAt, double critAt)
        {
            if (value >= critAt) return MetricLevel.Critical;
            if (value >= warnAt) return MetricLevel.Warning;
            return MetricLevel.Ok;
        }

        public static string ColorKey(StatusLevel level)
        {
            switch (level)
            {
                case StatusLevel.Ready:
                case StatusLevel.Optimizing:
                    return "Success";
                case StatusLevel.Attention:
                case StatusLevel.Off:
                    return "Warning";
                default:
                    return "Danger";
            }
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

`cmd //c dev.cmd test`
预期：`TOTAL 160/0/3`。

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/OverviewStatus.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "feat: 概览状态结论与指标分级纯逻辑（规格 §5.1）"
```

---

### Task 6: OverviewViewModel 与数据源抽象

`IOverviewSource` 隔离数据来源：示例数据源用于 `--wpf-shot` 截图与自测；真实数据源（包装 GameMode/Tamer）在 WPF 宿主启动时接线。Phase 1 的实时指标允许显示“—”（探测不可用时），完整指标接线属于后续阶段。

**Files:**
- Create: `src/UiShared/OverviewViewModel.cs`
- Test: `tests/SelfTests.UiShared.cs`
- Modify: `tests/SelfTests.cs`（追加注册）

- [ ] **Step 1: 写失败测试**

`tests/SelfTests.UiShared.cs` 追加：

```csharp
        private sealed class StubSource : IOverviewSource
        {
            public bool GuardEnabled = true;
            public bool GameActive;
            public bool HasWarning;
            public bool HasCritical;
            public double? GpuTempC = 62;
            public double? MemoryUsedPct = 53;
            public string MemoryUsedText = "8.4 GB";
            public string ModeText = "常规";
            public string LastCheckText = "上次检查 2 分钟前";

            bool IOverviewSource.GuardEnabled { get { return GuardEnabled; } }
            bool IOverviewSource.GameActive { get { return GameActive; } }
            bool IOverviewSource.HasWarning { get { return HasWarning; } }
            bool IOverviewSource.HasCritical { get { return HasCritical; } }
            double? IOverviewSource.GpuTempC { get { return GpuTempC; } }
            double? IOverviewSource.MemoryUsedPct { get { return MemoryUsedPct; } }
            string IOverviewSource.MemoryUsedText { get { return MemoryUsedText; } }
            string IOverviewSource.ModeText { get { return ModeText; } }
            string IOverviewSource.LastCheckText { get { return LastCheckText; } }
        }

        private static void TestOverviewViewModelMapping()
        {
            var src = new StubSource();
            var vm = new OverviewViewModel(src);
            vm.Refresh();
            Eq("游戏环境已准备好", vm.ConclusionTitle);
            Eq("Success", vm.ConclusionColorKey);
            Eq("常规", vm.ModeText);
            Eq(3, vm.Metrics.Count);
            Eq("GPU 温度", vm.Metrics[0].Label);
            Eq("62°", vm.Metrics[0].ValueText);
            Eq("Success", vm.Metrics[0].ColorKey);
            Eq("8.4 GB", vm.Metrics[2].ValueText);

            src.GuardEnabled = false;
            vm.Refresh();
            Eq("守护已关闭", vm.ConclusionTitle);
            Eq("Warning", vm.ConclusionColorKey);
        }

        private static void TestOverviewViewModelUnavailableMetrics()
        {
            var src = new StubSource();
            src.GpuTempC = null;
            src.MemoryUsedPct = null;
            src.MemoryUsedText = null;
            var vm = new OverviewViewModel(src);
            vm.Refresh();
            Eq("—", vm.Metrics[0].ValueText);
            Eq("Info", vm.Metrics[0].ColorKey);
            Eq("—", vm.Metrics[2].ValueText);
        }

        private static void TestOverviewDetailToggle()
        {
            var vm = new OverviewViewModel(new StubSource());
            Eq(false, vm.DetailVisible);
            vm.ToggleDetailCommand.Execute(null);
            Eq(true, vm.DetailVisible);
            vm.ToggleDetailCommand.Execute(null);
            Eq(false, vm.DetailVisible);
        }
```

`tests/SelfTests.cs` 注册：

```csharp
            test("概览 VM：数据源映射为结论/指标/颜色键", TestOverviewViewModelMapping);
            test("概览 VM：探测不可用时指标显示 — 且不着语义色", TestOverviewViewModelUnavailableMetrics);
            test("概览 VM：查看详情命令往返切换", TestOverviewDetailToggle);
```

- [ ] **Step 2: 运行测试确认失败**

`cmd //c dev.cmd test`
预期：FAIL（类型不存在）。

- [ ] **Step 3: 最小实现**

新建 `src/UiShared/OverviewViewModel.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 概览页 ViewModel 与数据源抽象（规格 §5.1 结论优先 + 渐进披露）

using System.Collections.ObjectModel;

namespace CaelusApp
{
    internal interface IOverviewSource
    {
        bool GuardEnabled { get; }
        bool GameActive { get; }
        bool HasWarning { get; }
        bool HasCritical { get; }
        double? GpuTempC { get; }
        double? MemoryUsedPct { get; }
        string MemoryUsedText { get; }
        string ModeText { get; }
        string LastCheckText { get; }
    }

    internal sealed class MetricViewModel
    {
        public string Label;
        public string ValueText;
        public double Fraction;      // 0..1，进度条；不可用时为 0
        public string ColorKey;      // Success / Warning / Danger / Info
    }

    internal sealed class OverviewViewModel : ViewModelBase
    {
        private readonly IOverviewSource source;
        private string conclusionTitle = "";
        private string conclusionDetail = "";
        private string conclusionGlyph = "";
        private string conclusionColorKey = "Info";
        private string modeText = "";
        private string lastCheckText = "";
        private bool detailVisible;

        public OverviewViewModel(IOverviewSource source)
        {
            this.source = source;
            Metrics = new ObservableCollection<MetricViewModel>();
            ToggleDetailCommand = new RelayCommand(
                () => { DetailVisible = !DetailVisible; });
        }

        public ObservableCollection<MetricViewModel> Metrics { get; private set; }
        public RelayCommand ToggleDetailCommand { get; private set; }

        public string ConclusionTitle
        {
            get { return conclusionTitle; }
            private set { SetProperty(ref conclusionTitle, value, "ConclusionTitle"); }
        }

        public string ConclusionDetail
        {
            get { return conclusionDetail; }
            private set { SetProperty(ref conclusionDetail, value, "ConclusionDetail"); }
        }

        public string ConclusionGlyph
        {
            get { return conclusionGlyph; }
            private set { SetProperty(ref conclusionGlyph, value, "ConclusionGlyph"); }
        }

        public string ConclusionColorKey
        {
            get { return conclusionColorKey; }
            private set { SetProperty(ref conclusionColorKey, value, "ConclusionColorKey"); }
        }

        public string ModeText
        {
            get { return modeText; }
            private set { SetProperty(ref modeText, value, "ModeText"); }
        }

        public string LastCheckText
        {
            get { return lastCheckText; }
            private set { SetProperty(ref lastCheckText, value, "LastCheckText"); }
        }

        public bool DetailVisible
        {
            get { return detailVisible; }
            private set { SetProperty(ref detailVisible, value, "DetailVisible"); }
        }

        public void Refresh()
        {
            StatusConclusion c = OverviewStatus.Conclude(
                source.GuardEnabled, source.GameActive, source.HasWarning, source.HasCritical);
            ConclusionTitle = c.Title;
            ConclusionDetail = c.Detail;
            ConclusionGlyph = c.Glyph;
            ConclusionColorKey = OverviewStatus.ColorKey(c.Level);
            ModeText = source.ModeText ?? "";
            LastCheckText = source.LastCheckText ?? "";

            Metrics.Clear();
            Metrics.Add(TempMetric("GPU 温度", source.GpuTempC, "°",
                OverviewStatus.GpuWarnC, OverviewStatus.GpuCritC, 110.0));
            Metrics.Add(new MetricViewModel
            {
                Label = "目标帧率",
                ValueText = "—",
                Fraction = 0,
                ColorKey = "Info"
            });
            Metrics.Add(MemoryMetric(source));
        }

        private static MetricViewModel TempMetric(
            string label, double? value, string unit,
            double warnAt, double critAt, double scaleMax)
        {
            if (!value.HasValue)
                return new MetricViewModel { Label = label, ValueText = "—", Fraction = 0, ColorKey = "Info" };
            double v = value.Value;
            MetricLevel lv = OverviewStatus.LevelFor(v, warnAt, critAt);
            return new MetricViewModel
            {
                Label = label,
                ValueText = ((int)System.Math.Round(v)) + unit,
                Fraction = v / scaleMax > 1 ? 1 : v / scaleMax,
                ColorKey = lv == MetricLevel.Ok ? "Success"
                    : lv == MetricLevel.Warning ? "Warning" : "Danger"
            };
        }

        private static MetricViewModel MemoryMetric(IOverviewSource src)
        {
            if (!src.MemoryUsedPct.HasValue || src.MemoryUsedText == null)
                return new MetricViewModel { Label = "已用内存", ValueText = "—", Fraction = 0, ColorKey = "Info" };
            double pct = src.MemoryUsedPct.Value;
            MetricLevel lv = OverviewStatus.LevelFor(
                pct, OverviewStatus.MemWarnPct, OverviewStatus.MemCritPct);
            return new MetricViewModel
            {
                Label = "已用内存",
                ValueText = src.MemoryUsedText,
                Fraction = pct / 100.0 > 1 ? 1 : pct / 100.0,
                ColorKey = lv == MetricLevel.Ok ? "Success"
                    : lv == MetricLevel.Warning ? "Warning" : "Danger"
            };
        }
    }

    // 示例数据源：供 --wpf-shot 截图与手动预览使用
    internal sealed class SampleOverviewSource : IOverviewSource
    {
        public bool GuardEnabled { get { return true; } }
        public bool GameActive { get { return false; } }
        public bool HasWarning { get { return false; } }
        public bool HasCritical { get { return false; } }
        public double? GpuTempC { get { return 62; } }
        public double? MemoryUsedPct { get { return 53; } }
        public string MemoryUsedText { get { return "8.4 GB"; } }
        public string ModeText { get { return "常规"; } }
        public string LastCheckText { get { return "上次检查 2 分钟前 · 没有需要处理的问题"; } }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

`cmd //c dev.cmd test`
预期：`TOTAL 163/0/3`。

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/OverviewViewModel.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "feat: 概览 ViewModel 与数据源抽象（含示例数据源）"
```

---

### Task 7: WPF 项目骨架与构建脚本

建立可编译的 WPF 项目。本机无 targeting pack，所以用 `TargetFrameworkVersion=v4.0` + 显式 HintPath。这是整个 Phase 1 风险最高的任务，先做构建验证再写任何 UI。

**Files:**
- Create: `wpf/Properties/AssemblyInfo.cs`
- Create: `wpf/app.manifest`
- Create: `wpf/App.xaml`、`wpf/App.xaml.cs`
- Create: `wpf/MainWindow.xaml`、`wpf/MainWindow.xaml.cs`
- Create: `wpf/Caelus.Wpf.csproj`
- Create: `build-wpf.cmd`

- [ ] **Step 1: 创建项目文件**

`wpf/Properties/AssemblyInfo.cs`：

```csharp
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: AssemblyTitle("Caelus")]
[assembly: AssemblyProduct("Caelus")]
[assembly: AssemblyCompany("zenjiro")]
[assembly: AssemblyCopyright("Copyright © zenjiro 2026")]
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]
[assembly: AssemblyInformationalVersion("1.7.0")]
[assembly: ComVisible(false)]
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]
```

`wpf/app.manifest`（内容与 build.cmd 第 38-53 行生成的完全一致）：

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

`wpf/App.xaml`：

```xml
<Application x:Class="CaelusApp.WpfHost.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</Application>
```

`wpf/App.xaml.cs`（占位入口，Task 8 起填充主题装载与截图探针）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主入口

using System.Windows;

namespace CaelusApp.WpfHost
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var w = new MainWindow();
            w.Show();
        }
    }
}
```

`wpf/MainWindow.xaml`（占位窗口，Task 9 替换成外壳）：

```xml
<Window x:Class="CaelusApp.WpfHost.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Caelus" Width="1196" Height="768"
        WindowStartupLocation="CenterScreen">
    <Grid/>
</Window>
```

`wpf/MainWindow.xaml.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主主窗口

using System.Windows;

namespace CaelusApp.WpfHost
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
```

`wpf/Caelus.Wpf.csproj`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="4.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Release</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{8F2B6A31-7C4E-4E9E-9A2E-2C8C0E0A0001}</ProjectGuid>
    <ProjectTypeGuids>{60dc8134-eba5-43b8-bcc9-bb4bc16c2548};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <OutputType>WinExe</OutputType>
    <RootNamespace>CaelusApp.WpfHost</RootNamespace>
    <AssemblyName>CaelusWpf</AssemblyName>
    <TargetFrameworkVersion>v4.0</TargetFrameworkVersion>
    <TargetFrameworkProfile />
    <OutputPath>bin\Release\</OutputPath>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon Condition="Exists('..\Caelus.ico')">..\Caelus.ico</ApplicationIcon>
    <WarningLevel>4</WarningLevel>
    <Optimize>true</Optimize>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System" />
    <Reference Include="System.Core" />
    <Reference Include="System.Drawing" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System.Management" />
    <Reference Include="System.Xml" />
    <Reference Include="System.Xaml" />
    <Reference Include="WindowsBase">
      <HintPath>$(WINDIR)\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="PresentationCore">
      <HintPath>$(WINDIR)\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="PresentationFramework">
      <HintPath>$(WINDIR)\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <ApplicationDefinition Include="App.xaml" />
    <Page Include="MainWindow.xaml" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Properties\AssemblyInfo.cs" />
    <Compile Include="App.xaml.cs">
      <DependentUpon>App.xaml</DependentUpon>
    </Compile>
    <Compile Include="MainWindow.xaml.cs">
      <DependentUpon>MainWindow.xaml</DependentUpon>
    </Compile>
    <Compile Include="..\src\Core\**\*.cs" />
    <Compile Include="..\src\Platform\**\*.cs" />
    <Compile Include="..\src\UiShared\**\*.cs" />
    <None Include="app.manifest" />
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

注意：`..\src\Core\**\*.cs` 采用链接式通配编译，新增 Core 文件自动进入两个构建；若 MSBuild 4.0 对跨目录 `**` 处理异常，降级为逐目录 `<Compile Include="..\src\Core\*.cs" />` 等显式列出（执行时以实际编译结果为准）。

`build-wpf.cmd`（ASCII ONLY）：

```bat
@rem @author zenjiro 18967498922@163.com
@rem file: build the WPF preview host (CaelusWpf.exe)
@rem ASCII ONLY - see build.cmd header for the codepage trap.
@echo off
setlocal
cd /d "%~dp0"
set MSB=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe
if not exist "%MSB%" (
    echo MSBuild.exe not found - install .NET Framework 4.x
    exit /b 1
)
"%MSB%" wpf\Caelus.Wpf.csproj /p:Configuration=Release /v:m /nologo
if errorlevel 1 (
    echo WPF build failed
    exit /b 1
)
echo.
echo WPF Build OK -^> wpf\bin\Release\CaelusWpf.exe
```

注意：图标通过 csproj 中 `ApplicationIcon` 的 `Exists('..\Caelus.ico')` 条件引用——若先前跑过 `build.cmd` 生成了 `Caelus.ico` 则自动复用，否则跳过；脚本本身不需要图标处理逻辑。全文件保持 ASCII。

- [ ] **Step 2: 构建 WPF 项目**

Git Bash 执行：`cmd //c build-wpf.cmd`
预期：`WPF Build OK -> wpf\bin\Release\CaelusWpf.exe`。若出现 MSB3644（缺 targeting pack 警告）可忽略；若出现引用解析错误，检查 HintPath 与实际 framework 路径。

- [ ] **Step 3: 冒烟运行**

`./wpf/bin/Release/CaelusWpf.exe`（会请求管理员权限，属预期）
预期：空白窗口弹出，标题 Caelus，无异常。关闭窗口。

- [ ] **Step 4: 确认既有构建无回归**

`cmd //c dev.cmd test`
预期：`TOTAL 163/0/3`（与 Task 6 结束一致）。

- [ ] **Step 5: Commit**

```bash
git add wpf/ build-wpf.cmd
git commit -m "feat: WPF 预览宿主骨架（net40 目标 + 显式 HintPath，链接 Core/Platform/UiShared）"
```

---

### Task 8: 主题资源字典与 ThemeManager

把 Task 2 的调色板落成 XAML 资源，支持运行时切换深浅主题。

**Files:**
- Create: `wpf/Themes/Colors.Light.xaml`、`wpf/Themes/Colors.Dark.xaml`
- Create: `wpf/Themes/Tokens.xaml`
- Create: `wpf/ThemeManager.cs`
- Modify: `wpf/App.xaml`、`wpf/App.xaml.cs`
- Modify: `wpf/Caelus.Wpf.csproj`（注册新文件）

- [ ] **Step 1: 创建颜色字典**

`wpf/Themes/Colors.Light.xaml`（色值与 `Palette.For(UiTone.Light)` 完全一致）：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Color x:Key="SuccessColor">#2F9E5F</Color>
  <Color x:Key="WarningColor">#D97706</Color>
  <Color x:Key="DangerColor">#DC2626</Color>
  <Color x:Key="InfoColor">#2563EB</Color>
  <Color x:Key="BrandColor">#D4A847</Color>
  <Color x:Key="BackgroundColor">#F5F7F9</Color>
  <Color x:Key="SurfaceColor">#FFFFFF</Color>
  <Color x:Key="SurfaceRaisedColor">#FAFBFC</Color>
  <Color x:Key="BorderColor">#D8E0E6</Color>
  <Color x:Key="BorderSubtleColor">#E8EDF1</Color>
  <Color x:Key="TextPrimaryColor">#141F29</Color>
  <Color x:Key="TextSecondaryColor">#6E7D89</Color>
  <Color x:Key="TextTertiaryColor">#9AA6AE</Color>
  <SolidColorBrush x:Key="SuccessBrush" Color="{StaticResource SuccessColor}"/>
  <SolidColorBrush x:Key="WarningBrush" Color="{StaticResource WarningColor}"/>
  <SolidColorBrush x:Key="DangerBrush" Color="{StaticResource DangerColor}"/>
  <SolidColorBrush x:Key="InfoBrush" Color="{StaticResource InfoColor}"/>
  <SolidColorBrush x:Key="BrandBrush" Color="{StaticResource BrandColor}"/>
  <SolidColorBrush x:Key="BackgroundBrush" Color="{StaticResource BackgroundColor}"/>
  <SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource SurfaceColor}"/>
  <SolidColorBrush x:Key="SurfaceRaisedBrush" Color="{StaticResource SurfaceRaisedColor}"/>
  <SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}"/>
  <SolidColorBrush x:Key="BorderSubtleBrush" Color="{StaticResource BorderSubtleColor}"/>
  <SolidColorBrush x:Key="TextPrimaryBrush" Color="{StaticResource TextPrimaryColor}"/>
  <SolidColorBrush x:Key="TextSecondaryBrush" Color="{StaticResource TextSecondaryColor}"/>
  <SolidColorBrush x:Key="TextTertiaryBrush" Color="{StaticResource TextTertiaryColor}"/>
</ResourceDictionary>
```

`wpf/Themes/Colors.Dark.xaml`：同上结构，色值替换为 `Palette.For(UiTone.Dark)` 的 13 个值。

- [ ] **Step 2: 创建 Token 字典**

`wpf/Themes/Tokens.xaml`（规格 §3.2/§3.3；WPF 尺寸单位为 1/96 英寸，字号直接用数值）：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <FontFamily x:Key="FontUi">Microsoft YaHei UI</FontFamily>
  <FontFamily x:Key="FontMono">Consolas</FontFamily>
  <sys:Double x:Key="FontSizeDisplay" xmlns:sys="clr-namespace:System;assembly=mscorlib">24</sys:Double>
  <sys:Double x:Key="FontSizeTitle" xmlns:sys="clr-namespace:System;assembly=mscorlib">18</sys:Double>
  <sys:Double x:Key="FontSizeBody" xmlns:sys="clr-namespace:System;assembly=mscorlib">14</sys:Double>
  <sys:Double x:Key="FontSizeCaption" xmlns:sys="clr-namespace:System;assembly=mscorlib">12</sys:Double>
  <sys:Double x:Key="FontSizeMono" xmlns:sys="clr-namespace:System;assembly=mscorlib">13</sys:Double>
  <sys:Double x:Key="SpaceXs" xmlns:sys="clr-namespace:System;assembly=mscorlib">4</sys:Double>
  <sys:Double x:Key="SpaceSm" xmlns:sys="clr-namespace:System;assembly=mscorlib">8</sys:Double>
  <sys:Double x:Key="SpaceMd" xmlns:sys="clr-namespace:System;assembly=mscorlib">12</sys:Double>
  <sys:Double x:Key="SpaceLg" xmlns:sys="clr-namespace:System;assembly=mscorlib">16</sys:Double>
  <sys:Double x:Key="SpaceXl" xmlns:sys="clr-namespace:System;assembly=mscorlib">24</sys:Double>
  <CornerRadius x:Key="RadiusSm">6</CornerRadius>
  <CornerRadius x:Key="RadiusMd">10</CornerRadius>
  <CornerRadius x:Key="RadiusLg">14</CornerRadius>
</ResourceDictionary>
```

注意：`xmlns:sys` 重复声明是为了每个 Double 独立可用；也可以在根节点声明一次后省略内联声明（执行时保持一种即可）。

- [ ] **Step 3: ThemeManager 与 App 接线**

`wpf/ThemeManager.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 宿主主题切换：替换应用级颜色资源字典

using System;
using System.Windows;

namespace CaelusApp.WpfHost
{
    internal static class ThemeManager
    {
        private static ResourceDictionary colors;

        public static UiTone Current { get; private set; }

        public static void Apply(Application app, UiTone tone)
        {
            string uri = tone == UiTone.Light
                ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml";
            var next = new ResourceDictionary
            {
                Source = new Uri(uri, UriKind.Relative)
            };
            var merged = app.Resources.MergedDictionaries;
            if (colors != null) merged.Remove(colors);
            merged.Add(next);
            colors = next;
            Current = tone;
            Native.LightModeQuery = () => tone == UiTone.Light;
        }
    }
}
```

`wpf/App.xaml` 改为：

```xml
<Application x:Class="CaelusApp.WpfHost.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Themes/Tokens.xaml"/>
        <ResourceDictionary Source="Themes/Styles.xaml"/>
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

`wpf/App.xaml.cs` 的 `OnStartup` 开头插入（在 `new MainWindow()` 之前）：

```csharp
            ThemeManager.Apply(this, UiTone.Light);
```

`wpf/Caelus.Wpf.csproj` 的 Page 列表追加（Styles.xaml 由 Task 9 创建，此处先注册 Colors/Tokens）：

```xml
    <Page Include="Themes\Tokens.xaml" />
    <Page Include="Themes\Colors.Light.xaml" />
    <Page Include="Themes\Colors.Dark.xaml" />
```

Compile 列表追加：

```xml
    <Compile Include="ThemeManager.cs" />
```

- [ ] **Step 4: 构建验证**

`cmd //c build-wpf.cmd`
预期：`WPF Build OK`。注意 Styles.xaml 尚未创建，App.xaml 中对它的引用会导致编译失败——先临时移除该行，或在 Task 9 Step 1 创建 Styles.xaml 后一并验证。**执行顺序：先临时注释掉 Styles.xaml 行，本任务验证 Colors/Tokens 装载；Task 9 再恢复。**

- [ ] **Step 5: Commit**

```bash
git add wpf/
git commit -m "feat: WPF 主题资源字典（深浅色板 + 字体/间距/圆角 Token）与 ThemeManager"
```

---

### Task 9: 主窗口外壳（标题栏 / NavRail / 分段控件）

实现规格 §4.5/§4.6 与 §5.1 的外壳结构。未迁移页面使用占位视图。

**Files:**
- Create: `wpf/Themes/Styles.xaml`
- Create: `wpf/Views/OverviewView.xaml`、`wpf/Views/OverviewView.xaml.cs`（本任务仅空壳，Task 10 填充）
- Create: `wpf/Views/PlaceholderView.xaml`、`wpf/Views/PlaceholderView.xaml.cs`
- Modify: `wpf/MainWindow.xaml`、`wpf/MainWindow.xaml.cs`
- Modify: `wpf/Caelus.Wpf.csproj`
- Modify: `wpf/App.xaml`（恢复 Styles.xaml 引用）

- [ ] **Step 1: 创建样式字典**

`wpf/Themes/Styles.xaml`（实现规格 §4.1/§4.5/§4.6 的按钮、导航项、分段控件、卡片）：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- 卡片（规格 §4.3） -->
  <Style x:Key="CardBorder" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusMd}"/>
    <Setter Property="Padding" Value="12"/>
  </Style>

  <!-- 主按钮（规格 §4.1 Primary） -->
  <Style x:Key="PrimaryButton" TargetType="Button">
    <Setter Property="Background" Value="{DynamicResource InfoBrush}"/>
    <Setter Property="Foreground" Value="White"/>
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

  <!-- 幽灵按钮（规格 §4.1 Ghost：展开/收起等轻量操作） -->
  <Style x:Key="GhostButton" TargetType="Button">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{DynamicResource InfoBrush}"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="6,4"/>
    <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
    <Setter Property="Cursor" Value="Hand"/>
  </Style>

  <!-- 导航项（规格 §4.5）：RadioButton 扮演选中态 -->
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
                  Background="Transparent">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource SurfaceRaisedBrush}"/>
              <Setter Property="Foreground" Value="{DynamicResource InfoBrush}"/>
              <Setter Property="FontWeight" Value="SemiBold"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource SurfaceRaisedBrush}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 分段控件项（规格 §4.6） -->
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
              <Setter TargetName="bd" Property="Background" Value="{DynamicResource SurfaceBrush}"/>
              <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
              <Setter Property="FontWeight" Value="SemiBold"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 分段控件容器 -->
  <Style x:Key="SegmentHost" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource SurfaceRaisedBrush}"/>
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}"/>
    <Setter Property="Padding" Value="3"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
  </Style>
</ResourceDictionary>
```

恢复 `wpf/App.xaml` 中 Styles.xaml 的引用（若 Task 8 临时移除了）。

- [ ] **Step 2: 占位视图与概览空壳**

`wpf/Views/PlaceholderView.xaml`：

```xml
<UserControl x:Class="CaelusApp.WpfHost.Views.PlaceholderView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <TextBlock x:Name="Hint" Text="该页面将在后续阶段迁移"
             Foreground="{DynamicResource TextTertiaryBrush}"
             FontSize="{DynamicResource FontSizeBody}"
             HorizontalAlignment="Center" VerticalAlignment="Center"/>
</UserControl>
```

`wpf/Views/PlaceholderView.xaml.cs`：

```csharp
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class PlaceholderView : UserControl
    {
        public PlaceholderView()
        {
            InitializeComponent();
        }
    }
}
```

`wpf/Views/OverviewView.xaml`（本任务先放空 Grid）：

```xml
<UserControl x:Class="CaelusApp.WpfHost.Views.OverviewView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Grid/>
</UserControl>
```

`wpf/Views/OverviewView.xaml.cs`：

```csharp
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class OverviewView : UserControl
    {
        public OverviewView()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 3: 主窗口外壳**

`wpf/MainWindow.xaml` 替换为：

```xml
<Window x:Class="CaelusApp.WpfHost.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:CaelusApp.WpfHost.Views"
        Title="Caelus" Width="1196" Height="768"
        WindowStartupLocation="CenterScreen"
        WindowStyle="None" AllowsTransparency="False" ResizeMode="CanResizeWithGrip"
        Background="{DynamicResource BackgroundBrush}"
        FontFamily="{DynamicResource FontUi}">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="38"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <!-- 标题栏 -->
    <Border Grid.Row="0" Background="{DynamicResource SurfaceBrush}"
            BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="0,0,0,1"
            MouseLeftButtonDown="TitleBarDrag">
      <DockPanel LastChildFill="False">
        <TextBlock Text="CAELUS · OVERVIEW" VerticalAlignment="Center" Margin="14,0,0,0"
                   Foreground="{DynamicResource TextTertiaryBrush}" FontSize="10"/>
        <Button x:Name="CloseBtn" DockPanel.Dock="Right" Content="✕" Width="46"
                Click="CloseClick" Background="Transparent" BorderThickness="0"
                Foreground="{DynamicResource TextSecondaryBrush}"/>
        <Button x:Name="MinBtn" DockPanel.Dock="Right" Content="—" Width="40"
                Click="MinClick" Background="Transparent" BorderThickness="0"
                Foreground="{DynamicResource TextSecondaryBrush}"/>
      </DockPanel>
    </Border>

    <Grid Grid.Row="1">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="200"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>

      <!-- NavRail（规格 §4.5） -->
      <Border Grid.Column="0" Background="{DynamicResource BackgroundBrush}"
              BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="0,0,1,0">
        <StackPanel Margin="0,14,0,0">
          <TextBlock Text="caelus" FontWeight="Bold" FontSize="16" Margin="18,0,0,20"
                     Foreground="{DynamicResource TextPrimaryBrush}"/>
          <RadioButton x:Name="NavOverview" Style="{DynamicResource NavItem}"
                       Content="概览" IsChecked="True" GroupName="nav" Checked="NavChecked"/>
          <RadioButton Style="{DynamicResource NavItem}" Content="游戏库" GroupName="nav" Checked="NavChecked"/>
          <RadioButton Style="{DynamicResource NavItem}" Content="优化策略" GroupName="nav" Checked="NavChecked"/>
          <RadioButton Style="{DynamicResource NavItem}" Content="系统体检" GroupName="nav" Checked="NavChecked"/>
          <RadioButton Style="{DynamicResource NavItem}" Content="日志" GroupName="nav" Checked="NavChecked"/>
          <RadioButton Style="{DynamicResource NavItem}" Content="设置" GroupName="nav" Checked="NavChecked"/>
        </StackPanel>
      </Border>

      <!-- 内容区 -->
      <Grid Grid.Column="1" Margin="22,18,22,16">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <DockPanel Grid.Row="0" Margin="0,0,0,14">
          <StackPanel>
            <TextBlock Text="系统状态" FontSize="{DynamicResource FontSizeDisplay}"
                       FontWeight="SemiBold" Foreground="{DynamicResource TextPrimaryBrush}"/>
            <TextBlock Text="{Binding LastCheckText}" FontSize="{DynamicResource FontSizeCaption}"
                       Foreground="{DynamicResource TextSecondaryBrush}" Margin="0,2,0,0"/>
          </StackPanel>
          <!-- 分段控件：模式选择（规格 §4.6，替代浮动弹层） -->
          <Border Style="{DynamicResource SegmentHost}" DockPanel.Dock="Right"
                  VerticalAlignment="Top">
            <StackPanel Orientation="Horizontal">
              <RadioButton Style="{DynamicResource SegmentItem}" Content="常规"
                           IsChecked="True" GroupName="mode"/>
              <RadioButton Style="{DynamicResource SegmentItem}" Content="竞技" GroupName="mode"/>
              <RadioButton Style="{DynamicResource SegmentItem}" Content="自定义" GroupName="mode"/>
            </StackPanel>
          </Border>
        </DockPanel>

        <ContentControl x:Name="PageHost" Grid.Row="1"/>
      </Grid>
    </Grid>
  </Grid>
</Window>
```

`wpf/MainWindow.xaml.cs` 替换为：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主主窗口外壳：标题栏 / NavRail / 内容宿主

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaelusApp.WpfHost.Views;

namespace CaelusApp.WpfHost
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            PageHost.Content = new OverviewView();
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
            var rb = sender as RadioButton;
            if (rb == null || PageHost == null) return;
            PageHost.Content = rb == NavOverview
                ? (object)new OverviewView()
                : new PlaceholderView();
        }
    }
}
```

`wpf/Caelus.Wpf.csproj` 追加：

```xml
    <Page Include="Themes\Styles.xaml" />
    <Page Include="Views\OverviewView.xaml" />
    <Page Include="Views\PlaceholderView.xaml" />
```

Compile 追加：

```xml
    <Compile Include="Views\OverviewView.xaml.cs">
      <DependentUpon>Views\OverviewView.xaml</DependentUpon>
    </Compile>
    <Compile Include="Views\PlaceholderView.xaml.cs">
      <DependentUpon>Views\PlaceholderView.xaml</DependentUpon>
    </Compile>
```

- [ ] **Step 4: 构建并冒烟**

`cmd //c build-wpf.cmd`，然后运行 `./wpf/bin/Release/CaelusWpf.exe`
预期：窗口显示标题栏、左侧导航（概览选中高亮）、顶部“常规/竞技/自定义”分段控件；点击其他导航项显示占位文案；最小化/关闭/拖动可用。

- [ ] **Step 5: Commit**

```bash
git add wpf/
git commit -m "feat: WPF 外壳——标题栏 / NavRail / 分段控件 / 占位页"
```

---

### Task 10: 概览视图与截图探针

实现规格 §5.1 的概览页，并加入 `--wpf-shot` 探针输出深浅主题 PNG 用于视觉验收。

**Files:**
- Modify: `wpf/Views/OverviewView.xaml`、`wpf/Views/OverviewView.xaml.cs`
- Modify: `wpf/App.xaml.cs`
- Modify: `wpf/MainWindow.xaml.cs`（暴露主题切换供探针使用）

- [ ] **Step 1: 概览视图**

`wpf/Views/OverviewView.xaml` 替换为以下内容。绑定 OverviewViewModel；语义颜色键经转换器解析为当前主题的 `DynamicResource` 画刷（转换器代码紧随其后，一并在本步创建）；进度条用「比例列宽 Grid」实现，`Fraction` 经 `FractionGridLengthConverter` 转成 `GridLength(f, Star)`：

```xml
<UserControl x:Class="CaelusApp.WpfHost.Views.OverviewView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:CaelusApp.WpfHost">
  <UserControl.Resources>
    <local:KeyBrushConverter x:Key="KeyBrush"/>
    <local:KeySoftBrushConverter x:Key="KeySoftBrush"/>
    <BooleanToVisibilityConverter x:Key="BoolVis"/>
    <local:DetailButtonTextConverter x:Key="DetailText"/>
    <local:FractionGridLengthConverter x:Key="FracLen"/>
  </UserControl.Resources>
  <StackPanel>
    <Border Style="{DynamicResource CardBorder}" Padding="16,14">
      <DockPanel>
        <Border Width="32" Height="32" CornerRadius="16" VerticalAlignment="Center"
                Background="{Binding ConclusionColorKey, Converter={StaticResource KeySoftBrush}}">
          <TextBlock Text="{Binding ConclusionGlyph}" HorizontalAlignment="Center"
                     VerticalAlignment="Center" FontSize="15"
                     Foreground="{Binding ConclusionColorKey, Converter={StaticResource KeyBrush}}"/>
        </Border>
        <Button Style="{DynamicResource GhostButton}" DockPanel.Dock="Right"
                VerticalAlignment="Center"
                Content="{Binding DetailVisible, Converter={StaticResource DetailText}}"
                Command="{Binding ToggleDetailCommand}"/>
        <StackPanel Margin="12,0,0,0" VerticalAlignment="Center">
          <TextBlock Text="{Binding ConclusionTitle}"
                     FontSize="{DynamicResource FontSizeTitle}" FontWeight="SemiBold"
                     Foreground="{DynamicResource TextPrimaryBrush}"/>
          <TextBlock Text="{Binding ConclusionDetail}"
                     FontSize="{DynamicResource FontSizeCaption}"
                     Foreground="{DynamicResource TextSecondaryBrush}" Margin="0,3,0,0"/>
        </StackPanel>
      </DockPanel>
    </Border>

    <ItemsControl ItemsSource="{Binding Metrics}" Margin="0,10,0,0">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
          <UniformGrid Columns="3"/>
        </ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <Border Style="{DynamicResource CardBorder}" Margin="0,0,8,0">
            <StackPanel>
              <TextBlock Text="{Binding ValueText}" FontSize="18" FontWeight="SemiBold"
                         Foreground="{DynamicResource TextPrimaryBrush}"/>
              <TextBlock Text="{Binding Label}" FontSize="{DynamicResource FontSizeCaption}"
                         Foreground="{DynamicResource TextSecondaryBrush}" Margin="0,3,0,0"/>
              <Grid Height="4" Margin="0,9,0,0">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="{Binding Fraction, Converter={StaticResource FracLen}}"/>
                  <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Border Grid.ColumnSpan="2" CornerRadius="2"
                        Background="{DynamicResource BorderSubtleBrush}"/>
                <Border Grid.Column="0" CornerRadius="2" HorizontalAlignment="Stretch"
                        Background="{Binding ColorKey, Converter={StaticResource KeyBrush}}"/>
              </Grid>
            </StackPanel>
          </Border>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>

    <Border Style="{DynamicResource CardBorder}" Margin="0,10,0,0"
            Visibility="{Binding DetailVisible, Converter={StaticResource BoolVis}}">
      <TextBlock Text="诊断详情将在后续阶段接入实时数据"
                 Foreground="{DynamicResource TextTertiaryBrush}"
                 FontSize="{DynamicResource FontSizeCaption}"/>
    </Border>
  </StackPanel>
</UserControl>
```

`wpf/Converters.cs`（新建，记得加入 csproj Compile 列表）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 概览页绑定用的轻量值转换器

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CaelusApp.WpfHost
{
    // "Success" 等语义键 → 当前主题的画刷（DynamicResource 求值）
    internal sealed class KeyBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            string key = (value as string) ?? "Info";
            object found = Application.Current.TryFindResource(key + "Brush");
            return found ?? Brushes.Gray;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    // 语义键 → 同色系 15% 透明底（用于结论图标圆形底）
    internal sealed class KeySoftBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            string key = (value as string) ?? "Info";
            var brush = Application.Current.TryFindResource(key + "Brush") as SolidColorBrush;
            if (brush == null) return new SolidColorBrush(Color.FromArgb(30, 128, 128, 128));
            Color col = brush.Color;
            return new SolidColorBrush(Color.FromArgb(38, col.R, col.G, col.B));
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class DetailButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return value is bool && (bool)value ? "收起详情" : "查看详情";
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class FractionGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            double f = value is double ? (double)value : 0;
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            return new GridLength(f, GridUnitType.Star);
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }
}
```

注意：`GridLength(f, Star)` 与 `*` 列配合得到比例填充；`Fraction=0` 时进度为空。

`wpf/Views/OverviewView.xaml.cs` 替换为：

```csharp
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class OverviewView : UserControl
    {
        public OverviewView()
        {
            InitializeComponent();
        }
    }
}
```

`wpf/MainWindow.xaml.cs`：在 `InitializeComponent()` 之后、设置 `PageHost.Content` 之前，注入 DataContext；新增构造函数重载供探针传入数据源：

```csharp
        public MainWindow() : this(null) { }

        public MainWindow(IOverviewSource source)
        {
            InitializeComponent();
            var vm = new OverviewViewModel(source ?? new SampleOverviewSource());
            vm.Refresh();
            DataContext = vm;
            PageHost.Content = new OverviewView { DataContext = vm };
        }
```

同时把 `NavChecked` 中概览分支改为复用同一 DataContext：

```csharp
            PageHost.Content = rb == NavOverview
                ? (object)new OverviewView { DataContext = DataContext }
                : new PlaceholderView();
```

`wpf/Caelus.Wpf.csproj` Compile 追加：

```xml
    <Compile Include="Converters.cs" />
```

- [ ] **Step 2: --wpf-shot 截图探针**

`wpf/App.xaml.cs` 替换为：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主入口：正常启动与 --wpf-shot 截图探针

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaelusApp.WpfHost
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (e.Args.Length >= 2 && e.Args[0] == "--wpf-shot")
            {
                int code = RunShot(e.Args[1]);
                Shutdown(code);
                return;
            }
            ThemeManager.Apply(this, UiTone.Light);
            var w = new MainWindow();
            w.Show();
        }

        // 离屏渲染深浅两个主题的概览页 PNG，供视觉验收
        private int RunShot(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                foreach (UiTone tone in new[] { UiTone.Light, UiTone.Dark })
                {
                    ThemeManager.Apply(this, tone);
                    var w = new MainWindow(new SampleOverviewSource())
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = -20000,
                        Top = -20000,
                        ShowInTaskbar = false,
                        ShowActivated = false
                    };
                    w.Show();
                    w.UpdateLayout();
                    var size = new Size(1196, 768);
                    w.Measure(size);
                    w.Arrange(new Rect(size));
                    w.UpdateLayout();
                    var rtb = new RenderTargetBitmap(1196, 768, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(w);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    string file = Path.Combine(dir, "wpf-overview-" +
                        (tone == UiTone.Light ? "light" : "dark") + ".png");
                    using (var fs = File.Create(file)) enc.Save(fs);
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
    }
}
```

- [ ] **Step 3: 构建 + 生成截图**

```bash
cmd //c build-wpf.cmd
./wpf/bin/Release/CaelusWpf.exe --wpf-shot "$TEMP/CaelusWpfShot"
```

预期：退出码 0，目录下出现 `wpf-overview-light.png` 与 `wpf-overview-dark.png`，无 `wpf-shot.error.txt`。

- [ ] **Step 4: 视觉验收**

用 Read 工具查看两张 PNG，对照规格 §5.1：
- 结论卡片在顶部、图标圆形底、右侧「查看详情」按钮
- 三个指标卡：GPU 温度 62°（绿色进度条）、目标帧率 —（无着色）、已用内存 8.4 GB（绿色进度条）
- 深浅主题均正确：浅色白底深字、深色暗底浅字
- 导航选中态为 Info 色高亮、分段控件「常规」选中

若渲染与规格不符，修正 XAML 后重复 Step 3-4，直至符合。

- [ ] **Step 5: Commit**

```bash
git add wpf/
git commit -m "feat: 概览视图（结论卡片+关键指标+渐进披露）与 --wpf-shot 截图探针"
```

---

### Task 11: 动效与减少动态效果

实现规格 §6 在 Phase 1 范围内的最小集：页面淡入（250ms）、详情区展开（300ms）。读取系统「显示动画」设置决定降级。

**Files:**
- Create: `wpf/Motion.cs`
- Modify: `wpf/Views/OverviewView.xaml.cs`（进入时淡入）
- Modify: `wpf/Caelus.Wpf.csproj`

- [ ] **Step 1: Motion 帮助类**

`wpf/Motion.cs`：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 WPF 动效执行：按系统设置降级（规格 §6.3）

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CaelusApp.WpfHost
{
    internal static class Motion
    {
        public static bool Reduced
        {
            get { return !SystemParameters.ClientAreaAnimation; }
        }

        // 页面进入：透明度淡入 +（未降级时）20px 上浮，250ms ease-out
        public static void FadeIn(FrameworkElement el)
        {
            if (el == null) return;
            int ms = UiMotion.Duration(UiMotion.PageFadeMs, Reduced);
            var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            el.BeginAnimation(UIElement.OpacityProperty, opacity);

            if (!UiMotion.AllowsOffset(Reduced)) return;
            if (el.RenderTransform == null || !(el.RenderTransform is TranslateTransform))
                el.RenderTransform = new TranslateTransform();
            var slide = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            el.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }
}
```

`wpf/Views/OverviewView.xaml.cs` 替换为：

```csharp
using System.Windows;
using System.Windows.Controls;

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
        }
    }
}
```

`wpf/Caelus.Wpf.csproj` Compile 追加：

```xml
    <Compile Include="Motion.cs" />
```

- [ ] **Step 2: 构建 + 截图回归**

```bash
cmd //c build-wpf.cmd
./wpf/bin/Release/CaelusWpf.exe --wpf-shot "$TEMP/CaelusWpfShot"
```

预期：截图仍正常生成（动画完成后截图内容不变）。

- [ ] **Step 3: Commit**

```bash
git add wpf/
git commit -m "feat: 概览页进入动效与减少动态效果降级（规格 §6）"
```

---

### Task 12: 可访问性最小集与托盘验证

规格 §10 要求 Phase 1 验证托盘图标在 WPF 宿主可用；规格 §7 的可访问性在 Phase 1 先落地 AutomationProperties 与键盘可达。

**Files:**
- Modify: `wpf/MainWindow.xaml`（AutomationProperties、Tab 顺序）
- Modify: `wpf/App.xaml.cs`（托盘图标）
- Modify: `wpf/Caelus.Wpf.csproj`（如需）

- [ ] **Step 1: AutomationProperties 标注**

`wpf/MainWindow.xaml` 中为交互元素补充标注：

- 关闭按钮：`AutomationProperties.Name="关闭窗口"`
- 最小化按钮：`AutomationProperties.Name="最小化窗口"`
- 导航 RadioButton：`AutomationProperties.Name="导航：概览"`（逐项）
- 分段控件 RadioButton：`AutomationProperties.Name="模式：常规"`（逐项）

示例（替换对应元素的开始标签）：

```xml
        <Button x:Name="CloseBtn" DockPanel.Dock="Right" Content="✕" Width="46"
                AutomationProperties.Name="关闭窗口"
                Click="CloseClick" Background="Transparent" BorderThickness="0"
                Foreground="{DynamicResource TextSecondaryBrush}"/>
```

`wpf/Views/OverviewView.xaml` 的「查看详情」按钮加 `AutomationProperties.Name="查看或收起诊断详情"`。

- [ ] **Step 2: 托盘图标（System.Windows.Forms.NotifyIcon 互操作）**

`wpf/App.xaml.cs`：`OnStartup` 正常分支（非 --wpf-shot）在 `w.Show()` 之后加入：

```csharp
            tray = new System.Windows.Forms.NotifyIcon
            {
                Text = "Caelus",
                Visible = true,
                Icon = System.Drawing.SystemIcons.Application
            };
```

类字段与清理：

```csharp
        private System.Windows.Forms.NotifyIcon tray;

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            }
            catch { }
            base.OnExit(e);
        }
```

注意：Phase 1 用系统占位图标即可，模式感知图标（IconArt）在 WinForms 侧，正式切换图标属后续阶段。

- [ ] **Step 3: 构建 + 冒烟**

`cmd //c build-wpf.cmd`，运行 `./wpf/bin/Release/CaelusWpf.exe`
预期：托盘出现图标；Tab 键可在导航/分段控件/按钮间移动焦点；关闭窗口后托盘图标消失。

- [ ] **Step 4: 回归自测**

`cmd //c dev.cmd test`
预期：`TOTAL 163/0/3`。

- [ ] **Step 5: Commit**

```bash
git add wpf/
git commit -m "feat: 可访问性标注与 WPF 宿主托盘图标验证"
```

---

### Task 13: 端到端验收与文档

**Files:**
- Modify: `docs/superpowers/specs/2026-08-09-ui-redesign-design.md`（不动，仅核对）
- Create: `docs/wpf-phase1-verification.md`（验收记录）

- [ ] **Step 1: 全量构建**

```bash
cmd //c "build.cmd"
cmd //c build-wpf.cmd
```

预期：`Build OK -> Caelus.exe` 与 `WPF Build OK` 均成功。

- [ ] **Step 2: 自测基线**

`cmd //c dev.cmd test`
预期：`TOTAL 163/0/3`（149 基线 + 14 新增，FAIL=0，SKIP=3）。

- [ ] **Step 3: 视觉验收截图**

```bash
./wpf/bin/Release/CaelusWpf.exe --wpf-shot docs/wpf-phase1
```

生成 `docs/wpf-phase1/wpf-overview-light.png` 与 `wpf-overview-dark.png`，人工核对规格 §5.1。

- [ ] **Step 4: 系统调用验证（规格 §10 风险项）**

正常运行 `./wpf/bin/Release/CaelusWpf.exe`（管理员）：
- 进程监控：WPF 宿主链接编译了 Core/Platform，`GameMode` 等类型可实例化（Task 7 编译通过即验证类型兼容；运行时不启动监控循环属预期——Phase 1 只验证宿主兼容性，监控接线在正式切换时完成）
- 托盘图标：Task 12 已验证
- 优先级调整：不属 Phase 1 范围（无 UI 触发入口），记录于验收文档

- [ ] **Step 5: 写验收记录并提交**

创建 `docs/wpf-phase1-verification.md`，记录：构建结果、自测 TOTAL、截图路径、托盘/键盘验证结论、遗留项（实时指标接线、详情面板内容、图标）。

```bash
git add docs/wpf-phase1/
git add docs/wpf-phase1-verification.md
git commit -m "docs: Phase 1 验收记录与概览页深浅主题截图"
```

---

## 自检记录

**规格覆盖：** §3.1→Task 2/8；§3.2/§3.3→Task 8 Tokens；§4.1/§4.3/§4.5/§4.6→Task 9 Styles；§5.1→Task 5/6/10；§6→Task 3/11；§7→Task 12（最小集）；§8 技术路线→Task 7；§10 风险项→Task 12/13。§4.2 开关、§4.4 输入框样式属 Phase 2/3 页面任务，本阶段不需要。§5.2-§5.6 属 Phase 2/3。Phase 4 完整动效/可访问性超出本计划。

**类型一致性：** `UiTone`/`ThemeColors`/`Palette.For`（Task 2）在 Task 8 ThemeManager 使用；`UiMotion.PageFadeMs/Duration/AllowsOffset`（Task 3）在 Task 11 Motion 使用；`ViewModelBase.SetProperty/Raise`（Task 4）在 Task 6 OverviewViewModel 使用；`IOverviewSource`（Task 6）在 Task 10 MainWindow 构造函数使用；`Native.LightModeQuery/QueryLightMode`（Task 1，`Native` 为 partial 静态类，无嵌套 Desktop 类）在 Task 8 ThemeManager 赋值。测试注册名与方法名一一对应。

**已知取舍：** WPF 目标框架 v4.0（匹配现有 csc 输出，避开缺失的 targeting pack）；目标帧率指标 Phase 1 恒为 “—”；详情面板为占位；托盘用系统图标。
