// @author zenjiro 18967498922@163.com
// 文件用途 WPF 反作弊页：分区入场、设置反馈与页面可见期低频状态刷新

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaelusApp.WpfHost.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class AntiCheatView : UserControl
    {
        private readonly DispatcherTimer statusTimer;

        public AntiCheatView()
        {
            InitializeComponent();
            statusTimer = new DispatcherTimer(DispatcherPriority.Background);
            statusTimer.Interval = TimeSpan.FromSeconds(4);
            statusTimer.Tick += OnStatusTick;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
            statusTimer.Start();
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneSummary, 100);
            Motion.RiseIn(ZoneMaster, 160);
            Motion.RiseIn(ZoneGroups, 220);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            statusTimer.Stop();
        }

        private void OnStatusTick(object sender, EventArgs e)
        {
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            AntiCheatViewModel vm = DataContext as AntiCheatViewModel;
            if (vm != null) vm.RefreshStatus();
        }

        private void OnMasterChanged(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
            Motion.Emphasize(ZoneSummary);
            if (SwMaster.IsChecked != true) Motion.Emphasize(ZonePause);
        }

        private void OnGroupChanged(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
            Motion.Emphasize(ZoneSummary);
        }

        // SegmentedControl 只发出索引事件；先同步绑定，再刷新状态文案。
        private void OnLevelChanged(object sender, int index)
        {
            SegmentedControl control = sender as SegmentedControl;
            if (control != null && control.SelectedIndex != index)
                control.SetCurrentValue(SegmentedControl.SelectedIndexProperty, index);

            RefreshStatus();
            FrameworkElement element = sender as FrameworkElement;
            if (element != null) Motion.Emphasize(element);
        }
    }
}
