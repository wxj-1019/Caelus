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
        // 预览探针（--wpf-shot）注入代表性开关态；生产永不置 true。
        internal static bool InjectSampleData;

        public GraphicsView() { InitializeComponent(); Loaded += OnLoaded; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 预览样例：开启若干 NV 项以呈现代表性态（探针下无真实显卡 API）
            if (InjectSampleData)
            {
                GraphicsViewModel vm = DataContext as GraphicsViewModel;
                if (vm != null)
                {
                    vm.GpuHighPerf = true;
                    vm.NvMaxPerf = true;
                    vm.NvLowLatency = true;
                    vm.NvRebar = true;
                    vm.NvBattFull = true;
                    vm.NvBgFrl = true;
                    vm.WindowedOpt = true;
                    vm.NotifySegments();
                }
            }
            // 入场 stagger（页头 + 摘要；分组随滚动自然呈现）
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneSummary, 100);
        }

        // SegmentedControl 只发出索引事件；在视图层同步依赖属性以触发现有 TwoWay 绑定，
        // 并刷新标题旁的值回显。
        private void OnFrlChanged(object sender, int index) { UpdateSegmentSelection(sender, index); RefreshVm(); }
        private void OnDlssChanged(object sender, int index) { UpdateSegmentSelection(sender, index); RefreshVm(); }
        private void OnAmdChillChanged(object sender, int index) { UpdateSegmentSelection(sender, index); RefreshVm(); }

        private void RefreshVm()
        {
            GraphicsViewModel vm = DataContext as GraphicsViewModel;
            if (vm != null) vm.NotifySegments();
        }

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
