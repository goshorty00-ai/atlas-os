using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.SmartAssistant.Models;

namespace AtlasAI.SmartAssistant.Services
{
    /// <summary>
    /// Manages scheduled background tasks
    /// </summary>
    public class TaskScheduler
    {
        private readonly string _dataPath;
        private List<ScheduledTask> _tasks = new();
        private Timer? _checkTimer;
        private bool _isRunning;
        
        public event EventHandler<ScheduledTask>? TaskDue;
        public event EventHandler<ScheduledTask>? TaskCompleted;
        public event EventHandler<(ScheduledTask Task, string Error)>? TaskFailed;
        
        public IReadOnlyList<ScheduledTask> Tasks => _tasks.AsReadOnly();
        
        // Delegate for executing scheduled tasks
        public Func<string, Task<string>>? TaskExecutor { get; set; }
        
        public TaskScheduler()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _dataPath = Path.Combine(appData, "AtlasAI", "scheduled_tasks.json");
        }
        
        public async Task InitializeAsync()
        {
            await LoadTasksAsync();
            Debug.WriteLine($"[Scheduler] Loaded {_tasks.Count} scheduled tasks");
        }
        
        /// <summary>
        /// Start the scheduler
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _checkTimer = new Timer(CheckTasks, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            
            // Run startup tasks
            _ = RunStartupTasksAsync();
            
            Debug.WriteLine("[Scheduler] Started");
        }
        
        /// <summary>
        /// Stop the scheduler
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _checkTimer?.Dispose();
            _checkTimer = null;
            Debug.WriteLine("[Scheduler] Stopped");
        }
        
        /// <summary>
        /// Add a new scheduled task
        /// </summary>
        public async Task<ScheduledTask> AddTaskAsync(
            string name,
            string command,
            ScheduleType scheduleType,
            TimeSpan? timeOfDay = null,
            DayOfWeek[]? daysOfWeek = null,
            TimeSpan? interval = null)
        {
            var task = new ScheduledTask
            {
                Name = name,
                ActionCommand = command,
                ScheduleType = scheduleType,
                TimeOfDay = timeOfDay,
                DaysOfWeek = daysOfWeek,
                Interval = interval
            };
            
            task.NextRunTime = CalculateNextRunTime(task);
            
            _tasks.Add(task);
            await SaveTasksAsync();
            
            Debug.WriteLine($"[Scheduler] Added task: {name}, next run: {task.NextRunTime}");
            return task;
        }
        
        /// <summary>
        /// Remove a scheduled task
        /// </summary>
        public async Task RemoveTaskAsync(string taskId)
        {
            _tasks.RemoveAll(t => t.Id == taskId);
            await SaveTasksAsync();
        }
        
        /// <summary>
        /// Enable/disable a task
        /// </summary>
        public async Task SetTaskEnabledAsync(string taskId, bool enabled)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.IsEnabled = enabled;
                if (enabled)
                {
                    task.NextRunTime = CalculateNextRunTime(task);
                }
                await SaveTasksAsync();
            }
        }
        
        /// <summary>
        /// Run a task immediately
        /// </summary>
        public async Task<bool> RunTaskNowAsync(string taskId)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return false;
            
            return await ExecuteTaskAsync(task);
        }
        
        /// <summary>
        /// Get upcoming tasks
        /// </summary>
        public List<ScheduledTask> GetUpcomingTasks(int count = 10)
        {
            return _tasks
                .Where(t => t.IsEnabled && t.NextRunTime.HasValue)
                .OrderBy(t => t.NextRunTime)
                .Take(count)
                .ToList();
        }
        
        private void CheckTasks(object? state)
        {
            if (!_isRunning) return;
            
            var now = DateTime.Now;
            var dueTasks = _tasks
                .Where(t => t.IsEnabled && t.NextRunTime.HasValue && t.NextRunTime <= now)
                .ToList();
            
            foreach (var task in dueTasks)
            {
                _ = ExecuteTaskAsync(task);
            }
        }
        
        private async Task RunStartupTasksAsync()
        {
            var startupTasks = _tasks.Where(t => t.IsEnabled && t.ScheduleType == ScheduleType.OnStartup);
            foreach (var task in startupTasks)
            {
                await ExecuteTaskAsync(task);
            }
        }
        
        private async Task<bool> ExecuteTaskAsync(ScheduledTask task)
        {
            Debug.WriteLine($"[Scheduler] Executing task: {task.Name}");
            TaskDue?.Invoke(this, task);
            
            try
            {
                if (TaskExecutor != null)
                {
                    await TaskExecutor(task.ActionCommand);
                }
                
                task.LastRunTime = DateTime.Now;
                task.NextRunTime = CalculateNextRunTime(task);
                await SaveTasksAsync();
                
                TaskCompleted?.Invoke(this, task);
                Debug.WriteLine($"[Scheduler] Task completed: {task.Name}");
                return true;
            }
            catch (Exception ex)
            {
                TaskFailed?.Invoke(this, (task, ex.Message));
                Debug.WriteLine($"[Scheduler] Task failed: {task.Name} - {ex.Message}");
                return false;
            }
        }
        
        private DateTime? CalculateNextRunTime(ScheduledTask task)
        {
            var now = DateTime.Now;
            
            switch (task.ScheduleType)
            {
                case ScheduleType.Once:
                    if (task.TimeOfDay.HasValue)
                    {
                        var runTime = now.Date.Add(task.TimeOfDay.Value);
                        return runTime > now ? runTime : (DateTime?)null;
                    }
                    return null;
                    
                case ScheduleType.Daily:
                    if (task.TimeOfDay.HasValue)
                    {
                        var runTime = now.Date.Add(task.TimeOfDay.Value);
                        return runTime > now ? runTime : runTime.AddDays(1);
                    }
                    return now.AddDays(1);
                    
                case ScheduleType.Weekly:
                    if (task.DaysOfWeek != null && task.DaysOfWeek.Length > 0 && task.TimeOfDay.HasValue)
                    {
                        for (int i = 0; i < 7; i++)
                        {
                            var checkDate = now.AddDays(i);
                            if (task.DaysOfWeek.Contains(checkDate.DayOfWeek))
                            {
                                var runTime = checkDate.Date.Add(task.TimeOfDay.Value);
                                if (runTime > now) return runTime;
                            }
                        }
                    }
                    return now.AddDays(7);
                    
                case ScheduleType.Interval:
                    if (task.Interval.HasValue)
                    {
                        return (task.LastRunTime ?? now).Add(task.Interval.Value);
                    }
                    return now.AddHours(1);
                    
                case ScheduleType.OnStartup:
                    return null; // Runs on startup only
                    
                case ScheduleType.OnIdle:
                    return null; // Triggered by idle detection
                    
                default:
                    return null;
            }
        }
        
        private async Task LoadTasksAsync()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var json = await File.ReadAllTextAsync(_dataPath);
                    _tasks = JsonSerializer.Deserialize<List<ScheduledTask>>(json) ?? new();
                    
                    // Recalculate next run times
                    foreach (var task in _tasks.Where(t => t.IsEnabled))
                    {
                        task.NextRunTime = CalculateNextRunTime(task);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Scheduler] Error loading tasks: {ex.Message}");
            }
        }
        
        private async Task SaveTasksAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_dataPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_tasks, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_dataPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Scheduler] Error saving tasks: {ex.Message}");
            }
        }
    }
}
