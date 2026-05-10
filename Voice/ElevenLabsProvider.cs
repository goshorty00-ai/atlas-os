using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Voice
{
    /// <summary>
    /// ElevenLabs voice settings for optimal AI assistant pacing and tone
    /// Optimized for JARVIS-like calm, measured, sophisticated speech
    /// </summary>
    public class ElevenLabsVoiceSettings
    {
        /// <summary>Stability (0.0-1.0) - Higher values = slower, more measured speech. Max for JARVIS-like calm delivery</summary>
        public double Stability { get; set; } = 1.0;
        
        /// <summary>Similarity boost (0.0-1.0) - How closely to match the original voice</summary>
        public double SimilarityBoost { get; set; } = 0.70;
        
        /// <summary>Style (0.0-1.0) - How much style to apply (0 = neutral, 1 = maximum style). Zero for calm delivery</summary>
        public double Style { get; set; } = 0.0;
        
        /// <summary>Use speaker boost - Enhances voice clarity and consistency</summary>
        public bool UseSpeakerBoost { get; set; } = true;
    }

    /// <summary>
    /// ElevenLabs TTS Provider - Premium expressive voices.
    /// Requires: ElevenLabs API key.
    /// Supports both default voices and custom "My Voices" from your account.
    /// Optimized for calm, premium AI assistant voice pacing and tone.
    /// </summary>
    public class ElevenLabsProvider : IVoiceProvider
    {
        private static readonly HttpClient _httpClient = new();
        private const string PreferredModelId = "eleven_turbo_v2";
        private const string FallbackModelId = "eleven_monolingual_v1";
        private string? _apiKey;
        private CancellationTokenSource? _currentCts;
        private List<VoiceInfo>? _cachedVoices;
        private DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
        
        /// <summary>Voice settings optimized for calm, premium AI assistant</summary>
        public static readonly ElevenLabsVoiceSettings DefaultVoiceSettings = new();
        
        /// <summary>Current voice settings (can be modified at runtime)</summary>
        public static ElevenLabsVoiceSettings CurrentVoiceSettings { get; set; } = new();
        
        /// <summary>
        /// Update voice settings for all future synthesis requests
        /// </summary>
        /// <param name="stability">Voice stability (0.0-1.0) - Higher values make voice more consistent but less expressive</param>
        /// <param name="similarityBoost">Similarity boost (0.0-1.0) - How closely to match the original voice</param>
        /// <param name="style">Style (0.0-1.0) - How much style to apply (0 = neutral, 1 = maximum style)</param>
        /// <param name="useSpeakerBoost">Use speaker boost - Enhances voice clarity and consistency</param>
        public static void UpdateVoiceSettings(double stability = 0.90, double similarityBoost = 0.80, double style = 0.05, bool useSpeakerBoost = true)
        {
            CurrentVoiceSettings.Stability = Math.Clamp(stability, 0.0, 1.0);
            CurrentVoiceSettings.SimilarityBoost = Math.Clamp(similarityBoost, 0.0, 1.0);
            CurrentVoiceSettings.Style = Math.Clamp(style, 0.0, 1.0);
            CurrentVoiceSettings.UseSpeakerBoost = useSpeakerBoost;
            
            Debug.WriteLine($"[ElevenLabs] Voice settings updated - Stability: {CurrentVoiceSettings.Stability}, Similarity: {CurrentVoiceSettings.SimilarityBoost}, Style: {CurrentVoiceSettings.Style}, Speaker Boost: {CurrentVoiceSettings.UseSpeakerBoost}");
        }
        
        /// <summary>Event fired when voice fetch status changes (for UI feedback)</summary>
        public event Action<string>? StatusChanged;
        
        /// <summary>Last error message from API call</summary>
        public string? LastError { get; private set; }

        // Default voices (ElevenLabs pre-made voices - fallback when no API key)
        private static readonly VoiceInfo[] DefaultVoices = new[]
        {
            new VoiceInfo { Id = VoiceProfile.DefaultAtlasVoiceId, DisplayName = "Daniel (Atlas Default)", Gender = "Male", Locale = "en", IsCloud = true, Provider = VoiceProviderType.ElevenLabs, Category = "Default" },
            // Standard ElevenLabs voices
            new VoiceInfo { Id = "21m00Tcm4TlvDq8ikWAM", DisplayName = "Rachel (Calm Female)", Gender = "Female", Locale = "en", IsCloud = true, Provider = VoiceProviderType.ElevenLabs, Category = "Default" },
            new VoiceInfo { Id = "AZnzlk1XvdvUeBnXmlld", DisplayName = "Domi (Strong Female)", Gender = "Female", Locale = "en", IsCloud = true, Provider = VoiceProviderType.ElevenLabs, Category = "Default" },
            new VoiceInfo { Id = "EXAVITQu4vr4xnSDxMaL", DisplayName = "Bella (Soft Female)", Gender = "Female", Locale = "en", IsCloud = true, Provider = VoiceProviderType.ElevenLabs, Category = "Default" },
            new VoiceInfo { Id = "ErXwobaYiN019PkySvjV", DisplayName = "Antoni (Warm Male)", Gender = "Male", Locale = "en", IsCloud = true, Provider = VoiceProviderType.ElevenLabs, Category = "Default" },
            new VoiceInfo { Id = "VR6AewLTigWG4xSOukaG", DisplayName = "Arnold (Deep Male)", Gender = "Male", Locale = "en", IsCloud = true, Provider = VoiceProviderType.ElevenLabs, Category = "Default" },
            new VoiceInfo { Id = "pNInz6obpgDQGcFmaJgB", DisplayName = "Adam (Narrator Male)", Gender = "Male", Locale = "en", IsCloud = true, Provider = VoiceProviderType.ElevenLabs, Category = "Default" },
        };

        public VoiceProviderType ProviderType => VoiceProviderType.ElevenLabs;
        public string DisplayName => "ElevenLabs (Cloud)";
        public bool RequiresInternet => true;
        public bool RequiresApiKey => true;

        public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        {
            return Task.FromResult(!string.IsNullOrEmpty(_apiKey));
        }

        /// <summary>
        /// Force refresh voices from ElevenLabs API (clears cache)
        /// </summary>
        public void RefreshVoices()
        {
            _cachedVoices = null;
            _cacheTime = DateTime.MinValue;
            Debug.WriteLine("[ElevenLabs] Voice cache cleared - will refresh on next GetVoicesAsync");
        }

        public async Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
        {
            // Return cached if still valid
            if (_cachedVoices != null && DateTime.Now - _cacheTime < CacheDuration)
            {
                Debug.WriteLine($"[ElevenLabs] Returning {_cachedVoices.Count} cached voices");
                return _cachedVoices;
            }
            
            if (string.IsNullOrEmpty(_apiKey))
            {
                Debug.WriteLine("[ElevenLabs] No API key configured, returning default voices with Atlas");
                LastError = "No API key - using defaults";
                StatusChanged?.Invoke("No ElevenLabs API key - using default voices");
                return DefaultVoices;
            }

            try
            {
                Debug.WriteLine($"[ElevenLabs] Fetching voices from API with key: {_apiKey?.Substring(0, Math.Min(8, _apiKey?.Length ?? 0))}...");
                StatusChanged?.Invoke("Fetching voices from ElevenLabs...");
                
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
                request.Headers.Add("xi-api-key", _apiKey);

                var response = await _httpClient.SendAsync(request, ct);
                Debug.WriteLine($"[ElevenLabs] API response: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    
                    var voices = new List<VoiceInfo>();
                    
                    foreach (var voice in doc.RootElement.GetProperty("voices").EnumerateArray())
                    {
                        var voiceId = voice.GetProperty("voice_id").GetString() ?? "";
                        var name = voice.GetProperty("name").GetString() ?? "";
                        var category = voice.TryGetProperty("category", out var cat) ? cat.GetString() ?? "premade" : "premade";
                        
                        // Determine display category
                        string displayCategory;
                        if (category == "cloned" || category == "generated" || category == "professional")
                            displayCategory = "My Voices";
                        else if (category == "premade")
                            displayCategory = "Default";
                        else
                            displayCategory = category;
                        
                        var gender = "Unknown";
                        if (voice.TryGetProperty("labels", out var labels) && labels.TryGetProperty("gender", out var genderProp))
                            gender = genderProp.GetString() ?? "Unknown";

                        voices.Add(new VoiceInfo
                        {
                            Id = voiceId,
                            DisplayName = name,
                            Gender = gender,
                            Locale = "en",
                            IsCloud = true,
                            Provider = VoiceProviderType.ElevenLabs,
                            Category = displayCategory
                        });
                        
                        Debug.WriteLine($"[ElevenLabs] Found voice: {name} ({voiceId}) - Category: {displayCategory}");
                    }

                    // Sort: My Voices first, then Default
                    voices.Sort((a, b) =>
                    {
                        if (a.Category == "My Voices" && b.Category != "My Voices") return -1;
                        if (a.Category != "My Voices" && b.Category == "My Voices") return 1;
                        return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                    });

                    _cachedVoices = voices;
                    _cacheTime = DateTime.Now;
                    LastError = null;
                    Debug.WriteLine($"[ElevenLabs] Cached {voices.Count} voices (including Atlas)");
                    StatusChanged?.Invoke($"Loaded {voices.Count} voices from ElevenLabs");
                    return voices;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    LastError = $"API error: {response.StatusCode}";
                    Debug.WriteLine($"[ElevenLabs] API error: {response.StatusCode} - {error}");
                    StatusChanged?.Invoke($"ElevenLabs API error: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.WriteLine($"[ElevenLabs] Error fetching voices: {ex.Message}");
                StatusChanged?.Invoke($"Error: {ex.Message}");
            }

            return DefaultVoices;
        }

        public async Task<SynthesisResult> SynthesizeAsync(string text, SynthesisOptions options, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return new SynthesisResult
                {
                    Success = false,
                    ErrorMessage = "ElevenLabs API key not configured"
                };
            }

            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var audioFile = Path.Combine(Path.GetTempPath(), $"atlas_eleven_{Guid.NewGuid()}.mp3");

            try
            {
                var sw = Stopwatch.StartNew();
                Debug.WriteLine($"[ElevenLabs] Synthesizing with voice settings - Stability: {CurrentVoiceSettings.Stability}, Similarity: {CurrentVoiceSettings.SimilarityBoost}, Style: {CurrentVoiceSettings.Style}, Speaker Boost: {CurrentVoiceSettings.UseSpeakerBoost}");
                
                // ElevenLabs request with optimized voice settings for calm, premium AI assistant
                async Task<HttpResponseMessage> SendAsync(string modelId)
                {
                    var request = new
                    {
                        text = text,
                        model_id = modelId,
                        voice_settings = new
                        {
                            stability = CurrentVoiceSettings.Stability,
                            similarity_boost = CurrentVoiceSettings.SimilarityBoost,
                            style = CurrentVoiceSettings.Style,
                            use_speaker_boost = CurrentVoiceSettings.UseSpeakerBoost
                        }
                    };

                    var json = JsonSerializer.Serialize(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var requestMessage = new HttpRequestMessage(
                        HttpMethod.Post,
                        $"https://api.elevenlabs.io/v1/text-to-speech/{options.VoiceId}");
                    requestMessage.Headers.Add("xi-api-key", _apiKey);
                    requestMessage.Headers.Add("Accept", "audio/mpeg");
                    requestMessage.Headers.Add("xi-optimize-streaming-latency", "4");
                    requestMessage.Content = content;

                    return await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, _currentCts.Token);
                }

                var response = await SendAsync(PreferredModelId);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(_currentCts.Token);
                    Debug.WriteLine($"[ElevenLabs] API error (model={PreferredModelId}): {response.StatusCode} - {error}");

                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        response.Dispose();
                        response = await SendAsync(FallbackModelId);
                    }
                    else
                    {
                        return new SynthesisResult
                        {
                            Success = false,
                            ErrorMessage = $"ElevenLabs error ({response.StatusCode}): {error}"
                        };
                    }
                }

                if (response.IsSuccessStatusCode)
                {
                    var audioBytes = await response.Content.ReadAsByteArrayAsync(_currentCts.Token);
                    await File.WriteAllBytesAsync(audioFile, audioBytes, _currentCts.Token);

                    sw.Stop();
                    Debug.WriteLine($"[ElevenLabs] Successfully synthesized {audioBytes.Length} bytes in {sw.ElapsedMilliseconds}ms");
                    return new SynthesisResult
                    {
                        Success = true,
                        AudioFilePath = audioFile,
                        AudioData = audioBytes
                    };
                }

                var finalError = await response.Content.ReadAsStringAsync(_currentCts.Token);
                Debug.WriteLine($"[ElevenLabs] API error (final): {response.StatusCode} - {finalError}");
                return new SynthesisResult
                {
                    Success = false,
                    ErrorMessage = $"ElevenLabs error ({response.StatusCode}): {finalError}"
                };
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ElevenLabs] Synthesis cancelled");
                return new SynthesisResult { Success = false, ErrorMessage = "Synthesis cancelled" };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ElevenLabs] Synthesis error: {ex.Message}");
                return new SynthesisResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _currentCts = null;
            }
        }

        public void CancelCurrentSpeech()
        {
            _currentCts?.Cancel();
        }

        public void Configure(Dictionary<string, string> settings)
        {
            if (settings.TryGetValue("ApiKey", out var apiKey))
            {
                _apiKey = ApiKeySanitizer.SanitizeForHttpHeader(apiKey);
                _cachedVoices = null; // Refresh voices on new key
            }
        }
    }
}
