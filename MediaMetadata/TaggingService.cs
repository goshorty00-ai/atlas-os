using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaMetadata
{
    public class TaggingService
    {
        private static readonly object FileLock = new();

        public bool WriteTags(string filePath, string? title, string? artist, string? album, int? trackNumber, int? discNumber)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                lock (FileLock)
                {
                    using var file = TagLib.File.Create(filePath);
                    if (file == null) return false;

                    bool changed = false;

                    if (title != null && file.Tag.Title != title)
                    {
                        file.Tag.Title = title;
                        changed = true;
                    }

                    if (artist != null)
                    {
                        var performers = file.Tag.Performers ?? Array.Empty<string>();
                        var current = performers.Length > 0 ? performers[0] : "";
                        if (!string.Equals(current, artist, StringComparison.Ordinal))
                        {
                            file.Tag.Performers = new[] { artist };
                            changed = true;
                        }
                    }

                    if (album != null && file.Tag.Album != album)
                    {
                        file.Tag.Album = album;
                        changed = true;
                    }

                    if (trackNumber.HasValue && trackNumber.Value > 0 && file.Tag.Track != (uint)trackNumber.Value)
                    {
                        file.Tag.Track = (uint)trackNumber.Value;
                        changed = true;
                    }

                    if (discNumber.HasValue && discNumber.Value > 0 && file.Tag.Disc != (uint)discNumber.Value)
                    {
                        file.Tag.Disc = (uint)discNumber.Value;
                        changed = true;
                    }

                    if (changed)
                    {
                        file.Save();
                        Debug.WriteLine($"[TaggingService] Saved tags for: {filePath}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaggingService] Error writing tags to {filePath}: {ex.Message}");
                return false;
            }

            return false;
        }

        public (string? title, string? artist, string? album, int track, int disc) ReadTags(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return (null, null, null, 0, 0);

            try
            {
                lock (FileLock)
                {
                    using var file = TagLib.File.Create(filePath);
                    if (file == null) return (null, null, null, 0, 0);

                    var t = file.Tag;
                    return (
                        t.Title,
                        t.FirstPerformer,
                        t.Album,
                        (int)t.Track,
                        (int)t.Disc
                    );
                }
            }
            catch
            {
                return (null, null, null, 0, 0);
            }
        }

        public (string? title, string? artist, string? album, int track, int disc, int year, List<string> genres) ReadTagsExtended(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return (null, null, null, 0, 0, 0, new List<string>());

            try
            {
                lock (FileLock)
                {
                    using var file = TagLib.File.Create(filePath);
                    if (file == null) return (null, null, null, 0, 0, 0, new List<string>());

                    var t = file.Tag;
                    var genres = (t.Genres ?? Array.Empty<string>())
                        .Select(g => (g ?? "").Trim())
                        .Where(g => !string.IsNullOrWhiteSpace(g))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToList();

                    var year = (int)t.Year;
                    if (year < 0) year = 0;

                    return (
                        t.Title,
                        t.FirstPerformer,
                        t.Album,
                        (int)t.Track,
                        (int)t.Disc,
                        year,
                        genres
                    );
                }
            }
            catch
            {
                return (null, null, null, 0, 0, 0, new List<string>());
            }
        }
    }
}
