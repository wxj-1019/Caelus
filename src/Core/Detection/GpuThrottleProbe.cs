// @author zenjiro 18967498922@163.com
// 文件用途 按会话累计 GPU 降频原因采样 归因功耗墙温度墙电池限制
//           NVIDIA 走驱动降频掩码；AMD 无掩码接口，用 ADLX 指标做
//           「高温 + 频率较本局峰值回落」的保守启发式（结果标注疑似）

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class GpuThrottleProbe
    {
        private const int MinIntervalSeconds = 15;
        private const int MinSamplesForVerdict = 4;
        private const double AmdThermalTempC = 90;
        private const double AmdClockDropRatio = 0.75;
        private const int AmdBaselineClockFloorMhz = 500;
        private static readonly object lk = new object();
        private static IntPtr[] gpus;
        private static bool gpusResolved;
        private static long nextSampleTicks;
        private static int samples, thermalHits, powerHits, batteryHits;
        // AMD 启发式：本局见过的最高 3D 频率作基线（低温期采集），高温且频率回落即计一次
        private static int amdSamples, amdThermalHits;
        private static int amdBaselineClockMhz;

        public static void Reset()
        {
            lock (lk)
            {
                samples = 0; thermalHits = 0; powerHits = 0; batteryHits = 0;
                amdSamples = 0; amdThermalHits = 0; amdBaselineClockMhz = 0;
                nextSampleTicks = 0;
            }
        }

        public static void SampleIfDue()
        {
            long now = DateTime.UtcNow.Ticks;
            lock (lk)
            {
                if (now < nextSampleTicks) return;
                nextSampleTicks = now + MinIntervalSeconds * TimeSpan.TicksPerSecond;
            }
            if (NvApi.Available)
            {
                uint mask;
                if (!TryReadMask(out mask)) return;
                lock (lk)
                {
                    samples++;
                    if ((mask & NvApi.PerfDecreaseThermal) != 0) thermalHits++;
                    if ((mask & (NvApi.PerfDecreasePower | NvApi.PerfDecreaseInsufficientPower)) != 0) powerHits++;
                    if ((mask & NvApi.PerfDecreaseAcBatt) != 0) batteryHits++;
                }
                return;
            }
            SampleAmdIfAvailable();
        }

        /// <summary>AMD 降频启发式采样：ADLX 无降频原因掩码，只能用「温度近墙 +
        /// 频率较本局峰值明显回落」推断温度墙，不虚构功耗墙/电池结论。</summary>
        private static void SampleAmdIfAvailable()
        {
            if (!AdlxApi.Available) return;
            IntPtr[] handles = AdlxApi.GetGpus();
            if (handles == null || handles.Length == 0) return;
            try
            {
                double hottest = 0;
                int bestClock = 0;
                foreach (IntPtr h in handles)
                {
                    double usage, temp, power;
                    int clock, vram;
                    if (!AdlxApi.TryReadMetrics(h, out usage, out clock, out temp, out power, out vram)) continue;
                    if (temp > hottest) hottest = temp;
                    if (clock > bestClock) bestClock = clock;
                }
                if (hottest <= 0) return;
                lock (lk)
                {
                    amdSamples++;
                    if (hottest < AmdThermalTempC - 5 && bestClock > amdBaselineClockMhz)
                        amdBaselineClockMhz = bestClock;   // 低温期刷新频率基线
                    if (hottest >= AmdThermalTempC && amdBaselineClockMhz >= AmdBaselineClockFloorMhz
                        && bestClock > 0 && bestClock <= amdBaselineClockMhz * AmdClockDropRatio)
                        amdThermalHits++;
                }
            }
            finally { AdlxApi.ReleaseAll(handles); }
        }

        private static bool AmdThrottledNow(out string text)
        {
            text = null;
            if (!AdlxApi.Available) return false;
            IntPtr[] handles = AdlxApi.GetGpus();
            if (handles == null || handles.Length == 0) return false;
            try
            {
                double hottest = 0;
                int bestClock = 0;
                foreach (IntPtr h in handles)
                {
                    double usage, temp, power;
                    int clock, vram;
                    if (!AdlxApi.TryReadMetrics(h, out usage, out clock, out temp, out power, out vram)) continue;
                    if (temp > hottest) hottest = temp;
                    if (clock > bestClock) bestClock = clock;
                }
                int baseline;
                lock (lk) baseline = amdBaselineClockMhz;
                if (hottest >= AmdThermalTempC && baseline >= AmdBaselineClockFloorMhz
                    && bestClock > 0 && bestClock <= baseline * AmdClockDropRatio)
                {
                    text = "温度墙（疑似）";
                    return true;
                }
                return false;
            }
            finally { AdlxApi.ReleaseAll(handles); }
        }

        public static bool TryReadMask(out uint combined)
        {
            combined = 0;
            if (!NvApi.Available) return false;
            IntPtr[] handles;
            lock (lk)
            {
                if (!gpusResolved) { gpus = NvApi.EnumGpuHandles(); gpusResolved = true; }
                handles = gpus;
            }
            if (handles == null) return false;
            bool any = false;
            foreach (IntPtr h in handles)
            {
                uint mask;
                if (NvApi.TryGetPerfDecrease(h, out mask)) { combined |= mask; any = true; }
            }
            return any;
        }

        public static string Summarize()
        {
            int n, t, p, b, an, at;
            lock (lk)
            {
                n = samples; t = thermalHits; p = powerHits; b = batteryHits;
                an = amdSamples; at = amdThermalHits;
            }
            var parts = new List<string>();
            if (n >= MinSamplesForVerdict)
            {
                if (p > 0) parts.Add("功耗墙 " + Percent(p, n));
                if (t > 0) parts.Add("温度墙 " + Percent(t, n));
                if (b > 0) parts.Add("电池限制 " + Percent(b, n));
            }
            if (an >= MinSamplesForVerdict && at > 0)
                parts.Add("温度墙（疑似）" + Percent(at, an));
            if (parts.Count == 0) return null;
            int total = n + an;
            return string.Join("、", parts.ToArray()) + "（这局共查 " + total + " 次）";
        }

        private static readonly object instantLk = new object();
        private static string instantCache;   // 空串表示"无文案"（null）
        private static long instantCacheUntilTicks;

        /// <summary>带 3 秒结果缓存：概览页按 2 秒节拍轮询，NV 路径每次读掩码尚廉价，
        /// AMD 路径每次全量 COM 枚举+指标读——缓存把成本压平（null=无信号）。</summary>
        public static string InstantText()
        {
            long now = DateTime.UtcNow.Ticks;
            lock (instantLk)
            {
                if (instantCache != null && now < instantCacheUntilTicks)
                    return instantCache.Length == 0 ? null : instantCache;
            }
            string value = InstantTextCore();
            lock (instantLk)
            {
                instantCache = value ?? "";
                instantCacheUntilTicks = now + 3L * TimeSpan.TicksPerSecond;
            }
            return value;
        }

        private static string InstantTextCore()
        {
            uint mask;
            if (NvApi.Available && TryReadMask(out mask))
            {
                if (mask == 0) return "无限制";
                var parts = new List<string>();
                if ((mask & NvApi.PerfDecreaseThermal) != 0) parts.Add("温度墙");
                if ((mask & (NvApi.PerfDecreasePower | NvApi.PerfDecreaseInsufficientPower)) != 0) parts.Add("功耗墙");
                if ((mask & NvApi.PerfDecreaseAcBatt) != 0) parts.Add("电池限制");
                if ((mask & NvApi.PerfDecreaseApi) != 0) parts.Add("软件限制");
                if (parts.Count == 0) return "其他限制 (0x" + mask.ToString("X") + ")";
                return string.Join("、", parts.ToArray());
            }
            // AMD/核显：无掩码接口，只有启发式命中才出文案，不虚构"无限制"
            string amd;
            if (AmdThrottledNow(out amd)) return amd;
            return null;
        }

        internal static string Percent(int hits, int total)
        {
            if (total <= 0) return "0%";
            return (hits * 100 / total) + "%";
        }
    }
}
