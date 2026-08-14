// @author zenjiro 18967498922@163.com
// 文件用途 系统健康维护：到点判定与执行编排（仅 DailyCare 掌权期调用）

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class HealthCare
    {
        /// <summary>到点判定（纯逻辑可单测）：从未运行/损坏数据/超过间隔 → true</summary>
        internal static bool IsDue(string lastRunStamp, int intervalDays, DateTime today)
        {
            if (intervalDays < 1) intervalDays = 1;
            DateTime last;
            if (!DateTime.TryParse(lastRunStamp, out last)) return true;
            return (today.Date - last.Date).TotalDays >= intervalDays;
        }

        /// <summary>读取配置的维护间隔（天），默认 1</summary>
        public static int IntervalDays()
        {
            int days;
            if (!int.TryParse(Settings.LoadStr("HealthIntervalDays", "1"), out days) || days < 1) return 1;
            return days > 30 ? 30 : days;
        }

        /// <summary>到点则执行：着色器缓存清理 + 启动项审查。只在 DailyCare 掌权期被调用。</summary>
        public static void RunIfDue()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!IsDue(Settings.LoadStr("HealthLastRun", ""), IntervalDays(), DateTime.Now)) return;

            try
            {
                long beforeBytes = ShaderCache.MeasureBytes();
                if (beforeBytes > 64L * 1024 * 1024)
                {
                    CacheSweep.Result r = ShaderCache.Clean();
                    Logger.Log("健康维护：着色器缓存清理 " + CacheSweep.FmtBytes(beforeBytes)
                        + "（释放 " + CacheSweep.FmtBytes(r.FreedBytes) + "）");
                }
            }
            catch (Exception ex) { Logger.LogFailure("健康维护：着色器清理失败", ex); }

            try
            {
                var current = StartupAudit.ScanCurrent();
                var baseline = StartupAudit.LoadBaseline(StartupAudit.BaselinePath);
                var added = StartupAudit.DiffNew(current, baseline);
                if (baseline.Count > 0 && added.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var e in added) names.Add(e.Name + "（" + e.Source + "）");
                    string news = string.Join("、", names.ToArray());
                    if (news.Length > 300) news = news.Substring(0, 300) + "...";
                    Settings.SaveStr("HealthStartupNews", news);
                    Logger.Log("健康维护：发现 " + added.Count + " 个新启动项：" + news);
                }
                StartupAudit.SaveBaseline(StartupAudit.BaselinePath, current);
            }
            catch (Exception ex) { Logger.LogFailure("健康维护：启动项审查失败", ex); }

            Settings.SaveStr("HealthLastRun", today);
        }
    }
}
