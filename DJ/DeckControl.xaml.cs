using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Linq;

namespace AtlasAI.DJ
{
    public partial class DeckControl : UserControl
    {
        private DeckState? _deck;
        private AudioEngine? _engine;
        private bool _isLeft;
        public string Theme { get; set; } = "Cyan";

        public DeckControl()
        {
            InitializeComponent();
            LoadBtn.Click += OnLoad;
            PlayBtn.Click += OnPlay;
            PauseBtn.Click += OnPause;
            VolSlider.ValueChanged += OnVolChanged;
            TempoSlider.ValueChanged += OnTempoChanged;
            EqHigh.ValueChanged += (_, e) => EqHighFill.Value = e.NewValue;
            EqMid.ValueChanged += (_, e) => EqMidFill.Value = e.NewValue;
            EqLow.ValueChanged += (_, e) => EqLowFill.Value = e.NewValue;
            CueIntroBtn.Click += (_, __) => JumpTo(0.05);
            CueDropBtn.Click += (_, __) => JumpTo(0.35);
            CueBreakBtn.Click += (_, __) => JumpTo(0.65);
            CueBuildBtn.Click += (_, __) => JumpTo(0.85);
            LoopInBtn.Click += OnLoopIn;
            LoopOutBtn.Click += OnLoopOut;
            LoopClearBtn.Click += OnLoopClear;
        }

        public void Bind(DeckState deck, AudioEngine engine, bool isLeft)
        {
            _deck = deck;
            _engine = engine;
            _isLeft = isLeft;
            DeckTitle.Text = isLeft ? "DECK A" : "DECK B";
            CueIntroBtn.Content = isLeft ? "INTRO" : "START";
            CueDropBtn.Content = isLeft ? "DROP" : "VERSE";
            CueBreakBtn.Content = isLeft ? "BREAK" : "CHORUS";
            CueBuildBtn.Content = isLeft ? "BUILD" : "OUTRO";
            var glowColor = isLeft ? System.Windows.Media.Color.FromRgb(34, 211, 238) : System.Windows.Media.Color.FromRgb(249, 115, 22);
            var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = glowColor, BlurRadius = 30, Opacity = 0.4, ShadowDepth = 0 };
            CardTop.Effect = glow;
            CardMid.Effect = glow;
            CardBottom.Effect = glow;
            _deck.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeckState.CurrentTime))
                {
                    Dispatcher.Invoke(() =>
                    {
                        Progress.Value = _deck.CurrentTime;
                        Wave.SetPlayhead(_deck.CurrentTime);
                    });
                }
                if (e.PropertyName == nameof(DeckState.LoopStart) || e.PropertyName == nameof(DeckState.LoopEnd) || e.PropertyName == nameof(DeckState.LoopActive))
                {
                    Dispatcher.Invoke(() =>
                    {
                        Wave.SetLoopMarkers(_deck.LoopStart, _deck.LoopEnd);
                    });
                }
            };
            var bars = isLeft ? _engine.DeckAWaveformBars : _engine.DeckBWaveformBars;
            Wave.SetBars(bars, Theme);
            var accent = isLeft ? System.Windows.Media.Color.FromRgb(34, 211, 238) : System.Windows.Media.Color.FromRgb(249, 115, 22);
            Resources["AtlasAccentBrush"] = new System.Windows.Media.SolidColorBrush(accent);
            PlayGlow.Color = accent;
            EqHighFill.Value = EqHigh.Value;
            EqMidFill.Value = EqMid.Value;
            EqLowFill.Value = EqLow.Value;
            VolFill.Value = VolSlider.Value;
            _deck.Volume = VolSlider.Value;
            _engine.SetVolumes();
            TempoFill.Value = TempoSlider.Value + 8;

            var cueBrush = new System.Windows.Media.SolidColorBrush(accent);
            var cues = new System.Collections.Generic.List<(double pct, string label, System.Windows.Media.Brush brush)>();
            if (isLeft)
            {
                cues.Add((5, "INTRO", cueBrush));
                cues.Add((35, "DROP", cueBrush));
                cues.Add((65, "BREAK", cueBrush));
                cues.Add((85, "BUILD", cueBrush));
            }
            else
            {
                cues.Add((5, "START", cueBrush));
                cues.Add((35, "VERSE", cueBrush));
                cues.Add((65, "CHORUS", cueBrush));
                cues.Add((90, "OUTRO", cueBrush));
            }
            Wave.SetCueMarkers(cues);
        }

        public void SetWaveMuteLevel(double level)
        {
            Wave.SetMuteLevel(level);
        }

        private void OnLoad(object? sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Audio|*.mp3;*.wav;*.wma",
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
            {
                if (_isLeft) _engine?.LoadA(dlg.FileName, 128, "Am", System.IO.Path.GetFileNameWithoutExtension(dlg.FileName), "");
                else _engine?.LoadB(dlg.FileName, 126, "Dm", System.IO.Path.GetFileNameWithoutExtension(dlg.FileName), "");
                UpdateTrackLabels();
            }
        }

        private void UpdateTrackLabels()
        {
            if (_deck == null) return;
            TrackTitle.Text = _deck.Track.Name;
            DeckInfo.Text = $"{_deck.Track.Bpm} BPM • {_deck.Track.Key}";
            var bpm = _deck.Track.Bpm > 0 ? $"{_deck.Track.Bpm} BPM" : "";
            var key = string.IsNullOrWhiteSpace(_deck.Track.Key) ? "" : _deck.Track.Key;
            var dur = string.IsNullOrWhiteSpace(_deck.Track.Duration) ? "" : _deck.Track.Duration;
            var right = string.Join(" • ", new[] { bpm, key, dur }.Where(s => !string.IsNullOrWhiteSpace(s)));
            TrackMeta.Text = $"{_deck.Track.Artist}{(string.IsNullOrWhiteSpace(right) ? "" : " • " + right)}";
            DeckBpm.Text = (_deck.Track.Bpm > 0 ? _deck.Track.Bpm.ToString() : "0");
            DeckDuration.Text = dur;
        }

        private void OnPlay(object? sender, RoutedEventArgs e)
        {
            if (_isLeft) _engine?.PlayA(); else _engine?.PlayB();
            StartPlatterAnimation();
        }

        private void OnPause(object? sender, RoutedEventArgs e)
        {
            if (_isLeft) _engine?.PauseA(); else _engine?.PauseB();
            StopPlatterAnimation();
        }

        private void OnVolChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_deck == null || _engine == null) return;
            _deck.Volume = e.NewValue;
            _engine.SetVolumes();
            VolFill.Value = e.NewValue;
        }

        private void OnTempoChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_deck == null) return;
            _deck.Tempo = e.NewValue;
            _engine?.UpdateTempo();
            TempoFill.Value = e.NewValue + 8;
        }

        private void JumpTo(double pct)
        {
            if (_engine == null) return;
            if (_isLeft) _engine.SeekA(pct * 100); else _engine.SeekB(pct * 100);
        }

        private void OnLoopIn(object? sender, RoutedEventArgs e)
        {
            if (_deck == null) return;
            _deck.LoopStart = _deck.CurrentTime;
            _deck.LoopActive = false;
            Wave.SetLoopMarkers(_deck.LoopStart, _deck.LoopEnd);
        }
        private void OnLoopOut(object? sender, RoutedEventArgs e)
        {
            if (_deck == null) return;
            _deck.LoopEnd = _deck.CurrentTime;
            if (_deck.LoopStart.HasValue && _deck.LoopEnd.HasValue)
                _deck.LoopActive = true;
            Wave.SetLoopMarkers(_deck.LoopStart, _deck.LoopEnd);
        }
        private void OnLoopClear(object? sender, RoutedEventArgs e)
        {
            if (_deck == null) return;
            _deck.LoopStart = null;
            _deck.LoopEnd = null;
            _deck.LoopActive = false;
            Wave.SetLoopMarkers(null, null);
        }

        private void StartPlatterAnimation()
        {
            var dur1 = new System.Windows.Duration(System.TimeSpan.FromSeconds(1.2));
            var anim1 = new System.Windows.Media.Animation.DoubleAnimation(0, 360, dur1)
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            PlatterRot.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, anim1);
            var dur2 = new System.Windows.Duration(System.TimeSpan.FromSeconds(0.9));
            var anim2 = new System.Windows.Media.Animation.DoubleAnimation(0, 360, dur2)
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            RingRot.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, anim2);

            // Pulse the play button glow
            var glowAnim = new System.Windows.Media.Animation.DoubleAnimation(5, 25, new System.Windows.Duration(System.TimeSpan.FromSeconds(0.8)))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            PlayGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, glowAnim);
            var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(0.6, 1.0, new System.Windows.Duration(System.TimeSpan.FromSeconds(0.8)))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            PlayGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, opacityAnim);
        }

        private void StopPlatterAnimation()
        {
            PlatterRot.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            RingRot.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            PlayGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, null);
            PlayGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
            PlayGlow.BlurRadius = 0;
        }

    }
}
