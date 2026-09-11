// @author zenjiro 18967498922@163.com
// 文件用途 专注时长统计：累计开发专注掌权时长、会话次数与分心命中，按日历天归零；
// 同时合并写 FocusHistory 按日 TSV（趋势数据源），任一侧失败不影响另一侧

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class FocusStats
    {
        private const string DayKey = "FocusStatsDay";
        private const string SecKey = "FocusStatsSeconds";
        private const string NKey = "FocusStatsSessions";
        private const string DistractKey = "FocusStatsDistract";
        private const string BlockedKey = "FocusStatsBlocked";
        private const string DistractNamesKey = "FocusStatsDistractNames";
        private const int DistractNamesTop = 8;
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
            long sec = elapsedTicks / TimeSpan.TicksPerSecond;
            lock (sync)
            {
                EnsureDayLocked(today);
                Settings.SaveStr(SecKey, (LoadLong(SecKey) + sec).ToString());
                Settings.SaveStr(NKey, (LoadInt(NKey) + 1).ToString());
            }
            // 历史合并失败不影响今日键（统计不许反噬挂起路径）
            try { FocusHistory.AppendOrUpdate(today, sec, 1, 0, 0); } catch { }
        }

        /// <summary>记录一次分心应用命中（专注模式掌权期间）。wasBlocked=本次被阻断关闭。</summary>
        internal static void RecordDistract(bool wasBlocked)
        {
            RecordDistract(wasBlocked, "", DateTime.Now);
        }

        /// <summary>纯逻辑可单测：记录分心命中（按 now 的日历天归零重计）。name 用于今日按名 Top 统计。</summary>
        internal static void RecordDistract(bool wasBlocked, string name, DateTime now)
        {
            string today = now.ToString("yyyy-MM-dd");
            lock (sync)
            {
                EnsureDayLocked(today);
                Settings.SaveStr(DistractKey, (LoadInt(DistractKey) + 1).ToString());
                if (wasBlocked) Settings.SaveStr(BlockedKey, (LoadInt(BlockedKey) + 1).ToString());
                if (!string.IsNullOrEmpty(name)) UpdateDistractNamesLocked(name);
            }
            try { FocusHistory.AppendOrUpdate(today, 0, 0, 1, wasBlocked ? 1 : 0); } catch { }
        }

        /// <summary>不带名版本（不参与按名统计）。</summary>
        internal static void RecordDistract(bool wasBlocked, DateTime now)
        {
            RecordDistract(wasBlocked, "", now);
        }

        /// <summary>今日分心按名 Top 文案（"steam×2 · discord×1"），无命中返回空串。</summary>
        internal static string TodayDistractTopText(DateTime now)
        {
            if (Settings.LoadStr(DayKey, "") != now.ToString("yyyy-MM-dd")) return "";
            return FormatDistractNames(Settings.LoadStr(DistractNamesKey, ""));
        }

        /// <summary>锁内：命中计数并入今日按名表（name:count;... 按次数降序截 Top N）。
        /// 名字在存储层归一（去 .exe/Trim/大小写不敏感合并）；进程名不含 ';' 与 ':'
        /// （文件名非法字符），格式天然安全。</summary>
        private static void UpdateDistractNamesLocked(string name)
        {
            name = name.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (name.Length == 0) return;
            if (name.Length > 32) name = name.Substring(0, 32);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in Settings.LoadStr(DistractNamesKey, "").Split(';'))
            {
                int sep = part.LastIndexOf(':');
                if (sep <= 0) continue;
                int c;
                if (!int.TryParse(part.Substring(sep + 1), out c) || c <= 0) continue;
                counts[part.Substring(0, sep)] = c;
            }
            int cur;
            counts.TryGetValue(name, out cur);
            counts[name] = cur + 1;
            var ordered = new List<KeyValuePair<string, int>>(counts);
            ordered.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                if (a.Value != b.Value) return b.Value - a.Value;
                return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < ordered.Count && i < DistractNamesTop; i++)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(ordered[i].Key).Append(':').Append(ordered[i].Value);
            }
            Settings.SaveStr(DistractNamesKey, sb.ToString());
        }

        private static string FormatDistractNames(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            var top = new List<KeyValuePair<string, int>>();
            foreach (string part in stored.Split(';'))
            {
                int sep = part.LastIndexOf(':');
                if (sep <= 0) continue;
                int c;
                if (!int.TryParse(part.Substring(sep + 1), out c) || c <= 0) continue;
                top.Add(new KeyValuePair<string, int>(part.Substring(0, sep), c));
            }
            if (top.Count == 0) return "";
            top.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                if (a.Value != b.Value) return b.Value - a.Value;
                return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < top.Count; i++)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(top[i].Key).Append('×').Append(top[i].Value);
            }
            return sb.ToString();
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

        internal static int TodayDistract(DateTime now)
        {
            if (Settings.LoadStr(DayKey, "") != now.ToString("yyyy-MM-dd")) return 0;
            return LoadInt(DistractKey);
        }

        internal static int TodayBlocked(DateTime now)
        {
            if (Settings.LoadStr(DayKey, "") != now.ToString("yyyy-MM-dd")) return 0;
            return LoadInt(BlockedKey);
        }

        /// <summary>锁内：日切时重置今日计数键。</summary>
        private static void EnsureDayLocked(string today)
        {
            if (Settings.LoadStr(DayKey, "") == today) return;
            Settings.SaveStr(DayKey, today);
            Settings.SaveStr(SecKey, "0");
            Settings.SaveStr(NKey, "0");
            Settings.SaveStr(DistractKey, "0");
            Settings.SaveStr(BlockedKey, "0");
            Settings.SaveStr(DistractNamesKey, "");
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
            Settings.SaveStr(DistractKey, "0");
            Settings.SaveStr(BlockedKey, "0");
            Settings.SaveStr(DistractNamesKey, "");
        }
#endif
    }
}
