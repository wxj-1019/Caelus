# 设置页主题定制：三模式强调色 + 深浅跟随系统

日期：2026-08-21
状态：待确认（确认后分期实施）
版本：v2（已并入审查修正，见 §10）

## 0. 现状与目标

**现状**（双轴四槽，优先级从低到高）：
1. `Colors.Dark/Light.xaml` —— 明暗色板（表面/文字/语义色）
2. `Mode.Standard/Competitive/Custom.xaml` —— 三模式强调色 + Aurora（靛蓝/蜜桃橙/暗金）
3. `Caelus.theme.xaml` —— 应用目录用户文件覆盖层（需过 ModeKeys 契约校验，无 UI）
4. 高对比覆盖层（系统高对比时自动叠加）

已有入口：设置页「亮色主题」开关（仅手动深/浅二态）；概览页模式档位切换。

**目标**：设置页新增「主题定制」区——
1. 三模式强调色自定义（预设色板 + 自定义 hex，即时预览）
2. 深浅模式三态（手动深 / 手动浅 / **跟随系统**）
3. 一键恢复默认棉花糖配色

## 1. 架构：运行时强调色覆盖层（第五槽）

在 Mode 槽之后、User 文件之前插入**运行时构造的覆盖字典**（非 XAML 文件，代码生成）：

```
Colors → Mode → AccentOverride(新) → User(Caelus.theme.xaml) → Accessibility
```

- 仅覆盖**当前模式**的槽——切换模式时按当前模式的注册表色重新构造，即时生效
- `ThemeManager.ApplyAccentOverride()`：读注册表，空值 = 不叠加（用预设）
- User 文件优先级保持最高（文件级完整覆盖仍强于本功能，兼容既有契约）

### 1.1 衍生色计算（单色进、全家桶出）

用户给一个 `#RRGGBB`，代码派生全部 ModeKeys：

| 衍生键 | 规则 |
|---|---|
| `AccentPrimaryColor/Brush` | 用户色原值 |
| `AccentSecondaryColor/Brush` | HSL 亮度 +12% |
| `AccentSoftBrush` | 用户色 + alpha 0x16 |
| `AccentEdgeBrush` | 用户色 + alpha 0x44 |
| `AccentGlowColor` | 用户色原值 |
| `AccentGradientBrush` | Primary → Secondary 线性渐变 |
| `ModeAccentOnDarkBrush` | 亮度 +18%（深底可读） |
| `ModeAccentOnLightBrush` | 亮度 −25%（浅底可读，自动满足对比度） |
| `HeroTitleOnDark/LightBrush` | 同 ModeAccent 两变体 |
| `OnAccentBrush` | **对比度取大者**（§10.1 修正）：计算 `contrastRatio(accent, #FFFFFF)` 与 `contrastRatio(accent, #2B1F1A)` 取高者；两者均 <4.5:1 时取更高者并记日志警告一次 |
| `AuroraPrimary/Secondary/TertiaryColor` | 用户色 / +12% / +24%（保持 Aurora 联动三档） |
| `Aurora*FadeColor` | 对应色 alpha 0 |
| `Ambient*Brush` / Opacity / DriftSeconds | **不覆盖**——Ambient*Brush 在预设字典内经 DynamicResource 引用 AuroraPrimaryColor，覆盖 Aurora 色即自动联动；节奏沿用预设 |

**资源类型注意**：`AccentGlowColor` 是 `Color`，`OnAccentBrush` 是 `SolidColorBrush`，`AccentGradientBrush` 是 `LinearGradientBrush`——构造时类型必须与预设字典完全一致，测试覆盖。

**覆盖层重建时序**：`Apply`/`ApplyAccentOverride` 重建顺序必须保持——先 Remove 旧 override 再 Add 新的；若 `user != null`，需 Remove(user) → Add(override) → Add(user)（user 始终最后）；accessibility 同理最后。连续调用须幂等（不泄漏字典，测试覆盖）。

## 2. 注册表键

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `AccentStandard` | string | 空 | 常规模式强调色 `#RRGGBB`，空=预设靛蓝 |
| `AccentCompetitive` | string | 空 | 竞技模式，空=预设蜜桃橙 |
| `AccentCustom` | string | 空 | 自定义模式，空=预设暗金 |
| `UiToneMode` | int | −1 | 0=手动深 1=手动浅 2=跟随系统；−1=未设置（回退旧 `UiLight` 兼容读取：UiLight=true→1，false→0） |

## 3. 设置页 UI（「外观」区升级）

```
外观
├─ 深浅模式      [深色 | 浅色 | 跟随系统]   ← SegmentedControl 三态
├─ 常规 · 强调色  [■靛蓝] ●●●●●●●●●●  [____] 重置
├─ 竞技 · 强调色  [■蜜桃] ●●●●●●●●●●  [____] 重置
├─ 自定义 · 强调色 [■暗金] ●●●●●●●●●●  [____] 重置
└─ 恢复默认配色                      [恢复]  ← GhostButton
```

- **色板 swatch**：每模式一行 10 个预设色圆点（该模式默认色居首，选中态描边）
- **hex 输入**：TextBox 支持 `#RRGGBB` 与 `#RGB` 短格式（解析时展开）；非法值红框提示且不写入
- **即时预览**：选中 swatch 或输入合法 hex 立即 `ApplyAccentOverride()`，全应用强调色即时换肤
- **单行重置**：清空该模式键回到预设
- Lang 键 8 个：`set.theme`/`set.theme.n`/`set.theme.tone`/`set.theme.accent`/`set.theme.accent.hex`/`set.theme.reset`/`set.theme.reset.all`/`set.theme.follow`

### 预设色板（10 色）

`#5E5CE6` 靛蓝 · `#FF8A5C` 蜜桃橙 · `#B8933E` 暗金 · `#E84C88` 品红 · `#3DD68C` 湖绿 · `#4C9BE8` 天蓝 · `#8B5CF6` 紫罗兰 · `#F2555A` 珊瑚红 · `#14B8A6` 青碧 · `#64748B` 石墨

> 注：湖绿/珊瑚红与 Success/Danger 语义色同值——选为强调色会与状态按钮（删除=红/成功=绿）色相混淆，允许选择但 swatch Tooltip 提示「与状态色相近」。

## 4. 深浅跟随系统（§10.2 修正：事件监听替代轮询）

- `UiToneMode=2` 时：挂 `HwndSource` hook 监听 `WM_SETTINGCHANGE (0x001A)`，Windows 主题变化即时响应（零轮询开销）
- 回调读 `HKCU\...\Themes\Personalize\AppsUseLightTheme`（1=浅 0=深）→ `ThemeManager.Apply(tone)` + CrossFade（复用 ModeChanged 集中过渡）
- **初始化探测**：宿主启动且 `UiToneMode=2` 时立即读一次注册表应用当前系统主题（不等首次变更）
- 选深/浅手动档时注销 hook；旧 `UiLight` 键只读兼容，不再写入

## 5. WinForms 侧边界

本期**仅 WPF**。明确：**WinForms 设置页不新增这些 UI**——避免生产宿主用户看到「改了不生效」的选项。WinForms 读同一注册表键同步 `ModePalette`/`Palette` 列为 P3 后续项。

## 6. 实施分期

| 期 | 内容 | 预估 |
|---|---|---|
| P1a | ThemeManager.ApplyAccentOverride + 衍生色计算 + 覆盖层时序 + 自测 8 项 | ~150 行 |
| P1b | 设置页色板/hex/重置 UI + 即时预览 + Lang 键 | ~150 行 |
| P2 | 深浅三态 SegmentedControl + WM_SETTINGCHANGE 监听 + 初始化探测 + UiLight 兼容迁移 | ~100 行 |
| P3（可选） | WinForms ModePalette 同步 + 当前配色导出为 Caelus.theme.xaml | ~120 行 |

## 7. 测试计划

自测新增（`SelfTests.UiShared.cs` 扩展，纯逻辑可单测）：
1. hex 解析：`#RRGGBB`/`#RGB`/非法（#GGG/#FFF? 非法标记规则）/空串
2. 衍生色：SoftBrush alpha=0x16、EdgeBrush alpha=0x44、Secondary 亮度单调提升
3. **OnAccent 对比度取大者**（§10.1）：蜜桃橙 → 深可可；纯白 → 深可可；纯黑 → 白；断言所选对比度 ≥ 4.5:1（普通文字 WCAG AA；均不达时取更高并警告）
4. ModeAccentOnLight 暗化后与浅底对比度 ≥ 3:1（大字/填充语义，抽样 10 预设色）
5. 资源类型匹配：AccentGlowColor=Color、OnAccentBrush=SolidColorBrush、GradientBrush=LinearGradientBrush
6. 覆盖层顺序与幂等：连续 50 次 ApplyAccentOverride 字典数不增长；override 不覆盖 User 文件键
7. 跟随系统初始探测：UiToneMode=2 构造时读注册表（mock）
8. UiToneMode 兼容：−1 + UiLight=true → 1（浅）；−1 + UiLight=false → 0（深）

## 8. 风险与对策

| 风险 | 对策 |
|---|---|
| 用户选浅色强调色导致按钮文字不可读 | OnAccent 对比度取大者（§10.1 修正后的规则），下限 4.5:1 |
| 浅色主题下强调色过淡 | ModeAccentOnLightBrush 自动 −25% 亮度，测试 §7.4 校验 |
| hex 输入错误 | 校验 + 红框提示 + 不写入非法值；合法才应用（中间态不触发换肤） |
| 与 Caelus.theme.xaml 冲突 | 文件层始终最后并入（优先级最高），文档注明 |
| WM_SETTINGCHANGE 高频触发 | hook 内去抖 500ms 再 Apply |

## 9. 设计决策记录

| 决策 | 选择 | 理由 |
|---|---|---|
| 覆盖层形态 | 运行时代码构造，非 XAML 文件 | 免文件 IO、即时预览、无需契约校验 |
| 覆盖范围 | 仅当前模式槽 | 省三套构造、切换时按需重建 |
| 强调色入口 | 色板 + hex 双轨 | 色板防错、hex 自由 |
| OnAccent 判定 | 对比度取大者 | §10.1：固定亮度阈值对中亮强调色会配错字色 |
| 深浅跟随 | WM_SETTINGCHANGE | §10.2：.NET 4 无系统主题事件；监听即时且零开销，轮询延迟 30s 不可接受 |
| Aurora 联动 | 用户色三档亮度，Ambient 不覆盖 | DynamicResource 引用自动跟随；节奏不动 |
| 本期范围 | 仅 WPF，WinForms 不加 UI | 双宿主同步扩大回归面；避免生产宿主「改了不生效」误导 |

## 10. 审查修正记录（v2）

### 10.1 OnAccentBrush 判定（P0 设计错误修正）

v1：相对亮度 > 0.55 → 深可可，否则白。
验证：蜜桃橙 `#FF8A5C` 相对亮度 ≈ 0.40 < 0.55 → 会配白字，对比度仅 2.33:1（严重不达 4.5:1）；实际棉花糖配深可可 6.44:1。
**修正**：不做固定亮度阈值，直接计算 `contrastRatio(accent, #FFFFFF)` 与 `contrastRatio(accent, #2B1F1A)` 取大者。

### 10.2 深浅跟随实现（P1）

v1：30 秒 DispatcherTimer 轮询注册表。
问题：延迟长、空转开销。
**修正**：HwndSource hook `WM_SETTINGCHANGE`（§10.2），并补宿主启动初始化探测（UiToneMode=2 立即应用当前系统主题）。

### 10.3 WinForms UI 边界（P1）

v1 只说「本期仅 WPF」。**补充明确**：WinForms 设置页不新增这些 UI，防止生产宿主用户误操作。

### 10.4 测试阈值与补充（P2）

- OnAccentBrush 断言阈值 3:1 → **4.5:1**（普通按钮文字 WCAG AA）
- ModeAccentOnLight 保留 ≥3:1（大字/填充语义）
- 补充：hex `#RGB` 短格式、资源类型匹配、重复应用幂等、初始化探测、UiToneMode 兼容、语义色混淆提示
