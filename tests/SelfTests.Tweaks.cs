// @author zenjiro 18967498922@163.com
// 文件用途 驱动调优键位映射与网卡清单编解码的自测

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestDrsKeyIdMapping()
        {
            Eq(NvApi.SettingPreferredPState, NvDrsTweaks.SettingIdOf(NvDrsTweaks.KeyPState));
            Eq(NvApi.SettingFrlFps, NvDrsTweaks.SettingIdOf(NvDrsTweaks.KeyFrl));
            Eq(0x007BA09Eu, NvDrsTweaks.SettingIdOf(NvDrsTweaks.KeyPreRender));
            Eq(0x0005F543u, NvDrsTweaks.SettingIdOf(NvDrsTweaks.KeyLowLatCpl));
            var ids = new HashSet<uint>
            {
                NvDrsTweaks.SettingIdOf(NvDrsTweaks.KeyPState),
                NvDrsTweaks.SettingIdOf(NvDrsTweaks.KeyFrl),
                NvDrsTweaks.SettingIdOf(NvDrsTweaks.KeyPreRender),
                NvDrsTweaks.SettingIdOf(NvDrsTweaks.KeyLowLatCpl)
            };
            Eq(4, ids.Count);
        }

        private static void TestDrsSnapshotRoundtrip()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { NvDrsTweaks.KeyPState, "1" },
                { NvDrsTweaks.KeyFrl, "absent" },
                { NvDrsTweaks.KeyPreRender, "2" },
                { NvDrsTweaks.KeyLowLatCpl, "absent" }
            };
            var back = NvDrsTweaks.ParseSnapshot(NvDrsTweaks.SerializeSnapshot(map));
            Eq(4, back.Count);
            foreach (var kv in map) Eq(kv.Value, back[kv.Key]);
        }

        private static void TestNagleListCodec()
        {
            Eq(0, NagleTweak.ParseList("").Length);
            Eq(0, NagleTweak.ParseList(null).Length);
            string[] two = NagleTweak.ParseList("{aaa};{bbb}");
            Eq(2, two.Length);
            Eq("{aaa}", two[0]);
            Eq("{bbb}", two[1]);
        }

        private static void TestRenderLaneIdentifiesBusyThread()
        {
            using (var stop = new System.Threading.ManualResetEvent(false))
            {
                var busy = new System.Threading.Thread(delegate ()
                {
                    while (!stop.WaitOne(0)) { }
                });
                busy.IsBackground = true;
                busy.Start();
                try
                {
                    RenderLane.Candidate best;
                    int self = System.Diagnostics.Process.GetCurrentProcess().Id;
                    if (!RenderLane.TryIdentify(self, out best))
                        throw new Exception("thread identification failed on the test process itself");
                    if (best.Share < RenderLane.MinDominantShare)
                        throw new Exception("the spinning thread did not dominate: share=" + best.Share.ToString("F2"));
                }
                finally { stop.Set(); busy.Join(2000); }
            }
        }

        private static void TestGameDescendantsExemption()
        {
            var parents = new Dictionary<int, int>
            {
                { 200, 100 }, { 300, 200 }, { 500, 400 }, { 600, 100 }, { 700, 600 }
            };
            var roots = new HashSet<int> { 100 };
            HashSet<int> got = GameMode.WalkDescendants(parents, roots, 600, 24);

            Eq(true, got.Contains(200));
            Eq(true, got.Contains(300));
            Eq(false, got.Contains(100));
            Eq(false, got.Contains(500));
            Eq(false, got.Contains(600));
            Eq(false, got.Contains(700));
            Eq(2, got.Count);

            var cycle = new Dictionary<int, int> { { 1000, 1100 }, { 1100, 1000 } };
            GameMode.WalkDescendants(cycle, new HashSet<int> { 9900 }, 0, 24);
            Eq(0, GameMode.WalkDescendants(null, roots, 0, 24).Count);
            Eq(0, GameMode.WalkDescendants(parents, new HashSet<int>(), 0, 24).Count);
        }

        private static void TestBoostClearsEfficiencyMode()
        {
            if (!Native.PowerThrottlingSupported) Skip("power throttling unavailable");
            using (Process probe = Process.Start(new ProcessStartInfo("cmd.exe", "/c pause")
            { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true }))
            {
                Thread.Sleep(250);
                IntPtr h = Native.OpenProcess(
                    Native.PROCESS_SET_INFORMATION | Native.PROCESS_SET_LIMITED_INFORMATION
                    | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, probe.Id);
                if (h == IntPtr.Zero) Skip("cannot open the probe process");
                try
                {
                    Native.ApplyEcoQoS(h);
                    Eq(true, WaitQoSState(h, false));

                    Native.ApplyHighQoS(h, Native.OsBuild() >= 22000);
                    Eq(true, WaitQoSState(h, true));
                }
                finally
                {
                    Native.CloseHandle(h);
                    try { probe.StandardInput.Close(); if (!probe.WaitForExit(3000)) probe.Kill(); } catch { }
                }
            }
        }

        private static bool WaitQoSState(IntPtr h, bool expectCleared)
        {
            bool ok = GameMode.HighQoSVerified(h) == expectCleared;
            for (int i = 0; !ok && i < 40; i++)
            {
                Thread.Sleep(25);
                ok = GameMode.HighQoSVerified(h) == expectCleared;
            }
            return ok;
        }

        private static void TestNetThrottleRangeJudgement()
        {
            Eq(10, NetTweak.SystemDefault);
            int? cur = NetTweak.Current();
            bool needs = NetTweak.NeedsRepair();
            if (!cur.HasValue) Eq(false, needs);
            else Eq(cur.Value < 1 || cur.Value > 70, needs);
            if (string.IsNullOrEmpty(NetTweak.Describe()))
                throw new Exception("net throttle description was empty");
        }

        private static void TestDevicePowerBitMerge()
        {
            Eq(0x08, DevicePowerTweak.Merge(null, true));
            Eq(0x18, DevicePowerTweak.Merge(0x10, true));
            Eq(0x18, DevicePowerTweak.Merge(0x18, true));
            Eq(0x10, DevicePowerTweak.Merge(0x18, false));
            Eq(0, DevicePowerTweak.Merge(0x08, false));
        }

        private static void TestMsiScanClassFilter()
        {
            foreach (MsiModeTweak.Candidate c in MsiModeTweak.Scan())
            {
                if (string.IsNullOrEmpty(c.InstanceId))
                    throw new Exception("MSI candidate had an empty instance id");
                if (!c.InstanceId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("MSI candidate was not a PCI device: " + c.InstanceId);
            }
            foreach (MsiModeTweak.Candidate c in MsiModeTweak.Disabled())
            {
                Eq(true, c.HasKey);
                Eq(0, c.Value.Value);
            }
            Eq(0, MsiModeTweak.ParseList("").Length);
            Eq(2, MsiModeTweak.ParseList("a;b").Length);
        }

        private static void TestRenderLaneJournalCodec()
        {
            int pid, tid, pri; long creation, threadCreation;
            Eq(false, RenderLane.ParseJournal("", out pid, out creation, out tid, out pri, out threadCreation));
            Eq(false, RenderLane.ParseJournal("1|2|3", out pid, out creation, out tid, out pri, out threadCreation));
            Eq(false, RenderLane.ParseJournal("0|2|3|4", out pid, out creation, out tid, out pri, out threadCreation));
            Eq(false, RenderLane.ParseJournal("1|2|3|4|5|6", out pid, out creation, out tid, out pri, out threadCreation));
            // 旧 4 段格式兼容（无线程身份校验），线程创建时间为 0
            Eq(true, RenderLane.ParseJournal("1234|99887766|4321|1", out pid, out creation, out tid, out pri, out threadCreation));
            Eq(1234, pid);
            Eq(99887766L, creation);
            Eq(4321, tid);
            Eq(1, pri);
            Eq(0L, threadCreation);
            // 新 5 段格式带线程创建时间
            Eq(true, RenderLane.ParseJournal("1234|99887766|4321|1|556677", out pid, out creation, out tid, out pri, out threadCreation));
            Eq(556677L, threadCreation);
        }

        private static void TestIfeoSandboxRoundtrip()
        {
            string sandbox = @"Software\CaelusTest\IFEO_" + System.Diagnostics.Process.GetCurrentProcess().Id;
            IfeoBoost.Hive = Microsoft.Win32.Registry.CurrentUser;
            IfeoBoost.RootOverride = sandbox;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(sandbox)) { }
                IfeoBoost.EnsureForGame("probe");
                using (var p = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\probe.exe\PerfOptions"))
                {
                    if (p == null) throw new Exception("IFEO PerfOptions key was not created");
                    Eq(3, (int)p.GetValue("CpuPriorityClass", -1));
                }
                IfeoBoost.EnsureForGame("probe.exe");
                Eq(1, IfeoBoost.ParseList(Settings.LoadStr("IfeoList", "")).Length);
                Eq(true, IfeoBoost.RestoreAll());
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\probe.exe"))
                    if (k != null) throw new Exception("IFEO exe key left behind after restore");
                Eq(0, IfeoBoost.ParseList(Settings.LoadStr("IfeoList", "")).Length);
            }
            finally
            {
                IfeoBoost.Hive = Microsoft.Win32.Registry.LocalMachine;
                IfeoBoost.RootOverride = null;
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(sandbox, false); } catch { }
            }
        }

        private static void TestIfeoWritesFullPerfOptionsTriple()
        {
            string sandbox = @"Software\CaelusTest\IFEO3_" + System.Diagnostics.Process.GetCurrentProcess().Id;
            IfeoBoost.Hive = Microsoft.Win32.Registry.CurrentUser;
            IfeoBoost.RootOverride = sandbox;
            IfeoBoost.ClearArmed();
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(sandbox)) { }
                IfeoBoost.EnsureForGame("triple");
                using (var p = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\triple.exe\PerfOptions"))
                {
                    if (p == null) throw new Exception("PerfOptions key was not created");
                    Eq(3, (int)p.GetValue("CpuPriorityClass", -1));
                    Eq(3, (int)p.GetValue("IoPriority", -1));
                    Eq(5, (int)p.GetValue("PagePriority", -1));
                }
                Eq(true, IfeoBoost.RestoreAll());
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\triple.exe"))
                    if (k != null) throw new Exception("all three values must be removed, key left behind");
            }
            finally
            {
                IfeoBoost.Hive = Microsoft.Win32.Registry.LocalMachine;
                IfeoBoost.RootOverride = null;
                IfeoBoost.ClearArmed();
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(sandbox, false); } catch { }
            }
        }

        private static void TestIfeoSystemReservedGuard()
        {
            // 纯逻辑判定：系统保留名裸名与 .exe 形态都拒绝，普通游戏名不误伤
            Eq(true, IfeoBoost.IsSystemReserved("svchost"));
            Eq(true, IfeoBoost.IsSystemReserved("svchost.exe"));
            Eq(true, IfeoBoost.IsSystemReserved("lsass.exe"));
            Eq(true, IfeoBoost.IsSystemReserved("explorer"));
            Eq(true, IfeoBoost.IsSystemReserved("System"));
            Eq(false, IfeoBoost.IsSystemReserved("cyberpunk2077"));
            Eq(false, IfeoBoost.IsSystemReserved("NebulaStrike-Win64-Shipping"));
            Eq(false, IfeoBoost.IsSystemReserved(null));
            Eq(false, IfeoBoost.IsSystemReserved(""));

            string sandbox = @"Software\CaelusTest\IFEOGuard_" + System.Diagnostics.Process.GetCurrentProcess().Id;
            IfeoBoost.Hive = Microsoft.Win32.Registry.CurrentUser;
            IfeoBoost.RootOverride = sandbox;
            IfeoBoost.ClearArmed();
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(sandbox)) { }

                // Arm：系统保留名被拒绝，预置列表不被污染
                Eq(false, IfeoBoost.Arm("svchost.exe"));
                Eq(false, IfeoBoost.Arm("winlogon"));
                Eq(0, IfeoBoost.Armed().Length);

                // EnsureForGame：不写任何 IFEO 键
                IfeoBoost.EnsureForGame("lsass.exe");
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\lsass.exe"))
                    if (k != null) throw new Exception("system-reserved name must not create IFEO key");

                // PreArmAll 兜底：向预置列表直接注入保留名也必须被跳过（ApplyFor 防护）
                Settings.SaveStr("IfeoArm", "normal.exe;csrss.exe");
                Eq(1, IfeoBoost.PreArmAll());
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\csrss.exe"))
                    if (k != null) throw new Exception("pre-arm must not apply system-reserved name");
                using (var p = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\normal.exe\PerfOptions"))
                {
                    if (p == null) throw new Exception("normal pre-arm should still apply");
                    Eq(3, (int)p.GetValue("CpuPriorityClass", -1));
                }
                Eq(true, IfeoBoost.RestoreAll());
                Eq(2, IfeoBoost.ClearArmed());
                Eq(0, IfeoBoost.Armed().Length);
            }
            finally
            {
                IfeoBoost.Hive = Microsoft.Win32.Registry.LocalMachine;
                IfeoBoost.RootOverride = null;
                IfeoBoost.ClearArmed();
                try { Settings.SaveStr("IfeoArm", ""); } catch { }
                try { Settings.SaveStr("IfeoList", ""); } catch { }
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(sandbox, false); } catch { }
            }
        }

        private static void TestIfeoPreArmAppliesBeforeGameStarts()
        {
            string sandbox = @"Software\CaelusTest\IFEOArm_" + System.Diagnostics.Process.GetCurrentProcess().Id;
            IfeoBoost.Hive = Microsoft.Win32.Registry.CurrentUser;
            IfeoBoost.RootOverride = sandbox;
            IfeoBoost.ClearArmed();
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(sandbox)) { }
                Eq(true, IfeoBoost.Arm("armed"));
                Eq(false, IfeoBoost.Arm("armed.exe"));
                Eq(1, IfeoBoost.Armed().Length);

                using (var p = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\armed.exe"))
                    if (p != null) throw new Exception("pre-arm must start from a clean key");

                Eq(1, IfeoBoost.PreArmAll());
                using (var p = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sandbox + @"\armed.exe\PerfOptions"))
                {
                    if (p == null) throw new Exception("pre-arm did not create PerfOptions");
                    Eq(3, (int)p.GetValue("CpuPriorityClass", -1));
                }
                Eq(true, IfeoBoost.RestoreAll());
                Eq(1, IfeoBoost.ClearArmed());
                Eq(0, IfeoBoost.Armed().Length);
                Eq(0, IfeoBoost.PreArmAll());
            }
            finally
            {
                IfeoBoost.Hive = Microsoft.Win32.Registry.LocalMachine;
                IfeoBoost.RootOverride = null;
                IfeoBoost.ClearArmed();
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(sandbox, false); } catch { }
            }
        }

        private static void TestLegacyPurgeKeepsDataWhenRestoreFails()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CaelusPurge_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            LegacyPurge.SkipRegistryDelete = true;
            try
            {
                string games = Path.Combine(dir, "Caelus.games.txt");
                string white = Path.Combine(dir, "Caelus.whitelist.txt");
                string doneFile = Path.Combine(dir, "purge.done");
                File.WriteAllText(games, "game-a");
                File.WriteAllText(white, "white-a");

                Settings.Save("PurgeV180Done", false);
                LegacyPurge.RestoreHook = delegate { return new List<string> { "电源计划", "IFEO" }; };
                LegacyPurge.RunOnce(dir);

                if (!File.Exists(games) || !File.Exists(white))
                    throw new Exception("restore failed but data was deleted anyway");
                Eq(false, File.Exists(doneFile));
                Eq(false, Settings.Load("PurgeV180Done", false));

                LegacyPurge.RestoreHook = delegate { return new List<string>(); };
                LegacyPurge.RunOnce(dir);
                if (File.Exists(games) || File.Exists(white))
                    throw new Exception("restore succeeded but data was not purged");
                // 完成标记写数据目录文件（注册表标记会把刚删的键重建出来），注册表不再写
                Eq(true, File.Exists(doneFile));
                Eq(false, Settings.Load("PurgeV180Done", false));

                File.WriteAllText(games, "re-added-by-user");
                LegacyPurge.RestoreHook = delegate { throw new Exception("must not restore twice"); };
                LegacyPurge.RunOnce(dir);
                Eq(true, File.Exists(games));
            }
            finally
            {
                LegacyPurge.RestoreHook = null;
                LegacyPurge.SkipRegistryDelete = false;
                Settings.Save("PurgeV180Done", false);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void TestLegacyPurgeNeverTouchesForeignFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CaelusPurge2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            LegacyPurge.SkipRegistryDelete = true;
            try
            {
                string exe = Path.Combine(dir, "Caelus.exe");
                string portable = Path.Combine(dir, "Caelus.portable");
                string userFile = Path.Combine(dir, "我的存档.sav");
                File.WriteAllText(exe, "MZ");
                File.WriteAllText(portable, "");
                File.WriteAllText(userFile, "save-data");
                File.WriteAllText(Path.Combine(dir, "Caelus.games.txt"), "g");

                Settings.Save("PurgeV180Done", false);
                LegacyPurge.RestoreHook = delegate { return new List<string>(); };
                LegacyPurge.RunOnce(dir);

                if (!File.Exists(exe)) throw new Exception("purge deleted Caelus.exe");
                if (!File.Exists(portable)) throw new Exception("purge deleted the portable marker");
                if (!File.Exists(userFile)) throw new Exception("purge deleted an unrelated user file");
                Eq(false, File.Exists(Path.Combine(dir, "Caelus.games.txt")));
            }
            finally
            {
                LegacyPurge.RestoreHook = null;
                LegacyPurge.SkipRegistryDelete = false;
                Settings.Save("PurgeV180Done", false);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>清单类 tweak 重复启用必须做并集合并：既有条目（含此前还原失败
        /// 留下的）不得被覆盖掉，新增条目追加且去重。</summary>
        private static void TestTweakDeviceListMerge()
        {
            // 既有条目保留、新增追加
            List<string> m1 = TweakDeviceList.Merge(new[] { "a", "b" }, new[] { "c" });
            Eq(3, m1.Count);
            Eq("a", m1[0]); Eq("b", m1[1]); Eq("c", m1[2]);
            // 重复启用同一批设备：不产生重复条目
            List<string> m2 = TweakDeviceList.Merge(new[] { "a", "b" }, new[] { "b", "c" });
            Eq(3, m2.Count);
            // 空清单与空新增的边界
            Eq(0, TweakDeviceList.Merge(null, null).Count);
            Eq(1, TweakDeviceList.Merge(new string[0], new[] { "x" }).Count);
            // 空字符串条目不进清单
            List<string> m3 = TweakDeviceList.Merge(new[] { "", "a" }, new[] { "" });
            Eq(1, m3.Count);
            Eq("a", m3[0]);
        }

        /// <summary>MEM_RESOURCE（cfgmgr32.h）偏移 8=MD_Alloc_Base、16=MD_Alloc_End：
        /// 长度必须按 末-基+1 计算；末<=基 与短缓冲都返回 0。</summary>
        private static void TestRebarRangeDecode()
        {
            var buf = new byte[24];
            // 高地址大窗口（ReBAR 常态）：base=0x3800000000, end=0x4000000000 → 0x800000001
            Array.Copy(BitConverter.GetBytes(0x3800000000UL), 0, buf, 8, 8);
            Array.Copy(BitConverter.GetBytes(0x4000000000UL), 0, buf, 16, 8);
            Eq(0x800000001UL, RebarProbe.DecodeRangeSize(buf));
            // 低地址窗口：base=0xA0000, end=0xBFFFF → 0x20000
            Array.Copy(BitConverter.GetBytes(0xA0000UL), 0, buf, 8, 8);
            Array.Copy(BitConverter.GetBytes(0xBFFFFUL), 0, buf, 16, 8);
            Eq(0x20000UL, RebarProbe.DecodeRangeSize(buf));
            // 末 <= 基：无效描述符；短缓冲与 null 返回 0
            Array.Copy(BitConverter.GetBytes(0x100UL), 0, buf, 16, 8);
            Eq(0UL, RebarProbe.DecodeRangeSize(buf));
            Eq(0UL, RebarProbe.DecodeRangeSize(new byte[8]));
            Eq(0UL, RebarProbe.DecodeRangeSize(null));
        }

        /// <summary>游戏模式守护：HKCU 开关写入、读回与还原往返（原值含"键不存在"）。</summary>
        private static void TestGameModeGuardRoundtrip()
        {
            const string barKey = @"Software\Microsoft\GameBar";
            const string valName = "AutoGameModeEnabled";
            object orig = null;
            bool origExisted = false;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(barKey))
                    if (k != null) { orig = k.GetValue(valName); origExisted = orig != null; }
                if (origExisted && !(orig is int)) Skip("AutoGameModeEnabled 原值类型异常，跳过往返");
                Settings.Save("GameModeGuardByCaelus", false);

                Eq(true, GameModeGuard.Enable());
                Eq(true, GameModeGuard.CurrentlyOn());
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(barKey))
                    Eq(1, (int)k.GetValue(valName));
                Eq(true, GameModeGuard.Restore());
                Eq(false, GameModeGuard.EnabledByCaelus);

                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(barKey))
                {
                    object v = k == null ? null : k.GetValue(valName);
                    if (origExisted) Eq((int)orig, (int)v);
                    else Eq(true, v == null);
                }
            }
            finally
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(barKey, true))
                    {
                        if (k != null)
                        {
                            if (origExisted) k.SetValue(valName, orig, Microsoft.Win32.RegistryValueKind.DWord);
                            else if (k.GetValue(valName) != null) k.DeleteValue(valName, false);
                        }
                    }
                }
                catch { }
                Settings.SaveStr("PrevAutoGameMode", "");
                Settings.Save("GameModeGuardByCaelus", false);
            }
        }

        /// <summary>窗口化游戏优化：DirectX 全局设置字符串的字段合并与还原往返。</summary>
        private static void TestWindowedOptRoundtrip()
        {
            const string gpuKey = @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences";
            const string valName = "DirectXUserGlobalSettings";
            string orig = null;
            bool origExisted = false;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(gpuKey))
                {
                    if (k != null)
                    {
                        object v = k.GetValue(valName);
                        orig = v as string;
                        origExisted = v != null && orig != null;
                    }
                }
                Settings.Save("WindowedOptOnByCaelus", false);
                Settings.SaveStr("PrevSwapEffectUpgrade", "");

                Eq(true, WindowedOptTweak.Enable());
                Eq(true, WindowedOptTweak.CurrentlyOn());
                Eq(true, WindowedOptTweak.Restore());
                Eq(false, WindowedOptTweak.EnabledByCaelus);

                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(gpuKey))
                {
                    object v = k == null ? null : k.GetValue(valName);
                    if (origExisted) Eq(orig, (string)v);
                    else Eq(false, WindowedOptTweak.CurrentlyOn());
                }
            }
            finally
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(gpuKey, true))
                    {
                        if (k != null)
                        {
                            if (origExisted) k.SetValue(valName, orig, Microsoft.Win32.RegistryValueKind.String);
                            else if (k.GetValue(valName) != null) k.DeleteValue(valName, false);
                        }
                    }
                }
                catch { }
                Settings.SaveStr("PrevSwapEffectUpgrade", "");
                Settings.Save("WindowedOptOnByCaelus", false);
            }
        }

        /// <summary>传递优化限制：策略键写入/读回/还原往返（含 DoSvc 服务的停与启）。</summary>
        private static void TestDoTweakRoundtrip()
        {
            const string doKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
            const string valName = "DOMaxBackgroundDownloadBandwidth";
            object orig = null;
            bool origExisted = false;
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(doKey))
                    if (k != null) { orig = k.GetValue(valName); origExisted = orig != null; }
                Settings.SaveStr("PrevDoSvcStopped", "");

                Eq(true, DoTweak.Activate());
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(doKey))
                {
                    object v = k == null ? null : k.GetValue(valName);
                    if (!(v is int) || (int)v != 1)
                        Skip("策略键不可写，无法验证传递优化往返");
                }
                Eq(true, DoTweak.Restore());
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(doKey))
                {
                    object v = k == null ? null : k.GetValue(valName);
                    if (origExisted) Eq((int)orig, (int)v);
                    else Eq(true, v == null);
                }
            }
            finally
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(doKey, true))
                    {
                        if (k != null)
                        {
                            if (origExisted) k.SetValue(valName, orig, Microsoft.Win32.RegistryValueKind.DWord);
                            else if (k.GetValue(valName) != null) k.DeleteValue(valName, false);
                        }
                    }
                }
                catch { }
                Settings.SaveStr("PrevDoBgBw", "");
                Settings.SaveStr("PrevDoSvcStopped", "");
                try { SvcCtl.EnsureStarted("DoSvc"); } catch { }
            }
        }

        /// <summary>Tamer 全量扫描间隔：事件可用时应显著长于纯轮询（事件减少扫描需求）。</summary>
        private static void TestTamerSweepInterval()
        {
            Eq(true, Tamer.FullSweepInterval(true) > Tamer.FullSweepInterval(false));
            Eq(8000, Tamer.FullSweepInterval(false));
            Eq(60000, Tamer.FullSweepInterval(true));
        }

        private static void TestKernelAntiCheatNamingIsHonest()
        {
            Eq("Ricochet", KernelAntiCheat.MatchByExe("cod22-cod"));
            Eq("Ricochet", KernelAntiCheat.MatchByExe(@"D:\Games\COD\cod.exe"));
            Eq("Ricochet", KernelAntiCheat.MatchByExe("ModernWarfare.exe"));
            if (KernelAntiCheat.MatchByExe("notepad.exe") != null)
                throw new Exception("an unknown executable must not be given an anti-cheat name");
            if (KernelAntiCheat.MatchByExe(null) != null)
                throw new Exception("null input must not produce a name");
            if (KernelAntiCheat.MatchByExe("") != null)
                throw new Exception("empty input must not produce a name");
            if (KernelAntiCheat.ServiceExists("CaelusDefinitelyNotAService_" + Guid.NewGuid().ToString("N")))
                throw new Exception("a nonexistent service must not be reported as installed");
        }

        /// <summary>MPO 还原必须停在快照原值：用户自己手动设过禁用值（5）时，
        /// 还原后不得再按"当前仍是禁用值"把用户自己的值删掉。</summary>
        private static void TestMpoRestoreKeepsUserOriginalValue()
        {
            const string dwmKey = @"SOFTWARE\Microsoft\Windows\Dwm";
            const string valName = "OverlayTestMode";
            object orig = null;
            bool origExisted = false;
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(dwmKey))
                {
                    if (k != null) { orig = k.GetValue(valName); origExisted = orig != null; }
                }
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(dwmKey, true))
                {
                    if (k == null) Skip("无法打开 DWM 注册表键");
                    k.SetValue(valName, 5, Microsoft.Win32.RegistryValueKind.DWord);
                }
                Settings.Save("MpoOffByCaelus", false);

                Eq(true, MpoTweak.Disable());
                Eq(true, MpoTweak.Restore());

                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(dwmKey))
                {
                    object v = k == null ? null : k.GetValue(valName);
                    Eq(true, v is int && (int)v == 5);
                }
                Eq(false, MpoTweak.DisabledByCaelus);
            }
            finally
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(dwmKey, true))
                    {
                        if (k != null)
                        {
                            if (origExisted) k.SetValue(valName, orig, Microsoft.Win32.RegistryValueKind.DWord);
                            else if (k.GetValue(valName) != null) k.DeleteValue(valName, false);
                        }
                    }
                }
                catch { }
                Settings.SaveStr("PrevMpoOverlay", "");
                Settings.Save("MpoOffByCaelus", false);
            }
        }

        /// <summary>HAGS 关闭必须停在快照原值：用户自己开了 HAGS（2）时，
        /// 关闭动作是"还原到原值"，不得再强制写成 1 覆盖用户设置。</summary>
        private static void TestHagsDisableKeepsUserOriginalValue()
        {
            const string gfxKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
            const string valName = "HwSchMode";
            object orig = null;
            bool origExisted = false;
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(gfxKey))
                {
                    if (k != null) { orig = k.GetValue(valName); origExisted = orig != null; }
                }
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(gfxKey, true))
                {
                    if (k == null) Skip("无法打开 GraphicsDrivers 注册表键");
                    k.SetValue(valName, 2, Microsoft.Win32.RegistryValueKind.DWord);
                }
                Settings.Save("HagsOnByCaelus", false);

                Eq(true, HagsTweak.Enable());
                Eq(true, HagsTweak.Disable());

                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(gfxKey))
                {
                    object v = k == null ? null : k.GetValue(valName);
                    Eq(true, v is int && (int)v == 2);
                }
                Eq(false, HagsTweak.EnabledByCaelus);
            }
            finally
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(gfxKey, true))
                    {
                        if (k != null)
                        {
                            if (origExisted) k.SetValue(valName, orig, Microsoft.Win32.RegistryValueKind.DWord);
                            else if (k.GetValue(valName) != null) k.DeleteValue(valName, false);
                        }
                    }
                }
                catch { }
                Settings.SaveStr("PrevHwSch", "");
                Settings.Save("HagsOnByCaelus", false);
            }
        }
    }
}
