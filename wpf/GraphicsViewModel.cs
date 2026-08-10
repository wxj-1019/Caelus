// @author zenjiro 18967498922@163.com
// 文件用途 WPF 显卡页 ViewModel：逐游戏 NV 项、会话项、呈现项、AMD 项的开关与档位

using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class GraphicsViewModel : ViewModelBase
    {
        private readonly GameMode gameMode;

        public GraphicsViewModel(GameMode gameMode) { this.gameMode = gameMode; }

        // —— 标题区 ——
        public string PageTitle { get { return Lang.T("nav.graphics"); } }
        public string PageSub { get { return Lang.T("v16.graphics.sub"); } }

        // —— 分组标题 ——
        public string PerGameTitle { get { return Lang.T("sec.pergame"); } }
        public string SessionTitle { get { return Lang.T("sec.gfx.session"); } }
        public string PresentTitle { get { return Lang.T("sec.graphics.present"); } }
        public string AmdTitle { get { return Lang.T("sec.amd"); } }

        // —— 可用性 ——
        public bool NvAvailable { get { return NvApi.Available; } }
        public bool DlssAvailable { get { return NvApi.Available && NvDrsTweaks.DlssOverrideSupported(); } }
        public bool AmdAvailable { get { return AdlxTweaks.Available; } }

        // —— 段标签 ——
        public List<string> FrlLabels
        {
            get
            {
                return new List<string> {
                    Lang.T("frl.off"), "60", "120", "240", Lang.T("frl.screen")
                };
            }
        }
        public List<string> DlssLabels
        {
            get
            {
                return new List<string> {
                    Lang.T("frl.off"), Lang.T("dlss.latest"), "J", "K"
                };
            }
        }

        // —— 逐游戏 NV 项（GPU 高性能 / FSO）——
        public bool GpuHighPerf
        {
            get { return gameMode.GpuHighPerf; }
            set { gameMode.GpuHighPerf = value; }
        }
        public string GpuTitle { get { return Lang.T("set.gpu"); } }
        public string GpuNote { get { return Lang.T("set.gpu.n"); } }
        public bool DisableFso
        {
            get { return gameMode.DisableFso; }
            set { gameMode.DisableFso = value; }
        }
        public string FsoTitle { get { return Lang.T("set.fso"); } }
        public string FsoNote { get { return Lang.T("set.fso.n"); } }

        // —— NV 驱动项（仅在 NV 可用时启用）——
        public bool NvMaxPerf
        {
            get { return gameMode.NvMaxPerf; }
            set { gameMode.NvMaxPerf = value; }
        }
        public string NvMaxTitle { get { return Lang.T("set.nvmax"); } }
        public string NvMaxNote { get { return NvAvailable ? Lang.T("set.nvmax.n") : Lang.T("set.nv.none"); } }

        public bool NvLowLatency
        {
            get { return gameMode.NvLowLatency; }
            set { gameMode.NvLowLatency = value; }
        }
        public string NvLowLatTitle { get { return Lang.T("set.nvll"); } }
        public string NvLowLatNote { get { return NvAvailable ? Lang.T("set.nvll.n") : Lang.T("set.nv.none"); } }

        // FRL 段
        public int NvFrlIndex
        {
            get { return FrlIndexOf(gameMode.NvFrlMode); }
            set { gameMode.NvFrlMode = FrlModeOf(value); }
        }
        public string NvFrlTitle { get { return Lang.T("set.nvfrl"); } }
        public string NvFrlNote { get { return NvAvailable ? Lang.T("set.nvfrl.n") : Lang.T("set.nv.none"); } }

        // DLSS 段
        public int NvDlssIndex
        {
            get { return DlssIndexOf(gameMode.NvDlssMode); }
            set { gameMode.NvDlssMode = DlssModeOf(value); }
        }
        public string NvDlssTitle { get { return Lang.T("set.nvdlss"); } }
        public string NvDlssNote
        {
            get
            {
                if (!NvAvailable) return Lang.T("set.nv.none");
                return DlssAvailable ? Lang.T("set.nvdlss.n") : Lang.T("set.nvdlss.old");
            }
        }

        public bool NvRebar
        {
            get { return gameMode.NvRebar; }
            set { gameMode.NvRebar = value; }
        }
        public string NvRebarTitle { get { return Lang.T("set.nvrebar"); } }
        public string NvRebarNote { get { return NvAvailable ? Lang.T("set.nvrebar.n") : Lang.T("set.nv.none"); } }

        public bool NvAnselOff
        {
            get { return gameMode.NvAnselOff; }
            set { gameMode.NvAnselOff = value; }
        }
        public string NvAnselTitle { get { return Lang.T("set.nvansel"); } }
        public string NvAnselNote { get { return NvAvailable ? Lang.T("set.nvansel.n") : Lang.T("set.nv.none"); } }

        public bool NvBattFull
        {
            get { return gameMode.NvBattFull; }
            set { gameMode.NvBattFull = value; }
        }
        public string NvBattTitle { get { return Lang.T("set.nvbatt"); } }
        public string NvBattNote { get { return NvAvailable ? Lang.T("set.nvbatt.n") : Lang.T("set.nv.none"); } }

        // —— 会话项 ——
        public bool NvBgFrl
        {
            get { return gameMode.NvBgFrl; }
            set { gameMode.NvBgFrl = value; }
        }
        public string NvBgTitle { get { return Lang.T("set.nvbg"); } }
        public string NvBgNote { get { return NvAvailable ? Lang.T("set.nvbg.n") : Lang.T("set.nv.none"); } }

        // —— 呈现项 ——
        public bool WindowedOpt
        {
            get { return WindowedOptTweak.EnabledByCaelus || WindowedOptTweak.CurrentlyOn(); }
            set
            {
                bool ok = value ? WindowedOptTweak.Enable() : WindowedOptTweak.Restore();
                if (!ok)
                {
                    System.Windows.MessageBox.Show(Lang.T("winopt.failed"), "Caelus",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
                Raise("WindowedOpt");
            }
        }
        public string WindowedOptTitle { get { return Lang.T("set.winopt"); } }
        public string WindowedOptNote { get { return Lang.T("set.winopt.n"); } }

        // —— AMD 项 ——
        public bool AmdAntiLag
        {
            get { return gameMode.AmdAntiLag; }
            set { gameMode.AmdAntiLag = value; }
        }
        public string AmdAlagTitle { get { return Lang.T("set.alag"); } }
        public string AmdAlagNote { get { return AmdAvailable ? Lang.T("set.alag.n") : Lang.T("set.amd.none"); } }

        public int AmdChillIndex
        {
            get { return FrlIndexOf(gameMode.AmdChillMode); }
            set { gameMode.AmdChillMode = FrlModeOf(value); }
        }
        public string AmdChillTitle { get { return Lang.T("set.achill"); } }
        public string AmdChillNote { get { return AmdAvailable ? Lang.T("set.achill.n") : Lang.T("set.amd.none"); } }

        public bool AmdEnhSync
        {
            get { return gameMode.AmdEnhSync; }
            set { gameMode.AmdEnhSync = value; }
        }
        public string AmdEsyncTitle { get { return Lang.T("set.aesync"); } }
        public string AmdEsyncNote { get { return AmdAvailable ? Lang.T("set.aesync.n") : Lang.T("set.amd.none"); } }

        public bool AmdRis
        {
            get { return gameMode.AmdRis; }
            set { gameMode.AmdRis = value; }
        }
        public string AmdRisTitle { get { return Lang.T("set.aris"); } }
        public string AmdRisNote { get { return AmdAvailable ? Lang.T("set.aris.n") : Lang.T("set.amd.none"); } }

        // —— AMD 着色器缓存重置按钮 ——
        public string AmdCacheTitle { get { return Lang.T("set.acache"); } }
        public string AmdCacheNote { get { return AmdAvailable ? Lang.T("set.acache.n") : Lang.T("set.amd.none"); } }
        public string AmdCacheBtnText { get { return Lang.T("amd.cache.btn"); } }

        // —— 索引 ↔ 模式字符串映射（与 WinForms 一致）——
        public static int FrlIndexOf(string mode)
        {
            return mode == "60" ? 1 : mode == "120" ? 2 : mode == "240" ? 3
                : mode == "screen" ? 4 : 0;
        }
        public static string FrlModeOf(int index)
        {
            return index == 1 ? "60" : index == 2 ? "120" : index == 3 ? "240"
                : index == 4 ? "screen" : "off";
        }
        public static int DlssIndexOf(string mode)
        {
            return mode == "latest" ? 1 : mode == "j" ? 2 : mode == "k" ? 3 : 0;
        }
        public static string DlssModeOf(int index)
        {
            return index == 1 ? "latest" : index == 2 ? "j" : index == 3 ? "k" : "off";
        }
    }
}
