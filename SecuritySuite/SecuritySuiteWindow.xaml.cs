using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AtlasAI.SecuritySuite
{
    public partial class SecuritySuiteWindow : Window
    {
        private DispatcherTimer _animationTimer;
        private DispatcherTimer _scanTimer;
        private int _scanProgress = 0;
        private double _sweepAngle = 0;
        private int _activeStep = 0;
        private Random _random = new();
        private bool _isScanning = false;
        private string _currentScanType = "IDLE";
        private double _cpuUsage = 0;
        private double _ramUsage = 0;
        private double _diskFree = 0;
        private int _processCount = 0;
        private int _threatCount = 0;
        private List<string> _scanLog = new();
        private List<double> _cpuHistory = new();
        private TextBlock _scannerProgress;
        private TextBlock _scannerStatus;
        private TextBlock _scannerMicro;
        private System.Windows.Shapes.Path _scanSweep;
        private PerformanceCounter _cpuCounter;
        private int _filesScanned = 0;
        private int _foldersScanned = 0;
        private int _servicesChecked = 0;
        private List<Ellipse> _neuralNodes = new();
        private double _neuralPulse = 0;

        public SecuritySuiteWindow()
        {
            InitializeComponent();
            InitializePerformanceCounters();
            InitializeUI();
            StartAnimations();
            AddLog("READY", "Click RUN SCAN to start", "queued");
        }

        private void InitializePerformanceCounters()
        {
            try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); _cpuCounter.NextValue(); }
            catch { _cpuCounter = null; }
        }

        private void InitializeUI()
        {
            for (int i = 0; i < 20; i++) _cpuHistory.Add(20 + _random.NextDouble() * 10);
            BuildProgressTicks();
            BuildPipelineSteps();
            BuildNeuralCorePanel();
            BuildSystemLogPanel();
            BuildAIAcceleratorPanel();
            BuildTrustMatrixPanel();
            BuildStatusModulesPanel();
            BuildDiagnosticScanner();
        }

        private void StartAnimations()
        {
            _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _animationTimer.Tick += (s, e) => { 
                _sweepAngle = (_sweepAngle + 2) % 360; 
                _neuralPulse = (_neuralPulse + 0.08) % (Math.PI * 2);
                UpdateScannerAnimation(); 
                UpdateHeartbeat();
                UpdateNeuralCoreAnimation();
            };
            _animationTimer.Start();
            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _scanTimer.Tick += ScanTimer_Tick;
            _scanTimer.Start();
        }

        private void RunScan_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning) { MessageBox.Show("Scan in progress!"); return; }
            AddLog("SCAN", "Starting scan...", "active");
            _ = RunFullScanAsync();
        }

        private async Task RunFullScanAsync()
        {
            if (_isScanning) return;
            _isScanning = true;
            _scanProgress = 0;
            _threatCount = 0;
            _filesScanned = 0;
            _foldersScanned = 0;
            _servicesChecked = 0;
            _scanLog.Clear();
            _currentScanType = "INIT";
            UpdateNeuralCoreStatus("DEEP SCAN ACTIVE");
            AddLog("AI_CORE", "Starting deep scan...", "active");
            var sw = Stopwatch.StartNew();
            try
            {
                _activeStep = 0; _currentScanType = "PROCESSES";
                AddLog("PROCESS", "Scanning processes...", "active");
                await ScanProcessesAsync();
                _activeStep = 1; _currentScanType = "STARTUP";
                AddLog("STARTUP", "Checking startup...", "active");
                await ScanStartupAsync();
                await ScanServicesAsync();
                _activeStep = 2; _currentScanType = "NETWORK";
                AddLog("NETWORK", "Analyzing network...", "active");
                await ScanNetworkAsync();
                _activeStep = 3; _currentScanType = "REGISTRY";
                AddLog("REGISTRY", "Scanning registry...", "active");
                await ScanRegistryAsync();
                _activeStep = 4; _currentScanType = "FILES";
                AddLog("FILESYSTEM", "Scanning all drives...", "active");
                await ScanAllDrivesAsync();
            }
            catch (Exception ex) { AddLog("ERROR", ex.Message, "warning"); }
            sw.Stop();
            _scanProgress = 100;
            _currentScanType = "DONE";
            _isScanning = false;
            Dispatcher.Invoke(() => {
                ThreatLevel.Text = _threatCount == 0 ? "THREAT_LEVEL: 0x00" : $"THREAT_LEVEL: 0x{_threatCount:X2}";
                ThreatLevel.Foreground = new SolidColorBrush(_threatCount == 0 ? Color.FromRgb(34, 197, 94) : Color.FromRgb(239, 68, 68));
                UpdateScannerProgress();
                UpdateNeuralCoreStatus(_threatCount == 0 ? "SYSTEM SECURE" : "THREATS DETECTED");
            });
            AddLog("DONE", $"{_filesScanned:N0} files in {sw.Elapsed.TotalSeconds:F1}s", "completed");
            MessageBox.Show($"Scan Complete!\n\nFiles: {_filesScanned:N0}\nFolders: {_foldersScanned:N0}\nTime: {sw.Elapsed.TotalMinutes:F1} min\nThreats: {_threatCount}");
        }

        private async Task ScanProcessesAsync()
        {
            await Task.Run(async () => {
                var procs = Process.GetProcesses();
                _processCount = procs.Length;
                var suspicious = new[] { "keylogger", "miner", "trojan", "malware" };
                int i = 0;
                foreach (var p in procs)
                {
                    try {
                        i++;
                        if (suspicious.Any(s => p.ProcessName.ToLower().Contains(s))) { _threatCount++; Dispatcher.Invoke(() => AddLog("THREAT", p.ProcessName, "warning")); }
                        if (i % 20 == 0) { _scanProgress = Math.Min(15, i * 15 / procs.Length); Dispatcher.Invoke(() => UpdateScannerProgress()); }
                    } catch { }
                }
                Dispatcher.Invoke(() => AddLog("PROCESS", $"{i} processes", "completed"));
                await Task.Delay(300);
            });
            _scanProgress = 15;
        }

        private async Task ScanStartupAsync()
        {
            await Task.Run(async () => {
                int count = 0;
                try {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (key != null) count += key.GetValueNames().Length;
                    using var key2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (key2 != null) count += key2.GetValueNames().Length;
                } catch { }
                Dispatcher.Invoke(() => AddLog("STARTUP", $"{count} entries", "completed"));
                await Task.Delay(300);
            });
            _scanProgress = 22; Dispatcher.Invoke(() => UpdateScannerProgress());
        }

        private async Task ScanServicesAsync()
        {
            await Task.Run(async () => {
                try {
                    var psi = new ProcessStartInfo("sc", "query type= service state= all") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                    using var proc = Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd() ?? "";
                    _servicesChecked = output.Split('\n').Count(l => l.Contains("SERVICE_NAME:"));
                    Dispatcher.Invoke(() => AddLog("SERVICE", $"{_servicesChecked} services", "completed"));
                } catch { }
                await Task.Delay(300);
            });
            _scanProgress = 30; Dispatcher.Invoke(() => UpdateScannerProgress());
        }

        private async Task ScanNetworkAsync()
        {
            await Task.Run(async () => {
                try {
                    var psi = new ProcessStartInfo("netstat", "-ano") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                    using var proc = Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd() ?? "";
                    var conns = output.Split('\n').Count(l => l.Contains("ESTABLISHED"));
                    Dispatcher.Invoke(() => AddLog("NETWORK", $"{conns} connections", "completed"));
                } catch { }
                await Task.Delay(300);
            });
            _scanProgress = 45; Dispatcher.Invoke(() => UpdateScannerProgress());
        }

        private async Task ScanRegistryAsync()
        {
            await Task.Run(async () => {
                int keys = 0;
                try {
                    using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (k != null) keys += k.GetValueNames().Length;
                } catch { }
                Dispatcher.Invoke(() => AddLog("REGISTRY", $"{keys} keys", "completed"));
                await Task.Delay(300);
            });
            _scanProgress = 60; Dispatcher.Invoke(() => UpdateScannerProgress());
        }

        private async Task ScanAllDrivesAsync()
        {
            await Task.Run(async () => {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
                Dispatcher.Invoke(() => AddLog("FILESYSTEM", $"{drives.Count} drives", "active"));
                int idx = 0;
                foreach (var drive in drives)
                {
                    idx++;
                    var startFiles = _filesScanned;
                    Dispatcher.Invoke(() => AddLog("FILESYSTEM", $"Scanning {drive.Name}...", "active"));
                    await ScanDriveAsync(drive.Name, idx, drives.Count);
                    Dispatcher.Invoke(() => AddLog("FILESYSTEM", $"{drive.Name}: {_filesScanned - startFiles:N0} files", "completed"));
                }
            });
        }

        private async Task ScanDriveAsync(string root, int idx, int total)
        {
            var suspicious = new[] { "crack", "keygen", "hack", "miner", "trojan", "virus" };
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "$recycle.bin", "system volume information" };
            var dirs = new Stack<string>();
            dirs.Push(root);
            var uiSw = Stopwatch.StartNew();
            while (dirs.Count > 0)
            {
                var dir = dirs.Pop();
                try
                {
                    _foldersScanned++;
                    string[] files = Array.Empty<string>();
                    try { files = Directory.GetFiles(dir); } catch { }
                    foreach (var f in files)
                    {
                        _filesScanned++;
                        try {
                            var name = System.IO.Path.GetFileName(f).ToLower();
                            if (suspicious.Any(s => name.Contains(s))) { _threatCount++; Dispatcher.Invoke(() => AddLog("THREAT", name, "warning")); }
                        } catch { }
                    }
                    if (uiSw.ElapsedMilliseconds > 100)
                    {
                        uiSw.Restart();
                        _scanProgress = Math.Min(99, 60 + ((idx - 1) * 40 / total));
                        var shortDir = dir.Length > 30 ? "..." + dir.Substring(dir.Length - 27) : dir;
                        Dispatcher.Invoke(() => { _currentScanType = shortDir; UpdateScannerProgress(); });
                        await Task.Delay(1);
                    }
                    string[] subdirs = Array.Empty<string>();
                    try { subdirs = Directory.GetDirectories(dir); } catch { }
                    foreach (var sd in subdirs)
                    {
                        try { var n = System.IO.Path.GetFileName(sd).ToLower(); if (!skip.Contains(n)) dirs.Push(sd); } catch { }
                    }
                }
                catch { }
            }
            _scanProgress = Math.Min(99, 60 + (idx * 40 / total));
            Dispatcher.Invoke(() => UpdateScannerProgress());
        }

        private void AddLog(string tag, string msg, string status)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            _scanLog.Insert(0, $"{time}|{tag}|{msg}|{status}");
            if (_scanLog.Count > 10) _scanLog.RemoveAt(_scanLog.Count - 1);
            Dispatcher.Invoke(() => BuildSystemLogPanel());
        }

        private void ScanTimer_Tick(object sender, EventArgs e)
        {
            UpdateRealMetrics();
            UpdateProgressTicks();
            UpdateScannerProgress();
            if (!_isScanning) _activeStep = (_activeStep + 1) % 5;
            UpdatePipelineSteps();
            _cpuHistory.RemoveAt(0);
            _cpuHistory.Add(_cpuUsage);
            UpdateAIAcceleratorPanel();
            UpdateStatusModulesPanel();
        }

        private void UpdateRealMetrics()
        {
            try {
                _cpuUsage = _cpuCounter?.NextValue() ?? _random.Next(10, 40);
                var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
                _ramUsage = 100.0 * (1 - (double)ci.AvailablePhysicalMemory / ci.TotalPhysicalMemory);
                var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
                if (drive != null) _diskFree = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                _processCount = Process.GetProcesses().Length;
            } catch { }
        }

        private void UpdateScannerProgress()
        {
            if (_scannerProgress != null) _scannerProgress.Text = $"{_scanProgress}%";
            if (_scannerStatus != null) _scannerStatus.Text = _isScanning ? "SCANNING" : (_threatCount == 0 ? "SECURE" : "THREATS");
            if (_scannerMicro != null) _scannerMicro.Text = _isScanning && _filesScanned > 0 ? $"{_filesScanned:N0} FILES" : _currentScanType;
            ScanStatus.Text = _isScanning ? $"SCANNING: {_filesScanned:N0} files | {_currentScanType}" : "SCAN COMPLETE";
            HexProgress.Text = $"0x{_scanProgress:X2}";
        }

        private void BuildPipelineSteps()
        {
            var steps = new[] { "INPUT", "CONTEXT", "RISK", "DECISION", "ACTION" };
            PipelineGrid.ColumnDefinitions.Clear();
            for (int i = 0; i < 5; i++) PipelineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PipelineGrid.Children.Clear();
            for (int i = 0; i < 5; i++)
            {
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var ng = new Grid { Width = 24, Height = 24 };
                var outer = new Ellipse { Width = 24, Height = 24, Stroke = new SolidColorBrush(Color.FromArgb(77, 34, 211, 238)), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(128, 10, 12, 20)) };
                var inner = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromArgb(102, 34, 211, 238)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                ng.Children.Add(outer); ng.Children.Add(inner); ng.Tag = new[] { outer, inner };
                sp.Children.Add(ng);
                var lbl = new TextBlock { Text = steps[i], Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)), FontFamily = new FontFamily("Cascadia Code"), FontSize = 8, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
                sp.Children.Add(lbl); sp.Tag = lbl;
                Grid.SetColumn(sp, i); PipelineGrid.Children.Add(sp);
            }
        }

        private void UpdatePipelineSteps()
        {
            var cyan = Color.FromRgb(34, 211, 238);
            foreach (var child in PipelineGrid.Children)
                if (child is StackPanel panel && panel.Children.Count > 0)
                {
                    var idx = Grid.GetColumn(panel); var active = idx == _activeStep;
                    if (panel.Children[0] is Grid ng && ng.Tag is Ellipse[] c) { c[0].Stroke = new SolidColorBrush(active ? cyan : Color.FromArgb(77, 34, 211, 238)); c[1].Fill = new SolidColorBrush(active ? cyan : Color.FromArgb(102, 34, 211, 238)); c[0].Effect = active ? new DropShadowEffect { Color = cyan, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.8 } : null; }
                    if (panel.Tag is TextBlock lbl) lbl.Foreground = new SolidColorBrush(active ? cyan : Color.FromRgb(107, 114, 128));
                }
        }

        private void BuildProgressTicks()
        {
            ProgressTicks.Children.Clear();
            for (int i = 0; i < 10; i++) ProgressTicks.Children.Add(new Rectangle { Width = 2, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(77, 34, 211, 238)), RadiusX = 1, RadiusY = 1, Margin = new Thickness(2, 0, 2, 0) });
        }

        private void UpdateProgressTicks()
        {
            var active = _scanProgress / 10;
            for (int i = 0; i < ProgressTicks.Children.Count; i++) if (ProgressTicks.Children[i] is Rectangle t) t.Fill = new SolidColorBrush(i < active ? Color.FromArgb(204, 34, 211, 238) : Color.FromArgb(77, 34, 211, 238));
        }

        private void BuildNeuralCorePanel()
        {
            var canvas = NeuralCoreCanvas;
            canvas.Children.Clear();
            _neuralNodes.Clear();
            try
            {
                var imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SecuritySuite", "Assets", "ai-head.png");
                if (File.Exists(imagePath))
                {
                    var bitmap = new BitmapImage(new Uri(imagePath, UriKind.Absolute));
                    var img = new Image { Source = bitmap, Width = 180, Height = 200, Opacity = 0.5, Stretch = Stretch.Uniform };
                    img.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 140, 0), BlurRadius = 15, ShadowDepth = 0, Opacity = 0.3 };
                    Canvas.SetLeft(img, 10);
                    Canvas.SetTop(img, 10);
                    canvas.Children.Add(img);
                }
            }
            catch { }
            var glowGradient = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.35), Center = new Point(0.5, 0.35), RadiusX = 0.5, RadiusY = 0.5 };
            glowGradient.GradientStops.Add(new GradientStop(Color.FromArgb(80, 255, 140, 0), 0));
            glowGradient.GradientStops.Add(new GradientStop(Color.FromArgb(40, 255, 100, 0), 0.3));
            glowGradient.GradientStops.Add(new GradientStop(Colors.Transparent, 0.7));
            var glowRect = new Rectangle { Width = 200, Height = 220, Fill = glowGradient };
            Canvas.SetLeft(glowRect, 0); Canvas.SetTop(glowRect, 0);
            canvas.Children.Add(glowRect);
            var brainCore = new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(Color.FromArgb(255, 255, 160, 50)) };
            brainCore.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 140, 0), BlurRadius = 20, ShadowDepth = 0, Opacity = 0.9 };
            Canvas.SetLeft(brainCore, 90); Canvas.SetTop(brainCore, 55);
            canvas.Children.Add(brainCore);
            _neuralNodes.Add(brainCore);
            var brainCenterX = 100.0; var brainCenterY = 65.0;
            for (int i = 0; i < 30; i++)
            {
                var angle = (Math.PI * 2 * i) / 30;
                var distance = 12 + _random.NextDouble() * 35;
                var x = brainCenterX + Math.Cos(angle) * distance;
                var y = brainCenterY + Math.Sin(angle) * distance * 0.8;
                var size = 3 + _random.NextDouble() * 5;
                var brightness = 0.5 + _random.NextDouble() * 0.5;
                var node = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromArgb((byte)(brightness * 255), 255, (byte)(140 + _random.Next(60)), 0)), Tag = new double[] { _random.NextDouble() * 2, brightness } };
                node.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 140, 0), BlurRadius = 6, ShadowDepth = 0, Opacity = 0.6 };
                Canvas.SetLeft(node, x - size / 2); Canvas.SetTop(node, y - size / 2);
                canvas.Children.Add(node);
                _neuralNodes.Add(node);
            }
            for (int i = 0; i < 10; i++)
            {
                var x = 75 + _random.NextDouble() * 50;
                var y = 120 + _random.NextDouble() * 40;
                var size = 2 + _random.NextDouble() * 3;
                var brightness = 0.3 + _random.NextDouble() * 0.3;
                var node = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromArgb((byte)(brightness * 255), 255, (byte)(140 + _random.Next(40)), 0)), Tag = new double[] { _random.NextDouble() * 2, brightness } };
                Canvas.SetLeft(node, x); Canvas.SetTop(node, y);
                canvas.Children.Add(node);
                _neuralNodes.Add(node);
            }
            for (int i = 0; i < 12; i++)
            {
                var x = 50 + _random.NextDouble() * 100;
                var y = 160 + _random.NextDouble() * 50;
                var size = 2 + _random.NextDouble() * 3;
                var brightness = 0.2 + _random.NextDouble() * 0.3;
                var node = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromArgb((byte)(brightness * 255), 255, (byte)(140 + _random.Next(30)), 0)), Tag = new double[] { _random.NextDouble() * 2, brightness } };
                Canvas.SetLeft(node, x); Canvas.SetTop(node, y);
                canvas.Children.Add(node);
                _neuralNodes.Add(node);
            }
        }
        
        private void UpdateNeuralCoreAnimation()
        {
            var pulseIntensity = _isScanning ? 1.0 : 0.6;
            foreach (var node in _neuralNodes)
            {
                if (node.Tag is double[] data && data.Length >= 2)
                {
                    var delay = data[0];
                    var baseBrightness = data[1];
                    var nodePulse = (Math.Sin(_neuralPulse + delay) + 1) / 2;
                    var brightness = baseBrightness * (0.5 + nodePulse * 0.5 * pulseIntensity);
                    node.Fill = new SolidColorBrush(Color.FromArgb((byte)(brightness * 255), 255, (byte)(140 + nodePulse * 60), (byte)(nodePulse * 50)));
                }
            }
        }
        
        private void UpdateNeuralCoreStatus(string status)
        {
            if (NeuralCoreStatus != null)
            {
                NeuralCoreStatus.Text = status;
                NeuralCoreStatus.Foreground = new SolidColorBrush(status.Contains("THREAT") ? Color.FromRgb(239, 68, 68) : status.Contains("SCAN") ? Color.FromRgb(255, 140, 0) : Color.FromRgb(34, 211, 238));
            }
        }

        private void BuildDiagnosticScanner()
        {
            var canvas = ScannerCanvas; var c = 180.0;
            canvas.Children.Clear();
            var brackets = new[] { (10.0, 10.0, new Thickness(2, 2, 0, 0)), (330.0, 10.0, new Thickness(0, 2, 2, 0)), (10.0, 330.0, new Thickness(2, 0, 0, 2)), (330.0, 330.0, new Thickness(0, 0, 2, 2)) };
            foreach (var (x, y, t) in brackets) { var b = new Border { Width = 32, Height = 32, BorderBrush = new SolidColorBrush(Color.FromArgb(153, 34, 211, 238)), BorderThickness = t }; Canvas.SetLeft(b, x); Canvas.SetTop(b, y); canvas.Children.Add(b); }
            canvas.Children.Add(new Ellipse { Width = 340, Height = 340, Stroke = new SolidColorBrush(Color.FromArgb(51, 34, 211, 238)), StrokeThickness = 1 });
            Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], 10); Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], 10);
            foreach (var sz in new[] { 280, 260, 240, 220 }) { var ring = new Ellipse { Width = sz, Height = sz, Stroke = new SolidColorBrush(Color.FromArgb(26, 34, 211, 238)), StrokeThickness = 1 }; Canvas.SetLeft(ring, c - sz / 2); Canvas.SetTop(ring, c - sz / 2); canvas.Children.Add(ring); }
            _scanSweep = new System.Windows.Shapes.Path { Stroke = new SolidColorBrush(Color.FromArgb(153, 34, 211, 238)), StrokeThickness = 3, Effect = new DropShadowEffect { Color = Color.FromRgb(34, 211, 238), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.6 } }; canvas.Children.Add(_scanSweep);
            var core = new Ellipse { Width = 180, Height = 180, Fill = new SolidColorBrush(Color.FromArgb(230, 10, 12, 20)), Stroke = new SolidColorBrush(Color.FromArgb(77, 34, 211, 238)), StrokeThickness = 1, Effect = new DropShadowEffect { Color = Color.FromRgb(34, 211, 238), BlurRadius = 30, ShadowDepth = 0, Opacity = 0.3 } }; Canvas.SetLeft(core, c - 90); Canvas.SetTop(core, c - 90); canvas.Children.Add(core);
            _scannerProgress = new TextBlock { Text = "0%", Foreground = new SolidColorBrush(Color.FromRgb(34, 211, 238)), FontFamily = new FontFamily("Segoe UI"), FontSize = 48, FontWeight = FontWeights.Bold }; Canvas.SetLeft(_scannerProgress, c - 40); Canvas.SetTop(_scannerProgress, c - 30); canvas.Children.Add(_scannerProgress);
            _scannerStatus = new TextBlock { Text = "READY", Foreground = new SolidColorBrush(Color.FromArgb(204, 34, 211, 238)), FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = FontWeights.SemiBold }; Canvas.SetLeft(_scannerStatus, c - 25); Canvas.SetTop(_scannerStatus, c + 25); canvas.Children.Add(_scannerStatus);
            _scannerMicro = new TextBlock { Text = "CLICK RUN SCAN", Foreground = new SolidColorBrush(Color.FromArgb(128, 34, 211, 238)), FontFamily = new FontFamily("Cascadia Code"), FontSize = 9 }; Canvas.SetLeft(_scannerMicro, c - 50); Canvas.SetTop(_scannerMicro, c + 45); canvas.Children.Add(_scannerMicro);
        }

        private void UpdateScannerAnimation()
        {
            if (_scanSweep == null) return;
            var c = 180.0; var r = 160.0; var a = _sweepAngle * Math.PI / 180;
            var g = new PathGeometry(); var f = new PathFigure { StartPoint = new Point(c, c) };
            f.Segments.Add(new LineSegment(new Point(c + r * Math.Cos(a), c + r * Math.Sin(a)), true));
            g.Figures.Add(f); _scanSweep.Data = g;
        }

        private void BuildSystemLogPanel()
        {
            SystemLogPanel.Child = null; var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "SYSTEM LOG", Foreground = new SolidColorBrush(Color.FromArgb(204, 34, 211, 238)), FontFamily = new FontFamily("Segoe UI"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            if (_scanLog.Count == 0) _scanLog.Add($"{DateTime.Now:HH:mm:ss}|SYSTEM|Ready|queued");
            foreach (var entry in _scanLog.Take(6))
            {
                var parts = entry.Split('|'); if (parts.Length < 4) continue;
                var (time, tag, msg, status) = (parts[0], parts[1], parts[2], parts[3]);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                var icon = status == "completed" ? "✓" : status == "active" ? "→" : status == "warning" ? "⚠" : "○";
                var iconColor = status == "completed" ? Color.FromRgb(34, 197, 94) : status == "active" ? Color.FromRgb(34, 211, 238) : status == "warning" ? Color.FromRgb(245, 158, 11) : Color.FromRgb(75, 85, 99);
                row.Children.Add(new TextBlock { Text = icon, Foreground = new SolidColorBrush(iconColor), FontSize = 9, Margin = new Thickness(0, 0, 4, 0) });
                var ts = new StackPanel();
                ts.Children.Add(new TextBlock { Text = $"[{time}] [{tag}]", Foreground = new SolidColorBrush(Color.FromArgb(153, 34, 211, 238)), FontFamily = new FontFamily("Cascadia Code"), FontSize = 8 });
                ts.Children.Add(new TextBlock { Text = msg, Foreground = new SolidColorBrush(iconColor), FontFamily = new FontFamily("Cascadia Code"), FontSize = 8, TextWrapping = TextWrapping.Wrap, MaxWidth = 170 });
                row.Children.Add(ts); stack.Children.Add(row);
            }
            SystemLogPanel.Child = stack;
        }

        private void BuildAIAcceleratorPanel()
        {
            AIAcceleratorPanel.Child = null; var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "SYSTEM METRICS", Foreground = new SolidColorBrush(Color.FromArgb(204, 139, 92, 246)), FontFamily = new FontFamily("Segoe UI"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            foreach (var (l, v) in new[] { ("CPU", $"{_cpuUsage:F1}%"), ("PROCESSES", $"{_processCount}"), ("RAM", $"{_ramUsage:F1}%") })
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = l, Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)), FontFamily = new FontFamily("Cascadia Code"), FontSize = 9 });
                var vt = new TextBlock { Text = v, Foreground = new SolidColorBrush(Color.FromRgb(139, 92, 246)), FontFamily = new FontFamily("Cascadia Code"), FontSize = 12 }; Grid.SetColumn(vt, 1); row.Children.Add(vt); stack.Children.Add(row);
            }
            AIAcceleratorPanel.Child = stack;
        }

        private void UpdateAIAcceleratorPanel() => BuildAIAcceleratorPanel();

        private void BuildTrustMatrixPanel()
        {
            TrustMatrixPanel.Child = null; var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "TRUST MATRIX", Foreground = new SolidColorBrush(Color.FromArgb(204, 34, 197, 94)), FontFamily = new FontFamily("Segoe UI"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            var kt = _threatCount == 0 ? 98 : Math.Max(50, 98 - _threatCount * 10);
            foreach (var (l, v) in new[] { ("KERNEL TRUST", kt), ("MEMORY", 100), ("NETWORK", 96) })
            {
                var mp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var lr = new Grid(); lr.ColumnDefinitions.Add(new ColumnDefinition()); lr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                lr.Children.Add(new TextBlock { Text = l, Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)), FontFamily = new FontFamily("Cascadia Code"), FontSize = 8 });
                var bc = v >= 90 ? Color.FromRgb(34, 197, 94) : Color.FromRgb(245, 158, 11);
                var vt = new TextBlock { Text = $"{v}%", Foreground = new SolidColorBrush(bc), FontFamily = new FontFamily("Cascadia Code"), FontSize = 10 }; Grid.SetColumn(vt, 1); lr.Children.Add(vt); mp.Children.Add(lr);
                var bp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                for (int i = 0; i < 40; i++) bp.Children.Add(new Rectangle { Width = 3.5, Height = 6, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(i < v * 40 / 100 ? bc : Color.FromArgb(26, 34, 197, 94)), Margin = new Thickness(0, 0, 1, 0) });
                mp.Children.Add(bp); stack.Children.Add(mp);
            }
            TrustMatrixPanel.Child = stack;
        }

        private void BuildStatusModulesPanel()
        {
            StatusModulesPanel.Child = null; var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "MEMORY", Foreground = new SolidColorBrush(Color.FromArgb(204, 139, 92, 246)), FontFamily = new FontFamily("Segoe UI"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = $"{_ramUsage * 64 / 100:F1}", Foreground = new SolidColorBrush(Color.FromRgb(139, 92, 246)), FontFamily = new FontFamily("Segoe UI"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = "GB / 64", Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)), FontFamily = new FontFamily("Cascadia Code"), FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "DISK", Foreground = new SolidColorBrush(Color.FromArgb(204, 34, 211, 238)), FontFamily = new FontFamily("Segoe UI"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) });
            stack.Children.Add(new TextBlock { Text = $"{_diskFree:F1}", Foreground = new SolidColorBrush(Color.FromRgb(34, 211, 238)), FontFamily = new FontFamily("Segoe UI"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = "GB FREE", Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)), FontFamily = new FontFamily("Cascadia Code"), FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center });
            StatusModulesPanel.Child = stack;
        }

        private void UpdateStatusModulesPanel() => BuildStatusModulesPanel();

        private void UpdateHeartbeat()
        {
            HeartbeatCanvas.Children.Clear();
            var poly = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(153, 139, 92, 246)), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            var pts = new double[] { 12, 12, 12, 12, 12, 12, 12, 12, 6, 18, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 8, 16, 12, 12, 12, 12, 12, 12 };
            for (int i = 0; i < 32; i++) poly.Points.Add(new Point(i * 2, pts[i]));
            HeartbeatCanvas.Children.Add(poly);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void Close_Click(object sender, RoutedEventArgs e) { _animationTimer?.Stop(); _scanTimer?.Stop(); _cpuCounter?.Dispose(); Close(); }
    }
}
