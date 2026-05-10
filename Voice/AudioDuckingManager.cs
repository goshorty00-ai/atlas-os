using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Manages audio ducking - automatically lowers/pauses music when voice commands are detected
    /// Like Alexa/Siri behavior - music pauses when you say the wake word
    /// </summary>
    public static class AudioDuckingManager
    {
        private static bool _isDucked = false;
        private static bool _skipNextRestore = false;
        private static bool _wasPlayingBeforeDuck = false;
        private static float _originalVolume = 1.0f;
        
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        
        private const int VK_VOLUME_MUTE = 0xAD;
        private const int VK_VOLUME_DOWN = 0xAE;
        private const int VK_VOLUME_UP = 0xAF;
        private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        
        /// <summary>
        /// Duck (lower/pause) system audio when voice command starts
        /// Only pauses if music is currently playing
        /// </summary>
        public static async Task DuckAudioAsync()
        {
            // If skip flag is set, don't duck (music was just started)
            if (_skipNextRestore)
            {
                Debug.WriteLine("[AudioDucking] Skip flag set - not ducking");
                return;
            }
            
            if (_isDucked) return;
            
            try
            {
                // Check if Spotify or other media is actually playing
                _wasPlayingBeforeDuck = IsMediaPlaying();
                
                if (!_wasPlayingBeforeDuck)
                {
                    Debug.WriteLine("[AudioDucking] No media playing - skipping duck");
                    _isDucked = true;
                    return;
                }
                
                Debug.WriteLine("[AudioDucking] Ducking audio for voice command");
                _isDucked = true;
                
                // Send media pause key to pause music/video players
                keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, UIntPtr.Zero);
                await Task.Delay(50);
                keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                
                Debug.WriteLine("[AudioDucking] Audio ducked - music should be paused");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioDucking] Error ducking audio: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if media is currently playing (Spotify, etc.)
        /// </summary>
        private static bool IsMediaPlaying()
        {
            try
            {
                var spotifyProcesses = Process.GetProcessesByName("Spotify");
                foreach (var proc in spotifyProcesses)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(proc.MainWindowTitle) && 
                            proc.MainWindowTitle.Contains(" - ") &&
                            !proc.MainWindowTitle.StartsWith("Spotify"))
                        {
                            Debug.WriteLine($"[AudioDucking] Spotify is playing: {proc.MainWindowTitle}");
                            return true;
                        }
                    }
                    catch { }
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioDucking] Error checking media status: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Restore (resume) system audio after voice command completes
        /// </summary>
        public static async Task RestoreAudioAsync()
        {
            if (!_isDucked) return;
            
            if (_skipNextRestore)
            {
                Debug.WriteLine("[AudioDucking] Skipping restore - music was just started");
                _isDucked = false;
                _skipNextRestore = false;
                _wasPlayingBeforeDuck = false;
                return;
            }
            
            if (!_wasPlayingBeforeDuck)
            {
                Debug.WriteLine("[AudioDucking] Skipping restore - music wasn't playing before");
                _isDucked = false;
                _wasPlayingBeforeDuck = false;
                return;
            }
            
            try
            {
                Debug.WriteLine("[AudioDucking] Restoring audio after voice command");
                await Task.Delay(1000);
                
                if (_skipNextRestore)
                {
                    _isDucked = false;
                    _skipNextRestore = false;
                    _wasPlayingBeforeDuck = false;
                    return;
                }
                
                keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, UIntPtr.Zero);
                await Task.Delay(50);
                keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                
                _isDucked = false;
                _wasPlayingBeforeDuck = false;
                Debug.WriteLine("[AudioDucking] Audio restored");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioDucking] Error restoring audio: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Call this when starting new music - prevents ducking/restore from interfering
        /// </summary>
        public static void SkipNextRestore()
        {
            _skipNextRestore = true;
            _isDucked = false;
            _wasPlayingBeforeDuck = false;
            Debug.WriteLine("[AudioDucking] Skip flag SET - won't duck or restore");
        }
        
        /// <summary>
        /// Clear the skip flag when user stops music
        /// </summary>
        public static void ClearMusicProtection()
        {
            _skipNextRestore = false;
            Debug.WriteLine("[AudioDucking] Skip flag CLEARED");
        }
        
        public static bool IsDucked => _isDucked;
        public static bool IsMusicProtectionActive => _skipNextRestore;
        
        public static void ForceRestore()
        {
            _isDucked = false;
            _skipNextRestore = false;
            _wasPlayingBeforeDuck = false;
            Debug.WriteLine("[AudioDucking] Force restored");
        }
    }
}
