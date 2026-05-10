using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AtlasAI.Core;
using AtlasAI.Integrations;
using AtlasAI.Models;

namespace AtlasAI.Views.ViewModels
{
    public sealed class ServersViewModel : INotifyPropertyChanged
    {
        private readonly AddonManifestService _addonsService = new();
        private readonly string _configPath;
        private AddonServerItem? _selectedServer;
        private bool _isTestingServer;

        public ObservableCollection<AddonServerItem> Servers { get; } = new();

        public AddonServerItem? SelectedServer
        {
            get => _selectedServer;
            set => SetProperty(ref _selectedServer, value);
        }

        public bool IsTestingServer
        {
            get => _isTestingServer;
            set => SetProperty(ref _isTestingServer, value);
        }

        public ICommand AddServerCommand { get; }
        public ICommand EditServerCommand { get; }
        public ICommand RemoveServerCommand { get; }
        public ICommand TestServerCommand { get; }
        public ICommand TestAllServersCommand { get; }
        public ICommand ToggleEnabledCommand { get; }

        public ServersViewModel()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
            Directory.CreateDirectory(appDataPath);
            _configPath = Path.Combine(appDataPath, "addon_servers.json");

            AddServerCommand = new RelayCommand(AddServer);
            EditServerCommand = new RelayCommand(EditServer, () => SelectedServer != null);
            RemoveServerCommand = new RelayCommand(RemoveServer, () => SelectedServer != null);
            TestServerCommand = new RelayCommand(async () => await TestServerAsync(), () => SelectedServer != null && !IsTestingServer);
            TestAllServersCommand = new RelayCommand(async () => await ConnectAllEnabledAddonsAsync(), () => !IsTestingServer);
            ToggleEnabledCommand = new RelayCommand<AddonServerItem>(ToggleEnabled);

            // Don't load servers in constructor - will be called from View.Loaded event
        }

        public async Task LoadServersAsync()
        {
            try
            {
                var loadedServers = new AddonServerItem[0];
                if (File.Exists(_configPath))
                {
                    var json = await File.ReadAllTextAsync(_configPath).ConfigureAwait(false);
                    loadedServers = JsonSerializer.Deserialize<AddonServerItem[]>(json) ?? Array.Empty<AddonServerItem>();
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Servers.Clear();
                    AddManagedServers();

                    var persistedServers = loadedServers
                        .Where(server => server != null && !server.IsManagedByAtlas)
                        .ToArray();

                    if (persistedServers.Length > 0)
                    {
                        foreach (var server in persistedServers)
                            Servers.Add(server);
                        Debug.WriteLine($"[ServersViewModel] Loaded {persistedServers.Length} remote addon servers from config");
                    }
                    else
                    {
                        SeedDefaultServers();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServersViewModel] Failed to load servers: {ex.Message}");
                SeedDefaultServers();
            }
        }

        private void SeedDefaultServers()
        {
            Servers.Clear();
            AddManagedServers();

            Debug.WriteLine("[ServersViewModel] Seeded Atlas managed addons");
            SaveServers();
        }

        private void AddManagedServers()
        {
            foreach (var server in AtlasAI.Integrations.AtlasManagedAddonCatalog.CreateManagedServerItems())
            {
                if (Servers.Any(existing => string.Equals(existing.Url, server.Url, StringComparison.OrdinalIgnoreCase)))
                    continue;

                Servers.Add(server);
            }
        }

        private void SaveServers()
        {
            try
            {
                var json = JsonSerializer.Serialize(Servers.ToArray(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                var persistedServers = Servers
                    .Where(server => server != null && !server.IsManagedByAtlas)
                    .ToArray();

                json = JsonSerializer.Serialize(persistedServers, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_configPath, json);
                Debug.WriteLine($"[ServersViewModel] Saved {persistedServers.Length} remote addon servers to config");

                // Keep protected store in sync with config so removed servers don't reappear.
                try
                {
                    var urls = Servers
                        .Where(s => s != null && !s.IsManagedByAtlas && !string.IsNullOrWhiteSpace(s.Url))
                        .Select(s => s.Url.Trim())
                        .ToList();
                    IntegrationKeyStore.SetProtected("streaming_addon_servers", JsonSerializer.Serialize(urls));
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServersViewModel] Failed to save servers: {ex.Message}");
            }
        }

        private void AddServer()
        {
            var dialog = new AddServerDialog();
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ServerName) && !string.IsNullOrWhiteSpace(dialog.ServerUrl))
            {
                var newServer = new AddonServerItem
                {
                    Name = dialog.ServerName,
                    Url = dialog.ServerUrl,
                    Enabled = false,
                    Status = ServerStatus.Unknown
                };
                Servers.Add(newServer);
                SaveServers();
                SelectedServer = newServer;
            }
        }

        private void EditServer()
        {
            if (SelectedServer == null) return;
            if (SelectedServer.IsManagedByAtlas)
            {
                MessageBox.Show("Atlas managed addons are built in and cannot be edited here.",
                    "Atlas Addon",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dialog = new AddServerDialog(SelectedServer.Name, SelectedServer.Url);
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ServerName) && !string.IsNullOrWhiteSpace(dialog.ServerUrl))
            {
                SelectedServer.Name = dialog.ServerName;
                SelectedServer.Url = dialog.ServerUrl;
                SaveServers();
            }
        }

        private void RemoveServer()
        {
            if (SelectedServer == null) return;
            if (SelectedServer.IsManagedByAtlas)
            {
                MessageBox.Show("Atlas managed addons are built in and cannot be removed.",
                    "Atlas Addon",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to remove '{SelectedServer.Name}'?",
                "Remove Server",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Servers.Remove(SelectedServer);
                SaveServers();
                SelectedServer = Servers.FirstOrDefault();
            }
        }

        private async Task TestServerAsync()
        {
            if (SelectedServer == null || IsTestingServer) return;

            IsTestingServer = true;
            SelectedServer.Status = ServerStatus.Unknown;
            SelectedServer.ErrorMessage = "Testing...";
            SelectedServer.LastCheck = DateTime.Now;

            try
            {
                var (success, manifest, error) = await _addonsService.FetchManifestAsync(SelectedServer.Url);

                if (success && manifest != null)
                {
                    SelectedServer.Status = ServerStatus.Online;
                    SelectedServer.ErrorMessage = $"✓ {manifest.Name} v{manifest.Version}";
                    Debug.WriteLine($"[ServersViewModel] Server '{SelectedServer.Name}' is online: {manifest.Name}");
                }
                else
                {
                    SelectedServer.Status = ServerStatus.Error;
                    SelectedServer.ErrorMessage = error;
                    Debug.WriteLine($"[ServersViewModel] Server '{SelectedServer.Name}' test failed: {error}");
                }
            }
            catch (Exception ex)
            {
                SelectedServer.Status = ServerStatus.Error;
                SelectedServer.ErrorMessage = $"Unexpected error: {ex.Message}";
                Debug.WriteLine($"[ServersViewModel] Unexpected error testing '{SelectedServer.Name}': {ex.Message}");
            }
            finally
            {
                IsTestingServer = false;
                SaveServers();
            }
        }

        /// <summary>
        /// Connects to ALL enabled addon servers concurrently
        /// </summary>
        public async Task ConnectAllEnabledAddonsAsync()
        {
            if (IsTestingServer) return;

            var enabledServers = Servers.Where(s => s.Enabled).ToList();
            if (enabledServers.Count == 0)
            {
                Debug.WriteLine("[ServersViewModel] No enabled servers to test");
                MessageBox.Show("No enabled addon servers found. Please enable at least one server.", 
                    "No Enabled Servers", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsTestingServer = true;
            Debug.WriteLine($"[ServersViewModel] Testing {enabledServers.Count} enabled servers concurrently...");

            // Reset all enabled servers to Unknown status
            foreach (var server in enabledServers)
            {
                server.Status = ServerStatus.Unknown;
                server.ErrorMessage = "Testing...";
                server.LastCheck = DateTime.Now;
            }

            try
            {
                // Test all enabled servers concurrently
                var tasks = enabledServers.Select(async server =>
                {
                    try
                    {
                        var (success, manifest, error) = await _addonsService.FetchManifestAsync(server.Url);

                        if (success && manifest != null)
                        {
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                server.Status = ServerStatus.Online;
                                server.ErrorMessage = $"✓ {manifest.Name} v{manifest.Version}";
                            });
                            Debug.WriteLine($"[ServersViewModel] Server '{server.Name}' is online: {manifest.Name}");
                        }
                        else
                        {
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                server.Status = ServerStatus.Error;
                                server.ErrorMessage = error;
                            });
                            Debug.WriteLine($"[ServersViewModel] Server '{server.Name}' test failed: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            server.Status = ServerStatus.Error;
                            server.ErrorMessage = $"Unexpected error: {ex.Message}";
                        });
                        Debug.WriteLine($"[ServersViewModel] Unexpected error testing '{server.Name}': {ex.Message}");
                    }
                });

                await Task.WhenAll(tasks);

                var onlineCount = enabledServers.Count(s => s.Status == ServerStatus.Online);
                Debug.WriteLine($"[ServersViewModel] Connected to {onlineCount}/{enabledServers.Count} enabled addon servers");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"Successfully connected to {onlineCount} out of {enabledServers.Count} enabled addon servers.",
                        "Addon Connection Complete",
                        MessageBoxButton.OK,
                        onlineCount == enabledServers.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServersViewModel] Failed to test all servers: {ex.Message}");
            }
            finally
            {
                IsTestingServer = false;
                SaveServers();
            }
        }

        private void ToggleEnabled(AddonServerItem? server)
        {
            if (server == null) return;
            server.Enabled = !server.Enabled;
            SaveServers();
            Debug.WriteLine($"[ServersViewModel] Server '{server.Name}' enabled: {server.Enabled}");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class AddServerDialog : Window
    {
        private readonly System.Windows.Controls.TextBox _nameBox;
        private readonly System.Windows.Controls.TextBox _urlBox;

        public string ServerName => _nameBox.Text;
        public string ServerUrl => _urlBox.Text;

        public AddServerDialog(string name = "", string url = "")
        {
            Title = string.IsNullOrEmpty(name) ? "Add Server" : "Edit Server";
            Width = 500;
            Height = 220;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0E14"));

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            var nameLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Server Name:",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            System.Windows.Controls.Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);

            _nameBox = new System.Windows.Controls.TextBox
            {
                Text = name,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 15)
            };
            System.Windows.Controls.Grid.SetRow(_nameBox, 1);
            grid.Children.Add(_nameBox);

            var urlLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Server URL:",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            System.Windows.Controls.Grid.SetRow(urlLabel, 2);
            grid.Children.Add(urlLabel);

            _urlBox = new System.Windows.Controls.TextBox
            {
                Text = url,
                Padding = new Thickness(8)
            };
            System.Windows.Controls.Grid.SetRow(_urlBox, 3);
            grid.Children.Add(_urlBox);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            System.Windows.Controls.Grid.SetRow(buttonPanel, 5);
            grid.Children.Add(buttonPanel);

            var okButton = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 80,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0)
            };
            okButton.Click += (s, e) => { DialogResult = true; Close(); };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 32
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelButton);

            Content = grid;
        }
    }
}
