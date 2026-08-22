// @author zenjiro 18967498922@163.com
// 文件用途 消息弹窗纯逻辑自测：级别映射 / 按钮组解析 / 标题正文拆分（规格 2026-08-23 §7）

using System.Windows;
using CaelusApp.WpfHost.Dialogs;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestMsgDialogSeverityMaps()
        {
            Eq("IconInfo", MsgDialogMaps.IconKey(MsgSeverity.Info));
            Eq("IconCheck", MsgDialogMaps.IconKey(MsgSeverity.Success));
            Eq("IconWarn", MsgDialogMaps.IconKey(MsgSeverity.Warning));
            Eq("IconError", MsgDialogMaps.IconKey(MsgSeverity.Danger));
            Eq("InfoBrush", MsgDialogMaps.BrushKey(MsgSeverity.Info));
            Eq("SuccessBrush", MsgDialogMaps.BrushKey(MsgSeverity.Success));
            Eq("DangerEdgeBrush", MsgDialogMaps.EdgeKey(MsgSeverity.Danger));
            Eq("WarningSoftBrush", MsgDialogMaps.SoftKey(MsgSeverity.Warning));
            Eq("PrimaryButton", MsgDialogMaps.PrimaryStyleKey(MsgSeverity.Warning));
            Eq("PrimaryButton", MsgDialogMaps.PrimaryStyleKey(MsgSeverity.Success));
            Eq("DangerButton", MsgDialogMaps.PrimaryStyleKey(MsgSeverity.Danger));
        }

        private static void TestMsgDialogButtonSets()
        {
            MsgButtonSet ok = MsgDialogMaps.ResolveButtons(MsgButtons.Ok, null);
            Eq<bool>(false, ok.HasSecondary);
            Eq("知道了", ok.PrimaryText);
            Eq(MessageBoxResult.OK, ok.PrimaryResult);
            Eq(MessageBoxResult.OK, ok.EscResult);

            MsgButtonSet named = MsgDialogMaps.ResolveButtons(MsgButtons.Ok, "好");
            Eq("好", named.PrimaryText);

            MsgButtonSet okCancel = MsgDialogMaps.ResolveButtons(MsgButtons.OkCancel, "全部取消");
            Eq<bool>(true, okCancel.HasSecondary);
            Eq("全部取消", okCancel.PrimaryText);
            Eq("取消", okCancel.SecondaryText);
            Eq(MessageBoxResult.OK, okCancel.PrimaryResult);
            Eq(MessageBoxResult.Cancel, okCancel.SecondaryResult);
            Eq(MessageBoxResult.Cancel, okCancel.EscResult);

            MsgButtonSet yesNo = MsgDialogMaps.ResolveButtons(MsgButtons.YesNo, null);
            Eq<bool>(true, yesNo.HasSecondary);
            Eq("是", yesNo.PrimaryText);
            Eq("否", yesNo.SecondaryText);
            Eq(MessageBoxResult.Yes, yesNo.PrimaryResult);
            Eq(MessageBoxResult.No, yesNo.SecondaryResult);
            Eq(MessageBoxResult.No, yesNo.EscResult);
        }

        private static void TestMsgDialogSplitTitleBody()
        {
            // 标准两段式：\r\n\r\n 拆分 + 去定前缀 + 去吗字尾
            string[] r = MsgDialogMaps.SplitTitleBody(
                "确定取消全部 3 个 Defender 排除吗？\r\n\r\n手工添加的排除不会被修改。");
            Eq("取消全部 3 个 Defender 排除？", r[0]);
            Eq("手工添加的排除不会被修改。", r[1]);

            // \n\n 分隔 + 多段正文合并
            string[] r2 = MsgDialogMaps.SplitTitleBody("A\n\nB\n\nC");
            Eq("A", r2[0]);
            Eq("B\n\nC", r2[1]);

            // 无空行：整句为标题、正文为空
            string[] r3 = MsgDialogMaps.SplitTitleBody("这是系统必需的内置项，不能删。");
            Eq("这是系统必需的内置项，不能删。", r3[0]);
            Eq("", r3[1]);

            // 超长无句末标点：硬截 24 字加省略号
            string long32 = "一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二";
            string[] r4 = MsgDialogMaps.SplitTitleBody(long32);
            Eq<bool>(true, r4[0].Length == 25);
            Eq<bool>(true, r4[0].EndsWith("…"));

            // 超长含句末标点：截到最后一个句末标点（保留至少 8 字）
            string longP = "第一句要表达的意思完整。第二句继续补充一些内容再补充一些内容。";
            string[] r5 = MsgDialogMaps.SplitTitleBody(longP);
            Eq("第一句要表达的意思完整。", r5[0]);

            // null 安全
            string[] r6 = MsgDialogMaps.SplitTitleBody(null);
            Eq("", r6[0]);
            Eq("", r6[1]);

            // 拆分后尾段仅重复提问（继续吗？）时移除
            string[] r7 = MsgDialogMaps.SplitTitleBody("关闭后台冻结\r\n\r\n说明文字。\r\n\r\n继续吗？");
            Eq("关闭后台冻结", r7[0]);
            Eq("说明文字。", r7[1]);
        }
    }
}
