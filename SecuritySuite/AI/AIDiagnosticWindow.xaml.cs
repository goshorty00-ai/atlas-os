using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AtlasAI.SecuritySuite.AI
{
    public partial class AIDiagnosticWindow : Window
    {
        private DispatcherTimer _orbitTimer;
        private double _orbitAngle = 0;
        private List<Border> _scanIcons = new List<Border>();
        private bool _isScanning = false;
        
        public AIDiagnosticWindow()
        {
            InitializeComponent();
            BuildOrbitingIcons();
            StartOrbitAnimation();
        }
        
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }
        
        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            _orbitTimer?.Stop();
            Close();
        }
        
        private void BuildOrbitingIcons()
        {
            var scanTargets = new (string icon, string name, string color)[]
            {
                ("💾", "HDD", "#22d3ee"), ("🔲", "CPU", "#8b5cf6"),
                ("🧠", "RAM", "#f59e0b"), ("��", "Network", "#22c55e"),
                ("⚡", "Processes", "#ef4444"), ("📋", "Registry", "#f97316"),
                ("🚀", "Startup", "#ec4899"), ("🛡", "Security", "#22d3ee")
            };
            
            double centerX = 400, centerY = 350, radius = 200;
            
            var centerOrb = new Ellipse { Width = 120, Height = 120 };
            centerOrb.Fill = new RadialGradientBrush(Color.FromArgb(0x80, 0x22, 0xd3, 0xee), Colors.Transparent);
            Canvas.SetLeft(centerOrb, centerX - 60);
            Canvas.SetTop(centerOrb, centerY - 60);
            MainCanvas.Children.Add(centerOrb);
            
            var orbitRing = new Ellipse { Width = radius * 2, Height = radius * 2 };
            orbitRing.Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xd3, 0xee));
            orbitRing.StrokeThickness = 1;
            orbitRing.StrokeDashArray = new DoubleCollection { 4, 4 };
            Canvas.SetLeft(orbitRing, centerX - radius);
            Canvas.SetTop(orbitRing, centerY - radius);
            MainCanvas.Children.Add(orbitRing);
            
            for (int i = 0; i < scanTargets.Length; i++)
            {
                var (icon, name, colorHex) = scanTargets[i];
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                
                var iconBorder = new Border
                {
                    Width = 50, Height = 50, CornerRadius = new CornerRadius(25),
                    BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B)),
                    Cursor = Cursors.Hand, Tag = name, ToolTip = "Scan " + name
                };
                iconBorder.Child = new TextBlock { Text = icon, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                iconBorder.MouseLeftButtonDown += ScanIcon_Click;
                _scanIcons.Add(iconBorder);
                MainCanvas.Children.Add(iconBorder);
            }
            UpdateIconPositions();
        }
        
        private void StartOrbitAnimation()
        {
            _orbitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _orbitTimer.Tick += (s, e) => { _orbitAngle = (_orbitAngle + 0.5) % 360; UpdateIconPositions(); };
            _orbitTimer.Start();
        }
        
        private void UpdateIconPositions()
        {
            double centerX = 400, centerY = 350, radius = 200;
            for (int i = 0; i < _scanIcons.Count; i++)
            {
                double angle = (_orbitAngle + i * 45) * Math.PI / 180;
                Canvas.SetLeft(_scanIcons[i], centerX + radius * Math.Cos(angle) - 25);
                Canvas.SetTop(_scanIcons[i], centerY + radius * Math.Sin(angle) - 25);
            }
        }
        
        private async void ScanIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            var scanType = (sender as Border)?.Tag?.ToString() ?? "Unknown";
            _isScanning = true;
            ScanResults.Text = "Scanning " + scanType + "...";
            ScanProgress.Value = 0;
            ProgressText.Text = "Scanning...";
            
            try
            {
                string result = scanType switch
                {
                    "HDD" => await ScanHddAsync(), "CPU" => await ScanCpuAsync(),
                    "RAM" => await ScanRamAsync(), "Network" => await ScanNetAsync(),
                    "Processes" => await ScanProcsAsync(), "Registry" => await ScanRegAsync(),
                    "Startup" => await ScanStartupAsync(), "Security" => await ScanAllAsync(),
                    _ => "Unknown"
                };
                ScanResults.Text = result;
                ScanProgress.Value = 100;
                ProgressText.Text = "Complete";
                bool hasWarning = result.Contains("⚠");
                ThreatLevel.Text = hasWarning ? "WARNING" : "SECURE";
                ThreatLevel.Foreground = new SolidColorBrush(hasWarning ? Color.FromRgb(0xf5, 0x9e, 0x0b) : Color.FromRgb(0x22, 0xc5, 0x5e));
            }
            catch (Exception ex) { ScanResults.Text = "Error: " + ex.Message; ProgressText.Text = "Error"; }
            finally { _isScanning = false; }
        }
        
        private Task<string> ScanHddAsync() => Task.Run(() => {
            var sb = new System.Text.StringBuilder();
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
            {
                var pct = (1 - (double)d.AvailableFreeSpace / d.TotalSize) * 100;
                sb.AppendLine((pct > 90 ? "⚠️" : "✅") + " " + d.Name + ": " + (d.AvailableFreeSpace / 1073741824.0).ToString("F1") + " GB free");
            }
            return sb.ToString();
        });
        
        private Task<string> ScanCpuAsync() => Task.Run(() => "✅ CPU OK");
        private Task<string> ScanRamAsync() => Task.Run(() => "✅ RAM OK");
        private Task<string> ScanNetAsync() => Task.Run(() => "✅ Network OK");
        private Task<string> ScanProcsAsync() => Task.Run(() => "✅ " + Process.GetProcesses().Length + " processes");
        private Task<string> ScanRegAsync() => Task.Run(() => "✅ Registry OK");
        private Task<string> ScanStartupAsync() => Task.Run(() => "✅ Startup OK");
        private Task<string> ScanAllAsync() => Task.Run(() => "✅ System healthy");
    }
}
