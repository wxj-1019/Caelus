using System;
using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class ScenarioDetailView : UserControl
    {
        /// <summary>截图探针用：与概览页保持一致，由宿主注入演示态。此视图直接复用宿主的状态源。</summary>
        public static bool InjectSampleData;

        public ScenarioDetailView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneBanner, 100);
            Motion.RiseIn(ZoneCard, 160);
            Motion.RiseIn(ZoneSources, 220);
            if (ZoneFocus != null && ZoneFocus.Visibility == Visibility.Visible)
                Motion.RiseIn(ZoneFocus, 280);
            if (ZoneHealth != null && ZoneHealth.Visibility == Visibility.Visible)
                Motion.RiseIn(ZoneHealth, 280);
            Motion.RiseIn(ZoneNote, 340);
        }

        private void OnHealthRunNow(object sender, RoutedEventArgs e)
        {
            ScenarioDetailViewModel vm = DataContext as ScenarioDetailViewModel;
            if (vm == null) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { vm.RunHealthNowCore(); } catch { }
                Dispatcher.BeginInvoke(new Action(delegate { vm.RefreshHealthZone(true); }));
            });
        }

        private void OnStartupDisable(object sender, RoutedEventArgs e)
        {
            ScenarioDetailViewModel vm = DataContext as ScenarioDetailViewModel;
            if (vm == null) return;
            // 勾选快照在 UI 线程取，后台线程只做效果层
            var ids = new System.Collections.Generic.List<string>();
            foreach (StartupFindingRow r in vm.StartupFindings)
                if (r.IsChecked) ids.Add(r.Id);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { vm.DisableSelectedStartupCore(ids); } catch { }
                Dispatcher.BeginInvoke(new Action(delegate { vm.RefreshHealthZone(true); }));
            });
        }

        private void OnStartupUndo(object sender, RoutedEventArgs e)
        {
            FrameworkElement fe = sender as FrameworkElement;
            ScenarioDetailViewModel vm = DataContext as ScenarioDetailViewModel;
            if (fe == null || vm == null) return;
            string payload = fe.Tag as string;
            if (string.IsNullOrEmpty(payload)) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { vm.UndoStartupCore(payload); } catch { }
                Dispatcher.BeginInvoke(new Action(delegate { vm.RefreshHealthZone(true); }));
            });
        }
    }
}
