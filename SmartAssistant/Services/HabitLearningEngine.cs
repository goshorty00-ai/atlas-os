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
    /// Learns user habits and patterns to provide proactive suggestions
    /// </summary>
    public class HabitLearningEngine
    {
        private readonly string _dataPath;
        private List<LearnedIntent> _learnedIntents = new();
        private List<UserHabit> _habits = new();
        private List<ActionRecord> _recentActions = new();
        private const int MaxRecentActions = 500;
        
        public event EventHandler<UserHabit>? HabitDetected;
        public event EventHandler<string>? SuggestionReady;
        
        public IReadOnlyList<LearnedIntent> LearnedIntents => _learnedIntents.AsReadOnly();
        public IReadOnlyList<UserHabit> Habits => _habits.AsReadOnly();
        
        public HabitLearningEngine()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _dataPath = Path.Combine(appData, "AtlasAI", "habits");
            Directory.CreateDirectory(_dataPath);
        }
        
        public async Task InitializeAsync()
        {
            await LoadDataAsync();
            Debug.WriteLine($"[HabitEngine] Loaded {_learnedIntents.Count} intents, {_habits.Count} habits");
        }
        
        /// <summary>
        /// Record a user action for pattern learning
        /// </summary>
        public async Task RecordActionAsync(string action, string input, Dictionary<string, string>? parameters = null)
        {
            var record = new ActionRecord
            {
                Action = action,
                Input = input,
                Parameters = parameters ?? new(),
                Timestamp = DateTime.Now,
                DayOfWeek = DateTime.Now.DayOfWeek,
                TimeOfDay = DateTime.Now.TimeOfDay
            };
            
            _recentActions.Add(record);
            if (_recentActions.Count > MaxRecentActions)
                _recentActions.RemoveAt(0);
            
            // Update or create learned intent
            await UpdateLearnedIntentAsync(input, action, parameters);
            
            // Analyze for habit patterns
            await AnalyzeForHabitsAsync();
            
            await SaveDataAsync();
        }
        
        /// <summary>
        /// Get suggestion based on current context
        /// </summary>
        public string? GetSuggestion(TimeSpan currentTime, DayOfWeek day)
        {
            // Find habits that typically occur around this time
            var relevantHabits = _habits
                .Where(h => h.IsConfirmed && h.TypicalTime.HasValue)
                .Where(h => Math.Abs((h.TypicalTime!.Value - currentTime).TotalMinutes) < 30)
                .Where(h => h.TypicalDays == null || h.TypicalDays.Contains(day))
                .OrderByDescending(h => h.OccurrenceCount)
                .FirstOrDefault();
            
            if (relevantHabits != null)
            {
                return $"Would you like me to {relevantHabits.Name}? You usually do this around now.";
            }
            
            return null;
        }
        
        /// <summary>
        /// Find best matching intent for user input
        /// </summary>
        public LearnedIntent? FindMatchingIntent(string input)
        {
            var normalized = input.ToLowerInvariant().Trim();
            
            return _learnedIntents
                .Where(i => CalculateSimilarity(normalized, i.Pattern.ToLowerInvariant()) > 0.7)
                .OrderByDescending(i => i.ConfidenceScore * i.UsageCount)
                .FirstOrDefault();
        }
        
        /// <summary>
        /// Confirm a detected habit
        /// </summary>
        public async Task ConfirmHabitAsync(string habitId, bool confirmed)
        {
            var habit = _habits.FirstOrDefault(h => h.Id == habitId);
            if (habit != null)
            {
                habit.IsConfirmed = confirmed;
                await SaveDataAsync();
            }
        }
        
        /// <summary>
        /// Delete a habit
        /// </summary>
        public async Task DeleteHabitAsync(string habitId)
        {
            _habits.RemoveAll(h => h.Id == habitId);
            await SaveDataAsync();
        }
        
        /// <summary>
        /// Clear all learned data
        /// </summary>
        public async Task ClearAllDataAsync()
        {
            _learnedIntents.Clear();
            _habits.Clear();
            _recentActions.Clear();
            await SaveDataAsync();
            Debug.WriteLine("[HabitEngine] All data cleared");
        }
        
        private async Task UpdateLearnedIntentAsync(string input, string action, Dictionary<string, string>? parameters)
        {
            var normalized = input.ToLowerInvariant().Trim();
            var existing = _learnedIntents.FirstOrDefault(i => 
                CalculateSimilarity(normalized, i.Pattern.ToLowerInvariant()) > 0.9);
            
            if (existing != null)
            {
                existing.UsageCount++;
                existing.LastUsed = DateTime.Now;
                existing.ConfidenceScore = Math.Min(1.0, existing.ConfidenceScore + 0.05);
            }
            else
            {
                _learnedIntents.Add(new LearnedIntent
                {
                    Pattern = normalized,
                    ResolvedAction = action,
                    UsageCount = 1,
                    LastUsed = DateTime.Now,
                    ConfidenceScore = 0.5,
                    Parameters = parameters ?? new()
                });
            }
            
            await Task.CompletedTask;
        }
        
        private async Task AnalyzeForHabitsAsync()
        {
            if (_recentActions.Count < 10) return;
            
            // Group actions by time of day (rounded to 30 min)
            var timeGroups = _recentActions
                .GroupBy(a => new { 
                    Hour = a.TimeOfDay.Hours, 
                    HalfHour = a.TimeOfDay.Minutes >= 30 ? 30 : 0,
                    a.Action 
                })
                .Where(g => g.Count() >= 3)
                .ToList();
            
            foreach (var group in timeGroups)
            {
                var avgTime = TimeSpan.FromMinutes(
                    group.Average(a => a.TimeOfDay.TotalMinutes));
                var days = group.Select(a => a.DayOfWeek).Distinct().ToArray();
                
                var existingHabit = _habits.FirstOrDefault(h => 
                    h.ActionSequence.Contains(group.Key.Action) &&
                    h.TypicalTime.HasValue &&
                    Math.Abs((h.TypicalTime.Value - avgTime).TotalMinutes) < 60);
                
                if (existingHabit != null)
                {
                    existingHabit.OccurrenceCount = group.Count();
                    existingHabit.LastObserved = DateTime.Now;
                    existingHabit.TypicalDays = days;
                }
                else if (group.Count() >= 5)
                {
                    var newHabit = new UserHabit
                    {
                        Name = $"Run '{group.Key.Action}'",
                        Description = $"You often do this around {avgTime:hh\\:mm}",
                        TypicalTime = avgTime,
                        TypicalDays = days,
                        ActionSequence = new List<string> { group.Key.Action },
                        OccurrenceCount = group.Count(),
                        FirstObserved = group.Min(a => a.Timestamp),
                        LastObserved = DateTime.Now,
                        IsConfirmed = false
                    };
                    
                    _habits.Add(newHabit);
                    HabitDetected?.Invoke(this, newHabit);
                    Debug.WriteLine($"[HabitEngine] New habit detected: {newHabit.Name}");
                }
            }
            
            await Task.CompletedTask;
        }
        
        private double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;
            if (s1 == s2) return 1.0;
            
            var words1 = s1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words2 = s2.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            var commonWords = words1.Intersect(words2, StringComparer.OrdinalIgnoreCase).Count();
            var totalWords = Math.Max(words1.Length, words2.Length);
            
            return totalWords > 0 ? (double)commonWords / totalWords : 0;
        }
        
        private async Task LoadDataAsync()
        {
            try
            {
                var intentsPath = Path.Combine(_dataPath, "intents.json");
                if (File.Exists(intentsPath))
                {
                    var json = await File.ReadAllTextAsync(intentsPath);
                    _learnedIntents = JsonSerializer.Deserialize<List<LearnedIntent>>(json) ?? new();
                }
                
                var habitsPath = Path.Combine(_dataPath, "habits.json");
                if (File.Exists(habitsPath))
                {
                    var json = await File.ReadAllTextAsync(habitsPath);
                    _habits = JsonSerializer.Deserialize<List<UserHabit>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HabitEngine] Error loading data: {ex.Message}");
            }
        }
        
        private async Task SaveDataAsync()
        {
            try
            {
                var intentsPath = Path.Combine(_dataPath, "intents.json");
                var intentsJson = JsonSerializer.Serialize(_learnedIntents, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(intentsPath, intentsJson);
                
                var habitsPath = Path.Combine(_dataPath, "habits.json");
                var habitsJson = JsonSerializer.Serialize(_habits, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(habitsPath, habitsJson);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HabitEngine] Error saving data: {ex.Message}");
            }
        }
        
        private class ActionRecord
        {
            public string Action { get; set; } = "";
            public string Input { get; set; } = "";
            public Dictionary<string, string> Parameters { get; set; } = new();
            public DateTime Timestamp { get; set; }
            public DayOfWeek DayOfWeek { get; set; }
            public TimeSpan TimeOfDay { get; set; }
        }
    }
}
