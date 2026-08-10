// @author zenjiro 18967498922@163.com
// 文件用途 Caelus Core 品牌核心：双环反向旋转 + 模式名随换肤（规格 §4.3，仅概览页 Hero 使用）

using System;
using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Controls
{
    public partial class CaelusCore : UserControl
    {
        public CaelusCore()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.Spin(RingOuter, 14, false);
            Motion.Spin(RingMid, 22, true);
            ThemeManager.ModeChanged += OnModeChanged;
            RefreshModeLabel();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ModeChanged -= OnModeChanged;
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            RefreshModeLabel();
        }

        private void RefreshModeLabel()
        {
            ModeLabel.Text = ThemeManager.CurrentMode == AppMode.Competitive ? "竞技"
                : ThemeManager.CurrentMode == AppMode.Custom ? "自定义" : "常规";
        }
    }
}
