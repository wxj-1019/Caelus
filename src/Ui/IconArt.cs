// @author zenjiro 18967498922@163.com
// 文件用途 生成程序图标和托盘图像

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class IconArt
    {
        private static readonly Color Ink   = Color.FromArgb(24, 26, 32);
        private static readonly Color Rim   = Color.FromArgb(40, 255, 255, 255);

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

        public static Icon MakeIcon(int px)
        {
            return MakeIcon(px, PerformancePreset.Standard, true);
        }

        public static Icon MakeIcon(int px, PerformancePreset mode, bool enabled)
        {
            using (var bmp = Render(px, mode, enabled))
            {
                IntPtr hIcon = bmp.GetHicon();
                try { using (var tmp = Icon.FromHandle(hIcon)) return (Icon)tmp.Clone(); }
                finally { DestroyIcon(hIcon); }
            }
        }

        public static Icon MakeMultiIcon()
        {
            return MakeMultiIcon(PerformancePreset.Standard, true);
        }

        public static Icon MakeMultiIcon(PerformancePreset mode, bool enabled)
        {
            byte[] data = IcoWriter.Build(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }, mode, enabled);
            using (var ms = new MemoryStream(data)) return new Icon(ms);
        }

        public static Bitmap Render(int px)
        {
            return Render(px, PerformancePreset.Standard, true);
        }

        public static Bitmap Render(int px, PerformancePreset mode, bool enabled)
        {
            var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                g.ScaleTransform(px / 100f, px / 100f);

                Color modeColor = Theme.ModeColor(mode);
                Color bolt = enabled ? modeColor : Col.Lerp(modeColor, Color.FromArgb(105, 110, 121), 0.58f);
                Color rim = enabled ? Col.Alpha(bolt, 105) : Rim;
                using (var badge = Squircle(2.5f, 2.5f, 95, 95, 25))
                {
                    using (var b = new SolidBrush(Ink)) g.FillPath(b, badge);
                    using (var pen = new Pen(rim, 1.8f)) g.DrawPath(pen, badge);
                }
                // 弯月：填充外圆，但用 SetClip 排除偏移的内圆，挖出月牙
                GraphicsState gs = g.Save();
                using (var cutout = new GraphicsPath())
                {
                    cutout.AddEllipse(CrescentCutout());
                    g.SetClip(cutout, CombineMode.Exclude);
                }
                using (var b = new SolidBrush(bolt)) g.FillPath(b, CrescentPath());
                g.Restore(gs);
                Color starColor = enabled ? Col.Lerp(bolt, Color.White, 0.15f) : bolt;
                using (var b = new SolidBrush(starColor)) g.FillPath(b, StarPath(66, 62, 8));
            }
            return bmp;
        }

        private static GraphicsPath Squircle(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath(); float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // 弯月：用裁剪实现——先填充外圆，再挖掉偏移的内圆区域。
        // 返回外圆路径；调用方用 SetClip 排除内圆后填充。
        private static GraphicsPath CrescentPath()
        {
            float ox = 42, oy = 48, or = 26;
            var p = new GraphicsPath();
            p.AddEllipse(ox - or, oy - or, or * 2, or * 2);
            return p;
        }

        // 弯月挖洞用的内圆（向右上偏移，挖出左下开口的月牙）
        private static RectangleF CrescentCutout()
        {
            float ix = 51, iy = 43, ir = 23;
            return new RectangleF(ix - ir, iy - ir, ir * 2, ir * 2);
        }

        // 四角星：4 个外尖点 + 4 个内凹点交替，cx/cy 为中心，r 为外半径
        private static GraphicsPath StarPath(float cx, float cy, float r)
        {
            float inner = r * 0.38f;
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                float angle = (float)(Math.PI / 4 * i - Math.PI / 2);
                float rad = (i % 2 == 0) ? r : inner;
                pts[i] = new PointF(cx + rad * (float)Math.Cos(angle), cy + rad * (float)Math.Sin(angle));
            }
            var p = new GraphicsPath();
            p.AddLines(pts);
            p.CloseFigure();
            return p;
        }
    }

    internal static class IcoWriter
    {
        public static byte[] Build(int[] sizes)
        {
            return Build(sizes, PerformancePreset.Standard, true);
        }

        public static byte[] Build(int[] sizes, PerformancePreset mode, bool enabled)
        {
            var frames = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = IconArt.Render(s, mode, enabled))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }

            using (var outMs = new MemoryStream())
            using (var w = new BinaryWriter(outMs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count);
                int offset = 6 + 16 * frames.Count;
                for (int i = 0; i < frames.Count; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length);
                    w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) w.Write(f);
                w.Flush();
                return outMs.ToArray();
            }
        }

        public static void Save(string path, int[] sizes) { File.WriteAllBytes(path, Build(sizes)); }
    }

}
