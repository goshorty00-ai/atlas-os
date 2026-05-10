using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AtlasAI.Core;
using AtlasAI.Settings;
using AtlasAI.UI;
using AtlasAI.Voice;

namespace AtlasAI.SmartHome
{
    internal sealed class RingDoorbellMonitorService : IDisposable
    {
        private const int HelperPort = 43120;
        private const string RingLiveHelperBaseUrl = "http://127.0.0.1:43119";
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        private readonly object _gate = new();
        private readonly RingManagedLiveViewService _liveViewService = new();
        private readonly SmartHomeRuntimeService _runtimeService = new();
        private Process? _helperProcess;
        private CancellationTokenSource? _monitorCts;
        private Task? _monitorTask;
        private string _lastRefreshToken = string.Empty;
        private string _lastHandledEventId = string.Empty;

        public void Start()
        {
            lock (_gate)
            {
                if (_monitorTask != null && !_monitorTask.IsCompleted)
                    return;

                _monitorCts = new CancellationTokenSource();
                _monitorTask = Task.Run(() => MonitorLoopAsync(_monitorCts.Token));
            }
        }

        private async Task MonitorLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var ringSettings = SettingsStore.Current?.SmartHome?.Ring;
                    if (ringSettings == null || string.IsNullOrWhiteSpace(ringSettings.RefreshToken))
                    {
                        StopHelperProcess();
                        await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await EnsureHelperRunningAsync(ringSettings.RefreshToken, cancellationToken).ConfigureAwait(false);

                    var nextEvent = await TryGetNextEventAsync(cancellationToken).ConfigureAwait(false);
                    if (nextEvent != null && !string.IsNullOrWhiteSpace(nextEvent.EventId) &&
                        !string.Equals(_lastHandledEventId, nextEvent.EventId, StringComparison.Ordinal))
                    {
                        _lastHandledEventId = nextEvent.EventId;
                        await HandleRingEventAsync(nextEvent).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[RingDoorbellMonitor] Poll failed", ex);
                }

                try
                {
                    await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task HandleRingEventAsync(RingDoorbellEvent nextEvent)
        {
            var cameraName = string.IsNullOrWhiteSpace(nextEvent.CameraName) ? "Front door" : nextEvent.CameraName.Trim();
            var isMotionEvent = string.Equals(nextEvent.EventType, "motion", StringComparison.OrdinalIgnoreCase);
            var spokenText = isMotionEvent
                ? $"Motion detected at {cameraName}."
                : $"Doorbell pressed at {cameraName}.";

            AppLogger.LogInfo($"[RingDoorbellMonitor] Handling {(isMotionEvent ? "motion" : "doorbell")} event '{nextEvent.EventId}' for '{cameraName}' ({nextEvent.CameraId}).");

            _ = Task.Run(async () =>
            {
                try
                {
                    await SpeechCoordinator.Instance.SpeakSystemAsync(spokenText, "ring-doorbell").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[RingDoorbellMonitor] Speech failed", ex);
                }
            });

            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (isMotionEvent)
                        {
                            ToastNotificationManager.Instance.Show(
                                $"Motion detected at {cameraName}.",
                                ToastType.Info,
                                10000);
                        }
                        else
                        {
                            ToastNotificationManager.Instance.ShowAction(
                                $"{cameraName} rang the doorbell.",
                                "Open Live",
                                () => _ = OpenLiveViewAsync(nextEvent.CameraId, cameraName, true),
                                "Dismiss",
                                null,
                                ToastType.Info,
                                12000);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[RingDoorbellMonitor] Toast failed", ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[RingDoorbellMonitor] Dispatcher toast failed", ex);
            }

            if (!isMotionEvent && SettingsStore.Current?.SmartHome?.Agent?.AnswerDoorEnabled == true)
            {
                var liveViewStarted = await OpenLiveViewAsync(nextEvent.CameraId, cameraName, false).ConfigureAwait(false);
                if (liveViewStarted)
                {
                    await SpeakDoorbellReplyAsync(nextEvent.CameraId, cameraName).ConfigureAwait(false);
                }
            }
        }

        private async Task<bool> OpenLiveViewAsync(string cameraId, string cameraName, bool launchPlayer)
        {
            try
            {
                var ringSettings = SettingsStore.Current?.SmartHome?.Ring;
                if (ringSettings == null)
                    return false;

                var result = await _liveViewService.StartAsync(ringSettings, cameraId, CancellationToken.None).ConfigureAwait(false);
                if (!result.Ok || string.IsNullOrWhiteSpace(result.PlayerUrl))
                {
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ToastNotificationManager.Instance.Show(
                                string.IsNullOrWhiteSpace(result.Message) ? $"Atlas could not open {cameraName}." : result.Message,
                                ToastType.Error,
                                5000);
                        }
                        catch
                        {
                        }
                    }));
                    return false;
                }

                if (!launchPlayer)
                {
                    _runtimeService.InvalidateSnapshotCache();
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ToastNotificationManager.Instance.Show(
                                $"Atlas answered {cameraName} and started the live view.",
                                ToastType.Success,
                                6000);
                        }
                        catch
                        {
                        }
                    }));
                    return true;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = result.PlayerUrl,
                    UseShellExecute = true,
                });
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[RingDoorbellMonitor] Open live view failed", ex);
                return false;
            }
        }

        private async Task SpeakDoorbellReplyAsync(string cameraId, string cameraName)
        {
            var reply = GetDoorbellReplyText();
            if (string.IsNullOrWhiteSpace(reply))
                return;

            string? audioPath = null;
            var talkbackStarted = false;
            try
            {
                var provider = new WindowsSapiProvider();
                var synthesis = await provider.SynthesizeAsync(reply, new SynthesisOptions
                {
                    OutputFormat = "wav",
                    Rate = 1.0,
                    Volume = 1.0,
                }).ConfigureAwait(false);

                if (!synthesis.Success || string.IsNullOrWhiteSpace(synthesis.AudioFilePath) || !File.Exists(synthesis.AudioFilePath))
                {
                    AppLogger.LogWarning($"[RingDoorbellMonitor] Doorbell reply synthesis failed for '{cameraName}': {synthesis.ErrorMessage ?? "No audio file was produced."}");
                    return;
                }

                audioPath = synthesis.AudioFilePath;

                await StartTalkbackWithRetryAsync(cameraId).ConfigureAwait(false);
                talkbackStarted = true;

                var audioBytes = await File.ReadAllBytesAsync(audioPath).ConfigureAwait(false);
                using (var audioContent = new ByteArrayContent(audioBytes))
                {
                    audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                    using var uploadResponse = await HttpClient.PostAsync($"{RingLiveHelperBaseUrl}/talkback/chunk/{Uri.EscapeDataString(cameraId)}", audioContent).ConfigureAwait(false);
                    uploadResponse.EnsureSuccessStatusCode();
                }

                await Task.Delay(300).ConfigureAwait(false);

                AppLogger.LogInfo($"[RingDoorbellMonitor] Sent custom doorbell reply to '{cameraName}'.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[RingDoorbellMonitor] Doorbell reply failed for '{cameraName}'.", ex);
            }
            finally
            {
                if (talkbackStarted)
                {
                    try
                    {
                        using var stopResponse = await HttpClient.PostAsJsonAsync($"{RingLiveHelperBaseUrl}/talkback/stop", new { cameraId }).ConfigureAwait(false);
                        stopResponse.EnsureSuccessStatusCode();
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(audioPath))
                {
                    try
                    {
                        File.Delete(audioPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static async Task StartTalkbackWithRetryAsync(string cameraId)
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var startResponse = await HttpClient.PostAsJsonAsync($"{RingLiveHelperBaseUrl}/talkback/start", new
                    {
                        cameraId,
                        mimeType = "audio/wav",
                    }).ConfigureAwait(false);
                    startResponse.EnsureSuccessStatusCode();
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < 3)
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                }
            }

            throw lastError ?? new InvalidOperationException("Atlas could not start Ring talkback for the doorbell reply.");
        }

        private static string GetDoorbellReplyText()
        {
            var command = SettingsStore.Current?.SmartHome?.CustomCommands?
                .FirstOrDefault(item =>
                    string.Equals(item.Id, SmartHomeRuntimeService.AnswerDoorCommandId, StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(item.TargetKind, "atlas-intent", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(item.TargetScope, "door-answer", StringComparison.OrdinalIgnoreCase)));

            if (command == null)
                return string.Empty;

            return command.DoorbellResponseText?.Trim() ?? string.Empty;
        }

        private async Task<RingDoorbellEvent?> TryGetNextEventAsync(CancellationToken cancellationToken)
        {
            using var response = await HttpClient.GetAsync(GetUri("/events/next"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<RingDoorbellEventEnvelope>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return payload?.Event;
        }

        private async Task EnsureHelperRunningAsync(string refreshToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(_lastRefreshToken, refreshToken, StringComparison.Ordinal) &&
                await IsHelperHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            StopHelperProcess();

            var workspaceRoot = FindWorkspaceRoot();
            var scriptPath = Path.Combine(workspaceRoot, "Figma", "AI_Smart_Home", "tools", "ring-doorbell-monitor.mjs");
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Atlas could not find the Ring doorbell monitor script.", scriptPath);

            var nodePath = ResolveNodeExecutable(workspaceRoot);
            if (string.IsNullOrWhiteSpace(nodePath) || !File.Exists(nodePath))
                throw new FileNotFoundException("Atlas could not find Node.js for Ring doorbell monitoring.", nodePath);

            var workingDirectory = Path.GetDirectoryName(scriptPath);
            if (string.IsNullOrWhiteSpace(workingDirectory))
                throw new InvalidOperationException("Atlas could not determine the Ring doorbell monitor working directory.");

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
            startInfo.Environment["RING_DOORBELL_MONITOR_PORT"] = HelperPort.ToString();
            startInfo.Environment["RING_DOORBELL_MONITOR_HOST"] = "127.0.0.1";

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    AppLogger.LogInfo($"[RingDoorbellMonitor][helper] {args.Data}");
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    AppLogger.LogWarning($"[RingDoorbellMonitor][helper] {args.Data}");
            };

            if (!process.Start())
                throw new InvalidOperationException("Atlas could not start the Ring doorbell monitor process.");

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
                    throw new InvalidOperationException("The Ring doorbell monitor exited before Atlas could connect to it.");

                if (await IsHelperHealthyAsync(cancellationToken).ConfigureAwait(false))
                    return;

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException("Atlas timed out while starting the Ring doorbell monitor.");
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
                process.Dispose();
            }
        }

        private static Uri GetUri(string path)
            => new($"http://127.0.0.1:{HelperPort}{path}", UriKind.Absolute);

        private static string ResolveNodeExecutable(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
                Path.Combine(workspaceRoot, "node.exe"),
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return candidate;
            }

            return "node";
        }

        private static string FindWorkspaceRoot()
        {
            var candidates = new[]
            {
                AppContext.BaseDirectory,
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
                @"D:\Atlas.OS",
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    var current = new DirectoryInfo(candidate);
                    while (current != null)
                    {
                        if (File.Exists(Path.Combine(current.FullName, "AtlasAI.csproj")))
                            return current.FullName;

                        current = current.Parent;
                    }
                }
                catch
                {
                }
            }

            return @"D:\Atlas.OS";
        }

        public void Dispose()
        {
            try
            {
                lock (_gate)
                {
                    _monitorCts?.Cancel();
                }
            }
            catch
            {
            }

            StopHelperProcess();
            lock (_gate)
            {
                _monitorCts?.Dispose();
                _monitorCts = null;
                _monitorTask = null;
            }
        }

        private sealed class RingDoorbellEventEnvelope
        {
            [JsonPropertyName("event")]
            public RingDoorbellEvent? Event { get; init; }
        }

        private sealed class RingDoorbellEvent
        {
            [JsonPropertyName("eventId")]
            public string EventId { get; init; } = string.Empty;

            [JsonPropertyName("eventType")]
            public string EventType { get; init; } = string.Empty;

            [JsonPropertyName("cameraId")]
            public string CameraId { get; init; } = string.Empty;

            [JsonPropertyName("cameraName")]
            public string CameraName { get; init; } = string.Empty;

            [JsonPropertyName("occurredAtUtc")]
            public string OccurredAtUtc { get; init; } = string.Empty;
        }
    }
}