// @author zenjiro 18967498922@163.com
// 文件用途 WPF 设置页：应用偏好开关 + Defender 排除与 LOL 附加层维护工作流

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CaelusApp.WpfHost.Controls;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp.WpfHost.Views
{
    public partial class SettingsView : UserControl
    {
        private static volatile bool shaderCleaning;

        public SettingsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneSummary, 90);
            Motion.RiseIn(ZoneApp, 140);
            Motion.RiseIn(ZoneDev, 190);
            Motion.RiseIn(ZoneDaily, 215);
            Motion.RiseIn(ZoneTheme, 225);
            Motion.RiseIn(ZoneMaint, 250);
            Motion.RiseIn(ZoneDanger, 300);
            InitTonePicker();
            InitAccentSwatches();
        }

        // —— 深浅模式三态 ——

        private void InitTonePicker()
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            TonePicker.ItemsSource = new System.Collections.Generic.List<string>
            {
                Lang.T("set.theme.tone.dark"),
                Lang.T("set.theme.tone.light"),
                Lang.T("set.theme.tone.follow"),
            };
            TonePicker.SetCurrentValue(SegmentedControl.SelectedIndexProperty, vm.ToneMode);
        }

        private void OnToneModeChanged(object sender, int index)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            vm.ToneMode = index;
            vm.ApplyToneFromSetting();
            Motion.Emphasize(PageFeedbackBanner);
        }

        // —— 强调色色板 ——

        private void InitAccentSwatches()
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            PopulateSwatch(SwatchStandard, "AccentStandard", vm.AccentStandardDisplay);
            PopulateSwatch(SwatchCompetitive, "AccentCompetitive", vm.AccentCompetitiveDisplay);
            PopulateSwatch(SwatchCustom, "AccentCustom", vm.AccentCustomDisplay);
            // 设置 hex 输入框占位
            HexStandard.Text = vm.AccentStandardDisplay ?? "";
            HexCompetitive.Text = vm.AccentCompetitiveDisplay ?? "";
            HexCustom.Text = vm.AccentCustomDisplay ?? "";
        }

        private void PopulateSwatch(StackPanel target, string modeKey, string currentHex)
        {
            target.Children.Clear();
            bool hasCustom = !string.IsNullOrEmpty(currentHex);
            for (int i = 0; i < SettingsViewModel.AccentPresetColors.Count; i++)
            {
                string hex = SettingsViewModel.AccentPresetColors[i];
                byte r, g, b;
                if (!AccentMath.ParseHex(hex, out r, out g, out b)) continue;
                var circle = new Border
                {
                    Width = 26, Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand,
                    Tag = new AccentSwatchTag { ModeKey = modeKey, Hex = hex },
                    ToolTip = hex,
                    Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
                };
                // 选中态：非自定义且匹配默认预设色时描边
                bool isSelected = !hasCustom && i == PresetIndexForMode(modeKey);
                if (isSelected || (hasCustom && string.Equals(hex, currentHex, StringComparison.OrdinalIgnoreCase)))
                {
                    circle.BorderBrush = new SolidColorBrush(Colors.White);
                    circle.BorderThickness = new Thickness(2.5);
                }
                else
                {
                    circle.BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0));
                    circle.BorderThickness = new Thickness(1);
                }
                circle.MouseLeftButtonDown += OnSwatchClick;
                target.Children.Add(circle);
            }
        }

        private static int PresetIndexForMode(string modeKey)
        {
            // 常规默认靛蓝(0)、竞技默认蜜桃橙(1)、自定义默认暗金(2)
            if (modeKey == "AccentCompetitive") return 1;
            if (modeKey == "AccentCustom") return 2;
            return 0;
        }

        private sealed class AccentSwatchTag
        {
            public string ModeKey;
            public string Hex;
        }

        private void OnSwatchClick(object sender, MouseButtonEventArgs e)
        {
            Border circle = sender as Border;
            AccentSwatchTag tag = circle == null ? null : circle.Tag as AccentSwatchTag;
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (tag == null || vm == null) return;
            vm.ApplyAccent(tag.ModeKey, tag.Hex);
            // 刷新色板高亮
            InitAccentSwatches();
            Motion.Emphasize(PageFeedbackBanner);
        }

        private void OnAccentSaveStandard(object sender, RoutedEventArgs e)
        {
            ApplyAccentFromHex("AccentStandard", HexStandard);
        }

        private void OnAccentSaveCompetitive(object sender, RoutedEventArgs e)
        {
            ApplyAccentFromHex("AccentCompetitive", HexCompetitive);
        }

        private void OnAccentSaveCustom(object sender, RoutedEventArgs e)
        {
            ApplyAccentFromHex("AccentCustom", HexCustom);
        }

        private void ApplyAccentFromHex(string modeKey, TextBox hexBox)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            string hex = (hexBox.Text ?? "").Trim();
            byte r, g, b;
            if (!AccentMath.ParseHex(hex, out r, out g, out b))
            {
                vm.ShowFeedback("请输入合法的十六进制色值，如 #FF8A5C 或 #F85。", "Error");
                return;
            }
            vm.ApplyAccent(modeKey, "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2"));
            InitAccentSwatches();
            Motion.Emphasize(PageFeedbackBanner);
        }

        private void OnAccentResetStandard(object sender, RoutedEventArgs e)
        {
            ResetAccentKey("AccentStandard");
        }

        private void OnAccentResetCompetitive(object sender, RoutedEventArgs e)
        {
            ResetAccentKey("AccentCompetitive");
        }

        private void OnAccentResetCustom(object sender, RoutedEventArgs e)
        {
            ResetAccentKey("AccentCustom");
        }

        private void ResetAccentKey(string modeKey)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            vm.ResetAccent(modeKey);
            InitAccentSwatches();
            Motion.Emphasize(PageFeedbackBanner);
        }

        private void OnAccentResetAll(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            if (MessageBox.Show("确定恢复三模式默认配色（靛蓝/蜜桃橙/暗金）吗？自定义强调色将被清除。",
                "Caelus", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            vm.ResetAllAccents();
            InitAccentSwatches();
            Motion.Emphasize(PageFeedbackBanner);
        }

        private void OnDevSave(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            vm.SaveDevCustom(TbDevCustom.Text);
            Motion.Emphasize(PageFeedbackBanner);
        }

        private void OnDistractSave(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            vm.SaveDistract(TbDistract.Text);
            Motion.Emphasize(PageFeedbackBanner);
        }

        private void OnDevSvcSave(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            vm.SaveDevSvc(TbDevSvc.Text);
            Motion.Emphasize(PageFeedbackBanner);
        }

        private void OnHealthFreqSave(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            vm.SaveHealthFreq(TbHealthFreq.Text);
            Motion.Emphasize(PageFeedbackBanner);
        }

        private void OnDevEnvRun(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null) return;
            // 工具链版本探测可能耗时数秒，放后台线程，完成后回 UI 线程
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result = vm.RunDevEnvAudit();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    vm.SetDevEnvResult(result);
                    vm.ShowFeedback("开发环境体检完成。", "Success");
                }));
            });
        }

        private void OnRestore(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm == null || vm.IsRestoreBusy) return;
            string ask = "确定恢复所有已记录的系统项吗？\r\n\r\n这会退出当前优化状态，并尝试撤销 Caelus 记录的相关修改。";
            if (MessageBox.Show(ask, CaelusApp.App.DisplayName, MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            vm.IsRestoreBusy = true;
            vm.ShowFeedback("正在恢复所有已记录项，请勿关闭应用。", "Info");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool completed; int failed; int attempted;
                vm.RestoreAll(out completed, out failed, out attempted);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    vm.IsRestoreBusy = false;
                    string message = Lang.T(completed ? "panic.done" : "panic.timeout");
                    if (!completed) message += " " + Lang.F("panic.failedcount", failed, attempted);
                    vm.ShowFeedback(message, completed ? "Success" : "Warning");
                    Motion.Emphasize(PageFeedbackBanner);
                    // 与旧 WinForms 一致：恢复完成后全局回刷各页开关
                    Window w = Window.GetWindow(this);
                    MainWindow mw = w as MainWindow;
                    if (mw != null) mw.SyncAllToggles();
                }));
            });
        }

        private GameMode CurrentGameMode()
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            return vm == null ? null : vm.GameMode;
        }

        private void OnDefender(object sender, RoutedEventArgs e)
        {
            GameMode gameMode = CurrentGameMode();
            if (gameMode == null)
            {
                MessageBox.Show(Lang.T("def.unavailable"), CaelusApp.App.DisplayName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dialog = new DefenderExclusionDialogWpf(gameMode);
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

        private void OnAddon(object sender, RoutedEventArgs e)
        {
            GameMode gameMode = CurrentGameMode();
            var roots = new List<string>();
            if (gameMode != null)
            {
                foreach (GameProfile profile in gameMode.GetProfiles())
                    if (!string.IsNullOrEmpty(profile.Root)) roots.Add(profile.Root);
            }
            var dialog = new LolAddonDialogWpf(roots);
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

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
            shaderCleaning = true;
            vm.IsShaderBusy = true;
            vm.ShaderStatus = Lang.T("shader.busy");
            vm.ShowFeedback("正在清理着色器缓存，请稍候。", "Info");
            ThreadPool.QueueUserWorkItem(delegate
            {
                CacheSweep.Result cr = null;
                long left = 0;
                string failure = null;
                try
                {
                    cr = ShaderCache.Clean();
                    left = ShaderCache.MeasureBytes();
                    Logger.Log("着色器缓存清理：释放 " + CacheSweep.FmtBytes(cr.FreedBytes)
                        + (cr.FailedFiles > 0 ? "，" + cr.FailedFiles + " 个文件被占用已跳过" : ""));
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                    Logger.LogFailure("WPF 着色器缓存清理", ex);
                }
                finally { shaderCleaning = false; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    vm.IsShaderBusy = false;
                    if (cr == null)
                    {
                        vm.ShaderStatus = Lang.T("shader.note");
                        vm.ShowFeedback("着色器缓存清理失败：" + (failure ?? "未知错误"), "Error");
                    }
                    else
                    {
                        vm.ShaderStatus = CacheSweep.FmtBytes(left);
                        string msg = Lang.F("shader.freed", CacheSweep.FmtBytes(cr.FreedBytes))
                            + (cr.FailedFiles > 0 ? " " + Lang.F("shader.skip", cr.FailedFiles) : "");
                        vm.ShowFeedback(msg, cr.FailedFiles > 0 ? "Warning" : "Success");
                    }
                    Motion.Emphasize(PageFeedbackBanner);
                }));
            });
        }
    }
}

namespace CaelusApp.WpfHost.Dialogs
{
    internal static class DialogUi
    {
        internal static void Style(FrameworkElement element, string key)
        {
            element.SetResourceReference(FrameworkElement.StyleProperty, key);
        }

        internal static Brush Brush(string key, Brush fallback)
        {
            object value = Application.Current == null ? null : Application.Current.TryFindResource(key);
            return value as Brush ?? fallback;
        }

        internal static TextBlock Text(string value, double size, Brush brush)
        {
            return new TextBlock
            {
                Text = value,
                FontSize = size,
                Foreground = brush,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        internal static Binding Bind(string path)
        {
            return new Binding(path) { Mode = BindingMode.OneWay };
        }
    }

    internal partial class DefenderExclusionDialogWpf : Window
    {
        private sealed class Row : INotifyPropertyChanged
        {
            private bool excluded;
            private string stateText;
            private Brush stateBrush;

            public string Name { get; set; }
            public string Root { get; set; }
            internal bool Owned { get; set; }
            public string AutomationName { get { return Name + " Defender 排除"; } }
            public bool Excluded
            {
                get { return excluded; }
                set { excluded = value; Raise("Excluded"); }
            }
            public string StateText
            {
                get { return stateText; }
                set { stateText = value; Raise("StateText"); }
            }
            public Brush StateBrush
            {
                get { return stateBrush; }
                set { stateBrush = value; Raise("StateBrush"); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void Raise(string name)
            {
                PropertyChangedEventHandler changed = PropertyChanged;
                if (changed != null) changed(this, new PropertyChangedEventArgs(name));
            }
        }

        private readonly GameMode gameMode;
        private readonly ObservableCollection<Row> rows = new ObservableCollection<Row>();
        private ItemsControl rowsHost;
        private TextBlock status;
        private ProgressBar progress;
        private Button clearAll;
        private Button refresh;
        private int busy;
        private bool closed;

        internal DefenderExclusionDialogWpf(GameMode mode)
        {
            gameMode = mode;
            if (!TryLoadMarkup()) BuildWindow();
            Loaded += delegate
            {
                FrameworkElement content = Content as FrameworkElement;
                if (content != null) Motion.Reveal(content);
                RefreshState();
            };
        }

        private bool TryLoadMarkup()
        {
            MethodInfo initializer = GetType().GetMethod(
                "InitializeComponent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (initializer == null) return false;
            try
            {
                initializer.Invoke(this, null);
                rowsHost = FindName("RowsHost") as ItemsControl;
                status = FindName("LblStatus") as TextBlock;
                progress = FindName("BusyIndicator") as ProgressBar;
                clearAll = FindName("BtnClearAll") as Button;
                refresh = FindName("BtnRefresh") as Button;
                if (rowsHost == null || status == null || progress == null
                    || clearAll == null || refresh == null) return false;
                rowsHost.ItemsSource = rows;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogFailure("WPF Defender 对话框 XAML 加载", ex);
                Content = null;
                return false;
            }
        }

        private void BuildWindow()
        {
            Title = Lang.T("def.title");
            Width = 720; Height = 620; MinWidth = 600; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            SetResourceReference(BackgroundProperty, "BackgroundBrush");
            SetResourceReference(FontFamilyProperty, "FontUi");
            AutomationProperties.SetName(this, Lang.T("def.title"));
            PreviewKeyDown += OnWindowKeyDown;

            Grid root = new Grid { Margin = new Thickness(24, 20, 24, 20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            TextBlock title = DialogUi.Text(Lang.T("def.title"), 24, DialogUi.Brush("TextPrimaryBrush", Brushes.Black));
            DialogUi.Style(title, "PageHeader");
            TextBlock subtitle = DialogUi.Text("按游戏目录管理实时扫描排除；只移除由 Caelus 添加的项目", 11,
                DialogUi.Brush("TextSecondaryBrush", Brushes.Gray));
            DialogUi.Style(subtitle, "PageSubtitle"); subtitle.Margin = new Thickness(0, 4, 0, 0);
            header.Children.Add(title); header.Children.Add(subtitle);
            Grid.SetRow(header, 0); root.Children.Add(header);

            Border warning = new Border { Margin = new Thickness(0, 0, 0, 12) };
            DialogUi.Style(warning, "SettingsGroup");
            warning.SetResourceReference(Border.BorderBrushProperty, "DangerBrush");
            Border warningRow = new Border(); DialogUi.Style(warningRow, "PolicyRow");
            StackPanel warningText = new StackPanel();
            warningText.Children.Add(DialogUi.Text(Lang.T("def.warn.title"), 13,
                DialogUi.Brush("DangerBrush", Brushes.Firebrick)));
            TextBlock body = DialogUi.Text(Lang.T("def.warn.body"), 11,
                DialogUi.Brush("TextSecondaryBrush", Brushes.Gray));
            body.Margin = new Thickness(0, 5, 0, 0); warningText.Children.Add(body);
            warningRow.Child = warningText; warning.Child = warningRow;
            Grid.SetRow(warning, 1); root.Children.Add(warning);

            Border statusBanner = new Border { Margin = new Thickness(0, 0, 0, 12) };
            DialogUi.Style(statusBanner, "StatusBanner");
            DockPanel statusDock = new DockPanel();
            progress = new ProgressBar { Width = 92, Height = 4, IsIndeterminate = true,
                Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(progress, Dock.Right);
            status = DialogUi.Text("正在读取 Defender 状态…", 11,
                DialogUi.Brush("TextSecondaryBrush", Brushes.Gray));
            AutomationProperties.SetName(status, "Defender 状态");
            statusDock.Children.Add(progress); statusDock.Children.Add(status);
            statusBanner.Child = statusDock; Grid.SetRow(statusBanner, 2); root.Children.Add(statusBanner);

            rowsHost = new ItemsControl();
            rowsHost.ItemsSource = rows;
            rowsHost.ItemTemplate = BuildRowTemplate();
            AutomationProperties.SetName(rowsHost, "游戏目录排除列表");
            ScrollViewer scroll = new ScrollViewer { Content = rowsHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Border listBorder = new Border { Child = scroll };
            DialogUi.Style(listBorder, "SettingsGroup");
            Grid.SetRow(listBorder, 3); root.Children.Add(listBorder);

            Grid actions = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            clearAll = Button(Lang.T("def.clearall"), "DangerButton", OnClearAll,
                "取消所有由 Caelus 添加的 Defender 排除");
            refresh = Button("刷新", "GhostButton", OnRefresh, "刷新 Defender 排除状态");
            refresh.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(refresh, 2);
            Button close = Button(Lang.T("notes.close"), "PrimaryButton", OnClose,
                "关闭 Defender 扫描排除");
            close.Margin = new Thickness(8, 0, 0, 0); close.IsCancel = true; close.IsDefault = true;
            Grid.SetColumn(close, 3);
            actions.Children.Add(clearAll); actions.Children.Add(refresh); actions.Children.Add(close);
            Grid.SetRow(actions, 4); root.Children.Add(actions);
            Content = root;
        }

        private DataTemplate BuildRowTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetResourceReference(FrameworkElement.StyleProperty, "PolicyRow");
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(DockPanel));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, DialogUi.Bind("Name"));
            name.SetValue(TextBlock.FontSizeProperty, 12.0);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            FrameworkElementFactory path = new FrameworkElementFactory(typeof(TextBlock));
            path.SetBinding(TextBlock.TextProperty, DialogUi.Bind("Root"));
            path.SetValue(TextBlock.FontSizeProperty, 10.0);
            path.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
            path.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            panel.AppendChild(name); panel.AppendChild(path);
            FrameworkElementFactory state = new FrameworkElementFactory(typeof(TextBlock));
            state.SetBinding(TextBlock.TextProperty, DialogUi.Bind("StateText"));
            state.SetBinding(TextBlock.ForegroundProperty, DialogUi.Bind("StateBrush"));
            state.SetValue(DockPanel.DockProperty, Dock.Right);
            state.SetValue(TextBlock.MarginProperty, new Thickness(14, 0, 12, 0));
            state.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            FrameworkElementFactory toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetResourceReference(FrameworkElement.StyleProperty, "PolicyToggle");
            toggle.SetBinding(ToggleButton.IsCheckedProperty, DialogUi.Bind("Excluded"));
            toggle.SetBinding(FrameworkElement.TagProperty, new Binding());
            toggle.SetBinding(AutomationProperties.NameProperty, DialogUi.Bind("AutomationName"));
            toggle.SetValue(DockPanel.DockProperty, Dock.Right);
            toggle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            toggle.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnToggleClick));
            grid.AppendChild(toggle); grid.AppendChild(state); grid.AppendChild(panel);
            border.AppendChild(grid);
            DataTemplate template = new DataTemplate(); template.VisualTree = border;
            return template;
        }

        private static Button Button(string text, string style, RoutedEventHandler click, string automationName)
        {
            Button button = new Button { Content = text, Padding = new Thickness(14, 7, 14, 7) };
            DialogUi.Style(button, style); button.Click += click;
            AutomationProperties.SetName(button, automationName);
            return button;
        }

        private void SetBusy(bool value, string message)
        {
            progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            rowsHost.IsEnabled = !value;
            refresh.IsEnabled = !value;
            clearAll.IsEnabled = !value && rows.Count > 0;
            if (!string.IsNullOrEmpty(message)) status.Text = message;
        }

        private void RefreshState()
        {
            if (Interlocked.Exchange(ref busy, 1) != 0) return;
            SetBusy(true, "正在读取 Defender 状态…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                DefenderState state = DefenderState.Unavailable;
                List<string> system = null;
                List<string> owned = new List<string>();
                List<GameProfile> profiles = new List<GameProfile>();
                try
                {
                    state = DefenderExclusion.QueryState();
                    owned = DefenderExclusion.OwnedByCaelus();
                    if (state == DefenderState.Active)
                    {
                        system = DefenderExclusion.QuerySystem();
                        profiles = gameMode.GetProfiles();
                    }
                }
                catch (Exception ex) { Logger.LogFailure("WPF Defender 排除状态读取", ex); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Interlocked.Exchange(ref busy, 0);
                    if (closed) return;
                    RenderState(state, system, owned, profiles);
                }));
            });
        }

        private void RenderState(DefenderState state, List<string> system,
            List<string> owned, List<GameProfile> profiles)
        {
            rows.Clear();
            if (state != DefenderState.Active)
            {
                SetBusy(false, Lang.T(state == DefenderState.Disabled ? "def.off" : "def.unavailable"));
                clearAll.IsEnabled = false;
                return;
            }
            if (system == null)
            {
                SetBusy(false, Lang.T("def.unavailable")); clearAll.IsEnabled = false; return;
            }

            var seen = new List<string>();
            foreach (GameProfile profile in profiles)
            {
                string root = DefenderExclusion.Normalize(profile.Root);
                if (root.Length == 0 || DefenderExclusion.Contains(seen, root)) continue;
                seen.Add(root); AddRow(profile.Name, root, system, owned);
            }
            foreach (string path in owned)
            {
                string root = DefenderExclusion.Normalize(path);
                if (root.Length == 0 || DefenderExclusion.Contains(seen, root)) continue;
                seen.Add(root); AddRow(LeafName(root), root, system, owned);
            }
            SetBusy(false, rows.Count == 0 ? Lang.T("def.nogames") : "已读取 " + rows.Count + " 个游戏目录");
            clearAll.IsEnabled = owned.Count > 0;
        }

        private void AddRow(string name, string root, List<string> system, List<string> owned)
        {
            bool excluded = DefenderExclusion.IsExcludedInSystem(system, root);
            Row row = new Row
            {
                Name = string.IsNullOrEmpty(name) ? LeafName(root) : name,
                Root = root,
                Owned = DefenderExclusion.Contains(owned, root),
                Excluded = excluded
            };
            UpdateRow(row); rows.Add(row);
        }

        private static string LeafName(string root)
        {
            string trimmed = root.TrimEnd('\\');
            int cut = trimmed.LastIndexOf('\\');
            string leaf = cut >= 0 && cut + 1 < trimmed.Length ? trimmed.Substring(cut + 1) : trimmed;
            return leaf.Length == 0 ? root : leaf;
        }

        private static void UpdateRow(Row row)
        {
            row.StateText = Lang.T(row.Excluded ? "def.state.on" : "def.state.off");
            row.StateBrush = DialogUi.Brush(row.Excluded ? "DangerBrush" : "TextTertiaryBrush",
                row.Excluded ? Brushes.Firebrick : Brushes.Gray);
        }

        private void OnToggleClick(object sender, RoutedEventArgs e)
        {
            ToggleButton toggle = sender as ToggleButton;
            Row row = toggle == null ? null : toggle.Tag as Row;
            if (row == null) return;
            bool want = toggle.IsChecked == true;
            toggle.IsChecked = row.Excluded;
            if (busy != 0 || want == row.Excluded) return;
            if (!want && !row.Owned)
            {
                MessageBox.Show(this, Lang.T("def.notours"), CaelusApp.App.DisplayName,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (want && MessageBox.Show(this, Lang.F("def.confirm", row.Name, row.Root),
                    CaelusApp.App.DisplayName, MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            RunToggle(row, want);
        }

        private void RunToggle(Row row, bool want)
        {
            if (Interlocked.Exchange(ref busy, 1) != 0) return;
            SetBusy(true, want ? "正在添加排除…" : "正在取消排除…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = want ? DefenderExclusion.Add(row.Root) : DefenderExclusion.Remove(row.Root); }
                catch (Exception ex) { Logger.LogFailure("WPF Defender 排除修改", ex); }
                List<string> fresh = DefenderExclusion.QuerySystem();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Interlocked.Exchange(ref busy, 0);
                    if (closed) return;
                    if (fresh != null) row.Excluded = DefenderExclusion.IsExcludedInSystem(fresh, row.Root);
                    else if (ok) row.Excluded = want;
                    row.Owned = DefenderExclusion.IsOwned(row.Root);
                    UpdateRow(row);
                    SetBusy(false, ok ? (want ? "已添加排除" : "已取消排除") : Lang.T("def.failed"));
                    clearAll.IsEnabled = DefenderExclusion.OwnedByCaelus().Count > 0;
                    if (!ok) MessageBox.Show(this, Lang.T("def.failed"), CaelusApp.App.DisplayName,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }));
            });
        }

        private void OnClearAll(object sender, RoutedEventArgs e)
        {
            int count = DefenderExclusion.OwnedByCaelus().Count;
            if (count == 0)
            {
                MessageBox.Show(this, Lang.T("def.clearall.none"), CaelusApp.App.DisplayName,
                    MessageBoxButton.OK, MessageBoxImage.Information); return;
            }
            string ask = "确定取消全部 " + count + " 个由 Caelus 添加的 Defender 排除吗？\r\n\r\n手工添加的排除不会被修改。";
            if (MessageBox.Show(this, ask, CaelusApp.App.DisplayName, MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            if (Interlocked.Exchange(ref busy, 1) != 0) return;
            SetBusy(true, "正在取消全部排除…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                int removed = 0;
                try { removed = DefenderExclusion.RemoveAllOwned(); }
                catch (Exception ex) { Logger.LogFailure("WPF Defender 全部取消排除", ex); }
                List<string> fresh = DefenderExclusion.QuerySystem();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Interlocked.Exchange(ref busy, 0);
                    if (closed) return;
                    if (fresh != null)
                    {
                        foreach (Row row in rows)
                        {
                            row.Excluded = DefenderExclusion.IsExcludedInSystem(fresh, row.Root);
                            row.Owned = DefenderExclusion.IsOwned(row.Root); UpdateRow(row);
                        }
                    }
                    string message = Lang.F("def.clearall.done", removed);
                    SetBusy(false, fresh == null ? message + "\r\n" + Lang.T("def.unavailable") : message);
                    clearAll.IsEnabled = DefenderExclusion.OwnedByCaelus().Count > 0;
                    MessageBox.Show(this, message, CaelusApp.App.DisplayName,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    if (fresh == null) MessageBox.Show(this, Lang.T("def.unavailable"),
                        CaelusApp.App.DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
                }));
            });
        }

        private void OnRefresh(object sender, RoutedEventArgs e) { RefreshState(); }
        private void OnClose(object sender, RoutedEventArgs e) { Close(); }
        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.F5) { RefreshState(); e.Handled = true; }
        }
        protected override void OnClosed(EventArgs e) { closed = true; try { var w = Owner; if (w != null) w.Focus(); } catch { } base.OnClosed(e); }
    }

    internal partial class LolAddonDialogWpf : Window
    {
        private sealed class CandidateRow
        {
            public string RelativePath { get; set; }
            public string Detail { get; set; }
            public string State { get; set; }
            public Brush StateBrush { get; set; }
        }

        private readonly IList<string> hints;
        private readonly ObservableCollection<CandidateRow> candidates = new ObservableCollection<CandidateRow>();
        private TextBox pathBox;
        private TextBlock status;
        private ProgressBar progress;
        private ListBox list;
        private Button inspectButton;
        private Button browseButton;
        private Button deleteButton;
        private string resolvedRoot;
        private int busy;
        private bool closed;

        internal LolAddonDialogWpf() : this(null) { }
        internal LolAddonDialogWpf(IList<string> rootHints)
        {
            hints = rootHints;
            if (!TryLoadMarkup()) BuildWindow();
            Loaded += delegate
            {
                FrameworkElement content = Content as FrameworkElement;
                if (content != null) Motion.Reveal(content);
                AutoFill();
            };
        }

        private bool TryLoadMarkup()
        {
            MethodInfo initializer = GetType().GetMethod(
                "InitializeComponent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (initializer == null) return false;
            try
            {
                initializer.Invoke(this, null);
                pathBox = FindName("TbPath") as TextBox;
                status = FindName("LblStatus") as TextBlock;
                progress = FindName("BusyIndicator") as ProgressBar;
                list = FindName("LstCandidates") as ListBox;
                inspectButton = FindName("BtnInspect") as Button;
                browseButton = FindName("BtnBrowse") as Button;
                deleteButton = FindName("BtnDelete") as Button;
                if (pathBox == null || status == null || progress == null || list == null
                    || inspectButton == null || browseButton == null || deleteButton == null) return false;
                list.ItemsSource = candidates;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogFailure("WPF LOL 附加层对话框 XAML 加载", ex);
                Content = null;
                return false;
            }
        }

        private void BuildWindow()
        {
            Title = Lang.T("addon.title"); Width = 720; Height = 590; MinWidth = 600; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize;
            SetResourceReference(BackgroundProperty, "BackgroundBrush");
            SetResourceReference(FontFamilyProperty, "FontUi");
            AutomationProperties.SetName(this, Lang.T("addon.title"));
            AllowDrop = true; DragEnter += OnDragEnter; Drop += OnDrop; PreviewKeyDown += OnWindowKeyDown;

            Grid root = new Grid { Margin = new Thickness(24, 20, 24, 20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            TextBlock title = DialogUi.Text(Lang.T("addon.title"), 24, DialogUi.Brush("TextPrimaryBrush", Brushes.Black));
            DialogUi.Style(title, "PageHeader");
            TextBlock subtitle = DialogUi.Text("检查并删除客户端附加组件，不触碰游戏本体与登录链路", 11,
                DialogUi.Brush("TextSecondaryBrush", Brushes.Gray));
            DialogUi.Style(subtitle, "PageSubtitle"); subtitle.Margin = new Thickness(0, 4, 0, 0);
            header.Children.Add(title); header.Children.Add(subtitle); Grid.SetRow(header, 0); root.Children.Add(header);

            Border note = new Border { Margin = new Thickness(0, 0, 0, 12) }; DialogUi.Style(note, "SettingsGroup");
            Border noteRow = new Border(); DialogUi.Style(noteRow, "PolicyRow");
            noteRow.Child = DialogUi.Text(Lang.T("addon.desc"), 11,
                DialogUi.Brush("TextSecondaryBrush", Brushes.Gray));
            note.Child = noteRow; Grid.SetRow(note, 1); root.Children.Add(note);

            Border inputGroup = new Border { Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12) };
            DialogUi.Style(inputGroup, "SettingsGroup");
            Grid input = new Grid(); input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
            DialogUi.Style(pathBox, "InputBox"); pathBox.KeyDown += OnPathKeyDown;
            AutomationProperties.SetName(pathBox, "英雄联盟安装目录");
            inspectButton = ActionButton("检查", "PrimaryButton", OnInspect, "检查英雄联盟附加层");
            inspectButton.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(inspectButton, 1);
            browseButton = ActionButton("浏览…", "GhostButton", OnBrowse, "浏览英雄联盟安装目录");
            browseButton.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(browseButton, 2);
            input.Children.Add(pathBox); input.Children.Add(inspectButton); input.Children.Add(browseButton);
            inputGroup.Child = input; Grid.SetRow(inputGroup, 2); root.Children.Add(inputGroup);

            Border statusBanner = new Border { Margin = new Thickness(0, 0, 0, 12) };
            DialogUi.Style(statusBanner, "StatusBanner"); DockPanel statusDock = new DockPanel();
            progress = new ProgressBar { Width = 92, Height = 4, IsIndeterminate = true,
                Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(progress, Dock.Right);
            status = DialogUi.Text(Lang.T("addon.hint.pick"), 11,
                DialogUi.Brush("TextSecondaryBrush", Brushes.Gray));
            AutomationProperties.SetName(status, "附加层检查状态");
            statusDock.Children.Add(progress); statusDock.Children.Add(status); statusBanner.Child = statusDock;
            Grid.SetRow(statusBanner, 3); root.Children.Add(statusBanner);

            Border listBorder = new Border { Padding = new Thickness(6) }; DialogUi.Style(listBorder, "SettingsGroup");
            Grid listGrid = new Grid(); listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            TextBlock resultTitle = DialogUi.Text("检查结果", 13, DialogUi.Brush("TextPrimaryBrush", Brushes.Black));
            resultTitle.FontWeight = FontWeights.SemiBold; resultTitle.Margin = new Thickness(10, 8, 10, 6);
            list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                ItemsSource = candidates, ItemTemplate = BuildCandidateTemplate() };
            list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ListItem");
            AutomationProperties.SetName(list, "可删除的附加层项目"); Grid.SetRow(list, 1);
            listGrid.Children.Add(resultTitle); listGrid.Children.Add(list); listBorder.Child = listGrid;
            Grid.SetRow(listBorder, 4); root.Children.Add(listBorder);

            Grid actions = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button close = ActionButton(Lang.T("notes.close"), "GhostButton", OnClose, "关闭英雄联盟附加层清理");
            close.IsCancel = true; close.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(close, 1);
            deleteButton = ActionButton(Lang.T("addon.delete"), "DangerButton", OnDelete, "删除检查到的英雄联盟附加层");
            deleteButton.IsEnabled = false; deleteButton.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(deleteButton, 2);
            actions.Children.Add(close); actions.Children.Add(deleteButton); Grid.SetRow(actions, 5); root.Children.Add(actions);
            Content = root;
        }

        private DataTemplate BuildCandidateTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetResourceReference(FrameworkElement.StyleProperty, "PolicyRow");
            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(DockPanel));
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, DialogUi.Bind("RelativePath"));
            name.SetValue(TextBlock.FontSizeProperty, 12.0); name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            FrameworkElementFactory detail = new FrameworkElementFactory(typeof(TextBlock));
            detail.SetBinding(TextBlock.TextProperty, DialogUi.Bind("Detail")); detail.SetValue(TextBlock.FontSizeProperty, 10.0);
            detail.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0)); detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            panel.AppendChild(name); panel.AppendChild(detail);
            FrameworkElementFactory state = new FrameworkElementFactory(typeof(TextBlock));
            state.SetBinding(TextBlock.TextProperty, DialogUi.Bind("State")); state.SetBinding(TextBlock.ForegroundProperty, DialogUi.Bind("StateBrush"));
            state.SetValue(DockPanel.DockProperty, Dock.Right); state.SetValue(TextBlock.MarginProperty, new Thickness(14, 0, 4, 0));
            state.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(state); grid.AppendChild(panel); border.AppendChild(grid);
            DataTemplate template = new DataTemplate(); template.VisualTree = border; return template;
        }

        private static Button ActionButton(string text, string style, RoutedEventHandler click, string automationName)
        {
            Button button = new Button { Content = text, Padding = new Thickness(14, 7, 14, 7) };
            DialogUi.Style(button, style); button.Click += click; AutomationProperties.SetName(button, automationName);
            return button;
        }

        private void AutoFill()
        {
            pathBox.Focus();
            if (hints == null) return;
            foreach (string hint in hints)
            {
                string root; string error;
                if (!LolAddonCleaner.TryResolveRoot(hint, out root, out error)) continue;
                pathBox.Text = root; Inspect(null); return;
            }
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = Lang.T("addon.pick.desc"); dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                pathBox.Text = dialog.SelectedPath; Inspect(null);
            }
        }

        private void OnInspect(object sender, RoutedEventArgs e) { Inspect(null); }
        private void Inspect(string operationMessage)
        {
            if (Interlocked.Exchange(ref busy, 1) != 0) return;
            string input = pathBox.Text;
            SetBusy(true, Lang.T("addon.hint.busy")); candidates.Clear(); resolvedRoot = null;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string root; string error;
                LolAddonCleaner.Inspection inspection = null;
                bool ok = LolAddonCleaner.TryResolveRoot(input, out root, out error);
                if (ok)
                {
                    try { inspection = LolAddonCleaner.Inspect(root); }
                    catch (Exception ex) { error = ex.Message; }
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Interlocked.Exchange(ref busy, 0);
                    if (closed) return;
                    if (!ok)
                    {
                        SetBusy(false, string.IsNullOrEmpty(error) ? Lang.T("addon.hint.fail") : error); return;
                    }
                    resolvedRoot = root; pathBox.Text = root;
                    Render(inspection, operationMessage);
                }));
            });
        }

        private void Render(LolAddonCleaner.Inspection inspection, string operationMessage)
        {
            candidates.Clear(); deleteButton.IsEnabled = false;
            string message;
            if (inspection == null) message = Lang.T("addon.hint.fail");
            else if (!string.IsNullOrEmpty(inspection.Error)) message = inspection.Error;
            else if (!inspection.IsValidRoot) message = Lang.T("addon.hint.notlol");
            else
            {
                foreach (LolAddonCleaner.CandidateInfo candidate in inspection.Candidates)
                {
                    candidates.Add(new CandidateRow
                    {
                        RelativePath = candidate.RelativePath,
                        Detail = candidate.Exists
                            ? CacheSweep.FmtBytes(candidate.Bytes) + " · " + Lang.F("addon.files", candidate.FileCount)
                            : Lang.T("addon.absent"),
                        State = candidate.Exists ? (candidate.IsSafe ? "可删除" : "不可删除") : "未安装",
                        StateBrush = DialogUi.Brush(candidate.Exists && candidate.IsSafe
                            ? "DangerBrush" : "TextTertiaryBrush", candidate.Exists && candidate.IsSafe
                            ? Brushes.Firebrick : Brushes.Gray)
                    });
                }
                if (inspection.IsBlocked)
                    message = Lang.F("addon.hint.blocked", string.Join("、", inspection.BlockingProcesses.ToArray()));
                else if (inspection.CandidateCount == 0) message = Lang.T("addon.hint.clean");
                else
                {
                    message = Lang.F("addon.hint.ready", inspection.CandidateCount,
                        CacheSweep.FmtBytes(inspection.CandidateBytes));
                    deleteButton.IsEnabled = inspection.CanDelete;
                }
            }
            if (!string.IsNullOrEmpty(operationMessage)) message = operationMessage + "\r\n" + message;
            SetBusy(false, message);
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(resolvedRoot) || busy != 0) return;
            if (MessageBox.Show(this, Lang.F("addon.confirm", resolvedRoot), CaelusApp.App.DisplayName,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (Interlocked.Exchange(ref busy, 1) != 0) return;
            string root = resolvedRoot; SetBusy(true, Lang.T("addon.hint.deleting"));
            ThreadPool.QueueUserWorkItem(delegate
            {
                LolAddonCleaner.OperationResult result = null;
                try { result = LolAddonCleaner.Delete(root); }
                catch (Exception ex) { Logger.LogFailure("WPF LOL 附加层删除", ex); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Interlocked.Exchange(ref busy, 0);
                    if (closed) return;
                    string message = result == null ? Lang.T("addon.hint.fail")
                        : (result.Message ?? Lang.F("addon.hint.done", result.DeletedCount,
                            CacheSweep.FmtBytes(result.Bytes)));
                    if (result != null && result.Changed)
                        message += "（" + result.DeletedCount + " 项，" + CacheSweep.FmtBytes(result.Bytes) + "）";
                    Inspect(message);
                }));
            });
        }

        private void SetBusy(bool value, string message)
        {
            progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            pathBox.IsEnabled = !value; inspectButton.IsEnabled = !value; browseButton.IsEnabled = !value;
            list.IsEnabled = !value; deleteButton.IsEnabled = !value && deleteButton.IsEnabled;
            if (value) deleteButton.IsEnabled = false;
            if (!string.IsNullOrEmpty(message)) status.Text = message;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        private void OnDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0 || busy != 0) return;
            pathBox.Text = paths[0]; Inspect(null); e.Handled = true;
        }
        private void OnPathKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Inspect(null); e.Handled = true; }
        }
        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.F5 && busy == 0) { Inspect(null); e.Handled = true; }
        }
        private void OnClose(object sender, RoutedEventArgs e) { Close(); }
        protected override void OnClosed(EventArgs e) { closed = true; try { var w = Owner; if (w != null) w.Focus(); } catch { } base.OnClosed(e); }
    }
}
