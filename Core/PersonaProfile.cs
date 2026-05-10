using System;
using System.Collections.Generic;

namespace AtlasAI.Core
{
    /// <summary>
    /// Persona profiles that affect tone and messaging text only.
    /// Does NOT change capabilities or system behavior.
    /// </summary>
    public static class PersonaProfile
    {
        /// <summary>
        /// Get the display name for a persona
        /// </summary>
        public static string GetDisplayName(PersonaType persona) => persona switch
        {
            PersonaType.Jarvis => "Jarvis",
            PersonaType.Ultron => "Ultron",
            PersonaType.Neutral => "Neutral",
            _ => "Neutral"
        };

        /// <summary>
        /// Get the description for a persona
        /// </summary>
        public static string GetDescription(PersonaType persona) => persona switch
        {
            PersonaType.Jarvis => "Calm, professional, helpful",
            PersonaType.Ultron => "Sharp, direct, efficient",
            PersonaType.Neutral => "Balanced, straightforward",
            _ => "Balanced, straightforward"
        };

        /// <summary>
        /// Get the status label text (shown in UI)
        /// </summary>
        public static string GetStatusLabel(PersonaType persona, bool isOnline) => persona switch
        {
            PersonaType.Jarvis => isOnline ? "AT YOUR SERVICE" : "STANDBY",
            PersonaType.Ultron => isOnline ? "OPERATIONAL" : "DORMANT",
            PersonaType.Neutral => isOnline ? "ONLINE" : "OFFLINE",
            _ => isOnline ? "ONLINE" : "OFFLINE"
        };

        /// <summary>
        /// Get confidence statement phrasing based on persona and result type
        /// </summary>
        public static string GetConfidenceStatement(PersonaType persona, ConfidenceContext context)
        {
            return (persona, context) switch
            {
                // Success - no issues
                (PersonaType.Jarvis, ConfidenceContext.Success) => "Everything appears to be in order, sir.",
                (PersonaType.Ultron, ConfidenceContext.Success) => "Systems nominal.",
                (PersonaType.Neutral, ConfidenceContext.Success) => "Here's what I found.",

                // Success - with issues
                (PersonaType.Jarvis, ConfidenceContext.SuccessWithIssues) => "I've identified a few items that may warrant your attention.",
                (PersonaType.Ultron, ConfidenceContext.SuccessWithIssues) => "Anomalies detected. Review recommended.",
                (PersonaType.Neutral, ConfidenceContext.SuccessWithIssues) => "I've identified a few things worth your attention.",

                // Success - minor issues
                (PersonaType.Jarvis, ConfidenceContext.SuccessMinorIssues) => "A few minor items to note, nothing urgent.",
                (PersonaType.Ultron, ConfidenceContext.SuccessMinorIssues) => "Minor deviations. Non-critical.",
                (PersonaType.Neutral, ConfidenceContext.SuccessMinorIssues) => "A few items could use a look.",

                // Failure
                (PersonaType.Jarvis, ConfidenceContext.Failure) => "I'm afraid I couldn't complete that request.",
                (PersonaType.Ultron, ConfidenceContext.Failure) => "Operation failed.",
                (PersonaType.Neutral, ConfidenceContext.Failure) => "I couldn't complete that check.",

                // Repeated check - no change
                (PersonaType.Jarvis, ConfidenceContext.RepeatedNoChange) => "The situation remains unchanged, sir.",
                (PersonaType.Ultron, ConfidenceContext.RepeatedNoChange) => "No delta detected.",
                (PersonaType.Neutral, ConfidenceContext.RepeatedNoChange) => "The situation hasn't changed.",

                // Repeated check - still good
                (PersonaType.Jarvis, ConfidenceContext.RepeatedStillGood) => "All systems continue to operate normally.",
                (PersonaType.Ultron, ConfidenceContext.RepeatedStillGood) => "Status unchanged. Optimal.",
                (PersonaType.Neutral, ConfidenceContext.RepeatedStillGood) => "Still looking good.",

                // Macro-specific statements
                (PersonaType.Jarvis, ConfidenceContext.SystemOverview) => "Your system is operating within normal parameters.",
                (PersonaType.Ultron, ConfidenceContext.SystemOverview) => "System status: operational.",
                (PersonaType.Neutral, ConfidenceContext.SystemOverview) => "Your system is operating normally.",

                (PersonaType.Jarvis, ConfidenceContext.StartupInventory) => "Here's what initializes with Windows, sir.",
                (PersonaType.Ultron, ConfidenceContext.StartupInventory) => "Startup sequence inventory.",
                (PersonaType.Neutral, ConfidenceContext.StartupInventory) => "Here's what starts with Windows.",

                (PersonaType.Jarvis, ConfidenceContext.NetworkSnapshot) => "Your network connection appears healthy.",
                (PersonaType.Ultron, ConfidenceContext.NetworkSnapshot) => "Network: connected. Latency acceptable.",
                (PersonaType.Neutral, ConfidenceContext.NetworkSnapshot) => "Your network connection looks healthy.",

                (PersonaType.Jarvis, ConfidenceContext.DiskHealth) => "Your storage drives are in excellent condition.",
                (PersonaType.Ultron, ConfidenceContext.DiskHealth) => "Storage subsystem: nominal.",
                (PersonaType.Neutral, ConfidenceContext.DiskHealth) => "Your drives are in good shape.",

                (PersonaType.Jarvis, ConfidenceContext.InstalledApps) => "Here's your complete software inventory.",
                (PersonaType.Ultron, ConfidenceContext.InstalledApps) => "Software manifest compiled.",
                (PersonaType.Neutral, ConfidenceContext.InstalledApps) => "Here's your software inventory.",

                (PersonaType.Jarvis, ConfidenceContext.SecurityStatus) => "Your security measures are active and functioning.",
                (PersonaType.Ultron, ConfidenceContext.SecurityStatus) => "Security protocols: engaged.",
                (PersonaType.Neutral, ConfidenceContext.SecurityStatus) => "Your security settings are active.",

                (PersonaType.Jarvis, ConfidenceContext.EventViewer) => "Here are the recent system events for your review.",
                (PersonaType.Ultron, ConfidenceContext.EventViewer) => "Event log extracted.",
                (PersonaType.Neutral, ConfidenceContext.EventViewer) => "Here are the recent system events.",

                (PersonaType.Jarvis, ConfidenceContext.PerformanceDiagnostics) => "I've completed the performance analysis.",
                (PersonaType.Ultron, ConfidenceContext.PerformanceDiagnostics) => "Performance metrics analyzed.",
                (PersonaType.Neutral, ConfidenceContext.PerformanceDiagnostics) => "I've analyzed your system performance.",

                _ => "Here's what I found."
            };
        }

        /// <summary>
        /// Get greeting text based on persona
        /// </summary>
        public static string GetGreeting(PersonaType persona, string? userName = null)
        {
            var name = string.IsNullOrWhiteSpace(userName) ? "" : $", {userName}";
            return persona switch
            {
                PersonaType.Jarvis => $"Good to see you{name}. How may I assist?",
                PersonaType.Ultron => $"Ready{name}.",
                PersonaType.Neutral => $"Hello{name}. What can I help with?",
                _ => $"Hello{name}. What can I help with?"
            };
        }

        /// <summary>
        /// Get thinking/processing text
        /// </summary>
        public static string GetThinkingText(PersonaType persona) => persona switch
        {
            PersonaType.Jarvis => "One moment, please...",
            PersonaType.Ultron => "Processing...",
            PersonaType.Neutral => "Thinking...",
            _ => "Thinking..."
        };

        /// <summary>
        /// Get completion text
        /// </summary>
        public static string GetCompletionText(PersonaType persona) => persona switch
        {
            PersonaType.Jarvis => "Task complete.",
            PersonaType.Ultron => "Done.",
            PersonaType.Neutral => "Complete.",
            _ => "Complete."
        };
    }

    /// <summary>
    /// Context for confidence statement generation
    /// </summary>
    public enum ConfidenceContext
    {
        Success,
        SuccessWithIssues,
        SuccessMinorIssues,
        Failure,
        RepeatedNoChange,
        RepeatedStillGood,
        // Macro-specific
        SystemOverview,
        StartupInventory,
        NetworkSnapshot,
        DiskHealth,
        InstalledApps,
        SecurityStatus,
        EventViewer,
        PerformanceDiagnostics
    }
}
