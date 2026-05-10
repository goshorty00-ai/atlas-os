using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaMetadata
{
    public sealed class SoundCloudAlbumMetadataProvider : IMusicAlbumMetadataProvider
    {
        public string Name => "SoundCloud";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(MusicMetadataHub.GetKey("soundcloud_client_id"));

        public Task<MusicAlbumMetadata?> TryGetAlbumAsync(MusicAlbumQuery query, CancellationToken ct)
        {
            return Task.FromResult<MusicAlbumMetadata?>(null);
        }

        public Task<bool> TryDownloadCoverAsync(MusicAlbumQuery query, string destinationPath, CancellationToken ct)
        {
            return Task.FromResult(false);
        }
    }
}
