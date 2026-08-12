// @author zenjiro 18967498922@163.com
// 文件用途 WPF 日志页 ViewModel：拉取、筛选日志尾部并提供摘要与反馈

using System;

namespace CaelusApp
{
    internal sealed class LogViewModel : ViewModelBase
    {
        private string logText = "";
        private string allLogText = "";
        private string filterText = "";
        private string summaryText = "";
        private string feedbackText = "";
        private string feedbackKind = "Info";

        public string LogText { get { return logText; } private set { SetProperty(ref logText, value, "LogText"); } }
        public string OpenLogText { get { return Lang.T("btn.openlog"); } }
        public string ClearLogText { get { return Lang.T("rep.clear.log"); } }
        public string RefreshText { get { return "刷新"; } }
        public string FilterHint { get { return "筛选日志"; } }
        public string EmptyTitle { get { return "暂时没有运行日志"; } }
        public string EmptyHint { get { return "新的运行记录会自动显示在这里。"; } }
        public string SummaryText { get { return summaryText; } private set { SetProperty(ref summaryText, value, "SummaryText"); } }
        public string FeedbackText { get { return feedbackText; } set { SetProperty(ref feedbackText, value, "FeedbackText"); } }
        public string FeedbackKind { get { return feedbackKind; } set { SetProperty(ref feedbackKind, value, "FeedbackKind"); } }
        public bool HasLog { get { return !string.IsNullOrEmpty(allLogText); } }
        public bool HasVisibleLog { get { return !string.IsNullOrEmpty(logText); } }
        public bool CanClear { get { return HasLog; } }

        public string FilterText
        {
            get { return filterText; }
            set
            {
                if (!SetProperty(ref filterText, value ?? "", "FilterText")) return;
                ApplyFilter();
            }
        }

        public void Refresh()
        {
            allLogText = Logger.Tail(220) ?? "";
            ApplyFilter();
            Raise("HasLog");
            Raise("CanClear");
        }

        public void ShowFeedback(string text, string kind)
        {
            FeedbackKind = string.IsNullOrEmpty(kind) ? "Info" : kind;
            FeedbackText = text ?? "";
        }

        private void ApplyFilter()
        {
            string visible = allLogText;
            string filter = filterText.Trim();
            if (filter.Length > 0 && visible.Length > 0)
            {
                string[] lines = visible.Replace("\r\n", "\n").Split('\n');
                visible = "";
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                    if (visible.Length > 0) visible += Environment.NewLine;
                    visible += lines[i];
                }
            }
            LogText = visible;
            SummaryText = allLogText.Length == 0 ? "尚无记录"
                : (filter.Length == 0 ? CountLines(allLogText) + " 条记录" : "已筛选 " + CountLines(visible) + " 条记录");
            Raise("HasVisibleLog");
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') count++;
            return count;
        }
    }
}
