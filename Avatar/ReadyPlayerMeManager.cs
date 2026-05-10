using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AtlasAI.Avatar
{
    /// <summary>
    /// Manages Ready Player Me avatar integration
    /// </summary>
    public class ReadyPlayerMeManager
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private readonly string _avatarCachePath;
        private Dictionary<string, string> _avatarUrls = new();

        public ReadyPlayerMeManager()
        {
            _avatarCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "Avatars");
            Directory.CreateDirectory(_avatarCachePath);
            
            // Initialize with some default Ready Player Me avatars
            InitializeDefaultAvatars();
        }

        private void InitializeDefaultAvatars()
        {
            // These are example Ready Player Me avatar URLs - replace with your actual avatars
            _avatarUrls = new Dictionary<string, string>
            {
                ["default"] = "https://models.readyplayer.me/64bfa15f0e72c63d7c3f5c8a.glb",
                ["casual"] = "https://models.readyplayer.me/64bfa15f0e72c63d7c3f5c8b.glb", 
                ["business"] = "https://models.readyplayer.me/64bfa15f0e72c63d7c3f5c8c.glb",
                ["gaming"] = "https://models.readyplayer.me/64bfa15f0e72c63d7c3f5c8d.glb"
            };
        }

        /// <summary>
        /// Add a custom Ready Player Me avatar URL
        /// </summary>
        public void AddAvatar(string name, string avatarUrl)
        {
            _avatarUrls[name] = avatarUrl;
            SaveAvatarConfig();
        }

        /// <summary>
        /// Get all available avatar names
        /// </summary>
        public IEnumerable<string> GetAvatarNames()
        {
            return _avatarUrls.Keys;
        }

        /// <summary>
        /// Download and cache an avatar model
        /// </summary>
        public async Task<string?> DownloadAvatarAsync(string avatarName)
        {
            if (!_avatarUrls.TryGetValue(avatarName, out var url))
                return null;

            var fileName = $"{avatarName}.glb";
            var localPath = Path.Combine(_avatarCachePath, fileName);

            // Return cached version if exists
            if (File.Exists(localPath))
                return localPath;

            try
            {
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(localPath, content);
                    return localPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to download avatar {avatarName}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get avatar thumbnail/preview image
        /// </summary>
        public async Task<BitmapImage?> GetAvatarThumbnailAsync(string avatarName)
        {
            if (!_avatarUrls.TryGetValue(avatarName, out var url))
                return null;

            // Ready Player Me provides thumbnail URLs by replacing .glb with .png
            var thumbnailUrl = url.Replace(".glb", ".png");
            
            try
            {
                var response = await httpClient.GetAsync(thumbnailUrl);
                if (response.IsSuccessStatusCode)
                {
                    var imageData = await response.Content.ReadAsByteArrayAsync();
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(imageData);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load thumbnail for {avatarName}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Create a simple 2D avatar representation for the chat window
        /// </summary>
        public async Task<UIElement> CreateAvatarUIAsync(string avatarName, double size = 40)
        {
            var thumbnail = await GetAvatarThumbnailAsync(avatarName);
            
            if (thumbnail != null)
            {
                return new Border
                {
                    Width = size,
                    Height = size,
                    CornerRadius = new CornerRadius(size / 2),
                    Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill },
                    BorderBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                    BorderThickness = new Thickness(2)
                };
            }
            else
            {
                // Fallback to emoji/text avatar
                return new Border
                {
                    Width = size,
                    Height = size,
                    CornerRadius = new CornerRadius(size / 2),
                    Background = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                    Child = new TextBlock
                    {
                        Text = GetAvatarEmoji(avatarName),
                        FontSize = size * 0.5,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.White
                    }
                };
            }
        }

        private string GetAvatarEmoji(string avatarName)
        {
            return avatarName.ToLower() switch
            {
                "default" => "🤖",
                "casual" => "😊",
                "business" => "👔",
                "gaming" => "🎮",
                "energetic" => "⚡",
                "calm" => "😌",
                "readyplayer" => "🎭",
                _ => "👤"
            };
        }

        /// <summary>
        /// Save avatar configuration to disk
        /// </summary>
        private void SaveAvatarConfig()
        {
            try
            {
                var configPath = Path.Combine(_avatarCachePath, "avatar_config.json");
                var json = System.Text.Json.JsonSerializer.Serialize(_avatarUrls, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save avatar config: {ex.Message}");
            }
        }

        /// <summary>
        /// Load avatar configuration from disk
        /// </summary>
        public void LoadAvatarConfig()
        {
            try
            {
                var configPath = Path.Combine(_avatarCachePath, "avatar_config.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        foreach (var kvp in loaded)
                        {
                            _avatarUrls[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load avatar config: {ex.Message}");
            }
        }

        /// <summary>
        /// Get avatar URL for external use (Unity integration, etc.)
        /// </summary>
        public string? GetAvatarUrl(string avatarName)
        {
            return _avatarUrls.TryGetValue(avatarName, out var url) ? url : null;
        }

        /// <summary>
        /// Validate if a URL is a valid Ready Player Me avatar
        /// </summary>
        public static bool IsValidReadyPlayerMeUrl(string url)
        {
            return !string.IsNullOrEmpty(url) && 
                   (url.Contains("models.readyplayer.me") || url.Contains("readyplayer.me")) &&
                   url.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }
    }
}