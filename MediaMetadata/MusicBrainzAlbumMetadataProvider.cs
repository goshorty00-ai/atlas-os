using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaMetadata
{
    public sealed class MusicBrainzAlbumMetadataProvider : IMusicAlbumMetadataProvider
    {
        private readonly MusicBrainzClient _client = new MusicBrainzClient();

        public string Name => "MusicBrainz";
        public bool IsConfigured => true;

        public async Task<MusicAlbumMetadata?> TryGetAlbumAsync(MusicAlbumQuery query, CancellationToken ct)
        {
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album)) return null;

            var mbContact = (MusicMetadataHub.GetKey("musicbrainz_contact") ?? "").Trim();
            var effectiveContact = string.IsNullOrWhiteSpace(mbContact) ? "AtlasAI" : mbContact;

            var tracks = await _client.SearchReleaseMetadataAsync(effectiveContact, artist, album, ct).ConfigureAwait(false);
            if (tracks == null || tracks.Count == 0) return null;

            var first = tracks.FirstOrDefault();

            var mapped = tracks
                .Where(t => t != null && t.TrackNumber > 0)
                .OrderBy(t => t.DiscNumber)
                .ThenBy(t => t.TrackNumber)
                .Select(t => new Views.ViewModels.AlbumDetailTrack
                {
                    DiscNumber = t.DiscNumber <= 0 ? 1 : t.DiscNumber,
                    TrackNumber = t.TrackNumber,
                    Title = (t.Title ?? "").Trim(),
                    Artist = (t.Artist ?? "").Trim(),
                    RecordingId = (t.RecordingId ?? "").Trim()
                })
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .ToList();

            var meta = new MusicAlbumMetadata
            {
                Provider = Name,
                ReleaseId = (first?.ReleaseId ?? "").Trim(),
                ReleaseDate = (first?.ReleaseDate ?? "").Trim(),
                Country = (first?.Country ?? "").Trim(),
                Label = (first?.Label ?? "").Trim(),
                Barcode = (first?.Barcode ?? "").Trim(),
                Status = (first?.Status ?? "").Trim(),
                Packaging = (first?.Packaging ?? "").Trim(),
                Tracks = mapped
            };

            if (!string.IsNullOrWhiteSpace(meta.ReleaseId))
                meta.ReleaseUrl = $"https://musicbrainz.org/release/{meta.ReleaseId}";

            if (!string.IsNullOrWhiteSpace(meta.ReleaseDate) && meta.ReleaseDate.Length >= 4)
            {
                if (int.TryParse(meta.ReleaseDate.Substring(0, 4), out var y) && y > 0) meta.Year = y;
            }

            return meta;
        }

        public async Task<bool> TryDownloadCoverAsync(MusicAlbumQuery query, string destinationPath, CancellationToken ct)
        {
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album)) return false;

            var mbContact = (MusicMetadataHub.GetKey("musicbrainz_contact") ?? "").Trim();
            var effectiveContact = string.IsNullOrWhiteSpace(mbContact) ? "AtlasAI" : mbContact;

            return await _client.TryDownloadAlbumArtAsync(effectiveContact, artist, album, destinationPath, ct).ConfigureAwait(false);
        }
    }
}
