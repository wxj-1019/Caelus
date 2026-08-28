// @author zenjiro 18967498922@163.com
// 文件用途 NVIDIA GPU 温度读取（NVML，nvidia-smi 同款接口）：枚举全部设备取最热，
//           双卡工作站不再只看 0 号卡；初始化失败进 10 分钟冷却后重试，不再终身判死
//           （驱动中途更新/恢复后自会接上），连续读失败同样触发重建。

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CaelusApp
{
    internal static class NvmlTemp
    {
        private const int NvmlTemperatureGpu = 0;
        private const uint MaxDevices = 8;
        private const long InitRetryCooldownTicks = 600L * TimeSpan.TicksPerSecond;
        private const int ReadFailuresBeforeReinit = 6;

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlInit();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetCount(ref uint count);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetHandleByIndex(uint index, ref IntPtr device);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, ref uint temp);

        private static readonly object lk = new object();
        private static int state; // 0=未初始化 1=可用 2=不可用
        private static IntPtr[] devices = new IntPtr[0];
        private static long initRetryAfterTicks;
        private static int readFails;

        public static bool TryRead(out int celsius)
        {
            celsius = 0;
            lock (lk)
            {
                long now = DateTime.UtcNow.Ticks;
                if (state == 0 || (state == 2 && now >= initRetryAfterTicks))
                    InitLocked(now);
                if (state != 1 || devices.Length == 0) return false;

                int best = 0;
                try
                {
                    foreach (IntPtr dev in devices)
                    {
                        uint temp = 0;
                        if (nvmlDeviceGetTemperature(dev, NvmlTemperatureGpu, ref temp) == 0
                            && temp > best) best = (int)temp;
                    }
                }
                catch { best = 0; }
                if (best <= 0)
                {
                    // 驱动更新等会让全部设备读失败：连续多次后降级重建，冷却期后再试
                    if (++readFails >= ReadFailuresBeforeReinit)
                    {
                        state = 2;
                        devices = new IntPtr[0];
                        initRetryAfterTicks = DateTime.UtcNow.Ticks + InitRetryCooldownTicks;
                        readFails = 0;
                    }
                    return false;
                }
                readFails = 0;
                celsius = best;
                return true;
            }
        }

        private static void InitLocked(long now)
        {
            devices = new IntPtr[0];
            state = 2;
            initRetryAfterTicks = now + InitRetryCooldownTicks;
            try
            {
                if (nvmlInit() != 0) return;
                uint count = 0;
                if (nvmlDeviceGetCount(ref count) != 0 || count == 0) return;
                if (count > MaxDevices) count = MaxDevices;
                var handles = new List<IntPtr>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    IntPtr dev = IntPtr.Zero;
                    if (nvmlDeviceGetHandleByIndex(i, ref dev) == 0 && dev != IntPtr.Zero)
                        handles.Add(dev);
                }
                if (handles.Count == 0) return;
                devices = handles.ToArray();
                state = 1;
            }
            catch { }
        }
    }
}
