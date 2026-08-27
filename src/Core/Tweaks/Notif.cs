// @author zenjiro 18967498922@163.com
// 文件用途 关闭并恢复系统通知弹窗

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class Notif
    {
        internal const string OwnerGame = "game";
        internal const string OwnerDevFocus = "devfocus";

        private static readonly ReversibleReg Toast = new ReversibleReg(
            Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications",
            "ToastEnabled", RegistryValueKind.DWord, "PrevToast");
        private static readonly object lk = new object();
        // 多占用方登记：游戏免打扰与开发专注免打扰叠加时，最后一个占用方离开才还原。
        private static readonly HashSet<string> owners = new HashSet<string>(StringComparer.Ordinal);

        public static bool Quiet(string owner)
        {
            lock (lk)
            {
                if (!SharedEffectClaim.Acquire(owners, owner)) return true;
                bool applied = Toast.Apply(0);
                if (!applied) SharedEffectClaim.Release(owners, owner);
                Logger.Log(applied ? "游戏免打扰：已禁用通知弹窗" : "游戏免打扰写入或回读失败，本轮未启用");
                return applied;
            }
        }

        /// <summary>全量还原：清空全部占用方并恢复系统通知。退出链与崩溃自愈使用。</summary>
        public static bool Restore()
        {
            lock (lk)
            {
                SharedEffectClaim.ReleaseAll(owners);
                if (Toast.HasBackup && Toast.Restore()) Logger.Log("通知弹窗已还原");
                return !Toast.HasBackup;
            }
        }

        /// <summary>释放指定占用方；仅当这是最后一个占用方时才真正恢复系统通知。</summary>
        public static bool Restore(string owner)
        {
            lock (lk)
            {
                if (!SharedEffectClaim.Release(owners, owner)) return true;
                if (Toast.HasBackup && Toast.Restore()) Logger.Log("通知弹窗已还原");
                return !Toast.HasBackup;
            }
        }

        /// <summary>交接直通：把通知静默的占用权从一个占用方移交给另一个，底层保持静默不动。</summary>
        public static bool HandoffOwner(string from, string to)
        {
            lock (lk) return SharedEffectClaim.Handoff(owners, from, to);
        }

        internal static bool HeldBy(string owner)
        {
            lock (lk) return owners.Contains(owner);
        }

        public static void HealFromCrash() { if (Toast.HasBackup) Restore(); }
    }
}
