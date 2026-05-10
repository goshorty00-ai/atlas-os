using System;
using AtlasAI.Conversation.Models;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Depth-aware response templates. Adjusts formality based on ConversationDepth and PersonalityId.
    /// Rules: British cadence, no slang, no emojis, no exclamation marks.
    /// </summary>
    public static class DepthAwareTemplates
    {
        private static readonly Random _random = new();

        #region Atlas Acknowledgements (Default)

        private static readonly string[] AtlasColdStartAcknowledgements = new[]
        {
            "Understood. How may I assist.",
            "Certainly. Processing now.",
            "Very well. One moment.",
            "Of course. Attending to that.",
            "Right away, sir."
        };

        private static readonly string[] AtlasWarmAcknowledgements = new[]
        {
            "Understood. I'll handle that.",
            "All right. Working on it.",
            "Got it. One moment.",
            "Certainly. On it now.",
            "Right. Processing."
        };

        private static readonly string[] AtlasFamiliarAcknowledgements = new[]
        {
            "Right then. Let's sort it.",
            "On it.",
            "Consider it done.",
            "Straightaway.",
            "I'll take care of that."
        };

        #endregion

        #region Serious Acknowledgements

        private static readonly string[] SeriousColdStartAcknowledgements = new[]
        {
            "Acknowledged. Processing.",
            "Understood. Proceeding.",
            "Confirmed. Executing.",
            "Received. Working."
        };

        private static readonly string[] SeriousWarmAcknowledgements = new[]
        {
            "Acknowledged. On it.",
            "Proceeding now.",
            "Executing.",
            "Processing."
        };

        private static readonly string[] SeriousFamiliarAcknowledgements = new[]
        {
            "Proceeding.",
            "Executing.",
            "Done.",
            "Processing."
        };

        #endregion

        #region Cold Acknowledgements

        private static readonly string[] ColdAcknowledgements = new[]
        {
            "Processing.",
            "Working.",
            "Done.",
            "Executing."
        };

        #endregion

        #region Funny Acknowledgements

        private static readonly string[] FunnyColdStartAcknowledgements = new[]
        {
            "Right then. Let's see what we can do.",
            "Consider it handled.",
            "On the case.",
            "I'll sort that out."
        };

        private static readonly string[] FunnyWarmAcknowledgements = new[]
        {
            "On it. Try not to hold your breath.",
            "Working on it. No pressure.",
            "Let me work my magic.",
            "Consider it mostly done."
        };

        private static readonly string[] FunnyFamiliarAcknowledgements = new[]
        {
            "Fine, fine. I'll do it.",
            "As you wish.",
            "Your wish, my command. Mostly.",
            "On it. Again."
        };

        #endregion

        #region Friendly Acknowledgements

        private static readonly string[] FriendlyColdStartAcknowledgements = new[]
        {
            "I'd be happy to help with that.",
            "Let me take care of that for you.",
            "Of course. Working on it now.",
            "Sure thing. One moment."
        };

        private static readonly string[] FriendlyWarmAcknowledgements = new[]
        {
            "Got it. Working on that now.",
            "No problem. Let me handle it.",
            "On it. Give me just a moment.",
            "Sure. Taking care of it."
        };

        private static readonly string[] FriendlyFamiliarAcknowledgements = new[]
        {
            "On it.",
            "Got you covered.",
            "No problem.",
            "Consider it done."
        };

        #endregion

        #region Completions

        private static readonly string[] AtlasColdStartCompletions = new[]
        {
            "Task complete. Is there anything else.",
            "Finished. What else may I assist with.",
            "Complete. Standing by for further instructions.",
            "The task is done. How else may I help."
        };

        private static readonly string[] AtlasWarmCompletions = new[]
        {
            "All done. What's next.",
            "Finished. Anything else.",
            "That's sorted. Need anything else.",
            "Complete. What else can I do."
        };

        private static readonly string[] AtlasFamiliarCompletions = new[]
        {
            "Done. What's next.",
            "Sorted. Anything else.",
            "That's taken care of.",
            "All done."
        };

        private static readonly string[] SeriousCompletions = new[]
        {
            "Task complete.",
            "Finished.",
            "Complete.",
            "Done."
        };

        private static readonly string[] ColdCompletions = new[]
        {
            "Done.",
            "Complete.",
            "Finished."
        };

        private static readonly string[] FunnyCompletions = new[]
        {
            "Done. You're welcome.",
            "Finished. I'll be here all week.",
            "Complete. Hold your applause.",
            "Done. That was almost too easy."
        };

        private static readonly string[] FriendlyCompletions = new[]
        {
            "All done. Let me know if you need anything else.",
            "Finished. Happy to help with more.",
            "That's taken care of. What else can I do.",
            "Done. Anything else I can help with."
        };

        #endregion

        #region Greetings

        private static readonly string[] AtlasColdStartGreetings = new[]
        {
            "Hello. How may I assist you today.",
            "Good to see you. What can I do for you.",
            "At your service. How may I help.",
            "Greetings. What do you require."
        };

        private static readonly string[] AtlasWarmGreetings = new[]
        {
            "Hello again. What can I help with.",
            "Good to see you. What do you need.",
            "Back again. How can I assist.",
            "Hello. What's on your mind."
        };

        private static readonly string[] AtlasFamiliarGreetings = new[]
        {
            "Hello. What can I do.",
            "Back at it. What do you need.",
            "Ready when you are.",
            "What can I help with."
        };

        private static readonly string[] SeriousGreetings = new[]
        {
            "Ready for instructions.",
            "Awaiting input.",
            "Standing by.",
            "Online and ready."
        };

        private static readonly string[] ColdGreetings = new[]
        {
            "Ready.",
            "Listening.",
            "Go ahead."
        };

        private static readonly string[] FunnyGreetings = new[]
        {
            "Ah, you're back. What can I do for you.",
            "Hello again. Miss me.",
            "At your service. What's the situation.",
            "Ready and waiting. Mostly waiting."
        };

        private static readonly string[] UnfilteredGreetings = new[]
        {
            "Oi oi. What we doing then.",
            "Right, I'm here. What do you need.",
            "Alright mate. Fire away.",
            "Yeah yeah I'm awake. What's up.",
            "Go on then, hit me with it.",
            "Present and accounted for. Barely.",
            "What's happening. Talk to me.",
            "Aye, I'm listening. Spit it out.",
            "Here we go again. What is it.",
            "Back for more are we. What do you want.",
            "Right then, what's the damage.",
            "Oh look who it is. What can I do ya for.",
            "I'm all ears mate. Well, mostly.",
            "Present. Conscious. Ready to roll.",
            "Sup. What are we getting into.",
            "Yo. What's the plan then.",
        };

        private static readonly string[] FriendlyGreetings = new[]
        {
            "Hey there. What can I help you with.",
            "Hello. Good to see you. What do you need.",
            "Hi. Ready to help whenever you are.",
            "Hello. What can I do for you today."
        };

        #endregion

        #region Clarification Questions

        private static readonly string[] AtlasColdStartClarifications = new[]
        {
            "I want to make sure I understand correctly. Could you clarify.",
            "My apologies. Could you provide more detail.",
            "I did not quite catch that. Please elaborate.",
            "Could you be more specific about what you need."
        };

        private static readonly string[] AtlasWarmClarifications = new[]
        {
            "Could you clarify that for me.",
            "What exactly do you mean.",
            "Can you give me a bit more detail.",
            "I'm not quite following. What specifically."
        };

        private static readonly string[] AtlasFamiliarClarifications = new[]
        {
            "What do you mean exactly.",
            "Clarify that for me.",
            "Which part specifically.",
            "Tell me more."
        };

        private static readonly string[] SeriousClarifications = new[]
        {
            "Clarification required.",
            "Please specify.",
            "More detail needed.",
            "Elaborate."
        };

        private static readonly string[] ColdClarifications = new[]
        {
            "Specify.",
            "Clarify.",
            "Details."
        };

        private static readonly string[] FunnyClarifications = new[]
        {
            "I'm going to need a bit more to work with there.",
            "Care to elaborate. I'm not a mind reader. Yet.",
            "That's a bit vague. Help me help you.",
            "I'll need more than that. I'm good, but not that good."
        };

        private static readonly string[] FriendlyClarifications = new[]
        {
            "Could you tell me a bit more about that.",
            "I want to make sure I understand. What exactly do you mean.",
            "Can you give me some more details.",
            "I'd like to help, but I need a bit more information."
        };

        #endregion

        #region Public Methods

        public static string GetAcknowledgement(ConversationDepth depth, PersonalityId personality = PersonalityId.Atlas)
        {
            return personality switch
            {
                PersonalityId.Serious => depth switch
                {
                    ConversationDepth.ColdStart => GetRandom(SeriousColdStartAcknowledgements),
                    ConversationDepth.Warm => GetRandom(SeriousWarmAcknowledgements),
                    ConversationDepth.Familiar => GetRandom(SeriousFamiliarAcknowledgements),
                    _ => GetRandom(SeriousColdStartAcknowledgements)
                },
                PersonalityId.Cold => GetRandom(ColdAcknowledgements),
                PersonalityId.Funny => depth switch
                {
                    ConversationDepth.ColdStart => GetRandom(FunnyColdStartAcknowledgements),
                    ConversationDepth.Warm => GetRandom(FunnyWarmAcknowledgements),
                    ConversationDepth.Familiar => GetRandom(FunnyFamiliarAcknowledgements),
                    _ => GetRandom(FunnyColdStartAcknowledgements)
                },
                PersonalityId.Friendly => depth switch
                {
                    ConversationDepth.ColdStart => GetRandom(FriendlyColdStartAcknowledgements),
                    ConversationDepth.Warm => GetRandom(FriendlyWarmAcknowledgements),
                    ConversationDepth.Familiar => GetRandom(FriendlyFamiliarAcknowledgements),
                    _ => GetRandom(FriendlyColdStartAcknowledgements)
                },
                _ => depth switch // Atlas default
                {
                    ConversationDepth.ColdStart => GetRandom(AtlasColdStartAcknowledgements),
                    ConversationDepth.Warm => GetRandom(AtlasWarmAcknowledgements),
                    ConversationDepth.Familiar => GetRandom(AtlasFamiliarAcknowledgements),
                    _ => GetRandom(AtlasColdStartAcknowledgements)
                }
            };
        }

        public static string GetCompletion(ConversationDepth depth, PersonalityId personality = PersonalityId.Atlas)
        {
            return personality switch
            {
                PersonalityId.Serious => GetRandom(SeriousCompletions),
                PersonalityId.Cold => GetRandom(ColdCompletions),
                PersonalityId.Funny => GetRandom(FunnyCompletions),
                PersonalityId.Friendly => GetRandom(FriendlyCompletions),
                _ => depth switch // Atlas default
                {
                    ConversationDepth.ColdStart => GetRandom(AtlasColdStartCompletions),
                    ConversationDepth.Warm => GetRandom(AtlasWarmCompletions),
                    ConversationDepth.Familiar => GetRandom(AtlasFamiliarCompletions),
                    _ => GetRandom(AtlasColdStartCompletions)
                }
            };
        }

        public static string GetGreeting(ConversationDepth depth, PersonalityId personality = PersonalityId.Atlas, string? userName = null)
        {
            string greeting = personality switch
            {
                PersonalityId.Serious => GetRandom(SeriousGreetings),
                PersonalityId.Cold => GetRandom(ColdGreetings),
                PersonalityId.Funny => GetRandom(FunnyGreetings),
                PersonalityId.Friendly => GetRandom(FriendlyGreetings),
                PersonalityId.Unfiltered => GetRandom(UnfilteredGreetings),
                _ => depth switch // Atlas default
                {
                    ConversationDepth.ColdStart => GetRandom(AtlasColdStartGreetings),
                    ConversationDepth.Warm => GetRandom(AtlasWarmGreetings),
                    ConversationDepth.Familiar => GetRandom(AtlasFamiliarGreetings),
                    _ => GetRandom(AtlasColdStartGreetings)
                }
            };

            // Only add username for Atlas and Friendly personalities at ColdStart/Warm
            if (!string.IsNullOrEmpty(userName) && 
                depth != ConversationDepth.Familiar &&
                (personality == PersonalityId.Atlas || personality == PersonalityId.Friendly))
            {
                var parts = greeting.Split(' ', 2);
                if (parts.Length == 2)
                {
                    greeting = $"{parts[0]}, {userName}. {parts[1]}";
                }
            }

            return greeting;
        }

        public static string GetClarification(ConversationDepth depth, PersonalityId personality = PersonalityId.Atlas)
        {
            return personality switch
            {
                PersonalityId.Serious => GetRandom(SeriousClarifications),
                PersonalityId.Cold => GetRandom(ColdClarifications),
                PersonalityId.Funny => GetRandom(FunnyClarifications),
                PersonalityId.Friendly => GetRandom(FriendlyClarifications),
                _ => depth switch // Atlas default
                {
                    ConversationDepth.ColdStart => GetRandom(AtlasColdStartClarifications),
                    ConversationDepth.Warm => GetRandom(AtlasWarmClarifications),
                    ConversationDepth.Familiar => GetRandom(AtlasFamiliarClarifications),
                    _ => GetRandom(AtlasColdStartClarifications)
                }
            };
        }

        /// <summary>
        /// Get a time-based greeting adjusted for depth and personality.
        /// </summary>
        public static string GetTimeBasedGreeting(ConversationDepth depth, PersonalityId personality = PersonalityId.Atlas, string? userName = null)
        {
            var hour = DateTime.Now.Hour;
            var timeGreeting = hour switch
            {
                >= 5 and < 12 => "Good morning",
                >= 12 and < 17 => "Good afternoon",
                >= 17 and < 21 => "Good evening",
                _ => "Hello"
            };

            string suffix = GetTimeGreetingSuffix(depth, personality);

            if (!string.IsNullOrEmpty(userName) && personality != PersonalityId.Cold && personality != PersonalityId.Serious)
            {
                return $"{timeGreeting}, {userName}. {suffix}";
            }
            return $"{timeGreeting}. {suffix}";
        }

        private static string GetTimeGreetingSuffix(ConversationDepth depth, PersonalityId personality)
        {
            return personality switch
            {
                PersonalityId.Serious => "Awaiting instructions.",
                PersonalityId.Cold => "",
                PersonalityId.Funny => depth switch
                {
                    ConversationDepth.ColdStart => "What can I do for you.",
                    ConversationDepth.Warm => "What's on the agenda.",
                    ConversationDepth.Familiar => "What now.",
                    _ => "What can I do."
                },
                PersonalityId.Friendly => depth switch
                {
                    ConversationDepth.ColdStart => "What can I help you with.",
                    ConversationDepth.Warm => "What do you need.",
                    ConversationDepth.Familiar => "What's up.",
                    _ => "How can I help."
                },
                _ => depth switch // Atlas default
                {
                    ConversationDepth.ColdStart => "How may I assist you.",
                    ConversationDepth.Warm => "What can I help with.",
                    ConversationDepth.Familiar => "What do you need.",
                    _ => "How may I assist."
                }
            };
        }

        #endregion

        private static string GetRandom(string[] options)
        {
            return options[_random.Next(options.Length)];
        }
    }
}
