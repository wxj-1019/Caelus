# 深化开发模式 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补全开发模式五个短板:自定义编译进程列表、编译收益报告、热重载守护识别、IDE 语言服务器豁免、Git/Docker build/测试运行识别。

**Architecture:** 全部改动集中在 `BuildCatalog.cs`(识别目录)、`DevServiceWhitelist.cs`(豁免目录)、`BuildWatch.cs`(状态机)、`SettingsPage.cs`(设置 UI)、`Lang.cs`(文案)。不改变开发模式核心架构。

**Tech Stack:** C# / .NET Framework 4.x / WinForms,`csc.exe` 直接编译。

**构建/测试命令:**
- 编译:`cmd.exe //c "build.cmd"`
- 自测:`cmd.exe //c "dev.cmd test"`(期望 `TOTAL 152  PASS 149  FAIL 0  SKIP 3`)

**关键接口(已核实):**
- `BuildCatalog.IsMatch(name)` — `src/Core/BuildCatalog.cs:27`
- `DevServiceWhitelist.Contains(name)` — `src/Core/DevServiceWhitelist.cs:22`
- `Settings.LoadStr(name, def)` / `SaveStr(name, val)` — `src/Platform/Settings.cs:99/118`
- `Theme.MakeTextBox(x, y, w)` — `src/Ui/Theme.cs:295`,返回 `TextBox`
- `MakeAutoCard(scroll, x, y, w, h, title, note, control, out cardH)` — `SettingsPage.cs`
- `BuildWatch.NotifyProcessChanges(batch)` / `ActivateSuppression` / `DeactivateSuppression` — `src/Core/BuildWatch.cs`

---

### Task 1: 热重载守护 + IDE 语言服务器加入豁免目录(极低复杂度)

把热重载守护进程和 IDE 语言服务器加入 `DevServiceWhitelist.Names`。

**Files:**
- Modify: `src/Core/DevServiceWhitelist.cs`

- [ ] **Step 1: 扩展豁免列表**

在 `DevServiceWhitelist.Names` 的"消息队列"行后追加:

```csharp
            // 热重载守护（编译期间不压，避免热重载失效）
            "dotnet-watch", "nodemon", "webpack-dev-server", "vite", "concurrently", "pm2",
            // IDE 语言服务器（编译期间不压，保证智能提示/补全不卡）
            "omnisharp", "roslyn", "jdtls", "jdt", "eclipse-jdt", "pyls", "pyright", "pylance",
            "jedi-language-server", "tsserver", "vscode-language-server", "gopls", "rust-analyzer",
            "clangd", "ccls", "language-server", "lsp"
```

- [ ] **Step 2: 编译验证**

`cmd.exe //c "build.cmd"` → `Build OK`

- [ ] **Step 3: 暂不提交**

---

### Task 2: Git / Docker build / 测试运行器加入识别目录

把 Git、Docker、测试运行器加入 `BuildCatalog.Names`。

**Files:**
- Modify: `src/Core/BuildCatalog.cs`

- [ ] **Step 1: 扩展识别目录**

在 `BuildCatalog.Names` 的"调试器"行后追加:

```csharp
            // Git 大规模 IO 操作（rebase/gc/pack/clone）
            "git", "git-bash", "git-cmd", "hub", "gh",
            // Docker（build/run 都触发；守护进程 dockerd 在豁免列表不受影响）
            "docker", "docker-buildx",
            // 测试运行器
            "nunit3-console", "vstest.console", "pytest", "jest", "mocha", "go-test"
```

- [ ] **Step 2: 编译验证**

`cmd.exe //c "build.cmd"` → `Build OK`

- [ ] **Step 3: 暂不提交**

---

### Task 3: 编译收益报告

BuildWatch 在编译会话期间记录开始时间和压制统计,结束时写报告日志。

**Files:**
- Modify: `src/Core/BuildWatch.cs`

- [ ] **Step 1: 加统计字段**

在 `private bool suppressing;` 后加:

```csharp
        private long sessionStartTicks;
        private int sessionSuppressedCount;
```

- [ ] **Step 2: 记录会话开始和压制数**

`ActivateSuppression` 方法开头(在 `try {` 后)加:

```csharp
                sessionStartTicks = DateTime.UtcNow.Ticks;
```

`DeactivateSuppression` 里,在 `Logger.Log("开发模式：编译/调试进程已退出，恢复后台资源");` 前加报告日志:

```csharp
                long elapsedMs = (DateTime.UtcNow.Ticks - sessionStartTicks) / TimeSpan.TicksPerMillisecond;
                if (elapsedMs >= 0)
                    Logger.Log(string.Format("开发模式：本次编译 {0:0.#} 秒，期间压制 {1} 个后台进程",
                        elapsedMs / 1000.0, sessionSuppressedCount));
```

- [ ] **Step 3: 编译 + 自测**

`cmd.exe //c "dev.cmd test"` → `TOTAL 152  PASS 149  FAIL 0  SKIP 3`

- [ ] **Step 4: 暂不提交**

---

### Task 4: 自定义编译进程列表(核心)

BuildCatalog 从注册表加载用户自定义进程名;设置页加文本输入。

**Files:**
- Modify: `src/Core/BuildCatalog.cs`
- Modify: `src/Ui/Pages/PanelForm.SettingsPage.cs`
- Modify: `src/Platform/Lang.cs`

- [ ] **Step 1: BuildCatalog 加自定义列表**

在 `BuildCatalog` 类里加:

```csharp
        private const string CustomKey = "CustomBuildProcs";
        private static readonly object CustomLock = new object();
        private static HashSet<string> customNames;
        private static long customLoadedTicks;

        public static bool IsCustomMatch(string processName)
        {
            return false;
        }
```

不——需要实际逻辑。改为(完整方法):

```csharp
        private static readonly object CustomLock = new object();
        private static HashSet<string> customNames;

        // 自定义编译进程名（分号/换行分隔），存注册表，设置页可编辑
        public static string CustomList
        {
            get { return Settings.LoadStr(CustomKey, ""); }
            set { Settings.SaveStr(CustomKey, value ?? ""); lock (CustomLock) customNames = null; }
        }

        public static bool IsMatch(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            string bare = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4) : processName;
            if (Names.Contains(bare)) return true;
            HashSet<string> custom = LoadCustom();
            return custom != null && custom.Contains(bare);
        }

        private static HashSet<string> LoadCustom()
        {
            lock (CustomLock)
            {
                if (customNames != null) return customNames;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = Settings.LoadStr(CustomKey, "");
                if (raw != null)
                    foreach (string part in raw.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = part.Trim();
                        if (t.Length > 0) set.Add(t);
                    }
                customNames = set;
                return set;
            }
        }
```

- [ ] **Step 2: 设置页加文本输入**

在 `swDev` 开关卡片(`:46-49`)之后、`sy += 10;` 之前,加:

```csharp
            var devCustom = Theme.MakeTextBox(Theme.S(110), Theme.S(6), Theme.S(ScrollContentW - 130));
            devCustom.Text = BuildCatalog.CustomList;
            devCustom.Height = Theme.S(44);
            devCustom.Multiline = true;
            devCustom.ScrollBars = ScrollBars.Vertical;
            var btnDevSave = new PillButton(Lang.T("set.dev.custom.save"), BtnKind.Normal);
            btnDevSave.Bg = Theme.Card;
            btnDevSave.Size = new Size(Theme.S(76), Theme.S(28));
            btnDevSave.Location = new Point(Theme.S(ScrollContentW - 90), Theme.S(20));
            btnDevSave.Click += delegate
            {
                BuildCatalog.CustomList = devCustom.Text;
                btnDevSave.Text = Lang.T("set.dev.custom.saved");
            };
            var devCustomPanel = new DBPanel();
            devCustomPanel.SetBounds(Theme.S(6), Theme.S(sy), Theme.S(ScrollContentW), Theme.S(56));
            devCustomPanel.BackColor = Theme.Card;
            devCustomPanel.Controls.Add(devCustom);
            devCustomPanel.Controls.Add(btnDevSave);
            scroll.Controls.Add(devCustomPanel);
            sy += 64;
```

注意:需要核实 `DBPanel`、`PillButton`、`ScrollBars` 的 using 是否已在 SettingsPage 可用(通常已 in using System.Windows.Forms)。

- [ ] **Step 3: Lang.cs 加文案**

在 `set.dev.n` 后加:

```csharp
            { "set.dev.custom", new[]{ "自定义编译进程（分号分隔）" } },
            { "set.dev.custom.n", new[]{ "内置目录没覆盖到的编译/构建工具，手动加进程名（如 mybuilder.exe 写 mybuilder）" } },
            { "set.dev.custom.save", new[]{ "保存" } },
            { "set.dev.custom.saved", new[]{ "已保存" } },
```

- [ ] **Step 4: 编译 + 自测**

`cmd.exe //c "dev.cmd test"` → `TOTAL 152  PASS 149  FAIL 0  SKIP 3`

若 UI 构建报错(DBPanel/ScrollBars 不可见),按报错调整 using 或控件类型。

- [ ] **Step 5: 暂不提交**

---

### Task 5: 最终验证 + 提交

- [ ] **Step 1: 全量编译 + 自测**

`cmd.exe //c "dev.cmd test"` → `TOTAL 152  PASS 149  FAIL 0  SKIP 3`

- [ ] **Step 2: 手动验证**

1. 启动 Caelus → 设置页 → 开发模式开关下应看到"自定义编译进程"输入框
2. 输入 `mybuilder` 保存 → 运行一个名为 mybuilder.exe 的假进程 → 确认日志出现"开发模式:检测到编译/调试进程"
3. 触发一次编译 → 确认日志出现"本次编译 X 秒,期间压制 N 个后台进程"
4. 启动 nodemon(若装了)→ 触发编译 → 确认 nodemon 未被压
5. 运行 `git gc`(在任意 git 仓库)→ 确认开发模式激活

- [ ] **Step 3: 提交**

```bash
git add -A
git commit -m "feat: 深化开发模式——自定义进程列表+收益报告+热重载/IDE保护+Git/Docker识别

- BuildCatalog: 自定义编译进程（注册表存储，设置页可编辑）；新增 git/docker/测试运行器识别
- DevServiceWhitelist: 新增热重载守护（nodemon/dotnet-watch）和 IDE 语言服务器（omnisharp/tsserver 等）
- BuildWatch: 编译会话结束时输出耗时和压制统计报告
- 设置页: 开发模式卡片下新增自定义进程输入框
- Lang.cs: 新增 set.dev.custom.* 文案"
```
