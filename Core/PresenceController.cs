using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AtlasAI.Voice;

namespace AtlasAI.Core
{
    /// <summary>
    /// Presence Controller - manages the "alive computer" state machine and animation loop.
    /// Deterministic state transitions based on input signals.
    /// 
    /// Visual Polish Features:
    /// - Smooth ease-in/ease-out state transitions
    /// - "Gathering" motion when entering Thinking state
    /// - "Release" motion when completing tasks
    /// - Persona-aware timing variations
    /// </summary>
    public class PresenceController : IDisposable
    {
        private static PresenceController? _instance;
        private static readonly object _lock = new object();

        public static PresenceController Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new PresenceController();
                    }
                }
                return _instance;
            }
        }

        // === Configuration ===
        private const double IdleTimeoutSeconds = 5.0;      // Time before transitioning to Idle
        private const double ErrorDisplaySeconds = 3.0;     // How long to show error state
        private const double SmoothingFactor = 0.08;        // Lerp smoothing (lower = smoother)
        private const double IdleSmoothingFactor = 0.03;    // Even smoother when idle (save CPU)
        
        // State transition timing (for smooth easing)
        private const double TransitionDurationNormal = 0.4;   // 400ms for normal transitions
        private const double TransitionDurationThinking = 0.3; // 300ms for thinking (feels intentional)
        private const double TransitionDurationRelease = 0.6;  // 600ms for completion (feels like release)

        // === State Signals ===
        private bool _isProcessingAI = false;
        private bool _isWorkingTask = false;
        private bool _isFocused = false;
        private DateTime _lastInputTime = DateTime.Now;
        private DateTime? _errorStartTime = null;

        // === State Transition Tracking ===
        private PresenceState _previousState = PresenceState.Idle;
        private double _stateTransitionProgress = 1.0; // 0 = just started, 1 = complete
        private double _stateTransitionDuration = TransitionDurationNormal;
        private bool _isGathering = false;  // True during "gathering" motion into Thinking
        private bool _isReleasing = false;  // True during "release" motion after completion

        // === Animation Loop ===
        private bool _isRunning = false;
        private DateTime _lastFrameTime;
        private double _animationTime = 0;
        private readonly Random _random = new Random();

        // === Persona Timing Multipliers ===
        private double _personaTimingMultiplier = 1.0; // Adjusted based on persona

        // === Public Properties ===

        public bool IsProcessingAI
        {
            get => _isProcessingAI;
            set
            {
                if (_isProcessingAI != value)
                {
                    _isProcessingAI = value;
                    Debug.WriteLine($"[Presence] IsProcessingAI = {value}");
                    UpdateState();
                }
            }
        }

        public bool IsWorkingTask
        {
            get => _isWorkingTask;
            set
            {
                if (_isWorkingTask != value)
                {
                    _isWorkingTask = value;
                    Debug.WriteLine($"[Presence] IsWorkingTask = {value}");
                    UpdateState();
                }
            }
        }

        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    UpdateState();
                }
            }
        }

        public PresenceVisualModel VisualModel => PresenceVisualModel.Instance;

        // === Constructor ===

        private PresenceController()
        {
            _lastFrameTime = DateTime.Now;
        }

        // === Public Methods ===

        /// <summary>
        /// Start the animation loop. Call once when the main window loads.
        /// </summary>
        public void Start()
        {
            Debug.WriteLine("[Presence] ═══════════════════════════════════════");
            Debug.WriteLine("[Presence] Start() called");
            Debug.WriteLine($"[Presence] _isRunning: {_isRunning}");
            
            if (_isRunning)
            {
                Debug.WriteLine("[Presence] Already running - skipping");
                return;
            }
            
            try
            {
                _isRunning = true;
                _lastFrameTime = DateTime.Now;
                CompositionTarget.Rendering += OnRendering;
                Debug.WriteLine("[Presence] [OK] Animation loop started");
                Debug.WriteLine("[Presence] [OK] Subscribed to CompositionTarget.Rendering");
                Debug.WriteLine("[Presence] ═══════════════════════════════════════");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Presence] ═══════════════════════════════════════");
                Debug.WriteLine($"[Presence] [ERROR] START FAILED");
                Debug.WriteLine($"[Presence] Error: {ex.Message}");
                Debug.WriteLine($"[Presence] Stack: {ex.StackTrace}");
                Debug.WriteLine("[Presence] ═══════════════════════════════════════");
                throw; // Re-throw - presence MUST start
            }
        }

        /// <summary>
        /// Stop the animation loop. Call when the application is closing.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            CompositionTarget.Rendering -= OnRendering;
            Debug.WriteLine("[Presence] Animation loop stopped");
        }

        /// <summary>
        /// Record user input activity (resets idle timer)
        /// </summary>
        public void RecordInput()
        {
            _lastInputTime = DateTime.Now;
            UpdateState();
        }

        /// <summary>
        /// Trigger error state for a brief period (non-fatal, visual only)
        /// </summary>
        public void TriggerError()
        {
            _errorStartTime = DateTime.Now;
            UpdateState();
        }

        /// <summary>
        /// Clear error state manually
        /// </summary>
        public void ClearError()
        {
            _errorStartTime = null;
            UpdateState();
        }

        /// <summary>
        /// Notify that a command/action was triggered (brief attention pulse)
        /// </summary>
        public void NotifyCommandTriggered()
        {
            // Brief focused pulse for command acknowledgment
            var model = VisualModel;
            model.TargetAttentionLevel = Math.Max(model.TargetAttentionLevel, 0.8);
            RecordInput();
        }

        /// <summary>
        /// Notify that wake word was triggered (transition to Attentive)
        /// </summary>
        public void NotifyWakeWordTriggered()
        {
            // Transition to Attentive state
            RecordInput();
            
            // Brief pulse for acknowledgment
            var model = VisualModel;
            model.TargetAttentionLevel = 0.9;
            
            Debug.WriteLine("[Presence] Wake word triggered → Attentive");
        }

        /// <summary>
        /// Update persona timing based on current preference
        /// </summary>
        public void UpdatePersonaTiming(PersonaType persona)
        {
            _personaTimingMultiplier = persona switch
            {
                PersonaType.Jarvis => 1.1,  // Slightly slower, more deliberate
                PersonaType.Ultron => 0.85, // Sharper, tighter timing
                PersonaType.Neutral => 1.0, // Balanced
                _ => 1.0
            };
        }

        // === State Machine ===

        private void UpdateState()
        {
            var model = VisualModel;
            var previousState = model.CurrentState;

            // Deterministic state rules (priority order):
            // 1. Error (if within error display window)
            // 2. Thinking (AI processing)
            // 3. Working (task execution)
            // 4. Attentive (focused or recent input)
            // 5. Idle (default)

            PresenceState newState;

            if (_errorStartTime.HasValue && (DateTime.Now - _errorStartTime.Value).TotalSeconds < ErrorDisplaySeconds)
            {
                newState = PresenceState.Error;
            }
            else if (_isProcessingAI)
            {
                newState = PresenceState.Thinking;
            }
            else if (_isWorkingTask)
            {
                newState = PresenceState.Working;
            }
            else if (_isFocused || (DateTime.Now - _lastInputTime).TotalSeconds < IdleTimeoutSeconds)
            {
                newState = PresenceState.Attentive;
            }
            else
            {
                newState = PresenceState.Idle;
            }

            // Clear error if expired
            if (_errorStartTime.HasValue && (DateTime.Now - _errorStartTime.Value).TotalSeconds >= ErrorDisplaySeconds)
            {
                _errorStartTime = null;
            }

            // Handle state transition with appropriate timing
            if (previousState != newState)
            {
                _previousState = previousState;
                _stateTransitionProgress = 0;
                
                // Determine transition characteristics
                if (newState == PresenceState.Thinking)
                {
                    // Entering Thinking: "gathering" motion
                    _stateTransitionDuration = TransitionDurationThinking * _personaTimingMultiplier;
                    _isGathering = true;
                    _isReleasing = false;
                }
                else if (previousState == PresenceState.Thinking || previousState == PresenceState.Working)
                {
                    // Leaving Thinking/Working: "release" motion
                    _stateTransitionDuration = TransitionDurationRelease * _personaTimingMultiplier;
                    _isGathering = false;
                    _isReleasing = true;
                }
                else
                {
                    // Normal transition
                    _stateTransitionDuration = TransitionDurationNormal * _personaTimingMultiplier;
                    _isGathering = false;
                    _isReleasing = false;
                }

                Debug.WriteLine($"[Presence] State: {previousState} → {newState} (duration: {_stateTransitionDuration:F2}s, gathering: {_isGathering}, releasing: {_isReleasing})");
            }

            model.CurrentState = newState;

            // Set target visual values based on state
            SetTargetValuesForState(newState);
        }

        private void SetTargetValuesForState(PresenceState state)
        {
            var model = VisualModel;

            switch (state)
            {
                case PresenceState.Idle:
                    model.TargetPulseRate = 0.2;
                    model.TargetPulseAmplitude = 0.15;
                    model.TargetCoreGlow = 0.25;
                    model.TargetCoreAccent = 0.0;
                    model.TargetRingSpeed = 0.1;
                    model.TargetNoiseJitter = 0.0;
                    model.TargetAttentionLevel = 0.0;
                    break;

                case PresenceState.Attentive:
                    model.TargetPulseRate = 0.4;
                    model.TargetPulseAmplitude = 0.3;
                    model.TargetCoreGlow = 0.5;
                    model.TargetCoreAccent = 0.0;
                    model.TargetRingSpeed = 0.3;
                    model.TargetNoiseJitter = 0.05;
                    model.TargetAttentionLevel = 0.7;
                    break;

                case PresenceState.Thinking:
                    model.TargetPulseRate = 0.8;
                    model.TargetPulseAmplitude = 0.6;
                    model.TargetCoreGlow = 0.8;
                    model.TargetCoreAccent = 0.9;  // Orange accent ONLY in Thinking
                    model.TargetRingSpeed = 0.7;
                    model.TargetNoiseJitter = 0.15;
                    model.TargetAttentionLevel = 1.0;
                    break;

                case PresenceState.Working:
                    model.TargetPulseRate = 0.5;
                    model.TargetPulseAmplitude = 0.4;
                    model.TargetCoreGlow = 0.6;
                    model.TargetCoreAccent = 0.0;
                    model.TargetRingSpeed = 0.5;
                    model.TargetNoiseJitter = 0.08;
                    model.TargetAttentionLevel = 0.8;
                    break;

                case PresenceState.Error:
                    model.TargetPulseRate = 0.9;
                    model.TargetPulseAmplitude = 0.5;
                    model.TargetCoreGlow = 0.7;
                    model.TargetCoreAccent = 0.0;
                    model.TargetRingSpeed = 0.1;
                    model.TargetNoiseJitter = 0.3;
                    model.TargetAttentionLevel = 1.0;
                    break;
            }
        }

        // === Animation Loop ===

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_isRunning) return;

            var now = DateTime.Now;
            var deltaTime = (now - _lastFrameTime).TotalSeconds;
            
            // Throttle to 30fps to reduce interference with HoloCoreControl (60fps)
            if (deltaTime < 0.033) return; // Skip frame if less than 33ms
            
            _lastFrameTime = now;

            // Cap delta time to prevent huge jumps
            deltaTime = Math.Min(deltaTime, 0.1);

            // Accumulate animation time
            _animationTime += deltaTime;

            // Update state (check for idle timeout)
            UpdateState();

            // Progress state transition
            if (_stateTransitionProgress < 1.0)
            {
                _stateTransitionProgress = Math.Min(1.0, _stateTransitionProgress + deltaTime / _stateTransitionDuration);
                
                // Clear gathering/releasing flags when transition completes
                if (_stateTransitionProgress >= 1.0)
                {
                    _isGathering = false;
                    _isReleasing = false;
                }
            }

            // Smooth interpolation toward target values with ease curves
            var model = VisualModel;
            var baseSmoothing = model.CurrentState == PresenceState.Idle ? IdleSmoothingFactor : SmoothingFactor;
            
            // Apply ease-in-out curve to smoothing during transitions
            double transitionEase = EaseInOutCubic(_stateTransitionProgress);
            double effectiveSmoothing = baseSmoothing * (0.5 + transitionEase * 0.5);
            
            model.LerpTowardTargets(effectiveSmoothing);

            // Apply gathering/releasing motion modifiers
            if (_isGathering && _stateTransitionProgress < 0.5)
            {
                // During first half of gathering: slight inward pull
                double gatherIntensity = (0.5 - _stateTransitionProgress) * 2;
                model.TargetAttentionLevel = Math.Min(1.0, model.TargetAttentionLevel + gatherIntensity * 0.2);
            }
            else if (_isReleasing && _stateTransitionProgress < 0.7)
            {
                // During release: slight outward expansion then settle
                double releaseIntensity = Math.Sin(_stateTransitionProgress * Math.PI / 0.7) * 0.15;
                model.PulseAmplitude = Math.Min(1.0, model.PulseAmplitude + releaseIntensity);
            }

            // Add subtle noise/jitter for organic feel
            if (model.NoiseJitter > 0.01)
            {
                var noise = (_random.NextDouble() - 0.5) * model.NoiseJitter * 0.1;
                model.PulseAmplitude = Math.Max(0, Math.Min(1, model.PulseAmplitude + noise));
            }

            // Update VoiceActivityService and sync to PresenceVisualModel
            try
            {
                var voiceService = Voice.VoiceActivityService.Instance;
                voiceService.Update(deltaTime);
                model.VoiceAmplitude = voiceService.CurrentAmplitude01;
                model.IsSpeaking = voiceService.IsSpeaking;
            }
            catch { /* VoiceActivityService may not be initialized yet */ }
        }

        /// <summary>
        /// Cubic ease-in-out curve for smooth state transitions
        /// </summary>
        private static double EaseInOutCubic(double t)
        {
            return t < 0.5 
                ? 4 * t * t * t 
                : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        // === Computed Animation Values ===

        /// <summary>
        /// Get the current pulse value (oscillating 0..1 based on time and rate)
        /// </summary>
        public double GetPulseValue()
        {
            var model = VisualModel;
            var frequency = 0.5 + model.PulseRate * 2.0; // 0.5 to 2.5 Hz
            var phase = _animationTime * frequency * Math.PI * 2;
            var pulse = (Math.Sin(phase) + 1) * 0.5; // 0..1
            return pulse * model.PulseAmplitude;
        }

        /// <summary>
        /// Get the current ring rotation angle in degrees
        /// </summary>
        public double GetRingRotation()
        {
            var model = VisualModel;
            var speed = model.RingSpeed * 60; // degrees per second at max
            return (_animationTime * speed) % 360;
        }

        // === IDisposable ===

        private bool _disposed = false;

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
