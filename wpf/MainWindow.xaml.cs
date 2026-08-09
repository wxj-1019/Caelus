// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主主窗口外壳：标题栏 / NavRail / 内容宿主

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaelusApp.WpfHost.Views;

namespace CaelusApp.WpfHost
{
    public partial class MainWindow : Window
    {
        public MainWindow() : this(null) { }

        internal MainWindow(IOverviewSource source)
        {
            InitializeComponent();
            OverviewViewModel vm = new OverviewViewModel(source ?? new SampleOverviewSource());
            vm.Refresh();
            DataContext = vm;
            PageHost.Content = new OverviewView { DataContext = vm };
        }

        private void TitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void MinClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void NavChecked(object sender, RoutedEventArgs e)
        {
            RadioButton rb = sender as RadioButton;
            if (rb == null || PageHost == null) return;
            PageHost.Content = rb == NavOverview
                ? (object)new OverviewView { DataContext = DataContext }
                : new PlaceholderView();
        }
    }
}
