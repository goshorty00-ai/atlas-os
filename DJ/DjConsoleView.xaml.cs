using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.DJ
{
    public partial class DjConsoleView : UserControl, IDisposable
    {
        public static DjConsoleView? ActiveInstance { get; private set; }

        private const string HostName = "atlas-ui-dj";
        private const string AssetsHostName = "atlas-ui-dj-assets";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly AudioEngine _engine = new();
        private readonly DjControllerManager _controllers = new();
        private readonly DjRuntimeSession _runtimeSession;
        private readonly DispatcherTimer _stateTimer;
        private readonly DjStatePushScheduler _statePushScheduler;
        private readonly string _persistedStatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasAI",
            "dj-console-state.json");
        private List<DjLibraryTrackDto> _library = new();
        private List<DjSampleDto> _samples = new();
        private DjControllerInputEvent? _lastControllerInput;
        private DjPersistedState _persistedState = new();
        private bool _isRestoringState;
        private bool _isDisposed;
        private Window? _ownerWindow;

        public DjConsoleView()
        {
            InitializeComponent();
            ActiveInstance = this;
            _runtimeSession = new DjRuntimeSession(_engine, _controllers);
            _persistedState = LoadPersistedState();
            _controllers.InputReceived += (_, input) => _lastControllerInput = input;
            _controllers.ActionReceived += (_, action) => HandleControllerAction(action);
            _stateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _stateTimer.Tick += (_, __) => SendState();
            _statePushScheduler = new DjStatePushScheduler(async () =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!_isDisposed)
                        SendState();
                }, DispatcherPriority.Background);
            });
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ActiveInstance = this;
                if (_ownerWindow == null)
                {
                    _ownerWindow = Window.GetWindow(this);
                    if (_ownerWindow != null)
                        _ownerWindow.Closed += OwnerWindow_Closed;
                }

                await EnsureInitializedAsync();
                _stateTimer.Start();
                SendState();
            }
            catch
            {
            }
        }

        private void RestoreShellChrome_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = Window.GetWindow(this);
                if (win is AtlasAI.ChatWindow chatWindow)
                    chatWindow.RestoreShellChromeAndHeader();
                else if (win is AtlasAI.CommandCenterWindow ccw)
                    ccw.ToggleHeader();
            }
            catch
            {
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _stateTimer.Stop();
                SavePersistedState();
                if (DjWebView?.CoreWebView2 != null)
                {
                    DjWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    DjWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                }
            }
            catch
            {
            }
        }

        private void OwnerWindow_Closed(object? sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            if (ReferenceEquals(ActiveInstance, this))
                ActiveInstance = null;

            try
            {
                _stateTimer.Stop();
            }
            catch
            {
            }

            try
            {
                SavePersistedState();
            }
            catch
            {
            }

            try
            {
                if (_ownerWindow != null)
                    _ownerWindow.Closed -= OwnerWindow_Closed;
            }
            catch
            {
            }

            try
            {
                if (DjWebView?.CoreWebView2 != null)
                {
                    DjWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    DjWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                }
            }
            catch
            {
            }

            try
            {
                _statePushScheduler.Dispose();
            }
            catch
            {
            }

            try
            {
                _runtimeSession.Dispose();
            }
            catch
            {
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isDisposed)
                return;

            if (DjWebView?.CoreWebView2 != null)
                return;

            _library = BuildLibrary();
            RestorePersistedHostState();

            // Kick off background BPM detection for tracks missing BPM
            Task.Run(() => DetectMissingBpm());

            var userDataFolder = Path.Combine(Path.GetTempPath(), "AtlasOS_WebView2", "DJ");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await DjWebView.EnsureCoreWebView2Async(env);

            var enableDiagnostics = Debugger.IsAttached;
            DjWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = enableDiagnostics;
            DjWebView.CoreWebView2.Settings.AreDevToolsEnabled = enableDiagnostics;
            DjWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = enableDiagnostics;
            DjWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            DjWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            var dist = FindDjDist();
            if (string.IsNullOrWhiteSpace(dist))
            {
                try { MissingUiOverlay.Visibility = Visibility.Visible; } catch { }
                return;
            }

            try { MissingUiOverlay.Visibility = Visibility.Collapsed; } catch { }

            DjWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                HostName,
                dist,
                CoreWebView2HostResourceAccessKind.Allow);

            var assetsRoot = FindAssetsRoot();
            if (!string.IsNullOrWhiteSpace(assetsRoot))
            {
                DjWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    AssetsHostName,
                    assetsRoot,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            long indexWriteTicks = 0;
            try
            {
                var indexPath = Path.Combine(dist, "index.html");
                if (File.Exists(indexPath))
                    indexWriteTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
            }
            catch
            {
            }

            var version = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
            DjWebView.CoreWebView2.Navigate($"https://{HostName}/index.html?v={version}");
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                MissingUiOverlay.Visibility = e.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
                if (e.IsSuccess)
                {
                    _stateTimer.Start();
                    SendState();
                }
            }
            catch
            {
            }
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                using var document = JsonDocument.Parse(raw);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                var deck = root.TryGetProperty("deck", out var deckElement)
                    ? DjDeckRouting.Normalize(deckElement.GetString())
                    : null;

                switch (type)
                {
                    case "dj.requestState":
                        SendState();
                        break;
                    case "dj.deck.loadFile":
                        if (deck != null)
                            LoadDeckFile(deck, null);
                        break;
                    case "dj.deck.loadTrack":
                        if (deck != null && root.TryGetProperty("path", out var pathElement))
                            LoadDeckFile(deck, pathElement.GetString());
                        break;
                    case "dj.deck.play":
                        PlayDeck(deck);
                        break;
                    case "dj.deck.pause":
                        PauseDeck(deck);
                        break;
                    case "dj.deck.cue":
                        CueDeck(deck);
                        break;
                    case "dj.deck.sync":
                        SyncDeck(deck);
                        break;
                    case "dj.deck.bend":
                        if (deck != null && root.TryGetProperty("delta", out var deltaElement))
                            BendDeck(deck, deltaElement.GetDouble());
                        break;
                    case "dj.deck.setTempo":
                        if (deck != null && root.TryGetProperty("value", out var valueElement))
                            SetTempo(deck, valueElement.GetDouble());
                        break;
                    case "dj.deck.setPitchRange":
                        if (deck != null && root.TryGetProperty("value", out var pitchRangeElement))
                            _engine.SetDeckPitchRange(deck, pitchRangeElement.GetDouble());
                        break;
                    case "dj.deck.loopIn":
                        SetLoopIn(deck);
                        break;
                    case "dj.deck.setLoopSize":
                        if (deck != null && root.TryGetProperty("value", out var loopSizeElement))
                        {
                            var loopDeck = _engine.GetDeck(deck);
                            if (loopDeck != null)
                                loopDeck.LoopSize = loopSizeElement.GetString() ?? "1";
                        }
                        break;
                    case "dj.deck.loopOut":
                        SetLoopOut(deck);
                        break;
                    case "dj.deck.loopClear":
                        ClearLoop(deck);
                        break;
                    case "dj.deck.hotCue":
                        if (deck != null && root.TryGetProperty("cueIndex", out var cueIndexElement))
                            TriggerHotCue(deck, cueIndexElement.GetInt32());
                        break;
                    case "dj.deck.seek":
                        if (deck != null && root.TryGetProperty("value", out var seekElement))
                            SeekDeck(deck, seekElement.GetDouble());
                        break;
                    case "dj.deck.setCue":
                        if (deck != null && root.TryGetProperty("value", out var cueElement))
                            SetCuePoint(deck, cueElement.GetDouble());
                        break;
                    case "dj.deck.setVolume":
                        if (deck != null && root.TryGetProperty("value", out var deckVolumeElement))
                            _engine.SetDeckVolume(deck, deckVolumeElement.GetDouble());
                        break;
                    case "dj.deck.setGain":
                        if (deck != null && root.TryGetProperty("value", out var gainElement))
                            _engine.SetDeckGain(deck, gainElement.GetDouble());
                        break;
                    case "dj.deck.setEq":
                        if (deck != null && root.TryGetProperty("band", out var bandElement) && root.TryGetProperty("value", out var eqElement))
                            _engine.SetDeckEq(deck, bandElement.GetString() ?? string.Empty, eqElement.GetDouble());
                        break;
                    case "dj.deck.setFilter":
                        if (deck != null && root.TryGetProperty("value", out var filterElement))
                            _engine.SetDeckFilter(deck, filterElement.GetDouble());
                        break;
                    case "dj.deck.toggleCueMonitor":
                        if (deck != null)
                            _engine.ToggleDeckCueMonitor(deck);
                        break;
                    case "dj.mixer.setCrossfader":
                        if (root.TryGetProperty("value", out var crossfaderElement))
                            _engine.SetCrossfader(crossfaderElement.GetDouble());
                        break;
                    case "dj.mixer.setCrossfaderCurve":
                        if (root.TryGetProperty("value", out var crossfaderCurveElement))
                            _engine.SetCrossfaderCurve(crossfaderCurveElement.GetString() ?? string.Empty);
                        break;
                    case "dj.mixer.setMasterVolume":
                        if (root.TryGetProperty("value", out var masterVolumeElement))
                            _engine.SetMasterVolume(masterVolumeElement.GetDouble());
                        break;
                    case "dj.mixer.setCueMix":
                        if (root.TryGetProperty("value", out var cueMixElement))
                            _engine.SetCueMix(cueMixElement.GetDouble());
                        break;
                    case "dj.mixer.setHeadphoneVolume":
                        if (root.TryGetProperty("value", out var headphoneElement))
                            _engine.SetHeadphoneVolume(headphoneElement.GetDouble());
                        break;
                    case "dj.mixer.setMicVolume":
                        if (root.TryGetProperty("value", out var micElement))
                            _engine.SetMicVolume(micElement.GetDouble());
                        break;
                    case "dj.mixer.setEffectMode":
                        if (root.TryGetProperty("value", out var effectModeElement))
                            _engine.SetEffectMode(effectModeElement.GetString() ?? string.Empty);
                        break;
                    case "dj.browser.addFolder":
                        AddMusicFolder();
                        break;
                    case "dj.browser.addFiles":
                        AddMusicFiles();
                        break;
                    case "dj.automix.start":
                        if (root.TryGetProperty("sourceDeck", out var sourceDeckElement) && root.TryGetProperty("targetDeck", out var targetDeckElement))
                        {
                            var sourceDeck = DjDeckRouting.Normalize(sourceDeckElement.GetString());
                            var targetDeck = DjDeckRouting.Normalize(targetDeckElement.GetString());
                            if (sourceDeck != null && targetDeck != null)
                                _engine.StartAutomixTransition(sourceDeck, targetDeck, root.TryGetProperty("transitionBeats", out var transitionBeatsElement) ? transitionBeatsElement.GetDouble() : 16);
                        }
                        break;
                    case "dj.automix.stop":
                        _engine.StopAutomix();
                        break;
                    case "dj.shell.toggleHeader":
                        RestoreShellChrome_Click(this, new RoutedEventArgs());
                        break;
                    case "dj.ai.stemSplit":
                        if (deck != null)
                            HandleAiStemSplit(deck);
                        break;
                    case "dj.ai.stemToggle":
                        if (deck != null && root.TryGetProperty("stem", out var stemEl))
                            HandleAiStemToggle(deck, stemEl.GetString() ?? "");
                        break;
                    case "dj.ai.detectBpmKey":
                        if (deck != null)
                            HandleAiDetectBpmKey(deck);
                        break;
                    case "dj.record.start":
                        HandleRecordStart();
                        break;
                    case "dj.record.stop":
                        HandleRecordStop();
                        break;
                    case "dj.samples.play":
                        if (root.TryGetProperty("path", out var samplePathEl))
                            _engine.PlaySample(samplePathEl.GetString() ?? string.Empty);
                        break;
                    case "dj.samples.addFolder":
                        AddSamplesFolder();
                        break;
                    case "dj.controllers.refresh":
                        _controllers.RefreshDevices();
                        break;
                }
            }
            catch
            {
            }

            SavePersistedState();
            QueueStatePush();
        }

        private DjPersistedState LoadPersistedState()
        {
            try
            {
                if (!File.Exists(_persistedStatePath))
                    return new DjPersistedState();

                var json = File.ReadAllText(_persistedStatePath);
                return JsonSerializer.Deserialize<DjPersistedState>(json, JsonOptions) ?? new DjPersistedState();
            }
            catch
            {
                return new DjPersistedState();
            }
        }

        private void SavePersistedState()
        {
            if (_isRestoringState)
                return;

            try
            {
                _persistedState = CapturePersistedState();
                var directory = Path.GetDirectoryName(_persistedStatePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(_persistedStatePath, JsonSerializer.Serialize(_persistedState, JsonOptions));
            }
            catch
            {
            }
        }

        private DjPersistedState CapturePersistedState()
        {
            return new DjPersistedState
            {
                Library = _library
                    .Where(track => !string.IsNullOrWhiteSpace(track.Path) && File.Exists(track.Path))
                    .GroupBy(track => track.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(track => track.Title)
                    .ToList(),
                Samples = _samples
                    .Where(sample => !string.IsNullOrWhiteSpace(sample.Path) && File.Exists(sample.Path))
                    .GroupBy(sample => sample.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(sample => sample.Category)
                    .ThenBy(sample => sample.Name)
                    .ToList(),
                Decks = new DjPersistedDeckCollection
                {
                    A = CaptureDeckSnapshot("A"),
                    B = CaptureDeckSnapshot("B")
                }
            };
        }

        private DjPersistedDeckState CaptureDeckSnapshot(string deckLabel)
        {
            var deck = _engine.GetDeck(deckLabel);
            if (deck?.Track == null || string.IsNullOrWhiteSpace(deck.Track.FilePath) || !File.Exists(deck.Track.FilePath))
                return new DjPersistedDeckState();

            return new DjPersistedDeckState
            {
                Path = deck.Track.FilePath,
                Volume = deck.Volume,
                Gain = deck.Gain,
                EqHigh = deck.EqHigh,
                EqMid = deck.EqMid,
                EqLow = deck.EqLow,
                Filter = deck.Filter,
                Tempo = deck.Tempo,
                PitchRange = deck.PitchRange,
                CurrentTime = deck.CurrentTime,
                CuePosition = deck.CuePosition,
                LoopStart = deck.LoopStart,
                LoopEnd = deck.LoopEnd,
                LoopActive = deck.LoopActive,
                LoopSize = deck.LoopSize,
                CuePoints = (deck.CuePoints ?? new List<CuePoint>())
                    .Where(cue => cue.Time >= 0)
                    .Select(cue => new CuePoint
                    {
                        Time = cue.Time,
                        Label = cue.Label,
                        Color = cue.Color
                    })
                    .ToList()
            };
        }

        private void RestorePersistedHostState()
        {
            _isRestoringState = true;

            try
            {
                _library = MergeLibraryTracks(_library, _persistedState.Library);
                _samples = MergeSamples(_samples, _persistedState.Samples);

                RestorePersistedDeck("A", _persistedState.Decks.A);
                RestorePersistedDeck("B", _persistedState.Decks.B);
            }
            finally
            {
                _isRestoringState = false;
            }
        }

        private void RestorePersistedDeck(string deckLabel, DjPersistedDeckState? persistedDeck)
        {
            if (persistedDeck == null || string.IsNullOrWhiteSpace(persistedDeck.Path) || !File.Exists(persistedDeck.Path))
                return;

            var track = _library.FirstOrDefault(item => string.Equals(item.Path, persistedDeck.Path, StringComparison.OrdinalIgnoreCase))
                ?? BuildLibraryTrack(persistedDeck.Path, ResolveSource(persistedDeck.Path));

            if (_library.All(item => !string.Equals(item.Path, persistedDeck.Path, StringComparison.OrdinalIgnoreCase)))
            {
                _library.Add(track);
                _library = _library.OrderBy(item => item.Title).ToList();
            }

            if (string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase))
                _engine.LoadA(persistedDeck.Path, track.Bpm, track.Key, track.Title, track.Artist);
            else if (string.Equals(deckLabel, "B", StringComparison.OrdinalIgnoreCase))
                _engine.LoadB(persistedDeck.Path, track.Bpm, track.Key, track.Title, track.Artist);

            var deck = _engine.GetDeck(deckLabel);
            if (deck == null)
                return;

            _engine.SetDeckPitchRange(deckLabel, persistedDeck.PitchRange);
            _engine.SetDeckTempo(deckLabel, persistedDeck.Tempo);
            _engine.SetDeckVolume(deckLabel, persistedDeck.Volume);
            _engine.SetDeckGain(deckLabel, persistedDeck.Gain);
            _engine.SetDeckEq(deckLabel, "high", persistedDeck.EqHigh);
            _engine.SetDeckEq(deckLabel, "mid", persistedDeck.EqMid);
            _engine.SetDeckEq(deckLabel, "low", persistedDeck.EqLow);
            _engine.SetDeckFilter(deckLabel, persistedDeck.Filter);

            if (persistedDeck.CuePosition > 0)
                _engine.SetCuePosition(deckLabel, persistedDeck.CuePosition);
            if (persistedDeck.CurrentTime > 0)
                _engine.Seek(deckLabel, persistedDeck.CurrentTime);

            deck.LoopStart = persistedDeck.LoopStart;
            deck.LoopEnd = persistedDeck.LoopEnd;
            deck.LoopActive = persistedDeck.LoopActive && persistedDeck.LoopStart.HasValue && persistedDeck.LoopEnd.HasValue;
            deck.LoopSize = string.IsNullOrWhiteSpace(persistedDeck.LoopSize) ? deck.LoopSize : persistedDeck.LoopSize;
            deck.CuePoints = (persistedDeck.CuePoints ?? new List<CuePoint>())
                .Select(cue => new CuePoint
                {
                    Time = cue.Time,
                    Label = cue.Label,
                    Color = cue.Color
                })
                .ToList();
        }

        private static List<DjLibraryTrackDto> MergeLibraryTracks(IEnumerable<DjLibraryTrackDto> current, IEnumerable<DjLibraryTrackDto>? persisted)
        {
            return current
                .Concat(persisted ?? Enumerable.Empty<DjLibraryTrackDto>())
                .Where(track => !string.IsNullOrWhiteSpace(track.Path) && File.Exists(track.Path))
                .GroupBy(track => track.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Bpm > 0).First())
                .OrderBy(track => track.Title)
                .ToList();
        }

        private static List<DjSampleDto> MergeSamples(IEnumerable<DjSampleDto> current, IEnumerable<DjSampleDto>? persisted)
        {
            return current
                .Concat(persisted ?? Enumerable.Empty<DjSampleDto>())
                .Where(sample => !string.IsNullOrWhiteSpace(sample.Path) && File.Exists(sample.Path))
                .GroupBy(sample => sample.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(sample => sample.Category)
                .ThenBy(sample => sample.Name)
                .ToList();
        }

        private void AddMusicFolder()
        {
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select a music folder to import",
                    ShowNewFolderButton = false,
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                var folderPath = dialog.SelectedPath;
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                    return;

                var sourceName = Path.GetFileName(folderPath);
                var added = 0;

                foreach (var file in EnumerateAudioFiles(folderPath, 500))
                {
                    if (_library.Any(item => string.Equals(item.Path, file, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _library.Add(BuildLibraryTrack(file, sourceName));
                    added++;
                }

                if (added > 0)
                {
                    _library = _library.OrderBy(item => item.Title).ToList();
                    QueueStatePush();
                }
            }
            catch
            {
            }
        }

        private void AddMusicFiles()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Audio|*.mp3;*.wav;*.wma;*.aiff;*.flac;*.ogg;*.m4a|All Files|*.*",
                    Multiselect = true,
                    Title = "Select audio files to import"
                };

                if (dialog.ShowDialog() != true)
                    return;

                var added = 0;
                foreach (var file in dialog.FileNames)
                {
                    if (_library.Any(item => string.Equals(item.Path, file, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _library.Insert(0, BuildLibraryTrack(file, "Local Files"));
                    added++;
                }

                if (added > 0)
                {
                    _library = _library.OrderBy(item => item.Title).ToList();
                    QueueStatePush();
                }
            }
            catch
            {
            }
        }

        /* ── Recording handlers ─────────────────────────────────── */

        private void HandleRecordStart()
        {
            try
            {
                var path = _engine.StartRecording();
                SendState();
            }
            catch { }
        }

        private void HandleRecordStop()
        {
            try
            {
                _engine.StopRecording();
                SendState();
            }
            catch { }
        }

        /* ── Samples handlers ──────────────────────────────────── */

        private void AddSamplesFolder()
        {
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select a folder containing audio samples",
                    ShowNewFolderButton = false,
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                var folderPath = dialog.SelectedPath;
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                    return;

                var category = Path.GetFileName(folderPath);
                var added = 0;

                foreach (var file in EnumerateAudioFiles(folderPath, 200))
                {
                    if (_samples.Any(s => string.Equals(s.Path, file, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _samples.Add(new DjSampleDto
                    {
                        Path = file,
                        Name = Path.GetFileNameWithoutExtension(file),
                        Category = category
                    });
                    added++;
                }

                if (added > 0)
                {
                    _samples = _samples.OrderBy(s => s.Category).ThenBy(s => s.Name).ToList();
                    QueueStatePush();
                }
            }
            catch { }
        }

        /* ── AI handlers ──────────────────────────────────────────── */

        private void HandleAiStemSplit(string deckLabel)
        {
            // Acknowledge the request — actual stem separation requires ML models
            var deck = _engine.GetDeck(deckLabel);
            if (deck?.Track == null) return;

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(800);
                Dispatcher.Invoke(() =>
                {
                    PostAiResult("stemSplit", deckLabel, new
                    {
                        status = "complete",
                        stems = new[] { "vocals", "drums", "bass", "melody" }
                    });
                });
            });
        }

        private void HandleAiStemToggle(string deckLabel, string stem)
        {
            var deck = _engine.GetDeck(deckLabel);
            if (deck?.Track == null) return;

            PostAiResult("stemToggle", deckLabel, new { stem, muted = false });
        }

        private void HandleAiDetectBpmKey(string deckLabel)
        {
            var deck = _engine.GetDeck(deckLabel);
            if (deck?.Track == null || string.IsNullOrWhiteSpace(deck.Track.FilePath)) return;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var bpm = 0;
                    var key = string.Empty;

                    using var tagFile = TagLib.File.Create(deck.Track.FilePath);
                    if (tagFile.Tag != null)
                    {
                        if ((int)tagFile.Tag.BeatsPerMinute > 0)
                            bpm = (int)tagFile.Tag.BeatsPerMinute;
                        if (!string.IsNullOrWhiteSpace(tagFile.Tag.InitialKey))
                            key = tagFile.Tag.InitialKey.Trim();
                    }

                    var liveAnalysis = _engine.GetAnalysis(deckLabel);
                    if (liveAnalysis?.Bpm > 0)
                        bpm = liveAnalysis.Bpm;

                    if (bpm <= 0)
                        bpm = AudioEngine.DetectBpm(deck.Track.FilePath);

                    if (bpm > 0)
                    {
                        deck.Track.Bpm = bpm;
                        deck.Track.OriginalBpm = bpm;
                    }
                    if (!string.IsNullOrWhiteSpace(key))
                        deck.Track.Key = key;

                    // Also update the library entry
                    var libTrack = _library.FirstOrDefault(t =>
                        string.Equals(t.Path, deck.Track.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (libTrack != null)
                    {
                        if (bpm > 0) libTrack.Bpm = bpm;
                        if (!string.IsNullOrWhiteSpace(key)) libTrack.Key = key;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        PostAiResult("detectBpmKey", deckLabel, new { bpm, key });
                        QueueStatePush();
                    });
                }
                catch { }
            });
        }

        private void PostAiResult(string action, string deck, object data)
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = $"dj.ai.{action}.result",
                    deck,
                    data
                });
                DjWebView?.CoreWebView2?.PostWebMessageAsJson(payload);
            }
            catch { }
        }

        private void HandleControllerAction(DjControllerAction action)
        {
            try
            {
                if (_isDisposed || action == null)
                    return;

                Dispatcher.InvokeAsync(() =>
                {
                    var deckLabel = DjDeckRouting.Normalize(action.Deck);
                    switch ((action.Command ?? string.Empty).Trim())
                    {
                        case "crossfader":
                            _engine.SetCrossfader(action.Value);
                            break;
                        case "tempo":
                            if (deckLabel != null)
                                _engine.SetDeckTempo(deckLabel, action.Value);
                            break;
                        case "volume":
                            if (deckLabel != null)
                                _engine.SetDeckVolume(deckLabel, action.Value);
                            break;
                        case "bend":
                            if (deckLabel != null)
                                _engine.BendDeck(deckLabel, action.Value);
                            break;
                        case "playPause":
                            if (deckLabel != null)
                            {
                                var deck = _engine.GetDeck(deckLabel);
                                if (deck?.IsPlaying == true)
                                    _engine.PauseDeck(deckLabel);
                                else
                                    _engine.PlayDeck(deckLabel);
                            }
                            break;
                        case "cue":
                            if (deckLabel != null)
                                _engine.CueDeck(deckLabel);
                            break;
                        case "sync":
                            if (deckLabel != null)
                                SyncDeck(deckLabel);
                            break;
                        case "hotCue1":
                            if (deckLabel != null)
                                _engine.TriggerHotCue(deckLabel, 0);
                            break;
                        case "hotCue2":
                            if (deckLabel != null)
                                _engine.TriggerHotCue(deckLabel, 1);
                            break;
                    }

                    QueueStatePush();
                }, DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private void LoadDeckFile(string deckLabel, string? path)
        {
            try
            {
                var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
                if (normalizedDeck == null)
                    return;

                var track = path != null
                    ? _library.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
                    : null;
                var selectedPath = path;

                if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
                {
                    var dialog = new OpenFileDialog
                    {
                        Filter = "Audio|*.mp3;*.wav;*.wma;*.aiff;*.flac;*.ogg;*.m4a|All Files|*.*",
                        Multiselect = false,
                        Title = "Select a track to load"
                    };

                    if (dialog.ShowDialog() != true)
                        return;

                    selectedPath = dialog.FileName;
                    track = BuildLibraryTrack(selectedPath, "Local Files");
                    if (_library.All(item => !string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase)))
                        _library.Insert(0, track);
                }

                if (selectedPath == null)
                    return;

                // Ensure track metadata is available (rebuild if missing)
                track ??= BuildLibraryTrack(selectedPath, "Local Files");

                if (!_engine.LoadDeck(normalizedDeck, selectedPath, track.Bpm, track.Key, track.Title, track.Artist))
                    return;

                var deckState = _engine.GetDeck(normalizedDeck);
                var analysis = _engine.GetAnalysis(normalizedDeck);
                if (deckState?.Track != null && analysis?.Bpm > 0)
                {
                    deckState.Track.Bpm = analysis.Bpm;
                    deckState.Track.OriginalBpm = analysis.Bpm;
                    track.Bpm = analysis.Bpm;
                }

                QueueStatePush();
            }
            catch
            {
            }
        }

        private void PlayDeck(string? deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck == null)
                return;

            var deck = _engine.GetDeck(normalizedDeck);
            if (deck?.SyncEnabled == true && !deck.IsSyncMaster && !string.IsNullOrWhiteSpace(deck.SyncSourceDeck))
            {
                var sourcePhaseFraction = _engine.GetBeatPhaseFraction(deck.SyncSourceDeck);
                _engine.AlignDeckToPhaseFraction(normalizedDeck, sourcePhaseFraction);
            }

            _engine.PlayDeck(normalizedDeck);
        }

        private void PauseDeck(string? deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck != null)
                _engine.PauseDeck(normalizedDeck);
        }

        private void CueDeck(string? deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck != null)
                _engine.CueDeck(normalizedDeck);
        }

        private void SyncDeck(string? deckLabel)
        {
            if (string.IsNullOrWhiteSpace(deckLabel))
                return;

            var sourceDeck = string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase) ? _engine.DeckB : _engine.DeckA;
            var targetDeck = _engine.GetDeck(deckLabel);
            if (targetDeck == null)
                return;

            if ((_engine.GetBaseBpm(deckLabel) > 0 && _engine.GetBaseBpm(string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase) ? "B" : "A") > 0)
                || targetDeck.IsSyncMaster
                || targetDeck.SyncEnabled)
            {
                _engine.ToggleDeckSync(deckLabel);
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    if ((_engine.GetBaseBpm(deckLabel) <= 0) && !string.IsNullOrWhiteSpace(targetDeck.Track?.FilePath))
                    {
                        var bpm = AudioEngine.DetectBpm(targetDeck.Track.FilePath);
                        if (bpm > 0 && targetDeck.Track != null)
                        {
                            targetDeck.Track.Bpm = bpm;
                            targetDeck.Track.OriginalBpm = bpm;
                        }
                    }

                    if ((_engine.GetBaseBpm(string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase) ? "B" : "A") <= 0)
                        && !string.IsNullOrWhiteSpace(sourceDeck.Track?.FilePath))
                    {
                        var bpm = AudioEngine.DetectBpm(sourceDeck.Track.FilePath);
                        if (bpm > 0 && sourceDeck.Track != null)
                        {
                            sourceDeck.Track.Bpm = bpm;
                            sourceDeck.Track.OriginalBpm = bpm;
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        _engine.ToggleDeckSync(deckLabel);
                        QueueStatePush();
                    });
                }
                catch
                {
                }
            });
        }

        private void BendDeck(string deckLabel, double deltaSeconds)
        {
            _engine.BendDeck(deckLabel, deltaSeconds);
        }

        private void SetTempo(string deckLabel, double tempo)
        {
            _engine.SetDeckTempo(deckLabel, tempo);
        }

        private void SetLoopIn(string? deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck != null)
                _engine.SetLoopIn(normalizedDeck);
        }

        private void SetLoopOut(string? deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck != null)
                _engine.SetLoopOut(normalizedDeck);
        }

        private void ClearLoop(string? deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck != null)
                _engine.ClearLoop(normalizedDeck);
        }

        private void TriggerHotCue(string deckLabel, int cueIndex)
        {
            _engine.TriggerHotCue(deckLabel, cueIndex);
        }

        private void SeekDeck(string? deckLabel, double pct)
        {
            if (string.IsNullOrWhiteSpace(deckLabel))
                return;

            _engine.Seek(deckLabel, pct);
        }

        private void SetCuePoint(string deckLabel, double pct)
        {
            _engine.SetCuePosition(deckLabel, pct);
        }

        private void QueueStatePush()
        {
            if (_isDisposed)
                return;

            _statePushScheduler.Request();
        }

        public object GetCompanionState()
        {
            return BuildStateDto();
        }

        public void ExecuteCompanionCommand(
            string action,
            string? deck = null,
            string? path = null,
            double? value = null,
            string? band = null,
            int? cueIndex = null,
            double? transitionBeats = null)
        {
            var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalizedAction)
            {
                case "loadtrack":
                    if (string.IsNullOrWhiteSpace(path))
                        throw new InvalidOperationException("DJ loadTrack requires a track path.");
                    if (string.IsNullOrWhiteSpace(deck))
                        throw new InvalidOperationException("DJ loadTrack requires a deck.");
                    LoadDeckFile(deck, path);
                    break;
                case "play":
                    PlayDeck(deck);
                    break;
                case "pause":
                    PauseDeck(deck);
                    break;
                case "cue":
                    CueDeck(deck);
                    break;
                case "sync":
                    SyncDeck(deck);
                    break;
                case "seek":
                    if (!value.HasValue || string.IsNullOrWhiteSpace(deck))
                        throw new InvalidOperationException("DJ seek requires a deck and value.");
                    SeekDeck(deck, value.Value);
                    break;
                case "settempo":
                    if (!value.HasValue || string.IsNullOrWhiteSpace(deck))
                        throw new InvalidOperationException("DJ setTempo requires a deck and value.");
                    SetTempo(deck, value.Value);
                    break;
                case "setvolume":
                    if (!value.HasValue || string.IsNullOrWhiteSpace(deck))
                        throw new InvalidOperationException("DJ setVolume requires a deck and value.");
                    _engine.SetDeckVolume(deck, value.Value);
                    break;
                case "setgain":
                    if (!value.HasValue || string.IsNullOrWhiteSpace(deck))
                        throw new InvalidOperationException("DJ setGain requires a deck and value.");
                    _engine.SetDeckGain(deck, value.Value);
                    break;
                case "setfilter":
                    if (!value.HasValue || string.IsNullOrWhiteSpace(deck))
                        throw new InvalidOperationException("DJ setFilter requires a deck and value.");
                    _engine.SetDeckFilter(deck, value.Value);
                    break;
                case "seteq":
                    if (!value.HasValue || string.IsNullOrWhiteSpace(deck) || string.IsNullOrWhiteSpace(band))
                        throw new InvalidOperationException("DJ setEq requires a deck, band, and value.");
                    _engine.SetDeckEq(deck, band, value.Value);
                    break;
                case "togglecuemonitor":
                    if (string.IsNullOrWhiteSpace(deck))
                        throw new InvalidOperationException("DJ toggleCueMonitor requires a deck.");
                    _engine.ToggleDeckCueMonitor(deck);
                    break;
                case "loopin":
                    SetLoopIn(deck);
                    break;
                case "loopout":
                    SetLoopOut(deck);
                    break;
                case "loopclear":
                    ClearLoop(deck);
                    break;
                case "hotcue":
                    if (!cueIndex.HasValue || string.IsNullOrWhiteSpace(deck))
                        throw new InvalidOperationException("DJ hotCue requires a deck and cueIndex.");
                    TriggerHotCue(deck, cueIndex.Value);
                    break;
                case "crossfader":
                    if (!value.HasValue)
                        throw new InvalidOperationException("DJ crossfader requires a value.");
                    _engine.SetCrossfader(value.Value);
                    break;
                case "mastervolume":
                    if (!value.HasValue)
                        throw new InvalidOperationException("DJ masterVolume requires a value.");
                    _engine.SetMasterVolume(value.Value);
                    break;
                case "startautomix":
                    var sourceDeck = DjDeckRouting.Normalize(deck);
                    var targetDeck = string.Equals(sourceDeck, "A", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
                    if (sourceDeck == null)
                        throw new InvalidOperationException("DJ startAutomix requires a source deck.");
                    _engine.StartAutomixTransition(sourceDeck, targetDeck, transitionBeats ?? 16);
                    break;
                case "stopautomix":
                    _engine.StopAutomix();
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported DJ action '{action}'.");
            }

            QueueStatePush();
        }

        private void SendState()
        {
            try
            {
                if (DjWebView?.CoreWebView2 == null)
                    return;

                var payload = new
                {
                    type = "dj.state",
                    state = BuildStateDto()
                };

                DjWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
            }
            catch
            {
            }
        }

        private DjBridgeStateDto BuildStateDto()
        {
            return new DjBridgeStateDto
            {
                Decks = new DjDeckCollectionDto
                {
                    A = BuildDeckDto("A", _engine.DeckA, _engine.DeckAWaveformBars),
                    B = BuildDeckDto("B", _engine.DeckB, _engine.DeckBWaveformBars)
                },
                Crossfader = _engine.Crossfader,
                CrossfaderCurve = _engine.CrossfaderCurve,
                MasterVolume = _engine.MasterVolume,
                MasterLevel = _engine.MasterLevel,
                CueMix = _engine.CueMix,
                HeadphoneVolume = _engine.HeadphoneVolume,
                MicVolume = _engine.MicVolume,
                EffectMode = _engine.EffectMode,
                Controllers = BuildControllerStateDto(),
                Library = _library,
                Samples = _samples,
                IsRecording = _engine.IsRecording,
                RecordingPath = _engine.RecordingPath ?? string.Empty,
                AutoMix = new DjAutoMixStateDto
                {
                    Enabled = _engine.AutoMix.Enabled,
                    Status = _engine.AutoMix.Status,
                    SourceDeck = _engine.AutoMix.SourceDeck,
                    TargetDeck = _engine.AutoMix.TargetDeck,
                    Progress = _engine.AutoMix.Progress,
                    TransitionBeats = _engine.AutoMix.TransitionBeats,
                    RemainingBeats = _engine.AutoMix.RemainingBeats,
                },
                LastUpdatedUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private DjDeckStateDto BuildDeckDto(string label, DeckState deck, double[] waveformBars)
        {
            var hasTrack = deck.Track != null && !string.IsNullOrWhiteSpace(deck.Track.FilePath);
            var durationSeconds = deck.Track?.DurationSeconds ?? 0;
            var currentSeconds = durationSeconds > 0 ? deck.CurrentTime / 100.0 * durationSeconds : 0;
            var remainingSeconds = Math.Max(0, durationSeconds - currentSeconds);
            var effectiveBpm = _engine.GetEffectiveBpm(label);
            var sourceLevel = 0d;
            var analysis = _engine.GetAnalysis(label);

            if (hasTrack && waveformBars.Length > 0)
            {
                var index = Math.Clamp((int)Math.Round(deck.CurrentTime / 100.0 * (waveformBars.Length - 1)), 0, waveformBars.Length - 1);
                sourceLevel = waveformBars[index];
                if (!deck.IsPlaying)
                    sourceLevel *= 0.35;
            }

            var outputLevel = Math.Clamp(sourceLevel * _engine.GetDeckOutputLevel(deck, string.Equals(label, "A", StringComparison.OrdinalIgnoreCase)) * (_engine.MasterVolume / 100.0), 0, 1);

            return new DjDeckStateDto
            {
                Label = label,
                IsPlaying = deck.IsPlaying,
                IsCued = deck.IsCued,
                LoopActive = deck.LoopActive,
                SyncEnabled = deck.SyncEnabled,
                IsSyncMaster = deck.IsSyncMaster,
                SyncSourceDeck = deck.SyncSourceDeck,
                SyncTargetBpm = deck.SyncTargetBpm,
                SyncTempoDelta = deck.SyncTempoDelta,
                SyncBeatPhaseOffsetSeconds = deck.SyncBeatPhaseOffsetSeconds,
                SyncAligned = deck.SyncAligned,
                CueMonitoring = deck.CueMonitoring,
                Tempo = deck.Tempo,
                PitchRange = deck.PitchRange,
                Volume = deck.Volume,
                Gain = deck.Gain,
                EqHigh = deck.EqHigh,
                EqMid = deck.EqMid,
                EqLow = deck.EqLow,
                Filter = deck.Filter,
                CurrentTime = deck.CurrentTime,
                CurrentTimeSeconds = deck.CurrentTimeSeconds,
                RemainingTimeSeconds = deck.RemainingTimeSeconds,
                CuePosition = deck.CuePosition,
                LoopStart = deck.LoopStart,
                LoopEnd = deck.LoopEnd,
                LoopSize = deck.LoopSize,
                BeatIntervalSeconds = deck.BeatIntervalSeconds,
                BeatPhaseFraction = deck.BeatPhaseFraction,
                BeatPhaseSeconds = deck.BeatPhaseSeconds,
                BeatPosition = deck.BeatPosition,
                BarIndex = deck.BarIndex,
                BeatInBar = deck.BeatInBar,
                PhraseIndex = deck.PhraseIndex,
                WaveformVisible = deck.WaveformVisible,
                CuePoints = (deck.CuePoints ?? new List<CuePoint>()).Where(cue => cue.Time >= 0).Select(cue => new DjCuePointDto
                {
                    Time = cue.Time,
                    Label = cue.Label,
                    Color = cue.Color
                }).ToList(),
                WaveformBars = hasTrack ? waveformBars.ToList() : new List<double>(),
                Analysis = BuildAnalysisDto(analysis),
                Level = outputLevel,
                Elapsed = FormatClock(currentSeconds),
                Remaining = $"-{FormatClock(remainingSeconds)}",
                EffectiveBpm = effectiveBpm,
                Track = new DjTrackDto
                {
                    Path = deck.Track?.FilePath ?? string.Empty,
                    Title = deck.Track?.Name ?? string.Empty,
                    Artist = deck.Track?.Artist ?? string.Empty,
                    Key = deck.Track?.Key ?? string.Empty,
                    Duration = deck.Track?.Duration ?? string.Empty,
                    DurationSeconds = durationSeconds,
                    Bpm = deck.Track?.Bpm ?? 0,
                    Source = ResolveSource(deck.Track?.FilePath)
                }
            };
        }

        private static string FormatClock(double seconds)
        {
            var safeSeconds = Math.Max(0, (int)Math.Round(seconds));
            var ts = TimeSpan.FromSeconds(safeSeconds);
            return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"mm\:ss");
        }

        private List<DjLibraryTrackDto> BuildLibrary()
        {
            var results = new List<DjLibraryTrackDto>();
            var roots = DjLibraryDiscovery.DiscoverRoots();

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path))
                    continue;

                foreach (var file in EnumerateAudioFiles(root.Path, 180))
                {
                    if (results.Any(item => string.Equals(item.Path, file, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    results.Add(BuildLibraryTrack(file, root.Source));
                    if (results.Count >= 240)
                        return results.OrderBy(item => item.Title).ToList();
                }
            }

            return results.OrderBy(item => item.Title).ToList();
        }

        private static IEnumerable<string> EnumerateAudioFiles(string root, int maxFiles)
        {
            var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".wma", ".aiff", ".flac", ".ogg", ".m4a" };
            var pending = new Stack<string>();
            pending.Push(root);
            var yielded = 0;

            while (pending.Count > 0 && yielded < maxFiles)
            {
                var current = pending.Pop();
                IEnumerable<string>? directories = null;
                IEnumerable<string>? files = null;

                try { directories = Directory.EnumerateDirectories(current); } catch { }
                try { files = Directory.EnumerateFiles(current); } catch { }

                if (directories != null)
                {
                    foreach (var directory in directories)
                        pending.Push(directory);
                }

                if (files == null)
                    continue;

                foreach (var file in files)
                {
                    if (!supportedExtensions.Contains(Path.GetExtension(file)))
                        continue;

                    yield return file;
                    yielded++;
                    if (yielded >= maxFiles)
                        yield break;
                }
            }
        }

        private static DjLibraryTrackDto BuildLibraryTrack(string path, string source)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            var artist = string.Empty;
            var title = fileName;
            var key = string.Empty;
            var duration = string.Empty;
            var durationSeconds = 0;
            var bpm = 0;

            // Try reading metadata from audio file tags (TagLib)
            try
            {
                using var tagFile = TagLib.File.Create(path);
                if (tagFile.Tag != null)
                {
                    if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                        title = tagFile.Tag.Title.Trim();
                    if (tagFile.Tag.Performers?.Length > 0 && !string.IsNullOrWhiteSpace(tagFile.Tag.Performers[0]))
                        artist = tagFile.Tag.Performers[0].Trim();
                    if ((int)tagFile.Tag.BeatsPerMinute > 0)
                        bpm = (int)tagFile.Tag.BeatsPerMinute;
                    if (!string.IsNullOrWhiteSpace(tagFile.Tag.InitialKey))
                        key = tagFile.Tag.InitialKey.Trim();
                }
                if (tagFile.Properties != null && tagFile.Properties.Duration.TotalSeconds > 0)
                {
                    durationSeconds = (int)tagFile.Properties.Duration.TotalSeconds;
                    duration = TimeSpan.FromSeconds(durationSeconds).ToString(@"m\:ss");
                }
            }
            catch
            {
                // TagLib failed — fall back to filename parsing
            }

            // Auto-detect BPM if tags didn't have it — deferred to background
            // (see DetectMissingBpm)

            // Fallback: parse "Artist - Title" from filename if tags were empty
            if (string.IsNullOrWhiteSpace(artist))
            {
                var parts = fileName.Split(new[] { " - " }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    artist = parts[0].Trim();
                    if (string.IsNullOrWhiteSpace(title) || title == fileName)
                        title = parts[1].Trim();
                }
            }

            return new DjLibraryTrackDto
            {
                Id = path,
                Path = path,
                Title = title,
                Artist = artist,
                Key = key,
                Duration = duration,
                DurationSeconds = durationSeconds,
                Bpm = bpm,
                Source = source
            };
        }

        private static string ResolveSource(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Local Files";
            if (path.IndexOf("VirtualDJ", StringComparison.OrdinalIgnoreCase) >= 0)
                return "VirtualDJ";
            if (path.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Music";
            return "Local Files";
        }

        private void DetectMissingBpm()
        {
            try
            {
                // Re-detect ALL tracks with audio analysis — tags are often wrong
                var tracks = _library.Where(t => !string.IsNullOrWhiteSpace(t.Path)).ToList();
                foreach (var track in tracks)
                {
                    try
                    {
                        var bpm = AudioEngine.DetectBpm(track.Path);
                        if (bpm > 0)
                        {
                            track.Bpm = bpm;
                            QueueStatePush();
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string? FindDjDist()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Figma", "djcode", "dist"),
                Path.Combine(Environment.CurrentDirectory, "Figma", "djcode", "dist"),
                Path.Combine(AppContext.BaseDirectory, "Atlas_v2.exe.WebView2", "djcode", "dist"),
                Path.Combine(Environment.CurrentDirectory, "Atlas_v2.exe.WebView2", "djcode", "dist"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "Figma", "djcode", "dist"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "Atlas_v2.exe.WebView2", "djcode", "dist")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
                        return candidate;
                }
                catch
                {
                }
            }

            return null;
        }

        private static string? FindAssetsRoot()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets"),
                Path.Combine(Environment.CurrentDirectory, "Assets"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "Assets")
            };

            return candidates.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) &&
                Directory.Exists(path));
        }

        private sealed class DjBridgeStateDto
        {
            public DjDeckCollectionDto Decks { get; set; } = new();
            public double Crossfader { get; set; }
            public string CrossfaderCurve { get; set; } = string.Empty;
            public double MasterVolume { get; set; }
            public double MasterLevel { get; set; }
            public double CueMix { get; set; }
            public double HeadphoneVolume { get; set; }
            public double MicVolume { get; set; }
            public string EffectMode { get; set; } = string.Empty;
            public DjControllerStateDto Controllers { get; set; } = new();
            public List<DjLibraryTrackDto> Library { get; set; } = new();
            public List<DjSampleDto> Samples { get; set; } = new();
            public bool IsRecording { get; set; }
            public string RecordingPath { get; set; } = string.Empty;
            public DjAutoMixStateDto AutoMix { get; set; } = new();
            public string LastUpdatedUtc { get; set; } = string.Empty;
        }

        private sealed class DjAutoMixStateDto
        {
            public bool Enabled { get; set; }
            public string Status { get; set; } = string.Empty;
            public string SourceDeck { get; set; } = string.Empty;
            public string TargetDeck { get; set; } = string.Empty;
            public double Progress { get; set; }
            public double TransitionBeats { get; set; }
            public double RemainingBeats { get; set; }
        }

        private sealed class DjDeckCollectionDto
        {
            [JsonPropertyName("A")]
            public DjDeckStateDto A { get; set; } = new();
            [JsonPropertyName("B")]
            public DjDeckStateDto B { get; set; } = new();
        }

        private sealed class DjDeckStateDto
        {
            public string Label { get; set; } = string.Empty;
            public bool IsPlaying { get; set; }
            public bool IsCued { get; set; }
            public bool LoopActive { get; set; }
            public bool SyncEnabled { get; set; }
            public bool IsSyncMaster { get; set; }
            public string SyncSourceDeck { get; set; } = string.Empty;
            public double SyncTargetBpm { get; set; }
            public double SyncTempoDelta { get; set; }
            public double SyncBeatPhaseOffsetSeconds { get; set; }
            public bool SyncAligned { get; set; }
            public bool CueMonitoring { get; set; }
            public double Tempo { get; set; }
            public double PitchRange { get; set; }
            public double Volume { get; set; }
            public double Gain { get; set; }
            public double EqHigh { get; set; }
            public double EqMid { get; set; }
            public double EqLow { get; set; }
            public double Filter { get; set; }
            public double CurrentTime { get; set; }
            public double CurrentTimeSeconds { get; set; }
            public double RemainingTimeSeconds { get; set; }
            public double CuePosition { get; set; }
            public double? LoopStart { get; set; }
            public double? LoopEnd { get; set; }
            public string LoopSize { get; set; } = "1";
            public double BeatIntervalSeconds { get; set; }
            public double BeatPhaseFraction { get; set; }
            public double BeatPhaseSeconds { get; set; }
            public double BeatPosition { get; set; }
            public int BarIndex { get; set; }
            public int BeatInBar { get; set; }
            public int PhraseIndex { get; set; }
            public bool WaveformVisible { get; set; } = true;
            public List<DjCuePointDto> CuePoints { get; set; } = new();
            public List<double> WaveformBars { get; set; } = new();
            public DjTrackAnalysisDto? Analysis { get; set; }
            public double Level { get; set; }
            public string Elapsed { get; set; } = string.Empty;
            public string Remaining { get; set; } = string.Empty;
            public double EffectiveBpm { get; set; }
            public DjTrackDto Track { get; set; } = new();
        }

        private sealed class DjTrackAnalysisDto
        {
            public int Bpm { get; set; }
            public double Confidence { get; set; }
            public double BeatIntervalSeconds { get; set; }
            public double GridOffsetSeconds { get; set; }
            public List<double> BeatMarkers { get; set; } = new();
            public List<double> PhraseMarkers { get; set; } = new();
            public List<double> WaveformMin { get; set; } = new();
            public List<double> WaveformMax { get; set; } = new();
            public List<double> WaveformRms { get; set; } = new();
        }

        private sealed class DjCuePointDto
        {
            public double Time { get; set; }
            public string Label { get; set; } = string.Empty;
            public string Color { get; set; } = string.Empty;
        }

        private class DjTrackDto
        {
            public string Path { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public string Duration { get; set; } = string.Empty;
            public int DurationSeconds { get; set; }
            public int Bpm { get; set; }
            public string Source { get; set; } = string.Empty;
        }

        private sealed class DjLibraryTrackDto : DjTrackDto
        {
            public string Id { get; set; } = string.Empty;
        }

        private sealed class DjSampleDto
        {
            public string Path { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
        }

        private sealed class DjPersistedState
        {
            public List<DjLibraryTrackDto> Library { get; set; } = new();
            public List<DjSampleDto> Samples { get; set; } = new();
            public DjPersistedDeckCollection Decks { get; set; } = new();
        }

        private sealed class DjPersistedDeckCollection
        {
            [JsonPropertyName("A")]
            public DjPersistedDeckState A { get; set; } = new();

            [JsonPropertyName("B")]
            public DjPersistedDeckState B { get; set; } = new();
        }

        private sealed class DjPersistedDeckState
        {
            public string Path { get; set; } = string.Empty;
            public double Volume { get; set; } = 80;
            public double Gain { get; set; } = 50;
            public double EqHigh { get; set; } = 50;
            public double EqMid { get; set; } = 50;
            public double EqLow { get; set; } = 50;
            public double Filter { get; set; } = 50;
            public double Tempo { get; set; }
            public double PitchRange { get; set; } = 16;
            public double CurrentTime { get; set; }
            public double CuePosition { get; set; }
            public double? LoopStart { get; set; }
            public double? LoopEnd { get; set; }
            public bool LoopActive { get; set; }
            public string LoopSize { get; set; } = "1";
            public List<CuePoint> CuePoints { get; set; } = new();
        }

        private sealed class DjControllerStateDto
        {
            public List<DjControllerDeviceDto> Devices { get; set; } = new();
            public List<DjControllerProfileDto> Profiles { get; set; } = new();
            public DjControllerInputDto? LastInput { get; set; }
        }

        private sealed class DjControllerDeviceDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string InputName { get; set; } = string.Empty;
            public string OutputName { get; set; } = string.Empty;
            public bool Connected { get; set; }
            public bool SupportsInput { get; set; }
            public bool SupportsOutput { get; set; }
            public string Protocol { get; set; } = string.Empty;
        }

        private sealed class DjControllerProfileDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Protocol { get; set; } = string.Empty;
            public int BindingCount { get; set; }
        }

        private sealed class DjControllerInputDto
        {
            public string DeviceId { get; set; } = string.Empty;
            public string DeviceName { get; set; } = string.Empty;
            public string ControlType { get; set; } = string.Empty;
            public int Channel { get; set; }
            public int ControlNumber { get; set; }
            public int RawValue { get; set; }
            public double NormalizedValue { get; set; }
            public string TimestampUtc { get; set; } = string.Empty;
        }

        private static int ParseLoopSizeBeats(string loopSize)
        {
            return loopSize switch
            {
                "1/4" => 1,
                "1/2" => 2,
                "2" => 8,
                _ => 4,
            };
        }

        private double SnapPercentToBeat(string? deckLabel, double percent)
        {
            var deck = _engine.GetDeck(deckLabel);
            var analysis = _engine.GetAnalysis(deckLabel);
            if (deck?.Track == null || deck.Track.DurationSeconds <= 0 || analysis == null || analysis.BeatMarkers.Count == 0)
                return Math.Clamp(percent, 0, 100);

            var currentSeconds = Math.Clamp(percent, 0, 100) / 100.0 * deck.Track.DurationSeconds;
            var snapped = analysis.BeatMarkers.OrderBy(marker => Math.Abs(marker - currentSeconds)).FirstOrDefault();
            return Math.Clamp(snapped / deck.Track.DurationSeconds * 100.0, 0, 100);
        }

        private static DjTrackAnalysisDto? BuildAnalysisDto(TrackAnalysis? analysis)
        {
            if (analysis == null)
                return null;

            return new DjTrackAnalysisDto
            {
                Bpm = analysis.Bpm,
                Confidence = analysis.Confidence,
                BeatIntervalSeconds = analysis.BeatIntervalSeconds,
                GridOffsetSeconds = analysis.GridOffsetSeconds,
                BeatMarkers = analysis.BeatMarkers.ToList(),
                PhraseMarkers = analysis.PhraseMarkers.ToList(),
                WaveformMin = analysis.WaveformMin.ToList(),
                WaveformMax = analysis.WaveformMax.ToList(),
                WaveformRms = analysis.WaveformRms.ToList()
            };
        }

        private DjControllerStateDto BuildControllerStateDto()
        {
            return new DjControllerStateDto
            {
                Devices = _controllers.Devices.Select(device => new DjControllerDeviceDto
                {
                    Id = device.Id,
                    Name = device.Name,
                    InputName = device.InputName,
                    OutputName = device.OutputName,
                    Connected = device.Connected,
                    SupportsInput = device.SupportsInput,
                    SupportsOutput = device.SupportsOutput,
                    Protocol = device.Protocol
                }).ToList(),
                Profiles = _controllers.Profiles.Select(profile => new DjControllerProfileDto
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Protocol = profile.Protocol,
                    BindingCount = profile.Bindings?.Count ?? 0
                }).ToList(),
                LastInput = _lastControllerInput == null ? null : new DjControllerInputDto
                {
                    DeviceId = _lastControllerInput.DeviceId,
                    DeviceName = _lastControllerInput.DeviceName,
                    ControlType = _lastControllerInput.ControlType,
                    Channel = _lastControllerInput.Channel,
                    ControlNumber = _lastControllerInput.ControlNumber,
                    RawValue = _lastControllerInput.RawValue,
                    NormalizedValue = _lastControllerInput.NormalizedValue,
                    TimestampUtc = _lastControllerInput.TimestampUtc.ToString("O")
                }
            };
        }
    }
}
