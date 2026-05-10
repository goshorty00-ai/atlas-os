using System;
using System.Diagnostics;
using System.Windows;
using AtlasAI.Voice;

namespace AtlasAI.Core
{
    /// <summary>
    /// STEP 29 STABILIZATION: Single source of truth for wake word activation.
    /// 
    /// MANDATORY RULES (Rule 2 - Wake Word Is App-Lifetime):
    /// - This is a SINGLETON for the ENTIRE app lifetime
    /// - Does NOT depend on: Chat window, Orb visibility, UI focus
    /// - Never auto-dispose or pause unless explicitly disabled by user
    /// - UI elements may subscribe, but must NEVER control wake listening
    /// 
    /// Key behavior:
    /// - Does NOT auto-open Chat window on wake word
    /// - Broadcasts to whichever surface is active (Chat or Orb)
    /// - Integrates with WakeWordFlowController for state management
    /// </summary>
    public class WakeWordCoordinator
    {
        private static WakeWordCoordinator? _instance;
        private static readonly object _lock = new object();

        public static WakeWordCoordinator Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new WakeWordCoordinator();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Broadcast event for UI surfaces to subscribe to.
        /// Fired on Dispatcher thread, safe for UI operations.
        /// NOTE: Does NOT open Chat window - just notifies active surfaces.
        /// </summary>
        public event EventHandler? WakeWordActivated;
        
        /// <summary>
        /// Event with full wake word details (text, confidence)
        /// </summary>
        public event EventHandler<WakeWordEventArgs>? WakeWordActivatedWithDetails;

        private bool _isInitialized = false;
        private DateTime _lastWakeWordTime = DateTime.MinValue;
        private readonly TimeSpan _cooldownPeriod = TimeSpan.FromSeconds(2);

        private WakeWordCoordinator()
        {
        }

        /// <summary>
        /// Initialize the coordinator and subscribe to WakeWordService.
        /// Should be called once during app startup.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
            {
                Debug.WriteLine("[WakeWordCoordinator] Already initialized");
                return;
            }

            Debug.WriteLine("[WakeWordCoordinator] ═══════════════════════════════════════");
            Debug.WriteLine("[WakeWordCoordinator] Initializing wake word coordinator");
            Debug.WriteLine("[WakeWordCoordinator] Hard constraint: NO auto-open Chat window");
            Debug.WriteLine("[WakeWordCoordinator] ═══════════════════════════════════════");

            // Subscribe to WakeWordService as the ONLY subscriber
            WakeWordService.Instance.WakeWordDetected += OnWakeWordDetected;
            WakeWordService.Instance.Error += OnWakeWordError;
            
            Debug.WriteLine($"[WakeWordCoordinator] ✅ Subscribed to WakeWordService.WakeWordDetected");
            Debug.WriteLine($"[WakeWordCoordinator] ✅ Subscribed to WakeWordService.Error");

            _isInitialized = true;
            Debug.WriteLine("[WakeWordCoordinator] ✅ Initialized and subscribed to WakeWordService");
            Debug.WriteLine("[WakeWordCoordinator] ═══════════════════════════════════════");
        }

        private void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
        {
            // Cooldown check to prevent rapid-fire triggers
            if (DateTime.Now - _lastWakeWordTime < _cooldownPeriod)
            {
                Debug.WriteLine($"[WakeWordCoordinator] Wake word ignored (cooldown): {e.Text}");
                StabilizationLogger.LogEvent("WakeWordCoordinator", "CooldownRejected", reason: e.Text);
                return;
            }
            _lastWakeWordTime = DateTime.Now;
            
            Debug.WriteLine($"[WakeWordCoordinator] ═══════════════════════════════════════");
            Debug.WriteLine($"[WakeWordCoordinator] Wake word received: '{e.Text}'");
            Debug.WriteLine($"[WakeWordCoordinator] Normalized: '{e.NormalizedText}'");
            Debug.WriteLine($"[WakeWordCoordinator] Confidence: {e.Confidence:P0}");
            Debug.WriteLine($"[WakeWordCoordinator] Timestamp: {e.Timestamp:HH:mm:ss.fff}");
            Debug.WriteLine($"[WakeWordCoordinator] ═══════════════════════════════════════");
            
            // STEP 29: Log wake word activation
            StabilizationLogger.LogEvent("WakeWordCoordinator", "WakeWordActivated", 
                extra: new { text = e.Text, confidence = e.Confidence });

            // Notify WakeWordFlowController for state management
            try
            {
                WakeWordFlowController.Instance.OnWakeWordDetected(e.Text, e.Confidence);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordCoordinator] FlowController error: {ex.Message}");
                StabilizationLogger.LogEvent("WakeWordCoordinator", "FlowControllerError", reason: ex.Message);
            }

            // Execute UI actions on Dispatcher thread
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Debug.WriteLine($"[WakeWordCoordinator] Broadcasting WakeWordActivated event");
                    Debug.WriteLine($"[WakeWordCoordinator] NOTE: NOT opening Chat window (per hard constraint)");
                    
                    // Broadcast to all UI subscribers (Chat window, Orb, etc.)
                    // Each subscriber decides how to respond based on their state
                    WakeWordActivated?.Invoke(this, EventArgs.Empty);
                    
                    // Also broadcast with details
                    WakeWordActivatedWithDetails?.Invoke(this, new WakeWordEventArgs
                    {
                        Text = e.Text,
                        NormalizedText = e.NormalizedText,
                        Confidence = e.Confidence,
                        Timestamp = e.Timestamp
                    });
                    
                    Debug.WriteLine($"[WakeWordCoordinator] ✅ Broadcast complete");
                    StabilizationLogger.LogEvent("WakeWordCoordinator", "BroadcastComplete");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WakeWordCoordinator] ❌ Error broadcasting: {ex.Message}");
                    StabilizationLogger.LogEvent("WakeWordCoordinator", "BroadcastError", reason: ex.Message);
                }
            });
        }

        private void OnWakeWordError(object? sender, string error)
        {
            Debug.WriteLine($"[WakeWordCoordinator] ❌ WakeWordService error: {error}");
        }

        /// <summary>
        /// Cleanup subscriptions
        /// </summary>
        public void Shutdown()
        {
            if (_isInitialized)
            {
                WakeWordService.Instance.WakeWordDetected -= OnWakeWordDetected;
                WakeWordService.Instance.Error -= OnWakeWordError;
                _isInitialized = false;
                Debug.WriteLine("[WakeWordCoordinator] Shutdown complete");
            }
        }
    }
    
    /// <summary>
    /// Wake word event arguments with full details
    /// </summary>
    public class WakeWordEventArgs : EventArgs
    {
        public string Text { get; set; } = "";
        public string NormalizedText { get; set; } = "";
        public double Confidence { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
