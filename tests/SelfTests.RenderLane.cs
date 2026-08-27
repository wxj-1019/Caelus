// @author zenjiro 18967498922@163.com
// 文件用途 渲染主权域的自测：真实烧线程上的识别、抬高与按快照还原往返

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        /// <summary>身份校验必须用与调用方一致的 FILETIME 纪元（QueryProcessSample）。
        /// 此前 RestoreThread 拿它与 DateTime ticks 比较，永不相等，还原沦为
        /// "报告成功"的空操作，线程优先级一直留到进程退出。</summary>
        private static void TestRenderLaneThreadPriorityRoundtrip()
        {
            string dir = NewTempDir("renderlane");
            Process probe = null;
            try
            {
                string copy = Path.Combine(dir, "renderprobe.exe");
                File.Copy(Application.ExecutablePath, copy, true);
                var psi = new ProcessStartInfo(copy, "--cpu-burn");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                probe = Process.Start(psi);
                Thread.Sleep(600);   // 让烧线程稳定吃满该进程 CPU

                long creation, cpu;
                ulong io;
                IntPtr hq = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, probe.Id);
                Eq(true, hq != IntPtr.Zero);
                try { Eq(true, Native.QueryProcessSample(hq, out creation, out cpu, out io)); }
                finally { Native.CloseHandle(hq); }

                RenderLane.EnsureForGame(probe.Id, creation, "renderprobe");
                if (!RenderLane.IsActiveFor(probe.Id, creation))
                    Skip("采样窗内烧线程主导占比不足 35%（机器负载高），无法识别帧关键线程");
                Eq(true, HasThreadAtPriority(probe.Id, Native.THREAD_PRIORITY_ABOVE_NORMAL));

                Eq(true, RenderLane.Release());
                Eq(false, HasThreadAtPriority(probe.Id, Native.THREAD_PRIORITY_ABOVE_NORMAL));
            }
            finally
            {
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static bool HasThreadAtPriority(int pid, int priority)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    foreach (ProcessThread t in p.Threads)
                    {
                        IntPtr h = Native.OpenThread(Native.THREAD_QUERY_LIMITED_INFORMATION, false, t.Id);
                        if (h == IntPtr.Zero) continue;
                        try { if (Native.GetThreadPriority(h) == priority) return true; }
                        finally { Native.CloseHandle(h); }
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
