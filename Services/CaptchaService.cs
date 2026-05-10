using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Services
{
    public enum CaptchaProvider
    {
        TwoCaptcha,
        AntiCaptcha,
        CapMonster
    }

    public class CaptchaResult
    {
        public bool Success { get; init; }
        public string? Solution { get; init; }
        public string? Error { get; init; }
        public string? TaskId { get; init; }
    }

    public class CaptchaService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
        private readonly CaptchaProvider _provider;
        private readonly string? _apiKey;

        public CaptchaService()
        {
            var prefs = PreferencesStore.Instance.Current;
            
            // Try to get API key from preferences (temporarily disabled - properties not in UserPreferences)
            _apiKey = ""; // prefs.CaptchaApiKey;
            
            // Determine provider from preferences or default to 2captcha (temporarily disabled)
            _provider = CaptchaProvider.TwoCaptcha; // Default provider
            /*
            _provider = prefs.CaptchaProvider switch
            {
                "anticaptcha" => CaptchaProvider.AntiCaptcha,
                "capmonster" => CaptchaProvider.CapMonster,
                _ => CaptchaProvider.TwoCaptcha
            };
            */
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        public async Task<CaptchaResult> SolveRecaptchaV2Async(string siteKey, string pageUrl, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new CaptchaResult { Success = false, Error = "CAPTCHA service not configured" };

            return _provider switch
            {
                CaptchaProvider.TwoCaptcha => await SolveTwoCaptchaRecaptchaV2Async(siteKey, pageUrl, ct),
                CaptchaProvider.AntiCaptcha => await SolveAntiCaptchaRecaptchaV2Async(siteKey, pageUrl, ct),
                CaptchaProvider.CapMonster => await SolveCapMonsterRecaptchaV2Async(siteKey, pageUrl, ct),
                _ => new CaptchaResult { Success = false, Error = "Unknown CAPTCHA provider" }
            };
        }

        public async Task<CaptchaResult> SolveRecaptchaV3Async(string siteKey, string pageUrl, string action = "verify", double minScore = 0.3, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new CaptchaResult { Success = false, Error = "CAPTCHA service not configured" };

            return _provider switch
            {
                CaptchaProvider.TwoCaptcha => await SolveTwoCaptchaRecaptchaV3Async(siteKey, pageUrl, action, minScore, ct),
                CaptchaProvider.AntiCaptcha => await SolveAntiCaptchaRecaptchaV3Async(siteKey, pageUrl, action, minScore, ct),
                CaptchaProvider.CapMonster => await SolveCapMonsterRecaptchaV3Async(siteKey, pageUrl, action, minScore, ct),
                _ => new CaptchaResult { Success = false, Error = "Unknown CAPTCHA provider" }
            };
        }

        public async Task<CaptchaResult> SolveHCaptchaAsync(string siteKey, string pageUrl, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new CaptchaResult { Success = false, Error = "CAPTCHA service not configured" };

            return _provider switch
            {
                CaptchaProvider.TwoCaptcha => await SolveTwoCaptchaHCaptchaAsync(siteKey, pageUrl, ct),
                CaptchaProvider.AntiCaptcha => await SolveAntiCaptchaHCaptchaAsync(siteKey, pageUrl, ct),
                CaptchaProvider.CapMonster => await SolveCapMonsterHCaptchaAsync(siteKey, pageUrl, ct),
                _ => new CaptchaResult { Success = false, Error = "Unknown CAPTCHA provider" }
            };
        }

        #region 2captcha Implementation

        private async Task<CaptchaResult> SolveTwoCaptchaRecaptchaV2Async(string siteKey, string pageUrl, CancellationToken ct)
        {
            try
            {
                // Submit CAPTCHA
                var submitData = new Dictionary<string, string>
                {
                    ["key"] = _apiKey!,
                    ["method"] = "userrecaptcha",
                    ["googlekey"] = siteKey,
                    ["pageurl"] = pageUrl,
                    ["json"] = "1"
                };

                var submitResponse = await Http.PostAsync("http://2captcha.com/in.php", 
                    new FormUrlEncodedContent(submitData), ct);
                var submitResult = await submitResponse.Content.ReadAsStringAsync(ct);

                using var submitDoc = JsonDocument.Parse(submitResult);
                if (submitDoc.RootElement.GetProperty("status").GetInt32() != 1)
                {
                    var error = submitDoc.RootElement.TryGetProperty("error_text", out var errorEl) 
                        ? errorEl.GetString() : "Submit failed";
                    return new CaptchaResult { Success = false, Error = error };
                }

                var taskId = submitDoc.RootElement.GetProperty("request").GetString();

                // Wait for solution
                await Task.Delay(TimeSpan.FromSeconds(20), ct); // Initial wait

                for (int attempt = 0; attempt < 24; attempt++) // Max 2 minutes
                {
                    ct.ThrowIfCancellationRequested();

                    var resultResponse = await Http.GetAsync(
                        $"http://2captcha.com/res.php?key={_apiKey}&action=get&id={taskId}&json=1", ct);
                    var resultText = await resultResponse.Content.ReadAsStringAsync(ct);

                    using var resultDoc = JsonDocument.Parse(resultText);
                    var status = resultDoc.RootElement.GetProperty("status").GetInt32();

                    if (status == 1)
                    {
                        var solution = resultDoc.RootElement.GetProperty("request").GetString();
                        return new CaptchaResult { Success = true, Solution = solution, TaskId = taskId };
                    }

                    if (resultDoc.RootElement.TryGetProperty("error_text", out var errorEl))
                    {
                        var error = errorEl.GetString();
                        if (error != "CAPCHA_NOT_READY")
                            return new CaptchaResult { Success = false, Error = error };
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }

                return new CaptchaResult { Success = false, Error = "Timeout waiting for solution" };
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SolveTwoCaptchaRecaptchaV3Async(string siteKey, string pageUrl, string action, double minScore, CancellationToken ct)
        {
            try
            {
                var submitData = new Dictionary<string, string>
                {
                    ["key"] = _apiKey!,
                    ["method"] = "userrecaptcha",
                    ["version"] = "v3",
                    ["googlekey"] = siteKey,
                    ["pageurl"] = pageUrl,
                    ["action"] = action,
                    ["min_score"] = minScore.ToString("F1"),
                    ["json"] = "1"
                };

                return await SubmitAndWaitTwoCaptcha(submitData, ct);
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SolveTwoCaptchaHCaptchaAsync(string siteKey, string pageUrl, CancellationToken ct)
        {
            try
            {
                var submitData = new Dictionary<string, string>
                {
                    ["key"] = _apiKey!,
                    ["method"] = "hcaptcha",
                    ["sitekey"] = siteKey,
                    ["pageurl"] = pageUrl,
                    ["json"] = "1"
                };

                return await SubmitAndWaitTwoCaptcha(submitData, ct);
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SubmitAndWaitTwoCaptcha(Dictionary<string, string> submitData, CancellationToken ct)
        {
            var submitResponse = await Http.PostAsync("http://2captcha.com/in.php", 
                new FormUrlEncodedContent(submitData), ct);
            var submitResult = await submitResponse.Content.ReadAsStringAsync(ct);

            using var submitDoc = JsonDocument.Parse(submitResult);
            if (submitDoc.RootElement.GetProperty("status").GetInt32() != 1)
            {
                var error = submitDoc.RootElement.TryGetProperty("error_text", out var errorEl) 
                    ? errorEl.GetString() : "Submit failed";
                return new CaptchaResult { Success = false, Error = error };
            }

            var taskId = submitDoc.RootElement.GetProperty("request").GetString();
            await Task.Delay(TimeSpan.FromSeconds(20), ct);

            for (int attempt = 0; attempt < 24; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var resultResponse = await Http.GetAsync(
                    $"http://2captcha.com/res.php?key={_apiKey}&action=get&id={taskId}&json=1", ct);
                var resultText = await resultResponse.Content.ReadAsStringAsync(ct);

                using var resultDoc = JsonDocument.Parse(resultText);
                var status = resultDoc.RootElement.GetProperty("status").GetInt32();

                if (status == 1)
                {
                    var solution = resultDoc.RootElement.GetProperty("request").GetString();
                    return new CaptchaResult { Success = true, Solution = solution, TaskId = taskId };
                }

                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

            return new CaptchaResult { Success = false, Error = "Timeout" };
        }

        #endregion

        #region AntiCaptcha Implementation

        private async Task<CaptchaResult> SolveAntiCaptchaRecaptchaV2Async(string siteKey, string pageUrl, CancellationToken ct)
        {
            try
            {
                var taskData = new
                {
                    clientKey = _apiKey,
                    task = new
                    {
                        type = "NoCaptchaTaskProxyless",
                        websiteURL = pageUrl,
                        websiteKey = siteKey
                    }
                };

                return await SubmitAndWaitAntiCaptcha(taskData, ct);
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SolveAntiCaptchaRecaptchaV3Async(string siteKey, string pageUrl, string action, double minScore, CancellationToken ct)
        {
            try
            {
                var taskData = new
                {
                    clientKey = _apiKey,
                    task = new
                    {
                        type = "RecaptchaV3TaskProxyless",
                        websiteURL = pageUrl,
                        websiteKey = siteKey,
                        minScore = minScore,
                        pageAction = action
                    }
                };

                return await SubmitAndWaitAntiCaptcha(taskData, ct);
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SolveAntiCaptchaHCaptchaAsync(string siteKey, string pageUrl, CancellationToken ct)
        {
            try
            {
                var taskData = new
                {
                    clientKey = _apiKey,
                    task = new
                    {
                        type = "HCaptchaTaskProxyless",
                        websiteURL = pageUrl,
                        websiteKey = siteKey
                    }
                };

                return await SubmitAndWaitAntiCaptcha(taskData, ct);
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SubmitAndWaitAntiCaptcha(object taskData, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(taskData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var submitResponse = await Http.PostAsync("https://api.anti-captcha.com/createTask", content, ct);
            var submitResult = await submitResponse.Content.ReadAsStringAsync(ct);

            using var submitDoc = JsonDocument.Parse(submitResult);
            if (submitDoc.RootElement.GetProperty("errorId").GetInt32() != 0)
            {
                var error = submitDoc.RootElement.TryGetProperty("errorDescription", out var errorEl) 
                    ? errorEl.GetString() : "Submit failed";
                return new CaptchaResult { Success = false, Error = error };
            }

            var taskId = submitDoc.RootElement.GetProperty("taskId").GetInt32().ToString();
            await Task.Delay(TimeSpan.FromSeconds(20), ct);

            for (int attempt = 0; attempt < 24; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var resultData = new { clientKey = _apiKey, taskId = int.Parse(taskId) };
                var resultJson = JsonSerializer.Serialize(resultData);
                var resultContent = new StringContent(resultJson, Encoding.UTF8, "application/json");

                var resultResponse = await Http.PostAsync("https://api.anti-captcha.com/getTaskResult", resultContent, ct);
                var resultText = await resultResponse.Content.ReadAsStringAsync(ct);

                using var resultDoc = JsonDocument.Parse(resultText);
                if (resultDoc.RootElement.GetProperty("errorId").GetInt32() != 0)
                {
                    var error = resultDoc.RootElement.TryGetProperty("errorDescription", out var errorEl) 
                        ? errorEl.GetString() : "Unknown error";
                    return new CaptchaResult { Success = false, Error = error };
                }

                var status = resultDoc.RootElement.GetProperty("status").GetString();
                if (status == "ready")
                {
                    var solution = resultDoc.RootElement.GetProperty("solution").GetProperty("gRecaptchaResponse").GetString();
                    return new CaptchaResult { Success = true, Solution = solution, TaskId = taskId };
                }

                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

            return new CaptchaResult { Success = false, Error = "Timeout" };
        }

        #endregion

        #region CapMonster Implementation

        private async Task<CaptchaResult> SolveCapMonsterRecaptchaV2Async(string siteKey, string pageUrl, CancellationToken ct)
        {
            try
            {
                var taskData = new
                {
                    clientKey = _apiKey,
                    task = new
                    {
                        type = "NoCaptchaTaskProxyless",
                        websiteURL = pageUrl,
                        websiteKey = siteKey
                    }
                };

                return await SubmitAndWaitCapMonster(taskData, ct);
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SolveCapMonsterRecaptchaV3Async(string siteKey, string pageUrl, string action, double minScore, CancellationToken ct)
        {
            try
            {
                var taskData = new
                {
                    clientKey = _apiKey,
                    task = new
                    {
                        type = "RecaptchaV3TaskProxyless",
                        websiteURL = pageUrl,
                        websiteKey = siteKey,
                        minScore = minScore,
                        pageAction = action
                    }
                };

                return await SubmitAndWaitCapMonster(taskData, ct);
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SolveCapMonsterHCaptchaAsync(string siteKey, string pageUrl, CancellationToken ct)
        {
            try
            {
                var taskData = new
                {
                    clientKey = _apiKey,
                    task = new
                    {
                        type = "HCaptchaTaskProxyless",
                        websiteURL = pageUrl,
                        websiteKey = siteKey
                    }
                };

                return await SubmitAndWaitCapMonster(taskData, ct);
            }
            catch (Exception ex)
            {
                return new CaptchaResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CaptchaResult> SubmitAndWaitCapMonster(object taskData, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(taskData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var submitResponse = await Http.PostAsync("https://api.capmonster.cloud/createTask", content, ct);
            var submitResult = await submitResponse.Content.ReadAsStringAsync(ct);

            using var submitDoc = JsonDocument.Parse(submitResult);
            if (submitDoc.RootElement.GetProperty("errorId").GetInt32() != 0)
            {
                var error = submitDoc.RootElement.TryGetProperty("errorDescription", out var errorEl) 
                    ? errorEl.GetString() : "Submit failed";
                return new CaptchaResult { Success = false, Error = error };
            }

            var taskId = submitDoc.RootElement.GetProperty("taskId").GetInt32().ToString();
            await Task.Delay(TimeSpan.FromSeconds(20), ct);

            for (int attempt = 0; attempt < 24; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var resultData = new { clientKey = _apiKey, taskId = int.Parse(taskId) };
                var resultJson = JsonSerializer.Serialize(resultData);
                var resultContent = new StringContent(resultJson, Encoding.UTF8, "application/json");

                var resultResponse = await Http.PostAsync("https://api.capmonster.cloud/getTaskResult", resultContent, ct);
                var resultText = await resultResponse.Content.ReadAsStringAsync(ct);

                using var resultDoc = JsonDocument.Parse(resultText);
                if (resultDoc.RootElement.GetProperty("errorId").GetInt32() != 0)
                {
                    var error = resultDoc.RootElement.TryGetProperty("errorDescription", out var errorEl) 
                        ? errorEl.GetString() : "Unknown error";
                    return new CaptchaResult { Success = false, Error = error };
                }

                var status = resultDoc.RootElement.GetProperty("status").GetString();
                if (status == "ready")
                {
                    var solution = resultDoc.RootElement.GetProperty("solution").GetProperty("gRecaptchaResponse").GetString();
                    return new CaptchaResult { Success = true, Solution = solution, TaskId = taskId };
                }

                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

            return new CaptchaResult { Success = false, Error = "Timeout" };
        }

        #endregion
    }
}