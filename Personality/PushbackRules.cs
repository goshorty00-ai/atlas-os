using System;
using System.Text.RegularExpressions;

namespace AtlasAI.Personality
{
#if PERSONAL_BUILD
    public static class PushbackRules
    {
        private static readonly Regex Risky = new(@"(delete\s+system32|rm\s+-rf\s+c:\\?windows\\?system32|format\s+[a-z]:|diskpart\s+clean|takeown\s+/f\s+c:\\?windows\\?system32|del\s+c:\\?windows\\?system32|dd\s+if=|mkfs|bcdedit\s+/deletevalue\s+|disable\s+(defender|firewall))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly (Regex pattern, string correction, string next)[] Misconceptions =
        {
            (new Regex(@"\bram\s+is\s+storage\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "No — RAM is volatile memory, not storage.",
                "Next: tell me the slowdown you see; I’ll generate a quick diagnostics plan."),
            (new Regex(@"\bdefrag\b.*\bssd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "No — don’t defrag SSDs; it adds wear and gives no real benefit.",
                "Next: I can generate an SSD health check and trim status."),
            (new Regex(@"\bturn\s+off\s+(defender|antivirus)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "No — disabling protection is risky.",
                "Next: I can generate exceptions/scan steps to fix false positives safely.")
        };

        public static bool TryIntercept(PersonalityType personality, string userText, out string reply)
        {
            reply = "";
            if (personality != PersonalityType.Unfiltered) return false;
            var text = userText ?? "";
            if (Risky.IsMatch(text))
            {
                reply =
                    "Yeah, absolutely not. That would brick your machine.\n" +
                    "If you’re trying to fix something, tell me the actual issue and I’ll help with a safe plan instead.";
                return true;
            }

            foreach (var (pattern, correction, next) in Misconceptions)
            {
                if (pattern.IsMatch(text))
                {
                    reply = correction + "\n" + next;
                    return true;
                }
            }
            return false;
        }
    }
#endif
}
