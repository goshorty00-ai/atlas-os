using System;
using System.Collections.Generic;
using System.Linq;
using AtlasAI.Core;

namespace AtlasAI.Streaming
{
    public static class AddonRouter
    {
        private const string RatingsCompatibilityHost = "stremio-addon-ratings.baby-beamup.club";

        public static string NormalizeBaseUrl(string input)
        {
            var s = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "";

            while (s.Length >= 2)
            {
                var first = s[0];
                var last = s[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '`' && last == '`') || (first == '<' && last == '>'))
                {
                    s = s.Substring(1, s.Length - 2).Trim();
                    continue;
                }
                break;
            }

            s = s.TrimEnd('/');

            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
            {
                if (!Uri.TryCreate("http://" + s, UriKind.Absolute, out uri))
                    return "";
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return "";

            var builder = new UriBuilder(uri) { Fragment = "" };
            if ((builder.Path ?? "").EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                builder.Path = builder.Path.Substring(0, builder.Path.Length - "/manifest.json".Length).TrimEnd('/');

            return builder.Uri.ToString().TrimEnd('/');
        }

        public static string WithInjectedKeys(string baseUrl)
        {
            var normalized = NormalizeBaseUrl(baseUrl);
            if (string.IsNullOrWhiteSpace(normalized))
                return "";

            var mdbListKey = "";
            var tmdbKey = "";
            try
            {
                mdbListKey = (IntegrationKeyStore.GetDecrypted("mdblist") ?? "").Trim();
                tmdbKey = (IntegrationKeyStore.GetDecrypted("tmdb") ?? "").Trim();
            }
            catch
            {
                mdbListKey = "";
                tmdbKey = "";
            }

            if (string.IsNullOrWhiteSpace(mdbListKey) && string.IsNullOrWhiteSpace(tmdbKey))
                return normalized;

            if (!normalized.Contains(RatingsCompatibilityHost, StringComparison.OrdinalIgnoreCase))
                return normalized;

            try
            {
                if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                    return normalized;

                var builder = new UriBuilder(uri) { Fragment = "" };

                var path = (builder.Path ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(path) && !string.Equals(path, "/", StringComparison.Ordinal))
                {
                    var trimmed = path.TrimStart('/');
                    var segs = trimmed.Split(new[] { '/' }, 2, StringSplitOptions.None);
                    var config = segs.Length > 0 ? segs[0] : "";
                    var rest = segs.Length > 1 ? "/" + segs[1] : "";
                    var dict = ParseQuery(config);

                    if (!string.IsNullOrWhiteSpace(mdbListKey) &&
                        (!dict.TryGetValue("mdbListApiKey", out var existing) || string.IsNullOrWhiteSpace(existing)))
                    {
                        dict["mdbListApiKey"] = mdbListKey;
                    }

                    if (!string.IsNullOrWhiteSpace(tmdbKey) &&
                        (!dict.TryGetValue("tmdbApiKey", out var existingTmdb) || string.IsNullOrWhiteSpace(existingTmdb)))
                    {
                        dict["tmdbApiKey"] = tmdbKey;
                    }

                    builder.Path = "/" + BuildQuery(dict) + rest;
                    return builder.Uri.ToString().TrimEnd('/');
                }

                var query = (builder.Query ?? "").TrimStart('?').Trim();
                var qdict = ParseQuery(query);

                if (!string.IsNullOrWhiteSpace(mdbListKey) &&
                    (!qdict.TryGetValue("mdbListApiKey", out var existingQ) || string.IsNullOrWhiteSpace(existingQ)))
                {
                    qdict["mdbListApiKey"] = mdbListKey;
                }

                if (!string.IsNullOrWhiteSpace(tmdbKey) &&
                    (!qdict.TryGetValue("tmdbApiKey", out var existingT) || string.IsNullOrWhiteSpace(existingT)))
                {
                    qdict["tmdbApiKey"] = tmdbKey;
                }

                builder.Query = BuildQuery(qdict);
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch
            {
                return normalized;
            }
        }

        public static string ManifestUrl(string baseUrl)
        {
            baseUrl = WithInjectedKeys(baseUrl);
            return BuildEndpointUrl(baseUrl, "/manifest.json");
        }

        public static string StreamUrl(string baseUrl, string type, string id)
        {
            baseUrl = WithInjectedKeys(baseUrl);
            return BuildEndpointUrl(baseUrl, $"/stream/{Uri.EscapeDataString((type ?? "").Trim())}/{Uri.EscapeDataString((id ?? "").Trim())}.json");
        }

        public static string MetaUrl(string baseUrl, string type, string id)
        {
            baseUrl = WithInjectedKeys(baseUrl);
            return BuildEndpointUrl(baseUrl, $"/meta/{Uri.EscapeDataString((type ?? "").Trim())}/{Uri.EscapeDataString((id ?? "").Trim())}.json");
        }

        public static string CatalogUrl(string baseUrl, string type, string catalogId, IReadOnlyDictionary<string, string>? extra)
        {
            baseUrl = WithInjectedKeys(baseUrl);
            var extraPart = "";
            try
            {
                if (extra != null && extra.Count > 0)
                {
                    extraPart = string.Join("&", extra
                        .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                        .Select(kv => $"{Uri.EscapeDataString(kv.Key.Trim())}={Uri.EscapeDataString((kv.Value ?? "").Trim())}"));
                }
            }
            catch
            {
                extraPart = "";
            }

            var basePath = $"/catalog/{Uri.EscapeDataString((type ?? "").Trim())}/{Uri.EscapeDataString((catalogId ?? "").Trim())}";
            if (string.IsNullOrWhiteSpace(extraPart))
                return BuildEndpointUrl(baseUrl, basePath + ".json");
            return BuildEndpointUrl(baseUrl, basePath + "/" + extraPart + ".json");
        }

        private static string BuildEndpointUrl(string baseUrl, string endpointPath)
        {
            try
            {
                baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
                endpointPath = (endpointPath ?? "").Trim();
                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(endpointPath))
                    return "";

                var normalized = NormalizeBaseUrl(baseUrl);
                if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                    return "";

                var builder = new UriBuilder(uri) { Fragment = "" };
                var basePath = (builder.Path ?? "").Trim();
                if (basePath.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                    basePath = basePath.Substring(0, basePath.Length - "/manifest.json".Length);
                basePath = basePath.TrimEnd('/');
                if (string.Equals(basePath, "/", StringComparison.Ordinal))
                    basePath = "";

                if (!endpointPath.StartsWith("/", StringComparison.Ordinal))
                    endpointPath = "/" + endpointPath;

                builder.Path = (basePath + endpointPath).Replace("//", "/");
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch
            {
                return "";
            }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var q = (query ?? "").Trim().TrimStart('?');
            if (string.IsNullOrWhiteSpace(q))
                return dict;

            var parts = q.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var idx = p.IndexOf('=');
                var k = idx < 0 ? p : p.Substring(0, idx);
                var v = idx < 0 ? "" : Uri.UnescapeDataString(p.Substring(idx + 1));
                k = Uri.UnescapeDataString((k ?? "").Trim());
                if (string.IsNullOrWhiteSpace(k))
                    continue;
                dict[k] = v ?? "";
            }
            return dict;
        }

        private static string BuildQuery(Dictionary<string, string> dict)
        {
            if (dict == null || dict.Count == 0)
                return "";
            return string.Join("&", dict.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString((kv.Value ?? "").Trim())}"));
        }
    }
}

