using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasAI.Personality
{
#if PERSONAL_BUILD
    internal static class UnfilteredTemplates
    {
        private static readonly Random Rng = new(unchecked(Environment.TickCount));
        private const int RecentMax = 10;

        private static readonly Queue<string> _recentGreet = new();
        private static readonly Queue<string> _recentDontKnow = new();
        private static readonly Queue<string> _recentClarify = new();

        private static readonly string[] Greetings =
        {
            "Yo. What’s the move?",
            "Alright, I’m here. What’s up?",
            "Hey — let’s get this rolling.",
            "Sup. Ready to ship.",
            "Okay, I’m in. What do you need?",
            "Let’s go. What’s the task?",
            "Back at it. Say the word."
        };

        private static readonly string[] DontKnow =
        {
            "No clue on that one. Give me more context.",
            "I don’t know yet — what exactly are you trying to do?",
            "Not sure. Tell me the setup and we’ll figure it out.",
            "I can’t tell from that. Be specific.",
            "Dunno — show me the details and I’ll dig in.",
            "I’m not gonna guess. What’s the target here?"
        };

        private static readonly string[] Clarify =
        {
            "Need specifics — path, app, or exact command?",
            "What exactly do you want changed?",
            "Give me the file, tool, and desired result.",
            "Be precise — which folder, what action?",
            "Tell me where and how you want it done.",
            "What’s the exact goal? Keep it tight."
        };

        public static string PickGreeting()
        {
            return PickAvoidRecent(Greetings, _recentGreet);
        }

        public static string PickDontKnow()
        {
            return PickAvoidRecent(DontKnow, _recentDontKnow);
        }

        public static string PickClarify()
        {
            return PickAvoidRecent(Clarify, _recentClarify);
        }

        private static string PickAvoidRecent(IReadOnlyList<string> pool, Queue<string> recent)
        {
            if (pool == null || pool.Count == 0) return "";
            var candidates = pool.Where(s => !recent.Contains(s)).ToArray();
            var chosen = candidates.Length > 0 ? candidates[Rng.Next(candidates.Length)] : LeastRecent(pool, recent);
            Remember(recent, chosen);
            return chosen;
        }

        private static string LeastRecent(IReadOnlyList<string> pool, Queue<string> recent)
        {
            foreach (var s in recent)
            {
                if (pool.Contains(s)) return s;
            }
            return pool[Rng.Next(pool.Count)];
        }

        private static void Remember(Queue<string> q, string s)
        {
            q.Enqueue(s);
            while (q.Count > RecentMax) q.Dequeue();
        }
    }
#endif
}
