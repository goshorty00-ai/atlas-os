using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AtlasAI.Controls
{
    public partial class ScanProgressBar : UserControl
    {
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(ScanProgressBar),
                new PropertyMetadata(0d));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(ScanProgressBar),
                new PropertyMetadata(100d));

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(ScanProgressBar),
                new PropertyMetadata(""));

        public static readonly DependencyProperty ProgressTextProperty =
            DependencyProperty.Register(nameof(ProgressText), typeof(string), typeof(ScanProgressBar),
                new PropertyMetadata("0%"));

        public static readonly DependencyProperty MonoFontFamilyProperty =
            DependencyProperty.Register(nameof(MonoFontFamily), typeof(FontFamily), typeof(ScanProgressBar),
                new PropertyMetadata(new FontFamily("Cascadia Code, Consolas")));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        public string ProgressText
        {
            get => (string)GetValue(ProgressTextProperty);
            set => SetValue(ProgressTextProperty, value);
        }

        public FontFamily MonoFontFamily
        {
            get => (FontFamily)GetValue(MonoFontFamilyProperty);
            set => SetValue(MonoFontFamilyProperty, value);
        }

        public ScanProgressBar()
        {
            InitializeComponent();
        }
    }
}

