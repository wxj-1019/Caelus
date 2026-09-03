# 概览页「夜空烟花」视觉强化 + 改样式工作流强化 · 设计文档

日期：2026-08-31 · 状态：已批准
范围：概览页标杆翻新（骨架不动只换皮）+ 改样式工作流三强化；其余 12 页本次不动，后续按同款令牌跟进。

## 背景与目标

用户痛点：WPF 样式修改困难（改 XAML → 编译 → 启动 → 截图的分钟级反馈循环）。项目已有
design-sandbox（HTML/CSS 镜像设计系统，浏览器秒级迭代）与 --wpf-shot 截图探针，本次把
概览页做成"夜空烟花"强化方向的标杆，并把工作流的三处短板补齐。

已确认的产品决策（头脑风暴结论）：
- 视觉方向：**A · 夜空烟花**——保留棉花糖性格（糖果色 + 大圆角 + 光晕），个性强度拉满
- 信息骨架：**不动**——概览页八分区（标题/徽章/Hero/模式/场景卡/规则/底部）原地保留
- 范围：概览页先开路做标杆
- 工作流三强化全做：沙盒↔XAML 自动校验 / 一键全页截图验收 / 沙盒组件库补齐

## 视觉设计

### Hero 卡（ZoneHero）

- 标题：40px → 56px 级超大字号；**渐变文字**（奶油白 `#F7F1EA` → 薰衣草紫 `#C8B0FA`），
  XAML 实现为 `Foreground` 赋 `LinearGradientBrush`（.NET Framework 4 兼容）。
- 光晕：卡内右上紫色径向光晕 + 左下蜜桃橙补光；**模式切换时光晕色相跟换**
  （巡航=紫主导、竞技=橙主导、自定义=金主导）。
- 状态行：加绿色圆点（`--success`）标识"后台安静"。

### 指标行（三张指标卡）

各自上色：紫（活跃场景）、绿（后台被压）、蜜桃橙（GPU 温度）；高饱和数字 +
同色系柔底 + 柔描边。

### 新增令牌（明暗双轴 × 三模式槽各给一份）

| 令牌 | 用途 |
|---|---|
| `HeroTitleBrush` | Hero 渐变文字画刷 |
| `HeroGlowA` / `HeroGlowB` | Hero 双光晕色（按模式换色相） |
| `MetricAccent1/2/3` | 指标卡强调色（紫/绿/橙） |

浅色版：光晕压透明度、渐变落在暖可可底上，避免过曝。

## 实施路径（沙盒先行）

1. `design-sandbox/overview.html` + `tokens.css` 定稿（明暗 × 三模式，浏览器刷新即看）。
2. 翻译回 `wpf/Themes/Tokens.xaml`、`Colors.Dark/Light.xaml`、`Mode.Standard/Competitive/Custom.xaml`
   与 `wpf/Views/OverviewView.xaml`。
3. `build.cmd` → `--wpf-shot` 出图 → 对比目检。
4. `dev.cmd test` 全绿收尾。

## 工作流三强化

### a) 沙盒↔XAML 自动校验（自测守护）

新增自测：解析 `design-sandbox/tokens.css` 与 `wpf/Themes/Tokens.xaml`、`Colors.Dark/Light.xaml`，
对关键令牌集（圆角、字号、主色板、模式强调色）做**名称 + 值**比对，漂移即 FAIL，
进 `dev.cmd test` 门禁。令牌映射遵循 README 既有约定（PascalCase ↔ kebab-case、ARGB↔RGBA）。

### b) 一键全页截图验收

现状：`--wpf-shot <dir>` 出概览×4 主题组合 + 其余 12 页仅深色常规。扩展为
**13 页 × 明暗 × 三模式全矩阵**（每页 6 图），配 `scripts/shoot-all.ps1` 一键调用，
输出到 `docs/shots/`。探针保留既有防裁剪处理（显式重置尺寸/最大化态）。

### c) 沙盒组件库补齐

`design-sandbox/index.html` 覆盖全部核心组件 × 状态：按钮（主/次/危险）、开关、
卡片（玻璃/组）、徽章、分段选择器、对话框（含危险确认）、禁用/悬停态、进度环。
组件名与 XAML Style 键一一对应。

## 风险与对策

| 风险 | 对策 |
|---|---|
| 渐变文字兼容性 | LinearGradientBrush 于 Foreground 是标准玩法，.NET 4 无虞 |
| 浅色模式光晕过曝 | 令牌按主题分槽压透明度 |
| 双轴×三模式槽遗漏 | ThemeContract 自测扩展进新令牌，键集缺失即 FAIL |
| 沙盒与 XAML 漂移 | 强化 a 的对比自测兜底 |

## 验收标准

- 自测全绿（253 + 新增对比自测与 ThemeContract 扩展项）
- `--wpf-shot` 全矩阵出图，截图入 `docs/shots/` 归档
- 概览页新版截图替换三份 README 的旧图
- 沙盒与 XAML 双向同步、对比自测长期守门

## 明确不做（本次范围外）

- 其余 12 页的逐页翻新（后续跟进令牌即可）
- 概览页信息骨架/阅读顺序调整（用户已选"骨架不动"）
- 动效系统改动（Motion 层不动）
