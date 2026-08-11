// @author zenjiro 18967498922@163.com
// 文件用途 WPF 设置页 ViewModel：应用偏好开关状态 + 维护工具文案

using System.Threading;
using System.Windows;
using CaelusApp.WpfHost;

namespace CaelusApp
{
    internal sealed class SettingsViewModel : ViewModelBase
    {
        private readonly GameMode gameMode;
        private readonly Tamer tamer;
        private bool autoStart;
        private bool autoHide;
        private bool lightMode;
        private bool devMode;
        private string shaderStatus;
        private int restoreBusy; // 0=空闲 1=进行中（Interlocked 守护）

        public SettingsViewModel(GameMode gameMode, Tamer tamer)
        {
            this.gameMode = gameMode;
            this.tamer = tamer;
            autoStart = TaskHelper.TaskExistsCached();
            autoHide = Settings.Load("AutoHideOnGame", false);
            lightMode = Settings.Load("UiLight", false);
            devMode = Settings.Load("DevModeOn", true);
            shaderStatus = Lang.T("set.shader.n");
        }

        // —— 分组标题 ——
        public string AppSectionTitle { get { return Lang.T("sec.app"); } }
        public string MaintSectionTitle { get { return Lang.T("sec.maint"); } }
        internal GameMode GameMode { get { return gameMode; } }

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
                    // 回滚到真实状态
                    autoStart = TaskHelper.TaskExists();
                    Raise("AutoStart");
                }
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
            }
        }

        // —— 自定义编译进程 ——
        public string DevCustomTitle { get { return Lang.T("set.dev.custom"); } }
        public string DevCustomNote { get { return Lang.T("set.dev.custom.n"); } }
        public string DevCustomSaveText { get { return Lang.T("set.dev.custom.save"); } }
        public string DevCustomInitial { get { return BuildCatalog.CustomList; } }
        public void SaveDevCustom(string text) { BuildCatalog.CustomList = text ?? ""; }

        // —— 维护：一键恢复 ——
        public string RestoreTitle { get { return Lang.T("v15.restore.title"); } }
        public string RestoreDesc { get { return Lang.T("v15.restore.desc"); } }
        public string RestoreText { get { return Lang.T("btn.panic"); } }
        public bool IsRestoreBusy
        {
            get { return Interlocked.CompareExchange(ref restoreBusy, 0, 0) != 0; }
            set { Interlocked.Exchange(ref restoreBusy, value ? 1 : 0); }
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

        // —— 维护：着色器缓存 ——
        public string ShaderTitle { get { return Lang.T("btn.shader"); } }
        public string ShaderNote { get { return Lang.T("set.shader.n"); } }
        public string ShaderText { get { return Lang.T("btn.clean"); } }
        public string ShaderStatus
        {
            get { return shaderStatus; }
            set { SetProperty(ref shaderStatus, value, "ShaderStatus"); }
        }

        // —— 版本信息 ——
        public string AboutText { get { return Lang.F("set.about", App.VersionTag, Paths.Data ?? ""); } }
    }
}
