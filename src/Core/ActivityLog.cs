// @author zenjiro 18967498922@163.com
// 文件用途 实时监控动作环形日志：核心侧「用户可感知动作」的内存缓冲（60 条），
//           供实时监控页 2 秒轮询展示——不走文件日志，零 IO、故障静默

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class ActivityEntry
    {
        public long Ticks;   // DateTime.UtcNow.Ticks
        public string Text;
    }

    internal static class ActivityLog
    {
        internal const int Cap = 60;
        private static readonly object lk = new object();
        private static readonly List<ActivityEntry> entries = new List<ActivityEntry>();

        /// <summary>记录一条动作。任何路径调用都不许抛异常——监控不得反噬业务。</summary>
        public static void Add(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                lock (lk)
                {
                    entries.Add(new ActivityEntry { Ticks = DateTime.UtcNow.Ticks, Text = text });
                    if (entries.Count > Cap) entries.RemoveRange(0, entries.Count - Cap);
                }
            }
            catch { }
        }

        /// <summary>最近 max 条，新→旧。返回副本，调用方随便用。</summary>
        public static List<ActivityEntry> Recent(int max)
        {
            var list = new List<ActivityEntry>();
            if (max <= 0) return list;
            try
            {
                lock (lk)
                {
                    for (int i = entries.Count - 1; i >= 0 && list.Count < max; i--)
                        list.Add(entries[i]);
                }
            }
            catch { }
            return list;
        }

#if CAELUS_SELFTEST
        public static void ResetForTest()
        {
            lock (lk) entries.Clear();
        }
#endif
    }
}
