// @author zenjiro 18967498922@163.com
// 文件用途 专注时长统计：累计开发专注掌权时长与会话次数，按日历天归零

using System;

namespace CaelusApp
{
    internal static class FocusStats
    {
        private const string DayKey = "FocusStatsDay";
        private const string SecKey = "FocusStatsSeconds";
        private const string NKey = "FocusStatsSessions";
        private static readonly object sync = new object();

        /// <summary>记录一段开发专注掌权时长（Suspend 时调用）。</summary>
        internal static void RecordSession(long elapsedTicks)
        {
            RecordSession(elapsedTicks, DateTime.Now);
        }

        /// <summary>纯逻辑可单测：记录时长（按 now 的日历天归零重计）。</summary>
        internal static void RecordSession(long elapsedTicks, DateTime now)
        {
            if (elapsedTicks <= 0) return;
            string today = now.ToString("yyyy-MM-dd");
            lock (sync)
            {
                if (Settings.LoadStr(DayKey, "") != today)
                {
                    Settings.SaveStr(DayKey, today);
                    Settings.SaveStr(SecKey, "0");
                    Settings.SaveStr(NKey, "0");
                }
                long sec = LoadLong(SecKey) + elapsedTicks / TimeSpan.TicksPerSecond;
                int n = LoadInt(NKey) + 1;
                Settings.SaveStr(SecKey, sec.ToString());
                Settings.SaveStr(NKey, n.ToString());
            }
        }

        internal static long TodaySeconds(DateTime now)
        {
            if (Settings.LoadStr(DayKey, "") != now.ToString("yyyy-MM-dd")) return 0;
            return LoadLong(SecKey);
        }

        internal static int TodaySessions(DateTime now)
        {
            if (Settings.LoadStr(DayKey, "") != now.ToString("yyyy-MM-dd")) return 0;
            return LoadInt(NKey);
        }

        private static long LoadLong(string key)
        {
            long v;
            return long.TryParse(Settings.LoadStr(key, "0"), out v) && v > 0 ? v : 0;
        }

        private static int LoadInt(string key)
        {
            int v;
            return int.TryParse(Settings.LoadStr(key, "0"), out v) && v > 0 ? v : 0;
        }

#if CAELUS_SELFTEST
        internal static void ResetForTest()
        {
            Settings.SaveStr(DayKey, "");
            Settings.SaveStr(SecKey, "0");
            Settings.SaveStr(NKey, "0");
        }
#endif
    }
}
