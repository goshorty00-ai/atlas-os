using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AtlasAI.Views.Converters
{
    public sealed class BoolToGridLengthConverter : IValueConverter
    {
        public GridLength TrueLength { get; set; } = new GridLength(0);
        public GridLength FalseLength { get; set; } = new GridLength(1, GridUnitType.Star);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var b = value is bool v && v;
            return b ? TrueLength : FalseLength;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}

