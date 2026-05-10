using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia.Services
{
    /// <summary>
    /// Platform-specific publishing service
    /// Uses APIs where available, falls back to UI pre-fill
    /// </summary>
    public class PlatformPublisherService
    {
        private static readonly HttpClient _httpClient = new();
        private readonly string _configPath;
        private Dictionary<string, string> _apiKeys = new();
        
        public PlatformPublisherService()
        {
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "social_api_keys.json");
            LoadApiKeys();
        }
        
        /// <summary>
        /// Publish content to a platform
        /// </summary>
        public async Task<PublishResult> PublishAsync(SocialContent content, SocialPlatform platform)
        {
            // Log the action
            LogAction($"Publishing to {platform}: {content.Title}");
            
            return platform switch
            {
                SocialPlatform.TikTok => await PublishToTikTokAsync(content),
                SocialPlatform.Instagram => await PublishToInstagramAsync(content),
                SocialPlatform.Facebook => await PublishToFacebookAsync(content),
                SocialPlatform.YouTube => await PublishToYouTubeAsync(content),
                _ => new PublishResult { Success = false, Message = "Unknown platform" }
            };
        }
        
        /// <summary>
        /// Open platform with content pre-filled for manual posting
        /// </summary>
        public async Task<PublishResult> OpenForManualPostAsync(SocialContent content, SocialPlatform platform)
        {
            var url = GetPlatformCreateUrl(platform, content);
            
            try
            {
                // Copy content to clipboard for easy pasting
                var clipboardText = BuildClipboardContent(content);
                await SetClipboardAsync(clipboardText);
                
                // Open the platform
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                
                return new PublishResult
                {
                    Success = true,
                    Message = $"Opened {platform} - content copied to clipboard. Paste to complete posting.",
                    RequiresManualAction = true
                };
            }
            catch (Exception ex)
            {
                return new PublishResult
                {
                    Success = false,
                    Message = $"Failed to open {platform}: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Check if API access is configured for a platform
        /// </summary>
        public bool HasApiAccess(SocialPlatform platform)
        {
            var keyName = platform switch
            {
                SocialPlatform.TikTok => "tiktok_access_token",
                SocialPlatform.Instagram => "instagram_access_token",
                SocialPlatform.Facebook => "facebook_access_token",
                SocialPlatform.YouTube => "youtube_api_key",
                _ => ""
            };
            
            return _apiKeys.ContainsKey(keyName) && !string.IsNullOrEmpty(_apiKeys[keyName]);
        }
        
        /// <summary>
        /// Configure API key for a platform
        /// </summary>
        public void SetApiKey(SocialPlatform platform, string key)
        {
            var keyName = platform switch
            {
                SocialPlatform.TikTok => "tiktok_access_token",
                SocialPlatform.Instagram => "instagram_access_token",
                SocialPlatform.Facebook => "facebook_access_token",
                SocialPlatform.YouTube => "youtube_api_key",
                _ => ""
            };
            
            if (!string.IsNullOrEmpty(keyName))
            {
                _apiKeys[keyName] = key;
                SaveApiKeys();
            }
        }
        
        #region Platform-Specific Publishing
        
        private async Task<PublishResult> PublishToTikTokAsync(SocialContent content)
        {
            // TikTok requires video content and OAuth
            // For now, open TikTok Studio for manual posting
            if (!HasApiAccess(SocialPlatform.TikTok))
            {
                return await OpenForManualPostAsync(content, SocialPlatform.TikTok);
            }
            
            // TODO: Implement TikTok API posting when user provides credentials
            // TikTok Content Posting API requires business account
            return await OpenForManualPostAsync(content, SocialPlatform.TikTok);
        }
        
        private async Task<PublishResult> PublishToInstagramAsync(SocialContent content)
        {
            // Instagram Graph API requires Facebook Business account
            if (!HasApiAccess(SocialPlatform.Instagram))
            {
                return await OpenForManualPostAsync(content, SocialPlatform.Instagram);
            }
            
            // TODO: Implement Instagram Graph API posting
            // Requires: Instagram Business/Creator account linked to Facebook Page
            return await OpenForManualPostAsync(content, SocialPlatform.Instagram);
        }
        
        private async Task<PublishResult> PublishToFacebookAsync(SocialContent content)
        {
            if (!HasApiAccess(SocialPlatform.Facebook))
            {
                return await OpenForManualPostAsync(content, SocialPlatform.Facebook);
            }
            
            try
            {
                var accessToken = _apiKeys["facebook_access_token"];
                var pageId = _apiKeys.GetValueOrDefault("facebook_page_id", "me");
                
                var postData = new Dictionary<string, string>
                {
                    ["message"] = content.Caption,
                    ["access_token"] = accessToken
                };
                
                var formContent = new FormUrlEncodedContent(postData);
                var response = await _httpClient.PostAsync(
                    $"https://graph.facebook.com/v18.0/{pageId}/feed",
                    formContent);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    return new PublishResult
                    {
                        Success = true,
                        Message = "Posted to Facebook successfully!",
                        PostId = JsonDocument.Parse(result).RootElement.GetProperty("id").GetString()
                    };
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return new PublishResult
                    {
                        Success = false,
                        Message = $"Facebook API error: {error}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new PublishResult
                {
                    Success = false,
                    Message = $"Facebook posting failed: {ex.Message}"
                };
            }
        }
        
        private async Task<PublishResult> PublishToYouTubeAsync(SocialContent content)
        {
            // YouTube requires video upload via Data API
            // For text posts, open YouTube Studio
            if (!HasApiAccess(SocialPlatform.YouTube) || content.Type != ContentType.Video)
            {
                return await OpenForManualPostAsync(content, SocialPlatform.YouTube);
            }
            
            // TODO: Implement YouTube Data API v3 video upload
            // Requires OAuth 2.0 and video file
            return await OpenForManualPostAsync(content, SocialPlatform.YouTube);
        }
        
        #endregion
        
        #region Helper Methods
        
        private string GetPlatformCreateUrl(SocialPlatform platform, SocialContent content)
        {
            var encodedCaption = HttpUtility.UrlEncode(content.Caption);
            
            return platform switch
            {
                SocialPlatform.TikTok => "https://www.tiktok.com/creator-center/upload",
                SocialPlatform.Instagram => "https://www.instagram.com/",
                SocialPlatform.Facebook => $"https://www.facebook.com/sharer/sharer.php?quote={encodedCaption}",
                SocialPlatform.YouTube => "https://studio.youtube.com/",
                _ => ""
            };
        }
        
        private string BuildClipboardContent(SocialContent content)
        {
            var sb = new StringBuilder();
            
            if (!string.IsNullOrEmpty(content.Hook))
                sb.AppendLine(content.Hook);
            
            if (!string.IsNullOrEmpty(content.Body))
            {
                sb.AppendLine();
                sb.AppendLine(content.Body);
            }
            
            if (!string.IsNullOrEmpty(content.CallToAction))
            {
                sb.AppendLine();
                sb.AppendLine(content.CallToAction);
            }
            
            if (content.Hashtags.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Join(" ", content.Hashtags));
            }
            
            return sb.ToString();
        }
        
        private async Task SetClipboardAsync(string text)
        {
            await Task.Run(() =>
            {
                var thread = new System.Threading.Thread(() =>
                {
                    System.Windows.Clipboard.SetText(text);
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join();
            });
        }
        
        private void LogAction(string action)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "SocialMedia", "publish_log.txt");
            
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {action}\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlatformPublisherService] Error writing publish log: {ex.Message}");
            }
        }
        
        private void LoadApiKeys()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _apiKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlatformPublisherService] Error loading API keys: {ex.Message}");
            }
        }
        
        private void SaveApiKeys()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                var json = JsonSerializer.Serialize(_apiKeys, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlatformPublisherService] Error saving API keys: {ex.Message}");
            }
        }
        
        #endregion
    }
    
    public class PublishResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? PostId { get; set; }
        public string? PostUrl { get; set; }
        public bool RequiresManualAction { get; set; }
    }
}
