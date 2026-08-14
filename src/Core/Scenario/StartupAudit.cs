// @author zenjiro 18967498922@163.com
// 文件用途 启动项审查：枚举 Run 键与启动文件夹，与基线快照对比，只报告新增项（不自动删除）

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class StartupAudit
    {
        internal sealed class Entry
        {
            public string Source;
            public string Name;
            public string Command;

            public Entry(string source, string name, string command)
            {
                Source = source;
                Name = name;
                Command = command;
            }
        }

        public static string BaselinePath { get { return Path.Combine(Paths.Data, "Caelus.startup.baseline"); } }

        /// <summary>只读枚举当前启动项：HKCU/HKLM Run 键 + 当前用户启动文件夹</summary>
        public static List<Entry> ScanCurrent()
        {
            var list = new List<Entry>();
            ScanRunKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run", list);
            ScanRunKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run", list);
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(folder))
                    foreach (string f in Directory.GetFiles(folder))
                        list.Add(new Entry("StartupFolder", Path.GetFileName(f), ""));
            }
            catch { }
            return list;
        }

        private static void ScanRunKey(RegistryKey hive, string subKey, string source, List<Entry> list)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(subKey))
                {
                    if (k == null) return;
                    foreach (string name in k.GetValueNames())
                    {
                        string cmd = "";
                        try { cmd = Convert.ToString(k.GetValue(name, "")); } catch { }
                        list.Add(new Entry(source, name, cmd));
                    }
                }
            }
            catch { }
        }

        /// <summary>新增项 = 当前有而基线没有（Source+Name 为键）。纯逻辑可单测。</summary>
        internal static List<Entry> DiffNew(List<Entry> current, List<Entry> baseline)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in baseline) known.Add(KeyOf(e));
            var added = new List<Entry>();
            foreach (Entry e in current)
                if (known.Add(KeyOf(e))) added.Add(e);
            return added;
        }

        private static string KeyOf(Entry e) { return e.Source + "|" + e.Name; }

        public static List<Entry> LoadBaseline(string path)
        {
            var list = new List<Entry>();
            try
            {
                if (!File.Exists(path)) return list;
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    list.Add(new Entry(Unesc(parts[0]), Unesc(parts[1]), Unesc(parts[2])));
                }
            }
            catch { }
            return list;
        }

        public static void SaveBaseline(string path, List<Entry> entries)
        {
            try
            {
                var lines = new List<string>();
                foreach (Entry e in entries)
                    lines.Add(Esc(e.Source) + "\t" + Esc(e.Name) + "\t" + Esc(e.Command));
                AtomicFile.WriteLines(path, lines.ToArray(), "StartupAudit baseline");
            }
            catch { }
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // 转义是歧义的（"\\t" 既可能是字面反斜杠+t，也可能是转义后的 TAB），
            // 连续 Replace 无法正确处理，必须单趟从左到右扫描：
            //   "\\" → 字面反斜杠；"\\t/\\r/\\n" → 对应控制字符；其余原样保留。
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                    if (next == 't') { sb.Append('\t'); i++; continue; }
                    if (next == 'r') { sb.Append('\r'); i++; continue; }
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
