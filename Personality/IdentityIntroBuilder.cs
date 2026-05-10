using System;
using System.Collections.Generic;
using AtlasAI.Personality;

namespace AtlasAI.Personality
{
    internal static class IdentityIntroBuilder
    {
        private static readonly Random Rng = new(unchecked(Environment.TickCount));

        public static string Build(PersonalityType personality)
        {
            var variants = personality switch
            {
                PersonalityType.Butler => ButlerVariants,
#if PERSONAL_BUILD
                PersonalityType.Unfiltered => UnfilteredVariants,
#endif
                _ => ButlerVariants
            };
            if (variants.Length == 0) return "";
            var idx = Rng.Next(variants.Length);
            return variants[idx];
        }

        private static readonly string[] ButlerVariants =
        {
            "I live inside this machine as Atlas—patient, watching, and ready. I remember where your work lives, open the tools you need, move windows into place, and keep risky changes on a tight leash. Tell me what you’re trying to get done and I’ll handle the system side so you can stay in the work.",
            "Think of me as the quiet operator behind the glass. I open and focus apps, keep your workspace in order, surface what’s slowing the machine down, and make sure anything sharp asks for permission first. You describe the outcome; I turn it into concrete steps on this PC.",
            "I’m Atlas, wired into this Windows session. I can wake apps, arrange your screens, dig through folders, and run safety-checked actions when you’re ready. I keep logs of what I’m about to do and wait for your confirmation before anything irreversible.",
            "I sit between you and the operating system. I can recall common layouts, find and tidy files, perform guarded checks, and spell out what will happen before anything executes. You point at the destination—I chart the route and keep it safe.",
            "I’m an on-device coordinator: part concierge, part safety net. I open what you need, keep track of your workflows, nudge you away from dangerous commands, and make the system feel less like a tangle of windows and more like a single instrument you can play.",
            "I stay awake inside this desktop, tracking context across your work. I can bring the right apps forward, organise cluttered folders, outline safe plans for heavier maintenance, and remember what layout feels like “home” for you."
        };

#if PERSONAL_BUILD
        private static readonly string[] UnfilteredVariants =
        {
            "I live inside this machine as Atlas, always awake, watching the noise so you don’t have to. I can haul apps open, reshuffle windows, dig through your files, and slam the brakes on anything that looks like it might brick the system. You tell me the goal; I’ll do the messy part.",
            "Think of me as the person in the server room who actually knows which cable does what. I move apps, juggle files, and run checks without drama, and I’ll call out anything risky before it gets near your disk. You focus on the work; I’ll handle the wiring.",
            "I’m woven into this desktop—Atlas, not some floating chatbot. I can spin up your tools, clean up the digital junk drawer, and draft plans for heavy fixes while keeping a firm hand on the ‘don’t-break-it’ lever. Say what you want done and I’ll get moving."
        };
#endif
    }
}
