// @author zenjiro 18967498922@163.com
// 文件用途 维护动作·着色器缓存清理：包原有 >64MB 才清的阈值逻辑

namespace CaelusApp
{
    internal sealed class ShaderCacheAction : IHealthAction
    {
        internal const long Threshold = 64L * 1024 * 1024;

        /// <summary>测试挂钩：隔离真实文件系统（生产为 null 走 ShaderCache 真实实现）</summary>
        internal static System.Func<long> MeasureHook;
        internal static System.Func<CacheSweep.Result> CleanHook;

        public string Id { get { return "shader-cache"; } }
        public string TitleKey { get { return "health.shader.title"; } }
        public string DescKey { get { return "health.shader.desc"; } }
        public bool AllowAuto { get { return true; } }
        public bool CanUndo { get { return false; } }

        public HealthReport Analyze()
        {
            long bytes = Measure();
            var r = new HealthReport { ActionId = Id, Bytes = bytes };
            if (bytes > Threshold)
                r.Findings.Add(new HealthFinding { Id = "cache", Label = "着色器缓存", Detail = CacheSweep.FmtBytes(bytes) });
            return r;
        }

        public HealthResult Execute(string[] selectedIds)
        {
            long before = Measure();
            if (before <= Threshold)
                return new HealthResult { ActionId = Id, Outcome = HealthOutcome.Skipped, Summary = "着色器缓存不足 " + CacheSweep.FmtBytes(Threshold) + "，无需清理" };
            CacheSweep.Result r = Clean();
            return new HealthResult
            {
                ActionId = Id, Outcome = HealthOutcome.Success, FreedBytes = r.FreedBytes,
                Summary = "着色器缓存清理 " + CacheSweep.FmtBytes(before) + "（释放 " + CacheSweep.FmtBytes(r.FreedBytes) + "）"
            };
        }

        public bool Undo(string undoPayload, out string error) { error = "清理不可还原"; return false; }
        public System.Collections.Generic.List<HealthFinding> ListDisabled() { return new System.Collections.Generic.List<HealthFinding>(); }

        private static long Measure() { return MeasureHook != null ? MeasureHook() : ShaderCache.MeasureBytes(); }
        private static CacheSweep.Result Clean() { return CleanHook != null ? CleanHook() : ShaderCache.Clean(); }
    }
}
