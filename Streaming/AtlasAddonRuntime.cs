using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Streaming
{
    public enum AtlasAddonKind
    {
        Native = 0,
        LegacyCompatible = 1
    }

    public sealed record AtlasAddonDescriptor(
        string Id,
        string DisplayName,
        AtlasAddonKind Kind,
        IReadOnlyList<string>? SupportedCategories = null,
        IReadOnlyDictionary<string, string>? Metadata = null);

    public interface IAtlasAddon
    {
        AtlasAddonDescriptor Descriptor { get; }

        Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct);
    }

    public sealed class ProviderAtlasAddonAdapter : IAtlasAddon
    {
        private readonly IAddonProvider _provider;

        public ProviderAtlasAddonAdapter(
            IAddonProvider provider,
            AtlasAddonKind kind,
            IReadOnlyList<string>? supportedCategories = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Descriptor = new AtlasAddonDescriptor(
                _provider.Id,
                _provider.DisplayName,
                kind,
                supportedCategories,
                metadata);
        }

        public AtlasAddonDescriptor Descriptor { get; }

        public Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct)
            => _provider.GetSourcesAsync(request, ct);
    }

    public sealed class AtlasAddonProviderAdapter : IAddonProvider
    {
        private readonly IAtlasAddon _addon;

        public AtlasAddonProviderAdapter(IAtlasAddon addon)
        {
            _addon = addon ?? throw new ArgumentNullException(nameof(addon));
        }

        public string Id => _addon.Descriptor.Id;

        public string DisplayName => _addon.Descriptor.DisplayName;

        public Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct)
            => _addon.GetSourcesAsync(request, ct);
    }

    public static class AtlasAddonRegistry
    {
        public static IReadOnlyList<IAtlasAddon> CreateOwnAddons()
        {
            return new IAtlasAddon[]
            {
                new ProviderAtlasAddonAdapter(
                    new LocalLibraryAddonProvider(),
                    AtlasAddonKind.Native,
                    new[] { "movie", "series", "music", "anime", "documentary", "file" },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["origin"] = "atlas",
                        ["transport"] = "local-library"
                    }),
                new ProviderAtlasAddonAdapter(
                    new CloudLinkAddonProvider(),
                    AtlasAddonKind.Native,
                    new[] { "cloud" },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["origin"] = "atlas",
                        ["transport"] = "cloud-link"
                    })
            };
        }

        public static IReadOnlyList<IAtlasAddon> CreateLegacyCompatibleAddons()
        {
            return new IAtlasAddon[]
            {
                new ProviderAtlasAddonAdapter(
                    new AddonServersAddonProvider(),
                    AtlasAddonKind.LegacyCompatible,
                    new[] { "movie", "series", "music", "anime", "channel" },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["origin"] = "compatibility",
                        ["protocol"] = "manifest-catalog-meta-stream"
                    })
            };
        }

        public static IReadOnlyList<IAtlasAddon> CreateDefaultAddons()
        {
            return CreateOwnAddons()
                .Concat(CreateLegacyCompatibleAddons())
                .ToArray();
        }
    }

    public static class AtlasStreamResolverFactory
    {
        public static IStreamResolverService CreateOwnResolver()
            => CreateResolver(AtlasAddonRegistry.CreateOwnAddons());

        public static IStreamResolverService CreateServersResolver()
            => CreateResolver(AtlasAddonRegistry.CreateLegacyCompatibleAddons());

        public static IStreamResolverService CreateDefaultResolver()
            => CreateResolver(AtlasAddonRegistry.CreateDefaultAddons());

        private static IStreamResolverService CreateResolver(IEnumerable<IAtlasAddon> addons)
        {
            return new StreamResolverService(addons.Select(static addon => new AtlasAddonProviderAdapter(addon)));
        }
    }
}