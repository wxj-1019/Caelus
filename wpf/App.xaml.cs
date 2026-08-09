// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主入口

using System.Windows;

namespace CaelusApp.WpfHost
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeManager.Apply(this, UiTone.Light);
            var w = new MainWindow();
            w.Show();
        }
    }
}
