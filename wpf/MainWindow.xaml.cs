// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主主窗口外壳：标题栏 / NavRail / 内容宿主 / 模式切换

using System;
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
        private readonly GameMode gameMode;
        private readonly PolicyPageViewModel policyVm;
        private readonly LibraryViewModel libraryVm;
        private readonly LogViewModel logVm;
        private readonly AboutViewModel aboutVm;
        private readonly SettingsViewModel settingsVm;
        private readonly AntiCheatViewModel antiCheatVm;
        private readonly EnvironmentViewModel environmentVm;
        private readonly GraphicsViewModel graphicsVm;
        private readonly Tamer tamer;

        public MainWindow() : this(null) { }

        internal MainWindow(GameMode gm)
        {
            InitializeComponent();
            source = new SampleOverviewSource();
            vm = new OverviewViewModel(source);
            vm.Refresh();
            gameMode = gm ?? new GameMode(Paths.Data, new SuppressionCore());
            policyVm = new PolicyPageViewModel(gameMode);
            libraryVm = new LibraryViewModel(gameMode);
            libraryVm.Refresh();
            logVm = new LogViewModel();
            logVm.Refresh();
            aboutVm = new AboutViewModel();
            // 单一 Tamer 实例供设置页（一键恢复）与反作弊页共用。
            tamer = new Tamer(new SuppressionCore());
            settingsVm = new SettingsViewModel(gameMode, tamer);
            antiCheatVm = new AntiCheatViewModel(tamer);
            antiCheatVm.BuildCards();
            environmentVm = new EnvironmentViewModel(gameMode);
            environmentVm.BuildToggles();
            graphicsVm = new GraphicsViewModel(gameMode);
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
            policyVm.RefreshLocks();
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
            gameMode.Preset = ModeController.ToPreset(mode);
            policyVm.RefreshLocks();
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
            if (rb == NavOverview)
                PageHost.Content = new OverviewView { DataContext = DataContext };
            else if (rb == NavPolicy)
                PageHost.Content = new PolicyView { DataContext = policyVm };
            else if (rb == NavLibrary)
                PageHost.Content = new LibraryView { DataContext = libraryVm };
            else if (rb == NavAntiCheat)
            {
                antiCheatVm.RefreshStatus();
                PageHost.Content = new AntiCheatView { DataContext = antiCheatVm };
            }
            else if (rb == NavGraphics)
                PageHost.Content = new GraphicsView { DataContext = graphicsVm };
            else if (rb == NavEnvironment)
            {
                environmentVm.RefreshStatus();
                PageHost.Content = new EnvironmentView { DataContext = environmentVm };
            }
            else if (rb == NavLog)
            {
                logVm.Refresh();
                PageHost.Content = new LogView { DataContext = logVm };
            }
            else if (rb == NavSettings)
                PageHost.Content = new SettingsView { DataContext = settingsVm };
            else if (rb == NavAbout)
                PageHost.Content = new AboutView { DataContext = aboutVm };
            else
                PageHost.Content = new PlaceholderView();
        }

        // 截图探针：离屏渲染前切到策略页
        internal void NavigateToPolicyForShot()
        {
            PageHost.Content = new PolicyView { DataContext = policyVm };
            UpdateLayout();
        }

        // 截图探针：离屏渲染前切到游戏库页
        internal void NavigateToLibraryForShot()
        {
            PageHost.Content = new LibraryView { DataContext = libraryVm };
            UpdateLayout();
        }
    }
}
