// @author zenjiro 18967498922@163.com
// 文件用途 WPF 显卡页 ViewModel：逐游戏 NV 项、会话项、呈现项、AMD 项的开关与档位

using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class GraphicsViewModel : ViewModelBase
    {
        private readonly GameMode gameMode;
        private bool amdCacheBusy;
        private string amdCacheFeedback = string.Empty;
        private bool amdCacheError;

        public GraphicsViewModel(GameMode gameMode) { this.gameMode = gameMode; }

        // —— 标题区 ——
        public string PageTitle { get { return Lang.T("nav.graphics"); } }
        public string PageSub { get { return Lang.T("v16.graphics.sub"); } }

        // —— 分组标题 ——
        public string PerGameTitle { get { return "通用逐游戏"; } }
        public string NvidiaTitle { get { return "NVIDIA 驱动"; } }
        public string SessionTitle { get { return Lang.T("sec.gfx.session"); } }
        public string PresentTitle { get { return "Windows 呈现"; } }
        public string AmdTitle { get { return Lang.T("sec.amd"); } }

        // —— 可用性 ——
        public bool NvAvailable { get { return NvApi.Available; } }
        public bool NvUnavailable { get { return !NvAvailable; } }
        public bool DlssAvailable { get { return NvApi.Available && NvDrsTweaks.DlssOverrideSupported(); } }
        public bool AmdAvailable { get { return AdlxTweaks.Available; } }
        public bool AmdUnavailable { get { return !AmdAvailable; } }

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

        // —— 通用逐游戏项 ——
        public bool GpuHighPerf
        {
            get { return gameMode.GpuHighPerf; }
            set
            {
                if (gameMode.GpuHighPerf == value) return;
                gameMode.GpuHighPerf = value;
                NotifyToggle("GpuHighPerf");
            }
        }
        public string GpuTitle { get { return Lang.T("set.gpu"); } }
        public string GpuNote { get { return Lang.T("set.gpu.n"); } }
        public bool DisableFso
        {
            get { return gameMode.DisableFso; }
            set
            {
                if (gameMode.DisableFso == value) return;
                gameMode.DisableFso = value;
                NotifyToggle("DisableFso");
            }
        }
        public string FsoTitle { get { return Lang.T("set.fso"); } }
        public string FsoNote { get { return Lang.T("set.fso.n"); } }

        // —— NV 驱动项（仅在 NV 可用时启用）——
        public bool NvMaxPerf
        {
            get { return gameMode.NvMaxPerf; }
            set
            {
                if (gameMode.NvMaxPerf == value) return;
                gameMode.NvMaxPerf = value;
                NotifyToggle("NvMaxPerf");
            }
        }
        public string NvMaxTitle { get { return Lang.T("set.nvmax"); } }
        public string NvMaxNote { get { return NvAvailable ? Lang.T("set.nvmax.n") : Lang.T("set.nv.none"); } }

        public bool NvLowLatency
        {
            get { return gameMode.NvLowLatency; }
            set
            {
                if (gameMode.NvLowLatency == value) return;
                gameMode.NvLowLatency = value;
                NotifyToggle("NvLowLatency");
            }
        }
        public string NvLowLatTitle { get { return Lang.T("set.nvll"); } }
        public string NvLowLatNote { get { return NvAvailable ? Lang.T("set.nvll.n") : Lang.T("set.nv.none"); } }

        public int NvFrlIndex
        {
            get { return FrlIndexOf(gameMode.NvFrlMode); }
            set
            {
                string mode = FrlModeOf(value);
                if (gameMode.NvFrlMode == mode) return;
                gameMode.NvFrlMode = mode;
                Raise("NvFrlIndex");
                Raise("NvFrlLabel");
            }
        }
        public string NvFrlTitle { get { return Lang.T("set.nvfrl"); } }
        public string NvFrlNote { get { return NvAvailable ? Lang.T("set.nvfrl.n") : Lang.T("set.nv.none"); } }

        public int NvDlssIndex
        {
            get { return DlssIndexOf(gameMode.NvDlssMode); }
            set
            {
                string mode = DlssModeOf(value);
                if (gameMode.NvDlssMode == mode) return;
                gameMode.NvDlssMode = mode;
                Raise("NvDlssIndex");
                Raise("NvDlssLabel");
            }
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
            set
            {
                if (gameMode.NvRebar == value) return;
                gameMode.NvRebar = value;
                NotifyToggle("NvRebar");
            }
        }
        public string NvRebarTitle { get { return Lang.T("set.nvrebar"); } }
        private string rebarNoteCache;
        public string NvRebarNote
        {
            get
            {
                // 与旧 WinForms 一致：说明中追加 RebarProbe 硬件检测结果（是否开启/窗口大小）
                if (!NvAvailable) return Lang.T("set.nv.none");
                if (rebarNoteCache == null)
                {
                    string text = Lang.T("set.nvrebar.n");
                    bool rebarOn;
                    ulong rebarWindow;
                    string rebarGpu;
                    try
                    {
                        if (RebarProbe.TryDetect(out rebarOn, out rebarWindow, out rebarGpu))
                            text += Lang.F(rebarOn ? "set.nvrebar.det.on" : "set.nvrebar.det.off",
                                RebarProbe.WindowText(rebarWindow));
                    }
                    catch { }
                    rebarNoteCache = text;
                }
                return rebarNoteCache;
            }
        }

        public bool NvAnselOff
        {
            get { return gameMode.NvAnselOff; }
            set
            {
                if (gameMode.NvAnselOff == value) return;
                gameMode.NvAnselOff = value;
                NotifyToggle("NvAnselOff");
            }
        }
        public string NvAnselTitle { get { return Lang.T("set.nvansel"); } }
        public string NvAnselNote { get { return NvAvailable ? Lang.T("set.nvansel.n") : Lang.T("set.nv.none"); } }

        public bool NvBattFull
        {
            get { return gameMode.NvBattFull; }
            set
            {
                if (gameMode.NvBattFull == value) return;
                gameMode.NvBattFull = value;
                NotifyToggle("NvBattFull");
            }
        }
        public string NvBattTitle { get { return Lang.T("set.nvbatt"); } }
        public string NvBattNote { get { return NvAvailable ? Lang.T("set.nvbatt.n") : Lang.T("set.nv.none"); } }

        // —— 会话项 ——
        public bool NvBgFrl
        {
            get { return gameMode.NvBgFrl; }
            set
            {
                if (gameMode.NvBgFrl == value) return;
                gameMode.NvBgFrl = value;
                NotifyToggle("NvBgFrl");
            }
        }
        public string NvBgTitle { get { return Lang.T("set.nvbg"); } }
        public string NvBgNote { get { return NvAvailable ? Lang.T("set.nvbg.n") : Lang.T("set.nv.none"); } }

        // —— Windows 呈现项 ——
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
                NotifyEnabledCount();
            }
        }
        public string WindowedOptTitle { get { return Lang.T("set.winopt"); } }
        public string WindowedOptNote { get { return Lang.T("set.winopt.n"); } }

        // —— AMD 项 ——
        public bool AmdAntiLag
        {
            get { return gameMode.AmdAntiLag; }
            set
            {
                if (gameMode.AmdAntiLag == value) return;
                gameMode.AmdAntiLag = value;
                NotifyToggle("AmdAntiLag");
            }
        }
        public string AmdAlagTitle { get { return Lang.T("set.alag"); } }
        public string AmdAlagNote { get { return AmdAvailable ? Lang.T("set.alag.n") : Lang.T("set.amd.none"); } }

        public int AmdChillIndex
        {
            get { return FrlIndexOf(gameMode.AmdChillMode); }
            set
            {
                string mode = FrlModeOf(value);
                if (gameMode.AmdChillMode == mode) return;
                gameMode.AmdChillMode = mode;
                Raise("AmdChillIndex");
                Raise("AmdChillLabel");
            }
        }
        public string AmdChillTitle { get { return Lang.T("set.achill"); } }
        public string AmdChillNote { get { return AmdAvailable ? Lang.T("set.achill.n") : Lang.T("set.amd.none"); } }

        public bool AmdEnhSync
        {
            get { return gameMode.AmdEnhSync; }
            set
            {
                if (gameMode.AmdEnhSync == value) return;
                gameMode.AmdEnhSync = value;
                NotifyToggle("AmdEnhSync");
            }
        }
        public string AmdEsyncTitle { get { return Lang.T("set.aesync"); } }
        public string AmdEsyncNote { get { return AmdAvailable ? Lang.T("set.aesync.n") : Lang.T("set.amd.none"); } }

        public bool AmdRis
        {
            get { return gameMode.AmdRis; }
            set
            {
                if (gameMode.AmdRis == value) return;
                gameMode.AmdRis = value;
                NotifyToggle("AmdRis");
            }
        }
        public string AmdRisTitle { get { return Lang.T("set.aris"); } }
        public string AmdRisNote { get { return AmdAvailable ? Lang.T("set.aris.n") : Lang.T("set.amd.none"); } }

        // —— AMD 着色器缓存重置状态 ——
        public string AmdCacheTitle { get { return Lang.T("set.acache"); } }
        public string AmdCacheNote { get { return AmdAvailable ? Lang.T("set.acache.n") : Lang.T("set.amd.none"); } }
        public bool AmdCacheBusy { get { return amdCacheBusy; } }
        public bool AmdCacheCanRun { get { return AmdAvailable && !amdCacheBusy; } }
        public string AmdCacheBtnText { get { return amdCacheBusy ? "正在重置…" : Lang.T("amd.cache.btn"); } }
        public string AmdCacheFeedback { get { return amdCacheFeedback; } }
        public bool HasAmdCacheFeedback { get { return !string.IsNullOrEmpty(amdCacheFeedback); } }
        public bool AmdCacheError { get { return amdCacheError; } }

        internal bool BeginAmdCacheReset()
        {
            if (!AmdCacheCanRun) return false;
            amdCacheBusy = true;
            amdCacheFeedback = "正在清理着色器缓存，请稍候…";
            amdCacheError = false;
            NotifyAmdCacheState();
            return true;
        }

        internal void CompleteAmdCacheReset(bool ok)
        {
            amdCacheBusy = false;
            amdCacheError = !ok;
            amdCacheFeedback = ok ? Lang.T("amd.cache.done") : Lang.T("amd.cache.fail");
            NotifyAmdCacheState();
        }

        private void NotifyAmdCacheState()
        {
            Raise("AmdCacheBusy");
            Raise("AmdCacheCanRun");
            Raise("AmdCacheBtnText");
            Raise("AmdCacheFeedback");
            Raise("HasAmdCacheFeedback");
            Raise("AmdCacheError");
        }

        // —— 顶部摘要：分段当前值回显 + 启用计数 + 厂商检测 ——
        public string NvFrlLabel { get { return FrlLabels[NvFrlIndex]; } }
        public string NvDlssLabel { get { return DlssLabels[NvDlssIndex]; } }
        public string AmdChillLabel { get { return FrlLabels[AmdChillIndex]; } }
        public int TotalCount { get { return 12; } }
        public int EnabledCount
        {
            get
            {
                int n = 0;
                if (GpuHighPerf) n++;
                if (DisableFso) n++;
                if (NvMaxPerf) n++;
                if (NvLowLatency) n++;
                if (NvRebar) n++;
                if (NvAnselOff) n++;
                if (NvBattFull) n++;
                if (NvBgFrl) n++;
                if (WindowedOpt) n++;
                if (AmdAntiLag) n++;
                if (AmdEnhSync) n++;
                if (AmdRis) n++;
                return n;
            }
        }
        public string EnabledSummaryText
        {
            get { return TotalCount + " 个开关中 " + EnabledCount + " 个开启"; }
        }
        public string SummaryAutomationName { get { return "显卡优化状态：" + EnabledSummaryText; } }
        public string NvStatusText { get { return NvAvailable ? "NVIDIA 可用" : "NVIDIA 不可用"; } }
        public string AmdStatusText { get { return AmdAvailable ? "AMD 可用" : "AMD 不可用"; } }
        public string NvUnavailableText { get { return "未检测到可用的 NVIDIA 驱动，NVIDIA 与相关会话设置已收起。"; } }
        public string AmdUnavailableText { get { return "未检测到可用的 AMD 驱动，AMD 详细设置已收起。"; } }

        private void NotifyToggle(string propertyName)
        {
            Raise(propertyName);
            NotifyEnabledCount();
        }

        private void NotifyEnabledCount()
        {
            Raise("EnabledCount");
            Raise("EnabledSummaryText");
            Raise("SummaryAutomationName");
        }

        // 分段切换后由视图调用，兼容现有视图事件并刷新值回显。
        internal void NotifySegments()
        {
            Raise("NvFrlLabel");
            Raise("NvDlssLabel");
            Raise("AmdChillLabel");
            NotifyEnabledCount();
        }

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
