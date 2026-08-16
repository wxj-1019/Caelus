// @author zenjiro 18967498922@163.com
// 文件用途 游戏库页 ViewModel：游戏列表 + 添加/移除 + 运行态探测

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;

namespace CaelusApp
{
    internal sealed class LibraryItem : ViewModelBase
    {
        private bool isRunning;

        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Path { get; private set; }

        public bool IsRunning
        {
            get { return isRunning; }
            set
            {
                if (SetProperty(ref isRunning, value, "IsRunning"))
                    Raise("StatusText");
            }
        }

        public string StatusText { get { return isRunning ? Lang.T("v15.library.running") : Lang.T("v15.library.ready"); } }

        public string Initial
        {
            get { return string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1); }
        }

        public LibraryItem(string id, string name, string path)
        {
            Id = id;
            Name = name;
            Path = path;
        }
    }

    internal sealed class LibraryViewModel : ViewModelBase
    {
        private readonly GameMode gm;
        private string feedbackText = "";
        private string feedbackKind = "Info";
        private string netQosSignature;
        private bool netQosDeferred;
        private int netQosBusy;
        private readonly object netQosSync = new object();

        public ObservableCollection<LibraryItem> Items { get; private set; }
        public string EmptyTitle { get { return "CAELUS LIBRARY"; } }
        public string EmptyHint { get { return Lang.T("v15.library.empty"); } }
        public string AddButtonText { get { return Lang.T("v15.library.add"); } }
        public string RemoveButtonText { get { return Lang.T("btn.remove"); } }
        public string DropHint { get { return Lang.T("v15.library.drop"); } }
        public string FeedbackText { get { return feedbackText; } }
        public string FeedbackKind { get { return feedbackKind; } }

        internal void SetFeedback(string text, string kind)
        {
            feedbackKind = string.IsNullOrEmpty(kind) ? "Info" : kind;
            feedbackText = text ?? "";
            Raise("FeedbackKind");
            Raise("FeedbackText");
        }

        public bool IsEmpty { get { return Items.Count == 0; } }
        public int TotalCount { get { return Items.Count; } }
        public int RunningCount
        {
            get
            {
                int n = 0;
                foreach (LibraryItem item in Items) if (item.IsRunning) n++;
                return n;
            }
        }

        internal void NotifyCounts()
        {
            Raise("IsEmpty");
            Raise("TotalCount");
            Raise("RunningCount");
        }

        public LibraryViewModel(GameMode gm)
        {
            this.gm = gm;
            Items = new ObservableCollection<LibraryItem>();
            Items.CollectionChanged += OnItemsCollectionChanged;
        }

        private void OnItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (LibraryItem item in e.OldItems) item.PropertyChanged -= OnItemPropertyChanged;
            if (e.NewItems != null)
                foreach (LibraryItem item in e.NewItems) item.PropertyChanged += OnItemPropertyChanged;
            NotifyCounts();
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsRunning") Raise("RunningCount");
        }

        public void Refresh()
        {
            Items.Clear();
            foreach (GameProfile p in gm.GetProfiles())
            {
                string displayPath = p.ExecutablePath ?? p.Root ?? "";
                string name = p.Name;
                if (string.IsNullOrEmpty(name))
                    name = System.IO.Path.GetFileNameWithoutExtension(displayPath);
                Items.Add(new LibraryItem(p.Id, name, displayPath));
            }
            NotifyCounts();
            SyncNetQosPolicies();
        }

        // 与旧 WinForms 的 SyncNetQosPolicies 一致：游戏库变更后重同步网络优先级策略
        //（签名比对防重复；游戏会话进行中推迟到退出游戏后；后台线程执行）
        private void SyncNetQosPolicies()
        {
            var sb = new System.Text.StringBuilder();
            foreach (GameProfile profile in gm.GetProfiles())
            {
                if (string.IsNullOrEmpty(profile.ExecutablePath)) continue;
                sb.Append(profile.Name).Append('>').Append(profile.ExecutablePath).Append('|');
            }
            string signature = sb.ToString();
            if (netQosSignature == null) { netQosSignature = signature; return; }
            if (netQosSignature == signature && !netQosDeferred) return;
            netQosSignature = signature;
            if (!NetworkAffinityTweak.EnabledByCaelus) { netQosDeferred = false; return; }
            if (gm.ActiveGame != null)
            {
                if (!netQosDeferred)
                    Logger.Log("网络优先级：游戏会话进行中，游戏库变更的策略同步推迟到退出游戏后执行");
                netQosDeferred = true;
                return;
            }
            netQosDeferred = false;
            if (System.Threading.Interlocked.Exchange(ref netQosBusy, 1) == 1) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (netQosSync)
                {
                    System.Threading.Interlocked.Exchange(ref netQosBusy, 0);
                    try { NetworkAffinityTweak.Enable(gm.GetProfiles()); }
                    catch { }
                }
            });
        }

        // 运行态定时刷新时调用：游戏退出后补执行被推迟的策略同步
        public void SyncDeferredNetQos()
        {
            if (netQosDeferred && gm.ActiveGame == null) SyncNetQosPolicies();
        }

        public string AddFile(string file)
        {
            string error;
            gm.AddGameFile(file, out error);
            Refresh();
            return error;
        }

        public int AddScannedGames(System.Collections.Generic.IList<ScanHit> hits)
        {
            string lastError;
            int added = gm.AddScannedGames(hits, out lastError);
            Refresh();
            return added;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            gm.RemoveProfile(Items[index].Id);
            Refresh();
        }

        // 学习到的真身路径（添加对话框用于标记"已在库中"）
        public System.Collections.Generic.IEnumerable<string> KnownLearnedPaths()
        {
            foreach (GameProfile p in gm.GetProfiles())
                if (!string.IsNullOrEmpty(p.LearnedExecutablePath)) yield return p.LearnedExecutablePath;
        }

        // 与 WinForms 一致：仅无进行中游戏会话时允许 GPU 采样识别渲染进程
        public bool CanGpuProbe { get { return gm.ActiveGame == null; } }

        // 与 WinForms 版一致，使用映像路径而不是 MainModule，兼容提权进程。
        public void ProbeRunning()
        {
            if (Items.Count == 0) return;
            var wanted = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (LibraryItem item in Items)
            {
                if (!string.IsNullOrEmpty(item.Path)) wanted.Add(item.Path);
            }
            if (wanted.Count == 0)
            {
                foreach (LibraryItem item in Items) item.IsRunning = false;
                return;
            }

            var running = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            Process[] all = null;
            try
            {
                all = Process.GetProcesses();
                foreach (Process proc in all)
                {
                    try
                    {
                        if (proc.SessionId == 0) continue;
                        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
                        if (h == IntPtr.Zero) continue;
                        string image;
                        try { image = Native.ImagePath(h); }
                        finally { Native.CloseHandle(h); }
                        if (!string.IsNullOrEmpty(image) && wanted.Contains(image)) running.Add(image);
                    }
                    catch { }
                }
            }
            catch { }
            finally { if (all != null) foreach (Process p in all) try { p.Dispose(); } catch { } }

            foreach (LibraryItem item in Items)
                item.IsRunning = !string.IsNullOrEmpty(item.Path) && running.Contains(item.Path);
            Raise("RunningCount");
        }
    }
}
