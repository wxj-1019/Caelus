// @author zenjiro 18967498922@163.com
// 文件用途 WPF 白名单页：列表选中 + 拖放 + 浏览 + 移除/缩窄/扩展/重置按钮

using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp.WpfHost.Views
{
    public partial class WhitelistView : UserControl
    {
        private WhitelistViewModel vm;
        private WhitelistItemSelected lastRuleSelection;
        private bool panelStateInitialized;
        private bool showingEmpty;
        private bool loadingPanelState;
        private bool isDropActive;

        public WhitelistView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneLeft, 100);
            Motion.RiseIn(ZoneRight, 160);
            WhitelistViewModel next = DataContext as WhitelistViewModel;
            if (vm != next)
            {
                if (vm != null) vm.PropertyChanged -= OnVmPropertyChanged;
                vm = next;
                if (vm != null) vm.PropertyChanged += OnVmPropertyChanged;
            }
            if (vm == null) return;
            loadingPanelState = true;
            try
            {
                vm.Refresh(true);
                UpdatePanelVisibility(false);
            }
            finally
            {
                loadingPanelState = false;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (vm != null) vm.PropertyChanged -= OnVmPropertyChanged;
            vm = null;
            lastRuleSelection = null;
            panelStateInitialized = false;
            SetDropActive(false);
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsEmpty") UpdatePanelVisibility(!loadingPanelState);
        }

        private void UpdatePanelVisibility(bool reveal)
        {
            if (vm == null) return;
            bool isEmpty = vm.IsEmpty;
            bool changed = !panelStateInitialized || showingEmpty != isEmpty;

            EmptyPanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            EmptyPanel.IsHitTestVisible = isEmpty;
            RuleList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            RuleList.IsHitTestVisible = !isEmpty;

            showingEmpty = isEmpty;
            if (!panelStateInitialized)
            {
                panelStateInitialized = true;
                return;
            }
            if (reveal && changed)
                Motion.Reveal(isEmpty ? (FrameworkElement)EmptyPanel : RuleList);
        }

        private void OnRuleSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (vm == null) return;
            WhitelistItemSelected item = RuleList.SelectedItem as WhitelistItemSelected;
            if (item != null && item.IsGroupHeader)
            {
                RuleList.SelectedItem = lastRuleSelection;
                return;
            }
            lastRuleSelection = item;
            vm.Selected = item;
        }

        private void OnRuleListPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Up && e.Key != Key.Down) return;
            int direction = e.Key == Key.Down ? 1 : -1;
            int index = RuleList.SelectedIndex;
            if (index < 0) index = direction > 0 ? -1 : RuleList.Items.Count;
            int next = index + direction;
            while (next >= 0 && next < RuleList.Items.Count)
            {
                WhitelistItemSelected item = RuleList.Items[next] as WhitelistItemSelected;
                if (item != null && !item.IsGroupHeader)
                {
                    RuleList.SelectedIndex = next;
                    RuleList.ScrollIntoView(item);
                    ListBoxItem container = RuleList.ItemContainerGenerator.ContainerFromIndex(next) as ListBoxItem;
                    if (container != null) container.Focus();
                    e.Handled = true;
                    return;
                }
                next += direction;
            }
        }

        private void OnRuleListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete || vm == null || !vm.CanRemoveSelected) return;
            e.Handled = true;
            RemoveSelected();
        }

        private void OnRuleListPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            ListBoxItem item = ItemsControl.ContainerFromElement(RuleList, source) as ListBoxItem;
            if (item == null) return;
            WhitelistItemSelected rule = item.DataContext as WhitelistItemSelected;
            if (rule == null || rule.IsGroupHeader) return;
            RuleList.SelectedItem = rule;
        }

        // 从运行中的程序批量选择，已存在的路径不再列出。
        private void OnPickClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            var known = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (WhitelistItemSelected item in vm.Items)
            {
                if (item == null || item.IsGroupHeader
                    || item.Kind == WhitelistRuleKind.LegacyName
                    || string.IsNullOrEmpty(item.Value)) continue;
                known.Add(item.Value);
            }

            var dlg = new RunningPickerDialogWpf(known);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true && dlg.SelectedPaths.Count > 0)
                AddFiles(dlg.SelectedPaths);
        }

        // 浏览（多选）
        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            var dlg = new OpenFileDialog();
            dlg.Filter = Lang.T("white.browse.filter");
            dlg.Multiselect = true;
            if (dlg.ShowDialog() != true) return;
            AddFiles(dlg.FileNames);
        }

        // 拖入文件加入白名单（OLE 拖放与窗口级 WM_DROPFILES 兜底路径共用）
        public void AddFiles(IEnumerable<string> files)
        {
            if (vm == null) return;
            string error = vm.AddFiles(files);
            if (!string.IsNullOrEmpty(error))
                MessageDialogWpf.Show(Window.GetWindow(this), "白名单操作失败",
                    "详细信息见技术详情。", MsgSeverity.Danger, MsgButtons.Ok,
                    error, null, MessageBoxResult.OK);
        }

        // 移除当前选中
        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            RemoveSelected();
        }

        private void RemoveSelected()
        {
            if (vm == null || vm.Selected == null) return;
            if (vm.Selected.Required)
            {
                MessageDialogWpf.Show(Window.GetWindow(this), "系统内置项不可删除",
                    "这是系统必需的内置项。", MsgSeverity.Info, MsgButtons.Ok);
                return;
            }
            string error;
            if (!vm.RemoveSelected(out error) && !string.IsNullOrEmpty(error))
                MessageDialogWpf.Show(Window.GetWindow(this), "白名单操作失败",
                    "详细信息见技术详情。", MsgSeverity.Danger, MsgButtons.Ok,
                    error, null, MessageBoxResult.OK);
        }

        // 缩窄：family → exact
        private void OnNarrowClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            string error;
            if (!vm.NarrowSelected(out error) && !string.IsNullOrEmpty(error))
                MessageDialogWpf.Show(Window.GetWindow(this), "白名单操作失败",
                    "详细信息见技术详情。", MsgSeverity.Danger, MsgButtons.Ok,
                    error, null, MessageBoxResult.OK);
        }

        // 扩展：exact → family
        private void OnWidenClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            string error;
            if (!vm.WidenSelected(out error) && !string.IsNullOrEmpty(error))
                MessageDialogWpf.Show(Window.GetWindow(this), "白名单操作失败",
                    "详细信息见技术详情。", MsgSeverity.Danger, MsgButtons.Ok,
                    error, null, MessageBoxResult.OK);
        }

        // 重置
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            MessageBoxResult r = MessageDialogWpf.Show(Window.GetWindow(this), "恢复默认白名单预设？",
                "会删除全部自定义白名单规则，且无法撤销。",
                MsgSeverity.Danger, MsgButtons.YesNo, null, "恢复默认", MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;
            string error;
            if (!vm.Reset(out error) && !string.IsNullOrEmpty(error))
                MessageDialogWpf.Show(Window.GetWindow(this), "白名单重置失败",
                    "详细信息见技术详情。", MsgSeverity.Danger, MsgButtons.Ok,
                    error, null, MessageBoxResult.OK);
        }

        // 拖放
        private void OnDragOver(object sender, DragEventArgs e)
        {
            bool isFileDrop = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = isFileDrop ? DragDropEffects.Copy : DragDropEffects.None;
            SetDropActive(isFileDrop);
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            SetDropActive(false);
        }

        private void SetDropActive(bool active)
        {
            if (isDropActive == active) return;
            isDropActive = active;
            DropOverlay.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active) Motion.Reveal(DropOverlay);
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            SetDropActive(false);
            if (vm == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            AddFiles(files);
        }
    }
}
