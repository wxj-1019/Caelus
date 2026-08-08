// @author zenjiro 18967498922@163.com
// 文件用途 识别编译/调试/构建进程，作为开发模式的触发信号

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class BuildCatalog
    {
        // 明确的编译/构建/调试工具进程名（不含运行时如 node/java/dotnet，避免误触发）
        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // .NET
            "msbuild", "csc", "vbc", "fsc", "roslyn", "msbuildsdp", "dotnet-build",
            // JVM
            "javac", "gradle", "mvn", "sbt", "kotlinc", "kotlin-compiler",
            // C/C++/Rust
            "gcc", "g++", "clang", "clang++", "cmake", "make", "ninja", "ld", "lld", "lld-link", "rustc", "cargo",
            // Node/前端
            "tsc", "webpack", "vite", "esbuild", "rollup", "gulp", "babel", "swc", "parcel", "turbo",
            // 调试器
            "gdb", "lldb", "msvsmon",
            // Git 大规模 IO 操作（rebase/gc/pack/clone）
            "git", "git-bash", "git-cmd", "hub", "gh",
            // Docker（build/run 都触发；守护进程 dockerd 在豁免列表不受影响）
            "docker", "docker-buildx",
            // 测试运行器
            "nunit3-console", "vstest.console", "pytest", "jest", "mocha", "go-test"
        };

        private const string CustomKey = "CustomBuildProcs";
        private static readonly object CustomLock = new object();
        private static HashSet<string> customNames;

        // 自定义编译进程名（分号/换行分隔），存注册表，设置页可编辑
        public static string CustomList
        {
            get { return Settings.LoadStr(CustomKey, ""); }
            set { Settings.SaveStr(CustomKey, value ?? ""); lock (CustomLock) customNames = null; }
        }

        public static bool IsMatch(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            string bare = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4) : processName;
            if (Names.Contains(bare)) return true;
            HashSet<string> custom = LoadCustom();
            return custom != null && custom.Contains(bare);
        }

        private static HashSet<string> LoadCustom()
        {
            lock (CustomLock)
            {
                if (customNames != null) return customNames;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = Settings.LoadStr(CustomKey, "");
                if (raw != null)
                    foreach (string part in raw.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = part.Trim();
                        if (t.Length > 0) set.Add(t);
                    }
                customNames = set;
                return set;
            }
        }
    }
}
