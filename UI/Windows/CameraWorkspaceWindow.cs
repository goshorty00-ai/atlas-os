using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AtlasAI.SmartHome;
using Microsoft.Web.WebView2.Wpf;

namespace AtlasAI.UI.Windows
{
    internal sealed class CameraWorkspaceWindow : Window
    {
        private sealed class CameraSessionView
        {
            public required string SessionId { get; init; }
            public required string Title { get; set; }
            public required string NavigationUrl { get; set; }
            public required string RecordingUrl { get; set; }
            public required string RecordingId { get; set; }
            public string ManagedCameraId { get; set; } = string.Empty;
            public Border? Container { get; set; }
            public WebView2? Browser { get; set; }
            public TextBlock? StatusText { get; set; }
            public Button? RecordButton { get; set; }
            public Button? FocusButton { get; set; }
        }

        private readonly SmartHomeCameraRecordingService _recordingService;
        private readonly Func<string, Task>? _stopManagedCameraAsync;
        private readonly Dictionary<string, CameraSessionView> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly UniformGrid _cameraGrid;
        private readonly TextBlock _summaryText;
        private readonly Button _layoutButton;
        private readonly Button _fullscreenButton;
        private string _focusedSessionId = string.Empty;
        private bool _fullscreenMode;
        private WindowStyle _restoreWindowStyle = WindowStyle.SingleBorderWindow;
        private ResizeMode _restoreResizeMode = ResizeMode.CanResize;
        private WindowState _restoreWindowState = WindowState.Normal;
        private bool _restoreTopmost;

        public CameraWorkspaceWindow(SmartHomeCameraRecordingService recordingService, Func<string, Task>? stopManagedCameraAsync)
        {
            _recordingService = recordingService;
            _stopManagedCameraAsync = stopManagedCameraAsync;

            Title = "Atlas Camera Workspace";
            Width = 1520;
            Height = 920;
            MinWidth = 920;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(4, 10, 18));
            Foreground = Brushes.White;

            var root = new DockPanel();

            var toolbar = new Grid
            {
                Margin = new Thickness(16, 14, 16, 12),
            };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
            };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Camera Workspace",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(207, 250, 254)),
            });

            _summaryText = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(127, 232, 249)),
                Text = "No active camera views.",
            };
            titleStack.Children.Add(_summaryText);
            toolbar.Children.Add(titleStack);

            var actionBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(actionBar, 1);

            _layoutButton = CreateToolbarButton("Single View", ToggleFocusedLayout);
            _fullscreenButton = CreateToolbarButton("Fullscreen", ToggleFullscreenWindow);
            var closeAllButton = CreateToolbarButton("Close All", async (_, _) => await CloseAllSessionsAsync().ConfigureAwait(true));

            actionBar.Children.Add(_layoutButton);
            actionBar.Children.Add(_fullscreenButton);
            actionBar.Children.Add(closeAllButton);
            toolbar.Children.Add(actionBar);

            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            _cameraGrid = new UniformGrid
            {
                Margin = new Thickness(16, 0, 16, 16),
                Columns = 1,
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _cameraGrid,
            };
            root.Children.Add(scrollViewer);

            Content = root;
            Closed += CameraWorkspaceWindow_Closed;
        }

        public async Task OpenCameraAsync(string sessionId, string navigationUrl, string title, string? recordingUrl, string? managedCameraId)
        {
            var effectiveSessionId = NormalizeSessionId(sessionId, title, navigationUrl);
            var effectiveRecordingId = effectiveSessionId;
            var effectiveRecordingUrl = SmartHomeCameraRecordingService.IsSupportedSourceUrl(recordingUrl ?? string.Empty)
                ? (recordingUrl ?? string.Empty).Trim()
                : string.Empty;

            if (_sessions.TryGetValue(effectiveSessionId, out var existingSession))
            {
                existingSession.Title = string.IsNullOrWhiteSpace(title) ? existingSession.Title : title.Trim();
                existingSession.NavigationUrl = navigationUrl.Trim();
                existingSession.RecordingUrl = effectiveRecordingUrl;
                existingSession.RecordingId = effectiveRecordingId;
                existingSession.ManagedCameraId = (managedCameraId ?? string.Empty).Trim();

                if (existingSession.Browser?.CoreWebView2 != null)
                {
                    existingSession.Browser.CoreWebView2.Navigate(existingSession.NavigationUrl);
                }

                UpdateSessionChrome(existingSession, "Camera view refreshed.");
                FocusSession(existingSession.SessionId);
                return;
            }

            var session = new CameraSessionView
            {
                SessionId = effectiveSessionId,
                Title = string.IsNullOrWhiteSpace(title) ? "Camera View" : title.Trim(),
                NavigationUrl = navigationUrl.Trim(),
                RecordingUrl = effectiveRecordingUrl,
                RecordingId = effectiveRecordingId,
                ManagedCameraId = (managedCameraId ?? string.Empty).Trim(),
            };

            session.Container = BuildSessionContainer(session);
            _sessions[session.SessionId] = session;
            RefreshLayout();

            if (!IsVisible)
                Show();

            Activate();
            FocusSession(session.SessionId);

            if (session.Browser != null)
            {
                await session.Browser.EnsureCoreWebView2Async();
                session.Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                session.Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
                session.Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                session.Browser.CoreWebView2.Navigate(session.NavigationUrl);
                UpdateSessionChrome(session, string.IsNullOrWhiteSpace(session.RecordingUrl)
                    ? "Live view opened. Recording will enable when Atlas has a direct stream URL."
                    : "Live view opened. Recording is ready for this camera.");
            }
        }

        public void NotifyCameraStopped(string sessionId, string? message = null)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                UpdateSessionChrome(session, string.IsNullOrWhiteSpace(message) ? "Camera view stopped." : message.Trim());
        }

        public void NotifyCameraError(string sessionId, string message)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                UpdateSessionChrome(session, string.IsNullOrWhiteSpace(message) ? "Atlas could not start this camera." : message.Trim());
        }

        private Border BuildSessionContainer(CameraSessionView session)
        {
            var container = new Border
            {
                Margin = new Thickness(0, 0, 0, 14),
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x67, 0xE8, 0xF9)),
                Background = new SolidColorBrush(Color.FromRgb(8, 16, 29)),
            };

            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid
            {
                Margin = new Thickness(14, 12, 14, 10),
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = session.Title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(216, 249, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            header.Children.Add(titleBlock);

            var buttonBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(buttonBar, 1);

            session.RecordButton = CreateTileButton("Record", async (_, _) => await ToggleRecordingAsync(session).ConfigureAwait(true));
            session.FocusButton = CreateTileButton("Single", (_, _) => ToggleSessionFocus(session.SessionId));
            var closeButton = CreateTileButton("Close", async (_, _) => await CloseSessionAsync(session.SessionId).ConfigureAwait(true));

            buttonBar.Children.Add(session.RecordButton);
            buttonBar.Children.Add(session.FocusButton);
            buttonBar.Children.Add(closeButton);
            header.Children.Add(buttonBar);
            shell.Children.Add(header);

            session.Browser = new WebView2
            {
                Margin = new Thickness(14, 0, 14, 12),
            };
            Grid.SetRow(session.Browser, 1);
            shell.Children.Add(session.Browser);

            session.StatusText = new TextBlock
            {
                Margin = new Thickness(14, 0, 14, 14),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(127, 232, 249)),
                TextWrapping = TextWrapping.Wrap,
                Text = "Opening camera view...",
            };
            Grid.SetRow(session.StatusText, 2);
            shell.Children.Add(session.StatusText);

            container.Child = shell;
            session.Container = container;
            return container;
        }

        private async Task ToggleRecordingAsync(CameraSessionView session)
        {
            if (_recordingService.IsRecordingSession(session.RecordingId))
            {
                var stopResult = await _recordingService.StopAsync(session.RecordingId, CancellationToken.None).ConfigureAwait(true);
                UpdateSessionChrome(session, stopResult.Message);
                return;
            }

            if (!SmartHomeCameraRecordingService.IsSupportedSourceUrl(session.RecordingUrl))
            {
                UpdateSessionChrome(session, "Recording is available when Atlas has a direct stream URL.");
                return;
            }

            var startResult = await _recordingService.StartAsync(
                session.RecordingUrl,
                session.Title,
                session.RecordingId,
                CancellationToken.None).ConfigureAwait(true);
            UpdateSessionChrome(session, startResult.Message);
        }

        private void ToggleSessionFocus(string sessionId)
        {
            _focusedSessionId = string.Equals(_focusedSessionId, sessionId, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : sessionId;
            RefreshLayout();
        }

        private void ToggleFocusedLayout(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_focusedSessionId))
            {
                var firstSession = _sessions.Keys.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstSession))
                    _focusedSessionId = firstSession;
            }
            else
            {
                _focusedSessionId = string.Empty;
            }

            RefreshLayout();
        }

        private void ToggleFullscreenWindow(object? sender, RoutedEventArgs e)
        {
            if (_fullscreenMode)
            {
                WindowStyle = _restoreWindowStyle;
                ResizeMode = _restoreResizeMode;
                WindowState = _restoreWindowState;
                Topmost = _restoreTopmost;
                _fullscreenMode = false;
                _fullscreenButton.Content = "Fullscreen";
                return;
            }

            _restoreWindowStyle = WindowStyle;
            _restoreResizeMode = ResizeMode;
            _restoreWindowState = WindowState;
            _restoreTopmost = Topmost;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Topmost = true;
            _fullscreenMode = true;
            _fullscreenButton.Content = "Exit Fullscreen";
        }

        private async Task CloseSessionAsync(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            if (_recordingService.IsRecordingSession(session.RecordingId))
            {
                try
                {
                    await _recordingService.StopAsync(session.RecordingId, CancellationToken.None).ConfigureAwait(true);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(session.ManagedCameraId) && _stopManagedCameraAsync != null)
            {
                try
                {
                    await _stopManagedCameraAsync(session.ManagedCameraId).ConfigureAwait(true);
                }
                catch
                {
                }
            }

            try
            {
                session.Browser?.CoreWebView2?.Navigate("about:blank");
            }
            catch
            {
            }

            _sessions.Remove(sessionId);
            if (string.Equals(_focusedSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                _focusedSessionId = string.Empty;

            RefreshLayout();

            if (_sessions.Count == 0)
                Hide();
        }

        private async Task CloseAllSessionsAsync()
        {
            var sessionIds = _sessions.Keys.ToArray();
            foreach (var sessionId in sessionIds)
                await CloseSessionAsync(sessionId).ConfigureAwait(true);
        }

        private void FocusSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            _focusedSessionId = sessionId;
            RefreshLayout();
        }

        private void RefreshLayout()
        {
            if (!string.IsNullOrWhiteSpace(_focusedSessionId) && !_sessions.ContainsKey(_focusedSessionId))
                _focusedSessionId = string.Empty;

            var visibleSessions = string.IsNullOrWhiteSpace(_focusedSessionId)
                ? _sessions.Values.ToArray()
                : _sessions.Values.Where(session => string.Equals(session.SessionId, _focusedSessionId, StringComparison.OrdinalIgnoreCase)).ToArray();

            _cameraGrid.Children.Clear();
            foreach (var session in visibleSessions)
            {
                if (session.Container != null)
                    _cameraGrid.Children.Add(session.Container);
            }

            var count = visibleSessions.Length;
            _cameraGrid.Columns = string.IsNullOrWhiteSpace(_focusedSessionId)
                ? GetAutoColumnCount(count)
                : 1;

            foreach (var session in _sessions.Values)
            {
                if (session.FocusButton != null)
                    session.FocusButton.Content = string.Equals(_focusedSessionId, session.SessionId, StringComparison.OrdinalIgnoreCase) ? "Grid" : "Single";

                if (session.RecordButton != null)
                    session.RecordButton.Content = _recordingService.IsRecordingSession(session.RecordingId) ? "Stop" : "Record";
            }

            _layoutButton.Content = string.IsNullOrWhiteSpace(_focusedSessionId) ? "Single View" : "Show Grid";
            _summaryText.Text = _sessions.Count == 0
                ? "No active camera views."
                : $"{_sessions.Count} camera{(_sessions.Count == 1 ? string.Empty : "s")} active. {(_recordingService.ActiveRecordingCount == 0 ? "No recordings running." : $"{_recordingService.ActiveRecordingCount} recording{(_recordingService.ActiveRecordingCount == 1 ? string.Empty : "s")} running.")}";
        }

        private void UpdateSessionChrome(CameraSessionView session, string message)
        {
            if (session.StatusText != null)
                session.StatusText.Text = message;

            if (session.RecordButton != null)
                session.RecordButton.Content = _recordingService.IsRecordingSession(session.RecordingId) ? "Stop" : "Record";

            RefreshLayout();
        }

        private static int GetAutoColumnCount(int sessionCount)
        {
            if (sessionCount <= 1)
                return 1;
            if (sessionCount <= 4)
                return 2;
            if (sessionCount <= 9)
                return 3;

            return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(sessionCount)));
        }

        private static Button CreateToolbarButton(string content, RoutedEventHandler onClick)
        {
            var button = CreateTileButton(content, onClick);
            button.Margin = new Thickness(0, 0, 10, 0);
            button.MinWidth = 118;
            return button;
        }

        private static Button CreateTileButton(string content, RoutedEventHandler onClick)
        {
            var button = new Button
            {
                Content = content,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromRgb(22, 35, 48)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x67, 0xE8, 0xF9)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            button.Click += onClick;
            return button;
        }

        private static string NormalizeSessionId(string? sessionId, string? title, string? navigationUrl)
        {
            var normalizedSessionId = (sessionId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedSessionId))
                return normalizedSessionId;

            var normalizedTitle = (title ?? string.Empty).Trim();
            var normalizedUrl = (navigationUrl ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalizedTitle)
                ? normalizedUrl
                : $"{normalizedTitle}:{normalizedUrl}";
        }

        private async void CameraWorkspaceWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                await CloseAllSessionsAsync().ConfigureAwait(true);
            }
            catch
            {
            }
        }
    }
}