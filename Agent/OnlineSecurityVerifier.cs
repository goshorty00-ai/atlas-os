using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Online security verification using VirusTotal API and other services
    /// </summary>
    public class OnlineSecurityVerifier
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        
        // VirusTotal free API (4 requests/minute limit)
        private const string VirusTotalApiUrl = "https://www.virustotal.com/api/v3";
        private static string? _virusTotalApiKey;
        
        // Cache to avoid repeated lookups
        private static readonly Dictionary<string, VerificationResult> _cache = new();
        private static readonly object _cacheLock = new();
        
        public static void SetVirusTotalApiKey(string apiKey)
        {
            _virusTotalApiKey = apiKey;
        }
        
        /// <summary>
        /// Verify a file's safety using online services
        /// </summary>
        public static async Task<VerificationResult> VerifyFileAsync(string filePath)
        {
            var result = new VerificationResult { FilePath = filePath };
            
            try
            {
                if (!File.Exists(filePath))
                {
                    result.Error = "File not found";
                    return result;
                }
                
                // Calculate file hash
                result.FileHash = await CalculateSHA256Async(filePath);
                
                // Check cache first
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(result.FileHash, out var cached))
                    {
                        cached.FromCache = true;
                        return cached;
                    }
                }
                
                // Try VirusTotal if API key is set
                if (!string.IsNullOrEmpty(_virusTotalApiKey))
                {
                    await CheckVirusTotalAsync(result);
                }
                
                // Fallback: Check against known safe hashes (Microsoft signed, etc.)
                await CheckKnownSafeHashesAsync(result);
                
                // Cache the result
                lock (_cacheLock)
                {
                    _cache[result.FileHash] = result;
                }
                
                result.Verified = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Debug.WriteLine($"[SecurityVerifier] Error: {ex.Message}");
            }
            
            return result;
        }
        
        private static async Task<string> CalculateSHA256Async(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await Task.Run(() => sha256.ComputeHash(stream));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
        
        private static async Task CheckVirusTotalAsync(VerificationResult result)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, 
                    $"{VirusTotalApiUrl}/files/{result.FileHash}");
                request.Headers.Add("x-apikey", _virusTotalApiKey);
                
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var data = doc.RootElement.GetProperty("data");
                    var attributes = data.GetProperty("attributes");
                    var stats = attributes.GetProperty("last_analysis_stats");
                    
                    result.VirusTotalMalicious = stats.GetProperty("malicious").GetInt32();
                    result.VirusTotalSuspicious = stats.GetProperty("suspicious").GetInt32();
                    result.VirusTotalHarmless = stats.GetProperty("harmless").GetInt32();
                    result.VirusTotalUndetected = stats.GetProperty("undetected").GetInt32();
                    result.VirusTotalChecked = true;
                    
                    // Determine safety
                    if (result.VirusTotalMalicious > 0)
                    {
                        result.IsSafe = false;
                        result.ThreatLevel = result.VirusTotalMalicious > 5 ? "HIGH" : "MEDIUM";
                        result.Recommendation = $"⚠️ DANGER: {result.VirusTotalMalicious} antivirus engines flagged this file as malicious!";
                    }
                    else if (result.VirusTotalSuspicious > 0)
                    {
                        result.IsSafe = false;
                        result.ThreatLevel = "MEDIUM";
                        result.Recommendation = $"⚡ CAUTION: {result.VirusTotalSuspicious} engines flagged this as suspicious.";
                    }
                    else
                    {
                        result.IsSafe = true;
                        result.ThreatLevel = "LOW";
                        result.Recommendation = $"✅ SAFE: Scanned by {result.VirusTotalHarmless + result.VirusTotalUndetected} engines, no threats found.";
                    }
                    
                    Debug.WriteLine($"[VirusTotal] {result.FileHash}: Malicious={result.VirusTotalMalicious}, Safe={result.VirusTotalHarmless}");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // File not in VirusTotal database - unknown
                    result.VirusTotalChecked = true;
                    result.ThreatLevel = "UNKNOWN";
                    result.Recommendation = "⚡ UNKNOWN: File not found in VirusTotal database. Exercise caution.";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VirusTotal] API error: {ex.Message}");
                result.Error = $"VirusTotal check failed: {ex.Message}";
            }
        }
        
        private static async Task CheckKnownSafeHashesAsync(VerificationResult result)
        {
            // This would check against a local database of known safe hashes
            // For now, we'll just mark as needing manual verification if VirusTotal wasn't checked
            await Task.CompletedTask;
            
            if (!result.VirusTotalChecked && string.IsNullOrEmpty(result.Recommendation))
            {
                result.ThreatLevel = "UNKNOWN";
                result.Recommendation = "⚡ Unable to verify online. Check the publisher and source before running.";
            }
        }
    }
    
    public class VerificationResult
    {
        public string FilePath { get; set; } = "";
        public string FileHash { get; set; } = "";
        public bool Verified { get; set; }
        public bool FromCache { get; set; }
        public bool IsSafe { get; set; }
        public string ThreatLevel { get; set; } = "UNKNOWN";
        public string Recommendation { get; set; } = "";
        public string? Error { get; set; }
        
        // VirusTotal results
        public bool VirusTotalChecked { get; set; }
        public int VirusTotalMalicious { get; set; }
        public int VirusTotalSuspicious { get; set; }
        public int VirusTotalHarmless { get; set; }
        public int VirusTotalUndetected { get; set; }
    }
}
