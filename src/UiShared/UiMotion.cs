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
    }
}
