using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AtlasAI.UI.Pages
{
    public partial class FuturisticSecurityDashboard : Page
    {
        private DispatcherTimer? _animationTimer;
        private DispatcherTimer? _logTimer;
        private DispatcherTimer? _resourceTimer;
        private double _radarAngle = 0;
        private double _scanProgress = 0;
        private int _tickIndex = 0;
        private Random _random = new();
        private ObservableCollection<LogEntry> _logEntries = new();

        public FuturisticSecurityDashboard()
        {
            InitializeComponent();
            LogEntries.ItemsSource = _logEntries;
            InitializePipeline();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            StartAnimations();
            StartLogSimulation();
            StartResourceMonitor();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _animationTimer?.Stop();
            _logTimer?.Stop();
            _resourceTimer?.Stop();
        }

        private void InitializePipeline()
        {
            var steps = new[] { "DETECT", "ANALYZE", "CLASSIFY", "RESPOND", "LEARN" };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            
            for (int i = 0; i < steps.Length; i++)
            {
                var stepBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x22, 0xD3, 0xEE)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0x22, 0xD3, 0xEE)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(4, 0, 4, 0)
                };
                
                var stepText = new TextBlock
                {
                    Text = steps[i],
                    Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)),
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas")
                };
                
                stepBorder.Child = stepText;
                panel.Children.Add(stepBorder);
                
                if (i < steps.Length - 1)
                {
                    var arrow = new TextBlock
                    {
                        Text = "→",
                        Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0x22, 0xD3, 0xEE)),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    panel.Children.Add(arrow);
                }
            }
            
            PipelineSteps.Items.Add(panel);
        }

        private void StartAnimations()
        {
            _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _animationTimer.Tick += AnimationTick;
            _animationTimer.Start();
        }

        private void AnimationTick(object? sender, EventArgs e)
        {
            // Rotate radar sweep
            _radarAngle += 3;
            if (_radarAngle >= 360) _radarAngle = 0;
            RadarSweep.RenderTransform = new RotateTransform(_radarAngle, 70, 70);

            // Rotate outer ring
            OuterRing.RenderTransform = new RotateTransform(-_radarAngle * 0.5, 140, 140);

            // Update scan progress
            _scanProgress += 0.3;
            if (_scanProgress >= 100) _scanProgress = 0;
            ProgressText.Text = $"{(int)_scanProgress}%";
            HexProgress.Text = $"0x{(int)(_scanProgress * 2.55):X2}";

            // Animate heartbeat dot
            HeartbeatDot.Opacity = 0.5 + Math.Sin(_radarAngle * Math.PI / 180) * 0.5;

            // Animate header dots
            var dotOpacity = (Math.Sin(_radarAngle * Math.PI / 90) + 1) / 2;
            Dot1.Opacity = 0.3 + dotOpacity * 0.7;
            Dot2.Opacity = 0.3 + (1 - dotOpacity) * 0.7;
            Dot3.Opacity = 0.3 + dotOpacity * 0.7;

            // Animate status bar ticks
            _tickIndex = (_tickIndex + 1) % 10;
            var ticks = new[] { Tick1, Tick2, Tick3, Tick4, Tick5, Tick6, Tick7, Tick8, Tick9, Tick10 };
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i].Fill = i == _tickIndex || i == (_tickIndex + 1) % 10
                    ? new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE))
                    : new SolidColorBrush(Color.FromArgb(0x4D, 0x22, 0xD3, 0xEE));
            }
        }

        private void StartLogSimulation()
        {
            AddLog("SYS", "Security agent initialized", LogType.Info);
            AddLog("NET", "Network monitoring active", LogType.Success);
            AddLog("AI", "Threat detection model loaded", LogType.Info);

            _logTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _logTimer.Tick += (s, e) => GenerateRandomLog();
            _logTimer.Start();
        }

        private void GenerateRandomLog()
        {
            var logs = new[]
            {
                ("NET", "Packet inspection: 0 anomalies", LogType.Success),
                ("SYS", "Memory scan complete", LogType.Info),
                ("AI", "Behavioral analysis: normal", LogType.Success),
                ("SEC", "Firewall rules verified", LogType.Success),
                ("NET", "DNS queries analyzed", LogType.Info),
                ("SYS", "Process integrity check passed", LogType.Success),
                ("AI", "Pattern recognition: no threats", LogType.Info),
                ("SEC", "Certificate chain validated", LogType.Success),
            };

            var log = logs[_random.Next(logs.Length)];
            AddLog(log.Item1, log.Item2, log.Item3);
        }

        private void AddLog(string tag, string message, LogType type)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Tag = tag,
                Message = message,
                Icon = type == LogType.Success ? "\uE73E" : type == LogType.Warning ? "\uE7BA" : "\uE946",
                IconColor = type == LogType.Success ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
                          : type == LogType.Warning ? new SolidColorBrush(Color.FromRgb(0xFB, 0xB7, 0x24))
                          : new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)),
                MessageColor = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
            };

            _logEntries.Add(entry);
            if (_logEntries.Count > 20) _logEntries.RemoveAt(0);
            LogScroller.ScrollToEnd();
        }

        private void StartResourceMonitor()
        {
            _resourceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _resourceTimer.Tick += (s, e) => UpdateResources();
            _resourceTimer.Start();
        }

        private void UpdateResources()
        {
            var cpu = _random.Next(15, 35);
            var ram = _random.Next(40, 60);
            var gpu = _random.Next(55, 75);

            CpuBar.Width = cpu * 1.2;
            CpuText.Text = $"{cpu}%";

            RamBar.Width = ram * 1.2;
            RamText.Text = $"{ram}%";

            GpuBar.Width = gpu * 1.2;
            GpuText.Text = $"{gpu}%";
        }

        private enum LogType { Info, Success, Warning }

        private class LogEntry
        {
            public string Timestamp { get; set; } = "";
            public string Tag { get; set; } = "";
            public string Message { get; set; } = "";
            public string Icon { get; set; } = "";
            public Brush IconColor { get; set; } = Brushes.White;
            public Brush MessageColor { get; set; } = Brushes.White;
        }
    }
}
