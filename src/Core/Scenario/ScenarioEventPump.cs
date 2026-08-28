// @author zenjiro 18967498922@163.com
// 文件用途 场景进程事件泵：DevFocus/DailyCare 的批次处理从 ProcNotify 事件线程
//           移到本专职线程——场景的掌权交接内含 SCM 停服务、全量进程扫描、逐进程
//           提优等秒级操作，在事件线程同步执行会拖延其后的游戏启动/退出检测。
//           游戏模式/守护类轻量处理仍留在事件线程，互不影响。

using System;
using System.Collections.Generic;
using System.Threading;

namespace CaelusApp
{
    internal sealed class ScenarioEventPump
    {
        private readonly object sync = new object();
        private readonly Queue<ProcessChangeBatch> queue = new Queue<ProcessChangeBatch>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread thread;
        private bool stopping;

        /// <summary>泵线程上串行触发（宿主在此接线各场景的 NotifyProcessChanges）。</summary>
        public event Action<ProcessChangeBatch> Batch;

        public ScenarioEventPump()
        {
            thread = new Thread(Work);
            thread.IsBackground = true;
            thread.Name = "Caelus.ScenarioEvents";
            thread.Start();
        }

        public void Post(ProcessChangeBatch batch)
        {
            if (batch == null) return;
            lock (sync)
            {
                if (stopping) return;
                queue.Enqueue(batch);
            }
            wake.Set();
        }

        private void Work()
        {
            while (true)
            {
                ProcessChangeBatch batch = null;
                lock (sync)
                {
                    if (queue.Count > 0) batch = queue.Dequeue();
                    else if (stopping) return;
                }
                if (batch == null) { wake.WaitOne(); continue; }
                Action<ProcessChangeBatch> h = Batch;
                if (h != null)
                {
                    try { h(batch); }
                    catch (Exception ex) { Logger.LogFailure("场景事件泵处理失败", ex); }
                }
            }
        }

        /// <summary>退出路径：停收新批次并丢弃积压（退出时场景由 Stop 强制还原，
        /// 积压批次无需再消化——否则退出途中场景可能被积压事件重新激活）。
        /// 在途批次可能正执行 Grant/Suspend（SCM 操作秒级）：短暂等待让交接落定。</summary>
        public void Stop()
        {
            lock (sync) { stopping = true; queue.Clear(); }
            wake.Set();
            try
            {
                if (!thread.Join(5000))
                    Logger.Log("场景事件泵：退出等待超时（在途交接由启动自愈兜底）");
            }
            catch { }
        }
    }
}
