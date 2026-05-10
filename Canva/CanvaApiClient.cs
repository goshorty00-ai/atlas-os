using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Canva
{
    /// <summary>
    /// Canva Connect API Client
    /// Documentation: https://www.canva.dev/docs/connect/
    /// 
    /// IMPORTANT: This requires a Canva Connect API key from https://www.canva.com/developers/
    /// The API allows creating designs, uploading assets, and autofilling templates.
    /// </summary>
    public class CanvaApiClient
    {
        private static readonly HttpClient _httpClient = new();
        private const string BaseUrl = "https://api.canva.com/rest/v1";
        
        private string? _accessToken;
        private string? _clientId;
        private string? _clientSecret;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public bool IsConfigured => !string.IsNullOrEmpty(_accessToken) || 
                                    (!string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret));
        public bool HasValidToken => !string.IsNullOrEmpty(_accessToken) && DateTime.Now < _tokenExpiry;

        /// <summary>
        /// Configure with OAuth credentials (for full API access)
        /// </summary>
        public void Configure(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            Debug.WriteLine("[CanvaAPI] Configured with OAuth credentials");
        }

        /// <summary>
        /// Configure with direct access token (simpler setup)
        /// </summary>
        public void ConfigureWithToken(string accessToken, int expiresInSeconds = 3600)
        {
            _accessToken = accessToken;
            _tokenExpiry = DateTime.Now.AddSeconds(expiresInSeconds - 60); // 1 min buffer
            Debug.WriteLine("[CanvaAPI] Configured with access token");
        }

        /// <summary>
        /// Get OAuth authorization URL for user to grant access
        /// </summary>
        public string GetAuthorizationUrl(string redirectUri, string state)
        {
            var scopes = "design:content:read design:content:write design:meta:read asset:read asset:write";
            return $"https://www.canva.com/api/oauth/authorize?" +
                   $"client_id={Uri.EscapeDataString(_clientId ?? "")}" +
                   $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                   $"&response_type=code" +
                   $"&scope={Uri.EscapeDataString(scopes)}" +
                   $"&state={Uri.EscapeDataString(state)}";
        }

        /// <summary>
        /// Exchange authorization code for access token
        /// </summary>
        public async Task<bool> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct = default)
        {
            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = _clientId ?? "",
                    ["client_secret"] = _clientSecret ?? ""
                });

                var response = await _httpClient.PostAsync("https://api.canva.com/rest/v1/oauth/token", content, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    _accessToken = doc.RootElement.GetProperty("access_token").GetString();
                    var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
                    _tokenExpiry = DateTime.Now.AddSeconds(expiresIn - 60);
                    Debug.WriteLine("[CanvaAPI] Token obtained successfully");
                    return true;
                }
                Debug.WriteLine($"[CanvaAPI] Token exchange failed: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAPI] Token exchange error: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Create a new design from scratch
        /// </summary>
        public async Task<CanvaApiDesign?> CreateDesignAsync(CanvaDesignSpec spec, CancellationToken ct = default)
        {
            if (!HasValidToken) return null;

            try
            {
                var designType = MapDesignType(spec.DesignType);
                var request = new
                {
                    design_type = new { type = designType },
                    title = spec.Title,
                    asset_id = (string?)null // Optional: start from a template
                };

                var response = await PostAsync("/designs", request, ct);
                if (response != null)
                {
                    return new CanvaApiDesign
                    {
                        Id = response.RootElement.GetProperty("design").GetProperty("id").GetString() ?? "",
                        Title = spec.Title,
                        EditUrl = response.RootElement.GetProperty("design").TryGetProperty("urls", out var urls) 
                            ? urls.GetProperty("edit_url").GetString() ?? "" : "",
                        ViewUrl = response.RootElement.GetProperty("design").TryGetProperty("urls", out var viewUrls)
                            ? viewUrls.GetProperty("view_url").GetString() ?? "" : ""
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAPI] Create design error: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Create design from a template with autofill data
        /// </summary>
        public async Task<CanvaApiDesign?> CreateFromTemplateAsync(string brandTemplateId, Dictionary<string, string> autofillData, string title, CancellationToken ct = default)
        {
            if (!HasValidToken) return null;

            try
            {
                var dataItems = new List<object>();
                foreach (var kvp in autofillData)
                {
                    dataItems.Add(new { type = "text", name = kvp.Key, text = kvp.Value });
                }

                var request = new
                {
                    brand_template_id = brandTemplateId,
                    title = title,
                    data = dataItems
                };

                var response = await PostAsync("/autofills", request, ct);
                if (response != null && response.RootElement.TryGetProperty("job", out var job))
                {
                    var jobId = job.GetProperty("id").GetString();
                    // Poll for completion
                    return await WaitForAutofillJobAsync(jobId ?? "", ct);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAPI] Create from template error: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Upload an asset (image) to Canva
        /// </summary>
        public async Task<string?> UploadAssetAsync(byte[] imageData, string filename, CancellationToken ct = default)
        {
            if (!HasValidToken) return null;

            try
            {
                using var content = new MultipartFormDataContent();
                var imageContent = new ByteArrayContent(imageData);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "file", filename);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/assets/upload");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                request.Content = content;

                var response = await _httpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("job", out var job))
                    {
                        var jobId = job.GetProperty("id").GetString();
                        return await WaitForUploadJobAsync(jobId ?? "", ct);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAPI] Upload asset error: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// List user's designs
        /// </summary>
        public async Task<List<CanvaApiDesign>> ListDesignsAsync(int limit = 20, CancellationToken ct = default)
        {
            var designs = new List<CanvaApiDesign>();
            if (!HasValidToken) return designs;

            try
            {
                var response = await GetAsync($"/designs?ownership=owned&limit={limit}", ct);
                if (response != null && response.RootElement.TryGetProperty("items", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        designs.Add(new CanvaApiDesign
                        {
                            Id = item.GetProperty("id").GetString() ?? "",
                            Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                            EditUrl = item.TryGetProperty("urls", out var urls) && urls.TryGetProperty("edit_url", out var edit)
                                ? edit.GetString() ?? "" : "",
                            ThumbnailUrl = item.TryGetProperty("thumbnail", out var thumb) && thumb.TryGetProperty("url", out var thumbUrl)
                                ? thumbUrl.GetString() ?? "" : ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAPI] List designs error: {ex.Message}");
            }
            return designs;
        }

        /// <summary>
        /// Export a design to image/PDF
        /// </summary>
        public async Task<byte[]?> ExportDesignAsync(string designId, string format = "png", CancellationToken ct = default)
        {
            if (!HasValidToken) return null;

            try
            {
                var request = new { design_id = designId, format = format };
                var response = await PostAsync("/exports", request, ct);
                
                if (response != null && response.RootElement.TryGetProperty("job", out var job))
                {
                    var jobId = job.GetProperty("id").GetString();
                    var exportUrl = await WaitForExportJobAsync(jobId ?? "", ct);
                    
                    if (!string.IsNullOrEmpty(exportUrl))
                    {
                        return await _httpClient.GetByteArrayAsync(exportUrl, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAPI] Export design error: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Get brand templates available to the user
        /// </summary>
        public async Task<List<CanvaBrandTemplate>> GetBrandTemplatesAsync(CancellationToken ct = default)
        {
            var templates = new List<CanvaBrandTemplate>();
            if (!HasValidToken) return templates;

            try
            {
                var response = await GetAsync("/brand-templates", ct);
                if (response != null && response.RootElement.TryGetProperty("items", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        templates.Add(new CanvaBrandTemplate
                        {
                            Id = item.GetProperty("id").GetString() ?? "",
                            Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                            ThumbnailUrl = item.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() ?? "" : ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAPI] Get brand templates error: {ex.Message}");
            }
            return templates;
        }

        #region Private Helpers

        private async Task<JsonDocument?> GetAsync(string endpoint, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{endpoint}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            
            var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonDocument.Parse(json);
            }
            Debug.WriteLine($"[CanvaAPI] GET {endpoint} failed: {response.StatusCode}");
            return null;
        }

        private async Task<JsonDocument?> PostAsync(string endpoint, object body, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{endpoint}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(json);
            }
            Debug.WriteLine($"[CanvaAPI] POST {endpoint} failed: {response.StatusCode} - {json}");
            return null;
        }

        private async Task<CanvaApiDesign?> WaitForAutofillJobAsync(string jobId, CancellationToken ct)
        {
            for (int i = 0; i < 30; i++) // Max 30 seconds
            {
                await Task.Delay(1000, ct);
                var response = await GetAsync($"/autofills/{jobId}", ct);
                if (response != null)
                {
                    var status = response.RootElement.GetProperty("job").GetProperty("status").GetString();
                    if (status == "success" && response.RootElement.TryGetProperty("job", out var job) 
                        && job.TryGetProperty("result", out var result))
                    {
                        return new CanvaApiDesign
                        {
                            Id = result.GetProperty("design").GetProperty("id").GetString() ?? "",
                            EditUrl = result.GetProperty("design").TryGetProperty("urls", out var urls)
                                ? urls.GetProperty("edit_url").GetString() ?? "" : ""
                        };
                    }
                    if (status == "failed") break;
                }
            }
            return null;
        }

        private async Task<string?> WaitForUploadJobAsync(string jobId, CancellationToken ct)
        {
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000, ct);
                var response = await GetAsync($"/assets/upload/{jobId}", ct);
                if (response != null)
                {
                    var status = response.RootElement.GetProperty("job").GetProperty("status").GetString();
                    if (status == "success")
                    {
                        return response.RootElement.GetProperty("job").GetProperty("asset").GetProperty("id").GetString();
                    }
                    if (status == "failed") break;
                }
            }
            return null;
        }

        private async Task<string?> WaitForExportJobAsync(string jobId, CancellationToken ct)
        {
            for (int i = 0; i < 60; i++) // Exports can take longer
            {
                await Task.Delay(1000, ct);
                var response = await GetAsync($"/exports/{jobId}", ct);
                if (response != null)
                {
                    var status = response.RootElement.GetProperty("job").GetProperty("status").GetString();
                    if (status == "success")
                    {
                        return response.RootElement.GetProperty("job").GetProperty("urls")
                            .EnumerateArray().First().GetProperty("url").GetString();
                    }
                    if (status == "failed") break;
                }
            }
            return null;
        }

        private string MapDesignType(CanvaDesignType type) => type switch
        {
            CanvaDesignType.InstagramPost => "instagram_post_square",
            CanvaDesignType.InstagramStory => "instagram_story",
            CanvaDesignType.FacebookPost => "facebook_post",
            CanvaDesignType.TwitterPost => "twitter_post",
            CanvaDesignType.LinkedInPost => "linkedin_post",
            CanvaDesignType.YouTubeThumbnail => "youtube_thumbnail",
            CanvaDesignType.Presentation => "presentation",
            CanvaDesignType.Logo => "logo",
            CanvaDesignType.BusinessCard => "business_card",
            CanvaDesignType.Flyer => "flyer",
            CanvaDesignType.Poster => "poster",
            CanvaDesignType.Resume => "resume",
            CanvaDesignType.Infographic => "infographic",
            _ => "custom"
        };

        #endregion
    }

    /// <summary>
    /// Represents a design from the Canva API
    /// </summary>
    public class CanvaApiDesign
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string EditUrl { get; set; } = "";
        public string ViewUrl { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
    }

    /// <summary>
    /// Represents a brand template from Canva
    /// </summary>
    public class CanvaBrandTemplate
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
    }
}
