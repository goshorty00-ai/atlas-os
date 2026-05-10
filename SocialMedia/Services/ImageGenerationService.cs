using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AtlasAI.Core;
using AtlasAI.Settings;

namespace AtlasAI.SocialMedia.Services
{
    /// <summary>
    /// AI Image Generation Service for Social Media Posts
    /// Supports OpenAI DALL-E and other providers
    /// </summary>
    public class ImageGenerationService
    {
        private readonly HttpClient _httpClient;
        private string _openAiApiKey;
        private string _provider = "openai"; // openai, stability, local

        public ImageGenerationService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
            LoadApiKey();
        }

        private void LoadApiKey()
        {
            try
            {
                if (SettingsStore.TryGetAiProviderKey("openai", out var key))
                {
                    _openAiApiKey = key;
                    if (!string.IsNullOrEmpty(_openAiApiKey))
                        return;
                }

                _openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageGenerationService] Error loading API key: {ex.Message}");
            }
        }

        public void SetApiKey(string apiKey)
        {
            _openAiApiKey = apiKey;

            try
            {
                SettingsStore.SetAiProviderKey("openai", apiKey ?? string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageGenerationService] Error saving API key: {ex.Message}");
            }
        }

        public bool HasApiKey => !string.IsNullOrEmpty(_openAiApiKey);

        /// <summary>
        /// Generate an image using AI based on a text prompt
        /// </summary>
        public async Task<ImageGenerationResult> GenerateImageAsync(ImageGenerationRequest request)
        {
            if (string.IsNullOrEmpty(_openAiApiKey))
            {
                return new ImageGenerationResult
                {
                    Success = false,
                    ErrorMessage = "OpenAI API key not configured. Please set your API key in settings."
                };
            }

            try
            {
                // Build optimized prompt for social media
                var optimizedPrompt = BuildSocialMediaPrompt(request);

                var requestBody = new
                {
                    model = "dall-e-3",
                    prompt = optimizedPrompt,
                    n = 1,
                    size = GetImageSize(request.Size),
                    quality = request.HighQuality ? "hd" : "standard",
                    style = request.Style == ImageStyle.Natural ? "natural" : "vivid"
                };

                var json = JsonSerializer.Serialize(requestBody);
                
                // Create fresh request to avoid header issues
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations");
                httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _openAiApiKey.Trim());

                var response = await _httpClient.SendAsync(httpRequest);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Parse error for better message
                    var errorMsg = responseJson;
                    try
                    {
                        using var errorDoc = JsonDocument.Parse(responseJson);
                        if (errorDoc.RootElement.TryGetProperty("error", out var errorObj))
                        {
                            if (errorObj.TryGetProperty("message", out var msg))
                                errorMsg = msg.GetString();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ImageGenerationService] Error parsing API error response: {ex.Message}");
                    }
                    
                    return new ImageGenerationResult
                    {
                        Success = false,
                        ErrorMessage = $"API Error ({response.StatusCode}): {errorMsg}"
                    };
                }

                using var doc = JsonDocument.Parse(responseJson);
                var imageUrl = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString();
                var revisedPrompt = doc.RootElement.GetProperty("data")[0].TryGetProperty("revised_prompt", out var rp) 
                    ? rp.GetString() : optimizedPrompt;

                // Download the image with clean client
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                var localPath = await SaveImageLocallyAsync(imageBytes, request.Platform);

                return new ImageGenerationResult
                {
                    Success = true,
                    ImageUrl = imageUrl,
                    LocalPath = localPath,
                    RevisedPrompt = revisedPrompt,
                    ImageBytes = imageBytes
                };
            }
            catch (Exception ex)
            {
                return new ImageGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"Generation failed: {ex.Message}"
                };
            }
        }

        private string BuildSocialMediaPrompt(ImageGenerationRequest request)
        {
            // Keep prompt simple - don't add extra instructions that might cause collages
            return request.Prompt;
        }

        private string GetImageSize(ImageSize size)
        {
            return size switch
            {
                ImageSize.Square => "1024x1024",
                ImageSize.Portrait => "1024x1792",
                ImageSize.Landscape => "1792x1024",
                _ => "1024x1024"
            };
        }

        private async Task<string> SaveImageLocallyAsync(byte[] imageBytes, string platform)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                "AtlasAI", "SocialMedia", platform ?? "General");
            Directory.CreateDirectory(dir);

            var filename = $"generated_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var path = Path.Combine(dir, filename);

            await File.WriteAllBytesAsync(path, imageBytes);
            return path;
        }

        /// <summary>
        /// Generate multiple image variations for A/B testing
        /// </summary>
        public async Task<ImageGenerationResult[]> GenerateVariationsAsync(ImageGenerationRequest request, int count = 3)
        {
            var results = new ImageGenerationResult[count];
            var styles = new[] { "vivid and colorful", "minimalist and clean", "professional and polished" };

            for (int i = 0; i < count; i++)
            {
                var varRequest = new ImageGenerationRequest
                {
                    Prompt = request.Prompt,
                    Platform = request.Platform,
                    Size = request.Size,
                    StyleModifier = styles[i % styles.Length],
                    BrandColors = request.BrandColors,
                    HighQuality = request.HighQuality,
                    Style = i % 2 == 0 ? ImageStyle.Vivid : ImageStyle.Natural
                };

                results[i] = await GenerateImageAsync(varRequest);
                
                // Small delay between requests to avoid rate limiting
                if (i < count - 1)
                    await Task.Delay(500);
            }

            return results;
        }

        /// <summary>
        /// Generate a thumbnail optimized for video content
        /// </summary>
        public async Task<ImageGenerationResult> GenerateThumbnailAsync(string videoTitle, string platform)
        {
            var request = new ImageGenerationRequest
            {
                Prompt = $"Eye-catching video thumbnail for: {videoTitle}. Bold text-friendly composition with clear focal point, high contrast, professional quality",
                Platform = platform,
                Size = ImageSize.Landscape,
                HighQuality = true,
                Style = ImageStyle.Vivid
            };

            return await GenerateImageAsync(request);
        }

        /// <summary>
        /// Generate product/promotional image
        /// </summary>
        public async Task<ImageGenerationResult> GeneratePromoImageAsync(string productDescription, string callToAction, string platform)
        {
            var request = new ImageGenerationRequest
            {
                Prompt = $"Professional promotional image for: {productDescription}. Clean background, product-focused, space for text overlay saying '{callToAction}'",
                Platform = platform,
                Size = platform?.ToLower() == "tiktok" ? ImageSize.Portrait : ImageSize.Square,
                HighQuality = true,
                Style = ImageStyle.Vivid
            };

            return await GenerateImageAsync(request);
        }
    }

    public class ImageGenerationRequest
    {
        public string Prompt { get; set; }
        public string Platform { get; set; }
        public ImageSize Size { get; set; } = ImageSize.Square;
        public string StyleModifier { get; set; }
        public string BrandColors { get; set; }
        public bool HighQuality { get; set; } = true;
        public ImageStyle Style { get; set; } = ImageStyle.Vivid;
    }

    public class ImageGenerationResult
    {
        public bool Success { get; set; }
        public string ImageUrl { get; set; }
        public string LocalPath { get; set; }
        public string RevisedPrompt { get; set; }
        public string ErrorMessage { get; set; }
        public byte[] ImageBytes { get; set; }

        public BitmapImage GetBitmapImage()
        {
            if (ImageBytes == null || ImageBytes.Length == 0)
                return null;

            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(ImageBytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            return bitmap;
        }
    }

    public enum ImageSize
    {
        Square,      // 1024x1024 - Instagram feed, Facebook
        Portrait,    // 1024x1792 - TikTok, Instagram Stories, Reels
        Landscape    // 1792x1024 - YouTube thumbnails, Facebook covers
    }

    public enum ImageStyle
    {
        Vivid,   // Bold, dramatic, hyper-real
        Natural  // More natural, less hyper-real
    }
}
