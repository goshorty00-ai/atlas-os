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
    /// Records user actions and allows replay as automated tasks
    /// </summary>
    public class TaskRecorder
    {
        private readonly string _dataPath;
        private List<RecordedTask> _recordedTasks = new();
        private RecordedTask? _currentRecording;
        private DateTime _lastActionTime;
        private bool _isRecording;
        
        public event EventHandler? RecordingStarted;
        public event EventHandler<RecordedTask>? RecordingStopped;
        public event EventHandler<RecordedAction>? ActionRecorded;
        public event EventHandler<RecordedTask>? TaskReplaying;
        public event EventHandler<RecordedTask>? TaskReplayCompleted;
        
        public bool IsRecording => _isRecording;
        public IReadOnlyList<RecordedTask> RecordedTasks => _recordedTasks.AsReadOnly();
        
        // Delegate for replaying actions
        public Func<RecordedAction, Task<bool>>? ActionReplayer { get; set; }
        
        public TaskRecorder()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _dataPath = Path.Combine(appData, "AtlasAI", "recorded_tasks.json");
        }
        
        public async Task InitializeAsync()
        {
            await LoadTasksAsync();
            Debug.WriteLine($"[TaskRecorder] Loaded {_recordedTasks.Count} recorded tasks");
        }
        
        /// <summary>
        /// Start recording a new task
        /// </summary>
        public void StartRecording(string name, string? description = null)
        {
            if (_isRecording)
            {
                Debug.WriteLine("[TaskRecorder] Already recording");
                return;
            }
            
            _currentRecording = new RecordedTask
            {
                Name = name,
                Description = description ?? $"Recorded on {DateTime.Now:g}"
            };
            
            _isRecording = true;
            _lastActionTime = DateTime.Now;
            RecordingStarted?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine($"[TaskRecorder] Started recording: {name}");
        }
        
        /// <summary>
        /// Record an action during recording
        /// </summary>
        public void RecordAction(string actionType, string target, Dictionary<string, object>? parameters = null)
        {
            if (!_isRecording || _currentRecording == null) return;
            
            var now = DateTime.Now;
            var delay = now - _lastActionTime;
            
            var action = new RecordedAction
            {
                Order = _currentRecording.Actions.Count + 1,
                ActionType = actionType,
                Target = target,
                Parameters = parameters ?? new(),
                DelayBefore = delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(1) : delay
            };
            
            _currentRecording.Actions.Add(action);
            _lastActionTime = now;
            ActionRecorded?.Invoke(this, action);
            Debug.WriteLine($"[TaskRecorder] Recorded action: {actionType} -> {target}");
        }
        
        /// <summary>
        /// Stop recording and save the task
        /// </summary>
        public async Task<RecordedTask?> StopRecordingAsync()
        {
            if (!_isRecording || _currentRecording == null)
            {
                Debug.WriteLine("[TaskRecorder] Not recording");
                return null;
            }
            
            _isRecording = false;
            
            if (_currentRecording.Actions.Count == 0)
            {
                Debug.WriteLine("[TaskRecorder] No actions recorded, discarding");
                _currentRecording = null;
                return null;
            }
            
            _recordedTasks.Add(_currentRecording);
            await SaveTasksAsync();
            
            var task = _currentRecording;
            _currentRecording = null;
            
            RecordingStopped?.Invoke(this, task);
            Debug.WriteLine($"[TaskRecorder] Stopped recording: {task.Name} with {task.Actions.Count} actions");
            
            return task;
        }
        
        /// <summary>
        /// Cancel current recording
        /// </summary>
        public void CancelRecording()
        {
            _isRecording = false;
            _currentRecording = null;
            Debug.WriteLine("[TaskRecorder] Recording cancelled");
        }
        
        /// <summary>
        /// Replay a recorded task
        /// </summary>
        public async Task<bool> ReplayTaskAsync(string taskId, double speedMultiplier = 1.0)
        {
            var task = _recordedTasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null)
            {
                Debug.WriteLine($"[TaskRecorder] Task not found: {taskId}");
                return false;
            }
            
            return await ReplayTaskAsync(task, speedMultiplier);
        }
        
        /// <summary>
        /// Replay a recorded task
        /// </summary>
        public async Task<bool> ReplayTaskAsync(RecordedTask task, double speedMultiplier = 1.0)
        {
            if (!task.IsEnabled)
            {
                Debug.WriteLine($"[TaskRecorder] Task is disabled: {task.Name}");
                return false;
            }
            
            TaskReplaying?.Invoke(this, task);
            Debug.WriteLine($"[TaskRecorder] Replaying task: {task.Name}");
            
            try
            {
                foreach (var action in task.Actions.OrderBy(a => a.Order))
                {
                    // Apply delay (adjusted by speed multiplier)
                    var delay = TimeSpan.FromMilliseconds(action.DelayBefore.TotalMilliseconds / speedMultiplier);
                    if (delay > TimeSpan.Zero && delay < TimeSpan.FromSeconds(10))
                    {
                        await Task.Delay(delay);
                    }
                    
                    // Execute action
                    if (ActionReplayer != null)
                    {
                        var success = await ActionReplayer(action);
                        if (!success)
                        {
                            Debug.WriteLine($"[TaskRecorder] Action failed: {action.ActionType}");
                            return false;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[TaskRecorder] Simulating action: {action.ActionType} -> {action.Target}");
                    }
                }
                
                task.PlayCount++;
                await SaveTasksAsync();
                
                TaskReplayCompleted?.Invoke(this, task);
                Debug.WriteLine($"[TaskRecorder] Task replay completed: {task.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskRecorder] Replay error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Find task by trigger phrase
        /// </summary>
        public RecordedTask? FindByTrigger(string phrase)
        {
            return _recordedTasks.FirstOrDefault(t => 
                t.IsEnabled && 
                !string.IsNullOrEmpty(t.TriggerPhrase) &&
                phrase.ToLowerInvariant().Contains(t.TriggerPhrase.ToLowerInvariant()));
        }
        
        /// <summary>
        /// Set trigger phrase for a task
        /// </summary>
        public async Task SetTriggerPhraseAsync(string taskId, string phrase)
        {
            var task = _recordedTasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.TriggerPhrase = phrase;
                await SaveTasksAsync();
            }
        }
        
        /// <summary>
        /// Delete a recorded task
        /// </summary>
        public async Task DeleteTaskAsync(string taskId)
        {
            _recordedTasks.RemoveAll(t => t.Id == taskId);
            await SaveTasksAsync();
        }
        
        /// <summary>
        /// Enable/disable a task
        /// </summary>
        public async Task SetTaskEnabledAsync(string taskId, bool enabled)
        {
            var task = _recordedTasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.IsEnabled = enabled;
                await SaveTasksAsync();
            }
        }
        
        private async Task LoadTasksAsync()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var json = await File.ReadAllTextAsync(_dataPath);
                    _recordedTasks = JsonSerializer.Deserialize<List<RecordedTask>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskRecorder] Error loading tasks: {ex.Message}");
            }
        }
        
        private async Task SaveTasksAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_dataPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_recordedTasks, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_dataPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskRecorder] Error saving tasks: {ex.Message}");
            }
        }
    }
}
