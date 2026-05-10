using System;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public class AllDebridResolver : ILinkResolver
    {
        public string Name => "AllDebrid";
        public bool IsEnabled { get; set; } = false;
        public int Priority => 50;

        public bool CanHandle(Uri input)
        {
            return input.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                   input.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }

        public Task<ResolvedLink?> ResolveAsync(Uri input, CancellationToken ct)
        {
            return Task.FromResult<ResolvedLink?>(null);
        }
    }
}

