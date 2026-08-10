// @author zenjiro 18967498922@163.com
// 文件用途 游戏库页 ViewModel：游戏列表 + 添加/移除 + 运行态探测

using System;
using System.Collections.ObjectModel;
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
            set { SetProperty(ref isRunning, value, "IsRunning"); }
        }

        public string StatusText { get { return isRunning ? Lang.T("v15.library.running") : Lang.T("v15.library.ready"); } }

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

        public ObservableCollection<LibraryItem> Items { get; private set; }
        public string EmptyTitle { get { return "CAELUS LIBRARY"; } }
        public string EmptyHint { get { return Lang.T("v15.library.empty"); } }
        public string AddButtonText { get { return Lang.T("v15.library.add"); } }
        public string RemoveButtonText { get { return Lang.T("btn.remove"); } }
        public string DropHint { get { return Lang.T("v15.library.drop"); } }

        public bool IsEmpty { get { return Items.Count == 0; } }

        public LibraryViewModel(GameMode gm)
        {
            this.gm = gm;
            Items = new ObservableCollection<LibraryItem>();
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
            Raise("IsEmpty");
        }

        // 拖放或浏览添加文件（返回错误列表，空=全部成功）
        public string AddFile(string file)
        {
            string error;
            gm.AddGameFile(file, out error);
            Refresh();
            return error;
        }

        // 批量添加扫描结果
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

        // 运行态探测：检查游戏 exe 是否有对应进程在运行。
        // 与 WinForms 版一致，用 Native.OpenProcess + Native.ImagePath 取进程映像路径，
        // 避开 Process.MainModule 对提权进程抛异常的问题。
        public void ProbeRunning()
        {
            if (Items.Count == 0) return;
            var wanted = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (LibraryItem item in Items)
            {
                if (!string.IsNullOrEmpty(item.Path)) wanted.Add(item.Path);
            }
            if (wanted.Count == 0) { foreach (LibraryItem item in Items) item.IsRunning = false; return; }

            var running = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            Process[] all = null;
            try
            {
                all = Process.GetProcesses();
                foreach (Process proc in all)
                {
                    try
                    {
                        if (proc.SessionId == 0) continue; // 跳过系统会话
                        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
                        if (h == System.IntPtr.Zero) continue;
                        string image;
                        try { image = Native.ImagePath(h); }
                        finally { Native.CloseHandle(h); }
                        if (!string.IsNullOrEmpty(image) && wanted.Contains(image))
                            running.Add(image);
                    }
                    catch { }
                }
            }
            catch { }
            finally { if (all != null) foreach (Process p in all) try { p.Dispose(); } catch { } }

            foreach (LibraryItem item in Items)
                item.IsRunning = !string.IsNullOrEmpty(item.Path) && running.Contains(item.Path);
        }
    }
}
