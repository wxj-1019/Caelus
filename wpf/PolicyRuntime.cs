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
        }

        public string HintText { get; private set; }
        public string CoreGroupTitle { get; private set; }
        public string CustomGroupTitle { get; private set; }
        public string ExtraGroupTitle { get; private set; }
        public ObservableCollection<PolicyCardViewModel> CoreCards { get; private set; }
        public ObservableCollection<PolicyCardViewModel> CustomCards { get; private set; }
        public ObservableCollection<PolicyCardViewModel> ExtraCards { get; private set; }

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
        }
    }
}
