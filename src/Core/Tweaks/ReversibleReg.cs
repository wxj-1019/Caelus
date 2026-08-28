// @author zenjiro 18967498922@163.com
// 文件用途 为注册表改动保存原值并提供可靠恢复

using System;
using Microsoft.Win32;

namespace CaelusApp
{
    internal sealed class ReversibleReg
    {
        public const string Absent = "__caelus_absent__";
        private const string LegacyAbsent = "none";

        /// <summary>设备作用域键的防幽灵写入模式——CreateSubKey 在设备被拔出/禁用后会
        /// 把整条已删除的设备路径重建出来（幽灵键，设备管理器不认、Restore 也删不掉）：
        /// OpenOnly：目标键自身必须已存在（设备的驱动键/接口键，设备在键就在）。
        /// RequireParent：允许创建叶子键（如 Affinity Policy 默认不存在），但父键
        /// （设备参数键）必须已存在——父键在即设备在，设备没了就不写。</summary>
        internal enum WriteMode
        {
            CreateAlways = 0,
            OpenOnly = 1,
            RequireParent = 2,
        }

        private readonly RegistryKey hive;
        private readonly string subKey;
        private readonly string valName;
        private readonly RegistryValueKind kind;
        private readonly string slot;
        private readonly WriteMode mode;

        public ReversibleReg(RegistryKey hive, string subKey, string valName, RegistryValueKind kind, string slot)
            : this(hive, subKey, valName, kind, slot, WriteMode.CreateAlways)
        {
        }

        public ReversibleReg(RegistryKey hive, string subKey, string valName, RegistryValueKind kind, string slot, WriteMode writeMode)
        {
            this.hive = hive; this.subKey = subKey; this.valName = valName; this.kind = kind; this.slot = slot;
            mode = writeMode;
        }

        public bool HasBackup { get { return Settings.LoadStr(slot, "").Length > 0; } }

        /// <summary>父键路径（最后一段 '\' 之前）；无 '\' 返回 null。</summary>
        private string ParentPath()
        {
            int cut = subKey.LastIndexOf('\\');
            return cut <= 0 ? null : subKey.Substring(0, cut);
        }

        public bool Apply(object newVal)
        {
            try
            {
                if (mode == WriteMode.RequireParent)
                {
                    string parent = ParentPath();
                    using (RegistryKey pk = parent == null ? null : hive.OpenSubKey(parent))
                    {
                        if (pk == null)
                        {
                            // 设备已不在（拔出/禁用/卸载）：不是失败也不写，保留无快照状态
                            Logger.Log("父键不存在（设备可能已移除），跳过 " + valName + "：" + subKey);
                            return false;
                        }
                    }
                }
                using (RegistryKey k = mode == WriteMode.OpenOnly
                    ? hive.OpenSubKey(subKey, true)
                    : hive.CreateSubKey(subKey))
                {
                    if (k == null)
                    {
                        if (mode == WriteMode.OpenOnly)
                            Logger.Log("注册表键不存在（设备可能已移除），跳过 " + valName + "：" + subKey);
                        return false;
                    }
                    if (Settings.LoadStr(slot, "").Length == 0)
                    {
                        object cur = k.GetValue(valName);
                        if (cur != null)
                        {
                            RegistryValueKind curKind = RegistryValueKind.Unknown;
                            try { curKind = k.GetValueKind(valName); } catch { }
                            if (curKind != kind)
                            {
                                Logger.Log(valName + " 原值类型异常（" + curKind + "），跳过此项调整");
                                return false;
                            }
                        }
                        string snapshot;
                        if (cur == null) snapshot = Absent;
                        else if (kind == RegistryValueKind.Binary) snapshot = "b" + Convert.ToBase64String((byte[])cur);
                        else snapshot = "=" + cur;
                        Settings.SaveStr(slot, snapshot);
                        if (Settings.LoadStr(slot, "") != snapshot)
                        {
                            Logger.Log("无法持久化 " + valName + " 原值快照，已取消写入");
                            return false;
                        }
                    }
                    k.SetValue(valName, newVal, kind);
                    object actual = k.GetValue(valName);
                    if (actual == null) return false;
                    if (kind == RegistryValueKind.DWord)
                        return Convert.ToInt64(actual) == Convert.ToInt64(newVal);
                    if (kind == RegistryValueKind.Binary)
                        return BytesEqual((byte[])actual, (byte[])newVal);
                    return string.Equals(actual.ToString(), newVal == null ? "" : newVal.ToString(), StringComparison.Ordinal);
                }
            }
            catch { return false; }
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public bool Matches(object expected)
        {
            try
            {
                using (var k = hive.OpenSubKey(subKey))
                {
                    object cur = k == null ? null : k.GetValue(valName);
                    if (cur == null) return false;
                    if (kind == RegistryValueKind.DWord)
                        return Convert.ToInt64(cur) == Convert.ToInt64(expected);
                    if (kind == RegistryValueKind.Binary)
                        return BytesEqual(cur as byte[], expected as byte[]);
                    return string.Equals(cur.ToString(), expected == null ? "" : expected.ToString(), StringComparison.Ordinal);
                }
            }
            catch { return false; }
        }

        public bool Restore()
        {
            string s = Settings.LoadStr(slot, "");
            if (s.Length == 0) return true;

            bool absent = s == Absent || s == LegacyAbsent;
            object val = null;
            if (!absent)
            {
                if (kind == RegistryValueKind.Binary)
                {
                    if (s.Length < 1 || s[0] != 'b')
                    {
                        Settings.SaveStr(slot, "");
                        Logger.Log("注册表快照损坏，放弃还原 " + valName + "（二进制快照格式不符）");
                        return false;
                    }
                    try { val = Convert.FromBase64String(s.Substring(1)); }
                    catch
                    {
                        Settings.SaveStr(slot, "");
                        Logger.Log("注册表快照损坏，放弃还原 " + valName + "（二进制快照解码失败）");
                        return false;
                    }
                }
                else
                {
                    string v = s[0] == '=' ? s.Substring(1) : s;
                    if (kind == RegistryValueKind.DWord)
                    {
                        long n;
                        if (!long.TryParse(v, out n))
                        {
                            Settings.SaveStr(slot, "");
                            Logger.Log("注册表快照损坏，放弃还原 " + valName + "（记录值 \"" + v + "\"）");
                            return false;
                        }
                        val = unchecked((int)n);
                    }
                    else val = v;
                }
            }

            try
            {
                bool restored = false;
                using (var k = hive.OpenSubKey(subKey, true))
                {

                    if (k == null) restored = true;
                    else if (absent)
                    {
                        if (k.GetValue(valName) != null) k.DeleteValue(valName, false);
                        restored = k.GetValue(valName) == null;
                    }
                    else
                    {
                        k.SetValue(valName, val, kind);
                        object actual = k.GetValue(valName);
                        RegistryValueKind actualKind = RegistryValueKind.Unknown;
                        try { actualKind = k.GetValueKind(valName); } catch { }
                        restored = actual != null && actualKind == kind;
                        if (restored && kind == RegistryValueKind.DWord)
                            restored = Convert.ToInt64(actual) == Convert.ToInt64(val);
                        else if (restored && kind == RegistryValueKind.Binary)
                            restored = BytesEqual((byte[])actual, (byte[])val);
                        else if (restored)
                            restored = string.Equals(actual.ToString(), val == null ? "" : val.ToString(), StringComparison.Ordinal);
                    }
                }
                if (!restored)
                {
                    Logger.Log("还原 " + valName + " 后回读不一致，快照保留待下次重试");
                    return false;
                }
                Settings.SaveStr(slot, "");
                return Settings.LoadStr(slot, "").Length == 0;
            }
            catch
            {
                Logger.Log("还原 " + valName + " 失败（多半是权限不足），快照保留待下次重试");
                return false;
            }
        }
    }
}
