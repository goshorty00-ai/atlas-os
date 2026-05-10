using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AtlasAI.Core;
using AtlasAI.Views.ViewModels;
using AtlasAI.Agent;

namespace AtlasAI.Controls
{
    public enum BuilderComponentType
    {
        View,
        Logic,
        Data,
        Api
    }

    public enum BuilderComponentStatus
    {
        Queued,
        Building,
        Complete
    }

    public enum BuilderMessageSender
    {
        User,
        Atlas
    }

    public sealed class BuildComponent : INotifyPropertyChanged
    {
        private string _id = "";
        private string _name = "";
        private BuilderComponentType _type;
        private BuilderComponentStatus _status;
        private double _progress;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public BuilderComponentType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        public BuilderComponentStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class BuilderMessage : INotifyPropertyChanged
    {
        private string _id = "";
        private BuilderMessageSender _sender;
        private string _content = "";
        private DateTime _timestamp;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public BuilderMessageSender Sender
        {
            get => _sender;
            set { _sender = value; OnPropertyChanged(); }
        }

        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(); }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class CodeBuilderViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _progressTimer;
        private readonly Random _rng = new Random();
        private AgentOrchestrator? _agent;
        private CancellationTokenSource? _currentCts;
        private string _workspaceRoot = "";
        private bool _isAgentRunning;
        private string _currentToolDescription = "";
        private double _buildProgress;
        private string _buildPhase = "INITIALIZING";
        private string _buildSubtitle = "IDLE · WATCHING WORKSPACE";
        private string _outputLog = "";
        private string _chatInput = "";
        private bool _isStarted;
        private FileSystemWatcher? _watcher;
        private readonly object _watchLock = new object();
        private readonly System.Collections.Generic.Dictionary<string, DateTime> _lastEventUtc = new System.Collections.Generic.Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastActivityUtc = DateTime.UtcNow;
        private List<WorkspaceTask> _tasks = new List<WorkspaceTask>();
        private string _tasksRoot = "";
        private string _lastTaskStatus = "";
        private Process? _taskProcess;
        private CancellationTokenSource? _taskCts;
        private readonly List<TaskRun> _taskHistory = new List<TaskRun>();
        private WorkspaceTask? _lastWorkspaceTask;
        private int _errorIndex = -1;

        public event EventHandler<OpenFileRequestEventArgs>? OpenFileRequested;

        private static readonly string[] Phases =
        {
            "INITIALIZING",
            "GENERATING ARCHITECTURE",
            "BUILDING UI COMPONENTS",
            "CREATING VIEW MODELS",
            "CONNECTING DATA SERVICES",
            "OPTIMIZING PERFORMANCE",
            "FINALIZING BUILD"
        };

        public ObservableCollection<BuildComponent> Components { get; } = new ObservableCollection<BuildComponent>();
        public ObservableCollection<BuilderMessage> Messages { get; } = new ObservableCollection<BuilderMessage>();

        public double BuildProgress
        {
            get => _buildProgress;
            private set
            {
                var v = Math.Max(0, Math.Min(100, value));
                if (Math.Abs(_buildProgress - v) < 0.0001) return;
                _buildProgress = v;
                OnPropertyChanged();
            }
        }

        public int BuildProgressInt => (int)Math.Round(BuildProgress);

        public string BuildPhase
        {
            get => _buildPhase;
            private set
            {
                var v = (value ?? "").Trim();
                if (string.Equals(_buildPhase, v, StringComparison.Ordinal)) return;
                _buildPhase = v;
                OnPropertyChanged();
            }
        }

        public string BuildSubtitle
        {
            get => _buildSubtitle;
            private set
            {
                var v = (value ?? "").Trim();
                if (string.Equals(_buildSubtitle, v, StringComparison.Ordinal)) return;
                _buildSubtitle = v;
                OnPropertyChanged();
            }
        }

        public int PresentationCount => Components.Count(c => c.Type == BuilderComponentType.View);
        public int BusinessLogicCount => Components.Count(c => c.Type == BuilderComponentType.Logic);
        public int DataAccessCount => Components.Count(c => c.Type == BuilderComponentType.Data);
        public int InfrastructureCount => Components.Count(c => c.Type == BuilderComponentType.Api);

        public double PresentationProgress => GetCategoryProgress(BuilderComponentType.View);
        public double BusinessLogicProgress => GetCategoryProgress(BuilderComponentType.Logic);
        public double DataAccessProgress => GetCategoryProgress(BuilderComponentType.Data);
        public double InfrastructureProgress => GetCategoryProgress(BuilderComponentType.Api);

        public string OutputLog
        {
            get => _outputLog;
            private set
            {
                _outputLog = value ?? "";
                OnPropertyChanged();
            }
        }

        public string ChatInput
        {
            get => _chatInput;
            set
            {
                _chatInput = value ?? "";
                OnPropertyChanged();
            }
        }

        public ICommand SendMessageCommand { get; }
        public ICommand HelpCommand { get; }
        public ICommand ClearOutputCommand { get; }
        public ICommand RunDefaultTaskCommand { get; }
        public ICommand BuildDefaultTaskCommand { get; }
        public ICommand TestDefaultTaskCommand { get; }
        public ICommand ListTasksCommand { get; }
        public ICommand CancelCommand { get; }

        public CodeBuilderViewModel()
        {
            _workspaceRoot = TryFindWorkspaceRoot();
            LoadDefaultComponents();
            RefreshTasks();

            AppendLog("AUTONOMOUS BUILDER ACTIVE");
            AppendLog(
                "ATLAS CODER\n" +
                "• Browse your project in IDE mode\n" +
                "• Search files: /search <pattern> [path]\n" +
                "• List folders: /ls [path]\n" +
                "• Read files: /read <path>\n" +
                "• Clear output: /clear\n" +
                "TIP\n" +
                "• Type /help for commands and examples");

            SendMessageCommand = new RelayCommand(SendMessage);
            HelpCommand = new RelayCommand(() => TryHandleLocalCommand("/help"));
            ClearOutputCommand = new RelayCommand(() => TryHandleLocalCommand("/clear"));
            RunDefaultTaskCommand = new RelayCommand(() => _ = RunDefaultTaskAsync(), () => !string.IsNullOrWhiteSpace(_workspaceRoot));
            BuildDefaultTaskCommand = new RelayCommand(() => _ = RunDefaultBuildAsync(), () => !string.IsNullOrWhiteSpace(_workspaceRoot));
            TestDefaultTaskCommand = new RelayCommand(() => _ = RunDefaultTestAsync(), () => !string.IsNullOrWhiteSpace(_workspaceRoot));
            ListTasksCommand = new RelayCommand(() => ListTasks(), () => !string.IsNullOrWhiteSpace(_workspaceRoot));
            CancelCommand = new RelayCommand(CancelOperation, () => _isAgentRunning);

            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _progressTimer.Tick += (_, __) =>
            {
                try
                {
                    if (Components.Count == 0)
                    {
                        BuildProgress = 0;
                        if (!_isAgentRunning && string.Equals(BuildPhase, "INITIALIZING", StringComparison.Ordinal))
                            BuildPhase = "IDLE";
                        UpdateSubtitle();
                        return;
                    }

                    var sum = Components.Sum(c => Math.Max(0, Math.Min(100, c.Progress)));
                    BuildProgress = sum / Components.Count;

                    if (!_isAgentRunning)
                    {
                        if (Components.All(c => c.Status == BuilderComponentStatus.Complete))
                        {
                            if (string.Equals(BuildPhase, "BUILD COMPLETE", StringComparison.Ordinal) &&
                                (DateTime.UtcNow - _lastActivityUtc).TotalSeconds < 3)
                            {
                            }
                            else
                            {
                                BuildPhase = "IDLE";
                            }
                        }
                        else if (BuildProgress <= 0.01)
                            BuildPhase = string.IsNullOrWhiteSpace(_workspaceRoot) ? "INITIALIZING" : "IDLE";
                    }
                    UpdateSubtitle();
                    RaiseArchitecture();
                }
                catch
                {
                }
            };

            Components.CollectionChanged += Components_CollectionChanged;
        }

        public string WorkspaceRoot => _workspaceRoot;
        
        public string LastTaskStatus
        {
            get => _lastTaskStatus;
            private set
            {
                _lastTaskStatus = value ?? "";
                OnPropertyChanged();
            }
        }

        public void RestartSimulation()
        {
            try
            {
                LoadDefaultComponents();
                BuildPhase = string.IsNullOrWhiteSpace(_workspaceRoot) ? "INITIALIZING" : "IDLE";
                BuildSubtitle = string.IsNullOrWhiteSpace(_workspaceRoot) ? "INITIALIZING" : "IDLE · WATCHING WORKSPACE";
                _lastActivityUtc = DateTime.UtcNow;

                _progressTimer.Stop();
                _progressTimer.Start();
                TryInitializeAgent();
                StartWatcher();
            }
            catch
            {
            }
        }

        private void Components_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (e.NewItems != null)
                {
                    foreach (var obj in e.NewItems)
                    {
                        if (obj is BuildComponent c)
                            c.PropertyChanged += (_, __) =>
                            {
                                _lastActivityUtc = DateTime.UtcNow;
                                RaiseArchitecture();
                                CommandManager.InvalidateRequerySuggested();
                            };
                    }
                }
                _lastActivityUtc = DateTime.UtcNow;
                RaiseArchitecture();
            }
            catch
            {
            }
        }

        public void Start()
        {
            if (_isStarted) return;
            _isStarted = true;
            _progressTimer.Start();
            StartWatcher();
        }

        public void Stop()
        {
            _progressTimer.Stop();
            StopWatcher();
        }

        public void SetWorkspaceRoot(string path)
        {
            try
            {
                var p = (path ?? "").Trim();
                if (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p)) return;

                if (string.Equals(_workspaceRoot, p, StringComparison.OrdinalIgnoreCase))
                    return;

                StopWatcher();

                _workspaceRoot = p;
                _agent = null;
                _lastActivityUtc = DateTime.UtcNow;
                RefreshTasks();

                BuildPhase = "IDLE";
                BuildSubtitle = "IDLE · WATCHING WORKSPACE";

                AppendLog($"WORKSPACE · {p}");

                // IMPORTANT: do not auto-initialize the agent here.
                // Workspace selection should be a pure file/navigation action; the agent is initialized lazily on demand.
                if (_isStarted)
                    StartWatcher();
                OnPropertyChanged(nameof(WorkspaceRoot));
            }
            catch
            {
            }
        }

        private sealed class WorkspaceTask
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Command { get; set; } = "";
            public string Group { get; set; } = "";
        }

        private sealed class TaskRun
        {
            public string Title { get; set; } = "";
            public string Command { get; set; } = "";
            public DateTime StartedUtc { get; set; }
            public int DurationMs { get; set; }
            public int ExitCode { get; set; }
            public bool Cancelled { get; set; }
            public bool Success { get; set; }
        }

        private void RefreshTasks()
        {
            try
            {
                var root = _workspaceRoot;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    _tasks = new List<WorkspaceTask>();
                    _tasksRoot = "";
                    return;
                }

                if (string.Equals(_tasksRoot, root, StringComparison.OrdinalIgnoreCase) && _tasks.Count > 0)
                    return;

                _tasks = DetectWorkspaceTasks(root);
                _tasksRoot = root;
            }
            catch
            {
                _tasks = new List<WorkspaceTask>();
                _tasksRoot = _workspaceRoot ?? "";
            }
        }

        private static List<WorkspaceTask> DetectWorkspaceTasks(string root)
        {
            var list = new List<WorkspaceTask>();
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return list;

                var sln = Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                var csproj = Directory.GetFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                var pkg = Path.Combine(root, "package.json");

                if (!string.IsNullOrWhiteSpace(sln) || !string.IsNullOrWhiteSpace(csproj))
                {
                    list.Add(new WorkspaceTask { Id = "dotnet_run", Title = "Run (.NET)", Command = "dotnet run", Group = "dotnet" });
                    list.Add(new WorkspaceTask { Id = "dotnet_build", Title = "Build (.NET)", Command = "dotnet build", Group = "dotnet" });
                    list.Add(new WorkspaceTask { Id = "dotnet_test", Title = "Test (.NET)", Command = "dotnet test", Group = "dotnet" });
                }

                if (File.Exists(pkg))
                {
                    try
                    {
                        using var fs = File.OpenRead(pkg);
                        using var doc = JsonDocument.Parse(fs);
                        if (doc.RootElement.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in scripts.EnumerateObject())
                            {
                                var name = (prop.Name ?? "").Trim();
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                list.Add(new WorkspaceTask
                                {
                                    Id = "npm_" + name.ToLowerInvariant(),
                                    Title = $"npm: {name}",
                                    Command = $"npm run {name}",
                                    Group = "npm"
                                });
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (list.All(t => t.Id != "npm_install"))
                        list.Add(new WorkspaceTask { Id = "npm_install", Title = "npm: install", Command = "npm install", Group = "npm" });
                }
            }
            catch
            {
            }

            return list
                .GroupBy(t => t.Id)
                .Select(g => g.First())
                .OrderBy(t => t.Group)
                .ThenBy(t => t.Title)
                .ToList();
        }

        private WorkspaceTask? GetDefaultRunTask()
        {
            try
            {
                RefreshTasks();
                var npmDev = _tasks.FirstOrDefault(t => t.Id == "npm_dev") ?? _tasks.FirstOrDefault(t => t.Id == "npm_start");
                if (npmDev != null) return npmDev;
                return _tasks.FirstOrDefault(t => t.Id == "dotnet_run") ?? _tasks.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private WorkspaceTask? GetDefaultBuildTask()
        {
            try
            {
                RefreshTasks();
                return _tasks.FirstOrDefault(t => t.Id == "npm_build") ?? _tasks.FirstOrDefault(t => t.Id == "dotnet_build");
            }
            catch
            {
                return null;
            }
        }

        private WorkspaceTask? GetDefaultTestTask()
        {
            try
            {
                RefreshTasks();
                return _tasks.FirstOrDefault(t => t.Id == "npm_test") ?? _tasks.FirstOrDefault(t => t.Id == "dotnet_test");
            }
            catch
            {
                return null;
            }
        }

        private async Task RunDefaultTaskAsync()
        {
            try
            {
                var task = GetDefaultRunTask();
                if (task == null)
                {
                    AddAtlasMessageOnUi("NO TASKS DETECTED");
                    return;
                }
                await RunWorkspaceTaskAsync(task).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task RunDefaultBuildAsync()
        {
            try
            {
                var task = GetDefaultBuildTask();
                if (task == null)
                {
                    AddAtlasMessageOnUi("NO BUILD TASK DETECTED");
                    return;
                }
                await RunWorkspaceTaskAsync(task).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task RunDefaultTestAsync()
        {
            try
            {
                var task = GetDefaultTestTask();
                if (task == null)
                {
                    AddAtlasMessageOnUi("NO TEST TASK DETECTED");
                    return;
                }
                await RunWorkspaceTaskAsync(task).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task RunWorkspaceTaskAsync(WorkspaceTask task)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_workspaceRoot))
                {
                    AddAtlasMessageOnUi("WORKSPACE NOT FOUND");
                    return;
                }

                if (_isAgentRunning)
                {
                    AddAtlasMessageOnUi("BUSY · CURRENT BUILD SEQUENCE IN PROGRESS");
                    return;
                }

                if (_taskProcess != null)
                {
                    try
                    {
                        if (!_taskProcess.HasExited)
                        {
                            AddAtlasMessageOnUi("TASK RUNNING · USE /stop TO CANCEL");
                            return;
                        }
                    }
                    catch
                    {
                    }
                }

                var startedUtc = DateTime.UtcNow;
                _lastWorkspaceTask = task;
                var run = new TaskRun { Title = task.Title, Command = task.Command, StartedUtc = startedUtc };
                try
                {
                    _taskHistory.Insert(0, run);
                    if (_taskHistory.Count > 20)
                        _taskHistory.RemoveRange(20, _taskHistory.Count - 20);
                }
                catch
                {
                }

                _lastActivityUtc = DateTime.UtcNow;
                BuildPhase = "APPLICATION ASSEMBLY IN PROGRESS";
                LastTaskStatus = $"Running · {task.Title}";
                AddAtlasMessageOnUi($"❯ {task.Command}");

                TryMarkAllComponentsBuilding();
                StartProgressSimulation();

                _taskCts = new CancellationTokenSource();
                var token = _taskCts.Token;

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + task.Command,
                    WorkingDirectory = _workspaceRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _taskProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _taskProcess.OutputDataReceived += (_, e) =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(e.Data)) return;
                        AddAtlasMessageOnUi(e.Data);
                    }
                    catch
                    {
                    }
                };
                _taskProcess.ErrorDataReceived += (_, e) =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(e.Data)) return;
                        AddAtlasMessageOnUi(e.Data);
                    }
                    catch
                    {
                    }
                };

                _taskProcess.Start();
                _taskProcess.BeginOutputReadLine();
                _taskProcess.BeginErrorReadLine();

                await _taskProcess.WaitForExitAsync(token).ConfigureAwait(false);

                var exitCode = 0;
                try { exitCode = _taskProcess.ExitCode; } catch { }
                var ok = exitCode == 0;

                var elapsed = DateTime.UtcNow - startedUtc;
                var ms = Math.Max(0, (int)elapsed.TotalMilliseconds);
                AddAtlasMessageOnUi($"[process exited: {exitCode}]");

                StopProgressSimulation(ok);
                try
                {
                    run.ExitCode = exitCode;
                    run.DurationMs = ms;
                    run.Success = ok;
                }
                catch
                {
                }
                LastTaskStatus = (ok ? "OK" : "FAILED") + $" · {task.Title} · {ms}ms";
                BuildPhase = ok ? "BUILD COMPLETE" : "IDLE";
            }
            catch (OperationCanceledException)
            {
                StopProgressSimulation(false);
                LastTaskStatus = "CANCELLED";
                BuildPhase = "IDLE";
                AddAtlasMessageOnUi("[task cancelled]");
                try
                {
                    if (_taskHistory.Count > 0)
                    {
                        _taskHistory[0].Cancelled = true;
                        _taskHistory[0].Success = false;
                        _taskHistory[0].ExitCode = -1;
                    }
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                StopProgressSimulation(false);
                LastTaskStatus = "FAILED";
                AddAtlasMessageOnUi($"ERROR · {ex.Message}");
            }
            finally
            {
                try { _taskCts?.Dispose(); } catch { }
                _taskCts = null;
                _taskProcess = null;
            }
        }

        public void StopRunningTask()
        {
            try
            {
                _taskCts?.Cancel();
                try { _taskProcess?.Kill(); } catch { }
            }
            catch
            {
            }
        }

        private void TryMarkAllComponentsBuilding()
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        foreach (var c in Components)
                        {
                            if (c.Status == BuilderComponentStatus.Complete) continue;
                            c.Status = BuilderComponentStatus.Building;
                            c.Progress = Math.Max(c.Progress, 10);
                        }
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }
        }

        private void StartProgressSimulation()
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (_taskProcess != null)
                        {
                            try
                            {
                                if (_taskProcess.HasExited) break;
                            }
                            catch
                            {
                            }

                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    foreach (var c in Components)
                                    {
                                        if (c.Status != BuilderComponentStatus.Building) continue;
                                        if (c.Progress >= 95) continue;
                                        c.Progress = Math.Min(95, c.Progress + 1 + _rng.NextDouble() * 2);
                                    }
                                }
                                catch
                                {
                                }
                            }));

                            await Task.Delay(250).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        private void StopProgressSimulation(bool success)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        foreach (var c in Components)
                        {
                            if (c.Status != BuilderComponentStatus.Building && c.Status != BuilderComponentStatus.Queued)
                                continue;

                            if (success)
                            {
                                c.Progress = 100;
                                c.Status = BuilderComponentStatus.Complete;
                            }
                            else
                            {
                                c.Progress = Math.Min(95, Math.Max(c.Progress, 30));
                                c.Status = BuilderComponentStatus.Queued;
                            }
                        }
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }
        }

        private void ListTasks()
        {
            try
            {
                RefreshTasks();
                if (_tasks.Count == 0)
                {
                    AppendLog("NO TASKS DETECTED");
                    return;
                }

                AppendLog("TASKS");
                foreach (var t in _tasks.Take(30))
                    AppendLog($"• {t.Title}");
                if (_tasks.Count > 30)
                    AppendLog($"… {(_tasks.Count - 30)} more");
            }
            catch
            {
            }
        }

        public void ShowTaskHistory()
        {
            try
            {
                if (_taskHistory.Count == 0)
                {
                    AppendLog("NO TASK HISTORY");
                    return;
                }

                AppendLog("TASK HISTORY");
                foreach (var h in _taskHistory.Take(12))
                {
                    var state = h.Cancelled ? "CANCELLED" : (h.Success ? "OK" : "FAILED");
                    var ms = Math.Max(0, h.DurationMs);
                    AppendLog($"• {state} · {h.Title} · {ms}ms");
                }
                if (_taskHistory.Count > 12)
                    AppendLog($"… {(_taskHistory.Count - 12)} more");
            }
            catch
            {
            }
        }

        public async Task RunLastTaskAsync()
        {
            try
            {
                if (_lastWorkspaceTask == null)
                {
                    AppendLog("NO LAST TASK");
                    return;
                }
                await RunWorkspaceTaskAsync(_lastWorkspaceTask).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        public void OpenFirstError()
        {
            try
            {
                var errors = GetErrorLocations();
                if (errors.Count == 0)
                {
                    AppendLog(string.IsNullOrWhiteSpace(OutputLog) ? "NO OUTPUT" : "NO ERRORS FOUND");
                    return;
                }

                _errorIndex = 0;
                OpenErrorAtIndex(errors, _errorIndex);
            }
            catch
            {
            }
        }

        public void OpenNextError()
        {
            try
            {
                var errors = GetErrorLocations();
                if (errors.Count == 0)
                {
                    AppendLog(string.IsNullOrWhiteSpace(OutputLog) ? "NO OUTPUT" : "NO ERRORS FOUND");
                    return;
                }

                _errorIndex = (_errorIndex + 1) % errors.Count;
                OpenErrorAtIndex(errors, _errorIndex);
            }
            catch
            {
            }
        }

        public void OpenPrevError()
        {
            try
            {
                var errors = GetErrorLocations();
                if (errors.Count == 0)
                {
                    AppendLog(string.IsNullOrWhiteSpace(OutputLog) ? "NO OUTPUT" : "NO ERRORS FOUND");
                    return;
                }

                _errorIndex = _errorIndex <= 0 ? errors.Count - 1 : _errorIndex - 1;
                OpenErrorAtIndex(errors, _errorIndex);
            }
            catch
            {
            }
        }

        private readonly struct ErrorLocation
        {
            public string Path { get; }
            public int Line { get; }
            public int Column { get; }

            public ErrorLocation(string path, int line, int column)
            {
                Path = path ?? "";
                Line = line;
                Column = column;
            }
        }

        private List<ErrorLocation> GetErrorLocations()
        {
            var list = new List<ErrorLocation>();
            try
            {
                if (string.IsNullOrWhiteSpace(OutputLog)) return list;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var lines = OutputLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var raw in lines)
                {
                    var line = (raw ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    if (!TryParseFileLocation(line, out var path, out var ln, out var col))
                        continue;

                    var full = path;
                    if (!Path.IsPathRooted(full) && !string.IsNullOrWhiteSpace(_workspaceRoot))
                    {
                        try { full = Path.GetFullPath(Path.Combine(_workspaceRoot, full)); } catch { }
                    }

                    if (!File.Exists(full)) continue;
                    var key = $"{full}|{ln}|{col}";
                    if (!seen.Add(key)) continue;
                    list.Add(new ErrorLocation(full, ln, col));
                    if (list.Count >= 200) break;
                }
            }
            catch
            {
            }
            return list;
        }

        private void OpenErrorAtIndex(IReadOnlyList<ErrorLocation> errors, int index)
        {
            try
            {
                if (index < 0 || index >= errors.Count) return;
                var e = errors[index];
                OpenFileRequested?.Invoke(this, new OpenFileRequestEventArgs(e.Path, e.Line, e.Column));
                AppendLog($"ERROR {index + 1}/{errors.Count}");
            }
            catch
            {
            }
        }

        private static bool TryParseFileLocation(string text, out string path, out int line, out int col)
        {
            path = "";
            line = 0;
            col = 0;
            try
            {
                var i = text.IndexOf(@":\", StringComparison.Ordinal);
                if (i < 1) return false;

                var start = i - 1;
                while (start > 0 && text[start - 1] != ' ' && text[start - 1] != '"' && text[start - 1] != '\'' && text[start - 1] != '(' && text[start - 1] != '[')
                    start--;

                var end = i + 2;
                while (end < text.Length && text[end] != ' ' && text[end] != '"' && text[end] != '\'' && text[end] != ')' && text[end] != ']' )
                    end++;

                var candidate = text.Substring(start, end - start).Trim().Trim('"', '\'', '(', ')', '[', ']', ',');
                if (string.IsNullOrWhiteSpace(candidate)) return false;

                if (TryParseMsBuildSuffix(text, end, out var ln, out var c))
                {
                    line = ln;
                    col = c;
                }

                path = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseMsBuildSuffix(string text, int fromIndex, out int line, out int col)
        {
            line = 0;
            col = 0;
            try
            {
                var open = text.IndexOf('(', fromIndex);
                if (open < 0) return false;
                var close = text.IndexOf(')', open + 1);
                if (close < 0) return false;

                var inner = text.Substring(open + 1, close - open - 1);
                var parts = inner.Split(',');
                if (parts.Length >= 1)
                    _ = int.TryParse(parts[0].Trim(), out line);
                if (parts.Length >= 2)
                    _ = int.TryParse(parts[1].Trim(), out col);
                return line > 0;
            }
            catch
            {
                return false;
            }
        }

        private void CancelOperation()
        {
            try
            {
                _currentCts?.Cancel();
                _currentCts?.Dispose();
                _currentCts = null;
                _isAgentRunning = false;
                AppendLog("CANCELLED · OPERATION STOPPED BY USER");
                BuildPhase = "IDLE";
                CommandManager.InvalidateRequerySuggested();
            }
            catch
            {
            }
        }

        private void SendMessage()
        {
            try
            {
                var text = (ChatInput ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text)) return;
                AppendLog($"> {text}");

                ChatInput = "";

                if (TryHandleLocalCommand(text))
                    return;

                if (_agent == null)
                {
                    TryInitializeAgent();
                    if (_agent == null)
                    {
                        AppendLog("AI OFFLINE · CONFIGURE PROVIDER IN SETTINGS · /help FOR LOCAL COMMANDS");
                        return;
                    }
                }

                if (_isAgentRunning)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(400).ConfigureAwait(false);
                        AddAtlasMessageOnUi("BUSY · CURRENT BUILD SEQUENCE IN PROGRESS");
                    });
                    return;
                }

                _isAgentRunning = true;
                _currentCts = new CancellationTokenSource();
                var ct = _currentCts.Token;

                BuildPhase = "GENERATING ARCHITECTURE";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(200, ct).ConfigureAwait(false);
                        var result = await _agent.RunAsync(text, ct).ConfigureAwait(false);
                        AddAtlasMessageOnUi(result);
                    }
                    catch (OperationCanceledException)
                    {
                        // Handled in CancelOperation
                    }
                    catch (Exception ex)
                    {
                        AddAtlasMessageOnUi($"ERROR · {ex.Message}");
                    }
                    finally
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            _isAgentRunning = false;
                            _currentCts?.Dispose();
                            _currentCts = null;
                            CommandManager.InvalidateRequerySuggested();
                        }
                    }
                }, ct);
            }
            catch
            {
            }
        }

        private bool TryHandleLocalCommand(string input)
        {
            try
            {
                var t = (input ?? "").Trim();
                if (string.IsNullOrWhiteSpace(t)) return true;

                if (string.Equals(t, "/clear", StringComparison.OrdinalIgnoreCase))
                {
                    OutputLog = "";
                    return true;
                }

                if (string.Equals(t, "/help", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "help", StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog("COMMANDS");
                    AppendLog("• /tasks  (list detected tasks)");
                    AppendLog("• /run  (run default task)");
                    AppendLog("• /build  (run default build)");
                    AppendLog("• /test  (run default test)");
                    AppendLog("• /again  (run last task)");
                    AppendLog("• /firsterror  (open first build error)");
                    AppendLog("• /nexterror  (open next build error)");
                    AppendLog("• /preverror  (open previous build error)");
                    AppendLog("• /history  (show task history)");
                    AppendLog("• /stop  (stop running task)");
                    AppendLog("• /ls [path]  (example: /ls Controls)");
                    AppendLog("• /search <pattern> [path]  (example: /search *.xaml Controls)");
                    AppendLog("• /read <path>  (example: /read Controls/CodeControl.xaml)");
                    AppendLog("• /clear");
                    return true;
                }

                if (string.Equals(t, "/tasks", StringComparison.OrdinalIgnoreCase))
                {
                    ListTasks();
                    return true;
                }

                if (string.Equals(t, "/run", StringComparison.OrdinalIgnoreCase))
                {
                    _ = RunDefaultTaskAsync();
                    return true;
                }

                if (string.Equals(t, "/build", StringComparison.OrdinalIgnoreCase))
                {
                    _ = RunDefaultBuildAsync();
                    return true;
                }

                if (string.Equals(t, "/test", StringComparison.OrdinalIgnoreCase))
                {
                    _ = RunDefaultTestAsync();
                    return true;
                }

                if (string.Equals(t, "/again", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "/rerun", StringComparison.OrdinalIgnoreCase))
                {
                    _ = RunLastTaskAsync();
                    return true;
                }

                if (string.Equals(t, "/firsterror", StringComparison.OrdinalIgnoreCase))
                {
                    OpenFirstError();
                    return true;
                }

                if (string.Equals(t, "/nexterror", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "/next", StringComparison.OrdinalIgnoreCase))
                {
                    OpenNextError();
                    return true;
                }

                if (string.Equals(t, "/preverror", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "/prev", StringComparison.OrdinalIgnoreCase))
                {
                    OpenPrevError();
                    return true;
                }

                if (string.Equals(t, "/history", StringComparison.OrdinalIgnoreCase))
                {
                    ShowTaskHistory();
                    return true;
                }

                if (string.Equals(t, "/stop", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "/cancel", StringComparison.OrdinalIgnoreCase))
                {
                    StopRunningTask();
                    return true;
                }

                if (t.StartsWith("/ls", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = t.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var path = parts.Length > 1 ? parts[1].Trim() : ".";
                    
                    _currentCts?.Cancel();
                    _currentCts?.Dispose();
                    _currentCts = new CancellationTokenSource();
                    var ct = _currentCts.Token;
                    _isAgentRunning = true;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await RunToolAsync("list_directory", new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["path"] = path,
                                ["recursive"] = false
                            }, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (!ct.IsCancellationRequested)
                            {
                                _isAgentRunning = false;
                                CommandManager.InvalidateRequerySuggested();
                            }
                        }
                    }, ct);
                    return true;
                }

                if (t.StartsWith("/search", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        AppendLog("USAGE · /search <pattern> [path]");
                        return true;
                    }

                    var pattern = parts[1];
                    var path = parts.Length >= 3 ? parts[2] : ".";

                    _currentCts?.Cancel();
                    _currentCts?.Dispose();
                    _currentCts = new CancellationTokenSource();
                    var ct = _currentCts.Token;
                    _isAgentRunning = true;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await RunToolAsync("search_files", new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["pattern"] = pattern,
                                ["path"] = path
                            }, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (!ct.IsCancellationRequested)
                            {
                                _isAgentRunning = false;
                                CommandManager.InvalidateRequerySuggested();
                            }
                        }
                    }, ct);
                    return true;
                }

                if (t.StartsWith("/read", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = t.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        AppendLog("USAGE · /read <path>");
                        return true;
                    }

                    var path = parts[1].Trim();

                    _currentCts?.Cancel();
                    _currentCts?.Dispose();
                    _currentCts = new CancellationTokenSource();
                    var ct = _currentCts.Token;
                    _isAgentRunning = true;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await RunToolAsync("read_file", new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["path"] = path
                            }, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (!ct.IsCancellationRequested)
                            {
                                _isAgentRunning = false;
                                CommandManager.InvalidateRequerySuggested();
                            }
                        }
                    }, ct);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task RunToolAsync(string tool, System.Collections.Generic.Dictionary<string, object> parameters, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_workspaceRoot))
                {
                    AppendLog("WORKSPACE NOT FOUND");
                    return;
                }

                BuildPhase = "GENERATING ARCHITECTURE";
                _lastActivityUtc = DateTime.UtcNow;
                var result = await AtlasAI.Agent.AgentTools.ExecuteToolAsync(tool, parameters, _workspaceRoot, ct).ConfigureAwait(false);
                AddAtlasMessageOnUi(result.Output ?? "");
            }
            catch (OperationCanceledException)
            {
                // Handled in CancelOperation
            }
            catch (Exception ex)
            {
                AddAtlasMessageOnUi($"ERROR · {ex.Message}");
            }
        }

        private void TryInitializeAgent()
        {
            try
            {
                if (_agent != null) return;
                if (string.IsNullOrWhiteSpace(_workspaceRoot)) return;

                _agent = new AgentOrchestrator(_workspaceRoot);
                _agent.OnThinking += (_, msg) => AddAtlasMessageOnUi(msg);
                _agent.OnToolExecuting += (_, tool) =>
                {
                    _currentToolDescription = tool ?? "";
                    _lastActivityUtc = DateTime.UtcNow;
                    AddAtlasMessageOnUi(tool ?? "");
                    UpdatePhaseFromTool(tool ?? "");
                    TryMarkComponentBuildingFromTool(tool ?? "");
                };
                _agent.OnToolResult += (_, res) =>
                {
                    _lastActivityUtc = DateTime.UtcNow;
                    TryMarkComponentCompleteFromTool(_currentToolDescription, res?.Success ?? false);
                    try
                    {
                        var output = res?.Output ?? "";
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            var trimmed = output.Trim();
                            if (trimmed.Length > 1400)
                                trimmed = trimmed.Substring(0, 1400) + "\n…(truncated)";
                            AddAtlasMessageOnUi(trimmed);
                        }
                    }
                    catch
                    {
                    }
                };
                _agent.OnResponse += (_, msg) => AddAtlasMessageOnUi(msg);
                _agent.OnError += (_, msg) => AddAtlasMessageOnUi(msg);
                _agent.OnConfirmationRequired = async (tool, description) =>
                {
                    AddAtlasMessageOnUi($"CONFIRMATION REQUIRED · {description}");
                    await Task.Delay(10).ConfigureAwait(false);
                    if (string.Equals(tool, "run_command", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tool, "run_powershell", StringComparison.OrdinalIgnoreCase))
                        return true;
                    return false;
                };
            }
            catch
            {
                _agent = null;
            }
        }

        private void AddAtlasMessageOnUi(string content)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        AppendLog(content ?? "");
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }
        }

        private void AppendLog(string line)
        {
            try
            {
                var text = (line ?? "").TrimEnd();
                if (string.IsNullOrWhiteSpace(text)) return;

                var stamped = $"[{DateTime.Now:HH:mm:ss}] {text}";
                var next = string.IsNullOrEmpty(_outputLog) ? stamped : (_outputLog + "\n" + stamped);

                const int maxChars = 50000;
                if (next.Length > maxChars)
                {
                    next = next.Substring(next.Length - maxChars);
                    var cut = next.IndexOf('\n');
                    if (cut > 0) next = next.Substring(cut + 1);
                }

                OutputLog = next;
            }
            catch
            {
            }
        }

        private void UpdatePhaseFromTool(string toolDescription)
        {
            try
            {
                var t = (toolDescription ?? "").ToLowerInvariant();
                if (t.Contains("search") || t.Contains("list_directory") || t.Contains("read_file"))
                {
                    BuildPhase = "GENERATING ARCHITECTURE";
                    return;
                }
                if (t.Contains("write_file") || t.Contains("append_file") || t.Contains("create_code_file"))
                {
                    BuildPhase = "BUILDING UI COMPONENTS";
                    return;
                }
                if (t.Contains("modify_code") || t.Contains("refactor_code") || t.Contains("fix_code"))
                {
                    BuildPhase = "CREATING VIEW MODELS";
                    return;
                }
                if (t.Contains("run_command") || t.Contains("run_powershell"))
                {
                    BuildPhase = "FINALIZING BUILD";
                    return;
                }
            }
            catch
            {
            }
        }

        private void StartWatcher()
        {
            try
            {
                if (_watcher != null) return;
                if (string.IsNullOrWhiteSpace(_workspaceRoot)) return;
                if (!Directory.Exists(_workspaceRoot)) return;

                _watcher = new FileSystemWatcher(_workspaceRoot)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };

                _watcher.Changed += (_, e) => OnWorkspaceFileEvent("UPDATED", e.FullPath);
                _watcher.Created += (_, e) => OnWorkspaceFileEvent("CREATED", e.FullPath);
                _watcher.Deleted += (_, e) => OnWorkspaceFileEvent("DELETED", e.FullPath);
                _watcher.Renamed += (_, e) =>
                {
                    var re = e as RenamedEventArgs;
                    if (re != null)
                    {
                        OnWorkspaceFileEvent("RENAMED", re.OldFullPath);
                        OnWorkspaceFileEvent("RENAMED", re.FullPath);
                    }
                };
            }
            catch
            {
            }
        }

        private void StopWatcher()
        {
            try
            {
                if (_watcher == null) return;
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
            catch
            {
            }
        }

        private void OnWorkspaceFileEvent(string verb, string fullPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fullPath)) return;
                var ext = System.IO.Path.GetExtension(fullPath ?? "").ToLowerInvariant();
                if (ext != ".cs" && ext != ".xaml" && ext != ".csproj" && ext != ".json" && ext != ".md") return;

                var now = DateTime.UtcNow;
                lock (_watchLock)
                {
                    if (_lastEventUtc.TryGetValue(fullPath, out var last) && (now - last).TotalMilliseconds < 350)
                        return;
                    _lastEventUtc[fullPath] = now;
                }
                _lastActivityUtc = DateTime.UtcNow;

                var rel = "";
                try { rel = System.IO.Path.GetRelativePath(_workspaceRoot, fullPath); } catch { rel = fullPath; }
                var file = System.IO.Path.GetFileName(fullPath);

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        AddAtlasMessageOnUi($"FILE {verb} · {rel}");
                        var c = Components.FirstOrDefault(x => string.Equals(x.Name, file, StringComparison.OrdinalIgnoreCase));
                        if (c == null)
                        {
                            if (Components.Count >= 12) return;
                            c = new BuildComponent
                            {
                                Id = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture),
                                Name = file,
                                Type = InferTypeFromPath(rel),
                                Status = BuilderComponentStatus.Building,
                                Progress = 50
                            };
                            Components.Add(c);
                        }
                        else
                        {
                            if (c.Status != BuilderComponentStatus.Complete)
                                c.Status = BuilderComponentStatus.Building;
                            c.Progress = Math.Max(c.Progress, 50);
                        }

                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(600).ConfigureAwait(false);
                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    if (c.Status == BuilderComponentStatus.Building)
                                    {
                                        c.Progress = 100;
                                        c.Status = BuilderComponentStatus.Complete;
                                        RaiseArchitecture();
                                    }
                                }
                                catch
                                {
                                }
                            }));
                        });
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }
        }

        private double GetCategoryProgress(BuilderComponentType type)
        {
            try
            {
                var list = Components.Where(c => c.Type == type).ToList();
                if (list.Count == 0) return 0;
                var sum = list.Sum(c => Math.Max(0, Math.Min(100, c.Progress)));
                return sum / (100.0 * list.Count);
            }
            catch
            {
                return 0;
            }
        }

        private void RaiseArchitecture()
        {
            try
            {
                OnPropertyChanged(nameof(PresentationCount));
                OnPropertyChanged(nameof(BusinessLogicCount));
                OnPropertyChanged(nameof(DataAccessCount));
                OnPropertyChanged(nameof(InfrastructureCount));
                OnPropertyChanged(nameof(PresentationProgress));
                OnPropertyChanged(nameof(BusinessLogicProgress));
                OnPropertyChanged(nameof(DataAccessProgress));
                OnPropertyChanged(nameof(InfrastructureProgress));
            }
            catch
            {
            }
        }

        private void UpdateSubtitle()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_workspaceRoot))
                {
                    BuildSubtitle = "INITIALIZING";
                    return;
                }

                if (_isAgentRunning)
                {
                    if (string.IsNullOrWhiteSpace(BuildSubtitle) || string.Equals(BuildSubtitle, "IDLE · WATCHING WORKSPACE", StringComparison.Ordinal))
                        BuildSubtitle = "BUILD IN PROGRESS";
                    return;
                }

                var busy = Components.Any(c => c.Status == BuilderComponentStatus.Building) || (DateTime.UtcNow - _lastActivityUtc).TotalSeconds < 2;
                BuildSubtitle = busy ? "PROCESSING CHANGES" : "IDLE · WATCHING WORKSPACE";
            }
            catch
            {
            }
        }

        private void LoadDefaultComponents()
        {
            try
            {
                Components.Clear();
                if (string.IsNullOrWhiteSpace(_workspaceRoot))
                    _workspaceRoot = Environment.CurrentDirectory;
                
                var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".xaml", ".cs", ".csproj", ".sln", ".json", ".md", ".ts", ".tsx", ".js", ".jsx"
                };

                var files = new List<(string full, DateTime ts)>();
                var stack = new Stack<string>();
                stack.Push(_workspaceRoot);

                while (stack.Count > 0 && files.Count < 2000)
                {
                    var dir = stack.Pop();
                    try
                    {
                        var dirName = System.IO.Path.GetFileName(dir);
                        if (string.Equals(dirName, "bin", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(dirName, "obj", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(dirName, "node_modules", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(dirName, ".git", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(dirName, ".vs", StringComparison.OrdinalIgnoreCase))
                            continue;

                        foreach (var sub in Directory.GetDirectories(dir))
                            stack.Push(sub);

                        foreach (var file in Directory.GetFiles(dir))
                        {
                            var ext = System.IO.Path.GetExtension(file);
                            if (!allowedExt.Contains(ext)) continue;
                            DateTime ts;
                            try { ts = File.GetLastWriteTimeUtc(file); } catch { ts = DateTime.MinValue; }
                            files.Add((file, ts));
                            if (files.Count >= 2000) break;
                        }
                    }
                    catch
                    {
                    }
                }

                var list = files
                    .OrderByDescending(f => f.ts)
                    .Select(f =>
                    {
                        try { return System.IO.Path.GetRelativePath(_workspaceRoot, f.full); }
                        catch { return f.full; }
                    })
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList();

                var i = 1;
                foreach (var rel in list)
                {
                    var t = InferTypeFromPath(rel);
                    Components.Add(new BuildComponent
                    {
                        Id = i.ToString(CultureInfo.InvariantCulture),
                        Name = System.IO.Path.GetFileName(rel),
                        Type = t,
                        Status = BuilderComponentStatus.Complete,
                        Progress = 100
                    });
                    i++;
                }
            }
            catch
            {
            }
        }

        private static BuilderComponentType InferTypeFromPath(string path)
        {
            var p = (path ?? "").Replace('/', '\\');
            if (p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                return BuilderComponentType.View;
            if (p.IndexOf("\\Agent\\", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("\\Services\\", StringComparison.OrdinalIgnoreCase) >= 0)
                return BuilderComponentType.Logic;
            if (p.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0)
                return BuilderComponentType.Data;
            if (p.IndexOf("Api", StringComparison.OrdinalIgnoreCase) >= 0)
                return BuilderComponentType.Api;
            return BuilderComponentType.Logic;
        }

        private void TryMarkComponentBuildingFromTool(string toolDescription)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(toolDescription)) return;
                var path = ExtractPathFromToolDescription(toolDescription);
                if (string.IsNullOrWhiteSpace(path)) return;
                var file = System.IO.Path.GetFileName(path);
                var c = Components.FirstOrDefault(x => string.Equals(x.Name, file, StringComparison.OrdinalIgnoreCase));
                if (c == null)
                {
                    if (Components.Count >= 6) return;
                    c = new BuildComponent
                    {
                        Id = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture),
                        Name = file,
                        Type = InferTypeFromPath(path),
                        Status = BuilderComponentStatus.Queued,
                        Progress = 0
                    };
                    Components.Add(c);
                }

                if (c.Status == BuilderComponentStatus.Complete) return;
                c.Status = BuilderComponentStatus.Building;
                c.Progress = Math.Max(c.Progress, 10);
            }
            catch
            {
            }
        }

        private void TryMarkComponentCompleteFromTool(string toolDescription, bool success)
        {
            try
            {
                if (!success) return;
                var path = ExtractPathFromToolDescription(toolDescription);
                if (string.IsNullOrWhiteSpace(path)) return;
                var file = System.IO.Path.GetFileName(path);
                var c = Components.FirstOrDefault(x => string.Equals(x.Name, file, StringComparison.OrdinalIgnoreCase));
                if (c == null) return;
                c.Progress = 100;
                c.Status = BuilderComponentStatus.Complete;
            }
            catch
            {
            }
        }

        private static string ExtractPathFromToolDescription(string toolDescription)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(toolDescription ?? "", @"\bpath=([^,]+)");
                if (!m.Success) return "";
                var raw = m.Groups[1].Value.Trim().Trim('"');
                return raw;
            }
            catch
            {
                return "";
            }
        }

        private static string TryFindWorkspaceRoot()
        {
            try
            {
                try
                {
                    var prefs = PreferencesStore.Instance.Current;
                    var saved = (prefs.CodeWorkspaceFolder ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
                        return saved;
                }
                catch
                {
                }

                var start = AppDomain.CurrentDomain.BaseDirectory;
                var dir = new System.IO.DirectoryInfo(start);
                for (var i = 0; i < 8 && dir != null; i++)
                {
                    var csproj = System.IO.Path.Combine(dir.FullName, "AtlasAI.csproj");
                    if (System.IO.File.Exists(csproj))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch
            {
            }
            return "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name == nameof(BuildProgress))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BuildProgressInt)));
        }
    }

    public sealed class ProgressToDashOffsetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var p = value is double d ? d : value is int i ? i : 0;
                var circumference = 251.2;
                if (parameter != null)
                {
                    var s = parameter.ToString();
                    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var c))
                        circumference = c;
                }

                var t = Math.Max(0, Math.Min(100, p));
                var dashOffset = (1.0 - (t / 100.0)) * circumference;
                return dashOffset;
            }
            catch
            {
                return 0.0;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderComponentTypeLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is BuilderComponentType t ? t.ToString().ToUpperInvariant() : "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderComponentStatusLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is BuilderComponentStatus s
                ? s switch
                {
                    BuilderComponentStatus.Queued => "QUEUED",
                    BuilderComponentStatus.Building => "BUILDING",
                    BuilderComponentStatus.Complete => "COMPLETE",
                    _ => ""
                }
                : "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    internal static class AtlasThemeResources
    {
        public static Brush GetBrush(string key, Brush fallback)
        {
            try
            {
                var res = Application.Current?.Resources;
                if (res == null) return fallback;
                if (res.Contains(key) && res[key] is Brush b) return b;
            }
            catch
            {
            }
            return fallback;
        }

        public static Color GetColor(string key, Color fallback)
        {
            try
            {
                var res = Application.Current?.Resources;
                if (res == null) return fallback;
                if (!res.Contains(key)) return fallback;
                if (res[key] is Color c) return c;
                if (res[key] is SolidColorBrush sb) return sb.Color;
            }
            catch
            {
            }
            return fallback;
        }

        public static SolidColorBrush WithAlpha(Color color, byte alpha) =>
            new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public sealed class BuilderComponentAccentBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var type = values.Length > 0 && values[0] is BuilderComponentType t ? t : BuilderComponentType.View;
            var status = values.Length > 1 && values[1] is BuilderComponentStatus s ? s : BuilderComponentStatus.Queued;

            if (status == BuilderComponentStatus.Complete)
                return AtlasThemeResources.GetBrush("AtlasSuccessBrush", Brushes.LimeGreen);

            return type switch
            {
                BuilderComponentType.View => AtlasThemeResources.GetBrush("AtlasCyanBrush", Brushes.Cyan),
                BuilderComponentType.Logic => AtlasThemeResources.GetBrush("AtlasWarningBrush", Brushes.Orange),
                BuilderComponentType.Data => AtlasThemeResources.GetBrush("AtlasSuccessBrush", Brushes.LimeGreen),
                BuilderComponentType.Api => AtlasThemeResources.GetBrush("AtlasPurpleBrush", Brushes.MediumPurple),
                _ => AtlasThemeResources.GetBrush("AtlasTextMutedBrush", Brushes.Gray)
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderComponentBackgroundBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var type = values.Length > 0 && values[0] is BuilderComponentType t ? t : BuilderComponentType.View;
            var status = values.Length > 1 && values[1] is BuilderComponentStatus s ? s : BuilderComponentStatus.Queued;

            if (status == BuilderComponentStatus.Queued)
                return AtlasThemeResources.GetBrush("AtlasGlassBrush", Brushes.Transparent);

            var accentColor = type switch
            {
                BuilderComponentType.View => AtlasThemeResources.GetColor("AtlasCyan", Colors.Cyan),
                BuilderComponentType.Logic => AtlasThemeResources.GetColor("AtlasWarning", Colors.Orange),
                BuilderComponentType.Data => AtlasThemeResources.GetColor("AtlasSuccess", Colors.LimeGreen),
                BuilderComponentType.Api => AtlasThemeResources.GetColor("AtlasPurple", Colors.MediumPurple),
                _ => AtlasThemeResources.GetColor("AtlasTextMuted", Colors.Gray)
            };

            if (status == BuilderComponentStatus.Complete)
                accentColor = AtlasThemeResources.GetColor("AtlasSuccess", Colors.LimeGreen);

            return AtlasThemeResources.WithAlpha(accentColor, 0x10);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderComponentBorderBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var type = values.Length > 0 && values[0] is BuilderComponentType t ? t : BuilderComponentType.View;
            var status = values.Length > 1 && values[1] is BuilderComponentStatus s ? s : BuilderComponentStatus.Queued;

            if (status == BuilderComponentStatus.Queued)
                return AtlasThemeResources.GetBrush("AtlasGlassBorderBrush", Brushes.DimGray);

            var accentColor = type switch
            {
                BuilderComponentType.View => AtlasThemeResources.GetColor("AtlasCyan", Colors.Cyan),
                BuilderComponentType.Logic => AtlasThemeResources.GetColor("AtlasWarning", Colors.Orange),
                BuilderComponentType.Data => AtlasThemeResources.GetColor("AtlasSuccess", Colors.LimeGreen),
                BuilderComponentType.Api => AtlasThemeResources.GetColor("AtlasPurple", Colors.MediumPurple),
                _ => AtlasThemeResources.GetColor("AtlasTextMuted", Colors.Gray)
            };

            if (status == BuilderComponentStatus.Complete)
                accentColor = AtlasThemeResources.GetColor("AtlasSuccess", Colors.LimeGreen);

            return AtlasThemeResources.WithAlpha(accentColor, 0x55);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderMessageHeaderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is BuilderMessageSender s ? (s == BuilderMessageSender.Atlas ? "ATLAS" : "USER") : "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderMessageHeaderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not BuilderMessageSender s)
                return AtlasThemeResources.GetBrush("AtlasCyanBrush", Brushes.Cyan);

            return s == BuilderMessageSender.Atlas
                ? AtlasThemeResources.GetBrush("AtlasCyanBrush", Brushes.Cyan)
                : AtlasThemeResources.GetBrush("AtlasWarningBrush", Brushes.Orange);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderMessageContainerBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not BuilderMessageSender s)
                return AtlasThemeResources.GetBrush("AtlasAssistantMsgGradient", AtlasThemeResources.GetBrush("AtlasGlassSolidBrush", Brushes.Transparent));

            return s == BuilderMessageSender.Atlas
                ? AtlasThemeResources.GetBrush("AtlasAssistantMsgGradient", AtlasThemeResources.GetBrush("AtlasGlassSolidBrush", Brushes.Transparent))
                : AtlasThemeResources.GetBrush("AtlasUserMsgGradient", AtlasThemeResources.GetBrush("AtlasGlassSolidBrush", Brushes.Transparent));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderMessageBorderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not BuilderMessageSender s)
                return AtlasThemeResources.GetBrush("AtlasGlassBorderBrush", Brushes.DimGray);

            return s == BuilderMessageSender.Atlas
                ? AtlasThemeResources.GetBrush("AtlasCyanGlowBrush", AtlasThemeResources.GetBrush("AtlasGlassBorderBrush", Brushes.DimGray))
                : AtlasThemeResources.GetBrush("AtlasGlassBorderBrush", Brushes.DimGray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderMessageForegroundBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not BuilderMessageSender s)
                return AtlasThemeResources.GetBrush("AtlasTextSecondaryBrush", Brushes.White);

            return s == BuilderMessageSender.Atlas
                ? AtlasThemeResources.GetBrush("AtlasTextSecondaryBrush", Brushes.White)
                : AtlasThemeResources.GetBrush("AtlasTextPrimaryBrush", Brushes.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderMessageAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is BuilderMessageSender s && s == BuilderMessageSender.User
                ? System.Windows.HorizontalAlignment.Right
                : System.Windows.HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BuilderProgressWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var w = values.Length > 0 && values[0] is double dw ? dw : 0;
                var v = values.Length > 1 && values[1] is double dv ? dv : values.Length > 1 && values[1] is int iv ? iv : 0;
                var max = values.Length > 2 && values[2] is double dm ? dm : values.Length > 2 && values[2] is int im ? im : 100;
                if (w <= 0) return 0.0;
                if (max <= 0) return 0.0;
                var ratio = Math.Max(0, Math.Min(1, v / max));
                return w * ratio;
            }
            catch
            {
                return 0.0;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class ProgressToScaleXConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var v = value is double d ? d : value is int i ? i : 0;
                if (v <= 0) return 0.0;
                if (v >= 1) return 1.0;
                return v;
            }
            catch
            {
                return 0.0;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class ComponentTypeIconConverter : IValueConverter
    {
        private static readonly Geometry Layout = Geometry.Parse("M3 3h18v18H3V3z M3 9h18 M9 3v18");
        private static readonly Geometry Cpu = Geometry.Parse("M9 2v2H7a2 2 0 0 0-2 2v2H3v2h2v2H3v2h2v2a2 2 0 0 0 2 2h2v2h2v-2h2v2h2v-2h2a2 2 0 0 0 2-2v-2h2v-2h-2v-2h2v-2h-2V6a2 2 0 0 0-2-2h-2V2h-2v2h-2V2H9z M8 8h8v8H8V8z");
        private static readonly Geometry Database = Geometry.Parse("M12 2c-4.4 0-8 1.3-8 3v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5c0-1.7-3.6-3-8-3z M4 5c0 1.7 3.6 3 8 3s8-1.3 8-3 M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3 M4 19c0 1.7 3.6 3 8 3s8-1.3 8-3");
        private static readonly Geometry Wifi = Geometry.Parse("M5 8c4.7-4.7 12.3-4.7 17 0 M8.5 11.5c2.8-2.8 7.2-2.8 10 0 M12 15a2 2 0 0 1 2 2 2 2 0 0 1-4 0 2 2 0 0 1 2-2");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is not BuilderComponentType t) return Layout;
                return t switch
                {
                    BuilderComponentType.View => Layout,
                    BuilderComponentType.Logic => Cpu,
                    BuilderComponentType.Data => Database,
                    BuilderComponentType.Api => Wifi,
                    _ => Layout
                };
            }
            catch
            {
                return Layout;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
