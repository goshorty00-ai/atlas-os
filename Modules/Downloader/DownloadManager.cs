using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using AtlasAI.Core;
using AtlasAI.MediaScanner;
using AtlasAI.Modules.Downloader.Csv;
using AtlasAI.Modules.Downloader.Resolvers;
using AtlasAI.Modules.Downloader.Storage;
using AtlasAI.Tools;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.MediaFoundation;
using TagLibFile = TagLib.File;
using TagLibPicture = TagLib.Picture;
using TagLibByteVector = TagLib.ByteVector;
using TagLibPictureType = TagLib.PictureType;
using TagLibIPicture = TagLib.IPicture;

namespace AtlasAI.Modules.Downloader
{
    public sealed class DownloadManager
    {
        public static DownloadManager Instance { get; } = new DownloadManager();

        private readonly object _lock = new object();
        private readonly HttpClient _http;

        private readonly string _dataDir;
        private readonly string _settingsPath;
        private readonly string _jobsPath;
        private readonly ProtectedTokenStore _tokenStore;

        private DownloadSettings _settings = new DownloadSettings();
        private readonly List<DownloadJob> _jobs = new List<DownloadJob>();

        private readonly Dictionary<string, CancellationTokenSource> _jobCts = new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        private ResolverPipeline? _pipeline;
        private RealDebridResolver? _realDebrid;

        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;
        private SemaphoreSlim _parallelGate = new SemaphoreSlim(3, 3);
        private bool _initialized;

        public event EventHandler? StateChanged;

        private DownloadManager()
        {
            _http = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            })
            {
                Timeout = TimeSpan.FromMinutes(20)
            };

            _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI", "Downloader");
            Directory.CreateDirectory(_dataDir);
            _settingsPath = Path.Combine(_dataDir, "settings.json");
            _jobsPath = Path.Combine(_dataDir, "jobs.json");
            _tokenStore = new ProtectedTokenStore(Path.Combine(_dataDir, "tokens"));
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true;

            LoadSettings();
            LoadJobs();
            RebuildPipeline();

            _loopCts = new CancellationTokenSource();
            _loopTask = Task.Run(() => SchedulerLoopAsync(_loopCts.Token));

            // Check if Real-Debrid is configured
            var token = _tokenStore.Load("RealDebrid");
            if (string.IsNullOrWhiteSpace(token))
            {
                // Show config dialog on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var result = MessageBox.Show(
                        "Real-Debrid is not configured. Would you like to configure it now?\n\n" +
                        "Real-Debrid is required to download from premium file hosts like RapidGator.",
                        "Configure Real-Debrid",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        ShowRealDebridConfig();
                    }
                });
            }

            await Task.CompletedTask;
        }

        public void ShowRealDebridConfig()
        {
            try
            {
                var dialog = new RealDebridConfigDialog();
                var result = dialog.ShowDialog();
                if (result == true && !string.IsNullOrWhiteSpace(dialog.Token))
                {
                    _tokenStore.Save("RealDebrid", dialog.Token);
                    _settings.Providers.RealDebrid.Enabled = true;
                    SaveSettings();
                    RebuildPipeline();
                    MessageBox.Show("Real-Debrid token saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving token: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public object GetUiState()
        {
            lock (_lock)
            {
                static int Rank(DownloadStatus s) => s switch
                {
                    DownloadStatus.Downloading => 0,
                    DownloadStatus.Resolving => 0,
                    DownloadStatus.Converting => 0,
                    DownloadStatus.Queued => 1,
                    DownloadStatus.Paused => 2,
                    DownloadStatus.Error => 3,
                    DownloadStatus.Completed => 4,
                    DownloadStatus.Cancelled => 5,
                    _ => 9
                };

                var downloads = _jobs
                    .OrderBy(j => Rank(j.Status))
                    .ThenByDescending(j => j.CreatedUtc)
                    .Select(j => new
                    {
                        id = j.Id,
                        url = j.Url,
                        provider = j.Provider,
                        resolver = j.Resolver,
                        resolvedUrl = j.ResolvedUrl,
                        filename = j.Filename ?? GuessFileName(Uri.TryCreate(j.Url, UriKind.Absolute, out var uri) ? uri : new Uri("http://localhost")),
                        outputPath = j.OutputPath,
                        status = j.Status.ToString(),
                        progress = Math.Clamp(j.Progress * 100, 0, 100), // Convert to percentage (0-100)
                        bytesDownloaded = j.BytesDownloaded,
                        totalBytes = j.TotalBytes,
                        speedBps = j.SpeedBps,
                        etaSeconds = j.EtaSeconds,
                        error = j.Error,
                        createdUtc = j.CreatedUtc
                    })
                    .ToList();

                var safeSettings = new
                {
                    maxParallelDownloads = _settings.MaxParallelDownloads,
                    resolverMode = _settings.ResolverMode.ToString(),
                    providers = new
                    {
                        realDebrid = new { enabled = _settings.Providers.RealDebrid.Enabled },
                        allDebrid = new { enabled = _settings.Providers.AllDebrid.Enabled },
                        premiumize = new { enabled = _settings.Providers.Premiumize.Enabled }
                    }
                };

                return new
                {
                    downloads,
                    settings = safeSettings,
                    outputFolder = EnsureOutputFolder()
                };
            }
        }

        public List<DownloadJob> GetJobsSnapshot()
        {
            lock (_lock)
            {
                return _jobs.Select(j => new DownloadJob
                {
                    Id = j.Id,
                    Url = j.Url,
                    Provider = j.Provider,
                    Resolver = j.Resolver,
                    ResolvedUrl = j.ResolvedUrl,
                    Filename = j.Filename,
                    OutputPath = j.OutputPath,
                    Status = j.Status,
                    Progress = j.Progress,
                    BytesDownloaded = j.BytesDownloaded,
                    TotalBytes = j.TotalBytes,
                    SpeedBps = j.SpeedBps,
                    EtaSeconds = j.EtaSeconds,
                    Error = j.Error,
                    CreatedUtc = j.CreatedUtc
                }).ToList();
            }
        }

        public Task AddUrlsAsync(JsonElement payload)
        {
            var provider = payload.TryGetProperty("provider", out var p) ? (p.GetString() ?? "Auto") : "Auto";
            var urls = new List<string>();

            if (payload.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in urlsEl.EnumerateArray())
                {
                    var s = (u.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
                }
            }

            AddUrlsWithResult(urls, provider);
            return Task.CompletedTask;
        }

        public Task AddUrlsAsync(IEnumerable<string> urls, string provider = "Auto")
        {
            AddUrlsWithResult(urls, provider);
            return Task.CompletedTask;
        }

        public (int added, int skipped) AddUrlsWithResult(IEnumerable<string> urls, string provider = "Auto")
        {
            var list = (urls ?? Array.Empty<string>())
                .Select(u => (u ?? "").Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();

            var added = 0;
            var skipped = 0;
            lock (_lock)
            {
                foreach (var raw in list)
                {
                    var normalized = raw;
                    if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                    {
                        var guess = "https://" + normalized.Trim().TrimStart('/');
                        if (!Uri.TryCreate(guess, UriKind.Absolute, out uri))
                        {
                            skipped++;
                            continue;
                        }
                        normalized = uri.AbsoluteUri;
                    }

                    if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                    {
                        var existing = _jobs.FirstOrDefault(j => string.Equals(j.Url, normalized, StringComparison.OrdinalIgnoreCase));
                        if (existing != null && existing.Status != DownloadStatus.Cancelled && existing.Status != DownloadStatus.Error)
                        {
                            skipped++;
                            continue;
                        }

                        if (TryExtractSpotifyPlaylistId(normalized, out var playlistId))
                        {
                            _ = ImportSpotifyPlaylistAsMp3Async(playlistId);
                            added++;
                            continue;
                        }

                        _jobs.Add(new DownloadJob
                        {
                            Url = normalized,
                            Provider = string.IsNullOrWhiteSpace(provider) ? "Auto" : provider,
                            Status = DownloadStatus.Queued
                        });
                        added++;
                        continue;
                    }

                    if (uri.Scheme.Equals("atlastrack", StringComparison.OrdinalIgnoreCase))
                    {
                        var existing = _jobs.FirstOrDefault(j => string.Equals(j.Url, raw, StringComparison.OrdinalIgnoreCase));
                        if (existing != null && existing.Status != DownloadStatus.Cancelled && existing.Status != DownloadStatus.Error)
                        {
                            skipped++;
                            continue;
                        }

                        _jobs.Add(new DownloadJob
                        {
                            Url = raw,
                            Provider = "Auto",
                            Status = DownloadStatus.Queued,
                            TranscodeToMp3 = true
                        });
                        added++;
                        continue;
                    }

                    if (uri.Scheme.Equals("magnet", StringComparison.OrdinalIgnoreCase))
                    {
                        var existing = _jobs.FirstOrDefault(j => string.Equals(j.Url, raw, StringComparison.OrdinalIgnoreCase));
                        if (existing != null && existing.Status != DownloadStatus.Cancelled && existing.Status != DownloadStatus.Error)
                        {
                            skipped++;
                            continue;
                        }

                        _jobs.Add(new DownloadJob
                        {
                            Url = raw,
                            Provider = "Auto",
                            Status = DownloadStatus.Queued
                        });
                        added++;
                        continue;
                    }

                    skipped++;
                }
                PersistJobs();
            }

            if (added > 0) RaiseStateChanged();
            return (added, skipped);
        }

        public Task AddUrlAsync(string url, string provider = "Auto") => AddUrlsAsync(new[] { url }, provider);

        public async Task ImportCsvAsync()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Import CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true
            };

            var ok = dlg.ShowDialog(Application.Current?.MainWindow);
            if (ok != true) return;

            await ImportCsvFromPathAsync(dlg.FileName);
        }

        public async Task ImportCsvFromPathAsync(string csvPath)
        {
            await InitializeAsync();
            var path = (csvPath ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!System.IO.File.Exists(path)) return;

            var tracks = new List<SpotifyExportTrack>();
            try
            {
                tracks = SpotifyExportCsvParser.Parse(path);
            }
            catch
            {
            }

            if (tracks.Count > 0)
            {
                await ImportSpotifyCsvAsMp3Async(path, tracks);
                return;
            }

            var urls = CsvImporter.ExtractUrls(path);
            if (urls.Count == 0) return;
            await AddUrlsAsync(urls, "Auto");
        }

        public async Task ConvertPickFilesAsync(JsonElement payload)
        {
            await InitializeAsync();

            var fmt = payload.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String
                ? (f.GetString() ?? "").Trim()
                : "mp3";

            var targetExt = fmt.Equals("png", StringComparison.OrdinalIgnoreCase) ? ".png"
                : fmt.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg"
                : fmt.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg"
                : ".mp3";

            var dlg = new OpenFileDialog
            {
                Title = "Convert Files",
                Filter = "All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = true
            };

            var ok = dlg.ShowDialog(Application.Current?.MainWindow);
            if (ok != true) return;

            var files = dlg.FileNames?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
            if (files.Count == 0) return;

            lock (_lock)
            {
                foreach (var p in files)
                {
                    var path = (p ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!System.IO.File.Exists(path)) continue;

                    var baseName = "";
                    try { baseName = Path.GetFileNameWithoutExtension(path) ?? ""; } catch { baseName = ""; }
                    baseName = SanitizeFileName(baseName);
                    if (string.IsNullOrWhiteSpace(baseName)) baseName = "converted";

                    var filename = baseName + targetExt;
                    var uri = new Uri(path).AbsoluteUri;

                    _jobs.Add(new DownloadJob
                    {
                        Url = uri,
                        Provider = "Local",
                        Status = DownloadStatus.Queued,
                        Filename = filename,
                        ConvertToExtension = targetExt,
                        TranscodeToMp3 = targetExt.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                    });
                }
                PersistJobs();
            }

            RaiseStateChanged();
        }

        private async Task ImportSpotifyCsvAsMp3Async(string csvPath, List<SpotifyExportTrack> tracks)
        {
            await InitializeAsync();

            var prefs = PreferencesStore.Instance.Current;
            var outFolder = EnsureOutputFolder();
            Directory.CreateDirectory(outFolder);

            var minSec = Math.Max(0, prefs.CsvMinDurationSeconds);
            var maxSec = prefs.CsvMaxDurationSeconds <= 0 ? int.MaxValue : prefs.CsvMaxDurationSeconds;
            var excludeInstrumentals = prefs.CsvExcludeInstrumentals;
            var variants = (prefs.CsvVariants ?? "").Trim();

            var jobsAdded = new List<DownloadJob>();
            var index = 1;
            var packageName = SanitizeFileName(Path.GetFileNameWithoutExtension(csvPath) ?? "playlist");
            if (string.IsNullOrWhiteSpace(packageName)) packageName = "playlist";
            var packageFolder = Path.Combine(outFolder, packageName);
            try { Directory.CreateDirectory(packageFolder); } catch { }

            lock (_lock)
            {
                foreach (var t in tracks)
                {
                    var title = (t.Title ?? "").Trim();
                    var artists = (t.Artists ?? "").Trim();
                    var album = (t.Album ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    if (excludeInstrumentals)
                    {
                        if (title.IndexOf("instrumental", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                    }

                    var durSec = t.DurationMs > 0 ? (int)Math.Round(t.DurationMs / 1000.0) : 0;
                    if (durSec > 0 && (durSec < minSec || durSec > maxSec))
                        continue;

                    var primaryArtist = artists.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? artists;
                    var baseName = $"{primaryArtist} - {title}".Trim();
                    var mp3Name = SanitizeFileName(baseName) + ".mp3";

                    var q = string.IsNullOrWhiteSpace(variants) ? $"{title} {primaryArtist}" : $"{title} {primaryArtist} {variants}";
                    var uri = BuildAtlasTrackUri(q, title, artists, album, t.Year);

                    var effectiveFolder = packageFolder;
                    try { Directory.CreateDirectory(effectiveFolder); } catch { }

                    var trackPrefix = index.ToString("D2") + " - ";
                    var prefixedName = trackPrefix + mp3Name;

                    var job = new DownloadJob
                    {
                        Url = uri,
                        Provider = "Auto",
                        Status = DownloadStatus.Queued,
                        Filename = prefixedName,
                        TranscodeToMp3 = true,
                        MetaTitle = title,
                        MetaArtists = artists,
                        MetaAlbum = album,
                        MetaYear = t.Year,
                        MetaTrackNumber = index,
                        OutputFolder = effectiveFolder
                    };
                    index++;

                    _jobs.Add(job);
                    jobsAdded.Add(job);
                }
                PersistJobs();
            }

            if (jobsAdded.Count > 0)
            {
                RaiseStateChanged();
                if (prefs.CsvGenerateM3uPlaylist)
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(csvPath);
                        var m3uPath = Path.Combine(packageFolder, SanitizeFileName(name) + ".m3u");
                        var lines = new List<string> { "#EXTM3U" };
                        foreach (var j in jobsAdded)
                        {
                            var fn = (j.Filename ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(fn)) continue;
                            var relDir = "";
                            try
                            {
                                var f = (j.OutputFolder ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(f))
                                {
                                    relDir = Path.GetRelativePath(packageFolder, f);
                                    if (relDir == ".") relDir = "";
                                }
                            }
                            catch
                            {
                                relDir = "";
                            }

                            var relPath = string.IsNullOrWhiteSpace(relDir) ? fn : Path.Combine(relDir, fn);
                            lines.Add(relPath.Replace('\\', '/'));
                        }
                        System.IO.File.WriteAllLines(m3uPath, lines);
                    }
                    catch
                    {
                    }
                }
            }

            await Task.CompletedTask;
        }

        private static bool TryExtractSpotifyPlaylistId(string url, out string playlistId)
        {
            playlistId = "";
            var s = (url ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!s.Contains("open.spotify.com/playlist", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
                var parts = (u.AbsolutePath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var idx = Array.FindIndex(parts, p => string.Equals(p, "playlist", StringComparison.OrdinalIgnoreCase));
                if (idx < 0 || idx + 1 >= parts.Length) return false;
                playlistId = parts[idx + 1].Trim();
                if (string.IsNullOrWhiteSpace(playlistId)) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task ImportSpotifyPlaylistAsMp3Async(string playlistId)
        {
            await InitializeAsync();
            playlistId = (playlistId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(playlistId)) return;

            var outFolder = EnsureOutputFolder();
            Directory.CreateDirectory(outFolder);
            var variants = "";
            try { variants = (PreferencesStore.Instance.Current.CsvVariants ?? "").Trim(); } catch { variants = ""; }

            var queries = new List<string>();
            try
            {
                queries = await SpotifyTool.GetPlaylistTrackQueriesAsync(playlistId).ConfigureAwait(false);
            }
            catch
            {
                queries = new List<string>();
            }

            if (queries.Count == 0)
            {
                RaiseStateChanged();
                return;
            }

            var packageName = SanitizeFileName("spotify_playlist_" + playlistId);
            var packageFolder = Path.Combine(outFolder, packageName);
            try { Directory.CreateDirectory(packageFolder); } catch { }

            var jobsAdded = new List<DownloadJob>();
            var index = 1;

            lock (_lock)
            {
                foreach (var q in queries)
                {
                    var raw = (q ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var (artists, title) = SplitArtistTitle(raw);
                    if (string.IsNullOrWhiteSpace(title)) title = raw;

                    var mp3Name = SanitizeFileName(string.IsNullOrWhiteSpace(artists) ? title : $"{artists} - {title}") + ".mp3";
                    var search = string.IsNullOrWhiteSpace(variants) ? raw : (raw + " " + variants);
                    var uri = BuildAtlasTrackUri(search, title, artists, album: "", year: 0);
                    var trackPrefix = index.ToString("D2") + " - ";
                    var prefixedName = trackPrefix + mp3Name;

                    var job = new DownloadJob
                    {
                        Url = uri,
                        Provider = "Auto",
                        Status = DownloadStatus.Queued,
                        Filename = prefixedName,
                        TranscodeToMp3 = true,
                        MetaTitle = title,
                        MetaArtists = artists,
                        MetaTrackNumber = index,
                        OutputFolder = packageFolder
                    };
                    index++;
                    _jobs.Add(job);
                    jobsAdded.Add(job);
                }
                PersistJobs();
            }

            try
            {
                var m3uPath = Path.Combine(packageFolder, "playlist.m3u");
                var lines = new List<string> { "#EXTM3U" };
                foreach (var j in jobsAdded)
                {
                    var fn = (j.Filename ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(fn)) continue;
                    var relDir = "";
                    try
                    {
                        var f = (j.OutputFolder ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(f))
                        {
                            relDir = Path.GetRelativePath(packageFolder, f);
                            if (relDir == ".") relDir = "";
                        }
                    }
                    catch
                    {
                        relDir = "";
                    }
                    var relPath = string.IsNullOrWhiteSpace(relDir) ? fn : Path.Combine(relDir, fn);
                    lines.Add(relPath.Replace('\\', '/'));
                }
                System.IO.File.WriteAllLines(m3uPath, lines);
            }
            catch
            {
            }

            RaiseStateChanged();
        }

        private static (string artists, string title) SplitArtistTitle(string query)
        {
            var s = (query ?? "").Trim();
            var idx = s.IndexOf(" - ", StringComparison.OrdinalIgnoreCase);
            if (idx <= 0) return ("", s);
            var a = s.Substring(0, idx).Trim();
            var t = s.Substring(idx + 3).Trim();
            return (a, t);
        }

        private static string BuildAtlasTrackUri(string q, string title, string artists, string album, int year)
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(q)) query.Add("q=" + Uri.EscapeDataString(q));
            if (!string.IsNullOrWhiteSpace(title)) query.Add("title=" + Uri.EscapeDataString(title));
            if (!string.IsNullOrWhiteSpace(artists)) query.Add("artists=" + Uri.EscapeDataString(artists));
            if (!string.IsNullOrWhiteSpace(album)) query.Add("album=" + Uri.EscapeDataString(album));
            if (year > 0) query.Add("year=" + Uri.EscapeDataString(year.ToString()));
            return "atlastrack://track?" + string.Join("&", query);
        }

        public void Pause(JsonElement payload) => WithJobId(payload, id => PauseById(id));
        public void Resume(JsonElement payload) => WithJobId(payload, id => ResumeById(id));
        public void Retry(JsonElement payload) => WithJobId(payload, id => RetryById(id));
        public void Cancel(JsonElement payload) => WithJobId(payload, id => CancelById(id));
        public void Remove(JsonElement payload) => WithJobId(payload, id => RemoveById(id));
        public void OpenFolder(JsonElement payload) => WithJobId(payload, id => OpenFolderById(id));
        public void OpenMedia(JsonElement payload) => WithJobId(payload, id => OpenMediaById(id));

        public void OpenOutputFolder()
        {
            try
            {
                var folder = EnsureOutputFolder();
                TryOpenFolder(folder);
            }
            catch
            {
            }
        }

        public void PauseAll()
        {
            List<string> ids;
            lock (_lock) ids = _jobs.Select(j => j.Id).ToList();
            foreach (var id in ids) PauseById(id);
        }

        public void ResumeAll()
        {
            List<string> ids;
            lock (_lock) ids = _jobs.Select(j => j.Id).ToList();
            foreach (var id in ids) ResumeById(id);
        }

        public void StopAll()
        {
            List<string> ids;
            lock (_lock) ids = _jobs.Select(j => j.Id).ToList();
            foreach (var id in ids) CancelById(id);
        }

        public void RetryAll()
        {
            lock (_lock)
            {
                foreach (var job in _jobs)
                {
                    if (job.Status == DownloadStatus.Error || job.Status == DownloadStatus.Cancelled)
                    {
                        job.Status = DownloadStatus.Queued;
                        job.Error = null;
                        job.NextAttemptUtc = null;
                        job.InFlight = false;
                        job.Attempts = 0;
                        job.RequestStop = false;
                        job.SpeedBps = 0;
                        job.EtaSeconds = 0;
                    }
                    else if (job.Status == DownloadStatus.Queued && job.NextAttemptUtc.HasValue)
                    {
                        // Clear retry timers for queued jobs waiting to retry
                        job.NextAttemptUtc = null;
                        job.Error = null;
                    }
                }
                PersistJobs();
            }
            RaiseStateChanged();
        }

        public void ClearFinished()
        {
            lock (_lock)
            {
                _jobs.RemoveAll(j => j.Status == DownloadStatus.Completed || j.Status == DownloadStatus.Cancelled || j.Status == DownloadStatus.Error);
                PersistJobs();
            }
            RaiseStateChanged();
        }

        public async Task ApplySettingsPatchAsync(JsonElement payload)
        {
            lock (_lock)
            {
                if (payload.TryGetProperty("maxParallelDownloads", out var mp) && mp.ValueKind == JsonValueKind.Number)
                {
                    var v = mp.GetInt32();
                    _settings.MaxParallelDownloads = Math.Clamp(v, 1, 12);
                }

                if (payload.TryGetProperty("resolverMode", out var rm) && rm.ValueKind == JsonValueKind.String)
                {
                    var s = rm.GetString() ?? "Auto";
                    _settings.ResolverMode = s.Equals("ForcedProvider", StringComparison.OrdinalIgnoreCase) ? ResolverMode.ForcedProvider : ResolverMode.Auto;
                }

                if (payload.TryGetProperty("providers", out var prov) && prov.ValueKind == JsonValueKind.Object)
                {
                    ApplyProviderPatch(prov);
                }

                SaveSettings();
                RebuildPipeline();
                _parallelGate = new SemaphoreSlim(_settings.MaxParallelDownloads, _settings.MaxParallelDownloads);
            }

            RaiseStateChanged();
            await Task.CompletedTask;
        }

        public async Task<object> TestProviderAsync(JsonElement payload)
        {
            var provider = payload.TryGetProperty("provider", out var p) ? (p.GetString() ?? "") : "";
            if (provider.Equals("RealDebrid", StringComparison.OrdinalIgnoreCase) && _realDebrid != null)
            {
                var (ok, message) = await _realDebrid.TestTokenAsync(CancellationToken.None);
                return new { provider = "RealDebrid", ok, message };
            }
            if (provider.Equals("AllDebrid", StringComparison.OrdinalIgnoreCase))
                return new { provider = "AllDebrid", ok = false, message = "Scaffold only (phase 1)." };
            if (provider.Equals("Premiumize", StringComparison.OrdinalIgnoreCase))
                return new { provider = "Premiumize", ok = false, message = "Scaffold only (phase 1)." };
            return new { provider = provider, ok = false, message = "Unknown provider." };
        }

        private void ApplyProviderPatch(JsonElement providersObj)
        {
            if (providersObj.TryGetProperty("realDebrid", out var rd) && rd.ValueKind == JsonValueKind.Object)
            {
                if (rd.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
                    _settings.Providers.RealDebrid.Enabled = en.GetBoolean();

                if (rd.TryGetProperty("token", out var tk) && tk.ValueKind == JsonValueKind.String)
                {
                    var token = (tk.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                        _tokenStore.Save("RealDebrid", token);
                }
            }

            if (providersObj.TryGetProperty("allDebrid", out var ad) && ad.ValueKind == JsonValueKind.Object)
            {
                if (ad.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
                    _settings.Providers.AllDebrid.Enabled = en.GetBoolean();

                if (ad.TryGetProperty("token", out var tk) && tk.ValueKind == JsonValueKind.String)
                {
                    var token = (tk.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                        _tokenStore.Save("AllDebrid", token);
                }
            }

            if (providersObj.TryGetProperty("premiumize", out var pm) && pm.ValueKind == JsonValueKind.Object)
            {
                if (pm.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
                    _settings.Providers.Premiumize.Enabled = en.GetBoolean();

                if (pm.TryGetProperty("token", out var tk) && tk.ValueKind == JsonValueKind.String)
                {
                    var token = (tk.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                        _tokenStore.Save("Premiumize", token);
                }
            }
        }

        private async Task SchedulerLoopAsync(CancellationToken ct)
        {
            System.Diagnostics.Debug.WriteLine("[DownloadManager] Scheduler loop started");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(250, ct);
                    while (_parallelGate.CurrentCount > 0)
                    {
                        var next = DequeueNextEligibleJob();
                        if (next == null) break;
                        System.Diagnostics.Debug.WriteLine($"[DownloadManager] Starting job: {next.Id} - {next.Url}");
                        _ = RunJobAsync(next, ct);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DownloadManager] Scheduler error: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine("[DownloadManager] Scheduler loop stopped");
        }

        private DownloadJob? DequeueNextEligibleJob()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var queuedCount = _jobs.Count(j => j.Status == DownloadStatus.Queued);
                System.Diagnostics.Debug.WriteLine($"[DownloadManager] Checking queue: {queuedCount} queued jobs");
                
                var job = _jobs
                    .Where(j => j.Status == DownloadStatus.Queued)
                    .Where(j => !j.InFlight)
                    .Where(j => !j.NextAttemptUtc.HasValue || j.NextAttemptUtc.Value <= now)
                    .OrderBy(j => j.CreatedUtc)
                    .FirstOrDefault();
                    
                if (job == null)
                {
                    if (queuedCount > 0)
                    {
                        var blockedJob = _jobs.FirstOrDefault(j => j.Status == DownloadStatus.Queued);
                        if (blockedJob != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[DownloadManager] Job blocked: InFlight={blockedJob.InFlight}, NextAttempt={blockedJob.NextAttemptUtc}");
                        }
                    }
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"[DownloadManager] Dequeuing job: {job.Id}");
                job.InFlight = true;
                job.Status = DownloadStatus.Resolving;
                job.Error = null;
                // Only clear resolved URL for fresh jobs (no ResolvedUrl yet).
                // Paused/resumed jobs keep their cached link to avoid costly re-resolution.
                if (!HasResumePart(job) && string.IsNullOrWhiteSpace(job.ResolvedUrl))
                {
                    job.Resolver = null;
                    job.ResolvedUrl = null;
                }
                job.SpeedBps = 0;
                job.EtaSeconds = 0;
                job.NextAttemptUtc = null;
                PersistJobs();
                return job;
            }
        }

        private bool HasResumePart(DownloadJob job)
        {
            try
            {
                if (job == null) return false;
                var folder = !string.IsNullOrWhiteSpace(job.OutputFolder) ? job.OutputFolder!.Trim() : EnsureOutputFolder();
                if (string.IsNullOrWhiteSpace(folder)) return false;
                if (!Directory.Exists(folder)) return false;

                var name = (job.Filename ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) return false;

                var final = Path.Combine(folder, SanitizeFileName(name));
                var dlExt = (job.DownloadExtension ?? "").Trim();
                if (string.IsNullOrWhiteSpace(dlExt))
                {
                    dlExt = Path.GetExtension(final);
                }
                var download = job.TranscodeToMp3 && !string.IsNullOrWhiteSpace(dlExt)
                    ? Path.Combine(folder, Path.GetFileNameWithoutExtension(final) + dlExt)
                    : final;
                var part = download + ".part";
                if (!File.Exists(part)) return false;
                return new FileInfo(part).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task RunJobAsync(DownloadJob job, CancellationToken loopToken)
        {
            await _parallelGate.WaitAsync(loopToken);
            CancellationTokenSource? cts = null;
            CancellationTokenSource? linked = null;
            try
            {
                cts = new CancellationTokenSource();
                lock (_lock) _jobCts[job.Id] = cts;

                linked = CancellationTokenSource.CreateLinkedTokenSource(loopToken, cts.Token);
                await ExecuteJobAsync(job, linked.Token);
            }
            catch (OperationCanceledException)
            {
                var j = FindJob(job.Id);
                if (j != null)
                {
                    UpdateJob(j, x =>
                    {
                        x.Status = DownloadStatus.Paused;
                        x.SpeedBps = 0;
                        x.EtaSeconds = 0;
                        x.InFlight = false;
                    });
                    PersistJobs();
                }
            }
            catch (Exception ex)
            {
                var j = FindJob(job.Id);
                if (j != null)
                {
                    var isTransient = ex is HttpRequestException || ex is IOException || ex is TaskCanceledException;
                    var isExpiredLink = ex.Message.Contains("HTML instead of file content") || 
                                       ex.Message.Contains("403") || 
                                       ex.Message.Contains("401") ||
                                       ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase);
                    
                    var attempt = 0;
                    lock (_lock)
                    {
                        j.Attempts++;
                        attempt = j.Attempts;
                        
                        // Clear resolved URL if link expired so it gets re-resolved on retry
                        if (isExpiredLink && !string.IsNullOrWhiteSpace(j.ResolvedUrl))
                        {
                            j.ResolvedUrl = null;
                            j.Resolver = null;
                        }
                    }

                    if ((isTransient || isExpiredLink) && attempt <= 5)
                    {
                        var delaySeconds = Math.Min(120, (int)Math.Pow(2, Math.Max(0, attempt - 1)) * 2);
                        UpdateJob(j, x =>
                        {
                            x.Status = DownloadStatus.Queued;
                            x.Error = $"Retrying in {delaySeconds}s: {ex.Message}";
                            x.NextAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
                            x.SpeedBps = 0;
                            x.EtaSeconds = 0;
                            x.InFlight = false;
                        });
                    }
                    else
                    {
                        UpdateJob(j, x =>
                        {
                            x.Status = DownloadStatus.Error;
                            x.Error = ex.Message;
                            x.SpeedBps = 0;
                            x.EtaSeconds = 0;
                            x.InFlight = false;
                        });
                    }
                    PersistJobs();
                }
            }
            finally
            {
                try { linked?.Dispose(); } catch { }
                try { cts?.Dispose(); } catch { }
                lock (_lock) _jobCts.Remove(job.Id);
                _parallelGate.Release();
                RaiseStateChanged();
            }
        }

        private async Task ExecuteJobAsync(DownloadJob job, CancellationToken ct)
        {
            Uri input;
            if (!Uri.TryCreate(job.Url, UriKind.Absolute, out input!))
            {
                UpdateJob(job, j => { j.Status = DownloadStatus.Error; j.Error = "Invalid URL"; });
                UpdateJob(job, j => j.InFlight = false);
                return;
            }

            var rootFolder = EnsureOutputFolder();
            Directory.CreateDirectory(rootFolder);

            // Only create individual folders for specific cases (playlists, packages, etc.)
            // For regular downloads, use the root folder directly
            if (string.IsNullOrWhiteSpace(job.OutputFolder))
            {
                // Check if this is a package/playlist download (has MetaTrackNumber or explicit OutputFolder requirement)
                var needsSubfolder = job.MetaTrackNumber > 0 || job.TranscodeToMp3;
                
                if (needsSubfolder && !string.IsNullOrWhiteSpace(job.Filename))
                {
                    var seed = Path.GetFileNameWithoutExtension(job.Filename);
                    var desired = SanitizeFileName((seed ?? "").Trim());
                    if (string.IsNullOrWhiteSpace(desired)) desired = job.Id;
                    var folder = GetUniqueFolder(rootFolder, desired, job.Id);
                    UpdateJob(job, j => j.OutputFolder = folder);
                    PersistJobs();
                }
                else
                {
                    // Use root folder for regular downloads
                    UpdateJob(job, j => j.OutputFolder = rootFolder);
                    PersistJobs();
                }
            }

            var outFolder = !string.IsNullOrWhiteSpace(job.OutputFolder) ? job.OutputFolder!.Trim() : rootFolder;
            Directory.CreateDirectory(outFolder);

            if (input.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await ConvertLocalFileAsync(job, input, outFolder, ct);
                }
                catch (OperationCanceledException)
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Paused;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }
                catch (Exception ex)
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Error;
                        j.Error = ex.Message;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }

                UpdateJob(job, j => j.InFlight = false);
                PersistJobs();
                RaiseStateChanged();
                return;
            }

            if (input.Scheme.Equals("atlastrack", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await DownloadAtlasTrackWithYtDlpAsync(job, input, outFolder, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Paused;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }
                catch (Exception ex)
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Error;
                        j.Error = ex.Message;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }

                UpdateJob(job, j => j.InFlight = false);
                PersistJobs();
                RaiseStateChanged();
                return;
            }

            if (IsSoundCloud(input))
            {
                try
                {
                    await DownloadUrlWithYtDlpMp3Async(job, input, outFolder, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Paused;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }
                catch (Exception ex)
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Error;
                        j.Error = ex.Message;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }

                UpdateJob(job, j => j.InFlight = false);
                PersistJobs();
                RaiseStateChanged();
                return;
            }

            if (IsYtDlpPreferred(input))
            {
                try
                {
                    await DownloadUrlWithYtDlpMp3Async(job, input, outFolder, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Paused;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }
                catch (Exception ex)
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Error;
                        j.Error = ex.Message;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }

                UpdateJob(job, j => j.InFlight = false);
                PersistJobs();
                RaiseStateChanged();
                return;
            }

            var forced = job.Provider switch
            {
                "RealDebrid" => "RealDebrid",
                "AllDebrid" => "AllDebrid",
                "Premiumize" => "Premiumize",
                "DirectHttp" => "DirectHttp",
                _ => null
            };

            var mp3Name = (job.Filename ?? "").Trim();
            var hasName = !string.IsNullOrWhiteSpace(mp3Name);

            var targetFinalPath = hasName ? Path.Combine(outFolder, SanitizeFileName(mp3Name)) : "";
            var resumePartExists = false;
            if (hasName)
            {
                var dlExt = (job.DownloadExtension ?? "").Trim();
                var downloadPath = string.IsNullOrWhiteSpace(dlExt)
                    ? targetFinalPath
                    : Path.Combine(outFolder, Path.GetFileNameWithoutExtension(targetFinalPath) + dlExt);
                var partPath = downloadPath + ".part";
                try { resumePartExists = File.Exists(partPath) && new FileInfo(partPath).Length > 0; } catch { }
            }

            ResolvedLink resolved;
            Uri? storedResolvedUri = null;
            var canReuseResolved = resumePartExists &&
                                   !string.IsNullOrWhiteSpace(job.ResolvedUrl) &&
                                   Uri.TryCreate(job.ResolvedUrl, UriKind.Absolute, out storedResolvedUri);

            if (canReuseResolved)
            {
                resolved = new ResolvedLink
                {
                    DirectUrl = storedResolvedUri!,
                    ResolverName = string.IsNullOrWhiteSpace(job.Resolver) ? "DirectHttp" : job.Resolver!,
                    SuggestedFilename = job.Filename
                };
            }
            else
            {
                var pipeline = _pipeline ?? new ResolverPipeline(new ILinkResolver[] { new DirectHttpResolver() });
                var mode = job.Provider == "Auto" ? _settings.ResolverMode : ResolverMode.ForcedProvider;
                resolved = await pipeline.ResolveAsync(input, mode, forced, ct);

                if (mode == ResolverMode.ForcedProvider &&
                    !string.IsNullOrWhiteSpace(forced) &&
                    !string.Equals(forced, "DirectHttp", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(resolved.ResolverName, "DirectHttp", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Error;
                        j.Error = $"{forced} could not resolve this link.";
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }

                UpdateJob(job, j =>
                {
                    j.Resolver = resolved.ResolverName;
                    j.ResolvedUrl = resolved.DirectUrl.ToString();
                    if (!string.IsNullOrWhiteSpace(resolved.ArtworkUrl) && string.IsNullOrWhiteSpace(j.MetaArtworkUrl))
                        j.MetaArtworkUrl = resolved.ArtworkUrl;
                    if (!string.IsNullOrWhiteSpace(resolved.TrackTitle) && string.IsNullOrWhiteSpace(j.MetaTitle))
                        j.MetaTitle = resolved.TrackTitle;
                    if (!string.IsNullOrWhiteSpace(resolved.TrackArtists) && string.IsNullOrWhiteSpace(j.MetaArtists))
                        j.MetaArtists = resolved.TrackArtists;
                    if (!string.IsNullOrWhiteSpace(resolved.Album) && string.IsNullOrWhiteSpace(j.MetaAlbum))
                        j.MetaAlbum = resolved.Album;
                    if (j.MetaYear <= 0 && resolved.Year > 0)
                        j.MetaYear = resolved.Year;
                });
                PersistJobs();
            }

            if (!resolved.DirectUrl.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                !resolved.DirectUrl.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                UpdateJob(job, j =>
                {
                    j.Status = DownloadStatus.Error;
                    j.Error = input.Scheme.Equals("atlastrack", StringComparison.OrdinalIgnoreCase)
                        ? "No match found for this track."
                        : $"No resolver produced a downloadable URL for '{input.Scheme}'.";
                    j.InFlight = false;
                });
                PersistJobs();
                return;
            }

            UpdateJob(job, j => j.Status = DownloadStatus.Downloading);

            var effectiveTitleName = (job.Filename ?? "").Trim();
            if (string.IsNullOrWhiteSpace(effectiveTitleName))
            {
                // Try to use the suggested filename from the resolver first
                var suggestedName = (resolved.SuggestedFilename ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(suggestedName))
                {
                    var unique = GetUniquePaths(outFolder, SanitizeFileName(suggestedName), job.Id);
                    UpdateJob(job, j => j.Filename = unique.fileName);
                    PersistJobs();
                    effectiveTitleName = unique.fileName;
                }
                else
                {
                    // Fall back to guessing from URL
                    var guess = GuessFileName(resolved.DirectUrl);
                    var unique = GetUniquePaths(outFolder, SanitizeFileName(guess), job.Id);
                    UpdateJob(job, j => j.Filename = unique.fileName);
                    PersistJobs();
                    effectiveTitleName = unique.fileName;
                }
            }

            var finalOutputPath = Path.Combine(outFolder, SanitizeFileName(effectiveTitleName));
            var downloadExt2 = (job.DownloadExtension ?? "").Trim();
            if (string.IsNullOrWhiteSpace(downloadExt2))
            {
                // First, try to get extension from the filename if it exists
                if (!string.IsNullOrWhiteSpace(effectiveTitleName))
                {
                    var filenameExt = Path.GetExtension(effectiveTitleName);
                    if (!string.IsNullOrWhiteSpace(filenameExt))
                    {
                        downloadExt2 = filenameExt;
                    }
                }
                
                // If still no extension, try from the resolved URL (but filter out invalid ones)
                if (string.IsNullOrWhiteSpace(downloadExt2))
                {
                    try
                    {
                        var e = Path.GetExtension(resolved.DirectUrl.AbsolutePath);
                        if (!string.IsNullOrWhiteSpace(e))
                        {
                            // Ignore web-related extensions that aren't actual file types
                            var ext = e.ToLowerInvariant();
                            if (ext != ".html" && ext != ".htm" && ext != ".php" && ext != ".asp" && ext != ".aspx" && ext != ".jsp")
                            {
                                downloadExt2 = e;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                
                if (job.TranscodeToMp3 && string.IsNullOrWhiteSpace(downloadExt2))
                    downloadExt2 = ".m4a";

                if (!string.IsNullOrWhiteSpace(downloadExt2))
                {
                    UpdateJob(job, j => j.DownloadExtension = downloadExt2);
                    PersistJobs();
                }
            }

            var downloadOutputPath = job.TranscodeToMp3
                ? Path.Combine(outFolder, Path.GetFileNameWithoutExtension(finalOutputPath) + downloadExt2)
                : finalOutputPath;

            var downloadPartPath = downloadOutputPath + ".part";

            if (File.Exists(finalOutputPath) && !File.Exists(downloadPartPath) && !job.TranscodeToMp3)
            {
                try
                {
                    var len = new FileInfo(finalOutputPath).Length;
                    UpdateJob(job, j =>
                    {
                        j.OutputPath = finalOutputPath;
                        j.TotalBytes = len;
                        j.BytesDownloaded = len;
                        j.Progress = 1;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.Status = DownloadStatus.Completed;
                        j.InFlight = false;
                    });
                    PersistJobs();
                    return;
                }
                catch
                {
                }
            }

            UpdateJob(job, j => j.OutputPath = finalOutputPath);
            PersistJobs();

            await DownloadToFileAsync(job, resolved, downloadPartPath, downloadOutputPath, finalOutputPath, ct);
            UpdateJob(job, j => j.InFlight = false);
        }

        private async Task DownloadToFileAsync(DownloadJob job, ResolvedLink link, string partPath, string downloadPath, string finalPath, CancellationToken ct)
        {
            var existing = 0L;
            try
            {
                if (File.Exists(partPath))
                    existing = new FileInfo(partPath).Length;
            }
            catch { }

            if (existing > 0)
            {
                UpdateJob(job, j =>
                {
                    j.BytesDownloaded = existing;
                    if (j.TotalBytes > 0)
                        j.Progress = Math.Clamp(existing / (double)j.TotalBytes, 0, 1);
                });
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, link.DirectUrl);
            if (existing > 0)
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

            if (link.Headers != null)
            {
                foreach (var kv in link.Headers)
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (existing > 0 && resp.StatusCode == System.Net.HttpStatusCode.OK)
            {
                // Server doesn't support Range/resume — restart from beginning but keep the .part path.
                existing = 0;
                UpdateJob(job, j => { j.BytesDownloaded = 0; j.Progress = 0; });
            }

            resp.EnsureSuccessStatusCode();

            // Check if we're getting HTML instead of the actual file (before downloading)
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                var preview = "";
                try
                {
                    preview = await resp.Content.ReadAsStringAsync(ct);
                }
                catch
                {
                    preview = "(unable to read response)";
                }
                
                var errorMsg = preview.Length > 200 ? preview.Substring(0, 200) + "..." : preview;
                
                // Check if this is a Real-Debrid error
                if (job.Resolver?.Contains("RealDebrid", StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new Exception($"Real-Debrid returned an error page instead of the file. The link may have expired, or your Real-Debrid account may need attention. Error: {errorMsg}");
                }
                
                throw new Exception($"Server returned HTML instead of file content. This usually means the link expired or requires authentication. Preview: {errorMsg}");
            }

            // Try to extract filename from Content-Disposition header
            if (resp.Content.Headers.ContentDisposition?.FileName != null)
            {
                try
                {
                    var headerFilename = resp.Content.Headers.ContentDisposition.FileName.Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(headerFilename))
                    {
                        headerFilename = Uri.UnescapeDataString(headerFilename);
                        headerFilename = SanitizeFileName(headerFilename);
                        
                        // Update the job with the proper filename from the server
                        var currentFilename = (job.Filename ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(currentFilename) || 
                            currentFilename.StartsWith("download", StringComparison.OrdinalIgnoreCase) ||
                            currentFilename.Contains("_download.bin"))
                        {
                            var outFolder = !string.IsNullOrWhiteSpace(job.OutputFolder) ? job.OutputFolder!.Trim() : EnsureOutputFolder();
                            var unique = GetUniquePaths(outFolder, headerFilename, job.Id);
                            
                            UpdateJob(job, j => j.Filename = unique.fileName);
                            PersistJobs();
                            
                            // Update the paths with the new filename
                            finalPath = unique.finalPath;
                            var downloadExt = (job.DownloadExtension ?? "").Trim();
                            downloadPath = job.TranscodeToMp3 && !string.IsNullOrWhiteSpace(downloadExt)
                                ? Path.Combine(outFolder, Path.GetFileNameWithoutExtension(finalPath) + downloadExt)
                                : finalPath;
                            partPath = downloadPath + ".part";
                        }
                    }
                }
                catch
                {
                    // If extraction fails, continue with existing filename
                }
            }

            var rangeTotal = resp.Content.Headers.ContentRange?.Length;
            var contentLen = resp.Content.Headers.ContentLength;
            long expected;
            if (rangeTotal.HasValue)
                expected = rangeTotal.Value;
            else if (resp.StatusCode == System.Net.HttpStatusCode.PartialContent && contentLen.HasValue)
                expected = contentLen.Value + existing;
            else
                expected = contentLen ?? 0;
            
            // Warn if file is suspiciously small (likely an error page)
            if (expected > 0 && expected < 10000 && !contentType.Contains("image", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[DownloadManager] Warning: Expected size is only {expected} bytes. This might be an error page.");
            }
            
            UpdateJob(job, j => j.TotalBytes = expected);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);
            var mode = existing > 0 ? FileMode.Append : FileMode.Create;
            
            // Download to part file in a separate scope to ensure file is closed before moving
            {
                await using var file = new FileStream(partPath, mode, FileAccess.Write, FileShare.Read, 1024 * 256, useAsync: true);

                var buf = new byte[1024 * 128];
                var lastTick = Stopwatch.GetTimestamp();
                var lastBytes = existing;

                while (true)
                {
                    var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct);
                    if (read <= 0) break;
                    await file.WriteAsync(buf.AsMemory(0, read), ct);
                    existing += read;

                    var nowTick = Stopwatch.GetTimestamp();
                    var seconds = (nowTick - lastTick) / (double)Stopwatch.Frequency;
                    if (seconds >= 0.4)
                    {
                        var delta = existing - lastBytes;
                        var instant = delta / seconds;
                        lastTick = nowTick;
                        lastBytes = existing;

                        UpdateJob(job, j =>
                        {
                            j.BytesDownloaded = existing;
                            j.Progress = j.TotalBytes > 0 ? Math.Clamp(existing / (double)j.TotalBytes, 0, 1) : 0;
                            j.SmoothedSpeedBps = j.SmoothedSpeedBps <= 0 ? instant : (j.SmoothedSpeedBps * 0.75 + instant * 0.25);
                            j.SpeedBps = j.SmoothedSpeedBps;
                            if (j.TotalBytes > 0 && j.SpeedBps > 1)
                            {
                                var remain = Math.Max(0, j.TotalBytes - existing);
                                j.EtaSeconds = remain / j.SpeedBps;
                            }
                            j.LastProgressUtc = DateTime.UtcNow;
                        });
                    }
                }

                try 
                { 
                    await file.FlushAsync(ct);
                } 
                catch { }
            } // File stream is disposed here

            if (ct.IsCancellationRequested)
            {
                UpdateJob(job, j => { j.Status = DownloadStatus.Paused; j.SpeedBps = 0; j.EtaSeconds = 0; });
                PersistJobs();
                return;
            }

            // File stream is now closed, safe to move the file
            try
            {
                if (File.Exists(downloadPath)) File.Delete(downloadPath);
                File.Move(partPath, downloadPath);
            }
            catch (Exception ex)
            {
                UpdateJob(job, j => { j.Status = DownloadStatus.Error; j.Error = ex.Message; });
                PersistJobs();
                return;
            }

            if (!string.Equals(downloadPath, finalPath, StringComparison.OrdinalIgnoreCase) && job.TranscodeToMp3)
            {
                try
                {
                    UpdateJob(job, j =>
                    {
                        j.Status = DownloadStatus.Converting;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                    });

                    await TranscodeAndTagAsync(job, downloadPath, finalPath, ct);
                    try { if (File.Exists(downloadPath)) File.Delete(downloadPath); } catch { }
                }
                catch (Exception ex)
                {
                    UpdateJob(job, j => { j.Status = DownloadStatus.Error; j.Error = ex.Message; });
                    PersistJobs();
                    return;
                }
            }

            UpdateJob(job, j =>
            {
                j.OutputPath = finalPath;
                try
                {
                    var fi = new FileInfo(finalPath);
                    if (fi.Exists)
                    {
                        j.TotalBytes = fi.Length;
                        j.BytesDownloaded = fi.Length;
                    }
                    else
                    {
                        j.BytesDownloaded = j.TotalBytes > 0 ? j.TotalBytes : existing;
                    }
                }
                catch
                {
                    j.BytesDownloaded = j.TotalBytes > 0 ? j.TotalBytes : existing;
                }
                j.Progress = 1;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
                j.Status = DownloadStatus.Completed;
            });

            PersistJobs();
            RaiseStateChanged();
        }

        private async Task ConvertLocalFileAsync(DownloadJob job, Uri input, string outFolder, CancellationToken ct)
        {
            var sourcePath = input.LocalPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !System.IO.File.Exists(sourcePath))
                throw new FileNotFoundException("Source file not found.", sourcePath);

            var filename = (job.Filename ?? "").Trim();
            if (string.IsNullOrWhiteSpace(filename))
            {
                var baseName = Path.GetFileNameWithoutExtension(sourcePath);
                var ext = (job.ConvertToExtension ?? "").Trim();
                if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";
                filename = SanitizeFileName(baseName) + ext;
                UpdateJob(job, j => j.Filename = filename);
                PersistJobs();
            }

            var finalPath = Path.Combine(outFolder, SanitizeFileName(filename));
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            UpdateJob(job, j =>
            {
                j.OutputPath = finalPath;
                j.Progress = 0;
                j.Status = DownloadStatus.Converting;
                j.Error = null;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
            });
            PersistJobs();

            var targetExt = (job.ConvertToExtension ?? "").Trim();
            if (string.IsNullOrWhiteSpace(targetExt))
            {
                targetExt = Path.GetExtension(finalPath);
            }
            targetExt = targetExt.StartsWith(".") ? targetExt : "." + targetExt;
            targetExt = targetExt.ToLowerInvariant();

            if (targetExt == ".mp3")
            {
                await TranscodeAndTagAsync(job, sourcePath, finalPath, ct);
            }
            else if (targetExt == ".png" || targetExt == ".jpg" || targetExt == ".jpeg")
            {
                await ConvertImageAsync(sourcePath, finalPath, targetExt, ct);
            }
            else
            {
                throw new InvalidOperationException($"Conversion target '{targetExt}' is not supported yet.");
            }

            UpdateJob(job, j =>
            {
                j.Progress = 1;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
                j.Status = DownloadStatus.Completed;
                try
                {
                    var fi = new FileInfo(finalPath);
                    if (fi.Exists)
                    {
                        j.TotalBytes = fi.Length;
                        j.BytesDownloaded = fi.Length;
                    }
                }
                catch
                {
                }
            });
            PersistJobs();
            RaiseStateChanged();
        }

        private async Task DownloadAtlasTrackWithYtDlpAsync(DownloadJob job, Uri atlasTrackUri, string outFolder, CancellationToken ct)
        {
            var term = GetAtlasTrackSearchTerm(atlasTrackUri);
            if (string.IsNullOrWhiteSpace(term))
                throw new InvalidOperationException("Track query is empty.");

            var ytDlpPath = await EnsureYtDlpAsync(ct).ConfigureAwait(false);
            var ffmpegDir = await EnsureFfmpegDirAsync(ct).ConfigureAwait(false);
            Directory.CreateDirectory(outFolder);

            var fileName = (job.Filename ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = SanitizeFileName(term);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "track";
                fileName += ".mp3";
                UpdateJob(job, j => j.Filename = fileName);
                PersistJobs();
            }

            if (!fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                fileName = Path.GetFileNameWithoutExtension(fileName) + ".mp3";
                UpdateJob(job, j => j.Filename = fileName);
                PersistJobs();
            }

            var finalMp3Path = Path.Combine(outFolder, SanitizeFileName(fileName));
            Directory.CreateDirectory(Path.GetDirectoryName(finalMp3Path)!);

            if (System.IO.File.Exists(finalMp3Path))
            {
                try
                {
                    var fi = new FileInfo(finalMp3Path);
                    UpdateJob(job, j =>
                    {
                        j.OutputPath = finalMp3Path;
                        j.Status = DownloadStatus.Completed;
                        j.Progress = 1;
                        j.TotalBytes = fi.Exists ? fi.Length : 0;
                        j.BytesDownloaded = fi.Exists ? fi.Length : 0;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                        j.Error = null;
                    });
                    PersistJobs();
                    RaiseStateChanged();
                    return;
                }
                catch
                {
                }
            }

            UpdateJob(job, j =>
            {
                j.OutputPath = finalMp3Path;
                j.TranscodeToMp3 = true;
                j.Status = DownloadStatus.Downloading;
                j.Error = null;
                j.Resolver = "yt-dlp";
                j.ResolvedUrl = "ytmusicsearch1:" + term;
                j.Progress = 0;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
                j.TotalBytes = 0;
                j.BytesDownloaded = 0;
            });
            PersistJobs();

            var templateBase = "__yt_source.%(ext)s";
            var inputArg = "ytmusicsearch1:" + term;

            static bool TryGetDestination(string l, out string path)
            {
                path = "";
                var s = (l ?? "").Trim();
                if (s.StartsWith("[download] Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    path = s.Substring("[download] Destination:".Length).Trim().Trim('"');
                    return !string.IsNullOrWhiteSpace(path);
                }
                if (s.StartsWith("[ExtractAudio] Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    path = s.Substring("[ExtractAudio] Destination:".Length).Trim().Trim('"');
                    return !string.IsNullOrWhiteSpace(path);
                }
                return false;
            }

            static string Flatten(IEnumerable<string> lines)
            {
                var parts = lines.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
                return string.Join(" | ", parts);
            }

            static string? PickBestErrorLine(IEnumerable<string> lines)
            {
                var arr = lines.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
                for (var i = arr.Count - 1; i >= 0; i--)
                {
                    var l = arr[i];
                    var ll = l.ToLowerInvariant();
                    if (ll.Contains("error:") || ll.Contains("http error") || ll.Contains("sign in") || ll.Contains("confirm you're not a bot") || ll.Contains("unable to download") || ll.Contains("unable to extract") || ll.Contains("this video is unavailable"))
                        return l;
                }
                return arr.Count > 0 ? arr[^1] : null;
            }

            async Task<(int ExitCode, string? DownloadedPath, string? LastDestination, List<string> Recent)> RunYtDlpAsync(string? cookiesFromBrowser)
            {
                var recent = new List<string>(120);
                string? lastDestination = null;
                string? downloadedPath = null;

                void Remember(string l)
                {
                    if (string.IsNullOrWhiteSpace(l)) return;
                    if (recent.Count >= 120) recent.RemoveAt(0);
                    recent.Add(l);
                }

                var extra = new List<string>();
                if (!string.IsNullOrWhiteSpace(cookiesFromBrowser))
                {
                    extra.Add("--cookies-from-browser");
                    extra.Add(QuoteArg(cookiesFromBrowser));
                }

                var args = string.Join(" ", new[]
                    {
                        "--playlist-end", "1",
                        "--newline",
                        "--progress",
                        "--no-warnings",
                        "--retries", "3",
                        "--fragment-retries", "3",
                        "--geo-bypass",
                        "--force-ipv4",
                        "--extractor-args", QuoteArg("youtube:player_client=android"),
                        "--paths", QuoteArg(outFolder),
                        "--format", QuoteArg("bestaudio[ext=m4a]/bestaudio[ext=mp3]/bestaudio/best"),
                        "--extract-audio", "--audio-format", "mp3", "--audio-quality", "192K",
                        ffmpegDir != null ? "--ffmpeg-location" : "", ffmpegDir != null ? QuoteArg(ffmpegDir) : "",
                        "--output", QuoteArg(templateBase),
                        QuoteArg(inputArg)
                    }
                    .Concat(extra));

                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = outFolder
                };

                using var proc = Process.Start(psi);
                if (proc == null) throw new InvalidOperationException("Failed to start yt-dlp.");

                async Task ReadStreamAsync(StreamReader reader)
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var l = line.Trim();
                        Remember(l);

                        if (TryGetDestination(l, out var dest))
                        {
                            lastDestination = dest;
                            if (dest.Contains("__yt_source.", StringComparison.OrdinalIgnoreCase))
                            {
                                var candidate = dest.Replace('/', '\\').Trim('"');
                                if (!Path.IsPathRooted(candidate))
                                    candidate = Path.Combine(outFolder, candidate);
                                downloadedPath = candidate;
                            }
                            continue;
                        }

                        if (l.Contains("__yt_source.", StringComparison.OrdinalIgnoreCase) &&
                            (l.Contains("\\__yt_source.", StringComparison.OrdinalIgnoreCase) || l.Contains("/__yt_source.", StringComparison.OrdinalIgnoreCase)))
                        {
                            var candidate = l.Trim('"').Replace('/', '\\');
                            if (!Path.IsPathRooted(candidate))
                                candidate = Path.Combine(outFolder, candidate);
                            downloadedPath = candidate;
                            continue;
                        }

                        if (TryParseYtDlpProgress(l, out var p))
                        {
                            UpdateJob(job, j =>
                            {
                                if (p.TotalBytes > 0) j.TotalBytes = p.TotalBytes;
                                if (p.BytesDownloaded > 0) j.BytesDownloaded = p.BytesDownloaded;
                                if (p.Progress01 > 0) j.Progress = p.Progress01;
                                if (p.SpeedBps > 0) j.SpeedBps = p.SpeedBps;
                                if (p.EtaSeconds > 0) j.EtaSeconds = p.EtaSeconds;
                                j.LastProgressUtc = DateTime.UtcNow;
                            });
                        }
                    }
                }

                var outTask = Task.Run(() => ReadStreamAsync(proc.StandardOutput), ct);
                var errTask = Task.Run(() => ReadStreamAsync(proc.StandardError), ct);

                try
                {
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                    throw;
                }

                return (proc.ExitCode, downloadedPath, lastDestination, recent);
            }

            string? downloadedPath = null;
            string? lastDestination = null;
            List<string>? recentFinal = null;

            foreach (var cookieMode in new[] { (string?)null, "chrome", "edge" })
            {
                var result = await RunYtDlpAsync(cookieMode).ConfigureAwait(false);
                recentFinal = result.Recent;
                lastDestination = result.LastDestination;

                downloadedPath = result.DownloadedPath;
                if (string.IsNullOrWhiteSpace(downloadedPath))
                {
                    try
                    {
                        var found = Directory.GetFiles(outFolder, "__yt_source.*", SearchOption.TopDirectoryOnly)
                            .FirstOrDefault(p => !p.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(found)) downloadedPath = found;
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(downloadedPath) && System.IO.File.Exists(downloadedPath))
                    break;
            }

            if (recentFinal == null) recentFinal = new List<string>();

            if (string.IsNullOrWhiteSpace(downloadedPath) || !System.IO.File.Exists(downloadedPath))
            {
                try
                {
                    var logsDir = Path.Combine(_dataDir, "logs");
                    Directory.CreateDirectory(logsDir);
                    var logPath = Path.Combine(logsDir, $"yt-dlp_{job.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                    File.WriteAllText(logPath, string.Join(Environment.NewLine, recentFinal));
                }
                catch
                {
                }

                var best = PickBestErrorLine(recentFinal) ?? "No output from yt-dlp.";
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(lastDestination)) details.Add("Last destination: " + lastDestination);
                details.Add(best);
                throw new InvalidOperationException("yt-dlp did not produce an output file. " + Flatten(details));
            }

            UpdateJob(job, j =>
            {
                j.Status = DownloadStatus.Converting;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
            });
            PersistJobs();

            await TranscodeAndTagAsync(job, downloadedPath, finalMp3Path, ct).ConfigureAwait(false);

            try { System.IO.File.Delete(downloadedPath); } catch { }
            try
            {
                var part = downloadedPath + ".part";
                if (System.IO.File.Exists(part)) System.IO.File.Delete(part);
            }
            catch
            {
            }

            UpdateJob(job, j =>
            {
                j.Progress = 1;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
                j.Status = DownloadStatus.Completed;
                try
                {
                    var fi = new FileInfo(finalMp3Path);
                    if (fi.Exists)
                    {
                        j.TotalBytes = fi.Length;
                        j.BytesDownloaded = fi.Length;
                    }
                }
                catch
                {
                }
            });
            PersistJobs();
            RaiseStateChanged();
        }

        private static bool IsSoundCloud(Uri uri)
        {
            try
            {
                if (uri == null) return false;
                if (!uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
                var h = (uri.Host ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(h)) return false;
                if (h == "soundcloud.com" || h.EndsWith(".soundcloud.com", StringComparison.OrdinalIgnoreCase)) return true;
                if (h == "on.soundcloud.com") return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsYtDlpPreferred(Uri uri)
        {
            try
            {
                if (uri == null) return false;
                if (!uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
                var h = (uri.Host ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(h)) return false;

                if (h == "youtube.com" || h.EndsWith(".youtube.com")) return true;
                if (h == "youtu.be") return true;
                if (h == "music.youtube.com") return true;
                if (h == "soundcloud.com" || h.EndsWith(".soundcloud.com") || h == "on.soundcloud.com") return true;
                if (h == "bandcamp.com" || h.EndsWith(".bandcamp.com")) return true;
                if (h == "vimeo.com" || h.EndsWith(".vimeo.com")) return true;
                if (h == "dailymotion.com" || h.EndsWith(".dailymotion.com")) return true;
                if (h == "tiktok.com" || h.EndsWith(".tiktok.com")) return true;
                if (h == "instagram.com" || h.EndsWith(".instagram.com")) return true;
                if (h == "facebook.com" || h.EndsWith(".facebook.com")) return true;
                if (h == "x.com" || h.EndsWith(".x.com") || h == "twitter.com" || h.EndsWith(".twitter.com")) return true;
                if (h == "reddit.com" || h.EndsWith(".reddit.com")) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task DownloadUrlWithYtDlpMp3Async(DownloadJob job, Uri url, string outFolder, CancellationToken ct)
        {
            var ytDlpPath = await EnsureYtDlpAsync(ct).ConfigureAwait(false);
            await EnsureFfmpegDirAsync(ct).ConfigureAwait(false);
            Directory.CreateDirectory(outFolder);

            var fileName = (job.Filename ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                // Try to extract metadata from yt-dlp to get proper track name
                try
                {
                    var metadata = await ExtractMetadataFromYtDlpAsync(ytDlpPath, url.AbsoluteUri, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(metadata.Title))
                    {
                        var artist = (metadata.Artist ?? "").Trim();
                        fileName = string.IsNullOrWhiteSpace(artist) 
                            ? $"{metadata.Title}.mp3" 
                            : $"{artist} - {metadata.Title}.mp3";
                        
                        // Update job metadata
                        UpdateJob(job, j =>
                        {
                            j.Filename = fileName;
                            if (!string.IsNullOrWhiteSpace(metadata.Title)) j.MetaTitle = metadata.Title;
                            if (!string.IsNullOrWhiteSpace(metadata.Artist)) j.MetaArtists = metadata.Artist;
                        });
                    }
                    else
                    {
                        fileName = "track_" + job.Id + ".mp3";
                        UpdateJob(job, j => j.Filename = fileName);
                    }
                }
                catch
                {
                    fileName = "track_" + job.Id + ".mp3";
                    UpdateJob(job, j => j.Filename = fileName);
                }
                PersistJobs();
            }

            if (!fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                fileName = Path.GetFileNameWithoutExtension(fileName) + ".mp3";
                UpdateJob(job, j => j.Filename = fileName);
                PersistJobs();
            }

            var finalMp3Path = Path.Combine(outFolder, SanitizeFileName(fileName));
            Directory.CreateDirectory(Path.GetDirectoryName(finalMp3Path)!);

            if (System.IO.File.Exists(finalMp3Path))
            {
                try
                {
                    var fi = new FileInfo(finalMp3Path);
                    UpdateJob(job, j =>
                    {
                        j.OutputPath = finalMp3Path;
                        j.Status = DownloadStatus.Completed;
                        j.Progress = 1;
                        j.TotalBytes = fi.Exists ? fi.Length : 0;
                        j.BytesDownloaded = fi.Exists ? fi.Length : 0;
                        j.SpeedBps = 0;
                        j.EtaSeconds = 0;
                        j.InFlight = false;
                        j.Error = null;
                    });
                    PersistJobs();
                    RaiseStateChanged();
                    return;
                }
                catch
                {
                }
            }

            UpdateJob(job, j =>
            {
                j.OutputPath = finalMp3Path;
                j.TranscodeToMp3 = true;
                j.Status = DownloadStatus.Downloading;
                j.Error = null;
                j.Resolver = "yt-dlp";
                j.ResolvedUrl = url.AbsoluteUri;
                j.Progress = 0;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
                j.TotalBytes = 0;
                j.BytesDownloaded = 0;
            });
            PersistJobs();
            RaiseStateChanged();

            string QuoteArg(string s) => "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";

            bool TryParseProgress(string line, out YtProgress p) => TryParseYtDlpProgress(line, out p);

            static bool TryGetDestination(string l, out string path)
            {
                path = "";
                var s = (l ?? "").Trim();
                if (s.StartsWith("[download] Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    path = s.Substring("[download] Destination:".Length).Trim().Trim('"');
                    return !string.IsNullOrWhiteSpace(path);
                }
                if (s.StartsWith("[ExtractAudio] Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    path = s.Substring("[ExtractAudio] Destination:".Length).Trim().Trim('"');
                    return !string.IsNullOrWhiteSpace(path);
                }
                return false;
            }

            static string Flatten(IEnumerable<string> lines)
            {
                var parts = lines.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
                return string.Join(" | ", parts);
            }

            static string? PickBestErrorLine(IEnumerable<string> lines)
            {
                var arr = lines.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
                for (var i = arr.Count - 1; i >= 0; i--)
                {
                    var l = arr[i];
                    var ll = l.ToLowerInvariant();
                    if (ll.Contains("error:") || ll.Contains("http error") || ll.Contains("sign in") || ll.Contains("confirm you're not a bot") || ll.Contains("unable to download") || ll.Contains("unable to extract") || ll.Contains("this video is unavailable"))
                        return l;
                }
                return arr.Count > 0 ? arr[^1] : null;
            }

            string? downloadedPath = null;
            string? lastDestination = null;
            List<string>? recentFinal = null;

            foreach (var cookieMode in new[] { (string?)null, "chrome", "edge" })
            {
                var result = await RunYtDlpAsync(cookieMode).ConfigureAwait(false);
                recentFinal = result.Recent;
                lastDestination = result.LastDestination;

                downloadedPath = result.DownloadedPath;
                if (string.IsNullOrWhiteSpace(downloadedPath))
                {
                    try
                    {
                        var found = Directory.GetFiles(outFolder, "__yt_source.*", SearchOption.TopDirectoryOnly)
                            .FirstOrDefault(p => !p.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(found)) downloadedPath = found;
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(downloadedPath) && System.IO.File.Exists(downloadedPath))
                    break;
            }

            if (recentFinal == null) recentFinal = new List<string>();

            if (string.IsNullOrWhiteSpace(downloadedPath) || !System.IO.File.Exists(downloadedPath))
            {
                try
                {
                    var logsDir = Path.Combine(_dataDir, "logs");
                    Directory.CreateDirectory(logsDir);
                    var logPath = Path.Combine(logsDir, $"yt-dlp_{job.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                    File.WriteAllText(logPath, string.Join(Environment.NewLine, recentFinal));
                }
                catch
                {
                }

                var best = PickBestErrorLine(recentFinal) ?? "No output from yt-dlp.";
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(lastDestination)) details.Add("Last destination: " + lastDestination);
                details.Add(best);
                throw new InvalidOperationException("yt-dlp did not produce an output file. " + Flatten(details));
            }

            UpdateJob(job, j =>
            {
                j.Status = DownloadStatus.Converting;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
            });
            PersistJobs();

            await TranscodeAndTagAsync(job, downloadedPath, finalMp3Path, ct).ConfigureAwait(false);

            try { System.IO.File.Delete(downloadedPath); } catch { }
            try
            {
                var part = downloadedPath + ".part";
                if (System.IO.File.Exists(part)) System.IO.File.Delete(part);
            }
            catch
            {
            }

            UpdateJob(job, j =>
            {
                j.OutputPath = finalMp3Path;
                try
                {
                    var fi = new FileInfo(finalMp3Path);
                    if (fi.Exists)
                    {
                        j.TotalBytes = fi.Length;
                        j.BytesDownloaded = fi.Length;
                    }
                }
                catch
                {
                }
                j.Progress = 1;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
                j.Status = DownloadStatus.Completed;
            });
            PersistJobs();
            RaiseStateChanged();

            async Task<(int ExitCode, string? DownloadedPath, string? LastDestination, List<string> Recent)> RunYtDlpAsync(string? cookiesFromBrowser)
            {
                var recent = new List<string>();
                string? lastDest = null;
                void Remember(string l)
                {
                    recent.Add(l);
                    if (recent.Count > 250) recent.RemoveAt(0);
                }

                var templateBase = "__yt_source.%(ext)s";
                var extra = new List<string>();
                if (!string.IsNullOrWhiteSpace(cookiesFromBrowser))
                    extra.AddRange(new[] { "--cookies-from-browser", QuoteArg(cookiesFromBrowser) });

                var args = string.Join(" ", new[]
                    {
                        "--no-playlist",
                        "--newline",
                        "--progress",
                        "--no-color",
                        "--no-warnings",
                        "--retries", "3",
                        "--fragment-retries", "3",
                        "--geo-bypass",
                        "--force-ipv4",
                        "--format", QuoteArg("bestaudio[ext=m4a]/bestaudio[ext=mp3]/bestaudio/best"),
                        "--extract-audio", "--audio-format", "mp3", "--audio-quality", "192K",
                        _cachedFfmpegDir != null ? "--ffmpeg-location" : "", _cachedFfmpegDir != null ? QuoteArg(_cachedFfmpegDir!) : "",
                        "--paths", QuoteArg(outFolder),
                        "--output", QuoteArg(templateBase),
                        QuoteArg(url.AbsoluteUri)
                    }
                    .Concat(extra));

                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = outFolder
                };

                using var proc = Process.Start(psi);
                if (proc == null) throw new InvalidOperationException("Failed to start yt-dlp.");

                string? foundSource = null;
                async Task ReadStreamAsync(StreamReader reader)
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var l = line.Trim();
                        Remember(l);

                        if (TryGetDestination(l, out var dest))
                        {
                            lastDest = dest;
                            continue;
                        }

                        if (TryParseProgress(l, out var p))
                        {
                            UpdateJob(job, j =>
                            {
                                if (p.TotalBytes > 0) j.TotalBytes = p.TotalBytes;
                                if (p.BytesDownloaded > 0) j.BytesDownloaded = p.BytesDownloaded;
                                if (p.Progress01 > 0) j.Progress = p.Progress01;
                                if (p.SpeedBps > 0) j.SpeedBps = p.SpeedBps;
                                if (p.EtaSeconds > 0) j.EtaSeconds = p.EtaSeconds;
                                j.LastProgressUtc = DateTime.UtcNow;
                            });
                        }

                        if (l.Contains("__yt_source.", StringComparison.OrdinalIgnoreCase) &&
                            (l.Contains("\\__yt_source.", StringComparison.OrdinalIgnoreCase) || l.Contains("/__yt_source.", StringComparison.OrdinalIgnoreCase)))
                        {
                            var candidate = l.Trim('"').Replace('/', '\\');
                            if (!Path.IsPathRooted(candidate))
                                candidate = Path.Combine(outFolder, candidate);
                            foundSource = candidate;
                        }
                    }
                }

                var outTask = Task.Run(() => ReadStreamAsync(proc.StandardOutput), ct);
                var errTask = Task.Run(() => ReadStreamAsync(proc.StandardError), ct);

                try
                {
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                    throw;
                }

                if (string.IsNullOrWhiteSpace(foundSource))
                {
                    try
                    {
                        var found = Directory.GetFiles(outFolder, "__yt_source.*", SearchOption.TopDirectoryOnly)
                            .FirstOrDefault(p => !p.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(found)) foundSource = found;
                    }
                    catch
                    {
                    }
                }

                return (proc.ExitCode, foundSource, lastDest, recent);
            }
        }

        private static string GetAtlasTrackSearchTerm(Uri input)
        {
            var q = GetUriQueryParam(input, "q");
            var title = GetUriQueryParam(input, "title");
            var artists = GetUriQueryParam(input, "artists");

            var term = (q ?? "").Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                var a = (artists ?? "").Trim();
                var t = (title ?? "").Trim();
                term = string.IsNullOrWhiteSpace(a) ? t : $"{t} {a}";
            }
            return (term ?? "").Trim();
        }

        private static string? GetUriQueryParam(Uri uri, string key)
        {
            try
            {
                var q = (uri.Query ?? "").Trim();
                if (q.StartsWith("?")) q = q.Substring(1);
                if (string.IsNullOrWhiteSpace(q)) return null;

                foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length != 2) continue;
                    var k = Uri.UnescapeDataString(kv[0] ?? "");
                    if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
                    return Uri.UnescapeDataString(kv[1] ?? "");
                }
            }
            catch
            {
            }
            return null;
        }

        private static string QuoteArg(string value)
        {
            var s = (value ?? "").Replace("\"", "\\\"");
            return "\"" + s + "\"";
        }

        private async Task<(string? Title, string? Artist)> ExtractMetadataFromYtDlpAsync(string ytDlpPath, string url, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = $"--no-warnings --no-playlist --print \"%(title)s|||%(artist)s|||%(uploader)s\" {QuoteArg(url)}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (null, null);

                var output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return (null, null);

                var parts = output.Trim().Split(new[] { "|||" }, StringSplitOptions.None);
                var title = parts.Length > 0 ? parts[0].Trim() : null;
                var artist = parts.Length > 1 ? parts[1].Trim() : null;
                var uploader = parts.Length > 2 ? parts[2].Trim() : null;

                // If artist is empty, use uploader
                if (string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(uploader))
                    artist = uploader;

                // Clean up title if it contains common suffixes
                if (!string.IsNullOrWhiteSpace(title))
                {
                    title = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\(Official.*\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    title = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\[Official.*\]\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                }

                return (!string.IsNullOrWhiteSpace(title) ? SanitizeFileName(title) : null,
                        !string.IsNullOrWhiteSpace(artist) ? SanitizeFileName(artist) : null);
            }
            catch
            {
                return (null, null);
            }
        }

        private readonly record struct YtProgress(double Progress01, long BytesDownloaded, long TotalBytes, double SpeedBps, double EtaSeconds);

        private static bool TryParseYtDlpProgress(string line, out YtProgress p)
        {
            p = new YtProgress(0, 0, 0, 0, 0);
            try
            {
                if (!line.StartsWith("[download]", StringComparison.OrdinalIgnoreCase)) return false;
                var m = System.Text.RegularExpressions.Regex.Match(line,
                    @"\s(?<pct>\d+(?:\.\d+)?)%\s+of\s+(?<total>\d+(?:\.\d+)?)(?<totalUnit>KiB|MiB|GiB|B)\s+at\s+(?<speed>\d+(?:\.\d+)?)(?<speedUnit>KiB\/s|MiB\/s|GiB\/s|B\/s)\s+ETA\s+(?<eta>\d+:\d+|\d+:\d+:\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!m.Success) return false;

                var pct = double.TryParse(m.Groups["pct"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pctVal) ? pctVal : 0;
                var totalVal = double.TryParse(m.Groups["total"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0;
                var totalUnit = m.Groups["totalUnit"].Value;
                var speedVal = double.TryParse(m.Groups["speed"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv) ? sv : 0;
                var speedUnit = m.Groups["speedUnit"].Value;
                var etaStr = m.Groups["eta"].Value;

                var totalBytes = (long)Math.Round(totalVal * UnitToBytes(totalUnit));
                var speedBps = speedVal * UnitToBytes(speedUnit.Replace("/s", "", StringComparison.OrdinalIgnoreCase));
                var etaSeconds = ParseHmsSeconds(etaStr);
                var progress01 = Math.Clamp(pct / 100.0, 0, 1);
                var bytesDownloaded = totalBytes > 0 ? (long)Math.Round(totalBytes * progress01) : 0;

                p = new YtProgress(progress01, bytesDownloaded, totalBytes, speedBps, etaSeconds);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double UnitToBytes(string unit)
        {
            var u = (unit ?? "").Trim().ToLowerInvariant();
            return u switch
            {
                "b" => 1,
                "kib" => 1024,
                "mib" => 1024 * 1024,
                "gib" => 1024 * 1024 * 1024,
                _ => 1
            };
        }

        private static double ParseHmsSeconds(string hms)
        {
            try
            {
                var parts = (hms ?? "").Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var mm) && int.TryParse(parts[1], out var ss))
                    return Math.Max(0, mm) * 60 + Math.Max(0, ss);
                if (parts.Length == 3 && int.TryParse(parts[0], out var hh) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var s))
                    return Math.Max(0, hh) * 3600 + Math.Max(0, m) * 60 + Math.Max(0, s);
            }
            catch
            {
            }
            return 0;
        }

        private string? _cachedFfmpegDir;

        private static string ResolveFfmpegExe()
        {
            var baseDir = AppContext.BaseDirectory;
            var pathsToTry = new[]
            {
                Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "ffmpeg.exe"),
                Path.Combine(Environment.CurrentDirectory, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(Environment.CurrentDirectory, "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")
            };

            foreach (var p in pathsToTry)
            {
                if (System.IO.File.Exists(p)) return p;
            }

            var envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                foreach (var dir in envPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        var p = Path.Combine(dir.Trim(' ', '"'), "ffmpeg.exe");
                        if (System.IO.File.Exists(p)) return p;
                    }
                    catch { } // Ignore malformed paths
                }
            }

            return "ffmpeg";
        }

        private async Task<string?> EnsureFfmpegDirAsync(CancellationToken ct)
        {
            if (_cachedFfmpegDir != null) return _cachedFfmpegDir;
            try
            {
                // Check existing locations first
                var baseDir = AppContext.BaseDirectory;
                var p1 = Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe");
                if (System.IO.File.Exists(p1)) { _cachedFfmpegDir = Path.GetDirectoryName(p1)!; return _cachedFfmpegDir; }
                var cwd = Environment.CurrentDirectory;
                var p2 = Path.Combine(cwd, "ffmpeg", "ffmpeg.exe");
                if (System.IO.File.Exists(p2)) { _cachedFfmpegDir = Path.GetDirectoryName(p2)!; return _cachedFfmpegDir; }
                var p3 = Path.Combine(baseDir, "ffmpeg.exe");
                if (System.IO.File.Exists(p3)) { _cachedFfmpegDir = Path.GetDirectoryName(p3)!; return _cachedFfmpegDir; }

                // Download ffmpeg
                var targetDir = Path.Combine(cwd, "ffmpeg");
                Directory.CreateDirectory(targetDir);
                var destExe = Path.Combine(targetDir, "ffmpeg.exe");
                if (System.IO.File.Exists(destExe)) { _cachedFfmpegDir = targetDir; return _cachedFfmpegDir; }

                var tmpZip = Path.Combine(targetDir, "ffmpeg.zip.tmp");
                try { if (System.IO.File.Exists(tmpZip)) System.IO.File.Delete(tmpZip); } catch { }

                // Using gyan.dev stable release instead of BtbN nightly which has known access violation crashes
                using var resp = await _http.GetAsync("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var dst = new FileStream(tmpZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await src.CopyToAsync(dst, ct).ConfigureAwait(false);
                }

                var extractDir = Path.Combine(targetDir, "extracted_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extractDir);
                try
                {
                    ZipFile.ExtractToDirectory(tmpZip, extractDir);
                }
                catch
                {
                    try { Directory.Delete(extractDir, recursive: true); } catch { }
                    try { System.IO.File.Delete(tmpZip); } catch { }
                    return null;
                }

                var found = Directory.EnumerateFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(found) && System.IO.File.Exists(found))
                {
                    try { if (System.IO.File.Exists(destExe)) System.IO.File.Delete(destExe); } catch { }
                    System.IO.File.Move(found, destExe);
                    // Also grab ffprobe if available
                    var probeFound = Directory.EnumerateFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(probeFound) && System.IO.File.Exists(probeFound))
                    {
                        var destProbe = Path.Combine(targetDir, "ffprobe.exe");
                        try { System.IO.File.Move(probeFound, destProbe); } catch { }
                    }
                }

                try { Directory.Delete(extractDir, recursive: true); } catch { }
                try { System.IO.File.Delete(tmpZip); } catch { }

                if (System.IO.File.Exists(destExe)) { _cachedFfmpegDir = targetDir; return _cachedFfmpegDir; }
                return null;
            }
            catch { return null; }
        }

        private async Task<string> EnsureYtDlpAsync(CancellationToken ct)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var bundledA = Path.Combine(baseDir, "tools", "yt-dlp.exe");
                var bundledB = Path.Combine(baseDir, "yt-dlp.exe");
                if (File.Exists(bundledA)) return bundledA;
                if (File.Exists(bundledB)) return bundledB;
            }
            catch
            {
            }

            var toolsDir = Path.Combine(_dataDir, "tools");
            Directory.CreateDirectory(toolsDir);
            var exe = Path.Combine(toolsDir, "yt-dlp.exe");
            if (System.IO.File.Exists(exe))
                return exe;

            var url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
            var bytes = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            await System.IO.File.WriteAllBytesAsync(exe, bytes, ct).ConfigureAwait(false);
            return exe;
        }

        private Task ConvertImageAsync(string sourcePath, string destPath, string targetExt, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];

                BitmapEncoder encoder = targetExt == ".png"
                    ? new PngBitmapEncoder()
                    : new JpegBitmapEncoder { QualityLevel = 92 };

                encoder.Frames.Add(BitmapFrame.Create(frame));

                try
                {
                    if (System.IO.File.Exists(destPath)) System.IO.File.Delete(destPath);
                }
                catch
                {
                }

                using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                encoder.Save(outStream);
            }, ct);
        }

        private async Task TranscodeAndTagAsync(DownloadJob job, string sourcePath, string destMp3Path, CancellationToken ct)
        {
            // If source is already mp3, just move it
            if (sourcePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                try { if (System.IO.File.Exists(destMp3Path)) System.IO.File.Delete(destMp3Path); } catch { }
                System.IO.File.Move(sourcePath, destMp3Path);
            }
            else
            {
                // Try Media Foundation first, fall back to ffmpeg process
                bool mfOk = false;
                try
                {
                    await Task.Run(() =>
                    {
                        MediaFoundationApi.Startup();
                        try { if (System.IO.File.Exists(destMp3Path)) System.IO.File.Delete(destMp3Path); } catch { }
                        using var reader = new MediaFoundationReader(sourcePath);
                        MediaFoundationEncoder.EncodeToMp3(reader, destMp3Path, 192000);
                    }, ct);
                    mfOk = true;
                }
                catch
                {
                    mfOk = false;
                }

                if (!mfOk || !System.IO.File.Exists(destMp3Path))
                {
                    // Fallback: use ffmpeg process
                    var ffmpegExe = ResolveFfmpegExe();
                    bool exeExists = ffmpegExe == "ffmpeg" || System.IO.File.Exists(ffmpegExe);
                    if (string.IsNullOrWhiteSpace(ffmpegExe) || !exeExists)
                    {
                        var ensured = await EnsureFfmpegDirAsync(ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(ensured))
                            ffmpegExe = Path.Combine(ensured, "ffmpeg.exe");
                    }
                    
                    exeExists = ffmpegExe == "ffmpeg" || System.IO.File.Exists(ffmpegExe);
                    if (string.IsNullOrWhiteSpace(ffmpegExe) || !exeExists)
                        throw new InvalidOperationException("Cannot transcode: Media Foundation failed and ffmpeg not available.");

                    try { if (System.IO.File.Exists(destMp3Path)) System.IO.File.Delete(destMp3Path); } catch { }
                    var psi = new ProcessStartInfo
                    {
                        FileName = ffmpegExe,
                        Arguments = $"-y -i {QuoteArg(sourcePath)} -vn -ar 44100 -ac 2 -b:a 192k {QuoteArg(destMp3Path)}",
                        WorkingDirectory = Path.GetDirectoryName(ffmpegExe) ?? string.Empty,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) throw new InvalidOperationException("Failed to start ffmpeg.");
                    
                    var errTask = proc.StandardError.ReadToEndAsync(ct);
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    var stderr = await errTask.ConfigureAwait(false);
                    if (proc.ExitCode != 0 || !System.IO.File.Exists(destMp3Path))
                        throw new InvalidOperationException($"ffmpeg transcode failed (exit {proc.ExitCode}): {stderr}");
                }
            }

            var prefs = PreferencesStore.Instance.Current;
            if (!prefs.CsvEmbedThumbnails && string.IsNullOrWhiteSpace(job.MetaTitle) && string.IsNullOrWhiteSpace(job.MetaArtists) && string.IsNullOrWhiteSpace(job.MetaAlbum))
                return;

            try
            {
                using var f = TagLibFile.Create(destMp3Path);
                if (!string.IsNullOrWhiteSpace(job.MetaTitle))
                    f.Tag.Title = job.MetaTitle;

                var artists = (job.MetaArtists ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (artists.Length > 0)
                    f.Tag.Performers = artists;

                if (!string.IsNullOrWhiteSpace(job.MetaAlbum))
                    f.Tag.Album = job.MetaAlbum;

                if (job.MetaYear > 0)
                    f.Tag.Year = (uint)job.MetaYear;

                if (job.MetaTrackNumber > 0)
                    f.Tag.Track = (uint)job.MetaTrackNumber;

                if (prefs.CsvEmbedThumbnails && !string.IsNullOrWhiteSpace(job.MetaArtworkUrl))
                {
                    try
                    {
                        var bytes = _http.GetByteArrayAsync(job.MetaArtworkUrl, ct).GetAwaiter().GetResult();
                        if (bytes != null && bytes.Length > 0)
                        {
                            var pic = new TagLibPicture(new TagLibByteVector(bytes))
                            {
                                Type = TagLibPictureType.FrontCover
                            };
                            f.Tag.Pictures = new TagLibIPicture[] { pic };
                        }
                    }
                    catch
                    {
                    }
                }

                f.Save();
            }
            catch
            {
            }
        }

        private (string finalPath, string partPath, string fileName) GetUniquePaths(string folder, string desiredFileName, string jobId)
        {
            var baseName = Path.GetFileNameWithoutExtension(desiredFileName);
            var ext = Path.GetExtension(desiredFileName);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "download";

            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                foreach (var j in _jobs)
                {
                    if (j == null) continue;
                    if (j.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrWhiteSpace(j.OutputPath))
                        reserved.Add(j.OutputPath);
                    if (!string.IsNullOrWhiteSpace(j.Filename))
                        reserved.Add(Path.Combine(folder, j.Filename));
                }
            }

            for (var i = 0; i < 1000; i++)
            {
                var name = i == 0 ? $"{baseName}{ext}" : $"{baseName} ({i}){ext}";
                var final = Path.Combine(folder, name);
                var part = final + ".part";
                if (reserved.Contains(final)) continue;
                if (File.Exists(final) || File.Exists(part)) continue;
                return (final, part, name);
            }

            var fallback = Path.Combine(folder, $"{baseName}{ext}");
            return (fallback, fallback + ".part", $"{baseName}{ext}");
        }

        private string GetUniqueFolder(string rootFolder, string desiredName, string jobId)
        {
            var baseName = (desiredName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(baseName)) baseName = jobId;

            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                foreach (var j in _jobs)
                {
                    if (j == null) continue;
                    if (j.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrWhiteSpace(j.OutputFolder))
                        reserved.Add(j.OutputFolder);
                }
            }

            for (var i = 0; i < 10_000; i++)
            {
                var name = i == 0 ? baseName : $"{baseName} ({i})";
                var folder = Path.Combine(rootFolder, name);
                if (reserved.Contains(folder)) continue;
                if (Directory.Exists(folder)) continue;
                return folder;
            }

            return Path.Combine(rootFolder, jobId);
        }

        private void PauseById(string id)
        {
            CancellationTokenSource? cts = null;
            lock (_lock) _jobCts.TryGetValue(id, out cts);
            try { cts?.Cancel(); } catch { }
            var job = FindJob(id);
            if (job != null)
            {
                UpdateJob(job, j =>
                {
                    j.Status = DownloadStatus.Paused;
                    j.InFlight = false;
                    j.NextAttemptUtc = null;
                    j.SpeedBps = 0;
                    j.EtaSeconds = 0;
                });
                PersistJobs();
                RaiseStateChanged();
            }
        }

        private void ResumeById(string id)
        {
            var job = FindJob(id);
            if (job == null) return;
            if (job.Status == DownloadStatus.Completed) return;
            UpdateJob(job, j =>
            {
                j.Status = DownloadStatus.Queued;
                j.Error = null;
                j.NextAttemptUtc = null;
                j.InFlight = false;
            });
            PersistJobs();
            RaiseStateChanged();
        }

        private void RetryById(string id)
        {
            var job = FindJob(id);
            if (job == null) return;
            if (job.Status != DownloadStatus.Error && job.Status != DownloadStatus.Cancelled) return;
            UpdateJob(job, j =>
            {
                j.Status = DownloadStatus.Queued;
                j.Error = null;
                j.NextAttemptUtc = null;
                j.InFlight = false;
                j.Attempts = 0;
                j.RequestStop = false;
                j.SpeedBps = 0;
                j.EtaSeconds = 0;
            });
            PersistJobs();
            RaiseStateChanged();
        }

        private void CancelById(string id)
        {
            CancellationTokenSource? cts = null;
            lock (_lock) _jobCts.TryGetValue(id, out cts);
            try { cts?.Cancel(); } catch { }
            var job = FindJob(id);
            if (job != null)
            {
                UpdateJob(job, j =>
                {
                    j.Status = DownloadStatus.Cancelled;
                    j.SpeedBps = 0;
                    j.EtaSeconds = 0;
                    j.InFlight = false;
                    j.NextAttemptUtc = null;
                });
                PersistJobs();
                RaiseStateChanged();
            }
        }

        private void RemoveById(string id)
        {
            DownloadJob? job;
            lock (_lock)
            {
                job = _jobs.FirstOrDefault(j => j.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (job != null) _jobs.Remove(job);
                PersistJobs();
            }
            try
            {
                static void TryDelete(string path)
                {
                    if (string.IsNullOrWhiteSpace(path)) return;
                    try
                    {
                        if (!File.Exists(path)) return;
                        File.Delete(path);
                        return;
                    }
                    catch
                    {
                    }
                    try { AtlasAI.MediaScanner.PlaybackOutputCoordinator.StopActive(); } catch { }
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                    }
                    catch
                    {
                    }
                }

                if (job?.OutputPath != null && File.Exists(job.OutputPath))
                {
                    TryDelete(job.OutputPath);
                }
                if (job?.Filename != null)
                {
                    var folder = !string.IsNullOrWhiteSpace(job.OutputFolder) ? job.OutputFolder!.Trim() : EnsureOutputFolder();
                    var final = !string.IsNullOrWhiteSpace(job.OutputPath)
                        ? job.OutputPath!
                        : Path.Combine(folder, job.Filename);

                    try
                    {
                        TryDelete(final);
                    }
                    catch
                    {
                    }

                    var finalPart = final + ".part";
                    TryDelete(finalPart);

                    var dlExt = (job.DownloadExtension ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(dlExt))
                    {
                        var download = Path.Combine(folder, Path.GetFileNameWithoutExtension(final) + dlExt);
                        var downloadPart = download + ".part";
                        TryDelete(downloadPart);
                    }
                }
            }
            catch
            {
            }
            RaiseStateChanged();
        }

        private void OpenFolderById(string id)
        {
            var job = FindJob(id);
            var path = job?.OutputPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var folder = !string.IsNullOrWhiteSpace(job?.OutputFolder)
                    ? job!.OutputFolder!.Trim()
                    : EnsureOutputFolder();
                TryOpenFolder(folder);
                return;
            }
            TryOpenFolder(Path.GetDirectoryName(path)!);
        }

        private void OpenMediaById(string id)
        {
            var job = FindJob(id);
            if (job == null) return;

            var folder = !string.IsNullOrWhiteSpace(job.OutputFolder) ? job.OutputFolder!.Trim() : EnsureOutputFolder();
            var path = !string.IsNullOrWhiteSpace(job.OutputPath)
                ? job.OutputPath!.Trim()
                : (!string.IsNullOrWhiteSpace(job.Filename) ? Path.Combine(folder, job.Filename) : "");

            if (string.IsNullOrWhiteSpace(path))
            {
                TryOpenFolder(folder);
                return;
            }

            if (!File.Exists(path))
            {
                TryOpenFolder(Path.GetDirectoryName(path) ?? folder);
                return;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var isVideo = ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" or ".m4v";
            var isAudio = ext is ".mp3" or ".m4a" or ".aac" or ".wav" or ".flac" or ".ogg" or ".wma";
            if (!isVideo && !isAudio) return;

            var item = new MediaItem
            {
                FilePath = path,
                DisplayName = Path.GetFileNameWithoutExtension(path),
                Extension = ext,
                FolderPath = Path.GetDirectoryName(path) ?? folder,
                MediaType = isVideo ? AtlasAI.MediaScanner.MediaType.Video : AtlasAI.MediaScanner.MediaType.Audio,
                SectionName = isVideo ? "Movies" : "Music",
                DateAdded = DateTime.Now,
                LastModified = File.GetLastWriteTime(path),
                FileSize = new FileInfo(path).Length
            };

            try
            {
                var svc = MediaPlaybackService.GetOrCreate();
                Application.Current.Dispatcher.Invoke(() => svc.PlaySingle(item));
            }
            catch
            {
            }

            if (isVideo)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var window = Application.Current.Windows.OfType<CommandCenterWindow>().FirstOrDefault(w => w != null && w.IsLoaded)
                                     ?? Application.Current.MainWindow as CommandCenterWindow;
                        window?.NavigateToTab("AI MEDIA CENTRE", "Media");
                    });
                }
                catch
                {
                }
            }
        }

        private static void TryOpenFolder(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private DownloadJob? FindJob(string id)
        {
            lock (_lock) return _jobs.FirstOrDefault(j => j.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateJob(DownloadJob job, Action<DownloadJob> mutator)
        {
            lock (_lock)
            {
                mutator(job);
            }
            RaiseStateChanged();
        }

        private void WithJobId(JsonElement payload, Action<string> action)
        {
            if (payload.ValueKind != JsonValueKind.Object) return;
            if (!payload.TryGetProperty("id", out var idEl)) return;
            var id = (idEl.GetString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id)) return;
            action(id);
        }

        private void RaiseStateChanged()
        {
            try { StateChanged?.Invoke(this, EventArgs.Empty); } catch { }
        }

        private string EnsureOutputFolder()
        {
            var folder = (PreferencesStore.Instance.Current.MediaDownloadsFolder ?? "").Trim();
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AtlasAI");
                PreferencesStore.Instance.Update(p => p.MediaDownloadsFolder = folder);
            }
            return folder;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    opts.Converters.Add(new JsonStringEnumConverter());
                    var s = JsonSerializer.Deserialize<DownloadSettings>(json, opts);
                    if (s != null) _settings = s;
                }
            }
            catch
            {
            }

            try
            {
                _settings.Providers.RealDebrid.Token = _tokenStore.Load("RealDebrid");
                _settings.Providers.AllDebrid.Token = _tokenStore.Load("AllDebrid");
                _settings.Providers.Premiumize.Token = _tokenStore.Load("Premiumize");
            }
            catch
            {
            }

            _settings.MaxParallelDownloads = Math.Clamp(_settings.MaxParallelDownloads, 1, 12);
            _parallelGate = new SemaphoreSlim(_settings.MaxParallelDownloads, _settings.MaxParallelDownloads);
        }

        private void SaveSettings()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                opts.Converters.Add(new JsonStringEnumConverter());
                var json = JsonSerializer.Serialize(_settings, opts);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
            }
        }

        private void LoadJobs()
        {
            try
            {
                if (!File.Exists(_jobsPath)) return;
                var json = File.ReadAllText(_jobsPath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                opts.Converters.Add(new JsonStringEnumConverter());
                var items = JsonSerializer.Deserialize<List<DownloadJob>>(json, opts);
                if (items == null) return;
                lock (_lock)
                {
                    _jobs.Clear();
                    _jobs.AddRange(items);
                    foreach (var j in _jobs)
                    {
                        if (j.Status == DownloadStatus.Downloading || j.Status == DownloadStatus.Resolving)
                            j.Status = DownloadStatus.Queued;
                    }
                }
            }
            catch
            {
            }
        }

        private void PersistJobs()
        {
            try
            {
                List<DownloadJob> snapshot;
                lock (_lock) snapshot = _jobs.ToList();
                var opts = new JsonSerializerOptions { WriteIndented = true };
                opts.Converters.Add(new JsonStringEnumConverter());
                var json = JsonSerializer.Serialize(snapshot, opts);
                File.WriteAllText(_jobsPath, json);
            }
            catch
            {
            }
        }

        private void RebuildPipeline()
        {
            var tokenProvider = new Func<string>(() => (_tokenStore.Load("RealDebrid") ?? "").Trim());
            _realDebrid = new RealDebridResolver(_http, tokenProvider) { IsEnabled = _settings.Providers.RealDebrid.Enabled };
            var rdMagnet = new RealDebridMagnetResolver(_http, tokenProvider) { IsEnabled = _settings.Providers.RealDebrid.Enabled };
            var allDebrid = new AllDebridResolver { IsEnabled = _settings.Providers.AllDebrid.Enabled };
            var premiumize = new PremiumizeResolver { IsEnabled = _settings.Providers.Premiumize.Enabled };
            var itunes = new ItunesPreviewResolver(_http) { IsEnabled = true };
            var direct = new DirectHttpResolver { IsEnabled = true };
            _pipeline = new ResolverPipeline(new ILinkResolver[] { itunes, rdMagnet, _realDebrid, allDebrid, premiumize, direct });
        }

        private static string GuessFileName(Uri url)
        {
            try
            {
                // Try to get filename from URL path
                var path = url.LocalPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var name = Path.GetFileName(path);
                    if (!string.IsNullOrWhiteSpace(name) && name != "/" && name.Length > 1)
                    {
                        // Decode URL-encoded characters
                        name = Uri.UnescapeDataString(name);
                        // Remove query parameters if they got included
                        var queryIndex = name.IndexOf('?');
                        if (queryIndex > 0) name = name.Substring(0, queryIndex);
                        
                        if (!string.IsNullOrWhiteSpace(name) && name.Length > 1)
                            return name;
                    }
                }

                // Try to extract from Content-Disposition header or URL segments
                var segments = url.Segments;
                if (segments != null && segments.Length > 0)
                {
                    var lastSegment = segments[segments.Length - 1].Trim('/');
                    if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment.Length > 1)
                    {
                        lastSegment = Uri.UnescapeDataString(lastSegment);
                        var queryIndex = lastSegment.IndexOf('?');
                        if (queryIndex > 0) lastSegment = lastSegment.Substring(0, queryIndex);
                        
                        if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment.Length > 1)
                            return lastSegment;
                    }
                }

                // Try to use the host as a base for the filename
                var host = url.Host;
                if (!string.IsNullOrWhiteSpace(host))
                {
                    // Use .mkv as default for debrid CDN and media hosts, .bin otherwise
                    var ext = host.Contains("debrid", StringComparison.OrdinalIgnoreCase)
                           || host.Contains("cdn", StringComparison.OrdinalIgnoreCase)
                           || host.Contains("rdb", StringComparison.OrdinalIgnoreCase)
                        ? ".mkv" : ".bin";
                    return $"{host}_download{ext}";
                }
            }
            catch
            {
            }
            return "download.mkv";
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length == 0 ? "download.bin" : name;
        }
    }
}
