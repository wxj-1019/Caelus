// @author zenjiro 18967498922@163.com
// 文件用途 开发服务名录：注册表 DevServiceList，守护与压制豁免共用

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class DevServiceCatalog
    {
        private const string Key = "DevServiceList";
        private static readonly object Lock = new object();
        private static HashSet<string> names;

        /// <summary>是否命中已注册的开发服务名（去 .exe 后缀、忽略大小写）。</summary>
        public static bool IsMatch(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name;
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - 4);
            return Load().Contains(n);
        }

        /// <summary>设置页保存清单后调用，刷新缓存。</summary>
        public static void Reload()
        {
            lock (Lock) names = null;
        }

        private static HashSet<string> Load()
        {
            lock (Lock)
            {
                if (names != null) return names;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = Settings.LoadStr(Key, "");
                if (raw != null)
                    foreach (string part in raw.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = part.Trim();
                        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            t = t.Substring(0, t.Length - 4);
                        if (t.Length > 0) set.Add(t);
                    }
                names = set;
                return set;
            }
        }
    }
}
