// @author zenjiro 18967498922@163.com
// 文件用途 共享系统效果的占用方登记：纯逻辑决策，供 SvcPause/Notif 这类单实例
//           系统开关做跨场景（游戏/开发专注）多占用方引用计数与交接直通

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class SharedEffectClaim
    {
        /// <summary>登记占用。返回 true 表示调用方需要执行底层施加（此前无人占用）；
        /// 已有占用（含自己重复登记）返回 false，底层效果保持不动。</summary>
        internal static bool Acquire(HashSet<string> owners, string owner)
        {
            if (owners.Contains(owner)) return false;
            bool first = owners.Count == 0;
            owners.Add(owner);
            return first;
        }

        /// <summary>释放占用。返回 true 表示这是最后一个占用方、调用方需要执行底层还原；
        /// 仍有其他占用方（或本就不是占用方）返回 false，底层效果保持不动。</summary>
        internal static bool Release(HashSet<string> owners, string owner)
        {
            if (!owners.Remove(owner)) return false;
            return owners.Count == 0;
        }

        /// <summary>占用移交（交接直通）：底层施加保持不动，只换记账的占用方。
        /// 移交方不是占用方或原地移交时为无操作，返回 false。</summary>
        internal static bool Handoff(HashSet<string> owners, string from, string to)
        {
            if (string.Equals(from, to, StringComparison.Ordinal)) return false;
            if (!owners.Remove(from)) return false;
            owners.Add(to);
            return true;
        }

        /// <summary>全量释放（退出/自愈路径语义）：清空全部占用方。
        /// 返回清空前是否有人占用。</summary>
        internal static bool ReleaseAll(HashSet<string> owners)
        {
            bool any = owners.Count > 0;
            owners.Clear();
            return any;
        }
    }
}
