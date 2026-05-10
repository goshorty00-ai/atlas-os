using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using AtlasAI.Voice;
using AtlasAI.Core;

namespace AtlasAI.Tools
{
    /// <summary>
    /// Spotify integration with OAuth 2.0 PKCE flow for full playback control.
    /// Requires user to authorize once, then can control playback, search, and play tracks.
    /// </summary>
    public static class SpotifyTool
    {
        private static readonly HttpClient httpClient;
        private static string? _accessToken;
        private static string? _refreshToken;
        private static DateTime _tokenExpiry = DateTime.MinValue;
        
        // Spotify App credentials (user provides their own Client ID)
        private static string? _clientId;
        private const string RedirectUri = "http://127.0.0.1:5543/callback";
        private const string Scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing streaming app-remote-control playlist-read-private playlist-read-collaborative";
        
        // PKCE code verifier
        private static string? _codeVerifier;
        
        // Token storage path
        private static readonly string TokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "spotify_token.json");

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;

        static SpotifyTool()
        {
            httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            LoadSpotifyCredentials();
            LoadStoredToken();
        }

        private static void LoadSpotifyCredentials()
        {
            try
            {
                var keysPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "integration_keys.json");
                if (File.Exists(keysPath))
                {
                    var json = File.ReadAllText(keysPath);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("spotify_client_id", out var id))
                        _clientId = SecretProtector.UnprotectIfNeeded(id.GetString() ?? "");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Failed to load credentials: {ex.Message}");
            }
        }
        
        private static void LoadStoredToken()
        {
            try
            {
                if (File.Exists(TokenPath))
                {
                    var json = File.ReadAllText(TokenPath);
                    var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("access_token", out var at))
                        _accessToken = at.GetString();
                    if (doc.RootElement.TryGetProperty("refresh_token", out var rt))
                        _refreshToken = rt.GetString();
                    if (doc.RootElement.TryGetProperty("expiry", out var exp))
                        _tokenExpiry = DateTime.Parse(exp.GetString() ?? DateTime.MinValue.ToString());
                    
                    Debug.WriteLine($"[Spotify] Loaded stored token, expires: {_tokenExpiry}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Failed to load stored token: {ex.Message}");
            }
        }
        
        private static void SaveToken()
        {
            try
            {
                var dir = Path.GetDirectoryName(TokenPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                var token = new
                {
                    access_token = _accessToken,
                    refresh_token = _refreshToken,
                    expiry = _tokenExpiry.ToString("o")
                };
                
                var json = JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(TokenPath, json);
                Debug.WriteLine("[Spotify] Token saved");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Failed to save token: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if Spotify API is configured and authorized
        /// </summary>
        public static bool IsApiConfigured => !string.IsNullOrEmpty(_clientId);
        public static bool IsAuthorized => !string.IsNullOrEmpty(_accessToken) && DateTime.Now < _tokenExpiry;

        public static void ReloadClientId()
        {
            try
            {
                LoadSpotifyCredentials();
            }
            catch
            {
            }
        }
        
        /// <summary>
        /// Start OAuth authorization flow - opens browser for user to authorize
        /// </summary>
        public static async Task<string> AuthorizeAsync()
        {
            if (string.IsNullOrEmpty(_clientId))
            {
                return "❌ Spotify Client ID not configured. Add 'spotify_client_id' to Settings → Integrations.";
            }
            
            try
            {
                // Generate PKCE code verifier and challenge
                _codeVerifier = GenerateCodeVerifier();
                var codeChallenge = GenerateCodeChallenge(_codeVerifier);
                
                // Build authorization URL
                var authUrl = $"https://accounts.spotify.com/authorize?" +
                    $"client_id={_clientId}&" +
                    $"response_type=code&" +
                    $"redirect_uri={HttpUtility.UrlEncode(RedirectUri)}&" +
                    $"scope={HttpUtility.UrlEncode(Scopes)}&" +
                    $"code_challenge_method=S256&" +
                    $"code_challenge={codeChallenge}";
                
                // Start local HTTP listener for callback
                var listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:5543/");
                listener.Prefixes.Add("http://127.0.0.1:5543/");
                listener.Start();
                
                // Open browser for authorization
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                
                // Wait for callback (with timeout)
                var contextTask = listener.GetContextAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
                
                var completedTask = await Task.WhenAny(contextTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    listener.Stop();
                    return "❌ Authorization timed out. Please try again.";
                }
                
                var context = await contextTask;
                var code = context.Request.QueryString["code"];
                var error = context.Request.QueryString["error"];
                
                // Send response to browser
                var responseHtml = error != null
                    ? "<html><body><h1>Authorization Failed</h1><p>You can close this window.</p></body></html>"
                    : "<html><body><h1>✓ Authorization Successful!</h1><p>You can close this window and return to Atlas AI.</p></body></html>";
                
                var buffer = Encoding.UTF8.GetBytes(responseHtml);
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
                listener.Stop();
                
                if (!string.IsNullOrEmpty(error))
                {
                    return $"❌ Authorization denied: {error}";
                }
                
                if (string.IsNullOrEmpty(code))
                {
                    return "❌ No authorization code received.";
                }
                
                // Exchange code for tokens
                return await ExchangeCodeForTokenAsync(code);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Authorization error: {ex.Message}");
                return $"❌ Authorization failed: {ex.Message}";
            }
        }
        
        private static async Task<string> ExchangeCodeForTokenAsync(string code)
        {
            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = RedirectUri,
                    ["client_id"] = _clientId!,
                    ["code_verifier"] = _codeVerifier!
                });
                
                var response = await httpClient.PostAsync("https://accounts.spotify.com/api/token", content);
                var responseText = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[Spotify] Token exchange failed: {responseText}");
                    return $"❌ Token exchange failed: {response.StatusCode}";
                }
                
                var doc = JsonDocument.Parse(responseText);
                _accessToken = doc.RootElement.GetProperty("access_token").GetString();
                _refreshToken = doc.RootElement.GetProperty("refresh_token").GetString();
                var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
                _tokenExpiry = DateTime.Now.AddSeconds(expiresIn - 60);
                
                SaveToken();
                
                return "✓ Spotify authorized! You can now control playback with voice commands.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Token exchange error: {ex.Message}");
                return $"❌ Token exchange failed: {ex.Message}";
            }
        }
        
        private static async Task<bool> RefreshAccessTokenAsync()
        {
            if (string.IsNullOrEmpty(_refreshToken) || string.IsNullOrEmpty(_clientId))
                return false;
            
            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = _refreshToken,
                    ["client_id"] = _clientId
                });
                
                var response = await httpClient.PostAsync("https://accounts.spotify.com/api/token", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[Spotify] Token refresh failed: {response.StatusCode}");
                    return false;
                }
                
                var responseText = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(responseText);
                
                _accessToken = doc.RootElement.GetProperty("access_token").GetString();
                var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
                _tokenExpiry = DateTime.Now.AddSeconds(expiresIn - 60);
                
                // Refresh token might be rotated
                if (doc.RootElement.TryGetProperty("refresh_token", out var newRefresh))
                    _refreshToken = newRefresh.GetString();
                
                SaveToken();
                Debug.WriteLine("[Spotify] Token refreshed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Token refresh error: {ex.Message}");
                return false;
            }
        }
        
        private static async Task<bool> EnsureAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.Now < _tokenExpiry)
                return true;
            
            if (!string.IsNullOrEmpty(_refreshToken))
                return await RefreshAccessTokenAsync();
            
            return false;
        }
        
        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        
        private static string GenerateCodeChallenge(string verifier)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(verifier));
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Search for a track on Spotify
        /// </summary>
        private static async Task<(string? uri, string? name, string? artist)> SearchTrackAsync(string query)
        {
            if (!await EnsureAccessTokenAsync())
                return (null, null, null);

            try
            {
                var url = $"https://api.spotify.com/v1/search?q={HttpUtility.UrlEncode(query)}&type=track&limit=1";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(responseText);
                    var tracks = doc.RootElement.GetProperty("tracks").GetProperty("items");
                    if (tracks.GetArrayLength() > 0)
                    {
                        var track = tracks[0];
                        return (
                            track.GetProperty("uri").GetString(),
                            track.GetProperty("name").GetString(),
                            track.GetProperty("artists")[0].GetProperty("name").GetString()
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Search error: {ex.Message}");
            }
            return (null, null, null);
        }
        
        /// <summary>
        /// Search for an artist on Spotify
        /// </summary>
        private static async Task<(string? uri, string? name)> SearchArtistAsync(string query)
        {
            if (!await EnsureAccessTokenAsync())
                return (null, null);

            try
            {
                var url = $"https://api.spotify.com/v1/search?q={HttpUtility.UrlEncode(query)}&type=artist&limit=1";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(responseText);
                    var artists = doc.RootElement.GetProperty("artists").GetProperty("items");
                    if (artists.GetArrayLength() > 0)
                    {
                        var artist = artists[0];
                        return (
                            artist.GetProperty("uri").GetString(),
                            artist.GetProperty("name").GetString()
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Artist search error: {ex.Message}");
            }
            return (null, null);
        }
        
        /// <summary>
        /// Get available playback devices
        /// </summary>
        private static async Task<string?> GetActiveDeviceIdAsync()
        {
            if (!await EnsureAccessTokenAsync())
                return null;
            
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/devices");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                
                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(responseText);
                    var devices = doc.RootElement.GetProperty("devices");
                    
                    // Find active device or first available
                    foreach (var device in devices.EnumerateArray())
                    {
                        if (device.GetProperty("is_active").GetBoolean())
                            return device.GetProperty("id").GetString();
                    }
                    
                    // No active device, return first one
                    if (devices.GetArrayLength() > 0)
                        return devices[0].GetProperty("id").GetString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Get devices error: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Play a song on Spotify using the Web API
        /// </summary>
        public static async Task<string> PlayAsync(string query)
        {
            try
            {
                AudioDuckingManager.SkipNextRestore();
                Debug.WriteLine($"[Spotify] PlayAsync called with query: '{query}'");
                
                // Make sure Spotify desktop is running
                var procs = Process.GetProcessesByName("Spotify");
                bool wasSpotifyRunning = procs.Length > 0;
                
                if (!wasSpotifyRunning)
                {
                    Debug.WriteLine("[Spotify] Spotify not running, launching...");
                    Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
                    await Task.Delay(4000);
                }
                
                // Try API playback if authorized
                if (await EnsureAccessTokenAsync())
                {
                    // Search for track
                    var (trackUri, trackName, artistName) = await SearchTrackAsync(query);
                    
                    if (!string.IsNullOrEmpty(trackUri))
                    {
                        Debug.WriteLine($"[Spotify] Found track: {trackName} by {artistName}");
                        
                        // Get device ID
                        var deviceId = await GetActiveDeviceIdAsync();
                        
                        // Start playback via API
                        var playRequest = new HttpRequestMessage(HttpMethod.Put, 
                            deviceId != null 
                                ? $"https://api.spotify.com/v1/me/player/play?device_id={deviceId}"
                                : "https://api.spotify.com/v1/me/player/play");
                        playRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                        playRequest.Content = new StringContent(
                            JsonSerializer.Serialize(new { uris = new[] { trackUri } }),
                            Encoding.UTF8, "application/json");
                        
                        var response = await httpClient.SendAsync(playRequest);
                        
                        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
                        {
                            return $"🎵 Playing: {trackName} by {artistName}";
                        }
                        
                        Debug.WriteLine($"[Spotify] API play failed: {response.StatusCode}, falling back to URI");
                    }
                    
                    // Try artist if no track found
                    var (artistUri, foundArtistName) = await SearchArtistAsync(query);
                    if (!string.IsNullOrEmpty(artistUri))
                    {
                        var deviceId = await GetActiveDeviceIdAsync();
                        var playRequest = new HttpRequestMessage(HttpMethod.Put,
                            deviceId != null
                                ? $"https://api.spotify.com/v1/me/player/play?device_id={deviceId}"
                                : "https://api.spotify.com/v1/me/player/play");
                        playRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                        playRequest.Content = new StringContent(
                            JsonSerializer.Serialize(new { context_uri = artistUri }),
                            Encoding.UTF8, "application/json");
                        
                        var response = await httpClient.SendAsync(playRequest);
                        
                        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
                        {
                            return $"🎵 Playing: {foundArtistName}";
                        }
                    }
                }
                
                // Fallback: Use spotify: URI protocol (works without API auth)
                Debug.WriteLine("[Spotify] Using URI protocol fallback");
                var searchUri = $"spotify:search:{HttpUtility.UrlEncode(query)}";
                Process.Start(new ProcessStartInfo(searchUri) { UseShellExecute = true });
                await Task.Delay(2500);
                
                // Send Enter to play first result
                keybd_event(0x0D, 0, 0, UIntPtr.Zero);
                await Task.Delay(50);
                keybd_event(0x0D, 0, 2, UIntPtr.Zero);
                
                return $"🎵 Playing: {query} on Spotify";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Error: {ex.Message}");
                return $"❌ Error: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Control playback (play/pause/next/previous) via API
        /// </summary>
        public static async Task<string> ControlPlaybackAsync(string action)
        {
            var actionLower = action.ToLower();
            
            if (actionLower == "pause" || actionLower == "stop")
            {
                AudioDuckingManager.ClearMusicProtection();
            }
            
            // Try API control first
            if (await EnsureAccessTokenAsync())
            {
                try
                {
                    HttpRequestMessage request;
                    string endpoint;
                    HttpMethod method;
                    
                    switch (actionLower)
                    {
                        case "next":
                        case "skip":
                            endpoint = "https://api.spotify.com/v1/me/player/next";
                            method = HttpMethod.Post;
                            break;
                        case "previous":
                        case "prev":
                            endpoint = "https://api.spotify.com/v1/me/player/previous";
                            method = HttpMethod.Post;
                            break;
                        case "pause":
                        case "stop":
                            endpoint = "https://api.spotify.com/v1/me/player/pause";
                            method = HttpMethod.Put;
                            break;
                        case "resume":
                        case "play":
                        default:
                            endpoint = "https://api.spotify.com/v1/me/player/play";
                            method = HttpMethod.Put;
                            break;
                    }
                    
                    request = new HttpRequestMessage(method, endpoint);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                    
                    var response = await httpClient.SendAsync(request);
                    
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
                    {
                        return actionLower switch
                        {
                            "next" or "skip" => "⏭️ Next track",
                            "previous" or "prev" => "⏮️ Previous track",
                            "pause" or "stop" => "⏸️ Paused",
                            _ => "▶️ Playing"
                        };
                    }
                    
                    Debug.WriteLine($"[Spotify] API control failed: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Spotify] API control error: {ex.Message}");
                }
            }
            
            // Fallback to media keys
            byte key = actionLower switch
            {
                "next" or "skip" => VK_MEDIA_NEXT_TRACK,
                "previous" or "prev" => VK_MEDIA_PREV_TRACK,
                _ => VK_MEDIA_PLAY_PAUSE
            };
            SendMediaKey(key);
            
            return actionLower switch
            {
                "next" or "skip" => "⏭️ Next track",
                "previous" or "prev" => "⏮️ Previous track",
                _ => "▶️ Play/Pause"
            };
        }
        
        private static void SendMediaKey(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(100);
            keybd_event(vk, 0, 2, UIntPtr.Zero);
        }

        public static Task<string> OpenSpotifyAsync()
        {
            try
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
                return Task.FromResult("🎵 Opened Spotify");
            }
            catch
            {
                return Task.FromResult("❌ Could not open Spotify");
            }
        }
        
        /// <summary>
        /// Get current playback status
        /// </summary>
        public static async Task<string> GetCurrentlyPlayingAsync()
        {
            if (!await EnsureAccessTokenAsync())
                return "Not authorized. Say 'authorize Spotify' to connect.";
            
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/currently-playing");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                
                var response = await httpClient.SendAsync(request);
                
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return "Nothing currently playing.";
                
                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(responseText);
                    
                    var item = doc.RootElement.GetProperty("item");
                    var name = item.GetProperty("name").GetString();
                    var artist = item.GetProperty("artists")[0].GetProperty("name").GetString();
                    var isPlaying = doc.RootElement.GetProperty("is_playing").GetBoolean();
                    
                    return isPlaying 
                        ? $"🎵 Now playing: {name} by {artist}"
                        : $"⏸️ Paused: {name} by {artist}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Get currently playing error: {ex.Message}");
            }
            
            return "Could not get playback status.";
        }

        public static async Task<List<string>> GetPlaylistTrackQueriesAsync(string playlistId)
        {
            var results = new List<string>();
            playlistId = (playlistId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(playlistId)) return results;
            if (!await EnsureAccessTokenAsync()) return results;

            try
            {
                var offset = 0;
                const int limit = 100;

                while (true)
                {
                    var url = $"https://api.spotify.com/v1/playlists/{playlistId}/tracks?fields=items(track(name,artists(name))),next&limit={limit}&offset={offset}";
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                    var response = await httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode) break;

                    var responseText = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseText);

                    if (!doc.RootElement.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
                        break;

                    foreach (var itemEl in itemsEl.EnumerateArray())
                    {
                        if (!itemEl.TryGetProperty("track", out var trackEl) || trackEl.ValueKind != JsonValueKind.Object)
                            continue;
                        var name = trackEl.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? (nEl.GetString() ?? "") : "";
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var artistName = "";
                        if (trackEl.TryGetProperty("artists", out var aEl) && aEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var ar in aEl.EnumerateArray())
                            {
                                if (ar.ValueKind != JsonValueKind.Object) continue;
                                var an = ar.TryGetProperty("name", out var anEl) && anEl.ValueKind == JsonValueKind.String ? (anEl.GetString() ?? "") : "";
                                if (!string.IsNullOrWhiteSpace(an))
                                {
                                    artistName = an.Trim();
                                    break;
                                }
                            }
                        }

                        var q = string.IsNullOrWhiteSpace(artistName) ? name.Trim() : $"{artistName} - {name.Trim()}";
                        results.Add(q);
                    }

                    if (doc.RootElement.TryGetProperty("next", out var nextEl) && nextEl.ValueKind == JsonValueKind.Null)
                        break;
                    if (!doc.RootElement.TryGetProperty("next", out nextEl) || nextEl.ValueKind == JsonValueKind.Null)
                        break;

                    offset += limit;
                    if (offset > 10_000) break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Spotify] Playlist fetch error: {ex.Message}");
            }

            return results
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
