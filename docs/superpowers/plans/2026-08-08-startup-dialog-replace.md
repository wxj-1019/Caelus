# 启动弹窗重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 移除启动时的联系方式弹窗(`ContactDialog`),改为首次启动/大版本更新后自动弹出现成的更新日志弹窗(`ReleaseNotesDialog`)。

**Architecture:** `ReleaseNotesDialog` 已存在且已实现按版本号(`LastSeenNotesVersion`)判断 `HasUnseen`、关闭时 `MarkSeen` 的逻辑,只是从未在启动时自动触发。本次只需在 `Program.cs` 把启动弹窗从 `ContactDialog` 换成 `ReleaseNotesDialog`,然后删除 `ContactDialog` 的全部代码、设置页开关、语言文案。任务顺序受自测约束:`TestEveryLangKeyIsDefined` 会扫描源码校验每个 `Lang.T("key")` 引用都有定义,因此必须**先删引用、后删文案**。

**Tech Stack:** C# / .NET Framework 4.x / WinForms,`csc.exe` 直接编译(无 MSBuild/VS),自测为内置 `test()` 宏(`PAVISE_SELFTEST` 编入)。

**关键约束:**
- 此代码库无单元测试框架。每个任务的验证靠 `build.cmd` 编译 + `dev.cmd test` 跑 152 项自测 + 实际启动观察。
- 自测 `TestEveryLangKeyIsDefined`(`tests/SelfTests.LangKeys.cs`)扫描所有 `Lang.T("key")` 调用,要求 key 必须在 `Lang.cs` 有定义。**删除 Lang.cs 文案前必须先删完所有引用**。
- 自测 `TestNoUntranslatedKeysOnScreen`(`tests/SelfTests.LangCoverage.cs`)构建整个 PanelForm 检查无残留 key 文本。删除设置页卡片后不能留任何 `Lang.T("set.contact...")` 调用。

**构建/测试命令(Windows,Git Bash 环境):**
- 编译:`cmd.exe //c "build.cmd"`
- 编译+自测:`cmd.exe //c "dev.cmd test"`(期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`,SKIP 为本机环境原因)
- 启动观察:`powershell.exe -NoProfile -Command "Start-Process -FilePath 'E:\A_Project\Pavise-Game\Pavise.exe' -Verb RunAs"`

---

## File Structure

| 文件 | 操作 | 责任 |
|------|------|------|
| `src/Program.cs` | 修改 | 启动入口:删除 `--shot-contact` 命令分支,把启动弹窗从 ContactDialog 换成 ReleaseNotesDialog |
| `src/Ui/ContactDialog.cs` | 删除 | 整个联系方式弹窗(222 行) |
| `src/Ui/Pages/PanelForm.SettingsPage.cs` | 修改 | 删除"启动时显示反馈弹窗"开关卡片及 `swContact` 字段 |
| `src/Ui/Pages/PanelForm.AboutPage.cs` | 修改 | 删除 `about.contact.hint` 引用行 |
| `src/Platform/Lang.cs` | 修改 | 删除 `contact.*`/`set.contact*`/`about.contact.hint` 共 16 条文案(最后做) |

---

### Task 1: Program.cs — 替换启动弹窗触发逻辑

把启动时的联系方式弹窗换成更新日志弹窗。先做这步,因为 `ReleaseNotesDialog` 和 `ReleaseNotes` 都已存在,改完后 ContactDialog 暂时成为死代码(无人引用),不影响编译。

**Files:**
- Modify: `src/Program.cs:101-117`(`--shot-contact` 命令分支)
- Modify: `src/Program.cs:315-317`(启动弹窗调用)

- [ ] **Step 1: 删除 `--shot-contact` 命令分支**

打开 `src/Program.cs`,删除第 101-117 行整个 `--shot-contact` 分支(从 `if (args.Length >= 2 && args[0] == "--shot-contact")` 到对应的 `return;` 结束的右花括号 `}`)。

删除后,`--geniconpng` 分支(原 `:53`)和 `--screenshot` 分支(原 `:62`)之间应直接衔接,中间不再有 `--shot-contact` 块。

- [ ] **Step 2: 替换启动弹窗调用**

把 `src/Program.cs` 中的 ContactDialog 调用:

```csharp
            if (showingPanel && ContactDialog.ShouldShow())
                try { using (var contact = new ContactDialog()) contact.ShowDialog(); }
                catch { }
```

替换为:

```csharp
            if (showingPanel && ReleaseNotes.HasUnseen)
                try { using (var dlg = new ReleaseNotesDialog()) dlg.ShowDialog(); }
                catch { }
```

- [ ] **Step 3: 编译验证**

此时 `ContactDialog.cs` 仍存在但已无人引用,`ContactDialog` 类不再被构造(但 `SettingsPage` 仍在引用它,见 Task 3,所以暂时还能编译)。

运行:`cmd.exe //c "build.cmd"`
期望输出:`Build OK -> Pavise.exe`

注意:此时先不跑自测,因为设置页还在引用 `ContactDialog.ShouldShow()`。Task 3 删完后一起验证。

- [ ] **Step 4: 暂不提交**

本任务与 Task 2/3 有依赖(全部删完才能编译通过自测),合并到 Task 4 后一起提交。

---

### Task 2: 设置页 — 删除"启动弹窗"开关卡片

删除设置页里控制 ContactDialog 的开关卡片。这步删完后,`ContactDialog` 类的所有运行时引用就只剩 Program.cs 里已删除的(已无用)。

**Files:**
- Modify: `src/Ui/Pages/PanelForm.SettingsPage.cs:14`(字段声明)
- Modify: `src/Ui/Pages/PanelForm.SettingsPage.cs:42-47`(开关卡片)

- [ ] **Step 1: 删除 `swContact` 字段声明**

打开 `src/Ui/Pages/PanelForm.SettingsPage.cs`,第 14 行:

```csharp
        private Toggle swAuto, swAutoHide, swContact;
```

改为(删除 `swContact`):

```csharp
        private Toggle swAuto, swAutoHide;
```

- [ ] **Step 2: 删除开关卡片创建代码**

删除第 42-47 行整块:

```csharp
            swContact = MakeSwitch(ContactDialog.ShouldShow(), delegate
            {
                if (swContact.Checked) ContactDialog.ResetHidden(); else ContactDialog.MarkHidden();
            });
            MakeAutoCard(scroll, 6, sy, ScrollContentW, 76, Lang.T("set.contact"), Lang.T("set.contact.n"), swContact, out cardH);
            sy += cardH + 8;
```

删除后,第 40 行的 `sy += cardH + 8;`(autohide 卡片结尾)之后直接接第 49 行的 `sy += 10;`(维护分区前的间距)。`sy` 是相对累加器,删除一段后后续卡片自然上移,布局不会错位。

- [ ] **Step 3: 编译验证**

运行:`cmd.exe //c "build.cmd"`
期望:`Build OK -> Pavise.exe`

此时 `ContactDialog` 类已无人引用,成为死代码(文件还在)。

- [ ] **Step 4: 暂不提交**(合并到 Task 4)

---

### Task 3: 关于页 — 删除联系方式引导文案行

删除关于页里引用 `about.contact.hint` 的那行(原作者联系方式引导)。

**Files:**
- Modify: `src/Ui/Pages/PanelForm.AboutPage.cs:49`

- [ ] **Step 1: 删除 contact.hint 引用行**

打开 `src/Ui/Pages/PanelForm.AboutPage.cs`,删除第 49 行:

```csharp
            CardLabel(card, Lang.T("about.contact.hint"), 20, 216, infoW - 40, 32, 7.6f, false, Theme.Dim);
```

删除后,第 48 行的 `for` 循环右花括号 `}` 之后直接接第 51 行的 `bool unseenNotes = ReleaseNotes.HasUnseen;`。

- [ ] **Step 2: 编译验证**

运行:`cmd.exe //c "build.cmd"`
期望:`Build OK -> Pavise.exe`

- [ ] **Step 3: 暂不提交**(合并到 Task 4)

---

### Task 4: 删除 ContactDialog 文件 + 跑自测

删除 `ContactDialog.cs` 整个文件(此时已无人引用),然后跑完整自测确认无回归。

**Files:**
- Delete: `src/Ui/ContactDialog.cs`

- [ ] **Step 1: 删除文件**

```bash
rm src/Ui/ContactDialog.cs
```

- [ ] **Step 2: 编译 + 自测**

运行:`cmd.exe //c "dev.cmd test"`
期望输出末尾:`TOTAL 152  PASS 149  FAIL 0  SKIP 3`

如果 FAIL,检查错误信息。常见问题:
- 若提示 `ContactDialog` 未定义 → 说明 Task 1/2/3 有遗漏的引用没删干净,回去补删。
- 若 `TestEveryLangKeyIsDefined` 失败提示某 key 未定义 → 这是 Task 5 还没做导致的(此刻 `set.contact`/`contact.*` 仍在 Lang.cs 里定义着但已无引用,不会触发这个失败;真正会触发的是反过来——引用了已删的 key,此时不该发生)。

- [ ] **Step 3: 提交**

```bash
git add -A
git commit -m "refactor: 移除启动联系方式弹窗，改为首次/更新触发更新日志弹窗

- Program.cs: 启动时用 ReleaseNotes.HasUnseen 触发 ReleaseNotesDialog
- 删除 ContactDialog.cs 及 --shot-contact 截图命令
- 删除设置页"启动弹窗"开关卡片
- 删除关于页 about.contact.hint 引用

ReleaseNotesDialog 已有按版本号判断未读的逻辑，关闭即 MarkSeen，
首次安装和每次更新后各弹一次。"
```

---

### Task 5: 清理 Lang.cs 无用文案

删除 ContactDialog 相关的语言文案。**这步必须放在最后**,因为自测 `TestEveryLangKeyIsDefined` 会校验所有 `Lang.T("key")` 引用都有定义——只有当引用全部删完后(Task 1-4),才能安全删除定义。

**Files:**
- Modify: `src/Platform/Lang.cs:352-366`(contact.* 和 set.contact*)
- Modify: `src/Platform/Lang.cs:451`(about.contact.hint)

- [ ] **Step 1: 删除 contact.* 和 set.contact* 文案块**

打开 `src/Platform/Lang.cs`,删除第 352-366 行整块(共 15 行,从 `contact.title` 到 `set.contact.n`):

```csharp
            { "contact.title", new[]{ "反馈与交流" } },
            { "contact.sub", new[]{ "遇到 Bug、想提功能需求，或者只是想聊聊——都可以直接找到我。" } },
            { "contact.wechat", new[]{ "作者微信 · 功能需求 / BUG 反馈" } },
            { "contact.wechat.note", new[]{ "备注 Pavise" } },
            { "contact.qq", new[]{ "QQ 交流群" } },
            { "contact.qq.note", new[]{ "使用问题 / 版本通知" } },
            { "contact.copy", new[]{ "复制群号" } },
            { "contact.copied", new[]{ "已复制" } },
            { "contact.copyfail", new[]{ "复制失败" } },
            { "contact.free", new[]{ "Pavise 完全免费，禁止倒卖" } },
            { "contact.free.n", new[]{ "最新版和源码更新永远在微信群、QQ 群免费提供。花钱买到的是被骗了——去要求退款。" } },
            { "contact.dontshow", new[]{ "不再显示（设置页可重新开启）" } },
            { "contact.enter", new[]{ "进入 Pavise" } },
            { "set.contact", new[]{ "启动时显示反馈弹窗" } },
            { "set.contact.n", new[]{ "关闭后不再弹出反馈与交流窗口；作者微信和 QQ 群号在「关于」页始终可见" } },
```

删除后,第 351 行 `nav.audit` 之后直接接原第 367 行 `nav.hardware`。

- [ ] **Step 2: 删除 about.contact.hint 文案**

删除原第 451 行(因前面删了 15 行,现在行号约为 436):

```csharp
            { "about.contact.hint", new[]{ "反馈 Bug、提交新功能建议或交流使用问题，可加作者微信；也欢迎在 GitHub 提 Issue" } },
```

- [ ] **Step 3: 编译 + 自测**

运行:`cmd.exe //c "dev.cmd test"`
期望:`TOTAL 152  PASS 149  FAIL 0  SKIP 3`

重点确认 `TestEveryLangKeyIsDefined` 仍通过(它在自测序列里)——这验证没有任何残留的 `Lang.T("contact...")` 引用。

- [ ] **Step 4: 提交**

```bash
git add src/Platform/Lang.cs
git commit -m "chore: 删除 ContactDialog 相关的 16 条无用语言文案"
```

---

### Task 6: 实际启动验证

确认启动行为符合预期:首次/更新后弹 ReleaseNotesDialog,看过一次不再弹。

- [ ] **Step 1: 模拟首次启动(强制弹窗)**

先停掉可能运行中的实例,然后清空 `LastSeenNotesVersion` 让 `HasUnseen = true`:

```bash
powershell.exe -NoProfile -Command "try { [System.Threading.EventWaitHandle]::OpenExisting('Global\Pavise_Exit').Set() } catch {}; Start-Sleep 3; Get-Process Pavise -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2; Remove-ItemProperty -Path 'HKCU:\Software\Pavise' -Name 'LastSeenNotesVersion' -ErrorAction SilentlyContinue; Write-Output 'cleared'"
```

- [ ] **Step 2: 启动并确认弹窗**

```bash
powershell.exe -NoProfile -Command "Start-Process -FilePath 'E:\A_Project\Pavise-Game\Pavise.exe' -Verb RunAs; Start-Sleep 5"
```

期望:弹出的是**更新日志弹窗**(标题"版本说明"/`notes.title`,内容是版本更新列表),而不是联系方式弹窗。关闭弹窗后进入主界面。

- [ ] **Step 3: 确认不再弹**

关闭弹窗后(ReleaseNotesDialog 内部已调 `MarkSeen`),再次启动:

```bash
powershell.exe -NoProfile -Command "try { [System.Threading.EventWaitHandle]::OpenExisting('Global\Pavise_Exit').Set() } catch {}; Start-Sleep 3; Start-Process -FilePath 'E:\A_Project\Pavise-Game\Pavise.exe' -Verb RunAs; Start-Sleep 5"
```

期望:**不再弹窗**,直接进主界面。

- [ ] **Step 4: 确认设置页无残留开关**

打开主界面 → 设置页,确认原来的"启动时显示反馈弹窗"开关卡片已消失,布局正常(自动启动、自动隐藏开关之后直接是维护分区)。

- [ ] **Step 5: 清理并结束实例**

```bash
powershell.exe -NoProfile -Command "try { [System.Threading.EventWaitHandle]::OpenExisting('Global\Pavise_Exit').Set() } catch {}; Start-Sleep 3; Get-Process Pavise -ErrorAction SilentlyContinue | Stop-Process -Force"
```
