using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using AtlasAI.Core;
using AtlasAI.UI;

namespace AtlasAI.Tools
{
    public static class WebSearchTool
    {
        internal sealed record WebSearchExecutionResult(
            string UserFacingText,
            string EvidenceText,
            bool IsGrounded,
            bool WasDenied,
            bool ExternalLookupPerformed);

        private static readonly HttpClient httpClient;
        public static event Action<string>? SearchProgressChanged;

        internal static Func<string, Task<OnlineAccessResult>>? RequestAccessOverride { get; set; }
        internal static Func<string, CancellationToken, Task<string>>? InstantAnswerOverride { get; set; }
        internal static Func<string, CancellationToken, Task<string>>? SearchResultsOverride { get; set; }

        internal static void ResetTestOverrides()
        {
            RequestAccessOverride = null;
            InstantAnswerOverride = null;
            SearchResultsOverride = null;
        }

        static WebSearchTool()
        {
            httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // Set a browser-like user agent to avoid blocks
            httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            // Register consent handler
            OnlineModeManager.Instance.ConsentRequested += HandleConsentRequestAsync;
        }

        /// <summary>
        /// Handle consent request by showing dialog on UI thread.
        /// </summary>
        private static async Task<OnlineConsentResult> HandleConsentRequestAsync(string query)
        {
            var result = new OnlineConsentResult { Decision = OnlineConsentDecision.Denied };
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                result = OnlineConsentDialog.ShowConsent(Application.Current.MainWindow);
            });
            
            return result;
        }

        /// <summary>
        /// Search the web and return results WITH LINKS
        /// Uses DuckDuckGo HTML search to get real results.
        /// Requires online mode consent.
        /// </summary>
        public static async Task<string> SearchAsync(string query, CancellationToken ct = default)
            => (await SearchWithMetadataAsync(query, ct)).UserFacingText;

        internal static async Task<WebSearchExecutionResult> SearchWithMetadataAsync(string query, CancellationToken ct = default)
        {
            ReportSearchProgress("Checking online access...");

            // Check online mode consent
            var accessResult = RequestAccessOverride != null
                ? await RequestAccessOverride(query)
                : await OnlineModeManager.Instance.RequestAccessAsync(query);
            
            if (accessResult == OnlineAccessResult.Denied)
            {
                ReportSearchProgress("Online access denied. Staying offline.");
                Debug.WriteLine($"[WebSearchTool] Online access denied for: {query}");
                var denialText = $"WEB_SEARCH_DENIED · NON_GROUNDED\n{OnlineModeManager.GetDeniedMessage()}";
                return new WebSearchExecutionResult(
                    denialText,
                    string.Empty,
                    IsGrounded: false,
                    WasDenied: true,
                    ExternalLookupPerformed: false);
            }
            
            if (accessResult == OnlineAccessResult.PendingConsent)
            {
                ReportSearchProgress("Waiting for permission to search online...");
                const string pendingText = "I can do that with online access enabled. Would you like me to look it up?";
                return new WebSearchExecutionResult(
                    pendingText,
                    string.Empty,
                    IsGrounded: false,
                    WasDenied: false,
                    ExternalLookupPerformed: false);
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                Debug.WriteLine($"[WebSearchTool] Searching (online access granted): {query}");
                
                // First try DuckDuckGo Instant Answer for quick facts
                ReportSearchProgress("Checking quick answers...");
                var instantResult = InstantAnswerOverride != null
                    ? await InstantAnswerOverride(query, ct)
                    : await TryInstantAnswerAsync(query, ct);
                if (!string.IsNullOrEmpty(instantResult))
                {
                    ReportSearchProgress("Quick answer found.");
                    return new WebSearchExecutionResult(
                        instantResult,
                        instantResult,
                        IsGrounded: true,
                        WasDenied: false,
                        ExternalLookupPerformed: true);
                }

                // Scrape DuckDuckGo HTML for real search results with links
                ReportSearchProgress("Reading search results...");
                var searchResults = SearchResultsOverride != null
                    ? await SearchResultsOverride(query, ct)
                    : await ScrapeSearchResultsAsync(query, ct);
                if (!string.IsNullOrEmpty(searchResults))
                {
                    ReportSearchProgress("Search results loaded.");
                    return new WebSearchExecutionResult(
                        searchResults,
                        searchResults,
                        IsGrounded: true,
                        WasDenied: false,
                        ExternalLookupPerformed: true);
                }

                // Fallback: return a helpful message with search link
                ReportSearchProgress("Direct results unavailable. Creating browser search link...");
                var searchUrl = $"https://www.google.com/search?q={HttpUtility.UrlEncode(query)}";
                var fallbackText = $"🔍 I couldn't fetch results directly. Here's a search link:\n\n{query}\n🔗 {searchUrl}";
                return new WebSearchExecutionResult(
                    fallbackText,
                    string.Empty,
                    IsGrounded: false,
                    WasDenied: false,
                    ExternalLookupPerformed: true);
            }
            catch (OperationCanceledException)
            {
                ReportSearchProgress("Online research cancelled.");
                return new WebSearchExecutionResult(
                    "CANCELLED · OPERATION STOPPED",
                    string.Empty,
                    IsGrounded: false,
                    WasDenied: false,
                    ExternalLookupPerformed: false);
            }
            catch (Exception ex)
            {
                ReportSearchProgress("Search hit an error. Falling back to browser link...");
                Debug.WriteLine($"[WebSearchTool] Error: {ex.Message}");
                var searchUrl = $"https://www.google.com/search?q={HttpUtility.UrlEncode(query)}";
                var errorText = $"🔍 Search for {query}:\n🔗 {searchUrl}";
                return new WebSearchExecutionResult(
                    errorText,
                    string.Empty,
                    IsGrounded: false,
                    WasDenied: false,
                    ExternalLookupPerformed: true);
            }
        }

        private static void ReportSearchProgress(string message)
        {
            try
            {
                SearchProgressChanged?.Invoke(message);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Try DuckDuckGo Instant Answer API for quick facts
        /// </summary>
        private static async Task<string> TryInstantAnswerAsync(string query, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var encodedQuery = HttpUtility.UrlEncode(query);
                var url = $"https://api.duckduckgo.com/?q={encodedQuery}&format=json&no_html=1";

                var response = await httpClient.GetAsync(url, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                var result = "";

                // Get abstract/summary with source link
                if (root.TryGetProperty("Abstract", out var abstractProp))
                {
                    var abstractText = abstractProp.GetString();
                    if (!string.IsNullOrEmpty(abstractText))
                    {
                        result += $"Summary: {abstractText}\n";
                        
                        // Get source URL
                        if (root.TryGetProperty("AbstractURL", out var urlProp))
                        {
                            var sourceUrl = urlProp.GetString();
                            if (!string.IsNullOrEmpty(sourceUrl))
                                result += $"🔗 Source: {sourceUrl}\n";
                        }
                        result += "\n";
                    }
                }

                // Get direct answer
                if (root.TryGetProperty("Answer", out var answerProp))
                {
                    var answer = answerProp.GetString();
                    if (!string.IsNullOrEmpty(answer))
                        result += $"Answer: {answer}\n\n";
                }

                // Get related topics with links
                if (root.TryGetProperty("RelatedTopics", out var topics) && topics.GetArrayLength() > 0)
                {
                    result += "Related:\n";
                    var count = 0;
                    foreach (var topic in topics.EnumerateArray())
                    {
                        if (count >= 5) break;
                        if (topic.TryGetProperty("Text", out var text) && 
                            topic.TryGetProperty("FirstURL", out var topicUrl))
                        {
                            var topicText = text.GetString();
                            var link = topicUrl.GetString();
                            if (!string.IsNullOrEmpty(topicText))
                            {
                                // Truncate long text
                                if (topicText.Length > 150)
                                    topicText = topicText.Substring(0, 147) + "...";
                                result += $"• {topicText}\n";
                                if (!string.IsNullOrEmpty(link))
                                    result += $"  🔗 {link}\n";
                                count++;
                            }
                        }
                    }
                }

                return result;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Scrape DuckDuckGo HTML search for real results with links
        /// </summary>
        private static async Task<string> ScrapeSearchResultsAsync(string query, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var encodedQuery = HttpUtility.UrlEncode(query);
                // Use DuckDuckGo HTML version (no JavaScript required)
                var url = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

                var response = await httpClient.GetAsync(url, ct);
                var html = await response.Content.ReadAsStringAsync(ct);

                var results = new List<SearchResult>();

                // Parse search results from HTML
                // DuckDuckGo HTML format: <a class="result__a" href="...">Title</a>
                // and <a class="result__snippet">Description</a>
                var resultPattern = @"<a[^>]*class=""result__a""[^>]*href=""([^""]+)""[^>]*>([^<]+)</a>";
                var snippetPattern = @"<a[^>]*class=""result__snippet""[^>]*>([^<]+)</a>";

                var resultMatches = Regex.Matches(html, resultPattern, RegexOptions.IgnoreCase);
                var snippetMatches = Regex.Matches(html, snippetPattern, RegexOptions.IgnoreCase);

                for (int i = 0; i < Math.Min(resultMatches.Count, 6); i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var match = resultMatches[i];
                    var rawUrl = match.Groups[1].Value;
                    var title = HttpUtility.HtmlDecode(match.Groups[2].Value.Trim());

                    // DuckDuckGo wraps URLs - extract the actual URL
                    var actualUrl = ExtractActualUrl(rawUrl);
                    if (string.IsNullOrEmpty(actualUrl) || actualUrl.Contains("duckduckgo.com"))
                        continue;

                    var snippet = "";
                    if (i < snippetMatches.Count)
                        snippet = HttpUtility.HtmlDecode(snippetMatches[i].Groups[1].Value.Trim());

                    results.Add(new SearchResult
                    {
                        Title = title,
                        Url = actualUrl,
                        Snippet = snippet
                    });
                }

                if (results.Count == 0)
                    return "";

                // Format results
                var output = $"🔍 Search results for \"{query}\":\n\n";
                for (int i = 0; i < results.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var r = results[i];
                    output += $"{i + 1}. {r.Title}\n";
                    if (!string.IsNullOrEmpty(r.Snippet))
                        output += $"{r.Snippet}\n";
                    output += $"🔗 {r.Url}\n\n";
                }

                return output;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException) throw;
                Debug.WriteLine($"[WebSearchTool] Scrape error: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Extract actual URL from DuckDuckGo redirect URL
        /// </summary>
        private static string ExtractActualUrl(string ddgUrl)
        {
            try
            {
                // DuckDuckGo format: //duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com...
                if (ddgUrl.Contains("uddg="))
                {
                    var match = Regex.Match(ddgUrl, @"uddg=([^&]+)");
                    if (match.Success)
                        return HttpUtility.UrlDecode(match.Groups[1].Value);
                }
                
                // Direct URL
                if (ddgUrl.StartsWith("http"))
                    return ddgUrl;
                    
                if (ddgUrl.StartsWith("//"))
                    return "https:" + ddgUrl;

                return ddgUrl;
            }
            catch
            {
                return ddgUrl;
            }
        }

        /// <summary>
        /// Open a web search in the default browser
        /// </summary>
        public static Task<string> OpenBrowserSearchAsync(string query)
        {
            try
            {
                var encodedQuery = HttpUtility.UrlEncode(query);
                var searchUrl = $"https://www.google.com/search?q={encodedQuery}";

                Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true });
                return Task.FromResult($"🔍 Opened search for \"{query}\" in your browser\n🔗 {searchUrl}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Could not open browser: {ex.Message}");
            }
        }

        /// <summary>
        /// Get weather using wttr.in (free, no API key)
        /// Requires online mode consent.
        /// </summary>
        public static async Task<string> GetWeatherAsync(string location, CancellationToken ct = default)
        {
            // Check online mode consent
            var accessResult = await OnlineModeManager.Instance.RequestAccessAsync($"weather for {location}");
            
            if (accessResult == OnlineAccessResult.Denied)
            {
                Debug.WriteLine($"[WebSearchTool] Online access denied for weather: {location}");
                return OnlineModeManager.GetDeniedMessage();
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                // Default to Middlesbrough if no location specified
                if (string.IsNullOrWhiteSpace(location) || location.ToLower() == "auto")
                    location = "Middlesbrough";
                
                var encodedLocation = HttpUtility.UrlEncode(location);
                var url = $"https://wttr.in/{encodedLocation}?format=j1";

                // Add User-Agent header - wttr.in works better with curl
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "curl");
                
                // Use a linked CTS to combine the provided token with a 10s timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                
                var response = await httpClient.SendAsync(request, linkedCts.Token);
                var content = await response.Content.ReadAsStringAsync(linkedCts.Token);

                if (string.IsNullOrEmpty(content))
                    return $"Could not get weather for {location}";

                // Parse JSON response
                var json = System.Text.Json.JsonDocument.Parse(content);
                var current = json.RootElement.GetProperty("current_condition")[0];
                var weather = json.RootElement.GetProperty("weather")[0];
                
                var temp = current.GetProperty("temp_C").GetString();
                var feelsLike = current.GetProperty("FeelsLikeC").GetString();
                var humidity = current.GetProperty("humidity").GetString();
                var windSpeed = current.GetProperty("windspeedKmph").GetString();
                var desc = current.GetProperty("weatherDesc")[0].GetProperty("value").GetString();
                var area = json.RootElement.GetProperty("nearest_area")[0].GetProperty("areaName")[0].GetProperty("value").GetString();
                
                // Tomorrow's forecast
                var tomorrow = json.RootElement.GetProperty("weather")[1];
                var maxTemp = tomorrow.GetProperty("maxtempC").GetString();
                var minTemp = tomorrow.GetProperty("mintempC").GetString();
                
                return $@"🌤️ Weather for {area}

🌡️ {temp}°C (feels like {feelsLike}°C)
☁️ {desc}
💧 {humidity}% humidity
💨 {windSpeed} km/h wind

📅 Tomorrow: {minTemp}°C - {maxTemp}°C";
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested)
                    return "CANCELLED · OPERATION STOPPED";
                return $"⏱️ Weather request timed out for {location}. The weather service may be slow - try again.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Weather error: {ex.Message}");
                return $"❌ Couldn't get weather for {location}. Check your internet connection.";
            }
        }

        private class SearchResult
        {
            public string Title { get; set; } = "";
            public string Url { get; set; } = "";
            public string Snippet { get; set; } = "";
        }
    }
}
