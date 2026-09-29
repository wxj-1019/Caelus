// @author zenjiro 18967498922@163.com
// 文件用途 动效纯逻辑自测：弹簧预设映射 / 插值边界 / NumberRoll 禁用直出（规格 2026-09-29 §3）

using System;
using System.Windows.Controls;
using CaelusApp.WpfHost;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestSpringPresetMap()
        {
            int ms; double amp;
            UiMotion.SpringParams(UiMotion.SpringPreset.Gentle, out ms, out amp);
            Eq(300, ms); Eq(0.3, amp);
            UiMotion.SpringParams(UiMotion.SpringPreset.Snappy, out ms, out amp);
            Eq(240, ms); Eq(0.45, amp);
            UiMotion.SpringParams(UiMotion.SpringPreset.Bouncy, out ms, out amp);
            Eq(180, ms); Eq(0.5, amp);
        }

        private static void TestMotionInterpolate()
        {
            Eq(10d, Motion.Interpolate(10, 20, 0));
            Eq(20d, Motion.Interpolate(10, 20, 1));
            Eq(15d, Motion.Interpolate(10, 20, 0.5));
        }

        private static void TestNumberRollDisabledSetsFinal()
        {
            var tb = new TextBlock();
            bool prev = Motion.Enabled;
            try
            {
                Motion.Enabled = false;
                Motion.NumberRoll(tb, 0, 87, "0");
                Eq("87", tb.Text);
            }
            finally { Motion.Enabled = prev; }
        }
    }
}
