// @author zenjiro 18967498922@163.com
// 文件用途 系统健康维护：到点判定与执行编排（独立定时调度，与 DailyCare 掌权解耦）

using System;
using System.Threading;

namespace CaelusApp
{
    internal static class HealthCare
    {
        private static readonly object autoSync = new object();
        private static Timer autoTimer;
        private static int runningFlag;

        /// <summary>测试挂钩：替换动作目录（生产为 null 用 HealthCatalog.Shared）</summary>
        internal static HealthActionCatalog CatalogOverride;

        /// <summary>忙时门控（宿主接线）：游戏进行中时本轮跳过——着色器缓存是游戏
        /// 运行时热用的文件，对局中清理＝全量重编译卡顿，对游戏优化工具是反向操作。
        /// 未接线时视为空闲（自测场景）。</summary>
        public static Func<bool> ShouldDefer;

        /// <summary>启动独立维护调度。此前唯一的到点判定挂在 DailyCare 掌权节拍上，
        /// 市电台式机且不开日常家族、或长期被游戏/开发场景抢占的用户永远等不到掌权，
        /// 健康维护形同虚设。改为独立定时器：启动 2 分钟后首判，之后每 30 分钟到点判定。</summary>
        public static void StartAuto()
        {
            lock (autoSync)
            {
                if (autoTimer != null) return;
                autoTimer = new Timer(delegate { TickAuto(); }, null,
                    2 * 60 * 1000, 30 * 60 * 1000);
            }
        }

        public static void StopAuto()
        {
            Timer t;
            lock (autoSync) { t = autoTimer; autoTimer = null; }
            if (t != null) t.Dispose();
        }

        private static void TickAuto()
        {
            if (Interlocked.Exchange(ref runningFlag, 1) != 0) return;
            try
            {
                Func<bool> defer = ShouldDefer;
                if (defer != null && defer())
                {
                    // 只在确实到点时记一笔：顺延本身每 30 分钟才可能发生一次，不刷屏
                    if (IsDue(Settings.LoadStr("HealthLastRun", ""), IntervalDays(), DateTime.Now))
                        Logger.Log("健康维护：游戏进行中，本轮顺延到下个周期");
                    return;
                }
                RunIfDue();
            }
            catch (Exception ex) { Logger.LogFailure("健康维护调度异常", ex); }
            finally { Interlocked.Exchange(ref runningFlag, 0); }
        }

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

        /// <summary>到点则经维护框架执行一轮。由独立调度（StartAuto）调用。</summary>
        public static void RunIfDue()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!IsDue(Settings.LoadStr("HealthLastRun", ""), IntervalDays(), DateTime.Now)) return;

            // 到点判定后、执行前再让路一次：大缓存清理可持续数十秒，
            // 恰在此间隙启动的游戏不能撞上着色器全量重编译
            Func<bool> defer = ShouldDefer;
            if (defer != null && defer())
            {
                Logger.Log("健康维护：游戏进行中，本轮顺延到下个周期");
                return;   // 不写 HealthLastRun：下个 30 分钟周期继续尝试
            }

            try { HealthRunner.Run(HealthTrigger.Auto, CatalogOverride ?? HealthCatalog.Shared, null); }
            catch (Exception ex) { Logger.LogFailure("健康维护执行异常", ex); }

            Settings.SaveStr("HealthLastRun", today);
        }
    }
}
