using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.MediaMetadata
{
    public sealed class MusicMetadataHub
    {
        public static MusicMetadataHub Instance { get; } = new MusicMetadataHub();

        private readonly List<IMusicAlbumMetadataProvider> _providers;

        private MusicMetadataHub()
        {
            _providers = new List<IMusicAlbumMetadataProvider>
            {
                new MusicBrainzAlbumMetadataProvider(),
                new DeezerAlbumMetadataProvider(),
                new SpotifyAlbumMetadataProvider(),
                new DiscogsAlbumMetadataProvider(),
                new SoundCloudAlbumMetadataProvider(),
                new LastFmAlbumMetadataProvider(),
                new ITunesAlbumMetadataProvider(),
                new FanartTvAlbumMetadataProvider()
            };
        }

        public IReadOnlyList<IMusicAlbumMetadataProvider> Providers => _providers;

        public async Task<MusicAlbumMetadata?> GetBestAlbumAsync(MusicAlbumQuery query, CancellationToken ct)
        {
            var q = query ?? new MusicAlbumQuery();
            var candidates = new List<(MusicAlbumMetadata meta, int score)>();

            foreach (var p in _providers)
            {
                ct.ThrowIfCancellationRequested();
                if (p == null) continue;
                if (!p.IsConfigured && p.Name != "MusicBrainz" && p.Name != "iTunes") continue;
                MusicAlbumMetadata? meta = null;
                try { meta = await p.TryGetAlbumAsync(q, ct).ConfigureAwait(false); } catch { meta = null; }
                if (meta == null) continue;
                var score = Score(q, meta);
                candidates.Add((meta, score));
            }

            if (candidates.Count == 0) return null;
            var best = candidates.OrderByDescending(c => c.score).First().meta;

            var itunes = candidates.FirstOrDefault(c => string.Equals(c.meta.Provider, "iTunes", StringComparison.OrdinalIgnoreCase)).meta;
            if (itunes != null && !ReferenceEquals(itunes, best))
            {
                if (string.IsNullOrWhiteSpace(best.ReleaseDate) && !string.IsNullOrWhiteSpace(itunes.ReleaseDate)) best.ReleaseDate = itunes.ReleaseDate;
                if (best.Year <= 0 && itunes.Year > 0) best.Year = itunes.Year;
                if ((best.Genres == null || best.Genres.Count == 0) && itunes.Genres != null && itunes.Genres.Count > 0) best.Genres = itunes.Genres;
            }

            if (string.IsNullOrWhiteSpace(best.ReleaseUrl) && !string.IsNullOrWhiteSpace(best.ReleaseId) && string.Equals(best.Provider, "MusicBrainz", StringComparison.OrdinalIgnoreCase))
                best.ReleaseUrl = $"https://musicbrainz.org/release/{best.ReleaseId}";

            return best;
        }

        public async Task<bool> TryDownloadBestCoverAsync(MusicAlbumQuery query, string destinationPath, CancellationToken ct)
        {
            var q = query ?? new MusicAlbumQuery();
            if (string.IsNullOrWhiteSpace(destinationPath)) return false;
            try
            {
                var folder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            }
            catch
            {
            }

            foreach (var p in _providers)
            {
                ct.ThrowIfCancellationRequested();
                if (p == null) continue;
                if (!p.IsConfigured && p.Name != "MusicBrainz" && p.Name != "iTunes") continue;
                try
                {
                    var ok = await p.TryDownloadCoverAsync(q, destinationPath, ct).ConfigureAwait(false);
                    if (ok && File.Exists(destinationPath)) return true;
                }
                catch
                {
                }
            }
            return false;
        }

        private static int Score(MusicAlbumQuery q, MusicAlbumMetadata meta)
        {
            var score = 0;
            if (meta.Tracks != null && meta.Tracks.Count > 0) score += 80 + Math.Min(60, meta.Tracks.Count);
            if (!string.IsNullOrWhiteSpace(meta.ReleaseDate)) score += 10;
            if (!string.IsNullOrWhiteSpace(meta.Label)) score += 6;
            if (!string.IsNullOrWhiteSpace(meta.Barcode)) score += 4;
            if (meta.Genres != null && meta.Genres.Count > 0) score += Math.Min(12, meta.Genres.Count * 3);
            if (meta.Year > 0) score += 6;

            if (q.ExpectedTrackCount.HasValue && meta.Tracks != null && meta.Tracks.Count > 0)
            {
                var diff = Math.Abs(q.ExpectedTrackCount.Value - meta.Tracks.Count);
                score += Math.Max(0, 60 - diff * 10);
            }

            if (q.ExpectedYear.HasValue && meta.Year > 0)
            {
                var ydiff = Math.Abs(q.ExpectedYear.Value - meta.Year);
                score += Math.Max(0, 20 - ydiff * 4);
            }

            if (string.Equals(meta.Provider, "Spotify", StringComparison.OrdinalIgnoreCase)) score += 8;
            if (string.Equals(meta.Provider, "Discogs", StringComparison.OrdinalIgnoreCase)) score += 6;
            if (string.Equals(meta.Provider, "MusicBrainz", StringComparison.OrdinalIgnoreCase)) score += 4;
            return score;
        }

        public static string GetKey(string key) => (IntegrationKeyStore.GetDecrypted(key) ?? "").Trim();
    }
}
