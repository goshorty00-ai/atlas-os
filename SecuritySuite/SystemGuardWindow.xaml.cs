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
using AtlasAI.AI;

namespace AtlasAI.SecuritySuite
{
    public partial class SystemGuardWindow : Window
    {
        private DispatcherTimer _updateTimer;
        private PerformanceCounter _cpuCounter;
        private List<double> _cpuHistory = new();
        private List<double> _ramHistory = new();
        private long _lastBytesReceived = 0;
        private long _lastBytesSent = 0;
        private DateTime _lastNetCheck = DateTime.Now;
        private List<string> _recentEvents = new();

        public SystemGuardWindow()
        {
            InitializeComponent();
            InitializeCounters();
            StartMonitoring();
            AddAiMessage("👋 Hello! I'm Atlas Guard AI. I'm monitoring your system in real-time. Ask me anything about your PC's health, running processes, or security status.");
            LoadSystemInfo();
        }

        private void InitializeCounters()
        {
            try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); _cpuCounter.NextValue(); }
            catch { _cpuCounter = null; }
        }

        private void StartMonitoring()
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _updateTimer.Tick += UpdateMetrics;
            _updateTimer.Start();
        }

        private void LoadSystemInfo()
        {
            Task.Run(() =>
            {
                try
                {
                    // CPU Name
                    using var cpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                    foreach (var obj in cpuSearcher.Get())
                    {
                        var name = obj["Name"]?.ToString() ?? "Unknown CPU";
                        Dispatcher.Invoke(() => CpuName.Text = name.Length > 30 ? name.Substring(0, 30) + "..." : name);
                        break;
                    }
                }
                catch { Dispatcher.Invoke(() => CpuName.Text = "CPU Info Unavailable"); }

                try
                {
                    // GPU Name
                    using var gpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    foreach (var obj in gpuSearcher.Get())
                    {
                        var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                        Dispatcher.Invoke(() => GpuName.Text = name.Length > 25 ? name.Substring(0, 25) + "..." : name);
                        break;
                    }
                }
                catch { Dispatcher.Invoke(() => GpuName.Text = "GPU Info Unavailable"); }

                // Disks
                Dispatcher.Invoke(() => LoadDiskInfo());
            });
        }

        private void LoadDiskInfo()
        {
            DiskList.Children.Clear();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var usedPercent = (int)(100 - (drive.AvailableFreeSpace * 100.0 / drive.TotalSize));
                var usedGB = (drive.TotalSize - drive.AvailableFreeSpace) / (1024.0 * 1024 * 1024);
                var totalGB = drive.TotalSize / (1024.0 * 1024 * 1024);

                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock { Text = drive.Name.Replace("\\", ""), Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)), FontSize = 11, FontWeight = FontWeights.SemiBold };
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                var barBg = new Border { Background = new SolidColorBrush(Color.FromArgb(26, 34, 197, 94)), Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(8, 0, 8, 0) };
                var barFill = new Border { Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)), Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = barBg.ActualWidth * usedPercent / 100 };
                barBg.Child = barFill;
                barBg.SizeChanged += (s, e) => barFill.Width = e.NewSize.Width * usedPercent / 100;
                Grid.SetColumn(barBg, 1);
                row.Children.Add(barBg);

                var info = new TextBlock { Text = $"{usedGB:F0}/{totalGB:F0}GB", Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), FontSize = 9 };
                Grid.SetColumn(info, 2);
                row.Children.Add(info);

                DiskList.Children.Add(row);
            }
        }

        private void UpdateMetrics(object sender, EventArgs e)
        {
            TimeText.Text = DateTime.Now.ToString("HH:mm:ss");

            // CPU
            try
            {
                var cpu = _cpuCounter?.NextValue() ?? 0;
                CpuPercent.Text = $"{cpu:F0}%";
                _cpuHistory.Add(cpu);
                if (_cpuHistory.Count > 60) _cpuHistory.RemoveAt(0);
                DrawGraph(CpuGraph, _cpuHistory, Color.FromRgb(34, 211, 238));

                if (cpu > 90) AddActivity("⚠️ High CPU usage detected: " + cpu.ToString("F0") + "%", "#F59E0B");
            }
            catch { }

            // RAM
            try
            {
                var memInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                var totalRam = memInfo.TotalPhysicalMemory / (1024.0 * 1024 * 1024);
                var availRam = memInfo.AvailablePhysicalMemory / (1024.0 * 1024 * 1024);
                var usedRam = totalRam - availRam;
                var ramPercent = (usedRam / totalRam) * 100;

                RamPercent.Text = $"{ramPercent:F0}%";
                RamInfo.Text = $"{usedRam:F1} / {totalRam:F1} GB";
                _ramHistory.Add(ramPercent);
                if (_ramHistory.Count > 60) _ramHistory.RemoveAt(0);
                DrawGraph(RamGraph, _ramHistory, Color.FromRgb(255, 107, 0));

                if (ramPercent > 90) AddActivity("⚠️ High memory usage: " + ramPercent.ToString("F0") + "%", "#F59E0B");
            }
            catch { }

            // GPU (simplified - just show placeholder)
            GpuPercent.Text = "N/A";
            GpuTemp.Text = "Temp: N/A";

            // Network
            try
            {
                var now = DateTime.Now;
                var elapsed = (now - _lastNetCheck).TotalSeconds;
                if (elapsed > 0)
                {
                    long totalReceived = 0, totalSent = 0;
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                        {
                            var stats = ni.GetIPv4Statistics();
                            totalReceived += stats.BytesReceived;
                            totalSent += stats.BytesSent;
                        }
                    }
                    var downSpeed = (totalReceived - _lastBytesReceived) / elapsed;
                    var upSpeed = (totalSent - _lastBytesSent) / elapsed;
                    NetDown.Text = FormatSpeed(downSpeed);
                    NetUp.Text = FormatSpeed(upSpeed);
                    _lastBytesReceived = totalReceived;
                    _lastBytesSent = totalSent;
                    _lastNetCheck = now;
                }
            }
            catch { }

            // Update processes every 3 seconds
            if (DateTime.Now.Second % 3 == 0) UpdateProcessList();
        }

        private string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
            if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F1} KB/s";
            return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
        }

        private void DrawGraph(Canvas canvas, List<double> data, Color color)
        {
            canvas.Children.Clear();
            if (data.Count < 2) return;

            var w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 150;
            var h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 30;
            var poly = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = 1.5 };

            for (int i = 0; i < data.Count; i++)
            {
                var x = i * w / (data.Count - 1);
                var y = h - (data[i] / 100 * h);
                poly.Points.Add(new Point(x, Math.Max(0, Math.Min(h, y))));
            }
            canvas.Children.Add(poly);
        }

        private void UpdateProcessList()
        {
            Task.Run(() =>
            {
                var procs = Process.GetProcesses()
                    .Select(p => { try { return new { p.ProcessName, Mem = p.WorkingSet64, p.Id }; } catch { return null; } })
                    .Where(p => p != null)
                    .OrderByDescending(p => p.Mem)
                    .Take(15)
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    ProcessCount.Text = $" ({Process.GetProcesses().Length} running)";
                    ProcessList.Children.Clear();

                    foreach (var p in procs)
                    {
                        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var name = new TextBlock { Text = p.ProcessName, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis };
                        var mem = new TextBlock { Text = $"{p.Mem / (1024 * 1024):F0} MB", Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), FontSize = 9 };
                        Grid.SetColumn(mem, 1);

                        row.Children.Add(name);
                        row.Children.Add(mem);
                        ProcessList.Children.Add(row);
                    }
                });
            });
        }

        private void AddActivity(string message, string colorHex)
        {
            var key = message.GetHashCode().ToString();
            if (_recentEvents.Contains(key)) return;
            _recentEvents.Add(key);
            if (_recentEvents.Count > 20) _recentEvents.RemoveAt(0);

            Dispatcher.Invoke(() =>
            {
                var entry = new TextBlock
                {
                    Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                };
                ActivityFeed.Children.Insert(0, entry);
                if (ActivityFeed.Children.Count > 50) ActivityFeed.Children.RemoveAt(ActivityFeed.Children.Count - 1);
            });
        }

        private void AddAiMessage(string message, bool isUser = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            
            // Header with dot, name, and time
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var dot = new Ellipse 
            { 
                Width = 8, Height = 8, 
                Fill = new SolidColorBrush(isUser ? Color.FromRgb(139, 92, 246) : Color.FromRgb(34, 211, 238)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var name = new TextBlock 
            { 
                Text = isUser ? "You" : "Atlas", 
                Foreground = new SolidColorBrush(isUser ? Color.FromRgb(139, 92, 246) : Color.FromRgb(34, 211, 238)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            var time = new TextBlock 
            { 
                Text = $" . {DateTime.Now:h:mm tt}", 
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 11
            };
            header.Children.Add(dot);
            header.Children.Add(name);
            header.Children.Add(time);
            
            // Message bubble
            var bubble = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(26, isUser ? (byte)139 : (byte)34, isUser ? (byte)92 : (byte)211, isUser ? (byte)246 : (byte)238)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(51, isUser ? (byte)139 : (byte)34, isUser ? (byte)92 : (byte)211, isUser ? (byte)246 : (byte)238)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10)
            };
            bubble.Child = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };
            
            container.Children.Add(header);
            container.Children.Add(bubble);
            ChatMessages.Children.Add(container);
            ChatScroll.ScrollToEnd();
        }

        private async void SendChat_Click(object sender, RoutedEventArgs e) => await SendChatMessage();
        private async void ChatInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await SendChatMessage(); }

        private System.Threading.CancellationTokenSource? _chatCts;

        private async Task SendChatMessage()
        {
            var msg = ChatInput.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;

            ChatInput.Text = "";
            AddAiMessage(msg, true);
            AiStatus.Text = "Thinking...";
            AiStatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));

            _chatCts?.Cancel();
            _chatCts = new System.Threading.CancellationTokenSource();
            var ct = _chatCts.Token;

            try
            {
                var systemContext = GetSystemContext();
                var response = await AIManager.SendMessageAsync(new List<object>
                {
                    new { role = "system", content = "You are Atlas Guard AI, a helpful system monitoring assistant. You watch the user's PC and help them understand what's happening. Be concise and helpful. Current system status:\n" + systemContext },
                    new { role = "user", content = msg }
                }, 500, ct);

                if (response.Success)
                    AddAiMessage(response.Content);
                else
                {
                    if (ct.IsCancellationRequested)
                        AddAiMessage("CANCELLED · OPERATION STOPPED");
                    else
                        AddAiMessage("Sorry, I couldn't process that. " + response.Error);
                }
            }
            catch (OperationCanceledException)
            {
                AddAiMessage("CANCELLED · OPERATION STOPPED");
            }
            catch (Exception ex)
            {
                AddAiMessage("Error: " + ex.Message);
            }
            finally
            {
                if (_chatCts?.Token == ct)
                {
                    _chatCts?.Dispose();
                    _chatCts = null;
                }
            }

            AiStatus.Text = "Watching your system";
            AiStatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        }

        private string GetSystemContext()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"CPU: {CpuPercent.Text}");
            sb.AppendLine($"RAM: {RamPercent.Text} ({RamInfo.Text})");
            sb.AppendLine($"Processes: {ProcessCount.Text}");
            sb.AppendLine($"Network: Down {NetDown.Text}, Up {NetUp.Text}");
            return sb.ToString();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeBtn.Content = "☐";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeBtn.Content = "❐";
            }
        }
        private void Close_Click(object sender, RoutedEventArgs e) { _updateTimer?.Stop(); Close(); }
        
        // Chat panel toggle
        private bool _chatVisible = true;
        private void ToggleChat_Click(object sender, RoutedEventArgs e)
        {
            _chatVisible = !_chatVisible;
            ChatPanel.Visibility = _chatVisible ? Visibility.Visible : Visibility.Collapsed;
            ChatToggleBtn.Visibility = _chatVisible ? Visibility.Collapsed : Visibility.Visible;
        }
        
        private void HideChat_Click(object sender, RoutedEventArgs e)
        {
            _chatVisible = false;
            ChatPanel.Visibility = Visibility.Collapsed;
            ChatToggleBtn.Visibility = Visibility.Visible;
        }
        
        // Voice input
        private bool _isListening = false;
        private void Voice_Click(object sender, RoutedEventArgs e)
        {
            _isListening = !_isListening;
            if (_isListening)
            {
                VoiceIcon.Text = "🔴";
                AiStatus.Text = "Listening...";
                AiStatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                // TODO: Start voice recognition
            }
            else
            {
                VoiceIcon.Text = "🎤";
                AiStatus.Text = "Watching your system";
                AiStatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                // TODO: Stop voice recognition
            }
        }
    }
}
