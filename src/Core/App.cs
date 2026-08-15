// @author zenjiro 18967498922@163.com
// 文件用途 应用级常量（显示名/版本/仓库地址）。独立成文件以便 WinForms 与
// WPF 两个宿主共享同一份来源（Program.cs 仅含 WinForms 启动逻辑）。

namespace CaelusApp
{
    internal static class App
    {
        public const string DisplayName = "CAELUS";
        public const string Version = "1.8.0";
        public const string Author = "zenjiro";
        public const string AuthorEmail = "18967498922@163.com";
        public const string WeChat = "";
        public const string RepoName = "wxj-1019/Caelus";
        public const string RepoUrl = "https://github.com/" + RepoName;
        public const string ReleasesUrl = RepoUrl + "/releases";
        public static string VersionTag { get { return "v" + Version; } }
    }
}
