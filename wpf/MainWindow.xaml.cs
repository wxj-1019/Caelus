// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主主窗口外壳：标题栏 / NavRail / 内容宿主 / 模式切换

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaelusApp.WpfHost.Views;

namespace CaelusApp.WpfHost
{
    public partial class MainWindow : Window
    {
        private readonly SampleOverviewSource source;
        private readonly OverviewViewModel vm;

        public MainWindow() : this(null) { }

        internal MainWindow(IOverviewSource overviewSource)
        {
            InitializeComponent();
            source = overviewSource as SampleOverviewSource ?? new SampleOverviewSource();
            vm = new OverviewViewModel(source);
            vm.Refresh();
            DataContext = vm;
            PageHost.Content = new OverviewView { DataContext = vm };
            Loaded += OnLoadedAmbient;
        }

        // 启动时应用持久化模式的主题与氛围（无动画）
        internal void ApplyPersistedMode(AppMode mode)
        {
            if (mode == AppMode.Competitive) SegCompetitive.IsChecked = true;
            else if (mode == AppMode.Custom) SegCustom.IsChecked = true;
            else SegStandard.IsChecked = true;
            source.SetMode(mode);
            vm.Refresh();
        }

        private void OnLoadedAmbient(object sender, RoutedEventArgs e)
        {
            Ambient.Show();
        }

        private void ModeChecked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            AppMode mode = sender == SegCompetitive ? AppMode.Competitive
                : sender == SegCustom ? AppMode.Custom : AppMode.Standard;
            ModeController.SwitchTo(Application.Current, mode, Ambient, source, vm, true);
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
