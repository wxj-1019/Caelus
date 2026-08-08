# 图标视觉重设计：闪电 → 弯月+星

## 目标

把程序图标从原作者的"深色方形 + 闪电⚡"设计,改为 Caelus 品牌的"深色方形 + 弯月+星"设计。呼应产品名 Caelus(拉丁语"天空")和"全神贯注时把资源让给当前重任"的定位。

## 背景

图标是 100% 代码生成的(`src/Ui/IconArt.cs` 的 `Render` 方法),不是替换图片文件。当前架构:
- 深色圆角方形底(Squircle,`Ink = #181A20`,近黑)+ 1.8px 描边
- 中央闪电符号(`BoltPts()` 硬编码 6 个顶点的多边形)
- 闪电颜色随性能模式变:`Theme.ModeColor(mode)` —— 竞技=红、常规=琥珀、自定义=蓝;禁用时与灰 `#696E79` 混合 58%
- 输出多尺寸 ICO(16/20/24/32/40/48/64/128/256px),由 `IcoWriter.Build` 拼装

## 设计

### 视觉元素

```
┌─────────────────────┐
│      ╭─────╮        │   ← 弯月（主体图形，模式色填充）
│     ╱       ╲       │
│    │  ✦            │   ← 四角星（点缀，同色或稍亮）
│    │               │
│     ╲             ╱ │
│      ╰───────────╯  │
│                     │   ← 深色圆角方形底（#181A20）
└─────────────────────┘
```

- **底座**:保留深色圆角方形(`Ink = #181A20`)+ 描边。与现有 UI 暗色主题无缝衔接,改动最小。
- **弯月**:由两个错位的圆弧相减得到——外圆减去偏移的内圆,形成月牙形。填充模式色(`Theme.ModeColor`)。尺寸约占画布 45%,居中偏上。
- **四角星**:弯月右下方一颗小四角星(4 个尖点的星形),同色或稍亮(`ModeColor2`)。尺寸约占画布 15%。
- **禁用态**:弯月和星都与灰 `#696E79` 混合 58%(沿用原 `Col.Lerp` 逻辑)。

### 实现改动

只改 `src/Ui/IconArt.cs`,不动其他文件:

1. **替换 `BoltPts()` 为 `CrescentPath()`**:用 `GraphicsPath` 画弯月——外圆弧(`AddArc`)顺时针 + 内圆弧(`AddArc`)逆时针,`CloseFigure` 形成月牙闭合区域。坐标基于 100×100 画布:
   - 外圆:圆心 (42, 48),半径 26
   - 内圆:圆心 (50, 44),半径 22(向右上偏移,形成左下开口的月牙)

2. **新增 `StarPath(float cx, float cy, float r)`**:画四角星。4 个外尖点 + 4 个内凹点交替,用 `AddLines` + `CloseFigure`。位置约 (66, 62),外半径 8,内半径 3。

3. **修改 `Render` 方法**:把 `FillPolygon(b, BoltPts())` 替换为:
   ```csharp
   using (var b = new SolidBrush(bolt)) g.FillPath(b, CrescentPath());
   using (var b = new SolidBrush(enabled ? Col.Lerp(bolt, Color.White, 0.15f) : bolt)) g.FillPath(b, StarPath(66, 62, 8));
   ```
   星用稍亮的色(与主色 `Lerp` 15% 向白),增加层次。

4. **保留**:底座 Squircle、`Ink`/`Rim` 常量、`MakeIcon`/`MakeMultiIcon`/`IcoWriter` 全部不动。`--geniconpng`/`--genicon` 命令自动用新图形。

### 模式色对照(沿用 Theme.ModeColor,不改)

| 模式 | 弯月/星颜色 | 语义 |
|------|-----------|------|
| 常规 Standard | 琥珀 #EFBE42 | 温和 |
| 竞技 Competitive | 红 #FF3D52 | 激进 |
| 自定义 Custom | 蓝 #30B4FF | 灵活 |
| 禁用 | 灰(混合 58%) | 待命 |

### 不改的部分

- `docs/icon.png`(README 横幅):构建后用 `--geniconpng` 重新生成替换
- 图标文件名 `Caelus.ico`:已是 Caelus,不改
- `IconArt` 类名/方法签名:不改(太多调用方)
- Theme 配色:不改

## 验证方式

1. `build.cmd` 编译通过
2. `dev.cmd test` 152 项自测 PASS 149 / FAIL 0 / SKIP 3(自测不校验图形内容,只校验 ICO 格式有效性)
3. `--geniconpng` 生成新图标 PNG,目视确认:深色方形底 + 模式色弯月 + 小星
4. 三个模式各渲染一张(competitive/standard/custom)确认配色正确
5. 重新生成 `Caelus.ico` 和 `docs/icon.png`
