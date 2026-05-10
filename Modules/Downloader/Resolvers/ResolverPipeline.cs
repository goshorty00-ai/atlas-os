using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public class ResolverPipeline
    {
        private readonly List<ILinkResolver> _resolvers;

        public ResolverPipeline(IEnumerable<ILinkResolver> resolvers)
        {
            _resolvers = resolvers?.OrderByDescending(r => r.Priority).ToList() ?? new List<ILinkResolver>();
        }

        public async Task<ResolvedLink> ResolveAsync(Uri input, ResolverMode mode, string? forcedResolverName, CancellationToken ct)
        {
            if (mode == ResolverMode.ForcedProvider && !string.IsNullOrWhiteSpace(forcedResolverName))
            {
                var forced = _resolvers.FirstOrDefault(r => r.Name.Equals(forcedResolverName, StringComparison.OrdinalIgnoreCase));
                if (forced != null && forced.IsEnabled && forced.CanHandle(input))
                {
                    var resolved = await forced.ResolveAsync(input, ct);
                    if (resolved != null) return resolved;
                }
            }
            else
            {
                foreach (var r in _resolvers)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!r.IsEnabled) continue;
                    if (!r.CanHandle(input)) continue;
                    var resolved = await r.ResolveAsync(input, ct);
                    if (resolved != null) return resolved;
                }
            }

            return new ResolvedLink
            {
                DirectUrl = input,
                ResolverName = "DirectHttp"
            };
        }
    }
}

