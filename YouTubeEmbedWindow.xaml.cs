using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace AtlasAI
{
    public partial class YouTubeEmbedWindow : Window
    {
        private readonly string _url;

        public YouTubeEmbedWindow(string url, string? title = null)
        {
            InitializeComponent();
            _url = NormalizeUrl(url);

            if (!string.IsNullOrWhiteSpace(title))
                TitleText.Text = title.Trim();

            Loaded += async (_, _) => await InitializeAsync();
            Closed += OnWindowClosed;
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            try
            {
                // Navigate away to stop playback, then dispose the WebView2 control
                // so the embedded browser doesn't keep playing in the background.
                if (Browser?.CoreWebView2 != null)
                    Browser.CoreWebView2.Navigate("about:blank");
            }
            catch { }

            try
            {
                Browser?.Dispose();
            }
            catch { }
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasAI", "WebView2");
                Browser.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = userData };
                await Browser.EnsureCoreWebView2Async();

                try
                {
                    Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
                }
                catch
                {
                }

                Browser.Source = new Uri(_url);
            }
            catch
            {
                try { Browser.Source = new Uri(_url); } catch { }
            }
        }

        private static string NormalizeUrl(string input)
        {
            var s = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "https://www.youtube.com";

            if (TryExtractYouTubeVideoId(s, out var id))
                return $"https://www.youtube.com/embed/{id}?autoplay=1&rel=0";

            return s;
        }

        private static bool TryExtractYouTubeVideoId(string url, out string id)
        {
            id = "";
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return false;

                if (url.Length == 11 && System.Text.RegularExpressions.Regex.IsMatch(url, "^[a-zA-Z0-9_-]{11}$"))
                {
                    id = url;
                    return true;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
                    return false;

                var host = (u.Host ?? "").ToLowerInvariant();
                if (host.EndsWith("youtu.be"))
                {
                    var path = (u.AbsolutePath ?? "").Trim('/');
                    if (path.Length >= 11)
                    {
                        var candidate = path.Substring(0, 11);
                        if (System.Text.RegularExpressions.Regex.IsMatch(candidate, "^[a-zA-Z0-9_-]{11}$"))
                        {
                            id = candidate;
                            return true;
                        }
                    }
                    return false;
                }

                if (host.Contains("youtube.com"))
                {
                    var v = "";
                    try
                    {
                        var query = (u.Query ?? "").TrimStart('?');
                        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var kv = part.Split('=', 2);
                            if (kv.Length != 2) continue;
                            if (!string.Equals(kv[0], "v", StringComparison.OrdinalIgnoreCase)) continue;
                            v = Uri.UnescapeDataString(kv[1] ?? "").Trim();
                            break;
                        }
                    }
                    catch
                    {
                        v = "";
                    }
                    if (v.Length == 11 && System.Text.RegularExpressions.Regex.IsMatch(v, "^[a-zA-Z0-9_-]{11}$"))
                    {
                        id = v;
                        return true;
                    }

                    var path = (u.AbsolutePath ?? "").Trim('/');
                    if (path.StartsWith("embed/", StringComparison.OrdinalIgnoreCase))
                        path = path.Substring("embed/".Length);
                    if (path.StartsWith("shorts/", StringComparison.OrdinalIgnoreCase))
                        path = path.Substring("shorts/".Length);

                    if (path.Length >= 11)
                    {
                        var candidate = path.Substring(0, 11);
                        if (System.Text.RegularExpressions.Regex.IsMatch(candidate, "^[a-zA-Z0-9_-]{11}$"))
                        {
                            id = candidate;
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Close();
            }
            catch
            {
            }
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount >= 2)
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    return;
                }
                DragMove();
            }
            catch
            {
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                try { Close(); } catch { }
                e.Handled = true;
            }
        }
    }
}
