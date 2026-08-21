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
            // 入场 stagger（页头 + 摘要 + 五个分区）
            SetPoliteLiveSetting(ZoneSummary);
            SetPoliteLiveSetting(AmdCacheFeedback);
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneSummary, 100);
            Motion.RiseIn(ZoneGeneral, 160);
            Motion.RiseIn(ZoneNvidia, 220);
            Motion.RiseIn(ZoneSession, 280);
            Motion.RiseIn(ZonePresent, 340);
            Motion.RiseIn(ZoneAmd, 400);
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

        private static void SetPoliteLiveSetting(DependencyObject element)
        {
            // .NET 4 参考程序集不含 LiveSetting；在支持该 UIA API 的系统上反射启用。
            try
            {
                System.Type propertiesType = typeof(System.Windows.Automation.AutomationProperties);
                System.Reflection.MethodInfo setter = propertiesType.GetMethod("SetLiveSetting");
                if (setter == null) return;
                System.Type settingType = setter.GetParameters()[1].ParameterType;
                object polite = System.Enum.Parse(settingType, "Polite");
                setter.Invoke(null, new object[] { element, polite });
            }
            catch
            {
                // 旧版系统缺少 live region API 时保留 AutomationProperties.Name。
            }
        }

        // AMD 着色器缓存重置（后台线程执行，状态在行内反馈 + 页面 FeedbackBanner）
        private void OnAmdCache(object sender, RoutedEventArgs e)
        {
            GraphicsViewModel vm = DataContext as GraphicsViewModel;
            if (vm == null || !vm.BeginAmdCacheReset()) return;
            vm.ShowFeedback("正在清理着色器缓存…", "Info");
            Motion.Emphasize(AmdCacheFeedback);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int done;
                bool ok = false;
                try { ok = AdlxTweaks.ResetShaderCacheAll(out done); }
                catch { done = 0; }
                Dispatcher.BeginInvoke(new System.Action(delegate
                {
                    vm.CompleteAmdCacheReset(ok);
                    vm.ShowFeedback(ok
                        ? "着色器缓存清理完成（释放 " + CacheSweep.FmtBytes(done) + "）。"
                        : "着色器缓存清理失败，请稍后重试。", ok ? "Success" : "Error");
                    Motion.Emphasize(AmdCacheFeedback);
                }));
            });
        }
    }
}
