using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.MediaMetadata
{
    public sealed class ReccoBeatsClient
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        // Note: If the API requires a key, add it here or in the headers
        // private const string ApiKey = "YOUR_API_KEY";

        public class ReccoSearchResult
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Artist { get; set; } = "";
            public string CoverUrl { get; set; } = "";
            public string ReleaseDate { get; set; } = "";
        }

        public class ReccoAudioFeatures
        {
            public string Id { get; set; } = "";
            public float Danceability { get; set; }
            public float Energy { get; set; }
            public int Key { get; set; }
            public float Loudness { get; set; }
            public int Mode { get; set; }
            public float Speechiness { get; set; }
            public float Acousticness { get; set; }
            public float Instrumentalness { get; set; }
            public float Liveness { get; set; }
            public float Valence { get; set; }
            public float Tempo { get; set; }
            public int DurationMs { get; set; }
            public int TimeSignature { get; set; }
        }

        public async Task<List<ReccoSearchResult>> SearchAlbumAsync(string query, CancellationToken ct)
        {
            var results = new List<ReccoSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            try
            {
                // Corrected endpoint: /v1/album/search?searchText=...
                var q = Uri.EscapeDataString(query);
                var url = $"https://api.reccobeats.com/v1/album/search?searchText={q}&size=5";
                
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return results;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                
                // Response structure: { "content": [ ... ], "page": 0, "size": 25, ... }
                if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        var result = new ReccoSearchResult();
                        if (item.TryGetProperty("id", out var id)) result.Id = id.GetString() ?? "";
                        if (item.TryGetProperty("name", out var name)) result.Title = name.GetString() ?? "";
                        
                        if (item.TryGetProperty("image", out var img)) result.CoverUrl = img.GetString() ?? "";
                        // Fallback for image array if schema varies
                        else if (item.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var i in imgs.EnumerateArray())
                            {
                                if (i.TryGetProperty("url", out var u))
                                {
                                    result.CoverUrl = u.GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                        {
                            var artistNames = new List<string>();
                            foreach (var a in artists.EnumerateArray())
                            {
                                if (a.TryGetProperty("name", out var an)) artistNames.Add(an.GetString() ?? "");
                            }
                            result.Artist = string.Join(", ", artistNames);
                        }

                        results.Add(result);
                    }
                }

                return results;
            }
            catch
            {
                return results;
            }
        }

        public async Task<ReccoSearchResult?> GetAlbumDetailAsync(string id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            try
            {
                var url = $"https://api.reccobeats.com/v1/album/{id}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("data", out var d)) root = d;

                var result = new ReccoSearchResult();
                if (root.TryGetProperty("id", out var i)) result.Id = i.GetString() ?? "";
                if (root.TryGetProperty("name", out var n)) result.Title = n.GetString() ?? "";
                
                if (root.TryGetProperty("image", out var img)) result.CoverUrl = img.GetString() ?? "";
                else if (root.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in imgs.EnumerateArray())
                    {
                        if (item.TryGetProperty("url", out var u))
                        {
                            result.CoverUrl = u.GetString() ?? "";
                            break;
                        }
                    }
                }

                if (root.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                {
                    var artistNames = new List<string>();
                    foreach (var a in artists.EnumerateArray())
                    {
                        if (a.TryGetProperty("name", out var an)) artistNames.Add(an.GetString() ?? "");
                    }
                    result.Artist = string.Join(", ", artistNames);
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<ReccoSearchResult>> SearchTrackAsync(string query, CancellationToken ct)
        {
            var results = new List<ReccoSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            try
            {
                var q = Uri.EscapeDataString(query);
                // Assuming standard search pattern for tracks
                var url = $"https://api.reccobeats.com/v1/track/search?query={q}&limit=5";
                
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return results;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        var result = new ReccoSearchResult();
                        if (item.TryGetProperty("id", out var id)) result.Id = id.GetString() ?? "";
                        if (item.TryGetProperty("title", out var title)) result.Title = title.GetString() ?? ""; // 'title' or 'name'
                        else if (item.TryGetProperty("name", out var name)) result.Title = name.GetString() ?? "";

                        if (item.TryGetProperty("image", out var img)) result.CoverUrl = img.GetString() ?? "";
                        else if (item.TryGetProperty("album", out var album))
                        {
                            if (album.TryGetProperty("image", out var albImg)) result.CoverUrl = albImg.GetString() ?? "";
                            if (album.TryGetProperty("title", out var albTitle)) result.ReleaseDate = albTitle.GetString() ?? ""; // Storing album name in ReleaseDate for now or add Album property
                        }

                        if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                        {
                            var artistNames = new List<string>();
                            foreach (var a in artists.EnumerateArray())
                            {
                                if (a.TryGetProperty("name", out var an)) artistNames.Add(an.GetString() ?? "");
                            }
                            result.Artist = string.Join(", ", artistNames);
                        }

                        results.Add(result);
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return results;
        }

        public async Task<ReccoSearchResult?> GetTrackAlbumAsync(string trackId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(trackId)) return null;

            try
            {
                var url = $"https://api.reccobeats.com/v1/track/{trackId}/album";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var d)) root = d;

                var result = new ReccoSearchResult();
                if (root.TryGetProperty("id", out var i)) result.Id = i.GetString() ?? "";
                if (root.TryGetProperty("name", out var n)) result.Title = n.GetString() ?? "";
                if (root.TryGetProperty("image", out var img)) result.CoverUrl = img.GetString() ?? "";
                
                if (root.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                {
                    var artistNames = new List<string>();
                    foreach (var a in artists.EnumerateArray())
                    {
                        if (a.TryGetProperty("name", out var an)) artistNames.Add(an.GetString() ?? "");
                    }
                    result.Artist = string.Join(", ", artistNames);
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> TryDownloadCoverAsync(string artist, string album, string destinationPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(destinationPath)) return false;

            try
            {
                // Search for the album to get the cover URL
                var results = await SearchAlbumAsync($"{artist} {album}", ct);
                if (results.Count == 0) return false;

                var coverUrl = results[0].CoverUrl;
                if (string.IsNullOrWhiteSpace(coverUrl)) return false;

                return await TryDownloadCoverAsync(coverUrl, destinationPath, ct);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> TryDownloadCoverAsync(string coverUrl, string destinationPath, CancellationToken ct)
        {
             if (string.IsNullOrWhiteSpace(coverUrl)) return false;

             try
             {
                 using var req = new HttpRequestMessage(HttpMethod.Get, coverUrl);
                 using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                 if (!res.IsSuccessStatusCode) return false;

                 var dir = Path.GetDirectoryName(destinationPath);
                 if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

                 await using var input = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                 await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                 await input.CopyToAsync(output, ct).ConfigureAwait(false);
                 
                 return File.Exists(destinationPath);
             }
             catch
             {
                 return false;
             }
        }

        public async Task<ReccoSearchResult?> GetTrackDetailsAsync(string trackId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(trackId)) return null;

            try
            {
                var url = $"https://api.reccobeats.com/v1/track/{trackId}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var d)) root = d;

                var result = new ReccoSearchResult();
                if (root.TryGetProperty("id", out var i)) result.Id = i.GetString() ?? "";
                if (root.TryGetProperty("name", out var n)) result.Title = n.GetString() ?? "";
                else if (root.TryGetProperty("title", out var t)) result.Title = t.GetString() ?? "";
                
                if (root.TryGetProperty("image", out var img)) result.CoverUrl = img.GetString() ?? "";
                
                if (root.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                {
                    var artistNames = new List<string>();
                    foreach (var a in artists.EnumerateArray())
                    {
                        if (a.TryGetProperty("name", out var an)) artistNames.Add(an.GetString() ?? "");
                    }
                    result.Artist = string.Join(", ", artistNames);
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<ReccoSearchResult>> GetTrackRecommendationsAsync(CancellationToken ct)
        {
            var results = new List<ReccoSearchResult>();
            try
            {
                var url = "https://api.reccobeats.com/v1/track/recommendation";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return results;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        var result = new ReccoSearchResult();
                        if (item.TryGetProperty("id", out var id)) result.Id = id.GetString() ?? "";
                        if (item.TryGetProperty("title", out var title)) result.Title = title.GetString() ?? "";
                        else if (item.TryGetProperty("name", out var name)) result.Title = name.GetString() ?? "";

                        if (item.TryGetProperty("image", out var img)) result.CoverUrl = img.GetString() ?? "";
                        
                        if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                        {
                            var artistNames = new List<string>();
                            foreach (var a in artists.EnumerateArray())
                            {
                                if (a.TryGetProperty("name", out var an)) artistNames.Add(an.GetString() ?? "");
                            }
                            result.Artist = string.Join(", ", artistNames);
                        }

                        results.Add(result);
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return results;
        }

        public async Task<ReccoAudioFeatures?> GetTrackAudioFeaturesAsync(string trackId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(trackId)) return null;

            try
            {
                var url = $"https://api.reccobeats.com/v1/track/{trackId}/audio-features";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var d)) root = d;

                var result = new ReccoAudioFeatures();
                if (root.TryGetProperty("id", out var i)) result.Id = i.GetString() ?? "";
                if (root.TryGetProperty("danceability", out var da)) result.Danceability = (float)da.GetDouble();
                if (root.TryGetProperty("energy", out var en)) result.Energy = (float)en.GetDouble();
                if (root.TryGetProperty("key", out var k)) result.Key = k.GetInt32();
                if (root.TryGetProperty("loudness", out var l)) result.Loudness = (float)l.GetDouble();
                if (root.TryGetProperty("mode", out var m)) result.Mode = m.GetInt32();
                if (root.TryGetProperty("speechiness", out var s)) result.Speechiness = (float)s.GetDouble();
                if (root.TryGetProperty("acousticness", out var a)) result.Acousticness = (float)a.GetDouble();
                if (root.TryGetProperty("instrumentalness", out var inst)) result.Instrumentalness = (float)inst.GetDouble();
                if (root.TryGetProperty("liveness", out var li)) result.Liveness = (float)li.GetDouble();
                if (root.TryGetProperty("valence", out var v)) result.Valence = (float)v.GetDouble();
                if (root.TryGetProperty("tempo", out var t)) result.Tempo = (float)t.GetDouble();
                if (root.TryGetProperty("duration_ms", out var dur)) result.DurationMs = dur.GetInt32();
                if (root.TryGetProperty("time_signature", out var ts)) result.TimeSignature = ts.GetInt32();

                return result;
            }
            catch
            {
                return null;
            }
        }

    }
}
