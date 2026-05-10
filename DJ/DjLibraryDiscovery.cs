using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AtlasAI.DJ
{
    internal sealed class DjLibraryRoot
    {
        public string Path { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    internal static class DjLibraryDiscovery
    {
        private const string LibraryConfigFileName = "dj-library-roots.json";

        public static IReadOnlyList<DjLibraryRoot> DiscoverRoots()
        {
            var roots = new List<DjLibraryRoot>();

            AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music");
            AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonMusic), "Shared Music");
            AddIfExists(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VirtualDJ"), "VirtualDJ");
            AddIfExists(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music"), "Music");

            foreach (var configuredPath in ReadConfiguredPaths())
                AddIfExists(roots, configuredPath, InferSource(configuredPath));

            foreach (var envPath in ReadEnvironmentPaths())
                AddIfExists(roots, envPath, InferSource(envPath));

            return roots
                .Where(root => !string.IsNullOrWhiteSpace(root.Path))
                .GroupBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        internal static IReadOnlyList<string> ReadEnvironmentPaths()
        {
            var values = new List<string>();
            AddSplitPaths(values, Environment.GetEnvironmentVariable("ATLAS_DJ_LIBRARY_PATHS"));
            AddSplitPaths(values, Environment.GetEnvironmentVariable("ATLAS_DJ_LIBRARY"));
            AddSplitPaths(values, Environment.GetEnvironmentVariable("VIRTUALDJ_HOME"));
            return values;
        }

        internal static IReadOnlyList<string> ReadConfiguredPaths()
        {
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtlasAI",
                    LibraryConfigFileName);

                if (!File.Exists(configPath))
                    return Array.Empty<string>();

                var json = File.ReadAllText(configPath);
                var configuredPaths = JsonSerializer.Deserialize<List<string>>(json);
                if (configuredPaths != null)
                    return configuredPaths;

                return Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static void AddSplitPaths(ICollection<string> values, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            foreach (var part in raw.Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var path = part.Trim();
                if (!string.IsNullOrWhiteSpace(path))
                    values.Add(path);
            }
        }

        private static void AddIfExists(ICollection<DjLibraryRoot> roots, string? path, string source)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            roots.Add(new DjLibraryRoot
            {
                Path = path,
                Source = source
            });
        }

        private static string InferSource(string path)
        {
            if (path.IndexOf("VirtualDJ", StringComparison.OrdinalIgnoreCase) >= 0)
                return "VirtualDJ";
            if (path.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Music";
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }
}