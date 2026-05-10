using System;
using System.Globalization;
using System.Windows.Data;

namespace AtlasAI.Views.Converters
{
    public sealed class ProgressWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 3) return 0d;
                var width = values[0] is double w ? w : 0d;
                var value = values[1] is double v ? v : 0d;
                var max = values[2] is double m ? m : 100d;
                if (width <= 0 || max <= 0) return 0d;
                var pct = Math.Max(0d, Math.Min(1d, value / max));
                return width * pct;
            }
            catch
            {
                return 0d;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

