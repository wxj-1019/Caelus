// @author zenjiro 18967498922@163.com
// 文件用途 WPF 发布说明对话框：展示内置的三语版本说明并标记已读（与 WinForms 版行为一致）

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace CaelusApp.WpfHost.Dialogs
{
    internal sealed class ReleaseNoteRow
    {
        public string VersionTag;
        public string DateText;
        public bool IsCurrent;
        public List<string> Items;
    }

    internal partial class ReleaseNotesDialogWpf : Window
    {
        public ReleaseNotesDialogWpf()
        {
            InitializeComponent();
            LblTitle.Text = Lang.T("notes.title");
            LblSubtitle.Text = Lang.F("notes.sub", CaelusApp.App.VersionTag);
            BtnOnline.Content = Lang.T("notes.online");

            var rows = new List<ReleaseNoteRow>();
            foreach (ReleaseNote rn in ReleaseNotes.All)
            {
                var row = new ReleaseNoteRow();
                row.VersionTag = rn.Tag;
                row.DateText = rn.Date;
                row.IsCurrent = string.Equals(rn.Version, CaelusApp.App.Version, StringComparison.OrdinalIgnoreCase);
                row.Items = new List<string>();
                for (int i = 0; i < rn.Count; i++)
                {
                    string item = rn.Item(i);
                    if (!string.IsNullOrEmpty(item)) row.Items.Add(item);
                }
                rows.Add(row);
            }
            NotesHost.ItemsSource = rows;

            // 与 WinForms 版一致：显示即标记已读
            ReleaseNotes.MarkSeen();
            Loaded += delegate { Motion.RiseIn(NotesHost, 60); };
        }

        private void OnOnline(object sender, RoutedEventArgs e)
        {
            try
            {
                Uri uri;
                if (Uri.TryCreate(CaelusApp.App.ReleasesUrl, UriKind.Absolute, out uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                    Process.Start(uri.AbsoluteUri);
            }
            catch { }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }
    }
}
