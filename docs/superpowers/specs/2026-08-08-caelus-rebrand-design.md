# Caelus 品牌重构设计

## 目标

把项目从原作者 bdth 的 Pavise 彻底重构为 zenjiro 的 Caelus。覆盖所有身份信息:产品名、作者署名、联系方式、命名空间、数据文件名、注册表根、图标、LICENSE、README、文件头注释。

## 身份常量(唯一真理源)

| 字段 | 旧值 | 新值 |
|------|------|------|
| 产品显示名 | `PAVISE` | `CAELUS` |
| 版本 | `1.6.7` | `1.7.0`(大版本重构,版本号跳升) |
| 作者 | `bdth` | `zenjiro` |
| 邮箱 | `2074055628@qq.com` | `18967498922@163.com` |
| 微信 | `Ssssssstyle` | (移除,关于页微信行删除) |
| 仓库名 | `dulaiduwang003/Pavise-Game` | `zenjiro/Caelus`(占位,建好后替换) |
| 命名空间 | `PaviseApp` | `CaelusApp` |
| PerfLab 命名空间 | `PavisePerfLab` | `CaelusPerfLab` |

## 关键决策

1. **全新项目,不做数据迁移**:注册表根 `Software\Pavise` → `Software\Caelus`、数据文件名 `Pavise.*` → `Caelus.*`、回滚标志键 `*ByPavise` → `*ByCaelus` 直接改,不写迁移逻辑(用户确认无老用户)。
2. **GitHub 仓库占位**:`RepoName = "zenjiro/Caelus"`,更新检查暂指向不存在的仓库(会报错,建好仓库后即可用)。
3. **LICENSE 处理**:原协议要求衍生作品保留原版权声明 + 换名换图标。做法:在原 LICENSE 顶部追加 zenjiro 的衍生版权声明,保留 bdth 的原始版权作为上游记录。原协议的"禁止销售"条款继续约束本衍生品(除非另获授权)。
4. **微信行移除**:关于页的"微信"信息行删除(zenjiro 暂不提供微信),保留作者/邮箱/仓库/许可四行。

## 重构阶段(按风险从低到高排序)

### 阶段 1:身份常量 + AssemblyInfo(单点真理源)

最高杠杆、最低风险。改 `Program.cs:22-30` 的 7 个常量 + `AssemblyInfo.cs` 的 4 个属性。这一步完成后,关于页的作者/邮箱/仓库自动更新(它们读 App.* 常量)。

**改动:**
- `src/Program.cs:22-28`:DisplayName/Author/AuthorEmail/WeChat/RepoName/RepoUrl
- `src/Program.cs:30`:Version `1.6.7` → `1.7.0`(VersionTag 随之变 v1.7.0)
- `src/AssemblyInfo.cs:7,9,10,11`:Title/Company/Product/Copyright
- `src/Ui/Pages/PanelForm.AboutPage.cs:35-37`:关于页四行——删除微信行(WeChat),rowKeys/rowVals 从 4 行改 3 行

**影响测试:** ReleaseNotes.cs 里 `new ReleaseNote("1.7.0", ...)` 需新增一条版本日志(否则 HasUnseen 逻辑仍正常,但关于页版本号变了)。可选:加一条 1.7.0 的更新说明。

### 阶段 2:产品名字符串(展示层)

散落在 UI 各处的硬编码 "Pavise" 字符串,改为 "Caelus"。

**改动:**
- `src/Ui/PanelForm.Widgets.cs:19`:`"PAVISE  //  CONTROL"` → `"CAELUS  //  CONTROL"`
- `src/Ui/TrayMenu.cs:273`:MessageBox 标题 `"Pavise"` → `"Caelus"`
- `src/Ui/Pages/PanelForm.EnvironmentPage.cs`(约 10 处):MessageBox 标题 `"Pavise"` → `"Caelus"`
- `src/Platform/Lang.cs`:`about.lic.value` `"Pavise 许可协议 · 禁止销售"` → `"Caelus 许可协议 · 禁止销售"`;`about.desc` 描述文案改写(从"游戏"扩展为"游戏与开发");托盘/气泡文案中的 Pavise

### 阶段 3:命名空间全局替换

`PaviseApp` → `CaelusApp`、`PavisePerfLab` → `CaelusPerfLab`。157 个声明文件 + build.ps1 的 main 入口。

**改动:**
- 全局替换 `namespace PaviseApp` → `namespace CaelusApp`(src/ tests/ tools/,157 文件)
- `tools/PerfLab/build.ps1:36`:`-main:PaviseApp.PerfEngineProgram` → `-main:CaelusApp.PerfEngineProgram`
- `tools/PerfLab/PerfLab.cs`:`namespace PavisePerfLab` → `namespace CaelusPerfLab`
- 验证:全量编译 + selftest(命名空间改名不影响逻辑,但编译器会抓住所有遗漏)

### 阶段 4:持久化标识符(注册表 + 数据文件名 + 回滚标志键)

全新项目直接改,不迁移。

**改动:**
- `src/Platform/Settings.cs:12`:`Key = @"Software\Pavise"` → `@"Software\Caelus"`
- 数据文件名常量(7 处定义点):
  - `src/Core/GameMode.cs:164-166`:games/whitelist/autoignore 文件名
  - `src/Core/Detection/GameProfiles.cs:51`:profiles.dat
  - `src/Core/Suppression/SuppressionCore.cs:29`:suppression.state
  - `src/Core/Suppression/LegacyFreezeRecovery.cs:15`:freeze.state
  - `src/Platform/Paths.cs:24,25,53`:Paths 数组 + portable 标记
- `src/Platform/Paths.cs` + `src/Core/LegacyPurge.cs`:LegacyPurge 的清理数组同步改名
- 回滚标志键(11 个 Tweak 文件,~35 处):`*ByPavise` → `*ByCaelus`
- 单实例 Mutex/事件名:`Global\Pavise_SingleInstance` → `Global\Caelus_SingleInstance`、`Global\Pavise_ShowPanel` → `Global\Caelus_ShowPanel`、`Global\Pavise_Exit` → `Global\Caelus_Exit`
- `src/Program.cs`:Mutex 名 + EventWaitHandle 名(3 处)
- `dev.cmd`:`Global\Pavise_Exit` → `Global\Caelus_Exit`(2 处)
- `.gitignore`:`Pavise.*` → `Caelus.*` 全部替换

### 阶段 5:exe 名 + 图标 + 构建脚本

**改动:**
- `build.cmd`:`set OUT=Pavise.exe` → `Caelus.exe`;图标相关 `Pavise.ico` → `Caelus.ico`
- `dev.cmd`:`Pavise.dev.exe` → `Caelus.dev.exe`;selftest 相关命名
- `src/Program.cs:48`:`--genicon` 写出 `Caelus.ico`
- `tools/PerfLab/build.ps1` + PerfLab.cs:输出名 `Pavise.PerfLab.exe` → `Caelus.PerfLab.exe` 等
- 实际文件重命名:`Pavise.ico` → `Caelus.ico`(根目录);删除旧 `Pavise.exe`/`Pavise.selftest.exe`(构建产物)

### 阶段 6:LICENSE + README 三语

**LICENSE:**
- 顶部追加衍生版权:`Copyright (c) 2026 zenjiro (18967498922@163.com). Caelus is derived from Pavise by bdth.`
- 保留 bdth 原始版权段
- 名称引用 Pavise → Caelus(协议正文中的"本软件指 Pavise"改为"本软件指 Caelus")

**README.md / README.en.md / README.ja.md:**
- 标题 `# Pavise` → `# Caelus`;图标 alt
- 作者和许可段:作者 → zenjiro、邮箱、移除微信/QQ 群、仓库地址
- 移除赞赏码图片段(wechat.png/alipay.png 是原作者收款码)
- 产品描述从"游戏"扩展为"游戏与开发"
- 代码结构段:文件名引用 `Pavise.exe` → `Caelus.exe`

### 阶段 7:文件头注释批量替换

161 个文件的 `// @author bdth 2074055628@qq.com` → `// @author zenjiro 18967498922@163.com`(或自定义格式)。机械替换,最后做,配合全量编译验证。

### 阶段 8:测试占位名同步

- `tests/SelfTests.cs:1659`:断言 `"Pavise_Game"` 前缀 → `Caelus_Game`
- `tests/SelfTests.PowerPlan.cs`:`"Pavise 自测临时计划"` → `"Caelus 自测临时计划"`
- `tests/SelfTests.GpuTuning.cs`:`"PaviseGpuMode_"` → `"CaelusGpuMode_"`
- `src/Platform/NvApi.cs:215`:NVIDIA profile 名 `"Pavise - "` → `"Caelus - "`
- `src/Core/Tamer.cs:277`:日志标签 `"Pavise 退出"` → `"Caelus 退出"`

## 验证策略

每个阶段完成后:
1. `cmd.exe //c "build.cmd"` 编译通过
2. `cmd.exe //c "dev.cmd test"` 152 项自测 PASS 149 / FAIL 0 / SKIP 3
3. 阶段 1 后:实际启动,确认关于页作者/版本号更新
4. 阶段 4 后:确认注册表写入 `Software\Caelus`、数据文件 `Caelus.*`
5. 全部完成后:全量启动验证,确认无任何 Pavise 残留(`grep -ri pavise src/` 应为空或仅剩 LICENSE 原版权记录)

## 不在本次范围

- 图标视觉重设计(IconArt 的配色/图形)——许可要求换图标,但视觉设计可后续单独做;本次只改文件名
- 新增"程序员开发"相关功能——定位扩展先体现在文案,功能后续规划
- GitHub 仓库实际创建——建好后替换 RepoName 占位即可
