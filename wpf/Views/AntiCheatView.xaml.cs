// @author zenjiro 18967498922@163.com
// 文件用途 WPF 反作弊页：压制总开关 + 9 个分组卡片（开关 / 档位 / 状态）

using System.Windows.Controls;
using CaelusApp.WpfHost.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class AntiCheatView : UserControl
    {
        public AntiCheatView() { InitializeComponent(); }

        // 段控件选择变化后刷新状态文案
        private void OnLevelChanged(object sender, int index)
        {
            AntiCheatViewModel vm = DataContext as AntiCheatViewModel;
            if (vm != null) vm.RefreshStatus();
        }
    }
}
