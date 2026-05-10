using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.MediaScanner;
using Microsoft.Web.WebView2.Core;
using System.Linq;

namespace AtlasAI.Modules.Downloader
{
    public partial class DownloaderHostView : UserControl
    {
        private readonly DownloadManager _manager;
        private DateTime _lastStatePostUtc = DateTime.MinValue;
        private bool _statePostScheduled;
        private double _lastPlaybackDurationSeconds;

        public DownloaderHostView()
        {
            InitializeComponent();
            _manager = DownloadManager.Instance;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            try
            {
                var svc = MediaPlaybackService.GetOrCreate();

                if (DownloaderPlayer != null)
                {
                    DownloaderPlayer.PlaybackService = svc;
                    DownloaderPlayer.PlaybackDurationChanged += (_, seconds) =>
                    {
                        try { _lastPlaybackDurationSeconds = seconds; } catch { }
                    };
                    DownloaderPlayer.PlaybackPositionChanged += (_, seconds) =>
                    {
                        try { Post("downloader.playback", new { time = seconds, duration = _lastPlaybackDurationSeconds }); } catch { }
                    };
                }

                svc.CurrentMediaChanged += (_, item) =>
                {
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (DownloaderPlayer == null) return;
                            DownloaderPlayer.Visibility = item != null ? Visibility.Visible : Visibility.Collapsed;
                            DownloaderPlayer.IsEnabled = item != null;
                            DownloaderPlayer.Opacity = 1.0;
                        });
                    }
                    catch
                    {
                    }
                };
            }
            catch
            {
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureInitializedAsync();
                try { Post("downloader.state", _manager.GetUiState()); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DownloaderHostView] Init failed: {ex.Message}");
            }
        }

        private void RestoreShellChrome_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Window.GetWindow(this) is AtlasAI.ChatWindow chatWindow)
                    chatWindow.RestoreShellChromeAndHeader();
            }
            catch
            {
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DownloaderWebView?.CoreWebView2 != null)
                    DownloaderWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            }
            catch
            {
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (DownloaderWebView.CoreWebView2 != null) return;

            await DownloaderWebView.EnsureCoreWebView2Async();

            DownloaderWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            DownloaderWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            DownloaderWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

            // Prefer the premium Figma React UI (downloader-only mode) if its dist is available.
            var figmaDist = FindFigmaDist();
            if (figmaDist != null)
            {
                DownloaderWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "atlas-ui",
                    figmaDist,
                    CoreWebView2HostResourceAccessKind.Allow);
            }
            else
            {
                // Fallback to the legacy downloader web UI.
                var webRoot = FindWebRoot();
                if (webRoot == null)
                    throw new DirectoryNotFoundException("Modules/DownloaderWeb not found.");

                DownloaderWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "downloader.local",
                    webRoot,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            DownloaderWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            await _manager.InitializeAsync();
            _manager.StateChanged += Manager_StateChanged;

            if (figmaDist != null)
            {
                long indexWriteTicks = 0;
                try
                {
                    var indexPath = Path.Combine(figmaDist, "index.html");
                    if (File.Exists(indexPath))
                        indexWriteTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
                }
                catch { }

                var v = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
                DownloaderWebView.CoreWebView2.Navigate($"https://atlas-ui/index.html?mode=downloads&v={v}");
            }
            else
            {
                DownloaderWebView.CoreWebView2.Navigate("https://downloader.local/index.html");
            }
        }

        private void Manager_StateChanged(object? sender, EventArgs e)
        {
            try
            {
                if (_statePostScheduled) return;
                _statePostScheduled = true;

                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        _statePostScheduled = false;
                        if (DownloaderWebView?.CoreWebView2 == null) return;

                        var now = DateTime.UtcNow;
                        var dt = now - _lastStatePostUtc;
                        if (dt.TotalMilliseconds < 120)
                            await Task.Delay(TimeSpan.FromMilliseconds(120 - dt.TotalMilliseconds));

                        _lastStatePostUtc = DateTime.UtcNow;
                        Post("downloader.state", _manager.GetUiState());
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

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                if (string.IsNullOrWhiteSpace(json)) return;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) return;
                var type = typeEl.GetString() ?? "";
                var payload = root.TryGetProperty("payload", out var payloadEl) ? payloadEl : default;

                switch (type)
                {
                    case "downloader.getState":
                        Post("downloader.state", _manager.GetUiState());
                        break;
                    case "downloader.settings.get":
                        Post("downloader.state", _manager.GetUiState());
                        break;
                    case "downloader.settings.set":
                        await _manager.ApplySettingsPatchAsync(payload);
                        Post("downloader.state", _manager.GetUiState());
                        break;
                    case "downloader.addUrls":
                        try
                        {
                            var provider = payload.TryGetProperty("provider", out var p2) ? (p2.GetString() ?? "Auto") : "Auto";
                            var urls = new System.Collections.Generic.List<string>();
                            if (payload.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var u in urlsEl.EnumerateArray())
                                {
                                    var s = (u.GetString() ?? "").Trim();
                                    if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
                                }
                            }
                            var (added, skipped) = _manager.AddUrlsWithResult(urls, provider);
                            Post("downloader.addUrlsResult", new { added, skipped });
                        }
                        catch
                        {
                            await _manager.AddUrlsAsync(payload);
                        }
                        Post("downloader.state", _manager.GetUiState());
                        break;
                    case "downloader.importCsv":
                        await _manager.ImportCsvAsync();
                        Post("downloader.state", _manager.GetUiState());
                        break;
                    case "downloader.convert.pick":
                        await _manager.ConvertPickFilesAsync(payload);
                        Post("downloader.state", _manager.GetUiState());
                        break;
                    case "downloader.pause":
                        _manager.Pause(payload);
                        break;
                    case "downloader.resume":
                        _manager.Resume(payload);
                        break;
                    case "downloader.retry":
                        _manager.Retry(payload);
                        break;
                    case "downloader.cancel":
                        _manager.Cancel(payload);
                        break;
                    case "downloader.remove":
                        _manager.Remove(payload);
                        Post("downloader.state", _manager.GetUiState());
                        break;
                    case "downloader.pauseAll":
                        _manager.PauseAll();
                        break;
                    case "downloader.resumeAll":
                        _manager.ResumeAll();
                        break;
                    case "downloader.stopAll":
                        _manager.StopAll();
                        break;
                    case "downloader.retryAll":
                        _manager.RetryAll();
                        break;
                    case "downloader.clearFinished":
                        _manager.ClearFinished();
                        Post("downloader.state", _manager.GetUiState());
                        break;
                    case "downloader.openFolder":
                        _manager.OpenFolder(payload);
                        break;
                    case "downloader.openMedia":
                        _manager.OpenMedia(payload);
                        break;
                    case "downloader.provider.test":
                        var result = await _manager.TestProviderAsync(payload);
                        Post("downloader.providerStatus", result);
                        break;

                    case "downloader.playback.seek":
                        try
                        {
                            var seconds = payload.TryGetProperty("seconds", out var s2) ? s2.GetDouble() : 0;
                            Dispatcher.Invoke(() =>
                            {
                                try { DownloaderPlayer?.SeekToSeconds(Math.Max(0, seconds)); } catch { }
                            });
                        }
                        catch
                        {
                        }
                        break;

                    case "downloader.playback.stop":
                        try
                        {
                            Dispatcher.Invoke(() =>
                            {
                                try { DownloaderPlayer?.StopPlayback(); } catch { }
                            });
                        }
                        catch
                        {
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DownloaderHostView] WebMessage error: {ex.Message}");
                Post("downloader.error", new { message = ex.Message });
            }
        }

        private void Post(string type, object payload)
        {
            try
            {
                if (DownloaderWebView?.CoreWebView2 == null) return;
                var msg = JsonSerializer.Serialize(new { type, payload });
                DownloaderWebView.CoreWebView2.PostWebMessageAsJson(msg);
            }
            catch
            {
            }
        }

        private static string? FindWebRoot()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(dir); i++)
            {
                var candidate = Path.Combine(dir, "Modules", "DownloaderWeb");
                if (File.Exists(Path.Combine(candidate, "index.html")))
                    return candidate;

                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        private static string? FindFigmaDist()
        {
            try
            {
                // Prefer dist shipped alongside the built EXE (bin output / publish)
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
                            var uiFolder = Directory.GetDirectories(figmaRoot, "Futuristic AI Command Center (*)", SearchOption.TopDirectoryOnly)
                                .OrderByDescending(d => d)
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
    }
}
