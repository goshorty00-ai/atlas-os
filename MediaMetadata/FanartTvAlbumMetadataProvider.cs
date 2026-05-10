using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaMetadata
{
    public sealed class FanartTvAlbumMetadataProvider : IMusicAlbumMetadataProvider
    {
        public string Name => "fanart.tv";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(MusicMetadataHub.GetKey("fanarttv_key"));

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
