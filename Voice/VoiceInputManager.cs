using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Single source of truth for microphone capture in Atlas-AI.
    /// Provides centralized audio input that works regardless of window focus.
    /// Fail-closed behavior: if mic init fails, set state to Suspended and show UI status.
    /// </summary>
    public class VoiceInputManager : IDisposable
    {
        private static VoiceInputManager? _instance;
        private static readonly object _lock = new object();

        public static VoiceInputManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VoiceInputManager();
                    }
                }
                return _instance;
            }
        }

        // === State ===
        private IWaveIn? _capture;
        private bool _isRunning = false;
        private bool _isDisposed = false;
        private string _suspendReason = "";
        private MMDevice? _currentDevice;
        private WaveFormat? _captureFormat;
        private readonly object _stateLock = new object();

        // === Configuration ===
        private const int SampleRate = 16000;  // 16kHz for speech recognition
        private const int Channels = 1;        // Mono
        private const int BufferMs = 100;      // 100ms buffer for responsiveness

        // === Events ===
        public event EventHandler<AudioFrameEventArgs>? AudioFrame;
        public event EventHandler<string>? StateChanged;
        public event EventHandler<string>? Error;

        // === Properties ===
        public bool IsRunning
        {
            get
            {
                lock (_stateLock)
                {
                    return _isRunning;
                }
            }
        }

        public string CurrentDeviceName
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentDevice?.FriendlyName ?? "Unknown";
                }
            }
        }

        public string SuspendReason
        {
            get
            {
                lock (_stateLock)
                {
                    return _suspendReason;
                }
            }
        }

        public bool IsSuspended => !string.IsNullOrEmpty(SuspendReason);

        private VoiceInputManager()
        {
            Debug.WriteLine("[VoiceInputManager] Initializing voice input manager");
        }

        /// <summary>
        /// Start microphone capture. Fail-closed: if init fails, set suspended state.
        /// </summary>
        public async Task<bool> StartAsync()
        {
            if (_isDisposed)
            {
                Debug.WriteLine("[VoiceInputManager] Cannot start - disposed");
                return false;
            }

            lock (_stateLock)
            {
                if (_isRunning)
                {
                    Debug.WriteLine("[VoiceInputManager] Already running");
                    return true;
                }
            }

            try
            {
                Debug.WriteLine("[VoiceInputManager] Starting microphone capture...");

                using var enumerator = new MMDeviceEnumerator();
                _currentDevice = ResolvePreferredCaptureDevice(enumerator);
                
                if (_currentDevice == null)
                {
                    var error = "No microphone detected. Please connect a microphone.";
                    Suspend(error);
                    Error?.Invoke(this, error);
                    return false;
                }

                Debug.WriteLine($"[VoiceInputManager] Using device: {_currentDevice.FriendlyName}");

                DisposeCapture();

                if (ShouldUseWasapiCapture(_currentDevice.FriendlyName))
                {
                    try
                    {
                        _capture = new WasapiCapture(_currentDevice, true, BufferMs);
                    }
                    catch
                    {
                        _capture = new WasapiCapture(_currentDevice, true);
                    }
                    _captureFormat = _capture.WaveFormat;
                    Debug.WriteLine($"[VoiceInputManager] Using WASAPI capture: {_captureFormat.SampleRate}Hz, {_captureFormat.BitsPerSample}-bit, {_captureFormat.Channels}ch");
                }
                else
                {
                    var deviceNumber = ResolveWaveInDeviceNumber(_currentDevice.FriendlyName);
                    _capture = new WaveInEvent
                    {
                        DeviceNumber = deviceNumber,
                        WaveFormat = new WaveFormat(SampleRate, 16, Channels),
                        BufferMilliseconds = BufferMs
                    };
                    _captureFormat = _capture.WaveFormat;
                    Debug.WriteLine($"[VoiceInputManager] Resolved WaveIn device #{deviceNumber}");
                }

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();

                lock (_stateLock)
                {
                    _isRunning = true;
                    _suspendReason = "";
                }

                Debug.WriteLine("[VoiceInputManager] Microphone capture started successfully");
                StateChanged?.Invoke(this, $"Listening on: {_currentDevice.FriendlyName}");
                return true;
            }
            catch (Exception ex)
            {
                var error = $"Failed to start microphone: {ex.Message}";
                Debug.WriteLine($"[VoiceInputManager] {error}");
                Suspend(error);
                Error?.Invoke(this, error);
                return false;
            }
        }

        /// <summary>
        /// Stop microphone capture
        /// </summary>
        public void Stop()
        {
            if (_isDisposed) return;

            lock (_stateLock)
            {
                if (!_isRunning) return;
                _isRunning = false;
            }

            try
            {
                _capture?.StopRecording();
                Debug.WriteLine("[VoiceInputManager] Microphone capture stopped");
                StateChanged?.Invoke(this, "Stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceInputManager] Error stopping: {ex.Message}");
            }
        }

        /// <summary>
        /// Suspend with reason (fail-closed behavior)
        /// </summary>
        public void Suspend(string reason)
        {
            if (_isDisposed) return;

            lock (_stateLock)
            {
                _suspendReason = reason;
                if (_isRunning)
                {
                    _isRunning = false;
                }
            }

            try
            {
                _capture?.StopRecording();
                Debug.WriteLine($"[VoiceInputManager] Suspended: {reason}");
                StateChanged?.Invoke(this, $"Suspended: {reason}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceInputManager] Error during suspend: {ex.Message}");
            }
        }

        /// <summary>
        /// Resume from suspended state
        /// </summary>
        public async Task<bool> ResumeAsync()
        {
            if (_isDisposed) return false;

            lock (_stateLock)
            {
                _suspendReason = "";
            }

            return await StartAsync();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_isDisposed || !IsRunning) return;

            try
            {
                var format = _captureFormat;
                if (format == null || e.BytesRecorded <= 0)
                    return;

                float[] samples;
                var channelCount = Math.Max(1, format.Channels);

                if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
                {
                    var frameCount = e.BytesRecorded / (4 * channelCount);
                    samples = new float[frameCount];
                    for (var i = 0; i < frameCount; i++)
                    {
                        samples[i] = BitConverter.ToSingle(e.Buffer, i * 4 * channelCount);
                    }
                }
                else if (format.BitsPerSample == 16)
                {
                    var frameCount = e.BytesRecorded / (2 * channelCount);
                    samples = new float[frameCount];
                    for (var i = 0; i < frameCount; i++)
                    {
                        var offset = i * 2 * channelCount;
                        short sample = BitConverter.ToInt16(e.Buffer, offset);
                        samples[i] = sample / 32768f;
                    }
                }
                else if (format.BitsPerSample == 8)
                {
                    var frameCount = e.BytesRecorded / channelCount;
                    samples = new float[frameCount];
                    for (var i = 0; i < frameCount; i++)
                    {
                        var sample = e.Buffer[i * channelCount];
                        samples[i] = (sample - 128) / 128f;
                    }
                }
                else
                {
                    return;
                }

                AudioFrame?.Invoke(this, new AudioFrameEventArgs(samples, format.SampleRate, 1));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceInputManager] Error processing audio data: {ex.Message}");
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                var error = $"Recording stopped with error: {e.Exception.Message}";
                Debug.WriteLine($"[VoiceInputManager] {error}");
                StateChanged?.Invoke(this, $"Recovering: {error}");
                Error?.Invoke(this, error);
            }
            else
            {
                Debug.WriteLine("[VoiceInputManager] Recording stopped normally");
            }

            lock (_stateLock)
            {
                _isRunning = false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();

            try
            {
                DisposeCapture();
                _currentDevice?.Dispose();
                _currentDevice = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceInputManager] Error during dispose: {ex.Message}");
            }

            Debug.WriteLine("[VoiceInputManager] Disposed");
        }

        private void DisposeCapture()
        {
            try
            {
                if (_capture != null)
                {
                    try { _capture.DataAvailable -= OnDataAvailable; } catch { }
                    try { _capture.RecordingStopped -= OnRecordingStopped; } catch { }
                    try { _capture.Dispose(); } catch { }
                    _capture = null;
                }
                _captureFormat = null;
            }
            catch
            {
            }
        }

        private static bool ShouldUseWasapiCapture(string? deviceName)
        {
            var name = (deviceName ?? string.Empty).ToLowerInvariant();
            return name.Contains("airpod") ||
                   name.Contains("bluetooth") ||
                   name.Contains("wireless") ||
                   name.Contains("headset") ||
                   name.Contains("hands-free") ||
                   name.Contains("handsfree");
        }

        private static MMDevice? ResolvePreferredCaptureDevice(MMDeviceEnumerator enumerator)
        {
            var preferredId = GetPreferredDeviceId();
            if (!string.IsNullOrWhiteSpace(preferredId) && !string.Equals(preferredId, "auto", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var byId = enumerator.GetDevice(preferredId);
                    if (byId != null && byId.State == DeviceState.Active)
                        return byId;
                }
                catch
                {
                }
            }

            try
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }
            catch
            {
            }

            try
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }
            catch
            {
            }

            return null;
        }

        private static string GetPreferredDeviceId()
        {
            try
            {
                var preferenceId = (PreferencesStore.Instance.Current.MicrophoneDeviceId ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(preferenceId))
                    return preferenceId;
            }
            catch
            {
            }

            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI",
                    "hardware_settings.json");
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("micDeviceId", out var devId))
                    {
                        return (devId.GetString() ?? string.Empty).Trim();
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static int ResolveWaveInDeviceNumber(string friendlyName)
        {
            try
            {
                static string NormalizeDeviceName(string value)
                {
                    var source = (value ?? string.Empty).ToLowerInvariant();
                    var chars = new char[source.Length];
                    for (var index = 0; index < source.Length; index++)
                        chars[index] = char.IsLetterOrDigit(source[index]) ? source[index] : ' ';

                    return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
                }

                var defaultFriendlyName = NormalizeDeviceName(friendlyName);
                var bestScore = int.MinValue;
                var deviceNumber = 0;
                for (var i = 0; i < WaveIn.DeviceCount; i++)
                {
                    try
                    {
                        var cap = WaveIn.GetCapabilities(i);
                        var productName = NormalizeDeviceName(cap.ProductName.TrimEnd('\0'));
                        if (string.IsNullOrWhiteSpace(productName))
                            continue;

                        var score = 0;
                        if (string.Equals(defaultFriendlyName, productName, StringComparison.OrdinalIgnoreCase))
                        {
                            score = 1000;
                        }
                        else if (defaultFriendlyName.Contains(productName, StringComparison.OrdinalIgnoreCase) ||
                                 productName.Contains(defaultFriendlyName, StringComparison.OrdinalIgnoreCase))
                        {
                            score = 800;
                        }
                        else
                        {
                            var defaultTokens = defaultFriendlyName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var productTokens = productName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var sharedTokenCount = 0;
                            foreach (var token in productTokens)
                            {
                                foreach (var defaultToken in defaultTokens)
                                {
                                    if (!string.Equals(defaultToken, token, StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    sharedTokenCount += 1;
                                    break;
                                }
                            }

                            score = sharedTokenCount * 10;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            deviceNumber = i;
                        }
                    }
                    catch
                    {
                    }
                }

                return deviceNumber;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Audio frame data from microphone
    /// </summary>
    public class AudioFrameEventArgs : EventArgs
    {
        public float[] Samples { get; }
        public int SampleRate { get; }
        public int Channels { get; }

        public AudioFrameEventArgs(float[] samples, int sampleRate, int channels)
        {
            Samples = samples;
            SampleRate = sampleRate;
            Channels = channels;
        }
    }
}