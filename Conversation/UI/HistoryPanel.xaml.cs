using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AtlasAI.Conversation.Services;

namespace AtlasAI.Conversation.UI
{
    public partial class HistoryPanel : UserControl
    {
        private ConversationManager? _conversationManager;
        private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;

        public event EventHandler? NewChatRequested;
        public event EventHandler<string>? SessionSelected;
        public event EventHandler<string>? SessionContinueRequested;

        public HistoryPanel()
        {
            InitializeComponent();
            
            _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchDebounceTimer.Tick += SearchDebounce_Tick;
        }

        public void Initialize(ConversationManager conversationManager)
        {
            _conversationManager = conversationManager;
            RefreshHistory();
        }

        public void RefreshHistory()
        {
            if (_conversationManager == null) return;

            SessionsContainer.Children.Clear();

            var groupedSessions = _conversationManager.GetSessionHistory();

            foreach (var group in groupedSessions)
            {
                if (group.Value.Count == 0) continue;

                // Add group header
                var header = new TextBlock
                {
                    Text = group.Key,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(110, 118, 129)),
                    Margin = new Thickness(8, 12, 8, 6)
                };
                SessionsContainer.Children.Add(header);

                // Add sessions
                foreach (var session in group.Value)
                {
                    var sessionItem = CreateSessionItem(session);
                    SessionsContainer.Children.Add(sessionItem);
                }
            }

            // Show empty state if no sessions
            if (!groupedSessions.Values.Any(g => g.Count > 0))
            {
                var emptyState = new TextBlock
                {
                    Text = "No conversations yet.\nStart chatting to see your history here.",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(16, 40, 16, 0)
                };
                SessionsContainer.Children.Add(emptyState);
            }
        }

        private Border CreateSessionItem(SessionIndexEntry session)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(12, 10, 12, 10),
                Cursor = Cursors.Hand,
                Tag = session.Id
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Title and info
            var infoStack = new StackPanel();
            
            var title = new TextBlock
            {
                Text = session.Title,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 200
            };
            infoStack.Children.Add(title);

            var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            
            // Provider icon
            var providerIcon = session.Provider?.ToLower() switch
            {
                "claude" => "🟣",
                "openai" => "🟢",
                _ => "💬"
            };
            meta.Children.Add(new TextBlock
            {
                Text = providerIcon,
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            meta.Children.Add(new TextBlock
            {
                Text = $"{session.MessageCount} messages • {FormatTime(session.LastMessageAt)}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 118, 129))
            });
            infoStack.Children.Add(meta);

            Grid.SetColumn(infoStack, 0);
            grid.Children.Add(infoStack);

            // Continue button
            var continueBtn = new Button
            {
                Content = "→",
                FontSize = 14,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Continue this chat",
                Tag = session.Id,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center
            };
            continueBtn.Click += ContinueSession_Click;
            Grid.SetColumn(continueBtn, 1);
            grid.Children.Add(continueBtn);

            border.Child = grid;

            // Hover effects
            border.MouseEnter += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(33, 38, 45));
                continueBtn.Visibility = Visibility.Visible;
            };
            border.MouseLeave += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));
                continueBtn.Visibility = Visibility.Collapsed;
            };

            border.MouseLeftButtonUp += SessionItem_Click;

            return border;
        }

        private string FormatTime(DateTime time)
        {
            var diff = DateTime.Now - time;
            if (diff.TotalMinutes < 1) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return time.ToString("MMM d");
        }

        private void SessionItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string sessionId)
            {
                SessionSelected?.Invoke(this, sessionId);
            }
        }

        private void ContinueSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                SessionContinueRequested?.Invoke(this, sessionId);
                e.Handled = true;
            }
        }

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            NewChatRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer?.Start();
        }

        private async void SearchDebounce_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer?.Stop();
            
            var query = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                RefreshHistory();
                return;
            }

            await SearchSessionsAsync(query);
        }

        private async Task SearchSessionsAsync(string query)
        {
            if (_conversationManager == null) return;

            var results = await _conversationManager.SearchSessionsAsync(query);
            
            SessionsContainer.Children.Clear();

            if (results.Count == 0)
            {
                var noResults = new TextBlock
                {
                    Text = $"No results for \"{query}\"",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(16, 40, 16, 0)
                };
                SessionsContainer.Children.Add(noResults);
                return;
            }

            var header = new TextBlock
            {
                Text = $"Search Results ({results.Count})",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 118, 129)),
                Margin = new Thickness(8, 12, 8, 6)
            };
            SessionsContainer.Children.Add(header);

            foreach (var result in results)
            {
                var item = CreateSearchResultItem(result);
                SessionsContainer.Children.Add(item);
            }
        }

        private Border CreateSearchResultItem(SessionSearchResult result)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(12, 10, 12, 10),
                Cursor = Cursors.Hand,
                Tag = result.SessionId
            };

            var stack = new StackPanel();

            var title = new TextBlock
            {
                Text = result.Title,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(title);

            if (!string.IsNullOrEmpty(result.MatchPreview))
            {
                var preview = new TextBlock
                {
                    Text = result.MatchPreview,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                stack.Children.Add(preview);
            }

            border.Child = stack;

            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(33, 38, 45));
            border.MouseLeave += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));
            border.MouseLeftButtonUp += SessionItem_Click;

            return border;
        }
    }
}
