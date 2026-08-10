// @author zenjiro 18967498922@163.com
// 文件用途 优化策略页 ViewModel：21 项策略元数据 + 锁定矩阵 + GameMode 属性映射

using System;
using System.Collections.ObjectModel;

namespace CaelusApp
{
    internal sealed class PolicyItem
    {
        public string Title;
        public string Description;
        public string PropertyName;
        public string ConfirmKey;
    }

    internal sealed class PolicyViewModel : ViewModelBase
    {
        public static readonly string HintText = Lang.T("v15.policy.mode.hint");
        public static readonly string CoreGroupTitle = Lang.T("v15.policy.core");
        public static readonly string CustomGroupTitle = Lang.T("v15.policy.custom");
        public static readonly string ExtraGroupTitle = Lang.T("v15.policy.extras");

        private static readonly PolicyItem[] core = new PolicyItem[]
        {
            new PolicyItem { Title = Lang.T("v14.bg.master"), Description = Lang.T("v14.bg.master.sub"), PropertyName = "SuppressBackground" },
            new PolicyItem { Title = Lang.T("gm.gpudemote"), Description = Lang.T("gm.gpudemote.sub"), PropertyName = "GpuDemote" },
            new PolicyItem { Title = Lang.T("gm.freeze"), Description = Lang.T("gm.freeze.sub"), PropertyName = "FreezeBackground", ConfirmKey = "gm.freeze.warn" },
            new PolicyItem { Title = Lang.T("gm.boost"), Description = Lang.T("v15.boost.sub"), PropertyName = "BoostGame" },
            new PolicyItem { Title = Lang.T("gm.ifeo"), Description = Lang.T("gm.ifeo.sub"), PropertyName = "IfeoBoostFallback" },
            new PolicyItem { Title = Lang.T("gm.lane"), Description = Lang.T("gm.lane.sub"), PropertyName = "RenderLaneOn" },
            new PolicyItem { Title = Lang.T("set.plan"), Description = Lang.T("v15.plan.sub"), PropertyName = "PowerPlanSwitch" },
            new PolicyItem { Title = Lang.T("set.notif"), Description = Lang.T("v15.notif.sub"), PropertyName = "NotifQuiet" },
            new PolicyItem { Title = Lang.T("set.hz"), Description = Lang.T("v15.hz.sub"), PropertyName = "HzGuard" }
        };

        private static readonly PolicyItem[] custom = new PolicyItem[]
        {
            new PolicyItem { Title = Lang.T("v14.cpu.adaptive"), Description = Lang.T("v14.cpu.adaptive.sub2"), PropertyName = "StrictCoreIsolation" },
            new PolicyItem { Title = Lang.T("gm.aggressive"), Description = Lang.T("gm.aggressive.sub"), PropertyName = "AggressiveSuppression" },
            new PolicyItem { Title = Lang.T("gm.pausedl"), Description = Lang.T("v15.custom.override"), PropertyName = "PauseDownloads" },
            new PolicyItem { Title = Lang.T("gm.pausesvc"), Description = Lang.T("v15.custom.override"), PropertyName = "PauseSvcIndex" },
            new PolicyItem { Title = Lang.T("set.dvr"), Description = Lang.T("v15.custom.override"), PropertyName = "KillGameDvr" }
        };

        private static readonly PolicyItem[] extra = new PolicyItem[]
        {
            new PolicyItem { Title = Lang.T("gm.idledisable"), Description = Lang.T("gm.idledisable.sub"), PropertyName = "IdleStateDisable" },
            new PolicyItem { Title = Lang.T("gm.visualfx"), Description = Lang.T("gm.visualfx.sub"), PropertyName = "VisualFxDowngrade" },
            new PolicyItem { Title = Lang.T("set.trim"), Description = Lang.T("v15.trim.sub"), PropertyName = "TrimWorkingSet" },
            new PolicyItem { Title = Lang.T("gm.standby"), Description = Lang.T("gm.standby.sub"), PropertyName = "PurgeStandby" },
            new PolicyItem { Title = Lang.T("gm.pausewu"), Description = Lang.T("gm.pausewu.sub"), PropertyName = "PauseWindowsUpdate" },
            new PolicyItem { Title = Lang.T("set.pqos"), Description = Lang.T("set.pqos.n"), PropertyName = "PresenceQosOff" },
            new PolicyItem { Title = Lang.T("set.awake"), Description = Lang.T("set.awake.n"), PropertyName = "KeepAwake" }
        };

        public static ReadOnlyCollection<PolicyItem> CoreItems { get { return Array.AsReadOnly(core); } }
        public static ReadOnlyCollection<PolicyItem> CustomItems { get { return Array.AsReadOnly(custom); } }
        public static ReadOnlyCollection<PolicyItem> ExtraItems { get { return Array.AsReadOnly(extra); } }

        public static System.Collections.Generic.IEnumerable<PolicyItem> AllItems()
        {
            foreach (PolicyItem i in core) yield return i;
            foreach (PolicyItem i in custom) yield return i;
            foreach (PolicyItem i in extra) yield return i;
        }
    }
}
