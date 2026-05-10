using System.Text;

namespace AtlasAI.Core
{
    public static class ApiKeySanitizer
    {
        public static string SanitizeForHttpHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim().Trim('"');
            var sb = new StringBuilder(trimmed.Length);

            foreach (var ch in trimmed)
            {
                if (ch <= 0x7F && !char.IsWhiteSpace(ch) && !char.IsControl(ch))
                    sb.Append(ch);
            }

            return sb.ToString();
        }
    }
}

