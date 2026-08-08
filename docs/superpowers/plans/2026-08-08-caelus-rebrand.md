# Caelus 品牌重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把项目从原作者 bdth 的 Pavise 彻底重构为 zenjiro 的 Caelus,覆盖产品名、作者、命名空间、数据文件名、注册表根、图标、LICENSE、README、文件头注释。

**Architecture:** 纯机械替换,无逻辑变更。按风险从低到高分 8 个阶段,每阶段独立可验证(编译 + selftest)。身份常量(Program.cs:22-30)是单点真理源,改它连带更新关于页。全新项目不做数据迁移,注册表根/文件名/回滚标志键直接改。

**Tech Stack:** C# / .NET Framework 4.x / WinForms,`csc.exe` 直接编译,自测为内置 `test()` 宏。

**构建/测试命令(Windows,Git Bash):**
- 编译:`cmd.exe //c "build.cmd"`
- 编译+自测:`cmd.exe //c "dev.cmd test"`(期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`)
- 残留检查:`grep -ri pavise src/ tests/ tools/`(逐步归零)

**关键约束:**
- 自测 `TestEveryLangKeyIsDefined`(`tests/SelfTests.LangKeys.cs`)校验所有 `Lang.T("key")` 引用有定义——改 Lang.cs 时改值不改键名。
- 自测 `TestNoUntranslatedKeysOnScreen` 构建 PanelForm 检查无残留 key 文本——删除/修改 UI 引用要同步。
- 命名空间改名后 `tools/PerfLab/build.ps1:36` 的 `-main:PaviseApp.PerfEngineProgram` 必须同步,否则 PerfLab 编译失败。
- 回滚标志键有两类:**注册表键名字符串**(如 `"DevPowerByPavise"`,写入注册表,必须改)和 **C# 属性名**(如 `EnabledByPavise`,代码符号,为一致性一起改)。

---

## 替换规则总表(贯穿所有阶段)

| 旧 | 新 | 说明 |
|----|-----|------|
| `PAVISE` | `CAELUS` | 显示名(全大写) |
| `PaviseApp` | `CaelusApp` | 命名空间 |
| `PavisePerfLab` | `CaelusPerfLab` | PerfLab 命名空间 |
| `Pavise.exe` | `Caelus.exe` | 输出名 |
| `Pavise.ico` | `Caelus.ico` | 图标文件名 |
| `Pavise.` (文件前缀) | `Caelus.` | 数据文件(log/profiles/state 等) |
| `Software\Pavise` | `Software\Caelus` | 注册表根 |
| `Global\Pavise_` | `Global\Caelus_` | Mutex/事件名 |
| `ByPavise` | `ByCaelus` | 回滚标志键后缀 |
| `bdth` | `zenjiro` | 作者 |
| `2074055628@qq.com` | `18967498922@163.com` | 邮箱 |
| `dulaiduwang003/Pavise-Game` | `zenjiro/Caelus` | 仓库名(占位) |

---

### Task 1: 身份常量 + AssemblyInfo + 版本号(单点真理源)

改 Program.cs 的 7 个身份常量和 AssemblyInfo。这一步完成后关于页作者/邮箱/仓库自动更新。

**Files:**
- Modify: `src/Program.cs:22-30`
- Modify: `src/AssemblyInfo.cs:7-16`

- [ ] **Step 1: 改 Program.cs 身份常量**

把 `src/Program.cs` 中的:
```csharp
        public const string DisplayName = "PAVISE";
        public const string Version = "1.6.7";
        public const string Author = "bdth";
        public const string AuthorEmail = "2074055628@qq.com";
        public const string WeChat = "Ssssssstyle";
        public const string RepoName = "dulaiduwang003/Pavise-Game";
        public const string RepoUrl = "https://github.com/" + RepoName;
        public const string ReleasesUrl = RepoUrl + "/releases";
        public static string VersionTag { get { return "v" + Version; } }
```
改为:
```csharp
        public const string DisplayName = "CAELUS";
        public const string Version = "1.7.0";
        public const string Author = "zenjiro";
        public const string AuthorEmail = "18967498922@163.com";
        public const string WeChat = "";
        public const string RepoName = "zenjiro/Caelus";
        public const string RepoUrl = "https://github.com/" + RepoName;
        public const string ReleasesUrl = RepoUrl + "/releases";
        public static string VersionTag { get { return "v" + Version; } }
```
注意:`WeChat` 设为空字符串而非删除(关于页 Task 会处理空值显示)。

- [ ] **Step 2: 改 AssemblyInfo 属性**

把 `src/AssemblyInfo.cs:7-16` 改为:
```csharp
[assembly: AssemblyTitle("Caelus")]
[assembly: AssemblyDescription("Windows resource scheduling utility for games and development")]
[assembly: AssemblyCompany("zenjiro")]
[assembly: AssemblyProduct("Caelus")]
[assembly: AssemblyCopyright("Copyright 2026 zenjiro")]
[assembly: AssemblyTrademark("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]
[assembly: AssemblyInformationalVersion("1.7.0")]
```

- [ ] **Step 3: 编译验证**

`cmd.exe //c "build.cmd"` → 期望 `Build OK -> Pavise.exe`(exe 名下一阶段改)

- [ ] **Step 4: 提交**

```bash
git add src/Program.cs src/AssemblyInfo.cs
git commit -m "refactor: 身份常量改为 zenjiro/Caelus，版本升至 1.7.0"
```

---

### Task 2: 关于页移除微信行

关于页原来四行(作者/微信/仓库/许可),微信行删除变三行。

**Files:**
- Modify: `src/Ui/Pages/PanelForm.AboutPage.cs:35-48`

- [ ] **Step 1: 改关于页四行为三行**

把 `src/Ui/Pages/PanelForm.AboutPage.cs` 中的:
```csharp
            string[] rowKeys = { "about.author", "about.wechat", "about.repo", "about.lic" };
            string[] rowVals = { App.Author + " · " + App.AuthorEmail, App.WeChat,
                App.RepoUrl.Replace("https://", ""), Lang.T("about.lic.value") };
            for (int i = 0; i < 4; i++)
```
改为:
```csharp
            string[] rowKeys = { "about.author", "about.repo", "about.lic" };
            string[] rowVals = { App.Author + " · " + App.AuthorEmail,
                App.RepoUrl.Replace("https://", ""), Lang.T("about.lic.value") };
            for (int i = 0; i < 3; i++)
```

- [ ] **Step 2: 编译验证**

`cmd.exe //c "build.cmd"` → 期望 `Build OK`

- [ ] **Step 3: 提交**

```bash
git add src/Ui/Pages/PanelForm.AboutPage.cs
git commit -m "refactor: 关于页移除微信行（zenjiro 暂不提供微信）"
```

---

### Task 3: 产品名字符串(展示层)

散落在 UI 各处的硬编码 "Pavise"/"PAVISE" 改为 "Caelus"/"CAELUS"。

**Files:**
- Modify: `src/Ui/PanelForm.Widgets.cs:19`
- Modify: `src/Ui/TrayMenu.cs:273`
- Modify: `src/Ui/Pages/PanelForm.EnvironmentPage.cs`(约 10 处 MessageBox 标题)
- Modify: `src/Platform/Lang.cs`(about.lic.value + about.desc + 托盘文案)
- Modify: `src/Core/Tamer.cs:277`(日志标签)

- [ ] **Step 1: 主窗口标题**

`src/Ui/PanelForm.Widgets.cs` 中 `"PAVISE  //  CONTROL"` 改为 `"CAELUSELUS  //  CONTROL"`——不,应为 `"CAELUS  //  CONTROL"`。

- [ ] **Step 2: TrayMenu MessageBox 标题**

`src/Ui/TrayMenu.cs:273` 中 MessageBox 标题 `"Pavise"` 改为 `"Caelus"`。

- [ ] **Step 3: EnvironmentPage MessageBox 标题**

`src/Ui/Pages/PanelForm.EnvironmentPage.cs` 中所有 `MessageBox.Show(..., "Pavise", ...)` 的 `"Pavise"` 改为 `"Caelus"`(约 10 处,用 Edit replace_all)。

- [ ] **Step 4: Lang.cs 文案**

`src/Platform/Lang.cs` 中:
- `about.lic.value` 的值 `"Pavise 许可协议 · 禁止销售"` → `"Caelus 许可协议 · 禁止销售"`
- `about.desc` 的值改写为兼顾开发场景(原文是"把系统资源让给游戏的 Windows 工具",改为"把系统资源让给当前重任的 Windows 工具——打游戏、写代码、编译,都需要专注算力")
- 扫描 `tray.*`、`bal.*` 文案中含 "Pavise" 的,改为 "Caelus"(三语都要改:中文数组第 0 项必改,英/日若有也改)

- [ ] **Step 5: Tamer 日志标签**

`src/Core/Tamer.cs:277` 中 `ReleaseAll("Pavise 退出")` 改为 `ReleaseAll("Caelus 退出")`。

- [ ] **Step 6: 编译 + 自测**

`cmd.exe //c "dev.cmd test"` → 期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`

- [ ] **Step 7: 提交**

```bash
git add -A
git commit -m "refactor: UI 展示层产品名 Pavise → Caelus"
```

---

### Task 4: 命名空间全局替换(PaviseApp → CaelusApp)

157 个文件的 `namespace PaviseApp` → `namespace CaelusApp`,以及 PerfLab 命名空间和 build.ps1 入口。

**Files:**
- 全局:`src/`、`tests/`、`tools/` 下所有 .cs(157 处声明)
- Modify: `tools/PerfLab/build.ps1:36`

- [ ] **Step 1: 批量替换命名空间声明和引用**

运行脚本(用 sed 全局替换,覆盖 src/ tests/ tools/):
```bash
find src tests tools -name "*.cs" -exec sed -i 's/PaviseApp/CaelusApp/g' {} +
find src tests tools -name "*.cs" -exec sed -i 's/PavisePerfLab/CaelusPerfLab/g' {} +
```
这同时处理 `namespace PaviseApp`、`namespace PavisePerfLab`、以及跨文件引用 `PaviseApp.`(仅 1 处)。

- [ ] **Step 2: 同步 PerfLab build.ps1 入口**

`tools/PerfLab/build.ps1:36` 中 `-main:PaviseApp.PerfEngineProgram` 改为 `-main:CaelusApp.PerfEngineProgram`:
```bash
sed -i 's/PaviseApp.PerfEngineProgram/CaelusApp.PerfEngineProgram/g' tools/PerfLab/build.ps1
```

- [ ] **Step 3: 编译验证**

`cmd.exe //c "build.cmd"` → 期望 `Build OK`。若报类型未找到,说明有遗漏的引用,sed 可能漏了非常规写法,用 `grep -rn "PaviseApp" src/ tests/ tools/` 排查。

- [ ] **Step 4: 自测**

`cmd.exe //c "dev.cmd test"` → 期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "refactor: 命名空间 PaviseApp → CaelusApp（全局 157 文件）"
```

---

### Task 5: 持久化标识符(注册表根 + 数据文件名 + 回滚标志键 + Mutex)

全新项目直接改,不写迁移逻辑。

**Files:**
- Modify: `src/Platform/Settings.cs:12`
- Modify: `src/Core/GameMode.cs:164-166`
- Modify: `src/Core/Detection/GameProfiles.cs:51`
- Modify: `src/Core/Suppression/SuppressionCore.cs:29`
- Modify: `src/Core/Suppression/LegacyFreezeRecovery.cs:15,60`
- Modify: `src/Platform/Paths.cs:24,25,53`
- Modify: `src/Core/LegacyPurge.cs:53-55`
- Modify: `src/Program.cs`(Mutex/事件名 3 处 + log 文件名 2 处 + ico 文件名 1 处)
- Modify: 11 个 Tweak 文件的 `*ByPavise` 键名 + 属性名
- Modify: `dev.cmd`(Pavise_Exit 2 处)
- Modify: `.gitignore`

- [ ] **Step 1: 注册表根**

`src/Platform/Settings.cs:12`:`@"Software\Pavise"` → `@"Software\Caelus"`

- [ ] **Step 2: 数据文件名常量(定义点)**

逐个改:
- `src/Core/GameMode.cs:164` `"Pavise.games.txt"` → `"Caelus.games.txt"`
- `src/Core/GameMode.cs:165` `"Pavise.whitelist.txt"` → `"Caelus.whitelist.txt"`
- `src/Core/GameMode.cs:166` `"Pavise.autoignore.txt"` → `"Caelus.autoignore.txt"`
- `src/Core/Detection/GameProfiles.cs:51` `"Pavise.profiles.dat"` → `"Caelus.profiles.dat"`
- `src/Core/Suppression/SuppressionCore.cs:29` `"Pavise.suppression.state"` → `"Caelus.suppression.state"`
- `src/Core/Suppression/LegacyFreezeRecovery.cs:15` `"Pavise.freeze.state"` → `"Caelus.freeze.state"`
- `src/Core/Suppression/LegacyFreezeRecovery.cs:60` `"Pavise.LegacyFreezeRecovery"` → `"Caelus.LegacyFreezeRecovery"`

- [ ] **Step 3: Paths.cs + LegacyPurge.cs 清理数组同步**

`src/Platform/Paths.cs:24-25` 和 `src/Core/LegacyPurge.cs:53-55` 里的 `"Pavise.games.txt"` 等全部改为 `"Caelus.*"` 对应名。同时 `Paths.cs:53` 的 `"Pavise.portable"` → `"Caelus.portable"`。

- [ ] **Step 4: Program.cs 里的 log/ico 文件名**

`src/Program.cs` 中:
- `:48` `"Pavise.ico"` → `"Caelus.ico"`
- `:72` `"Pavise.log"` → `"Caelus.log"`
- `:105` `"Pavise.preview.log"` → `"Caelus.preview.log"`
- `:194` `"Pavise.log"` → `"Caelus.log"`

- [ ] **Step 5: Mutex/事件名(Program.cs + dev.cmd)**

`src/Program.cs` 中(3 处 Mutex/EventWaitHandle 名):
- `"Global\\Pavise_SingleInstance"` → `"Global\\Caelus_SingleInstance"`
- `"Global\\Pavise_ShowPanel"` → `"Global\\Caelus_ShowPanel"`
- `"Global\\Pavise_Exit"` → `"Global\\Caelus_Exit"`

`dev.cmd` 中(2 处):
- `Global\Pavise_Exit` → `Global\Caelus_Exit`(PowerShell 里的 OpenExisting 调用)

- [ ] **Step 6: 回滚标志键(*ByPavise → *ByCaelus)**

批量替换 src/Core/Tweaks/ 和相关文件:
```bash
find src/Core -name "*.cs" -exec sed -i 's/ByPavise/ByCaelus/g' {} +
```
这覆盖:11 个 Tweak 文件的注册表键名字符串(如 `"DevPowerByPavise"` → `"DevPowerByCaelus"`)和 C# 属性名(`EnabledByPavise` → `EnabledByCaelus`)。同时覆盖 `LegacyPurge.cs` 里的属性引用和 `IrqAffinityEngine.cs`/`NetworkAffinityTweak.cs` 的构造参数。

验证覆盖完整:`grep -rn "ByPavise" src/` 应返回空。

- [ ] **Step 7: .gitignore**

`.gitignore` 中所有 `Pavise.*` → `Caelus.*`、`Pavise-v*.zip` → `Caelus-v*.zip`、`Pavise.exe.sha256` → `Caelus.exe.sha256`、`Pavise.freeze.state` 等:
```bash
sed -i 's/Pavise/Caelus/g' .gitignore
```

- [ ] **Step 8: 编译 + 自测**

`cmd.exe //c "dev.cmd test"` → 期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`
残留检查:`grep -rn "ByPavise" src/` 应为空;`grep -rn '"Pavise\.' src/` 应为空。

- [ ] **Step 9: 提交**

```bash
git add -A
git commit -m "refactor: 持久化标识符 Pavise → Caelus（注册表根/数据文件名/回滚标志键/Mutex）"
```

---

### Task 6: exe 名 + 图标文件名 + 构建脚本

**Files:**
- Modify: `build.cmd`
- Modify: `dev.cmd`
- Modify: `tools/PerfLab/build.ps1`
- Modify: `tools/PerfLab/PerfLab.cs`(输出名引用)
- Modify: `tools/PerfLab/PerfEngine.cs`(usage 文案/log 名)
- Delete: `Pavise.exe`、`Pavise.selftest.exe`(旧构建产物)
- Rename: `Pavise.ico` → `Caelus.ico`

- [ ] **Step 1: build.cmd**

`build.cmd` 中:
- `set OUT=Pavise.exe` → `set OUT=Caelus.exe`
- `generating Pavise.ico` → `generating Caelus.ico`(echo 文案)
- `-win32icon:Pavise.ico` → `-win32icon:Caelus.ico`

```bash
sed -i 's/Pavise\.exe/Caelus.exe/g; s/Pavise\.ico/Caelus.ico/g' build.cmd
```

- [ ] **Step 2: dev.cmd**

`dev.cmd` 中:
- `set OUT=Pavise.dev.exe` → `set OUT=Caelus.dev.exe`
- `Pavise.selftest.work.exe` → `Caelus.selftest.work.exe`
- `Pavise.selftest.txt` → `Caelus.selftest.txt`
- `Pavise_Exit` → `Caelus_Exit`(若 Task 5 的 sed 没覆盖到,这里补)
- `tasklist ... Pavise*` → `Caelus*`、`taskkill ... Pavise*` → `Caelus*`

```bash
sed -i 's/Pavise\.dev\.exe/Caelus.dev.exe/g; s/Pavise\.selftest/Caelus.selftest/g; s/Pavise\*/Caelus*/g; s/Pavise_Exit/Caelus_Exit/g' dev.cmd
```

- [ ] **Step 3: PerfLab 构建脚本和代码**

```bash
sed -i 's/Pavise\.PerfLab/Caelus.PerfLab/g; s/Pavise\.PerfEngine/Caelus.PerfEngine/g; s/Pavise\.PerfLauncher/Caelus.PerfLauncher/g; s/Pavise\.PerfBackground/Caelus.PerfBackground/g; s/Pavise\.perf\.log/Caelus.perf.log/g; s/Pavise PerfLab/Caelus PerfLab/g' tools/PerfLab/build.ps1 tools/PerfLab/PerfLab.cs tools/PerfLab/PerfEngine.cs
```

- [ ] **Step 4: 图标文件重命名 + 删除旧产物**

```bash
mv Pavise.ico Caelus.ico
rm -f Pavise.exe Pavise.selftest.exe
```

- [ ] **Step 5: 编译验证**

`cmd.exe //c "build.cmd"` → 期望 `Build OK -> Caelus.exe`

- [ ] **Step 6: 自测**

`cmd.exe //c "dev.cmd test"` → 期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`

- [ ] **Step 7: 提交**

```bash
git add -A
git commit -m "refactor: exe/图标/构建脚本 Pavise → Caelus"
```

---

### Task 7: 测试占位名 + 驱动 profile 名

**Files:**
- Modify: `tests/SelfTests.cs:1659`
- Modify: `tests/SelfTests.PowerPlan.cs`
- Modify: `tests/SelfTests.GpuTuning.cs`
- Modify: `src/Platform/NvApi.cs:215`

- [ ] **Step 1: 批量替换测试占位名**

```bash
sed -i 's/Pavise_Game/Caelus_Game/g; s/Pavise 自测/Caelus 自测/g; s/PaviseGpuMode/CaelusGpuMode/g' tests/SelfTests.cs tests/SelfTests.PowerPlan.cs tests/SelfTests.GpuTuning.cs
```

- [ ] **Step 2: NvApi profile 名**

`src/Platform/NvApi.cs:215` 中 `ProfileName = "Pavise - " + exeName` → `"Caelus - " + exeName`:
```bash
sed -i 's/Pavise - /Caelus - /g' src/Platform/NvApi.cs
```

- [ ] **Step 3: 编译 + 自测**

`cmd.exe //c "dev.cmd test"` → 期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`

- [ ] **Step 4: 提交**

```bash
git add -A
git commit -m "refactor: 测试占位名和驱动 profile 名 Pavise → Caelus"
```

---

### Task 8: @author 文件头批量替换

161 个文件的 `// @author bdth 2074055628@qq.com` → `// @author zenjiro 18967498922@163.com`。

**Files:**
- 全局:`src/`、`tests/`、`tools/` 下所有 .cs/.cmd/.ps1

- [ ] **Step 1: 批量替换**

```bash
find src tests tools -name "*.cs" -exec sed -i 's|@author bdth 2074055628@qq.com|@author zenjiro 18967498922@163.com|g' {} +
sed -i 's|@rem @author bdth 2074055628@qq.com|@rem @author zenjiro 18967498922@163.com|g' build.cmd dev.cmd
sed -i 's|# @author bdth 2074055628@qq.com|# @author zenjiro 18967498922@163.com|g' scripts/app-smoke-test.ps1
```

- [ ] **Step 2: 验证无残留**

`grep -rn "2074055628" src/ tests/ tools/ scripts/ build.cmd dev.cmd` 应为空。
`grep -rn "@author bdth" src/ tests/ tools/ scripts/ build.cmd dev.cmd` 应为空。

- [ ] **Step 3: 编译验证**

`cmd.exe //c "build.cmd"` → 期望 `Build OK`(注释改动不影响编译,但确认 sed 没误伤代码)

- [ ] **Step 4: 提交**

```bash
git add -A
git commit -m "refactor: 文件头 @author bdth → zenjiro（161 文件）"
```

---

### Task 9: LICENSE + README 三语

**Files:**
- Modify: `LICENSE`
- Modify: `README.md`、`README.en.md`、`README.ja.md`
- Delete: `docs/wechat.png`、`docs/alipay.png`(原作者收款码)

- [ ] **Step 1: LICENSE 追加衍生版权 + 改名**

在 `LICENSE` 第 1 行 `Pavise 许可协议` 之前插入衍生版权声明,并把协议正文中的产品名引用 Pavise → Caelus。保留 bdth 原版权作为上游记录。

具体:在文件最顶部插入:
```
Caelus 衍生版权声明
Copyright (c) 2026 zenjiro (18967498922@163.com)

Caelus 由 zenjiro 基于 Pavise 衍生开发。原始版权与许可条款见下方。

```
然后把原 `Pavise 许可协议 / Pavise License` 改为 `Caelus 许可协议 / Caelus License`(基于 Pavise)`。协议正文中的 "本软件指 Pavise" 改为 "本软件指 Caelus"。

- [ ] **Step 2: README.md(中文)**

- 标题 `# Pavise` → `# Caelus`;图标 alt `Pavise` → `Caelus`
- 作者段:`作者：bdth` → `作者：zenjiro`;邮箱;移除微信行;QQ 群段移除
- 移除赞赏码图片段(wechat.png/alipay.png 引用)
- 仓库地址 `dulaiduwang003/Pavise-Game` → `zenjiro/Caelus`
- 产品描述扩展为"游戏与开发"
- 数据目录段:`%AppData%\Pavise` → `%AppData%\Caelus`;文件名 `Pavise.profiles.dat` 等 → `Caelus.*`;注册表 `HKCU\Software\Pavise` → `HKCU\Software\Caelus`
- 代码结构段:`src/Core` 等描述里的命名空间/文件名引用同步

- [ ] **Step 3: README.en.md / README.ja.md**

同 Step 2 的改动,对应英文/日文版本。

- [ ] **Step 4: 删除原作者收款码图片**

```bash
rm -f docs/wechat.png docs/alipay.png
```

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "docs: LICENSE 追加衍生版权，README 三语改为 Caelus/zenjiro"
```

---

### Task 10: 全量验证 + 残留扫描

最终确认整个重构无遗漏。

- [ ] **Step 1: 全量残留扫描**

```bash
echo "=== src/ tests/ tools/ 中的 Pavise 残留(忽略大小写)==="
grep -rni "pavise" src/ tests/ tools/ | grep -v "衍生\|derived\|Pavise by bdth\|上游"
```
预期:仅 LICENSE 里的原版权记录(已在 grep -v 排除)和 ReleaseNotes.cs 历史日志(属"日志事实",保留)。其余应为空。

- [ ] **Step 2: 全量编译 + 自测**

`cmd.exe //c "dev.cmd test"` → 期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`

- [ ] **Step 3: 实际启动验证**

启动 Caelus.exe,确认:
- 托盘图标标题为 CAELUS
- 关于页:作者 zenjiro、邮箱 18967498922@163.com、仓库 zenjiro/Caelus、无微信行
- 版本号 v1.7.0
- 主窗口标题 CAELUS // CONTROL
- 数据写入 `%AppData%\Caelus\`(Caelus.log 等)
- 注册表 `HKCU\Software\Caelus`

- [ ] **Step 4: 最终提交**

```bash
git add -A
git commit -m "chore: Caelus 品牌重构全量验证通过"
```
