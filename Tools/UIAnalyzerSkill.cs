using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AtlasAI.Tools;

public static class UIAnalyzerSkill
{
    public static async Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        var raw = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var lower = raw.ToLowerInvariant();
        if (!IsUiAnalyzeRequest(lower))
            return null;

        try
        {
            if (Application.Current?.Dispatcher == null)
                return "❌ UI Analyzer: no WPF dispatcher available.";

            // Keep analysis on the UI thread to avoid cross-thread ObservableCollection issues.
            return await Application.Current.Dispatcher.InvokeAsync(
                () => AnalyzeOnUiThread(raw, lower, ct),
                DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return "❌ UI Analyzer failed: " + ex.Message;
        }
    }

    private static bool IsUiAnalyzeRequest(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower)) return false;

        // Explicit commands.
        if (lower == "ui analyzer" || lower == "ui analyse" || lower == "ui analyze" || lower == "analyze ui" || lower == "analyse ui")
            return true;

        if (lower.StartsWith("ui analyze", StringComparison.Ordinal) || lower.StartsWith("ui analyse", StringComparison.Ordinal))
            return true;

        if (lower.Contains("viewmodel") && (lower.Contains("inspect") || lower.Contains("analyze") || lower.Contains("analyse")))
            return true;

        // Natural questions we want to catch.
        if (lower.Contains("genres") && (lower.Contains("empty") || lower.Contains("missing") || lower.Contains("blank") || lower.Contains("why")))
            return true;

        if (lower.Contains("duplicate") && (lower.Contains("items") || lower.Contains("entries") || lower.Contains("list") || lower.Contains("collection")))
            return true;

        if ((lower.Contains("empty") || lower.Contains("blank")) && (lower.Contains("collection") || lower.Contains("list") || lower.Contains("items")))
            return true;

        return false;
    }

    private static string AnalyzeOnUiThread(string raw, string lower, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var (root, source) = TryGetBestDataContext();
        if (root == null)
            return "❌ UI Analyzer: I couldn't find a live DataContext/ViewModel to inspect.";

        var focusGenres = lower.Contains("genre", StringComparison.OrdinalIgnoreCase);

        var scan = ScanObjectGraph(root, ct, new ScanOptions
        {
            MaxDepth = 5,
            MaxObjects = 220,
            MaxEnumerableSample = 260,
            FocusGenres = focusGenres
        });

        var sb = new StringBuilder();
        sb.AppendLine("🧠 UI Analyzer");
        sb.AppendLine($"Target: {root.GetType().FullName}");
        if (!string.IsNullOrWhiteSpace(source))
            sb.AppendLine($"Source: {source}");

        var keyState = ExtractKeyState(root);
        if (keyState.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("State");
            foreach (var line in keyState.Take(10))
                sb.AppendLine("- " + line);
        }

        sb.AppendLine();
        sb.AppendLine("Findings");

        if (scan.GenreProblems.Count > 0)
        {
            sb.AppendLine("- Genre lists:");
            foreach (var g in scan.GenreProblems.Take(8))
                sb.AppendLine("  - " + g);
        }
        else
        {
            sb.AppendLine("- Genre lists: no obvious issues found.");
        }

        sb.AppendLine($"- Empty collections: {scan.EmptyCollections.Count}");
        foreach (var e in scan.EmptyCollections.Take(8))
            sb.AppendLine("  - " + e);

        sb.AppendLine($"- Duplicate items: {scan.Duplicates.Count}");
        foreach (var d in scan.Duplicates.Take(8))
            sb.AppendLine("  - " + d);

        // If the user specifically asked “why are genres empty?”, give a direct answer line.
        if (focusGenres)
        {
            sb.AppendLine();
            sb.AppendLine("Answer");
            if (scan.GenreProblems.Count == 0)
            {
                sb.AppendLine("I don’t currently see an empty `Genres` list in the inspected DataContext. If you’re looking at a different panel, click it and ask again so I can pick up its DataContext.");
            }
            else
            {
                sb.AppendLine("Genres are empty in the current ViewModel state (see ‘Genre lists’ above). Most commonly this means metadata hasn’t been loaded yet, or the upstream metadata response didn’t include genres.");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static (object? root, string source) TryGetBestDataContext()
    {
        try
        {
            var app = Application.Current;
            if (app == null) return (null, "");

            var activeWindow = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
            if (activeWindow == null) return (null, "");

            // Prefer focused element DC.
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused != null)
            {
                var dc = TryFindNearestDataContext(focused);
                if (dc != null)
                    return (dc, "Keyboard.FocusedElement DataContext");
            }

            // Fall back: search inside window content for the “best” DC.
            var best = FindBestDataContextInTree(activeWindow);
            if (best != null)
                return (best, "ActiveWindow visual tree");

            // Finally: window DC.
            if (activeWindow.DataContext != null)
                return (activeWindow.DataContext, "ActiveWindow.DataContext");

            return (null, "");
        }
        catch
        {
            return (null, "");
        }
    }

    private static object? TryFindNearestDataContext(DependencyObject start)
    {
        DependencyObject? cur = start;
        for (var i = 0; i < 40 && cur != null; i++)
        {
            if (cur is FrameworkElement fe && fe.DataContext != null)
                return fe.DataContext;

            if (cur is FrameworkContentElement fce && fce.DataContext != null)
                return fce.DataContext;

            var parent = VisualTreeHelper.GetParent(cur);
            if (parent == null)
            {
                // Logical tree fallback.
                if (cur is FrameworkElement fe2)
                    parent = fe2.Parent as DependencyObject;
                else if (cur is FrameworkContentElement fce2)
                    parent = fce2.Parent as DependencyObject;
            }

            cur = parent;
        }

        return null;
    }

    private static object? FindBestDataContextInTree(Window window)
    {
        try
        {
            var root = window.Content as DependencyObject;
            if (root == null) return null;

            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            object? best = null;
            var bestScore = int.MinValue;

            var q = new Queue<DependencyObject>();
            q.Enqueue(root);

            var steps = 0;
            while (q.Count > 0 && steps < 2500)
            {
                steps++;
                var node = q.Dequeue();

                if (node is FrameworkElement fe && fe.DataContext != null)
                {
                    var dc = fe.DataContext;
                    if (dc != null && seen.Add(dc))
                    {
                        var score = ScoreDataContext(dc);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = dc;
                        }
                    }
                }

                var childCount = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child != null) q.Enqueue(child);
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreDataContext(object dc)
    {
        try
        {
            var t = dc.GetType();
            var name = (t.FullName ?? t.Name) ?? "";
            var score = 0;

            if (name.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase)) score += 50;
            if (name.Contains("Views.ViewModels", StringComparison.OrdinalIgnoreCase)) score += 20;
            if (name.Contains("AtlasAI", StringComparison.OrdinalIgnoreCase)) score += 10;
            if (name.Contains("MediaCentre", StringComparison.OrdinalIgnoreCase)) score += 10;

            // Prefer non-window DCs.
            if (dc is Window) score -= 100;

            return score;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class ScanOptions
    {
        public int MaxDepth { get; init; } = 5;
        public int MaxObjects { get; init; } = 200;
        public int MaxEnumerableSample { get; init; } = 250;
        public bool FocusGenres { get; init; } = false;
    }

    private sealed class ScanResult
    {
        public List<string> EmptyCollections { get; } = new();
        public List<string> Duplicates { get; } = new();
        public List<string> GenreProblems { get; } = new();
    }

    private static ScanResult ScanObjectGraph(object root, CancellationToken ct, ScanOptions options)
    {
        var result = new ScanResult();

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var q = new Queue<(object Obj, string Path, int Depth)>();
        q.Enqueue((root, "$", 0));

        var objectsScanned = 0;

        while (q.Count > 0 && objectsScanned < options.MaxObjects)
        {
            ct.ThrowIfCancellationRequested();

            var (obj, path, depth) = q.Dequeue();
            if (obj == null) continue;
            if (!visited.Add(obj)) continue;

            objectsScanned++;

            var t = obj.GetType();
            var props = GetReadableProps(t);

            foreach (var p in props)
            {
                ct.ThrowIfCancellationRequested();

                object? value = null;
                try { value = p.GetValue(obj); } catch { continue; }
                if (value == null) continue;

                if (IsSimple(value.GetType()))
                    continue;

                var propPath = path + "." + p.Name;

                if (value is IEnumerable en && value is not string)
                {
                    AnalyzeEnumerable(result, propPath, p.Name, en, options);
                    continue;
                }

                if (depth >= options.MaxDepth)
                    continue;

                // Only descend into app-owned object graphs to avoid exploding into WPF internals.
                var ns = value.GetType().Namespace ?? "";
                if (!ns.StartsWith("AtlasAI", StringComparison.OrdinalIgnoreCase))
                    continue;

                q.Enqueue((value, propPath, depth + 1));
            }
        }

        return result;
    }

    private static void AnalyzeEnumerable(ScanResult result, string path, string propName, IEnumerable en, ScanOptions options)
    {
        // Sample to avoid expensive enumerations.
        var items = new List<object?>();
        var countKnown = TryGetCollectionCount(en);
        var taken = 0;

        try
        {
            foreach (var item in en)
            {
                items.Add(item);
                taken++;
                if (taken >= options.MaxEnumerableSample) break;
            }
        }
        catch
        {
            // Enumeration failed (collection mutated, etc.)
            return;
        }

        var count = countKnown ?? items.Count;
        if (count == 0)
        {
            result.EmptyCollections.Add($"{path} (empty)");

            if (LooksLikeGenreProperty(propName))
                result.GenreProblems.Add($"{path} is empty.");

            return;
        }

        if (LooksLikeGenreProperty(propName))
        {
            var nonBlank = 0;
            foreach (var item in items)
            {
                if (item is string s)
                {
                    if (!string.IsNullOrWhiteSpace(s)) nonBlank++;
                }
                else if (item != null)
                {
                    nonBlank++;
                }
            }

            if (nonBlank == 0)
                result.GenreProblems.Add($"{path} contains only blank values.");
        }

        // Duplicates.
        if (count > 1)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var key = BuildDuplicateKey(item);
                if (string.IsNullOrWhiteSpace(key)) continue;
                counts.TryGetValue(key, out var c);
                counts[key] = c + 1;
            }

            foreach (var kvp in counts.OrderByDescending(k => k.Value).Take(6))
            {
                if (kvp.Value <= 1) continue;
                result.Duplicates.Add($"{path} has duplicate '{kvp.Key}' ×{kvp.Value}");
            }
        }
    }

    private static bool LooksLikeGenreProperty(string name)
    {
        var n = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(n)) return false;
        return n.Equals("Genres", StringComparison.OrdinalIgnoreCase) ||
               n.Contains("Genre", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryGetCollectionCount(IEnumerable en)
    {
        try
        {
            if (en is ICollection c) return c.Count;

            // Try read-only collection Count via reflection.
            var t = en.GetType();
            var p = t.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0)
            {
                var v = p.GetValue(en);
                if (v is int i) return i;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string BuildDuplicateKey(object? item)
    {
        if (item == null) return "";
        if (item is string s) return s.Trim();

        try
        {
            var t = item.GetType();

            // Prefer Id-like keys.
            foreach (var keyName in new[] { "Id", "Key", "ImdbId", "TmdbId", "Slug" })
            {
                var p = t.GetProperty(keyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p == null || p.GetIndexParameters().Length != 0) continue;
                var v = p.GetValue(item);
                if (v == null) continue;
                var str = v.ToString();
                if (!string.IsNullOrWhiteSpace(str)) return str.Trim();
            }

            // Then Name/Title.
            foreach (var keyName in new[] { "Name", "Title", "DisplayName" })
            {
                var p = t.GetProperty(keyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p == null || p.GetIndexParameters().Length != 0) continue;
                var v = p.GetValue(item) as string;
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }

            return (item.ToString() ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private static bool IsSimple(Type t)
    {
        if (t.IsPrimitive) return true;
        if (t.IsEnum) return true;
        if (t == typeof(string)) return true;
        if (t == typeof(decimal)) return true;
        if (t == typeof(DateTime)) return true;
        if (t == typeof(DateTimeOffset)) return true;
        if (t == typeof(TimeSpan)) return true;
        if (t == typeof(Guid)) return true;
        return false;
    }

    private static List<PropertyInfo> GetReadableProps(Type t)
    {
        try
        {
            return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToList();
        }
        catch
        {
            return new List<PropertyInfo>();
        }
    }

    private static List<string> ExtractKeyState(object root)
    {
        var lines = new List<string>();

        try
        {
            var t = root.GetType();
            var props = GetReadableProps(t);

            foreach (var p in props)
            {
                object? v = null;
                try { v = p.GetValue(root); } catch { continue; }
                if (v == null) continue;

                var name = p.Name;

                if (v is bool b)
                {
                    if (name.StartsWith("Is", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Has", StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add($"{name} = {b}");
                    }

                    continue;
                }

                if (v is string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;

                    if (name.Contains("Status", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Message", StringComparison.OrdinalIgnoreCase))
                    {
                        var clipped = s.Length > 140 ? s.Substring(0, 140) + "…" : s;
                        lines.Add($"{name} = {clipped}");
                    }

                    continue;
                }

                if (v is int i && (name.EndsWith("Count", StringComparison.OrdinalIgnoreCase) || name.Contains("Count", StringComparison.OrdinalIgnoreCase)))
                {
                    lines.Add($"{name} = {i}");
                }
            }
        }
        catch
        {
        }

        return lines;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
