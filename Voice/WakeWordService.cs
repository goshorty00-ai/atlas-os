using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Speech.Recognition;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AtlasAI.Core;
using NAudio.CoreAudioApi;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Wake word detection service for Atlas-AI.
    /// Listens for "Atlas" and "Hey Atlas" activation phrases.
    /// Works regardless of window focus or HUD state.
    /// Logs to %AppData%\AtlasAI\wake_word_log.txt for debugging.
    /// </summary>
    public class WakeWordService : IDisposable
    {
        private static WakeWordService? _instance;
        private static readonly object _lock = new object();
        
        // Log file for debugging wake word issues
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "wake_word_log.txt");
        
        private static void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var line = $"[{timestamp}] {message}";
            Debug.WriteLine($"[WakeWordService] {message}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }
        }        public static WakeWordService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new WakeWordService();
                    }
                }
                return _instance;
            }
        }

        // === Configuration ===
        // Wake word variations - keep this TIGHT.
        // Overly-broad grammars cause constant false triggers (e.g., "at", "last", etc.).
        // We rely on a small set of plausible variants + confidence gating.
        private readonly string[] _wakeWords = {
            "Atlas", "atlas",
            "Hey Atlas", "hey atlas",
            "OK Atlas", "ok atlas",
            "Okay Atlas", "okay atlas",
            "Hi Atlas", "hi atlas",
        };
        private const double DebounceSeconds = 1.5;
        private const int VadTimeoutMs = 2000; // VAD timeout for STT fallback
        // DISABLED dictation grammar. System.Speech's DictationGrammar completely
        // absorbs all speech (including "Atlas") so the WakeWords grammar never fires.
        // Instead we rely on CFGConfidenceRejectionThreshold to reject non-atlas speech.
        private bool _useDictationFallback = false;

        // === State ===
        private SpeechRecognitionEngine? _recognizer;
        private bool _isListening = false;
        private bool _isDisposed = false;
        // Prevent concurrent Start/Stop/auto-restart operations from fighting the SpeechRecognitionEngine
        // which causes: "Cannot perform this operation while the recognizer is doing recognition."
        private readonly SemaphoreSlim _startStopGate = new SemaphoreSlim(1, 1);
        private DateTime _lastTrigger = DateTime.MinValue;
        private bool _initializationFailed = false;
        private string _initializationError = "";
        
        // === Wake Word State Management ===
        private WakeWordState _currentState = WakeWordState.Idle;
        private readonly object _stateLock = new object();
        private System.Threading.Timer? _cooldownTimer;
        
        // === Continuous Listening Loop ===
        private System.Threading.Timer? _ensureListeningTimer;
        private int _restartAttempts = 0;
        private const int MaxRestartAttempts = 5;
        private const int RestartCheckIntervalMs = 2000; // Check every 2 seconds
        private bool _continuousListeningEnabled = true;
        private int _startGeneration = 0;
        
        // Fallback VAD + STT state
        private bool _vadActive = false;
        private DateTime _vadStartTime;
        private readonly List<float> _vadBuffer = new();

        // === Settings ===
        private double _sensitivity = 0.5; // 0..1
        private double _noiseGateDb = -40.0; // dB threshold

        // === Events ===
        public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;
        public event EventHandler<string>? StateChanged;
        public event EventHandler<string>? Error;

        // === Properties ===
        public bool IsListening => _isListening && !_initializationFailed;
        public bool IsInitialized => _recognizer != null && !_initializationFailed;
        
        /// <summary>
        /// When true, wake word listening will automatically restart if it stops unexpectedly.
        /// Set to false when intentionally stopping (e.g., for Whisper recording).
        /// </summary>
        public bool ContinuousListeningEnabled
        {
            get => _continuousListeningEnabled;
            set
            {
                _continuousListeningEnabled = value;
                Log($"ContinuousListeningEnabled = {value}");
                VoiceWakeLogger.Log("WakeWordService", "ContinuousListeningChanged", extra: new { enabled = value });
            }
        }
        public string InitializationError => _initializationError;
        
        /// <summary>
        /// Current state of the wake word detection system
        /// </summary>
        public WakeWordState CurrentState
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentState;
                }
            }
        }
        
        /// <summary>
        /// Timestamp of the last wake word trigger
        /// </summary>
        public DateTime LastTriggerTime => _lastTrigger;
        
        /// <summary>
        /// Whether the system is currently in cooldown period
        /// </summary>
        public bool IsInCooldown => CurrentState == WakeWordState.Cooldown;
        
        public double Sensitivity
        {
            get => _sensitivity;
            set => _sensitivity = Math.Clamp(value, 0.0, 1.0);
        }
        public double NoiseGateDb
        {
            get => _noiseGateDb;
            set => _noiseGateDb = Math.Clamp(value, -60.0, 0.0);
        }

        private WakeWordService()
        {
            Initialize();
        }

        /// <summary>
        /// Transition to a new wake word state with logging
        /// STEP 29: Added stabilization logging for debugging
        /// </summary>
        private void TransitionState(WakeWordState newState, string reason = "")
        {
            lock (_stateLock)
            {
                var oldState = _currentState;
                if (oldState == newState) return;

                _currentState = newState;
                Debug.WriteLine($"[WakeWordService] State: {oldState} → {newState}" + 
                    (string.IsNullOrEmpty(reason) ? "" : $" ({reason})"));
                
                // STEP 29: Stabilization logging
                StabilizationLogger.LogWakeStateTransition(oldState.ToString(), newState.ToString(), reason);
                
                StateChanged?.Invoke(this, $"{newState}");
            }
        }

        /// <summary>
        /// Check if we're in cooldown period and should reject wake word
        /// </summary>
        private bool CheckCooldown()
        {
            if (CurrentState == WakeWordState.Cooldown)
            {
                var timeSinceTrigger = DateTime.Now - _lastTrigger;
                Debug.WriteLine($"[WakeWordService] In cooldown - {timeSinceTrigger.TotalSeconds:F1}s since last trigger");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Start cooldown period after wake word trigger
        /// STEP 29: Added stabilization logging
        /// </summary>
        private void StartCooldown()
        {
            var cooldownMs = (int)(DebounceSeconds * 1000);
            TransitionState(WakeWordState.Cooldown, "debounce period");
            StabilizationLogger.LogWakeCooldownStart(cooldownMs);
            
            // Cancel existing timer if any
            _cooldownTimer?.Dispose();
            
            // Set timer to return to Listening state after debounce period
            _cooldownTimer = new System.Threading.Timer(_ =>
            {
                if (_isListening && !_isDisposed)
                {
                    TransitionState(WakeWordState.Listening, "cooldown complete");
                    StabilizationLogger.LogWakeCooldownEnd();
                }
                _cooldownTimer?.Dispose();
                _cooldownTimer = null;
            }, null, cooldownMs, System.Threading.Timeout.Infinite);
        }

        /// <summary>
        /// Log wake word activation attempt for debugging
        /// </summary>
        private void LogActivationAttempt(string text, double confidence, bool accepted, string reason = "")
        {
            var status = accepted ? "ACCEPTED" : "REJECTED";
            var reasonText = string.IsNullOrEmpty(reason) ? "" : $" - {reason}";
            Debug.WriteLine($"[WakeWordService] Activation {status}: '{text}' (confidence: {confidence:P0}){reasonText}");
        }

        /// <summary>
        /// Normalize wake word text for case-insensitive, noise-tolerant matching
        /// </summary>
        private string NormalizeWakeWord(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            // Trim whitespace
            var normalized = text.Trim();
            
            // Convert to lowercase
            normalized = normalized.ToLowerInvariant();
            
            // Remove multiple spaces
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }
            
            return normalized;
        }

        /// <summary>
        /// Check if normalized text matches wake word patterns.
        /// IMPORTANT: avoid matching inside normal sentences (false triggers).
        /// </summary>
        private bool IsWakeWordMatch(string normalizedText)
        {
            if (string.IsNullOrWhiteSpace(normalizedText)) return false;

            // Wake word should be a short utterance, not part of a longer sentence.
            var words = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 2) return false;
            
            // Direct exact matches only (no Contains).
            string[] directMatches = {
                "atlas",
                "hey atlas", "ok atlas", "okay atlas", "hi atlas"
            };

            for (int i = 0; i < directMatches.Length; i++)
            {
                if (string.Equals(normalizedText, directMatches[i], StringComparison.Ordinal))
                {
                    Log($"✅ Direct match '{directMatches[i]}' (exact)");
                    return true;
                }
            }

            // Two-word prefixes (e.g., "hey atlas") are already covered; now only allow
            // single-word phonetic variants close to "atlas".
            if (words.Length == 1)
            {
                var word = words[0];
                if (IsSimilarToAtlas(word))
                {
                    Log($"✅ Similarity match: '{word}' is similar to 'atlas'");
                    return true;
                }
            }

            // If it's short, allow combined phonetic check (e.g., weird spacing).
            if (words.Length == 2)
            {
                var combined = words[0] + words[1];
                if (IsSimilarToAtlas(combined))
                {
                    Log($"✅ Combined match: '{combined}' is similar to 'atlas'");
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if a word is phonetically similar to "atlas"
        /// Uses multiple heuristics for maximum detection
        /// </summary>
        private bool IsSimilarToAtlas(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            if (word.Length < 4 || word.Length > 7) return false;
            
            word = word.ToLowerInvariant();
            
            // Only exact matches — the grammar itself only contains "Atlas"/"atlas".
            // Edit-distance matching was causing too many false positives.
            string[] exactMatches = { "atlas" };
            
            foreach (var s in exactMatches)
            {
                if (word == s) return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Simple edit distance (Levenshtein) calculation
        /// </summary>
        private int SimpleEditDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;
            
            int[,] d = new int[a.Length + 1, b.Length + 1];
            
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            
            return d[a.Length, b.Length];
        }

        private void Initialize()
        {
            try
            {
                Log("═══════════════════════════════════════");
                Log("Initializing wake word detection");
                Log($"Thread: {Thread.CurrentThread.ManagedThreadId}");

                // Check if Windows Speech Recognition is available
                var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                Log($"Found {recognizers.Count} recognizer(s)");
                
                if (recognizers.Count == 0)
                {
                    _initializationFailed = true;
                    _initializationError = "Windows Speech Recognition not available. Please enable it in Windows Settings.";
                    Log($"❌ {_initializationError}");
                    Error?.Invoke(this, _initializationError);
                    return;
                }

                foreach (var r in recognizers)
                {
                    Log($"  - {r.Name} ({r.Culture})");
                }

                // Create recognizer - prefer English if available
                RecognizerInfo? preferredRecognizer = null;
                foreach (var r in recognizers)
                {
                    if (r.Culture.TwoLetterISOLanguageName == "en")
                    {
                        preferredRecognizer = r;
                        break;
                    }
                }
                
                if (preferredRecognizer != null)
                {
                    _recognizer = new SpeechRecognitionEngine(preferredRecognizer);
                    Debug.WriteLine($"[WakeWordService] ✅ Using preferred recognizer: {preferredRecognizer.Name}");
                }
                else
                {
                    _recognizer = new SpeechRecognitionEngine();
                    Debug.WriteLine($"[WakeWordService] ✅ Using default recognizer: {_recognizer.RecognizerInfo.Name}");
                }
                
                Debug.WriteLine($"[WakeWordService] Culture: {_recognizer.RecognizerInfo.Culture}");

                // Create wake word grammar with high priority
                var choices = new Choices(_wakeWords);
                var grammarBuilder = new GrammarBuilder(choices);
                grammarBuilder.Culture = _recognizer.RecognizerInfo.Culture;
                var wakeWordGrammar = new Grammar(grammarBuilder) 
                { 
                    Name = "WakeWords",
                    Priority = 127,  // Highest priority
                    Weight = 1.0f
                };
                _recognizer.LoadGrammar(wakeWordGrammar);
                Debug.WriteLine($"[WakeWordService] ✅ Wake word grammar loaded: {string.Join(", ", _wakeWords.Take(5))}...");

                // Add dictation grammar as fallback to catch any speech
                if (_useDictationFallback)
                {
                    try
                    {
                        var dictationGrammar = new DictationGrammar()
                        {
                            Name = "DictationFallback",
                            Priority = 0,  // Lower priority than wake words
                            Weight = 0.01f // Very low weight—only wins when wake grammar is a poor match
                        };
                        _recognizer.LoadGrammar(dictationGrammar);
                        Debug.WriteLine($"[WakeWordService] ✅ Dictation fallback grammar loaded");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WakeWordService] ⚠️ Could not load dictation grammar: {ex.Message}");
                        // Continue without dictation - wake word grammar should still work
                    }
                }

                // Configure for continuous listening - VERY AGGRESSIVE
                _recognizer.BabbleTimeout = TimeSpan.Zero;
                _recognizer.InitialSilenceTimeout = TimeSpan.Zero;
                _recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(150);
                _recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(150);
                Debug.WriteLine($"[WakeWordService] ✅ Timeouts configured (aggressive mode)");
                
                // Set rejection threshold so the engine rejects speech that doesn't
                // closely match the constrained grammar. Without this, the engine
                // force-matches ALL speech to the closest grammar entry at high
                // confidence (since there's nothing else to match against).
                // Value range 0–100; higher = stricter. 50 is a good balance.
                try
                {
                    _recognizer.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", 50);
                    Debug.WriteLine($"[WakeWordService] ✅ CFGConfidenceRejectionThreshold set to 50");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WakeWordService] ⚠️ Could not set rejection threshold: {ex.Message}");
                }

                // Event handlers
                _recognizer.SpeechRecognized += OnSpeechRecognized;
                _recognizer.SpeechRecognitionRejected += OnSpeechRejected;
                _recognizer.RecognizeCompleted += OnRecognizeCompleted;
                _recognizer.AudioStateChanged += OnAudioStateChanged;
                _recognizer.SpeechDetected += OnSpeechDetected;
                _recognizer.SpeechHypothesized += OnSpeechHypothesized;
                Debug.WriteLine($"[WakeWordService] ✅ Event handlers attached");

                // Try to set input to default audio device
                // NOTE: This may fail if another app has exclusive access - we'll retry in StartAsync
                try
                {
                    _recognizer.SetInputToDefaultAudioDevice();
                    Debug.WriteLine($"[WakeWordService] ✅ Audio input set to default device");
                }
                catch (Exception ex)
                {
                    // Don't fail initialization - we'll retry in StartAsync
                    Debug.WriteLine($"[WakeWordService] ⚠️ Initial audio setup deferred: {ex.Message}");
                    Debug.WriteLine($"[WakeWordService] Will retry audio acquisition in StartAsync()");
                }

                _initializationFailed = false;
                _initializationError = "";
                Debug.WriteLine("[WakeWordService] ✅ Initialization complete");
                Debug.WriteLine("[WakeWordService] ═══════════════════════════════════════");
                StateChanged?.Invoke(this, "Initialized");
            }
            catch (Exception ex)
            {
                _initializationFailed = true;
                _initializationError = $"Wake word initialization failed: {ex.Message}";
                Debug.WriteLine($"[WakeWordService] ❌ {_initializationError}");
                Debug.WriteLine($"[WakeWordService] Stack trace: {ex.StackTrace}");
                Error?.Invoke(this, _initializationError);
            }
        }

        /// <summary>
        /// Start wake word detection
        /// </summary>
        public async Task<bool> StartAsync()
        {
            await _startStopGate.WaitAsync().ConfigureAwait(false);
            try
            {
            Debug.WriteLine("[WakeWordService] ═══════════════════════════════════════");
            Debug.WriteLine("[WakeWordService] StartAsync() called");
            Debug.WriteLine($"[WakeWordService] Thread: {Thread.CurrentThread.ManagedThreadId}");
            Debug.WriteLine($"[WakeWordService] _isDisposed: {_isDisposed}");
            Debug.WriteLine($"[WakeWordService] _initializationFailed: {_initializationFailed}");
            Debug.WriteLine($"[WakeWordService] _isListening: {_isListening}");
            Debug.WriteLine($"[WakeWordService] _recognizer != null: {_recognizer != null}");
            
            // If initialization failed previously, try to reinitialize
            if (_initializationFailed && !_isDisposed)
            {
                Debug.WriteLine($"[WakeWordService] Previous init failed: {_initializationError}");
                Debug.WriteLine("[WakeWordService] Attempting reinitialization...");
                Reinitialize();
            }
            
            if (_isDisposed || _initializationFailed)
            {
                Debug.WriteLine($"[WakeWordService] ❌ Cannot start - disposed: {_isDisposed}, failed: {_initializationFailed}");
                if (_initializationFailed)
                {
                    Debug.WriteLine($"[WakeWordService] Initialization error: {_initializationError}");
                }
                return false;
            }

            if (_isListening)
            {
                Debug.WriteLine("[WakeWordService] Already listening");
                return true;
            }

            if (!CanUseWakeWordWithCurrentMicrophone(out var blockedDeviceName))
            {
                var message = $"Wake word is not reliable on {blockedDeviceName}. Select a wired, USB, or built-in microphone in Settings.";
                Debug.WriteLine($"[WakeWordService] ⚠️ {message}");
                Log($"⚠️ {message}");
                _isListening = false;
                TransitionState(WakeWordState.Idle, "bluetooth mic blocked");
                Error?.Invoke(this, message);
                StateChanged?.Invoke(this, "Bluetooth microphone unsupported for wake word");
                return false;
            }

            try
            {
                // NOTE: VoiceInputManager (NAudio) is NOT started here to avoid microphone conflicts
                // Windows SpeechRecognitionEngine needs exclusive access to the microphone
                Debug.WriteLine("[WakeWordService] Skipping VoiceInputManager (NAudio) to avoid mic conflicts");

                // Start Windows Speech Recognition
                if (_recognizer != null)
                {
                    // CRITICAL: Stop any existing recognition first
                    try
                    {
                        // Stop is more graceful than Cancel and is less likely to leave the engine in a weird state
                        _recognizer.RecognizeAsyncStop();
                        _recognizer.RecognizeAsyncCancel();
                        await Task.Delay(150).ConfigureAwait(false); // Brief pause to release audio device
                    }
                    catch { }
                    
                    // Re-acquire audio device - this is CRITICAL for wake word to work
                    bool audioAcquired = false;
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            Debug.WriteLine($"[WakeWordService] Acquiring audio device (attempt {attempt}/3)...");
                            _recognizer.SetInputToDefaultAudioDevice();
                            audioAcquired = true;
                            Debug.WriteLine("[WakeWordService] ✅ Audio input acquired");
                            break;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WakeWordService] ⚠️ Attempt {attempt} failed: {ex.Message}");
                            if (attempt < 3)
                            {
                                await Task.Delay(200 * attempt); // Increasing delay between retries
                            }
                        }
                    }
                    
                    if (!audioAcquired)
                    {
                        Debug.WriteLine("[WakeWordService] ❌ Failed to acquire audio device after 3 attempts");
                        const string micAccessError = "Could not access microphone. Check if another app is using it.";
                        DisableContinuousListeningForMicFailure("microphone unavailable");
                        Error?.Invoke(this, micAccessError);
                        return false;
                    }
                    
                    Debug.WriteLine("[WakeWordService] Starting recognizer with RecognizeMode.Multiple...");
                    Debug.WriteLine($"[WakeWordService] Recognizer info: {_recognizer.RecognizerInfo.Name}");
                    Debug.WriteLine($"[WakeWordService] Grammars loaded: {_recognizer.Grammars.Count}");
                    
                    // Verify grammars are loaded
                    if (_recognizer.Grammars.Count == 0)
                    {
                        Debug.WriteLine("[WakeWordService] ❌ No grammars loaded! Reinitializing...");
                        Reinitialize();
                        if (_recognizer == null || _recognizer.Grammars.Count == 0)
                        {
                            Error?.Invoke(this, "Failed to load wake word grammars");
                            return false;
                        }
                    }
                    
                    try
                    {
                        _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // If the engine is already recognizing, treat this as a successful start.
                        // This prevents noisy restart loops caused by overlapping callers.
                        Log($"⚠️ RecognizeAsync start raced: {ex.Message}");
                    }
                    _isListening = true;
                    _continuousListeningEnabled = true;
                    var generation = Interlocked.Increment(ref _startGeneration);
                    TransitionState(WakeWordState.Listening, "started");
                    
                    Log("✅ WakeWordService started");
                    Log("✅ Listening for wake words");
                    Log($"Event subscribers: WakeWordDetected={WakeWordDetected?.GetInvocationList().Length ?? 0}");
                    Log("═══════════════════════════════════════");
                    Log("🎤 SAY 'ATLAS' NOW - LISTENING ACTIVE 🎤");
                    Log("═══════════════════════════════════════");
                    
                    VoiceWakeLogger.LogStarted("WakeWordService", "Idle", "Listening");
                    StateChanged?.Invoke(this, "Listening");
                    
                    // Start a background task to verify audio is actually flowing
                    _ = VerifyAudioFlowAsync(generation);
                    
                    // Start the continuous listening loop to prevent "flash then stop"
                    StartContinuousListeningLoop();
                    
                    return true;
                }
                else
                {
                    Log("❌ Recognizer is null! Attempting reinitialization...");
                    Reinitialize();;
                    if (_recognizer != null)
                    {
                        // Retry start after reinitialization
                        return await StartAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Start failed: {ex.Message}");
                if (IsMicrophoneAccessFailure(ex.Message))
                    DisableContinuousListeningForMicFailure("microphone unavailable");
                Error?.Invoke(this, $"Failed to start wake word detection: {ex.Message}");
            }

            Log("═══════════════════════════════════════");
            return false;
            }
            finally
            {
                _startStopGate.Release();
            }
        }
        
        /// <summary>
        /// Verify that audio is actually flowing to the recognizer
        /// </summary>
        private async Task VerifyAudioFlowAsync(int generation)
        {
            await Task.Delay(1000); // Wait 1 second for audio to start flowing
            
            if (!_isListening || _isDisposed || !_continuousListeningEnabled) return;
            if (generation != Volatile.Read(ref _startGeneration)) return;
            
            try
            {
                // Check audio state
                var audioState = _recognizer?.AudioState;
                Log($"Audio verification - State: {audioState}");
                
                if (audioState == AudioState.Stopped)
                {
                    Log("⚠️ Audio state is Stopped - deferring recovery to continuous listening loop");
                }
                else if (audioState == AudioState.Silence)
                {
                    Log("✅ Audio state is Silence - microphone is working, waiting for speech");
                }
                else if (audioState == AudioState.Speech)
                {
                    Log("✅ Audio state is Speech - actively detecting speech");
                }
            }
            catch (Exception ex)
            {
                Log($"Audio verification error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Start the continuous listening loop that ensures wake word detection stays active.
        /// This prevents the "flash then stop" issue by automatically restarting if needed.
        /// </summary>
        private void StartContinuousListeningLoop()
        {
            // Cancel any existing timer
            _ensureListeningTimer?.Dispose();
            _restartAttempts = 0;
            
            Log("Starting continuous listening loop (check every 2s)");
            VoiceWakeLogger.Log("WakeWordService", "ContinuousLoopStarted");
            
            _ensureListeningTimer = new System.Threading.Timer(
                EnsureWakeListeningCallback,
                null,
                RestartCheckIntervalMs,
                RestartCheckIntervalMs);
        }
        
        /// <summary>
        /// Stop the continuous listening loop.
        /// Call this when intentionally stopping wake word (e.g., for Whisper recording).
        /// </summary>
        public void StopContinuousListeningLoop()
        {
            _ensureListeningTimer?.Dispose();
            _ensureListeningTimer = null;
            Log("Continuous listening loop stopped");
            VoiceWakeLogger.Log("WakeWordService", "ContinuousLoopStopped");
        }
        
        /// <summary>
        /// Timer callback that ensures wake word listening is active.
        /// If recognizer stopped unexpectedly, restart it with backoff.
        /// STEP 29: Enhanced logging and persistence fix
        /// </summary>
        private void EnsureWakeListeningCallback(object? state)
        {
            if (_isDisposed || !_continuousListeningEnabled) return;
            
            try
            {
                // Check if we should be listening but aren't
                var audioState = _recognizer?.AudioState;
                var shouldBeListening = _continuousListeningEnabled && !_isDisposed;
                var isActuallyListening = _isListening && audioState != AudioState.Stopped;
                
                // STEP 29: Log the check for debugging
                if (!isActuallyListening && shouldBeListening)
                {
                    StabilizationLogger.LogWakeListeningStop($"AudioState={audioState}, _isListening={_isListening}");
                }
                
                if (shouldBeListening && !isActuallyListening)
                {
                    _restartAttempts++;
                    
                    if (_restartAttempts > MaxRestartAttempts)
                    {
                        Log($"❌ Max restart attempts ({MaxRestartAttempts}) reached - giving up");
                        VoiceWakeLogger.LogStopped("WakeWordService", WakeStopReason.Exception, "MaxRestartsReached");
                        StabilizationLogger.LogEvent("WakeWord", "MaxRestartsReached", reason: $"Failed after {MaxRestartAttempts} attempts");
                        _ensureListeningTimer?.Dispose();
                        _ensureListeningTimer = null;
                        Error?.Invoke(this, "Wake word detection stopped after multiple restart failures");
                        return;
                    }
                    
                    // Calculate backoff delay: 250ms, 500ms, 750ms, 1000ms, 1000ms
                    var backoffMs = Math.Min(250 * _restartAttempts, 1000);
                    
                    Log($"⚠️ Wake listening stopped unexpectedly! Restarting (attempt {_restartAttempts}/{MaxRestartAttempts}, backoff {backoffMs}ms)");
                    VoiceWakeLogger.LogRestart("WakeWordService", _restartAttempts, backoffMs);
                    StabilizationLogger.LogEvent("WakeWord", "AutoRestart", reason: $"Attempt {_restartAttempts}, backoff {backoffMs}ms");
                    
                    // Restart on background thread
                    Task.Run(async () =>
                    {
                        await Task.Delay(backoffMs);
                        if (_isDisposed || !_continuousListeningEnabled) return;
                        
                        try
                        {
                            // Centralize restart logic through StartAsync to avoid engine races
                            _isListening = false;
                            var started = await StartAsync().ConfigureAwait(false);
                            if (started)
                            {
                                TransitionState(WakeWordState.Listening, "auto-restart");
                                StabilizationLogger.LogWakeListeningStart("auto-restart");
                                Log($"✅ Auto-restart successful (attempt {_restartAttempts})");
                                _restartAttempts = 0; // Reset on success
                            }
                            else
                            {
                                Log($"❌ Auto-restart failed (attempt {_restartAttempts})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"❌ Auto-restart failed: {ex.Message}");
                            VoiceWakeLogger.LogError("WakeWordService", ex);
                            StabilizationLogger.LogEvent("WakeWord", "AutoRestartFailed", reason: ex.Message);
                        }
                    });
                }
                else if (isActuallyListening && _restartAttempts > 0)
                {
                    // Reset restart counter if we're successfully listening
                    _restartAttempts = 0;
                }
            }
            catch (Exception ex)
            {
                Log($"EnsureWakeListening error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Reinitialize the recognizer if it failed previously
        /// </summary>
        private void Reinitialize()
        {
            Log("═══════════════════════════════════════");
            Log("REINITIALIZING...");
            
            try
            {
                // Dispose old recognizer
                if (_recognizer != null)
                {
                    try
                    {
                        _recognizer.RecognizeAsyncCancel();
                        _recognizer.SpeechRecognized -= OnSpeechRecognized;
                        _recognizer.SpeechRecognitionRejected -= OnSpeechRejected;
                        _recognizer.RecognizeCompleted -= OnRecognizeCompleted;
                        _recognizer.AudioStateChanged -= OnAudioStateChanged;
                        _recognizer.SpeechDetected -= OnSpeechDetected;
                        _recognizer.SpeechHypothesized -= OnSpeechHypothesized;
                        _recognizer.Dispose();
                    }
                    catch { }
                    _recognizer = null;
                }
                
                // Reset flags
                _initializationFailed = false;
                _initializationError = "";
                _isListening = false;
                
                // Reinitialize
                Initialize();
                
                Debug.WriteLine("[WakeWordService] Reinitialization complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordService] Reinitialization failed: {ex.Message}");
                _initializationFailed = true;
                _initializationError = ex.Message;
            }
            
            Debug.WriteLine("[WakeWordService] ═══════════════════════════════════════");
        }

        private static bool CanUseWakeWordWithCurrentMicrophone(out string blockedDeviceName)
        {
            blockedDeviceName = string.Empty;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var communicationsDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Communications);
                var multimediaDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Multimedia);
                var consoleDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Console);

                var preferredDeviceId = (PreferencesStore.Instance.Current.MicrophoneDeviceId ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(preferredDeviceId) &&
                    !string.Equals(preferredDeviceId, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var selectedDevice = enumerator.GetDevice(preferredDeviceId);
                        if (selectedDevice != null && selectedDevice.State == DeviceState.Active)
                        {
                            blockedDeviceName = selectedDevice.FriendlyName;
                            if (IsWindowsManagedCaptureEndpoint(selectedDevice, communicationsDevice, multimediaDevice, consoleDevice))
                                return true;

                            return !IsBluetoothStyleMicrophone(blockedDeviceName);
                        }
                    }
                    catch
                    {
                    }
                }

                var fallbackDevice = communicationsDevice
                    ?? multimediaDevice
                    ?? consoleDevice;

                blockedDeviceName = fallbackDevice?.FriendlyName ?? "the current microphone";
                if (IsWindowsManagedCaptureEndpoint(fallbackDevice, communicationsDevice, multimediaDevice, consoleDevice))
                    return true;

                return !IsBluetoothStyleMicrophone(blockedDeviceName);
            }
            catch
            {
                blockedDeviceName = "the current microphone";
                return true;
            }
        }

        private static MMDevice? TryGetDefaultCaptureEndpoint(MMDeviceEnumerator enumerator, Role role)
        {
            try
            {
                var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                if (endpoint != null && endpoint.State == DeviceState.Active)
                    return endpoint;
            }
            catch
            {
            }

            return null;
        }

        private static bool IsWindowsManagedCaptureEndpoint(MMDevice? device, params MMDevice?[] defaultEndpoints)
        {
            if (device == null)
                return false;

            foreach (var endpoint in defaultEndpoints)
            {
                if (endpoint != null && string.Equals(endpoint.ID, device.ID, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsBluetoothStyleMicrophone(string? deviceName)
        {
            var value = (deviceName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("airpod") ||
                   value.Contains("bluetooth") ||
                   value.Contains("wireless") ||
                   value.Contains("hands-free") ||
                   value.Contains("handsfree") ||
                   value.Contains("headset");
        }

        private static bool IsMicrophoneAccessFailure(string? message)
        {
            var text = (message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf("could not access microphone", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("another app is using it", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("audio device", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("device in use", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DisableContinuousListeningForMicFailure(string reason)
        {
            _continuousListeningEnabled = false;
            _isListening = false;
            StopContinuousListeningLoop();
            TransitionState(WakeWordState.Idle, reason);
            Log($"⚠️ Continuous listening disabled after microphone access failure: {reason}");
        }

        /// <summary>
        /// Stop wake word detection
        /// </summary>
        /// <param name="reason">Why wake word is being stopped</param>
        public void Stop(WakeStopReason reason = WakeStopReason.Unknown)
        {
            // Ensure StartAsync/auto-restart cannot run while we stop
            _startStopGate.Wait();
            try
            {
                if (!_isListening) return;

                try
                {
                    Log($"Stopping wake word detection (reason: {reason})");
                    VoiceWakeLogger.LogStopped("WakeWordService", reason, _currentState.ToString());

                    // Stop the continuous listening loop if this is an intentional stop
                    if (reason == WakeStopReason.WhisperTakeover ||
                        reason == WakeStopReason.ExternalHandlerDefer ||
                        reason == WakeStopReason.UserDisabled)
                    {
                        _continuousListeningEnabled = false;
                        StopContinuousListeningLoop();
                    }

                    try { _recognizer?.RecognizeAsyncStop(); } catch { }
                    try { _recognizer?.RecognizeAsyncCancel(); } catch { }
                    try { _recognizer?.SetInputToNull(); } catch { }

                    _isListening = false;
                    Interlocked.Increment(ref _startGeneration);
                    _vadActive = false;
                    _vadBuffer.Clear();

                    // Cancel cooldown timer
                    _cooldownTimer?.Dispose();
                    _cooldownTimer = null;

                    TransitionState(WakeWordState.Idle, $"stopped ({reason})");
                    Log("Stopped listening");
                    StateChanged?.Invoke(this, "Stopped");
                }
                catch (Exception ex)
                {
                    Log($"Stop error: {ex.Message}");
                    VoiceWakeLogger.LogError("WakeWordService", ex);
                }
            }
            finally
            {
                _startStopGate.Release();
            }
        }
        
        /// <summary>
        /// Stop wake word detection (legacy overload)
        /// </summary>
        public void Stop()
        {
            Stop(WakeStopReason.Unknown);
        }

        private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            Log("═══════════════════════════════════════");
            Log("*** OnSpeechRecognized() FIRED! ***");
            Log($"_isListening: {_isListening}");
            
            if (!_isListening)
            {
                Log("❌ Not listening - ignoring");
                return;
            }

            var text = e.Result.Text;
            var confidence = e.Result.Confidence;
            
            Log($"✅ Recognized: '{text}' (confidence: {confidence:P0})");
            Log($"Grammar: {e.Result.Grammar?.Name ?? "Unknown"}");

            // Only treat recognition from the wake-word grammar as a candidate.
            if (!string.Equals(e.Result.Grammar?.Name, "WakeWords", StringComparison.Ordinal))
            {
                Log("Not wake-word grammar - ignoring");
                return;
            }
            
            // Log all alternates for debugging
            for (int i = 0; i < Math.Min(5, e.Result.Alternates.Count); i++)
            {
                Log($"  Alt {i}: '{e.Result.Alternates[i].Text}' ({e.Result.Alternates[i].Confidence:P0})");
            }

            // Check cooldown first
            if (CheckCooldown())
            {
                Log($"REJECTED: '{text}' - in cooldown");
                return;
            }

            // Check the main result first
            var normalized = NormalizeWakeWord(text);
            Log($"Normalized: '{normalized}'");
            
            bool isWakeWord = IsWakeWordMatch(normalized);
            string matchedText = text;
            double matchedConfidence = confidence;
            
            // If main result isn't a wake word, check ALL alternates
            if (!isWakeWord && e.Result.Alternates.Count > 0)
            {
                foreach (var alt in e.Result.Alternates)
                {
                    var altNormalized = NormalizeWakeWord(alt.Text);
                    if (IsWakeWordMatch(altNormalized))
                    {
                        Log($"✅ Found wake word in alternate: '{alt.Text}'");
                        isWakeWord = true;
                        normalized = altNormalized;
                        matchedText = alt.Text;
                        matchedConfidence = alt.Confidence;
                        break;
                    }
                }
            }

            var minConfidence = GetMinConfidence();
            if (isWakeWord && matchedConfidence < minConfidence)
            {
                Log($"REJECTED: '{matchedText}' - below confidence gate ({matchedConfidence:P0} < {minConfidence:P0})");
                return;
            }

            if (isWakeWord)
            {
                // Confidence-gated wake word.
                
                // Transition to Triggered state
                TransitionState(WakeWordState.Triggered, "wake word detected");
                
                _lastTrigger = DateTime.Now;
                
                Log("*** WAKE WORD DETECTED! ***");
                Log($"Text: '{matchedText}', Normalized: '{normalized}', Confidence: {matchedConfidence:P0}");
                Log($"Firing WakeWordDetected event (subscribers: {WakeWordDetected?.GetInvocationList().Length ?? 0})");
                
                // Play optional audio cue
                var prefs = Core.PreferencesStore.Instance.Current;
                if (prefs.EnableWakeWordAudioCue)
                {
                    WakeWordAudioCues.Instance.Enabled = true;
                    WakeWordAudioCues.Instance.PlayAcknowledgment();
                }
                
                WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs(matchedText, normalized, matchedConfidence));
                Log("✅ WakeWordDetected event fired");
                
                // Start cooldown period
                StartCooldown();
            }
            else
            {
                Log($"Not a wake word: '{text}'");
            }
        }

        private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
        {
            // Log rejected speech for debugging
            if (e.Result?.Alternates.Count > 0)
            {
                var topAlternate = e.Result.Alternates[0];
                Log($"⚠️ Rejected: '{topAlternate.Text}' (confidence: {topAlternate.Confidence:P0})");
            }
        }

        private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs e)
        {
            Log($"🎤 Audio state: {e.AudioState}");
            
            if (e.AudioState == AudioState.Stopped)
            {
                Log("⚠️ Audio stopped - microphone may not be working!");
            }
        }

        private void OnSpeechDetected(object? sender, SpeechDetectedEventArgs e)
        {
            Log($"🗣️ Speech detected at: {e.AudioPosition}");
        }

        private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
        {
            var text = e.Result.Text;
            var confidence = e.Result.Confidence;
            
            Log($"💭 Hypothesis: '{text}' ({confidence:P0})");
            
            // Do not trigger wake word from hypotheses. Hypotheses are extremely noisy and
            // are the main cause of false activations.
        }

        private double GetMinConfidence()
        {
            // Sensitivity: 0 = strict, 1 = permissive.
            // Keep strict to prevent false triggers from ambient noise.
            // Raised from 0.78 base to 0.82 — genuine "Atlas" typically hits 80-94%.
            return Math.Clamp(0.82 - (_sensitivity * 0.18), 0.60, 0.85);
        }

        private double GetMinHypothesisConfidence()
        {
            // Hypotheses are noisier than final recognition.
            return Math.Clamp(GetMinConfidence() + 0.08, 0.45, 0.85);
        }

        private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
        {
            // Restart recognition if still listening
            if (_isListening && !_isDisposed && _recognizer != null)
            {
                Task.Delay(100).ContinueWith(_ =>
                {
                    if (_isListening && !_isDisposed && _recognizer != null)
                    {
                        try
                        {
                            _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WakeWordService] Restart recognition failed: {ex.Message}");
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Fallback VAD + STT for when Windows Speech Recognition isn't working well
        /// </summary>
        private void OnAudioFrame(object? sender, AudioFrameEventArgs e)
        {
            if (!_isListening || _isDisposed) return;

            // Simple VAD: calculate RMS and compare to noise gate
            var rms = CalculateRms(e.Samples);
            var rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-10)); // Avoid log(0)

            if (rmsDb > _noiseGateDb)
            {
                if (!_vadActive)
                {
                    // Voice activity started
                    _vadActive = true;
                    _vadStartTime = DateTime.Now;
                    _vadBuffer.Clear();
                    Debug.WriteLine($"[WakeWordService] VAD triggered (RMS: {rmsDb:F1} dB)");
                }

                // Accumulate audio for potential STT
                _vadBuffer.AddRange(e.Samples);
            }
            else if (_vadActive)
            {
                // Check if we should process the accumulated audio
                var vadDuration = DateTime.Now - _vadStartTime;
                if (vadDuration.TotalMilliseconds > 500 && vadDuration.TotalMilliseconds < VadTimeoutMs)
                {
                    // Process accumulated audio with simple keyword detection
                    ProcessVadBuffer();
                }

                _vadActive = false;
                _vadBuffer.Clear();
            }

            // Timeout VAD if it's been active too long
            if (_vadActive && (DateTime.Now - _vadStartTime).TotalMilliseconds > VadTimeoutMs)
            {
                _vadActive = false;
                _vadBuffer.Clear();
            }
        }

        private void ProcessVadBuffer()
        {
            // Simple keyword detection fallback
            // In a real implementation, this would use STT on the buffered audio
            // For now, we'll use a simple heuristic based on audio characteristics
            
            if (_vadBuffer.Count < 8000) return; // Need at least 0.5s of audio at 16kHz

            // Check debounce
            var timeSinceLastTrigger = DateTime.Now - _lastTrigger;
            if (timeSinceLastTrigger.TotalSeconds < DebounceSeconds) return;

            // Simple heuristic: if we have sustained voice activity in the right duration range
            var duration = _vadBuffer.Count / 16000.0; // Duration in seconds
            if (duration >= 0.5 && duration <= 2.0)
            {
                // This is a fallback - in production you'd run actual STT here
                Debug.WriteLine($"[WakeWordService] VAD fallback triggered (duration: {duration:F1}s)");
                
                // For now, trigger with lower confidence to indicate this is a fallback detection
                _lastTrigger = DateTime.Now;
                WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs("Atlas (VAD)", "atlas", 0.3));
            }
        }

        private static float CalculateRms(float[] samples)
        {
            if (samples.Length == 0) return 0f;

            double sum = 0;
            foreach (var sample in samples)
            {
                sum += sample * sample;
            }
            return (float)Math.Sqrt(sum / samples.Length);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();

            try
            {
                _cooldownTimer?.Dispose();
                _cooldownTimer = null;
                _recognizer?.Dispose();
                _recognizer = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordService] Dispose error: {ex.Message}");
            }

            Debug.WriteLine("[WakeWordService] Disposed");
        }

        /// <summary>
        /// Get diagnostic information for voice debugging
        /// </summary>
        public VoiceDiagnostics GetDiagnostics()
        {
            var diag = new VoiceDiagnostics
            {
                IsInitialized = IsInitialized,
                IsListening = IsListening,
                InitializationError = InitializationError,
                CurrentState = CurrentState.ToString(),
                LastTriggerTime = LastTriggerTime,
                IsInCooldown = IsInCooldown,
                ContinuousListeningEnabled = ContinuousListeningEnabled
            };

            // Get installed recognizers
            try
            {
                var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                diag.InstalledRecognizers = recognizers.Count;
                diag.RecognizerNames = recognizers
                    .Select(r => $"{r.Name} ({r.Culture.Name})")
                    .ToList();
            }
            catch (Exception ex)
            {
                diag.InstalledRecognizers = 0;
                diag.RecognizerNames = new List<string> { $"Error: {ex.Message}" };
            }

            // Get current recognizer info
            if (_recognizer != null && _recognizer.RecognizerInfo != null)
            {
                diag.ActiveRecognizer = $"{_recognizer.RecognizerInfo.Name} ({_recognizer.RecognizerInfo.Culture.Name})";
                diag.AudioState = _recognizer.AudioState.ToString();
                diag.AudioFormat = _recognizer.AudioFormat != null 
                    ? $"{_recognizer.AudioFormat.SamplesPerSecond}Hz, {_recognizer.AudioFormat.BitsPerSample}-bit"
                    : "Unknown";
            }
            else
            {
                diag.ActiveRecognizer = "None";
                diag.AudioState = "Not initialized";
                diag.AudioFormat = "Unknown";
            }

            return diag;
        }
    }

    /// <summary>
    /// Wake word detection event arguments
    /// </summary>
    public class WakeWordDetectedEventArgs : EventArgs
    {
        public string Text { get; }
        public string NormalizedText { get; }
        public double Confidence { get; }
        public DateTime Timestamp { get; }

        public WakeWordDetectedEventArgs(string text, string normalizedText, double confidence)
        {
            Text = text;
            NormalizedText = normalizedText;
            Confidence = confidence;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Voice diagnostics information for debugging
    /// </summary>
    public class VoiceDiagnostics
    {
        public bool IsInitialized { get; set; }
        public bool IsListening { get; set; }
        public string InitializationError { get; set; } = "";
        public string CurrentState { get; set; } = "";
        public DateTime LastTriggerTime { get; set; }
        public bool IsInCooldown { get; set; }
        public bool ContinuousListeningEnabled { get; set; }

        public int InstalledRecognizers { get; set; }
        public List<string> RecognizerNames { get; set; } = new();
        public string ActiveRecognizer { get; set; } = "";
        public string AudioState { get; set; } = "";
        public string AudioFormat { get; set; } = "";
    }
}
