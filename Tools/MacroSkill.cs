using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Tools;

public static class MacroSkill
{
    private sealed class PendingMacroRun
    {
        public string MacroName { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string PreviewText { get; init; } = "";
        public Func<CancellationToken, Task<string>> ExecuteAsync { get; init; } = _ => Task.FromResult("❌ Pending macro missing executor.");
    }

    private static readonly object Gate = new();
    private static PendingMacroRun? _pending;

    // Prevent macro execution from recursively re-entering MacroSkill.
    private static readonly AsyncLocal<int> MacroDepth = new();

    public static async Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        if (MacroDepth.Value > 0)
            return null;

        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        var clean = userMessage.Trim();
        var lower = clean.ToLowerInvariant();

        // Pending macro confirmation flow.
        var pending = GetPending();
        if (pending != null)
        {
            if (IsConfirm(lower))
                return await ConfirmPendingAsync(pending, ct);

            if (IsCancel(lower))
            {
                ClearPending();
                return "✅ Cancelled pending macro.";
            }

            if (IsShowPreview(lower))
                return pending.PreviewText;
        }

        // Define/create macro.
        var define = TryParseDefine(clean);
        if (define != null)
        {
            var (name, steps) = define.Value;
            if (steps.Count == 0)
                return "❌ Macro needs at least 1 step.";

            MacroStore.Instance.Upsert(name, steps);
            return $"✅ Saved macro '{name}' ({steps.Count} step(s)).\nRun it with: run macro {name}";
        }

        // Run/execute macro.
        var run = TryParseRun(clean);
        if (run != null)
        {
            var macro = MacroLibrary.FindByName(run);
            if (macro == null)
                return $"❌ Macro '{run}' not found.";

            return await BeginOrRunAsync(macro, ct);
        }

        // Auto-trigger macro by phrase (before AI). Keep fairly strict to avoid false positives.
        var triggered = MacroLibrary.FindByTrigger(clean);
        if (triggered != null)
            return await BeginOrRunAsync(triggered, ct);

        return null;
    }

    private static async Task<string> BeginOrRunAsync(MacroDefinition macro, CancellationToken ct)
    {
        if (macro == null)
            return "❌ Macro not found.";

        var risk = MacroRiskAnalyzer.Analyze(macro.Steps);
        if (risk.RequiresConfirmation)
        {
            var preview = BuildPreview(macro, risk);

            lock (Gate)
            {
                _pending = new PendingMacroRun
                {
                    MacroName = macro.Name,
                    CreatedUtc = DateTime.UtcNow,
                    PreviewText = preview,
                    ExecuteAsync = async ct2 => await RunMacroInternalAsync(macro, ct2)
                };
            }

            return preview;
        }

        return await RunMacroInternalAsync(macro, ct);
    }

    private static async Task<string?> ConfirmPendingAsync(PendingMacroRun pending, CancellationToken ct)
    {
        ClearPending();
        try
        {
            ct.ThrowIfCancellationRequested();
            return await pending.ExecuteAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"❌ Macro '{pending.MacroName}' failed: {ex.Message}";
        }
    }

    private static PendingMacroRun? GetPending()
    {
        lock (Gate)
        {
            if (_pending == null)
                return null;

            if ((DateTime.UtcNow - _pending.CreatedUtc) > TimeSpan.FromMinutes(5))
            {
                _pending = null;
                return null;
            }

            return _pending;
        }
    }

    private static void ClearPending()
    {
        lock (Gate)
        {
            _pending = null;
        }
    }

    private static async Task<string> RunMacroInternalAsync(MacroDefinition macro, CancellationToken ct)
    {
        MacroDepth.Value++;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"▶️ Running macro '{macro.Name}' ({macro.Steps.Count} step(s))...");

            for (int i = 0; i < macro.Steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var step = (macro.Steps[i] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(step))
                    continue;

                try
                {
                    Debug.WriteLine($"[Macro] Executing step: {step}");
                }
                catch
                {
                }

                string? result = null;
                try
                {
                    // Run each step through the existing skills/actions pipeline.
                    result = await ToolExecutor.TryExecuteToolWithCancellationAsync(step, ct);

                    // If the step armed a FileButler elevated op, auto-confirm it (macro-level confirm already happened).
                    if (!string.IsNullOrWhiteSpace(result) && result.Contains("Reply 'confirm' to execute", StringComparison.OrdinalIgnoreCase))
                    {
                        var confirm = await FileButlerSkill.TryExecuteAsync("confirm", ct);
                        if (!string.IsNullOrWhiteSpace(confirm))
                            result = confirm;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result = $"❌ {ex.Message}";
                }

                var line = result;
                if (string.IsNullOrWhiteSpace(line))
                    line = "(no matching tool/skill)";

                // Keep per-step output compact.
                line = Truncate(line, 400);
                sb.AppendLine($"{i + 1}) {step} → {line}");
            }

            return sb.ToString().TrimEnd();
        }
        finally
        {
            MacroDepth.Value = Math.Max(0, MacroDepth.Value - 1);
        }
    }

    private static (string name, List<string> steps)? TryParseDefine(string text)
    {
        // Supported:
        // - define macro <name>: step1; step2
        // - create macro <name>: step1; step2
        // - macro define <name>: ...
        // - macro create <name>: ...
        var match = Regex.Match(text,
            @"^\s*(?:(?:define|create|set)\s+macro|macro\s+(?:define|create|set))\s+(?<name>[^:=]+?)\s*(?:=|:)\s*(?<steps>.+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success)
            return null;

        var name = (match.Groups["name"].Value ?? "").Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var stepsRaw = (match.Groups["steps"].Value ?? "").Trim();
        var steps = SplitSteps(stepsRaw);

        return (name, steps);
    }

    private static string? TryParseRun(string text)
    {
        // Supported:
        // - run macro <name>
        // - execute macro <name>
        // - macro run <name>
        // - macro execute <name>
        var match = Regex.Match(text,
            @"^\s*(?:run\s+macro|execute\s+macro|macro\s+run|macro\s+execute)\s+(?<name>.+?)\s*$",
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var name = (match.Groups["name"].Value ?? "").Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        return null;
    }

    private static List<string> SplitSteps(string stepsRaw)
    {
        var raw = (stepsRaw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        // Prefer semicolon separation.
        string[] parts;
        if (raw.Contains(';'))
            parts = raw.Split(';');
        else if (raw.Contains('\n'))
            parts = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        else
            parts = new[] { raw };

        var steps = parts
            .Select(p => (p ?? "").Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        // Strip bullet prefixes.
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.StartsWith("- ")) s = s.Substring(2).Trim();
            if (s.StartsWith("• ")) s = s.Substring(2).Trim();
            steps[i] = s;
        }

        return steps;
    }

    private static string BuildPreview(MacroDefinition macro, MacroRisk risk)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🧾 Preview: Macro '{macro.Name}'");
        sb.AppendLine($"Steps: {macro.Steps.Count}");
        sb.AppendLine();

        foreach (var s in macro.Steps.Take(20))
            sb.AppendLine($"- {s}");

        if (macro.Steps.Count > 20)
            sb.AppendLine($"... and {macro.Steps.Count - 20} more");

        if (risk.FlaggedSteps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ Elevated/dangerous steps detected:");
            foreach (var f in risk.FlaggedSteps.Take(10))
                sb.AppendLine($"- {f}");
            if (risk.FlaggedSteps.Count > 10)
                sb.AppendLine($"... and {risk.FlaggedSteps.Count - 10} more");
        }

        sb.AppendLine();
        sb.AppendLine("Reply 'confirm' to execute, or 'cancel' to abort.");
        return sb.ToString().TrimEnd();
    }

    private static bool IsConfirm(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower)) return false;
        lower = lower.Trim();
        return lower == "confirm" || lower == "yes" || lower == "y" || lower == "ok" || lower == "okay" ||
               lower.StartsWith("confirm ") || lower.StartsWith("yes ") || lower.StartsWith("ok ") || lower.StartsWith("okay ") ||
               lower == "do it" || lower == "proceed";
    }

    private static bool IsCancel(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower)) return false;
        lower = lower.Trim();
        return lower == "cancel" || lower == "no" || lower == "n" || lower == "stop" || lower == "abort" ||
               lower.StartsWith("cancel ") || lower.StartsWith("no ") || lower.StartsWith("stop ") || lower.StartsWith("abort ");
    }

    private static bool IsShowPreview(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower)) return false;
        lower = lower.Trim();
        return lower == "preview" || lower == "show preview" || lower == "show me the preview";
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= max) return s;
        return s.Substring(0, max).TrimEnd() + "…";
    }
}

public sealed class MacroDefinition
{
    public string Name { get; set; } = "";
    public List<string> TriggerPhrases { get; set; } = new();
    public List<string> Steps { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal static class MacroLibrary
{
    private static readonly Lazy<List<MacroDefinition>> BuiltIns = new(() => new List<MacroDefinition>
    {
        new MacroDefinition
        {
            Name = "movie night",
            TriggerPhrases = new List<string> { "movie night", "movie time", "movie mode" },
            Steps = new List<string>
            {
                "switch personality to MediaCentreMaster",
                "open media centre",
                "set media filter trending",
                "enable ambient dim"
            },
            UpdatedUtc = DateTime.UtcNow
        },
        new MacroDefinition
        {
            Name = "dj mode",
            TriggerPhrases = new List<string> { "dj mode", "party mode", "music mode" },
            Steps = new List<string>
            {
                "switch personality to TotalDJ",
                "open dj booth",
                "suggest load track"
            },
            UpdatedUtc = DateTime.UtcNow
        }
    });

    public static MacroDefinition? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var q = name.Trim();

        // User macros first (allow override of built-ins).
        var user = MacroStore.Instance.TryGet(q);
        if (user != null) return user;

        return BuiltIns.Value.FirstOrDefault(m =>
            string.Equals(m.Name, q, StringComparison.OrdinalIgnoreCase) ||
            (m.TriggerPhrases?.Any(p => string.Equals((p ?? "").Trim(), q, StringComparison.OrdinalIgnoreCase)) ?? false));
    }

    public static MacroDefinition? FindByTrigger(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return null;
        var msg = NormalizeForTrigger(userMessage);

        // User macros first (allow overrides).
        foreach (var m in MacroStore.Instance.GetAll())
        {
            if (IsTriggerMatch(msg, m))
                return m;
        }

        foreach (var m in BuiltIns.Value)
        {
            if (IsTriggerMatch(msg, m))
                return m;
        }

        return null;
    }

    private static bool IsTriggerMatch(string normalizedMsg, MacroDefinition macro)
    {
        if (macro == null) return false;

        // If no explicit triggers, do not auto-fire.
        var triggers = macro.TriggerPhrases ?? new List<string>();
        if (triggers.Count == 0) return false;

        foreach (var t in triggers)
        {
            var phrase = NormalizeForTrigger(t);
            if (string.IsNullOrWhiteSpace(phrase)) continue;

            // Word-boundary match to reduce false positives.
            var pattern = $@"\b{Regex.Escape(phrase)}\b";
            if (Regex.IsMatch(normalizedMsg, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
        }

        return false;
    }

    private static string NormalizeForTrigger(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        var lower = s.Trim().ToLowerInvariant();
        lower = Regex.Replace(lower, @"\s+", " ");
        return lower;
    }
}

public sealed class MacroStore
{
    private static readonly Lazy<MacroStore> LazyInstance = new(() => new MacroStore());
    public static MacroStore Instance => LazyInstance.Value;

    private readonly object _lock = new();
    private Dictionary<string, MacroDefinition> _macros = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");

    private static readonly string MacrosPath = Path.Combine(AppDataDir, "macros.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private MacroStore() { }

    public void Upsert(string name, List<string> steps)
    {
        EnsureLoaded();

        var key = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key))
            return;

        lock (_lock)
        {
            _macros.TryGetValue(key, out var existing);
            _macros[key] = new MacroDefinition
            {
                Name = key,
                TriggerPhrases = existing?.TriggerPhrases?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList() ?? new List<string>(),
                Steps = steps.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList(),
                UpdatedUtc = DateTime.UtcNow
            };

            TrySaveInternal();
        }
    }

    public MacroDefinition? TryGet(string name)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(name)) return null;
        var q = name.Trim();

        lock (_lock)
        {
            if (_macros.TryGetValue(q, out var exact))
                return exact;

            // Fuzzy contains match.
            var hit = _macros.Values.FirstOrDefault(m =>
                m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || q.Contains(m.Name, StringComparison.OrdinalIgnoreCase));

            return hit;
        }
    }

    public List<MacroDefinition> GetAll()
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _macros.Values
                .Where(m => m != null)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _macros = TryLoadInternal() ?? new Dictionary<string, MacroDefinition>(StringComparer.OrdinalIgnoreCase);
            _loaded = true;
        }
    }

    private static Dictionary<string, MacroDefinition>? TryLoadInternal()
    {
        try
        {
            if (!File.Exists(MacrosPath)) return null;
            var json = File.ReadAllText(MacrosPath);
            var list = JsonSerializer.Deserialize<List<MacroDefinition>>(json, JsonOptions) ?? new List<MacroDefinition>();
            var dict = new Dictionary<string, MacroDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in list)
            {
                if (m == null) continue;
                var key = (m.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;
                m.Name = key;
                m.TriggerPhrases ??= new List<string>();
                m.TriggerPhrases = m.TriggerPhrases
                    .Select(t => (t ?? "").Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                m.Steps ??= new List<string>();
                dict[key] = m;
            }

            return dict;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MacroStore] Load failed: {ex.Message}");
            return null;
        }
    }

    private void TrySaveInternal()
    {
        try
        {
            if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);

            // Persist as a list for stable JSON.
            var list = _macros.Values
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(MacrosPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MacroStore] Save failed: {ex.Message}");
        }
    }
}

internal static class MacroRiskAnalyzer
{
    public static MacroRisk Analyze(List<string> steps)
    {
        var flagged = new List<string>();

        foreach (var raw in steps)
        {
            var s = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) continue;
            var lower = s.ToLowerInvariant();

            // Heuristics: conservative. Anything that looks destructive/elevated triggers confirmation.
            if (ContainsAny(lower, "rename ", "rename batch", "move ", "move files", "copy ", "copy files"))
                flagged.Add(s);
            else if (ContainsAny(lower, "delete", "remove", "uninstall", "wipe", "format", "registry", "cleanup", "clean temp", "clear temp"))
                flagged.Add(s);
            else if (ContainsAny(lower, "fix threats", "remove threats"))
                flagged.Add(s);
        }

        return new MacroRisk
        {
            RequiresConfirmation = flagged.Count > 0,
            FlaggedSteps = flagged
        };
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

internal sealed class MacroRisk
{
    public bool RequiresConfirmation { get; set; }
    public List<string> FlaggedSteps { get; set; } = new();
}
