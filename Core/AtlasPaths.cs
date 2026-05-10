using System;
using System.Collections.Generic;
using System.IO;

namespace AtlasAI.Core
{
    public static class AtlasPaths
    {
        public static string RoamingDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");

        public static string LocalDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasAI");

        public static string RoamingVoiceKeysPath => Path.Combine(RoamingDir, "voice_keys.json");
        public static string LocalVoiceKeysPath => Path.Combine(LocalDir, "voice_keys.json");

        public static IEnumerable<string> VoiceKeysReadCandidates()
        {
            yield return LocalVoiceKeysPath;
            yield return RoamingVoiceKeysPath;
        }
    }
}
