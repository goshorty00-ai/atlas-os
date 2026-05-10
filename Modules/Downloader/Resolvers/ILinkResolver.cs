using System;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public interface ILinkResolver
    {
        string Name { get; }
        bool IsEnabled { get; set; }
        int Priority { get; }
        bool CanHandle(Uri input);
        Task<ResolvedLink?> ResolveAsync(Uri input, CancellationToken ct);
    }
}

