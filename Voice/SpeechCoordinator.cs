using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Voice
{
    /// <summary>
    /// SINGLE SOURCE OF TRUTH for all speech output in Atlas AI.
    /// 
    /// STEP 29 STABILIZATION: Enforces single-speaker rule with debug logging.
    /// 
    /// RULE: Exactly ONE component is allowed to speak per interaction.
    /// 
    /// Speech Categories:
    /// - SystemVoice: Startup, errors, wake acknowledgement ("Yes?") ONLY
    /// - ConversationVoice: ALL AI responses, greetings, everything else
    /// 
    /// MANDATORY RULES:
    /// - NO OVERLAP: If one voice is speaking, the other is MUTED
    /// - NO FALLBACK: Never speak fallback phrases when LLM response is available
    /// - NO DUPLICATE: Same text cannot be spoken twice in same turn
    /// - SYNC GUARANTEE: Spoken text MUST match chat text exactly
    /// </summary>
    public class SpeechCoordinator
    {
        private static SpeechCoordinator? _instance;
        private static readonly object _lock = new();

        public static SpeechCoordinator Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new SpeechCoordinator();
                    }
                }
                return _instance;
            }
        }

        // === State ===
        private VoiceManager? _voiceManager;
        private SpeechCategory _currentSpeaker = SpeechCategory.None;
        private Guid _currentTurnId = Guid.Empty;
        private readonly object _speakerLock = new();
        private CancellationTokenSource? _currentSpeechCts;
        private DateTime _lastSpeechEnd = DateTime.MinValue;
        private const int MinSpeechGapMs = 300; // Minimum gap between speeches

        // === Events ===
        public event EventHandler<SpeechCategory>? SpeechStarted;
        public event EventHandler<SpeechCategory>? SpeechEnded;
        public event EventHandler<string>? SpeechRejected;

        // === Properties ===
        public bool IsSpeaking => _currentSpeaker != SpeechCategory.None;
        public SpeechCategory CurrentSpeaker => _currentSpeaker;
        public Guid CurrentTurnId => _currentTurnId;

        private SpeechCoordinator()
        {
            Debug.WriteLine("[SpeechCoordinator] Initialized - Single speaker enforcement active");
        }

        /// <summary>
        /// Set the VoiceManager instance to use for TTS
        /// </summary>
        public void SetVoiceManager(VoiceManager voiceManager)
        {
            _voiceManager = voiceManager;
            if (_voiceManager != null)
            {
                _voiceManager.SpeechEnded += OnVoiceManagerSpeechEnded;
            }
        }

        private void OnVoiceManagerSpeechEnded(object? sender, EventArgs e)
        {
            lock (_speakerLock)
            {
                var category = _currentSpeaker;
                _currentSpeaker = SpeechCategory.None;
                _currentTurnId = Guid.Empty;
                _lastSpeechEnd = DateTime.Now;
                Debug.WriteLine($"[SpeechCoordinator] Speech ended - category: {category}");
                SpeechEnded?.Invoke(this, category);
            }
        }

        /// <summary>
        /// Request to speak - returns true if granted, false if rejected.
        /// ENFORCES SINGLE SPEAKER RULE with stabilization logging.
        /// </summary>
        public bool RequestSpeech(SpeechCategory category, Guid turnId, string reason)
        {
            lock (_speakerLock)
            {
                // Check if someone is already speaking
                if (_currentSpeaker != SpeechCategory.None)
                {
                    // Same turn ID is allowed (continuation)
                    if (_currentTurnId == turnId)
                    {
                        Debug.WriteLine($"[SpeechCoordinator] ✅ Continuation allowed for turn {turnId.ToString("N")[..8]}");
                        StabilizationLogger.LogSpeechRequest(category.ToString(), turnId, reason, granted: true);
                        return true;
                    }

                    // Different speaker - REJECT
                    var rejectReason = $"Rejected {category} speech - {_currentSpeaker} is already speaking (turn: {_currentTurnId.ToString("N")[..8]})";
                    Debug.WriteLine($"[SpeechCoordinator] ❌ {rejectReason}");
                    StabilizationLogger.LogSpeechRequest(category.ToString(), turnId, reason, granted: false);
                    StabilizationLogger.LogVoiceConflict(_currentSpeaker.ToString(), category.ToString());
                    SpeechRejected?.Invoke(this, rejectReason);
                    return false;
                }

                // Check minimum gap between speeches
                var timeSinceLastSpeech = DateTime.Now - _lastSpeechEnd;
                if (timeSinceLastSpeech.TotalMilliseconds < MinSpeechGapMs)
                {
                    var waitMs = MinSpeechGapMs - (int)timeSinceLastSpeech.TotalMilliseconds;
                    Debug.WriteLine($"[SpeechCoordinator] ⏳ Waiting {waitMs}ms for speech gap");
                    Thread.Sleep(waitMs);
                }

                // Grant speech
                _currentSpeaker = category;
                _currentTurnId = turnId;
                Debug.WriteLine($"[SpeechCoordinator] ✅ Speech granted to {category} (turn: {turnId.ToString("N")[..8]}, reason: {reason})");
                StabilizationLogger.LogSpeechRequest(category.ToString(), turnId, reason, granted: true);
                StabilizationLogger.LogVoicePipelineActive(category.ToString(), active: true);
                SpeechStarted?.Invoke(this, category);
                return true;
            }
        }

        /// <summary>
        /// Release speech lock (call when done speaking)
        /// </summary>
        public void ReleaseSpeech(Guid turnId)
        {
            lock (_speakerLock)
            {
                if (_currentTurnId == turnId)
                {
                    var category = _currentSpeaker;
                    _currentSpeaker = SpeechCategory.None;
                    _currentTurnId = Guid.Empty;
                    _lastSpeechEnd = DateTime.Now;
                    Debug.WriteLine($"[SpeechCoordinator] Released speech for turn {turnId.ToString("N")[..8]}");
                    StabilizationLogger.LogSpeechEnd(category.ToString(), turnId, cancelled: false);
                    StabilizationLogger.LogVoicePipelineActive(category.ToString(), active: false);
                    SpeechEnded?.Invoke(this, category);
                }
            }
        }

        /// <summary>
        /// Cancel current speech and release lock
        /// </summary>
        public void CancelCurrentSpeech()
        {
            lock (_speakerLock)
            {
                if (_currentSpeaker != SpeechCategory.None)
                {
                    Debug.WriteLine($"[SpeechCoordinator] Cancelling {_currentSpeaker} speech");
                    StabilizationLogger.LogSpeechEnd(_currentSpeaker.ToString(), _currentTurnId, cancelled: true);
                    _currentSpeechCts?.Cancel();
                    _voiceManager?.Stop();
                    var category = _currentSpeaker;
                    _currentSpeaker = SpeechCategory.None;
                    _currentTurnId = Guid.Empty;
                    _lastSpeechEnd = DateTime.Now;
                    StabilizationLogger.LogVoicePipelineActive(category.ToString(), active: false);
                }
            }
        }

        /// <summary>
        /// Speak with coordination - SYSTEM VOICE (startup, errors, wake ack)
        /// </summary>
        public async Task<bool> SpeakSystemAsync(string text, string reason)
        {
            var turnId = Guid.NewGuid();
            
            if (!RequestSpeech(SpeechCategory.System, turnId, reason))
            {
                return false;
            }

            try
            {
                if (_voiceManager == null)
                {
                    Debug.WriteLine("[SpeechCoordinator] No VoiceManager - cannot speak");
                    return false;
                }

                var utterance = new AssistantUtterance(text, UtteranceSource.System);
                utterance.SetTurnId(turnId);
                
                await _voiceManager.SpeakAsync(utterance);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpeechCoordinator] System speech error: {ex.Message}");
                return false;
            }
            finally
            {
                ReleaseSpeech(turnId);
            }
        }

        /// <summary>
        /// Speak with coordination - CONVERSATION VOICE (AI responses, greetings, everything else)
        /// </summary>
        public async Task<bool> SpeakConversationAsync(string text, Guid? existingTurnId = null, string? reason = null)
        {
            var turnId = existingTurnId ?? Guid.NewGuid();
            
            if (!RequestSpeech(SpeechCategory.Conversation, turnId, reason ?? "conversation"))
            {
                return false;
            }

            try
            {
                if (_voiceManager == null)
                {
                    Debug.WriteLine("[SpeechCoordinator] No VoiceManager - cannot speak");
                    return false;
                }

                var utterance = new AssistantUtterance(text, UtteranceSource.Conversation);
                utterance.SetTurnId(turnId);
                
                await _voiceManager.SpeakAsync(utterance);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpeechCoordinator] Conversation speech error: {ex.Message}");
                return false;
            }
            finally
            {
                ReleaseSpeech(turnId);
            }
        }

        /// <summary>
        /// Speak wake word acknowledgement - SHORT, IMMEDIATE
        /// Uses System category but with minimal text
        /// </summary>
        public async Task<bool> SpeakWakeAcknowledgementAsync()
        {
            // Wake acknowledgement is ALWAYS just "Yes?" or similar
            // Never a full greeting - that comes from the conversation
            var ack = GetWakeAcknowledgement();
            return await SpeakSystemAsync(ack, "wake_ack");
        }

        private string GetWakeAcknowledgement()
        {
            // Short, immediate acknowledgements only
            var acks = new[] { "Yes?", "Listening.", "Here.", "Ready." };
            return acks[new Random().Next(acks.Length)];
        }
    }

    /// <summary>
    /// Categories of speech - used to enforce single speaker rule
    /// </summary>
    public enum SpeechCategory
    {
        None,
        System,      // Startup, errors, wake acknowledgement
        Conversation // AI responses, greetings, everything else
    }
}
