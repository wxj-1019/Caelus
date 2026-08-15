// @author zenjiro 18967498922@163.com
// 文件用途 开发环境体检：只读检测开发工具链版本与 Windows 开发者模式（不修改任何设置）

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace CaelusApp
{
    internal static class DevEnvAudit
    {
        internal sealed class DevEnvItem
        {
            public readonly string Name;
            public readonly string Detail;
            public readonly bool Found;

            public DevEnvItem(string name, string detail, bool found)
            {
                Name = name;
                Detail = detail;
                Found = found;
            }
        }

        /// <summary>只读检测：工具链版本 + 开发者模式。固定参数数组，无用户输入。</summary>
        public static List<DevEnvItem> Run()
        {
            var list = new List<DevEnvItem>();
            list.Add(Probe("dotnet", "--version"));
            list.Add(Probe("node", "--version"));
            list.Add(Probe("npm", "--version"));
            list.Add(Probe("git", "--version"));
            list.Add(Probe("python", "--version"));
            list.Add(Probe("java", "-version"));
            list.Add(Probe("cargo", "--version"));
            list.Add(Probe("go", "version"));
            list.Add(DevMode());
            return list;
        }

        /// <summary>解析版本：stdout/stderr 中首个非空行（python/java 的版本常走 stderr）。纯逻辑可单测。</summary>
        internal static string ParseVersion(string stdout, string stderr)
        {
            return FirstLine(stdout) ?? FirstLine(stderr) ?? "";
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Length > 0) return t;
            }
            return null;
        }

        private static DevEnvItem Probe(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return Missing(exe);
                    if (!p.WaitForExit(3000))
                    {
                        try { p.Kill(); } catch { }
                        return new DevEnvItem(exe, "(超时)", false);
                    }
                    p.WaitForExit();
                    string version = ParseVersion(p.StandardOutput.ReadToEnd(), p.StandardError.ReadToEnd());
                    if (version.Length == 0) return new DevEnvItem(exe, "(无输出)", false);
                    return new DevEnvItem(exe, version, true);
                }
            }
            catch { return Missing(exe); }
        }

        private static DevEnvItem Missing(string exe)
        {
            return new DevEnvItem(exe, "(未安装)", false);
        }

        private static DevEnvItem DevMode()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AllowDevelopmentWithoutDevLicense");
                        if (v != null && Convert.ToInt32(v) == 1)
                            return new DevEnvItem("开发者模式", "已开启", true);
                    }
                }
            }
            catch { }
            return new DevEnvItem("开发者模式", "未开启", false);
        }
    }
}
