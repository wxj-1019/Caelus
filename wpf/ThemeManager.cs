// @author zenjiro 18967498922@163.com
// 文件用途 WPF 宿主主题切换：替换应用级颜色资源字典

using System;
using System.Windows;

namespace CaelusApp.WpfHost
{
    internal static class ThemeManager
    {
        private static ResourceDictionary colors;

        public static UiTone Current { get; private set; }

        public static void Apply(Application app, UiTone tone)
        {
            string uri = tone == UiTone.Light
                ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml";
            var next = new ResourceDictionary
            {
                Source = new Uri(uri, UriKind.Relative)
            };
            var merged = app.Resources.MergedDictionaries;
            if (colors != null) merged.Remove(colors);
            merged.Add(next);
            colors = next;
            Current = tone;
            Native.LightModeQuery = () => tone == UiTone.Light;
        }
    }
}
