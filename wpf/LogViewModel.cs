// @author zenjiro 18967498922@163.com
// 文件用途 WPF 日志页 ViewModel：拉取运行日志尾部

namespace CaelusApp
{
    internal sealed class LogViewModel : ViewModelBase
    {
        private string logText = "";
        public string LogText { get { return logText; } private set { SetProperty(ref logText, value, "LogText"); } }
        public string OpenLogText { get { return Lang.T("btn.openlog"); } }
        public string ClearLogText { get { return Lang.T("rep.clear.log"); } }

        public void Refresh()
        {
            string text = Logger.Tail(220);
            if (string.IsNullOrEmpty(text)) text = Lang.T("rep.log.none");
            LogText = text;
        }
    }
}
