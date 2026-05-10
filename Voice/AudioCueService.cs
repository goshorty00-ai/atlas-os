using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Optional micro audio cues for UI feedback.
    /// OFF by default, extremely low volume, one sound per event max.
    /// 
    /// SAFETY: No system authority, just plays embedded audio.
    /// - No file system access beyond AppData
    /// - No network calls
    /// - No elevated permissions
    /// </summary>
    public class AudioCueService : IDisposable
    {
        private static AudioCueService? _instance;
        private static readonly object _lock = new();

        public static AudioCueService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new AudioCueService();
                    }
                }
                return _instance;
            }
        }

        // Audio cue types
        public enum CueType
        {
            FocusGained,        // Window/app gains focus
            WorkflowStarted,    // Workflow chain begins
            WorkflowCompleted,  // Workflow chain finishes
            CommandAcknowledged, // Command palette selection
            WakeWordDetected,   // Wake word heard
            ListeningStarted,   // Started listening for command
            ThinkingStarted,    // Processing command
            TaskCompleted       // Task finished successfully
        }

        // Settings
        private bool _enabled = false;
        private double _volume = 0.15; // Very low default (0-1)
        private bool _isPlaying = false;

        private AudioCueService()
        {
            // Subscribe to preference changes
            try
            {
                PreferencesStore.Instance.PreferencesChanged += OnPreferencesChanged;
                var prefs = PreferencesStore.Instance.Current;
                _enabled = prefs.EnableAudioCues;
                _volume = prefs.AudioCueVolume;
            }
            catch { /* Preferences may not be initialized */ }
        }

        private void OnPreferencesChanged(object? sender, UserPreferences prefs)
        {
            _enabled = prefs.EnableAudioCues;
            _volume = prefs.AudioCueVolume;
        }

        /// <summary>
        /// Play an audio cue if enabled. Non-blocking, one sound at a time.
        /// </summary>
        public void PlayCue(CueType cue)
        {
            if (!_enabled || _isPlaying || _volume < 0.01)
                return;

            _ = PlayCueAsync(cue);
        }

        private async Task PlayCueAsync(CueType cue)
        {
            if (_isPlaying) return;
            _isPlaying = true;

            try
            {
                // Generate simple tone based on cue type
                // Using System.Media.SoundPlayer with generated WAV data
                var frequency = cue switch
                {
                    CueType.FocusGained => 880,        // A5 - bright, attention
                    CueType.WorkflowStarted => 523,    // C5 - neutral start
                    CueType.WorkflowCompleted => 1047, // C6 - higher, completion
                    CueType.CommandAcknowledged => 659, // E5 - acknowledgment
                    CueType.WakeWordDetected => 784,   // G5 - wake word detected
                    CueType.ListeningStarted => 698,   // F5 - listening for command
                    CueType.ThinkingStarted => 587,    // D5 - processing
                    CueType.TaskCompleted => 1175,     // D6 - task done
                    _ => 660
                };

                var duration = cue switch
                {
                    CueType.FocusGained => 80,
                    CueType.WorkflowStarted => 100,
                    CueType.WorkflowCompleted => 150,
                    CueType.CommandAcknowledged => 60,
                    CueType.WakeWordDetected => 120,
                    CueType.ListeningStarted => 90,
                    CueType.ThinkingStarted => 70,
                    CueType.TaskCompleted => 180,
                    _ => 80
                };

                await PlayToneAsync(frequency, duration);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioCue] Error playing cue: {ex.Message}");
            }
            finally
            {
                _isPlaying = false;
            }
        }

        /// <summary>
        /// Generate and play a simple sine wave tone
        /// </summary>
        private async Task PlayToneAsync(int frequency, int durationMs)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Generate WAV data in memory
                    var sampleRate = 44100;
                    var samples = (int)(sampleRate * durationMs / 1000.0);
                    var amplitude = (short)(short.MaxValue * _volume * 0.3); // Extra reduction for subtlety

                    using var ms = new MemoryStream();
                    using var writer = new BinaryWriter(ms);

                    // WAV header
                    writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                    writer.Write(36 + samples * 2); // File size
                    writer.Write(new char[] { 'W', 'A', 'V', 'E' });
                    writer.Write(new char[] { 'f', 'm', 't', ' ' });
                    writer.Write(16); // Subchunk1Size
                    writer.Write((short)1); // AudioFormat (PCM)
                    writer.Write((short)1); // NumChannels
                    writer.Write(sampleRate); // SampleRate
                    writer.Write(sampleRate * 2); // ByteRate
                    writer.Write((short)2); // BlockAlign
                    writer.Write((short)16); // BitsPerSample
                    writer.Write(new char[] { 'd', 'a', 't', 'a' });
                    writer.Write(samples * 2); // Subchunk2Size

                    // Generate sine wave with fade in/out
                    for (int i = 0; i < samples; i++)
                    {
                        double t = (double)i / sampleRate;
                        double envelope = 1.0;

                        // Fade in (first 10%)
                        if (i < samples * 0.1)
                            envelope = i / (samples * 0.1);
                        // Fade out (last 30%)
                        else if (i > samples * 0.7)
                            envelope = (samples - i) / (samples * 0.3);

                        double sample = Math.Sin(2 * Math.PI * frequency * t) * envelope;
                        writer.Write((short)(sample * amplitude));
                    }

                    // Play the generated audio
                    ms.Position = 0;
                    using var player = new SoundPlayer(ms);
                    player.PlaySync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioCue] Tone generation error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Enable or disable audio cues
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            PreferencesStore.Instance.Update(p => p.EnableAudioCues = enabled);
        }

        /// <summary>
        /// Set audio cue volume (0-1)
        /// </summary>
        public void SetVolume(double volume)
        {
            _volume = Math.Clamp(volume, 0, 1);
            PreferencesStore.Instance.Update(p => p.AudioCueVolume = _volume);
        }

        /// <summary>
        /// Check if audio cues are enabled
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// Get current volume level
        /// </summary>
        public double Volume => _volume;

        public void Dispose()
        {
            try
            {
                PreferencesStore.Instance.PreferencesChanged -= OnPreferencesChanged;
            }
            catch { }
        }
    }
}
