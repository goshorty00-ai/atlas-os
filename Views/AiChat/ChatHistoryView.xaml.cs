using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AtlasAI.Conversation.Models;
using AtlasAI.Conversation.Services;

namespace AtlasAI.Views.AiChat;

public partial class ChatHistoryView : UserControl
{
    private readonly SessionStore _sessionStore = new();
    private readonly CommandCenterWindow? _owner;
    private ChatSession? _selectedSession;

    public ChatHistoryView(CommandCenterWindow? owner = null)
    {
        InitializeComponent();
        _owner = owner;
        Loaded += async (_, __) => await RefreshAsync();
    }

    private async Task RefreshAsync(string? search = null)
    {
        try
        {
            SessionTree.ItemsSource = await BuildGroupsAsync(search);
        }
        catch
        {
        }
    }

    private async Task<List<HistoryGroup>> BuildGroupsAsync(string? search)
    {
        var query = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var results = await _sessionStore.SearchSessionsAsync(query);
            var sessions = results
                .Select(result => new SessionIndexEntry
                {
                    Id = result.SessionId,
                    Title = result.Title,
                    CreatedAt = result.CreatedAt,
                    LastMessageAt = result.CreatedAt,
                    MessageCount = 0,
                    Provider = result.MatchType
                })
                .ToList();

            return new List<HistoryGroup>
            {
                new("Search Results", sessions)
            };
        }

        return _sessionStore.GetSessionsByDate()
            .Where(pair => pair.Value.Count > 0)
            .Select(pair => new HistoryGroup(pair.Key, pair.Value))
            .ToList();
    }

    private async void SessionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not SessionIndexEntry entry)
            return;

        _selectedSession = await _sessionStore.LoadSessionAsync(entry.Id);
        RenderPreview(_selectedSession);
    }

    private void RenderPreview(ChatSession? session)
    {
        PreviewStack.Children.Clear();
        OpenSessionButton.IsEnabled = session != null;
        DeleteSessionButton.IsEnabled = session != null;

        if (session == null)
        {
            SessionTitleText.Text = "Select a conversation";
            SessionMetaText.Text = "History preview appears here.";
            return;
        }

        SessionTitleText.Text = string.IsNullOrWhiteSpace(session.Title) ? "Untitled chat" : session.Title;
        SessionMetaText.Text = $"{session.LastMessageAt:dd MMM yyyy HH:mm}  |  {session.Messages.Count} messages";

        foreach (var message in session.Messages.OrderBy(m => m.Timestamp))
        {
            var border = new Border
            {
                Background = message.Role == MessageRole.User
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D2433"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#101827")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(message.Role == MessageRole.User ? "#335C7CFA" : "#3322D3EE")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = message.Role == MessageRole.User ? "YOU" : message.Role == MessageRole.System ? "SYSTEM" : "ATLAS",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(message.Role == MessageRole.User ? "#93C5FD" : "#22D3EE"))
            });
            stack.Children.Add(new TextBlock
            {
                Text = message.Content ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAF2F8"))
            });
            stack.Children.Add(new TextBlock
            {
                Text = message.Timestamp == default ? string.Empty : message.Timestamp.ToString("dd MMM HH:mm"),
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"))
            });

            border.Child = stack;
            PreviewStack.Children.Add(border);
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        await RefreshAsync(SearchBox.Text);
    }

    private async void OpenSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSession == null || _owner == null)
            return;

        await _owner.OpenChatHistorySessionAsync(_selectedSession.Id);
    }

    private async void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_owner == null)
            return;

        await _owner.StartNewChatSessionAsync();
    }

    private async void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSession == null)
            return;

        await _sessionStore.DeleteSessionAsync(_selectedSession.Id);
        _selectedSession = null;
        RenderPreview(null);
        await RefreshAsync(SearchBox.Text);
    }
}

public sealed record HistoryGroup(string Title, List<SessionIndexEntry> Sessions);