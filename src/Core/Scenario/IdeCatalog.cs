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

        /// <summary>名称预筛（零开销，事件热路径先用它过滤）</summary>
        public static bool NameMatches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = StripExe(name);
            return Map().ContainsKey(n);
        }

        /// <summary>双校验：名称命中且路径位于该 IDE 的已知安装目录前缀下</summary>
        public static bool IsMatch(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return false;
            IdeEntry e;
            if (!Map().TryGetValue(StripExe(name), out e)) return false;
            string full = path;
            try { full = Path.GetFullPath(path); } catch { }
            foreach (string prefix in e.RootPrefixes)
                if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string StripExe(string name)
        {
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - 4);
            return name;
        }
    }
}
