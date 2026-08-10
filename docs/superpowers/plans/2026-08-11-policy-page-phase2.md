# Phase 2 实现计划：优化策略页 WPF 迁移

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将优化策略页（21 个开关 + 3 分组 + 锁定矩阵）从 WinForms 迁移到 WPF，继承 Radium 座舱材质语言，真实读写 GameMode 属性。

**Architecture:** PolicyViewModel 是纯 C# 逻辑（放 `src/UiShared/`，可自测），封装 21 个策略项元数据 + 锁定矩阵计算 + GameMode 属性映射。PolicyView.xaml 是 WPF 渲染层（放 `wpf/Views/`），用玻璃 SettingCard + ToggleButton 列表展示。MainWindow 构造 GameMode 实例并传递。

**Tech Stack:** C# / WPF（net40，C# 5 语法上限）、既有自测框架（`dev.cmd test`，基线 166 PASS / 0 FAIL / 3 SKIP）、`GameMode`/`Settings`/`Lang`（`src/Core` + `src/Platform`，已链接编译进 WPF 项目）。

**规格文档:** `docs/superpowers/specs/2026-08-11-policy-page-phase2-design.md`

---

## 环境事实（执行前必读）

- 工作目录：`E:/A_Project/Pavise-Game`（main 分支，已合并 Phase 1 + 1.5）。
- 自测：`cmd //c "dev.cmd test"`，当前基线 `TOTAL 169  PASS 166  FAIL 0  SKIP 3`。
- WPF 构建：`cmd //c build-wpf.cmd` → `wpf\bin\Release\CaelusWpf.exe`。
- C# 5 语法上限（net4 csc）：无字符串插值、无表达式体成员、无 null 条件运算符、无 `nameof`。对象初始化器、`var`、Lambda 可用。
- `GameMode` 构造：`new GameMode(Paths.Data, new SuppressionCore())`（见 `src/Program.cs:93-95` 的预览入口先例）。构造时读 Settings 初值，不启动后台线程。
- `Lang.T(key)` 在 `src/Platform/Lang.cs:557`，返回中文文案。`Lang.F(key, args)` 带 `string.Format`。
- 21 个 GameMode bool 属性已确认存在于 `src/Core/GameMode.cs`（见规格 §4.2 表）。
- 锁定矩阵逻辑复刻自 `src/Ui/Pages/PanelForm.PolicyPage.cs:122-143` 的 `RefreshPolicyPresentation` + `ApplyPresetPolicy`。
- 现有 WPF 设施：`ThemeManager`（双轴）、`ModeController.SwitchTo`（已有模式切换）、`CardBorder`/`HeroCardBorder` 样式、`GlassCardBrush`/`GlassBorderBrush` 等画刷、`MainWindow` NavChecked 路由。
- `--wpf-shot` 截图探针在 `wpf/App.xaml.cs` RunShot 方法中。
- 所有 `.cmd` 文件 ASCII ONLY。

## 文件结构

| 文件 | 责任 |
|------|------|
| `src/UiShared/PolicyViewModel.cs` | 21 项元数据 + 锁定矩阵 + GameMode 属性映射（纯 C#，可自测） |
| `tests/SelfTests.UiShared.cs` | 追加 PolicyViewModel 自测 |
| `tests/SelfTests.cs` | 注册新测试 |
| `wpf/Views/PolicyView.xaml` | 策略页视图（3 分组 + 滚动 + 模式提示条 + SettingCard 列表） |
| `wpf/Views/PolicyView.xaml.cs` | code-behind（极简） |
| `wpf/Themes/Styles.xaml` | 追加 SettingCard + PolicyToggle 样式 |
| `wpf/MainWindow.xaml.cs` | 修改：构造 GameMode + NavPolicy 路由 + 模式切换刷新 |
| `wpf/App.xaml.cs` | 修改：构造 GameMode 并传给 MainWindow |
| `wpf/Caelus.Wpf.csproj` | 注册新文件 |

---

### Task 1: PolicyViewModel 纯逻辑——策略项元数据

**Files:**
- Create: `src/UiShared/PolicyViewModel.cs`
- Test: `tests/SelfTests.UiShared.cs`（追加）
- Modify: `tests/SelfTests.cs`（注册）

- [ ] **Step 1: 写失败测试**

`tests/SelfTests.UiShared.cs` 追加：

```csharp
        private static void TestPolicyItemsCompleteness()
        {
            // 三分组共 21 项
            Eq(9, PolicyViewModel.CoreItems.Count);
            Eq(5, PolicyViewModel.CustomItems.Count);
            Eq(7, PolicyViewModel.ExtraItems.Count);
            // 每项有标题、说明、属性名
            foreach (PolicyItem item in PolicyViewModel.AllItems())
            {
                if (string.IsNullOrEmpty(item.Title)) throw new Exception("empty title: " + item.PropertyName);
                if (string.IsNullOrEmpty(item.Description)) throw new Exception("empty desc: " + item.PropertyName);
                if (string.IsNullOrEmpty(item.PropertyName)) throw new Exception("empty propname");
            }
            // 验证关键文案（非空 = Lang.T 找到了 key）
            Eq("后台调度 · 总开关", PolicyViewModel.CoreItems[0].Title);
            Eq("严格 CPU 分区", PolicyViewModel.CustomItems[0].Title);
            Eq("竞技模式禁用 CPU 空闲状态", PolicyViewModel.ExtraItems[0].Title);
        }
```

`tests/SelfTests.cs` 注册（在最后的 mode 色板测试之后）：

```csharp
            test("策略项：三分组共 21 项，标题/说明/属性名齐全", TestPolicyItemsCompleteness);
```

- [ ] **Step 2: 运行测试确认失败**

`cmd //c "dev.cmd test"`
预期：FAIL（`PolicyViewModel` 不存在）。

- [ ] **Step 3: 最小实现**

新建 `src/UiShared/PolicyViewModel.cs`（元数据部分，暂不含 GameMode 接线）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 优化策略页 ViewModel：21 项策略元数据 + 锁定矩阵 + GameMode 属性映射

using System.Collections.ObjectModel;

namespace CaelusApp
{
    internal sealed class PolicyItem
    {
        public string Title;          // Lang.T(titleKey)
        public string Description;    // Lang.T(descKey)
        public string PropertyName;   // "SuppressBackground" 等
        public string ConfirmKey;     // 开前弹确认的文案 key（仅 gm.freeze），null 则不弹
    }

    internal sealed class PolicyViewModel : ViewModelBase
    {
        // 分组标题
        public static readonly string HintText = Lang.T("v15.policy.mode.hint");
        public static readonly string CoreGroupTitle = Lang.T("v15.policy.core");
        public static readonly string CustomGroupTitle = Lang.T("v15.policy.custom");
        public static readonly string ExtraGroupTitle = Lang.T("v15.policy.extras");

        // 21 项元数据（静态，不依赖 GameMode 实例）
        private static readonly PolicyItem[] core = new PolicyItem[]
        {
            new PolicyItem { Title = Lang.T("v14.bg.master"), Description = Lang.T("v14.bg.master.sub"), PropertyName = "SuppressBackground" },
            new PolicyItem { Title = Lang.T("gm.gpudemote"), Description = Lang.T("gm.gpudemote.sub"), PropertyName = "GpuDemote" },
            new PolicyItem { Title = Lang.T("gm.freeze"), Description = Lang.T("gm.freeze.sub"), PropertyName = "FreezeBackground", ConfirmKey = "gm.freeze.warn" },
            new PolicyItem { Title = Lang.T("gm.boost"), Description = Lang.T("v15.boost.sub"), PropertyName = "BoostGame" },
            new PolicyItem { Title = Lang.T("gm.ifeo"), Description = Lang.T("gm.ifeo.sub"), PropertyName = "IfeoBoostFallback" },
            new PolicyItem { Title = Lang.T("gm.lane"), Description = Lang.T("gm.lane.sub"), PropertyName = "RenderLaneOn" },
            new PolicyItem { Title = Lang.T("set.plan"), Description = Lang.T("v15.plan.sub"), PropertyName = "PowerPlanSwitch" },
            new PolicyItem { Title = Lang.T("set.notif"), Description = Lang.T("v15.notif.sub"), PropertyName = "NotifQuiet" },
            new PolicyItem { Title = Lang.T("set.hz"), Description = Lang.T("v15.hz.sub"), PropertyName = "HzGuard" }
        };

        private static readonly PolicyItem[] custom = new PolicyItem[]
        {
            new PolicyItem { Title = Lang.T("v14.cpu.adaptive"), Description = Lang.T("v14.cpu.adaptive.sub2"), PropertyName = "StrictCoreIsolation" },
            new PolicyItem { Title = Lang.T("gm.aggressive"), Description = Lang.T("gm.aggressive.sub"), PropertyName = "AggressiveSuppression" },
            new PolicyItem { Title = Lang.T("gm.pausedl"), Description = Lang.T("v15.custom.override"), PropertyName = "PauseDownloads" },
            new PolicyItem { Title = Lang.T("gm.pausesvc"), Description = Lang.T("v15.custom.override"), PropertyName = "PauseSvcIndex" },
            new PolicyItem { Title = Lang.T("set.dvr"), Description = Lang.T("v15.custom.override"), PropertyName = "KillGameDvr" }
        };

        private static readonly PolicyItem[] extra = new PolicyItem[]
        {
            new PolicyItem { Title = Lang.T("gm.idledisable"), Description = Lang.T("gm.idledisable.sub"), PropertyName = "IdleStateDisable" },
            new PolicyItem { Title = Lang.T("gm.visualfx"), Description = Lang.T("gm.visualfx.sub"), PropertyName = "VisualFxDowngrade" },
            new PolicyItem { Title = Lang.T("set.trim"), Description = Lang.T("v15.trim.sub"), PropertyName = "TrimWorkingSet" },
            new PolicyItem { Title = Lang.T("gm.standby"), Description = Lang.T("gm.standby.sub"), PropertyName = "PurgeStandby" },
            new PolicyItem { Title = Lang.T("gm.pausewu"), Description = Lang.T("gm.pausewu.sub"), PropertyName = "PauseWindowsUpdate" },
            new PolicyItem { Title = Lang.T("set.pqos"), Description = Lang.T("set.pqos.n"), PropertyName = "PresenceQosOff" },
            new PolicyItem { Title = Lang.T("set.awake"), Description = Lang.T("set.awake.n"), PropertyName = "KeepAwake" }
        };

        public static ReadOnlyCollection<PolicyItem> CoreItems { get { return Array.AsReadOnly(core); } }
        public static ReadOnlyCollection<PolicyItem> CustomItems { get { return Array.AsReadOnly(custom); } }
        public static ReadOnlyCollection<PolicyItem> ExtraItems { get { return Array.AsReadOnly(extra); } }

        public static System.Collections.Generic.IEnumerable<PolicyItem> AllItems()
        {
            foreach (PolicyItem i in core) yield return i;
            foreach (PolicyItem i in custom) yield return i;
            foreach (PolicyItem i in extra) yield return i;
        }
    }
}
```

注意：`ReadOnlyCollection`、`Array.AsReadOnly`、`yield return` 均为 C# 2.0+ 特性，net4 兼容。

- [ ] **Step 4: 运行测试确认通过**

`cmd //c "dev.cmd test"`
预期：`TOTAL 170  PASS 167  FAIL 0  SKIP 3`（166 + 1）。

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/PolicyViewModel.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "feat: PolicyViewModel 策略项元数据（21 项 × 3 分组，规格 §4.2）"
```

---

### Task 2: 锁定矩阵 + GameMode 属性映射

**Files:**
- Modify: `src/UiShared/PolicyViewModel.cs`
- Test: `tests/SelfTests.UiShared.cs`（追加）
- Modify: `tests/SelfTests.cs`（注册）

- [ ] **Step 1: 写失败测试**

`tests/SelfTests.UiShared.cs` 追加：

```csharp
        private static void TestPolicyLockMatrix()
        {
            // 锁定矩阵：5 个自定义项，Standard 全锁关，Competitive 全锁开，Custom 全放开
            foreach (PolicyItem item in PolicyViewModel.CustomItems)
            {
                // Standard
                bool stdLocked, stdValue;
                PolicyViewModel.GetLockState(item.PropertyName, PerformancePreset.Standard, out stdLocked, out stdValue);
                if (!stdLocked) throw new Exception(item.PropertyName + " should be locked in Standard");
                Eq(false, stdValue);

                // Competitive
                bool compLocked, compValue;
                PolicyViewModel.GetLockState(item.PropertyName, PerformancePreset.Competitive, out compLocked, out compValue);
                if (!compLocked) throw new Exception(item.PropertyName + " should be locked in Competitive");
                Eq(true, compValue);

                // Custom
                bool custLocked, custValue;
                PolicyViewModel.GetLockState(item.PropertyName, PerformancePreset.Custom, out custLocked, out custValue);
                if (custLocked) throw new Exception(item.PropertyName + " should be unlocked in Custom");
            }

            // 核心项和附加项在所有模式下都不锁
            foreach (PolicyItem item in PolicyViewModel.CoreItems)
            {
                bool locked, value;
                PolicyViewModel.GetLockState(item.PropertyName, PerformancePreset.Competitive, out locked, out value);
                if (locked) throw new Exception("core item " + item.PropertyName + " should never lock");
            }
            foreach (PolicyItem item in PolicyViewModel.ExtraItems)
            {
                bool locked, value;
                PolicyViewModel.GetLockState(item.PropertyName, PerformancePreset.Competitive, out locked, out value);
                if (locked) throw new Exception("extra item " + item.PropertyName + " should never lock");
            }
        }

        private static GameMode CreateTestGameMode()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "CaelusPolicyTest_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            System.IO.Directory.CreateDirectory(dir);
            return new GameMode(dir, new SuppressionCore());
        }

        private static void TestPolicyPropertyAccess()
        {
            GameMode gm = CreateTestGameMode();
            try
            {
                // 读初值（应能读不报错）
                bool v = PolicyViewModel.GetProperty(gm, "SuppressBackground");
                // 写
                PolicyViewModel.SetProperty(gm, "BoostGame", true);
                Eq(true, PolicyViewModel.GetProperty(gm, "BoostGame"));
                PolicyViewModel.SetProperty(gm, "BoostGame", false);
                Eq(false, PolicyViewModel.GetProperty(gm, "BoostGame"));
                // 验证全 21 项都能读
                foreach (PolicyItem item in PolicyViewModel.AllItems())
                {
                    PolicyViewModel.GetProperty(gm, item.PropertyName);
                }
            }
            finally
            {
                try { System.IO.Directory.Delete(gm.TestDataDir, true); } catch { }
            }
        }
```

注意：`TestDataDir` 可能不存在——如果 GameMode 没有暴露数据目录，测试用自己创建的 `dir` 变量清理。先读 GameMode 源码确认是否有 public 数据目录属性；如果没有，改为测试外保存 `dir` 变量。

`tests/SelfTests.cs` 注册：

```csharp
            test("策略锁定矩阵：5 自定义项在 Standard/Competitive 锁定，Custom 放开", TestPolicyLockMatrix);
            test("策略属性映射：21 项 get/set 正确读写 GameMode", TestPolicyPropertyAccess);
```

- [ ] **Step 2: 运行测试确认失败**

`cmd //c "dev.cmd test"`
预期：FAIL（`GetLockState`/`GetProperty`/`SetProperty` 不存在）。

- [ ] **Step 3: 最小实现**

`src/UiShared/PolicyViewModel.cs` 追加（在 `AllItems()` 之后）：

```csharp
        // 锁定矩阵：只有 5 个自定义项会被预设锁定
        public static void GetLockState(string propertyName, PerformancePreset preset,
            out bool locked, out bool lockedValue)
        {
            // 核心项和附加项永不锁定
            bool isCustom = false;
            foreach (PolicyItem item in custom)
            {
                if (item.PropertyName == propertyName) { isCustom = true; break; }
            }
            if (!isCustom) { locked = false; lockedValue = false; return; }

            // 自定义项：Standard 锁关，Competitive 锁开，Custom 放开
            if (preset == PerformancePreset.Standard) { locked = true; lockedValue = false; return; }
            if (preset == PerformancePreset.Competitive) { locked = true; lockedValue = true; return; }
            locked = false; lockedValue = false;
        }

        // GameMode 属性读写（显式映射，21 个属性）
        public static bool GetProperty(GameMode gm, string name)
        {
            switch (name)
            {
                case "SuppressBackground": return gm.SuppressBackground;
                case "GpuDemote": return gm.GpuDemote;
                case "FreezeBackground": return gm.FreezeBackground;
                case "BoostGame": return gm.BoostGame;
                case "IfeoBoostFallback": return gm.IfeoBoostFallback;
                case "RenderLaneOn": return gm.RenderLaneOn;
                case "PowerPlanSwitch": return gm.PowerPlanSwitch;
                case "NotifQuiet": return gm.NotifQuiet;
                case "HzGuard": return gm.HzGuard;
                case "StrictCoreIsolation": return gm.StrictCoreIsolation;
                case "AggressiveSuppression": return gm.AggressiveSuppression;
                case "PauseDownloads": return gm.PauseDownloads;
                case "PauseSvcIndex": return gm.PauseSvcIndex;
                case "KillGameDvr": return gm.KillGameDvr;
                case "IdleStateDisable": return gm.IdleStateDisable;
                case "VisualFxDowngrade": return gm.VisualFxDowngrade;
                case "TrimWorkingSet": return gm.TrimWorkingSet;
                case "PurgeStandby": return gm.PurgeStandby;
                case "PauseWindowsUpdate": return gm.PauseWindowsUpdate;
                case "PresenceQosOff": return gm.PresenceQosOff;
                case "KeepAwake": return gm.KeepAwake;
                default: throw new System.ArgumentException("unknown policy property: " + name);
            }
        }

        public static void SetProperty(GameMode gm, string name, bool value)
        {
            switch (name)
            {
                case "SuppressBackground": gm.SuppressBackground = value; break;
                case "GpuDemote": gm.GpuDemote = value; break;
                case "FreezeBackground": gm.FreezeBackground = value; break;
                case "BoostGame": gm.BoostGame = value; break;
                case "IfeoBoostFallback": gm.IfeoBoostFallback = value; break;
                case "RenderLaneOn": gm.RenderLaneOn = value; break;
                case "PowerPlanSwitch": gm.PowerPlanSwitch = value; break;
                case "NotifQuiet": gm.NotifQuiet = value; break;
                case "HzGuard": gm.HzGuard = value; break;
                case "StrictCoreIsolation": gm.StrictCoreIsolation = value; break;
                case "AggressiveSuppression": gm.AggressiveSuppression = value; break;
                case "PauseDownloads": gm.PauseDownloads = value; break;
                case "PauseSvcIndex": gm.PauseSvcIndex = value; break;
                case "KillGameDvr": gm.KillGameDvr = value; break;
                case "IdleStateDisable": gm.IdleStateDisable = value; break;
                case "VisualFxDowngrade": gm.VisualFxDowngrade = value; break;
                case "TrimWorkingSet": gm.TrimWorkingSet = value; break;
                case "PurgeStandby": gm.PurgeStandby = value; break;
                case "PauseWindowsUpdate": gm.PauseWindowsUpdate = value; break;
                case "PresenceQosOff": gm.PresenceQosOff = value; break;
                case "KeepAwake": gm.KeepAwake = value; break;
                default: throw new System.ArgumentException("unknown policy property: " + name);
            }
        }
```

注意：`CreateTestGameMode` 测试辅助方法中的清理逻辑——GameMode 不暴露 `TestDataDir`。修改测试为：

```csharp
        private static GameMode CreateTestGameMode(out string dir)
        {
            dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "CaelusPolicyTest_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            System.IO.Directory.CreateDirectory(dir);
            return new GameMode(dir, new SuppressionCore());
        }
```

并相应修改 `TestPolicyPropertyAccess` 的调用和清理（用 `out string dir` 接收，finally 里删 `dir`）。

- [ ] **Step 4: 运行测试确认通过**

`cmd //c "dev.cmd test"`
预期：`TOTAL 172  PASS 169  FAIL 0  SKIP 3`（167 + 2）。

- [ ] **Step 5: Commit**

```bash
git add src/UiShared/PolicyViewModel.cs tests/SelfTests.UiShared.cs tests/SelfTests.cs
git commit -m "feat: 策略锁定矩阵 + GameMode 属性映射（21 项显式 switch）"
```

---

### Task 3: SettingCard 与 PolicyToggle 样式

**Files:**
- Modify: `wpf/Themes/Styles.xaml`（追加）

- [ ] **Step 1: 追加样式**

在 `wpf/Themes/Styles.xaml` 末尾（`</ResourceDictionary>` 之前）追加：

```xml
  <!-- 策略开关卡片：整卡可点击（点卡片=切开关），玻璃底 + 左侧标题 + 右侧 ToggleButton -->
  <Style x:Key="PolicyCard" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource GlassCardBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource GlassBorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusMd}"/>
    <Setter Property="Padding" Value="14,10"/>
  </Style>

  <!-- iOS 风格滑动开关（ToggleButton 模板） -->
  <Style x:Key="PolicyToggle" TargetType="ToggleButton">
    <Setter Property="Width" Value="44"/>
    <Setter Property="Height" Value="24"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToggleButton">
          <Grid>
            <!-- 轨道 -->
            <Border x:Name="track" CornerRadius="12" Background="#FF3A4250"/>
            <!-- 滑块 -->
            <Border x:Name="thumb" Width="20" Height="20" CornerRadius="10"
                    HorizontalAlignment="Left" Margin="2,0,0,0">
              <Border.Background>
                <SolidColorBrush Color="White"/>
              </Border.Background>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="track" Property="Background" Value="{DynamicResource SuccessBrush}"/>
              <Setter TargetName="thumb" Property="HorizontalAlignment" Value="Right"/>
              <Setter TargetName="thumb" Property="Margin" Value="0,0,2,0"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.4"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

注意：WPF net4 的 `ToggleButton` 滑块无动画过渡（Storyboard 会显著增加复杂度，Phase 2 先无动画，Phase 4 打磨时加）。`IsEnabled=false` 时 `Opacity=0.4` 实现锁定灰化（复刻 WinForms Toggle 的蒙版效果）。

- [ ] **Step 2: 构建验证**

`cmd //c build-wpf.cmd`
预期：`WPF Build OK`。

- [ ] **Step 3: Commit**

```bash
git add wpf/Themes/Styles.xaml
git commit -m "feat: 策略卡片与滑动开关样式（PolicyCard 玻璃底 + PolicyToggle iOS 风格）"
```

---

### Task 4: PolicyView 视图

**Files:**
- Create: `wpf/Views/PolicyView.xaml`、`wpf/Views/PolicyView.xaml.cs`
- Modify: `wpf/Caelus.Wpf.csproj`

- [ ] **Step 1: 创建视图**

`wpf/Views/PolicyView.xaml`：

```xml
<UserControl x:Class="CaelusApp.WpfHost.Views.PolicyView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:CaelusApp.WpfHost">
  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="0,0,0,16">

      <!-- 模式提示条 -->
      <Border Style="{DynamicResource HeroCardBorder}" Padding="14,10" Margin="0,0,0,16">
        <TextBlock Text="{Binding HintText}" FontSize="{DynamicResource FontSizeCaption}"
                   Foreground="{DynamicResource TextSecondaryBrush}" TextWrapping="Wrap"/>
      </Border>

      <!-- 分组标题 + 卡片列表（三个分组共用模板，通过 ItemsControl 绑定） -->
      <ItemsControl ItemsSource="{Binding CoreCards}">
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate><StackPanel/></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
      </ItemsControl>

      <ItemsControl ItemsSource="{Binding CustomCards}" Margin="0,16,0,0">
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate><StackPanel/></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
      </ItemsControl>

      <ItemsControl ItemsSource="{Binding ExtraCards}" Margin="0,16,0,0">
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate><StackPanel/></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
      </ItemsControl>

    </StackPanel>
  </ScrollViewer>
</UserControl>
```

注意：上方的 `ItemsSource` 绑定到 `CoreCards` 等——这是 WPF 绑定的 `ObservableCollection<PolicyCardViewModel>`，在 Task 5 的运行时 ViewModel 中定义。本任务的 XAML 声明了绑定路径，Task 5 提供 DataContext。

分组标题（"全模式核心控制"等）需要显示——在每组 ItemsControl 上方加一个分组标题 TextBlock。修改为：

```xml
      <TextBlock Text="{Binding CoreGroupTitle}" FontSize="{DynamicResource FontSizeBody}"
                 FontWeight="Bold" Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,0,8"/>
      <ItemsControl ItemsSource="{Binding CoreCards}">
```

三组各加一个分组标题 TextBlock。

`wpf/Views/PolicyView.xaml.cs`：

```csharp
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class PolicyView : UserControl
    {
        public PolicyView()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 2: csproj 注册**

Page 追加：
```xml
    <Page Include="Views\PolicyView.xaml" />
```
Compile 追加：
```xml
    <Compile Include="Views\PolicyView.xaml.cs">
      <DependentUpon>Views\PolicyView.xaml</DependentUpon>
    </Compile>
```

- [ ] **Step 3: 构建验证**

`cmd //c build-wpf.cmd`
预期：`WPF Build OK`（绑定路径在运行时才解析，编译不检查 DataContext）。

- [ ] **Step 4: Commit**

```bash
git add wpf/Views/PolicyView.xaml wpf/Views/PolicyView.xaml.cs wpf/Caelus.Wpf.csproj
git commit -m "feat: PolicyView 视图骨架（滚动 + 模式提示 + 三分组 ItemsControl）"
```

---

### Task 5: 运行时 ViewModel + GameMode 接线

**Files:**
- Create: `wpf/PolicyRuntime.cs`（WPF 宿主专用的运行时 ViewModel）
- Modify: `wpf/MainWindow.xaml.cs`
- Modify: `wpf/App.xaml.cs`
- Modify: `wpf/Caelus.Wpf.csproj`

- [ ] **Step 1: 运行时 ViewModel**

创建 `wpf/PolicyRuntime.cs`——WPF 绑定用的运行时 ViewModel（不放 UiShared，因为它依赖 WPF 的 `ObservableCollection` 和 `INotifyPropertyChanged`）：

```csharp
// @author zenjiro 18967498922@163.com
// 文件用途 策略页运行时 ViewModel：绑定 WPF 视图，读写 GameMode 属性

using System.Collections.ObjectModel;
using System.Windows;

namespace CaelusApp.WpfHost
{
    // 单个策略卡片的 ViewModel（每项一个实例，绑定到 PolicyCard 模板）
    internal sealed class PolicyCardViewModel : ViewModelBase
    {
        private readonly GameMode gm;
        private readonly PolicyItem item;
        private bool isOn;
        private bool isLocked;
        private string displayTitle;

        public PolicyCardViewModel(GameMode gm, PolicyItem item)
        {
            this.gm = gm;
            this.item = item;
            displayTitle = item.Title;
            isOn = PolicyViewModel.GetProperty(gm, item.PropertyName);
        }

        public string Title { get { return displayTitle; } }
        public string Description { get { return item.Description; } }
        public string PropertyName { get { return item.PropertyName; } }
        public string ConfirmKey { get { return item.ConfirmKey; } }

        public bool IsOn
        {
            get { return isOn; }
            set
            {
                if (isLocked) return;
                if (SetProperty(ref isOn, value, "IsOn"))
                {
                    PolicyViewModel.SetProperty(gm, item.PropertyName, value);
                }
            }
        }

        public bool IsLocked
        {
            get { return isLocked; }
            set
            {
                if (SetProperty(ref isLocked, value, "IsLocked"))
                {
                    UpdateDisplayTitle();
                }
            }
        }

        public bool IsEnabled { get { return !isLocked; } }

        // 从 GameMode 刷新开关状态（模式切换后锁定项的值被预设改了）
        public void RefreshFromGameMode()
        {
            bool newVal = PolicyViewModel.GetProperty(gm, item.PropertyName);
            if (isOn != newVal)
            {
                isOn = newVal;
                Raise("IsOn");
            }
        }

        // 确认对话框：冻结开关开启前弹确认
        // 返回 true=用户确认/不需要确认，false=用户取消
        public bool ConfirmIfNeeded()
        {
            if (string.IsNullOrEmpty(ConfirmKey)) return true;
            if (IsOn) return true; // 关闭不需要确认
            string warning = Lang.T(ConfirmKey);
            MessageBoxResult r = MessageBox.Show(warning, "Caelus",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            return r == MessageBoxResult.OK;
        }

        private void UpdateDisplayTitle()
        {
            string t = item.Title;
            if (isLocked) t = t + " · " + Lang.T("v14.preset.forced");
            displayTitle = t;
            Raise("Title");
            Raise("IsEnabled");
        }
    }

    // 策略页整体 ViewModel
    internal sealed class PolicyPageViewModel : ViewModelBase
    {
        private readonly GameMode gm;

        public PolicyPageViewModel(GameMode gm)
        {
            this.gm = gm;
            HintText = PolicyViewModel.HintText;
            CoreGroupTitle = PolicyViewModel.CoreGroupTitle;
            CustomGroupTitle = PolicyViewModel.CustomGroupTitle;
            ExtraGroupTitle = PolicyViewModel.ExtraGroupTitle;

            CoreCards = new ObservableCollection<PolicyCardViewModel>();
            CustomCards = new ObservableCollection<PolicyCardViewModel>();
            ExtraCards = new ObservableCollection<PolicyCardViewModel>();

            foreach (PolicyItem item in PolicyViewModel.CoreItems)
                CoreCards.Add(new PolicyCardViewModel(gm, item));
            foreach (PolicyItem item in PolicyViewModel.CustomItems)
                CustomCards.Add(new PolicyCardViewModel(gm, item));
            foreach (PolicyItem item in PolicyViewModel.ExtraItems)
                ExtraCards.Add(new PolicyCardViewModel(gm, item));

            RefreshLocks();
        }

        public string HintText { get; private set; }
        public string CoreGroupTitle { get; private set; }
        public string CustomGroupTitle { get; private set; }
        public string ExtraGroupTitle { get; private set; }
        public ObservableCollection<PolicyCardViewModel> CoreCards { get; private set; }
        public ObservableCollection<PolicyCardViewModel> CustomCards { get; private set; }
        public ObservableCollection<PolicyCardViewModel> ExtraCards { get; private set; }

        // 模式切换后重算锁定矩阵 + 刷新开关值
        public void RefreshLocks()
        {
            PerformancePreset preset = gm.ActivePreset;
            foreach (PolicyCardViewModel card in CustomCards)
            {
                bool locked, lockedValue;
                PolicyViewModel.GetLockState(card.PropertyName, preset, out locked, out lockedValue);
                card.IsLocked = locked;
                if (locked)
                {
                    PolicyViewModel.SetProperty(gm, card.PropertyName, lockedValue);
                    card.RefreshFromGameMode();
                }
                else
                {
                    card.RefreshFromGameMode();
                }
            }
            // 核心项和附加项不锁，但模式切换可能改了它们的值（预设间接影响）
            foreach (PolicyCardViewModel card in CoreCards) card.RefreshFromGameMode();
            foreach (PolicyCardViewModel card in ExtraCards) card.RefreshFromGameMode();
        }
    }
}
```

注意：`PolicyCardViewModel.IsOn` 的 setter 在 `isLocked` 时直接 return（锁定项不可手动切）。但 WPF 绑定会尝试写——这没问题，setter 静默拒绝。确认对话框逻辑（`ConfirmIfNeeded`）在 Task 6 的视图交互中接入。

- [ ] **Step 2: App.xaml.cs 构造 GameMode**

`wpf/App.xaml.cs` 正常启动分支，在 `MainWindow w = new MainWindow()` 之前加：

```csharp
            var gameCore = new SuppressionCore();
            var gameMode = new GameMode(Paths.Data, gameCore);
```

修改 `MainWindow` 构造为接收 GameMode：

```csharp
            MainWindow w = new MainWindow(gameMode);
```

- [ ] **Step 3: MainWindow 构造函数 + NavPolicy 路由**

`wpf/MainWindow.xaml.cs` 修改构造函数链：

```csharp
        private readonly GameMode gameMode;
        private readonly PolicyPageViewModel policyVm;

        public MainWindow() : this(null, null) { }

        internal MainWindow(GameMode gm, IOverviewSource overviewSource)
        {
            InitializeComponent();
            gameMode = gm ?? new GameMode(Paths.Data, new SuppressionCore());
            source = overviewSource as SampleOverviewSource ?? new SampleOverviewSource();
            vm = new OverviewViewModel(source);
            vm.Refresh();
            DataContext = vm;
            PageHost.Content = new OverviewView { DataContext = vm };
            policyVm = new PolicyPageViewModel(gameMode);
            Loaded += OnLoadedAmbient;
        }
```

NavChecked 路由增加 PolicyView 分支：

```csharp
        private void NavChecked(object sender, RoutedEventArgs e)
        {
            RadioButton rb = sender as RadioButton;
            if (rb == null || PageHost == null) return;
            if (rb == NavOverview)
                PageHost.Content = new OverviewView { DataContext = vm };
            else if (rb == NavPolicy)
                PageHost.Content = new PolicyView { DataContext = policyVm };
            else
                PageHost.Content = new PlaceholderView();
        }
```

注意：需要给 `优化策略` RadioButton 加 `x:Name="NavPolicy"`（在 `wpf/MainWindow.xaml` 中）。读 MainWindow.xaml 找到 `优化策略` 的 RadioButton，加 `x:Name="NavPolicy"`。

ModeChecked 事件中模式切换后刷新策略锁定：

```csharp
        private void ModeChecked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            AppMode mode = sender == SegCompetitive ? AppMode.Competitive
                : sender == SegCustom ? AppMode.Custom : AppMode.Standard;
            ModeController.SwitchTo(Application.Current, mode, Ambient, source, vm, true);
            if (policyVm != null) policyVm.RefreshLocks();
        }
```

注意：ModeController.SwitchTo 目前读写 Settings 的 PerformancePreset 键，但不直接写 `gameMode.Preset`。为了让 GameMode 的 ActivePreset 与模式同步，在 ModeChecked 中 SwitchTo 之后补一行：

```csharp
            gameMode.Preset = ModeController.AppModeToPreset(mode);
```

并在 `wpf/ModeController.cs` 中追加公共映射方法（如果还没有）：

```csharp
        public static PerformancePreset AppModeToPreset(AppMode mode)
        {
            if (mode == AppMode.Competitive) return PerformancePreset.Competitive;
            if (mode == AppMode.Custom) return PerformancePreset.Competitive;
            return PerformancePreset.Standard;
        }
```

等等——上面 Custom 映射错了，应该是：

```csharp
        public static PerformancePreset AppModeToPreset(AppMode mode)
        {
            if (mode == AppMode.Competitive) return PerformancePreset.Competitive;
            if (mode == AppMode.Custom) return PerformancePreset.Custom;
            return PerformancePreset.Standard;
        }
```

注意：`ModeController` 的 `ToPreset` 当前是 `private static`（`wpf/ModeController.cs:32`）。执行时把它改为 `public static PerformancePreset ToPreset(AppMode mode)`（仅改可见性，方法体不变），然后在 ModeChecked 中用 `gameMode.Preset = ModeController.ToPreset(mode)` 同步 GameMode。不要新增 `AppModeToPreset` 方法（DRY）。

- [ ] **Step 4: 冻结确认对话框接线**

PolicyView 的 XAML 中，ToggleButton 的 Click 事件（PreviewClick——在绑定写入前拦截）接入确认对话框。但 WPF 绑定的 IsChecked 写入时机难拦截。替代方案：PolicyCardViewModel.IsOn setter 内部做确认——但这会让 ViewModel 依赖 MessageBox（不佳但可接受）。

修改 `PolicyCardViewModel.IsOn` setter：

```csharp
        public bool IsOn
        {
            get { return isOn; }
            set
            {
                if (isLocked) return;
                // 开启前确认（仅冻结开关）
                if (value && !string.IsNullOrEmpty(ConfirmKey))
                {
                    MessageBoxResult r = MessageBox.Show(Lang.T(ConfirmKey), "Caelus",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.OK) return;
                }
                if (SetProperty(ref isOn, value, "IsOn"))
                {
                    PolicyViewModel.SetProperty(gm, item.PropertyName, value);
                }
            }
        }
```

这样移除 `ConfirmIfNeeded` 方法（不再需要），确认逻辑内聚在 setter。

- [ ] **Step 5: csproj 注册**

```xml
    <Compile Include="PolicyRuntime.cs" />
```

- [ ] **Step 6: 构建 + 自测回归**

```bash
cmd //c build-wpf.cmd
cmd //c "dev.cmd test"
```

预期：WPF Build OK；`TOTAL 172  PASS 169  FAIL 0  SKIP 3`。

- [ ] **Step 7: Commit**

```bash
git add wpf/ src/UiShared/PolicyViewModel.cs
git commit -m "feat: 策略页运行时接线——GameMode 实例 + PolicyPageViewModel + 导航路由 + 模式切换刷新锁定"
```

---

### Task 6: PolicyView 卡片模板 + 截图验证

**Files:**
- Modify: `wpf/Views/PolicyView.xaml`（完善卡片模板）
- Modify: `wpf/App.xaml.cs`（截图探针增加策略页）

- [ ] **Step 1: 完善卡片模板**

`wpf/Views/PolicyView.xaml` 中三个 ItemsControl 各加 `ItemTemplate`（每组共享同一模板）：

```xml
      <ItemsControl ItemsSource="{Binding CoreCards}">
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate><StackPanel/></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Style="{DynamicResource PolicyCard}" Margin="0,0,0,6">
              <DockPanel>
                <ToggleButton Style="{DynamicResource PolicyToggle}"
                              IsChecked="{Binding IsOn}"
                              IsEnabled="{Binding IsEnabled}"
                              DockPanel.Dock="Right" VerticalAlignment="Top"
                              Margin="0,2,0,0"/>
                <StackPanel VerticalAlignment="Center">
                  <TextBlock Text="{Binding Title}" FontSize="{DynamicResource FontSizeCaption}"
                             FontWeight="Bold" Foreground="{DynamicResource TextPrimaryBrush}"
                             TextWrapping="Wrap"/>
                  <TextBlock Text="{Binding Description}" FontSize="11"
                             Foreground="{DynamicResource TextSecondaryBrush}"
                             TextWrapping="Wrap" Margin="0,2,0,0"/>
                </StackPanel>
              </DockPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
```

三组 ItemsControl 都加相同的 `ItemTemplate`（Content 相同，只是 ItemsSource 不同）。可以使用 `ContentControl` + `DataTemplate` 资源避免重复，但 net4 WPF 的 DataTemplate 资源共享在 ItemsControl.ItemTemplate 中需要 `x:Key` 引用——为简单起见，三组各内联一份 `ItemTemplate`（DRY 牺牲换简单，21 个卡片 × 3 组不算多）。

注意：`ToggleButton` 的 `IsChecked` 绑定是双向的（默认 TwoWay for ToggleButton.IsChecked）。`IsEnabled` 绑定到 `IsEnabled` 属性（= `!IsLocked`）。

- [ ] **Step 2: 截图探针扩展**

`wpf/App.xaml.cs` 的 RunShot 方法，在矩阵循环之后追加策略页截图。在循环内或循环后添加：

```csharp
                // 策略页截图（巡航深色）
                ThemeManager.Apply(this, UiTone.Dark, AppMode.Standard);
                MainWindow pw = new MainWindow(new GameMode(Paths.Data, new SuppressionCore()), new SampleOverviewSource());
                pw.ApplyPersistedMode(AppMode.Standard);
                pw.WindowStartupLocation = WindowStartupLocation.Manual;
                pw.Left = -20000; pw.Top = -20000;
                pw.ShowInTaskbar = false; pw.ShowActivated = false;
                pw.Show(); pw.UpdateLayout();
                Size psize = new Size(1196, 768);
                pw.Measure(psize); pw.Arrange(new Rect(psize)); pw.UpdateLayout();
                // 导航到策略页
                RadioButton navPolicy = null;
                // 用 UIA 或代码导航——最简单：直接设 PageHost
                // 但 RunShot 没有 UIA。替代：在 MainWindow 加 public 方法 NavigateToPolicy()
                pw.Close();
```

上面的截图探针需要导航到策略页才能截图——在 MainWindow 加 `public void NavigateToPolicyForShot()` 方法：

```csharp
        internal void NavigateToPolicyForShot()
        {
            PageHost.Content = new PolicyView { DataContext = policyVm };
            UpdateLayout();
        }
```

RunShot 中替换为：

```csharp
                MainWindow pw = new MainWindow(new GameMode(Paths.Data, new SuppressionCore()), new SampleOverviewSource());
                pw.ApplyPersistedMode(AppMode.Standard);
                pw.WindowStartupLocation = WindowStartupLocation.Manual;
                pw.Left = -20000; pw.Top = -20000;
                pw.ShowInTaskbar = false; pw.ShowActivated = false;
                pw.Show(); pw.UpdateLayout();
                pw.NavigateToPolicyForShot();
                Size psize = new Size(1196, 768);
                pw.Measure(psize); pw.Arrange(new Rect(psize)); pw.UpdateLayout();
                RenderTargetBitmap prtb = new RenderTargetBitmap(1196, 768, 96, 96, PixelFormats.Pbgra32);
                prtb.Render(pw);
                PngBitmapEncoder penc = new PngBitmapEncoder();
                penc.Frames.Add(BitmapFrame.Create(prtb));
                string pfile = Path.Combine(dir, "wpf-policy-dark-cruise.png");
                using (FileStream pfs = File.Create(pfile)) penc.Save(pfs);
                pw.Close();
```

- [ ] **Step 3: 构建 + 截图**

```bash
cmd //c build-wpf.cmd
./wpf/bin/Release/CaelusWpf.exe --wpf-shot "$TEMP/PolicyShot"
```

预期：生成 `wpf-policy-dark-cruise.png`，无 error.txt。

- [ ] **Step 4: 视觉验证**

用 Read 工具或 UIA 检查 `wpf-policy-dark-cruise.png`：21 个卡片分三组可见、开关渲染正确、锁定项有灰化效果、文案无截断。

- [ ] **Step 5: 自测回归**

`cmd //c "dev.cmd test"`
预期：`TOTAL 172  PASS 169  FAIL 0  SKIP 3`。

- [ ] **Step 6: 端到端回归**

```bash
cmd //c "build.cmd"
cmd //c build-wpf.cmd
cmd //c "dev.cmd test"
```

预期：两个 Build OK；自测 172/169/0/3。

- [ ] **Step 7: Commit**

```bash
git add wpf/
git commit -m "feat: 策略页卡片模板完善 + 截图探针扩展（策略页巡航深色截图）"
```

---

## 自检记录

**规格覆盖：** §3.1 GameMode 构造 → Task 5 Step 2；§3.2 传递路径 → Task 5 Step 3；§3.3 模式切换锁定刷新 → Task 5 Step 3 ModeChecked；§4.1 布局 → Task 4 XAML；§4.2 21 项清单 → Task 1 元数据 + Task 2 属性映射；§4.3 锁定矩阵 → Task 2 GetLockState；§5.1 PolicyViewModel → Task 1+2（元数据/UiShared）+ Task 5（运行时/wpf）；§5.2 SettingCard 样式 → Task 3；§5.3 冻结确认 → Task 5 Step 4（setter 内 MessageBox）；§6 导航接线 → Task 5 Step 3；§7 截图探针 → Task 6 Step 2；§8 范围 8 项 → Task 1-6；§9 验收 → Task 6 Step 5-6。

**类型一致性：** `PolicyItem`（Task 1）→ Task 2/5 使用；`PolicyViewModel.CoreItems/CustomItems/ExtraItems/AllItems/GetLockState/GetProperty/SetProperty`（Task 1+2）→ Task 5 PolicyPageViewModel 使用；`PolicyCardViewModel`/`PolicyPageViewModel`（Task 5）→ Task 4/6 XAML 绑定使用；`MainWindow(GameMode, IOverviewSource)` 构造签名（Task 5）→ Task 6 RunShot 使用；`NavigateToPolicyForShot`（Task 6 Step 2）→ RunShot 调用。

**已知取舍：** ToggleButton 无滑动动画（Phase 4 打磨）；卡片模板三组内联重复（简单优先）；确认对话框在 ViewModel setter 内弹 MessageBox（不佳但 Phase 2 可接受）；RunShot 构造额外 GameMode 实例（内存短暂占用，截图后释放）。
