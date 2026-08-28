// @author zenjiro 18967498922@163.com
// 文件用途 逐网卡禁用 Nagle 与延迟 ACK 只惠及 TCP 联网的游戏 逐值快照可逆

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class NagleTweak
    {
        private const string IfRoot = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
        private const string ListKey = "NagleIfList";

        private static readonly object lk = new object();

        public static bool EnabledByCaelus { get { return Settings.Load("NagleOffByCaelus", false); } }

        private static ReversibleReg RegOf(string guid, string valName)
        {
            // 接口 GUID 键只写不改：适配器卸载后残留键不应被重建
            return new ReversibleReg(Registry.LocalMachine, IfRoot + "\\" + guid, valName,
                RegistryValueKind.DWord, "Nagle_" + valName + "_" + guid,
                ReversibleReg.WriteMode.OpenOnly);
        }

        public static bool Enable()
        {
            lock (lk)
            {
                try
                {
                    string[] guids;
                    using (var root = Registry.LocalMachine.OpenSubKey(IfRoot))
                    {
                        if (root == null) return false;
                        guids = root.GetSubKeyNames();
                    }
                    // 并集合并 + 逐值记账：任一值写入成功该网卡就进清单（Restore 对无
                    // 备份的值是幂等空操作），否则部分成功的那半会被永久留在系统里。
                    // 清单持久化失败的回滚只针对本轮写入的网卡
                    List<string> touched = TweakDeviceList.Merge(
                        ParseList(Settings.LoadStr(ListKey, "")), new string[0]);
                    var touchedNow = new List<string>();
                    // 只写物理网卡接口：虚拟交换/VPN/残留 GUID 不再被无差别写入；
                    // 物理集合拿不到（WMI 失败）时退回旧的全量行为
                    HashSet<string> physical = DevicePowerTweak.PhysicalAdapterNetCfgIds();
                    int skippedByFilter = 0;
                    foreach (string guid in guids)
                    {
                        if (physical.Count > 0 && !physical.Contains(guid))
                        {
                            skippedByFilter++;
                            continue;
                        }
                        bool ack = RegOf(guid, "TcpAckFrequency").Apply(1);
                        bool nodelay = RegOf(guid, "TCPNoDelay").Apply(1);
                        if (ack || nodelay)
                        {
                            touchedNow.Add(guid);
                            if (!touched.Contains(guid)) touched.Add(guid);
                        }
                        if (!ack || !nodelay)
                            Logger.Log(ack || nodelay
                                ? "TCP 低延迟：网卡 " + guid + " 部分值写入失败（"
                                    + (ack ? "" : "TcpAckFrequency ")
                                    + (nodelay ? "" : "TCPNoDelay") + "），已记入清单可还原"
                                : "TCP 低延迟：网卡 " + guid + " 两个值均写入失败，本轮跳过（未入清单）");
                    }
                    if (touchedNow.Count == 0)
                    {
                        if (skippedByFilter > 0)
                            Logger.Log("TCP 低延迟：" + skippedByFilter
                                + " 个接口被物理网卡过滤跳过且无可写入项，请反馈日志");
                        return false;
                    }
                    if (!Settings.SaveStr(ListKey, string.Join(";", touched.ToArray())))
                    {
                        foreach (string guid in touchedNow)
                        {
                            RegOf(guid, "TcpAckFrequency").Restore();
                            RegOf(guid, "TCPNoDelay").Restore();
                        }
                        Logger.Log("TCP 低延迟：网卡清单无法持久化，本轮改动已还原（既有清单保留）");
                        return false;
                    }
                    Settings.Save("NagleOffByCaelus", true);
                    Logger.Log("TCP 低延迟已启用：本轮新写入 " + touchedNow.Count
                        + " 块网卡（清单共 " + touched.Count + " 块），新建连接生效");
                    return true;
                }
                catch { return false; }
            }
        }

        public static bool Restore()
        {
            lock (lk)
            {
                try
                {
                    bool all = true;
                    foreach (string guid in ParseList(Settings.LoadStr(ListKey, "")))
                    {
                        all &= RegOf(guid, "TcpAckFrequency").Restore();
                        all &= RegOf(guid, "TCPNoDelay").Restore();
                    }
                    if (all)
                    {
                        Settings.SaveStr(ListKey, "");
                        Settings.Save("NagleOffByCaelus", false);
                        Logger.Log("TCP 低延迟已关闭，各网卡原值已还原");
                    }
                    else Logger.Log("TCP 低延迟：部分网卡还原失败，快照保留待下次重试");
                    return all;
                }
                catch { return false; }
            }
        }

        internal static string[] ParseList(string raw)
        {
            return (raw ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
