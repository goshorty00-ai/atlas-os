using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Centralized tone tuning parameters loaded from tone.json.
    /// Controls response style without code changes.
    /// </summary>
    public class ToneTuningProfile
    {
        private static ToneTuningProfile? _instance;
        private static readonly object _lock = new();
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "tone.json");

        public static ToneTuningProfile Instance
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
        /// 0..1: Higher = more concise, technical phrasing; reduces warmth.
        /// </summary>
        [JsonPropertyName("roboticLevel")]
        public double RoboticLevel { get; set; } = 0.6;

        /// <summary>
        /// 0..1: Higher = more formal ("sir", "how may I assist").
        /// </summary>
        [JsonPropertyName("formalityLevel")]
        public double FormalityLevel { get; set; } = 0.7;

        /// <summary>
        /// 0..1: Higher = friendlier at Familiar depth.
        /// </summary>
        [JsonPropertyName("warmthLevel")]
        public double WarmthLevel { get; set; } = 0.4;

        /// <summary>
        /// Maximum word count for responses (unless user asks for detail).
        /// </summary>
        [JsonPropertyName("maxWordCount")]
        public int MaxWordCount { get; set; } = 90;

        /// <summary>
        /// Rate at which closers ("Anything else?") are added (0..1).
        /// </summary>
        [JsonPropertyName("closerRate")]
        public double CloserRate { get; set; } = 0.2;

        /// <summary>
        /// Whether to prefer bullet points for multi-step responses.
        /// </summary>
        [JsonPropertyName("bulletPreference")]
        public bool BulletPreference { get; set; } = true;

        /// <summary>
        /// Whether to use "sir" honorific.
        /// </summary>
        [JsonPropertyName("useSirHonorific")]
        public bool UseSirHonorific { get; set; } = true;

        /// <summary>
        /// Maximum bullets in a response.
        /// </summary>
        [JsonPropertyName("maxBullets")]
        public int MaxBullets { get; set; } = 4;

        /// <summary>
        /// Load profile from disk or return defaults.
        /// </summary>
        public static ToneTuningProfile Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var profile = JsonSerializer.Deserialize<ToneTuningProfile>(json);
                    if (profile != null)
                    {
                        Debug.WriteLine($"[ToneTuningProfile] Loaded from {ConfigPath}");
                        return profile;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ToneTuningProfile] Error loading: {ex.Message}");
            }

            // Return defaults and save them
            var defaults = new ToneTuningProfile();
            defaults.Save();
            return defaults;
        }

        /// <summary>
        /// Save current profile to disk.
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
                Debug.WriteLine($"[ToneTuningProfile] Saved to {ConfigPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ToneTuningProfile] Error saving: {ex.Message}");
            }
        }

        /// <summary>
        /// Reload profile from disk.
        /// </summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _instance = Load();
            }
        }

        /// <summary>
        /// Get tone guidance for system prompt based on current settings.
        /// </summary>
        public string GetToneGuidance()
        {
            var guidance = new System.Text.StringBuilder();
            
            guidance.AppendLine("=== TONE SETTINGS ===");
            
            if (RoboticLevel > 0.7)
                guidance.AppendLine("Be highly concise and technical. Minimal pleasantries.");
            else if (RoboticLevel > 0.4)
                guidance.AppendLine("Be measured and precise. Brief pleasantries acceptable.");
            else
                guidance.AppendLine("Allow natural conversational flow while staying professional.");

            if (FormalityLevel > 0.7)
                guidance.AppendLine("Use formal address ('sir'). Maintain professional distance.");
            else if (FormalityLevel > 0.4)
                guidance.AppendLine("Moderate formality. Use 'sir' occasionally.");
            else
                guidance.AppendLine("Relaxed formality. Skip honorifics unless appropriate.");

            if (WarmthLevel > 0.6)
                guidance.AppendLine("Show warmth and friendliness, especially at Familiar depth.");
            else if (WarmthLevel > 0.3)
                guidance.AppendLine("Balanced warmth. Professional but approachable.");
            else
                guidance.AppendLine("Maintain professional reserve. Warmth only when earned.");

            guidance.AppendLine($"Keep responses under {MaxWordCount} words unless asked for detail.");
            
            if (BulletPreference)
                guidance.AppendLine("Use bullet points for multi-step instructions (max 4).");

            return guidance.ToString();
        }

        /// <summary>
        /// Check if closer should be added based on rate.
        /// </summary>
        public bool ShouldAddCloser()
        {
            return new Random().NextDouble() < CloserRate;
        }
    }
}
