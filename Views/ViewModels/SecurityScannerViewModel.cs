using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Windows.Media;
using AtlasAI.Services;

namespace AtlasAI.Views.ViewModels
{
    public sealed class SecurityScannerViewModel : INotifyPropertyChanged
    {
        private readonly Random _rng = new Random();
        private DispatcherTimer? _metricsTimer;
        private DispatcherTimer? _scanTimer;
        private HardwareMetricsService? _hardware;
        private int _phaseIndex;

        private int _scannedItems;
        private int _threatsDetected;
        private double _scanProgress;
        private string _scanPhaseText = "INITIALIZING THREAT SCAN";

        public event PropertyChangedEventHandler? PropertyChanged;

        public SecurityGaugeViewModel Cpu { get; } = new SecurityGaugeViewModel { Label = "CPU USAGE" };
        public SecurityGaugeViewModel Gpu { get; } = new SecurityGaugeViewModel { Label = "GPU USAGE" };
        public SecurityGaugeViewModel Memory { get; } = new SecurityGaugeViewModel { Label = "MEMORY" };
        public SecurityGaugeViewModel Disk { get; } = new SecurityGaugeViewModel { Label = "DISK" };

        public SecurityChatViewModel Chat { get; } = new SecurityChatViewModel();

        public int ScannedItems
        {
            get => _scannedItems;
            private set => SetProperty(ref _scannedItems, value);
        }

        public int ThreatsDetected
        {
            get => _threatsDetected;
            private set => SetProperty(ref _threatsDetected, value);
        }

        public double ScanProgress
        {
            get => _scanProgress;
            private set
            {
                if (SetProperty(ref _scanProgress, value))
                    OnPropertyChanged(nameof(ScanProgressText));
            }
        }

        public string ScanProgressText => $"{ScanProgress:0}%";

        public string ScanPhaseText
        {
            get => _scanPhaseText;
            private set => SetProperty(ref _scanPhaseText, value);
        }

        public Brush ThreatsBrush => ThreatsDetected == 0
            ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

        public void Start()
        {
            if (_metricsTimer != null) return;

            _hardware ??= new HardwareMetricsService();

            Cpu.Value = 23;
            Gpu.Value = 18;
            Memory.Value = 45;
            Disk.Value = 67;

            Cpu.IconGeometry = Geometry.Parse("M9 2h6v2h2v2h2v6h-2v2h-2v2H9v-2H7v-2H5V6h2V4h2V2zm-2 6v6h10V8H7z");
            Gpu.IconGeometry = Geometry.Parse("M13 2L3 14h9l-1 8 10-12h-9l1-8z");
            Memory.IconGeometry = Geometry.Parse("M22 12h-4l-3 9L9 3l-3 9H2");
            Disk.IconGeometry = Geometry.Parse("M4 4h16v14H4V4zm2 2v6h12V6H6zm2 10a1 1 0 1 0 0.01 0z");

            ApplyThresholds();

            _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _metricsTimer.Tick += (_, __) => UpdateMetrics();
            _metricsTimer.Start();

            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _scanTimer.Tick += (_, __) => UpdateScan();
            _scanTimer.Start();

            Chat.ContextProvider = () =>
            {
                var phase = (ScanPhaseText ?? "").Trim();
                var prog = ScanProgressText;
                return $"SCAN: {phase} · {prog}\nCPU: {Cpu.Value:0}% · GPU: {Gpu.Value:0}% · RAM: {Memory.Value:0}% · DISK: {Disk.Value:0}%\nTHREATS: {ThreatsDetected:N0} · SCANNED: {ScannedItems:N0}";
            };
        }

        public void Stop()
        {
            try { _metricsTimer?.Stop(); } catch { }
            try { _scanTimer?.Stop(); } catch { }
            _metricsTimer = null;
            _scanTimer = null;
            try { _hardware?.Dispose(); } catch { }
            _hardware = null;
        }

        private void UpdateMetrics()
        {
            try
            {
                var snap = _hardware?.GetSnapshot();
                if (snap.HasValue)
                {
                    Cpu.Value = Clamp(snap.Value.Cpu, 0, 100);
                    Gpu.Value = Clamp(snap.Value.Gpu, 0, 100);
                    Memory.Value = Clamp(snap.Value.Ram, 0, 100);
                    Disk.Value = Clamp(snap.Value.Disk, 0, 100);
                }
                else
                {
                    Cpu.Value = Clamp(Cpu.Value + NextDelta(10), 10, 90);
                    Gpu.Value = Clamp(Gpu.Value + NextDelta(8), 10, 90);
                    Memory.Value = Clamp(Memory.Value + NextDelta(5), 20, 85);
                    Disk.Value = Clamp(Disk.Value + NextDelta(3), 50, 80);
                }
            }
            catch
            {
                Cpu.Value = Clamp(Cpu.Value + NextDelta(10), 10, 90);
                Gpu.Value = Clamp(Gpu.Value + NextDelta(8), 10, 90);
                Memory.Value = Clamp(Memory.Value + NextDelta(5), 20, 85);
                Disk.Value = Clamp(Disk.Value + NextDelta(3), 50, 80);
            }
            ApplyThresholds();
        }

        private void ApplyThresholds()
        {
            Cpu.ApplyThresholds(SecurityGaugeViewModel.GaugeType.Cpu);
            Gpu.ApplyThresholds(SecurityGaugeViewModel.GaugeType.Gpu);
            Memory.ApplyThresholds(SecurityGaugeViewModel.GaugeType.Memory);
            Disk.ApplyThresholds(SecurityGaugeViewModel.GaugeType.Disk);
        }

        private void UpdateScan()
        {
            var next = ScanProgress + 1;
            if (next >= 100)
            {
                ScanProgress = 0;
                ScannedItems += _rng.Next(1, 51);
                _phaseIndex = (_phaseIndex + 1) % 5;
                ScanPhaseText = _phases[_phaseIndex];
                return;
            }

            ScanProgress = next;
        }

        private double NextDelta(double maxAbs)
        {
            var span = maxAbs * 2;
            return (_rng.NextDouble() * span) - maxAbs;
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        private static readonly string[] _phases = new[]
        {
            "SCANNING FILE SYSTEM",
            "ANALYZING NETWORK TRAFFIC",
            "CHECKING REGISTRY KEYS",
            "MONITORING PROCESSES",
            "VALIDATING SIGNATURES"
        };

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            if (name == nameof(ThreatsDetected))
                OnPropertyChanged(nameof(ThreatsBrush));
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
