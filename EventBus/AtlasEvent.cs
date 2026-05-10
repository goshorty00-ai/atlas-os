using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.EventBus
{
    /// <summary>
    /// Base class for all Atlas events. All events are JSON-serializable.
    /// </summary>
    public abstract class AtlasEvent
    {
        [JsonPropertyName("event")]
        public string EventType { get; set; } = "";

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");

        [JsonPropertyName("source")]
        public string Source { get; set; } = "atlas";

        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Serialize this event to JSON string.
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, GetType(), new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }

        /// <summary>
        /// Deserialize JSON to event object.
        /// </summary>
        public static T? FromJson<T>(string json) where T : AtlasEvent
        {
            return JsonSerializer.Deserialize<T>(json);
        }
    }

    // ── System Events ────────────────────────────────────────────────────────

    public class ProcessStartedEvent : AtlasEvent
    {
        [JsonPropertyName("process")]
        public string Process { get; set; } = "";

        [JsonPropertyName("pid")]
        public int Pid { get; set; }

        [JsonPropertyName("cpu")]
        public float Cpu { get; set; }

        [JsonPropertyName("memory")]
        public long Memory { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        public ProcessStartedEvent() { EventType = "process_started"; }
    }

    public class ProcessTerminatedEvent : AtlasEvent
    {
        [JsonPropertyName("process")]
        public string Process { get; set; } = "";

        [JsonPropertyName("pid")]
        public int Pid { get; set; }

        [JsonPropertyName("exitCode")]
        public int? ExitCode { get; set; }

        public ProcessTerminatedEvent() { EventType = "process_terminated"; }
    }

    public class DownloadDetectedEvent : AtlasEvent
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = "";

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("fileType")]
        public string FileType { get; set; } = "";

        [JsonPropertyName("isExecutable")]
        public bool IsExecutable { get; set; }

        public DownloadDetectedEvent() { EventType = "download_detected"; }
    }

    public class FileCreatedEvent : AtlasEvent
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = "";

        [JsonPropertyName("directory")]
        public string Directory { get; set; } = "";

        public FileCreatedEvent() { EventType = "file_created"; }
    }

    public class NetworkConnectionOpenedEvent : AtlasEvent
    {
        [JsonPropertyName("localAddress")]
        public string LocalAddress { get; set; } = "";

        [JsonPropertyName("localPort")]
        public int LocalPort { get; set; }

        [JsonPropertyName("remoteAddress")]
        public string RemoteAddress { get; set; } = "";

        [JsonPropertyName("remotePort")]
        public int RemotePort { get; set; }

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = "TCP";

        [JsonPropertyName("state")]
        public string State { get; set; } = "";

        public NetworkConnectionOpenedEvent() { EventType = "network_connection_opened"; }
    }

    public class CpuSpikeEvent : AtlasEvent
    {
        [JsonPropertyName("cpuPercent")]
        public float CpuPercent { get; set; }

        [JsonPropertyName("threshold")]
        public float Threshold { get; set; }

        [JsonPropertyName("duration")]
        public int DurationSeconds { get; set; }

        public CpuSpikeEvent() { EventType = "cpu_spike"; }
    }

    public class MemoryPressureEvent : AtlasEvent
    {
        [JsonPropertyName("memoryPercent")]
        public float MemoryPercent { get; set; }

        [JsonPropertyName("memoryUsedMb")]
        public long MemoryUsedMb { get; set; }

        [JsonPropertyName("memoryTotalMb")]
        public long MemoryTotalMb { get; set; }

        [JsonPropertyName("threshold")]
        public float Threshold { get; set; }

        public MemoryPressureEvent() { EventType = "memory_pressure"; }
    }

    public class SoftwareInstalledEvent : AtlasEvent
    {
        [JsonPropertyName("softwareName")]
        public string SoftwareName { get; set; } = "";

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("publisher")]
        public string? Publisher { get; set; }

        [JsonPropertyName("installPath")]
        public string? InstallPath { get; set; }

        public SoftwareInstalledEvent() { EventType = "software_installed"; }
    }

    // ── Security Events ──────────────────────────────────────────────────────

    public class ThreatDetectedEvent : AtlasEvent
    {
        [JsonPropertyName("threatType")]
        public string ThreatType { get; set; } = "";

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "medium";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("affectedResource")]
        public string? AffectedResource { get; set; }

        public ThreatDetectedEvent() { EventType = "threat_detected"; }
    }

    public class SecurityScanCompletedEvent : AtlasEvent
    {
        [JsonPropertyName("scanType")]
        public string ScanType { get; set; } = "";

        [JsonPropertyName("itemsScanned")]
        public int ItemsScanned { get; set; }

        [JsonPropertyName("threatsFound")]
        public int ThreatsFound { get; set; }

        [JsonPropertyName("durationMs")]
        public long DurationMs { get; set; }

        public SecurityScanCompletedEvent() { EventType = "security_scan_completed"; }
    }

    // ── AI Events ────────────────────────────────────────────────────────────

    public class AiAnalysisCompletedEvent : AtlasEvent
    {
        [JsonPropertyName("analysisType")]
        public string AnalysisType { get; set; } = "";

        [JsonPropertyName("result")]
        public string Result { get; set; } = "";

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }

        [JsonPropertyName("recommendations")]
        public List<string>? Recommendations { get; set; }

        public AiAnalysisCompletedEvent() { EventType = "ai_analysis_completed"; }
    }

    public class AiActionRecommendedEvent : AtlasEvent
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = "normal";

        [JsonPropertyName("autoExecute")]
        public bool AutoExecute { get; set; }

        public AiActionRecommendedEvent() { EventType = "ai_action_recommended"; }
    }

    // ── Automation Events ────────────────────────────────────────────────────

    public class AutomationTriggeredEvent : AtlasEvent
    {
        [JsonPropertyName("automationName")]
        public string AutomationName { get; set; } = "";

        [JsonPropertyName("trigger")]
        public string Trigger { get; set; } = "";

        [JsonPropertyName("actions")]
        public List<string>? Actions { get; set; }

        public AutomationTriggeredEvent() { EventType = "automation_triggered"; }
    }

    public class AutomationCompletedEvent : AtlasEvent
    {
        [JsonPropertyName("automationName")]
        public string AutomationName { get; set; } = "";

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }

        public AutomationCompletedEvent() { EventType = "automation_completed"; }
    }

    // ── Generic Event ────────────────────────────────────────────────────────

    public class GenericEvent : AtlasEvent
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalData { get; set; }

        public GenericEvent() { }

        public GenericEvent(string eventType)
        {
            EventType = eventType;
        }
    }
}
