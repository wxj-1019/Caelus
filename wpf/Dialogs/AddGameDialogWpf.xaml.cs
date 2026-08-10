// @author zenjiro 18967498922@163.com
// 文件用途 WPF 添加游戏对话框：扫描+过滤+复选+浏览+深度扫描

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        private volatile bool closed;
        private Thread scanThread;

        // existingLibraryItems 用于传入已有游戏（标记 Already）
        internal AddGameDialogWpf(System.Collections.Generic.IEnumerable<LibraryItem> existingLibraryItems)
        {
            InitializeComponent();
            Rows = new ObservableCollection<ScanRow>();
            LstGames.ItemsSource = Rows;
            SelectedHits = new List<ScanHit>();

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
            StartScan(null);
        }

        private void StartScan(string deepRoot)
        {
            BtnDeep.IsEnabled = false;
            LblInfo.Text = Lang.T(deepRoot == null ? "scan.busy" : "scan.busy.deep");
            closed = false;

            scanThread = new Thread(delegate()
            {
                List<ScanHit> hits;
                try
                {
                    hits = deepRoot == null
                        ? GameScan.RunManifests(() => closed)
                        : GameScan.Run(deepRoot, () => closed, null);
                }
                catch { hits = new List<ScanHit>(); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    MergeScanResults(hits);
                    BtnDeep.IsEnabled = true;
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
                Rows.Add(new ScanRow
                {
                    DisplayName = string.IsNullOrEmpty(h.Name) ? Path.GetFileNameWithoutExtension(exe) : h.Name,
                    ExePath = exe,
                    Name = h.Name,
                    Root = h.Root,
                    Tag = already ? "已在库中" : "",
                    CanCheck = !already,
                    Checked = false
                });
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

        private void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            string f = TbFilter.Text.Trim();
            if (f.Length == 0) { LstGames.ItemsSource = Rows; return; }
            var filtered = new ObservableCollection<ScanRow>();
            foreach (ScanRow r in Rows)
            {
                if (r.DisplayName.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0
                    || r.ExePath.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add(r);
            }
            LstGames.ItemsSource = filtered;
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            bool anyUnchecked = false;
            foreach (ScanRow r in Rows) if (r.CanCheck && !r.Checked) { anyUnchecked = true; break; }
            foreach (ScanRow r in Rows) if (r.CanCheck) r.Checked = anyUnchecked;
            UpdateAddButton();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = Lang.T("ofd.game");
            dlg.Filter = Lang.T("ofd.filter");
            dlg.CheckFileExists = false;
            if (dlg.ShowDialog() != true) return;
            string resolved, error;
            if (!GameExecutableResolver.TryResolve(dlg.FileName, out resolved, out error))
            {
                if (!string.IsNullOrEmpty(error))
                    MessageBox.Show(error, "Caelus", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 浏览模式：清空列表只放一条
            Rows.Clear();
            Rows.Add(new ScanRow
            {
                DisplayName = Path.GetFileNameWithoutExtension(resolved),
                ExePath = resolved,
                Name = null,
                Root = GameScan.InferGameRoot(resolved),
                Tag = "",
                CanCheck = true,
                Checked = true
            });
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

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void OnItemDoubleClick(object sender, RoutedEventArgs e)
        {
            // 双击直接接受当前选中项
            if (LstGames.SelectedItem is ScanRow)
            {
                OnAccept(null, null);
            }
        }

        private void UpdateAddButton()
        {
            int n = 0;
            foreach (ScanRow r in Rows) if (r.Checked && r.CanCheck) n++;
            BtnAdd.Content = n > 0 ? Lang.F("scan.add.n", n) : Lang.T("btn.add");
            BtnAdd.IsEnabled = n > 0;
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

        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            base.OnClosed(e);
        }
    }
}
