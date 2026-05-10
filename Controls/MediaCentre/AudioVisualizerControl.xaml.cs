using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Dsp;
using NAudio.Wave;

namespace AtlasAI.Controls.MediaCentre
{
    public partial class AudioVisualizerControl : UserControl
    {
        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register(
                nameof(IsPlaying),
                typeof(bool),
                typeof(AudioVisualizerControl),
                new PropertyMetadata(false, OnIsPlayingChanged));

        private readonly DispatcherTimer _timer;
        private EventHandler? _renderHandler;
        private readonly List<double> _currentHeights = new();
        private readonly List<Rectangle> _bars = new();
        private Brush? _barBrush;
        private WasapiLoopbackCapture? _capture;
        private readonly object _audioLock = new();
        private float[] _spectrum = Array.Empty<float>();
        private readonly float[] _fftBuffer = new float[2048];
        private int _fftPos;
        private readonly int _fftLength = 2048;
        private readonly int _m = 11;

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        public AudioVisualizerControl()
        {
            InitializeComponent();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _timer.Tick += (_, _) => { /* keep timer alive so Unloaded stops it; actual updates use Rendering */ };

            Loaded += (_, _) =>
            {
                EnsureBars();
                UpdateTimerState();
                SetAllBars(4);
            };

            Unloaded += (_, _) =>
            {
                if (_timer.IsEnabled) _timer.Stop();
                if (_renderHandler != null)
                {
                    CompositionTarget.Rendering -= _renderHandler;
                    _renderHandler = null;
                }
                StopCapture();
            };
        }

        private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AudioVisualizerControl c) return;
            c.UpdateTimerState();
            if (!c.IsPlaying) c.SetAllBars(4);
        }

        private void EnsureBars()
        {
            if (_bars.Count > 0) return;

            _barBrush = CreateBarBrush();

            for (int i = 0; i < 64; i++)
            {
                var rect = new Rectangle
                {
                    Width = 3,
                    Height = 4,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = _barBrush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0.5, 0, 0.5, 0)
                };

                _bars.Add(rect);
                BarHost.Children.Add(rect);
            }

            _spectrum = new float[_bars.Count];
        }

        private static Brush CreateBarBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 1),
                EndPoint = new Point(0.5, 0)
            };

            // Neon blue → neon orange → neon blue
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xD4, 0xFF), 0.0));  // neon blue
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x7A, 0x00), 0.55)); // neon orange
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0xD3, 0xEE), 1.0));  // cyan-blue
            brush.Freeze();
            return brush;
        }

        private void UpdateTimerState()
        {
            if (!IsLoaded) return;

            if (IsPlaying)
            {
                StartCapture();
                if (_renderHandler == null)
                {
                    _renderHandler = (_, __) => Tick();
                    CompositionTarget.Rendering += _renderHandler;
                }
                if (!_timer.IsEnabled) _timer.Start();
                return;
            }

            if (_renderHandler != null)
            {
                CompositionTarget.Rendering -= _renderHandler;
                _renderHandler = null;
            }
            if (_timer.IsEnabled) _timer.Stop();
            StopCapture();
        }

        private void StartCapture()
        {
            try
            {
                lock (_audioLock)
                {
                    if (_capture != null) return;
                    _fftPos = 0;
                    Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
                    Array.Clear(_spectrum, 0, _spectrum.Length);

                    _capture = new WasapiLoopbackCapture();
                    _capture.DataAvailable += Capture_DataAvailable;
                    _capture.RecordingStopped += Capture_RecordingStopped;
                    _capture.StartRecording();
                }
            }
            catch
            {
                StopCapture();
            }
        }

        private void StopCapture()
        {
            try
            {
                lock (_audioLock)
                {
                    if (_capture == null) return;
                    try { _capture.DataAvailable -= Capture_DataAvailable; } catch { }
                    try { _capture.RecordingStopped -= Capture_RecordingStopped; } catch { }
                    try { _capture.StopRecording(); } catch { }
                    try { _capture.Dispose(); } catch { }
                    _capture = null;
                    _fftPos = 0;
                    if (_spectrum.Length > 0) Array.Clear(_spectrum, 0, _spectrum.Length);
                }
            }
            catch
            {
            }
        }

        private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            StopCapture();
        }

        private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                if (!IsPlaying) return;
                var bytes = e.BytesRecorded;
                if (bytes <= 0) return;

                lock (_audioLock)
                {
                    if (_capture == null) return;
                    var wf = _capture.WaveFormat;
                    var channels = Math.Max(1, wf.Channels);
                    var bits = wf.BitsPerSample;
                    var bytesPerSample = Math.Max(1, bits / 8);
                    var blockAlign = wf.BlockAlign > 0 ? wf.BlockAlign : (channels * bytesPerSample);

                    for (int i = 0; i + blockAlign <= bytes; i += blockAlign)
                    {
                        float sum = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            var o = i + (ch * bytesPerSample);
                            float s;
                            if (wf.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                            {
                                s = BitConverter.ToSingle(e.Buffer, o);
                            }
                            else if (wf.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 2)
                            {
                                s = BitConverter.ToInt16(e.Buffer, o) / 32768f;
                            }
                            else if (wf.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 3)
                            {
                                var sample24 = (e.Buffer[o + 2] << 16) | (e.Buffer[o + 1] << 8) | e.Buffer[o];
                                if ((sample24 & 0x800000) != 0) sample24 |= unchecked((int)0xFF000000);
                                s = sample24 / 8388608f;
                            }
                            else if (wf.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 4)
                            {
                                s = BitConverter.ToInt32(e.Buffer, o) / (float)int.MaxValue;
                            }
                            else
                            {
                                s = 0;
                            }
                            sum += s;
                        }

                        var sample = sum / channels;
                        _fftBuffer[_fftPos++] = sample;
                        if (_fftPos >= _fftLength)
                        {
                            ProcessFft();
                            _fftPos = 0;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void ProcessFft()
        {
            if (_spectrum.Length == 0) return;

            var fft = new Complex[_fftLength];
            for (int i = 0; i < _fftLength; i++)
            {
                var window = (float)FastFourierTransform.HannWindow(i, _fftLength);
                fft[i].X = _fftBuffer[i] * window;
                fft[i].Y = 0;
            }

            FastFourierTransform.FFT(true, _m, fft);

            var bins = _fftLength / 2;
            for (int i = 0; i < _spectrum.Length; i++)
            {
                var frac = (i + 1f) / _spectrum.Length;
                var start = (int)Math.Clamp(MathF.Pow(frac, 2.3f) * bins, 1, bins - 2);
                var end = (int)Math.Clamp(MathF.Pow((i + 2f) / _spectrum.Length, 2.3f) * bins, start + 1, bins - 1);

                float max = 0;
                for (int b = start; b < end; b++)
                {
                    var re = fft[b].X;
                    var im = fft[b].Y;
                    var mag = (float)Math.Sqrt((re * re) + (im * im));
                    if (mag > max) max = mag;
                }

                var db = 20f * (float)Math.Log10(max + 1e-9f);
                var scaled = (db + 70f) / 70f;
                scaled = Math.Clamp(scaled, 0f, 1f);
                _spectrum[i] = (float)(_spectrum[i] * 0.70 + scaled * 0.30);
            }
        }

        private void Tick()
        {
            if (!IsPlaying) return;
            if (_bars.Count == 0) return;

            if (_currentHeights.Count != _bars.Count)
            {
                _currentHeights.Clear();
                for (int i = 0; i < _bars.Count; i++) _currentHeights.Add(_bars[i].Height);
            }

            float[] snapshot;
            lock (_audioLock)
            {
                snapshot = (float[])_spectrum.Clone();
            }

            for (int i = 0; i < _bars.Count; i++)
            {
                var baseMin = 4d;
                var max = 68d;
                var v = snapshot.Length > i ? snapshot[i] : 0f;
                var target = baseMin + (v * (max - baseMin));
                var current = _currentHeights[i];
                var smoothed = current + (target - current) * 0.25;
                _currentHeights[i] = smoothed;
                _bars[i].BeginAnimation(HeightProperty, null);
                _bars[i].Height = smoothed;
            }
        }

        private void SetAllBars(double height)
        {
            foreach (var b in _bars)
            {
                b.BeginAnimation(HeightProperty, null);
                b.Height = height;
            }
        }

        private static void AnimateHeight(FrameworkElement element, double to, int ms)
        {
            var anim = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(ms),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            element.BeginAnimation(HeightProperty, anim, HandoffBehavior.Compose);
        }
    }
}
