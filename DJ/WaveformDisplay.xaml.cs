using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AtlasAI.DJ
{
    public partial class WaveformDisplay : UserControl
    {
        private double[] _bars = Array.Empty<double>();
        private Brush _barBrush = new SolidColorBrush(Color.FromRgb(34, 211, 238));
        private Brush _barMutedBrush = new SolidColorBrush(Color.FromRgb(64, 72, 82));
        private double _muteLevel = 0.0; // 0 = normal, 1 = fully muted
        private double? _loopStartPct;
        private double? _loopEndPct;
        private System.Collections.Generic.List<(double pct, string label, Brush brush)> _cues = new();

        public WaveformDisplay()
        {
            InitializeComponent();
            SizeChanged += (_, __) => Redraw();
        }

        public void SetBars(double[] bars, string theme)
        {
            _bars = bars ?? Array.Empty<double>();
            if (theme.Equals("Orange", StringComparison.OrdinalIgnoreCase))
            {
                _barBrush = new LinearGradientBrush(
                    Color.FromRgb(249, 115, 22),
                    Color.FromRgb(220, 38, 38),
                    90);
            }
            else
            {
                _barBrush = new LinearGradientBrush(
                    Color.FromRgb(34, 211, 238),
                    Color.FromRgb(37, 99, 235),
                    90);
            }
            Redraw();
        }

        public void SetMuteLevel(double level)
        {
            _muteLevel = Math.Clamp(level, 0, 1);
            Redraw();
        }

        public void SetLoopMarkers(double? startPct, double? endPct)
        {
            _loopStartPct = startPct;
            _loopEndPct = endPct;
            Redraw();
        }

        public void SetCueMarkers(System.Collections.Generic.IEnumerable<(double pct, string label, Brush brush)> cues)
        {
            _cues = new System.Collections.Generic.List<(double pct, string label, Brush brush)>(cues ?? Array.Empty<(double, string, Brush)>());
            Redraw();
        }

        public void SetPlayhead(double pct)
        {
            pct = Math.Clamp(pct, 0, 100);
            var x = ActualWidth * pct / 100.0;
            Canvas.SetLeft(Playhead, x);
            Playhead.Height = ActualHeight;
        }

        private void Redraw()
        {
            BarsCanvas.Children.Clear();
            MarkersCanvas.Children.Clear();
            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 0 || h <= 0 || _bars.Length == 0) return;

            var count = _bars.Length;
            var barWidth = Math.Max(2, w / count - 1);
            for (int i = 0; i < count; i++)
            {
                var val = Math.Clamp(_bars[i], 0, 1);
                var bh = val * h;
                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = bh,
                    Fill = (_muteLevel > 0.01 ? _barMutedBrush : _barBrush),
                    RadiusX = 1,
                    RadiusY = 1
                };
                rect.Opacity = 1.0 - 0.5 * _muteLevel;
                var x = i * (barWidth + 1);
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, h - bh);
                BarsCanvas.Children.Add(rect);
            }

            if (_loopStartPct.HasValue && _loopEndPct.HasValue)
            {
                var s = Math.Min(_loopStartPct.Value, _loopEndPct.Value);
                var e = Math.Max(_loopStartPct.Value, _loopEndPct.Value);
                var sx = w * s / 100.0;
                var ex = w * e / 100.0;
                LoopArea.Visibility = Visibility.Visible;
                LoopArea.Height = h;
                LoopArea.Width = Math.Max(1, ex - sx);
                Canvas.SetLeft(LoopArea, sx);
            }
            else
            {
                LoopArea.Visibility = Visibility.Collapsed;
            }

            foreach (var c in _cues)
            {
                var x = w * Math.Clamp(c.pct, 0, 100) / 100.0;
                var line = new Rectangle { Width = 2, Height = h, Fill = c.brush, Opacity = 0.9 };
                Canvas.SetLeft(line, x);
                MarkersCanvas.Children.Add(line);
                var label = new TextBlock
                {
                    Text = c.label,
                    Foreground = c.brush,
                    FontSize = 10,
                    Margin = new Thickness(4, 4, 0, 0)
                };
                Canvas.SetLeft(label, Math.Min(x + 4, w - 40));
                Canvas.SetTop(label, 4);
                MarkersCanvas.Children.Add(label);
            }
        }
    }
}
