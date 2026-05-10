using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Services
{
    public sealed class OfflineCaptchaSolverService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

        public string InstallDirectory { get; }

        public OfflineCaptchaSolverService()
        {
            InstallDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasAI",
                "OfflineCaptchaSolver");
        }

        public bool IsInstalled
        {
            get
            {
                try
                {
                    return File.Exists(Path.Combine(InstallDirectory, "ocr.js")) &&
                           File.Exists(Path.Combine(InstallDirectory, "package.json")) &&
                           (File.Exists(Path.Combine(InstallDirectory, "node.exe")) || !string.IsNullOrWhiteSpace(FindOnPath("node.exe"))) &&
                           Directory.Exists(Path.Combine(InstallDirectory, "darknet64"));
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<bool> EnsureInstalledAsync(CancellationToken ct = default)
        {
            try
            {
                Directory.CreateDirectory(InstallDirectory);

                if (!IsInstalled)
                {
                    var url = await GetLatestStandaloneWinZipUrlAsync(ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(url))
                        return false;

                    var tmpRoot = Path.Combine(Path.GetTempPath(), "AtlasAI", "OfflineCaptchaSolver", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tmpRoot);
                    var zipPath = Path.Combine(tmpRoot, "solver.zip");

                    await DownloadFileAsync(url!, zipPath, ct).ConfigureAwait(false);

                    var extractDir = Path.Combine(tmpRoot, "extract");
                    Directory.CreateDirectory(extractDir);
                    ExtractZipSafe(zipPath, extractDir);

                    var payload = FindOfflineCaptchaSolverFolder(extractDir);
                    if (string.IsNullOrWhiteSpace(payload) || !Directory.Exists(payload))
                        return false;

                    CopyDirectory(payload, InstallDirectory);
                }

                var nodeModulesDir = Path.Combine(InstallDirectory, "node_modules");
                if (!Directory.Exists(nodeModulesDir) || !Directory.EnumerateFileSystemEntries(nodeModulesDir).Any())
                {
                    var npm = FindOnPath("npm.cmd") ?? FindOnPath("npm");
                    if (string.IsNullOrWhiteSpace(npm))
                        return false;

                    var psi = new ProcessStartInfo
                    {
                        FileName = npm,
                        Arguments = "install --no-fund --no-audit",
                        WorkingDirectory = InstallDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null) return false;
                    using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
                    await p.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (p.ExitCode != 0)
                        return false;
                }

                return IsInstalled;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> TrySolveAsync(string hostKey, byte[] captchaBytes, CancellationToken ct = default)
        {
            try
            {
                if (!await EnsureInstalledAsync(ct).ConfigureAwait(false))
                    return null;

                var nodeExe = File.Exists(Path.Combine(InstallDirectory, "node.exe"))
                    ? Path.Combine(InstallDirectory, "node.exe")
                    : (FindOnPath("node.exe") ?? "");

                if (string.IsNullOrWhiteSpace(nodeExe) || !File.Exists(nodeExe))
                    return null;

                var inputPath = Path.Combine(InstallDirectory, "input.gif");
                var resultPath = Path.Combine(InstallDirectory, "result.txt");

                try { if (File.Exists(resultPath)) File.Delete(resultPath); } catch { }
                await File.WriteAllBytesAsync(inputPath, captchaBytes, ct).ConfigureAwait(false);

                var psi = new ProcessStartInfo
                {
                    FileName = nodeExe,
                    Arguments = $"ocr.js {hostKey}",
                    WorkingDirectory = InstallDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return null;

                using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
                await p.WaitForExitAsync(ct).ConfigureAwait(false);
                if (p.ExitCode != 0)
                    return null;

                if (!File.Exists(resultPath))
                    return null;

                var text = (await File.ReadAllTextAsync(resultPath, ct).ConfigureAwait(false) ?? "").Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string?> GetLatestStandaloneWinZipUrlAsync(CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/cracker0dks/CaptchaSolver/releases/latest");
                req.Headers.TryAddWithoutValidation("User-Agent", "AtlasAI/1.0");
                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                    return null;

                var preferred = assets.EnumerateArray()
                    .Select(a =>
                    {
                        var name = a.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                        var url = a.TryGetProperty("browser_download_url", out var u) ? (u.GetString() ?? "") : "";
                        return (name: name.Trim(), url: url.Trim());
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.name) && !string.IsNullOrWhiteSpace(x.url))
                    .Where(x => x.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var win = preferred.FirstOrDefault(x => x.name.Contains("standalone", StringComparison.OrdinalIgnoreCase) && x.name.Contains("win", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(win.url)) return win.url;

                win = preferred.FirstOrDefault(x => x.name.Contains("win", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(win.url)) return win.url;

                var any = preferred.FirstOrDefault();
                return string.IsNullOrWhiteSpace(any.url) ? null : any.url;
            }
            catch
            {
                return null;
            }
        }

        private static async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "AtlasAI/1.0");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(destPath, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        private static void ExtractZipSafe(string zipPath, string destDir)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var destRoot = Path.GetFullPath(destDir);
            foreach (var entry in archive.Entries)
            {
                var rel = (entry.FullName ?? "").Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(rel)) continue;
                if (rel.Contains("..", StringComparison.Ordinal)) continue;

                var outPath = Path.GetFullPath(Path.Combine(destRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!outPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    Directory.CreateDirectory(outPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? destRoot);
                entry.ExtractToFile(outPath, overwrite: true);
            }
        }

        private static string? FindOfflineCaptchaSolverFolder(string root)
        {
            try
            {
                var p = Directory.GetDirectories(root, "offlineCaptchaSolver", SearchOption.AllDirectories).FirstOrDefault();
                return string.IsNullOrWhiteSpace(p) ? null : p;
            }
            catch
            {
                return null;
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            var src = new DirectoryInfo(sourceDir);
            if (!src.Exists) return;

            Directory.CreateDirectory(destDir);
            foreach (var file in src.GetFiles("*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, file.FullName);
                var target = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destDir);
                file.CopyTo(target, overwrite: true);
            }
        }

        private static string? FindOnPath(string fileName)
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var part in path.Split(Path.PathSeparator).Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    try
                    {
                        var candidate = Path.Combine(part.Trim(), fileName);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return null;
        }
    }
}

