using System;
using System.Diagnostics;
using System.IO;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AtlasAI.Voice
{
    public class WakeWordDetector : IDisposable
    {
        private SpeechRecognitionEngine? _recognizer;
        private WaveInEvent? _waveIn;
        private MemoryStream? _audioStream;
        private bool _isListening;
        private bool _isDisposed;
        private int _audioLevel;
        
        public event EventHandler<string>? WakeWordDetected;
        public event EventHandler<string>? Error;
        public event EventHandler<string>? AudioStateChanged;
        
        public bool IsListening => _isListening;
        public int AudioLevel => _audioLevel;
        
        public static bool IsSpeechRecognitionAvailable()
        {
            try
            {
                var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                return recognizers.Count > 0;
            }
            catch
            {
                return false;
            }
        }
        
        public static string GetDefaultRecordingDeviceName()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                return defaultDevice?.FriendlyName ?? "Unknown";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordDetector] Failed to get default device: {ex.Message}");
                return "Unknown";
            }
        }
        
        public WakeWordDetector()
        {
            Debug.WriteLine("[WakeWordDetector] Constructor called");
        }
        
        public void StartListening()
        {
            if (_isListening)
            {
                Debug.WriteLine("[WakeWordDetector] Already listening");
                return;
            }
            
            if (_isDisposed)
            {
                Debug.WriteLine("[WakeWordDetector] Cannot start - disposed");
                return;
            }
            
            try
            {
                Debug.WriteLine("[WakeWordDetector] ========== STARTING WAKE WORD DETECTION ==========");
                
                // Initialize speech recognizer
                _recognizer = new SpeechRecognitionEngine();
                Debug.WriteLine($"[WakeWordDetector] Engine: {_recognizer.RecognizerInfo.Name}");
                
                // Load grammars
                var wakeWords = new Choices("Atlas", "atlas", "at last", "Hey Atlas", "OK Atlas", "Atlus", "Alice");
                var grammarBuilder = new GrammarBuilder(wakeWords);
                grammarBuilder.Culture = _recognizer.RecognizerInfo.Culture;
                var grammar = new Grammar(grammarBuilder) { Name = "WakeWord", Weight = 1.0f };
                _recognizer.LoadGrammar(grammar);
                
                var dictationGrammar = new DictationGrammar() { Name = "Dictation", Weight = 0.1f };
                _recognizer.LoadGrammar(dictationGrammar);
                Debug.WriteLine("[WakeWordDetector] Grammars loaded");
                
                // Set timeouts
                _recognizer.BabbleTimeout = TimeSpan.FromHours(24);
                _recognizer.InitialSilenceTimeout = TimeSpan.FromHours(24);
                _recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(500);
                _recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(500);
                
                // Subscribe to events
                _recognizer.SpeechRecognized += OnSpeechRecognized;
                _recognizer.SpeechHypothesized += OnSpeechHypothesized;
                _recognizer.SpeechDetected += OnSpeechDetected;
                _recognizer.RecognizeCompleted += OnRecognizeCompleted;
                _recognizer.AudioStateChanged += OnAudioStateChanged;
                _recognizer.AudioLevelUpdated += OnAudioLevelUpdated;
                
                // Try SetInputToDefaultAudioDevice first
                var deviceName = GetDefaultRecordingDeviceName();
                Debug.WriteLine($"[WakeWordDetector] Default device: {deviceName}");
                
                _recognizer.SetInputToDefaultAudioDevice();
                Debug.WriteLine("[WakeWordDetector] SetInputToDefaultAudioDevice() called");
                
                // Start recognition
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                _isListening = true;
                
                Debug.WriteLine("[WakeWordDetector] ✅ NOW LISTENING for 'Atlas'");
                Debug.WriteLine($"[WakeWordDetector] BabbleTimeout: {_recognizer.BabbleTimeout}");
                Debug.WriteLine($"[WakeWordDetector] InitialSilenceTimeout: {_recognizer.InitialSilenceTimeout}");
                
                // Start a background task to monitor audio level
                Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    if (_recognizer != null && _isListening)
                    {
                        Debug.WriteLine($"[WakeWordDetector] Audio level after 1s: {_recognizer.AudioLevel}");
                        if (_recognizer.AudioLevel == 0)
                        {
                            Debug.WriteLine("[WakeWordDetector] ⚠️ WARNING: Audio level is 0 - microphone may not be working!");
                            Debug.WriteLine("[WakeWordDetector] Trying NAudio fallback...");
                            TryNAudioFallback();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordDetector] ❌ Failed to start: {ex.Message}");
                Debug.WriteLine($"[WakeWordDetector] Stack: {ex.StackTrace}");
                _isListening = false;
                Error?.Invoke(this, ex.Message);
            }
        }
        
        private void TryNAudioFallback()
        {
            try
            {
                Debug.WriteLine("[WakeWordDetector] Attempting NAudio direct capture fallback...");
                
                // Stop current recognition
                try { _recognizer?.RecognizeAsyncStop(); } catch { }
                
                // Find the default recording device
                int deviceIndex = -1;
                for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                {
                    var caps = WaveInEvent.GetCapabilities(i);
                    Debug.WriteLine($"[WakeWordDetector] NAudio Device {i}: {caps.ProductName}");
                    if (deviceIndex == -1) deviceIndex = i;
                }
                
                if (deviceIndex == -1)
                {
                    Debug.WriteLine("[WakeWordDetector] No NAudio recording devices found!");
                    return;
                }
                
                // Create WaveIn for capturing
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = deviceIndex,
                    WaveFormat = new WaveFormat(16000, 16, 1) // 16kHz, 16-bit, mono - good for speech
                };
                
                _waveIn.DataAvailable += OnWaveInDataAvailable;
                _waveIn.RecordingStopped += OnWaveInRecordingStopped;
                
                // Create a memory stream for the audio
                _audioStream = new MemoryStream();
                
                // Write WAV header
                WriteWavHeader(_audioStream, _waveIn.WaveFormat);
                
                _waveIn.StartRecording();
                Debug.WriteLine($"[WakeWordDetector] NAudio recording started on device {deviceIndex}");
                
                // After collecting some audio, feed it to the recognizer
                Task.Run(async () =>
                {
                    await Task.Delay(2000); // Collect 2 seconds of audio
                    FeedAudioToRecognizer();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordDetector] NAudio fallback failed: {ex.Message}");
            }
        }
        
        private void WriteWavHeader(Stream stream, WaveFormat format)
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0); // Placeholder for file size
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // Subchunk1Size for PCM
            writer.Write((short)1); // AudioFormat (PCM)
            writer.Write((short)format.Channels);
            writer.Write(format.SampleRate);
            writer.Write(format.AverageBytesPerSecond);
            writer.Write((short)format.BlockAlign);
            writer.Write((short)format.BitsPerSample);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(0); // Placeholder for data size
        }
        
        private void OnWaveInDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_audioStream != null && e.BytesRecorded > 0)
            {
                _audioStream.Write(e.Buffer, 0, e.BytesRecorded);
                
                // Calculate audio level
                int max = 0;
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    int abs = Math.Abs(sample);
                    if (abs > max) max = abs;
                }
                _audioLevel = (int)(max * 100.0 / 32768.0);
                
                if (_audioLevel > 5)
                {
                    Debug.WriteLine($"[WakeWordDetector] 📊 NAudio Level: {_audioLevel}");
                }
            }
        }
        
        private void OnWaveInRecordingStopped(object? sender, StoppedEventArgs e)
        {
            Debug.WriteLine($"[WakeWordDetector] NAudio recording stopped: {e.Exception?.Message ?? "no error"}");
        }
        
        private void FeedAudioToRecognizer()
        {
            try
            {
                if (_audioStream == null || _recognizer == null) return;
                
                // Stop recording temporarily
                _waveIn?.StopRecording();
                
                // Update WAV header with actual sizes
                long dataSize = _audioStream.Length - 44;
                _audioStream.Position = 4;
                using (var writer = new BinaryWriter(_audioStream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write((int)(dataSize + 36)); // File size - 8
                }
                _audioStream.Position = 40;
                using (var writer = new BinaryWriter(_audioStream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write((int)dataSize);
                }
                
                // Reset to beginning
                _audioStream.Position = 0;
                
                Debug.WriteLine($"[WakeWordDetector] Feeding {_audioStream.Length} bytes to recognizer...");
                
                // Feed to recognizer
                _recognizer.SetInputToWaveStream(_audioStream);
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                
                Debug.WriteLine("[WakeWordDetector] Audio fed to recognizer");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordDetector] Failed to feed audio: {ex.Message}");
            }
        }
        
        private void OnAudioLevelUpdated(object? sender, AudioLevelUpdatedEventArgs e)
        {
            _audioLevel = e.AudioLevel;
            if (e.AudioLevel > 5)
            {
                Debug.WriteLine($"[WakeWordDetector] 📊 Audio Level: {e.AudioLevel}");
            }
        }
        
        private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs e)
        {
            Debug.WriteLine($"[WakeWordDetector] Audio state: {e.AudioState}");
            AudioStateChanged?.Invoke(this, e.AudioState.ToString());
        }
        
        private void OnSpeechDetected(object? sender, SpeechDetectedEventArgs e)
        {
            Debug.WriteLine($"[WakeWordDetector] 🔊 SPEECH DETECTED at {e.AudioPosition}");
        }
        
        private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
        {
            Debug.WriteLine($"[WakeWordDetector] 🎤 HYPOTHESIZED: '{e.Result.Text}' ({e.Result.Confidence:P0})");
        }
        
        private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            Debug.WriteLine($"[WakeWordDetector] ✅ RECOGNIZED: '{e.Result.Text}' (Confidence: {e.Result.Confidence:P0}, Grammar: {e.Result.Grammar?.Name ?? "unknown"})");
            
            if (e.Result.Confidence < 0.1)
            {
                Debug.WriteLine($"[WakeWordDetector] Confidence too low ({e.Result.Confidence:P0}), ignoring");
                return;
            }
            
            var text = e.Result.Text.ToLower();
            if (text.Contains("atlas") || text.Contains("at last") || text.Contains("atlus") || text.Contains("alice"))
            {
                Debug.WriteLine("[WakeWordDetector] *** WAKE WORD DETECTED! ***");
                WakeWordDetected?.Invoke(this, e.Result.Text);
            }
        }
        
        private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
        {
            Debug.WriteLine($"[WakeWordDetector] RecognizeCompleted - Cancelled: {e.Cancelled}, Error: {e.Error?.Message ?? "none"}, InitialSilenceTimeout: {e.InitialSilenceTimeout}, BabbleTimeout: {e.BabbleTimeout}");
            
            if (_isListening && !_isDisposed && _recognizer != null && !e.Cancelled)
            {
                Task.Delay(200).ContinueWith(_ =>
                {
                    if (_isListening && !_isDisposed && _recognizer != null)
                    {
                        try
                        {
                            Debug.WriteLine("[WakeWordDetector] Restarting recognition...");
                            _recognizer.SetInputToDefaultAudioDevice();
                            _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WakeWordDetector] Restart failed: {ex.Message}");
                        }
                    }
                });
            }
        }
        
        public void StopListening()
        {
            if (!_isListening) return;
            
            Debug.WriteLine("[WakeWordDetector] Stopping...");
            _isListening = false;
            
            try { _waveIn?.StopRecording(); } catch { }
            try { _recognizer?.RecognizeAsyncStop(); } catch { }
        }
        
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            StopListening();
            
            _waveIn?.Dispose();
            _waveIn = null;
            
            _audioStream?.Dispose();
            _audioStream = null;
            
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }
}
