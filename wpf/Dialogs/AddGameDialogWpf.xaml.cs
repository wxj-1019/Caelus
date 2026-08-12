// @author zenjiro 18967498922@163.com
// 文件用途 WPF 添加游戏对话框：扫描+过滤+复选+浏览+深度扫描

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
            public string Tag { get; set; }
            public bool HasTag { get { return !string.IsNullOrEmpty(Tag); } }
            public bool CanCheck { get; set; }
            public string Name { get; set; }
            public string Root { get; set; }

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
        private volatile bool closed;
        private Thread scanThread;
        private int scanVersion;

        // existingLibraryItems 用于传入已有游戏（标记 Already）
        internal AddGameDialogWpf(System.Collections.Generic.IEnumerable<LibraryItem> existingLibraryItems)
        {
            InitializeComponent();
            Rows = new ObservableCollection<ScanRow>();
            rowsView = CollectionViewSource.GetDefaultView(Rows);
            rowsView.Filter = FilterRow;
            LstGames.ItemsSource = rowsView;
            SelectedHits = new List<ScanHit>();
            Motion.SetPoliteLiveSetting(LblInfo);

            existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingLibraryItems != null)
            {
                foreach (LibraryItem item in existingLibraryItems)
                {
                    if (!string.IsNullOrEmpty(item.Path))
                        existingPaths.Add(item.Path);
                }
            }

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            FrameworkElement content = Content as FrameworkElement;
            if (content != null) Motion.Reveal(content);
            TbFilter.Focus();
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
                    MergeScanResults(hits);
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

        private void MergeScanResults(List<ScanHit> hits)
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
                    Tag = already ? Lang.T("scan.already") : "",
                    CanCheck = !already,
                    Checked = false
                };
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
            base.OnClosed(e);
        }
    }
}
