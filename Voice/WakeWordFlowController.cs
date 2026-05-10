using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AtlasAI.Core;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Wake word flow states
    /// </summary>
    public enum WakeWordFlowState
    {
        /// <summary>Passively listening for wake word</summary>
        Idle,
        
        /// <summary>Wake word detected, waiting for command</summary>
        Listening,
        
        /// <summary>Processing user command</summary>
        Processing,
        
        /// <summary>Speaking response</summary>
        Speaking,
        
        /// <summary>Follow-up listening window (after response)</summary>
        FollowUp
    }

    /// <summary>
    /// Controls wake word flow consistently across Chat window and Orb.
    /// 
    /// Key behaviors:
    /// - Wake word does NOT auto-open Chat window (per hard constraint)
    /// - Wake word activates listening mode in whichever surface is active
    /// - Follow-up listening allows continued conversation without re-triggering wake word
    /// - Deterministic state transitions with logging
    /// </summary>
    public class WakeWordFlowController
    {
        private static WakeWordFlowController? _instance;
        private static readonly object _lock = new();

        public static WakeWordFlowController Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new WakeWordFlowController();
                    }
                }
                return _instance;
            }
        }

        // State
        private WakeWordFlowState _currentState = WakeWordFlowState.Idle;
        private DateTime _lastWakeWordTime = DateTime.MinValue;
        private DateTime _lastResponseTime = DateTime.MinValue;
        private CancellationTokenSource? _followUpCts;
        
        // Configuration
        private TimeSpan _followUpDuration = TimeSpan.FromSeconds(4);
        private TimeSpan _wakeWordCooldown = TimeSpan.FromMilliseconds(1500);
        
        // Events
        public event EventHandler<WakeWordFlowState>? StateChanged;
        public event EventHandler? ListeningStarted;
        public event EventHandler? ListeningStopped;
        public event EventHandler<string>? CommandReceived;
        public event EventHandler? FollowUpExpired;

        public WakeWordFlowState CurrentState => _currentState;
        public bool IsListening => _currentState == WakeWordFlowState.Listening || _currentState == WakeWordFlowState.FollowUp;

        private WakeWordFlowController()
        {
            LoadSettings();
            Debug.WriteLine("[WakeWordFlow] Controller initialized");
        }

        /// <summary>
        /// Load settings from UserPreferences
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                _followUpDuration = TimeSpan.FromSeconds(prefs.FollowUpListeningDuration);
                _wakeWordCooldown = TimeSpan.FromMilliseconds(prefs.WakeWordCooldownMs);
                Debug.WriteLine($"[WakeWordFlow] Settings: followUp={_followUpDuration.TotalSeconds}s, cooldown={_wakeWordCooldown.TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordFlow] Settings load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Configure follow-up duration (how long to listen after response)
        /// </summary>
        public void SetFollowUpDuration(TimeSpan duration)
        {
            _followUpDuration = duration;
            Debug.WriteLine($"[WakeWordFlow] Follow-up duration set to {duration.TotalSeconds}s");
        }

        /// <summary>
        /// Configure wake word cooldown
        /// </summary>
        public void SetWakeWordCooldown(TimeSpan cooldown)
        {
            _wakeWordCooldown = cooldown;
            Debug.WriteLine($"[WakeWordFlow] Wake word cooldown set to {cooldown.TotalMilliseconds}ms");
        }

        /// <summary>
        /// Handle wake word detection.
        /// Does NOT open Chat window (per hard constraint).
        /// </summary>
        public void OnWakeWordDetected(string wakeWord, double confidence)
        {
            // Check cooldown
            if (DateTime.Now - _lastWakeWordTime < _wakeWordCooldown)
            {
                Debug.WriteLine($"[WakeWordFlow] Wake word ignored (cooldown): {wakeWord}");
                return;
            }

            _lastWakeWordTime = DateTime.Now;
            Debug.WriteLine($"[WakeWordFlow] Wake word detected: '{wakeWord}' ({confidence:P0})");

            // Transition to Listening state
            TransitionTo(WakeWordFlowState.Listening);
            
            // Play audio cue
            try
            {
                AudioCueService.Instance.PlayCue(AudioCueService.CueType.WakeWordDetected);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordFlow] Audio cue error: {ex.Message}");
            }

            // Notify listeners
            ListeningStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Handle user command received (after wake word or during follow-up)
        /// </summary>
        public void OnCommandReceived(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                Debug.WriteLine("[WakeWordFlow] Empty command ignored");
                return;
            }

            Debug.WriteLine($"[WakeWordFlow] Command received: '{command}'");
            
            // Cancel any follow-up timer
            _followUpCts?.Cancel();
            
            // Transition to Processing
            TransitionTo(WakeWordFlowState.Processing);
            
            // Notify listeners
            CommandReceived?.Invoke(this, command);
        }

        /// <summary>
        /// Handle response started (TTS speaking)
        /// </summary>
        public void OnResponseStarted()
        {
            Debug.WriteLine("[WakeWordFlow] Response started (speaking)");
            TransitionTo(WakeWordFlowState.Speaking);
        }

        /// <summary>
        /// Handle response completed (TTS finished)
        /// </summary>
        public void OnResponseCompleted()
        {
            _lastResponseTime = DateTime.Now;
            Debug.WriteLine("[WakeWordFlow] Response completed");
            
            // Start follow-up listening window
            StartFollowUpWindow();
        }

        /// <summary>
        /// Cancel current operation and return to Idle
        /// </summary>
        public void Cancel()
        {
            Debug.WriteLine("[WakeWordFlow] Cancelled");
            _followUpCts?.Cancel();
            TransitionTo(WakeWordFlowState.Idle);
            ListeningStopped?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Force return to Idle state
        /// </summary>
        public void Reset()
        {
            Debug.WriteLine("[WakeWordFlow] Reset to Idle");
            _followUpCts?.Cancel();
            _currentState = WakeWordFlowState.Idle;
            StateChanged?.Invoke(this, WakeWordFlowState.Idle);
        }

        private void TransitionTo(WakeWordFlowState newState)
        {
            if (_currentState == newState) return;
            
            var oldState = _currentState;
            _currentState = newState;
            
            Debug.WriteLine($"[WakeWordFlow] State: {oldState} → {newState}");
            
            // STEP 29: Stabilization logging
            StabilizationLogger.LogWakeStateTransition(oldState.ToString(), newState.ToString(), "FlowController");
            
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                StateChanged?.Invoke(this, newState);
            });
        }

        private void StartFollowUpWindow()
        {
            // Cancel any existing follow-up timer
            _followUpCts?.Cancel();
            _followUpCts = new CancellationTokenSource();
            
            TransitionTo(WakeWordFlowState.FollowUp);
            Debug.WriteLine($"[WakeWordFlow] Follow-up window started ({_followUpDuration.TotalSeconds}s)");
            
            // Start timer
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_followUpDuration, _followUpCts.Token);
                    
                    // Follow-up expired
                    Debug.WriteLine("[WakeWordFlow] Follow-up window expired");
                    TransitionTo(WakeWordFlowState.Idle);
                    
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        FollowUpExpired?.Invoke(this, EventArgs.Empty);
                        ListeningStopped?.Invoke(this, EventArgs.Empty);
                    });
                }
                catch (TaskCanceledException)
                {
                    // Follow-up was cancelled (new command received)
                    Debug.WriteLine("[WakeWordFlow] Follow-up cancelled (new command)");
                }
            });
        }

        /// <summary>
        /// Check if we should accept voice input (wake word detected or in follow-up)
        /// </summary>
        public bool ShouldAcceptVoiceInput()
        {
            return _currentState == WakeWordFlowState.Listening || 
                   _currentState == WakeWordFlowState.FollowUp;
        }

        /// <summary>
        /// Get status text for UI display
        /// </summary>
        public string GetStatusText()
        {
            return _currentState switch
            {
                WakeWordFlowState.Idle => "Idle",
                WakeWordFlowState.Listening => "Listening",
                WakeWordFlowState.Processing => "Processing",
                WakeWordFlowState.Speaking => "Speaking",
                WakeWordFlowState.FollowUp => "Listening",
                _ => ""
            };
        }
    }
}
