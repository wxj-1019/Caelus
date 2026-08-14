// @author zenjiro 18967498922@163.com
// 文件用途 场景仲裁器：按严格优先级决定哪个场景持有系统副作用掌权资格

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class ScenarioArbiter
    {
        private readonly object sync = new object();
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

        /// <summary>纯逻辑：给定活跃集合，返回优先级最高的场景；无活跃返回 null。</summary>
        internal static ScenarioKind? Evaluate(
            HashSet<ScenarioKind> activeSet, Dictionary<ScenarioKind, IScenario> map)
        {
            ScenarioKind? winner = null;
            int best = int.MinValue;
            foreach (ScenarioKind kind in activeSet)
            {
                IScenario s;
                if (!map.TryGetValue(kind, out s)) continue;
                if (!winner.HasValue || s.Priority > best)
                {
                    winner = kind;
                    best = s.Priority;
                }
            }
            return winner;
        }

        /// <summary>场景报告自身活性。锁内只记账，Grant/Suspend 在锁外执行。</summary>
        public void ReportActivity(ScenarioKind kind, bool isActive)
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
                catch (Exception ex) { Logger.LogFailure("场景授权失败（" + toGrant.Kind + "）", ex); }
            }
            var handler = GrantedChanged;
            if (handler != null)
            {
                try { handler(nowGranted); } catch { }
            }
        }
    }
}
