// @author zenjiro 18967498922@163.com
// 文件用途 WPF 反作弊页 ViewModel：逐分组的压制开关、档位、语义状态与聚合摘要

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CaelusApp
{
    internal sealed class AntiCheatViewModel : ViewModelBase
    {
        private readonly Tamer tamer;
        private int enabledGroupCount;
        private int runningProcessCount;
        private int suppressedProcessCount;

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
                if (value == !tamer.Paused) return;
                tamer.Paused = !value;
                Settings.Save("TameOn", value);
                Raise("MasterOn");
                Raise("MasterStateText");
                Raise("PauseNoticeText");
                RefreshStatus();
            }
        }

        public string MasterStateText
        {
            get { return MasterOn ? "正在监测已启用分组" : "配置保留 · 当前暂停"; }
        }

        public string PauseNoticeText
        {
            get
            {
                return "总开关已关闭：" + EnabledGroupCount + " 个分组配置仍会保留，当前不会压制任何进程。";
            }
        }

        public int TotalGroupCount { get { return Cards == null ? 0 : Cards.Count; } }

        public int EnabledGroupCount
        {
            get { return enabledGroupCount; }
            private set { SetProperty(ref enabledGroupCount, value, "EnabledGroupCount"); }
        }

        public int RunningProcessCount
        {
            get { return runningProcessCount; }
            private set { SetProperty(ref runningProcessCount, value, "RunningProcessCount"); }
        }

        public int SuppressedProcessCount
        {
            get { return suppressedProcessCount; }
            private set { SetProperty(ref suppressedProcessCount, value, "SuppressedProcessCount"); }
        }

        // —— 分组卡片：9 个反作弊产品 ——
        public List<AcCard> Cards { get; private set; }

        public void BuildCards()
        {
            Cards = new List<AcCard>();
            foreach (AcGroup g in AntiCheatCatalog.Groups)
                Cards.Add(new AcCard(this, tamer, g));
            Raise("TotalGroupCount");
            RefreshStatus();
        }

        // 页面可见期间低频调用；只读取现有 Tamer 状态，不触发业务动作。
        public void RefreshStatus()
        {
            if (Cards == null) return;

            int enabled = 0;
            int running = 0;
            int suppressed = 0;
            foreach (AcCard c in Cards)
            {
                c.Refresh();
                if (c.Enabled) enabled++;
                running += c.RunningCount;
                suppressed += c.SuppressedCount;
            }

            EnabledGroupCount = enabled;
            RunningProcessCount = running;
            SuppressedProcessCount = suppressed;
            Raise("MasterOn");
            Raise("MasterStateText");
            Raise("PauseNoticeText");
        }
    }

    // 单个反作弊分组卡片
    internal sealed class AcCard : ViewModelBase
    {
        private readonly AntiCheatViewModel parent;
        private readonly Tamer tamer;
        private readonly AcGroup group;
        private string status;
        private string statusKind;
        private bool isRunning;
        private bool isSuppressing;
        private int runningCount;
        private int suppressedCount;

        public AcCard(AntiCheatViewModel parent, Tamer tamer, AcGroup group)
        {
            this.parent = parent;
            this.tamer = tamer;
            this.group = group;
            status = "";
            statusKind = "Neutral";
        }

        public string Key { get { return group.Key; } }
        public string Title { get { return Lang.T("ac." + group.Key + ".n"); } }
        public string Desc { get { return Lang.T("ac." + group.Key + ".d"); } }
        public string ProcessText { get { return "匹配进程 · " + string.Join(" / ", group.Procs); } }

        public bool Enabled
        {
            get { return tamer.IsGroupEnabled(group.Key); }
            set
            {
                if (value == tamer.IsGroupEnabled(group.Key)) return;
                tamer.SetGroupEnabled(group.Key, value);
                parent.RefreshStatus();
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
                Raise("LevelIndex");
            }
        }

        public string Status
        {
            get { return status; }
            private set { SetProperty(ref status, value, "Status"); }
        }

        // Success / Info / Disabled / Warning，由视图映射到共享语义画刷。
        public string StatusKind
        {
            get { return statusKind; }
            private set { SetProperty(ref statusKind, value, "StatusKind"); }
        }

        public bool IsRunning
        {
            get { return isRunning; }
            private set { SetProperty(ref isRunning, value, "IsRunning"); }
        }

        public bool IsSuppressing
        {
            get { return isSuppressing; }
            private set { SetProperty(ref isSuppressing, value, "IsSuppressing"); }
        }

        public int RunningCount
        {
            get { return runningCount; }
            private set { SetProperty(ref runningCount, value, "RunningCount"); }
        }

        public int SuppressedCount
        {
            get { return suppressedCount; }
            private set { SetProperty(ref suppressedCount, value, "SuppressedCount"); }
        }

        public void Refresh()
        {
            bool enabled = tamer.IsGroupEnabled(group.Key);
            bool masterOn = parent.MasterOn;
            string nextStatus = tamer.GroupStatus(group.Key);
            int state = tamer.GroupState(group.Key);

            Raise("Enabled");
            Raise("LevelIndex");
            Status = nextStatus;
            UpdateCounts(enabled && masterOn, enabled && masterOn ? nextStatus : "");

            if (!enabled)
            {
                StatusKind = "Disabled";
                IsRunning = false;
                IsSuppressing = false;
            }
            else if (!masterOn)
            {
                StatusKind = "Warning";
                IsRunning = false;
                IsSuppressing = false;
            }
            else if (state == 1)
            {
                StatusKind = "Success";
                IsRunning = true;
                IsSuppressing = true;
            }
            else if (nextStatus == Lang.T("gs.noproc"))
            {
                StatusKind = "Info";
                IsRunning = false;
                IsSuppressing = false;
            }
            else
            {
                // 检测到进程但没有实际压制（例如仅有受保护进程）。
                StatusKind = "Warning";
                IsRunning = true;
                IsSuppressing = false;
            }
        }

        private void UpdateCounts(bool active, string text)
        {
            int suppressed = 0;
            int protectedCount = 0;
            if (active && !string.IsNullOrEmpty(text))
            {
                MatchCollection matches = Regex.Matches(text, "[0-9]+");
                if (matches.Count > 0) int.TryParse(matches[0].Value, out suppressed);
                if (matches.Count > 1) int.TryParse(matches[1].Value, out protectedCount);
            }
            SuppressedCount = suppressed;
            RunningCount = suppressed + protectedCount;
        }
    }
}
