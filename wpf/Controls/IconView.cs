// @author zenjiro 18967498922@163.com
// 文件用途 几何线性图标宿主：按 key 从主题资源取 StreamGeometry 描边绘制（规格 §4.1）

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Controls
{
    internal sealed class IconView : Control
    {
        public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
            "Key", typeof(string), typeof(IconView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Key
        {
            get { return (string)GetValue(KeyProperty); }
            set { SetValue(KeyProperty, value); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            string key = Key;
            if (key == null) return;
            Geometry geo = Application.Current.TryFindResource(key) as Geometry;
            if (geo == null) return;
            double size = Math.Min(RenderSize.Width, RenderSize.Height);
            if (size <= 0) size = 16;
            dc.PushTransform(new ScaleTransform(size / 24.0, size / 24.0));
            var pen = new Pen(Foreground, 2.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            // DynamicResource-backed theme brushes are not always freezable.
            if (pen.CanFreeze) pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
            dc.Pop();
        }
    }
}
