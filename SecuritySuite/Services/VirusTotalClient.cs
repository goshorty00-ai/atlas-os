using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.SecuritySuite.Models;

namespace AtlasAI.SecuritySuite.Services
{
    /// <summary>
    /// VirusTotal API client for real threat intelligence
    /// Free tier: 4 requests/minute, 500 requests/day
    /// </summary>
    public class VirusTotalClient
    {
        private const string BaseUrl = "https://www.virustotal.com/api/v3";
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _rateLimiter = new(1, 1);
        private DateTime _lastRequestTime = DateTime.MinValue;
        private const int MinRequestIntervalMs = 15500; // ~4 requests per minute
        
        private string? _apiKey;
        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        
        // Cache to avoid re-checking same files
        private readonly Dictionary<string, VirusTotalResult> _cache = new();
        private const int MaxCacheSize = 1000;
        
        public event Action<string>? StatusChanged;
        
        public VirusTotalClient()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            LoadApiKey();
        }
        
        private void LoadApiKey()
        {
            try
            {
                // 1. Try IntegrationKeyStore (DPAPI-protected, preferred)
                var storeKey = AtlasAI.Core.IntegrationKeyStore.GetDecrypted("virustotal");
                if (!string.IsNullOrWhiteSpace(storeKey))
                {
                    _apiKey = storeKey.Trim();
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("x-apikey", _apiKey);
                    return;
                }

                // 2. Try ignored local flat file (%APPDATA%\AtlasAI\virustotal_key.txt)
                var keyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "virustotal_key.txt");

                if (File.Exists(keyPath))
                {
                    var savedKey = File.ReadAllText(keyPath).Trim();
                    if (!string.IsNullOrWhiteSpace(savedKey))
                    {
                        _apiKey = savedKey;
                        _httpClient.DefaultRequestHeaders.Clear();
                        _httpClient.DefaultRequestHeaders.Add("x-apikey", _apiKey);
                        return;
                    }
                }

                // No key available — IsConfigured returns false; callers handle gracefully.
                _apiKey = null;
            }
            catch
            {
                _apiKey = null;
            }
        }
        
        public void SetApiKey(string apiKey)
        {
            _apiKey = apiKey?.Trim();
            
            // Save for future use
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "virustotal_key.txt"), _apiKey ?? "");
                
                _httpClient.DefaultRequestHeaders.Clear();
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    _httpClient.DefaultRequestHeaders.Add("x-apikey", _apiKey);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VirusTotalClient] Error saving API key: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check a file hash against VirusTotal database
        /// </summary>
        public async Task<VirusTotalResult?> CheckFileHashAsync(string filePath, CancellationToken ct = default)
        {
            if (!IsConfigured) return null;
            
            try
            {
                // Compute SHA256 hash
                var hash = await ComputeSha256Async(filePath);
                if (string.IsNullOrEmpty(hash)) return null;
                
                // Check cache first
                if (_cache.TryGetValue(hash, out var cached))
                {
                    return cached;
                }
                
                // Rate limit
                await RateLimitAsync(ct);
                
                StatusChanged?.Invoke($"Checking VirusTotal: {Path.GetFileName(filePath)}");
                
                var response = await _httpClient.GetAsync($"{BaseUrl}/files/{hash}", ct);
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // File not in VT database - not necessarily clean, just unknown
                    var unknownResult = new VirusTotalResult
                    {
                        Hash = hash,
                        FileName = Path.GetFileName(filePath),
                        Status = VTStatus.NotFound,
                        Message = "File not in VirusTotal database"
                    };
                    CacheResult(hash, unknownResult);
                    return unknownResult;
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    return new VirusTotalResult
                    {
                        Hash = hash,
                        Status = VTStatus.Error,
                        Message = $"API error: {response.StatusCode}"
                    };
                }
                
                var json = await response.Content.ReadAsStringAsync(ct);
                var vtResponse = JsonSerializer.Deserialize<VTFileResponse>(json);
                
                if (vtResponse?.Data?.Attributes == null)
                {
                    return new VirusTotalResult { Hash = hash, Status = VTStatus.Error, Message = "Invalid response" };
                }
                
                var attrs = vtResponse.Data.Attributes;
                var stats = attrs.LastAnalysisStats;
                
                var result = new VirusTotalResult
                {
                    Hash = hash,
                    FileName = attrs.MeaningfulName ?? Path.GetFileName(filePath),
                    Status = DetermineStatus(stats),
                    Malicious = stats?.Malicious ?? 0,
                    Suspicious = stats?.Suspicious ?? 0,
                    Harmless = stats?.Harmless ?? 0,
                    Undetected = stats?.Undetected ?? 0,
                    TotalEngines = (stats?.Malicious ?? 0) + (stats?.Suspicious ?? 0) + 
                                   (stats?.Harmless ?? 0) + (stats?.Undetected ?? 0),
                    DetectionNames = ExtractDetectionNames(attrs.LastAnalysisResults),
                    FileType = attrs.TypeDescription,
                    FirstSeen = attrs.FirstSubmissionDate,
                    LastAnalyzed = attrs.LastAnalysisDate
                };
                
                CacheResult(hash, result);
                return result;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                return new VirusTotalResult
                {
                    Status = VTStatus.Error,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Check a URL against VirusTotal
        /// </summary>
        public async Task<VirusTotalResult?> CheckUrlAsync(string url, CancellationToken ct = default)
        {
            if (!IsConfigured) return null;
            
            try
            {
                // URL ID is base64 of URL without padding
                var urlId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                
                // Check cache
                if (_cache.TryGetValue(urlId, out var cached))
                {
                    return cached;
                }
                
                await RateLimitAsync(ct);
                
                var response = await _httpClient.GetAsync($"{BaseUrl}/urls/{urlId}", ct);
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new VirusTotalResult
                    {
                        Hash = urlId,
                        FileName = url,
                        Status = VTStatus.NotFound,
                        Message = "URL not in VirusTotal database"
                    };
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    return new VirusTotalResult { Status = VTStatus.Error, Message = $"API error: {response.StatusCode}" };
                }
                
                var json = await response.Content.ReadAsStringAsync(ct);
                var vtResponse = JsonSerializer.Deserialize<VTFileResponse>(json);
                var stats = vtResponse?.Data?.Attributes?.LastAnalysisStats;
                
                var result = new VirusTotalResult
                {
                    Hash = urlId,
                    FileName = url,
                    Status = DetermineStatus(stats),
                    Malicious = stats?.Malicious ?? 0,
                    Suspicious = stats?.Suspicious ?? 0,
                    Harmless = stats?.Harmless ?? 0,
                    Undetected = stats?.Undetected ?? 0,
                    TotalEngines = (stats?.Malicious ?? 0) + (stats?.Suspicious ?? 0) + 
                                   (stats?.Harmless ?? 0) + (stats?.Undetected ?? 0)
                };
                
                CacheResult(urlId, result);
                return result;
            }
            catch (Exception ex)
            {
                return new VirusTotalResult { Status = VTStatus.Error, Message = ex.Message };
            }
        }
        
        /// <summary>
        /// Get quota/rate limit info
        /// </summary>
        public async Task<VTQuotaInfo?> GetQuotaInfoAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return null;
            
            try
            {
                await RateLimitAsync(ct);
                
                var response = await _httpClient.GetAsync($"{BaseUrl}/users/current", ct);
                if (!response.IsSuccessStatusCode) return null;
                
                var json = await response.Content.ReadAsStringAsync(ct);
                // Parse quota info from response
                using var doc = JsonDocument.Parse(json);
                var quotas = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("quotas");
                
                return new VTQuotaInfo
                {
                    DailyUsed = quotas.GetProperty("api_requests_daily").GetProperty("used").GetInt32(),
                    DailyLimit = quotas.GetProperty("api_requests_daily").GetProperty("allowed").GetInt32(),
                    MonthlyUsed = quotas.GetProperty("api_requests_monthly").GetProperty("used").GetInt32(),
                    MonthlyLimit = quotas.GetProperty("api_requests_monthly").GetProperty("allowed").GetInt32()
                };
            }
            catch
            {
                return null;
            }
        }
        
        private async Task RateLimitAsync(CancellationToken ct)
        {
            await _rateLimiter.WaitAsync(ct);
            try
            {
                var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
                if (elapsed < MinRequestIntervalMs)
                {
                    await Task.Delay((int)(MinRequestIntervalMs - elapsed), ct);
                }
                _lastRequestTime = DateTime.UtcNow;
            }
            finally
            {
                _rateLimiter.Release();
            }
        }
        
        private async Task<string?> ComputeSha256Async(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = await sha256.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }
        
        private VTStatus DetermineStatus(VTAnalysisStats? stats)
        {
            if (stats == null) return VTStatus.Unknown;
            
            if (stats.Malicious > 0)
                return stats.Malicious >= 5 ? VTStatus.Malicious : VTStatus.Suspicious;
            if (stats.Suspicious > 0)
                return VTStatus.Suspicious;
            if (stats.Harmless > 0 || stats.Undetected > 0)
                return VTStatus.Clean;
            
            return VTStatus.Unknown;
        }
        
        private List<string> ExtractDetectionNames(Dictionary<string, VTEngineResult>? results)
        {
            var names = new List<string>();
            if (results == null) return names;
            
            foreach (var kvp in results)
            {
                if (kvp.Value.Category == "malicious" || kvp.Value.Category == "suspicious")
                {
                    if (!string.IsNullOrEmpty(kvp.Value.Result))
                    {
                        names.Add($"{kvp.Key}: {kvp.Value.Result}");
                    }
                }
            }
            return names;
        }
        
        private void CacheResult(string key, VirusTotalResult result)
        {
            if (_cache.Count >= MaxCacheSize)
            {
                _cache.Clear(); // Simple cache eviction
            }
            _cache[key] = result;
        }
    }
    
    #region VirusTotal Models
    
    public enum VTStatus
    {
        Unknown,
        Clean,
        Suspicious,
        Malicious,
        NotFound,
        Error
    }
    
    public class VirusTotalResult
    {
        public string Hash { get; set; } = "";
        public string FileName { get; set; } = "";
        public VTStatus Status { get; set; }
        public string? Message { get; set; }
        public int Malicious { get; set; }
        public int Suspicious { get; set; }
        public int Harmless { get; set; }
        public int Undetected { get; set; }
        public int TotalEngines { get; set; }
        public List<string> DetectionNames { get; set; } = new();
        public string? FileType { get; set; }
        public long? FirstSeen { get; set; }
        public long? LastAnalyzed { get; set; }
        
        public string Summary => Status switch
        {
            VTStatus.Malicious => $"🔴 MALICIOUS - {Malicious}/{TotalEngines} engines detected threats",
            VTStatus.Suspicious => $"🟡 SUSPICIOUS - {Malicious + Suspicious}/{TotalEngines} engines flagged",
            VTStatus.Clean => $"🟢 CLEAN - No threats detected by {TotalEngines} engines",
            VTStatus.NotFound => "⚪ Not in VirusTotal database",
            VTStatus.Error => $"❌ Error: {Message}",
            _ => "Unknown status"
        };
    }
    
    public class VTQuotaInfo
    {
        public int DailyUsed { get; set; }
        public int DailyLimit { get; set; }
        public int MonthlyUsed { get; set; }
        public int MonthlyLimit { get; set; }
        
        public string Summary => $"Daily: {DailyUsed}/{DailyLimit} | Monthly: {MonthlyUsed}/{MonthlyLimit}";
    }
    
    // JSON response models
    public class VTFileResponse
    {
        [JsonPropertyName("data")]
        public VTFileData? Data { get; set; }
    }
    
    public class VTFileData
    {
        [JsonPropertyName("attributes")]
        public VTFileAttributes? Attributes { get; set; }
    }
    
    public class VTFileAttributes
    {
        [JsonPropertyName("last_analysis_stats")]
        public VTAnalysisStats? LastAnalysisStats { get; set; }
        
        [JsonPropertyName("last_analysis_results")]
        public Dictionary<string, VTEngineResult>? LastAnalysisResults { get; set; }
        
        [JsonPropertyName("meaningful_name")]
        public string? MeaningfulName { get; set; }
        
        [JsonPropertyName("type_description")]
        public string? TypeDescription { get; set; }
        
        [JsonPropertyName("first_submission_date")]
        public long? FirstSubmissionDate { get; set; }
        
        [JsonPropertyName("last_analysis_date")]
        public long? LastAnalysisDate { get; set; }
    }
    
    public class VTAnalysisStats
    {
        [JsonPropertyName("malicious")]
        public int Malicious { get; set; }
        
        [JsonPropertyName("suspicious")]
        public int Suspicious { get; set; }
        
        [JsonPropertyName("harmless")]
        public int Harmless { get; set; }
        
        [JsonPropertyName("undetected")]
        public int Undetected { get; set; }
    }
    
    public class VTEngineResult
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }
        
        [JsonPropertyName("result")]
        public string? Result { get; set; }
        
        [JsonPropertyName("engine_name")]
        public string? EngineName { get; set; }
    }
    
    #endregion
}
