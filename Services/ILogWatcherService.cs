using System;
using System.Collections.Generic;

namespace AtlasAI.Services;

public interface ILogWatcherService
{
    void Initialize();
    IReadOnlyList<string> GetRecentLines();
    DiagnosticsNarration GetLastNarration();
    void ReportException(string source, Exception ex);
    void ReportApiFailure(string subsystem, int statusCode, string details);
}

public sealed class DiagnosticsNarration
{
    public DateTime CapturedAtUtc { get; set; } = DateTime.MinValue;
    public string Title { get; set; } = "";
    public string Explanation { get; set; } = "";
    public List<string> Suggestions { get; set; } = new List<string>();
    public string RawEvidence { get; set; } = "";

    public bool IsEmpty
    {
        get
        {
            return string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Explanation);
        }
    }
}
