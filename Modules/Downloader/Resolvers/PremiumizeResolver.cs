using System;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public class PremiumizeResolver : ILinkResolver
    {
        public string Name => "Premiumize";
        public bool IsEnabled { get; set; } = false;
        public int Priority => 40;

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

