// @author zenjiro 18967498922@163.com
// 文件用途 WPF 系统环境页 ViewModel：11 项内核/驱动开关的当前状态与执行逻辑

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class EnvironmentViewModel : ViewModelBase
    {
        private readonly GameMode gameMode;

        public EnvironmentViewModel(GameMode gameMode) { this.gameMode = gameMode; }

        // —— 标题区 ——
        public string PageTitle { get { return Lang.T("nav.env"); } }
        public string PageSub { get { return Lang.T("v16.env.sub"); } }
        public string KernelSection { get { return Lang.T("sec.env.kernel"); } }

        // —— 11 项开关卡片 ——
        public List<EnvToggle> Toggles { get; private set; }

        public void BuildToggles()
        {
            Toggles = new List<EnvToggle>();
            // HAGS
            Toggles.Add(new EnvToggle("hags",
                Lang.T("set.hags"), Lang.T("set.hags.n"),
                () => HagsTweak.EnabledByCaelus || HagsTweak.CurrentlyOn(),
                on => { bool ok = on ? HagsTweak.Enable() : HagsTweak.Disable(); return ok; },
                "hags.reboot"));
            // VBS（简化：仅 Disable/Restore）
            Toggles.Add(new EnvToggle("vbs",
                Lang.T("set.vbs"), VbsDesc(),
                () => VbsTweak.DisabledByCaelus,
                on => { bool ok = on ? VbsTweak.Disable() : VbsTweak.Restore(); return ok; },
                (Func<bool, string>)(on => on ? "vbs.done" : "vbs.restored")));
            // MPO
            Toggles.Add(new EnvToggle("mpo",
                Lang.T("set.mpo"), Lang.T("set.mpo.n"),
                () => MpoTweak.DisabledByCaelus || MpoTweak.CurrentlyDisabled(),
                on => { bool ok = on ? MpoTweak.Disable() : MpoTweak.Restore(); return ok; },
                "mpo.reboot"));
            // GPU 中断亲和
            Toggles.Add(new EnvToggle("irqaffinity",
                Lang.T("set.irqaffinity"), Lang.T("set.irqaffinity.n"),
                () => InterruptAffinityTweak.EnabledByCaelus,
                on => { bool ok = on ? InterruptAffinityTweak.Enable() : InterruptAffinityTweak.Disable(); return ok; },
                "irqaffinity.reboot"));
            // 网络亲和
            Toggles.Add(new EnvToggle("netaffinity",
                Lang.T("set.netaffinity"), Lang.T("set.netaffinity.n"),
                () => NetworkAffinityTweak.EnabledByCaelus,
                on => { bool ok = on ? NetworkAffinityTweak.Enable(gameMode.GetProfiles()) : NetworkAffinityTweak.Disable(); return ok; },
                "netaffinity.reboot"));
            // USB 中断亲和
            Toggles.Add(new EnvToggle("usbaffinity",
                Lang.T("set.usbaffinity"), Lang.T("set.usbaffinity.n"),
                () => UsbInterruptAffinityTweak.EnabledByCaelus,
                on => { bool ok = on ? UsbInterruptAffinityTweak.Enable() : UsbInterruptAffinityTweak.Disable(); return ok; },
                "irqaffinity.reboot"));
            // 游戏模式守护
            Toggles.Add(new EnvToggle("gmguard",
                Lang.T("set.gmguard"), Lang.T("set.gmguard.n"),
                () => GameModeGuard.EnabledByCaelus,
                on => { bool ok = on ? GameModeGuard.Enable() : GameModeGuard.Restore(); return ok; },
                null));
            // Nagle
            Toggles.Add(new EnvToggle("nagle",
                Lang.T("set.nagle"), Lang.T("set.nagle.n"),
                () => NagleTweak.EnabledByCaelus,
                on => { bool ok = on ? NagleTweak.Enable() : NagleTweak.Restore(); return ok; },
                "nagle.applied"));
            // 网络限流值校正
            Toggles.Add(new EnvToggle("netthrottle",
                Lang.T("set.netthrottle"), Lang.T("set.netthrottle.n") + "\r\n" + NetTweak.Describe(),
                () => NetTweak.RepairedByCaelus,
                on => { bool ok = on ? NetTweak.Repair() : NetTweak.Restore(); return ok; },
                null));
            // 设备电源
            Toggles.Add(new EnvToggle("devpower",
                Lang.T("set.devpower"), Lang.T("set.devpower.n"),
                () => DevicePowerTweak.EnabledByCaelus,
                on => { bool ok = on ? DevicePowerTweak.Enable() : DevicePowerTweak.Restore(); return ok; },
                null));
            // MSI
            Toggles.Add(new EnvToggle("msi",
                Lang.T("set.msi"), MsiDesc(),
                () => MsiModeTweak.EnabledByCaelus,
                on => { bool ok = on ? MsiModeTweak.Enable() : MsiModeTweak.Restore(); return ok; },
                "irqaffinity.reboot"));
        }

        // 刷新全部开关的当前状态（导航进入时调用）
        public void RefreshStatus()
        {
            if (Toggles == null) return;
            foreach (EnvToggle t in Toggles) t.Refresh();
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
            bool msiIdle = MsiModeTweak.Disabled().Count == 0 && !MsiModeTweak.EnabledByCaelus;
            return msiIdle ? Lang.T("msi.none") : Lang.T("set.msi.n");
        }
    }

    // 单个环境开关项
    internal sealed class EnvToggle : ViewModelBase
    {
        private readonly string id;
        private readonly string title;
        private readonly string desc;
        private readonly Func<bool> readState;
        private readonly Func<bool, bool> apply;
        // 完成提示的 lang key；为 null 表示不弹提示。也可能是带条件：Func<bool,string>
        private readonly object hintKey;
        private bool isOn;

        public EnvToggle(string id, string title, string desc,
            Func<bool> readState, Func<bool, bool> apply, object hintKey)
        {
            this.id = id;
            this.title = title;
            this.desc = desc;
            this.readState = readState;
            this.apply = apply;
            this.hintKey = hintKey;
            this.isOn = SafeRead();
        }

        public string Id { get { return id; } }
        public string Title { get { return title; } }
        public string Desc { get { return desc; } }

        public bool IsOn
        {
            get { return isOn; }
            set { isOn = value; Raise("IsOn"); }
        }

        // 执行 tweak 并返回提示文案的 lang key（null = 不提示，空串 = 操作失败）
        public string Apply(bool on)
        {
            bool ok = false;
            try { ok = apply(on); }
            catch { ok = false; }
            // 重读真实状态
            IsOn = SafeRead();
            if (!ok) return "";
            return ResolveHint(on);
        }

        public void Refresh() { IsOn = SafeRead(); }

        private bool SafeRead()
        {
            try { return readState(); }
            catch { return false; }
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
