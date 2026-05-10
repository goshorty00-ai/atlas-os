using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Simplified voice state enum.
    /// Requested for command-mode orchestration and echo suppression.
    /// </summary>
    public enum VoiceState
    {
        Idle,
        Listening,
        Thinking,
        Speaking
    }

    /// <summary>
    /// Simplified command-mode state (requested by VoiceCommandMode)
    /// </summary>
    public enum VoiceCommandModeState
    {
        Idle,
        Listening,
        Thinking,
        Speaking
    }

    /// <summary>
    /// Voice system states for user trust and UI feedback
    /// </summary>
    public enum VoiceSystemState
    {
        /// <summary>Voice system is disabled</summary>
        Disabled,
        
        /// <summary>Listening for wake word only</summary>
        PassiveListening,
        
        /// <summary>Wake word detected, actively capturing command</summary>
        ActiveListening,
        
        /// <summary>Processing captured command</summary>
        Processing,
        
        /// <summary>Speaking response</summary>
        Speaking,
        
        /// <summary>Listening for follow-up command (no wake word required)</summary>
        FollowUpListening,
        
        /// <summary>Suspended due to error or user action</summary>
        Suspended
    }

    /// <summary>
    /// Voice system state manager with UI binding support
    /// </summary>
    public class VoiceStateManager : INotifyPropertyChanged
    {
        private static VoiceStateManager? _instance;
        private static readonly object _lock = new object();

        public static VoiceStateManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VoiceStateManager();
                    }
                }
                return _instance;
            }
        }

        private VoiceSystemState _currentState = VoiceSystemState.Disabled;
        private string _statusMessage = "Voice system disabled";
        private DateTime _stateChangeTime = DateTime.Now;
        private VoiceCommandModeState _commandModeState = VoiceCommandModeState.Idle;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<VoiceSystemState>? StateChanged;
        public event EventHandler<VoiceCommandModeState>? CommandModeStateChanged;

        /// <summary>
        /// Current voice system state
        /// </summary>
        public VoiceSystemState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    var previousState = _currentState;
                    _currentState = value;
                    _stateChangeTime = DateTime.Now;
                    
                    // Update status message
                    StatusMessage = GetStatusMessage(value);
                    
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsListening));
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(CanSpeak));
                    UpdateCommandModeState();
                    
                    StateChanged?.Invoke(this, value);
                    
                    System.Diagnostics.Debug.WriteLine($"[VoiceStateManager] State: {previousState} → {value}");
                }
            }
        }

        /// <summary>
        /// Simplified voice command mode state (Idle/Listening/Thinking/Speaking)
        /// </summary>
        public VoiceCommandModeState CommandModeState => _commandModeState;

        /// <summary>
        /// Human-readable status message
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Time when current state was entered
        /// </summary>
        public DateTime StateChangeTime => _stateChangeTime;

        /// <summary>
        /// Whether the system is actively listening (passive, active, or follow-up)
        /// </summary>
        public bool IsListening => _currentState == VoiceSystemState.PassiveListening || 
                                   _currentState == VoiceSystemState.ActiveListening ||
                                   _currentState == VoiceSystemState.FollowUpListening;

        /// <summary>
        /// Whether the system is in an active state (not disabled/suspended)
        /// </summary>
        public bool IsActive => _currentState != VoiceSystemState.Disabled && 
                               _currentState != VoiceSystemState.Suspended;

        /// <summary>
        /// Whether the system can speak (not already speaking or processing)
        /// </summary>
        public bool CanSpeak => _currentState != VoiceSystemState.Speaking && 
                               _currentState != VoiceSystemState.Disabled &&
                               _currentState != VoiceSystemState.Suspended;

        private VoiceStateManager()
        {
        }

        private void UpdateCommandModeState()
        {
            var next = _currentState switch
            {
                VoiceSystemState.ActiveListening => VoiceCommandModeState.Listening,
                VoiceSystemState.FollowUpListening => VoiceCommandModeState.Listening,
                VoiceSystemState.Processing => VoiceCommandModeState.Thinking,
                VoiceSystemState.Speaking => VoiceCommandModeState.Speaking,
                _ => VoiceCommandModeState.Idle
            };

            if (_commandModeState != next)
            {
                _commandModeState = next;
                OnPropertyChanged(nameof(CommandModeState));
                CommandModeStateChanged?.Invoke(this, next);
            }
        }

        /// <summary>
        /// Transition to a new state
        /// </summary>
        public void SetState(VoiceSystemState newState, string? customMessage = null)
        {
            CurrentState = newState;
            if (!string.IsNullOrEmpty(customMessage))
            {
                StatusMessage = customMessage;
            }
        }

        /// <summary>
        /// Enable voice system (transition to PassiveListening)
        /// </summary>
        public void Enable()
        {
            if (_currentState == VoiceSystemState.Disabled || _currentState == VoiceSystemState.Suspended)
            {
                CurrentState = VoiceSystemState.PassiveListening;
            }
        }

        /// <summary>
        /// Disable voice system
        /// </summary>
        public void Disable()
        {
            CurrentState = VoiceSystemState.Disabled;
        }

        /// <summary>
        /// Suspend with reason
        /// </summary>
        public void Suspend(string reason)
        {
            CurrentState = VoiceSystemState.Suspended;
            StatusMessage = $"Suspended: {reason}";
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "logs", "voice_error.log");
                var dir = System.IO.Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SUSPEND: {reason}{Environment.NewLine}" +
                    $"  Stack: {Environment.StackTrace}{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>
        /// Wake word detected - transition to active listening
        /// </summary>
        public void WakeWordDetected()
        {
            if (_currentState == VoiceSystemState.PassiveListening)
            {
                CurrentState = VoiceSystemState.ActiveListening;
            }
        }

        /// <summary>
        /// Command captured - transition to processing
        /// </summary>
        public void StartProcessing()
        {
            if (_currentState == VoiceSystemState.ActiveListening)
            {
                CurrentState = VoiceSystemState.Processing;
            }
        }

        /// <summary>
        /// Start speaking response
        /// </summary>
        public void StartSpeaking()
        {
            if (_currentState == VoiceSystemState.Processing || _currentState == VoiceSystemState.ActiveListening)
            {
                CurrentState = VoiceSystemState.Speaking;
            }
        }

        /// <summary>
        /// Finished speaking - transition to follow-up listening
        /// </summary>
        public void FinishedSpeaking()
        {
            if (_currentState == VoiceSystemState.Speaking)
            {
                CurrentState = VoiceSystemState.FollowUpListening;
            }
        }

        /// <summary>
        /// Follow-up listening timeout - return to passive listening
        /// </summary>
        public void FollowUpTimeout()
        {
            if (_currentState == VoiceSystemState.FollowUpListening)
            {
                CurrentState = VoiceSystemState.PassiveListening;
            }
        }

        /// <summary>
        /// Follow-up command captured - transition to processing
        /// </summary>
        public void FollowUpCaptured()
        {
            if (_currentState == VoiceSystemState.FollowUpListening)
            {
                CurrentState = VoiceSystemState.Processing;
            }
        }

        /// <summary>
        /// Command processing completed without speech - return to passive listening
        /// </summary>
        public void FinishedProcessing()
        {
            if (_currentState == VoiceSystemState.Processing)
            {
                CurrentState = VoiceSystemState.PassiveListening;
            }
        }

        /// <summary>
        /// Timeout or error during active listening - return to passive
        /// </summary>
        public void TimeoutActiveListening()
        {
            if (_currentState == VoiceSystemState.ActiveListening)
            {
                CurrentState = VoiceSystemState.PassiveListening;
            }
        }

        private static string GetStatusMessage(VoiceSystemState state)
        {
            return state switch
            {
                VoiceSystemState.Disabled => "Voice system disabled",
                VoiceSystemState.PassiveListening => "Listening",
                VoiceSystemState.ActiveListening => "Listening",
                VoiceSystemState.Processing => "Processing command...",
                VoiceSystemState.Speaking => "Speaking...",
                VoiceSystemState.FollowUpListening => "Listening",
                VoiceSystemState.Suspended => "Voice system suspended",
                _ => "Unknown state"
            };
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
