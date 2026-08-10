// @author zenjiro 18967498922@163.com
// 文件用途 WPF 设置页：应用偏好开关 + 维护工具入口

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class SettingsView : UserControl
    {
        private static volatile bool shaderCleaning;

        public SettingsView() { InitializeComponent(); }

        // 保存自定义编译进程列表
        private void OnDevSave(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            vm.SaveDevCustom(TbDevCustom.Text);
            BtnDevSave.Content = Lang.T("set.dev.custom.saved");
            Dispatcher.BeginInvoke(new Action(delegate
            {
                BtnDevSave.Content = Lang.T("set.dev.custom.save");
            }), System.Windows.Threading.DispatcherPriority.Background, null);
            // 立即恢复按钮文字也走一次延时，避免瞬间闪烁后保持「已保存」
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(1500);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    BtnDevSave.Content = Lang.T("set.dev.custom.save");
                }));
            });
        }

        // 一键恢复已记录项：游戏模式 + 反作弊压制
        private void OnRestore(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null || vm.IsRestoreBusy) return;
            vm.IsRestoreBusy = true;
            BtnRestore.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool completed; int failed; int attempted;
                vm.RestoreAll(out completed, out failed, out attempted);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    BtnRestore.IsEnabled = true;
                    vm.IsRestoreBusy = false;
                    string message = Lang.T(completed ? "panic.done" : "panic.timeout");
                    if (!completed)
                        message += "\r\n\r\n" + Lang.F("panic.failedcount", failed, attempted);
                    MessageBox.Show(message, CaelusApp.App.DisplayName, MessageBoxButton.OK,
                        completed ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }));
            });
        }

        // Defender 扫描排除：WinForms 对话框不在 WPF 宿主链接范围内，给出提示
        private void OnDefender(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(Lang.T("def.open") + "\r\n\r\n" + Lang.T("def.open.sub"),
                "Caelus", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 英雄联盟附加层清理：同上
        private void OnAddon(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(Lang.T("addon.open") + "\r\n\r\n" + Lang.T("addon.open.sub"),
                "Caelus", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 着色器缓存清理（后台线程，避免 UI 卡顿）
        private void OnShader(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            if (shaderCleaning)
            {
                vm.ShaderStatus = Lang.T("shader.busy");
                return;
            }
            if (MessageBox.Show(Lang.T("shader.confirm"), "Caelus",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            BtnShader.IsEnabled = false;
            shaderCleaning = true;
            vm.ShaderStatus = Lang.T("shader.busy");
            ThreadPool.QueueUserWorkItem(delegate
            {
                CacheSweep.Result cr = ShaderCache.Clean();
                long left = ShaderCache.MeasureBytes();
                Logger.Log("着色器缓存清理：释放 " + CacheSweep.FmtBytes(cr.FreedBytes)
                    + (cr.FailedFiles > 0 ? "，" + cr.FailedFiles + " 个文件被占用已跳过" : ""));
                shaderCleaning = false;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    BtnShader.IsEnabled = true;
                    vm.ShaderStatus = CacheSweep.FmtBytes(left);
                    string msg = Lang.F("shader.freed", CacheSweep.FmtBytes(cr.FreedBytes))
                        + (cr.FailedFiles > 0 ? "\r\n" + Lang.F("shader.skip", cr.FailedFiles) : "")
                        + "\r\n\r\n" + Lang.T("shader.note");
                    MessageBox.Show(msg, "Caelus", MessageBoxButton.OK, MessageBoxImage.Information);
                }));
            });
        }
    }
}
