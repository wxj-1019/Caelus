// @author zenjiro 18967498922@163.com
// 文件用途 迷你趋势线：数据点归一化折线 + 描边色 9% 面积淡填充（规格 §4.4）

using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Controls
{
    internal sealed class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            "Values", typeof(IList<double>), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IList<double> Values
        {
            get { return (IList<double>)GetValue(ValuesProperty); }
            set { SetValue(ValuesProperty, value); }
        }

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            "Stroke", typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke
        {
            get { return (Brush)GetValue(StrokeProperty); }
            set { SetValue(StrokeProperty, value); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            IList<double> values = Values;
            if (values == null || values.Count < 2) return;
            double w = RenderSize.Width, h = RenderSize.Height;
            if (w < 4 || h < 4) return;

            double min = double.MaxValue, max = double.MinValue;
            foreach (double v in values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            double range = max - min;
            if (range < 0.001) range = 1;

            // 面积淡填充（描边色取 9% 透明度）
            Brush stroke = Stroke;
            var scb = stroke as SolidColorBrush;
            if (scb != null)
            {
                Color c = scb.Color;
                var area = new StreamGeometry();
                using (StreamGeometryContext ctx = area.Open())
                {
                    ctx.BeginFigure(Pt(values, 0, w, h, min, range), true, true);
                    for (int i = 1; i < values.Count; i++)
                        ctx.LineTo(Pt(values, i, w, h, min, range), true, false);
                    ctx.LineTo(new Point(w, h), true, false);
                    ctx.LineTo(new Point(0, h), true, false);
                }
                area.Freeze();
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(23, c.R, c.G, c.B)), null, area);
            }

            var line = new StreamGeometry();
            using (StreamGeometryContext ctx = line.Open())
            {
                ctx.BeginFigure(Pt(values, 0, w, h, min, range), false, false);
                for (int i = 1; i < values.Count; i++)
                    ctx.LineTo(Pt(values, i, w, h, min, range), true, false);
            }
            line.Freeze();
            if (stroke != null)
            {
                var pen = new Pen(stroke, 1.6) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, line);
            }
        }

        // 数据点 → 画布坐标（留 1.5px 描边余量；框架自带 csc 只认 C# 5，勿用局部函数）
        private static Point Pt(IList<double> values, int i, double w, double h, double min, double range)
        {
            double x = w * i / (values.Count - 1);
            double y = h - 1.5 - (h - 3) * (values[i] - min) / range;
            return new Point(x, y);
        }
    }
}
