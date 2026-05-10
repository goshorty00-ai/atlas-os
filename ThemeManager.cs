using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace AtlasAI
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    public static class ThemeManager
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "theme.json");

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

        public static event Action<AppTheme>? ThemeChanged;

        // Dark theme colors
        public static class Dark
        {
            public static Color Background => Color.FromRgb(30, 30, 30);
            public static Color Surface => Color.FromRgb(45, 45, 48);
            public static Color Input => Color.FromRgb(60, 60, 60);
            public static Color Text => Colors.White;
            public static Color TextSecondary => Color.FromRgb(200, 200, 200);
            public static Color Accent => Color.FromRgb(0, 122, 204);
            public static Color UserBubble => Color.FromRgb(0, 100, 180);
            public static Color AssistantBubble => Color.FromRgb(50, 50, 50);
        }

        // Light theme colors
        public static class Light
        {
            public static Color Background => Color.FromRgb(250, 250, 250);
            public static Color Surface => Color.FromRgb(240, 240, 240);
            public static Color Input => Colors.White;
            public static Color Text => Color.FromRgb(20, 20, 20);
            public static Color TextSecondary => Color.FromRgb(80, 80, 80);
            public static Color Accent => Color.FromRgb(0, 120, 212);
            public static Color UserBubble => Color.FromRgb(0, 120, 212);
            public static Color AssistantBubble => Color.FromRgb(220, 220, 220);
        }

        public static void LoadTheme()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("theme", out var theme) &&
                        Enum.TryParse<AppTheme>(theme.GetString(), out var t))
                    {
                        CurrentTheme = t;
                    }
                }
            }
            catch { }
        }

        public static void SaveTheme()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                var json = JsonSerializer.Serialize(new { theme = CurrentTheme.ToString() });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public static void SetTheme(AppTheme theme)
        {
            CurrentTheme = theme;
            SaveTheme();
            ThemeChanged?.Invoke(theme);
        }

        public static void ToggleTheme()
        {
            SetTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        }

        // Helper methods to get current theme colors
        public static Color GetBackground() => CurrentTheme == AppTheme.Dark ? Dark.Background : Light.Background;
        public static Color GetSurface() => CurrentTheme == AppTheme.Dark ? Dark.Surface : Light.Surface;
        public static Color GetInput() => CurrentTheme == AppTheme.Dark ? Dark.Input : Light.Input;
        public static Color GetText() => CurrentTheme == AppTheme.Dark ? Dark.Text : Light.Text;
        public static Color GetTextSecondary() => CurrentTheme == AppTheme.Dark ? Dark.TextSecondary : Light.TextSecondary;
        public static Color GetAccent() => CurrentTheme == AppTheme.Dark ? Dark.Accent : Light.Accent;
        public static Color GetUserBubble() => CurrentTheme == AppTheme.Dark ? Dark.UserBubble : Light.UserBubble;
        public static Color GetAssistantBubble() => CurrentTheme == AppTheme.Dark ? Dark.AssistantBubble : Light.AssistantBubble;

        public static SolidColorBrush BrushBackground => new(GetBackground());
        public static SolidColorBrush BrushSurface => new(GetSurface());
        public static SolidColorBrush BrushInput => new(GetInput());
        public static SolidColorBrush BrushText => new(GetText());
        public static SolidColorBrush BrushTextSecondary => new(GetTextSecondary());
        public static SolidColorBrush BrushAccent => new(GetAccent());
        public static SolidColorBrush BrushUserBubble => new(GetUserBubble());
        public static SolidColorBrush BrushAssistantBubble => new(GetAssistantBubble());
    }
}
