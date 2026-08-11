// @author zenjiro 18967498922@163.com
// 文件用途 游戏库视图：列表选择、空态、添加/移除、运行态与拖放

using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp.WpfHost.Views
{
    public partial class LibraryView : UserControl
    {
        private LibraryViewModel vm;
        private bool panelStateInitialized;
        private bool showingEmpty;
        private bool loadingPanelState;

        public LibraryView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LibraryViewModel next = DataContext as LibraryViewModel;
            if (vm != next)
            {
                DetachViewModel();
                vm = next;
                if (vm != null) vm.PropertyChanged += OnVmPropertyChanged;
            }

            loadingPanelState = true;
            try
            {
                if (vm != null)
                {
                    vm.ProbeRunning();
                    GameList.Items.Refresh();
                }
                UpdatePanelVisibility(false);
            }
            finally
            {
                loadingPanelState = false;
            }
            UpdateSelectionState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachViewModel();
            vm = null;
            panelStateInitialized = false;
            GameList.SelectedIndex = -1;
        }

        private void DetachViewModel()
        {
            if (vm != null) vm.PropertyChanged -= OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsEmpty")
            {
                UpdatePanelVisibility(!loadingPanelState);
                UpdateSelectionState();
            }
        }

        private void UpdatePanelVisibility(bool reveal)
        {
            bool isEmpty = vm == null || vm.IsEmpty;
            bool changed = !panelStateInitialized || showingEmpty != isEmpty;

            EmptyPanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            EmptyPanel.IsHitTestVisible = isEmpty;
            ListPanel.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            ListPanel.IsHitTestVisible = !isEmpty;

            showingEmpty = isEmpty;
            if (!panelStateInitialized)
            {
                panelStateInitialized = true;
                return;
            }
            if (reveal && changed)
                Motion.Reveal(isEmpty ? (FrameworkElement)EmptyPanel : ListPanel);
        }

        private void UpdateSelectionState()
        {
            BtnRemove.IsEnabled = vm != null && GameList.SelectedIndex >= 0;
        }

        private void OnGameSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionState();
        }

        private void OnGameListPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete || !BtnRemove.IsEnabled) return;
            e.Handled = true;
            RemoveSelected();
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            var dlg = new AddGameDialogWpf(vm.Items);
            dlg.Owner = Window.GetWindow(this);
            bool? result = dlg.ShowDialog();
            if (result == true && dlg.SelectedHits.Count > 0)
                vm.AddScannedGames(dlg.SelectedHits);
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            RemoveSelected();
        }

        private void RemoveSelected()
        {
            if (vm == null) return;
            int index = GameList.SelectedIndex;
            if (index < 0 || index >= vm.Items.Count) return;

            vm.RemoveAt(index);
            if (vm.Items.Count > 0)
            {
                GameList.SelectedIndex = index < vm.Items.Count ? index : vm.Items.Count - 1;
                GameList.ScrollIntoView(GameList.SelectedItem);
            }
            UpdateSelectionState();
        }

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
            if (vm == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
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
