using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AtlasAI.Core;
using AtlasAI.UI;

namespace AtlasAI.Services
{
    public sealed class SelfHealingMonitor
    {
        private sealed class FailureBucket
        {
            public int Count;
            public DateTime FirstUtc;
            public DateTime LastUtc;
            public DateTime LastToastUtc;
            public DateTime SnoozedUntilUtc;
        }

        private static readonly Lazy<SelfHealingMonitor> _instance = new Lazy<SelfHealingMonitor>(() => new SelfHealingMonitor());
        public static SelfHealingMonitor Instance => _instance.Value;

        private readonly object _gate = new object();
        private readonly Dictionary<string, FailureBucket> _buckets = new Dictionary<string, FailureBucket>(StringComparer.OrdinalIgnoreCase);

        private SelfHealingMonitor() { }

        public void ReportUnhandledException(string source, Exception ex)
        {
            try
            {
                if (ex == null) return;
                var key = "unhandled:" + (source ?? "unknown");
                if (!TryIncrement(key, TimeSpan.FromMinutes(5), out var bucket)) return;

                // Only toast once per 2 minutes per source.
                if ((DateTime.UtcNow - bucket.LastToastUtc) < TimeSpan.FromMinutes(2)) return;
                bucket.LastToastUtc = DateTime.UtcNow;

                ToastOnUi(() =>
                {
                    ToastNotificationManager.Instance.ShowAction(
                        "Atlas hit an unexpected error. Open crash log?",
                        "Open Log",
                        () => TryOpenFile(GetCrashLogPath()),
                        "Ignore",
                        null,
                        ToastType.Error,
                        12000);
                });
            }
            catch
            {
            }
        }

        public void ReportApiFailure(string provider, string url, int statusCode, string detail, Action retryOrRefresh)
        {
            try
            {
                var apiToasts = "";
                try { apiToasts = (IntegrationKeyStore.GetDecrypted("api_toasts") ?? "").Trim(); } catch { apiToasts = ""; }
                if (!string.Equals(apiToasts, "1", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(apiToasts, "true", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(apiToasts, "on", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var p = (provider ?? "API").Trim();
                if (string.IsNullOrWhiteSpace(p)) p = "API";

                if (string.Equals(p, "TMDB", StringComparison.OrdinalIgnoreCase))
                {
                    var tmdbKey = (IntegrationKeyStore.GetDecrypted("tmdb") ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(tmdbKey))
                        return;
                }

                var normalizedUrl = NormalizeUrlForKey(url);
                var is404 = statusCode == 404;

                // Separate buckets for 404s vs general failures.
                var key = is404
                    ? $"{p}:404:{normalizedUrl}"
                    : $"{p}:fail";

                var window = is404 ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(3);
                if (!TryIncrement(key, window, out var bucket)) return;

                // Thresholds.
                var threshold = is404 ? 3 : 2;
                if (bucket.Count < threshold) return;

                if (bucket.SnoozedUntilUtc > DateTime.UtcNow) return;

                // Rate limit toast spam.
                var toastCooldown = string.Equals(p, "TMDB", StringComparison.OrdinalIgnoreCase) && !is404
                    ? TimeSpan.FromHours(1)
                    : TimeSpan.FromMinutes(3);
                if ((DateTime.UtcNow - bucket.LastToastUtc) < toastCooldown) return;
                bucket.LastToastUtc = DateTime.UtcNow;
                bucket.SnoozedUntilUtc = string.Equals(p, "TMDB", StringComparison.OrdinalIgnoreCase) && !is404
                    ? DateTime.UtcNow.AddHours(1)
                    : DateTime.UtcNow.AddMinutes(15);

                ToastOnUi(() =>
                {
                    var what = string.IsNullOrWhiteSpace(detail) ? p : $"{p} ({detail})";
                    var msg = is404
                        ? $"Repeated 404s from {what}. Want me to refresh metadata?"
                        : $"{what} failed twice. Want me to open Settings and refresh?";

                    var hasRefresh = retryOrRefresh != null;

                    ToastNotificationManager.Instance.ShowAction(
                        msg,
                        is404 ? "Refresh" : "Settings",
                        () =>
                        {
                            try
                            {
                                if (is404)
                                    retryOrRefresh?.Invoke();
                                else
                                    TryOpenSettingsWindow();
                            }
                            catch
                            {
                            }
                        },
                        is404 ? "Ignore" : (hasRefresh ? "Refresh" : "Ignore (1h)"),
                        is404
                            ? null
                            : (hasRefresh
                                ? (Action)(() => { try { retryOrRefresh?.Invoke(); } catch { } })
                                : (Action)(() =>
                                {
                                    try { Snooze(key, TimeSpan.FromHours(1)); } catch { }
                                })),
                        ToastType.Warning,
                        12000);
                });
            }
            catch
            {
            }
        }

        public void ReportApiFailure(string provider, string url, int statusCode, string detail)
        {
            ReportApiFailure(provider, url, statusCode, detail, null);
        }

        public void ReportNullPoster(string title, string provider, Action rescan)
        {
            try
            {
                var t = (title ?? "").Trim();
                if (string.IsNullOrWhiteSpace(t)) t = "this item";
                var p = (provider ?? "metadata").Trim();
                if (string.IsNullOrWhiteSpace(p)) p = "metadata";

                var key = $"poster:null:{p}:{t.ToLowerInvariant()}";
                if (!TryIncrement(key, TimeSpan.FromMinutes(10), out var bucket)) return;

                if (bucket.Count < 2) return;
                if ((DateTime.UtcNow - bucket.LastToastUtc) < TimeSpan.FromMinutes(10)) return;
                bucket.LastToastUtc = DateTime.UtcNow;

                ToastOnUi(() =>
                {
                    ToastNotificationManager.Instance.ShowAction(
                        $"Poster data is missing for {t}. Rescan metadata?",
                        "Rescan",
                        () =>
                        {
                            try { rescan?.Invoke(); } catch { }
                        },
                        "Ignore",
                        null,
                        ToastType.Info,
                        12000);
                });
            }
            catch
            {
            }
        }

        private bool TryIncrement(string key, TimeSpan window, out FailureBucket bucket)
        {
            bucket = null;
            if (string.IsNullOrWhiteSpace(key)) return false;

            lock (_gate)
            {
                if (!_buckets.TryGetValue(key, out bucket))
                {
                    bucket = new FailureBucket { Count = 1, FirstUtc = DateTime.UtcNow, LastUtc = DateTime.UtcNow, LastToastUtc = DateTime.MinValue, SnoozedUntilUtc = DateTime.MinValue };
                    _buckets[key] = bucket;
                    return true;
                }

                var now = DateTime.UtcNow;
                if ((now - bucket.FirstUtc) > window)
                {
                    bucket.Count = 1;
                    bucket.FirstUtc = now;
                    bucket.LastUtc = now;
                    return true;
                }

                bucket.Count++;
                bucket.LastUtc = now;
                return true;
            }
        }

        private void Snooze(string key, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_gate)
            {
                if (!_buckets.TryGetValue(key, out var b) || b == null) return;
                b.SnoozedUntilUtc = DateTime.UtcNow.Add(duration);
                b.LastToastUtc = DateTime.UtcNow;
            }
        }

        private static string NormalizeUrlForKey(string url)
        {
            try
            {
                var u = (url ?? "").Trim();
                if (string.IsNullOrWhiteSpace(u)) return "";
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) return u;
                // Host + path only; drop query to avoid high-cardinality.
                return (uri.Host + uri.AbsolutePath).ToLowerInvariant();
            }
            catch
            {
                return (url ?? "").Trim().ToLowerInvariant();
            }
        }

        private static string GetCrashLogPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI", "crash_log.txt");
        }

        private static void ToastOnUi(Action a)
        {
            try
            {
                if (a == null) return;
                var app = Application.Current;
                if (app == null)
                    return;

                var disp = app.Dispatcher;
                if (disp == null)
                    return;

                if (disp.CheckAccess())
                    a();
                else
                    disp.BeginInvoke(a, DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private static void TryOpenFile(string path)
        {
            try
            {
                var p = (path ?? "").Trim();
                if (string.IsNullOrWhiteSpace(p)) return;
                if (!File.Exists(p)) return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = p,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private static void TryOpenSettingsWindow()
        {
            try
            {
                ToastOnUi(() =>
                {
                    try
                    {
                        var w = new global::AtlasAI.SettingsWindow();
                        w.Owner = Application.Current != null ? Application.Current.MainWindow : null;
                        w.Show();
                        w.Activate();
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }
    }
}
