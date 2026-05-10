using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Win32.TaskScheduler;
using AtlasAI.Ledger;

namespace AtlasAI.SecuritySuite.Services
{
    /// <summary>
    /// Watches Windows Task Scheduler for task creation, modification, and deletion.
    /// </summary>
    public class ScheduledTaskWatcher : IDisposable
    {
        private static readonly Lazy<ScheduledTaskWatcher> _instance = new(() => new ScheduledTaskWatcher());
        public static ScheduledTaskWatcher Instance => _instance.Value;

        private Timer? _pollTimer;
        private readonly object _lock = new();
        private bool _isDisposed;
        private bool _isInitialized;

        // Baseline snapshot
        private Dictionary<string, TaskSnapshot> _baseline = new();

        private const int PollIntervalMs = 6000; // 6 seconds

        public event Action<string>? StatusChanged;

        private ScheduledTaskWatcher() { }

        public void Start()
        {
            if (_isInitialized) return;

            try
            {
                // Capture baseline
                CaptureBaseline();

                // Start polling timer
                _pollTimer = new Timer(OnPollTick, null, PollIntervalMs, PollIntervalMs);

                _isInitialized = true;
                StatusChanged?.Invoke("Scheduled task watcher started");
                Debug.WriteLine($"[TaskWatcher] Started monitoring {_baseline.Count} scheduled tasks");

                // Add initial ledger event
                var initEvent = new LedgerEvent
                {
                    Category = LedgerCategory.ScheduledTask,
                    Severity = LedgerSeverity.Info,
                    Title = "Task Scheduler monitoring active",
                    WhyItMatters = "Atlas is watching for new or modified scheduled tasks."
                };
                initEvent.WithEvidence("Tasks Monitored", $"{_baseline.Count} tasks")
                         .WithAction(LedgerAction.Dismiss("Got it"));

                LedgerManager.Instance.AddEvent(initEvent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskWatcher] Failed to start: {ex.Message}");
                StatusChanged?.Invoke($"Failed to start task watcher: {ex.Message}");
            }
        }

        public void Stop()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            _isInitialized = false;
            StatusChanged?.Invoke("Scheduled task watcher stopped");
        }

        private void CaptureBaseline()
        {
            _baseline = GetAllTasks();
        }

        private void OnPollTick(object? state)
        {
            if (_isDisposed) return;

            lock (_lock)
            {
                try
                {
                    CheckForChanges();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TaskWatcher] Poll error: {ex.Message}");
                }
            }
        }

        private void CheckForChanges()
        {
            var current = GetAllTasks();

            // Check for added tasks
            foreach (var kvp in current)
            {
                if (!_baseline.TryGetValue(kvp.Key, out var oldTask))
                {
                    // New task created
                    CreateTaskAddedEvent(kvp.Value);
                }
                else if (HasTaskChanged(oldTask, kvp.Value))
                {
                    // Task modified
                    CreateTaskModifiedEvent(oldTask, kvp.Value);
                }
            }

            // Check for deleted tasks
            foreach (var kvp in _baseline)
            {
                if (!current.ContainsKey(kvp.Key))
                {
                    CreateTaskDeletedEvent(kvp.Value);
                }
            }

            _baseline = current;
        }

        private bool HasTaskChanged(TaskSnapshot old, TaskSnapshot current)
        {
            return old.IsEnabled != current.IsEnabled ||
                   old.ActionCommand != current.ActionCommand ||
                   old.TriggerSummary != current.TriggerSummary;
        }

        private Dictionary<string, TaskSnapshot> GetAllTasks()
        {
            var tasks = new Dictionary<string, TaskSnapshot>();

            try
            {
                using var ts = new TaskService();
                EnumerateTasks(ts.RootFolder, tasks);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskWatcher] Error enumerating tasks: {ex.Message}");
            }

            return tasks;
        }

        private void EnumerateTasks(TaskFolder folder, Dictionary<string, TaskSnapshot> tasks)
        {
            try
            {
                foreach (var task in folder.Tasks)
                {
                    try
                    {
                        var snapshot = CreateSnapshot(task);
                        tasks[snapshot.FullPath] = snapshot;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TaskWatcher] Error reading task {task.Name}: {ex.Message}");
                    }
                }

                foreach (var subFolder in folder.SubFolders)
                {
                    EnumerateTasks(subFolder, tasks);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskWatcher] Error enumerating folder {folder.Path}: {ex.Message}");
            }
        }

        private TaskSnapshot CreateSnapshot(Microsoft.Win32.TaskScheduler.Task task)
        {
            var actionCommand = "";
            var actionArgs = "";

            if (task.Definition.Actions.Count > 0)
            {
                var action = task.Definition.Actions[0];
                if (action is ExecAction execAction)
                {
                    actionCommand = execAction.Path ?? "";
                    actionArgs = execAction.Arguments ?? "";
                }
                else
                {
                    actionCommand = action.ActionType.ToString();
                }
            }

            var triggerSummary = "";
            if (task.Definition.Triggers.Count > 0)
            {
                var trigger = task.Definition.Triggers[0];
                triggerSummary = GetTriggerSummary(trigger);
            }

            return new TaskSnapshot
            {
                Name = task.Name,
                FullPath = task.Path,
                FolderPath = task.Folder.Path,
                IsEnabled = task.Enabled,
                ActionCommand = actionCommand,
                ActionArguments = actionArgs,
                TriggerSummary = triggerSummary,
                Principal = task.Definition.Principal?.UserId ?? "Unknown",
                LastRunTime = task.LastRunTime,
                NextRunTime = task.NextRunTime
            };
        }

        private string GetTriggerSummary(Trigger trigger)
        {
            return trigger switch
            {
                TimeTrigger t => $"At {t.StartBoundary:g}",
                DailyTrigger d => $"Daily at {d.StartBoundary:t}",
                WeeklyTrigger w => $"Weekly on {w.DaysOfWeek}",
                MonthlyTrigger m => $"Monthly on day {string.Join(",", m.DaysOfMonth)}",
                LogonTrigger => "At logon",
                BootTrigger => "At startup",
                IdleTrigger => "When idle",
                EventTrigger e => $"On event: {e.Subscription}",
                _ => trigger.TriggerType.ToString()
            };
        }

        #region Event Creation

        private void CreateTaskAddedEvent(TaskSnapshot task)
        {
            Debug.WriteLine($"[TaskWatcher] Task created: {task.FullPath}");

            var evt = new LedgerEvent
            {
                Category = LedgerCategory.ScheduledTask,
                Severity = LedgerSeverity.High,
                Title = "Scheduled task created",
                WhyItMatters = "Scheduled tasks are commonly used for persistence and background execution. Verify this task is expected."
            };

            evt.WithEvidence("Task Name", task.Name)
               .WithEvidence("Path", task.FullPath)
               .WithEvidence("Action", string.IsNullOrEmpty(task.ActionArguments) 
                   ? task.ActionCommand 
                   : $"{task.ActionCommand} {task.ActionArguments}")
               .WithEvidence("Trigger", task.TriggerSummary)
               .WithEvidence("Enabled", task.IsEnabled ? "Yes" : "No")
               .WithEvidence("Run As", task.Principal)
               .WithEvidence("Detected", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            evt.BackupData = System.Text.Json.JsonSerializer.Serialize(new
            {
                Type = "TaskAdd",
                task.Name,
                task.FullPath,
                task.FolderPath
            });

            evt.WithAction(new LedgerAction
            {
                Label = "⏸️ Disable",
                Type = LedgerActionType.Block,
                Data = evt.BackupData,
                RequiresConfirmation = false
            });
            evt.WithAction(new LedgerAction
            {
                Label = "🗑️ Delete",
                Type = LedgerActionType.Delete,
                Data = evt.BackupData,
                RequiresConfirmation = true
            });
            evt.WithAction(LedgerAction.Dismiss("✓ Allow"));

            LedgerManager.Instance.AddEvent(evt);
            StatusChanged?.Invoke($"Scheduled task created: {task.Name}");
        }

        private void CreateTaskModifiedEvent(TaskSnapshot oldTask, TaskSnapshot newTask)
        {
            Debug.WriteLine($"[TaskWatcher] Task modified: {newTask.FullPath}");

            var evt = new LedgerEvent
            {
                Category = LedgerCategory.ScheduledTask,
                Severity = LedgerSeverity.Medium,
                Title = "Scheduled task modified",
                WhyItMatters = "An existing scheduled task was changed. Verify this modification is expected."
            };

            evt.WithEvidence("Task Name", newTask.Name)
               .WithEvidence("Path", newTask.FullPath);

            if (oldTask.IsEnabled != newTask.IsEnabled)
                evt.WithEvidence("Enabled Changed", $"{oldTask.IsEnabled} → {newTask.IsEnabled}");

            if (oldTask.ActionCommand != newTask.ActionCommand)
                evt.WithEvidence("Action Changed", $"{oldTask.ActionCommand} → {newTask.ActionCommand}");

            if (oldTask.TriggerSummary != newTask.TriggerSummary)
                evt.WithEvidence("Trigger Changed", $"{oldTask.TriggerSummary} → {newTask.TriggerSummary}");

            evt.WithEvidence("Detected", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            evt.BackupData = System.Text.Json.JsonSerializer.Serialize(new
            {
                Type = "TaskModify",
                newTask.Name,
                newTask.FullPath,
                newTask.FolderPath,
                OldEnabled = oldTask.IsEnabled,
                NewEnabled = newTask.IsEnabled
            });

            evt.WithAction(new LedgerAction
            {
                Label = "⏸️ Disable",
                Type = LedgerActionType.Block,
                Data = evt.BackupData,
                RequiresConfirmation = false
            });
            evt.WithAction(LedgerAction.Dismiss("✓ Allow"));

            LedgerManager.Instance.AddEvent(evt);
            StatusChanged?.Invoke($"Scheduled task modified: {newTask.Name}");
        }

        private void CreateTaskDeletedEvent(TaskSnapshot task)
        {
            Debug.WriteLine($"[TaskWatcher] Task deleted: {task.FullPath}");

            var evt = new LedgerEvent
            {
                Category = LedgerCategory.ScheduledTask,
                Severity = LedgerSeverity.Low,
                Title = "Scheduled task deleted",
                WhyItMatters = "A scheduled task was removed from the system."
            };

            evt.WithEvidence("Task Name", task.Name)
               .WithEvidence("Path", task.FullPath)
               .WithEvidence("Action Was", task.ActionCommand)
               .WithEvidence("Detected", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            evt.WithAction(LedgerAction.Dismiss("✓ Got it"));

            LedgerManager.Instance.AddEvent(evt);
            StatusChanged?.Invoke($"Scheduled task deleted: {task.Name}");
        }

        #endregion

        #region Revert Actions

        /// <summary>
        /// Disable a scheduled task
        /// </summary>
        public (bool Success, string Message) DisableTask(string taskPath)
        {
            try
            {
                using var ts = new TaskService();
                var task = ts.GetTask(taskPath);

                if (task == null)
                    return (false, "Task not found");

                task.Enabled = false;

                // Create follow-up event
                var evt = new LedgerEvent
                {
                    Category = LedgerCategory.ScheduledTask,
                    Severity = LedgerSeverity.Info,
                    Title = "Scheduled task disabled",
                    WhyItMatters = "The scheduled task was successfully disabled and will no longer run."
                };
                evt.WithEvidence("Task Name", task.Name)
                   .WithEvidence("Path", taskPath)
                   .WithEvidence("Disabled At", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                   .WithAction(LedgerAction.Dismiss("Got it"));

                LedgerManager.Instance.AddEvent(evt);

                return (true, "✅ Task disabled");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "⚠️ Administrator privileges required to disable this task");
            }
            catch (Exception ex)
            {
                return (false, $"❌ Failed to disable task: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete a scheduled task
        /// </summary>
        public (bool Success, string Message) DeleteTask(string taskPath, string folderPath)
        {
            try
            {
                using var ts = new TaskService();
                var folder = ts.GetFolder(folderPath);

                if (folder == null)
                    return (false, "Task folder not found");

                var taskName = taskPath.Split('\\').Last();
                folder.DeleteTask(taskName, exceptionOnNotExists: false);

                // Create follow-up event
                var evt = new LedgerEvent
                {
                    Category = LedgerCategory.ScheduledTask,
                    Severity = LedgerSeverity.Info,
                    Title = "Scheduled task deleted",
                    WhyItMatters = "The scheduled task was successfully deleted from the system."
                };
                evt.WithEvidence("Task Name", taskName)
                   .WithEvidence("Path", taskPath)
                   .WithEvidence("Deleted At", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                   .WithAction(LedgerAction.Dismiss("Got it"));

                LedgerManager.Instance.AddEvent(evt);

                return (true, "✅ Task deleted");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "⚠️ Administrator privileges required to delete this task");
            }
            catch (Exception ex)
            {
                return (false, $"❌ Failed to delete task: {ex.Message}");
            }
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
            GC.SuppressFinalize(this);
        }

        #region Data Classes

        private class TaskSnapshot
        {
            public string Name { get; set; } = "";
            public string FullPath { get; set; } = "";
            public string FolderPath { get; set; } = "";
            public bool IsEnabled { get; set; }
            public string ActionCommand { get; set; } = "";
            public string ActionArguments { get; set; } = "";
            public string TriggerSummary { get; set; } = "";
            public string Principal { get; set; } = "";
            public DateTime LastRunTime { get; set; }
            public DateTime NextRunTime { get; set; }
        }

        #endregion
    }
}
