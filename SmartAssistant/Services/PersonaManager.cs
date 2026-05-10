using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.SmartAssistant.Models;

namespace AtlasAI.SmartAssistant.Services
{
    /// <summary>
    /// Manages switchable assistant personas with different tones and behaviors
    /// </summary>
    public class PersonaManager
    {
        private readonly string _dataPath;
        private List<AssistantPersona> _personas = new();
        private AssistantPersona? _activePersona;
        private MoodState _currentMood = new();
        
        public event EventHandler<AssistantPersona>? PersonaChanged;
        public event EventHandler<MoodState>? MoodDetected;
        
        public AssistantPersona? ActivePersona => _activePersona;
        public MoodState CurrentMood => _currentMood;
        public IReadOnlyList<AssistantPersona> Personas => _personas.AsReadOnly();
        
        public PersonaManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _dataPath = Path.Combine(appData, "AtlasAI", "personas.json");
            
            InitializeBuiltInPersonas();
        }
        
        public async Task InitializeAsync()
        {
            await LoadPersonasAsync();
            
            // Set default persona if none active
            if (_activePersona == null)
            {
                _activePersona = _personas.FirstOrDefault(p => p.Name == "Atlas");
            }
            
            Debug.WriteLine($"[PersonaManager] Loaded {_personas.Count} personas, active: {_activePersona?.Name}");
        }
        
        /// <summary>
        /// Switch to a different persona
        /// </summary>
        public async Task SwitchPersonaAsync(string personaId)
        {
            var persona = _personas.FirstOrDefault(p => p.Id == personaId);
            if (persona != null)
            {
                _activePersona = persona;
                await SavePersonasAsync();
                PersonaChanged?.Invoke(this, persona);
                Debug.WriteLine($"[PersonaManager] Switched to persona: {persona.Name}");
            }
        }
        
        /// <summary>
        /// Create a custom persona
        /// </summary>
        public async Task<AssistantPersona> CreatePersonaAsync(
            string name,
            string description,
            PersonaTone tone,
            string? systemPromptAddition = null,
            string? voiceId = null)
        {
            var persona = new AssistantPersona
            {
                Name = name,
                Description = description,
                Tone = tone,
                SystemPromptAddition = systemPromptAddition ?? "",
                VoiceId = voiceId,
                IsBuiltIn = false
            };
            
            _personas.Add(persona);
            await SavePersonasAsync();
            
            Debug.WriteLine($"[PersonaManager] Created persona: {name}");
            return persona;
        }
        
        /// <summary>
        /// Delete a custom persona
        /// </summary>
        public async Task DeletePersonaAsync(string personaId)
        {
            var persona = _personas.FirstOrDefault(p => p.Id == personaId);
            if (persona != null && !persona.IsBuiltIn)
            {
                _personas.Remove(persona);
                
                if (_activePersona?.Id == personaId)
                {
                    _activePersona = _personas.FirstOrDefault(p => p.Name == "Atlas");
                }
                
                await SavePersonasAsync();
            }
        }
        
        /// <summary>
        /// Detect user mood from text
        /// </summary>
        public MoodState DetectMood(string text)
        {
            var lowerText = text.ToLowerInvariant();
            
            var mood = new MoodState
            {
                TriggerText = text,
                DetectedAt = DateTime.Now
            };
            
            // Frustration indicators
            var frustrationWords = new[] { "ugh", "damn", "frustrated", "annoying", "stupid", "broken", "doesn't work", "hate", "terrible", "awful" };
            if (frustrationWords.Any(w => lowerText.Contains(w)))
            {
                mood.DetectedMood = UserMood.Frustrated;
                mood.Confidence = 0.8;
            }
            // Rushed indicators
            else if (lowerText.Contains("quick") || lowerText.Contains("hurry") || lowerText.Contains("asap") || lowerText.Contains("urgent") || lowerText.Contains("fast"))
            {
                mood.DetectedMood = UserMood.Rushed;
                mood.Confidence = 0.7;
            }
            // Happy indicators
            else if (lowerText.Contains("thanks") || lowerText.Contains("great") || lowerText.Contains("awesome") || lowerText.Contains("perfect") || lowerText.Contains("love"))
            {
                mood.DetectedMood = UserMood.Happy;
                mood.Confidence = 0.7;
            }
            // Curious indicators
            else if (lowerText.Contains("how") || lowerText.Contains("why") || lowerText.Contains("what if") || lowerText.Contains("explain") || lowerText.Contains("curious"))
            {
                mood.DetectedMood = UserMood.Curious;
                mood.Confidence = 0.6;
            }
            // Tired indicators
            else if (lowerText.Contains("tired") || lowerText.Contains("exhausted") || lowerText.Contains("sleepy") || lowerText.Contains("long day"))
            {
                mood.DetectedMood = UserMood.Tired;
                mood.Confidence = 0.7;
            }
            else
            {
                mood.DetectedMood = UserMood.Neutral;
                mood.Confidence = 0.5;
            }
            
            _currentMood = mood;
            
            if (mood.DetectedMood != UserMood.Neutral)
            {
                MoodDetected?.Invoke(this, mood);
            }
            
            return mood;
        }
        
        /// <summary>
        /// Get response style based on current mood and persona
        /// </summary>
        public string GetResponseStyle()
        {
            var style = _activePersona?.Tone switch
            {
                PersonaTone.Professional => "Be professional, clear, and efficient.",
                PersonaTone.Friendly => "Be warm, friendly, and conversational.",
                PersonaTone.Casual => "Be relaxed and casual, like talking to a friend.",
                PersonaTone.Formal => "Be formal and respectful.",
                PersonaTone.Playful => "Be playful and add some humor when appropriate.",
                PersonaTone.Serious => "Be serious and focused on the task.",
                _ => "Be helpful and clear."
            };
            
            // Adjust for mood
            style += _currentMood.DetectedMood switch
            {
                UserMood.Frustrated => " The user seems frustrated, so be extra patient and understanding.",
                UserMood.Rushed => " The user is in a hurry, so be concise and get straight to the point.",
                UserMood.Happy => " The user is in a good mood, feel free to be more conversational.",
                UserMood.Tired => " The user seems tired, keep responses brief and offer to help simplify tasks.",
                UserMood.Curious => " The user is curious, provide detailed explanations.",
                _ => ""
            };
            
            return style;
        }
        
        /// <summary>
        /// Get the full system prompt addition for current persona
        /// </summary>
        public string GetSystemPromptAddition()
        {
            if (_activePersona == null) return "";
            
            var prompt = _activePersona.SystemPromptAddition;
            prompt += $"\n\nResponse style: {GetResponseStyle()}";
            
            return prompt;
        }
        
        private void InitializeBuiltInPersonas()
        {
            _personas = new List<AssistantPersona>
            {
                new AssistantPersona
                {
                    Name = "Atlas",
                    Description = "The default professional AI assistant, like JARVIS",
                    Tone = PersonaTone.Professional,
                    SystemPromptAddition = "You are Atlas, a sophisticated AI assistant. Be analytical, proactive, and technically precise.",
                    IsBuiltIn = true
                },
                new AssistantPersona
                {
                    Name = "Buddy",
                    Description = "A friendly, casual assistant",
                    Tone = PersonaTone.Friendly,
                    SystemPromptAddition = "You are Buddy, a friendly and approachable assistant. Be warm, use casual language, and make the user feel comfortable.",
                    IsBuiltIn = true
                },
                new AssistantPersona
                {
                    Name = "Professor",
                    Description = "An educational, detailed assistant",
                    Tone = PersonaTone.Formal,
                    SystemPromptAddition = "You are Professor, an educational assistant. Provide detailed explanations, teach concepts, and be thorough in your responses.",
                    IsBuiltIn = true
                },
                new AssistantPersona
                {
                    Name = "Quick",
                    Description = "A fast, no-nonsense assistant",
                    Tone = PersonaTone.Serious,
                    SystemPromptAddition = "You are Quick, an efficient assistant. Give the shortest possible answers that still fully address the request. No fluff.",
                    IsBuiltIn = true
                },
                new AssistantPersona
                {
                    Name = "Spark",
                    Description = "A playful, creative assistant",
                    Tone = PersonaTone.Playful,
                    SystemPromptAddition = "You are Spark, a creative and playful assistant. Add personality to your responses, use emojis occasionally, and make interactions fun.",
                    IsBuiltIn = true
                }
            };
        }
        
        private async Task LoadPersonasAsync()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var json = await File.ReadAllTextAsync(_dataPath);
                    var customPersonas = JsonSerializer.Deserialize<List<AssistantPersona>>(json) ?? new();
                    
                    // Add custom personas to built-in ones
                    foreach (var persona in customPersonas.Where(p => !p.IsBuiltIn))
                    {
                        _personas.Add(persona);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PersonaManager] Error loading personas: {ex.Message}");
            }
        }
        
        private async Task SavePersonasAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_dataPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                // Only save custom personas
                var customPersonas = _personas.Where(p => !p.IsBuiltIn).ToList();
                var json = JsonSerializer.Serialize(customPersonas, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_dataPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PersonaManager] Error saving personas: {ex.Message}");
            }
        }
    }
}
