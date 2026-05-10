using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Settings;

namespace AtlasAI.Tools
{
    /// <summary>
    /// AI Image Generator using OpenAI DALL-E API
    /// Supports: "generate image of X", "create picture of X", "draw X"
    /// </summary>
    public static class ImageGeneratorTool
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        private static string? _apiKey;
        private static readonly string ImagesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "GeneratedImages");

        /// <summary>
        /// Generate an image from a text prompt using DALL-E
        /// </summary>
        public static async Task<ImageGenerationResult> GenerateImageAsync(string prompt, string size = "1024x1024", string quality = "standard", System.Threading.CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                LoadApiKey();
                
                if (string.IsNullOrEmpty(_apiKey))
                {
                    return new ImageGenerationResult
                    {
                        Success = false,
                        Error = "AI not configured. This installation is locked to admin configuration."
                    };
                }

                Debug.WriteLine($"[ImageGenerator] Generating image: {prompt}");

                // Ensure images directory exists
                if (!Directory.Exists(ImagesPath))
                    Directory.CreateDirectory(ImagesPath);

                // Build request
                var requestBody = new
                {
                    model = "dall-e-3",
                    prompt = prompt,
                    n = 1,
                    size = size,
                    quality = quality,
                    response_format = "url"
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations");
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = content;

                var response = await httpClient.SendAsync(request, ct);
                var responseText = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ImageGenerator] API error: {responseText}");
                    
                    // Parse error message
                    try
                    {
                        using var errorDoc = JsonDocument.Parse(responseText);
                        var errorMsg = errorDoc.RootElement.GetProperty("error").GetProperty("message").GetString();
                        return new ImageGenerationResult
                        {
                            Success = false,
                            Error = $"DALL-E error: {errorMsg}"
                        };
                    }
                    catch
                    {
                        return new ImageGenerationResult
                        {
                            Success = false,
                            Error = $"API error ({response.StatusCode}): {responseText.Substring(0, Math.Min(200, responseText.Length))}"
                        };
                    }
                }

                // Parse response
                using var doc = JsonDocument.Parse(responseText);
                var imageUrl = doc.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("url")
                    .GetString();

                var revisedPrompt = "";
                if (doc.RootElement.GetProperty("data")[0].TryGetProperty("revised_prompt", out var revisedProp))
                {
                    revisedPrompt = revisedProp.GetString() ?? "";
                }

                if (string.IsNullOrEmpty(imageUrl))
                {
                    return new ImageGenerationResult
                    {
                        Success = false,
                        Error = "No image URL in response"
                    };
                }

                // Download the image
                var imageBytes = await httpClient.GetByteArrayAsync(imageUrl, ct);
                
                // Save to file
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var safePrompt = SanitizeFilename(prompt);
                var filename = $"{timestamp}_{safePrompt}.png";
                var filePath = Path.Combine(ImagesPath, filename);
                
                await File.WriteAllBytesAsync(filePath, imageBytes);
                Debug.WriteLine($"[ImageGenerator] Image saved to: {filePath}");

                return new ImageGenerationResult
                {
                    Success = true,
                    ImagePath = filePath,
                    ImageUrl = imageUrl,
                    Prompt = prompt,
                    RevisedPrompt = revisedPrompt
                };
            }
            catch (TaskCanceledException)
            {
                return new ImageGenerationResult
                {
                    Success = false,
                    Error = "Image generation timed out. Try a simpler prompt."
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageGenerator] Error: {ex.Message}");
                return new ImageGenerationResult
                {
                    Success = false,
                    Error = $"Failed to generate image: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Extract the image prompt from user message
        /// </summary>
        public static string ExtractPrompt(string message)
        {
            var lower = message.ToLower();
            
            // Remove common prefixes
            var prefixes = new[]
            {
                "generate an image of ", "generate image of ", "generate a picture of ", "generate picture of ",
                "create an image of ", "create image of ", "create a picture of ", "create picture of ",
                "draw an image of ", "draw image of ", "draw a picture of ", "draw picture of ",
                "make an image of ", "make image of ", "make a picture of ", "make picture of ",
                "generate ", "create ", "draw ", "make ",
                "an image of ", "image of ", "a picture of ", "picture of ",
                "paint ", "illustrate ", "design "
            };

            var prompt = message;
            foreach (var prefix in prefixes)
            {
                if (lower.StartsWith(prefix))
                {
                    prompt = message.Substring(prefix.Length);
                    break;
                }
                
                // Also check for "of" in the middle
                var idx = lower.IndexOf(prefix);
                if (idx >= 0)
                {
                    prompt = message.Substring(idx + prefix.Length);
                    break;
                }
            }

            // Clean up
            prompt = prompt.Trim().TrimEnd('.', '!', '?');
            
            return prompt;
        }

        /// <summary>
        /// Check if a message is an image generation request
        /// </summary>
        public static bool IsImageGenerationRequest(string message)
        {
            var lower = message.ToLower();
            
            // Must contain generation keywords
            var hasGenerationKeyword = lower.Contains("generate") || lower.Contains("create") || 
                                       lower.Contains("draw") || lower.Contains("make") ||
                                       lower.Contains("paint") || lower.Contains("illustrate") ||
                                       lower.Contains("design");
            
            // Must contain image keywords
            var hasImageKeyword = lower.Contains("image") || lower.Contains("picture") || 
                                  lower.Contains("photo") || lower.Contains("art") ||
                                  lower.Contains("illustration") || lower.Contains("drawing") ||
                                  lower.Contains("artwork") || lower.Contains("graphic");
            
            // Exclude code/file related requests - these should NOT trigger image generation
            var isCodeRequest = lower.Contains("script") || lower.Contains("code") || 
                               lower.Contains("function") || lower.Contains("class") ||
                               lower.Contains("program") || lower.Contains("file") ||
                               lower.Contains(".py") || lower.Contains(".cs") || 
                               lower.Contains(".js") || lower.Contains(".ts") ||
                               lower.Contains(".html") || lower.Contains(".css") ||
                               lower.Contains("python") || lower.Contains("javascript") ||
                               lower.Contains("csharp") || lower.Contains("c#");
            
            if (isCodeRequest)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageGenerator] Skipping - detected as code request: '{message}'");
                return false;
            }
            
            // Specific image patterns - only for actual image requests
            var hasPattern = lower.StartsWith("draw ") || lower.StartsWith("paint ") ||
                            lower.Contains("generate an image") || lower.Contains("create an image") ||
                            lower.Contains("generate a picture") || lower.Contains("create a picture") ||
                            lower.Contains("generate me an image") || lower.Contains("create me an image") ||
                            lower.Contains("make me an image") || lower.Contains("make me a picture") ||
                            lower.Contains("draw me ") || lower.Contains("paint me ");
            
            var result = (hasGenerationKeyword && hasImageKeyword) || hasPattern;
            System.Diagnostics.Debug.WriteLine($"[ImageGenerator] IsImageGenerationRequest('{message}'): gen={hasGenerationKeyword}, img={hasImageKeyword}, pattern={hasPattern} => {result}");
            return result;
        }

        private static string SanitizeFilename(string prompt)
        {
            // Take first 30 chars and remove invalid characters
            var safe = prompt.Length > 30 ? prompt.Substring(0, 30) : prompt;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }
            return safe.Replace(' ', '_');
        }

        private static void LoadApiKey()
        {
            if (_apiKey != null) return;
            try
            {
                if (SettingsStore.TryGetAiProviderKey("openai", out var key))
                    _apiKey = key;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageGenerator] Failed to load API key: {ex.Message}");
            }
        }

        /// <summary>
        /// Open the generated images folder
        /// </summary>
        public static void OpenImagesFolder()
        {
            if (!Directory.Exists(ImagesPath))
                Directory.CreateDirectory(ImagesPath);
            Process.Start("explorer.exe", ImagesPath);
        }
    }

    public class ImageGenerationResult
    {
        public bool Success { get; set; }
        public string? ImagePath { get; set; }
        public string? ImageUrl { get; set; }
        public string? Prompt { get; set; }
        public string? RevisedPrompt { get; set; }
        public string? Error { get; set; }
    }
}
