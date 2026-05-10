using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.AI
{
    /// <summary>
    /// Centralized LLM quality settings to prevent "dumb mode" responses.
    /// Tuned for consistent Jarvis tone, low randomness, high relevance.
    /// </summary>
    public class LLMProviderQualitySettings
    {
        private static LLMProviderQualitySettings? _instance;
        private static readonly object _lock = new();
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "llm_quality.json");

        public static LLMProviderQualitySettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= Load();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Temperature for response generation (0..2). Lower = more deterministic.
        /// Default 0.7 for balanced creativity with consistency.
        /// </summary>
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Top-p (nucleus sampling). Lower = more focused responses.
        /// Default 0.9 for good balance.
        /// </summary>
        [JsonPropertyName("topP")]
        public double TopP { get; set; } = 0.9;

        /// <summary>
        /// Default max tokens for responses.
        /// </summary>
        [JsonPropertyName("maxTokens")]
        public int MaxTokens { get; set; } = 500;

        /// <summary>
        /// Max tokens for detailed/long responses.
        /// </summary>
        [JsonPropertyName("maxTokensDetailed")]
        public int MaxTokensDetailed { get; set; } = 2000;

        /// <summary>
        /// Presence penalty (-2..2). Positive = less repetition of topics.
        /// </summary>
        [JsonPropertyName("presencePenalty")]
        public double PresencePenalty { get; set; } = 0.3;

        /// <summary>
        /// Frequency penalty (-2..2). Positive = less word repetition.
        /// </summary>
        [JsonPropertyName("frequencyPenalty")]
        public double FrequencyPenalty { get; set; } = 0.2;

        /// <summary>
        /// Stop sequences to prevent certain patterns.
        /// </summary>
        [JsonPropertyName("stopSequences")]
        public string[] StopSequences { get; set; } = new[]
        {
            "User:", "Human:", "Assistant:", "[END]"
        };

        /// <summary>
        /// Whether to retry on low-quality responses.
        /// </summary>
        [JsonPropertyName("retryOnLowQuality")]
        public bool RetryOnLowQuality { get; set; } = true;

        /// <summary>
        /// Maximum retries for quality gate failures.
        /// </summary>
        [JsonPropertyName("maxRetries")]
        public int MaxRetries { get; set; } = 1;

        /// <summary>
        /// Minimum specificity score to accept response.
        /// </summary>
        [JsonPropertyName("minSpecificityScore")]
        public int MinSpecificityScore { get; set; } = 2;

        /// <summary>
        /// Load settings from disk or return defaults.
        /// </summary>
        public static LLMProviderQualitySettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var settings = JsonSerializer.Deserialize<LLMProviderQualitySettings>(json);
                    if (settings != null)
                    {
                        Debug.WriteLine($"[LLMQualitySettings] Loaded from {ConfigPath}");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LLMQualitySettings] Error loading: {ex.Message}");
            }

            // Return defaults and save them
            var defaults = new LLMProviderQualitySettings();
            defaults.Save();
            return defaults;
        }

        /// <summary>
        /// Save current settings to disk.
        /// </summary>
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigPath, json);
                Debug.WriteLine($"[LLMQualitySettings] Saved to {ConfigPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LLMQualitySettings] Error saving: {ex.Message}");
            }
        }

        /// <summary>
        /// Reload settings from disk.
        /// </summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _instance = Load();
            }
        }

        /// <summary>
        /// Validate and clamp settings to safe ranges.
        /// </summary>
        public void Validate()
        {
            Temperature = Math.Clamp(Temperature, 0, 2);
            TopP = Math.Clamp(TopP, 0, 1);
            MaxTokens = Math.Clamp(MaxTokens, 50, 4096);
            MaxTokensDetailed = Math.Clamp(MaxTokensDetailed, 100, 8192);
            PresencePenalty = Math.Clamp(PresencePenalty, -2, 2);
            FrequencyPenalty = Math.Clamp(FrequencyPenalty, -2, 2);
            MaxRetries = Math.Clamp(MaxRetries, 0, 3);
            MinSpecificityScore = Math.Clamp(MinSpecificityScore, 0, 5);
        }
    }
}
