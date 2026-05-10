using System;
using System.Windows;
using System.Windows.Media;

namespace AtlasAI.Controls
{
    public static class RoundedClip
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.RegisterAttached(
                "CornerRadius",
                typeof(double),
                typeof(RoundedClip),
                new PropertyMetadata(0d, OnCornerRadiusChanged));

        public static void SetCornerRadius(DependencyObject element, double value) =>
            element.SetValue(CornerRadiusProperty, value);

        public static double GetCornerRadius(DependencyObject element) =>
            (double)element.GetValue(CornerRadiusProperty);

        private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe)
                return;

            void Apply()
            {
                var radius = GetCornerRadius(fe);
                if (radius <= 0 || fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
                {
                    fe.Clip = null;
                    return;
                }

                fe.Clip = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), radius, radius);
            }

            fe.Loaded -= FeOnLoaded;
            fe.SizeChanged -= FeOnSizeChanged;

            if (GetCornerRadius(fe) > 0)
            {
                fe.Loaded += FeOnLoaded;
                fe.SizeChanged += FeOnSizeChanged;
            }
            else
            {
                fe.Clip = null;
            }

            void FeOnLoaded(object sender, RoutedEventArgs args) => Apply();
            void FeOnSizeChanged(object sender, SizeChangedEventArgs args) => Apply();
        }
    }
}

