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
            UpdateStateVisibility(false);
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
            else if (result) Motion.Reveal(ResultPanel);
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
