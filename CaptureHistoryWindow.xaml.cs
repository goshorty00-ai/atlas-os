using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AtlasAI; // For ThemeManager
using AtlasAI.ScreenCapture;

namespace AtlasAI {
    public partial class CaptureHistoryWindow : Window
    {
        private CaptureHistoryManager _historyManager;
        private List<CaptureHistoryItem> _currentItems = new();
        private CaptureHistoryItem? _selectedItem;
        private string _currentFilter = "all";

        public CaptureHistoryWindow()
        {
            InitializeComponent();
            
            _historyManager = new CaptureHistoryManager();
            _historyManager.ItemAdded += OnItemAdded;
            _historyManager.ItemUpdated += OnItemUpdated;
            _historyManager.ItemDeleted += OnItemDeleted;
            _historyManager.HistoryCleared += OnHistoryCleared;
            
            Loaded += CaptureHistoryWindow_Loaded;
            
            // Apply theme
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            Background = ThemeManager.BrushBackground;
        }

        private async void CaptureHistoryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshHistoryAsync();
            UpdateStatusBar();
        }

        private async Task RefreshHistoryAsync()
        {
            try
            {
                _currentItems = _currentFilter switch
                {
                    "favorites" => _historyManager.GetFavorites(),
                    "recent" => _historyManager.GetRecentItems(20),
                    _ => _historyManager.GetAllItems()
                };

                await UpdateHistoryDisplayAsync();
                UpdateItemCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing history: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateHistoryDisplayAsync()
        {
            HistoryPanel.Children.Clear();

            if (_currentItems.Count == 0)
            {
                var emptyMessage = new TextBlock
                {
                    Text = "📷 No screenshots found.\nTake your first screenshot using the 📸 button or Ctrl+Shift+S!",
                    Foreground = Brushes.Gray,
                    FontSize = 16,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                HistoryPanel.Children.Add(emptyMessage);
                return;
            }

            foreach (var item in _currentItems)
            {
                var itemControl = await CreateHistoryItemControlAsync(item);
                HistoryPanel.Children.Add(itemControl);
            }
        }

        private async Task<Border> CreateHistoryItemControlAsync(CaptureHistoryItem item)
        {
            var border = new Border
            {
                Tag = item
            };
            
            // Try to find the style, use default if not found
            if (TryFindResource("HistoryItemStyle") is Style historyStyle)
            {
                border.Style = historyStyle;
            }
            else
            {
                // Fallback styling
                border.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                border.CornerRadius = new CornerRadius(8);
                border.Padding = new Thickness(12);
                border.Margin = new Thickness(0, 0, 0, 8);
                border.Cursor = Cursors.Hand;
            }

            border.MouseLeftButtonUp += (s, e) => OnItemClicked(item);
            border.MouseRightButtonUp += (s, e) => ShowContextMenu(item, e);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Thumbnail
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions

            // Thumbnail
            var thumbnailBorder = new Border
            {
                Width = 100,
                Height = 75,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 12, 0)
            };

            try
            {
                if (File.Exists(item.ThumbnailPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(item.ThumbnailPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    thumbnailBorder.Background = new ImageBrush(bitmap)
                    {
                        Stretch = Stretch.UniformToFill
                    };
                }
                else
                {
                    // Fallback icon
                    var icon = new TextBlock
                    {
                        Text = "📷",
                        FontSize = 24,
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    thumbnailBorder.Child = icon;
                }
            }
            catch
            {
                // Fallback for broken thumbnails
                var icon = new TextBlock
                {
                    Text = "❌",
                    FontSize = 20,
                    Foreground = Brushes.Red,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                thumbnailBorder.Child = icon;
            }

            Grid.SetColumn(thumbnailBorder, 0);
            grid.Children.Add(thumbnailBorder);

            // Content
            var contentStack = new StackPanel
            {
                Margin = new Thickness(0, 0, 12, 0)
            };

            // Title with favorite indicator
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            
            if (item.IsFavorite)
            {
                titlePanel.Children.Add(new TextBlock
                {
                    Text = "⭐ ",
                    Foreground = Brushes.Gold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            titlePanel.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14
            });

            contentStack.Children.Add(titlePanel);

            // Capture time
            contentStack.Children.Add(new TextBlock
            {
                Text = $"📅 {item.CaptureTime:MMM dd, yyyy 'at' h:mm tt}",
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });

            // File info
            var fileSize = item.FileSizeBytes / 1024.0; // KB
            var fileSizeText = fileSize > 1024 ? $"{fileSize / 1024:F1} MB" : $"{fileSize:F0} KB";
            
            contentStack.Children.Add(new TextBlock
            {
                Text = $"💾 {fileSizeText} • {item.Metadata.Resolution.Width}x{item.Metadata.Resolution.Height}",
                Foreground = Brushes.Gray,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0)
            });

            // Description or extracted text preview
            var previewText = !string.IsNullOrEmpty(item.Description) ? item.Description :
                             !string.IsNullOrEmpty(item.ExtractedText) ? item.ExtractedText.Substring(0, Math.Min(100, item.ExtractedText.Length)) + "..." :
                             "No description available";

            if (previewText.Length > 150)
                previewText = previewText.Substring(0, 150) + "...";

            contentStack.Children.Add(new TextBlock
            {
                Text = previewText,
                Foreground = Brushes.LightGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                MaxHeight = 40
            });

            // Tags
            if (item.Tags.Any())
            {
                var tagsPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var tag in item.Tags.Take(3))
                {
                    var tagBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                        CornerRadius = new CornerRadius(10),
                        Padding = new System.Windows.Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 4, 2)
                    };
                    
                    tagBorder.Child = new TextBlock
                    {
                        Text = tag,
                        Foreground = Brushes.White,
                        FontSize = 9
                    };
                    
                    tagsPanel.Children.Add(tagBorder);
                }
                contentStack.Children.Add(tagsPanel);
            }

            Grid.SetColumn(contentStack, 1);
            grid.Children.Add(contentStack);

            // Action buttons
            var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Helper to get button style safely
            Style? GetButtonStyle()
            {
                if (TryFindResource("ModernButton") is Style s) return s;
                return null;
            }
            var btnStyle = GetButtonStyle();

            var openButton = new Button
            {
                Content = "👁️",
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Open image",
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            if (btnStyle != null) openButton.Style = btnStyle;
            openButton.Click += (s, e) => OpenImage(item);

            var analyzeButton = new Button
            {
                Content = "🔍",
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "AI Analysis",
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            if (btnStyle != null) analyzeButton.Style = btnStyle;
            analyzeButton.Click += (s, e) => AnalyzeImage(item);

            var favoriteButton = new Button
            {
                Content = item.IsFavorite ? "⭐" : "☆",
                Width = 30,
                Height = 30,
                ToolTip = item.IsFavorite ? "Remove from favorites" : "Add to favorites",
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            if (btnStyle != null) favoriteButton.Style = btnStyle;
            favoriteButton.Click += async (s, e) => await ToggleFavorite(item);

            actionsPanel.Children.Add(openButton);
            actionsPanel.Children.Add(analyzeButton);
            actionsPanel.Children.Add(favoriteButton);

            Grid.SetColumn(actionsPanel, 2);
            grid.Children.Add(actionsPanel);

            border.Child = grid;
            return border;
        }

        private void OnItemClicked(CaptureHistoryItem item)
        {
            _selectedItem = item;
            OpenImage(item);
        }

        private void ShowContextMenu(CaptureHistoryItem item, MouseButtonEventArgs e)
        {
            _selectedItem = item;
            if (ItemContextMenu != null)
            {
                ItemContextMenu.IsOpen = true;
            }
            e.Handled = true;
        }

        private void OpenImage(CaptureHistoryItem item)
        {
            try
            {
                if (File.Exists(item.FilePath))
                {
                    Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("Image file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AnalyzeImage(CaptureHistoryItem item)
        {
            try
            {
                // This would integrate with the existing AI analysis system
                MessageBox.Show($"AI Analysis for: {item.Title}\n\nThis feature will integrate with the existing AI analysis system to provide detailed insights about the screenshot.", 
                    "AI Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error analyzing image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ToggleFavorite(CaptureHistoryItem item)
        {
            try
            {
                await _historyManager.ToggleFavoriteAsync(item.Id);
                await RefreshHistoryAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error toggling favorite: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateItemCount()
        {
            ItemCountText.Text = $" ({_currentItems.Count} items)";
        }

        private void UpdateStatusBar()
        {
            var totalSize = _historyManager.GetTotalStorageSize();
            var sizeText = totalSize > 1024 * 1024 ? $"{totalSize / (1024.0 * 1024):F1} MB" : $"{totalSize / 1024.0:F0} KB";
            StorageInfoText.Text = $"Storage: {sizeText}";

            var recentItems = _historyManager.GetRecentItems(1);
            if (recentItems.Any())
            {
                LastCaptureText.Text = $"Last capture: {recentItems[0].CaptureTime:MMM dd 'at' h:mm tt}";
            }
            else
            {
                LastCaptureText.Text = "Last capture: Never";
            }
        }

        // Event handlers
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshHistoryAsync();
            UpdateStatusBar();
        }

        private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all screenshot history?\n\nThis will delete all screenshots and cannot be undone.",
                "Clear History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await _historyManager.ClearHistoryAsync(true);
            }
        }

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_historyManager == null) return;
            
            if (SearchBox == null || SearchBox.Text == "Search screenshots..." || string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                _currentItems = _historyManager.GetAllItems();
            }
            else
            {
                _currentItems = _historyManager.SearchItems(SearchBox.Text);
            }

            await UpdateHistoryDisplayAsync();
            UpdateItemCount();
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search screenshots...")
            {
                SearchBox.Text = "";
                SearchBox.Foreground = Brushes.White;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Search screenshots...";
                SearchBox.Foreground = Brushes.Gray;
            }
        }

        private async void FavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = "favorites";
            await RefreshHistoryAsync();
        }

        private async void RecentButton_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = "recent";
            await RefreshHistoryAsync();
        }

        private async void AllButton_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = "all";
            await RefreshHistoryAsync();
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var capturesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "Captures");

                if (Directory.Exists(capturesPath))
                {
                    Process.Start("explorer.exe", capturesPath);
                }
                else
                {
                    MessageBox.Show("Captures folder not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Context menu handlers
        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
                OpenImage(_selectedItem);
        }

        private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                Clipboard.SetText(_selectedItem.FilePath);
                MessageBox.Show("File path copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AnalyzeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
                AnalyzeImage(_selectedItem);
        }

        private async void FavoriteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
                await ToggleFavorite(_selectedItem);
        }

        private async void ArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                await _historyManager.ArchiveItemAsync(_selectedItem.Id);
                await RefreshHistoryAsync();
            }
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete this screenshot?\n\n{_selectedItem.Title}",
                    "Delete Screenshot",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    await _historyManager.DeleteItemAsync(_selectedItem.Id);
                }
            }
        }

        // History manager event handlers
        private void OnItemAdded(CaptureHistoryItem item)
        {
            Dispatcher.Invoke(async () =>
            {
                await RefreshHistoryAsync();
                UpdateStatusBar();
            });
        }

        private void OnItemUpdated(CaptureHistoryItem item)
        {
            Dispatcher.Invoke(async () =>
            {
                await RefreshHistoryAsync();
            });
        }

        private void OnItemDeleted(string itemId)
        {
            Dispatcher.Invoke(async () =>
            {
                await RefreshHistoryAsync();
                UpdateStatusBar();
            });
        }

        private void OnHistoryCleared()
        {
            Dispatcher.Invoke(async () =>
            {
                await RefreshHistoryAsync();
                UpdateStatusBar();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _historyManager.ItemAdded -= OnItemAdded;
            _historyManager.ItemUpdated -= OnItemUpdated;
            _historyManager.ItemDeleted -= OnItemDeleted;
            _historyManager.HistoryCleared -= OnHistoryCleared;
            base.OnClosed(e);
        }
    }
}