// @author zenjiro 18967498922@163.com
// 文件用途 场景统计历史：按日一行的 TSV（最近 60 天截断），AtomicFile 原子写。
//           8 列统一存开发/日常两侧统计；旧 5 列行（开发侧早期版本）无损读取。
//           列序：day focusSec focusSessions distract blocked dailySec dailySessions buildSec

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal sealed class FocusDayRecord
    {
        public string Day;              // yyyy-MM-dd
        public long FocusSeconds;
        public int FocusSessions;
        public int Distract;            // 分心应用命中次数
        public int Blocked;             // 其中被阻断关闭的次数
        public long DailySeconds;       // 日常场景掌权时长
        public int DailySessions;
        public long BuildSeconds;       // 编译会话累计时长
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

        /// <summary>按日合并增量写入：同日行累加 delta 的全部字段，无则新建行；
        /// 超 Keep 天截掉最旧。统计组件不许抛——读写失败静默（下次写入会重试全量重写）。</summary>
        public static void AppendOrUpdate(FocusDayRecord delta)
        {
            if (delta == null || string.IsNullOrEmpty(delta.Day)) return;
            if (delta.FocusSeconds == 0 && delta.FocusSessions == 0 && delta.Distract == 0
                && delta.Blocked == 0 && delta.DailySeconds == 0 && delta.DailySessions == 0
                && delta.BuildSeconds == 0) return;
            lock (lk)
            {
                var all = LoadAll();
                FocusDayRecord hit = null;
                foreach (FocusDayRecord r in all)
                    if (string.Equals(r.Day, delta.Day, StringComparison.Ordinal)) { hit = r; break; }
                if (hit == null)
                {
                    hit = new FocusDayRecord();
                    hit.Day = delta.Day;
                    all.Add(hit);
                    all.Sort(CompareDay);
                }
                hit.FocusSeconds = Math.Max(0, hit.FocusSeconds + delta.FocusSeconds);
                hit.FocusSessions = Math.Max(0, hit.FocusSessions + delta.FocusSessions);
                hit.Distract = Math.Max(0, hit.Distract + delta.Distract);
                hit.Blocked = Math.Max(0, hit.Blocked + delta.Blocked);
                hit.DailySeconds = Math.Max(0, hit.DailySeconds + delta.DailySeconds);
                hit.DailySessions = Math.Max(0, hit.DailySessions + delta.DailySessions);
                hit.BuildSeconds = Math.Max(0, hit.BuildSeconds + delta.BuildSeconds);
                if (all.Count > Keep) all.RemoveRange(0, all.Count - Keep);
                Save(all);
            }
        }

        /// <summary>按日升序读全部历史；坏行跳过。旧 5 列行（day/focusSec/focusSessions/distract/blocked）
        /// 读为尾部三列补零。</summary>
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
                        DateTime d;
                        if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd", null,
                            System.Globalization.DateTimeStyles.None, out d)) continue;
                        FocusDayRecord r = new FocusDayRecord();
                        r.Day = f[0];
                        long sec;
                        if (f.Length >= 8)
                        {
                            int n;
                            long.TryParse(f[1], out sec); r.FocusSeconds = Math.Max(0, sec);
                            int.TryParse(f[2], out n); r.FocusSessions = Math.Max(0, n);
                            int.TryParse(f[3], out n); r.Distract = Math.Max(0, n);
                            int.TryParse(f[4], out n); r.Blocked = Math.Max(0, n);
                            long.TryParse(f[5], out sec); r.DailySeconds = Math.Max(0, sec);
                            int.TryParse(f[6], out n); r.DailySessions = Math.Max(0, n);
                            long.TryParse(f[7], out sec); r.BuildSeconds = Math.Max(0, sec);
                        }
                        else if (f.Length == 5)
                        {
                            int n;
                            long.TryParse(f[1], out sec); r.FocusSeconds = Math.Max(0, sec);
                            int.TryParse(f[2], out n); r.FocusSessions = Math.Max(0, n);
                            int.TryParse(f[3], out n); r.Distract = Math.Max(0, n);
                            int.TryParse(f[4], out n); r.Blocked = Math.Max(0, n);
                        }
                        else continue;   // 6/7 列或其他坏行直接跳过
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
                    lines.Add(r.Day + "\t" + r.FocusSeconds + "\t" + r.FocusSessions + "\t"
                        + r.Distract + "\t" + r.Blocked + "\t" + r.DailySeconds + "\t"
                        + r.DailySessions + "\t" + r.BuildSeconds);
                AtomicFile.WriteLines(FilePath, lines.ToArray(), "FocusHistory");
            }
            catch { }
        }
    }
}
