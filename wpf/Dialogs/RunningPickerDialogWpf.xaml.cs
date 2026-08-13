// @author zenjiro 18967498922@163.com
// 文件用途 WPF 运行程序选择器：复用旧版枚举规则，支持筛选与批量加入白名单

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace CaelusApp.WpfHost.Dialogs
{
    internal partial class RunningPickerDialogWpf : Window
    {
        internal sealed class Entry : ViewModelBase
        {
            public string Path { get; set; }
            public string Title { get; set; }
            public long Memory { get; set; }
            public int Count { get; set; }
            public BitmapSource Icon { get; set; }
            public string Initial
            {
                get
                {
                    return string.IsNullOrEmpty(Title) ? "?"
                        : Title.Substring(0, 1).ToUpperInvariant();
                }
            }

            public string Detail
            {
                get
                {
                    string memory = FormatMemory(Memory);
                    return Count > 1 ? memory + " · " + Lang.F("white.pick.procs", Count) : memory;
                }
            }

            private bool isChecked;
            public bool Checked
            {
                get { return isChecked; }
                set { SetProperty(ref isChecked, value, "Checked"); }
            }
        }

        private readonly HashSet<string> known;
        private readonly ObservableCollection<Entry> all;
        private readonly ObservableCollection<Entry> shown;
        private volatile bool closed;
        private bool scanning;

        internal List<string> SelectedPaths { get; private set; }

        internal RunningPickerDialogWpf(IEnumerable<string> alreadyListed)
        {
            InitializeComponent();

            known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (alreadyListed != null)
            {
                foreach (string path in alreadyListed)
                    if (!string.IsNullOrEmpty(path)) known.Add(path);
            }

            all = new ObservableCollection<Entry>();
            shown = new ObservableCollection<Entry>();
            SelectedPaths = new List<string>();
            LstPrograms.ItemsSource = shown;
            Motion.SetPoliteLiveSetting(LblInfo);
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            FrameworkElement content = Content as FrameworkElement;
            if (content != null) Motion.Reveal(content);
            TbFilter.Focus();
            BeginScan();
        }

        private void BeginScan()
        {
            if (scanning) return;
            scanning = true;
            var selectedBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in all) if (entry.Checked) selectedBefore.Add(entry.Path);
            LblInfo.Text = Lang.T("white.pick.busy");
            BtnSelectAll.IsEnabled = false;
            BtnAdd.IsEnabled = false;
            BtnRefresh.IsEnabled = false;

            Thread thread = new Thread(delegate()
            {
                List<Entry> found;
                try { found = Scan(known); }
                catch { found = new List<Entry>(); }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (closed) return;
                    UnsubscribeEntries();
                    all.Clear();
                    foreach (Entry entry in found)
                    {
                        entry.Checked = selectedBefore.Contains(entry.Path);
                        entry.PropertyChanged += OnEntryPropertyChanged;
                        all.Add(entry);
                    }
                    scanning = false;
                    BtnRefresh.IsEnabled = true;
                    ApplyFilter();
                    UpdateSelectionState();
                    if (shown.Count > 0)
                    {
                        LstPrograms.SelectedIndex = 0;
                        LstPrograms.ScrollIntoView(LstPrograms.SelectedItem);
                    }
                }));
            });
            thread.IsBackground = true;
            thread.Start();
        }

        internal static List<Entry> Scan(HashSet<string> exclude)
        {
            Dictionary<string, Entry> map =
                new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            int self = 0;
            int session = -1;
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    self = me.Id;
                    session = me.SessionId;
                }
            }
            catch { }

            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch { return new List<Entry>(); }

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string windowsPrefix = string.IsNullOrEmpty(windows)
                ? @"C:\Windows\" : windows.TrimEnd('\\') + "\\";

            foreach (Process process in processes)
            {
                try
                {
                    int pid = process.Id;
                    if (pid <= 4 || pid == self) continue;
                    if (process.SessionId != session) continue;
                    if (process.MainWindowHandle == IntPtr.Zero) continue;

                    string name = process.ProcessName;
                    if (GameSessionDetector.IsAntiCheatLikeName(name)) continue;

                    IntPtr handle = Native.OpenProcess(
                        Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (handle == IntPtr.Zero) continue;
                    string path;
                    try { path = Native.ImagePath(handle); }
                    finally { Native.CloseHandle(handle); }
                    if (string.IsNullOrEmpty(path)) continue;
                    if (path.StartsWith(windowsPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (exclude != null && exclude.Contains(path)) continue;

                    Entry entry;
                    if (!map.TryGetValue(path, out entry))
                    {
                        entry = new Entry
                        {
                            Path = path,
                            Title = TitleOf(path, name),
                            Icon = LoadIcon(path)
                        };
                        map[path] = entry;
                    }
                    entry.Count++;
                    try { entry.Memory += process.WorkingSet64; }
                    catch { }
                }
                catch { }
                finally { try { process.Dispose(); } catch { } }
            }

            List<Entry> result = new List<Entry>(map.Values);
            result.Sort(delegate(Entry a, Entry b) { return b.Memory.CompareTo(a.Memory); });
            return result;
        }

        private static string TitleOf(string path, string fallback)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                string title = string.IsNullOrEmpty(info.FileDescription)
                    ? info.ProductName : info.FileDescription;
                if (!string.IsNullOrEmpty(title)) return title.Trim();
            }
            catch { }
            return fallback;
        }

        private static BitmapSource LoadIcon(string path)
        {
            try
            {
                using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            catch { return null; }
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            FilterHint.Visibility = TbFilter.Text.Length == 0
                ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (shown == null || all == null) return;
            string needle = TbFilter.Text.Trim();
            shown.Clear();
            foreach (Entry entry in all)
            {
                if (needle.Length > 0
                    && entry.Title.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0
                    && entry.Path.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown.Add(entry);
            }
            UpdateSelectionState();
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            ToggleShown();
        }

        private void ToggleShown()
        {
            if (shown.Count == 0) return;
            bool anyUnchecked = false;
            foreach (Entry entry in shown)
            {
                if (!entry.Checked) { anyUnchecked = true; break; }
            }
            foreach (Entry entry in shown) entry.Checked = anyUnchecked;
            UpdateSelectionState();
        }

        private void OnRowCheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateSelectionState();
        }

        private void OnEntryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Checked") UpdateSelectionState();
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            Entry entry = LstPrograms.SelectedItem as Entry;
            if (e.Key == Key.Space && entry != null)
            {
                entry.Checked = !entry.Checked;
                e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleShown();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && BtnAdd.IsEnabled)
            {
                OnAccept(null, null);
                e.Handled = true;
            }
        }

        private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Entry entry = LstPrograms.SelectedItem as Entry;
            if (entry == null) return;
            entry.Checked = !entry.Checked;
        }

        private void UpdateSelectionState()
        {
            if (all == null || shown == null) return;
            int selected = 0, shownSelected = 0;
            foreach (Entry entry in all) if (entry.Checked) selected++;
            foreach (Entry entry in shown) if (entry.Checked) shownSelected++;
            BtnAdd.Content = selected > 0
                ? Lang.F("white.pick.add.n", selected) : Lang.T("white.pick.add");
            BtnAdd.IsEnabled = selected > 0 && !scanning;
            BtnSelectAll.IsEnabled = shown.Count > 0 && !scanning;
            bool allShownChecked = shown.Count > 0;
            foreach (Entry entry in shown)
            {
                if (!entry.Checked) { allShownChecked = false; break; }
            }
            BtnSelectAll.Content = allShownChecked ? "取消全选" : "全选";
            if (!scanning)
            {
                int hiddenSelected = selected - shownSelected;
                LblInfo.Text = all.Count == 0 ? Lang.T("white.pick.none")
                    : "显示 " + shown.Count + " / 共 " + all.Count + " · 已选 " + selected
                    + (hiddenSelected > 0 ? "（其中 " + hiddenSelected + " 项被筛选隐藏）" : "");
            }
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            BeginScan();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (e.Key != Key.F5 || scanning) return;
            BeginScan();
            e.Handled = true;
        }

        private void OnAccept(object sender, RoutedEventArgs e)
        {
            SelectedPaths.Clear();
            foreach (Entry entry in all)
                if (entry.Checked) SelectedPaths.Add(entry.Path);
            if (SelectedPaths.Count == 0) return;
            closed = true;
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            closed = true;
            DialogResult = false;
            Close();
        }

        private void UnsubscribeEntries()
        {
            foreach (Entry entry in all)
                entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            scanning = false;
            UnsubscribeEntries();
            try { var w = Owner; if (w != null) w.Focus(); } catch { }
            base.OnClosed(e);
        }

        internal static string FormatMemory(long bytes)
        {
            if (bytes >= 1073741824L)
                return (bytes / 1073741824.0).ToString("0.0") + " GB";
            if (bytes >= 1048576L)
                return (bytes / 1048576.0).ToString("0") + " MB";
            if (bytes <= 0) return "—";
            return (bytes / 1024.0).ToString("0") + " KB";
        }
    }
}
