// @author zenjiro 18967498922@163.com
// 文件用途 WPF 主题化消息弹窗：无边框模态小窗（规格 2026-08-23 §2/§3）

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CaelusApp.WpfHost.Dialogs
{
    internal partial class MessageDialogWpf : Window
    {
        private MsgButtonSet buttons;
        private MessageBoxResult chosen;
        private MessageBoxResult enterResult;
        private bool choseExplicitly;
        private bool closing;
        private MaskAdorner mask;
        // 遮罩挂载目标：owner 被判定不可见/离屏时为 null，与 CenterScreen 回落保持一致
        private Window maskOwner;

        private MessageDialogWpf(Window owner, string title, string body,
            MsgSeverity severity, MsgButtons buttonSet, string detail, string okText,
            MessageBoxResult defaultResult)
        {
            InitializeComponent();
            buttons = MsgDialogMaps.ResolveButtons(buttonSet, okText);
            System.Windows.Automation.AutomationProperties.SetHelpText(this, severity.ToString());

            Brush sevBrush = TryFindResource(MsgDialogMaps.BrushKey(severity)) as Brush;
            if (sevBrush == null) sevBrush = Brushes.White;
            SeverityIcon.Key = MsgDialogMaps.IconKey(severity);
            SeverityIcon.Foreground = sevBrush;
            Brush edge = TryFindResource(MsgDialogMaps.EdgeKey(severity)) as Brush;
            if (edge != null) Card.BorderBrush = edge;
            Glow.Fill = BuildGlowBrush(sevBrush);

            LblTitle.Text = title;
            if (string.IsNullOrEmpty(body)) BodyScroll.Visibility = Visibility.Collapsed;
            else LblBody.Text = body;

            if (string.IsNullOrEmpty(detail)) BtnDetail.Visibility = Visibility.Collapsed;
            else LblDetailText.Text = detail;

            Style primaryStyle = TryFindResource(MsgDialogMaps.PrimaryStyleKey(severity)) as Style;
            if (primaryStyle != null) BtnPrimary.Style = primaryStyle;
            BtnPrimary.Content = buttons.PrimaryText;
            if (buttons.HasSecondary)
            {
                BtnSecondary.Visibility = Visibility.Visible;
                BtnSecondary.Content = buttons.SecondaryText;
                System.Windows.Automation.AutomationProperties.SetName(BtnSecondary, buttons.SecondaryText);
            }
            else
            {
                Grid.SetColumn(BtnPrimary, 0);
                Grid.SetColumnSpan(BtnPrimary, 2);
                BtnPrimary.Margin = new Thickness(0);
            }

            // ViewModel / 策略页等无 Window 调用方：回落主窗口，恢复模态 / 遮罩 / CenterOwner
            if (owner == null && System.Windows.Application.Current != null)
                owner = System.Windows.Application.Current.MainWindow;

            // owner 隐藏在托盘、或其坐标落在已断开的显示器上时，CenterOwner 会把模态框
            // 定位到不可见位置（表现为"假死"）——这两种情况都回落屏幕居中，
            // 且遮罩也不挂到已判定不可用的 owner 上
            bool useOwner = owner != null && owner.IsLoaded && VisibleOnScreen(owner);
            if (useOwner) Owner = owner;
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            maskOwner = useOwner ? owner : null;

            Loaded += delegate
            {
                ScaleTransform st = new ScaleTransform(0.96, 0.96);
                Card.RenderTransform = st;
                Card.RenderTransformOrigin = new Point(0.5, 0.5);
                DoubleAnimation grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180));
                grow.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                Motion.BreathPulse(Glow); // 内部已 8fps 节流
                mask = AttachMask(maskOwner);
                FocusDefault(defaultResult);
            };
            Closed += delegate
            {
                if (!choseExplicitly) chosen = buttons.EscResult;
                DetachMask();
            };
        }

        // 光晕：严重级色 18% → 70% 处透明的径向渐变
        private Brush BuildGlowBrush(Brush source)
        {
            SolidColorBrush solid = source as SolidColorBrush;
            if (solid == null) solid = Brushes.White;
            RadialGradientBrush radial = new RadialGradientBrush();
            radial.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x2E, solid.Color.R, solid.Color.G, solid.Color.B), 0.0));
            radial.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.7));
            if (radial.CanFreeze) radial.Freeze();
            return radial;
        }

        private static bool VisibleOnScreen(Window w)
        {
            if (w.Visibility != Visibility.Visible) return false;
            try
            {
                double x = w.Left, y = w.Top;
                if (double.IsNaN(x) || double.IsNaN(y)) return true;
                var virtualScreen = new Rect(
                    SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                return virtualScreen.Contains(x, y);
            }
            catch { return true; }
        }

        private void FocusDefault(MessageBoxResult defaultResult)
        {
            if (buttons.HasSecondary && defaultResult == buttons.SecondaryResult)
            {
                BtnSecondary.Focus();
                enterResult = buttons.SecondaryResult;
            }
            else
            {
                BtnPrimary.Focus();
                enterResult = buttons.PrimaryResult;
            }
        }

        private MaskAdorner AttachMask(Window owner)
        {
            if (owner == null || owner.Visibility != Visibility.Visible) return null;
            UIElement adornable = owner.Content as UIElement;
            if (adornable == null) return null;
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(adornable);
            if (layer == null) return null;
            MaskAdorner a = new MaskAdorner(adornable);
            layer.Add(a);
            return a;
        }

        private void DetachMask()
        {
            if (mask == null) return;
            UIElement target = mask.AdornedElement;
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(target);
            if (layer != null) layer.Remove(mask);
            mask = null;
        }

        private void OnPrimaryClick(object sender, RoutedEventArgs e) { CloseWith(buttons.PrimaryResult); }
        private void OnSecondaryClick(object sender, RoutedEventArgs e) { CloseWith(buttons.SecondaryResult); }

        private void CloseWith(MessageBoxResult r)
        {
            if (closing) return;
            closing = true;
            choseExplicitly = true;
            chosen = r;
            DoubleAnimation fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += delegate { Close(); };
            Card.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private bool detailOpen;
        private void OnDetailClick(object sender, RoutedEventArgs e)
        {
            detailOpen = !detailOpen;
            DetailHost.Visibility = detailOpen ? Visibility.Visible : Visibility.Collapsed;
            BtnDetail.Content = detailOpen ? "技术详情 ▴" : "技术详情 ▾";
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseWith(buttons.EscResult); }
            else if (e.Key == Key.Enter) { e.Handled = true; CloseWith(enterResult); }
        }

        // —— 静态入口 ——

        internal static MessageBoxResult Show(Window owner, string title, string body,
            MsgSeverity severity, MsgButtons buttonSet, string detail, string okText,
            MessageBoxResult defaultResult)
        {
            try
            {
                MessageDialogWpf dlg = new MessageDialogWpf(owner, title, body, severity,
                    buttonSet, detail, okText, defaultResult);
                dlg.ShowDialog();
                return dlg.chosen;
            }
            catch (Exception ex)
            {
                Logger.Log("MessageDialogWpf 异常，降级原生弹窗：" + ex.Message);
                MessageBoxButton native = buttonSet == MsgButtons.Ok ? MessageBoxButton.OK
                    : (buttonSet == MsgButtons.OkCancel ? MessageBoxButton.OKCancel : MessageBoxButton.YesNo);
                MessageBoxImage img = severity == MsgSeverity.Danger ? MessageBoxImage.Error
                    : (severity == MsgSeverity.Warning ? MessageBoxImage.Warning : MessageBoxImage.Information);
                // 主题路径的默认焦点在「否」（危险确认回车=否）：降级路径用
                // defaultResult=No 保持同一语义，避免回车直接确认危险操作
                MessageBoxResult defResult = native == MessageBoxButton.YesNo
                    ? MessageBoxResult.No : MessageBoxResult.OK;
                return MessageBox.Show(owner, title + "\r\n\r\n" + body, "Caelus", native, img, defResult);
            }
        }

        // 便捷重载：无详情/无自定义文案/默认焦点在主按钮
        internal static MessageBoxResult Show(Window owner, string title, string body,
            MsgSeverity severity, MsgButtons buttonSet)
        {
            return Show(owner, title, body, severity, buttonSet, null, null, MessageBoxResult.None);
        }

        // 单字符串自动拆分（动态 ConfirmKey 等场景）
        internal static MessageBoxResult Show(Window owner, string message,
            MsgSeverity severity, MsgButtons buttonSet, MessageBoxResult defaultResult)
        {
            string[] parts = MsgDialogMaps.SplitTitleBody(message);
            return Show(owner, parts[0], parts[1], severity, buttonSet, null, null, defaultResult);
        }

        private sealed class MaskAdorner : Adorner
        {
            public MaskAdorner(UIElement adorned) : base(adorned) { }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                SolidColorBrush b = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
                if (b.CanFreeze) b.Freeze();
                dc.DrawRectangle(b, null, new Rect(AdornedElement.RenderSize));
            }
        }
    }
}
