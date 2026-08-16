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

        public static void GetLockState(string propertyName, PerformancePreset preset,
            out bool locked, out bool lockedValue)
        {
            // 与旧 WinForms ApplyPresetPolicy 一致：
            // ・StrictCoreIsolation 任何模式下都可编辑（引擎只在自定义档读它，用户可预配置）
            // ・PauseSvcIndex 常规/竞技档强制显示 false（引擎 useSvc = custom ? svcPauseOn : false）
            // ・AggressiveSuppression / PauseDownloads / KillGameDvr 常规档 false、竞技档 true
            // ・自定义档全部可编辑
            bool isCustom = false;
            foreach (PolicyItem item in custom)
            {
                if (item.PropertyName == propertyName) { isCustom = true; break; }
            }
            if (!isCustom || propertyName == "StrictCoreIsolation")
            {
                locked = false; lockedValue = false;
                return;
            }

            if (preset == PerformancePreset.Standard) { locked = true; lockedValue = false; return; }
            if (preset == PerformancePreset.Competitive)
            {
                locked = true;
                lockedValue = propertyName != "PauseSvcIndex";
                return;
            }
            locked = false; lockedValue = false;
        }

        public static bool GetProperty(GameMode gm, string name)
        {
            switch (name)
            {
                case "SuppressBackground": return gm.SuppressBackground;
                case "GpuDemote": return gm.GpuDemote;
                case "FreezeBackground": return gm.FreezeBackground;
                case "BoostGame": return gm.BoostGame;
                case "IfeoBoostFallback": return gm.IfeoBoostFallback;
                case "RenderLaneOn": return gm.RenderLaneOn;
                case "PowerPlanSwitch": return gm.PowerPlanSwitch;
                case "NotifQuiet": return gm.NotifQuiet;
                case "HzGuard": return gm.HzGuard;
                case "StrictCoreIsolation": return gm.StrictCoreIsolation;
                case "AggressiveSuppression": return gm.AggressiveSuppression;
                case "PauseDownloads": return gm.PauseDownloads;
                case "PauseSvcIndex": return gm.PauseSvcIndex;
                case "KillGameDvr": return gm.KillGameDvr;
                case "IdleStateDisable": return gm.IdleStateDisable;
                case "VisualFxDowngrade": return gm.VisualFxDowngrade;
                case "TrimWorkingSet": return gm.TrimWorkingSet;
                case "PurgeStandby": return gm.PurgeStandby;
                case "PauseWindowsUpdate": return gm.PauseWindowsUpdate;
                case "PresenceQosOff": return gm.PresenceQosOff;
                case "KeepAwake": return gm.KeepAwake;
                default: throw new System.ArgumentException("unknown policy property: " + name);
            }
        }

        public static void SetProperty(GameMode gm, string name, bool value)
        {
            switch (name)
            {
                case "SuppressBackground": gm.SuppressBackground = value; break;
                case "GpuDemote": gm.GpuDemote = value; break;
                case "FreezeBackground": gm.FreezeBackground = value; break;
                case "BoostGame": gm.BoostGame = value; break;
                case "IfeoBoostFallback": gm.IfeoBoostFallback = value; break;
                case "RenderLaneOn": gm.RenderLaneOn = value; break;
                case "PowerPlanSwitch": gm.PowerPlanSwitch = value; break;
                case "NotifQuiet": gm.NotifQuiet = value; break;
                case "HzGuard": gm.HzGuard = value; break;
                case "StrictCoreIsolation": gm.StrictCoreIsolation = value; break;
                case "AggressiveSuppression": gm.AggressiveSuppression = value; break;
                case "PauseDownloads": gm.PauseDownloads = value; break;
                case "PauseSvcIndex": gm.PauseSvcIndex = value; break;
                case "KillGameDvr": gm.KillGameDvr = value; break;
                case "IdleStateDisable": gm.IdleStateDisable = value; break;
                case "VisualFxDowngrade": gm.VisualFxDowngrade = value; break;
                case "TrimWorkingSet": gm.TrimWorkingSet = value; break;
                case "PurgeStandby": gm.PurgeStandby = value; break;
                case "PauseWindowsUpdate": gm.PauseWindowsUpdate = value; break;
                case "PresenceQosOff": gm.PresenceQosOff = value; break;
                case "KeepAwake": gm.KeepAwake = value; break;
                default: throw new System.ArgumentException("unknown policy property: " + name);
            }
        }
    }
}
