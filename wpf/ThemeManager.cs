// @author zenjiro 18967498922@163.com
// 文件用途 WPF 宿主主题切换：双轴（明暗 tone × 飞行模式 mode）四槽资源字典

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CaelusApp.WpfHost
{
    internal static class ThemeManager
    {
        private static ResourceDictionary colors;
        private static ResourceDictionary mode;
        private static ResourceDictionary user;
        private static ResourceDictionary accessibility;
        private static readonly System.Collections.Generic.Dictionary<string, ResourceDictionary> cache =
            new System.Collections.Generic.Dictionary<string, ResourceDictionary>(StringComparer.OrdinalIgnoreCase);

        public static UiTone CurrentTone { get; private set; }
        public static AppMode CurrentMode { get; private set; }

        // 模式（或明暗）换槽完成事件：CaelusCore 等随模式换肤的控件订阅。
        // 注意：这是静态事件，会强引用订阅者实例——订阅者必须在 Unloaded 时取消订阅，
        // 否则控件无法被 GC（每次导航换页泄漏一份）。
        public static event EventHandler ModeChanged;

        public static void Apply(Application app, UiTone tone, AppMode appMode)
        {
            var merged = app.Resources.MergedDictionaries;

            string colorsUri = tone == UiTone.Light
                ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml";
            ResourceDictionary nextColors = DictionaryFor(colorsUri);
            if (colors != null) merged.Remove(colors);
            merged.Add(nextColors);
            colors = nextColors;
            CurrentTone = tone;

            string modeUri = modeUriFor(appMode);
            ResourceDictionary nextMode = DictionaryFor(modeUri);
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
            ApplyAccessibilityOverlay(merged);

            // 换槽完成：通知订阅者（CaelusCore 等随模式换肤控件）。user 已重新提升，
            // 订阅者看到的资源状态是最终态。
            var handler = ModeChanged;
            if (handler != null) handler(null, EventArgs.Empty);

            Native.LightModeQuery = () => tone == UiTone.Light;
        }

        private static ResourceDictionary DictionaryFor(string uri)
        {
            ResourceDictionary dictionary;
            if (cache.TryGetValue(uri, out dictionary)) return dictionary;
            dictionary = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };
            cache[uri] = dictionary;
            return dictionary;
        }

        private static void ApplyAccessibilityOverlay(System.Collections.ObjectModel.Collection<ResourceDictionary> merged)
        {
            if (accessibility != null) merged.Remove(accessibility);
            accessibility = null;
            if (!SystemParameters.HighContrast) return;

            var overlay = new ResourceDictionary();
            overlay["BackgroundBrush"] = SystemColors.WindowBrush;
            overlay["SurfaceBrush"] = SystemColors.ControlBrush;
            overlay["Surface0Brush"] = SystemColors.ControlBrush;
            overlay["Surface1Brush"] = SystemColors.ControlBrush;
            overlay["Surface2Brush"] = SystemColors.ControlDarkBrush;
            overlay["TextPrimaryBrush"] = SystemColors.WindowTextBrush;
            overlay["TextSecondaryBrush"] = SystemColors.WindowTextBrush;
            overlay["TextTertiaryBrush"] = SystemColors.GrayTextBrush;
            overlay["BorderSubtleBrush"] = SystemColors.ControlTextBrush;
            overlay["BorderStrongBrush"] = SystemColors.ControlTextBrush;
            overlay["CardEdgeBrush"] = SystemColors.ControlTextBrush;
            overlay["TopHighlightBrush"] = System.Windows.Media.Brushes.Transparent;
            overlay["GlassNavBrush"] = SystemColors.ControlBrush;
            overlay["ModeAccentBrush"] = SystemColors.HighlightBrush;
            overlay["AccentGradientBrush"] = SystemColors.HighlightBrush;
            overlay["AccentPrimaryBrush"] = SystemColors.HighlightBrush;
            overlay["AccentSecondaryBrush"] = SystemColors.HighlightBrush;
            overlay["AccentGlowColor"] = System.Windows.Media.Colors.Transparent;
            overlay["ButtonSheenColor"] = System.Windows.Media.Colors.Transparent;
            overlay["AccentSoftBrush"] = SystemColors.HighlightBrush;
            overlay["AccentEdgeBrush"] = SystemColors.HighlightTextBrush;
            overlay["OnAccentBrush"] = SystemColors.HighlightTextBrush;
            overlay["SuccessBrush"] = SystemColors.WindowTextBrush;
            overlay["WarningBrush"] = SystemColors.WindowTextBrush;
            overlay["DangerBrush"] = SystemColors.WindowTextBrush;
            overlay["InfoBrush"] = SystemColors.WindowTextBrush;
            overlay["SuccessSoftBrush"] = SystemColors.ControlBrush;
            overlay["SuccessEdgeBrush"] = SystemColors.ControlTextBrush;
            overlay["WarningSoftBrush"] = SystemColors.ControlBrush;
            overlay["WarningEdgeBrush"] = SystemColors.ControlTextBrush;
            overlay["DangerSoftBrush"] = SystemColors.ControlBrush;
            overlay["DangerEdgeBrush"] = SystemColors.ControlTextBrush;
            overlay["InfoSoftBrush"] = SystemColors.ControlBrush;
            overlay["InfoEdgeBrush"] = SystemColors.ControlTextBrush;
            overlay["NeutralSoftBrush"] = SystemColors.ControlBrush;
            overlay["NeutralEdgeBrush"] = SystemColors.ControlTextBrush;
            overlay["TrackBrush"] = SystemColors.ControlDarkBrush;
            overlay["ScrollTrackBrush"] = SystemColors.ControlBrush;
            overlay["ScrollThumbBrush"] = SystemColors.ControlTextBrush;
            overlay["ScrollThumbHoverBrush"] = SystemColors.HighlightBrush;
            overlay["SegSelectedBrush"] = SystemColors.HighlightBrush;
            overlay["SegSelectedTextBrush"] = SystemColors.HighlightTextBrush;
            overlay["HeroTitleOnDarkBrush"] = SystemColors.WindowTextBrush;
            overlay["HeroTitleOnLightBrush"] = SystemColors.WindowTextBrush;
            overlay["AuroraPrimaryOpacity"] = 0.0;
            overlay["AuroraSecondaryOpacity"] = 0.0;
            overlay["AuroraTertiaryOpacity"] = 0.0;
            merged.Add(overlay);
            accessibility = overlay;
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
