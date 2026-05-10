using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.EventBus.Modules
{
    /// <summary>
    /// Security module that monitors downloads, processes, and network activity.
    /// Scans files and publishes threat detection events.
    /// </summary>
    public class SecurityModule : AtlasModuleBase
    {
        public override string ModuleId => "security";
        public override string ModuleName => "Security Module";

        private static readonly string[] SuspiciousExtensions = { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".dll" };
        private static readonly string[] SuspiciousProcesses = { "powershell", "cmd", "wscript", "cscript" };

        protected override void RegisterEventHandlers()
        {
            // Monitor downloads
            Subscribe<DownloadDetectedEvent>(OnDownloadDetected);

            // Monitor process starts
            Subscribe<ProcessStartedEvent>(OnProcessStarted);

            // Monitor network connections
            Subscribe<NetworkConnectionOpenedEvent>(OnNetworkConnection);

            // Monitor file creation
            Subscribe<FileCreatedEvent>(OnFileCreated);
        }

        private void OnDownloadDetected(DownloadDetectedEvent evt)
        {
            Log($"Download detected: {evt.FileName} ({evt.FileSize} bytes)");

            // Scan the file
            Task.Run(() => ScanFile(evt));
        }

        private void OnProcessStarted(ProcessStartedEvent evt)
        {
            Log($"Process started: {evt.Process} (PID: {evt.Pid})");

            // Check if process is suspicious
            var isSuspicious = SuspiciousProcesses.Any(p => 
                evt.Process.ToLowerInvariant().Contains(p));

            if (isSuspicious)
            {
                Publish(new ThreatDetectedEvent
                {
                    ThreatType = "suspicious_process",
                    Severity = "medium",
                    Description = $"Suspicious process detected: {evt.Process}",
                    AffectedResource = evt.Process
                });
            }
        }

        private void OnNetworkConnection(NetworkConnectionOpenedEvent evt)
        {
            Log($"Network connection: {evt.RemoteAddress}:{evt.RemotePort}");

            // Check for suspicious ports
            var suspiciousPorts = new[] { 4444, 1337, 31337, 6666, 9999, 12345 };
            if (suspiciousPorts.Contains(evt.RemotePort))
            {
                Publish(new ThreatDetectedEvent
                {
                    ThreatType = "suspicious_port",
                    Severity = "high",
                    Description = $"Connection to suspicious port {evt.RemotePort}",
                    AffectedResource = $"{evt.RemoteAddress}:{evt.RemotePort}"
                });
            }
        }

        private void OnFileCreated(FileCreatedEvent evt)
        {
            Log($"File created: {evt.FileName}");

            var ext = Path.GetExtension(evt.FileName).ToLowerInvariant();
            if (SuspiciousExtensions.Contains(ext))
            {
                // Trigger scan
                Task.Run(() => ScanFile(evt.FilePath, evt.FileName));
            }
        }

        private void ScanFile(DownloadDetectedEvent evt)
        {
            ScanFile(evt.FilePath, evt.FileName);
        }

        private void ScanFile(string filePath, string fileName)
        {
            try
            {
                Log($"Scanning file: {fileName}");

                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                var isExecutable = SuspiciousExtensions.Contains(ext);

                // Simulate scan delay
                System.Threading.Thread.Sleep(100);

                // Simple heuristic: flag executables
                if (isExecutable)
                {
                    Publish(new ThreatDetectedEvent
                    {
                        ThreatType = "executable_file",
                        Severity = "medium",
                        Description = $"Executable file detected: {fileName}",
                        AffectedResource = filePath
                    });

                    // Trigger AI analysis
                    Publish(new AiActionRecommendedEvent
                    {
                        Action = "analyze_file",
                        Reason = $"Executable file requires analysis: {fileName}",
                        Priority = "high",
                        AutoExecute = true
                    });
                }

                Publish(new SecurityScanCompletedEvent
                {
                    ScanType = "file_scan",
                    ItemsScanned = 1,
                    ThreatsFound = isExecutable ? 1 : 0,
                    DurationMs = 100
                });
            }
            catch (Exception ex)
            {
                Log($"Error scanning file: {ex.Message}");
            }
        }
    }
}
