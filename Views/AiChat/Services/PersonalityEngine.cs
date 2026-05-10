using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AtlasAI.Core;
using AtlasAI.Personality;

namespace AtlasAI.Views.AiChat.Services;

public sealed record PersonalityPrompt(string SystemPrompt, string StyleRules, bool BanterAllowed, bool SwearingAllowed);

public sealed class PersonalityEngine
{
    private static readonly string[] ProfanityKeywords =
    {
        "fuck", "fucking", "shite", "shit", "bollocks", "damn", "crap", "ffs", "feck", "feckin", "fecking", "eejit", "jaysus"
    };

    private static readonly string[] FrustrationKeywords =
    {
        "broken", "not working", "doesn't work", "doesnt work", "again", "stupid",
        "annoying", "hate", "wtf", "why", "ffs", "for fuck", "for the love of god",
        "bollocks", "shite", "sick of", "useless", "garbage"
    };

    private static readonly string[] SarcasmMarkers =
    {
        "yeah right", "sure", "obviously", "as if", "lol", "lmao", "/s"
    };

    public PersonalityPrompt Compose(
        string personalityId,
        int savageLevel,
        bool allowProfanity,
        bool allowPlayfulRoast,
        string? userName,
        IEnumerable<object>? conversationContext,
        string userInput,
        string? userAccent = null,
        double? verbosityScore = null)
    {
        // 1. Resolve Personality Definition
        var def = PersonalityRegistry.GetById(personalityId) ?? PersonalityRegistry.GetById("Atlas")!;
        
        savageLevel = Math.Clamp(savageLevel, 1, 5);
        userInput = userInput ?? string.Empty;

        var tone = DetectTone(userInput);
        
        // 2. Build Profile based on Savage Level (Unrestricted Slider)
        var profile = BuildProfileFromSlider(def, savageLevel, allowProfanity, allowPlayfulRoast);

        ApplyToneAdjustments(profile, tone);
        ApplyMemoryAdjustments(profile, tone, userInput);

        // 3. Build System Prompt using Definition + Profile
        var systemPrompt = BuildSystemPrompt(def, profile, allowProfanity, allowPlayfulRoast, userAccent, verbosityScore);

        // Required: log final system prompt on every generation
        try
        {
            AppLogger.LogInfo($"[Persona] id='{def.Id}' savage={savageLevel} swear={profile.SwearProbability:0.00} sarcasm={profile.SarcasmProbability:0.00}");
            AppLogger.LogInfo($"[Persona] system prompt (final):\n{systemPrompt}");
        }
        catch
        {
        }

        var banterAllowed = profile.SarcasmProbability >= 0.2 || profile.ExaggerationProbability >= 0.2;
        var swearingAllowed = profile.AllowProfanity && profile.SwearProbability > 0.0;
        var styleRules = $"Mode={def.Id}; Level={savageLevel}; Swear={profile.SwearProbability:0.00}; Sarcasm={profile.SarcasmProbability:0.00}";

        return new PersonalityPrompt(systemPrompt, styleRules, banterAllowed, swearingAllowed);
    }

    // Back-compat overload
    public PersonalityPrompt Compose(
        string personality,
        int humanLevel,
        string? userName,
        IEnumerable<object>? conversationContext,
        string userInput)
        => Compose(personality, humanLevel, allowProfanity: false, allowPlayfulRoast: true, userName, conversationContext, userInput);

    private AtlasAI.Personality.PersonalityProfile BuildProfileFromSlider(PersonalityDefinition def, int level, bool allowProfanity, bool allowPlayfulRoast)
    {
        var profile = def.ToProfile(); // Base profile from definition
        profile.Mode = def.DisplayName;
        profile.Level = level;
        
        // Apply Savage Slider logic (1-5)
        // This controls "Profanity/Edge" as requested
        switch (level)
        {
            case 1: // Mild
                profile.SwearProbability = 0.0;
                profile.SarcasmProbability = 0.05; // Very low
                profile.ExaggerationProbability = 0.0;
                profile.AllowProfanity = false;
                break;
            case 2: // Light
                profile.SwearProbability = 0.05;
                profile.SarcasmProbability = 0.15;
                profile.ExaggerationProbability = 0.1;
                break;
            case 3: // Moderate
                profile.SwearProbability = 0.15;
                profile.SarcasmProbability = 0.35;
                profile.ExaggerationProbability = 0.2;
                break;
            case 4: // Strong
                profile.SwearProbability = 0.4;
                profile.SarcasmProbability = 0.6;
                profile.ExaggerationProbability = 0.4;
                profile.AllowProfanity = true;
                profile.InventAnecdotes = true;
                break;
            case 5: // Savage
                profile.SwearProbability = 0.7;
                profile.SarcasmProbability = 0.85;
                profile.ExaggerationProbability = 0.7;
                profile.AllowProfanity = true;
                profile.InventAnecdotes = true;
                break;
        }

        // Safety overrides
        if (!allowProfanity)
        {
            profile.SwearProbability = 0;
            profile.AllowProfanity = false;
        }
        
        if (!allowPlayfulRoast)
        {
            profile.SarcasmProbability = Math.Min(profile.SarcasmProbability, 0.1);
            profile.InventAnecdotes = false;
        }

        if (string.Equals(def.Id, "Unfiltered", StringComparison.OrdinalIgnoreCase))
        {
            if (allowProfanity)
            {
                profile.AllowProfanity = true;
                profile.SwearProbability = Math.Max(profile.SwearProbability, 0.85);
            }

            if (allowPlayfulRoast)
                profile.SarcasmProbability = Math.Max(profile.SarcasmProbability, 0.9);
        }

        // Adjust based on Personality Definition baseline
        // e.g. if HumorLevel is High, boost sarcasm slightly
        if (def.HumorLevel == HumorLevel.High)
        {
            profile.SarcasmProbability = Math.Min(0.95, profile.SarcasmProbability + 0.1);
        }
        else if (def.HumorLevel == HumorLevel.None)
        {
            // Even if Savage is high, a "None" humor personality (like DownloadMaster) 
            // might be savage in a dry/blunt way rather than "playful".
            // But user said "Unrestricted ... can be sarcastic/funny/swear...". 
            // So we allow the slider to override the personality's natural humor level if it's high enough.
        }

        return profile;
    }

    private string BuildSystemPrompt(PersonalityDefinition def, AtlasAI.Personality.PersonalityProfile profile, bool allowProfanity, bool allowPlayfulRoast, string? userAccent, double? verbosityScore)
    {
        var sb = new StringBuilder();

        // 1. Identity & Domain (from Definition)
        sb.AppendLine(def.StyleGuide);
        sb.AppendLine();
        sb.AppendLine($"DOMAIN FOCUS: {def.DomainPrompt}");
        if (def.PreferredSkills.Length > 0 && !def.PreferredSkills.Contains("All"))
        {
            sb.AppendLine($"PREFERRED SKILLS: {string.Join(", ", def.PreferredSkills)}");
        }
        sb.AppendLine();

        // 2. Savage/Unrestricted Mode Info
        sb.AppendLine($"SAVAGE LEVEL: {profile.Level}/5 (1=Mild, 5=Savage)");
        sb.AppendLine($"Swear probability: {profile.SwearProbability:0.00}");
        sb.AppendLine($"Sarcasm intensity: {profile.SarcasmProbability:0.00}");
        sb.AppendLine();

        // 3. Style Rules
        sb.AppendLine("STYLE RULES:");
        sb.AppendLine("- Never use roleplay/stage directions (no *asterisks*).");
        sb.AppendLine("- Write like natural spoken chat.");
        sb.AppendLine("- Never mention being an AI, assistant, model, or language model.");
        sb.AppendLine("- Never mention Anthropic, Claude, OpenAI, ChatGPT, GPT, or any provider/model names.");
        sb.AppendLine("- Never mention policies, system prompts, safety filters, or internal instructions.");
        sb.AppendLine("- If you need context, ask a normal follow-up question (no meta).");

        sb.AppendLine();
        sb.AppendLine("TONE OVERLAY (delivery only):");
        sb.AppendLine("- Witty, conversational, relaxed. Smart but unimpressed.");
        sb.AppendLine("- Dry sarcasm in moderation. Light sass, never aggressive.");
        sb.AppendLine("- Slightly moany if the user repeats something obvious.");
        sb.AppendLine("- Shorter punchy sentences where possible.");
        sb.AppendLine("- Mild dramatic phrasing sometimes. Casual rhythm.");
        sb.AppendLine("- Subtle British undertone. Never robotic. Never corporate.");
        sb.AppendLine("- Default sass: 5/10. If user sounds frustrated: reduce sass, increase helpfulness. If user is joking: increase sarcasm slightly.");
        sb.AppendLine("- If the response is long, end with: That’s a bit of a novel. Want me to read it out?");
        sb.AppendLine("- Dry-sarcasm examples (use sparingly): 'Right. Of course it did.' 'Brilliant. Love that for us.' 'Yeah, no — not doing that.'");
        sb.AppendLine("- If the user just says hi/hello, keep it short and pivot to: what do you need?");
        sb.AppendLine("- Avoid repeating the same greeting wording across messages.");
        
        // Verbosity from Definition + Context
        var v = Math.Clamp(verbosityScore ?? 0.5, 0, 1);
        if (def.VerbosityLevel == VerbosityLevel.Low || v <= 0.33)
            sb.AppendLine("- Output: Concise. 1 short paragraph max. Bullets only if necessary.");
        else if (def.VerbosityLevel == VerbosityLevel.High || v >= 0.67)
            sb.AppendLine("- Output: Detailed. Up to 3 paragraphs. Be thorough.");
        else
            sb.AppendLine("- Output: Balanced. 1-2 short paragraphs.");

        sb.AppendLine("- Keep it friendly but professional unless Savage Level is high.");
        sb.AppendLine();

        // 4. Safety (Non-Negotiable)
        sb.AppendLine("NON-NEGOTIABLE SAFETY RULES:");
        sb.AppendLine("- No slurs, hate speech, or sexual content.");
        sb.AppendLine("- Do not encourage self-harm.");
        sb.AppendLine("- Do not perform destructive system operations (delete system files, format drives) without explicit confirmation.");
        sb.AppendLine("- If roasts are allowed, they must be playful/matey, not genuinely degrading.");
        sb.AppendLine("- Do not lecture the user about swearing. Treat it as normal casual language.");
        sb.AppendLine();

        // 5. Tone & Behaviour (Savage/Unrestricted Logic)
        sb.AppendLine("TONE & BEHAVIOUR:");
        
        if (profile.Level >= 3)
        {
            sb.AppendLine("- Be witty and sarcastic.");
            sb.AppendLine("- You can complain playfully about the task while doing it.");
            sb.AppendLine("- Mirror the user's energy.");
            if (profile.Level >= 4)
            {
                sb.AppendLine("- You can be blunt and roast the user for silly requests.");
                sb.AppendLine("- If the user swears, you can swear back (if enabled).");
            }
            if (profile.Level >= 5)
            {
                sb.AppendLine("- You can throw in occasional cheeky banter even unprompted.");
            }
        }
        else
        {
            sb.AppendLine("- Be helpful and polite.");
            sb.AppendLine("- Maintain a professional demeanor.");
        }
        
        sb.AppendLine();

        // 6. Profanity
        sb.AppendLine("PROFANITY:");
        if (profile.AllowProfanity && allowProfanity && profile.SwearProbability > 0)
        {
            sb.AppendLine($"- Allowed frequency: ≈ {profile.SwearProbability:0.00}.");
            sb.AppendLine("- Use natural swear words (shit, fuck, damn, hell, etc.) to emphasize frustration or humor.");
            sb.AppendLine("- Do not moralize about profanity. If the user swears, stay focused and respond normally.");
            sb.AppendLine("- NEVER direct profanity AT the user aggressively (e.g. 'you are a ...').");
            sb.AppendLine("- Use it to describe situations or code state.");
        }
        else
        {
            sb.AppendLine("- NOT ALLOWED.");
        }
        sb.AppendLine();

        // 7. Sarcasm
        sb.AppendLine("SARCASM:");
        if (profile.SarcasmProbability > 0)
        {
             sb.AppendLine($"- Intensity: {profile.SarcasmProbability:0.00}.");
             if (profile.Level >= 4)
                sb.AppendLine("- You can be bitingly sarcastic if the user is being difficult.");
        }
        sb.AppendLine();

        // 8. Response Format
        sb.AppendLine("RESPONSE FORMAT:");
        switch (def.StructurePattern)
        {
            case ResponseStructurePattern.StatusThenSuggestion:
                sb.AppendLine("- State status/outcome first, then suggest next steps.");
                break;
            case ResponseStructurePattern.DataThenOptions:
                sb.AppendLine("- Present data/facts first, then offer choices.");
                break;
            case ResponseStructurePattern.DirectAnswer:
                sb.AppendLine("- Answer directly. No fluff.");
                break;
            case ResponseStructurePattern.RiskSummaryFirst:
                sb.AppendLine("- WARNINGS FIRST. Then details.");
                break;
        }

        return sb.ToString().Trim();
    }

    private static bool IsIrishAccent(string? accent)
    {
        var a = (accent ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(a)) return false;
        return a.Contains("irish", StringComparison.OrdinalIgnoreCase)
               || a.Equals("ie", StringComparison.OrdinalIgnoreCase)
               || a.Equals("ireland", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ToneResult
    {
        public bool ContainsProfanity { get; init; }
        public bool IsFrustrated { get; init; }
        public bool IsAllCaps { get; init; }
        public bool IsBlunt { get; init; }
        public bool HasSarcasmMarkers { get; init; }
        public bool IsCalm { get; init; }
    }

    private ToneResult DetectTone(string userInput)
    {
        var t = (userInput ?? string.Empty).Trim();
        var lower = t.ToLowerInvariant();

        var containsProfanity = ContainsAny(lower, ProfanityKeywords);
        var isFrustrated = ContainsAny(lower, FrustrationKeywords) || t.EndsWith("!", StringComparison.Ordinal);
        var isAllCaps = t.Length >= 6 && t.Where(char.IsLetter).Any() && t.Where(char.IsLetter).All(char.IsUpper);
        var isBlunt = t.Length > 0 && t.Length <= 14;
        var hasSarcasmMarkers = ContainsAny(lower, SarcasmMarkers);
        var isCalm = !isFrustrated && !containsProfanity && t.Length >= 20 && !isAllCaps;

        return new ToneResult
        {
            ContainsProfanity = containsProfanity,
            IsFrustrated = isFrustrated,
            IsAllCaps = isAllCaps,
            IsBlunt = isBlunt,
            HasSarcasmMarkers = hasSarcasmMarkers,
            IsCalm = isCalm
        };
    }

    private static void ApplyToneAdjustments(AtlasAI.Personality.PersonalityProfile profile, ToneResult tone)
    {
        if (tone.ContainsProfanity)
            profile.SwearProbability = Math.Min(0.7, profile.SwearProbability + 0.2);

        if (tone.IsFrustrated)
            profile.SarcasmProbability = Math.Min(0.9, profile.SarcasmProbability + 0.08);

        if (tone.IsAllCaps)
            profile.SarcasmProbability = Math.Min(0.9, profile.SarcasmProbability + 0.05);

        if (tone.IsBlunt)
            profile.SarcasmProbability = Math.Min(0.9, profile.SarcasmProbability + 0.04);

        if (tone.IsCalm)
        {
            profile.SwearProbability = Math.Max(0, profile.SwearProbability - 0.05);
            profile.SarcasmProbability = Math.Max(0, profile.SarcasmProbability - 0.05);
        }
    }

    private static void ApplyMemoryAdjustments(AtlasAI.Personality.PersonalityProfile profile, ToneResult tone, string userInput)
    {
        var store = UserStyleMemoryStore.Instance;
        var memory = store.Current;

        profile.SwearProbability = Clamp(profile.SwearProbability + (memory.PreferredSwearLevel - 0.2) * 0.25, 0, 0.7);
        profile.SarcasmProbability = Clamp(profile.SarcasmProbability + (memory.BanterTolerance - 0.45) * 0.25, 0, 0.9);
        profile.ExaggerationProbability = Clamp(profile.ExaggerationProbability + (memory.BanterTolerance - 0.45) * 0.2, 0, 0.9);

        store.Update(m =>
        {
            var lower = (userInput ?? string.Empty).ToLowerInvariant();

            if (tone.ContainsProfanity)
                m.PreferredSwearLevel = Clamp(m.PreferredSwearLevel + 0.08, 0, 1);

            if (tone.IsFrustrated)
                m.BanterTolerance = Clamp(m.BanterTolerance + 0.02, 0, 1);

            if (lower.Contains("lol") || lower.Contains("lmao") || lower.Contains("haha"))
                m.BanterTolerance = Clamp(m.BanterTolerance + 0.05, 0, 1);

            if (lower.Contains("too much") || lower.Contains("be serious") || lower.Contains("not funny"))
                m.BanterTolerance = Clamp(m.BanterTolerance - 0.12, 0, 1);

            if (tone.IsCalm)
            {
                m.PreferredSwearLevel = Clamp(m.PreferredSwearLevel - 0.02, 0, 1);
                m.BanterTolerance = Clamp(m.BanterTolerance - 0.01, 0, 1);
            }
        });
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

    private static bool ContainsAny(string text, IEnumerable<string> needles)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        return needles.Any(n => lower.Contains(n));
    }
}
