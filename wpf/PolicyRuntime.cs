// @author zenjiro 18967498922@163.com
// 文件用途 策略页运行时 ViewModel：绑定 WPF 视图，读写 GameMode 属性

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace CaelusApp.WpfHost
{
    internal sealed class PolicyCardViewModel : ViewModelBase
    {
        private readonly GameMode gm;
        private readonly PolicyItem item;
        private bool isOn;
        private bool isLocked;
        private string displayTitle;

        public PolicyCardViewModel(GameMode gm, PolicyItem item)
        {
            this.gm = gm;
            this.item = item;
            displayTitle = item.Title;
            isOn = PolicyViewModel.GetProperty(gm, item.PropertyName);
        }

        public string Title { get { return displayTitle; } }
        public string Description { get { return item.Description; } }
        public string PropertyName { get { return item.PropertyName; } }
        public string LockNote { get { return isLocked ? "此项由当前模式控制，切换到自定义模式后可调整。" : string.Empty; } }
        public string ToggleAutomationName
        {
            get { return isLocked ? item.Title + "，由当前模式控制" : item.Title; }
        }

        // 风险项：开启前需确认（ConfirmKey 非空）→ 行内保留警示标记，说明文字保持次级色。
        public bool IsRisky { get { return !string.IsNullOrEmpty(item.ConfirmKey); } }
        public string WarningTag { get { return "需注意"; } }

        // 开关成功翻转时通知宿主页 VM 重算计数（订阅见 PolicyPageViewModel）
        internal event System.Action Toggled;

        public bool IsOn
        {
            get { return isOn; }
            set
            {
                if (value == isOn) return;
                if (isLocked)
                {
                    ReassertToggleState();
                    return;
                }
                // 绑定已先改变 ToggleButton 的视觉态；取消时主动重发旧值，确保可靠回滚。
                if (value && !string.IsNullOrEmpty(item.ConfirmKey))
                {
                    MessageBoxResult r = MessageBox.Show(Lang.T(item.ConfirmKey), "Caelus",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.OK)
                    {
                        ReassertToggleState();
                        return;
                    }
                }
                if (SetProperty(ref isOn, value, "IsOn"))
                {
                    PolicyViewModel.SetProperty(gm, item.PropertyName, value);
                    System.Action toggled = Toggled;
                    if (toggled != null) toggled();
                }
            }
        }

        public bool IsLocked
        {
            get { return isLocked; }
            set
            {
                if (SetProperty(ref isLocked, value, "IsLocked"))
                {
                    UpdateDisplayTitle();
                    Raise("IsEnabled");
                    Raise("LockNote");
                    Raise("ToggleAutomationName");
                }
            }
        }

        public bool IsEnabled { get { return !isLocked; } }

        public void RefreshFromGameMode()
        {
            bool newVal = PolicyViewModel.GetProperty(gm, item.PropertyName);
            if (isOn != newVal)
            {
                isOn = newVal;
                Raise("IsOn");
            }
        }

        // 与旧 WinForms 的 SetSilently 一致：锁定只做视觉同步，
        // 绝不写回 GameMode / 注册表（避免覆盖用户在自定义档的真实偏好）。
        public void SyncVisual(bool locked, bool lockedValue)
        {
            if (isLocked != locked)
            {
                isLocked = locked;
                UpdateDisplayTitle();
                Raise("IsLocked");
                Raise("IsEnabled");
                Raise("LockNote");
                Raise("ToggleAutomationName");
            }
            bool visual = locked ? lockedValue : PolicyViewModel.GetProperty(gm, item.PropertyName);
            if (isOn != visual)
            {
                isOn = visual;
                Raise("IsOn");
            }
        }

        private void ReassertToggleState()
        {
            System.Windows.Application app = System.Windows.Application.Current;
            if (app != null && app.Dispatcher != null)
            {
                app.Dispatcher.BeginInvoke(new System.Action(delegate { Raise("IsOn"); }));
            }
            else
            {
                Raise("IsOn");
            }
        }

        private void UpdateDisplayTitle()
        {
            displayTitle = item.Title;
            Raise("Title");
        }
    }

    internal sealed class PolicyPageViewModel : ViewModelBase
    {
        private readonly GameMode gm;

        public PolicyPageViewModel(GameMode gm)
        {
            this.gm = gm;
            HintText = PolicyViewModel.HintText;
            CoreGroupTitle = PolicyViewModel.CoreGroupTitle;
            CustomGroupTitle = PolicyViewModel.CustomGroupTitle;
            ExtraGroupTitle = PolicyViewModel.ExtraGroupTitle;

            CoreCards = new ObservableCollection<PolicyCardViewModel>();
            CustomCards = new ObservableCollection<PolicyCardViewModel>();
            ExtraCards = new ObservableCollection<PolicyCardViewModel>();

            foreach (PolicyItem item in PolicyViewModel.CoreItems)
                CoreCards.Add(new PolicyCardViewModel(gm, item));
            foreach (PolicyItem item in PolicyViewModel.CustomItems)
                CustomCards.Add(new PolicyCardViewModel(gm, item));
            foreach (PolicyItem item in PolicyViewModel.ExtraItems)
                ExtraCards.Add(new PolicyCardViewModel(gm, item));

            RefreshLocks();

            // 订阅每张卡的翻转事件 → 实时刷新分组计数摘要
            foreach (PolicyCardViewModel c in CoreCards) c.Toggled += NotifyCounts;
            foreach (PolicyCardViewModel c in CustomCards) c.Toggled += NotifyCounts;
            foreach (PolicyCardViewModel c in ExtraCards) c.Toggled += NotifyCounts;
            NotifyCounts();
        }

        public string HintText { get; private set; }
        public string CoreGroupTitle { get; private set; }
        public string CustomGroupTitle { get; private set; }
        public string ExtraGroupTitle { get; private set; }
        public List<string> GroupLabels
        {
            get { return new List<string> { "核心", "自定义", "额外" }; }
        }

        // 当前模式名（摘要显示）；随 RefreshLocks（模式切换）刷新
        public string ModeText
        {
            get
            {
                PerformancePreset p = gm.ActivePreset;
                if (p == PerformancePreset.Competitive) return Lang.T("preset.competitive");
                if (p == PerformancePreset.Custom) return Lang.T("preset.custom");
                return Lang.T("preset.standard");
            }
        }
        public string ModeBadgeText { get { return ModeText + "模式"; } }
        public string ModeAutomationName { get { return "当前策略模式：" + ModeText; } }
        public string SummaryAutomationName
        {
            get { return "策略状态：" + TotalCount + " 项中 " + TotalEnabled + " 项已启用，当前为" + ModeText + "模式"; }
        }
        public ObservableCollection<PolicyCardViewModel> CoreCards { get; private set; }
        public ObservableCollection<PolicyCardViewModel> CustomCards { get; private set; }
        public ObservableCollection<PolicyCardViewModel> ExtraCards { get; private set; }

        // 聚合 + 分组计数（绑定顶部摘要与各分组头）
        public int TotalCount { get { return CoreCards.Count + CustomCards.Count + ExtraCards.Count; } }
        public int TotalEnabled { get { return CoreEnabled + CustomEnabled + ExtraEnabled; } }
        public int CoreTotal { get { return CoreCards.Count; } }
        public int CoreEnabled { get { return CountOn(CoreCards); } }
        public int CustomTotal { get { return CustomCards.Count; } }
        public int CustomEnabled { get { return CountOn(CustomCards); } }
        public int ExtraTotal { get { return ExtraCards.Count; } }
        public int ExtraEnabled { get { return CountOn(ExtraCards); } }

        private static int CountOn(System.Collections.Generic.IEnumerable<PolicyCardViewModel> cards)
        {
            int n = 0;
            foreach (PolicyCardViewModel c in cards) if (c.IsOn) n++;
            return n;
        }

        internal void NotifyCounts()
        {
            Raise("CoreTotal"); Raise("CoreEnabled");
            Raise("CustomTotal"); Raise("CustomEnabled");
            Raise("ExtraTotal"); Raise("ExtraEnabled");
            Raise("TotalCount"); Raise("TotalEnabled");
            Raise("SummaryAutomationName");
        }

        public void RefreshLocks()
        {
            PerformancePreset preset = gm.ActivePreset;
            // 与旧 WinForms 一致：锁定只做视觉同步（SetSilently），不写入 GameMode/注册表
            foreach (PolicyCardViewModel card in CustomCards)
            {
                bool locked, lockedValue;
                PolicyViewModel.GetLockState(card.PropertyName, preset, out locked, out lockedValue);
                card.SyncVisual(locked, lockedValue);
            }
            foreach (PolicyCardViewModel card in CoreCards) card.SyncVisual(false, false);
            foreach (PolicyCardViewModel card in ExtraCards) card.SyncVisual(false, false);
            Raise("ModeText");
            Raise("ModeBadgeText");
            Raise("ModeAutomationName");
            NotifyCounts();
        }
    }
}
