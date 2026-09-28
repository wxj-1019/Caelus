// @author zenjiro 18967498922@163.com
// 文件用途 自动收起共享状态机（AutoHidePolicy）的回归测试：
//           可见性切换同步基线、游戏激活边沿只触发一次、游戏结束解除武装。
//           原 WinForms 宿主的界面休眠断言（动画时钟/导航分组）已随 src/Ui 一并移除。

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestUiDormancyState()
        {
            bool last = false, armed = false;
            AutoHidePolicy.SyncBaseline(true, ref last, ref armed);
            Eq(true, last);
            Eq(true, armed);
            Eq(AutoHideAction.None, AutoHidePolicy.Next(true, ref last, ref armed, true, true));
            AutoHidePolicy.SyncBaseline(false, ref last, ref armed);
            Eq(false, last);
            Eq(false, armed);
            Eq(AutoHideAction.Schedule, AutoHidePolicy.Next(true, ref last, ref armed, true, true));

            Eq("off", GraphicsViewModel.FrlModeOf(0));
            Eq("60", GraphicsViewModel.FrlModeOf(1));
            Eq("120", GraphicsViewModel.FrlModeOf(2));
            Eq("240", GraphicsViewModel.FrlModeOf(3));
            Eq("screen", GraphicsViewModel.FrlModeOf(4));
            Eq("off", GraphicsViewModel.FrlModeOf(9));
            for (int i = 0; i <= 4; i++) Eq(i, GraphicsViewModel.FrlIndexOf(GraphicsViewModel.FrlModeOf(i)));
            Eq(0, GraphicsViewModel.FrlIndexOf("nonsense"));
            Eq(0, GraphicsViewModel.FrlIndexOf(null));
        }
    }
}
