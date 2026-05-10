// Media Library Scanner - MVP Implementation
// Scans file system for media files and categorizes them by type

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaScanner
{
    /// <summary>
    /// Progress report for media scanning
    /// </summary>
    public class MediaScanProgress
    {
        public int FilesProcessed { get; set; }
        public int TotalFiles { get; set; }
        public string CurrentPath { get; set; } = "";
        public double ProgressPercentage => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles * 100 : 0;
    }

    /// <summary>
    /// Result of a media scan operation
    /// </summary>
    public class MediaScanResult
    {
        public bool Success { get; set; }
        public List<MediaItem> Items { get; set; } = new List<MediaItem>();
        public int FilesScanned { get; set; }
        public int Errors { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Scans file system for media files
    /// </summary>
    public class MediaLibraryScanner
    {
        // Extension sets for media type detection
        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp"
        };

        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".alac"
        };

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".svg", ".ico"
        };

        /// <summary>
        /// Scans specified root directories for media files
        /// </summary>
        public async Task<MediaScanResult> ScanAsync(
            IEnumerable<string> roots,
            CancellationToken ct = default,
            IProgress<MediaScanProgress>? progress = null)
        {
            var startTime = DateTime.UtcNow;
            var result = new MediaScanResult { Success = true };
            var items = new List<MediaItem>();
            int filesProcessed = 0;
            int errors = 0;

            try
            {
                foreach (var root in roots)
                {
                    if (!Directory.Exists(root))
                    {
                        System.Diagnostics.Debug.WriteLine($"[MediaScanner] Root does not exist: {root}");
                        continue;
                    }

                    System.Diagnostics.Debug.WriteLine($"[MediaScanner] Scanning: {root}");

                    await Task.Run(() =>
                    {
                        try
                        {
                            var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);

                            foreach (var filePath in files)
                            {
                                // Check cancellation every 250 files
                                if (filesProcessed % 250 == 0)
                                {
                                    ct.ThrowIfCancellationRequested();
                                }

                                try
                                {
                                    var extension = Path.GetExtension(filePath);
                                    var mediaType = GetMediaType(extension);

                                    // Only process recognized media files
                                    if (mediaType != MediaType.Unknown)
                                    {
                                        var fileInfo = new FileInfo(filePath);
                                        var item = new MediaItem
                                        {
                                            FilePath = filePath,
                                            DisplayName = Path.GetFileNameWithoutExtension(filePath),
                                            Extension = extension,
                                            FileSize = fileInfo.Length,
                                            LastModified = fileInfo.LastWriteTimeUtc,
                                            MediaType = mediaType,
                                            DateAdded = DateTime.UtcNow
                                        };

                                        items.Add(item);
                                    }

                                    filesProcessed++;

                                    // Report progress every 10 files (more frequent) and add small delay
                                    if (filesProcessed % 10 == 0)
                                    {
                                        ct.ThrowIfCancellationRequested();
                                        
                                        if (progress != null)
                                        {
                                            progress.Report(new MediaScanProgress
                                            {
                                                FilesProcessed = filesProcessed,
                                                TotalFiles = filesProcessed,
                                                CurrentPath = filePath
                                            });
                                        }
                                        
                                        // Small delay to make progress visible (10ms per 10 files)
                                        System.Threading.Thread.Sleep(10);
                                    }
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    errors++;
                                    // Skip files we can't access
                                }
                                catch (PathTooLongException)
                                {
                                    errors++;
                                    // Skip paths that are too long
                                }
                                catch (IOException)
                                {
                                    errors++;
                                    // Skip files with IO errors
                                }
                            }
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MediaScanner] Access denied to: {root} - {ex.Message}");
                            errors++;
                        }
                        catch (IOException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MediaScanner] IO error scanning: {root} - {ex.Message}");
                            errors++;
                        }
                    }, ct);
                }

                result.Items = items;
                result.FilesScanned = filesProcessed;
                result.Errors = errors;
                result.Duration = DateTime.UtcNow - startTime;
                result.CompletedAt = DateTime.UtcNow;

                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Scan complete: {items.Count} media files found, {filesProcessed} files scanned, {errors} errors");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Scan cancelled");
                result.Success = false;
                result.FilesScanned = filesProcessed;
                result.Errors = errors;
                result.Duration = DateTime.UtcNow - startTime;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaScanner] Scan failed: {ex.Message}");
                result.Success = false;
                result.FilesScanned = filesProcessed;
                result.Errors = errors;
                result.Duration = DateTime.UtcNow - startTime;
            }

            return result;
        }

        /// <summary>
        /// Determines media type from file extension
        /// </summary>
        private MediaType GetMediaType(string extension)
        {
            if (VideoExtensions.Contains(extension))
                return MediaType.Video;
            if (AudioExtensions.Contains(extension))
                return MediaType.Audio;
            if (ImageExtensions.Contains(extension))
                return MediaType.Image;

            return MediaType.Unknown;
        }

#if DEBUG
        /// <summary>
        /// Test method for scanner (DEBUG only)
        /// </summary>
        public static async Task TestScanAsync()
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI", "scanner_test.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            
            using (var writer = new StreamWriter(logPath, false))
            {
                await writer.WriteLineAsync("[MediaScanner] === TEST SCAN START ===");
                await writer.WriteLineAsync($"[MediaScanner] Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                var scanner = new MediaLibraryScanner();
                var testRoots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) };
                
                await writer.WriteLineAsync($"[MediaScanner] Scanning: {testRoots[0]}");

                var progress = new Progress<MediaScanProgress>(p =>
                {
                    writer.WriteLine($"[MediaScanner] Progress: {p.FilesProcessed} files processed");
                    writer.Flush();
                });

                var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout for test

                var result = await scanner.ScanAsync(testRoots, cts.Token, progress);

                await writer.WriteLineAsync($"[MediaScanner] === TEST SCAN COMPLETE ===");
                await writer.WriteLineAsync($"[MediaScanner] Success: {result.Success}");
                await writer.WriteLineAsync($"[MediaScanner] Media files found: {result.Items.Count}");
                await writer.WriteLineAsync($"[MediaScanner] Files scanned: {result.FilesScanned}");
                await writer.WriteLineAsync($"[MediaScanner] Errors: {result.Errors}");
                await writer.WriteLineAsync($"[MediaScanner] Duration: {result.Duration.TotalSeconds:F2}s");

                // Show breakdown by type
                var videoCount = result.Items.Count(i => i.MediaType == MediaType.Video);
                var audioCount = result.Items.Count(i => i.MediaType == MediaType.Audio);
                var imageCount = result.Items.Count(i => i.MediaType == MediaType.Image);

                await writer.WriteLineAsync($"[MediaScanner] Videos: {videoCount}, Audio: {audioCount}, Images: {imageCount}");
                
                // Show first 5 items as sample
                if (result.Items.Count > 0)
                {
                    await writer.WriteLineAsync($"[MediaScanner] Sample files:");
                    foreach (var item in result.Items.Take(5))
                    {
                        await writer.WriteLineAsync($"  - {item.DisplayName} ({item.MediaType}, {item.FileSize / 1024 / 1024:F2} MB)");
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[MediaScanner] Test log written to: {logPath}");
        }
#endif
    }
}
