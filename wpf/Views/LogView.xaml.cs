// @author zenjiro 18967498922@163.com
// 文件用途 WPF 日志页：展示运行日志尾部并提供打开/清空

using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class LogView : UserControl
    {
        public LogView() { InitializeComponent(); }

        private void OnOpenLog(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", Logger.LogPath); }
            catch { }
        }

        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            MessageBoxResult r = MessageBox.Show(Lang.T("rep.clear.ask"), "Caelus",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            Logger.Clear();
            Logger.Log("运行日志已手动清除");
            LogViewModel vm = DataContext as LogViewModel;
            if (vm != null) vm.Refresh();
        }
    }
}
