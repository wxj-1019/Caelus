// @author zenjiro 18967498922@163.com
// 文件用途 WPF 关于页：项目信息、外部链接与更新检查

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace CaelusApp.WpfHost.Views
{
    public partial class AboutView : UserControl
    {
        public AboutView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneBrand, 100);
            Motion.RiseIn(ZoneInfo, 160);
            Motion.RiseIn(ZoneUpdate, 220);
        }

        private void OnOpenNotes(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Dialogs.ReleaseNotesDialogWpf();
                dlg.Owner = Window.GetWindow(this);
                dlg.ShowDialog();
                AboutViewModel vm = DataContext as AboutViewModel;
                if (vm != null) vm.RefreshNotesButton();
            }
            catch { }
        }

        private void OnNavigate(object sender, RequestNavigateEventArgs e)
        {
            OpenExternalUrl(e.Uri == null ? null : e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private void OnDownload(object sender, RoutedEventArgs e)
        {
            AboutViewModel vm = DataContext as AboutViewModel;
            if (vm == null || !OpenExternalUrl(vm.DownloadUrl))
            {
                if (vm != null)
                {
                    vm.UpdateKind = "Error";
                    vm.UpdateStatus = "无法打开下载地址，请稍后重试。";
                }
            }
        }

        private void OnCheckUpdate(object sender, RoutedEventArgs e)
        {
            AboutViewModel vm = DataContext as AboutViewModel;
            if (vm == null) return;
            BtnCheckUpdate.IsEnabled = false;
            vm.SetUpdateResult(null);
            vm.UpdateKind = "Info";
            vm.UpdateStatus = Lang.T("upd.checking");
            UpdateChecker.CheckAsync(delegate(UpdateResult r)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!IsLoaded) return;
                    BtnCheckUpdate.IsEnabled = true;
                    vm.SetUpdateResult(r);
                    if (r == null || !r.Ok)
                    {
                        vm.UpdateKind = "Error";
                        vm.UpdateStatus = Lang.T("upd.fail");
                        Logger.Log("检查更新失败：" + (r == null ? "未知错误" : r.Error));
                    }
                    else if (r.Newer)
                    {
                        vm.UpdateKind = "Success";
                        vm.UpdateStatus = Lang.F("upd.newver", r.Latest, CaelusApp.App.VersionTag);
                        Logger.Log("检查更新：发现新版本 " + r.Latest + "（当前 " + CaelusApp.App.VersionTag + "）");
                    }
                    else
                    {
                        vm.UpdateKind = "Success";
                        vm.UpdateStatus = Lang.F("upd.latest", CaelusApp.App.VersionTag);
                        Logger.Log("检查更新：已是最新版本（" + CaelusApp.App.VersionTag + "）");
                    }
                    Motion.Emphasize(ZoneUpdate);
                }));
            });
        }

        private static bool OpenExternalUrl(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return false;
            try
            {
                Process.Start(uri.AbsoluteUri);
                return true;
            }
            catch { return false; }
        }
    }
}
