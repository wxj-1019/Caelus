// @author zenjiro 18967498922@163.com
// 文件用途 共享效果占用方登记（SharedEffectClaim）的自测：引用计数、移交、全量释放

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestSharedClaimOwnerSemantics()
        {
            var owners = new HashSet<string>(StringComparer.Ordinal);

            // 首个占用方：调用方需要执行底层施加
            Eq(true, SharedEffectClaim.Acquire(owners, "a"));
            Eq(true, owners.Contains("a"));
            // 同一占用方重复登记：幂等，不需要再次施加
            Eq(false, SharedEffectClaim.Acquire(owners, "a"));
            Eq(1, owners.Count);
            // 他人占用期间新占用方登记：底层保持，不重复施加
            Eq(false, SharedEffectClaim.Acquire(owners, "b"));
            Eq(2, owners.Count);

            // 先走的占用方释放：仍有人占用，不执行底层还原
            Eq(false, SharedEffectClaim.Release(owners, "a"));
            Eq(true, owners.Contains("b"));
            // 最后一个占用方释放：需要执行底层还原
            Eq(true, SharedEffectClaim.Release(owners, "b"));
            Eq(0, owners.Count);
            // 非占用方释放：无操作（也不触发还原）
            Eq(false, SharedEffectClaim.Release(owners, "ghost"));
            Eq(0, owners.Count);
        }

        private static void TestSharedClaimHandoffAndForceRelease()
        {
            var owners = new HashSet<string>(StringComparer.Ordinal);
            SharedEffectClaim.Acquire(owners, "devfocus");
            SharedEffectClaim.Acquire(owners, "other");

            // 占用移交：系统效果保持不动，只是换人记账
            Eq(true, SharedEffectClaim.Handoff(owners, "devfocus", "game"));
            Eq(false, owners.Contains("devfocus"));
            Eq(true, owners.Contains("game"));
            Eq(2, owners.Count);

            // 非占用方移交与自我移交都是无操作
            Eq(false, SharedEffectClaim.Handoff(owners, "ghost", "game"));
            Eq(false, SharedEffectClaim.Handoff(owners, "game", "game"));
            Eq(2, owners.Count);

            // 全量释放（退出路径语义）：清空全部占用方并报告曾有人占用
            Eq(true, SharedEffectClaim.ReleaseAll(owners));
            Eq(0, owners.Count);
            Eq(false, SharedEffectClaim.ReleaseAll(owners));
        }
    }
}
