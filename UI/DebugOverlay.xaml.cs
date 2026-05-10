using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AtlasAI.Agent;
using AtlasAI.Core;
using AtlasAI.Voice;

namespace AtlasAI.UI
{
    /// <summary>
    /// Debug overlay for development/troubleshooting.
    /// Shows: Voice state, intent classification, online mode, follow-up timer, quality gate.
    /// Toggle with Ctrl+Shift+D. Default OFF.
    /// </summary>
    public partial class DebugOverlay : Window
    {
        private static DebugOverlay? _instance;
        private static readonly object _lock = new();
        private readonly DispatcherTimer _updateTimer;
        
        // Last known values for display
        private string _lastIntent = "--";
        private string _lastPipeline = "--";
        private bool _lastQualityGateRegen = false;
        private int _lastSpecificityScore = 0;

        public static DebugOverlay Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new DebugOverlay();
                    }
                }
                return _instance;
            }
        }

        public static bool IsEnabled { get; private set; } = false;

        private DebugOverlay()
        {
            InitializeComponent();
            
            // Position in top-right corner
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Top + 20;
            
            // Update timer
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
            
            Closing += (s, e) =>
            {
                e.Cancel = true;
                Hide();
                IsEnabled = false;
            };
        }

        /// <summary>
        /// Toggle the debug overlay visibility.
        /// </summary>
        public static void Toggle()
        {
            if (IsEnabled)
            {
                Instance.Hide();
                Instance._updateTimer.Stop();
                IsEnabled = false;
                Debug.WriteLine("[DebugOverlay] Hidden");
            }
            else
            {
                Instance.Show();
                Instance._updateTimer.Start();
                Instance.UpdateDisplay();
                IsEnabled = true;
                Debug.WriteLine("[DebugOverlay] Shown");
            }
        }

        /// <summary>
        /// Register the global hotkey (Ctrl+Shift+D).
        /// Call from App.xaml.cs or MainWindow.
        /// </summary>
        public static void RegisterHotkey(Window window)
        {
            window.KeyDown += (s, e) =>
            {
                if (e.Key == Key.D && 
                    Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                {
                    Toggle();
                    e.Handled = true;
                }
            };
        }

        /// <summary>
        /// Update intent display from routing result.
        /// </summary>
        public void UpdateIntent(RoutingResult result)
        {
            if (!IsEnabled) return;
            
            _lastIntent = result.Intent;
            _lastPipeline = result.Pipeline.ToString();
            
            Dispatcher.BeginInvoke(() =>
            {
                LastIntentText.Text = _lastIntent;
                PipelineText.Text = _lastPipeline;
            });
        }

        /// <summary>
        /// Update quality gate display.
        /// </summary>
        public void UpdateQualityGate(bool regenerated, int specificityScore)
        {
            if (!IsEnabled) return;
            
            _lastQualityGateRegen = regenerated;
            _lastSpecificityScore = specificityScore;
            
            Dispatcher.BeginInvoke(() =>
            {
                var status = regenerated ? "Regen" : "Pass";
                QualityGateText.Text = $"{status} (score: {specificityScore})";
                QualityGateText.Foreground = regenerated 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36))  // Yellow
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153)); // Green
            });
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            try
            {
                // Voice state
                var voiceCoord = VoiceInteractionCoordinator.Instance;
                VoiceStateText.Text = voiceCoord.CurrentState.ToString();
                
                // Follow-up timer
                var remaining = voiceCoord.GetPostSpeechTimeRemaining();
                if (remaining.HasValue)
                {
                    FollowUpText.Text = $"{remaining.Value.TotalSeconds:F1}s";
                    FollowUpText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(244, 114, 182)); // Pink
                }
                else
                {
                    FollowUpText.Text = voiceCoord.CurrentState == VoiceSessionState.ActiveConversation 
                        ? "Active" : "--";
                    FollowUpText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(136, 136, 136)); // Gray
                }
                
                // Online mode
                var onlineManager = OnlineModeManager.Instance;
                var onlineStatus = onlineManager.IsOnlineAccessActive ? "Active" : onlineManager.Setting.ToString();
                var expiry = onlineManager.TemporaryAccessRemaining;
                if (expiry.HasValue)
                {
                    onlineStatus += $" ({expiry.Value.Minutes}:{expiry.Value.Seconds:D2})";
                }
                OnlineModeText.Text = onlineStatus;
                OnlineModeText.Foreground = onlineManager.IsOnlineAccessActive
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153))  // Green
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36)); // Yellow
                
                // Last update
                LastUpdateText.Text = $"Updated: {DateTime.Now:HH:mm:ss.fff}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DebugOverlay] Update error: {ex.Message}");
            }
        }
    }
}
