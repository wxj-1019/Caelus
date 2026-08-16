// @author zenjiro 18967498922@163.com
// 文件用途 WPF 添加游戏对话框：扫描+过滤+复选+浏览+深度扫描

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace CaelusApp.WpfHost.Dialogs
{
    internal partial class AddGameDialogWpf : Window
    {
        // ViewModelBase 为 internal（与本程序集同），嵌套类需与之同级可见，故用 internal。
        internal class ScanRow : ViewModelBase
        {
            public string DisplayName { get; set; }
            public string ExePath { get; set; }
            private string tag = "";
            public string Tag
            {
                get { return tag; }
                set { tag = value; Raise("Tag"); Raise("HasTag"); }
            }
            public bool HasTag { get { return !string.IsNullOrEmpty(tag); } }
            public bool CanCheck { get; set; }
            public string Name { get; set; }
            public string Root { get; set; }
            public bool Running { get; set; }
            public bool RendererLike { get; set; }
            public double Gpu { get; set; }

            private bool isChecked;
            public bool Checked
            {
                get { return isChecked; }
                set { SetProperty(ref isChecked, value, "Checked"); }
            }
        }

        internal ObservableCollection<ScanRow> Rows { get; private set; }
        internal List<ScanHit> SelectedHits { get; private set; }

        private readonly HashSet<string> existingPaths;
        private readonly ICollectionView rowsView;
        private readonly bool allowGpuProbe;
        private volatile bool closed;
        private Thread scanThread;
        private int scanVersion;

        // existingLibraryItems 用于传入已有游戏（标记 Already）；learnedPaths 为学习到的真身路径；
        // allowGpuProbe 与 WinForms 一致：仅无进行中游戏会话时允许 GPU 采样识别渲染进程。
        internal AddGameDialogWpf(System.Collections.Generic.IEnumerable<LibraryItem> existingLibraryItems,
            System.Collections.Generic.IEnumerable<string> learnedPaths, bool allowGpuProbe)
        {
            InitializeComponent();
            Rows = new ObservableCollection<ScanRow>();
            rowsView = CollectionViewSource.GetDefaultView(Rows);
            rowsView.Filter = FilterRow;
            rowsView.SortDescriptions.Add(new SortDescription("Running", ListSortDirection.Descending));
            LstGames.ItemsSource = rowsView;
            SelectedHits = new List<ScanHit>();
            Motion.SetPoliteLiveSetting(LblInfo);
            this.allowGpuProbe = allowGpuProbe;

            existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingLibraryItems != null)
            {
                foreach (LibraryItem item in existingLibraryItems)
                {
                    if (!string.IsNullOrEmpty(item.Path))
                        existingPaths.Add(item.Path);
                }
            }
            if (learnedPaths != null)
            {
                foreach (string p in learnedPaths)
                    if (!string.IsNullOrEmpty(p)) existingPaths.Add(p);
            }

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            FrameworkElement content = Content as FrameworkElement;
            if (content != null) Motion.Reveal(content);
            TbFilter.Focus();
            StartRunningCollect();
            StartScan(null);
        }

        private void StartScan(string deepRoot)
        {
            int version = ++scanVersion;
            BtnBrowse.IsEnabled = false;
            BtnDeep.IsEnabled = false;
            BtnSelectAll.IsEnabled = false;
            ScanProgress.Visibility = Visibility.Visible;
            LblInfo.Text = Lang.T(deepRoot == null ? "scan.busy" : "scan.busy.deep");
            closed = false;

            scanThread = new Thread(delegate()
            {
                List<ScanHit> hits;
                try
                {
                    hits = deepRoot == null
                        ? GameScan.RunManifests(() => closed || version != scanVersion)
                        : GameScan.Run(deepRoot, () => closed || version != scanVersion, null);
                }
                catch { hits = new List<ScanHit>(); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (closed || version != scanVersion) return;
                    MergeScanResults(hits, false);
                    rowsView.Refresh();
                    BtnBrowse.IsEnabled = true;
                    BtnDeep.IsEnabled = true;
                    ScanProgress.Visibility = Visibility.Collapsed;
                    LblInfo.Text = Rows.Count == 0 ? Lang.T("scan.none") : Lang.F("scan.count", Rows.Count, CountCheckable());
                    UpdateAddButton();
                }));
            });
            scanThread.IsBackground = true;
            scanThread.Start();
        }

        // 与旧 WinForms 一致：后台收集正在运行的游戏候选进程，随后可选 GPU 采样标记"渲染中"
        private void StartRunningCollect()
        {
            var worker = new Thread(delegate()
            {
                List<ScanHit> hits;
                Dictionary<string, int> pidByPath;
                try { hits = CollectRunningCandidates(out pidByPath); }
                catch
                {
                    hits = new List<ScanHit>();
                    pidByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (closed) return;
                    MergeScanResults(hits, true);
                    rowsView.Refresh();
                    UpdateAddButton();
                }));

                if (!allowGpuProbe || closed || pidByPath.Count == 0) return;
                Dictionary<int, double> util = null;
                try
                {
                    util = GpuEvidence.Sample3D(
                        GpuEvidence.BurstRounds, GpuEvidence.BurstIntervalMs,
                        delegate { return closed; });
                }
                catch { }
                if (util == null || closed) return;
                var utilByPath = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, int> kv in pidByPath)
                {
                    double u;
                    if (util.TryGetValue(kv.Value, out u) && u > 0) utilByPath[kv.Key] = u;
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (closed) return;
                    ApplyGpuTags(utilByPath);
                }));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private static List<ScanHit> CollectRunningCandidates(out Dictionary<string, int> pidByPath)
        {
            pidByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var hits = new List<ScanHit>();
            int session = -1, selfPid = 0;
            try
            {
                using (Process current = Process.GetCurrentProcess())
                {
                    session = current.SessionId;
                    selfPid = current.Id;
                }
            }
            catch { }
            if (session < 0) return hits;
            string windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            windowsRoot = string.IsNullOrEmpty(windowsRoot) ? @"C:\Windows\" : windowsRoot.TrimEnd('\\') + "\\";
            HashSet<int> visible = GameSessionDetector.VisibleWindowPids(true);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] all = null;
            try
            {
                all = Process.GetProcesses();
                foreach (Process p in all)
                {
                    try
                    {
                        if (p.Id <= 4 || p.Id == selfPid || !visible.Contains(p.Id)) continue;
                        int processSession = -1;
                        try { processSession = p.SessionId; } catch { }
                        if (processSession != session) continue;
                        string path = null;
                        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                        if (h != IntPtr.Zero)
                        {
                            try { path = Native.ImagePath(h); }
                            finally { Native.CloseHandle(h); }
                        }
                        if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                        string name = GameSessionDetector.ImageNameFromVerifiedPath(path);
                        if (!GameSessionDetector.IsLibraryCandidate(name, path, windowsRoot)) continue;
                        if (GamePlatformCatalog.IsPlatformProcess(name, path)) continue;
                        pidByPath[path] = p.Id;
                        hits.Add(new ScanHit
                        {
                            Name = DisplayNameOf(path, name),
                            Proc = name,
                            Root = GameScan.InferGameRoot(path),
                            Exe = path
                        });
                    }
                    catch { }
                }
            }
            catch { }
            finally { if (all != null) foreach (Process p in all) { try { p.Dispose(); } catch { } } }
            return hits;
        }

        private static string DisplayNameOf(string executablePath, string fallback)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(executablePath);
                string value = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            catch { }
            return fallback;
        }

        private void ApplyGpuTags(Dictionary<string, double> utilByPath)
        {
            ScanRow best = null;
            foreach (ScanRow r in Rows)
            {
                if (!r.Running) continue;
                double u;
                if (!utilByPath.TryGetValue(r.ExePath, out u)) continue;
                r.Gpu = u;
                if (best == null || u > best.Gpu) best = r;
            }
            if (best != null && best.Gpu >= GpuEvidence.MinElectUtilization)
            {
                best.RendererLike = true;
                best.Tag = Lang.F("scan.renderer.tag", ((int)best.Gpu).ToString());
                if (best.CanCheck) best.Checked = true;
            }
            UpdateAddButton();
        }

        private void MergeScanResults(List<ScanHit> hits, bool running)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScanRow r in Rows) known.Add(r.ExePath);

            foreach (ScanHit h in hits)
            {
                if (h == null) continue;
                string exe = ResolveExe(h);
                if (exe == null || !known.Add(exe)) continue;

                bool already = existingPaths.Contains(exe);
                ScanRow row = new ScanRow
                {
                    DisplayName = string.IsNullOrEmpty(h.Name) ? Path.GetFileNameWithoutExtension(exe) : h.Name,
                    ExePath = exe,
                    Name = h.Name,
                    Root = h.Root,
                    CanCheck = !already,
                    Checked = false,
                    Running = running
                };
                row.Tag = already ? Lang.T("scan.already")
                    : running ? Lang.T("scan.running.tag") : "";
                row.PropertyChanged += OnRowPropertyChanged;
                Rows.Add(row);
            }
        }

        private static string ResolveExe(ScanHit h)
        {
            if (string.IsNullOrEmpty(h.Exe)) return null;
            string resolved, error;
            return GameExecutableResolver.TryResolve(h.Exe, out resolved, out error) ? resolved : null;
        }

        private int CountCheckable()
        {
            int n = 0;
            foreach (ScanRow r in Rows) if (r.CanCheck && !existingPaths.Contains(r.ExePath)) n++;
            return n;
        }

        private bool FilterRow(object item)
        {
            ScanRow row = item as ScanRow;
            if (row == null) return false;
            string needle = TbFilter == null ? "" : TbFilter.Text.Trim();
            return needle.Length == 0
                || row.DisplayName.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0
                || row.ExePath.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (rowsView == null) return;
            string f = TbFilter.Text.Trim();
            FilterHint.Visibility = f.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            rowsView.Refresh();
            UpdateAddButton();
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            bool anyUnchecked = false;
            foreach (object item in rowsView)
            {
                ScanRow row = item as ScanRow;
                if (row != null && row.CanCheck && !row.Checked) { anyUnchecked = true; break; }
            }
            foreach (object item in rowsView)
            {
                ScanRow row = item as ScanRow;
                if (row != null && row.CanCheck) row.Checked = anyUnchecked;
            }
            UpdateAddButton();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = Lang.T("ofd.game");
            dlg.Filter = Lang.T("ofd.filter");
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog() != true) return;
            string resolved, error;
            if (!GameExecutableResolver.TryResolve(dlg.FileName, out resolved, out error))
            {
                if (!string.IsNullOrEmpty(error))
                    MessageBox.Show(error, "Caelus", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 浏览模式：清空列表只放一条
            UnsubscribeRows();
            Rows.Clear();
            ScanRow row = new ScanRow
            {
                DisplayName = Path.GetFileNameWithoutExtension(resolved),
                ExePath = resolved,
                Name = null,
                Root = GameScan.InferGameRoot(resolved),
                Tag = "",
                CanCheck = true,
                Checked = true
            };
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
            rowsView.Refresh();
            LstGames.SelectedItem = row;
            LblInfo.Text = "";
            UpdateAddButton();
        }

        private void OnDeepScan(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = Lang.T("scan.deep.pick");
            dlg.ShowNewFolderButton = false;
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            if (string.IsNullOrEmpty(dlg.SelectedPath)) return;
            StartScan(dlg.SelectedPath);
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAddButton();
        }

        private void OnRowCheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateAddButton();
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Checked") UpdateAddButton();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            ScanRow row = LstGames.SelectedItem as ScanRow;
            if (e.Key == Key.Space && row != null && row.CanCheck)
            {
                row.Checked = !row.Checked;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && BtnAdd.IsEnabled)
            {
                OnAccept(null, null);
                e.Handled = true;
            }
        }

        private void OnItemDoubleClick(object sender, RoutedEventArgs e)
        {
            ScanRow row = LstGames.SelectedItem as ScanRow;
            if (row == null || !row.CanCheck) return;
            row.Checked = !row.Checked;
        }

        private void UpdateAddButton()
        {
            int n = 0;
            foreach (ScanRow r in Rows) if (r.Checked && r.CanCheck) n++;
            BtnAdd.Content = n > 0 ? Lang.F("scan.add.n", n) : Lang.T("btn.add");
            BtnAdd.IsEnabled = n > 0;

            bool hasVisible = false;
            bool allVisibleChecked = true;
            foreach (object item in rowsView)
            {
                ScanRow row = item as ScanRow;
                if (row == null || !row.CanCheck) continue;
                hasVisible = true;
                if (!row.Checked) allVisibleChecked = false;
            }
            BtnSelectAll.IsEnabled = hasVisible && ScanProgress.Visibility != Visibility.Visible;
            BtnSelectAll.Content = hasVisible && allVisibleChecked ? "取消全选" : "全选";
        }

        private void OnAccept(object sender, RoutedEventArgs e)
        {
            SelectedHits.Clear();
            foreach (ScanRow r in Rows)
            {
                if (r.Checked && r.CanCheck)
                {
                    SelectedHits.Add(new ScanHit { Name = r.Name, Root = r.Root, Exe = r.ExePath });
                }
            }
            if (SelectedHits.Count == 0) return;
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

        private void UnsubscribeRows()
        {
            foreach (ScanRow row in Rows)
                row.PropertyChanged -= OnRowPropertyChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            UnsubscribeRows();
            try { var w = Owner; if (w != null) w.Focus(); } catch { }
            base.OnClosed(e);
        }
    }
}
