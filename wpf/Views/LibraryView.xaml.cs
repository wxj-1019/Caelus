// @author zenjiro 18967498922@163.com
// 文件用途 游戏库视图：列表选择、空态、添加/移除、运行态与拖放

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp.WpfHost.Views
{
    public partial class LibraryView : UserControl
    {
        internal static bool InjectSampleData;

        private LibraryViewModel vm;
        private DispatcherTimer runningTimer;
        private bool panelStateInitialized;
        private bool showingEmpty;
        private bool loadingPanelState;
        private bool isDropActive;

        public LibraryView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
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
            finally { loadingPanelState = false; }
            UpdateSelectionState();

            if (InjectSampleData && vm != null && vm.Items.Count == 0)
            {
                vm.Items.Add(new LibraryItem("sample-lol", "League of Legends", @"C:\Riot Games\League of Legends\LeagueClient.exe"));
                vm.Items.Add(new LibraryItem("sample-val", "VALORANT", @"C:\Riot Games\VALORANT\live\VALORANT.exe"));
                vm.Items.Add(new LibraryItem("sample-cs2", "Counter-Strike 2", @"D:\Steam\steamapps\common\CS2\cs2.exe"));
                vm.Items.Add(new LibraryItem("sample-genshin", "原神", @"G:\Genshin Impact\GenshinImpact.exe"));
                vm.Items[0].IsRunning = true;
                vm.Items[1].IsRunning = true;
                loadingPanelState = true;
                try { UpdatePanelVisibility(false); } finally { loadingPanelState = false; }
                GameList.Items.Refresh();
                GameList.SelectedIndex = 0;
                UpdateSelectionState();
            }

            StartRunningTimer();
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneToolbar, 100);
            Motion.RiseIn(ZoneList, 160);
            Motion.RiseIn(EmptyPanel, 160);
            Motion.BreathScale(EmptyHeroIcon, 1.0, 1.06, 3);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopRunningTimer();
            DetachViewModel();
            vm = null;
            panelStateInitialized = false;
            GameList.SelectedIndex = -1;
            SetDropActive(false);
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) StartRunningTimer();
            else StopRunningTimer();
        }

        private void StartRunningTimer()
        {
            if (!IsLoaded || !IsVisible || vm == null) return;
            if (runningTimer == null)
            {
                runningTimer = new DispatcherTimer(DispatcherPriority.Background);
                runningTimer.Interval = TimeSpan.FromSeconds(5);
                runningTimer.Tick += OnRunningTimerTick;
            }
            runningTimer.Start();
        }

        private void StopRunningTimer()
        {
            if (runningTimer == null) return;
            runningTimer.Stop();
            runningTimer.Tick -= OnRunningTimerTick;
            runningTimer = null;
        }

        private void OnRunningTimerTick(object sender, EventArgs e)
        {
            if (vm == null || !IsVisible) { StopRunningTimer(); return; }
            vm.ProbeRunning();
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
            if (!panelStateInitialized) { panelStateInitialized = true; return; }
            if (reveal && changed) Motion.Reveal(isEmpty ? (FrameworkElement)EmptyPanel : ListPanel);
        }

        private void UpdateSelectionState()
        {
            BtnRemove.IsEnabled = vm != null && GameList.SelectedIndex >= 0;
        }

        private void OnGameSelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateSelectionState(); }

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
            if (result != true || dlg.SelectedHits.Count == 0) return;
            int added = vm.AddScannedGames(dlg.SelectedHits);
            vm.SetFeedback(added > 0 ? "已添加 " + added + " 个游戏。" : "没有添加新游戏，所选项可能已在列表中。", added > 0 ? "Success" : "Warning");
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e) { RemoveSelected(); }

        private void RemoveSelected()
        {
            if (vm == null) return;
            int index = GameList.SelectedIndex;
            if (index < 0 || index >= vm.Items.Count) return;
            LibraryItem item = vm.Items[index];
            if (MessageBox.Show("确定从游戏库移除“" + item.Name + "”吗？", "Caelus", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            vm.RemoveAt(index);
            vm.SetFeedback("已移除“" + item.Name + "”。", "Success");
            if (vm.Items.Count > 0)
            {
                GameList.SelectedIndex = index < vm.Items.Count ? index : vm.Items.Count - 1;
                GameList.ScrollIntoView(GameList.SelectedItem);
            }
            UpdateSelectionState();
        }

        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            LibraryItem item = button == null ? null : button.DataContext as LibraryItem;
            if (vm == null || item == null || string.IsNullOrEmpty(item.Path)) return;
            try
            {
                Clipboard.SetText(item.Path);
                vm.SetFeedback("已复制“" + item.Name + "”的完整路径。", "Success");
            }
            catch { vm.SetFeedback("无法复制路径，请稍后重试。", "Error"); }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            bool isFileDrop = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = isFileDrop ? DragDropEffects.Copy : DragDropEffects.None;
            SetDropActive(isFileDrop);
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e) { SetDropActive(false); }

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
            if (files == null || files.Length == 0) return;

            int added = 0;
            int duplicates = 0;
            List<string> errors = new List<string>();
            foreach (string file in files)
            {
                string error = vm.AddFile(file);
                if (string.IsNullOrEmpty(error)) added++;
                else if (error == "该游戏已经在列表中") duplicates++;
                else errors.Add(error);
            }
            if (errors.Count > 0)
                vm.SetFeedback("已添加 " + added + " 个，重复 " + duplicates + " 个；失败：" + string.Join("；", errors.ToArray()), "Error");
            else if (added > 0)
                vm.SetFeedback("已添加 " + added + " 个游戏" + (duplicates > 0 ? "，跳过 " + duplicates + " 个重复项。" : "。"), "Success");
            else
                vm.SetFeedback("没有添加新游戏，跳过 " + duplicates + " 个重复项。", "Warning");
        }
    }
}
