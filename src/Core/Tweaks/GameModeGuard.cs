// @author zenjiro 18967498922@163.com
// 文件用途 确保 Windows 游戏模式未被关闭 大量旧优化教程教人关它而实测它只有收益

using System;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class GameModeGuard
    {
        private const string BarKey = @"Software\Microsoft\GameBar";
        private const string Val = "AutoGameModeEnabled";

        private static readonly ReversibleReg Auto = new ReversibleReg(
            Registry.CurrentUser, BarKey, Val, RegistryValueKind.DWord, "PrevAutoGameMode");

        public static bool EnabledByCaelus { get { return Settings.Load("GameModeGuardByCaelus", false); } }

        public static bool CurrentlyOn()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(BarKey))
                {
                    if (k == null) return true;
                    object v = k.GetValue(Val);
                    return !(v is int) || (int)v != 0;
                }
            }
            catch { return true; }
        }

        public static bool Enable()
        {
            try
            {
                if (!Auto.Apply(1))
                {
                    Logger.Log("游戏模式守护写入或回读失败，未标记为已开启");
                    return false;
                }
                Settings.Save("GameModeGuardByCaelus", true);
                if (!Settings.Load("GameModeGuardByCaelus", false))
                {
                    Auto.Restore();
                    Logger.Log("游戏模式守护状态标志无法持久化，已还原注册表修改");
                    return false;
                }
                Logger.Log("游戏模式守护已开启（AutoGameModeEnabled → 1）");
                return true;
            }
            catch { return false; }
        }

        public static bool Restore()
        {
            try
            {
                bool ok = !Auto.HasBackup || Auto.Restore();
                if (ok)
                {
                    Settings.Save("GameModeGuardByCaelus", false);
                    if (Settings.Load("GameModeGuardByCaelus", true)) return false;
                    Logger.Log("游戏模式守护已关闭并还原原值");
                }
                return ok;
            }
            catch { return false; }
        }
    }
}
