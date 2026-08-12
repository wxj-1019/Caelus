// @author zenjiro 18967498922@163.com
// 文件用途 WPF 白名单页 ViewModel：规则列表 + 添加/移除/重置 + 当前匹配数深查

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CaelusApp
{
    internal sealed class WhitelistViewModel : ViewModelBase
    {
        private readonly GameMode gm;
        private WhitelistItemSelected selected;
        private int busy; // 深查占位 0/1（Interlocked）
        private int userRuleCount;

        public WhitelistViewModel(GameMode gm)
        {
            this.gm = gm;
            Items = new ObservableCollection<WhitelistItemSelected>();
        }

        // —— 标题区 ——
        public string PageTitle { get { return Lang.T("nav.white"); } }
        public string PageSub { get { return Lang.T("white.page.sub"); } }

        // —— 空闲态 ——
        public string EmptyTitle { get { return "CAELUS SHIELD"; } }
        public string EmptyHint { get { return Lang.T("white.page.empty"); } }

        // —— 按钮文案 ——
        public string PickText { get { return Lang.T("white.page.pick"); } }
        public string BrowseText { get { return Lang.T("btn.browse"); } }
        public string RemoveText { get { return Lang.T("btn.remove"); } }
        public string ResetText { get { return Lang.T("btn.reset"); } }
        public string DropHint
        {
            get
            {
                string text = Lang.T("white.page.drop");
                try
                {
                    List<string> detected = GamePlatformCatalog.DetectedPlatforms();
                    if (detected.Count > 0)
                        text += "\r\n\r\n" + Lang.F("white.page.platforms", string.Join("、", detected.ToArray()));
                }
                catch { }
                return text;
            }
        }

        public ObservableCollection<WhitelistItemSelected> Items { get; private set; }

        public WhitelistItemSelected Selected
        {
            get { return selected; }
            set
            {
                if (SetProperty(ref selected, value, "Selected"))
                {
                    Raise("CanRemoveSelected");
                    Raise("CanNarrow");
                    Raise("CanWiden");
                }
            }
        }

        // 空态只看用户自建规则；内置必需规则不应遮掉“添加第一条规则”的任务入口。
        public bool IsEmpty { get { return userRuleCount == 0; } }

        public bool CanRemoveSelected
        {
            get { return selected != null && !selected.Required; }
        }

        // 缩窄（family→exact）
        public bool CanNarrow
        {
            get { return selected != null && !selected.Required && selected.Kind == WhitelistRuleKind.ApplicationFamily; }
        }

        // 扩展（exact→family，且不是危险锚点）
        public bool CanWiden
        {
            get
            {
                if (selected == null || selected.Required) return false;
                if (selected.Kind != WhitelistRuleKind.ExactPath) return false;
                return !WhitelistRule.IsUnsafeFamilyAnchor(selected.Value);
            }
        }

        // —— 刷新：先填快速视图，后台再补全匹配数（与 WinForms RefreshWhitelist 同流程） ——
        public void Refresh(bool deep)
        {
            List<WhitelistRuleView> fast = SafeGetFast();
            Fill(fast);
            if (!deep) return;
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                List<WhitelistRuleView> full = null;
                try { full = gm.GetWhitelistRules(); }
                catch { }
                Interlocked.Exchange(ref busy, 0);
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (full != null) Fill(full);
                }));
            });
        }

        private List<WhitelistRuleView> SafeGetFast()
        {
            try { return gm.GetWhitelistRulesFast(); }
            catch { return new List<WhitelistRuleView>(); }
        }

        private void Fill(List<WhitelistRuleView> views)
        {
            string keepKey = selected != null ? selected.Key : null;
            Items.Clear();
            // 先放用户规则，后放必需规则（与 WinForms 一致的分组顺序）
            var user = new List<WhitelistRuleView>();
            var required = new List<WhitelistRuleView>();
            foreach (WhitelistRuleView v in views)
                (v.Required ? required : user).Add(v);

            userRuleCount = user.Count;
            WhitelistItemSelected reselect = null;
            foreach (WhitelistRuleView v in user)
            {
                var item = WhitelistItemSelected.Build(v);
                if (keepKey != null && item.Key == keepKey) reselect = item;
                Items.Add(item);
            }
            if (required.Count > 0)
            {
                Items.Add(new WhitelistItemSelected(
                    Lang.F("white.group.builtin", required.Count), true));
                foreach (WhitelistRuleView v in required)
                {
                    var item = WhitelistItemSelected.Build(v);
                    if (keepKey != null && item.Key == keepKey) reselect = item;
                    Items.Add(item);
                }
            }
            Selected = reselect;
            Raise("IsEmpty");
        }

        // —— 操作 ——
        // 批量加入文件（拖放或浏览）。返回 null 表示全部成功，否则返回首个错误。
        public string AddFiles(IEnumerable<string> files)
        {
            int added = 0;
            string firstError = null;
            foreach (string raw in files)
            {
                string path = ResolveWhitelistTarget(raw);
                if (string.IsNullOrEmpty(path)) continue;
                bool ok;
                try { ok = gm.AddWhitelistAuto(path); }
                catch { ok = false; }
                if (ok) added++;
                else if (firstError == null) firstError = gm.WhitelistLastError;
            }
            if (added > 0) Refresh(true);
            return added == 0 ? firstError : null;
        }

        // 与 WinForms ResolveWhitelistTarget 相同：解析 .lnk，要求结尾是 .exe。
        internal static string ResolveWhitelistTarget(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string path = raw.Trim().Trim('"');
            try
            {
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string resolved = GameExecutableResolver.ResolveShortcut(path);
                    if (!string.IsNullOrEmpty(resolved)) path = resolved;
                }
            }
            catch { }
            return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        // 删除当前选中（必需规则拒绝）
        public bool RemoveSelected(out string error)
        {
            error = null;
            if (selected == null || selected.Required) return false;
            bool ok;
            try { ok = gm.RemoveWhitelistRule(selected.Key); }
            catch { ok = false; }
            if (!ok) error = gm.WhitelistLastError;
            Refresh(true);
            return ok;
        }

        // 缩窄：family → exact
        public bool NarrowSelected(out string error)
        {
            error = null;
            if (selected == null || selected.Kind != WhitelistRuleKind.ApplicationFamily) return false;
            bool ok;
            try { ok = gm.NarrowWhitelistRule(selected.Key); }
            catch { ok = false; }
            if (!ok) error = gm.WhitelistLastError;
            Refresh(true);
            return ok;
        }

        // 扩展：exact → family
        public bool WidenSelected(out string error)
        {
            error = null;
            if (selected == null || selected.Kind != WhitelistRuleKind.ExactPath) return false;
            bool ok;
            try { ok = gm.WidenWhitelistRule(selected.Key); }
            catch { ok = false; }
            if (!ok) error = gm.WhitelistLastError;
            Refresh(true);
            return ok;
        }

        // 重置为预设
        public bool Reset(out string error)
        {
            error = null;
            bool ok;
            try { ok = gm.ResetWhitelist(); }
            catch { ok = false; }
            if (!ok) error = gm.WhitelistLastError;
            Refresh(true);
            return ok;
        }
    }

    // 白名单条目视图模型。isGroupHeader=true 时仅显示分组标题（其余字段为空）。
    internal sealed class WhitelistItemSelected : ViewModelBase
    {
        private readonly bool isGroupHeader;
        private readonly string groupTitle;
        private readonly WhitelistRuleView view;
        private string title;
        private string subtitle;
        private string badge;
        private string stateText;

        // 规则条目构造（Build 调用）
        private WhitelistItemSelected(WhitelistRuleView v)
        {
            view = v;
            Decorate();
        }

        // 分组标题构造
        public WhitelistItemSelected(string groupTitle, bool isGroupHeader)
        {
            this.isGroupHeader = isGroupHeader;
            this.groupTitle = groupTitle;
        }

        public static WhitelistItemSelected Build(WhitelistRuleView v)
        {
            return new WhitelistItemSelected(v);
        }

        public bool IsGroupHeader { get { return isGroupHeader; } }
        public string GroupTitle { get { return groupTitle ?? ""; } }
        public string Key { get { return view != null ? view.Rule.Key : null; } }
        public string Value { get { return view != null ? view.Rule.Value : null; } }
        public WhitelistRuleKind Kind { get { return view != null ? view.Rule.Kind : WhitelistRuleKind.LegacyName; } }
        public bool Required { get { return view != null && view.Required; } }
        public int CurrentMatches { get { return view != null ? view.CurrentMatches : 0; } }
        public string InspectorValue { get { return string.IsNullOrEmpty(Value) ? "—" : Value; } }
        public string RuleTypeText
        {
            get
            {
                if (Kind == WhitelistRuleKind.ApplicationFamily) return Lang.T("white.badge.family");
                if (Kind == WhitelistRuleKind.ExactPath) return Lang.T("white.badge.only");
                return Lang.T("white.badge.name");
            }
        }
        public string MatchCountText
        {
            get
            {
                if (Required) return Lang.T("white.required.badge");
                if (CurrentMatches < 0) return Lang.T("white.matches.pending");
                return CurrentMatches + " 个运行进程";
            }
        }
        public string LockReason
        {
            get { return Required ? Lang.T("white.required.locked") : ""; }
        }

        public string Title { get { return title; } set { SetProperty(ref title, value, "Title"); } }
        public string Subtitle { get { return subtitle; } set { SetProperty(ref subtitle, value, "Subtitle"); } }
        public string Badge { get { return badge; } set { SetProperty(ref badge, value, "Badge"); } }
        public string StateText { get { return stateText; } set { SetProperty(ref stateText, value, "StateText"); } }

        // 是否在跑（用于状态点颜色）
        public bool IsLive { get { return !Required && CurrentMatches > 0; } }

        // 与 WinForms Decorate 等价：根据规则填充显示字段
        private void Decorate()
        {
            WhitelistRule rule = view.Rule;
            Title = WhitelistTitle(rule);
            Subtitle = rule.Kind == WhitelistRuleKind.LegacyName
                ? Lang.T("white.badge.name.sub") : rule.Value;
            Badge = rule.Kind == WhitelistRuleKind.ApplicationFamily
                ? Lang.T("white.badge.family")
                : rule.Kind == WhitelistRuleKind.ExactPath
                    ? Lang.T("white.badge.only") : Lang.T("white.badge.name");
            StateText = view.Required
                ? Lang.T("white.required.badge")
                : view.CurrentMatches < 0
                    ? Lang.T("white.matches.pending")
                    : view.CurrentMatches > 0
                        ? Lang.F("white.state.running", view.CurrentMatches)
                        : Lang.T("white.state.idle");
        }

        // 与 WinForms WhitelistTitle 等价：优先 FileVersionInfo，回退文件名。
        private static string WhitelistTitle(WhitelistRule rule)
        {
            if (rule.Kind == WhitelistRuleKind.LegacyName) return rule.Value;
            string key = rule.Value;
            string title = null;
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(key);
                title = string.IsNullOrEmpty(info.FileDescription) ? info.ProductName : info.FileDescription;
                if (!string.IsNullOrEmpty(title)) title = title.Trim();
            }
            catch { }
            if (string.IsNullOrEmpty(title))
            {
                try { title = Path.GetFileNameWithoutExtension(key); }
                catch { title = key; }
            }
            return title;
        }
    }
}
