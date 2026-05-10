using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Voice provider types supported by Atlas AI
    /// </summary>
    public enum VoiceProviderType
    {
        WindowsSAPI,  // Local/Instant - Built-in Windows voices (NO DELAY!)
        EdgeTTS,      // Local/Free - Microsoft Edge neural voices
        OpenAI,       // Cloud/Premium - OpenAI TTS
        ElevenLabs    // Cloud/Premium - ElevenLabs expressive voices
    }

    /// <summary>
    /// Represents a voice option from a provider
    /// </summary>
    public class VoiceInfo
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Gender { get; set; } = "";
        public string Locale { get; set; } = "";
        public bool IsCloud { get; set; }
        public VoiceProviderType Provider { get; set; }
        public string Category { get; set; } = "";  // e.g., "My Voices", "Default"
    }

    /// <summary>
    /// Result of a speech synthesis operation
    /// </summary>
    public class SynthesisResult
    {
        public bool Success { get; set; }
        public string? AudioFilePath { get; set; }
        public byte[]? AudioData { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Speech synthesis options
    /// </summary>
    public class SynthesisOptions
    {
        public string VoiceId { get; set; } = "";
        public double Rate { get; set; } = 1.0;      // 0.5 to 2.0
        public double Pitch { get; set; } = 1.0;     // 0.5 to 2.0
        public double Volume { get; set; } = 1.0;    // 0.0 to 1.0
        public string OutputFormat { get; set; } = "mp3";
    }

    /// <summary>
    /// Core interface for all voice providers.
    /// Implementations must be thread-safe and support cancellation.
    /// </summary>
    public interface IVoiceProvider
    {
        /// <summary>Provider type identifier</summary>
        VoiceProviderType ProviderType { get; }
        
        /// <summary>Human-readable provider name</summary>
        string DisplayName { get; }
        
        /// <summary>Whether this provider requires internet</summary>
        bool RequiresInternet { get; }
        
        /// <summary>Whether this provider requires an API key</summary>
        bool RequiresApiKey { get; }
        
        /// <summary>Check if provider is available and configured</summary>
        Task<bool> IsAvailableAsync(CancellationToken ct = default);
        
        /// <summary>Get all available voices from this provider</summary>
        Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken ct = default);
        
        /// <summary>Synthesize text to speech</summary>
        Task<SynthesisResult> SynthesizeAsync(string text, SynthesisOptions options, CancellationToken ct = default);
        
        /// <summary>Cancel any ongoing synthesis</summary>
        void CancelCurrentSpeech();
        
        /// <summary>Configure the provider (e.g., set API key)</summary>
        void Configure(Dictionary<string, string> settings);
    }
}
