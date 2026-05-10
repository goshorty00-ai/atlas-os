using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AtlasAI.Settings;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Voice tag categories for filtering.
    /// </summary>
    [Flags]
    public enum VoiceTag
    {
        None = 0,
        Male = 1,
        Female = 2,
        Robotic = 4,
        Warm = 8,
        Deep = 16,
        Calm = 32,
        British = 64,
        American = 128,
        Narrator = 256
    }

    /// <summary>
    /// Extended voice profile with tags for catalog display.
    /// </summary>
    public class CatalogVoice
    {
        public string VoiceId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Provider { get; set; } = "ElevenLabs";
        public VoiceTag Tags { get; set; } = VoiceTag.None;
        public double CadenceMultiplier { get; set; } = 1.0;
        public bool IsDefault { get; set; }
        public string? RecommendedFor { get; set; } // e.g., "Atlas", "Serious"

        /// <summary>
        /// Convert to VoiceProfile for use in voice selection.
        /// </summary>
        public VoiceProfile ToVoiceProfile() => new()
        {
            VoiceId = VoiceId,
            DisplayName = DisplayName,
            Description = Description,
            Provider = Provider,
            CadenceMultiplier = CadenceMultiplier
        };
    }

    /// <summary>
    /// Single source of truth for available TTS voices.
    /// Loads from ElevenLabs API or falls back to static list.
    /// </summary>
    public class VoiceCatalogService
    {
        private static VoiceCatalogService? _instance;
        private static readonly object _lock = new();

        public static VoiceCatalogService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VoiceCatalogService();
                    }
                }
                return _instance;
            }
        }

        private List<CatalogVoice>? _cachedVoices;
        private DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Event fired when voice catalog is refreshed.
        /// </summary>
        public event Action<IReadOnlyList<CatalogVoice>>? CatalogRefreshed;

        /// <summary>
        /// Static fallback voices when API is unavailable.
        /// </summary>
        private static readonly List<CatalogVoice> StaticVoices = new()
        {
            new CatalogVoice
            {
                VoiceId = VoiceProfile.DefaultAtlasVoiceId,
                DisplayName = "Daniel (Atlas Default)",
                Description = "Calm, confident British cadence with measured delivery",
                Tags = VoiceTag.Male | VoiceTag.Robotic | VoiceTag.Calm | VoiceTag.British,
                CadenceMultiplier = 0.95,
                IsDefault = true,
                RecommendedFor = "Atlas"
            },
            new CatalogVoice
            {
                VoiceId = "pNInz6obpgDQGcFmaJgB",
                DisplayName = "Adam (Narrator)",
                Description = "Deep, authoritative narrator voice",
                Tags = VoiceTag.Male | VoiceTag.Deep | VoiceTag.Narrator,
                CadenceMultiplier = 0.9,
                RecommendedFor = "Serious"
            },
            new CatalogVoice
            {
                VoiceId = "VR6AewLTigWG4xSOukaG",
                DisplayName = "Arnold (Deep)",
                Description = "Deep, terse male voice",
                Tags = VoiceTag.Male | VoiceTag.Deep,
                CadenceMultiplier = 0.85,
                RecommendedFor = "Cold"
            },
            new CatalogVoice
            {
                VoiceId = "ErXwobaYiN019PkySvjV",
                DisplayName = "Antoni (Warm)",
                Description = "Warm, approachable male voice",
                Tags = VoiceTag.Male | VoiceTag.Warm,
                CadenceMultiplier = 1.0,
                RecommendedFor = "Friendly"
            },
            new CatalogVoice
            {
                VoiceId = "21m00Tcm4TlvDq8ikWAM",
                DisplayName = "Rachel (Calm)",
                Description = "Calm, professional female voice",
                Tags = VoiceTag.Female | VoiceTag.Calm,
                CadenceMultiplier = 1.0
            },
            new CatalogVoice
            {
                VoiceId = "AZnzlk1XvdvUeBnXmlld",
                DisplayName = "Domi (Strong)",
                Description = "Strong, confident female voice",
                Tags = VoiceTag.Female,
                CadenceMultiplier = 1.0
            },
            new CatalogVoice
            {
                VoiceId = "EXAVITQu4vr4xnSDxMaL",
                DisplayName = "Bella (Soft)",
                Description = "Soft, gentle female voice",
                Tags = VoiceTag.Female | VoiceTag.Warm,
                CadenceMultiplier = 1.0
            }
        };

        private VoiceCatalogService()
        {
            Debug.WriteLine("[VoiceCatalog] Initialized");
        }

        /// <summary>
        /// Get all available voices. Tries ElevenLabs API first, falls back to static list.
        /// </summary>
        public async Task<IReadOnlyList<CatalogVoice>> GetVoicesAsync(CancellationToken ct = default)
        {
            // Return cached if valid
            if (_cachedVoices != null && DateTime.Now - _cacheTime < CacheDuration)
            {
                return _cachedVoices;
            }

            try
            {
                // Route 1: try via the live VoiceManager (already has key configured)
                var voiceManager = GetVoiceManager();
                if (voiceManager != null)
                {
                    var elevenLabsProvider = voiceManager.GetProvider(VoiceProviderType.ElevenLabs) as ElevenLabsProvider;
                    if (elevenLabsProvider != null && await elevenLabsProvider.IsAvailableAsync(ct))
                    {
                        var apiVoices = await elevenLabsProvider.GetVoicesAsync(ct);
                        var catalogVoices = ConvertToCatalogVoices(apiVoices);
                        _cachedVoices = catalogVoices;
                        _cacheTime = DateTime.Now;
                        Debug.WriteLine($"[VoiceCatalog] Loaded {catalogVoices.Count} voices from ElevenLabs (via VoiceManager)");
                        CatalogRefreshed?.Invoke(catalogVoices);
                        return catalogVoices;
                    }
                }

                // Route 2: read key from SettingsStore and fetch ourselves
                var savedKey = ReadElevenLabsKeyFromDisk();
                if (!string.IsNullOrWhiteSpace(savedKey))
                {
                    var tempProvider = new ElevenLabsProvider();
                    tempProvider.Configure(new Dictionary<string, string> { ["ApiKey"] = savedKey });
                    if (await tempProvider.IsAvailableAsync(ct))
                    {
                        var apiVoices = await tempProvider.GetVoicesAsync(ct);
                        var catalogVoices = ConvertToCatalogVoices(apiVoices);
                        _cachedVoices = catalogVoices;
                        _cacheTime = DateTime.Now;
                        Debug.WriteLine($"[VoiceCatalog] Loaded {catalogVoices.Count} voices from ElevenLabs (direct key)");
                        CatalogRefreshed?.Invoke(catalogVoices);
                        return catalogVoices;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceCatalog] API fetch failed: {ex.Message}, using static list");
            }

            // Fall back to static list
            _cachedVoices = new List<CatalogVoice>(StaticVoices);
            _cacheTime = DateTime.Now;
            Debug.WriteLine($"[VoiceCatalog] Using {_cachedVoices.Count} static voices");
            CatalogRefreshed?.Invoke(_cachedVoices);
            return _cachedVoices;
        }

        /// <summary>
        /// Read the ElevenLabs API key from the live settings store.
        /// </summary>
        private static string? ReadElevenLabsKeyFromDisk()
        {
            try
            {
                if (SettingsStore.TryGetVoiceProviderKey("elevenlabs", out var key) && !string.IsNullOrWhiteSpace(key))
                    return key;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceCatalog] Could not read key from SettingsStore: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Get voices synchronously (uses cache or static list).
        /// </summary>
        public IReadOnlyList<CatalogVoice> GetVoices()
        {
            if (_cachedVoices != null)
                return _cachedVoices;

            return StaticVoices;
        }

        /// <summary>
        /// Get a specific voice by ID.
        /// </summary>
        public CatalogVoice? GetVoice(string voiceId)
        {
            var voices = GetVoices();
            return voices.FirstOrDefault(v => v.VoiceId == voiceId);
        }

        /// <summary>
        /// Get the default voice for a personality.
        /// </summary>
        public CatalogVoice? GetDefaultForPersonality(PersonalityId personality)
        {
            var voices = GetVoices();
            var personalityName = personality.ToString();
            return voices.FirstOrDefault(v => v.RecommendedFor == personalityName)
                ?? voices.FirstOrDefault(v => v.IsDefault);
        }

        /// <summary>
        /// Force refresh the voice catalog.
        /// </summary>
        public async Task RefreshAsync(CancellationToken ct = default)
        {
            _cachedVoices = null;
            _cacheTime = DateTime.MinValue;
            await GetVoicesAsync(ct);
        }

        /// <summary>
        /// Convert VoiceInfo list to CatalogVoice list.
        /// </summary>
        private List<CatalogVoice> ConvertToCatalogVoices(IReadOnlyList<VoiceInfo> voiceInfos)
        {
            var result = new List<CatalogVoice>();

            foreach (var info in voiceInfos)
            {
                // Check if we have a static definition for this voice
                var staticVoice = StaticVoices.FirstOrDefault(v => v.VoiceId == info.Id);
                
                if (staticVoice != null)
                {
                    result.Add(staticVoice);
                }
                else
                {
                    // Create new catalog entry from API data
                    var tags = VoiceTag.None;
                    if (info.Gender?.ToLower() == "male") tags |= VoiceTag.Male;
                    if (info.Gender?.ToLower() == "female") tags |= VoiceTag.Female;

                    result.Add(new CatalogVoice
                    {
                        VoiceId = info.Id,
                        DisplayName = info.DisplayName ?? info.Id,
                        Description = $"{info.Gender ?? "Unknown"} voice",
                        Provider = info.Provider.ToString(),
                        Tags = tags,
                        CadenceMultiplier = 1.0
                    });
                }
            }

            // Ensure static voices are included even if not in API response
            foreach (var staticVoice in StaticVoices)
            {
                if (!result.Any(v => v.VoiceId == staticVoice.VoiceId))
                {
                    result.Add(staticVoice);
                }
            }

            // Sort: recommended voices first, then alphabetically
            result.Sort((a, b) =>
            {
                if (a.IsDefault && !b.IsDefault) return -1;
                if (!a.IsDefault && b.IsDefault) return 1;
                if (!string.IsNullOrEmpty(a.RecommendedFor) && string.IsNullOrEmpty(b.RecommendedFor)) return -1;
                if (string.IsNullOrEmpty(a.RecommendedFor) && !string.IsNullOrEmpty(b.RecommendedFor)) return 1;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        /// <summary>
        /// Try to get VoiceManager from App resources.
        /// </summary>
        private VoiceManager? GetVoiceManager()
        {
            try
            {
                foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is ChatWindow chatWindow)
                        return chatWindow.VoiceManager;
                }

                if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
                {
                    // Access via reflection or public property if available
                    var field = mainWindow.GetType().GetField("_voiceManager", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    return field?.GetValue(mainWindow) as VoiceManager;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceCatalog] Could not get VoiceManager: {ex.Message}");
            }
            return null;
        }
    }
}
