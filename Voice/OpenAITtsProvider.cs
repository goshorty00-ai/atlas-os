using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Voice
{
    /// <summary>
    /// OpenAI TTS Provider - Premium cloud voices with excellent quality.
    /// Requires: OpenAI API key with TTS access.
    /// Voices: alloy, echo, fable, onyx, nova, shimmer
    /// </summary>
    public class OpenAITtsProvider : IVoiceProvider
    {
        private static readonly HttpClient _httpClient = new();
        private string? _apiKey;
        private CancellationTokenSource? _currentCts;

        private static readonly VoiceInfo[] BuiltInVoices = new[]
        {
            new VoiceInfo { Id = "alloy", DisplayName = "Alloy (Neutral)", Gender = "Neutral", Locale = "en", IsCloud = true, Provider = VoiceProviderType.OpenAI },
            new VoiceInfo { Id = "echo", DisplayName = "Echo (Male)", Gender = "Male", Locale = "en", IsCloud = true, Provider = VoiceProviderType.OpenAI },
            new VoiceInfo { Id = "fable", DisplayName = "Fable (Expressive)", Gender = "Neutral", Locale = "en", IsCloud = true, Provider = VoiceProviderType.OpenAI },
            new VoiceInfo { Id = "onyx", DisplayName = "Onyx (Deep Male)", Gender = "Male", Locale = "en", IsCloud = true, Provider = VoiceProviderType.OpenAI },
            new VoiceInfo { Id = "nova", DisplayName = "Nova (Female)", Gender = "Female", Locale = "en", IsCloud = true, Provider = VoiceProviderType.OpenAI },
            new VoiceInfo { Id = "shimmer", DisplayName = "Shimmer (Soft Female)", Gender = "Female", Locale = "en", IsCloud = true, Provider = VoiceProviderType.OpenAI },
        };

        public VoiceProviderType ProviderType => VoiceProviderType.OpenAI;
        public string DisplayName => "OpenAI TTS (Cloud)";
        public bool RequiresInternet => true;
        public bool RequiresApiKey => true;

        public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        {
            return Task.FromResult(!string.IsNullOrEmpty(_apiKey));
        }

        public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<VoiceInfo>>(BuiltInVoices);
        }

        public async Task<SynthesisResult> SynthesizeAsync(string text, SynthesisOptions options, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return new SynthesisResult
                {
                    Success = false,
                    ErrorMessage = "OpenAI API key not configured"
                };
            }

            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var audioFile = Path.Combine(Path.GetTempPath(), $"atlas_openai_{Guid.NewGuid()}.mp3");

            try
            {
                // OpenAI TTS speed: 0.25 to 4.0 (1.0 is normal)
                var speed = Math.Clamp(options.Rate, 0.25, 4.0);

                var request = new
                {
                    model = "tts-1",  // or "tts-1-hd" for higher quality
                    input = text,
                    voice = options.VoiceId,
                    speed = speed,
                    response_format = "mp3"
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
                requestMessage.Headers.Add("Authorization", $"Bearer {_apiKey}");
                requestMessage.Content = content;

                var response = await _httpClient.SendAsync(requestMessage, _currentCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var audioBytes = await response.Content.ReadAsByteArrayAsync(_currentCts.Token);
                    await File.WriteAllBytesAsync(audioFile, audioBytes, _currentCts.Token);

                    return new SynthesisResult
                    {
                        Success = true,
                        AudioFilePath = audioFile,
                        AudioData = audioBytes
                    };
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(_currentCts.Token);
                    return new SynthesisResult
                    {
                        Success = false,
                        ErrorMessage = $"OpenAI TTS error ({response.StatusCode}): {error}"
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return new SynthesisResult { Success = false, ErrorMessage = "Synthesis cancelled" };
            }
            catch (Exception ex)
            {
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
                _apiKey = apiKey;
            }
        }
    }
}
