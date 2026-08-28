// @author zenjiro 18967498922@163.com
// 文件用途 为支持但未启用消息信号中断的设备开启 MSI 还原时删键回到系统默认

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class MsiModeTweak
    {
        private const string EnumRoot = @"SYSTEM\CurrentControlSet\Enum";
        private const string MsiLeaf = @"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
        private const string ListKey = "MsiList";

        private static readonly object lk = new object();

        private static readonly string[] AllowedClasses = { "Display", "Net" };

        public static bool EnabledByCaelus { get { return Settings.Load("MsiOnByCaelus", false); } }

        internal struct Candidate
        {
            public string InstanceId;
            public string Description;
            public bool HasKey;
            public int? Value;
        }

        private sealed class ScannedDevice
        {
            public string Cls;
            public string Vendor;
            public Candidate Candidate;
        }

        /// <summary>设备 ID 目录名（VEN_XXXX&DEV_…&SUBSYS_…）里的厂商段。</summary>
        private static string VendorTokenOf(string deviceIdFolder)
        {
            if (string.IsNullOrEmpty(deviceIdFolder)) return null;
            int idx = deviceIdFolder.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
            if (idx != 0 || deviceIdFolder.Length < 8) return null;
            return deviceIdFolder.Substring(0, 8).ToUpperInvariant();
        }

        private static readonly object scanLk = new object();
        private static List<Candidate> scanCache;
        private static long scanCacheUntilTicks;
        private const long ScanCacheTtlTicks = 5L * TimeSpan.TicksPerSecond;

        /// <summary>全量遍历 PCI 三层注册表树，数百毫秒级；环境页可用性探针、描述
        /// 与托盘回刷会在同一时间窗内重复调用——5 秒 TTL 缓存挡住重复扫描。</summary>
        public static List<Candidate> Scan()
        {
            lock (scanLk)
            {
                if (scanCache != null && DateTime.UtcNow.Ticks < scanCacheUntilTicks)
                    return new List<Candidate>(scanCache);
            }
            List<Candidate> found = ScanCore();
            lock (scanLk)
            {
                scanCache = found;
                scanCacheUntilTicks = DateTime.UtcNow.Ticks + ScanCacheTtlTicks;
            }
            return new List<Candidate>(found);
        }

        private static List<Candidate> ScanCore()
        {
            // 显卡的 HDMI/DP 音频是同卡另一 PCI function（Media 类），在线中断下是
            // 常见 DPC 尖峰来源；但 Media 类也含采集卡等杂设备——只纳入与已扫到的
            // Display 设备同厂商的 Media function，避免误扩
            var all = new List<ScannedDevice>();
            try
            {
                using (var pci = Registry.LocalMachine.OpenSubKey(EnumRoot + @"\PCI"))
                {
                    if (pci == null) return new List<Candidate>();
                    foreach (string devClass in pci.GetSubKeyNames())
                        using (var dev = pci.OpenSubKey(devClass))
                        {
                            if (dev == null) continue;
                            foreach (string inst in dev.GetSubKeyNames())
                                using (var node = dev.OpenSubKey(inst))
                                {
                                    if (node == null) continue;
                                    string cls = node.GetValue("Class") as string;
                                    if (cls == null) continue;
                                    string id = @"PCI\" + devClass + @"\" + inst;
                                    var c = new Candidate
                                    {
                                        InstanceId = id,
                                        Description = (node.GetValue("DeviceDesc") as string) ?? id
                                    };
                                    int cut = c.Description.LastIndexOf(';');
                                    if (cut >= 0) c.Description = c.Description.Substring(cut + 1);
                                    using (var msi = node.OpenSubKey(MsiLeaf))
                                    {
                                        c.HasKey = msi != null;
                                        object v = msi == null ? null : msi.GetValue("MSISupported");
                                        c.Value = v is int ? (int?)(int)v : null;
                                    }
                                    all.Add(new ScannedDevice
                                    {
                                        Cls = cls,
                                        Vendor = VendorTokenOf(devClass),
                                        Candidate = c
                                    });
                                }
                        }
                }
            }
            catch { }
            var displayVendors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScannedDevice d in all)
                if (d.Cls == "Display" && d.Vendor != null) displayVendors.Add(d.Vendor);
            var found = new List<Candidate>();
            foreach (ScannedDevice d in all)
            {
                // 注册表 Class 值音频类是全大写 "MEDIA"（INF 类名约定），必须忽略大小写
                if (Array.IndexOf(AllowedClasses, d.Cls) >= 0
                    || (string.Equals(d.Cls, "Media", StringComparison.OrdinalIgnoreCase)
                        && d.Vendor != null && displayVendors.Contains(d.Vendor)))
                    found.Add(d.Candidate);
            }
            return found;
        }

        public static List<Candidate> Disabled()
        {
            var need = new List<Candidate>();
            foreach (Candidate c in Scan())
                if (c.HasKey && c.Value.HasValue && c.Value.Value == 0) need.Add(c);
            return need;
        }

        public static bool Enable()
        {
            lock (lk)
            {
                List<Candidate> targets = Disabled();
                if (targets.Count == 0)
                {
                    Logger.Log("MSI 模式：显卡/网卡/配套音频均已启用消息信号中断，无需改动");
                    return true;
                }
                // 并集合并：此前已在清单里的设备（含还原失败留下的）不得被本轮覆盖掉；
                // 回滚只针对本轮真正写入的设备（appliedNow），不能波及历史条目
                List<string> done = TweakDeviceList.Merge(
                    ParseList(Settings.LoadStr(ListKey, "")), new string[0]);
                var appliedNow = new List<string>();
                foreach (Candidate c in targets)
                {
                    if (Reg(c.InstanceId).Apply(1))
                    {
                        appliedNow.Add(c.InstanceId);
                        if (!done.Contains(c.InstanceId)) done.Add(c.InstanceId);
                    }
                    else Logger.Log("MSI 模式：写入失败 " + c.Description);
                }
                if (appliedNow.Count == 0) return false;
                if (!Settings.SaveStr(ListKey, string.Join(";", done.ToArray())))
                {
                    foreach (string id in appliedNow) Reg(id).Restore();
                    Logger.Log("MSI 模式：清单无法持久化，本轮改动已还原（既有清单保留）");
                    return false;
                }
                Settings.Save("MsiOnByCaelus", true);
                Logger.Log("MSI 模式：本轮启用 " + appliedNow.Count + " 个设备（清单共 "
                    + done.Count + " 个），重启后生效");
                return true;
            }
        }

        public static bool Restore()
        {
            lock (lk)
            {
                bool all = true;
                foreach (string id in ParseList(Settings.LoadStr(ListKey, "")))
                    all &= Reg(id).Restore();
                if (all)
                {
                    Settings.SaveStr(ListKey, "");
                    Settings.Save("MsiOnByCaelus", false);
                    Logger.Log("MSI 模式：已还原各设备原值，重启后生效");
                }
                else Logger.Log("MSI 模式：部分设备还原失败，快照保留待下次重试");
                return all;
            }
        }

        private static ReversibleReg Reg(string instanceId)
        {
            // MSI 叶子键可能默认不存在（须创建），但设备 Enum 父链在才写——
            // 设备中途移除时不能 CreateSubKey 重建幽灵设备路径
            return new ReversibleReg(Registry.LocalMachine,
                EnumRoot + @"\" + instanceId + @"\" + MsiLeaf,
                "MSISupported", RegistryValueKind.DWord,
                "Msi_" + instanceId.Replace('\\', '_'), ReversibleReg.WriteMode.RequireParent);
        }

        internal static string[] ParseList(string raw)
        {
            return (raw ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
