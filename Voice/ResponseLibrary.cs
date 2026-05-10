using System;
using System.Collections.Generic;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Jarvis-style conversation response templates.
    /// Rules: 1-2 sentences, no slang, no emojis, no exclamation marks, polite and calm.
    /// </summary>
    public static class JarvisConversationTemplates
    {
        private static readonly Random _random = new();

        #region Greeting Responses

        private static readonly string[] GreetingResponses = new[]
        {
            "Hello.",
            "Good to see you.",
            "Hello. How may I assist you today.",
            "Greetings.",
            "At your service.",
            "Hello. What can I do for you."
        };

        private static readonly string[] GreetingWithNameResponses = new[]
        {
            "Hello, {Name}.",
            "Good to see you, {Name}.",
            "Hello, {Name}. How may I assist you.",
            "Greetings, {Name}.",
            "At your service, {Name}."
        };

        #endregion

        #region Introduction Responses

        private static readonly string[] IntroductionResponses = new[]
        {
            "Hello, {Name}. It is a pleasure to meet you.",
            "Nice to meet you, {Name}. How may I assist.",
            "A pleasure, {Name}. I am Atlas, at your service.",
            "Hello, {Name}. I am glad to make your acquaintance.",
            "Welcome, {Name}. How may I be of assistance."
        };

        private static readonly string[] IntroductionWithSmallTalkResponses = new[]
        {
            "Hello, {Name}. The pleasure is mine. How may I assist you today.",
            "Nice to meet you as well, {Name}. I am Atlas, ready to help.",
            "A pleasure to meet you, {Name}. What can I do for you."
        };

        #endregion

        #region Small Talk Responses

        private static readonly string[] HowAreYouResponses = new[]
        {
            "All systems are operating normally. How may I assist.",
            "Functioning optimally. What can I do for you.",
            "Operating at full capacity. How may I help.",
            "All systems nominal. What do you need."
        };

        private static readonly string[] NiceToMeetYouResponses = new[]
        {
            "The pleasure is mine.",
            "Likewise. How may I assist you.",
            "And you as well. What can I do for you."
        };

        private static readonly string[] GeneralSmallTalkResponses = new[]
        {
            "I am here to assist. What do you need.",
            "Ready and waiting. How may I help.",
            "At your service. What can I do for you."
        };

        #endregion

        #region Clarification Responses

        private static readonly string[] ClarificationResponses = new[]
        {
            "I did not quite catch that. Could you please repeat.",
            "My apologies. Could you rephrase that.",
            "I am not certain I understood. Please clarify.",
            "Could you say that again."
        };

        #endregion

        #region Denial/Cancel Responses

        private static readonly string[] DenialResponses = new[]
        {
            "Understood. Let me know if you need anything else.",
            "Very well. I am here when you need me.",
            "Of course. Standing by."
        };

        #endregion

        #region Error Responses

        private static readonly string[] ErrorResponses = new[]
        {
            "I apologize, but I encountered an issue. Please try again.",
            "Something went wrong on my end. My apologies.",
            "I was unable to complete that request. Please try again."
        };

        #endregion

        #region Unknown Intent Responses

        private static readonly string[] UnknownResponses = new[]
        {
            "I am not sure I understand. Could you clarify.",
            "I did not quite follow. What would you like me to do.",
            "Could you please rephrase that request."
        };

        #endregion

        #region Public Methods

        public static string GetResponse(ResponseIntentResult intent, string? userName = null)
        {
            var response = intent.Intent switch
            {
                ResponseIntentType.Greeting => GetGreetingResponse(userName),
                ResponseIntentType.Introduction => GetIntroductionResponse(intent, userName),
                ResponseIntentType.SmallTalk => GetSmallTalkResponse(intent.OriginalText, userName),
                ResponseIntentType.ClarificationNeeded => GetRandomResponse(ClarificationResponses),
                ResponseIntentType.Denied => GetRandomResponse(DenialResponses),
                ResponseIntentType.Error => GetRandomResponse(ErrorResponses),
                ResponseIntentType.Unknown => GetRandomResponse(UnknownResponses),
                _ => null // Question and Command are handled elsewhere
            };

            return response ?? string.Empty;
        }

        public static bool IsConversationalIntent(ResponseIntentType intent)
        {
            return intent switch
            {
                ResponseIntentType.Greeting => true,
                ResponseIntentType.Introduction => true,
                ResponseIntentType.SmallTalk => true,
                ResponseIntentType.ClarificationNeeded => true,
                ResponseIntentType.Denied => true,
                _ => false
            };
        }

        #endregion

        #region Private Methods

        private static string GetGreetingResponse(string? userName)
        {
            if (!string.IsNullOrEmpty(userName))
            {
                return GetRandomResponse(GreetingWithNameResponses).Replace("{Name}", userName);
            }
            return GetRandomResponse(GreetingResponses);
        }

        private static string GetIntroductionResponse(ResponseIntentResult intent, string? existingName)
        {
            var name = intent.ExtractedName ?? existingName ?? "there";
            
            // Check if the introduction also contains small talk
            if (intent.Parameters.ContainsKey("hasSmallTalk"))
            {
                return GetRandomResponse(IntroductionWithSmallTalkResponses).Replace("{Name}", name);
            }
            
            return GetRandomResponse(IntroductionResponses).Replace("{Name}", name);
        }

        private static string GetSmallTalkResponse(string originalText, string? userName)
        {
            var text = originalText.ToLowerInvariant();
            
            // "How are you" variants
            if (text.Contains("how are you") || text.Contains("how's it going") || 
                text.Contains("how do you do") || text.Contains("how have you been"))
            {
                return GetRandomResponse(HowAreYouResponses);
            }
            
            // "Nice to meet you" variants
            if (text.Contains("nice to meet") || text.Contains("pleasure to meet") ||
                text.Contains("good to meet"))
            {
                return GetRandomResponse(NiceToMeetYouResponses);
            }
            
            return GetRandomResponse(GeneralSmallTalkResponses);
        }

        private static string GetRandomResponse(string[] responses)
        {
            return responses[_random.Next(responses.Length)];
        }

        #endregion

        #region Time-Based Greetings

        public static string GetTimeBasedGreeting(string? userName = null)
        {
            var hour = DateTime.Now.Hour;
            var greeting = hour switch
            {
                >= 5 and < 12 => "Good morning",
                >= 12 and < 17 => "Good afternoon",
                >= 17 and < 21 => "Good evening",
                _ => "Hello"
            };

            if (!string.IsNullOrEmpty(userName))
            {
                return $"{greeting}, {userName}. How may I assist you.";
            }
            return $"{greeting}. How may I assist you.";
        }

        #endregion

        #region Session-Aware Greeting Responses

        /// <summary>
        /// First-time formal introduction response. Used only once per session per user.
        /// </summary>
        public static string GetFirstIntroductionResponse(string name, bool hasSmallTalk)
        {
            if (hasSmallTalk)
            {
                var responses = new[]
                {
                    $"Hello, {name}. The pleasure is mine. How may I assist you today.",
                    $"A pleasure to meet you, {name}. I am Atlas, at your service.",
                    $"Welcome, {name}. It is good to make your acquaintance. How may I help."
                };
                return GetRandomResponse(responses);
            }
            else
            {
                var responses = new[]
                {
                    $"Hello, {name}. It is a pleasure to meet you. How may I assist.",
                    $"A pleasure, {name}. I am Atlas, ready to help.",
                    $"Welcome, {name}. How may I be of assistance."
                };
                return GetRandomResponse(responses);
            }
        }

        /// <summary>
        /// Brief acknowledgement for subsequent greetings. Never repeats "pleasure is mine".
        /// </summary>
        public static string GetSubsequentGreetingResponse(string name)
        {
            var hour = DateTime.Now.Hour;
            var timeGreeting = hour switch
            {
                >= 5 and < 12 => "Good morning",
                >= 12 and < 17 => "Good afternoon",
                >= 17 and < 21 => "Good evening",
                _ => "Hello"
            };

            var responses = new[]
            {
                $"{timeGreeting}, {name}. How may I assist.",
                $"Yes, {name}. What can I help you with.",
                $"I am here, {name}. What do you need.",
                $"Good to speak with you again, {name}.",
                $"{name}. How may I help.",
                $"At your service, {name}."
            };
            return GetRandomResponse(responses);
        }

        #endregion

        #region Acknowledgement Responses (for commands)

        public static string GetAcknowledgement()
        {
            // Delegate to depth-aware templates
            return DepthAwareTemplates.GetAcknowledgement(Conversation.Models.ConversationContext.Instance.CurrentDepth);
        }

        public static string GetCompletionResponse()
        {
            // Delegate to depth-aware templates
            return DepthAwareTemplates.GetCompletion(Conversation.Models.ConversationContext.Instance.CurrentDepth);
        }

        #endregion
    }
}
