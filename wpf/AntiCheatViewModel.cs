// @author zenjiro 18967498922@163.com
// 文件用途 WPF 反作弊页 ViewModel：逐分组的压制开关、档位与状态文案

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class AntiCheatViewModel : ViewModelBase
    {
        private readonly Tamer tamer;

        public AntiCheatViewModel(Tamer tamer) { this.tamer = tamer; }

        // —— 标题区 ——
        public string PageTitle { get { return Lang.T("v14.anticheat"); } }
        public string PageSub { get { return Lang.T("v15.anticheat.sub"); } }
        public string BoundaryTitle { get { return Lang.T("v14.anticheat.boundary"); } }

        // —— 压制档位 3 段标签 ——
        public List<string> LevelLabels
        {
            get
            {
                return new List<string> {
                    Lang.T("tame.lvl.eco"), Lang.T("tame.lvl.res"), Lang.T("tame.lvl.iso")
                };
            }
        }

        // —— 总开关（tamer.Paused 反相）——
        public string MasterTitle { get { return Lang.T("tame.toggle"); } }
        public string MasterSub { get { return Lang.T("v14.anticheat.master.sub"); } }
        public bool MasterOn
        {
            get { return !tamer.Paused; }
            set
            {
                tamer.Paused = !value;
                Settings.Save("TameOn", value);
                Raise("MasterOn");
                RefreshStatus();
            }
        }

        // —— 分组卡片：9 个反作弊产品 ——
        public List<AcCard> Cards { get; private set; }

        public void BuildCards()
        {
            Cards = new List<AcCard>();
            foreach (AcGroup g in AntiCheatCatalog.Groups)
            {
                Cards.Add(new AcCard(this, tamer, g));
            }
        }

        // 刷新全部状态文案（导航进入时调用）
        public void RefreshStatus()
        {
            if (Cards == null) return;
            foreach (AcCard c in Cards) c.Refresh();
        }
    }

    // 单个反作弊分组卡片
    internal sealed class AcCard : ViewModelBase
    {
        private readonly AntiCheatViewModel parent;
        private readonly Tamer tamer;
        private readonly AcGroup group;
        private string status;

        public AcCard(AntiCheatViewModel parent, Tamer tamer, AcGroup group)
        {
            this.parent = parent;
            this.tamer = tamer;
            this.group = group;
            status = "";
        }

        public string Key { get { return group.Key; } }
        public string Title { get { return Lang.T("ac." + group.Key + ".n"); } }
        public string Desc { get { return Lang.T("ac." + group.Key + ".d") + "  ·  " + string.Join(" / ", group.Procs); } }

        public bool Enabled
        {
            get { return tamer.IsGroupEnabled(group.Key); }
            set
            {
                tamer.SetGroupEnabled(group.Key, value);
                Refresh();
            }
        }

        // 档位索引：Eco=0, Restrained=1, Isolated=2
        public int LevelIndex
        {
            get { return (int)tamer.GroupLevel(group.Key) - 1; }
            set
            {
                int clamped = value < 0 ? 0 : (value > 2 ? 2 : value);
                tamer.SetGroupLevel(group.Key, (SuppressionLevel)(clamped + 1));
            }
        }

        public string Status
        {
            get { return status; }
            set { SetProperty(ref status, value, "Status"); }
        }

        public void Refresh()
        {
            // 重读档位/开关以反映外部变化
            Raise("Enabled");
            Raise("LevelIndex");
            Status = tamer.GroupStatus(group.Key);
        }
    }
}
