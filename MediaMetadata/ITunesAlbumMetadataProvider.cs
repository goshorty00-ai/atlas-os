using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaMetadata
{
    public sealed class ITunesAlbumMetadataProvider : IMusicAlbumMetadataProvider
    {
        private readonly iTunesClient _client = new iTunesClient();

        public string Name => "iTunes";
        public bool IsConfigured => true;

        public async Task<MusicAlbumMetadata?> TryGetAlbumAsync(MusicAlbumQuery query, CancellationToken ct)
        {
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album)) return null;

            var info = await _client.SearchAlbumAsync(artist, album, ct).ConfigureAwait(false);
            if (info == null) return null;

            var meta = new MusicAlbumMetadata
            {
                Provider = Name,
                ReleaseId = (info.CollectionId ?? "").Trim(),
                ReleaseDate = (info.ReleaseDate ?? "").Trim(),
                Year = 0,
                Genres = new List<string>()
            };

            if (!string.IsNullOrWhiteSpace(info.PrimaryGenre))
                meta.Genres.Add(info.PrimaryGenre.Trim());

            if (!string.IsNullOrWhiteSpace(meta.ReleaseDate) && meta.ReleaseDate.Length >= 4)
            {
                if (int.TryParse(meta.ReleaseDate.Substring(0, 4), out var y) && y > 0) meta.Year = y;
            }

            if (!string.IsNullOrWhiteSpace(meta.ReleaseId))
                meta.ReleaseUrl = $"https://music.apple.com/album/{meta.ReleaseId}";

            return meta;
        }

        public async Task<bool> TryDownloadCoverAsync(MusicAlbumQuery query, string destinationPath, CancellationToken ct)
        {
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album)) return false;
            return await _client.TryDownloadCoverAsync(artist, album, destinationPath, ct).ConfigureAwait(false);
        }
    }
}
