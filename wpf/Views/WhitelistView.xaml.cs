// @author zenjiro 18967498922@163.com
// 文件用途 WPF 白名单页：列表选中 + 拖放 + 浏览 + 移除/缩窄/扩展/重置按钮

using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace CaelusApp.WpfHost.Views
{
    public partial class WhitelistView : UserControl
    {
        private WhitelistViewModel vm;

        public WhitelistView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            vm = DataContext as WhitelistViewModel;
            if (vm == null) return;
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.Refresh(false);
            vm.Refresh(true);
            UpdatePanelVisibility();
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsEmpty") UpdatePanelVisibility();
        }

        private void UpdatePanelVisibility()
        {
            if (vm == null) return;
            EmptyPanel.Visibility = vm.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            ListScroll.Visibility = vm.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        // 列表项点击 = 选中
        private void OnItemClick(object sender, MouseButtonEventArgs e)
        {
            if (vm == null) return;
            WhitelistItemSelected item = (sender as FrameworkElement).DataContext as WhitelistItemSelected;
            if (item != null && !item.IsGroupHeader) vm.Selected = item;
        }

        // 从运行中的程序选 —— Phase 4：暂未实现，弹提示
        private void OnPickClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("该功能将在后续版本中支持。", "Caelus",
                MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void AddFiles(IEnumerable<string> files)
        {
            if (vm == null) return;
            string error = vm.AddFiles(files);
            if (!string.IsNullOrEmpty(error))
                MessageBox.Show(error, "Caelus", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 移除当前选中
        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            if (vm.Selected == null) return;
            if (vm.Selected.Required)
            {
                MessageBox.Show(Lang.T("white.required.locked"), "Caelus",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string error;
            if (!vm.RemoveSelected(out error) && !string.IsNullOrEmpty(error))
                MessageBox.Show(error, "Caelus", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 缩窄：family → exact
        private void OnNarrowClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            string error;
            if (!vm.NarrowSelected(out error) && !string.IsNullOrEmpty(error))
                MessageBox.Show(error, "Caelus", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 扩展：exact → family
        private void OnWidenClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            string error;
            if (!vm.WidenSelected(out error) && !string.IsNullOrEmpty(error))
                MessageBox.Show(error, "Caelus", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 重置
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            MessageBoxResult r = MessageBox.Show(Lang.T("white.reset.confirm"), "Caelus",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;
            string error;
            if (!vm.Reset(out error) && !string.IsNullOrEmpty(error))
                MessageBox.Show(error, "Caelus", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 拖放
        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (vm == null) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;
            AddFiles(files);
        }
    }
}
