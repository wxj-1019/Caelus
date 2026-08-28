// @author zenjiro 18967498922@163.com
// 文件用途 会话期间应用 AMD 全局 3D 设置 快照先行 回读核验 退出恢复 崩溃续还原 实验性

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CaelusApp
{
    internal static class AdlxTweaks
    {
        private const string SnapKey = "AmdSnap";
        public const int RisSharpness = 80;
        private static readonly object lk = new object();

        public static bool Available
        {
            get { return AdlxApi.Available; }
        }

        public static bool ActivateAntiLag()
        {
            return ApplyToggle("alag", "Anti-Lag",
                delegate(IntPtr gpu, out bool supported, out bool enabled)
                { return AdlxApi.AntiLagGet(gpu, out supported, out enabled); },
                delegate(IntPtr gpu) { return AdlxApi.AntiLagSet(gpu, true); },
                true);
        }

        public static bool RestoreAntiLag()
        {
            return RestoreToggle("alag", "Anti-Lag",
                delegate(IntPtr gpu, bool on) { return AdlxApi.AntiLagSet(gpu, on); },
                delegate(IntPtr gpu, out bool supported, out bool enabled)
                { return AdlxApi.AntiLagGet(gpu, out supported, out enabled); });
        }

        public static bool ActivateEnhancedSync()
        {
            return ApplyToggle("esync", "Enhanced Sync",
                delegate(IntPtr gpu, out bool supported, out bool enabled)
                { return AdlxApi.EnhancedSyncGet(gpu, out supported, out enabled); },
                delegate(IntPtr gpu) { return AdlxApi.EnhancedSyncSet(gpu, true); },
                true);
        }

        public static bool RestoreEnhancedSync()
        {
            return RestoreToggle("esync", "Enhanced Sync",
                delegate(IntPtr gpu, bool on) { return AdlxApi.EnhancedSyncSet(gpu, on); },
                delegate(IntPtr gpu, out bool supported, out bool enabled)
                { return AdlxApi.EnhancedSyncGet(gpu, out supported, out enabled); });
        }

        public static bool ActivateChill(int targetFps)
        {
            if (targetFps <= 0 || !Available) return false;
            lock (lk)
            {
                IntPtr[] gpus = AdlxApi.GetGpus();
                if (gpus == null || gpus.Length == 0) return false;
                try
                {
                    var snapshot = NvDrsTweaks.ParseSnapshot(Settings.LoadStr(SnapKey, ""));
                    string[] gpuKeys = GpuStableKeys(gpus);
                    int applied = 0, failed = 0;
                    for (int i = 0; i < gpus.Length; i++)
                    {
                        bool supported, enabled;
                        int minFps, maxFps;
                        AdlxIntRange range;
                        if (!AdlxApi.ChillGet(gpus[i], out supported, out enabled, out minFps, out maxFps, out range)
                            || !supported) continue;
                        string key = gpuKeys[i] + ".chill";
                        if (!snapshot.ContainsKey(key))
                        {
                            snapshot[key] = (enabled ? "1" : "0") + "|" + minFps + "|" + maxFps;
                            if (!Settings.SaveStr(SnapKey, NvDrsTweaks.SerializeSnapshot(snapshot)))
                            {
                                Logger.Log("AMD Chill：快照无法持久化，本轮未启用");
                                return false;
                            }
                        }
                        int fps = Clamp(targetFps, range.Min, range.Max);
                        if (!AdlxApi.ChillSet(gpus[i], true, fps, fps)) { failed++; continue; }
                        bool vSupported, vEnabled;
                        int vMin, vMax;
                        AdlxIntRange vRange;
                        if (AdlxApi.ChillGet(gpus[i], out vSupported, out vEnabled, out vMin, out vMax, out vRange)
                            && vEnabled && vMax == fps) applied++;
                        else failed++;
                    }
                    if (applied > 0 && failed == 0)
                    {
                        Logger.Log("AMD Chill：帧率钉在 " + targetFps + " fps（驱动级，退出恢复）");
                        return true;
                    }
                    if (failed > 0) Logger.Log("AMD Chill：写入或回读核验失败 (" + failed + " 个 GPU)");
                    return false;
                }
                finally { AdlxApi.ReleaseAll(gpus); }
            }
        }

        public static bool RestoreChill()
        {
            lock (lk)
            {
                var snapshot = NvDrsTweaks.ParseSnapshot(Settings.LoadStr(SnapKey, ""));
                if (!HasPrefix(snapshot, ".chill")) return true;
                if (!Available) return false;
                IntPtr[] gpus = AdlxApi.GetGpus();
                if (gpus == null) return false;
                try
                {
                    string[] gpuKeys = GpuStableKeys(gpus);
                    bool allOk = true;
                    for (int i = 0; i < gpus.Length; i++)
                    {
                        string key = gpuKeys[i] + ".chill";
                        string legacyKey = "g" + i + ".chill";
                        string orig;
                        if (!TryGetSnapshotValue(snapshot, key, legacyKey, out orig)) continue;
                        string[] parts = orig.Split('|');
                        int minFps, maxFps;
                        if (parts.Length != 3
                            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minFps)
                            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out maxFps))
                        { RemoveSnapshotKey(snapshot, key, legacyKey); continue; }
                        bool wantOn = parts[0] == "1";
                        bool ok = wantOn
                            ? AdlxApi.ChillSet(gpus[i], true, minFps, maxFps)
                            : AdlxApi.ChillSet(gpus[i], false, 0, 0);
                        if (ok) RemoveSnapshotKey(snapshot, key, legacyKey);
                        else allOk = false;
                    }
                    LogOrphanKeys(snapshot, gpuKeys, ".chill", "Chill");
                    Settings.SaveStr(SnapKey, snapshot.Count == 0 ? "" : NvDrsTweaks.SerializeSnapshot(snapshot));
                    if (allOk) Logger.Log("AMD Chill 已还原");
                    else Logger.Log("AMD Chill 还原失败，快照保留，下次启动继续尝试");
                    return allOk;
                }
                finally { AdlxApi.ReleaseAll(gpus); }
            }
        }

        public static bool ActivateRis()
        {
            if (!Available) return false;
            lock (lk)
            {
                IntPtr[] gpus = AdlxApi.GetGpus();
                if (gpus == null || gpus.Length == 0) return false;
                try
                {
                    var snapshot = NvDrsTweaks.ParseSnapshot(Settings.LoadStr(SnapKey, ""));
                    string[] gpuKeys = GpuStableKeys(gpus);
                    int applied = 0, failed = 0;
                    for (int i = 0; i < gpus.Length; i++)
                    {
                        bool supported, enabled;
                        int sharpness;
                        AdlxIntRange range;
                        if (!AdlxApi.RisGet(gpus[i], out supported, out enabled, out sharpness, out range)
                            || !supported) continue;
                        string key = gpuKeys[i] + ".ris";
                        if (!snapshot.ContainsKey(key))
                        {
                            snapshot[key] = (enabled ? "1" : "0") + "|" + sharpness;
                            if (!Settings.SaveStr(SnapKey, NvDrsTweaks.SerializeSnapshot(snapshot)))
                            {
                                Logger.Log("AMD 锐化：快照无法持久化，本轮未启用");
                                return false;
                            }
                        }
                        int target = Clamp(RisSharpness, range.Min, range.Max);
                        if (!AdlxApi.RisSet(gpus[i], true, target)) { failed++; continue; }
                        bool vSupported, vEnabled;
                        int vSharp;
                        AdlxIntRange vRange;
                        if (AdlxApi.RisGet(gpus[i], out vSupported, out vEnabled, out vSharp, out vRange)
                            && vEnabled) applied++;
                        else failed++;
                    }
                    if (applied > 0 && failed == 0)
                    {
                        Logger.Log("AMD 锐化：RIS 已开启，强度 " + RisSharpness);
                        return true;
                    }
                    if (failed > 0) Logger.Log("AMD 锐化：写入或回读核验失败 (" + failed + " 个 GPU)");
                    return false;
                }
                finally { AdlxApi.ReleaseAll(gpus); }
            }
        }

        public static bool RestoreRis()
        {
            lock (lk)
            {
                var snapshot = NvDrsTweaks.ParseSnapshot(Settings.LoadStr(SnapKey, ""));
                if (!HasPrefix(snapshot, ".ris")) return true;
                if (!Available) return false;
                IntPtr[] gpus = AdlxApi.GetGpus();
                if (gpus == null) return false;
                try
                {
                    string[] gpuKeys = GpuStableKeys(gpus);
                    bool allOk = true;
                    for (int i = 0; i < gpus.Length; i++)
                    {
                        string key = gpuKeys[i] + ".ris";
                        string legacyKey = "g" + i + ".ris";
                        string orig;
                        if (!TryGetSnapshotValue(snapshot, key, legacyKey, out orig)) continue;
                        string[] parts = orig.Split('|');
                        int sharpness;
                        if (parts.Length != 2
                            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sharpness))
                        { RemoveSnapshotKey(snapshot, key, legacyKey); continue; }
                        bool ok = AdlxApi.RisSet(gpus[i], parts[0] == "1", sharpness);
                        if (ok) RemoveSnapshotKey(snapshot, key, legacyKey);
                        else allOk = false;
                    }
                    LogOrphanKeys(snapshot, gpuKeys, ".ris", "锐化");
                    Settings.SaveStr(SnapKey, snapshot.Count == 0 ? "" : NvDrsTweaks.SerializeSnapshot(snapshot));
                    if (allOk) Logger.Log("AMD 锐化已还原");
                    else Logger.Log("AMD 锐化还原失败，快照保留，下次启动继续尝试");
                    return allOk;
                }
                finally { AdlxApi.ReleaseAll(gpus); }
            }
        }

        private delegate bool FeatureGetter(IntPtr gpu, out bool supported, out bool enabled);
        private delegate bool FeatureApplier(IntPtr gpu);
        private delegate bool FeatureSetter(IntPtr gpu, bool on);

        private static bool ApplyToggle(string keySuffix, string label,
            FeatureGetter get, FeatureApplier apply, bool wantEnabled)
        {
            if (!Available) return false;
            lock (lk)
            {
                IntPtr[] gpus = AdlxApi.GetGpus();
                if (gpus == null || gpus.Length == 0) return false;
                try
                {
                    var snapshot = NvDrsTweaks.ParseSnapshot(Settings.LoadStr(SnapKey, ""));
                    string[] gpuKeys = GpuStableKeys(gpus);
                    int applied = 0, failed = 0;
                    for (int i = 0; i < gpus.Length; i++)
                    {
                        bool supported, enabled;
                        if (!get(gpus[i], out supported, out enabled) || !supported) continue;
                        if (enabled == wantEnabled) { applied++; continue; }
                        string key = gpuKeys[i] + "." + keySuffix;
                        if (!snapshot.ContainsKey(key))
                        {
                            snapshot[key] = enabled ? "1" : "0";
                            if (!Settings.SaveStr(SnapKey, NvDrsTweaks.SerializeSnapshot(snapshot)))
                            {
                                Logger.Log("AMD " + label + "：快照无法持久化，本轮未启用");
                                return false;
                            }
                        }
                        if (!apply(gpus[i])) { failed++; continue; }
                        bool vSupported, vEnabled;
                        if (get(gpus[i], out vSupported, out vEnabled) && vEnabled == wantEnabled) applied++;
                        else failed++;
                    }
                    if (applied > 0 && failed == 0)
                    {
                        Logger.Log("AMD " + label + "：已开启（驱动级，退出恢复）");
                        return true;
                    }
                    if (failed > 0) Logger.Log("AMD " + label + "：写入或回读核验失败 (" + failed + " 个 GPU)");
                    else if (applied == 0) Logger.Log("AMD " + label + "：本机 GPU 均不支持该功能");
                    return false;
                }
                finally { AdlxApi.ReleaseAll(gpus); }
            }
        }

        private static bool RestoreToggle(string keySuffix, string label, FeatureSetter set, FeatureGetter get)
        {
            lock (lk)
            {
                var snapshot = NvDrsTweaks.ParseSnapshot(Settings.LoadStr(SnapKey, ""));
                if (!HasPrefix(snapshot, "." + keySuffix)) return true;
                if (!Available) return false;
                IntPtr[] gpus = AdlxApi.GetGpus();
                if (gpus == null) return false;
                try
                {
                    string[] gpuKeys = GpuStableKeys(gpus);
                    bool allOk = true;
                    for (int i = 0; i < gpus.Length; i++)
                    {
                        string key = gpuKeys[i] + "." + keySuffix;
                        string legacyKey = "g" + i + "." + keySuffix;
                        string orig;
                        if (!TryGetSnapshotValue(snapshot, key, legacyKey, out orig)) continue;
                        if (set(gpus[i], orig == "1")) RemoveSnapshotKey(snapshot, key, legacyKey);
                        else allOk = false;
                    }
                    LogOrphanKeys(snapshot, gpuKeys, "." + keySuffix, label);
                    Settings.SaveStr(SnapKey, snapshot.Count == 0 ? "" : NvDrsTweaks.SerializeSnapshot(snapshot));
                    if (allOk) Logger.Log("AMD " + label + " 已还原");
                    else Logger.Log("AMD " + label + " 还原失败，快照保留，下次启动继续尝试");
                    return allOk;
                }
                finally { AdlxApi.ReleaseAll(gpus); }
            }
        }

        private static bool HasPrefix(Dictionary<string, string> snapshot, string suffix)
        {
            foreach (var kv in snapshot)
                if (kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // 快照键用 GPU 稳定身份（厂商:名称:类型 净化串），不用枚举序号——ADLX 枚举顺序
        // 在重启/驱动更新/iGPU 启用后会变，按序号记账会把 A 卡的快照还原到 B 卡。
        // 键内禁含 '=' 与 ';'（NvDrsTweaks 序列化分隔符），非字母数字一律替换为 '_'。
        private static string[] GpuStableKeys(IntPtr[] gpus)
        {
            var keys = new string[gpus.Length];
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < gpus.Length; i++)
            {
                string vendor = AdlxApi.GpuVendor(gpus[i]);
                string name = AdlxApi.GpuName(gpus[i]);
                string raw = (vendor == null ? "?" : vendor.Trim()) + ":"
                    + (name == null ? "?" : name.Trim()) + ":" + AdlxApi.GpuType(gpus[i]);
                var sb = new System.Text.StringBuilder(raw.Length);
                foreach (char c in raw)
                    sb.Append(char.IsLetterOrDigit(c) ? c : '_');
                string baseKey = sb.ToString();
                int n;
                seen.TryGetValue(baseKey, out n);
                seen[baseKey] = n + 1;
                // 同型号多卡追加组内序号：同型号共享同一份快照值，还原语义不变
                keys[i] = n == 0 ? baseKey : baseKey + "_" + n;
            }
            return keys;
        }

        // 还原查找：先按稳定身份键，缺省时退回旧版「g<序号>.」键（升级前崩溃残留的
        // 会话快照仍可还原）。命中即视为已处理，两个键都清除。
        private static bool TryGetSnapshotValue(Dictionary<string, string> snapshot,
            string stableKey, string legacyKey, out string value)
        {
            if (snapshot.TryGetValue(stableKey, out value)) return true;
            return snapshot.TryGetValue(legacyKey, out value);
        }

        private static void RemoveSnapshotKey(Dictionary<string, string> snapshot,
            string stableKey, string legacyKey)
        {
            snapshot.Remove(stableKey);
            snapshot.Remove(legacyKey);
        }

        // 还原后仍残留的该功能键 = 对应 GPU 当前不在线（拔卡/禁用）或显示名已改。
        // 保留快照待其回归，逐键列出便于诊断；不计失败（无还原目标时删快照也于事无补）。
        private static void LogOrphanKeys(Dictionary<string, string> snapshot,
            string[] currentKeys, string suffix, string label)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in currentKeys) known.Add(k + suffix);
            for (int i = 0; i < 8; i++) known.Add("g" + i + suffix);   // 旧版序号键的合法形态
            var orphans = new List<string>();
            foreach (var kv in snapshot)
                if (kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && !known.Contains(kv.Key))
                    orphans.Add(kv.Key);
            if (orphans.Count > 0)
                Logger.Log("AMD " + label + "：" + orphans.Count + " 条快照对应的 GPU 当前不在线或已改名（"
                    + string.Join("、", orphans.ToArray()) + "），快照保留待其回归");
        }

        private static int Clamp(int value, int min, int max)
        {
            if (max > 0 && value > max) return max;
            if (min > 0 && value < min) return min;
            return value;
        }

        public static bool ResetShaderCacheAll(out int done)
        {
            done = 0;
            if (!Available) return false;
            IntPtr[] gpus = AdlxApi.GetGpus();
            if (gpus == null || gpus.Length == 0) return false;
            try
            {
                foreach (IntPtr gpu in gpus)
                    if (AdlxApi.ResetShaderCache(gpu)) done++;
                Logger.Log("AMD 着色器缓存重置：" + done + "/" + gpus.Length + " 个 GPU 已执行");
                return done > 0;
            }
            finally { AdlxApi.ReleaseAll(gpus); }
        }

        public static void HealFromCrash()
        {
            if (Settings.LoadStr(SnapKey, "").Length == 0) return;
            bool ok = RestoreAntiLag() & RestoreEnhancedSync() & RestoreChill() & RestoreRis();
            if (ok) Logger.Log("检测到上次未还原的 AMD 驱动设置，已恢复");
        }

        public sealed class ProbeRow
        {
            public string Name;
            public string Outcome;
            public bool Ok;
        }

        public static List<ProbeRow> ProbeWriteback()
        {
            var rows = new List<ProbeRow>();
            if (!Available) return rows;
            IntPtr[] gpus = AdlxApi.GetGpus();
            if (gpus == null || gpus.Length == 0) return rows;
            try
            {
                for (int i = 0; i < gpus.Length; i++)
                {
                    string name = AdlxApi.GpuName(gpus[i]);
                    bool supported, enabled;
                    if (AdlxApi.AntiLagGet(gpus[i], out supported, out enabled) && supported)
                    {
                        bool wrote = AdlxApi.AntiLagSet(gpus[i], !enabled);
                        bool vSupported, vEnabled;
                        bool verified = wrote && AdlxApi.AntiLagGet(gpus[i], out vSupported, out vEnabled)
                            && vEnabled == !enabled;
                        bool restored = AdlxApi.AntiLagSet(gpus[i], enabled);
                        rows.Add(new ProbeRow
                        {
                            Name = (name ?? ("GPU" + i)) + " Anti-Lag",
                            Ok = verified && restored,
                            Outcome = !wrote ? "写入被拒绝"
                                : !verified ? "写入报成功但回读不符"
                                : !restored ? "生效但原值恢复失败"
                                : "写入与还原均生效"
                        });
                    }
                    else
                        rows.Add(new ProbeRow
                        {
                            Name = (name ?? ("GPU" + i)) + " Anti-Lag",
                            Ok = false,
                            Outcome = "不支持或读取失败"
                        });

                    bool cSupported, cEnabled;
                    int cMin, cMax;
                    AdlxIntRange cRange;
                    if (AdlxApi.ChillGet(gpus[i], out cSupported, out cEnabled, out cMin, out cMax, out cRange))
                        rows.Add(new ProbeRow
                        {
                            Name = (name ?? ("GPU" + i)) + " Chill",
                            Ok = cSupported,
                            Outcome = cSupported
                                ? "支持，范围 " + cRange.Min + "-" + cRange.Max + " fps"
                                : "不支持"
                        });
                }
                return rows;
            }
            finally { AdlxApi.ReleaseAll(gpus); }
        }
    }
}
