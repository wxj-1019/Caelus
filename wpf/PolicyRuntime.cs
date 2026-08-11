// @author zenjiro 18967498922@163.com
// 文件用途 策略页运行时 ViewModel：绑定 WPF 视图，读写 GameMode 属性

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

        // 风险项：开启前需确认（ConfirmKey 非空）→ 行内显示警示标记 + 描述变色
        public bool IsRisky { get { return !string.IsNullOrEmpty(item.ConfirmKey); } }
        public string WarningTag { get { return "需注意"; } }

        // 开关成功翻转时通知宿主页 VM 重算计数（订阅见 PolicyPageViewModel）
        internal event System.Action Toggled;

        public bool IsOn
        {
            get { return isOn; }
            set
            {
                if (isLocked) return;
                // 开启前确认（仅冻结开关）
                if (value && !string.IsNullOrEmpty(item.ConfirmKey))
                {
                    MessageBoxResult r = MessageBox.Show(Lang.T(item.ConfirmKey), "Caelus",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.OK) return;
                }
                if (SetProperty(ref isOn, value, "IsOn"))
                {
                    PolicyViewModel.SetProperty(gm, item.PropertyName, value);
                    var toggled = Toggled;
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

        private void UpdateDisplayTitle()
        {
            string t = item.Title;
            if (isLocked) t = t + " · " + Lang.T("v14.preset.forced");
            displayTitle = t;
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

        // 当前模式名（摘要显示）；随 RefreshLocks（模式切换）刷新
        public string ModeText
        {
            get
            {
                PerformancePreset p = gm.ActivePreset;
                if (p == PerformancePreset.Competitive) return "竞技";
                if (p == PerformancePreset.Custom) return "自定义";
                return "巡航";
            }
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
        }

        public void RefreshLocks()
        {
            PerformancePreset preset = gm.ActivePreset;
            foreach (PolicyCardViewModel card in CustomCards)
            {
                bool locked, lockedValue;
                PolicyViewModel.GetLockState(card.PropertyName, preset, out locked, out lockedValue);
                card.IsLocked = locked;
                if (locked)
                {
                    PolicyViewModel.SetProperty(gm, card.PropertyName, lockedValue);
                    card.RefreshFromGameMode();
                }
                else
                {
                    card.RefreshFromGameMode();
                }
            }
            foreach (PolicyCardViewModel card in CoreCards) card.RefreshFromGameMode();
            foreach (PolicyCardViewModel card in ExtraCards) card.RefreshFromGameMode();
            Raise("ModeText");
            NotifyCounts();
        }
    }
}
