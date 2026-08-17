using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class ScenarioDetailView : UserControl
    {
        /// <summary>截图探针用：与概览页保持一致，由宿主注入演示态。此视图直接复用宿主的状态源。</summary>
        public static bool InjectSampleData;

        public ScenarioDetailView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneBanner, 100);
            Motion.RiseIn(ZoneCard, 160);
            Motion.RiseIn(ZoneSources, 220);
            if (ZoneFocus != null && ZoneFocus.Visibility == Visibility.Visible)
                Motion.RiseIn(ZoneFocus, 280);
            Motion.RiseIn(ZoneNote, 340);
        }
    }
}
