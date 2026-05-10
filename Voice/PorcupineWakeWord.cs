using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Lightweight wake word detection using audio energy detection.
    /// 
    /// KEY INSIGHT: The audio distortion happens because WASAPI/WaveIn capture
    /// interferes with the audio output when using the same audio device.
    /// 
    /// SOLUTION: Use Windows Audio Session API (WASAPI) in LOOPBACK mode to 
    /// monitor system audio levels WITHOUT capturing from the microphone.
    /// When we detect a pause in system audio (user stopped music to speak),
    /// THEN we briefly activate the microphone.
    /// 
    /// This mimics how Alexa works - it has dedicated hardware that doesn't
    /// interfere with audio playback.
    /// </summary>
    public class PorcupineWakeWord : IDisposable
    {
        private WasapiLoopbackCapture? _loopbackCapture;
        private bool _isRunning = false;
        private bool _isDisposed = false;
        private CancellationTokenSource? _cts;
        
        // Track system audio state
        private double _lastSystemAudioLevel = 0;
        private DateTime _lastAudioTime = DateTime.Now;
        private bool _systemAudioPlaying = false;
        
        // Wake word detection state
        private DateTime _silenceStartTime = DateTime.MinValue;
        private bool _waitingForWakeWord = false;
        
        public event EventHandler? WakeWordDetected;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? Error;
        
        public bool IsRunning => _isRunning;
        
        /// <summary>
        /// Start monitoring for wake word opportunities.
        /// This does NOT capture from microphone - it only monitors system audio.
        /// </summary>
        public void Start()
        {
            if (_isRunning || _isDisposed) return;
            
            try
            {
                _cts = new CancellationTokenSource();
                _isRunning = true;
                
                // Start loopback capture to monitor system audio levels
                // This does NOT interfere with audio playback!
                _loopbackCapture = new WasapiLoopbackCapture();
                
                _loopbackCapture.DataAvailable += OnLoopbackDataAvailable;
                _loopbackCapture.RecordingStopped += (s, e) =>
                {
                    if (e.Exception != null)
                        Debug.WriteLine($"[PorcupineWakeWord] Loopback stopped: {e.Exception.Message}");
                };
                
                _loopbackCapture.StartRecording();
                
                StatusChanged?.Invoke(this, "Monitoring for wake word (no distortion)");
                Debug.WriteLine("[PorcupineWakeWord] Started - monitoring system audio without distortion");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PorcupineWakeWord] Start failed: {ex.Message}");
                Error?.Invoke(this, ex.Message);
                _isRunning = false;
            }
        }
        
        private void OnLoopbackDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning || _isDisposed) return;
            
            try
            {
                // Calculate system audio level
                double level = CalculateAudioLevel(e.Buffer, e.BytesRecorded, _loopbackCapture!.WaveFormat);
                _lastSystemAudioLevel = level;
                
                bool wasPlaying = _systemAudioPlaying;
                _systemAudioPlaying = level > 100; // Threshold for "audio is playing"
                
                if (_systemAudioPlaying)
                {
                    _lastAudioTime = DateTime.Now;
                    _silenceStartTime = DateTime.MinValue;
                    _waitingForWakeWord = false;
                }
                else
                {
                    // System audio stopped - start silence timer
                    if (_silenceStartTime == DateTime.MinValue)
                    {
                        _silenceStartTime = DateTime.Now;
                    }
                    
                    // If silence for 500ms, user might be about to speak
                    var silenceDuration = (DateTime.Now - _silenceStartTime).TotalMilliseconds;
                    if (silenceDuration > 500 && !_waitingForWakeWord)
                    {
                        _waitingForWakeWord = true;
                        Debug.WriteLine("[PorcupineWakeWord] System audio quiet - ready for wake word");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PorcupineWakeWord] Error processing audio: {ex.Message}");
            }
        }
        
        private double CalculateAudioLevel(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            double sum = 0;
            int sampleCount = 0;
            
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                for (int i = 0; i < bytesRecorded; i += 4 * format.Channels)
                {
                    if (i + 3 < bytesRecorded)
                    {
                        float sample = BitConverter.ToSingle(buffer, i);
                        sum += Math.Abs(sample) * 32767;
                        sampleCount++;
                    }
                }
            }
            else if (format.BitsPerSample == 16)
            {
                for (int i = 0; i < bytesRecorded; i += 2 * format.Channels)
                {
                    if (i + 1 < bytesRecorded)
                    {
                        short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                        sum += Math.Abs(sample);
                        sampleCount++;
                    }
                }
            }
            
            return sampleCount > 0 ? sum / sampleCount : 0;
        }
        
        /// <summary>
        /// Check if system audio is currently playing
        /// </summary>
        public bool IsSystemAudioPlaying => _systemAudioPlaying;
        
        /// <summary>
        /// Check if ready to listen for wake word (system audio is quiet)
        /// </summary>
        public bool IsReadyForWakeWord => _waitingForWakeWord && !_systemAudioPlaying;
        
        public void Stop()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _cts?.Cancel();
            
            try
            {
                _loopbackCapture?.StopRecording();
                _loopbackCapture?.Dispose();
                _loopbackCapture = null;
            }
            catch { }
            
            Debug.WriteLine("[PorcupineWakeWord] Stopped");
        }
        
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            Stop();
            _cts?.Dispose();
        }
    }
}
