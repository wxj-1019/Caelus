// @author zenjiro 18967498922@163.com
// 文件用途 识别本机已安装的内核态反作弊 只用于把日志说清楚和提前预置 IFEO
// 判定游戏能否被提优一律以句柄 granted access 的实测为准 这里的目录不参与该判定

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class KernelAntiCheat
    {
        private sealed class Sig
        {
            public string Name;
            public string[] Services;
            /// <summary>服务名是通用词时（如 Javelin）加验 ImagePath 含此 token，防误报。</summary>
            public string ImagePathToken;
        }

        private static readonly Sig[] Known = new[]
        {
            new Sig { Name = "Vanguard",        Services = new[]{ "vgk", "vgc" } },
            new Sig { Name = "EasyAntiCheat",   Services = new[]{ "EasyAntiCheat", "EasyAntiCheat_EOS" } },
            new Sig { Name = "BattlEye",        Services = new[]{ "BEDaisy", "BEService" } },
            new Sig { Name = "ACE-Guard",       Services = new[]{ "ACE-BASE", "ACE-GAME", "AntiCheatExpert" } },
            new Sig { Name = "nProtect GameGuard", Services = new[]{ "npggsvc" } },
            new Sig { Name = "Faceit AC",       Services = new[]{ "faceit" } },
            // 米哈游内核反作弊（原神/星穹铁道/绝区零国服大盘）
            new Sig { Name = "HoYoKProtect",    Services = new[]{ "HoYoKProtect", "mhyprot2", "mhyprot3" } },
            new Sig { Name = "EA Javelin",      Services = new[]{ "Javelin" }, ImagePathToken = "javelin" },
            new Sig { Name = "Xigncode3",       Services = new[]{ "xhunter1" } },
        };

        private static readonly string[][] ByExePrefix = new[]
        {
            new[]{ "Ricochet", "cod", "modernwarfare", "blackops", "warzone" },
        };

        private const string ServiceRoot = @"SYSTEM\CurrentControlSet\Services";

        private static readonly object lk = new object();
        private static string cached;
        private static bool probed;

        internal static bool ServiceExists(string name)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(ServiceRoot + "\\" + name))
                    return k != null;
            }
            catch { return false; }
        }

        /// <summary>服务 ImagePath 是否包含指定 token（通用词服务名的第二特征校验）。
        /// 读不到 ImagePath 时返回 false（宁可漏报也不把无关软件认成内核反作弊）。</summary>
        internal static bool ServiceImagePathContains(string serviceName, string token)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(ServiceRoot + "\\" + serviceName))
                {
                    if (k == null) return false;
                    string path = k.GetValue("ImagePath") as string;
                    return !string.IsNullOrEmpty(path)
                        && path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        public static string InstalledName()
        {
            lock (lk)
            {
                if (probed) return cached;
                probed = true;
                var hits = new List<string>();
                foreach (Sig s in Known)
                    foreach (string svc in s.Services)
                    {
                        if (!ServiceExists(svc)) continue;
                        if (s.ImagePathToken != null
                            && !ServiceImagePathContains(svc, s.ImagePathToken)) continue;
                        hits.Add(s.Name);
                        break;
                    }
                cached = hits.Count == 0 ? null : string.Join(" / ", hits.ToArray());
                return cached;
            }
        }

        internal static string MatchByExe(string rendererName)
        {
            if (string.IsNullOrEmpty(rendererName)) return null;
            string exe = rendererName;
            int slash = exe.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) exe = exe.Substring(slash + 1);
            if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                exe = exe.Substring(0, exe.Length - 4);
            foreach (string[] row in ByExePrefix)
                for (int i = 1; i < row.Length; i++)
                    if (exe.StartsWith(row[i], StringComparison.OrdinalIgnoreCase)) return row[0];
            return null;
        }

        public static string Describe(string rendererName)
        {
            string byExe = MatchByExe(rendererName);
            if (byExe != null) return byExe;
            return InstalledName();
        }

#if CAELUS_SELFTEST
        internal static void ResetProbeForTest() { lock (lk) { probed = false; cached = null; } }
#endif
    }
}
