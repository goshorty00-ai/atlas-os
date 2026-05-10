using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AtlasAI.MediaMetadata
{
    public sealed class LastFmAlbumMetadataProvider : IMusicAlbumMetadataProvider
    {
        private static readonly HttpClient _http = new HttpClient();

        public string Name => "Last.fm";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(MusicMetadataHub.GetKey("lastfm_key"));

        public async Task<MusicAlbumMetadata?> TryGetAlbumAsync(MusicAlbumQuery query, CancellationToken ct)
        {
            if (!IsConfigured) return null;
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(artist)) return null;

            try
            {
                var key = MusicMetadataHub.GetKey("lastfm_key");
                var url = "https://ws.audioscrobbler.com/2.0/?method=album.getinfo&api_key=" +
                          HttpUtility.UrlEncode(key) +
                          "&artist=" + HttpUtility.UrlEncode(artist) +
                          "&album=" + HttpUtility.UrlEncode(album) +
                          "&format=json";

                using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("album", out var albumEl) || albumEl.ValueKind != JsonValueKind.Object)
                    return null;

                var meta = new MusicAlbumMetadata { Provider = Name };
                if (albumEl.TryGetProperty("url", out var uEl) && uEl.ValueKind == JsonValueKind.String)
                    meta.ReleaseUrl = (uEl.GetString() ?? "").Trim();

                if (albumEl.TryGetProperty("wiki", out var wikiEl) && wikiEl.ValueKind == JsonValueKind.Object &&
                    wikiEl.TryGetProperty("published", out var pEl) && pEl.ValueKind == JsonValueKind.String)
                {
                    var pub = (pEl.GetString() ?? "").Trim();
                    if (pub.Length >= 4 && int.TryParse(pub.Substring(pub.Length - 4), out var y) && y > 0)
                        meta.Year = y;
                }

                if (albumEl.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Object &&
                    tagsEl.TryGetProperty("tag", out var tagEl) && tagEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tagEl.EnumerateArray())
                    {
                        var name = t.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "").Trim() : "";
                        if (!string.IsNullOrWhiteSpace(name) && meta.Genres.Count < 6) meta.Genres.Add(name);
                    }
                }

                return (meta.Genres.Count > 0 || meta.Year > 0 || !string.IsNullOrWhiteSpace(meta.ReleaseUrl)) ? meta : null;
            }
            catch
            {
                return null;
            }
        }

        public Task<bool> TryDownloadCoverAsync(MusicAlbumQuery query, string destinationPath, CancellationToken ct)
        {
            return Task.FromResult(false);
        }
    }
}
