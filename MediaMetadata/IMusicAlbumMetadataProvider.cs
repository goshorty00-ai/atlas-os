using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaMetadata
{
    public interface IMusicAlbumMetadataProvider
    {
        string Name { get; }
        bool IsConfigured { get; }
        Task<MusicAlbumMetadata?> TryGetAlbumAsync(MusicAlbumQuery query, CancellationToken ct);
        Task<bool> TryDownloadCoverAsync(MusicAlbumQuery query, string destinationPath, CancellationToken ct);
    }
}
