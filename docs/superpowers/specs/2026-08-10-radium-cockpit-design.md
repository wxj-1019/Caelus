# Caelus Radium 座舱化设计规格

**版本**: 1.0  
**日期**: 2026-08-10  
**状态**: 待实现  
**作者**: zenjiro  
**上游规格**: `2026-08-09-ui-redesign-design.md`（本规格修订其视觉系统章节）

---

## 1. 定位与上游规格的关系

本规格将 Radium 设计理念应用于 Caelus UI 重构，是在 Phase 1（WPF 骨架 + 设计系统 + 概览页，已验收）基础上的**视觉材质层演进**，记为 **Phase 1.5 · Radium 座舱化**。

**修订上游规格的章节**：

| 上游章节 | 修订方式 |
|----------|----------|
| §3.1 色彩 | 追加模式氛围色系统（本规格 §4.2）；中性色与状态语义色表**保持不变** |
| §4 组件规范 | 面板/按钮/导航/分段控件的材质配方升级为玻璃配方（本规格 §5） |
| §6 动效规范 | 追加模式氛围过渡（400ms 交叉淡入，本规格 §6） |

**不修订的章节**：§1 目标、§2 设计原则、§5 页面设计（信息架构与布局结构）、§7 可访问性、§8 技术方案（WPF + MVVM）、§9 阶段划分。信息架构（结论前置、渐进披露、双层密度）与已验收的 Phase 1 代码成果（UiShared 逻辑、ViewModel、163 个自测）全部保留。

## 2. 设计决策（已与用户确认）

| # | 决策 | 选择 |
|---|------|------|
| 1 | Radium 与 Apple 克制方向的关系 | **融合演进**：架构与纪律来自 Phase 1，材质与氛围来自 Radium |
| 2 | 场景化世界观 | **驾驶舱/指挥中心**：驾驶舱的语言、不要驾驶舱的图形 |
| 3 | 主题明暗 | **深色为主**（玻璃座舱是主视觉与默认），**浅色保留**为日间模式（同源磨砂配方） |
| 4 | 模式联动范围 | **只换氛围光**：模式色管氛围与导航强调；状态语义色零例外不被覆盖 |
| 5 | 处理强度 | **L2 座舱**：深度来自分层与环境光，不来自发光与网格 |
| 6 | 发光纪律 | **发光只给语义元素**：状态图标与数据进度条；导航、卡片、按钮一律不发光 |
| 7 | 玻璃实现 | **alpha 分层**（不做真 backdrop blur；DWM acrylic 为可选增强） |

## 3. 世界观：驾驶舱

**隐喻映射**（只用于组织界面语言，不画具象图形）：

| 产品概念 | 座舱语言 |
|----------|----------|
| 系统状态结论 | 一眼态势感知（HUD 顶部主状态） |
| GPU 温度 / 内存等指标 | 仪表读数（发光进度条 = 仪表刻度光） |
| 性能模式（常规/竞技/自定义） | 飞行模式（巡航/战备/工程） |
| 游戏检测与优化生效 | 目标锁定 / 系统增压 |
| 状态图标发光 | 仪表状态灯 |

**禁止清单**（防止"过度科技感导致疏离"）：舷窗/螺丝/伪金属边框等具象图形、网格地面、扫描线、无语义的发光边框、持续闪烁。

## 4. 视觉系统

### 4.1 三层材质结构

深度感完全来自分层，不来自发光：

**① 环境层（Ambient）**——窗口根 Grid 最底层，两个径向渐变光域（右上主光域 + 左下次光域）
- 颜色 = 当前模式色，主光域 alpha 13%、次光域 alpha 8%（深色主题）；浅色主题 alpha 16% / 10%
- 减少动态效果模式下为静态渐变

**② 玻璃面板层（Glass Panels）**——导航、卡片、弹层

深色主题配方：

| 层级 | 填充 | 边框 | 投影 |
|------|------|------|------|
| 导航栏 | 白 5% | 白 8% | 无 |
| 普通卡片 | 白 6% | 白 10% | 黑 30%，Blur 16 |
| 结论主卡片 | 白 8% | 白 14% | 黑 40%，Blur 32 |
| 弹层/浮起 | 白 10% | 白 16% | 黑 40%，Blur 24 |

所有面板附加：顶部 1px 内高光（白色 12% 渐变，两端淡出）；圆角沿用上游 Token（RadiusSm 6 / RadiusMd 10 / RadiusLg 14）。

浅色日间配方：填充改为白 55-75%（磨砂感）、边框黑 6-10%、投影黑 8-12%，结构与深色同构。

**③ 发光层（Glow，仅语义）**
- 状态图标圆形底：`DropShadowEffect`，Color = 状态色，BlurRadius 18，ShadowDepth 0，Opacity 0.35
- 进度条填充：`DropShadowEffect`，Color = 状态色，BlurRadius 8，ShadowDepth 0，Opacity 0.45
- 其余元素（导航、卡片、按钮、文字）**一律不发光**

### 4.2 色彩语义宪法

| 颜色角色 | 负责 | 随模式变 |
|----------|------|----------|
| 模式色（青/红/紫） | 环境光域、logo、导航选中、分段选中、主按钮 | 是 |
| 状态语义色（绿/橙/红） | 状态图标、进度条、状态文字 | **否，零例外** |
| 中性色 | 文字、背景、边框 | 否 |

战备模式下进度条与结论图标**仍保持状态语义色**（用户确认：只换氛围光）。

### 4.3 模式氛围色表（ModePalette）

每模式 4 个键；`Ambient*` 为 alpha 配方深浅主题共用，`ModeAccent` 因用于选中导航文字需按主题分深浅两档：

| 模式 | AmbientPrimary | AmbientSecondary | ModeAccentOnDark | ModeAccentOnLight |
|------|---------------|------------------|------------------|-------------------|
| 常规 · 巡航 | `#1FB6D6` | `#2E7DD1` | `#3EC9FF` | `#0E7490` |
| 竞技 · 战备 | `#E5484D` | `#C22E3E` | `#FF6B74` | `#CC2020` |
| 自定义 · 工程 | `#8B5CF6` | `#6D4AC8` | `#A78BFA` | `#7C3AED` |

**约束（由自测强制）**：三模式色互异；`ModeAccentOnDark` 对深色 `Background`（#0F1419）对比度 ≥ 4.5:1；`ModeAccentOnLight` 对浅色 `Background`（#F5F7F9）对比度 ≥ 4.5:1。若某值不达标，按 Task 2 先例同色相微调加深/提亮并同步更新本表。

### 4.4 排版与既有 Token

上游规格的字体五级（Display 24 / Title 18 / Body 14 / Caption 12 / Mono 13）、间距五级（4/8/12/16/24）、圆角三档（6/10/14）、文字对比度标准（正文 ≥4.5:1）全部不变。

## 5. 组件材质变更

仅材质升级，组件结构与交互不变：

| 组件 | 变更 |
|------|------|
| 卡片（CardBorder） | 白 6% 填充 + 白 10% 边框 + 内高光 + 投影（按 §4.1 层级表） |
| 导航项（NavItem） | 选中态：ModeAccent 12% 填充 + ModeAccent 文字 + 1px ModeAccent 33% 内描边；不发光 |
| 分段控件（SegmentItem/Host） | 容器白 5% 填充；选中项白 8% 填充 + 主文字色；容器描边白 10% |
| 主按钮（PrimaryButton） | 填充 ModeAccent（深色主题用 OnDark 档）；不发光 |
| 状态图标底 | 状态色 15% 填充 + §4.1 发光配方 |
| 进度条 | 轨道白 8%；填充状态色渐变 + §4.1 发光配方 |

## 6. 动效

| 场景 | 参数 | 降级 |
|------|------|------|
| 模式氛围切换 | 两层环境光 Opacity 交叉淡入 400ms，CubicEase EaseOut | 瞬时切换，无动画 |
| 其余动效 | 沿用上游 §6（页面淡入 250ms 等，已实现） | 沿用既有策略 |

**性能纪律**：动画只动 Opacity（GPU 合成），不动渐变参数（避免 CPU 重绘）；DropShadowEffect 限于卡片与状态图标，长列表项禁用；v1 不使用 BlurEffect。

## 7. 技术方案

### 7.1 主题架构：双轴四槽

`ThemeManager.Apply(Application, UiTone, AppMode)`，MergedDictionaries 四槽：

```
[ Tokens.xaml ] + [ Styles.xaml ] + [ Colors.{Light|Dark}.xaml ] + [ Mode.{Standard|Competitive|Custom}.xaml ]
```

- `Colors.*.xaml`（现有）：中性色 + 状态语义色；新增一个别名键 `ModeAccentBrush`，浅色字典指向 `ModeAccentOnLightColor`、深色指向 `ModeAccentOnDarkColor`（DynamicResource 运行时解析）
- `Mode.{Standard|Competitive|Custom}.xaml`（新增）：`AmbientPrimaryColor`、`AmbientSecondaryColor`、`ModeAccentOnDarkColor`、`ModeAccentOnLightColor` 及对应画刷
- 新增 `AppMode` 枚举（Standard / Competitive / Custom）于 `ModePalette.cs`

### 7.2 环境层实现

`MainWindow.xaml` 根 Grid 最底层放两对 Ellipse（每对 = 右上主光域 + 左下次光域，RadialGradientBrush 的 GradientStop 绑定 Ambient 色键）。模式切换时：新模式的渐变写入后台那对 Ellipse → 淡入新对、淡出旧对（各 400ms Opacity 动画）→ 完成后交换角色。不产生布局抖动，不做任何位图截图。

### 7.3 UiShared 扩展

新增 `src/UiShared/ModePalette.cs`（纯 C#，与 Palette.cs 同构）：

- `enum AppMode { Standard, Competitive, Custom }`
- `ModePalette.For(AppMode)` 返回 4 个 hex（§4.3 表）
- 自测 3 个：Token 齐全合法 hex；三模式互异且巡航/战备色相差足够（避免混淆）；ModeAccent 两档对比度 ≥4.5:1（对各自主题 Background）

### 7.4 模式切换接线

分段控件选中变化 → `ThemeManager.Apply(app, tone, mode)` 换模式槽 → 氛围 400ms 过渡 → 调用现有 GameMode 预设切换 API（复用 WinForms 策略页同一入口，保持行为一致）→ ViewModel ModeText 更新。

### 7.5 截图探针扩展

`--wpf-shot <dir>` 输出模式×主题矩阵 4 张：`wpf-overview-dark-cruise.png`、`wpf-overview-dark-combat.png`、`wpf-overview-dark-custom.png`、`wpf-overview-light-cruise.png`，存档 `docs/wpf-phase1_5/` 作为视觉回归基线。

### 7.6 可选增强（stretch goal，不做验收要求）

`SetWindowCompositionAttribute` P/Invoke 实现窗口级 acrylic，与桌面壁纸融合。不可用则静默跳过，不影响本规格其余部分。

## 8. 范围

**做（9 项）**：

1. `ModePalette.cs` + 3 个自测
2. 三个模式资源字典
3. ThemeManager 双轴升级
4. 环境光层（MainWindow 根 Grid）
5. 玻璃样式升级（Styles.xaml 按 §5 表）
6. 语义发光（状态图标 + 进度条）
7. 模式分段控件真实切换 + 氛围过渡
8. 概览页玻璃化（材质替换，布局结构不变）
9. 截图探针模式×主题矩阵

**不做**：其余页面迁移（原 Phase 2/3，届时继承新材质）、实时指标接线、详情面板内容、浅色日间模式完整打磨（仅配方定义 + 巡航浅一张验证图）、真毛玻璃。

## 9. 验收标准

- 自测：`TOTAL 169  PASS 166  FAIL 0  SKIP 3`（现有 163 + ModePalette 3 个）
- 截图矩阵 4 张人工核对：玻璃层级可读、发光仅限语义元素、战备模式进度条保持状态色、巡航浅磨砂成立
- 模式切换氛围过渡实机肉眼确认（含减少动态效果模式下的瞬时切换）
- WinForms 构建（`build.cmd`）与自测基线零回归

## 10. 风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| DropShadowEffect 在低端 GPU 掉帧 | 卡顿 | 限量使用 + 动画只动 Opacity；必要时加资源级开关关闭发光（列为应急手段，默认不开放） |
| 浅色磨砂可读性 | 白天看不清 | 浅色玻璃填充 55-75% 高不透明度兜底；验收含巡航浅截图核对 |
| 模式色与状态色混淆（战备红 vs 危险红） | 误读 | 宪法规定状态色零例外；战备氛围红 alpha ≤15% 远低于状态图标浓度 |
| net4 渲染能力不及预期 | 视觉降级 | alpha 分层不依赖任何新 API；最坏情况退回 L1 霜冻（纯扁平深色），架构不变 |

## 11. 参考

- 上游规格：`docs/superpowers/specs/2026-08-09-ui-redesign-design.md`
- Phase 1 验收：`docs/wpf-phase1-verification.md`
- 视觉对比稿（模式联动三模式 / L1-L2-L3 强度）：`.superpowers/brainstorm/810-1786323603/content/`
- Radium 设计理念：用户提供的风格描述（场景化世界观、三维空间化、视觉一致性、功能与情感平衡）
