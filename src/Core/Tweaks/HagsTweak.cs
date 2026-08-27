// @author zenjiro 18967498922@163.com
// 文件用途 开启 关闭并恢复硬件加速 GPU 调度

using System;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class HagsTweak
    {
        private const string GfxKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        private const string Val = "HwSchMode";

        private static readonly ReversibleReg Sch = new ReversibleReg(
            Registry.LocalMachine, GfxKey, Val, RegistryValueKind.DWord, "PrevHwSch");

        public static bool EnabledByCaelus { get { return Settings.Load("HagsOnByCaelus", false); } }

        public static bool CurrentlyOn()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(GfxKey))
                {
                    if (k == null) return false;
                    object v = k.GetValue(Val);
                    return v is int && (int)v == 2;
                }
            }
            catch { return false; }
        }

        public static bool Enable()
        {
            try
            {
                if (!Sch.Apply(2))
                {
                    Logger.Log("GPU 硬件调度（HAGS）写入或回读失败，未标记为已开启");
                    return false;
                }
                Settings.Save("HagsOnByCaelus", true);
                if (!Settings.Load("HagsOnByCaelus", false))
                {
                    Sch.Restore();
                    Logger.Log("HAGS 状态标志无法持久化，已还原注册表修改");
                    return false;
                }
                Logger.Log("GPU 硬件调度（HAGS）已开启，重启后生效");
                return true;
            }
            catch { return false; }
        }

        public static bool Disable()
        {
            try
            {
                // 有快照：还原到快照原值即终态——原值可能本身就是开启态（用户自己开了
                // HAGS），不得再按"当前仍是开启态"强制写 1 覆盖用户设置；无快照才写 1
                bool hadBackup = Sch.HasBackup;
                bool ok = hadBackup ? Sch.Restore() : Sch.Apply(1);
                if (ok)
                {
                    Settings.Save("HagsOnByCaelus", false);
                    if (Settings.Load("HagsOnByCaelus", true)) return false;
                    Logger.Log(hadBackup
                        ? "GPU 硬件调度（HAGS）已还原到系统原值（若原值为开启态则保持开启，Caelus 不再托管）"
                        : "GPU 硬件调度（HAGS）已关闭，重启后生效");
                }
                return ok;
            }
            catch { return false; }
        }

    }
}
