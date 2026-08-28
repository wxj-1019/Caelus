// @author zenjiro 18967498922@163.com
// 文件用途 识别网游加速器进程 使其免于后台压制与冻结

using System;
using System.Collections.Generic;

namespace CaelusApp
{

    internal static class NetAcceleratorCatalog
    {

        private static readonly HashSet<string> ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {

            "uu", "uu_ball",
            "xunyou",
            "leigod", "leigod_launcher", "leishensdk",
            "qiyou",
            "biubiu", "bbservice",
            "dolphinq",
            "wtfast",
            // Watt Toolkit（原 Steam++）与 OurPlay：近年常见款
            "steam++", "ourplay",
        };

        private static readonly string[] Tokens =
        {

            "accelerat", "booster", "加速器",

            "xunyou", "leigod", "leishen", "qiyou", "biubiu", "dolphinq",
            "wtfast", "exitlag", "noping", "mudfish", "steam++", "ourplay"
        };

        // 用户自定义加速器进程名（分号/换行分隔，注册表存储），与 AntiCheatCatalog 同机制
        private const string CustomKey = "CustomAccelerators";
        private static readonly object CustomLock = new object();
        private static HashSet<string> customNames;

        public static string CustomList
        {
            get { return Settings.LoadStr(CustomKey, ""); }
            set { Settings.SaveStr(CustomKey, value ?? ""); lock (CustomLock) customNames = null; }
        }

        internal static bool IsAcceleratorLikeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string bare = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4) : name;
            if (ProcessNames.Contains(bare)) return true;
            HashSet<string> custom = LoadCustom();
            if (custom != null && custom.Contains(bare)) return true;
            string low = bare.ToLowerInvariant();
            foreach (string t in Tokens) if (low.Contains(t)) return true;
            return false;
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
