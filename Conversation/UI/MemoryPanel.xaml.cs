using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AtlasAI.Conversation.Models;
using AtlasAI.Conversation.Services;

namespace AtlasAI.Conversation.UI
{
    public partial class MemoryPanel : UserControl
    {
        private ConversationManager? _conversationManager;

        public MemoryPanel()
        {
            InitializeComponent();
        }

        public void Initialize(ConversationManager conversationManager)
        {
            _conversationManager = conversationManager;
            RefreshMemory();
            
            // Set toggle state
            var profile = _conversationManager.UserProfile;
            MemoryToggle.IsChecked = profile?.AllowMemory ?? true;
        }

        public void RefreshMemory()
        {
            if (_conversationManager == null) return;

            MemoryItemsContainer.Children.Clear();

            var memories = _conversationManager.Memories;

            if (memories.Count == 0)
            {
                var emptyState = new TextBlock
                {
                    Text = "No memories yet.\n\nSay \"remember this\" or \"remember that I prefer...\" to save information.",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16, 40, 16, 0)
                };
                MemoryItemsContainer.Children.Add(emptyState);
                return;
            }

            foreach (var memory in memories)
            {
                var item = CreateMemoryItem(memory);
                MemoryItemsContainer.Children.Add(item);
            }
        }

        private Border CreateMemoryItem(MemoryItem memory)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12),
                Tag = memory.Id
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Content
            var contentStack = new StackPanel();

            // Category badge
            var categoryBadge = new Border
            {
                Background = GetCategoryColor(memory.Category),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6)
            };
            categoryBadge.Child = new TextBlock
            {
                Text = memory.Category.ToString(),
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.White)
            };
            contentStack.Children.Add(categoryBadge);

            // Memory content
            var content = new TextBlock
            {
                Text = memory.Content,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                TextWrapping = TextWrapping.Wrap
            };
            contentStack.Children.Add(content);

            // Metadata
            var meta = new TextBlock
            {
                Text = $"Added {FormatDate(memory.CreatedAt)} • Used {memory.UseCount} times",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 118, 129)),
                Margin = new Thickness(0, 6, 0, 0)
            };
            contentStack.Children.Add(meta);

            Grid.SetColumn(contentStack, 0);
            grid.Children.Add(contentStack);

            // Delete button
            var deleteBtn = new Button
            {
                Content = "🗑",
                FontSize = 14,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Delete this memory",
                Tag = memory.Id,
                VerticalAlignment = VerticalAlignment.Top
            };
            deleteBtn.Click += DeleteMemory_Click;
            Grid.SetColumn(deleteBtn, 1);
            grid.Children.Add(deleteBtn);

            border.Child = grid;

            // Hover effect
            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(33, 38, 45));
            border.MouseLeave += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));

            return border;
        }

        private Brush GetCategoryColor(MemoryCategory category)
        {
            return category switch
            {
                MemoryCategory.Preference => new SolidColorBrush(Color.FromRgb(0, 212, 170)),
                MemoryCategory.Project => new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                MemoryCategory.PersonalInfo => new SolidColorBrush(Color.FromRgb(210, 153, 34)),
                MemoryCategory.Workflow => new SolidColorBrush(Color.FromRgb(163, 113, 247)),
                MemoryCategory.Technical => new SolidColorBrush(Color.FromRgb(248, 81, 73)),
                _ => new SolidColorBrush(Color.FromRgb(110, 118, 129))
            };
        }

        private string FormatDate(DateTime date)
        {
            var diff = DateTime.Now - date;
            if (diff.TotalDays < 1) return "today";
            if (diff.TotalDays < 2) return "yesterday";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
            return date.ToString("MMM d");
        }

        private async void MemoryToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_conversationManager == null) return;

            var isEnabled = MemoryToggle.IsChecked == true;
            await _conversationManager.UpdateProfileAsync(p => p.AllowMemory = isEnabled);
        }

        private async void DeleteMemory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string memoryId && _conversationManager != null)
            {
                await _conversationManager.ForgetAsync(memoryId);
                RefreshMemory();
            }
        }

        private void AddMemory_Click(object sender, RoutedEventArgs e)
        {
            // Show add memory dialog
            var dialog = new AddMemoryDialog();
            dialog.Owner = Window.GetWindow(this);
            
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.MemoryContent))
            {
                _ = AddMemoryAsync(dialog.MemoryContent, dialog.SelectedCategory);
            }
        }

        private async System.Threading.Tasks.Task AddMemoryAsync(string content, MemoryCategory category)
        {
            if (_conversationManager == null) return;
            
            await _conversationManager.RememberAsync(content, category);
            RefreshMemory();
        }

        private async void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all memories?\n\nThis cannot be undone.",
                "Clear All Memories",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes && _conversationManager != null)
            {
                await _conversationManager.ClearAllMemoriesAsync();
                RefreshMemory();
            }
        }
    }

    /// <summary>
    /// Simple dialog for adding a memory
    /// </summary>
    public class AddMemoryDialog : Window
    {
        public string MemoryContent { get; private set; } = "";
        public MemoryCategory SelectedCategory { get; private set; } = MemoryCategory.General;

        private TextBox _contentBox;
        private ComboBox _categoryBox;

        public AddMemoryDialog()
        {
            Title = "Add Memory";
            Width = 400;
            Height = 250;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel();

            // Title
            stack.Children.Add(new TextBlock
            {
                Text = "Add Memory",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Content input
            stack.Children.Add(new TextBlock
            {
                Text = "What should Atlas remember?",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            _contentBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 60
            };
            stack.Children.Add(_contentBox);

            // Category selector
            stack.Children.Add(new TextBlock
            {
                Text = "Category",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                Margin = new Thickness(0, 12, 0, 6)
            });

            _categoryBox = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                Padding = new Thickness(10, 8, 10, 8)
            };
            foreach (MemoryCategory cat in Enum.GetValues(typeof(MemoryCategory)))
            {
                _categoryBox.Items.Add(cat.ToString());
            }
            _categoryBox.SelectedIndex = 0;
            stack.Children.Add(_categoryBox);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(16, 8, 16, 8),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelBtn);

            var saveBtn = new Button
            {
                Content = "Save",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromRgb(0, 212, 170)),
                Foreground = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            saveBtn.Click += (s, e) =>
            {
                MemoryContent = _contentBox.Text;
                if (Enum.TryParse<MemoryCategory>(_categoryBox.SelectedItem?.ToString(), out var cat))
                    SelectedCategory = cat;
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(saveBtn);

            stack.Children.Add(buttonPanel);
            border.Child = stack;
            Content = border;
        }
    }
}
