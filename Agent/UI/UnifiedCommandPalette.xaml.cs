using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AtlasAI.Agent.UI
{
    /// <summary>
    /// Unified Command Palette - Spotlight-style search for all Atlas commands
    /// Toggle with Ctrl+Space (configurable)
    /// </summary>
    public partial class UnifiedCommandPalette : Window
    {
        private readonly ObservableCollection<CommandEntryViewModel> _results = new();
        private int _selectedIndex = -1;
        private bool _isClosing = false;
        private bool _isExecuting = false;

        public event EventHandler<CommandExecutionResult>? CommandExecuted;
        public event EventHandler<string>? NavigationRequested;
        public event EventHandler<string>? FallbackToChat;

        public UnifiedCommandPalette()
        {
            // Add converter
            Resources.Add("BoolToVis", new BooleanToVisibilityConverter());
            
            InitializeComponent();
            ResultsList.ItemsSource = _results;
            UpdateAgentModeIndicator();

            AgentModeManager.AgentModeChanged += (s, e) => Dispatcher.Invoke(UpdateAgentModeIndicator);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Play fade in animation
            var fadeIn = (Storyboard)FindResource("FadeIn");
            fadeIn.Begin(this);

            // Load suggested commands
            LoadSuggested();
            SearchBox.Focus();
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

        private void LoadSuggested()
        {
            _results.Clear();
            var suggested = CommandIndexService.Instance.GetSuggested(12);
            
            foreach (var cmd in suggested)
            {
                _results.Add(new CommandEntryViewModel(cmd));
            }

            _selectedIndex = _results.Count > 0 ? 0 : -1;
            UpdateSelection();
            UpdateResultCount();
            HintText.Text = "Suggested • ↑↓ navigate • Enter execute";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                LoadSuggested();
                return;
            }

            // Search and update results
            var results = CommandIndexService.Instance.Search(query, 15);
            
            _results.Clear();
            foreach (var cmd in results)
            {
                _results.Add(new CommandEntryViewModel(cmd));
            }

            _selectedIndex = _results.Count > 0 ? 0 : -1;
            UpdateSelection();
            UpdateResultCount();

            // Update hint based on results
            if (_results.Count > 0)
            {
                var first = _results[0];
                HintText.Text = $"Enter to run '{first.DisplayText}'";
            }
            else
            {
                HintText.Text = "No matches • Enter to send to chat";
            }
        }

        private void UpdateResultCount()
        {
            ResultCountText.Text = _results.Count > 0 ? $"{_results.Count} results" : "";
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    ExecuteSelected();
                    e.Handled = true;
                    break;

                case Key.Down:
                    if (_selectedIndex < _results.Count - 1)
                    {
                        _selectedIndex++;
                        UpdateSelection();
                        ScrollToSelected();
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                        UpdateSelection();
                        ScrollToSelected();
                    }
                    e.Handled = true;
                    break;

                case Key.Escape:
                    CloseWithAnimation();
                    e.Handled = true;
                    break;

                case Key.Tab:
                    // Tab cycles through results
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        if (_selectedIndex > 0) _selectedIndex--;
                        else _selectedIndex = _results.Count - 1;
                    }
                    else
                    {
                        if (_selectedIndex < _results.Count - 1) _selectedIndex++;
                        else _selectedIndex = 0;
                    }
                    UpdateSelection();
                    ScrollToSelected();
                    e.Handled = true;
                    break;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseWithAnimation();
                e.Handled = true;
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (!_isClosing && !_isExecuting)
            {
                CloseWithAnimation();
            }
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < _results.Count; i++)
            {
                _results[i].IsSelected = (i == _selectedIndex);
            }
        }

        private void ScrollToSelected()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _results.Count)
            {
                // Find the container and scroll to it
                var container = ResultsList.ItemContainerGenerator.ContainerFromIndex(_selectedIndex) as FrameworkElement;
                container?.BringIntoView();
            }
        }

        private async void ExecuteSelected()
        {
            if (_isExecuting) return;

            var query = SearchBox.Text.Trim();

            if (_selectedIndex >= 0 && _selectedIndex < _results.Count)
            {
                var selected = _results[_selectedIndex];
                await ExecuteCommandAsync(selected.Entry);
            }
            else if (!string.IsNullOrEmpty(query))
            {
                // No match - fall back to chat
                FallbackToChat?.Invoke(this, query);
                CloseWithAnimation();
            }
        }

        private async Task ExecuteCommandAsync(CommandEntry command)
        {
            _isExecuting = true;

            // Show execution status
            ShowExecutionStatus(command);

            try
            {
                var result = await CommandIndexService.Instance.ExecuteAsync(command);

                if (result.Success)
                {
                    // Handle navigation
                    if (command.Type == CommandType.Navigation)
                    {
                        NavigationRequested?.Invoke(this, result.NavigationTarget ?? command.Id);
                    }

                    CommandExecuted?.Invoke(this, result);
                    
                    // Show success briefly then close
                    ShowSuccessStatus(result);
                    await Task.Delay(300);
                    CloseWithAnimation();
                }
                else
                {
                    ShowErrorStatus(result.Message ?? "Execution failed");
                    await Task.Delay(1500);
                    HideExecutionStatus();
                }
            }
            catch (Exception ex)
            {
                ShowErrorStatus(ex.Message);
                await Task.Delay(1500);
                HideExecutionStatus();
            }
            finally
            {
                _isExecuting = false;
            }
        }

        private void ShowExecutionStatus(CommandEntry command)
        {
            StatusFooter.Visibility = Visibility.Visible;
            StatusIcon.Background = new SolidColorBrush(Color.FromRgb(0x22, 0xd3, 0xee));
            StatusIconText.Text = "⚡";
            StatusText.Text = $"Executing {command.DisplayText}...";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
            StatusDuration.Text = "";
        }

        private void ShowSuccessStatus(CommandExecutionResult result)
        {
            StatusIcon.Background = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
            StatusIconText.Text = "✓";
            StatusText.Text = result.Message ?? "Completed";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
            StatusDuration.Text = $"{result.ExecutionTime.TotalMilliseconds:F0}ms";
        }

        private void ShowErrorStatus(string message)
        {
            StatusIcon.Background = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
            StatusIconText.Text = "✕";
            StatusText.Text = message;
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
            StatusDuration.Text = "";
        }

        private void HideExecutionStatus()
        {
            StatusFooter.Visibility = Visibility.Collapsed;
        }

        private void Result_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is CommandEntryViewModel vm)
            {
                _selectedIndex = _results.IndexOf(vm);
                UpdateSelection();
                ExecuteSelected();
            }
        }

        private void Result_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0x22, 0xd3, 0xee));
            }
        }

        private void Result_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.DataContext is CommandEntryViewModel vm)
            {
                border.Background = vm.IsSelected
                    ? new SolidColorBrush(Color.FromArgb(0x20, 0x22, 0xd3, 0xee))
                    : Brushes.Transparent;
            }
        }

        private void CloseWithAnimation()
        {
            if (_isClosing) return;
            _isClosing = true;

            var fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Completed += (s, e) => Close();
            fadeOut.Begin(this);
        }

        /// <summary>
        /// Show the palette (static helper)
        /// </summary>
        public static UnifiedCommandPalette ShowPalette(Window? owner = null)
        {
            var palette = new UnifiedCommandPalette();
            if (owner != null)
            {
                palette.Owner = owner;
                palette.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            palette.Show();
            return palette;
        }
    }

    /// <summary>
    /// ViewModel wrapper for CommandEntry with selection state
    /// </summary>
    public class CommandEntryViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        public CommandEntry Entry { get; }

        public string Id => Entry.Id;
        public CommandType Type => Entry.Type;
        public string DisplayText => Entry.DisplayText;
        public string Description => Entry.Description;
        public string Icon => Entry.Icon;
        public string TypeBadge => Entry.TypeBadge;
        public string TypeColor => Entry.TypeColor;
        public bool IsRecent => Entry.IsRecent;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public CommandEntryViewModel(CommandEntry entry)
        {
            Entry = entry;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
