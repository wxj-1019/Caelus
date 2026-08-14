// @author zenjiro 18967498922@163.com
// 文件用途 场景契约：仲裁器只仲裁副作用掌权资格，检测由各场景自行持续运行

namespace CaelusApp
{
    internal interface IScenario
    {
        ScenarioKind Kind { get; }
        int Priority { get; }

        /// <summary>获得副作用掌职权，施加本场景的全部系统副作用。在仲裁器锁外调用。</summary>
        void Grant();

        /// <summary>还原本场景的全部系统副作用并挂起；检测状态必须继续维护。在仲裁器锁外调用。</summary>
        void Suspend();
    }
}
