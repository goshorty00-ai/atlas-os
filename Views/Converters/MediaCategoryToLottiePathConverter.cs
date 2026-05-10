using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using AtlasAI.Core;

namespace AtlasAI.Views.Converters
{
    public sealed class MediaCategoryToLottiePathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var id = (value as string ?? "").Trim().ToLowerInvariant();

            // Optional user override: use a single chosen Lottie for the sidebar.
            try
            {
                var overrideName = (PreferencesStore.Instance.Current.MediaCentreSidebarLottie ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(overrideName))
                {
                    var safeName = Path.GetFileName(overrideName);
                    var overridePath = App.GetLottiePath(safeName);
                    if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
                        return overridePath;
                }
            }
            catch
            {
            }

            // Best-effort mapping. Uses runtime folder: bin/x64/Animations (preferred) via App.GetLottiePath.
            var fileName = id switch
            {
                "servers" => "Spinning Globe.json",
                "movies" => "show time icon.json",
                "tv" => "show time icon.json",
                "music" => "media player dancing.json",
                "radio" => "Ripple loading animation.json",
                "images" => "floating hearts.json",
                "games" => "Ghostsmart.json",
                "apps" => "AI Assistant.json",
                "karaoke" => "chatbot.json",
                _ => "Spinning Globe.json"
            };

            try
            {
                var path = App.GetLottiePath(fileName);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;

                // Fallback: try any known file.
                var fallback = App.GetLottiePath("Spinning Globe.json");
                return fallback ?? "";
            }
            catch
            {
                return "";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
