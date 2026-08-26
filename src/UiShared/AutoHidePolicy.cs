// @author zenjiro 18967498922@163.com
// 文件用途 「检测到游戏后自动收起」共享状态机：游戏激活 10 秒后窗口自动缩回托盘，
//           每局只收一次（用户重新打开后不再被收走）。WinForms / WPF 双宿主共享同一套
//           边沿判定语义（纯逻辑，无 UI 依赖）；定时器与窗口操作由各宿主自行实现。
namespace CaelusApp
{
    internal enum AutoHideAction { None, Schedule, Cancel }

    internal static class AutoHidePolicy
    {
        public const int DelayMs = 10000;
        public const string SettingKey = "AutoHideOnGame";

        // 每 UI tick 调用一次。gameActive 上升沿且本局未收过（armed）时置 armed；
        // 仅当设置开启且窗口可见时返回 Schedule（宿主据此启动延迟定时器）。
        public static AutoHideAction Next(bool gameActive, ref bool lastActive, ref bool armed,
            bool settingOn, bool visible)
        {
            if (gameActive == lastActive) return AutoHideAction.None;
            lastActive = gameActive;
            if (!gameActive) { armed = false; return AutoHideAction.Cancel; }
            if (armed) return AutoHideAction.None;
            armed = true;
            if (!settingOn || !visible) return AutoHideAction.None;
            return AutoHideAction.Schedule;
        }

        // 窗口可见性切换时同步基线：游戏已活跃 → 视为本局已收过，避免刚打开就被收起。
        public static void SyncBaseline(bool gameActive, ref bool lastActive, ref bool armed)
        {
            lastActive = gameActive;
            armed = gameActive;
        }
    }
}
