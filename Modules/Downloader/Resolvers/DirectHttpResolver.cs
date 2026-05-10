using System;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public class DirectHttpResolver : ILinkResolver
    {
        public string Name => "DirectHttp";
        public bool IsEnabled { get; set; } = true;
        public int Priority => 0;

        public bool CanHandle(Uri input)
        {
            return input.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                   input.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }

        public Task<ResolvedLink?> ResolveAsync(Uri input, CancellationToken ct)
        {
            return Task.FromResult<ResolvedLink?>(new ResolvedLink
            {
                DirectUrl = input,
                ResolverName = Name
            });
        }
    }
}

