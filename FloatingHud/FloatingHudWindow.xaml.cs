using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AtlasAI.Core;
using AtlasAI.Voice;

namespace AtlasAI.FloatingHud
{
    /// <summary>
    /// Floating HUD Window - Always-present Atlas companion.
    /// Pure presence visualization with no control actions.
    /// 
    /// Visual Polish Features:
    /// - Idle breathing (subtle scale/glow variance)
    /// - Attention acknowledgment pulse on command trigger
    /// - Persona-aware motion timing
    /// 
    /// SAFETY: This window performs NO system operations.
    /// - No file access
    /// - No registry access
    /// - No process management
    /// - No input hooks beyond standard WPF events
    /// </summary>
    public partial class FloatingHudWindow : Window
    {
        private readonly PresenceController _presence;
        private bool _isHovered = false;
        private bool _isClickThrough = false;

        // Animation for hover effects
        private readonly DoubleAnimation _fadeIn;
        private readonly DoubleAnimation _fadeOut;

        // Idle breathing animation state
        private double _idleBreathPhase = 0;
        private double _idleGlowPhase = 0;
        private DateTime _lastUpdateTime;
        private bool _isBreathingEnabled = true;

        // Persona timing
        private double _personaTimingMultiplier = 1.0;

        // Attention pulse state
        private double _attentionPulseIntensity = 0;
        private const double AttentionPulseDecay = 3.0; // Decay rate per second

        public FloatingHudWindow()
        {
            InitializeComponent();

            _presence = PresenceController.Instance;

            // Setup hover animations with ease curves
            _fadeIn = new DoubleAnimation(0.6, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            _lastUpdateTime = DateTime.Now;

            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load preferences and position
                var prefs = PreferencesStore.Instance.Current;
                ApplyPreferences(prefs);

                // Subscribe to preference changes
                PreferencesStore.Instance.PreferencesChanged += OnPreferencesChanged;

                // Subscribe to presence model for status updates
                PresenceVisualModel.Instance.PropertyChanged += OnPresenceChanged;

                // Subscribe to voice state manager for listening indicators
                VoiceStateManager.Instance.PropertyChanged += OnVoiceStatePropertyChanged;
                VoiceStateManager.Instance.StateChanged += OnVoiceStateChanged;

                // Start idle breathing animation loop
                CompositionTarget.Rendering += OnIdleBreathingUpdate;

                // Initial status update
                UpdateStatusLabel();

                Debug.WriteLine("[FloatingHud] Window loaded with idle breathing and voice state monitoring enabled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHud] Load error: {ex.Message}");
            }
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            try
            {
                // Unsubscribe from events
                PreferencesStore.Instance.PreferencesChanged -= OnPreferencesChanged;
                PresenceVisualModel.Instance.PropertyChanged -= OnPresenceChanged;
                VoiceStateManager.Instance.PropertyChanged -= OnVoiceStatePropertyChanged;
                VoiceStateManager.Instance.StateChanged -= OnVoiceStateChanged;
                CompositionTarget.Rendering -= OnIdleBreathingUpdate;

                Debug.WriteLine("[FloatingHud] Window closing, events unsubscribed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHud] Closing error: {ex.Message}");
            }
        }

        private void OnIdleBreathingUpdate(object? sender, EventArgs e)
        {
            if (!_isBreathingEnabled) return;

            var now = DateTime.Now;
            var deltaTime = (now - _lastUpdateTime).TotalSeconds;
            _lastUpdateTime = now;

            // Cap delta to prevent jumps
            deltaTime = Math.Min(deltaTime, 0.1);

            // Update breathing phases with persona timing
            double breathSpeed = 0.6 * _personaTimingMultiplier;
            double glowSpeed = 0.4 * _personaTimingMultiplier;
            
            _idleBreathPhase += deltaTime * breathSpeed;
            _idleGlowPhase += deltaTime * glowSpeed;

            // Decay attention pulse
            if (_attentionPulseIntensity > 0)
            {
                _attentionPulseIntensity = Math.Max(0, _attentionPulseIntensity - deltaTime * AttentionPulseDecay);
            }

            // Calculate breathing values (extremely subtle)
            double breathScale = 1.0 + Math.Sin(_idleBreathPhase) * 0.015; // ±1.5% scale
            double glowOpacity = 0.08 + Math.Sin(_idleGlowPhase) * 0.04;   // 4-12% glow

            // Add attention pulse if active
            if (_attentionPulseIntensity > 0)
            {
                double pulseBoost = _attentionPulseIntensity * 0.1;
                breathScale += pulseBoost;
                glowOpacity += _attentionPulseIntensity * 0.15;
            }

            // Apply breathing to core viewbox
            try
            {
                var scaleTransform = CoreViewbox.RenderTransform as ScaleTransform;
                if (scaleTransform == null)
                {
                    scaleTransform = new ScaleTransform(1, 1);
                    CoreViewbox.RenderTransform = scaleTransform;
                    CoreViewbox.RenderTransformOrigin = new Point(0.5, 0.5);
                }
                scaleTransform.ScaleX = breathScale;
                scaleTransform.ScaleY = breathScale;

                // Apply glow to hover element (repurposed as ambient glow)
                if (!_isHovered)
                {
                    HoverGlow.Opacity = glowOpacity;
                }
            }
            catch { /* Ignore transform errors */ }
        }

        /// <summary>
        /// Trigger attention acknowledgment pulse (called when command is triggered)
        /// </summary>
        public void TriggerAttentionPulse()
        {
            _attentionPulseIntensity = 1.0;
            _presence.NotifyCommandTriggered();
        }

        private void OnPreferencesChanged(object? sender, UserPreferences prefs)
        {
            Dispatcher.BeginInvoke(() => ApplyPreferences(prefs));
        }

        private void ApplyPreferences(UserPreferences prefs)
        {
            // Apply position
            PositionWindow(prefs.FloatingHudPosition);

            // Apply size
            ApplySize(prefs.FloatingHudSize);

            // Apply click-through
            _isClickThrough = prefs.FloatingHudClickThrough;
            UpdateClickThrough();

            // Update persona timing
            UpdatePersonaTiming(prefs.Persona);

            // Update status label with persona
            UpdateStatusLabel();
        }

        private void UpdatePersonaTiming(PersonaType persona)
        {
            _personaTimingMultiplier = persona switch
            {
                PersonaType.Jarvis => 1.15,  // Smoother, more deliberate
                PersonaType.Ultron => 0.8,   // Sharper, tighter
                PersonaType.Neutral => 1.0,  // Balanced
                _ => 1.0
            };

            // Also update the presence controller
            _presence.UpdatePersonaTiming(persona);
        }

        private void PositionWindow(HudPosition position)
        {
            var workArea = SystemParameters.WorkArea;
            const double padding = 10;

            switch (position)
            {
                case HudPosition.TopLeft:
                    Left = workArea.Left + padding;
                    Top = workArea.Top + padding;
                    break;

                case HudPosition.TopRight:
                    Left = workArea.Right - Width - padding;
                    Top = workArea.Top + padding;
                    break;

                case HudPosition.BottomLeft:
                    Left = workArea.Left + padding;
                    Top = workArea.Bottom - Height - padding;
                    break;

                case HudPosition.BottomRight:
                default:
                    Left = workArea.Right - Width - padding;
                    Top = workArea.Bottom - Height - padding;
                    break;
            }
        }

        private void ApplySize(HudSize size)
        {
            switch (size)
            {
                case HudSize.Small:
                    Width = 80;
                    Height = 100;
                    CoreViewbox.Width = 60;
                    CoreViewbox.Height = 60;
                    StatusLabel.FontSize = 8;
                    break;

                case HudSize.Medium:
                default:
                    Width = 100;
                    Height = 120;
                    CoreViewbox.Width = 80;
                    CoreViewbox.Height = 80;
                    StatusLabel.FontSize = 9;
                    break;
            }

            // Re-position after size change
            var prefs = PreferencesStore.Instance.Current;
            PositionWindow(prefs.FloatingHudPosition);
        }

        private void UpdateClickThrough()
        {
            // Click-through is handled by checking _isClickThrough in mouse events
            // When enabled, we don't capture mouse events
            IsHitTestVisible = !_isClickThrough;
        }

        private void OnPresenceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PresenceVisualModel.CurrentState) ||
                e.PropertyName == nameof(PresenceVisualModel.StatusText))
            {
                Dispatcher.BeginInvoke(UpdateStatusLabel);
            }
            else if (e.PropertyName == nameof(PresenceVisualModel.IsWorkflowActive))
            {
                Dispatcher.BeginInvoke(UpdateWorkflowIndicator);
            }
        }

        private void UpdateStatusLabel()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                var model = PresenceVisualModel.Instance;

                // Use persona-aware status label
                bool isOnline = model.CurrentState != PresenceState.Idle;
                
                // Show workflow indicator if active
                if (model.IsWorkflowActive)
                {
                    StatusLabel.Text = "WORKFLOW";
                    StatusLabel.Opacity = 0.9;
                }
                else
                {
                    StatusLabel.Text = PersonaProfile.GetStatusLabel(prefs.Persona, isOnline);
                    StatusLabel.Opacity = isOnline ? 0.8 : 0.5;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHud] Status update error: {ex.Message}");
                StatusLabel.Text = "ONLINE";
            }
        }

        private void UpdateWorkflowIndicator()
        {
            var model = PresenceVisualModel.Instance;
            
            if (model.IsWorkflowActive)
            {
                // Show subtle workflow pulse on the core
                MiniCore.PresenceLevel = Math.Max(MiniCore.PresenceLevel, 0.6);
            }
            
            UpdateStatusLabel();
        }

        private void OnVoiceStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(UpdateVoiceStateVisuals);
        }

        private void OnVoiceStateChanged(object? sender, VoiceSystemState e)
        {
            Dispatcher.BeginInvoke(UpdateVoiceStateVisuals);
        }

        private void UpdateVoiceStateVisuals()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                var voiceState = VoiceStateManager.Instance;

                // Only show voice indicators if listening indicator is enabled
                if (!prefs.ShowListeningIndicator)
                    return;

                // Update status label to show voice state
                switch (voiceState.CurrentState)
                {
                    case VoiceSystemState.PassiveListening:
                        StatusLabel.Text = "LISTENING";
                        StatusLabel.Opacity = 0.7;
                        // Subtle pulse on core
                        MiniCore.PresenceLevel = Math.Max(MiniCore.PresenceLevel, 0.4);
                        break;

                    case VoiceSystemState.ActiveListening:
                        StatusLabel.Text = "COMMAND";
                        StatusLabel.Opacity = 0.9;
                        // Stronger presence
                        MiniCore.PresenceLevel = Math.Max(MiniCore.PresenceLevel, 0.7);
                        // Trigger attention pulse
                        TriggerAttentionPulse();
                        break;

                    case VoiceSystemState.Processing:
                        StatusLabel.Text = "THINKING";
                        StatusLabel.Opacity = 0.8;
                        MiniCore.ThinkingLevel = Math.Max(MiniCore.ThinkingLevel, 0.8);
                        break;

                    case VoiceSystemState.Speaking:
                        StatusLabel.Text = "SPEAKING";
                        StatusLabel.Opacity = 0.9;
                        break;

                    case VoiceSystemState.Suspended:
                        StatusLabel.Text = "SUSPENDED";
                        StatusLabel.Opacity = 0.5;
                        break;

                    case VoiceSystemState.Disabled:
                    default:
                        // Fall back to normal status
                        UpdateStatusLabel();
                        break;
                }

                Debug.WriteLine($"[FloatingHud] Voice state visual update: {voiceState.CurrentState}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHud] Voice state update error: {ex.Message}");
            }
        }

        #region Mouse Interaction

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isClickThrough) return;

            _isHovered = true;
            _presence.RecordInput();

            // Show hover glow
            HoverGlow.BeginAnimation(OpacityProperty, _fadeIn);

            // Slight attention pulse on the core
            MiniCore.PresenceLevel = Math.Max(MiniCore.PresenceLevel, 0.5);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _isHovered = false;

            // Hide hover glow
            HoverGlow.BeginAnimation(OpacityProperty, _fadeOut);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isClickThrough) return;

            _presence.RecordInput();

            // Single click opens main Atlas window
            OpenMainWindow();
        }

        private void OpenMainWindow()
        {
            try
            {
                // Find and activate the main chat window
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is ChatWindow chatWindow)
                    {
                        chatWindow.Activate();
                        chatWindow.WindowState = WindowState.Normal;
                        return;
                    }
                }

                // If no chat window exists, create one
                var newChatWindow = new ChatWindow();
                newChatWindow.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHud] Open main window error: {ex.Message}");
            }
        }

        #endregion
    }
}
