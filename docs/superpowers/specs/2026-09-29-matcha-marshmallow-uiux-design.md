# 苹果动效升级 + 亮色抹茶棉花糖主题（双轨 UI/UX 优化）

## 0. 背景与目标

应用已完成单 WPF 化（eedf711），视觉底子：暗紫极光驾驶舱（暗色）+ 棉花糖天空奶油底（亮色），
动效底子：QuinticEase iOS 减速曲线、BackEase 入场/按压弹簧、圆角 12/18/24、圆头线性图标。
本次目标：在**不动暗色轨**的前提下——

- **动效升级**（双轨共享）：弹簧参数化三档、侧栏滑动指示器、页面错落入场、数字滚动
- **亮色主题重做**：抹茶拿铁 pastel 棉花糖（米白底 + 抹茶渐变 + 深墨绿文字）

## 1. 决策记录（brainstorm 确认项，2026-09-29）

| 问题 | 结论 |
|------|------|
| 棉花糖风格走多远 | **双轨制**：暗色保持极光驾驶舱零改动；亮色彻底 pastel 棉花糖 |
| 动效补强范围 | 交互层弹簧化 + 导航/页面过渡（滑动指示器、staggered 入场、数字滚动）；**不做**庆祝粒子 |
| pastel 方向 | **抹茶拿铁**：`#7CB68F → #A8D5B5`，米白底 `#FAF8F1`（可视化对比页四选一） |
| 可爱剂量 | **适中**：空态小插画 3 处 + 微文案软糯化；图标已是圆头圆接头（现状满足，不重绘） |
| 实施方案 | **方案一**：token 深化 + 动效升级，控件模板与布局零改动；两批提交（先动效后主题） |

关键事实核查（实施前已验证）：

- 主题机制：`ThemeManager` 四槽资源字典（colors 明暗槽 → mode 模式槽 → 用户强调色覆盖槽 → 可达性槽）
- `ModeAccentBrush` 为 tone-aware：`Colors.Light.xaml:101` 指 `ModeAccentOnLightColor`（Mode 文件明暗键分离），亮色换肤无需动暗色
- 按钮/Toggle/进度条模板全部 `DynamicResource` 画刷键：**纯色键换渐变画刷即可，模板零改动**
- 现有亮色主按钮策略为「深可可字落糖果底」（Mode.Standard.xaml 注释），本设计延续：填充奶绿 + 深墨绿字
- net4 WPF **无原生 SpringAnimation**：弹簧用「参数表 → BackEase Amplitude + 时长」实现

## 2. 范围与不做清单

**做**：Motion/UiMotion 扩展、侧栏滑动指示器、各 View 错落入场接线、数字滚动、
`Colors.Light.xaml` 抹茶重做、`Mode.*.xaml` 亮色键、空态插画 3 处、微文案、截图矩阵重摄。

**不做（YAGNI）**：控件模板重排、布局改动、**暗色任何改动**、庆祝粒子/撒花、
Aurora 光晕结构改造（见 §4.4）、图标重绘、竞技/自定义模式亮色强调色变更。

## 3. 动效规格

### 3.1 弹簧参数化（`Motion.Spring` 统一入口）

`UiMotion` 新增三档弹簧预设，`Motion.Spring(from, to, preset)` 为唯一弹簧入口：

| 预设 | 时长 | BackEase Amplitude | 用途 |
|------|------|------|------|
| Gentle | 300ms | 0.3 | 页面/卡片入场位移（沿用现 RiseIn 手感） |
| Snappy | 240ms | 0.45 | 侧栏指示器滑动、开关拇指 |
| Bouncy | 180ms | 0.5 | 按钮按压释放（沿用现 BuildPressSpring，ButtonPressMs×2 单段） |

- 现有 `RiseIn`/`PressTo` 内部改走 `Motion.Spring`：**行为不变、实现收口**（回归安全）
- opacity 不走弹簧（过冲被 clamp 无意义），维持 QuinticEase
- 自测：三档预设 → (时长, Amplitude) 映射表值固定，防意外漂移

### 3.2 侧栏滑动指示器（iOS 分段控件手感）

导航现状：`MainWindow.xaml` 导航列 = `RadioButton`（`NavItem` 样式，`GroupName="nav"`，`Checked="NavChecked"`）。

- 导航列根部（所有导航项之下层）加一枚滑动 pill：`Border`，`AccentSoftBrush` 底 + `RadiusSm` 圆角
- `NavChecked` 时用 `TransformToVisual` 计算目标项相对导航列的 Y/高度，Snappy 弹簧滑动 `TranslateTransform.Y`
- 首次布局、窗口尺寸变化、Reduced 策略下直接落位（无动画）
- 导航项选中态视觉（现有静态高亮）保留，pill 在其底层滑动，二者不冲突

### 3.3 页面错落入场（staggered）

- 各 View 顶层区块（Hero / 内容卡 / 列表区）接 `RiseIn(element, delayMs)`，delay 按 0/40/80/120ms 递增，**最多 4 档**防拖沓
- 只接线不改布局：现有 `RiseIn` 已支持延迟与 Reduced 直落位
- `Reveal`/`CrossFade` 现状保持（页面切换 180ms 淡化不变）

### 3.4 数字滚动

- `Motion.NumberRoll(TextBlock, from, to, format)`：220ms（`UiMotion.NumberRollMs`）DoubleAnimation 驱动插值，每帧 `string.Format(format, value)` 落文本
- 插值函数拆为纯逻辑（`Interpolate(from, to, t)`），便于自测
- 落点：概览页统计数字、体检页评分；Reduced 策略直出终值
- 自测：`Interpolate` 边界（t=0/1/中点）、Reduced 直出终值

### 3.5 硬约束

- 全部动画走 `RenderTransform`/`Opacity`（合成器线程），不做逐帧 CPU 动画
- 无限动画维持 15fps 节流（`Motion.Throttle` 现状），本次不新增任何常驻动画
- 游戏掌权/竞技模式不新增动画路径；Reduced 策略不变（90ms 淡入、无位移无缩放）

## 4. 抹茶色板（暗色零改动）

### 4.1 `Colors.Light.xaml` token 清单

| token | 现值 | 新值 | 说明 |
|------|------|------|------|
| BackgroundColor | `#FBF4EE` | `#FAF8F1` | 米白（暖调减弱，衬奶绿） |
| Surface0Color | `#FFFFFF` | `#FFFFFF` | 白棉花糖卡片，不变 |
| Surface1Color | `#FFF7F2` | `#FAF6EE` | 奶白 |
| Surface2Color | `#F5EDE7` | `#F3EFE4` | 奶咖 |
| TextPrimaryColor | `#2B1F1A` | `#26332A` | 墨绿（替代暖可可） |
| TextSecondaryColor | `#6B5D55` | `#5E7064` | 灰绿 |
| TextTertiaryColor | `#7F6E64` | `#66766A` | 灰绿，AA 达标（见 §4.3） |
| BrandColor | `#8B7CF6` | `#6FAF88` | 抹茶主色：实心小元素（图标/chip/徽标）用 |
| SuccessColor | `#248A3D` | `#27755C` | 深苔绿，与奶绿品牌拉开明度+饱和度 |
| InfoColor / WarningColor / DangerColor | 不变 | 不变 | 语义色与抹茶无冲突 |
| 新增 BrandGradientBrush | — | `#7CB68F → #A8D5B5`（135°） | 亮色槽 `AccentPrimaryBrush` 改指此渐变：主按钮/进度条自动渐变，模板零改动 |
| 新增 TextOnAccentColor | — | `#1E3328` | 奶绿填充上的深墨绿字（白字 2.35:1 不达标，见 §4.3） |

柔色层（`*SoftBrush`/`*EdgeBrush` 低 alpha 系列）随主色同步换绿调，alpha 结构不变。

### 4.2 `Mode.*.xaml` 亮色键（只动一行）

| 键 | 现值 | 新值 | 说明 |
|------|------|------|------|
| Standard `ModeAccentOnLightColor` | `#6D5CE0` | `#3F7A58` | 深抹茶，白底 5.08:1 全场景 AA |
| Competitive `ModeAccentOnLightColor` | `#B84518` | 不变 | 焙茶橙：战斗语义，且焙茶×抹茶和谐 |
| Custom | — | 不动 | 用户强调色覆盖机制不变 |
| 全部 `ModeAccentOnDarkColor` | — | 不动 | 暗色轨零改动 |

### 4.3 WCAG 实测表（Python 精确计算，2026-09-29）

| 组合 | 对比度 | 判定 |
|------|------|------|
| Primary `#26332A` on `#FAF8F1` / `#FFFFFF` | 12.43 / 13.21 :1 | AA ✓ |
| Secondary `#5E7064` on `#FAF8F1` / `#FFFFFF` | 4.97 / 5.28 :1 | AA ✓ |
| Tertiary `#66766A` on `#FAF8F1` / `#FFFFFF` | 4.53 / 4.81 :1 | AA ✓ |
| 墨绿字 `#1E3328` on 抹茶 `#7CB68F` / 渐变尾 `#A8D5B5` | 5.74 / 8.24 :1 | AA ✓（按钮文字方案） |
| ~~白字 on 抹茶 `#7CB68F`~~ | 2.35 :1 | FAIL → 否决，按钮不用白字 |
| Success `#27755C` on `#FFFFFF` | 5.55 :1 | AA ✓ |
| Standard 强调色 `#3F7A58` on `#FFFFFF` / `#FAF8F1` | 5.08 / 4.78 :1 | AA ✓ |
| Competitive `#B84518` on `#FFFFFF`（维持现值） | 5.38 :1 | AA ✓ |
| Info `#2F7BD6` / Warning `#C07A1B`（维持现值） | 4.26 / 3.48 :1 | 与现主题同档，仅用于大字/图标/柔色块场景 |

### 4.4 Aurora 光晕决策：亮色保持薰衣草

`Aurora*Color`/`Ambient*Brush` 定义在 Mode 文件（明暗共享一槽）：改成绿调必波及暗色轨。
且低透明度（0.08–0.18）薰衣草雾落在米白底上与抹茶主体构成「抹茶 + 薰衣草奶盖」的和菓子配色，和谐。
**结论：不动。** 若未来要绿调光晕，需 ThemeManager 支持 mode×tone 六槽结构（另行立项，不在本次范围）。

## 5. 插画与文案

### 5.1 空态插画 3 处

- 新建 `wpf/Themes/Illustrations.xaml`：3 幅扁平圆润 `StreamGeometry` 插画（96 视窗、多 path 组合），
  颜色全部 `DynamicResource` 引用主题 token——**双主题通用**，暗色空态同样受益
- 落点（替换现有几何图标位，不改布局）：
  1. `LibraryView` 空态（现 48px `EmptyHeroIcon` → 96px 插画）
  2. `WhitelistView` 空态（`EmptyPanel`）
  3. `AuditView` 体检达标态（分数 Hero 旁）
- csproj 逐文件登记：`<Page Include="Themes\Illustrations.xaml" />`（老坑：wpf csproj 无 glob）

### 5.2 微文案软糯化（示例，实施时可调味）

| 位置 | 现文案 | 新文案 |
|------|------|------|
| 游戏库空态标题 | 添加你的第一个游戏 | 还没有游戏哦，拖个 exe 进来养一只 |
| 白名单空态 | （EmptyTitle 绑定值） | 白名单空空的，加一条规则试试 |
| 体检达标 | （达标态文案） | 状态满分，去尽情玩耍吧 |

- 文案跟随现有机制：XAML 硬串改硬串，`Lang` key 改三语（`TestEveryLangKeyIsDefined` 自测会校验引用-定义闭环）
- 仅中文语气调味；en/ja 同步翻译（如该文案走 Lang）

## 6. 实施批次与改动清单

### 批 1：动效升级（双轨共享，独立可验收）

| 文件 | 改动 |
|------|------|
| `src/UiShared/UiMotion.cs` | + 弹簧三档预设常量 |
| `wpf/Motion.cs` | + `Spring` 统一入口、`NumberRoll`、插值纯函数；`RiseIn`/`PressTo` 收口到 Spring（行为不变） |
| `wpf/MainWindow.xaml(.cs)` | 导航列滑动 pill + `NavChecked` 接线 |
| `wpf/Views/*.xaml(.cs)`（13 页） | 顶层区块 RiseIn staggered 接线（0/40/80/120ms，≤4 档） |
| `tests/SelfTests.*.cs` | + 弹簧映射表断言、`Interpolate` 边界、Reduced 直出 |

### 批 2：抹茶主题 + 插画 + 文案

| 文件 | 改动 |
|------|------|
| `wpf/Themes/Colors.Light.xaml` | §4.1 全表 + `BrandGradientBrush` + `TextOnAccentColor` + 柔色层绿调化 |
| `wpf/Themes/Mode.Standard.xaml` | `ModeAccentOnLightColor` 一行（§4.2） |
| `wpf/Themes/Illustrations.xaml` | 新建 3 幅插画 + csproj `<Page>` 登记 |
| `wpf/Views/LibraryView/WhitelistView/AuditView` | 插画落点接线 |
| 文案（XAML 硬串 / Lang 三语） | §5.2 |
| `docs/shots/` | 全矩阵 76 张重摄（批 2 验收） |

## 7. 验证门禁

1. **批 1**：`dev.cmd test` FAIL 0（含新增自测）；`scripts/wpf-motion-stress.ps1` 动效压测
2. **批 2**：`dev.cmd test` FAIL 0；`scripts/shoot-all.ps1` 重摄 76 张矩阵（13 页 × 明暗 × 三模式），
   人工核对亮色 38 张 + 暗色 38 张无意外偏移（暗色只允许时间戳类噪声差异）；
   `scripts/app-smoke-test.ps1` 真机冒烟
3. 自测计数若增加：三语 README + `plan/milestones/M12` 同步（沿用既有惯例）
4. 全程 C# 5 语法上限（无插值串/表达式体/null 条件运算符）

## 8. 风险与备注

- **net4 无原生 SpringAnimation**：三档弹簧是 BackEase 参数表的「近似弹簧」，不是物理弹簧；
  手感调参以矩阵截图 + 真机为准，Amplitude 可在实施期微调（自测同步更新）
- **渐变画刷替换的边界**：`AccentPrimaryBrush` 换渐变后，所有引用位（主按钮/进度条等）同帧生效；
  若个别引用位渐变效果不佳（如小尺寸 chip），实施期退回该位纯色引用并记录
- **字段绑定坑**（c67b14b）：若插画/指示器新增绑定类，成员一律属性，自测已有 `TestReleaseNoteRowBindable` 范式可仿
- **ModeChanged 静态事件强引用**：订阅者必须 Unloaded 退订（ThemeManager.cs 注释既有约束），接线指示器时遵守
- **文案三语闭环**：`TestEveryLangKeyIsDefined` 会扫 `Lang.T` 引用，先加定义后引用

- **2026-09-29 实施偏差回写**（随落地 commit 同步）：
  1. §3.3 错落入场 12/13 页已存在，仅补 ActivityView
  2. §3.4 NumberRoll 落点仅体检 Score（概览 Hero 为 GrantedTitle 字符串大字，滚动需 VM 改造，YAGNI 移出）
  3. §4.1 TextOnAccentColor 未新增，复用既有 OnAccentBrush 桥接（OnAccentOnLightColor #1E3328）
  4. §4.1 BrandGradientBrush 未新增，渐变直接落在色板槽 AccentPrimaryBrush（StartPoint 0,0 EndPoint 1,1）
  5. §4.2「只动一行」修正为 accent 家族明暗桥接：6 画刷迁色板槽 + 模式档 9 色键（竞技/自定义 OnLight=现值，视觉不变）；ModeAccentOnLightColor #6D5CE0→#3F7A58 按原案落地
  6. §5.2 体检达标走新增 IsExcellent 触发位（插画+caption），HealthLabel 不动
  7. §4.2 桥接实测追加修正（e8d7fa3）：桥接画刷的 DynamicResource 在字典实例化时一次性求值固化，缓存色板实例会让竞技/自定义渲染成常规紫（截图矩阵实证）——ThemeManager.Apply 改为模式槽先换、色板槽后换且不缓存重建；用户主题校验改用 UserThemeKeys 旧 22 键快照防老主题误判
  8. §4.1 品牌色断言同步：TestPaletteSemantics 亮侧 BrandColor 期望 #8B7CF6→#6FAF88（既定色值变更的测试跟随，非规格改动）
  9. §5.1 文案「三语」按现状只落 zh：Lang.cs 当前为中文单语值（Cur 恒 0），en/ja 数组不存在可改
  10. 验收基线说明：HEAD 历史截图残留本机自定义青色强调色（非预设），本次 82 张矩阵为无覆盖干净基线重摄——暗色三档橙/金/紫归位，故暗色张张有差异属预期而非漂移
  11. 冒烟脚本健壮性：app-smoke-test.ps1 测试靶进程 mspaint.exe 在本机（Store 版 Paint 无执行别名）不存在，加 notepad.exe 回退，语义不变（非游戏 GUI 进程应判 NONE 且优先级/亲和性零副作用）
  12. §5.1 插画载体改道（2026-09-30 坑、2026-10-01 收尾）：`Illustrations.xaml` 落地后真机验收发现 BAML 实例化的 Canvas/Shape 子树（资源注入与视图内联同病）在本机 net4+25H2 WPF 渲染管线中确定性丢件（盾牌主体/右上点消失、勾纵向位移，位置敏感），弃用整个文件——删除 `Themes/Illustrations.xaml` 及 App.xaml 合并字典与 csproj `<Page>` 登记，改 `Controls/IllusHost.cs`：与 IconView 同族的 OnRender `DrawingContext` 直绘（3 幅几何逐笔移植、96 视窗），画刷经自定义 DP + `SetResourceReference` 保留 DynamicResource 语义，明暗/模式换肤联动不受损；2026-10-01 真机暗/亮双主题复验白名单盾形插画完整（盾/勾/双点齐、无调试残留、换肤正确）
