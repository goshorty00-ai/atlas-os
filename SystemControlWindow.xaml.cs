using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using AtlasAI.SystemControl;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Grid = System.Windows.Controls.Grid;
using StackPanel = System.Windows.Controls.StackPanel;
using Border = System.Windows.Controls.Border;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using System.Windows.Shapes;

namespace AtlasAI {
    public partial class SystemControlWindow : FluentWindow
    {
        private WindowsSystemScanner _scanner;
        private WindowsSystemController _controller;
        private SpywareScanner _spywareScanner;
        private UnifiedScanner _unifiedScanner;
        private ThreatRemover _threatRemover;
        private SystemScanResult _lastScanResult;
        private SpywareScanResult _lastSpywareScanResult;
        private List<SystemIssue> _currentIssues = new();
        private List<SpywareThreat> _currentThreats = new();
        private List<UnifiedThreat> _currentUnifiedThreats = new();
        private DispatcherTimer _systemMonitorTimer;
        private DispatcherTimer _scanAnimationTimer;
        private DispatcherTimer _elapsedTimeTimer;
        private bool _isDeepScanning = false;
        private double _scanRotation = 0;
        private DateTime _scanStartTime;

        public SystemControlWindow()
        {
            try
            {
                InitializeComponent();
                
                // Force cyan accent color
                try
                {
                    var cyan = Color.FromRgb(0x22, 0xd3, 0xee);
                    Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(cyan, Wpf.Ui.Appearance.ApplicationTheme.Dark);
                }
                catch { }
                
                _scanner = new WindowsSystemScanner();
                _controller = new WindowsSystemController();
                _spywareScanner = new SpywareScanner();
                _unifiedScanner = new UnifiedScanner();
                _threatRemover = new ThreatRemover();
                
                // Subscribe to events
                _scanner.ScanProgress += OnScanProgress;
                _scanner.IssueDetected += OnIssueDetected;
                _controller.FixProgress += OnFixProgress;
                _controller.FixCompleted += OnFixCompleted;
                _spywareScanner.ScanProgress += OnSpywareScanProgress;
                _spywareScanner.ThreatDetected += OnThreatDetected;
                
                Loaded += SystemControlWindow_Loaded;
                
                InitializeSystemMonitoring();
                InitializeScanAnimation();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing System Control Window: {ex.Message}", 
                               "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private void InitializeScanAnimation()
        {
            _scanAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _scanAnimationTimer.Tick += (s, e) =>
            {
                _scanRotation += 3;
                if (_scanRotation >= 360) _scanRotation = 0;
                if (ProgressRotation != null)
                    ProgressRotation.Angle = _scanRotation;
            };
            
            // Timer to update elapsed time display
            _elapsedTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimeTimer.Tick += (s, e) =>
            {
                if (_isDeepScanning)
                {
                    var elapsed = DateTime.Now - _scanStartTime;
                    ElapsedTimeText.Text = elapsed.TotalMinutes >= 1 
                        ? $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}" 
                        : $"0:{elapsed.Seconds:D2}";
                }
            };
        }

        private void InitializeSystemMonitoring()
        {
            _systemMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _systemMonitorTimer.Tick += async (s, e) => await UpdateSystemMetricsAsync();
        }

        private void SystemControlWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _systemMonitorTimer?.Start();
            _ = UpdateSystemMetricsAsync();
        }

        // Navigation handlers
        private void NavScan_Click(object sender, MouseButtonEventArgs e) => ShowPage("Scan");
        private void NavProtection_Click(object sender, MouseButtonEventArgs e) => ShowPage("Protection");
        private void NavTools_Click(object sender, MouseButtonEventArgs e) => ShowPage("Tools");
        private void NavHistory_Click(object sender, MouseButtonEventArgs e) => ShowPage("Scan"); // Reuse scan page

        private void ShowPage(string page)
        {
            ScanPage.Visibility = page == "Scan" ? Visibility.Visible : Visibility.Collapsed;
            ProtectionPage.Visibility = page == "Protection" ? Visibility.Visible : Visibility.Collapsed;
            ToolsPage.Visibility = page == "Tools" ? Visibility.Visible : Visibility.Collapsed;

            // Update nav highlighting
            NavScan.Background = page == "Scan" ? new SolidColorBrush(Color.FromRgb(27, 75, 138)) : Brushes.Transparent;
            NavProtection.Background = page == "Protection" ? new SolidColorBrush(Color.FromRgb(27, 75, 138)) : Brushes.Transparent;
            NavTools.Background = page == "Tools" ? new SolidColorBrush(Color.FromRgb(27, 75, 138)) : Brushes.Transparent;
        }

        // Scan circle click handler
        private async void ScanCircle_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isDeepScanning) return;
            await PerformDeepScanAsync();
        }

        private void NewScanButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset to scan button view
            ScanButtonArea.Visibility = Visibility.Visible;
            ScanResultsArea.Visibility = Visibility.Collapsed;
            ScanButtonText.Text = "SCAN";
            ScanButtonText.Visibility = Visibility.Visible;
            ScanPercentText.Visibility = Visibility.Collapsed;
            ScanSubText.Text = "Click to start full malware scan";
            ProgressArcContainer.Visibility = Visibility.Collapsed;
            CategoryPanel.Visibility = Visibility.Collapsed;
            ScanStatsPanel.Visibility = Visibility.Collapsed;
            CancelScanButton.IsEnabled = true;
            
            // Reset stats
            FilesScannedText.Text = "0";
            ThreatsFoundText.Text = "0";
            ElapsedTimeText.Text = "0:00";
            CurrentFileText.Text = "Ready to scan";
            ScanProgressBar.Value = 0;
        }

        private void MasterShieldToggle_Click(object sender, RoutedEventArgs e)
        {
            var isOn = MasterShieldToggle.IsChecked == true;
            MalwareShieldToggle.IsChecked = isOn;
            AntivirusToggle.IsChecked = isOn;
            RealtimeToggle.IsChecked = isOn;
            NetworkToggle.IsChecked = isOn;
            ProtectionStatusText.Text = isOn ? "All shields are turned ON" : "All shields are turned OFF";
            ProtectionStatusText.Foreground = isOn ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : Brushes.Red;
        }

        // Event handler references for cleanup
        private Action<string>? _progressChangedHandler;
        private Action<int>? _progressPercentHandler;
        private Action<UnifiedThreat>? _threatFoundHandler;

        public async void StartFullScan()
        {
            await Task.Delay(500);
            await PerformDeepScanAsync();
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e) => await PerformDeepScanAsync();
        private async void QuickScanButton_Click(object sender, RoutedEventArgs e) => await PerformScanAsync(false);

        private async Task PerformDeepScanAsync()
        {
            if (_isDeepScanning) return;
            _isDeepScanning = true;
            _scanStartTime = DateTime.Now;
            
            try
            {
                // Show scanning UI - Norton style
                ScanButtonText.Visibility = Visibility.Collapsed;
                ScanPercentText.Visibility = Visibility.Visible;
                ScanPercentText.Text = "0%";
                ScanSubText.Text = "Initializing deep scan...";
                ProgressArcContainer.Visibility = Visibility.Visible;
                CategoryPanel.Visibility = Visibility.Visible;
                ScanStatsPanel.Visibility = Visibility.Visible;
                
                // Reset stats
                FilesScannedText.Text = "0";
                ThreatsFoundText.Text = "0";
                ElapsedTimeText.Text = "0:00";
                CurrentFileText.Text = "Starting...";
                ScanProgressBar.Value = 0;
                
                _scanAnimationTimer.Start();
                _elapsedTimeTimer.Start();

                // Remove existing handlers
                if (_progressChangedHandler != null) _unifiedScanner.ProgressChanged -= _progressChangedHandler;
                if (_progressPercentHandler != null) _unifiedScanner.ProgressPercentChanged -= _progressPercentHandler;
                if (_threatFoundHandler != null) _unifiedScanner.ThreatFound -= _threatFoundHandler;
                
                // Create new handlers - use BeginInvoke (non-blocking) instead of Invoke
                _progressChangedHandler = msg => Dispatcher.BeginInvoke(() => ScanSubText.Text = msg);
                _progressPercentHandler = pct => Dispatcher.BeginInvoke(() => 
                {
                    ScanPercentText.Text = $"{pct}%";
                    ScanProgressBar.Value = pct;
                });
                _threatFoundHandler = threat => Dispatcher.BeginInvoke(() => 
                {
                    UpdateCategoryCount(threat.Category);
                    ThreatsFoundText.Text = _unifiedScanner.ThreatsFound.ToString();
                    ThreatsFoundText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                });
                
                // Subscribe to new events - use BeginInvoke for non-blocking UI updates
                _unifiedScanner.ProgressChanged += _progressChangedHandler;
                _unifiedScanner.ProgressPercentChanged += _progressPercentHandler;
                _unifiedScanner.ThreatFound += _threatFoundHandler;
                _unifiedScanner.FilesScannedChanged += count => Dispatcher.BeginInvoke(() => 
                    FilesScannedText.Text = count.ToString("N0"));
                _unifiedScanner.CurrentFileChanged += file => Dispatcher.BeginInvoke(() =>
                {
                    // Show just the filename or truncated path
                    if (file.Length > 60)
                        CurrentFileText.Text = "..." + file.Substring(file.Length - 57);
                    else
                        CurrentFileText.Text = file;
                });
                
                // Run scan on background thread with ConfigureAwait(false) to not capture context
                var result = await Task.Run(() => _unifiedScanner.PerformDeepScanAsync()).ConfigureAwait(false);
                
                // Switch back to UI thread for results
                await Dispatcher.InvokeAsync(() =>
                {
                    _currentUnifiedThreats = result.Threats;
                    ConvertUnifiedThreatsToIssues();
                    
                    // Stop timers
                    _scanAnimationTimer.Stop();
                    _elapsedTimeTimer.Stop();
                    
                    if (result.WasCancelled)
                    {
                        // Show cancelled state
                        ScanPercentText.Text = "—";
                        ScanSubText.Text = $"Scan cancelled. Scanned {result.FilesScanned:N0} files.";
                        CurrentFileText.Text = "Cancelled by user";
                    }
                    else
                    {
                        ShowScanResults(result);
                    }
                });
                
                if (result.WasCancelled)
                {
                    await Task.Delay(2000);
                    await Dispatcher.InvokeAsync(() => NewScanButton_Click(null, null));
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Scan failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _scanAnimationTimer.Stop();
                    _elapsedTimeTimer.Stop();
                    NewScanButton_Click(null, null);
                });
            }
            finally
            {
                _isDeepScanning = false;
            }
        }

        private void CancelScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeepScanning)
            {
                _unifiedScanner.CancelScan();
                ScanSubText.Text = "Cancelling scan...";
                CurrentFileText.Text = "Stopping...";
                CancelScanButton.IsEnabled = false;
            }
        }

        private void ShowScanResults(UnifiedScanResult result)
        {
            ScanButtonArea.Visibility = Visibility.Collapsed;
            ScanResultsArea.Visibility = Visibility.Visible;

            if (result.Threats.Count == 0)
            {
                ResultIcon.Text = "✓";
                ResultTitle.Text = "Scan Complete - No Issues Found";
                ResultSubtitle.Text = $"Scanned {result.FilesScanned:N0} files in {result.Duration.TotalSeconds:F0}s";
                AutoFixButton.IsEnabled = false;
            }
            else
            {
                ResultIcon.Text = "⚠";
                ResultTitle.Text = $"{result.Threats.Count} Issues Found";
                ResultSubtitle.Text = $"Scanned {result.FilesScanned:N0} files - Click 'Fix Issues Now' to resolve";
                AutoFixButton.IsEnabled = result.Threats.Any(t => t.CanRemove);
            }

            UpdateIssuesDisplay();
        }

        private void UpdateCategoryCount(string category)
        {
            // Update category panel counts dynamically
        }

        private void ConvertUnifiedThreatsToIssues()
        {
            _currentIssues.Clear();
            foreach (var threat in _currentUnifiedThreats)
            {
                _currentIssues.Add(new SystemIssue
                {
                    Title = threat.Name,
                    Description = threat.Description,
                    Type = threat.Category == "Spyware" ? SystemIssueType.Security : SystemIssueType.Software,
                    Severity = threat.Severity == SeverityLevel.Critical ? IssueSeverity.Critical :
                               threat.Severity == SeverityLevel.High ? IssueSeverity.Error :
                               threat.Severity == SeverityLevel.Medium ? IssueSeverity.Warning : IssueSeverity.Info,
                    Recommendation = $"Location: {threat.Location}",
                    CanAutoFix = threat.CanRemove,
                    DetectedAt = DateTime.Now
                });
            }
        }

        private async Task PerformScanAsync(bool fullScan)
        {
            try
            {
                _lastScanResult = await _scanner.PerformFullScanAsync();
                _currentIssues = _lastScanResult.Issues;
                UpdateIssuesDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AutoFixButton_Click(object sender, RoutedEventArgs e)
        {
            var removableThreats = _currentUnifiedThreats.Where(t => t.CanRemove).ToList();
            if (!removableThreats.Any())
            {
                MessageBox.Show("No removable threats found.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Check admin status but don't block - just warn
            bool isAdmin = ThreatRemover.IsRunningAsAdmin();
            string confirmMsg = $"Remove {removableThreats.Count} threats?";
            if (!isAdmin)
            {
                confirmMsg += "\n\n⚠️ Note: Some items may require admin rights. We'll remove what we can.";
            }

            var result = MessageBox.Show(confirmMsg, "Confirm", 
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                AutoFixButton.IsEnabled = false;
                int successCount = 0, failCount = 0, protectedCount = 0, needsAdminCount = 0;
                
                for (int i = 0; i < removableThreats.Count; i++)
                {
                    var threat = removableThreats[i];
                    ScanSubText.Text = $"Removing {i + 1}/{removableThreats.Count}: {threat.Name}";
                    
                    var removeResult = await _threatRemover.RemoveThreatAsync(threat);
                    
                    if (removeResult.Success) 
                    { 
                        successCount++; 
                        _currentUnifiedThreats.Remove(threat); 
                    }
                    else if (removeResult.IsProtectedSystem) 
                    { 
                        protectedCount++; 
                        threat.CanRemove = false; 
                    }
                    else if (removeResult.Message?.Contains("Access denied") == true || 
                             removeResult.Message?.Contains("administrator") == true)
                    { 
                        needsAdminCount++; 
                    }
                    else 
                    { 
                        failCount++; 
                    }
                }
                
                ConvertUnifiedThreatsToIssues();
                UpdateIssuesDisplay();
                
                var msg = "";
                if (successCount > 0) msg += $"✅ {successCount} removed\n";
                if (protectedCount > 0) msg += $"🛡️ {protectedCount} protected Windows files (safe to ignore)\n";
                if (needsAdminCount > 0) msg += $"🔒 {needsAdminCount} need manual removal (in use or protected)\n";
                if (failCount > 0) msg += $"❌ {failCount} failed\n";
                
                if (string.IsNullOrEmpty(msg)) msg = "No changes made.";
                
                MessageBox.Show(msg.TrimEnd(), "Results", MessageBoxButton.OK, MessageBoxImage.Information);
                ResultTitle.Text = $"{_currentUnifiedThreats.Count} Issues Remaining";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AutoFixButton.IsEnabled = _currentUnifiedThreats.Any(t => t.CanRemove);
            }
        }

        private void UpdateIssuesDisplay()
        {
            if (IssuesPanel == null) return;
            IssuesPanel.Children.Clear();
            
            if (!_currentIssues.Any())
            {
                IssuesPanel.Children.Add(new TextBlock
                {
                    Text = "🎉 No issues detected! Your system is clean.",
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 153, 170)),
                    FontSize = 14, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                });
                return;
            }

            // Group by category
            var grouped = _currentIssues.GroupBy(i => i.Type.ToString());
            foreach (var group in grouped)
            {
                var categoryBorder = CreateCategorySection(group.Key, group.ToList());
                IssuesPanel.Children.Add(categoryBorder);
            }
        }

        private Border CreateCategorySection(string category, List<SystemIssue> issues)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 22, 40)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel();
            
            // Header
            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = GetCategoryIcon(category);
            var iconText = new TextBlock { Text = icon, FontSize = 20, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(iconText, 0);
            header.Children.Add(iconText);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock { Text = category, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            titleStack.Children.Add(new TextBlock { Text = $"{issues.Count} issues found", Foreground = new SolidColorBrush(Color.FromRgb(107, 123, 140)), FontSize = 11 });
            Grid.SetColumn(titleStack, 1);
            header.Children.Add(titleStack);

            var countBadge = new Border
            {
                Background = issues.Any(i => i.Severity == IssueSeverity.Critical) ? Brushes.Red : new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 8, 4)
            };
            countBadge.Child = new TextBlock { Text = issues.Count.ToString(), Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold };
            Grid.SetColumn(countBadge, 2);
            header.Children.Add(countBadge);

            stack.Children.Add(header);

            // Issues - show ALL of them in a scrollable area
            var issuesScroll = new ScrollViewer 
            { 
                MaxHeight = 400, 
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var issuesStack = new StackPanel();
            
            foreach (var issue in issues)
            {
                issuesStack.Children.Add(CreateIssueRow(issue));
            }
            
            issuesScroll.Content = issuesStack;
            stack.Children.Add(issuesScroll);

            border.Child = stack;
            return border;
        }

        private Border CreateIssueRow(SystemIssue issue)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 40, 71)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var checkbox = new CheckBox { IsChecked = issue.CanAutoFix, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(checkbox, 0);
            grid.Children.Add(checkbox);

            var contentStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            contentStack.Children.Add(new TextBlock { Text = issue.Title, Foreground = Brushes.White, FontSize = 12 });
            contentStack.Children.Add(new TextBlock { Text = issue.Description, Foreground = new SolidColorBrush(Color.FromRgb(107, 123, 140)), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(contentStack, 1);
            grid.Children.Add(contentStack);

            var severityDot = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = issue.Severity == IssueSeverity.Critical ? Brushes.Red :
                       issue.Severity == IssueSeverity.Error ? Brushes.Orange :
                       issue.Severity == IssueSeverity.Warning ? Brushes.Yellow : Brushes.LightBlue,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(severityDot, 2);
            grid.Children.Add(severityDot);

            border.Child = grid;
            return border;
        }

        private string GetCategoryIcon(string category)
        {
            return category switch
            {
                "Security" => "🛡️",
                "Performance" => "⚡",
                "Storage" => "💾",
                "Network" => "🌐",
                "Registry" => "📝",
                "Software" => "📦",
                "Services" => "⚙️",
                _ => "📋"
            };
        }

        // System metrics
        private PerformanceCounter? _cpuCounter;
        private bool _cpuCounterInitialized = false;

        private async Task UpdateSystemMetricsAsync()
        {
            try
            {
                if (CpuUsageText == null) return;

                var cpuUsage = await GetCpuUsageAsync();
                CpuUsageText.Text = $"{cpuUsage:F0}%";
                CpuUsageBar.Value = cpuUsage;
                
                var memoryInfo = await GetMemoryInfoAsync();
                MemoryUsageText.Text = $"{memoryInfo.UsagePercent:F0}%";
                MemoryUsageBar.Value = memoryInfo.UsagePercent;
                
                var diskUsage = GetDiskUsage();
                DiskUsageText.Text = $"{diskUsage:F0}%";
                DiskUsageBar.Value = diskUsage;
            }
            catch { }
        }

        private async Task<double> GetCpuUsageAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!_cpuCounterInitialized)
                    {
                        try
                        {
                            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                            _cpuCounter.NextValue();
                            _cpuCounterInitialized = true;
                        }
                        catch { _cpuCounterInitialized = true; }
                    }
                    
                    if (_cpuCounter != null)
                    {
                        System.Threading.Thread.Sleep(100);
                        return Math.Round(_cpuCounter.NextValue(), 1);
                    }
                }
                catch { }
                return 0;
            });
        }

        private async Task<(double UsagePercent, double TotalGB, double AvailableGB)> GetMemoryInfoAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var query = new System.Management.ObjectQuery("SELECT * FROM Win32_OperatingSystem");
                    using var searcher = new System.Management.ManagementObjectSearcher(query);
                    foreach (var obj in searcher.Get())
                    {
                        var totalMemoryKB = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        var freeMemoryKB = Convert.ToDouble(obj["FreePhysicalMemory"]);
                        var totalGB = totalMemoryKB / 1024 / 1024;
                        var freeGB = freeMemoryKB / 1024 / 1024;
                        var usedGB = totalGB - freeGB;
                        return (Math.Round((usedGB / totalGB) * 100, 1), Math.Round(totalGB, 1), Math.Round(freeGB, 1));
                    }
                }
                catch { }
                return (0, 0, 0);
            });
        }

        private double GetDiskUsage()
        {
            try
            {
                var drive = new DriveInfo("C:");
                if (drive.IsReady)
                    return (1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;
            }
            catch { }
            return 0;
        }

        // Tool button handlers
        private void DiskCleanupButton_Click(object sender, RoutedEventArgs e) => Process.Start("cleanmgr");
        private void DiskCleanupButton_Click(object sender, MouseButtonEventArgs e) => DiskCleanupButton_Click(sender, new RoutedEventArgs());
        private void TaskManagerButton_Click(object sender, RoutedEventArgs e) => Process.Start("taskmgr");
        private void TaskManagerButton_Click(object sender, MouseButtonEventArgs e) => TaskManagerButton_Click(sender, new RoutedEventArgs());
        private void DeviceManagerButton_Click(object sender, RoutedEventArgs e) => Process.Start("devmgmt.msc");
        private void DeviceManagerButton_Click(object sender, MouseButtonEventArgs e) => DeviceManagerButton_Click(sender, new RoutedEventArgs());
        private void NetworkResetButton_Click(object sender, RoutedEventArgs e) => Process.Start("ms-settings:network");
        private void NetworkResetButton_Click(object sender, MouseButtonEventArgs e) => NetworkResetButton_Click(sender, new RoutedEventArgs());
        private void SystemInfoButton_Click(object sender, RoutedEventArgs e) => Process.Start("msinfo32");
        private void SystemInfoButton_Click(object sender, MouseButtonEventArgs e) => SystemInfoButton_Click(sender, new RoutedEventArgs());
        
        private void ExportReportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUnifiedThreats == null || !_currentUnifiedThreats.Any())
            {
                MessageBox.Show("No scan results to export.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Scan Report",
                Filter = "Text File (*.txt)|*.txt",
                FileName = $"ScanReport_{DateTime.Now:yyyy-MM-dd_HHmm}"
            };

            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("ATLAS AI - SYSTEM SCAN REPORT");
                sb.AppendLine($"Generated: {DateTime.Now:g}");
                sb.AppendLine($"Total Issues: {_currentUnifiedThreats.Count}");
                sb.AppendLine();
                
                foreach (var threat in _currentUnifiedThreats)
                {
                    sb.AppendLine($"[{threat.Severity}] {threat.Name}");
                    sb.AppendLine($"  Location: {threat.Location}");
                    sb.AppendLine($"  {threat.Description}");
                    sb.AppendLine();
                }
                
                File.WriteAllText(dialog.FileName, sb.ToString());
                MessageBox.Show("Report exported!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void ExportReportButton_Click(object sender, MouseButtonEventArgs e) => ExportReportButton_Click(sender, new RoutedEventArgs());

        // Legacy event handlers (for compatibility)
        private void OnScanProgress(string message) => Dispatcher.Invoke(() => { if (ScanSubText != null) ScanSubText.Text = message; });
        private void OnIssueDetected(SystemIssue issue) { }
        private void OnFixProgress(string message) => Dispatcher.Invoke(() => { if (ScanSubText != null) ScanSubText.Text = message; });
        private void OnFixCompleted(SystemIssue issue, FixAttemptResult result) { }
        private void OnSpywareScanProgress(string message) => Dispatcher.Invoke(() => { if (ScanSubText != null) ScanSubText.Text = message; });
        private void OnThreatDetected(SpywareThreat threat) { }
        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateIssuesDisplay();
        private void RefreshButton_Click(object sender, RoutedEventArgs e) => UpdateIssuesDisplay();
        private void RestartServicesButton_Click(object sender, RoutedEventArgs e) => Process.Start("services.msc");
        private void SecurityScanButton_Click(object sender, RoutedEventArgs e) { }
        private void PerformanceTuneButton_Click(object sender, RoutedEventArgs e) => Process.Start("perfmon");
        private void DiskManagementButton_Click(object sender, RoutedEventArgs e) => Process.Start("diskmgmt.msc");
        private void EventViewerButton_Click(object sender, RoutedEventArgs e) => Process.Start("eventvwr");
        private void UpdateHealthIndicator(double cpu, double mem, double disk) { }
        private void UpdateScanSummary() { }
        private void UpdateSystemOverview() { }

        protected override void OnClosed(EventArgs e)
        {
            _systemMonitorTimer?.Stop();
            _scanAnimationTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
