// @author zenjiro 18967498922@163.com
// 文件用途 WPF 系统体检页：4 个触发按钮 + 扫描中的进度估算驱动

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CaelusApp.WpfHost.Views
{
    public partial class AuditView : UserControl
    {
        // 预览探针（--wpf-shot）注入代表性结果；生产永不置 true。
        internal static bool InjectSampleData;

        private AuditViewModel vm;
        private DispatcherTimer progressTimer;
        private Stopwatch progressClock;
        private int progressTotalMs;
        private int displayedState = -1;

        public AuditView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AuditViewModel next = DataContext as AuditViewModel;
            if (vm != next)
            {
                DetachViewModel();
                vm = next;
                if (vm != null) vm.PropertyChanged += OnVmPropertyChanged;
            }
            displayedState = -1;
            UpdateStateVisibility(vm != null && vm.HasResult);

        }

        // 预览探针（--wpf-shot）显式调用：填充代表性结果，捕获结果态实机图。
        // 不依赖 OnLoaded 的静态标志时序（探针下多窗口串扰不可靠）。
        internal void ApplySampleResult()
        {
            AuditViewModel m = DataContext as AuditViewModel;
            if (m == null || m.CapabilityRows.Count > 0) return;
            m.CapabilityRows.Add(new AuditRowView("CPU 虚拟化", "已启用", "", "cpu_feature: svm", false));
            m.CapabilityRows.Add(new AuditRowView("硬件虚拟化 VT-x", "已启用", "", "cpu_feature: vmx", false));
            m.CapabilityRows.Add(new AuditRowView("系统盘类型", "NVMe SSD", "", "physicaldrive0 · NVMe", false));
            m.MachineRows.Add(new AuditRowView("处理器", "AMD Ryzen 7 5800X", "8 核 16 线程 · 3.8 GHz", "cpu", false));
            m.MachineRows.Add(new AuditRowView("图形设备", "NVIDIA RTX 4070", "12 GB VRAM · 驱动 566.14", "gpu", false));
            m.MachineRows.Add(new AuditRowView("内存", "32 GB DDR4", "3200 MHz · 双通道", "mem", false));
            m.PersistentRows.Add(new AuditRowView("Xbox Game Bar", "常驻", "游戏覆盖会触发全屏钩子，建议关闭。", "process: GameBar.exe", true));
            m.PersistentRows.Add(new AuditRowView("GameDVR 后台录制", "常驻", "持续占用磁盘与编码资源，竞技场景建议禁用。", "service: BcastDVRUserService", true));
            m.PersistentRows.Add(new AuditRowView("MSI Afterburner", "常驻", "", "process: MSIAfterburner.exe", false));
            m.PersistentRows.Add(new AuditRowView("Riot Client", "常驻", "", "process: RiotClientServices.exe", false));
            m.VerdictRows.Add(new AuditRowView("后台占用峰值", "偏高 18%", "可在优化策略中开启后台进程压制。", "peak_cpu: 18.4%", true));
            m.VerdictRows.Add(new AuditRowView("综合评估", "良好", "硬件能力充足，按建议处置后可达 90+。", "score: 82/100", false));
            m.Progress = 1;
            m.State = AuditState.Result;
            m.NotifyHealth();
            // Loaded 异步派发，PropertyChanged 监听器此时可能尚未挂上——直接同步面板可见性
            IdlePanel.Visibility = System.Windows.Visibility.Collapsed;
            ScanningPanel.Visibility = System.Windows.Visibility.Collapsed;
            ResultPanel.Visibility = System.Windows.Visibility.Visible;
            UpdateLayout();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopProgressTimer();
            DetachViewModel();
            vm = null;
            displayedState = -1;
        }

        private void DetachViewModel()
        {
            if (vm != null) vm.PropertyChanged -= OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsIdle"
                || e.PropertyName == "IsScanning"
                || e.PropertyName == "HasResult")
                UpdateStateVisibility(true);
        }

        private void UpdateStateVisibility(bool reveal)
        {
            int state = -1;
            if (vm != null)
            {
                if (vm.IsIdle) state = 0;
                else if (vm.IsScanning) state = 1;
                else if (vm.HasResult) state = 2;
            }

            bool idle = state == 0;
            bool scanning = state == 1;
            bool result = state == 2;
            bool changed = displayedState != state;

            IdlePanel.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
            IdlePanel.IsHitTestVisible = idle;
            ScanningPanel.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
            ScanningPanel.IsHitTestVisible = scanning;
            ResultPanel.Visibility = result ? Visibility.Visible : Visibility.Collapsed;
            ResultPanel.IsHitTestVisible = result;

            displayedState = state;
            if (!reveal || !changed) return;
            if (idle) Motion.Reveal(IdlePanel);
            else if (scanning) Motion.Reveal(ScanningPanel);
            else if (result)
            {
                Motion.RiseIn(ZoneHealth, 40);
                Motion.RiseIn(ZoneMetrics, 100);
                Motion.RiseIn(ZoneCapability, 160);
                Motion.RiseIn(ZoneMachine, 220);
                Motion.RiseIn(ZonePersistent, 280);
                Motion.RiseIn(ZoneVerdict, 340);
            }
        }

        // 空闲态的「开始体检」按钮 = 快速体检（与 WinForms 一致）
        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            StartWithProgress(AuditViewModel.QuickWindowMs, delegate { vm.StartAudit(AuditViewModel.QuickWindowMs); });
        }

        private void OnQuickClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            StartWithProgress(AuditViewModel.QuickWindowMs, delegate { vm.StartAudit(AuditViewModel.QuickWindowMs); });
        }

        private void OnPreciseClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            StartWithProgress(AuditViewModel.PreciseWindowMs, delegate { vm.StartAudit(AuditViewModel.PreciseWindowMs); });
        }

        private void OnNvClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            StartWithProgress(AuditViewModel.QuickWindowMs, vm.StartNvProbe);
        }

        private void OnAmdClick(object sender, RoutedEventArgs e)
        {
            if (vm == null) return;
            StartWithProgress(AuditViewModel.QuickWindowMs, vm.StartAmdProbe);
        }

        // 启动扫描并开一个 DispatcherTimer 估算进度（仅视觉反馈，不影响后端）
        // 与 WinForms BeginAuditProgress 同一公式：totalMs = window + 边距 + 900。
        private void StartWithProgress(int windowMs, Action trigger)
        {
            StopProgressTimer();
            try { trigger(); }
            catch { return; }
            if (vm == null || vm.State != AuditState.Scanning) return;

            progressTotalMs = windowMs + Math.Max(600, Math.Min(3000, windowMs / 10)) + 900;
            progressClock = Stopwatch.StartNew();
            progressTimer = new DispatcherTimer(DispatcherPriority.Background);
            progressTimer.Interval = TimeSpan.FromMilliseconds(80);
            progressTimer.Tick += OnProgressTick;
            progressTimer.Start();
        }

        private void OnProgressTick(object sender, EventArgs e)
        {
            if (vm == null || progressClock == null || progressTotalMs <= 0)
            {
                StopProgressTimer();
                return;
            }
            // 一旦离开扫描态（已进入结果态），停表
            if (vm.State != AuditState.Scanning)
            {
                StopProgressTimer();
                return;
            }
            double ratio = (double)progressClock.ElapsedMilliseconds / progressTotalMs;
            if (ratio > 0.97) ratio = 0.97;
            vm.ReportProgress(ratio);
        }

        private void StopProgressTimer()
        {
            if (progressTimer != null)
            {
                progressTimer.Stop();
                progressTimer.Tick -= OnProgressTick;
                progressTimer = null;
            }
            progressClock = null;
        }
    }
}
