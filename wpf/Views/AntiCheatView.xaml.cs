// @author zenjiro 18967498922@163.com
// 文件用途 WPF 反作弊页：压制总开关 + 9 个分组卡片（开关 / 档位 / 状态）

using System.Windows;
using System.Windows.Controls;
using CaelusApp.WpfHost.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class AntiCheatView : UserControl
    {
        public AntiCheatView() { InitializeComponent(); Loaded += OnLoaded; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
        }

        // SegmentedControl 只发出索引事件；先同步绑定，再刷新状态文案。
        private void OnLevelChanged(object sender, int index)
        {
            SegmentedControl control = sender as SegmentedControl;
            if (control != null && control.SelectedIndex != index)
                control.SetCurrentValue(SegmentedControl.SelectedIndexProperty, index);

            AntiCheatViewModel vm = DataContext as AntiCheatViewModel;
            if (vm != null) vm.RefreshStatus();
        }
    }
}
