// @author zenjiro 18967498922@163.com
// 文件用途 专注统计历史：按日一行的 TSV（最近 60 天截断），AtomicFile 原子写

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal sealed class FocusDayRecord
    {
        public string Day;      // yyyy-MM-dd
        public long Seconds;
        public int Sessions;
        public int Distract;    // 分心应用命中次数
        public int Blocked;     // 其中被阻断关闭的次数
    }

    internal static class FocusHistory
    {
        internal const int Keep = 60;
        private static readonly object lk = new object();
        private static string filePath;

        /// <summary>历史文件路径；测试可覆盖。默认 Paths.Data 下；自测进程未初始化
        /// Paths.Data 时退到临时目录，保证取值本身绝不抛异常（历史组件不许抛）。</summary>
        internal static string FilePath
        {
            get
            {
                if (filePath != null) return filePath;
                string data = Paths.Data ?? Path.GetTempPath();
                return Path.Combine(data, "focus-history.tsv");
            }
            set { filePath = value; }
        }

        /// <summary>按日合并增量写入：同日行累加，无则新建；超 Keep 天截掉最旧。
        /// 统计组件不许抛——读写失败静默（当日数据下次写入会再尝试重写整个文件）。</summary>
        public static void AppendOrUpdate(string day, long dSeconds, int dSessions, int dDistract, int dBlocked)
        {
            if (string.IsNullOrEmpty(day)) return;
            if (dSeconds == 0 && dSessions == 0 && dDistract == 0 && dBlocked == 0) return;
            lock (lk)
            {
                var all = LoadAll();
                FocusDayRecord hit = null;
                foreach (FocusDayRecord r in all)
                    if (string.Equals(r.Day, day, StringComparison.Ordinal)) { hit = r; break; }
                if (hit == null)
                {
                    hit = new FocusDayRecord();
                    hit.Day = day;
                    all.Add(hit);
                    all.Sort(CompareDay);
                }
                hit.Seconds += dSeconds;
                if (hit.Seconds < 0) hit.Seconds = 0;
                hit.Sessions = Math.Max(0, hit.Sessions + dSessions);
                hit.Distract = Math.Max(0, hit.Distract + dDistract);
                hit.Blocked = Math.Max(0, hit.Blocked + dBlocked);
                if (all.Count > Keep) all.RemoveRange(0, all.Count - Keep);
                Save(all);
            }
        }

        /// <summary>按日升序读全部历史；坏行（列数不足/日期非法）跳过。</summary>
        public static List<FocusDayRecord> LoadAll()
        {
            lock (lk)
            {
                var list = new List<FocusDayRecord>();
                try
                {
                    string p = FilePath;
                    if (!File.Exists(p)) return list;
                    foreach (string line in File.ReadAllLines(p))
                    {
                        string[] f = line.Split('\t');
                        if (f.Length < 5) continue;
                        DateTime d;
                        if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd", null,
                            System.Globalization.DateTimeStyles.None, out d)) continue;
                        FocusDayRecord r = new FocusDayRecord();
                        r.Day = f[0];
                        long sec; long.TryParse(f[1], out sec); r.Seconds = Math.Max(0, sec);
                        int n; int.TryParse(f[2], out n); r.Sessions = Math.Max(0, n);
                        int dis; int.TryParse(f[3], out dis); r.Distract = Math.Max(0, dis);
                        int blk; int.TryParse(f[4], out blk); r.Blocked = Math.Max(0, blk);
                        list.Add(r);
                    }
                    list.Sort(CompareDay);
                }
                catch { }
                return list;
            }
        }

        /// <summary>最近 n 天（含 today），缺日补零，老→新。</summary>
        public static List<FocusDayRecord> LastDays(int n, DateTime today)
        {
            var byDay = new Dictionary<string, FocusDayRecord>(StringComparer.Ordinal);
            foreach (FocusDayRecord r in LoadAll()) byDay[r.Day] = r;
            var list = new List<FocusDayRecord>();
            for (int i = n - 1; i >= 0; i--)
            {
                string day = today.AddDays(-i).ToString("yyyy-MM-dd");
                FocusDayRecord hit;
                if (!byDay.TryGetValue(day, out hit))
                {
                    hit = new FocusDayRecord();
                    hit.Day = day;
                }
                list.Add(hit);
            }
            return list;
        }

        private static int CompareDay(FocusDayRecord a, FocusDayRecord b)
        {
            return string.CompareOrdinal(a.Day, b.Day);
        }

        private static void Save(List<FocusDayRecord> all)
        {
            try
            {
                var lines = new List<string>();
                foreach (FocusDayRecord r in all)
                    lines.Add(r.Day + "\t" + r.Seconds + "\t" + r.Sessions + "\t" + r.Distract + "\t" + r.Blocked);
                AtomicFile.WriteLines(FilePath, lines.ToArray(), "FocusHistory");
            }
            catch { }
        }
    }
}
