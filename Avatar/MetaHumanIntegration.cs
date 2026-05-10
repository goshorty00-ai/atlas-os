using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Avatar
{
    /// <summary>
    /// MetaHuman personality definition - each assistant has unique traits
    /// </summary>
    public class MetaHumanPersonality
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Archetype { get; set; } = "";
        public string Gender { get; set; } = "";
        public string VoiceStyle { get; set; } = "";
        public string ElevenLabsVoiceId { get; set; } = "";
        public string Accent { get; set; } = "";
        public string[] SamplePhrases { get; set; } = Array.Empty<string>();
        public string Emoji { get; set; } = "🤖";
        public Dictionary<string, double> VoiceSettings { get; set; } = new();
        
        // MetaHuman appearance settings (for UE5)
        public string MetaHumanPreset { get; set; } = "";
        public string HairStyle { get; set; } = "";
        public string Outfit { get; set; } = "";
    }

    /// <summary>
    /// MetaHuman state for animation control
    /// </summary>
    public enum MetaHumanState
    {
        Idle,
        Listening,
        Thinking,
        Speaking,
        Happy,
        Concerned,
        Excited,
        Focused
    }

    /// <summary>
    /// Command sent to Unreal Engine MetaHuman
    /// </summary>
    public class MetaHumanCommand
    {
        public string Type { get; set; } = "";
        public string PersonalityId { get; set; } = "";
        public string State { get; set; } = "";
        public string Text { get; set; } = "";
        public string AudioPath { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    /// <summary>
    /// Integration layer for Unreal Engine MetaHuman avatars.
    /// Communicates via named pipes to control realistic digital humans.
    /// </summary>
    public class MetaHumanIntegration : IDisposable
    {
        private const string PIPE_NAME = "AtlasAI_MetaHuman";
        private const string UE_EXE_NAME = "AtlasAI_MetaHuman.exe";
        
        private Process? _unrealProcess;
        private NamedPipeServerStream? _pipeServer;
        private CancellationTokenSource? _listenerCts;
        private bool _isRunning;
        private MetaHumanPersonality? _activePersonality;
        
        private static readonly Dictionary<string, MetaHumanPersonality> _personalities = new();
        
        public event Action<string>? OnUnrealMessage;
        public event Action? OnUnrealStarted;
        public event Action? OnUnrealStopped;
        public event Action<MetaHumanPersonality>? OnPersonalityChanged;
        public event Action<MetaHumanState>? OnStateChanged;
        
        public bool IsRunning => _isRunning && _unrealProcess != null && !_unrealProcess.HasExited;
        public MetaHumanPersonality? ActivePersonality => _activePersonality;
        
        static MetaHumanIntegration()
        {
            InitializePersonalities();
        }
        
        /// <summary>
        /// Initialize the 6 MetaHuman personalities
        /// </summary>
        private static void InitializePersonalities()
        {
            // 1. ATLAS - British Butler / JARVIS-inspired (Primary)
            _personalities["atlas"] = new MetaHumanPersonality
            {
                Id = "atlas",
                Name = "Atlas",
                Description = "Your distinguished British butler. Impeccably polite with dry wit.",
                Archetype = "British Butler",
                Gender = "Male",
                VoiceStyle = "British RP, warm baritone, measured pace",
                ElevenLabsVoiceId = Voice.VoiceProfile.DefaultAtlasVoiceId,
                Accent = "British RP",
                Emoji = "🎩",
                SamplePhrases = new[]
                {
                    "Very good, sir. I've taken the liberty of organizing your schedule.",
                    "Might I suggest a brief respite? You've been working for 4 hours straight.",
                    "Consider it done. Though I must say, your taste in music remains... eclectic."
                },
                VoiceSettings = new Dictionary<string, double>
                {
                    { "stability", 0.92 },
                    { "similarity_boost", 0.85 },
                    { "style", 0.03 }
                },
                MetaHumanPreset = "DistinguishedGentleman",
                HairStyle = "SilverGrey_Refined",
                Outfit = "Butler_Classic"
            };

            // 2. NOVA - Energetic Tech Genius
            _personalities["nova"] = new MetaHumanPersonality
            {
                Id = "nova",
                Name = "Nova",
                Description = "Energetic tech enthusiast. Creative problem solver with startup energy.",
                Archetype = "Tech Genius",
                Gender = "Female",
                VoiceStyle = "American West Coast, upbeat, slightly fast-paced",
                ElevenLabsVoiceId = "EXAVITQu4vr4xnSDxMaL", // Bella - soft but energetic
                Accent = "American",
                Emoji = "⚡",
                SamplePhrases = new[]
                {
                    "Ooh, that's a cool problem! Let me dig into this real quick.",
                    "Okay so I found like three ways to do this - want the fast way or the right way?",
                    "You're crushing it today! Seriously, look at this productivity graph!"
                },
                VoiceSettings = new Dictionary<string, double>
                {
                    { "stability", 0.75 },
                    { "similarity_boost", 0.80 },
                    { "style", 0.15 }
                },
                MetaHumanPreset = "YoungProfessional",
                HairStyle = "Asymmetric_BlueHighlights",
                Outfit = "TechCasual_Modern"
            };
            
            // 3. SENTINEL - Security Expert
            _personalities["sentinel"] = new MetaHumanPersonality
            {
                Id = "sentinel",
                Name = "Sentinel",
                Description = "Security specialist with military precision. Direct and protective.",
                Archetype = "Security Expert",
                Gender = "Male",
                VoiceStyle = "American, authoritative, calm but firm",
                ElevenLabsVoiceId = "VR6AewLTigWG4xSOukaG", // Arnold - deep and authoritative
                Accent = "American",
                Emoji = "🛡️",
                SamplePhrases = new[]
                {
                    "Threat detected. I've already blocked it. You're secure.",
                    "Running security sweep now. All systems nominal.",
                    "I don't recommend that action. Here's why."
                },
                VoiceSettings = new Dictionary<string, double>
                {
                    { "stability", 0.95 },
                    { "similarity_boost", 0.90 },
                    { "style", 0.0 }
                },
                MetaHumanPreset = "MilitaryProfessional",
                HairStyle = "Short_Military",
                Outfit = "TacticalCasual"
            };
            
            // 4. SAGE - Wise Mentor
            _personalities["sage"] = new MetaHumanPersonality
            {
                Id = "sage",
                Name = "Sage",
                Description = "Wise mentor and scholar. Patient teacher who explains thoroughly.",
                Archetype = "Academic Scholar",
                Gender = "Female",
                VoiceStyle = "Soft British/Irish, measured, warm",
                ElevenLabsVoiceId = "21m00Tcm4TlvDq8ikWAM", // Rachel - calm and wise
                Accent = "British/Irish",
                Emoji = "📚",
                SamplePhrases = new[]
                {
                    "Ah, an interesting question. Let me share what I know...",
                    "You're on the right track. Consider this perspective...",
                    "Mistakes are simply lessons in disguise. Shall we try again?"
                },
                VoiceSettings = new Dictionary<string, double>
                {
                    { "stability", 0.90 },
                    { "similarity_boost", 0.85 },
                    { "style", 0.05 }
                },
                MetaHumanPreset = "DistinguishedProfessor",
                HairStyle = "SilverWhite_Elegant",
                Outfit = "Academic_Professional"
            };
            
            // 5. SPARK - Creative Artist
            _personalities["spark"] = new MetaHumanPersonality
            {
                Id = "spark",
                Name = "Spark",
                Description = "Creative artist and imaginative dreamer. Thinks outside the box.",
                Archetype = "Creative Artist",
                Gender = "Non-binary",
                VoiceStyle = "Melodic, expressive, slight ethereal quality",
                ElevenLabsVoiceId = "AZnzlk1XvdvUeBnXmlld", // Domi - expressive
                Accent = "Neutral",
                Emoji = "🎨",
                SamplePhrases = new[]
                {
                    "What if we approached this from a completely different angle?",
                    "I'm sensing you need something more... inspiring today.",
                    "Let's make something beautiful together!"
                },
                VoiceSettings = new Dictionary<string, double>
                {
                    { "stability", 0.70 },
                    { "similarity_boost", 0.75 },
                    { "style", 0.25 }
                },
                MetaHumanPreset = "ArtisticCreative",
                HairStyle = "Colorful_Flowing",
                Outfit = "Artistic_Bohemian"
            };
            
            // 6. FORGE - Engineer / Builder
            _personalities["forge"] = new MetaHumanPersonality
            {
                Id = "forge",
                Name = "Forge",
                Description = "Practical engineer and builder. Hands-on problem solver.",
                Archetype = "Engineer",
                Gender = "Male",
                VoiceStyle = "Warm American Midwest, steady, reassuring",
                ElevenLabsVoiceId = "ErXwobaYiN019PkySvjV", // Antoni - warm and practical
                Accent = "American Midwest",
                Emoji = "🔧",
                SamplePhrases = new[]
                {
                    "Alright, let's roll up our sleeves and figure this out.",
                    "I've seen this before. Here's what we're gonna do...",
                    "Good work holds up. Let's make sure we do this right."
                },
                VoiceSettings = new Dictionary<string, double>
                {
                    { "stability", 0.88 },
                    { "similarity_boost", 0.82 },
                    { "style", 0.08 }
                },
                MetaHumanPreset = "WorkingProfessional",
                HairStyle = "SaltPepper_Practical",
                Outfit = "Workshop_Casual"
            };
        }

        
        #region Public API
        
        /// <summary>Get all available personalities</summary>
        public static IReadOnlyDictionary<string, MetaHumanPersonality> GetPersonalities() => _personalities;
        
        /// <summary>Get a specific personality by ID</summary>
        public static MetaHumanPersonality? GetPersonality(string id)
        {
            return _personalities.TryGetValue(id.ToLower(), out var p) ? p : null;
        }
        
        /// <summary>Start the Unreal Engine MetaHuman process</summary>
        public async Task<bool> StartAsync(string? personalityId = "atlas")
        {
            if (IsRunning) return true;
            
            try
            {
                // Find UE executable
                var exePath = FindUnrealExecutable();
                if (string.IsNullOrEmpty(exePath))
                {
                    Debug.WriteLine("[MetaHuman] Unreal executable not found - running in simulation mode");
                    // Continue without UE for development
                }
                else
                {
                    // Start UE process
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"-personality={personalityId}",
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };
                    
                    _unrealProcess = Process.Start(startInfo);
                    if (_unrealProcess != null)
                    {
                        _unrealProcess.EnableRaisingEvents = true;
                        _unrealProcess.Exited += (s, e) =>
                        {
                            _isRunning = false;
                            OnUnrealStopped?.Invoke();
                        };
                    }
                }
                
                // Start pipe server for communication
                await StartPipeServerAsync();
                
                _isRunning = true;
                
                // Set initial personality
                if (!string.IsNullOrEmpty(personalityId))
                {
                    await SetPersonalityAsync(personalityId);
                }
                
                OnUnrealStarted?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MetaHuman] Start error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>Stop the MetaHuman system</summary>
        public void Stop()
        {
            _listenerCts?.Cancel();
            _pipeServer?.Dispose();
            
            if (_unrealProcess != null && !_unrealProcess.HasExited)
            {
                _unrealProcess.CloseMainWindow();
                if (!_unrealProcess.WaitForExit(3000))
                {
                    _unrealProcess.Kill();
                }
            }
            
            _isRunning = false;
            _unrealProcess = null;
            OnUnrealStopped?.Invoke();
        }
        
        /// <summary>Switch to a different personality</summary>
        public async Task<bool> SetPersonalityAsync(string personalityId)
        {
            var personality = GetPersonality(personalityId);
            if (personality == null) return false;
            
            _activePersonality = personality;
            
            // Send command to UE
            await SendCommandAsync(new MetaHumanCommand
            {
                Type = "SET_PERSONALITY",
                PersonalityId = personalityId,
                Parameters = new Dictionary<string, object>
                {
                    { "preset", personality.MetaHumanPreset },
                    { "hair", personality.HairStyle },
                    { "outfit", personality.Outfit }
                }
            });
            
            OnPersonalityChanged?.Invoke(personality);
            Debug.WriteLine($"[MetaHuman] Switched to personality: {personality.Name}");
            return true;
        }
        
        /// <summary>Set the MetaHuman's emotional/animation state</summary>
        public async Task SetStateAsync(MetaHumanState state)
        {
            await SendCommandAsync(new MetaHumanCommand
            {
                Type = "SET_STATE",
                State = state.ToString(),
                PersonalityId = _activePersonality?.Id ?? "atlas"
            });
            
            OnStateChanged?.Invoke(state);
        }
        
        /// <summary>Make the MetaHuman speak with lip-sync</summary>
        public async Task SpeakAsync(string text, string? audioFilePath = null)
        {
            await SendCommandAsync(new MetaHumanCommand
            {
                Type = "SPEAK",
                Text = text,
                AudioPath = audioFilePath ?? "",
                PersonalityId = _activePersonality?.Id ?? "atlas"
            });
        }
        
        /// <summary>Trigger a specific animation</summary>
        public async Task PlayAnimationAsync(string animationName)
        {
            await SendCommandAsync(new MetaHumanCommand
            {
                Type = "PLAY_ANIMATION",
                Parameters = new Dictionary<string, object> { { "animation", animationName } },
                PersonalityId = _activePersonality?.Id ?? "atlas"
            });
        }
        
        /// <summary>Set MetaHuman to listening mode (attentive pose)</summary>
        public async Task StartListeningAsync()
        {
            await SetStateAsync(MetaHumanState.Listening);
        }
        
        /// <summary>Set MetaHuman to thinking mode (contemplative)</summary>
        public async Task StartThinkingAsync()
        {
            await SetStateAsync(MetaHumanState.Thinking);
        }
        
        /// <summary>Return to idle state</summary>
        public async Task ReturnToIdleAsync()
        {
            await SetStateAsync(MetaHumanState.Idle);
        }
        
        /// <summary>Get the ElevenLabs voice ID for the active personality</summary>
        public string GetActiveVoiceId()
        {
            return _activePersonality?.ElevenLabsVoiceId ?? Voice.VoiceProfile.DefaultAtlasVoiceId;
        }
        
        /// <summary>Get voice settings for the active personality</summary>
        public Dictionary<string, double> GetActiveVoiceSettings()
        {
            return _activePersonality?.VoiceSettings ?? new Dictionary<string, double>
            {
                { "stability", 0.85 },
                { "similarity_boost", 0.80 },
                { "style", 0.05 }
            };
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>Send a command to Unreal Engine via named pipe</summary>
        private async Task<bool> SendCommandAsync(MetaHumanCommand command)
        {
            try
            {
                var json = JsonSerializer.Serialize(command, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                // If UE isn't running, just log the command (simulation mode)
                if (_unrealProcess == null || _unrealProcess.HasExited)
                {
                    Debug.WriteLine($"[MetaHuman] Simulated command: {command.Type} - {json}");
                    return true;
                }
                
                using var pipeClient = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out);
                await pipeClient.ConnectAsync(2000); // 2 second timeout
                
                using var writer = new StreamWriter(pipeClient);
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();
                
                Debug.WriteLine($"[MetaHuman] Sent command: {command.Type}");
                return true;
            }
            catch (TimeoutException)
            {
                Debug.WriteLine($"[MetaHuman] Pipe connection timeout - UE may not be ready");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MetaHuman] Send command error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>Start the named pipe server to receive messages from UE</summary>
        private async Task StartPipeServerAsync()
        {
            _listenerCts = new CancellationTokenSource();
            
            _ = Task.Run(async () =>
            {
                while (!_listenerCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        _pipeServer = new NamedPipeServerStream(
                            PIPE_NAME + "_Response",
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);
                        
                        await _pipeServer.WaitForConnectionAsync(_listenerCts.Token);
                        
                        using var reader = new StreamReader(_pipeServer);
                        while (_pipeServer.IsConnected && !_listenerCts.Token.IsCancellationRequested)
                        {
                            var message = await reader.ReadLineAsync();
                            if (!string.IsNullOrEmpty(message))
                            {
                                Debug.WriteLine($"[MetaHuman] Received from UE: {message}");
                                OnUnrealMessage?.Invoke(message);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MetaHuman] Pipe server error: {ex.Message}");
                        await Task.Delay(1000); // Wait before retry
                    }
                    finally
                    {
                        _pipeServer?.Dispose();
                        _pipeServer = null;
                    }
                }
            }, _listenerCts.Token);
            
            await Task.CompletedTask;
        }
        
        /// <summary>Find the Unreal Engine MetaHuman executable</summary>
        private string? FindUnrealExecutable()
        {
            string[] possiblePaths =
            {
                // Local build paths
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UE_EXE_NAME),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Unreal", UE_EXE_NAME),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MetaHuman", UE_EXE_NAME),
                Path.Combine(Directory.GetCurrentDirectory(), UE_EXE_NAME),
                
                // Development paths
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                    "Unreal Projects", "AtlasAI_MetaHuman", "Binaries", "Win64", UE_EXE_NAME),
                
                // Packaged game paths
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "AtlasAI", "MetaHuman", UE_EXE_NAME),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtlasAI", "MetaHuman", UE_EXE_NAME)
            };
            
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"[MetaHuman] Found UE executable: {path}");
                    return path;
                }
            }
            
            return null;
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            Stop();
            _listenerCts?.Dispose();
            _pipeServer?.Dispose();
            _unrealProcess?.Dispose();
        }
        
        #endregion
    }
    
    /// <summary>
    /// Extension methods for easy MetaHuman integration
    /// </summary>
    public static class MetaHumanExtensions
    {
        private static MetaHumanIntegration? _instance;
        private static readonly object _lock = new();
        
        /// <summary>Get the singleton MetaHuman integration instance</summary>
        public static MetaHumanIntegration GetMetaHumanIntegration()
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new MetaHumanIntegration();
                }
            }
            return _instance;
        }
        
        /// <summary>Start MetaHuman with default personality (Atlas)</summary>
        public static async Task StartMetaHumanAsync(string personality = "atlas")
        {
            var integration = GetMetaHumanIntegration();
            await integration.StartAsync(personality);
        }
        
        /// <summary>Stop MetaHuman system</summary>
        public static void StopMetaHuman()
        {
            _instance?.Stop();
        }
        
        /// <summary>Switch personality</summary>
        public static async Task SwitchPersonalityAsync(string personalityId)
        {
            var integration = GetMetaHumanIntegration();
            await integration.SetPersonalityAsync(personalityId);
        }
        
        /// <summary>Make MetaHuman speak</summary>
        public static async Task MetaHumanSpeakAsync(string text, string? audioPath = null)
        {
            var integration = GetMetaHumanIntegration();
            await integration.SpeakAsync(text, audioPath);
        }
        
        /// <summary>Set MetaHuman state</summary>
        public static async Task SetMetaHumanStateAsync(MetaHumanState state)
        {
            var integration = GetMetaHumanIntegration();
            await integration.SetStateAsync(state);
        }
    }
}
