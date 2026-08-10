// @author zenjiro 18967498922@163.com
// 文件用途 UiShared 表现层逻辑与 WPF 解耦点的自测

using System;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestNativeLightModeHook()
        {
            Func<bool> prev = Native.LightModeQuery;
            try
            {
                Native.LightModeQuery = null;
                Eq(false, Native.QueryLightMode());
                Native.LightModeQuery = () => true;
                Eq(true, Native.QueryLightMode());
                Native.LightModeQuery = () => false;
                Eq(false, Native.QueryLightMode());
            }
            finally { Native.LightModeQuery = prev; }
        }

        private static void TestPaletteCompleteness()
        {
            foreach (UiTone tone in new[] { UiTone.Light, UiTone.Dark })
            {
                ThemeColors c = Palette.For(tone);
                string[] all =
                {
                    c.Success, c.Warning, c.Danger, c.Info, c.Brand,
                    c.Background, c.Surface, c.SurfaceRaised,
                    c.Border, c.BorderSubtle,
                    c.TextPrimary, c.TextSecondary, c.TextTertiary
                };
                foreach (string hex in all)
                {
                    if (String.IsNullOrEmpty(hex)) throw new Exception("empty token in " + tone);
                    Eq(7, hex.Length);
                    Eq('#', hex[0]);
                }
            }
        }

        private static void TestPaletteSemantics()
        {
            // 语义色必须互不相同，且深浅主题的品牌色一致（规格 §3.1.1）
            ThemeColors l = Palette.For(UiTone.Light);
            if (l.Success == l.Warning || l.Warning == l.Danger || l.Danger == l.Info)
                throw new Exception("semantic colors must be distinct");
            Eq(Palette.For(UiTone.Light).Brand, Palette.For(UiTone.Dark).Brand);
            Eq("#D4A847", l.Brand);
        }

        private static void TestPaletteContrast()
        {
            // 正文与背景的对比度至少 4.5:1（WCAG AA 正文标准）
            foreach (UiTone tone in new[] { UiTone.Light, UiTone.Dark })
            {
                ThemeColors c = Palette.For(tone);
                double ratio = Contrast(c.TextPrimary, c.Background);
                if (ratio < 4.5) throw new Exception(tone + " text/background contrast " + ratio.ToString("0.00"));
                double subSurf = Contrast(c.TextSecondary, c.Surface);
                if (subSurf < 4.5) throw new Exception(tone + " secondary/surface contrast " + subSurf.ToString("0.00"));
                double subBg = Contrast(c.TextSecondary, c.Background);
                if (subBg < 4.5) throw new Exception(tone + " secondary/background contrast " + subBg.ToString("0.00"));
                // 三级文字用于占位符等非正文，WCAG AA 允许 3:1（UI 组件/大字号标准）
                double ter = Contrast(c.TextTertiary, c.Surface);
                if (ter < 3.0) throw new Exception(tone + " tertiary/surface contrast " + ter.ToString("0.00"));
            }
        }

        private static double Contrast(string hexA, string hexB)
        {
            double la = RelLum(hexA), lb = RelLum(hexB);
            if (la < lb) { double t = la; la = lb; lb = t; }
            return (la + 0.05) / (lb + 0.05);
        }

        private static double RelLum(string hex)
        {
            double r = Channel(hex.Substring(1, 2));
            double g = Channel(hex.Substring(3, 2));
            double b = Channel(hex.Substring(5, 2));
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double Channel(string hh)
        {
            double v = Convert.ToInt32(hh, 16) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static void TestMotionTokens()
        {
            Eq(250, UiMotion.PageFadeMs);
            Eq(300, UiMotion.CardExpandMs);
            Eq(400, UiMotion.NumberRollMs);
            Eq(200, UiMotion.ToggleMs);
            Eq(250, UiMotion.ModalMs);
            Eq(400, UiMotion.SuccessPopMs);
        }

        private static void TestMotionReducedPolicy()
        {
            Eq(250, UiMotion.Duration(UiMotion.PageFadeMs, false));
            Eq(125, UiMotion.Duration(UiMotion.PageFadeMs, true));
            Eq(true, UiMotion.AllowsOffset(false));
            Eq(false, UiMotion.AllowsOffset(true));
        }

        private sealed class ProbeVm : ViewModelBase
        {
            private int count;
            public int Count
            {
                get { return count; }
                set { SetProperty(ref count, value, "Count"); }
            }
        }

        private static void TestViewModelBase()
        {
            var vm = new ProbeVm();
            var changed = new System.Collections.Generic.List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
            vm.Count = 1;
            vm.Count = 1; // 同值不应重复触发
            vm.Count = 2;
            Eq(2, changed.Count);
            Eq("Count", changed[0]);
            Eq(2, vm.Count);
        }

        private static void TestRelayCommand()
        {
            int runs = 0;
            var can = new RelayCommand(() => runs++, () => false);
            Eq(false, can.CanExecute(null));
            can.Execute(null);
            Eq(0, runs);
            var go = new RelayCommand(() => runs++);
            Eq(true, go.CanExecute(null));
            go.Execute(null);
            Eq(1, runs);
        }

        private static void TestOverviewConclusionRules()
        {
            // 规格 §5.1：守护关闭优先级最高，其次是危险、警告，再区分游戏中/就绪
            Eq(StatusLevel.Off, OverviewStatus.Conclude(false, false, false, false).Level);
            Eq(StatusLevel.Off, OverviewStatus.Conclude(false, true, true, true).Level);
            Eq(StatusLevel.Critical, OverviewStatus.Conclude(true, false, true, true).Level);
            Eq(StatusLevel.Attention, OverviewStatus.Conclude(true, false, true, false).Level);
            Eq(StatusLevel.Optimizing, OverviewStatus.Conclude(true, true, false, false).Level);
            Eq(StatusLevel.Ready, OverviewStatus.Conclude(true, false, false, false).Level);

            Eq("游戏环境已准备好", OverviewStatus.Conclude(true, false, false, false).Title);
            Eq("守护已关闭", OverviewStatus.Conclude(false, false, false, false).Title);
            Eq("游戏优化中", OverviewStatus.Conclude(true, true, false, false).Title);
        }

        private static void TestMetricLevels()
        {
            // GPU 温度阈值 75/85（规格 §5.1）
            Eq(MetricLevel.Ok, OverviewStatus.LevelFor(62, 75, 85));
            Eq(MetricLevel.Warning, OverviewStatus.LevelFor(75, 75, 85));
            Eq(MetricLevel.Warning, OverviewStatus.LevelFor(80, 75, 85));
            Eq(MetricLevel.Critical, OverviewStatus.LevelFor(85, 75, 85));
            Eq(MetricLevel.Critical, OverviewStatus.LevelFor(96, 75, 85));
            // 内存占用阈值 80/90（百分比）
            Eq(MetricLevel.Ok, OverviewStatus.LevelFor(53, 80, 90));
            Eq(MetricLevel.Warning, OverviewStatus.LevelFor(80, 80, 90));
            Eq(MetricLevel.Critical, OverviewStatus.LevelFor(90, 80, 90));
        }

        private static void TestConclusionColorKeys()
        {
            // Level → 语义色 Token 键名（XAML 用 DynamicResource 解析）
            Eq("Success", OverviewStatus.ColorKey(StatusLevel.Ready));
            Eq("Success", OverviewStatus.ColorKey(StatusLevel.Optimizing));
            Eq("Warning", OverviewStatus.ColorKey(StatusLevel.Attention));
            Eq("Warning", OverviewStatus.ColorKey(StatusLevel.Off));
            Eq("Danger", OverviewStatus.ColorKey(StatusLevel.Critical));
        }

        private sealed class StubSource : IOverviewSource
        {
            public bool GuardEnabled = true;
            public bool GameActive;
            public bool HasWarning;
            public bool HasCritical;
            public double? GpuTempC = 62;
            public double? MemoryUsedPct = 53;
            public string MemoryUsedText = "8.4 GB";
            public string ModeText = "常规";
            public string LastCheckText = "上次检查 2 分钟前";

            bool IOverviewSource.GuardEnabled { get { return GuardEnabled; } }
            bool IOverviewSource.GameActive { get { return GameActive; } }
            bool IOverviewSource.HasWarning { get { return HasWarning; } }
            bool IOverviewSource.HasCritical { get { return HasCritical; } }
            double? IOverviewSource.GpuTempC { get { return GpuTempC; } }
            double? IOverviewSource.MemoryUsedPct { get { return MemoryUsedPct; } }
            string IOverviewSource.MemoryUsedText { get { return MemoryUsedText; } }
            string IOverviewSource.ModeText { get { return ModeText; } }
            string IOverviewSource.LastCheckText { get { return LastCheckText; } }
        }

        private static void TestOverviewViewModelMapping()
        {
            var src = new StubSource();
            var vm = new OverviewViewModel(src);
            vm.Refresh();
            Eq("游戏环境已准备好", vm.ConclusionTitle);
            Eq("Success", vm.ConclusionColorKey);
            Eq("常规", vm.ModeText);
            Eq(3, vm.Metrics.Count);
            Eq("GPU 温度", vm.Metrics[0].Label);
            Eq("62°", vm.Metrics[0].ValueText);
            Eq("Success", vm.Metrics[0].ColorKey);
            Eq("8.4 GB", vm.Metrics[2].ValueText);

            src.GuardEnabled = false;
            vm.Refresh();
            Eq("守护已关闭", vm.ConclusionTitle);
            Eq("Warning", vm.ConclusionColorKey);
        }

        private static void TestOverviewViewModelUnavailableMetrics()
        {
            var src = new StubSource();
            src.GpuTempC = null;
            src.MemoryUsedPct = null;
            src.MemoryUsedText = null;
            var vm = new OverviewViewModel(src);
            vm.Refresh();
            Eq("—", vm.Metrics[0].ValueText);
            Eq("Info", vm.Metrics[0].ColorKey);
            Eq("—", vm.Metrics[2].ValueText);
        }

        private static void TestOverviewDetailToggle()
        {
            var vm = new OverviewViewModel(new StubSource());
            Eq(false, vm.DetailVisible);
            vm.ToggleDetailCommand.Execute(null);
            Eq(true, vm.DetailVisible);
            vm.ToggleDetailCommand.Execute(null);
            Eq(false, vm.DetailVisible);
        }

        private static void TestModePaletteCompleteness()
        {
            foreach (AppMode mode in new[] { AppMode.Standard, AppMode.Competitive, AppMode.Custom })
            {
                ModeColors c = ModePalette.For(mode);
                string[] all = { c.AmbientPrimary, c.AmbientSecondary, c.ModeAccentOnDark, c.ModeAccentOnLight };
                foreach (string hex in all)
                {
                    if (String.IsNullOrEmpty(hex)) throw new Exception("empty mode token in " + mode);
                    Eq(7, hex.Length);
                    Eq('#', hex[0]);
                }
            }
            Eq("常规", ModePalette.DisplayName(AppMode.Standard));
            Eq("竞技", ModePalette.DisplayName(AppMode.Competitive));
            Eq("自定义", ModePalette.DisplayName(AppMode.Custom));
            Eq(AppMode.Standard, ModePalette.FromPreset(PerformancePreset.Standard));
            Eq(AppMode.Competitive, ModePalette.FromPreset(PerformancePreset.Competitive));
            Eq(AppMode.Custom, ModePalette.FromPreset(PerformancePreset.Custom));
        }

        private static void TestModePaletteDistinct()
        {
            ModeColors a = ModePalette.For(AppMode.Standard);
            ModeColors b = ModePalette.For(AppMode.Competitive);
            ModeColors c = ModePalette.For(AppMode.Custom);
            if (a.ModeAccentOnDark == b.ModeAccentOnDark || b.ModeAccentOnDark == c.ModeAccentOnDark
                || a.ModeAccentOnDark == c.ModeAccentOnDark)
                throw new Exception("mode accents must be mutually distinct");
            if (a.AmbientPrimary == b.AmbientPrimary || b.AmbientPrimary == c.AmbientPrimary
                || a.AmbientPrimary == c.AmbientPrimary)
                throw new Exception("ambient primaries must be mutually distinct");
            int[] cyan = Rgb(a.AmbientPrimary);
            int[] red = Rgb(b.AmbientPrimary);
            int dist = Math.Abs(cyan[0] - red[0]) + Math.Abs(cyan[1] - red[1]) + Math.Abs(cyan[2] - red[2]);
            if (dist < 200) throw new Exception("cruise/combat ambient too close: " + dist);
        }

        private static void TestModeAccentContrast()
        {
            ThemeColors dark = Palette.For(UiTone.Dark);
            ThemeColors light = Palette.For(UiTone.Light);
            foreach (AppMode mode in new[] { AppMode.Standard, AppMode.Competitive, AppMode.Custom })
            {
                ModeColors c = ModePalette.For(mode);
                double d = Contrast(c.ModeAccentOnDark, dark.Background);
                if (d < 4.5) throw new Exception(mode + " accent/dark-bg contrast " + d.ToString("0.00"));
                double l = Contrast(c.ModeAccentOnLight, light.Background);
                if (l < 4.5) throw new Exception(mode + " accent/light-bg contrast " + l.ToString("0.00"));
            }
        }

        private static int[] Rgb(string hex)
        {
            return new int[]
            {
                Convert.ToInt32(hex.Substring(1, 2), 16),
                Convert.ToInt32(hex.Substring(3, 2), 16),
                Convert.ToInt32(hex.Substring(5, 2), 16)
            };
        }

        private static void TestPolicyItemsCompleteness()
        {
            // 三分组共 21 项
            Eq(9, PolicyViewModel.CoreItems.Count);
            Eq(5, PolicyViewModel.CustomItems.Count);
            Eq(7, PolicyViewModel.ExtraItems.Count);
            // 每项有标题、说明、属性名
            foreach (PolicyItem item in PolicyViewModel.AllItems())
            {
                if (string.IsNullOrEmpty(item.Title)) throw new Exception("empty title: " + item.PropertyName);
                if (string.IsNullOrEmpty(item.Description)) throw new Exception("empty desc: " + item.PropertyName);
                if (string.IsNullOrEmpty(item.PropertyName)) throw new Exception("empty propname");
            }
            // 验证关键文案（非空 = Lang.T 找到了 key）
            Eq("后台调度 · 总开关", PolicyViewModel.CoreItems[0].Title);
            Eq("严格 CPU 分区", PolicyViewModel.CustomItems[0].Title);
            Eq("竞技模式禁用 CPU 空闲状态", PolicyViewModel.ExtraItems[0].Title);
        }
    }
}
