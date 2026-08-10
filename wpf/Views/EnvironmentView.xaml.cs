// @author zenjiro 18967498922@163.com
// 文件用途 WPF 系统环境页：11 项内核/驱动开关的点击执行 + 重启提示

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CaelusApp.WpfHost.Views
{
    public partial class EnvironmentView : UserControl
    {
        public EnvironmentView() { InitializeComponent(); }

        // 开关点击：执行对应 tweak，弹出重启提示
        private void OnToggleClick(object sender, RoutedEventArgs e)
        {
            ToggleButton tb = sender as ToggleButton;
            EnvToggle item = tb == null ? null : tb.DataContext as EnvToggle;
            if (item == null) return;
            EnvironmentViewModel vm = DataContext as EnvironmentViewModel;
            if (vm == null) return;

            // tb.IsChecked 反映用户期望的新状态（OneWay 绑定下不会自动回写）
            bool desired = tb.IsChecked == true;
            string hint = item.Apply(desired);

            if (hint == null) return;          // 无需提示
            if (hint.Length == 0)
            {
                // 操作失败
                MessageBox.Show(Lang.T("winopt.failed"), "Caelus",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MessageBox.Show(Lang.T(hint), "Caelus",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
