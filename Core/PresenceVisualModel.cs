using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtlasAI.Core
{
    /// <summary>
    /// Presence states for the "alive computer" layer
    /// </summary>
    public enum PresenceState
    {
        Idle,       // No focus, no recent input
        Attentive,  // Window focused or user interacting
        Thinking,   // AI pipeline busy
        Working,    // Executing safe read-only tasks
        Error       // Non-fatal error occurred
    }

    /// <summary>
    /// Bindable visual model for presence animations.
    /// Single source of truth - all UI elements bind to these normalized (0..1) values.
    /// No direct UI manipulation from business logic.
    /// </summary>
    public class PresenceVisualModel : INotifyPropertyChanged
    {
        private static PresenceVisualModel? _instance;
        private static readonly object _lock = new object();

        public static PresenceVisualModel Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new PresenceVisualModel();
                    }
                }
                return _instance;
            }
        }

        private PresenceVisualModel() { }

        // === State ===
        private PresenceState _currentState = PresenceState.Idle;
        public PresenceState CurrentState
        {
            get => _currentState;
            set { if (_currentState != value) { _currentState = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
        }

        // === Normalized Visual Values (0..1) ===

        private double _pulseRate = 0.3;
        /// <summary>Pulse frequency multiplier (0=stopped, 1=fast)</summary>
        public double PulseRate
        {
            get => _pulseRate;
            set { if (Math.Abs(_pulseRate - value) > 0.001) { _pulseRate = Clamp01(value); OnPropertyChanged(); } }
        }

        private double _pulseAmplitude = 0.2;
        /// <summary>Pulse intensity (0=none, 1=full)</summary>
        public double PulseAmplitude
        {
            get => _pulseAmplitude;
            set { if (Math.Abs(_pulseAmplitude - value) > 0.001) { _pulseAmplitude = Clamp01(value); OnPropertyChanged(); } }
        }

        private double _coreGlow = 0.3;
        /// <summary>Core glow intensity (0=dim, 1=bright)</summary>
        public double CoreGlow
        {
            get => _coreGlow;
            set { if (Math.Abs(_coreGlow - value) > 0.001) { _coreGlow = Clamp01(value); OnPropertyChanged(); } }
        }

        private double _coreAccent = 0.0;
        /// <summary>Orange accent intensity (0=none/blue, 1=full orange) - only >0 in Thinking state</summary>
        public double CoreAccent
        {
            get => _coreAccent;
            set { if (Math.Abs(_coreAccent - value) > 0.001) { _coreAccent = Clamp01(value); OnPropertyChanged(); } }
        }

        private double _ringSpeed = 0.2;
        /// <summary>Ring rotation speed (0=stopped, 1=fast)</summary>
        public double RingSpeed
        {
            get => _ringSpeed;
            set { if (Math.Abs(_ringSpeed - value) > 0.001) { _ringSpeed = Clamp01(value); OnPropertyChanged(); } }
        }

        private double _noiseJitter = 0.0;
        /// <summary>Noise/jitter amount for organic feel (0=smooth, 1=jittery)</summary>
        public double NoiseJitter
        {
            get => _noiseJitter;
            set { if (Math.Abs(_noiseJitter - value) > 0.001) { _noiseJitter = Clamp01(value); OnPropertyChanged(); } }
        }

        private double _attentionLevel = 0.0;
        /// <summary>Attention indicator (0=not attending, 1=fully focused)</summary>
        public double AttentionLevel
        {
            get => _attentionLevel;
            set { if (Math.Abs(_attentionLevel - value) > 0.001) { _attentionLevel = Clamp01(value); OnPropertyChanged(); } }
        }

        private double _voiceAmplitude = 0.0;
        /// <summary>Voice/TTS amplitude (0=silent, 1=loud) - for HoloCore particle synthesis</summary>
        public double VoiceAmplitude
        {
            get => _voiceAmplitude;
            set { if (Math.Abs(_voiceAmplitude - value) > 0.001) { _voiceAmplitude = Clamp01(value); OnPropertyChanged(); } }
        }

        private bool _isSpeaking = false;
        /// <summary>Whether TTS is currently playing</summary>
        public bool IsSpeaking
        {
            get => _isSpeaking;
            set { if (_isSpeaking != value) { _isSpeaking = value; OnPropertyChanged(); } }
        }

        private bool _isWorkflowActive = false;
        /// <summary>Whether a workflow chain is currently active (for HUD pulse)</summary>
        public bool IsWorkflowActive
        {
            get => _isWorkflowActive;
            set { if (_isWorkflowActive != value) { _isWorkflowActive = value; OnPropertyChanged(); } }
        }

        // === Derived Properties ===

        /// <summary>Status text for display</summary>
        public string StatusText => _currentState switch
        {
            PresenceState.Idle => "IDLE",
            PresenceState.Attentive => "ATTENTIVE",
            PresenceState.Thinking => "THINKING",
            PresenceState.Working => "WORKING",
            PresenceState.Error => "ERROR",
            _ => "UNKNOWN"
        };

        // === Target Values (for smooth interpolation) ===
        // These are the "goal" values that the animation loop lerps toward

        internal double TargetPulseRate { get; set; } = 0.3;
        internal double TargetPulseAmplitude { get; set; } = 0.2;
        internal double TargetCoreGlow { get; set; } = 0.3;
        internal double TargetCoreAccent { get; set; } = 0.0;
        internal double TargetRingSpeed { get; set; } = 0.2;
        internal double TargetNoiseJitter { get; set; } = 0.0;
        internal double TargetAttentionLevel { get; set; } = 0.0;

        /// <summary>
        /// Apply smooth interpolation toward target values
        /// </summary>
        /// <param name="smoothing">Smoothing factor (0.05-0.2 typical)</param>
        internal void LerpTowardTargets(double smoothing)
        {
            PulseRate = Lerp(PulseRate, TargetPulseRate, smoothing);
            PulseAmplitude = Lerp(PulseAmplitude, TargetPulseAmplitude, smoothing);
            CoreGlow = Lerp(CoreGlow, TargetCoreGlow, smoothing);
            CoreAccent = Lerp(CoreAccent, TargetCoreAccent, smoothing);
            RingSpeed = Lerp(RingSpeed, TargetRingSpeed, smoothing);
            NoiseJitter = Lerp(NoiseJitter, TargetNoiseJitter, smoothing);
            AttentionLevel = Lerp(AttentionLevel, TargetAttentionLevel, smoothing);
        }

        // === Helpers ===

        private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));
        private static double Lerp(double current, double target, double t) => current + (target - current) * t;

        // === INotifyPropertyChanged ===

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
