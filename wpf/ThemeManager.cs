// @author zenjiro 18967498922@163.com
// 文件用途 WPF 宿主主题切换：双轴（明暗 tone × 飞行模式 mode）四槽资源字典

using System;
using System.IO;
using System.Windows;

namespace CaelusApp.WpfHost
{
    internal static class ThemeManager
    {
        private static ResourceDictionary colors;
        private static ResourceDictionary mode;
        private static ResourceDictionary user;

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

            // 规格 §3.4：用户主题必须始终保持最高优先级（最后=覆盖三模式预设）；
            // Apply 重排 colors/mode 后需重新把 user 提升到末尾
            if (user != null)
            {
                merged.Remove(user);
                merged.Add(user);
            }

            Native.LightModeQuery = () => tone == UiTone.Light;
        }

        private static string modeUriFor(AppMode appMode)
        {
            if (appMode == AppMode.Competitive) return "Themes/Mode.Competitive.xaml";
            if (appMode == AppMode.Custom) return "Themes/Mode.Custom.xaml";
            return "Themes/Mode.Standard.xaml";
        }

        // 规格 §3.4：应用目录 Caelus.theme.xaml 通过模式档契约校验则并入覆盖层；
        // 缺 key 或解析失败记日志忽略，绝不影响启动
        public static void TryApplyUserTheme(Application app)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caelus.theme.xaml");
                if (!File.Exists(path)) return;
                string[] missing = ThemeContract.MissingKeys(File.ReadAllText(path), ThemeContract.ModeKeys);
                if (missing.Length > 0)
                {
                    LogUserTheme("用户主题缺 key 已忽略：" + string.Join("、", missing));
                    return;
                }
                using (FileStream fs = File.OpenRead(path))
                {
                    var dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(fs);
                    app.Resources.MergedDictionaries.Add(dict);
                    user = dict;
                }
            }
            catch (Exception ex)
            {
                LogUserTheme("用户主题加载失败：" + ex.Message);
            }
        }

        private static void LogUserTheme(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "CaelusWpf.crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
