# Caelus WPF Apple 设计理念与动效增强验收

日期：2026-08-11

## 设计定位

本次没有复制 macOS 外观，而是将 Apple 的 Clarity、Deference、Depth、Continuity 和 Purposeful Motion 融入现有 Windows 专业工具界面。Windows 窗口按钮、工作区信息架构、紧凑设置行和三种性能模式业务语义保持不变。

明确排除：macOS 交通灯、SF Symbols、全窗 Acrylic/Mica 伪造、BlurEffect、全屏 Aurora、视差、大幅弹簧、布局属性动画和长列表逐项入场。

## 共享组件增强

- 按钮：鼠标和键盘按下时做 90ms、`Scale=0.98` 的轻量触感反馈。
- 开关：thumb 使用独立 `TranslateTransform` 在 150ms 内滑动，不再通过 Margin/Alignment 瞬间跳变。
- 分段控件：单一选中板按真实段宽在 180ms 内移动，支持左右键、Home、End 和默认 TwoWay 回写。
- 主窗口性能模式复用同一个 SegmentedControl，外壳与设置页行为统一。
- 滚动条：深浅主题均使用 10px 窄轨道，hover 和 dragging 状态与主题一致。
- 页面：导航后整体做 180ms 淡入和最多 4px X 位移，不保留两份页面，不做逐行 stagger。
- 模式：当前页只做 220ms Accent 交叉淡化，不缩放整个工具面板。
- 状态：游戏库、白名单和体检三态只对新显示的根层播放一次 Reveal。
- 对话框：添加游戏、运行程序、Defender 和 LOL 维护窗口内容统一一次淡入。

## 动效生命周期

- 一次性动画使用 `SnapshotAndReplace`，完成后清除 animation clock 并写回最终基值。
- `Motion` 使用 TransformGroup 组合 Translate/Scale，不覆盖元素已有 RenderTransform。
- Reduced Motion 或高对比度下禁用位移、缩放、旋转和永久脉冲，只保留 90ms 必要淡化。
- 系统动画和高对比度设置变化会在运行时触发策略更新，无需重启。
- 高对比度模式用 Windows 系统实体色覆盖半透明表面、文字、边框和选中态。
- 概览 READY 从永久 Pulse 改为一次性 Emphasize。
- CaelusCore 仅在概览可见、窗口激活、非最小化且允许动画时以 8fps 旋转；离页、失活、最小化或 Unloaded 时清除旋转时钟。
- 主题 tone/mode 字典按 URI 缓存，切换时复用已解析资源，避免反复创建 XAML 对象。

## 构建与自测

- `build.cmd`：通过，输出 `Caelus.exe`。
- `build-wpf.cmd`：通过，输出 `wpf/bin/Release/CaelusWpf.exe`。
- `dev.cmd test`：`TOTAL 178 / PASS 178 / FAIL 0 / SKIP 0`。
- 动效 token 自测已更新，覆盖 Button/Toggle/Segment/Page/Mode/Reduced Motion/Scale 策略。
- `git diff --check`：通过。

构建仍保留工作树既有提示：脚本开头的 `'2' 不是内部或外部命令`，以及 MSIL 与 x86 引用架构警告。

## 视觉矩阵

`CaelusWpf.exe --wpf-shot docs/wpf-apple-motion` 成功生成 14 张截图：

- 概览：深色常规、深色竞技、深色自定义、浅色常规。
- 深色常规：游戏库、优化策略、显卡、反作弊、系统环境、白名单、体检、日志、设置、关于。

截图模式会设置 `Motion.Enabled=false`，所有开关、分段指示器、页面和 Core 直接渲染最终静态状态。逐图检查未发现文字重叠、控件漂移、错误 thumb 位置、亮色系统滚动条或模式指示器错位。

## 性能

正常启动并稳定 5 秒后采样 10 秒：

- CPU：`0.02%`
- 工作集：`149.2 MB -> 149.2 MB`
- 工作集增量：`0.0 MB`

结果低于 `1%` 硬门槛，也优于现代工具风重设计前一次记录的 `0.09%`。

## 压力验收

外部 UI Automation 完成 110 次导航和 30 次模式切换，交互失败 `0`。最终泄漏判断使用应用内探针，排除 AutomationPeer 缓存干扰：

- 每轮：110 次导航 + 30 次模式切换。
- 托管堆：`7.0 MB -> 6.9 MB -> 6.9 MB`。
- 托管堆第二轮增量：`0.0 MB`。
- Private Bytes：首次 WPF 动效/渲染设施暖机 `+31.0 MB`；第二轮 `+1.1 MB`，明显收敛。

结论：没有页面实例或 animation clock 的线性托管泄漏；Private Bytes 增长属于首次原生设施高水位，暖机后收敛。

## 验收产物

- `docs/wpf-apple-motion/`：14 张视觉矩阵、`performance.txt`、`internal-stress-two-rounds.txt`。
- `scripts/wpf-motion-stress.ps1`：可复用的管理员 UI Automation 压力脚本。
- `--wpf-motion-stress <report>`：应用内托管堆与 Private Bytes 双轮压力探针。
