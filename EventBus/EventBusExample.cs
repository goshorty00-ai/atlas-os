using AtlasAI.EventBus.Modules;
using System;
using System.Diagnostics;

namespace AtlasAI.EventBus
{
    /// <summary>
    /// Example usage of the Atlas event bus and module system.
    /// </summary>
    public static class EventBusExample
    {
        public static void InitializeEventSystem()
        {
            var eventBus = AtlasEventBus.Instance;
            var moduleManager = AtlasModuleManager.Instance;

            // Configure event bus
            eventBus.EnableLogging = true;
            eventBus.EnableStatistics = true;

            // Register modules
            moduleManager.RegisterModule(new SecurityModule());
            moduleManager.RegisterModule(new AiAnalysisModule());
            moduleManager.RegisterModule(new AutomationModule());

            // Start everything
            moduleManager.StartAll();

            Debug.WriteLine("[EventBus] Event system initialized");
        }

        public static void SimulateEvents()
        {
            var eventBus = AtlasEventBus.Instance;

            // Simulate process started
            eventBus.Publish(new ProcessStartedEvent
            {
                Process = "chrome.exe",
                Pid = 1234,
                Cpu = 12.5f,
                Memory = 450,
                Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe"
            });

            // Simulate download
            eventBus.Publish(new DownloadDetectedEvent
            {
                FileName = "setup.exe",
                FilePath = @"C:\Users\User\Downloads\setup.exe",
                FileSize = 5242880,
                FileType = "application/x-msdownload",
                IsExecutable = true
            });

            // Simulate network connection
            eventBus.Publish(new NetworkConnectionOpenedEvent
            {
                LocalAddress = "192.168.1.100",
                LocalPort = 54321,
                RemoteAddress = "93.184.216.34",
                RemotePort = 443,
                Protocol = "TCP",
                State = "ESTABLISHED"
            });

            // Simulate CPU spike
            eventBus.Publish(new CpuSpikeEvent
            {
                CpuPercent = 85.5f,
                Threshold = 80.0f,
                DurationSeconds = 15
            });
        }

        public static void SubscribeToAllEvents()
        {
            var eventBus = AtlasEventBus.Instance;

            // Subscribe to all events for logging
            eventBus.SubscribeAll(evt =>
            {
                Debug.WriteLine($"[Event] {evt.EventType} at {evt.Timestamp}");
                Debug.WriteLine($"  JSON: {evt.ToJson()}");
            });

            // Subscribe to specific threat events
            eventBus.Subscribe<ThreatDetectedEvent>(evt =>
            {
                Debug.WriteLine($"[THREAT] {evt.ThreatType} - {evt.Severity}");
                Debug.WriteLine($"  {evt.Description}");
            });

            // Subscribe to AI analysis with filter
            eventBus.Subscribe<AiAnalysisCompletedEvent>(
                evt =>
                {
                    Debug.WriteLine($"[AI] Analysis: {evt.AnalysisType}");
                    Debug.WriteLine($"  Result: {evt.Result}");
                    Debug.WriteLine($"  Confidence: {evt.Confidence:P0}");
                },
                filter: evt => evt.Confidence > 0.7f
            );
        }

        public static void PrintStatistics()
        {
            var eventBus = AtlasEventBus.Instance;
            var stats = eventBus.GetAllStatistics();

            Debug.WriteLine("\n=== Event Bus Statistics ===");
            foreach (var (eventType, stat) in stats)
            {
                Debug.WriteLine($"\n{eventType}:");
                Debug.WriteLine($"  Published: {stat.PublishedCount}");
                Debug.WriteLine($"  Handled: {stat.HandledCount}");
                Debug.WriteLine($"  Errors: {stat.ErrorCount}");
                Debug.WriteLine($"  Avg Queue Time: {stat.AvgQueueTimeMs:F2}ms");
                Debug.WriteLine($"  Last Event: {stat.LastEventTime}");
            }

            Debug.WriteLine($"\nQueue Size: {eventBus.GetQueueSize()}");
        }

        public static void Shutdown()
        {
            var moduleManager = AtlasModuleManager.Instance;
            moduleManager.StopAll();
            moduleManager.Dispose();

            var eventBus = AtlasEventBus.Instance;
            eventBus.Stop();
            eventBus.Dispose();

            Debug.WriteLine("[EventBus] Event system shutdown complete");
        }
    }
}
