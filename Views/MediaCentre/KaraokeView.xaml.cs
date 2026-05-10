using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Views.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Views.MediaCentre
{
    public partial class KaraokeView : UserControl
    {
        private MediaCentreViewModel? _viewModel;
        private bool _vmHooked;

        public KaraokeView()
        {
            InitializeComponent();
            DataContextChanged += KaraokeView_DataContextChanged;
            Loaded += KaraokeView_Loaded;
            Unloaded += KaraokeView_Unloaded;
        }

        private async void KaraokeView_Loaded(object sender, RoutedEventArgs e)
        {
            try { await EnsureInitializedAsync(); } catch { }
        }

        private void KaraokeView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (KaraokeWebView?.CoreWebView2 != null)
                    KaraokeWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            }
            catch { }

            try
            {
                if (_viewModel != null && _vmHooked)
                {
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                    _viewModel.LibraryItems.CollectionChanged -= LibraryItems_CollectionChanged;
                    _vmHooked = false;
                }
            }
            catch
            {
            }
        }

        private void KaraokeView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (_viewModel != null && _vmHooked)
                {
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                    _viewModel.LibraryItems.CollectionChanged -= LibraryItems_CollectionChanged;
                    _vmHooked = false;
                }

                _viewModel = DataContext as MediaCentreViewModel;
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                    _viewModel.LibraryItems.CollectionChanged += LibraryItems_CollectionChanged;
                    _vmHooked = true;
                }
            }
            catch
            {
            }
        }

        private void LibraryItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try { PostKaraokeState(); } catch { }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                var n = e.PropertyName ?? "";
                if (n == nameof(MediaCentreViewModel.IsPlaying) ||
                    n == nameof(MediaCentreViewModel.NowPlayingTitle) ||
                    n == nameof(MediaCentreViewModel.ProgressSeconds) ||
                    n == nameof(MediaCentreViewModel.TotalSeconds) ||
                    n == nameof(MediaCentreViewModel.Volume) ||
                    n == nameof(MediaCentreViewModel.NowPlayingTypeId))
                {
                    PostKaraokeState();
                }
            }
            catch
            {
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (KaraokeWebView.CoreWebView2 == null)
            {
                var userDataFolder = Path.Combine(Path.GetTempPath(), "AtlasOS_WebView2", "Karaoke");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await KaraokeWebView.EnsureCoreWebView2Async(env);
            }
            try { KaraokeWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 5, 7, 11); } catch { }

            KaraokeWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            KaraokeWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            KaraokeWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            KaraokeWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            var dist = FindKaraokePlayerDist() ?? FindFallbackCommandCenterDist();
            if (dist == null) return;

            var host = "atlas-ui-karaoke";
            KaraokeWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                host,
                dist,
                CoreWebView2HostResourceAccessKind.Allow);

            KaraokeWebView.CoreWebView2.AddWebResourceRequestedFilter("https://local-media/*", CoreWebView2WebResourceContext.All);
            KaraokeWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            KaraokeWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            long indexWriteTicks = 0;
            try
            {
                var indexPath = Path.Combine(dist, "index.html");
                if (File.Exists(indexPath))
                    indexWriteTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
            }
            catch { }

            var v = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
            var url = $"https://{host}/index.html?mode=karaoke&v={v}";
            KaraokeWebView.CoreWebView2.Navigate(url);
            PostKaraokeState();
        }

        private static string? FindKaraokePlayerDist()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var shipped = Path.Combine(baseDir, "Figma", "Futuristic Karaoke Player", "dist");
                if (Directory.Exists(shipped) && File.Exists(Path.Combine(shipped, "index.html")))
                    return shipped;
            }
            catch { }

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
                            var uiFolder = Directory.GetDirectories(figmaRoot, "Futuristic Karaoke Player", SearchOption.TopDirectoryOnly)
                                .FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(uiFolder))
                            {
                                var dist = Path.Combine(uiFolder, "dist");
                                if (Directory.Exists(dist) && File.Exists(Path.Combine(dist, "index.html")))
                                    return dist;
                            }
                        }
                        dir = dir.Parent;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string? FindFallbackCommandCenterDist()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var shipped = Path.Combine(baseDir, "Figma", "Futuristic AI Command Center (6)", "dist");
                if (Directory.Exists(shipped) && File.Exists(Path.Combine(shipped, "index.html")))
                    return shipped;
            }
            catch { }
            return null;
        }

        private void PostKaraokeState()
        {
            try
            {
                if (_viewModel == null || KaraokeWebView?.CoreWebView2 == null) return;

                var karaokeItems = _viewModel.AllItems
                    .Where(i => i != null && string.Equals(i.Type, "karaoke", StringComparison.OrdinalIgnoreCase))
                    .Select(i => new
                    {
                        id = (i.FilePath ?? "").Trim(),
                        title = (i.Title ?? "").Trim(),
                        artist = (i.Artist ?? "").Trim(),
                        album = (i.Album ?? "").Trim(),
                        durationSeconds = Math.Max(0, (int)i.Duration.TotalSeconds),
                        filePath = (i.FilePath ?? "").Trim(),
                    })
                    .Where(i => !string.IsNullOrWhiteSpace(i.id))
                    .Take(5000)
                    .ToList();

                var nowType = (_viewModel.NowPlayingTypeId ?? "").Trim();
                var nowTitle = (_viewModel.NowPlayingTitle ?? "").Trim();

                var msg = new
                {
                    type = "karaoke.state",
                    payload = new
                    {
                        items = karaokeItems,
                        isPlaying = _viewModel.IsPlaying,
                        nowPlayingTypeId = nowType,
                        nowPlayingTitle = nowTitle,
                        progressSeconds = _viewModel.ProgressSeconds,
                        totalSeconds = _viewModel.TotalSeconds,
                        volume = _viewModel.Volume,
                    }
                };

                KaraokeWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
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
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return;
                var type = (typeEl.GetString() ?? "").Trim();

                if (string.Equals(type, "karaoke.getState", StringComparison.OrdinalIgnoreCase))
                {
                    PostKaraokeState();
                    return;
                }

                if (string.Equals(type, "karaoke.play", StringComparison.OrdinalIgnoreCase))
                {
                    if (_viewModel == null) return;
                    if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("filePath", out var fpEl))
                    {
                        var fp = (fpEl.GetString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(fp)) return;
                        var item = _viewModel.AllItems.FirstOrDefault(i =>
                            i != null &&
                            string.Equals(i.Type, "karaoke", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((i.FilePath ?? "").Trim(), fp, StringComparison.OrdinalIgnoreCase));
                        if (item != null && _viewModel.PlayItemCommand.CanExecute(item))
                            _viewModel.PlayItemCommand.Execute(item);
                    }
                    return;
                }

                if (string.Equals(type, "play.toggle", StringComparison.OrdinalIgnoreCase))
                {
                    if (_viewModel?.TogglePlayPauseCommand.CanExecute(null) == true)
                        _viewModel.TogglePlayPauseCommand.Execute(null);
                    return;
                }

                if (string.Equals(type, "play.seek", StringComparison.OrdinalIgnoreCase))
                {
                    if (_viewModel == null) return;
                    if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("seconds", out var secEl))
                    {
                        var sec = secEl.ValueKind == JsonValueKind.Number ? secEl.GetDouble() : 0;
                        _viewModel.EndUserSeek(sec);
                    }
                    return;
                }

                if (string.Equals(type, "play.volume", StringComparison.OrdinalIgnoreCase))
                {
                    if (_viewModel == null) return;
                    if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("value", out var vEl))
                    {
                        var v = vEl.ValueKind == JsonValueKind.Number ? vEl.GetDouble() : 0;
                        _viewModel.Volume = (int)Math.Max(0, Math.Min(100, v));
                    }
                    return;
                }

                if (string.Equals(type, "lyrics.get", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("filePath", out var fpElement))
                    {
                        var fp = (fpElement.GetString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(fp))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var scannerItem = new AtlasAI.MediaScanner.MediaItem
                                    {
                                        FilePath = fp,
                                        DisplayName = Path.GetFileNameWithoutExtension(fp),
                                        MediaType = AtlasAI.MediaScanner.MediaType.Audio
                                    };
                                    var lines = await AtlasAI.MediaScanner.LyricsService.Instance.GetLyricsAsync(scannerItem).ConfigureAwait(false);
                                    var payloadObj = new
                                    {
                                        filePath = fp,
                                        lines = lines.Select(l => new { timeSeconds = l.Timestamp.TotalSeconds, text = l.Text ?? "" }).ToList()
                                    };
                                    var msg = new { type = "lyrics.result", payload = payloadObj };
                                    Dispatcher.Invoke(() =>
                                    {
                                        KaraokeWebView?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
                                    });
                                }
                                catch
                                {
                                }
                            });
                        }
                    }
                    return;
                }

                if (string.Equals(type, "karaoke.youtube.search", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("query", out var qElement))
                    {
                        var query = (qElement.GetString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(query)) return;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var apiKey = (AtlasAI.Core.IntegrationKeyStore.GetDecrypted("youtube_api_key") ?? "").Trim();
                                if (string.IsNullOrWhiteSpace(apiKey)) return;

                                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
                                var client = new AtlasAI.Integrations.YouTubeDataClient();
                                var results = await client.SearchVideosAsync(apiKey, query, cts.Token).ConfigureAwait(false);

                                var msg = new
                                {
                                    type = "karaoke.youtube.results",
                                    payload = new
                                    {
                                        query,
                                        items = results.Select(r => new
                                        {
                                            videoId = r.VideoId,
                                            title = r.Title,
                                            channelTitle = r.ChannelTitle,
                                            thumbnailUrl = r.ThumbnailUrl
                                        }).ToList()
                                    }
                                };

                                Dispatcher.Invoke(() =>
                                {
                                    KaraokeWebView?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
                                });
                            }
                            catch
                            {
                            }
                        });
                    }
                    return;
                }
            }
            catch
            {
            }
        }

        private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var uri = new Uri(e.Request.Uri);
                if (uri.Host != "local-media") return;

                var localPath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                if (!File.Exists(localPath)) return;

                var ext = Path.GetExtension(localPath).ToLowerInvariant();
                var mime = "application/octet-stream";
                if (ext == ".mp3") mime = "audio/mpeg";
                else if (ext == ".flac") mime = "audio/flac";
                else if (ext == ".wav") mime = "audio/wav";
                else if (ext == ".mp4") mime = "video/mp4";
                else if (ext == ".mkv") mime = "video/x-matroska";
                else if (ext == ".webm") mime = "video/webm";

                var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var totalLen = fileStream.Length;

                string? rangeHeader = null;
                try { rangeHeader = e.Request.Headers.GetHeader("Range"); } catch { }

                if (TryParseRangeHeader(rangeHeader, totalLen, out var start, out var end))
                {
                    try { fileStream.Position = start; } catch { }
                    var len = (end - start) + 1;
                    var body = new RangeStream(fileStream, len);
                    var headers =
                        $"Content-Type: {mime}\r\n" +
                        $"Accept-Ranges: bytes\r\n" +
                        $"Content-Range: bytes {start}-{end}/{totalLen}\r\n" +
                        $"Content-Length: {len}\r\n";
                    e.Response = KaraokeWebView.CoreWebView2.Environment.CreateWebResourceResponse(body, 206, "Partial Content", headers);
                    return;
                }

                var fullHeaders =
                    $"Content-Type: {mime}\r\n" +
                    $"Accept-Ranges: bytes\r\n" +
                    $"Content-Length: {totalLen}\r\n";

                e.Response = KaraokeWebView.CoreWebView2.Environment.CreateWebResourceResponse(fileStream, 200, "OK", fullHeaders);
            }
            catch
            {
            }
        }

        private sealed class RangeStream : Stream
        {
            private readonly Stream _inner;
            private long _remaining;

            public RangeStream(Stream inner, long length)
            {
                _inner = inner;
                _remaining = length;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_remaining <= 0) return 0;
                var toRead = (int)Math.Min(count, _remaining);
                var read = _inner.Read(buffer, offset, toRead);
                _remaining -= read;
                return read;
            }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            {
                if (_remaining <= 0) return Task.FromResult(0);
                var toRead = (int)Math.Min(count, _remaining);
                return _inner.ReadAsync(buffer, offset, toRead, cancellationToken).ContinueWith(t =>
                {
                    var read = t.Result;
                    _remaining -= read;
                    return read;
                }, cancellationToken);
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }

        private static bool TryParseRangeHeader(string? rangeHeader, long totalLength, out long start, out long end)
        {
            start = 0;
            end = 0;
            if (string.IsNullOrWhiteSpace(rangeHeader)) return false;
            var s = rangeHeader.Trim();
            if (!s.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
            s = s.Substring("bytes=".Length);

            var dash = s.IndexOf('-');
            if (dash < 0) return false;
            var a = s.Substring(0, dash).Trim();
            var b = s.Substring(dash + 1).Trim();

            if (string.IsNullOrWhiteSpace(a))
            {
                if (!long.TryParse(b, out var suffixLen) || suffixLen <= 0) return false;
                suffixLen = Math.Min(suffixLen, totalLength);
                start = Math.Max(0, totalLength - suffixLen);
                end = totalLength - 1;
                return true;
            }

            if (!long.TryParse(a, out start) || start < 0) return false;
            if (string.IsNullOrWhiteSpace(b))
            {
                end = totalLength - 1;
                return start < totalLength;
            }

            if (!long.TryParse(b, out end) || end < start) return false;
            end = Math.Min(end, totalLength - 1);
            return start < totalLength;
        }
    }
}
