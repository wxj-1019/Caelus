// @author zenjiro 18967498922@163.com
// 文件用途 构建概览页 核心动画 守护状态与仪表盘图块

using System;
using System.Drawing;
using System.Windows.Forms;

namespace CaelusApp
{
    internal partial class PanelForm
    {
        private Toggle swGame;
        private CaelusCore caelusCore;
        private StatusDot statusDot;
        private Label lblStatus;
        private Label lblOverviewBoost, lblEvidenceLive;
        private DeviceSpecBar deviceBar;
        private Label lblHeroMode, lblHeroSource;
        private DashboardTile tileGame, tileDev, tileDaily;

        private void BuildOverviewPage()
        {
            int y = PageHeader(pageOverview, Lang.T("nav.overview"), Lang.T("v15.overview.sub"), 2);
            const int coreW = 360, coreH = 352, gap = 16;
            int rightX = ContentX + coreW + gap;
            int rightW = ContentW - coreW - gap;

            caelusCore = new CaelusCore();
            caelusCore.SetBounds(Theme.S(ContentX), Theme.S(y), Theme.S(coreW), Theme.S(coreH));
            caelusCore.SetState(gameMode.ActivePreset, gameMode.Enabled, gameMode.IsActive);
            pageOverview.Controls.Add(caelusCore);

            var guard = MakeConsolePanel(pageOverview, rightX, y, rightW, 120, true);
            CardLabel(guard, Lang.T("v15.guard.state"), 18, 12, rightW - 92, 18, 7.8f, true, Theme.Faint);
            statusDot = new StatusDot(); statusDot.SetBounds(Theme.S(15), Theme.S(41), Theme.S(22), Theme.S(22));
            statusDot.Bg = Theme.Card; statusDot.Color = Theme.Dim;

            lblStatus = CardLabel(guard, "…", 47, 30, rightW - 114, 44, 9.2f, true, Theme.Fg);
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            swGame = MakeSwitch(gameMode.Enabled, delegate
            {
                gameMode.Enabled = swGame.Checked;
                Settings.Save("GameModeOn", swGame.Checked);
                UpdateModePresentation(true);
            });
            swGame.Bg = Theme.Card; swGame.Location = new Point(Theme.S(rightW - 66), Theme.S(14));
            CardLabel(guard, Lang.T("v15.master.short"), 18, 72, rightW - 36, 34, 7.7f, false, Theme.Dim);
            guard.Controls.AddRange(new Control[] { statusDot, swGame });

            var mode = MakeConsolePanel(pageOverview, rightX, y + 132, rightW, 100, false);
            CardLabel(mode, Lang.T("v15.effective.mode"), 18, 12, rightW - 36, 17, 7.6f, true, Theme.Faint);
            lblHeroMode = CardLabel(mode, ModeButton.ModeName(gameMode.ActivePreset), 18, 31, rightW - 36, 31, 14.5f, true, Theme.Accent);
            lblHeroSource = CardLabel(mode, Lang.T("mode.source.global"), 18, 66, rightW - 36, 18, 7.7f, false, Theme.Dim);

            var boost = MakeConsolePanel(pageOverview, rightX, y + 244, rightW, 108, false);
            CardLabel(boost, Lang.T("v14.boost.status"), 18, 13, rightW - 36, 18, 7.7f, true, Theme.Faint);
            lblOverviewBoost = CardLabel(boost, "…", 18, 33, rightW - 36, 72, 10.2f, false, Theme.Fg);

            int tileY = y + coreH + 10;
            int tileW = (ContentW - 28) / 3;
            tileGame = MakeDashboardTile(pageOverview, ContentX, tileY, tileW, "游戏", "…", "game", 1);
            tileDev = MakeDashboardTile(pageOverview, ContentX + tileW + 14, tileY, tileW, "开发专注", "…", "chip", 2);
            tileDaily = MakeDashboardTile(pageOverview, ContentX + (tileW + 14) * 2, tileY, tileW, "日常优化", "…", "sun", 3);
            RefreshScenarioTiles();

            int topologyY = tileY + 80;
            var topology = MakeConsolePanel(pageOverview, ContentX, topologyY, ContentW, 68, false);
            CardLabel(topology, Lang.T("v14.cpu.topology"), 18, 10, ContentW - 36, 17, 7.7f, true, Theme.Faint);
            lblEvidenceLive = CardLabel(topology, CpuTopologySummary(), 18, 30, ContentW - 36, 27, 9.5f, false, Theme.Fg);
            lblEvidenceLive.Text = CpuTopologySummary();

            deviceBar = new DeviceSpecBar();
            deviceBar.SetBounds(Theme.S(ContentX), Theme.S(topologyY + 74), Theme.S(ContentW), Theme.S(66));
            pageOverview.Controls.Add(deviceBar);
            LoadDeviceInfoAsync();
            UpdateModePresentation(false);
        }

        private void LoadDeviceInfoAsync()
        {
            string[] fast;
            try { fast = DeviceInfo.Specs(); }
            catch { fast = new[] { "—", "—", "—", "—" }; }
            deviceBar.SetValues(fast);
            if (fast[1] != "—") return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string[] full;
                try { full = DeviceInfo.SpecsWithSlowFallback(); }
                catch { return; }
                try
                {
                    if (!IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed || deviceBar == null) return;
                        deviceBar.SetValues(full);
                    });
                }
                catch { }
            });
        }

        private DashboardTile MakeDashboardTile(Control parent, int x, int y, int w, string title, string detail, string glyph, int channel)
        {
            var tile = new DashboardTile();
            tile.SetBounds(Theme.S(x), Theme.S(y), Theme.S(w), Theme.S(70));
            tile.Bg = Theme.Bg;
            tile.Title = title;
            tile.Detail = detail;
            tile.Glyph = glyph;
            tile.Channel = channel;
            parent.Controls.Add(tile);
            return tile;
        }

        /// <summary>三场景状态条：颜色 = 关闭(灰) / 待机(弱) / 活跃等待(强调) / 掌权(绿)。</summary>
        private void RefreshScenarioTiles()
        {
            if (tileGame == null) return;

            bool gameOn = gameMode != null && gameMode.Enabled;
            bool gameAct = gameOn && gameMode != null && gameMode.IsActive;
            string gameDetail = gameAct ? "游戏本体保护中，后台已让路"
                : (gameOn ? "待机 · 等待游戏启动" : "已关闭 · 不参与仲裁");
            SetScenarioTile(tileGame, gameOn, gameAct, grantedScenario == ScenarioKind.Game, gameDetail);

            bool devOn = Settings.Load("DevModeOn", true);
            bool devAct = devOn && devFocus != null && devFocus.IsActive;
            string devDetail;
            if (!devOn) devDetail = "已关闭 · 不参与仲裁";
            else if (devAct && devFocus.FocusModeOn) devDetail = "专注模式开启 · 等待掌权";
            else if (devAct) devDetail = "检测到编译或 IDE · 等待掌权";
            else devDetail = "待机 · 等待编译 / IDE / 专注模式";
            SetScenarioTile(tileDev, devOn, devAct, grantedScenario == ScenarioKind.DevFocus, devDetail);

            bool dailyOn = Settings.Load("DailyCareOn", true);
            bool dailyAct = dailyOn && dailyCare != null && dailyCare.IsActive;
            string dailyDetail;
            if (!dailyOn) dailyDetail = "已关闭 · 不参与仲裁";
            else if (dailyAct) dailyDetail = "日常家族活跃或电池供电 · 等待掌权";
            else dailyDetail = "待机 · 等待浏览器 / Office / 电池触发";
            SetScenarioTile(tileDaily, dailyOn, dailyAct, grantedScenario == ScenarioKind.DailyCare, dailyDetail);
        }

        private void SetScenarioTile(DashboardTile tile, bool on, bool active, bool granted, string detail)
        {
            string prefix = granted ? "掌权中 · " : "";
            if (tile.Detail != prefix + detail) tile.Detail = prefix + detail;
            Color color = !on ? Theme.Dim
                : granted ? Theme.Green
                : active ? Theme.Accent
                : Theme.Faint;
            if (tile.StatusColor != color) tile.StatusColor = color;
            tile.Invalidate();
        }

        private string CpuTopologySummary()
        {
            if (CpuTopology.MultiGroup) return Lang.T("v14.cpu.multigroup");
            if (CpuTopology.Hybrid) return Lang.T("v14.cpu.hybrid");
            if (CpuTopology.AsymCache) return Lang.T("v14.cpu.x3d");
            return Lang.F("v14.cpu.generic", Environment.ProcessorCount);
        }

        private void RefreshBoostPresentation()
        {
            if (lblOverviewBoost == null) return;
            string text = gameMode.BoostStatusText;
            if (lblOverviewBoost.Text != text) lblOverviewBoost.Text = text;
            lblOverviewBoost.ForeColor = gameMode.BoostStateVerified ? Theme.Green
                : (gameMode.BoostHandleProtected ? Theme.Dim : Theme.Fg);
        }

    }
}
