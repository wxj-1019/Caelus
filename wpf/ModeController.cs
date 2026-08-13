// @author zenjiro 18967498922@163.com
// 文件用途 驾驶舱模式切换编排：持久化 + 主题换槽 + 氛围过渡 + 视图刷新

using System.Windows;

namespace CaelusApp.WpfHost
{
    internal static class ModeController
    {
        // 启动时读取持久化模式（与 GameMode.Preset 使用同一 Settings 键；
        // 预览宿主不运行 GameMode，正式宿主接管时改用 gameMode.Preset 赋值）
        public static AppMode LoadPersisted()
        {
            int raw;
            if (int.TryParse(Settings.LoadStr("PerformancePreset", "0"), out raw)
                && raw >= 0 && raw <= 2)
                return ModePalette.FromPreset((PerformancePreset)raw);
            return AppMode.Standard;
        }

        public static void SwitchTo(Application app, AppMode mode,
            SampleOverviewSource source, OverviewViewModel vm)
        {
            Settings.SaveStr("PerformancePreset", ((int)ToPreset(mode)).ToString());
            ThemeManager.Apply(app, ThemeManager.CurrentTone, mode);
            if (source != null) source.SetMode(mode);
            if (vm != null) vm.Refresh();
        }

        public static PerformancePreset ToPreset(AppMode mode)
        {
            if (mode == AppMode.Competitive) return PerformancePreset.Competitive;
            if (mode == AppMode.Custom) return PerformancePreset.Custom;
            return PerformancePreset.Standard;
        }
    }
}
