using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.Commands
{
    /// <summary>
    /// Structured response from command execution.
    /// </summary>
    public class CommandResult
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "success";

        [JsonPropertyName("action")]
        public string Action { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("data")]
        public Dictionary<string, object>? Data { get; set; }

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");

        [JsonPropertyName("duration_ms")]
        public long DurationMs { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }

        public static CommandResult Success(string action, string message, Dictionary<string, object>? data = null)
        {
            return new CommandResult
            {
                Status = "success",
                Action = action,
                Message = message,
                Data = data
            };
        }

        public static CommandResult Error(string action, string error)
        {
            return new CommandResult
            {
                Status = "error",
                Action = action,
                Message = "Command execution failed",
                ErrorMessage = error
            };
        }

        public static CommandResult InProgress(string action, string message)
        {
            return new CommandResult
            {
                Status = "in_progress",
                Action = action,
                Message = message
            };
        }
    }
}
