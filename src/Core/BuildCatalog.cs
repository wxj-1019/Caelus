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
            "gdb", "lldb", "msvsmon"
        };

        public static bool IsMatch(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            string bare = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4) : processName;
            return Names.Contains(bare);
        }
    }
}
