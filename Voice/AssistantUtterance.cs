// Voice/AssistantUtterance.cs
// Single source of truth for assistant speech output
// All TTS must flow through this object to prevent duplicate speech

using System;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Source of the assistant utterance - used for logging and debugging
    /// </summary>
    public enum UtteranceSource
    {
        /// <summary>Response from LLM (Claude/OpenAI)</summary>
        LLM,
        
        /// <summary>Template response from ResponseLibrary</summary>
        Template,
        
        /// <summary>Macro execution result</summary>
        Macro,
        
        /// <summary>Agent action result</summary>
        Action,
        
        /// <summary>Web search result</summary>
        Web,
        
        /// <summary>System message (welcome, error, etc.)</summary>
        System,
        
        /// <summary>Direct UI interaction (voice preview, etc.)</summary>
        UI,
        
        /// <summary>Conversation response (coordinated through SpeechCoordinator)</summary>
        Conversation
    }

    /// <summary>
    /// Represents a single assistant utterance that should be spoken and/or displayed.
    /// This is the ONLY object that should flow into TTS - enforces single source of truth.
    /// </summary>
    public class AssistantUtterance
    {
        /// <summary>
        /// Unique identifier for this turn/utterance.
        /// Used to prevent duplicate speech for the same turn.
        /// </summary>
        public Guid TurnId { get; private set; }
        
        /// <summary>
        /// Set the turn ID (used by SpeechCoordinator for tracking)
        /// </summary>
        public void SetTurnId(Guid turnId)
        {
            TurnId = turnId;
        }
        
        /// <summary>
        /// Text to display in chat UI.
        /// </summary>
        public string Text { get; }
        
        /// <summary>
        /// Text to speak via TTS. Defaults to Text if not specified.
        /// Use this when spoken text should differ from displayed text
        /// (e.g., shorter spoken version, no markdown, etc.)
        /// </summary>
        public string SpeechText { get; }
        
        /// <summary>
        /// Source of this utterance (LLM, Template, Macro, etc.)
        /// </summary>
        public UtteranceSource Source { get; }
        
        /// <summary>
        /// Intent of this utterance (for response styling)
        /// </summary>
        public string Intent { get; }
        
        /// <summary>
        /// When this utterance was created
        /// </summary>
        public DateTime Timestamp { get; }
        
        /// <summary>
        /// If true, only display text - do not speak.
        /// Used for UI-only labels, long responses, etc.
        /// </summary>
        public bool SuppressSpeech { get; }
        
        /// <summary>
        /// Response type for voice selection
        /// </summary>
        public ResponseType ResponseType { get; }

        /// <summary>
        /// Create a new assistant utterance
        /// </summary>
        /// <param name="text">Text to display (and speak if speechText is null)</param>
        /// <param name="source">Source of the utterance</param>
        /// <param name="intent">Intent for response styling</param>
        /// <param name="speechText">Optional different text for TTS</param>
        /// <param name="suppressSpeech">If true, don't speak this utterance</param>
        /// <param name="responseType">Response type for voice selection</param>
        /// <param name="turnId">Optional turn ID (auto-generated if not provided)</param>
        public AssistantUtterance(
            string text,
            UtteranceSource source = UtteranceSource.LLM,
            string intent = "Acknowledged",
            string? speechText = null,
            bool suppressSpeech = false,
            ResponseType responseType = ResponseType.Normal,
            Guid? turnId = null)
        {
            TurnId = turnId ?? Guid.NewGuid();
            Text = text ?? "";
            SpeechText = speechText ?? text ?? "";
            Source = source;
            Intent = intent;
            SuppressSpeech = suppressSpeech;
            ResponseType = responseType;
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// Create a simple utterance for quick use
        /// </summary>
        public static AssistantUtterance Simple(string text, UtteranceSource source = UtteranceSource.System)
        {
            return new AssistantUtterance(text, source);
        }
        
        /// <summary>
        /// Create a display-only utterance (no speech)
        /// </summary>
        public static AssistantUtterance DisplayOnly(string text, UtteranceSource source = UtteranceSource.LLM)
        {
            return new AssistantUtterance(text, source, suppressSpeech: true);
        }
        
        /// <summary>
        /// Create an utterance with different display and speech text
        /// </summary>
        public static AssistantUtterance WithSpeech(string displayText, string speechText, UtteranceSource source = UtteranceSource.LLM)
        {
            return new AssistantUtterance(displayText, source, speechText: speechText);
        }

        public override string ToString()
        {
            return $"[{TurnId.ToString("N")[..8]}] {Source}: \"{(Text.Length > 50 ? Text[..50] + "..." : Text)}\"";
        }
    }
}
