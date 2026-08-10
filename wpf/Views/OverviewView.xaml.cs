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
            Motion.Pulse(ReadyDot);
        }

        // R2 指标行过滤掉首项（GPU 温度已在大卡聚光灯展示，避免重复）
        private void OnMetricsFilter(object sender, FilterEventArgs e)
        {
            var vm = DataContext as OverviewViewModel;
            var item = e.Item as MetricViewModel;
            // 防御性放行（vm/item 为 null 时保守接受，避免过滤异常）
            if (vm == null || item == null) { e.Accepted = true; return; }
            // 业务过滤：排除首项（GPU 温度已在大卡聚光灯展示）
            e.Accepted = vm.Metrics.IndexOf(item) != 0;
        }
    }
}
