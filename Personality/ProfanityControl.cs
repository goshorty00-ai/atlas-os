using System;
using System.IO;
using System.Text.Json;
using AtlasAI.Settings;

namespace AtlasAI.Personality
{
    public static class ProfanityControl
    {
        private static int? _cached;

        public static int GetIntensity()
        {
            try
            {
                var settings = SettingsStore.Current;
                var allow = settings?.UnfilteredAllowProfanity ?? true;
                var level = Math.Clamp(settings?.UnfilteredChaosIntensity ?? 3, 1, 5);

                if (!allow)
                    return 0;

                // Map 1–5 to 0–3 profanity intensity.
                return level switch
                {
                    <= 2 => 0,
                    3 => 1,
                    4 => 2,
                    _ => 3
                };
            }
            catch
            {
            }

            if (_cached.HasValue) return _cached.Value;
#if PERSONAL_BUILD
            _cached = 2;
#else
            _cached = 0;
#endif
            return _cached.Value;
        }

        public static void SetIntensity(int level)
        {
            var v = Clamp(level);
            _cached = v;
            try
            {
                SettingsStore.Update(settings =>
                {
                    switch (v)
                    {
                        case 0:
                            settings.UnfilteredAllowProfanity = false;
                            settings.UnfilteredChaosIntensity = 3;
                            break;
                        case 1:
                            settings.UnfilteredAllowProfanity = true;
                            settings.UnfilteredChaosIntensity = 3;
                            break;
                        case 2:
                            settings.UnfilteredAllowProfanity = true;
                            settings.UnfilteredChaosIntensity = 4;
                            break;
                        default:
                            settings.UnfilteredAllowProfanity = true;
                            settings.UnfilteredChaosIntensity = 5;
                            break;
                    }
                });
            }
            catch
            {
            }
        }

        private static int Clamp(int n) => n < 0 ? 0 : n > 3 ? 3 : n;
    }
}
