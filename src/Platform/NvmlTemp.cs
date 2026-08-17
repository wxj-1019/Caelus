// @author zenjiro 18967498922@163.com
// 文件用途 NVIDIA GPU 温度读取（NVML，nvidia-smi 同款接口）：笔记本混合显卡与
//           驱动节能状态下比 NVAPI GetThermalSettings 稳定得多，作为主采样路径。

using System;
using System.Runtime.InteropServices;

namespace CaelusApp
{
    internal static class NvmlTemp
    {
        private const int NvmlTemperatureGpu = 0;

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
        private static IntPtr device;

        public static bool TryRead(out int celsius)
        {
            celsius = 0;
            lock (lk)
            {
                if (state == 0)
                {
                    state = 2;
                    try
                    {
                        if (nvmlInit() == 0)
                        {
                            uint count = 0;
                            if (nvmlDeviceGetCount(ref count) == 0 && count > 0)
                            {
                                IntPtr dev = IntPtr.Zero;
                                if (nvmlDeviceGetHandleByIndex(0, ref dev) == 0 && dev != IntPtr.Zero)
                                {
                                    device = dev;
                                    state = 1;
                                }
                            }
                        }
                    }
                    catch { state = 2; }
                }
                if (state != 1 || device == IntPtr.Zero) return false;
                try
                {
                    uint temp = 0;
                    if (nvmlDeviceGetTemperature(device, NvmlTemperatureGpu, ref temp) != 0) return false;
                    if (temp == 0) return false;
                    celsius = (int)temp;
                    return true;
                }
                catch { return false; }
            }
        }
    }
}
