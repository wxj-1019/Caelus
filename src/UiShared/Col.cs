// @author zenjiro 18967498922@163.com
// 文件用途 颜色工具（Lerp/Alpha）。最初从旧 WinForms Theme.cs 拆出，
// 现由 WPF 单一宿主使用（IconArt 图标渲染等）

using System.Drawing;

namespace CaelusApp
{
    internal static class Col
    {
        public static Color Lerp(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f; if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
        public static Color Alpha(Color c, int a) { return Color.FromArgb(a < 0 ? 0 : a > 255 ? 255 : a, c.R, c.G, c.B); }
    }
}
