// @author zenjiro 18967498922@163.com
// 文件用途 WPF 日志页：展示运行日志尾部并提供筛选、刷新、打开和清空

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CaelusApp.WpfHost.Views
{
    public partial class LogView : UserControl
    {
        private readonly DispatcherTimer refreshTimer;

        public LogView()
        {
            InitializeComponent();
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            refreshTimer.Tick += OnRefreshTimerTick;
            SwFollow.Checked += OnFollowChanged;
            SwFollow.Unchecked += OnFollowChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneLog, 100);
            RefreshLog(true);
            UpdateRefreshState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) { refreshTimer.Stop(); }
        private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) { UpdateRefreshState(); }
        private void OnRefreshTimerTick(object sender, EventArgs e) { RefreshLog(true); }
        private void OnRefreshLog(object sender, RoutedEventArgs e) { RefreshLog(true); }
        private void OnFollowChanged(object sender, RoutedEventArgs e)
        {
            UpdateRefreshState();
            if (SwFollow.IsChecked == true) RefreshLog(true);
        }

        private void UpdateRefreshState()
        {
            if (IsLoaded && IsVisible && SwFollow.IsChecked == true) refreshTimer.Start();
            else refreshTimer.Stop();
        }

        private void RefreshLog(bool scrollToEnd)
        {
            LogViewModel vm = DataContext as LogViewModel;
            if (vm == null) return;
            vm.Refresh();
            if (scrollToEnd && vm.HasVisibleLog) TbLog.ScrollToEnd();
        }

        private void OnOpenLog(object sender, RoutedEventArgs e)
        {
            LogViewModel vm = DataContext as LogViewModel;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", Logger.LogPath);
                if (vm != null) vm.ShowFeedback("已打开日志文件位置。", "Success");
            }
            catch
            {
                if (vm != null) vm.ShowFeedback("无法打开日志文件位置，请确认文件路径可用。", "Error");
            }
        }

        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            MessageBoxResult r = MessageBox.Show(Lang.T("rep.clear.ask"), "Caelus",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            LogViewModel vm = DataContext as LogViewModel;
            try
            {
                Logger.Clear();
                if (vm != null)
                {
                    vm.Refresh();
                    vm.ShowFeedback("运行日志已清除。", "Success");
                }
            }
            catch
            {
                if (vm != null) vm.ShowFeedback("清除运行日志失败，请稍后重试。", "Error");
            }
        }
    }
}
