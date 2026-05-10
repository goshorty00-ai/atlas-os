using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AtlasAI.MediaIntelligence;
using AtlasAI.Services;
using AtlasAI.Settings;
using AtlasAI.Streaming;
using AtlasAI.Views.ViewModels;
using AtlasAI.Voice;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Views.MediaCentre
{
    public partial class ServersView : UserControl
    {
        private const string LocalLibraryServerOption = "Local Library";
        private const string AllServersOption = "All servers";
        private const string ShelfOrderStoreKey = "media_centre_servers_shelf_order";
        private const string ClientShelfOrderStoreKey = "media_centre_servers_client_shelf_order";
        private const string ServersChromePatchScript = """
(() => {
    try {
        const normalize = value => String(value || '').trim().replace(/\s+/g, ' ').toLowerCase();
        const textFor = element => String(element?.innerText || element?.textContent || '').trim();
        const isContinueShelfTitle = value => normalize(value) === 'continue watching';

        const bestTitleFromCard = card => {
            const lines = textFor(card)
                .split('\n')
                .map(line => String(line || '').trim())
                .filter(Boolean);

            const noise = /^(\d{4}|\d{1,3}%|rd|hd|sd)$/i;
            for (const line of lines) {
                if (line.length < 2 || line.length > 140) continue;
                if (noise.test(line)) continue;
                if (line.includes('•')) continue;
                return line;
            }

            return lines[0] || '';
        };

        const shelfTitleForCard = card => {
            let current = card;
            while (current && current !== document.body) {
                const headings = current.querySelectorAll ? current.querySelectorAll('h1,h2,h3,h4,[data-shelf-title]') : [];
                for (const heading of headings) {
                    const title = textFor(heading);
                    if (isContinueShelfTitle(title)) return title;
                }
                current = current.parentElement;
            }
            return '';
        };

        window.__atlasGetContinueCardInfoAt = (x, y) => {
            try {
                let current = document.elementFromPoint(x, y);
                while (current && current !== document.body) {
                    const rect = current.getBoundingClientRect ? current.getBoundingClientRect() : null;
                    const text = bestTitleFromCard(current);
                    if (rect && rect.width >= 120 && rect.height >= 120 && text) {
                        return { shelfTitle: shelfTitleForCard(current), title: text };
                    }
                    current = current.parentElement;
                }
            } catch {
            }

            return null;
        };

        window.__atlasServersApplyChromePatch = () => true;
        window.__atlasServersChromePatchVersion = 'restored';
        return true;
    } catch {
        return false;
    }
})();
""";

        private MediaCentreViewModel? _viewModel;
        private Brush? _defaultBackground;
        private readonly HashSet<MediaCentreViewModel.ServerShelf> _hookedShelves = new();
        private readonly HashSet<INotifyPropertyChanged> _hookedItems = new();
        private bool _vmHooked;
        private bool _serverOptionsHooked;
        private bool _musicAlbumsHooked;
        private bool _catalogItemsHooked;
        private bool _catalogOptionsHooked;
        private bool _genreOptionsHooked;
        private bool _streamSourcesHooked;
        private bool _serverSeriesSeasonsHooked;
        private bool _serverSeriesEpisodesHooked;
        private bool _contextMenuHooked;
        private bool _figmaEnabled;
        private bool _statePostScheduled;
        private System.Drawing.Point _lastContextMenuPoint;
        private DateTime _lastEnteredGridUtc = DateTime.MinValue;
        private DateTime _lastStatePostUtc = DateTime.MinValue;
        private DateTime _lastAutoLoadUtc = DateTime.MinValue;
        private string? _lastPostedStateMessage;
        private string? _lastPostedStreamsMessage;
        private string? _lastNavigatedServersUri;
        private string _selectedServerView = string.Empty;
        private CoreWebView2Environment? _serversEnvironment;
        private Microsoft.Web.WebView2.Wpf.WebView2? _addonManagerWebView;
        private readonly DiscoveryService _discoveryService = new();
        private static readonly JsonSerializerOptions DiscoveryJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        private static MediaItem? FindServerItemByToken(MediaCentreViewModel vm, string token, string? shelfKey = null)
        {
            var key = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) return null;

            static bool Match(MediaItem item, string candidate)
            {
                var tokenValue = candidate.Trim();
                if (string.IsNullOrWhiteSpace(tokenValue)) return false;

                return string.Equals((item.MetaId ?? "").Trim(), tokenValue, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals((item.ImdbId ?? "").Trim(), tokenValue, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals((item.FilePath ?? "").Trim(), tokenValue, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals((item.Title ?? "").Trim(), tokenValue, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(shelfKey))
            {
                var shelf = vm.ServerShelves.FirstOrDefault(s => string.Equals($"{(s.ContentType ?? "").Trim()}::{(s.CatalogId ?? "").Trim()}", shelfKey, StringComparison.OrdinalIgnoreCase));
                var shelfItem = shelf?.Items.FirstOrDefault(i => Match(i, key));
                if (shelfItem != null)
                    return shelfItem;
            }

            return vm.ServerCatalogItems.FirstOrDefault(i => Match(i, key))
                   ?? vm.ServerShelves.SelectMany(s => s.Items).FirstOrDefault(i => Match(i, key));
        }

        private static MediaItem? BuildBridgeMediaItem(JsonElement payload)
        {
            static string GetString(JsonElement payload, string name)
            {
                if (!payload.TryGetProperty(name, out var el)) return "";
                return el.ValueKind switch
                {
                    JsonValueKind.String => (el.GetString() ?? "").Trim(),
                    JsonValueKind.Number => el.ToString().Trim(),
                    _ => ""
                };
            }

            static int GetInt(JsonElement payload, string name)
            {
                if (!payload.TryGetProperty(name, out var el)) return 0;
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var value)) return value;
                if (el.ValueKind == JsonValueKind.String && int.TryParse((el.GetString() ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
                return 0;
            }

            static double GetDouble(JsonElement payload, string name)
            {
                if (!payload.TryGetProperty(name, out var el)) return 0;
                if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var value)) return value;
                if (el.ValueKind == JsonValueKind.String && double.TryParse((el.GetString() ?? "").Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)) return value;
                return 0;
            }

            static IReadOnlyList<string> GetStringList(JsonElement payload, string name)
            {
                if (!payload.TryGetProperty(name, out var el)) return Array.Empty<string>();
                if (el.ValueKind == JsonValueKind.Array)
                {
                    return el.EnumerateArray()
                        .Select(entry => entry.ValueKind == JsonValueKind.String ? (entry.GetString() ?? "").Trim() : entry.ToString().Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                if (el.ValueKind == JsonValueKind.String)
                {
                    return (el.GetString() ?? "")
                        .Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                return Array.Empty<string>();
            }

            static Dictionary<string, double> GetRatings(JsonElement payload, string name)
            {
                var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (!payload.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object) return result;
                foreach (var property in el.EnumerateObject())
                {
                    var value = property.Value.ValueKind == JsonValueKind.Number
                        ? property.Value.GetDouble()
                        : double.TryParse(property.Value.ToString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)
                            ? parsed
                            : 0;
                    if (value > 0)
                        result[property.Name] = value;
                }
                return result;
            }

            static Dictionary<string, string> GetRatingStrings(JsonElement payload, string name)
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!payload.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object) return result;
                foreach (var property in el.EnumerateObject())
                {
                    var value = property.Value.ValueKind == JsonValueKind.String ? (property.Value.GetString() ?? "").Trim() : property.Value.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        result[property.Name] = value;
                }
                return result;
            }

            var metaId = GetString(payload, "metaId");
            var imdbId = GetString(payload, "imdbId");
            var fallbackId = GetString(payload, "id");
            var title = GetString(payload, "title");
            if (string.IsNullOrWhiteSpace(metaId) && string.IsNullOrWhiteSpace(imdbId) && string.IsNullOrWhiteSpace(fallbackId) && string.IsNullOrWhiteSpace(title))
                return null;

            var type = GetString(payload, "type");
            if (string.Equals(type, "tv", StringComparison.OrdinalIgnoreCase)) type = "series";
            if (string.IsNullOrWhiteSpace(type)) type = "movie";

            var summary = GetString(payload, "summary");
            if (string.IsNullOrWhiteSpace(summary)) summary = GetString(payload, "description");
            if (string.IsNullOrWhiteSpace(summary)) summary = GetString(payload, "overview");

            var runtimeMinutes = GetInt(payload, "runtimeMinutes");
            var year = GetInt(payload, "year");
            var filePath = !string.IsNullOrWhiteSpace(metaId)
                ? metaId
                : !string.IsNullOrWhiteSpace(imdbId)
                    ? imdbId
                    : !string.IsNullOrWhiteSpace(fallbackId)
                        ? fallbackId
                        : title;

            var item = new MediaItem
            {
                MetaId = string.IsNullOrWhiteSpace(metaId) ? null : metaId,
                TmdbId = GetInt(payload, "tmdbId") > 0 ? GetInt(payload, "tmdbId") : null,
                ImdbId = imdbId,
                FilePath = filePath,
                Type = type,
                Duration = runtimeMinutes > 0 ? TimeSpan.FromMinutes(runtimeMinutes) : TimeSpan.Zero,
                Title = string.IsNullOrWhiteSpace(title) ? filePath : title,
                Metadata = summary,
                Overview = summary,
                CoverUrl = GetString(payload, "coverUrl"),
                BackdropUrl = GetString(payload, "backdropUrl"),
                LogoUrl = GetString(payload, "logoUrl"),
                TrailerUrl = GetString(payload, "trailerUrl"),
                Year = year,
                Rating = GetDouble(payload, "rating"),
                AiScore = GetDouble(payload, "aiScore"),
                RuntimeMinutes = runtimeMinutes,
                Genres = GetStringList(payload, "genres"),
                RatingsBreakdown = GetRatings(payload, "ratings"),
                RpdbRatings = GetRatingStrings(payload, "rpdbRatings")
            };

            if (string.IsNullOrWhiteSpace(item.CoverUrl))
                item.CoverUrl = GetString(payload, "thumbnail");
            if (string.IsNullOrWhiteSpace(item.CoverUrl))
                item.CoverUrl = GetString(payload, "poster");
            if (string.IsNullOrWhiteSpace(item.BackdropUrl))
                item.BackdropUrl = item.CoverUrl;
            if (string.IsNullOrWhiteSpace(item.TrailerUrl))
                item.TrailerUrl = GetString(payload, "trailer");

            return item;
        }

        private static bool IsHttpLikeUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase));
        }

        private static string ToWebAssetUrl(string? raw)
        {
            var value = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return "";
            if (IsHttpLikeUrl(value)) return value;
            if (!File.Exists(value)) return value;
            return $"https://local-media/{Uri.EscapeDataString(value.Replace('\\', '/'))}";
        }

        private static string BuildLocalAlbumId(AlbumEntry album)
        {
            var folder = (album.SourceFolderPath ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(folder))
                return folder;

            return $"{(album.Artist ?? "").Trim()}::{(album.AlbumTitle ?? "").Trim()}";
        }

        private static double NormalizeOverlayProgress(double progress)
        {
            if (double.IsNaN(progress) || double.IsInfinity(progress)) return 0;
            var normalized = progress > 1 ? progress / 100.0 : progress;
            return Math.Max(0, Math.Min(1, normalized));
        }

        private static IEnumerable<MediaItem> EnumerateLocalMovieItems(MediaCentreViewModel vm)
        {
            return vm.AllItems
                .Where(item => item != null)
                .Where(item =>
                {
                    var type = (item.Type ?? "").Trim();
                    return string.Equals(type, "movie", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(type, "movies", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(type, "tv", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(type, "series", StringComparison.OrdinalIgnoreCase);
                })
                .GroupBy(item => (item.MetaId ?? item.FilePath ?? item.Title ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => item.Year)
                .ThenByDescending(item => item.ReleaseDate ?? DateTime.MinValue)
                .ThenBy(item => item.Title ?? "", StringComparer.OrdinalIgnoreCase);
        }

        private static object BuildLocalMoviePayload(MediaItem item)
        {
            var quality = (item.QualityTier ?? "").Trim();
            var genres = item.Genres?.Where(g => !string.IsNullOrWhiteSpace(g)).Take(4).ToArray() ?? Array.Empty<string>();
            var releaseDate = item.ReleaseDate?.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) ?? "";
            var runtimeMinutes = item.RuntimeMinutes > 0
                ? item.RuntimeMinutes
                : item.Duration.TotalMinutes > 0
                    ? (int)Math.Round(item.Duration.TotalMinutes)
                    : 0;

            return new
            {
                id = (item.MetaId ?? item.FilePath ?? item.Title ?? "").Trim(),
                type = "movie",
                title = (item.Title ?? "Untitled").Trim(),
                subtitle = string.IsNullOrWhiteSpace(item.Metadata) ? "" : item.Metadata.Trim(),
                year = item.Year > 0 ? item.Year : item.ReleaseDate?.Year ?? 0,
                certification = quality,
                runtime = runtimeMinutes,
                genres,
                rating = item.Rating > 0 ? item.Rating : 0,
                resolution = string.IsNullOrWhiteSpace(quality) ? Array.Empty<string>() : new[] { quality },
                audio = Array.Empty<string>(),
                director = "",
                cast = Array.Empty<string>(),
                plot = string.IsNullOrWhiteSpace(item.Overview) ? (item.Metadata ?? "").Trim() : item.Overview.Trim(),
                releaseDate,
                progress = NormalizeOverlayProgress(item.ProgressPercent),
                coverUrl = ToWebAssetUrl(!string.IsNullOrWhiteSpace(item.CoverUrl) ? item.CoverUrl : item.BackdropUrl),
                backdropUrl = ToWebAssetUrl(item.BackdropUrl)
            };
        }

        private static object BuildLocalAlbumPayload(AlbumEntry album, MediaCentreViewModel vm)
        {
            var tracks = album.Tracks?.ToList() ?? new List<MediaItem>();
            var coverSource = tracks
                .Select(track => (track.CoverUrl ?? "").Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            var genres = tracks
                .SelectMany(track => track.Genres ?? Array.Empty<string>())
                .Where(genre => !string.IsNullOrWhiteSpace(genre))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();
            var totalDuration = TimeSpan.FromSeconds(tracks.Sum(track => Math.Max(0, track.Duration.TotalSeconds)));
            var year = tracks.Select(track => track.Year).FirstOrDefault(value => value > 0);
            if (year <= 0)
                year = tracks.Select(track => track.ReleaseDate?.Year ?? 0).FirstOrDefault(value => value > 0);

            var matchesNowPlaying = !string.IsNullOrWhiteSpace(vm.NowPlayingAlbum) &&
                                    string.Equals(vm.NowPlayingAlbum.Trim(), (album.AlbumTitle ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(vm.NowPlayingArtist.Trim(), (album.Artist ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            var progress = matchesNowPlaying && vm.TotalSeconds > 0
                ? NormalizeOverlayProgress(vm.ProgressSeconds / vm.TotalSeconds)
                : 0;

            return new
            {
                id = BuildLocalAlbumId(album),
                type = "album",
                title = (album.AlbumTitle ?? "Untitled Album").Trim(),
                artist = (album.Artist ?? "Unknown Artist").Trim(),
                year,
                genre = genres,
                trackCount = album.TrackCount,
                duration = totalDuration > TimeSpan.Zero
                    ? (totalDuration.TotalHours >= 1
                        ? $"{(int)totalDuration.TotalHours}:{totalDuration.Minutes:00}:{totalDuration.Seconds:00}"
                        : $"{totalDuration.Minutes}:{totalDuration.Seconds:00}")
                    : "0:00",
                label = string.IsNullOrWhiteSpace(album.MusicBrainzLabel) ? "" : album.MusicBrainzLabel.Trim(),
                popularity = matchesNowPlaying ? 100 : 0,
                isFavorite = false,
                progress,
                tracks = tracks.Select(track => (track.Title ?? "").Trim()).Where(title => !string.IsNullOrWhiteSpace(title)).Take(8).ToArray(),
                coverUrl = ToWebAssetUrl(coverSource)
            };
        }

        private static MediaItem? ResolveLocalMovieItem(MediaCentreViewModel vm, string token)
        {
            var key = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) return null;

            return EnumerateLocalMovieItems(vm).FirstOrDefault(item =>
                string.Equals((item.MetaId ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((item.FilePath ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((item.Title ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase));
        }

        private static AlbumEntry? ResolveLocalAlbum(MediaCentreViewModel vm, string token)
        {
            var key = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) return null;

            return vm.MusicAlbums.FirstOrDefault(album =>
                string.Equals(BuildLocalAlbumId(album), key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((album.AlbumTitle ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase));
        }

        private static string GuessContentType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".bmp" => "image/bmp",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".flac" => "audio/flac",
                ".wav" => "audio/wav",
                _ => "application/octet-stream"
            };
        }

        private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                if (ServersFigmaWebView?.CoreWebView2 == null) return;

                var uri = new Uri(e.Request.Uri);
                if (!string.Equals(uri.Host, "local-media", StringComparison.OrdinalIgnoreCase)) return;

                var rawPath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                if (string.IsNullOrWhiteSpace(rawPath)) return;

                var filePath = rawPath.Replace('/', Path.DirectorySeparatorChar);
                if (!File.Exists(filePath)) return;

                var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var headers = $"Content-Type: {GuessContentType(filePath)}\r\nAccess-Control-Allow-Origin: *";
                e.Response = ServersFigmaWebView.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK", headers);
            }
            catch
            {
            }
        }

        private static bool IsSeriesType(string? value)
        {
            var normalized = (value ?? "").Trim();
            return string.Equals(normalized, "series", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "tv", StringComparison.OrdinalIgnoreCase);
        }

        private static MediaItem? ResolveServerItem(MediaCentreViewModel vm, JsonElement payload, string token, string? shelfKey = null)
        {
            var matched = !string.IsNullOrWhiteSpace(token) ? FindServerItemByToken(vm, token, shelfKey) : null;
            var bridged = BuildBridgeMediaItem(payload);
            if (matched == null)
                return bridged;

            if (bridged == null)
                return matched;

            var payloadType = payload.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? (typeEl.GetString() ?? "").Trim()
                : "";

            if (IsSeriesType(payloadType) && !IsSeriesType(matched.Type) && IsSeriesType(bridged.Type))
                return bridged;

            return matched;
        }

        private static AddonSource? ResolvePlaySource(MediaCentreViewModel vm, string sourceId)
        {
            var rawRequested = (sourceId ?? string.Empty).Trim();
            var normalizedRequested = NormalizeSourceToken(rawRequested);
            if (string.IsNullOrWhiteSpace(normalizedRequested))
                return null;

            var fuzzyMatches = new List<AddonSource>();
            foreach (var source in vm.StreamSources)
            {
                var rawCandidate = (source.SourceId ?? string.Empty).Trim();
                if (string.Equals(rawCandidate, rawRequested, StringComparison.OrdinalIgnoreCase))
                    return source;

                var normalizedCandidate = NormalizeSourceToken(rawCandidate);
                if (string.Equals(normalizedCandidate, normalizedRequested, StringComparison.OrdinalIgnoreCase))
                    return source;

                if (!string.IsNullOrWhiteSpace(normalizedCandidate) &&
                    (normalizedCandidate.Contains(normalizedRequested, StringComparison.OrdinalIgnoreCase) ||
                     normalizedRequested.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase)))
                {
                    fuzzyMatches.Add(source);
                }
            }

            if (fuzzyMatches.Count == 1)
                return fuzzyMatches[0];

            var playableSources = vm.StreamSources
                .Where(source => vm.PlayStreamSourceCommand?.CanExecute(source) == true)
                .Take(2)
                .ToList();

            return playableSources.Count == 1 ? playableSources[0] : null;
        }

        private static string NormalizeSourceToken(string value)
        {
            var normalized = DecodeSourceToken(value).Trim();
            return string.IsNullOrWhiteSpace(normalized)
                ? string.Empty
                : string.Join(" ", normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }

        private static string DecodeSourceToken(string value)
        {
            var decoded = value ?? string.Empty;
            try
            {
                decoded = Uri.UnescapeDataString(decoded);
            }
            catch
            {
            }

            return decoded;
        }

        private static string BuildShelfBridgeKey(MediaCentreViewModel.ServerShelf shelf)
        {
            return $"{(shelf.ContentType ?? "").Trim()}::{(shelf.CatalogId ?? "").Trim()}";
        }

        private static List<string> LoadSavedShelfOrder()
        {
            try
            {
                var raw = AtlasAI.Core.IntegrationKeyStore.GetDecrypted(ShelfOrderStoreKey);
                if (string.IsNullOrWhiteSpace(raw))
                    return new List<string>();

                return (JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static void SaveShelfOrder(IEnumerable<string> shelfKeys)
        {
            try
            {
                var normalized = (shelfKeys ?? Enumerable.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                AtlasAI.Core.IntegrationKeyStore.SetProtected(ShelfOrderStoreKey, JsonSerializer.Serialize(normalized));
            }
            catch
            {
            }
        }

        private static List<string> LoadSavedClientShelfOrder()
        {
            try
            {
                var raw = AtlasAI.Core.IntegrationKeyStore.GetDecrypted(ClientShelfOrderStoreKey);
                if (string.IsNullOrWhiteSpace(raw))
                    return new List<string>();

                return (JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static void SaveClientShelfOrder(IEnumerable<string> shelfKeys)
        {
            try
            {
                var normalized = (shelfKeys ?? Enumerable.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                AtlasAI.Core.IntegrationKeyStore.SetProtected(ClientShelfOrderStoreKey, JsonSerializer.Serialize(normalized));
            }
            catch
            {
            }
        }

        private static List<MediaCentreViewModel.ServerShelf> OrderShelvesForClient(IEnumerable<MediaCentreViewModel.ServerShelf> shelves)
        {
            var source = (shelves ?? Enumerable.Empty<MediaCentreViewModel.ServerShelf>())
                .Where(shelf => shelf != null)
                .ToList();
            if (source.Count <= 1)
                return source;

            var savedOrder = LoadSavedShelfOrder();
            if (savedOrder.Count == 0)
                return source;

            var rank = savedOrder
                .Select((key, index) => new { key, index })
                .ToDictionary(entry => entry.key, entry => entry.index, StringComparer.OrdinalIgnoreCase);

            return source
                .Select((shelf, index) => new { shelf, index, key = BuildShelfBridgeKey(shelf) })
                .OrderBy(entry => rank.TryGetValue(entry.key, out var value) ? value : int.MaxValue)
                .ThenBy(entry => entry.index)
                .Select(entry => entry.shelf)
                .ToList();
        }


        public ServersView()
        {
            InitializeComponent();
            DataContextChanged += ServersView_DataContextChanged;
            Loaded += ServersView_Loaded;
            Unloaded += ServersView_Unloaded;
        }

        private void ServersView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _defaultBackground ??= Background;
            }
            catch
            {
            }

            // Clear any stale preview/details from a previous navigation so we start on shelves
            try
            {
                if (DataContext is MediaCentreViewModel vm)
                {
                    if (vm.CloseDetailsCommand?.CanExecute(null) == true)
                        vm.CloseDetailsCommand.Execute(null);
                    if (vm.ClosePreviewCommand?.CanExecute(null) == true)
                        vm.ClosePreviewCommand.Execute(null);
                    if (vm.BackToServerShelvesCommand?.CanExecute(null) == true)
                        vm.BackToServerShelvesCommand.Execute(null);
                }
            }
            catch
            {
            }

            // Clear any stale server overlays so shelves don't load with an old sources/series panel still open.
            try
            {
                if (DataContext is MediaCentreViewModel vm)
                {
                    if (vm.CloseStreamsCommand?.CanExecute(null) == true)
                        vm.CloseStreamsCommand.Execute(null);
                    if (vm.CloseServerSeriesCommand?.CanExecute(null) == true)
                        vm.CloseServerSeriesCommand.Execute(null);
                    if (vm.CloseWatchLaterCommand?.CanExecute(null) == true)
                        vm.CloseWatchLaterCommand.Execute(null);
                    if (vm.CloseContinuePanelCommand?.CanExecute(null) == true)
                        vm.CloseContinuePanelCommand.Execute(null);
                }
            }
            catch
            {
            }

            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (DataContext is MediaCentreViewModel vm)
                            vm.EnsureServerCatalogLoaded();
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }

            try { _ = EnsureInitializedAsync(); } catch { }
        }

        private void ServersView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ServersFigmaWebView?.CoreWebView2 != null)
                    ServersFigmaWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            }
            catch
            {
            }

            try
            {
                if (ServersFigmaWebView != null)
                    ServersFigmaWebView.NavigationCompleted -= ServersFigmaWebView_NavigationCompleted;
            }
            catch
            {
            }

            try { UnhookViewModel(); } catch { }
        }

        private void ServersView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                UnhookViewModel();
                _viewModel = DataContext as MediaCentreViewModel;
                HookViewModel();
                UpdateEmbeddedVisibility();
                SchedulePostState();
            }
            catch
            {
            }
        }

        private void HookViewModel()
        {
            try
            {
                if (_viewModel == null || _vmHooked) return;
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                _viewModel.ServerShelves.CollectionChanged += ServerShelves_CollectionChanged;
                HookShelves(_viewModel.ServerShelves);

                try
                {
                    _viewModel.AddonServerOptions.CollectionChanged += AddonServerOptions_CollectionChanged;
                    _serverOptionsHooked = true;
                }
                catch
                {
                    _serverOptionsHooked = false;
                }

                try
                {
                    _viewModel.MusicAlbums.CollectionChanged += MusicAlbums_CollectionChanged;
                    _musicAlbumsHooked = true;
                }
                catch
                {
                    _musicAlbumsHooked = false;
                }

                try
                {
                    _viewModel.ServerCatalogItems.CollectionChanged += ServerCatalogItems_CollectionChanged;
                    HookItems(_viewModel.ServerCatalogItems);
                    _catalogItemsHooked = true;
                }
                catch
                {
                    _catalogItemsHooked = false;
                }

                try
                {
                    _viewModel.ServerCatalogCatalogs.CollectionChanged += ServerCatalogOptions_CollectionChanged;
                    _catalogOptionsHooked = true;
                }
                catch
                {
                    _catalogOptionsHooked = false;
                }

                try
                {
                    _viewModel.ServerCatalogGenres.CollectionChanged += ServerCatalogGenres_CollectionChanged;
                    _genreOptionsHooked = true;
                }
                catch
                {
                    _genreOptionsHooked = false;
                }

                try
                {
                    _viewModel.StreamSources.CollectionChanged += StreamSources_CollectionChanged;
                    _streamSourcesHooked = true;
                }
                catch
                {
                    _streamSourcesHooked = false;
                }

                try
                {
                    _viewModel.ServerSeriesSeasons.CollectionChanged += ServerSeriesSeasons_CollectionChanged;
                    _serverSeriesSeasonsHooked = true;
                }
                catch
                {
                    _serverSeriesSeasonsHooked = false;
                }

                try
                {
                    _viewModel.VisibleServerSeriesEpisodes.CollectionChanged += ServerSeriesEpisodes_CollectionChanged;
                    _serverSeriesEpisodesHooked = true;
                }
                catch
                {
                    _serverSeriesEpisodesHooked = false;
                }

                _vmHooked = true;
            }
            catch
            {
            }
        }

        private void UnhookViewModel()
        {
            try
            {
                if (_viewModel != null && _vmHooked)
                {
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                    _viewModel.ServerShelves.CollectionChanged -= ServerShelves_CollectionChanged;
                    if (_serverOptionsHooked)
                        _viewModel.AddonServerOptions.CollectionChanged -= AddonServerOptions_CollectionChanged;
                    if (_musicAlbumsHooked)
                        _viewModel.MusicAlbums.CollectionChanged -= MusicAlbums_CollectionChanged;
                    if (_catalogItemsHooked)
                        _viewModel.ServerCatalogItems.CollectionChanged -= ServerCatalogItems_CollectionChanged;
                    if (_catalogOptionsHooked)
                        _viewModel.ServerCatalogCatalogs.CollectionChanged -= ServerCatalogOptions_CollectionChanged;
                    if (_genreOptionsHooked)
                        _viewModel.ServerCatalogGenres.CollectionChanged -= ServerCatalogGenres_CollectionChanged;
                    if (_streamSourcesHooked)
                        _viewModel.StreamSources.CollectionChanged -= StreamSources_CollectionChanged;
                    if (_serverSeriesSeasonsHooked)
                        _viewModel.ServerSeriesSeasons.CollectionChanged -= ServerSeriesSeasons_CollectionChanged;
                    if (_serverSeriesEpisodesHooked)
                        _viewModel.VisibleServerSeriesEpisodes.CollectionChanged -= ServerSeriesEpisodes_CollectionChanged;
                }
            }
            catch
            {
            }

            try
            {
                foreach (var s in _hookedShelves.ToList())
                {
                    try { s.Items.CollectionChanged -= ShelfItems_CollectionChanged; } catch { }
                }
                _hookedShelves.Clear();
            }
            catch
            {
            }

            try
            {
                foreach (var i in _hookedItems.ToList())
                {
                    try { i.PropertyChanged -= ShelfItem_PropertyChanged; } catch { }
                }
                _hookedItems.Clear();
            }
            catch
            {
            }

            _vmHooked = false;
            _viewModel = null;
            _serverOptionsHooked = false;
            _musicAlbumsHooked = false;
            _catalogItemsHooked = false;
            _catalogOptionsHooked = false;
            _genreOptionsHooked = false;
            _streamSourcesHooked = false;
            _serverSeriesSeasonsHooked = false;
            _serverSeriesEpisodesHooked = false;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                var n = e.PropertyName ?? "";
                if (n == nameof(MediaCentreViewModel.IsServerShelvesMode) ||
                    n == nameof(MediaCentreViewModel.IsServerGridMode) ||
                    n == nameof(MediaCentreViewModel.IsServersView) ||
                    n == nameof(MediaCentreViewModel.IsServerStreamsPanelOpen) ||
                    n == nameof(MediaCentreViewModel.IsServerSeriesPanelOpen))
                    UpdateEmbeddedVisibility();

                try
                {
                    if (n == nameof(MediaCentreViewModel.IsServerGridMode) && _viewModel != null && _viewModel.IsServerGridMode)
                        _lastEnteredGridUtc = DateTime.UtcNow;
                }
                catch
                {
                }

                if (n == nameof(MediaCentreViewModel.IsServerShelvesMode) ||
                    n == nameof(MediaCentreViewModel.IsServerGridMode) ||
                    n == nameof(MediaCentreViewModel.SelectedAddonServer) ||
                    n == nameof(MediaCentreViewModel.SelectedAlbum) ||
                    n == nameof(MediaCentreViewModel.SelectedServerCatalogType) ||
                    n == nameof(MediaCentreViewModel.SelectedServerCatalogCatalogId) ||
                    n == nameof(MediaCentreViewModel.CanLoadMoreServerCatalog) ||
                    n == nameof(MediaCentreViewModel.CanLoadNextServerCatalogPage) ||
                    n == nameof(MediaCentreViewModel.ServerCatalogHasMore) ||
                    n == nameof(MediaCentreViewModel.IsServerCatalogBusy) ||
                    n == nameof(MediaCentreViewModel.ActiveServerShelf) ||
                    n == nameof(MediaCentreViewModel.PreviewItem) ||
                        n == nameof(MediaCentreViewModel.DetailsItem) ||
                        n == nameof(MediaCentreViewModel.PreviewOverview) ||
                        n == nameof(MediaCentreViewModel.DetailsOverview) ||
                        n == nameof(MediaCentreViewModel.PreviewRuntimeText) ||
                        n == nameof(MediaCentreViewModel.NowPlayingTitle) ||
                        n == nameof(MediaCentreViewModel.NowPlayingArtist) ||
                        n == nameof(MediaCentreViewModel.NowPlayingAlbum) ||
                        n == nameof(MediaCentreViewModel.ProgressSeconds) ||
                        n == nameof(MediaCentreViewModel.TotalSeconds) ||
                        n == nameof(MediaCentreViewModel.IsStreamsBusy) ||
                        n == nameof(MediaCentreViewModel.StreamsStatusText) ||
                        n == nameof(MediaCentreViewModel.IsServerSeriesPanelOpen) ||
                        n == nameof(MediaCentreViewModel.IsServerSeriesBusy) ||
                        n == nameof(MediaCentreViewModel.ServerSeriesStatusText) ||
                        n == nameof(MediaCentreViewModel.SelectedServerSeriesSeason))
                    SchedulePostState();
            }
            catch
            {
            }
        }

        private void AddonServerOptions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try { SchedulePostState(); } catch { }
        }

        private void MusicAlbums_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try { SchedulePostState(); } catch { }
        }

        private void ServerCatalogItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (e.OldItems != null)
                {
                    foreach (var obj in e.OldItems)
                    {
                        if (obj is not INotifyPropertyChanged npc) continue;
                        if (_hookedItems.Remove(npc))
                        {
                            try { npc.PropertyChanged -= ShelfItem_PropertyChanged; } catch { }
                        }
                    }
                }

                if (e.NewItems != null)
                    HookItems(e.NewItems.Cast<object>());
            }
            catch
            {
            }

            try { SchedulePostState(); } catch { }
        }

        private void ServerCatalogOptions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try { SchedulePostState(); } catch { }
        }

        private void ServerCatalogGenres_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try { SchedulePostState(); } catch { }
        }

        private void StreamSources_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try { SchedulePostState(); } catch { }
        }

        private void ServerSeriesSeasons_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try { SchedulePostState(); } catch { }
        }

        private void ServerSeriesEpisodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try { SchedulePostState(); } catch { }
        }

        private void ServerShelves_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (_viewModel == null) return;

                // If shelves were rebuilt while the UI is in a "grid" page, force back to shelves.
                // IMPORTANT: don't do this immediately after the user navigates into grid/details,
                // or it will snap them back instantly.
                try
                {
                    if (_viewModel.IsServerGridMode)
                    {
                        var isFullRebuild = e != null &&
                                           (e.Action == NotifyCollectionChangedAction.Reset ||
                                            e.Action == NotifyCollectionChangedAction.Replace);

                        var hasActivePreview = _viewModel.PreviewItem != null;
                        var graceMs = (DateTime.UtcNow - _lastEnteredGridUtc).TotalMilliseconds;

                        if (isFullRebuild && !hasActivePreview && graceMs > 1500)
                        {
                            if (_viewModel.BackToServerShelvesCommand?.CanExecute(null) == true)
                                _viewModel.BackToServerShelvesCommand.Execute(null);
                        }
                    }
                }
                catch
                {
                }

                HookShelves(_viewModel.ServerShelves);
                SchedulePostState();
            }
            catch
            {
            }
        }

        private void HookShelves(IEnumerable<MediaCentreViewModel.ServerShelf> shelves)
        {
            foreach (var s in shelves)
            {
                if (s == null) continue;
                if (_hookedShelves.Add(s))
                {
                    try { s.Items.CollectionChanged += ShelfItems_CollectionChanged; } catch { }
                }
                HookItems(s.Items);
            }
        }

        private void HookItems(IEnumerable<object> items)
        {
            foreach (var obj in items)
            {
                if (obj is not INotifyPropertyChanged npc) continue;
                if (_hookedItems.Add(npc))
                {
                    try { npc.PropertyChanged += ShelfItem_PropertyChanged; } catch { }
                }
            }
        }

        private void ShelfItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                var n = e.PropertyName ?? "";
                if (string.Equals(n, "CoverUrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "CoverImage", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "Title", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "Year", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "Rating", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "Metadata", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "Overview", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "BackdropUrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "LogoUrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "MetaId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "ImdbId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "ProgressPercent", StringComparison.OrdinalIgnoreCase))
                {
                    SchedulePostState();
                }
            }
            catch
            {
            }
        }

        private void ShelfItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (e.NewItems != null)
                {
                    foreach (var o in e.NewItems)
                    {
                        if (o == null) continue;
                        HookItems(new[] { o });
                    }
                }
            }
            catch
            {
            }
            try { SchedulePostState(); } catch { }
        }

        private async Task EnsureInitializedAsync()
        {
            try
            {
                if (ServersFigmaWebView == null) return;
                var alreadyInitialized = ServersFigmaWebView.CoreWebView2 != null;

                string? dist = null;
                try
                {
                    dist = FindMediaStreamerDist();
                }
                catch
                {
                    dist = null;
                }

                if (!alreadyInitialized)
                {
                    if (dist == null) return;
                    _serversEnvironment ??= await CreateServersWebViewEnvironmentAsync();
                    await ServersFigmaWebView.EnsureCoreWebView2Async(_serversEnvironment);
                }

                if (ServersFigmaWebView.CoreWebView2 == null) return;

                // Opaque background so no legacy WPF visuals can bleed through behind the WebView.
                try { ServersFigmaWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 8, 10, 16); } catch { }

                try
                {
                    ServersFigmaWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                    ServersFigmaWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    ServersFigmaWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    ServersFigmaWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                }
                catch
                {
                }

                var host = "atlas-ui-servers";
                if (dist != null)
                {
                    try
                    {
                        ServersFigmaWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                            host,
                            dist,
                            CoreWebView2HostResourceAccessKind.Allow);
                    }
                    catch
                    {
                        // Mapping may already exist; ignore.
                    }
                }

                try
                {
                    ServersFigmaWebView.CoreWebView2.AddWebResourceRequestedFilter("https://local-media/*", CoreWebView2WebResourceContext.All);
                    ServersFigmaWebView.CoreWebView2.WebResourceRequested -= CoreWebView2_WebResourceRequested;
                    ServersFigmaWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
                }
                catch
                {
                }

                try
                {
                    if (!_contextMenuHooked)
                    {
                        _contextMenuHooked = true;
                        ServersFigmaWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
                    }
                }
                catch
                {
                }

                try
                {
                    // Hook WebMessageReceived once per CoreWebView2 instance.
                    ServersFigmaWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    ServersFigmaWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                }
                catch
                {
                }

                try
                {
                    ServersFigmaWebView.NavigationCompleted -= ServersFigmaWebView_NavigationCompleted;
                    ServersFigmaWebView.NavigationCompleted += ServersFigmaWebView_NavigationCompleted;
                }
                catch
                {
                }

                long newestWriteTicks = 0;
                try
                {
                    var indexPath = Path.Combine(dist, "index.html");
                    if (File.Exists(indexPath))
                        newestWriteTicks = Math.Max(newestWriteTicks, File.GetLastWriteTimeUtc(indexPath).Ticks);

                    var assetsDir = Path.Combine(dist, "assets");
                    if (Directory.Exists(assetsDir))
                    {
                        foreach (var fp in Directory.EnumerateFiles(assetsDir, "*", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                newestWriteTicks = Math.Max(newestWriteTicks, File.GetLastWriteTimeUtc(fp).Ticks);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }

                if (dist != null)
                {
                    var v = (newestWriteTicks != 0 ? newestWriteTicks : DateTime.UtcNow.Ticks).ToString();
                    var targetUri = $"https://{host}/index.html?v={v}";
                    // The embedded Figma bundle uses React Router (browser router) with routes like '/', '/browse', etc.
                    // Do not force hash routes (e.g. '#/servers') which will produce a 404 inside the app.
                    var shouldNavigate = !alreadyInitialized ||
                                         !string.Equals(_lastNavigatedServersUri, targetUri, StringComparison.OrdinalIgnoreCase);
                    if (shouldNavigate)
                    {
                        _lastPostedStateMessage = null;
                        _lastPostedStreamsMessage = null;
                        _lastNavigatedServersUri = targetUri;
                        try { Console.WriteLine($"[ServersWebView:navigate] initialized={alreadyInitialized} dist={dist} uri={targetUri}"); } catch { }
                        ServersFigmaWebView.CoreWebView2.Navigate(targetUri);
                    }
                }

                _figmaEnabled = true;
                UpdateEmbeddedVisibility();
                SchedulePostState();
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[ServersWebView:init-failed] {ex.Message}"); } catch { }
            }
        }

        public async Task OpenShelfToolsAsync()
        {
            try
            {
                await EnsureInitializedAsync().ConfigureAwait(true);
                if (ServersFigmaWebView?.CoreWebView2 == null)
                    return;

                var msg = JsonSerializer.Serialize(new { type = "servers.openShelfTools" });
                ServersFigmaWebView.CoreWebView2.PostWebMessageAsJson(msg);
            }
            catch
            {
            }
        }

        private static async Task<CoreWebView2Environment> CreateServersWebViewEnvironmentAsync()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasAI", "webview_servers"),
                Path.Combine(Path.GetTempPath(), "AtlasOS_WebView2", "Servers"),
            };

            Exception? lastError = null;
            foreach (var folder in candidates)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    var env = await CoreWebView2Environment.CreateAsync(null, folder);
                    try { Console.WriteLine($"[ServersWebView:env] {folder}"); } catch { }
                    return env;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try { Console.WriteLine($"[ServersWebView:env-failed] {folder} :: {ex.Message}"); } catch { }
                }
            }

            throw lastError ?? new InvalidOperationException("Unable to create a WebView2 environment for ServersView.");
        }

        private void CoreWebView2_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                if (ServersFigmaWebView?.CoreWebView2 == null) return;
                _lastContextMenuPoint = e.Location;

                var env = ServersFigmaWebView.CoreWebView2.Environment;
                if (env == null) return;

                var remove = env.CreateContextMenuItem("Remove from Continue Watching", null, CoreWebView2ContextMenuItemKind.Command);
                remove.CustomItemSelected += async (_, __) => await RemoveContinueAtLastContextPointAsync();

                // Put it near the top; harmless if not on a Continue card (handler will no-op).
                e.MenuItems.Insert(0, remove);
            }
            catch
            {
            }
        }

        private async Task RemoveContinueAtLastContextPointAsync()
        {
            try
            {
                if (ServersFigmaWebView?.CoreWebView2 == null) return;

                // Ask the page what Continue card (if any) is under the context-menu point.
                var x = _lastContextMenuPoint.X;
                var y = _lastContextMenuPoint.Y;
                var raw = await ServersFigmaWebView.ExecuteScriptAsync($"(() => {{ try {{ return JSON.stringify(window.__atlasGetContinueCardInfoAt && window.__atlasGetContinueCardInfoAt({x},{y}) || null); }} catch {{ return 'null'; }} }})()")
                    .ConfigureAwait(false);

                var json = (raw ?? "").Trim();
                if (json.StartsWith("\"", StringComparison.Ordinal) && json.EndsWith("\"", StringComparison.Ordinal) && json.Length >= 2)
                {
                    try { json = JsonSerializer.Deserialize<string>(json) ?? ""; } catch { }
                }
                if (string.IsNullOrWhiteSpace(json) || json == "null") return;

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
                var title = doc.RootElement.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String ? (tEl.GetString() ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(title)) return;

                var removed = 0;
                try
                {
                    // Prefer removing by stable key (MetaId/FilePath) if we can match the clicked card to a shelf item.
                    var bestKey = "";
                    try
                    {
                        var vm = _viewModel;
                        var shelf = vm?.ServerShelves?.FirstOrDefault(s => string.Equals((s?.Title ?? "").Trim(), "Continue Watching", StringComparison.OrdinalIgnoreCase));
                        if (shelf != null)
                        {
                            var wantedNorm = AtlasAI.Core.WatchHistoryStore.NormalizeTitleForMatch(title);
                            if (string.IsNullOrWhiteSpace(wantedNorm)) wantedNorm = title;
                            var match = shelf.Items
                                .FirstOrDefault(i =>
                                {
                                    var it = (i?.Title ?? "").Trim();
                                    if (string.IsNullOrWhiteSpace(it)) return false;
                                    var norm = AtlasAI.Core.WatchHistoryStore.NormalizeTitleForMatch(it);
                                    if (string.IsNullOrWhiteSpace(norm)) norm = it;
                                    if (string.Equals(norm, wantedNorm, StringComparison.OrdinalIgnoreCase)) return true;
                                    if (wantedNorm.Length >= 4 && norm.IndexOf(wantedNorm, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                                    if (norm.Length >= 4 && wantedNorm.IndexOf(norm, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                                    return false;
                                });
                            if (match != null)
                                bestKey = ((match.MetaId ?? match.FilePath) ?? "").Trim();
                        }
                    }
                    catch
                    {
                        bestKey = "";
                    }

                    if (!string.IsNullOrWhiteSpace(bestKey))
                        removed = AtlasAI.Core.WatchHistoryStore.RemoveMatching(bestKey);

                    if (removed <= 0)
                        removed = AtlasAI.Core.WatchHistoryStore.RemoveByTitle(title);
                }
                catch
                {
                    removed = 0;
                }

                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            _viewModel?.NotifyWatchHistoryChanged();
                            SchedulePostState();
                        }
                        catch { }
                    });
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private static string? FindMediaStreamerDist()
        {
            const string mediaStreamerFolder = "Media Streamer";
            var candidates = new List<string>();

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var shipped = Path.Combine(baseDir, "Figma", mediaStreamerFolder, "dist");
                if (Directory.Exists(shipped) && File.Exists(Path.Combine(shipped, "index.html")))
                    candidates.Add(shipped);
            }
            catch
            {
            }

            var roots = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            };

            foreach (var root in roots)
            {
                try
                {
                    var dir = new DirectoryInfo(root);
                    for (var i = 0; i < 10 && dir != null; i++)
                    {
                        var figmaRoot = Path.Combine(dir.FullName, "Figma");
                        if (Directory.Exists(figmaRoot))
                        {
                            var uiFolder = Directory.GetDirectories(figmaRoot, mediaStreamerFolder, SearchOption.TopDirectoryOnly)
                                .FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(uiFolder))
                            {
                                var dist = Path.Combine(uiFolder, "dist");
                                if (Directory.Exists(dist) && File.Exists(Path.Combine(dist, "index.html")))
                                    candidates.Add(dist);
                            }
                        }
                        dir = dir.Parent;
                    }
                }
                catch
                {
                }
            }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(GetNewestDistWriteTicks)
                .FirstOrDefault();
        }

        private async void ServersFigmaWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                if (ServersFigmaWebView?.CoreWebView2 == null) return;
                try
                {
                    var source = ServersFigmaWebView.Source?.ToString() ?? _lastNavigatedServersUri ?? "";
                    Console.WriteLine($"[ServersWebView:navigation-complete] success={e.IsSuccess} status={(int)e.WebErrorStatus} source={source}");
                }
                catch
                {
                }

                if (!e.IsSuccess)
                    return;

                // Clear any stale preview from a previous navigation so we start on shelves
                try
                {
                    var vm = _viewModel;
                    if (vm?.PreviewItem != null)
                    {
                        if (vm.ClosePreviewCommand?.CanExecute(null) == true)
                            vm.ClosePreviewCommand.Execute(null);
                        if (vm.CloseDetailsCommand?.CanExecute(null) == true)
                            vm.CloseDetailsCommand.Execute(null);
                    }
                }
                catch { }

                // Keep the bundle's own routing. We only sync state via postMessage.
                await ApplyServersChromePatchAsync();
                SchedulePostState();
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[ServersWebView:navigation-handler-failed] {ex.Message}"); } catch { }
            }
        }

        private async Task ApplyServersChromePatchAsync()
        {
            try
            {
                if (ServersFigmaWebView == null || ServersFigmaWebView.CoreWebView2 == null) return;
                await ServersFigmaWebView.ExecuteScriptAsync(ServersChromePatchScript);
            }
            catch
            {
            }
        }

        private void TryHookAddonManagerCloseBridge(DependencyObject root)
        {
            try
            {
                var addonWebView = FindDescendant<Microsoft.Web.WebView2.Wpf.WebView2>(root);
                if (addonWebView == null)
                    return;

                if (ReferenceEquals(_addonManagerWebView, addonWebView))
                    return;

                UnhookAddonManagerCloseBridge();
                _addonManagerWebView = addonWebView;
                _addonManagerWebView.CoreWebView2InitializationCompleted += AddonManagerWebView_CoreWebView2InitializationCompleted;

                if (_addonManagerWebView.CoreWebView2 != null)
                {
                    _addonManagerWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    _addonManagerWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                }
            }
            catch
            {
            }
        }

        private void AddonManagerWebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            try
            {
                if (!e.IsSuccess)
                    return;

                if (sender is Microsoft.Web.WebView2.Wpf.WebView2 webView && webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                }
            }
            catch
            {
            }
        }

        private void UnhookAddonManagerCloseBridge()
        {
            try
            {
                if (_addonManagerWebView != null)
                {
                    _addonManagerWebView.CoreWebView2InitializationCompleted -= AddonManagerWebView_CoreWebView2InitializationCompleted;
                    if (_addonManagerWebView.CoreWebView2 != null)
                        _addonManagerWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                }
            }
            catch
            {
            }

            _addonManagerWebView = null;
        }

        private void CloseAddonManagerOverlay()
        {
            try
            {
                if (AddonManagerOverlay?.Content is AddonsView addonsView)
                    addonsView.CloseRequested -= AddonsView_CloseRequested;
            }
            catch
            {
            }

            try
            {
                if (AddonManagerOverlay != null)
                {
                    AddonManagerOverlay.Content = null;
                    AddonManagerOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
            }

            try
            {
                if (ServersFigmaWebView != null)
                    ServersFigmaWebView.Visibility = Visibility.Visible;
            }
            catch
            {
            }

            UnhookAddonManagerCloseBridge();
        }

        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null)
                return null;

            if (root is T typed)
                return typed;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var match = FindDescendant<T>(VisualTreeHelper.GetChild(root, index));
                if (match != null)
                    return match;
            }

            return null;
        }

        private void AddonsView_CloseRequested(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.Invoke(CloseAddonManagerOverlay);
            }
            catch
            {
            }
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                if (string.IsNullOrWhiteSpace(json)) return;

                try
                {
                    Console.WriteLine($"[AddonNavTest] backend.received raw={json}");
                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] backend.received raw={json}");
                }
                catch
                {
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) return;
                var type = typeEl.GetString() ?? "";
                var payload = root.TryGetProperty("payload", out var payloadEl) ? payloadEl : default;

                try
                {
                    AtlasAI.Core.AppLogger.LogInfo($"[DiscoveryHeroData] servers.received type={type}");
                }
                catch
                {
                }

                switch (type)
                {
                    case "mediahub.openExternalUrl":
                    case "servers.openExternalUrl":
                        try
                        {
                            var url = payload.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String ? (urlEl.GetString() ?? "").Trim() : "";
                            if (string.IsNullOrWhiteSpace(url))
                                break;
                            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                                break;

                            var vm = _viewModel;
                            if (vm == null)
                                break;

                            Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    var appsCategory = vm.Categories.FirstOrDefault(c => string.Equals(c.Id, "apps", StringComparison.OrdinalIgnoreCase));
                                    if (appsCategory != null && vm.SelectCategoryCommand?.CanExecute(appsCategory) == true)
                                        vm.SelectCategoryCommand.Execute(appsCategory);
                                }
                                catch
                                {
                                }

                                vm.NavigateToAppsBrowserUrl(url);
                            });
                        }
                        catch
                        {
                        }
                        break;
                    case "discovery.getData":
                        try
                        {
                            AtlasAI.Core.AppLogger.LogInfo("[DiscoveryHeroData] servers.discovery.getData received=true");
                        }
                        catch
                        {
                        }
                        _ = PostDiscoveryDataFromServersAsync();
                        break;
                    case "servers.clientError":
                        try
                        {
                            var message = payload.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String ? (msgEl.GetString() ?? "").Trim() : "";
                            var source = payload.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.String ? (srcEl.GetString() ?? "").Trim() : "";
                            var stack = payload.TryGetProperty("stack", out var stackEl) && stackEl.ValueKind == JsonValueKind.String ? (stackEl.GetString() ?? "").Trim() : "";
                            if (!string.IsNullOrWhiteSpace(message))
                            {
                                Console.WriteLine($"[ServersWebView:error] {message}");
                                if (!string.IsNullOrWhiteSpace(source))
                                    Console.WriteLine($"[ServersWebView:source] {source}");
                                if (!string.IsNullOrWhiteSpace(stack))
                                    Console.WriteLine($"[ServersWebView:stack] {stack}");
                                try
                                {
                                    AtlasAI.Core.AppLogger.LogError($"[ServersWebView] {message}{(string.IsNullOrWhiteSpace(source) ? "" : $" | source={source}")}{(string.IsNullOrWhiteSpace(stack) ? "" : $" | stack={stack}")}");
                                }
                                catch
                                {
                                }
                            }
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.ready":
                    case "servers.getState":
                        try
                        {
                            Console.WriteLine($"[ServersWebView:message] {type}");
                            AtlasAI.Core.AppLogger.LogInfo($"[ServersWebView] message={type}");
                        }
                        catch
                        {
                        }
                        try
                        {
                            if (_viewModel != null)
                            {
                                _viewModel.UseServersWebViewBridge = true;
                            }
                        }
                        catch
                        {
                        }
                        try { _viewModel?.ReloadAddonServersFromStore(); } catch { }
                        SchedulePostState();
                        _ = PostDiscoveryDataFromServersAsync();
                        break;
                    case "servers.playSource":
                        try
                        {
                            var sourceId = payload.TryGetProperty("sourceId", out var sourceIdEl) && sourceIdEl.ValueKind == JsonValueKind.String ? (sourceIdEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null || string.IsNullOrWhiteSpace(sourceId)) break;
                            var source = ResolvePlaySource(vm, sourceId);
                            if (source != null && vm.PlayStreamSourceCommand?.CanExecute(source) == true)
                                vm.PlayStreamSourceCommand.Execute(source);
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.ai.query":
                        try
                        {
                            var id = payload.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? (idEl.GetString() ?? "").Trim() : "";
                            var title = payload.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String ? (titleEl.GetString() ?? "").Trim() : "";
                            var text = payload.TryGetProperty("text", out var tEl) && tEl.ValueKind == JsonValueKind.String ? (tEl.GetString() ?? "").Trim() : "";
                            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text))
                                break;
                            _ = Task.Run(() => HandleAiQueryAsync(id, text, title));
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.ai.speak":
                        try
                        {
                            var text = payload.TryGetProperty("text", out var speakEl) && speakEl.ValueKind == JsonValueKind.String ? (speakEl.GetString() ?? "").Trim() : "";
                            if (string.IsNullOrWhiteSpace(text))
                                break;
                            _ = Task.Run(() => SpeakMediaResultAsync(text));
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.setServer":
                        try
                        {
                            var server = payload.TryGetProperty("server", out var sEl) && sEl.ValueKind == JsonValueKind.String ? (sEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null) break;
                            var wantsLocalLibrary = string.Equals(server, LocalLibraryServerOption, StringComparison.OrdinalIgnoreCase);
                            _selectedServerView = wantsLocalLibrary
                                ? LocalLibraryServerOption
                                : (string.IsNullOrWhiteSpace(server) ? AllServersOption : server);
                            if (!wantsLocalLibrary)
                                vm.SelectedAddonServer = string.IsNullOrWhiteSpace(server) ? AllServersOption : server;
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.copyServerLink":
                        try
                        {
                            var url = payload.TryGetProperty("url", out var copyEl) && copyEl.ValueKind == JsonValueKind.String ? (copyEl.GetString() ?? "").Trim() : "";
                            if (!string.IsNullOrWhiteSpace(url))
                                Dispatcher.Invoke(() =>
                                {
                                    for (var attempt = 0; attempt < 3; attempt++)
                                    {
                                        try
                                        {
                                            Clipboard.SetText(url);
                                            Clipboard.Flush();
                                            break;
                                        }
                                        catch
                                        {
                                            if (attempt >= 2)
                                                throw;

                                            try
                                            {
                                                System.Threading.Thread.Sleep(30);
                                            }
                                            catch
                                            {
                                            }
                                        }
                                    }
                                });
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.saveShelfOrder":
                        try
                        {
                            if (payload.TryGetProperty("shelfIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
                            {
                                var ids = idsEl.EnumerateArray()
                                    .Where(entry => entry.ValueKind == JsonValueKind.String)
                                    .Select(entry => (entry.GetString() ?? "").Trim())
                                    .Where(value => !string.IsNullOrWhiteSpace(value))
                                    .ToList();
                                SaveShelfOrder(ids);
                            }

                            if (payload.TryGetProperty("clientShelfIds", out var clientIdsEl) && clientIdsEl.ValueKind == JsonValueKind.Array)
                            {
                                var clientIds = clientIdsEl.EnumerateArray()
                                    .Where(entry => entry.ValueKind == JsonValueKind.String)
                                    .Select(entry => (entry.GetString() ?? "").Trim())
                                    .Where(value => !string.IsNullOrWhiteSpace(value))
                                    .ToList();
                                SaveClientShelfOrder(clientIds);
                            }
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.openAddonManager":
                        try
                        {
                            try
                            {
                                Console.WriteLine("[AddonNavTest] handler.matched=true");
                                AtlasAI.Core.AppLogger.LogInfo("[AddonNavTest] handler.matched=true");
                            }
                            catch
                            {
                            }

                            var overlayExists = AddonManagerOverlay != null;
                            try
                            {
                                Console.WriteLine($"[AddonNavTest] overlay.exists={overlayExists.ToString().ToLowerInvariant()}");
                                AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] overlay.exists={overlayExists.ToString().ToLowerInvariant()}");
                            }
                            catch
                            {
                            }

                            if (!overlayExists)
                            {
                                try
                                {
                                    Console.WriteLine("[AddonNavTest] failed reason=overlay_missing");
                                    AtlasAI.Core.AppLogger.LogError("[AddonNavTest] failed reason=overlay_missing");
                                }
                                catch
                                {
                                }
                                break;
                            }

                            // Instantiate the real AddonsView (from Figma/Addon Manager backend)
                            UnhookAddonManagerCloseBridge();
                            var addonsView = new AddonsView();
                            try
                            {
                                Console.WriteLine("[AddonNavTest] addonsView.created=true");
                                AtlasAI.Core.AppLogger.LogInfo("[AddonNavTest] addonsView.created=true");
                            }
                            catch
                            {
                            }
                            
                            // Set DataContext to the existing ViewModel singleton
                            addonsView.DataContext = MediaCentreViewModel.Instance;
                            addonsView.CloseRequested += AddonsView_CloseRequested;
                            
                            // Add to overlay container
                            if (AddonManagerOverlay != null)
                            {
                                try
                                {
                                    if (ServersFigmaWebView != null)
                                    {
                                        ServersFigmaWebView.Visibility = Visibility.Collapsed;
                                        AtlasAI.Core.AppLogger.LogInfo("[AddonNavTest] servers.webview.hidden=true reason=open-addon-manager");
                                    }
                                }
                                catch
                                {
                                }

                                try
                                {
                                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] overlay.bounds width={AddonManagerOverlay.ActualWidth:0.##} height={AddonManagerOverlay.ActualHeight:0.##}");
                                }
                                catch
                                {
                                }

                                AddonManagerOverlay.Content = addonsView;
                                AddonManagerOverlay.Visibility = Visibility.Visible;
                                TryHookAddonManagerCloseBridge(addonsView);
                                try
                                {
                                    AtlasAI.Core.AppLogger.LogInfo("[AddonNavTest] addon.overlay.primary=true");
                                }
                                catch
                                {
                                }

                                var overlayVisible = AddonManagerOverlay.Visibility == Visibility.Visible;
                                try
                                {
                                    Console.WriteLine($"[AddonNavTest] overlay.visible={overlayVisible.ToString().ToLowerInvariant()}");
                                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] overlay.visible={overlayVisible.ToString().ToLowerInvariant()}");
                                }
                                catch
                                {
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                Console.WriteLine($"[AddonNavTest] failed reason={ex.Message}");
                                AtlasAI.Core.AppLogger.LogError($"[AddonNavTest] failed reason={ex.Message}", ex);
                            }
                            catch
                            {
                            }
                        }
                        break;
                    case "addons.close":
                        try
                        {
                            Dispatcher.Invoke(CloseAddonManagerOverlay);
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.setType":
                        try
                        {
                            var t = payload.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String ? (tEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null) break;
                            if (!string.IsNullOrWhiteSpace(t))
                                vm.SelectedServerCatalogType = t;
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.setCatalog":
                        try
                        {
                            var cid = payload.TryGetProperty("catalogId", out var cEl) && cEl.ValueKind == JsonValueKind.String ? (cEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null) break;
                            if (!string.IsNullOrWhiteSpace(cid))
                                vm.SelectedServerCatalogCatalogId = cid;
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.refresh":
                        try
                        {
                            var vm = _viewModel;
                            if (vm?.RefreshServerCatalogCommand?.CanExecute(null) == true)
                                vm.RefreshServerCatalogCommand.Execute(null);
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.setGenre":
                        try
                        {
                            var genre = payload.TryGetProperty("genre", out var gEl) && gEl.ValueKind == JsonValueKind.String ? (gEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null) break;
                            vm.SelectedServerCatalogGenre = genre;
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.openShelf":
                    case "servers.seeAll":
                        try
                        {
                            var key = payload.TryGetProperty("key", out var kEl) ? (kEl.GetString() ?? "").Trim() : "";
                            if (string.IsNullOrWhiteSpace(key)) break;
                            var vm = _viewModel;
                            if (vm == null) break;
                            var shelf = vm.ServerShelves.FirstOrDefault(s => string.Equals($"{(s.ContentType ?? "").Trim()}::{(s.CatalogId ?? "").Trim()}", key, StringComparison.OrdinalIgnoreCase));
                            if (shelf != null)
                            {
                                // Prefer opening a real paged catalog for "View All" so Load More works.
                                if (vm.OpenServerShelfCatalogCommand?.CanExecute(shelf) == true)
                                    vm.OpenServerShelfCatalogCommand.Execute(shelf);
                                else if (vm.OpenServerShelfCommand?.CanExecute(shelf) == true)
                                    vm.OpenServerShelfCommand.Execute(shelf);
                            }
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.back":
                        try
                        {
                            var vm = _viewModel;
                            if (vm == null) break;
                            if (vm.CloseDetailsCommand?.CanExecute(null) == true)
                                vm.CloseDetailsCommand.Execute(null);
                            if (vm.BackToServerShelvesCommand?.CanExecute(null) == true)
                                vm.BackToServerShelvesCommand.Execute(null);
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.loadMore":
                        try
                        {
                            var vm = _viewModel;
                            if (vm == null) break;
                            try
                            {
                                AtlasAI.Core.AppLogger.LogInfo($"[ServersWebView] loadMore requested busy={vm.IsServerCatalogBusy} canLoadMore={vm.CanLoadMoreServerCatalog} visible={vm.ServerCatalogItems.Count} mode={(vm.IsServerShelvesMode ? "shelves" : "grid")}");
                            }
                            catch
                            {
                            }
                            // In the embedded React grid, a load-more action should prefer fetching the next
                            // server page immediately. Falling back to the local visible-count step makes the
                            // user spend clicks revealing items the host already has buffered.
                            try
                            {
                                if (!vm.IsServerCatalogBusy && vm.CanLoadNextServerCatalogPage && vm.NextServerCatalogPageCommand != null)
                                    vm.NextServerCatalogPageCommand.Execute(null);
                                else if (vm.LoadMoreServerCatalogButtonCommand != null)
                                    vm.LoadMoreServerCatalogButtonCommand.Execute(null);
                                else
                                    vm.TryLoadMoreServerCatalog();
                            }
                            catch
                            {
                            }
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.closeDetails":
                        try
                        {
                            var vm = _viewModel;
                            if (vm == null) break;
                            if (vm.ClosePreviewCommand?.CanExecute(null) == true)
                                vm.ClosePreviewCommand.Execute(null);
                            if (vm.CloseDetailsCommand?.CanExecute(null) == true)
                                vm.CloseDetailsCommand.Execute(null);
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.play":
                        try
                        {
                            var metaId = payload.TryGetProperty("metaId", out var idEl) && idEl.ValueKind == JsonValueKind.String ? (idEl.GetString() ?? "").Trim() : "";
                            var fallbackId = payload.TryGetProperty("id", out var fidEl) && fidEl.ValueKind == JsonValueKind.String ? (fidEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null) break;
                            var id = !string.IsNullOrWhiteSpace(metaId) ? metaId : fallbackId;
                            if (string.IsNullOrWhiteSpace(id)) break;
                            var item = ResolveServerItem(vm, payload, id);
                            if (item != null)
                            {
                                var itemType = (item.Type ?? "").Trim();
                                var isSeries = string.Equals(itemType, "series", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(itemType, "tv", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals((vm.SelectedServerCatalogType ?? "").Trim(), "series", StringComparison.OrdinalIgnoreCase);
                                if (isSeries)
                                {
                                    if (vm.OpenServerSeriesCommand?.CanExecute(item) == true)
                                        vm.OpenServerSeriesCommand.Execute(item);
                                    else if (vm.PlayItemCommand?.CanExecute(item) == true)
                                        vm.PlayItemCommand.Execute(item);
                                }
                                else
                                {
                                    if (vm.OpenStreamsCommand?.CanExecute(item) == true)
                                        vm.OpenStreamsCommand.Execute(item);
                                    else if (vm.PlayItemCommand?.CanExecute(item) == true)
                                        vm.PlayItemCommand.Execute(item);
                                }

                                // Immediately collapse the WebView when native streams/series panels open,
                                // so the web UI never remains visible underneath the overlay.
                                try { UpdateEmbeddedVisibility(); } catch { }
                            }
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.local.playMovie":
                        try
                        {
                            var movieId = payload.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? (idEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null || string.IsNullOrWhiteSpace(movieId)) break;
                            var item = ResolveLocalMovieItem(vm, movieId);
                            if (item != null && vm.PlayItemCommand?.CanExecute(item) == true)
                                vm.PlayItemCommand.Execute(item);
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.local.playAlbum":
                        try
                        {
                            var albumId = payload.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? (idEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null || string.IsNullOrWhiteSpace(albumId)) break;
                            var album = ResolveLocalAlbum(vm, albumId);
                            if (album == null) break;
                            if (vm.SelectAlbumCommand?.CanExecute(album) == true)
                                vm.SelectAlbumCommand.Execute(album);
                            var firstTrack = album.Tracks.FirstOrDefault();
                            if (firstTrack != null && vm.PlayItemCommand?.CanExecute(firstTrack) == true)
                                vm.PlayItemCommand.Execute(firstTrack);
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.stopSpeech":
                        try
                        {
                            var stopVm = Application.Current.Dispatcher.Invoke(() => TryGetVoiceManager());
                            stopVm?.Stop();
                        }
                        catch { }
                        break;
                    case "servers.playTrailer":
                        try
                        {
                            var metaId = payload.TryGetProperty("metaId", out var idEl) && idEl.ValueKind == JsonValueKind.String ? (idEl.GetString() ?? "").Trim() : "";
                            var fallbackId = payload.TryGetProperty("id", out var fidEl) && fidEl.ValueKind == JsonValueKind.String ? (fidEl.GetString() ?? "").Trim() : "";
                            var shelfKey = payload.TryGetProperty("key", out var skEl) ? (skEl.GetString() ?? "").Trim() : "";
                            var vm = _viewModel;
                            if (vm == null) break;
                            var id = !string.IsNullOrWhiteSpace(metaId) ? metaId : fallbackId;
                            var item = ResolveServerItem(vm, payload, id, shelfKey);
                            if (item == null) break;
                            vm.SetPreviewItem(item);
                            if (vm.PlayPreviewTrailerCommand?.CanExecute(null) == true)
                                vm.PlayPreviewTrailerCommand.Execute(null);
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.selectCatalogItem":
                        try
                        {
                            var metaId = payload.TryGetProperty("metaId", out var idEl) && idEl.ValueKind == JsonValueKind.String ? (idEl.GetString() ?? "").Trim() : "";
                            if (string.IsNullOrWhiteSpace(metaId)) break;
                            var vm = _viewModel;
                            if (vm == null) break;
                            var item = ResolveServerItem(vm, payload, metaId);
                            if (item != null)
                                vm.SetPreviewItem(item);
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.selectItem":
                        try
                        {
                            var shelfKey = payload.TryGetProperty("key", out var skEl) ? (skEl.GetString() ?? "").Trim() : "";
                            var metaId = payload.TryGetProperty("metaId", out var idEl) ? (idEl.GetString() ?? "").Trim() : "";
                            if (string.IsNullOrWhiteSpace(metaId)) break;
                            var vm = _viewModel;
                            if (vm == null) break;
                            var item = ResolveServerItem(vm, payload, metaId, shelfKey);
                            if (item != null)
                                vm.SetPreviewItem(item);
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.openDetail":
                        try
                        {
                            var shelfKey = payload.TryGetProperty("key", out var skEl) ? (skEl.GetString() ?? "").Trim() : "";
                            var metaId = payload.TryGetProperty("metaId", out var idEl) ? (idEl.GetString() ?? "").Trim() : "";
                            if (string.IsNullOrWhiteSpace(metaId)) break;
                            var vm = _viewModel;
                            if (vm == null) break;

                            // Close any existing native panels — React handles the detail view.
                            if (vm.CloseStreamsCommand?.CanExecute(null) == true)
                                vm.CloseStreamsCommand.Execute(null);
                            if (vm.CloseServerSeriesCommand?.CanExecute(null) == true)
                                vm.CloseServerSeriesCommand.Execute(null);
                            if (vm.CloseDetailsCommand?.CanExecute(null) == true)
                                vm.CloseDetailsCommand.Execute(null);

                            var item = ResolveServerItem(vm, payload, metaId, shelfKey);

                            if (item != null)
                            {
                                vm.SetPreviewItem(item);

                                var itemType = (item.Type ?? "").Trim();
                                var isSeries = string.Equals(itemType, "series", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(itemType, "tv", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals((vm.SelectedServerCatalogType ?? "").Trim(), "series", StringComparison.OrdinalIgnoreCase);

                                if (isSeries)
                                {
                                    // Load episodes without opening native series panel.
                                    vm.LoadServerSeriesForBridge(item);
                                }
                                else
                                {
                                    // Load streams without opening native streams panel.
                                    vm.LoadStreamsForBridge(item);
                                }
                            }

                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.selectEpisode":
                        try
                        {
                            var vm = _viewModel;
                            if (vm == null) break;
                            var episodeSeason = payload.TryGetProperty("season", out var sEl) && sEl.ValueKind == JsonValueKind.Number ? sEl.GetInt32() : -1;
                            var episodeNum = payload.TryGetProperty("episode", out var eEl) && eEl.ValueKind == JsonValueKind.Number ? eEl.GetInt32() : -1;
                            var episodeId = payload.TryGetProperty("id", out var eidEl) && eidEl.ValueKind == JsonValueKind.String ? (eidEl.GetString() ?? "").Trim() : "";

                            MediaItem? episode = null;
                            if (!string.IsNullOrWhiteSpace(episodeId))
                                episode = vm.VisibleServerSeriesEpisodes.FirstOrDefault(e => string.Equals((e.MetaId ?? e.FilePath ?? "").Trim(), episodeId, StringComparison.OrdinalIgnoreCase));

                            if (episode == null && episodeSeason > 0 && episodeNum > 0)
                                episode = vm.VisibleServerSeriesEpisodes.FirstOrDefault(e => e.TrackNumber == episodeSeason && e.DiscNumber == episodeNum);

                            if (episode != null)
                            {
                                // Use bridge-only load (no native panel).
                                vm.LoadStreamsForBridge(episode);
                            }

                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                    case "servers.selectSeason":
                        try
                        {
                            var vm = _viewModel;
                            if (vm == null) break;
                            var seasonNum = payload.TryGetProperty("season", out var snEl) && snEl.ValueKind == JsonValueKind.Number ? snEl.GetInt32() : 0;
                            var match = vm.ServerSeriesSeasons.FirstOrDefault(s => s.SeasonNumber == seasonNum);
                            if (match != null)
                                vm.SelectedServerSeriesSeason = match;
                            SchedulePostState();
                        }
                        catch
                        {
                        }
                        break;
                }
            }
            catch
            {
            }
        }

        private async Task PostDiscoveryDataFromServersAsync()
        {
            try
            {
                try
                {
                    var beginLog = "[DiscoveryHeroData] servers.discovery.request begin";
                    Console.WriteLine(beginLog);
                    AtlasAI.Core.AppLogger.LogInfo(beginLog);
                }
                catch
                {
                }

                var vm = _viewModel;
                var traktClientId = "";
                var traktToken = "";

                if (vm != null)
                {
                    traktClientId = vm.TraktClientId;
                    traktToken = vm.TraktToken;
                }

                var data = await _discoveryService.GetDiscoveryDataAsync(traktClientId, traktToken, CancellationToken.None);
                var message = JsonSerializer.Serialize(new { type = "discovery.data", payload = data }, DiscoveryJsonOptions);

                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        ServersFigmaWebView?.CoreWebView2?.PostWebMessageAsJson(message);
                    }
                    catch
                    {
                    }
                });

                try
                {
                    var successLog = $"[DiscoveryHeroData] servers.discovery.post success=true trending={data?.Trending?.Count ?? 0} upcoming={data?.Upcoming?.Count ?? 0} news={data?.News?.Count ?? 0} featured={(data?.Featured != null).ToString().ToLowerInvariant()}";
                    Console.WriteLine(successLog);
                    AtlasAI.Core.AppLogger.LogInfo(successLog);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var failedLog = $"[DiscoveryHeroData] servers.discovery.post failed error={ex.Message}";
                    Console.WriteLine(failedLog);
                    AtlasAI.Core.AppLogger.LogError(failedLog, ex);
                }
                catch
                {
                }
            }
        }

        private async Task HandleAiQueryAsync(string id, string text, string requestedTitle = "")
        {
            try
            {
                var vm = _viewModel;
                if (vm == null) return;

                var resolvedShelfTitle = string.IsNullOrWhiteSpace(requestedTitle) ? "AI Shelf" : requestedTitle.Trim();

                async Task PostAiResultAsync(string content, object recommendations, bool speak = false)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (ServersFigmaWebView?.CoreWebView2 == null) return;
                            var msg = JsonSerializer.Serialize(new
                            {
                                type = "servers.ai.result",
                                payload = new { id, shelfTitle = resolvedShelfTitle, content, recommendations }
                            });
                            ServersFigmaWebView.CoreWebView2.PostWebMessageAsJson(msg);
                        }
                        catch
                        {
                        }
                    });

                    if (speak)
                        await SpeakMediaResultAsync(content).ConfigureAwait(false);
                }

                // Handle single-title overview requests from "Ask Atlas" button.
                // Speech is triggered only after explicit frontend confirmation.
                if (id.Contains("-overview", StringComparison.OrdinalIgnoreCase) || id.Contains("-next-watch", StringComparison.OrdinalIgnoreCase))
                {
                    var analysis = await BuildTitleAnalysisAsync(vm, text).ConfigureAwait(false);
                    await PostAiResultAsync(analysis, Array.Empty<object>(), speak: false);
                    return;
                }

                var tmdbApiKey = (AtlasAI.Core.IntegrationKeyStore.GetDecrypted("tmdb") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(tmdbApiKey))
                {
                    await PostAiResultAsync("TMDB API key is missing. Add your TMDB key in Atlas settings to use Atlas AI shelves and recommendations.", Array.Empty<object>());
                    return;
                }

                if (!string.IsNullOrWhiteSpace(tmdbApiKey))
                {
                    try
                    {
                        static bool ContainsAny(string value, params string[] needles)
                        {
                            foreach (var needle in needles)
                            {
                                if (!string.IsNullOrWhiteSpace(needle) && value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                                    return true;
                            }

                            return false;
                        }

                        var agent = new MediaIntelligenceAgent();
                        var isCustomShelfRequest = id.StartsWith("ai-shelf-", StringComparison.OrdinalIgnoreCase);

                        if (isCustomShelfRequest)
                        {
                            var discoveryItems = await agent.DiscoverByQueryAsync(tmdbApiKey, text, 120, CancellationToken.None).ConfigureAwait(false);
                            var discoveryRecommendations = discoveryItems
                                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Title))
                                .GroupBy(item => MediaIntelligenceAgent.BuildDedupeKey(item), StringComparer.OrdinalIgnoreCase)
                                .Select(group => group.First())
                                .Take(120)
                                .Select(item => (object)new
                                {
                                    shelfKey = "discovery",
                                    id = item.Id,
                                    metaId = !string.IsNullOrWhiteSpace(item.ImdbId)
                                        ? item.ImdbId
                                        : item.TmdbId.HasValue && item.TmdbId.Value > 0
                                            ? $"tmdb:{item.TmdbId.Value}"
                                            : item.Id,
                                    tmdbId = item.TmdbId,
                                    imdbId = item.ImdbId,
                                    title = item.Title,
                                    coverUrl = item.Poster,
                                    backdropUrl = item.Backdrop,
                                    trailerUrl = item.Trailer,
                                    year = item.ReleaseDate?.Year ?? 0,
                                    rating = Math.Max(item.Ratings?.Tmdb ?? 0, item.Ratings?.Trakt ?? 0),
                                    genres = item.Genres,
                                    aiScore = Math.Round(item.AiScore, 1),
                                    summary = item.Overview,
                                    overview = item.Overview,
                                    description = item.Overview,
                                    type = item.Kind == MediaKind.Series ? "series" : "movie",
                                    cast = item.Cast,
                                    director = item.Directors,
                                    runtimeMinutes = item.RuntimeMinutes,
                                    popularity = item.Ratings?.PopularityRaw ?? 0,
                                    ratings = new Dictionary<string, double>(item.Ratings?.Signals ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase)
                                    {
                                        ["tmdb"] = item.Ratings?.Tmdb ?? 0,
                                        ["trakt"] = item.Ratings?.Trakt ?? 0,
                                        ["critic"] = item.Ratings?.Critic ?? 0,
                                        ["audience"] = item.Ratings?.Audience ?? 0
                                    },
                                    rpdbRatings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        ["tmdb"] = (item.Ratings?.Tmdb ?? 0).ToString("0.0", CultureInfo.InvariantCulture),
                                        ["trakt"] = (item.Ratings?.Trakt ?? 0).ToString("0.0", CultureInfo.InvariantCulture),
                                        ["critic"] = (item.Ratings?.Critic ?? 0).ToString("0.0", CultureInfo.InvariantCulture),
                                        ["audience"] = (item.Ratings?.Audience ?? 0).ToString("0.0", CultureInfo.InvariantCulture)
                                    }
                                })
                                .ToList();

                            if (discoveryRecommendations.Count > 0)
                            {
                                await PostAiResultAsync($"Built \"{resolvedShelfTitle}\" from TMDB discovery with {discoveryRecommendations.Count} matching titles.", discoveryRecommendations);
                                return;
                            }

                            await PostAiResultAsync("I couldn't build that shelf from TMDB discovery. Try a broader genre, a clearer franchise, or a narrower year range.", Array.Empty<object>());
                            return;
                        }

                        var intelligence = await agent.BuildHomeAsync(tmdbApiKey, vm.TraktClientId, vm.TraktToken, text, 72, CancellationToken.None).ConfigureAwait(false);
                        var lowerText = (text ?? string.Empty).Trim();
                        var sectionKey = ContainsAny(lowerText, "still watching", "continue watching") ? "still-watching"
                            : ContainsAny(lowerText, "latest movies", "new movies") ? "latest-movies"
                            : ContainsAny(lowerText, "latest tv", "latest series", "new tv", "new series") ? "latest-tv"
                            : ContainsAny(lowerText, "marvel") ? "popular-marvel"
                            : ContainsAny(lowerText, "dc") ? "popular-dc"
                            : ContainsAny(lowerText, "best rated", "all time", "top rated") ? "best-rated-movies-of-all-time"
                            : ContainsAny(lowerText, "kids", "family", "children") ? "kids-choice"
                            : string.Empty;

                        var sections = intelligence?.Sections ?? new List<MediaSection>();
                        if (string.IsNullOrWhiteSpace(requestedTitle))
                            resolvedShelfTitle = sections.FirstOrDefault(section => string.Equals(section.Key, sectionKey, StringComparison.OrdinalIgnoreCase))?.Title ?? resolvedShelfTitle;

                        IEnumerable<(string sectionKey, EnrichedMediaObject item)> intelligentMatches;
                        if (!string.IsNullOrWhiteSpace(sectionKey))
                        {
                            intelligentMatches = sections
                                .Where(section => string.Equals(section.Key, sectionKey, StringComparison.OrdinalIgnoreCase))
                                .SelectMany(section => (section.Items ?? new List<EnrichedMediaObject>()).Select(item => (section.Key, item)));
                        }
                        else
                        {
                            var tokens = (text ?? string.Empty)
                                .ToLowerInvariant()
                                .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ':', ';', '!', '?', '-', '_', '/', '\\', '(', ')', '[', ']', '{', '}', '"' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(token => token.Length >= 3)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            static int CountMatches(EnrichedMediaObject item, List<string> tokens)
                            {
                                if (tokens.Count == 0)
                                    return 0;

                                var haystack = string.Join(" ", new[]
                                {
                                    item.Title ?? string.Empty,
                                    item.Overview ?? string.Empty,
                                    string.Join(" ", item.Genres ?? new List<string>()),
                                    string.Join(" ", item.Cast ?? new List<string>()),
                                    string.Join(" ", item.Directors ?? new List<string>())
                                }).ToLowerInvariant();

                                return tokens.Count(token => haystack.Contains(token, StringComparison.Ordinal));
                            }

                            intelligentMatches = sections
                                .SelectMany(section => (section.Items ?? new List<EnrichedMediaObject>())
                                    .Select(item => new { section.Key, Item = item, Matches = CountMatches(item, tokens) }))
                                .OrderByDescending(entry => entry.Matches)
                                .ThenByDescending(entry => entry.Item.AiScore)
                                .ThenByDescending(entry => entry.Item.Ratings?.PopularityRaw ?? 0)
                                .ThenByDescending(entry => entry.Item.ReleaseDate)
                                .Select(entry => (entry.Key, entry.Item));

                            // If token-matching the pre-built home shelves yields weak results,
                            // use TMDB discovery to search for what the user actually asked for.
                            var preBuiltTop = intelligentMatches
                                .Where(e => e.item != null && !string.IsNullOrWhiteSpace(e.item.Title))
                                .Take(5)
                                .ToList();
                            var bestMatchCount = preBuiltTop.Count > 0
                                ? preBuiltTop.Max(e => CountMatches(e.item, tokens))
                                : 0;

                            if (bestMatchCount < 2)
                            {
                                try
                                {
                                    var discoveryItems = await agent.DiscoverByQueryAsync(tmdbApiKey, text, 40, CancellationToken.None).ConfigureAwait(false);
                                    if (discoveryItems.Count > 0)
                                    {
                                        intelligentMatches = discoveryItems
                                            .Select(item => ("discovery", item));
                                    }
                                }
                                catch { }
                            }
                        }

                        var recommendations = intelligentMatches
                            .Where(entry => entry.item != null && !string.IsNullOrWhiteSpace(entry.item.Title))
                            .GroupBy(entry => MediaIntelligenceAgent.BuildDedupeKey(entry.item), StringComparer.OrdinalIgnoreCase)
                            .Select(group => group.First())
                            .Take(30)
                            .Select(entry => (object)new
                            {
                                shelfKey = entry.sectionKey,
                                id = entry.item.Id,
                                metaId = !string.IsNullOrWhiteSpace(entry.item.ImdbId)
                                    ? entry.item.ImdbId
                                    : entry.item.TmdbId.HasValue && entry.item.TmdbId.Value > 0
                                        ? $"tmdb:{entry.item.TmdbId.Value}"
                                        : entry.item.Id,
                                tmdbId = entry.item.TmdbId,
                                imdbId = entry.item.ImdbId,
                                title = entry.item.Title,
                                coverUrl = entry.item.Poster,
                                backdropUrl = entry.item.Backdrop,
                                trailerUrl = entry.item.Trailer,
                                year = entry.item.ReleaseDate?.Year ?? 0,
                                rating = Math.Max(entry.item.Ratings?.Tmdb ?? 0, entry.item.Ratings?.Trakt ?? 0),
                                genres = entry.item.Genres,
                                aiScore = Math.Round(entry.item.AiScore, 1),
                                summary = entry.item.Overview,
                                overview = entry.item.Overview,
                                description = entry.item.Overview,
                                type = entry.item.Kind == MediaKind.Series ? "series" : "movie",
                                cast = entry.item.Cast,
                                director = entry.item.Directors,
                                runtimeMinutes = entry.item.RuntimeMinutes,
                                popularity = entry.item.Ratings?.PopularityRaw ?? 0,
                                ratings = new Dictionary<string, double>(entry.item.Ratings?.Signals ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase)
                                {
                                    ["tmdb"] = entry.item.Ratings?.Tmdb ?? 0,
                                    ["trakt"] = entry.item.Ratings?.Trakt ?? 0,
                                    ["critic"] = entry.item.Ratings?.Critic ?? 0,
                                    ["audience"] = entry.item.Ratings?.Audience ?? 0
                                },
                                rpdbRatings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["tmdb"] = (entry.item.Ratings?.Tmdb ?? 0).ToString("0.0", CultureInfo.InvariantCulture),
                                    ["trakt"] = (entry.item.Ratings?.Trakt ?? 0).ToString("0.0", CultureInfo.InvariantCulture),
                                    ["critic"] = (entry.item.Ratings?.Critic ?? 0).ToString("0.0", CultureInfo.InvariantCulture),
                                    ["audience"] = (entry.item.Ratings?.Audience ?? 0).ToString("0.0", CultureInfo.InvariantCulture)
                                }
                            })
                            .ToList();

                        if (recommendations.Count > 0)
                        {
                            var intelligentResponseText = string.IsNullOrWhiteSpace(sectionKey)
                                ? "Here are picks matching your request, built from TMDB discovery and your metadata sources."
                                : $"Here is the {sections.FirstOrDefault(section => string.Equals(section.Key, sectionKey, StringComparison.OrdinalIgnoreCase))?.Title ?? "requested"} shelf built from live metadata sources.";

                            await PostAiResultAsync(intelligentResponseText, recommendations);
                            return;
                        }

                        var emptyIntelligentResponse = string.IsNullOrWhiteSpace(sectionKey)
                            ? "I couldn't find matching titles. Try a genre (e.g. horror, comedy), franchise (e.g. Marvel), or era (e.g. 90s action movies)."
                            : $"I couldn't find enough TMDB-backed titles for the requested {sections.FirstOrDefault(section => string.Equals(section.Key, sectionKey, StringComparison.OrdinalIgnoreCase))?.Title ?? "shelf"}. Try widening the request or choosing another angle.";
                        await PostAiResultAsync(emptyIntelligentResponse, Array.Empty<object>());
                        return;
                    }
                    catch
                    {
                        await PostAiResultAsync("TMDB-backed recommendations are temporarily unavailable. Check your metadata settings and try again.", Array.Empty<object>());
                        return;
                    }
                }

                List<(string shelfKey, MediaItem item)> candidates = new();
                try
                {
                    candidates = vm.ServerShelves
                        .Where(s => s != null)
                        .SelectMany(s =>
                        {
                            var key = $"{(s!.ContentType ?? "").Trim()}::{(s.CatalogId ?? "").Trim()}";
                            return (s.Items ?? new System.Collections.ObjectModel.ObservableCollection<MediaItem>())
                                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Title))
                                .Select(i => (shelfKey: key, item: i));
                        })
                        .ToList();
                }
                catch
                {
                    candidates = new List<(string shelfKey, MediaItem item)>();
                }

                static double? LocalScore(MediaItem i)
                {
                    var r = i.Rating;
                    if (r <= 0) return null;
                    return Math.Round(Math.Clamp(r, 0, 10), 1);
                }

                var unique = candidates
                    .GroupBy(x => (x.item.MetaId ?? x.item.FilePath ?? x.item.Title ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Where(x => x.item != null && !string.IsNullOrWhiteSpace(x.item.Title))
                    .ToList();

                var top = unique
                    .OrderByDescending(x => LocalScore(x.item) ?? -1)
                    .ThenByDescending(x => x.item.Year)
                    .Take(60)
                    .ToList();

                var remaining = unique
                    .Except(top)
                    .Where(x => x.item != null && !string.IsNullOrWhiteSpace(x.item.Title))
                    .ToList();

                try
                {
                    var rnd = new Random(unchecked(Environment.TickCount ^ text.GetHashCode()));
                    for (var i = remaining.Count - 1; i > 0; i--)
                    {
                        var j = rnd.Next(i + 1);
                        (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
                    }
                }
                catch
                {
                }

                var seed = top
                    .Concat(remaining.Take(180))
                    .Select(x => new
                    {
                        shelfKey = x.shelfKey,
                        metaId = (x.item.MetaId ?? "").Trim(),
                        title = (x.item.Title ?? "").Trim(),
                        year = x.item.Year,
                        rating = x.item.Rating,
                        genres = x.item.Genres
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.title))
                    .ToList();

                var aiText = "";
                try
                {
                    var messages = new List<object>
                    {
                        new { role = "system", content = "You are Atlas AI. Return only valid JSON." },
                        new
                        {
                            role = "user",
                            content =
                                "User request:\n" + text.Trim() + "\n\n" +
                                "Choose up to 18 items from this list.\n" +
                                "- Avoid duplicates and avoid obvious repeats.\n" +
                                "- Prefer variety: mix classics + new, genres, and moods.\n" +
                                "Return JSON with shape:\n" +
                                "{ \"content\": \"short explanation\", \"recommendations\": [ { \"shelfKey\": \"...\", \"metaId\": \"...\" } ] }\n\n" +
                                "Items:\n" + JsonSerializer.Serialize(seed)
                        }
                    };

                    var gemini = AtlasAI.AI.AIManager.GetProvider(AtlasAI.AI.AIProviderType.Gemini);
                    if (gemini != null && gemini.IsConfigured)
                    {
                        var resp = await gemini.SendMessageAsync(messages, "", 450);
                        if (resp != null && resp.Success)
                            aiText = (resp.Content ?? "").Trim();
                    }
                    else
                    {
                        var resp = await AtlasAI.AI.AIManager.SendMessageAsync("ServersAI", messages, maxTokens: 450);
                        if (resp != null && resp.Success)
                            aiText = (resp.Content ?? "").Trim();
                    }
                }
                catch
                {
                    aiText = "";
                }

                string responseText;
                List<object> results = new();
                if (!string.IsNullOrWhiteSpace(aiText)) { try { aiText = aiText.Trim(); if (aiText.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) { aiText = aiText.Substring(7).Trim(); } if (aiText.EndsWith("```")) { aiText = aiText.Substring(0, aiText.Length - 3).Trim(); } using var doc = JsonDocument.Parse(aiText);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            responseText = doc.RootElement.TryGetProperty("content", out var cEl) ? (cEl.GetString() ?? "").Trim() : "";
                            if (string.IsNullOrWhiteSpace(responseText))
                                responseText = "Here are picks from your connected server shelves.";

                            if (doc.RootElement.TryGetProperty("recommendations", out var rEl) && rEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var rec in rEl.EnumerateArray())
                                {
                                    if (rec.ValueKind != JsonValueKind.Object) continue;
                                    var shelfKey = rec.TryGetProperty("shelfKey", out var sk) ? (sk.GetString() ?? "").Trim() : "";
                                    var metaId = rec.TryGetProperty("metaId", out var mi) ? (mi.GetString() ?? "").Trim() : "";
                                    if (string.IsNullOrWhiteSpace(metaId)) continue;
                                    var match = unique.FirstOrDefault(x =>
                                        string.Equals((x.item.MetaId ?? "").Trim(), metaId, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals((x.item.FilePath ?? "").Trim(), metaId, StringComparison.OrdinalIgnoreCase));
                                    if (match.item == null) continue;
                                    results.Add(new
                                    {
                                        shelfKey = string.IsNullOrWhiteSpace(shelfKey) ? match.shelfKey : shelfKey,
                                        metaId = (match.item.MetaId ?? "").Trim(),
                                        title = (match.item.Title ?? "").Trim(),
                                        coverUrl = (match.item.CoverUrl ?? "").Trim(),
                                        year = match.item.Year,
                                        rating = match.item.Rating,
                                        genres = match.item.Genres,
                                        aiScore = LocalScore(match.item)
                                    });
                                    if (results.Count >= 18) break;
                                }
                            }
                        }
                        else
                        {
                            responseText = "";
                        }
                    }
                    catch
                    {
                        responseText = "";
                    }
                }
                else
                {
                    responseText = "";
                }

                if (results.Count == 0)
                {
                    var tokens = (text ?? "")
                        .ToLowerInvariant()
                        .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ':', ';', '!', '?', '-', '_', '/', '\\', '(', ')', '[', ']', '{', '}', '"' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(t => t.Length >= 3)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    static int TokenMatchCount(string haystack, string compactHaystack, List<string> tokens)
                    {
                        if (tokens.Count == 0) return 0;
                        var count = 0;
                        foreach (var t in tokens)
                        {
                            if (haystack.Contains(t, StringComparison.Ordinal)) { count++; continue; }
                            if (t.Length >= 4 && compactHaystack.Contains(t, StringComparison.Ordinal)) { count++; continue; }
                        }
                        return count;
                    }

                    var ranked = unique
                        .Select(x =>
                        {
                            var title = (x.item.Title ?? "").Trim();
                            var lower = title.ToLowerInvariant();
                            var compact = lower.Replace(" ", "");
                            var matches = TokenMatchCount(lower, compact, tokens);
                            var score = (LocalScore(x.item) ?? 0) + (matches * 0.25);
                            return new { x.shelfKey, x.item, matches, score };
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.item.Title))
                        .OrderByDescending(x => x.matches)
                        .ThenByDescending(x => x.score)
                        .ThenByDescending(x => x.item.Year)
                        .Take(18)
                        .Select(x => (object)new
                        {
                            shelfKey = x.shelfKey,
                            metaId = (x.item.MetaId ?? "").Trim(),
                            title = (x.item.Title ?? "").Trim(),
                            coverUrl = (x.item.CoverUrl ?? "").Trim(),
                            year = x.item.Year,
                            rating = x.item.Rating,
                            genres = x.item.Genres,
                            aiScore = LocalScore(x.item)
                        })
                        .ToList();

                    results = ranked;
                    responseText =
                        results.Count == 0
                            ? "I couldn’t find anything in the current server shelves. Try opening a shelf first or searching a different title."
                            : "Here are picks from your connected server shelves based on your request.";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(responseText))
                        responseText = "Here are picks from your connected server shelves.";
                }

                await PostAiResultAsync(responseText, results);
            }
            catch
            {
            }
        }

        private async Task<string> BuildTitleAnalysisAsync(MediaCentreViewModel vm, string requestText)
        {
            var item = vm.PreviewItem ?? vm.DetailsItem;
            var overview = (item?.Overview ?? item?.Metadata ?? vm.PreviewOverview ?? vm.DetailsOverview ?? "").Trim();
            if (string.IsNullOrWhiteSpace(overview) || overview.StartsWith("Loading", StringComparison.OrdinalIgnoreCase))
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    overview = await Dispatcher.InvokeAsync(() =>
                    {
                        var current = vm.PreviewItem ?? vm.DetailsItem;
                        return (current?.Overview ?? current?.Metadata ?? vm.PreviewOverview ?? vm.DetailsOverview ?? "").Trim();
                    });
                    if (!string.IsNullOrWhiteSpace(overview) && !overview.StartsWith("Loading", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }

            var title = (item?.Title ?? "This title").Trim();
            var genres = item?.Genres?.Where(g => !string.IsNullOrWhiteSpace(g)).ToArray() ?? Array.Empty<string>();
            var cast = vm.PreviewCast?.Where(c => !string.IsNullOrWhiteSpace(c)).Take(8).ToArray() ?? Array.Empty<string>();
            var playableSources = vm.StreamSources
                .Where(s => s != null && !s.IsInfoOnly && !string.IsNullOrWhiteSpace(s.UrlOrPath))
                .Take(8)
                .Select(s => new
                {
                    provider = (s.ProviderName ?? "").Trim(),
                    name = (s.Name ?? "").Trim(),
                    quality = (s.Quality ?? "").Trim(),
                    size = (s.SizeText ?? "").Trim(),
                    seeders = (s.SeedersText ?? "").Trim()
                })
                .ToArray();
            var infoCards = vm.StreamSources
                .Where(s => s != null && (s.IsInfoOnly || string.IsNullOrWhiteSpace(s.UrlOrPath)))
                .Take(6)
                .Select(s => new
                {
                    provider = (s.ProviderName ?? "").Trim(),
                    name = (s.Name ?? "").Trim(),
                    info = GetAddonInfoSummary(s.Metadata)
                })
                .ToArray();

            // ── Fetch real reviews & ratings from TMDB + OMDb ──
            string onlineReviewsBlock = "";
            try
            {
                var tmdbApiKey = (AtlasAI.Core.IntegrationKeyStore.GetDecrypted("tmdb") ?? "").Trim();
                var imdbId = (item?.ImdbId ?? "").Trim();
                var tmdbId = item?.TmdbId ?? 0;
                var mediaType = string.Equals(item?.Type ?? "", "series", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(item?.Type ?? "", "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";

                // If we have a TMDB key but no TMDB ID, try to find it via search
                if (!string.IsNullOrWhiteSpace(tmdbApiKey) && tmdbId <= 0 && !string.IsNullOrWhiteSpace(title))
                {
                    try
                    {
                        var tmdb = new MediaMetadata.TmdbClient();
                        if (mediaType == "tv")
                        {
                            var tvResults = await tmdb.SearchTvAsync(tmdbApiKey, title, CancellationToken.None).ConfigureAwait(false);
                            if (tvResults.Count > 0) tmdbId = tvResults[0].Id;
                        }
                        else
                        {
                            var movieResults = await tmdb.SearchMovieAsync(tmdbApiKey, title, CancellationToken.None).ConfigureAwait(false);
                            if (movieResults.Count > 0) tmdbId = movieResults[0].Id;
                        }
                    }
                    catch { }
                }

                // If we have TMDB ID but no IMDB ID, resolve it
                if (!string.IsNullOrWhiteSpace(tmdbApiKey) && tmdbId > 0 && string.IsNullOrWhiteSpace(imdbId))
                {
                    try
                    {
                        var tmdb = new MediaMetadata.TmdbClient();
                        var (resolvedImdb, _) = await tmdb.TryResolveImdbIdFromTmdbIdAsync(tmdbApiKey, tmdbId, mediaType == "tv", CancellationToken.None).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(resolvedImdb)) imdbId = resolvedImdb!;
                    }
                    catch { }
                }

                // Fetch TMDB reviews + OMDb ratings in parallel
                Task<MediaMetadata.TmdbClient.TmdbReviewData?> tmdbReviewTask = Task.FromResult<MediaMetadata.TmdbClient.TmdbReviewData?>(null);
                Task<Dictionary<string, string>> omdbTask = Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(tmdbApiKey) && tmdbId > 0)
                    tmdbReviewTask = new MediaMetadata.TmdbClient().GetReviewDataAsync(tmdbApiKey, mediaType, tmdbId, CancellationToken.None);

                if (!string.IsNullOrWhiteSpace(imdbId))
                    omdbTask = new MediaMetadata.TmdbClient().GetOmdbRatingsAsync(imdbId, CancellationToken.None);

                await Task.WhenAll(tmdbReviewTask, omdbTask).ConfigureAwait(false);

                var tmdbReviews = tmdbReviewTask.Result;
                var omdbRatings = omdbTask.Result;

                var reviewSb = new StringBuilder();

                // Add aggregated ratings
                if (omdbRatings.Count > 0 || (tmdbReviews != null && tmdbReviews.VoteCount > 0))
                {
                    reviewSb.AppendLine("REAL CRITIC & AUDIENCE RATINGS:");
                    if (tmdbReviews != null && tmdbReviews.VoteCount > 0)
                        reviewSb.AppendLine($"- TMDB: {tmdbReviews.VoteAverage:F1}/10 ({tmdbReviews.VoteCount:N0} votes)");
                    foreach (var kv in omdbRatings)
                    {
                        if (string.Equals(kv.Key, "Awards", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(kv.Key, "BoxOffice", StringComparison.OrdinalIgnoreCase))
                            continue;
                        reviewSb.AppendLine($"- {kv.Key}: {kv.Value}");
                    }
                    if (omdbRatings.TryGetValue("Awards", out var awards))
                        reviewSb.AppendLine($"- Awards: {awards}");
                    if (omdbRatings.TryGetValue("BoxOffice", out var boxOffice))
                        reviewSb.AppendLine($"- Box Office: {boxOffice}");
                    reviewSb.AppendLine();
                }

                // Add actual user reviews from TMDB
                if (tmdbReviews != null && tmdbReviews.Reviews.Count > 0)
                {
                    reviewSb.AppendLine("REAL USER REVIEWS FROM TMDB:");
                    foreach (var review in tmdbReviews.Reviews.Take(3))
                    {
                        var ratingStr = review.Rating.HasValue ? $" ({review.Rating.Value:F0}/10)" : "";
                        reviewSb.AppendLine($"- {review.Author ?? "Anonymous"}{ratingStr}: {review.Content}");
                    }
                    reviewSb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(tmdbReviews?.Tagline))
                    reviewSb.AppendLine($"Tagline: \"{tmdbReviews!.Tagline}\"");

                onlineReviewsBlock = reviewSb.ToString().Trim();
            }
            catch { }

            try
            {
                var provider = AtlasAI.AI.AIManager.GetActiveProviderInstance();
                if (provider != null && provider.IsConfigured)
                {
                    var contextBlock = "Title context:\n" + JsonSerializer.Serialize(new
                    {
                        title,
                        type = (item?.Type ?? "").Trim(),
                        year = item?.Year ?? 0,
                        rating = item?.Rating ?? 0,
                        runtimeMinutes = item?.RuntimeMinutes ?? 0,
                        releaseDate = item?.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                        genres,
                        overview,
                        cast,
                        streamsStatus = (vm.StreamsStatusText ?? "").Trim(),
                        playableSources,
                        addonDetails = infoCards
                    });

                    if (!string.IsNullOrWhiteSpace(onlineReviewsBlock))
                        contextBlock += "\n\n" + onlineReviewsBlock;

                    var messages = new List<object>
                    {
                        new
                        {
                            role = "system",
                            content = BuildTitleAnalysisSystemPrompt()
                        },
                        new
                        {
                            role = "user",
                            content =
                                "User request:\n" + (string.IsNullOrWhiteSpace(requestText) ? "Tell me everything useful about this title." : requestText.Trim()) + "\n\n" +
                                contextBlock
                        }
                    };

                    var response = await provider.SendMessageAsync(messages, model: "", maxTokens: 1000, ct: CancellationToken.None).ConfigureAwait(false);
                    var content = NormalizeAnalysisResponse(response?.Content ?? "");
                    if (response != null && response.Success && !string.IsNullOrWhiteSpace(content) && !LooksLikeOverviewEcho(content, overview) && !LooksLikeBrokenTitleAnalysis(content))
                        return content;
                }
            }
            catch
            {
            }

            var playableSourceLines = playableSources
                .Select(source => string.Join(" | ", new[]
                {
                    source.provider,
                    source.quality,
                    source.size,
                    source.seeders
                }.Where(value => !string.IsNullOrWhiteSpace(value))))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            var infoCardLines = infoCards
                .Select(card => string.Join(": ", new[]
                {
                    card.provider,
                    card.info
                }.Where(value => !string.IsNullOrWhiteSpace(value))))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            return BuildDeterministicTitleAnalysis(title, item, overview, cast, playableSources.Length, infoCards.Length, vm.StreamsStatusText, playableSourceLines, infoCardLines);
        }

        private static string NormalizeAnalysisResponse(string value)
        {
            var text = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "";

            text = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace("**", "", StringComparison.Ordinal)
                .Replace("__", "", StringComparison.Ordinal)
                .Trim();

            while (text.Contains("\n\n\n", StringComparison.Ordinal))
                text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);

            return text.Trim();
        }

        private static bool LooksLikeBrokenTitleAnalysis(string content)
        {
            var text = (content ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var lower = text.ToLowerInvariant();
            if (text.Length < 80)
                return true;
            var allowProfanity = (SettingsStore.Current.UnfilteredChaosIntensity >= 3) && SettingsStore.Current.UnfilteredAllowProfanity;
            // Only block casual openings in clean mode — in profanity mode the sweary critic naturally opens with these
            if (!allowProfanity &&
                (lower.StartsWith("mate", StringComparison.Ordinal) ||
                 lower.StartsWith("right", StringComparison.Ordinal) ||
                 lower.StartsWith("hello", StringComparison.Ordinal)))
                return true;
            if (lower.Contains("command center", StringComparison.Ordinal) ||
                lower.Contains("command centre", StringComparison.Ordinal) ||
                lower.Contains("assistant", StringComparison.Ordinal) ||
                (!allowProfanity && lower.Contains("fuck", StringComparison.Ordinal)) ||
                (!allowProfanity && lower.Contains("shit", StringComparison.Ordinal)) ||
                text.Contains('*', StringComparison.Ordinal))
                return true;

            var tail = text[^1];
            return tail != '.' && tail != '!' && tail != '?';
        }

        private static bool LooksLikeOverviewEcho(string content, string overview)
        {
            var normalizedContent = NormalizeAnalysisText(content);
            var normalizedOverview = NormalizeAnalysisText(overview);
            if (string.IsNullOrWhiteSpace(normalizedContent) || string.IsNullOrWhiteSpace(normalizedOverview))
                return false;

            if (string.Equals(normalizedContent, normalizedOverview, StringComparison.OrdinalIgnoreCase))
                return true;

            return normalizedContent.Length <= normalizedOverview.Length + 40 &&
                   normalizedContent.Contains(normalizedOverview, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAnalysisText(string value)
        {
            var text = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            while (text.Contains("  ", StringComparison.Ordinal))
                text = text.Replace("  ", " ", StringComparison.Ordinal);
            return text.Trim();
        }

        private static string GetAddonInfoSummary(IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
                return "";

            var preferredKeys = new[] { "description", "details", "info", "message", "note" };
            foreach (var key in preferredKeys)
            {
                if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            foreach (var pair in metadata)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                    return pair.Value.Trim();
            }

            return "";
        }

        private static string BuildDeterministicTitleAnalysis(string title, MediaItem? item, string overview, IReadOnlyList<string> cast, int playableCount, int infoCount, string? streamsStatusText, IReadOnlyList<string> playableSourceLines, IReadOnlyList<string> infoCardLines)
        {
            var allowStrongLanguage = SettingsStore.Current.UnfilteredChaosIntensity >= 3 && SettingsStore.Current.UnfilteredAllowProfanity;
            var sb = new StringBuilder();
            var descriptorParts = new List<string>();
            var year = item?.Year ?? 0;
            if (year > 0)
                descriptorParts.Add(year.ToString(CultureInfo.InvariantCulture));

            var type = (item?.Type ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(type))
                descriptorParts.Add(string.Equals(type, "series", StringComparison.OrdinalIgnoreCase) ? "series" : type.ToLowerInvariant());

            var genres = item?.Genres?.Where(g => !string.IsNullOrWhiteSpace(g)).Take(4).ToArray() ?? Array.Empty<string>();
            if (genres.Length > 0)
                descriptorParts.Add(string.Join(", ", genres));

            sb.Append(title);
            if (descriptorParts.Count > 0)
                sb.Append(" is a ").Append(string.Join(" - ", descriptorParts)).Append('.');
            sb.AppendLine();
            sb.AppendLine();

            sb.AppendLine("Verdict");
            var rating = item?.Rating ?? 0;
            if (rating >= 8.2)
                sb.AppendLine(allowStrongLanguage ? "Right, this is genuinely fucking brilliant. Properly class. If the premise even slightly grabs you, get it on — you won't regret it." : "This is strong enough to recommend without hedging if the premise is your thing.");
            else if (rating >= 6.8)
                sb.AppendLine(allowStrongLanguage ? "It's decent. Not gonna change your life but it's a solid bloody watch. Grab a beer and enjoy it for what it is." : "This lands as a solid watch, but it is not operating at masterpiece level.");
            else if (rating > 0)
                sb.AppendLine(allowStrongLanguage ? "Honestly? It's a bit shit in places. Might still work for you if you're in the right mood, but don't go in expecting miracles for fuck's sake." : "This is a mixed watch with noticeable rough edges, so it depends heavily on your tolerance for its weaknesses.");
            else
                sb.AppendLine(allowStrongLanguage ? "Barely any data on this one. Could be a hidden gem or could be absolute bollocks — only one way to find out, innit." : "The metadata is thin, so the safest read is to judge this by premise rather than score confidence.");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(overview) && !overview.StartsWith("Loading", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("Story and setup");
                sb.AppendLine(overview.Trim());
                sb.AppendLine();
            }

            var detailBits = new List<string>();
            if ((item?.RuntimeMinutes ?? 0) > 0)
                detailBits.Add($"runtime {item!.RuntimeMinutes} min");
            if (rating > 0)
                detailBits.Add($"rating {rating:0.0}/10");
            if (cast.Count > 0)
                detailBits.Add("cast: " + string.Join(", ", cast.Take(5)));
            if (detailBits.Count > 0)
            {
                sb.AppendLine("Key details");
                sb.AppendLine(string.Join(" | ", detailBits));
                sb.AppendLine();
            }

            if (genres.Length > 0)
            {
                sb.AppendLine("What it is trying to do");
                sb.Append("It is leaning into ").Append(string.Join(", ", genres.Take(3))).Append(" territory");
                if ((item?.RuntimeMinutes ?? 0) >= 140)
                    sb.Append(", so expect a bigger commitment rather than something tight and disposable");
                sb.AppendLine(".");
                sb.AppendLine();
            }

            sb.AppendLine("Playback and addons");
            if (!string.IsNullOrWhiteSpace(streamsStatusText))
                sb.AppendLine(streamsStatusText.Trim());
            else if (playableCount > 0 || infoCount > 0)
                sb.AppendLine($"{playableCount} playable source(s), {infoCount} addon detail card(s).");
            else
                sb.AppendLine("No addon playback details are available yet.");

            if (playableSourceLines.Count > 0)
            {
                sb.AppendLine("Best source clues");
                foreach (var line in playableSourceLines.Take(3))
                    sb.AppendLine("- " + line);
            }

            if (infoCardLines.Count > 0)
            {
                sb.AppendLine("Addon notes");
                foreach (var line in infoCardLines.Take(2))
                    sb.AppendLine("- " + line);
            }

            if (infoCount > 0 && playableCount == 0)
                sb.AppendLine("The connected addons returned detail cards, but no playable stream links yet.");

            if (playableCount > 0)
                sb.AppendLine("If a trailer addon is installed, Atlas will try that first before falling back to direct trailer links or a YouTube search.");

            return sb.ToString().Trim();
        }

        private static string BuildTitleAnalysisSystemPrompt()
        {
            var settings = SettingsStore.Current;
            var allowStrongLanguage = settings.UnfilteredChaosIntensity >= 3 && settings.UnfilteredAllowProfanity;

            return allowStrongLanguage
                ? "You are Atlas — a foul-mouthed, hilarious, sarcastic movie critic. You talk like a real human, like someone's sweary mate down the pub after a few pints. Rules you MUST follow:\n" +
                  "1. SWEAR CONSTANTLY — every paragraph needs at least one: fucking, shit, bollocks, wank, arse, piss, bloody hell, for fuck's sake, shite, cock-up, sodding, twat. Mix them up, don't repeat the same swear.\n" +
                  "2. BE FUNNY AND SARCASTIC — take the piss, make jokes, be a smartarse. If something's bad, tear it apart. If something's good, hype it like you've just discovered it.\n" +
                  "3. REFERENCE THE REAL DATA — you'll receive real IMDb/Rotten Tomatoes/Metacritic scores, real user reviews from TMDB, box office numbers, and awards. Quote SPECIFIC numbers like \"it pulled a 92% on Rotten Tomatoes and honestly it fucking deserves it\" or \"Metacritic gave it a 45 which tells you everything\". NEVER invent scores.\n" +
                  "4. ARGUE WITH REVIEWERS — if a TMDB user said something you disagree with, call them out. If critics loved it but audiences didn't, say why.\n" +
                  "5. TELL THE STORY YOUR OWN WAY — don't copy the synopsis. Describe the plot like you're telling a mate about it: \"So basically this bloke...\"\n" +
                  "6. EACH REVIEW MUST BE UNIQUE — the real data is different for every film. Use it. Mention specific actor performances, specific box office figures, specific award wins.\n" +
                  "7. FORMAT: Short punchy paragraphs, plain text only, NO markdown, NO asterisks, NO bullet points, NO being polite, NO headers, NO \"in conclusion\". Just talk.\n" +
                  "8. NEVER say you're an AI, assistant, or Atlas AI. You're just Atlas."
                : "You are Atlas AI inside a media centre. Write a clean, conversational title analysis in plain text — no markdown, no asterisks, no bullet points, no headers. You'll receive REAL critic ratings (IMDb, Rotten Tomatoes, Metacritic), real audience scores, awards, box office data, and actual TMDB user reviews. You MUST reference these specific scores and real reviews in your response — quote exact numbers and mention what real reviewers said. Never invent ratings. Each response must be unique because the data is different per title. Retell the premise in your own words rather than copying the synopsis. Finish every sentence. Never roleplay or mention being an assistant.";

        }

        private static VoiceManager? TryGetVoiceManager()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is AtlasAI.CommandCenterWindow commandCenter)
                        return commandCenter.VoiceManager;
                    if (window is ChatWindow chatWindow)
                        return chatWindow.VoiceManager;
                }
            }
            catch
            {
            }

            return null;
        }

        private static long GetNewestDistWriteTicks(string dist)
        {
            if (string.IsNullOrWhiteSpace(dist) || !Directory.Exists(dist))
                return 0;

            long newestWriteTicks = 0;
            try
            {
                var indexPath = Path.Combine(dist, "index.html");
                if (File.Exists(indexPath))
                    newestWriteTicks = Math.Max(newestWriteTicks, File.GetLastWriteTimeUtc(indexPath).Ticks);

                var assetsDir = Path.Combine(dist, "assets");
                if (Directory.Exists(assetsDir))
                {
                    foreach (var filePath in Directory.EnumerateFiles(assetsDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            newestWriteTicks = Math.Max(newestWriteTicks, File.GetLastWriteTimeUtc(filePath).Ticks);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return newestWriteTicks;
        }

        private static async Task SpeakMediaResultAsync(string content)
        {
            try
            {
                var text = (content ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    return;

                // TryGetVoiceManager accesses Application.Current.Windows which requires the UI thread.
                var voiceManager = await Application.Current.Dispatcher.InvokeAsync(() => TryGetVoiceManager());
                if (voiceManager == null)
                    return;

                if (!voiceManager.SpeechEnabled)
                    voiceManager.SpeechEnabled = true;

                var spokenText = text.Length > 700 ? text[..700] : text;
                await voiceManager.SpeakAsync(new AssistantUtterance(spokenText, UtteranceSource.Conversation));
            }
            catch
            {
            }
        }

        private void UpdateEmbeddedVisibility()
        {
            try
            {
                if (ServersFigmaWebView == null || RootHost == null) return;

                var vm = _viewModel;
                var inServers = vm == null || vm.IsServersView;

                Background = new SolidColorBrush(Color.FromRgb(8, 10, 16));
                RootHost.Visibility = inServers ? Visibility.Visible : Visibility.Collapsed;
                ServersFigmaWebView.Visibility = inServers ? Visibility.Visible : Visibility.Collapsed;
                ServersFigmaWebView.IsHitTestVisible = inServers;
            }
            catch
            {
            }
        }

        private void SchedulePostState()
        {
            try
            {
                if (!IsLoaded || !IsVisible) return;
                if (RootHost?.Visibility != Visibility.Visible) return;
                if (_statePostScheduled) return;
                _statePostScheduled = true;

                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        _statePostScheduled = false;
                        if (!IsLoaded || !IsVisible) return;
                        if (RootHost?.Visibility != Visibility.Visible) return;
                        if (ServersFigmaWebView?.CoreWebView2 == null) return;
                        if (!_figmaEnabled) return;

                        var now = DateTime.UtcNow;
                        var dt = now - _lastStatePostUtc;
                        if (dt.TotalMilliseconds < 120)
                            await Task.Delay(TimeSpan.FromMilliseconds(120 - dt.TotalMilliseconds));

                        _lastStatePostUtc = DateTime.UtcNow;
                        PostState();
                        PostStreamsState();
                        PostSeriesState();
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        private static string ExtractImdbId(params string?[] values)
        {
            foreach (var rawValue in values)
            {
                var value = (rawValue ?? "").Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                for (var index = 1; index < value.Length - 7; index++)
                {
                    if ((value[index - 1] != 't' && value[index - 1] != 'T') || (value[index] != 't' && value[index] != 'T'))
                        continue;

                    var digitStart = index + 1;
                    var digitEnd = digitStart;
                    while (digitEnd < value.Length && char.IsDigit(value[digitEnd]))
                        digitEnd++;

                    if (digitEnd - digitStart < 7)
                        continue;

                    return $"tt{value.Substring(digitStart, digitEnd - digitStart)}";
                }
            }

            return "";
        }

        private static string RpdbPosterUrl(string rpdbKey, string imdbId)
        {
            var resolvedImdbId = ExtractImdbId(imdbId);
            if (string.IsNullOrWhiteSpace(rpdbKey) || string.IsNullOrWhiteSpace(resolvedImdbId))
                return "";
            return $"https://api.ratingposterdb.com/{Uri.EscapeDataString(rpdbKey.Trim())}/imdb/poster-default/{Uri.EscapeDataString(resolvedImdbId)}.jpg?fallback=true";
        }

        private void PostState()
        {
            try
            {
                if (!IsLoaded || !IsVisible) return;
                if (RootHost?.Visibility != Visibility.Visible) return;
                var vm = _viewModel;
                if (vm == null) return;
                if (ServersFigmaWebView?.CoreWebView2 == null) return;

                var rpdbKey = "";
                try { rpdbKey = (AtlasAI.Core.IntegrationKeyStore.GetDecrypted("rpdb") ?? "").Trim(); } catch { }

                var orderedShelves = OrderShelvesForClient(vm.ServerShelves);
                var shelves = orderedShelves.Select(s => new
                {
                    key = $"{(s.ContentType ?? "").Trim()}::{(s.CatalogId ?? "").Trim()}",
                    title = (s.Title ?? "").Trim(),
                    type = (s.ContentType ?? "").Trim(),
                    catalogId = (s.CatalogId ?? "").Trim(),
                    items = s.Items.Select(i =>
                    {
                        var imdb = ExtractImdbId(i.ImdbId, i.MetaId, i.FilePath);
                        var rpdbPoster = RpdbPosterUrl(rpdbKey, imdb);
                        var originalPoster = (i.CoverUrl ?? "").Trim();
                        var poster = !string.IsNullOrWhiteSpace(rpdbPoster) ? rpdbPoster : originalPoster;
                        return new
                    {
                        id = (i.MetaId ?? i.FilePath ?? "").Trim(),
                        metaId = (i.MetaId ?? "").Trim(),
                        tmdbId = i.TmdbId,
                        tmdb_id = i.TmdbId,
                        imdbId = imdb,
                        imdb_id = imdb,
                        title = (i.Title ?? "").Trim(),
                        thumbnail = poster,
                        coverUrl = poster,
                        poster,
                        originalPoster = originalPoster,
                        logoUrl = (i.LogoUrl ?? "").Trim(),
                        type = (i.Type ?? "").Trim(),
                        year = i.Year,
                        rating = i.Rating,
                        aiScore = i.AiScore,
                        aiRating = new { aiScore = i.AiScore, criticScore = i.CriticScore, audienceScore = i.AudienceScore, trendScore = i.TrendScore, qualityTier = (i.QualityTier ?? "").Trim() },
                        confidence = i.Confidence,
                        overview = (i.Overview ?? "").Trim(),
                        runtimeMinutes = i.RuntimeMinutes,
                        releaseDate = i.ReleaseDate == null ? "" : i.ReleaseDate.Value.ToString("yyyy-MM-dd"),
                        genres = i.Genres,
                        progress = i.ProgressPercent,
                        backdrop = (i.BackdropUrl ?? "").Trim(),
                        backdropUrl = (i.BackdropUrl ?? "").Trim(),
                        trailer = (i.TrailerUrl ?? "").Trim(),
                        trailerUrl = (i.TrailerUrl ?? "").Trim(),
                        streamSource = (i.StreamSource ?? "").Trim(),
                        stream_source = (i.StreamSource ?? "").Trim(),
                        streamingAvailability = i.StreamingAvailability ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        ratings = i.RatingsBreakdown ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                        rpdbRatings = i.RpdbRatings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        originalLanguage = (i.OriginalLanguage ?? "").Trim()
                    };
                    }).ToList()
                }).ToList();

                var serverOptions = new List<string>();
                try { serverOptions = vm.AddonServerOptions?.ToList() ?? new List<string>(); } catch { serverOptions = new List<string>(); }
                if (!serverOptions.Any(option => string.Equals(option, LocalLibraryServerOption, StringComparison.OrdinalIgnoreCase)))
                    serverOptions.Insert(0, LocalLibraryServerOption);

                var catalogOptions = new List<object>();
                try
                {
                    foreach (var o in vm.ServerCatalogCatalogs ?? new System.Collections.ObjectModel.ObservableCollection<MediaCentreViewModel.ServerCatalogCatalogOption>())
                    {
                        if (o == null) continue;
                        catalogOptions.Add(new { id = (o.Value ?? "").Trim(), title = (o.Label ?? "").Trim() });
                    }
                }
                catch
                {
                }

                var typeOptions = new List<object>();
                try
                {
                    foreach (var o in vm.ServerCatalogTypes ?? new System.Collections.ObjectModel.ObservableCollection<MediaCentreViewModel.ServerCatalogTypeOption>())
                    {
                        if (o == null) continue;
                        typeOptions.Add(new { id = (o.Value ?? "").Trim(), title = (o.Label ?? "").Trim() });
                    }
                }
                catch
                {
                }

                var genreOptions = new List<object>();
                try
                {
                    foreach (var o in vm.ServerCatalogGenres ?? new System.Collections.ObjectModel.ObservableCollection<MediaCentreViewModel.ServerCatalogOption>())
                    {
                        if (o == null) continue;
                        genreOptions.Add(new { id = (o.Value ?? "").Trim(), title = (o.Label ?? "").Trim() });
                    }
                }
                catch
                {
                }

                var catalogItems = new List<object>();
                try
                {
                    foreach (var i in vm.ServerCatalogItems ?? new System.Collections.ObjectModel.ObservableCollection<MediaItem>())
                    {
                        if (i == null) continue;
                        var catImdb = ExtractImdbId(i.ImdbId, i.MetaId, i.FilePath);
                        var catRpdb = RpdbPosterUrl(rpdbKey, catImdb);
                        var catOriginal = (i.CoverUrl ?? "").Trim();
                        var catPoster = !string.IsNullOrWhiteSpace(catRpdb) ? catRpdb : catOriginal;
                        catalogItems.Add(new
                        {
                            metaId = (i.MetaId ?? "").Trim(),
                            tmdbId = i.TmdbId,
                            tmdb_id = i.TmdbId,
                            imdbId = catImdb,
                            imdb_id = catImdb,
                            title = (i.Title ?? "").Trim(),
                            coverUrl = catPoster,
                            originalPoster = catOriginal,
                            logoUrl = (i.LogoUrl ?? "").Trim(),
                            year = i.Year,
                            rating = i.Rating,
                            aiScore = i.AiScore,
                            aiRating = new { aiScore = i.AiScore, criticScore = i.CriticScore, audienceScore = i.AudienceScore, trendScore = i.TrendScore, qualityTier = (i.QualityTier ?? "").Trim() },
                            confidence = i.Confidence,
                            overview = (i.Overview ?? "").Trim(),
                            runtimeMinutes = i.RuntimeMinutes,
                            releaseDate = i.ReleaseDate == null ? "" : i.ReleaseDate.Value.ToString("yyyy-MM-dd"),
                            genres = i.Genres,
                            type = (i.Type ?? "").Trim(),
                            backdropUrl = (i.BackdropUrl ?? "").Trim(),
                            trailerUrl = (i.TrailerUrl ?? "").Trim(),
                            streamSource = (i.StreamSource ?? "").Trim(),
                            stream_source = (i.StreamSource ?? "").Trim(),
                            streamingAvailability = i.StreamingAvailability ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                            ratings = i.RatingsBreakdown ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                            rpdbRatings = i.RpdbRatings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        });
                    }
                }
                catch
                {
                }

                object? preview = null;
                try
                {
                    var p = vm.PreviewItem;
                    if (p != null)
                    {
                        var previewImdb = ExtractImdbId(p.ImdbId, p.MetaId, p.FilePath);
                        var previewRpdb = RpdbPosterUrl(rpdbKey, previewImdb);
                        var previewOriginal = (p.CoverUrl ?? "").Trim();
                        var previewPoster = !string.IsNullOrWhiteSpace(previewRpdb) ? previewRpdb : previewOriginal;
                        preview = new
                        {
                            metaId = (p.MetaId ?? "").Trim(),
                            tmdbId = p.TmdbId,
                            tmdb_id = p.TmdbId,
                            imdbId = previewImdb,
                            imdb_id = previewImdb,
                            title = (p.Title ?? "").Trim(),
                            coverUrl = previewPoster,
                            originalPoster = previewOriginal,
                            backdropUrl = (p.BackdropUrl ?? "").Trim(),
                            logoUrl = (p.LogoUrl ?? "").Trim(),
                            duration = (vm.PreviewRuntimeText ?? "").Trim(),
                            summary = (vm.PreviewOverview ?? "").Trim(),
                            year = p.Year,
                            rating = p.Rating,
                            aiScore = p.AiScore,
                            aiRating = new { aiScore = p.AiScore, criticScore = p.CriticScore, audienceScore = p.AudienceScore, trendScore = p.TrendScore, qualityTier = (p.QualityTier ?? "").Trim() },
                            confidence = p.Confidence,
                            overview = (p.Overview ?? "").Trim(),
                            runtimeMinutes = p.RuntimeMinutes,
                            releaseDate = p.ReleaseDate == null ? "" : p.ReleaseDate.Value.ToString("yyyy-MM-dd"),
                            genres = p.Genres,
                            type = (p.Type ?? "").Trim(),
                            trailerUrl = (p.TrailerUrl ?? "").Trim(),
                            cast = vm.PreviewCast?.ToArray() ?? Array.Empty<string>(),
                            streamSource = (p.StreamSource ?? "").Trim(),
                            stream_source = (p.StreamSource ?? "").Trim(),
                            streamingAvailability = p.StreamingAvailability ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                            ratings = p.RatingsBreakdown ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                            rpdbRatings = p.RpdbRatings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        };
                    }
                }
                catch
                {
                    preview = null;
                }

                var effectiveMode = vm.IsServerShelvesMode
                    ? (shelves.Count > 0 ? "shelves" : (catalogItems.Count > 0 ? "grid" : "shelves"))
                    : "grid";

                var selectedServer = string.Equals((_selectedServerView ?? "").Trim(), LocalLibraryServerOption, StringComparison.OrdinalIgnoreCase)
                    ? LocalLibraryServerOption
                    : (!string.IsNullOrWhiteSpace((_selectedServerView ?? "").Trim())
                        ? (_selectedServerView ?? "").Trim()
                        : (!string.IsNullOrWhiteSpace((vm.SelectedAddonServer ?? "").Trim())
                            ? (vm.SelectedAddonServer ?? "").Trim()
                            : AllServersOption));
                var isLocalLibraryMode = string.Equals(selectedServer, LocalLibraryServerOption, StringComparison.OrdinalIgnoreCase);
                var localMovies = isLocalLibraryMode
                    ? EnumerateLocalMovieItems(vm).Take(80).Select(BuildLocalMoviePayload).ToList()
                    : new List<object>();
                var localAlbums = isLocalLibraryMode
                    ? vm.MusicAlbums.Take(60).Select(album => BuildLocalAlbumPayload(album, vm)).ToList()
                    : new List<object>();
                var localSelection = (vm.SelectedAlbum != null || string.Equals(vm.NowPlayingTypeId, "music", StringComparison.OrdinalIgnoreCase))
                    ? "music"
                    : "movies";

                var payload = new
                {
                    viewMode = isLocalLibraryMode ? "local" : "servers",
                    mode = effectiveMode,
                    selectedServer,
                    clientShelfOrderIds = LoadSavedClientShelfOrder(),
                    serverOptions,
                    typeOptions,
                    selectedType = (vm.SelectedServerCatalogType ?? "").Trim(),
                    selectedCatalogId = (vm.SelectedServerCatalogCatalogId ?? "").Trim(),
                    selectedGenre = (vm.SelectedServerCatalogGenre ?? "").Trim(),
                    catalogOptions,
                    genreOptions,
                    canLoadMore = vm.CanLoadMoreServerCatalog,
                    isBusy = vm.IsServerCatalogBusy,
                    activeShelfKey = vm.ActiveServerShelf == null ? "" : $"{(vm.ActiveServerShelf.ContentType ?? "").Trim()}::{(vm.ActiveServerShelf.CatalogId ?? "").Trim()}",
                    shelves,
                    catalogItems,
                    preview,
                    localSelection,
                    localMovies,
                    localAlbums,
                    preferredContentLanguage = (SettingsStore.Current.PreferredContentLanguage ?? "en").Trim()
                };

                var msg = JsonSerializer.Serialize(new { type = "servers.state", payload });
                if (string.Equals(_lastPostedStateMessage, msg, StringComparison.Ordinal))
                    return;

                _lastPostedStateMessage = msg;

                ServersFigmaWebView.CoreWebView2.PostWebMessageAsJson(msg);
                _ = ApplyServersChromePatchAsync();
            }
            catch
            {
            }
        }

        private void PostStreamsState()
        {
            try
            {
                var vm = _viewModel;
                if (vm == null) return;
                if (ServersFigmaWebView?.CoreWebView2 == null) return;

                var mediaId = "";
                try
                {
                    var target = vm.StreamsTargetItem ?? vm.PreviewItem ?? vm.DetailsItem;
                    mediaId = (target?.MetaId ?? target?.ImdbId ?? target?.FilePath ?? "").Trim();
                }
                catch
                {
                    mediaId = "";
                }

                var sources = vm.StreamSources.Select(s => new
                {
                    sourceId = (s.SourceId ?? "").Trim(),
                    name = (s.Name ?? "").Trim(),
                    providerId = (s.ProviderId ?? "").Trim(),
                    providerName = (s.ProviderName ?? "").Trim(),
                        urlOrPath = (s.UrlOrPath ?? "").Trim(),
                    quality = (s.Quality ?? "").Trim(),
                    requiresDebrid = s.RequiresDebrid,
                    isInfoOnly = s.IsInfoOnly,
                    isPlayable = !s.IsInfoOnly && !string.IsNullOrWhiteSpace(s.UrlOrPath) && !MediaCentreViewModel.IsLikelyAddonNavigationUrl(s.UrlOrPath),
                    rank = s.Rank,
                    metadata = s.Metadata ?? new Dictionary<string, string>(),
                    sizeText = (s.SizeText ?? "").Trim(),
                    seedersText = (s.SeedersText ?? "").Trim()
                }).ToList();

                var msg = JsonSerializer.Serialize(new
                {
                    type = "atlas:streams:state",
                    mediaId,
                    isBusy = vm.IsStreamsBusy,
                    statusText = (vm.StreamsStatusText ?? "").Trim(),
                    sources
                });

                if (string.Equals(_lastPostedStreamsMessage, msg, StringComparison.Ordinal))
                    return;

                _lastPostedStreamsMessage = msg;
                ServersFigmaWebView.CoreWebView2.PostWebMessageAsJson(msg);
            }
            catch
            {
            }
        }

        private string _lastPostedSeriesMessage = "";

        private void PostSeriesState()
        {
            try
            {
                var vm = _viewModel;
                if (vm == null) return;
                if (ServersFigmaWebView?.CoreWebView2 == null) return;

                var isOpen = vm.IsServerSeriesPanelOpen || (vm.UseServersWebViewBridge && vm.ServerSeriesRootItem != null);
                var root = vm.ServerSeriesRootItem;

                var seasons = vm.ServerSeriesSeasons.Select(s => new
                {
                    seasonNumber = s.SeasonNumber,
                    label = (s.Label ?? "").Trim()
                }).ToList();

                var selectedSeason = vm.SelectedServerSeriesSeason?.SeasonNumber ?? 0;

                var episodes = vm.VisibleServerSeriesEpisodes.Select(e => new
                {
                    id = (e.MetaId ?? e.FilePath ?? "").Trim(),
                    metaId = (e.MetaId ?? "").Trim(),
                    imdbId = (e.ImdbId ?? "").Trim(),
                    title = (e.Title ?? "").Trim(),
                    season = e.TrackNumber,
                    episode = e.DiscNumber,
                    overview = (e.Overview ?? "").Trim(),
                    thumbnail = (e.CoverUrl ?? "").Trim(),
                    released = e.ReleaseDate == null ? "" : e.ReleaseDate.Value.ToString("yyyy-MM-dd"),
                    runtime = e.RuntimeMinutes > 0 ? $"{e.RuntimeMinutes} min" : ""
                }).ToList();

                var msg = JsonSerializer.Serialize(new
                {
                    type = "servers.series.state",
                    isOpen,
                    isBusy = vm.IsServerSeriesBusy,
                    statusText = (vm.ServerSeriesStatusText ?? "").Trim(),
                    rootTitle = (root?.Title ?? "").Trim(),
                    rootId = (root?.MetaId ?? root?.FilePath ?? "").Trim(),
                    rootType = (root?.Type ?? "series").Trim(),
                    rootBackdrop = (root?.BackdropUrl ?? root?.CoverUrl ?? "").Trim(),
                    rootPoster = (root?.CoverUrl ?? "").Trim(),
                    seasons,
                    selectedSeason,
                    episodes
                });

                if (string.Equals(_lastPostedSeriesMessage, msg, StringComparison.Ordinal))
                    return;

                _lastPostedSeriesMessage = msg;
                ServersFigmaWebView.CoreWebView2.PostWebMessageAsJson(msg);
            }
            catch
            {
            }
        }

        private void ShelfHorizontalScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            if (sender is not ScrollViewer sv) return;

            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (shift)
            {
                sv.ScrollToHorizontalOffset(Math.Max(0, sv.HorizontalOffset - e.Delta));
                e.Handled = true;
                return;
            }

            var parent = FindAncestorScrollViewer(sv);
            if (parent == null) return;
            parent.ScrollToVerticalOffset(Math.Max(0, parent.VerticalOffset - e.Delta));
            e.Handled = true;
        }

        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject start)
        {
            try
            {
                var current = VisualTreeHelper.GetParent(start);
                while (current != null)
                {
                    if (current is ScrollViewer sv) return sv;
                    current = VisualTreeHelper.GetParent(current);
                }
            }
            catch
            {
            }
            return null;
        }

		private void ServerCatalogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
		{
			try
			{
				if (sender is not ScrollViewer sv)
					return;
				if (DataContext is not MediaCentreViewModel vm)
					return;
				if (vm.IsServerCatalogBusy)
					return;
				if (!vm.CanLoadNextServerCatalogPage)
					return;
				if (sv.ScrollableHeight <= 0)
					return;

				if (sv.VerticalOffset < sv.ScrollableHeight - 250)
					return;

				var now = DateTime.UtcNow;
				if (now - _lastAutoLoadUtc < TimeSpan.FromMilliseconds(600))
					return;
				_lastAutoLoadUtc = now;

				ICommand? cmd = vm.NextServerCatalogPageCommand;
				if (cmd?.CanExecute(null) == true)
					cmd.Execute(null);
			}
			catch
			{
			}
		}
    }
}


