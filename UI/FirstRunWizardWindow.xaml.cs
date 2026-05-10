using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using AtlasAI.Core;

namespace AtlasAI.UI
{
    public partial class FirstRunWizardWindow : Window
    {
        private sealed class MicItem
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
        }

        private readonly List<MicItem> _mics = new();
        private int _stepIndex = 0; // 0..4

        private WasapiCapture? _capture;
        private float _lastLevel;
        private DateTime _lastUiUpdateUtc = DateTime.MinValue;
        private bool _allowClose = false;

        public FirstRunWizardWindow()
        {
            InitializeComponent();

            BanterSlider.ValueChanged += (_, __) =>
            {
                BanterValueText.Text = ((int)Math.Round(BanterSlider.Value)).ToString();
            };

            LoadFromPreferences();
            LoadMicrophones();
            SetStep(0);
        }

        private void LoadFromPreferences()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                NameTextBox.Text = prefs.ChatPreferredName ?? "";
                AccentTextBox.Text = prefs.ChatAccent ?? "";
                BanterSlider.Value = Math.Clamp(prefs.ChatBanterLevel, 1, 5);
            }
            catch
            {
            }
        }

        private void LoadMicrophones()
        {
            _mics.Clear();
            MicCombo.ItemsSource = null;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var d in devices)
                {
                    var id = d.ID ?? "";
                    var name = d.FriendlyName ?? "(microphone)";
                    if (!string.IsNullOrWhiteSpace(id))
                        _mics.Add(new MicItem { Id = id, Name = name });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FirstRunWizard] Mic enumeration failed: {ex.Message}");
            }

            if (_mics.Count == 0)
            {
                MicHintText.Text = "No microphones detected.";
                ToggleMicTestBtn.IsEnabled = false;
            }
            else
            {
                MicHintText.Text = "";
                MicCombo.ItemsSource = _mics;

                try
                {
                    var prefs = PreferencesStore.Instance.Current;
                    var preferredId = prefs.MicrophoneDeviceId ?? "";
                    if (!string.IsNullOrWhiteSpace(preferredId) && _mics.Any(m => string.Equals(m.Id, preferredId, StringComparison.OrdinalIgnoreCase)))
                    {
                        MicCombo.SelectedValue = preferredId;
                    }
                    else
                    {
                        MicCombo.SelectedIndex = 0;
                    }
                }
                catch
                {
                    MicCombo.SelectedIndex = 0;
                }
            }
        }

        private void SetStep(int index)
        {
            _stepIndex = Math.Clamp(index, 0, 4);

            Step1Panel.Visibility = _stepIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _stepIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = _stepIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step4Panel.Visibility = _stepIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            Step5Panel.Visibility = _stepIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

            StepText.Text = $"Step {_stepIndex + 1}/5";
            TitleText.Text = _stepIndex switch
            {
                0 => "Welcome to Atlas AI",
                1 => "Accent Preference",
                2 => "Banter Level",
                3 => "Default Microphone",
                4 => "Test Microphone",
                _ => "Welcome to Atlas AI"
            };

            BackBtn.IsEnabled = _stepIndex > 0;
            NextBtn.Visibility = _stepIndex < 4 ? Visibility.Visible : Visibility.Collapsed;
            FinishBtn.Visibility = _stepIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

            if (_stepIndex != 4)
            {
                StopMicTest();
            }
            else
            {
                ToggleMicTestBtn.IsEnabled = MicCombo.SelectedValue is string s && !string.IsNullOrWhiteSpace(s);
                MicTestStatusText.Text = "";
                MicLevelBar.Value = 0;
                MicLevelText.Text = "0%";
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => SetStep(_stepIndex - 1);

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_stepIndex == 0)
            {
                // Name is allowed to be blank; keep moving.
            }

            if (_stepIndex == 3 && _mics.Count > 0 && MicCombo.SelectedValue == null)
                MicCombo.SelectedIndex = 0;

            SetStep(_stepIndex + 1);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            StopMicTest();
            _allowClose = true;
            DialogResult = false;
            Close();
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            StopMicTest();

            var name = (NameTextBox.Text ?? "").Trim();
            var accent = (AccentTextBox.Text ?? "").Trim();
            var banter = (int)Math.Round(BanterSlider.Value);

            var micId = MicCombo.SelectedValue as string ?? "";
            var micName = _mics.FirstOrDefault(m => string.Equals(m.Id, micId, StringComparison.OrdinalIgnoreCase))?.Name ?? "";

            try
            {
                PreferencesStore.Instance.Update(p =>
                {
                    p.ChatPreferredName = name;
                    p.ChatAccent = accent;
                    p.ChatBanterLevel = banter;
                    p.MicrophoneDeviceId = micId;
                    p.MicrophoneDevice = micName;
                    p.FirstRunWizardComplete = true;
                    p.ChatOnboardingComplete = true;
                });
                PreferencesStore.Instance.SaveNow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FirstRunWizard] Save failed: {ex.Message}");
            }

            _allowClose = true;
            DialogResult = true;
            Close();
        }

        private void MicCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ToggleMicTestBtn.IsEnabled = MicCombo.SelectedValue is string s && !string.IsNullOrWhiteSpace(s);
            StopMicTest();
        }

        private void ToggleMicTest_Click(object sender, RoutedEventArgs e)
        {
            if (_capture == null)
                StartMicTest();
            else
                StopMicTest();
        }

        private void StartMicTest()
        {
            StopMicTest();

            var micId = MicCombo.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(micId))
                return;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(micId);
                if (device == null)
                {
                    MicTestStatusText.Text = "Selected mic not available.";
                    return;
                }

                _capture = new WasapiCapture(device);
                _capture.DataAvailable += Capture_DataAvailable;
                _capture.RecordingStopped += Capture_RecordingStopped;
                _capture.StartRecording();

                ToggleMicTestBtn.Content = "Stop Test";
                MicTestStatusText.Text = "Listening…";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FirstRunWizard] Mic test start failed: {ex.Message}");
                MicTestStatusText.Text = "Failed to start mic test.";
                StopMicTest();
            }
        }

        private void StopMicTest()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= Capture_DataAvailable;
                    _capture.RecordingStopped -= Capture_RecordingStopped;
                    _capture.StopRecording();
                    _capture.Dispose();
                    _capture = null;
                }
            }
            catch
            {
                try
                {
                    _capture?.Dispose();
                }
                catch
                {
                }
                _capture = null;
            }

            ToggleMicTestBtn.Content = "Start Test";
            MicTestStatusText.Text = "";
        }

        private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ToggleMicTestBtn.Content = "Start Test";
                if (e.Exception != null)
                    MicTestStatusText.Text = "Mic test stopped (error).";
            });
        }

        private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            var cap = _capture;
            if (cap == null) return;

            try
            {
                var level = ComputeLevel01(e.Buffer, e.BytesRecorded, cap.WaveFormat);
                _lastLevel = Math.Max(_lastLevel * 0.8f, level);

                var now = DateTime.UtcNow;
                if ((now - _lastUiUpdateUtc).TotalMilliseconds < 50)
                    return;
                _lastUiUpdateUtc = now;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var v = Math.Clamp(_lastLevel, 0f, 1f);
                    var pct = (int)Math.Round(v * 100);
                    MicLevelBar.Value = pct;
                    MicLevelText.Text = $"{pct}%";
                }));
            }
            catch
            {
            }
        }

        private static float ComputeLevel01(byte[] buffer, int bytes, WaveFormat waveFormat)
        {
            if (bytes <= 0) return 0;

            // Common cases for WasapiCapture: 32-bit float or 16-bit PCM
            if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                int samples = bytes / 4;
                if (samples <= 0) return 0;
                double sumSquares = 0;
                for (int i = 0; i < samples; i++)
                {
                    float sample = BitConverter.ToSingle(buffer, i * 4);
                    sumSquares += sample * sample;
                }
                var rms = Math.Sqrt(sumSquares / samples);
                return (float)Math.Clamp(rms, 0.0, 1.0);
            }

            if (waveFormat.BitsPerSample == 16)
            {
                int samples = bytes / 2;
                if (samples <= 0) return 0;
                double sumSquares = 0;
                for (int i = 0; i < samples; i++)
                {
                    short sample = BitConverter.ToInt16(buffer, i * 2);
                    var v = sample / 32768.0;
                    sumSquares += v * v;
                }
                var rms = Math.Sqrt(sumSquares / samples);
                return (float)Math.Clamp(rms, 0.0, 1.0);
            }

            return 0;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_allowClose)
            {
                // Treat window close (X) as cancel
                e.Cancel = true;
                Cancel_Click(this, new RoutedEventArgs());
                return;
            }

            StopMicTest();
        }
    }
}
