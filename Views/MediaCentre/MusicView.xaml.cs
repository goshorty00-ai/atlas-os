using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using AtlasAI.Views.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AtlasAI.MediaScanner;
using AtlasAI.MediaMetadata;
using System.Collections.Generic;
using System.Windows.Threading;
using NAudio.Dsp;
using NAudio.Wave;


namespace AtlasAI.Views.MediaCentre
{
    public partial class MusicView : UserControl
    {
        private MediaCentreViewModel? _viewModel;
        private bool _vmHooked;
        private readonly DispatcherTimer _spectrumTimer;
        private WasapiLoopbackCapture? _spectrumCapture;
        private readonly object _spectrumLock = new();
        private readonly float[] _fftBuffer = new float[2048];
        private readonly float[] _spectrum = new float[64];
        private int _fftPosition;
        private const int FftLength = 2048;
        private const int FftPower = 11;

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

            // Only support a single range.
            var dash = s.IndexOf('-');
            if (dash < 0) return false;
            var a = s.Substring(0, dash).Trim();
            var b = s.Substring(dash + 1).Trim();

            if (string.IsNullOrWhiteSpace(a))
            {
                // Suffix range: bytes=-N
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

        private static string MakeSafeFileName(string name)
        {
            var n = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(n)) n = "playlist";
            foreach (var c in Path.GetInvalidFileNameChars())
                n = n.Replace(c, '_');
            return n;
        }

        private static string BuildUniquePath(string folder, string baseName, string ext)
        {
            var safe = MakeSafeFileName(baseName);
            var p = Path.Combine(folder, safe + ext);
            if (!File.Exists(p)) return p;
            for (var i = 2; i < 1000; i++)
            {
                var next = Path.Combine(folder, $"{safe} ({i}){ext}");
                if (!File.Exists(next)) return next;
            }
            return Path.Combine(folder, $"{safe} ({Guid.NewGuid():N}){ext}");
        }

        public MusicView()
        {
            InitializeComponent();
            _spectrumTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _spectrumTimer.Tick += (_, _) => PostSpectrumFrame();
            DataContextChanged += MusicView_DataContextChanged;
            Loaded += MusicView_Loaded;
            Unloaded += MusicView_Unloaded;
        }

        private void MusicView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.MusicAlbums.CollectionChanged -= MusicAlbums_CollectionChanged;
                if (_vmHooked)
                {
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                    _vmHooked = false;
                }
            }

            _viewModel = e.NewValue as MediaCentreViewModel;

            if (_viewModel != null)
            {
                _viewModel.MusicAlbums.CollectionChanged += MusicAlbums_CollectionChanged;
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                _vmHooked = true;
            }
        }

        private async void MusicView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureInitializedAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicView] Init failed: {ex.Message}");
            }
        }

        private void MusicView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MusicWebView?.CoreWebView2 != null)
                    MusicWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            }
            catch { }

            try
            {
                if (_viewModel != null)
                    _viewModel.MusicAlbums.CollectionChanged -= MusicAlbums_CollectionChanged;
                if (_viewModel != null && _vmHooked)
                {
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                    _vmHooked = false;
                }
            }
            catch { }

            StopSpectrumCapture();
            try
            {
                if (_spectrumTimer.IsEnabled)
                    _spectrumTimer.Stop();
            }
            catch { }
        }

        private async Task EnsureInitializedAsync()
        {
            if (MusicWebView.CoreWebView2 != null) return;

            var userDataFolder = Path.Combine(Path.GetTempPath(), "AtlasOS_WebView2", "Music");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await MusicWebView.EnsureCoreWebView2Async(env);

            MusicWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            MusicWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            MusicWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            var dist = FindMusicPlayerDist() ?? FindFallbackCommandCenterDist();
            if (dist == null) return;

            var host = "atlas-ui-music";
            MusicWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                host,
                dist,
                CoreWebView2HostResourceAccessKind.Allow);

            MusicWebView.CoreWebView2.AddWebResourceRequestedFilter("https://local-media/*", CoreWebView2WebResourceContext.All);
            MusicWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            MusicWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            long indexWriteTicks = 0;
            try
            {
                var indexPath = Path.Combine(dist, "index.html");
                if (File.Exists(indexPath))
                    indexWriteTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
            }
            catch { }

            var v = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
            var url = $"https://{host}/index.html?v={v}";
            MusicWebView.CoreWebView2.Navigate(url);
            StartSpectrumCapture();
            if (!_spectrumTimer.IsEnabled)
                _spectrumTimer.Start();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                var propertyName = e.PropertyName ?? "";
                if (propertyName == nameof(MediaCentreViewModel.IsPlaying) ||
                    propertyName == nameof(MediaCentreViewModel.NowPlayingTitle) ||
                    propertyName == nameof(MediaCentreViewModel.NowPlayingArtist) ||
                    propertyName == nameof(MediaCentreViewModel.NowPlayingAlbum) ||
                    propertyName == nameof(MediaCentreViewModel.NowPlayingTypeId) ||
                    propertyName == nameof(MediaCentreViewModel.ProgressSeconds) ||
                    propertyName == nameof(MediaCentreViewModel.TotalSeconds) ||
                    propertyName == nameof(MediaCentreViewModel.Volume))
                {
                    PostPlaybackState();
                }
            }
            catch
            {
            }
        }

        private void StartSpectrumCapture()
        {
            try
            {
                lock (_spectrumLock)
                {
                    if (_spectrumCapture != null) return;
                    _fftPosition = 0;
                    Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
                    Array.Clear(_spectrum, 0, _spectrum.Length);

                    _spectrumCapture = new WasapiLoopbackCapture();
                    _spectrumCapture.DataAvailable += SpectrumCapture_DataAvailable;
                    _spectrumCapture.RecordingStopped += SpectrumCapture_RecordingStopped;
                    _spectrumCapture.StartRecording();
                }
            }
            catch
            {
                StopSpectrumCapture();
            }
        }

        private void StopSpectrumCapture()
        {
            try
            {
                lock (_spectrumLock)
                {
                    if (_spectrumCapture == null) return;
                    try { _spectrumCapture.DataAvailable -= SpectrumCapture_DataAvailable; } catch { }
                    try { _spectrumCapture.RecordingStopped -= SpectrumCapture_RecordingStopped; } catch { }
                    try { _spectrumCapture.StopRecording(); } catch { }
                    try { _spectrumCapture.Dispose(); } catch { }
                    _spectrumCapture = null;
                    _fftPosition = 0;
                    Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
                    Array.Clear(_spectrum, 0, _spectrum.Length);
                }
            }
            catch
            {
            }
        }

        private void SpectrumCapture_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            StopSpectrumCapture();
        }

        private void SpectrumCapture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                if (e.BytesRecorded <= 0) return;

                lock (_spectrumLock)
                {
                    if (_spectrumCapture == null) return;
                    var waveFormat = _spectrumCapture.WaveFormat;
                    var channels = Math.Max(1, waveFormat.Channels);
                    var bitsPerSample = waveFormat.BitsPerSample;
                    var bytesPerSample = Math.Max(1, bitsPerSample / 8);
                    var blockAlign = waveFormat.BlockAlign > 0 ? waveFormat.BlockAlign : channels * bytesPerSample;

                    for (var offset = 0; offset + blockAlign <= e.BytesRecorded; offset += blockAlign)
                    {
                        float sum = 0;
                        for (var channel = 0; channel < channels; channel++)
                        {
                            var sampleOffset = offset + (channel * bytesPerSample);
                            float sample;
                            if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                            {
                                sample = BitConverter.ToSingle(e.Buffer, sampleOffset);
                            }
                            else if (waveFormat.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 2)
                            {
                                sample = BitConverter.ToInt16(e.Buffer, sampleOffset) / 32768f;
                            }
                            else if (waveFormat.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 3)
                            {
                                var sample24 = (e.Buffer[sampleOffset + 2] << 16) | (e.Buffer[sampleOffset + 1] << 8) | e.Buffer[sampleOffset];
                                if ((sample24 & 0x800000) != 0) sample24 |= unchecked((int)0xFF000000);
                                sample = sample24 / 8388608f;
                            }
                            else if (waveFormat.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 4)
                            {
                                sample = BitConverter.ToInt32(e.Buffer, sampleOffset) / (float)int.MaxValue;
                            }
                            else
                            {
                                sample = 0;
                            }

                            sum += sample;
                        }

                        _fftBuffer[_fftPosition++] = sum / channels;
                        if (_fftPosition >= FftLength)
                        {
                            ProcessSpectrumFft();
                            _fftPosition = 0;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void ProcessSpectrumFft()
        {
            var fft = new Complex[FftLength];
            for (var i = 0; i < FftLength; i++)
            {
                var window = (float)FastFourierTransform.HannWindow(i, FftLength);
                fft[i].X = _fftBuffer[i] * window;
                fft[i].Y = 0;
            }

            FastFourierTransform.FFT(true, FftPower, fft);

            var bins = FftLength / 2;
            for (var i = 0; i < _spectrum.Length; i++)
            {
                var start = (int)Math.Clamp(MathF.Pow((i + 1f) / _spectrum.Length, 2.3f) * bins, 1, bins - 2);
                var end = (int)Math.Clamp(MathF.Pow((i + 2f) / _spectrum.Length, 2.3f) * bins, start + 1, bins - 1);

                float max = 0;
                for (var bin = start; bin < end; bin++)
                {
                    var real = fft[bin].X;
                    var imaginary = fft[bin].Y;
                    var magnitude = (float)Math.Sqrt((real * real) + (imaginary * imaginary));
                    if (magnitude > max)
                        max = magnitude;
                }

                var decibels = 20f * (float)Math.Log10(max + 1e-9f);
                var scaled = (decibels + 70f) / 70f;
                scaled = Math.Clamp(scaled, 0f, 1f);
                _spectrum[i] = (float)(_spectrum[i] * 0.72 + scaled * 0.28);
            }
        }

        private void PostSpectrumFrame()
        {
            try
            {
                if (MusicWebView?.CoreWebView2 == null) return;
                float[] snapshot;
                lock (_spectrumLock)
                {
                    snapshot = (float[])_spectrum.Clone();
                }

                var msg = new
                {
                    type = "media.spectrum",
                    payload = new
                    {
                        bars = snapshot.Select(value => Math.Round(Math.Clamp(value, 0f, 1f), 4)).ToArray()
                    }
                };

                MusicWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
            }
            catch
            {
            }
        }

        private void PostPlaybackState()
        {
            try
            {
                if (_viewModel == null || MusicWebView?.CoreWebView2 == null) return;

                var currentMedia = MediaPlaybackService.Instance?.CurrentMedia;
                var msg = new
                {
                    type = "playback.state",
                    payload = new
                    {
                        isPlaying = _viewModel.IsPlaying,
                        nowPlayingTitle = _viewModel.NowPlayingTitle,
                        nowPlayingArtist = _viewModel.NowPlayingArtist,
                        nowPlayingAlbum = _viewModel.NowPlayingAlbum,
                        nowPlayingTypeId = _viewModel.NowPlayingTypeId,
                        filePath = currentMedia?.FilePath ?? "",
                        progressSeconds = _viewModel.ProgressSeconds,
                        totalSeconds = _viewModel.TotalSeconds,
                        progressText = _viewModel.ProgressText,
                        totalText = _viewModel.TotalText,
                        volume = _viewModel.Volume,
                        shuffleEnabled = _viewModel.ShuffleEnabled,
                        repeatEnabled = _viewModel.RepeatEnabled,
                    }
                };

                MusicWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
            }
            catch
            {
            }
        }

        private void PostApiStatus()
        {
            try
            {
                if (MusicWebView?.CoreWebView2 == null) return;

                static bool IsKeylessProvider(string? name)
                {
                    return string.Equals(name, "MusicBrainz", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "iTunes", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "Deezer", StringComparison.OrdinalIgnoreCase);
                }

                var providers = MusicMetadataHub.Instance.Providers
                    .Select(provider => new
                    {
                        name = provider.Name,
                        requiresKey = !IsKeylessProvider(provider.Name),
                        isConfigured = provider.IsConfigured || IsKeylessProvider(provider.Name),
                        status = IsKeylessProvider(provider.Name)
                            ? "available"
                            : provider.IsConfigured
                                ? "connected"
                                : "setup required"
                    })
                    .ToList();

                var msg = new
                {
                    type = "media.api.status",
                    payload = new
                    {
                        providers,
                        configuredCount = providers.Count(provider => provider.isConfigured)
                    }
                };

                MusicWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
            }
            catch
            {
            }
        }

        private void PostLyricsResult(string filePath, List<LyricLine> lines)
        {
            try
            {
                if (MusicWebView?.CoreWebView2 == null) return;

                var msg = new
                {
                    type = "lyrics.result",
                    payload = new
                    {
                        filePath,
                        lines = (lines ?? new List<LyricLine>())
                            .Select(line => new { timeSeconds = line.Timestamp.TotalSeconds, text = line.Text ?? "" })
                            .ToList()
                    }
                };

                MusicWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
            }
            catch
            {
            }
        }

        private static string? FindMusicPlayerDist()
        {
            var folderNames = new[] { "Music_MediaHub", "Futuristic Music Player(1)" };

            try
            {
                // Prefer dist shipped alongside the built EXE (bin output / publish)
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var folderName in folderNames)
                {
                    var shipped = Path.Combine(baseDir, "Figma", folderName, "dist");
                    if (Directory.Exists(shipped) && File.Exists(Path.Combine(shipped, "index.html")))
                        return shipped;
                }
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
                            foreach (var folderName in folderNames)
                            {
                                var uiFolder = Directory.GetDirectories(figmaRoot, folderName, SearchOption.TopDirectoryOnly)
                                    .FirstOrDefault();
                                if (string.IsNullOrWhiteSpace(uiFolder))
                                    continue;

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
                            var uiFolder = Directory.GetDirectories(figmaRoot, "Futuristic AI Command Center (6)", SearchOption.TopDirectoryOnly)
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

        private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var uri = new Uri(e.Request.Uri);
            if (uri.Host == "local-media")
            {
                // Uri absolute path starts with / but we want the normal file path.
                var localPath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                if (File.Exists(localPath))
                {
                    // Determine mime type based on extension
                    var ext = Path.GetExtension(localPath).ToLowerInvariant();
                    var mime = "application/octet-stream";
                    if (ext == ".mp3") mime = "audio/mpeg";
                    else if (ext == ".flac") mime = "audio/flac";
                    else if (ext == ".wav") mime = "audio/wav";
                    else if (ext == ".jpg" || ext == ".jpeg") mime = "image/jpeg";
                    else if (ext == ".png") mime = "image/png";

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
                        e.Response = MusicWebView.CoreWebView2.Environment.CreateWebResourceResponse(body, 206, "Partial Content", headers);
                        return;
                    }

                    var fullHeaders =
                        $"Content-Type: {mime}\r\n" +
                        $"Accept-Ranges: bytes\r\n" +
                        $"Content-Length: {totalLen}\r\n";

                    e.Response = MusicWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                        fileStream, 200, "OK", fullHeaders);
                }
            }
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (root.TryGetProperty("type", out var typeElement))
                {
                    var type = typeElement.GetString();
                    if (type == "media.init")
                    {
                        PostLibraryUpdated();
                        PostPlaybackState();
                        PostApiStatus();
                    }
                    else if (type == "media.album.selected")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null)
                            {
                                _viewModel!.LoadAlbumDetails(album);
                                PostAlbumDetails(album);

                                _ = System.Threading.Tasks.Task.Delay(700).ContinueWith(_ =>
                                {
                                    try { Dispatcher.Invoke(() => PostAlbumDetails(album)); } catch { }
                                });

                                // Covers can be downloaded asynchronously; refresh the library shortly after so the UI picks up the new cover path.
                                _ = System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
                                {
                                    try { Dispatcher.Invoke(PostLibraryUpdated); } catch { }
                                });
                            }
                        }
                    }
                    else if (type == "play.toggle")
                    {
                        if (_viewModel?.TogglePlayPauseCommand.CanExecute(null) == true)
                            _viewModel.TogglePlayPauseCommand.Execute(null);
                    }
                    else if (type == "play.seek")
                    {
                        if (_viewModel != null && root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("seconds", out var secondsElement))
                        {
                            var seconds = secondsElement.ValueKind == JsonValueKind.Number ? secondsElement.GetDouble() : 0;
                            _viewModel.EndUserSeek(seconds);
                        }
                    }
                    else if (type == "play.volume")
                    {
                        if (_viewModel != null && root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("value", out var valueElement))
                        {
                            var value = valueElement.ValueKind == JsonValueKind.Number ? valueElement.GetDouble() : 0;
                            _viewModel.Volume = (int)Math.Max(0, Math.Min(100, value));
                        }
                    }
                    else if (type == "play.next")
                    {
                        if (_viewModel?.NextCommand.CanExecute(null) == true)
                            _viewModel.NextCommand.Execute(null);
                    }
                    else if (type == "play.prev")
                    {
                        if (_viewModel?.PreviousCommand.CanExecute(null) == true)
                            _viewModel.PreviousCommand.Execute(null);
                    }
                    else if (type == "play.shuffle")
                    {
                        if (_viewModel?.ToggleShuffleCommand.CanExecute(null) == true)
                            _viewModel.ToggleShuffleCommand.Execute(null);
                    }
                    else if (type == "play.repeat")
                    {
                        if (_viewModel?.ToggleRepeatCommand.CanExecute(null) == true)
                            _viewModel.ToggleRepeatCommand.Execute(null);
                    }
                    else if (type == "play.track")
                    {
                        if (_viewModel != null && root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("filePath", out var filePathElement))
                        {
                            var filePath = (filePathElement.GetString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                var item = _viewModel.AllItems.FirstOrDefault(candidate =>
                                    candidate != null &&
                                    string.Equals(candidate.Type, "music", StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals((candidate.FilePath ?? "").Trim(), filePath, StringComparison.OrdinalIgnoreCase));

                                if (item != null && _viewModel.PlayItemCommand.CanExecute(item))
                                    _viewModel.PlayItemCommand.Execute(item);
                            }
                        }
                    }
                    else if (type == "lyrics.get")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("filePath", out var filePathElement))
                        {
                            var filePath = (filePathElement.GetString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        var track = _viewModel?.AllItems.FirstOrDefault(candidate =>
                                            candidate != null &&
                                            string.Equals(candidate.Type, "music", StringComparison.OrdinalIgnoreCase) &&
                                            string.Equals((candidate.FilePath ?? "").Trim(), filePath, StringComparison.OrdinalIgnoreCase));

                                        var scannerItem = track?.PlaybackItem ?? new AtlasAI.MediaScanner.MediaItem
                                        {
                                            FilePath = filePath,
                                            DisplayName = Path.GetFileNameWithoutExtension(filePath),
                                            Artist = track?.Artist ?? "",
                                            Album = track?.Album ?? "",
                                            MediaType = AtlasAI.MediaScanner.MediaType.Audio
                                        };

                                        var lines = await LyricsService.Instance.GetLyricsAsync(scannerItem).ConfigureAwait(false);
                                        Dispatcher.Invoke(() => PostLyricsResult(filePath, lines));
                                    }
                                    catch
                                    {
                                    }
                                });
                            }
                        }
                    }
                    else if (type == "media.album.edit")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null) {
                                if (_viewModel?.EditAlbumInfoCommand.CanExecute(album) == true)
                                    _viewModel.EditAlbumInfoCommand.Execute(album);
                            }
                        }
                    }
                    else if (type == "media.album.play")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            var firstTrack = album?.Tracks?.FirstOrDefault();
                            if (firstTrack != null && _viewModel?.PlayItemCommand.CanExecute(firstTrack) == true)
                                _viewModel.PlayItemCommand.Execute(firstTrack);
                        }
                    }
                    else if (type == "media.album.musicbrainz")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null) {
                                _viewModel?.FixMusicAlbumsCommand?.Execute(null); 
                            }
                        }
                    }
                    else if (type == "media.album.cover")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null) {
                                var firstTrack = album.Tracks?.FirstOrDefault();
                                if (firstTrack != null && _viewModel?.SetCoverCommand.CanExecute(firstTrack) == true)
                                    _viewModel.SetCoverCommand.Execute(firstTrack);
                            }
                        }
                    }
                    else if (type == "media.album.ai_cover")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null)
                                _ = GenerateAlbumCoverAsync(album);
                        }
                    }
                    else if (type == "media.album.remove")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null && _viewModel?.RemoveAlbumCommand.CanExecute(album) == true)
                                _viewModel.RemoveAlbumCommand.Execute(album);
                        }
                    }
                    else if (type == "media.album.ai_tag")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null && _viewModel?.OptimizeAlbumCommand.CanExecute(album) == true)
                                _viewModel.OptimizeAlbumCommand.Execute(album);
                        }
                    }
                    else if (type == "media.album.refresh")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null) {
                                try { _viewModel?.ForceReloadAlbumDetails(album); } catch { _viewModel?.LoadAlbumDetails(album); }
                                _ = System.Threading.Tasks.Task.Delay(650).ContinueWith(t =>
                                {
                                    try { Dispatcher.Invoke(() => PostAlbumDetails(album)); } catch { }
                                });

                                _ = System.Threading.Tasks.Task.Delay(1800).ContinueWith(_ =>
                                {
                                    try { Dispatcher.Invoke(PostLibraryUpdated); } catch { }
                                });
                            }
                        }
                    }
                    else if (type == "media.album.open_folder")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null)
                            {
                                if (_viewModel?.OpenAlbumFolderCommand.CanExecute(album) == true)
                                    _viewModel.OpenAlbumFolderCommand.Execute(album);
                            }
                        }
                    }
                    else if (type == "media.album.playlist")
                    {
                        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            var album = _viewModel?.MusicAlbums.FirstOrDefault(a => a.SourceFolderPath == id || a.AlbumTitle == id);
                            if (album != null)
                            {
                                var folder = album.SourceFolderPath;
                                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                                {
                                    var title = string.IsNullOrWhiteSpace(album.AlbumTitle) ? "playlist" : album.AlbumTitle;
                                    var playlistPath = BuildUniquePath(folder, title, ".m3u");
                                    var lines = new System.Collections.Generic.List<string> { "#EXTM3U" };
                                    try
                                    {
                                        if (album.Tracks != null)
                                        {
                                            foreach (var t in album.Tracks)
                                            {
                                                var fp = t?.FilePath;
                                                if (string.IsNullOrWhiteSpace(fp)) continue;
                                                if (File.Exists(fp)) lines.Add(fp);
                                            }
                                        }

                                        File.WriteAllLines(playlistPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicView] WebMessage error: {ex.Message}");
            }
        }

        private void MusicAlbums_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PostLibraryUpdated();
            PostApiStatus();
        }

        private string? GetCoverImagePath(string sourceFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder)) return null;
            var ext = new[] { "cover.jpg", "folder.jpg", "album.jpg", "front.jpg", "poster.jpg", "cover.png", "folder.png", "album.png", "front.png", "poster.png" };
            foreach (var e in ext)
            {
                var p = System.IO.Path.Combine(sourceFolder, e);
                if (System.IO.File.Exists(p)) return p;
            }
            if (AtlasAI.MediaMetadata.MediaArtworkCache.TryGetCustomMusicFolderCoverCachePath(sourceFolder, out var custom) && System.IO.File.Exists(custom)) return custom;
            if (AtlasAI.MediaMetadata.MediaArtworkCache.TryGetMusicFolderCoverCachePath(sourceFolder, out var cached) && System.IO.File.Exists(cached)) return cached;
            return null;
        }

        private void PostLibraryUpdated()
        {
            if (_viewModel == null || MusicWebView?.CoreWebView2 == null) return;

            var albumsObj = _viewModel.MusicAlbums.Select(a => {
                var coverPath = GetCoverImagePath(a.SourceFolderPath);
                var primaryTrack = a.Tracks?.FirstOrDefault();
                var summaryText = BuildAlbumSummaryText(a);
                var songs = (a.Tracks ?? new ObservableCollection<AtlasAI.Views.ViewModels.MediaItem>())
                    .Select(t => new
                    {
                        id = string.IsNullOrWhiteSpace(t.FilePath) ? Guid.NewGuid().ToString() : t.FilePath,
                        title = string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(t.FilePath) : t.Title,
                        artist = string.IsNullOrWhiteSpace(t.Artist) ? (a.Artist ?? "Unknown Artist") : t.Artist,
                        album = a.AlbumTitle ?? "Unknown Album",
                        duration = string.Join(":", ((int)t.Duration.TotalSeconds / 60).ToString("0"), ((int)t.Duration.TotalSeconds % 60).ToString("00")),
                        genre = t.Genres?.FirstOrDefault() ?? "Unknown",
                        year = t.Year,
                        filePath = t.FilePath,
                        audioUrl = string.IsNullOrWhiteSpace(t.FilePath) ? "" : $"https://local-media/{Uri.EscapeDataString(t.FilePath.Replace('\\', '/'))}",
                    })
                    .ToList();

                return new
                {
                    id = a.SourceFolderPath ?? a.AlbumTitle ?? Guid.NewGuid().ToString(),
                    title = a.AlbumTitle,
                    artist = a.Artist,
                    artwork = string.IsNullOrWhiteSpace(coverPath) ? "" : $"https://local-media/{Uri.EscapeDataString(coverPath.Replace('\\', '/'))}",
                    cover = string.IsNullOrWhiteSpace(coverPath) ? "" : $"https://local-media/{Uri.EscapeDataString(coverPath.Replace('\\', '/'))}",
                    year = primaryTrack?.Year ?? 0,
                    genre = primaryTrack?.Genres?.FirstOrDefault() ?? "Unknown",
                    tracks = a.TrackCount,
                    duration = a.Tracks?.Count > 0 ? string.Join(":", (a.Tracks.Sum(t => t.Duration.TotalSeconds) / 60).ToString("0"), (a.Tracks.Sum(t => t.Duration.TotalSeconds) % 60).ToString("00")) : "0:00",
                    detailsStatusText = a.DetailsStatusText,
                    summaryText = summaryText,
                    mbRelease = BuildAlbumReleasePayload(a),
                    songs = songs,
                };
            }).ToList();

            var msg = new
            {
                type = "media.library.updated",
                payload = new { albums = albumsObj }
            };

            var json = JsonSerializer.Serialize(msg);
            MusicWebView.CoreWebView2.PostWebMessageAsJson(json);
        }

        private static string BuildAlbumSummaryText(AtlasAI.Views.ViewModels.AlbumEntry album)
        {
            var localTrackCount = album?.Tracks?.Count ?? 0;
            var summaryParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(album?.Artist) && !string.IsNullOrWhiteSpace(album?.AlbumTitle))
            {
                summaryParts.Add($"{album.Artist.Trim()} - {album.AlbumTitle.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(album?.MusicBrainzReleaseDate))
            {
                summaryParts.Add($"release date: {album.MusicBrainzReleaseDate}");
            }

            if (localTrackCount > 0)
            {
                summaryParts.Add($"{localTrackCount} local tracks loaded");
            }

            if (!string.IsNullOrWhiteSpace(album?.DetailsStatusText))
            {
                summaryParts.Add(album.DetailsStatusText.Trim());
            }

            var summaryText = string.Join(". ", summaryParts.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (!string.IsNullOrWhiteSpace(summaryText) && !summaryText.EndsWith(".", StringComparison.Ordinal))
            {
                summaryText += ".";
            }

            return summaryText;
        }

        private static object BuildAlbumReleasePayload(AtlasAI.Views.ViewModels.AlbumEntry album)
        {
            return new
            {
                id = album?.MusicBrainzReleaseId,
                date = album?.MusicBrainzReleaseDate,
                country = album?.MusicBrainzCountry,
                label = album?.MusicBrainzLabel,
                barcode = album?.MusicBrainzBarcode,
                status = album?.MusicBrainzStatus,
                packaging = album?.MusicBrainzPackaging
            };
        }

        private async Task GenerateAlbumCoverAsync(AtlasAI.Views.ViewModels.AlbumEntry album)
        {
            try
            {
                var sourceFolder = (album?.SourceFolderPath ?? "").Trim();
                if (string.IsNullOrWhiteSpace(sourceFolder)) return;

                var title = (album?.AlbumTitle ?? "Album").Trim();
                var artist = (album?.Artist ?? "Unknown Artist").Trim();
                var prompt = $"Create premium square album cover artwork for the album '{title}' by '{artist}'. No text, no lettering, no logos, no watermark. Cinematic, detailed, polished artwork suitable as a music album cover.";
                var result = await AtlasAI.Tools.ImageGeneratorTool.GenerateImageAsync(prompt).ConfigureAwait(false);
                if (!result.Success || string.IsNullOrWhiteSpace(result.ImagePath) || !File.Exists(result.ImagePath)) return;

                static void Touch(string path)
                {
                    try { File.SetLastWriteTimeUtc(path, System.DateTime.UtcNow); } catch { }
                }

                var localCoverNames = new[]
                {
                    "cover.jpg", "folder.jpg", "album.jpg", "front.jpg", "poster.jpg",
                    "cover.png", "folder.png", "album.png", "front.png", "poster.png"
                };

                var destination = localCoverNames
                    .Select(name => Path.Combine(sourceFolder, name))
                    .FirstOrDefault(File.Exists);

                if (string.IsNullOrWhiteSpace(destination))
                    destination = Path.Combine(sourceFolder, "cover.png");

                var wroteToFolder = false;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? sourceFolder);
                    File.Copy(result.ImagePath, destination, true);
                    Touch(destination);
                    wroteToFolder = true;
                }
                catch
                {
                }

                try
                {
                    var cacheDest = AtlasAI.MediaMetadata.MediaArtworkCache.GetCustomMusicFolderCoverCachePath(sourceFolder);
                    var cacheDir = Path.GetDirectoryName(cacheDest);
                    if (!string.IsNullOrWhiteSpace(cacheDir))
                        Directory.CreateDirectory(cacheDir);
                    File.Copy(result.ImagePath, cacheDest, true);
                    Touch(cacheDest);
                }
                catch
                {
                    if (!wroteToFolder) return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    PostLibraryUpdated();
                    try { PostAlbumDetails(album); } catch { }
                });
            }
            catch
            {
            }
        }

        private void PostAlbumDetails(AtlasAI.Views.ViewModels.AlbumEntry album)
        {
            if (MusicWebView?.CoreWebView2 == null) return;

            var trackList = new System.Collections.Generic.List<object>();
            
            if (album.Tracks != null && album.Tracks.Count > 0)
            {
                foreach (var t in album.Tracks)
                {
                    var title = string.IsNullOrWhiteSpace(t.Title) ? System.IO.Path.GetFileNameWithoutExtension(t.FilePath) : t.Title;
                    trackList.Add(new {
                        title = title,
                        duration = string.Join(":", ((int)t.Duration.TotalSeconds / 60).ToString("0"), ((int)t.Duration.TotalSeconds % 60).ToString("00")),
                        audioUrl = $"https://local-media/{Uri.EscapeDataString(t.FilePath.Replace('\\', '/'))}",
                        filePath = t.FilePath,
                        discNumber = t.DiscNumber,
                        trackNumber = t.TrackNumber,
                        artist = string.IsNullOrWhiteSpace(t.Artist) ? (album.Artist ?? "") : t.Artist,
                        year = t.Year,
                        genres = t.Genres
                    });
                }
            }

            var mbTracks = new System.Collections.Generic.List<object>();
            try
            {
                if (album.DetailTracks != null && album.DetailTracks.Count > 0)
                {
                    foreach (var t in album.DetailTracks)
                    {
                        mbTracks.Add(new
                        {
                            discNumber = t.DiscNumber,
                            trackNumber = t.TrackNumber,
                            trackNumberText = t.TrackNumberText,
                            title = t.Title,
                            artist = t.Artist
                        });
                    }
                }
            }
            catch
            {
            }

            var summaryParts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(album.Artist))
            {
                summaryParts.Add($"{album.Artist} album");
            }

            if (album.Tracks?.FirstOrDefault()?.Year is int trackYear && trackYear > 0)
            {
                summaryParts.Add($"released in {trackYear}");
            }

            var primaryGenre = album.Tracks?.FirstOrDefault()?.Genres?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(primaryGenre))
            {
                summaryParts.Add($"genre: {primaryGenre}");
            }

            if (!string.IsNullOrWhiteSpace(album.MusicBrainzLabel))
            {
                summaryParts.Add($"label: {album.MusicBrainzLabel}");
            }

            if (!string.IsNullOrWhiteSpace(album.MusicBrainzCountry))
            {
                summaryParts.Add($"country: {album.MusicBrainzCountry}");
            }

            var summaryText = BuildAlbumSummaryText(album);

            var msg = new
            {
                type = "media.album.details",
                payload = new
                {
                    id = album.SourceFolderPath ?? album.AlbumTitle,
                    trackList = trackList,
                    title = album.AlbumTitle,
                    artist = album.Artist,
                      year = album.Tracks?.FirstOrDefault()?.Year.ToString() ?? "Unknown",
                      genre = album.Tracks?.FirstOrDefault()?.Genres?.FirstOrDefault() ?? "Unknown",
                      tracks = album.Tracks?.Count ?? 0,
                      duration = album.Tracks != null ? string.Join(":", ((int)album.Tracks.Sum(t => t.Duration.TotalSeconds) / 60).ToString("0"), ((int)album.Tracks.Sum(t => t.Duration.TotalSeconds) % 60).ToString("00")) : "0:00",
                      detailsStatusText = album.DetailsStatusText,
                      summaryText = summaryText,
                      mbTrackList = mbTracks,
                      mbRelease = BuildAlbumReleasePayload(album)
                  }
              };

              var json = System.Text.Json.JsonSerializer.Serialize(msg);
              MusicWebView?.CoreWebView2?.PostWebMessageAsJson(json);
          }
      }
  }
