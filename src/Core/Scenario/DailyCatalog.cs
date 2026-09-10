// @author zenjiro 18967498922@163.com
// 文件用途 日常应用家族识别：浏览器/Office/会议，进程名 + 安装目录双重校验

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static class DailyCatalog
    {
        private sealed class Entry
        {
            public readonly string Name;
            public readonly string[] Roots;
            public Entry(string name, string[] roots) { Name = name; Roots = roots; }
        }

        private static string Pf { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } }
        private static string Pf86 { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86); } }
        private static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        private static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }

        private static Entry[] BuildEntries()
        {
            return new[]
            {
                new Entry("chrome", new[] { Path.Combine(Pf, @"Google\Chrome\Application\"), Path.Combine(Pf86, @"Google\Chrome\Application\") }),
                new Entry("msedge", new[] { Path.Combine(Pf86, @"Microsoft\Edge\Application\"), Path.Combine(Pf, @"Microsoft\Edge\Application\") }),
                new Entry("firefox", new[] { Path.Combine(Pf, @"Mozilla Firefox\"), Path.Combine(Pf86, @"Mozilla Firefox\") }),
                new Entry("brave", new[] { Path.Combine(Pf, @"BraveSoftware\Brave-Browser\Application\"), Path.Combine(Pf86, @"BraveSoftware\Brave-Browser\Application\") }),
                new Entry("winword", new[] { Path.Combine(Pf, @"Microsoft Office\"), Path.Combine(Pf86, @"Microsoft Office\") }),
                new Entry("excel", new[] { Path.Combine(Pf, @"Microsoft Office\"), Path.Combine(Pf86, @"Microsoft Office\") }),
                new Entry("powerpnt", new[] { Path.Combine(Pf, @"Microsoft Office\"), Path.Combine(Pf86, @"Microsoft Office\") }),
                new Entry("outlook", new[] { Path.Combine(Pf, @"Microsoft Office\"), Path.Combine(Pf86, @"Microsoft Office\") }),
                new Entry("wps", new[] { Path.Combine(Local, @"Kingsoft\"), Path.Combine(Pf, @"Kingsoft\"), Path.Combine(Pf86, @"Kingsoft\") }),
                new Entry("zoom", new[] { Path.Combine(Roaming, @"Zoom\bin\") }),
                new Entry("teams", new[] { Path.Combine(Local, @"Microsoft\Teams\") }),
                new Entry("feishu", new[] { Path.Combine(Local, @"Feishu\"), Path.Combine(Pf, @"Feishu\") }),
                new Entry("dingtalk", new[] { Path.Combine(Pf86, @"DingDing\"), Path.Combine(Pf, @"DingDing\") })
            };
        }

        private static readonly object Sync = new object();
        private static Dictionary<string, Entry> byName;

        private const string CustomKey = "CustomDailyProcs";
        private static HashSet<string> customNames;

        /// <summary>自定义日常进程名（分号/换行分隔），存注册表，设置页可编辑。无安装目录锚点，按名匹配。</summary>
        public static string CustomList
        {
            get { return Settings.LoadStr(CustomKey, ""); }
            set { Settings.SaveStr(CustomKey, value ?? ""); lock (Sync) customNames = null; }
        }

        private static HashSet<string> LoadCustom()
        {
            lock (Sync)
            {
                if (customNames != null) return customNames;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = Settings.LoadStr(CustomKey, "");
                if (raw != null)
                    foreach (string part in raw.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = part.Trim();
                        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
                        if (t.Length > 0) set.Add(t);
                    }
                customNames = set;
                return set;
            }
        }

        private static Dictionary<string, Entry> Map()
        {
            lock (Sync)
            {
                if (byName != null) return byName;
                var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (Entry e in BuildEntries()) map[e.Name] = e;
                byName = map;
                return map;
            }
        }

        public static bool NameMatches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string bare = StripExe(name);
            return Map().ContainsKey(bare) || LoadCustom().Contains(bare);
        }

        public static bool IsMatch(string name, string path)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string bare = StripExe(name);
            Entry e;
            if (Map().TryGetValue(bare, out e))
            {
                if (string.IsNullOrEmpty(path)) return false;
                string full = path;
                try { full = Path.GetFullPath(path); } catch { }
                foreach (string prefix in e.Roots)
                    if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            return LoadCustom().Contains(bare);   // 自定义名录：无目录锚点，按名匹配
        }

        private static string StripExe(string name)
        {
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - 4);
            return name;
        }
    }
}
