using System;
using System.Security.Cryptography;
using System.Text;

namespace AtlasAI.Core
{
    public static class SecretProtector
    {
        private const string Prefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AtlasAI.Secrets.v1");

        public static string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return "";
            var data = Encoding.UTF8.GetBytes(plaintext);
            var protectedBytes = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }

        public static string Unprotect(string protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue)) return "";
            if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal)) return protectedValue;

            var b64 = protectedValue.Substring(Prefix.Length);
            var protectedBytes = Convert.FromBase64String(b64);
            var data = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }

        public static string UnprotectIfNeeded(string value)
        {
            try
            {
                return Unprotect(value);
            }
            catch
            {
                return "";
            }
        }
    }
}
