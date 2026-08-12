using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CaelusApp.WpfHost.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class PolicyView : UserControl
    {
        // 预览探针（--wpf-shot）注入代表性开关态；生产永不置 true。
        internal static bool InjectSampleData;
        private PolicyPageViewModel subscribedViewModel;

        public PolicyView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PolicyPageViewModel vm = DataContext as PolicyPageViewModel;
            if (vm != null && subscribedViewModel != vm)
            {
                if (subscribedViewModel != null) subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                subscribedViewModel = vm;
                subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            // 预览样例：开启所有非风险项（锁定项与风险项由 setter 自行拒绝/跳过）
            if (InjectSampleData && vm != null)
            {
                foreach (PolicyCardViewModel c in vm.CoreCards) if (!c.IsRisky) c.IsOn = true;
                foreach (PolicyCardViewModel c in vm.CustomCards) if (!c.IsRisky) c.IsOn = true;
                foreach (PolicyCardViewModel c in vm.ExtraCards) if (!c.IsRisky) c.IsOn = true;
                vm.NotifyCounts();
            }

            SetPoliteLiveSetting(ZoneSummary);
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneSummary, 100);
            Motion.RiseIn(PolicyGroupSelector, 150);
            Motion.RiseIn(ZoneGroups, 200);
            Motion.Emphasize(SummaryModeBadge);
        }

        private static void SetPoliteLiveSetting(DependencyObject element)
        {
            // .NET 4 参考程序集不含 LiveSetting；在支持该 UIA API 的系统上反射启用。
            try
            {
                System.Type propertiesType = typeof(System.Windows.Automation.AutomationProperties);
                System.Reflection.MethodInfo setter = propertiesType.GetMethod("SetLiveSetting");
                if (setter == null) return;
                System.Type settingType = setter.GetParameters()[1].ParameterType;
                object polite = System.Enum.Parse(settingType, "Polite");
                setter.Invoke(null, new object[] { element, polite });
            }
            catch
            {
                // 旧版系统缺少 live region API 时保留 AutomationProperties.Name。
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (subscribedViewModel != null)
            {
                subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                subscribedViewModel = null;
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ModeText") Motion.Emphasize(SummaryModeBadge);
        }

        private void OnPolicyGroupChanged(object sender, int index)
        {
            SegmentedControl control = sender as SegmentedControl;
            if (control != null && control.SelectedIndex != index)
                control.SetCurrentValue(SegmentedControl.SelectedIndexProperty, index);

            ZoneCore.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            ZoneCustom.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            ZoneExtra.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

            FrameworkElement selected = index == 1 ? (FrameworkElement)ZoneCustom
                : index == 2 ? (FrameworkElement)ZoneExtra : ZoneCore;
            Motion.CrossFade(selected);
        }
    }
}
