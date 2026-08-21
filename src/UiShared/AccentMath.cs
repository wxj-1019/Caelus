// @author zenjiro 18967498922@163.com
// 文件用途 强调色派生数学：HSL 转换 / WCAG 对比度 / 衍生色计算
//           纯 System 数学（无 WPF 类型），src/ 和 wpf/ 共用，测试可直接调用

using System;

namespace CaelusApp
{
    internal static class AccentMath
    {
        // —— 解析 ——

        /// <summary>解析 #RRGGBB 或 #RGB，成功返回 true 并输出 0-255 字节。</summary>
        public static bool ParseHex(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            hex = hex.Trim();
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length == 3)
                hex = new string(new char[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            if (hex.Length != 6) return false;
            int ri, gi, bi;
            if (!int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out ri)) return false;
            if (!int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out gi)) return false;
            if (!int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out bi)) return false;
            r = (byte)ri; g = (byte)gi; b = (byte)bi;
            return true;
        }

        // —— HSL 转换（h,s,l ∈ [0,1]）——

        public static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            l = (max + min) / 2.0;
            if (Math.Abs(max - min) < 0.001) { h = 0; s = 0; return; }
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (Math.Abs(max - rd) < 0.001)
                h = (gd - bd) / d + (gd < bd ? 6.0 : 0.0);
            else if (Math.Abs(max - gd) < 0.001)
                h = (bd - rd) / d + 2.0;
            else
                h = (rd - gd) / d + 4.0;
            h /= 6.0;
        }

        public static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
        {
            double rd, gd, bd;
            if (Math.Abs(s) < 0.001) { rd = gd = bd = l; }
            else
            {
                double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
                double p = 2.0 * l - q;
                rd = Hue2Rgb(p, q, h + 1.0 / 3.0);
                gd = Hue2Rgb(p, q, h);
                bd = Hue2Rgb(p, q, h - 1.0 / 3.0);
            }
            r = (byte)Math.Round(rd * 255);
            g = (byte)Math.Round(gd * 255);
            b = (byte)Math.Round(bd * 255);
        }

        private static double Hue2Rgb(double p, double q, double t)
        {
            if (t < 0) t += 1.0;
            if (t > 1) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        // —— WCAG 2.0 相对亮度与对比度 ——

        public static double RelativeLuminance(byte r, byte g, byte b)
        {
            return 0.2126 * Linearize(r / 255.0)
                 + 0.7152 * Linearize(g / 255.0)
                 + 0.0722 * Linearize(b / 255.0);
        }

        private static double Linearize(double c)
        {
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>WCAG 对比度（较大值+0.05）/（较小值+0.05）。</summary>
        public static double ContrastRatio(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
        {
            double la = RelativeLuminance(r1, g1, b1);
            double lb = RelativeLuminance(r2, g2, b2);
            double lighter = Math.Max(la, lb);
            double darker = Math.Min(la, lb);
            return (lighter + 0.05) / (darker + 0.05);
        }

        // —— 衍生色计算 ——

        public static void BrightenHsl(byte r, byte g, byte b, double delta,
            out byte oR, out byte oG, out byte oB)
        {
            double h, s, l;
            RgbToHsl(r, g, b, out h, out s, out l);
            l = Math.Min(1.0, l + delta);
            HslToRgb(h, s, l, out oR, out oG, out oB);
        }

        public static void DarkenHsl(byte r, byte g, byte b, double delta,
            out byte oR, out byte oG, out byte oB)
        {
            double h, s, l;
            RgbToHsl(r, g, b, out h, out s, out l);
            l = Math.Max(0.0, l - delta);
            HslToRgb(h, s, l, out oR, out oG, out oB);
        }

        /// <summary>OnAccent 按钮文字色：对比度取大者（深可可 #2B1F1A vs 白 #FFFFFF）。</summary>
        public static void ChooseOnAccent(byte r, byte g, byte b,
            out byte oR, out byte oG, out byte oB)
        {
            double vsWhite = ContrastRatio(r, g, b, 255, 255, 255);
            double vsDark = ContrastRatio(r, g, b, 0x2B, 0x1F, 0x1A);
            if (vsWhite >= vsDark) { oR = 255; oG = 255; oB = 255; }
            else { oR = 0x2B; oG = 0x1F; oB = 0x1A; }
        }

        // —— 色板预设 ——

        public static readonly string[] PresetColors = new[]
        {
            "#5E5CE6",  // 靛蓝（默认·常规）
            "#FF8A5C",  // 蜜桃橙（默认·竞技）
            "#B8933E",  // 暗金（默认·自定义）
            "#E84C88",  // 品红
            "#3DD68C",  // 湖绿（与 Success 语义色同值，Tooltip 提示）
            "#4C9BE8",  // 天蓝
            "#8B5CF6",  // 紫罗兰
            "#F2555A",  // 珊瑚红（与 Danger 语义色同值，Tooltip 提示）
            "#14B8A6",  // 青碧
            "#64748B",  // 石墨
        };
    }
}
