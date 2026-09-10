// @author zenjiro 18967498922@163.com
// 文件用途 维护历史：追加式 TSV（最近 50 条截断），AtomicFile 原子写

using System;
using System.Collections.Generic;
using System.IO;

namespace CaelusApp
{
    internal static class HealthHistory
    {
        internal const int Keep = 50;
        private static readonly object lk = new object();
        private static int seq;
        private static string filePath;

        /// <summary>历史文件路径；测试可覆盖。默认 Paths.Data 下；自测进程未初始化
        /// Paths.Data 时退到临时目录，保证取值本身绝不抛异常（历史组件不许抛）。</summary>
        internal static string FilePath
        {
            get
            {
                if (filePath != null) return filePath;
                string data = Paths.Data ?? Path.GetTempPath();
                return Path.Combine(data, "health-history.tsv");
            }
            set { filePath = value; }
        }

        public static void Append(HealthRecord r)
        {
            if (r == null) return;
            lock (lk)
            {
                if (string.IsNullOrEmpty(r.Id))
                    r.Id = DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + (++seq);
                var all = LoadAll();
                all.Add(r);
                if (all.Count > Keep) all.RemoveRange(0, all.Count - Keep);
                Save(all);
            }
        }

        public static List<HealthRecord> LoadAll()
        {
            lock (lk)
            {
                var list = new List<HealthRecord>();
                try
                {
                    string p = FilePath;
                    if (!File.Exists(p)) return list;
                    foreach (string line in File.ReadAllLines(p))
                    {
                        string[] f = line.Split('\t');
                        if (f.Length < 9) continue;
                        HealthRecord r = new HealthRecord();
                        r.Id = f[0];
                        DateTime t; DateTime.TryParse(f[1], out t); r.Time = t;
                        r.Trigger = f[2];
                        r.ActionId = f[3];
                        HealthOutcome oc; r.Outcome = Enum.TryParse(f[4], out oc) ? oc : HealthOutcome.Failed;
                        long fb; long.TryParse(f[5], out fb); r.FreedBytes = fb;
                        int ic; int.TryParse(f[6], out ic); r.ItemCount = ic;
                        r.Summary = HealthEsc.Unesc(f[7]);
                        r.UndoPayload = HealthEsc.Unesc(f[8]);
                        list.Add(r);
                    }
                }
                catch { }
                return list;
            }
        }

        public static HealthRecord Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (HealthRecord r in LoadAll())
                if (string.Equals(r.Id, id, StringComparison.Ordinal)) return r;
            return null;
        }

        private static void Save(List<HealthRecord> all)
        {
            try
            {
                var lines = new List<string>();
                foreach (HealthRecord r in all)
                    lines.Add(r.Id + "\t" + r.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\t"
                        + r.Trigger + "\t" + r.ActionId + "\t" + r.Outcome + "\t"
                        + r.FreedBytes + "\t" + r.ItemCount + "\t"
                        + HealthEsc.Esc(r.Summary) + "\t" + HealthEsc.Esc(r.UndoPayload));
                AtomicFile.WriteLines(FilePath, lines.ToArray(), "HealthHistory");
            }
            catch { }
        }
    }
}
