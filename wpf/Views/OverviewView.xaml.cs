using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CaelusApp.WpfHost.Views
{
    public partial class OverviewView : UserControl
    {
        public OverviewView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 主题/模式换槽后需重选标题渐变档（参照 CaelusCore 的订阅-退订模式）
            ThemeManager.ModeChanged += OnThemeChanged;
            ApplyHeroTitle();

            // 分区 staggered 入场（与 HTML 沙盒 rise-in 编排一致：40ms 递增）
            Motion.RiseIn(ZoneHeader, 40);
            Motion.RiseIn(ZoneBadge, 100);
            Motion.RiseIn(ZoneHero, 160);
            Motion.RiseIn(ZoneMetricsLabel, 220);
            Motion.RiseIn(ZoneMetrics, 220);
            Motion.RiseIn(ZoneBottom, 280);
            Motion.BreathPulse(ReadyDot);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ModeChanged -= OnThemeChanged;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            ApplyHeroTitle();
        }

        // 标题渐变档按主题选用：深色用 Accent 梯度，浅色用 OnLight 变体保证白底对比度
        private void ApplyHeroTitle()
        {
            string key = ThemeManager.CurrentTone == UiTone.Light
                ? "HeroTitleOnLightBrush" : "HeroTitleOnDarkBrush";
            object brush = TryFindResource(key);
            if (brush is Brush) ConclusionTitleText.Foreground = (Brush)brush;
        }

        // 占比条 Loaded 时入场生长（BarGrowFill 样式的 EventSetter）
        private void OnBarLoaded(object sender, RoutedEventArgs e)
        {
            Motion.GrowX(sender as FrameworkElement, 350);
        }

        // GPU 温度使用独立指标面板，其余两项由集合视图生成。
        private void OnMetricsFilter(object sender, FilterEventArgs e)
        {
            var vm = DataContext as OverviewViewModel;
            var item = e.Item as MetricViewModel;
            if (vm == null || item == null) { e.Accepted = true; return; }
            e.Accepted = vm.Metrics.IndexOf(item) != 0;
        }
    }
}
