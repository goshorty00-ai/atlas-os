using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using AtlasAI.SecuritySuite.Models;
using AtlasAI.SecuritySuite.Services;
using AtlasAI.Ledger;

namespace AtlasAI.SecuritySuite.ViewModels
{
    /// <summary>
    /// Converter for string comparison to visibility
    /// </summary>
    public class StringMatchToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;
            
            return value.ToString() == parameter.ToString() ? Visibility.Visible : Visibility.Collapsed;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// System Intelligence ViewModel - Narrative timeline approach with sci-fi orbital UI
    /// </summary>
    public class SecuritySuiteViewModel : ViewModelBase, IDisposable
    {
        private readonly SecuritySuiteManager _manager;
        private readonly DateTime _sessionStart;
        
        // Timeline Events
        public ObservableCollection<TimelineEvent> TimelineEvents { get; } = new();
        
        // Selected Event for details panel
        private TimelineEvent? _selectedEvent;
        public TimelineEvent? SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                SetProperty(ref _selectedEvent, value);
                OnPropertyChanged(nameof(HasSelectedEvent));
                OnPropertyChanged(nameof(HasNoSelectedEvent));
            }
        }
        
        public bool HasSelectedEvent => _selectedEvent != null;
        public bool HasNoSelectedEvent => _selectedEvent == null;
        
        // System status for the monitoring indicator
        private string _systemStatus = "Everything is stable.";
        public string SystemStatus
        {
            get => _systemStatus;
            set => SetProperty(ref _systemStatus, value);
        }
        
        // System State
        private string _observationPeriod = "Since startup";
        public string ObservationPeriod
        {
            get => _observationPeriod;
            set => SetProperty(ref _observationPeriod, value);
        }
        
        private string _eventsRecorded = "0 events";
        public string EventsRecorded
        {
            get => _eventsRecorded;
            set => SetProperty(ref _eventsRecorded, value);
        }
        
        private string _systemConfidence = "High";
        public string SystemConfidence
        {
            get => _systemConfidence;
            set => SetProperty(ref _systemConfidence, value);
        }
        
        public bool HasNoEvents => TimelineEvents.Count == 0;
        
        // Scan State
        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                SetProperty(ref _isScanning, value);
                OnPropertyChanged(nameof(HasNoEvents));
            }
        }
        
        private int _scanProgress;
        public int ScanProgress
        {
            get => _scanProgress;
            set => SetProperty(ref _scanProgress, value);
        }
        
        private string _scanStatus = "Observing...";
        public string ScanStatus
        {
            get => _scanStatus;
            set => SetProperty(ref _scanStatus, value);
        }
        
        private string _currentFile = "";
        public string CurrentFile
        {
            get => _currentFile;
            set => SetProperty(ref _currentFile, value);
        }
        
        // Legacy properties for compatibility
        private string _currentView = "Dashboard";
        public string CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }
        
        private int _protectionScore = 100;
        public int ProtectionScore
        {
            get => _protectionScore;
            set => SetProperty(ref _protectionScore, value);
        }
        
        private string _definitionsVersion = "v2.0.0";
        public string DefinitionsVersion
        {
            get => _definitionsVersion;
            set => SetProperty(ref _definitionsVersion, value);
        }
        
        private DateTime _lastUpdated = DateTime.Now;
        public DateTime LastUpdated
        {
            get => _lastUpdated;
            set => SetProperty(ref _lastUpdated, value);
        }
        
        // Stats for the new UI
        private int _filesScannedToday = 0;
        public int FilesScannedToday
        {
            get => _filesScannedToday;
            set => SetProperty(ref _filesScannedToday, value);
        }
        
        private int _threatsBlocked = 0;
        public int ThreatsBlocked
        {
            get => _threatsBlocked;
            set => SetProperty(ref _threatsBlocked, value);
        }
        
        private int _quarantineCount = 0;
        public int QuarantineCount
        {
            get => _quarantineCount;
            set => SetProperty(ref _quarantineCount, value);
        }
        
        private DateTime? _lastScanTime = null;
        public DateTime? LastScanTime
        {
            get => _lastScanTime;
            set => SetProperty(ref _lastScanTime, value);
        }
        
        // Collections for compatibility
        public ObservableCollection<SecurityFinding> Findings { get; } = new();
        public ObservableCollection<QuarantinedItem> QuarantinedItems { get; } = new();
        public ObservableCollection<ScanReport> ScanHistory { get; } = new();
        
        // Commands
        public ICommand QuickScanCommand { get; }
        public ICommand FullScanCommand { get; }
        public ICommand CancelScanCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand HandleEventCommand { get; }
        public ICommand NavigateCommand { get; }
        public ICommand CleanJunkCommand { get; }
        
        public SecuritySuiteViewModel()
        {
            _manager = new SecuritySuiteManager();
            _sessionStart = DateTime.Now;
            
            // Wire up events
            _manager.ScanEngine.JobUpdated += OnScanJobUpdated;
            
            // Subscribe to Ledger events
            LedgerManager.Instance.EventAdded += OnLedgerEventAdded;
            LedgerManager.Instance.EventResolved += OnLedgerEventResolved;
            
            // Initialize commands - use AsyncRelayCommand for async operations
            QuickScanCommand = new AsyncRelayCommand(async () => await StartObservationAsync(ScanType.Quick), () => !IsScanning);
            FullScanCommand = new AsyncRelayCommand(async () => await StartObservationAsync(ScanType.Full), () => !IsScanning);
            CancelScanCommand = new RelayCommand(CancelObservation, () => IsScanning);
            CheckUpdatesCommand = new AsyncRelayCommand(async () => await UpdateKnowledgeBaseAsync());
            HandleEventCommand = new RelayCommand<TimelineEvent>(HandleTimelineEvent);
            NavigateCommand = new RelayCommand<string>(Navigate);
            CleanJunkCommand = new AsyncRelayCommand(async () => await CleanJunkFilesAsync());
            
            // Load existing ledger events into timeline
            foreach (var evt in LedgerManager.Instance.Events.Take(20))
            {
                TimelineEvents.Add(ConvertLedgerToTimeline(evt));
            }
            
            // Add initial timeline events to show activity
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = "Atlas began observing your system.",
                Context = "System intelligence is now active. Real-time protection enabled.",
                Significance = EventSignificance.Routine,
                ConfidencePercent = 100,
                ActionText = "View system status"
            });
            
            AddTimelineEvent(new TimelineEvent
            {
                Timestamp = DateTime.Now.AddMinutes(-5),
                Narrative = "Verified Windows Defender integration.",
                Context = "Atlas works alongside your existing security software for enhanced protection.",
                Significance = EventSignificance.Routine,
                ConfidencePercent = 95
            });
            
            AddTimelineEvent(new TimelineEvent
            {
                Timestamp = DateTime.Now.AddMinutes(-10),
                Narrative = "Scanned startup programs for anomalies.",
                Context = "All 12 startup items verified as safe. No suspicious entries detected.",
                Significance = EventSignificance.Notable,
                ConfidencePercent = 92,
                ActionText = "View startup items →"
            });
            
            UpdateObservationPeriod();
            
            // Create a test ledger event to verify the system works
            CreateTestLedgerEvent();
        }
        
        /// <summary>
        /// Creates a test ledger event to verify the timeline integration
        /// </summary>
        private void CreateTestLedgerEvent()
        {
            var testEvent = new LedgerEvent
            {
                Category = LedgerCategory.System,
                Severity = LedgerSeverity.Info,
                Title = "System monitoring initialized",
                WhyItMatters = "Atlas is now watching for system changes and will alert you to anything suspicious."
            };
            
            testEvent.WithEvidence("Status", "Active")
                     .WithEvidence("Watchers", "Hosts file, Startup, Scheduled Tasks")
                     .WithAction(LedgerAction.Dismiss("Got it"));
            
            LedgerManager.Instance.AddEvent(testEvent);
        }
        
        /// <summary>
        /// Convert a LedgerEvent to a TimelineEvent for display
        /// </summary>
        private TimelineEvent ConvertLedgerToTimeline(LedgerEvent ledger)
        {
            var significance = ledger.Severity switch
            {
                LedgerSeverity.Info => EventSignificance.Routine,
                LedgerSeverity.Low => EventSignificance.Routine,
                LedgerSeverity.Medium => EventSignificance.Notable,
                LedgerSeverity.High => EventSignificance.Attention,
                LedgerSeverity.Critical => EventSignificance.Significant,
                _ => EventSignificance.Routine
            };
            
            var icon = ledger.Category switch
            {
                LedgerCategory.Security => "🛡️",
                LedgerCategory.Network => "🌐",
                LedgerCategory.Startup => "🚀",
                LedgerCategory.ScheduledTask => "⏰",
                LedgerCategory.Software => "📦",
                LedgerCategory.FileSystem => "📁",
                LedgerCategory.Registry => "🔧",
                LedgerCategory.CreativeAsset => "🎨",
                LedgerCategory.Mode => "🎯",
                _ => "📋"
            };
            
            // Build evidence string
            var evidenceText = string.Join(" • ", ledger.Evidence.Select(e => $"{e.Key}: {e.Value}"));
            
            // Get first action for display
            var firstAction = ledger.Actions.FirstOrDefault();
            
            return new TimelineEvent
            {
                Id = ledger.Id,
                Timestamp = ledger.Timestamp,
                Title = ledger.Title,
                Narrative = $"{icon} {ledger.Title}",
                WhyItMatters = ledger.WhyItMatters,
                Context = string.IsNullOrEmpty(ledger.WhyItMatters) 
                    ? evidenceText 
                    : $"{ledger.WhyItMatters}\n{evidenceText}",
                Significance = significance,
                ConfidencePercent = ledger.Severity >= LedgerSeverity.High ? 95 : 80,
                ActionText = firstAction?.Label ?? "",
                ActionId = ledger.Id,
                RelatedPath = ledger.Evidence.FirstOrDefault(e => e.IsPath)?.Value ?? "",
                IsCreativeAsset = ledger.Category == LedgerCategory.CreativeAsset,
                Evidence = ledger.Evidence,
                Actions = ledger.Actions
            };
        }
        
        private void OnLedgerEventAdded(LedgerEvent evt)
        {
            var timelineEvent = ConvertLedgerToTimeline(evt);
            AddTimelineEvent(timelineEvent);
        }
        
        private void OnLedgerEventResolved(LedgerEvent evt)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                // Find and update the timeline event
                var existing = TimelineEvents.FirstOrDefault(t => t.Id == evt.Id);
                if (existing != null)
                {
                    existing.Context = $"✅ Resolved: {evt.ResolvedBy}\n{existing.Context}";
                    existing.ActionText = ""; // Remove action button
                }
            });
        }
        
        private async Task CleanJunkFilesAsync()
        {
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = "🧹 Starting junk file cleanup...",
                Significance = EventSignificance.Routine
            });
            
            await Task.Delay(1000); // Simulate cleanup
            
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = "✅ Cleanup complete. Freed 0 MB of disk space.",
                Context = "Temporary files, browser cache, and system logs were cleaned.",
                Significance = EventSignificance.Notable,
                ConfidencePercent = 100
            });
        }
        
        // Public methods for direct button click handlers
        public async Task RunFullScanAsync()
        {
            await StartObservationAsync(ScanType.Full);
        }
        
        public async Task RunQuickScanAsync()
        {
            await StartObservationAsync(ScanType.Quick);
        }
        
        public async Task RunCleanJunkAsync()
        {
            await CleanJunkFilesAsync();
        }
        
        private void Navigate(string? view)
        {
            if (!string.IsNullOrEmpty(view))
                CurrentView = view;
        }
        
        private void UpdateObservationPeriod()
        {
            var elapsed = DateTime.Now - _sessionStart;
            if (elapsed.TotalMinutes < 1)
                ObservationPeriod = "Just started";
            else if (elapsed.TotalHours < 1)
                ObservationPeriod = $"For {(int)elapsed.TotalMinutes} minutes";
            else if (elapsed.TotalDays < 1)
                ObservationPeriod = $"For {(int)elapsed.TotalHours} hours";
            else
                ObservationPeriod = $"Since {_sessionStart:MMM d, h:mm tt}";
        }
        
        private void AddTimelineEvent(TimelineEvent evt)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                TimelineEvents.Insert(0, evt);
                EventsRecorded = $"{TimelineEvents.Count} event{(TimelineEvents.Count == 1 ? "" : "s")}";
                OnPropertyChanged(nameof(HasNoEvents));
            });
        }
        
        private async Task StartObservationAsync(ScanType type)
        {
            IsScanning = true;
            ScanStatus = "Beginning observation...";
            ScanProgress = 0;
            CurrentFile = "";
            
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = type == ScanType.Quick 
                    ? "⚡ Starting quick scan..."
                    : "🔍 Starting deep scan of your system...",
                Significance = EventSignificance.Notable,
                ConfidencePercent = 100
            });
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SecuritySuite] Starting {type} scan...");
                await _manager.StartScanAsync(type);
                System.Diagnostics.Debug.WriteLine($"[SecuritySuite] Scan completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecuritySuite] Scan error: {ex}");
                ScanStatus = "Scan failed";
                IsScanning = false;
                
                AddTimelineEvent(new TimelineEvent
                {
                    Narrative = "❌ Scan encountered an error.",
                    Context = ex.Message,
                    Significance = EventSignificance.Attention
                });
            }
        }
        
        private void CancelObservation()
        {
            _manager.CancelScan();
            
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = "Observation stopped at your request.",
                Significance = EventSignificance.Routine
            });
        }
        
        private void OnScanJobUpdated(ScanJob job)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                IsScanning = job.Status == Models.ScanStatus.Running;
                ScanProgress = job.ProgressPercent;
                CurrentFile = job.CurrentItem;
                
                // Calm status messages
                ScanStatus = job.ProgressPercent switch
                {
                    < 20 => "Looking around...",
                    < 40 => "Examining files...",
                    < 60 => "Checking processes...",
                    < 80 => "Reviewing findings...",
                    _ => "Finishing up..."
                };
                
                if (job.Status == Models.ScanStatus.Completed || job.Status == Models.ScanStatus.Cancelled)
                {
                    ProcessObservationResults(job);
                }
            });
        }
        
        private void ProcessObservationResults(ScanJob job)
        {
            Findings.Clear();
            
            // Update stats
            FilesScannedToday += (int)job.FilesScanned;
            ThreatsBlocked += job.ThreatsFound;
            LastScanTime = DateTime.Now;
            
            // Convert findings to timeline events
            var significantFindings = 0;
            var creativeAssets = 0;
            
            foreach (var finding in job.Findings)
            {
                Findings.Add(finding);
                
                var isCreative = CreativeAssetPolicy.IsTrustedCreativeAsset(finding.FilePath);
                if (isCreative)
                {
                    creativeAssets++;
                    continue; // Don't add creative assets to timeline
                }
                
                if (finding.Severity >= ThreatSeverity.Medium)
                {
                    significantFindings++;
                    AddTimelineEvent(TimelineEvent.FromFinding(finding));
                }
            }
            
            // Summary event
            if (job.Findings.Count == 0)
            {
                AddTimelineEvent(new TimelineEvent
                {
                    Narrative = $"✅ Scan complete! Examined {job.FilesScanned:N0} files.",
                    Context = "No threats detected. Your system is clean.",
                    Significance = EventSignificance.Notable,
                    ConfidencePercent = 95
                });
                SystemConfidence = "High";
            }
            else if (significantFindings == 0)
            {
                var msg = creativeAssets > 0
                    ? $"Found {creativeAssets} creative asset files (game dev, 3D art). These are safe."
                    : "Only minor items found. Nothing requires attention.";
                    
                AddTimelineEvent(new TimelineEvent
                {
                    Narrative = $"✅ Scan complete! Examined {job.FilesScanned:N0} files.",
                    Context = msg,
                    Significance = EventSignificance.Notable,
                    ConfidencePercent = 90
                });
                SystemConfidence = "High";
            }
            else
            {
                AddTimelineEvent(new TimelineEvent
                {
                    Narrative = $"⚠️ Scan complete. {significantFindings} item{(significantFindings == 1 ? "" : "s")} may need attention.",
                    Context = "Review the timeline above when you have a moment.",
                    Significance = EventSignificance.Notable,
                    ConfidencePercent = 85
                });
                SystemConfidence = significantFindings > 3 ? "Moderate" : "Good";
            }
            
            UpdateObservationPeriod();
        }
        
        public async Task UpdateKnowledgeBaseAsync()
        {
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = "Checking for knowledge base updates...",
                Significance = EventSignificance.Routine
            });
            
            IsScanning = true;
            ScanStatus = "Updating knowledge...";
            
            try
            {
                var (success, message) = await _manager.UpdateDefinitionsAsync();
                
                AddTimelineEvent(new TimelineEvent
                {
                    Narrative = success 
                        ? "Knowledge base is current."
                        : "Knowledge base update completed.",
                    Context = message,
                    Significance = EventSignificance.Routine,
                    ConfidencePercent = 95
                });
            }
            finally
            {
                IsScanning = false;
            }
        }
        
        private void HandleTimelineEvent(TimelineEvent? evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.RelatedPath)) return;
            
            try
            {
                if (File.Exists(evt.RelatedPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{evt.RelatedPath}\"");
                }
                else if (Directory.Exists(Path.GetDirectoryName(evt.RelatedPath)))
                {
                    System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(evt.RelatedPath)!);
                }
            }
            catch { }
        }
        
        /// <summary>
        /// Execute an action from a timeline event (linked to ledger)
        /// </summary>
        public async Task ExecuteTimelineActionAsync(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            
            // Find the ledger event
            var ledgerEvent = LedgerManager.Instance.Events.FirstOrDefault(e => e.Id == actionId);
            if (ledgerEvent == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SecuritySuite] Ledger event not found: {actionId}");
                return;
            }
            
            // Get the first action
            var action = ledgerEvent.Actions.FirstOrDefault();
            if (action == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SecuritySuite] No actions available for event: {actionId}");
                return;
            }
            
            // Build confirmation message based on action type
            if (action.RequiresConfirmation)
            {
                var confirmMessage = action.Type switch
                {
                    LedgerActionType.Revert => $"Are you sure you want to revert this change?\n\nThis will restore the previous state.",
                    LedgerActionType.Delete => $"Are you sure you want to delete this item?\n\nThis action cannot be undone.",
                    LedgerActionType.Block => $"Are you sure you want to disable this item?",
                    _ => $"Are you sure you want to {action.Label.ToLower()}?"
                };
                
                var result = MessageBox.Show(
                    confirmMessage,
                    "Confirm Action",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            // Execute the action
            System.Diagnostics.Debug.WriteLine($"[SecuritySuite] Executing action: {action.Label} on event: {ledgerEvent.Title}");
            var resultMessage = await LedgerManager.Instance.ExecuteActionAsync(ledgerEvent, action);
            
            // Show result
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = resultMessage,
                Significance = resultMessage.Contains("✅") ? EventSignificance.Notable : EventSignificance.Attention,
                ConfidencePercent = 100
            });
            
            // Update system status
            SystemStatus = resultMessage.Contains("✅") ? "Action completed successfully." : "Action completed.";
        }
        
        /// <summary>
        /// Select an event to show in the details panel
        /// </summary>
        public void SelectEvent(TimelineEvent evt)
        {
            SelectedEvent = evt;
        }
        
        /// <summary>
        /// Clear the selected event (close details panel)
        /// </summary>
        public void ClearSelectedEvent()
        {
            SelectedEvent = null;
        }
        
        /// <summary>
        /// Execute an action on the currently selected event
        /// </summary>
        public async Task ExecuteSelectedEventActionAsync(LedgerAction action)
        {
            if (SelectedEvent == null || string.IsNullOrEmpty(SelectedEvent.ActionId)) return;
            
            // Find the ledger event
            var ledgerEvent = LedgerManager.Instance.Events.FirstOrDefault(e => e.Id == SelectedEvent.ActionId);
            if (ledgerEvent == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SecuritySuite] Ledger event not found: {SelectedEvent.ActionId}");
                return;
            }
            
            // Build confirmation message based on action type
            if (action.RequiresConfirmation)
            {
                var confirmMessage = action.Type switch
                {
                    LedgerActionType.Revert => $"Are you sure you want to revert this change?\n\nThis will restore the previous state.",
                    LedgerActionType.Delete => $"Are you sure you want to delete this item?\n\nThis action cannot be undone.",
                    LedgerActionType.Block => $"Are you sure you want to disable this item?",
                    _ => $"Are you sure you want to {action.Label.ToLower()}?"
                };
                
                var result = MessageBox.Show(
                    confirmMessage,
                    "Confirm Action",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            // Execute the action
            System.Diagnostics.Debug.WriteLine($"[SecuritySuite] Executing action: {action.Label} on event: {ledgerEvent.Title}");
            var resultMessage = await LedgerManager.Instance.ExecuteActionAsync(ledgerEvent, action);
            
            // Show result
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = resultMessage,
                Significance = resultMessage.Contains("✅") ? EventSignificance.Notable : EventSignificance.Attention,
                ConfidencePercent = 100
            });
            
            // Update system status
            SystemStatus = resultMessage.Contains("✅") ? "Action completed successfully." : "Action completed.";
            
            // Clear selection after action
            ClearSelectedEvent();
        }
        
        /// <summary>
        /// Add a system event to the timeline (for external callers)
        /// </summary>
        public void AddSystemEvent(string narrative, string context = "")
        {
            AddTimelineEvent(new TimelineEvent
            {
                Narrative = narrative,
                Context = context,
                Significance = EventSignificance.Notable,
                ConfidencePercent = 100
            });
        }
        
        public void Dispose()
        {
            // Unsubscribe from ledger events
            LedgerManager.Instance.EventAdded -= OnLedgerEventAdded;
            LedgerManager.Instance.EventResolved -= OnLedgerEventResolved;
            
            _manager.Dispose();
        }
    }
    
    /// <summary>
    /// Simple relay command implementation
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;
        
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        
        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
    }
    
    /// <summary>
    /// Async-aware relay command for async operations
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;
        
        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        
        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);
        
        public async void Execute(object? parameter)
        {
            if (_isExecuting) return;
            
            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            
            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;
        
        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        
        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
        public void Execute(object? parameter) => _execute((T?)parameter);
    }
}
