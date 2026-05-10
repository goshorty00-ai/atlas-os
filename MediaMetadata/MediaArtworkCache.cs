using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AtlasAI.MediaMetadata
{
    public static class MediaArtworkCache
    {
        private static readonly string CacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI",
            "media_cache");

        public static string GetCustomMoviePosterCachePath(string mediaFilePath)
        {
            var key = HashKey(NormalizePathKey(mediaFilePath));
            var dir = Path.Combine(CacheRoot, "custom", "movies");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{key}.jpg");
        }

        public static bool TryGetCustomMoviePosterCachePath(string mediaFilePath, out string? path)
        {
            var normalized = GetCustomMoviePosterCachePath(mediaFilePath);
            if (File.Exists(normalized))
            {
                path = normalized;
                return true;
            }
            path = null;
            return false;
        }

        public static string GetMoviePosterCachePath(string mediaFilePath)
        {
            var key = HashKey(NormalizePathKey(mediaFilePath));
            var dir = Path.Combine(CacheRoot, "tmdb");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{key}.jpg");
        }

        public static bool TryGetMoviePosterCachePath(string mediaFilePath, out string? path)
        {
            var normalized = GetMoviePosterCachePath(mediaFilePath);
            if (File.Exists(normalized))
            {
                path = normalized;
                return true;
            }
            var legacy = GetMoviePosterCachePathLegacy(mediaFilePath);
            if (File.Exists(legacy))
            {
                path = legacy;
                return true;
            }
            path = null;
            return false;
        }

        public static string GetMusicFolderCoverCachePath(string albumFolderPath)
        {
            var key = HashKey(NormalizePathKey(albumFolderPath));
            var dir = Path.Combine(CacheRoot, "musicbrainz");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{key}.jpg");
        }

        public static bool TryGetMusicFolderCoverCachePath(string albumFolderPath, out string? path)
        {
            var normalized = GetMusicFolderCoverCachePath(albumFolderPath);
            if (File.Exists(normalized))
            {
                path = normalized;
                return true;
            }
            var legacy = GetMusicFolderCoverCachePathLegacy(albumFolderPath);
            if (File.Exists(legacy))
            {
                path = legacy;
                return true;
            }
            path = null;
            return false;
        }

        public static string GetCustomMusicFolderCoverCachePath(string albumFolderPath)
        {
            var key = HashKey(NormalizePathKey(albumFolderPath));
            var dir = Path.Combine(CacheRoot, "custom", "music");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{key}.jpg");
        }

        public static bool TryGetCustomMusicFolderCoverCachePath(string albumFolderPath, out string? path)
        {
            var normalized = GetCustomMusicFolderCoverCachePath(albumFolderPath);
            if (File.Exists(normalized))
            {
                path = normalized;
                return true;
            }
            path = null;
            return false;
        }

        private static string GetMoviePosterCachePathLegacy(string mediaFilePath)
        {
            var key = HashKey(mediaFilePath ?? "");
            var dir = Path.Combine(CacheRoot, "tmdb");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{key}.jpg");
        }

        private static string GetMusicFolderCoverCachePathLegacy(string albumFolderPath)
        {
            var key = HashKey(albumFolderPath ?? "");
            var dir = Path.Combine(CacheRoot, "musicbrainz");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{key}.jpg");
        }

        private static string NormalizePathKey(string? input)
        {
            var trimmed = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return "";

            try
            {
                var full = Path.GetFullPath(trimmed);
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return full.ToUpperInvariant();
            }
            catch
            {
                return trimmed.ToUpperInvariant();
            }
        }

        private static string HashKey(string input)
        {
            try
            {
                using var sha1 = SHA1.Create();
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = sha1.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}
