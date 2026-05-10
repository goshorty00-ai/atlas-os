using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Settings;

namespace AtlasAI.SmartHome
{
    internal sealed class RingManagedLiveViewService
    {
        private const int HelperPort = 43119;
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(180),
        };

        private readonly object _gate = new();
        private readonly SemaphoreSlim _helperLifecycleLock = new(1, 1);
        private Process? _helperProcess;
        private string _lastRefreshToken = string.Empty;
        private string _cachedRefreshToken = string.Empty;

        public async Task<RingManagedLiveViewStartResult> StartAsync(RingSettings settings, string deviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (settings == null || string.IsNullOrWhiteSpace(settings.RefreshToken))
            {
                return new RingManagedLiveViewStartResult
                {
                    Ok = false,
                    Message = "Ring account is not linked yet.",
                };
            }

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return new RingManagedLiveViewStartResult
                {
                    Ok = false,
                    Message = "Ring camera id is missing.",
                };
            }

            try
            {
                AppLogger.LogInfo($"[SmartHome][RingManaged] Starting managed live view for camera '{deviceId}'.");
                RingManagedHelperStartResponse? payload = null;

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AppLogger.LogInfo($"[SmartHome][RingManaged] Helper startup attempt {attempt + 1} for '{deviceId}'.");
                    await EnsureHelperRunningAsync(settings.RefreshToken, cancellationToken).ConfigureAwait(false);

                    payload = await StartHelperStreamAsync(deviceId, cancellationToken).ConfigureAwait(false);
                    if (payload == null)
                    {
                        AppLogger.LogWarning($"[SmartHome][RingManaged] Helper returned no start payload for '{deviceId}'.");
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(payload.PlayerUrl))
                    {
                        AppLogger.LogInfo($"[SmartHome][RingManaged] Managed live view launch accepted for '{deviceId}' with player '{payload.PlayerUrl}'.");
                        return new RingManagedLiveViewStartResult
                        {
                            Ok = true,
                            Message = payload.Message,
                            CameraId = payload.CameraId ?? deviceId,
                            PlayerUrl = payload.PlayerUrl ?? string.Empty,
                            ManifestUrl = payload.ManifestUrl ?? string.Empty,
                        };
                    }

                    AppLogger.LogWarning($"[SmartHome][RingManaged] Helper response for '{deviceId}' did not include a usable player URL. Retrying.");
                    await StopHelperStreamAsync(cancellationToken).ConfigureAwait(false);
                    StopHelperProcess();
                }

                AppLogger.LogWarning($"[SmartHome][RingManaged] Managed live view failed for '{deviceId}': {payload?.Message ?? "Atlas could not verify the managed Ring live stream."}");
                return new RingManagedLiveViewStartResult
                {
                    Ok = false,
                    Message = payload?.Message ?? "Atlas could not verify the managed Ring live stream.",
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[SmartHome][RingManaged] Managed live view failed for '{deviceId}'.", ex);
                return new RingManagedLiveViewStartResult
                {
                    Ok = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<RingManagedLiveViewStatusResult> GetStatusAsync(string deviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return new RingManagedLiveViewStatusResult
                {
                    Ok = false,
                    State = "error",
                    Message = "Ring camera id is missing.",
                };
            }

            try
            {
                if (!await IsHelperHealthyAsync(cancellationToken).ConfigureAwait(false))
                {
                    var recovered = await TryRecoverHelperForCameraAsync(deviceId, cancellationToken).ConfigureAwait(false);
                    if (recovered != null)
                        return recovered;

                    return new RingManagedLiveViewStatusResult
                    {
                        Ok = false,
                        State = "error",
                        CameraId = deviceId,
                        Message = "Atlas could not reach the managed Ring live view helper.",
                    };
                }

                using var response = await HttpClient.GetAsync(GetUri($"/stream/status/{Uri.EscapeDataString(deviceId)}"), cancellationToken).ConfigureAwait(false);

                RingManagedHelperStatusResponse? payload = null;
                try
                {
                    payload = await response.Content.ReadFromJsonAsync<RingManagedHelperStatusResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                }

                if (!response.IsSuccessStatusCode || payload == null)
                {
                    return new RingManagedLiveViewStatusResult
                    {
                        Ok = false,
                        State = "error",
                        CameraId = deviceId,
                        Message = payload?.Message ?? $"Ring helper failed with {(int)response.StatusCode}.",
                    };
                }

                return new RingManagedLiveViewStatusResult
                {
                    Ok = payload.Ok,
                    State = payload.State ?? string.Empty,
                    Message = payload.Message ?? string.Empty,
                    CameraId = payload.CameraId ?? deviceId,
                    PlayerUrl = payload.PlayerUrl ?? string.Empty,
                    ManifestUrl = payload.ManifestUrl ?? string.Empty,
                };
            }
            catch (Exception ex)
            {
                return new RingManagedLiveViewStatusResult
                {
                    Ok = false,
                    State = "error",
                    CameraId = deviceId,
                    Message = ex.Message,
                };
            }
        }

        private static async Task<RingManagedHelperStartResponse?> StartHelperStreamAsync(string deviceId, CancellationToken cancellationToken)
        {
            using var response = await HttpClient.PostAsJsonAsync(
                GetUri("/stream/start"),
                new { cameraId = deviceId },
                cancellationToken).ConfigureAwait(false);

            RingManagedHelperStartResponse? payload = null;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<RingManagedHelperStartResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(payload?.Message ?? $"Ring helper failed with {(int)response.StatusCode}.");

            if (payload == null || !payload.Ok)
                throw new InvalidOperationException(payload?.Message ?? "Atlas did not receive a valid Ring helper response.");

            return payload;
        }

        private static async Task StopHelperStreamAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var response = await HttpClient.PostAsJsonAsync(GetUri("/stream/stop"), new { }, cancellationToken).ConfigureAwait(false);
                response.Dispose();
            }
            catch
            {
            }
        }

        public async Task<RingManagedLiveViewStopResult> StopAsync(string deviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AppLogger.LogInfo($"[SmartHome][RingManaged] Stopping managed live view for camera '{deviceId}'.");

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return new RingManagedLiveViewStopResult
                {
                    Ok = true,
                    CameraId = string.Empty,
                    Message = "Ring live view already closed.",
                };
            }

            try
            {
                if (!await IsHelperHealthyAsync(cancellationToken).ConfigureAwait(false))
                {
                    return new RingManagedLiveViewStopResult
                    {
                        Ok = true,
                        CameraId = deviceId,
                        Message = "Ring live view already closed.",
                    };
                }

                using var response = await HttpClient.PostAsJsonAsync(
                    GetUri("/stream/stop"),
                    new { cameraId = deviceId },
                    cancellationToken).ConfigureAwait(false);

                var payload = await response.Content.ReadFromJsonAsync<RingManagedHelperStopResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
                AppLogger.LogInfo($"[SmartHome][RingManaged] Stop helper response for '{deviceId}': {payload?.Message ?? (response.IsSuccessStatusCode ? "Ring live view stopped." : $"Ring helper failed with {(int)response.StatusCode}.")}");
                return new RingManagedLiveViewStopResult
                {
                    Ok = response.IsSuccessStatusCode && (payload?.Ok ?? false),
                    CameraId = deviceId,
                    Message = payload?.Message ?? (response.IsSuccessStatusCode ? "Ring live view stopped." : $"Ring helper failed with {(int)response.StatusCode}."),
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[SmartHome][RingManaged] Stop failed for '{deviceId}': {ex.Message}");
                return new RingManagedLiveViewStopResult
                {
                    Ok = false,
                    CameraId = deviceId,
                    Message = ex.Message,
                };
            }
        }

        private async Task EnsureHelperRunningAsync(string refreshToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _helperLifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _cachedRefreshToken = refreshToken;

                if (string.Equals(_lastRefreshToken, refreshToken, StringComparison.Ordinal) &&
                    await IsHelperHealthyAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                StopHelperProcess();

                var workspaceRoot = FindWorkspaceRoot();
                var scriptPath = Path.Combine(workspaceRoot, "Figma", "AI_Smart_Home", "tools", "ring-live-helper.mjs");
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException("Atlas could not find the Ring live helper script.", scriptPath);

                var nodePath = ResolveNodeExecutable(workspaceRoot);
                if (string.IsNullOrWhiteSpace(nodePath) || !File.Exists(nodePath))
                    throw new FileNotFoundException("Atlas could not find Node.js for Ring live streaming.", nodePath);

                var ffmpegPath = ResolveFfmpegExecutable(workspaceRoot);
                if (string.IsNullOrWhiteSpace(ffmpegPath) || (!string.Equals(ffmpegPath, "ffmpeg", StringComparison.OrdinalIgnoreCase) && !File.Exists(ffmpegPath)))
                    throw new FileNotFoundException("Atlas could not find ffmpeg for Ring live streaming.", ffmpegPath);

                var workingDirectory = Path.GetDirectoryName(scriptPath);
                if (string.IsNullOrWhiteSpace(workingDirectory))
                    throw new InvalidOperationException("Atlas could not determine the Ring helper working directory.");

                var startInfo = new ProcessStartInfo
                {
                    FileName = nodePath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                startInfo.ArgumentList.Add(scriptPath);
                startInfo.Environment["RING_REFRESH_TOKEN"] = refreshToken;
                startInfo.Environment["RING_FFMPEG_PATH"] = ffmpegPath;
                startInfo.Environment["RING_HELPER_PORT"] = HelperPort.ToString();
                startInfo.Environment["RING_HELPER_HOST"] = "127.0.0.1";

                var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true,
                };

                process.OutputDataReceived += (_, _) => { };
                process.OutputDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                        AppLogger.LogInfo($"[SmartHome][RingManaged][helper] {args.Data}");
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                        AppLogger.LogWarning($"[SmartHome][RingManaged][helper] {args.Data}");
                };
                process.Exited += (_, _) =>
                {
                    AppLogger.LogWarning($"[SmartHome][RingManaged] Helper process exited with code {process.ExitCode}.");
                };

                if (!process.Start())
                    throw new InvalidOperationException("Atlas could not start the Ring live helper process.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                lock (_gate)
                {
                    _helperProcess = process;
                    _lastRefreshToken = refreshToken;
                }

                var readyDeadline = DateTime.UtcNow.AddSeconds(20);
                while (DateTime.UtcNow < readyDeadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (process.HasExited)
                        throw new InvalidOperationException("The Ring live helper exited before Atlas could connect to it.");

                    if (await IsHelperHealthyAsync(cancellationToken).ConfigureAwait(false))
                        return;

                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }

                AppLogger.LogWarning("[SmartHome][RingManaged] Helper health endpoint did not come online within the startup timeout.");
                throw new TimeoutException("Atlas timed out while starting the managed Ring live helper.");
            }
            finally
            {
                _helperLifecycleLock.Release();
            }
        }

        private async Task<RingManagedLiveViewStatusResult?> TryRecoverHelperForCameraAsync(string deviceId, CancellationToken cancellationToken)
        {
            string refreshToken;
            lock (_gate)
            {
                refreshToken = _lastRefreshToken;
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
                refreshToken = _cachedRefreshToken;

            if (string.IsNullOrWhiteSpace(refreshToken))
                return null;

            try
            {
                AppLogger.LogWarning($"[SmartHome][RingManaged] Helper became unreachable while tracking '{deviceId}'. Attempting recovery.");
                await EnsureHelperRunningAsync(refreshToken, cancellationToken).ConfigureAwait(false);

                if (!await IsHelperHealthyAsync(cancellationToken).ConfigureAwait(false))
                    return null;

                var restartPayload = await StartHelperStreamAsync(deviceId, cancellationToken).ConfigureAwait(false);
                if (restartPayload == null)
                    return null;

                return new RingManagedLiveViewStatusResult
                {
                    Ok = true,
                    State = "starting",
                    CameraId = restartPayload.CameraId ?? deviceId,
                    PlayerUrl = restartPayload.PlayerUrl ?? string.Empty,
                    ManifestUrl = restartPayload.ManifestUrl ?? string.Empty,
                    Message = string.IsNullOrWhiteSpace(restartPayload.Message)
                        ? "Atlas restarted the Ring live helper and is reopening the stream."
                        : restartPayload.Message,
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[SmartHome][RingManaged] Recovery failed for '{deviceId}': {ex.Message}");
                return null;
            }
        }

        private async Task<bool> IsHelperHealthyAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var response = await HttpClient.GetAsync(GetUri("/health"), cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private void StopHelperProcess()
        {
            Process? process;
            lock (_gate)
            {
                process = _helperProcess;
                _helperProcess = null;
                _lastRefreshToken = string.Empty;
            }

            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
            }
            finally
            {
                AppLogger.LogInfo("[SmartHome][RingManaged] Helper process stopped.");
                process.Dispose();
            }
        }

        private static Uri GetUri(string path)
        {
            return new Uri($"http://127.0.0.1:{HelperPort}{path}", UriKind.Absolute);
        }

        private static string ResolveNodeExecutable(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "node", "node.exe"),
                Path.Combine(workspaceRoot, "node.exe"),
                Path.Combine(AppContext.BaseDirectory, "node", "node.exe"),
                Path.Combine(AppContext.BaseDirectory, "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
                FindOnPath("node.exe") ?? string.Empty,
                FindOnPath("node") ?? string.Empty,
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static string ResolveFfmpegExecutable(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(workspaceRoot, "ffmpeg.exe"),
                Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(Environment.CurrentDirectory, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(Environment.CurrentDirectory, "ffmpeg.exe"),
                FindOnPath("ffmpeg.exe") ?? string.Empty,
                FindOnPath("ffmpeg") ?? string.Empty,
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static string? FindOnPath(string fileName)
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var part in path.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(part))
                        continue;

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

        private static string FindWorkspaceRoot()
        {
            var candidates = new[]
            {
                Path.Combine("D:\\Atlas.OS"),
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
                AppDomain.CurrentDomain.BaseDirectory,
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(Path.Combine(fullPath, "Figma", "AI_Smart_Home")))
                    return fullPath;
            }

            throw new DirectoryNotFoundException("Atlas could not find the workspace root for the Ring live helper.");
        }

        private sealed class RingManagedHelperStartResponse
        {
            public bool Ok { get; init; }
            public string Message { get; init; } = string.Empty;
            public string? CameraId { get; init; }
            public string? PlayerUrl { get; init; }
            public string? ManifestUrl { get; init; }
        }

        private sealed class RingManagedHelperStatusResponse
        {
            public bool Ok { get; init; }
            public string? State { get; init; }
            public string? Message { get; init; }
            public string? CameraId { get; init; }
            public string? PlayerUrl { get; init; }
            public string? ManifestUrl { get; init; }
        }

        private sealed class RingManagedHelperStopResponse
        {
            public bool Ok { get; init; }
            public string Message { get; init; } = string.Empty;
        }
    }
}