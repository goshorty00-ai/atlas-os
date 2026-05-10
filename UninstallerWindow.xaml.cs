using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.SystemControl;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace AtlasAI {
    public partial class UninstallerWindow : FluentWindow
    {
        private List<InstalledApp> _allApps = new();
        private List<InstalledApp> _filteredApps = new();
        private InstalledApp? _selectedApp;
        private string _currentFilter = "all";

        public UninstallerWindow()
        {
            InitializeComponent();
            Loaded += UninstallerWindow_Loaded;
            
            // Force cyan accent color
            try
            {
                var cyan = System.Windows.Media.Color.FromRgb(0x22, 0xd3, 0xee);
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(cyan, Wpf.Ui.Appearance.ApplicationTheme.Dark);
            }
            catch { }
            
            AppUninstaller.ProgressChanged += msg => Dispatcher.Invoke(() => ProgressText.Text = msg);
            AppUninstaller.ProgressPercentChanged += pct => Dispatcher.Invoke(() => UninstallProgress.Value = pct);
        }

        private async void UninstallerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAppsAsync();
        }

        private async System.Threading.Tasks.Task LoadAppsAsync()
        {
            LoadingRing.Visibility = Visibility.Visible;
            AppsGrid.ItemsSource = null;
            AppCountText.Text = "Loading installed applications...";

            try
            {
                _allApps = await AppUninstaller.GetInstalledAppsAsync();
                _filteredApps = _allApps.Where(a => !a.IsSystemComponent).ToList();
                AppsGrid.ItemsSource = _filteredApps;
                AppCountText.Text = $"{_filteredApps.Count} applications found ({_allApps.Count} total including system)";
            }
            catch (Exception ex)
            {
                AppCountText.Text = $"Error loading apps: {ex.Message}";
            }
            finally
            {
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void FilterBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Wpf.Ui.Controls.Button;
            if (btn?.Tag == null) return;

            _currentFilter = btn.Tag.ToString() ?? "all";

            // Update button appearances
            AllAppsBtn.Appearance = _currentFilter == "all" ? ControlAppearance.Primary : ControlAppearance.Secondary;
            LargeAppsBtn.Appearance = _currentFilter == "large" ? ControlAppearance.Primary : ControlAppearance.Secondary;
            RecentAppsBtn.Appearance = _currentFilter == "recent" ? ControlAppearance.Primary : ControlAppearance.Secondary;
            SystemAppsBtn.Appearance = _currentFilter == "system" ? ControlAppearance.Primary : ControlAppearance.Secondary;

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var searchText = SearchBox.Text?.ToLower() ?? "";

            _filteredApps = _currentFilter switch
            {
                "large" => _allApps.Where(a => a.EstimatedSize > 100 * 1024).ToList(), // >100MB
                "recent" => _allApps.Where(a => !string.IsNullOrEmpty(a.InstallDate))
                                    .OrderByDescending(a => a.InstallDate).Take(50).ToList(),
                "system" => _allApps.Where(a => a.IsSystemComponent).ToList(),
                _ => _allApps.Where(a => !a.IsSystemComponent).ToList()
            };

            if (!string.IsNullOrEmpty(searchText))
            {
                _filteredApps = _filteredApps.Where(a =>
                    a.Name.ToLower().Contains(searchText) ||
                    a.Publisher.ToLower().Contains(searchText)).ToList();
            }

            AppsGrid.ItemsSource = _filteredApps;
            AppCountText.Text = $"{_filteredApps.Count} applications shown";
        }

        private void AppsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedApp = AppsGrid.SelectedItem as InstalledApp;
            
            if (_selectedApp != null)
            {
                SelectedAppText.Text = _selectedApp.Name;
                SelectedAppDetails.Text = $"{_selectedApp.Publisher} • {_selectedApp.Version} • {_selectedApp.SizeDisplay}";
                UninstallBtn.IsEnabled = true;
                ForceUninstallBtn.IsEnabled = true;
            }
            else
            {
                SelectedAppText.Text = "Select an application to uninstall";
                SelectedAppDetails.Text = "";
                UninstallBtn.IsEnabled = false;
                ForceUninstallBtn.IsEnabled = false;
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadAppsAsync();
        }

        private async void UninstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApp == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to uninstall {_selectedApp.Name}?\n\n" +
                $"Publisher: {_selectedApp.Publisher}\n" +
                $"Version: {_selectedApp.Version}\n" +
                $"Size: {_selectedApp.SizeDisplay}",
                "Confirm Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            await PerformUninstallAsync(_selectedApp, cleanLeftovers: false);
        }

        private async void ForceUninstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApp == null) return;

            var result = MessageBox.Show(
                $"Force uninstall will:\n\n" +
                $"1. Run the standard uninstaller\n" +
                $"2. Scan for leftover files and folders\n" +
                $"3. Scan for leftover registry entries\n" +
                $"4. Clean all leftovers\n\n" +
                $"Uninstall {_selectedApp.Name}?",
                "Force Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            await PerformUninstallAsync(_selectedApp, cleanLeftovers: true);
        }

        private async System.Threading.Tasks.Task PerformUninstallAsync(InstalledApp app, bool cleanLeftovers)
        {
            UninstallBtn.IsEnabled = false;
            ForceUninstallBtn.IsEnabled = false;
            RefreshBtn.IsEnabled = false;
            UninstallProgress.Visibility = Visibility.Visible;
            UninstallProgress.Value = 0;

            try
            {
                var uninstallResult = await AppUninstaller.UninstallAppAsync(app, cleanLeftovers);

                if (cleanLeftovers && (uninstallResult.LeftoverPaths.Any() || uninstallResult.LeftoverRegistry.Any()))
                {
                    var leftoverMsg = $"Found leftovers:\n\n";
                    
                    if (uninstallResult.LeftoverPaths.Any())
                    {
                        leftoverMsg += $"📁 {uninstallResult.LeftoverPaths.Count} folder(s):\n";
                        foreach (var path in uninstallResult.LeftoverPaths.Take(5))
                            leftoverMsg += $"  • {path}\n";
                        if (uninstallResult.LeftoverPaths.Count > 5)
                            leftoverMsg += $"  ... and {uninstallResult.LeftoverPaths.Count - 5} more\n";
                    }

                    if (uninstallResult.LeftoverRegistry.Any())
                    {
                        leftoverMsg += $"\n🔑 {uninstallResult.LeftoverRegistry.Count} registry key(s):\n";
                        foreach (var key in uninstallResult.LeftoverRegistry.Take(3))
                            leftoverMsg += $"  • {key}\n";
                        if (uninstallResult.LeftoverRegistry.Count > 3)
                            leftoverMsg += $"  ... and {uninstallResult.LeftoverRegistry.Count - 3} more\n";
                    }

                    leftoverMsg += "\nDelete these leftovers?";

                    var cleanResult = MessageBox.Show(leftoverMsg, "Clean Leftovers?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (cleanResult == MessageBoxResult.Yes)
                    {
                        ProgressText.Text = "Cleaning leftovers...";
                        var cleaned = await AppUninstaller.CleanLeftoversAsync(
                            uninstallResult.LeftoverPaths, 
                            uninstallResult.LeftoverRegistry);
                        
                        MessageBox.Show($"Cleaned {cleaned} leftover items.", "Cleanup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                MessageBox.Show(uninstallResult.Message, 
                    uninstallResult.Success ? "Uninstall Complete" : "Uninstall Result",
                    MessageBoxButton.OK,
                    uninstallResult.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

                // Refresh the list
                await LoadAppsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during uninstall: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UninstallBtn.IsEnabled = true;
                ForceUninstallBtn.IsEnabled = true;
                RefreshBtn.IsEnabled = true;
                UninstallProgress.Visibility = Visibility.Collapsed;
                ProgressText.Text = "";
            }
        }
    }
}
