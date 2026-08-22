// @author zenjiro 18967498922@163.com
// 文件用途 WPF 系统环境页：分区入场、危险确认、开关回滚与行内成功反馈

using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp.WpfHost.Views
{
    public partial class EnvironmentView : UserControl
    {
        public EnvironmentView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            EnvironmentViewModel vm = DataContext as EnvironmentViewModel;
            if (vm != null) vm.RefreshStatus();

            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneGraphics, 100);
            Motion.RiseIn(ZoneSecurity, 160);
            Motion.RiseIn(ZoneInterrupt, 220);
            Motion.RiseIn(ZoneNetwork, 280);
        }

        private void OnToggleClick(object sender, RoutedEventArgs e)
        {
            ToggleButton toggle = sender as ToggleButton;
            EnvToggle item = toggle == null ? null : toggle.DataContext as EnvToggle;
            if (toggle == null || item == null) return;

            bool desired = toggle.IsChecked == true;

            // 与旧 WinForms 保持一致：除游戏模式守护（gmguard 无需管理员）外，
            // 全部内核/驱动项先查管理员权限；无权限时提示并回滚 Toggle。
            if (item.Id != "gmguard" && !IsAdministrator())
            {
                MessageDialogWpf.Show(Window.GetWindow(this), "需要管理员权限",
                    "修改 VBS / hypervisor 需要管理员身份运行 Caelus。",
                    MsgSeverity.Warning, MsgButtons.Ok);
                RollBack(toggle, item);
                return;
            }

            // 与旧 WinForms 保持一致：关闭 VBS 前警告；无权限或取消时立即回滚 Toggle。
            if (item.Id == "vbs")
            {
                if (desired)
                {
                    MessageBoxResult result = MessageDialogWpf.Show(Window.GetWindow(this), "关闭 VBS / 内存完整性？",
                        "系统安全性会下降，WSL2 / Docker / Hyper-V / 沙盒将不可用。重启后生效，将来恢复需再重启一次。",
                        MsgSeverity.Danger, MsgButtons.OkCancel, null, "关闭 VBS", MessageBoxResult.Cancel);
                    if (result != MessageBoxResult.OK)
                    {
                        RollBack(toggle, item);
                        return;
                    }
                }
            }

            if (!item.Apply(desired))
            {
                string message = item.Id == "vbs" && !desired
                    ? Lang.T("vbs.restorefail")
                    : Lang.T("env.failed");
                MessageDialogWpf.Show(Window.GetWindow(this), "环境项设置失败", "系统设置保持原样。",
                    MsgSeverity.Danger, MsgButtons.Ok, message, null, MessageBoxResult.OK);
                RollBack(toggle, item);
                return;
            }

            // IsChecked 是 OneWay：显式同步真实状态，避免注册表回读与期望值不一致。
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, item.IsOn);
            Motion.Emphasize(toggle);
        }

        private static void RollBack(ToggleButton toggle, EnvToggle item)
        {
            item.Refresh();
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, item.IsOn);
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
