// @author zenjiro 18967498922@163.com
// 文件用途 新 UI 的动效 Token 与减少动态效果策略

namespace CaelusApp
{
    internal static class UiMotion
    {
        public const int ButtonPressMs = 90;
        public const int ToggleMs = 150;
        public const int SegmentMs = 180;
        public const int PageFadeMs = 180;
        public const int ModeChangeMs = 220;
        public const int SuccessPopMs = 260;
        public const int ModalMs = 180;

        // Compatibility aliases retained for older call sites.
        public const int CardExpandMs = 220;
        public const int NumberRollMs = 220;
        public const int ReducedFadeMs = 90;

        public static int Duration(int baseMs, bool reduced)
        {
            return reduced ? ReducedFadeMs : baseMs;
        }

        public static bool AllowsOffset(bool reduced)
        {
            return !reduced;
        }

        public static bool AllowsScale(bool reduced)
        {
            return !reduced;
        }

        // 弹簧三档预设（规格 2026-09-29 §3.1）：net4 无原生 SpringAnimation，以 BackEase 参数表近似
        public enum SpringPreset { Gentle, Snappy, Bouncy }

        // Gentle=页面/卡片入场 300ms/0.3；Snappy=指示器/开关 240ms/0.45；Bouncy=按压释放 180ms/0.5
        public static void SpringParams(SpringPreset preset, out int milliseconds, out double amplitude)
        {
            switch (preset)
            {
                case SpringPreset.Snappy: milliseconds = 240; amplitude = 0.45; return;
                case SpringPreset.Bouncy: milliseconds = 180; amplitude = 0.5; return;
                default: milliseconds = 300; amplitude = 0.3; return; // Gentle
            }
        }
    }
}
