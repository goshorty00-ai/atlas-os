using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AtlasAI; // For ClipboardItem

namespace AtlasAI {
    public partial class ClipboardWindow : Window
    {
        public ClipboardWindow()
        {
            InitializeComponent();
            
            // Initialize clipboard manager
            ClipboardManager.Initialize();
            ClipboardManager.ClipboardChanged += OnClipboardChanged;
            
            // Setup search box placeholder behavior
            SearchBox.GotFocus += SearchBox_GotFocus;
            SearchBox.LostFocus += SearchBox_LostFocus;
            SearchBox.Foreground = Brushes.Gray;
            
            LoadClipboardItems();
        }

        private void OnClipboardChanged(ClipboardItem item)
        {
            Dispatcher.Invoke(() => LoadClipboardItems());
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search clipboard history...")
            {
                SearchBox.Text = "";
                SearchBox.Foreground = Brushes.White;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Search clipboard history...";
                SearchBox.Foreground = Brushes.Gray;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchBox.Text != "Search clipboard history..." && SearchBox.Foreground == Brushes.White)
            {
                LoadClipboardItems(SearchBox.Text);
            }
        }

        private void LoadClipboardItems(string searchQuery = "")
        {
            ClipboardItemsPanel.Children.Clear();
            
            var items = string.IsNullOrWhiteSpace(searchQuery) || searchQuery == "Search clipboard history..."
                ? ClipboardManager.GetHistory()
                : ClipboardManager.SearchHistory(searchQuery);

            ItemCountLabel.Text = $"{items.Count} items";

            if (items.Count == 0)
            {
                var emptyLabel = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(searchQuery) || searchQuery == "Search clipboard history..."
                        ? "No clipboard items yet. Copy some text to get started!"
                        : "No items found matching your search.",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(20)
                };
                ClipboardItemsPanel.Children.Add(emptyLabel);
                return;
            }

            foreach (var item in items)
            {
                CreateClipboardItemUI(item);
            }
        }

        private void CreateClipboardItemUI(ClipboardItem item)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Content preview
            var contentBlock = new TextBlock
            {
                Text = item.Content,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                MaxHeight = 100
            };
            Grid.SetRow(contentBlock, 0);
            grid.Children.Add(contentBlock);

            // Timestamp and actions
            var bottomGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var timestampBlock = new TextBlock
            {
                Text = item.Timestamp.ToString("MMM dd, HH:mm"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timestampBlock, 0);
            bottomGrid.Children.Add(timestampBlock);

            var copyButton = new Button
            {
                Content = "📋 Copy",
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            copyButton.Click += (s, e) => CopyItem(item);
            Grid.SetColumn(copyButton, 1);
            bottomGrid.Children.Add(copyButton);

            Grid.SetRow(bottomGrid, 1);
            grid.Children.Add(bottomGrid);

            border.Child = grid;

            // Double-click to copy
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    CopyItem(item);
                }
            };

            // Hover effect
            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            border.MouseLeave += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));

            ClipboardItemsPanel.Children.Add(border);
        }

        private void CopyItem(ClipboardItem item)
        {
            ClipboardManager.CopyToClipboard(item.Content);
            StatusLabel.Text = $"Copied: {item.Preview}";
            
            // Reset status after 3 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, e) =>
            {
                StatusLabel.Text = "Clipboard monitoring active";
                timer.Stop();
            };
            timer.Start();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all clipboard history?",
                "Clear Clipboard History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ClipboardManager.ClearHistory();
                LoadClipboardItems();
                StatusLabel.Text = "Clipboard history cleared";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            ClipboardManager.ClipboardChanged -= OnClipboardChanged;
            base.OnClosed(e);
        }
    }
}