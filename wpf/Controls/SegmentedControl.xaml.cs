// @author zenjiro 18967498922@163.com
// 文件用途 通用分段选择控件：横向排列的单选段（替代 WinForms TierPicker）

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Controls
{
    public partial class SegmentedControl : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(List<string>), typeof(SegmentedControl),
                new PropertyMetadata(null, OnItemsSourceChanged));
        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register("SelectedIndex", typeof(int), typeof(SegmentedControl),
                new PropertyMetadata(0, OnSelectedIndexChanged));

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

        public SegmentedControl() { InitializeComponent(); }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SegmentedControl)d).Rebuild();
        }
        private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SegmentedControl)d).UpdateSelection();
        }

        private void Rebuild()
        {
            ItemsHost.Items.Clear();
            if (ItemsSource == null) return;
            // 单组名：同一控件内互斥；附加哈希避免跨实例共享同组
            string group = "seg_" + GetHashCode();
            for (int i = 0; i < ItemsSource.Count; i++)
            {
                int idx = i;
                RadioButton rb = new RadioButton();
                rb.Content = ItemsSource[i];
                rb.Style = (Style)FindResource("SegmentItem");
                rb.GroupName = group;
                rb.Tag = i;
                rb.Checked += delegate { if (SelectionChanged != null) SelectionChanged(this, idx); };
                ItemsHost.Items.Add(rb);
            }
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            int sel = SelectedIndex;
            foreach (object item in ItemsHost.Items)
            {
                RadioButton rb = item as RadioButton;
                if (rb == null) continue;
                int idx = (int)rb.Tag;
                rb.IsChecked = idx == sel;
            }
        }
    }
}
