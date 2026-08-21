// @author zenjiro 18967498922@163.com
// 文件用途 WPF 宿主主题切换：双轴（明暗 tone × 飞行模式 mode）四槽资源字典
//           + 用户强调色覆盖层（第五槽，运行时构造）

using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CaelusApp.WpfHost
{
    internal static class ThemeManager
    {
        private static ResourceDictionary colors;
        private static ResourceDictionary mode;
        private static ResourceDictionary user;
        private static ResourceDictionary accessibility;
        private static ResourceDictionary overrideAccent;
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

            // 用户强调色覆盖层：mode 之后加入（空值=用预设，不叠加）
            if (overrideAccent != null) merged.Remove(overrideAccent);
            ResourceDictionary nextOverride = AccentOverrideForMode(appMode);
            overrideAccent = nextOverride;
            if (overrideAccent != null) merged.Add(overrideAccent);

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

        /// <summary>设置页即时预览：重建当前模式的强调色覆盖层，不影响 colors/mode/user/accessibility。</summary>
        public static void ApplyAccentOverride(Application app)
        {
            var merged = app.Resources.MergedDictionaries;
            if (overrideAccent != null) merged.Remove(overrideAccent);
            ResourceDictionary nextOverride = AccentOverrideForMode(CurrentMode);
            overrideAccent = nextOverride;
            if (overrideAccent == null) return;
            // 插入在 user 之前；user 不存在则在 accessibility 之前；均不存在则在末尾
            int userIndex = user != null ? merged.IndexOf(user) : -1;
            if (userIndex >= 0)
                merged.Insert(userIndex, overrideAccent);
            else
            {
                int accIndex = accessibility != null ? merged.IndexOf(accessibility) : -1;
                if (accIndex >= 0)
                    merged.Insert(accIndex, overrideAccent);
                else
                    merged.Add(overrideAccent);
            }
            // DynamicResource 自动感知字典变更；触发 ModeChanged 让订阅者同步
            try { var h = ModeChanged; if (h != null) h(null, EventArgs.Empty); } catch { }
        }

        // —— 跟随系统深浅监控（WM_SETTINGCHANGE） ——

        /// <summary>启动时解析深浅主题：UiToneMode 优先，回退旧 UiLight。</summary>
        public static UiTone ResolveTone()
        {
            int toneMode;
            if (!int.TryParse(Settings.LoadStr("UiToneMode", "-1"), out toneMode)) toneMode = -1;
            if (toneMode == 2) return ProbeSystemTheme();
            if (toneMode == 1) return UiTone.Light;
            if (toneMode == 0) return UiTone.Dark;
            // 兼容旧键 UiLight
            return Settings.Load("UiLight", false) ? UiTone.Light : UiTone.Dark;
        }

        private static HwndSource systemThemeSource;
        private static Application systemThemeApp;
        private static bool systemThemeDebouncing;

        /// <summary>MainWindow.OnSourceInitialized 调用：挂 WM_SETTINGCHANGE hook 监听系统主题变化。</summary>
        public static void StartSystemThemeMonitor(Application app, IntPtr hwnd)
        {
            systemThemeApp = app;
            if (systemThemeSource != null) return;
            systemThemeSource = HwndSource.FromHwnd(hwnd);
            if (systemThemeSource != null)
                systemThemeSource.AddHook(SystemThemeHook);
        }

        public static void StopSystemThemeMonitor()
        {
            if (systemThemeSource != null)
            {
                systemThemeSource.RemoveHook(SystemThemeHook);
                systemThemeSource = null;
            }
            systemThemeApp = null;
        }

        private static IntPtr SystemThemeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != 0x001A) return IntPtr.Zero; // WM_SETTINGCHANGE
            // 仅跟随系统模式时响应
            int toneMode;
            if (!int.TryParse(Settings.LoadStr("UiToneMode", "-1"), out toneMode)) toneMode = -1;
            if (toneMode != 2)
            {
                // 兼容旧键：UiToneMode 未设置 + UiLight 未设 → 默认不跟随
                if (toneMode >= 0) return IntPtr.Zero;
                return IntPtr.Zero;
            }
            // 去抖 500ms：连续 WM_SETTINGCHANGE 只响应最后一次
            if (systemThemeDebouncing) return IntPtr.Zero;
            systemThemeDebouncing = true;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            timer.Tick += delegate
            {
                timer.Stop();
                systemThemeDebouncing = false;
                try
                {
                    UiTone systemTone = ProbeSystemTheme();
                    if (systemTone != CurrentTone && systemThemeApp != null)
                        Apply(systemThemeApp, systemTone, CurrentMode);
                }
                catch { }
            };
            timer.Start();
            return IntPtr.Zero;
        }

        /// <summary>读取 Windows 个性化注册表判断当前系统深浅主题。</summary>
        public static UiTone ProbeSystemTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val is int) return (int)val == 1 ? UiTone.Light : UiTone.Dark;
                    }
                }
            }
            catch { }
            return UiTone.Dark;
        }

        private static ResourceDictionary AccentOverrideForMode(AppMode appMode)
        {
            string key;
            switch (appMode)
            {
                case AppMode.Competitive: key = "AccentCompetitive"; break;
                case AppMode.Custom: key = "AccentCustom"; break;
                default: key = "AccentStandard"; break;
            }
            string hex = Settings.LoadStr(key, "");
            if (string.IsNullOrEmpty(hex)) return null;
            return BuildAccentOverride(hex);
        }

        private static ResourceDictionary BuildAccentOverride(string hex)
        {
            byte pr, pg, pb;
            if (!AccentMath.ParseHex(hex, out pr, out pg, out pb)) return null;
            var dict = new ResourceDictionary();
            Color primary = Color.FromRgb(pr, pg, pb);

            // 强调色 Secondary = HSL L+12%
            byte sr, sg, sb;
            AccentMath.BrightenHsl(pr, pg, pb, 0.12, out sr, out sg, out sb);
            Color secondary = Color.FromRgb(sr, sg, sb);
            dict["AccentPrimaryColor"] = primary;
            dict["AccentPrimaryBrush"] = new SolidColorBrush(primary);
            dict["AccentSecondaryColor"] = secondary;
            dict["AccentSecondaryBrush"] = new SolidColorBrush(secondary);

            // 渐变
            dict["AccentGradientBrush"] = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(primary, 0),
                    new GradientStop(secondary, 1)
                },
                new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 0));

            // 柔色/描边/Glow
            dict["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(0x16, pr, pg, pb));
            dict["AccentEdgeBrush"] = new SolidColorBrush(Color.FromArgb(0x44, pr, pg, pb));
            dict["AccentGlowColor"] = primary;

            // ModeAccent 明暗变体
            byte dr, dg, db, lr, lg, lb;
            AccentMath.BrightenHsl(pr, pg, pb, 0.18, out dr, out dg, out db);
            AccentMath.DarkenHsl(pr, pg, pb, 0.25, out lr, out lg, out lb);
            Color darkVar = Color.FromRgb(dr, dg, db);
            Color lightVar = Color.FromRgb(lr, lg, lb);
            dict["ModeAccentOnDarkColor"] = darkVar;
            dict["ModeAccentOnDarkBrush"] = new SolidColorBrush(darkVar);
            dict["ModeAccentOnLightColor"] = lightVar;
            dict["ModeAccentOnLightBrush"] = new SolidColorBrush(lightVar);
            dict["HeroTitleOnDarkBrush"] = new SolidColorBrush(darkVar);
            dict["HeroTitleOnLightBrush"] = new SolidColorBrush(lightVar);

            // OnAccent 按钮文字色：对比度取大者（§10.1 修正）
            byte oR, oG, oB;
            AccentMath.ChooseOnAccent(pr, pg, pb, out oR, out oG, out oB);
            dict["OnAccentBrush"] = new SolidColorBrush(Color.FromRgb(oR, oG, oB));

            // Aurora 三档（用户色 +12% +24%）+ FadeColor（alpha 0）
            byte ar, ag, ab;
            AccentMath.BrightenHsl(pr, pg, pb, 0.24, out ar, out ag, out ab);
            Color aurora3 = Color.FromRgb(ar, ag, ab);
            dict["AuroraPrimaryColor"] = primary;
            dict["AuroraPrimaryFadeColor"] = Color.FromArgb(0, pr, pg, pb);
            dict["AuroraSecondaryColor"] = secondary;
            dict["AuroraSecondaryFadeColor"] = Color.FromArgb(0, sr, sg, sb);
            dict["AuroraTertiaryColor"] = aurora3;
            dict["AuroraTertiaryFadeColor"] = Color.FromArgb(0, ar, ag, ab);

            return dict;
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
