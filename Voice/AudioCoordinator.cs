using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Coordinates audio systems to prevent interference between voice recording and audio monitoring
    /// Enhanced to prevent audio distortion in headphones with emergency protection
    /// </summary>
    public static class AudioCoordinator
    {
        private static readonly List<SettingsWindow> _registeredMonitors = new();
        private static bool _isVoiceRecordingActive = false;
        private static bool _emergencyAudioProtectionActive = false;
        private static readonly object _lockObject = new object();
        
        /// <summary>
        /// Enable emergency audio protection - completely disables all audio capture to prevent distortion
        /// </summary>
        public static void EnableEmergencyAudioProtection()
        {
            lock (_lockObject)
            {
                _emergencyAudioProtectionActive = true;
                Debug.WriteLine("[AudioCoordinator] EMERGENCY AUDIO PROTECTION ENABLED - All audio capture disabled");
                
                // Force stop all audio systems immediately
                EmergencyStopAllAudio();
            }
        }
        
        /// <summary>
        /// Disable emergency audio protection and allow audio systems to resume
        /// </summary>
        public static void DisableEmergencyAudioProtection()
        {
            lock (_lockObject)
            {
                _emergencyAudioProtectionActive = false;
                Debug.WriteLine("[AudioCoordinator] Emergency audio protection disabled - Audio systems can resume");
                
                // Allow audio systems to resume after a delay
                ResumeAllAudio();
            }
        }
        
        /// <summary>
        /// Check if emergency audio protection is active
        /// </summary>
        public static bool IsEmergencyProtectionActive
        {
            get
            {
                lock (_lockObject)
                {
                    return _emergencyAudioProtectionActive;
                }
            }
        }
        
        /// <summary>
        /// Register a settings window for audio coordination
        /// </summary>
        public static void RegisterMonitor(SettingsWindow settingsWindow)
        {
            lock (_lockObject)
            {
                if (!_registeredMonitors.Contains(settingsWindow))
                {
                    _registeredMonitors.Add(settingsWindow);
                    Debug.WriteLine($"[AudioCoordinator] Registered settings window for audio coordination");
                }
            }
        }
        
        /// <summary>
        /// Unregister a settings window from audio coordination
        /// </summary>
        public static void UnregisterMonitor(SettingsWindow settingsWindow)
        {
            lock (_lockObject)
            {
                _registeredMonitors.Remove(settingsWindow);
                Debug.WriteLine($"[AudioCoordinator] Unregistered settings window from audio coordination");
            }
        }
        
        /// <summary>
        /// Notify that voice recording has started - all audio monitors should pause immediately
        /// </summary>
        public static void NotifyVoiceRecordingStarted()
        {
            lock (_lockObject)
            {
                // If emergency protection is active, block all audio operations
                if (_emergencyAudioProtectionActive)
                {
                    Debug.WriteLine("[AudioCoordinator] Emergency protection active - blocking voice recording");
                    return;
                }
                
                if (_isVoiceRecordingActive) return; // Already active
                
                _isVoiceRecordingActive = true;
                Debug.WriteLine("[AudioCoordinator] Voice recording started - immediately pausing all monitors to prevent distortion");
                
                // Pause all monitors immediately to prevent audio interference
                foreach (var monitor in _registeredMonitors)
                {
                    try
                    {
                        monitor.OnVoiceRecordingStarted();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AudioCoordinator] Error notifying monitor: {ex.Message}");
                    }
                }
                
                // Add a longer delay to ensure all audio systems have stopped
                Task.Delay(100).ContinueWith(_ =>
                {
                    Debug.WriteLine("[AudioCoordinator] Audio coordination delay complete - voice recording can proceed safely");
                });
            }
        }
        
        /// <summary>
        /// Notify that voice recording has stopped - audio monitors can resume after delay
        /// </summary>
        public static void NotifyVoiceRecordingStopped()
        {
            lock (_lockObject)
            {
                // If emergency protection is active, don't resume anything
                if (_emergencyAudioProtectionActive)
                {
                    Debug.WriteLine("[AudioCoordinator] Emergency protection active - not resuming audio systems");
                    return;
                }
                
                if (!_isVoiceRecordingActive) return; // Already stopped
                
                _isVoiceRecordingActive = false;
                Debug.WriteLine("[AudioCoordinator] Voice recording stopped - scheduling monitor resume after safety delay");
                
                // Wait even longer before resuming monitors to prevent audio conflicts
                Task.Delay(500).ContinueWith(_ =>
                {
                    lock (_lockObject)
                    {
                        // Double-check emergency protection hasn't been enabled
                        if (_emergencyAudioProtectionActive)
                        {
                            Debug.WriteLine("[AudioCoordinator] Emergency protection enabled during delay - not resuming");
                            return;
                        }
                        
                        Debug.WriteLine("[AudioCoordinator] Safety delay complete - notifying monitors to resume");
                        foreach (var monitor in _registeredMonitors)
                        {
                            try
                            {
                                monitor.OnVoiceRecordingStopped();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[AudioCoordinator] Error notifying monitor resume: {ex.Message}");
                            }
                        }
                    }
                });
            }
        }
        
        /// <summary>
        /// Check if voice recording is currently active
        /// </summary>
        public static bool IsVoiceRecordingActive 
        { 
            get 
            { 
                lock (_lockObject) 
                { 
                    return _isVoiceRecordingActive; 
                } 
            } 
        }
        
        /// <summary>
        /// Force stop all audio monitoring (emergency stop for audio distortion issues)
        /// </summary>
        public static void EmergencyStopAllAudio()
        {
            lock (_lockObject)
            {
                Debug.WriteLine("[AudioCoordinator] EMERGENCY STOP - Stopping all audio systems to prevent distortion");
                _isVoiceRecordingActive = true; // Block all audio
                
                foreach (var monitor in _registeredMonitors)
                {
                    try
                    {
                        monitor.OnVoiceRecordingStarted(); // Force stop monitoring
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AudioCoordinator] Error in emergency stop: {ex.Message}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Resume all audio systems after emergency stop
        /// </summary>
        public static void ResumeAllAudio()
        {
            Task.Delay(500).ContinueWith(_ =>
            {
                lock (_lockObject)
                {
                    Debug.WriteLine("[AudioCoordinator] Resuming all audio systems after emergency stop");
                    _isVoiceRecordingActive = false;
                    
                    foreach (var monitor in _registeredMonitors)
                    {
                        try
                        {
                            monitor.OnVoiceRecordingStopped();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AudioCoordinator] Error resuming after emergency: {ex.Message}");
                        }
                    }
                }
            });
        }
    }
}