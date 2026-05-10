using System;
using System.Globalization;
using System.Windows.Data;

namespace AtlasAI.Views.Converters
{
    public sealed class WidthMultiplierConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double width)
                return 0d;

            if (parameter == null)
                return width;

            if (parameter is double d)
                return width * d;

            if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                return width * m;

            return width;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

