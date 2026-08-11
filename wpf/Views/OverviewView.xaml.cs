using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CaelusApp.WpfHost.Views
{
    public partial class OverviewView : UserControl
    {
        public OverviewView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.FadeIn(this);
            Motion.Emphasize(ReadyDot);
        }

        // GPU 温度使用独立指标面板，其余两项由集合视图生成。
        private void OnMetricsFilter(object sender, FilterEventArgs e)
        {
            var vm = DataContext as OverviewViewModel;
            var item = e.Item as MetricViewModel;
            if (vm == null || item == null) { e.Accepted = true; return; }
            e.Accepted = vm.Metrics.IndexOf(item) != 0;
        }
    }
}
