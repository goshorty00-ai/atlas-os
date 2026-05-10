using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AtlasAI.Core;
using AtlasAI.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Central voice management system for Atlas AI.
    /// Handles provider switching, voice selection, playback, and caching.
    /// Implements single-speaker gate to prevent duplicate/overlapping speech.
    /// </summary>
    public class VoiceManager : IDisposable
    {
        private readonly Dictionary<VoiceProviderType, IVoiceProvider> _providers;
        private readonly MediaPlayer _mediaPlayer;
        private readonly string _cacheDir;
        private readonly string _settingsPath;
        private readonly Task _initTask;
        private readonly object _audioOutputLock = new object();
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioReader;
        
        private IVoiceProvider _activeProvider;
        private VoiceInfo? _selectedVoice;
        private bool _speechEnabled = true;
        private double _volume = 1.0;
        private double _rate = 1.0; // Normal speed
        private CancellationTokenSource? _playbackCts;
        
        // === Single-Speaker Gate ===
        // Prevents duplicate speech for the same turn and overlapping audio
        private Guid _currentTurnId = Guid.Empty;
        private bool _isSpeakingInternal = false;
        private readonly object _speakerGateLock = new object();
        private CancellationTokenSource? _speechCts;
        private int _lastSpokenHash = 0;
        private long _speechSessionCounter = 0;
        private long _activeSpeechSessionId = 0;

        public event EventHandler? SpeechStarted;
        public event EventHandler? SpeechEnded;
        public event EventHandler<string>? SpeechError;
        public event EventHandler<string>? StatusUpdated;
        public event EventHandler<VoiceProviderType>? ProviderChanged;

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

        private void RaiseSpeechStarted()
        {
            try { DispatchToUi(() => SpeechStarted?.Invoke(this, EventArgs.Empty)); } catch { }
        }

        private void RaiseSpeechEnded()
        {
            try { DispatchToUi(() => SpeechEnded?.Invoke(this, EventArgs.Empty)); } catch { }
        }

        private void RaiseSpeechError(string message)
        {
            try { DispatchToUi(() => SpeechError?.Invoke(this, message)); } catch { }
        }

        private void RaiseStatus(string message)
        {
            try { DispatchToUi(() => StatusUpdated?.Invoke(this, message)); } catch { }
        }

        private void RaiseProviderChanged(VoiceProviderType type)
        {
            try { DispatchToUi(() => ProviderChanged?.Invoke(this, type)); } catch { }
        }
        
        /// <summary>
        /// Current turn ID being spoken (for debugging)
        /// </summary>
        public Guid CurrentTurnId => _currentTurnId;

        public VoiceManager()
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += (s, e) => OnSpeechEnded();
            _mediaPlayer.MediaFailed += (s, e) =>
            {
                try
                {
                    var msg = e?.ErrorException?.Message ?? "Playback failed";
                    RaiseSpeechError(msg);
                }
                catch
                {
                }
            };

            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "voice_cache");
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "voice_settings.json");

            _providers = new Dictionary<VoiceProviderType, IVoiceProvider>
            {
                { VoiceProviderType.WindowsSAPI, new WindowsSapiProvider() },
                { VoiceProviderType.EdgeTTS, new EdgeTtsProvider() },
                { VoiceProviderType.OpenAI, new OpenAITtsProvider() },
                { VoiceProviderType.ElevenLabs, new ElevenLabsProvider() }
            };

            var voiceKeysPath = AtlasPaths.VoiceKeysReadCandidates().FirstOrDefault(File.Exists)
                ?? AtlasPaths.RoamingVoiceKeysPath;

            _activeProvider = _providers[VoiceProviderType.WindowsSAPI];

            // Ensure cache directory exists
            Directory.CreateDirectory(_cacheDir);

            // Load API keys and settings asynchronously after construction
            _initTask = InitializeAsync(voiceKeysPath);

            VoicePreferences.PreferencesChanged += OnVoicePreferencesChanged;
        }

        public Task WaitForInitializationAsync()
        {
            return _initTask ?? Task.CompletedTask;
        }

        private static bool ShouldSuppressWindowsFallback(VoiceProviderType providerType)
        {
            return providerType == VoiceProviderType.ElevenLabs;
        }
        private static bool ShouldIgnoreSavedProvider(VoiceProviderType providerType)
        {
            // Migrate away from persisted ElevenLabs defaults so local/free providers can take over.
            return providerType == VoiceProviderType.ElevenLabs;
        }

        private static bool ShouldPreferConfiguredElevenLabsOverSavedProvider(VoiceProviderType savedProvider)
        {
            return savedProvider == VoiceProviderType.WindowsSAPI && HasConfiguredProviderKey(VoiceProviderType.ElevenLabs);
        }

        private async Task InitializeAsync(string voiceKeysPath)
        {
            string? elevenLabsKey = null;
            string? openAiTtsKey = null;
            try
            {
                if (File.Exists(voiceKeysPath))
                {
                    var json = await File.ReadAllTextAsync(voiceKeysPath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("elevenlabs", out var eleven))
                        elevenLabsKey = SecretProtector.UnprotectIfNeeded(eleven.GetString() ?? "");
                    if (root.TryGetProperty("openai", out var openai))
                        openAiTtsKey = SecretProtector.UnprotectIfNeeded(openai.GetString() ?? "");
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(elevenLabsKey))
                _providers[VoiceProviderType.ElevenLabs].Configure(new Dictionary<string, string> { ["ApiKey"] = elevenLabsKey });
            if (!string.IsNullOrWhiteSpace(openAiTtsKey))
                _providers[VoiceProviderType.OpenAI].Configure(new Dictionary<string, string> { ["ApiKey"] = openAiTtsKey });

            await LoadSettingsAsync();
        }

        // Properties
        public bool SpeechEnabled
        {
            get => _speechEnabled;
            set
            {
                _speechEnabled = value;
                if (!value) Stop();
                SaveSettings();
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0, 1.0);
                try
                {
                    if (_mediaPlayer.Dispatcher.CheckAccess())
                        _mediaPlayer.Volume = _volume;
                    else
                        _mediaPlayer.Dispatcher.Invoke(() => _mediaPlayer.Volume = _volume);
                }
                catch
                {
                }
                SaveSettings();
            }
        }

        public double Rate
        {
            get => _rate;
            set
            {
                _rate = Math.Clamp(value, 0.5, 2.0);
                SaveSettings();
            }
        }

        public VoiceProviderType ActiveProviderType => _activeProvider.ProviderType;
        public VoiceInfo? SelectedVoice => _selectedVoice;
        public bool IsCloudVoice => _selectedVoice?.IsCloud ?? false;
        public bool IsSpeaking => _isSpeakingInternal;
        
        /// <summary>
        /// Debug status string for UI indicator
        /// </summary>
        public string DebugStatus => SpeechDedupeLogger.GetDebugStatus(_isSpeakingInternal, _currentTurnId == Guid.Empty ? null : _currentTurnId);

        /// <summary>Get all registered providers</summary>
        public IEnumerable<IVoiceProvider> GetProviders() => _providers.Values;

        /// <summary>Get a specific provider</summary>
        public IVoiceProvider GetProvider(VoiceProviderType type) => _providers[type];

        /// <summary>Refresh voices from the active provider (clears cache)</summary>
        public void RefreshVoices()
        {
            if (_activeProvider is ElevenLabsProvider elevenLabs)
            {
                elevenLabs.RefreshVoices();
            }
            // Other providers can be added here if they support refresh
        }

        /// <summary>Switch to a different voice provider</summary>
        public async Task<bool> SetProviderAsync(VoiceProviderType type, CancellationToken ct = default)
        {
            return await SetProviderInternalAsync(type, stopSpeech: true, ct);
        }

        private async Task<bool> SetProviderInternalAsync(VoiceProviderType type, bool stopSpeech, CancellationToken ct = default)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceManager] SetProviderAsync called for {type} (stopSpeech={stopSpeech})");

            if (!_providers.TryGetValue(type, out var provider))
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Provider {type} not found");
                return false;
            }

            // Auto-load persisted API keys for cloud providers so switching works without a restart.
            // (API Manager writes to voice_keys.json, but VoiceManager instances are long-lived.)
            TryAutoConfigureCloudProvider(type, provider);

            if (!await provider.IsAvailableAsync(ct))
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Provider {type} not available");
                return false;
            }

            if (stopSpeech)
            {
                Stop();
            }
            else
            {
                // When switching providers mid-speech-selection, don't cancel the current speech session.
                // We'll explicitly stop playback/provider output without touching the active speech CTS.
                StopPlaybackOnly();
            }

            _activeProvider = provider;
            
            // Try to restore saved voice first
            string? savedVoiceId = null;
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("voiceId", out var vid))
                        savedVoiceId = vid.GetString();
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] Found saved voiceId: {savedVoiceId}");
                }
            }
            catch { }
            
            // Get available voices
            var voices = await provider.GetVoicesAsync(ct);
            
            // Try to use saved voice if it exists for this provider
            if (!string.IsNullOrEmpty(savedVoiceId))
            {
                var savedVoice = voices.FirstOrDefault(v => v.Id == savedVoiceId);
                if (savedVoice != null)
                {
                    _selectedVoice = savedVoice;
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] Restored saved voice: {_selectedVoice.DisplayName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] Saved voice '{savedVoiceId}' not found in provider {type}");
                    _selectedVoice = null; // Will fall through to default selection
                }
            }
            
            // If no saved voice or saved voice not found, select default
            if (_selectedVoice == null)
            {
                // For ElevenLabs, prefer the Atlas AI voice
                if (type == VoiceProviderType.ElevenLabs)
                {
                    _selectedVoice = voices.FirstOrDefault(v => v.Id == VoiceProfile.DefaultAtlasVoiceId)
                                  ?? voices.FirstOrDefault(v => v.DisplayName?.Contains("Atlas", StringComparison.OrdinalIgnoreCase) == true)
                                  ?? voices.FirstOrDefault(v => v.DisplayName?.Contains("Daniel", StringComparison.OrdinalIgnoreCase) == true)
                                  ?? voices.FirstOrDefault();
                }
                else
                {
                    _selectedVoice = voices.FirstOrDefault();
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[VoiceManager] Provider set to {type}, selected voice: {_selectedVoice?.DisplayName ?? "NULL"}");
            
            RaiseProviderChanged(type);
            // Don't save settings here - only save when user explicitly changes voice
            return true;
        }

        private void TryAutoConfigureCloudProvider(VoiceProviderType type, IVoiceProvider provider)
        {
            try
            {
                // Only attempt this for known providers that use voice_keys.json
                var providerKey = type switch
                {
                    VoiceProviderType.ElevenLabs => "elevenlabs",
                    VoiceProviderType.OpenAI => "openai",
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(providerKey))
                    return;

                // If already configured, no-op.
                try
                {
                    var available = provider.IsAvailableAsync(CancellationToken.None).GetAwaiter().GetResult();
                    if (available)
                        return;
                }
                catch
                {
                }

                var apiKey = TryReadVoiceApiKeyFromDisk(providerKey);
                if (string.IsNullOrWhiteSpace(apiKey))
                    return;

                var sanitized = ApiKeySanitizer.SanitizeForHttpHeader(apiKey);
                sanitized = (sanitized ?? "").Trim();
                if (string.IsNullOrWhiteSpace(sanitized))
                    return;

                ConfigureProvider(type, new Dictionary<string, string> { ["ApiKey"] = sanitized });

                // Clear cached voice lists so the next GetVoicesAsync uses the new key.
                try
                {
                    if (type == VoiceProviderType.ElevenLabs)
                        RefreshVoices();
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private static string? TryReadVoiceApiKeyFromDisk(string provider)
        {
            try
            {
                var voiceKeysPath = AtlasPaths.VoiceKeysReadCandidates().FirstOrDefault(File.Exists)
                    ?? AtlasPaths.RoamingVoiceKeysPath;
                if (!File.Exists(voiceKeysPath))
                    return null;

                var json = File.ReadAllText(voiceKeysPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(provider, out var el) && el.ValueKind == JsonValueKind.String)
                {
                    var raw = (el.GetString() ?? "").Trim();
                    return SecretProtector.UnprotectIfNeeded(raw);
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool HasConfiguredProviderKey(VoiceProviderType type)
        {
            var providerKey = type switch
            {
                VoiceProviderType.ElevenLabs => "elevenlabs",
                VoiceProviderType.OpenAI => "openai",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(providerKey))
                return false;

            var key = TryReadVoiceApiKeyFromDisk(providerKey);
            return !string.IsNullOrWhiteSpace(key);
        }

        private void OnVoicePreferencesChanged(VoicePreferences prefs)
        {
            if (prefs == null)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await WaitForInitializationAsync().ConfigureAwait(false);
                    var preferredVoiceId = GetDirectPreferenceVoiceId(ResponseType.Normal, prefs);
                    if (!string.IsNullOrWhiteSpace(preferredVoiceId))
                        await TryApplyPreferredVoiceAsync(preferredVoiceId, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            });
        }

        private static string? GetDirectPreferenceVoiceId(ResponseType responseType, VoicePreferences? prefs = null)
        {
            prefs ??= VoicePreferences.Current;

            if (responseType == ResponseType.Boot || responseType == ResponseType.Alert || responseType == ResponseType.Error)
                return string.IsNullOrWhiteSpace(prefs.SystemVoiceId) ? null : prefs.SystemVoiceId.Trim();

            return string.IsNullOrWhiteSpace(prefs.GlobalVoiceId) ? null : prefs.GlobalVoiceId.Trim();
        }

        private async Task<bool> TryApplyPreferredVoiceAsync(string voiceId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(voiceId))
                return false;

            voiceId = voiceId.Trim();

            if (string.Equals(_selectedVoice?.Id, voiceId, StringComparison.Ordinal))
                return true;

            var providersToTry = new List<VoiceProviderType> { _activeProvider.ProviderType };
            foreach (var providerType in new[]
                     {
                         VoiceProviderType.ElevenLabs,
                         VoiceProviderType.OpenAI,
                         VoiceProviderType.EdgeTTS,
                         VoiceProviderType.WindowsSAPI
                     })
            {
                if (!providersToTry.Contains(providerType))
                    providersToTry.Add(providerType);
            }

            foreach (var providerType in providersToTry)
            {
                if (!_providers.TryGetValue(providerType, out var provider))
                    continue;

                TryAutoConfigureCloudProvider(providerType, provider);

                bool available;
                try
                {
                    available = await provider.IsAvailableAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                if (!available)
                    continue;

                IReadOnlyList<VoiceInfo> voices;
                try
                {
                    voices = await provider.GetVoicesAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                if (!voices.Any(v => string.Equals(v.Id, voiceId, StringComparison.Ordinal)))
                    continue;

                if (_activeProvider.ProviderType != providerType)
                {
                    var providerSet = await SetProviderInternalAsync(providerType, stopSpeech: false, ct).ConfigureAwait(false);
                    if (!providerSet)
                        continue;
                }

                return await SelectVoiceAsync(voiceId, ct).ConfigureAwait(false);
            }

            return false;
        }

        private void StopPlaybackOnly()
        {
            try { _activeProvider.CancelCurrentSpeech(); } catch { }
            try { StopNaudioPlayback(); } catch { }

            try
            {
                if (_mediaPlayer.Dispatcher.CheckAccess())
                    _mediaPlayer.Stop();
                else
                    _mediaPlayer.Dispatcher.Invoke(() => _mediaPlayer.Stop());
            }
            catch { }
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

        /// <summary>Configure a provider with settings (e.g., API key)</summary>
        public void ConfigureProvider(VoiceProviderType type, Dictionary<string, string> settings)
        {
            if (_providers.TryGetValue(type, out var provider))
            {
                provider.Configure(settings);
            }
        }

        /// <summary>Get all voices from the active provider</summary>
        public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
        {
            return _activeProvider.GetVoicesAsync(ct);
        }

        /// <summary>Select a voice by ID</summary>
        public async Task<bool> SelectVoiceAsync(string voiceId, CancellationToken ct = default)
        {
            var voices = await _activeProvider.GetVoicesAsync(ct);
            var voice = voices.FirstOrDefault(v => v.Id == voiceId);
            if (voice != null)
            {
                _selectedVoice = voice;
                SaveSettings();
                return true;
            }
            return false;
        }

        /// <summary>Restore the saved voice from settings file (call after API keys are configured)</summary>
        public async Task RestoreSavedVoiceAsync()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = await File.ReadAllTextAsync(_settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    // 1. Restore Provider
                    if (root.TryGetProperty("provider", out var prov) && 
                        Enum.TryParse<VoiceProviderType>(prov.GetString(), out var provType))
                    {
                        if (_providers.TryGetValue(provType, out var provider))
                        {
                            _activeProvider = provider;
                        }
                    }

                    // 2. Restore Voice
                    if (root.TryGetProperty("voiceId", out var vid))
                    {
                        var voiceId = vid.GetString();
                        if (!string.IsNullOrEmpty(voiceId))
                        {
                            // Try to select this voice on the active provider
                            var success = await SelectVoiceAsync(voiceId);
                            if (!success)
                            {
                                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Saved voice '{voiceId}' not found on provider {_activeProvider.ProviderType}. Trying fallback.");
                            }
                        }
                    }
                }

                // 3. Validation & Fallback
                if (_selectedVoice == null)
                {
                    // Force a default selection
                    var voices = await _activeProvider.GetVoicesAsync();
                    if (voices.Any())
                    {
                        _selectedVoice = voices.FirstOrDefault();
                        System.Diagnostics.Debug.WriteLine($"[VoiceManager] Fallback voice selected: {_selectedVoice?.DisplayName}");
                        SaveSettings();
                    }
                    else if (_activeProvider.ProviderType != VoiceProviderType.WindowsSAPI && !ShouldSuppressWindowsFallback(_activeProvider.ProviderType))
                    {
                        System.Diagnostics.Debug.WriteLine("[VoiceManager] Active provider has no voices, falling back to SAPI");
                        _activeProvider = _providers[VoiceProviderType.WindowsSAPI];
                        var sapiVoices = await _activeProvider.GetVoicesAsync();
                        _selectedVoice = sapiVoices.FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Error restoring saved voice: {ex.Message}");
            }
        }

        /// <summary>Speak text using the current voice (legacy - generates new TurnId)</summary>
        public async Task SpeakAsync(string text, CancellationToken ct = default)
        {
            // Create a simple utterance for backward compatibility
            var utterance = new AssistantUtterance(text, UtteranceSource.System);
            await SpeakAsync(utterance, ct);
        }

        /// <summary>Speak text using voice selected by VoiceSelectionService based on response type (legacy)</summary>
        public async Task SpeakAsync(string text, ResponseType responseType, CancellationToken ct = default)
        {
            var utterance = new AssistantUtterance(text, UtteranceSource.System, responseType: responseType);
            await SpeakAsync(utterance, ct);
        }
        
        /// <summary>
        /// Speak an AssistantUtterance - the canonical entry point for all TTS.
        /// Enforces single-speaker gate to prevent duplicate/overlapping speech.
        /// </summary>
        public async Task SpeakAsync(AssistantUtterance utterance, CancellationToken ct = default, [CallerMemberName] string? caller = null)
        {
            try { await _initTask; } catch { }

            if (utterance == null)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] SpeakAsync called with null utterance");
                return;
            }

            var speechSessionId = Interlocked.Increment(ref _speechSessionCounter);
            
            var turnId = utterance.TurnId;
            var text = utterance.SpeechText;
            var responseType = utterance.ResponseType;
            
            System.Diagnostics.Debug.WriteLine($"[VoiceManager] SpeakAsync called - TurnId: {turnId.ToString("N")[..8]}, Enabled: {_speechEnabled}, Suppressed: {utterance.SuppressSpeech}, Text length: {text?.Length ?? 0}");
            Core.AppLogger.Log($"Voice: Speak requested. Text: '{text?.Substring(0, Math.Min(text?.Length ?? 0, 30))}...'");

            CancellationTokenSource? myCts = null;
            CancellationTokenSource? previousCts = null;
            Guid previousTurnId = Guid.Empty;
            
            // === SINGLE-SPEAKER GATE ===
            lock (_speakerGateLock)
            {
                // Check if speech is suppressed
                if (utterance.SuppressSpeech)
                {
                    SpeechDedupeLogger.Log(turnId, text, false, SpeechRejectReason.Suppressed, utterance.Source, caller);
                    RaiseStatus("Speech suppressed");
                    return;
                }
                
                // Check if speech is disabled
                if (!_speechEnabled)
                {
                    SpeechDedupeLogger.Log(turnId, text, false, SpeechRejectReason.Disabled, utterance.Source, caller);
                    RaiseStatus("Speech disabled");
                    return;
                }
                
                // Check for empty text
                if (string.IsNullOrWhiteSpace(text))
                {
                    SpeechDedupeLogger.Log(turnId, text, false, SpeechRejectReason.EmptyText, utterance.Source, caller);
                    RaiseStatus("Speech skipped (empty text)");
                    return;
                }
                
                // Check for duplicate TurnId (same turn already speaking)
                if (_currentTurnId == turnId && _isSpeakingInternal)
                {
                    SpeechDedupeLogger.Log(turnId, text, false, SpeechRejectReason.DuplicateTurnId, utterance.Source, caller);
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] ❌ REJECTED: Duplicate TurnId {turnId.ToString("N")[..8]} - already speaking this turn");
                    Core.AppLogger.LogWarning($"Voice: Rejected duplicate turn {turnId.ToString("N")[..8]}");
                    RaiseStatus("Speech rejected (duplicate turn)");
                    return;
                }
                
                // Check for duplicate content (same text hash)
                var textHash = text.GetHashCode();
                if (_isSpeakingInternal && textHash == _lastSpokenHash)
                {
                    SpeechDedupeLogger.Log(turnId, text, false, SpeechRejectReason.AlreadySpeaking, utterance.Source, caller);
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] ❌ REJECTED: Same text already speaking");
                    Core.AppLogger.LogWarning("Voice: Rejected duplicate text");
                    RaiseStatus("Speech rejected (already speaking)");
                    return;
                }
                
                // If already speaking a different turn, cancel it first
                if (_isSpeakingInternal && _currentTurnId != turnId)
                {
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] Cancelling previous speech (TurnId: {_currentTurnId.ToString("N")[..8]}) for new turn (TurnId: {turnId.ToString("N")[..8]})");
                    previousCts = _speechCts;
                    previousTurnId = _currentTurnId;
                    try { previousCts?.Cancel(); } catch { }
                    SpeechDedupeLogger.LogComplete(_currentTurnId, cancelled: true);
                }
                
                // Accept this speech request
                _currentTurnId = turnId;
                _lastSpokenHash = textHash;
                _isSpeakingInternal = true; // mark immediately to avoid races with concurrent SpeakAsync calls
                _activeSpeechSessionId = speechSessionId;
                SpeechDedupeLogger.Log(turnId, text, true, SpeechRejectReason.None, utterance.Source, caller);

                try
                {
                    _playbackCts?.Cancel();
                }
                catch { }

                _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _speechCts = _playbackCts;
                myCts = _playbackCts;
            }

            // Ensure any previous audio output is stopped promptly.
            // (Token cancellation alone can leave MediaPlayer in an odd state.)
            if (previousCts != null && previousTurnId != Guid.Empty)
            {
                try { StopPlaybackOnly(); } catch { }
            }

            // Defensive: myCts should always be set when we get here, but if anything went wrong,
            // release the speaker gate so we don't get stuck "speaking" forever.
            if (myCts == null)
            {
                lock (_speakerGateLock)
                {
                    if (_activeSpeechSessionId == speechSessionId)
                    {
                        _isSpeakingInternal = false;
                        _playbackCts = null;
                        _speechCts = null;
                        RaiseStatus("");
                    }
                }
                OnSpeechEnded();
                return;
            }
            
            // Clean text for TTS - remove markdown formatting
            text = CleanTextForTTS(text);
            
            if (string.IsNullOrWhiteSpace(text))
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Skipping - text empty after cleaning");
                try { SpeechDedupeLogger.LogComplete(turnId); } catch { }
                lock (_speakerGateLock)
                {
                    if (_activeSpeechSessionId == speechSessionId)
                    {
                        _isSpeakingInternal = false;
                        _playbackCts = null;
                        _speechCts = null;
                        RaiseStatus("");
                    }
                }
                OnSpeechEnded();
                return;
            }

            // Use VoiceSelectionService to get the appropriate voice
            var directPreferenceVoiceId = GetDirectPreferenceVoiceId(responseType);
            if (!string.IsNullOrWhiteSpace(directPreferenceVoiceId))
            {
                try { await TryApplyPreferredVoiceAsync(directPreferenceVoiceId, myCts?.Token ?? ct).ConfigureAwait(false); } catch { }
            }

            var voiceSelection = VoiceSelectionService.SelectVoice(responseType);
            var voiceId = voiceSelection.Voice.VoiceId;

            if (!string.IsNullOrWhiteSpace(directPreferenceVoiceId) &&
                string.Equals(_selectedVoice?.Id, directPreferenceVoiceId, StringComparison.Ordinal))
            {
                voiceId = directPreferenceVoiceId;
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Using direct preferred voice override: {voiceId}");
            }
            
            System.Diagnostics.Debug.WriteLine($"[VoiceManager] Voice selected: {voiceSelection.Voice.DisplayName} ({voiceId}) - {voiceSelection.Reason}");

            if (!string.IsNullOrWhiteSpace(voiceSelection.Voice.Provider) &&
                Enum.TryParse<VoiceProviderType>(voiceSelection.Voice.Provider, out var providerType) &&
                providerType != _activeProvider.ProviderType)
            {
                var ok = await SetProviderInternalAsync(providerType, stopSpeech: false, myCts?.Token ?? ct);
                if (!ok && providerType != VoiceProviderType.WindowsSAPI && !ShouldSuppressWindowsFallback(providerType))
                    await SetProviderInternalAsync(VoiceProviderType.WindowsSAPI, stopSpeech: false, myCts?.Token ?? ct);
                else if (!ok)
                {
                    RaiseSpeechError($"Voice provider unavailable: {providerType}");
                    return;
                }
            }

            if (!string.IsNullOrEmpty(voiceId))
            {
                if (!string.Equals(_selectedVoice?.Id, voiceId, StringComparison.Ordinal))
                {
                    var selected = await SelectVoiceAsync(voiceId, myCts?.Token ?? ct);
                    if (!selected)
                    {
                        try
                        {
                            var voices = await _activeProvider.GetVoicesAsync(myCts?.Token ?? ct);
                            var fallback = voices.FirstOrDefault();
                            if (fallback != null)
                                _selectedVoice = fallback;
                        }
                        catch
                        {
                        }
                    }
                }
            }

            // IMPORTANT: always synthesize using the voice that actually ended up selected.
            // If the requested voiceId can't be selected for the active provider, we fall back
            // to the first available voice. In that case, using the original (invalid) voiceId
            // can cause synthesis to fail (especially for Windows SAPI) and result in silence.
            var effectiveVoiceId = (_selectedVoice?.Id ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(effectiveVoiceId))
                voiceId = effectiveVoiceId;

            // Stop any lingering playback/device output without cancelling this session.
            StopPlaybackOnly();

            try
            {
                _isSpeakingInternal = true;
                RaiseSpeechStarted();
                RaiseStatus("Synthesizing voice...");

                var options = new SynthesisOptions
                {
                    VoiceId = voiceId,
                    Rate = _rate,
                    Volume = _volume
                };

                var synthesisSw = System.Diagnostics.Stopwatch.StartNew();
                
                // Add timeout for synthesis (15 seconds)
                using var synthesisCts = CancellationTokenSource.CreateLinkedTokenSource(myCts.Token);
                synthesisCts.CancelAfter(15000);
                
                var result = await _activeProvider.SynthesizeAsync(text, options, synthesisCts.Token);
                synthesisSw.Stop();
                if (result == null)
                {
                    RaiseStatus("Synthesis failed, trying fallback...");
                    if (_activeProvider.ProviderType != VoiceProviderType.WindowsSAPI && !ShouldSuppressWindowsFallback(_activeProvider.ProviderType))
                    {
                        try
                        {
                            var fallbackOk = await SetProviderInternalAsync(VoiceProviderType.WindowsSAPI, stopSpeech: false, myCts.Token);
                            if (!fallbackOk)
                            {
                                RaiseSpeechError("Synthesis failed");
                                return;
                            }

                            var voices = await _activeProvider.GetVoicesAsync(myCts.Token);
                            var fallbackVoice = voices?.FirstOrDefault();
                            if (fallbackVoice != null)
                                _selectedVoice = fallbackVoice;

                            var fallbackOptions = new SynthesisOptions
                            {
                                VoiceId = _selectedVoice?.Id ?? "",
                                Rate = _rate,
                                Volume = _volume
                            };
                            result = await _activeProvider.SynthesizeAsync(text, fallbackOptions, myCts.Token);
                            if (result == null || !result.Success)
                            {
                                RaiseSpeechError(result?.ErrorMessage ?? "Synthesis failed");
                                return;
                            }
                        }
                        catch
                        {
                            RaiseSpeechError("Synthesis failed");
                            return;
                        }
                    }
                    else
                    {
                        RaiseSpeechError(result?.ErrorMessage ?? "Synthesis failed");
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Synthesis completed in {synthesisSw.ElapsedMilliseconds}ms (success={result.Success})");
                
                if (!result.Success)
                {
                    RaiseStatus("Synthesis failed, trying fallback...");
                    if (_activeProvider.ProviderType != VoiceProviderType.WindowsSAPI && !ShouldSuppressWindowsFallback(_activeProvider.ProviderType))
                    {
                        try
                        {
                            var fallbackOk = await SetProviderInternalAsync(VoiceProviderType.WindowsSAPI, stopSpeech: false, myCts.Token);
                            if (fallbackOk)
                            {
                                var voices = await _activeProvider.GetVoicesAsync(myCts.Token);
                                var fallbackVoice = voices?.FirstOrDefault();
                                if (fallbackVoice != null)
                                    _selectedVoice = fallbackVoice;

                                var fallbackOptions = new SynthesisOptions
                                {
                                    VoiceId = _selectedVoice?.Id ?? "",
                                    Rate = _rate,
                                    Volume = _volume
                                };
                                result = await _activeProvider.SynthesizeAsync(text, fallbackOptions, myCts.Token);
                                if (!result.Success)
                                {
                                    RaiseSpeechError(result.ErrorMessage ?? "Synthesis failed");
                                    return;
                                }
                            }
                            else
                            {
                                RaiseSpeechError(result.ErrorMessage ?? "Synthesis failed");
                                return;
                            }
                        }
                        catch
                        {
                            RaiseSpeechError(result.ErrorMessage ?? "Synthesis failed");
                            return;
                        }
                    }
                    else
                    {
                        RaiseSpeechError(result.ErrorMessage ?? "Synthesis failed");
                        return;
                    }
                }

                var audioFile = result.AudioFilePath;
                if (string.IsNullOrWhiteSpace(audioFile))
                {
                    SpeechDedupeLogger.LogComplete(turnId);
                    return;
                }

                try
                {
                    var activeReplyRequestId = CompanionTransportService.Instance.GetActiveReplyRequestId();
                    await CompanionTransportService.Instance.PublishReplyAudioAsync(utterance, audioFile, activeReplyRequestId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] Failed to publish companion reply audio: {ex.Message}");
                }

                // Play audio
                RaiseStatus("Playing audio...");
                var playbackSw = System.Diagnostics.Stopwatch.StartNew();
                
                var playbackTimeoutMs = GetPlaybackTimeoutMs(audioFile, text);
                
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Dynamic playback timeout: {playbackTimeoutMs}ms for {text.Length} chars");
                
                using var dynamicCts = CancellationTokenSource.CreateLinkedTokenSource(myCts.Token);
                dynamicCts.CancelAfter(playbackTimeoutMs);
                
                await PlayAudioAsync(audioFile, dynamicCts.Token);
                playbackSw.Stop();
                RaiseStatus("Playback finished");
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Playback completed in {playbackSw.ElapsedMilliseconds}ms");
                SpeechDedupeLogger.LogComplete(turnId);
            }
            catch (OperationCanceledException)
            {
                // Cancelled - normal
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Speech cancelled");
                RaiseStatus("Speech cancelled");
                Core.AppLogger.Log("Voice: Speech cancelled");
                SpeechDedupeLogger.LogComplete(turnId, cancelled: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Speech error: {ex.Message}");
                RaiseStatus($"Error: {ex.Message}");
                RaiseSpeechError(ex.Message);
                Core.AppLogger.LogError($"Voice error: {ex.Message}");
            }
            finally
            {
                var isOwner = false;
                lock (_speakerGateLock)
                {
                    isOwner = _activeSpeechSessionId == speechSessionId;
                    if (isOwner)
                    {
                        _isSpeakingInternal = false;
                        _playbackCts = null;
                        _speechCts = null;
                        RaiseStatus(""); // Clear status
                    }
                }

                if (isOwner)
                {
                    OnSpeechEnded();
                }
            }
        }
        
        /// <summary>
        /// Clean text for TTS - removes markdown formatting, limits length, and normalizes for speech
        /// </summary>
        private string CleanTextForTTS(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = AtlasAI.AI.ResponsePostProcessor.CleanAssistantText(text);
            
            // Remove markdown bold/italic (asterisks)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "$1"); // ***bold italic***
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1"); // **bold**
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.+?)\*", "$1"); // *italic*
            text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.+?)__", "$1"); // __bold__
            text = System.Text.RegularExpressions.Regex.Replace(text, @"_(.+?)_", "$1"); // _italic_
            
            // Remove markdown headers
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            
            // Remove markdown links [text](url) -> text
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            
            // Remove markdown images ![alt](url)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"!\[([^\]]*)\]\([^\)]+\)", "");
            
            // Keep code blocks but remove fences
            text = System.Text.RegularExpressions.Regex.Replace(text, @"```", "");
            
            // Remove inline code `code`
            text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "$1");
            
            // Remove bullet points and list markers
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[\s]*[-*+]\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[\s]*[•·]\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[\s]*\d+\.\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);

            // Replace decorative separators and inline markdown-style dividers with speech-friendly punctuation.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=\S)\s[-–—]{1,3}\s(?=\S)", ", ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^[\s]*[-=_]{3,}[\s]*$", "");
            text = text.Replace("*", string.Empty);
            
            // Remove blockquotes
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^>\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            
            // Remove horizontal rules
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[-*_]{3,}$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            
            // Remove emoji shortcodes :emoji:
            text = System.Text.RegularExpressions.Regex.Replace(text, @":[a-zA-Z0-9_+-]+:", "");
            
            // Clean up multiple newlines
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

            // Turn leftover line breaks into natural pauses so TTS does not read list punctuation awkwardly.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\r?\n\s*", ". ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?:\.\s*){2,}", ". ");
            
            // Clean up multiple spaces
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]{2,}", " ");
            
            // Trim
            text = text.Trim();
            
            // Limit length for TTS - very long messages take forever to synthesize
            // Keep first ~2500 chars (about 3-4 minutes of speech max)
            const int maxLength = 2500;
            if (text.Length > maxLength)
            {
                // Try to cut at a sentence boundary
                var cutPoint = text.LastIndexOf('.', maxLength);
                if (cutPoint < maxLength / 2) cutPoint = text.LastIndexOf(' ', maxLength);
                if (cutPoint < maxLength / 2) cutPoint = maxLength;
                
                text = text.Substring(0, cutPoint).Trim();
                // Don't add "continued in text" - just truncate cleanly
            }
            
            System.Diagnostics.Debug.WriteLine($"[VoiceManager] Cleaned text for TTS: {text.Length} chars");
            return text;
        }

        /// <summary>Stop current speech</summary>
        public void Stop()
        {
            CancellationTokenSource? toCancelPlayback = null;
            CancellationTokenSource? toCancelSpeech = null;

            lock (_speakerGateLock)
            {
                if (_isSpeakingInternal && _currentTurnId != Guid.Empty)
                {
                    SpeechDedupeLogger.LogComplete(_currentTurnId, cancelled: true);
                }
                _currentTurnId = Guid.Empty;
                _lastSpokenHash = 0;
                _isSpeakingInternal = false;
                _activeSpeechSessionId = 0;

                toCancelPlayback = _playbackCts;
                toCancelSpeech = _speechCts;
            }

            try { toCancelPlayback?.Cancel(); } catch { }
            try { toCancelSpeech?.Cancel(); } catch { }

            _activeProvider.CancelCurrentSpeech();
            try { StopNaudioPlayback(); } catch { }
            if (_mediaPlayer.Dispatcher.CheckAccess())
                _mediaPlayer.Stop();
            else
                _mediaPlayer.Dispatcher.Invoke(() => _mediaPlayer.Stop());
            OnSpeechEnded();
        }

        /// <summary>Clear the voice cache</summary>
        public void ClearCache()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_cacheDir, "*.mp3"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        private async Task PlayAudioAsync(string audioFile, CancellationToken ct)
        {
            Exception? naudioError = null;
            try
            {
                await PlayAudioWithNaudioAsync(audioFile, ct);
                return;
            }
            catch (Exception ex)
            {
                naudioError = ex;
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] NAudio playback failed: {ex.Message}");
                RaiseStatus("Primary audio output failed, trying fallback...");
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var openedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            void OnOpened(object? s, EventArgs e) => openedTcs.TrySetResult(true);
            void OnEnded(object? s, EventArgs e) => tcs.TrySetResult(true);
            void OnFailed(object? s, ExceptionEventArgs e) 
            {
                openedTcs.TrySetException(e.ErrorException);
                tcs.TrySetException(e.ErrorException);
            }

            _mediaPlayer.MediaOpened += OnOpened;
            _mediaPlayer.MediaEnded += OnEnded;
            _mediaPlayer.MediaFailed += OnFailed;

            try
            {
                // Ensure volume is not muted
                if (_volume < 0.05) _volume = 1.0;

                RaiseStatus("Opening audio device...");
                await _mediaPlayer.Dispatcher.InvokeAsync(() =>
                {
                    _mediaPlayer.Open(new Uri(audioFile));
                    _mediaPlayer.Volume = _volume;
                });
                
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Playing audio file: {audioFile} (Volume: {_volume})");

                // Wait for media to be fully loaded before playing (fixes audio cutout at start)
                using var openReg = ct.Register(() => openedTcs.TrySetCanceled());
                try
                {
                    // Wait up to 3 seconds for media to open (increased from 2000ms)
                    var openTask = openedTcs.Task;
                    var timeoutTask = Task.Delay(3000, ct);
                    var completedTask = await Task.WhenAny(openTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        System.Diagnostics.Debug.WriteLine("[VoiceManager] Media open timed out - attempting force play");
                        RaiseStatus("Media open timed out - forcing play...");
                        // Don't fail, try to play anyway
                        
                        // Check if file exists - if not, fail fast
                        if (!File.Exists(audioFile))
                        {
                            throw new FileNotFoundException("Audio file not found", audioFile);
                        }
                    }
                    
                    // If opened successfully, wait a bit more for the audio device to be ready
                    if (openTask.IsCompletedSuccessfully)
                    {
                        System.Diagnostics.Debug.WriteLine("[VoiceManager] Media opened successfully, waiting for audio device...");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] Error waiting for media open: {ex.Message}");
                }
                
                // Increased buffer to ensure audio device is ready and no audio is cut off
                // This is critical for Bluetooth/USB audio devices which have higher latency
                await Task.Delay(200, ct);
                
                System.Diagnostics.Debug.WriteLine("[VoiceManager] Starting playback...");
                RaiseStatus("Speaking...");
                await _mediaPlayer.Dispatcher.InvokeAsync(() => _mediaPlayer.Play());

                // Wait for playback to finish or token to cancel
                // The token now comes from dynamicCts which has the calculated timeout
                var playbackTask = tcs.Task;
                
                // We use the token directly to cancel the task wait
                using var reg = ct.Register(() => tcs.TrySetCanceled());
                
                await playbackTask;
            }
            catch (Exception ex) when (audioFile.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                // MediaPlayer failed, try SoundPlayer fallback for WAV files
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] MediaPlayer failed ({ex.Message}), trying SoundPlayer fallback...");
                RaiseStatus("MediaPlayer failed, using fallback...");
                
                try
                {
                    await Task.Run(() =>
                    {
                        using var player = new System.Media.SoundPlayer(audioFile);
                        player.Load();
                        player.PlaySync();
                    }, ct);
                    
                    System.Diagnostics.Debug.WriteLine("[VoiceManager] SoundPlayer fallback success");
                    // Return successfully
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] SoundPlayer fallback failed: {ex2.Message}");
                    throw; // Rethrow original exception (or the new one?) - rethrow original is probably better context, or aggregate.
                    // Actually let's throw the original one as it's the primary failure.
                }
            }
            catch (Exception ex) when (naudioError != null)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceManager] Fallback playback failed after NAudio error '{naudioError.Message}': {ex.Message}");
                throw new InvalidOperationException($"Audio playback failed: {naudioError.Message}; fallback failed: {ex.Message}", ex);
            }
            finally
            {
                _ = tcs.Task.Exception;
                _ = openedTcs.Task.Exception;
                _mediaPlayer.MediaOpened -= OnOpened;
                _mediaPlayer.MediaEnded -= OnEnded;
                _mediaPlayer.MediaFailed -= OnFailed;
            }
        }

        private async Task PlayAudioWithNaudioAsync(string audioFile, CancellationToken ct)
        {
            if (!File.Exists(audioFile))
                throw new FileNotFoundException("Audio file not found", audioFile);

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
                    reader = new AudioFileReader(audioFile)
                    {
                        Volume = (float)Math.Clamp(_volume, 0.0, 1.0)
                    };

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

                    RaiseStatus("Speaking...");
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] NAudio playback start via {backend.Name}: {audioFile}");

                    // Give the audio device a moment to wake from idle before playing.
                    // Bluetooth and USB devices need 100-300ms to transition from idle
                    // to active, otherwise the start of the utterance gets clipped.
                    await Task.Delay(150, ct).ConfigureAwait(false);

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
                    System.Diagnostics.Debug.WriteLine($"[VoiceManager] NAudio backend '{backend.Name}' failed: {ex.Message}");
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

        private void OnSpeechEnded()
        {
            RaiseSpeechEnded();
        }

        private static int GetPlaybackTimeoutMs(string audioFile, string text)
        {
            try
            {
                if (File.Exists(audioFile))
                {
                    using var probeReader = new AudioFileReader(audioFile);
                    var audioSeconds = Math.Ceiling(probeReader.TotalTime.TotalSeconds);
                    if (audioSeconds > 0)
                        return (int)Math.Clamp((audioSeconds + 12) * 1000, 15000, 120000);
                }
            }
            catch
            {
            }

            var estimatedSeconds = Math.Max(15, Math.Min(90, Math.Max(1, text.Length / 9) + 12));
            return estimatedSeconds * 1000;
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

        private string GetCacheKey(string text, string voiceId, double rate)
        {
            var input = $"{text}|{voiceId}|{rate:F1}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash)[..16];
        }

        private async Task LoadSettingsAsync()
        {
            var providerLoadedFromSettings = false;

            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = await File.ReadAllTextAsync(_settingsPath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("speechEnabled", out var se))
                        _speechEnabled = se.GetBoolean();
                    if (root.TryGetProperty("volume", out var vol))
                        _volume = vol.GetDouble();
                    if (root.TryGetProperty("rate", out var rate))
                        _rate = rate.GetDouble();
                    if (root.TryGetProperty("provider", out var prov) &&
                        Enum.TryParse<VoiceProviderType>(prov.GetString(), out var provType))
                    {
                        if (_providers.TryGetValue(provType, out var provider))
                        {
                            _activeProvider = provider;
                            providerLoadedFromSettings = true;
                        }
                    }
                    if (root.TryGetProperty("voiceId", out var vid))
                    {
                        var voiceId = vid.GetString();
                        if (!string.IsNullOrEmpty(voiceId))
                        {
                            // Don't fire-and-forget here. Let RestoreSavedVoiceAsync handle it properly
                            // _ = SelectVoiceAsync(voiceId);
                        }
                    }
                }
            }
            catch { }

            if (!providerLoadedFromSettings)
            {
                // Default to EdgeTTS when no saved provider — avoids burning ElevenLabs credits
                _activeProvider = _providers[VoiceProviderType.EdgeTTS];
            }

            // Disabled default selection here to avoid race conditions with RestoreSavedVoiceAsync
            /*
            // Ensure we have a selected voice - prefer Atlas for ElevenLabs
            if (_selectedVoice == null)
            {
                _ = Task.Run(async () =>
                {
                    var voices = await _activeProvider.GetVoicesAsync();
                    if (_activeProvider.ProviderType == VoiceProviderType.ElevenLabs)
                    {
                        _selectedVoice = voices.FirstOrDefault(v => v.Id == VoiceProfile.DefaultAtlasVoiceId)
                                      ?? voices.FirstOrDefault(v => v.DisplayName?.Contains("Atlas", StringComparison.OrdinalIgnoreCase) == true)
                                      ?? voices.FirstOrDefault(v => v.DisplayName?.Contains("Daniel", StringComparison.OrdinalIgnoreCase) == true)
                                      ?? voices.FirstOrDefault();
                    }
                    else
                    {
                        _selectedVoice = voices.FirstOrDefault();
                    }
                });
            }
            */

            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await _activeProvider.IsAvailableAsync())
                    {
                        if (await _providers[VoiceProviderType.EdgeTTS].IsAvailableAsync())
                            _activeProvider = _providers[VoiceProviderType.EdgeTTS];
                        else if (await _providers[VoiceProviderType.OpenAI].IsAvailableAsync())
                            _activeProvider = _providers[VoiceProviderType.OpenAI];
                        else
                            _activeProvider = _providers[VoiceProviderType.WindowsSAPI];

                        SaveSettings();
                    }
                }
                catch
                {
                    _activeProvider = _providers[VoiceProviderType.WindowsSAPI];
                    SaveSettings();
                }
            });
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new
                {
                    speechEnabled = _speechEnabled,
                    volume = _volume,
                    rate = _rate,
                    provider = _activeProvider.ProviderType.ToString(),
                    voiceId = _selectedVoice?.Id
                };

                var dir = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }

        public void Dispose()
        {
            VoicePreferences.PreferencesChanged -= OnVoicePreferencesChanged;
            Stop();
            try { StopNaudioPlayback(); } catch { }
            try
            {
                if (_mediaPlayer.Dispatcher.CheckAccess())
                    _mediaPlayer.Close();
                else
                    _mediaPlayer.Dispatcher.Invoke(() => _mediaPlayer.Close());
            }
            catch
            {
            }
        }
    }
}
