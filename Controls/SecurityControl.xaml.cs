using AtlasAI.Security;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AtlasAI.Controls
{
    public partial class SecurityControl : UserControl
    {
        private bool _initialized;

        public SecurityControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            IsVisibleChanged += OnVisibleChanged;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (IsVisible) await TryInitAsync();
        }

        private async void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) await TryInitAsync();
        }

        private async Task TryInitAsync()
        {
            if (_initialized) return;
            _initialized = true;
            await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                // Must be on UI thread and visible for WebView2 to init
                if (!Dispatcher.CheckAccess())
                {
                    await Dispatcher.InvokeAsync(async () => await InitializeAsync());
                    return;
                }

                // Step 1: init WebView2 with dedicated user data folder to avoid shared-environment conflicts
                if (SecurityWebView.CoreWebView2 == null)
                {
                    var userDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AtlasAI", "WebView2", "Security");
                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
                    await SecurityWebView.EnsureCoreWebView2Async(env);
                }

                if (SecurityWebView.CoreWebView2 == null) return;

                try { SecurityWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 2, 8, 23); } catch { }

                SecurityWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                SecurityWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                SecurityWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                SecurityWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

                // Clear cache to force fresh bundle load
                try
                {
                    await SecurityWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.DiskCache | 
                        CoreWebView2BrowsingDataKinds.CacheStorage);
                }
                catch { }

                // Step 2: find dist
                var dist = FindSecurityDashboardDist();

                if (dist == null)
                {
                    // Show diagnostic page so we can see what's happening
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var figmaDir = Path.Combine(baseDir, "Figma");
                    var figmaExists = Directory.Exists(figmaDir);
                    var html = $"<html><body style='background:#020817;color:#22d3ee;font-family:monospace;padding:40px'>" +
                               $"<h2>Security Dashboard - dist not found</h2>" +
                               $"<p>BaseDir: {baseDir}</p>" +
                               $"<p>Figma dir exists: {figmaExists}</p>" +
                               (figmaExists ? $"<p>Figma contents: {string.Join(", ", Directory.GetDirectories(figmaDir))}</p>" : "") +
                               $"</body></html>";
                    SecurityWebView.CoreWebView2.NavigateToString(html);
                    return;
                }

                // Step 3: map virtual host and navigate
                SecurityWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "atlas-security",
                    dist,
                    CoreWebView2HostResourceAccessKind.Allow);

                SecurityWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                SecurityWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                SecurityTelemetryService.Instance.TelemetryMessage -= OnTelemetryMessage;
                SecurityTelemetryService.Instance.TelemetryMessage += OnTelemetryMessage;
                SecurityTelemetryService.Instance.Start();

                var v = File.GetLastWriteTimeUtc(Path.Combine(dist, "index.html")).Ticks;
                SecurityWebView.CoreWebView2.Navigate($"https://atlas-security/index.html?v={v}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecurityControl] ERROR: {ex}");
                try
                {
                    SecurityWebView.CoreWebView2?.NavigateToString(
                        $"<html><body style='background:#020817;color:red;font-family:monospace;padding:40px'><h2>Error</h2><pre>{ex}</pre></body></html>");
                }
                catch { }
            }
        }

        private void OnTelemetryMessage(string json)
        {
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try { SecurityWebView.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
                });
            }
            catch { }
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // JS sends JSON.stringify(obj) as a string via postMessage — use TryGetWebMessageAsString
                // to get the raw string, then parse it. WebMessageAsJson would double-encode it.
                string raw;
                try { raw = e.TryGetWebMessageAsString(); }
                catch { raw = e.WebMessageAsJson; }
                if (string.IsNullOrWhiteSpace(raw)) return;

                System.Diagnostics.Debug.WriteLine($"[SecurityControl] Received message: {raw}");

                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp)) return;
                var type = typeProp.GetString();

                if (type == "chat")
                {
                    var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(text)) return;

                    System.Diagnostics.Debug.WriteLine($"[SecurityControl] Processing chat: {text}");
                    PostMessage("{\"type\":\"chat_typing\",\"typing\":true}");

                    try
                    {
                        var response = await SecurityAIEngine.ProcessChatAsync(text);
                        System.Diagnostics.Debug.WriteLine($"[SecurityControl] Got response, sending to UI");
                        PostMessage(response);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SecurityControl] Error processing chat: {ex.Message}");
                        var errorResponse = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            type = "chat_response",
                            text = $"Error processing request: {ex.Message}",
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        });
                        PostMessage(errorResponse);
                    }
                    finally
                    {
                        PostMessage("{\"type\":\"chat_typing\",\"typing\":false}");
                        System.Diagnostics.Debug.WriteLine($"[SecurityControl] Typing indicator cleared");
                    }
                }
                else if (type == "command")
                {
                    var cmd = root.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        System.Diagnostics.Debug.WriteLine($"[SecurityControl] Processing command: {cmd}");
                        PostMessage("{\"type\":\"chat_typing\",\"typing\":true}");

                        try
                        {
                            var response = await SecurityAIEngine.ProcessChatAsync(cmd);
                            PostMessage(response);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SecurityControl] Error processing command: {ex.Message}");
                            var errorResponse = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                type = "chat_response",
                                text = $"Error processing command: {ex.Message}",
                                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            });
                            PostMessage(errorResponse);
                        }
                        finally
                        {
                            PostMessage("{\"type\":\"chat_typing\",\"typing\":false}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecurityControl] Fatal error in message handler: {ex}");
                // Always try to clear typing indicator
                try
                {
                    PostMessage("{\"type\":\"chat_typing\",\"typing\":false}");
                }
                catch { }
            }
        }

        private void PostMessage(string json)
        {
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try { SecurityWebView.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
                });
            }
            catch { }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SecurityTelemetryService.Instance.TelemetryMessage -= OnTelemetryMessage;
                if (SecurityWebView.CoreWebView2 != null)
                    SecurityWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }
            catch { }
        }

        private static string? FindSecurityDashboardDist()
        {
            const string folderName = "AI Security Command Center Dashboard";

            var roots = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            };

            foreach (var root in roots)
            {
                try
                {
                    var dir = new DirectoryInfo(root);
                    for (var i = 0; i < 10 && dir != null; i++)
                    {
                        var candidate = Path.Combine(dir.FullName, "Figma", folderName, "dist");
                        if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
                            return candidate;
                        dir = dir.Parent;
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
