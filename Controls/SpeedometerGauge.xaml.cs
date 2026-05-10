using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AtlasAI.Controls
{
    public partial class SpeedometerGauge : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(SpeedometerGauge),
                new PropertyMetadata(0d, OnValueChanged));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(SpeedometerGauge),
                new PropertyMetadata(""));

        public static readonly DependencyProperty ValueTextProperty =
            DependencyProperty.Register(nameof(ValueText), typeof(string), typeof(SpeedometerGauge),
                new PropertyMetadata("0.0%"));

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(SpeedometerGauge),
                new PropertyMetadata("OPTIMAL"));

        public static readonly DependencyProperty StatusBrushProperty =
            DependencyProperty.Register(nameof(StatusBrush), typeof(Brush), typeof(SpeedometerGauge),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)), OnStatusBrushChanged));

        public static readonly DependencyProperty IconGeometryProperty =
            DependencyProperty.Register(nameof(IconGeometry), typeof(Geometry), typeof(SpeedometerGauge),
                new PropertyMetadata(Geometry.Empty));

        public static readonly DependencyProperty MonoFontFamilyProperty =
            DependencyProperty.Register(nameof(MonoFontFamily), typeof(FontFamily), typeof(SpeedometerGauge),
                new PropertyMetadata(new FontFamily("Cascadia Code, Consolas")));

        private static readonly DependencyProperty ArcValueProperty =
            DependencyProperty.Register(nameof(ArcValue), typeof(double), typeof(SpeedometerGauge),
                new PropertyMetadata(0d, OnArcValueChanged));

        private static readonly DependencyProperty NeedleValueProperty =
            DependencyProperty.Register(nameof(NeedleValue), typeof(double), typeof(SpeedometerGauge),
                new PropertyMetadata(0d, OnNeedleValueChanged));

        private double ArcValue
        {
            get => (double)GetValue(ArcValueProperty);
            set => SetValue(ArcValueProperty, value);
        }

        private double NeedleValue
        {
            get => (double)GetValue(NeedleValueProperty);
            set => SetValue(NeedleValueProperty, value);
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string ValueText
        {
            get => (string)GetValue(ValueTextProperty);
            set => SetValue(ValueTextProperty, value);
        }

        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        public Brush StatusBrush
        {
            get => (Brush)GetValue(StatusBrushProperty);
            set => SetValue(StatusBrushProperty, value);
        }

        public Geometry IconGeometry
        {
            get => (Geometry)GetValue(IconGeometryProperty);
            set => SetValue(IconGeometryProperty, value);
        }

        public FontFamily MonoFontFamily
        {
            get => (FontFamily)GetValue(MonoFontFamilyProperty);
            set => SetValue(MonoFontFamilyProperty, value);
        }

        public SpeedometerGauge()
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                UpdateArc(ArcValue);
                UpdateNeedle(NeedleValue);
                ApplyStatusBrush();
            };
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SpeedometerGauge g) return;
            var target = ClampPercent((double)e.NewValue);

            var arcAnim = new DoubleAnimation(g.ArcValue, target, TimeSpan.FromMilliseconds(1000))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            g.BeginAnimation(ArcValueProperty, arcAnim);

            var needleAnim = new DoubleAnimation(g.NeedleValue, target, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            g.BeginAnimation(NeedleValueProperty, needleAnim);
        }

        private static void OnArcValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SpeedometerGauge g) return;
            g.UpdateArc((double)e.NewValue);
        }

        private static void OnNeedleValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SpeedometerGauge g) return;
            g.UpdateNeedle((double)e.NewValue);
        }

        private void UpdateArc(double value)
        {
            try
            {
                var pct = ClampPercent(value);
                var filled = Math.Max(0.0001, pct);
                var remaining = Math.Max(0.0001, 100d - pct);
                ProgressArc.StrokeDashArray = new DoubleCollection { filled, remaining };
                ProgressArc.StrokeDashOffset = 0;
            }
            catch
            {
            }
        }

        private void UpdateNeedle(double value)
        {
            try
            {
                var pct = ClampPercent(value);
                var angle = (pct / 100d) * 180d - 90d;
                NeedleRotate.Angle = angle;
            }
            catch
            {
            }
        }

        private static double ClampPercent(double value) => Math.Max(0d, Math.Min(100d, value));

        private static void OnStatusBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SpeedometerGauge g) return;
            g.ApplyStatusBrush();
        }

        private void ApplyStatusBrush()
        {
            try
            {
                if (StatusBrush is not SolidColorBrush sb) return;
                NeedleLine.Stroke = sb;
                IconPath.Fill = sb;
                MidStop.Color = sb.Color;
            }
            catch
            {
            }
        }
    }
}

