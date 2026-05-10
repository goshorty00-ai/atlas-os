using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Streaming
{
    public interface IAddonProvider
    {
        string Id { get; }
        string DisplayName { get; }

        Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct);
    }

    public record MediaRequest(string Title, string Category, string? PrimaryPathOrUrl);

    public record AddonSource(
        string SourceId,
        string Name,
        string UrlOrPath,
        string ProviderId,
        string ProviderName,
        bool RequiresDebrid,
        int Rank,
        string? Quality,
        bool IsInfoOnly = false,
        IReadOnlyDictionary<string, string>? Metadata = null)
    {
        public bool IsDebrid => RequiresDebrid;
        public string SizeText { get; init; } = "";
        public string SeedersText { get; init; } = "";
    }
}
