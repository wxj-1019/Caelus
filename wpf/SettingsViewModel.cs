// @author zenjiro 18967498922@163.com
// 文件用途 WPF 设置页 ViewModel：应用偏好、维护状态与页面反馈

using System;
using System.Text;
using System.Threading;
using System.Windows;
using CaelusApp.WpfHost;

namespace CaelusApp
{
    internal sealed class SettingsViewModel : ViewModelBase
    {
        private readonly GameMode gameMode;
        private readonly Tamer tamer;
        private readonly DevFocus devFocus;
        private bool autoStart;
        private bool autoHide;
        private bool lightMode;
        private bool devMode;
        private bool focusMode;
        private bool ideOn;
        private bool dailyCare;
        private bool batteryOn;
        private string shaderStatus;
        private string pageFeedback = "";
        private string pageFeedbackKind = "Info";
        private bool shaderBusy;
        private int restoreBusy; // 0=空闲 1=进行中（Interlocked 守护）

        public SettingsViewModel(GameMode gameMode, Tamer tamer) : this(gameMode, tamer, null) { }

        public SettingsViewModel(GameMode gameMode, Tamer tamer, DevFocus devFocus)
        {
            this.gameMode = gameMode;
            this.tamer = tamer;
            this.devFocus = devFocus;
            autoStart = TaskHelper.TaskExistsCached();
            autoHide = Settings.Load("AutoHideOnGame", false);
            lightMode = Settings.Load("UiLight", false);
            devMode = Settings.Load("DevModeOn", true);
            focusMode = Settings.Load("DevFocusModeOn", false);
            ideOn = Settings.Load("DevFocusIdeOn", true);
            dailyCare = Settings.Load("DailyCareOn", true);
            batteryOn = Settings.Load("DailyCareBatteryOn", true);
            shaderStatus = Lang.T("set.shader.n");

            // 与旧 WinForms 的 RefreshSlowStateAsync 一致：后台异步测量着色器缓存占用并回显
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    long bytes = ShaderCache.MeasureBytes();
                    System.Windows.Application app = System.Windows.Application.Current;
                    if (app == null || bytes < 0) return;
                    app.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { if (!shaderBusy) ShaderStatus = CacheSweep.FmtBytes(bytes); } catch { }
                    }));
                }
                catch { }
            });
        }

        // —— 分组标题 ——
        public string AppSectionTitle { get { return Lang.T("sec.app"); } }
        public string MaintSectionTitle { get { return Lang.T("sec.maint"); } }
        internal GameMode GameMode { get { return gameMode; } }
        public string PreferenceSummary
        {
            get
            {
                int enabled = (autoStart ? 1 : 0) + (autoHide ? 1 : 0) + (lightMode ? 1 : 0);
                return enabled == 0 ? "当前未启用自动化偏好" : "已启用 " + enabled + " 项应用偏好";
            }
        }
        public string DevSummary { get { return devMode ? "开发模式已开启。" : "开发模式已关闭。"; } }
        public string PageFeedback { get { return pageFeedback; } private set { SetProperty(ref pageFeedback, value, "PageFeedback"); } }
        public string PageFeedbackKind { get { return pageFeedbackKind; } private set { SetProperty(ref pageFeedbackKind, value, "PageFeedbackKind"); } }
        public bool IsShaderBusy { get { return shaderBusy; } set { SetProperty(ref shaderBusy, value, "IsShaderBusy"); } }

        // —— 开机自启 ——
        public string AutoStartTitle { get { return Lang.T("set.autostart"); } }
        public string AutoStartNote { get { return Lang.T("set.autostart.n"); } }
        public bool AutoStart
        {
            get { return autoStart; }
            set
            {
                if (!SetProperty(ref autoStart, value, "AutoStart")) return;
                int rc = value ? TaskHelper.CreateStartupTask() : TaskHelper.DeleteStartupTask();
                if (rc != 0)
                {
                    Logger.Log("开机自启任务操作失败 rc=" + rc);
                    autoStart = TaskHelper.TaskExists();
                    Raise("AutoStart");
                    ShowFeedback("开机自启设置未能保存，已恢复为实际状态。", "Warning");
                }
                else ShowFeedback("开机自启偏好已保存。", "Success");
                Raise("PreferenceSummary");
            }
        }

        // —— 自动收起 ——
        public string AutoHideTitle { get { return Lang.T("set.autohide"); } }
        public string AutoHideNote { get { return Lang.T("set.autohide.n"); } }
        public bool AutoHide
        {
            get { return autoHide; }
            set
            {
                if (!SetProperty(ref autoHide, value, "AutoHide")) return;
                Settings.Save("AutoHideOnGame", value);
                ShowFeedback("自动收起偏好已保存。", "Success");
                Raise("PreferenceSummary");
            }
        }

        // —— 明暗主题 ——
        public string LightModeTitle { get { return Lang.T("set.light"); } }
        public string LightModeNote { get { return Lang.T("set.light.n"); } }
        public bool LightMode
        {
            get { return lightMode; }
            set
            {
                if (!SetProperty(ref lightMode, value, "LightMode")) return;
                Settings.Save("UiLight", value);
                if (Application.Current != null)
                    ThemeManager.Apply(Application.Current,
                        value ? UiTone.Light : UiTone.Dark, ThemeManager.CurrentMode);
                ShowFeedback("主题偏好已保存。", "Success");
                Raise("PreferenceSummary");
            }
        }

        // —— 开发模式 ——
        public string DevModeTitle { get { return Lang.T("set.dev"); } }
        public string DevModeNote { get { return Lang.T("set.dev.n"); } }
        public bool DevMode
        {
            get { return devMode; }
            set
            {
                if (!SetProperty(ref devMode, value, "DevMode")) return;
                Settings.Save("DevModeOn", value);
                Raise("DevSummary");
                ShowFeedback(value ? "开发模式已开启。" : "开发模式已关闭。", "Success");
            }
        }

        // —— 专注模式 ——
        public string FocusModeTitle { get { return Lang.T("set.focus"); } }
        public string FocusModeNote { get { return Lang.T("set.focus.n"); } }
        public bool FocusMode
        {
            get { return focusMode; }
            set
            {
                if (!SetProperty(ref focusMode, value, "FocusMode")) return;
                // 与托盘菜单一致走 SetFocusMode：写注册表 + 活性重算立即生效 + 关闭时清理分心记录
                if (devFocus != null) devFocus.SetFocusMode(value);
                else Settings.Save("DevFocusModeOn", value);
                ShowFeedback(value ? "专注模式已开启。" : "专注模式已关闭。", "Success");
            }
        }

        // —— 专注时长统计 ——
        public string FocusStatsText
        {
            get
            {
                long sec = FocusStats.TodaySeconds(DateTime.Now);
                int n = FocusStats.TodaySessions(DateTime.Now);
                return Lang.F("set.focus.stats", (sec / 60).ToString(), n.ToString());
            }
        }

        // —— IDE 优化 ——
        public string IdeTitle { get { return Lang.T("set.ide"); } }
        public string IdeNote { get { return Lang.T("set.ide.n"); } }
        public bool IdeOn
        {
            get { return ideOn; }
            set
            {
                if (!SetProperty(ref ideOn, value, "IdeOn")) return;
                // 与专注模式一致走 SetIdeOn：写注册表 + 活性重算 + 关闭时清空已追踪 IDE 集合
                if (devFocus != null) devFocus.SetIdeOn(value);
                else Settings.Save("DevFocusIdeOn", value);
                ShowFeedback(value ? "IDE 优化已开启。" : "IDE 优化已关闭。", "Success");
            }
        }

        // —— 分心应用清单 ——
        public string DistractTitle { get { return Lang.T("set.distract"); } }
        public string DistractNote { get { return Lang.T("set.distract.n"); } }
        public string DistractInitial { get { return Settings.LoadStr("DevFocusDistractList", ""); } }
        public void SaveDistract(string text)
        {
            Settings.SaveStr("DevFocusDistractList", text ?? "");
            DistractCatalog.Reload();
            ShowFeedback("分心应用清单已保存。", "Success");
        }

        // —— 开发服务守护 ——
        public string DevSvcTitle { get { return Lang.T("set.devsvc"); } }
        public string DevSvcNote { get { return Lang.T("set.devsvc.n"); } }
        public string DevSvcInitial { get { return Settings.LoadStr("DevServiceList", ""); } }
        public void SaveDevSvc(string text)
        {
            Settings.SaveStr("DevServiceList", text ?? "");
            DevServiceCatalog.Reload();
            ShowFeedback("开发服务守护清单已保存。", "Success");
        }

        // —— 开发环境体检（只读） ——
        private string devEnvResult = "";
        public string DevEnvTitle { get { return Lang.T("set.devenv"); } }
        public string DevEnvNote { get { return Lang.T("set.devenv.n"); } }
        public string DevEnvRunText { get { return Lang.T("set.devenv.run"); } }
        public string DevEnvResult
        {
            get { return devEnvResult; }
            private set { SetProperty(ref devEnvResult, value, "DevEnvResult"); }
        }
        /// <summary>后台线程调用：探测工具链并返回格式化结果。</summary>
        public string RunDevEnvAudit()
        {
            var sb = new StringBuilder();
            foreach (DevEnvAudit.DevEnvItem it in DevEnvAudit.Run())
                sb.AppendLine(it.Name.PadRight(12) + it.Detail);
            return sb.ToString().TrimEnd();
        }
        public void SetDevEnvResult(string text)
        {
            DevEnvResult = text ?? "";
        }

        // —— 自定义编译进程 ——
        public string DevCustomTitle { get { return Lang.T("set.dev.custom"); } }
        public string DevCustomNote { get { return Lang.T("set.dev.custom.n"); } }
        public string DevCustomSaveText { get { return Lang.T("set.dev.custom.save"); } }
        public string DevCustomInitial { get { return BuildCatalog.CustomList; } }
        public void SaveDevCustom(string text)
        {
            BuildCatalog.CustomList = text ?? "";
            ShowFeedback("自定义编译进程已保存。", "Success");
        }

        // —— 维护：一键恢复 ——
        public string RestoreTitle { get { return Lang.T("v15.restore.title"); } }
        public string RestoreDesc { get { return Lang.T("v15.restore.desc"); } }
        public string RestoreText { get { return Lang.T("btn.panic"); } }
        public bool IsRestoreBusy
        {
            get { return Interlocked.CompareExchange(ref restoreBusy, 0, 0) != 0; }
            set { Interlocked.Exchange(ref restoreBusy, value ? 1 : 0); Raise("IsRestoreBusy"); }
        }

        // 执行一键恢复（在后台线程调用）。返回各计数，由 UI 层组装提示。
        public void RestoreAll(out bool completed, out int failed, out int attempted)
        {
            failed = 0; attempted = 0;
            try
            {
                attempted++;
                if (!TryRestore("游戏模式", gameMode.PanicRestore())) failed++;
                attempted++;
                if (!TryRestore("反作弊压制", tamer.PanicRestore())) failed++;
                completed = failed == 0;
                Logger.Log("一键全部恢复：已执行 " + attempted
                    + " 项，失败 " + failed + " 项；"
                    + (completed ? "恢复流程已完成" : "未确认项保留并继续重试"));
            }
            catch (System.Exception ex)
            {
                completed = false;
                attempted++;
                failed++;
                Logger.LogFailure("一键全部恢复流程", ex);
            }
        }

        private static bool TryRestore(string name, bool restored)
        {
            if (!restored) Logger.Log("一键全部恢复：" + name + " 未确认完成");
            return restored;
        }

        // —— 维护：Defender 排除 / 附加层 ——
        public string DefenderTitle { get { return Lang.T("def.open"); } }
        public string DefenderDesc { get { return Lang.T("def.open.sub"); } }
        public string DefenderText { get { return Lang.T("btn.open"); } }
        public string AddonTitle { get { return Lang.T("addon.open"); } }
        public string AddonDesc { get { return Lang.T("addon.open.sub"); } }
        public string AddonText { get { return Lang.T("btn.open"); } }

        // —— 日常优化 ——
        public string DailySectionTitle { get { return Lang.T("sec.daily"); } }
        public string DailyCareTitle { get { return Lang.T("set.daily"); } }
        public string DailyCareNote { get { return Lang.T("set.daily.n"); } }
        public bool DailyCare
        {
            get { return dailyCare; }
            set
            {
                if (!SetProperty(ref dailyCare, value, "DailyCare")) return;
                Settings.Save("DailyCareOn", value);
                ShowFeedback(value ? "日常场景调度已开启。" : "日常场景调度已关闭。", "Success");
            }
        }
        public string StartupNewsTitle { get { return Lang.T("set.startup.news"); } }
        public string StartupNews
        {
            get
            {
                string news = Settings.LoadStr("HealthStartupNews", "");
                return news.Length == 0 ? Lang.T("set.startup.none") : news;
            }
        }

        // —— 电池供电增强 ——
        public string BatteryTitle { get { return Lang.T("set.battery"); } }
        public string BatteryNote { get { return Lang.T("set.battery.n"); } }
        public bool BatteryOn
        {
            get { return batteryOn; }
            set
            {
                if (!SetProperty(ref batteryOn, value, "BatteryOn")) return;
                Settings.Save("DailyCareBatteryOn", value);
                ShowFeedback(value ? "电池供电增强已开启。" : "电池供电增强已关闭。", "Success");
            }
        }

        // —— 健康维护频率（1-30 天） ——
        public string HealthFreqTitle { get { return Lang.T("set.health.freq"); } }
        public string HealthFreqNote { get { return Lang.T("set.health.freq.n"); } }
        public string HealthFreqInitial { get { return HealthCare.IntervalDays().ToString(); } }
        public void SaveHealthFreq(string text)
        {
            int days;
            if (!int.TryParse((text ?? "").Trim(), out days) || days < 1 || days > 30)
            {
                ShowFeedback("请输入 1-30 之间的天数。", "Error");
                return;
            }
            Settings.SaveStr("HealthIntervalDays", days.ToString());
            ShowFeedback("健康维护频率已保存（每 " + days + " 天一次）。", "Success");
        }

        // —— 外观：深浅模式与三模式强调色 ——
        public string ThemeSectionTitle { get { return Lang.T("set.theme"); } }
        public string ThemeSectionNote { get { return Lang.T("set.theme.n"); } }
        public string ToneModeTitle { get { return Lang.T("set.theme.tone"); } }
        public string AccentSectionTitle { get { return Lang.T("set.theme.accent"); } }
        public string AccentHexLabel { get { return Lang.T("set.theme.accent.hex"); } }

        /// <summary>深浅三态：0=深色 1=浅色 2=跟随系统（注册表 UiToneMode；−1=未设置回退 UiLight）。</summary>
        public int ToneMode
        {
            get
            {
                int raw;
                if (!int.TryParse(Settings.LoadStr("UiToneMode", "-1"), out raw)) raw = -1;
                if (raw >= 0 && raw <= 2) return raw;
                // 兼容旧键 UiLight
                return Settings.Load("UiLight", false) ? 1 : 0;
            }
            set
            {
                if (value < 0 || value > 2) return;
                Settings.SaveStr("UiToneMode", value.ToString());
                Raise("ToneMode");
                ApplyToneFromSetting();
            }
        }

        /// <summary>根据当前 UiToneMode 解析并应用深浅主题。</summary>
        public void ApplyToneFromSetting()
        {
            int mode = ToneMode;
            UiTone tone;
            if (mode == 2)
            {
                // 跟随系统：读 Windows 个性化注册表
                tone = ThemeManager.ProbeSystemTheme();
            }
            else
            {
                tone = mode == 1 ? UiTone.Light : UiTone.Dark;
            }
            try
            {
                ThemeManager.Apply(Application.Current, tone, ThemeManager.CurrentMode);
            }
            catch { }
        }

        /// <summary>预设色板（10 色），供设置页色板 UI 消费。</summary>
        public static readonly System.Collections.Generic.List<string> AccentPresetColors =
            new System.Collections.Generic.List<string>(AccentMath.PresetColors);

        public string AccentStandardDisplay { get { return Settings.LoadStr("AccentStandard", ""); } }
        public string AccentCompetitiveDisplay { get { return Settings.LoadStr("AccentCompetitive", ""); } }
        public string AccentCustomDisplay { get { return Settings.LoadStr("AccentCustom", ""); } }

        /// <summary>应用强调色（色板点击或 hex 保存时调用），即时预览。</summary>
        public void ApplyAccent(string registryKey, string hex)
        {
            Settings.SaveStr(registryKey, hex ?? "");
            try { ThemeManager.ApplyAccentOverride(Application.Current); } catch { }
            ShowFeedback(Lang.T("set.theme.accent.applied"), "Success");
        }

        /// <summary>重置单个模式强调色为预设。</summary>
        public void ResetAccent(string registryKey)
        {
            Settings.SaveStr(registryKey, "");
            try { ThemeManager.ApplyAccentOverride(Application.Current); } catch { }
            ShowFeedback(Lang.T("set.theme.accent.reset"), "Success");
        }

        /// <summary>重置三模式强调色为默认棉花糖配色。</summary>
        public void ResetAllAccents()
        {
            Settings.SaveStr("AccentStandard", "");
            Settings.SaveStr("AccentCompetitive", "");
            Settings.SaveStr("AccentCustom", "");
            try { ThemeManager.ApplyAccentOverride(Application.Current); } catch { }
            ShowFeedback(Lang.T("set.theme.accent.resetall"), "Success");
        }

        // —— 维护：着色器缓存 ——
        public string ShaderTitle { get { return Lang.T("btn.shader"); } }
        public string ShaderText { get { return Lang.T("btn.clean"); } }
        public string ShaderStatus
        {
            get { return shaderStatus; }
            set { SetProperty(ref shaderStatus, value, "ShaderStatus"); }
        }

        public void ShowFeedback(string text, string kind)
        {
            PageFeedbackKind = string.IsNullOrEmpty(kind) ? "Info" : kind;
            PageFeedback = text ?? "";
        }

        // —— 版本信息 ——
        public string AboutText { get { return Lang.F("set.about", App.VersionTag, Paths.Data ?? ""); } }
    }
}
