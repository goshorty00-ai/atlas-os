using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// IP Lookup - Get geolocation and info for IP addresses.
    /// </summary>
    public static class IpLookup
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        
        /// <summary>
        /// Handle IP lookup commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // My IP
            if (lower == "my ip" || lower == "what is my ip" || lower == "whats my ip" || 
                lower == "my public ip" || lower == "external ip")
            {
                return await GetMyIpAsync();
            }
            
            // IP lookup
            if (lower.Contains("lookup") || lower.Contains("locate") || lower.Contains("where is"))
            {
                var ipMatch = System.Text.RegularExpressions.Regex.Match(input, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
                if (ipMatch.Success)
                {
                    return await LookupIpAsync(ipMatch.Groups[1].Value);
                }
            }
            
            // Direct IP address in input
            var directIpMatch = System.Text.RegularExpressions.Regex.Match(input, @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})$");
            if (directIpMatch.Success && (lower.Contains("ip") || lower.Contains("lookup") || lower.Contains("info")))
            {
                return await LookupIpAsync(directIpMatch.Groups[1].Value);
            }
            
            return null;
        }
        
        private static async Task<string> GetMyIpAsync()
        {
            try
            {
                // Get public IP
                var ip = await _httpClient.GetStringAsync("https://api.ipify.org");
                ip = ip.Trim();
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(ip));
                
                // Get location info
                var locationInfo = await GetIpInfoAsync(ip);
                
                return $"🌐 **Your Public IP:**\n```\n{ip}\n```\n{locationInfo}\n✓ Copied to clipboard!";
            }
            catch (Exception ex)
            {
                return $"❌ Couldn't get IP: {ex.Message}";
            }
        }
        
        private static async Task<string> LookupIpAsync(string ip)
        {
            try
            {
                var info = await GetIpInfoAsync(ip);
                return $"🔍 **IP Lookup: {ip}**\n\n{info}";
            }
            catch (Exception ex)
            {
                return $"❌ Lookup failed: {ex.Message}";
            }
        }
        
        private static async Task<string> GetIpInfoAsync(string ip)
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"http://ip-api.com/json/{ip}");
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("status", out var status) && status.GetString() == "fail")
                {
                    return "❌ Invalid or private IP address";
                }
                
                var country = root.TryGetProperty("country", out var c) ? c.GetString() : "Unknown";
                var region = root.TryGetProperty("regionName", out var r) ? r.GetString() : "";
                var city = root.TryGetProperty("city", out var ci) ? ci.GetString() : "";
                var isp = root.TryGetProperty("isp", out var i) ? i.GetString() : "";
                var org = root.TryGetProperty("org", out var o) ? o.GetString() : "";
                var timezone = root.TryGetProperty("timezone", out var tz) ? tz.GetString() : "";
                var lat = root.TryGetProperty("lat", out var la) ? la.GetDouble() : 0;
                var lon = root.TryGetProperty("lon", out var lo) ? lo.GetDouble() : 0;
                
                var location = string.Join(", ", new[] { city, region, country }.Where(s => !string.IsNullOrEmpty(s)));
                
                return $"📍 **Location:** {location}\n" +
                       $"🏢 **ISP:** {isp}\n" +
                       $"🏛️ **Organization:** {org}\n" +
                       $"🕐 **Timezone:** {timezone}\n" +
                       $"🗺️ **Coordinates:** {lat:F4}, {lon:F4}";
            }
            catch
            {
                return "Location info unavailable";
            }
        }
    }
}
