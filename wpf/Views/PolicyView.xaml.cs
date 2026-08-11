using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class PolicyView : UserControl
    {
        // 预览探针（--wpf-shot）注入代表性开关态；生产永不置 true。
        internal static bool InjectSampleData;

        public PolicyView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 预览样例：开启所有非风险项（锁定项与风险项由 setter 自行拒绝/跳过）
            if (InjectSampleData)
            {
                PolicyPageViewModel vm = DataContext as PolicyPageViewModel;
                if (vm != null)
                {
                    foreach (PolicyCardViewModel c in vm.CoreCards) if (!c.IsRisky) c.IsOn = true;
                    foreach (PolicyCardViewModel c in vm.CustomCards) if (!c.IsRisky) c.IsOn = true;
                    foreach (PolicyCardViewModel c in vm.ExtraCards) if (!c.IsRisky) c.IsOn = true;
                    vm.NotifyCounts();
                }
            }

            // 入场 stagger + 摘要脉冲点
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneSummary, 100);
            Motion.RiseIn(ZoneCore, 160);
            Motion.RiseIn(ZoneCustom, 220);
            Motion.RiseIn(ZoneExtra, 280);
            Motion.BreathPulse(SummaryDot);
        }
    }
}
