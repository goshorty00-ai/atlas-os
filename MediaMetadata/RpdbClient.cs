using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Services;

namespace AtlasAI.MediaMetadata
{
    public sealed class RpdbClient
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public async Task<RpdbMetadata?> LookupAsync(string apiKey, string imdbId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(imdbId))
                return null;

            var url = "";
            try
            {
                // RPDB API lookup
                // Format: https://api.rpdb.info/v1/lookup?key={key}&id={imdbId}
                url = $"https://api.rpdb.info/v1/lookup?key={Uri.EscapeDataString(apiKey.Trim())}&id={Uri.EscapeDataString(imdbId)}";
                
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("RPDB", url, (int)resp.StatusCode, "lookup"); } catch { }
                    return null;
                }

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var result = new RpdbMetadata();
                
                if (root.TryGetProperty("poster", out var posterEl) && posterEl.ValueKind == JsonValueKind.String)
                    result.PosterUrl = posterEl.GetString();
                else if (root.TryGetProperty("posterUrl", out var posterEl2) && posterEl2.ValueKind == JsonValueKind.String)
                    result.PosterUrl = posterEl2.GetString();

                if (root.TryGetProperty("backdrop", out var backdropEl) && backdropEl.ValueKind == JsonValueKind.String)
                    result.BackdropUrl = backdropEl.GetString();
                else if (root.TryGetProperty("background", out var backgroundEl) && backgroundEl.ValueKind == JsonValueKind.String)
                    result.BackdropUrl = backgroundEl.GetString();

                if (root.TryGetProperty("logo", out var logoEl) && logoEl.ValueKind == JsonValueKind.String)
                    result.LogoUrl = logoEl.GetString();

                if (root.TryGetProperty("rating", out var ratingEl))
                {
                    if (ratingEl.ValueKind == JsonValueKind.Number && ratingEl.TryGetDouble(out var r))
                        result.Rating = r;
                    else if (ratingEl.ValueKind == JsonValueKind.String && double.TryParse(ratingEl.GetString(), out var r2))
                        result.Rating = r2;
                }

                // Overview/plot/synopsis
                if (string.IsNullOrWhiteSpace(result.Overview))
                {
                    if (root.TryGetProperty("overview", out var ovEl) && ovEl.ValueKind == JsonValueKind.String)
                        result.Overview = ovEl.GetString();
                    else if (root.TryGetProperty("description", out var deEl) && deEl.ValueKind == JsonValueKind.String)
                        result.Overview = deEl.GetString();
                    else if (root.TryGetProperty("plot", out var plEl) && plEl.ValueKind == JsonValueKind.String)
                        result.Overview = plEl.GetString();
                    else if (root.TryGetProperty("synopsis", out var syEl) && syEl.ValueKind == JsonValueKind.String)
                        result.Overview = syEl.GetString();
                    else if (root.TryGetProperty("summary", out var suEl) && suEl.ValueKind == JsonValueKind.String)
                        result.Overview = suEl.GetString();
                }

                // RPDB also provides multiple ratings (IMDB, RT, Metacritic, etc.)
                if (root.TryGetProperty("ratings", out var ratingsEl) && ratingsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in ratingsEl.EnumerateArray())
                    {
                        if (r.TryGetProperty("source", out var sEl) && sEl.ValueKind == JsonValueKind.String &&
                            r.TryGetProperty("value", out var vEl))
                        {
                            var source = sEl.GetString() ?? "";
                            var val = "";
                            if (vEl.ValueKind == JsonValueKind.String) val = vEl.GetString() ?? "";
                            else if (vEl.ValueKind == JsonValueKind.Number) val = vEl.GetDouble().ToString("F1");
                            
                            if (!string.IsNullOrEmpty(source))
                                result.AllRatings[source] = val;
                        }
                    }
                }

                return result;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("RPDB", url, 0, "lookup"); } catch { }
                return null;
            }
        }
    }

    public class RpdbMetadata
    {
        public string? PosterUrl { get; set; }
        public string? BackdropUrl { get; set; }
        public string? LogoUrl { get; set; }
        public string? Overview { get; set; }
        public double? Rating { get; set; }
        public Dictionary<string, string> AllRatings { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
