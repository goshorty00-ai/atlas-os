using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaMetadata
{
    public sealed class MusicBrainzClient
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly object RateLock = new();
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        public class MetadataResult
        {
            public string Title { get; set; } = "";
            public string Artist { get; set; } = "";
            public string Album { get; set; } = "";
            public int TrackNumber { get; set; }
            public int DiscNumber { get; set; }
            public string ReleaseId { get; set; } = "";
            public string RecordingId { get; set; } = "";

            // Release-level metadata (from MusicBrainz release lookup)
            public string ReleaseDate { get; set; } = "";
            public string Country { get; set; } = "";
            public string Barcode { get; set; } = "";
            public string Status { get; set; } = "";
            public string Packaging { get; set; } = "";
            public string Label { get; set; } = "";
        }

        private string CleanSearchTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return "";
            
            // Remove "feat.", "ft.", "featuring" and content in brackets/parentheses for better search matches
            var cleaned = term;
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*[\(\[].*?[\)\]]", ""); // remove (...) and [...]
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+(feat\.?|ft\.?|featuring)\s+.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase); // remove feat...
            
            return cleaned.Trim();
        }

        public async Task<List<MetadataResult>> SearchRecordingMetadataAsync(string contact, string artist, string title, CancellationToken ct)
        {
            var results = new List<MetadataResult>();
            if (string.IsNullOrWhiteSpace(title)) return results;

            var userAgent = BuildUserAgent(contact);
            if (string.IsNullOrWhiteSpace(userAgent)) return results;

            try
            {
                await RespectRateLimitAsync(ct).ConfigureAwait(false);

                // Clean terms
                var cleanTitle = CleanSearchTerm(title);
                var cleanArtist = CleanSearchTerm(artist);

                var queryParts = new System.Collections.Generic.List<string>();
                queryParts.Add($"recording:\"{cleanTitle}\"");
                if (!string.IsNullOrWhiteSpace(cleanArtist)) queryParts.Add($"artist:\"{cleanArtist}\"");
                
                // Fallback: if cleaning made it empty (e.g. title was just "(Explicit)"), use original
                if (string.IsNullOrWhiteSpace(cleanTitle))
                {
                    queryParts.Clear();
                    queryParts.Add($"recording:\"{title}\"");
                    if (!string.IsNullOrWhiteSpace(artist)) queryParts.Add($"artist:\"{artist}\"");
                }
                
                var q = Uri.EscapeDataString(string.Join(" AND ", queryParts));
                var searchUrl = $"https://musicbrainz.org/ws/2/recording/?query={q}&fmt=json&limit=5";
                
                using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return results;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("recordings", out var recordings) || recordings.ValueKind != JsonValueKind.Array)
                    return results;

                foreach (var r in recordings.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    
                    var resItem = new MetadataResult();
                    if (r.TryGetProperty("title", out var t)) resItem.Title = t.GetString() ?? "";
                    if (r.TryGetProperty("id", out var rid)) resItem.RecordingId = rid.GetString() ?? "";

                    // Artist
                    if (r.TryGetProperty("artist-credit", out var ac) && ac.ValueKind == JsonValueKind.Array)
                    {
                        var artists = new System.Collections.Generic.List<string>();
                        foreach (var c in ac.EnumerateArray())
                        {
                            if (c.TryGetProperty("name", out var an)) artists.Add(an.GetString() ?? "");
                        }
                        if (artists.Count > 0) resItem.Artist = string.Join(", ", artists);
                    }

                    // Release (Album) - take the first one
                    if (r.TryGetProperty("releases", out var rels) && rels.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var rel in rels.EnumerateArray())
                        {
                            if (rel.TryGetProperty("title", out var rt)) resItem.Album = rt.GetString() ?? "";
                            if (rel.TryGetProperty("id", out var reid)) resItem.ReleaseId = reid.GetString() ?? "";
                            
                            // Try to get track info from media
                            if (rel.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var m in media.EnumerateArray())
                                {
                                     // In recording search, media usually contains track-offset or similar, 
                                     // but the full track structure might not be fully populated like in release lookups.
                                     // However, 'track-count' and 'tracks' might be there.
                                     // For simplicity in search results, we often just get the album title.
                                     // To get track number, we might need to check 'media' -> 'track-offset' + 1 or look for specific track list.
                                     // The recording search result structure for releases usually includes 'media' with 'track' info?
                                     // Actually, in recording search, the releases list usually has a 'media' list, 
                                     // and inside 'media', there are 'tracks' (usually just the one matching).
                                     if (m.TryGetProperty("track-offset", out var to)) resItem.TrackNumber = to.GetInt32() + 1;
                                     if (m.TryGetProperty("position", out var pos)) resItem.DiscNumber = pos.GetInt32(); // 'position' in media is disc number usually
                                }
                            }
                            break; // Just take the first release
                        }
                    }

                    results.Add(resItem);
                }
            }
            catch
            {
                // Ignore errors
            }

            return results;
        }

        public async Task<List<MetadataResult>> SearchReleaseMetadataAsync(string contact, string artist, string album, CancellationToken ct)
        {
            var results = new List<MetadataResult>();
            if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(album)) return results;

            var userAgent = BuildUserAgent(contact);
            if (string.IsNullOrWhiteSpace(userAgent)) return results;

            try
            {
                await RespectRateLimitAsync(ct).ConfigureAwait(false);

                // Clean terms
                var cleanAlbum = CleanSearchTerm(album);
                var cleanArtist = CleanSearchTerm(artist);

                var queryParts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(cleanAlbum)) queryParts.Add($"release:\"{cleanAlbum}\"");
                if (!string.IsNullOrWhiteSpace(cleanArtist)) queryParts.Add($"artist:\"{cleanArtist}\"");
                
                // Fallback
                if (string.IsNullOrWhiteSpace(cleanAlbum))
                {
                    queryParts.Clear();
                    if (!string.IsNullOrWhiteSpace(album)) queryParts.Add($"release:\"{album}\"");
                    if (!string.IsNullOrWhiteSpace(artist)) queryParts.Add($"artist:\"{artist}\"");
                }

                var q = Uri.EscapeDataString(string.Join(" AND ", queryParts));
                var searchUrl = $"https://musicbrainz.org/ws/2/release/?query={q}&fmt=json&limit=1";
                
                using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return results;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
                    return results;

                string? releaseId = null;
                string? releaseTitle = null;
                
                foreach (var r in releases.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    if (r.TryGetProperty("id", out var id))
                    {
                        releaseId = id.GetString();
                        if (r.TryGetProperty("title", out var rt)) releaseTitle = rt.GetString();
                        break; // Take best match
                    }
                }

                if (string.IsNullOrWhiteSpace(releaseId)) return results;

                // Step 2: Get release details with recordings
                await RespectRateLimitAsync(ct).ConfigureAwait(false);

                var detailsUrl = $"https://musicbrainz.org/ws/2/release/{releaseId}?inc=recordings+artist-credits&fmt=json";
                using var detReq = new HttpRequestMessage(HttpMethod.Get, detailsUrl);
                detReq.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                detReq.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var detRes = await Http.SendAsync(detReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!detRes.IsSuccessStatusCode) return results;

                var detJson = await detRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var detDoc = JsonDocument.Parse(detJson);

                var releaseDate = "";
                var country = "";
                var barcode = "";
                var status = "";
                var packaging = "";
                var label = "";
                try
                {
                    var root = detDoc.RootElement;
                    if (root.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.String)
                        releaseDate = dateEl.GetString() ?? "";
                    if (root.TryGetProperty("country", out var countryEl) && countryEl.ValueKind == JsonValueKind.String)
                        country = countryEl.GetString() ?? "";
                    if (root.TryGetProperty("barcode", out var barcodeEl) && barcodeEl.ValueKind == JsonValueKind.String)
                        barcode = barcodeEl.GetString() ?? "";
                    if (root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
                        status = statusEl.GetString() ?? "";
                    if (root.TryGetProperty("packaging", out var packagingEl) && packagingEl.ValueKind == JsonValueKind.String)
                        packaging = packagingEl.GetString() ?? "";

                    if (root.TryGetProperty("label-info", out var liEl) && liEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var li in liEl.EnumerateArray())
                        {
                            if (li.ValueKind != JsonValueKind.Object) continue;
                            if (!li.TryGetProperty("label", out var labelEl) || labelEl.ValueKind != JsonValueKind.Object)
                                continue;
                            if (labelEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            {
                                var name = (nameEl.GetString() ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    label = name;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore
                }
                
                if (detDoc.RootElement.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
                {
                    int discCounter = 1;
                    foreach (var m in media.EnumerateArray())
                    {
                        if (m.ValueKind != JsonValueKind.Object) continue;

                        var discNumber = discCounter;
                        if (m.TryGetProperty("position", out var discPos) && discPos.ValueKind == JsonValueKind.Number)
                        {
                            try { discNumber = discPos.GetInt32(); } catch { }
                        }
                        
                        if (m.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var t in tracks.EnumerateArray())
                            {
                                if (t.ValueKind != JsonValueKind.Object) continue;
                                
                                var track = new MetadataResult
                                {
                                    Album = releaseTitle ?? album,
                                    ReleaseId = releaseId,
                                    DiscNumber = discNumber,
                                    ReleaseDate = releaseDate,
                                    Country = country,
                                    Barcode = barcode,
                                    Status = status,
                                    Packaging = packaging,
                                    Label = label
                                };

                                if (t.TryGetProperty("position", out var pos)) track.TrackNumber = pos.GetInt32();
                                if (t.TryGetProperty("title", out var tt)) track.Title = tt.GetString() ?? "";
                                if (t.TryGetProperty("id", out var rid)) track.RecordingId = rid.GetString() ?? "";

                                // Get artist from track artist-credit if available, else use album artist
                                if (t.TryGetProperty("artist-credit", out var ac) && ac.ValueKind == JsonValueKind.Array)
                                {
                                    var artists = new System.Collections.Generic.List<string>();
                                    foreach (var c in ac.EnumerateArray())
                                    {
                                        if (c.TryGetProperty("name", out var an)) artists.Add(an.GetString() ?? "");
                                    }
                                    if (artists.Count > 0) track.Artist = string.Join(", ", artists);
                                }

                                if (string.IsNullOrWhiteSpace(track.Artist)) track.Artist = artist; // Fallback

                                results.Add(track);
                            }
                        }
                        discCounter++;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return results;
        }

        public async Task<bool> TryDownloadAlbumArtAsync(string contact, string artist, string album, string destinationPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(artist)) return false;
            if (string.IsNullOrWhiteSpace(album)) return false;
            if (string.IsNullOrWhiteSpace(destinationPath)) return false;

            var userAgent = BuildUserAgent(contact);
            if (string.IsNullOrWhiteSpace(userAgent)) return false;

            try
            {
                await RespectRateLimitAsync(ct).ConfigureAwait(false);

                // Clean terms
                var cleanAlbum = CleanSearchTerm(album);
                var cleanArtist = CleanSearchTerm(artist);

                var queryParts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(cleanAlbum)) queryParts.Add($"release:\"{cleanAlbum}\"");
                if (!string.IsNullOrWhiteSpace(cleanArtist)) queryParts.Add($"artist:\"{cleanArtist}\"");

                // Fallback
                if (string.IsNullOrWhiteSpace(cleanAlbum))
                {
                    queryParts.Clear();
                    if (!string.IsNullOrWhiteSpace(album)) queryParts.Add($"release:\"{album}\"");
                    if (!string.IsNullOrWhiteSpace(artist)) queryParts.Add($"artist:\"{artist}\"");
                }

                var q = Uri.EscapeDataString(string.Join(" AND ", queryParts));
                var url = $"https://musicbrainz.org/ws/2/release/?query={q}&fmt=json&limit=1";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return false;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
                    return false;

                string? mbid = null;
                foreach (var r in releases.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    if (r.TryGetProperty("id", out var id))
                    {
                        mbid = id.GetString();
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(mbid)) return false;

                await RespectRateLimitAsync(ct).ConfigureAwait(false);

                var coverUrl = $"https://coverartarchive.org/release/{mbid}/front-500";
                using var coverReq = new HttpRequestMessage(HttpMethod.Get, coverUrl);
                coverReq.Headers.TryAddWithoutValidation("User-Agent", userAgent);

                using var coverRes = await Http.SendAsync(coverReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!coverRes.IsSuccessStatusCode) return false;

                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                await using var input = await coverRes.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
                return File.Exists(destinationPath);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildUserAgent(string contact)
        {
            var trimmed = (contact ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return "";
            return $"AtlasAI/1.0 ({trimmed})";
        }

        private static Task RespectRateLimitAsync(CancellationToken ct)
        {
            TimeSpan delay = TimeSpan.Zero;
            lock (RateLock)
            {
                var now = DateTime.UtcNow;
                var next = _lastRequestUtc.AddSeconds(1);
                if (now < next)
                    delay = next - now;
                _lastRequestUtc = now.Add(delay);
            }

            return delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, ct);
        }
    }
}

