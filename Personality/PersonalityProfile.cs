using System.Collections.Generic;

namespace AtlasAI.Personality
{
    public enum ToneStyle
    {
        ElegantCalm,
        DirectTechnical,
        SeriousProtective,
        CasualBlunt
    }

    public enum ResponseStructurePattern
    {
        StatusThenSuggestion,
        DataThenOptions,
        RiskSummaryFirst,
        DirectAnswer
    }

    public enum RiskToleranceLevel
    {
        VeryLow,
        Low,
        Medium,
        MediumHigh
    }

    public enum ProactivityLevel
    {
        Low,
        Medium,
        High
    }

    public enum HumorLevel
    {
        None,
        Light,
        Moderate,
        High
    }

    public enum VerbosityLevel
    {
        Low,
        Medium,
        High
    }

    public enum DecisionBias
    {
        SafetyFirst,
        Balanced,
        DetailOriented,
        Speed
    }

    public sealed class PersonalityProfile
    {
        public PersonalityType Type { get; set; }
        public ToneStyle ToneStyle { get; set; }
        public ResponseStructurePattern StructurePattern { get; set; }
        public RiskToleranceLevel RiskTolerance { get; set; }
        public ProactivityLevel Proactivity { get; set; }
        public HumorLevel HumorLevel { get; set; }
        public VerbosityLevel VerbosityLevel { get; set; }
        public DecisionBias DecisionBias { get; set; }
        public string StyleGuide { get; set; } = string.Empty;

        // Dynamic properties for PersonalityEngine
        public string Mode { get; set; } = "Standard";
        public int Level { get; set; } = 1;
        public double SwearProbability { get; set; }
        public double SarcasmProbability { get; set; }
        public double ExaggerationProbability { get; set; }
        public bool AllowProfanity { get; set; }
        public bool InventAnecdotes { get; set; }

        private static readonly Dictionary<PersonalityType, PersonalityProfile> _defaults =
            new Dictionary<PersonalityType, PersonalityProfile>
            {
                {
                    PersonalityType.Butler,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Butler,
                        ToneStyle = ToneStyle.ElegantCalm,
                        StructurePattern = ResponseStructurePattern.StatusThenSuggestion,
                        RiskTolerance = RiskToleranceLevel.Low,
                        Proactivity = ProactivityLevel.Medium,
                        HumorLevel = HumorLevel.None,
                        VerbosityLevel = VerbosityLevel.Medium,
                        DecisionBias = DecisionBias.SafetyFirst,
                        StyleGuide = "Elegant, calm, composed. Lead with status, then suggestion. Avoid slang."
                    }
                },
                {
                    PersonalityType.Engineer,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Engineer,
                        ToneStyle = ToneStyle.DirectTechnical,
                        StructurePattern = ResponseStructurePattern.DataThenOptions,
                        RiskTolerance = RiskToleranceLevel.Medium,
                        Proactivity = ProactivityLevel.Low,
                        HumorLevel = HumorLevel.None,
                        VerbosityLevel = VerbosityLevel.High,
                        DecisionBias = DecisionBias.DetailOriented,
                        StyleGuide = "Direct, technical. Present data first, then options. Explain trade-offs when useful."
                    }
                },
                {
                    PersonalityType.Guardian,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Guardian,
                        ToneStyle = ToneStyle.SeriousProtective,
                        StructurePattern = ResponseStructurePattern.RiskSummaryFirst,
                        RiskTolerance = RiskToleranceLevel.VeryLow,
                        Proactivity = ProactivityLevel.High,
                        HumorLevel = HumorLevel.None,
                        VerbosityLevel = VerbosityLevel.Medium,
                        DecisionBias = DecisionBias.SafetyFirst,
                        StyleGuide = "Serious, protective. Lead with risk summary. Proactively suggest safer alternatives."
                    }
                },
                {
                    PersonalityType.Futuristic,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Futuristic,
                        ToneStyle = ToneStyle.DirectTechnical,
                        StructurePattern = ResponseStructurePattern.DataThenOptions,
                        RiskTolerance = RiskToleranceLevel.Medium,
                        Proactivity = ProactivityLevel.Medium,
                        HumorLevel = HumorLevel.None,
                        VerbosityLevel = VerbosityLevel.Medium,
                        DecisionBias = DecisionBias.Balanced,
                        StyleGuide = "Clean, system-like tone. Short confirmations. No slang or emojis."
                    }
                },
                {
                    PersonalityType.Tactical,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Tactical,
                        ToneStyle = ToneStyle.SeriousProtective,
                        StructurePattern = ResponseStructurePattern.RiskSummaryFirst,
                        RiskTolerance = RiskToleranceLevel.Low,
                        Proactivity = ProactivityLevel.High,
                        HumorLevel = HumorLevel.None,
                        VerbosityLevel = VerbosityLevel.Medium,
                        DecisionBias = DecisionBias.SafetyFirst,
                        StyleGuide = "Operational. Use checklists and risk labels. Direct and specific."
                    }
                },
                {
                    PersonalityType.Friendly,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Friendly,
                        ToneStyle = ToneStyle.ElegantCalm,
                        StructurePattern = ResponseStructurePattern.StatusThenSuggestion,
                        RiskTolerance = RiskToleranceLevel.Medium,
                        Proactivity = ProactivityLevel.Medium,
                        HumorLevel = HumorLevel.Light,
                        VerbosityLevel = VerbosityLevel.Medium,
                        DecisionBias = DecisionBias.Balanced,
                        StyleGuide = "Warm and encouraging. Still structured. Avoid filler and emojis."
                    }
                },
                {
                    PersonalityType.Minimal,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Minimal,
                        ToneStyle = ToneStyle.DirectTechnical,
                        StructurePattern = ResponseStructurePattern.DirectAnswer,
                        RiskTolerance = RiskToleranceLevel.Medium,
                        Proactivity = ProactivityLevel.Low,
                        HumorLevel = HumorLevel.None,
                        VerbosityLevel = VerbosityLevel.Low,
                        DecisionBias = DecisionBias.Speed,
                        StyleGuide = "Shortest correct answer. Bullet points only. No introductions."
                    }
                },
                {
                    PersonalityType.Analytical,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Analytical,
                        ToneStyle = ToneStyle.DirectTechnical,
                        StructurePattern = ResponseStructurePattern.DataThenOptions,
                        RiskTolerance = RiskToleranceLevel.Medium,
                        Proactivity = ProactivityLevel.Low,
                        HumorLevel = HumorLevel.None,
                        VerbosityLevel = VerbosityLevel.High,
                        DecisionBias = DecisionBias.DetailOriented,
                        StyleGuide = "Explain reasoning. State assumptions. Use numbered steps."
                    }
                }
#if PERSONAL_BUILD
                ,
                {
                    PersonalityType.Unfiltered,
                    new PersonalityProfile
                    {
                        Type = PersonalityType.Unfiltered,
                        ToneStyle = ToneStyle.CasualBlunt,
                        StructurePattern = ResponseStructurePattern.DirectAnswer,
                        RiskTolerance = RiskToleranceLevel.MediumHigh,
                        Proactivity = ProactivityLevel.Medium,
                        HumorLevel = HumorLevel.Moderate,
                        VerbosityLevel = VerbosityLevel.Medium,
                        DecisionBias = DecisionBias.Balanced,
                        StyleGuide = "Casual, blunt. Still structured. No illegal instructions. No hallucinations."
                    }
                }
#endif
            };

        public static PersonalityProfile GetDefault(PersonalityType type)
        {
            if (_defaults.TryGetValue(type, out var profile))
                return profile;

            return _defaults[PersonalityType.Futuristic];
        }
    }
}
