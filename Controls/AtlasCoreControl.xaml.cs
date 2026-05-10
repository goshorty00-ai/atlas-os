using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AtlasAI.Controls
{
    public enum AtlasVisualState
    {
        Idle,
        Listening,
        Thinking,
        Speaking
    }
    
    public partial class AtlasCoreControl : UserControl
    {
        #region Dependency Properties
        
        public static readonly DependencyProperty StateProperty =
            DependencyProperty.Register(nameof(State), typeof(AtlasVisualState), typeof(AtlasCoreControl),
                new PropertyMetadata(AtlasVisualState.Idle, OnStateChanged));
        
        public AtlasVisualState State
        {
            get => (AtlasVisualState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }
        
        public static readonly DependencyProperty ShowStateLabelProperty =
            DependencyProperty.Register(nameof(ShowStateLabel), typeof(bool), typeof(AtlasCoreControl),
                new PropertyMetadata(true, OnShowStateLabelChanged));
        
        public bool ShowStateLabel
        {
            get => (bool)GetValue(ShowStateLabelProperty);
            set => SetValue(ShowStateLabelProperty, value);
        }
        
        public static readonly DependencyProperty SpeakingEnergyProperty =
            DependencyProperty.Register(nameof(SpeakingEnergy), typeof(double), typeof(AtlasCoreControl),
                new PropertyMetadata(0.0, OnSpeakingEnergyChanged));
        
        public double SpeakingEnergy
        {
            get => (double)GetValue(SpeakingEnergyProperty);
            set => SetValue(SpeakingEnergyProperty, Math.Clamp(value, 0.0, 1.0));
        }
        
        // Customization properties
        public static readonly DependencyProperty PrimaryColorProperty =
            DependencyProperty.Register(nameof(PrimaryColor), typeof(Color), typeof(AtlasCoreControl),
                new PropertyMetadata(Color.FromRgb(34, 211, 238), OnColorChanged)); // #22d3ee
        
        public Color PrimaryColor
        {
            get => (Color)GetValue(PrimaryColorProperty);
            set => SetValue(PrimaryColorProperty, value);
        }
        
        public static readonly DependencyProperty SecondaryColorProperty =
            DependencyProperty.Register(nameof(SecondaryColor), typeof(Color), typeof(AtlasCoreControl),
                new PropertyMetadata(Color.FromRgb(125, 211, 252), OnColorChanged)); // #7dd3fc
        
        public Color SecondaryColor
        {
            get => (Color)GetValue(SecondaryColorProperty);
            set => SetValue(SecondaryColorProperty, value);
        }
        
        public static readonly DependencyProperty ThinkingColorProperty =
            DependencyProperty.Register(nameof(ThinkingColor), typeof(Color), typeof(AtlasCoreControl),
                new PropertyMetadata(Color.FromRgb(249, 115, 22), OnColorChanged)); // #f97316
        
        public Color ThinkingColor
        {
            get => (Color)GetValue(ThinkingColorProperty);
            set => SetValue(ThinkingColorProperty, value);
        }
        
        public static readonly DependencyProperty AnimationSpeedProperty =
            DependencyProperty.Register(nameof(AnimationSpeed), typeof(double), typeof(AtlasCoreControl),
                new PropertyMetadata(1.0, OnAnimationSpeedChanged));
        
        public double AnimationSpeed
        {
            get => (double)GetValue(AnimationSpeedProperty);
            set => SetValue(AnimationSpeedProperty, Math.Clamp(value, 0.1, 3.0));
        }
        
        public static readonly DependencyProperty ParticleCountProperty =
            DependencyProperty.Register(nameof(ParticleCount), typeof(int), typeof(AtlasCoreControl),
                new PropertyMetadata(180, OnParticleCountChanged));
        
        public int ParticleCount
        {
            get => (int)GetValue(ParticleCountProperty);
            set => SetValue(ParticleCountProperty, Math.Clamp(value, 50, 300));
        }
        
        private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AtlasCoreControl control && control._isInitialized)
            {
                control.UpdateParticleColors();
            }
        }
        
        private static void OnAnimationSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Speed is applied in the animation tick
            Debug.WriteLine($"[AtlasCore] AnimationSpeed changed: {e.OldValue} -> {e.NewValue}");
        }
        
        private static void OnParticleCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AtlasCoreControl control && control._isInitialized)
            {
                control.RecreateParticles((int)e.NewValue);
            }
        }
        
        #endregion
        
        #region Particle Class
        
        private class Particle
        {
            public Ellipse Element { get; set; } = null!;
            public double X { get; set; }
            public double Y { get; set; }
            public double VelX { get; set; }
            public double VelY { get; set; }
            public double Size { get; set; }
            public double BaseOpacity { get; set; }
            public double TargetX { get; set; }
            public double TargetY { get; set; }
            public double OrbitAngle { get; set; }
            public double OrbitRadius { get; set; }
            public double OrbitSpeed { get; set; }
            public double PulsePhase { get; set; }
            public bool IsOrange { get; set; }
        }
        
        #endregion
        
        #region Fields
        
        private readonly List<Particle> _particles = new();
        private bool _isAnimating = false;
        private DispatcherTimer? _energyTimer;
        
        private AtlasVisualState _currentState = AtlasVisualState.Idle;
        private bool _isInitialized = false;
        private Random _random = new();
        private double _time = 0;
        private DateTime _lastFrameTime;
        
        private const int PARTICLE_COUNT = 120; // Fewer particles for smaller orb
        private const double CENTER_X = 70;
        private const double CENTER_Y = 70;
        
        // Colors
        private static readonly Color BluePrimary = (Color)ColorConverter.ConvertFromString("#22d3ee")!;
        private static readonly Color BlueLight = (Color)ColorConverter.ConvertFromString("#7dd3fc")!;
        private static readonly Color OrangePrimary = (Color)ColorConverter.ConvertFromString("#f97316")!;
        private static readonly Color OrangeLight = (Color)ColorConverter.ConvertFromString("#fdba74")!;
        
        // Animation parameters
        private double _convergence = 0.0; // 0 = spread out, 1 = converged to center
        private double _targetConvergence = 0.0;
        private double _orbitSpeedMultiplier = 1.0;
        private double _targetOrbitSpeed = 1.0;
        private bool _isOrangeMode = false;
        
        // Energy
        private double _rawEnergy = 0;
        private double _smoothedEnergy = 0;
        private bool _isEasingOut = false;
        private DateTime _easeOutStartTime;
        
        #endregion
        
        public AtlasCoreControl()
        {
            InitializeComponent();
            Loaded += AtlasCoreControl_Loaded;
        }
        
        private void AtlasCoreControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            
            try
            {
                CreateParticles();
                
                // Use CompositionTarget.Rendering for GPU-synced smooth animation
                _lastFrameTime = DateTime.Now;
                _isAnimating = true;
                
                // Subscribe to rendering events
                CompositionTarget.Rendering += OnCompositionTargetRendering;
                
                // Energy timer for speaking
                _energyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _energyTimer.Tick += EnergyTimer_Tick;
                
                ApplyState(State);
                _isInitialized = true;
                
                // Load saved orb settings AFTER initialization
                LoadSavedOrbSettings();
                
                Debug.WriteLine("[AtlasCoreControl] Particle system initialized with GPU rendering");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AtlasCoreControl] Error: {ex.Message}");
            }
        }
        
        private void OnCompositionTargetRendering(object? sender, EventArgs e)
        {
            if (!_isAnimating) return;
            
            // AGGRESSIVE throttling to 20fps to minimize interference with desktop orb (60fps)
            // This prevents both orbs from competing for the same UI thread
            // Desktop orb gets priority - chat orb runs at lower framerate
            var now = DateTime.Now;
            var elapsed = (now - _lastFrameTime).TotalSeconds;
            if (elapsed < 0.050) return; // Skip frame if less than 50ms (20fps)
            _lastFrameTime = now;
            
            // Use FIXED timestep for ultra-smooth animation
            const double FIXED_TIMESTEP = 0.0167; // 60fps = 16.7ms
            
            AnimationTick(FIXED_TIMESTEP);
        }
        
        /// <summary>
        /// Load saved orb settings from file
        /// </summary>
        private void LoadSavedOrbSettings()
        {
            try
            {
                var (colorPreset, orbStyle, speed, particles) = SettingsWindow.GetOrbSettings();
                Debug.WriteLine($"[AtlasCoreControl] Loading saved settings: color={colorPreset}, style={orbStyle}, speed={speed}, particles={particles}");
                
                ApplyColorPreset(colorPreset);
                AnimationSpeed = speed;
                ParticleCount = particles;
                
                Debug.WriteLine($"[AtlasCoreControl] Applied saved orb settings successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AtlasCoreControl] Error loading saved settings: {ex.Message}");
                // Use defaults if loading fails
                UpdateParticleColors();
            }
        }
        
        private void CreateParticles()
        {
            ParticleCanvas.Children.Clear();
            _particles.Clear();
            
            for (int i = 0; i < PARTICLE_COUNT; i++)
            {
                // Random starting position in a cloud around center
                double angle = _random.NextDouble() * Math.PI * 2;
                double radius = 20 + _random.NextDouble() * 70;
                double x = CENTER_X + Math.Cos(angle) * radius;
                double y = CENTER_Y + Math.Sin(angle) * radius;
                
                // Varied sizes - some small, some larger
                double size = 2 + _random.NextDouble() * 4;
                if (_random.NextDouble() < 0.15) size = 5 + _random.NextDouble() * 3; // Some bigger ones
                
                double opacity = 0.4 + _random.NextDouble() * 0.5;
                
                var ellipse = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(BluePrimary),
                    Opacity = opacity
                };
                
                // NO blur effects - pure GPU rendering for smooth animation
                
                Canvas.SetLeft(ellipse, x - size / 2);
                Canvas.SetTop(ellipse, y - size / 2);
                ParticleCanvas.Children.Add(ellipse);
                
                _particles.Add(new Particle
                {
                    Element = ellipse,
                    X = x,
                    Y = y,
                    VelX = (_random.NextDouble() - 0.5) * 0.5,
                    VelY = (_random.NextDouble() - 0.5) * 0.5,
                    Size = size,
                    BaseOpacity = opacity,
                    OrbitAngle = angle,
                    OrbitRadius = radius,
                    OrbitSpeed = 0.005 + _random.NextDouble() * 0.015,
                    PulsePhase = _random.NextDouble() * Math.PI * 2,
                    IsOrange = false
                });
            }
        }
        
        private void AnimationTick(double deltaTime)
        {
            // Frame-rate independent time increment - SMOOTH and consistent
            _time += deltaTime;
            
            // Ultra-smooth state transitions using exponential smoothing (frame-rate independent)
            double smoothFactor = 1.0 - Math.Pow(0.5, deltaTime * 30);
            _convergence = Lerp(_convergence, _targetConvergence, smoothFactor * 0.25);
            _orbitSpeedMultiplier = Lerp(_orbitSpeedMultiplier, _targetOrbitSpeed, smoothFactor * 0.35);
            
            // Calculate effective energy for speaking state
            double effectiveEnergy = 0;
            if (_currentState == AtlasVisualState.Speaking)
            {
                // DRAMATIC synthetic energy wave - multiple frequencies for organic feel
                double wave1 = 0.6 + 0.25 * Math.Sin(_time * 3.0);  // Base rhythm
                double wave2 = 0.15 * Math.Sin(_time * 7.0);         // Word rhythm
                double wave3 = 0.1 * Math.Sin(_time * 13.0);         // Syllable variation
                double combinedWave = wave1 + wave2 + wave3;
                effectiveEnergy = Math.Max(_smoothedEnergy, Math.Clamp(combinedWave, 0.3, 1.0));
            }
            
            // Smooth energy transitions
            _smoothedEnergy = Lerp(_smoothedEnergy, _rawEnergy, _rawEnergy > _smoothedEnergy ? smoothFactor * 2.5 : smoothFactor);
            
            // Pre-calculate common values
            double energyExpansion = effectiveEnergy * 40;
            double speedMult = _orbitSpeedMultiplier * (1.0 + effectiveEnergy * 1.5);
            
            // Base rotation speed - SMOOTH, frame-rate independent, no AnimationSpeed multiplier
            double baseRotation = _currentState == AtlasVisualState.Idle ? 0.08 : 0.5;
            baseRotation *= deltaTime; // Pure frame-rate independent rotation
            
            foreach (var p in _particles)
            {
                // Smooth orbit rotation - speed already factored into baseRotation
                p.OrbitAngle += p.OrbitSpeed * speedMult * baseRotation;
                
                // Calculate radius with smooth convergence
                double baseRadius = p.OrbitRadius + energyExpansion;
                double tightRadius = 6 + (p.PulsePhase * 2.0); // Deterministic tight radius
                double radius = Lerp(baseRadius, tightRadius, _convergence);
                
                // Target position on orbit
                double tx = CENTER_X + Math.Cos(p.OrbitAngle) * radius;
                double ty = CENTER_Y + Math.Sin(p.OrbitAngle) * radius;
                
                // Very smooth position interpolation (frame-rate independent)
                double posSmooth = _currentState == AtlasVisualState.Idle ? smoothFactor * 0.5 : smoothFactor * 1.2;
                p.X = Lerp(p.X, tx, posSmooth);
                p.Y = Lerp(p.Y, ty, posSmooth);
                
                // Gentle breathing drift (pure sine, no randomness)
                double breathe = _currentState == AtlasVisualState.Idle ? 0.5 : (1.0 + effectiveEnergy * 4.0);
                double dx = Math.Sin(_time * 0.2 + p.PulsePhase) * breathe;
                double dy = Math.Cos(_time * 0.15 + p.PulsePhase * 0.7) * breathe;
                
                // Apply position
                Canvas.SetLeft(p.Element, p.X + dx - p.Size * 0.5);
                Canvas.SetTop(p.Element, p.Y + dy - p.Size * 0.5);
                
                // Smooth opacity breathing
                double opacityWave = Math.Sin(_time * 0.5 + p.PulsePhase) * 0.06;
                p.Element.Opacity = Math.Clamp(p.BaseOpacity + opacityWave + effectiveEnergy * 0.2, 0.25, 1.0);
                
                // Smooth size scaling
                double targetScale = 1.0 + effectiveEnergy * 0.6;
                double currentScale = p.Element.Width / p.Size;
                double newScale = Lerp(currentScale, targetScale, smoothFactor * 1.5);
                p.Element.Width = p.Size * newScale;
                p.Element.Height = p.Size * newScale;
            }
            
            // Speaking glow animation
            if (_currentState == AtlasVisualState.Speaking)
            {
                double glowPulse = 0.7 + effectiveEnergy * 0.2 + Math.Sin(_time * 1.8) * 0.06;
                SpeakingGlow.Opacity = Math.Clamp(glowPulse, 0.5, 1.0);
                
                double glowSize = 50 * (1.0 + effectiveEnergy * 1.2);
                SpeakingGlow.Width = Lerp(SpeakingGlow.Width, glowSize, smoothFactor * 1.8);
                SpeakingGlow.Height = SpeakingGlow.Width;
            }
        }
        
        // Linear interpolation helper for smooth transitions
        private static double Lerp(double current, double target, double t)
        {
            return current + (target - current) * t;
        }

        #region State Machine
        
        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AtlasCoreControl control && control._isInitialized)
                control.ApplyState((AtlasVisualState)e.NewValue);
        }
        
        private static void OnShowStateLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AtlasCoreControl control)
                control.StateLabel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
        
        private static void OnSpeakingEnergyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AtlasCoreControl control)
            {
                control._rawEnergy = (double)e.NewValue;
                Debug.WriteLine($"[AtlasCore] Energy updated: {control._rawEnergy:F2}, State: {control._currentState}");
            }
        }
        
        private void ApplyState(AtlasVisualState newState)
        {
            var previousState = _currentState;
            _currentState = newState;
            
            Debug.WriteLine($"[AtlasCoreControl] State: {previousState} → {newState}");
            
            switch (newState)
            {
                case AtlasVisualState.Idle:
                    _targetConvergence = 0.0;
                    _targetOrbitSpeed = 1.0;
                    SetParticleColor(false);
                    ShowSpeakingGlow(false);
                    _rawEnergy = 0;
                    _smoothedEnergy = 0;
                    _energyTimer?.Stop(); // Stop energy processing
                    break;
                    
                case AtlasVisualState.Listening:
                    _targetConvergence = 0.3; // Slightly pulled in
                    _targetOrbitSpeed = 0.6;
                    SetParticleColor(false);
                    ShowSpeakingGlow(false);
                    break;
                    
                case AtlasVisualState.Thinking:
                    _targetConvergence = 0.85; // Pull tight to center
                    _targetOrbitSpeed = 3.0; // Fast spinning
                    SetParticleColor(true); // ORANGE
                    ShowSpeakingGlow(false);
                    break;
                    
                case AtlasVisualState.Speaking:
                    _targetConvergence = 0.2;
                    _targetOrbitSpeed = 1.3;
                    SetParticleColor(false);
                    _isEasingOut = false;
                    // DON'T reset smoothedEnergy - let it build up from external feed
                    // Show orange center glow IMMEDIATELY
                    ShowSpeakingGlow(true);
                    // Start energy timer for smooth energy processing
                    _energyTimer?.Start();
                    Debug.WriteLine($"[AtlasCoreControl] Speaking state - glow should be visible now, energy timer started");
                    break;
            }
            
            UpdateStateLabel(newState);
        }
        
        private void SetParticleColor(bool orange)
        {
            _isOrangeMode = orange;
            var color = orange ? ThinkingColor : PrimaryColor;
            
            foreach (var p in _particles)
            {
                if (p.IsOrange != orange)
                {
                    p.IsOrange = orange;
                    // Animate color change
                    var fromColor = p.IsOrange ? PrimaryColor : ThinkingColor;
                    var brush = new SolidColorBrush(fromColor);
                    var colorAnim = new ColorAnimation(color, TimeSpan.FromMilliseconds(300));
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                    p.Element.Fill = brush;
                }
            }
        }
        
        #endregion

        #region Energy System
        
        private void EnergyTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentState != AtlasVisualState.Speaking) return;
            
            try
            {
                if (_isEasingOut)
                {
                    var elapsed = (DateTime.Now - _easeOutStartTime).TotalMilliseconds;
                    var progress = Math.Min(1.0, elapsed / 500.0);
                    _smoothedEnergy *= (1.0 - progress);
                    
                    if (progress >= 1.0)
                    {
                        State = AtlasVisualState.Idle;
                        return;
                    }
                }
                else
                {
                    var processed = _rawEnergy < 0.08 ? 0 : Math.Min(1.0, _rawEnergy * 1.4);
                    
                    if (processed > _smoothedEnergy)
                        _smoothedEnergy += (processed - _smoothedEnergy) * 0.35;
                    else
                        _smoothedEnergy += (processed - _smoothedEnergy) * 0.08;
                }
            }
            catch { }
        }
        
        public void BeginSpeakingEaseOut()
        {
            if (_currentState != AtlasVisualState.Speaking) return;
            _isEasingOut = true;
            _easeOutStartTime = DateTime.Now;
        }
        
        #endregion

        #region Helpers
        
        private void UpdateStateLabel(AtlasVisualState state)
        {
            StateLabel.Text = state switch
            {
                AtlasVisualState.Idle => "",
                AtlasVisualState.Listening => "LISTENING",
                AtlasVisualState.Thinking => "PROCESSING",
                AtlasVisualState.Speaking => "SPEAKING",
                _ => ""
            };
        }
        
        private void ShowSpeakingGlow(bool show)
        {
            Debug.WriteLine($"[AtlasCoreControl] ShowSpeakingGlow({show})");
            if (show)
            {
                // Set immediately visible, then animate
                SpeakingGlow.Opacity = 0.8;
            }
            var anim = new DoubleAnimation(show ? 0.9 : 0, TimeSpan.FromMilliseconds(200));
            SpeakingGlow.BeginAnimation(OpacityProperty, anim);
        }
        
        private void UpdateSpeakingGlow()
        {
            if (_currentState != AtlasVisualState.Speaking) return;
            
            // Pulse the orange glow with voice energy - VERY VISIBLE
            double baseOpacity = 0.7;
            double energyBoost = _smoothedEnergy * 0.3;
            double pulse = Math.Sin(_time * 4) * 0.1;
            SpeakingGlow.Opacity = Math.Clamp(baseOpacity + energyBoost + pulse, 0.5, 1.0);
            
            // Scale with energy - BIGGER
            double baseSize = 50;
            double scale = 1.0 + _smoothedEnergy * 2.0;
            SpeakingGlow.Width = baseSize * scale;
            SpeakingGlow.Height = baseSize * scale;
        }
        
        #endregion
        
        #region Public Methods
        
        public void CycleState()
        {
            State = State switch
            {
                AtlasVisualState.Idle => AtlasVisualState.Listening,
                AtlasVisualState.Listening => AtlasVisualState.Thinking,
                AtlasVisualState.Thinking => AtlasVisualState.Speaking,
                AtlasVisualState.Speaking => AtlasVisualState.Idle,
                _ => AtlasVisualState.Idle
            };
        }
        
        public void SetIdle() => State = AtlasVisualState.Idle;
        public void SetListening() => State = AtlasVisualState.Listening;
        public void SetThinking() => State = AtlasVisualState.Thinking;
        public void SetSpeaking() 
        { 
            Debug.WriteLine("[AtlasCoreControl] SetSpeaking() called!");
            State = AtlasVisualState.Speaking; 
        }
        public void UpdateSpeakingEnergy(double amplitude) => SpeakingEnergy = amplitude;
        public void EndSpeaking() { if (_currentState == AtlasVisualState.Speaking) BeginSpeakingEaseOut(); }
        
        /// <summary>Update all particle colors to match current PrimaryColor</summary>
        public void UpdateParticleColors()
        {
            var color = _isOrangeMode ? ThinkingColor : PrimaryColor;
            foreach (var p in _particles)
            {
                var brush = new SolidColorBrush(color);
                p.Element.Fill = brush;
            }
        }
        
        /// <summary>Recreate particles with new count</summary>
        public void RecreateParticles(int count)
        {
            ParticleCanvas.Children.Clear();
            _particles.Clear();
            
            for (int i = 0; i < count; i++)
            {
                double angle = _random.NextDouble() * Math.PI * 2;
                double radius = 20 + _random.NextDouble() * 70;
                double x = CENTER_X + Math.Cos(angle) * radius;
                double y = CENTER_Y + Math.Sin(angle) * radius;
                
                double size = 2 + _random.NextDouble() * 4;
                if (_random.NextDouble() < 0.15) size = 5 + _random.NextDouble() * 3;
                
                double opacity = 0.4 + _random.NextDouble() * 0.5;
                
                var ellipse = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(PrimaryColor),
                    Opacity = opacity
                };
                
                // NO blur effects - pure GPU rendering for smooth animation
                
                Canvas.SetLeft(ellipse, x - size / 2);
                Canvas.SetTop(ellipse, y - size / 2);
                ParticleCanvas.Children.Add(ellipse);
                
                _particles.Add(new Particle
                {
                    Element = ellipse,
                    X = x,
                    Y = y,
                    VelX = (_random.NextDouble() - 0.5) * 0.5,
                    VelY = (_random.NextDouble() - 0.5) * 0.5,
                    Size = size,
                    BaseOpacity = opacity,
                    OrbitAngle = angle,
                    OrbitRadius = radius,
                    OrbitSpeed = 0.005 + _random.NextDouble() * 0.015,
                    PulsePhase = _random.NextDouble() * Math.PI * 2,
                    IsOrange = false
                });
            }
        }
        
        /// <summary>Apply a preset color theme</summary>
        public void ApplyColorPreset(string preset)
        {
            switch (preset.ToLower())
            {
                case "cyan": // Default
                    PrimaryColor = Color.FromRgb(34, 211, 238);
                    SecondaryColor = Color.FromRgb(125, 211, 252);
                    ThinkingColor = Color.FromRgb(249, 115, 22);
                    break;
                case "purple":
                    PrimaryColor = Color.FromRgb(168, 85, 247);
                    SecondaryColor = Color.FromRgb(196, 181, 253);
                    ThinkingColor = Color.FromRgb(236, 72, 153);
                    break;
                case "green":
                    PrimaryColor = Color.FromRgb(34, 197, 94);
                    SecondaryColor = Color.FromRgb(134, 239, 172);
                    ThinkingColor = Color.FromRgb(250, 204, 21);
                    break;
                case "red":
                    PrimaryColor = Color.FromRgb(239, 68, 68);
                    SecondaryColor = Color.FromRgb(252, 165, 165);
                    ThinkingColor = Color.FromRgb(249, 115, 22);
                    break;
                case "gold":
                    PrimaryColor = Color.FromRgb(234, 179, 8);
                    SecondaryColor = Color.FromRgb(253, 224, 71);
                    ThinkingColor = Color.FromRgb(249, 115, 22);
                    break;
                case "pink":
                    PrimaryColor = Color.FromRgb(236, 72, 153);
                    SecondaryColor = Color.FromRgb(249, 168, 212);
                    ThinkingColor = Color.FromRgb(168, 85, 247);
                    break;
            }
            UpdateParticleColors();
        }
        
        #endregion
    }
}
