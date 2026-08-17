using System;
using System.Windows;
using System.Windows.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class OverviewView : UserControl
    {
        /// <summary>截图探针用：宿主在构建概览页前注入演示态，展示“游戏掌权 / 开发待命”的完整构图。</summary>
        public static bool InjectSampleData;

        public OverviewView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneBadge, 100);
            Motion.RiseIn(ZoneHero, 160);
            Motion.RiseIn(ZoneCardsLabel, 220);
            Motion.RiseIn(ZoneCards, 220);
            Motion.RiseIn(ZoneRules, 280);
            Motion.RiseIn(ZoneBottom, 340);
            Motion.BreathPulse(ReadyDot);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
        }
    }
}
