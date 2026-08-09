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
    }
}
