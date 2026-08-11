// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主主窗口外壳：标题栏 / 工作区导航 / 内容宿主 / 模式切换

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
        private readonly AuditViewModel auditVm;
        private readonly WhitelistViewModel whitelistVm;
        private readonly Tamer tamer;

        private readonly OverviewView overviewView;
        private readonly PolicyView policyView;
        private readonly LibraryView libraryView;
        private readonly LogView logView;
        private readonly AboutView aboutView;
        private readonly SettingsView settingsView;
        private readonly AntiCheatView antiCheatView;
        private readonly EnvironmentView environmentView;
        private readonly GraphicsView graphicsView;
        private readonly AuditView auditView;
        private readonly WhitelistView whitelistView;

        public MainWindow() : this(null) { }

        internal MainWindow(GameMode gm)
        {
            InitializeComponent();
            ModePicker.ItemsSource = new System.Collections.Generic.List<string>
            {
                "常规", "竞技", "自定义"
            };
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
            tamer = new Tamer(new SuppressionCore());
            settingsVm = new SettingsViewModel(gameMode, tamer);
            antiCheatVm = new AntiCheatViewModel(tamer);
            antiCheatVm.BuildCards();
            environmentVm = new EnvironmentViewModel(gameMode);
            environmentVm.BuildToggles();
            graphicsVm = new GraphicsViewModel(gameMode);
            auditVm = new AuditViewModel();
            whitelistVm = new WhitelistViewModel(gameMode);

            overviewView = new OverviewView { DataContext = vm };
            policyView = new PolicyView { DataContext = policyVm };
            libraryView = new LibraryView { DataContext = libraryVm };
            logView = new LogView { DataContext = logVm };
            aboutView = new AboutView { DataContext = aboutVm };
            settingsView = new SettingsView { DataContext = settingsVm };
            antiCheatView = new AntiCheatView { DataContext = antiCheatVm };
            environmentView = new EnvironmentView { DataContext = environmentVm };
            graphicsView = new GraphicsView { DataContext = graphicsVm };
            auditView = new AuditView { DataContext = auditVm };
            whitelistView = new WhitelistView { DataContext = whitelistVm };

            DataContext = vm;
            PageHost.Content = overviewView;
        }

        internal void ApplyPersistedMode(AppMode mode)
        {
            ModePicker.SelectedIndex = mode == AppMode.Competitive ? 1
                : mode == AppMode.Custom ? 2 : 0;
            source.SetMode(mode);
            vm.Refresh();
            policyVm.RefreshLocks();
        }

        private void ModeChecked(object sender, int index)
        {
            if (!IsLoaded) return;
            AppMode mode = index == 1 ? AppMode.Competitive
                : index == 2 ? AppMode.Custom : AppMode.Standard;
            ModeController.SwitchTo(Application.Current, mode, Ambient, source, vm, true);
            gameMode.Preset = ModeController.ToPreset(mode);
            policyVm.RefreshLocks();
            FrameworkElement current = PageHost.Content as FrameworkElement;
            if (current != null) Motion.CrossFade(current);
        }

        private void TitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2)
            {
                ToggleMaximized();
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void MinClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxClick(object sender, RoutedEventArgs e)
        {
            ToggleMaximized();
        }

        private void ToggleMaximized()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
            MaxBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void NavChecked(object sender, RoutedEventArgs e)
        {
            RadioButton rb = sender as RadioButton;
            if (rb == null || PageHost == null || overviewView == null) return;
            FrameworkElement next = null;
            if (rb == NavOverview) next = overviewView;
            else if (rb == NavPolicy) next = policyView;
            else if (rb == NavLibrary) next = libraryView;
            else if (rb == NavAntiCheat)
            {
                antiCheatVm.RefreshStatus();
                next = antiCheatView;
            }
            else if (rb == NavGraphics) next = graphicsView;
            else if (rb == NavEnvironment)
            {
                environmentVm.RefreshStatus();
                next = environmentView;
            }
            else if (rb == NavAudit) next = auditView;
            else if (rb == NavWhitelist) next = whitelistView;
            else if (rb == NavLog)
            {
                logVm.Refresh();
                next = logView;
            }
            else if (rb == NavSettings) next = settingsView;
            else if (rb == NavAbout) next = aboutView;
            if (next == null || PageHost.Content == next) return;
            PageHost.IsHitTestVisible = false;
            PageHost.Content = next;
            PageHost.IsHitTestVisible = true;
            Motion.Reveal(next);
        }

        internal void NavigateToPolicyForShot()
        {
            NavigateToForShot("policy");
        }

        internal void NavigateToLibraryForShot()
        {
            NavigateToForShot("library");
        }

        internal void SwitchModeForStress(AppMode mode)
        {
            int index = mode == AppMode.Competitive ? 1 : mode == AppMode.Custom ? 2 : 0;
            ModePicker.SelectedIndex = index;
            ThemeManager.Apply(Application.Current, ThemeManager.CurrentTone, mode);
            source.SetMode(mode);
            vm.Refresh();
            policyVm.RefreshLocks();
            FrameworkElement current = PageHost.Content as FrameworkElement;
            if (current != null) Motion.CrossFade(current);
        }

        internal void NavigateToForShot(string page)
        {
            RadioButton target = page == "library" ? NavLibrary
                : page == "policy" ? NavPolicy
                : page == "graphics" ? NavGraphics
                : page == "anticheat" ? NavAntiCheat
                : page == "environment" ? NavEnvironment
                : page == "whitelist" ? NavWhitelist
                : page == "audit" ? NavAudit
                : page == "log" ? NavLog
                : page == "settings" ? NavSettings
                : page == "about" ? NavAbout : NavOverview;
            target.IsChecked = true;
            UpdateLayout();
        }
    }
}
