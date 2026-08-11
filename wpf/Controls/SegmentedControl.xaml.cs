// @author zenjiro 18967498922@163.com
// 文件用途 通用分段选择控件：单一滑动指示板、键盘选择与 TwoWay 回写

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CaelusApp.WpfHost.Controls
{
    public partial class SegmentedControl : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(List<string>), typeof(SegmentedControl),
                new PropertyMetadata(null, OnItemsSourceChanged));
        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register("SelectedIndex", typeof(int), typeof(SegmentedControl),
                new FrameworkPropertyMetadata(0,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIndexChanged));

        public List<string> ItemsSource
        {
            get { return (List<string>)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }
        public int SelectedIndex
        {
            get { return (int)GetValue(SelectedIndexProperty); }
            set { SetValue(SelectedIndexProperty, value); }
        }

        public event EventHandler<int> SelectionChanged;

        private bool updatingSelection;
        private bool indicatorInitialized;

        public SegmentedControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            LayoutHost.SizeChanged += OnLayoutHostSizeChanged;
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SegmentedControl)d).Rebuild();
        }

        private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SegmentedControl)d).UpdateSelection();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            MoveIndicator(false);
        }

        private void OnLayoutHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate { MoveIndicator(false); }),
                DispatcherPriority.Loaded);
        }

        private void Rebuild()
        {
            ItemsHost.Items.Clear();
            indicatorInitialized = false;
            SelectionIndicator.Visibility = Visibility.Hidden;
            if (ItemsSource == null || ItemsSource.Count == 0) return;

            string group = "seg_" + GetHashCode();
            ControlTemplate slidingTemplate = (ControlTemplate)FindResource("SlidingSegmentItemTemplate");
            for (int i = 0; i < ItemsSource.Count; i++)
            {
                int index = i;
                var button = new RadioButton
                {
                    Content = ItemsSource[i],
                    Style = (Style)FindResource("SegmentItem"),
                    Template = slidingTemplate,
                    GroupName = group,
                    Tag = i
                };
                AutomationProperties.SetName(button, ItemsSource[i]);
                button.Checked += delegate
                {
                    if (updatingSelection) return;
                    SetCurrentValue(SelectedIndexProperty, index);
                    EventHandler<int> handler = SelectionChanged;
                    if (handler != null) handler(this, index);
                };
                ItemsHost.Items.Add(button);
            }
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            int selected = CoerceIndex(SelectedIndex);
            if (selected != SelectedIndex)
            {
                SetCurrentValue(SelectedIndexProperty, selected);
                return;
            }

            updatingSelection = true;
            try
            {
                foreach (object item in ItemsHost.Items)
                {
                    RadioButton button = item as RadioButton;
                    if (button == null) continue;
                    button.IsChecked = (int)button.Tag == selected;
                }
            }
            finally { updatingSelection = false; }

            Dispatcher.BeginInvoke(new Action(delegate { MoveIndicator(indicatorInitialized); }),
                DispatcherPriority.Loaded);
        }

        private int CoerceIndex(int index)
        {
            int count = ItemsSource == null ? 0 : ItemsSource.Count;
            if (count == 0) return 0;
            if (index < 0) return 0;
            return index >= count ? count - 1 : index;
        }

        private void MoveIndicator(bool animate)
        {
            if (!IsLoaded || ItemsHost.Items.Count == 0) return;
            int selected = CoerceIndex(SelectedIndex);
            RadioButton button = ItemsHost.Items[selected] as RadioButton;
            if (button == null || button.ActualWidth <= 0) return;

            Point point = button.TranslatePoint(new Point(0, 0), LayoutHost);
            double target = point.X;
            SelectionIndicator.Width = button.ActualWidth;
            SelectionIndicator.Visibility = Visibility.Visible;

            bool shouldAnimate = animate && Motion.Enabled && !Motion.Reduced;
            if (!shouldAnimate)
            {
                IndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                IndicatorTranslate.X = target;
                indicatorInitialized = true;
                return;
            }

            double current = IndicatorTranslate.X;
            var animation = new DoubleAnimation(current, target,
                TimeSpan.FromMilliseconds(UiMotion.SegmentMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += delegate
            {
                IndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                IndicatorTranslate.X = target;
            };
            IndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, animation,
                HandoffBehavior.SnapshotAndReplace);
            indicatorInitialized = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ItemsSource == null || ItemsSource.Count == 0) return;
            int next = SelectedIndex;
            if (e.Key == Key.Left) next--;
            else if (e.Key == Key.Right) next++;
            else if (e.Key == Key.Home) next = 0;
            else if (e.Key == Key.End) next = ItemsSource.Count - 1;
            else return;

            next = CoerceIndex(next);
            if (next != SelectedIndex)
            {
                SetCurrentValue(SelectedIndexProperty, next);
                EventHandler<int> handler = SelectionChanged;
                if (handler != null) handler(this, next);
            }
            RadioButton button = ItemsHost.Items[next] as RadioButton;
            if (button != null) button.Focus();
            e.Handled = true;
        }
    }
}
