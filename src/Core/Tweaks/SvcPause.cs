// @author zenjiro 18967498922@163.com
// 文件用途 暂停并恢复索引和预取服务

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CaelusApp
{
    internal static class SvcPause
    {
        internal const string OwnerGame = "game";
        internal const string OwnerDevFocus = "devfocus";

        private static readonly string[] Names = { "SysMain", "WSearch" };
        private const string Flag = "PrevSvcPaused";
        private static readonly object lk = new object();
        // 多占用方登记：游戏模式（自定义档）与开发专注可先后/叠加要求同一效果，
        // 最后一个占用方离开才真正还原服务；交接直通只换记账不动系统。
        private static readonly HashSet<string> owners = new HashSet<string>(StringComparer.Ordinal);

        public static bool Activate(string owner)
        {
            lock (lk)
            {
                if (!SharedEffectClaim.Acquire(owners, owner)) return true;

                var owned = new List<string>();
                foreach (string s in Settings.LoadStr(Flag, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    owned.Add(s);

                var justStopped = new List<string>();
                var confirmedStopped = new List<string>();
                var intent = new List<string>(owned);
                foreach (string n in Names)
                {
                    try
                    {
                        int before = SvcState.Query(n);
                        if (before == 4 && !intent.Contains(n))
                        {
                            intent.Add(n);
                            Settings.SaveStr(Flag, string.Join("|", intent.ToArray()));
                        }
                        bool confirmedStop;
                        bool issued = SvcCtl.StopIfRunning(n, out confirmedStop);
                        if (!confirmedStop && before == 4 && SvcState.StopTaken(SvcState.Query(n)))
                            confirmedStop = true;
                        if (issued || confirmedStop) justStopped.Add(n);
                        if (confirmedStop) confirmedStopped.Add(n);
                    }
                    catch { }
                }

                foreach (string n in justStopped)
                    if (!owned.Contains(n)) owned.Add(n);

                if (owned.Count > 0 || intent.Count > 0)
                {
                    Settings.SaveStr(Flag, string.Join("|", owned.ToArray()));
                    if (Settings.LoadStr(Flag, "") != string.Join("|", owned.ToArray()))
                    {
                        foreach (string name in justStopped) SvcCtl.EnsureStarted(name);
                        Logger.Log("服务暂停状态无法持久化，已重新启动本轮停止的服务");
                        SharedEffectClaim.Release(owners, owner);
                        return false;
                    }
                    if (confirmedStopped.Count > 0)
                        Logger.Log("已暂停索引/预取服务：" + string.Join(" + ", confirmedStopped.ToArray()));
                    else if (justStopped.Count > 0)
                        Logger.Log("已请求停止索引/预取服务，尚未确认停止：" + string.Join(" + ", justStopped.ToArray()));
                }
                return true;
            }
        }

        /// <summary>全量还原：清空全部占用方并按标志重启服务。退出链与崩溃自愈使用。</summary>
        public static bool Restore()
        {
            lock (lk)
            {
                SharedEffectClaim.ReleaseAll(owners);
                return RestoreFlagged();
            }
        }

        /// <summary>释放指定占用方；仅当这是最后一个占用方时才真正重启服务。</summary>
        public static bool Restore(string owner)
        {
            lock (lk)
            {
                if (!SharedEffectClaim.Release(owners, owner)) return true;
                return RestoreFlagged();
            }
        }

        private static bool RestoreFlagged()
        {
            lock (lk)
            {
                string flag = Settings.LoadStr(Flag, "");
                if (flag.Length > 0)
                {
                    var remain = new List<string>();
                    foreach (string n in flag.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        bool ok = false;
                        try { ok = SvcCtl.EnsureStarted(n); } catch { }
                        if (!ok) remain.Add(n);
                    }
                    Settings.SaveStr(Flag, string.Join("|", remain.ToArray()));
                    if (remain.Count == 0) Logger.Log("索引/预取服务已恢复");
                    else Logger.Log("部分服务未能拉起（" + string.Join(",", remain.ToArray()) + "），标志保留待重试");
                }
                return Settings.LoadStr(Flag, "").Length == 0;
            }
        }

        /// <summary>交接直通：把服务暂停的占用权从一个占用方移交给另一个，底层保持暂停不动。</summary>
        public static bool HandoffOwner(string from, string to)
        {
            lock (lk) return SharedEffectClaim.Handoff(owners, from, to);
        }

        internal static bool HeldBy(string owner)
        {
            lock (lk) return owners.Contains(owner);
        }

        internal static bool PersistFlagPresentForTest()
        {
            lock (lk) return Settings.LoadStr(Flag, "").Length > 0;
        }

        public static void HealFromCrash() { if (Settings.LoadStr(Flag, "").Length > 0) Restore(); }
    }

    internal static class SvcState
    {
        public static bool StopTaken(int state) { return state == 1 || state == 3; }

        public static int Query(string name)
        {
            try
            {
                IntPtr scm = OpenSCManagerW(null, null, 1);
                if (scm == IntPtr.Zero) return 0;
                try
                {
                    IntPtr svc = OpenServiceW(scm, name, 0x4);
                    if (svc == IntPtr.Zero) return 0;
                    try
                    {
                        SERVICE_STATUS st;
                        return QueryServiceStatus(svc, out st) ? st.State : 0;
                    }
                    finally { CloseServiceHandle(svc); }
                }
                finally { CloseServiceHandle(scm); }
            }
            catch { return 0; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public int Type, State, ControlsAccepted, Win32ExitCode, SpecificExitCode, CheckPoint, WaitHint;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManagerW(string machine, string db, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenServiceW(IntPtr scm, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(IntPtr svc, out SERVICE_STATUS status);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);
    }
}
