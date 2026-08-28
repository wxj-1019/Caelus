// @author zenjiro 18967498922@163.com
// 文件用途 阻止系统为省电关闭网卡 免除唤醒延迟造成的对局卡顿

using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class DevicePowerTweak
    {
        private const string NetClass =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
        private const string ListKey = "DevPowerList";
        internal const int NoPowerDownBit = 0x08;

        private static readonly object lk = new object();

        public static bool EnabledByCaelus { get { return Settings.Load("DevPowerByCaelus", false); } }

        internal struct Adapter
        {
            public string Index;
            public string Description;
            public int? PnPCapabilities;
            public bool CanPowerDown;
        }

        public static List<Adapter> Scan()
        {
            var list = new List<Adapter>();
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(NetClass))
                {
                    if (cls == null) return list;
                    foreach (string idx in cls.GetSubKeyNames())
                    {
                        if (idx.Length != 4) continue;
                        using (var node = cls.OpenSubKey(idx))
                        {
                            if (node == null) continue;
                            string desc = node.GetValue("DriverDesc") as string;
                            if (string.IsNullOrEmpty(desc) || node.GetValue("NetCfgInstanceId") == null) continue;
                            object v = node.GetValue("PnPCapabilities");
                            int? cur = v is int ? (int?)(int)v : null;
                            list.Add(new Adapter
                            {
                                Index = idx,
                                Description = desc,
                                PnPCapabilities = cur,
                                CanPowerDown = !cur.HasValue || (cur.Value & NoPowerDownBit) == 0
                            });
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>物理网卡的 NetCfgInstanceId（= TCP/IP 接口 GUID）集合：WMI
        /// PhysicalAdapter 判定 + NetClass 的 MatchingDeviceId 关联。供 Nagle 这类
        /// 按接口 GUID 写注册表的 tweak 共用——Interfaces 下还有 WSL/Hyper-V 虚拟
        /// 交换、VPN 隧道与已卸载适配器残留，无差别写入徒增还原面。WMI 失败时
        /// 返回空集（调用方退回全量行为）。</summary>
        internal static HashSet<string> PhysicalAdapterNetCfgIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var physical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            string id = mo["PNPDeviceID"] as string;
                            if (string.IsNullOrEmpty(id)) continue;
                            // WMI 的 PNPDeviceID 带实例段（…\REV_A1\4&38A631B7&0&00E7），
                            // NetClass 的 MatchingDeviceId 是不带实例段的硬件 ID——截掉再比对
                            int cut = id.LastIndexOf('\\');
                            if (cut > 3) id = id.Substring(0, cut);
                            physical.Add(id);
                        }
                    }
                }
            }
            catch { return ids; }
            if (physical.Count == 0) return ids;
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(NetClass))
                {
                    if (cls == null) return ids;
                    foreach (string idx in cls.GetSubKeyNames())
                    {
                        if (idx.Length != 4) continue;
                        using (var node = cls.OpenSubKey(idx))
                        {
                            if (node == null) continue;
                            string matching = node.GetValue("MatchingDeviceId") as string;
                            if (string.IsNullOrEmpty(matching)) continue;
                            // USB 网卡的 MatchingDeviceId 与 WMI PNPDeviceID 截断结果
                            // 谁长谁短都可能（INF 兼容 ID 匹配）——双向前缀比对
                            bool hit = false;
                            foreach (string p in physical)
                                if (matching.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                                    || p.StartsWith(matching, StringComparison.OrdinalIgnoreCase))
                                {
                                    hit = true;
                                    break;
                                }
                            if (!hit) continue;
                            string cfg = node.GetValue("NetCfgInstanceId") as string;
                            if (!string.IsNullOrEmpty(cfg)) ids.Add(cfg);
                        }
                    }
                }
            }
            catch { }
            return ids;
        }

        public static bool Enable()
        {
            lock (lk)
            {
                // 并集合并：此前已在清单里的网卡（含还原失败留下的）不得被本轮覆盖掉；
                // 回滚只针对本轮真正写入的网卡（appliedNow）
                List<string> done = TweakDeviceList.Merge(
                    ParseList(Settings.LoadStr(ListKey, "")), new string[0]);
                var appliedNow = new List<string>();
                bool sawCandidate = false;
                foreach (Adapter a in Scan())
                {
                    if (!a.CanPowerDown) continue;
                    sawCandidate = true;
                    int target = (a.PnPCapabilities.HasValue ? a.PnPCapabilities.Value : 0) | NoPowerDownBit;
                    if (Reg(a.Index).Apply(target))
                    {
                        appliedNow.Add(a.Index);
                        if (!done.Contains(a.Index)) done.Add(a.Index);
                    }
                    else Logger.Log("网卡省电：写入失败 " + a.Description);
                }
                if (appliedNow.Count == 0)
                {
                    if (!sawCandidate)
                    {
                        Logger.Log("网卡省电：所有网卡均已禁止系统断电，无需改动");
                        return true;
                    }
                    return false;
                }
                if (!Settings.SaveStr(ListKey, string.Join(";", done.ToArray())))
                {
                    foreach (string idx in appliedNow) Reg(idx).Restore();
                    Logger.Log("网卡省电：清单无法持久化，本轮改动已还原（既有清单保留）");
                    return false;
                }
                Settings.Save("DevPowerByCaelus", true);
                Logger.Log("网卡省电：本轮新禁止 " + appliedNow.Count + " 块（清单共 "
                    + done.Count + " 块网卡）");
                return true;
            }
        }

        public static bool Restore()
        {
            lock (lk)
            {
                bool all = true;
                foreach (string idx in ParseList(Settings.LoadStr(ListKey, "")))
                    all &= Reg(idx).Restore();
                if (all)
                {
                    Settings.SaveStr(ListKey, "");
                    Settings.Save("DevPowerByCaelus", false);
                    Logger.Log("网卡省电：已还原原值");
                }
                else Logger.Log("网卡省电：部分网卡还原失败，快照保留待下次重试");
                return all;
            }
        }

        private static ReversibleReg Reg(string index)
        {
            // 设备驱动键只写不改：设备中途移除时不能 CreateSubKey 重建幽灵键
            return new ReversibleReg(Registry.LocalMachine, NetClass + @"\" + index,
                "PnPCapabilities", RegistryValueKind.DWord, "DevPower_" + index,
                ReversibleReg.WriteMode.OpenOnly);
        }

        internal static string[] ParseList(string raw)
        {
            return (raw ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        internal static int Merge(int? current, bool block)
        {
            int baseVal = current.HasValue ? current.Value : 0;
            return block ? (baseVal | NoPowerDownBit) : (baseVal & ~NoPowerDownBit);
        }
    }
}
