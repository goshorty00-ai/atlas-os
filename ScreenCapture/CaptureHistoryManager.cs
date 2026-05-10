using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;

namespace AtlasAI.ScreenCapture
{
    public class CaptureHistoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FilePath { get; set; } = "";
        public string ThumbnailPath { get; set; } = "";
        public DateTime CaptureTime { get; set; } = DateTime.Now;
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public string ExtractedText { get; set; } = "";
        public string AIAnalysis { get; set; } = "";
        public CaptureMetadata Metadata { get; set; } = new();
        public long FileSizeBytes { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsArchived { get; set; }
    }

    public class CaptureHistoryManager
    {
        private static readonly string HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "history.json");
        
        private static readonly string ThumbnailsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "Thumbnails");

        private List<CaptureHistoryItem> _historyItems = new();
        private readonly object _lockObject = new();

        public event Action<CaptureHistoryItem>? ItemAdded;
        public event Action<CaptureHistoryItem>? ItemUpdated;
        public event Action<string>? ItemDeleted;
        public event Action? HistoryCleared;

        public CaptureHistoryManager()
        {
            EnsureDirectoriesExist();
            LoadHistory();
        }

        private void EnsureDirectoriesExist()
        {
            var directories = new[] { 
                Path.GetDirectoryName(HistoryPath)!, 
                ThumbnailsPath 
            };
            
            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
        }

        public async Task<CaptureHistoryItem> AddCaptureAsync(CaptureResult captureResult)
        {
            var item = new CaptureHistoryItem
            {
                FilePath = captureResult.Metadata.FilePath,
                CaptureTime = captureResult.Metadata.Timestamp,
                Title = $"Screenshot {captureResult.Metadata.Timestamp:yyyy-MM-dd HH:mm:ss}",
                Metadata = captureResult.Metadata,
                FileSizeBytes = new FileInfo(captureResult.Metadata.FilePath).Length
            };

            // Generate thumbnail
            item.ThumbnailPath = await GenerateThumbnailAsync(item.FilePath, item.Id);

            lock (_lockObject)
            {
                _historyItems.Insert(0, item); // Add to beginning for chronological order
            }

            await SaveHistoryAsync();
            ItemAdded?.Invoke(item);
            
            return item;
        }

        private async Task<string> GenerateThumbnailAsync(string imagePath, string itemId)
        {
            try
            {
                var thumbnailPath = Path.Combine(ThumbnailsPath, $"{itemId}_thumb.jpg");
                
                await Task.Run(() =>
                {
                    using var originalImage = Image.FromFile(imagePath);
                    
                    // Calculate thumbnail size (max 200x150, maintain aspect ratio)
                    var maxWidth = 200;
                    var maxHeight = 150;
                    
                    var ratioX = (double)maxWidth / originalImage.Width;
                    var ratioY = (double)maxHeight / originalImage.Height;
                    var ratio = Math.Min(ratioX, ratioY);
                    
                    var newWidth = (int)(originalImage.Width * ratio);
                    var newHeight = (int)(originalImage.Height * ratio);
                    
                    using var thumbnail = new Bitmap(newWidth, newHeight);
                    using var graphics = Graphics.FromImage(thumbnail);
                    
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    
                    graphics.DrawImage(originalImage, 0, 0, newWidth, newHeight);
                    
                    thumbnail.Save(thumbnailPath, ImageFormat.Jpeg);
                });
                
                return thumbnailPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Thumbnail generation failed: {ex.Message}");
                return "";
            }
        }

        public async Task UpdateItemAsync(CaptureHistoryItem item)
        {
            lock (_lockObject)
            {
                var existingItem = _historyItems.FirstOrDefault(i => i.Id == item.Id);
                if (existingItem != null)
                {
                    var index = _historyItems.IndexOf(existingItem);
                    _historyItems[index] = item;
                }
            }

            await SaveHistoryAsync();
            ItemUpdated?.Invoke(item);
        }

        public async Task DeleteItemAsync(string itemId)
        {
            CaptureHistoryItem? itemToDelete = null;
            
            lock (_lockObject)
            {
                itemToDelete = _historyItems.FirstOrDefault(i => i.Id == itemId);
                if (itemToDelete != null)
                {
                    _historyItems.Remove(itemToDelete);
                }
            }

            if (itemToDelete != null)
            {
                // Delete files
                try
                {
                    if (File.Exists(itemToDelete.FilePath))
                        File.Delete(itemToDelete.FilePath);
                    if (File.Exists(itemToDelete.ThumbnailPath))
                        File.Delete(itemToDelete.ThumbnailPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"File deletion failed: {ex.Message}");
                }

                await SaveHistoryAsync();
                ItemDeleted?.Invoke(itemId);
            }
        }

        public List<CaptureHistoryItem> GetAllItems()
        {
            lock (_lockObject)
            {
                return new List<CaptureHistoryItem>(_historyItems);
            }
        }

        public List<CaptureHistoryItem> SearchItems(string query, bool includeArchived = false)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GetAllItems().Where(i => includeArchived || !i.IsArchived).ToList();

            var searchTerms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            lock (_lockObject)
            {
                return _historyItems.Where(item =>
                {
                    if (!includeArchived && item.IsArchived) return false;
                    
                    var searchableText = $"{item.Title} {item.Description} {item.ExtractedText} {item.AIAnalysis} {string.Join(" ", item.Tags)}".ToLower();
                    
                    return searchTerms.All(term => searchableText.Contains(term));
                }).ToList();
            }
        }

        public List<CaptureHistoryItem> GetFavorites()
        {
            lock (_lockObject)
            {
                return _historyItems.Where(i => i.IsFavorite && !i.IsArchived).ToList();
            }
        }

        public List<CaptureHistoryItem> GetRecentItems(int count = 10)
        {
            lock (_lockObject)
            {
                return _historyItems.Where(i => !i.IsArchived)
                                  .Take(count)
                                  .ToList();
            }
        }

        public async Task ToggleFavoriteAsync(string itemId)
        {
            lock (_lockObject)
            {
                var item = _historyItems.FirstOrDefault(i => i.Id == itemId);
                if (item != null)
                {
                    item.IsFavorite = !item.IsFavorite;
                }
            }

            await SaveHistoryAsync();
        }

        public async Task ArchiveItemAsync(string itemId)
        {
            lock (_lockObject)
            {
                var item = _historyItems.FirstOrDefault(i => i.Id == itemId);
                if (item != null)
                {
                    item.IsArchived = true;
                }
            }

            await SaveHistoryAsync();
        }

        public async Task ClearHistoryAsync(bool includeFiles = false)
        {
            if (includeFiles)
            {
                // Delete all files
                lock (_lockObject)
                {
                    foreach (var item in _historyItems)
                    {
                        try
                        {
                            if (File.Exists(item.FilePath))
                                File.Delete(item.FilePath);
                            if (File.Exists(item.ThumbnailPath))
                                File.Delete(item.ThumbnailPath);
                        }
                        catch { }
                    }
                }
            }

            lock (_lockObject)
            {
                _historyItems.Clear();
            }

            await SaveHistoryAsync();
            HistoryCleared?.Invoke();
        }

        public long GetTotalStorageSize()
        {
            lock (_lockObject)
            {
                return _historyItems.Sum(i => i.FileSizeBytes);
            }
        }

        public int GetItemCount()
        {
            lock (_lockObject)
            {
                return _historyItems.Count(i => !i.IsArchived);
            }
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryPath))
                {
                    var json = File.ReadAllText(HistoryPath);
                    var items = JsonSerializer.Deserialize<List<CaptureHistoryItem>>(json);
                    
                    if (items != null)
                    {
                        lock (_lockObject)
                        {
                            _historyItems = items.OrderByDescending(i => i.CaptureTime).ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load history: {ex.Message}");
            }
        }

        private async Task SaveHistoryAsync()
        {
            try
            {
                List<CaptureHistoryItem> itemsToSave;
                lock (_lockObject)
                {
                    itemsToSave = new List<CaptureHistoryItem>(_historyItems);
                }

                var json = JsonSerializer.Serialize(itemsToSave, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(HistoryPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save history: {ex.Message}");
            }
        }
    }
}