// @author zenjiro 18967498922@163.com
// 文件用途 独占模式切换电源滑块到最佳性能 原值取自注册表 退出还原

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class PowerOverlay
    {
        private const string SchemeKey = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
        private const string AcValue = "ActiveOverlayAcPowerScheme";
        private const string DcValue = "ActiveOverlayDcPowerScheme";
        private const string SnapKey = "PowerOverlaySnap";

        private static readonly Guid Max = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

        private static readonly object lk = new object();
        private static int support;

        public static bool Supported()
        {
            lock (lk)
            {
                if (support != 0) return support > 0;
                bool ok = false;
                try
                {
                    Guid current;
                    ok = TryReadActive(out current);
                }
                catch { }
                support = ok ? 1 : -1;
                if (!ok) Logger.Log("电源滑块：本机读不到 overlay 状态，独占模式跳过该项");
                return ok;
            }
        }

        internal static bool TryReadActive(out Guid value)
        {
            value = Guid.Empty;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(SchemeKey))
                {
                    if (key == null) return false;
                    string raw = key.GetValue(AcValue) as string;
                    if (string.IsNullOrEmpty(raw)) return false;
                    value = new Guid(raw);
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>读取 DC 侧滑块当前值（原始字符串，值不存在返回 null）。
        /// 部分系统上 PowerSetActiveOverlayScheme 只写 AC，笔记本电池档滑块独立存在。</summary>
        internal static string TryReadDcRaw()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(SchemeKey))
                {
                    if (key == null) return null;
                    return key.GetValue(DcValue) as string;
                }
            }
            catch { return null; }
        }

        public static bool Activate()
        {
            if (!Supported()) return false;
            lock (lk)
            {
                Guid before;
                if (!TryReadActive(out before)) return false;
                string dcBefore = TryReadDcRaw();
                Guid dcBeforeGuid = ParseGuidOrNull(dcBefore);
                if (before == Max && dcBeforeGuid == Max) return true;
                if (Settings.LoadStr(SnapKey, "").Length == 0
                    && !Settings.SaveStr(SnapKey, before.ToString() + "|" + (dcBefore ?? "")))
                {
                    Logger.Log("电源滑块：快照无法持久化，本轮未切换");
                    return false;
                }
                uint status;
                try { status = PowerSetActiveOverlayScheme(Max); }
                catch (EntryPointNotFoundException) { support = -1; return false; }
                catch { return false; }
                Guid after;
                if (status != 0 || !TryReadActive(out after) || after != Max)
                {
                    Logger.Log("电源滑块：切最佳性能失败（状态 " + status + "）");
                    return false;
                }
                Guid dcAfter = ParseGuidOrNull(TryReadDcRaw());
                if (dcAfter != Max)
                    Logger.Log("电源滑块：API 未覆盖电池档，还原时将按快照补写 DC 值");
                Logger.Log("电源滑块：已切到最佳性能，退出独占模式还原");
                return true;
            }
        }

        public static bool Restore()
        {
            lock (lk)
            {
                string saved = Settings.LoadStr(SnapKey, "");
                if (saved.Length == 0) return true;
                // 快照格式：新 "AC|DC"（DC 为原始串，缺失记空）；旧单值为仅 AC、DC 不动
                string acPart = saved;
                string dcPart = null;
                int bar = saved.IndexOf('|');
                if (bar >= 0)
                {
                    acPart = saved.Substring(0, bar);
                    dcPart = saved.Substring(bar + 1);
                }
                Guid original;
                try { original = new Guid(acPart); }
                catch { Settings.SaveStr(SnapKey, ""); return true; }
                bool dcKnown = bar >= 0;
                uint status;
                try { status = PowerSetActiveOverlayScheme(original); }
                catch { return false; }
                Guid after;
                if (status != 0 || !TryReadActive(out after) || after != original)
                {
                    Logger.Log("电源滑块还原失败，快照保留，下次启动继续尝试");
                    return false;
                }
                if (dcKnown)
                {
                    string dcNow = TryReadDcRaw();
                    if (!string.Equals(dcNow ?? "", dcPart ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        bool dcOk;
                        try
                        {
                            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(SchemeKey, true))
                            {
                                if (key == null) dcOk = false;
                                else if (string.IsNullOrEmpty(dcPart))
                                {
                                    // 原本无 DC 值而 API 设了一个：删值回到原状
                                    key.DeleteValue(DcValue, false);
                                    dcOk = true;
                                }
                                else
                                {
                                    key.SetValue(DcValue, dcPart);
                                    dcOk = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            dcOk = false;
                            Logger.Log("电源滑块：DC 值补写异常 " + ex.GetType().Name);
                        }
                        if (!dcOk)
                        {
                            Logger.Log("电源滑块：DC 值还原失败，快照保留，下次启动继续尝试");
                            return false;
                        }
                        // 直写注册表没有 API 的下发动作：注册表值已归位，当前会话的电池档
                        // 滑块可能要等下次电源切换/重启才呈现还原值
                        Logger.Log("电源滑块：电池档注册表值已按快照补写（实际生效可能滞后到下次电源切换）");
                    }
                }
                Settings.SaveStr(SnapKey, "");
                Logger.Log("电源滑块已还原");
                return true;
            }
        }

        private static Guid ParseGuidOrNull(string raw)
        {
            try
            {
                if (string.IsNullOrEmpty(raw)) return Guid.Empty;
                return new Guid(raw);
            }
            catch { return Guid.Empty; }
        }

        public static void HealFromCrash()
        {
            if (Settings.LoadStr(SnapKey, "").Length == 0) return;
            if (Restore()) Logger.Log("检测到上次未还原的电源滑块设置，已恢复");
        }
    }
}
