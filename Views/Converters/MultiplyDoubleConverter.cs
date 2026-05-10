using System;
using System.Globalization;
using System.Windows.Data;

namespace AtlasAI.Views.Converters
{
    public sealed class MultiplyDoubleConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return 0d;

            if (values[0] is not double a)
                return 0d;

            var b = 0d;
            if (values[1] is double bd)
                b = bd;
            else if (values[1] is float bf)
                b = bf;
            else if (values[1] is int bi)
                b = bi;

            if (double.IsNaN(a) || double.IsInfinity(a)) return 0d;
            if (double.IsNaN(b) || double.IsInfinity(b)) return 0d;

            if (b < 0) b = 0;
            if (b > 1) b = 1;

            return a * b;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

