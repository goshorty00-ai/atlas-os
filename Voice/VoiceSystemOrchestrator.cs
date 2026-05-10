using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.AI;
using AtlasAI.Core;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Source of the listening trigger - used to unify wake word and push-to-talk paths
    /// </summary>
    public enum ListeningSource
    {
        /// <summary>Triggered by wake word detection ("Atlas")</summary>
        WakeWord,
        
        /// <summary>Triggered by push-to-talk button or hotkey</summary>
        PushToTalk,
        
        /// <summary>Triggered by follow-up listening after TTS completes</summary>
        FollowUp
    }

    /// <summary>
    /// Orchestrates the complete voice system for Atlas-AI.
    /// Coordinates VoiceInputManager, WakeWordService, VoiceStateManager, and routing.
    /// Handles the complete flow from wake word detection to command execution.
    /// </summary>
    public class VoiceSystemOrchestrator : IDisposable
    {
        private static readonly string _voiceDiagPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "voice_diag.log");

        private static void VDiag(string msg)
        {
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}";
                var dir = System.IO.Path.GetDirectoryName(_voiceDiagPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(_voiceDiagPath, line);
            }
            catch { }
        }

        private static VoiceSystemOrchestrator? _instance;
        private static readonly object _lock = new object();

        public static VoiceSystemOrchestrator Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VoiceSystemOrchestrator();
                    }
                }
                return _instance;
            }
        }

        // === Services ===
        private readonly VoiceInputManager _inputManager;
        private readonly WakeWordService _wakeWordService;
        private readonly VoiceStateManager _stateManager;
        private VoiceManager? _voiceManager;
        private WhisperSpeechRecognition? _whisperRecognizer;
        private WindowsDictationRecognizer? _dictationRecognizer;

        // === State ===
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private CancellationTokenSource? _activeListeningCts;
        private CancellationTokenSource? _followUpListeningCts;
        private CancellationTokenSource? _currentActionCts;
        private readonly object _actionLock = new();
        private DateTime _activeListeningStart;
        private System.Threading.Timer? _followUpTimer;
        private ListeningSource _currentListeningSource;
        private bool _continuousPushToTalk;
        private const int ActiveListeningTimeoutMs = 8000;

        // Monotonic session id to prevent stale async callbacks (watchdogs) from affecting new sessions.
        private int _listeningSessionId = 0;

        private static readonly TimeSpan MicResumeDelayAfterSpeech = TimeSpan.FromMilliseconds(1400);
        private static readonly TimeSpan MinimumListenWindow = TimeSpan.FromMilliseconds(1200);

        // Echo suppression: ignore transcripts similar to the last AI spoken response.
        private string _lastAiSpokenText = "";
        private DateTime _lastAiSpokenAtUtc = DateTime.MinValue;
        private DateTime _lastAiSpeechEndedAtUtc = DateTime.MinValue;

        // Tracks whether the current response is part of a voice-initiated conversation
        // (wake word, push-to-talk, or follow-up). Used to decide whether to open follow-up mic.
        private bool _voiceConversationActive = false;

        // Tracks whether we've intentionally paused wake word during speech output.
        private bool _wakeWordPausedForSpeech = false;

        private static readonly TimeSpan PostTtsWakeWordCooldown = TimeSpan.FromMilliseconds(3200);
        private DateTime _postTtsCooldownUntilUtc = DateTime.MinValue;

        private void PauseVoiceInputForSpeech()
        {
            try
            {
                // Stop any active recognition so we don't transcribe TTS.
                try { _dictationRecognizer?.Stop(); } catch { }
                try { _whisperRecognizer?.CancelRecording(); } catch { }

                // Disable wake word detection while speaking (prevents mid-response triggers).
                try
                {
                    if (_wakeWordService.IsListening || _wakeWordService.ContinuousListeningEnabled)
                    {
                        _wakeWordService.ContinuousListeningEnabled = false;
                        _wakeWordService.Stop(WakeStopReason.ExternalHandlerDefer);
                        _wakeWordPausedForSpeech = true;
                    }
                }
                catch { }

                Core.AppLogger.Log("[Voice] Listening paused");
            }
            catch
            {
            }
        }

        private void ResumeWakeWordAfterSpeechIfNeeded()
        {
            if (_isDisposed) return;
            if (!_wakeWordPausedForSpeech) return;

            _wakeWordPausedForSpeech = false;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(MicResumeDelayAfterSpeech).ConfigureAwait(false);
                    if (_isDisposed) return;

                    // If a voice conversation is active, we will open the mic via follow-up listening instead.
                    if (_voiceConversationActive) return;

                    var prefs = PreferencesStore.Instance.Current;
                    if (!prefs.EnableWakeWord) return;
                    if (_wakeWordService.IsListening) return;

                    // Resume wake word detection (microphone) promptly.
                    _wakeWordService.ContinuousListeningEnabled = true;
                    var started = await _wakeWordService.StartAsync().ConfigureAwait(false);
                    if (!started)
                    {
                        // Fall back to the existing restart logic with retries/backoff.
                        RestartWakeWordService();
                    }
                }
                catch
                {
                }
            });
        }

        private bool ShouldIgnoreAsEcho(string? transcript)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(transcript)) return false;
                if (string.IsNullOrWhiteSpace(_lastAiSpokenText)) return false;

                var now = DateTime.UtcNow;
                var anchor = _lastAiSpeechEndedAtUtc != DateTime.MinValue ? _lastAiSpeechEndedAtUtc : _lastAiSpokenAtUtc;
                if (anchor == DateTime.MinValue) return false;

                if (now - anchor > TimeSpan.FromSeconds(10)) return false;

                var a = NormalizeForSimilarity(transcript);
                var b = NormalizeForSimilarity(_lastAiSpokenText);
                if (a.Length < 8 || b.Length < 8) return false;

                var sim = DiceCoefficient(a, b);
                if (sim >= 0.80)
                {
                    Core.AppLogger.Log("[Voice] Echo ignored");
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeForSimilarity(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var t = text.Trim().ToLowerInvariant();

            var chars = new char[t.Length];
            int j = 0;
            for (int i = 0; i < t.Length; i++)
            {
                var c = t[i];
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                    chars[j++] = c;
            }

            var cleaned = new string(chars, 0, j);
            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            return cleaned;
        }

        // Sørensen–Dice coefficient over character bigrams (fast, robust).
        private static double DiceCoefficient(string a, string b)
        {
            if (a == b) return 1.0;

            a = a.Replace(" ", "");
            b = b.Replace(" ", "");
            if (a.Length < 2 || b.Length < 2) return 0.0;

            var aCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < a.Length - 1; i++)
            {
                var bg = a.Substring(i, 2);
                aCounts.TryGetValue(bg, out var count);
                aCounts[bg] = count + 1;
            }

            int intersect = 0;
            int bTotal = 0;
            for (int i = 0; i < b.Length - 1; i++)
            {
                var bg = b.Substring(i, 2);
                bTotal++;
                if (aCounts.TryGetValue(bg, out var count) && count > 0)
                {
                    intersect++;
                    aCounts[bg] = count - 1;
                }
            }

            var aTotal = Math.Max(0, a.Length - 1);
            var denom = aTotal + bTotal;
            if (denom == 0) return 0.0;
            return (2.0 * intersect) / denom;
        }

        // === Events ===
        /// <summary>Fired when a command is captured (before routing)</summary>
        public event EventHandler<string>? CommandCaptured;
        /// <summary>Fired when an error occurs</summary>
        public event EventHandler<string>? Error;
        /// <summary>Fired when listening starts</summary>
        public event EventHandler<ListeningSource>? ListeningStarted;
        /// <summary>Fired when listening stops</summary>
        public event EventHandler? ListeningStopped;
        
        /// <summary>
        /// Handler for submitting messages to the main chat window.
        /// This unifies voice and typed input to use the same processing pipeline.
        /// </summary>
        public Action<string>? SubmitMessageHandler { get; set; }
        
        /// <summary>
        /// Optional handler for push-to-talk commands from ChatWindow.
        /// If set, push-to-talk commands are sent here instead of RouteCommand.
        /// This allows ChatWindow to handle the text in its own way (put in InputBox, send message).
        /// </summary>
        public Action<string>? PushToTalkCommandHandler { get; set; }
        
        /// <summary>
        /// When true, the orchestrator will NOT start its own Whisper recording on wake word.
        /// This allows ChatWindow to handle the recording instead, preventing conflicts.
        /// </summary>
        public bool DeferToExternalHandler { get; set; } = false;

        private VoiceSystemOrchestrator()
        {
            _inputManager = VoiceInputManager.Instance;
            _wakeWordService = WakeWordService.Instance;
            _stateManager = VoiceStateManager.Instance;
            // VoiceManager will be set via SetVoiceManager method
            Initialize();
        }

        /// <summary>
        /// Helper to safely invoke VoiceNotificationService on UI thread
        /// </summary>
        private void NotifyUI(Action notificationAction)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    notificationAction();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VoiceOrchestrator] ❌ UI notification error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Set the VoiceManager instance to use for TTS (should be called before StartAsync)
        /// </summary>
        public void SetVoiceManager(VoiceManager voiceManager)
        {
            if (_voiceManager != null)
            {
                // Unsubscribe from old manager
                _voiceManager.SpeechStarted -= OnSpeechStarted;
                _voiceManager.SpeechEnded -= OnSpeechEnded;
            }

            _voiceManager = voiceManager;

            if (_voiceManager != null)
            {
                // Subscribe to new manager
                _voiceManager.SpeechStarted += OnSpeechStarted;
                _voiceManager.SpeechEnded += OnSpeechEnded;

                // Connect VoiceActivityService to VoiceManager for visual feedback
                VoiceActivityService.Instance.ConnectToVoiceManager(_voiceManager);
            }
        }

        /// <summary>
        /// Speak text using the configured VoiceManager
        /// </summary>
        public async Task SpeakAsync(string text)
        {
            if (_voiceManager == null)
            {
                var vm = new VoiceManager();
                await vm.WaitForInitializationAsync();
                SetVoiceManager(vm);
            }
            await _voiceManager!.SpeakAsync(text);
        }

        private void Initialize()
        {
            try
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] Initializing voice system orchestrator");

                // Clear any persistent Whisper disable from a previous session's quota error.
                // Whisper should be retried on each fresh launch.
                try
                {
                    if (PreferencesStore.Instance.Current.DisableWhisperStt)
                    {
                        PreferencesStore.Instance.Update(p => p.DisableWhisperStt = false);
                        Debug.WriteLine("[VoiceSystemOrchestrator] Cleared DisableWhisperStt from previous session");
                        VDiag("Cleared DisableWhisperStt from previous session");
                    }
                }
                catch { }

                // Wire up events
                // NOTE: Subscribe to WakeWordCoordinator (not WakeWordService directly) to avoid duplicate handling
                // WakeWordCoordinator is the single source of truth for wake word events
                WakeWordCoordinator.Instance.WakeWordActivatedWithDetails += OnWakeWordActivated;
                _wakeWordService.Error += OnWakeWordError;
                _inputManager.Error += OnInputManagerError;

                // CRITICAL FIX: Subscribe to WakeWordFlowController.CommandReceived to route commands to AI
                WakeWordFlowController.Instance.CommandReceived += OnFlowControllerCommandReceived;
                Debug.WriteLine("[VoiceSystemOrchestrator] ✅ Subscribed to WakeWordFlowController.CommandReceived");

                _isInitialized = true;
                Debug.WriteLine("[VoiceSystemOrchestrator] ✅ Subscribed to WakeWordCoordinator (not WakeWordService directly)");
                Debug.WriteLine("[VoiceSystemOrchestrator] Initialization complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Initialization failed: {ex.Message}");
                Error?.Invoke(this, $"Voice system initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Start the voice system based on user preferences
        /// </summary>
        public async Task<bool> StartAsync()
        {
            Debug.WriteLine("[VoiceSystemOrchestrator] ═══════════════════════════════════════");
            Debug.WriteLine("[VoiceSystemOrchestrator] StartAsync() called");
            Debug.WriteLine($"[VoiceSystemOrchestrator] _isInitialized: {_isInitialized}");
            Debug.WriteLine($"[VoiceSystemOrchestrator] _isDisposed: {_isDisposed}");
            
            if (!_isInitialized || _isDisposed)
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] ❌ Cannot start - not initialized or disposed");
                return false;
            }

            var prefs = PreferencesStore.Instance.Current;
            Debug.WriteLine($"[VoiceSystemOrchestrator] EnableWakeWord preference: {prefs.EnableWakeWord}");

            // Check if wake word is enabled
            if (!prefs.EnableWakeWord)
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] ❌ Wake word disabled in preferences");
                _stateManager.Disable();
                return false;
            }

            if (!prefs.EnableMicrophone)
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] ❌ Microphone disabled in preferences");
                _stateManager.Disable();
                try { Error?.Invoke(this, "Microphone disabled"); } catch { }
                return false;
            }

            try
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] Starting voice system...");

                // Apply user preferences
                _wakeWordService.Sensitivity = prefs.WakeWordSensitivity;
                Debug.WriteLine($"[VoiceSystemOrchestrator] Wake word sensitivity: {prefs.WakeWordSensitivity}");

                // Start wake word service
                Debug.WriteLine("[VoiceSystemOrchestrator] Calling WakeWordService.StartAsync()...");
                var wakeWordStarted = await _wakeWordService.StartAsync();
                Debug.WriteLine($"[VoiceSystemOrchestrator] WakeWordService.StartAsync() returned: {wakeWordStarted}");
                
                if (!wakeWordStarted)
                {
                    Debug.WriteLine("[VoiceSystemOrchestrator] ❌ Failed to start wake word service");
                    _stateManager.SetState(GetWakeWordUnavailableState(), "Wake word unavailable");
                    try { Error?.Invoke(this, "Wake word unavailable"); } catch { }
                    return true;
                }

                // Transition to passive listening
                _stateManager.Enable();
                Debug.WriteLine("[VoiceSystemOrchestrator] ✅ Voice system started successfully");
                Debug.WriteLine("[VoiceSystemOrchestrator] State: PassiveListening");
                Debug.WriteLine("[VoiceSystemOrchestrator] ═══════════════════════════════════════");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] ❌ Start failed: {ex.Message}");
                Debug.WriteLine($"[VoiceSystemOrchestrator] Stack trace: {ex.StackTrace}");
                _stateManager.Suspend($"Start failed: {ex.Message}");
                Error?.Invoke(this, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Stop the voice system
        /// </summary>
        public void Stop()
        {
            if (_isDisposed) return;

            try
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] Stopping voice system...");

                // Cancel any active listening
                _activeListeningCts?.Cancel();
                _activeListeningCts = null;

                // Cancel any follow-up listening
                _followUpListeningCts?.Cancel();
                _followUpListeningCts = null;
                _followUpTimer?.Dispose();
                _followUpTimer = null;

                // Stop services
                _wakeWordService.Stop();
                _voiceManager?.Stop();

                // Update state
                _stateManager.Disable();

                Debug.WriteLine("[VoiceSystemOrchestrator] Voice system stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Stop error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update settings from preferences
        /// </summary>
        public async Task UpdateSettingsAsync()
        {
            if (_isDisposed) return;

            var prefs = PreferencesStore.Instance.Current;

            // Update wake word sensitivity
            _wakeWordService.Sensitivity = prefs.WakeWordSensitivity;

            // Restart if needed based on preferences
            if (prefs.EnableWakeWord && !_stateManager.IsActive)
            {
                await StartAsync();
            }
            else if (!prefs.EnableWakeWord && _stateManager.IsActive)
            {
                Stop();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // UNIFIED LISTENING ENTRY POINT
        // Both wake word and push-to-talk use this same path
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Canonical entry point for starting voice listening.
        /// Called by both wake word detection and push-to-talk button.
        /// </summary>
        /// <param name="source">What triggered the listening (WakeWord, PushToTalk, FollowUp)</param>
        public void BeginListening(ListeningSource source, bool continuous = false)
        {
            var sessionId = System.Threading.Interlocked.Increment(ref _listeningSessionId);
            _continuousPushToTalk = source == ListeningSource.PushToTalk && continuous;

            // Reject wake-word triggers that arrive during the post-TTS cooldown window.
            // This prevents TTS audio bleeding into the mic from re-triggering listening.
            if (source == ListeningSource.WakeWord && _postTtsCooldownUntilUtc > DateTime.UtcNow)
            {
                VDiag($"BeginListening(WakeWord) REJECTED — inside post-TTS cooldown (expires in {(_postTtsCooldownUntilUtc - DateTime.UtcNow).TotalMilliseconds:F0}ms)");
                Debug.WriteLine($"[VoiceOrchestrator] WakeWord rejected — post-TTS cooldown active");
                return;
            }

            // If the user re-triggers wake word while we believe we're already listening/speaking,
            // treat it as an intent to restart the capture pipeline. This prevents a common
            // "stuck on Listening" failure where state doesn't unwind cleanly.
            if (source == ListeningSource.WakeWord &&
                (_stateManager.CurrentState == VoiceSystemState.ActiveListening ||
                 _stateManager.CurrentState == VoiceSystemState.FollowUpListening ||
                 _stateManager.CurrentState == VoiceSystemState.Speaking))
            {
                try
                {
                    // Stop any in-progress speech so the mic can be acquired.
                    try { _voiceManager?.Stop(); } catch { }
                    StopListening();
                }
                catch
                {
                }
            }

            // If DeferToExternalHandler is set and this is a wake word trigger,
            // let the external handler (ChatWindow) handle the recording
            if (DeferToExternalHandler && source == ListeningSource.WakeWord)
            {
                Debug.WriteLine($"[VoiceOrchestrator] BeginListening({source}) - deferring to external handler (ChatWindow)");
                Debug.WriteLine($"[VoiceOrchestrator] NOT starting Whisper recording - ChatWindow will handle it");
                VoiceWakeLogger.Log("VoiceOrchestrator", "DeferToExternalHandler", extra: new { source = source.ToString() });
                
                // CRITICAL: Stop wake word service with proper reason to prevent microphone conflicts!
                // ChatWindow will restart it after command processing
                try
                {
                    if (_wakeWordService.IsListening)
                    {
                        Debug.WriteLine("[VoiceOrchestrator] Stopping wake word service to free microphone for ChatWindow");
                        _wakeWordService.Stop(WakeStopReason.ExternalHandlerDefer);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VoiceOrchestrator] Error stopping wake word: {ex.Message}");
                    VoiceWakeLogger.LogError("VoiceOrchestrator", ex);
                }
                
                return;
            }

            try
            {
                var prefsNow = PreferencesStore.Instance.Current;
                if (!prefsNow.EnableMicrophone)
                {
                    NotifyUI(() => VoiceNotificationService.Instance.NotifyError("Microphone disabled"));
                    try { Error?.Invoke(this, "Microphone disabled"); } catch { }
                    return;
                }
            }
            catch
            {
            }
            
            // Ignore if already in active conversation (unless it's follow-up which handles its own state)
            if (source != ListeningSource.FollowUp &&
                (_stateManager.CurrentState == VoiceSystemState.ActiveListening ||
                 _stateManager.CurrentState == VoiceSystemState.FollowUpListening ||
                 _stateManager.CurrentState == VoiceSystemState.Speaking))
            {
                Debug.WriteLine($"[VoiceOrchestrator] BeginListening({source}) ignored - already in state: {_stateManager.CurrentState}");
                return;
            }

            Debug.WriteLine($"[VoiceOrchestrator] ═══════════════════════════════════════");
            Debug.WriteLine($"[VoiceOrchestrator] BeginListening triggered by: {source}");
            Debug.WriteLine($"[VoiceOrchestrator] Timestamp: {DateTime.Now:HH:mm:ss.fff}");
            Debug.WriteLine($"[VoiceOrchestrator] ═══════════════════════════════════════");

            _currentListeningSource = source;
            _voiceConversationActive = true;
            Core.AppLogger.Log("[Voice] Listening started");

            // Set presence state to Listening
            NotifyUI(() => PresenceController.Instance.RecordInput());

            // Notify UI based on source
            var prefs = PreferencesStore.Instance.Current;
            if (source == ListeningSource.WakeWord && prefs.EnableWakeWordAudioCue)
            {
                NotifyUI(() => VoiceNotificationService.Instance.NotifyWakeWordDetected("Atlas", 1.0));
            }
            else
            {
                NotifyUI(() => VoiceNotificationService.Instance.NotifyListeningStarted());
            }

            // Transition state manager
            if (source == ListeningSource.FollowUp)
            {
                _stateManager.SetState(VoiceSystemState.FollowUpListening);
            }
            else
            {
                // Force state to ActiveListening (don't rely on WakeWordDetected guard)
                _stateManager.SetState(VoiceSystemState.ActiveListening);
            }

            Debug.WriteLine($"[VoiceOrchestrator] State → {_stateManager.CurrentState}");

            // Fire event for external listeners
            ListeningStarted?.Invoke(this, source);

            // Also emit a status line so UI surfaces (e.g., ChatWindow) can show a debug pill
            // even if other signal paths (audio callbacks) never arrive.
            try { Error?.Invoke(this, $"Listening ({source})"); } catch { }

            // Start the unified listening pipeline
            try
            {
                if (_wakeWordService.IsListening)
                    _wakeWordService.Stop(WakeStopReason.WhisperTakeover);
            }
            catch
            {
            }
            StartListeningPipeline(source, sessionId);
        }

        /// <summary>
        /// Stop listening and return to passive state
        /// </summary>
        public void StopListening()
        {
            Debug.WriteLine("[VoiceOrchestrator] StopListening called");

            _voiceConversationActive = false;

            _continuousPushToTalk = false;
            
            _activeListeningCts?.Cancel();
            _followUpListeningCts?.Cancel();
            _followUpTimer?.Dispose();
            _followUpTimer = null;

            try
            {
                if (_whisperRecognizer?.IsRecording == true)
                {
                    _ = _whisperRecognizer.StopRecordingAndTranscribeAsync();
                }
            }
            catch { }
            
            try
            {
                _dictationRecognizer?.Stop();
            }
            catch { }

            _stateManager.TimeoutActiveListening();
            NotifyUI(() => VoiceNotificationService.Instance.NotifyPassiveListening());
            RestartWakeWordService();
            
            ListeningStopped?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Check if currently listening for voice input
        /// </summary>
        public bool IsListening => 
            _stateManager.CurrentState == VoiceSystemState.ActiveListening ||
            _stateManager.CurrentState == VoiceSystemState.FollowUpListening;

        /// <summary>
        /// Get the current listening source (if listening)
        /// </summary>
        public ListeningSource CurrentListeningSource => _currentListeningSource;

        /// <summary>
        /// Handler for WakeWordCoordinator.WakeWordActivatedWithDetails event
        /// This is the canonical path - WakeWordCoordinator is the single source of truth
        /// </summary>
        private void OnWakeWordActivated(object? sender, WakeWordEventArgs e)
        {
            Debug.WriteLine($"[VoiceOrchestrator] Wake word activated via Coordinator: '{e.Text}' (confidence: {e.Confidence:P0})");
            
            // Use the unified entry point
            BeginListening(ListeningSource.WakeWord);
        }

        /// <summary>
        /// Unified listening pipeline - handles capture and transcription
        /// This is the canonical path for all listening sources (wake word, push-to-talk, follow-up)
        /// </summary>
        private void StartListeningPipeline(ListeningSource source, int sessionId)
        {
            VDiag($"StartListeningPipeline({source}) session={sessionId}");
            Debug.WriteLine($"[VoiceOrchestrator] ╔════════════════════════════════════════════════════════╗");
            Debug.WriteLine($"[VoiceOrchestrator] ║  StartListeningPipeline({source})                      ║");
            Debug.WriteLine($"[VoiceOrchestrator] ╚════════════════════════════════════════════════════════╝");
            
            // Cancel any existing listening
            _activeListeningCts?.Cancel();
            _activeListeningCts = new CancellationTokenSource();
            _followUpListeningCts?.Cancel();
            _followUpListeningCts = new CancellationTokenSource();
            _followUpTimer?.Dispose();
            _followUpTimer = null;
            _activeListeningStart = DateTime.Now;

            Debug.WriteLine($"[VoiceOrchestrator] Cancellation tokens reset");

            // Always arm a safety timeout immediately. Historically we only started timeouts
            // after RecordingStarted; if capture fails to actually start, the UI can remain stuck.
            try
            {
                StartListeningTimeout(source);
            }
            catch
            {
            }

            // Stop wake word service temporarily to avoid conflicts with Whisper
            try
            {
                if (_wakeWordService.IsListening)
                {
                    Debug.WriteLine("[VoiceOrchestrator] Temporarily stopping wake word detection for Whisper");
                    _wakeWordService.Stop(WakeStopReason.WhisperTakeover);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceOrchestrator] Error stopping wake word: {ex.Message}");
                VoiceWakeLogger.LogError("VoiceOrchestrator", ex);
            }

            // Create and configure Whisper recognizer
            try
            {
                if (source == ListeningSource.PushToTalk && _continuousPushToTalk)
                {
                    Debug.WriteLine("[VoiceOrchestrator] Continuous push-to-talk: using Windows dictation (no Whisper silence cutoff)");
                    _ = Task.Run(async () =>
                    {
                        try { await Task.Delay(200); } catch { }
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { StartWindowsDictation(source); } catch { }
                            }));
                        }
                        catch
                        {
                        }
                    });
                    return;
                }

                Debug.WriteLine("[VoiceOrchestrator] Creating WhisperSpeechRecognition instance...");
                _whisperRecognizer?.Dispose();
                _whisperRecognizer = new WhisperSpeechRecognition();
                
                VDiag($"Whisper IsConfigured={_whisperRecognizer.IsConfigured} HasApiKey={_whisperRecognizer.HasApiKey} TempDisabled={WhisperSpeechRecognition.IsTemporarilyDisabled} WillUseGemini={_whisperRecognizer.WillUseGemini} Reason={WhisperSpeechRecognition.TemporaryDisableReason}");
                Debug.WriteLine($"[VoiceOrchestrator] Whisper IsConfigured: {_whisperRecognizer.IsConfigured}, WillUseGemini: {_whisperRecognizer.WillUseGemini}");
                
                if (!_whisperRecognizer.IsConfigured)
                {
                    VDiag("Whisper NOT configured → falling back to Windows dictation");
                    Debug.WriteLine("[VoiceOrchestrator] Whisper not configured - falling back to Windows dictation");
                    try
                    {
                        Error?.Invoke(this, "Voice input using Windows dictation");
                    }
                    catch
                    {
                    }
                    _ = Task.Run(async () =>
                    {
                        try { await Task.Delay(350); } catch { }
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { StartWindowsDictation(source); } catch { }
                            }));
                        }
                        catch
                        {
                        }
                    });
                    return;
                }
                
                var sttProvider = _whisperRecognizer.WillUseGemini ? "Gemini" : "Whisper";
                VDiag($"{sttProvider} IS configured → starting {sttProvider}");
                Debug.WriteLine($"[VoiceOrchestrator] ✅ {sttProvider} is configured");
                try { Error?.Invoke(this, $"{sttProvider} starting..."); } catch { }

                // Configure silence timeout based on source
                _whisperRecognizer.MinimumRecordingSeconds = source == ListeningSource.FollowUp ? 0.9 : 0.7;
                _whisperRecognizer.NoSpeechTimeout = source == ListeningSource.FollowUp ? 4.5 : 3.5;
                _whisperRecognizer.SilenceTimeout = source == ListeningSource.FollowUp ? 1.8 : 1.4;
                Debug.WriteLine($"[VoiceOrchestrator] Silence timeout set to: {_whisperRecognizer.SilenceTimeout}s");

                var timeoutStarted = true;
                var fallbackTriggered = 0;
                var lastHearing = false;
                var lastHearingUpdateUtc = DateTime.MinValue;
                var recordingStarted = false;
                var audioLevelSeen = false;

                // Handle speech recognition - same for all sources
                _whisperRecognizer.SpeechRecognized += (s, text) =>
                {
                    VDiag($"Whisper SpeechRecognized: '{text}'");
                    Debug.WriteLine($"[VoiceOrchestrator] ╔════════════════════════════════════════════════════════╗");
                    Debug.WriteLine($"[VoiceOrchestrator] ║  🎤 SPEECH RECOGNIZED ({source})                       ║");
                    Debug.WriteLine($"[VoiceOrchestrator] ║  Text: \"{text}\"");
                    Debug.WriteLine($"[VoiceOrchestrator] ╚════════════════════════════════════════════════════════╝");
                    _activeListeningCts?.Cancel();
                    _followUpListeningCts?.Cancel();
                    _followUpTimer?.Dispose();
                    _followUpTimer = null;

                    if (source == ListeningSource.FollowUp)
                    {
                        _stateManager.FollowUpCaptured();
                    }

                    // Fire CommandCaptured event
                    CommandCaptured?.Invoke(this, text);

                    // CRITICAL FIX: Notify WakeWordFlowController of command
                    // This triggers state transitions and fires CommandReceived event
                    try
                    {
                        Debug.WriteLine($"[VoiceOrchestrator] 🔔 Notifying WakeWordFlowController.OnCommandReceived('{text}')");
                        WakeWordFlowController.Instance.OnCommandReceived(text);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VoiceOrchestrator] ⚠️ Error notifying flow controller: {ex.Message}");
                    }

                    // UNIFIED ROUTING: Always send to ChatWindow via SubmitMessageHandler
                    if (SubmitMessageHandler != null)
                    {
                        VDiag($"SubmitMessageHandler dispatching: '{text}'");
                        Debug.WriteLine("[VOICE] Final transcript: " + text);
                        try
                        {
                            SubmitMessageHandler(text);
                            VDiag("SubmitMessageHandler completed OK");
                        }
                        catch (Exception ex)
                        {
                            VDiag($"SubmitMessageHandler EXCEPTION: {ex.Message}");
                            Debug.WriteLine($"[VoiceOrchestrator] SubmitMessageHandler error: {ex.Message}");
                        }
                        finally
                        {
                            VDiag("FinishedProcessing → restarting wake word");
                            _stateManager.FinishedProcessing();
                            RestartWakeWordService();
                            ListeningStopped?.Invoke(this, EventArgs.Empty);
                            // FIX: Reset WakeWordFlowController state as external handler won't do it
                            try { WakeWordFlowController.Instance.Cancel(); } catch { }
                        }
                        return;
                    }
                    
                    if (PushToTalkCommandHandler != null)
                    {
                        Debug.WriteLine("[VoiceOrchestrator] SubmitMessageHandler missing - using PushToTalkCommandHandler");
                        try
                        {
                            PushToTalkCommandHandler(text);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[VoiceOrchestrator] PushToTalkCommandHandler error: {ex.Message}");
                        }
                        finally
                        {
                            _stateManager.FinishedProcessing();
                            RestartWakeWordService();
                            ListeningStopped?.Invoke(this, EventArgs.Empty);
                            // FIX: Reset WakeWordFlowController state as external handler won't do it
                            try { WakeWordFlowController.Instance.Cancel(); } catch { }
                        }
                        return;
                    }
                    
                    // Default: Route command through orchestrator
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            _stateManager.StartProcessing();
                            await RouteCommand(text);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[VoiceOrchestrator] Command processing error: {ex.Message}");
                        }
                        finally
                        {
                            RestartWakeWordService();
                        }
                    });
                };

                _whisperRecognizer.RecognitionError += (s, error) =>
                {
                    VDiag($"Whisper RecognitionError: '{error}'");
                    Debug.WriteLine($"[VoiceOrchestrator] ╔════════════════════════════════════════════════════════╗");
                    Debug.WriteLine($"[VoiceOrchestrator] ║  ❌ RECOGNITION ERROR ({source})                       ║");
                    Debug.WriteLine($"[VoiceOrchestrator] ║  Error: {error}");
                    Debug.WriteLine($"[VoiceOrchestrator] ╚════════════════════════════════════════════════════════╝");
                    _activeListeningCts?.Cancel();
                    _followUpListeningCts?.Cancel();
                    _followUpTimer?.Dispose();
                    _followUpTimer = null;

                    // If Whisper is recording but returns no_speech, users experience "Listening..." then nothing.
                    // For wake-word listens, fall back to Windows dictation once to ensure capture works.
                    if (sessionId == _listeningSessionId &&
                        source == ListeningSource.WakeWord &&
                        string.Equals(error, "no_speech", StringComparison.OrdinalIgnoreCase) &&
                        System.Threading.Interlocked.CompareExchange(ref fallbackTriggered, 1, 0) == 0)
                    {
                        Debug.WriteLine("[VoiceOrchestrator] Whisper returned no_speech → fallback to Windows dictation");
                        try { Error?.Invoke(this, "Didn't catch that — switching to Windows dictation"); } catch { }
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { StartWindowsDictation(source); } catch { HandleListeningTimeout(source); }
                            }));
                        }
                        catch
                        {
                            HandleListeningTimeout(source);
                        }
                        return;
                    }
                    
                    if (error != "no_speech")
                    {
                        NotifyUI(() => VoiceNotificationService.Instance.NotifyError(error));
                    }
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(error) && error != "no_speech")
                            Error?.Invoke(this, error);
                    }
                    catch
                    {
                    }
                    
                    HandleListeningTimeout(source);
                };

                _whisperRecognizer.RecognitionComplete += (s, e) =>
                {
                    Debug.WriteLine($"[VoiceOrchestrator] Recognition cycle complete ({source})");
                };

                _whisperRecognizer.RecordingStarted += (s, e) =>
                {
                    Debug.WriteLine($"[VoiceOrchestrator] Recording started ({source})");
                    NotifyUI(() => PresenceController.Instance.RecordInput());

                    recordingStarted = true;

                    try { Error?.Invoke(this, "Mic active (Whisper)"); } catch { }

                    // Only start timeout once we know capture actually started.
                    if (!timeoutStarted)
                    {
                        timeoutStarted = true;
                        StartListeningTimeout(source);
                    }
                };

                _whisperRecognizer.RecordingStopped += (s, e) =>
                {
                    Debug.WriteLine($"[VoiceOrchestrator] Recording stopped ({source})");
                    NotifyUI(() => VoiceNotificationService.Instance.NotifyProcessingStarted());
                    try { Error?.Invoke(this, "Processing..."); } catch { }
                };

                _whisperRecognizer.AudioLevelChanged += (s, args) =>
                {
                    try
                    {
                        audioLevelSeen = true;

                        // Only update on state change and throttle to avoid spamming UI.
                        var isHearing = args.IsHearing;
                        var nowUtc = DateTime.UtcNow;
                        if (isHearing != lastHearing && (nowUtc - lastHearingUpdateUtc) > TimeSpan.FromMilliseconds(250))
                        {
                            lastHearing = isHearing;
                            lastHearingUpdateUtc = nowUtc;

                            if (isHearing)
                            {
                                try { Error?.Invoke(this, "Hearing audio..."); } catch { }
                            }
                            else
                            {
                                try { Error?.Invoke(this, "Listening..."); } catch { }
                            }
                        }
                    }
                    catch
                    {
                    }
                };

                // Start recording
                Debug.WriteLine($"[VoiceOrchestrator] Starting Whisper recording ({source})...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // INCREASED: Wait longer for WakeWord service to fully release the mic
                        await Task.Delay(500);

                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                _whisperRecognizer.StartRecording();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[VoiceOrchestrator] Whisper StartRecording failed: {ex.Message}");
                                try { Error?.Invoke(this, "Whisper failed to start; using Windows dictation"); } catch { }

                                try
                                {
                                    // Fall back to Windows dictation instead of leaving the UI stuck on Listening
                                    StartWindowsDictation(source);
                                }
                                catch
                                {
                                    HandleListeningTimeout(source);
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VoiceOrchestrator] Whisper start task failed: {ex.Message}");
                        try { Error?.Invoke(this, "Voice capture failed to start"); } catch { }
                        HandleListeningTimeout(source);
                    }
                });

                // Watchdog: if Whisper hasn't actually started recording soon, fall back.
                // This prevents getting stuck on "Listening" when the mic can't be acquired.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1500).ConfigureAwait(false);
                        if (_isDisposed) return;
                        if (sessionId != _listeningSessionId) return;

                        if (System.Threading.Interlocked.CompareExchange(ref fallbackTriggered, 1, 0) != 0)
                            return;

                        if (_whisperRecognizer == null) return;
                        if (recordingStarted || _whisperRecognizer.IsRecording) return;

                        Debug.WriteLine($"[VoiceOrchestrator] Whisper watchdog: recording not started ({source}) → fallback to dictation");
                        try { Error?.Invoke(this, "Voice capture fallback: Windows dictation"); } catch { }

                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { StartWindowsDictation(source); } catch { HandleListeningTimeout(source); }
                            }));
                        }
                        catch
                        {
                            HandleListeningTimeout(source);
                        }
                    }
                    catch
                    {
                    }
                });

                // Watchdog: Whisper reports recording but we never receive audio callbacks.
                // This usually indicates a bad capture endpoint or driver stall.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (source != ListeningSource.WakeWord) return;

                        await Task.Delay(4500).ConfigureAwait(false);
                        if (_isDisposed) return;
                        if (sessionId != _listeningSessionId) return;

                        if (System.Threading.Interlocked.CompareExchange(ref fallbackTriggered, 1, 0) != 0)
                            return;

                        if (_whisperRecognizer == null) return;
                        if (!recordingStarted && !_whisperRecognizer.IsRecording) return;
                        if (audioLevelSeen) return;

                        Debug.WriteLine($"[VoiceOrchestrator] Whisper watchdog: no audio callbacks ({source}) → fallback to dictation");
                        try { Error?.Invoke(this, "Voice capture stalled; using Windows dictation"); } catch { }

                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { StartWindowsDictation(source); } catch { HandleListeningTimeout(source); }
                            }));
                        }
                        catch
                        {
                            HandleListeningTimeout(source);
                        }
                    }
                    catch
                    {
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceOrchestrator] Error starting Whisper: {ex.Message}");
                NotifyUI(() => VoiceNotificationService.Instance.NotifyError($"Voice error: {ex.Message}"));
                HandleListeningTimeout(source);
            }
        }

        private void StartWindowsDictation(ListeningSource source)
        {
            VDiag($"StartWindowsDictation({source})");
            // Ensure we create and start the recognizer on the UI thread to avoid COM threading issues
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    _dictationRecognizer?.Dispose();
                    _dictationRecognizer = new WindowsDictationRecognizer();

                    var continuous = source == ListeningSource.PushToTalk && _continuousPushToTalk;

                    // Enforce minimum listen window: keep mic active at least 3s unless user manually stops.
                    var listenWindowStartUtc = DateTime.UtcNow;
                    bool MinimumWindowSatisfied() => (DateTime.UtcNow - listenWindowStartUtc) >= MinimumListenWindow;

                    // Track manual silence detection and hypothesis stability
                    DateTime lastActivity = DateTime.Now;
                    DateTime lastHypothesisChange = DateTime.Now;
                    string currentHypothesis = "";
                    bool committed = false;

                    // Shared handler for recognition success
                    Action<string> onRecognized = (text) =>
                    {
                        VDiag($"Dictation onRecognized: '{text}' committed={committed}");
                        // Echo suppression (AI picking up its own voice)
                        if (ShouldIgnoreAsEcho(text))
                        {
                            lastActivity = DateTime.Now;
                            lastHypothesisChange = DateTime.Now;
                            currentHypothesis = "";
                            return;
                        }

                        if (TryHandleCancelOrStop(text, source))
                        {
                            committed = true;
                            return;
                        }

                        if (!continuous && !committed && !MinimumWindowSatisfied())
                        {
                            // Keep mic open; treat early results as hypothesis.
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                currentHypothesis = text;
                                lastHypothesisChange = DateTime.Now;
                                lastActivity = DateTime.Now;
                            }
                            return;
                        }

                        if (continuous)
                        {
                            // Continuous mic mode: forward each utterance but keep listening until user stops.
                            try { Error?.Invoke(this, "Listening..."); } catch { }

                            if (PushToTalkCommandHandler != null)
                            {
                                try { PushToTalkCommandHandler(text); } catch { }
                                return;
                            }

                            if (SubmitMessageHandler != null)
                            {
                                try { SubmitMessageHandler(text); } catch { }
                                return;
                            }

                            return;
                        }

                        if (committed) return;
                        committed = true;
                        
                        // Notify UI we are finalizing
                        try { Error?.Invoke(this, "Finalizing..."); } catch { }

                        Debug.WriteLine($"[VoiceOrchestrator] Committing command: {text}");

                        // Cancel tokens first
                        _activeListeningCts?.Cancel();
                        _followUpListeningCts?.Cancel();
                        _followUpTimer?.Dispose();
                        _followUpTimer = null;

                        // Stop recognizer when not in continuous mode
                        try { _dictationRecognizer?.Stop(); } catch { }

                        if (source == ListeningSource.FollowUp)
                            _stateManager.FollowUpCaptured();

                        CommandCaptured?.Invoke(this, text);

                        // CRITICAL FIX: Notify WakeWordFlowController of command
                        // This triggers state transitions and fires CommandReceived event
                        try
                        {
                            Debug.WriteLine($"[VoiceOrchestrator] 🔔 Notifying WakeWordFlowController.OnCommandReceived('{text}')");
                            WakeWordFlowController.Instance.OnCommandReceived(text);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[VoiceOrchestrator] ⚠️ Error notifying flow controller: {ex.Message}");
                        }

                        // UNIFIED ROUTING: Always send to ChatWindow via SubmitMessageHandler
                        // Suppress follow-up listening for dictation fallback — Windows Speech
                        // transcription quality is too poor to cascade into follow-ups.
                        _voiceConversationActive = false;
                        
                        if (SubmitMessageHandler != null)
                        {
                             Debug.WriteLine("[VOICE] Final transcript: " + text);
                             try
                             {
                                 SubmitMessageHandler(text);
                             }
                             catch (Exception ex)
                             {
                                 Debug.WriteLine($"[VoiceOrchestrator] SubmitMessageHandler error: {ex.Message}");
                             }
                             finally
                             {
                                 _stateManager.FinishedProcessing();
                                 RestartWakeWordService();
                                 ListeningStopped?.Invoke(this, EventArgs.Empty);
                                 // FIX: Reset WakeWordFlowController state as external handler won't do it
                                 try { WakeWordFlowController.Instance.Cancel(); } catch { }
                             }
                             return;
                        }

                        if (PushToTalkCommandHandler != null)
                        {
                             Debug.WriteLine("[VoiceOrchestrator] SubmitMessageHandler missing - using PushToTalkCommandHandler");
                             try
                             {
                                 PushToTalkCommandHandler(text);
                             }
                             catch (Exception ex)
                             {
                                 Debug.WriteLine($"[VoiceOrchestrator] PushToTalkCommandHandler error: {ex.Message}");
                             }
                             finally
                             {
                                 _stateManager.FinishedProcessing();
                                 RestartWakeWordService();
                                 ListeningStopped?.Invoke(this, EventArgs.Empty);
                                 // FIX: Reset WakeWordFlowController state as external handler won't do it
                                 try { WakeWordFlowController.Instance.Cancel(); } catch { }
                             }
                             return;
                        }

                        // Offload everything to background task to prevent UI freeze
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // Stop recognizer asynchronously to avoid blocking
                                try { _dictationRecognizer?.Stop(); } catch { }

                                Debug.WriteLine("[VoiceOrchestrator] Starting processing task...");
                                _stateManager.StartProcessing();
                                
                                // FORCE UPDATE UI STATUS
                                NotifyUI(() => {
                                    VoiceNotificationService.Instance.NotifyCommandCaptured(text);
                                    // Also update the status text directly as a fallback
                                    Error?.Invoke(this, $"Processing: {text}");
                                });
                                
                                await RouteCommand(text);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[VoiceOrchestrator] RouteCommand error: {ex.Message}");
                                NotifyUI(() => VoiceNotificationService.Instance.NotifyError($"Error: {ex.Message}"));
                            }
                            finally
                            {
                                RestartWakeWordService();
                            }
                        });
                    };

                    _dictationRecognizer.SpeechRecognized += (s, text) =>
                    {
                        VDiag($"Dictation SpeechRecognized: '{text}'");
                        onRecognized(text);
                    };

                    _dictationRecognizer.SpeechHypothesized += (s, text) =>
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                VDiag($"Dictation Hypothesized: '{text}'");
                                if (text != currentHypothesis)
                                {
                                    currentHypothesis = text;
                                    lastHypothesisChange = DateTime.Now;
                                }
                                lastActivity = DateTime.Now;
                                Error?.Invoke(this, $"Heard: {text}");
                            }
                        }
                        catch
                        {
                        }
                    };
                    
                    _dictationRecognizer.AudioLevelUpdated += (s, level) =>
                    {
                        try
                        {
                            if (continuous)
                            {
                                if (level > 20)
                                    lastActivity = DateTime.Now;
                                return;
                            }

                            // Manual Silence/Stability Detection
                            // 1. Audio Level Silence: If volume drops below threshold for ~1.4s
                            // 2. Hypothesis Stability: If text hasn't changed for ~1.1s (even if there is noise)
                            
                            if (level > 20) // Increased noise threshold from 15 to 20
                            {
                                lastActivity = DateTime.Now;
                            }
                            
                            if (!committed && !string.IsNullOrWhiteSpace(currentHypothesis))
                            {
                                var timeSinceLastAudio = (DateTime.Now - lastActivity).TotalSeconds;
                                var timeSinceLastHypothesis = (DateTime.Now - lastHypothesisChange).TotalSeconds;

                                if (!MinimumWindowSatisfied())
                                {
                                    // Minimum listen window not satisfied yet.
                                    return;
                                }
                                
                                var wordCount = Regex.Matches(currentHypothesis.Trim(), @"\S+").Count;
                                if (wordCount < 2 && timeSinceLastAudio < 1.8)
                                    return;

                                // Strategy 1: Silence detected
                                if (timeSinceLastAudio > 1.4)
                                {
                                    Debug.WriteLine($"[VoiceOrchestrator] Silence detected ({timeSinceLastAudio:F1}s) - committing: {currentHypothesis}");
                                    onRecognized(currentHypothesis);
                                }
                                // Strategy 2: Text stable (user stopped speaking but maybe background noise continues)
                                else if (timeSinceLastHypothesis > 1.1 && timeSinceLastAudio > 0.6)
                                {
                                    Debug.WriteLine($"[VoiceOrchestrator] Hypothesis stable ({timeSinceLastHypothesis:F1}s) - committing: {currentHypothesis}");
                                    onRecognized(currentHypothesis);
                                }
                            }
                        }
                        catch
                        {
                        }
                    };

                    _dictationRecognizer.RecognitionError += (s, error) =>
                    {
                        VDiag($"Dictation RecognitionError: '{error}'");
                        if (continuous)
                        {
                            // Keep the mic latched on; attempt to restart dictation if it errors out.
                            try
                            {
                                Debug.WriteLine($"[VoiceOrchestrator] Dictation error in continuous mode: {error}");
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await Task.Delay(350);
                                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            if (_continuousPushToTalk && _stateManager.CurrentState == VoiceSystemState.ActiveListening)
                                            {
                                                try { _dictationRecognizer?.Stop(); } catch { }
                                                StartWindowsDictation(ListeningSource.PushToTalk);
                                            }
                                        });
                                    }
                                    catch { }
                                });
                            }
                            catch { }

                            return;
                        }

                        // If we have a substantial hypothesis, try to use it before failing.
                        // Ignore short fragments (< 3 words) — they're likely noise from transient
                        // signal issues, not real speech.
                        if (!committed && !string.IsNullOrWhiteSpace(currentHypothesis))
                        {
                             var wordCount = System.Text.RegularExpressions.Regex.Matches(currentHypothesis.Trim(), @"\S+").Count;
                             if (wordCount >= 3)
                             {
                                 Debug.WriteLine($"[VoiceOrchestrator] Error '{error}' but have hypothesis ({wordCount} words) - committing: {currentHypothesis}");
                                 onRecognized(currentHypothesis);
                                 return;
                             }
                             Debug.WriteLine($"[VoiceOrchestrator] Error '{error}' - ignoring short hypothesis ({wordCount} words): '{currentHypothesis}'");
                        }

                        _activeListeningCts?.Cancel();
                        _followUpListeningCts?.Cancel();
                        _followUpTimer?.Dispose();
                        _followUpTimer = null;

                        if (!string.IsNullOrWhiteSpace(error))
                            NotifyUI(() => VoiceNotificationService.Instance.NotifyError(error));
                            
                        HandleListeningTimeout(source);
                    };

                    _dictationRecognizer.RecognitionComplete += (s, e) =>
                    {
                        Debug.WriteLine($"[VoiceOrchestrator] Dictation recognition complete ({source})");
                    };

                    NotifyUI(() => VoiceNotificationService.Instance.NotifyListeningStarted());
                    try
                    {
                        Error?.Invoke(this, "Dictation started");
                    }
                    catch
                    {
                    }

                    // Start directly on UI thread
                    try
                    {
                        var ok = _dictationRecognizer.Start();
                        VDiag($"Dictation Start() returned: {ok} | InputDevice: {_dictationRecognizer.InputDeviceName}");
                        if (!ok)
                        {
                            VDiag("Dictation Start failed");
                            try
                            {
                                Error?.Invoke(this, "Windows Speech Recognition not available");
                            }
                            catch
                            {
                            }
                            HandleListeningTimeout(source);
                        }
                        else
                        {
                            listenWindowStartUtc = DateTime.UtcNow;
                        }
                    }
                    catch
                    {
                        try
                        {
                            Error?.Invoke(this, "Voice input failed to start");
                        }
                        catch
                        {
                        }
                        HandleListeningTimeout(source);
                    }

                    // Increase timeout for Windows Dictation as it can be slower to initialize
                    // and user might take a moment to start speaking
                    if (!continuous)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // 15 seconds timeout for Windows Dictation
                                await Task.Delay(15000, _activeListeningCts!.Token);
                                VDiag($"Dictation 15s timeout fired ({source})");
                                Debug.WriteLine($"[VoiceOrchestrator] Active listening timeout ({source})");
                                try
                                {
                                    System.Windows.Application.Current.Dispatcher.Invoke(() => _dictationRecognizer?.Stop());
                                }
                                catch { }
                                HandleListeningTimeout(source);
                            }
                            catch (OperationCanceledException)
                            {
                                // Normal cancellation
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VoiceOrchestrator] Dictation start error: {ex.Message}");
                    NotifyUI(() => VoiceNotificationService.Instance.NotifyError($"Voice error: {ex.Message}"));
                    HandleListeningTimeout(source);
                }
            });
        }

        /// <summary>
        /// Start the appropriate timeout for the listening source
        /// </summary>
        private void StartListeningTimeout(ListeningSource source)
        {
            if (source == ListeningSource.PushToTalk && _continuousPushToTalk)
                return;

            if (source == ListeningSource.FollowUp)
            {
                // Follow-up uses a timer with user-configured duration
                var prefs = PreferencesStore.Instance.Current;
                var durationMs = (int)(prefs.FollowUpListeningDuration * 1000);
                
                _followUpTimer = new System.Threading.Timer(_ =>
                {
                    Debug.WriteLine("[VoiceOrchestrator] Follow-up timeout → PassiveListening");
                    
                    try
                    {
                        if (_whisperRecognizer?.IsRecording == true)
                        {
                            _ = _whisperRecognizer.StopRecordingAndTranscribeAsync();
                        }
                    }
                    catch { }
                    
                    HandleListeningTimeout(source);
                    
                    _followUpTimer?.Dispose();
                    _followUpTimer = null;
                }, null, durationMs, System.Threading.Timeout.Infinite);
            }
            else
            {
                // Wake word and push-to-talk use task-based timeout
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(ActiveListeningTimeoutMs, _activeListeningCts!.Token);
                        
                        Debug.WriteLine($"[VoiceOrchestrator] Active listening timeout ({source})");
                        
                        try
                        {
                            if (_whisperRecognizer?.IsRecording == true)
                                await _whisperRecognizer.StopRecordingAndTranscribeAsync();
                        }
                        catch
                        {
                        }

                        try
                        {
                            _dictationRecognizer?.Stop();
                        }
                        catch
                        {
                        }
                        
                        HandleListeningTimeout(source);
                    }
                    catch (OperationCanceledException)
                    {
                        // Normal cancellation - command was captured
                    }
                });
            }
        }

        /// <summary>
        /// Handle timeout for any listening source
        /// </summary>
        private void HandleListeningTimeout(ListeningSource source)
        {
            VDiag($"HandleListeningTimeout({source}) state={_stateManager.CurrentState}");
            Debug.WriteLine($"[VoiceOrchestrator] HandleListeningTimeout({source}) - current state: {_stateManager.CurrentState}");

            if (source == ListeningSource.FollowUp)
                _voiceConversationActive = false;
            
            if (source == ListeningSource.FollowUp)
            {
                _stateManager.FollowUpTimeout();
            }
            else
            {
                _stateManager.TimeoutActiveListening();
            }
            
            // Force state to PassiveListening if still not there (safety net)
            if (_stateManager.CurrentState != VoiceSystemState.PassiveListening &&
                _stateManager.CurrentState != VoiceSystemState.Disabled)
            {
                Debug.WriteLine($"[VoiceOrchestrator] Forcing state to PassiveListening (was: {_stateManager.CurrentState})");
                _stateManager.SetState(VoiceSystemState.PassiveListening);
            }
            
            NotifyUI(() => VoiceNotificationService.Instance.NotifyPassiveListening());
            RestartWakeWordService();
            ListeningStopped?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Restart wake word service after command processing
        /// </summary>
        private void RestartWakeWordService()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                if (prefs.EnableWakeWord && !_wakeWordService.IsListening)
                {
                    Debug.WriteLine("[VoiceSystemOrchestrator] Restarting wake word service...");
                    VoiceWakeLogger.Log("VoiceOrchestrator", "RestartWakeWordRequested");
                    
                    // Use Task.Run to avoid blocking and add small delay for audio system to settle
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Delay to let audio system settle (and to avoid wake word retriggering from TTS tail)
                            var now = DateTime.UtcNow;
                            var baseDelay = TimeSpan.FromMilliseconds(500);
                            var postTtsDelay = _postTtsCooldownUntilUtc > now ? (_postTtsCooldownUntilUtc - now) : TimeSpan.Zero;
                            var delay = baseDelay;
                            if (postTtsDelay > delay) delay = postTtsDelay;
                            if (delay > TimeSpan.Zero)
                                await Task.Delay(delay);
                            
                            // Re-enable continuous listening before starting
                            _wakeWordService.ContinuousListeningEnabled = true;
                            
                            var started = await _wakeWordService.StartAsync();
                            Debug.WriteLine($"[VoiceSystemOrchestrator] Wake word service restart: {(started ? "SUCCESS" : "FAILED")}");
                            VoiceWakeLogger.Log("VoiceOrchestrator", "RestartWakeWordResult", extra: new { success = started });
                            
                            if (!started)
                            {
                                // Retry loop with increasing backoff
                                for (int i = 1; i <= 3; i++)
                                {
                                    int retryDelayMs = 500 * i;
                                    Debug.WriteLine($"[VoiceSystemOrchestrator] Retrying wake word service start (attempt {i}/3) after {retryDelayMs}ms...");
                                    await Task.Delay(retryDelayMs);
                                    started = await _wakeWordService.StartAsync();
                                    Debug.WriteLine($"[VoiceSystemOrchestrator] Wake word service retry {i}: {(started ? "SUCCESS" : "FAILED")}");
                                    
                                    if (started) break;
                                }
                            }
                            
                            if (!started)
                            {
                                // Last resort: Force reinitialize
                                Debug.WriteLine("[VoiceSystemOrchestrator] ❌ All restart attempts failed - forcing state reset");
                                Error?.Invoke(this, "Voice activation failed to restart. Please toggle in settings.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[VoiceSystemOrchestrator] Wake word restart task error: {ex.Message}");
                            VoiceWakeLogger.LogError("VoiceOrchestrator", ex);
                        }
                    });
                }
                else
                {
                    Debug.WriteLine($"[VoiceSystemOrchestrator] Skip restart - EnableWakeWord: {prefs.EnableWakeWord}, IsListening: {_wakeWordService.IsListening}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Error restarting wake word: {ex.Message}");
            }
        }

        private async Task RouteCommand(string command)
        {
            try
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Routing command: '{command}'");

                // Notify that processing has started
                NotifyUI(() => VoiceNotificationService.Instance.NotifyProcessingStarted());

                // Simple command routing logic
                var lowerCommand = command.ToLower();

                if (lowerCommand.Contains("open"))
                {
                    // Route to Agent Action
                    Debug.WriteLine("[VoiceSystemOrchestrator] Routing to Agent Action");
                    await ProcessAgentAction(command, CancellationToken.None);
                }
                else if (lowerCommand.Contains("check") || lowerCommand.Contains("scan") || lowerCommand.Contains("diagnose"))
                {
                    // Route to Workflow or Macro
                    Debug.WriteLine("[VoiceSystemOrchestrator] Routing to Workflow/Macro");
                    await ProcessWorkflowMacro(command, CancellationToken.None);
                }
                else
                {
                    // Route to Chat
                    Debug.WriteLine("[VoiceSystemOrchestrator] Routing to Chat");
                    await ProcessChatCommand(command, CancellationToken.None);
                }

                // Notify task completion
                NotifyUI(() => VoiceNotificationService.Instance.NotifyTaskCompleted(command));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Command routing error: {ex.Message}");
                NotifyUI(() => VoiceNotificationService.Instance.NotifyError($"Command error: {ex.Message}"));
                
                // Use system error response
                var errorResponse = ResponseStyleController.Instance.SelectResponse(
                    ResponseIntent.SystemError,
                    SystemState.Error,
                    PreferencesStore.Instance.Current.Persona,
                    1.0);
                await SpeakResponse(errorResponse, ResponseIntent.SystemError);
            }
            finally
            {
                _stateManager.FinishedProcessing();
                NotifyUI(() => VoiceNotificationService.Instance.NotifyPassiveListening());
            }
        }

        private async Task ProcessAgentAction(string command, CancellationToken ct)
        {
            try
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Processing agent action: {command}");
                ct.ThrowIfCancellationRequested();
                
                // Extract what to open from the command
                var lowerCommand = command.ToLower();
                var appName = ExtractAppName(lowerCommand);
                
                if (string.IsNullOrEmpty(appName))
                {
                    // Fall back to chat processing if we can't determine what to open
                    await ProcessChatCommand(command, ct);
                    return;
                }

                // Speak execution start
                await SpeakResponse($"Opening {appName}", ResponseIntent.ExecutionStart, ct);
                ct.ThrowIfCancellationRequested();
                
                // Use QuickLauncher to open the app
                var result = await Agent.QuickLauncher.Instance.LaunchAsync(appName);
                
                if (result.StartsWith("✅") || result.Contains("Launched"))
                {
                    Debug.WriteLine($"[VoiceSystemOrchestrator] Launch successful: {result}");
                    // Don't speak completion - the app opening is confirmation enough
                }
                else
                {
                    Debug.WriteLine($"[VoiceSystemOrchestrator] Launch failed: {result}");
                    await SpeakResponse($"I couldn't find {appName}. Try saying the full application name.", ResponseIntent.SystemError, ct);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] Agent action cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Agent action error: {ex.Message}");
                await SpeakResponse("Something went wrong trying to open that.", ResponseIntent.SystemError, ct);
            }
        }

        /// <summary>
        /// Extract the application name from an "open" command
        /// </summary>
        private string ExtractAppName(string command)
        {
            // Common patterns: "open chrome", "launch notepad", "start calculator"
            var patterns = new[] { "open ", "launch ", "start ", "run " };
            
            foreach (var pattern in patterns)
            {
                var idx = command.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    return command.Substring(idx + pattern.Length).Trim();
                }
            }
            
            return "";
        }

        private async Task ProcessWorkflowMacro(string command, CancellationToken ct)
        {
            try
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Processing workflow/macro: {command}");
            ct.ThrowIfCancellationRequested();
                
                // Try to find and execute a matching macro
                var macroEngine = Agent.AgentMacroEngine.Instance;
                var macro = macroEngine.FindMacro(command);
                
                if (macro != null)
                {
                    // Speak what we're doing
                    await SpeakResponse($"Running {macro.Title}", ResponseIntent.ExecutionStart, ct);
                    ct.ThrowIfCancellationRequested();
                    
                    // Execute the macro
                    var result = await macroEngine.TryExecuteAsync(command);
                    
                    if (result != null && result.Success)
                    {
                        Debug.WriteLine($"[VoiceSystemOrchestrator] Macro completed: {result.Summary}");
                        
                        // Speak a brief summary
                        var summary = result.Summary ?? "Done";
                        if (summary.Length > 100)
                        {
                            summary = summary.Substring(0, 100) + "...";
                        }
                        await SpeakResponse(summary, ResponseIntent.ExecutionComplete);
                    }
                    else
                    {
                        await SpeakResponse("The diagnostic completed but found some issues.", ResponseIntent.OperationFailed, ct);
                    }
                }
                else
                {
                    // No matching macro - fall back to chat
                    Debug.WriteLine("[VoiceSystemOrchestrator] No matching macro found, falling back to chat");
                    await ProcessChatCommand(command, ct);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] Workflow/macro cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Workflow/macro error: {ex.Message}");
                await SpeakResponse("Something went wrong running that diagnostic.", ResponseIntent.SystemError, ct);
            }
        }

        private async Task ProcessChatCommand(string command, CancellationToken ct)
        {
            try
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Processing chat command: '{command}'");
                ct.ThrowIfCancellationRequested();
                
                if (string.IsNullOrWhiteSpace(command) || command.Length < 2)
                {
                     Debug.WriteLine("[VoiceSystemOrchestrator] Command too short/empty - ignoring to prevent AI confusion");
                     return;
                }

                // Check if AI is configured
                var provider = AIManager.GetActiveProviderInstance();
                if (provider == null || !provider.IsConfigured)
                {
                    Debug.WriteLine("[VoiceSystemOrchestrator] AI not configured");
                    await SpeakResponse("I need an API key to help with that. Please configure your AI provider in settings.", ResponseIntent.SystemError, ct);
                    return;
                }

                // Build messages for AI
                var messages = new List<object>
                {
                    new Dictionary<string, string>
                    {
                        { "role", "system" },
                        { "content", GetVoiceSystemPrompt() }
                    },
                    new Dictionary<string, string>
                    {
                        { "role", "user" },
                        { "content", command }
                    }
                };

                // Send to AI
                Debug.WriteLine("[VoiceSystemOrchestrator] Sending to AI...");
                NotifyUI(() => {
                    VoiceNotificationService.Instance.NotifyCommandCaptured(command);
                    Error?.Invoke(this, "Asking Atlas..."); 
                });

                // START WATCHDOG: Ensure status is cleared even if AI hangs
                // We start it BEFORE the AI call now
                using var watchdogCts = new CancellationTokenSource();
                var watchdogTask = Task.Delay(30000, watchdogCts.Token).ContinueWith(_ => 
                {
                    Debug.WriteLine("[VoiceOrchestrator] AI Watchdog triggered - force clearing status");
                    NotifyUI(() => Error?.Invoke(this, ""));
                }, TaskScheduler.Default);

                // Add 45s timeout to AI call
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var response = await AIManager.SendMessageAsync(messages, 500, linked.Token);
                
                // Cancel watchdog
                watchdogCts.Cancel();

                if (response.Success && !string.IsNullOrEmpty(response.Content))
                {
                    ct.ThrowIfCancellationRequested();

                    Debug.WriteLine($"[VoiceSystemOrchestrator] AI response: {response.Content}");
                    
                    NotifyUI(() => Error?.Invoke(this, "Speaking..."));

                    // Speak the response
                    await SpeakResponse(response.Content, ResponseIntent.Acknowledged, ct);
                }
                else
                {
                    ct.ThrowIfCancellationRequested();

                    Debug.WriteLine($"[VoiceSystemOrchestrator] AI error: {response.Error}");
                    await SpeakResponse("Sorry, I couldn't process that request.", ResponseIntent.SystemError, ct);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] Chat command cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Chat command error: {ex.Message}");
                await SpeakResponse("Something went wrong. Please try again.", ResponseIntent.SystemError, ct);
            }
            finally
            {
                NotifyUI(() => Error?.Invoke(this, ""));
            }
        }

        /// <summary>
        /// Get the system prompt for voice interactions
        /// </summary>
        private string GetVoiceSystemPrompt()
        {
            var prefs = PreferencesStore.Instance.Current;
            var persona = prefs.Persona;
            
            return $@"You are Atlas, a helpful AI assistant for Windows. You are responding to voice commands.

IMPORTANT RULES:
1. Keep responses SHORT and CONVERSATIONAL - they will be spoken aloud
2. Aim for 1-3 sentences maximum
3. Be direct and helpful
4. Don't use markdown, bullet points, or formatting - just natural speech
5. Don't say ""I'm an AI"" or similar disclaimers
6. Match the user's energy and tone
7. IF YOU DO NOT UNDERSTAND or the input seems like noise/gibberish, just say ""I'm listening"" or ""Go ahead"". Do NOT say ""I can't understand you"".

Persona style: {persona}

Current time: {DateTime.Now:dddd, MMMM d, yyyy h:mm tt}

Respond naturally as if having a conversation.";
        }

        private async Task SpeakResponse(string response, ResponseIntent intent = ResponseIntent.Acknowledged, CancellationToken ct = default)
        {
            try
            {
                if (_voiceManager == null)
                {
                    Debug.WriteLine("[VoiceSystemOrchestrator] No VoiceManager set - cannot speak response");
                    return;
                }

                if (ct.IsCancellationRequested)
                    return;

                // STEP 30 FIX: Use SpeechCoordinator to enforce single-speaker rule
                // This prevents the two-pipeline issue where System and Conversation voices overlap
                var coordinator = SpeechCoordinator.Instance;
                coordinator.SetVoiceManager(_voiceManager);

                // Filter generic chatbot responses
                var filtered = ResponseStyleController.Instance.FilterGenericResponse(response, intent);
                
                // If response was filtered, use system response instead
                string finalResponse;
                if (filtered != response)
                {
                    Debug.WriteLine($"[VoiceOrchestrator] Filtered generic response: \"{response}\" → using system response");
                    finalResponse = ResponseStyleController.Instance.SelectResponse(
                        intent,
                        MapVoiceStateToSystemState(_stateManager.CurrentState),
                        PreferencesStore.Instance.Current.Persona,
                        1.0);
                }
                else
                {
                    finalResponse = filtered;
                }
                
                // Check if we should speak this response
                if (!ResponseStyleController.Instance.ShouldSpeak(intent, MapVoiceStateToSystemState(_stateManager.CurrentState)))
                {
                    Debug.WriteLine($"[VoiceOrchestrator] Skipping speech for intent: {intent}");
                    Core.AppLogger.Log($"VoiceOrchestrator: Skipping speech for intent {intent}");
                    return;
                }
                
                Debug.WriteLine($"[VoiceOrchestrator] Speaking via SpeechCoordinator: \"{finalResponse}\"");
                Core.AppLogger.Log($"VoiceOrchestrator: Speaking via Coordinator");

                // Track last spoken response for echo suppression.
                _lastAiSpokenText = finalResponse;
                _lastAiSpokenAtUtc = DateTime.UtcNow;

                _stateManager.StartSpeaking();
                NotifyUI(() => VoiceNotificationService.Instance.NotifySpeakingStarted());

                // Critical: prevent AI voice from re-triggering wake word or being transcribed.
                PauseVoiceInputForSpeech();
                
                // Use SpeechCoordinator for coordinated speech
                // FALLBACK MECHANISM ADDED
                bool success = false;
                try
                {
                    using var _ = ct.Register(() =>
                    {
                        try { coordinator.CancelCurrentSpeech(); } catch { }
                        try { _voiceManager.Stop(); } catch { }
                    });

                    // Add 60s timeout to prevent UI hanging if speech engine locks up
                    var speechTask = coordinator.SpeakConversationAsync(finalResponse, reason: $"orchestrator_{intent}");
                    var timeoutTask = Task.Delay(60000);
                    
                    var completed = await Task.WhenAny(speechTask, timeoutTask);
                    if (completed == timeoutTask)
                     {
                          Debug.WriteLine("[VoiceOrchestrator] Speech timed out - forcing continue");
                          Core.AppLogger.LogError("VoiceOrchestrator: Speech timed out - forcing continue");
                          success = false;
                          try { coordinator.CancelCurrentSpeech(); } catch {}
                     }
                     else
                     {
                         success = await speechTask;
                         if (!success) Core.AppLogger.LogWarning("VoiceOrchestrator: Speech rejected by coordinator");
                     }
                 }
                 catch (Exception ex)
                 {
                     Debug.WriteLine($"[VoiceOrchestrator] Coordinator failed: {ex.Message}");
                     Core.AppLogger.LogError($"VoiceOrchestrator: Coordinator failed: {ex.Message}");
                 }

                 if (!success)
                 {
                     Debug.WriteLine("[VoiceOrchestrator] Speech rejected or failed - attempting direct fallback");
                     Core.AppLogger.LogWarning("VoiceOrchestrator: Attempting direct fallback");
                     // Try direct playback via VoiceManager if coordinator rejects it (e.g. state confusion)
                     // This is a safety net for "Speaking..." but no sound scenarios
                     try 
                     {
                         if (ct.IsCancellationRequested) return;
                         var utterance = new AssistantUtterance(finalResponse, UtteranceSource.Conversation);
                         // Also add timeout for fallback
                         var fallbackTask = _voiceManager.SpeakAsync(utterance);
                         var fallbackTimeout = Task.Delay(30000);
                         await Task.WhenAny(fallbackTask, fallbackTimeout);
                     }
                     catch (Exception ex)
                     {
                          Debug.WriteLine($"[VoiceOrchestrator] Direct fallback failed: {ex.Message}");
                          Core.AppLogger.LogError($"VoiceOrchestrator: Direct fallback failed: {ex.Message}");
                          NotifyUI(() => VoiceNotificationService.Instance.NotifyError($"Audio error: {ex.Message}"));
                     }
                 }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] Speech cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Speech error: {ex.Message}");
                Core.AppLogger.LogError($"VoiceSystemOrchestrator: Critical speech error: {ex.Message}");
                NotifyUI(() => VoiceNotificationService.Instance.NotifyError($"Speech error: {ex.Message}"));
            }
            finally
            {
                // Clear "Speaking..." status text in UI (which was set via Error event)
                NotifyUI(() => Error?.Invoke(this, ""));
            }
        }
        
        /// <summary>
        /// Map VoiceSystemState to SystemState for response selection
        /// </summary>
        private SystemState MapVoiceStateToSystemState(VoiceSystemState voiceState)
        {
            return voiceState switch
            {
                VoiceSystemState.Disabled => SystemState.Idle,
                VoiceSystemState.PassiveListening => SystemState.Listening,
                VoiceSystemState.ActiveListening => SystemState.Listening,
                VoiceSystemState.Processing => SystemState.Processing,
                VoiceSystemState.Speaking => SystemState.Speaking,
                VoiceSystemState.Suspended => SystemState.Error,
                _ => SystemState.Idle
            };
        }

        private void OnSpeechStarted(object? sender, EventArgs e)
        {
            // Speech started is handled by state manager
            // Also notify flow controller for UI state tracking
            try
            {
                Debug.WriteLine("[VoiceOrchestrator] 🔊 Speech started - notifying WakeWordFlowController");
                // Extra safety: ensure we paused listening at actual audio start.
                PauseVoiceInputForSpeech();
                WakeWordFlowController.Instance.OnResponseStarted();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceOrchestrator] ⚠️ Error notifying flow controller (speech started): {ex.Message}");
            }
        }

        private void OnSpeechEnded(object? sender, EventArgs e)
        {
            Debug.WriteLine("[VoiceOrchestrator] Speech ended - transitioning to follow-up listening");
            _postTtsCooldownUntilUtc = DateTime.UtcNow.Add(PostTtsWakeWordCooldown);
            _lastAiSpeechEndedAtUtc = DateTime.UtcNow;

            // Only open follow-up listening if the user initiated a voice conversation.
            if (_voiceConversationActive)
                _stateManager.FinishedSpeaking();
            else
                _stateManager.SetState(VoiceSystemState.PassiveListening);
            NotifyUI(() => VoiceNotificationService.Instance.NotifySpeakingEnded());

            // Notify flow controller that response is complete
            try
            {
                Debug.WriteLine("[VoiceOrchestrator] 🏁 Speech ended - notifying WakeWordFlowController");
                WakeWordFlowController.Instance.OnResponseCompleted();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceOrchestrator] ⚠️ Error notifying flow controller (speech ended): {ex.Message}");
            }

            // Resume microphone after a short delay to avoid picking up the TTS tail.
            if (_voiceConversationActive)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(MicResumeDelayAfterSpeech).ConfigureAwait(false);
                        if (_isDisposed) return;
                        StartFollowUpListening();
                    }
                    catch
                    {
                    }
                });
            }
            else
            {
                ResumeWakeWordAfterSpeechIfNeeded();
            }
        }

        private void OnWakeWordError(object? sender, string e)
        {
            Debug.WriteLine($"[VoiceSystemOrchestrator] Wake word error: {e}");
            AtlasAI.Core.AppLogger.LogInfo($"[VoiceSystem] Wake word error: {e}");
            if (IsWakeWordMicAccessIssue(e))
            {
                _stateManager.SetState(GetWakeWordUnavailableState(), "Wake word unavailable");
                Error?.Invoke(this, e);
                return;
            }

            try { _wakeWordService.Stop(); } catch { }
            _stateManager.SetState(VoiceSystemState.PassiveListening, "Voice recovering...");
            Error?.Invoke(this, e);
            ScheduleAutoRecovery(requiresSuspendedState: false);
        }

        private static bool IsWakeWordMicAccessIssue(string? error)
        {
            var text = (error ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            return text.IndexOf("could not access microphone", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("microphone", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("audio device", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("another app is using it", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private VoiceSystemState GetWakeWordUnavailableState()
        {
            return _stateManager.CurrentState switch
            {
                VoiceSystemState.Disabled => VoiceSystemState.PassiveListening,
                VoiceSystemState.Suspended => VoiceSystemState.PassiveListening,
                _ => _stateManager.CurrentState
            };
        }

        private void OnInputManagerError(object? sender, string e)
        {
            Debug.WriteLine($"[VoiceSystemOrchestrator] Input manager error: {e}");
            AtlasAI.Core.AppLogger.LogInfo($"[VoiceSystem] Microphone error: {e}");
            try { VoiceInputManager.Instance.Stop(); } catch { }

            if (IsWakeWordMicAccessIssue(e))
            {
                _stateManager.SetState(GetWakeWordUnavailableState(), "Wake word unavailable");
                Error?.Invoke(this, e);
                return;
            }

            _stateManager.SetState(VoiceSystemState.PassiveListening, "Voice recovering...");
            Error?.Invoke(this, e);
            ScheduleAutoRecovery(requiresSuspendedState: false);
        }

        private int _autoRecoveryAttempts;
        private void ScheduleAutoRecovery(bool requiresSuspendedState)
        {
            var attempt = System.Threading.Interlocked.Increment(ref _autoRecoveryAttempts);
            if (attempt > 5) return; // give up after 5 attempts
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt)).ConfigureAwait(false);
                    if (requiresSuspendedState && _stateManager.CurrentState != VoiceSystemState.Suspended) return;
                    AtlasAI.Core.AppLogger.LogInfo($"[VoiceSystem] Auto-recovery attempt {attempt}");

                    // Reset the wake word pipeline without forcing the capture manager to restart.
                    VoiceInputManager.Instance.Stop();
                    try { _wakeWordService.Stop(); } catch { }
                    await Task.Delay(800).ConfigureAwait(false);

                    var wakeOk = await _wakeWordService.StartAsync().ConfigureAwait(false);
                    if (!wakeOk)
                    {
                        AtlasAI.Core.AppLogger.LogInfo($"[VoiceSystem] Auto-recovery attempt {attempt}: wake word restart failed");
                        return;
                    }

                    _stateManager.Enable();
                    System.Threading.Interlocked.Exchange(ref _autoRecoveryAttempts, 0);
                    AtlasAI.Core.AppLogger.LogInfo("[VoiceSystem] Auto-recovery succeeded (wake word)");
                }
                catch (Exception ex)
                {
                    AtlasAI.Core.AppLogger.LogInfo($"[VoiceSystem] Auto-recovery attempt failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// CRITICAL FIX: Handle commands from WakeWordFlowController and route to AI pipeline.
        /// This is the missing link that connects recognized voice commands to the AI system.
        /// </summary>
        private void OnFlowControllerCommandReceived(object? sender, string command)
        {
            if (_isDisposed)
            {
                Debug.WriteLine("[VoiceOrchestrator] ❌ Command received but orchestrator is disposed");
                return;
            }

            // FIX: Check if we should defer to external handlers to prevent double processing
            // If SubmitMessageHandler or PushToTalkCommandHandler is set, the command is already being
            // handled in the recognition loop (StartListeningPipeline).
            if (SubmitMessageHandler != null || PushToTalkCommandHandler != null)
            {
                Debug.WriteLine("[VoiceOrchestrator] ⏭️ Ignoring FlowController event - deferring to external handler");
                return;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                Debug.WriteLine("[VoiceOrchestrator] ❌ Empty command received from flow controller - ignoring");
                return;
            }

            Debug.WriteLine("═══════════════════════════════════════════════════════════════");
            Debug.WriteLine($"[VoiceOrchestrator] ✅ COMMAND RECEIVED FROM FLOW CONTROLLER: '{command}'");
            Debug.WriteLine("═══════════════════════════════════════════════════════════════");

            // Check if user cancelled listening
            if (_activeListeningCts?.IsCancellationRequested == true)
            {
                Debug.WriteLine("[VoiceOrchestrator] ⚠️ Listening was cancelled - not forwarding command");
                return;
            }

            // Marshal to background thread to avoid blocking UI
            _ = Task.Run(async () =>
            {
                CancellationTokenSource? cts = null;
                try
                {
                    Debug.WriteLine($"[VoiceOrchestrator] 🔄 Forwarding command to AI pipeline: '{command}'");

                    lock (_actionLock)
                    {
                        _currentActionCts?.Cancel();
                        _currentActionCts?.Dispose();
                        _currentActionCts = new CancellationTokenSource();
                        cts = _currentActionCts;
                    }

                    // Update state to Processing
                    _stateManager.StartProcessing();

                    // Notify UI that we're processing
                    NotifyUI(() =>
                    {
                        VoiceNotificationService.Instance.NotifyCommandCaptured(command);
                        Error?.Invoke(this, $"Processing: {command}");
                    });

                    // Route command through existing routing logic
                    Debug.WriteLine($"[VoiceOrchestrator] 📤 Calling RouteCommand('{command}')...");
                    await RouteCommand(command, cts!.Token);

                    Debug.WriteLine($"[VoiceOrchestrator] ✅ Command routed successfully: '{command}'");
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"[VoiceOrchestrator] ⚠️ Command processing cancelled: '{command}'");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VoiceOrchestrator] ❌ ERROR routing command '{command}': {ex.Message}");
                    Debug.WriteLine($"[VoiceOrchestrator] Stack trace: {ex.StackTrace}");

                    // Show error to user via notification service (NOT MessageBox)
                    NotifyUI(() =>
                    {
                        VoiceNotificationService.Instance.NotifyError($"Voice command error: {ex.Message}");
                        Error?.Invoke(this, $"Error: {ex.Message}");
                    });

                    // Try to speak error response
                    try
                    {
                        var errorResponse = ResponseStyleController.Instance.SelectResponse(
                            ResponseIntent.SystemError,
                            SystemState.Error,
                            PreferencesStore.Instance.Current.Persona,
                            1.0);
                        await SpeakResponse(errorResponse, ResponseIntent.SystemError);
                    }
                    catch (Exception speakEx)
                    {
                        Debug.WriteLine($"[VoiceOrchestrator] ❌ Failed to speak error response: {speakEx.Message}");
                    }
                }
                finally
                {
                    Debug.WriteLine($"[VoiceOrchestrator] 🏁 Command processing complete: '{command}'");

                    // Ensure we return to proper state
                    _stateManager.FinishedProcessing();
                    RestartWakeWordService();
                }
            });
        }

        private async Task RouteCommand(string command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Routing command (ct): '{command}'");
                NotifyUI(() => VoiceNotificationService.Instance.NotifyProcessingStarted());

                var lowerCommand = (command ?? "").ToLowerInvariant();
                ct.ThrowIfCancellationRequested();

                if (lowerCommand.Contains("open"))
                {
                    Debug.WriteLine("[VoiceSystemOrchestrator] Routing to Agent Action");
                    await ProcessAgentAction(command, ct);
                }
                else if (lowerCommand.Contains("check") || lowerCommand.Contains("scan") || lowerCommand.Contains("diagnose"))
                {
                    Debug.WriteLine("[VoiceSystemOrchestrator] Routing to Workflow/Macro");
                    await ProcessWorkflowMacro(command, ct);
                }
                else
                {
                    Debug.WriteLine("[VoiceSystemOrchestrator] Routing to Chat");
                    await ProcessChatCommand(command, ct);
                }

                ct.ThrowIfCancellationRequested();
                NotifyUI(() => VoiceNotificationService.Instance.NotifyTaskCompleted(command));
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[VoiceSystemOrchestrator] RouteCommand cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Command routing error: {ex.Message}");
                NotifyUI(() => VoiceNotificationService.Instance.NotifyError($"Command error: {ex.Message}"));

                var errorResponse = ResponseStyleController.Instance.SelectResponse(
                    ResponseIntent.SystemError,
                    SystemState.Error,
                    PreferencesStore.Instance.Current.Persona,
                    1.0);
                await SpeakResponse(errorResponse, ResponseIntent.SystemError, ct);
            }
            finally
            {
                _stateManager.FinishedProcessing();
                NotifyUI(() => VoiceNotificationService.Instance.NotifyPassiveListening());
            }
        }

        private bool TryHandleCancelOrStop(string? text, ListeningSource source)
        {
            var normalized = NormalizeCancelStop(text);
            if (normalized == null) return false;

            if (normalized == "cancel" || normalized == "stop" || normalized.StartsWith("cancel ") || normalized.StartsWith("stop "))
            {
                Debug.WriteLine($"[VoiceOrchestrator] VoiceCommandMode cancel detected: '{text}' (source={source})");
                CancelCurrentActionInternal($"voice_{normalized}");
                return true;
            }

            return false;
        }

        private static string? NormalizeCancelStop(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim().ToLowerInvariant();
            // Strip simple punctuation at the end
            while (t.Length > 0 && ".,!?".Contains(t[^1]))
                t = t.Substring(0, t.Length - 1);
            // Collapse double spaces
            while (t.Contains("  "))
                t = t.Replace("  ", " ");
            return t;
        }

        public void CancelCurrentAction()
        {
            CancelCurrentActionInternal("manual_cancel");
        }

        private void CancelCurrentActionInternal(string reason)
        {
            try
            {
                Debug.WriteLine($"[VoiceOrchestrator] Cancelling current action ({reason})");
                _voiceConversationActive = false;
                _postTtsCooldownUntilUtc = DateTime.UtcNow.Add(PostTtsWakeWordCooldown);

                try { WakeWordFlowController.Instance.Cancel(); } catch { }

                lock (_actionLock)
                {
                    try { _currentActionCts?.Cancel(); } catch { }
                }

                try { SpeechCoordinator.Instance.CancelCurrentSpeech(); } catch { }
                try { _voiceManager?.Stop(); } catch { }

                try { _activeListeningCts?.Cancel(); } catch { }
                try { _followUpListeningCts?.Cancel(); } catch { }
                try { _followUpTimer?.Dispose(); } catch { }
                _followUpTimer = null;

                try { _dictationRecognizer?.Stop(); } catch { }
                try { _whisperRecognizer?.Dispose(); } catch { }

                _stateManager.SetState(VoiceSystemState.PassiveListening);
                NotifyUI(() =>
                {
                    Error?.Invoke(this, "");
                    VoiceNotificationService.Instance.NotifyPassiveListening();
                });

                RestartWakeWordService();
                ListeningStopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceOrchestrator] CancelCurrentAction error: {ex.Message}");
            }
        }

        /// <summary>
        /// Start follow-up listening window after TTS completes
        /// </summary>
        private void StartFollowUpListening()
        {
            // Use the unified entry point
            BeginListening(ListeningSource.FollowUp);
        }

        /// <summary>
        /// Cancel follow-up listening (e.g., user says "Stop")
        /// </summary>
        public void CancelFollowUpListening()
        {
            if (_stateManager.CurrentState == VoiceSystemState.FollowUpListening)
            {
                Debug.WriteLine("[VoiceOrchestrator] Follow-up listening cancelled by user");
                _followUpListeningCts?.Cancel();
                _followUpTimer?.Dispose();
                _followUpTimer = null;
                _stateManager.SetState(VoiceSystemState.PassiveListening);
                VoiceNotificationService.Instance.NotifyPassiveListening();
            }
        }

        /// <summary>
        /// Stop speaking immediately (e.g., user says "Stop")
        /// </summary>
        public void StopSpeaking()
        {
            if (_stateManager.CurrentState == VoiceSystemState.Speaking)
            {
                Debug.WriteLine("[VoiceOrchestrator] Speaking stopped by user");
                _voiceManager?.Stop();
                // Transition to follow-up listening (user might want to give another command)
                _stateManager.FinishedSpeaking();
                StartFollowUpListening();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();

            try
            {
                _activeListeningCts?.Cancel();
                _activeListeningCts?.Dispose();
                _followUpListeningCts?.Cancel();
                _followUpListeningCts?.Dispose();
                _followUpTimer?.Dispose();
                _followUpTimer = null;
                
                // Dispose Whisper recognizer
                _whisperRecognizer?.Dispose();
                _whisperRecognizer = null;

                // Unsubscribe from events
                // NOTE: We subscribe to WakeWordCoordinator, not WakeWordService directly
                WakeWordCoordinator.Instance.WakeWordActivatedWithDetails -= OnWakeWordActivated;
                _wakeWordService.Error -= OnWakeWordError;
                _inputManager.Error -= OnInputManagerError;
                WakeWordFlowController.Instance.CommandReceived -= OnFlowControllerCommandReceived;
                Debug.WriteLine("[VoiceSystemOrchestrator] ✅ Unsubscribed from WakeWordFlowController.CommandReceived");

                if (_voiceManager != null)
                {
                    _voiceManager.SpeechStarted -= OnSpeechStarted;
                    _voiceManager.SpeechEnded -= OnSpeechEnded;
                    // Don't dispose VoiceManager - it's shared with ChatWindow
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceSystemOrchestrator] Dispose error: {ex.Message}");
            }

            Debug.WriteLine("[VoiceSystemOrchestrator] Disposed");
        }
    }
}
