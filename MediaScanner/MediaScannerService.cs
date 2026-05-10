using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;

using AtlasAI.Core;

namespace AtlasAI.MediaScanner
{
    /// <summary>
    /// Atlas AI Media Scanner Service - Async media file indexing with metadata extraction
    /// Supports all common media formats with album-based music organization
    /// </summary>
    public class MediaScannerService
    {
        private readonly MediaIndexStore _indexStore;
        private readonly object _scanLock = new();
        private bool _isScanning = false;
        private CancellationTokenSource? _scanCancellation;

        // Supported file extensions by media type - ALL formats (LibVLC supports everything)
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".flv", ".wmv", ".mpg", ".mpeg", ".m2ts", ".ts"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg", ".aac", ".wma", ".m4a", ".opus", ".ape", ".alac", ".cdg", ".zip"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"
        };

        public event EventHandler<ScanProgressEventArgs>? ScanProgress;
        public event EventHandler<ScanCompletedEventArgs>? ScanCompleted;

        public MediaScannerService()
        {
            _indexStore = new MediaIndexStore();
        }

        public bool IsScanning
        {
            get
            {
                lock (_scanLock)
                {
                    return _isScanning;
                }
            }
        }

        /// <summary>
        /// Start a full media scan of all configured folders
        /// </summary>
        public async Task<ScanResult> StartFullScanAsync()
        {
            lock (_scanLock)
            {
                if (_isScanning)
                {
                    return new ScanResult { Success = false, Message = "Scan already in progress" };
                }
                _isScanning = true;
                _scanCancellation = new CancellationTokenSource();
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("[MediaScanner] Starting full media scan");
                
                var preferences = PreferencesStore.Instance.Current;
                var scanTasks = new List<Task<SectionScanResult>>();

                // Scan each media section
                if (!string.IsNullOrEmpty(preferences.MoviesFolders))
                    scanTasks.Add(ScanSectionAsync("Movies", preferences.MoviesFolders, _scanCancellation.Token));

                if (!string.IsNullOrEmpty(preferences.TVShowsFolders))
                    scanTasks.Add(ScanSectionAsync("TVShows", preferences.TVShowsFolders, _scanCancellation.Token));

                if (!string.IsNullOrEmpty(preferences.MusicFolders))
                    scanTasks.Add(ScanSectionAsync("Music", preferences.MusicFolders, _scanCancellation.Token));

                if (!string.IsNullOrEmpty(preferences.GamesFolders))
                    scanTasks.Add(ScanSectionAsync("Games", preferences.GamesFolders, _scanCancellation.Token));

                if (!string.IsNullOrEmpty(preferences.ImagesFolders))
                    scanTasks.Add(ScanSectionAsync("Images", preferences.ImagesFolders, _scanCancellation.Token));

                if (!string.IsNullOrEmpty(preferences.KaraokeFolders))
                    scanTasks.Add(ScanSectionAsync("Karaoke", preferences.KaraokeFolders, _scanCancellation.Token));

                if (!string.IsNullOrEmpty(preferences.CollectionsFolders))
                    scanTasks.Add(ScanSectionAsync("Collections", preferences.CollectionsFolders, _scanCancellation.Token));

                var results = await Task.WhenAll(scanTasks);
                
                // Collect all media items and albums
                var allMediaItems = new List<MediaItem>();
                var allAlbums = new List<AlbumInfo>();
                
                foreach (var result in results)
                {
                    allMediaItems.AddRange(result.MediaItems);
                    allAlbums.AddRange(result.Albums);
                }
                
                // Save to index store
                await _indexStore.SaveScanResultsAsync(allMediaItems, allAlbums);
                
                // Update last scan time
                PreferencesStore.Instance.Update(prefs => prefs.LastMediaScan = DateTime.UtcNow);

                var totalFiles = results.Sum(r => r.FilesFound);
                var totalErrors = results.Sum(r => r.Errors);

                var scanResult = new ScanResult
                {
                    Success = true,
                    FilesScanned = totalFiles,
                    Errors = totalErrors,
                    Message = $"Scan completed: {totalFiles} files indexed, {totalErrors} errors"
                };

                ScanCompleted?.Invoke(this, new ScanCompletedEventArgs(scanResult));
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] {scanResult.Message}");

                return scanResult;
            }
            catch (OperationCanceledException)
            {
                return new ScanResult { Success = false, Message = "Scan was cancelled" };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Scan error: {ex.Message}");
                return new ScanResult { Success = false, Message = $"Scan failed: {ex.Message}" };
            }
            finally
            {
                lock (_scanLock)
                {
                    _isScanning = false;
                    _scanCancellation?.Dispose();
                    _scanCancellation = null;
                }
            }
        }

        /// <summary>
        /// Scan a specific media section (Movies, Music, etc.)
        /// </summary>
        private async Task<SectionScanResult> ScanSectionAsync(string sectionName, string folderPaths, CancellationToken cancellationToken)
        {
            var result = new SectionScanResult { SectionName = sectionName };
            var folders = folderPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var folderPath in folders)
            {
                var trimmedPath = folderPath.Trim();
                if (!Directory.Exists(trimmedPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[MediaScanner] Folder not found: {trimmedPath}");
                    result.Errors++;
                    continue;
                }

                try
                {
                    await ScanFolderAsync(sectionName, trimmedPath, result, cancellationToken);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MediaScanner] Error scanning {trimmedPath}: {ex.Message}");
                    result.Errors++;
                }
            }

            // Special handling for Music section - organize into albums
            if (sectionName == "Music")
            {
                result.Albums = await OrganizeIntoAlbumsAsync(result.MediaItems);
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Music: {result.Albums.Count} albums from {result.FilesFound} files");
            }

            return result;
        }

        /// <summary>
        /// Recursively scan a folder for media files
        /// </summary>
        private async Task ScanFolderAsync(string sectionName, string folderPath, SectionScanResult result, CancellationToken cancellationToken)
        {
            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);
                
                foreach (var filePath in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // PRIORITY ZIP HANDLING: Always check for zip packs first, regardless of extension lists
                    if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                         try
                         {
                             // Only expand zips if they are in the Karaoke section OR if they are > 1MB (likely a pack)
                             // Small zips might be subtitle packs or something else, but safe to check
                             
                             // CRITICAL: We need to know the sectionName here. 
                             // If it's NOT Karaoke, we might not want to expand it unless it's a known media archive?
                             // User specifically asked for Karaoke zip packs.
                             // Let's force it for Karaoke section, and maybe others if we want.
                             
                             if (sectionName == "Karaoke" || sectionName == "Music")
                             {
                                 var zipItems = await ScanKaraokeZipPackAsync(filePath, sectionName);
                                 if (zipItems.Count > 0)
                                 {
                                     result.MediaItems.AddRange(zipItems);
                                     result.FilesFound += zipItems.Count;
                                     
                                     ScanProgress?.Invoke(this, new ScanProgressEventArgs
                                     {
                                         SectionName = sectionName,
                                         FilesProcessed = result.FilesFound,
                                         CurrentFile = Path.GetFileName(filePath) + " [UNZIPPED]"
                                     });
                                     
                                     // Skip standard processing
                                     continue;
                                 }
                             }
                             
                             // If not expanded or empty, fall through to standard processing
                         }
                         catch (Exception ex)
                         {
                             // Just log and fall through to standard processing
                             System.Diagnostics.Debug.WriteLine($"[MediaScanner] Zip scan error: {ex.Message}");
                         }
                    }

                    if (IsMediaFile(filePath))
                    {
                        try
                        {
                            var mediaItem = await CreateMediaItemAsync(filePath, sectionName);
                            if (mediaItem != null)
                            {
                                result.MediaItems.Add(mediaItem);
                                result.FilesFound++;

                                // Report progress every 10 files
                                if (result.FilesFound % 10 == 0)
                                {
                                    ScanProgress?.Invoke(this, new ScanProgressEventArgs
                                    {
                                        SectionName = sectionName,
                                        FilesProcessed = result.FilesFound,
                                        CurrentFile = Path.GetFileName(filePath)
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MediaScanner] Error processing {filePath}: {ex.Message}");
                            result.Errors++;
                        }
                    }

                    // Yield control to prevent UI blocking
                    if (result.FilesFound % 50 == 0)
                    {
                        await Task.Delay(1, cancellationToken);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Access denied: {folderPath}");
                result.Errors++;
            }
            catch (DirectoryNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Directory not found: {folderPath}");
                result.Errors++;
            }
        }

        private async Task<List<MediaItem>> ScanKaraokeZipPackAsync(string zipPath, string sectionName)
        {
            var items = new List<MediaItem>();
            try
            {
                // We use Task.Run to offload zip scanning
                return await Task.Run(() =>
                {
                    var result = new List<MediaItem>();
                    // Allow reading even if file is open elsewhere
                    using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
                    
                    // RECURSIVE SCANNING: Look for zips inside zips too!
                    var entries = archive.Entries.ToList();
                    
                    // 1. Look for standard audio/video
                    var mediaEntries = entries
                        .Where(e => !e.FullName.EndsWith("/") && 
                                    (e.Name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
                                     e.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                     e.Name.EndsWith(".wma", StringComparison.OrdinalIgnoreCase) ||
                                     e.Name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                                     e.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                     e.Name.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                     e.Name.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                     e.Name.EndsWith(".cdg", StringComparison.OrdinalIgnoreCase) && !HasMatchingAudio(archive, e)))
                        .ToList();

                    foreach (var entry in mediaEntries)
                    {
                        var baseName = Path.GetFileNameWithoutExtension(entry.Name);
                        var internalPath = entry.FullName;
                        var specialPath = $"{zipPath}|{internalPath}";
                        
                        var item = new MediaItem
                        {
                            FilePath = specialPath,
                            DisplayName = baseName,
                            Extension = Path.GetExtension(entry.Name).ToLowerInvariant(),
                            FileSize = entry.Length,
                            LastModified = entry.LastWriteTime.UtcDateTime,
                            MediaType = MediaType.Video,
                            SectionName = sectionName,
                            FolderPath = Path.GetDirectoryName(zipPath) ?? "",
                            DateAdded = DateTime.UtcNow
                        };
                        
                        ExtractAudioMetadata(item);
                        result.Add(item);
                    }

                    // 2. Look for nested ZIPs (e.g. SFKK01.zip inside the main zip)
                    var nestedZips = entries
                        .Where(e => !e.FullName.EndsWith("/") && e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var zipEntry in nestedZips)
                    {
                         // We treat nested zips as playable items too. 
                         // The player will have to extract them and then play them (or extract them and scan them?)
                         // For now, let's just add them as items. 
                         // If the user plays them, the player needs to handle "Path|InternalZip".
                         // BUT, if it's a zip of a single song (mp3+cdg), playing it is fine.
                         
                        var baseName = Path.GetFileNameWithoutExtension(zipEntry.Name);
                        var internalPath = zipEntry.FullName;
                        var specialPath = $"{zipPath}|{internalPath}";
                        
                        var item = new MediaItem
                        {
                            FilePath = specialPath,
                            DisplayName = baseName,
                            Extension = ".zip",
                            FileSize = zipEntry.Length,
                            LastModified = zipEntry.LastWriteTime.UtcDateTime,
                            MediaType = MediaType.Video,
                            SectionName = sectionName,
                            FolderPath = Path.GetDirectoryName(zipPath) ?? "",
                            DateAdded = DateTime.UtcNow
                        };
                        
                        // Treat name as metadata
                        ExtractAudioMetadata(item);
                        result.Add(item);
                    }

                    return result;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Error scanning zip contents {zipPath}: {ex.Message}");
                // If we found some items before error, return them? No, exception aborts all.
                // Throwing up to caller to handle fallback.
                throw; 
            }
        }

        private bool HasMatchingAudio(ZipArchive archive, ZipArchiveEntry cdgEntry)
        {
            var baseName = Path.GetFileNameWithoutExtension(cdgEntry.Name);
            var dir = Path.GetDirectoryName(cdgEntry.FullName);
            
            return archive.Entries.Any(e => 
            {
                if (Path.GetDirectoryName(e.FullName) != dir) return false;
                var name = Path.GetFileNameWithoutExtension(e.Name);
                if (!string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase)) return false;
                
                var ext = Path.GetExtension(e.Name);
                return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) || 
                       ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".wma", StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Check if a file is a supported media file
        /// </summary>
        private bool IsMediaFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return VideoExtensions.Contains(extension) || 
                   AudioExtensions.Contains(extension) || 
                   ImageExtensions.Contains(extension);
        }

        /// <summary>
        /// Create a MediaItem from a file path with metadata extraction
        /// </summary>
        private async Task<MediaItem?> CreateMediaItemAsync(string filePath, string sectionName)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var extension = fileInfo.Extension.ToLowerInvariant();

                var mediaType = GetMediaType(extension);
                if (mediaType == MediaType.Unknown)
                    return null;

                var mediaItem = new MediaItem
                {
                    FilePath = filePath,
                    DisplayName = Path.GetFileNameWithoutExtension(filePath),
                    Extension = extension,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    MediaType = mediaType,
                    SectionName = sectionName,
                    FolderPath = Path.GetDirectoryName(filePath) ?? "",
                    DateAdded = DateTime.UtcNow
                };

                // Extract additional metadata based on type
                await ExtractMetadataAsync(mediaItem);

                return mediaItem;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Error creating MediaItem for {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Determine media type from file extension
        /// </summary>
        private MediaType GetMediaType(string extension)
        {
            if (VideoExtensions.Contains(extension)) return MediaType.Video;
            if (AudioExtensions.Contains(extension)) return MediaType.Audio;
            if (ImageExtensions.Contains(extension)) return MediaType.Image;
            return MediaType.Unknown;
        }

        /// <summary>
        /// Extract metadata from media files (basic implementation)
        /// </summary>
        private async Task ExtractMetadataAsync(MediaItem mediaItem)
        {
            try
            {
                // Basic metadata extraction - can be enhanced with libraries like MediaInfo.NET
                switch (mediaItem.MediaType)
                {
                    case MediaType.Video:
                        // For videos, try to extract duration, resolution, etc.
                        // CRITICAL: For Karaoke (zip/cdg/mp4) in Karaoke folder, we WANT audio metadata (Artist - Title)
                        if (mediaItem.Extension == ".zip" || mediaItem.Extension == ".cdg" || mediaItem.SectionName == "Karaoke")
                        {
                            ExtractAudioMetadata(mediaItem);
                        }
                        break;

                    case MediaType.Audio:
                        // For audio, extract album, artist, track info from folder structure
                        ExtractAudioMetadata(mediaItem);
                        break;

                    case MediaType.Image:
                        // For images, could extract EXIF data, dimensions, etc.
                        break;
                }

                await Task.CompletedTask; // Placeholder for async metadata extraction
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Metadata extraction error for {mediaItem.FilePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract audio metadata from folder structure and filename
        /// </summary>
        private void ExtractAudioMetadata(MediaItem mediaItem)
        {
            try
            {
                var folderName = Path.GetFileName(mediaItem.FolderPath);
                var fileName = Path.GetFileNameWithoutExtension(mediaItem.FilePath);

                // Try to extract album and artist from folder structure
                // Format: Artist - Album or just Album
                if (folderName.Contains(" - "))
                {
                    var parts = folderName.Split(" - ", 2);
                    mediaItem.Artist = parts[0].Trim();
                    mediaItem.Album = parts[1].Trim();
                }
                else
                {
                    mediaItem.Album = folderName;
                }

                // Try to extract track number from filename
                // Format: 01 - Track Name or 01. Track Name
                if (fileName.Length > 2 && char.IsDigit(fileName[0]) && char.IsDigit(fileName[1]))
                {
                    if (fileName[2] == '-' || fileName[2] == '.' || fileName[2] == ' ')
                    {
                        if (int.TryParse(fileName.Substring(0, 2), out int trackNumber))
                        {
                            mediaItem.TrackNumber = trackNumber;
                            mediaItem.DisplayName = fileName.Substring(3).Trim(' ', '-', '.').Trim();
                        }
                    }
                }

                // If no album detected, use "Unknown Album"
                if (string.IsNullOrEmpty(mediaItem.Album))
                {
                    mediaItem.Album = "Unknown Album";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Audio metadata extraction error: {ex.Message}");
                mediaItem.Album = "Unknown Album";
            }
        }

        /// <summary>
        /// Organize audio files into albums (MANDATORY for music)
        /// </summary>
        private async Task<List<AlbumInfo>> OrganizeIntoAlbumsAsync(List<MediaItem> audioFiles)
        {
            var albums = new Dictionary<string, AlbumInfo>();

            foreach (var audioFile in audioFiles.Where(f => f.MediaType == MediaType.Audio))
            {
                var albumKey = $"{audioFile.Artist ?? "Unknown Artist"}|{audioFile.Album ?? "Unknown Album"}";
                
                if (!albums.TryGetValue(albumKey, out var album))
                {
                    album = new AlbumInfo
                    {
                        AlbumTitle = audioFile.Album ?? "Unknown Album",
                        Artist = audioFile.Artist ?? "Unknown Artist",
                        SourceFolderPath = audioFile.FolderPath,
                        Tracks = new List<MediaItem>()
                    };
                    albums[albumKey] = album;
                }

                album.Tracks.Add(audioFile);
            }

            // Sort tracks within each album and calculate totals
            foreach (var album in albums.Values)
            {
                album.Tracks = album.Tracks.OrderBy(t => t.TrackNumber ?? 999)
                                         .ThenBy(t => t.DisplayName)
                                         .ToList();
                
                album.TrackCount = album.Tracks.Count;
                
                // Look for cover art
                album.CoverArtPath = FindAlbumCoverArt(album.SourceFolderPath);
            }

            // CRITICAL FIX: Ensure Karaoke files are NOT grouped into albums if they are loose files
            // or if the user wants them separate. For now, we only group strictly by metadata.
            // (No change needed here as we use metadata, but just a comment for clarity)

            await Task.CompletedTask;
            return albums.Values.OrderBy(a => a.Artist).ThenBy(a => a.AlbumTitle).ToList();
        }

        /// <summary>
        /// Find album cover art in the album folder
        /// </summary>
        private string? FindAlbumCoverArt(string folderPath)
        {
            try
            {
                var coverNames = new[] { "cover.jpg", "folder.jpg", "album.jpg", "cover.png", "folder.png", "album.png", "front.jpg", "front.png" };
                
                // First check common names
                foreach (var coverName in coverNames)
                {
                    var coverPath = Path.Combine(folderPath, coverName);
                    if (File.Exists(coverPath))
                    {
                        return coverPath;
                    }
                }
                
                // Then check any jpg/png in the folder if it's the only one or named similar to folder
                var images = Directory.GetFiles(folderPath, "*.*")
                                     .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                                 f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                     .ToList();
                                     
                if (images.Count > 0) return images[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Cover art search error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Cancel the current scan operation
        /// </summary>
        public void CancelScan()
        {
            lock (_scanLock)
            {
                _scanCancellation?.Cancel();
            }
        }

        /// <summary>
        /// Get media items for a specific section
        /// </summary>
        public async Task<List<MediaItem>> GetMediaItemsAsync(string sectionName)
        {
            return await _indexStore.GetMediaItemsAsync(sectionName);
        }

        /// <summary>
        /// Get albums for the Music section
        /// </summary>
        public async Task<List<AlbumInfo>> GetAlbumsAsync()
        {
            return await _indexStore.GetAlbumsAsync();
        }
    }
}