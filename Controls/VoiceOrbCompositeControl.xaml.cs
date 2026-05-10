using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AtlasAI.Controls
{
    public partial class VoiceOrbCompositeControl : UserControl
    {
        public static readonly DependencyProperty EnableAnimationProperty =
            DependencyProperty.Register(nameof(EnableAnimation), typeof(bool), typeof(VoiceOrbCompositeControl),
                new PropertyMetadata(true, OnEnableAnimationChanged));

        public static readonly DependencyProperty PresenceLevelProperty =
            DependencyProperty.Register(nameof(PresenceLevel), typeof(double), typeof(VoiceOrbCompositeControl),
                new PropertyMetadata(0.3, OnPresenceChanged));

        public static readonly DependencyProperty ThinkingLevelProperty =
            DependencyProperty.Register(nameof(ThinkingLevel), typeof(double), typeof(VoiceOrbCompositeControl),
                new PropertyMetadata(0.0, OnThinkingChanged));

        public static readonly DependencyProperty RingSpeedProperty =
            DependencyProperty.Register(nameof(RingSpeed), typeof(double), typeof(VoiceOrbCompositeControl),
                new PropertyMetadata(0.3, OnRingSpeedChanged));

        public static readonly DependencyProperty VoiceAmplitudeProperty =
            DependencyProperty.Register(nameof(VoiceAmplitude), typeof(double), typeof(VoiceOrbCompositeControl),
                new PropertyMetadata(0.0, OnVoiceAmplitudeChanged));

        public bool EnableAnimation
        {
            get => (bool)GetValue(EnableAnimationProperty);
            set => SetValue(EnableAnimationProperty, value);
        }

        public double PresenceLevel
        {
            get => (double)GetValue(PresenceLevelProperty);
            set => SetValue(PresenceLevelProperty, value);
        }

        public double ThinkingLevel
        {
            get => (double)GetValue(ThinkingLevelProperty);
            set => SetValue(ThinkingLevelProperty, value);
        }

        public double RingSpeed
        {
            get => (double)GetValue(RingSpeedProperty);
            set => SetValue(RingSpeedProperty, value);
        }

        public double VoiceAmplitude
        {
            get => (double)GetValue(VoiceAmplitudeProperty);
            set => SetValue(VoiceAmplitudeProperty, Math.Clamp(value, 0.0, 1.0));
        }

        public VoiceOrbCompositeControl()
        {
            InitializeComponent();
            Loaded += VoiceOrbCompositeControl_Loaded;
        }

        private void VoiceOrbCompositeControl_Loaded(object sender, RoutedEventArgs e)
        {
            Core.AnimationSpeed = Math.Max(0.1, RingSpeed * 2.0);
            Core.ParticleCount = 180;
            Core.State = AtlasVisualState.Idle;
        }

        public void SetState(AtlasVisualState state)
        {
            Core.State = state;
        }

        private static void OnEnableAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VoiceOrbCompositeControl c)
            {
                // no-op for AtlasCoreControl
            }
        }

        private static void OnPresenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VoiceOrbCompositeControl c)
            {
                // map presence to animation speed subtly
                c.Core.AnimationSpeed = 0.5 + (double)e.NewValue;
            }
        }

        private static void OnThinkingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VoiceOrbCompositeControl c)
            {
                // no-op for AtlasCoreControl
            }
        }

        private static void OnRingSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VoiceOrbCompositeControl c)
            {
                c.Core.AnimationSpeed = 0.5 + (double)e.NewValue * 1.5;
            }
        }

        private static void OnVoiceAmplitudeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VoiceOrbCompositeControl c)
            {
                var v = Math.Clamp(Convert.ToDouble(e.NewValue), 0.0, 1.0);
                c.Core.SpeakingEnergy = v;
                c.Core.State = v > 0.05 ? AtlasVisualState.Speaking : AtlasVisualState.Idle;
            }
        }
    }
}
