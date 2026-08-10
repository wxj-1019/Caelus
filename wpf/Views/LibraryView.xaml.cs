// @author zenjiro 18967498922@163.com
// 文件用途 游戏库视图：列表+空态+添加/移除按钮+拖放

using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp.WpfHost.Views
{
    public partial class LibraryView : UserControl
    {
        private LibraryViewModel vm;
        private int selectedIndex = -1;

        public LibraryView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            vm = DataContext as LibraryViewModel;
            if (vm != null)
            {
                vm.PropertyChanged += OnVmPropertyChanged;
                UpdatePanelVisibility();
            }
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsEmpty") UpdatePanelVisibility();
        }

        private void UpdatePanelVisibility()
        {
            if (vm == null) return;
            EmptyPanel.Visibility = vm.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            ListPanel.Visibility = vm.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        // 添加按钮
        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            var dlg = new AddGameDialogWpf(vm.Items);
            dlg.Owner = Window.GetWindow(this);
            bool? result = dlg.ShowDialog();
            if (result == true && dlg.SelectedHits.Count > 0)
            {
                vm.AddScannedGames(dlg.SelectedHits);
            }
        }

        // 移除按钮（简化：移除第一个——Phase 4 可加选中态）
        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (vm == null || vm.Items.Count == 0) return;
            vm.RemoveAt(0);
        }

        // 拖放
        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (vm == null) return;
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files == null) return;
            List<string> errors = new List<string>();
            foreach (string file in files)
            {
                string error = vm.AddFile(file);
                if (!string.IsNullOrEmpty(error) && error != "该游戏已经在列表中")
                    errors.Add(error);
            }
            if (errors.Count > 0)
                MessageBox.Show(string.Join("\n", errors.ToArray()), "Caelus",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
