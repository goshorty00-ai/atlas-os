using System;

namespace AtlasAI.MediaScanner
{
    public interface IPlaybackOutput
    {
        void StopPlayback();
        string PlaybackOutputId { get; }
    }

    public static class PlaybackOutputCoordinator
    {
        private static readonly object Sync = new object();
        private static WeakReference<IPlaybackOutput>? _active;

        public static void SetActive(IPlaybackOutput output)
        {
            if (output == null) return;

            lock (Sync)
            {
                if (_active != null && _active.TryGetTarget(out var prev) && prev != null)
                {
                    if (!ReferenceEquals(prev, output))
                    {
                        try { prev.StopPlayback(); } catch { }
                    }
                }

                _active = new WeakReference<IPlaybackOutput>(output);
            }
        }

        public static void StopActive()
        {
            lock (Sync)
            {
                if (_active != null && _active.TryGetTarget(out var prev) && prev != null)
                {
                    try { prev.StopPlayback(); } catch { }
                }
                _active = null;
            }
        }
    }
}
