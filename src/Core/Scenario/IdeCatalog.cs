// @author zenjiro 18967498922@163.com
// 文件用途 IDE 家族识别：进程名 + 安装目录双重校验（防同名进程误伤）

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static class IdeCatalog
    {
        private sealed class IdeEntry
        {
            public readonly string Name;
            public readonly string[] RootPrefixes;

            public IdeEntry(string name, string[] rootPrefixes)
            {
                Name = name;
                RootPrefixes = rootPrefixes;
            }
        }

        private static string Pf { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } }
        private static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        private static IdeEntry[] BuildEntries()
        {
            return new[]
            {
                new IdeEntry("devenv", new[] { Path.Combine(Pf, @"Microsoft Visual Studio\") }),
                new IdeEntry("rider64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("code", new[] { Path.Combine(Local, @"Programs\Microsoft VS Code\"), Path.Combine(Pf, @"Microsoft VS Code") }),
                new IdeEntry("cursor", new[] { Path.Combine(Local, @"Programs\cursor\"), Path.Combine(Local, @"Programs\Cursor\") }),
                new IdeEntry("idea64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("webstorm64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("goland64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("clion64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("pycharm64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                // 数据库客户端 / 移动 IDE（进程名 + 安装目录双校验）
                new IdeEntry("ssms", new[] { Path.Combine(Pf, @"Microsoft SQL Server Management Studio\") }),
                new IdeEntry("datagrip64", new[] { Path.Combine(Pf, @"JetBrains\"), Path.Combine(Local, @"Programs\") }),
                new IdeEntry("dbeaver", new[] { Path.Combine(Pf, @"DBeaver\"), Path.Combine(Local, @"Programs\DBeaver\") }),
                new IdeEntry("studio64", new[] { Path.Combine(Pf, @"Android\Android Studio\"), Path.Combine(Local, @"Programs\Android Studio\") }),
                new IdeEntry("azuredatastudio", new[] { Path.Combine(Local, @"Programs\Azure Data Studio\"), Path.Combine(Pf, @"Azure Data Studio\") }),
                new IdeEntry("mysqlworkbench", new[] { Path.Combine(Pf, @"MySQL\MySQL Workbench\") })
            };
        }

        private static readonly object Sync = new object();
        private static Dictionary<string, IdeEntry> byName;
        private const string CustomKey = "CustomIdeProcs";
        private static HashSet<string> customNames;

        /// <summary>自定义 IDE 进程名（分号/换行分隔），存注册表，设置页可编辑。
        /// 无安装目录锚点，按名匹配（对齐 CustomDailyProcs/CustomBuildProcs 模式）。</summary>
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

        private static Dictionary<string, IdeEntry> Map()
        {
            lock (Sync)
            {
                if (byName != null) return byName;
                var map = new Dictionary<string, IdeEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (IdeEntry e in BuildEntries()) map[e.Name] = e;
                byName = map;
                return map;
            }
        }

        /// <summary>名称预筛（零开销，事件热路径先用它过滤）。内置与自定义名录合并。</summary>
        public static bool NameMatches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = StripExe(name);
            return Map().ContainsKey(n) || LoadCustom().Contains(n);
        }

        /// <summary>双校验：内置名录名称命中且路径位于已知安装目录前缀下；
        /// 自定义名录无目录锚点，按名即真。</summary>
        public static bool IsMatch(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return false;
            string bare = StripExe(name);
            IdeEntry e;
            if (Map().TryGetValue(bare, out e))
            {
                string full = path;
                try { full = Path.GetFullPath(path); } catch { }
                foreach (string prefix in e.RootPrefixes)
                    if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            return LoadCustom().Contains(bare);
        }

        private static string StripExe(string name)
        {
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - 4);
            return name;
        }
    }
}
