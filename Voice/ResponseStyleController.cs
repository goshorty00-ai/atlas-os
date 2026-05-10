using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Conversation.Models;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Legacy enums for VoiceSystemOrchestrator compatibility
    /// </summary>
    public enum ResponseIntent
    {
        Acknowledged,
        ExecutionStart,
        ExecutionComplete,
        OperationFailed,
        SystemError,
        Greeting,
        Farewell
    }

    public enum SystemState
    {
        Idle,
        Listening,
        Processing,
        Speaking,
        Error
    }

    /// <summary>
    /// Session-only greeting state - tracks if user has been introduced this session.
    /// Resets on app restart. Not persisted to disk.
    /// </summary>
    public class SessionGreetingState
    {
        public bool HasIntroducedToUser { get; set; }
        public string? KnownUserName { get; set; }
        public DateTime? FirstIntroductionTime { get; set; }
        public int GreetingCount { get; set; }
        
        public void Reset()
        {
            HasIntroducedToUser = false;
            KnownUserName = null;
            FirstIntroductionTime = null;
            GreetingCount = 0;
            Debug.WriteLine("[SessionGreetingState] Reset");
        }
        
        public void RecordIntroduction(string userName)
        {
            if (!HasIntroducedToUser)
            {
                FirstIntroductionTime = DateTime.Now;
            }
            HasIntroducedToUser = true;
            KnownUserName = userName;
            GreetingCount++;
            Debug.WriteLine($"[SessionGreetingState] Introduction recorded: {userName} (count: {GreetingCount})");
        }
    }

    /// <summary>
    /// Controls response routing between conversational (Jarvis-style) responses
    /// and command/agent execution. Manages session-based user name storage.
    /// </summary>
    public class ResponseStyleController
    {
        private static ResponseStyleController? _instance;
        private static readonly object _lock = new();

        public static ResponseStyleController Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ResponseStyleController();
                    }
                }
                return _instance;
            }
        }

        // Session greeting state - tracks introductions
        private readonly SessionGreetingState _greetingState = new();
        
        // Session-only name storage (not persisted)
        private string? _sessionUserName;
        
        public string? SessionUserName
        {
            get => _sessionUserName;
            private set
            {
                if (_sessionUserName != value)
                {
                    _sessionUserName = value;
                    Debug.WriteLine($"[ResponseStyleController] Session user name set to: {value ?? "(none)"}");
                    UserNameChanged?.Invoke(this, value);
                }
            }
        }
        
        public SessionGreetingState GreetingState => _greetingState;

        public event EventHandler<string?>? UserNameChanged;
        public event EventHandler<ConversationResponseEventArgs>? ConversationResponseGenerated;

        private ResponseStyleController()
        {
            Debug.WriteLine("[ResponseStyleController] Initialized");
        }

        /// <summary>
        /// Legacy method for VoiceSystemOrchestrator compatibility.
        /// Selects a response based on intent and system state.
        /// Now uses depth-aware templates.
        /// </summary>
        public string SelectResponse(ResponseIntent intent, SystemState state, object persona, double confidence)
        {
            var depth = ConversationContext.Instance.CurrentDepth;
            
            return intent switch
            {
                ResponseIntent.Acknowledged => DepthAwareTemplates.GetAcknowledgement(depth, PersonalityProfile.Current.Id),
                ResponseIntent.ExecutionStart => "Processing.",
                ResponseIntent.ExecutionComplete => DepthAwareTemplates.GetCompletion(depth, PersonalityProfile.Current.Id),
                ResponseIntent.OperationFailed => "The operation encountered an issue.",
                ResponseIntent.SystemError => "I encountered an error. Please try again.",
                ResponseIntent.Greeting => DepthAwareTemplates.GetTimeBasedGreeting(depth, PersonalityProfile.Current.Id, SessionUserName),
                ResponseIntent.Farewell => "Goodbye.",
                _ => "Understood."
            };
        }

        /// <summary>
        /// Filters generic chatbot responses like "hey", "ok", "sure" and replaces with Jarvis-style responses.
        /// STEP 29: Relaxed filtering - only reject truly empty/filler responses.
        /// </summary>
        public string FilterGenericResponse(string response, ResponseIntent intent)
        {
            if (string.IsNullOrWhiteSpace(response))
                return SelectResponse(intent, SystemState.Idle, null, 1.0);

            var lower = response.Trim().ToLowerInvariant();
            
            // STEP 29: Only filter truly generic filler responses
            // Allow short but meaningful responses like "Good to see you", "Understood", "Alright"
            var genericFillers = new[] { "hey", "hi", "ok", "okay", "sure", "yes", "no", "yeah", "yep", "nope", "k", "yup" };
            
            // Only filter if it's EXACTLY a filler word (not part of a sentence)
            if (genericFillers.Contains(lower))
            {
                Debug.WriteLine($"[ResponseStyleController] Filtering filler response: \"{response}\"");
                return SelectResponse(intent, SystemState.Idle, null, 1.0);
            }
            
            // Filter very short responses (under 3 chars) that aren't meaningful
            if (lower.Length < 3)
            {
                Debug.WriteLine($"[ResponseStyleController] Filtering too-short response: \"{response}\"");
                return SelectResponse(intent, SystemState.Idle, null, 1.0);
            }

            return response;
        }

        /// <summary>
        /// Determines if a response should be spoken based on intent and state.
        /// </summary>
        public bool ShouldSpeak(ResponseIntent intent, SystemState state)
        {
            // Always speak errors and important notifications
            if (intent == ResponseIntent.SystemError || intent == ResponseIntent.OperationFailed)
                return true;

            // Speak acknowledgements and completions
            if (intent == ResponseIntent.Acknowledged || intent == ResponseIntent.ExecutionComplete)
                return true;

            // Speak execution starts
            if (intent == ResponseIntent.ExecutionStart)
                return true;

            // Speak greetings
            if (intent == ResponseIntent.Greeting)
                return true;

            return true; // Default to speaking
        }

        /// <summary>
        /// Processes user input and determines if it should be handled as conversation
        /// or passed to the agent/command system.
        /// 
        /// STEP 29 FIX: Greetings and small talk now go to LLM by default.
        /// Templates are only used as fallback when LLM fails or times out.
        /// </summary>
        public ProcessingResult ProcessInput(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
            {
                return new ProcessingResult
                {
                    IsConversational = false,
                    ShouldPassToAgent = false,
                    Response = null
                };
            }

            Debug.WriteLine($"[ResponseStyleController] Processing: \"{userInput}\"");

            // Check for reset commands
            var lower = userInput.ToLowerInvariant();
            if (lower.Contains("reset") || lower.Contains("forget me") || lower.Contains("forget my name"))
            {
                _greetingState.Reset();
                SessionUserName = null;
                return new ProcessingResult
                {
                    IsConversational = true,
                    ShouldPassToAgent = false,
                    Response = "Session reset. I have forgotten our previous introduction.",
                    Intent = ResponseIntentType.Denied
                };
            }

            // Classify the intent
            var intentResult = ResponseIntentClassifier.Classify(userInput);
            Debug.WriteLine($"[ResponseStyleController] Intent: {intentResult.Intent} (Confidence: {intentResult.Confidence:P0})");

            // Handle introductions - extract name but let LLM respond naturally
            if (intentResult.Intent == ResponseIntentType.Introduction && !string.IsNullOrEmpty(intentResult.ExtractedName))
            {
                var name = intentResult.ExtractedName;
                
                // Record the introduction for context
                _greetingState.RecordIntroduction(name);
                SessionUserName = name;
                ConversationContext.Instance.KnownUserName = name;
                
                Debug.WriteLine($"[ResponseStyleController] Name extracted: {name} - passing to LLM for natural response");
                
                // STEP 29: Pass to LLM instead of using template
                // The LLM will have context about the user's name
                return new ProcessingResult
                {
                    IsConversational = false,  // Let LLM handle it
                    ShouldPassToAgent = true,
                    Response = null,
                    Intent = intentResult.Intent,
                    ExtractedName = name
                };
            }

            // STEP 29: Greetings and small talk go to LLM for natural responses
            // Templates are only used as fallback (handled elsewhere)
            if (intentResult.Intent == ResponseIntentType.Greeting || intentResult.Intent == ResponseIntentType.SmallTalk)
            {
                Debug.WriteLine($"[ResponseStyleController] Greeting/SmallTalk detected - passing to LLM for natural response");
                
                // Pass to LLM - it will generate a natural response
                return new ProcessingResult
                {
                    IsConversational = false,  // Let LLM handle it
                    ShouldPassToAgent = true,
                    Response = null,
                    Intent = intentResult.Intent
                };
            }

            // For commands and questions, pass to agent
            if (intentResult.Intent == ResponseIntentType.Command || 
                intentResult.Intent == ResponseIntentType.Question ||
                intentResult.Intent == ResponseIntentType.Unknown)
            {
                Debug.WriteLine($"[ResponseStyleController] Passing to agent: {intentResult.Intent}");
                
                return new ProcessingResult
                {
                    IsConversational = false,
                    ShouldPassToAgent = true,
                    Response = null,
                    Intent = intentResult.Intent
                };
            }

            // Default: pass to agent (LLM)
            return new ProcessingResult
            {
                IsConversational = false,
                ShouldPassToAgent = true,
                Response = null,
                Intent = intentResult.Intent
            };
        }

        /// <summary>
        /// Gets a greeting response, optionally using the session user name.
        /// Now uses depth-aware templates.
        /// </summary>
        public string GetGreeting()
        {
            var depth = ConversationContext.Instance.CurrentDepth;
            return DepthAwareTemplates.GetTimeBasedGreeting(depth, PersonalityProfile.Current.Id, SessionUserName);
        }

        /// <summary>
        /// Gets an acknowledgement for command execution.
        /// Now uses depth-aware templates.
        /// </summary>
        public string GetAcknowledgement()
        {
            var depth = ConversationContext.Instance.CurrentDepth;
            return DepthAwareTemplates.GetAcknowledgement(depth, PersonalityProfile.Current.Id);
        }

        /// <summary>
        /// Gets a completion response after command execution.
        /// Now uses depth-aware templates.
        /// </summary>
        public string GetCompletionResponse()
        {
            var depth = ConversationContext.Instance.CurrentDepth;
            return DepthAwareTemplates.GetCompletion(depth, PersonalityProfile.Current.Id);
        }

        /// <summary>
        /// Gets cadence hint for TTS prompt phrasing based on current personality voice.
        /// Returns hints like "slightly slower", "controlled pacing", or null for normal.
        /// </summary>
        public string? GetCadenceHint(ResponseType responseType = ResponseType.Normal)
        {
            var selection = VoiceSelectionService.SelectVoice(responseType);
            return selection.CadenceHint;
        }

        /// <summary>
        /// Manually sets the session user name.
        /// </summary>
        public void SetUserName(string? name)
        {
            SessionUserName = name;
            if (!string.IsNullOrEmpty(name))
            {
                _greetingState.KnownUserName = name;
                ConversationContext.Instance.KnownUserName = name;
            }
        }

        /// <summary>
        /// Clears the session user name.
        /// </summary>
        public void ClearUserName()
        {
            SessionUserName = null;
        }

        /// <summary>
        /// Resets the session state including greeting state.
        /// </summary>
        public void ResetSession()
        {
            SessionUserName = null;
            _greetingState.Reset();
            ConversationContext.Instance.Reset();
            Debug.WriteLine("[ResponseStyleController] Session reset");
        }
        
        /// <summary>
        /// Gets context for LLM system prompt about greeting state and conversation depth.
        /// </summary>
        public string GetGreetingContextForLLM()
        {
            var parts = new System.Collections.Generic.List<string>();
            
            // Add depth instruction
            var depthInstruction = ConversationContext.Instance.GetDepthInstructionForLLM();
            if (!string.IsNullOrEmpty(depthInstruction))
            {
                parts.Add(depthInstruction);
            }
            
            // Add greeting context
            if (_greetingState.HasIntroducedToUser && !string.IsNullOrEmpty(_greetingState.KnownUserName))
            {
                parts.Add($"The user's name is {_greetingState.KnownUserName}. You have already been introduced in this session. Do not repeat formal greetings or say 'nice to meet you' again. Address them naturally by name when appropriate.");
            }
            
            return string.Join("\n", parts);
        }
    }

    public class ProcessingResult
    {
        public bool IsConversational { get; set; }
        public bool ShouldPassToAgent { get; set; }
        public string? Response { get; set; }
        public ResponseIntentType Intent { get; set; }
        public string? ExtractedName { get; set; }
    }

    public class ConversationResponseEventArgs : EventArgs
    {
        public ResponseIntentType Intent { get; set; }
        public string UserInput { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string? UserName { get; set; }
    }
}
