// @author zenjiro 18967498922@163.com
// 文件用途 WPF 关于页 ViewModel：项目身份 + 更新状态

namespace CaelusApp
{
    internal sealed class AboutViewModel : ViewModelBase
    {
        private string updateStatus = "";
        public string VersionText { get { return App.VersionTag; } }
        public string AuthorText { get { return App.Author + " <" + App.AuthorEmail + ">"; } }
        public string RepoText { get { return App.RepoUrl; } }
        public string UpdateTitle { get { return Lang.T("v15.about.update"); } }
        public string UpdateSubText { get { return Lang.T("v15.about.update.sub"); } }
        public string CheckUpdateText { get { return Lang.T("btn.checkupd"); } }
        public string UpdateStatus { get { return updateStatus; } set { SetProperty(ref updateStatus, value, "UpdateStatus"); } }
    }
}
