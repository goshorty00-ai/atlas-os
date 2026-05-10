using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Streaming
{
    public readonly record struct UnrestrictResult(bool Success, string? Url, string? Error);

    public record ResolvePlan(AddonSource Source, string PlayUrlOrPath, bool IsHttp);

    public interface IStreamResolverService
    {
        Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct);

        Task<ResolvePlan> ResolveAsync(
            MediaRequest request,
            AddonSource source,
            Func<string, CancellationToken, Task<UnrestrictResult>>? unrestrict,
            CancellationToken ct);
    }

    public sealed class StreamResolverService : IStreamResolverService
    {
        private readonly IReadOnlyList<IAddonProvider> _addons;

        public StreamResolverService(IEnumerable<IAddonProvider> addons)
        {
            _addons = addons?.ToList() ?? new List<IAddonProvider>();
        }

        public async Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct)
        {
            var results = new List<AddonSource>();

            var tasks = _addons
                .Select(async addon =>
                {
                    try
                    {
                        var list = await addon.GetSourcesAsync(request, ct).ConfigureAwait(false);
                        if (list != null && list.Count > 0)
                            return list;
                    }
                    catch
                    {
                    }
                    return Array.Empty<AddonSource>() as IReadOnlyList<AddonSource>;
                })
                .ToArray();

            try
            {
                var all = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (var l in all)
                    if (l != null && l.Count > 0) results.AddRange(l);
            }
            catch
            {
            }

            var playable = results
                .Where(s => !s.IsInfoOnly && !string.IsNullOrWhiteSpace(s.UrlOrPath))
                .ToList();

            var infoOnly = results
                .Where(s => s.IsInfoOnly || string.IsNullOrWhiteSpace(s.UrlOrPath))
                .ToList();

            static string GetInfoText(AddonSource s)
            {
                try
                {
                    if (s.Metadata == null || s.Metadata.Count == 0) return "";
                    var preferredKeys = new[] { "info", "message", "note", "description", "details" };
                    foreach (var key in preferredKeys)
                    {
                        if (s.Metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                            return v.Trim();
                    }

                    foreach (var kv in s.Metadata)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Value))
                            return kv.Value.Trim();
                    }
                }
                catch
                {
                }
                return "";
            }

            static string InfoKey(AddonSource s)
            {
                var provider = (s.ProviderId ?? "").Trim();
                var name = (s.Name ?? "").Trim();
                var text = GetInfoText(s);
                return (provider + "|" + name + "|" + text).ToLowerInvariant();
            }

            infoOnly = infoOnly
                .GroupBy(InfoKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Rank).First())
                .OrderByDescending(s => s.Rank)
                .ThenBy(s => s.ProviderName ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dedupedPlayable = playable
                .GroupBy(s => $"{(s.ProviderId ?? s.ProviderName ?? "unknown").Trim().ToLowerInvariant()}|{(s.UrlOrPath ?? "").Trim().ToLowerInvariant()}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => x.Rank)
                    .First())
                .OrderByDescending(s => s.Rank)
                .ThenBy(s => s.RequiresDebrid ? 1 : 0)
                .ToList();

            if (infoOnly.Count == 0)
                return dedupedPlayable;

            var combined = new List<AddonSource>(infoOnly.Count + dedupedPlayable.Count);
            combined.AddRange(infoOnly);
            combined.AddRange(dedupedPlayable);
            return combined;
        }

        public async Task<ResolvePlan> ResolveAsync(
            MediaRequest request,
            AddonSource source,
            Func<string, CancellationToken, Task<UnrestrictResult>>? unrestrict,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (source.IsInfoOnly || string.IsNullOrWhiteSpace(source.UrlOrPath))
                throw new InvalidOperationException("Info-only source cannot be resolved for playback");

            if (source.RequiresDebrid)
            {
                if (unrestrict == null)
                    throw new InvalidOperationException("Debrid provider not available");

                var r = await unrestrict(source.UrlOrPath, ct).ConfigureAwait(false);
                if (!r.Success || string.IsNullOrWhiteSpace(r.Url))
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? "Unrestrict failed" : r.Error);

                return new ResolvePlan(source, r.Url!, IsHttp: true);
            }

            if (TryHttpUrl(source.UrlOrPath, out _))
                return new ResolvePlan(source, source.UrlOrPath, IsHttp: true);

            return new ResolvePlan(source, source.UrlOrPath, IsHttp: false);
        }

        private static bool TryHttpUrl(string value, out Uri uri)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out uri!))
            {
                if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            uri = null!;
            return false;
        }
    }
}
