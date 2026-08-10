// @author zenjiro 18967498922@163.com
// 文件用途 WPF 宿主主题切换：双轴（明暗 tone × 飞行模式 mode）四槽资源字典

using System;
using System.Windows;

namespace CaelusApp.WpfHost
{
    internal static class ThemeManager
    {
        private static ResourceDictionary colors;
        private static ResourceDictionary mode;

        public static UiTone CurrentTone { get; private set; }
        public static AppMode CurrentMode { get; private set; }

        public static void Apply(Application app, UiTone tone, AppMode appMode)
        {
            var merged = app.Resources.MergedDictionaries;

            string colorsUri = tone == UiTone.Light
                ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml";
            var nextColors = new ResourceDictionary
            {
                Source = new Uri(colorsUri, UriKind.Relative)
            };
            if (colors != null) merged.Remove(colors);
            merged.Add(nextColors);
            colors = nextColors;
            CurrentTone = tone;

            string modeUri = modeUriFor(appMode);
            var nextMode = new ResourceDictionary
            {
                Source = new Uri(modeUri, UriKind.Relative)
            };
            if (mode != null) merged.Remove(mode);
            merged.Add(nextMode);
            mode = nextMode;
            CurrentMode = appMode;

            Native.LightModeQuery = () => tone == UiTone.Light;
        }

        private static string modeUriFor(AppMode appMode)
        {
            if (appMode == AppMode.Competitive) return "Themes/Mode.Competitive.xaml";
            if (appMode == AppMode.Custom) return "Themes/Mode.Custom.xaml";
            return "Themes/Mode.Standard.xaml";
        }
    }
}
