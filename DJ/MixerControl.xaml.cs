using System;
using System.Windows;
using System.Windows.Controls;

namespace AtlasAI.DJ
{
    public partial class MixerControl : UserControl
    {
        private AudioEngine? _engine;
        public event Action<double>? MasterChanged;
        public event Action<double>? CrossfaderChanged;
        public event Action<double>? CueMixChanged;
        public MixerControl()
        {
            InitializeComponent();
            Crossfader.ValueChanged += Crossfader_ValueChanged;
            Master.ValueChanged += Master_ValueChanged;
            CueMix.ValueChanged += CueMix_ValueChanged;
            SyncBtn.Click += SyncBtn_Click;
            FxEcho.Click += (_, __) => ToggleFx(() => _engine!.EffectEcho, v => _engine!.EffectEcho = v, FxEcho);
            FxReverb.Click += (_, __) => ToggleFx(() => _engine!.EffectReverb, v => _engine!.EffectReverb = v, FxReverb);
            FxFlanger.Click += (_, __) => ToggleFx(() => _engine!.EffectFlanger, v => _engine!.EffectFlanger = v, FxFlanger);
            FxDelay.Click += (_, __) => ToggleFx(() => _engine!.EffectDelay, v => _engine!.EffectDelay = v, FxDelay);
            FxFilter.Click += (_, __) => ToggleFx(() => _engine!.EffectFilter, v => _engine!.EffectFilter = v, FxFilter);
            FxPhaser.Click += (_, __) => ToggleFx(() => _engine!.EffectPhaser, v => _engine!.EffectPhaser = v, FxPhaser);
        }

        public void Bind(AudioEngine engine)
        {
            _engine = engine;
            _engine.DeckA.PropertyChanged += (_, __) => UpdateVu();
            _engine.DeckB.PropertyChanged += (_, __) => UpdateVu();
            CrossFill.Value = Crossfader.Value;
        }


        private void Crossfader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_engine == null) return;
            _engine.SetCrossfader(e.NewValue);
            CrossFill.Value = e.NewValue;
            CrossfaderChanged?.Invoke(e.NewValue);
        }


        private void Master_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_engine == null) return;
            _engine.MasterVolume = e.NewValue;
            _engine.SetVolumes();
            MasterChanged?.Invoke(e.NewValue);
        }

        private void CueMix_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _engine?.SetCueMix(e.NewValue);
            CueMixChanged?.Invoke(e.NewValue);
        }

        private void SyncBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            var a = _engine.DeckA.Track?.Bpm ?? 0;
            var b = _engine.DeckB.Track?.Bpm ?? 0;
            if (b > 0)
            {
                var tempoAdjust = (a - b) / (double)b * 100.0;
                _engine.DeckB.Tempo = tempoAdjust / 12.5; // approximate
            }
        }

        private void ToggleFx(Func<bool> get, Action<bool> set, Button btn)
        {
            if (_engine == null) return;
            var newVal = !get();
            set(newVal);
            btn.Background = new System.Windows.Media.SolidColorBrush(newVal ? System.Windows.Media.Color.FromRgb(33, 48, 65) : System.Windows.Media.Color.FromRgb(20, 18, 26));
            btn.BorderBrush = new System.Windows.Media.SolidColorBrush(newVal ? System.Windows.Media.Color.FromRgb(168, 85, 247) : System.Windows.Media.Colors.Transparent);
            btn.BorderThickness = new System.Windows.Thickness(newVal ? 1 : 0);
            btn.Effect = newVal
                ? new System.Windows.Media.Effects.DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(168, 85, 247), BlurRadius = 16, Opacity = 0.6, ShadowDepth = 0 }
                : null;
        }
        private void UpdateVu()
        {
            if (_engine == null) return;
            var left = _engine.DeckA.Volume * (100 - _engine.Crossfader) / 50.0 * _engine.MasterVolume / 100.0;
            var right = _engine.DeckB.Volume * (_engine.Crossfader) / 50.0 * _engine.MasterVolume / 100.0;
            var l = Math.Clamp(left, 0, 100);
            var r = Math.Clamp(right, 0, 100);
            var meterHeight = 128.0;
            VuLeftFill.Height = meterHeight * l / 100.0;
            VuRightFill.Height = meterHeight * r / 100.0;
        }
    }
}
