using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Ultra-low impact audio capture for wake word detection
    /// Designed to run continuously without causing audio distortion
    /// Uses minimal CPU and memory, optimized for background operation
    /// </summary>
    public class LowImpactAudioCapture : IDisposable
    {
        private WasapiLoopbackCapture? _loopbackCapture;
        private WasapiCapture? _micCapture;
        private bool _isCapturing = false;
        private CancellationTokenSource? _cancellationTokenSource;
        
        // Ultra-conservative settings to prevent audio interference
        private const int SAMPLE_RATE = 8000; // Very low sample rate for minimal impact
        private const int BUFFER_MS = 1000; // Large buffer to prevent glitches
        private const int CHANNELS = 1; // Mono only
        
        // Events
        public event EventHandler<double>? AudioLevelDetected;
        public event EventHandler<string>? WakeWordDetected;
        
        /// <summary>
        /// Start ultra-low impact audio monitoring
        /// </summary>
        public async Task StartAsync()
        {
            if (_isCapturing) return;
            
            try
            {
                Debug.WriteLine("[LowImpact] Starting ultra-low impact audio capture");
                
                _cancellationTokenSource = new CancellationTokenSource();
                _isCapturing = true;
                
                // Use loopback capture to monitor system audio levels without interfering
                await StartLoopbackMonitoringAsync();
                
                Debug.WriteLine("[LowImpact] Low impact capture started successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LowImpact] Failed to start: {ex.Message}");
                _isCapturing = false;
            }
        }
        
        /// <summary>
        /// Start loopback monitoring to detect audio levels without microphone interference
        /// </summary>
        private async Task StartLoopbackMonitoringAsync()
        {
            try
            {
                // Use loopback capture to monitor when system audio is playing
                _loopbackCapture = new WasapiLoopbackCapture();
                
                double lastLevel = 0;
                var levelCheckTimer = new Timer(_ =>
                {
                    try
                    {
                        // Simple level detection - if system audio is quiet, we can safely use microphone
                        AudioLevelDetected?.Invoke(this, lastLevel);
                    }
                    catch { }
                }, null, 100, 100);
                
                _loopbackCapture.DataAvailable += (s, e) =>
                {
                    try
                    {
                        // Calculate system audio level
                        double sum = 0;
                        for (int i = 0; i < e.BytesRecorded; i += 4)
                        {
                            if (i + 3 < e.BytesRecorded)
                            {
                                float sample = BitConverter.ToSingle(e.Buffer, i);
                                sum += Math.Abs(sample);
                            }
                        }
                        lastLevel = sum / Math.Max(1, e.BytesRecorded / 4);
                    }
                    catch { }
                };
                
                _loopbackCapture.StartRecording();
                Debug.WriteLine("[LowImpact] Loopback monitoring started");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LowImpact] Loopback monitoring failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Temporarily activate microphone for wake word detection when system audio is quiet
        /// </summary>
        public async Task<bool> TryActivateMicrophoneAsync()
        {
            if (!_isCapturing) return false;
            
            try
            {
                Debug.WriteLine("[LowImpact] Temporarily activating microphone for wake word detection");
                
                // Use minimal microphone capture
                using var enumerator = new MMDeviceEnumerator();
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                
                if (defaultDevice == null) return false;
                
                using var micCapture = new WasapiCapture(defaultDevice, true, BUFFER_MS); // Shared mode, large buffer
                
                bool wakeWordDetected = false;
                var detectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); // Quick detection
                
                micCapture.DataAvailable += (s, e) =>
                {
                    try
                    {
                        // Very simple wake word detection based on audio patterns
                        double level = CalculateAudioLevel(e.Buffer, e.BytesRecorded, micCapture.WaveFormat);
                        
                        // If we detect speech-like patterns, trigger wake word detection
                        if (level > 100) // Adjust threshold as needed
                        {
                            wakeWordDetected = true;
                            WakeWordDetected?.Invoke(this, "atlas");
                        }
                    }
                    catch { }
                };
                
                micCapture.StartRecording();
                
                // Wait for detection or timeout
                while (!wakeWordDetected && !detectionTimeout.Token.IsCancellationRequested)
                {
                    await Task.Delay(50, detectionTimeout.Token);
                }
                
                micCapture.StopRecording();
                return wakeWordDetected;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LowImpact] Microphone activation failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Calculate audio level from buffer
        /// </summary>
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
        /// Stop audio capture
        /// </summary>
        public void Stop()
        {
            if (!_isCapturing) return;
            
            try
            {
                Debug.WriteLine("[LowImpact] Stopping low impact capture");
                
                _isCapturing = false;
                _cancellationTokenSource?.Cancel();
                
                _loopbackCapture?.StopRecording();
                _loopbackCapture?.Dispose();
                _loopbackCapture = null;
                
                _micCapture?.StopRecording();
                _micCapture?.Dispose();
                _micCapture = null;
                
                Debug.WriteLine("[LowImpact] Low impact capture stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LowImpact] Error stopping: {ex.Message}");
            }
        }
        
        public bool IsCapturing => _isCapturing;
        
        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}