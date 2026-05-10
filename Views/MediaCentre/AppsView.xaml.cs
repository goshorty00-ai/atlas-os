using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using AtlasAI.Views.ViewModels;

namespace AtlasAI.Views.MediaCentre
{
    public partial class AppsView : UserControl
    {
        private bool _eventsHooked;
        private double _zoom = 1.0;
        private ICollectionView? _appsServicesView;
        private bool _isExpanded;
        private CoreWebView2Environment? _environment;
        private string? _userDataFolder;
        private bool _fullscreenHooked;

        public AppsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is MediaCentreViewModel vm)
                {
                    vm.AttachAppsNavigator(NavigateTo);
                    _appsServicesView = CollectionViewSource.GetDefaultView(vm.AppsServices);
                    if (_appsServicesView != null)
                        _appsServicesView.Filter = AppsServicesFilter;
                }

                await EnsureBrowserAsync();
                if (!_fullscreenHooked)
                {
                    PreviewKeyDown += AppsView_PreviewKeyDown;
                    _fullscreenHooked = true;
                }
            }
            catch
            {
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isExpanded)
                    SetFullscreen(false);
            }
            catch
            {
            }
        }

        private bool AppsServicesFilter(object obj)
        {
            try
            {
                if (AppsSearchBox == null) return true;
                var q = (AppsSearchBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(q)) return true;
                if (obj is not MediaCentreViewModel.AppsServiceEntry s) return true;
                var name = (s.Name ?? "");
                return name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return true;
            }
        }

        private async Task EnsureBrowserAsync()
        {
            if (AppsBrowser == null) return;
            if (AppsBrowser.CoreWebView2 != null) return;

            var baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasAI");
            Directory.CreateDirectory(baseFolder);
            _userDataFolder ??= Path.Combine(baseFolder, "webview_apps");
            Directory.CreateDirectory(_userDataFolder);

            _environment ??= await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
            await AppsBrowser.EnsureCoreWebView2Async(_environment);

            if (AppsBrowser.CoreWebView2 != null && !_eventsHooked)
            {
                AppsBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                AppsBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                AppsBrowser.CoreWebView2.Settings.IsZoomControlEnabled = false;
                AppsBrowser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

                AppsBrowser.CoreWebView2.NewWindowRequested += (_, args) =>
                {
                    var deferral = args.GetDeferral();
                    Dispatcher.BeginInvoke(async () =>
                    {
                        try
                        {
                            await EnsureBrowserAsync();

                            var owner = Window.GetWindow(this);
                            var popupWindow = new Window
                            {
                                Title = "Sign in",
                                Width = 520,
                                Height = 760,
                                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                                Owner = owner,
                                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x14)),
                                Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB))
                            };

                            var popupBrowser = new WebView2();
                            popupWindow.Content = popupBrowser;

                            popupWindow.Show();

                            if (_environment != null)
                                await popupBrowser.EnsureCoreWebView2Async(_environment);
                            else
                                await popupBrowser.EnsureCoreWebView2Async();

                            if (popupBrowser.CoreWebView2 != null)
                            {
                                popupBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                                popupBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                                popupBrowser.CoreWebView2.Settings.IsZoomControlEnabled = false;
                                popupBrowser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

                                popupBrowser.CoreWebView2.NavigationCompleted += (_, __) =>
                                {
                                    try
                                    {
                                        var uri = popupBrowser.Source?.ToString() ?? "";
                                        if (!string.IsNullOrWhiteSpace(uri) &&
                                            (uri.StartsWith("https://soundcloud.com/", StringComparison.OrdinalIgnoreCase) ||
                                             uri.StartsWith("https://accounts.soundcloud.com/", StringComparison.OrdinalIgnoreCase)))
                                        {
                                            if (AppsBrowser?.CoreWebView2 != null)
                                                AppsBrowser.CoreWebView2.Navigate(uri);
                                        }
                                    }
                                    catch
                                    {
                                    }
                                };
                            }

                            args.NewWindow = popupBrowser.CoreWebView2;
                            args.Handled = true;
                        }
                        catch
                        {
                            try { args.Handled = true; } catch { }
                        }
                        finally
                        {
                            try { deferral.Complete(); } catch { }
                        }
                    });
                };

                AppsBrowser.CoreWebView2.PermissionRequested += (_, args) =>
                {
                    try
                    {
                        if (args.PermissionKind == CoreWebView2PermissionKind.Microphone ||
                            args.PermissionKind == CoreWebView2PermissionKind.Camera)
                            args.State = CoreWebView2PermissionState.Deny;
                    }
                    catch
                    {
                    }
                };

                AppsBrowser.CoreWebView2.NavigationCompleted += (_, __) =>
                {
                    try
                    {
                        if (UrlText != null)
                            UrlText.Text = AppsBrowser.Source?.ToString() ?? "";
                    }
                    catch
                    {
                    }
                };

                _eventsHooked = true;
            }

            if (AppsBrowser.Source == null)
                AppsBrowser.Source = new Uri("https://www.youtube.com/");
        }

        private void NavigateTo(string url)
        {
            Dispatcher.Invoke(async () =>
            {
                try
                {
                    await EnsureBrowserAsync();
                    if (AppsBrowser?.CoreWebView2 == null) return;
                    if (string.IsNullOrWhiteSpace(url)) return;
                    AppsBrowser.CoreWebView2.Navigate(url);
                }
                catch
                {
                }
            });
        }

        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureBrowserAsync();
                if (AppsBrowser?.CoreWebView2?.CanGoBack == true)
                    AppsBrowser.CoreWebView2.GoBack();
            }
            catch
            {
            }
        }

        private async void Forward_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureBrowserAsync();
                if (AppsBrowser?.CoreWebView2?.CanGoForward == true)
                    AppsBrowser.CoreWebView2.GoForward();
            }
            catch
            {
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureBrowserAsync();
                AppsBrowser?.CoreWebView2?.Reload();
            }
            catch
            {
            }
        }

        private async void Home_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureBrowserAsync();
                AppsBrowser?.CoreWebView2?.Navigate("https://www.youtube.com/");
            }
            catch
            {
            }
        }

        private void AppsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _appsServicesView?.Refresh();
            }
            catch
            {
            }
        }

        private void Expand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetFullscreen(!_isExpanded);
            }
            catch
            {
            }
        }

        private void FullscreenExit_Click(object sender, RoutedEventArgs e)
        {
            try { SetFullscreen(false); } catch { }
        }

        private void AppsView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (!_isExpanded) return;
                if (e.Key == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.F11)
                {
                    SetFullscreen(false);
                    e.Handled = true;
                }
            }
            catch
            {
            }
        }

        private void SetFullscreen(bool enabled)
        {
            _isExpanded = enabled;

            try
            {
                if (RootGrid != null)
                    RootGrid.Margin = enabled ? new Thickness(0) : new Thickness(24, 0, 24, 24);

                // Keep header visible in fullscreen so we always have a reliable way to exit.
                if (AppsHeaderBar != null)
                    AppsHeaderBar.Visibility = Visibility.Visible;

                try
                {
                    if (AppsHeaderSpacerRow != null)
                        AppsHeaderSpacerRow.Height = enabled ? new GridLength(0) : new GridLength(12);
                }
                catch
                {
                }

                try
                {
                    if (ExpandBtn != null)
                        ExpandBtn.Content = enabled ? "⤡" : "⤢";
                }
                catch
                {
                }

                if (FullscreenExitBtn != null)
                    FullscreenExitBtn.Visibility = Visibility.Collapsed;

                if (AppsListColumn != null)
                    AppsListColumn.Width = enabled ? new GridLength(0) : new GridLength(360);
                if (AppsListSpacerColumn != null)
                    AppsListSpacerColumn.Width = enabled ? new GridLength(0) : new GridLength(12);
                if (AppsListHost != null)
                    AppsListHost.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

                if (AppsBrowserHost != null)
                {
                    AppsBrowserHost.SetValue(Grid.ColumnProperty, enabled ? 0 : 2);
                    AppsBrowserHost.SetValue(Grid.ColumnSpanProperty, enabled ? 3 : 1);
                    AppsBrowserHost.SetValue(Grid.RowProperty, 2);
                    AppsBrowserHost.SetValue(Grid.RowSpanProperty, 1);
                    AppsBrowserHost.CornerRadius = enabled ? new CornerRadius(0) : (CornerRadius)TryFindResource("NeoRadius16");
                }
            }
            catch
            {
            }
        }

        private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            try
            {
                var cur = d;
                while (cur != null)
                {
                    if (cur is T t) return t;
                    cur = VisualTreeHelper.GetParent(cur);
                }
            }
            catch
            {
            }
            return null;
        }
    }
}
