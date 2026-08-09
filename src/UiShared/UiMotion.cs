// @author zenjiro 18967498922@163.com
// 文件用途 新 UI 的动效 Token 与减少动态效果策略（规格 §6）

namespace CaelusApp
{
    internal static class UiMotion
    {
        public const int PageFadeMs = 250;
        public const int CardExpandMs = 300;
        public const int NumberRollMs = 400;
        public const int ToggleMs = 200;
        public const int ModalMs = 250;
        public const int SuccessPopMs = 400;

        // 减少动态效果：时长减半（规格 §6.3 允许“时长×0.5 或直接禁用”）
        public static int Duration(int baseMs, bool reduced)
        {
            return reduced ? baseMs / 2 : baseMs;
        }

        // 位移动画在减少动态效果模式下禁用，仅保留透明度渐变
        public static bool AllowsOffset(bool reduced)
        {
            return !reduced;
        }
    }
}
