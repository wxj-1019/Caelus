// @author zenjiro 18967498922@163.com
// 文件用途 游戏场景在仲裁器中的占位实现：游戏副作用由 GameMode 自身管理，
//           这里只为仲裁器提供「游戏 > 开发专注 > 日常优化」的优先级参与者。
//           游戏掌权时仲裁器会先挂起开发/日常场景，游戏退出后再还原式补位。

namespace CaelusApp
{
    internal sealed class GameScenario : IScenario
    {
        public ScenarioKind Kind { get { return ScenarioKind.Game; } }
        public int Priority { get { return 100; } }

        // 游戏场景的 Grant/Suspend 不需要额外动作：GameMode.ActiveChanged 驱动
        // 游戏侧自己的加/还原流程；这里只在仲裁器里占住最高优先级席位。
        public void Grant() { }
        public void Suspend() { }
    }
}
