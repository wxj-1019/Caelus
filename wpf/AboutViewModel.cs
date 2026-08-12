// @author zenjiro 18967498922@163.com
// 文件用途 WPF 关于页 ViewModel：项目身份、诊断信息与更新状态

namespace CaelusApp
{
    internal sealed class AboutViewModel : ViewModelBase
    {
        private string updateStatus = "";
        private string updateKind = "Info";
        private string downloadUrl = "";
        private bool updateAvailable;

        public string VersionText { get { return App.VersionTag; } }
        public string AuthorText { get { return App.Author + " <" + App.AuthorEmail + ">"; } }
        public string RepoText { get { return App.RepoUrl; } }
        public string RepoDisplayText { get { return App.RepoUrl.Replace("https://", ""); } }
        public string ReleaseUrl { get { return App.ReleasesUrl; } }
        public string LicenseText { get { return "MIT License"; } }
        public string DiagnosticsText { get { return "数据目录: " + (Paths.Data ?? "未初始化"); } }
        public string UpdateTitle { get { return Lang.T("v15.about.update"); } }
        public string UpdateSubText { get { return Lang.T("v15.about.update.sub"); } }
        public string CheckUpdateText { get { return Lang.T("btn.checkupd"); } }
        public string DownloadText { get { return "前往下载"; } }
        public string UpdateStatus { get { return updateStatus; } set { SetProperty(ref updateStatus, value, "UpdateStatus"); } }
        public string UpdateKind { get { return updateKind; } set { SetProperty(ref updateKind, value, "UpdateKind"); } }
        public string DownloadUrl { get { return downloadUrl; } private set { SetProperty(ref downloadUrl, value, "DownloadUrl"); } }
        public bool UpdateAvailable { get { return updateAvailable; } private set { SetProperty(ref updateAvailable, value, "UpdateAvailable"); } }

        public void SetUpdateResult(UpdateResult result)
        {
            DownloadUrl = result == null ? "" : (result.Url ?? "");
            UpdateAvailable = result != null && result.Ok && result.Newer && DownloadUrl.Length > 0;
        }
    }
}
