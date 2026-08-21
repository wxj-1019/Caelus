// @author zenjiro 18967498922@163.com
// 文件用途 WPF 预览宿主主窗口外壳：标题栏 / 工作区导航 / 内容宿主 / 模式切换
//           合并远端运行时注入/托盘关闭/窗口图标能力 + 三场景调度前端

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using CaelusApp.WpfHost.Views;

namespace CaelusApp.WpfHost
{
    public partial class MainWindow : Window
    {
        private readonly ScenarioStatusSource source;
        private readonly ScenarioOverviewViewModel vm;
        private readonly ScenarioDetailViewModel devDetailVm;
        private readonly ScenarioDetailViewModel dailyDetailVm;
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
        private readonly System.Windows.Threading.DispatcherTimer refreshTimer;

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
        private readonly ScenarioDetailView devFocusView;
        private readonly ScenarioDetailView dailyCareView;

        // 托盘"退出"时置 true，允许真正关闭；平时点 X 只隐藏到托盘
        public bool RealExit;

        public MainWindow() : this(null, null, null, null) { }

        // 启动期间由 App 注入的分块泵送钩子：构建主窗口时让启动屏动画保持滚动
        internal static Action ProgressPump;

        private static void Pump()
        {
            Action p = ProgressPump;
            if (p != null) p();
        }

        internal MainWindow(GameMode gm) : this(gm, null, null, null) { }

        internal MainWindow(GameMode gm, Tamer runtimeTamer, ScenarioStatusSource runtimeSource)
            : this(gm, runtimeTamer, runtimeSource, null) { }

        internal MainWindow(GameMode gm, Tamer runtimeTamer, ScenarioStatusSource runtimeSource,
            DevFocus runtimeDevFocus)
        {
            InitializeComponent();
            Pump();
            // 正式运行时注入真实数据源与 Tamer/DevFocus；截图/压力探针无注入时回退只读场景探测
            gameMode = gm ?? new GameMode(Paths.Data, new SuppressionCore());
            source = runtimeSource ?? new ScenarioStatusSource(gameMode);
            vm = new ScenarioOverviewViewModel(source);
            devDetailVm = new ScenarioDetailViewModel(source, ScenarioKind.DevFocus);
            dailyDetailVm = new ScenarioDetailViewModel(source, ScenarioKind.DailyCare);
            vm.Refresh();
            Pump();
            policyVm = new PolicyPageViewModel(gameMode);
            libraryVm = new LibraryViewModel(gameMode);
            libraryVm.Refresh();
            logVm = new LogViewModel();
            logVm.Refresh();
            Pump();
            aboutVm = new AboutViewModel();
            tamer = runtimeTamer ?? new Tamer(new SuppressionCore());
            settingsVm = new SettingsViewModel(gameMode, tamer, runtimeDevFocus);
            antiCheatVm = new AntiCheatViewModel(tamer);
            antiCheatVm.BuildCards();
            Pump();
            environmentVm = new EnvironmentViewModel(gameMode);
            environmentVm.BuildToggles();
            Pump();
            graphicsVm = new GraphicsViewModel(gameMode);
            auditVm = new AuditViewModel();
            whitelistVm = new WhitelistViewModel(gameMode);

            Pump();
            overviewView = new OverviewView { DataContext = vm };
            overviewView.ModePicked += ModeChecked;
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
            devFocusView = new ScenarioDetailView { DataContext = devDetailVm };
            dailyCareView = new ScenarioDetailView { DataContext = dailyDetailVm };
            Pump();

            // 截图探针：注入“游戏掌权 / 开发活跃待命 / 日常待机”的完整三场景构图
            if (OverviewView.InjectSampleData || ScenarioDetailView.InjectSampleData)
                source.SetDemo(true, true, false);

            DataContext = vm;
            PageHost.Content = overviewView;
            Motion.PolicyChanged += OnMotionPolicyChanged;
            // 主题/模式换槽统一过渡：ModeChanged 在每次 ThemeManager.Apply（模式或深浅主题）后触发，
            // 在此集中 CrossFade 内容，替代各调用点的零散 CrossFade（含深浅主题切换这一原缺口）。
            ThemeManager.ModeChanged += OnThemeChanged;

            // 任务栏 / Alt-Tab 图标（从 IconArt 运行时生成，与托盘图标同一套视觉）
            Icon = WindowIcon.Create();

            // 运行时状态轮询：概览结论/指标与策略锁随压制、模式、场景变化刷新
            refreshTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            refreshTimer.Tick += delegate
            {
                try { vm.Refresh(); } catch { }
                try { policyVm.RefreshLocks(); } catch { }
            };
            refreshTimer.Start();
        }

        // 托盘开关改动后回刷界面各页
        internal void SyncAllToggles()
        {
            try { vm.Refresh(); } catch { }
            try { policyVm.RefreshLocks(); } catch { }
            try { antiCheatVm.RefreshStatus(); } catch { }
        }

        internal void NotifyLibraryChanged()
        {
            try { libraryVm.Refresh(); } catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!RealExit)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            try { refreshTimer.Stop(); } catch { }
        }

        private void OnThemeChanged(object sender, System.EventArgs e)
        {
            if (!IsLoaded) return;
            FrameworkElement current = PageHost.Content as FrameworkElement;
            if (current != null) Motion.CrossFade(current);
        }

        internal void ApplyPersistedMode(AppMode mode)
        {
            int index = mode == AppMode.Competitive ? 1
                : mode == AppMode.Custom ? 2 : 0;
            overviewView.SetModeSelection(index);
            source.SetMode(mode);
            vm.SetMode(mode);
            policyVm.RefreshLocks();
        }

        private void ModeChecked(object sender, int index)
        {
            if (!IsLoaded) return;
            AppMode mode = index == 1 ? AppMode.Competitive
                : index == 2 ? AppMode.Custom : AppMode.Standard;
            ModeController.SwitchTo(Application.Current, mode, source, vm);
            gameMode.Preset = ModeController.ToPreset(mode);
            policyVm.RefreshLocks();
            // 内容 CrossFade 由 OnThemeChanged 集中处理（ModeChanged 在 SwitchTo→Apply 后触发）
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

        // 消除无边框窗口顶部残留的系统非客户区：浅色 Windows 下 DWM 会在标题栏上方
        // 画一条 6px 左右的系统主题色条（白条）。把客户区扩展至整个窗口即可移除。
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (source != null) source.AddHook(WndProc);
            // Win11 22H2+：窗口圆角（DWMWCP_ROUND）+ Mica 主窗口材质（DWMSBT_MAINWINDOW）。
            // 两者都要求 AllowsTransparency=False（layered 窗口不参与 DWM 形状与材质）。
            ApplyWindowChrome(hwnd);
            // 跟随系统深浅模式：挂 WM_SETTINGCHANGE 监听（500ms 去抖）
            ThemeManager.StartSystemThemeMonitor(Application.Current, hwnd);
        }

        private void OnMotionPolicyChanged(object sender, EventArgs e)
        {
            if (!IsInitialized) return;
            ApplyWindowChrome(new WindowInteropHelper(this).Handle);
        }

        private static void ApplyWindowChrome(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int corner = 2; // DWMWCP_ROUND：系统级圆角，最大化时 DWM 自动方角
                DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
                int backdrop = Motion.HighContrast ? 1 : 2; // DWMSBT_NONE / DWMSBT_MAINWINDOW
                DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            Motion.PolicyChanged -= OnMotionPolicyChanged;
            ThemeManager.ModeChanged -= OnThemeChanged;
            ThemeManager.StopSystemThemeMonitor();
            if (source != null) source.Dispose();
            base.OnClosed(e);
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Point { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Win32Point ptReserved;
            public Win32Point ptMaxSize;
            public Win32Point ptMaxPosition;
            public Win32Point ptMinTrackSize;
            public Win32Point ptMaxTrackSize;
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0083) // WM_NCCALCSIZE：客户区 = 全窗口，去掉非客户区
            {
                handled = true;
                return IntPtr.Zero;
            }
            if (msg == 0x0086) // WM_NCACTIVATE：阻止失活/激活时系统重绘非客户区
            {
                handled = true;
                return new IntPtr(1);
            }
            if (msg == 0x0084) // WM_NCHITTEST：AllowsTransparency 后系统不再提供边缘 resize，手动补
            {
                int x = (short)(lParam.ToInt32() & 0xFFFF);
                int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                Win32Point pt = ScreenToClient(hwnd, x, y);
                const int edge = 6;
                bool left = pt.X <= edge, right = pt.X >= 0 && pt.X >= ClientWidth(hwnd) - edge;
                bool top = pt.Y <= edge, bottom = pt.Y >= 0 && pt.Y >= ClientHeight(hwnd) - edge;
                int hit = 0;
                if (top && left) hit = 13;        // HTTOPLEFT
                else if (top && right) hit = 14;  // HTTOPRIGHT
                else if (bottom && left) hit = 16; // HTBOTTOMLEFT
                else if (bottom && right) hit = 17; // HTBOTTOMRIGHT
                else if (top) hit = 12;           // HTTOP
                else if (bottom) hit = 15;        // HTBOTTOM
                else if (left) hit = 10;          // HTLEFT
                else if (right) hit = 11;         // HTRIGHT
                if (hit != 0)
                {
                    handled = true;
                    return new IntPtr(hit);
                }
                return IntPtr.Zero;
            }
            if (msg == 0x0024) // WM_GETMINMAXINFO：最大化不超出工作区（AllowsTransparency 的 WPF bug）
            {
                MinMaxInfo mmi = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
                Rect wa = SystemParameters.WorkArea;
                mmi.ptMaxSize.X = (int)wa.Width;
                mmi.ptMaxSize.Y = (int)wa.Height;
                mmi.ptMaxPosition.X = (int)wa.Left;
                mmi.ptMaxPosition.Y = (int)wa.Top;
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        private static Win32Point ScreenToClient(IntPtr hwnd, int screenX, int screenY)
        {
            Win32Rect r;
            GetWindowRect(hwnd, out r);
            Win32Point pt;
            pt.X = screenX - r.Left;
            pt.Y = screenY - r.Top;
            return pt;
        }

        private static int ClientWidth(IntPtr hwnd)
        {
            Win32Rect r;
            GetWindowRect(hwnd, out r);
            return r.Right - r.Left;
        }

        private static int ClientHeight(IntPtr hwnd)
        {
            Win32Rect r;
            GetWindowRect(hwnd, out r);
            return r.Bottom - r.Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out Win32Rect rect);

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
            bool max = WindowState == WindowState.Maximized;
            MaxIcon.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
            RestoreIcon.Visibility = max ? Visibility.Visible : Visibility.Collapsed;
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
            else if (rb == NavDevFocus) next = devFocusView;
            else if (rb == NavDailyCare) next = dailyCareView;
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
            overviewView.SetModeSelection(index);
            ThemeManager.Apply(Application.Current, ThemeManager.CurrentTone, mode);
            source.SetMode(mode);
            vm.SetMode(mode);
            policyVm.RefreshLocks();
            // 内容 CrossFade 由 OnThemeChanged 集中处理
        }

        internal FrameworkElement NavigateToForShot(string page)
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
                : page == "dev" ? NavDevFocus
                : page == "daily" ? NavDailyCare
                : page == "about" ? NavAbout : NavOverview;
            target.IsChecked = true;
            UpdateLayout();
            return PageHost.Content as FrameworkElement;
        }
    }
}
