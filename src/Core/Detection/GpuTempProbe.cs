// @author zenjiro 18967498922@163.com
// 文件用途 GPU 温度采样（概览页关键指标）：NVIDIA 走 NVAPI thermal，AMD 走 ADLX 指标，
//           2 秒节流 + 24 点环形历史供迷你趋势线。全部失败时返回 null（界面显示 —）。

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class GpuTempProbe
    {
        private const int HistoryCapacity = 24;
        private const long MinIntervalTicks = 5 * TimeSpan.TicksPerSecond;
        private static readonly object lk = new object();
        private static readonly List<double> history = new List<double>();
        private static long nextTicks;
        private static double? lastTemp;
        private static int consecutiveFailures;
        private static IList<double> snapshot;
        private static int diagnosticState; // 0=未报告 1=成功已报告 2=失败已报告

        // 读取当前温度并推入历史；节流期内直接返回上次读数。
        // NVAPI 热读不宜过频：5 秒节流。驱动偶发拒绝读取时保持上次读数，
        // 连续失败 6 次（约 30 秒）才判定为无数据。
        public static double? Read()
        {
            long now = DateTime.UtcNow.Ticks;
            lock (lk)
            {
                if (now < nextTicks) return lastTemp;
                nextTicks = now + MinIntervalTicks;
            }
            double? value = ReadOnce();
            lock (lk)
            {
                if (!value.HasValue && lastTemp.HasValue && consecutiveFailures < 6)
                {
                    consecutiveFailures++;
                    return lastTemp;
                }
                consecutiveFailures = value.HasValue ? 0 : 6;
                lastTemp = value;
                if (value.HasValue)
                {
                    history.Add(value.Value);
                    if (history.Count > HistoryCapacity) history.RemoveAt(0);
                    snapshot = new List<double>(history);
                }
                // 采样源状态翻转时记日志（成功/失败各状态只记一次，状态切换再记）
                if (value.HasValue && diagnosticState != 1)
                {
                    diagnosticState = 1;
                    try { Logger.Log("GPU 温度采样可用：" + ((int)value.Value) + "°C"); } catch { }
                }
                else if (!value.HasValue && diagnosticState != 2)
                {
                    diagnosticState = 2;
                    string detail = NvApi.Available
                        ? (!NvApi.ThermalDiagResolved ? "NVAPI 接口未解析"
                            : NvApi.ThermalDiagLastRc == -1 ? "读数为零"
                            : "NVAPI rc=" + NvApi.ThermalDiagLastRc)
                        : "无可用驱动接口";
                    try { Logger.Log("GPU 温度采样不可用：" + detail); } catch { }
                }
            }
            return value;
        }

        // 历史快照（最多 24 点，按采样时间升序）；无读数时返回 null。
        public static IList<double> History
        {
            get { lock (lk) return snapshot; }
        }

        private static double? ReadOnce()
        {
            try
            {
                // NVIDIA：优先 NVML（nvidia-smi 同款，驱动节能/混合显卡下稳定），NVAPI 备用
                if (NvApi.Available)
                {
                    int c;
                    if (NvmlTemp.TryRead(out c)) return c;
                    IntPtr[] gpus = NvApi.EnumGpuHandles();
                    if (gpus != null)
                    {
                        int best = int.MinValue;
                        foreach (IntPtr h in gpus)
                        {
                            if (NvApi.TryGetGpuTemperature(h, out c) && c > best) best = c;
                        }
                        if (best > 0) return best;
                    }
                }
                if (AdlxApi.Available)
                {
                    IntPtr[] gpus = AdlxApi.GetGpus();
                    if (gpus != null && gpus.Length > 0)
                    {
                        double best = double.MinValue;
                        foreach (IntPtr h in gpus)
                        {
                            double usage, temp, power;
                            int clock, vram;
                            if (AdlxApi.TryReadMetrics(h, out usage, out clock, out temp, out power, out vram)
                                && temp > best) best = temp;
                        }
                        if (best > 0) return best;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
