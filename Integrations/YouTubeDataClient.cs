using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AtlasAI.Integrations
{
    public sealed class YouTubeDataClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public sealed class VideoResult
        {
            public string VideoId { get; set; } = "";
            public string Title { get; set; } = "";
            public string ChannelTitle { get; set; } = "";
            public string ThumbnailUrl { get; set; } = "";
        }

        public async Task<List<VideoResult>> SearchVideosAsync(string apiKey, string query, CancellationToken ct)
        {
            var key = (apiKey ?? "").Trim();
            var q = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(q))
                return new List<VideoResult>();

            try
            {
                var url = "https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=12&q=" +
                          HttpUtility.UrlEncode(q) +
                          "&key=" + HttpUtility.UrlEncode(key);

                using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return new List<VideoResult>();
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    return new List<VideoResult>();

                var list = new List<VideoResult>();
                foreach (var it in items.EnumerateArray())
                {
                    var idObj = it.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Object ? idEl : default;
                    var videoId = idObj.ValueKind == JsonValueKind.Object && idObj.TryGetProperty("videoId", out var vid) ? (vid.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(videoId)) continue;

                    var sn = it.TryGetProperty("snippet", out var snEl) && snEl.ValueKind == JsonValueKind.Object ? snEl : default;
                    var title = sn.ValueKind == JsonValueKind.Object && sn.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "") : "";
                    var channel = sn.ValueKind == JsonValueKind.Object && sn.TryGetProperty("channelTitle", out var cEl) ? (cEl.GetString() ?? "") : "";

                    var thumb = "";
                    if (sn.ValueKind == JsonValueKind.Object &&
                        sn.TryGetProperty("thumbnails", out var thEl) && thEl.ValueKind == JsonValueKind.Object)
                    {
                        if (thEl.TryGetProperty("medium", out var mEl) && mEl.ValueKind == JsonValueKind.Object && mEl.TryGetProperty("url", out var u1) && u1.ValueKind == JsonValueKind.String)
                            thumb = (u1.GetString() ?? "").Trim();
                        else if (thEl.TryGetProperty("default", out var dEl) && dEl.ValueKind == JsonValueKind.Object && dEl.TryGetProperty("url", out var u2) && u2.ValueKind == JsonValueKind.String)
                            thumb = (u2.GetString() ?? "").Trim();
                    }

                    list.Add(new VideoResult
                    {
                        VideoId = videoId.Trim(),
                        Title = (title ?? "").Trim(),
                        ChannelTitle = (channel ?? "").Trim(),
                        ThumbnailUrl = thumb
                    });
                }
                return list;
            }
            catch
            {
                return new List<VideoResult>();
            }
        }
    }
}

