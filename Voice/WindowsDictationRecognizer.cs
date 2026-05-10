using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Speech.AudioFormat;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AtlasAI.Voice
{
    public sealed class WindowsDictationRecognizer : IVoiceDictationRecognizer
    {
        private static readonly string _diagPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "voice_diag.log");
        private static void VDiag(string msg)
        {
            try { File.AppendAllText(_diagPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Dictation] {msg}{Environment.NewLine}"); } catch { }
        }

        private sealed class PcmPipeStream : Stream
        {
            private readonly object _gate = new object();
            private readonly AutoResetEvent _dataAvailable = new AutoResetEvent(false);
            private readonly System.Collections.Generic.Queue<byte[]> _queue = new System.Collections.Generic.Queue<byte[]>();
            private byte[]? _current;
            private int _currentOffset;
            private bool _completed;
            private long _position;
            private long _length;

            public void Enqueue(byte[] buffer, int offset, int count)
            {
                if (count <= 0) return;
                var copy = new byte[count];
                Buffer.BlockCopy(buffer, offset, copy, 0, count);
                lock (_gate)
                {
                    if (_completed) return;
                    _queue.Enqueue(copy);
                    _length += count;
                }
                _dataAvailable.Set();
            }

            public void Complete()
            {
                lock (_gate)
                {
                    _completed = true;
                }
                _dataAvailable.Set();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (true)
                {
                    lock (_gate)
                    {
                        if (_current != null && _currentOffset < _current.Length)
                        {
                            var remaining = _current.Length - _currentOffset;
                            var toCopy = Math.Min(count, remaining);
                            Buffer.BlockCopy(_current, _currentOffset, buffer, offset, toCopy);
                            _currentOffset += toCopy;
                            if (_currentOffset >= _current.Length)
                            {
                                _current = null;
                                _currentOffset = 0;
                            }
                            _position += toCopy;
                            return toCopy;
                        }

                        if (_queue.Count > 0)
                        {
                            _current = _queue.Dequeue();
                            _currentOffset = 0;
                            continue;
                        }

                        if (_completed)
                            return 0;
                    }

                    // System.Speech expects a continuous audio stream.
                    // If we block here waiting for captured audio, the engine can stall.
                    // Instead, if no audio arrives quickly, return a small chunk of silence.
                    if (!_dataAvailable.WaitOne(20))
                    {
                        var n = Math.Min(count, 320); // ~10ms of 16kHz 16-bit mono PCM
                        Array.Clear(buffer, offset, n);
                        lock (_gate)
                        {
                            _position += n;
                            _length += n;
                        }
                        return n;
                    }
                }
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length
            {
                get
                {
                    lock (_gate)
                    {
                        return _length;
                    }
                }
            }

            public override long Position
            {
                get
                {
                    lock (_gate)
                    {
                        return _position;
                    }
                }
                set
                {
                    lock (_gate)
                    {
                        // Some engines reset Position to 0 when initializing.
                        // For a live stream, we treat this as "start fresh".
                        if (value <= 0)
                        {
                            _queue.Clear();
                            _current = null;
                            _currentOffset = 0;
                            _position = 0;
                            _length = 0;
                            return;
                        }

                        // We can't truly seek forward/backward in a live stream.
                        // Clamp to current position to avoid throwing.
                        _position = Math.Min(value, _position);
                    }
                }
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin)
            {
                lock (_gate)
                {
                    long target = origin switch
                    {
                        SeekOrigin.Begin => offset,
                        SeekOrigin.Current => _position + offset,
                        SeekOrigin.End => _length + offset,
                        _ => _position
                    };

                    if (target <= 0)
                    {
                        Position = 0;
                        return 0;
                    }

                    // Clamp to current position for safety.
                    _position = Math.Min(target, _position);
                    return _position;
                }
            }

            public override void SetLength(long value)
            {
                lock (_gate)
                {
                    _length = Math.Max(0, value);
                    if (_position > _length) _position = _length;
                }
            }
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                try { if (disposing) _dataAvailable.Dispose(); } catch { }
                base.Dispose(disposing);
            }
        }

        private SpeechRecognitionEngine? _engine;
        private WasapiCapture? _capture;
        private BufferedWaveProvider? _buffered;
        private IWaveProvider? _pcm16Provider;
        private CancellationTokenSource? _pumpCts;
        private Task? _pumpTask;
        private PcmPipeStream? _pipe;
        private string _inputDeviceName = "";

        private bool _isListening;
        private bool _stopRequested;
        private string _lastHypothesis = "";
        private DateTime _lastHypothesisUtc;
        private string _bestRecognized = "";
        private float _bestConfidence;
        private int _lastAudioLevel;
        private DateTime _lastAudioLevelUtc;

        private string _lastSignalProblem = "";
        private DateTime _lastSignalProblemUtc;
        private bool _hadNoSignal;
        private DateTime _firstNoSignalUtc;
        private bool _usingCustomInput;

        public event EventHandler<string>? SpeechRecognized;
        public event EventHandler<string>? SpeechHypothesized;
        public event EventHandler<int>? AudioLevelUpdated;
        public event EventHandler<string>? RecognitionError;
        public event EventHandler? RecognitionComplete;

        public bool IsListening => _isListening;

        public string InputDeviceName => _inputDeviceName;

        public bool Start()
        {
            if (_isListening) return true;
            _stopRequested = false;

            try
            {
                var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                Debug.WriteLine($"[WindowsDictation] Installed recognizers: {recognizers?.Count ?? 0}");
                try { AppLogger.LogInfo($"[WindowsDictation] Installed recognizers: {recognizers?.Count ?? 0}"); } catch { }
                if (recognizers != null)
                {
                    foreach (var r in recognizers)
                    {
                        Debug.WriteLine($"[WindowsDictation] - {r.Name} ({r.Culture})");
                        try { AppLogger.LogInfo($"[WindowsDictation] - {r.Name} ({r.Culture})"); } catch { }
                    }
                }

                // Prefer a recognizer matching OS culture first, then fall back.
                // This reduces "mumbo jumbo" caused by a mismatched language model.
                RecognizerInfo? preferred = null;
                if (recognizers != null && recognizers.Count > 0)
                {
                    CultureInfo? osCulture = null;
                    try { osCulture = CultureInfo.CurrentUICulture; } catch { }
                    osCulture ??= CultureInfo.CurrentCulture;

                    if (osCulture != null)
                    {
                        preferred = recognizers.FirstOrDefault(r => Equals(r.Culture, osCulture))
                            ?? recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName.Equals(osCulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
                    }

                    preferred ??= recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase));
                    preferred ??= recognizers.FirstOrDefault();
                }

                _engine?.Dispose();
                
                try 
                {
                    if (preferred != null)
                    {
                        _engine = new SpeechRecognitionEngine(preferred);
                    }
                    else
                    {
                        // Try default constructor which uses system default
                        _engine = new SpeechRecognitionEngine();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WindowsDictation] Failed to create engine: {ex.Message}");
                    RecognitionError?.Invoke(this, "Speech Engine unavailable. Please check Windows Speech settings.");
                    return false;
                }

                try
                {
                    _engine.BabbleTimeout = TimeSpan.FromSeconds(10);
                    _engine.InitialSilenceTimeout = TimeSpan.FromSeconds(10);
                    _engine.EndSilenceTimeout = TimeSpan.FromSeconds(2.0);
                    _engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(3.0);
                }
                catch
                {
                }

                // Prefer WASAPI capture (selected mic if set, otherwise Windows default endpoint).
                // On some systems, SetInputToDefaultAudioDevice can bind to an endpoint that produces no samples
                // (leading to repeated NoSignal). Feeding PCM via SetInputToAudioStream avoids that.
                // However, SetInputToAudioStream with a live pipe can cause the engine to complete immediately
                // if the pipe has no buffered data yet. So try default device first, fall back to WASAPI.
                bool inputReady = false;
                try
                {
                    _engine.SetInputToDefaultAudioDevice();
                    _usingCustomInput = false;
                    _inputDeviceName = "Windows default";
                    inputReady = true;
                    VDiag("[WindowsDictation] Using SetInputToDefaultAudioDevice()");
                    try { AppLogger.LogInfo("[WindowsDictation] Using SetInputToDefaultAudioDevice()"); } catch { }
                }
                catch (Exception exDefault)
                {
                    VDiag($"[WindowsDictation] Default audio device failed: {exDefault.Message}, trying WASAPI");
                    Debug.WriteLine($"[WindowsDictation] Default audio device failed: {exDefault.Message}");
                }

                if (!inputReady)
                {
                var customOk = TrySetInputFromSelectedMicrophone(_engine);
                if (customOk)
                {
                    _usingCustomInput = true;
                    inputReady = true;
                    try { AppLogger.LogInfo($"[WindowsDictation] Using WASAPI input: {_inputDeviceName}"); } catch { }
                }
                }

                if (!inputReady)
                {
                    VDiag("[WindowsDictation] No audio input available!");
                    RecognitionError?.Invoke(this, "No microphone available for dictation");
                    return false;
                }

                try
                {
                    var dict = new DictationGrammar();
                    dict.Name = "dictation";
                    _engine.LoadGrammar(dict);
                }
                catch
                {
                }

                try
                {
                    var gb = new GrammarBuilder();
                    gb.AppendWildcard();
                    var wild = new Grammar(gb) { Name = "wildcard" };
                    _engine.LoadGrammar(wild);
                }
                catch
                {
                }

                _engine.SpeechRecognized += OnSpeechRecognized;
                _engine.SpeechHypothesized += OnHypothesized;
                _engine.RecognizeCompleted += OnRecognizeCompleted;
                _engine.SpeechRecognitionRejected += OnRejected;
                _engine.AudioSignalProblemOccurred += OnSignalProblem;
                _engine.AudioLevelUpdated += OnAudioLevelUpdated;

                _isListening = true;
                _lastHypothesis = "";
                _bestRecognized = "";
                _bestConfidence = 0;
                _lastAudioLevel = 0;
                _lastAudioLevelUtc = DateTime.MinValue;
                _lastSignalProblem = "";
                _lastSignalProblemUtc = DateTime.MinValue;
                _hadNoSignal = false;
                _firstNoSignalUtc = DateTime.MinValue;
                VDiag($"RecognizeAsync starting, customInput={_usingCustomInput}, device={_inputDeviceName}");
                _engine.RecognizeAsync(RecognizeMode.Multiple);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsDictation] Start error: {ex.Message}");
                RecognitionError?.Invoke(this, ex.Message);
                Cleanup();
                return false;
            }
        }

        private bool TrySetInputFromSelectedMicrophone(SpeechRecognitionEngine engine)
        {
            try
            {
                MMDevice? device = null;
                try
                {
                    using var enumerator = new MMDeviceEnumerator();

                    // Prefer Atlas app mic selection first.
                    string preferredId = "";
                    try
                    {
                        preferredId = (PreferencesStore.Instance.Current.MicrophoneDeviceId ?? "").Trim();
                    }
                    catch
                    {
                    }

                    if (!string.IsNullOrWhiteSpace(preferredId) && !string.Equals(preferredId, "auto", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var byId = enumerator.GetDevice(preferredId);
                            if (byId != null && byId.State == DeviceState.Active)
                                device = byId;
                        }
                        catch
                        {
                        }
                    }

                    device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                    device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                }
                catch
                {
                }

                if (device == null)
                    return false;

                _inputDeviceName = device.FriendlyName;
                Debug.WriteLine($"[WindowsDictation] Using WASAPI capture device: {_inputDeviceName}");

                _pipe?.Dispose();
                _pipe = new PcmPipeStream();

                _pumpCts?.Cancel();
                _pumpCts?.Dispose();
                _pumpCts = new CancellationTokenSource();

                _capture?.Dispose();
                _capture = null;

                // Shared mode to avoid fighting other audio users.
                try
                {
                    _capture = new WasapiCapture(device, true, 100);
                }
                catch
                {
                    _capture = new WasapiCapture(device, true);
                }

                // Buffer raw capture data, then resample + convert to PCM16 mono for System.Speech.
                _buffered = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true
                };

                _capture.DataAvailable += OnCaptureData;

                ISampleProvider sample = _buffered.ToSampleProvider();
                if (sample.WaveFormat.Channels > 1)
                {
                    sample = new StereoToMonoSampleProvider(sample)
                    {
                        LeftVolume = 0.5f,
                        RightVolume = 0.5f
                    };
                }

                sample = new WdlResamplingSampleProvider(sample, 16000);
                _pcm16Provider = new SampleToWaveProvider16(sample);

                // SpeechRecognitionEngine expects raw PCM matching SpeechAudioFormatInfo.
                engine.SetInputToAudioStream(_pipe, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));

                _pumpTask = Task.Run(() => PumpAudioToStream(_pcm16Provider!, _pipe, _pumpCts.Token));

                _capture.StartRecording();
                try { AudioCoordinator.NotifyVoiceRecordingStarted(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsDictation] Failed to set custom audio input: {ex.Message}");

                try { _pumpCts?.Cancel(); } catch { }
                try { _capture?.StopRecording(); } catch { }
                try { _pipe?.Complete(); } catch { }
                try { _capture?.DataAvailable -= OnCaptureData; } catch { }
                _buffered = null;
                _pcm16Provider = null;
                try { _capture?.Dispose(); } catch { }
                _capture = null;
                try { _pipe?.Dispose(); } catch { }
                _pipe = null;

                try
                {
                    RecognitionError?.Invoke(this, $"Windows Speech mic init failed: {ex.Message}");
                }
                catch
                {
                }
                return false;
            }
        }

        private void OnCaptureData(object? sender, WaveInEventArgs e)
        {
            try
            {
                _buffered?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
            catch
            {
            }
        }

        private int _pumpLogCount = 0;
        private void PumpAudioToStream(IWaveProvider provider, PcmPipeStream pipe, CancellationToken token)
        {
            var buffer = new byte[4096];
            var lastLevelUtc = DateTime.UtcNow;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = provider.Read(buffer, 0, buffer.Length);
                    }
                    catch
                    {
                        read = 0;
                    }

                    if (read > 0)
                    {
                        if (System.Threading.Interlocked.Increment(ref _pumpLogCount) <= 3)
                            VDiag($"PumpAudio: read={read} bytes");
                        // Compute a simple audio level (0-100) from 16-bit mono PCM.
                        // This is used by VoiceSystemOrchestrator's silence/stability heuristics.
                        try
                        {
                            long sumAbs = 0;
                            int sampleCount = 0;
                            for (int i = 0; i + 1 < read; i += 2)
                            {
                                short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                                sumAbs += Math.Abs(sample);
                                sampleCount++;
                            }

                            if (sampleCount > 0)
                            {
                                var avg = (double)sumAbs / sampleCount;
                                var level = (int)Math.Max(0, Math.Min(100, (avg / 32767.0) * 100.0));

                                // Throttle updates to avoid overwhelming the UI.
                                var now = DateTime.UtcNow;
                                if ((now - lastLevelUtc) >= TimeSpan.FromMilliseconds(120))
                                {
                                    lastLevelUtc = now;
                                    _lastAudioLevel = level;
                                    _lastAudioLevelUtc = now;
                                    try { AudioLevelUpdated?.Invoke(this, level); } catch { }
                                }
                            }
                        }
                        catch
                        {
                        }

                        pipe.Enqueue(buffer, 0, read);
                        continue;
                    }

                    Thread.Sleep(10);
                }
            }
            finally
            {
                try { pipe.Complete(); } catch { }
            }
        }

        public void Stop()
        {
            if (!_isListening) return;
            _stopRequested = true;
            try
            {
                _engine?.RecognizeAsyncCancel();
                _engine?.RecognizeAsyncStop();
            }
            catch
            {
            }
            finally
            {
                Cleanup();
            }
        }

        private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            try
            {
                if (!_isListening) return;
                var text = (e.Result?.Text ?? "").Trim();
                VDiag($"SpeechRecognized: '{text}' conf={e.Result?.Confidence}");
                if (string.IsNullOrWhiteSpace(text)) return;
                var conf = e.Result.Confidence;
                if (conf > _bestConfidence)
                {
                    _bestConfidence = conf;
                    _bestRecognized = text;
                }

                // Be conservative: avoid emitting low-confidence garbage.
                // System.Speech can produce very short/incorrect recognitions with tiny confidence.
                // Let the orchestrator's silence/stability logic work with hypotheses instead.
                var wordCount = 0;
                try { wordCount = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length; } catch { }

                var accept = conf >= 0.30
                    || (conf >= 0.22 && text.Length >= 12)
                    || (conf >= 0.20 && wordCount >= 3);

                if (!accept)
                    return;

                SpeechRecognized?.Invoke(this, text);
            }
            catch
            {
            }
        }

        private void OnHypothesized(object? sender, SpeechHypothesizedEventArgs e)
        {
            try
            {
                if (!_isListening) return;
                var text = (e.Result?.Text ?? "").Trim();
                VDiag($"Hypothesized: '{text}'");
                if (string.IsNullOrWhiteSpace(text)) return;
                _lastHypothesis = text;
                _lastHypothesisUtc = DateTime.UtcNow;
                SpeechHypothesized?.Invoke(this, text);
            }
            catch
            {
            }
        }

        private int _audioLevelLogCount = 0;
        private void OnAudioLevelUpdated(object? sender, AudioLevelUpdatedEventArgs e)
        {
            try
            {
                if (!_isListening) return;
                if (System.Threading.Interlocked.Increment(ref _audioLevelLogCount) <= 5)
                    VDiag($"AudioLevel: {e.AudioLevel}");
                _lastAudioLevel = e.AudioLevel;
                _lastAudioLevelUtc = DateTime.UtcNow;
                AudioLevelUpdated?.Invoke(this, e.AudioLevel);
            }
            catch
            {
            }
        }

        private void OnRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
        {
            try
            {
                if (!_isListening) return;

                // If the engine reports no mic signal, don't "guess" transcripts.
                if (_hadNoSignal) return;

                // Do not emit transcripts from rejected events.
                // This path is a major source of "mumbo jumbo" commits.
                var heardAudioRecently = _lastAudioLevel >= 8 && (DateTime.UtcNow - _lastAudioLevelUtc) <= TimeSpan.FromSeconds(4);
                if (!heardAudioRecently)
                    return;

                if (_lastAudioLevel > 0 && (DateTime.UtcNow - _lastAudioLevelUtc) <= TimeSpan.FromSeconds(5))
                {
                    // If we heard audio but got no hypothesis, try to prompt user to speak louder
                    // But don't fail immediately - let the retry logic handle it
                    // RecognitionError?.Invoke(this, $"Couldn't understand (audio { _lastAudioLevel })");
                    return;
                }

                // RecognitionError?.Invoke(this, "Couldn't understand - try again");
            }
            catch
            {
            }
        }

        private void OnSignalProblem(object? sender, AudioSignalProblemOccurredEventArgs e)
        {
            try
            {
                if (!_isListening) return;
                VDiag($"SignalProblem: {e.AudioSignalProblem}");

                // Signal problems (NoSignal, TooLoud, TooSoft, TooNoisy, etc.) are TRANSIENT
                // audio quality events. The engine continues listening through them.
                // Do NOT raise RecognitionError here — that causes the orchestrator to abort
                // recognition and commit whatever garbage partial hypothesis exists.
                // The orchestrator's own 15-second timeout and silence/stability detection
                // handle the case where the mic truly isn't working.

                if (e.AudioSignalProblem == AudioSignalProblem.NoSignal)
                {
                    if (_firstNoSignalUtc == DateTime.MinValue)
                        _firstNoSignalUtc = DateTime.UtcNow;
                    _hadNoSignal = true;
                }
            }
            catch
            {
            }
        }

        private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
        {
            try
            {
                VDiag($"RecognizeCompleted cancelled={e.Cancelled} error={e.Error?.Message}");
                RecognitionComplete?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
            }

            // In continuous mode, RecognizeCompleted can fire due to internal engine stops.
            // Only clean up when Stop() was explicitly requested.
            if (_stopRequested)
            {
                Cleanup();
                return;
            }

            try
            {
                if (!_isListening || _engine == null) return;
                _engine.RecognizeAsync(RecognizeMode.Multiple);
            }
            catch
            {
            }
        }

        private void Cleanup()
        {
            _isListening = false;
            _stopRequested = false;
            if (_engine == null) return;
            try
            {
                _engine.SpeechRecognized -= OnSpeechRecognized;
                _engine.SpeechHypothesized -= OnHypothesized;
                _engine.RecognizeCompleted -= OnRecognizeCompleted;
                _engine.SpeechRecognitionRejected -= OnRejected;
                _engine.AudioSignalProblemOccurred -= OnSignalProblem;
                _engine.AudioLevelUpdated -= OnAudioLevelUpdated;
            }
            catch
            {
            }

            try { _pumpCts?.Cancel(); } catch { }
            try { _capture?.StopRecording(); } catch { }
            try { _pipe?.Complete(); } catch { }
            try { AudioCoordinator.NotifyVoiceRecordingStopped(); } catch { }

            try { _pumpTask?.Wait(400); } catch { }
            _pumpTask = null;

            try { _pumpCts?.Dispose(); } catch { }
            _pumpCts = null;

            try { _capture?.DataAvailable -= OnCaptureData; } catch { }
            _buffered = null;
            _pcm16Provider = null;
            try { _capture?.Dispose(); } catch { }
            _capture = null;
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch
            {
            }

            try
            {
                _engine?.Dispose();
            }
            catch
            {
            }
            _engine = null;
        }
    }
}

