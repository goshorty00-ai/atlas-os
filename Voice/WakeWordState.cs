using System;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Represents the current state of the wake word detection system.
    /// Used to track the lifecycle of wake word activation and prevent double-triggers.
    /// </summary>
    public enum WakeWordState
    {
        /// <summary>
        /// Wake word detection is not active. System is not listening.
        /// </summary>
        Idle,

        /// <summary>
        /// Actively listening for the wake word. Normal operational state.
        /// </summary>
        Listening,

        /// <summary>
        /// Wake word has been detected and is being processed.
        /// Brief transitional state before entering cooldown.
        /// </summary>
        Triggered,

        /// <summary>
        /// Cooldown/debounce period after wake word detection.
        /// Prevents double-triggers within the debounce window (1.5-2s).
        /// </summary>
        Cooldown
    }
}
