using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AtlasAI.Core;
using AtlasAI.Tools;
using AtlasAI.SecuritySuite;

namespace AtlasAI.Controls
{
    /// <summary>
    /// HoloCoreControl - ANIMATED orb with XAML animations - NO MEMORY LEAK
    /// CRITICAL: Uses XAML Storyboard animations instead of render loops
    /// </summary>
    public partial class HoloCoreControl : UserControl
    {
        #region Properties
        public static readonly DependencyProperty VoiceAmplitudeProperty =
            DependencyProperty.Register(nameof(VoiceAmplitude), typeof(double), typeof(HoloCoreControl),
                new PropertyMetadata(0.0, OnVoiceAmplitudeChanged));

        public static readonly DependencyProperty IsSpeakingProperty =
            DependencyProperty.Register(nameof(IsSpeaking), typeof(bool), typeof(HoloCoreControl),
                new PropertyMetadata(false, OnSpeakingChanged));

        public static readonly DependencyProperty PresenceLevelProperty =
            DependencyProperty.Register(nameof(PresenceLevel), typeof(double), typeof(HoloCoreControl),
                new PropertyMetadata(0.3));

        public double PresenceLevel
        {
            get => (double)GetValue(PresenceLevelProperty);
            set => SetValue(PresenceLevelProperty, Math.Clamp(value, 0, 1));
        }

        // Animation control properties
        public bool EnableAnimation { get; set; } = true;
        public double ThinkingLevel { get; set; } = 0.0;
        public double RingSpeed { get; set; } = 1.0;
        public int ParticleCount { get; set; } = 0;

        public double VoiceAmplitude
        {
            get => (double)GetValue(VoiceAmplitudeProperty);
            set => SetValue(VoiceAmplitudeProperty, Math.Clamp(value, 0, 1));
        }

        public bool IsSpeaking
        {
            get => (bool)GetValue(IsSpeakingProperty);
            set => SetValue(IsSpeakingProperty, value);
        }

        #endregion

        #region Animation Resources

        private Storyboard? _ringRotationStoryboard;
        private Storyboard? _breathingStoryboard;
        private readonly System.Collections.Generic.List<Storyboard> _particleStoryboards = new System.Collections.Generic.List<Storyboard>();
        private readonly System.Collections.Generic.List<(System.Windows.Shapes.Ellipse e, TranslateTransform t, double r, double phase)> _particles = new System.Collections.Generic.List<(System.Windows.Shapes.Ellipse, TranslateTransform, double, double)>();
        private readonly Random _rand = new Random();

        #endregion

        #region Constructor

        public HoloCoreControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        #endregion

        #region Lifecycle

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[HoloCoreControl] ═══════════════════════════════════════");
            Debug.WriteLine("[HoloCoreControl] OnLoaded() - Starting ANIMATED orb initialization");
            
            try
            {
                if (EnableAnimation)
                {
                    CreateAnimations();
                    StartAnimations();
                    Debug.WriteLine("[HoloCoreControl] [OK] ANIMATED ORB INITIALIZATION COMPLETE");
                    Debug.WriteLine("[HoloCoreControl] [INFO] Using XAML animations - No memory leak");
                }
                else
                {
                    Debug.WriteLine("[HoloCoreControl] [INFO] Animations disabled - Static orb");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HoloCoreControl] [ERROR] Animation initialization failed: {ex.Message}");
                Debug.WriteLine($"[HoloCoreControl] [ERROR] Stack trace: {ex.StackTrace}");
                throw; // Re-throw to see the full error
            }
            
            Debug.WriteLine("[HoloCoreControl] ═══════════════════════════════════════");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[HoloCoreControl] OnUnloaded() - Stopping animations");
            StopAnimations();
        }

        #endregion

        #region Animation Management

        private void CreateAnimations()
        {
            // Create ring rotation storyboard
            _ringRotationStoryboard = new Storyboard();

            // Outer ring - 20 second clockwise rotation
            var outerRotation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(20),
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(outerRotation, OuterRingTransform);
            Storyboard.SetTargetProperty(outerRotation, new PropertyPath(RotateTransform.AngleProperty));
            _ringRotationStoryboard.Children.Add(outerRotation);

            // Middle ring - 30 second clockwise rotation
            var middleRotation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(30),
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(middleRotation, MiddleRingTransform);
            Storyboard.SetTargetProperty(middleRotation, new PropertyPath(RotateTransform.AngleProperty));
            _ringRotationStoryboard.Children.Add(middleRotation);

            // Inner ring - 15 second counter-clockwise rotation
            var innerRotation = new DoubleAnimation
            {
                From = 0,
                To = -360,
                Duration = TimeSpan.FromSeconds(15),
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(innerRotation, InnerRingTransform);
            Storyboard.SetTargetProperty(innerRotation, new PropertyPath(RotateTransform.AngleProperty));
            _ringRotationStoryboard.Children.Add(innerRotation);

            // Create breathing animation storyboard
            _breathingStoryboard = new Storyboard();

            // Center core breathing - 3 second opacity pulse
            var breathingAnimation = new DoubleAnimation
            {
                From = 0.6,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(3),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(breathingAnimation, CenterCore);
            Storyboard.SetTargetProperty(breathingAnimation, new PropertyPath(UIElement.OpacityProperty));
            _breathingStoryboard.Children.Add(breathingAnimation);

            Debug.WriteLine("[HoloCoreControl] Ring rotation animations created");
            Debug.WriteLine("[HoloCoreControl] Breathing animation created");
        }

        private void StartAnimations()
        {
            try
            {
                _ringRotationStoryboard?.Begin();
                _breathingStoryboard?.Begin();
                Debug.WriteLine("[HoloCoreControl] Ring rotation animations started");
                Debug.WriteLine("[HoloCoreControl] Breathing animation started");
            BuildParticles(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HoloCoreControl] [ERROR] Failed to start animations: {ex.Message}");
                throw;
            }
        }

        private static void OnVoiceAmplitudeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HoloCoreControl core)
            {
                try
                {
                    double amp = Math.Clamp(Convert.ToDouble(e.NewValue), 0.0, 1.0);
                    double coreScale = 1.0 + (amp * 0.35);
                    double dotScale = 1.0 + (amp * 1.0);

                    core.CenterCoreScale.ScaleX = coreScale;
                    core.CenterCoreScale.ScaleY = coreScale;
                    core.CoreDotScale.ScaleX = dotScale;
                    core.CoreDotScale.ScaleY = dotScale;

                    if (core.IsSpeaking)
                    {
                        core.AmbientField.Opacity = GetAmbientOpacity(speaking: true);
                        core.InnerRing.Stroke = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
                        core.CoreDot.Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
                        for (int i = 0; i < core._particles.Count; i++)
                        {
                            var p = core._particles[i];
                            p.e.Opacity = 0.5 + amp * 0.5;
                        }
                    }
                    else
                    {
                        core.AmbientField.Opacity = GetAmbientOpacity(speaking: false);
                        core.InnerRing.Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0x22, 0xD3, 0xEE));
                        core.CoreDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
                        for (int i = 0; i < core._particles.Count; i++)
                        {
                            var p = core._particles[i];
                            p.e.Opacity = 0.4 + amp * 0.3;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HoloCoreControl] VoiceAmplitude update error: {ex.Message}");
                }
            }
        }

        private static void OnSpeakingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HoloCoreControl core)
            {
                bool speaking = (bool)e.NewValue;
                try
                {
                    core._ringRotationStoryboard?.Stop();
                    var sb = new Storyboard();

                    var outerRotation = new DoubleAnimation
                    {
                        From = 0,
                        To = 360,
                        Duration = TimeSpan.FromSeconds(speaking ? 6 : 12),
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    Storyboard.SetTarget(outerRotation, core.OuterRingTransform);
                    Storyboard.SetTargetProperty(outerRotation, new PropertyPath(RotateTransform.AngleProperty));
                    sb.Children.Add(outerRotation);

                    var middleRotation = new DoubleAnimation
                    {
                        From = 0,
                        To = 360,
                        Duration = TimeSpan.FromSeconds(speaking ? 10 : 20),
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    Storyboard.SetTarget(middleRotation, core.MiddleRingTransform);
                    Storyboard.SetTargetProperty(middleRotation, new PropertyPath(RotateTransform.AngleProperty));
                    sb.Children.Add(middleRotation);

                    var innerRotation = new DoubleAnimation
                    {
                        From = 0,
                        To = -360,
                        Duration = TimeSpan.FromSeconds(speaking ? 7.5 : 15),
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    Storyboard.SetTarget(innerRotation, core.InnerRingTransform);
                    Storyboard.SetTargetProperty(innerRotation, new PropertyPath(RotateTransform.AngleProperty));
                    sb.Children.Add(innerRotation);

                    core._ringRotationStoryboard = sb;
                    sb.Begin();

                    core.AmbientField.Opacity = GetAmbientOpacity(speaking);
                    core.BuildParticles(speaking);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HoloCoreControl] Speaking state update error: {ex.Message}");
                }
            }
        }

        private static double GetAmbientOpacity(bool speaking)
        {
            var opacity = speaking ? 0.45 : 0.30;
            try
            {
                if (PreferencesStore.Instance.Current.AmbientDimEnabled)
                    opacity *= 0.55;
            }
            catch
            {
            }
            return opacity;
        }
        private void BuildParticles(bool speaking)
        {
            try
            {
                ParticleLayer.Children.Clear();
                foreach (var sb in _particleStoryboards) { try { sb.Stop(); } catch { } }
                _particleStoryboards.Clear();
                _particles.Clear();

                double w = HoloCoreRoot.ActualWidth > 0 ? HoloCoreRoot.ActualWidth : 320.0;
                double center = w / 2.0;
                double maxR = Math.Max(10.0, center - 8.0);
                double minR = Math.Max(6.0, maxR * 0.35);
                int count = (int)Math.Clamp(w * 1.1, 120, 240);
                for (int i = 0; i < count; i++)
                {
                    double size = (w / 120.0) * (1.6 + _rand.NextDouble() * 2.0);
                    double radius = minR + _rand.NextDouble() * (maxR - minR);
                    double phase = _rand.NextDouble() * Math.PI * 2.0;

                    var el = new System.Windows.Shapes.Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = new SolidColorBrush(speaking && i % 3 == 0 ? Color.FromRgb(0xF9, 0x73, 0x16) : Color.FromRgb(0x22, 0xD3, 0xEE)),
                        Opacity = 0.95
                    };
                    el.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = speaking && i % 3 == 0 ? Color.FromRgb(0xF9, 0x73, 0x16) : Color.FromRgb(0x22, 0xD3, 0xEE),
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.5
                    };
                    var tt = new TranslateTransform();
                    el.RenderTransform = tt;
                    Canvas.SetLeft(el, center);
                    Canvas.SetTop(el, center);
                    ParticleLayer.Children.Add(el);
                    _particles.Add((el, tt, radius, phase));

                    var sb = new Storyboard();
                    var dur = TimeSpan.FromSeconds(speaking ? 6 + _rand.NextDouble() * 3 : 12 + _rand.NextDouble() * 5);

                    var xAnim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = dur };
                    var yAnim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = dur };
                    int segments = 8;
                    for (int k = 0; k <= segments; k++)
                    {
                        double a = phase + (Math.PI * 2.0 * k / segments);
                        double jitter = speaking ? (_rand.NextDouble() * 10.0 - 5.0) : (_rand.NextDouble() * 4.0 - 2.0);
                        double x = Math.Cos(a) * radius + jitter;
                        double y = Math.Sin(a) * radius + jitter;
                        var t = new TimeSpan((long)(dur.Ticks * (double)k / segments));
                        xAnim.KeyFrames.Add(new EasingDoubleKeyFrame(x, KeyTime.FromTimeSpan(t)));
                        yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(y, KeyTime.FromTimeSpan(t)));
                    }
                    Storyboard.SetTarget(xAnim, tt);
                    Storyboard.SetTargetProperty(xAnim, new PropertyPath(TranslateTransform.XProperty));
                    Storyboard.SetTarget(yAnim, tt);
                    Storyboard.SetTargetProperty(yAnim, new PropertyPath(TranslateTransform.YProperty));
                    sb.Children.Add(xAnim);
                    sb.Children.Add(yAnim);
                    _particleStoryboards.Add(sb);
                    sb.Begin();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HoloCoreControl] Particle build error: {ex.Message}");
            }
        }
        private void StopAnimations()
        {
            try
            {
                _ringRotationStoryboard?.Stop();
                _breathingStoryboard?.Stop();
                foreach (var sb in _particleStoryboards) { try { sb.Stop(); } catch { } }
                _particleStoryboards.Clear();
                Debug.WriteLine("[HoloCoreControl] Animations stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HoloCoreControl] [ERROR] Failed to stop animations: {ex.Message}");
            }
        }

        #endregion

        #region Action Button Handlers

        public void OnVolumeActionClick(object sender, RoutedEventArgs e)
        {
            _ = SystemTool.ToggleMuteAsync();
            Debug.WriteLine("[HoloCoreControl] Volume toggled");
        }

        public void OnSettingsActionClick(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new AtlasAI.SettingsWindow();
            settingsWindow.Show();
            Debug.WriteLine("[HoloCoreControl] Settings window opened");
        }

        public void OnSecurityActionClick(object sender, RoutedEventArgs e)
        {
            var securityWindow = new SecuritySuiteWindow();
            securityWindow.Show();
            Debug.WriteLine("[HoloCoreControl] Security suite opened");
        }

        public void OnNetworkActionClick(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[HoloCoreControl] Network scan requested (Not implemented)");
        }

        public void OnPerformanceActionClick(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[HoloCoreControl] Performance optimization requested (Not implemented)");
        }

        public void OnMicActionClick(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[HoloCoreControl] Microphone toggle requested (Not implemented)");
        }

        #endregion
    }
}
