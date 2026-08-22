// @author zenjiro 18967498922@163.com
// 文件用途 消息弹窗纯逻辑：级别/按钮映射 + 单字符串标题正文拆分（规格 2026-08-23 §2/§4）

using System;
using System.Windows;

namespace CaelusApp.WpfHost.Dialogs
{
    internal enum MsgSeverity { Info, Success, Warning, Danger }
    internal enum MsgButtons { Ok, OkCancel, YesNo }

    internal sealed class MsgButtonSet
    {
        internal string PrimaryText;
        internal string SecondaryText;
        internal bool HasSecondary;
        internal MessageBoxResult PrimaryResult;
        internal MessageBoxResult SecondaryResult;
        internal MessageBoxResult EscResult;
    }

    internal static class MsgDialogMaps
    {
        internal static string IconKey(MsgSeverity s)
        {
            switch (s)
            {
                case MsgSeverity.Success: return "IconCheck";
                case MsgSeverity.Warning: return "IconWarn";
                case MsgSeverity.Danger: return "IconError";
                default: return "IconInfo";
            }
        }

        internal static string BrushKey(MsgSeverity s) { return s.ToString() + "Brush"; }
        internal static string EdgeKey(MsgSeverity s) { return s.ToString() + "EdgeBrush"; }
        internal static string SoftKey(MsgSeverity s) { return s.ToString() + "SoftBrush"; }

        internal static string PrimaryStyleKey(MsgSeverity s)
        {
            return s == MsgSeverity.Danger ? "DangerButton" : "PrimaryButton";
        }

        internal static MsgButtonSet ResolveButtons(MsgButtons buttons, string okText)
        {
            MsgButtonSet set = new MsgButtonSet();
            if (buttons == MsgButtons.Ok)
            {
                set.PrimaryText = string.IsNullOrEmpty(okText) ? "知道了" : okText;
                set.PrimaryResult = MessageBoxResult.OK;
                set.EscResult = MessageBoxResult.OK;
                return set;
            }
            if (buttons == MsgButtons.OkCancel)
            {
                set.PrimaryText = string.IsNullOrEmpty(okText) ? "确定" : okText;
                set.SecondaryText = "取消";
                set.HasSecondary = true;
                set.PrimaryResult = MessageBoxResult.OK;
                set.SecondaryResult = MessageBoxResult.Cancel;
                set.EscResult = MessageBoxResult.Cancel;
                return set;
            }
            set.PrimaryText = string.IsNullOrEmpty(okText) ? "是" : okText;
            set.SecondaryText = "否";
            set.HasSecondary = true;
            set.PrimaryResult = MessageBoxResult.Yes;
            set.SecondaryResult = MessageBoxResult.No;
            set.EscResult = MessageBoxResult.No;
            return set;
        }

        // 单字符串 → {标题, 正文}：首个空行（\r\n\r\n 或 \n\n）拆分；无空行则全部为标题。
        internal static string[] SplitTitleBody(string message)
        {
            if (message == null) message = "";
            string title = message;
            string body = "";
            int i = message.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            int j = message.IndexOf("\n\n", StringComparison.Ordinal);
            int cut = -1, cutLen = 0;
            if (i >= 0 && (j < 0 || i <= j)) { cut = i; cutLen = 4; }
            else if (j >= 0) { cut = j; cutLen = 2; }
            if (cut >= 0)
            {
                title = message.Substring(0, cut);
                body = message.Substring(cut + cutLen).Trim();
            }
            return new string[] { NormalizeTitle(title), body };
        }

        // 标题规整：去“确定”前缀；“吗？”→“？”；超 24 字截断（句末标点优先，至少留 8 字）
        internal static string NormalizeTitle(string title)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0) return "";
            if (title.StartsWith("确定", StringComparison.Ordinal) && title.Length > 3)
                title = title.Substring(2);
            if (title.EndsWith("吗？", StringComparison.Ordinal))
                title = title.Substring(0, title.Length - 2) + "？";
            else if (title.EndsWith("吗?", StringComparison.Ordinal))
                title = title.Substring(0, title.Length - 2) + "?";
            if (title.Length <= 24) return title;
            string puncts = "。！？!?)）";
            int last = -1;
            for (int k = 23; k >= 8; k--)
            {
                if (puncts.IndexOf(title[k]) >= 0) { last = k; break; }
            }
            if (last >= 8) return title.Substring(0, last + 1);
            return title.Substring(0, 24) + "…";
        }
    }
}
