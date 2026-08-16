// @author zenjiro 18967498922@163.com
// 文件用途 WPF 系统环境页 ViewModel：11 项系统开关的分组、可用性、风险、重启与行内状态

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class EnvironmentViewModel : ViewModelBase
    {
        private readonly GameMode gameMode;

        public EnvironmentViewModel(GameMode gameMode) { this.gameMode = gameMode; }

        // —— 标题与分区 ——
        public string PageTitle { get { return Lang.T("nav.env"); } }
        public string PageSub { get { return "按生效范围整理系统底层设置。改动均可在本页恢复，只有标记“需重启”的项目需要重启。"; } }
        public string GraphicsSection { get { return "图形呈现"; } }
        public string SecuritySection { get { return "安全与内核"; } }
        public string InterruptSection { get { return "中断与设备"; } }
        public string NetworkSection { get { return "网络与守护"; } }

        // 保留聚合列表供既有刷新入口使用，同时向视图暴露四个语义分组。
        public List<EnvToggle> Toggles { get; private set; }
        public List<EnvToggle> GraphicsToggles { get; private set; }
        public List<EnvToggle> SecurityToggles { get; private set; }
        public List<EnvToggle> InterruptToggles { get; private set; }
        public List<EnvToggle> NetworkToggles { get; private set; }

        public void BuildToggles()
        {
            Toggles = new List<EnvToggle>();
            GraphicsToggles = new List<EnvToggle>();
            SecurityToggles = new List<EnvToggle>();
            InterruptToggles = new List<EnvToggle>();
            NetworkToggles = new List<EnvToggle>();

            Add(GraphicsToggles, new EnvToggle("hags",
                Lang.T("set.hags"), Lang.T("set.hags.n"),
                () => HagsTweak.EnabledByCaelus || HagsTweak.CurrentlyOn(),
                on => on ? HagsTweak.Enable() : HagsTweak.Disable(),
                "hags.reboot", true, false, null, null));

            Add(GraphicsToggles, new EnvToggle("mpo",
                Lang.T("set.mpo"), Lang.T("set.mpo.n"),
                () => MpoTweak.DisabledByCaelus || MpoTweak.CurrentlyDisabled(),
                on => on ? MpoTweak.Disable() : MpoTweak.Restore(),
                "mpo.reboot", true, false, null, null));

            Add(SecurityToggles, new EnvToggle("vbs",
                Lang.T("set.vbs"), VbsDesc(),
                () => VbsTweak.DisabledByCaelus,
                on => on ? VbsTweak.Disable() : VbsTweak.Restore(),
                (Func<bool, string>)(on => on ? "vbs.done" : "vbs.restored"),
                true, true, null, VbsDesc));

            Add(InterruptToggles, new EnvToggle("irqaffinity",
                Lang.T("set.irqaffinity"), Lang.T("set.irqaffinity.n"),
                () => InterruptAffinityTweak.EnabledByCaelus,
                on => on ? InterruptAffinityTweak.Enable() : InterruptAffinityTweak.Disable(),
                "irqaffinity.reboot", true, false, null, null));

            Add(InterruptToggles, new EnvToggle("usbaffinity",
                Lang.T("set.usbaffinity"), Lang.T("set.usbaffinity.n"),
                () => UsbInterruptAffinityTweak.EnabledByCaelus,
                on => on ? UsbInterruptAffinityTweak.Enable() : UsbInterruptAffinityTweak.Disable(),
                "irqaffinity.reboot", true, false, null, null));

            Add(InterruptToggles, new EnvToggle("msi",
                Lang.T("set.msi"), MsiDesc(),
                () => MsiModeTweak.EnabledByCaelus,
                on => on ? MsiModeTweak.Enable() : MsiModeTweak.Restore(),
                "irqaffinity.reboot", true, false,
                () => MsiModeTweak.EnabledByCaelus || MsiModeTweak.Disabled().Count > 0,
                MsiDesc));

            Add(InterruptToggles, new EnvToggle("devpower",
                Lang.T("set.devpower"), Lang.T("set.devpower.n"),
                () => DevicePowerTweak.EnabledByCaelus,
                on => on ? DevicePowerTweak.Enable() : DevicePowerTweak.Restore(),
                null, false, false, null, null));

            Add(NetworkToggles, new EnvToggle("netaffinity",
                Lang.T("set.netaffinity"), Lang.T("set.netaffinity.n"),
                () => NetworkAffinityTweak.EnabledByCaelus,
                on => on ? NetworkAffinityTweak.Enable(gameMode.GetProfiles()) : NetworkAffinityTweak.Disable(),
                "netaffinity.reboot", true, false, null, null));

            Add(NetworkToggles, new EnvToggle("gmguard",
                Lang.T("set.gmguard"), Lang.T("set.gmguard.n"),
                () => GameModeGuard.EnabledByCaelus,
                on => on ? GameModeGuard.Enable() : GameModeGuard.Restore(),
                null, false, false, null, null));

            Add(NetworkToggles, new EnvToggle("nagle",
                Lang.T("set.nagle"), Lang.T("set.nagle.n"),
                () => NagleTweak.EnabledByCaelus,
                on => on ? NagleTweak.Enable() : NagleTweak.Restore(),
                (Func<bool, string>)(on => on ? "nagle.applied" : null),
                false, false, null, null));

            Add(NetworkToggles, new EnvToggle("netthrottle",
                Lang.T("set.netthrottle"), NetThrottleDesc(),
                () => NetTweak.RepairedByCaelus,
                on => on ? NetTweak.Repair() : NetTweak.Restore(),
                null, true, false,
                () => NetTweak.NeedsRepair() || NetTweak.RepairedByCaelus,
                NetThrottleDesc));
        }

        public void RefreshStatus()
        {
            if (Toggles == null) return;
            foreach (EnvToggle t in Toggles) t.Refresh();
        }

        private void Add(List<EnvToggle> group, EnvToggle item)
        {
            group.Add(item);
            Toggles.Add(item);
        }

        private static string VbsDesc()
        {
            string key;
            VbsTweak.State st;
            try { st = VbsTweak.Query(); }
            catch { st = new VbsTweak.State(); }
            if (VbsTweak.DisabledByCaelus && (!st.WmiOk || st.VbsRunning)) key = "vbs.state.pending";
            else if (!st.WmiOk) key = "vbs.state.unknown";
            else if (st.VbsRunning) key = "vbs.state.on";
            else key = "vbs.state.off";
            return Lang.T(key);
        }

        private static string MsiDesc()
        {
            bool idle = MsiModeTweak.Disabled().Count == 0 && !MsiModeTweak.EnabledByCaelus;
            return idle ? Lang.T("msi.none") : Lang.T("set.msi.n");
        }

        private static string NetThrottleDesc()
        {
            return Lang.T("set.netthrottle.n") + "\r\n" + NetTweak.Describe();
        }
    }

    // 单个环境开关项。可用性探针缺失时采用安全默认“可用”，不虚构硬件结论。
    internal sealed class EnvToggle : ViewModelBase
    {
        private readonly string id;
        private readonly string title;
        private string desc;
        private readonly Func<bool> readState;
        private readonly Func<bool, bool> apply;
        private readonly object hintKey;
        private readonly Func<bool> readAvailability;
        private readonly Func<string> readDescription;
        private bool isOn;
        private bool isEnabled;
        private bool availabilityKnown;
        private string feedbackText;
        private string feedbackKind = "Success";

        public EnvToggle(string id, string title, string desc,
            Func<bool> readState, Func<bool, bool> apply, object hintKey,
            bool requiresRestart, bool isRisky, Func<bool> readAvailability,
            Func<string> readDescription)
        {
            this.id = id;
            this.title = title;
            this.desc = desc;
            this.readState = readState;
            this.apply = apply;
            this.hintKey = hintKey;
            this.readAvailability = readAvailability;
            this.readDescription = readDescription;
            RequiresRestart = requiresRestart;
            IsRisky = isRisky;
            feedbackText = "";
            isOn = SafeRead();
            isEnabled = SafeAvailability();
        }

        public string Id { get { return id; } }
        public string Title { get { return title; } }
        public string Desc { get { return desc; } }
        public bool RequiresRestart { get; private set; }
        public bool IsRisky { get; private set; }
        public string RestartText { get { return "需重启"; } }
        public string RiskText { get { return "风险"; } }

        public bool IsOn
        {
            get { return isOn; }
            private set { SetProperty(ref isOn, value, "IsOn"); }
        }

        public bool IsEnabled
        {
            get { return isEnabled; }
            private set
            {
                if (SetProperty(ref isEnabled, value, "IsEnabled"))
                    Raise("AvailabilityText");
            }
        }

        public string AvailabilityText
        {
            get
            {
                if (!availabilityKnown) return "当前状态不可确认";
                if (IsEnabled) return "可用";
                if (id == "msi") return "本机无可调整设备";
                if (id == "netthrottle") return "当前值正常，无需调整";
                return "当前不可用";
            }
        }

        public string StateText { get { return IsOn ? "已开启" : "已关闭"; } }

        public string FeedbackText
        {
            get { return feedbackText; }
            private set { SetProperty(ref feedbackText, value, "FeedbackText"); }
        }

        public string FeedbackKind
        {
            get { return feedbackKind; }
            private set { SetProperty(ref feedbackKind, value, "FeedbackKind"); }
        }

        // 返回 false 代表执行失败；成功/失败文案写入 FeedbackText + FeedbackKind，由视图行内呈现。
        public bool Apply(bool on)
        {
            bool ok;
            try { ok = apply(on); }
            catch { ok = false; }

            Refresh();
            if (!ok)
            {
                FeedbackKind = "Error";
                FeedbackText = Lang.T("env.failed");
                return false;
            }

            string hint = ResolveHint(on);
            FeedbackKind = "Success";
            FeedbackText = string.IsNullOrEmpty(hint)
                ? "已应用 · " + StateText
                : Lang.T(hint);
            return true;
        }

        public void Refresh()
        {
            IsOn = SafeRead();
            IsEnabled = SafeAvailability();
            if (readDescription != null)
            {
                string next;
                try { next = readDescription(); }
                catch { next = desc; }
                if (next != desc)
                {
                    desc = next;
                    Raise("Desc");
                }
            }
            Raise("StateText");
            Raise("AvailabilityText");
        }

        private bool SafeRead()
        {
            try { return readState(); }
            catch { return false; }
        }

        private bool SafeAvailability()
        {
            if (readAvailability == null)
            {
                availabilityKnown = true;
                return true;
            }
            try
            {
                bool available = readAvailability();
                availabilityKnown = true;
                return available;
            }
            catch
            {
                availabilityKnown = false;
                return false;
            }
        }

        private string ResolveHint(bool on)
        {
            if (hintKey == null) return null;
            Func<bool, string> fn = hintKey as Func<bool, string>;
            if (fn != null) return fn(on);
            return (string)hintKey;
        }
    }
}
