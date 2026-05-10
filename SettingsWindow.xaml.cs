using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Management;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using AtlasAI.Voice;
using AtlasAI.AI;
using AtlasAI.Conversation.Models;
using AtlasAI.Core;
using AtlasAI.Services;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using QRCoder;

namespace AtlasAI {
    public partial class SettingsWindow : Window
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.txt");
        private static readonly string AiKeysPath = Path.Combine(SettingsDir, "ai_keys.json");
        private static readonly string VoiceKeysPath = Path.Combine(SettingsDir, "voice_keys.json");
        private static readonly string HardwareSettingsPath = Path.Combine(SettingsDir, "hardware_settings.json");
        private static readonly string ProfilePath = Path.Combine(SettingsDir, "user_profile.json");
        private const string StartupKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AtlasAI";
        
        // Hardware settings
        private string? _selectedMicDeviceId = null; // Device ID for WASAPI
        private string _selectedMicDeviceName = "";
        private int _selectedMicDevice = -1; // Legacy device index (fallback)
        private int _micSensitivity = 120;
        private string _qualityMode = "balanced";
        
        // Audio monitoring
        private WaveInEvent? _monitorWaveIn;
        private bool _isMonitoring = false;
        private double _peakLevel = 0;
        private DispatcherTimer? _levelDecayTimer;
        
        // Flag to prevent saving during initialization
        private bool _isLoadingSettings = true;
		private bool _userClearedKeys;
		private bool _settingsLoadWarningShown;
		private bool _isClosed;
        private const string SelectedCloudProviderKey = "cloud_provider_selected";
        private const string AddonServerSelectedKey = "streaming_addon_servers_selected";
        private const string AddonServersKey = "streaming_addon_servers";
		private const string AllServersSentinel = "__ALL__";

        public SettingsWindow()
        {
            InitializeComponent();

			// Initialize providers first
			try
			{
				LoadAIProviders();
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
				ShowSettingsLoadWarningOnce();
			}
			try
			{
				LoadVoiceProviders();
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
				ShowSettingsLoadWarningOnce();
			}

            // Load settings after window is fully initialized
            Loaded += SettingsWindow_Loaded;
            Closed += SettingsWindow_Closed;

            // Subscribe to voice recording events to prevent audio interference
            SubscribeToVoiceRecordingEvents();
        }

		private void ShowSettingsLoadWarningOnce()
		{
			if (_settingsLoadWarningShown)
				return;
			_settingsLoadWarningShown = true;

			Dispatcher.BeginInvoke(new Action(() =>
			{
				try
				{
					MessageBox.Show(
						"Settings failed to load saved keys. Your runtime configuration is still active.",
						"Settings",
						MessageBoxButton.OK,
						MessageBoxImage.Warning);
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.ToString());
				}
			}));
		}
        
        /// <summary>
        /// Load voice settings asynchronously
        /// </summary>
        private async Task LoadVoiceSettingsAsync()
        {
            try
            {
                string? openAiKey = null;
                string? elevenLabsKey = null;
                VoiceProviderType? providerType = null;

                var voiceKeysReadPath = AtlasPaths.VoiceKeysReadCandidates().FirstOrDefault(File.Exists) ?? VoiceKeysPath;
                if (File.Exists(voiceKeysReadPath))
                {
                    var json = await File.ReadAllTextAsync(voiceKeysReadPath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("openai", out var openai))
                        openAiKey = SecretProtector.UnprotectIfNeeded(openai.GetString() ?? "");
                    if (root.TryGetProperty("elevenlabs", out var eleven))
                        elevenLabsKey = SecretProtector.UnprotectIfNeeded(eleven.GetString() ?? "");
                    if (root.TryGetProperty("provider", out var prov))
                    {
                        var provString = prov.GetString();
                        Debug.WriteLine($"[Settings] LoadVoiceSettingsAsync: Found provider '{provString}'");
                        if (Enum.TryParse<VoiceProviderType>(provString, out var provType))
                        {
                            providerType = provType;
                        }
                    }
                }

                // Marshal UI updates back to UI thread
                await Dispatcher.InvokeAsync(() =>
                {
					// Only populate empty fields during load; never clear or overwrite non-empty fields.
					if (string.IsNullOrWhiteSpace(OpenAIKeyBox.Password) && !string.IsNullOrWhiteSpace(openAiKey))
						OpenAIKeyBox.Password = openAiKey;
					if (string.IsNullOrWhiteSpace(ElevenLabsKeyBox.Password) && !string.IsNullOrWhiteSpace(elevenLabsKey))
						ElevenLabsKeyBox.Password = elevenLabsKey;

                    if (providerType.HasValue)
                    {
                        for (int i = 0; i < VoiceProviderCombo.Items.Count; i++)
                        {
                            if (VoiceProviderCombo.Items[i] is ComboBoxItem item && 
                                item.Tag is VoiceProviderType t && t == providerType.Value)
                            {
                                VoiceProviderCombo.SelectedIndex = i;
                                Debug.WriteLine($"[Settings] LoadVoiceSettingsAsync: Set SelectedIndex to {i}");
                                break;
                            }
                        }
                    }

                    // Fallback to first item if nothing selected
                    if (VoiceProviderCombo.SelectedIndex < 0 && VoiceProviderCombo.Items.Count > 0)
                    {
                        VoiceProviderCombo.SelectedIndex = 0;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
				ShowSettingsLoadWarningOnce();
                await Dispatcher.InvokeAsync(() =>
                {
                    if (VoiceProviderCombo.Items.Count > 0)
                        VoiceProviderCombo.SelectedIndex = 0;
                });
            }
        }
        
        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            _isClosed = true;
            Services.CompanionTransportService.Instance.StatusChanged -= CompanionTransportService_StatusChanged;
            StopAudioMonitor();
            AudioCoordinator.UnregisterMonitor(this);
        }
        
        // Audio system coordination to prevent interference
        private bool _wasMonitoringBeforeRecording = false;
        
        /// <summary>
        /// Subscribe to voice recording events to coordinate audio systems and prevent interference
        /// </summary>
        private void SubscribeToVoiceRecordingEvents()
        {
            // Register this settings window with the audio coordinator
            AudioCoordinator.RegisterMonitor(this);
        }
        
        /// <summary>
        /// Called when voice recording starts.
        /// Keep the settings visualizer alive during normal capture so the user can still verify input levels.
        /// Only force-stop monitoring when emergency audio protection is active.
        /// </summary>
        internal void OnVoiceRecordingStarted()
        {
            if (!_isMonitoring)
                return;

            _wasMonitoringBeforeRecording = true;

            if (AudioCoordinator.IsEmergencyProtectionActive)
            {
                Debug.WriteLine("[Settings] Emergency audio protection active - stopping audio monitoring");
                StopAudioMonitor();

                MicStatusText.Text = "🎤 Monitoring paused (audio protection active)";
                MicStatusText.Foreground = Brushes.Orange;
                return;
            }

            Debug.WriteLine("[Settings] Voice recording started - keeping monitor active for settings visualizer");
            MicStatusText.Text = "🎤 Monitoring microphone input";
            MicStatusText.Foreground = Brushes.LightGreen;
        }
        
        /// <summary>
        /// Called when voice recording stops - restart audio monitoring if it was running before
        /// </summary>
        internal void OnVoiceRecordingStopped()
        {
            if (!_wasMonitoringBeforeRecording)
                return;

            _wasMonitoringBeforeRecording = false;

            if (AudioCoordinator.IsEmergencyProtectionActive)
                return;

            if (_isMonitoring)
            {
                MicStatusText.Text = "🎤 Monitoring microphone input";
                MicStatusText.Foreground = Brushes.LightGreen;
                return;
            }

            Debug.WriteLine("[Settings] Voice recording stopped - restarting audio monitoring");

            // Wait a moment for voice recording to fully stop, then restart monitoring
            Task.Delay(500).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        StartAudioMonitor();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Settings] Failed to restart audio monitoring: {ex.Message}");
                        MicStatusText.Text = "⚠️ Failed to restart monitoring";
                        MicStatusText.Foreground = Brushes.Red;
                    }
                }));
            });
        }
        
        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
			try
			{
				await LoadVoiceSettingsAsync();
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
				ShowSettingsLoadWarningOnce();
			}
			try
			{
				await LoadSettingsAsync();
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
				ShowSettingsLoadWarningOnce();
			}
			try
			{
				LoadHardwareSettings();
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
				ShowSettingsLoadWarningOnce();
			}

            _ = DetectSystemHardwareAsync();
            _ = LoadMicrophonesAsync();

            try
            {
                Services.CompanionTransportService.Instance.StatusChanged -= CompanionTransportService_StatusChanged;
                Services.CompanionTransportService.Instance.StatusChanged += CompanionTransportService_StatusChanged;
                _ = RefreshCompanionConnectionPanelAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        private async Task LoadSettingsAsync()
        {
			_isLoadingSettings = true;
			try
			{
				try { await LoadSettings(); }
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); ShowSettingsLoadWarningOnce(); }

				try { await LoadAIModelsAsync(); }
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); ShowSettingsLoadWarningOnce(); }

				try { LoadOrbSettings(); }
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); ShowSettingsLoadWarningOnce(); }

				try { LoadPreferences(); }
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); ShowSettingsLoadWarningOnce(); }

				try { LoadCoreAnimationPreferences(); }
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); ShowSettingsLoadWarningOnce(); }

				try { LoadFloatingHudPreferences(); }
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); ShowSettingsLoadWarningOnce(); }

				try { LoadOnlineModeSettings(); }
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); ShowSettingsLoadWarningOnce(); }

				try { LoadContentLanguageSetting(); }
				catch { }

				try { await LoadVoiceSelectionAsync(); }
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); ShowSettingsLoadWarningOnce(); }
			}
			finally
			{
				_isLoadingSettings = false;
			}
        }

        #region Audio Level Monitor
        
        // WASAPI capture for Bluetooth devices
        private WasapiCapture? _monitorWasapi;
        private bool _usingWasapiMonitor = false;
        private bool _monitorRecoveryAttempted = false;
        private bool _monitorAutoReconnectAllowed = true;
        private bool _monitorRestartPending = false;
        private bool _monitorStopRequested = false;
        private bool _forceWaveInMonitorFallback = false;
        private string _activeMonitorDeviceName = "";
        private double _monitorRecoveryThreshold = 5;
        private double _monitorDetectedThreshold = 10;
        private int _monitorAllowedNoAudioChecks = 3;
        
        private void StartMonitor_Click(object sender, RoutedEventArgs e)
        {
            StartAudioMonitor();
        }
        
        private void StopMonitor_Click(object sender, RoutedEventArgs e)
        {
            StopAudioMonitor();
        }
        
        private void StartAudioMonitor()
        {
            if (_isMonitoring) return;
            
            // Check if emergency audio protection is active
            if (AudioCoordinator.IsEmergencyProtectionActive)
            {
                MicStatusText.Text = "🛡️ Audio protection active - Monitoring disabled to prevent distortion";
                MicStatusText.Foreground = Brushes.Orange;
                MessageBox.Show(
                    "Emergency Audio Protection is currently active.\n\n" +
                    "Audio monitoring is disabled to prevent headphone distortion.\n\n" +
                    "Disable audio protection in the main chat window to enable monitoring.",
                    "Audio Protection Active",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            
            try
            {
                string deviceName = "Windows Default";
                var forceWaveInFallback = _forceWaveInMonitorFallback;
                _monitorStopRequested = false;
                _monitorRestartPending = false;
                _monitorRecoveryAttempted = false;
                _monitorAutoReconnectAllowed = true;
                _activeMonitorDeviceName = "";

                // Prefer the explicitly selected capture device, then fall back to the live Windows default endpoint.
                try
                {
                    if (forceWaveInFallback)
                        throw new Exception("WaveIn fallback requested for this monitor session");

                    var preferredDevice = GetPreferredMonitorCaptureDevice();

                    if (preferredDevice != null && preferredDevice.State == DeviceState.Active)
                    {
                        deviceName = preferredDevice.FriendlyName;
                        _activeMonitorDeviceName = deviceName;
                        ConfigureMonitorThresholds(deviceName);
                        Debug.WriteLine($"[Monitor] Using selected/preferred mic: {deviceName}");
                        StartWasapiMonitorForDevice(preferredDevice);
                    }
                    else
                    {
                        throw new Exception("No active capture device available");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Monitor] Preferred WASAPI capture failed: {ex.Message}, trying WaveIn fallback");

                    var waveInIndex = GetSelectedDeviceIndex();
                    if (waveInIndex < 0 || waveInIndex >= WaveIn.DeviceCount)
                        waveInIndex = FindFirstWorkingMic();

                    if (waveInIndex >= 0 && waveInIndex < WaveIn.DeviceCount)
                    {
                        var caps = WaveIn.GetCapabilities(waveInIndex);
                        deviceName = caps.ProductName;
                        _activeMonitorDeviceName = deviceName;
                        ConfigureMonitorThresholds(deviceName);
                        
                        _monitorWaveIn = new WaveInEvent
                        {
                            DeviceNumber = waveInIndex,
                            WaveFormat = new WaveFormat(16000, 16, 1),
                            BufferMilliseconds = 600
                        };
                        
                        _monitorWaveIn.DataAvailable += MonitorWaveIn_DataAvailable;
                        _monitorWaveIn.RecordingStopped += MonitorWaveIn_RecordingStopped;
                        
                        _monitorWaveIn.StartRecording();
                        _usingWasapiMonitor = false;
                        _forceWaveInMonitorFallback = false;
                    }
                    else
                    {
                        throw new Exception("No microphones available");
                    }
                }
                
                // Start decay timer for peak indicator
                _levelDecayTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _levelDecayTimer.Tick += (s, e) =>
                {
                    _peakLevel *= 0.9; // Decay peak
                };
                _levelDecayTimer.Start();
                
                // Start a timer to check if mic is actually producing audio
                _noAudioTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _noAudioCheckCount = 0;
                _monitorMaxLevel = 0;
                _noAudioTimer.Tick += NoAudioTimer_Tick;
                _noAudioTimer.Start();
                
                _isMonitoring = true;
                _monitorMaxLevel = 0;
                
                StartMonitorBtn.IsEnabled = false;
                StopMonitorBtn.IsEnabled = true;
                MicStatusText.Text = $"🎤 Monitoring: {deviceName}";
                MicStatusText.Foreground = Brushes.LightGreen;
                
                Debug.WriteLine($"[Monitor] Started on: {deviceName} (WASAPI: {_usingWasapiMonitor})");
            }
            catch (Exception ex)
            {
                MicStatusText.Text = $"❌ Monitor failed: {ex.Message}";
                MicStatusText.Foreground = Brushes.Red;
                Debug.WriteLine($"[Monitor] Start failed: {ex.Message}");
            }
        }

        private MMDevice? GetPreferredMonitorCaptureDevice()
        {
            using var enumerator = new MMDeviceEnumerator();
            var communicationsDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Communications);
            var multimediaDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Multimedia);
            var consoleDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Console);

            if (!string.IsNullOrWhiteSpace(_selectedMicDeviceId) && _selectedMicDeviceId != "auto")
            {
                try
                {
                    var explicitDevice = enumerator.GetDevice(_selectedMicDeviceId);
                    if (explicitDevice != null && explicitDevice.State == DeviceState.Active)
                        return ResolveBluetoothMonitorDevice(explicitDevice, communicationsDevice, multimediaDevice, consoleDevice);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Monitor] Selected device lookup failed: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(_selectedMicDeviceName))
            {
                try
                {
                    foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    {
                        if (AudioDeviceNamesLikelyMatch(device.FriendlyName, _selectedMicDeviceName))
                            return ResolveBluetoothMonitorDevice(device, communicationsDevice, multimediaDevice, consoleDevice);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Monitor] Selected device name lookup failed: {ex.Message}");
                }
            }

            if (_selectedMicDevice >= 0)
            {
                try
                {
                    var matchedWaveInDevice = FindWasapiDeviceForWaveInIndex(enumerator, _selectedMicDevice);
                    if (matchedWaveInDevice != null && matchedWaveInDevice.State == DeviceState.Active)
                        return ResolveBluetoothMonitorDevice(matchedWaveInDevice, communicationsDevice, multimediaDevice, consoleDevice);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Monitor] WaveIn to WASAPI lookup failed: {ex.Message}");
                }
            }

            if (communicationsDevice != null)
                return communicationsDevice;

            if (multimediaDevice != null)
                return multimediaDevice;

            if (consoleDevice != null)
                return consoleDevice;

            return GetBestMicDevice();
        }

        private static MMDevice? TryGetDefaultCaptureEndpoint(MMDeviceEnumerator enumerator, Role role)
        {
            try
            {
                var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                if (endpoint != null && endpoint.State == DeviceState.Active)
                    return endpoint;
            }
            catch
            {
            }

            return null;
        }

        private static MMDevice ResolveBluetoothMonitorDevice(MMDevice selectedDevice, MMDevice? communicationsDevice, MMDevice? multimediaDevice, MMDevice? consoleDevice)
        {
            if (!IsBluetoothStyleMicrophone(selectedDevice.FriendlyName))
                return selectedDevice;

            foreach (var candidate in new[] { communicationsDevice, multimediaDevice, consoleDevice })
            {
                if (candidate == null || candidate.State != DeviceState.Active)
                    continue;

                if (string.Equals(candidate.ID, selectedDevice.ID, StringComparison.OrdinalIgnoreCase))
                    return selectedDevice;

                if (AudioDeviceNamesLikelyMatch(candidate.FriendlyName, selectedDevice.FriendlyName) || IsBluetoothStyleMicrophone(candidate.FriendlyName))
                    return candidate;
            }

            return selectedDevice;
        }
        
        private void StartWasapiMonitorForDevice(MMDevice device)
        {
            try
            {
                var deviceName = device.FriendlyName;
                Debug.WriteLine($"[Monitor] Starting WASAPI for: {deviceName}");
                
                // Use EXTREME conservative buffer settings to prevent audio distortion
                try
                {
                    // Use MASSIVE buffer (300ms) to prevent distortion in headphones
                    var bufferDuration = TimeSpan.FromMilliseconds(300);
                    _monitorWasapi = new WasapiCapture(device, true, (int)bufferDuration.TotalMilliseconds); // shared mode with 300ms buffer
                    var format = _monitorWasapi.WaveFormat;
                    Debug.WriteLine($"[Monitor] WASAPI shared with 300ms buffer: {format.SampleRate}Hz, {format.Channels}ch, {format.BitsPerSample}bit, {format.Encoding}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Monitor] WASAPI with custom buffer failed: {ex.Message}, trying default");
                    // Fallback to default buffer
                    _monitorWasapi = new WasapiCapture(device, true); // shared mode with default buffer
                    var format = _monitorWasapi.WaveFormat;
                    Debug.WriteLine($"[Monitor] WASAPI shared with default buffer: {format.SampleRate}Hz, {format.Channels}ch, {format.BitsPerSample}bit, {format.Encoding}");
                }
                
                _monitorWasapi.DataAvailable += MonitorWasapi_DataAvailable;
                _monitorWasapi.RecordingStopped += MonitorWasapi_RecordingStopped;
                
                _monitorWasapi.StartRecording();
                _usingWasapiMonitor = true;
                Debug.WriteLine($"[Monitor] WASAPI started in shared mode (audio-friendly, no distortion)");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Monitor] WASAPI shared mode failed: {ex.Message}");
                _monitorWasapi?.Dispose();
                _monitorWasapi = null;
                throw new Exception($"WASAPI shared mode capture failed: {ex.Message} (exclusive mode disabled to prevent audio distortion)");
            }
        }
        
        private void StartWasapiMonitor(MMDevice device)
        {
            try
            {
                var deviceName = device.FriendlyName;
                Debug.WriteLine($"[Monitor] Starting WASAPI for: {deviceName}");
                
                // For AirPods/Bluetooth "Headset" mode, we need special handling
                // The Hands-Free profile often has issues with standard WASAPI
                var nameLower = deviceName.ToLower();
                bool isHandsFree = nameLower.Contains("hands-free") || nameLower.Contains("headset");
                
                if (isHandsFree)
                {
                    Debug.WriteLine($"[Monitor] Detected Hands-Free/Headset profile - using conservative buffer settings");
                }
                
                // Use conservative buffer settings to prevent audio distortion
                try
                {
                    // Use larger buffer (100ms) especially for Bluetooth/headset devices
                    var bufferDuration = TimeSpan.FromMilliseconds(isHandsFree ? 150 : 100);
                    _monitorWasapi = new WasapiCapture(device, true, (int)bufferDuration.TotalMilliseconds); // shared mode with conservative buffer
                    var format = _monitorWasapi.WaveFormat;
                    Debug.WriteLine($"[Monitor] WASAPI shared mode with {bufferDuration.TotalMilliseconds}ms buffer: {format.SampleRate}Hz, {format.Channels}ch, {format.BitsPerSample}bit, {format.Encoding}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Monitor] WASAPI with custom buffer failed: {ex.Message}, trying default");
                    // Fallback to default buffer
                    _monitorWasapi = new WasapiCapture(device, true); // shared mode with default buffer
                    var format = _monitorWasapi.WaveFormat;
                    Debug.WriteLine($"[Monitor] WASAPI shared mode with default buffer: {format.SampleRate}Hz, {format.Channels}ch, {format.BitsPerSample}bit, {format.Encoding}");
                }
                
                _monitorWasapi.DataAvailable += MonitorWasapi_DataAvailable;
                _monitorWasapi.RecordingStopped += MonitorWasapi_RecordingStopped;
                
                _monitorWasapi.StartRecording();
                _usingWasapiMonitor = true;
                Debug.WriteLine($"[Monitor] WASAPI started successfully in shared mode (audio-friendly, no distortion)");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Monitor] WASAPI shared mode failed: {ex.Message}");
                _monitorWasapi?.Dispose();
                _monitorWasapi = null;
                throw new Exception($"WASAPI shared mode capture failed: {ex.Message} (exclusive mode disabled to prevent audio distortion)");
            }
        }
        
        private void MonitorWasapi_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_monitorWasapi == null) return;
            
            var format = _monitorWasapi.WaveFormat;
            double level = 0;
            double max = 0;
            int sampleCount = 0;
            
            // Handle IEEE float format (common with WASAPI/Bluetooth)
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                for (int i = 0; i < e.BytesRecorded; i += 4 * format.Channels)
                {
                    if (i + 3 < e.BytesRecorded)
                    {
                        float sample = BitConverter.ToSingle(e.Buffer, i);
                        double abs = Math.Abs(sample) * 32767; // Scale to 16-bit range
                        level += abs;
                        if (abs > max) max = abs;
                        sampleCount++;
                    }
                }
            }
            else if (format.BitsPerSample == 16)
            {
                // 16-bit PCM
                for (int i = 0; i < e.BytesRecorded; i += 2 * format.Channels)
                {
                    if (i + 1 < e.BytesRecorded)
                    {
                        short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                        double abs = Math.Abs(sample);
                        level += abs;
                        if (abs > max) max = abs;
                        sampleCount++;
                    }
                }
            }
            else if (format.BitsPerSample == 8)
            {
                // 8-bit PCM (common with Bluetooth HFP/Hands-Free)
                for (int i = 0; i < e.BytesRecorded; i += format.Channels)
                {
                    byte sample = e.Buffer[i];
                    double abs = Math.Abs(sample - 128) * 256; // Convert to 16-bit range
                    level += abs;
                    if (abs > max) max = abs;
                    sampleCount++;
                }
            }
            else
            {
                // Unknown format - try to read as bytes
                Debug.WriteLine($"[Monitor] Unknown format: {format.BitsPerSample}bit, {format.Encoding}");
                for (int i = 0; i < e.BytesRecorded; i++)
                {
                    double abs = Math.Abs(e.Buffer[i] - 128) * 256;
                    level += abs;
                    if (abs > max) max = abs;
                    sampleCount++;
                }
            }
            
            double avgLevel = sampleCount > 0 ? level / sampleCount : 0;
            
            // Normalize to 0-100 scale
            double normalizedLevel = Math.Min(100, (avgLevel / 32767.0) * 500);
            double normalizedPeak = Math.Min(100, (max / 32767.0) * 200);
            
            // Track max level for no-audio detection
            if (normalizedLevel > _monitorMaxLevel)
                _monitorMaxLevel = normalizedLevel;
            
            // Update peak
            if (normalizedPeak > _peakLevel)
                _peakLevel = normalizedPeak;
            
            // Update UI
            UpdateMonitorUI(normalizedLevel, normalizedPeak);
        }

        private void MonitorWasapi_RecordingStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
        {
            HandleMonitorRecordingStopped("WASAPI", e.Exception);
        }

        private void MonitorWaveIn_RecordingStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
        {
            HandleMonitorRecordingStopped("WaveIn", e.Exception);
        }

        private void HandleMonitorRecordingStopped(string backend, Exception? exception)
        {
            if (exception != null)
                Debug.WriteLine($"[Monitor] {backend} stopped with error: {exception.Message}");
            else
                Debug.WriteLine($"[Monitor] {backend} stopped unexpectedly");

            if (_monitorStopRequested || !_isMonitoring || _monitorRestartPending)
                return;

            _monitorRestartPending = true;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (!_isMonitoring)
                        return;

                    var friendlyName = string.IsNullOrWhiteSpace(_activeMonitorDeviceName)
                        ? "selected microphone"
                        : _activeMonitorDeviceName;

                    MicStatusText.Text = $"🎤 Reconnecting {friendlyName}...";
                    MicStatusText.Foreground = Brushes.Yellow;

                    await Task.Delay(250);

                    if (!_isMonitoring)
                        return;

                    StopAudioMonitor();
                    StartAudioMonitor();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Monitor] Failed to recover {backend} monitor: {ex.Message}");
                    MicStatusText.Text = $"❌ Monitor recovery failed: {ex.Message}";
                    MicStatusText.Foreground = Brushes.Red;
                }
                finally
                {
                    _monitorRestartPending = false;
                }
            }), DispatcherPriority.Background);
        }
        
        private DispatcherTimer? _noAudioTimer;
        private int _noAudioCheckCount = 0;
        private double _monitorMaxLevel = 0;
        
        private void NoAudioTimer_Tick(object? sender, EventArgs e)
        {
            _noAudioCheckCount++;

            if (_monitorMaxLevel >= _monitorRecoveryThreshold)
            {
                _noAudioTimer?.Stop();
                return;
            }

            var friendlyName = string.IsNullOrWhiteSpace(_activeMonitorDeviceName) ? "selected microphone" : _activeMonitorDeviceName;

            if (_usingWasapiMonitor &&
                !_monitorRecoveryAttempted &&
                IsBluetoothStyleMicrophone(friendlyName) &&
                _noAudioCheckCount >= 2)
            {
                _monitorRecoveryAttempted = true;
                _monitorRestartPending = true;
                _forceWaveInMonitorFallback = true;
                _noAudioTimer?.Stop();
                MicStatusText.Text = $"🎧 Switching {friendlyName} to alternate Windows audio path...";
                MicStatusText.Foreground = Brushes.Yellow;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isMonitoring) return;
                    StopAudioMonitor();
                    StartAudioMonitor();
                }), DispatcherPriority.Background);
                return;
            }

            if (_monitorAutoReconnectAllowed && _usingWasapiMonitor && !_monitorRecoveryAttempted && _noAudioCheckCount >= 2)
            {
                _monitorRecoveryAttempted = true;
                _noAudioTimer?.Stop();
                MicStatusText.Text = $"🎧 Reconnecting {friendlyName}...";
                MicStatusText.Foreground = Brushes.Yellow;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isMonitoring) return;
                    StopAudioMonitor();
                    StartAudioMonitor();
                }), DispatcherPriority.Background);
                return;
            }

            var allowedChecks = _usingWasapiMonitor ? _monitorAllowedNoAudioChecks : Math.Max(2, _monitorAllowedNoAudioChecks - 1);
            if (_noAudioCheckCount < allowedChecks)
            {
                MicStatusText.Text = _usingWasapiMonitor
                    ? $"🎧 Waiting for {friendlyName} audio..."
                    : "🎤 Waiting for microphone audio...";
                MicStatusText.Foreground = Brushes.Gold;
                return;
            }

            MicStatusText.Text = "⚠️ No audio detected! Try Auto-Scan or select different mic";
            MicStatusText.Foreground = Brushes.Orange;
            _noAudioTimer?.Stop();
        }
        
        private int FindFirstWorkingMic()
        {
            // Try to find a mic that actually produces audio
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                var name = caps.ProductName.ToLower();
                
                // Skip obviously bad devices
                if (name.Contains("stereo mix") || name.Contains("what u hear") || 
                    name.Contains("loopback") || name.Contains("digital"))
                    continue;
                
                // Prefer real microphones
                if (name.Contains("microphone") || name.Contains("mic") || 
                    name.Contains("usb") || name.Contains("sound blaster"))
                {
                    return i;
                }
            }
            
            // Fallback to first non-loopback device
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                var name = caps.ProductName.ToLower();
                if (!name.Contains("stereo mix") && !name.Contains("what u hear"))
                    return i;
            }
            
            return 0;
        }
        
        private void StopAudioMonitor()
        {
            if (!_isMonitoring) return;
            
            try
            {
                _monitorStopRequested = true;
                _monitorRestartPending = false;
                _levelDecayTimer?.Stop();
                _levelDecayTimer = null;
                
                _noAudioTimer?.Stop();
                _noAudioTimer = null;
                
                // Stop WASAPI monitor if active
                if (_monitorWasapi != null)
                {
                    _monitorWasapi.StopRecording();
                    _monitorWasapi.Dispose();
                    _monitorWasapi = null;
                }
                
                // Stop WaveIn monitor if active
                _monitorWaveIn?.StopRecording();
                _monitorWaveIn?.Dispose();
                _monitorWaveIn = null;
                
                _isMonitoring = false;
                _usingWasapiMonitor = false;
                _monitorRecoveryAttempted = false;
                _monitorAutoReconnectAllowed = true;
                _monitorStopRequested = false;
                _activeMonitorDeviceName = "";
                if (!_monitorRestartPending)
                    _forceWaveInMonitorFallback = false;
                _monitorMaxLevel = 0;
                _noAudioCheckCount = 0;
                _monitorRecoveryThreshold = 5;
                _monitorDetectedThreshold = 10;
                _monitorAllowedNoAudioChecks = 3;
                
                StartMonitorBtn.IsEnabled = true;
                StopMonitorBtn.IsEnabled = false;
                
                // Reset visualizer
                AudioLevelBar.Width = 0;
                AudioPeakIndicator.Visibility = Visibility.Collapsed;
                AudioLevelText.Text = "0";
                
                MicStatusText.Text = "🎤 Monitor stopped";
                MicStatusText.Foreground = Brushes.Orange;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Monitor] Stop error: {ex.Message}");
            }
        }
        
        private async void AutoScan_Click(object sender, RoutedEventArgs e)
        {
            // Stop any current monitoring
            StopAudioMonitor();
            
            AutoScanBtn.IsEnabled = false;
            AutoScanBtn.Content = "Scanning...";
            MicStatusText.Text = "🔍 Scanning all microphones...";
            MicStatusText.Foreground = Brushes.Yellow;
            
            await Task.Run(() =>
            {
                var results = WhisperSpeechRecognition.GetWorkingMicrophones();
                var workingMics = results.Where(r => r.Working).OrderByDescending(r => r.Level).ToList();
                
                Dispatcher.Invoke(() =>
                {
                    if (workingMics.Count > 0)
                    {
                        var best = workingMics.First();
                        MicStatusText.Text = $"✓ Found {workingMics.Count} working mic(s)! Best: {best.Name}";
                        MicStatusText.Foreground = Brushes.LightGreen;
                        
                        // Find and select this mic in the dropdown
                        // First, rebuild the dropdown with WaveIn indices for reliability
                        RebuildMicDropdownWithWaveIn();
                        
                        // Select the best working mic
                        for (int i = 0; i < MicrophoneCombo.Items.Count; i++)
                        {
                            if (MicrophoneCombo.Items[i] is ComboBoxItem comboItem)
                            {
                                var content = comboItem.Content?.ToString() ?? "";
                                if (content.Contains(best.Name))
                                {
                                    MicrophoneCombo.SelectedIndex = i;
                                    _selectedMicDevice = best.Index;
                                    break;
                                }
                            }
                        }
                        
                        // Auto-start monitoring with the working mic
                        StartAudioMonitor();
                    }
                    else
                    {
                        MicStatusText.Text = "❌ No working microphones found! Check connections.";
                        MicStatusText.Foreground = Brushes.Red;
                    }
                    
                    AutoScanBtn.IsEnabled = true;
                    AutoScanBtn.Content = "🔍 Auto-Scan";
                });
            });
        }
        
        private void RebuildMicDropdownWithWaveIn()
        {
            // Rebuild dropdown using WaveIn indices for more reliable device selection
            MicrophoneCombo.Items.Clear();
            
            MicrophoneCombo.Items.Add(new ComboBoxItem
            {
                Content = "🔄 Auto-detect (Recommended)",
                Tag = "auto"
            });
            
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                var name = caps.ProductName;
                
                var icon = "🎤";
                var nameLower = name.ToLower();
                if (nameLower.Contains("airpod") || nameLower.Contains("bluetooth") || nameLower.Contains("wireless"))
                    icon = "🎧";
                else if (nameLower.Contains("headset") || nameLower.Contains("headphone"))
                    icon = "🎧";
                else if (nameLower.Contains("usb"))
                    icon = "🎙️";
                else if (nameLower.Contains("webcam") || nameLower.Contains("camera"))
                    icon = "📷";
                
                MicrophoneCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{icon} {name}",
                    Tag = i.ToString() // Store WaveIn index as string
                });
            }
        }
        
        private void MonitorWaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            // Calculate RMS level
            double sum = 0;
            double max = 0;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                if (i + 1 < e.BytesRecorded)
                {
                    short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    double abs = Math.Abs(sample);
                    sum += abs;
                    if (abs > max) max = abs;
                }
            }
            double avgLevel = sum / Math.Max(1, e.BytesRecorded / 2);
            
            // Normalize to 0-100 scale (32767 is max for 16-bit)
            double normalizedLevel = Math.Min(100, (avgLevel / 32767.0) * 500); // Amplify for visibility
            double normalizedPeak = Math.Min(100, (max / 32767.0) * 200);
            
            // Track max level for no-audio detection
            if (normalizedLevel > _monitorMaxLevel)
                _monitorMaxLevel = normalizedLevel;
            
            // Update peak
            if (normalizedPeak > _peakLevel)
                _peakLevel = normalizedPeak;
            
            // Update UI
            UpdateMonitorUI(normalizedLevel, normalizedPeak);
        }
        
        private void UpdateMonitorUI(double normalizedLevel, double normalizedPeak)
        {
            // Update UI on dispatcher thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Get the parent border width for scaling
                    var parentBorder = AudioLevelBar.Parent as Grid;
                    double maxWidth = parentBorder?.ActualWidth ?? 200;
                    if (maxWidth <= 0) maxWidth = 200;
                    
                    // Update level bar
                    double barWidth = (normalizedLevel / 100.0) * maxWidth;
                    AudioLevelBar.Width = Math.Max(0, Math.Min(maxWidth, barWidth));
                    
                    // Color based on level
                    if (normalizedLevel > 70)
                        AudioLevelBar.Background = Brushes.Red;
                    else if (normalizedLevel > 40)
                        AudioLevelBar.Background = Brushes.Orange;
                    else if (normalizedLevel > 10)
                        AudioLevelBar.Background = new SolidColorBrush(Color.FromRgb(0, 170, 0));
                    else
                        AudioLevelBar.Background = new SolidColorBrush(Color.FromRgb(0, 100, 0));
                    
                    // Update peak indicator
                    if (_peakLevel > 5)
                    {
                        AudioPeakIndicator.Visibility = Visibility.Visible;
                        double peakPos = (_peakLevel / 100.0) * maxWidth;
                        AudioPeakIndicator.Margin = new Thickness(Math.Max(0, peakPos - 1.5), 2, 0, 2);
                    }
                    else
                    {
                        AudioPeakIndicator.Visibility = Visibility.Collapsed;
                    }
                    
                    // Update text
                    AudioLevelText.Text = $"{normalizedLevel:F0}";
                    
                    // Update status if we detect sound
                    if (normalizedLevel > _monitorDetectedThreshold)
                    {
                        _noAudioTimer?.Stop();
                        _noAudioCheckCount = 0;
                        MicStatusText.Text = "🎤 ✓ Audio detected!";
                        MicStatusText.Foreground = Brushes.LightGreen;
                    }
                }
                catch { }
            }));
        }

        private void ConfigureMonitorThresholds(string? deviceName)
        {
            var name = (deviceName ?? string.Empty).ToLowerInvariant();
            var isBluetoothStyle = name.Contains("airpod") ||
                                   name.Contains("bluetooth") ||
                                   name.Contains("wireless") ||
                                   name.Contains("hands-free") ||
                                   name.Contains("handsfree") ||
                                   name.Contains("headset");

            if (isBluetoothStyle)
            {
                _monitorAutoReconnectAllowed = false;
                _monitorRecoveryThreshold = 1.5;
                _monitorDetectedThreshold = 2.5;
                _monitorAllowedNoAudioChecks = 5;
                Debug.WriteLine($"[Monitor] Using Bluetooth thresholds for: {deviceName}");
                return;
            }

            _monitorAutoReconnectAllowed = true;
            _monitorRecoveryThreshold = 5;
            _monitorDetectedThreshold = 10;
            _monitorAllowedNoAudioChecks = 3;
        }
        
        private int GetSelectedDeviceIndex()
        {
            if (MicrophoneCombo.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag is string tagStr)
                {
                    if (tagStr == "auto") return -1;
                    
                    // Try to parse as WaveIn index first
                    if (int.TryParse(tagStr, out int idx)) 
                    {
                        if (idx >= 0 && idx < WaveIn.DeviceCount)
                            return idx;
                    }
                    
                    // It's a WASAPI device ID - try to find matching WaveIn device by name
                    try
                    {
                        using var enumerator = new MMDeviceEnumerator();
                        var mmDevice = enumerator.GetDevice(tagStr);
                        if (mmDevice != null)
                        {
                            var mmName = mmDevice.FriendlyName.ToLower();
                            Debug.WriteLine($"[Settings] GetSelectedDeviceIndex looking for: {mmDevice.FriendlyName}");
                            
                            // Find WaveIn device with similar name
                            for (int i = 0; i < WaveIn.DeviceCount; i++)
                            {
                                var caps = WaveIn.GetCapabilities(i);
                                var waveInName = caps.ProductName.ToLower();
                                
                                Debug.WriteLine($"[Settings] Comparing WASAPI '{mmName}' with WaveIn '{waveInName}'");
                                
                                if (AudioDeviceNamesLikelyMatch(mmName, waveInName))
                                {
                                    Debug.WriteLine($"[Settings] Matched to WaveIn device {i}: {caps.ProductName}");
                                    return i;
                                }
                            }
                            
                            Debug.WriteLine($"[Settings] No WaveIn match for '{mmDevice.FriendlyName}'");
                            return -1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Settings] Error in GetSelectedDeviceIndex: {ex.Message}");
                    }
                }
                else if (item.Tag is int idx)
                {
                    return idx;
                }
            }
            return -1;
        }

        private MMDevice? FindWasapiDeviceForWaveInIndex(MMDeviceEnumerator enumerator, int waveInIndex)
        {
            if (waveInIndex < 0 || waveInIndex >= WaveIn.DeviceCount)
                return null;

            var caps = WaveIn.GetCapabilities(waveInIndex);
            var waveInName = caps.ProductName;

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (AudioDeviceNamesLikelyMatch(device.FriendlyName, waveInName))
                    return device;
            }

            return null;
        }

        private static bool AudioDeviceNamesLikelyMatch(string left, string right)
        {
            var normalizedLeft = NormalizeAudioDeviceName(left);
            var normalizedRight = NormalizeAudioDeviceName(right);

            if (string.IsNullOrEmpty(normalizedLeft) || string.IsNullOrEmpty(normalizedRight))
                return false;

            if (normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) || normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))
                return true;

            string[] matchTokens = { "airpod", "headset", "handsfree", "bluetooth", "wireless", "realtek", "usbaudio", "soundblaster", "microphone" };
            foreach (var token in matchTokens)
            {
                if (normalizedLeft.Contains(token, StringComparison.Ordinal) && normalizedRight.Contains(token, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsBluetoothStyleMicrophone(string? deviceName)
        {
            var value = (deviceName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("airpod") ||
                   value.Contains("bluetooth") ||
                   value.Contains("wireless") ||
                   value.Contains("hands-free") ||
                   value.Contains("handsfree") ||
                   value.Contains("headset");
        }

        private static string NormalizeAudioDeviceName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var character in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                    builder.Append(character);
            }

            return builder.ToString();
        }
        
        #endregion

        #region Hardware Detection

        private sealed record HardwareSnapshot(string CpuName, int RamGb, string GpuName, string? ErrorMessage);

        private sealed record MicrophoneOptionSnapshot(string Content, string Tag);

        private sealed record MicrophoneLoadSnapshot(
            IReadOnlyList<MicrophoneOptionSnapshot> Options,
            int SelectedIndex,
            string StatusText,
            string StatusKind);

        private sealed record CompanionConnectionSnapshot(
            CompanionTransportService.CompanionTransportStatus Status,
            CompanionTransportService.CompanionPairingInfo Pairing,
            CompanionFileTransferDefaults FileTransfer,
            CompanionVncDefaults Vnc,
            string? SetupPayload,
            ImageSource? QrImage,
            CompanionRemoteDefaults Remote,
            string? RemoteSetupPayload,
            ImageSource? RemoteQrImage);

        private sealed record CompanionVncDefaults(
            string Host,
            int Port,
            string Password,
            string Target,
            string StatusText,
            string ProfileText,
            bool UsesDetectedHost);

        private sealed record CompanionRemoteDefaults(
            string Host,
            int Port,
            bool UseTls,
            string BaseUrl,
            string StatusText,
            bool IsConfigured);

        private async Task DetectSystemHardwareAsync()
        {
            try
            {
                var snapshot = await Task.Run(BuildHardwareSnapshot).ConfigureAwait(false);
                if (_isClosed)
                    return;

                await Dispatcher.InvokeAsync(() => ApplyHardwareSnapshot(snapshot), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                ShowSettingsLoadWarningOnce();
            }
        }

        private static HardwareSnapshot BuildHardwareSnapshot()
        {
            try
            {
                var cpuName = "Unknown CPU";
                var ramGb = 0;
                var gpuName = "Unknown GPU";

                try
                {
                    using var searcher = new ManagementObjectSearcher("select Name from Win32_Processor");
                    foreach (var item in searcher.Get())
                    {
                        cpuName = item["Name"]?.ToString() ?? "Unknown";
                        break;
                    }
                }
                catch
                {
                }

                try
                {
                    using var searcher = new ManagementObjectSearcher("select TotalPhysicalMemory from Win32_ComputerSystem");
                    foreach (var item in searcher.Get())
                    {
                        var bytes = Convert.ToInt64(item["TotalPhysicalMemory"]);
                        ramGb = (int)(bytes / 1024 / 1024 / 1024);
                        break;
                    }
                }
                catch
                {
                }

                try
                {
                    using var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController");
                    foreach (var item in searcher.Get())
                    {
                        gpuName = item["Name"]?.ToString() ?? "Unknown";
                        break;
                    }
                }
                catch
                {
                }

                return new HardwareSnapshot(cpuName, ramGb, gpuName, null);
            }
            catch (Exception ex)
            {
                return new HardwareSnapshot("Unknown CPU", 0, "Unknown GPU", ex.Message);
            }
        }

        private void ApplyHardwareSnapshot(HardwareSnapshot snapshot)
        {
            if (_isClosed)
                return;

            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                HardwareInfoText.Text = $"🖥️ Could not detect hardware: {snapshot.ErrorMessage}";
                return;
            }

            HardwareInfoText.Text = $"🖥️ {snapshot.CpuName}\n💾 {snapshot.RamGb} GB RAM | 🎮 {snapshot.GpuName}";

            if (string.IsNullOrEmpty(_qualityMode) || _qualityMode == "auto")
            {
                if (snapshot.RamGb >= 16)
                    _qualityMode = "high";
                else if (snapshot.RamGb >= 8)
                    _qualityMode = "balanced";
                else
                    _qualityMode = "low";

                SelectQualityMode(_qualityMode);
            }
        }

        private async Task LoadMicrophonesAsync()
        {
            try
            {
                var selectedMicDeviceId = _selectedMicDeviceId;
                var selectedMicDeviceName = _selectedMicDeviceName;
                var selectedMicDevice = _selectedMicDevice;

                var snapshot = await Task.Run(() => BuildMicrophoneLoadSnapshot(selectedMicDeviceId, selectedMicDeviceName, selectedMicDevice)).ConfigureAwait(false);
                if (_isClosed)
                    return;

                await Dispatcher.InvokeAsync(() => ApplyMicrophoneLoadSnapshot(snapshot), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                ShowSettingsLoadWarningOnce();
            }
        }

        private MicrophoneLoadSnapshot BuildMicrophoneLoadSnapshot(string? selectedMicDeviceId, string selectedMicDeviceName, int selectedMicDevice)
        {
            var options = new List<MicrophoneOptionSnapshot>
            {
                new("🔄 Use Windows default microphone", "auto")
            };

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

                if (devices.Count == 0)
                    return new MicrophoneLoadSnapshot(options, 0, "🎤 No microphone detected!", "error");

                foreach (var device in devices)
                {
                    var name = device.FriendlyName;
                    var icon = "🎤";
                    var nameLower = (name ?? string.Empty).ToLowerInvariant();
                    if (nameLower.Contains("airpod") || nameLower.Contains("bluetooth") || nameLower.Contains("wireless"))
                        icon = "🎧";
                    else if (nameLower.Contains("headset") || nameLower.Contains("headphone") || nameLower.Contains("hands-free"))
                        icon = "🎧";
                    else if (nameLower.Contains("usb"))
                        icon = "🎙️";
                    else if (nameLower.Contains("webcam") || nameLower.Contains("camera"))
                        icon = "📷";

                    options.Add(new MicrophoneOptionSnapshot($"{icon} {name}", device.ID));
                }

                var selectedIndex = 0;
                var restoredSelection = false;
                if (!string.IsNullOrEmpty(selectedMicDeviceId))
                {
                    for (var i = 1; i < options.Count; i++)
                    {
                        if (string.Equals(options[i].Tag, selectedMicDeviceId, StringComparison.Ordinal))
                        {
                            selectedIndex = i;
                            restoredSelection = true;
                            break;
                        }
                    }
                }

                if (!restoredSelection && !string.IsNullOrWhiteSpace(selectedMicDeviceName))
                {
                    for (var i = 1; i < options.Count; i++)
                    {
                        if (AudioDeviceNamesLikelyMatch(options[i].Content, selectedMicDeviceName))
                        {
                            selectedIndex = i;
                            restoredSelection = true;
                            break;
                        }
                    }
                }

                if (!restoredSelection && selectedMicDevice >= 0)
                {
                    for (var i = 1; i < options.Count; i++)
                    {
                        if (string.Equals(options[i].Tag, selectedMicDevice.ToString(), StringComparison.Ordinal))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                var bestDeviceName = ResolveBestMicrophoneName(enumerator, devices, selectedMicDeviceId, selectedMicDeviceName, selectedMicDevice);
                return new MicrophoneLoadSnapshot(
                    options,
                    selectedIndex,
                    !string.IsNullOrWhiteSpace(bestDeviceName) ? $"🎤 Active: {bestDeviceName}" : "🎤 No microphone detected!",
                    !string.IsNullOrWhiteSpace(bestDeviceName) ? "ok" : "error");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] MMDevice enumeration failed: {ex.Message}");
                return BuildLegacyMicrophoneLoadSnapshot(ex.Message);
            }
        }

        private MicrophoneLoadSnapshot BuildLegacyMicrophoneLoadSnapshot(string errorMessage)
        {
            var options = new List<MicrophoneOptionSnapshot>
            {
                new("🔄 Use Windows default microphone", "auto")
            };

            var deviceCount = WaveIn.DeviceCount;
            if (deviceCount == 0)
                return new MicrophoneLoadSnapshot(options, 0, "🎤 No microphone detected!", "error");

            for (var i = 0; i < deviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                var name = caps.ProductName;
                var icon = "🎤";
                var nameLower = (name ?? string.Empty).ToLowerInvariant();
                if (nameLower.Contains("airpod") || nameLower.Contains("bluetooth") || nameLower.Contains("wireless"))
                    icon = "🎧";
                else if (nameLower.Contains("headset") || nameLower.Contains("headphone"))
                    icon = "🎧";
                else if (nameLower.Contains("usb"))
                    icon = "🎙️";
                else if (nameLower.Contains("webcam") || nameLower.Contains("camera"))
                    icon = "📷";

                options.Add(new MicrophoneOptionSnapshot($"{icon} {name}", i.ToString()));
            }

            return new MicrophoneLoadSnapshot(options, 0, $"⚠️ Error detecting mics: {errorMessage}", "warning");
        }

        private string? ResolveBestMicrophoneName(MMDeviceEnumerator enumerator, IReadOnlyList<MMDevice> devices, string? selectedMicDeviceId, string selectedMicDeviceName, int selectedMicDevice)
        {
            if (!string.IsNullOrWhiteSpace(selectedMicDeviceId) && selectedMicDeviceId != "auto")
            {
                try
                {
                    var device = enumerator.GetDevice(selectedMicDeviceId);
                    if (device != null && device.State == DeviceState.Active)
                        return device.FriendlyName;
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedMicDeviceName))
            {
                var matchedDevice = devices.FirstOrDefault(device => AudioDeviceNamesLikelyMatch(device.FriendlyName, selectedMicDeviceName));
                if (matchedDevice != null)
                    return matchedDevice.FriendlyName;
            }

            if (selectedMicDevice >= 0)
            {
                try
                {
                    var matchedWaveInDevice = FindWasapiDeviceForWaveInIndex(enumerator, selectedMicDevice);
                    if (matchedWaveInDevice != null && matchedWaveInDevice.State == DeviceState.Active)
                        return matchedWaveInDevice.FriendlyName;
                }
                catch
                {
                }
            }

            var defaultDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Communications)
                ?? TryGetDefaultCaptureEndpoint(enumerator, Role.Multimedia)
                ?? TryGetDefaultCaptureEndpoint(enumerator, Role.Console);
            if (defaultDevice != null)
                return defaultDevice.FriendlyName;

            return devices.FirstOrDefault(device =>
            {
                var name = (device.FriendlyName ?? string.Empty).ToLowerInvariant();
                return !name.Contains("stereo mix") && !name.Contains("loopback");
            })?.FriendlyName;
        }

        private void ApplyMicrophoneLoadSnapshot(MicrophoneLoadSnapshot snapshot)
        {
            if (_isClosed)
                return;

            MicrophoneCombo.Items.Clear();
            foreach (var option in snapshot.Options)
            {
                MicrophoneCombo.Items.Add(new ComboBoxItem
                {
                    Content = option.Content,
                    Tag = option.Tag
                });
            }

            MicrophoneCombo.SelectedIndex = snapshot.SelectedIndex >= 0 && snapshot.SelectedIndex < MicrophoneCombo.Items.Count
                ? snapshot.SelectedIndex
                : 0;

            MicStatusText.Text = snapshot.StatusText;
            MicStatusText.Foreground = snapshot.StatusKind switch
            {
                "error" => Brushes.Red,
                "warning" => Brushes.Orange,
                _ => Brushes.LightGreen,
            };
        }

        private async Task RefreshCompanionConnectionPanelAsync()
        {
            try
            {
                var snapshot = await Task.Run(BuildCompanionConnectionSnapshot).ConfigureAwait(false);
                if (_isClosed)
                    return;

                await Dispatcher.InvokeAsync(() => ApplyCompanionConnectionPanel(snapshot), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        private CompanionConnectionSnapshot BuildCompanionConnectionSnapshot()
        {
            var status = Services.CompanionTransportService.Instance.GetStatus();
            var pairing = Services.CompanionTransportService.Instance.GetPairingInfo();
            var fileTransfer = GetCompanionFileTransferDefaults(status, pairing);
            var vnc = GetCompanionVncDefaults(status, pairing);
            var setupPayload = Services.CompanionTransportService.Instance.GetProvisioningPayload(preferRemote: false);
            var remote = GetCompanionRemoteDefaults();
            var remoteSetupPayload = Services.CompanionTransportService.Instance.GetProvisioningPayload(preferRemote: true);
            var qrImage = CreateCompanionQrImage(setupPayload);
            var remoteQrImage = CreateCompanionQrImage(remoteSetupPayload);
            return new CompanionConnectionSnapshot(status, pairing, fileTransfer, vnc, setupPayload, qrImage, remote, remoteSetupPayload, remoteQrImage);
        }

        private void ApplyCompanionConnectionPanel(CompanionConnectionSnapshot snapshot)
        {
            var status = snapshot.Status;
            var pairing = snapshot.Pairing;
            var fileTransfer = snapshot.FileTransfer;
            var vnc = snapshot.Vnc;
            var remote = snapshot.Remote;
            var lifecycleState = status.LifecycleState ?? "stopped";
            var isStarting = string.Equals(lifecycleState, "starting", StringComparison.OrdinalIgnoreCase);
            var isRestarting = string.Equals(lifecycleState, "restarting", StringComparison.OrdinalIgnoreCase);
            var isBusy = isStarting || isRestarting;

            CompanionRunningValue.Text = lifecycleState switch
            {
                "running" => "Running",
                "starting" => "Starting",
                "restarting" => status.RestartAttemptCount > 0
                    ? $"Restarting (attempt {status.RestartAttemptCount})"
                    : "Restarting",
                "error" => "Error",
                _ => "Stopped",
            };
            CompanionRunningValue.Foreground = lifecycleState switch
            {
                "running" => Brushes.LimeGreen,
                "starting" => Brushes.DeepSkyBlue,
                "restarting" => Brushes.Gold,
                "error" => Brushes.OrangeRed,
                _ => Brushes.OrangeRed,
            };

            CompanionPortValue.Text = status.Port.ToString(CultureInfo.InvariantCulture);
            CompanionMobileReachabilityValue.Text = status.IsLanAccessible
                ? "LAN reachable"
                : isBusy
                    ? "Retrying startup"
                : status.IsRunning
                    ? "Localhost only"
                    : string.Equals(lifecycleState, "error", StringComparison.OrdinalIgnoreCase)
                        ? "Unavailable (startup failed)"
                    : "Unavailable";
            CompanionMobileReachabilityValue.Foreground = status.IsLanAccessible
                ? Brushes.LimeGreen
                : string.Equals(lifecycleState, "error", StringComparison.OrdinalIgnoreCase)
                    ? Brushes.OrangeRed
                    : Brushes.Gold;

            CompanionBindingModeValue.Text = string.IsNullOrWhiteSpace(status.BindingMode)
                ? "Unknown"
                : status.BindingMode;
            CompanionLanIpsValue.Text = status.DetectedLanAddresses.Length > 0
                ? string.Join(", ", status.DetectedLanAddresses)
                : "No LAN IPv4 addresses detected.";
            CompanionRecommendedUrlValue.Text = !string.IsNullOrWhiteSpace(status.RecommendedBaseUrl)
                ? status.RecommendedBaseUrl
                : "Unavailable";

            CompanionPairingStatusValue.Text = !string.IsNullOrWhiteSpace(snapshot.SetupPayload)
                ? "Setup QR is ready. Scan it from the phone to import Atlas, VNC, and SFTP defaults in one step."
                : pairing.AvailabilityMessage;
            CompanionPairingStatusValue.Foreground = !string.IsNullOrWhiteSpace(snapshot.SetupPayload) ? Brushes.LimeGreen : Brushes.Gold;
            CompanionPairingEncodedUrlValue.Text = !string.IsNullOrWhiteSpace(snapshot.SetupPayload)
                ? snapshot.SetupPayload
                : !string.IsNullOrWhiteSpace(pairing.BaseUrl)
                    ? pairing.BaseUrl
                    : "Unavailable";

            SetTextBlockValue("CompanionRemoteStatusValue", remote.StatusText, remote.IsConfigured ? Brushes.LimeGreen : Brushes.Gold);
            SetTextBlockValue("CompanionRemoteBaseUrlValue", !string.IsNullOrWhiteSpace(remote.BaseUrl) ? remote.BaseUrl : "Unavailable", Brushes.DeepSkyBlue);

            var remoteHostBox = GetTextBox("CompanionRemoteHostBox");
            if (remoteHostBox != null && !remoteHostBox.IsKeyboardFocused)
                remoteHostBox.Text = remote.Host;

            var remotePortBox = GetTextBox("CompanionRemotePortBox");
            if (remotePortBox != null && !remotePortBox.IsKeyboardFocused)
                remotePortBox.Text = remote.Port.ToString(CultureInfo.InvariantCulture);

            if (FindName("CompanionRemoteUseTlsCheckBox") is CheckBox remoteUseTlsCheckBox)
                remoteUseTlsCheckBox.IsChecked = remote.UseTls;

            SetTextBlockValue("CompanionVncStatusValue", vnc.StatusText, !string.IsNullOrWhiteSpace(vnc.Target) ? Brushes.LimeGreen : Brushes.Gold);
            SetTextBlockValue("CompanionVncTargetValue", !string.IsNullOrWhiteSpace(vnc.Target) ? vnc.Target : "Unavailable", Brushes.DeepSkyBlue);

            var vncHostBox = GetTextBox("CompanionVncHostBox");
            if (vncHostBox != null && !vncHostBox.IsKeyboardFocused)
                vncHostBox.Text = vnc.UsesDetectedHost ? string.Empty : vnc.Host;

            var vncPortBox = GetTextBox("CompanionVncPortBox");
            if (vncPortBox != null && !vncPortBox.IsKeyboardFocused)
                vncPortBox.Text = vnc.Port.ToString(CultureInfo.InvariantCulture);

            if (FindName("CompanionVncPasswordBox") is PasswordBox vncPasswordBox && !vncPasswordBox.IsKeyboardFocused)
                vncPasswordBox.Password = vnc.Password;

            SetTextBlockValue("CompanionFileTransferStatusValue", fileTransfer.StatusText, fileTransfer.IsReady ? Brushes.LimeGreen : Brushes.Gold);
            SetTextBlockValue("CompanionSftpHostValue", !string.IsNullOrWhiteSpace(fileTransfer.Host) ? fileTransfer.Host : "Unavailable", Brushes.DeepSkyBlue);
            SetTextBlockValue("CompanionSftpPortValue", fileTransfer.Port.ToString(CultureInfo.InvariantCulture));
            SetTextBlockValue("CompanionSftpUserValue", fileTransfer.Username);
            SetTextBlockValue("CompanionSftpPathValue", fileTransfer.InitialPath);
            SetTextBlockValue("CompanionSftpPasswordValue", fileTransfer.PasswordText);
            SetTextBlockValue("CompanionSftpServiceValue", fileTransfer.ServiceStatusText, fileTransfer.IsSftpServiceRunning ? Brushes.LimeGreen : Brushes.Gold);
            SetTextBlockValue("CompanionDedicatedSftpStatusValue", fileTransfer.DedicatedAccountStatusText, fileTransfer.UsesDedicatedAccount ? Brushes.LimeGreen : Brushes.SlateGray);
            SetTextBlockValue("CompanionDedicatedSftpFolderValue", fileTransfer.DedicatedFolderPath);
            SetTextBlockValue("CompanionFtpValue", fileTransfer.FtpNote, Brushes.SlateGray);

            var configuredDedicatedUsername = fileTransfer.ConfiguredDedicatedUsername;
            var dedicatedUserBox = GetTextBox("CompanionDedicatedSftpUserBox");
            if (dedicatedUserBox != null && !dedicatedUserBox.IsKeyboardFocused)
                dedicatedUserBox.Text = configuredDedicatedUsername;

            CompanionLastErrorValue.Text = !string.IsNullOrWhiteSpace(status.LastStartupError)
                ? status.LastStartupError
                : "None";
            CompanionPrefixDiagnosticsValue.Text = status.PrefixBindingResults.Length > 0
                ? string.Join(Environment.NewLine, status.PrefixBindingResults.Select(result =>
                    $"{result.Prefix} | {(result.Succeeded ? "OK" : "FAILED")} | {result.Detail}"))
                : status.ActivePrefixes.Length > 0
                    ? string.Join(Environment.NewLine, status.ActivePrefixes)
                    : "No prefix diagnostics available yet.";
            CompanionFixCommandValue.Text = !string.IsNullOrWhiteSpace(status.UrlAclFixCommand)
                ? status.UrlAclFixCommand
                : "No URL ACL fix required.";

            CompanionCopyUrlButton.IsEnabled = !string.IsNullOrWhiteSpace(pairing.BaseUrl) || !string.IsNullOrWhiteSpace(status.RecommendedBaseUrl);
            CompanionGenerateQrButton.IsEnabled = !string.IsNullOrWhiteSpace(snapshot.SetupPayload);
            CompanionCopyPayloadButton.IsEnabled = !string.IsNullOrWhiteSpace(snapshot.SetupPayload);
            SetButtonEnabled("CompanionCopyRemoteUrlButton", !string.IsNullOrWhiteSpace(remote.BaseUrl));
            SetButtonEnabled("CompanionCopyRemotePayloadButton", !string.IsNullOrWhiteSpace(snapshot.RemoteSetupPayload));
            SetButtonEnabled("CompanionCopyVncTargetButton", !string.IsNullOrWhiteSpace(vnc.Target));
            SetButtonEnabled("CompanionCopyVncProfileButton", !string.IsNullOrWhiteSpace(vnc.Target));
            SetButtonEnabled("CompanionCopySftpHostButton", !string.IsNullOrWhiteSpace(fileTransfer.Host));
            SetButtonEnabled("CompanionCopySftpProfileButton", !string.IsNullOrWhiteSpace(fileTransfer.Host));
            SetButtonEnabled("CompanionCopySftpSetupButton", true);
            SetButtonEnabled("CompanionStartSftpServiceButton", !fileTransfer.IsSftpServiceRunning);
            SetButtonEnabled("CompanionRestartSftpServiceButton", fileTransfer.IsSftpServiceInstalled);
            SetButtonEnabled("CompanionUseWindowsSftpAccountButton", fileTransfer.UsesDedicatedAccount);
            CompanionCopyFixButton.IsEnabled = !string.IsNullOrWhiteSpace(status.UrlAclFixCommand);
            CompanionRestartButton.IsEnabled = status.IsRunning && !isBusy;
            CompanionStartButton.IsEnabled = !status.IsRunning && !isBusy;
            CompanionTestStatusText.Text = !string.IsNullOrWhiteSpace(snapshot.SetupPayload)
                ? "Setup QR is ready. Use the QR image or the copy buttons if you need the payload manually."
                : pairing.AvailabilityMessage;

            CompanionQrImage.Source = snapshot.QrImage;
            CompanionRemoteQrImage.Source = snapshot.RemoteQrImage;
            if (FindName("CompanionRemoteQrContainer") is Border companionRemoteQrContainer)
                companionRemoteQrContainer.Visibility = !string.IsNullOrWhiteSpace(snapshot.RemoteSetupPayload)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        
        private void DetectSystemHardware()
        {
            ApplyHardwareSnapshot(BuildHardwareSnapshot());
        }
        
        private void LoadMicrophones()
        {
            ApplyMicrophoneLoadSnapshot(BuildMicrophoneLoadSnapshot(_selectedMicDeviceId, _selectedMicDeviceName, _selectedMicDevice));
        }
        
        // Fallback method using legacy WaveIn API
        private void LoadMicrophonesLegacy()
        {
            var deviceCount = WaveIn.DeviceCount;
            if (deviceCount == 0)
            {
                MicStatusText.Text = "🎤 No microphone detected!";
                MicStatusText.Foreground = System.Windows.Media.Brushes.Red;
                MicrophoneCombo.SelectedIndex = 0;
                return;
            }
            
            for (int i = 0; i < deviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                var name = caps.ProductName;
                
                var icon = "🎤";
                var nameLower = name.ToLower();
                if (nameLower.Contains("airpod") || nameLower.Contains("bluetooth") || nameLower.Contains("wireless"))
                    icon = "🎧";
                else if (nameLower.Contains("headset") || nameLower.Contains("headphone"))
                    icon = "🎧";
                else if (nameLower.Contains("usb"))
                    icon = "🎙️";
                else if (nameLower.Contains("webcam") || nameLower.Contains("camera"))
                    icon = "📷";
                
                MicrophoneCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{icon} {name}",
                    Tag = i.ToString() // Store index as string for legacy
                });
            }
            
            MicrophoneCombo.SelectedIndex = 0;
        }
        
        private MMDevice? GetBestMicDevice()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                
                // If specific device ID is set, use it
                if (!string.IsNullOrEmpty(_selectedMicDeviceId) && _selectedMicDeviceId != "auto")
                {
                    try
                    {
                        var device = enumerator.GetDevice(_selectedMicDeviceId);
                        if (device != null && device.State == DeviceState.Active)
                            return device;
                    }
                    catch { }
                }

                var defaultDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Communications)
                    ?? TryGetDefaultCaptureEndpoint(enumerator, Role.Multimedia)
                    ?? TryGetDefaultCaptureEndpoint(enumerator, Role.Console);

                if (defaultDevice != null)
                    return defaultDevice;

                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var device in devices)
                {
                    var name = (device.FriendlyName ?? string.Empty).ToLowerInvariant();
                    if (name.Contains("stereo mix") || name.Contains("loopback"))
                        continue;

                    return device;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void RefreshMic_Click(object sender, RoutedEventArgs e)
        {
            LoadMicrophones();
        }
        
        private async void TestMic_Click(object sender, RoutedEventArgs e)
        {
            TestMicBtn.IsEnabled = false;
            TestMicBtn.Content = "Testing...";
            MicStatusText.Text = "🎤 Testing microphone...";
            MicStatusText.Foreground = System.Windows.Media.Brushes.Yellow;
            
            // Check if we have a WASAPI device ID selected (for Bluetooth/AirPods)
            string? deviceId = null;
            if (MicrophoneCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && tagStr != "auto")
            {
                // Check if it's a WASAPI device ID (not a numeric index)
                if (!int.TryParse(tagStr, out _))
                {
                    deviceId = tagStr;
                }
            }
            
            // If we have a device ID, test using WASAPI directly
            if (!string.IsNullOrEmpty(deviceId))
            {
                Debug.WriteLine($"[Settings] Testing WASAPI device: {deviceId}");
                
                await Task.Run(() =>
                {
                    var (working, level, message) = WhisperSpeechRecognition.TestMicrophoneByDeviceId(deviceId);
                    
                    Dispatcher.Invoke(() =>
                    {
                        if (working)
                        {
                            MicStatusText.Text = $"✓ Mic working! Level: {level:F0}";
                            MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                        }
                        else
                        {
                            MicStatusText.Text = $"✗ Mic NOT responding! Level: {level:F0}";
                            MicStatusText.Foreground = System.Windows.Media.Brushes.Red;
                        }
                        
                        TestMicBtn.IsEnabled = true;
                        TestMicBtn.Content = "🎤 Test";
                    });
                });
                return;
            }
            
            // Use WaveIn device index
            int deviceIndex = GetSelectedDeviceIndex();
            
            // If auto or invalid, test all mics
            if (deviceIndex < 0 || deviceIndex >= WaveIn.DeviceCount)
            {
                await TestAllMicrophonesAsync();
                return;
            }
            
            // Test the specific mic
            string deviceName = "Unknown";
            if (deviceIndex >= 0 && deviceIndex < WaveIn.DeviceCount)
            {
                var caps = WaveIn.GetCapabilities(deviceIndex);
                deviceName = caps.ProductName;
            }
            Debug.WriteLine($"[Settings] Testing device {deviceIndex}: {deviceName}");
            
            await Task.Run(() =>
            {
                var (working, level, message) = WhisperSpeechRecognition.TestMicrophone(deviceIndex);
                
                Dispatcher.Invoke(() =>
                {
                    if (working)
                    {
                        MicStatusText.Text = $"✓ Mic working! Level: {level:F0}";
                        MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    }
                    else
                    {
                        MicStatusText.Text = $"✗ Mic NOT responding! Level: {level:F0}";
                        MicStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    }
                    
                    TestMicBtn.IsEnabled = true;
                    TestMicBtn.Content = "🎤 Test";
                });
            });
        }
        
        private async Task TestAllMicrophonesAsync()
        {
            TestMicBtn.IsEnabled = false;
            TestMicBtn.Content = "Testing...";
            MicStatusText.Text = "🎤 Testing all microphones...";
            MicStatusText.Foreground = System.Windows.Media.Brushes.Yellow;
            
            await Task.Run(() =>
            {
                var results = WhisperSpeechRecognition.GetWorkingMicrophones();
                var workingMics = results.Where(r => r.Working).ToList();
                
                Dispatcher.Invoke(() =>
                {
                    if (workingMics.Count > 0)
                    {
                        var best = workingMics.OrderByDescending(m => m.Level).First();
                        MicStatusText.Text = $"✓ Found {workingMics.Count} working mic(s). Best: {best.Name}";
                        MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                        
                        // Auto-select the best working mic
                        for (int i = 0; i < MicrophoneCombo.Items.Count; i++)
                        {
                            if (MicrophoneCombo.Items[i] is ComboBoxItem comboItem)
                            {
                                var content = comboItem.Content?.ToString() ?? "";
                                if (content.Contains(best.Name))
                                {
                                    MicrophoneCombo.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        MicStatusText.Text = "✗ No working microphones found!";
                        MicStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    }
                    
                    TestMicBtn.IsEnabled = true;
                    TestMicBtn.Content = "🎤 Test";
                });
            });
        }
        
        private void Microphone_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (MicrophoneCombo.SelectedItem is ComboBoxItem item && item.Tag is string deviceTag)
            {
                if (deviceTag == "auto")
                {
                    _selectedMicDeviceId = null;
                    _selectedMicDeviceName = "";
                    _selectedMicDevice = -1;
                    
                    var bestDevice = GetBestMicDevice();
                    if (bestDevice != null)
                    {
                        MicStatusText.Text = $"🎤 Using Windows microphone: {bestDevice.FriendlyName}";
                        MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    }
                }
                else
                {
                    if (int.TryParse(deviceTag, out int waveInIndex))
                    {
                        _selectedMicDevice = waveInIndex;
                        _selectedMicDeviceId = null;

                        try
                        {
                            using var enumerator = new MMDeviceEnumerator();
                            var matchedDevice = FindWasapiDeviceForWaveInIndex(enumerator, waveInIndex);
                            if (matchedDevice != null)
                            {
                                _selectedMicDeviceName = matchedDevice.FriendlyName;
                                MicStatusText.Text = $"🎤 Selected: {matchedDevice.FriendlyName}";
                            }
                            else if (waveInIndex >= 0 && waveInIndex < WaveIn.DeviceCount)
                            {
                                var caps = WaveIn.GetCapabilities(waveInIndex);
                                _selectedMicDeviceName = caps.ProductName;
                                MicStatusText.Text = $"🎤 Selected: {caps.ProductName}";
                            }
                            else
                            {
                                _selectedMicDeviceName = "";
                                MicStatusText.Text = "🎤 Selected device";
                            }
                            MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                        }
                        catch
                        {
                            MicStatusText.Text = "🎤 Selected device";
                            MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                        }
                    }
                    else
                    {
                        _selectedMicDeviceId = deviceTag;
                        _selectedMicDevice = -1;
                        
                        try
                        {
                            using var enumerator = new MMDeviceEnumerator();
                            var device = enumerator.GetDevice(deviceTag);
                            if (device != null)
                            {
                                _selectedMicDeviceName = device.FriendlyName;
                                MicStatusText.Text = $"🎤 Selected: {device.FriendlyName}";
                                MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                            }
                        }
                        catch
                        {
                            _selectedMicDeviceName = "";
                            MicStatusText.Text = $"🎤 Selected device";
                            MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                        }
                    }
                }
            }
            // Legacy fallback for int tags
            else if (MicrophoneCombo.SelectedItem is ComboBoxItem legacyItem && legacyItem.Tag is int deviceIndex)
            {
                _selectedMicDevice = deviceIndex;
                _selectedMicDeviceId = null;
                _selectedMicDeviceName = "";
                
                if (deviceIndex == -1)
                {
                    var bestDevice = GetBestMicDevice();
                    if (bestDevice != null)
                    {
                        MicStatusText.Text = $"🎤 Auto-selected: {bestDevice.FriendlyName}";
                        MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    }
                }
                else if (deviceIndex >= 0 && deviceIndex < WaveIn.DeviceCount)
                {
                    var caps = WaveIn.GetCapabilities(deviceIndex);
                    _selectedMicDeviceName = caps.ProductName;
                    MicStatusText.Text = $"🎤 Selected: {caps.ProductName}";
                    MicStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                }
            }
        }
        
        private void MicSensitivity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _micSensitivity = (int)MicSensitivitySlider.Value;
            if (MicSensitivityValue != null)
                MicSensitivityValue.Text = _micSensitivity.ToString();
        }
        
        private void QualityMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return; // Prevent crash during initialization
            if (QualityModeCombo?.SelectedItem is ComboBoxItem item && item.Tag is string mode)
            {
                _qualityMode = mode;
                UpdatePerformanceInfo(mode);
            }
        }

        private void AiPublicQualityMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            QualityMode_Changed(sender, e);
        }

        private void AiRoutingMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string mode)
                return;

            Settings.SettingsStore.Update(settings => settings.AiRuntime.RoutingMode = mode);
        }

        private void AiCostMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string mode)
                return;

            Settings.SettingsStore.Update(settings => settings.AiRuntime.CostMode = mode);
        }

        private void DistributionBillingMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string mode)
                return;

            Settings.SettingsStore.Update(settings => settings.Distribution.BillingMode = mode);
        }

        private void DistributionSimulationEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || sender is not CheckBox checkBox)
                return;

            Settings.SettingsStore.Update(settings => settings.Distribution.EnablePublicPlanSimulation = checkBox.IsChecked == true);
        }

        private void DistributionSimulatedPlan_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string planId)
                return;

            Settings.SettingsStore.Update(settings => settings.Distribution.SimulatedPlanId = planId);
        }

        private void DistributionSimulatedBillingMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string mode)
                return;

            Settings.SettingsStore.Update(settings => settings.Distribution.SimulatedBillingMode = mode);
        }

        private void CompanionStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Services.CompanionTransportService.Instance.Start();
                RefreshCompanionConnectionPanel();
                MessageBox.Show("Companion API started.", "Companion", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Companion", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CompanionRestart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Services.CompanionTransportService.Instance.Stop();
                Services.CompanionTransportService.Instance.Start();
                RefreshCompanionConnectionPanel();
                MessageBox.Show("Companion API restarted.", "Companion", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Companion", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CompanionCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            var pairing = Services.CompanionTransportService.Instance.GetPairingInfo();
            var status = Services.CompanionTransportService.Instance.GetStatus();
            var value = !string.IsNullOrWhiteSpace(pairing.BaseUrl)
                ? pairing.BaseUrl
                : status.RecommendedBaseUrl;

            if (!string.IsNullOrWhiteSpace(value))
                Clipboard.SetText(value);
        }

        private void CompanionGenerateQr_Click(object sender, RoutedEventArgs e)
        {
            RefreshCompanionConnectionPanel();
        }

        private void CompanionCopyPayload_Click(object sender, RoutedEventArgs e)
        {
            var payload = GetCompanionSetupPayload();
            if (!string.IsNullOrWhiteSpace(payload))
                Clipboard.SetText(payload);
        }

        private void CompanionSaveRemoteProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var host = (GetTextBox("CompanionRemoteHostBox")?.Text ?? string.Empty).Trim();
                var portText = (GetTextBox("CompanionRemotePortBox")?.Text ?? string.Empty).Trim();
                var port = int.TryParse(portText, out var parsedPort) && parsedPort > 0 ? parsedPort : 3000;
                var useTls = (FindName("CompanionRemoteUseTlsCheckBox") as CheckBox)?.IsChecked == true;

                var settings = global::AtlasAI.Settings.SettingsStore.Current;
                settings.CompanionFileTransfer.RemoteHost = host;
                settings.CompanionFileTransfer.RemotePort = port;
                settings.CompanionFileTransfer.RemoteUseTls = useTls;
                global::AtlasAI.Settings.SettingsStore.Save(settings);

                RefreshCompanionConnectionPanel();
                MessageBox.Show(
                    string.IsNullOrWhiteSpace(host)
                        ? "Remote hostname cleared. The away-from-home QR is disabled until you save a remote hostname again."
                        : "Remote hostname saved. The away-from-home QR now points the phone at your saved remote endpoint.",
                    "Remote Access",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Remote Access", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CompanionCopyRemoteUrl_Click(object sender, RoutedEventArgs e)
        {
            var remote = GetCompanionRemoteDefaults();
            if (!string.IsNullOrWhiteSpace(remote.BaseUrl))
                Clipboard.SetText(remote.BaseUrl);
        }

        private void CompanionCopyRemotePayload_Click(object sender, RoutedEventArgs e)
        {
            var payload = GetCompanionRemoteSetupPayload();
            if (!string.IsNullOrWhiteSpace(payload))
                Clipboard.SetText(payload);
        }

        private void CompanionSaveVncProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var host = (GetTextBox("CompanionVncHostBox")?.Text ?? string.Empty).Trim();
                var portText = (GetTextBox("CompanionVncPortBox")?.Text ?? string.Empty).Trim();
                var password = GetPasswordBoxValue("CompanionVncPasswordBox");
                var port = int.TryParse(portText, out var parsedPort) && parsedPort > 0 ? parsedPort : 5900;

                var settings = global::AtlasAI.Settings.SettingsStore.Current;
                settings.CompanionFileTransfer.VncHost = host;
                settings.CompanionFileTransfer.VncPort = port;
                settings.CompanionFileTransfer.VncPassword = password ?? string.Empty;
                global::AtlasAI.Settings.SettingsStore.Save(settings);

                RefreshCompanionConnectionPanel();
                MessageBox.Show(
                    string.IsNullOrWhiteSpace(host)
                        ? "VNC target cleared. Atlas will fall back to the detected LAN host for setup QR generation."
                        : "VNC target saved for setup QR generation and desktop viewer launch.",
                    "VNC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VNC", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CompanionCopyVncTarget_Click(object sender, RoutedEventArgs e)
        {
            var vnc = GetCompanionVncDefaults();
            if (!string.IsNullOrWhiteSpace(vnc.Target))
                Clipboard.SetText(vnc.Target);
        }

        private void CompanionCopyVncProfile_Click(object sender, RoutedEventArgs e)
        {
            var vnc = GetCompanionVncDefaults();
            if (!string.IsNullOrWhiteSpace(vnc.ProfileText))
                Clipboard.SetText(vnc.ProfileText);
        }

        private void CompanionCopySftpHost_Click(object sender, RoutedEventArgs e)
        {
            var defaults = GetCompanionFileTransferDefaults();
            if (!string.IsNullOrWhiteSpace(defaults.Host))
                Clipboard.SetText(defaults.Host);
        }

        private void CompanionCopySftpProfile_Click(object sender, RoutedEventArgs e)
        {
            var defaults = GetCompanionFileTransferDefaults();
            if (!string.IsNullOrWhiteSpace(defaults.ProfileText))
                Clipboard.SetText(defaults.ProfileText);
        }

        private void CompanionCopySftpSetup_Click(object sender, RoutedEventArgs e)
        {
            var defaults = GetCompanionFileTransferDefaults();
            Clipboard.SetText(defaults.SetupCommand);
        }

        private void CompanionApplyDedicatedSftp_Click(object sender, RoutedEventArgs e)
        {
            var requestedUsername = (GetTextBox("CompanionDedicatedSftpUserBox")?.Text ?? string.Empty).Trim();
            var password = GetPasswordBoxValue("CompanionDedicatedSftpPasswordBox");
            var confirmPassword = GetPasswordBoxValue("CompanionDedicatedSftpPasswordConfirmBox");

            if (!Regex.IsMatch(requestedUsername, "^[a-zA-Z0-9._-]{3,32}$"))
            {
                MessageBox.Show("Choose a username using 3-32 letters, numbers, dots, underscores, or hyphens.", "SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (password.Length < 8)
            {
                MessageBox.Show("Choose an SFTP password with at least 8 characters.", "SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
            {
                MessageBox.Show("The SFTP passwords do not match.", "SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dedicatedFolder = GetDedicatedSftpFolderPath();
                EnsureDedicatedSftpAccount(requestedUsername, password, dedicatedFolder);

                var settings = global::AtlasAI.Settings.SettingsStore.Current;
                settings.CompanionFileTransfer.UseDedicatedAccount = true;
                settings.CompanionFileTransfer.DedicatedUsername = requestedUsername;
                settings.CompanionFileTransfer.DedicatedFolderPath = dedicatedFolder;
                global::AtlasAI.Settings.SettingsStore.Save(settings);

                SetPasswordBoxValue("CompanionDedicatedSftpPasswordBox", string.Empty);
                SetPasswordBoxValue("CompanionDedicatedSftpPasswordConfirmBox", string.Empty);
                RefreshCompanionConnectionPanel();
                MessageBox.Show("Atlas created or updated the dedicated SFTP login. Use that username and password from the phone.", "SFTP", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CompanionUseWindowsSftpAccount_Click(object sender, RoutedEventArgs e)
        {
            var settings = global::AtlasAI.Settings.SettingsStore.Current;
            settings.CompanionFileTransfer.UseDedicatedAccount = false;
            global::AtlasAI.Settings.SettingsStore.Save(settings);
            SetPasswordBoxValue("CompanionDedicatedSftpPasswordBox", string.Empty);
            SetPasswordBoxValue("CompanionDedicatedSftpPasswordConfirmBox", string.Empty);
            RefreshCompanionConnectionPanel();
        }

        private void CompanionStartSftpService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureSftpServiceRunning(restart: false);
                RefreshCompanionConnectionPanel();
                MessageBox.Show("OpenSSH Server is installed or started and SFTP is ready. Use your Windows sign-in password from the phone unless you created a dedicated Atlas transfer login.", "SFTP", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CompanionRestartSftpService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureSftpServiceRunning(restart: true);
                RefreshCompanionConnectionPanel();
                MessageBox.Show("OpenSSH SFTP service restarted.", "SFTP", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CompanionOpenPasswordSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:signinoptions") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CompanionCopyFixCommand_Click(object sender, RoutedEventArgs e)
        {
            var status = Services.CompanionTransportService.Instance.GetStatus();
            if (!string.IsNullOrWhiteSpace(status.UrlAclFixCommand))
                Clipboard.SetText(status.UrlAclFixCommand);
        }

        private void CompanionTestStatus_Click(object sender, RoutedEventArgs e)
        {
            var status = Services.CompanionTransportService.Instance.GetStatus();
            var message = !string.IsNullOrWhiteSpace(status.LastStartupError)
                ? status.LastStartupError
                : string.Equals(status.LifecycleState, "restarting", StringComparison.OrdinalIgnoreCase)
                    ? status.RestartAttemptCount > 0
                        ? $"Companion service is retrying startup on port {status.Port} (attempt {status.RestartAttemptCount})."
                        : $"Companion service is retrying startup on port {status.Port}."
                : string.Equals(status.LifecycleState, "starting", StringComparison.OrdinalIgnoreCase)
                    ? $"Companion service is starting on port {status.Port}."
                : status.IsRunning
                    ? $"Companion service is running on port {status.Port}."
                    : "Companion service is stopped.";
            MessageBox.Show(message, "Companion Status", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CompanionTransportService_StatusChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(new Action(() => _ = RefreshCompanionConnectionPanelAsync()));
                return;
            }

            _ = RefreshCompanionConnectionPanelAsync();
        }

        private void RefreshCompanionConnectionPanel()
        {
            ApplyCompanionConnectionPanel(BuildCompanionConnectionSnapshot());
        }

        private void SetTextBlockValue(string name, string text, Brush? foreground = null)
        {
            if (FindName(name) is not TextBlock textBlock)
                return;

            textBlock.Text = text;
            if (foreground != null)
                textBlock.Foreground = foreground;
        }

        private void SetButtonEnabled(string name, bool isEnabled)
        {
            if (FindName(name) is Button button)
                button.IsEnabled = isEnabled;
        }

        private TextBox? GetTextBox(string name)
        {
            return FindName(name) as TextBox;
        }

        private static string GetPasswordBoxValue(string name)
        {
            return (Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window is SettingsWindow) as SettingsWindow)?.FindName(name) is PasswordBox passwordBox
                ? passwordBox.Password ?? string.Empty
                : string.Empty;
        }

        private static void SetPasswordBoxValue(string name, string value)
        {
            if ((Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window is SettingsWindow) as SettingsWindow)?.FindName(name) is PasswordBox passwordBox)
                passwordBox.Password = value ?? string.Empty;
        }

        private sealed record CompanionFileTransferDefaults(
            string Host,
            int Port,
            string Username,
            string InitialPath,
            string PasswordText,
            bool IsReady,
            bool IsSftpServiceInstalled,
            bool IsSftpServiceRunning,
            string ServiceStatusText,
            bool UsesDedicatedAccount,
            string ConfiguredDedicatedUsername,
            string DedicatedFolderPath,
            string DedicatedAccountStatusText,
            string StatusText,
            string FtpNote,
            string ProfileText,
            string SetupCommand);

        private CompanionVncDefaults GetCompanionVncDefaults()
        {
            return GetCompanionVncDefaults(
                Services.CompanionTransportService.Instance.GetStatus(),
                Services.CompanionTransportService.Instance.GetPairingInfo());
        }

        private CompanionRemoteDefaults GetCompanionRemoteDefaults()
        {
            var settings = global::AtlasAI.Settings.SettingsStore.Current;
            var companionSettings = settings.CompanionFileTransfer ?? new global::AtlasAI.Settings.CompanionFileTransferSettings();
            var host = (companionSettings.RemoteHost ?? string.Empty).Trim();
            var port = companionSettings.RemotePort > 0 ? companionSettings.RemotePort : 3000;
            var useTls = companionSettings.RemoteUseTls;
            var scheme = useTls ? "https" : "http";
            var baseUrl = string.IsNullOrWhiteSpace(host) ? string.Empty : $"{scheme}://{host}:{port}";
            var statusText = string.IsNullOrWhiteSpace(host)
                ? "Save a tunnel hostname, VPN DNS name, Tailscale name, or public reverse-proxy address here to generate a separate away-from-home setup QR."
                : "Away-from-home setup QR is ready. Scan it on the phone to import the saved remote Atlas endpoint separately from your home LAN profile.";

            return new CompanionRemoteDefaults(host, port, useTls, baseUrl, statusText, !string.IsNullOrWhiteSpace(host));
        }

        private static CompanionVncDefaults GetCompanionVncDefaults(
            CompanionTransportService.CompanionTransportStatus status,
            CompanionTransportService.CompanionPairingInfo pairing)
        {
            var settings = global::AtlasAI.Settings.SettingsStore.Current;
            var companionSettings = settings.CompanionFileTransfer ?? new global::AtlasAI.Settings.CompanionFileTransferSettings();
            var configuredHost = (companionSettings.VncHost ?? string.Empty).Trim();
            var usesDetectedHost = string.IsNullOrWhiteSpace(configuredHost);
            var resolvedHost = usesDetectedHost
                ? ResolveCompanionFileTransferHost(status, pairing)
                : configuredHost;
            var port = companionSettings.VncPort > 0 ? companionSettings.VncPort : 5900;
            var password = companionSettings.VncPassword ?? string.Empty;
            var target = string.IsNullOrWhiteSpace(resolvedHost)
                ? string.Empty
                : $"{resolvedHost}:{port}";
            var statusText = string.IsNullOrWhiteSpace(target)
                ? "Atlas cannot generate a VNC target until a LAN host is detected or you enter a custom VNC hostname below."
                : usesDetectedHost
                    ? "Using the detected Atlas LAN host for VNC. Save a custom VNC host below if your viewer uses a different address."
                    : "Custom VNC target stored for setup QR generation and external viewer launch.";
            var profileText = string.IsNullOrWhiteSpace(target)
                ? string.Empty
                : string.Join(Environment.NewLine, new[]
                {
                    "Protocol: VNC",
                    $"Host: {resolvedHost}",
                    $"Port: {port}",
                    string.IsNullOrWhiteSpace(password)
                        ? "Password: set this in Atlas if your VNC server requires one"
                        : "Password: the saved VNC password is embedded in the setup QR payload",
                });

            return new CompanionVncDefaults(
                resolvedHost,
                port,
                password,
                target,
                statusText,
                profileText,
                usesDetectedHost);
        }

        private string? GetCompanionSetupPayload()
        {
            return Services.CompanionTransportService.Instance.GetProvisioningPayload(preferRemote: false);
        }

        private string? GetCompanionRemoteSetupPayload()
        {
            return Services.CompanionTransportService.Instance.GetProvisioningPayload(preferRemote: true);
        }

        private static string? BuildCompanionSetupPayload(
            CompanionTransportService.CompanionPairingInfo pairing,
            CompanionFileTransferDefaults fileTransfer,
            CompanionVncDefaults vnc)
        {
            return Services.CompanionTransportService.Instance.GetProvisioningPayload(preferRemote: false);
        }

        private static string? BuildCompanionRemoteSetupPayload(
            CompanionTransportService.CompanionPairingInfo pairing,
            CompanionRemoteDefaults remote,
            CompanionFileTransferDefaults fileTransfer,
            CompanionVncDefaults vnc)
        {
            return Services.CompanionTransportService.Instance.GetProvisioningPayload(preferRemote: true);
        }

        private CompanionFileTransferDefaults GetCompanionFileTransferDefaults()
        {
            return GetCompanionFileTransferDefaults(
                Services.CompanionTransportService.Instance.GetStatus(),
                Services.CompanionTransportService.Instance.GetPairingInfo());
        }

        private static CompanionFileTransferDefaults GetCompanionFileTransferDefaults(
            CompanionTransportService.CompanionTransportStatus status,
            CompanionTransportService.CompanionPairingInfo pairing)
        {
            var settings = global::AtlasAI.Settings.SettingsStore.Current;
            var companionFileTransfer = settings.CompanionFileTransfer ?? new global::AtlasAI.Settings.CompanionFileTransferSettings();
            var host = ResolveCompanionFileTransferHost(status, pairing);
            const int port = 22;
            var dedicatedFolderPath = string.IsNullOrWhiteSpace(companionFileTransfer.DedicatedFolderPath)
                ? GetDefaultDedicatedSftpFolderPath()
                : companionFileTransfer.DedicatedFolderPath.Trim();
            var configuredDedicatedUsername = string.IsNullOrWhiteSpace(companionFileTransfer.DedicatedUsername)
                ? "atlas_sftp"
                : companionFileTransfer.DedicatedUsername.Trim();
            var usesDedicatedAccount = companionFileTransfer.UseDedicatedAccount;
            var username = usesDedicatedAccount ? configuredDedicatedUsername : Environment.UserName;
            var initialPath = usesDedicatedAccount ? dedicatedFolderPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var passwordText = usesDedicatedAccount
                ? "Use the custom Atlas SFTP password you created for the dedicated transfer account. Atlas does not display that password after applying it."
                : "Use your Windows sign-in password. Atlas does not store or display that password.";
            var sftpServiceState = GetSftpServiceState();
            var hostReady = !string.IsNullOrWhiteSpace(host) &&
                !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);

            var isReady = status.IsLanAccessible && hostReady && sftpServiceState.IsRunning;
            var statusText = isReady
                ? usesDedicatedAccount
                    ? "SFTP defaults are ready for the companion app. Use the dedicated Atlas transfer username and password from the phone."
                    : "SFTP defaults are ready for the companion app. Use your Windows sign-in username and password to connect from the phone."
                : sftpServiceState.IsInstalled
                    ? "Atlas can show the SFTP defaults now, but phone transfer needs both LAN reachability and the OpenSSH service running."
                    : "OpenSSH Server is not installed yet. Install or start it, then connect from the phone with your Windows username and password.";
            var dedicatedAccountStatusText = usesDedicatedAccount
                ? $"Dedicated Atlas transfer login enabled for '{configuredDedicatedUsername}'."
                : "Using your Windows sign-in account for SFTP. If you sign in with a PIN only, create a dedicated Atlas transfer login below.";
            var ftpNote = "Atlas does not host an FTP server itself. Use SFTP on port 22 for Atlas-to-phone transfer, or point the companion app at a separate FTP server you manage yourself.";
            var profileText = string.Join(Environment.NewLine, new[]
            {
                "Protocol: SFTP",
                $"Host: {(string.IsNullOrWhiteSpace(host) ? "Unavailable" : host)}",
                $"Port: {port}",
                $"Username: {username}",
                "Password: your Windows sign-in password",
                $"Initial remote path: {initialPath}",
                ftpNote,
            });
            var setupCommand = "Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0; Start-Service sshd; Set-Service -Name sshd -StartupType Automatic; if (-not (Get-NetFirewallRule -Name sshd -ErrorAction SilentlyContinue)) { New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 }";

            return new CompanionFileTransferDefaults(
                host,
                port,
                username,
                initialPath,
                passwordText,
                isReady,
                sftpServiceState.IsInstalled,
                sftpServiceState.IsRunning,
                sftpServiceState.StatusText,
                usesDedicatedAccount,
                configuredDedicatedUsername,
                dedicatedFolderPath,
                dedicatedAccountStatusText,
                statusText,
                ftpNote,
                profileText,
                setupCommand);

        }

        private static string GetDefaultDedicatedSftpFolderPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Atlas Companion Transfer");
        }

        private static string GetDedicatedSftpFolderPath()
        {
            var configured = global::AtlasAI.Settings.SettingsStore.Current.CompanionFileTransfer?.DedicatedFolderPath;
            return string.IsNullOrWhiteSpace(configured)
                ? GetDefaultDedicatedSftpFolderPath()
                : configured.Trim();
        }

        private sealed record SftpServiceState(bool IsInstalled, bool IsRunning, string StatusText);

        private static SftpServiceState GetSftpServiceState()
        {
            try
            {
                using var controller = new ServiceController("sshd");
                var status = controller.Status;
                var statusText = status switch
                {
                    ServiceControllerStatus.Running => "Running",
                    ServiceControllerStatus.StartPending => "Starting",
                    ServiceControllerStatus.StopPending => "Stopping",
                    ServiceControllerStatus.Stopped => "Stopped",
                    ServiceControllerStatus.Paused => "Paused",
                    ServiceControllerStatus.PausePending => "Pausing",
                    ServiceControllerStatus.ContinuePending => "Resuming",
                    _ => status.ToString(),
                };

                return new SftpServiceState(true, status == ServiceControllerStatus.Running, statusText);
            }
            catch (InvalidOperationException)
            {
                return new SftpServiceState(false, false, "Not installed");
            }
            catch (Exception ex)
            {
                return new SftpServiceState(false, false, ex.Message);
            }
        }

        private static void EnsureSftpServiceRunning(bool restart)
        {
            var script = new StringBuilder();
            script.AppendLine("$service = Get-Service -Name sshd -ErrorAction SilentlyContinue");
            script.AppendLine("if (-not $service) {");
            script.AppendLine("  Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 | Out-Null");
            script.AppendLine("  $service = Get-Service -Name sshd -ErrorAction Stop");
            script.AppendLine("}");
            script.AppendLine("Set-Service -Name sshd -StartupType Automatic");
            script.AppendLine("if (-not (Get-NetFirewallRule -Name sshd -ErrorAction SilentlyContinue)) {");
            script.AppendLine("  New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null");
            script.AppendLine("}");
            script.AppendLine(restart
                ? "if ($service.Status -eq 'Running') { Restart-Service sshd -Force } else { Start-Service sshd }"
                : "if ($service.Status -ne 'Running') { Start-Service sshd }");

            RunElevatedPowerShell(script.ToString());
        }

        private static void EnsureDedicatedSftpAccount(string username, string password, string folderPath)
        {
            var escapedUsername = EscapePowerShellSingleQuotedString(username);
            var escapedPassword = EscapePowerShellSingleQuotedString(password);
            var escapedFolder = EscapePowerShellSingleQuotedString(folderPath);

            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine($"$username = '{escapedUsername}'");
            script.AppendLine($"$password = '{escapedPassword}'");
            script.AppendLine($"$folder = '{escapedFolder}'");
            script.AppendLine("$secure = ConvertTo-SecureString $password -AsPlainText -Force");
            script.AppendLine("$service = Get-Service -Name sshd -ErrorAction SilentlyContinue");
            script.AppendLine("if (-not $service) {");
            script.AppendLine("  Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 | Out-Null");
            script.AppendLine("  $service = Get-Service -Name sshd -ErrorAction Stop");
            script.AppendLine("}");
            script.AppendLine("Set-Service -Name sshd -StartupType Automatic");
            script.AppendLine("if (-not (Get-NetFirewallRule -Name sshd -ErrorAction SilentlyContinue)) {");
            script.AppendLine("  New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null");
            script.AppendLine("}");
            script.AppendLine("$existingUser = Get-LocalUser -Name $username -ErrorAction SilentlyContinue");
            script.AppendLine("if ($null -eq $existingUser) {");
            script.AppendLine("  New-LocalUser -Name $username -Password $secure -FullName 'Atlas SFTP Transfer' -Description 'Atlas companion SFTP transfer account' -PasswordNeverExpires -AccountNeverExpires | Out-Null");
            script.AppendLine("} else {");
            script.AppendLine("  $existingUser | Set-LocalUser -Password $secure");
            script.AppendLine("}");
            script.AppendLine("cmd /c \"net localgroup Users $username /add\" | Out-Null");
            script.AppendLine("New-Item -ItemType Directory -Path $folder -Force | Out-Null");
            script.AppendLine("cmd /c \"icacls \"\"$folder\"\" /grant $username`:(OI`)(CI`)M /T /C\" | Out-Null");
            script.AppendLine("if ((Get-Service -Name sshd).Status -ne 'Running') { Start-Service sshd }");

            RunElevatedPowerShell(script.ToString());
        }

        private static string EscapePowerShellSingleQuotedString(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private static void RunElevatedPowerShell(string script)
        {
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (process == null)
                throw new InvalidOperationException("Atlas could not start the elevated PowerShell process for OpenSSH.");

            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Windows did not complete the OpenSSH service command. Accept the admin prompt and try again.");
        }

        private static string ResolveCompanionFileTransferHost(
            CompanionTransportService.CompanionTransportStatus status,
            CompanionTransportService.CompanionPairingInfo pairing)
        {
            if (!string.IsNullOrWhiteSpace(pairing.Host) &&
                !string.Equals(pairing.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(pairing.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                return pairing.Host;
            }

            if (!string.IsNullOrWhiteSpace(status.RecommendedBaseUrl) &&
                Uri.TryCreate(status.RecommendedBaseUrl, UriKind.Absolute, out var recommendedUri) &&
                !string.IsNullOrWhiteSpace(recommendedUri.Host))
            {
                return recommendedUri.Host;
            }

            if (status.DetectedLanAddresses.Length > 0)
            {
                return status.DetectedLanAddresses[0];
            }

            return string.Empty;
        }

        private static ImageSource? CreateCompanionQrImage(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);
                var pngQrCode = new PngByteQRCode(data);
                var pngBytes = pngQrCode.GetGraphic(
                    12,
                    new byte[] { 0, 0, 0, 255 },
                    new byte[] { 255, 255, 255, 255 },
                    true);

                using var stream = new MemoryStream(pngBytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Companion QR generation failed: {ex}");
                return null;
            }
        }

        private void OptionalAssetPackUrl_LostFocus(object sender, RoutedEventArgs e)
        {
        }

        private void OpenOptionalAssetPackUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is string value)
                TryOpenHttpsUrl(value);
        }
        
        private void SelectQualityMode(string mode)
        {
            for (int i = 0; i < QualityModeCombo.Items.Count; i++)
            {
                if (QualityModeCombo.Items[i] is ComboBoxItem item && item.Tag as string == mode)
                {
                    QualityModeCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        
        private void UpdatePerformanceInfo(string mode)
        {
            if (PerformanceInfoText == null) return; // Null check
            switch (mode)
            {
                case "low":
                    PerformanceInfoText.Text = "🐢 Battery Saver: Longer response times, lower CPU usage. Good for laptops on battery.";
                    break;
                case "balanced":
                    PerformanceInfoText.Text = "⚖️ Balanced: Good response times with moderate CPU usage. Recommended for most systems.";
                    break;
                case "high":
                    PerformanceInfoText.Text = "🚀 Performance: Fastest response times, higher CPU usage. Best for desktop PCs with good specs.";
                    break;
            }
        }
        
        private void LoadHardwareSettings()
        {
            try
            {
                if (File.Exists(HardwareSettingsPath))
                {
                    var json = File.ReadAllText(HardwareSettingsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    // Load device ID (new format)
                    if (root.TryGetProperty("micDeviceId", out var micId))
                        _selectedMicDeviceId = micId.GetString();

                    if (root.TryGetProperty("micDeviceName", out var micName))
                        _selectedMicDeviceName = micName.GetString() ?? "";
                    
                    // Legacy: load device index
                    if (root.TryGetProperty("micDevice", out var mic))
                        _selectedMicDevice = mic.GetInt32();
                        
                    if (root.TryGetProperty("micSensitivity", out var sens))
                    {
                        _micSensitivity = sens.GetInt32();
                        MicSensitivitySlider.Value = _micSensitivity;
                        MicSensitivityValue.Text = _micSensitivity.ToString();
                    }
                    if (root.TryGetProperty("qualityMode", out var quality))
                    {
                        _qualityMode = quality.GetString() ?? "balanced";
                        SelectQualityMode(_qualityMode);
                    }
                    else
                    {
                        // Default to balanced if not in settings
                        SelectQualityMode("balanced");
                    }
                }
                else
                {
                    // No settings file - set default quality mode
                    SelectQualityMode("balanced");
                }
            }
            catch { }
        }
        
        private void SaveHardwareSettings()
        {
            try
            {
                var settings = new Dictionary<string, object>
                {
                    ["micDeviceId"] = _selectedMicDeviceId ?? "",
                    ["micDeviceName"] = _selectedMicDeviceName,
                    ["micDevice"] = _selectedMicDevice, // Keep for backward compatibility
                    ["micSensitivity"] = _micSensitivity,
                    ["qualityMode"] = _qualityMode
                };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                try
                {
                    SafeFile.WriteAllTextAtomic(HardwareSettingsPath, json);
                }
                catch (UnauthorizedAccessException)
                {
                    try
                    {
                        var fallback = Path.Combine(AtlasPaths.LocalDir, "hardware_settings.json");
                        SafeFile.WriteAllTextAtomic(fallback, json);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    Core.PreferencesStore.Instance.Update(p =>
                    {
                        p.MicrophoneDeviceId = _selectedMicDeviceId ?? "";
                        p.MicrophoneDevice = _selectedMicDeviceName ?? "";
                    });
                }
                catch
                {
                }
            }
            catch { }
        }
        
        #endregion

        #region AI Settings
        
        private async Task LoadAIModelsAsync()
        {
            try
            {
                var provider = AIManager.GetActiveProviderInstance();
                var models = new List<AIModel>();
                if (provider != null)
                    models = await provider.GetModelsAsync();
                
                AIModelComboBox.Items.Clear();
                foreach (var model in models)
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{model.DisplayName} - {model.Description}",
                        Tag = model.Id
                    };
                    AIModelComboBox.Items.Add(item);
                }
                
                var selectedModel = AIManager.GetSelectedModel();
                for (int i = 0; i < AIModelComboBox.Items.Count; i++)
                {
                    if (AIModelComboBox.Items[i] is ComboBoxItem item && 
                        item.Tag as string == selectedModel)
                    {
                        AIModelComboBox.SelectedIndex = i;
                        break;
                    }
                }
                
                if (AIModelComboBox.SelectedIndex < 0 && AIModelComboBox.Items.Count > 0)
                    AIModelComboBox.SelectedIndex = 0;
            }
            catch { }
        }

        private void LoadAIProviders()
        {
            AIProviderComboBox.Items.Clear();
            
            var providers = AIManager.GetAllProviders();
            foreach (var provider in providers)
            {
                var item = new ComboBoxItem
                {
                    Content = provider.DisplayName,
                    Tag = provider.ProviderType
                };
                AIProviderComboBox.Items.Add(item);
            }
            
            var activeProvider = AIManager.GetActiveProvider();
            for (int i = 0; i < AIProviderComboBox.Items.Count; i++)
            {
                if (AIProviderComboBox.Items[i] is ComboBoxItem item && 
                    item.Tag is AIProviderType type && type == activeProvider)
                {
                    AIProviderComboBox.SelectedIndex = i;
                    break;
                }
            }
            
            if (AIProviderComboBox.SelectedIndex < 0 && AIProviderComboBox.Items.Count > 0)
                AIProviderComboBox.SelectedIndex = 0;
        }

        private async void AIProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AIProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag is AIProviderType type)
            {
                await AIManager.SetActiveProviderAsync(type);
                await LoadAIModelsAsync();
                LoadAIKeyForProvider(type);
            }
        }

        private void AIModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AIModelComboBox.SelectedItem is ComboBoxItem item && item.Tag is string modelId)
            {
                AIManager.SetSelectedModel(modelId);
            }
        }

        private Dictionary<string, string> LoadAiKeys()
        {
            try
            {
                if (File.Exists(AiKeysPath))
                {
                    var json = File.ReadAllText(AiKeysPath);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private void LoadAIKeyForProvider(AIProviderType type)
        {
            AIKeyLabel.Text = type switch
            {
                AIProviderType.Claude => "Claude API Key",
                AIProviderType.OpenAI => "OpenAI API Key",
                AIProviderType.Gemini => "Gemini API Key",
                _ => "API Key"
            };
        }

        private async void TestAIConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AIProviderComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not AIProviderType type)
                    return;

                var apiKey = AIProviderKeyBox.Password?.Trim() ?? "";
                
                // Sanitize key aggressively
                apiKey = apiKey.Replace("\r", "").Replace("\n", "").Replace("\"", "").Replace(" ", "");

                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("Enter an API key first.", "AI Provider", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await AIManager.SetActiveProviderAsync(type);
                await AIManager.ConfigureProviderAsync(type, new Dictionary<string, string> { ["ApiKey"] = apiKey });
                
                // Manual test to get detailed error message
                var provider = AIManager.GetProvider(type);
                if (provider != null)
                {
                    // Use a simple test message
                    var testMessages = new List<object> { new { role = "user", content = "Hello" } };
                    
                    // Use default model for test to ensure basic connectivity
                    var response = await provider.SendMessageAsync(testMessages, "", 10);
                    
                    if (response.Success)
                    {
                        MessageBox.Show("Connection successful.", "AI Provider", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        // Show the actual error message from the provider
                        MessageBox.Show($"Connection failed.\n\n{response.Error}", "AI Provider", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Failed to initialize provider.", "AI Provider", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Test failed: {ex.Message}", "AI Provider", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        #endregion

        #region Voice Settings

        private void LoadVoiceProviders()
        {
            Debug.WriteLine("[Settings] LoadVoiceProviders called");
            VoiceProviderCombo.Items.Clear();
            VoiceProviderCombo.Items.Add(new ComboBoxItem
            {
                Content = "Windows SAPI (Local)",
                Tag = VoiceProviderType.WindowsSAPI
            });
            VoiceProviderCombo.Items.Add(new ComboBoxItem
            {
                Content = "Edge TTS (Local)",
                Tag = VoiceProviderType.EdgeTTS
            });
            VoiceProviderCombo.Items.Add(new ComboBoxItem
            {
                Content = "OpenAI TTS (Cloud)",
                Tag = VoiceProviderType.OpenAI
            });
            VoiceProviderCombo.Items.Add(new ComboBoxItem
            {
                Content = "☁️ ElevenLabs (Premium)",
                Tag = VoiceProviderType.ElevenLabs
            });
            Debug.WriteLine($"[Settings] LoadVoiceProviders: Added {VoiceProviderCombo.Items.Count} items, SelectedIndex={VoiceProviderCombo.SelectedIndex}");
            // Don't set SelectedIndex here - let LoadSettings() do it
        }

        private void VoiceProvider_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceProviderCombo.SelectedItem is ComboBoxItem item && item.Tag is VoiceProviderType type)
            {
                UpdateProviderInfo(type);
            }
        }

        private void UpdateProviderInfo(VoiceProviderType type)
        {
            // Key entry boxes are locked down — never shown to users.
            ElevenLabsKeyLabel.Visibility = Visibility.Collapsed;
            ElevenLabsKeyBox.Visibility = Visibility.Collapsed;
            OpenAIKeyLabel.Visibility = Visibility.Collapsed;
            OpenAIKeyBox.Visibility = Visibility.Collapsed;

            switch (type)
            {
                case VoiceProviderType.ElevenLabs:
                    ProviderInfoText.Text = "☁️ ElevenLabs: Premium expressive voices.";
                    ProviderInfoText.Visibility = Visibility.Visible;
                    CloudIndicator.Visibility = Visibility.Visible;
                    break;
                case VoiceProviderType.OpenAI:
                    ProviderInfoText.Text = "OpenAI TTS is active.";
                    ProviderInfoText.Visibility = Visibility.Visible;
                    CloudIndicator.Visibility = Visibility.Visible;
                    break;
                default:
                    ProviderInfoText.Text = "Local voice output is active.";
                    ProviderInfoText.Visibility = Visibility.Visible;
                    CloudIndicator.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        #region Voice Selection

        private bool _isLoadingVoiceSelection = false;
        private const string VoicePreviewText = "Diagnostics complete. Standing by.";

        /// <summary>
        /// Initialize voice selection dropdowns with available voices.
        /// </summary>
        private async Task LoadVoiceSelectionAsync()
        {
            _isLoadingVoiceSelection = true;
            try
            {
                // Always bust the cache so we get the user's actual ElevenLabs library
                await VoiceCatalogService.Instance.RefreshAsync();
                var voices = await VoiceCatalogService.Instance.GetVoicesAsync();
                var prefs = VoicePreferences.Current;

                // Populate Global Voice dropdown
                GlobalVoiceCombo.Items.Clear();
                GlobalVoiceCombo.Items.Add(new ComboBoxItem { Content = "Default (use personality voices)", Tag = "" });
                foreach (var voice in voices)
                {
                    var displayText = voice.DisplayName;
                    if (voice.IsDefault) displayText += " ★";
                    GlobalVoiceCombo.Items.Add(new ComboBoxItem { Content = displayText, Tag = voice.VoiceId });
                }
                SelectVoiceInCombo(GlobalVoiceCombo, prefs.GlobalVoiceId);

                // Populate System Voice dropdown
                SystemVoiceCombo.Items.Clear();
                SystemVoiceCombo.Items.Add(new ComboBoxItem { Content = "Default (Atlas AI)", Tag = "" });
                foreach (var voice in voices)
                {
                    SystemVoiceCombo.Items.Add(new ComboBoxItem { Content = voice.DisplayName, Tag = voice.VoiceId });
                }
                SelectVoiceInCombo(SystemVoiceCombo, prefs.SystemVoiceId);

                // Populate per-personality voice dropdowns
                PopulatePersonalityVoices(voices, prefs);

                Debug.WriteLine($"[Settings] Voice selection loaded with {voices.Count} voices");

                // Update voice preview status
                UpdateVoicePreviewStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error loading voice selection: {ex.Message}");
            }
            finally
            {
                _isLoadingVoiceSelection = false;
            }
        }

        /// <summary>
        /// Update the voice preview status TextBlock with device/engine availability.
        /// </summary>
        private void UpdateVoicePreviewStatus()
        {
            try
            {
                var statusMessage = Services.VoicePreviewService.Instance.GetStatusMessage();

                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    VoicePreviewStatusText.Text = statusMessage;
                    VoicePreviewStatusText.Visibility = Visibility.Visible;
                    Debug.WriteLine($"[Settings] Voice preview status: {statusMessage}");
                }
                else
                {
                    VoicePreviewStatusText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating voice preview status: {ex.Message}");
            }
        }

        private async void RefreshVoices_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;
            try
            {
                await VoiceCatalogService.Instance.RefreshAsync();
                await LoadVoiceSelectionAsync();
            }
            finally
            {
                if (sender is Button b) b.IsEnabled = true;
            }
        }

        /// <summary>
        /// Populate the per-personality voice selection panel.
        /// </summary>
        private void PopulatePersonalityVoices(IReadOnlyList<CatalogVoice> voices, VoicePreferences prefs)
        {
            PersonalityVoicesPanel.Children.Clear();

            // Get personalities from data-driven registry
            var settings = AtlasAI.Settings.SettingsStore.Current;
            bool devModeEnabled = settings?.DebugLogsEnabled ?? false;
            var personalities = AtlasAI.Personality.PersonalityRegistry.GetAvailable(includeHidden: devModeEnabled);

            foreach (var personalityDef in personalities)
            {
                var personalityKey = personalityDef.Id;
                var recommendedVoice = voices.FirstOrDefault(v => v.RecommendedFor == personalityKey);

                var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = $"{personalityDef.Icon} {personalityDef.DisplayName}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8")),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);

                var combo = new ComboBox
                {
                    Tag = personalityKey,
                    Style = (Style)FindResource("AtlasComboBox"),
                    Margin = new Thickness(0, 0, 8, 0)
                };

                var defaultText = $"Default ({VoiceProfile.AtlasDefault.DisplayName})";
                if (recommendedVoice != null) defaultText += " ★";
                combo.Items.Add(new ComboBoxItem { Content = defaultText, Tag = "" });

                foreach (var voice in voices)
                {
                    var displayText = voice.DisplayName;
                    if (voice.RecommendedFor == personalityKey) displayText += " ★";
                    combo.Items.Add(new ComboBoxItem { Content = displayText, Tag = voice.VoiceId });
                }

                var currentOverride = prefs.GetPersonalityVoice(personalityKey);
                SelectVoiceInCombo(combo, currentOverride);

                combo.SelectionChanged += PersonalityVoice_Changed;
                Grid.SetColumn(combo, 1);
                grid.Children.Add(combo);

                var previewBtn = new Button
                {
                    Content = "▶",
                    Width = 32,
                    Height = 32,
                    Tag = personalityKey,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8b5cf620")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8b5cf6")),
                    BorderThickness = new Thickness(0),
                    ToolTip = "Preview voice"
                };
                previewBtn.Click += PreviewPersonalityVoice_Click;
                Grid.SetColumn(previewBtn, 2);
                grid.Children.Add(previewBtn);

                PersonalityVoicesPanel.Children.Add(grid);
            }
        }

        /// <summary>
        /// Select a voice in a combo box by voice ID.
        /// </summary>
        private void SelectVoiceInCombo(ComboBox combo, string? voiceId)
        {
            if (string.IsNullOrEmpty(voiceId))
            {
                combo.SelectedIndex = 0; // Default
                return;
            }

            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == voiceId)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.SelectedIndex = 0; // Fallback to default
        }

        /// <summary>
        /// Get the selected voice ID from a combo box.
        /// </summary>
        private string? GetSelectedVoiceId(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                return string.IsNullOrEmpty(tag) ? null : tag;
            }
            return null;
        }

        private void GlobalVoice_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingVoiceSelection || _isLoadingSettings) return;

            var voiceId = GetSelectedVoiceId(GlobalVoiceCombo);
            VoicePreferences.Current.SetGlobalVoice(voiceId);
            Debug.WriteLine($"[Settings] Global voice changed to: {voiceId ?? "(default)"}");
        }

        private void SystemVoice_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingVoiceSelection || _isLoadingSettings) return;

            var voiceId = GetSelectedVoiceId(SystemVoiceCombo);
            VoicePreferences.Current.SetSystemVoice(voiceId);
            Debug.WriteLine($"[Settings] System voice changed to: {voiceId ?? "(default)"}");
        }

        private void PersonalityVoice_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingVoiceSelection || _isLoadingSettings) return;

            if (sender is ComboBox combo && combo.Tag is string personalityKey)
            {
                var voiceId = GetSelectedVoiceId(combo);
                VoicePreferences.Current.SetPersonalityVoice(personalityKey, voiceId);
                Debug.WriteLine($"[Settings] {personalityKey} voice changed to: {voiceId ?? "(default)"}");
            }
        }

        private async void PreviewGlobalVoice_Click(object sender, RoutedEventArgs e)
        {
            var voiceId = GetSelectedVoiceId(GlobalVoiceCombo);
            await PreviewVoiceAsync(voiceId ?? VoiceProfile.AtlasDefault.VoiceId);
        }

        private async void PreviewSystemVoice_Click(object sender, RoutedEventArgs e)
        {
            var voiceId = GetSelectedVoiceId(SystemVoiceCombo);
            await PreviewVoiceAsync(voiceId ?? VoiceProfile.SystemVoice.VoiceId);
        }

        private async void PreviewPersonalityVoice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string personalityKey)
            {
                foreach (var child in PersonalityVoicesPanel.Children)
                {
                    if (child is Grid grid)
                    {
                        foreach (var gridChild in grid.Children)
                        {
                            if (gridChild is ComboBox combo && combo.Tag is string p && p == personalityKey)
                            {
                                var voiceId = GetSelectedVoiceId(combo);
                                if (string.IsNullOrEmpty(voiceId))
                                    voiceId = VoiceProfile.AtlasDefault.VoiceId;
                                await PreviewVoiceAsync(voiceId);
                                return;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Preview a voice by speaking the test phrase using the app-level VoicePreviewService.
        /// Works independently of ChatWindow and shows errors via Atlas-themed dialogs.
        /// </summary>
        private async Task PreviewVoiceAsync(string voiceId)
        {
            try
            {
                Debug.WriteLine($"[Settings] Starting voice preview: {voiceId}");

                // Get selected provider from combo box
                AtlasAI.Voice.VoiceProviderType provider = AtlasAI.Voice.VoiceProviderType.WindowsSAPI;
                if (VoiceProviderCombo.SelectedItem is ComboBoxItem item && item.Tag is AtlasAI.Voice.VoiceProviderType provType)
                {
                    provider = provType;
                }

                // Use VoicePreviewService singleton
                var result = await Services.VoicePreviewService.Instance.PreviewVoiceAsync(
                    voiceId,
                    VoicePreviewText,
                    provider);

                if (!result.Success)
                {
                    Debug.WriteLine($"[Settings] Voice preview failed: {result.Error}");

                    // Show error via Atlas-themed dialog (NOT MessageBox)
                    await App.DialogService.ShowErrorAsync(
                        "Voice Preview Unavailable",
                        $"Could not preview voice: {result.Error}\n\nPlease check:\n• Audio output device is connected\n• Voice provider API key is configured (for cloud voices)\n• Selected voice is available");
                }
                else
                {
                    Debug.WriteLine($"[Settings] Voice preview started: {voiceId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Voice preview exception: {ex.Message}");

                // Show exception via Atlas dialog
                await App.DialogService.ShowErrorAsync(
                    "Preview Error",
                    $"An error occurred during voice preview:\n\n{ex.Message}");
            }
        }

        #endregion
        
        #endregion

        #region Load/Save Settings

        private async Task LoadSettings()
        {
            try
            {
                var voiceKeysReadPath = AtlasPaths.VoiceKeysReadCandidates().FirstOrDefault(File.Exists) ?? VoiceKeysPath;
                Debug.WriteLine($"[Settings] LoadSettings called, VoiceKeysPath={voiceKeysReadPath}");
                if (File.Exists(voiceKeysReadPath))
                {
                    var json = await File.ReadAllTextAsync(voiceKeysReadPath);
                    Debug.WriteLine($"[Settings] Loaded voice_keys.json: {json}");
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

					var loadedOpenAi = "";
					var loadedEleven = "";
					if (root.TryGetProperty("openai", out var openai))
						loadedOpenAi = SecretProtector.UnprotectIfNeeded(openai.GetString() ?? "");
					if (root.TryGetProperty("elevenlabs", out var eleven))
						loadedEleven = SecretProtector.UnprotectIfNeeded(eleven.GetString() ?? "");

					// Only populate empty fields during load; never clear/overwrite non-empty fields.
					if (string.IsNullOrWhiteSpace(OpenAIKeyBox.Password) && !string.IsNullOrWhiteSpace(loadedOpenAi))
						OpenAIKeyBox.Password = loadedOpenAi;
					if (string.IsNullOrWhiteSpace(ElevenLabsKeyBox.Password) && !string.IsNullOrWhiteSpace(loadedEleven))
						ElevenLabsKeyBox.Password = loadedEleven;
                    ElevenLabsKeyBox.IsEnabled = true;
                    if (root.TryGetProperty("provider", out var prov))
                    {
                        var provString = prov.GetString();
                        Debug.WriteLine($"[Settings] Found provider in JSON: '{provString}'");
                        if (Enum.TryParse<VoiceProviderType>(provString, out var provType))
                        {
                            Debug.WriteLine($"[Settings] Parsed provider type: {provType}");
                            for (int i = 0; i < VoiceProviderCombo.Items.Count; i++)
                            {
                                if (VoiceProviderCombo.Items[i] is ComboBoxItem item && 
                                    item.Tag is VoiceProviderType t && t == provType)
                                {
                                    Debug.WriteLine($"[Settings] Setting VoiceProviderCombo.SelectedIndex = {i}");
                                    VoiceProviderCombo.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[Settings] Failed to parse provider type from '{provString}'");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[Settings] No 'provider' property found in JSON");
                    }
                }
                else
                {
                    Debug.WriteLine($"[Settings] voice_keys.json does not exist");
                    ElevenLabsKeyBox.IsEnabled = true;
                }
                
                // Fallback: if no provider was selected, default to first item
                if (VoiceProviderCombo.SelectedIndex < 0 && VoiceProviderCombo.Items.Count > 0)
                {
                    Debug.WriteLine($"[Settings] No provider selected, defaulting to index 0");
                    VoiceProviderCombo.SelectedIndex = 0;
                }
                
                Debug.WriteLine($"[Settings] After LoadSettings: VoiceProviderCombo.SelectedIndex={VoiceProviderCombo.SelectedIndex}");
                
                // Load honorific from user profile
                await LoadHonorificFromProfileAsync();
                
                // Load integration API keys (only expose cloud provider token)
                var integrationKeys = GetIntegrationApiKeys();
                
                // Spotify, Canva, TMDB, MDBList, IGDB, MusicBrainz removed from UI as requested.
                // Logic to clear/populate them removed.

                LoadCloudProviderSettings(integrationKeys);
                LoadAddonServersSettings();
                LoadTmdbSettings(integrationKeys);
                LoadRpdbSettings(integrationKeys);
                LoadOmdbSettings(integrationKeys);
                
                using var key = Registry.CurrentUser.OpenSubKey(StartupKey, false);
                AutoStartCheckbox.IsChecked = key?.GetValue(AppName) != null;
                
                // Load voice input settings from preferences
                LoadVoiceInputSettings();

                LoadAIKeyForProvider(AIManager.GetActiveProvider());
            }
            catch (Exception ex)
            {
				Debug.WriteLine(ex.ToString());
				ShowSettingsLoadWarningOnce();
            }
        }

        private void LoadAddonServersSettings()
        {
            try
            {
                if (AddonServersList == null)
                    return;

				var loaded = LoadAddonServersFromStore();
				if (_isLoadingSettings && AddonServersList.Items.Count > 0 && loaded.Count == 0)
					return;

                AddonServersList.Items.Clear();
                foreach (var s in loaded)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        AddonServersList.Items.Add(s);
                }

				var selectedRaw = (IntegrationKeyStore.GetDecrypted(AddonServerSelectedKey) ?? "").Trim();
				if (string.Equals(selectedRaw, AllServersSentinel, StringComparison.OrdinalIgnoreCase))
				{
					AddonServersList.SelectedItem = null;
					if (AddonServerActiveText != null)
						AddonServerActiveText.Text = "Active: All Servers";
					return;
				}

				var selected = NormalizeAddonServerUrl(selectedRaw);
				if (!string.IsNullOrWhiteSpace(selected))
                {
                    foreach (var item in AddonServersList.Items)
                    {
                        if (string.Equals(item?.ToString() ?? "", selected, StringComparison.OrdinalIgnoreCase))
                        {
                            AddonServersList.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (AddonServerActiveText != null)
                {
                    var active = (AddonServersList.SelectedItem?.ToString() ?? "").Trim();
                    AddonServerActiveText.Text = string.IsNullOrWhiteSpace(active) ? "Active: (none)" : $"Active: {active}";
                }
            }
            catch
            {
            }
        }

        private void LoadRpdbSettings(Dictionary<string, string> integrationKeys)
        {
            try
            {
                if (RpdbKeyBox == null || RpdbStatusText == null)
                    return;

                var loaded = "";
                try
                {
                    if (integrationKeys != null && integrationKeys.TryGetValue("rpdb", out var v))
                        loaded = (v ?? "").Trim();
                }
                catch
                {
                    loaded = "";
                }

                if (string.IsNullOrWhiteSpace(RpdbKeyBox.Password) && !string.IsNullOrWhiteSpace(loaded))
                    RpdbKeyBox.Password = loaded;

                var current = (RpdbKeyBox.Password ?? "").Trim();
                if (string.IsNullOrWhiteSpace(current))
                {
                    RpdbStatusText.Text = "Not configured";
                }
                else if (current.StartsWith("t", StringComparison.OrdinalIgnoreCase) && current.Contains("-"))
                {
                    RpdbStatusText.Text = "Saved (poster token)";
                }
                else
                {
                    RpdbStatusText.Text = "Configured";
                }
            }
            catch
            {
            }
        }

        private void LoadTmdbSettings(Dictionary<string, string> integrationKeys)
        {
            try
            {
                if (TmdbKeyBox == null || TmdbStatusText == null)
                    return;

                var loaded = "";
                try
                {
                    if (integrationKeys != null && integrationKeys.TryGetValue("tmdb", out var v))
                        loaded = (v ?? "").Trim();
                }
                catch
                {
                    loaded = "";
                }

                if (string.IsNullOrWhiteSpace(TmdbKeyBox.Password) && !string.IsNullOrWhiteSpace(loaded))
                    TmdbKeyBox.Password = loaded;

                TmdbStatusText.Text = string.IsNullOrWhiteSpace((TmdbKeyBox.Password ?? "").Trim()) ? "Not configured" : "Configured";
            }
            catch
            {
            }
        }

        private void TmdbSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TmdbKeyBox == null || TmdbStatusText == null)
                    return;

                var key = (TmdbKeyBox.Password ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    TmdbStatusText.Text = "Missing key";
                    return;
                }

                SetIntegrationApiKey("tmdb", key);
                TmdbStatusText.Text = "Saved";
            }
            catch
            {
            }
        }

        private void TmdbClear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TmdbKeyBox == null || TmdbStatusText == null)
                    return;

                try { IntegrationKeyStore.Delete("tmdb"); } catch { }
                TmdbKeyBox.Password = "";
                TmdbStatusText.Text = "Cleared";
            }
            catch
            {
            }
        }

        private void RpdbSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (RpdbKeyBox == null || RpdbStatusText == null)
                    return;

                var key = (RpdbKeyBox.Password ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    RpdbStatusText.Text = "Missing key";
                    return;
                }

                SetIntegrationApiKey("rpdb", key);
                if (key.StartsWith("t", StringComparison.OrdinalIgnoreCase) && key.Contains("-"))
                    RpdbStatusText.Text = "Saved (poster token)";
                else
                    RpdbStatusText.Text = "Saved";
            }
            catch
            {
            }
        }

        private void RpdbClear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (RpdbKeyBox == null || RpdbStatusText == null)
                    return;

                try { IntegrationKeyStore.Delete("rpdb"); } catch { }
                RpdbKeyBox.Password = "";
                RpdbStatusText.Text = "Cleared";
            }
            catch
            {
            }
        }

        private void LoadOmdbSettings(Dictionary<string, string> integrationKeys)
        {
            try
            {
                if (OmdbKeyBox == null || OmdbStatusText == null)
                    return;

                var loaded = "";
                try
                {
                    if (integrationKeys != null && integrationKeys.TryGetValue("omdb", out var v))
                        loaded = (v ?? "").Trim();
                }
                catch
                {
                    loaded = "";
                }

                if (string.IsNullOrWhiteSpace(OmdbKeyBox.Password) && !string.IsNullOrWhiteSpace(loaded))
                    OmdbKeyBox.Password = loaded;

                OmdbStatusText.Text = string.IsNullOrWhiteSpace((OmdbKeyBox.Password ?? "").Trim()) ? "Not configured" : "Configured";
            }
            catch
            {
            }
        }

        private void OmdbSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OmdbKeyBox == null || OmdbStatusText == null)
                    return;

                var key = (OmdbKeyBox.Password ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    OmdbStatusText.Text = "Missing key";
                    return;
                }

                SetIntegrationApiKey("omdb", key);
                OmdbStatusText.Text = "Saved";
            }
            catch
            {
            }
        }

        private void OmdbClear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OmdbKeyBox == null || OmdbStatusText == null)
                    return;

                try { IntegrationKeyStore.Delete("omdb"); } catch { }
                OmdbKeyBox.Password = "";
                OmdbStatusText.Text = "Cleared";
            }
            catch
            {
            }
        }

		private void UseAllServers_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				if (AddonServersList == null)
					return;

				var current = AddonServersList.Items.Cast<object>()
					.Select(x => x?.ToString() ?? "")
					.Where(x => !string.IsNullOrWhiteSpace(x))
					.ToList();
				SaveAddonServersToStore(current);

				IntegrationKeyStore.SetProtected(AddonServerSelectedKey, AllServersSentinel);

				if (AddonServerActiveText != null)
					AddonServerActiveText.Text = "Active: All Servers";
				Debug.WriteLine("[ServerAddons] Active set to ALL servers");
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
			}
		}

        private List<string> LoadAddonServersFromStore()
        {
            try
            {
                var raw = IntegrationKeyStore.GetDecrypted(AddonServersKey);
                if (string.IsNullOrWhiteSpace(raw))
                    return new List<string>();

                var list = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
                return list
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeAddonServerUrl)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private void SaveAddonServersToStore(List<string> servers)
        {
            try
            {
                var normalized = (servers ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeAddonServerUrl)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

				IntegrationKeyStore.SetProtected(AddonServersKey, JsonSerializer.Serialize(normalized));
            }
            catch
            {
            }
        }

        private static string NormalizeAddonServerUrl(string input)
        {
            var s = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "";

            s = s.Trim();
            while (s.Length >= 2)
            {
                var first = s[0];
                var last = s[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '`' && last == '`') || (first == '<' && last == '>'))
                {
                    s = s.Substring(1, s.Length - 2).Trim();
                    continue;
                }
                break;
            }

            s = s.TrimEnd('/');

            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
            {
                if (!Uri.TryCreate("http://" + s, UriKind.Absolute, out uri))
                    return "";
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return "";

            var builder = new UriBuilder(uri) { Fragment = "" };
            if (builder.Path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                builder.Path = builder.Path.Substring(0, builder.Path.Length - "/manifest.json".Length).TrimEnd('/');

            return builder.Uri.ToString().TrimEnd('/');
        }

        private void AddonServerAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AddonServersList == null || AddonServerUrlBox == null)
                    return;

                var normalized = NormalizeAddonServerUrl(AddonServerUrlBox.Text ?? "");
                if (string.IsNullOrWhiteSpace(normalized))
                    return;

                var current = AddonServersList.Items.Cast<object>()
                    .Select(x => x?.ToString() ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                if (current.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    AddonServerUrlBox.Text = "";
                    return;
                }

                current.Add(normalized);
                SaveAddonServersToStore(current);
				IntegrationKeyStore.SetProtected(AddonServerSelectedKey, normalized);
                LoadAddonServersSettings();
                AddonServerUrlBox.Text = "";
            }
            catch
            {
            }
        }

        private void AddonServerRemove_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AddonServersList == null)
                    return;

                var selected = AddonServersList.SelectedItem?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(selected))
                    return;

                var current = AddonServersList.Items.Cast<object>()
                    .Select(x => x?.ToString() ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                current.RemoveAll(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
                SaveAddonServersToStore(current);

                // Also remove from addon_servers.json so RefreshAddonServers won't resurrect it.
                try
                {
                    var cfgPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI", "addon_servers.json");
                    if (System.IO.File.Exists(cfgPath))
                    {
                        var json = System.IO.File.ReadAllText(cfgPath);
                        var items = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<AtlasAI.Models.AddonServerItem>>(json)
                            ?? new System.Collections.Generic.List<AtlasAI.Models.AddonServerItem>();
                        var before = items.Count;
                        items.RemoveAll(i =>
                        {
                            var u = NormalizeAddonServerUrl(i?.Url ?? "");
                            return string.Equals(u, NormalizeAddonServerUrl(selected), StringComparison.OrdinalIgnoreCase);
                        });
                        if (items.Count != before)
                            System.IO.File.WriteAllText(cfgPath, System.Text.Json.JsonSerializer.Serialize(items,
                                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                catch { }

                var active = NormalizeAddonServerUrl(IntegrationKeyStore.GetDecrypted(AddonServerSelectedKey) ?? "");
                if (!string.IsNullOrWhiteSpace(active) && string.Equals(active, selected, StringComparison.OrdinalIgnoreCase))
                {
                    var next = current.FirstOrDefault() ?? "";
					IntegrationKeyStore.SetProtected(AddonServerSelectedKey, next);
                }
                LoadAddonServersSettings();
            }
            catch
            {
            }
        }

        private void AddonServerSetActive_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AddonServersList == null)
                    return;

                var selected = NormalizeAddonServerUrl(AddonServersList.SelectedItem?.ToString() ?? "");
                if (string.IsNullOrWhiteSpace(selected))
                    return;

				IntegrationKeyStore.SetProtected(AddonServerSelectedKey, selected);
                LoadAddonServersSettings();
            }
            catch
            {
            }
        }

        private void AddonServersList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (AddonServersList == null)
                    return;

                var dep = e.OriginalSource as DependencyObject;
                var container = dep != null ? ItemsControl.ContainerFromElement(AddonServersList, dep) as ListBoxItem : null;
                if (container == null)
                    return;

                var value = container.Content?.ToString() ?? "";
                value = NormalizeAddonServerUrl(value);
                if (string.IsNullOrWhiteSpace(value))
                    return;

                AddonServersList.SelectedItem = container.Content;
                Clipboard.SetText(value);
                e.Handled = true;
            }
            catch
            {
            }
        }
        
        private void LoadVoiceInputSettings()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                
                // Load voice settings from preferences
                EnableWakeWordCheckBox.IsChecked = prefs.EnableWakeWord;
                EnableAlwaysListeningCheckBox.IsChecked = prefs.EnableAlwaysListening;
                WakeWordSensitivitySlider.Value = prefs.WakeWordSensitivity;
                FollowUpListeningDurationSlider.Value = prefs.FollowUpListeningDuration;
                EnableWakeWordAudioCueCheckBox.IsChecked = prefs.EnableWakeWordAudioCue;
                ShowListeningIndicatorCheckBox.IsChecked = prefs.ShowListeningIndicator;
                
                // Update sensitivity display
                WakeWordSensitivityValue.Text = $"{(int)(prefs.WakeWordSensitivity * 100)}%";
                FollowUpListeningDurationValue.Text = $"{(int)prefs.FollowUpListeningDuration}s";
                
                // Update voice status
                UpdateVoiceStatus();
                
                Debug.WriteLine($"[Settings] Loaded voice input settings - WakeWord: {prefs.EnableWakeWord}, AlwaysListening: {prefs.EnableAlwaysListening}, Sensitivity: {prefs.WakeWordSensitivity}, FollowUpDuration: {prefs.FollowUpListeningDuration}s");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error loading voice input settings: {ex.Message}");
            }
        }
        
        private void UpdateVoiceStatus()
        {
            try
            {
                var voiceState = VoiceStateManager.Instance;
                var prefs = PreferencesStore.Instance.Current;
                
                if (!prefs.EnableWakeWord)
                {
                    VoiceStatusText.Text = "🎙️ Voice System: Disabled";
                    VoiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8b));
                }
                else
                {
                    switch (voiceState.CurrentState)
                    {
                        case VoiceSystemState.PassiveListening:
                            VoiceStatusText.Text = "🎙️ Voice System: Listening";
                            VoiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xd3, 0xee));
                            break;
                        case VoiceSystemState.ActiveListening:
                            VoiceStatusText.Text = "🎙️ Voice System: Capturing command";
                            VoiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
                            break;
                        case VoiceSystemState.Processing:
                            VoiceStatusText.Text = "🎙️ Voice System: Processing";
                            VoiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b));
                            break;
                        case VoiceSystemState.Speaking:
                            VoiceStatusText.Text = "🎙️ Voice System: Speaking";
                            VoiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x5c, 0xf6));
                            break;
                        case VoiceSystemState.Suspended:
                            VoiceStatusText.Text = "🎙️ Voice System: Suspended";
                            VoiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
                            break;
                        default:
                            VoiceStatusText.Text = "🎙️ Voice System: Ready";
                            VoiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating voice status: {ex.Message}");
            }
        }
        
        private async Task LoadHonorificFromProfileAsync()
        {
            try
            {
                if (File.Exists(ProfilePath))
                {
                    var json = await File.ReadAllTextAsync(ProfilePath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("Honorific", out var honorificProp))
                    {
                        var honorificValue = honorificProp.GetInt32();
                        var honorific = (UserHonorific)honorificValue;
                        
                        // Find and select the matching item in the combo box
                        for (int i = 0; i < HonorificComboBox.Items.Count; i++)
                        {
                            if (HonorificComboBox.Items[i] is ComboBoxItem item && 
                                item.Tag?.ToString() == honorific.ToString())
                            {
                                HonorificComboBox.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Default to Sir if not set
                        HonorificComboBox.SelectedIndex = 0;
                    }
                }
                else
                {
                    // Default to Sir if no profile exists
                    HonorificComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error loading honorific: {ex.Message}");
                HonorificComboBox.SelectedIndex = 0;
            }
        }
        
        private void HonorificComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Just track the selection - actual save happens in Save_Click
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(SettingsDir)) 
                    Directory.CreateDirectory(SettingsDir);
                
                var selectedProvider = VoiceProviderType.WindowsSAPI;
                if (VoiceProviderCombo.SelectedItem is ComboBoxItem providerItem && providerItem.Tag is VoiceProviderType providerType)
                    selectedProvider = providerType;

                var elevenLabsKey = Core.ApiKeySanitizer.SanitizeForHttpHeader(ElevenLabsKeyBox.Password);
                ElevenLabsKeyBox.Password = elevenLabsKey;

                var protectedOpenAiKey = "";
                try
                {
                    var existingPath = AtlasPaths.VoiceKeysReadCandidates().FirstOrDefault(File.Exists);
                    if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
                    {
                        var existingJson = File.ReadAllText(existingPath);
                        using var existingDoc = JsonDocument.Parse(existingJson);
                        if (existingDoc.RootElement.TryGetProperty("openai", out var openaiProp))
                            protectedOpenAiKey = openaiProp.GetString() ?? "";
                    }
                }
                catch
                {
                }

                var openAiKeyFromUi = (OpenAIKeyBox.Password ?? "").Trim();
                if (string.IsNullOrWhiteSpace(protectedOpenAiKey) && !string.IsNullOrWhiteSpace(openAiKeyFromUi))
                    protectedOpenAiKey = SecretProtector.Protect(openAiKeyFromUi);

                var voiceSettings = new Dictionary<string, string>
                {
                    ["elevenlabs"] = SecretProtector.Protect(elevenLabsKey),
                    ["provider"] = selectedProvider.ToString()
                };
                if (!string.IsNullOrWhiteSpace(protectedOpenAiKey))
                    voiceSettings["openai"] = protectedOpenAiKey;
                var json = JsonSerializer.Serialize(voiceSettings, new JsonSerializerOptions { WriteIndented = true });
                string? voiceKeysWriteError = null;
                try
                {
                    SafeFile.WriteAllTextAtomic(VoiceKeysPath, json);
                }
                catch (UnauthorizedAccessException uaex)
                {
                    voiceKeysWriteError = uaex.Message;
                    try
                    {
                        var fallbackPath = AtlasPaths.LocalVoiceKeysPath;
                        SafeFile.WriteAllTextAtomic(fallbackPath, json);
                        voiceKeysWriteError = null;
                    }
                    catch (Exception ex2)
                    {
                        voiceKeysWriteError = ex2.Message;
                    }
                }
                catch (Exception ex)
                {
                    voiceKeysWriteError = ex.Message;
                }

                try
                {
                    VoiceManager voiceManager = null;
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window is ChatWindow chatWindow)
                        {
                            voiceManager = chatWindow.VoiceManager;
                            break;
                        }
                    }

                    if (voiceManager != null)
                    {
                        var keys = GetVoiceApiKeys();
                        if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrWhiteSpace(elevenKey))
                            voiceManager.ConfigureProvider(VoiceProviderType.ElevenLabs, new Dictionary<string, string> { ["ApiKey"] = elevenKey });

                        await voiceManager.SetProviderAsync(selectedProvider);
                        await voiceManager.RestoreSavedVoiceAsync();
                    }
                }
                catch { }

                if (AIProviderComboBox.SelectedItem is ComboBoxItem aiItem && aiItem.Tag is AIProviderType aiType)
                    await AIManager.SetActiveProviderAsync(aiType);
                
                // Save hardware settings
                SaveHardwareSettings();
                
                // Save honorific to user profile
                await SaveHonorificToProfileAsync();
                
                // Save integration API keys
                // Spotify, Canva, TMDB, IGDB, MusicBrainz removed from UI as requested
                // but keys are preserved in store if already present.

                var cloudProviderId = GetSelectedCloudProviderId();
                var cloudProviderKey = CloudProviderKeyBox.Password?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(cloudProviderId))
                {
                    SetIntegrationApiKey(SelectedCloudProviderKey, cloudProviderId);
                    if (!string.IsNullOrWhiteSpace(cloudProviderKey))
                        SetIntegrationApiKey(cloudProviderId, cloudProviderKey);
                }
                
                // Handle auto-start - only update if changed to avoid unnecessary Safety Mode prompts
                bool currentAutoStartState = false;
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(StartupKey, false);
                    currentAutoStartState = key?.GetValue(AppName) != null;
                }
                catch { }
                
                bool desiredAutoStartState = AutoStartCheckbox.IsChecked == true;
                if (currentAutoStartState != desiredAutoStartState)
                {
                    SetAutoStart(desiredAutoStartState);
                }

                if (!string.IsNullOrWhiteSpace(voiceKeysWriteError))
                {
                    try
                    {
                        MessageBox.Show(
                            "Saved settings, but couldn't write voice_keys.json.\n\n" + voiceKeysWriteError +
                            "\n\nThis can happen if the file was created by an admin account or is locked. " +
                            "Restart Atlas and try again.",
                            "Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    catch
                    {
                    }
                }

                CloseWithDialogResult(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async Task SaveHonorificToProfileAsync()
        {
            try
            {
                // Get selected honorific
                UserHonorific selectedHonorific = UserHonorific.Sir;
                if (HonorificComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
                {
                    if (Enum.TryParse<UserHonorific>(item.Tag.ToString(), out var parsed))
                    {
                        selectedHonorific = parsed;
                    }
                }
                
                // Load existing profile or create new one
                UserProfile profile;
                if (File.Exists(ProfilePath))
                {
                    var json = await File.ReadAllTextAsync(ProfilePath);
                    profile = JsonSerializer.Deserialize<UserProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new UserProfile();
                }
                else
                {
                    profile = new UserProfile();
                }
                
                // Update honorific
                profile.Honorific = selectedHonorific;
                profile.LastUpdated = DateTime.Now;
                
                // Save profile
                var updatedJson = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(ProfilePath, updatedJson);
                
                Debug.WriteLine($"[Settings] Saved honorific: {selectedHonorific}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error saving honorific: {ex.Message}");
            }
        }

        private async void SetAutoStart(bool enable)
        {
            try
            {
                // SAFETY GATE: Check before registry write
                var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                    AtlasAI.Core.OperationType.RegistryWrite,
                    AtlasAI.Core.OperationRisk.Medium,
                    enable ? "Add Atlas AI to Windows startup" : "Remove Atlas AI from Windows startup",
                    new Dictionary<string, object>
                    {
                        ["registryPath"] = $"HKCU\\{StartupKey}",
                        ["valueName"] = AppName,
                        ["action"] = enable ? "add" : "remove"
                    });
                
                if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
                {
                    await App.DialogService.ShowInfoAsync("Safety Mode", safetyCheck.Message + "\n\nEnable dangerous actions in Settings to modify startup entries.");
                    // Revert checkbox state
                    AutoStartCheckbox.IsChecked = !enable;
                    return;
                }
                
                using var key = Registry.CurrentUser.OpenSubKey(StartupKey, true);
                if (key == null) return;
                
                if (enable)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithDialogResult(false);
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            CloseWithDialogResult(false);
        }

        private void CloseWithDialogResult(bool? result)
        {
            try
            {
                DialogResult = result;
            }
            catch
            {
            }

            Close();
        }

        public void FocusIntegration(string focusIntegrationId)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Activate();

                        var normalized = (focusIntegrationId ?? "").Trim().ToLowerInvariant();
                        FrameworkElement? target = null;

                        switch (normalized)
                        {
                            case "realdebrid":
                                if (CloudProviderSection != null)
                                    CloudProviderSection.Visibility = Visibility.Visible;
                                try { LoadCloudProviderSettings(GetIntegrationApiKeys()); } catch { }
                                target = CloudProviderKeyBox;
                                break;

                            case "addon_servers":
                            case var _ when normalized.StartsWith("addon_", StringComparison.Ordinal):
                                if (AddonServersSection != null)
                                    AddonServersSection.Visibility = Visibility.Visible;
                                try { LoadAddonServersSettings(); } catch { }
                                target = AddonServerUrlBox;
                                break;

                            case "elevenlabs":
                                target = ElevenLabsKeyBox;
                                break;

                            default:
                                target = null;
                                break;
                        }

                        if (target == null)
                            return;

                        target.BringIntoView();
                        target.Focus();
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private void ElevenLabsLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                TryOpenHttpsUrl(e.Uri?.AbsoluteUri);
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void FactoryReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var confirmation = MessageBox.Show(
                    "Factory reset will clear saved keys, addons, and local Atlas data on next start. Atlas will close after this is armed. Continue?",
                    "Factory Reset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                FactoryReset.RequestOnNextStart();

                MessageBox.Show(
                    "Factory reset is armed. Start Atlas again to complete the reset.",
                    "Factory Reset",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                CloseWithDialogResult(false);
                Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Factory reset failed: {ex.Message}", "Factory Reset", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddonServersList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (AddonServersList == null)
                    return;

                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.A)
                {
                    AddonServersList.SelectAll();
                    e.Handled = true;
                    return;
                }

                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
                {
                    var selected = AddonServersList.SelectedItems.Cast<object>()
                        .Select(item => NormalizeAddonServerUrl(item?.ToString() ?? ""))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToList();
                    if (selected.Count > 0)
                    {
                        Clipboard.SetText(string.Join(Environment.NewLine, selected));
                        e.Handled = true;
                    }
                    return;
                }

                if (e.Key == Key.Delete || e.Key == Key.Back)
                {
                    AddonServerRemove_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    AddonServerSetActive_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
            catch
            {
            }
        }
        
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }
        
        // Link click handlers removed as UI is removed

        /// <summary>
        /// Guardrail: Only allow https:// URLs to be opened via Process.Start (shell).
        /// </summary>
        private bool TryOpenHttpsUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            // Guardrail: prevent opening arbitrary schemes (file:, ms-settings:, cmd:, etc.)
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                // Shell is required for URLs
                UseShellExecute = true
            });

            return true;
        }
        
        // SpotifyAuth_Click removed - UI removed

        private void LoadCloudProviderSettings(Dictionary<string, string> integrationKeys)
        {
            try
            {
                if (CloudProviderCombo == null || CloudProviderKeyBox == null || CloudProviderStatusText == null)
                    return;

                CloudProviderCombo.Items.Clear();

                if (CloudProviderRegistry.Providers.Count == 0)
                {
                    try
                    {
                        CloudProviderRegistry.Register(new AtlasAI.Integrations.CloudProviders.RealDebridProvider(new DpapiFileSecretsStore()));
                    }
                    catch
                    {
                    }
                }

                var providers = CloudProviderRegistry.Providers;
                providers = providers
                    .Where(p => string.Equals(p.Id, "realdebrid", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (providers.Count == 0)
                {
                    CloudProviderCombo.Items.Add(new ComboBoxItem { Content = "No providers installed", IsEnabled = false });
                    CloudProviderCombo.SelectedIndex = 0;
                    SetCloudProviderUiEnabled(false);
                    CloudProviderStatusText.Text = "No providers installed";
                    return;
                }

                foreach (var p in providers)
                {
                    CloudProviderCombo.Items.Add(new ComboBoxItem { Content = p.DisplayName, Tag = p.Id });
                }

                var preferredId = IntegrationKeyStore.GetDecrypted(SelectedCloudProviderKey);
                var selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(preferredId))
                {
                    for (int i = 0; i < CloudProviderCombo.Items.Count; i++)
                    {
                        if (CloudProviderCombo.Items[i] is ComboBoxItem item &&
                            string.Equals(item.Tag?.ToString(), preferredId, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                CloudProviderCombo.SelectedIndex = selectedIndex;
                SetCloudProviderUiEnabled(true);
                UpdateCloudProviderKeyBox(integrationKeys);
            }
            catch
            {
            }
        }

        private void CloudProvider_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            try
            {
                var integrationKeys = GetIntegrationApiKeys();
                UpdateCloudProviderKeyBox(integrationKeys);
            }
            catch
            {
            }
        }

        private async void CloudProviderValidate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var providerId = GetSelectedCloudProviderId();
                if (string.IsNullOrWhiteSpace(providerId))
                    return;

                SetIntegrationApiKey(SelectedCloudProviderKey, providerId);

                var key = CloudProviderKeyBox.Password?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(key))
                    SetIntegrationApiKey(providerId, key);

                var provider = CloudProviderRegistry.GetById(providerId);
                if (provider == null)
                {
                    CloudProviderStatusText.Text = "No providers installed";
                    return;
                }

                CloudProviderValidateBtn.IsEnabled = false;
                CloudProviderStatusText.Text = "Validating...";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var ok = await provider.ValidateAsync(cts.Token);
                CloudProviderStatusText.Text = ok ? "Validated" : "Not configured or invalid";
            }
            catch
            {
                CloudProviderStatusText.Text = "Validation failed";
            }
            finally
            {
                if (CloudProviderValidateBtn != null)
                    CloudProviderValidateBtn.IsEnabled = true;
            }
        }

        private void CloudProviderClear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var providerId = GetSelectedCloudProviderId();
                if (string.IsNullOrWhiteSpace(providerId))
                    return;

				_userClearedKeys = true;
                IntegrationKeyStore.Delete(providerId);
                CloudProviderKeyBox.Password = "";
                CloudProviderStatusText.Text = "Cleared";
            }
            catch
            {
            }
        }

        private void UpdateCloudProviderKeyBox(Dictionary<string, string> integrationKeys)
        {
            var providerId = GetSelectedCloudProviderId();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                if (!_isLoadingSettings || _userClearedKeys)
					CloudProviderKeyBox.Password = "";
                CloudProviderStatusText.Text = "";
                return;
            }

			var loaded = integrationKeys.TryGetValue(providerId, out var v) ? (v ?? "") : "";
			if (!_isLoadingSettings)
			{
				// Outside of load, always show the current configured key (even if empty).
				CloudProviderKeyBox.Password = loaded;
			}
			else
			{
				// During load, never clear/overwrite a non-empty field with an empty loaded value.
				if (string.IsNullOrWhiteSpace(CloudProviderKeyBox.Password) && !string.IsNullOrWhiteSpace(loaded))
					CloudProviderKeyBox.Password = loaded;
			}

            var provider = CloudProviderRegistry.GetById(providerId);
            if (provider == null)
            {
                CloudProviderStatusText.Text = "No providers installed";
            }
            else
            {
                CloudProviderStatusText.Text = provider.IsConfigured ? "Configured" : "Not configured";
            }
        }

        private void SetCloudProviderUiEnabled(bool enabled)
        {
            CloudProviderCombo.IsEnabled = enabled;
            CloudProviderKeyBox.IsEnabled = enabled;
            CloudProviderValidateBtn.IsEnabled = enabled;
            CloudProviderClearBtn.IsEnabled = enabled;
        }

        private string GetSelectedCloudProviderId()
        {
            if (CloudProviderCombo?.SelectedItem is ComboBoxItem item)
                return item.Tag?.ToString() ?? "";
            return "";
        }
        
        #endregion

        #region Static Helpers

        public static string GetApiKey()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return File.ReadAllText(SettingsPath).Trim();
            }
            catch { }
            return string.Empty;
        }

        public static VoiceProviderType GetSelectedVoiceProvider()
        {
            try
            {
                var voiceSettingsPath = Path.Combine(SettingsDir, "voice_settings.json");
                if (File.Exists(voiceSettingsPath))
                {
                    var json = File.ReadAllText(voiceSettingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("provider", out var prov) &&
                        Enum.TryParse<VoiceProviderType>(prov.GetString(), out var type))
                        return type;
                }

                if (File.Exists(VoiceKeysPath))
                {
                    var json = File.ReadAllText(VoiceKeysPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("provider", out var prov) &&
                        Enum.TryParse<VoiceProviderType>(prov.GetString(), out var type))
                        return type;
                }
            }
            catch { }
            return VoiceProviderType.EdgeTTS;
        }

        public static Dictionary<string, string> GetVoiceApiKeys()
        {
            var keys = new Dictionary<string, string>();

            try
            {
                var p = AtlasPaths.VoiceKeysReadCandidates().FirstOrDefault(File.Exists);
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                {
                    var json = File.ReadAllText(p);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("openai", out var openai))
                        keys["openai"] = SecretProtector.UnprotectIfNeeded(openai.GetString() ?? "");
                    if (root.TryGetProperty("elevenlabs", out var elevenlabs))
                        keys["elevenlabs"] = Core.ApiKeySanitizer.SanitizeForHttpHeader(SecretProtector.UnprotectIfNeeded(elevenlabs.GetString() ?? ""));
                }
            }
            catch { }
            return keys;
        }
        
        /// <summary>
        /// Get integration API keys (Canva, etc.) - users provide their own keys
        /// </summary>
        public static Dictionary<string, string> GetIntegrationApiKeys()
        {
            return Core.IntegrationKeyStore.GetAllDecrypted();
        }
        
        /// <summary>
        /// Save integration API key (user's own key)
        /// </summary>
        public static void SetIntegrationApiKey(string service, string apiKey)
        {
            Core.IntegrationKeyStore.SetProtected(service, apiKey);

            try
            {
                if (service == "canva" && !string.IsNullOrEmpty(apiKey))
                {
                    Canva.CanvaTool.Instance.ConfigureApi(apiKey);
                }
            }
            catch { }
        }
        
        public static (int device, int sensitivity, string quality, string? deviceId) GetHardwareSettings()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "hardware_settings.json");
                    
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    var deviceId = root.TryGetProperty("micDeviceId", out var id) ? id.GetString() : null;
                    var device = root.TryGetProperty("micDevice", out var d) ? d.GetInt32() : -1;
                    var sensitivity = root.TryGetProperty("micSensitivity", out var s) ? s.GetInt32() : 120;
                    var quality = root.TryGetProperty("qualityMode", out var q) ? q.GetString() ?? "balanced" : "balanced";
                    
                    return (device, sensitivity, quality, deviceId);
                }
            }
            catch { }
            return (-1, 120, "balanced", null);
        }
        
        /// <summary>
        /// Set hardware settings (used by automatic microphone fallback)
        /// </summary>
        public static void SetHardwareSettings(int device, int sensitivity, string quality, string? deviceId)
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
                Directory.CreateDirectory(appDataPath);
                
                var path = Path.Combine(appDataPath, "hardware_settings.json");
                
                var settings = new
                {
                    micDevice = device,
                    micDeviceId = deviceId ?? "",
                    micSensitivity = sensitivity,
                    qualityMode = quality
                };
                
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                
                Debug.WriteLine($"[Settings] Updated hardware settings: device={device}, deviceId={deviceId}, sensitivity={sensitivity}, quality={quality}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Failed to save hardware settings: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Safety Self-Test
        
        private async void RunSelfTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selfTestWindow = new AtlasAI.UI.SafetySelfTestWindow();
                selfTestWindow.Owner = this;
                selfTestWindow.ShowDialog();
                
                // Update safety status badge after test
                UpdateSafetyStatusBadge();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Failed to open self-test window: {ex.Message}");
                await App.DialogService.ShowErrorAsync("Error", $"Failed to open safety self-test: {ex.Message}");
            }
        }
        
        private void UpdateSafetyStatusBadge()
        {
            var safetyKernel = AtlasAI.Core.SafetyKernel.Instance;
            
            if (safetyKernel.DangerousActionsEnabled)
            {
                SafetyStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)); // Red
                SafetyStatusText.Text = "⚠️ LIVE";
            }
            else
            {
                SafetyStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b)); // Amber
                SafetyStatusText.Text = "🛡️ SAFE";
            }
        }
        
        #endregion
        
        #region Orb Appearance Settings
        
        private void OrbStyle_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (OrbStyleCombo?.SelectedItem is ComboBoxItem item && item.Tag is string style)
            {
                // Don't apply during loading
                if (_isLoadingSettings)
                    return;
                    
                bool useParticles = style == "particles";
                
                // Apply to ChatWindow
                if (Owner is ChatWindow ownerChat)
                {
                    if (useParticles)
                    {
                        ownerChat.SetOrbStyle(false, null); // Use particles
                    }
                    else
                    {
                        ownerChat.SetOrbStyle(true, style); // Use Lottie with specific animation
                    }
                    Debug.WriteLine($"[Settings] Applied orb style via Owner: {style}");
                }
                else
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window is ChatWindow chatWindow)
                        {
                            if (useParticles)
                            {
                                chatWindow.SetOrbStyle(false, null);
                            }
                            else
                            {
                                chatWindow.SetOrbStyle(true, style);
                            }
                            Debug.WriteLine($"[Settings] Applied orb style: {style}");
                            break;
                        }
                        // Also apply to any ChatView hosted in a different window (Command Center)
                        if (window.Content is FrameworkElement fe)
                        {
                            foreach (var descendant in FindVisualDescendants(fe))
                            {
                                if (descendant is Views.ChatView cv)
                                {
                                    cv.ApplyOrbStyle(!useParticles, useParticles ? null : style);
                                    Debug.WriteLine($"[Settings] Applied orb style to ChatView: {style}");
                                    break;
                                }
                            }
                        }
                    }
                }
                
                // Save setting
                SaveOrbSettings();
            }
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> FindVisualDescendants(DependencyObject root)
        {
            if (root == null) yield break;
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in FindVisualDescendants(child))
                    yield return d;
            }
        }
        
        private void OrbColor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (OrbColorCombo?.SelectedItem is ComboBoxItem item && item.Tag is string preset)
            {
                // Don't apply during loading - wait for LoadOrbSettings to set the correct value
                if (_isLoadingSettings)
                    return;
                    
                // HoloCoreControl removed - orb functionality disabled
                Debug.WriteLine($"[Settings] Orb color preset changed: {preset} (orb removed)");
                
                // Save setting
                SaveOrbSettings();
            }
        }
        
        private void OrbSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OrbSpeedLabel != null && OrbSpeedSlider != null)
            {
                var speed = OrbSpeedSlider.Value;
                OrbSpeedLabel.Text = $"{speed:F1}x";
                
                // Don't apply during loading
                if (_isLoadingSettings)
                    return;
                
                // HoloCoreControl removed - orb functionality disabled
                Debug.WriteLine($"[Settings] Orb speed changed: {speed} (orb removed)");
                
                SaveOrbSettings();
            }
        }
        
        private void OrbParticle_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OrbParticleLabel != null && OrbParticleSlider != null)
            {
                var count = (int)OrbParticleSlider.Value;
                OrbParticleLabel.Text = count.ToString();
                
                // Don't apply during loading
                if (_isLoadingSettings)
                    return;
                
                // HoloCoreControl removed - orb functionality disabled
                Debug.WriteLine($"[Settings] Orb particle count changed: {count} (orb removed)");
                
                SaveOrbSettings();
            }
        }
        
        private void LoadOrbSettings()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "orb_settings.json");
                    
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("colorPreset", out var color))
                    {
                        var preset = color.GetString() ?? "cyan";
                        for (int i = 0; i < OrbColorCombo.Items.Count; i++)
                        {
                            if (OrbColorCombo.Items[i] is ComboBoxItem item && item.Tag as string == preset)
                            {
                                OrbColorCombo.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Default to cyan
                        OrbColorCombo.SelectedIndex = 0;
                    }
                    
                    if (root.TryGetProperty("orbStyle", out var orbStyle))
                    {
                        var style = orbStyle.GetString() ?? "Siri Animation.json";
                        for (int i = 0; i < OrbStyleCombo.Items.Count; i++)
                        {
                            if (OrbStyleCombo.Items[i] is ComboBoxItem item && item.Tag as string == style)
                            {
                                OrbStyleCombo.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Default to Siri Animation (index 1)
                        OrbStyleCombo.SelectedIndex = 1;
                    }
                    
                    if (root.TryGetProperty("animationSpeed", out var speed))
                    {
                        OrbSpeedSlider.Value = speed.GetDouble();
                    }
                    
                    if (root.TryGetProperty("particleCount", out var particles))
                    {
                        OrbParticleSlider.Value = particles.GetInt32();
                    }
                }
                else
                {
                    // No settings file - set defaults
                    OrbColorCombo.SelectedIndex = 0; // Cyan
                    OrbStyleCombo.SelectedIndex = 1; // Siri Animation
                }
            }
            catch 
            {
                // On error, set defaults
                if (OrbColorCombo.SelectedIndex < 0) OrbColorCombo.SelectedIndex = 0;
                if (OrbStyleCombo.SelectedIndex < 0) OrbStyleCombo.SelectedIndex = 1;
            }
        }
        
        private void SaveOrbSettings()
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
                Directory.CreateDirectory(appDataPath);
                
                var path = Path.Combine(appDataPath, "orb_settings.json");
                
                var colorPreset = "cyan";
                if (OrbColorCombo.SelectedItem is ComboBoxItem item && item.Tag is string preset)
                    colorPreset = preset;
                
                var orbStyle = "Siri Animation.json";
                if (OrbStyleCombo.SelectedItem is ComboBoxItem styleItem && styleItem.Tag is string style)
                    orbStyle = style;
                
                var settings = new
                {
                    colorPreset = colorPreset,
                    orbStyle = orbStyle,
                    animationSpeed = OrbSpeedSlider.Value,
                    particleCount = (int)OrbParticleSlider.Value
                };
                
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }
        
        /// <summary>
        /// Get saved orb settings (for loading on startup)
        /// </summary>
        public static (string colorPreset, string orbStyle, double speed, int particles) GetOrbSettings()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "orb_settings.json");
                    
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    var colorPreset = root.TryGetProperty("colorPreset", out var c) ? c.GetString() ?? "cyan" : "cyan";
                    var orbStyle = root.TryGetProperty("orbStyle", out var o) ? o.GetString() ?? "Siri Animation.json" : "Siri Animation.json";
                    var speed = root.TryGetProperty("animationSpeed", out var s) ? s.GetDouble() : 1.0;
                    var particles = root.TryGetProperty("particleCount", out var p) ? p.GetInt32() : 180;
                    
                    return (colorPreset, orbStyle, speed, particles);
                }
            }
            catch { }
            return ("cyan", "Siri Animation.json", 1.0, 180);
        }
        
        #endregion
        
        #region Preferences
        
        private void LoadPreferences()
        {
            try
            {
                var prefs = Core.PreferencesStore.Instance.Current;
                var settings = AtlasAI.Settings.SettingsStore.Current;

                // Populate Personality ComboBox dynamically from PersonalityRegistry
                PersonalityCombo.Items.Clear();
                bool devModeEnabled = settings?.DebugLogsEnabled ?? false;
                var personalities = AtlasAI.Personality.PersonalityRegistry.GetAvailable(includeHidden: devModeEnabled);

                foreach (var personalityDef in personalities)
                {
                    PersonalityCombo.Items.Add(new ComboBoxItem
                    {
                        Content = $"{personalityDef.Icon} {personalityDef.DisplayName}",
                        Tag = personalityDef.Id,
                        ToolTip = personalityDef.Description
                    });
                }

                // Select current personality
                var selectedPersonality = string.IsNullOrWhiteSpace(prefs.ChatPersonality) ? "Buddy" : prefs.ChatPersonality.Trim();
                if (AtlasAI.Personality.PersonalityRegistry.GetById(selectedPersonality) == null)
                    selectedPersonality = "Buddy";
                for (int i = 0; i < PersonalityCombo.Items.Count; i++)
                {
                    if (PersonalityCombo.Items[i] is ComboBoxItem item &&
                        item.Tag?.ToString() == selectedPersonality)
                    {
                        PersonalityCombo.SelectedIndex = i;
                        break;
                    }
                }
                if (PersonalityCombo.SelectedIndex < 0) PersonalityCombo.SelectedIndex = 0;
                UpdatePersonalityDescription(selectedPersonality);

                // Load Unrestricted slider
                int unrestrictedLevel = prefs.ChatBanterLevel > 0 ? prefs.ChatBanterLevel : 3;
                UnrestrictedSlider.Value = unrestrictedLevel;
                UpdateUnrestrictedLevelLabel(unrestrictedLevel);

                // Checkboxes
                ShowConfidenceCheckbox.IsChecked = prefs.ShowConfidenceStatement;
                ProactiveSuggestionsCheckbox.IsChecked = prefs.EnableProactiveSuggestions;
                AgentModeDefaultCheckbox.IsChecked = prefs.AgentModeDefaultOn;
                AliveModeCheckbox.IsChecked = prefs.AliveModeEnabled;
                UpdateAliveModeStatus(prefs.AliveModeEnabled);

                if (CheckInsEnabledCheckbox != null)
                    CheckInsEnabledCheckbox.IsChecked = prefs.ChatCheckInsEnabled;
                if (CheckInMinMinutesTextBox != null)
                    CheckInMinMinutesTextBox.Text = prefs.ChatCheckInMinMinutes.ToString();
                if (CheckInMaxMinutesTextBox != null)
                    CheckInMaxMinutesTextBox.Text = prefs.ChatCheckInMaxMinutes.ToString();
                if (CheckInIdleMinutesTextBox != null)
                    CheckInIdleMinutesTextBox.Text = prefs.ChatCheckInIdleMinutes.ToString();

                // CSV Import
                CsvGenerateM3uPlaylistCheckbox.IsChecked = prefs.CsvGenerateM3uPlaylist;
                CsvTranscodeToMp3Checkbox.IsChecked = prefs.CsvTranscodeToMp3;
                CsvVariantsTextBox.Text = prefs.CsvVariants;
                CsvMinDurationTextBox.Text = prefs.CsvMinDurationSeconds.ToString();
                CsvMaxDurationTextBox.Text = prefs.CsvMaxDurationSeconds.ToString();
                CsvExcludeInstrumentalsCheckbox.IsChecked = prefs.CsvExcludeInstrumentals;
                CsvEmbedThumbnailsCheckbox.IsChecked = prefs.CsvEmbedThumbnails;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] LoadPreferences error: {ex.Message}");
            }
        }
        
        private void Personality_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (PersonalityCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                Core.PreferencesStore.Instance.Update(p =>
                {
                    p.ChatPersonality = tag;
                    p.SelectedPersonalityId = tag;
                });
                try
                {
                    var current = AtlasAI.Settings.SettingsStore.Current;
                    current.PersonalitySelected = tag;
                    AtlasAI.Settings.SettingsStore.Save(current);
                }
                catch
                {
                }
                UpdatePersonalityDescription(tag);
                Debug.WriteLine($"[Settings] Personality changed to: {tag}");
            }
        }

        private void UnrestrictedLevel_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;

            int level = (int)UnrestrictedSlider.Value;
            UpdateUnrestrictedLevelLabel(level);

            Core.PreferencesStore.Instance.Update(p =>
            {
                p.ChatBanterLevel = level;
                p.ChatHumanLevel = level;
                p.SavageLevel = level;
            });

            try
            {
                var current = AtlasAI.Settings.SettingsStore.Current;
                if (current != null)
                {
                    current.UnfilteredChaosIntensity = level;
                    AtlasAI.Settings.SettingsStore.Save(current);
                }
            }
            catch
            {
            }
            Debug.WriteLine($"[Settings] Unrestricted level changed to: {level}");
        }

        private void UpdateUnrestrictedLevelLabel(int level)
        {
            if (UnrestrictedLevelLabel == null) return;

            UnrestrictedLevelLabel.Text = level switch
            {
                1 => "Mild",
                2 => "Light",
                3 => "Moderate",
                4 => "Strong",
                5 => "Savage",
                _ => "Moderate"
            };
        }
        
        private void UpdatePersonalityDescription(string personalityId)
        {
            if (PersonalityDescText == null) return;

            var personalityDef = AtlasAI.Personality.PersonalityRegistry.GetById(personalityId);
            if (personalityDef != null)
            {
                PersonalityDescText.Text = $"{personalityDef.StyleGuide}\n\nDomain: {personalityDef.Domain}";
            }
            else
            {
                PersonalityDescText.Text = "Balanced, professional assistant.";
            }
        }
        
        private void ShowConfidence_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.ShowConfidenceStatement = ShowConfidenceCheckbox.IsChecked == true);
        }
        
        private void ProactiveSuggestions_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.EnableProactiveSuggestions = ProactiveSuggestionsCheckbox.IsChecked == true);
        }
        
        private void AgentModeDefault_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.AgentModeDefaultOn = AgentModeDefaultCheckbox.IsChecked == true);
        }
        
        private void AliveMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            var enabled = AliveModeCheckbox.IsChecked == true;
            Core.PreferencesStore.Instance.Update(p => p.AliveModeEnabled = enabled);
            UpdateAliveModeStatus(enabled);
            Debug.WriteLine($"[Settings] AliveMode changed to: {enabled}");
        }

        private void CheckInsEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            var enabled = CheckInsEnabledCheckbox?.IsChecked == true;
            Core.PreferencesStore.Instance.Update(p => p.ChatCheckInsEnabled = enabled);
        }

        private void CheckInMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            int ReadBox(TextBox? tb, int fallback)
            {
                var s = (tb?.Text ?? "").Trim();
                return int.TryParse(s, out var v) ? v : fallback;
            }

            var prefs = Core.PreferencesStore.Instance.Current;
            var min = ReadBox(CheckInMinMinutesTextBox, prefs.ChatCheckInMinMinutes);
            var max = ReadBox(CheckInMaxMinutesTextBox, prefs.ChatCheckInMaxMinutes);
            var idle = ReadBox(CheckInIdleMinutesTextBox, prefs.ChatCheckInIdleMinutes);

            min = Math.Clamp(min, 5, 360);
            max = Math.Clamp(max, 5, 360);
            if (max < min) (min, max) = (max, min);
            idle = Math.Clamp(idle, 0, 240);

            Core.PreferencesStore.Instance.Update(p =>
            {
                p.ChatCheckInMinMinutes = min;
                p.ChatCheckInMaxMinutes = max;
                p.ChatCheckInIdleMinutes = idle;
            });

            if (CheckInMinMinutesTextBox != null) CheckInMinMinutesTextBox.Text = min.ToString();
            if (CheckInMaxMinutesTextBox != null) CheckInMaxMinutesTextBox.Text = max.ToString();
            if (CheckInIdleMinutesTextBox != null) CheckInIdleMinutesTextBox.Text = idle.ToString();
        }
        
        private void UpdateAliveModeStatus(bool enabled)
        {
            if (AliveModeStatusBorder == null || AliveModeStatusText == null) return;
            
            if (enabled)
            {
                AliveModeStatusBorder.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22d3ee10"));
                AliveModeStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22d3ee"));
                AliveModeStatusText.Text = "✨ On (Recommended) — Atlas responds naturally, adapts tone over time, and maintains conversational flow.";
            }
            else
            {
                AliveModeStatusBorder.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#64748b10"));
                AliveModeStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#64748b"));
                AliveModeStatusText.Text = "Off — Atlas uses stricter, more predictable phrasing with increased fallback responses.";
            }
        }
        
        private async void ResetPreferences_Click(object sender, RoutedEventArgs e)
        {
            var result = await App.DialogService.ShowAsync(
                "Reset Preferences",
                "Reset all preferences to defaults?\n\nThis will clear your pinned commands, usage history, and all customizations.",
                AtlasDialogButtons.YesNo,
                AtlasDialogIcon.Question);

            if (result == AtlasDialogResult.Yes)
            {
                Core.PreferencesStore.Instance.ResetToDefaults();
                LoadPreferences();
                LoadFloatingHudPreferences();
                await App.DialogService.ShowInfoAsync("Reset Complete", "Preferences reset to defaults.");
            }
        }
        
        private async void ResetWorkspaceMemory_Click(object sender, RoutedEventArgs e)
        {
            var result = await App.DialogService.ShowAsync(
                "Reset Workspace Memory",
                "Reset workspace memory for the current project?\n\nThis will clear learned build commands, project type, and entry points for this workspace.",
                AtlasDialogButtons.YesNo,
                AtlasDialogIcon.Question);

            if (result == AtlasDialogResult.Yes)
            {
                try
                {
                    await Memory.MemoryManager.Instance.ResetWorkspaceMemoryAsync();
                    await App.DialogService.ShowInfoAsync("Reset Complete", "Workspace memory has been reset.");
                }
                catch (Exception ex)
                {
                    await App.DialogService.ShowErrorAsync("Error", $"Failed to reset workspace memory: {ex.Message}");
                }
            }
        }
        
        private async void ExportPreferences_Click(object sender, RoutedEventArgs e)
        {
            var exportPath = Core.PreferencesStore.Instance.ExportPreferences();
            if (exportPath != null)
            {
                await App.DialogService.ShowInfoAsync(
                    "Export Complete",
                    $"Preferences exported to:\n{exportPath}");
                    
                // Open the exports folder
                try
                {
                    var folder = Path.GetDirectoryName(exportPath);
                    if (folder != null)
                        Process.Start("explorer.exe", folder);
                }
                catch { }
            }
            else
            {
                await App.DialogService.ShowErrorAsync("Export Error", "Failed to export preferences.");
            }
        }
        
        #endregion

        #region CSV Import Preferences

        private void CsvGenerateM3uPlaylist_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.CsvGenerateM3uPlaylist = CsvGenerateM3uPlaylistCheckbox.IsChecked == true);
        }

        private void CsvTranscodeToMp3_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.CsvTranscodeToMp3 = CsvTranscodeToMp3Checkbox.IsChecked == true);
        }

        private void CsvVariants_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.CsvVariants = CsvVariantsTextBox.Text ?? "");
        }

        private void CsvMinDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (int.TryParse(CsvMinDurationTextBox.Text, out var v))
                Core.PreferencesStore.Instance.Update(p => p.CsvMinDurationSeconds = v);
        }

        private void CsvMaxDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (int.TryParse(CsvMaxDurationTextBox.Text, out var v))
                Core.PreferencesStore.Instance.Update(p => p.CsvMaxDurationSeconds = v);
        }

        private void CsvExcludeInstrumentals_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.CsvExcludeInstrumentals = CsvExcludeInstrumentalsCheckbox.IsChecked == true);
        }

        private void CsvEmbedThumbnails_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.CsvEmbedThumbnails = CsvEmbedThumbnailsCheckbox.IsChecked == true);
        }

        private void NumberOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (var ch in e.Text)
            {
                if (!char.IsDigit(ch))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        #endregion
        
        #region Core Animations
        
        private void LoadCoreAnimationPreferences()
        {
            try
            {
                var prefs = Core.PreferencesStore.Instance.Current;
                
                // Load style selection
                for (int i = 0; i < CoreStyleCombo.Items.Count; i++)
                {
                    if (CoreStyleCombo.Items[i] is ComboBoxItem item && 
                        item.Tag?.ToString() == prefs.CoreStyle)
                    {
                        CoreStyleCombo.SelectedIndex = i;
                        break;
                    }
                }
                
                // Load colors into button backgrounds
                UpdateColorButton(PrimaryColorButton, prefs.CorePrimaryColor);
                UpdateColorButton(SecondaryColorButton, prefs.CoreSecondaryColor);
                UpdateColorButton(CenterColorButton, prefs.CorePrimaryColor); // Center uses primary
                UpdateColorButton(ThinkingColorButton, prefs.CoreThinkingColor);
                
                // Load animation settings
                CoreAnimationSpeedSlider.Value = prefs.CoreAnimationSpeed;
                CoreAnimationSpeedValue.Text = $"{prefs.CoreAnimationSpeed:F1}x";
                
                CoreParticleCountSlider.Value = prefs.CoreParticleCount;
                CoreParticleCountValue.Text = prefs.CoreParticleCount.ToString();
                
                CoreRingSpeedSlider.Value = prefs.CoreRingSpeed;
                CoreRingSpeedValue.Text = $"{prefs.CoreRingSpeed:F1}x";
                
                Debug.WriteLine($"[Settings] Loaded core animation settings: Style={prefs.CoreStyle}, Colors={prefs.CorePrimaryColor}/{prefs.CoreSecondaryColor}, Speed={prefs.CoreAnimationSpeed:F1}x, Particles={prefs.CoreParticleCount}, RingSpeed={prefs.CoreRingSpeed:F1}x");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] LoadCoreAnimationPreferences error: {ex.Message}");
            }
        }
        
        private void CoreStyle_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            
            if (CoreStyleCombo.SelectedItem is ComboBoxItem item && item.Tag is string style)
            {
                Core.PreferencesStore.Instance.Update(p => p.CoreStyle = style);
                Debug.WriteLine($"[Settings] Core style changed to: {style}");
                // TODO: Apply style to MainWindow (orb removed)
            }
        }
        
        // Color picker button handlers
        private void PrimaryColorButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[Settings] PrimaryColorButton clicked");
            var currentColor = GetColorFromButton(PrimaryColorButton);
            Debug.WriteLine($"[Settings] Current color: R={currentColor.R}, G={currentColor.G}, B={currentColor.B}");
            
            if (ShowColorPickerDialog(currentColor, out var newColor))
            {
                var hex = ColorToHex(newColor);
                Debug.WriteLine($"[Settings] New color selected: {hex}");
                UpdateColorButton(PrimaryColorButton, hex);
                UpdateColorButton(CenterColorButton, hex); // Center uses same color as primary
                Core.PreferencesStore.Instance.Update(p => p.CorePrimaryColor = hex);
                Debug.WriteLine($"[Settings] Primary color changed to: {hex}");
            }
            else
            {
                Debug.WriteLine("[Settings] Color picker cancelled");
            }
        }
        
        private void SecondaryColorButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[Settings] ═══════════════════════════════════════");
            Debug.WriteLine("[Settings] SecondaryColorButton clicked");
            var currentColor = GetColorFromButton(SecondaryColorButton);
            Debug.WriteLine($"[Settings] Current secondary color: R={currentColor.R}, G={currentColor.G}, B={currentColor.B}");
            
            if (ShowColorPickerDialog(currentColor, out var newColor))
            {
                var hex = ColorToHex(newColor);
                Debug.WriteLine($"[Settings] New secondary color selected: {hex}");
                UpdateColorButton(SecondaryColorButton, hex);
                
                // Update preferences - ONLY secondary color
                Core.PreferencesStore.Instance.Update(p => {
                    Debug.WriteLine($"[Settings] BEFORE UPDATE: Primary={p.CorePrimaryColor}, Secondary={p.CoreSecondaryColor}");
                    p.CoreSecondaryColor = hex;
                    Debug.WriteLine($"[Settings] AFTER UPDATE: Primary={p.CorePrimaryColor}, Secondary={p.CoreSecondaryColor}");
                });
                
                Debug.WriteLine($"[Settings] Secondary color changed to: {hex}");
            }
            else
            {
                Debug.WriteLine("[Settings] Color picker cancelled");
            }
            Debug.WriteLine("[Settings] ═══════════════════════════════════════");
        }
        
        private void CenterColorButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[Settings] CenterColorButton clicked - redirecting to Primary");
            // Center uses primary color, so just call the primary handler
            PrimaryColorButton_Click(sender, e);
        }
        
        private void ThinkingColorButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[Settings] ThinkingColorButton clicked");
            var currentColor = GetColorFromButton(ThinkingColorButton);
            if (ShowColorPickerDialog(currentColor, out var newColor))
            {
                var hex = ColorToHex(newColor);
                UpdateColorButton(ThinkingColorButton, hex);
                Core.PreferencesStore.Instance.Update(p => p.CoreThinkingColor = hex);
                Debug.WriteLine($"[Settings] Thinking color changed to: {hex}");
            }
        }
        
        private void QuickPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string preset)
            {
                var (primary, secondary, thinking) = GetColorPreset(preset);
                
                // Update button backgrounds
                UpdateColorButton(PrimaryColorButton, primary);
                UpdateColorButton(SecondaryColorButton, secondary);
                UpdateColorButton(CenterColorButton, primary);
                UpdateColorButton(ThinkingColorButton, thinking);
                
                // Update all at once
                Core.PreferencesStore.Instance.Update(p =>
                {
                    p.CorePrimaryColor = primary;
                    p.CoreSecondaryColor = secondary;
                    p.CoreThinkingColor = thinking;
                });
                
                Debug.WriteLine($"[Settings] Applied preset: {preset}");
            }
        }
        
        private (string primary, string secondary, string thinking) GetColorPreset(string preset)
        {
            return preset switch
            {
                "Cyan" => ("#22d3ee", "#7dd3fc", "#f97316"),
                "Purple" => ("#a855f7", "#c4b5fd", "#ec4899"),
                "Green" => ("#22c55e", "#86efac", "#facc15"),
                "Red" => ("#ef4444", "#fca5a5", "#f97316"),
                "Gold" => ("#eab308", "#fde047", "#f97316"),
                "Orange" => ("#f97316", "#fdba74", "#dc2626"),
                "IceBlue" => ("#06b6d4", "#67e8f9", "#8b5cf6"),
                "Pink" => ("#ec4899", "#f9a8d4", "#a855f7"),
                _ => ("#22d3ee", "#7dd3fc", "#f97316")
            };
        }
        
        // Color picker helper methods
        private void UpdateColorButton(System.Windows.Controls.Button button, string hexColor)
        {
            try
            {
                if (string.IsNullOrEmpty(hexColor) || !hexColor.StartsWith("#") || hexColor.Length != 7)
                {
                    Debug.WriteLine($"[Settings] UpdateColorButton: Invalid hex color '{hexColor}'");
                    return;
                }
                    
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                Debug.WriteLine($"[Settings] UpdateColorButton: Updating {button.Name} to {hexColor}");
                
                // Force template to be applied
                button.ApplyTemplate();
                button.UpdateLayout();
                
                var template = button.Template;
                if (template == null)
                {
                    Debug.WriteLine($"[Settings] UpdateColorButton: Template is null for {button.Name}");
                    return;
                }
                
                // Find the Border and TextBlock in the button's template
                var border = template.FindName("ColorBorder", button) as Border;
                var textBlock = template.FindName(button.Name.Replace("Button", "Text"), button) as TextBlock;
                
                Debug.WriteLine($"[Settings] UpdateColorButton: Border={border != null}, TextBlock={textBlock != null}");
                
                if (border != null)
                {
                    // Update border background with gradient
                    var gradient = new LinearGradientBrush();
                    gradient.StartPoint = new Point(0, 0);
                    gradient.EndPoint = new Point(1, 1);
                    gradient.GradientStops.Add(new GradientStop(color, 0));
                    gradient.GradientStops.Add(new GradientStop(Color.FromArgb(200, color.R, color.G, color.B), 1));
                    border.Background = gradient;
                    Debug.WriteLine($"[Settings] UpdateColorButton: Border background updated");
                }
                
                if (textBlock != null)
                {
                    // Update text to show hex value
                    textBlock.Text = hexColor.ToUpper();
                    Debug.WriteLine($"[Settings] UpdateColorButton: TextBlock text updated to {hexColor.ToUpper()}");
                    
                    // Set text color based on brightness
                    var brightness = (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 255;
                    textBlock.Foreground = brightness > 0.5 ? Brushes.Black : Brushes.White;
                }
                
                Debug.WriteLine($"[Settings] UpdateColorButton: {button.Name} = {hexColor}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] UpdateColorButton error: {ex.Message}");
            }
        }
        
        private bool ShowColorPickerDialog(System.Drawing.Color currentColor, out System.Drawing.Color selectedColor)
        {
            using var dialog = new System.Windows.Forms.ColorDialog();
            dialog.Color = currentColor;
            dialog.FullOpen = true;
            dialog.AnyColor = true;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                selectedColor = dialog.Color;
                return true;
            }
            
            selectedColor = currentColor;
            return false;
        }
        
        private System.Drawing.Color GetColorFromButton(System.Windows.Controls.Button button)
        {
            try
            {
                // Get color from the TextBlock in the template
                button.ApplyTemplate();
                var template = button.Template;
                if (template != null)
                {
                    var textBlock = template.FindName(button.Name.Replace("Button", "Text"), button) as TextBlock;
                    if (textBlock != null && textBlock.Text.StartsWith("#") && textBlock.Text.Length == 7)
                    {
                        var wpfColor = (Color)ColorConverter.ConvertFromString(textBlock.Text);
                        return System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] GetColorFromButton error: {ex.Message}");
            }
            
            // Default to cyan
            return System.Drawing.Color.FromArgb(0x22, 0xd3, 0xee);
        }
        
        private string ColorToHex(System.Drawing.Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        
        private void UpdateColorPreview(Border preview, string hexColor)
        {
            try
            {
                if (string.IsNullOrEmpty(hexColor) || !hexColor.StartsWith("#") || hexColor.Length != 7)
                    return;
                    
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                preview.Background = new SolidColorBrush(color);
            }
            catch
            {
                // Invalid color format - ignore
            }
        }
        
        private bool IsValidHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#") || hex.Length != 7)
                return false;
                
            try
            {
                ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private void CoreAnimationSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            
            var speed = CoreAnimationSpeedSlider.Value;
            CoreAnimationSpeedValue.Text = $"{speed:F1}x";
            
            // MainWindow orb removed - no HoloCore to update
            Debug.WriteLine($"[Settings] Animation speed changed to: {speed:F1}x (orb removed)");
            
            // Save to preferences
            Core.PreferencesStore.Instance.Update(p => p.CoreAnimationSpeed = speed);
        }
        
        private void CoreParticleCount_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            
            var count = (int)CoreParticleCountSlider.Value;
            CoreParticleCountValue.Text = count.ToString();
            
            // MainWindow orb removed - no HoloCore to update
            Debug.WriteLine($"[Settings] Particle count changed to: {count} (orb removed)");
            
            // Save to preferences
            Core.PreferencesStore.Instance.Update(p => p.CoreParticleCount = count);
        }
        
        private void CoreRingSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            
            var speed = CoreRingSpeedSlider.Value;
            CoreRingSpeedValue.Text = $"{speed:F1}x";
            
            // MainWindow orb removed - no HoloCore to update
            Debug.WriteLine($"[Settings] Ring speed changed to: {speed:F1}x (orb removed)");
            
            // Save to preferences
            Core.PreferencesStore.Instance.Update(p => p.CoreRingSpeed = speed);
        }
        
        private void ResetCoreAnimations_Click(object sender, RoutedEventArgs e)
        {
            // Reset to defaults
            CoreStyleCombo.SelectedIndex = 0; // Classic
            UpdateColorButton(PrimaryColorButton, "#22d3ee");
            UpdateColorButton(SecondaryColorButton, "#7dd3fc");
            UpdateColorButton(CenterColorButton, "#22d3ee");
            UpdateColorButton(ThinkingColorButton, "#f97316");
            CoreAnimationSpeedSlider.Value = 1.0;
            CoreParticleCountSlider.Value = 120;
            CoreRingSpeedSlider.Value = 1.0;
            
            // MainWindow orb removed - no HoloCore to reset
            Debug.WriteLine("[Settings] Orb settings reset (orb removed)");
            
            // Save to preferences
            Core.PreferencesStore.Instance.Update(p =>
            {
                p.CoreStyle = "Classic";
                p.CorePrimaryColor = "#22d3ee";
                p.CoreSecondaryColor = "#7dd3fc";
                p.CoreThinkingColor = "#f97316";
                p.CoreAnimationSpeed = 1.0;
                p.CoreParticleCount = 120;
                p.CoreRingSpeed = 1.0;
            });
            
            Debug.WriteLine("[Settings] Core animations and colors reset to defaults");
        }
        
        #endregion
        
        #region Floating HUD
        
        private void LoadFloatingHudPreferences()
        {
            try
            {
                var prefs = Core.PreferencesStore.Instance.Current;
                
                // Enable checkbox
                FloatingHudEnabledCheckbox.IsChecked = prefs.FloatingHudEnabled;
                
                // Position
                for (int i = 0; i < HudPositionCombo.Items.Count; i++)
                {
                    if (HudPositionCombo.Items[i] is ComboBoxItem item && 
                        item.Tag?.ToString() == prefs.FloatingHudPosition.ToString())
                    {
                        HudPositionCombo.SelectedIndex = i;
                        break;
                    }
                }
                
                // Size
                for (int i = 0; i < HudSizeCombo.Items.Count; i++)
                {
                    if (HudSizeCombo.Items[i] is ComboBoxItem item && 
                        item.Tag?.ToString() == prefs.FloatingHudSize.ToString())
                    {
                        HudSizeCombo.SelectedIndex = i;
                        break;
                    }
                }
                
                // Click-through
                HudClickThroughCheckbox.IsChecked = prefs.FloatingHudClickThrough;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] LoadFloatingHudPreferences error: {ex.Message}");
            }
        }
        
        private void FloatingHudEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.FloatingHudEnabled = FloatingHudEnabledCheckbox.IsChecked == true);
        }
        
        private void HudPosition_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (HudPositionCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<Core.HudPosition>(tag, out var position))
                {
                    Core.PreferencesStore.Instance.Update(p => p.FloatingHudPosition = position);
                }
            }
        }
        
        private void HudSize_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (HudSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<Core.HudSize>(tag, out var size))
                {
                    Core.PreferencesStore.Instance.Update(p => p.FloatingHudSize = size);
                }
            }
        }
        
        private void HudClickThrough_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            Core.PreferencesStore.Instance.Update(p => p.FloatingHudClickThrough = HudClickThroughCheckbox.IsChecked == true);
        }
        
        #endregion
        
        #region Online Mode
        
        private void LoadOnlineModeSettings()
        {
            try
            {
                var setting = Core.OnlineModeManager.Instance.Setting;
                
                // Select the matching combo item
                for (int i = 0; i < OnlineModeCombo.Items.Count; i++)
                {
                    if (OnlineModeCombo.Items[i] is ComboBoxItem item && 
                        item.Tag?.ToString() == setting.ToString())
                    {
                        OnlineModeCombo.SelectedIndex = i;
                        break;
                    }
                }
                
                // Update status indicator
                UpdateOnlineStatusIndicator();
                
                // Subscribe to changes
                Core.OnlineModeManager.Instance.OnlineModeChanged += OnOnlineModeChanged;
                Core.OnlineModeManager.Instance.TemporaryAccessTick += OnTemporaryAccessTick;
                
                Debug.WriteLine($"[Settings] Online mode loaded: {setting}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] LoadOnlineModeSettings error: {ex.Message}");
            }
        }

        private void LoadContentLanguageSetting()
        {
            try
            {
                if (ContentLanguageCombo == null) return;
                var lang = (global::AtlasAI.Settings.SettingsStore.Current.PreferredContentLanguage ?? "en").Trim();
                if (string.IsNullOrWhiteSpace(lang)) lang = "en";
                for (int i = 0; i < ContentLanguageCombo.Items.Count; i++)
                {
                    if (ContentLanguageCombo.Items[i] is ComboBoxItem item &&
                        string.Equals(item.Tag?.ToString(), lang, StringComparison.OrdinalIgnoreCase))
                    {
                        ContentLanguageCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
            catch { }
        }
        
        private void OnlineMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            
            if (OnlineModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<Core.OnlineModeSetting>(tag, out var setting))
                {
                    Core.OnlineModeManager.Instance.Setting = setting;
                    UpdateOnlineStatusIndicator();
                    Debug.WriteLine($"[Settings] Online mode changed to: {setting}");
                }
            }
        }
        
        private void OnOnlineModeChanged(bool isActive)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateOnlineStatusIndicator();
            }));
        }
        
        private void OnTemporaryAccessTick(TimeSpan remaining)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (OnlineStatusBorder.Visibility == Visibility.Visible)
                {
                    OnlineStatusText.Text = $"Online access active · {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                }
            }));
        }
        
        private void UpdateOnlineStatusIndicator()
        {
            var manager = Core.OnlineModeManager.Instance;
            
            if (manager.IsOnlineAccessActive)
            {
                OnlineStatusBorder.Visibility = Visibility.Visible;
                OnlineStatusText.Text = manager.GetStatusText();
            }
            else
            {
                OnlineStatusBorder.Visibility = Visibility.Collapsed;
            }
        }
        
        #endregion

        private void ContentLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            try
            {
                if (ContentLanguageCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                {
                    var settings = global::AtlasAI.Settings.SettingsStore.Current;
                    settings.PreferredContentLanguage = tag;
                    global::AtlasAI.Settings.SettingsStore.Save(settings);
                    if (ContentLanguageStatus != null)
                        ContentLanguageStatus.Text = $"Set to {item.Content}";
                }
            }
            catch { }
        }
        
        #region Voice Input Event Handlers
        
        private void EnableWakeWord_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                prefs.EnableWakeWord = EnableWakeWordCheckBox.IsChecked == true;
                PreferencesStore.Instance.SavePreferences(prefs);
                
                // Update voice system
                _ = UpdateVoiceSystemAsync();
                
                // Update status
                UpdateVoiceStatus();
                
                Debug.WriteLine($"[Settings] Wake word enabled: {prefs.EnableWakeWord}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating wake word setting: {ex.Message}");
            }
        }
        
        private void EnableAlwaysListening_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                prefs.EnableAlwaysListening = EnableAlwaysListeningCheckBox.IsChecked == true;
                PreferencesStore.Instance.SavePreferences(prefs);
                
                // Update voice system
                _ = UpdateVoiceSystemAsync();
                
                // Update status
                UpdateVoiceStatus();
                
                Debug.WriteLine($"[Settings] Always listening enabled: {prefs.EnableAlwaysListening}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating always listening setting: {ex.Message}");
            }
        }
        
        private void WakeWordSensitivity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                prefs.WakeWordSensitivity = WakeWordSensitivitySlider.Value;
                PreferencesStore.Instance.SavePreferences(prefs);
                
                // Update display
                WakeWordSensitivityValue.Text = $"{(int)(prefs.WakeWordSensitivity * 100)}%";
                
                // Update voice system
                _ = UpdateVoiceSystemAsync();
                
                Debug.WriteLine($"[Settings] Wake word sensitivity: {prefs.WakeWordSensitivity}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating wake word sensitivity: {ex.Message}");
            }
        }
        
        private void ShowListeningIndicator_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                prefs.ShowListeningIndicator = ShowListeningIndicatorCheckBox.IsChecked == true;
                PreferencesStore.Instance.SavePreferences(prefs);
                
                Debug.WriteLine($"[Settings] Show listening indicator: {prefs.ShowListeningIndicator}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating listening indicator setting: {ex.Message}");
            }
        }
        
        private void FollowUpListeningDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                prefs.FollowUpListeningDuration = FollowUpListeningDurationSlider.Value;
                PreferencesStore.Instance.SavePreferences(prefs);
                
                // Update display
                FollowUpListeningDurationValue.Text = $"{(int)prefs.FollowUpListeningDuration}s";
                
                Debug.WriteLine($"[Settings] Follow-up listening duration: {prefs.FollowUpListeningDuration}s");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating follow-up listening duration: {ex.Message}");
            }
        }
        
        private void EnableWakeWordAudioCue_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                prefs.EnableWakeWordAudioCue = EnableWakeWordAudioCueCheckBox.IsChecked == true;
                PreferencesStore.Instance.SavePreferences(prefs);
                
                Debug.WriteLine($"[Settings] Wake word audio cue enabled: {prefs.EnableWakeWordAudioCue}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating wake word audio cue setting: {ex.Message}");
            }
        }
        
        private async Task UpdateVoiceSystemAsync()
        {
            try
            {
                await VoiceSystemOrchestrator.Instance.UpdateSettingsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error updating voice system: {ex.Message}");
            }
        }
        
        #endregion
    }
}
