using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AtlasAI.Views.Converters
{
    public sealed class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string s || string.IsNullOrWhiteSpace(s))
                return "";

            try
            {
                var name = Path.GetFileName(s);
                return string.IsNullOrWhiteSpace(name) ? s : name;
            }
            catch
            {
                return s;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
