// @author zenjiro 18967498922@163.com
// 文件用途 WPF 显卡页：逐游戏 NV 项、会话项、呈现项、AMD 项的开关与档位

using System.Threading;
using System.Windows;
using System.Windows.Controls;
using CaelusApp.WpfHost.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class GraphicsView : UserControl
    {
        public GraphicsView() { InitializeComponent(); }

        // SegmentedControl 只发出索引事件；在视图层同步依赖属性以触发现有 TwoWay 绑定。
        private void OnFrlChanged(object sender, int index) { UpdateSegmentSelection(sender, index); }
        private void OnDlssChanged(object sender, int index) { UpdateSegmentSelection(sender, index); }
        private void OnAmdChillChanged(object sender, int index) { UpdateSegmentSelection(sender, index); }

        private static void UpdateSegmentSelection(object sender, int index)
        {
            SegmentedControl control = sender as SegmentedControl;
            if (control == null || control.SelectedIndex == index) return;
            control.SetCurrentValue(SegmentedControl.SelectedIndexProperty, index);
        }

        // AMD 着色器缓存重置（后台线程执行）
        private void OnAmdCache(object sender, RoutedEventArgs e)
        {
            BtnAmdCache.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int done;
                bool ok = false;
                try { ok = AdlxTweaks.ResetShaderCacheAll(out done); }
                catch { done = 0; }
                Dispatcher.BeginInvoke(new System.Action(delegate
                {
                    BtnAmdCache.IsEnabled = AdlxTweaks.Available;
                    MessageBox.Show(ok ? Lang.T("amd.cache.done") : Lang.T("amd.cache.fail"),
                        "Caelus", MessageBoxButton.OK,
                        ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }));
            });
        }
    }
}
