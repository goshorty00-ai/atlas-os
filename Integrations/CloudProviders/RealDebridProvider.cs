using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Integrations.CloudProviders
{
    public sealed class RealDebridProvider : ICloudProvider
    {
        private static readonly Uri BaseUri = new("https://api.real-debrid.com/rest/1.0/");
        private static readonly HttpClient Http = new() { BaseAddress = BaseUri, Timeout = TimeSpan.FromSeconds(30) };

        private readonly ISecretsStore _secrets;

        public RealDebridProvider(ISecretsStore secrets)
        {
            _secrets = secrets;
        }

        public string Id => "realdebrid";
        public string DisplayName => "Real-Debrid";

        public bool IsConfigured
        {
            get
            {
                var t = _secrets.GetSecret("realdebrid");
                return !string.IsNullOrWhiteSpace(t);
            }
        }

        public async Task<bool> ValidateAsync(CancellationToken ct)
        {
            var token = _secrets.GetSecret("realdebrid");
            if (string.IsNullOrWhiteSpace(token)) return false;

            using var resp = await SendAuthedAsync(() =>
            {
                var r = new HttpRequestMessage(HttpMethod.Get, "user");
                r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return r;
            }, () =>
            {
                return new HttpRequestMessage(HttpMethod.Get, $"user?auth_token={Uri.EscapeDataString(token)}");
            }, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch
            {
                return false;
            }
        }

        public async Task<CloudUnrestrictResult> UnrestrictAsync(string url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
                return new CloudUnrestrictResult { Success = false, Error = "URL is required" };

            var token = _secrets.GetSecret("realdebrid");
            if (string.IsNullOrWhiteSpace(token))
                return new CloudUnrestrictResult { Success = false, Error = "Provider is not configured" };

            if (IsMagnet(url))
            {
                var added = await TryAddMagnetAsync(token, url, ct).ConfigureAwait(false);
                if (!added.Success || string.IsNullOrWhiteSpace(added.Id))
                    return new CloudUnrestrictResult { Success = false, Error = string.IsNullOrWhiteSpace(added.Error) ? "Magnet add failed" : added.Error };

                var selected = await TrySelectAllFilesAsync(token, added.Id!, ct).ConfigureAwait(false);
                if (!selected.Success)
                    return new CloudUnrestrictResult { Success = false, Error = string.IsNullOrWhiteSpace(selected.Error) ? "Torrent file selection failed" : selected.Error };

                var links = await TryGetTorrentLinksAsync(token, added.Id!, ct).ConfigureAwait(false);
                if (links == null || links.Count == 0)
                    return new CloudUnrestrictResult { Success = false, Error = "No torrent links available" };

                var direct = await UnrestrictLinkAsync(token, links[0], ct).ConfigureAwait(false);
                if (!direct.Success)
                    return direct;

                return direct;
            }

            using var resp = await SendAuthedAsync(() =>
            {
                var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("link", url) });
                var r = new HttpRequestMessage(HttpMethod.Post, "unrestrict/link") { Content = content };
                r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return r;
            }, () =>
            {
                var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("link", url) });
                return new HttpRequestMessage(HttpMethod.Post, $"unrestrict/link?auth_token={Uri.EscapeDataString(token)}") { Content = content };
            }, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var apiMsg = TryExtractApiError(body);
                var msg = resp.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Invalid token",
                    HttpStatusCode.Forbidden => "Permission denied (account locked or token blocked)",
                    HttpStatusCode.TooManyRequests => "Rate limited",
                    _ => "Request failed"
                };

                if (!string.IsNullOrWhiteSpace(apiMsg))
                    msg = $"{msg}: {apiMsg}";

                return new CloudUnrestrictResult { Success = false, Error = msg };
            }

            if (string.IsNullOrWhiteSpace(body))
                return new CloudUnrestrictResult { Success = false, Error = "Empty response" };

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return new CloudUnrestrictResult { Success = false, Error = "Invalid response" };

                if (!doc.RootElement.TryGetProperty("download", out var downloadEl))
                    return new CloudUnrestrictResult { Success = false, Error = "No stream URL returned" };

                var streamUrl = downloadEl.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(streamUrl))
                    return new CloudUnrestrictResult { Success = false, Error = "No stream URL returned" };

                string? filename = null;
                if (doc.RootElement.TryGetProperty("filename", out var fnEl))
                    filename = fnEl.GetString();

                long? size = null;
                if (doc.RootElement.TryGetProperty("filesize", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number && sizeEl.TryGetInt64(out var s))
                    size = s;

                string? mime = null;
                if (doc.RootElement.TryGetProperty("mimeType", out var mimeEl))
                    mime = mimeEl.GetString();

                return new CloudUnrestrictResult
                {
                    Success = true,
                    StreamUrl = streamUrl,
                    Filename = string.IsNullOrWhiteSpace(filename) ? null : filename,
                    Size = size,
                    Mime = string.IsNullOrWhiteSpace(mime) ? null : mime
                };
            }
            catch
            {
                return new CloudUnrestrictResult { Success = false, Error = "Failed to parse response" };
            }
        }

        public Task<IReadOnlyList<CloudItem>> GetCloudItemsAsync(CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<CloudItem>>(Array.Empty<CloudItem>());
        }

        private static bool IsMagnet(string url)
        {
            var u = (url ?? "").Trim();
            return u.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<CloudUnrestrictResult> UnrestrictLinkAsync(string token, string link, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(link))
                return new CloudUnrestrictResult { Success = false, Error = "No link to unrestrict" };

            using var resp = await SendAuthedAsync(() =>
            {
                var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("link", link) });
                var r = new HttpRequestMessage(HttpMethod.Post, "unrestrict/link") { Content = content };
                r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return r;
            }, () =>
            {
                var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("link", link) });
                return new HttpRequestMessage(HttpMethod.Post, $"unrestrict/link?auth_token={Uri.EscapeDataString(token)}") { Content = content };
            }, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var apiMsg = TryExtractApiError(body);
                var msg = resp.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Invalid token",
                    HttpStatusCode.Forbidden => "Permission denied (account locked or token blocked)",
                    HttpStatusCode.TooManyRequests => "Rate limited",
                    _ => "Request failed"
                };

                if (!string.IsNullOrWhiteSpace(apiMsg))
                    msg = $"{msg}: {apiMsg}";

                return new CloudUnrestrictResult { Success = false, Error = msg };
            }

            if (string.IsNullOrWhiteSpace(body))
                return new CloudUnrestrictResult { Success = false, Error = "Empty response" };

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return new CloudUnrestrictResult { Success = false, Error = "Invalid response" };

                if (!doc.RootElement.TryGetProperty("download", out var downloadEl))
                    return new CloudUnrestrictResult { Success = false, Error = "No stream URL returned" };

                var streamUrl = downloadEl.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(streamUrl))
                    return new CloudUnrestrictResult { Success = false, Error = "No stream URL returned" };

                string? filename = null;
                if (doc.RootElement.TryGetProperty("filename", out var fnEl))
                    filename = fnEl.GetString();

                long? size = null;
                if (doc.RootElement.TryGetProperty("filesize", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number && sizeEl.TryGetInt64(out var s))
                    size = s;

                string? mime = null;
                if (doc.RootElement.TryGetProperty("mimeType", out var mimeEl))
                    mime = mimeEl.GetString();

                return new CloudUnrestrictResult
                {
                    Success = true,
                    StreamUrl = streamUrl,
                    Filename = string.IsNullOrWhiteSpace(filename) ? null : filename,
                    Size = size,
                    Mime = string.IsNullOrWhiteSpace(mime) ? null : mime
                };
            }
            catch
            {
                return new CloudUnrestrictResult { Success = false, Error = "Failed to parse response" };
            }
        }

        private async Task<(bool Success, string? Id, string? Error)> TryAddMagnetAsync(string token, string magnet, CancellationToken ct)
        {
            try
            {
                using var resp = await SendAuthedAsync(() =>
                {
                    var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("magnet", magnet) });
                    var r = new HttpRequestMessage(HttpMethod.Post, "torrents/addMagnet") { Content = content };
                    r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    return r;
                }, () =>
                {
                    var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("magnet", magnet) });
                    return new HttpRequestMessage(HttpMethod.Post, $"torrents/addMagnet?auth_token={Uri.EscapeDataString(token)}") { Content = content };
                }, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
                    return (false, null, "Magnet add failed");

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return (false, null, "Invalid response");

                if (!doc.RootElement.TryGetProperty("id", out var idEl))
                    return (false, null, "No torrent id returned");

                var id = (idEl.GetString() ?? "").Trim();
                return string.IsNullOrWhiteSpace(id) ? (false, null, "No torrent id returned") : (true, id, null);
            }
            catch
            {
                return (false, null, "Magnet add failed");
            }
        }

        private async Task<(bool Success, string? Error)> TrySelectAllFilesAsync(string token, string torrentId, CancellationToken ct)
        {
            try
            {
                using var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("files", "all")
                });

                using var req = new HttpRequestMessage(HttpMethod.Post, $"torrents/selectFiles/{torrentId}") { Content = content };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return (false, "File selection failed");

                return (true, null);
            }
            catch
            {
                return (false, "File selection failed");
            }
        }

        private async Task<List<string>?> TryGetTorrentLinksAsync(string token, string torrentId, CancellationToken ct)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"torrents/info/{torrentId}");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    using var resp = await SendWithRetryAsync(req, ct).ConfigureAwait(false);
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(900), ct).ConfigureAwait(false);
                        continue;
                    }

                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(900), ct).ConfigureAwait(false);
                        continue;
                    }

                    if (doc.RootElement.TryGetProperty("links", out var linksEl) && linksEl.ValueKind == JsonValueKind.Array)
                    {
                        var links = new List<string>();
                        foreach (var l in linksEl.EnumerateArray())
                        {
                            if (l.ValueKind != JsonValueKind.String) continue;
                            var s = (l.GetString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(s))
                                links.Add(s);
                        }

                        if (links.Count > 0)
                            return links;
                    }
                }
                catch
                {
                }

                await Task.Delay(TimeSpan.FromMilliseconds(900), ct).ConfigureAwait(false);
            }

            return null;
        }

        private static async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken ct)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                HttpRequestMessage cloned;
                try
                {
                    cloned = await CloneAsync(request, ct).ConfigureAwait(false);
                }
                catch
                {
                    cloned = request;
                }

                HttpResponseMessage resp;
                try
                {
                    resp = await Http.SendAsync(cloned, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                }
                catch when (attempt < 2 && !ct.IsCancellationRequested)
                {
                    await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                if ((int)resp.StatusCode == 429 || resp.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt < 2)
                    {
                        await Task.Delay(Backoff(attempt, resp), ct).ConfigureAwait(false);
                        resp.Dispose();
                        continue;
                    }
                }

                return resp;
            }

            return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }

        private static async Task<HttpResponseMessage> SendAuthedAsync(Func<HttpRequestMessage> bearerRequest, Func<HttpRequestMessage> tokenRequest, CancellationToken ct)
        {
            HttpResponseMessage? resp = null;
            try
            {
                using var r1 = bearerRequest();
                resp = await SendWithRetryAsync(r1, ct).ConfigureAwait(false);
                if (resp.StatusCode != HttpStatusCode.Unauthorized && resp.StatusCode != HttpStatusCode.Forbidden)
                    return resp;
            }
            catch
            {
                resp?.Dispose();
            }

            try
            {
                resp?.Dispose();
                using var r2 = tokenRequest();
                return await SendWithRetryAsync(r2, ct).ConfigureAwait(false);
            }
            catch
            {
                resp?.Dispose();
                throw;
            }
        }

        private static string? TryExtractApiError(string? body)
        {
            var b = (body ?? "").Trim();
            if (string.IsNullOrWhiteSpace(b)) return null;
            try
            {
                using var doc = JsonDocument.Parse(b);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                {
                    var s = (e.GetString() ?? "").Trim();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                }
                if (doc.RootElement.TryGetProperty("error_code", out var code) && code.ValueKind == JsonValueKind.Number &&
                    doc.RootElement.TryGetProperty("error_message", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    var s = (msg.GetString() ?? "").Trim();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                }
            }
            catch
            {
            }
            return null;
        }

        private static TimeSpan Backoff(int attempt, HttpResponseMessage? response = null)
        {
            if (response != null && response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                    return response.Headers.RetryAfter.Delta.Value;

                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    var delta = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                    if (delta > TimeSpan.Zero && delta < TimeSpan.FromMinutes(2)) return delta;
                }
            }

            var ms = attempt switch
            {
                0 => 300,
                1 => 900,
                _ => 2000
            };
            return TimeSpan.FromMilliseconds(ms);
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);

            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (request.Content != null)
            {
                var ms = new System.IO.MemoryStream();
                await request.Content.CopyToAsync(ms, ct).ConfigureAwait(false);
                ms.Position = 0;
                var streamContent = new StreamContent(ms);
                foreach (var header in request.Content.Headers)
                    streamContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                clone.Content = streamContent;
            }

            return clone;
        }
    }
}
