using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AtlasAI.Core;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Voice conversation loop states.
    /// </summary>
    public enum VoiceSessionState
    {
        /// <summary>Passively listening for wake word only</summary>
        Idle,
        
        /// <summary>Listening for wake word (same as Idle but explicit)</summary>
        WakeListening,
        
        /// <summary>Active conversation - accepting speech without wake word</summary>
        ActiveConversation,
        
        /// <summary>Processing user command</summary>
        Processing,
        
        /// <summary>Atlas is speaking response</summary>
        Speaking,
        
        /// <summary>Post-speech cooldown before returning to WakeListening</summary>
        Cooldown
    }

    /// <summary>
    /// Single source of truth for voice interaction.
    /// Owns the microphone pipeline and coordinates between wake word detection,
    /// speech recognition, and TTS.
    /// 
    /// Key behaviors:
    /// - Wake word NEVER opens Chat window (per hard constraint)
    /// - After Atlas speaks, enters ActiveConversation for configurable duration
    /// - User can speak follow-ups without wake word during ActiveConversation
    /// - Single microphone pipeline prevents conflicts
    /// </summary>
    public class VoiceInteractionCoordinator
    {
        private static VoiceInteractionCoordinator? _instance;
        private static readonly object _lock = new();

        public static VoiceInteractionCoordinator Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VoiceInteractionCoordinator();
                    }
                }
                return _instance;
            }
        }

        // State
        private VoiceSessionState _currentState = VoiceSessionState.Idle;
        private DateTime _lastStateChange = DateTime.MinValue;
        private DateTime _lastSpeechEnd = DateTime.MinValue;
        private CancellationTokenSource? _postSpeechListenCts;
        private bool _isInitialized = false;

        // Configuration (from UserPreferences)
        private double _postSpeechListenSeconds = 4.0;
        private int _wakeWordCooldownMs = 1500;
        private bool _wakeWordEnabled = true;

        // Events
        public event EventHandler<VoiceSessionState>? StateChanged;
        public event EventHandler? WakeWordActivated;
        public event EventHandler<string>? CommandReceived;
        public event EventHandler? ListeningStarted;
        public event EventHandler? ListeningStopped;
        public event EventHandler? PostSpeechListeningStarted;
        public event EventHandler? PostSpeechListeningExpired;

        // Properties
        public VoiceSessionState CurrentState => _currentState;
        public bool IsListening => _currentState == VoiceSessionState.ActiveConversation || 
                                   _currentState == VoiceSessionState.WakeListening;
        public bool IsInActiveConversation => _currentState == VoiceSessionState.ActiveConversation;
        public bool WakeWordEnabled
        {
            get => _wakeWordEnabled;
            set
            {
                _wakeWordEnabled = value;
                LogEvent("WakeWordEnabledChanged", new { enabled = value });
            }
        }

        public double PostSpeechListenSeconds
        {
            get => _postSpeechListenSeconds;
            set => _postSpeechListenSeconds = Math.Clamp(value, 2.0, 10.0);
        }

        public int WakeWordCooldownMs
        {
            get => _wakeWordCooldownMs;
            set => _wakeWordCooldownMs = Math.Clamp(value, 500, 5000);
        }

        private VoiceInteractionCoordinator()
        {
            LoadSettings();
        }

        /// <summary>
        /// Initialize the coordinator and wire up to WakeWordCoordinator.
        /// Call once during app startup.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
            {
                Debug.WriteLine("[VoiceInteraction] Already initialized");
                return;
            }

            Debug.WriteLine("[VoiceInteraction] ═══════════════════════════════════════");
            Debug.WriteLine("[VoiceInteraction] Initializing Voice Interaction Coordinator");
            Debug.WriteLine("[VoiceInteraction] Single microphone pipeline owner");
            Debug.WriteLine("[VoiceInteraction] ═══════════════════════════════════════");

            // Subscribe to WakeWordCoordinator
            WakeWordCoordinator.Instance.WakeWordActivated += OnWakeWordActivated;
            WakeWordCoordinator.Instance.WakeWordActivatedWithDetails += OnWakeWordActivatedWithDetails;

            // Subscribe to WakeWordFlowController for state sync
            WakeWordFlowController.Instance.StateChanged += OnFlowStateChanged;

            _isInitialized = true;
            TransitionTo(VoiceSessionState.WakeListening, "initialized");

            LogEvent("Initialized", new { postSpeechSeconds = _postSpeechListenSeconds, cooldownMs = _wakeWordCooldownMs });
            Debug.WriteLine("[VoiceInteraction] ✅ Initialized");
        }

        /// <summary>
        /// Handle wake word detection.
        /// Transitions to ActiveConversation, does NOT open Chat window.
        /// </summary>
        private void OnWakeWordActivated(object? sender, EventArgs e)
        {
            Debug.WriteLine("[VoiceInteraction] Wake word activated (no details)");
            HandleWakeWord("Atlas", 0.8);
        }

        private void OnWakeWordActivatedWithDetails(object? sender, WakeWordEventArgs e)
        {
            Debug.WriteLine($"[VoiceInteraction] Wake word activated: '{e.Text}' ({e.Confidence:P0})");
            HandleWakeWord(e.Text, e.Confidence);
        }

        private void HandleWakeWord(string text, double confidence)
        {
            // Check if wake word is enabled
            if (!_wakeWordEnabled)
            {
                LogEvent("WakeWordRejected", new { reason = "disabled", text });
                Debug.WriteLine("[VoiceInteraction] Wake word rejected - disabled");
                return;
            }

            // Check cooldown
            if (_currentState == VoiceSessionState.Cooldown)
            {
                LogEvent("WakeWordRejected", new { reason = "cooldown", text });
                Debug.WriteLine("[VoiceInteraction] Wake word rejected - in cooldown");
                return;
            }

            // Accept wake word
            LogEvent("WakeWordAccepted", new { text, confidence });
            Debug.WriteLine($"[VoiceInteraction] Wake word ACCEPTED: '{text}'");

            // Transition to ActiveConversation
            TransitionTo(VoiceSessionState.ActiveConversation, "wake word detected");

            // Notify listeners (but NOT to open Chat window)
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                WakeWordActivated?.Invoke(this, EventArgs.Empty);
                ListeningStarted?.Invoke(this, EventArgs.Empty);
            });
        }

        /// <summary>
        /// Handle user command received (from speech recognition).
        /// </summary>
        public void OnCommandReceived(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                Debug.WriteLine("[VoiceInteraction] Empty command ignored");
                return;
            }

            Debug.WriteLine($"[VoiceInteraction] Command received: '{command}'");
            LogEvent("CommandReceived", new { command, state = _currentState.ToString() });

            // Cancel any post-speech listening timer
            _postSpeechListenCts?.Cancel();

            // Transition to Processing
            TransitionTo(VoiceSessionState.Processing, "command received");

            // Notify listeners
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                CommandReceived?.Invoke(this, command);
            });
        }

        /// <summary>
        /// Notify that Atlas has started speaking.
        /// </summary>
        public void OnSpeakingStarted()
        {
            Debug.WriteLine("[VoiceInteraction] Speaking started");
            LogEvent("SpeakingStarted", new { });
            TransitionTo(VoiceSessionState.Speaking, "TTS started");
        }

        /// <summary>
        /// Notify that Atlas has finished speaking.
        /// Starts post-speech listening window.
        /// </summary>
        public void OnSpeakingEnded()
        {
            _lastSpeechEnd = DateTime.Now;
            Debug.WriteLine("[VoiceInteraction] Speaking ended");
            LogEvent("SpeakingEnded", new { });

            // Start post-speech listening window
            StartPostSpeechListening();
        }

        /// <summary>
        /// Start the post-speech listening window.
        /// User can speak follow-ups without wake word during this time.
        /// Uses FollowUpListeningDuration from UserPreferences.
        /// </summary>
        private void StartPostSpeechListening()
        {
            // Cancel any existing timer
            _postSpeechListenCts?.Cancel();
            _postSpeechListenCts = new CancellationTokenSource();

            // Reload settings to get latest FollowUpListeningDuration
            LoadSettings();
            
            TransitionTo(VoiceSessionState.ActiveConversation, "post-speech listening");
            Debug.WriteLine($"[VoiceInteraction] ═══════════════════════════════════════");
            Debug.WriteLine($"[VoiceInteraction] POST-SPEECH LISTENING WINDOW STARTED");
            Debug.WriteLine($"[VoiceInteraction] Duration: {_postSpeechListenSeconds}s (from UserPreferences.FollowUpListeningDuration)");
            Debug.WriteLine($"[VoiceInteraction] User can speak follow-ups without wake word");
            Debug.WriteLine($"[VoiceInteraction] ═══════════════════════════════════════");
            LogEvent("PostSpeechListeningStarted", new { 
                durationSeconds = _postSpeechListenSeconds,
                source = "UserPreferences.FollowUpListeningDuration",
                wakeWordEnabled = _wakeWordEnabled
            });

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                PostSpeechListeningStarted?.Invoke(this, EventArgs.Empty);
            });

            // Start timer
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_postSpeechListenSeconds), _postSpeechListenCts.Token);

                    // Timer expired - return to WakeListening
                    Debug.WriteLine($"[VoiceInteraction] ═══════════════════════════════════════");
                    Debug.WriteLine($"[VoiceInteraction] POST-SPEECH LISTENING WINDOW EXPIRED");
                    Debug.WriteLine($"[VoiceInteraction] No follow-up detected within {_postSpeechListenSeconds}s");
                    Debug.WriteLine($"[VoiceInteraction] Returning to wake word listening mode");
                    Debug.WriteLine($"[VoiceInteraction] ═══════════════════════════════════════");
                    LogEvent("PostSpeechListeningExpired", new { 
                        durationSeconds = _postSpeechListenSeconds,
                        reason = "timeout_no_followup"
                    });

                    TransitionTo(VoiceSessionState.WakeListening, "post-speech timeout");

                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        PostSpeechListeningExpired?.Invoke(this, EventArgs.Empty);
                        ListeningStopped?.Invoke(this, EventArgs.Empty);
                    });
                }
                catch (TaskCanceledException)
                {
                    // Cancelled - new command received
                    Debug.WriteLine("[VoiceInteraction] Post-speech listening cancelled (new command received - good!)");
                    LogEvent("PostSpeechListeningCancelled", new { reason = "new_command_received" });
                }
            });
        }

        /// <summary>
        /// Check if we should accept voice input (wake word detected or in active conversation).
        /// </summary>
        public bool ShouldAcceptVoiceInput()
        {
            return _currentState == VoiceSessionState.ActiveConversation;
        }

        /// <summary>
        /// Force return to WakeListening state.
        /// </summary>
        public void Reset()
        {
            Debug.WriteLine("[VoiceInteraction] Reset");
            _postSpeechListenCts?.Cancel();
            TransitionTo(VoiceSessionState.WakeListening, "reset");
            LogEvent("Reset", new { });
        }

        /// <summary>
        /// Cancel current operation and return to WakeListening.
        /// </summary>
        public void Cancel()
        {
            Debug.WriteLine("[VoiceInteraction] Cancelled");
            _postSpeechListenCts?.Cancel();
            TransitionTo(VoiceSessionState.WakeListening, "cancelled");
            LogEvent("Cancelled", new { });

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ListeningStopped?.Invoke(this, EventArgs.Empty);
            });
        }

        private void TransitionTo(VoiceSessionState newState, string reason)
        {
            if (_currentState == newState) return;

            var oldState = _currentState;
            _currentState = newState;
            _lastStateChange = DateTime.Now;

            Debug.WriteLine($"[VoiceInteraction] State: {oldState} → {newState} ({reason})");
            LogEvent("StateTransition", new { from = oldState.ToString(), to = newState.ToString(), reason });

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                StateChanged?.Invoke(this, newState);
            });
        }

        private void OnFlowStateChanged(object? sender, WakeWordFlowState e)
        {
            // Sync with WakeWordFlowController state
            Debug.WriteLine($"[VoiceInteraction] FlowController state: {e}");
        }

        /// <summary>
        /// Get status text for UI display.
        /// </summary>
        public string GetStatusText()
        {
            return _currentState switch
            {
                VoiceSessionState.Idle => "Idle",
                VoiceSessionState.WakeListening => "Listening",
                VoiceSessionState.ActiveConversation => "Listening",
                VoiceSessionState.Processing => "Processing",
                VoiceSessionState.Speaking => "Speaking",
                VoiceSessionState.Cooldown => "One moment",
                _ => ""
            };
        }

        /// <summary>
        /// Get remaining time in post-speech listening window.
        /// </summary>
        public TimeSpan? GetPostSpeechTimeRemaining()
        {
            if (_currentState != VoiceSessionState.ActiveConversation)
                return null;

            var elapsed = DateTime.Now - _lastSpeechEnd;
            var remaining = TimeSpan.FromSeconds(_postSpeechListenSeconds) - elapsed;
            return remaining > TimeSpan.Zero ? remaining : null;
        }

        private void LoadSettings()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                _postSpeechListenSeconds = prefs.FollowUpListeningDuration;
                _wakeWordEnabled = prefs.EnableWakeWord;
                Debug.WriteLine($"[VoiceInteraction] Settings loaded: postSpeech={_postSpeechListenSeconds}s, wakeWord={_wakeWordEnabled}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceInteraction] Settings load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Shutdown and cleanup.
        /// </summary>
        public void Shutdown()
        {
            if (_isInitialized)
            {
                WakeWordCoordinator.Instance.WakeWordActivated -= OnWakeWordActivated;
                WakeWordCoordinator.Instance.WakeWordActivatedWithDetails -= OnWakeWordActivatedWithDetails;
                WakeWordFlowController.Instance.StateChanged -= OnFlowStateChanged;
                _postSpeechListenCts?.Cancel();
                _isInitialized = false;
                Debug.WriteLine("[VoiceInteraction] Shutdown complete");
            }
        }

        #region JSONL Logging

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "voice_interaction.jsonl");

        private void LogEvent(string eventType, object data)
        {
            try
            {
                var logDir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                var entry = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    @event = eventType,
                    state = _currentState.ToString(),
                    data
                };

                var json = JsonSerializer.Serialize(entry);
                File.AppendAllText(LogPath, json + Environment.NewLine);
            }
            catch
            {
                // Fail silently - logging should never crash the app
            }
        }

        #endregion
    }
}
