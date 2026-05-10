using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using AtlasAI.Agent;

namespace AtlasAI.Core
{
    /// <summary>
    /// Online mode settings for user preferences.
    /// </summary>
    public enum OnlineModeSetting
    {
        /// <summary>Online access is disabled (default).</summary>
        Off,
        /// <summary>Ask for consent each time.</summary>
        AskEachTime,
        /// <summary>Allow for the current session only.</summary>
        AllowForSession,
        /// <summary>Always allow read-only web access.</summary>
        AlwaysAllow
    }

    /// <summary>
    /// Result of an online access request.
    /// </summary>
    public enum OnlineAccessResult
    {
        /// <summary>Access granted.</summary>
        Allowed,
        /// <summary>User denied access.</summary>
        Denied,
        /// <summary>Access pending user consent.</summary>
        PendingConsent
    }

    /// <summary>
    /// Manages online mode consent and state.
    /// Read-only web research only. No logins, purchases, or system changes.
    /// </summary>
    public class OnlineModeManager
    {
        private static OnlineModeManager? _instance;
        private static readonly object _lock = new();

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "online_mode.json");

        // State
        private OnlineModeSetting _setting = OnlineModeSetting.Off;
        private DateTime? _temporaryAccessExpiry;
        private bool _sessionAccessGranted;
        private DispatcherTimer? _expiryTimer;

        // Events
        public event Action<bool>? OnlineModeChanged;
        public event Action<TimeSpan>? TemporaryAccessTick;
        public event Action? TemporaryAccessExpired;
        public event Func<string, Task<OnlineConsentResult>>? ConsentRequested;

        public static OnlineModeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new OnlineModeManager();
                    }
                }
                return _instance;
            }
        }

        private OnlineModeManager()
        {
            LoadSettings();
            Debug.WriteLine($"[OnlineMode] Initialized with setting: {_setting}");
        }

        #region Public Properties

        /// <summary>
        /// Current online mode setting.
        /// </summary>
        public OnlineModeSetting Setting
        {
            get => _setting;
            set
            {
                if (_setting != value)
                {
                    _setting = value;
                    SaveSettings();
                    Debug.WriteLine($"[OnlineMode] Setting changed to: {value}");
                    OnlineModeChanged?.Invoke(IsOnlineAccessActive);
                }
            }
        }

        /// <summary>
        /// Whether online access is currently active.
        /// </summary>
        public bool IsOnlineAccessActive
        {
            get
            {
                return _setting switch
                {
                    OnlineModeSetting.AlwaysAllow => true,
                    OnlineModeSetting.AllowForSession => _sessionAccessGranted,
                    OnlineModeSetting.AskEachTime => _temporaryAccessExpiry.HasValue && DateTime.Now < _temporaryAccessExpiry.Value,
                    OnlineModeSetting.Off => _temporaryAccessExpiry.HasValue && DateTime.Now < _temporaryAccessExpiry.Value,
                    _ => false
                };
            }
        }

        /// <summary>
        /// Time remaining for temporary access (null if not active or permanent).
        /// </summary>
        public TimeSpan? TemporaryAccessRemaining
        {
            get
            {
                if (_temporaryAccessExpiry.HasValue && DateTime.Now < _temporaryAccessExpiry.Value)
                {
                    return _temporaryAccessExpiry.Value - DateTime.Now;
                }
                return null;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Request online access for a web operation.
        /// Returns immediately if access is already granted, otherwise requests consent.
        /// </summary>
        public async Task<OnlineAccessResult> RequestAccessAsync(string query)
        {
            // Check if already allowed
            if (IsOnlineAccessActive)
            {
                Debug.WriteLine($"[OnlineMode] Access granted (already active) for: {query}");
                return OnlineAccessResult.Allowed;
            }

            // If setting is Off or AskEachTime, request consent
            if (_setting == OnlineModeSetting.Off || _setting == OnlineModeSetting.AskEachTime)
            {
                if (ConsentRequested != null)
                {
                    Debug.WriteLine($"[OnlineMode] Requesting consent for: {query}");
                    var result = await ConsentRequested.Invoke(query);
                    return HandleConsentResult(result);
                }
                else
                {
                    Debug.WriteLine("[OnlineMode] No consent handler registered, denying access");
                    return OnlineAccessResult.Denied;
                }
            }

            return OnlineAccessResult.Denied;
        }

        /// <summary>
        /// Request online access with user input context (for permission prompts).
        /// Returns true if access granted, false if denied.
        /// </summary>
        public async Task<bool> RequestAccessAsync(string reason, string userInput)
        {
            var result = await RequestAccessAsync(reason);
            return result == OnlineAccessResult.Allowed;
        }

        /// <summary>
        /// Set the online mode from settings UI.
        /// </summary>
        public void SetMode(string mode)
        {
            Debug.WriteLine($"[OnlineMode] Setting mode to: {mode}");
            
            switch (mode)
            {
                case "Off":
                    Setting = OnlineModeSetting.Off;
                    _temporaryAccessExpiry = null;
                    _sessionAccessGranted = false;
                    break;
                case "AskEachTime":
                    Setting = OnlineModeSetting.AskEachTime;
                    _temporaryAccessExpiry = null;
                    _sessionAccessGranted = false;
                    break;
                case "AllowForSession":
                    Setting = OnlineModeSetting.AllowForSession;
                    _sessionAccessGranted = true;
                    break;
                case "AlwaysAllow":
                    Setting = OnlineModeSetting.AlwaysAllow;
                    break;
            }
            
            OnlineModeChanged?.Invoke(IsOnlineAccessActive);
        }

        /// <summary>
        /// Grant temporary access for a specified duration.
        /// </summary>
        public void GrantTemporaryAccess(TimeSpan duration)
        {
            _temporaryAccessExpiry = DateTime.Now.Add(duration);
            StartExpiryTimer();
            Debug.WriteLine($"[OnlineMode] Temporary access granted for {duration.TotalMinutes} minutes");
            OnlineModeChanged?.Invoke(true);
        }

        /// <summary>
        /// Grant access for the current session only.
        /// </summary>
        public void GrantSessionAccess()
        {
            _sessionAccessGranted = true;
            Debug.WriteLine("[OnlineMode] Session access granted");
            OnlineModeChanged?.Invoke(true);
        }

        /// <summary>
        /// Revoke all temporary and session access.
        /// </summary>
        public void RevokeAccess()
        {
            _temporaryAccessExpiry = null;
            _sessionAccessGranted = false;
            StopExpiryTimer();
            Debug.WriteLine("[OnlineMode] Access revoked");
            OnlineModeChanged?.Invoke(false);
        }

        /// <summary>
        /// Get the tooltip text for the online indicator.
        /// </summary>
        public string GetIndicatorTooltip()
        {
            if (!IsOnlineAccessActive)
            {
                return "Online Mode\nOffline - Web access disabled";
            }

            return "Online Mode\nRead-only web research enabled\nNo logins • No purchases • No system changes";
        }

        /// <summary>
        /// Get the status text for display.
        /// </summary>
        public string GetStatusText()
        {
            if (!IsOnlineAccessActive)
            {
                return "Offline";
            }

            var remaining = TemporaryAccessRemaining;
            if (remaining.HasValue)
            {
                return $"Online · {remaining.Value.Minutes:D2}:{remaining.Value.Seconds:D2}";
            }

            return _setting == OnlineModeSetting.AllowForSession ? "Online (Session)" : "Online";
        }

        #endregion

        #region Consent Response Messages

        /// <summary>
        /// Get the confirmation message for "Allow once".
        /// </summary>
        public static string GetAllowOnceConfirmation()
        {
            return "Understood. I'll use the web for this request only.";
        }

        /// <summary>
        /// Get the confirmation message for "Allow for duration".
        /// </summary>
        public static string GetAllowDurationConfirmation(int minutes)
        {
            return $"Online access enabled for the next {minutes} minutes. I'll stay in read-only mode.";
        }

        /// <summary>
        /// Get the message when user denies permission.
        /// </summary>
        public static string GetDeniedMessage()
        {
            return "No problem. I'll stay offline.";
        }

        /// <summary>
        /// Get the follow-up message after denial.
        /// </summary>
        public static string GetDeniedFollowUp()
        {
            return "If you tell me your budget or what you're looking for, I can still help you choose.";
        }

        /// <summary>
        /// Get the message when temporary access expires.
        /// </summary>
        public static string GetExpiredMessage()
        {
            return "Online access has expired. I can continue offline or ask again if needed.";
        }

        /// <summary>
        /// Get the voice-friendly consent request.
        /// </summary>
        public static string GetVoiceConsentRequest()
        {
            return "I can look that up online if you'd like. It's read-only and won't change anything. Should I proceed?";
        }

        #endregion

        #region Private Methods

        private OnlineAccessResult HandleConsentResult(OnlineConsentResult result)
        {
            switch (result.Decision)
            {
                case OnlineConsentDecision.AllowOnce:
                    // Grant very short temporary access (just for this request)
                    GrantTemporaryAccess(TimeSpan.FromSeconds(30));
                    Debug.WriteLine("[OnlineMode] Allow once granted");
                    IntentLogger.LogOnlineConsent("", "AllowOnce", null);
                    return OnlineAccessResult.Allowed;

                case OnlineConsentDecision.AllowForDuration:
                    GrantTemporaryAccess(TimeSpan.FromMinutes(result.DurationMinutes));
                    Debug.WriteLine($"[OnlineMode] Allow for {result.DurationMinutes} minutes granted");
                    IntentLogger.LogOnlineConsent("", "AllowForDuration", result.DurationMinutes);
                    return OnlineAccessResult.Allowed;

                case OnlineConsentDecision.Denied:
                    Debug.WriteLine("[OnlineMode] Access denied by user");
                    IntentLogger.LogOnlineConsent("", "Denied", null);
                    return OnlineAccessResult.Denied;

                default:
                    return OnlineAccessResult.Denied;
            }
        }

        private void StartExpiryTimer()
        {
            StopExpiryTimer();

            _expiryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _expiryTimer.Tick += ExpiryTimer_Tick;
            _expiryTimer.Start();
        }

        private void StopExpiryTimer()
        {
            if (_expiryTimer != null)
            {
                _expiryTimer.Stop();
                _expiryTimer.Tick -= ExpiryTimer_Tick;
                _expiryTimer = null;
            }
        }

        private void ExpiryTimer_Tick(object? sender, EventArgs e)
        {
            var remaining = TemporaryAccessRemaining;
            if (remaining.HasValue)
            {
                TemporaryAccessTick?.Invoke(remaining.Value);
            }
            else
            {
                // Access expired
                _temporaryAccessExpiry = null;
                StopExpiryTimer();
                Debug.WriteLine("[OnlineMode] Temporary access expired");
                TemporaryAccessExpired?.Invoke();
                OnlineModeChanged?.Invoke(false);
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<OnlineModeSettings>(json);
                    if (settings != null)
                    {
                        _setting = settings.Setting;
                        Debug.WriteLine($"[OnlineMode] Loaded setting: {_setting}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnlineMode] Failed to load settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var settings = new OnlineModeSettings
                {
                    Setting = _setting,
                    LastModified = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnlineMode] Failed to save settings: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// User's consent decision.
    /// </summary>
    public enum OnlineConsentDecision
    {
        AllowOnce,
        AllowForSession,
        AllowForDuration,  // Legacy - kept for compatibility
        Denied
    }

    /// <summary>
    /// Result of a consent request.
    /// </summary>
    public class OnlineConsentResult
    {
        public OnlineConsentDecision Decision { get; set; }
        public int DurationMinutes { get; set; } = 10;  // Legacy - kept for compatibility
    }

    /// <summary>
    /// Persisted online mode settings.
    /// </summary>
    internal class OnlineModeSettings
    {
        [JsonPropertyName("setting")]
        public OnlineModeSetting Setting { get; set; } = OnlineModeSetting.AskEachTime;

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }
    }
}
