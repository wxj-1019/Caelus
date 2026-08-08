# 启动弹窗重构:移除联系方式弹窗,改为首次/更新触发更新日志弹窗

## 背景

项目正在从原作者(bdth)的 Pavise 重构为新作者的私有项目。第一步是把启动时弹出的"反馈与交流"联系方式弹窗(`ContactDialog`)移除,改为:

- **首次启动**:自动弹出指导弹窗(向用户介绍产品)
- **每次大版本更新后**:再弹一次
- **其余情况**:不弹,直接进主界面

## 现状

项目里已有两个弹窗:

| 弹窗 | 触发机制 | 当前状态 |
|------|---------|---------|
| `ContactDialog` | 布尔开关 `ContactPromptHidden`(可勾选"不再显示") | 启动时自动弹(`Program.cs:315`),需移除 |
| `ReleaseNotesDialog` | 按版本号 `LastSeenNotesVersion` 判断 `HasUnseen` | 仅关于页手动点击触发,启动时不自动弹 |

关键发现:`ReleaseNotesDialog` 的 `HasUnseen` 机制**完全符合**"首次+大版本更新显示一次"的需求:
- 首次安装时无历史版本记录 → `HasUnseen = true`
- 版本号变化(更新后) → `HasUnseen = true`
- 看过当前版本后 `MarkSeen()` → `HasUnseen = false`

## 方案:复用 ReleaseNotesDialog

启动时检测 `ReleaseNotes.HasUnseen`,有未读版本就自动弹出已有的 `ReleaseNotesDialog`,不再新建独立弹窗。

### 改动清单

#### 1. `src/Program.cs`(核心改动)
- **删除** `:101-117` 的 `--shot-contact` 命令分支(仅供 ContactDialog 截图,已无用)
- **替换** `:315-317` 的 ContactDialog 调用:
  ```csharp
  // 旧
  if (showingPanel && ContactDialog.ShouldShow())
      try { using (var contact = new ContactDialog()) contact.ShowDialog(); }
      catch { }
  // 新
  if (showingPanel && ReleaseNotes.HasUnseen)
      try { using (var dlg = new ReleaseNotesDialog()) dlg.ShowDialog(); }
      catch { }
  ```
- `ReleaseNotesDialog.ShowDialog()` 内部已调用 `ReleaseNotes.MarkSeen()`(`ReleaseNotesDialog.cs:127`),关闭即标记为已读,下次启动不再弹。

#### 2. `src/Ui/ContactDialog.cs`
- **删除整个文件**(222 行)

#### 3. `src/Ui/Pages/PanelForm.SettingsPage.cs`
- **删除** `swContact` 字段声明(`:14`)
- **删除** 弹窗开关卡片的创建代码(`:42-46`,含 `MakeSwitch`、`ResetHidden`/`MarkHidden` 回调、`MakeAutoCard`)
- 删除后需检查 `sy`(y 坐标累加器)是否需要补回,避免下方卡片位置错位

#### 4. `src/Ui/Pages/PanelForm.AboutPage.cs`
- **删除** `:49` 的 `about.contact.hint` 文案行(原作者联系方式引导)
- 其余作者信息行(`:35-37` 的 author/wechat/repo/lic)暂保留,留给后续"身份替换"步骤

#### 5. `src/Platform/Lang.cs`
- **删除** `contact.*` 系列(15 条,`:352-364`):title/sub/wechat/qq/copy/free/dontshow/enter
- **删除** `set.contact`、`set.contact.n`(`:365-366`):设置页开关文案
- **删除** `about.contact.hint`(`:451`):关于页联系方式引导
- 注意:`notes.*` 系列文案保留(ReleaseNotesDialog 在用)

### 不动的部分

- `ReleaseNotesDialog.cs`、`ReleaseNotes.cs`、`notes.*` 文案:全部保留,复用
- 注册表 `ContactPromptHidden`:不再读取,自然作废,无需迁移清理
- `PanelForm.AboutPage.cs` 的"更新日志"按钮(`:51-63`):保留,用户可随时手动重看
- 关于页的作者署名/微信/仓库信息:留给后续重构步骤

### 验证方式

1. `build.cmd` 编译通过
2. `dev.cmd test` 跑 152 项自测,FAIL 0
3. 实际启动验证:
   - 清空注册表 `LastSeenNotesVersion` → 启动应自动弹 ReleaseNotesDialog
   - 关闭弹窗 → 再次启动 → 不再弹
   - 设置页不再有"启动时显示反馈弹窗"开关

## 后续步骤(本次不做)

- 关于页作者信息替换为新人(身份重构)
- 产品名 Pavise、图标、代码注释中的作者署名
- README 三语版本
