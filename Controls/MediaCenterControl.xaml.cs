using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using AtlasAI.Core;
using AtlasAI.Integrations;
using LottieSharp.WPF;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using AtlasAI.MediaScanner;
using AtlasAI.Streaming;
using AtlasAI.Views.ViewModels;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AtlasAI.Controls
{
    public partial class MediaCenterControl : UserControl, INotifyPropertyChanged
    {
        private readonly MediaPlaybackService _playbackService;
        private readonly MediaCentreViewModel _vm;
        private bool _isLoaded;
        private bool _exitHooked;
        private Window? _hostWindow;
        private bool _hostKeyboardHooksHooked;
        private AtlasAI.MediaScanner.MediaItem? _currentMedia;
        private DateTime _lastServerCatalogLoadMoreUtc = DateTime.MinValue;
        private Window? _exclusivePlayerWindow;
        private bool _isExclusiveFullscreen;
        private AtlasAI.FullscreenPlayerWindow? _videoPlayerWindow;
        private Panel? _playerOriginalParent;
        private int _playerOriginalIndex = -1;
        private int _playerOriginalRow;
        private int _playerOriginalColumn;
        private int _playerOriginalRowSpan;
        private int _playerOriginalColumnSpan;
        private int _playerOriginalZIndex;
        private HorizontalAlignment _playerOriginalHorizontalAlignment;
        private VerticalAlignment _playerOriginalVerticalAlignment;
        private Thickness _playerOriginalMargin;
        private double _playerOriginalWidth;
        private double _playerOriginalHeight;
        private double _exclusiveResumeSeconds;
        private bool _exclusiveResumeWasPlaying;
        private bool _isPlayerMinimized;
        private bool _isSidebarCollapsed;
        private bool _serversFullPageRestoreCaptured;
        private Visibility _restoreLibraryStatsVisibility = Visibility.Visible;
        private Visibility _restoreLibraryHeaderVisibility = Visibility.Visible;
        private GridLength _restoreServersRightPanelWidth = GridLength.Auto;
        private Thickness _restoreServersMainMargin;
        private bool _defaultServersApplied;
        private GridLength _restoreNeoNavWidth = new GridLength(200);
        private Visibility _restoreHeaderVisibility = Visibility.Visible;
        private Visibility _restoreNeoReopenVisibility = Visibility.Collapsed;
        private Visibility _restoreShellTopNavVisibility = Visibility.Visible;
        // Immersive mode (toggled from MediaHub React frontend)
        private bool _isMediaHubImmersive;
        private GridLength _restoreNeoNavWidthImmersive = new GridLength(200);
        // Stored reference — set by ServersView on Loaded, cleared on Unloaded
        private WeakReference<AtlasAI.Views.MediaCentre.ServersView>? _activeServersViewRef;

        private bool _prefsHooked;
        private bool _suppressSidebarLottiePicker;
        private bool _sidebarLottiePickerInitialized;
        private System.Windows.Threading.DispatcherTimer? _mediaVoiceNoteTimer;
        private readonly AddonManifestService _addonManifestService = new();
        private static readonly HttpClient AddonDirectoryHttpClient = CreateAddonDirectoryHttpClient();
        private const string StremioAddonsCatalogUrl = "https://stremio-addons.net/api/addon_catalog/all/stremio-addons.net.json?skip=0";
        private readonly ObservableCollection<AddonBrowserItem> _addonBrowserItems = new();
        private readonly ICollectionView _addonBrowserItemsView;
        private string _networkSummary = "Refreshing network status...";
        private string _wifiNetworksText = "Run a Wi-Fi scan to list nearby networks.";
        private string _selectedAddonScope = "STREMIO-ADDONS.NET";
        private string _selectedAddonType = "All";
        private string _addonSearchText = "";
        private string _addonBrowserStatus = "Loading addon directory...";
        private string _pendingAddonUrl = "";
        private string _addonInstallStatus = "";
        private bool _isAddonBrowserBusy;
        private bool _isAddAddonDialogOpen;
        private int _remoteAddonCatalogCount;

        public IReadOnlyList<string> AddonScopeOptions { get; } = new[] { "Installed", "STREMIO-ADDONS.NET" };

        public IReadOnlyList<string> AddonTypeOptions { get; } = new[]
        {
            "All",
            "Movie",
            "Series",
            "TV",
            "Channel",
            "Anime",
            "Other"
        };

        public ICollectionView AddonBrowserItemsView => _addonBrowserItemsView;


        public string SelectedAddonScope
        {
            get => _selectedAddonScope;
            set
            {
                if (!string.Equals(_selectedAddonScope, value, StringComparison.Ordinal))
                {
                    _selectedAddonScope = value;
                    OnPropertyChanged(nameof(SelectedAddonScope));
                    OnPropertyChanged(nameof(IsInstalledAddonScopeSelected));
                    OnPropertyChanged(nameof(IsDirectoryAddonScopeSelected));
                    RefreshAddonBrowserFilter();
                    _ = RefreshAddonBrowserAsync();
                }
            }
        }

        public string SelectedAddonType
        {
            get => _selectedAddonType;
            set
            {
                if (!string.Equals(_selectedAddonType, value, StringComparison.Ordinal))
                {
                    _selectedAddonType = value;
                    OnPropertyChanged(nameof(SelectedAddonType));
                    RefreshAddonBrowserFilter();
                }
            }
        }

        public string AddonSearchText
        {
            get => _addonSearchText;
            set
            {
                if (!string.Equals(_addonSearchText, value, StringComparison.Ordinal))
                {
                    _addonSearchText = value;
                    OnPropertyChanged(nameof(AddonSearchText));
                    RefreshAddonBrowserFilter();
                }
            }
        }

        public string AddonBrowserStatus
        {
            get => _addonBrowserStatus;
            set
            {
                if (!string.Equals(_addonBrowserStatus, value, StringComparison.Ordinal))
                {
                    _addonBrowserStatus = value;
                    OnPropertyChanged(nameof(AddonBrowserStatus));
                }
            }
        }

        public int InstalledAddonCount => GetInstalledAddonUrls().Count;

        public int RemoteAddonCatalogCount
        {
            get => _remoteAddonCatalogCount;
            set
            {
                if (_remoteAddonCatalogCount != value)
                {
                    _remoteAddonCatalogCount = value;
                    OnPropertyChanged(nameof(RemoteAddonCatalogCount));
                }
            }
        }

        public bool IsInstalledAddonScopeSelected => string.Equals(_selectedAddonScope, "Installed", StringComparison.OrdinalIgnoreCase);

        public bool IsDirectoryAddonScopeSelected => string.Equals(_selectedAddonScope, "STREMIO-ADDONS.NET", StringComparison.OrdinalIgnoreCase);

        public bool IsAddonBrowserBusy
        {
            get => _isAddonBrowserBusy;
            set
            {
                if (_isAddonBrowserBusy != value)
                {
                    _isAddonBrowserBusy = value;
                    OnPropertyChanged(nameof(IsAddonBrowserBusy));
                }
            }
        }

        public bool IsAddAddonDialogOpen
        {
            get => _isAddAddonDialogOpen;
            set
            {
                if (_isAddAddonDialogOpen != value)
                {
                    _isAddAddonDialogOpen = value;
                    OnPropertyChanged(nameof(IsAddAddonDialogOpen));
                }
            }
        }

        public string PendingAddonUrl
        {
            get => _pendingAddonUrl;
            set
            {
                if (!string.Equals(_pendingAddonUrl, value, StringComparison.Ordinal))
                {
                    _pendingAddonUrl = value;
                    OnPropertyChanged(nameof(PendingAddonUrl));
                }
            }
        }

        public string AddonInstallStatus
        {
            get => _addonInstallStatus;
            set
            {
                if (!string.Equals(_addonInstallStatus, value, StringComparison.Ordinal))
                {
                    _addonInstallStatus = value;
                    OnPropertyChanged(nameof(AddonInstallStatus));
                }
            }
        }

        public string NetworkSummary
        {
            get => _networkSummary;
            set
            {
                if (!string.Equals(_networkSummary, value, StringComparison.Ordinal))
                {
                    _networkSummary = value;
                    OnPropertyChanged(nameof(NetworkSummary));
                }
            }
        }

        public string WifiNetworksText
        {
            get => _wifiNetworksText;
            set
            {
                if (!string.Equals(_wifiNetworksText, value, StringComparison.Ordinal))
                {
                    _wifiNetworksText = value;
                    OnPropertyChanged(nameof(WifiNetworksText));
                }
            }
        }

        public ICommand ReloadAddonServersUiCommand { get; }
        public ICommand RefreshNetworkUiCommand { get; }
        public ICommand ScanWifiUiCommand { get; }
        public ICommand OpenNetworkSettingsUiCommand { get; }
        public ICommand OpenAddAddonDialogUiCommand { get; }
        public ICommand CloseAddAddonDialogUiCommand { get; }
        public ICommand ConfirmAddAddonUiCommand { get; }
        public ICommand ShowInstalledAddonsUiCommand { get; }
        public ICommand ShowAddonDirectoryUiCommand { get; }
        public ICommand InstallAddonCardUiCommand { get; }
        public ICommand RemoveAddonCardUiCommand { get; }
        public ICommand ShareAddonCardUiCommand { get; }

        // Dynamic grid columns support
        private int _coverGridColumns = 6;
        public int CoverGridColumns
        {
            get => _coverGridColumns;
            set
            {
                if (_coverGridColumns != value)
                {
                    _coverGridColumns = value;
                    OnPropertyChanged(nameof(CoverGridColumns));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public MediaCenterControl()
        {
            InitializeComponent();

            _addonBrowserItemsView = CollectionViewSource.GetDefaultView(_addonBrowserItems);
            _addonBrowserItemsView.Filter = FilterAddonBrowserItem;

            _playbackService = MediaPlaybackService.GetOrCreate();
            _vm = new MediaCentreViewModel(_playbackService);
            DataContext = _vm;
            ReloadAddonServersUiCommand = new RelayCommand(() => AddonsReloadServers_Click(this, new RoutedEventArgs()));
            RefreshNetworkUiCommand = new RelayCommand(() => AddonsRefreshNetwork_Click(this, new RoutedEventArgs()));
            ScanWifiUiCommand = new RelayCommand(() => AddonsScanWifi_Click(this, new RoutedEventArgs()));
            OpenNetworkSettingsUiCommand = new RelayCommand(() => AddonsOpenNetworkSettings_Click(this, new RoutedEventArgs()));
            OpenAddAddonDialogUiCommand = new RelayCommand(OpenAddAddonDialog);
            CloseAddAddonDialogUiCommand = new RelayCommand(CloseAddAddonDialog);
            ConfirmAddAddonUiCommand = new RelayCommand(ConfirmAddAddon);
            ShowInstalledAddonsUiCommand = new RelayCommand(() => SetAddonBrowserScope("Installed"));
            ShowAddonDirectoryUiCommand = new RelayCommand(() => SetAddonBrowserScope("STREMIO-ADDONS.NET"));
            InstallAddonCardUiCommand = new RelayCommand<AddonBrowserItem>(InstallAddonCard);
            RemoveAddonCardUiCommand = new RelayCommand<AddonBrowserItem>(RemoveAddonCard);
            ShareAddonCardUiCommand = new RelayCommand<AddonBrowserItem>(ShareAddonCard);

            AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(MediaCenterControl_PreviewTextInput), true);
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(MediaCenterControl_PreviewKeyDown), true);

            Player.PlaybackService = _playbackService;
            Player.PlaybackEnded += Player_PlaybackEnded;
            Player.PlaybackDurationChanged += (_, seconds) => _vm.UpdatePlaybackDuration(seconds);
            Player.PlaybackPositionChanged += (_, seconds) => _vm.UpdatePlaybackPosition(seconds);
            Player.PlaybackStateChanged += (_, isPlaying) => { try { _vm.IsPlaying = isPlaying; } catch { } };
            Player.PlayerClosed += Player_PlayerClosed;
            Player.PlayerMinimized += Player_PlayerMinimized;
            Player.FullscreenRequested += Player_FullscreenRequested;

            _vm.AttachPlayerControls(Player.TogglePlayPause, Player.Stop, Player.SetVolume, Player.SeekToSeconds);
            _playbackService.CurrentMediaChanged += PlaybackService_CurrentMediaChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;
            Loaded += MediaCenterControl_Loaded;
            Unloaded += MediaCenterControl_Unloaded;
            IsVisibleChanged += MediaCenterControl_IsVisibleChanged;
            SizeChanged += (_, _) => PositionMiniPlayerIfNeeded();
        }

        private void MediaCenterControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (Visibility != Visibility.Visible)
                    return;

                if (_currentMedia == null)
                    _currentMedia = _playbackService.CurrentMedia;

                SyncEmbeddedPlayerBinding();
                ShowPlayer();
                PositionMiniPlayerIfNeeded();
            }
            catch
            {
            }
        }

        private static HttpClient CreateAddonDirectoryHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AtlasAI/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
            }
            catch
            {
            }

            return client;
        }

        private void EnsureSidebarLottieUiInitialized()
        {
            try
            {
                if (_sidebarLottiePickerInitialized)
                    return;
                _sidebarLottiePickerInitialized = true;

                ReloadSidebarLottieListItems();
                SyncSidebarLottiePickerUi(PreferencesStore.Instance.Current.MediaCentreSidebarLottie);
            }
            catch
            {
            }
        }

        private void ReloadSidebarLottieListItems()
        {
            if (SidebarLottiePickerListPanel == null)
                return;

            _suppressSidebarLottiePicker = true;
            try
            {
                SidebarLottiePickerListPanel.Children.Clear();

                SidebarLottiePickerListPanel.Children.Add(CreateSidebarLottieChoiceButton("Auto (by category)", ""));

                foreach (var fileName in GetAvailableSidebarLottieFiles())
                {
                    SidebarLottiePickerListPanel.Children.Add(
                        CreateSidebarLottieChoiceButton(Path.GetFileNameWithoutExtension(fileName), fileName));
                }
            }
            finally
            {
                _suppressSidebarLottiePicker = false;
            }
        }

        private Button CreateSidebarLottieChoiceButton(string label, string tag)
        {
            var btn = new Button
            {
                Style = TryFindResource("NeoNavSidebarItem") as Style,
                Tag = tag ?? "",
                FocusVisualStyle = TryFindResource("NeoFocusVisual") as Style,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            btn.Click += SidebarLottieChoice_Click;

            btn.Content = new TextBlock
            {
                Text = label ?? "",
                Foreground = TryFindResource("NeoTextSecondary") as Brush,
                FontFamily = TryFindResource("NeoFontFamily") as FontFamily,
                FontSize = 13,
                Margin = new Thickness(16, 0, 16, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = label ?? "",
                VerticalAlignment = VerticalAlignment.Center
            };

            return btn;
        }

        private static List<string> GetAvailableSidebarLottieFiles()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<string>();

            void AddFromDir(string dir)
            {
                try
                {
                    if (!Directory.Exists(dir)) return;
                    foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        var file = Path.GetFileName(path);
                        if (string.IsNullOrWhiteSpace(file)) continue;
                        if (found.Add(file))
                            results.Add(file);
                    }
                }
                catch
                {
                }
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            AddFromDir(Path.Combine(baseDir, "Animations"));
            AddFromDir(Path.Combine(baseDir, "Assets", "Animations", "Lottie"));

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        private void SyncSidebarLottiePickerUi(string? preferredFileName)
        {
            if (SidebarLottiePickerListPanel == null)
                return;

            var desired = Path.GetFileName((preferredFileName ?? "").Trim());

            _suppressSidebarLottiePicker = true;
            try
            {
                foreach (var child in SidebarLottiePickerListPanel.Children)
                {
                    if (child is not Button btn) continue;
                    var tag = btn.Tag?.ToString() ?? "";
                    var isActive = string.IsNullOrWhiteSpace(desired)
                        ? string.IsNullOrWhiteSpace(tag)
                        : string.Equals(Path.GetFileName(tag), desired, StringComparison.OrdinalIgnoreCase);

                    try { btn.Opacity = isActive ? 1.0 : 0.92; } catch { }
                }

                var label = string.IsNullOrWhiteSpace(desired)
                    ? "Animations: Auto"
                    : $"Animations: {Path.GetFileNameWithoutExtension(desired)}";
                if (SidebarLottiePickerLabel != null)
                    SidebarLottiePickerLabel.Text = label;
            }
            finally
            {
                _suppressSidebarLottiePicker = false;
            }
        }

        private void RefreshSidebarCategoryLottieBinding()
        {
            try
            {
                if (SidebarCategoryLottie == null) return;
                var expr = BindingOperations.GetBindingExpression(SidebarCategoryLottie, LottieAnimationView.FileNameProperty);
                expr?.UpdateTarget();
            }
            catch
            {
            }
        }

        private void PreferencesStore_PreferencesChanged(object? sender, UserPreferences prefs)
        {
            try
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        SyncSidebarLottiePickerUi(prefs.MediaCentreSidebarLottie);
                        RefreshSidebarCategoryLottieBinding();
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }
        }

        private void SidebarLottiePickerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SidebarLottiePickerListHost == null)
                    return;

                SidebarLottiePickerListHost.Visibility = SidebarLottiePickerListHost.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            catch
            {
            }
        }

        private void SidebarLottieChoice_Click(object sender, RoutedEventArgs e)
        {
            if (_suppressSidebarLottiePicker)
                return;

            try
            {
                var selected = (sender as Button)?.Tag?.ToString() ?? "";
                selected = Path.GetFileName((selected ?? "").Trim());

                PreferencesStore.Instance.Update(p => p.MediaCentreSidebarLottie = selected);
                SyncSidebarLottiePickerUi(selected);
                RefreshSidebarCategoryLottieBinding();

                if (SidebarLottiePickerListHost != null)
                    SidebarLottiePickerListHost.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }
        }
                
        private static void ScrollBy(ScrollViewer? sv, double delta)
        {
            try
            {
                if (sv == null) return;
                sv.ScrollToHorizontalOffset(Math.Max(0, sv.HorizontalOffset + delta));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaCenterControl] Error scrolling: {ex.Message}");
            }
        }

        private void StreamSourcesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not ListBox list)
                    return;

                var dep = e.OriginalSource as DependencyObject;
                var container = dep != null ? ItemsControl.ContainerFromElement(list, dep) as ListBoxItem : null;
                if (container == null)
                    return;

                var url = "";
                var dc = container.DataContext;
                if (dc is AddonSource src)
                {
                    url = src.UrlOrPath ?? "";
                }
                else if (dc != null)
                {
                    var prop = dc.GetType().GetProperty("UrlOrPath") ?? dc.GetType().GetProperty("Url");
                    if (prop != null)
                        url = prop.GetValue(dc)?.ToString() ?? "";
                }

                url = (url ?? "").Trim();
                if (string.IsNullOrWhiteSpace(url))
                    return;

                Clipboard.SetText(url);
                AtlasAI.UI.ToastNotificationManager.Instance.Show("Copied URL", AtlasAI.UI.ToastType.Success, 1500);
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void ScrollCategoriesLeft_Click(object sender, RoutedEventArgs e)
        {
            ScrollBy(CategoryScrollViewer, -260);
        }

        private void ScrollCategoriesRight_Click(object sender, RoutedEventArgs e)
        {
            ScrollBy(CategoryScrollViewer, 260);
        }

        private void ScrollHeaderActionsLeft_Click(object sender, RoutedEventArgs e)
        {
            ScrollBy(HeaderActionsScrollViewer, -320);
        }

        private void ScrollHeaderActionsRight_Click(object sender, RoutedEventArgs e)
        {
            ScrollBy(HeaderActionsScrollViewer, 320);
        }

        private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                if (sender is not ScrollViewer sv) return;
                if (e.Handled) return;

                var delta = e.Delta > 0 ? -160 : 160;
                ScrollBy(sv, delta);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaCenterControl] Error in horizontal scroll: {ex.Message}");
            }
        }

        private void Player_PlaybackEnded(object? sender, EventArgs e)
        {
            try
            {
                var before = _playbackService.CurrentMedia?.FilePath ?? "";
                var wasRepeat = _playbackService.IsRepeat;

                try { _vm.MarkLastEpisodeWatched(); } catch { }
                _playbackService.PlayNext();
                try { _vm.AutoPlayNextEpisode(); } catch { }

                if (wasRepeat) return;

                var after = _playbackService.CurrentMedia?.FilePath ?? "";
                if (!string.IsNullOrWhiteSpace(before) && string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
                {
                    try { _playbackService.ClearQueue(); } catch { }
                    _currentMedia = null;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ExitExclusiveFullscreen();
                            _isPlayerMinimized = false;
                            SyncEmbeddedPlayerBinding();
                            HidePlayer();
                        }
                        catch
                        {
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaCenterControl] Error in playback ended handler: {ex.Message}");
            }
        }

        private static bool IsTextInputFocused()
        {
            try
            {
                var focused = Keyboard.FocusedElement;
                return focused is System.Windows.Controls.Primitives.TextBoxBase || focused is PasswordBox;
            }
            catch
            {
                return false;
            }
        }

        private void TopSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (sender is not TextBox tb) return;
                var query = tb.Text ?? "";
                if (_vm != null && _vm.IsServersView)
                    _vm.ServerCatalogSearchQuery = query;
                _vm.SearchQuery = query;
            }
            catch
            {
            }
        }

        private void Servers_ToggleFullPage(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Primitives.ToggleButton tb)
                    return;

                if (!_serversFullPageRestoreCaptured)
                {
                    try { _restoreLibraryStatsVisibility = LibraryStatsRow?.Visibility ?? Visibility.Visible; } catch { _restoreLibraryStatsVisibility = Visibility.Visible; }
                    try { _restoreLibraryHeaderVisibility = LibraryHeaderRow?.Visibility ?? Visibility.Visible; } catch { _restoreLibraryHeaderVisibility = Visibility.Visible; }
                    try { _restoreServersRightPanelWidth = ServersRightPanelColumn?.Width ?? GridLength.Auto; } catch { _restoreServersRightPanelWidth = GridLength.Auto; }
                    try { _restoreServersMainMargin = ServersMainPanel?.Margin ?? new Thickness(0, 0, 0, 20); } catch { _restoreServersMainMargin = new Thickness(0, 0, 0, 20); }
                    _serversFullPageRestoreCaptured = true;
                }

                var expand = tb.IsChecked == true;

                try { if (LibraryStatsRow != null) LibraryStatsRow.Visibility = expand ? Visibility.Collapsed : _restoreLibraryStatsVisibility; } catch { }
                try { if (LibraryHeaderRow != null) LibraryHeaderRow.Visibility = expand ? Visibility.Collapsed : _restoreLibraryHeaderVisibility; } catch { }
                // try { if (ServersRightPanelColumn != null) ServersRightPanelColumn.Width = expand ? new GridLength(0) : _restoreServersRightPanelWidth; } catch { }
                try { if (ServersMainPanel != null) ServersMainPanel.Margin = expand ? new Thickness(0) : _restoreServersMainMargin; } catch { }
            }
            catch
            {
            }
        }

        private void RefreshMediaAiInput()
        {
            try
            {
                if (MediaAiInput == null) return;
                MediaAiInput.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                var targetText = _vm.MediaAiCommandText ?? "";
                if (!string.Equals(MediaAiInput.Text, targetText, StringComparison.Ordinal))
                    MediaAiInput.Text = targetText;
                MediaAiInput.CaretIndex = MediaAiInput.Text.Length;
            }
            catch
            {
            }
        }

        private void MediaCenterControl_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (!_vm.IsChatVisible) return;
                if (IsTextInputFocused()) return;
                if (string.IsNullOrEmpty(e.Text)) return;

                _vm.MediaAiCommandText = (_vm.MediaAiCommandText ?? "") + e.Text;
                e.Handled = true;
                RefreshMediaAiInput();
                MediaAiInput?.Focus();
                Keyboard.Focus(MediaAiInput);
                MediaAiInput?.Select(MediaAiInput.Text?.Length ?? 0, 0);
            }
            catch
            {
            }
        }

        private void MediaCenterControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Escape)
                {
                    try
                    {
                        if (IsAddAddonDialogOpen)
                        {
                            CloseAddAddonDialog();
                            e.Handled = true;
                            return;
                        }

                        if (_isExclusiveFullscreen)
                        {
                            ExitExclusiveFullscreen();
                            e.Handled = true;
                            return;
                        }

                        if (Player != null && Player.Visibility == Visibility.Visible)
                        {
                            Player.ClosePlayer();
                            _isPlayerMinimized = false;
                            HidePlayer();
                            e.Handled = true;
                            return;
                        }
                    }
                    catch
                    {
                    }
                }

                if (e.Key == Key.Escape && _videoPlayerWindow != null)
                {
                    try
                    {
                        _playbackService.ClearQueue();
                    }
                    catch
                    {
                    }

                    try
                    {
                        _videoPlayerWindow.Close();
                    }
                    catch
                    {
                    }

                    e.Handled = true;
                    return;
                }

                if (!_vm.IsChatVisible) return;
                if (IsTextInputFocused()) return;

                if (e.Key == Key.Back)
                {
                    var t = _vm.MediaAiCommandText ?? "";
                    if (t.Length > 0)
                        _vm.MediaAiCommandText = t.Substring(0, t.Length - 1);
                    e.Handled = true;
                    RefreshMediaAiInput();
                    MediaAiInput?.Focus();
                    Keyboard.Focus(MediaAiInput);
                    MediaAiInput?.Select(MediaAiInput.Text?.Length ?? 0, 0);
                    return;
                }

                if (e.Key == Key.Escape)
                {
                    try
                    {
                        if (_vm.ToggleChatCommand?.CanExecute(null) == true)
                            _vm.ToggleChatCommand.Execute(null);
                        else
                            _vm.IsChatVisible = false;
                    }
                    catch
                    {
                        _vm.IsChatVisible = false;
                    }
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
                {
                    try
                    {
                        if (_vm.SendMediaAiCommand?.CanExecute(null) == true)
                            _vm.SendMediaAiCommand.Execute(null);
                    }
                    catch
                    {
                    }
                    e.Handled = true;
                    RefreshMediaAiInput();
                    return;
                }
            }
            catch
            {
            }
        }

        public void NavigateWithContext(string context)
        {
        }

        private void MediaCenterControl_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            if (!_exitHooked)
            {
                try
                {
                    if (Application.Current != null)
                    {
                        Application.Current.Exit += Application_Exit;
                        _exitHooked = true;
                    }
                }
                catch
                {
                }
            }

            if (_hostWindow == null)
            {
                try
                {
                    _hostWindow = Window.GetWindow(this);
                    if (_hostWindow != null)
                        _hostWindow.Closing += HostWindow_Closing;
                }
                catch
                {
                }
            }

            if (_hostWindow != null && !_hostKeyboardHooksHooked)
            {
                try
                {
                    _hostWindow.AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(HostWindow_PreviewTextInput), true);
                    _hostWindow.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HostWindow_PreviewKeyDown), true);
                    _hostKeyboardHooksHooked = true;
                }
                catch
                {
                }
            }

            try
            {
                SyncEmbeddedPlayerBinding();
                ShowPlayer();
                PositionMiniPlayerIfNeeded();
            }
            catch
            {
            }

            // Start library loading AFTER UI is shown to prevent freeze
            try
            {
                _vm.StartLibraryLoading();
            }
            catch
            {
            }

            // Ensure grid layout is correct
            UpdateCoverGridColumns();

            try { UpdateMediaVoiceButtonsUi(); } catch { }

            try
            {
                if (!_defaultServersApplied)
                {
                    var url = AtlasAI.App.ConsumePendingOpenUrl();
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        var servers = _vm.Categories.FirstOrDefault(c => string.Equals(c.Id, "servers", StringComparison.OrdinalIgnoreCase));
                        if (servers != null && _vm.SelectCategoryCommand?.CanExecute(servers) == true)
                            _vm.SelectCategoryCommand.Execute(servers);
                        _defaultServersApplied = true;
                    }
                    else
                    {
                        var apps = _vm.Categories.FirstOrDefault(c => string.Equals(c.Id, "apps", StringComparison.OrdinalIgnoreCase));
                        if (apps != null && _vm.SelectCategoryCommand?.CanExecute(apps) == true)
                            _vm.SelectCategoryCommand.Execute(apps);
                        // NavigateApps(url); // Moved to AppsView
                        _defaultServersApplied = true;
                    }
                }
            }
            catch
            {
            }

            try
            {
                EnsureSidebarLottieUiInitialized();

                if (!_prefsHooked)
                {
                    PreferencesStore.Instance.PreferencesChanged += PreferencesStore_PreferencesChanged;
                    _prefsHooked = true;
                }

                RefreshSidebarCategoryLottieBinding();
            }
            catch
            {
            }

            try
            {
                RefreshNetworkSummary();
            }
            catch
            {
            }

            _ = RefreshAddonBrowserAsync();
        }

        private async void AddonsReloadServers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vm.ReloadAddonServersFromStore();
                await RefreshAddonBrowserAsync();
            }
            catch
            {
            }
        }

        private void AddonsRefreshNetwork_Click(object sender, RoutedEventArgs e)
        {
            RefreshNetworkSummary();
        }

        private async void AddonsScanWifi_Click(object sender, RoutedEventArgs e)
        {
            WifiNetworksText = "Scanning nearby Wi-Fi networks...";

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show networks mode=bssid",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(start);
                if (process == null)
                {
                    WifiNetworksText = "Unable to start Wi-Fi scan.";
                    return;
                }

                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
                WifiNetworksText = string.IsNullOrWhiteSpace(text)
                    ? "No Wi-Fi scan output was returned."
                    : text.Trim();
            }
            catch (Exception ex)
            {
                WifiNetworksText = string.IsNullOrWhiteSpace(ex.Message) ? "Wi-Fi scan failed." : ex.Message;
            }
        }

        private void AddonsOpenNetworkSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:network",
                    UseShellExecute = true,
                });
            }
            catch
            {
            }
        }

        private void OpenAddonsSection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var addons = _vm.Categories.FirstOrDefault(c => string.Equals(c.Id, "addons", StringComparison.OrdinalIgnoreCase));
                if (addons != null && _vm.SelectCategoryCommand?.CanExecute(addons) == true)
                    _vm.SelectCategoryCommand.Execute(addons);

                EnsureAddonBrowserHasVisibleScope();
                _ = RefreshAddonBrowserAsync();
            }
            catch
            {
            }
        }

        private async void OpenShelfToolsSection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var servers = _vm.Categories.FirstOrDefault(c => string.Equals(c.Id, "servers", StringComparison.OrdinalIgnoreCase));
                if (servers != null && _vm.SelectCategoryCommand?.CanExecute(servers) == true)
                    _vm.SelectCategoryCommand.Execute(servers);

                await Dispatcher.InvokeAsync(async () =>
                {
                    UpdateLayout();

                    var serversView = FindDescendant<AtlasAI.Views.MediaCentre.ServersView>(this);
                    if (serversView != null)
                        await serversView.OpenShelfToolsAsync();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T match)
                return match;

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                var result = FindDescendant<T>(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        private bool FilterAddonBrowserItem(object item)
        {
            if (item is not AddonBrowserItem addon)
                return false;

            var typeFilter = (SelectedAddonType ?? "All").Trim();
            if (!string.Equals(typeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(addon.TypeSummary) ||
                    addon.TypeSummary.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            var search = (AddonSearchText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(search))
                return true;

            return (addon.Name?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                   (addon.Description?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                   (addon.Host?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                   (addon.Url?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private void RefreshAddonBrowserFilter()
        {
            _addonBrowserItemsView.Refresh();

            var scope = (SelectedAddonScope ?? "Installed").Trim();
            var scopeLabel = string.Equals(scope, "STREMIO-ADDONS.NET", StringComparison.OrdinalIgnoreCase)
                ? "directory addon"
                : "addon";

            if (IsAddonBrowserBusy)
                return;

            if (_addonBrowserItems.Count == 0)
            {
                AddonBrowserStatus = string.Equals(scope, "STREMIO-ADDONS.NET", StringComparison.OrdinalIgnoreCase)
                    ? "No addons were loaded from STREMIO-ADDONS.NET."
                    : "No addons installed yet. Use Add addon to add a Stremio-compatible manifest URL.";
                return;
            }

            var visibleCount = _addonBrowserItemsView.Cast<object>().Count();
            AddonBrowserStatus = visibleCount == 0
                ? "No addons match the current filters."
                : $"{visibleCount} {scopeLabel}{(visibleCount == 1 ? "" : "s")} available";
        }

        private List<string> GetInstalledAddonUrls()
        {
            try
            {
                return _vm.AddonServers
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private void SetAddonBrowserScope(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
                return;

            SelectedAddonScope = scope;
        }

        private async Task RefreshAddonBrowserAsync()
        {
            var urls = GetInstalledAddonUrls();
            OnPropertyChanged(nameof(InstalledAddonCount));

            var scope = (SelectedAddonScope ?? "Installed").Trim();
            var isRemoteDirectory = string.Equals(scope, "STREMIO-ADDONS.NET", StringComparison.OrdinalIgnoreCase);

            if (!isRemoteDirectory && urls.Count == 0)
            {
                _selectedAddonScope = "STREMIO-ADDONS.NET";
                OnPropertyChanged(nameof(SelectedAddonScope));
                OnPropertyChanged(nameof(IsInstalledAddonScopeSelected));
                OnPropertyChanged(nameof(IsDirectoryAddonScopeSelected));
                scope = _selectedAddonScope;
                isRemoteDirectory = true;
            }

            IsAddonBrowserBusy = true;
            AddonBrowserStatus = isRemoteDirectory
                ? "Loading addon directory..."
                : (urls.Count == 0
                    ? "No addons installed yet. Use Add addon to add a Stremio-compatible manifest URL."
                    : "Loading installed addons...");

            try
            {
                List<AddonBrowserItem> items;
                if (isRemoteDirectory)
                {
                    items = await LoadRemoteAddonDirectoryAsync(urls);
                    RemoteAddonCatalogCount = items.Count;
                }
                else
                {
                    var tasks = urls.Select(BuildAddonBrowserItemAsync).ToList();
                    items = tasks.Count == 0
                        ? new List<AddonBrowserItem>()
                        : (await Task.WhenAll(tasks)).Where(item => item != null).Cast<AddonBrowserItem>().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }

                _addonBrowserItems.Clear();
                foreach (var item in items)
                    _addonBrowserItems.Add(item);
            }
            catch (Exception ex)
            {
                AddonBrowserStatus = string.IsNullOrWhiteSpace(ex.Message)
                    ? (isRemoteDirectory ? "Failed to load addon directory." : "Failed to load installed addons.")
                    : ex.Message;
            }
            finally
            {
                IsAddonBrowserBusy = false;
                OnPropertyChanged(nameof(InstalledAddonCount));
                RefreshAddonBrowserFilter();
            }
        }

        private void EnsureAddonBrowserHasVisibleScope()
        {
            try
            {
                var hasInstalled = _vm.AddonServers.Any(url => !string.IsNullOrWhiteSpace(url));
                if (!hasInstalled && !string.Equals(_selectedAddonScope, "STREMIO-ADDONS.NET", StringComparison.Ordinal))
                {
                    _selectedAddonScope = "STREMIO-ADDONS.NET";
                    OnPropertyChanged(nameof(SelectedAddonScope));
                    OnPropertyChanged(nameof(IsInstalledAddonScopeSelected));
                    OnPropertyChanged(nameof(IsDirectoryAddonScopeSelected));
                }
            }
            catch
            {
            }
        }

        private async Task<AddonBrowserItem?> BuildAddonBrowserItemAsync(string url)
        {
            var normalizedUrl = NormalizeAddonUrl(url);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
                return null;

            var host = TryGetAddonHost(normalizedUrl);
            var (success, manifest, error) = await _addonManifestService.FetchManifestAsync(normalizedUrl);

            if (!success || manifest == null)
            {
                return new AddonBrowserItem
                {
                    Name = string.IsNullOrWhiteSpace(host) ? normalizedUrl : host,
                    Version = "Unavailable",
                    Description = string.IsNullOrWhiteSpace(error) ? "Manifest could not be loaded." : error,
                    TypeSummary = "Other",
                    ResourceSummary = "Manifest unavailable",
                    Url = normalizedUrl,
                    ManifestUrl = BuildManifestUrl(normalizedUrl),
                    Host = string.IsNullOrWhiteSpace(host) ? normalizedUrl : host,
                    HasError = true,
                    ErrorText = error ?? ""
                };
            }

            var description = string.IsNullOrWhiteSpace(manifest.Description)
                ? "No description provided."
                : manifest.Description.Trim();

            return new AddonBrowserItem
            {
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? host : manifest.Name.Trim(),
                Version = string.IsNullOrWhiteSpace(manifest.Version) ? "v?" : $"v{manifest.Version.TrimStart('v', 'V')}",
                Description = description,
                TypeSummary = BuildAddonTypeSummary(manifest.Types),
                ResourceSummary = BuildAddonResourceSummary(manifest.Resources),
                Url = normalizedUrl,
                DirectoryUrl = "",
                InstallManifestUrl = BuildManifestUrl(normalizedUrl),
                ManifestUrl = BuildManifestUrl(normalizedUrl),
                IconUrl = ResolveAddonAssetUrl(normalizedUrl, manifest.Logo),
                Host = string.IsNullOrWhiteSpace(host) ? normalizedUrl : host,
                StarsText = "",
                IsInstalled = true,
                HasError = false,
                ErrorText = ""
            };
        }

        private async Task<List<AddonBrowserItem>> LoadRemoteAddonDirectoryAsync(IReadOnlyCollection<string> installedUrls)
        {
            var installedSet = new HashSet<string>(installedUrls.Select(NormalizeAddonUrl).Where(url => !string.IsNullOrWhiteSpace(url)), StringComparer.OrdinalIgnoreCase);
            var json = await AddonDirectoryHttpClient.GetStringAsync(StremioAddonsCatalogUrl);
            return ParseRemoteAddonDirectoryJson(json, installedSet);
        }

        private static List<AddonBrowserItem> ParseRemoteAddonDirectoryJson(string json, IReadOnlyCollection<string> installedUrls)
        {
            var items = new List<AddonBrowserItem>();
            if (string.IsNullOrWhiteSpace(json))
                return items;

            var installedSet = new HashSet<string>(installedUrls.Where(url => !string.IsNullOrWhiteSpace(url)), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("addons", out var addons) || addons.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var addon in addons.EnumerateArray())
            {
                var normalizedUrl = NormalizeAddonUrl(GetJsonString(addon, "transportUrl"));
                if (string.IsNullOrWhiteSpace(normalizedUrl) || !seen.Add(normalizedUrl))
                    continue;

                if (!addon.TryGetProperty("manifest", out var manifest) || manifest.ValueKind != JsonValueKind.Object)
                    continue;

                var name = GetJsonString(manifest, "name");
                var description = GetJsonString(manifest, "description");
                var version = GetJsonString(manifest, "version");
                var logo = GetJsonString(manifest, "logo");
                var host = TryGetAddonHost(normalizedUrl);
                var isInstalled = installedSet.Contains(normalizedUrl);

                items.Add(new AddonBrowserItem
                {
                    Name = string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(host) ? normalizedUrl : host) : name.Trim(),
                    Version = string.IsNullOrWhiteSpace(version) ? "v?" : $"v{version.TrimStart('v', 'V')}",
                    Description = string.IsNullOrWhiteSpace(description) ? "Community addon directory entry." : description.Trim(),
                    TypeSummary = BuildAddonTypeSummary(GetJsonStringArray(manifest, "types")),
                    ResourceSummary = BuildAddonResourceSummary(GetJsonStringArray(manifest, "resources")),
                    Url = normalizedUrl,
                    DirectoryUrl = "",
                    InstallManifestUrl = BuildManifestUrl(normalizedUrl),
                    ManifestUrl = BuildManifestUrl(normalizedUrl),
                    IconUrl = ResolveAddonAssetUrl(normalizedUrl, logo),
                    Host = string.IsNullOrWhiteSpace(host) ? normalizedUrl : host,
                    StarsText = "",
                    IsInstalled = isInstalled,
                    HasError = false,
                    ErrorText = ""
                });
            }

            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return "";

            return property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? ""
                : property.ToString();
        }

        private static IEnumerable<string> GetJsonStringArray(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var values = new List<string>();
            foreach (var entry in property.EnumerateArray())
            {
                switch (entry.ValueKind)
                {
                    case JsonValueKind.String:
                    {
                        var value = entry.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            values.Add(value.Trim());
                        break;
                    }
                    case JsonValueKind.Object:
                    {
                        var value = GetJsonString(entry, "name");
                        if (!string.IsNullOrWhiteSpace(value))
                            values.Add(value.Trim());
                        break;
                    }
                }
            }

            return values;
        }

        private static string ExtractMatch(string input, string pattern, string groupName)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var match = Regex.Match(input, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[groupName].Value : "";
        }

        private void CloseAddAddonDialog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CloseAddAddonDialog();
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void ConfirmAddAddon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfirmAddAddon();
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void AddAddonDialogOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                CloseAddAddonDialog();
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void AddAddonDialogPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private async Task<string> ResolveDirectoryInstallManifestUrlAsync(string detailUrl)
        {
            if (string.IsNullOrWhiteSpace(detailUrl))
                return "";

            var html = await AddonDirectoryHttpClient.GetStringAsync(detailUrl);
            var match = Regex.Match(html, "\\\"manifestUrl\\\":\\\"(?<url>https:[^\\\"]+manifest\\.json)\\\"", RegexOptions.IgnoreCase);
            if (!match.Success)
                return "";

            return WebUtility.HtmlDecode(Regex.Unescape(match.Groups["url"].Value));
        }

        private static string CleanupHtmlText(string input)
        {
            var decoded = WebUtility.HtmlDecode(input ?? "");
            decoded = Regex.Replace(decoded, "<.*?>", string.Empty);
            decoded = Regex.Replace(decoded, "\\s+", " ").Trim();
            return decoded;
        }

        private static string NormalizeAddonUrl(string input)
        {
            var value = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return "";

            while (value.Length >= 2)
            {
                var first = value[0];
                var last = value[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '<' && last == '>'))
                {
                    value = value.Substring(1, value.Length - 2).Trim();
                    continue;
                }

                break;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (!Uri.TryCreate("https://" + value.TrimStart('/'), UriKind.Absolute, out uri))
                    return "";
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            var rawPath = (uri.AbsolutePath ?? "").Trim();
            if (Regex.IsMatch(rawPath, @"(?i)\.(png|jpe?g|webp|gif|svg)$") ||
                rawPath.Contains("/poster/", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            var builder = new UriBuilder(uri) { Fragment = "" };
            if (builder.Path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                builder.Path = builder.Path.Substring(0, builder.Path.Length - "/manifest.json".Length);

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string TryGetAddonHost(string url)
        {
            try
            {
                return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    ? (uri.Host ?? "").Trim()
                    : "";
            }
            catch
            {
                return "";
            }
        }

        private static string BuildManifestUrl(string baseUrl)
        {
            var normalized = NormalizeAddonUrl(baseUrl);
            return string.IsNullOrWhiteSpace(normalized)
                ? ""
                : normalized.TrimEnd('/') + "/manifest.json";
        }

        private static string ResolveAddonAssetUrl(string baseUrl, string assetUrl)
        {
            if (string.IsNullOrWhiteSpace(assetUrl))
                return "";

            try
            {
                if (Uri.TryCreate(assetUrl.Trim(), UriKind.Absolute, out var absolute))
                    return absolute.ToString();

                var normalized = NormalizeAddonUrl(baseUrl);
                if (!Uri.TryCreate(normalized.EndsWith("/") ? normalized : normalized + "/", UriKind.Absolute, out var baseUri))
                    return "";

                return new Uri(baseUri, assetUrl.TrimStart('/')).ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string BuildAddonTypeSummary(IEnumerable<string>? types)
        {
            var normalized = (types ?? Array.Empty<string>())
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Select(type => type.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0)
                return "Other";

            return string.Join(" & ", normalized.Select(type => char.ToUpperInvariant(type[0]) + type.Substring(1)));
        }

        private static string BuildAddonResourceSummary(IEnumerable<string>? resources)
        {
            var normalized = (resources ?? Array.Empty<string>())
                .Where(resource => !string.IsNullOrWhiteSpace(resource))
                .Select(resource => resource.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return normalized.Count == 0
                ? "Manifest only"
                : string.Join(" • ", normalized);
        }

        private void OpenAddAddonDialog()
        {
            AddonInstallStatus = "";
            PendingAddonUrl = "";
            IsAddAddonDialogOpen = true;
        }

        private void CloseAddAddonDialog()
        {
            IsAddAddonDialogOpen = false;
            AddonInstallStatus = "";
            PendingAddonUrl = "";
        }

        private async void ConfirmAddAddon()
        {
            var normalized = NormalizeAddonUrl(PendingAddonUrl);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                AddonInstallStatus = "Enter a valid addon URL or manifest URL.";
                return;
            }

            AddonInstallStatus = "Checking addon manifest...";
            try
            {
                var (success, manifest, error) = await _addonManifestService.FetchManifestAsync(normalized);
                if (!success || manifest == null)
                {
                    AddonInstallStatus = string.IsNullOrWhiteSpace(error)
                        ? "That addon manifest could not be loaded."
                        : error;
                    return;
                }

                if (_vm.AddAddonServerCommand?.CanExecute(normalized) == true)
                    _vm.AddAddonServerCommand.Execute(normalized);

                _vm.ReloadAddonServersFromStore();
                await RefreshAddonBrowserAsync();

                AddonInstallStatus = $"Installed {manifest.Name}.";
                PendingAddonUrl = "";
                IsAddAddonDialogOpen = false;
            }
            catch (Exception ex)
            {
                AddonInstallStatus = string.IsNullOrWhiteSpace(ex.Message) ? "Failed to add addon." : ex.Message;
            }
        }

        private async void InstallAddonCard(AddonBrowserItem? addon)
        {
            if (addon == null)
                return;

            try
            {
                AddonInstallStatus = $"Installing {addon.Name}...";
                var installTarget = !string.IsNullOrWhiteSpace(addon.InstallManifestUrl)
                    ? addon.InstallManifestUrl
                    : addon.Url;
                if (string.IsNullOrWhiteSpace(installTarget) && !string.IsNullOrWhiteSpace(addon.DirectoryUrl))
                {
                    installTarget = await ResolveDirectoryInstallManifestUrlAsync(addon.DirectoryUrl);
                    addon.InstallManifestUrl = installTarget;
                    addon.ManifestUrl = installTarget;
                }

                var normalized = NormalizeAddonUrl(installTarget);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    AddonInstallStatus = $"No install link was found for {addon.Name}.";
                    return;
                }

                if (_vm.AddAddonServerCommand?.CanExecute(normalized) == true)
                    _vm.AddAddonServerCommand.Execute(normalized);

                _vm.ReloadAddonServersFromStore();
                await RefreshAddonBrowserAsync();
                AddonInstallStatus = $"Installed {addon.Name}.";
            }
            catch (Exception ex)
            {
                AddonInstallStatus = string.IsNullOrWhiteSpace(ex.Message) ? $"Failed to install {addon.Name}." : ex.Message;
            }
        }

        private async void RemoveAddonCard(AddonBrowserItem? addon)
        {
            if (addon == null)
                return;

            try
            {
                var candidates = new[]
                {
                    NormalizeAddonUrl(addon.InstallManifestUrl ?? ""),
                    NormalizeAddonUrl(addon.Url ?? ""),
                    NormalizeAddonUrl(addon.DirectoryUrl ?? "")
                }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

                var installed = _vm.AddonServers
                    .Select(NormalizeAddonUrl)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var targets = installed
                    .Where(installedUrl => candidates.Any(candidate =>
                        string.Equals(installedUrl, candidate, StringComparison.OrdinalIgnoreCase) ||
                        installedUrl.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase) ||
                        candidate.StartsWith(installedUrl + "/", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (targets.Count == 0 && candidates.Count > 0)
                    targets.Add(candidates[0]);

                foreach (var target in targets)
                {
                    if (_vm.RemoveAddonServerCommand?.CanExecute(target) == true)
                        _vm.RemoveAddonServerCommand.Execute(target);
                }

                _vm.ReloadAddonServersFromStore();
                await RefreshAddonBrowserAsync();
            }
            catch
            {
            }
        }

        private void ShareAddonCard(AddonBrowserItem? addon)
        {
            var url = !string.IsNullOrWhiteSpace(addon?.InstallManifestUrl)
                ? addon!.InstallManifestUrl
                : addon?.DirectoryUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Clipboard.SetText(url);
            }
            catch
            {
            }
        }

        private void RefreshNetworkSummary()
        {
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                var active = adapters.FirstOrDefault();
                if (active == null)
                {
                    NetworkSummary = "No active network adapters found.";
                    return;
                }

                var props = active.GetIPProperties();
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address
                    .ToString() ?? "Unknown";
                var gateway = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address
                    .ToString() ?? "Unknown";
                var dns = props.DnsAddresses
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?.ToString() ?? "Unknown";

                NetworkSummary = $"Adapter: {active.Name}\nType: {active.NetworkInterfaceType}\nIPv4: {ipv4}\nGateway: {gateway}\nDNS: {dns}";
            }
            catch (Exception ex)
            {
                NetworkSummary = string.IsNullOrWhiteSpace(ex.Message) ? "Unable to read network status." : ex.Message;
            }
        }

        private void MediaCenterControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _mediaVoiceNoteTimer?.Stop(); } catch { }

            if (_prefsHooked)
            {
                try { PreferencesStore.Instance.PreferencesChanged -= PreferencesStore_PreferencesChanged; } catch { }
                _prefsHooked = false;
            }

            try
            {
                _vm.FlushLibrarySavesBlocking();
            }
            catch
            {
            }

            if (_exitHooked)
            {
                try
                {
                    if (Application.Current != null)
                        Application.Current.Exit -= Application_Exit;
                }
                catch
                {
                }
                _exitHooked = false;
            }

            if (_hostWindow != null)
            {
                try
                {
                    if (_hostKeyboardHooksHooked)
                    {
                        _hostWindow.RemoveHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(HostWindow_PreviewTextInput));
                        _hostWindow.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HostWindow_PreviewKeyDown));
                        _hostKeyboardHooksHooked = false;
                    }
                    _hostWindow.Closing -= HostWindow_Closing;
                }
                catch
                {
                }
                _hostWindow = null;
            }
        }

        private void HostWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            MediaCenterControl_PreviewTextInput(sender, e);
        }

        private void HostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            MediaCenterControl_PreviewKeyDown(sender, e);
        }

        private void UpdateMediaVoiceButtonsUi()
        {
            try
            {
                bool speechWired = AtlasAI.Brain.SectionSpeechMicStandard.IsSpeechWired("Media");
                bool speechEnabled = AtlasAI.Brain.SectionSpeechMicStandard.IsSpeechEnabled("Media");
                bool micWired = AtlasAI.Brain.SectionSpeechMicStandard.IsMicWired("Media");

                if (MediaVoiceSpeechBtn != null)
                {
                    MediaVoiceSpeechBtn.Visibility = speechWired ? Visibility.Visible : Visibility.Collapsed;
                    MediaVoiceSpeechBtn.Content = speechEnabled ? "🔊" : "🔇";
                    MediaVoiceSpeechBtn.ToolTip = speechEnabled ? "Speech output on" : "Speech output off";
                }

                if (MediaVoiceMicBtn != null)
                {
                    MediaVoiceMicBtn.Content = "🎤";
                    MediaVoiceMicBtn.ToolTip = micWired ? "Start media microphone" : "Mic not wired";
                    MediaVoiceMicBtn.Opacity = micWired ? 1.0 : 0.55;
                }
            }
            catch
            {
            }
        }

        private void ShowMediaVoiceNote(string text)
        {
            try
            {
                if (MediaVoiceNoteText == null)
                    return;

                MediaVoiceNoteText.Text = text;
                MediaVoiceNoteText.Visibility = Visibility.Visible;

                _mediaVoiceNoteTimer?.Stop();
                if (_mediaVoiceNoteTimer == null)
                {
                    _mediaVoiceNoteTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(2.4)
                    };
                    _mediaVoiceNoteTimer.Tick += (_, __) =>
                    {
                        try
                        {
                            _mediaVoiceNoteTimer?.Stop();
                            if (MediaVoiceNoteText != null)
                                MediaVoiceNoteText.Visibility = Visibility.Collapsed;
                        }
                        catch
                        {
                        }
                    };
                }

                _mediaVoiceNoteTimer.Start();
            }
            catch
            {
            }
        }

        private void MediaVoiceSpeechBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!AtlasAI.Brain.SectionSpeechMicStandard.IsSpeechWired("Media"))
                    return;

                bool enabled = AtlasAI.Brain.SectionSpeechMicStandard.ToggleSpeech("Media");
                UpdateMediaVoiceButtonsUi();
                ShowMediaVoiceNote(enabled ? "Speech on" : "Speech off");
            }
            catch
            {
            }
        }

        private void MediaVoiceMicBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!AtlasAI.Brain.SectionSpeechMicStandard.IsMicWired("Media"))
                {
                    ShowMediaVoiceNote("Mic not wired");
                    return;
                }

                AtlasAI.Voice.VoiceSystemOrchestrator.Instance.BeginListening(AtlasAI.Voice.ListeningSource.PushToTalk);
                ShowMediaVoiceNote("Mic listening");
            }
            catch
            {
                ShowMediaVoiceNote("Mic unavailable");
            }
        }

        private void Application_Exit(object? sender, ExitEventArgs e)
        {
            try
            {
                _vm.FlushLibrarySavesBlocking();
            }
            catch
            {
            }
        }

        private void HostWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                _vm.FlushLibrarySavesBlocking();
            }
            catch
            {
            }
        }

        private void UpdateCoverGridColumns()
        {
            var sidebarOpen = !_isSidebarCollapsed;
            var streamsPanelOpen = _vm != null && _vm.IsServerStreamsPanelOpen;

            // User requirement:
            // 0 sidebars open -> 6 columns
            // 1 sidebar open -> 5 columns
            // 2 sidebars open -> 4 columns
            
            var openPanels = 0;
            if (sidebarOpen) openPanels++;
            if (streamsPanelOpen) openPanels++;

            if (openPanels == 0) CoverGridColumns = 6;
            else if (openPanels == 1) CoverGridColumns = 5;
            else CoverGridColumns = 4;
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MediaCentreViewModel.SelectedCategory) &&
                string.Equals(_vm.SelectedCategory?.Id, "addons", StringComparison.OrdinalIgnoreCase))
            {
                EnsureAddonBrowserHasVisibleScope();
                _ = RefreshAddonBrowserAsync();
            }

            if (e.PropertyName == nameof(MediaCentreViewModel.IsChatVisible) && _vm.IsChatVisible)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    try
                    {
                        var host = Window.GetWindow(this);
                        if (host == null || !host.IsActive) return;
                        if (Application.Current?.Windows.OfType<Window>().Any(w => w != host && w.IsActive) == true) return;
                        MediaAiInput?.Focus();
                        Keyboard.Focus(MediaAiInput);
                    }
                    catch
                    {
                    }
                }));
            }

            if (e.PropertyName == nameof(MediaCentreViewModel.IsStreamsOpen) && _vm.IsStreamsOpen)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    try
                    {
                        var host = Window.GetWindow(this);
                        if (host == null || !host.IsActive) return;
                        if (Application.Current?.Windows.OfType<Window>().Any(w => w != host && w.IsActive) == true) return;
                        StreamsList?.Focus();
                        Keyboard.Focus(StreamsList);
                    }
                    catch
                    {
                    }
                }));
            }

            if (e.PropertyName == nameof(MediaCentreViewModel.MediaAgentPendingAppsUrl) && !string.IsNullOrWhiteSpace(_vm.MediaAgentPendingAppsUrl))
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        var apps = _vm.Categories.FirstOrDefault(c => string.Equals(c.Id, "apps", StringComparison.OrdinalIgnoreCase));
                        if (apps != null && _vm.SelectCategoryCommand?.CanExecute(apps) == true)
                            _vm.SelectCategoryCommand.Execute(apps);
                    }
                    catch
                    {
                    }

                    try
                    {
                        _vm.TryNavigateMediaAgentPendingAppsUrl();
                    }
                    catch
                    {
                    }
                }));
            }

            if (e.PropertyName == nameof(MediaCentreViewModel.IsMusicAlbumsView) ||
                e.PropertyName == nameof(MediaCentreViewModel.IsAlbumTracksView) ||
                e.PropertyName == nameof(MediaCentreViewModel.SelectedCategory) ||
                e.PropertyName == nameof(MediaCentreViewModel.IsServerStreamsPanelOpen))
            {
                UpdateCoverGridColumns();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        SyncEmbeddedPlayerBinding();
                        ShowPlayer();
                        PositionMiniPlayerIfNeeded();
                    }
                    catch
                    {
                    }
                }));
            }
        }

        private void PlaybackService_CurrentMediaChanged(object? sender, AtlasAI.MediaScanner.MediaItem e)
        {
            _currentMedia = e; // Always set so the Loaded handler and IsVisibleChanged handler see it
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isPlayerMinimized = false;
                try
                {
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                    }
                }
                catch
                {
                    _videoPlayerWindow = null;
                }
                // If a FullscreenPlayerWindow is visible, it owns playback.
                // Do not activate ChatWindow or make the embedded player visible —
                // that would start a second VLC instance and steal PlaybackOutputCoordinator.
                var fullscreenVisible = System.Windows.Application.Current.Windows
                    .OfType<FullscreenPlayerWindow>()
                    .Any(w => w.IsVisible);
                if (fullscreenVisible)
                    return;

                // Always ensure ChatWindow is visible for video, even before _isLoaded
                // This triggers MediaCenterControl_Loaded which then calls ShowPlayer
                if (IsVideoMedia(_currentMedia))
                    EnsureVideoPlaybackWindowState();
                if (!_isLoaded) return;
                SyncEmbeddedPlayerBinding();
                ShowPlayer();
                PositionMiniPlayerIfNeeded();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Player_PlayerClosed(object? sender, EventArgs e)
        {
            ExitExclusiveFullscreen();
            _isPlayerMinimized = false;
            if (IsVideoMedia(_currentMedia))
            {
                try { _playbackService.ClearQueue(); } catch { }
                _currentMedia = null;
                try { SyncEmbeddedPlayerBinding(); } catch { }
            }
            HidePlayer();
        }

        private void Player_PlayerMinimized(object? sender, EventArgs e)
        {
            ExitExclusiveFullscreen();
            if (IsVideoMedia(_currentMedia))
            {
                // For video, minimize would leave audio playing with no accessible stop control
                // (RestorePlayerButton is covered by the WebView2 Win32 HWND). Stop instead.
                _isPlayerMinimized = false;
                try { Player.Stop(); } catch { }
                try { _playbackService.ClearQueue(); } catch { }
                _currentMedia = null;
                try { SyncEmbeddedPlayerBinding(); } catch { }
                HidePlayer();
            }
            else
            {
                _isPlayerMinimized = true;
                HidePlayer();
            }
        }

        private void RestorePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            _isPlayerMinimized = false;
            UpdateRestorePlayerButtonVisibility();
            ShowPlayer();
            PositionMiniPlayerIfNeeded();
        }

        private void Player_FullscreenRequested(object? sender, EventArgs e)
        {
            if (_isExclusiveFullscreen)
                ExitExclusiveFullscreen();
            else
                EnterExclusiveFullscreen();
        }

        private void ShowPlayer()
        {
                var isMusicView = _vm.IsMusicAlbumsView || _vm.IsAlbumTracksView;
                var isVideo = IsVideoMedia(_currentMedia);
                var isKaraoke = _currentMedia?.SectionName != null && _currentMedia.SectionName.Equals("Karaoke", StringComparison.OrdinalIgnoreCase);
                var isKaraokeView = string.Equals(_vm.SelectedCategory?.Id, "karaoke", StringComparison.OrdinalIgnoreCase);

                LogShowPlayerTrace($"isMusicView={isMusicView} isVideo={isVideo} isKaraoke={isKaraoke} isKaraokeView={isKaraokeView} minimized={_isPlayerMinimized} mediaNull={_currentMedia == null} mediaType={_currentMedia?.MediaType} section={_currentMedia?.SectionName} path={_currentMedia?.FilePath}");

                if (_isPlayerMinimized)
                {
                    LogShowPlayerTrace("Collapsed: _isPlayerMinimized");
                    Player.Visibility = Visibility.Collapsed;
                    SetAllWebView2sVisible(true);
                    NowPlayingVisualizer.Visibility = Visibility.Visible;
                    UpdateRestorePlayerButtonVisibility();
                    return;
                }

                if (!isMusicView && !isVideo)
                {
                    LogShowPlayerTrace("Collapsed: !isMusicView && !isVideo");
                    Player.Visibility = Visibility.Collapsed;
                    SetAllWebView2sVisible(true);
                    NowPlayingVisualizer.Visibility = Visibility.Visible;
                    UpdateRestorePlayerButtonVisibility();
                    return;
                }

                if (_currentMedia == null)
                {
                    LogShowPlayerTrace("Collapsed: _currentMedia == null");
                    Player.Visibility = Visibility.Collapsed;
                    SetAllWebView2sVisible(true);
                    NowPlayingVisualizer.Visibility = Visibility.Visible;
                    UpdateRestorePlayerButtonVisibility();
                    return;
                }

                if (isVideo)
                {
                    if (isKaraoke && !isKaraokeView)
                    {
                            LogShowPlayerTrace("Collapsed: isKaraoke && !isKaraokeView");
                        Player.Visibility = Visibility.Collapsed;
                        SetAllWebView2sVisible(true);
                        NowPlayingVisualizer.Visibility = Visibility.Visible;
                        UpdateRestorePlayerButtonVisibility();
                        return;
                    }
                    if (this.Visibility != Visibility.Visible)
                    {
                        LogShowPlayerTrace($"VISIBLE: auto-showing MediaCenterControl (was {this.Visibility})");
                        this.Visibility = Visibility.Visible;
                        Panel.SetZIndex(this, 5000);
                    }
                    SetAllWebView2sVisible(false);
                    Player.Visibility = Visibility.Visible;
                    LogShowPlayerTrace($"POST-VISIBLE: this.IsVisible={this.IsVisible} Player.IsVisible={Player.IsVisible} Player.Visibility={Player.Visibility}");
                    NowPlayingVisualizer.Visibility = Visibility.Collapsed;
                    Player.Focus();
                    UpdateRestorePlayerButtonVisibility();
                    return;
                }

                LogShowPlayerTrace("Collapsed: fallback audio branch");
                Player.Visibility = Visibility.Collapsed;
                SetAllWebView2sVisible(true);
                NowPlayingVisualizer.Visibility = Visibility.Visible;
                UpdateRestorePlayerButtonVisibility();
        }

        private void HidePlayer()
        {
            Player.Visibility = Visibility.Collapsed;
            SetAllWebView2sVisible(true);
            NowPlayingVisualizer.Visibility = Visibility.Visible;
            UpdateRestorePlayerButtonVisibility();
        }

        private void UpdateRestorePlayerButtonVisibility()
        {
            try
            {
                if (RestorePlayerButton == null) return;
                var show = _isPlayerMinimized && IsVideoMedia(_currentMedia);
                RestorePlayerButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
            }
        }

        /// <summary>
        /// Recursively shows or hides all WebView2 HWNDs within this control.
        /// WebView2 (and LibVLC VideoView) both use Win32 child HWNDs that render at the
        /// OS level — WPF ZIndex has no effect between them. Hiding the WebView2s lets
        /// the VideoView HWND become visible when the inline player is shown.
        /// </summary>
        private void SetAllWebView2sVisible(bool visible)
        {
            try { SetWebView2sInTree(this, visible); } catch { }
        }

        private static void SetWebView2sInTree(DependencyObject root, bool visible)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Microsoft.Web.WebView2.Wpf.WebView2 wv)
                    wv.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                else
                    SetWebView2sInTree(child, visible);
            }
        }

        private bool IsMusicView()
        {
            try
            {
                return _vm.IsMusicAlbumsView || _vm.IsAlbumTracksView;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsVideoMedia(AtlasAI.MediaScanner.MediaItem? media)
        {
                if (media == null) { LogShowPlayerTrace("[IsVideoMediaTrace] media==null"); return false; }
                // CRITICAL: Trust explicit flags first
                if (media.SectionName != null && media.SectionName.Equals("Karaoke", StringComparison.OrdinalIgnoreCase)) { LogShowPlayerTrace("[IsVideoMediaTrace] Karaoke section"); return true; }
                if (media.MediaType == MediaType.Video) { LogShowPlayerTrace("[IsVideoMediaTrace] MediaType.Video"); return true; }
                if (media.MediaType == MediaType.Unknown) { LogShowPlayerTrace("[IsVideoMediaTrace] MediaType.Unknown"); return true; }

                // Check extension first to rescue Karaoke/Zip files that might be classified as Audio
                var path = (media.FilePath ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    try
                    {
                        string? ext = null;
                        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                        {
                            ext = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                        }
                        else
                        {
                            ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                        }
                        // Karaoke files are technically "Video" for playback purposes (graphics)
                        if (ext is ".zip" or ".cdg") { LogShowPlayerTrace($"[IsVideoMediaTrace] ext={ext} => true (karaoke)"); return true; }
                        if (ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".wma" or ".m4a" or ".ogg" or ".opus" or ".m4b") { LogShowPlayerTrace($"[IsVideoMediaTrace] ext={ext} => false (audio)"); return false; }
                        // Streams from cloud/addon providers often have no extension.
                        if (string.Equals(media.SectionName, "cloud", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(ext)) { LogShowPlayerTrace($"[IsVideoMediaTrace] section=cloud, no ext => true"); return true; }
                    }
                    catch { }
                }
                if (media.MediaType == MediaType.Audio) { LogShowPlayerTrace("[IsVideoMediaTrace] MediaType.Audio"); return false; }
                if (string.IsNullOrWhiteSpace(path)) { LogShowPlayerTrace("[IsVideoMediaTrace] empty path"); return false; }
                try
                {
                    if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                        (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                    {
                        var ext = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                        if (ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".wma" or ".m4a" or ".ogg") { LogShowPlayerTrace($"[IsVideoMediaTrace] ext={ext} => false (audio2)"); return false; }
                        var isVid = ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v" or ".flv" or ".wmv" or ".m3u8" or ".zip" or ".cdg";
                        LogShowPlayerTrace($"[IsVideoMediaTrace] ext={ext} => {isVid} (video ext)");
                        return isVid;
                    }
                    var e = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    if (e is ".mp3" or ".wav" or ".flac" or ".aac" or ".wma" or ".m4a" or ".ogg") { LogShowPlayerTrace($"[IsVideoMediaTrace] ext={e} => false (audio3)"); return false; }
                    var isVid2 = e is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v" or ".flv" or ".wmv" or ".m3u8" or ".zip" or ".cdg";
                    LogShowPlayerTrace($"[IsVideoMediaTrace] ext={e} => {isVid2} (video ext2)");
                    return isVid2;
                }
                catch
                {
                    LogShowPlayerTrace("[IsVideoMediaTrace] catch-all false");
                    return false;
                }
            }

        private static void LogShowPlayerTrace(string msg)
        {
            try
            {
                var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "ShowPlayerTrace.txt");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { }
        }

        private void SyncEmbeddedPlayerBinding()
        {
            try
            {
                // Always keep the service bound so that duration/position events fire
                // and the mini-player (sidebar) can update stats even if the main player is hidden.
                if (Player.PlaybackService == null)
                    Player.PlaybackService = _playbackService;
            }
            catch
            {
            }
        }

        private void EnsureVideoPlayerWindow()
        {
            try
            {
                if (_videoPlayerWindow != null)
                {
                    try { _videoPlayerWindow.Close(); } catch { }
                    _videoPlayerWindow = null;
                }
            }
            catch
            {
            }
        }

        private void EnsureVideoPlaybackWindowState()
        {
            try
            {
                var parentWindow = Window.GetWindow(this);
                if (parentWindow == null) return;
                if (parentWindow.Visibility != Visibility.Visible)
                    parentWindow.Visibility = Visibility.Visible;
                if (parentWindow.WindowState == WindowState.Minimized)
                    parentWindow.WindowState = WindowState.Normal;
                if (parentWindow is ChatWindow chatWin)
                    chatWin.ShowPage("media");
                // Briefly set Topmost to force window to front without hiding other windows
                parentWindow.Topmost = true;
                parentWindow.Activate();
                parentWindow.Topmost = false;
            }
            catch { }
        }

        private void PositionMiniPlayerIfNeeded()
        {
            if (Player.Visibility != Visibility.Visible) return;
            if (Player.IsFullscreen) return;

            // If it's video content (including Karaoke), we want it filling the overlay area, not the mini-player slot
            if (IsVideoMedia(_currentMedia))
            {
                Player.HorizontalAlignment = HorizontalAlignment.Stretch;
                Player.VerticalAlignment = VerticalAlignment.Stretch;
                Player.Margin = new Thickness(0);
                Player.Width = double.NaN;
                Player.Height = double.NaN;
                return;
            }

            if (!_vm.IsMusicAlbumsView && !_vm.IsAlbumTracksView)
            {
                Player.Visibility = Visibility.Collapsed;
                return;
            }
            if (_currentMedia == null)
            {
                Player.Visibility = Visibility.Collapsed;
                NowPlayingVisualizer.Visibility = Visibility.Visible;
                return;
            }

            Player.HorizontalAlignment = HorizontalAlignment.Stretch;
            Player.VerticalAlignment = VerticalAlignment.Stretch;
            Player.Margin = new Thickness(0);
            Player.Width = double.NaN;
            Player.Height = double.NaN;
        }

        private void EnsureExclusiveFullscreen()
        {
            if (_isExclusiveFullscreen) return;
            EnterExclusiveFullscreen();
        }

        private void EnterExclusiveFullscreen()
        {
            if (_isExclusiveFullscreen) return;
            if (Player.Visibility != Visibility.Visible) return;
            
            // Prevent exclusive fullscreen window for audio - keep it in the mini-player
            if (_currentMedia != null)
            {
                if (_currentMedia.MediaType == AtlasAI.MediaScanner.MediaType.Audio) return;
                var path = (_currentMedia.FilePath ?? "").Trim();
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".wma" or ".m4a" or ".ogg" or ".opus" or ".m4b") return;
            }

            if (_exclusivePlayerWindow != null) return;

            if (Player.Parent is not Panel parent)
                return;

            try
            {
                _exclusiveResumeSeconds = Player.GetCurrentPlaybackSeconds();
                _exclusiveResumeWasPlaying = Player.GetIsActuallyPlaying();
            }
            catch
            {
                _exclusiveResumeSeconds = 0;
                _exclusiveResumeWasPlaying = false;
            }

            _playerOriginalParent = parent;
            _playerOriginalIndex = parent.Children.IndexOf(Player);
            _playerOriginalRow = Grid.GetRow(Player);
            _playerOriginalColumn = Grid.GetColumn(Player);
            _playerOriginalRowSpan = Grid.GetRowSpan(Player);
            _playerOriginalColumnSpan = Grid.GetColumnSpan(Player);
            _playerOriginalZIndex = Panel.GetZIndex(Player);
            _playerOriginalHorizontalAlignment = Player.HorizontalAlignment;
            _playerOriginalVerticalAlignment = Player.VerticalAlignment;
            _playerOriginalMargin = Player.Margin;
            _playerOriginalWidth = Player.Width;
            _playerOriginalHeight = Player.Height;

            var owner = Window.GetWindow(this);

            Player.IsReparenting = true;
            var reparentSucceeded = false;
            try
            {
                parent.Children.Remove(Player);

                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    WindowState = WindowState.Maximized,
                    AllowsTransparency = false,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Owner = owner,
                    Content = Player
                };

                window.KeyDown += ExclusivePlayerWindow_KeyDown;
                window.StateChanged += ExclusivePlayerWindow_StateChanged;
                window.Closed += ExclusivePlayerWindow_Closed;

                _exclusivePlayerWindow = window;
                _isExclusiveFullscreen = true;
                Player.IsFullscreen = true;
                Player.HorizontalAlignment = HorizontalAlignment.Stretch;
                Player.VerticalAlignment = VerticalAlignment.Stretch;
                Player.Margin = new Thickness(0);
                Player.Width = double.NaN;
                Player.Height = double.NaN;
                window.Show();
                Player.Focus();
                Dispatcher.BeginInvoke(new Action(() => Player.RestorePlayback(_exclusiveResumeSeconds, _exclusiveResumeWasPlaying)), System.Windows.Threading.DispatcherPriority.Background);
                reparentSucceeded = true;
            }
            catch
            {
                try { Player.IsFullscreen = false; } catch { }
                try { _isExclusiveFullscreen = false; } catch { }
                try { _exclusivePlayerWindow = null; } catch { }
                try { Player.IsReparenting = false; } catch { }
            }
            finally
            {
                if (reparentSucceeded)
                {
                    Dispatcher.BeginInvoke(new Action(() => Player.IsReparenting = false), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }
        }

        private void ExitExclusiveFullscreen()
        {
            if (!_isExclusiveFullscreen && _exclusivePlayerWindow == null)
                return;

            try
            {
                _exclusiveResumeSeconds = Player.GetCurrentPlaybackSeconds();
                _exclusiveResumeWasPlaying = Player.GetIsActuallyPlaying();
            }
            catch
            {
                _exclusiveResumeSeconds = 0;
                _exclusiveResumeWasPlaying = false;
            }

            var window = _exclusivePlayerWindow;
            _exclusivePlayerWindow = null;

            _isExclusiveFullscreen = false;
            Player.IsFullscreen = false;

            Player.IsReparenting = true;
            var reparentSucceeded = false;
            try
            {
                if (Player.Parent is Panel currentParent)
                    currentParent.Children.Remove(Player);
                else if (window != null && ReferenceEquals(window.Content, Player))
                    window.Content = null;

                if (_playerOriginalParent != null)
                {
                    var insertIndex = _playerOriginalIndex;
                    if (insertIndex < 0 || insertIndex > _playerOriginalParent.Children.Count)
                        insertIndex = _playerOriginalParent.Children.Count;
                    _playerOriginalParent.Children.Insert(insertIndex, Player);

                    Grid.SetRow(Player, _playerOriginalRow);
                    Grid.SetColumn(Player, _playerOriginalColumn);
                    Grid.SetRowSpan(Player, _playerOriginalRowSpan);
                    Grid.SetColumnSpan(Player, _playerOriginalColumnSpan);
                    Panel.SetZIndex(Player, _playerOriginalZIndex);
                    Player.HorizontalAlignment = _playerOriginalHorizontalAlignment;
                    Player.VerticalAlignment = _playerOriginalVerticalAlignment;
                    Player.Margin = _playerOriginalMargin;
                    Player.Width = _playerOriginalWidth;
                    Player.Height = _playerOriginalHeight;
                }
                reparentSucceeded = true;
            }
            catch
            {
                try { Player.IsReparenting = false; } catch { }
            }
            finally
            {
                if (reparentSucceeded)
                {
                    Dispatcher.BeginInvoke(new Action(() => Player.IsReparenting = false), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            Dispatcher.BeginInvoke(new Action(() => Player.RestorePlayback(_exclusiveResumeSeconds, _exclusiveResumeWasPlaying)), System.Windows.Threading.DispatcherPriority.Background);

            if (window != null)
            {
                window.KeyDown -= ExclusivePlayerWindow_KeyDown;
                window.StateChanged -= ExclusivePlayerWindow_StateChanged;
                window.Closed -= ExclusivePlayerWindow_Closed;
                if (window.IsVisible)
                    window.Close();
            }

            Dispatcher.BeginInvoke(new Action(() => PositionMiniPlayerIfNeeded()), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void ExclusivePlayerWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.F11)
            {
                ExitExclusiveFullscreen();
                e.Handled = true;
            }
        }

        private void ExclusivePlayerWindow_StateChanged(object? sender, EventArgs e)
        {
            try
            {
                if (sender is Window window && window.WindowState == WindowState.Minimized)
                    ExitExclusiveFullscreen();
            }
            catch
            {
            }
        }

        private void ExclusivePlayerWindow_Closed(object? sender, EventArgs e)
        {
            if (_isExclusiveFullscreen)
                ExitExclusiveFullscreen();
        }

        private void ListBoxItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is Views.ViewModels.MediaItem mediaItem)
            {
                if (DataContext is MediaCentreViewModel vm)
                {
                    vm.PlayItemCommand.Execute(mediaItem);
                }
            }
        }

        private void CoversCard_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is Views.ViewModels.MediaItem item)
                {
                    // Always allow preview to populate on hover so info/backdrop appear immediately
                    _vm.SetPreviewItem(item);
                }
            }
            catch
            {
            }
        }

        private void NowPlayingSeek_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _vm.BeginUserSeek();
        }

        private void NowPlayingSeek_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
                _vm.EndUserSeek(slider.Value);
        }

        private void ServersScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"[MediaCentre SCROLL] Triggered - Offset:{e.VerticalOffset:F0} Viewport:{e.ViewportHeight:F0} Extent:{e.ExtentHeight:F0} Change:{e.VerticalChange:F0}");
                
                if (!_vm.IsServersView)
                {
                    Debug.WriteLine("[MediaCentre SCROLL] Not in servers view");
                    return;
                }
                if (!_vm.IsServerCatalogPanelOpen)
                {
                    Debug.WriteLine("[MediaCentre SCROLL] Catalog panel not open");
                    return;
                }
                if (_vm.IsServerCatalogBusy)
                {
                    Debug.WriteLine("[MediaCentre SCROLL] Catalog busy");
                    return;
                }
                if (e.ExtentHeight <= 0 || e.ViewportHeight <= 0)
                {
                    Debug.WriteLine("[MediaCentre SCROLL] Invalid dimensions");
                    return;
                }
                if (e.VerticalChange <= 0)
                {
                    Debug.WriteLine("[MediaCentre SCROLL] Not scrolling down");
                    return;
                }

                var nearBottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 40;
                Debug.WriteLine($"[MediaCentre SCROLL] Near bottom: {nearBottom} (distance from bottom: {e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight):F0}px)");
                
                if (!nearBottom) return;

                var now = DateTime.UtcNow;
                var timeSince = (now - _lastServerCatalogLoadMoreUtc).TotalMilliseconds;
                Debug.WriteLine($"[MediaCentre SCROLL] Time since last load: {timeSince:F0}ms");
                
                if (timeSince < 150)
                {
                    Debug.WriteLine("[MediaCentre SCROLL] THROTTLED");
                    return;
                }

                Debug.WriteLine("[MediaCentre SCROLL] *** TRIGGERING LOAD MORE ***");
                _lastServerCatalogLoadMoreUtc = now;
                _vm.TryLoadMoreServerCatalog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaCentre SCROLL] ERROR: {ex.Message}");
            }
        }

        private void MediaAiChatScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                if (sender is not ScrollViewer sv) return;
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void MediaAiPopup_Opened(object? sender, EventArgs e)
        {
            try
            {
                if (!_vm.IsChatVisible) return;

                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        RefreshMediaAiInput();
                        MediaAiInput?.Focus();
                        Keyboard.Focus(MediaAiInput);
                        MediaAiInput?.Select(MediaAiInput.Text?.Length ?? 0, 0);
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }
        }

        private void MediaAiInput_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsChatVisible) return;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        RefreshMediaAiInput();
                        MediaAiInput?.Focus();
                        Keyboard.Focus(MediaAiInput);
                        MediaAiInput?.Select(MediaAiInput.Text?.Length ?? 0, 0);
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }
        }

        private void MediaAiInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not TextBox tb) return;
                if (!tb.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    tb.Focus();
                    Keyboard.Focus(tb);
                    tb.Select(tb.Text?.Length ?? 0, 0);
                }
            }
            catch
            {
            }
        }

        private void AiModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var window = new AtlasAI.Views.AiModeWindow();
                if (owner != null) window.Owner = owner;
                window.Show();
            }
            catch
            {
            }
        }

        private void NeoNavClose_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                try
                {
                    if (NeoNavColumn != null && NeoNavColumn.Width.Value > 0)
                        _restoreNeoNavWidth = NeoNavColumn.Width;
                }
                catch { }

                if (NeoNavColumn != null)
                    NeoNavColumn.Width = new GridLength(0);
                _isSidebarCollapsed = true;
                if (NeoNavReopenTab != null)
                {
                    NeoNavReopenTab.IsEnabled = true;
                    NeoNavReopenTab.Visibility = Visibility.Visible;
                }

                try { if (NeoNavSidebar != null) NeoNavSidebar.Visibility = Visibility.Collapsed; } catch { }

                if (NeoHeaderLayoutGrid != null)
                    NeoHeaderLayoutGrid.Margin = new Thickness(18, 0, 24, 0);

                // WebView2 is an airspace (HWND) and can sit above WPF elements regardless of ZIndex.
                // When the sidebar is fully collapsed we keep a small left strip so the reopen tab isn't blocked.
                try
                {
                    if (LibraryContent != null)
                        LibraryContent.Margin = new Thickness(18, 0, 0, 0);
                }
                catch
                {
                }
            }
            catch { }
        }

        private void NeoNavReopen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (NeoNavColumn != null)
                    NeoNavColumn.Width = _restoreNeoNavWidth.Value > 0 ? _restoreNeoNavWidth : new GridLength(200);
                _isSidebarCollapsed = false;
                if (NeoNavReopenTab != null)
                    NeoNavReopenTab.Visibility = Visibility.Collapsed;

                try { if (NeoNavSidebar != null) NeoNavSidebar.Visibility = Visibility.Visible; } catch { }

                if (NeoHeaderLayoutGrid != null)
                    NeoHeaderLayoutGrid.Margin = new Thickness(40, 0, 24, 0);

                try
                {
                    if (LibraryContent != null)
                        LibraryContent.Margin = new Thickness(0);
                }
                catch
                {
                }
            }
            catch { }
        }

        /// <summary>
        /// Immersive mode toggle — called by the MediaHub React frontend via bridge message
        /// <c>mediahub.immersive.set { enabled: bool }</c>.
        /// Collapses the Atlas NeoNav sidebar (WPF) when entering immersive and restores it on exit.
        /// The matching reveal arrow lives entirely inside the React/WebView2 layer to avoid
        /// HWND airspace conflicts.
        /// </summary>
        /// <summary>Called by ServersView on Loaded so we always have a direct reference to the active instance.</summary>
        public void RegisterActiveServersView(AtlasAI.Views.MediaCentre.ServersView sv)
        {
            _activeServersViewRef = new WeakReference<AtlasAI.Views.MediaCentre.ServersView>(sv);
            AtlasAI.Core.AppLogger.LogInfo("[MediaHubImmersive] ServersView registered");
        }

        /// <summary>Called by ServersView on Unloaded to prevent stale references.</summary>
        public void UnregisterActiveServersView(AtlasAI.Views.MediaCentre.ServersView sv)
        {
            if (_activeServersViewRef != null &&
                _activeServersViewRef.TryGetTarget(out var existing) &&
                ReferenceEquals(existing, sv))
            {
                _activeServersViewRef = null;
                AtlasAI.Core.AppLogger.LogInfo("[MediaHubImmersive] ServersView unregistered");
            }
        }

        /// <summary>Posts a chrome-state notification to the active MediaHub WebView2 so that
        /// the React sidebar can collapse/restore in sync with the Atlas header.</summary>
        public void NotifyMediaHubChromeState(bool collapsed)
        {
            try
            {
                AtlasAI.Core.AppLogger.LogInfo($"[MediaHubImmersive] NotifyMediaHubChromeState called collapsed={collapsed}");

                // Prefer the directly-registered reference; fall back to visual-tree walk
                AtlasAI.Views.MediaCentre.ServersView? sv = null;
                if (_activeServersViewRef != null)
                    _activeServersViewRef.TryGetTarget(out sv);

                if (sv == null)
                {
                    AtlasAI.Core.AppLogger.LogInfo("[MediaHubImmersive] stored ref null — falling back to FindDescendant");
                    sv = FindDescendant<AtlasAI.Views.MediaCentre.ServersView>(this);
                }

                if (sv == null)
                {
                    AtlasAI.Core.AppLogger.LogInfo("[MediaHubImmersive] ServersView NOT FOUND in visual tree — message not sent");
                    return;
                }

                AtlasAI.Core.AppLogger.LogInfo($"[MediaHubImmersive] ServersView found, WebView ready={sv.IsWebViewReady}");
                var collapsedStr = collapsed ? "true" : "false";
                var json = $"{{\"type\":\"mediahub.chromeCollapsed\",\"payload\":{{\"collapsed\":{collapsedStr}}}}}";
                sv.PostToWebView(json);
                AtlasAI.Core.AppLogger.LogInfo($"[MediaHubImmersive] headerCollapsed={collapsed} sentToWebView=true");
            }
            catch (Exception ex)
            {
                AtlasAI.Core.AppLogger.LogError($"[MediaHubImmersive] NotifyMediaHubChromeState error: {ex.Message}");
            }
        }

        public void SetMediaHubImmersiveMode(bool enabled)
        {
            try
            {
                _isMediaHubImmersive = enabled;
                AtlasAI.Core.AppLogger.LogInfo($"[MediaHubImmersive] enabled={enabled}");

                if (enabled)
                {
                    // Save current sidebar width before collapsing
                    if (NeoNavColumn != null && NeoNavColumn.Width.Value > 0)
                        _restoreNeoNavWidthImmersive = NeoNavColumn.Width;

                    // Collapse the WPF NeoNav sidebar column completely
                    if (NeoNavColumn != null)
                        NeoNavColumn.Width = new GridLength(0);
                    try { if (NeoNavSidebar != null) NeoNavSidebar.Visibility = Visibility.Collapsed; } catch { }

                    // Never show the WPF reopen tab — the React layer handles the restore arrow
                    if (NeoNavReopenTab != null)
                        NeoNavReopenTab.Visibility = Visibility.Collapsed;

                    // Remove any residual left-margin gutter on the content area
                    try { if (LibraryContent != null) LibraryContent.Margin = new Thickness(0); } catch { }
                }
                else
                {
                    // Restore the NeoNav sidebar
                    if (NeoNavColumn != null)
                        NeoNavColumn.Width = _restoreNeoNavWidthImmersive.Value > 0
                            ? _restoreNeoNavWidthImmersive : new GridLength(200);
                    try { if (NeoNavSidebar != null) NeoNavSidebar.Visibility = Visibility.Visible; } catch { }

                    if (NeoNavReopenTab != null)
                        NeoNavReopenTab.Visibility = Visibility.Collapsed;

                    try { if (LibraryContent != null) LibraryContent.Margin = new Thickness(0); } catch { }
                }
            }
            catch { }
        }

        private void VoiceModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var window = new AtlasAI.Views.VoiceModeWindow();
                if (owner != null) window.Owner = owner;
                window.Show();
            }
            catch
            {
            }
        }

        private void ActionsDropdown_Click(object sender, RoutedEventArgs e)
        {
            // Toggle is handled automatically via IsChecked binding to Popup.IsOpen
        }
    }
}
