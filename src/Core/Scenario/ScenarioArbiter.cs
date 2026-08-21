// @author zenjiro 18967498922@163.com
// 文件用途 场景仲裁器：按严格优先级决定哪个场景持有系统副作用掌权资格

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class ScenarioArbiter
    {
        private readonly object sync = new object();
        private readonly object dispatchLock = new object();
        private readonly Dictionary<ScenarioKind, IScenario> scenarios =
            new Dictionary<ScenarioKind, IScenario>();
        private readonly HashSet<ScenarioKind> active = new HashSet<ScenarioKind>();
        private ScenarioKind? granted;

        /// <summary>掌权者变化时触发，参数是新掌权者（无人掌权为 null）。在锁外触发。</summary>
        public event Action<ScenarioKind?> GrantedChanged;

        public ScenarioKind? CurrentGranted { get { lock (sync) return granted; } }

        public void Register(IScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException("scenario");
            lock (sync) scenarios[scenario.Kind] = scenario;
        }

        /// <summary>纯逻辑：给定活跃集合，返回优先级最高的场景；无活跃返回 null。平级时取枚举值较小者，保证结果确定。</summary>
        internal static ScenarioKind? Evaluate(
            HashSet<ScenarioKind> activeSet, Dictionary<ScenarioKind, IScenario> map)
        {
            ScenarioKind? winner = null;
            int best = int.MinValue;
            foreach (ScenarioKind kind in activeSet)
            {
                IScenario s;
                if (!map.TryGetValue(kind, out s)) continue;
                if (!winner.HasValue || s.Priority > best
                    || (s.Priority == best && kind < winner.Value))
                {
                    winner = kind;
                    best = s.Priority;
                }
            }
            return winner;
        }

        /// <summary>场景报告自身活性。锁内只记账，Grant/Suspend 在锁外执行。</summary>
        /// <remarks>报告尚未注册的场景种类会被记录但被 Evaluate 忽略，直到 Register；
        /// 注册后的场景在其下一次报告时才会成为候选。</remarks>
        public void ReportActivity(ScenarioKind kind, bool isActive)
        {
            // 串行化记账+派发整体：防并发报告交错导致系统状态与记账背离。
            // Monitor 同线程可重入，Grant/Suspend 回调同线程重入 ReportActivity 不会自死锁。
            lock (dispatchLock)
            {
                IScenario toSuspend = null;
                IScenario toGrant = null;
                ScenarioKind? nowGranted;
                lock (sync)
                {
                    if (isActive) active.Add(kind);
                    else active.Remove(kind);

                    nowGranted = Evaluate(active, scenarios);
                    if (nowGranted.Equals(granted)) return;

                    IScenario s;
                    if (granted.HasValue && scenarios.TryGetValue(granted.Value, out s))
                        toSuspend = s;
                    granted = nowGranted;
                    if (granted.HasValue && scenarios.TryGetValue(granted.Value, out s))
                        toGrant = s;
                }

                // 先还原旧掌权者，再授权新掌权者（顺序是要害：系统状态任一时刻只反映一个场景）
                if (toSuspend != null)
                {
                    try { toSuspend.Suspend(); }
                    catch (Exception ex) { Logger.LogFailure("场景挂起失败（" + toSuspend.Kind + "）", ex); }
                }
                if (toGrant != null)
                {
                    try { toGrant.Grant(); }
                    catch (Exception ex)
                    {
                        // 授权失败：回滚记账为无人掌权，避免 UI 显示掌权但副作用未施加。
                        Logger.LogFailure("场景授权失败（" + toGrant.Kind + "）", ex);
                        lock (sync) granted = null;
                        nowGranted = null;
                    }
                }
                var handler = GrantedChanged;
                if (handler != null)
                {
                    try { handler(nowGranted); }
                    catch (Exception ex) { Logger.LogFailure("场景掌权者变更通知失败", ex); }
                }
            }
        }
    }
}
