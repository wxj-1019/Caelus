using System;
using System.Windows;
using System.Windows.Controls;
using CaelusApp.WpfHost.Controls;

namespace CaelusApp.WpfHost.Views
{
    public partial class OverviewView : UserControl
    {
        /// <summary>截图探针用：宿主在构建概览页前注入演示态，展示“游戏掌权 / 开发待命”的完整构图。</summary>
        public static bool InjectSampleData;

        /// <summary>游戏性能档位切换，转发给宿主（MainWindow 处理模式切换副作用）。</summary>
        public event SegmentSelectionChangedEventHandler ModePicked;

        public OverviewView()
        {
            InitializeComponent();
            ModePicker.ItemsSource = new System.Collections.Generic.List<string>
            {
                ModePalette.DisplayName(AppMode.Standard),
                ModePalette.DisplayName(AppMode.Competitive),
                ModePalette.DisplayName(AppMode.Custom)
            };
            Loaded += OnLoaded;
        }

        /// <summary>程序化同步档位选中态（持久化恢复 / 压力测试），不重入触发 ModePicked。</summary>
        public void SetModeSelection(int index)
        {
            ModePicker.SetCurrentValue(SegmentedControl.SelectedIndexProperty, index);
        }

        private void OnModePicked(object sender, int index)
        {
            SegmentSelectionChangedEventHandler h = ModePicked;
            if (h != null)
            {
                try { h(this, index); } catch { }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneBadge, 100);
            Motion.RiseIn(ZoneHero, 160);
            Motion.RiseIn(ZoneMode, 200);
            Motion.RiseIn(ZoneCardsLabel, 240);
            Motion.RiseIn(ZoneCards, 240);
            Motion.RiseIn(ZoneRules, 300);
            Motion.RiseIn(ZoneBottom, 340);
            Motion.BreathPulse(ReadyDot);
        }
    }
}
