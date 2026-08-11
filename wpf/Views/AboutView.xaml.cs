// @author zenjiro 18967498922@163.com
// 文件用途 WPF 关于页：项目信息 + 更新检查

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Views
{
    public partial class AboutView : UserControl
    {
        public AboutView() { InitializeComponent(); Loaded += OnLoaded; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
        }

        private void OnCheckUpdate(object sender, RoutedEventArgs e)
        {
            AboutViewModel vm = DataContext as AboutViewModel;
            if (vm == null) return;
            BtnCheckUpdate.IsEnabled = false;
            vm.UpdateStatus = Lang.T("upd.checking");
            LblUpdateStatus.Foreground = (Brush)Application.Current.TryFindResource("TextSecondaryBrush");
            UpdateChecker.CheckAsync(delegate(UpdateResult r)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    BtnCheckUpdate.IsEnabled = true;
                    if (!r.Ok)
                    {
                        vm.UpdateStatus = Lang.T("upd.fail");
                        LblUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                    }
                    else if (r.Newer)
                    {
                        vm.UpdateStatus = Lang.F("upd.newver", r.Latest, CaelusApp.App.VersionTag);
                        LblUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                    }
                    else
                    {
                        vm.UpdateStatus = Lang.F("upd.latest", CaelusApp.App.VersionTag);
                        LblUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                    }
                }));
            });
        }
    }
}
