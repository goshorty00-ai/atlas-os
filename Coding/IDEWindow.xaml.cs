using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using Microsoft.Web.WebView2.Core;
using AtlasAI.Agent;
using AtlasAI.Coding.Services;

namespace AtlasAI.Coding
{
    /// <summary>
    /// Atlas IDE - Cursor/Kiro-style embedded code editor with AI assistant.
    /// Step 30: v1 implementation with Monaco Editor, file explorer, terminal, and AI panel.
    /// </summary>
    public partial class IDEWindow : Window
    {
        // === State ===
        private string? _projectPath;
        private readonly Dictionary<string, OpenFile> _openFiles = new();
        private string? _activeFilePath;
        private Process? _terminalProcess;
        private CancellationTokenSource? _terminalCts;
        private CancellationTokenSource? _aiCts;
        private readonly IDELogger _logger = new();
        private Window? _embeddedHostWindow;
        private Process? _buildProcess;
        private Process? _testProcess;

        public event EventHandler? EmbeddedCloseRequested;

        private bool _focusMode;
        private GridLength _iconColWidth;
        private GridLength _explorerColWidth;
        private GridLength _leftSplitColWidth;
        private GridLength _editorColWidth;
        private GridLength _rightSplitColWidth;
        private GridLength _aiPanelColWidth;
        private double _explorerMinWidth;
        private double _explorerMaxWidth;
        private double _aiMinWidth;
        private double _aiMaxWidth;

        private bool _preferVisualStudio = true;
        private object? _vsDte;
        private string _vsProgId = "";
        private bool _vsConnecting;
        private string _agentRole = "Builder";
        private List<ProjectTask> _projectTasks = new();
        private string _projectTasksPath = "";

        private readonly ObservableCollection<CommandPaletteEntry> _commandPaletteEntries = new();
        private int _commandPaletteIndex = -1;
        private CancellationTokenSource? _commandPaletteCts;
        private bool _indexReady;
        private bool _indexBuilding;
        
        // === AI State ===
        private AIMode _currentAIMode = AIMode.Ask;
        private string? _currentSelection;
        private int _selectionStartLine;
        private int _selectionEndLine;
        
        // === Events ===
        public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;
        public event EventHandler<FileOpenedEventArgs>? FileOpened;
        public event EventHandler<FileSavedEventArgs>? FileSaved;

        public IDEWindow()
        {
            InitializeComponent();
            try { CommandPaletteResults.ItemsSource = _commandPaletteEntries; } catch { }
            try
            {
                if (AgentRoleCombo?.SelectedItem is ComboBoxItem it && it.Content is string s && !string.IsNullOrWhiteSpace(s))
                    _agentRole = s.Trim();
            }
            catch { }

            InitializeAsync();
        }

        public void SetEmbeddedHost(Window hostWindow)
        {
            _embeddedHostWindow = hostWindow;
        }

        private async void InitializeAsync()
        {
            try
            {
                CacheLayoutDefaults();

                // Initialize WebView2 for Monaco Editor
                await EditorWebView.EnsureCoreWebView2Async();
                EditorWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                EditorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                EditorWebView.CoreWebView2.WebMessageReceived += EditorWebView_WebMessageReceived;
                
                // Load Monaco Editor HTML
                var monacoHtml = GetMonacoEditorHtml();
                EditorWebView.NavigateToString(monacoHtml);
                
                // Initialize optional Figma/Trae web UI if present
                TryInitFigmaWebUI();
                
                _logger.Log("Initialized", new { });
                StatusText.Text = "Ready - Open a folder to start";
                UpdateVisualStudioUiState();
                _ = Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        await Task.Delay(1200);
                        await EnsureVisualStudioConnectedAsync(startIfNeeded: false);
                    }
                    catch
                    {
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IDE] Init error: {ex.Message}");
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void UpdateVisualStudioUiState()
        {
            try
            {
                if (_vsDte != null)
                {
                    VsConnectBtn.Content = "VS✓";
                    VsConnectBtn.ToolTip = "Visual Studio connected";
                }
                else
                {
                    VsConnectBtn.Content = "VS";
                    VsConnectBtn.ToolTip = "Connect to Visual Studio";
                }
            }
            catch
            {
            }
        }

        private void TryInitFigmaWebUI()
        {
            try
            {
                FigmaHostBorder.Visibility = Visibility.Collapsed;
                EditorWebView.Visibility = Visibility.Visible;
            }
            catch
            {
            }
        }

        private void FigmaWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<Dictionary<string, object?>>(e.WebMessageAsJson);
                if (msg == null || !msg.TryGetValue("type", out var t) || t is not string type) return;

                if (type == "list_files")
                {
                    var root = _projectPath ?? Environment.CurrentDirectory;
                    var tree = BuildFileTree(root);
                    var payload = new
                    {
                        type = "files_tree",
                        root = tree
                    };
                    var json = JsonSerializer.Serialize(payload);
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                    return;
                }
                if (type == "open_file" && msg.TryGetValue("path", out var p) && p is string path && File.Exists(path))
                {
                    var content = File.ReadAllText(path);
                    var payload = new { type = "file_content", path, content };
                    var json = JsonSerializer.Serialize(payload);
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                    OpenFilePreferred(path);
                    return;
                }
                if (type == "run_command" && msg.TryGetValue("command", out var c) && c is string command && !string.IsNullOrWhiteSpace(command))
                {
                    RunTerminalCommand(command);
                    return;
                }
                if (type == "start_build")
                {
                    _ = RunDotnetBuildAndStreamAsync();
                    return;
                }
                if (type == "start_rebuild")
                {
                    _ = RunDotnetRebuildAndStreamAsync();
                    return;
                }
                if (type == "start_tests" || type == "start_test")
                {
                    _ = RunDotnetTestAndStreamAsync();
                    return;
                }
                if (type == "cancel_build" || type == "cancel_tests" || type == "cancel_test")
                {
                    CancelBuildAndTests();
                    return;
                }
                if (type == "save_file" && msg.TryGetValue("path", out var sp) && sp is string savePath && msg.TryGetValue("content", out var sc) && sc is string saveContent)
                {
                    try
                    {
                        File.WriteAllText(savePath, saveContent);
                        var payload = new { type = "file_saved", path = savePath };
                        var json = JsonSerializer.Serialize(payload);
                        FigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                    }
                    catch (Exception ex)
                    {
                        var payload = new { type = "file_save_error", path = savePath, error = ex.Message };
                        var json = JsonSerializer.Serialize(payload);
                        FigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                    }
                    return;
                }
                if (type == "chat_message" && msg.TryGetValue("text", out var tx) && tx is string text && !string.IsNullOrWhiteSpace(text))
                {
                    _ = RunAgentChatAsync(text);
                    return;
                }
            }
            catch { }
        }

        private object BuildFileTree(string path)
        {
            try
            {
                var dir = new DirectoryInfo(path);
                var node = new
                {
                    id = path,
                    name = dir.Name,
                    type = "folder",
                    isOpen = true,
                    children = Directory.GetDirectories(path)
                        .Where(d => !Path.GetFileName(d).StartsWith(".") && Path.GetFileName(d) != "node_modules" && Path.GetFileName(d) != "bin" && Path.GetFileName(d) != "obj")
                        .Select(d => BuildFileTree(d))
                        .Concat(Directory.GetFiles(path)
                            .Where(f => !Path.GetFileName(f).StartsWith("."))
                            .Select(f => new
                            {
                                id = f,
                                name = Path.GetFileName(f),
                                type = "file",
                                extension = Path.GetExtension(f).Trim('.'),
                                content = (string?)null
                            }))
                        .ToList()
                };
                return node;
            }
            catch
            {
                return new { id = path, name = Path.GetFileName(path), type = "folder", isOpen = true, children = Array.Empty<object>() };
            }
        }

        private void ActivateFigmaOnlyMode()
        {
            try
            {
                IconSidebarBorder.Visibility = Visibility.Collapsed;
                ExplorerBorder.Visibility = Visibility.Collapsed;
                LeftSplitter.Visibility = Visibility.Collapsed;
                RightSplitter.Visibility = Visibility.Collapsed;
                AIPanelBorder.Visibility = Visibility.Collapsed;
                TabBar.Visibility = Visibility.Collapsed;
                TerminalTabBtn.Visibility = Visibility.Collapsed;
                ProblemsTabBtn.Visibility = Visibility.Collapsed;
                OutputTabBtn.Visibility = Visibility.Collapsed;
                TerminalPanel.Visibility = Visibility.Collapsed;
                ProblemsPanel.Visibility = Visibility.Collapsed;
                OutputPanel.Visibility = Visibility.Collapsed;
                
                IconCol.Width = new GridLength(0);
                ExplorerCol.Width = new GridLength(0);
                LeftSplitCol.Width = new GridLength(0);
                RightSplitCol.Width = new GridLength(0);
                AIPanelCol.Width = new GridLength(0);
                
                // Collapse top and bottom rows
                try
                {
                    var rootGrid = (Grid)Content;
                    if (rootGrid.RowDefinitions.Count >= 3)
                    {
                        rootGrid.RowDefinitions[0].Height = new GridLength(0);
                        rootGrid.RowDefinitions[2].Height = new GridLength(0);
                    }
                }
                catch { }
            }
            catch { }
        }

        private async Task<bool> RunDotnetBuildAndStreamAsync()
        {
            try
            {
                try
                {
                    if (_buildProcess != null && !_buildProcess.HasExited)
                        _buildProcess.Kill(true);
                }
                catch { }

                var root = _projectPath;
                if (string.IsNullOrWhiteSpace(root)) root = Environment.CurrentDirectory;

                string? target = null;
                try
                {
                    target = Directory.GetFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? Directory.GetFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                }
                catch { }

                var args = string.IsNullOrWhiteSpace(target) ? "build" : $"build \"{target}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = args,
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _buildProcess = p;

                p.OutputDataReceived += (s, e) =>
                {
                    var line = e.Data;
                    if (string.IsNullOrEmpty(line)) return;
                    var payload = JsonSerializer.Serialize(new { type = "build_log", kind = "dotnet", line });
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    var line = e.Data;
                    if (string.IsNullOrEmpty(line)) return;
                    var payload = JsonSerializer.Serialize(new { type = "build_log", kind = "dotnet", line });
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload);
                };

                var started = JsonSerializer.Serialize(new { type = "build_started", kind = "dotnet" });
                FigmaWebView.CoreWebView2?.PostWebMessageAsJson(started);

                try
                {
                    var infoLine = string.IsNullOrWhiteSpace(target)
                        ? $"> dotnet {args}"
                        : $"> dotnet {args}\n> cwd: {root}";
                    var info = JsonSerializer.Serialize(new { type = "build_log", kind = "dotnet", line = infoLine });
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(info);
                }
                catch { }

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await Task.Run(() => p.WaitForExit());

                var success = p.ExitCode == 0;
                var done = JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success });
                FigmaWebView.CoreWebView2?.PostWebMessageAsJson(done);
                try { _buildProcess = null; } catch { }

                return success;
            }
            catch (Exception ex)
            {
                try
                {
                    var err = JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success = false, error = ex.Message });
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(err);
                }
                catch { }
                try { _buildProcess = null; } catch { }
                return false;
            }
        }

        private void CancelBuildAndTests()
        {
            try
            {
                try
                {
                    if (_buildProcess != null && !_buildProcess.HasExited)
                        _buildProcess.Kill(true);
                }
                catch { }

                try
                {
                    if (_testProcess != null && !_testProcess.HasExited)
                        _testProcess.Kill(true);
                }
                catch { }

                try
                {
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success = false, error = "Cancelled" }));
                }
                catch { }

                try
                {
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "test_complete", success = false, error = "Cancelled" }));
                }
                catch { }
            }
            catch
            {
            }
            finally
            {
                try { _buildProcess = null; } catch { }
                try { _testProcess = null; } catch { }
            }
        }

        private async Task<bool> RunDotnetRebuildAndStreamAsync()
        {
            try
            {
                try
                {
                    if (_buildProcess != null && !_buildProcess.HasExited)
                        _buildProcess.Kill(true);
                }
                catch { }

                var root = _projectPath;
                if (string.IsNullOrWhiteSpace(root)) root = Environment.CurrentDirectory;

                string? target = null;
                try
                {
                    target = Directory.GetFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? Directory.GetFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                }
                catch { }

                var started = JsonSerializer.Serialize(new { type = "build_started", kind = "dotnet" });
                FigmaWebView.CoreWebView2?.PostWebMessageAsJson(started);

                var cleanArgs = string.IsNullOrWhiteSpace(target) ? "clean" : $"clean \"{target}\"";
                var buildArgs = string.IsNullOrWhiteSpace(target) ? "build" : $"build \"{target}\"";

                void Push(string line)
                {
                    if (string.IsNullOrEmpty(line)) return;
                    try
                    {
                        FigmaWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "build_log", kind = "dotnet", line }));
                    }
                    catch { }
                }

                try
                {
                    var infoLine = string.IsNullOrWhiteSpace(target)
                        ? $"> dotnet {cleanArgs}\n> dotnet {buildArgs}"
                        : $"> dotnet {cleanArgs}\n> dotnet {buildArgs}\n> cwd: {root}";
                    Push(infoLine);
                }
                catch { }

                async Task<int> RunStepAsync(string args)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = args,
                        WorkingDirectory = root,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    _buildProcess = p;
                    p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Push(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Push(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    await Task.Run(() => p.WaitForExit());
                    return p.ExitCode;
                }

                var cleanExit = await RunStepAsync(cleanArgs);
                if (cleanExit != 0)
                {
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success = false }));
                    return false;
                }

                var buildExit = await RunStepAsync(buildArgs);
                var success = buildExit == 0;
                FigmaWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success }));
                return success;
            }
            catch (Exception ex)
            {
                try
                {
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success = false, error = ex.Message }));
                }
                catch { }
                return false;
            }
            finally
            {
                try { _buildProcess = null; } catch { }
            }
        }

        private async Task<bool> RunDotnetTestAndStreamAsync()
        {
            try
            {
                try
                {
                    if (_testProcess != null && !_testProcess.HasExited)
                        _testProcess.Kill(true);
                }
                catch { }

                var root = _projectPath;
                if (string.IsNullOrWhiteSpace(root)) root = Environment.CurrentDirectory;

                string? target = null;
                try
                {
                    target = Directory.GetFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? Directory.GetFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                }
                catch { }

                var args = string.IsNullOrWhiteSpace(target) ? "test" : $"test \"{target}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = args,
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _testProcess = p;
                p.OutputDataReceived += (s, e) =>
                {
                    var line = e.Data;
                    if (string.IsNullOrEmpty(line)) return;
                    var payload = JsonSerializer.Serialize(new { type = "test_log", line });
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    var line = e.Data;
                    if (string.IsNullOrEmpty(line)) return;
                    var payload = JsonSerializer.Serialize(new { type = "test_log", line });
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload);
                };
                var started = JsonSerializer.Serialize(new { type = "test_started" });
                FigmaWebView.CoreWebView2?.PostWebMessageAsJson(started);
                try
                {
                    var infoLine = string.IsNullOrWhiteSpace(target)
                        ? $"> dotnet {args}"
                        : $"> dotnet {args}\n> cwd: {root}";
                    var info = JsonSerializer.Serialize(new { type = "test_log", line = infoLine });
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(info);
                }
                catch { }

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await Task.Run(() => p.WaitForExit());
                var success = p.ExitCode == 0;
                var done = JsonSerializer.Serialize(new { type = "test_complete", success });
                FigmaWebView.CoreWebView2?.PostWebMessageAsJson(done);
                try { _testProcess = null; } catch { }
                return success;
            }
            catch (Exception ex)
            {
                try
                {
                    var err = JsonSerializer.Serialize(new { type = "test_complete", success = false, error = ex.Message });
                    FigmaWebView.CoreWebView2?.PostWebMessageAsJson(err);
                }
                catch { }
                try { _testProcess = null; } catch { }
                return false;
            }
        }
        
        private async Task RunAgentChatAsync(string text)
        {
            _aiCts?.Cancel();
            _aiCts?.Dispose();
            _aiCts = new CancellationTokenSource();

            var trimmed = (text ?? string.Empty).Trim();
            bool isContinueLike = !string.IsNullOrWhiteSpace(trimmed) &&
                                  (trimmed.StartsWith("continue", StringComparison.OrdinalIgnoreCase) ||
                                   trimmed.StartsWith("resume", StringComparison.OrdinalIgnoreCase));

            bool isAuditLike = !string.IsNullOrWhiteSpace(text) &&
                               (text.Contains("audit", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("review", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("analy", StringComparison.OrdinalIgnoreCase));

            bool shouldValidate = isAuditLike || isContinueLike;

            string? lastToolName = null;

            void Post(object payload)
            {
                try
                {
                    var json = JsonSerializer.Serialize(payload);
                    Dispatcher.BeginInvoke(() => FigmaWebView.CoreWebView2?.PostWebMessageAsJson(json));
                }
                catch { }
            }

            try
            {
                var trace = new System.Collections.Generic.List<string>(64);
                var originalText = text;
                var prompt = text;
                const int TimeoutMsPerAttempt = 120_000;

                var phase = "thinking";
                var action = "";

                void PostProgress()
                {
                    Post(new { type = "agent_progress", phase, action });
                }

                string BuildContinuationPrompt(string original, System.Collections.Generic.List<string> traceLines)
                {
                    const int MaxTraceLines = 40;
                    var tail = traceLines.Count <= MaxTraceLines
                        ? traceLines
                        : traceLines.GetRange(traceLines.Count - MaxTraceLines, MaxTraceLines);
                    var traceText = string.Join("\n", tail);
                    return
                        "Continue from the previous attempt.\n" +
                        "- Original request:\n" + original + "\n\n" +
                        "- Partial progress log (may be incomplete):\n" + traceText + "\n\n" +
                        "Rules: Keep the user updated with a short visible plan and the current action. Resume where you left off.";
                }

                var root = _projectPath ?? Environment.CurrentDirectory;

                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    phase = "thinking";
                    action = "";
                    Post(new { type = "agent_started", attempt, timeoutMs = TimeoutMsPerAttempt, phase, action });

                    // Minimal visible plan so the user can see what's happening.
                    Post(new
                    {
                        type = "plan_update",
                        items = new object[]
                        {
                            new { id = "1", title = "Understand request", status = "active" },
                            new { id = "2", title = "Gather context", status = "pending" },
                            new { id = "3", title = "Apply changes", status = "pending" },
                            new { id = "4", title = "Validate (build/tests)", status = "pending" },
                        }
                    });

                    using var perAttemptTimeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(TimeoutMsPerAttempt));
                    using var attemptLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(_aiCts.Token, perAttemptTimeoutCts.Token);
                    var attemptCt = attemptLinkedCts.Token;

                    using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(attemptCt);
                    var heartbeatTask = Task.Run(async () =>
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromSeconds(1));
                            while (await timer.WaitForNextTickAsync(heartbeatCts.Token))
                            {
                                var elapsedMs = (long)sw.Elapsed.TotalMilliseconds;
                                var remainingMs = Math.Max(0, TimeoutMsPerAttempt - elapsedMs);
                                Post(new { type = "agent_tick", attempt, elapsedMs, remainingMs, phase, action });
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch { }
                    });

                    try
                    {
                        var agent = new AgentOrchestrator(root);

                        var planStep2Activated = false;

                        agent.SystemPromptPrefix =
                            "You are Atlas Builder Agent — a senior full-stack engineer for this repo (WPF/.NET + embedded UI).\n" +
                            "Goal: ship working improvements with minimal, safe diffs. Fix root causes.\n" +
                            "Tooling discipline: avoid broad list_directory / recursive scans; prefer targeted search_files/read_file.\n" +
                            "Build discipline: only run builds/tests when asked or after code changes to verify.\n" +
                            "Output format: 'Summary' (1-3 bullets), then 'Changes' (file list), then 'Next' (1-2 suggested follow-ups).";

                        agent.OnThinking += (_, msg) =>
                        {
                            try
                            {
                                phase = "thinking";
                                action = (msg ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(action))
                                    trace.Add($"[thinking] {action}");
                                PostProgress();
                                Post(new { type = "chat_status", text = msg });
                            }
                            catch { }
                        };
                        agent.OnToolExecuting += (_, tool) =>
                        {
                            lastToolName = tool;
                            phase = "acting";
                            action = tool ?? "";
                            if (!string.IsNullOrWhiteSpace(action))
                                trace.Add($"[tool] {action}");
                            PostProgress();

                            if (!planStep2Activated)
                            {
                                planStep2Activated = true;
                                Post(new
                                {
                                    type = "plan_update",
                                    items = new object[]
                                    {
                                        new { id = "1", title = "Understand request", status = "complete" },
                                        new { id = "2", title = "Gather context", status = "active" },
                                        new { id = "3", title = "Apply changes", status = "pending" },
                                        new { id = "4", title = "Validate (build/tests)", status = "pending" },
                                    }
                                });
                            }

                            if (isAuditLike)
                            {
                                Post(new { type = "chat_status", text = $"Running audit: {tool}…" });
                            }

                            Post(new { type = "tool_start", tool });
                        };
                        agent.OnToolResult += (_, result) =>
                        {
                            var output = result?.Output ?? "";
                            if (string.IsNullOrWhiteSpace(output)) return;

                            const int MaxLines = 200;
                            const int MaxCharsPerLine = 2000;

                            var lines = output.Replace("\r\n", "\n").Split('\n');
                            var sent = 0;
                            string? firstNonEmpty = null;

                            foreach (var raw in lines)
                            {
                                var ln = raw?.TrimEnd() ?? "";
                                if (string.IsNullOrWhiteSpace(ln)) continue;
                                firstNonEmpty ??= ln;

                                sent++;
                                if (sent > MaxLines)
                                {
                                    Post(new { type = "build_log", kind = "agent", line = $"…(truncated after {MaxLines} lines)…" });
                                    break;
                                }

                                if (ln.Length > MaxCharsPerLine)
                                    ln = ln.Substring(0, MaxCharsPerLine) + "…(truncated)…";

                                Post(new { type = "build_log", kind = "agent", line = ln });
                            }

                            if (!string.IsNullOrWhiteSpace(firstNonEmpty))
                            {
                                trace.Add($"[out] {firstNonEmpty.Replace("\r", "").Replace("\n", " ")}");
                            }
                        };
                        agent.OnResponse += (_, resp) =>
                        {
                            phase = "complete";
                            action = "";
                            PostProgress();
                            Post(new { type = "chat_response", text = resp });
                        };
                        agent.OnError += (_, err) =>
                        {
                            phase = "error";
                            action = "";
                            PostProgress();
                            Post(new { type = "chat_error", text = err });
                        };

                        await agent.RunAsync(prompt).WaitAsync(attemptCt);

                        // Validation for audit/continue: dotnet build + dotnet test before declaring ACTION COMPLETE.
                        if (shouldValidate)
                        {
                            phase = "validating";
                            action = "dotnet build";
                            PostProgress();
                            Post(new { type = "chat_status", text = "Validating: dotnet build…" });
                            Post(new
                            {
                                type = "plan_update",
                                items = new object[]
                                {
                                    new { id = "1", title = "Understand request", status = "complete" },
                                    new { id = "2", title = "Gather context", status = "complete" },
                                    new { id = "3", title = "Apply changes", status = "complete" },
                                    new { id = "4", title = "Validate (build/tests)", status = "active" },
                                }
                            });

                            var buildOk = await RunDotnetBuildAndStreamAsync();
                            var testOk = buildOk;
                            if (buildOk)
                            {
                                action = "dotnet test";
                                PostProgress();
                                Post(new { type = "chat_status", text = "Validating: dotnet test…" });
                                testOk = await RunDotnetTestAndStreamAsync();
                            }

                            Post(new
                            {
                                type = "plan_update",
                                items = new object[]
                                {
                                    new { id = "1", title = "Understand request", status = "complete" },
                                    new { id = "2", title = "Gather context", status = "complete" },
                                    new { id = "3", title = "Apply changes", status = "complete" },
                                    new { id = "4", title = "Validate (build/tests)", status = (buildOk && testOk) ? "complete" : "active" },
                                }
                            });

                            Post(new { type = "build_complete", kind = "agent", success = buildOk && testOk });
                        }
                        else
                        {
                            Post(new { type = "build_complete", kind = "agent", success = true });
                        }
                        break;
                    }
                    catch (OperationCanceledException) when (perAttemptTimeoutCts.IsCancellationRequested && attempt == 1)
                    {
                        trace.Add("[timeout] attempt 1 timed out");
                        phase = "continuing";
                        action = "Starting follow-up…";
                        PostProgress();
                        Post(new { type = "chat_status", text = "Request timed out — continuing in a new follow-up with prior context…" });
                        prompt = BuildContinuationPrompt(originalText, trace);
                        continue;
                    }
                    catch (OperationCanceledException)
                    {
                        // If the user cancelled, treat as cancel; otherwise timeout.
                        var isUserCancel = _aiCts?.IsCancellationRequested == true;
                        Post(new { type = "chat_error", text = isUserCancel ? "Cancelled." : "Request timed out. Try again (or switch model/provider), or ask in smaller chunks." });
                        Post(new { type = "build_complete", kind = "agent", success = false });
                        break;
                    }
                    finally
                    {
                        try { heartbeatCts.Cancel(); } catch { }
                        try { await heartbeatTask; } catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Post(new { type = "chat_error", text = "Request timed out. Try again (or switch model/provider), or ask in smaller chunks." });
                Post(new { type = "build_complete", kind = "agent", success = false });
            }
            catch (Exception ex)
            {
                Post(new { type = "chat_error", text = ex.Message });
                Post(new { type = "build_complete", kind = "agent", success = false });
            }
            finally
            {
                try
                {
                    _aiCts?.Dispose();
                }
                catch { }
                _aiCts = null;
            }
        }

        private void CacheLayoutDefaults()
        {
            try
            {
                _iconColWidth = IconCol.Width;
                _explorerColWidth = ExplorerCol.Width;
                _leftSplitColWidth = LeftSplitCol.Width;
                _editorColWidth = EditorCol.Width;
                _rightSplitColWidth = RightSplitCol.Width;
                _aiPanelColWidth = AIPanelCol.Width;

                _explorerMinWidth = ExplorerCol.MinWidth;
                _explorerMaxWidth = ExplorerCol.MaxWidth;
                _aiMinWidth = AIPanelCol.MinWidth;
                _aiMaxWidth = AIPanelCol.MaxWidth;
            }
            catch
            {
            }
        }

        #region Window Controls

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
            {
                try
                {
                    if (_embeddedHostWindow != null && _embeddedHostWindow != this)
                        _embeddedHostWindow.DragMove();
                    else
                        DragMove();
                }
                catch
                {
                }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var target = (_embeddedHostWindow != null && _embeddedHostWindow != this) ? _embeddedHostWindow : this;
                target.WindowState = WindowState.Minimized;
            }
            catch
            {
            }
        }
        
        private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
        
        private void ToggleMaximize()
        {
            try
            {
                var target = (_embeddedHostWindow != null && _embeddedHostWindow != this) ? _embeddedHostWindow : this;
                target.WindowState = target.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            catch
            {
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var unsaved = _openFiles.Values.Where(f => f.IsDirty).ToList();
                if (unsaved.Any())
                {
                    var result = MessageBox.Show(
                        $"You have {unsaved.Count} unsaved file(s). Save before closing?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Cancel) return;
                    if (result == MessageBoxResult.Yes)
                    {
                        foreach (var file in unsaved)
                            SaveFile(file.Path);
                    }
                }

                if (_embeddedHostWindow != null && _embeddedHostWindow != this)
                {
                    EmbeddedCloseRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }

                _terminalProcess?.Kill();
                Close();
            }
            catch
            {
            }
        }
        
        // New toolbar buttons
        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_projectPath))
            {
                OpenFolder_Click(sender, e);
                if (string.IsNullOrEmpty(_projectPath)) return;
            }

            EnsureProjectTasks();
            TerminalTab_Click(sender, e);

            var runCandidates = _projectTasks
                .Where(t => t.Id == "dotnet_run" || (t.Id.StartsWith("npm_", StringComparison.OrdinalIgnoreCase) && t.Id != "npm_install"))
                .ToList();

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || runCandidates.Count > 1)
            {
                ShowRunTasksMenu();
                return;
            }

            var task = GetDefaultRunTask();
            if (task == null)
            {
                StatusText.Text = "No runnable tasks detected";
                return;
            }

            StatusText.Text = task.Title;
            RunTerminalCommand(task.Command);
        }
        
        private void Debug_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_projectPath))
            {
                OpenFolder_Click(sender, e);
                if (string.IsNullOrEmpty(_projectPath)) return;
            }

            EnsureProjectTasks();
            TerminalTab_Click(sender, e);
            var task = _projectTasks.FirstOrDefault(t => t.Id == "dotnet_build_debug") ?? GetDefaultBuildTask();
            if (task == null)
            {
                StatusText.Text = "No build task detected";
                return;
            }

            StatusText.Text = task.Title;
            RunTerminalCommand(task.Command);
        }
        
        // Sidebar icons
        private void FilesIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FileTree.Focus();
                StatusText.Text = "Explorer";
            }
            catch
            {
            }
        }

        private void SearchIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CommandPalette.Focus();
                CommandPalette.SelectAll();
                StatusText.Text = "Search";
            }
            catch
            {
            }
        }

        private void GitIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_projectPath))
                {
                    OpenFolder_Click(sender, e);
                    return;
                }

                TerminalTab_Click(sender, e);
                RunTerminalCommand("git status");
                StatusText.Text = "Git status";
            }
            catch
            {
            }
        }

        private void DebugIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_projectPath))
                {
                    OpenFolder_Click(sender, e);
                    return;
                }

                TerminalTab_Click(sender, e);
                RunTerminalCommand("dotnet build --configuration Debug");
                StatusText.Text = "Debug build";
            }
            catch
            {
            }
        }

        private void ExtIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (EnsureVisualStudioConnected(false))
                {
                    try
                    {
                        ((dynamic)_vsDte!).ExecuteCommand("Tools.ManageExtensions");
                        StatusText.Text = "Visual Studio: Manage Extensions";
                        return;
                    }
                    catch
                    {
                    }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://marketplace.visualstudio.com/search?target=VS&term=",
                    UseShellExecute = true
                });
                StatusText.Text = "Visual Studio Marketplace";
            }
            catch
            {
            }
        }

        private static void OpenInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;

                var args = "";
                if (File.Exists(path))
                    args = $"/select,\"{path}\"";
                else if (Directory.Exists(path))
                    args = $"\"{path}\"";
                else
                    args = "";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = args,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void ProjectSwitcher_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                OpenFolder_Click(sender, new RoutedEventArgs());
            }
            catch
            {
            }
        }

        private void SettingsAI_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = _embeddedHostWindow ?? this;
                var w = new AtlasAI.SettingsWindow
                {
                    Owner = owner,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                w.Show();
            }
            catch
            {
            }
        }

        private async void ConnectVisualStudio_Click(object sender, RoutedEventArgs e)
        {
            if (_vsConnecting) return;
            _vsConnecting = true;

            try
            {
                if (_vsDte != null)
                {
                    try { ((dynamic)_vsDte).MainWindow.Activate(); } catch { }
                    StatusText.Text = "Visual Studio activated";
                    UpdateVisualStudioUiState();
                    return;
                }

                StatusText.Text = "Connecting to Visual Studio…";
                VsConnectBtn.Content = "…";

                var ok = await EnsureVisualStudioConnectedAsync(startIfNeeded: true);
                if (ok)
                {
                    StatusText.Text = "Connected to Visual Studio";
                    _preferVisualStudio = true;
                    ApplyFocusMode(true);
                    UpdateVisualStudioUiState();
                }
                else
                {
                    StatusText.Text = "Open Visual Studio, then click VS";
                    UpdateVisualStudioUiState();
                }
            }
            catch
            {
                try { StatusText.Text = "Visual Studio connection failed"; } catch { }
                try { _vsDte = null; } catch { }
                UpdateVisualStudioUiState();
            }
            finally
            {
                _vsConnecting = false;
            }
        }

        private void ToggleFocusMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyFocusMode(!_focusMode);
            }
            catch
            {
            }
        }

        private void ApplyFocusMode(bool enabled)
        {
            try
            {
                _focusMode = enabled;
                FocusModeBtn.Content = enabled ? "◰" : "◱";

                if (enabled)
                {
                    IconCol.Width = new GridLength(0);
                    ExplorerCol.Width = new GridLength(0);
                    LeftSplitCol.Width = new GridLength(0);
                    EditorCol.Width = new GridLength(0);
                    RightSplitCol.Width = new GridLength(0);

                    ExplorerCol.MinWidth = 0;
                    ExplorerCol.MaxWidth = 0;
                    AIPanelCol.MinWidth = 0;
                    AIPanelCol.MaxWidth = double.PositiveInfinity;
                    AIPanelCol.Width = new GridLength(1, GridUnitType.Star);

                    LeftSplitter.Visibility = Visibility.Collapsed;
                    RightSplitter.Visibility = Visibility.Collapsed;
                    IconSidebarBorder.Visibility = Visibility.Collapsed;
                    ExplorerBorder.Visibility = Visibility.Collapsed;
                    EditorGrid.Visibility = Visibility.Collapsed;
                }
                else
                {
                    IconCol.Width = _iconColWidth;
                    ExplorerCol.Width = _explorerColWidth;
                    LeftSplitCol.Width = _leftSplitColWidth;
                    EditorCol.Width = _editorColWidth;
                    RightSplitCol.Width = _rightSplitColWidth;
                    AIPanelCol.Width = _aiPanelColWidth;

                    ExplorerCol.MinWidth = _explorerMinWidth;
                    ExplorerCol.MaxWidth = _explorerMaxWidth;
                    AIPanelCol.MinWidth = _aiMinWidth;
                    AIPanelCol.MaxWidth = _aiMaxWidth;

                    LeftSplitter.Visibility = Visibility.Visible;
                    RightSplitter.Visibility = Visibility.Visible;
                    IconSidebarBorder.Visibility = Visibility.Visible;
                    ExplorerBorder.Visibility = Visibility.Visible;
                    EditorGrid.Visibility = Visibility.Visible;
                }
            }
            catch
            {
            }
        }

        private bool EnsureVisualStudioConnected(bool startIfNeeded)
        {
            try
            {
                if (_vsDte != null) return true;
                if (!startIfNeeded) return false;
                _ = EnsureVisualStudioConnectedAsync(startIfNeeded: true);
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> EnsureVisualStudioConnectedAsync(bool startIfNeeded)
        {
            try
            {
                if (_vsDte != null) return true;

                if (TryGetActiveVisualStudioDte(out var dte, out var progId))
                {
                    _vsDte = dte;
                    _vsProgId = progId;
                    Dispatcher.Invoke(UpdateVisualStudioUiState);
                    return true;
                }

                if (!startIfNeeded) return false;

                var sln = FindSolutionPath();
                if (string.IsNullOrWhiteSpace(sln))
                {
                    if (!string.IsNullOrWhiteSpace(_projectPath))
                    {
                        Process.Start(new ProcessStartInfo { FileName = _projectPath, UseShellExecute = true });
                    }
                    return false;
                }

                Process.Start(new ProcessStartInfo { FileName = sln, UseShellExecute = true });

                var start = DateTime.UtcNow;
                while ((DateTime.UtcNow - start).TotalSeconds < 15)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    if (TryGetActiveVisualStudioDte(out dte, out progId))
                    {
                        try
                        {
                            var fullName = (string?)((dynamic)dte).Solution?.FullName;
                            if (!string.IsNullOrWhiteSpace(fullName))
                            {
                                _vsDte = dte;
                                _vsProgId = progId;
                                Dispatcher.Invoke(UpdateVisualStudioUiState);
                                return true;
                            }
                        }
                        catch
                        {
                            _vsDte = dte;
                            _vsProgId = progId;
                            Dispatcher.Invoke(UpdateVisualStudioUiState);
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetActiveVisualStudioDte(out object? dte, out string progId)
        {
            dte = null;
            progId = "";

            try
            {
                var progIds = new[]
                {
                    "VisualStudio.DTE.17.0",
                    "VisualStudio.DTE.16.0"
                };

                foreach (var id in progIds)
                {
                    try
                    {
                        if (TryGetActiveComObjectFromRot(id, out var obj) && obj != null)
                        {
                            dte = obj;
                            progId = id;
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

        private static bool TryGetActiveComObjectFromRot(string progId, out object? comObject)
        {
            comObject = null;

            try
            {
                if (GetRunningObjectTable(0, out var rot) != 0 || rot == null)
                    return false;

                rot.EnumRunning(out var enumMoniker);
                if (enumMoniker == null) return false;

                enumMoniker.Reset();

                var monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    if (moniker == null) continue;

                    try
                    {
                        CreateBindCtx(0, out var bindCtx);
                        moniker.GetDisplayName(bindCtx, null, out var displayName);

                        if (string.IsNullOrWhiteSpace(displayName))
                            continue;

                        if (!displayName.StartsWith("!" + progId, StringComparison.OrdinalIgnoreCase))
                            continue;

                        rot.GetObject(moniker, out var obj);
                        if (obj != null)
                        {
                            comObject = obj;
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private string FindSolutionPath()
        {
            try
            {
                var root = _projectPath;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return "";

                var slnx = Directory.GetFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(slnx)) return slnx;

                var sln = Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(sln)) return sln;

                var atlasCsproj = Path.Combine(root, "AtlasAI.csproj");
                if (File.Exists(atlasCsproj)) return atlasCsproj;

                var anyCsproj = Directory.GetFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(anyCsproj)) return anyCsproj;
            }
            catch
            {
            }

            return "";
        }
        
        // Context manifest viewer
        private void ViewContext_Click(object sender, RoutedEventArgs e)
        {
            var manifest = Services.AgentContextBudgeter.Instance.GetManifest();
            var dialog = new Window
            {
                Title = "Context Manifest",
                Width = 400,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0a0a0f")),
                Owner = this
            };
            
            var content = new StackPanel { Margin = new Thickness(16) };
            content.Children.Add(new TextBlock
            {
                Text = "CONTEXT MANIFEST",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00d4ff")),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16)
            });
            content.Children.Add(new TextBlock
            {
                Text = $"Total Characters: {manifest.TotalChars:N0}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e8")),
                FontSize = 12
            });
            content.Children.Add(new TextBlock
            {
                Text = $"Files: {manifest.IncludedFiles.Count}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e8")),
                FontSize = 12
            });
            content.Children.Add(new TextBlock
            {
                Text = $"Snippets: {manifest.IncludedSnippets.Count}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e8")),
                FontSize = 12
            });
            
            dialog.Content = new ScrollViewer { Content = content };
            dialog.ShowDialog();
        }

        #endregion

        #region File Explorer

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Open Project Folder"
            };
            
            if (dialog.ShowDialog() == true)
            {
                OpenProject(dialog.FolderName);
            }
        }

        public void OpenProject(string path)
        {
            if (!Directory.Exists(path)) return;
            
            _projectPath = path;
            ProjectName.Text = $"— {Path.GetFileName(path)}";
            WindowTitle.Text = $"Atlas IDE";
            
            RefreshFileTree();
            RefreshProjectTasks();
            _logger.Log("ProjectOpened", new { path });
            StatusText.Text = $"Opened: {path}";

            _ = EnsureProjectIndexAsync();
        }

        private sealed class ProjectTask
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Command { get; set; } = "";
            public string Meta { get; set; } = "";
        }

        private void EnsureProjectTasks()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_projectPath)) return;
                if (string.Equals(_projectTasksPath, _projectPath, StringComparison.OrdinalIgnoreCase) && _projectTasks.Count > 0)
                    return;
                RefreshProjectTasks();
            }
            catch
            {
            }
        }

        private void RefreshProjectTasks()
        {
            try
            {
                _projectTasksPath = _projectPath ?? "";
                _projectTasks = DetectProjectTasks(_projectPath);
            }
            catch
            {
                _projectTasks = new List<ProjectTask>();
            }
        }

        private static List<ProjectTask> DetectProjectTasks(string? projectPath)
        {
            var list = new List<ProjectTask>();
            try
            {
                if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath)) return list;

                var sln = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                var csproj = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                var pkg = Path.Combine(projectPath, "package.json");

                if (!string.IsNullOrWhiteSpace(sln) || !string.IsNullOrWhiteSpace(csproj))
                {
                    list.Add(new ProjectTask { Id = "dotnet_run", Title = "Run (.NET)", Command = "dotnet run", Meta = "dotnet" });
                    list.Add(new ProjectTask { Id = "dotnet_build", Title = "Build (.NET)", Command = "dotnet build", Meta = "dotnet" });
                    list.Add(new ProjectTask { Id = "dotnet_build_debug", Title = "Build Debug (.NET)", Command = "dotnet build --configuration Debug", Meta = "dotnet" });
                    list.Add(new ProjectTask { Id = "dotnet_test", Title = "Test (.NET)", Command = "dotnet test", Meta = "dotnet" });
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
                                var name = prop.Name?.Trim() ?? "";
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                list.Add(new ProjectTask
                                {
                                    Id = "npm_" + name.ToLowerInvariant(),
                                    Title = $"npm: {name}",
                                    Command = $"npm run {name}",
                                    Meta = "npm"
                                });
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (list.All(t => t.Id != "npm_install"))
                        list.Add(new ProjectTask { Id = "npm_install", Title = "npm: install", Command = "npm install", Meta = "npm" });
                }
            }
            catch
            {
            }

            return list
                .GroupBy(t => t.Id)
                .Select(g => g.First())
                .OrderBy(t => t.Meta)
                .ThenBy(t => t.Title)
                .ToList();
        }

        private ProjectTask? GetDefaultRunTask()
        {
            try
            {
                var npmDev = _projectTasks.FirstOrDefault(t => t.Id == "npm_dev") ?? _projectTasks.FirstOrDefault(t => t.Id == "npm_start");
                if (npmDev != null) return npmDev;

                var dotnet = _projectTasks.FirstOrDefault(t => t.Id == "dotnet_run");
                if (dotnet != null) return dotnet;

                return _projectTasks.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private ProjectTask? GetDefaultBuildTask()
        {
            try
            {
                var npmBuild = _projectTasks.FirstOrDefault(t => t.Id == "npm_build");
                if (npmBuild != null) return npmBuild;

                var dotnet = _projectTasks.FirstOrDefault(t => t.Id == "dotnet_build");
                if (dotnet != null) return dotnet;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void ShowRunTasksMenu()
        {
            try
            {
                EnsureProjectTasks();

                if (_projectTasks.Count == 0)
                {
                    StatusText.Text = "No tasks detected";
                    return;
                }

                var menu = new ContextMenu();
                foreach (var task in _projectTasks.Take(25))
                {
                    var mi = new MenuItem { Header = task.Title, Tag = task };
                    mi.Click += (_, __) =>
                    {
                        try
                        {
                            TerminalTab_Click(this, new RoutedEventArgs());
                            StatusText.Text = task.Title;
                            RunTerminalCommand(task.Command);
                        }
                        catch
                        {
                        }
                    };
                    menu.Items.Add(mi);
                }

                RunBtn.ContextMenu = menu;
                menu.PlacementTarget = RunBtn;
                menu.IsOpen = true;
            }
            catch
            {
            }
        }

        private async Task EnsureProjectIndexAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_projectPath)) return;
                if (_indexReady || _indexBuilding) return;
                _indexBuilding = true;
                Dispatcher.Invoke(() => StatusText.Text = "Indexing project…");

                var index = ProjectIndexService.Instance;
                var loaded = false;
                try { loaded = await index.LoadIndexAsync().ConfigureAwait(false); } catch { loaded = false; }
                if (!loaded)
                    await index.BuildIndexAsync(_projectPath).ConfigureAwait(false);
                try { await index.SaveIndexAsync().ConfigureAwait(false); } catch { }
                _indexReady = true;

                Dispatcher.Invoke(() => StatusText.Text = $"Indexed: {index.GetStats().FileCount} files");
            }
            catch
            {
            }
            finally
            {
                _indexBuilding = false;
            }
        }

        private void IDEWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    CommandPalette.Focus();
                    CommandPalette.SelectAll();
                    e.Handled = true;
                }
                else if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    AIInput.Focus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Oem2 && Keyboard.Modifiers == ModifierKeys.None) // '/'
                {
                    AIInput.Focus();
                    e.Handled = true;
                }
            }
            catch
            {
            }
        }

        private void CommandPalette_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _commandPaletteCts?.Cancel();
                _commandPaletteCts?.Dispose();
                _commandPaletteCts = new CancellationTokenSource();
                var token = _commandPaletteCts.Token;

                var text = (CommandPalette.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    CommandPalettePopup.IsOpen = false;
                    _commandPaletteEntries.Clear();
                    _commandPaletteIndex = -1;
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(120, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested) return;

                        var entries = await BuildCommandPaletteEntriesAsync(text).ConfigureAwait(false);
                        if (token.IsCancellationRequested) return;

                        Dispatcher.Invoke(() =>
                        {
                            _commandPaletteEntries.Clear();
                            foreach (var it in entries)
                                _commandPaletteEntries.Add(it);

                            _commandPaletteIndex = _commandPaletteEntries.Count > 0 ? 0 : -1;
                            CommandPaletteResults.SelectedIndex = _commandPaletteIndex;
                            CommandPalettePopup.IsOpen = _commandPaletteEntries.Count > 0;
                        });
                    }
                    catch
                    {
                    }
                }, token);
            }
            catch
            {
            }
        }

        private async Task<List<CommandPaletteEntry>> BuildCommandPaletteEntriesAsync(string query)
        {
            var list = new List<CommandPaletteEntry>();

            try
            {
                var q = (query ?? "").Trim();
                if (string.IsNullOrWhiteSpace(q)) return list;

                var qLower = q.ToLowerInvariant();
                var actions = new List<CommandPaletteEntry>
                {
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Connect Visual Studio", Meta = "vs", Payload = "connect_vs" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Manage Visual Studio Extensions", Meta = "vs", Payload = "vs_extensions" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Install VSIX extension", Meta = "vs", Payload = "install_vsix" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Download Visual Studio", Meta = "vs", Payload = "download_vs" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Download VS Code", Meta = "dev", Payload = "download_vscode" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Scaffold: WPF app", Meta = "scaffold", Payload = "scaffold_wpf" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Scaffold: Web API", Meta = "scaffold", Payload = "scaffold_webapi" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Scaffold: Console app", Meta = "scaffold", Payload = "scaffold_console" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Role: Builder", Meta = "role", Payload = "role_builder" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Role: Designer", Meta = "role", Payload = "role_designer" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Role: Debugger", Meta = "role", Payload = "role_debugger" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Role: Architect", Meta = "role", Payload = "role_architect" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Open project in Explorer", Meta = "system", Payload = "open_explorer" },
                    new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Command, Display = "Toggle focus mode", Meta = "ui", Payload = "toggle_focus" }
                };

                foreach (var a in actions)
                {
                    if (a.Display.ToLowerInvariant().Contains(qLower) || a.Meta.ToLowerInvariant().Contains(qLower) || qLower == "vs" || qLower == "focus")
                        list.Add(a);
                }

                try
                {
                    EnsureProjectTasks();
                    foreach (var t in _projectTasks)
                    {
                        var titleLower = t.Title.ToLowerInvariant();
                        if (qLower.StartsWith("task ", StringComparison.Ordinal) || qLower.StartsWith("run ", StringComparison.Ordinal))
                        {
                            list.Add(new CommandPaletteEntry
                            {
                                Kind = CommandPaletteEntryKind.Terminal,
                                Display = $"Task: {t.Title}",
                                Meta = t.Meta,
                                Payload = t.Command
                            });
                            continue;
                        }

                        if (titleLower.Contains(qLower) || t.Meta.ToLowerInvariant().Contains(qLower))
                        {
                            list.Add(new CommandPaletteEntry
                            {
                                Kind = CommandPaletteEntryKind.Terminal,
                                Display = $"Task: {t.Title}",
                                Meta = t.Meta,
                                Payload = t.Command
                            });
                        }
                    }
                }
                catch
                {
                }

                if (qLower.StartsWith("ext ", StringComparison.Ordinal) || qLower.StartsWith("extensions ", StringComparison.Ordinal))
                {
                    var extQuery = qLower.StartsWith("ext ", StringComparison.Ordinal) ? q.Substring(4).Trim() : q.Substring("extensions ".Length).Trim();
                    if (!string.IsNullOrWhiteSpace(extQuery))
                    {
                        var url = "https://marketplace.visualstudio.com/search?target=VS&term=" + Uri.EscapeDataString(extQuery);
                        list.Add(new CommandPaletteEntry { Kind = CommandPaletteEntryKind.Url, Display = $"Search VS extensions: {extQuery}", Meta = "web", Payload = url });
                        return list;
                    }
                }

                if (q.StartsWith(">", StringComparison.Ordinal))
                {
                    var cmd = q.Substring(1).Trim();
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        list.Add(new CommandPaletteEntry
                        {
                            Kind = CommandPaletteEntryKind.Terminal,
                            Display = $"> {cmd}",
                            Meta = "terminal",
                            Payload = cmd
                        });
                    }
                    return list;
                }

                if (_projectPath != null && Directory.Exists(_projectPath))
                {
                    var maybePath = q.Replace('/', '\\').TrimStart('\\');
                    var candidate = Path.Combine(_projectPath, maybePath);
                    if (File.Exists(candidate))
                    {
                        list.Add(new CommandPaletteEntry
                        {
                            Kind = CommandPaletteEntryKind.File,
                            Display = maybePath.Replace('\\', '/'),
                            Meta = "file",
                            Payload = candidate
                        });
                        return list;
                    }
                }

                if (!_indexReady && !_indexBuilding)
                    _ = EnsureProjectIndexAsync();

                if (_indexReady)
                {
                    var results = await ProjectIndexService.Instance.SearchAsync(q, 8).ConfigureAwait(false);
                    foreach (var r in results)
                    {
                        list.Add(new CommandPaletteEntry
                        {
                            Kind = CommandPaletteEntryKind.SearchResult,
                            Display = r.RelativePath,
                            Meta = r.Reason,
                            Payload = r.FilePath,
                            Line = r.Line
                        });
                    }
                }
                else
                {
                    list.Add(new CommandPaletteEntry
                    {
                        Kind = CommandPaletteEntryKind.Info,
                        Display = "Indexing…",
                        Meta = "search",
                        Payload = ""
                    });
                }
            }
            catch
            {
            }

            return list;
        }

        private void CommandPalette_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Down)
                {
                    if (_commandPaletteEntries.Count == 0) return;
                    _commandPaletteIndex = Math.Min(_commandPaletteEntries.Count - 1, _commandPaletteIndex + 1);
                    CommandPaletteResults.SelectedIndex = _commandPaletteIndex;
                    CommandPaletteResults.ScrollIntoView(CommandPaletteResults.SelectedItem);
                    CommandPalettePopup.IsOpen = true;
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Up)
                {
                    if (_commandPaletteEntries.Count == 0) return;
                    _commandPaletteIndex = Math.Max(0, _commandPaletteIndex - 1);
                    CommandPaletteResults.SelectedIndex = _commandPaletteIndex;
                    CommandPaletteResults.ScrollIntoView(CommandPaletteResults.SelectedItem);
                    CommandPalettePopup.IsOpen = true;
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    var entry = CommandPaletteResults.SelectedItem as CommandPaletteEntry;
                    if (entry == null && _commandPaletteEntries.Count > 0)
                        entry = _commandPaletteEntries[0];

                    if (entry != null)
                        ExecuteCommandPaletteEntry(entry);

                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Escape)
                {
                    CommandPalettePopup.IsOpen = false;
                    _commandPaletteEntries.Clear();
                    _commandPaletteIndex = -1;
                    e.Handled = true;
                }
            }
            catch
            {
            }
        }

        private void CommandPalette_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (CommandPalettePopup.IsKeyboardFocusWithin) return;
                CommandPalettePopup.IsOpen = false;
            }
            catch
            {
            }
        }

        private void CommandPaletteResults_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                _commandPaletteIndex = CommandPaletteResults.SelectedIndex;
            }
            catch
            {
            }
        }

        private void CommandPaletteResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (CommandPaletteResults.SelectedItem is CommandPaletteEntry entry)
                    ExecuteCommandPaletteEntry(entry);
            }
            catch
            {
            }
        }

        private void ExecuteCommandPaletteEntry(CommandPaletteEntry entry)
        {
            try
            {
                CommandPalettePopup.IsOpen = false;
                CommandPalette.Text = "";

                if (entry.Kind == CommandPaletteEntryKind.Command)
                {
                    ExecuteCommandPaletteCommand(entry.Payload ?? "");
                    return;
                }

                if (entry.Kind == CommandPaletteEntryKind.Url)
                {
                    var url = entry.Payload ?? "";
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                        StatusText.Text = "Opened in browser";
                    }
                    return;
                }

                if (entry.Kind == CommandPaletteEntryKind.Terminal)
                {
                    TerminalTab_Click(this, new RoutedEventArgs());
                    RunTerminalCommand(entry.Payload ?? "");
                    return;
                }

                if (entry.Kind == CommandPaletteEntryKind.File || entry.Kind == CommandPaletteEntryKind.SearchResult)
                {
                    var path = entry.Payload ?? "";
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        OpenFilePreferred(path, entry.Line);
                    }
                }
            }
            catch
            {
            }
        }

        private void ExecuteCommandPaletteCommand(string command)
        {
            try
            {
                var c = (command ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(c)) return;

                if (c == "toggle_focus")
                {
                    ApplyFocusMode(!_focusMode);
                    StatusText.Text = _focusMode ? "Focus mode" : "Normal mode";
                    return;
                }

                if (c == "connect_vs")
                {
                    ConnectVisualStudio_Click(this, new RoutedEventArgs());
                    return;
                }

                if (c == "vs_extensions")
                {
                    ExtIcon_Click(this, new RoutedEventArgs());
                    return;
                }

                if (c == "install_vsix")
                {
                    InstallVsix();
                    return;
                }

                if (c == "open_explorer")
                {
                    var path = _projectPath ?? Environment.CurrentDirectory;
                    OpenInExplorer(path);
                    StatusText.Text = "Explorer";
                    return;
                }

                if (c == "download_vs")
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://aka.ms/vs/17/release/vs_Community.exe",
                        UseShellExecute = true
                    });
                    StatusText.Text = "Visual Studio download started";
                    return;
                }

                if (c == "download_vscode")
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://code.visualstudio.com/sha/download?build=stable&os=win32-x64-user",
                        UseShellExecute = true
                    });
                    StatusText.Text = "VS Code download started";
                    return;
                }

                if (c == "role_builder") { SetAgentRole("Builder"); return; }
                if (c == "role_designer") { SetAgentRole("Designer"); return; }
                if (c == "role_debugger") { SetAgentRole("Debugger"); return; }
                if (c == "role_architect") { SetAgentRole("Architect"); return; }

                if (c == "scaffold_wpf") { ScaffoldDotnetProject("wpf", "WPF app"); return; }
                if (c == "scaffold_webapi") { ScaffoldDotnetProject("webapi", "Web API"); return; }
                if (c == "scaffold_console") { ScaffoldDotnetProject("console", "Console app"); return; }
            }
            catch
            {
            }
        }

        private void SetAgentRole(string role)
        {
            try
            {
                _agentRole = role;
                StatusText.Text = $"Role: {_agentRole}";

                if (AgentRoleCombo != null)
                {
                    foreach (var item in AgentRoleCombo.Items)
                    {
                        if (item is ComboBoxItem it && it.Content is string s && string.Equals(s, role, StringComparison.OrdinalIgnoreCase))
                        {
                            AgentRoleCombo.SelectedItem = it;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void ScaffoldDotnetProject(string template, string displayName)
        {
            try
            {
                var folderDialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = $"Choose where to create the {displayName}"
                };

                if (folderDialog.ShowDialog() != true) return;
                var parent = folderDialog.FolderName;
                if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return;

                var name = PromptForText($"Create {displayName}", "Project name", "MyApp");
                if (string.IsNullOrWhiteSpace(name)) return;

                name = SanitizeProjectName(name);
                if (string.IsNullOrWhiteSpace(name)) return;

                TerminalTab_Click(this, new RoutedEventArgs());
                RunTerminalCommand($"cd /d \"{parent}\" && dotnet new {template} -n \"{name}\" -o \"{name}\"");

                var newPath = Path.Combine(parent, name);
                if (Directory.Exists(newPath))
                {
                    OpenProject(newPath);
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            for (var i = 0; i < 20; i++)
                            {
                                await Task.Delay(250).ConfigureAwait(false);
                                if (Directory.Exists(newPath))
                                {
                                    Dispatcher.Invoke(() => OpenProject(newPath));
                                    break;
                                }
                            }
                        }
                        catch
                        {
                        }
                    });
                }

                StatusText.Text = $"Scaffolding {displayName}: {name}";
            }
            catch
            {
            }
        }

        private static string SanitizeProjectName(string input)
        {
            try
            {
                var trimmed = (input ?? "").Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) return "";

                var sb = new StringBuilder(trimmed.Length);
                foreach (var ch in trimmed)
                {
                    if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                        sb.Append(ch);
                }
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        private string PromptForText(string title, string label, string defaultValue)
        {
            try
            {
                var owner = _embeddedHostWindow ?? this;
                var win = new Window
                {
                    Title = title,
                    Width = 420,
                    Height = 170,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = owner,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0a0a0f")),
                    ResizeMode = ResizeMode.NoResize
                };

                var root = new Grid { Margin = new Thickness(16) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var text = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e8")),
                    FontSize = 12
                };
                root.Children.Add(text);

                var box = new TextBox
                {
                    Text = defaultValue ?? "",
                    Margin = new Thickness(0, 8, 0, 0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080810")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e8")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3300d4ff")),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 6, 10, 6)
                };
                Grid.SetRow(box, 1);
                root.Children.Add(box);

                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
                var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
                var ok = new Button { Content = "OK", Padding = new Thickness(14, 6, 14, 6) };
                btnRow.Children.Add(cancel);
                btnRow.Children.Add(ok);
                Grid.SetRow(btnRow, 2);
                root.Children.Add(btnRow);

                string result = "";

                cancel.Click += (_, __) =>
                {
                    win.DialogResult = false;
                    win.Close();
                };
                ok.Click += (_, __) =>
                {
                    result = box.Text.Trim();
                    win.DialogResult = true;
                    win.Close();
                };
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        result = box.Text.Trim();
                        win.DialogResult = true;
                        win.Close();
                    }
                };

                win.Content = root;
                _ = win.ShowDialog();
                return result;
            }
            catch
            {
                return "";
            }
        }

        private void InstallVsix()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select VSIX extension",
                    Filter = "VSIX (*.vsix)|*.vsix|All files (*.*)|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true) return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true
                });

                StatusText.Text = "VSIX installer opened";
            }
            catch
            {
            }
        }

        private void OpenFilePreferred(string path, int line = 0)
        {
            try
            {
                if (_preferVisualStudio && _vsDte != null)
                {
                    if (TryOpenFileInVisualStudio(path, line))
                        return;
                }

                OpenFile(path);
                if (line > 1)
                {
                    var script = $"revealLine({line});";
                    EditorWebView.CoreWebView2?.ExecuteScriptAsync(script);
                }
            }
            catch
            {
            }
        }

        private bool TryOpenFileInVisualStudio(string path, int line = 0)
        {
            try
            {
                if (_vsDte == null) return false;

                dynamic dte = _vsDte;
                try { dte.MainWindow.Activate(); } catch { }

                try
                {
                    dte.ItemOperations.OpenFile(path);
                }
                catch
                {
                    return false;
                }

                if (line > 1)
                {
                    try
                    {
                        dynamic doc = dte.ActiveDocument;
                        dynamic sel = doc.Selection;
                        sel.GotoLine(line, true);
                    }
                    catch
                    {
                    }
                }

                _activeFilePath = path;
                StatusText.Text = Path.GetFileName(path);
                UpdateContextIndicator();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private enum CommandPaletteEntryKind
        {
            File,
            SearchResult,
            Terminal,
            Command,
            Url,
            Info
        }

        private sealed class CommandPaletteEntry
        {
            public CommandPaletteEntryKind Kind { get; set; }
            public string Display { get; set; } = "";
            public string Meta { get; set; } = "";
            public string? Payload { get; set; }
            public int Line { get; set; }
        }

        private void RefreshTree_Click(object sender, RoutedEventArgs e) => RefreshFileTree();

        private void RefreshFileTree()
        {
            if (string.IsNullOrEmpty(_projectPath)) return;
            
            FileTree.Items.Clear();
            var rootItem = CreateTreeItem(_projectPath, true);
            rootItem.IsExpanded = true;
            FileTree.Items.Add(rootItem);
        }

        private TreeViewItem CreateTreeItem(string path, bool isRoot = false)
        {
            var isDirectory = Directory.Exists(path);
            var name = isRoot ? Path.GetFileName(path) : Path.GetFileName(path);
            var icon = isDirectory ? "📁" : GetFileIcon(path);
            
            var item = new TreeViewItem
            {
                Header = $"{icon} {name}",
                Tag = path,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e2e8f0"))
            };
            
            if (isDirectory)
            {
                try
                {
                    // Add subdirectories
                    foreach (var dir in Directory.GetDirectories(path).OrderBy(d => Path.GetFileName(d)))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (dirName.StartsWith(".") || dirName == "node_modules" || dirName == "bin" || dirName == "obj")
                            continue;
                        item.Items.Add(CreateTreeItem(dir));
                    }
                    
                    // Add files
                    foreach (var file in Directory.GetFiles(path).OrderBy(f => Path.GetFileName(f)))
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName.StartsWith(".")) continue;
                        item.Items.Add(CreateTreeItem(file));
                    }
                }
                catch { }
            }
            
            return item;
        }

        private string GetFileIcon(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".cs" => "🟣",
                ".ts" or ".tsx" => "🔵",
                ".js" or ".jsx" => "🟡",
                ".py" => "🐍",
                ".json" => "📋",
                ".xml" or ".xaml" => "📄",
                ".html" or ".htm" => "🌐",
                ".css" or ".scss" or ".sass" => "🎨",
                ".md" => "📝",
                ".txt" => "📃",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "🖼️",
                ".sln" => "🔷",
                ".csproj" => "⚙️",
                _ => "📄"
            };
        }

        private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string path)
            {
                if (File.Exists(path))
                {
                    OpenFilePreferred(path);
                }
            }
        }

        #endregion

        #region File Management

        public void OpenFile(string path)
        {
            if (!File.Exists(path)) return;
            
            // Check if already open
            if (_openFiles.ContainsKey(path))
            {
                ActivateFile(path);
                return;
            }
            
            try
            {
                var content = File.ReadAllText(path);
                var language = GetLanguageFromPath(path);
                
                var openFile = new OpenFile
                {
                    Path = path,
                    Content = content,
                    OriginalContent = content,
                    Language = language,
                    IsDirty = false
                };
                
                _openFiles[path] = openFile;
                AddTab(path);
                ActivateFile(path);
                
                _logger.Log("FileOpened", new { path, language, size = content.Length });
                FileOpened?.Invoke(this, new FileOpenedEventArgs { Path = path, Language = language });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IDE] Error opening file: {ex.Message}");
                StatusText.Text = $"Error opening file: {ex.Message}";
            }
        }

        public void OpenFileAtLine(string path, int line = 0)
        {
            try
            {
                OpenFile(path);
                if (line > 1)
                {
                    var script = $"revealLine({line});";
                    EditorWebView.CoreWebView2?.ExecuteScriptAsync(script);
                }
            }
            catch
            {
            }
        }

        private void ActivateFile(string path)
        {
            if (!_openFiles.TryGetValue(path, out var file)) return;
            
            _activeFilePath = path;
            
            // Update Monaco editor
            var escapedContent = JsonSerializer.Serialize(file.Content);
            var script = $"setEditorContent({escapedContent}, '{file.Language}');";
            EditorWebView.CoreWebView2?.ExecuteScriptAsync(script);
            
            // Update UI
            UpdateTabSelection();
            FileLanguage.Text = file.Language.ToUpperInvariant();
            StatusText.Text = Path.GetFileName(path);
            
            // Update context indicator
            UpdateContextIndicator();
        }

        private void AddTab(string path)
        {
            var fileName = Path.GetFileName(path);
            
            var tab = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b")),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Padding = new Thickness(12, 6, 8, 6),
                Margin = new Thickness(0, 4, 2, 0),
                Tag = path,
                Cursor = Cursors.Hand
            };
            
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            
            var nameBlock = new TextBlock
            {
                Text = fileName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e2e8f0")),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var dirtyIndicator = new TextBlock
            {
                Text = " •",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22d3ee")),
                FontSize = 14,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "dirty"
            };
            
            var closeBtn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b")),
                BorderThickness = new Thickness(0),
                FontSize = 10,
                Padding = new Thickness(4, 0, 0, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                Tag = path
            };
            closeBtn.Click += CloseTab_Click;
            
            stack.Children.Add(nameBlock);
            stack.Children.Add(dirtyIndicator);
            stack.Children.Add(closeBtn);
            tab.Child = stack;
            
            tab.MouseLeftButtonDown += (s, e) => ActivateFile(path);
            
            TabBar.Children.Add(tab);
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                CloseFile(path);
            }
        }

        private void CloseFile(string path)
        {
            if (!_openFiles.TryGetValue(path, out var file)) return;
            
            if (file.IsDirty)
            {
                var result = MessageBox.Show(
                    $"Save changes to {Path.GetFileName(path)}?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel);
                
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes) SaveFile(path);
            }
            
            _openFiles.Remove(path);
            
            // Remove tab
            var tab = TabBar.Children.OfType<Border>().FirstOrDefault(t => t.Tag as string == path);
            if (tab != null) TabBar.Children.Remove(tab);
            
            // Activate another file if this was active
            if (_activeFilePath == path)
            {
                _activeFilePath = _openFiles.Keys.FirstOrDefault();
                if (_activeFilePath != null)
                    ActivateFile(_activeFilePath);
                else
                    ClearEditor();
            }
        }

        public void SaveFile(string path)
        {
            if (!_openFiles.TryGetValue(path, out var file)) return;
            
            try
            {
                File.WriteAllText(path, file.Content);
                file.OriginalContent = file.Content;
                file.IsDirty = false;
                UpdateTabDirtyState(path);
                
                _logger.Log("FileSaved", new { path });
                FileSaved?.Invoke(this, new FileSavedEventArgs { Path = path });
                StatusText.Text = $"Saved: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IDE] Error saving file: {ex.Message}");
                StatusText.Text = $"Error saving: {ex.Message}";
            }
        }

        private void UpdateTabSelection()
        {
            foreach (var child in TabBar.Children.OfType<Border>())
            {
                var isActive = child.Tag as string == _activeFilePath;
                child.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    isActive ? "#1e1e1e" : "#1e293b"));
            }
        }

        private void UpdateTabDirtyState(string path)
        {
            var tab = TabBar.Children.OfType<Border>().FirstOrDefault(t => t.Tag as string == path);
            if (tab?.Child is StackPanel stack)
            {
                var dirty = stack.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "dirty");
                if (dirty != null && _openFiles.TryGetValue(path, out var file))
                {
                    dirty.Visibility = file.IsDirty ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void ClearEditor()
        {
            EditorWebView.CoreWebView2?.ExecuteScriptAsync("setEditorContent('', 'plaintext');");
            FileLanguage.Text = "";
            CursorPosition.Text = "Ln 1, Col 1";
        }

        private string GetLanguageFromPath(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".cs" => "csharp",
                ".ts" => "typescript",
                ".tsx" => "typescriptreact",
                ".js" => "javascript",
                ".jsx" => "javascriptreact",
                ".py" => "python",
                ".json" => "json",
                ".xml" => "xml",
                ".xaml" => "xml",
                ".html" or ".htm" => "html",
                ".css" => "css",
                ".scss" => "scss",
                ".md" => "markdown",
                ".yaml" or ".yml" => "yaml",
                ".sql" => "sql",
                ".sh" or ".bash" => "shell",
                ".ps1" => "powershell",
                _ => "plaintext"
            };
        }

        #endregion

        #region Monaco Editor Integration

        private void EditorWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = JsonSerializer.Deserialize<EditorMessage>(e.WebMessageAsJson);
                if (message == null) return;
                
                switch (message.Type)
                {
                    case "contentChanged":
                        OnEditorContentChanged(message.Content ?? "");
                        break;
                    case "selectionChanged":
                        OnEditorSelectionChanged(message);
                        break;
                    case "cursorChanged":
                        OnEditorCursorChanged(message.Line, message.Column);
                        break;
                    case "save":
                        if (_activeFilePath != null) SaveFile(_activeFilePath);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IDE] Editor message error: {ex.Message}");
            }
        }

        private void OnEditorContentChanged(string content)
        {
            if (_activeFilePath == null || !_openFiles.TryGetValue(_activeFilePath, out var file)) return;
            
            file.Content = content;
            file.IsDirty = content != file.OriginalContent;
            UpdateTabDirtyState(_activeFilePath);
        }

        private void OnEditorSelectionChanged(EditorMessage message)
        {
            _currentSelection = message.SelectedText;
            _selectionStartLine = message.StartLine;
            _selectionEndLine = message.EndLine;
            
            UpdateContextIndicator();
            
            _logger.Log("SelectionChanged", new { 
                file = _activeFilePath, 
                startLine = _selectionStartLine, 
                endLine = _selectionEndLine,
                length = _currentSelection?.Length ?? 0
            });
            
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs
            {
                FilePath = _activeFilePath,
                SelectedText = _currentSelection,
                StartLine = _selectionStartLine,
                EndLine = _selectionEndLine
            });
        }

        private void OnEditorCursorChanged(int line, int column)
        {
            CursorPosition.Text = $"Ln {line}, Col {column}";
        }

        private string GetMonacoEditorHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body, html { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; background: #1e1e1e; }
        #editor { width: 100%; height: 100%; }
    </style>
</head>
<body>
    <div id=""editor""></div>
    <script src=""https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.45.0/min/vs/loader.min.js""></script>
    <script>
        let editor;
        
        require.config({ paths: { vs: 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.45.0/min/vs' } });
        
        require(['vs/editor/editor.main'], function() {
            monaco.editor.defineTheme('atlas-dark', {
                base: 'vs-dark',
                inherit: true,
                rules: [],
                colors: {
                    'editor.background': '#1e1e1e',
                    'editor.foreground': '#e2e8f0',
                    'editor.lineHighlightBackground': '#2d2d2d',
                    'editorCursor.foreground': '#22d3ee',
                    'editor.selectionBackground': '#22d3ee40',
                    'editorLineNumber.foreground': '#64748b'
                }
            });
            
            editor = monaco.editor.create(document.getElementById('editor'), {
                value: '',
                language: 'plaintext',
                theme: 'atlas-dark',
                fontSize: 14,
                fontFamily: 'Consolas, Monaco, monospace',
                lineNumbers: 'on',
                minimap: { enabled: true },
                scrollBeyondLastLine: false,
                automaticLayout: true,
                wordWrap: 'on',
                tabSize: 4,
                insertSpaces: true
            });
            
            editor.onDidChangeModelContent(() => {
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'contentChanged',
                    content: editor.getValue()
                }));
            });
            
            editor.onDidChangeCursorSelection(() => {
                const selection = editor.getSelection();
                const selectedText = editor.getModel().getValueInRange(selection);
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'selectionChanged',
                    selectedText: selectedText,
                    startLine: selection.startLineNumber,
                    endLine: selection.endLineNumber,
                    startColumn: selection.startColumn,
                    endColumn: selection.endColumn
                }));
            });
            
            editor.onDidChangeCursorPosition((e) => {
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'cursorChanged',
                    line: e.position.lineNumber,
                    column: e.position.column
                }));
            });
            
            editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'save' }));
            });
        });
        
        function setEditorContent(content, language) {
            if (editor) {
                const model = monaco.editor.createModel(content, language);
                editor.setModel(model);
            }
        }
        
        function getEditorContent() {
            return editor ? editor.getValue() : '';
        }
        
        function getSelection() {
            if (!editor) return null;
            const selection = editor.getSelection();
            return {
                text: editor.getModel().getValueInRange(selection),
                startLine: selection.startLineNumber,
                endLine: selection.endLineNumber
            };
        }
        
        function applyEdit(startLine, endLine, newText) {
            if (!editor) return;
            const range = new monaco.Range(startLine, 1, endLine, editor.getModel().getLineMaxColumn(endLine));
            editor.executeEdits('atlas-ai', [{ range: range, text: newText }]);
        }

        function revealLine(line) {
            if (!editor) return;
            const l = Math.max(1, line || 1);
            editor.setPosition({ lineNumber: l, column: 1 });
            editor.revealLineInCenter(l);
            editor.focus();
        }
    </script>
</body>
</html>";
        }

        #endregion

        #region Terminal

        private void TerminalTab_Click(object sender, RoutedEventArgs e)
        {
            TerminalPanel.Visibility = Visibility.Visible;
            ProblemsPanel.Visibility = Visibility.Collapsed;
            OutputPanel.Visibility = Visibility.Collapsed;
            UpdatePanelTabs("terminal");
        }

        private void ProblemsTab_Click(object sender, RoutedEventArgs e)
        {
            TerminalPanel.Visibility = Visibility.Collapsed;
            ProblemsPanel.Visibility = Visibility.Visible;
            OutputPanel.Visibility = Visibility.Collapsed;
            UpdatePanelTabs("problems");
        }

        private void OutputTab_Click(object sender, RoutedEventArgs e)
        {
            TerminalPanel.Visibility = Visibility.Collapsed;
            ProblemsPanel.Visibility = Visibility.Collapsed;
            OutputPanel.Visibility = Visibility.Visible;
            UpdatePanelTabs("output");
        }

        private void UpdatePanelTabs(string active)
        {
            TerminalTabBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                active == "terminal" ? "#22d3ee" : "#64748b"));
            ProblemsTabBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                active == "problems" ? "#22d3ee" : "#64748b"));
            OutputTabBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                active == "output" ? "#22d3ee" : "#64748b"));
        }

        private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var command = TerminalInput.Text.Trim();
                if (!string.IsNullOrEmpty(command))
                {
                    RunTerminalCommand(command);
                    TerminalInput.Clear();
                }
                e.Handled = true;
            }
        }

        public async void RunTerminalCommand(string command)
        {
            AppendTerminalOutput($"❯ {command}\n", "#22d3ee");
            _logger.Log("TerminalCommand", new { command });
            
            CancellationToken token = default;
            try
            {
                _terminalCts?.Cancel();
                _terminalCts?.Dispose();
                _terminalCts = new CancellationTokenSource();
                token = _terminalCts.Token;
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    WorkingDirectory = _projectPath ?? Environment.CurrentDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                
                _terminalProcess = new Process { StartInfo = startInfo };
                _terminalProcess.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        Dispatcher.Invoke(() => AppendTerminalOutput(e.Data + "\n"));
                };
                _terminalProcess.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        Dispatcher.Invoke(() => AppendTerminalOutput(e.Data + "\n", "#ef4444"));
                };
                
                _terminalProcess.Start();
                _terminalProcess.BeginOutputReadLine();
                _terminalProcess.BeginErrorReadLine();
                
                await Task.Run(() => _terminalProcess.WaitForExit(), token);
                
                var exitCode = _terminalProcess.ExitCode;
                AppendTerminalOutput($"\n[Process exited with code {exitCode}]\n", exitCode == 0 ? "#22c55e" : "#ef4444");
                
                _logger.Log("TerminalComplete", new { command, exitCode });
            }
            catch (OperationCanceledException)
            {
                AppendTerminalOutput("\n[Process cancelled]\n", "#f59e0b");
            }
            catch (Exception ex)
            {
                AppendTerminalOutput($"\nError: {ex.Message}\n", "#ef4444");
            }
            finally
            {
                _terminalProcess = null;
                if (_terminalCts?.Token == token)
                {
                    _terminalCts?.Dispose();
                    _terminalCts = null;
                }
            }
        }

        private void AppendTerminalOutput(string text, string? color = null)
        {
            var run = new System.Windows.Documents.Run(text);
            if (color != null)
                run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            
            TerminalOutput.Inlines.Add(run);
            TerminalScroller.ScrollToEnd();
        }

        public void StopTerminalProcess()
        {
            _terminalCts?.Cancel();
            _terminalCts?.Dispose();
            _terminalCts = null;
            try { _terminalProcess?.Kill(); } catch { }
        }

        #endregion

        #region AI Assistant

        private void AIMode_Changed(object sender, RoutedEventArgs e)
        {
            if (AskModeBtn.IsChecked == true) _currentAIMode = AIMode.Ask;
            else if (EditModeBtn.IsChecked == true) _currentAIMode = AIMode.Edit;
            else if (FixModeBtn.IsChecked == true) _currentAIMode = AIMode.Fix;
            
            UpdateContextIndicator();
        }

        private void AgentRoleCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (AgentRoleCombo?.SelectedItem is ComboBoxItem it && it.Content is string s && !string.IsNullOrWhiteSpace(s))
                {
                    _agentRole = s.Trim();
                    StatusText.Text = $"Role: {_agentRole}";
                }
            }
            catch
            {
            }
        }

        private void UpdateContextIndicator()
        {
            if (_activeFilePath == null)
            {
                ContextIndicator.Visibility = Visibility.Collapsed;
                return;
            }
            
            var fileName = Path.GetFileName(_activeFilePath);
            var hasSelection = !string.IsNullOrEmpty(_currentSelection);
            
            if (hasSelection)
            {
                ContextText.Text = $"📎 {fileName} (lines {_selectionStartLine}-{_selectionEndLine})";
            }
            else
            {
                ContextText.Text = $"📎 {fileName}";
            }
            
            ContextIndicator.Visibility = Visibility.Visible;
        }

        private void AIInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Could add typing indicator or suggestions here
        }

        private void AIInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                SendAI_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void SendAI_Click(object sender, RoutedEventArgs e)
        {
            var prompt = AIInput.Text.Trim();
            if (string.IsNullOrEmpty(prompt)) return;
            
            AIInput.Clear();
            AIEmptyState.Visibility = Visibility.Collapsed;
            
            // Add user message
            AddAIChatMessage(prompt, true);
            
            // Build context
            var context = BuildAIContext(prompt);
            
            _logger.Log("AIPrompt", new { 
                mode = _currentAIMode.ToString(),
                promptLength = prompt.Length,
                contextSize = context.Length,
                hasSelection = !string.IsNullOrEmpty(_currentSelection)
            });
            
            // Show thinking indicator
            var thinkingMsg = AddAIChatMessage("Thinking...", false, isThinking: true);
            
            _aiCts?.Cancel();
            _aiCts?.Dispose();
            _aiCts = new CancellationTokenSource();

            // Use linked token with timeout
            using var timeoutCts = new CancellationTokenSource(120000);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_aiCts.Token, timeoutCts.Token);
            var ct = linkedCts.Token;

            try
            {
                // Call AI
                var response = await GetAIResponseAsync(context, ct);
                
                // Remove thinking indicator
                AIChatPanel.Children.Remove(thinkingMsg);
                
                // Add response
                if (_currentAIMode == AIMode.Edit || _currentAIMode == AIMode.Fix)
                {
                    AddAIDiffMessage(response);
                }
                else
                {
                    AddAIChatMessage(response, false);
                }
                
                _logger.Log("AIResponse", new { responseLength = response.Length });
            }
            catch (OperationCanceledException)
            {
                AIChatPanel.Children.Remove(thinkingMsg);
                AddAIChatMessage("CANCELLED · OPERATION STOPPED", false, isThinking: false, isError: true);
            }
            catch (Exception ex)
            {
                AIChatPanel.Children.Remove(thinkingMsg);
                AddAIChatMessage($"Error: {ex.Message}", false, isError: true);
            }
            finally
            {
                AIChatScroller.ScrollToEnd();
            }
        }

        private string BuildAIContext(string userPrompt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are Atlas IDE Assistant.");
            sb.AppendLine();
            
            // Mode instruction
            sb.AppendLine($"Mode: {_currentAIMode}");
            sb.AppendLine($"Role: {_agentRole}");
            sb.AppendLine(_currentAIMode switch
            {
                AIMode.Ask => "Explain the code clearly and concisely.",
                AIMode.Edit => "Propose code changes. Return ONLY the modified code, no explanations.",
                AIMode.Fix => "Fix errors and issues. Return ONLY the corrected code.",
                _ => ""
            });
            sb.AppendLine();
            
            // Active file
            if (_activeFilePath != null && _openFiles.TryGetValue(_activeFilePath, out var file))
            {
                sb.AppendLine($"Active file: {Path.GetFileName(_activeFilePath)}");
                sb.AppendLine($"Language: {file.Language}");
                sb.AppendLine();
                
                if (!string.IsNullOrEmpty(_currentSelection))
                {
                    sb.AppendLine($"Selected code (lines {_selectionStartLine}-{_selectionEndLine}):");
                    sb.AppendLine("```");
                    sb.AppendLine(_currentSelection);
                    sb.AppendLine("```");
                }
                else
                {
                    sb.AppendLine("Full file contents:");
                    sb.AppendLine("```");
                    sb.AppendLine(file.Content);
                    sb.AppendLine("```");
                }
                sb.AppendLine();
            }
            
            // Other open files (names only)
            var otherFiles = _openFiles.Keys.Where(k => k != _activeFilePath).ToList();
            if (otherFiles.Any())
            {
                sb.AppendLine("Other open files:");
                foreach (var f in otherFiles)
                    sb.AppendLine($"  - {Path.GetFileName(f)}");
                sb.AppendLine();
            }
            
            // Terminal errors (last output)
            var terminalText = TerminalOutput.Text;
            if (!string.IsNullOrEmpty(terminalText) && terminalText.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                var lastLines = string.Join("\n", terminalText.Split('\n').TakeLast(20));
                sb.AppendLine("Recent terminal output (may contain errors):");
                sb.AppendLine(lastLines);
                sb.AppendLine();
            }
            
            sb.AppendLine($"User request: {userPrompt}");
            
            return sb.ToString();
        }

        private async Task<string> GetAIResponseAsync(string context, CancellationToken ct = default)
        {
            // Use existing AI infrastructure
            var provider = AI.AIManager.GetActiveProviderInstance();
            if (provider == null || !provider.IsConfigured)
                throw new Exception("No AI provider configured. Please set up API keys in Settings.");
            
            // Build messages list with system prompt and user message
            var messages = new List<object>
            {
                new { role = "system", content = GetIDESystemPrompt() },
                new { role = "user", content = context }
            };
            
            var response = await AI.AIManager.SendMessageAsync(messages, 2000, ct);
            
            if (!response.Success)
            {
                if (ct.IsCancellationRequested)
                    return "CANCELLED · OPERATION STOPPED";
                throw new Exception(response.Error ?? "AI request failed");
            }
            
            return response.Content ?? "No response from AI.";
        }
        
        private string GetIDESystemPrompt()
        {
            var role = (_agentRole ?? "Builder").Trim();
            var roleBlock = role switch
            {
                "Designer" => "You prioritize UI/UX, layout, styling, accessibility, and interaction design. Prefer concrete WPF/XAML changes and design-system consistency.",
                "Debugger" => "You prioritize diagnosing issues, reproducing bugs, interpreting logs, and proposing minimal safe fixes with verification steps.",
                "Architect" => "You prioritize architecture, separation of concerns, testability, and maintainable patterns. Prefer incremental refactors over rewrites.",
                _ => "You prioritize building working features quickly with clean code and pragmatic tradeoffs."
            };

            return $@"You are Atlas IDE Assistant, an AI coding assistant embedded in the Atlas IDE.
You help developers understand, edit, fix, and build real software.

Current role: {role}
{roleBlock}

When asked to explain code, provide clear and concise explanations.
When asked to edit code, provide the exact changes needed in a diff-like format.
When asked to fix code, identify the issue and provide the corrected code.

Always be helpful, accurate, and respect the developer's time.";
        }

        private Border AddAIChatMessage(string text, bool isUser, bool isThinking = false, bool isError = false)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    isUser ? "#22d3ee15" : (isError ? "#ef444415" : "#1e293b"))),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = 280
            };
            
            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    isError ? "#ef4444" : (isThinking ? "#64748b" : "#e2e8f0"))),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                FontStyle = isThinking ? FontStyles.Italic : FontStyles.Normal
            };
            
            border.Child = textBlock;
            AIChatPanel.Children.Add(border);
            
            return border;
        }

        private void AddAIDiffMessage(string codeResponse)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };
            
            var stack = new StackPanel();
            
            // Code preview
            var codeBlock = new TextBox
            {
                Text = codeResponse,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0a0c14")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e2e8f0")),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 200,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            stack.Children.Add(codeBlock);
            
            // Action buttons
            var btnStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            
            var applyBtn = new Button
            {
                Content = "✓ Apply Changes",
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22d3ee")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0a0c14")),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Tag = codeResponse
            };
            applyBtn.Click += ApplyChanges_Click;
            
            var discardBtn = new Button
            {
                Content = "✕ Discard",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b")),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b40")),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            discardBtn.Click += (s, e) => AIChatPanel.Children.Remove(border);
            
            btnStack.Children.Add(applyBtn);
            btnStack.Children.Add(discardBtn);
            stack.Children.Add(btnStack);
            
            border.Child = stack;
            AIChatPanel.Children.Add(border);
        }

        private void ApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string newCode && _activeFilePath != null)
            {
                if (_openFiles.TryGetValue(_activeFilePath, out var file))
                {
                    // Apply to selection or full file
                    if (!string.IsNullOrEmpty(_currentSelection))
                    {
                        // Apply to selection via Monaco
                        var escapedCode = JsonSerializer.Serialize(newCode);
                        var script = $"applyEdit({_selectionStartLine}, {_selectionEndLine}, {escapedCode});";
                        EditorWebView.CoreWebView2?.ExecuteScriptAsync(script);
                    }
                    else
                    {
                        // Replace entire file
                        file.Content = newCode;
                        file.IsDirty = true;
                        ActivateFile(_activeFilePath);
                    }
                    
                    _logger.Log("ChangesApplied", new { file = _activeFilePath });
                    StatusText.Text = "Changes applied";
                    
                    // Remove the diff message
                    var parent = (btn.Parent as StackPanel)?.Parent as StackPanel;
                    if (parent?.Parent is Border border)
                        AIChatPanel.Children.Remove(border);
                }
            }
        }

        private void ClearAI_Click(object sender, RoutedEventArgs e)
        {
            AIChatPanel.Children.Clear();
            AIChatPanel.Children.Add(AIEmptyState);
            AIEmptyState.Visibility = Visibility.Visible;
        }

        #endregion

        #region Public API (for Atlas context injection)

        public List<string> GetOpenFiles() => _openFiles.Keys.ToList();
        
        public string? GetActiveFile() => _activeFilePath;
        
        public string? GetFileContents(string path) => 
            _openFiles.TryGetValue(path, out var file) ? file.Content : null;
        
        public string? GetActiveFileContents() => 
            _activeFilePath != null ? GetFileContents(_activeFilePath) : null;
        
        public (string? text, int startLine, int endLine) GetSelection() => 
            (_currentSelection, _selectionStartLine, _selectionEndLine);
        
        public Dictionary<string, string> GetProjectTree()
        {
            var tree = new Dictionary<string, string>();
            if (_projectPath == null) return tree;
            
            void AddFiles(string dir, string prefix = "")
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    var relativePath = Path.GetRelativePath(_projectPath, file);
                    tree[relativePath] = "file";
                }
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    var name = Path.GetFileName(subDir);
                    if (name.StartsWith(".") || name == "node_modules" || name == "bin" || name == "obj")
                        continue;
                    var relativePath = Path.GetRelativePath(_projectPath, subDir);
                    tree[relativePath] = "directory";
                    AddFiles(subDir, relativePath);
                }
            }
            
            AddFiles(_projectPath);
            return tree;
        }

        #endregion
    }

    #region Models

    public class OpenFile
    {
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
        public string OriginalContent { get; set; } = "";
        public string Language { get; set; } = "plaintext";
        public bool IsDirty { get; set; }
    }

    public class EditorMessage
    {
        public string Type { get; set; } = "";
        public string? Content { get; set; }
        public string? SelectedText { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public enum AIMode
    {
        Ask,
        Edit,
        Fix,
        Agent
    }

    public class SelectionChangedEventArgs : EventArgs
    {
        public string? FilePath { get; set; }
        public string? SelectedText { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }

    public class FileOpenedEventArgs : EventArgs
    {
        public string Path { get; set; } = "";
        public string Language { get; set; } = "";
    }

    public class FileSavedEventArgs : EventArgs
    {
        public string Path { get; set; } = "";
    }

    #endregion

    #region IDE Logger

    public class IDELogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "ide_debug.jsonl");

        public void Log(string eventType, object data)
        {
            try
            {
                var logDir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                var entry = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    @event = eventType,
                    data
                };

                var json = JsonSerializer.Serialize(entry);
                File.AppendAllText(LogPath, json + Environment.NewLine);
                
                Debug.WriteLine($"[IDE] {eventType}: {JsonSerializer.Serialize(data)}");
            }
            catch
            {
                // Fail silently
            }
        }
    }

    #endregion
}
