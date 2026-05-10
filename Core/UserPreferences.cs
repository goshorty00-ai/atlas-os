using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AtlasAI.Core
{
    /// <summary>
    /// Strongly-typed user preferences model.
    /// Stores ONLY safe, non-sensitive data.
    /// 
    /// ALLOWED: UI choices, behavior settings, command IDs
    /// NOT ALLOWED: file paths, credentials, chat content, PII
    /// </summary>
    public class UserPreferences
    {
        // Schema version for migration support
        public int SchemaVersion { get; set; } = 2;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        // ═══════════════════════════════════════════════════════════════
        // UI / EXPERIENCE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Theme variant (dark-cyan, dark-purple, etc.)</summary>
        public string ThemeVariant { get; set; } = "dark-cyan";

        /// <summary>Whether Agent Mode is enabled by default on startup</summary>
        public bool AgentModeDefaultOn { get; set; } = false;

        /// <summary>Particle count preference (clamped 0-100)</summary>
        public int ParticleCount { get; set; } = 50;

        /// <summary>Command palette hotkey (default: Ctrl+Space)</summary>
        public string CommandPaletteHotkey { get; set; } = "Ctrl+Space";

        /// <summary>Show floating overlay hints</summary>
        public bool ShowOverlayHints { get; set; } = true;

        // ═══════════════════════════════════════════════════════════════
        // FLOATING HUD (Alive Companion Mode)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Enable the floating HUD window</summary>
        public bool FloatingHudEnabled { get; set; } = false;

        /// <summary>HUD screen position (corner anchor)</summary>
        public HudPosition FloatingHudPosition { get; set; } = HudPosition.BottomRight;

        /// <summary>HUD size (Small or Medium)</summary>
        public HudSize FloatingHudSize { get; set; } = HudSize.Medium;

        /// <summary>Enable click-through mode (HUD ignores mouse)</summary>
        public bool FloatingHudClickThrough { get; set; } = false;

        // ═══════════════════════════════════════════════════════════════
        // VISUAL POLISH (Effects and Animations)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Enable particle trails in HoloCore during Thinking/Speaking</summary>
        public bool EnableParticleTrails { get; set; } = true;

        /// <summary>Enable depth parallax effect in HoloCore</summary>
        public bool EnableParallaxEffect { get; set; } = true;

        /// <summary>Enable idle breathing animation on Floating HUD</summary>
        public bool EnableIdleBreathing { get; set; } = true;

        // ═══════════════════════════════════════════════════════════════
        // CORE COLORS (Customizable orb appearance)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Primary core color (hex format, e.g., "#22d3ee" for cyan)</summary>
        public string CorePrimaryColor { get; set; } = "#22d3ee";

        /// <summary>Secondary/light core color (hex format)</summary>
        public string CoreSecondaryColor { get; set; } = "#7dd3fc";

        /// <summary>Thinking/accent color (hex format, default orange)</summary>
        public string CoreThinkingColor { get; set; } = "#f97316";

        /// <summary>Core style variant (Classic, EnergySphere, QuantumCore, Nebula, Minimal)</summary>
        public string CoreStyle { get; set; } = "Classic";

        /// <summary>Core color preset (Cyan, Purple, Green, Red, Gold, Orange, IceBlue, Pink)</summary>
        public string CoreColorPreset { get; set; } = "Cyan";

        /// <summary>Core animation speed multiplier (0.1-3.0, default 1.0)</summary>
        public double CoreAnimationSpeed { get; set; } = 1.0;

        /// <summary>Core particle count (50-300, default 120)</summary>
        public int CoreParticleCount { get; set; } = 120;

        /// <summary>Core ring rotation speed multiplier (0.1-5.0, default 1.0)</summary>
        public double CoreRingSpeed { get; set; } = 1.0;

        // ═══════════════════════════════════════════════════════════════
        // AUDIO CUES (Optional, OFF by default)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Enable optional micro audio cues (OFF by default)</summary>
        public bool EnableAudioCues { get; set; } = false;

        /// <summary>Audio cue volume (0-1, default very low)</summary>
        public double AudioCueVolume { get; set; } = 0.15;

        // ═══════════════════════════════════════════════════════════════
        // VOICE SYSTEM (Privacy-First)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Enable wake word detection (default ON for convenience)</summary>
        public bool EnableWakeWord { get; set; } = true;
        
        /// <summary>Enable audio cue when wake word is detected (default OFF)</summary>
        public bool EnableWakeWordAudioCue { get; set; } = true;

        /// <summary>Enable always-listening mode (default OFF for privacy)</summary>
        public bool EnableAlwaysListening { get; set; } = false;

        /// <summary>Selected microphone device name (empty = default)</summary>
        public string MicrophoneDevice { get; set; } = "";

        public string MicrophoneDeviceId { get; set; } = "";

        /// <summary>Wake word sensitivity (0.0 = low, 1.0 = high)</summary>
        public double WakeWordSensitivity { get; set; } = 0.5;

        /// <summary>Follow-up listening duration in seconds (2-10, default 4)</summary>
        public double FollowUpListeningDuration { get; set; } = 2.5;

        /// <summary>Wake word cooldown in milliseconds (500-5000, default 1500)</summary>
        public int WakeWordCooldownMs { get; set; } = 1500;

        /// <summary>Show listening indicator in UI (default ON)</summary>
        public bool ShowListeningIndicator { get; set; } = true;

        /// <summary>
        /// Disable Whisper Speech-to-Text (OpenAI) and always use Windows Speech fallback.
        /// Useful when OpenAI credits/quota are unavailable.
        /// </summary>
        public bool DisableWhisperStt { get; set; } = false;

        // ═══════════════════════════════════════════════════════════════
        // ONLINE MODE (Read-only web research)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Online mode setting (Off, AskEachTime, AllowForSession, AlwaysAllow)</summary>
        public string OnlineMode { get; set; } = "AskEachTime";

        /// <summary>Online mode temporary access expiry (UTC)</summary>
        public DateTime? OnlineModeExpiry { get; set; } = null;

        // ═══════════════════════════════════════════════════════════════
        // SAFETY & PERMISSIONS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Enable microphone access (default ON)</summary>
        public bool EnableMicrophone { get; set; } = true;

        /// <summary>Enable dangerous/destructive actions (default OFF - safe)</summary>
        public bool DangerousActionsEnabled { get; set; } = false;

        /// <summary>Allow registry cleanup operations (default OFF - requires DangerousActionsEnabled)</summary>
        public bool AllowRegistryCleanup { get; set; } = false;

        // ═══════════════════════════════════════════════════════════════
        // BEHAVIOR
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Response style: Concise, Balanced, Detailed</summary>
        public ResponseStyle ResponseStyle { get; set; } = ResponseStyle.Balanced;

        /// <summary>Show confidence statement in chat responses</summary>
        public bool ShowConfidenceStatement { get; set; } = true;

        /// <summary>Selected persona profile</summary>
        public PersonaType Persona { get; set; } = PersonaType.Neutral;

        /// <summary>Enable proactive suggestions</summary>
        public bool EnableProactiveSuggestions { get; set; } = true;

        /// <summary>Last time a proactive module suggestion was shown</summary>
        public DateTime LastModuleSuggestionUtc { get; set; } = DateTime.MinValue;

        /// <summary>Selected personality pack (Atlas, Serious, Cold, Funny, Friendly)</summary>
        public string SelectedPersonalityId { get; set; } = "Atlas";

        public bool SpeechEnabled { get; set; } = true;

        public bool AmbientDimEnabled { get; set; } = false;

        public bool FirstRunWizardComplete { get; set; } = false;

        public string AnimatedLogoMode { get; set; } = "globe";

        public string ChatHeaderLottie { get; set; } = "";

        /// <summary>
        /// Optional override Lottie file name (e.g. "Spinning Globe.json") used for the Media Centre sidebar.
        /// Empty = Auto (use category-based default mapping).
        /// Stores file name only (no paths).
        /// </summary>
        public string MediaCentreSidebarLottie { get; set; } = "";

        public string CommandCenterWallpaperMode { get; set; } = "";

        public string CommandCenterWallpaperPath { get; set; } = "";

        public string CodeWorkspaceFolder { get; set; } = "";

        public DateTime ProfanityLearningWindowStartUtc { get; set; } = DateTime.MinValue;

        public int ProfanityLearningHits { get; set; } = 0;

        public DateTime LastBanterAutoAdjustUtc { get; set; } = DateTime.MinValue;

        // ═══════════════════════════════════════════════════════════════
        // ALIVE MODE (Natural AI Responses)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Enable Alive Mode - prefer natural AI replies over templates.
        /// When ON: LLM responses win by default, templates are last resort.
        /// When OFF: Stricter quality gates and more template fallbacks.
        /// </summary>
        public bool AliveModeEnabled { get; set; } = true;

        // ═══════════════════════════════════════════════════════════════
        // MEDIA CENTRE FOLDER CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Folder paths for Movies section (semicolon-separated)</summary>
        public string MoviesFolders { get; set; } = "";

        /// <summary>Folder paths for TV Shows section (semicolon-separated)</summary>
        public string TVShowsFolders { get; set; } = "";

        /// <summary>Folder paths for Music section (semicolon-separated)</summary>
        public string MusicFolders { get; set; } = "";

        /// <summary>Folder paths for Games section (semicolon-separated)</summary>
        public string GamesFolders { get; set; } = "";

        /// <summary>Folder paths for Images section (semicolon-separated)</summary>
        public string ImagesFolders { get; set; } = "";

        /// <summary>Folder paths for Collections section (semicolon-separated)</summary>
        public string CollectionsFolders { get; set; } = "";

        /// <summary>Last media scan timestamp (UTC)</summary>
        public DateTime LastMediaScan { get; set; } = DateTime.MinValue;

        /// <summary>Enable automatic media scanning on startup</summary>
        public bool EnableAutoMediaScan { get; set; } = true;

        public string MediaDownloadsFolder { get; set; } = "";

        public string KaraokeFolders { get; set; } = "";

        public List<string> WatchLaterMediaIds { get; set; } = new();

        public const int MaxWatchLaterMediaIds = 500;

        public class AppBookmark
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Url { get; set; } = "";
        }

        public List<AppBookmark> AppsBookmarks { get; set; } = new();

        public bool CsvTranscodeToMp3 { get; set; } = true;

        public bool CsvGenerateM3uPlaylist { get; set; } = false;

        public string CsvVariants { get; set; } = "Official Audio";

        public int CsvMinDurationSeconds { get; set; } = 45;

        public int CsvMaxDurationSeconds { get; set; } = 900;

        public bool CsvExcludeInstrumentals { get; set; } = false;

        public bool CsvEmbedThumbnails { get; set; } = true;

        public string ChatPersonality { get; set; } = "Buddy";

        public int ChatBanterLevel { get; set; } = 3;

        public int ChatHumanLevel { get; set; } = 3;

        public int SavageLevel { get; set; } = 3;

        public bool ChatAllowProfanity { get; set; } = false;

        public bool ChatAllowPlayfulRoast { get; set; } = false;

        public bool ChatCheckInsEnabled { get; set; } = true;

        public int ChatCheckInMinMinutes { get; set; } = 15;

        public int ChatCheckInMaxMinutes { get; set; } = 45;

        public int ChatCheckInIdleMinutes { get; set; } = 10;

        public string ChatPreferredName { get; set; } = "";

        public string ChatAccent { get; set; } = "Irish";

        public bool ChatOnboardingComplete { get; set; } = false;

        // ═══════════════════════════════════════════════════════════════
        // USAGE SIGNALS (IDs only - no sensitive data)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Top 10 most-used command IDs with usage counts</summary>
        public Dictionary<string, int> MostUsedCommands { get; set; } = new();

        public Dictionary<string, int> MostUsedModules { get; set; } = new();

        public List<string> RecentModuleIds { get; set; } = new();

        /// <summary>Last 20 recently executed command IDs</summary>
        public List<string> RecentCommandIds { get; set; } = new();

        /// <summary>User-pinned command IDs for quick access</summary>
        public List<string> PinnedCommandIds { get; set; } = new();

        /// <summary>Last command type used (for type bias)</summary>
        public string LastUsedCommandType { get; set; } = "";

        // ═══════════════════════════════════════════════════════════════
        // CONSTANTS
        // ═══════════════════════════════════════════════════════════════

        public const int MaxRecentCommands = 20;
        public const int MaxMostUsedCommands = 10;
        public const int MaxPinnedCommands = 10;
        public const int MinParticleCount = 0;
        public const int MaxParticleCount = 100;

        /// <summary>
        /// Create default preferences
        /// </summary>
        public static UserPreferences CreateDefault()
        {
            return new UserPreferences
            {
                SchemaVersion = 1,
                LastModified = DateTime.UtcNow,
                ThemeVariant = "dark-cyan",
                AgentModeDefaultOn = false,
                ParticleCount = 50,
                CommandPaletteHotkey = "Ctrl+Space",
                ShowOverlayHints = true,
                FloatingHudEnabled = false,
                FloatingHudPosition = HudPosition.BottomRight,
                FloatingHudSize = HudSize.Medium,
                FloatingHudClickThrough = false,
                EnableParticleTrails = true,
                EnableParallaxEffect = true,
                EnableIdleBreathing = true,
                CorePrimaryColor = "#22d3ee",
                CoreSecondaryColor = "#7dd3fc",
                CoreThinkingColor = "#f97316",
                CoreStyle = "Classic",
                CoreColorPreset = "Cyan",
                CoreAnimationSpeed = 1.0,
                CoreParticleCount = 120,
                CoreRingSpeed = 1.0,
                EnableAudioCues = false,
                AudioCueVolume = 0.15,
                EnableWakeWord = true,
                EnableWakeWordAudioCue = true,
                EnableAlwaysListening = false,
                MicrophoneDevice = "",
                MicrophoneDeviceId = "",
                WakeWordSensitivity = 0.5,
                ShowListeningIndicator = true,
                WakeWordCooldownMs = 1500,
                OnlineMode = "AskEachTime",
                OnlineModeExpiry = null,
                EnableMicrophone = true,
                DangerousActionsEnabled = false,
                AllowRegistryCleanup = false,
                ResponseStyle = ResponseStyle.Balanced,
                ShowConfidenceStatement = true,
                Persona = PersonaType.Neutral,
                EnableProactiveSuggestions = true,
                SelectedPersonalityId = "Atlas",
                SpeechEnabled = true,
                AmbientDimEnabled = false,
                FirstRunWizardComplete = false,
                AnimatedLogoMode = "globe",
                ChatHeaderLottie = "",
                MediaCentreSidebarLottie = "",
                CommandCenterWallpaperMode = "",
                CommandCenterWallpaperPath = "",
                CodeWorkspaceFolder = "",
                ProfanityLearningWindowStartUtc = DateTime.MinValue,
                ProfanityLearningHits = 0,
                LastBanterAutoAdjustUtc = DateTime.MinValue,
                AliveModeEnabled = true,
                MoviesFolders = "",
                TVShowsFolders = "",
                MusicFolders = "",
                GamesFolders = "",
                ImagesFolders = "",
                CollectionsFolders = "",
                LastMediaScan = DateTime.MinValue,
                EnableAutoMediaScan = true,
                MediaDownloadsFolder = "",
                KaraokeFolders = "",
                WatchLaterMediaIds = new List<string>(),
                AppsBookmarks = new List<AppBookmark>(),
                CsvTranscodeToMp3 = true,
                CsvGenerateM3uPlaylist = false,
                CsvVariants = "Official Audio",
                CsvMinDurationSeconds = 45,
                CsvMaxDurationSeconds = 900,
                CsvExcludeInstrumentals = false,
                CsvEmbedThumbnails = true,
                ChatPersonality = "Buddy",
                ChatBanterLevel = 3,
                ChatHumanLevel = 3,
                SavageLevel = 3,
                ChatAllowProfanity = false,
                ChatAllowPlayfulRoast = false,
                ChatCheckInsEnabled = true,
                ChatCheckInMinMinutes = 15,
                ChatCheckInMaxMinutes = 45,
                ChatCheckInIdleMinutes = 10,
                ChatPreferredName = "",
                ChatAccent = "Irish",
                ChatOnboardingComplete = false,
                MostUsedCommands = new Dictionary<string, int>(),
                MostUsedModules = new Dictionary<string, int>(),
                RecentModuleIds = new List<string>(),
                RecentCommandIds = new List<string>(),
                PinnedCommandIds = new List<string>(),
                LastUsedCommandType = ""
            };
        }

        /// <summary>
        /// Validate and clamp values to safe ranges
        /// </summary>
        public void Sanitize()
        {
            // Clamp particle count
            ParticleCount = Math.Clamp(ParticleCount, MinParticleCount, MaxParticleCount);

            // Clamp audio volume
            AudioCueVolume = Math.Clamp(AudioCueVolume, 0, 1);

            // Clamp wake word sensitivity
            WakeWordSensitivity = Math.Clamp(WakeWordSensitivity, 0, 1);

            // Clamp follow-up listening duration (2-10 seconds)
            FollowUpListeningDuration = Math.Clamp(FollowUpListeningDuration, 2.0, 10.0);

            // Clamp wake word cooldown (500-5000 ms)
            WakeWordCooldownMs = Math.Clamp(WakeWordCooldownMs, 500, 5000);

            // Clamp core animation settings
            CoreAnimationSpeed = Math.Clamp(CoreAnimationSpeed, 0.1, 3.0);
            CoreParticleCount = Math.Clamp(CoreParticleCount, 50, 300);
            CoreRingSpeed = Math.Clamp(CoreRingSpeed, 0.1, 5.0);

            // Validate online mode
            var validOnlineModes = new[] { "Off", "AskEachTime", "AllowForSession", "AlwaysAllow" };
            if (!validOnlineModes.Contains(OnlineMode))
                OnlineMode = "Off";

            // Sanitize microphone device name
            MicrophoneDevice = SanitizeString(MicrophoneDevice, "");
            MicrophoneDeviceId = (MicrophoneDeviceId ?? "").Trim();

            // Ensure collections exist
            MostUsedCommands ??= new Dictionary<string, int>();
            RecentCommandIds ??= new List<string>();
            PinnedCommandIds ??= new List<string>();
            MostUsedModules ??= new Dictionary<string, int>();
            RecentModuleIds ??= new List<string>();

            // Trim collections to max size
            while (RecentCommandIds.Count > MaxRecentCommands)
                RecentCommandIds.RemoveAt(RecentCommandIds.Count - 1);

            while (PinnedCommandIds.Count > MaxPinnedCommands)
                PinnedCommandIds.RemoveAt(PinnedCommandIds.Count - 1);

            // Trim most-used to top N
            if (MostUsedCommands.Count > MaxMostUsedCommands)
            {
                var sorted = new List<KeyValuePair<string, int>>(MostUsedCommands);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                MostUsedCommands.Clear();
                for (int i = 0; i < MaxMostUsedCommands && i < sorted.Count; i++)
                    MostUsedCommands[sorted[i].Key] = sorted[i].Value;
            }

            // Validate enums
            if (!Enum.IsDefined(typeof(ResponseStyle), ResponseStyle))
                ResponseStyle = ResponseStyle.Balanced;

            if (!Enum.IsDefined(typeof(PersonaType), Persona))
                Persona = PersonaType.Neutral;

            if (!Enum.IsDefined(typeof(HudPosition), FloatingHudPosition))
                FloatingHudPosition = HudPosition.BottomRight;

            if (!Enum.IsDefined(typeof(HudSize), FloatingHudSize))
                FloatingHudSize = HudSize.Medium;

            // Sanitize strings (remove any potential path-like content)
            ThemeVariant = SanitizeString(ThemeVariant, "dark-cyan");
            CommandPaletteHotkey = SanitizeString(CommandPaletteHotkey, "Ctrl+Space");
            LastUsedCommandType = SanitizeString(LastUsedCommandType, "");
            CoreStyle = SanitizeString(CoreStyle, "Classic");
            CoreColorPreset = SanitizeString(CoreColorPreset, "Cyan");
            AnimatedLogoMode = SanitizeString(AnimatedLogoMode, "globe");
            ChatHeaderLottie = (ChatHeaderLottie ?? "").Trim();
            MediaCentreSidebarLottie = Path.GetFileName((MediaCentreSidebarLottie ?? "").Trim());
            CommandCenterWallpaperMode = (CommandCenterWallpaperMode ?? "").Trim();
            CommandCenterWallpaperPath = (CommandCenterWallpaperPath ?? "").Trim();
            CodeWorkspaceFolder = SanitizeFolderPaths(CodeWorkspaceFolder);
            if (!string.IsNullOrWhiteSpace(CodeWorkspaceFolder) && CodeWorkspaceFolder.Contains(';'))
                CodeWorkspaceFolder = CodeWorkspaceFolder.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
            ProfanityLearningHits = Math.Clamp(ProfanityLearningHits, 0, 10000);
            
            // Validate personality ID
            var validPersonalities = new[] { "Atlas", "Professional", "Serious", "Cold", "Funny", "Friendly" };
            if (!validPersonalities.Contains(SelectedPersonalityId))
                SelectedPersonalityId = "Atlas";

            // Sanitize media folder paths (allow paths but validate format)
            MoviesFolders = SanitizeFolderPaths(MoviesFolders);
            TVShowsFolders = SanitizeFolderPaths(TVShowsFolders);
            MusicFolders = SanitizeFolderPaths(MusicFolders);
            GamesFolders = SanitizeFolderPaths(GamesFolders);
            ImagesFolders = SanitizeFolderPaths(ImagesFolders);
            CollectionsFolders = SanitizeFolderPaths(CollectionsFolders);

            MediaDownloadsFolder = SanitizeFolderPaths(MediaDownloadsFolder);
            KaraokeFolders = SanitizeFolderPaths(KaraokeFolders);

            WatchLaterMediaIds ??= new List<string>();
            while (WatchLaterMediaIds.Count > MaxWatchLaterMediaIds)
                WatchLaterMediaIds.RemoveAt(0);

            AppsBookmarks ??= new List<AppBookmark>();
            if (AppsBookmarks.Count > 200)
                AppsBookmarks = AppsBookmarks.Take(200).ToList();
            foreach (var b in AppsBookmarks)
            {
                if (b == null) continue;
                b.Id = (b.Id ?? "").Trim();
                b.Name = (b.Name ?? "").Trim();
                b.Url = (b.Url ?? "").Trim();
            }

            CsvMinDurationSeconds = Math.Clamp(CsvMinDurationSeconds, 0, 3600);
            CsvMaxDurationSeconds = Math.Clamp(CsvMaxDurationSeconds, 0, 7200);
            CsvVariants = (CsvVariants ?? "").Trim();

            ChatBanterLevel = Math.Clamp(ChatBanterLevel, 1, 5);
            ChatHumanLevel = Math.Clamp(ChatHumanLevel, 1, 5);
            SavageLevel = Math.Clamp(SavageLevel, 1, 5);
            ChatCheckInMinMinutes = Math.Clamp(ChatCheckInMinMinutes, 0, 240);
            ChatCheckInMaxMinutes = Math.Clamp(ChatCheckInMaxMinutes, 0, 240);
            ChatCheckInIdleMinutes = Math.Clamp(ChatCheckInIdleMinutes, 0, 240);
            ChatPersonality = SanitizeString(ChatPersonality, "Buddy");
            ChatPreferredName = (ChatPreferredName ?? "").Trim();
            ChatAccent = SanitizeString(ChatAccent, "Irish");
        }

        private static string SanitizeString(string? value, string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            // Block anything that looks like a path or contains sensitive chars
            if (value.Contains('\\') || value.Contains('/') || 
                value.Contains(':') || value.Length > 50)
                return defaultValue;

            return value.Trim();
        }

        private static string SanitizeFolderPaths(string? folderPaths)
        {
            if (string.IsNullOrWhiteSpace(folderPaths))
                return "";

            try
            {
                // Split by semicolon, validate each path, rejoin
                var paths = folderPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
                var validPaths = new List<string>();

                foreach (var path in paths)
                {
                    var trimmedPath = path.Trim();
                    
                    // Basic path validation - must be absolute Windows path
                    if (trimmedPath.Length >= 3 && 
                        char.IsLetter(trimmedPath[0]) && 
                        trimmedPath[1] == ':' && 
                        (trimmedPath[2] == '\\' || trimmedPath[2] == '/') &&
                        trimmedPath.Length <= 260) // MAX_PATH limit
                    {
                        validPaths.Add(trimmedPath);
                    }
                }

                return string.Join(";", validPaths);
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>
    /// Response verbosity style
    /// </summary>
    public enum ResponseStyle
    {
        Concise,    // Brief, to-the-point
        Balanced,   // Default middle ground
        Detailed    // More explanation
    }

    /// <summary>
    /// Persona profiles affecting tone/messaging
    /// </summary>
    public enum PersonaType
    {
        Neutral,    // Default balanced tone
        Jarvis,     // Calm, professional, helpful
        Ultron      // Sharper, more direct, efficient
    }

    /// <summary>
    /// Floating HUD screen position (corner anchor)
    /// </summary>
    public enum HudPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Floating HUD size
    /// </summary>
    public enum HudSize
    {
        Small,      // 80x100 - minimal footprint
        Medium      // 100x120 - default
    }
}
