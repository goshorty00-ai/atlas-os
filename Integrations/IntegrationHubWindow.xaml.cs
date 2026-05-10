using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AtlasAI.Integrations
{
    public partial class IntegrationHubWindow : Window
    {
        public IntegrationHubWindow()
        {
            InitializeComponent();
            LoadIntegrations();
        }

        private void LoadIntegrations()
        {
            IntegrationRegistry.Initialize();
            IntegrationRegistry.Refresh();
            
            var summary = IntegrationRegistry.GetSummary();
            SummaryText.Text = $"✅ {summary.ConfiguredCount} configured • 📦 {summary.AvailableCount} available • 🔜 {summary.ComingSoonCount} coming soon";
            
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var integrations = IntegrationRegistry.GetAll();
            
            // Category filter
            if (CategoryFilter.SelectedItem is ComboBoxItem categoryItem && categoryItem.Tag?.ToString() != "all")
            {
                var categoryStr = categoryItem.Tag?.ToString();
                if (Enum.TryParse<IntegrationCategory>(categoryStr, out var category))
                {
                    integrations = integrations.Where(i => i.Category == category).ToList();
                }
            }
            
            // Configured only filter
            if (ShowConfiguredOnly.IsChecked == true)
            {
                integrations = integrations.Where(i => i.IsConfigured).ToList();
            }
            
            // Coming soon filter
            if (ShowComingSoon.IsChecked != true)
            {
                integrations = integrations.Where(i => i.Status != IntegrationStatus.ComingSoon).ToList();
            }
            
            // Convert to view models
            var viewModels = integrations.Select(i => new IntegrationViewModel(i)).ToList();
            IntegrationsList.ItemsSource = viewModels;
        }

        private static string? PromptForApiKey(string title, string labelText)
        {
            var window = new Window
            {
                Title = title,
                Width = 520,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(18, 18, 26)),
                Foreground = Brushes.White
            };

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = labelText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14
            };
            Grid.SetRow(label, 0);
            root.Children.Add(label);

            var box = new PasswordBox
            {
                Height = 32,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(box, 1);
            root.Children.Add(box);

            var hint = new TextBlock
            {
                Text = "Stored locally (Windows DPAPI, current user).",
                Opacity = 0.75,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(hint, 2);
            root.Children.Add(hint);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancel = new Button
            {
                Content = "Cancel",
                MinWidth = 90,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(14, 8, 14, 8)
            };

            var ok = new Button
            {
                Content = "Save",
                MinWidth = 90,
                Padding = new Thickness(14, 8, 14, 8),
                IsDefault = true
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            window.Content = root;

            string? result = null;
            cancel.Click += (_, _) => window.Close();
            ok.Click += (_, _) =>
            {
                var value = (box.Password ?? "").Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    MessageBox.Show(window, "Please paste a token/key.", "Missing Value", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = value;
                window.Close();
            };

            window.ShowDialog();
            return result;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadIntegrations();
        }

        private void CategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilters();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) ApplyFilters();
        }

        private void Configure_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string integrationId)
            {
                var integration = IntegrationRegistry.GetById(integrationId);
                if (integration == null) return;

                if (integration.Status == IntegrationStatus.ComingSoon)
                {
                    MessageBox.Show($"{integration.Name} is coming soon!\n\nWe're working on adding this integration.", 
                        "Coming Soon", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!string.IsNullOrEmpty(integration.ApiKeyUrl))
                {
                    var result = MessageBox.Show(
                        $"To configure {integration.Name}, you need an API key.\n\n" +
                        $"Would you like to:\n" +
                        $"• Open the API key page in your browser?\n" +
                        $"• Then add the key in Atlas Settings > Integrations\n\n" +
                        (integration.FreeWithoutKey ? $"💡 Note: {integration.FreeModeDescription}" : ""),
                        $"Configure {integration.Name}",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(integration.ApiKeyUrl) { UseShellExecute = true });
                        }
                        catch { }
                    }
                }
                else
                {
                    MessageBox.Show($"{integration.Name} is ready to use!\n\nTry saying: \"{integration.ExampleCommands.FirstOrDefault()}\"",
                        "Ready to Use", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }

    /// <summary>
    /// View model for displaying integration in the list
    /// </summary>
    public class IntegrationViewModel
    {
        private readonly IntegrationInfo _info;

        public IntegrationViewModel(IntegrationInfo info)
        {
            _info = info;
        }

        public string Id => _info.Id;
        public string Name => _info.Name;
        public string Icon => _info.Icon;
        public string Description => _info.Description;
        public List<string> CapabilitiesList => _info.Capabilities.Take(5).ToList();
        
        public string ExampleText => _info.ExampleCommands.Length > 0 
            ? $"Try: \"{_info.ExampleCommands[0]}\"" 
            : "";
        public Visibility ExampleVisibility => _info.ExampleCommands.Length > 0 && _info.Status == IntegrationStatus.Available
            ? Visibility.Visible 
            : Visibility.Collapsed;

        // Status badge
        public string StatusText => _info.Status switch
        {
            IntegrationStatus.ComingSoon => "COMING SOON",
            IntegrationStatus.Disabled => "DISABLED",
            IntegrationStatus.Error => "ERROR",
            _ => ""
        };
        public Brush StatusColor => _info.Status switch
        {
            IntegrationStatus.ComingSoon => new SolidColorBrush(Color.FromRgb(100, 100, 180)),
            IntegrationStatus.Disabled => new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            IntegrationStatus.Error => new SolidColorBrush(Color.FromRgb(180, 80, 80)),
            _ => Brushes.Transparent
        };
        public Visibility StatusBadgeVisibility => _info.Status != IntegrationStatus.Available 
            ? Visibility.Visible 
            : Visibility.Collapsed;

        // Config status
        public string ConfigStatusText
        {
            get
            {
                if (_info.Status == IntegrationStatus.ComingSoon) return "🔜 Soon";
                if (!_info.RequiresApiKey) return "✅ Ready";
                if (_info.IsConfigured) return "✅ Configured";
                if (_info.FreeWithoutKey) return "⚡ Free Mode";
                return "🔑 Needs Key";
            }
        }
        public Brush ConfigStatusColor
        {
            get
            {
                if (_info.Status == IntegrationStatus.ComingSoon) 
                    return new SolidColorBrush(Color.FromRgb(80, 80, 120));
                if (!_info.RequiresApiKey || _info.IsConfigured) 
                    return new SolidColorBrush(Color.FromRgb(45, 125, 45));
                if (_info.FreeWithoutKey) 
                    return new SolidColorBrush(Color.FromRgb(125, 93, 45));
                return new SolidColorBrush(Color.FromRgb(125, 93, 45));
            }
        }

        // Action button
        public string ActionButtonText
        {
            get
            {
                if (_info.Status == IntegrationStatus.ComingSoon) return "Learn More";
                if (_info.RequiresApiKey && !_info.IsConfigured) return "Configure";
                return "Details";
            }
        }
        public Visibility ActionButtonVisibility => Visibility.Visible;
    }
}
