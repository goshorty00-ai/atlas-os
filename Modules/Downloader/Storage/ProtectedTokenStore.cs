using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AtlasAI.Modules.Downloader.Storage
{
    public class ProtectedTokenStore
    {
        private readonly string _folder;

        public ProtectedTokenStore(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(_folder);
        }

        public void Save(string key, string token)
        {
            var path = Path.Combine(_folder, $"{Sanitize(key)}.bin");
            var bytes = Encoding.UTF8.GetBytes(token ?? "");
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, protectedBytes);
        }

        public string Load(string key)
        {
            var path = Path.Combine(_folder, $"{Sanitize(key)}.bin");
            if (!File.Exists(path)) return "";
            var protectedBytes = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        public void Delete(string key)
        {
            var path = Path.Combine(_folder, $"{Sanitize(key)}.bin");
            if (File.Exists(path)) File.Delete(path);
        }

        private static string Sanitize(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');
            return key;
        }
    }
}

