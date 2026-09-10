// @author zenjiro 18967498922@163.com
// 文件用途 维护动作统一执行入口：定时调度与手动按钮同走此处，逐动作故障隔离

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class HealthRunner
    {
        /// <summary>跑一轮。Auto：仅 AllowAuto 动作执行，其余只扫描（实现 IHealthAutoCycle
        /// 的顺带跑自动周期职责）；Manual：全部执行，selected 传动作 Id→勾选明细。</summary>
        public static List<HealthResult> Run(HealthTrigger trigger, HealthActionCatalog catalog,
            IDictionary<string, string[]> selected)
        {
            var results = new List<HealthResult>();
            foreach (IHealthAction action in catalog.All)
            {
                HealthReport report = null;
                try { report = action.Analyze(); }
                catch (Exception ex)
                {
                    results.Add(Failure(action.Id, "扫描异常：" + ex.GetType().Name));
                    continue;
                }
                if (trigger == HealthTrigger.Auto && !action.AllowAuto)
                {
                    results.Add(new HealthResult
                    {
                        ActionId = action.Id, Outcome = HealthOutcome.Skipped,
                        ItemCount = report != null ? report.Findings.Count : 0,
                        Summary = "仅扫描，发现 " + (report != null ? report.Findings.Count : 0) + " 项"
                    });
                    var cycle = action as IHealthAutoCycle;
                    if (cycle != null)
                    {
                        try { cycle.OnAutoCycle(); }
                        catch (Exception ex) { Logger.LogFailure("维护自动周期：" + action.Id, ex); }
                    }
                    continue;
                }
                string[] ids = null;
                if (trigger == HealthTrigger.Manual && selected != null)
                    selected.TryGetValue(action.Id, out ids);
                HealthResult r;
                try { r = action.Execute(ids); }
                catch (Exception ex) { r = Failure(action.Id, "执行异常：" + ex.GetType().Name); }
                results.Add(r);
            }
            foreach (HealthResult r in results)
                HealthHistory.Append(ToRecord(trigger, r));
            return results;
        }

        /// <summary>手动禁用类入口：只执行指定动作（带勾选项），其他动作不受影响。</summary>
        public static HealthResult RunSelected(HealthActionCatalog catalog, string actionId, string[] selectedIds)
        {
            IHealthAction action = catalog.Find(actionId);
            HealthResult r;
            if (action == null) r = Failure(actionId, "动作未注册");
            else
            {
                try { r = action.Execute(selectedIds); }
                catch (Exception ex) { r = Failure(actionId, "执行异常：" + ex.GetType().Name); }
            }
            HealthHistory.Append(ToRecord(HealthTrigger.Manual, r));
            return r;
        }

        /// <summary>单条还原：payloadLine 为一行负载。成功追加一条 Undo 历史记录。</summary>
        public static bool UndoSingle(HealthActionCatalog catalog, string actionId, string payloadLine, out string error)
        {
            error = null;
            IHealthAction action = catalog.Find(actionId);
            if (action == null || !action.CanUndo) { error = "该动作不支持还原"; return false; }
            bool ok;
            try { ok = action.Undo(payloadLine, out error); }
            catch (Exception ex) { error = ex.GetType().Name; ok = false; }
            if (ok)
            {
                HealthHistory.Append(new HealthRecord
                {
                    Time = DateTime.Now, Trigger = "Undo", ActionId = actionId,
                    Outcome = HealthOutcome.Success, ItemCount = 1,
                    Summary = "已还原：" + HealthEsc.Unesc(payloadLine.Split('\t')[1])
                });
            }
            return ok;
        }

        private static HealthRecord ToRecord(HealthTrigger trigger, HealthResult r)
        {
            return new HealthRecord
            {
                Time = DateTime.Now,
                Trigger = trigger == HealthTrigger.Auto ? "Auto" : "Manual",
                ActionId = r.ActionId, Outcome = r.Outcome,
                FreedBytes = r.FreedBytes, ItemCount = r.ItemCount,
                Summary = r.Outcome == HealthOutcome.Failed ? (r.Error ?? "失败") : (r.Summary ?? ""),
                UndoPayload = r.UndoPayload ?? ""
            };
        }

        private static HealthResult Failure(string actionId, string error)
        {
            return new HealthResult { ActionId = actionId, Outcome = HealthOutcome.Failed, Error = error };
        }
    }
}
