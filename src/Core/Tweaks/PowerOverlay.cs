// @author zenjiro 18967498922@163.com
// 文件用途 独占模式切换电源滑块到最佳性能 原值取自注册表 退出还原

using System;
using System.Collections.Generic;
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

        private static readonly Guid Saver = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private const string SaverSnapKey = "PowerOverlayDcSaverSnap";
        private const string SaverSnapAbsent = "\x1f";   // 快照哨兵：原本无 DC 值
        private static readonly HashSet<string> saverOwners = new HashSet<string>(StringComparer.Ordinal);

        internal const string OwnerDailyCare = "dailycare";

        /// <summary>续航档 GUID 文本（自测锚定，防误改）</summary>
        internal static string SaverGuidText { get { return Saver.ToString(); } }

        /// <summary>原始注册表串是否为续航档（纯逻辑可单测）</summary>
        internal static bool IsSaverGuid(string raw)
        {
            return ParseGuidOrNull(raw) == Saver;
        }

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

        /// <summary>日常养护·电池联动：DC 侧滑块切「更长的续航」。多占用方引用计数；
        /// 游戏档快照在位时让位不动（游戏优先）。</summary>
        public static bool ActivateDcSaver(string owner)
        {
            if (!Supported()) return false;
            lock (lk)
            {
                if (!SharedEffectClaim.Acquire(saverOwners, owner)) return true;
                if (Settings.LoadStr(SnapKey, "").Length > 0)
                {
                    SharedEffectClaim.Release(saverOwners, owner);
                    Logger.Log("电源滑块：游戏档占用中，续航档本轮让位");
                    return false;
                }
                string dcBefore = TryReadDcRaw();
                if (IsSaverGuid(dcBefore)) return true;   // 已是续航档：无快照也视为成功
                if (!Settings.SaveStr(SaverSnapKey, dcBefore ?? SaverSnapAbsent))
                {
                    SharedEffectClaim.Release(saverOwners, owner);
                    Logger.Log("电源滑块：续航档快照无法持久化，本轮未切换");
                    return false;
                }
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(SchemeKey, true))
                    {
                        if (key == null) throw new System.IO.IOException("SchemeKey unavailable");
                        key.SetValue(DcValue, Saver.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Settings.SaveStr(SaverSnapKey, "");
                    SharedEffectClaim.Release(saverOwners, owner);
                    Logger.Log("电源滑块：续航档写入失败（" + ex.GetType().Name + "）");
                    return false;
                }
                Logger.Log("电源滑块：电池档已切到「更长的续航」，回电或场景挂起时还原");
                return true;
            }
        }

        /// <summary>还原 DC 续航档。最后一个占用方释放才真正还原；失败快照保留下次再试。</summary>
        public static bool RestoreDcSaver(string owner)
        {
            lock (lk)
            {
                if (!SharedEffectClaim.Release(saverOwners, owner)) return true;
                return RestoreDcSaverCore();
            }
        }

        private static bool RestoreDcSaverCore()
        {
            string saved = Settings.LoadStr(SaverSnapKey, "");
            if (saved.Length == 0) return true;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(SchemeKey, true))
                {
                    if (key == null) return false;
                    if (saved == SaverSnapAbsent) key.DeleteValue(DcValue, false);
                    else key.SetValue(DcValue, saved);
                }
            }
            catch { return false; }   // 快照保留，下次启动/释放继续
            Settings.SaveStr(SaverSnapKey, "");
            Logger.Log("电源滑块：电池档续航已还原");
            return true;
        }

        public static void HealFromCrash()
        {
            if (Settings.LoadStr(SnapKey, "").Length > 0)
                if (Restore()) Logger.Log("检测到上次未还原的电源滑块设置，已恢复");
            // 续航档崩溃自愈：进程已死，占用记账无从谈起，清空后直接还原
            if (Settings.LoadStr(SaverSnapKey, "").Length > 0)
            {
                lock (lk) { SharedEffectClaim.ReleaseAll(saverOwners); }
                if (RestoreDcSaverCore()) Logger.Log("检测到上次未还原的电池续航档设置，已恢复");
            }
        }
    }
}
