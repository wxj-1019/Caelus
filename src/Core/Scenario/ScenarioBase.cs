// @author zenjiro 18967498922@163.com
// 文件用途 场景基类：仲裁器对接、活性重算、死 PID 清理（DevFocus/DailyCare 共用）

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal abstract class ScenarioBase : IScenario
    {
        protected readonly object sync = new object();
        protected readonly ScenarioArbiter arbiter;
        protected bool reported;

        protected ScenarioBase(ScenarioArbiter arbiter)
        {
            if (arbiter == null) throw new ArgumentNullException("arbiter");
            this.arbiter = arbiter;
            arbiter.Register(this);
        }

        public abstract ScenarioKind Kind { get; }
        public abstract int Priority { get; }
        public abstract void Grant();
        public abstract void Suspend();

        /// <summary>锁内判定：场景此刻是否想活跃（含自身开关检查）。子类实现。</summary>
        protected abstract bool WantsActiveLocked { get; }

        /// <summary>已向仲裁器报告的活性状态</summary>
        protected bool Reported { get { lock (sync) return reported; } }

        /// <summary>统一活性重算：任一活性来源变化后调用。锁内记账，锁外向仲裁器报告翻转。</summary>
        protected void RecomputeActivity()
        {
            bool becameActive = false;
            bool becameIdle = false;
            lock (sync)
            {
                bool nowActive = WantsActiveLocked;
                if (nowActive && !reported)
                {
                    reported = true;
                    becameActive = true;
                }
                else if (!nowActive && reported)
                {
                    reported = false;
                    becameIdle = true;
                }
            }
            if (becameActive) arbiter.ReportActivity(Kind, true);
            if (becameIdle) arbiter.ReportActivity(Kind, false);
        }

        /// <summary>强制撤销活性报告（开关关闭/退出路径用）：若已报告则向仲裁器报告不活跃</summary>
        protected void ForceReportInactive()
        {
            bool wasReported;
            lock (sync)
            {
                wasReported = reported;
                reported = false;
            }
            if (wasReported) arbiter.ReportActivity(Kind, false);
        }

        /// <summary>死 PID 兜底清理：短命进程的 Stopped 事件可能丢失，每次事件到达时清理</summary>
        protected static void PruneDeadPids(HashSet<int> pids)
        {
            if (pids.Count == 0) return;
            var dead = new List<int>();
            foreach (int pid in pids)
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) { dead.Add(pid); continue; }
                try
                {
                    if (!Native.StillActive(h)) dead.Add(pid);
                }
                finally { Native.CloseHandle(h); }
            }
            foreach (int pid in dead) pids.Remove(pid);
        }
    }
}
