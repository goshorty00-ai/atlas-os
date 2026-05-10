using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.Views.ViewModels;
using AtlasAI.MediaScanner;

namespace AtlasAI.Tools
{
    /// <summary>
    /// Unified media player tool supporting multiple platforms with learning capabilities
    /// Supports: Spotify, YouTube, SoundCloud, Apple Music/iTunes, Amazon Music, Deezer, Tidal, Pandora
    /// </summary>
    public static class MediaPlayerTool
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly string PrefsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "media_preferences.json");
        
        private static MediaPreferences? _prefs;
        
        public enum Platform
        {
            Spotify,
            YouTube,
            SoundCloud,
            AppleMusic,
            AmazonMusic,
            Deezer,
            Tidal,
            Pandora,
            YouTubeMusic,
            Default  // User's preferred default
        }

        #region Platform Detection
        
        /// <summary>
        /// Detect which platform the user wants from their message
        /// </summary>
        public static Platform? DetectPlatform(string message)
        {
            var msg = message.ToLower();
            
            if (msg.Contains("spotify")) return Platform.Spotify;
            if (msg.Contains("youtube music") || msg.Contains("ytmusic") || msg.Contains("yt music")) return Platform.YouTubeMusic;
            if (msg.Contains("youtube") || msg.Contains("yt ")) return Platform.YouTube;
            if (msg.Contains("soundcloud") || msg.Contains("sound cloud")) return Platform.SoundCloud;
            if (msg.Contains("apple music") || msg.Contains("itunes") || msg.Contains("apple ")) return Platform.AppleMusic;
            if (msg.Contains("amazon music") || msg.Contains("amazon ")) return Platform.AmazonMusic;
            if (msg.Contains("deezer")) return Platform.Deezer;
            if (msg.Contains("tidal")) return Platform.Tidal;
            if (msg.Contains("pandora")) return Platform.Pandora;
            
            return null; // No specific platform mentioned
        }
        
        /// <summary>
        /// Detect platform from a string name (for intelligent understanding)
        /// </summary>
        public static Platform? DetectPlatformFromString(string? platformName)
        {
            if (string.IsNullOrEmpty(platformName)) return null;
            
            return platformName.ToLower() switch
            {
                "spotify" => Platform.Spotify,
                "youtube" => Platform.YouTube,
                "youtube_music" or "youtubemusic" => Platform.YouTubeMusic,
                "soundcloud" => Platform.SoundCloud,
                "apple_music" or "applemusic" or "itunes" => Platform.AppleMusic,
                "amazon_music" or "amazonmusic" or "amazon" => Platform.AmazonMusic,
                "deezer" => Platform.Deezer,
                "tidal" => Platform.Tidal,
                "pandora" => Platform.Pandora,
                _ => null
            };
        }

        /// <summary>
        /// Check if message is a media/music request
        /// </summary>
        public static bool IsMediaRequest(string message)
        {
            var msg = message.ToLower();
            return ContainsAny(msg, "play ", "put on ", "listen to ", "play me ", "queue ", "add to queue") &&
                   (ContainsAny(msg, "song", "music", "track", "artist", "album", "playlist", "video", " by ") ||
                    DetectPlatform(message) != null ||
                    msg.StartsWith("play "));
        }

        #endregion

        #region Main Play Method

        /// <summary>
        /// Play media on the appropriate platform (auto-detects or uses default)
        /// </summary>
        public static async Task<string> PlayAsync(string query, Platform? requestedPlatform = null)
        {
            LoadPreferences();
            
            // 1. Try Local Media Center First
            if (requestedPlatform == null || requestedPlatform == Platform.Default)
            {
                var localResult = await PlayLocalMediaAsync(query);
                if (localResult != null) return localResult;
            }

            // Determine platform
            var platform = requestedPlatform ?? _prefs?.DefaultPlatform ?? Platform.Spotify;
            
            Debug.WriteLine($"[MediaPlayer] Playing '{query}' on {platform}");
            
            // Record this play for learning
            RecordPlay(query, platform);
            
            // Execute on the appropriate platform
            return platform switch
            {
                Platform.Spotify => await PlayOnSpotifyAsync(query),
                Platform.YouTube => await PlayOnYouTubeAsync(query),
                Platform.YouTubeMusic => await PlayOnYouTubeMusicAsync(query),
                Platform.SoundCloud => await PlayOnSoundCloudAsync(query),
                Platform.AppleMusic => await PlayOnAppleMusicAsync(query),
                Platform.AmazonMusic => await PlayOnAmazonMusicAsync(query),
                Platform.Deezer => await PlayOnDeezerAsync(query),
                Platform.Tidal => await PlayOnTidalAsync(query),
                Platform.Pandora => await PlayOnPandoraAsync(query),
                _ => await PlayOnSpotifyAsync(query)
            };
        }

        #endregion

        #region Local Media Player

        private static async Task<string?> PlayLocalMediaAsync(string query)
        {
            try
            {
                // Check if Media Center is active
                var vm = MediaCentreViewModel.Instance;
                var service = MediaPlaybackService.Instance;

                if (vm == null || service == null)
                    return null;

                // Search in library
                var normalizedQuery = query.ToLowerInvariant();
                var items = vm.AllItems;
                
                // 1. Exact match
                var exactMatch = items.FirstOrDefault(i => 
                    i.Title.Equals(query, StringComparison.OrdinalIgnoreCase) || 
                    i.Artist.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                    i.Album.Equals(query, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        if (exactMatch.PlaybackItem != null)
                            service.PlaySingle(exactMatch.PlaybackItem);
                    });
                    return $"▶️ Playing local file: {exactMatch.Title}";
                }

                // 2. Fuzzy match
                var fuzzyMatch = items.FirstOrDefault(i => 
                    i.Title.ToLowerInvariant().Contains(normalizedQuery) || 
                    i.Artist.ToLowerInvariant().Contains(normalizedQuery) ||
                    i.Album.ToLowerInvariant().Contains(normalizedQuery));

                if (fuzzyMatch != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        if (fuzzyMatch.PlaybackItem != null)
                            service.PlaySingle(fuzzyMatch.PlaybackItem);
                    });
                    return $"▶️ Playing local file: {fuzzyMatch.Title}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaPlayer] Local play error: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Platform-Specific Players

        private static async Task<string> PlayOnSpotifyAsync(string query)
        {
            // Use existing SpotifyTool
            return await SpotifyTool.PlayAsync(query);
        }

        private static async Task<string> PlayOnYouTubeAsync(string query)
        {
            try
            {
                // Try to get the first video ID from YouTube search
                var videoId = await SearchYouTubeVideoIdAsync(query);
                
                if (!string.IsNullOrEmpty(videoId))
                {
                    // Open the video directly (auto-plays)
                    var url = $"https://www.youtube.com/watch?v={videoId}";
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    return $"▶️ Playing \"{query}\" on YouTube";
                }
                else
                {
                    // Fallback to search page
                    var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                    var url = $"https://www.youtube.com/results?search_query={encodedQuery}";
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    return $"🔍 Opened YouTube search for \"{query}\"";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open YouTube: {ex.Message}";
            }
        }

        /// <summary>
        /// Search YouTube and get the first video ID
        /// </summary>
        private static async Task<string?> SearchYouTubeVideoIdAsync(string query)
        {
            try
            {
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                // Use YouTube's search page and parse the first video ID from HTML
                var searchUrl = $"https://www.youtube.com/results?search_query={encodedQuery}";
                
                var response = await httpClient.GetStringAsync(searchUrl);
                
                // Look for video ID pattern in the response
                // YouTube embeds video IDs in the page like: "videoId":"XXXXXXXXXXX"
                var match = System.Text.RegularExpressions.Regex.Match(response, @"""videoId"":""([a-zA-Z0-9_-]{11})""");
                if (match.Success)
                {
                    var videoId = match.Groups[1].Value;
                    Debug.WriteLine($"[YouTube] Found video ID: {videoId}");
                    return videoId;
                }
                
                // Alternative pattern: /watch?v=XXXXXXXXXXX
                match = System.Text.RegularExpressions.Regex.Match(response, @"/watch\?v=([a-zA-Z0-9_-]{11})");
                if (match.Success)
                {
                    var videoId = match.Groups[1].Value;
                    Debug.WriteLine($"[YouTube] Found video ID (alt): {videoId}");
                    return videoId;
                }
                
                Debug.WriteLine("[YouTube] No video ID found in search results");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[YouTube] Search error: {ex.Message}");
                return null;
            }
        }

        private static async Task<string> PlayOnYouTubeMusicAsync(string query)
        {
            try
            {
                // YouTube Music also supports direct video playback
                var videoId = await SearchYouTubeVideoIdAsync(query + " official audio");
                
                if (!string.IsNullOrEmpty(videoId))
                {
                    var url = $"https://music.youtube.com/watch?v={videoId}";
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    return $"▶️ Playing \"{query}\" on YouTube Music";
                }
                
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                var searchUrl = $"https://music.youtube.com/search?q={encodedQuery}";
                Process.Start(new ProcessStartInfo { FileName = searchUrl, UseShellExecute = true });
                return $"🔍 Opened YouTube Music search for \"{query}\"";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open YouTube Music: {ex.Message}";
            }
        }

        private static async Task<string> PlayOnSoundCloudAsync(string query)
        {
            try
            {
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                var url = $"https://soundcloud.com/search?q={encodedQuery}";
                
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return $"🔊 Opened SoundCloud search for \"{query}\"\n💡 Click the first result to play!";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open SoundCloud: {ex.Message}";
            }
        }

        private static async Task<string> PlayOnAppleMusicAsync(string query)
        {
            try
            {
                // Try to open iTunes/Apple Music app first
                var iTunesPath = @"C:\Program Files\iTunes\iTunes.exe";
                var iTunesPath86 = @"C:\Program Files (x86)\iTunes\iTunes.exe";
                
                if (File.Exists(iTunesPath) || File.Exists(iTunesPath86))
                {
                    var path = File.Exists(iTunesPath) ? iTunesPath : iTunesPath86;
                    Process.Start(path);
                    await Task.Delay(500);
                }
                
                // Open Apple Music web search
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                var url = $"https://music.apple.com/search?term={encodedQuery}";
                
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return $"🍎 Opened Apple Music search for \"{query}\"";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open Apple Music: {ex.Message}";
            }
        }

        private static async Task<string> PlayOnAmazonMusicAsync(string query)
        {
            try
            {
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                var url = $"https://music.amazon.com/search/{encodedQuery}";
                
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return $"📦 Opened Amazon Music search for \"{query}\"";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open Amazon Music: {ex.Message}";
            }
        }

        private static async Task<string> PlayOnDeezerAsync(string query)
        {
            try
            {
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                var url = $"https://www.deezer.com/search/{encodedQuery}";
                
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return $"🎧 Opened Deezer search for \"{query}\"";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open Deezer: {ex.Message}";
            }
        }

        private static async Task<string> PlayOnTidalAsync(string query)
        {
            try
            {
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                var url = $"https://listen.tidal.com/search?q={encodedQuery}";
                
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return $"🌊 Opened Tidal search for \"{query}\"";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open Tidal: {ex.Message}";
            }
        }

        private static async Task<string> PlayOnPandoraAsync(string query)
        {
            try
            {
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                var url = $"https://www.pandora.com/search/{encodedQuery}/all";
                
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return $"📻 Opened Pandora search for \"{query}\"";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open Pandora: {ex.Message}";
            }
        }

        #endregion

        #region Learning System

        /// <summary>
        /// Set the user's default music platform
        /// </summary>
        public static string SetDefaultPlatform(Platform platform)
        {
            LoadPreferences();
            _prefs!.DefaultPlatform = platform;
            _prefs.LastUpdated = DateTime.Now;
            SavePreferences();
            
            return $"✅ Set {platform} as your default music player. I'll use it when you just say \"play [song]\"!";
        }

        /// <summary>
        /// Get the user's default platform
        /// </summary>
        public static Platform GetDefaultPlatform()
        {
            LoadPreferences();
            return _prefs?.DefaultPlatform ?? Platform.Spotify;
        }

        /// <summary>
        /// Record a play for learning purposes
        /// </summary>
        private static void RecordPlay(string query, Platform platform)
        {
            LoadPreferences();
            
            // Update platform usage count
            if (!_prefs!.PlatformUsage.ContainsKey(platform))
                _prefs.PlatformUsage[platform] = 0;
            _prefs.PlatformUsage[platform]++;
            
            // Record recent play
            _prefs.RecentPlays.Insert(0, new PlayRecord
            {
                Query = query,
                Platform = platform,
                Timestamp = DateTime.Now
            });
            
            // Keep only last 100 plays
            if (_prefs.RecentPlays.Count > 100)
                _prefs.RecentPlays = _prefs.RecentPlays.Take(100).ToList();
            
            // Extract and track artist/genre preferences
            ExtractAndTrackPreferences(query, platform);
            
            _prefs.LastUpdated = DateTime.Now;
            SavePreferences();
        }

        /// <summary>
        /// Extract artist/genre info and track preferences
        /// </summary>
        private static void ExtractAndTrackPreferences(string query, Platform platform)
        {
            // Try to extract artist from "X by Y" pattern
            var byMatch = Regex.Match(query, @"(.+?)\s+by\s+(.+)", RegexOptions.IgnoreCase);
            if (byMatch.Success)
            {
                var artist = byMatch.Groups[2].Value.Trim();
                if (!_prefs!.FavoriteArtists.ContainsKey(artist))
                    _prefs.FavoriteArtists[artist] = 0;
                _prefs.FavoriteArtists[artist]++;
                
                // Track artist's preferred platform
                if (!_prefs.ArtistPlatformPrefs.ContainsKey(artist))
                    _prefs.ArtistPlatformPrefs[artist] = platform;
            }
        }

        /// <summary>
        /// Get smart platform suggestion based on learning
        /// </summary>
        public static Platform GetSmartPlatformSuggestion(string query)
        {
            LoadPreferences();
            
            // Check if query mentions a known artist with platform preference
            var byMatch = Regex.Match(query, @"by\s+(.+)", RegexOptions.IgnoreCase);
            if (byMatch.Success)
            {
                var artist = byMatch.Groups[1].Value.Trim();
                if (_prefs!.ArtistPlatformPrefs.TryGetValue(artist, out var artistPlatform))
                {
                    Debug.WriteLine($"[MediaPlayer] Using learned platform {artistPlatform} for artist {artist}");
                    return artistPlatform;
                }
            }
            
            // Check for genre-based preferences (future enhancement)
            
            // Fall back to most used platform or default
            if (_prefs!.PlatformUsage.Count > 0)
            {
                var mostUsed = _prefs.PlatformUsage.OrderByDescending(x => x.Value).First().Key;
                if (_prefs.PlatformUsage[mostUsed] > 5) // Only if used more than 5 times
                {
                    Debug.WriteLine($"[MediaPlayer] Using most-used platform: {mostUsed}");
                    return mostUsed;
                }
            }
            
            return _prefs.DefaultPlatform;
        }

        /// <summary>
        /// Get usage statistics
        /// </summary>
        public static string GetStats()
        {
            LoadPreferences();
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📊 Your Music Stats\n");
            
            sb.AppendLine($"🎵 Default player: {_prefs!.DefaultPlatform}");
            sb.AppendLine($"📝 Total plays tracked: {_prefs.RecentPlays.Count}");
            
            if (_prefs.PlatformUsage.Count > 0)
            {
                sb.AppendLine("\nPlatform Usage:");
                foreach (var (platform, count) in _prefs.PlatformUsage.OrderByDescending(x => x.Value))
                {
                    var icon = GetPlatformIcon(platform);
                    sb.AppendLine($"  {icon} {platform}: {count} plays");
                }
            }
            
            if (_prefs.FavoriteArtists.Count > 0)
            {
                sb.AppendLine("\nTop Artists:");
                foreach (var (artist, count) in _prefs.FavoriteArtists.OrderByDescending(x => x.Value).Take(5))
                {
                    sb.AppendLine($"  🎤 {artist}: {count} plays");
                }
            }
            
            return sb.ToString();
        }

        private static string GetPlatformIcon(Platform platform) => platform switch
        {
            Platform.Spotify => "💚",
            Platform.YouTube => "🔴",
            Platform.YouTubeMusic => "🎵",
            Platform.SoundCloud => "🔊",
            Platform.AppleMusic => "🍎",
            Platform.AmazonMusic => "📦",
            Platform.Deezer => "🎧",
            Platform.Tidal => "🌊",
            Platform.Pandora => "📻",
            _ => "🎵"
        };

        #endregion

        #region Preferences Storage

        private static void LoadPreferences()
        {
            if (_prefs != null) return;
            
            try
            {
                if (File.Exists(PrefsPath))
                {
                    var json = File.ReadAllText(PrefsPath);
                    _prefs = JsonSerializer.Deserialize<MediaPreferences>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaPlayer] Failed to load prefs: {ex.Message}");
            }
            
            _prefs ??= new MediaPreferences();
        }

        private static void SavePreferences()
        {
            try
            {
                var dir = Path.GetDirectoryName(PrefsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_prefs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PrefsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaPlayer] Failed to save prefs: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private static bool ContainsAny(string text, params string[] keywords)
        {
            foreach (var keyword in keywords)
                if (text.Contains(keyword)) return true;
            return false;
        }

        /// <summary>
        /// Extract the song/media query from user message
        /// </summary>
        public static string ExtractQuery(string message)
        {
            var patterns = new[]
            {
                @"(?:play|listen to|put on|queue)\s+(.+?)\s+on\s+(?:spotify|youtube|soundcloud|apple music|itunes|amazon|deezer|tidal|pandora)",
                @"(?:play|listen to|put on|queue)\s+(.+?)\s+(?:on|in|using)\s+\w+",
                @"(?:play|listen to|put on|queue)\s+(.+)",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var query = match.Groups[1].Value.Trim();
                    // Clean up
                    query = Regex.Replace(query, @"\s*(please|for me|now)$", "", RegexOptions.IgnoreCase);
                    query = Regex.Replace(query, @"^(some |a |the )", "", RegexOptions.IgnoreCase);
                    if (!string.IsNullOrWhiteSpace(query))
                        return query;
                }
            }

            // Fallback: everything after "play"
            var playMatch = Regex.Match(message, @"play\s+(.+)", RegexOptions.IgnoreCase);
            if (playMatch.Success)
            {
                var query = playMatch.Groups[1].Value.Trim();
                // Remove platform names from end
                query = Regex.Replace(query, @"\s+(?:on|in|using)\s+(?:spotify|youtube|soundcloud|apple music|itunes|amazon|deezer|tidal|pandora).*$", "", RegexOptions.IgnoreCase);
                return query;
            }

            return "";
        }

        #endregion
    }

    #region Models

    public class MediaPreferences
    {
        public MediaPlayerTool.Platform DefaultPlatform { get; set; } = MediaPlayerTool.Platform.Spotify;
        public Dictionary<MediaPlayerTool.Platform, int> PlatformUsage { get; set; } = new();
        public Dictionary<string, int> FavoriteArtists { get; set; } = new();
        public Dictionary<string, MediaPlayerTool.Platform> ArtistPlatformPrefs { get; set; } = new();
        public List<PlayRecord> RecentPlays { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    public class PlayRecord
    {
        public string Query { get; set; } = "";
        public MediaPlayerTool.Platform Platform { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}
