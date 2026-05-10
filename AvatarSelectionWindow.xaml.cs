using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AtlasAI.Avatar;

namespace AtlasAI {
    public partial class AvatarSelectionWindow : Window
    {
        private ReadyPlayerMeManager _avatarManager;
        private string? _selectedAvatar;
        private List<AvatarInfo> _avatars = new();

        public string? SelectedAvatar => _selectedAvatar;

        public AvatarSelectionWindow()
        {
            InitializeComponent();
            _avatarManager = new ReadyPlayerMeManager();
            _avatarManager.LoadAvatarConfig();
            LoadAvatars();
        }

        private async void LoadAvatars()
        {
            _avatars.Clear();
            
            // Add default avatars
            _avatars.Add(new AvatarInfo
            {
                Name = "default",
                DisplayName = "Default Atlas",
                Description = "Classic AI assistant look",
                Emoji = "🤖",
                Status = "✅ Ready"
            });
            
            _avatars.Add(new AvatarInfo
            {
                Name = "casual",
                DisplayName = "Casual Style",
                Description = "Relaxed and friendly",
                Emoji = "😊",
                Status = "🔄 Loading..."
            });
            
            _avatars.Add(new AvatarInfo
            {
                Name = "business",
                DisplayName = "Professional",
                Description = "Business ready avatar",
                Emoji = "👔",
                Status = "🔄 Loading..."
            });
            
            _avatars.Add(new AvatarInfo
            {
                Name = "gaming",
                DisplayName = "Gaming Style",
                Description = "Perfect for gaming sessions",
                Emoji = "🎮",
                Status = "🔄 Loading..."
            });

            // Load any custom avatars
            foreach (var avatarName in _avatarManager.GetAvatarNames())
            {
                if (!_avatars.Any(a => a.Name == avatarName))
                {
                    _avatars.Add(new AvatarInfo
                    {
                        Name = avatarName,
                        DisplayName = avatarName.Substring(0, 1).ToUpper() + avatarName.Substring(1),
                        Description = "Custom Ready Player Me avatar",
                        Emoji = "🎭",
                        Status = "🔄 Loading..."
                    });
                }
            }

            AvatarGrid.ItemsSource = _avatars;

            // Load thumbnails in background
            await LoadAvatarThumbnails();
        }

        private async System.Threading.Tasks.Task LoadAvatarThumbnails()
        {
            foreach (var avatar in _avatars.Skip(1)) // Skip default
            {
                try
                {
                    var thumbnail = await _avatarManager.GetAvatarThumbnailAsync(avatar.Name);
                    if (thumbnail != null)
                    {
                        avatar.Status = "✅ Ready";
                        // Update UI - find the corresponding visual element and update thumbnail
                        // This would require more complex data binding or manual UI updates
                    }
                    else
                    {
                        avatar.Status = "❌ Failed";
                    }
                }
                catch
                {
                    avatar.Status = "❌ Error";
                }
            }
            
            // Refresh the UI
            AvatarGrid.ItemsSource = null;
            AvatarGrid.ItemsSource = _avatars;
        }

        private void Avatar_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string avatarName)
            {
                _selectedAvatar = avatarName;
                
                // Update visual selection
                foreach (Border item in FindVisualChildren<Border>(AvatarGrid))
                {
                    if (item.Tag is string tag)
                    {
                        item.BorderBrush = tag == avatarName 
                            ? new SolidColorBrush(Color.FromRgb(99, 102, 241))
                            : Brushes.Transparent;
                    }
                }
                
                SelectButton.IsEnabled = true;
            }
        }

        private void AddAvatar_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddAvatarDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.AvatarUrl))
            {
                var name = dialog.AvatarName ?? $"avatar_{DateTime.Now:yyyyMMdd_HHmmss}";
                _avatarManager.AddAvatar(name, dialog.AvatarUrl);
                LoadAvatars();
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadAvatars();
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Helper method to find visual children
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }
    }

    public class AvatarInfo
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Emoji { get; set; } = "👤";
        public string Status { get; set; } = "Loading...";
    }
}