using System;
using System.Collections.Generic;
using System.IO;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Windows SAPI Provider - Built-in Windows voices with INSTANT playback.
    /// No file generation, no network latency - speaks immediately.
    /// Best for: Fast responses, offline use, low latency requirements.
    /// </summary>
    public class WindowsSapiProvider : IVoiceProvider
    {
        private SpeechSynthesizer? _synthesizer;
        private CancellationTokenSource? _currentCts;
        private TaskCompletionSource<bool>? _speakingTcs;
        private List<VoiceInfo>? _cachedVoices;

        private readonly string _cacheDir;

        public VoiceProviderType ProviderType => VoiceProviderType.WindowsSAPI;
        public string DisplayName => "Windows (Instant)";
        public bool RequiresInternet => false;
        public bool RequiresApiKey => false;

        public WindowsSapiProvider()
        {
            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "voice_cache");
            try { Directory.CreateDirectory(_cacheDir); } catch { }
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        {
            try
            {
                using var synth = new SpeechSynthesizer();
                return Task.FromResult(synth.GetInstalledVoices().Count > 0);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
        {
            if (_cachedVoices != null)
                return Task.FromResult<IReadOnlyList<VoiceInfo>>(_cachedVoices);

            var voices = new List<VoiceInfo>();
            
            try
            {
                using var synth = new SpeechSynthesizer();
                foreach (var voice in synth.GetInstalledVoices())
                {
                    if (voice.Enabled)
                    {
                        var info = voice.VoiceInfo;
                        voices.Add(new VoiceInfo
                        {
                            Id = info.Name,
                            DisplayName = $"{info.Name} ({info.Culture.DisplayName})",
                            Gender = info.Gender.ToString(),
                            Locale = info.Culture.Name,
                            IsCloud = false,
                            Provider = VoiceProviderType.WindowsSAPI
                        });
                    }
                }
            }
            catch { }

            // Add some common Windows voices as fallback if none found
            if (voices.Count == 0)
            {
                voices.Add(new VoiceInfo
                {
                    Id = "Microsoft David Desktop",
                    DisplayName = "David (US Male)",
                    Gender = "Male",
                    Locale = "en-US",
                    IsCloud = false,
                    Provider = VoiceProviderType.WindowsSAPI
                });
                voices.Add(new VoiceInfo
                {
                    Id = "Microsoft Zira Desktop",
                    DisplayName = "Zira (US Female)",
                    Gender = "Female",
                    Locale = "en-US",
                    IsCloud = false,
                    Provider = VoiceProviderType.WindowsSAPI
                });
            }

            _cachedVoices = voices;
            return Task.FromResult<IReadOnlyList<VoiceInfo>>(voices);
        }

        public async Task<SynthesisResult> SynthesizeAsync(string text, SynthesisOptions options, CancellationToken ct = default)
        {
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _speakingTcs = new TaskCompletionSource<bool>();

            try
            {
                _synthesizer = new SpeechSynthesizer();

                // Generate a wav file for playback via VoiceManager's MediaPlayer.
                // This avoids cases where direct SAPI output produces silence.
                var fileName = $"sapi_{Guid.NewGuid():N}.wav";
                var wavPath = Path.Combine(_cacheDir, fileName);
                
                // Configure voice
                try
                {
                    _synthesizer.SelectVoice(options.VoiceId);
                }
                catch
                {
                    // Use default voice if specified one not found
                }

                // Configure rate (-10 to 10, where 0 is normal)
                // Our rate is 0.5 to 2.0, so convert: (rate - 1) * 10
                var sapiRate = (int)((options.Rate - 1.0) * 10);
                _synthesizer.Rate = Math.Clamp(sapiRate, -10, 10);

                // Configure volume (0-100)
                _synthesizer.Volume = (int)(options.Volume * 100);

                // Set up completion handler
                _synthesizer.SpeakCompleted += (s, e) =>
                {
                    _speakingTcs?.TrySetResult(!e.Cancelled);
                };

                // Output to wav file
                try
                {
                    _synthesizer.SetOutputToWaveFile(wavPath);
                }
                catch
                {
                    // If wave output fails, try device output as a last resort.
                    try { _synthesizer.SetOutputToDefaultAudioDevice(); } catch { }
                }

                _synthesizer.SpeakAsync(text);

                // Wait for completion or cancellation
                using var reg = _currentCts.Token.Register(() =>
                {
                    _synthesizer?.SpeakAsyncCancelAll();
                    _speakingTcs?.TrySetCanceled();
                });

                await _speakingTcs.Task;

                return new SynthesisResult
                {
                    Success = true,
                    AudioFilePath = File.Exists(wavPath) ? wavPath : null
                };
            }
            catch (OperationCanceledException)
            {
                return new SynthesisResult { Success = false, ErrorMessage = "Speech cancelled" };
            }
            catch (Exception ex)
            {
                return new SynthesisResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _synthesizer?.Dispose();
                _synthesizer = null;
                _currentCts = null;
                _speakingTcs = null;
            }
        }

        public void CancelCurrentSpeech()
        {
            try
            {
                _currentCts?.Cancel();
                _synthesizer?.SpeakAsyncCancelAll();
            }
            catch { }
        }

        public void Configure(Dictionary<string, string> settings)
        {
            // Windows SAPI doesn't need configuration
        }
    }
}
