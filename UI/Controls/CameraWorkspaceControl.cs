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

namespace AtlasAI.UI.Controls
{
    internal sealed class CameraWorkspaceControl : Border
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
        private string _focusedSessionId = string.Empty;

        public event EventHandler? WorkspaceChanged;

        public bool HasSessions => _sessions.Count > 0;
        public int SessionCount => _sessions.Count;
        public int RecordingCount => _recordingService.ActiveRecordingCount;
        public bool IsFocusedMode => !string.IsNullOrWhiteSpace(_focusedSessionId);

        public string SummaryText => _sessions.Count == 0
            ? "No cameras open."
            : _sessions.Count == 1
                ? $"1 camera open. {(RecordingCount == 0 ? "" : $"{RecordingCount} recording running.")}".Trim()
                : $"{_sessions.Count} cameras open in grid view. {(RecordingCount == 0 ? "" : $"{RecordingCount} recordings running.")}".Trim();

        public CameraWorkspaceControl(SmartHomeCameraRecordingService recordingService, Func<string, Task>? stopManagedCameraAsync)
        {
            _recordingService = recordingService;
            _stopManagedCameraAsync = stopManagedCameraAsync;

            Background = new SolidColorBrush(Color.FromRgb(5, 8, 13));
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x67, 0xE8, 0xF9));
            BorderThickness = new Thickness(0);

            _cameraGrid = new UniformGrid
            {
                Columns = 1,
                Margin = new Thickness(0),
            };

            SizeChanged += (_, _) => RefreshLayout();

            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _cameraGrid,
            };
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
                    existingSession.Browser.CoreWebView2.Navigate(existingSession.NavigationUrl);

                UpdateSessionChrome(existingSession, "Camera view refreshed.");
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

            if (session.Browser != null)
            {
                await session.Browser.EnsureCoreWebView2Async();
                session.Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                session.Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
                session.Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                session.Browser.CoreWebView2.Navigate(session.NavigationUrl);
            }

            UpdateSessionChrome(session, string.IsNullOrWhiteSpace(session.RecordingUrl)
                ? "Live view opened. Recording will enable when Atlas has a direct stream URL."
                : "Live view opened. Recording is ready for this camera.");
        }

        public async Task CloseAllSessionsAsync()
        {
            var sessionIds = _sessions.Keys.ToArray();
            foreach (var sessionId in sessionIds)
                await CloseSessionAsync(sessionId).ConfigureAwait(true);
        }

        public void ToggleFocusedLayout()
        {
            if (string.IsNullOrWhiteSpace(_focusedSessionId))
            {
                var first = _sessions.Keys.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first))
                    _focusedSessionId = first;
            }
            else
            {
                _focusedSessionId = string.Empty;
            }

            RefreshLayout();
        }

        public void NotifyCameraReady(string sessionId, string message)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                UpdateSessionChrome(session, message);
        }

        public void NotifyCameraStopped(string sessionId, string? message = null)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                UpdateSessionChrome(session, string.IsNullOrWhiteSpace(message) ? "Camera stopped." : message.Trim());
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
                MinHeight = 320,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0x67, 0xE8, 0xF9)),
                Background = new SolidColorBrush(Color.FromRgb(8, 16, 29)),
            };

            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            header.Children.Add(new TextBlock
            {
                Text = session.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(216, 249, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(actions, 1);

            session.RecordButton = CreateButton("Record", async (_, _) => await ToggleRecordingAsync(session).ConfigureAwait(true));
            session.FocusButton = CreateButton("Single", (_, _) => ToggleSessionFocus(session.SessionId));
            var closeButton = CreateButton("Close", async (_, _) => await CloseSessionAsync(session.SessionId).ConfigureAwait(true));
            actions.Children.Add(session.RecordButton);
            actions.Children.Add(session.FocusButton);
            actions.Children.Add(closeButton);
            header.Children.Add(actions);
            shell.Children.Add(header);

            session.Browser = new WebView2
            {
                Margin = new Thickness(12, 0, 12, 12),
                MinHeight = 220,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetRow(session.Browser, 1);
            shell.Children.Add(session.Browser);

            session.StatusText = new TextBlock
            {
                Margin = new Thickness(12, 0, 12, 12),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(127, 232, 249)),
                TextWrapping = TextWrapping.Wrap,
                Text = "Opening camera view...",
            };
            Grid.SetRow(session.StatusText, 2);
            shell.Children.Add(session.StatusText);

            container.Child = shell;
            return container;
        }

        private async Task ToggleRecordingAsync(CameraSessionView session)
        {
            if (_recordingService.IsRecordingSession(session.RecordingId))
            {
                var stop = await _recordingService.StopAsync(session.RecordingId, CancellationToken.None).ConfigureAwait(true);
                UpdateSessionChrome(session, stop.Message);
                return;
            }

            if (!SmartHomeCameraRecordingService.IsSupportedSourceUrl(session.RecordingUrl))
            {
                UpdateSessionChrome(session, "Recording is available when Atlas has a direct stream URL.");
                return;
            }

            var start = await _recordingService.StartAsync(session.RecordingUrl, session.Title, session.RecordingId, CancellationToken.None).ConfigureAwait(true);
            UpdateSessionChrome(session, start.Message);
        }

        private void ToggleSessionFocus(string sessionId)
        {
            _focusedSessionId = string.Equals(_focusedSessionId, sessionId, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : sessionId;
            RefreshLayout();
        }

        private async Task CloseSessionAsync(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            if (_recordingService.IsRecordingSession(session.RecordingId))
            {
                try { await _recordingService.StopAsync(session.RecordingId, CancellationToken.None).ConfigureAwait(true); } catch { }
            }

            if (!string.IsNullOrWhiteSpace(session.ManagedCameraId) && _stopManagedCameraAsync != null)
            {
                try { await _stopManagedCameraAsync(session.ManagedCameraId).ConfigureAwait(true); } catch { }
            }

            try { session.Browser?.CoreWebView2?.Navigate("about:blank"); } catch { }

            _sessions.Remove(sessionId);
            if (string.Equals(_focusedSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                _focusedSessionId = string.Empty;

            RefreshLayout();
        }

        private void RefreshLayout()
        {
            if (!string.IsNullOrWhiteSpace(_focusedSessionId) && !_sessions.ContainsKey(_focusedSessionId))
                _focusedSessionId = string.Empty;

            var visible = string.IsNullOrWhiteSpace(_focusedSessionId)
                ? _sessions.Values.ToArray()
                : _sessions.Values.Where(session => string.Equals(session.SessionId, _focusedSessionId, StringComparison.OrdinalIgnoreCase)).ToArray();

            _cameraGrid.Children.Clear();
            foreach (var session in visible)
            {
                if (session.Container != null)
                    _cameraGrid.Children.Add(session.Container);
            }

            _cameraGrid.Columns = string.IsNullOrWhiteSpace(_focusedSessionId)
                ? GetColumnCount(visible.Length)
                : 1;

            ApplySessionSizing(visible, _cameraGrid.Columns);

            foreach (var session in _sessions.Values)
            {
                if (session.FocusButton != null)
                    session.FocusButton.Content = string.Equals(_focusedSessionId, session.SessionId, StringComparison.OrdinalIgnoreCase) ? "Grid" : "Single";
                if (session.RecordButton != null)
                    session.RecordButton.Content = _recordingService.IsRecordingSession(session.RecordingId) ? "Stop" : "Record";
            }

            WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateSessionChrome(CameraSessionView session, string message)
        {
            if (session.StatusText != null)
                session.StatusText.Text = message;
            if (session.RecordButton != null)
                session.RecordButton.Content = _recordingService.IsRecordingSession(session.RecordingId) ? "Stop" : "Record";
            WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplySessionSizing(IReadOnlyCollection<CameraSessionView> visibleSessions, int columns)
        {
            var tileHeight = GetTileHeight(visibleSessions.Count, columns);
            var browserHeight = Math.Max(180, tileHeight - 92);

            foreach (var session in _sessions.Values)
            {
                if (session.Container != null)
                {
                    session.Container.Height = tileHeight;
                    session.Container.MinHeight = tileHeight;
                }

                if (session.Browser != null)
                {
                    session.Browser.Height = browserHeight;
                    session.Browser.MinHeight = browserHeight;
                }
            }
        }

        private static double GetTileHeight(int visibleCount, int columns)
        {
            if (visibleCount <= 1)
                return 620;

            if (columns <= 1)
                return 420;

            if (columns == 2)
                return 320;

            return 260;
        }

        private static int GetColumnCount(int sessionCount)
        {
            if (sessionCount <= 1) return 1;
            if (sessionCount <= 4) return 2;
            if (sessionCount <= 9) return 3;
            return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(sessionCount)));
        }

        private static Button CreateButton(string label, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Content = label,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush(Color.FromRgb(22, 35, 48)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x67, 0xE8, 0xF9)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            button.Click += handler;
            return button;
        }

        private static string NormalizeSessionId(string? sessionId, string? title, string? navigationUrl)
        {
            var normalized = (sessionId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;

            var normalizedTitle = (title ?? string.Empty).Trim();
            var normalizedUrl = (navigationUrl ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalizedTitle) ? normalizedUrl : $"{normalizedTitle}:{normalizedUrl}";
        }
    }
}