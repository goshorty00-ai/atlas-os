using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.EventBus;

namespace AtlasAI.Security
{
    /// <summary>
    /// Lightweight background telemetry engine.
    /// Collects real Windows system data and raises events consumed by SecurityControl → WebView2.
    /// Target: CPU idle < 2%, RAM < 50MB overhead.
    /// </summary>
    public sealed class SecurityTelemetryService : IDisposable
    {
        // ── Singleton ────────────────────────────────────────────────────────────
        private static SecurityTelemetryService? _instance;
        private static readonly object _lock = new();
        public static SecurityTelemetryService Instance
        {
            get
            {
                lock (_lock) { return _instance ??= new SecurityTelemetryService(); }
            }
        }

        // ── Events ───────────────────────────────────────────────────────────────
        public event Action<string>? TelemetryMessage;   // JSON string → PostWebMessageAsJson
        public event Action<ActivityEvent>? ActivityDetected;

        // ── State ────────────────────────────────────────────────────────────────
        private PerformanceCounter? _cpuCounter;
        private CancellationTokenSource? _cts;
        private bool _running;
        private bool _disposed;

        // Process snapshot for delta detection
        private readonly Dictionary<int, ProcessSnapshot> _lastProcesses = new();
        private readonly object _processLock = new();

        // File watchers
        private readonly List<FileSystemWatcher> _watchers = new();

        // Metrics history for sparklines (last 20 samples)
        private readonly Queue<float> _cpuHistory = new(20);
        private readonly Queue<float> _ramHistory = new(20);

        // Counters
        private int _filesScannedToday;
        private int _suspiciousFlagged;
        private int _networkConnections;

        private SecurityTelemetryService() { }

        // ── Public API ───────────────────────────────────────────────────────────

        public void Start()
        {
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // prime
            }
            catch { }

            StartFileWatchers();

            _ = Task.Run(() => TelemetryLoopAsync(_cts.Token));
            _ = Task.Run(() => ProcessMonitorLoopAsync(_cts.Token));
            _ = Task.Run(() => NetworkMonitorLoopAsync(_cts.Token));

            // Publish system started event
            try
            {
                AtlasEventBus.Instance.Publish(new GenericEvent("system_monitoring_started"));
            }
            catch { }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _running = false;
            foreach (var w in _watchers) try { w.Dispose(); } catch { }
            _watchers.Clear();
        }

        public SystemSnapshot GetSnapshot() => BuildSnapshot();

        // ── Telemetry loop (2s) ──────────────────────────────────────────────────

        private async Task TelemetryLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var snap = BuildSnapshot();
                    var json = JsonSerializer.Serialize(new
                    {
                        type = "telemetry",
                        cpu = snap.CpuPercent,
                        ram = snap.RamPercent,
                        ramUsedMb = snap.RamUsedMb,
                        ramTotalMb = snap.RamTotalMb,
                        netKbps = snap.NetKbps,
                        processCount = snap.ProcessCount,
                        filesScanned = snap.FilesScannedToday,
                        suspicious = snap.SuspiciousFlagged,
                        networkConnections = snap.NetworkConnections,
                        vulnerabilityScore = snap.VulnerabilityScore,
                        status = snap.OverallStatus,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                    TelemetryMessage?.Invoke(json);
                }
                catch { }

                await Task.Delay(2000, ct).ConfigureAwait(false);
            }
        }

        // ── Process monitor loop (3s) ────────────────────────────────────────────

        private async Task ProcessMonitorLoopAsync(CancellationToken ct)
        {
            // Initial snapshot
            await Task.Delay(1000, ct).ConfigureAwait(false);
            TakeProcessSnapshot();

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(3000, ct).ConfigureAwait(false);
                try { DetectNewProcesses(); } catch { }
            }
        }

        private void TakeProcessSnapshot()
        {
            lock (_processLock)
            {
                _lastProcesses.Clear();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        _lastProcesses[p.Id] = new ProcessSnapshot
                        {
                            Id = p.Id,
                            Name = p.ProcessName,
                            MemoryMb = p.WorkingSet64 / 1024 / 1024,
                            StartTime = TryGetStartTime(p)
                        };
                    }
                    catch { }
                }
            }
        }

        private void DetectNewProcesses()
        {
            var current = new Dictionary<int, ProcessSnapshot>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    current[p.Id] = new ProcessSnapshot
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        MemoryMb = p.WorkingSet64 / 1024 / 1024,
                        StartTime = TryGetStartTime(p)
                    };
                }
                catch { }
            }

            List<ProcessSnapshot> newProcs;
            lock (_processLock)
            {
                newProcs = current.Values
                    .Where(p => !_lastProcesses.ContainsKey(p.Id))
                    .ToList();

                _lastProcesses.Clear();
                foreach (var kv in current) _lastProcesses[kv.Key] = kv.Value;
            }

            foreach (var proc in newProcs)
            {
                var risk = ScoreProcess(proc);
                _filesScannedToday++;

                if (risk >= 60) _suspiciousFlagged++;

                // Publish to event bus
                try
                {
                    AtlasEventBus.Instance.Publish(new ProcessStartedEvent
                    {
                        Process = proc.Name,
                        Pid = proc.Id,
                        Cpu = 0,
                        Memory = proc.MemoryMb,
                        Path = null
                    });
                }
                catch { }

                var evt = new ActivityEvent
                {
                    Id = $"proc-{proc.Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    Type = "process",
                    Title = "Process Started",
                    Description = $"{proc.Name}.exe  •  {proc.MemoryMb} MB",
                    Risk = risk >= 60 ? "high" : risk >= 30 ? "medium" : "safe",
                    RiskScore = risk,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                ActivityDetected?.Invoke(evt);

                var json = JsonSerializer.Serialize(new
                {
                    type = "activity",
                    id = evt.Id,
                    eventType = evt.Type,
                    title = evt.Title,
                    description = evt.Description,
                    risk = evt.Risk,
                    riskScore = evt.RiskScore,
                    timestamp = evt.Timestamp
                });
                TelemetryMessage?.Invoke(json);
            }
        }

        // ── Network monitor loop (5s) ────────────────────────────────────────────

        private async Task NetworkMonitorLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct).ConfigureAwait(false);
                try { ScanNetworkConnections(); } catch { }
            }
        }

        private void ScanNetworkConnections()
        {
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                var tcpConns = props.GetActiveTcpConnections();
                _networkConnections = tcpConns.Length;

                // Look for suspicious ports
                var suspiciousPorts = new HashSet<int> { 4444, 1337, 31337, 6666, 9999, 12345 };
                foreach (var conn in tcpConns)
                {
                    if (suspiciousPorts.Contains(conn.RemoteEndPoint.Port))
                    {
                        _suspiciousFlagged++;
                        var evt = new ActivityEvent
                        {
                            Id = $"net-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                            Type = "network",
                            Title = "Suspicious Port Detected",
                            Description = $"Connection on port {conn.RemoteEndPoint.Port} → {conn.RemoteEndPoint.Address}",
                            Risk = "high",
                            RiskScore = 75,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                        ActivityDetected?.Invoke(evt);
                        var json = JsonSerializer.Serialize(new
                        {
                            type = "activity",
                            evt.Id, evt.Type, evt.Title, evt.Description,
                            evt.Risk, evt.RiskScore, evt.Timestamp
                        });
                        TelemetryMessage?.Invoke(json);
                    }
                }
            }
            catch { }
        }

        // ── File watchers ────────────────────────────────────────────────────────

        private void StartFileWatchers()
        {
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            };

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    var w = new FileSystemWatcher(folder)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        EnableRaisingEvents = true,
                        IncludeSubdirectories = false
                    };
                    w.Created += OnFileCreated;
                    _watchers.Add(w);
                }
                catch { }
            }
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                _filesScannedToday++;
                var ext = Path.GetExtension(e.Name ?? "").ToLowerInvariant();
                var executableExts = new HashSet<string> { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".scr" };
                var isExec = executableExts.Contains(ext);
                var risk = isExec ? "medium" : "safe";
                var riskScore = isExec ? 40 : 5;

                if (isExec) _suspiciousFlagged++;

                // Publish to event bus
                try
                {
                    var fileInfo = new FileInfo(e.FullPath);
                    var isDownload = e.FullPath.Contains("Downloads", StringComparison.OrdinalIgnoreCase);

                    if (isDownload)
                    {
                        AtlasEventBus.Instance.Publish(new DownloadDetectedEvent
                        {
                            FileName = e.Name ?? "",
                            FilePath = e.FullPath,
                            FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                            FileType = ext,
                            IsExecutable = isExec
                        });
                    }
                    else
                    {
                        AtlasEventBus.Instance.Publish(new FileCreatedEvent
                        {
                            FileName = e.Name ?? "",
                            FilePath = e.FullPath,
                            Directory = Path.GetDirectoryName(e.FullPath) ?? ""
                        });
                    }
                }
                catch { }

                var folder = Path.GetFileName(Path.GetDirectoryName(e.FullPath) ?? "");
                var evt = new ActivityEvent
                {
                    Id = $"file-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    Type = "download",
                    Title = isExec ? "Executable File Detected" : "File Download Detected",
                    Description = $"{e.Name}  •  {folder}",
                    Risk = risk,
                    RiskScore = riskScore,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                ActivityDetected?.Invoke(evt);
                var json = JsonSerializer.Serialize(new
                {
                    type = "activity",
                    id = evt.Id,
                    eventType = evt.Type,
                    title = evt.Title,
                    description = evt.Description,
                    risk = evt.Risk,
                    riskScore = evt.RiskScore,
                    timestamp = evt.Timestamp
                });
                TelemetryMessage?.Invoke(json);
            }
            catch { }
        }

        // ── Snapshot builder ─────────────────────────────────────────────────────

        private SystemSnapshot BuildSnapshot()
        {
            float cpu = 0;
            try { cpu = _cpuCounter?.NextValue() ?? 0; } catch { }

            float ramPercent = 0;
            long ramUsedMb = 0, ramTotalMb = 0;
            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                ramTotalMb = gcInfo.TotalAvailableMemoryBytes / 1024 / 1024;
                using var avail = new PerformanceCounter("Memory", "Available MBytes");
                var availMb = (long)avail.NextValue();
                ramUsedMb = ramTotalMb - availMb;
                ramPercent = ramTotalMb > 0 ? (float)ramUsedMb / ramTotalMb * 100f : 0;
            }
            catch { }

            // Keep history
            if (_cpuHistory.Count >= 20) _cpuHistory.Dequeue();
            _cpuHistory.Enqueue(cpu);
            if (_ramHistory.Count >= 20) _ramHistory.Dequeue();
            _ramHistory.Enqueue(ramPercent);

            // Publish CPU spike event
            if (cpu > 80 && _cpuHistory.Count >= 3 && _cpuHistory.TakeLast(3).All(c => c > 80))
            {
                try
                {
                    AtlasEventBus.Instance.Publish(new CpuSpikeEvent
                    {
                        CpuPercent = cpu,
                        Threshold = 80,
                        DurationSeconds = 6
                    });
                }
                catch { }
            }

            // Publish memory pressure event
            if (ramPercent > 85)
            {
                try
                {
                    AtlasEventBus.Instance.Publish(new MemoryPressureEvent
                    {
                        MemoryPercent = ramPercent,
                        MemoryUsedMb = ramUsedMb,
                        MemoryTotalMb = ramTotalMb,
                        Threshold = 85
                    });
                }
                catch { }
            }

            int procCount;
            lock (_processLock) { procCount = _lastProcesses.Count > 0 ? _lastProcesses.Count : Process.GetProcesses().Length; }

            // Vulnerability score: starts at 100, deducted by suspicious count
            var vulnScore = Math.Max(0, 100 - (_suspiciousFlagged * 3));

            var status = _suspiciousFlagged > 5 ? "threat" : _suspiciousFlagged > 1 ? "warning" : "secure";

            return new SystemSnapshot
            {
                CpuPercent = MathF.Round(cpu, 1),
                RamPercent = MathF.Round(ramPercent, 1),
                RamUsedMb = ramUsedMb,
                RamTotalMb = ramTotalMb,
                NetKbps = 0,
                ProcessCount = procCount,
                FilesScannedToday = _filesScannedToday,
                SuspiciousFlagged = _suspiciousFlagged,
                NetworkConnections = _networkConnections,
                VulnerabilityScore = vulnScore,
                OverallStatus = status
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static int ScoreProcess(ProcessSnapshot proc)
        {
            var name = proc.Name.ToLowerInvariant();

            // Known safe
            var safe = new HashSet<string> { "chrome", "firefox", "msedge", "explorer", "svchost",
                "winlogon", "csrss", "lsass", "services", "system", "idle", "dwm",
                "taskhostw", "sihost", "ctfmon", "searchhost", "runtimebroker" };
            if (safe.Contains(name)) return 5;

            // High memory with no known name = suspicious
            if (proc.MemoryMb > 500 && !safe.Contains(name)) return 45;

            // Random-looking names (short, no vowels)
            if (name.Length <= 4 && !name.Any(c => "aeiou".Contains(c))) return 55;

            return 10;
        }

        private static DateTime? TryGetStartTime(Process p)
        {
            try { return p.StartTime; } catch { return null; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cpuCounter?.Dispose();
        }
    }

    // ── Data models ──────────────────────────────────────────────────────────────

    public class SystemSnapshot
    {
        public float CpuPercent { get; set; }
        public float RamPercent { get; set; }
        public long RamUsedMb { get; set; }
        public long RamTotalMb { get; set; }
        public float NetKbps { get; set; }
        public int ProcessCount { get; set; }
        public int FilesScannedToday { get; set; }
        public int SuspiciousFlagged { get; set; }
        public int NetworkConnections { get; set; }
        public int VulnerabilityScore { get; set; }
        public string OverallStatus { get; set; } = "secure";
    }

    public class ActivityEvent
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Risk { get; set; } = "safe";
        public int RiskScore { get; set; }
        public long Timestamp { get; set; }
    }

    public class ProcessSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public long MemoryMb { get; set; }
        public DateTime? StartTime { get; set; }
    }
}
