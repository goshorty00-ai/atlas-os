using System;

namespace AtlasAI.Views.AiChat.Services;

public class PersonalityProfile
{
    public string Mode { get; set; } = "Butler";          // e.g., ChaosTesting
    public int Level { get; set; } = 2;                    // 1–5
    public double SwearProbability { get; set; }
    public double SarcasmProbability { get; set; }
    public double ExaggerationProbability { get; set; }
    public bool AllowProfanity { get; set; }
    public bool InventAnecdotes { get; set; }
}