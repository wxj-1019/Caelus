# Phase 2 设计规格：优化策略页 WPF 迁移

**版本**: 1.0  
**日期**: 2026-08-11  
**状态**: 待实现  
**上游规格**: `2026-08-09-ui-redesign-design.md` §5.3、`2026-08-10-radium-cockpit-design.md`  
**实现计划**: `docs/superpowers/plans/2026-08-11-policy-page-phase2.md`（后续生成）

---

## 1. 定位

Phase 2 将优化策略页从 WinForms 迁移到 WPF，继承 Phase 1.5 的 Radium 座舱材质语言。这是第一个"信息密集页面"迁移——21 个策略开关 + 3 个分组 + 锁定矩阵，检验玻璃材质在密集列表中是否成立。

**范围**：仅优化策略页。游戏库页留待 Phase 3。

## 2. 关键决策（已确认）

| # | 决策 | 选择 | 理由 |
|---|------|------|------|
| 1 | 数据源 | **真实 GameMode 实例** | 策略页价值在于"真实可调"；GameMode 已被 WPF 项目链接编译，构造安全（不启动后台线程） |
| 2 | 模式选择器 | **复用外壳分段控件** | 分段控件跨页面存在于 MainWindow；锁定矩阵本身就是模式反馈；不重复内嵌选择器 |

## 3. 数据源接线

### 3.1 GameMode 构造

WPF 宿主 `App.xaml.cs` 的正常启动分支构造 GameMode 实例（与 WinForms 预览入口 `Program.cs:93-95` 同模式）：

```csharp
var core = new SuppressionCore();
var gameMode = new GameMode(Paths.Data, core);
```

- `Paths.Data` = `%APPDATA%\Caelus`（或便携模式下的 exe 目录）
- `SuppressionCore()` 无参构造创建空状态（不启动 Tamer/扫描线程）
- GameMode 构造时从 `Settings.Load` 读入 21 个开关初值 + 读 `Caelus.profiles.dat`/`Caelus.games.txt`
- **不调用** `gameMode.Start()` / `gameMode.Enabled = true`——不启动后台检测/压制循环
- 开关 setter 的 `Settings.Save` 正常工作（持久化）；`RequestPolicyApply()` 被调用但因无后台 worker，策略不会实际下发（预览宿主可接受；正式宿主接管时补 worker）

### 3.2 GameMode 传递路径

```
App.OnStartup → new GameMode(Paths.Data, new SuppressionCore())
  → MainWindow(gameMode) → PolicyView(gameMode) → PolicyViewModel(gameMode)
```

MainWindow 构造函数增加 `GameMode` 参数，传递给 PolicyView 的 ViewModel。

### 3.3 模式切换与锁定矩阵

分段控件的模式切换（`ModeController.SwitchTo`）已有持久化 + 主题换槽 + 氛围过渡。Phase 2 扩展它：切换后调用 `PolicyViewModel.RefreshLocks()`，重算 5 个自定义项的锁定矩阵。

GameMode.Preset setter 本身会 `RequestPolicyApply()`（无 worker 时无副作用）。模式切换仍走 `ModeController.SwitchTo`（已有 `Settings.SaveStr`），但额外通知 PolicyViewModel 刷新锁定状态。

## 4. 页面结构

### 4.1 布局

```
┌─────────────────────────────────────────┐
│ [滚动区]                                │
│                                         │
│ ┌─────────────────────────────────────┐ │
│ │ 模式提示条（玻璃卡片）               │ │
│ │ 常规/竞技模式会覆盖部分自定义开关…  │ │
│ └─────────────────────────────────────┘ │
│                                         │
│ 全模式核心控制                          │
│ ┌─────────────────────────────────────┐ │
│ │ [开关] 后台调度 · 总开关             │ │
│ │        关掉就完全不碰后台程序…      │ │
│ ├─────────────────────────────────────┤ │
│ │ [开关] 后台 GPU 让位                 │ │
│ │        被重压的后台用显卡时…        │ │
│ │ ...（共 9 项）                       │ │
│ └─────────────────────────────────────┘ │
│                                         │
│ 自定义预设细节                          │
│ ┌─────────────────────────────────────┐ │
│ │ [🔒开关] 严格 CPU 分区               │ │
│ │          游戏绑性能核…· 当前预设强制 │ │
│ ├─────────────────────────────────────┤ │
│ │ ...（共 5 项，锁定矩阵控制）         │ │
│ └─────────────────────────────────────┘ │
│                                         │
│ 会话附加动作 · 所有模式生效             │
│ ┌─────────────────────────────────────┐ │
│ │ [开关] 竞技模式禁用 CPU 空闲状态     │ │
│ │ ...（共 7 项）                       │ │
│ └─────────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

### 4.2 分组与策略项清单

**分组 1：全模式核心控制**（9 项，均不锁）

| # | 标题 | 说明 | GameMode 属性 | 特殊 |
|---|------|------|---------------|------|
| 1 | 后台调度 · 总开关 | 关掉就完全不碰后台程序… | SuppressBackground | — |
| 2 | 后台 GPU 让位 | 被重压的后台用显卡时给游戏让路… | GpuDemote | — |
| 3 | 冻结静默后台 | 把已经压到最低…彻底暂停… | FreezeBackground | 开前弹确认 |
| 4 | 游戏提优 | 自动认出真正出画面的游戏进程… | BoostGame | — |
| 5 | 后备提优 | 有的游戏被反作弊保护着… | IfeoBoostFallback | — |
| 6 | 保帧线程 | 游戏有上百个线程… | RenderLaneOn | — |
| 7 | 游戏时切换电源计划 | 打游戏时切到 Caelus 竞技电源计划… | PowerPlanSwitch | — |
| 8 | 游戏时免打扰 | 打游戏时不弹系统通知… | NotifQuiet | — |
| 9 | 刷新率守护 | 打游戏时把屏幕保持在最高刷新率… | HzGuard | — |

**分组 2：自定义预设细节**（5 项，锁定矩阵控制）

| # | 标题 | 说明 | GameMode 属性 |
|---|------|------|---------------|
| 10 | 严格 CPU 分区 | 游戏绑性能核、后台赶到能效核… | StrictCoreIsolation |
| 11 | 竞技级压制范围 | 开了就和竞技模式一样狠… | AggressiveSuppression |
| 12 | 暂停后台下载 | 只在自定义模式下由你决定… | PauseDownloads |
| 13 | 暂停索引 / 预取服务 | 只在自定义模式下由你决定… | PauseSvcIndex |
| 14 | 关闭 Game DVR | 只在自定义模式下由你决定… | KillGameDvr |

**分组 3：会话附加动作**（7 项，均不锁）

| # | 标题 | 说明 | GameMode 属性 |
|---|------|------|---------------|
| 15 | 竞技模式禁用 CPU 空闲状态 | CPU 核心不再进入空闲状态… | IdleStateDisable |
| 16 | 游戏时降级桌面视觉效果 | 关闭桌面透明与窗口动画… | VisualFxDowngrade |
| 17 | 自定义重压时清空后台占用的内存 | 把被压制程序占的内存挤出来… | TrimWorkingSet |
| 18 | 对局前清理待机内存 | 开局前把系统攒的缓存内存清一次… | PurgeStandby |
| 19 | 对局期间暂停 Windows 更新 | 打游戏时不让 Windows 更新下载安装… | PauseWindowsUpdate |
| 20 | 不许无故降级 | 系统默认会在你一段时间没碰键鼠后… | PresenceQosOff |
| 21 | 不熄屏不睡眠 | 打游戏期间不让屏幕熄灭… | KeepAwake |

文案使用 `Lang.T(key)` 读取（与 WinForms 版一致的 key），不硬编码。完整 key 映射表见实现计划。

### 4.3 锁定矩阵

5 个自定义项的锁定状态由当前 `PerformancePreset` 决定（复用 WinForms `ApplyPresetPolicy` 逻辑）：

| 属性 | Standard | Competitive | Custom |
|------|----------|-------------|--------|
| StrictCoreIsolation | 锁=关 | 锁=开 | 放开 |
| AggressiveSuppression | 锁=关 | 锁=开 | 放开 |
| PauseDownloads | 锁=关 | 锁=开 | 放开 |
| PauseSvcIndex | 锁=关 | 锁=开 | 放开 |
| KillGameDvr | 锁=关 | 锁=开 | 放开 |

- **锁住**：开关 `IsEnabled=false`，值由预设强制（`SetSilently` 语义），标题追加「 · 当前预设强制」
- **放开**：开关 `IsEnabled=true`，值由用户控制

## 5. 组件设计

### 5.1 PolicyViewModel

```csharp
internal sealed class PolicyItem
{
    public string Title;          // Lang.T(titleKey)
    public string Description;    // Lang.T(descKey)
    public string PropertyName;   // "SuppressBackground" 等
    public bool IsLocked;         // 锁定矩阵结果
    public bool LockedValue;      // 锁定时的强制值
}

internal sealed class PolicyViewModel : ViewModelBase
{
    // 三个分组的 ObservableCollection<PolicyItem>
    public ObservableCollection<PolicyItem> CoreItems { get; }
    public ObservableCollection<PolicyItem> CustomItems { get; }
    public ObservableCollection<PolicyItem> ExtraItems { get; }

    // 每项的开关状态由 GameMode 属性驱动（get/set 经反射或 switch）
    public bool IsOn(string propertyName) { ... }
    public void Set(string propertyName, bool value) { ... }

    // 模式切换后重算锁定矩阵
    public void RefreshLocks() { ... }
}
```

开关状态的 get/set 用 `switch (propertyName)` 直接映射到 GameMode 属性（不反射——21 个属性，显式映射更清晰且编译期检查）。

### 5.2 SettingCard 样式（WPF）

复用 Phase 1.5 的 `CardBorder` 玻璃样式，增加策略卡片专用样式：

- 整卡可点击（点卡片 = 切开关，复刻 WinForms `SettingCard.OnMouseUp` 行为）
- 开关用 WPF ToggleButton（iOS 风格滑动），绑定 `IsChecked`
- 锁定态：`IsEnabled=false` + 半透明蒙版 + 标题后缀
- 悬停：卡片边框微亮（已有 `GlassBorderBrush` + 悬停触发器）

### 5.3 冻结确认对话框

`FreezeBackground` 开启前弹 WPF `MessageBox`（OKCancel），复刻 WinForms 版的 `gm.freeze.warn` 警告文案。取消则开关回退。

## 6. 导航接线

MainWindow 的 NavChecked 路由增加"优化策略"分支：

```csharp
PageHost.Content = rb == NavOverview ? new OverviewView { DataContext = ... }
    : rb == NavPolicy ? new PolicyView { DataContext = policyVm }
    : new PlaceholderView();
```

`NavPolicy` 是 `优化策略` RadioButton 的 x:Name（Phase 1 已建，当前导航到 PlaceholderView）。

## 7. 截图探针扩展

`--wpf-shot` 矩阵增加策略页截图（巡航深色 1 张），验证密集卡片在座舱材质下的可读性：

```
wpf-policy-dark-cruise.png
```

## 8. 范围

**做**：
1. GameMode 实例构造 + 传递
2. PolicyViewModel（21 项 + 锁定矩阵）
3. PolicyView.xaml（3 分组 + 滚动 + 模式提示条）
4. SettingCard WPF 样式（玻璃卡片 + ToggleButton + 整卡点击 + 锁定态）
5. 冻结确认对话框
6. 导航接线（NavPolicy → PolicyView）
7. 模式切换 → 锁定矩阵刷新
8. 截图探针扩展（策略页 1 张）

**不做**：
- 游戏库页迁移（Phase 3）
- 添加游戏对话框（Phase 3）
- 托盘右键菜单
- 实时指标接线（概览页）
- 策略下发的后台 worker（正式宿主接管时补）

## 9. 验收标准

- 21 个开关显示正确文案 + 说明
- 开关点击真实读写 GameMode 属性 + Settings 持久化
- 模式切换后锁定矩阵正确刷新（5 项锁/解锁）
- 锁定项标题有「 · 当前预设强制」后缀
- 冻结开关开启时弹确认对话框
- 自测 166 PASS / 0 FAIL / 3 SKIP 不回归
- 策略页截图玻璃卡片可读、不拥挤
- WinForms 构建（`build.cmd`）零回归
