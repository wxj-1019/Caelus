// @author zenjiro 18967498922@163.com
// 文件用途 WPF 系统环境页：分区入场、危险确认、开关回滚与行内成功反馈

using System;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp.WpfHost.Views
{
    public partial class EnvironmentView : UserControl
    {
        // 0=空闲 1=应用中（Interlocked 守护）：MSI 全量扫描/中断亲和等项可耗时数秒
        private int applyBusy;

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

            // 应用期间忽略其他开关点击：防止并发写注册表/设备与状态错乱
            if (Interlocked.Exchange(ref applyBusy, 1) != 0)
            {
                item.ShowInfo("正在应用其他项，请稍候…");
                RollBack(toggle, item);
                return;
            }

            bool desired = toggle.IsChecked == true;

            // 与旧 WinForms 保持一致：除游戏模式守护（gmguard 无需管理员）外，
            // 全部内核/驱动项先查管理员权限；无权限时提示并回滚 Toggle。
            if (item.Id != "gmguard" && !IsAdministrator())
            {
                Interlocked.Exchange(ref applyBusy, 0);
                MessageDialogWpf.Show(Window.GetWindow(this), "需要管理员权限",
                    "修改 VBS / hypervisor 需要管理员身份运行 Caelus。",
                    MsgSeverity.Warning, MsgButtons.Ok);
                RollBack(toggle, item);
                return;
            }

            // 与旧 WinForms 保持一致：关闭 VBS 前警告；无权限或取消时立即回滚 Toggle。
            if (item.Id == "vbs" && desired)
            {
                MessageBoxResult result = MessageDialogWpf.Show(Window.GetWindow(this), "关闭 VBS / 内存完整性？",
                    "系统安全性会下降，WSL2 / Docker / Hyper-V / 沙盒将不可用。重启后生效，将来恢复需再重启一次。",
                    MsgSeverity.Danger, MsgButtons.OkCancel, null, "关闭 VBS", MessageBoxResult.Cancel);
                if (result != MessageBoxResult.OK)
                {
                    Interlocked.Exchange(ref applyBusy, 0);
                    RollBack(toggle, item);
                    return;
                }
            }

            // 重型 tweak 放后台线程执行：UI 不冻结，行内给忙碌反馈，完成后回 UI 线程收尾
            toggle.IsEnabled = false;
            item.ShowBusy();
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok;
                try { ok = item.Apply(desired); }
                catch { ok = false; }
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Interlocked.Exchange(ref applyBusy, 0);
                        toggle.IsEnabled = true;
                        if (!ok)
                        {
                            string message = item.Id == "vbs" && !desired
                                ? Lang.T("vbs.restorefail")
                                : Lang.T("env.failed");
                            MessageDialogWpf.Show(Window.GetWindow(this), "环境项设置失败", "系统设置保持原样。",
                                MsgSeverity.Danger, MsgButtons.Ok, message, null, MessageBoxResult.OK);
                        }
                        // IsChecked 是 OneWay：显式同步真实状态，避免注册表回读与期望值不一致。
                        RollBack(toggle, item);
                        if (ok)
                        {
                            Motion.Emphasize(toggle);
                            if (desired && (item.Id == "irqaffinity" || item.Id == "usbaffinity"))
                                OfferDeviceRestart(item.Id);
                        }
                    }));
                }
                catch
                {
                    // 调度失败（应用正在退出）：必须释放忙碌标志，不留死锁的整页开关
                    Interlocked.Exchange(ref applyBusy, 0);
                }
            });
        }

        private static void RollBack(ToggleButton toggle, EnvToggle item)
        {
            item.Refresh();
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, item.IsOn);
        }

        /// <summary>中断亲和写入后可选热重启设备立即生效（否则要整机重启才应用）。
        /// GPU 的 Disable/Enable 会闪屏、USB 控制器会瞬断外设——必须用户确认后才动。</summary>
        private void OfferDeviceRestart(string itemId)
        {
            bool isGpu = itemId == "irqaffinity";
            MessageBoxResult r = MessageDialogWpf.Show(Window.GetWindow(this), "立即重启设备使其生效？",
                isGpu
                    ? "显卡设备将禁用后重新启用：屏幕会黑几秒，正在运行的游戏可能被中断。不重启则要等下次开机才生效。"
                    : "USB 控制器将禁用后重新启用：已插的外设会瞬断几秒。不重启则要等下次开机才生效。",
                MsgSeverity.Warning, MsgButtons.OkCancel, null, "立即重启设备", MessageBoxResult.Cancel);
            if (r != MessageBoxResult.OK) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool anyOk = false;
                var errors = new System.Collections.Generic.List<string>();
                System.Collections.Generic.List<string> ids = isGpu
                    ? CaelusApp.InterruptAffinityTweak.EnumerateGpuDeviceIds()
                    : CaelusApp.UsbInterruptAffinityTweak.EnumerateUsbControllerIds();
                foreach (string id in ids)
                {
                    string err;
                    if (isGpu
                            ? CaelusApp.InterruptAffinityTweak.RestartDevice(id, out err)
                            : CaelusApp.UsbInterruptAffinityTweak.RestartDevice(id, out err))
                        anyOk = true;
                    else if (err != null) errors.Add(err);
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    EnvironmentViewModel vm = DataContext as EnvironmentViewModel;
                    if (vm == null) return;
                    if (ids.Count == 0) vm.ShowPageFeedback("未找到可重启的设备。", "Warning");
                    else if (anyOk) vm.ShowPageFeedback("设备已重启，中断亲和已生效。", "Success");
                    else vm.ShowPageFeedback("设备重启失败：" + (errors.Count > 0 ? errors[0] : "未知错误"), "Error");
                    Motion.Emphasize(PageFeedbackBanner);
                }));
            });
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
