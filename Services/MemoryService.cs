using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Services;

public sealed class LocalUserProfile
{
    public string PreferredName { get; set; } = "";
    public string Accent { get; set; } = "";
    public int BanterLevel { get; set; } = 2; // 1-5
    public int Verbosity { get; set; } = 3;   // 1-5
    public string MostUsedModule { get; set; } = "";
    public List<string> FavoriteActions { get; set; } = new();

    public void Sanitize()
    {
        PreferredName = (PreferredName ?? string.Empty).Trim();
        Accent = (Accent ?? string.Empty).Trim();
        BanterLevel = Math.Clamp(BanterLevel, 1, 5);
        Verbosity = Math.Clamp(Verbosity, 1, 5);
        MostUsedModule = (MostUsedModule ?? string.Empty).Trim();

        FavoriteActions ??= new List<string>();
        var cleaned = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in FavoriteActions)
        {
            var t = (a ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (seen.Add(t)) cleaned.Add(t);
            if (cleaned.Count >= 20) break;
        }
        FavoriteActions = cleaned;
    }
}

public sealed class MemoryService : IMemoryService
{
    private readonly object _lock = new();
    private bool _initialized;
    private LocalUserProfile _current = new();

    private DateTime _lastSaveUtc = DateTime.MinValue;
    private int _saveVersion;

    private DateTime _lastVerbosityAdjustUtc = DateTime.MinValue;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");

    private static readonly string ProfilePath = Path.Combine(AppDataDir, "UserProfile.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true
    };

    public void Initialize()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            _current = TryLoadInternal() ?? new LocalUserProfile();
            _current.Sanitize();
            try
            {
                if (!File.Exists(ProfilePath))
                {
                    if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
                    var json = JsonSerializer.Serialize(_current, JsonOptions);
                    File.WriteAllText(ProfilePath, json);
                }
            }
            catch
            {
            }
            _initialized = true;
        }

        try
        {
            PreferencesStore.Instance.PreferencesChanged += OnPreferencesChanged;
            // Prime MostUsedModule immediately.
            TryUpdateMostUsedModuleFromPreferences(PreferencesStore.Instance.Current);
        }
        catch
        {
        }
    }

    public LocalUserProfile GetSnapshot()
    {
        Initialize();
        lock (_lock)
        {
            return new LocalUserProfile
            {
                PreferredName = _current.PreferredName,
                Accent = _current.Accent,
                BanterLevel = _current.BanterLevel,
                Verbosity = _current.Verbosity,
                MostUsedModule = _current.MostUsedModule,
                FavoriteActions = new List<string>(_current.FavoriteActions ?? new List<string>())
            };
        }
    }

    public void Update(Action<LocalUserProfile> updater)
    {
        if (updater == null) return;
        Initialize();

        lock (_lock)
        {
            updater(_current);
            _current.Sanitize();
        }

        _ = SaveDebouncedAsync();
    }

    public void ObserveUserMessage(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return;
        Initialize();

        // Only infer style signals for non-slash commands.
        var t = userText.Trim();
        if (t.StartsWith("/", StringComparison.Ordinal)) return;

        // Heuristic: if the user explicitly requests short replies, gradually reduce verbosity.
        // Map to a 1-5 integer scale.
        var lower = t.ToLowerInvariant();
        int? target = null;

        if (ContainsAny(lower, "be brief", "be concise", "keep it short", "short answer", "tl;dr", "tldr", "one sentence", "quickly"))
            target = 1;
        else if (ContainsAny(lower, "more detail", "in detail", "detailed", "deep dive", "thorough", "step by step", "explain"))
            target = 5;
        else
        {
            var len = t.Length;
            if (len <= 40) target = 2;
            else if (len >= 350) target = 4;
        }

        if (target == null) return;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            // Avoid rapid oscillation.
            if (_lastVerbosityAdjustUtc != DateTime.MinValue && (now - _lastVerbosityAdjustUtc).TotalMinutes < 2)
                return;

            var before = Math.Clamp(_current.Verbosity, 1, 5);
            var after = before;
            if (target.Value < before) after = before - 1;
            else if (target.Value > before) after = before + 1;

            after = Math.Clamp(after, 1, 5);
            if (after == before) return;

            _current.Verbosity = after;
            _current.Sanitize();
            _lastVerbosityAdjustUtc = now;
        }

        _ = SaveDebouncedAsync();
    }

    public string BuildPromptMemoryBlock(LocalUserProfile snapshot)
    {
        if (snapshot == null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("LOCAL MEMORY PROFILE (local-only; no cloud)");

        if (!string.IsNullOrWhiteSpace(snapshot.PreferredName))
            sb.AppendLine($"- PreferredName: {snapshot.PreferredName}");
        if (!string.IsNullOrWhiteSpace(snapshot.Accent))
            sb.AppendLine($"- Accent: {snapshot.Accent}");

        sb.AppendLine($"- BanterLevel: {Math.Clamp(snapshot.BanterLevel, 1, 5)}/5");
        sb.AppendLine($"- Verbosity: {Math.Clamp(snapshot.Verbosity, 1, 5)}/5");

        if (!string.IsNullOrWhiteSpace(snapshot.MostUsedModule))
            sb.AppendLine($"- MostUsedModule: {snapshot.MostUsedModule}");

        if (snapshot.FavoriteActions != null && snapshot.FavoriteActions.Count > 0)
            sb.AppendLine($"- FavoriteActions: {string.Join(", ", snapshot.FavoriteActions.Take(8))}");

        return sb.ToString().TrimEnd();
    }

    public void RecordAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        Initialize();

        lock (_lock)
        {
            _current.FavoriteActions ??= new List<string>();
            var t = action.Trim();
            _current.FavoriteActions.RemoveAll(a => string.Equals(a, t, StringComparison.OrdinalIgnoreCase));
            _current.FavoriteActions.Insert(0, t);
            if (_current.FavoriteActions.Count > 20)
                _current.FavoriteActions.RemoveRange(20, _current.FavoriteActions.Count - 20);
            _current.Sanitize();
        }

        _ = SaveDebouncedAsync();
    }

    private void OnPreferencesChanged(object? sender, UserPreferences prefs)
    {
        try
        {
            TryUpdateMostUsedModuleFromPreferences(prefs);
        }
        catch
        {
        }
    }

    private void TryUpdateMostUsedModuleFromPreferences(UserPreferences prefs)
    {
        var best = PersonalityLearningService.GetMostUsedModuleSnapshot(prefs);
        if (best == null) return;

        var friendly = FriendlyModuleName(best.Value.ModuleId);
        if (string.IsNullOrWhiteSpace(friendly)) return;

        bool changed;
        lock (_lock)
        {
            changed = !string.Equals((_current.MostUsedModule ?? "").Trim(), friendly.Trim(), StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                _current.MostUsedModule = friendly.Trim();
                _current.Sanitize();
            }
        }

        if (changed)
            _ = SaveDebouncedAsync();
    }

    private static string FriendlyModuleName(string id)
    {
        var t = (id ?? "").Trim().ToUpperInvariant();
        return t switch
        {
            "AI CHAT" => "AI Chat",
            "AI MEDIA CENTRE" => "Media Centre",
            "AI DJ BOOTH" => "DJ Booth",
            "AI DOWNLOADS" => "Downloads",
            "AI SECURITY" => "Security",
            "AI CREATE" => "Create",
            "AI CODE" => "Code",
            _ => string.IsNullOrWhiteSpace(id) ? "" : id.Trim()
        };
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static LocalUserProfile? TryLoadInternal()
    {
        try
        {
            if (!File.Exists(ProfilePath)) return null;
            var json = File.ReadAllText(ProfilePath);
            return JsonSerializer.Deserialize<LocalUserProfile>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoryService] Load failed: {ex.Message}");
            return null;
        }
    }

    private async Task SaveDebouncedAsync()
    {
        int myVersion;
        lock (_lock)
        {
            myVersion = ++_saveVersion;
        }

        try
        {
            await Task.Delay(250).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        lock (_lock)
        {
            if (myVersion != _saveVersion) return;

            var now = DateTime.UtcNow;
            if (_lastSaveUtc != DateTime.MinValue && (now - _lastSaveUtc).TotalMilliseconds < 250)
                return;

            TrySaveInternal();
            _lastSaveUtc = now;
        }
    }

    private void TrySaveInternal()
    {
        try
        {
            if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(_current, JsonOptions);
            File.WriteAllText(ProfilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoryService] Save failed: {ex.Message}");
        }
    }
}
