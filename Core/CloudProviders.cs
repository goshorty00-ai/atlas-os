using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Core
{
    public interface ICloudProvider
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsConfigured { get; }
        Task<bool> ValidateAsync(CancellationToken ct);
        Task<CloudUnrestrictResult> UnrestrictAsync(string url, CancellationToken ct);
        Task<IReadOnlyList<CloudItem>> GetCloudItemsAsync(CancellationToken ct);
    }

    public sealed class CloudUnrestrictResult
    {
        public bool Success { get; init; }
        public string StreamUrl { get; init; } = "";
        public string? Filename { get; init; }
        public long? Size { get; init; }
        public string? Mime { get; init; }
        public string? Error { get; init; }
    }

    public sealed class CloudItem
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? StreamUrl { get; init; }
        public DateTimeOffset? Added { get; init; }
        public string? Provider { get; init; }
        public string? PosterUrl { get; init; }
    }

    public static class CloudProviderRegistry
    {
        private static readonly object Sync = new();
        private static readonly List<ICloudProvider> ProvidersInternal = new();

        public static IReadOnlyList<ICloudProvider> Providers
        {
            get
            {
                lock (Sync)
                {
                    return ProvidersInternal.ToList();
                }
            }
        }

        public static void Register(ICloudProvider provider)
        {
            if (provider == null) return;
            lock (Sync)
            {
                var existingIndex = ProvidersInternal.FindIndex(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0) ProvidersInternal[existingIndex] = provider;
                else ProvidersInternal.Add(provider);
            }
        }

        public static ICloudProvider? GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (Sync)
            {
                return ProvidersInternal.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public interface ISecretsStore
    {
        string? GetSecret(string key);
        void SetSecret(string key, string plaintext);
        void DeleteSecret(string key);
    }

    public sealed class DpapiFileSecretsStore : ISecretsStore
    {
        public string? GetSecret(string key)
        {
            var v = IntegrationKeyStore.GetDecrypted(key);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        public void SetSecret(string key, string plaintext)
        {
            IntegrationKeyStore.SetProtected(key, plaintext);
        }

        public void DeleteSecret(string key)
        {
            IntegrationKeyStore.Delete(key);
        }
    }
}

