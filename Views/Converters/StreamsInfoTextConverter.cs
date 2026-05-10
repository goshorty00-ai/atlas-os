using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using AtlasAI.Streaming;

namespace AtlasAI.Views.Converters
{
    public class StreamsInfoTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AddonSource s)
            {
                var provider = (s.ProviderName ?? "").Trim();

                if (s.IsInfoOnly && s.Metadata != null)
                {
                    // Prefer common info keys; otherwise use first non-empty metadata value
                    var preferredKeys = new[] { "info", "message", "note", "description", "details" };
                    foreach (var key in preferredKeys)
                    {
                        if (s.Metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                            return string.IsNullOrWhiteSpace(provider) ? v : $"{provider}: {v}";
                    }

                    var any = s.Metadata.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                    if (!string.IsNullOrWhiteSpace(any))
                        return string.IsNullOrWhiteSpace(provider) ? any! : $"{provider}: {any}";
                }

                if (!string.IsNullOrWhiteSpace(s.Quality))
                    return string.IsNullOrWhiteSpace(provider) ? s.Quality! : $"{provider} · {s.Quality}";

                if (!string.IsNullOrWhiteSpace(s.Name))
                    return string.IsNullOrWhiteSpace(provider) ? s.Name! : $"{provider} · {s.Name}";

                if (!string.IsNullOrWhiteSpace(provider))
                    return provider;
            }

            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
