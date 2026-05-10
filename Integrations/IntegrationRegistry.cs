using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasAI.Integrations
{
    /// <summary>
    /// Central registry of all Atlas AI integrations and their capabilities
    /// Scans and tracks what apps/services Atlas can connect to
    /// </summary>
    public static class IntegrationRegistry
    {
        private static List<IntegrationInfo> _integrations = new();
        private static bool _initialized = false;

        /// <summary>
        /// Initialize and scan all available integrations
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            
            _integrations = new List<IntegrationInfo>
            {
                // ==================== MUSIC & MEDIA ====================
                new IntegrationInfo
                {
                    Id = "spotify",
                    Name = "Spotify",
                    Category = IntegrationCategory.Music,
                    Icon = "🎵",
                    Description = "Play music, control playback, search songs",
                    Capabilities = new[] { "Play songs/artists/playlists", "Pause/Resume/Skip", "Search music", "Volume control" },
                    RequiresApiKey = false,
                    RequiresApp = true,
                    AppName = "Spotify",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Play Shape of You", "Pause music", "Next song", "Play my liked songs" }
                },
                new IntegrationInfo
                {
                    Id = "youtube",
                    Name = "YouTube",
                    Category = IntegrationCategory.Music,
                    Icon = "📺",
                    Description = "Play videos and music on YouTube",
                    Capabilities = new[] { "Play videos", "Search YouTube", "Play music videos" },
                    RequiresApiKey = false,
                    RequiresApp = false,
                    WebUrl = "https://youtube.com",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Play Bohemian Rhapsody on YouTube", "Search YouTube for cat videos" }
                },
                new IntegrationInfo
                {
                    Id = "youtube_music",
                    Name = "YouTube Music",
                    Category = IntegrationCategory.Music,
                    Icon = "🎶",
                    Description = "Stream music on YouTube Music",
                    Capabilities = new[] { "Play songs", "Search music", "Play playlists" },
                    RequiresApiKey = false,
                    WebUrl = "https://music.youtube.com",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Play Drake on YouTube Music" }
                },
                new IntegrationInfo
                {
                    Id = "soundcloud",
                    Name = "SoundCloud",
                    Category = IntegrationCategory.Music,
                    Icon = "☁️",
                    Description = "Stream music on SoundCloud",
                    Capabilities = new[] { "Play tracks", "Search artists", "Discover music" },
                    RequiresApiKey = false,
                    WebUrl = "https://soundcloud.com",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Play lo-fi beats on SoundCloud" }
                },
                new IntegrationInfo
                {
                    Id = "apple_music",
                    Name = "Apple Music",
                    Category = IntegrationCategory.Music,
                    Icon = "🍎",
                    Description = "Stream music on Apple Music/iTunes",
                    Capabilities = new[] { "Play songs", "Search library", "Control playback" },
                    RequiresApiKey = false,
                    RequiresApp = true,
                    AppName = "iTunes",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Play Taylor Swift on Apple Music" }
                },

                // ==================== DESIGN & CREATIVE ====================
                new IntegrationInfo
                {
                    Id = "canva",
                    Name = "Canva",
                    Category = IntegrationCategory.Design,
                    Icon = "🎨",
                    Description = "Create designs - social posts, logos, presentations",
                    Capabilities = new[] { "Create Instagram/Facebook posts", "Design logos", "Make presentations", "Color palette suggestions", "Font pairing advice" },
                    RequiresApiKey = true,
                    ApiKeyName = "canva",
                    ApiKeyUrl = "https://www.canva.com/developers/",
                    FreeWithoutKey = true,
                    FreeModeDescription = "Design guidance (colors, fonts, tips) - API enables automated creation",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Create an Instagram post", "Design a logo for my business", "Make a presentation" }
                },
                new IntegrationInfo
                {
                    Id = "dalle",
                    Name = "DALL-E (Image Generation)",
                    Category = IntegrationCategory.Design,
                    Icon = "🖼️",
                    Description = "Generate AI images from text descriptions",
                    Capabilities = new[] { "Generate images from prompts", "Create artwork", "Design concepts" },
                    RequiresApiKey = true,
                    ApiKeyName = "openai",
                    ApiKeyUrl = "https://platform.openai.com/api-keys",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Generate an image of a sunset over mountains", "Create a logo concept" }
                },

                // ==================== PRODUCTIVITY ====================
                new IntegrationInfo
                {
                    Id = "file_manager",
                    Name = "File Manager",
                    Category = IntegrationCategory.Productivity,
                    Icon = "📁",
                    Description = "Organize, move, rename files and folders",
                    Capabilities = new[] { "Organize files by type", "Consolidate folders", "Rename files", "Delete duplicates", "Clean up downloads" },
                    RequiresApiKey = false,
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Organize my downloads folder", "Put all songs in one folder", "Clean up my desktop" }
                },
                new IntegrationInfo
                {
                    Id = "clipboard",
                    Name = "Clipboard Manager",
                    Category = IntegrationCategory.Productivity,
                    Icon = "📋",
                    Description = "Advanced clipboard with history",
                    Capabilities = new[] { "Clipboard history", "Pin items", "Search history", "Quick paste" },
                    RequiresApiKey = false,
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Show clipboard history", "Open clipboard manager" }
                },
                new IntegrationInfo
                {
                    Id = "screen_capture",
                    Name = "Screen Capture",
                    Category = IntegrationCategory.Productivity,
                    Icon = "📸",
                    Description = "Take screenshots with AI analysis",
                    Capabilities = new[] { "Capture screen", "Capture window", "Capture region", "AI image analysis", "OCR text extraction" },
                    RequiresApiKey = false,
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Take a screenshot", "Capture this window", "What's on my screen?" }
                },

                // ==================== SYSTEM & SECURITY ====================
                new IntegrationInfo
                {
                    Id = "system_control",
                    Name = "System Control",
                    Category = IntegrationCategory.System,
                    Icon = "⚙️",
                    Description = "Control Windows settings and apps",
                    Capabilities = new[] { "Open apps", "System settings", "Volume control", "Brightness", "WiFi/Bluetooth" },
                    RequiresApiKey = false,
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Open Chrome", "Turn up the volume", "Open settings", "What's my IP?" }
                },
                new IntegrationInfo
                {
                    Id = "security_suite",
                    Name = "Security Suite",
                    Category = IntegrationCategory.System,
                    Icon = "🛡️",
                    Description = "Scan for malware, spyware, and threats",
                    Capabilities = new[] { "Quick scan", "Full scan", "Spyware detection", "Threat removal", "Privacy scan" },
                    RequiresApiKey = false,
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Scan my computer", "Check for spyware", "Open security suite" }
                },
                new IntegrationInfo
                {
                    Id = "software_installer",
                    Name = "Software Installer",
                    Category = IntegrationCategory.System,
                    Icon = "📦",
                    Description = "Install and uninstall software",
                    Capabilities = new[] { "Install apps via winget", "Uninstall programs", "Check if installed" },
                    RequiresApiKey = false,
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Install VLC", "Uninstall Spotify", "Is Chrome installed?" }
                },

                // ==================== WEB & SEARCH ====================
                new IntegrationInfo
                {
                    Id = "web_search",
                    Name = "Web Search",
                    Category = IntegrationCategory.Web,
                    Icon = "🔍",
                    Description = "Search the web and get answers",
                    Capabilities = new[] { "Web search", "Get weather", "Find information", "Open URLs" },
                    RequiresApiKey = false,
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Search for Python tutorials", "What's the weather?", "Open google.com" }
                },

                // ==================== AI & VOICE ====================
                new IntegrationInfo
                {
                    Id = "openai",
                    Name = "OpenAI (GPT)",
                    Category = IntegrationCategory.AI,
                    Icon = "🤖",
                    Description = "AI chat and assistance powered by GPT",
                    Capabilities = new[] { "Natural conversation", "Code help", "Writing assistance", "Problem solving" },
                    RequiresApiKey = true,
                    ApiKeyName = "openai",
                    ApiKeyUrl = "https://platform.openai.com/api-keys",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Help me write an email", "Explain this code", "What's the best way to..." }
                },
                new IntegrationInfo
                {
                    Id = "claude",
                    Name = "Claude (Anthropic)",
                    Category = IntegrationCategory.AI,
                    Icon = "🧠",
                    Description = "AI chat powered by Claude",
                    Capabilities = new[] { "Natural conversation", "Analysis", "Writing", "Coding help" },
                    RequiresApiKey = true,
                    ApiKeyName = "claude",
                    ApiKeyUrl = "https://console.anthropic.com/",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Analyze this document", "Help me brainstorm" }
                },
                new IntegrationInfo
                {
                    Id = "elevenlabs",
                    Name = "ElevenLabs Voice",
                    Category = IntegrationCategory.AI,
                    Icon = "🎙️",
                    Description = "Premium AI voice synthesis",
                    Capabilities = new[] { "Natural speech", "Custom voices", "Emotion control" },
                    RequiresApiKey = true,
                    ApiKeyName = "elevenlabs",
                    ApiKeyUrl = "https://elevenlabs.io/",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Use ElevenLabs voice" }
                },
                new IntegrationInfo
                {
                    Id = "edge_tts",
                    Name = "Edge TTS (Free)",
                    Category = IntegrationCategory.AI,
                    Icon = "🔊",
                    Description = "Free Microsoft neural voices",
                    Capabilities = new[] { "Text to speech", "Multiple voices", "Multiple languages" },
                    RequiresApiKey = false,
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Use Edge voice" }
                },
                new IntegrationInfo
                {
                    Id = "whisper",
                    Name = "Whisper (Speech Recognition)",
                    Category = IntegrationCategory.AI,
                    Icon = "👂",
                    Description = "AI-powered speech recognition",
                    Capabilities = new[] { "Voice commands", "Dictation", "Transcription" },
                    RequiresApiKey = true,
                    ApiKeyName = "openai",
                    Status = IntegrationStatus.Available,
                    ExampleCommands = new[] { "Enable voice input" }
                },

                // ==================== COMING SOON ====================
                new IntegrationInfo
                {
                    Id = "notion",
                    Name = "Notion",
                    Category = IntegrationCategory.Productivity,
                    Icon = "📝",
                    Description = "Notes, docs, and project management",
                    Capabilities = new[] { "Create pages", "Search notes", "Update databases" },
                    RequiresApiKey = true,
                    ApiKeyUrl = "https://www.notion.so/my-integrations",
                    Status = IntegrationStatus.ComingSoon,
                    ExampleCommands = new[] { "Add to my Notion", "Search my notes" }
                },
                new IntegrationInfo
                {
                    Id = "google_calendar",
                    Name = "Google Calendar",
                    Category = IntegrationCategory.Productivity,
                    Icon = "📅",
                    Description = "Manage your calendar and events",
                    Capabilities = new[] { "Create events", "Check schedule", "Set reminders" },
                    RequiresApiKey = true,
                    Status = IntegrationStatus.ComingSoon,
                    ExampleCommands = new[] { "What's on my calendar?", "Schedule a meeting" }
                },
                new IntegrationInfo
                {
                    Id = "discord",
                    Name = "Discord",
                    Category = IntegrationCategory.Communication,
                    Icon = "💬",
                    Description = "Send messages and manage Discord",
                    Capabilities = new[] { "Send messages", "Join voice", "Manage servers" },
                    RequiresApiKey = true,
                    Status = IntegrationStatus.ComingSoon,
                    ExampleCommands = new[] { "Send a Discord message", "Join voice channel" }
                },
                new IntegrationInfo
                {
                    Id = "slack",
                    Name = "Slack",
                    Category = IntegrationCategory.Communication,
                    Icon = "💼",
                    Description = "Team communication and messaging",
                    Capabilities = new[] { "Send messages", "Search channels", "Set status" },
                    RequiresApiKey = true,
                    Status = IntegrationStatus.ComingSoon,
                    ExampleCommands = new[] { "Send Slack message to #general" }
                },
                new IntegrationInfo
                {
                    Id = "github",
                    Name = "GitHub",
                    Category = IntegrationCategory.Development,
                    Icon = "🐙",
                    Description = "Manage repos, issues, and PRs",
                    Capabilities = new[] { "Create issues", "Check PRs", "Clone repos" },
                    RequiresApiKey = true,
                    ApiKeyUrl = "https://github.com/settings/tokens",
                    Status = IntegrationStatus.ComingSoon,
                    ExampleCommands = new[] { "Create GitHub issue", "Show my PRs" }
                },
                new IntegrationInfo
                {
                    Id = "home_assistant",
                    Name = "Home Assistant",
                    Category = IntegrationCategory.SmartHome,
                    Icon = "🏠",
                    Description = "Control smart home devices",
                    Capabilities = new[] { "Control lights", "Adjust thermostat", "Lock doors", "Check sensors" },
                    RequiresApiKey = true,
                    Status = IntegrationStatus.ComingSoon,
                    ExampleCommands = new[] { "Turn off the lights", "Set temperature to 20" }
                },
                new IntegrationInfo
                {
                    Id = "philips_hue",
                    Name = "Philips Hue",
                    Category = IntegrationCategory.SmartHome,
                    Icon = "💡",
                    Description = "Control Philips Hue lights",
                    Capabilities = new[] { "Turn on/off", "Change colors", "Set scenes", "Dim lights" },
                    RequiresApiKey = true,
                    Status = IntegrationStatus.ComingSoon,
                    ExampleCommands = new[] { "Turn lights blue", "Dim the bedroom" }
                }
            };

            // Check which integrations are actually configured
            CheckIntegrationStatus();
            
            _initialized = true;
        }

        /// <summary>
        /// Check status of each integration (API keys, apps installed, etc.)
        /// </summary>
        private static void CheckIntegrationStatus()
        {
            var integrationKeys = SettingsWindow.GetIntegrationApiKeys();
            
            foreach (var integration in _integrations)
            {
                if (integration.Status == IntegrationStatus.ComingSoon)
                    continue;

                // Check if API key is configured
                if (integration.RequiresApiKey && !string.IsNullOrEmpty(integration.ApiKeyName))
                {
                    if (integrationKeys.TryGetValue(integration.ApiKeyName, out var key) && !string.IsNullOrEmpty(key))
                    {
                        integration.IsConfigured = true;
                    }
                    else if (integration.FreeWithoutKey)
                    {
                        integration.IsConfigured = true; // Works without key in limited mode
                    }
                }
                else
                {
                    integration.IsConfigured = true; // No API key needed
                }
            }
        }

        /// <summary>
        /// Get all integrations
        /// </summary>
        public static List<IntegrationInfo> GetAll()
        {
            if (!_initialized) Initialize();
            return _integrations.ToList();
        }

        /// <summary>
        /// Get integrations by category
        /// </summary>
        public static List<IntegrationInfo> GetByCategory(IntegrationCategory category)
        {
            if (!_initialized) Initialize();
            return _integrations.Where(i => i.Category == category).ToList();
        }

        /// <summary>
        /// Get available (working) integrations
        /// </summary>
        public static List<IntegrationInfo> GetAvailable()
        {
            if (!_initialized) Initialize();
            return _integrations.Where(i => i.Status == IntegrationStatus.Available).ToList();
        }

        /// <summary>
        /// Get configured integrations (API keys set up)
        /// </summary>
        public static List<IntegrationInfo> GetConfigured()
        {
            if (!_initialized) Initialize();
            CheckIntegrationStatus();
            return _integrations.Where(i => i.IsConfigured && i.Status == IntegrationStatus.Available).ToList();
        }

        /// <summary>
        /// Get coming soon integrations
        /// </summary>
        public static List<IntegrationInfo> GetComingSoon()
        {
            if (!_initialized) Initialize();
            return _integrations.Where(i => i.Status == IntegrationStatus.ComingSoon).ToList();
        }

        /// <summary>
        /// Get integration by ID
        /// </summary>
        public static IntegrationInfo? GetById(string id)
        {
            if (!_initialized) Initialize();
            return _integrations.FirstOrDefault(i => i.Id == id);
        }

        /// <summary>
        /// Get summary of all integrations
        /// </summary>
        public static IntegrationSummary GetSummary()
        {
            if (!_initialized) Initialize();
            CheckIntegrationStatus();

            return new IntegrationSummary
            {
                TotalIntegrations = _integrations.Count,
                AvailableCount = _integrations.Count(i => i.Status == IntegrationStatus.Available),
                ConfiguredCount = _integrations.Count(i => i.IsConfigured && i.Status == IntegrationStatus.Available),
                ComingSoonCount = _integrations.Count(i => i.Status == IntegrationStatus.ComingSoon),
                Categories = _integrations.Select(i => i.Category).Distinct().ToList()
            };
        }

        /// <summary>
        /// Refresh integration status (re-check API keys, etc.)
        /// </summary>
        public static void Refresh()
        {
            CheckIntegrationStatus();
        }
    }

    public class IntegrationInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public IntegrationCategory Category { get; set; }
        public string Icon { get; set; } = "🔌";
        public string Description { get; set; } = "";
        public string[] Capabilities { get; set; } = Array.Empty<string>();
        public string[] ExampleCommands { get; set; } = Array.Empty<string>();
        
        // Configuration
        public bool RequiresApiKey { get; set; }
        public string? ApiKeyName { get; set; }
        public string? ApiKeyUrl { get; set; }
        public bool FreeWithoutKey { get; set; }
        public string? FreeModeDescription { get; set; }
        
        // App requirements
        public bool RequiresApp { get; set; }
        public string? AppName { get; set; }
        public string? WebUrl { get; set; }
        
        // Status
        public IntegrationStatus Status { get; set; } = IntegrationStatus.Available;
        public bool IsConfigured { get; set; }
    }

    public class IntegrationSummary
    {
        public int TotalIntegrations { get; set; }
        public int AvailableCount { get; set; }
        public int ConfiguredCount { get; set; }
        public int ComingSoonCount { get; set; }
        public List<IntegrationCategory> Categories { get; set; } = new();
    }

    public enum IntegrationCategory
    {
        Music,
        Design,
        Productivity,
        System,
        Web,
        AI,
        Communication,
        Development,
        SmartHome
    }

    public enum IntegrationStatus
    {
        Available,
        ComingSoon,
        Disabled,
        Error
    }
}
