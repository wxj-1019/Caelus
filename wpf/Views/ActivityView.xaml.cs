// @author zenjiro 18967498922@163.com
// 文件用途 实时监控页：展示场景掌权状态、实时压制/提优清单与最近动作流

using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class ActivityView : UserControl
    {
        /// <summary>截图探针样例数据开关（对齐其他视图的 InjectSampleData 模式）。</summary>
        public static bool InjectSampleData;

        public ActivityView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // 错落入场（规格 2026-09-29 §3.3）：与其余 12 页同一范式
        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneStatus, 100);
        }
    }
}
