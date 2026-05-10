using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.SmartHome
{
    internal sealed class SmartHomeCameraRecordingService
    {
        private sealed class RecordingSession
        {
            public required string RecordingId { get; init; }
            public required Process Process { get; init; }
            public required string OutputPath { get; init; }
            public required string CameraName { get; init; }
        }

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly System.Collections.Generic.Dictionary<string, RecordingSession> _recordingSessions = new(StringComparer.OrdinalIgnoreCase);

        public bool IsRecording
        {
            get
            {
                return _recordingSessions.Values.Any(static session => !session.Process.HasExited);
            }
        }

        public string RecordingPath => _recordingSessions.Values.FirstOrDefault(static session => !session.Process.HasExited)?.OutputPath ?? string.Empty;

        public int ActiveRecordingCount => _recordingSessions.Count(static pair => !pair.Value.Process.HasExited);

        public bool IsRecordingSession(string recordingId)
        {
            if (string.IsNullOrWhiteSpace(recordingId))
                return false;

            return _recordingSessions.TryGetValue(recordingId.Trim(), out var session) && !session.Process.HasExited;
        }

        public string GetRecordingPath(string recordingId)
        {
            if (string.IsNullOrWhiteSpace(recordingId))
                return string.Empty;

            return _recordingSessions.TryGetValue(recordingId.Trim(), out var session) && !session.Process.HasExited
                ? session.OutputPath
                : string.Empty;
        }

        public async Task<SmartHomeCameraRecordingResult> StartAsync(string sourceUrl, string cameraName, string? recordingId, CancellationToken cancellationToken)
        {
            if (!IsSupportedSourceUrl(sourceUrl))
            {
                return new SmartHomeCameraRecordingResult
                {
                    Ok = false,
                    RecordingId = NormalizeRecordingId(recordingId),
                    Message = "Atlas needs a direct camera stream before it can record.",
                };
            }

            var effectiveRecordingId = NormalizeRecordingId(recordingId);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                PruneCompletedSessions();

                if (_recordingSessions.TryGetValue(effectiveRecordingId, out var existingSession) && !existingSession.Process.HasExited)
                {
                    return new SmartHomeCameraRecordingResult
                    {
                        Ok = false,
                        RecordingId = effectiveRecordingId,
                        Message = $"{existingSession.CameraName} is already recording.",
                        OutputPath = existingSession.OutputPath,
                    };
                }

                var workspaceRoot = FindWorkspaceRoot();
                var ffmpegPath = ResolveFfmpegExecutable(workspaceRoot);
                if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
                {
                    return new SmartHomeCameraRecordingResult
                    {
                        Ok = false,
                        RecordingId = effectiveRecordingId,
                        Message = "Atlas could not find ffmpeg for camera recording.",
                    };
                }

                var recordingsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    "Atlas Camera Recordings");
                Directory.CreateDirectory(recordingsDirectory);

                var safeCameraName = SanitizeFileName(string.IsNullOrWhiteSpace(cameraName) ? "camera" : cameraName);
                var outputPath = Path.Combine(recordingsDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeCameraName}.mp4");
                string lastError = string.Empty;

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                startInfo.ArgumentList.Add("-hide_banner");
                startInfo.ArgumentList.Add("-loglevel");
                startInfo.ArgumentList.Add("error");
                startInfo.ArgumentList.Add("-y");
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(sourceUrl);
                startInfo.ArgumentList.Add("-map");
                startInfo.ArgumentList.Add("0");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("copy");
                startInfo.ArgumentList.Add("-movflags");
                startInfo.ArgumentList.Add("+faststart");
                startInfo.ArgumentList.Add(outputPath);

                var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true,
                };

                process.OutputDataReceived += (_, _) => { };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        lastError = args.Data;
                    }
                };

                if (!process.Start())
                {
                    return new SmartHomeCameraRecordingResult
                    {
                        Ok = false,
                        RecordingId = effectiveRecordingId,
                        Message = "Atlas could not start the camera recording process.",
                    };
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _recordingSessions[effectiveRecordingId] = new RecordingSession
                {
                    RecordingId = effectiveRecordingId,
                    Process = process,
                    OutputPath = outputPath,
                    CameraName = string.IsNullOrWhiteSpace(cameraName) ? "camera" : cameraName,
                };

                await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
                if (process.HasExited)
                {
                    _recordingSessions.Remove(effectiveRecordingId);

                    return new SmartHomeCameraRecordingResult
                    {
                        Ok = false,
                        RecordingId = effectiveRecordingId,
                        Message = string.IsNullOrWhiteSpace(lastError)
                            ? "Atlas could not start recording that camera stream."
                            : lastError,
                    };
                }

                return new SmartHomeCameraRecordingResult
                {
                    Ok = true,
                    RecordingId = effectiveRecordingId,
                    Message = "Camera recording started.",
                    OutputPath = outputPath,
                };
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<SmartHomeCameraRecordingResult> StopAsync(string? recordingId, CancellationToken cancellationToken)
        {
            var effectiveRecordingId = NormalizeRecordingId(recordingId);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                PruneCompletedSessions();

                if (!_recordingSessions.TryGetValue(effectiveRecordingId, out var session))
                {
                    return new SmartHomeCameraRecordingResult
                    {
                        Ok = true,
                        RecordingId = effectiveRecordingId,
                        Message = "Camera recording already stopped.",
                    };
                }

                _recordingSessions.Remove(effectiveRecordingId);

                var process = session.Process;
                var outputPath = session.OutputPath;

                try
                {
                    if (!process.HasExited)
                    {
                        await process.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
                        await process.StandardInput.FlushAsync().ConfigureAwait(false);
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(6));

                        try
                        {
                            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(true);
                                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                    }
                }
                finally
                {
                    process.Dispose();
                }

                return new SmartHomeCameraRecordingResult
                {
                    Ok = true,
                    RecordingId = effectiveRecordingId,
                    Message = string.IsNullOrWhiteSpace(outputPath)
                        ? "Camera recording stopped."
                        : $"Camera recording saved to {outputPath}.",
                    OutputPath = outputPath,
                };
            }
            finally
            {
                _gate.Release();
            }
        }

        public static bool IsSupportedSourceUrl(string sourceUrl)
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "rtsps", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "rtmp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "rtmps", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
            return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".mjpeg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".mjpg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
        }

        private void PruneCompletedSessions()
        {
            var completedIds = _recordingSessions
                .Where(static pair => pair.Value.Process.HasExited)
                .Select(static pair => pair.Key)
                .ToArray();

            foreach (var completedId in completedIds)
            {
                if (_recordingSessions.Remove(completedId, out var completedSession))
                {
                    try
                    {
                        completedSession.Process.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string NormalizeRecordingId(string? recordingId)
        {
            var normalized = (recordingId ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
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
                {
                    return candidate;
                }
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
                    {
                        continue;
                    }

                    try
                    {
                        var candidate = Path.Combine(part.Trim(), fileName);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
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
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(Path.Combine(fullPath, "Figma", "AI_Smart_Home")) || File.Exists(Path.Combine(fullPath, "AtlasAI.csproj")))
                {
                    return fullPath;
                }
            }

            return Directory.GetCurrentDirectory();
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = value.Trim();
            foreach (var character in invalid)
            {
                sanitized = sanitized.Replace(character, '_');
            }

            return string.IsNullOrWhiteSpace(sanitized) ? "camera" : sanitized;
        }
    }
}