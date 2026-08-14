// @author zenjiro 18967498922@163.com
// 文件用途 分心应用清单：专注模式期间命中清单的新进程触发一次性托盘提醒（不强制处理）

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class DistractCatalog
    {
        private const string CustomKey = "DevFocusDistractList";
        private static readonly object CustomLock = new object();
        private static HashSet<string> customNames;

        public static bool IsMatch(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name;
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - 4);
            return LoadCustom().Contains(n);
        }

        /// <summary>设置页保存清单后调用，刷新缓存</summary>
        public static void Reload()
        {
            lock (CustomLock) customNames = null;
        }

        private static HashSet<string> LoadCustom()
        {
            lock (CustomLock)
            {
                if (customNames != null) return customNames;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = Settings.LoadStr(CustomKey, "");
                if (raw != null)
                    foreach (string part in raw.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = part.Trim();
                        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            t = t.Substring(0, t.Length - 4);
                        if (t.Length > 0) set.Add(t);
                    }
                customNames = set;
                return set;
            }
        }
    }
}
