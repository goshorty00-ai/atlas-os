using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AtlasAI.Core;
using AtlasAI.Voice;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AtlasAI.Services
{
    /// <summary>
    /// App-level singleton service for voice preview functionality.
    /// Works independently of ChatWindow and other UI components.
    /// Used by SettingsWindow and other parts of the app to preview TTS voices.
    /// </summary>
    public class VoicePreviewService : IDisposable
    {
        private static VoicePreviewService? _instance;
        private static readonly object _lock = new object();

        public static VoicePreviewService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VoicePreviewService();
                    }
                }
                return _instance;
            }
        }

        private readonly Dictionary<VoiceProviderType, IVoiceProvider> _providers;
        private readonly MediaPlayer _mediaPlayer;
        private readonly string _cacheDir;
        private IVoiceProvider _activeProvider;
        private bool _isSpeaking = false;
        private CancellationTokenSource? _currentCts;
        private bool _isDisposed = false;
        private readonly object _audioOutputLock = new object();
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioReader;

        // Events for UI feedback
        public event EventHandler? PreviewStarted;
        public event EventHandler? PreviewEnded;
        public event EventHandler<string>? PreviewError;

        private void DispatchToUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher ?? _mediaPlayer.Dispatcher;
            if (dispatcher == null)
            {
                action();
                return;
            }

            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(action);
        }

        private VoicePreviewService()
        {
            Debug.WriteLine("[VoicePreviewService] Initializing...");

            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;

            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "voice_preview_cache");

            _providers = new Dictionary<VoiceProviderType, IVoiceProvider>
            {
                { VoiceProviderType.WindowsSAPI, new WindowsSapiProvider() },
                { VoiceProviderType.EdgeTTS, new EdgeTtsProvider() },
                { VoiceProviderType.OpenAI, new OpenAITtsProvider() },
                { VoiceProviderType.ElevenLabs, new ElevenLabsProvider() }
            };

            _activeProvider = _providers[VoiceProviderType.WindowsSAPI];

            // Ensure cache directory exists
            Directory.CreateDirectory(_cacheDir);

            // Load API keys asynchronously
            _ = InitializeAsync();

            Debug.WriteLine("[VoicePreviewService] Initialized successfully");
        }

        private async Task InitializeAsync()
        {
            try
            {
                var voiceKeysPath = AtlasPaths.VoiceKeysReadCandidates().FirstOrDefault(File.Exists)
                    ?? AtlasPaths.RoamingVoiceKeysPath;

                if (File.Exists(voiceKeysPath))
                {
                    var json = await File.ReadAllTextAsync(voiceKeysPath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("elevenlabs", out var eleven))
                    {
                        var key = SecretProtector.UnprotectIfNeeded(eleven.GetString() ?? "");
                        if (!string.IsNullOrWhiteSpace(key))
                            _providers[VoiceProviderType.ElevenLabs].Configure(new Dictionary<string, string> { ["ApiKey"] = key });
                    }

                    if (root.TryGetProperty("openai", out var openai))
                    {
                        var key = SecretProtector.UnprotectIfNeeded(openai.GetString() ?? "");
                        if (!string.IsNullOrWhiteSpace(key))
                            _providers[VoiceProviderType.OpenAI].Configure(new Dictionary<string, string> { ["ApiKey"] = key });
                    }
                }

                Debug.WriteLine("[VoicePreviewService] API keys loaded");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicePreviewService] Failed to load API keys: {ex.Message}");
            }
        }

        /// <summary>
        /// Preview a voice by speaking test text.
        /// </summary>
        /// <param name="voiceId">Voice ID to preview</param>
        /// <param name="text">Text to speak (default: "This is a preview of the selected voice")</param>
        /// <param name="providerType">Provider to use (default: current active provider)</param>
        public async Task<(bool Success, string Error)> PreviewVoiceAsync(
            string voiceId,
            string text = "This is a preview of the selected voice",
            VoiceProviderType? providerType = null)
        {
            if (_isDisposed)
            {
                return (false, "Voice preview service is disposed");
            }

            if (string.IsNullOrWhiteSpace(voiceId))
            {
                return (false, "Voice ID is required");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = "This is a preview of the selected voice";
            }

            // Cancel any existing preview
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = new CancellationTokenSource();

            try
            {
                Debug.WriteLine($"[VoicePreviewService] Starting preview: voiceId={voiceId}, provider={providerType}");

                // Switch provider if specified
                if (providerType.HasValue && providerType.Value != _activeProvider.ProviderType)
                {
                    if (_providers.TryGetValue(providerType.Value, out var provider))
                    {
                        _activeProvider = provider;
                        Debug.WriteLine($"[VoicePreviewService] Switched to provider: {providerType.Value}");
                    }
                }

                // Get available voices
                var voices = await _activeProvider.GetVoicesAsync(_currentCts.Token).ConfigureAwait(false);
                var voice = voices.FirstOrDefault(v => v.Id == voiceId);

                if (voice == null)
                {
                    var error = $"Voice '{voiceId}' not found in {_activeProvider.ProviderType} provider";
                    Debug.WriteLine($"[VoicePreviewService] ❌ {error}");
                    return (false, error);
                }

                // Check if provider requires API key (cloud providers)
                if (voice.IsCloud && _activeProvider.RequiresApiKey)
                {
                    var isAvailable = await _activeProvider.IsAvailableAsync(_currentCts.Token).ConfigureAwait(false);
                    if (!isAvailable)
                    {
                        var error = $"{_activeProvider.ProviderType} requires API key configuration";
                        Debug.WriteLine($"[VoicePreviewService] ❌ {error}");
                        return (false, error);
                    }
                }

                _isSpeaking = true;
                DispatchToUi(() => PreviewStarted?.Invoke(this, EventArgs.Empty));

                // Generate speech using correct interface method
                Debug.WriteLine($"[VoicePreviewService] Generating speech with voice: {voice.DisplayName}");
                var options = new SynthesisOptions
                {
                    VoiceId = voiceId,
                    Rate = 1.0,
                    Volume = 1.0,
                    OutputFormat = "mp3"
                };

                var result = await _activeProvider.SynthesizeAsync(text, options, _currentCts.Token).ConfigureAwait(false);

                if (!result.Success || result.AudioData == null || result.AudioData.Length == 0)
                {
                    var error = result.ErrorMessage ?? "Failed to generate audio";
                    Debug.WriteLine($"[VoicePreviewService] ❌ {error}");
                    _isSpeaking = false;
                    PreviewEnded?.Invoke(this, EventArgs.Empty);
                    return (false, error);
                }

                // Save to cache
                var cacheFile = Path.Combine(_cacheDir, $"preview_{Guid.NewGuid()}.mp3");
                await File.WriteAllBytesAsync(cacheFile, result.AudioData, _currentCts.Token).ConfigureAwait(false);

                try
                {
                    await PlayPreviewAudioAsync(cacheFile, _currentCts.Token).ConfigureAwait(false);
                    Debug.WriteLine("[VoicePreviewService] ✅ Playing audio");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VoicePreviewService] ❌ Playback error: {ex.Message}");
                    _isSpeaking = false;
                    DispatchToUi(() => PreviewError?.Invoke(this, ex.Message));
                    DispatchToUi(() => PreviewEnded?.Invoke(this, EventArgs.Empty));
                }

                return (true, string.Empty);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[VoicePreviewService] Preview cancelled");
                return (false, "Preview cancelled");
            }
            catch (Exception ex)
            {
                var error = $"Preview failed: {ex.Message}";
                Debug.WriteLine($"[VoicePreviewService] ❌ {error}");
                DispatchToUi(() => PreviewError?.Invoke(this, error));
                return (false, error);
            }
        }

        /// <summary>
        /// Stop current preview playback.
        /// </summary>
        public void StopPreview()
        {
            Debug.WriteLine("[VoicePreviewService] Stopping preview");
            _currentCts?.Cancel();
            
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    StopNaudioPlayback();
                    _mediaPlayer.Stop();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VoicePreviewService] Error stopping playback: {ex.Message}");
                }
            });

            _isSpeaking = false;
            DispatchToUi(() => PreviewEnded?.Invoke(this, EventArgs.Empty));
        }

        /// <summary>
        /// Check if output device is available.
        /// </summary>
        public bool IsOutputDeviceAvailable()
        {
            try
            {
                // Check if we can access audio system via existing player
                return _mediaPlayer != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get status message for UI display.
        /// </summary>
        public string GetStatusMessage()
        {
            if (!IsOutputDeviceAvailable())
            {
                return "⚠️ Audio output device not available";
            }

            if (_activeProvider.RequiresApiKey && _activeProvider.ProviderType != VoiceProviderType.WindowsSAPI && _activeProvider.ProviderType != VoiceProviderType.EdgeTTS)
            {
                return $"⚠️ {_activeProvider.ProviderType} API key required";
            }

            return string.Empty;
        }

        private void OnMediaEnded(object? sender, EventArgs e)
        {
            Debug.WriteLine("[VoicePreviewService] Playback ended");
            _isSpeaking = false;
            PreviewEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnMediaFailed(object? sender, ExceptionEventArgs e)
        {
            var error = e?.ErrorException?.Message ?? "Playback failed";
            Debug.WriteLine($"[VoicePreviewService] ❌ Playback failed: {error}");
            _isSpeaking = false;
            DispatchToUi(() => PreviewError?.Invoke(this, error));
            DispatchToUi(() => PreviewEnded?.Invoke(this, EventArgs.Empty));
        }

        private async Task PlayPreviewAudioAsync(string audioFile, CancellationToken ct)
        {
            if (!File.Exists(audioFile))
                throw new FileNotFoundException("Preview audio file not found", audioFile);

            Exception? lastError = null;

            foreach (var backend in CreateWavePlayerFactories())
            {
                ct.ThrowIfCancellationRequested();
                StopNaudioPlayback();

                IWavePlayer? waveOut = null;
                AudioFileReader? reader = null;
                EventHandler<StoppedEventArgs>? playbackStopped = null;
                Exception? playbackError = null;

                try
                {
                    reader = new AudioFileReader(audioFile) { Volume = 0.8f };
                    waveOut = backend.Factory();
                    playbackStopped = (_, args) =>
                    {
                        if (args.Exception != null)
                            playbackError = args.Exception;
                    };

                    waveOut.PlaybackStopped += playbackStopped;
                    waveOut.Init(reader);

                    lock (_audioOutputLock)
                    {
                        _audioReader = reader;
                        _waveOut = waveOut;
                    }

                    waveOut.Play();

                    using var reg = ct.Register(() =>
                    {
                        try { waveOut.Stop(); } catch { }
                    });

                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (playbackError != null)
                            throw playbackError;

                        var isFinished = waveOut.PlaybackState == PlaybackState.Stopped
                            || reader.Position >= reader.Length
                            || reader.CurrentTime >= reader.TotalTime.Subtract(TimeSpan.FromMilliseconds(150));

                        if (isFinished)
                            return;

                        await Task.Delay(100, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Debug.WriteLine($"[VoicePreviewService] Audio backend '{backend.Name}' failed: {ex.Message}");
                }
                finally
                {
                    if (waveOut != null && playbackStopped != null)
                        waveOut.PlaybackStopped -= playbackStopped;
                    StopNaudioPlayback();
                }
            }

            throw lastError ?? new InvalidOperationException("No audio output backend available");
        }

        private void StopNaudioPlayback()
        {
            lock (_audioOutputLock)
            {
                try { _waveOut?.Stop(); } catch { }
                try { _waveOut?.Dispose(); } catch { }
                try { _audioReader?.Dispose(); } catch { }
                _waveOut = null;
                _audioReader = null;
            }
        }

        private static IEnumerable<(string Name, Func<IWavePlayer> Factory)> CreateWavePlayerFactories()
        {
            yield return ("WASAPI", () =>
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (device == null)
                    throw new InvalidOperationException("No default audio render device available");
                return new WasapiOut(device, AudioClientShareMode.Shared, false, 200);
            });

            yield return ("DirectSound", () => new DirectSoundOut());
            yield return ("WaveOut", () => new WaveOutEvent { DesiredLatency = 200 });
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Debug.WriteLine("[VoicePreviewService] Disposing...");

            _currentCts?.Cancel();
            _currentCts?.Dispose();

            try
            {
                StopNaudioPlayback();
                if (_mediaPlayer.Dispatcher.CheckAccess())
                    _mediaPlayer.Close();
                else
                    _mediaPlayer.Dispatcher.Invoke(() => _mediaPlayer.Close());
            }
            catch { }

            // Clean up cache directory
            try
            {
                if (Directory.Exists(_cacheDir))
                {
                    foreach (var file in Directory.GetFiles(_cacheDir))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }

            Debug.WriteLine("[VoicePreviewService] Disposed");
        }
    }
}
