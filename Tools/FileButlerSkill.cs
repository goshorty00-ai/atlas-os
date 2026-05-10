using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Tools
{
    public static class FileButlerSkill
    {
        private sealed class PendingOperation
        {
            public string OperationName { get; init; } = "";
            public DateTime CreatedUtc { get; init; }
            public string PreviewText { get; init; } = "";
            public Func<CancellationToken, Task<string>> ExecuteAsync { get; init; } = _ => Task.FromResult("❌ Pending operation missing executor.");
        }

        private static readonly object _gate = new();
        private static PendingOperation? _pending;

        public static Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return Task.FromResult<string?>(null);

            var clean = userMessage.Trim();
            var lower = clean.ToLowerInvariant();

            // Handle confirm/cancel/preview for elevated operations.
            var pending = GetPending();
            if (pending != null)
            {
                if (IsConfirm(lower))
                    return ConfirmPendingAsync(pending, ct);

                if (IsCancel(lower))
                {
                    ClearPending();
                    return Task.FromResult<string?>("✅ Cancelled pending file operation.");
                }

                if (IsShowPreview(lower))
                    return Task.FromResult<string?>(pending.PreviewText);
            }

            // SAFE: Find files
            var find = TryParseFindFiles(clean);
            if (find != null)
                return ExecuteFindFilesAsync(find.Value.pattern, find.Value.pathOrAlias, ct);

            // SAFE: Zip folder
            var zip = TryParseZipFolder(clean);
            if (zip != null)
                return ExecuteZipFolderAsync(zip.Value.folderPathOrAlias, zip.Value.zipPathMaybe, ct);

            // ELEVATED: Rename batch (with preview + confirm)
            var renameBatch = TryParseRenameBatch(clean);
            if (renameBatch != null)
                return Task.FromResult<string?>(BeginRenameBatch(renameBatch.Value.folderPathOrAlias, renameBatch.Value.oldText, renameBatch.Value.newText));

            // ELEVATED: Move files (with preview + confirm)
            var move = TryParseMoveFiles(clean);
            if (move != null)
                return Task.FromResult<string?>(BeginMoveFiles(move.Value.sourcePathOrAlias, move.Value.destPathOrAlias, move.Value.patternMaybe));

            // ELEVATED: Copy files (with preview + confirm)
            var copy = TryParseCopyFiles(clean);
            if (copy != null)
                return Task.FromResult<string?>(BeginCopyFiles(copy.Value.sourcePathOrAlias, copy.Value.destPathOrAlias, copy.Value.patternMaybe));

            // ELEVATED (single): Rename file/folder (with preview + confirm)
            // This intentionally supersedes the legacy direct rename implementation so destructive ops always preview first.
            var renameOne = TryParseRenameSingle(clean);
            if (renameOne != null)
                return Task.FromResult<string?>(BeginRenameSingle(renameOne.Value.pathOrAlias, renameOne.Value.newName));

            return Task.FromResult<string?>(null);
        }

        private static async Task<string?> ConfirmPendingAsync(PendingOperation pending, CancellationToken ct)
        {
            ClearPending();
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await pending.ExecuteAsync(ct);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"❌ {pending.OperationName} failed: {ex.Message}";
            }
        }

        private static PendingOperation? GetPending()
        {
            lock (_gate)
            {
                if (_pending == null)
                    return null;

                // Expire after 5 minutes to avoid surprising late confirmations.
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
            lock (_gate)
            {
                _pending = null;
            }
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

        private static (string pattern, string pathOrAlias)? TryParseFindFiles(string message)
        {
            // Examples:
            // - "find files report in downloads"
            // - "find files named \"report\" in \"C:\\Work\""
            var match = Regex.Match(message,
                @"^\s*(?:find|search|locate)\s+file[s]?\s+(?:named\s+)?(?<pattern>.+?)(?:\s+in\s+(?<path>.+))?\s*$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            var pattern = TrimOuterQuotes(match.Groups["pattern"].Value.Trim());
            var path = match.Groups["path"].Success ? TrimOuterQuotes(match.Groups["path"].Value.Trim()) : "";

            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            return (pattern, path);
        }

        private static async Task<string?> ExecuteFindFilesAsync(string pattern, string pathOrAlias, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var basePath = ResolveExistingDirectory(pathOrAlias);
                if (string.IsNullOrEmpty(basePath))
                {
                    // Fall back to SystemTool behavior (user profile) if nothing specified.
                    basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                // Reuse the project’s safe recursive search implementation.
                return await SystemTool.FindFilesAsync(basePath, pattern);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"❌ Find files failed: {ex.Message}";
            }
        }

        private static (string folderPathOrAlias, string zipPathMaybe)? TryParseZipFolder(string message)
        {
            // Examples:
            // - "zip folder C:\\Work\\Project"
            // - "zip C:\\Work\\Project to C:\\Work\\Project.zip"
            var match = Regex.Match(message,
                @"^\s*(?:zip|zip\s+folder|compress\s+folder|compress)\s+(?<folder>.+?)(?:\s+(?:to|into)\s+(?<zip>.+))?\s*$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            var folder = TrimOuterQuotes(match.Groups["folder"].Value.Trim());
            var zip = match.Groups["zip"].Success ? TrimOuterQuotes(match.Groups["zip"].Value.Trim()) : "";

            if (string.IsNullOrWhiteSpace(folder))
                return null;

            return (folder, zip);
        }

        private static async Task<string?> ExecuteZipFolderAsync(string folderPathOrAlias, string zipPathMaybe, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var folderPath = ResolveExistingDirectory(folderPathOrAlias);
                if (string.IsNullOrEmpty(folderPath))
                    return $"❌ Folder not found: {folderPathOrAlias}";

                var zipPath = ResolveZipOutputPath(folderPath, zipPathMaybe);
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath) ?? Directory.GetCurrentDirectory());

                // Avoid overwriting existing archives.
                zipPath = EnsureUniquePath(zipPath);

                await Task.Run(() =>
                {
                    ZipFile.CreateFromDirectory(folderPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                }, ct);

                AppLogger.Log($"[Skill] Executed: ZipFolder ({zipPath})");
                return $"✅ Zipped folder to: {zipPath}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"❌ Zip folder failed: {ex.Message}";
            }
        }

        private static (string folderPathOrAlias, string oldText, string newText)? TryParseRenameBatch(string message)
        {
            // Examples:
            // - rename batch in C:\\Work replace "old" with "new"
            // - batch rename in downloads from v1 to v2
            // - rename batch C:\\Work from old to new
            var match = Regex.Match(message,
                @"^\s*(?:rename\s+batch|batch\s+rename)\s+(?:in\s+)?(?<folder>.+?)\s+(?:replace|from)\s+(?<old>""[^""]*""|'[^']*'|\S+?)\s+(?:with|to)\s+(?<new>""[^""]*""|'[^']*'|.+)\s*$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            var folder = TrimOuterQuotes(match.Groups["folder"].Value.Trim());
            var oldText = TrimOuterQuotes(match.Groups["old"].Value.Trim());
            var newText = TrimOuterQuotes(match.Groups["new"].Value.Trim());

            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrEmpty(oldText))
                return null;

            return (folder, oldText, newText);
        }

        private static string BeginRenameBatch(string folderPathOrAlias, string oldText, string newText)
        {
            var folderPath = ResolveExistingDirectory(folderPathOrAlias);
            if (string.IsNullOrEmpty(folderPath))
                return $"❌ Folder not found: {folderPathOrAlias}";

            var files = SafeGetFiles(folderPath, "*");
            if (files.Count == 0)
                return $"ℹ️ No files found in: {folderPath}";

            var planned = new List<(string from, string to)>();
            var conflicts = new List<string>();
            var invalid = new List<string>();

            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                if (!fileName.Contains(oldText, StringComparison.Ordinal))
                    continue;

                var newName = fileName.Replace(oldText, newText, StringComparison.Ordinal);
                if (string.Equals(newName, fileName, StringComparison.Ordinal))
                    continue;

                if (!IsValidFileName(newName))
                {
                    invalid.Add($"{fileName} -> {newName}");
                    continue;
                }

                var destPath = Path.Combine(folderPath, newName);
                if (File.Exists(destPath) && !string.Equals(destPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add($"{fileName} -> {newName} (target exists)");
                    continue;
                }

                planned.Add((filePath, destPath));
            }

            if (planned.Count == 0)
                return $"ℹ️ No filenames in '{folderPath}' contained '{oldText}'.";

            var preview = BuildRenamePreview("RenameBatch", planned, conflicts, invalid);

            if (conflicts.Count > 0)
                return preview + "\n\n⚠️ Not armed due to conflicts. Resolve them and re-run the command.";

            lock (_gate)
            {
                _pending = new PendingOperation
                {
                    OperationName = "Rename batch",
                    CreatedUtc = DateTime.UtcNow,
                    PreviewText = preview,
                    ExecuteAsync = async ct => await ExecuteRenamePlanAsync(planned, ct)
                };
            }

            return preview;
        }

        private static (string pathOrAlias, string newName)? TryParseRenameSingle(string message)
        {
            // Example: "rename C:\\Work\\a.txt to b.txt"
            // Keep this fairly strict to avoid catching conversational "rename".
            var match = Regex.Match(message,
                @"^\s*rename\s+(?<path>.+?)\s+to\s+(?<new>.+)\s*$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            var path = TrimOuterQuotes(match.Groups["path"].Value.Trim());
            var newName = TrimOuterQuotes(match.Groups["new"].Value.Trim());

            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(newName))
                return null;

            return (path, newName);
        }

        private static string BeginRenameSingle(string pathOrAlias, string newName)
        {
            var resolved = ResolveExistingFileOrDirectory(pathOrAlias);
            if (string.IsNullOrEmpty(resolved))
                return $"❌ Not found: {pathOrAlias}";

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return $"❌ Invalid name: {newName}";

            var parent = Path.GetDirectoryName(resolved) ?? Directory.GetCurrentDirectory();
            var dest = Path.Combine(parent, newName);

            var planned = new List<(string from, string to)> { (resolved, dest) };

            var conflicts = new List<string>();
            if ((File.Exists(dest) || Directory.Exists(dest)) && !string.Equals(dest, resolved, StringComparison.OrdinalIgnoreCase))
                conflicts.Add($"{resolved} -> {dest} (target exists)");

            var preview = BuildRenamePreview("Rename", planned, conflicts, invalid: new List<string>());

            if (conflicts.Count > 0)
                return preview + "\n\n⚠️ Not armed due to conflicts. Choose a different name and try again.";

            lock (_gate)
            {
                _pending = new PendingOperation
                {
                    OperationName = "Rename",
                    CreatedUtc = DateTime.UtcNow,
                    PreviewText = preview,
                    ExecuteAsync = async ct => await ExecuteRenamePlanAsync(planned, ct)
                };
            }

            return preview;
        }

        private static async Task<string> ExecuteRenamePlanAsync(List<(string from, string to)> planned, CancellationToken ct)
        {
            if (planned.Count == 0)
                return "ℹ️ Nothing to rename.";

            // Block execution when there are collisions.
            var collisions = planned.Where(p => (File.Exists(p.to) || Directory.Exists(p.to)) && !string.Equals(p.to, p.from, StringComparison.OrdinalIgnoreCase)).ToList();
            if (collisions.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("❌ Refusing to rename because one or more targets already exist:");
                foreach (var c in collisions.Take(20))
                    sb.AppendLine($"- {c.from} -> {c.to}");
                if (collisions.Count > 20)
                    sb.AppendLine($"... and {collisions.Count - 20} more");
                return sb.ToString().TrimEnd();
            }

            var renamed = 0;
            var failed = new List<string>();

            foreach (var (from, to) in planned)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(from))
                    {
                        File.Move(from, to);
                        renamed++;
                    }
                    else if (Directory.Exists(from))
                    {
                        Directory.Move(from, to);
                        renamed++;
                    }
                    else
                    {
                        failed.Add($"Not found: {from}");
                    }
                }
                catch (Exception ex)
                {
                    failed.Add($"{Path.GetFileName(from)}: {ex.Message}");
                }
            }

            var result = new StringBuilder();
            result.AppendLine($"✅ Renamed {renamed} item(s).");
            if (failed.Count > 0)
            {
                result.AppendLine();
                result.AppendLine("Failures:");
                foreach (var f in failed.Take(20))
                    result.AppendLine($"- {f}");
                if (failed.Count > 20)
                    result.AppendLine($"... and {failed.Count - 20} more");
            }

            return result.ToString().TrimEnd();
        }

        private static (string sourcePathOrAlias, string destPathOrAlias, string patternMaybe)? TryParseMoveFiles(string message)
        {
            // Examples:
            // - move files from C:\\A to C:\\B
            // - move C:\\A\\file.txt to C:\\B
            // - move C:\\A to C:\\B matching *.png
            if (!Regex.IsMatch(message, @"\bmove\b", RegexOptions.IgnoreCase))
                return null;

            var match = Regex.Match(message,
                @"^\s*move\s+(?:file[s]?|folder|directory)?\s*(?:from\s+)?(?<src>.+?)\s+to\s+(?<dst>.+?)\s*(?:matching\s+(?<pattern>\S+))?\s*$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            var src = TrimOuterQuotes(match.Groups["src"].Value.Trim());
            var dst = TrimOuterQuotes(match.Groups["dst"].Value.Trim());
            var pattern = match.Groups["pattern"].Success ? match.Groups["pattern"].Value.Trim() : "";

            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
                return null;

            return (src, dst, pattern);
        }

        private static (string sourcePathOrAlias, string destPathOrAlias, string patternMaybe)? TryParseCopyFiles(string message)
        {
            // Examples:
            // - copy files from C:\\A to C:\\B
            // - copy C:\\A\\file.txt to C:\\B
            // - copy C:\\A to C:\\B matching *.png
            if (!Regex.IsMatch(message, @"\bcopy\b", RegexOptions.IgnoreCase))
                return null;

            var match = Regex.Match(message,
                @"^\s*copy\s+(?:file[s]?|folder|directory)?\s*(?:from\s+)?(?<src>.+?)\s+to\s+(?<dst>.+?)\s*(?:matching\s+(?<pattern>\S+))?\s*$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            var src = TrimOuterQuotes(match.Groups["src"].Value.Trim());
            var dst = TrimOuterQuotes(match.Groups["dst"].Value.Trim());
            var pattern = match.Groups["pattern"].Success ? match.Groups["pattern"].Value.Trim() : "";

            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
                return null;

            return (src, dst, pattern);
        }

        private static string BeginMoveFiles(string sourcePathOrAlias, string destPathOrAlias, string patternMaybe)
        {
            var srcFile = ResolveExistingFile(sourcePathOrAlias);
            var srcDir = string.IsNullOrEmpty(srcFile) ? ResolveExistingDirectory(sourcePathOrAlias) : "";

            if (string.IsNullOrEmpty(srcFile) && string.IsNullOrEmpty(srcDir))
                return $"❌ Source not found: {sourcePathOrAlias}";

            var destResolved = ResolveNonExistingPath(destPathOrAlias);
            if (string.IsNullOrEmpty(destResolved))
                return $"❌ Invalid destination: {destPathOrAlias}";

            var planned = new List<(string from, string to)>();
            var conflicts = new List<string>();

            if (!string.IsNullOrEmpty(srcFile))
            {
                // Single file move.
                var targetPath = DetermineSingleFileMoveTarget(srcFile, destResolved);

                if (File.Exists(targetPath) && !string.Equals(targetPath, srcFile, StringComparison.OrdinalIgnoreCase))
                    conflicts.Add($"{srcFile} -> {targetPath} (target exists)");
                else
                    planned.Add((srcFile, targetPath));
            }
            else
            {
                // Directory: move matching files (non-recursive).
                var pattern = string.IsNullOrWhiteSpace(patternMaybe) ? "*" : patternMaybe;
                var files = SafeGetFiles(srcDir, pattern);

                if (files.Count == 0)
                    return $"ℹ️ No files found to move in: {srcDir}";

                // Destination treated as directory.
                var destDir = destResolved;
                foreach (var file in files)
                {
                    var target = Path.Combine(destDir, Path.GetFileName(file));
                    if (File.Exists(target) && !string.Equals(target, file, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts.Add($"{file} -> {target} (target exists)");
                        continue;
                    }
                    planned.Add((file, target));
                }
            }

            if (planned.Count == 0 && conflicts.Count == 0)
                return "ℹ️ Nothing to move.";

            var preview = BuildMovePreview(planned, conflicts);

            if (conflicts.Count > 0)
                return preview + "\n\n⚠️ Not armed due to conflicts. Resolve them (or choose a different destination) and re-run the command.";

            if (planned.Count == 0)
                return preview + "\n\nℹ️ No moves planned.";

            lock (_gate)
            {
                _pending = new PendingOperation
                {
                    OperationName = "Move files",
                    CreatedUtc = DateTime.UtcNow,
                    PreviewText = preview,
                    ExecuteAsync = async ct => await ExecuteMovePlanAsync(planned, ct)
                };
            }

            return preview;
        }

        private static string DetermineSingleFileMoveTarget(string srcFile, string destResolved)
        {
            // If destination exists as directory, move into it.
            if (Directory.Exists(destResolved))
                return Path.Combine(destResolved, Path.GetFileName(srcFile));

            // If destination looks like a directory path (ends with slash/backslash), move into it.
            if (destResolved.EndsWith(Path.DirectorySeparatorChar) || destResolved.EndsWith(Path.AltDirectorySeparatorChar))
                return Path.Combine(destResolved, Path.GetFileName(srcFile));

            // If destination has an extension, treat as full file path; otherwise treat as directory.
            if (Path.HasExtension(destResolved))
                return destResolved;

            return Path.Combine(destResolved, Path.GetFileName(srcFile));
        }

        private static async Task<string> ExecuteMovePlanAsync(List<(string from, string to)> planned, CancellationToken ct)
        {
            if (planned.Count == 0)
                return "ℹ️ Nothing to move.";

            // Refuse to overwrite.
            var collisions = planned.Where(p => File.Exists(p.to) && !string.Equals(p.to, p.from, StringComparison.OrdinalIgnoreCase)).ToList();
            if (collisions.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("❌ Refusing to move because one or more targets already exist:");
                foreach (var c in collisions.Take(20))
                    sb.AppendLine($"- {c.from} -> {c.to}");
                if (collisions.Count > 20)
                    sb.AppendLine($"... and {collisions.Count - 20} more");
                return sb.ToString().TrimEnd();
            }

            var moved = 0;
            var failed = new List<string>();

            foreach (var (from, to) in planned)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var toDir = Path.GetDirectoryName(to);
                    if (!string.IsNullOrEmpty(toDir))
                        Directory.CreateDirectory(toDir);

                    if (!File.Exists(from))
                    {
                        failed.Add($"Not found: {from}");
                        continue;
                    }

                    File.Move(from, to);
                    moved++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{Path.GetFileName(from)}: {ex.Message}");
                }
            }

            var result = new StringBuilder();
            result.AppendLine($"✅ Moved {moved} file(s).");
            if (failed.Count > 0)
            {
                result.AppendLine();
                result.AppendLine("Failures:");
                foreach (var f in failed.Take(20))
                    result.AppendLine($"- {f}");
                if (failed.Count > 20)
                    result.AppendLine($"... and {failed.Count - 20} more");
            }

            return result.ToString().TrimEnd();
        }

        private static string BeginCopyFiles(string sourcePathOrAlias, string destPathOrAlias, string patternMaybe)
        {
            var srcFile = ResolveExistingFile(sourcePathOrAlias);
            var srcDir = string.IsNullOrEmpty(srcFile) ? ResolveExistingDirectory(sourcePathOrAlias) : "";

            if (string.IsNullOrEmpty(srcFile) && string.IsNullOrEmpty(srcDir))
                return $"❌ Source not found: {sourcePathOrAlias}";

            var destResolved = ResolveNonExistingPath(destPathOrAlias);
            if (string.IsNullOrEmpty(destResolved))
                return $"❌ Invalid destination: {destPathOrAlias}";

            var planned = new List<(string from, string to)>();
            var conflicts = new List<string>();

            if (!string.IsNullOrEmpty(srcFile))
            {
                var targetPath = DetermineSingleFileMoveTarget(srcFile, destResolved);
                if (File.Exists(targetPath) && !string.Equals(targetPath, srcFile, StringComparison.OrdinalIgnoreCase))
                    conflicts.Add($"{srcFile} -> {targetPath} (target exists)");
                else
                    planned.Add((srcFile, targetPath));
            }
            else
            {
                var pattern = string.IsNullOrWhiteSpace(patternMaybe) ? "*" : patternMaybe;
                var files = SafeGetFiles(srcDir, pattern);
                if (files.Count == 0)
                    return $"ℹ️ No files found to copy in: {srcDir}";

                var destDir = destResolved;
                foreach (var file in files)
                {
                    var target = Path.Combine(destDir, Path.GetFileName(file));
                    if (File.Exists(target) && !string.Equals(target, file, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts.Add($"{file} -> {target} (target exists)");
                        continue;
                    }
                    planned.Add((file, target));
                }
            }

            if (planned.Count == 0 && conflicts.Count == 0)
                return "ℹ️ Nothing to copy.";

            var preview = BuildCopyPreview(planned, conflicts);

            lock (_gate)
            {
                _pending = new PendingOperation
                {
                    OperationName = "Copy files",
                    CreatedUtc = DateTime.UtcNow,
                    PreviewText = preview,
                    ExecuteAsync = async ct => await ExecuteCopyPlanAsync(planned, ct)
                };
            }

            return preview;
        }

        private static async Task<string> ExecuteCopyPlanAsync(List<(string from, string to)> planned, CancellationToken ct)
        {
            if (planned.Count == 0)
                return "ℹ️ Nothing to copy.";

            // Refuse to overwrite.
            var collisions = planned.Where(p => File.Exists(p.to) && !string.Equals(p.to, p.from, StringComparison.OrdinalIgnoreCase)).ToList();
            if (collisions.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("❌ Refusing to copy because one or more targets already exist:");
                foreach (var c in collisions.Take(20))
                    sb.AppendLine($"- {c.from} -> {c.to}");
                if (collisions.Count > 20)
                    sb.AppendLine($"... and {collisions.Count - 20} more");
                return sb.ToString().TrimEnd();
            }

            var copied = 0;
            var failed = new List<string>();

            foreach (var (from, to) in planned)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var toDir = Path.GetDirectoryName(to);
                    if (!string.IsNullOrEmpty(toDir))
                        Directory.CreateDirectory(toDir);

                    if (!File.Exists(from))
                    {
                        failed.Add($"Not found: {from}");
                        continue;
                    }

                    File.Copy(from, to);
                    copied++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{Path.GetFileName(from)}: {ex.Message}");
                }
            }

            var result = new StringBuilder();
            result.AppendLine($"✅ Copied {copied} file(s).");
            if (failed.Count > 0)
            {
                result.AppendLine();
                result.AppendLine("Failures:");
                foreach (var f in failed.Take(20))
                    result.AppendLine($"- {f}");
                if (failed.Count > 20)
                    result.AppendLine($"... and {failed.Count - 20} more");
            }

            return result.ToString().TrimEnd();
        }

        private static string BuildCopyPreview(List<(string from, string to)> planned, List<string> conflicts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("🧾 Preview: Copy files");
            sb.AppendLine($"Planned: {planned.Count} file(s)");
            sb.AppendLine();

            foreach (var (from, to) in planned.Take(20))
                sb.AppendLine($"- {Path.GetFileName(from)} → {to}");

            if (planned.Count > 20)
                sb.AppendLine($"... and {planned.Count - 20} more");

            if (conflicts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Conflicts (will NOT execute until resolved):");
                foreach (var c in conflicts.Take(10))
                    sb.AppendLine($"- {c}");
                if (conflicts.Count > 10)
                    sb.AppendLine($"... and {conflicts.Count - 10} more");
            }

            sb.AppendLine();
            sb.AppendLine("Reply 'confirm' to execute, or 'cancel' to abort.");
            return sb.ToString().TrimEnd();
        }

        private static string BuildRenamePreview(string title, List<(string from, string to)> planned, List<string> conflicts, List<string> invalid)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🧾 Preview: {title}");
            sb.AppendLine($"Planned: {planned.Count} item(s)");
            sb.AppendLine();

            foreach (var (from, to) in planned.Take(20))
                sb.AppendLine($"- {Path.GetFileName(from)} → {Path.GetFileName(to)}");

            if (planned.Count > 20)
                sb.AppendLine($"... and {planned.Count - 20} more");

            if (conflicts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Conflicts (will NOT execute until resolved):");
                foreach (var c in conflicts.Take(10))
                    sb.AppendLine($"- {c}");
                if (conflicts.Count > 10)
                    sb.AppendLine($"... and {conflicts.Count - 10} more");
            }

            if (invalid.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Invalid target names (skipped):");
                foreach (var i in invalid.Take(10))
                    sb.AppendLine($"- {i}");
                if (invalid.Count > 10)
                    sb.AppendLine($"... and {invalid.Count - 10} more");
            }

            sb.AppendLine();
            sb.AppendLine("Reply 'confirm' to execute, or 'cancel' to abort.");
            return sb.ToString().TrimEnd();
        }

        private static string BuildMovePreview(List<(string from, string to)> planned, List<string> conflicts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("🧾 Preview: Move files");
            sb.AppendLine($"Planned: {planned.Count} file(s)");
            sb.AppendLine();

            foreach (var (from, to) in planned.Take(20))
                sb.AppendLine($"- {Path.GetFileName(from)} → {to}");

            if (planned.Count > 20)
                sb.AppendLine($"... and {planned.Count - 20} more");

            if (conflicts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Conflicts (will NOT execute until resolved):");
                foreach (var c in conflicts.Take(10))
                    sb.AppendLine($"- {c}");
                if (conflicts.Count > 10)
                    sb.AppendLine($"... and {conflicts.Count - 10} more");
            }

            sb.AppendLine();
            sb.AppendLine("Reply 'confirm' to execute, or 'cancel' to abort.");
            return sb.ToString().TrimEnd();
        }

        private static string ResolveZipOutputPath(string folderPath, string zipPathMaybe)
        {
            if (!string.IsNullOrWhiteSpace(zipPathMaybe))
            {
                var expanded = ExpandPath(zipPathMaybe);
                if (!Path.IsPathRooted(expanded))
                    expanded = Path.GetFullPath(expanded);

                if (!expanded.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    expanded += ".zip";

                return expanded;
            }

            var folderName = new DirectoryInfo(folderPath).Name;
            var parent = Directory.GetParent(folderPath)?.FullName ?? Directory.GetCurrentDirectory();
            return Path.Combine(parent, folderName + ".zip");
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            for (var i = 1; i <= 9999; i++)
            {
                var candidate = Path.Combine(dir, $"{name}-{i}{ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            // Fallback to timestamp.
            return Path.Combine(dir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
        }

        private static string ResolveExistingDirectory(string pathOrAlias)
        {
            var expanded = ExpandAndNormalize(pathOrAlias);
            if (string.IsNullOrWhiteSpace(expanded))
                return "";

            var alias = TryResolveKnownFolderAlias(expanded);
            if (!string.IsNullOrEmpty(alias) && Directory.Exists(alias))
                return alias;

            // Rooted
            if (Path.IsPathRooted(expanded) && Directory.Exists(expanded))
                return expanded;

            // Current dir relative
            var cwd = Directory.GetCurrentDirectory();
            var relative = Path.Combine(cwd, expanded);
            if (Directory.Exists(relative))
                return relative;

            // User folders
            foreach (var baseDir in GetCommonBaseDirs())
            {
                var candidate = Path.Combine(baseDir, expanded);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        private static string ResolveExistingFile(string pathOrAlias)
        {
            var expanded = ExpandAndNormalize(pathOrAlias);
            if (string.IsNullOrWhiteSpace(expanded))
                return "";

            // Rooted
            if (Path.IsPathRooted(expanded) && File.Exists(expanded))
                return expanded;

            // Current dir relative
            var cwd = Directory.GetCurrentDirectory();
            var relative = Path.Combine(cwd, expanded);
            if (File.Exists(relative))
                return relative;

            // User folders
            foreach (var baseDir in GetCommonBaseDirs())
            {
                var candidate = Path.Combine(baseDir, expanded);
                if (File.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        private static string ResolveExistingFileOrDirectory(string pathOrAlias)
        {
            var file = ResolveExistingFile(pathOrAlias);
            if (!string.IsNullOrEmpty(file))
                return file;

            var dir = ResolveExistingDirectory(pathOrAlias);
            if (!string.IsNullOrEmpty(dir))
                return dir;

            return "";
        }

        private static string ResolveNonExistingPath(string pathOrAlias)
        {
            var expanded = ExpandAndNormalize(pathOrAlias);
            if (string.IsNullOrWhiteSpace(expanded))
                return "";

            var alias = TryResolveKnownFolderAlias(expanded);
            if (!string.IsNullOrEmpty(alias))
                return alias;

            if (Path.IsPathRooted(expanded))
                return expanded;

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), expanded));
        }

        private static string ExpandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            return Environment.ExpandEnvironmentVariables(path.Trim());
        }

        private static string ExpandAndNormalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var path = TrimOuterQuotes(input.Trim());
            path = ExpandPath(path);
            path = path.Replace('/', '\\');

            return path;
        }

        private static string TryResolveKnownFolderAlias(string input)
        {
            var lower = input.Trim().TrimEnd('\\').TrimEnd('/').ToLowerInvariant();
            return lower switch
            {
                "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "documents" or "docs" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "pictures" or "photos" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                _ => ""
            };
        }

        private static IEnumerable<string> GetCommonBaseDirs()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static string TrimOuterQuotes(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            value = value.Trim();
            if (value.Length >= 2)
            {
                if ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'")))
                    return value.Substring(1, value.Length - 2);
            }
            return value;
        }

        private static List<string> SafeGetFiles(string directory, string searchPattern)
        {
            try
            {
                return Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static bool IsValidFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }
    }
}
