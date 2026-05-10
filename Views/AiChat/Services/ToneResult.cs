namespace AtlasAI.Views.AiChat.Services;

public sealed class ToneResult
{
    public bool ContainsProfanity { get; init; }
    public bool IsFrustrated { get; init; }
    public bool IsBlunt { get; init; }
    public bool IsAllCaps { get; init; }
    public bool HasSarcasmMarkers { get; init; }
    public bool IsCalm { get; init; }
}