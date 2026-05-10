using System;
using System.Linq;
using System.Threading;
using System.Windows;
using AtlasAI.Brain;
using AtlasAI.Settings;
using AtlasAI.UI;

namespace AtlasAI.Personality
{
    internal static class ProactiveBanterService
    {
        private static readonly object Sync = new();
        private static Timer? _timer;
        private static DateTime _lastToastUtc = DateTime.MinValue;
        private static readonly Random Rng = new(unchecked(Environment.TickCount));

        // Keep it non-intrusive.
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan MinToastInterval = TimeSpan.FromMinutes(20);

        private static readonly string[] DevCheckIns =
        {
            "How’s the coding going — need me to kick the agent into gear?",
            "You still alive over there, mate? Want me to take a swing at it?",
            "Need a hand, mate, or are you enjoying the suffering?",
            "If you want me to jump in, say the word — I’m bored."
        };

        private static readonly string[] DevCheckInsSpicy =
        {
            "How’s the coding going, mate — want me to boot the agent up the arse?",
            "Need me to drag this over the finish line, mate?",
            "Say the word and I’ll kick the lazy bastard into the next commit.",
            "Want me to take over, mate, or are we doing this the hard way on purpose?"
        };

        private static readonly string[] Jokes =
        {
            "Quick one: I tried to tell a UDP joke… but I’m not sure you got it.",
            "If this build breaks again, I’m blaming cosmic rays. It’s tradition.",
            "Reminder: it’s only ‘technical debt’ if you plan to pay it back."
        };

        public static void Start()
        {
            lock (Sync)
            {
                if (_timer != null) return;
                _timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                try { _timer?.Dispose(); } catch { }
                _timer = null;
            }
        }

        private static void Tick()
        {
            try
            {
                // Only toast when the app is foreground/active.
                if (!IsAnyWindowActive())
                    return;

                var settings = SettingsStore.Current;
                if (settings == null)
                    return;

                if (!settings.AllowProactive)
                    return;

                if (DateTime.UtcNow < settings.UnfilteredChillModeUntil)
                    return;

                var level = Math.Clamp(settings.UnfilteredChaosIntensity, 1, 5);
                if (level < 4)
                    return;

                if (DateTime.UtcNow - _lastToastUtc < MinToastInterval)
                    return;

                // Infer what the user is doing.
                WorkspaceEngine.Refresh(Environment.CurrentDirectory);
                var mode = WorkspaceEngine.ActiveWorkspace;

                // Don’t interrupt movie/music/gaming.
                if (mode is WorkspaceMode.Media or WorkspaceMode.Gaming)
                    return;

                if (mode != WorkspaceMode.Development)
                    return;

                // Prefer a check-in; occasionally drop a joke.
                var roll = Rng.NextDouble();
                var message = roll < 0.20 ? Pick(Jokes) : Pick(level >= 5 ? DevCheckInsSpicy : DevCheckIns);

                message = ApplySalutation(message, settings);

                ToastNotificationManager.Instance.Show(message, ToastType.Info, 2800);
                _lastToastUtc = DateTime.UtcNow;
            }
            catch
            {
            }
        }

        private static bool IsAnyWindowActive()
        {
            try
            {
                var app = Application.Current;
                if (app == null) return false;
                return app.Windows.OfType<Window>().Any(w => w != null && w.IsVisible && w.IsActive);
            }
            catch
            {
                return false;
            }
        }

        private static string ApplySalutation(string message, AtlasSettings settings)
        {
            try
            {
                var preferredName = (settings.PreferredName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(preferredName))
                {
                    // Keep it casual.
                    return $"{preferredName} — {message}";
                }

                // If the template already says mate, don’t add anything.
                if (message.IndexOf("mate", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return message;
            }
            catch
            {
                return message;
            }
        }

        private static string Pick(string[] pool)
        {
            if (pool == null || pool.Length == 0) return "";
            return pool[Rng.Next(pool.Length)];
        }
    }
}
