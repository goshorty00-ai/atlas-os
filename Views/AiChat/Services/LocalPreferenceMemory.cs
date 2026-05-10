using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace AtlasAI.Views.AiChat.Services;

public sealed class LocalPreferenceMemory
{
    // Explicit only: preferences the user stated directly.
    public Dictionary<string, PreferenceItem> ExplicitPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Inferred only: verbosity (concise/balanced/detailed).
    public VerbosityMemory Verbosity { get; set; } = new();

    public void Sanitize()
    {
        if (ExplicitPreferences == null)
            ExplicitPreferences = new Dictionary<string, PreferenceItem>(StringComparer.OrdinalIgnoreCase);

        // Normalize keys/values.
        var normalized = new Dictionary<string, PreferenceItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ExplicitPreferences)
        {
            var key = (kv.Key ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            var item = kv.Value ?? new PreferenceItem();
            item.Key = key;
            item.Value = (item.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(item.Value)) continue;

            normalized[key] = item;
        }

        ExplicitPreferences = normalized;

        Verbosity ??= new VerbosityMemory();
        Verbosity.Sanitize();
    }
}

public sealed class PreferenceItem
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string? Evidence { get; set; }
}

public sealed class VerbosityMemory
{
    // 0.0 = concise, 0.5 = balanced, 1.0 = detailed
    public double Score { get; set; } = 0.5;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string? Evidence { get; set; }

    public string Level
    {
        get
        {
            if (Score <= 0.33) return "concise";
            if (Score >= 0.67) return "detailed";
            return "balanced";
        }
    }

    public void Sanitize()
    {
        Score = Math.Clamp(Score, 0, 1);
    }
}

public sealed class LocalPreferenceMemoryStore
{
    private static readonly Lazy<LocalPreferenceMemoryStore> LazyInstance = new(() => new LocalPreferenceMemoryStore());
    public static LocalPreferenceMemoryStore Instance => LazyInstance.Value;

    private readonly object _lock = new();
    private LocalPreferenceMemory _current = new();
    private bool _loaded;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");

    private static readonly string MemoryPath = Path.Combine(AppDataDir, "local_preference_memory.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private LocalPreferenceMemoryStore() { }

    public LocalPreferenceMemory Current
    {
        get
        {
            EnsureLoaded();
            lock (_lock)
            {
                var copy = new LocalPreferenceMemory
                {
                    Verbosity = new VerbosityMemory
                    {
                        Score = _current.Verbosity.Score,
                        UpdatedUtc = _current.Verbosity.UpdatedUtc,
                        Evidence = _current.Verbosity.Evidence
                    }
                };

                foreach (var kv in _current.ExplicitPreferences)
                {
                    copy.ExplicitPreferences[kv.Key] = new PreferenceItem
                    {
                        Key = kv.Value.Key,
                        Value = kv.Value.Value,
                        UpdatedUtc = kv.Value.UpdatedUtc,
                        Evidence = kv.Value.Evidence
                    };
                }

                copy.Sanitize();
                return copy;
            }
        }
    }

    public void RememberExplicit(string key, string value, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (string.IsNullOrWhiteSpace(value)) return;

        EnsureLoaded();
        lock (_lock)
        {
            var k = key.Trim();
            _current.ExplicitPreferences[k] = new PreferenceItem
            {
                Key = k,
                Value = value.Trim(),
                UpdatedUtc = DateTime.UtcNow,
                Evidence = string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim()
            };

            _current.Sanitize();
            TrySaveInternal();
        }
    }

    public void Forget(string query, out List<string> forgottenKeys, out bool verbosityReset)
    {
        forgottenKeys = new List<string>();
        verbosityReset = false;

        if (string.IsNullOrWhiteSpace(query)) return;

        EnsureLoaded();
        var q = Normalize(query);

        lock (_lock)
        {
            // Allow forgetting inferred verbosity explicitly.
            if (q == "verbosity" || q == "response style" || q == "style")
            {
                _current.Verbosity.Score = 0.5;
                _current.Verbosity.UpdatedUtc = DateTime.UtcNow;
                _current.Verbosity.Evidence = "reset";
                verbosityReset = true;
            }

            // Small synonym mapping for common user phrasing.
            var keyHint = q switch
            {
                "name" or "my name" or "preferred name" => "preferred_name",
                "accent" => "accent",
                "banter" => "banter",
                _ => null
            };

            var candidates = _current.ExplicitPreferences.Keys.ToList();
            foreach (var key in candidates)
            {
                var keyNorm = Normalize(key);
                if (keyHint != null)
                {
                    if (string.Equals(keyNorm, keyHint, StringComparison.OrdinalIgnoreCase))
                    {
                        _current.ExplicitPreferences.Remove(key);
                        forgottenKeys.Add(key);
                    }
                    continue;
                }

                if (keyNorm.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    _current.ExplicitPreferences.Remove(key);
                    forgottenKeys.Add(key);
                }
            }

            _current.Sanitize();
            TrySaveInternal();
        }
    }

    public void ObserveUserMessage(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return;

        EnsureLoaded();
        var t = userText.Trim();
        var lower = t.ToLowerInvariant();

        // Heuristic: explicit verbosity hints in the user's message.
        double? target = null;
        string? evidence = null;

        if (ContainsAny(lower, "be brief", "be concise", "keep it short", "short answer", "tl;dr", "tldr", "one sentence", "quickly"))
        {
            target = 0.15;
            evidence = "explicit_concise_hint";
        }
        else if (ContainsAny(lower, "more detail", "in detail", "detailed", "deep dive", "thorough", "step by step", "explain"))
        {
            target = 0.85;
            evidence = "explicit_detailed_hint";
        }
        else
        {
            // Implicit inference based on message length.
            var len = t.Length;
            if (len <= 40) { target = 0.30; evidence = "length_short"; }
            else if (len >= 350) { target = 0.70; evidence = "length_long"; }
        }

        if (target == null) return;

        lock (_lock)
        {
            // EMA update to avoid wild swings.
            _current.Verbosity.Score = (_current.Verbosity.Score * 0.8) + (target.Value * 0.2);
            _current.Verbosity.UpdatedUtc = DateTime.UtcNow;
            _current.Verbosity.Evidence = evidence;
            _current.Verbosity.Sanitize();
            TrySaveInternal();
        }
    }

    public string BuildShowText()
    {
        EnsureLoaded();
        lock (_lock)
        {
            var lines = new List<string>();

            lines.Add("LOCAL PREFERENCE MEMORY");
            lines.Add($"verbosity: {_current.Verbosity.Level} (score={_current.Verbosity.Score:0.00})");

            if (_current.ExplicitPreferences.Count == 0)
            {
                lines.Add("explicit preferences: (none)");
            }
            else
            {
                lines.Add("explicit preferences:");
                foreach (var kv in _current.ExplicitPreferences.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add($"- {kv.Key}: {kv.Value.Value}");
                }
            }

            lines.Add("\nCommands: \"show what you remember\" · \"forget <key>\"");
            return string.Join("\n", lines);
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _current = TryLoadInternal() ?? new LocalPreferenceMemory();
            _current.Sanitize();
            _loaded = true;
        }
    }

    private static LocalPreferenceMemory? TryLoadInternal()
    {
        try
        {
            if (!File.Exists(MemoryPath)) return null;
            var json = File.ReadAllText(MemoryPath);
            return JsonSerializer.Deserialize<LocalPreferenceMemory>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalPreferenceMemoryStore] Load failed: {ex.Message}");
            return null;
        }
    }

    private void TrySaveInternal()
    {
        try
        {
            if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(_current, JsonOptions);
            File.WriteAllText(MemoryPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalPreferenceMemoryStore] Save failed: {ex.Message}");
        }
    }

    private static string Normalize(string s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant();
        while (t.EndsWith(".") || t.EndsWith("!") || t.EndsWith("?"))
            t = t.Substring(0, t.Length - 1).Trim();
        return t;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
