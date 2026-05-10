using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.SmartAssistant.Models;
using AtlasAI.SmartAssistant.Services;
using AtlasAI.SmartAssistant.UI;

namespace AtlasAI.SmartAssistant
{
    /// <summary>
    /// Central manager for all smart assistant features
    /// </summary>
    public class SmartAssistantManager
    {
        private static SmartAssistantManager? _instance;
        public static SmartAssistantManager Instance => _instance ??= new SmartAssistantManager();
        
        // Services
        public HabitLearningEngine HabitEngine { get; }
        public MultiStepTaskExecutor TaskExecutor { get; }
        public TaskRecorder TaskRecorder { get; }
        public Services.TaskScheduler TaskScheduler { get; }
        public PersonaManager PersonaManager { get; }
        public PrivacyDashboard PrivacyDashboard { get; }
        public ProjectContextAnalyzer ProjectAnalyzer { get; }
        
        // UI
        public FloatingOverlay? FloatingOverlay { get; private set; }
        
        // Events
        public event EventHandler<string>? QuickCommandReceived;
        public event EventHandler<string>? SuggestionReady;
        public event EventHandler<UserHabit>? HabitSuggestion;
        public event EventHandler<DetectedError>? ErrorDetected;
        
        // Delegate for executing commands
        public Func<string, Task<string>>? CommandExecutor { get; set; }
        
        private bool _isInitialized;
        
        private SmartAssistantManager()
        {
            HabitEngine = new HabitLearningEngine();
            TaskExecutor = new MultiStepTaskExecutor();
            TaskRecorder = new TaskRecorder();
            TaskScheduler = new Services.TaskScheduler();
            PersonaManager = new PersonaManager();
            PrivacyDashboard = new PrivacyDashboard();
            ProjectAnalyzer = new ProjectContextAnalyzer();
            
            WireUpEvents();
        }
        
        /// <summary>
        /// Initialize all smart assistant services
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;
            
            Debug.WriteLine("[SmartAssistant] Initializing...");
            
            try
            {
                await PrivacyDashboard.InitializeAsync();
                await HabitEngine.InitializeAsync();
                await TaskRecorder.InitializeAsync();
                await TaskScheduler.InitializeAsync();
                await PersonaManager.InitializeAsync();
                
                // Start scheduler
                TaskScheduler.Start();
                
                // Wire up command executor
                TaskExecutor.ActionExecutor = async (action, parameters) =>
                {
                    if (CommandExecutor != null)
                    {
                        return await CommandExecutor(action);
                    }
                    return $"Executed: {action}";
                };
                
                TaskScheduler.TaskExecutor = async (command) =>
                {
                    if (CommandExecutor != null)
                    {
                        return await CommandExecutor(command);
                    }
                    return $"Executed: {command}";
                };
                
                TaskRecorder.ActionReplayer = async (action) =>
                {
                    if (CommandExecutor != null)
                    {
                        await CommandExecutor($"{action.ActionType} {action.Target}");
                        return true;
                    }
                    return true;
                };
                
                _isInitialized = true;
                Debug.WriteLine("[SmartAssistant] Initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SmartAssistant] Initialization error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initialize the floating overlay (must be called from UI thread)
        /// </summary>
        public void InitializeOverlay()
        {
            if (FloatingOverlay != null) return;
            
            FloatingOverlay = new FloatingOverlay();
            FloatingOverlay.CommandSubmitted += (s, command) =>
            {
                QuickCommandReceived?.Invoke(this, command);
            };
            
            Debug.WriteLine("[SmartAssistant] Floating overlay initialized");
        }
        
        /// <summary>
        /// Process a user command with smart features
        /// </summary>
        public async Task<SmartCommandResult> ProcessCommandAsync(string input)
        {
            var result = new SmartCommandResult { OriginalInput = input };
            
            // Check privacy
            if (!PrivacyDashboard.IsAllowed("data"))
            {
                result.Response = "Data collection is disabled in privacy settings.";
                return result;
            }
            
            // Detect mood
            var mood = PersonaManager.DetectMood(input);
            result.DetectedMood = mood.DetectedMood;
            
            // Check for recorded task trigger
            var recordedTask = TaskRecorder.FindByTrigger(input);
            if (recordedTask != null)
            {
                result.MatchedRecordedTask = recordedTask;
                result.SuggestedAction = $"Run recorded task: {recordedTask.Name}";
            }
            
            // Check for learned intent
            var learnedIntent = HabitEngine.FindMatchingIntent(input);
            if (learnedIntent != null && learnedIntent.ConfidenceScore > 0.8)
            {
                result.MatchedIntent = learnedIntent;
            }
            
            // Get persona context
            result.PersonaContext = PersonaManager.GetSystemPromptAddition();
            
            // Record action for learning
            if (PrivacyDashboard.IsAllowed("habit"))
            {
                await HabitEngine.RecordActionAsync("command", input);
            }
            
            // Log privacy audit
            await PrivacyDashboard.LogAccessAsync("command", "process", input, true);
            
            return result;
        }
        
        /// <summary>
        /// Create and execute a multi-step task
        /// </summary>
        public async Task<bool> ExecuteMultiStepTaskAsync(string name, List<(string action, string description)> steps)
        {
            var taskSteps = new List<(string action, string desc, Dictionary<string, object>? parameters)>();
            foreach (var (action, desc) in steps)
            {
                taskSteps.Add((action, desc, null));
            }
            
            var plan = TaskExecutor.CreatePlan(name, $"Multi-step task: {name}", taskSteps);
            return await TaskExecutor.ExecutePlanAsync(plan);
        }
        
        /// <summary>
        /// Start recording a task
        /// </summary>
        public void StartRecording(string name)
        {
            TaskRecorder.StartRecording(name);
        }
        
        /// <summary>
        /// Stop recording and save the task
        /// </summary>
        public async Task<RecordedTask?> StopRecordingAsync()
        {
            return await TaskRecorder.StopRecordingAsync();
        }
        
        /// <summary>
        /// Record an action during recording
        /// </summary>
        public void RecordAction(string actionType, string target)
        {
            TaskRecorder.RecordAction(actionType, target);
        }
        
        /// <summary>
        /// Schedule a task
        /// </summary>
        public async Task<ScheduledTask> ScheduleTaskAsync(
            string name, 
            string command, 
            ScheduleType scheduleType,
            TimeSpan? timeOfDay = null)
        {
            return await TaskScheduler.AddTaskAsync(name, command, scheduleType, timeOfDay);
        }
        
        /// <summary>
        /// Switch assistant persona
        /// </summary>
        public async Task SwitchPersonaAsync(string personaName)
        {
            var persona = PersonaManager.Personas
                .FirstOrDefault(p => p.Name.Equals(personaName, StringComparison.OrdinalIgnoreCase));
            
            if (persona != null)
            {
                await PersonaManager.SwitchPersonaAsync(persona.Id);
            }
        }
        
        /// <summary>
        /// Analyze a project directory
        /// </summary>
        public async Task<ProjectContext?> AnalyzeProjectAsync(string path)
        {
            return await ProjectAnalyzer.AnalyzeDirectoryAsync(path);
        }
        
        /// <summary>
        /// Get current suggestion based on time and habits
        /// </summary>
        public string? GetCurrentSuggestion()
        {
            return HabitEngine.GetSuggestion(DateTime.Now.TimeOfDay, DateTime.Now.DayOfWeek);
        }
        
        /// <summary>
        /// Get privacy summary
        /// </summary>
        public PrivacySummary GetPrivacySummary()
        {
            return PrivacyDashboard.GetSummary();
        }
        
        /// <summary>
        /// Show the floating overlay
        /// </summary>
        public void ShowOverlay()
        {
            FloatingOverlay?.ShowOverlay();
        }
        
        /// <summary>
        /// Hide the floating overlay
        /// </summary>
        public void HideOverlay()
        {
            FloatingOverlay?.HideOverlay();
        }
        
        /// <summary>
        /// Shutdown all services
        /// </summary>
        public void Shutdown()
        {
            TaskScheduler.Stop();
            ProjectAnalyzer.StopWatching();
            FloatingOverlay?.UnregisterGlobalHotkey();
            FloatingOverlay?.Close();
            Debug.WriteLine("[SmartAssistant] Shutdown complete");
        }
        
        private void WireUpEvents()
        {
            HabitEngine.HabitDetected += (s, habit) =>
            {
                HabitSuggestion?.Invoke(this, habit);
            };
            
            HabitEngine.SuggestionReady += (s, suggestion) =>
            {
                SuggestionReady?.Invoke(this, suggestion);
            };
            
            ProjectAnalyzer.ErrorDetected += (s, error) =>
            {
                ErrorDetected?.Invoke(this, error);
            };
            
            TaskScheduler.TaskDue += async (s, task) =>
            {
                Debug.WriteLine($"[SmartAssistant] Scheduled task due: {task.Name}");
                await Task.CompletedTask;
            };
        }
    }
    
    public class SmartCommandResult
    {
        public string OriginalInput { get; set; } = "";
        public string? Response { get; set; }
        public UserMood DetectedMood { get; set; }
        public LearnedIntent? MatchedIntent { get; set; }
        public RecordedTask? MatchedRecordedTask { get; set; }
        public string? SuggestedAction { get; set; }
        public string? PersonaContext { get; set; }
    }
}
