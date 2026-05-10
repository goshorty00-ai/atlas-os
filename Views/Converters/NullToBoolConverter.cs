using System;
using System.Globalization;
using System.Windows.Data;

namespace AtlasAI.Views.Converters
{
    public sealed class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Returns true if the value is null or empty string, false otherwise.
            // This matches the logic used in MediaCenterControl.xaml where:
            // Value="False" (meaning value is NOT null) -> Visibility="Collapsed" for TextBlock
            // Value="False" (meaning value is NOT null) -> Visibility="Visible" for Image
            
            if (value == null) return true;
            if (value is string s && string.IsNullOrWhiteSpace(s)) return true;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
