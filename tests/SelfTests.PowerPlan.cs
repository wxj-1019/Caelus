// @author zenjiro 18967498922@163.com
// 文件用途 竞技电源计划参数表与真机读写的自测

using System;
using System.Collections.Generic;

namespace CaelusApp
{
internal static partial class SelfTests
{
    /// <summary>自测进程用临时 Settings 存储（ArenaPlanGuid 读出为空），PurgeStaleClones
    /// 的「在册托管方案豁免」护栏因此失效——SelfTestPurge 会把真实生产计划
    /// 「Caelus 竞技」当孤儿副本清掉。从真实注册表读出在册 GUID 籽入临时存储，
    /// 恢复护栏。必须在每个 SelfTestPurge 之前调用。</summary>
    private static void ProtectRealArenaPlanInTest()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Caelus"))
            {
                string guid = k == null ? null : k.GetValue("ArenaPlanGuid") as string;
                if (!string.IsNullOrEmpty(guid)) Settings.SaveStr("ArenaPlanGuid", guid);
            }
        }
        catch { }
    }

    private static void TestPowerKnobTableHasNoDuplicates()
        {
            string[] pairs = PowerPlan.SelfTestKnobPairs();
            string[] labels = PowerPlan.SelfTestKnobLabels();
            Eq(pairs.Length, labels.Length);
            if (pairs.Length < 25) throw new Exception("参数表项数异常偏少: " + pairs.Length);

            var seenPair = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in pairs)
                if (!seenPair.Add(p)) throw new Exception("参数表存在重复的子组+设置 GUID: " + p);

            var seenLabel = new HashSet<string>();
            foreach (string l in labels)
                if (!seenLabel.Add(l)) throw new Exception("参数表存在重复的项名: " + l);
        }

        private static void TestPowerArenaDiffersFromCalm()
        {
            Eq(true, PowerPlan.SelfTestArenaDiffersFromCalm("最小处理器状态"));
            Eq(true, PowerPlan.SelfTestArenaDiffersFromCalm("核心停放最小核心数"));
            Eq(true, PowerPlan.SelfTestArenaDiffersFromCalm("允许节流状态"));
            Eq(true, PowerPlan.SelfTestArenaDiffersFromCalm("PCIe 链接电源管理"));
            Eq(false, PowerPlan.SelfTestArenaDiffersFromCalm("最大处理器状态"));
            Eq(false, PowerPlan.SelfTestArenaDiffersFromCalm("处理器最大频率"));
        }

        private static void TestPowerPlanWritesArenaValues()
        {
            Guid probe;
            if (!PowerPlan.SelfTestDuplicate(out probe)) Skip("本机无法复制电源计划");
            try
            {
                PowerPlan.SelfTestWriteName(probe, "Caelus 自测临时计划");

                if (!PowerPlan.SelfTestTune(probe, true, false))
                    throw new Exception("竞技档写入失败");

                ExpectKnob(probe, "最小处理器状态", 100);
                ExpectKnob(probe, "最大处理器状态", 100);
                ExpectKnob(probe, "核心停放最小核心数", 100);
                ExpectKnob(probe, "睿频模式", 2);
                ExpectKnob(probe, "允许节流状态", 0);
                ExpectKnob(probe, "PCIe 链接电源管理", 0);
                ExpectKnob(probe, "USB 选择性暂停", 0);
                ExpectKnob(probe, "关闭硬盘时间", 0);
                ExpectKnob(probe, "能源性能首选项", 0);

                if (!PowerPlan.SelfTestTune(probe, false, false))
                    throw new Exception("常规档写入失败");

                ExpectKnob(probe, "最小处理器状态", 20);
                ExpectKnob(probe, "核心停放最小核心数", 50);
                ExpectKnob(probe, "允许节流状态", 2);
                ExpectKnob(probe, "最大处理器状态", 100);
            }
            finally { PowerPlan.SelfTestDelete(probe); }
        }

        private static void TestPowerPlanNameRoundtrip()
        {
            Guid probe;
            if (!PowerPlan.SelfTestDuplicate(out probe)) Skip("本机无法复制电源计划");
            try
            {
                string title = "Caelus 自测命名 " + probe.ToString().Substring(0, 8);
                if (!PowerPlan.SelfTestWriteName(probe, title)) throw new Exception("写入方案名失败");
                Eq(title, PowerPlan.SelfTestName(probe));
            }
            finally { PowerPlan.SelfTestDelete(probe); }
        }

        private static void TestPowerPlanDeleteLeavesActiveIntact()
        {
            string before = PowerPlan.CurrentPlanLabel();
            Guid probe;
            if (!PowerPlan.SelfTestDuplicate(out probe)) Skip("本机无法复制电源计划");
            PowerPlan.SelfTestWriteName(probe, "Caelus 自测待删计划");
            if (!PowerPlan.SelfTestDelete(probe)) throw new Exception("删除临时计划失败");
            Eq("", PowerPlan.SelfTestName(probe));
            Eq(before, PowerPlan.CurrentPlanLabel());
        }

        private static void TestPowerPlanPurgesDuplicateClones()
        {
            Guid keep, dup1, dup2;
            if (!PowerPlan.SelfTestDuplicate(out keep)) Skip("本机无法复制电源计划");
            bool madeDup1 = PowerPlan.SelfTestDuplicate(out dup1);
            bool madeDup2 = PowerPlan.SelfTestDuplicate(out dup2);
            try
            {
                if (!madeDup1 || !madeDup2) Skip("本机无法复制出多份电源计划");
                // 名字+描述都匹配才算 Caelus 创建的副本（与 OwnsScheme 判定一致）
                PowerPlan.SelfTestWriteNameNote(keep, PowerPlan.PlanTitle, PowerPlan.SelfTestNote);
                PowerPlan.SelfTestWriteNameNote(dup1, PowerPlan.PlanTitle, PowerPlan.SelfTestNote);
                PowerPlan.SelfTestWriteNameNote(dup2, PowerPlan.PlanTitle, PowerPlan.SelfTestNote);

                ProtectRealArenaPlanInTest();
                PowerPlan.SelfTestPurge(keep);

                Eq(PowerPlan.PlanTitle, PowerPlan.SelfTestName(keep));
                Eq("", PowerPlan.SelfTestName(dup1));
                Eq("", PowerPlan.SelfTestName(dup2));
            }
            finally
            {
                PowerPlan.SelfTestDelete(keep);
                if (madeDup1) PowerPlan.SelfTestDelete(dup1);
                if (madeDup2) PowerPlan.SelfTestDelete(dup2);
            }
        }

        /// <summary>用户手工创建的同名方案（没有 Caelus 描述串）必须幸免：
        /// 既不被认领写入竞技参数，也不被重复副本清理误删。</summary>
        private static void TestPowerPlanPurgeSparesUserSameNameScheme()
        {
            Guid mine, userPlan;
            if (!PowerPlan.SelfTestDuplicate(out mine)) Skip("本机无法复制电源计划");
            bool madeUser = PowerPlan.SelfTestDuplicate(out userPlan);
            try
            {
                if (!madeUser) Skip("本机无法复制出多份电源计划");
                PowerPlan.SelfTestWriteNameNote(mine, PowerPlan.PlanTitle, PowerPlan.SelfTestNote);
                PowerPlan.SelfTestWriteName(userPlan, PowerPlan.PlanTitle);

                ProtectRealArenaPlanInTest();
                PowerPlan.SelfTestPurge(mine);

                Eq(PowerPlan.PlanTitle, PowerPlan.SelfTestName(userPlan));
            }
            finally
            {
                PowerPlan.SelfTestDelete(mine);
                if (madeUser) PowerPlan.SelfTestDelete(userPlan);
            }
        }

        private static void TestPowerPlanPurgeSparesForeignSchemes()
        {
            Guid mine, foreign;
            if (!PowerPlan.SelfTestDuplicate(out mine)) Skip("本机无法复制电源计划");
            bool madeForeign = PowerPlan.SelfTestDuplicate(out foreign);
            try
            {
                if (!madeForeign) Skip("本机无法复制出多份电源计划");
                PowerPlan.SelfTestWriteName(mine, PowerPlan.PlanTitle);
                PowerPlan.SelfTestWriteName(foreign, "用户自己的计划");

                ProtectRealArenaPlanInTest();
                PowerPlan.SelfTestPurge(mine);

                Eq("用户自己的计划", PowerPlan.SelfTestName(foreign));
            }
            finally
            {
                PowerPlan.SelfTestDelete(mine);
                if (madeForeign) PowerPlan.SelfTestDelete(foreign);
            }
        }

        private static void TestPowerPlanMigratesLegacyClone()
        {
            const string key = "SelfTestLegacyPlanGuid";
            Guid legacy;
            if (!PowerPlan.SelfTestDuplicate(out legacy)) Skip("本机无法复制电源计划");
            try
            {
                PowerPlan.SelfTestWriteName(legacy, "Caelus 自测旧副本");
                Settings.SaveStr(key, legacy.ToString());

                PowerPlan.SelfTestMigrate(key);

                string nameAfter = PowerPlan.SelfTestName(legacy);
                if (nameAfter.Length != 0)
                    throw new Exception("旧副本未被删除，方案名仍是 " + nameAfter);
                string keyAfter = Settings.LoadStr(key, "");
                if (keyAfter.Length != 0)
                    throw new Exception("旧键未清空，仍是 " + keyAfter);
            }
            finally
            {
                PowerPlan.SelfTestDelete(legacy);
                Settings.SaveStr(key, "");
            }
        }

        private static void TestPowerPlanResolveIsIdempotent()
        {
            PowerPlan.SelfTestResetResolve();
            // 真实机器上可能已存在生产「Caelus 竞技」方案：resolve 走认回而非新建，
            // 方案数量不变、结束时不删（认回的是生产方案，删了=毁掉真实配置）。
            // 无既有方案时才走新建路径，数量 +1 且收尾自删。
            bool hadOwned = PowerPlan.SelfTestHasOwnedScheme();
            Settings.SaveStr("ArenaPlanGuid", "");
            int before = PowerPlan.SelfTestSchemeCount();

            Guid first = PowerPlan.SelfTestResolve();
            try
            {
                if (!PowerPlan.SelfTestTargetOwned()) Skip("本机无法创建独立电源计划");

                Eq(PowerPlan.PlanTitle, PowerPlan.SelfTestName(first));
                int expectedAfterCreate = hadOwned ? before : before + 1;
                Eq(expectedAfterCreate, PowerPlan.SelfTestSchemeCount());
                Eq(first.ToString(), Settings.LoadStr("ArenaPlanGuid", ""));

                PowerPlan.SelfTestResetResolve();
                Guid second = PowerPlan.SelfTestResolve();
                Eq(first, second);
                Eq(expectedAfterCreate, PowerPlan.SelfTestSchemeCount());

                Settings.SaveStr("ArenaPlanGuid", "");
                PowerPlan.SelfTestResetResolve();
                Guid third = PowerPlan.SelfTestResolve();
                Eq(first, third);
                Eq(expectedAfterCreate, PowerPlan.SelfTestSchemeCount());
            }
            finally
            {
                if (!hadOwned) PowerPlan.SelfTestDelete(first);
                PowerPlan.SelfTestResetResolve();
                Settings.SaveStr("ArenaPlanGuid", "");
            }
        }

        private static void ExpectKnob(Guid scheme, string label, uint expected)
        {
            if (!PowerPlan.SelfTestSupports(scheme, label)) return;
            uint actual;
            if (!PowerPlan.SelfTestReadKnob(scheme, label, out actual))
                throw new Exception("读不回「" + label + "」");
            if (actual != expected)
                throw new Exception("「" + label + "」期望 " + expected + "，实际 " + actual);
        }

        private static void TestPowerOverlaySaverConstants()
        {
            Eq("961cc777-2547-4f9d-8174-7d86181b8a7a", PowerOverlay.SaverGuidText);
            Eq(true, PowerOverlay.IsSaverGuid("961CC777-2547-4F9D-8174-7D86181B8A7A"));  // 大小写不敏感
            Eq(false, PowerOverlay.IsSaverGuid("ded574b5-45a0-4f42-8737-46345c09c238")); // 最佳性能 ≠ 续航
            Eq(false, PowerOverlay.IsSaverGuid(null));
            Eq(false, PowerOverlay.IsSaverGuid("garbage"));
        }
    }
}
