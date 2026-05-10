using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.MediaScanner
{
    /// <summary>
    /// Atlas AI Media Index Store - Persistent storage for scanned media items and albums
    /// Uses JSON file storage for simplicity and performance
    /// </summary>
    public class MediaIndexStore
    {
        private readonly string _indexFilePath;
        private readonly object _fileLock = new();
        private MediaIndex? _cachedIndex;
        private DateTime _lastCacheUpdate = DateTime.MinValue;

        public MediaIndexStore()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var atlasDataPath = Path.Combine(appDataPath, "AtlasAI");
            Directory.CreateDirectory(atlasDataPath);
            
            _indexFilePath = Path.Combine(atlasDataPath, "media_index.json");
        }

        /// <summary>
        /// Get all media items for a specific section
        /// </summary>
        public async Task<List<MediaItem>> GetMediaItemsAsync(string sectionName)
        {
            var index = await LoadIndexAsync();
            return index.MediaItems.Where(item => 
                string.Equals(item.SectionName, sectionName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.DisplayName)
                .ToList();
        }

        /// <summary>
        /// Get all albums (for Music section only)
        /// </summary>
        public async Task<List<AlbumInfo>> GetAlbumsAsync()
        {
            var index = await LoadIndexAsync();
            return index.Albums.OrderBy(album => album.Artist)
                              .ThenBy(album => album.AlbumTitle)
                              .ToList();
        }

        /// <summary>
        /// Save media items and albums to the index
        /// </summary>
        public async Task SaveScanResultsAsync(List<MediaItem> mediaItems, List<AlbumInfo> albums)
        {
            try
            {
                var index = new MediaIndex
                {
                    MediaItems = mediaItems,
                    Albums = albums,
                    LastUpdated = DateTime.UtcNow,
                    Version = 1
                };

                var json = JsonSerializer.Serialize(index, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                lock (_fileLock)
                {
                    File.WriteAllText(_indexFilePath, json);
                    _cachedIndex = index;
                    _lastCacheUpdate = DateTime.UtcNow;
                }

                System.Diagnostics.Debug.WriteLine($"[MediaIndexStore] Saved {mediaItems.Count} items, {albums.Count} albums");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaIndexStore] Save error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Clear all indexed media
        /// </summary>
        public async Task ClearIndexAsync()
        {
            try
            {
                lock (_fileLock)
                {
                    if (File.Exists(_indexFilePath))
                    {
                        File.Delete(_indexFilePath);
                    }
                    _cachedIndex = null;
                }

                System.Diagnostics.Debug.WriteLine("[MediaIndexStore] Index cleared");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaIndexStore] Clear error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get index statistics
        /// </summary>
        public async Task<IndexStats> GetStatsAsync()
        {
            var index = await LoadIndexAsync();
            
            return new IndexStats
            {
                TotalItems = index.MediaItems.Count,
                VideoCount = index.MediaItems.Count(i => i.MediaType == MediaType.Video),
                AudioCount = index.MediaItems.Count(i => i.MediaType == MediaType.Audio),
                ImageCount = index.MediaItems.Count(i => i.MediaType == MediaType.Image),
                AlbumCount = index.Albums.Count,
                LastUpdated = index.LastUpdated,
                TotalSizeBytes = index.MediaItems.Sum(i => i.FileSize)
            };
        }

        /// <summary>
        /// Load the media index from disk (with caching)
        /// </summary>
        private async Task<MediaIndex> LoadIndexAsync()
        {
            try
            {
                // Use cached version if recent (within 30 seconds)
                if (_cachedIndex != null && 
                    DateTime.UtcNow - _lastCacheUpdate < TimeSpan.FromSeconds(30))
                {
                    return _cachedIndex;
                }

                lock (_fileLock)
                {
                    if (!File.Exists(_indexFilePath))
                    {
                        _cachedIndex = CreateEmptyIndex();
                        _lastCacheUpdate = DateTime.UtcNow;
                        return _cachedIndex;
                    }

                    var json = File.ReadAllText(_indexFilePath);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        _cachedIndex = CreateEmptyIndex();
                        _lastCacheUpdate = DateTime.UtcNow;
                        return _cachedIndex;
                    }

                    var index = JsonSerializer.Deserialize<MediaIndex>(json, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    _cachedIndex = index ?? CreateEmptyIndex();
                    _lastCacheUpdate = DateTime.UtcNow;
                    return _cachedIndex;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaIndexStore] Load error: {ex.Message}");
                _cachedIndex = CreateEmptyIndex();
                _lastCacheUpdate = DateTime.UtcNow;
                return _cachedIndex;
            }
        }

        /// <summary>
        /// Create an empty media index
        /// </summary>
        private MediaIndex CreateEmptyIndex()
        {
            return new MediaIndex
            {
                MediaItems = new List<MediaItem>(),
                Albums = new List<AlbumInfo>(),
                LastUpdated = DateTime.UtcNow,
                Version = 1
            };
        }
    }

    /// <summary>
    /// Media index data structure
    /// </summary>
    public class MediaIndex
    {
        public List<MediaItem> MediaItems { get; set; } = new();
        public List<AlbumInfo> Albums { get; set; } = new();
        public DateTime LastUpdated { get; set; }
        public int Version { get; set; }
    }

    /// <summary>
    /// Index statistics
    /// </summary>
    public class IndexStats
    {
        public int TotalItems { get; set; }
        public int VideoCount { get; set; }
        public int AudioCount { get; set; }
        public int ImageCount { get; set; }
        public int AlbumCount { get; set; }
        public DateTime LastUpdated { get; set; }
        public long TotalSizeBytes { get; set; }

        public string TotalSizeFormatted => FormatFileSize(TotalSizeBytes);

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}