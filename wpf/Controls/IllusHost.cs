// @author zenjiro 18967498922@163.com
// 文件用途 空态插画宿主：OnRender 直绘插画（规格 2026-09-29 §5.1 偏差修正）。
// 背景：2026-09-30 真机验收发现插画 Shape 子树（Canvas+Path+Ellipse）在本机 net4+25H2 AOT
// WPF 渲染管线中确定性丢件——盾牌主体与右上圆点消失、勾与左下点纵向位移；资源注入、视图
// 内联、加载期代码构建、首渲后延迟挂载全部同病（同款几何不同位置一好一坏，位置敏感），
// 而 IconView 的 OnRender 直绘路径从未出此问题。故插画弃用保留模式 Shape 子树，改与
// IconView 同族的 DrawingContext 立即直绘；画刷经自定义 DP + SetResourceReference
// （DynamicResource 语义），明暗/模式换肤照常联动。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Controls
{
    internal sealed class IllusHost : Control
    {
        public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
            "Key", typeof(string), typeof(IllusHost),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Key
        {
            get { return (string)GetValue(KeyProperty); }
            set { SetValue(KeyProperty, value); }
        }

        // 主题画刷 DP：构造函数里 SetResourceReference 挂 DynamicResource，AffectsRender 保证换肤重绘
        private static readonly DependencyProperty SoftBrushProperty = BrushDP("SoftBrush");
        private static readonly DependencyProperty PrimaryBrushProperty = BrushDP("PrimaryBrush");
        private static readonly DependencyProperty SecondaryBrushProperty = BrushDP("SecondaryBrush");
        private static readonly DependencyProperty TertiaryBrushProperty = BrushDP("TertiaryBrush");

        private static DependencyProperty BrushDP(string name)
        {
            return DependencyProperty.Register(name, typeof(Brush), typeof(IllusHost),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        }

        private Brush SoftBrush { get { return (Brush)GetValue(SoftBrushProperty); } }
        private Brush PrimaryBrush { get { return (Brush)GetValue(PrimaryBrushProperty); } }
        private Brush SecondaryBrush { get { return (Brush)GetValue(SecondaryBrushProperty); } }
        private Brush TertiaryBrush { get { return (Brush)GetValue(TertiaryBrushProperty); } }

        public IllusHost()
        {
            Width = 96;
            Height = 96;
            HorizontalAlignment = HorizontalAlignment.Center;
            SetResourceReference(SoftBrushProperty, "AccentSoftBrush");
            SetResourceReference(PrimaryBrushProperty, "AccentPrimaryBrush");
            SetResourceReference(SecondaryBrushProperty, "AccentSecondaryBrush");
            SetResourceReference(TertiaryBrushProperty, "TextTertiaryBrush");
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            string key = Key;
            if (key == "Whitelist") DrawWhitelist(dc);
            else if (key == "Library") DrawLibrary(dc);
            else if (key == "AuditWin") DrawAuditWin(dc);
        }

        // —— 直绘小助手 ——

        private static Pen MkPen(Brush b, double thickness, bool roundCap)
        {
            Pen p = new Pen(b, thickness);
            if (roundCap)
            {
                p.StartLineCap = PenLineCap.Round;
                p.EndLineCap = PenLineCap.Round;
            }
            if (p.CanFreeze) p.Freeze();
            return p;
        }

        private static void Geo(DrawingContext dc, double x, double y, string data, Brush fill, Pen pen)
        {
            dc.PushTransform(new TranslateTransform(x, y));
            dc.DrawGeometry(fill, pen, Geometry.Parse(data));
            dc.Pop();
        }

        private static void Dot(DrawingContext dc, Brush fill, double opacity,
            double left, double top, double size)
        {
            dc.PushOpacity(opacity);
            dc.DrawGeometry(fill, null, new EllipseGeometry(
                new Point(left + size / 2, top + size / 2), size / 2, size / 2));
            dc.Pop();
        }

        // 白名单空态：圆盾 + 勾（原 Illustrations.xaml IllusWhitelist 逐笔移植）
        private void DrawWhitelist(DrawingContext dc)
        {
            Geo(dc, 24, 10, "M24,0 L44,8 V26 C44,40 36,50 24,56 C12,50 4,40 4,26 V8 Z",
                SoftBrush, MkPen(PrimaryBrush, 2.5, false));
            Geo(dc, 24, 10, "M15,27 L22,34 L34,20", null, MkPen(PrimaryBrush, 3, true));
            Dot(dc, SecondaryBrush, 0.5, 70, 18, 8);
            Dot(dc, SecondaryBrush, 0.4, 16, 70, 6);
        }

        // 游戏库空态：棉花糖盒子 + 飘出的圆点（原 IllusLibrary 逐笔移植）
        private void DrawLibrary(DrawingContext dc)
        {
            Geo(dc, 18, 34, "M0,10 H60 A6,6 0 0 1 66,16 V52 A6,6 0 0 1 60,58 H6 A6,6 0 0 1 0,52 V16 A6,6 0 0 1 6,10 Z",
                SoftBrush, MkPen(PrimaryBrush, 2.5, false));
            Geo(dc, 18, 34, "M0,22 H66", null, MkPen(PrimaryBrush, 2.5, true));
            Dot(dc, PrimaryBrush, 0.85, 30, 12, 12);
            Dot(dc, SecondaryBrush, 0.6, 50, 4, 8);
            Dot(dc, SecondaryBrush, 0.45, 64, 16, 6);
            Geo(dc, 36, 66, "M0,4 Q12,14 24,4", null, MkPen(TertiaryBrush, 2.5, true));
        }

        // 体检达标：奖章 + 星星（原 IllusAuditWin 逐笔移植）
        private void DrawAuditWin(DrawingContext dc)
        {
            dc.DrawGeometry(SoftBrush, MkPen(PrimaryBrush, 2.5, false),
                new EllipseGeometry(new Point(48, 28), 20, 20));
            Geo(dc, 40, 19, "M8,0 L10.5,5.5 L16,6 L12,10 L13,16 L8,13 L3,16 L4,10 L0,6 L5.5,5.5 Z",
                PrimaryBrush, null);
            Geo(dc, 36, 46, "M4,0 L0,18 M20,0 L24,18", null, MkPen(PrimaryBrush, 2.5, true));
            Dot(dc, SecondaryBrush, 0.5, 14, 30, 6);
            Dot(dc, SecondaryBrush, 0.4, 76, 24, 8);
        }
    }
}
