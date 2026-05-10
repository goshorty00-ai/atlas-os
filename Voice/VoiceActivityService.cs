using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Windows;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Service that provides normalized voice amplitude (0..1) for visual synchronization.
    /// Supports both real RMS amplitude from NAudio and synthetic envelope fallback.
    /// </summary>
    public class VoiceActivityService : INotifyPropertyChanged, IDisposable
    {
        private static VoiceActivityService? _instance;
        private static readonly object _lock = new object();

        public static VoiceActivityService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VoiceActivityService();
                    }
                }
                return _instance;
            }
        }

        // === Configuration ===
        private const double AttackRate = 0.35;      // Fast attack (voice starts)
        private const double ReleaseRate = 0.08;     // Slow release (voice ends)
        private const double MinAmplitude = 0.0;
        private const double MaxAmplitude = 1.0;
        private const float MinPeakThreshold = 0.05f; // Minimum peak for normalization

        // === State ===
        private double _currentAmplitude01 = 0.0;
        private double _targetAmplitude = 0.0;
        private bool _isSpeaking = false;
        private DateTime _speechStartTime;
        private readonly Random _random = new Random();
        
        // === Connection Guard ===
        private bool _isConnectedToVoiceManager = false;
        private VoiceManager? _connectedVoiceManager = null;

        // === Real RMS Metering State ===
        private bool _hasRealMeter = false;
        private float _rollingPeak = 0.1f;           // Rolling peak for normalization
        private const float PeakDecay = 0.995f;      // Slow decay of rolling peak
        private DateTime _lastRmsTime = DateTime.MinValue;
        private const double RmsTimeoutMs = 100;     // If no RMS for 100ms, fall back to synthetic

        // Synthetic envelope state (used when real audio samples unavailable)
        private double _envelopePhase = 0.0;
        private double _envelopeFrequency = 4.0; // Hz - syllable rate

        // === Events ===
        public event EventHandler<double>? AmplitudeChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        private void DispatchToUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
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

        // === Properties ===

        /// <summary>
        /// Current smoothed amplitude normalized to 0..1 range.
        /// Bind HoloCoreControl.VoiceAmplitude to this property.
        /// </summary>
        public double CurrentAmplitude01
        {
            get => _currentAmplitude01;
            private set
            {
                if (Math.Abs(_currentAmplitude01 - value) > 0.001)
                {
                    _currentAmplitude01 = Math.Clamp(value, MinAmplitude, MaxAmplitude);
                    OnPropertyChanged();
                    try
                    {
                        var amplitude = _currentAmplitude01;
                        DispatchToUi(() => AmplitudeChanged?.Invoke(this, amplitude));
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Whether TTS is currently playing
        /// </summary>
        public bool IsSpeaking
        {
            get => _isSpeaking;
            private set
            {
                if (_isSpeaking != value)
                {
                    _isSpeaking = value;
                    OnPropertyChanged();
                    
                    if (value)
                    {
                        _speechStartTime = DateTime.Now;
                        _envelopePhase = 0;
                        Debug.WriteLine("[VoiceActivityService] Speech started");
                    }
                    else
                    {
                        _targetAmplitude = 0;
                        _hasRealMeter = false;
                        _rollingPeak = 0.1f; // Reset peak for next speech
                        Debug.WriteLine("[VoiceActivityService] Speech ended");
                    }
                }
            }
        }

        /// <summary>
        /// Whether real RMS metering is active (vs synthetic envelope)
        /// </summary>
        public bool HasRealMeter
        {
            get => _hasRealMeter;
            private set
            {
                if (_hasRealMeter != value)
                {
                    _hasRealMeter = value;
                    OnPropertyChanged();
                    Debug.WriteLine($"[VoiceActivityService] HasRealMeter = {value}");
                }
            }
        }

        // === Constructor ===

        private VoiceActivityService()
        {
        }

        // === Public Methods ===

        /// <summary>
        /// Connect to a VoiceManager to automatically track speech state.
        /// Call this once during app initialization.
        /// Guards against duplicate subscriptions.
        /// </summary>
        public void ConnectToVoiceManager(VoiceManager voiceManager)
        {
            if (voiceManager == null) return;
            
            // Guard against duplicate subscriptions
            if (_isConnectedToVoiceManager)
            {
                if (_connectedVoiceManager == voiceManager)
                {
                    Debug.WriteLine("[VoiceActivityService] Already connected to this VoiceManager - skipping");
                    return;
                }
                
                // Disconnect from old manager first
                if (_connectedVoiceManager != null)
                {
                    Debug.WriteLine("[VoiceActivityService] Disconnecting from previous VoiceManager");
                    try
                    {
                        _connectedVoiceManager.SpeechStarted -= OnVoiceManagerSpeechStarted;
                        _connectedVoiceManager.SpeechEnded -= OnVoiceManagerSpeechEnded;
                    }
                    catch { }
                }
            }

            _connectedVoiceManager = voiceManager;
            voiceManager.SpeechStarted += OnVoiceManagerSpeechStarted;
            voiceManager.SpeechEnded += OnVoiceManagerSpeechEnded;
            _isConnectedToVoiceManager = true;
            
            Debug.WriteLine("[VoiceActivityService] Connected to VoiceManager (guarded)");
        }
        
        private void OnVoiceManagerSpeechStarted(object? sender, EventArgs e)
        {
            IsSpeaking = true;
        }
        
        private void OnVoiceManagerSpeechEnded(object? sender, EventArgs e)
        {
            IsSpeaking = false;
        }
        
        /// <summary>
        /// Disconnect from VoiceManager (call on dispose/unload)
        /// </summary>
        public void DisconnectFromVoiceManager()
        {
            if (_connectedVoiceManager != null && _isConnectedToVoiceManager)
            {
                try
                {
                    _connectedVoiceManager.SpeechStarted -= OnVoiceManagerSpeechStarted;
                    _connectedVoiceManager.SpeechEnded -= OnVoiceManagerSpeechEnded;
                }
                catch { }
                
                _connectedVoiceManager = null;
                _isConnectedToVoiceManager = false;
                Debug.WriteLine("[VoiceActivityService] Disconnected from VoiceManager");
            }
        }

        /// <summary>
        /// Push real RMS amplitude from NAudio metering.
        /// Called by RmsMeteringSampleProvider during audio playback.
        /// </summary>
        /// <param name="rms">Raw RMS value (typically 0..0.5 for speech)</param>
        public void PushRms(float rms)
        {
            if (!_isSpeaking) return;
            
            _lastRmsTime = DateTime.Now;
            HasRealMeter = true;
            
            // Update rolling peak with slow decay
            _rollingPeak = Math.Max(_rollingPeak * PeakDecay, rms);
            
            // Normalize against rolling peak (with minimum threshold)
            float normalizedRms = rms / Math.Max(_rollingPeak, MinPeakThreshold);
            
            // Clamp to 0..1
            normalizedRms = Math.Clamp(normalizedRms, 0f, 1f);
            
            // Set target amplitude (smoothing applied in Update)
            _targetAmplitude = normalizedRms;
        }

        /// <summary>
        /// Update the amplitude smoothing. Call this from a render loop or timer.
        /// </summary>
        public void Update(double deltaTime)
        {
            if (!_isSpeaking)
            {
                // Decay to zero when not speaking
                CurrentAmplitude01 = Lerp(CurrentAmplitude01, 0, ReleaseRate * 2);
                return;
            }

            // Check if real RMS has timed out
            if (_hasRealMeter && (DateTime.Now - _lastRmsTime).TotalMilliseconds > RmsTimeoutMs)
            {
                // RMS stopped coming in - fall back to synthetic
                HasRealMeter = false;
            }

            // Use real RMS if available, otherwise synthetic envelope
            if (_hasRealMeter)
            {
                // Real RMS - apply smoothing (fast attack, slow release)
                double rate = _targetAmplitude > CurrentAmplitude01 ? AttackRate : ReleaseRate;
                CurrentAmplitude01 = Lerp(CurrentAmplitude01, _targetAmplitude, rate);
            }
            else
            {
                // Synthetic envelope fallback
                UpdateSyntheticEnvelope(deltaTime);
            }
        }

        /// <summary>
        /// Generate a synthetic voice envelope when real audio samples aren't available.
        /// Creates organic-feeling amplitude variations tied to speech timing.
        /// </summary>
        private void UpdateSyntheticEnvelope(double deltaTime)
        {
            // CRITICAL FIX: Don't generate synthetic amplitude during initial TTS delay
            // Wait at least 200ms after speech start before generating synthetic envelope
            var timeSinceSpeechStart = (DateTime.Now - _speechStartTime).TotalMilliseconds;
            if (timeSinceSpeechStart < 200)
            {
                // Keep amplitude at zero during TTS initialization delay
                _targetAmplitude = 0;
                CurrentAmplitude01 = Lerp(CurrentAmplitude01, 0, ReleaseRate);
                return;
            }

            // Advance envelope phase
            _envelopePhase += deltaTime * _envelopeFrequency * Math.PI * 2;

            // Multi-frequency synthesis for organic feel
            // Base rhythm (word-level ~2-3 Hz)
            double wordRhythm = 0.5 + 0.3 * Math.Sin(_envelopePhase * 0.5);
            
            // Syllable rhythm (~4-6 Hz)
            double syllableRhythm = 0.2 * Math.Sin(_envelopePhase);
            
            // Micro-variation (~10-15 Hz)
            double microVar = 0.1 * Math.Sin(_envelopePhase * 2.5);
            
            // Occasional emphasis peaks
            double emphasis = 0.15 * Math.Max(0, Math.Sin(_envelopePhase * 0.3) - 0.7);

            // Combine with slight randomness for natural feel
            double jitter = (_random.NextDouble() - 0.5) * 0.05;
            
            _targetAmplitude = Math.Clamp(
                wordRhythm + syllableRhythm + microVar + emphasis + jitter,
                0.2, // Minimum while speaking
                1.0
            );

            // Smooth toward target
            double rate = _targetAmplitude > CurrentAmplitude01 ? AttackRate : ReleaseRate;
            CurrentAmplitude01 = Lerp(CurrentAmplitude01, _targetAmplitude, rate);
        }

        /// <summary>
        /// Manually start speech tracking (if not using VoiceManager connection)
        /// </summary>
        public void StartSpeech()
        {
            IsSpeaking = true;
        }

        /// <summary>
        /// Manually stop speech tracking (if not using VoiceManager connection)
        /// </summary>
        public void StopSpeech()
        {
            IsSpeaking = false;
        }

        /// <summary>
        /// Reset metering state (call when playback is interrupted)
        /// </summary>
        public void ResetMetering()
        {
            HasRealMeter = false;
            _rollingPeak = 0.1f;
            _targetAmplitude = 0;
        }

        // === Helpers ===

        private static double Lerp(double current, double target, double t)
        {
            return current + (target - current) * Math.Clamp(t, 0, 1);
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            try
            {
                DispatchToUi(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            // Disconnect from VoiceManager
            DisconnectFromVoiceManager();
            
            // Cleanup - reset state
            _hasRealMeter = false;
            _isSpeaking = false;
        }
    }
}
