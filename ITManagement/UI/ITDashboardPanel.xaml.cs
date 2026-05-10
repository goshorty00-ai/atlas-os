using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AtlasAI.ITManagement.UI
{
    public partial class ITDashboardPanel : UserControl
    {
        private readonly ITManagementService _itService;
        
        public event Action<string>? OnActivityMessage;
        
        public ITDashboardPanel()
        {
            InitializeComponent();
            
            _itService = ITManagementService.Instance;
            
            // Wire up events
            _itService.HealthMonitor.OnHealthUpdated += UpdateHealthDisplay;
            _itService.IssueDetector.OnIssueDetected += OnIssueDetected;
            _itService.IssueDetector.OnIssueResolved += OnIssueResolved;
            _itService.NetworkDiscovery.OnDeviceDiscovered += OnDeviceDiscovered;
            _itService.OnNotification += AddActivity;
            
            // Start monitoring
            _itService.StartAllMonitoring();
            
            Loaded += (s, e) => RefreshDisplay();
            Unloaded += (s, e) => _itService.StopAllMonitoring();
        }
        
        private void RefreshDisplay()
        {
            var health = _itService.HealthMonitor.CollectHealthData();
            UpdateHealthDisplay(health);
            UpdateIssuesDisplay();
        }
        
        private void UpdateHealthDisplay(SystemHealth health)
        {
            Dispatcher.Invoke(() =>
            {
                // CPU
                CpuValue.Text = $"{health.CpuUsage}%";
                CpuBar.Value = health.CpuUsage;
                CpuBar.Maximum = 100;
                CpuBar.Foreground = GetHealthBrush(health.CpuUsage);
                
                // RAM
                RamValue.Text = $"{health.RamUsage}%";
                RamDetails.Text = $"{health.UsedRamMB / 1024.0:F1} / {health.TotalRamMB / 1024.0:F1} GB";
                RamBar.Value = health.RamUsage;
                RamBar.Maximum = 100;
                RamBar.Foreground = GetHealthBrush(health.RamUsage);
                
                // Disk (primary drive)
                var primaryDrive = health.Drives.FirstOrDefault();
                if (primaryDrive != null)
                {
                    DiskValue.Text = $"{primaryDrive.UsagePercent}%";
                    DiskDetails.Text = $"{primaryDrive.FreeGB} GB free";
                    DiskBar.Value = primaryDrive.UsagePercent;
                    DiskBar.Maximum = 100;
                    DiskBar.Foreground = GetHealthBrush(primaryDrive.UsagePercent);
                }
                
                // Uptime
                UptimeValue.Text = health.UptimeHours > 24 
                    ? $"{health.UptimeHours / 24}d {health.UptimeHours % 24}h"
                    : $"{health.UptimeHours}h";
            });
        }
        
        private Brush GetHealthBrush(int percentage)
        {
            if (percentage >= 90) return new SolidColorBrush(Color.FromRgb(255, 107, 107)); // Red
            if (percentage >= 75) return new SolidColorBrush(Color.FromRgb(255, 217, 61)); // Yellow
            return new SolidColorBrush(Color.FromRgb(74, 222, 128)); // Green
        }
        
        private void UpdateIssuesDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                IssuesPanel.Children.Clear();
                
                var issues = _itService.IssueDetector.ActiveIssues;
                if (!issues.Any())
                {
                    IssuesPanel.Children.Add(new TextBlock
                    {
                        Text = "✅ No issues detected - System is healthy!",
                        Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                        FontSize = 14,
                        Margin = new Thickness(5)
                    });
                    return;
                }
                
                foreach (var issue in issues)
                {
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10),
                        Margin = new Thickness(0, 3, 0, 3)
                    };
                    
                    var stack = new StackPanel();
                    
                    var severityColor = issue.Severity switch
                    {
                        IssueSeverity.Critical => Color.FromRgb(255, 107, 107),
                        IssueSeverity.Warning => Color.FromRgb(255, 217, 61),
                        _ => Color.FromRgb(100, 149, 237)
                    };
                    
                    var header = new StackPanel { Orientation = Orientation.Horizontal };
                    header.Children.Add(new TextBlock
                    {
                        Text = issue.Severity == IssueSeverity.Critical ? "🔴" : 
                               issue.Severity == IssueSeverity.Warning ? "🟡" : "🔵",
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 8, 0)
                    });
                    header.Children.Add(new TextBlock
                    {
                        Text = issue.Title,
                        Foreground = new SolidColorBrush(severityColor),
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 13
                    });
                    
                    stack.Children.Add(header);
                    stack.Children.Add(new TextBlock
                    {
                        Text = issue.Description,
                        Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(22, 5, 0, 0)
                    });
                    
                    if (issue.AutoFixAvailable)
                    {
                        var fixBtn = new Button
                        {
                            Content = "🔧 Auto-Fix",
                            Padding = new Thickness(10, 4, 10, 4),
                            Margin = new Thickness(22, 8, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Tag = issue.Id
                        };
                        fixBtn.Click += async (s, e) =>
                        {
                            var btn = (Button)s;
                            btn.IsEnabled = false;
                            btn.Content = "⏳ Fixing...";
                            
                            var result = await _itService.AutoFixIssueAsync((string)btn.Tag);
                            AddActivity(result);
                            
                            UpdateIssuesDisplay();
                        };
                        stack.Children.Add(fixBtn);
                    }
                    
                    border.Child = stack;
                    IssuesPanel.Children.Add(border);
                }
            });
        }
        
        private void OnIssueDetected(DetectedIssue issue)
        {
            UpdateIssuesDisplay();
            AddActivity($"⚠️ Issue detected: {issue.Title}");
        }
        
        private void OnIssueResolved(DetectedIssue issue)
        {
            UpdateIssuesDisplay();
            AddActivity($"✅ Issue resolved: {issue.Title}");
        }
        
        private void OnDeviceDiscovered(NetworkDevice device)
        {
            Dispatcher.Invoke(() =>
            {
                if (NetworkPanel.Children.Count == 1 && NetworkPanel.Children[0] is TextBlock)
                {
                    NetworkPanel.Children.Clear();
                }
                
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                
                var text = new TextBlock
                {
                    Text = $"📱 {device.IPAddress} - {device.Hostname} ({device.DeviceType})",
                    Foreground = Brushes.White,
                    FontSize = 12
                };
                
                border.Child = text;
                NetworkPanel.Children.Add(border);
            });
        }
        
        private void AddActivity(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (ActivityPanel.Children.Count == 1 && 
                    ActivityPanel.Children[0] is TextBlock tb && 
                    tb.Text == "No recent activity")
                {
                    ActivityPanel.Children.Clear();
                }
                
                var entry = new TextBlock
                {
                    Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                
                ActivityPanel.Children.Insert(0, entry);
                
                // Keep only last 50 entries
                while (ActivityPanel.Children.Count > 50)
                {
                    ActivityPanel.Children.RemoveAt(ActivityPanel.Children.Count - 1);
                }
                
                OnActivityMessage?.Invoke(message);
            });
        }
        
        #region Button Handlers
        
        private async void BtnCleanTemp_Click(object sender, RoutedEventArgs e)
        {
            BtnCleanTemp.IsEnabled = false;
            BtnCleanTemp.Content = "⏳ Cleaning...";
            AddActivity("🧹 Starting temp file cleanup...");
            
            var result = await _itService.ScriptLibrary.ExecuteScriptAsync("cleanup_temp");
            AddActivity(result.Message);
            
            BtnCleanTemp.Content = "🧹 Clean Temp";
            BtnCleanTemp.IsEnabled = true;
        }
        
        private async void BtnScanNetwork_Click(object sender, RoutedEventArgs e)
        {
            BtnScanNetwork.IsEnabled = false;
            BtnScanNetwork.Content = "⏳ Scanning...";
            NetworkPanel.Children.Clear();
            NetworkPanel.Children.Add(new TextBlock
            {
                Text = "🔍 Scanning network...",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 212, 255)),
                FontSize = 12,
                Margin = new Thickness(5)
            });
            
            AddActivity("🌐 Starting network scan...");
            
            var devices = await _itService.NetworkDiscovery.ScanNetworkAsync();
            AddActivity($"🌐 Found {devices.Count} devices on network");
            
            BtnScanNetwork.Content = "🌐 Scan Network";
            BtnScanNetwork.IsEnabled = true;
        }
        
        private async void BtnCheckIssues_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckIssues.IsEnabled = false;
            BtnCheckIssues.Content = "⏳ Checking...";
            AddActivity("🔍 Running system analysis...");
            
            var issues = await _itService.IssueDetector.RunFullAnalysisAsync();
            AddActivity($"🔍 Analysis complete: {issues.Count} new issues found");
            
            UpdateIssuesDisplay();
            
            BtnCheckIssues.Content = "🔍 Check Issues";
            BtnCheckIssues.IsEnabled = true;
        }
        
        private async void BtnSpeedTest_Click(object sender, RoutedEventArgs e)
        {
            BtnSpeedTest.IsEnabled = false;
            BtnSpeedTest.Content = "⏳ Testing...";
            AddActivity("📶 Running speed test...");
            
            var result = await _itService.ScriptLibrary.ExecuteScriptAsync("speed_test");
            AddActivity(result.Message);
            
            BtnSpeedTest.Content = "📶 Speed Test";
            BtnSpeedTest.IsEnabled = true;
        }
        
        private async void BtnVirusScan_Click(object sender, RoutedEventArgs e)
        {
            BtnVirusScan.IsEnabled = false;
            BtnVirusScan.Content = "⏳ Scanning...";
            AddActivity("🛡️ Starting virus scan...");
            
            var result = await _itService.ScriptLibrary.ExecuteScriptAsync("windows_defender_scan");
            AddActivity(result.Message);
            
            BtnVirusScan.Content = "🛡️ Virus Scan";
            BtnVirusScan.IsEnabled = true;
        }
        
        private async void BtnFullReport_Click(object sender, RoutedEventArgs e)
        {
            BtnFullReport.IsEnabled = false;
            BtnFullReport.Content = "⏳ Generating...";
            
            var report = await _itService.GetSystemReportAsync();
            
            // Show in a dialog
            var dialog = new Window
            {
                Title = "Atlas AI - System Report",
                Width = 600,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 46))
            };
            
            var scroll = new ScrollViewer { Margin = new Thickness(20) };
            var text = new TextBlock
            {
                Text = report,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            scroll.Content = text;
            dialog.Content = scroll;
            dialog.Show();
            
            AddActivity("📊 Generated full system report");
            
            BtnFullReport.Content = "📊 Full Report";
            BtnFullReport.IsEnabled = true;
        }
        
        #endregion
    }
}
