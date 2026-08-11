// @author zenjiro 18967498922@163.com
// 文件用途 系统体检扫描态的圆形进度弧：track 全环 + Arc 弧（Progress 0..1），仿 Sparkline OnRender

using System;
using System.Windows;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Controls
{
    internal sealed class ProgressRing : FrameworkElement
    {
        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            "Progress", typeof(double), typeof(ProgressRing),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Progress
        {
            get { return (double)GetValue(ProgressProperty); }
            set { SetValue(ProgressProperty, value); }
        }

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            "Stroke", typeof(Brush), typeof(ProgressRing),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke
        {
            get { return (Brush)GetValue(StrokeProperty); }
            set { SetValue(StrokeProperty, value); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double size = ActualWidth < ActualHeight ? ActualWidth : ActualHeight;
            if (size < 8) return;
            double thick = 6;
            double r = size / 2 - thick / 2 - 1;
            if (r < 2) return;
            Point c = new Point(ActualWidth / 2, ActualHeight / 2);

            // 轨道整环（取 TrackBrush 令牌；缺失则用透明避免崩）
            Brush track = TryFindResource("TrackBrush") as Brush;
            if (track != null)
            {
                Pen trackPen = new Pen(track, thick);
                trackPen.Freeze();
                dc.DrawGeometry(null, trackPen, new EllipseGeometry(c, r, r));
            }

            // 进度弧：从顶端(-90°)顺时针扫 Progress×360°
            double p = Progress;
            if (p < 0) p = 0; else if (p > 1) p = 1;
            if (p > 0.0001 && Stroke != null)
            {
                double start = -Math.PI / 2;
                double end = start + 2 * Math.PI * p;
                Point sp = PointAt(c, r, start);
                Point ep = PointAt(c, r, end);
                bool largeArc = (end - start) > Math.PI;
                var arc = new StreamGeometry();
                using (StreamGeometryContext ctx = arc.Open())
                {
                    ctx.BeginFigure(sp, false, false);
                    ctx.ArcTo(ep, new Size(r, r), 0, largeArc, SweepDirection.Clockwise, true, false);
                }
                arc.Freeze();
                Pen arcPen = new Pen(Stroke, thick)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                arcPen.Freeze();
                dc.DrawGeometry(null, arcPen, arc);
            }
        }

        private static Point PointAt(Point center, double r, double angle)
        {
            return new Point(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle));
        }
    }
}
