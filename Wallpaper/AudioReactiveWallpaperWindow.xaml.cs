using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using NAudio.Wave;

namespace AtlasAI.Wallpaper
{
    public partial class AudioReactiveWallpaperWindow : Window
    {
        private WasapiLoopbackCapture? _capture;
        private readonly object _audioLock = new();
        private float _rms;
        private readonly List<Particle> _particles = new();
        private DateTime _lastFrameUtc = DateTime.UtcNow;
        private EventHandler? _renderHandler;

        private RadialGradientBrush? _brush;
        private GradientStop? _hotStop;
        private GradientStop? _midStop;
        private GradientStop? _edgeStop;

        public AudioReactiveWallpaperWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                PlaceOnPrimaryScreen();
                EnsureBrush();
                EnsureParticles();
                StartCapture();

                var hwnd = new WindowInteropHelper(this).Handle;
                _ = DesktopWallpaperHost.TryAttachToDesktop(hwnd);

                if (_renderHandler == null)
                {
                    _renderHandler = (_, __) => Tick();
                    CompositionTarget.Rendering += _renderHandler;
                }
            }
            catch
            {
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            try
            {
                if (_renderHandler != null)
                {
                    CompositionTarget.Rendering -= _renderHandler;
                    _renderHandler = null;
                }
            }
            catch
            {
            }

            StopCapture();
        }

        private void PlaceOnPrimaryScreen()
        {
            var s = SystemParameters.WorkArea;
            Left = s.Left;
            Top = s.Top;
            Width = s.Width;
            Height = s.Height;
        }

        private void EnsureBrush()
        {
            if (_brush != null) return;

            _hotStop = new GradientStop(Color.FromArgb(140, 34, 211, 238), 0.0);
            _midStop = new GradientStop(Color.FromArgb(90, 255, 122, 0), 0.45);
            _edgeStop = new GradientStop(Color.FromArgb(255, 10, 14, 20), 1.0);

            _brush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.45),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.85,
                RadiusY = 0.85
            };
            _brush.GradientStops.Add(_hotStop);
            _brush.GradientStops.Add(_midStop);
            _brush.GradientStops.Add(_edgeStop);
            _brush.FreezeIfPossible();

            Backdrop.Fill = _brush;
        }

        private void EnsureParticles()
        {
            if (_particles.Count > 0) return;

            var rng = new Random();
            for (var i = 0; i < 96; i++)
            {
                var p = new Particle
                {
                    X = rng.NextDouble(),
                    Y = rng.NextDouble(),
                    VX = (rng.NextDouble() - 0.5) * 0.02,
                    VY = (rng.NextDouble() - 0.5) * 0.02,
                    R = 1.0 + rng.NextDouble() * 2.4,
                    BaseOpacity = 0.05 + rng.NextDouble() * 0.08,
                    Hue = rng.NextDouble()
                };

                var ellipse = new Ellipse
                {
                    Width = p.R * 2,
                    Height = p.R * 2,
                    Fill = new SolidColorBrush(Color.FromArgb(255, 34, 211, 238)),
                    Opacity = p.BaseOpacity,
                    IsHitTestVisible = false
                };
                p.Ellipse = ellipse;
                _particles.Add(p);
                ParticleHost.Children.Add(ellipse);
            }
        }

        private void Tick()
        {
            var now = DateTime.UtcNow;
            var dt = (now - _lastFrameUtc).TotalSeconds;
            if (dt <= 0) dt = 1.0 / 60.0;
            _lastFrameUtc = now;

            var a = GetRms();
            var intensity = Math.Clamp(a * 8.0, 0, 1);

            UpdateBrush(intensity, now);
            UpdateParticles(intensity, dt, now);
        }

        private void UpdateBrush(double intensity, DateTime now)
        {
            if (_hotStop == null || _midStop == null || _edgeStop == null) return;
            if (_brush == null) return;

            var t = now.TimeOfDay.TotalSeconds;
            var wobble = 0.5 + 0.5 * Math.Sin(t * 0.35);
            var cx = 0.5 + (Math.Sin(t * 0.13) * 0.04);
            var cy = 0.5 + (Math.Cos(t * 0.11) * 0.035);

            var hotAlpha = (byte)Math.Clamp(60 + intensity * 170, 0, 255);
            var midAlpha = (byte)Math.Clamp(30 + intensity * 110, 0, 255);

            var hot = Color.FromArgb(hotAlpha, 34, 211, 238);
            var mid = Color.FromArgb(midAlpha, 255, 122, 0);
            var edge = Color.FromArgb(255, 10, 14, 20);

            _hotStop.Color = hot;
            _midStop.Color = mid;
            _edgeStop.Color = edge;

            _brush.Center = new Point(cx, cy);
            _brush.GradientOrigin = new Point(cx, cy - 0.06);
            _brush.RadiusX = 0.78 + wobble * 0.18 + intensity * 0.10;
            _brush.RadiusY = 0.78 + wobble * 0.18 + intensity * 0.10;
        }

        private void UpdateParticles(double intensity, double dt, DateTime now)
        {
            if (Width <= 0 || Height <= 0) return;
            var w = Width;
            var h = Height;
            var t = now.TimeOfDay.TotalSeconds;

            foreach (var p in _particles)
            {
                var n = 0.35 + intensity * 1.2;
                var ax = Math.Sin((t + p.Hue * 12.0) * 0.8) * 0.003 * n;
                var ay = Math.Cos((t + p.Hue * 10.0) * 0.7) * 0.003 * n;

                p.VX = (p.VX + ax) * (1.0 - (0.08 * dt));
                p.VY = (p.VY + ay) * (1.0 - (0.08 * dt));

                p.X += p.VX * dt;
                p.Y += p.VY * dt;

                if (p.X < -0.05) p.X = 1.05;
                if (p.X > 1.05) p.X = -0.05;
                if (p.Y < -0.05) p.Y = 1.05;
                if (p.Y > 1.05) p.Y = -0.05;

                var x = p.X * w;
                var y = p.Y * h;
                Canvas.SetLeft(p.Ellipse, x);
                Canvas.SetTop(p.Ellipse, y);

                var pulse = 0.6 + 0.4 * Math.Sin((t * 0.9) + p.Hue * 6.28);
                p.Ellipse.Opacity = Math.Clamp(p.BaseOpacity + intensity * 0.18 * pulse, 0, 0.4);
            }
        }

        private void StartCapture()
        {
            try
            {
                lock (_audioLock)
                {
                    if (_capture != null) return;
                    _rms = 0;
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
                    _rms = 0;
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
                lock (_audioLock)
                {
                    if (_capture == null) return;
                    var wf = _capture.WaveFormat;
                    var bytes = e.BytesRecorded;
                    if (bytes <= 0) return;

                    var channels = Math.Max(1, wf.Channels);
                    var bytesPerSample = Math.Max(1, wf.BitsPerSample / 8);
                    var blockAlign = wf.BlockAlign > 0 ? wf.BlockAlign : (channels * bytesPerSample);

                    double sumSq = 0;
                    var frames = 0;

                    for (int i = 0; i + blockAlign <= bytes; i += blockAlign)
                    {
                        float sum = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            var o = i + (ch * bytesPerSample);
                            float s;
                            if (wf.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                                s = BitConverter.ToSingle(e.Buffer, o);
                            else if (wf.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 2)
                                s = BitConverter.ToInt16(e.Buffer, o) / 32768f;
                            else if (wf.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 4)
                                s = BitConverter.ToInt32(e.Buffer, o) / (float)int.MaxValue;
                            else
                                s = 0;
                            sum += s;
                        }

                        var sample = sum / channels;
                        sumSq += sample * sample;
                        frames++;
                    }

                    if (frames > 0)
                    {
                        var rms = Math.Sqrt(sumSq / frames);
                        _rms = (float)(_rms * 0.86 + rms * 0.14);
                    }
                }
            }
            catch
            {
            }
        }

        private double GetRms()
        {
            lock (_audioLock) return _rms;
        }

        private sealed class Particle
        {
            public double X;
            public double Y;
            public double VX;
            public double VY;
            public double R;
            public double BaseOpacity;
            public double Hue;
            public Ellipse Ellipse = null!;
        }
    }

    internal static class FreezableExtensions
    {
        public static void FreezeIfPossible(this Freezable f)
        {
            if (f.CanFreeze) f.Freeze();
        }
    }
}
