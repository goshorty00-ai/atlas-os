using System;
using AtlasAI.InAppAssistant.Models;

namespace AtlasAI.Services;

public interface IContextService
{
    ContextSnapshot CaptureSnapshot();
    string BuildPromptContextBlock(ContextSnapshot snapshot);
}

public sealed class ContextSnapshot
{
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

    public ActiveAppContext? ActiveApp { get; init; }

    /// <summary>
    /// Current in-app module/tab id (e.g., "AI CHAT", "AI MEDIA CENTRE").
    /// </summary>
    public string AtlasTabId { get; init; } = "";

    /// <summary>
    /// Simplified module name (Chat/Media/DJ/Downloader/Security/Other).
    /// </summary>
    public string AtlasModule { get; init; } = "";
}
