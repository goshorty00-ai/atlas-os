using System;
using System.Diagnostics;
using System.Windows;
using AtlasAI.Core;
using AtlasAI.UI;
using AtlasAI.Controls;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Handles visual and audio notifications for voice system events.
    /// Coordinates animations, sounds, and status updates across the UI.
    /// </summary>
    public class VoiceNotificationService
    {
        private static VoiceNotificationService? _instance;
        private static readonly object _lock = new();

        public static VoiceNotificationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VoiceNotificationService();
                    }
                }
                return _instance;
            }
        }

        // References to UI elements (set by ChatWindow)
        private AtlasCoreControl? _atlasCore;
        private Action<string>? _showStatusAction;
        private Action<AtlasVisualState>? _setVisualStateAction;

        private readonly object _statusLock = new();
        private string _lastStatusMessage = "";
        private DateTime _lastStatusUtc = DateTime.MinValue;

        private VoiceNotificationService()
        {
        }

        /// <summary>
        /// Initialize with UI references from ChatWindow
        /// </summary>
        public void Initialize(AtlasCoreControl atlasCore, Action<string> showStatus, Action<AtlasVisualState> setVisualState)
        {
            _atlasCore = atlasCore;
            _showStatusAction = showStatus;
            _setVisualStateAction = setVisualState;
            Debug.WriteLine("[VoiceNotificationService] Initialized with UI references");
        }

        /// <summary>
        /// Notify that wake word was detected
        /// </summary>
        public void NotifyWakeWordDetected(string wakeWord, double confidence)
        {
            Debug.WriteLine($"[VoiceNotificationService] Wake word detected: {wakeWord} ({confidence:P0})");
			try { AppLogger.LogInfo($"[VoiceUI] Wake word detected: {wakeWord} ({confidence:P0})"); } catch { }
            
            // Play audio cue
            AudioCueService.Instance.PlayCue(AudioCueService.CueType.WakeWordDetected);
            
            // Update visual state
            _setVisualStateAction?.Invoke(AtlasVisualState.Listening);
            
            // Show status
            ShowStatus("Listening", ToastType.Info, 1200);
        }

        /// <summary>
        /// Notify that active listening has started
        /// </summary>
        public void NotifyListeningStarted()
        {
            Debug.WriteLine("[VoiceNotificationService] Active listening started");
			try { AppLogger.LogInfo("[VoiceUI] Listening started"); } catch { }
            
            // Play audio cue
            AudioCueService.Instance.PlayCue(AudioCueService.CueType.ListeningStarted);
            
            // Update visual state
            _setVisualStateAction?.Invoke(AtlasVisualState.Listening);
            
            // Show status
            ShowStatus("Listening", ToastType.Info, 1200);
        }

        /// <summary>
        /// Notify that a command was captured
        /// </summary>
        public void NotifyCommandCaptured(string command)
        {
            Debug.WriteLine($"[VoiceNotificationService] Command captured: {command}");
			try { AppLogger.LogInfo($"[VoiceUI] Command captured: {command}"); } catch { }
            
            // Play audio cue
            AudioCueService.Instance.PlayCue(AudioCueService.CueType.CommandAcknowledged);
            
            // Update visual state to thinking
            _setVisualStateAction?.Invoke(AtlasVisualState.Thinking);
            
            // Show status
            ShowStatus($"Processing: \"{command}\"", ToastType.Info, 2500);
        }

        /// <summary>
        /// Notify that processing has started
        /// </summary>
        public void NotifyProcessingStarted()
        {
            Debug.WriteLine("[VoiceNotificationService] Processing started");
			try { AppLogger.LogInfo("[VoiceUI] Processing started"); } catch { }
            
            // Play audio cue
            AudioCueService.Instance.PlayCue(AudioCueService.CueType.ThinkingStarted);
            
            // Update visual state
            _setVisualStateAction?.Invoke(AtlasVisualState.Thinking);
            
            // Show status
            ShowStatus("Processing...", ToastType.Info, 2000);
        }

        /// <summary>
        /// Notify that a task has been completed
        /// </summary>
        public void NotifyTaskCompleted(string taskDescription = "")
        {
            Debug.WriteLine($"[VoiceNotificationService] Task completed: {taskDescription}");
			try { AppLogger.LogInfo($"[VoiceUI] Task completed: {taskDescription}"); } catch { }
            
            // Play audio cue
            AudioCueService.Instance.PlayCue(AudioCueService.CueType.TaskCompleted);
            
            // Update visual state back to idle
            _setVisualStateAction?.Invoke(AtlasVisualState.Idle);
            
            // Show status
            var message = string.IsNullOrEmpty(taskDescription) 
                ? "✅ Task completed" 
                : $"✅ Completed: {taskDescription}";
            ShowStatus(message, ToastType.Success, 3500);
        }

        /// <summary>
        /// Notify that speaking has started
        /// </summary>
        public void NotifySpeakingStarted()
        {
            Debug.WriteLine("[VoiceNotificationService] Speaking started");
			try { AppLogger.LogInfo("[VoiceUI] Speaking started"); } catch { }
            
            // Update visual state
            _setVisualStateAction?.Invoke(AtlasVisualState.Speaking);
            
            // Show status
            ShowStatus("Speaking...", ToastType.Info, 1800);
        }

        /// <summary>
        /// Notify that speaking has ended
        /// </summary>
        public void NotifySpeakingEnded()
        {
            Debug.WriteLine("[VoiceNotificationService] Speaking ended");
			try { AppLogger.LogInfo("[VoiceUI] Speaking ended"); } catch { }
            
            // Update visual state back to idle
            _setVisualStateAction?.Invoke(AtlasVisualState.Idle);
            
            // Clear status
			// Do not force-clear here; passive listening will handle reset.
        }

        /// <summary>
        /// Notify of an error
        /// </summary>
        public void NotifyError(string errorMessage)
        {
            Debug.WriteLine($"[VoiceNotificationService] Error: {errorMessage}");
			try { AppLogger.LogError($"[VoiceUI] {errorMessage}"); } catch { }
            
            // Update visual state to idle (no error state available)
            _setVisualStateAction?.Invoke(AtlasVisualState.Idle);
            
            // Show error status
            ShowStatus($"⚠️ {errorMessage}", ToastType.Error, 12000);
        }

        /// <summary>
        /// Notify that listening timed out
        /// </summary>
        public void NotifyListeningTimeout()
        {
            Debug.WriteLine("[VoiceNotificationService] Listening timeout");
			try { AppLogger.LogWarning("[VoiceUI] Listening timeout"); } catch { }
            
            // Update visual state back to idle
            _setVisualStateAction?.Invoke(AtlasVisualState.Idle);
            
            // Show status
            ShowStatus("Listening timed out", ToastType.Warning, 6000);
        }

        /// <summary>
        /// Notify that the system is returning to passive listening
        /// </summary>
        public void NotifyPassiveListening()
        {
            Debug.WriteLine("[VoiceNotificationService] Returning to passive listening");
			try { AppLogger.LogInfo("[VoiceUI] Passive listening"); } catch { }
            
            // Update visual state back to idle
            _setVisualStateAction?.Invoke(AtlasVisualState.Idle);
            
            // Show status
            // Keep UI quiet in passive state.
        }

        private void ShowStatus(string message, ToastType toastType, int durationMs)
        {
            try
            {
                // De-dupe identical messages firing back-to-back (common with mic signal events)
                if (!string.IsNullOrWhiteSpace(message))
                {
                    lock (_statusLock)
                    {
                        var now = DateTime.UtcNow;
                        var window = (toastType == ToastType.Error || toastType == ToastType.Warning)
                            ? TimeSpan.FromSeconds(4)
                            : TimeSpan.FromSeconds(1.25);
                        if (string.Equals(_lastStatusMessage, message, StringComparison.Ordinal) &&
                            (now - _lastStatusUtc) <= window)
                        {
                            return;
                        }
                        _lastStatusMessage = message;
                        _lastStatusUtc = now;
                    }
                }
            }
            catch
            {
            }

            try
            {
                // Preferred: ChatWindow status surface
                if (_showStatusAction != null)
                {
                    _showStatusAction(message);
                    return;
                }
            }
            catch
            {
            }

            // Fallback: Toasts (CommandCenter initializes ToastNotificationManager)
            try
            {
                if (!string.IsNullOrWhiteSpace(message))	
                    ToastNotificationManager.Instance.Show(message, toastType, durationMs);
            }
            catch
            {
            }
        }
    }
}
