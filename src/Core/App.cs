// @author zenjiro 18967498922@163.com
// 文件用途 应用级常量（显示名/版本/仓库地址）与版本比较。独立成文件以便 WinForms 与
// WPF 两个宿主共享同一份来源（Program.cs 仅含 WinForms 启动逻辑）。

using System;

namespace CaelusApp
{
    internal static class App
    {
        public const string DisplayName = "CAELUS";
        public const string Version = "1.9.0";
        public const string Author = "zenjiro";
        public const string AuthorEmail = "18967498922@163.com";
        public const string WeChat = "";
        public const string RepoName = "wxj-1019/Caelus";
        public const string RepoUrl = "https://github.com/" + RepoName;
        public const string ReleasesUrl = RepoUrl + "/releases";
        public static string VersionTag { get { return "v" + Version; } }

        // 版本比较（新实例接管旧实例时用）：容忍 "v" 前缀与缺段
        public static int CompareVersions(string left, string right)
        {
            System.Version a, b;
            if (!System.Version.TryParse(NormalizeVersion(left), out a)) a = new System.Version(0, 0);
            if (!System.Version.TryParse(NormalizeVersion(right), out b)) b = new System.Version(0, 0);
            return a.CompareTo(b);
        }

        private static string NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "0.0.0.0";
            string text = raw.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            int parts = text.Split('.').Length;
            for (int i = parts; i < 4; i++) text += ".0";
            return text;
        }
    }
}
