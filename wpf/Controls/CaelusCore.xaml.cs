// @author zenjiro 18967498922@163.com
// 文件用途 Caelus Core 品牌核心：双环反向旋转 + 模式名随换肤（规格 §4.3，仅概览页 Hero 使用）

using System;
using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Controls
{
    public partial class CaelusCore : UserControl
    {
        private Window hostWindow;
        private AppMode currentMode;

        public CaelusCore()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachWindow(Window.GetWindow(this));
            ThemeManager.ModeChanged += OnModeChanged;
            Motion.PolicyChanged += OnMotionPolicyChanged;
            currentMode = ThemeManager.CurrentMode;
            RefreshModeLabel();
            UpdateSpinState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ModeChanged -= OnModeChanged;
            Motion.PolicyChanged -= OnMotionPolicyChanged;
            StopSpin();
            AttachWindow(null);
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            bool modeChanged = currentMode != ThemeManager.CurrentMode;
            currentMode = ThemeManager.CurrentMode;
            RefreshModeLabel();
            if (modeChanged) Motion.Emphasize(CoreIdentity);
        }

        private void OnMotionPolicyChanged(object sender, EventArgs e)
        {
            UpdateSpinState();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateSpinState();
        }

        private void OnWindowActivityChanged(object sender, EventArgs e)
        {
            UpdateSpinState();
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            UpdateSpinState();
        }

        private void AttachWindow(Window window)
        {
            if (hostWindow == window) return;
            if (hostWindow != null)
            {
                hostWindow.Activated -= OnWindowActivityChanged;
                hostWindow.Deactivated -= OnWindowActivityChanged;
                hostWindow.StateChanged -= OnWindowStateChanged;
            }
            hostWindow = window;
            if (hostWindow != null)
            {
                hostWindow.Activated += OnWindowActivityChanged;
                hostWindow.Deactivated += OnWindowActivityChanged;
                hostWindow.StateChanged += OnWindowStateChanged;
            }
        }

        // 极简 Linear：品牌核心保持静态几何，不做旋转动效。
        private void UpdateSpinState()
        {
            StopSpin();
        }

        private void StopSpin()
        {
            Motion.StopSpin(RingOuter);
            Motion.StopSpin(RingMid);
        }

        private void RefreshModeLabel()
        {
            ModeLabel.Text = ThemeManager.CurrentMode == AppMode.Competitive ? "竞技"
                : ThemeManager.CurrentMode == AppMode.Custom ? "自定义" : "常规";
        }
    }
}
