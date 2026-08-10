# WPF Phase 1.5 Radium 座舱化验收记录

**日期**: 2026-08-10  
**分支**: `feature/ui-redesign-phase1`  
**范围**: Phase 1.5 — Radium 驾驶舱材质层演进（三层材质 + 模式氛围联动 + 玻璃配方）  
**规格**: `docs/superpowers/specs/2026-08-10-radium-cockpit-design.md`  
**计划**: `docs/superpowers/plans/2026-08-10-radium-cockpit-phase1_5.md`

---

## 构建结果

| 构建 | 结果 |
|------|------|
| WinForms（`build.cmd`） | ✅ `Build OK -> Caelus.exe` |
| WPF（`build-wpf.cmd`） | ✅ `WPF Build OK -> wpf/bin/Release/CaelusWpf.exe` |

## 自测结果

```
TOTAL 169  PASS 166  FAIL 0  SKIP 3
```

- Phase 1 基线：163 PASS
- Phase 1.5 新增：3 个 ModePalette 测试（Token 齐全、互异、对比度 AA）
- **FAIL 0**

## 截图矩阵验收（4 张）

`--wpf-shot` 探针输出模式×主题矩阵，经图像分析逐张核对：

| 截图 | 环境光域 | 玻璃面板 | 状态图标/进度条 | 导航选中 | 结论 |
|------|----------|----------|-----------------|----------|------|
| `dark-cruise` | 青蓝（巡航）✓ | 半透明+投影 ✓ | 绿色发光 ✓ | 青色软底 ✓ | 通过 |
| `dark-combat` | 红色（战备）✓ | 半透明+投影 ✓ | **保持绿色**（零例外）✓ | 红色软底 ✓ | 通过 |
| `dark-custom` | 紫色（工程）✓ | 半透明+投影 ✓ | 绿色发光 ✓ | 紫色软底 ✓ | 通过 |
| `light-cruise` | 微弱青蓝 ✓ | 浅色磨砂玻璃 ✓ | 可读 ✓ | 青色 ✓ | 通过 |

### 色彩语义宪法验证（关键）

战备模式（dark-combat）是关键验收点：环境光域变红，但**状态图标与进度条保持绿色**。这验证了规格 §4.2 的零例外原则——模式色管氛围，状态色管语义，两者不混淆。图像分析确认：「Ambient background glow: red/crimson ✓；Status icon circle and progress bars: green ✓」。

## 功能验证

| 验证项 | 结论 |
|--------|------|
| 模式氛围切换（巡航↔战备↔工程） | ✅ 三模式色域正确切换，400ms 交叉淡入 |
| 模式持久化 | ✅ Settings "PerformancePreset" 键，重开保持上次模式 |
| 默认深色主题 | ✅ 启动默认 UiTone.Dark |
| 减少动态效果 | ✅ Motion.Reduced 检测，切换瞬时完成 |
| 三层材质结构 | ✅ 环境光层 + 玻璃面板层 + 语义发光层 |
| 发光仅限语义元素 | ✅ 状态图标 + 进度条有发光；导航/卡片/按钮不发光 |
| WCAG 对比度 | ✅ 3 个 ModePalette 浘认对比度自测全通过（含 #CC2020 修正） |

## 7 个提交（Phase 1.5）

| # | Commit | 描述 |
|---|--------|------|
| 1 | `b4ea957` | feat: ModePalette 模式氛围色板（巡航/战备/工程，规格 §4.3） |
| 2 | `c209bdb` | feat: 模式资源字典与双轴 ThemeManager（四槽主题架构） |
| 3 | `a860907` | feat: AmbientLayer 环境光控件（两对光域交替交叉淡入） |
| 4 | `4ea42f2` | feat: 玻璃样式升级（面板 alpha 分层 + 内高光 + 投影，ModeAccent 导航选中） |
| 5 | `bf85776` | feat: 概览页语义发光（状态图标+进度条）与玻璃主卡片 |
| 6 | `a285298` | feat: 模式分段控件真实切换——氛围过渡 + Settings 持久化 + VM 刷新 |
| 7 | （本提交） | docs: Phase 1.5 验收记录与模式×主题截图矩阵 |

## 实现中发现并修复的缺陷

| 缺陷 | 发现方式 | 修复 |
|------|----------|------|
| Competitive ModeAccentOnLight #DC2626 对比度 4.497 < 4.5 | Task 1 对比度自测 | 同色相加深至 #CC2020（5.15:1），同步更新规格 §4.3 |
| MainWindow CS0051 可访问性不一致 | Task 6 WPF 构建 | MainWindow(IOverviewSource) 与 ApplyPersistedMode(AppMode) 改为 internal |

## 遗留项（Phase 2-4）

- 浅色日间模式导航选中态可读性（OnDark 色 alpha 软底在浅色下偏亮）——完整打磨不在本阶段范围
- 其余页面迁移（游戏库、优化策略、反作弊、日志、设置等）——届时自动继承座舱材质语言
- DWM acrylic 真毛玻璃（可选增强，stretch goal）
- 实时指标接线与详情面板内容
