# 极简 Linear · 新页面骨架（Page Skeleton）

新增一个 UI 页面 = **复制下面骨架 → 改内容 → 在导航注册**，全程不写新样式。
组件样式全部来自 `wpf/Themes/Styles.xaml`（组件库），令牌来自 `Tokens.xaml` / `Colors.*.xaml`，强调色来自 `Mode.*.xaml`。

## 1. 页面骨架（XAML）

复制为 `wpf/Views/MyNewView.xaml`（并把 `x:Class` 换成你的类名）：

```xml
<UserControl x:Class="CaelusApp.WpfHost.Views.MyNewView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:CaelusApp.WpfHost"
             AutomationProperties.Name="页面名">
  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="0,0,0,16">

      <!-- 页头：排版驱动层级，不要新写样式 -->
      <StackPanel x:Name="ZoneHeader" Margin="0,0,0,16">
        <TextBlock Text="页面标题" Style="{DynamicResource PageHeader}"/>
        <TextBlock Text="一句话副标题" Style="{DynamicResource PageSubtitle}" Margin="0,4,0,0"/>
      </StackPanel>

      <!-- 状态横幅（可选）：整宽信息条，语义变体见下 -->
      <Border Style="{DynamicResource StatusBanner}" Margin="0,0,0,16" Padding="14,11">
        <TextBlock Text="就绪 · 描述当前状态" FontSize="{DynamicResource FontSizeCaption}"
                   Foreground="{DynamicResource TextPrimaryBrush}"/>
      </Border>

      <!-- 分组容器 + 设置行（示例：一个开关行） -->
      <TextBlock Text="分组标题" Style="{DynamicResource PageSubtitle}" FontWeight="SemiBold"
                 Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,0,8"/>
      <Border Style="{DynamicResource SettingsGroup}" Margin="0,0,0,16">
        <Border Style="{DynamicResource PolicyRow}" local:RowToggle.Enabled="True">
          <DockPanel>
            <ToggleButton Style="{DynamicResource PolicyToggle}" DockPanel.Dock="Right"
                          VerticalAlignment="Center" Margin="16,0,0,0"
                          AutomationProperties.Name="某项开关"/>
            <StackPanel VerticalAlignment="Center">
              <TextBlock Text="某项开关" FontSize="{DynamicResource FontSizeCaption}"
                         FontWeight="SemiBold" Foreground="{DynamicResource TextPrimaryBrush}"/>
              <TextBlock Text="一句话说明它做什么" FontSize="{DynamicResource FontSizeSmall}"
                         Foreground="{DynamicResource TextSecondaryBrush}" TextWrapping="Wrap" Margin="0,3,0,0"/>
            </StackPanel>
          </DockPanel>
        </Border>
      </Border>

    </StackPanel>
  </ScrollViewer>
</UserControl>
```

## 2. 导航注册（3 处）

`wpf/MainWindow.xaml`：在对应分组下加一个 `NavItem`（复用 `IconView` 图标 + `NavChecked`）：

```xml
<RadioButton x:Name="NavMyNew" Style="{DynamicResource NavItem}" GroupName="nav"
             Checked="NavChecked" AutomationProperties.Name="导航：页面名">
  <StackPanel Orientation="Horizontal">
    <controls:IconView Key="IconOverview" Width="15" Height="15" VerticalAlignment="Center"/>
    <TextBlock Text="页面名" Margin="9,0,0,0" VerticalAlignment="Center"/>
  </StackPanel>
</RadioButton>
```

`wpf/MainWindow.xaml.cs`：声明视图实例并在 `NavChecked` 路由：

```csharp
// 字段区
private readonly MyNewView myNewView;
// 构造里
myNewView = new MyNewView { DataContext = myNewVm };
// NavChecked 里
else if (rb == NavMyNew) next = myNewView;
```

`NavigateToForShot`（如需纳入截图矩阵）：加一个分支返回该视图。

## 3. 组件速查（全部在 Styles.xaml，直接 `Style="{DynamicResource Xxx}"`）

| 用途 | 组件 key |
|---|---|
| 卡片/容器 | `CardBorder` `HeroCardBorder` `SettingsGroup` `SettingsRow` `MetricPanel` `EmptyState` |
| 横幅 | `StatusBanner` + `Info/Success/Warning/ErrorStatusBanner` |
| 徽章/标签 | `StatusBadge` + 5 个语义变体、`StatusChip` `WarnTag` `SegValueChip` `EmptyChip` `GameAvatar` |
| 按钮 | `PrimaryButton`（主） `GhostButton`（次） `DangerButton`（危险） |
| 开关/输入/列表 | `PolicyToggle` `InputBox` `ListItem` |
| 行 | `PolicyRow`（可点行，配 `local:RowToggle.Enabled="True"` 整行切开关） `ResultRow` |
| 排版 | `PageHeader` `PageSubtitle` `CardLabel` `DisplayNumber` `MetricNumber` |
| 导航 | `NavItem` `NavGroupLabel` `SegmentHost` `SegmentItem` |

## 4. 令牌速查

- 字号：`FontSizeCaption(12) FontSizeSmall(11) FontSizeXs(10) FontSizeBody(14) FontSizeRegion(13) FontSizeSection(22) FontSizeEmpty(16) FontSizeMono(12) FontSizeMicro(9)`
- 间距：`SpaceXs(4) SpaceSm(8) SpaceMd(12) SpaceLg(16) SpaceXl(24) Space2Xl(32)`
- 圆角：`RadiusSm(6) RadiusMd(10) RadiusLg(14)`
- 颜色：`Background/Surface0/1/2Brush`、`TextPrimary/Secondary/TertiaryBrush`、`BorderSubtle/StrongBrush`、语义 `Success/Warning/Danger/Info`(+`Soft/Edge`)、强调 `ModeAccentBrush AccentPrimaryBrush AccentSoftBrush AccentEdgeBrush`

## 设计纪律（极简 Linear）

- 层级靠**排版与留白**，不加阴影/辉光/渐变描边/粒子。
- 强调色**单一**、克制使用（主按钮、选中态、状态竖条、进度）。
- 发丝线描边（`BorderSubtleBrush`）表达卡片边界，悬停仅浅底（`Surface1Brush`）。
- 动效只保留克制的入场/反馈；不新增循环装饰动画。
