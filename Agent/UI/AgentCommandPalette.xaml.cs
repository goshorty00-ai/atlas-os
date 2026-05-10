using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AtlasAI.Agent.UI
{
    public partial class AgentCommandPalette : UserControl
    {
        private readonly ObservableCollection<MacroSuggestion> _suggestions = new();
        private int _selectedIndex = -1;

        public event EventHandler<MacroResult>? MacroExecuted;
        public event EventHandler<string>? FallbackToChat;

        public AgentCommandPalette()
        {
            InitializeComponent();
            SuggestionsList.ItemsSource = _suggestions;
            LoadAllMacros();
            UpdateAgentModeIndicator();

            AgentModeManager.AgentModeChanged += (s, e) => Dispatcher.Invoke(UpdateAgentModeIndicator);
        }

        private void UpdateAgentModeIndicator()
        {
            if (AgentModeManager.IsAgentModeEnabled)
            {
                AgentModeBadge.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xd3, 0xee));
                AgentModeText.Text = "AGENT MODE";
                AgentModeText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xd3, 0xee));
            }
            else
            {
                AgentModeBadge.Background = new SolidColorBrush(Color.FromArgb(0x20, 0x94, 0xa3, 0xb8));
                AgentModeText.Text = "CHAT MODE";
                AgentModeText.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
            }
        }

        private void LoadAllMacros()
        {
            _suggestions.Clear();
            foreach (var macro in AgentMacroEngine.Instance.GetMacroSummaries())
            {
                _suggestions.Add(new MacroSuggestion
                {
                    Id = macro.Id,
                    Title = macro.Title,
                    Description = macro.Description,
                    Icon = macro.Icon,
                    Keywords = macro.Keywords,
                    KeywordsPreview = string.Join(", ", macro.Keywords.Take(3))
                });
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(query))
            {
                LoadAllMacros();
                HintText.Text = "Press Enter to execute";
                return;
            }

            // Filter and sort by relevance
            var allMacros = AgentMacroEngine.Instance.GetMacroSummaries();
            var filtered = allMacros
                .Select(m => new
                {
                    Macro = m,
                    Score = CalculateMatchScore(m, query)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => new MacroSuggestion
                {
                    Id = x.Macro.Id,
                    Title = x.Macro.Title,
                    Description = x.Macro.Description,
                    Icon = x.Macro.Icon,
                    Keywords = x.Macro.Keywords,
                    KeywordsPreview = string.Join(", ", x.Macro.Keywords.Take(3))
                })
                .ToList();

            _suggestions.Clear();
            foreach (var item in filtered)
                _suggestions.Add(item);

            _selectedIndex = filtered.Any() ? 0 : -1;
            UpdateSelection();

            HintText.Text = filtered.Any()
                ? $"Press Enter to run '{filtered.First().Title}'"
                : "No macro match - Enter to send to chat";
        }

        private int CalculateMatchScore(MacroSummary macro, string query)
        {
            int score = 0;

            // Title match
            if (macro.Title.ToLowerInvariant().Contains(query))
                score += 100;

            // Keyword matches
            foreach (var keyword in macro.Keywords)
            {
                if (keyword.ToLowerInvariant().Contains(query))
                    score += 50;
                if (query.Contains(keyword.ToLowerInvariant()))
                    score += 30;
            }

            // Description match
            if (macro.Description.ToLowerInvariant().Contains(query))
                score += 20;

            return score;
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    ExecuteSelected();
                    e.Handled = true;
                    break;

                case Key.Down:
                    if (_selectedIndex < _suggestions.Count - 1)
                    {
                        _selectedIndex++;
                        UpdateSelection();
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                        UpdateSelection();
                    }
                    e.Handled = true;
                    break;

                case Key.Escape:
                    SearchBox.Text = "";
                    break;
            }
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < _suggestions.Count; i++)
            {
                _suggestions[i].IsSelected = (i == _selectedIndex);
            }
        }

        private async void ExecuteSelected()
        {
            var query = SearchBox.Text.Trim();

            if (_selectedIndex >= 0 && _selectedIndex < _suggestions.Count)
            {
                // Execute the selected macro
                var selected = _suggestions[_selectedIndex];
                var result = await AgentMacroEngine.Instance.ExecuteByIdAsync(selected.Id);
                if (result != null)
                {
                    MacroExecuted?.Invoke(this, result);
                }
                SearchBox.Text = "";
            }
            else if (!string.IsNullOrEmpty(query))
            {
                // No macro match - fall back to chat
                FallbackToChat?.Invoke(this, query);
                SearchBox.Text = "";
            }
        }

        private void Suggestion_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is MacroSuggestion suggestion)
            {
                _selectedIndex = _suggestions.IndexOf(suggestion);
                UpdateSelection();
                ExecuteSelected();
            }
        }

        private void Suggestion_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x20, 0x22, 0xd3, 0xee));
            }
        }

        private void Suggestion_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.DataContext is MacroSuggestion suggestion)
            {
                border.Background = suggestion.IsSelected
                    ? new SolidColorBrush(Color.FromArgb(0x20, 0x22, 0xd3, 0xee))
                    : Brushes.Transparent;
            }
        }

        public void Focus()
        {
            SearchBox.Focus();
        }

        public class MacroSuggestion : System.ComponentModel.INotifyPropertyChanged
        {
            private bool _isSelected;

            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public string Icon { get; set; } = "";
            public string[] Keywords { get; set; } = Array.Empty<string>();
            public string KeywordsPreview { get; set; } = "";

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
