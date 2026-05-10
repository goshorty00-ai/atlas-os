using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using AtlasAI.Controls;
using AtlasAI.Coding;
using Microsoft.Web.WebView2.Core;
using AtlasAI.Agent;
using System.Threading.Tasks;

namespace AtlasAI.UI.Pages
{
    public partial class CodePage : UserControl
    {
        private enum CodeMode
        {
            Builder,
            Ide
        }

        private IDEWindow? _ide;
        private CodeMode _mode = CodeMode.Builder;
        private bool _figmaWebViewInitialized;
        private bool _figmaLoaded;
        private static readonly System.Collections.Generic.List<string> _recentFolders = new System.Collections.Generic.List<string>();
        private int _chatInFlight;
        private long _lastChatStartUtcTicks;
        private readonly string _figmaCacheBuster = DateTime.UtcNow.Ticks.ToString();
        private System.Diagnostics.Process? _buildProcess;
        private System.Diagnostics.Process? _testProcess;
        private long _lastUiIndexWriteUtcTicks;

        public CodePage()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BuilderControl != null)
                    BuilderControl.OpenFileRequested += BuilderControl_OpenFileRequested;
                ApplyMode(CodeMode.Builder);
            }
            catch
            {
            }
        }

        private void SwitchModeBtn_Click(object sender, RoutedEventArgs e) => ApplyMode(_mode == CodeMode.Ide ? CodeMode.Builder : CodeMode.Ide);

        private void ToolsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menu = new ContextMenu();

                var openFolder = new MenuItem { Header = "Open Project Folder…" };
                openFolder.Click += (_, __) =>
                {
                    try
                    {
                        var dialog = new OpenFolderDialog { Title = "Open Project Folder" };
                        if (dialog.ShowDialog() != true) return;
                        var folder = dialog.FolderName;
                        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

                        AddRecentFolder(folder);
                        SaveWorkspacePreference(folder);
                        try { BuilderControl?.SetWorkspaceRoot(folder); } catch { }
                        EnsureIdeEmbedded();
                        try { _ide?.OpenProject(folder); } catch { }
                    }
                    catch
                    {
                    }
                };
                menu.Items.Add(openFolder);

                if (_recentFolders.Count > 0)
                {
                    var recent = new MenuItem { Header = "Recent (session)" };
                    foreach (var folder in _recentFolders.Take(10))
                    {
                        var mi = new MenuItem { Header = folder, Tag = folder };
                        mi.Click += (_, __) =>
                        {
                            try
                            {
                                if (mi.Tag is not string f) return;
                                if (string.IsNullOrWhiteSpace(f) || !Directory.Exists(f)) return;

                                AddRecentFolder(f);
                                SaveWorkspacePreference(f);
                                try { BuilderControl?.SetWorkspaceRoot(f); } catch { }
                                EnsureIdeEmbedded();
                                try { _ide?.OpenProject(f); } catch { }
                            }
                            catch
                            {
                            }
                        };
                        recent.Items.Add(mi);
                    }
                    menu.Items.Add(recent);
                }

                var openInExplorer = new MenuItem { Header = "Open in Explorer" };
                openInExplorer.Click += (_, __) =>
                {
                    try
                    {
                        var folder = BuilderControl?.WorkspaceRoot;
                        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{folder}\"",
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                    }
                };
                menu.Items.Add(openInExplorer);

                var tasks = new MenuItem { Header = "Tasks" };
                var run = new MenuItem { Header = "Run" };
                run.Click += (_, __) => { try { BuilderControl?.RunDefaultTask(); } catch { } };
                var again = new MenuItem { Header = "Run last" };
                again.Click += (_, __) => { try { BuilderControl?.RunLastTask(); } catch { } };
                var build = new MenuItem { Header = "Build" };
                build.Click += (_, __) => { try { BuilderControl?.BuildDefault(); } catch { } };
                var test = new MenuItem { Header = "Test" };
                test.Click += (_, __) => { try { BuilderControl?.TestDefault(); } catch { } };
                var firstError = new MenuItem { Header = "Open first error" };
                firstError.Click += (_, __) => { try { BuilderControl?.OpenFirstError(); } catch { } };
                var nextError = new MenuItem { Header = "Next error" };
                nextError.Click += (_, __) => { try { BuilderControl?.OpenNextError(); } catch { } };
                var prevError = new MenuItem { Header = "Previous error" };
                prevError.Click += (_, __) => { try { BuilderControl?.OpenPrevError(); } catch { } };
                var stop = new MenuItem { Header = "Stop running task" };
                stop.Click += (_, __) => { try { BuilderControl?.StopRunningTask(); } catch { } };
                var list = new MenuItem { Header = "List tasks" };
                list.Click += (_, __) => { try { BuilderControl?.ListTasks(); } catch { } };
                var history = new MenuItem { Header = "History" };
                history.Click += (_, __) => { try { BuilderControl?.ShowTaskHistory(); } catch { } };
                tasks.Items.Add(run);
                tasks.Items.Add(again);
                tasks.Items.Add(build);
                tasks.Items.Add(test);
                tasks.Items.Add(firstError);
                tasks.Items.Add(nextError);
                tasks.Items.Add(prevError);
                tasks.Items.Add(new Separator());
                tasks.Items.Add(stop);
                tasks.Items.Add(new Separator());
                tasks.Items.Add(list);
                tasks.Items.Add(history);
                menu.Items.Add(tasks);

                var output = new MenuItem { Header = "Output" };
                var clear = new MenuItem { Header = "Clear" };
                clear.Click += (_, __) => { try { BuilderControl?.ClearOutput(); } catch { } };
                output.Items.Add(clear);
                menu.Items.Add(output);

                var mode = new MenuItem { Header = _mode == CodeMode.Ide ? "Switch to Builder Mode" : "Switch to IDE Mode" };
                mode.Click += (_, __) => ApplyMode(_mode == CodeMode.Ide ? CodeMode.Builder : CodeMode.Ide);
                menu.Items.Add(new Separator());
                menu.Items.Add(mode);

                ToolsBtn.ContextMenu = menu;
                menu.PlacementTarget = ToolsBtn;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            }
            catch
            {
            }
        }

        private static void AddRecentFolder(string folder)
        {
            try
            {
                var f = (folder ?? "").Trim();
                if (string.IsNullOrWhiteSpace(f)) return;

                _recentFolders.RemoveAll(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase));
                _recentFolders.Insert(0, f);
                if (_recentFolders.Count > 12)
                    _recentFolders.RemoveRange(12, _recentFolders.Count - 12);
            }
            catch
            {
            }
        }

        private void BuilderControl_OpenFileRequested(object? sender, OpenFileRequestEventArgs e)
        {
            try
            {
                ApplyMode(CodeMode.Ide);
                EnsureIdeEmbedded();

                try
                {
                    var root = BuilderControl?.WorkspaceRoot;
                    if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                        _ide?.OpenProject(root);
                }
                catch
                {
                }

                try
                {
                    if (e != null && e.Line > 1)
                        _ide?.OpenFileAtLine(e.Path, e.Line);
                    else
                        _ide?.OpenFile(e.Path);
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private void ApplyMode(CodeMode mode)
        {
            try
            {
                _mode = mode;
                ModeLabel.Text = mode == CodeMode.Ide ? "ATLAS IDE" : "AUTONOMOUS AI BUILDER";

                BuilderHost.Visibility = mode == CodeMode.Builder ? Visibility.Visible : Visibility.Collapsed;
                IdeHost.Visibility = mode == CodeMode.Ide ? Visibility.Visible : Visibility.Collapsed;
                SwitchModeBtn.Content = mode == CodeMode.Ide ? "</> SWITCH TO BUILDER MODE" : "</> SWITCH TO IDE MODE";

                if (mode == CodeMode.Ide)
                {
                    EnsureIdeEmbedded();
                }
                else
                {
                    EnsureBuilderFigmaEmbedded();
                }
            }
            catch
            {
            }
        }

        private void EnsureIdeEmbedded()
        {
            try
            {
                if (_ide != null) return;

                _ide = new IDEWindow
                {
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true
                };
                try
                {
                    _ide.SetEmbeddedHost(Window.GetWindow(this));
                    _ide.EmbeddedCloseRequested += (_, __) => ApplyMode(CodeMode.Builder);
                }
                catch
                {
                }

                try
                {
                    var root = TryFindWorkspaceRoot();
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        try { BuilderControl?.SetWorkspaceRoot(root); } catch { }
                        _ide.OpenProject(root);
                    }
                }
                catch
                {
                }

                var content = _ide.Content as UIElement;
                if (content != null)
                {
                    _ide.Content = null;
                    IdeHost.Child = content;
                }
            }
            catch
            {
            }
        }

        public bool TryInjectMicTranscript(string transcript)
        {
            try
            {
                var text = (transcript ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                if (BuilderFigmaWebView?.CoreWebView2 == null)
                    return false;

                var payload = new
                {
                    type = "code.mic.transcript",
                    payload = new { transcript = text }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                BuilderFigmaWebView.CoreWebView2.PostWebMessageAsJson(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async void EnsureBuilderFigmaEmbedded()
        {
            try
            {
                BuilderControl.Visibility = Visibility.Collapsed;
                BuilderFigmaHost.Visibility = Visibility.Visible;
                TopBar.Visibility = Visibility.Collapsed;
                ContentGrid.Margin = new Thickness(0);
                BuilderHost.CornerRadius = new CornerRadius(0);

                await BuilderFigmaWebView.EnsureCoreWebView2Async();

                // Only hook once
                if (!_figmaWebViewInitialized)
                {
                    _figmaWebViewInitialized = true;
                    BuilderFigmaWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                    BuilderFigmaWebView.CoreWebView2.WebMessageReceived += BuilderFigmaWebView_WebMessageReceived;
                }

                // Search for the Figma dist folder across candidate roots
                string? dist = null;

                // Prefer dist shipped alongside the built EXE (bin output / publish)
                try
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var shipped = System.IO.Path.Combine(baseDir, "Figma", "Futuristic AI Command Center (6)", "dist");
                    if (System.IO.Directory.Exists(shipped))
                        dist = shipped;
                }
                catch { }

                var candidates = new[]
                {
                    System.Environment.CurrentDirectory,
                    AppDomain.CurrentDomain.BaseDirectory,
                    System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                };

                foreach (var root in candidates)
                {
                    // Walk up from each candidate looking for the Figma folder
                    var dir = new System.IO.DirectoryInfo(root);
                    for (int i = 0; i < 10 && dir != null; i++)
                    {
                        var figmaRoot = System.IO.Path.Combine(dir.FullName, "Figma");
                        if (System.IO.Directory.Exists(figmaRoot))
                        {
                            var uiFolder = System.IO.Directory.GetDirectories(figmaRoot, "Futuristic AI Command Center (*)", System.IO.SearchOption.TopDirectoryOnly)
                                .OrderByDescending(d => d)
                                .FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(uiFolder))
                            {
                                var d = System.IO.Path.Combine(uiFolder, "dist");
                                if (System.IO.Directory.Exists(d))
                                {
                                    dist = d;
                                    break;
                                }
                            }
                        }
                        dir = dir.Parent;
                    }
                    if (dist != null) break;
                }

                if (string.IsNullOrWhiteSpace(dist)) return;

                long indexWriteTicks = 0;
                try
                {
                    var indexPath = System.IO.Path.Combine(dist, "index.html");
                    if (System.IO.File.Exists(indexPath))
                        indexWriteTicks = System.IO.File.GetLastWriteTimeUtc(indexPath).Ticks;
                }
                catch { }

                if (!_figmaLoaded)
                {
                    _figmaLoaded = true;
                    _lastUiIndexWriteUtcTicks = indexWriteTicks;
                    BuilderFigmaWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("atlas-ui", dist, CoreWebView2HostResourceAccessKind.Allow);
                    BuilderFigmaWebView.Source = new Uri($"https://atlas-ui/index.html?mode=autonomous&v={_figmaCacheBuster}");
                }
                else
                {
                    // If the UI bundle changed (new dist build), force a reload with a new cache-buster.
                    if (indexWriteTicks != 0 && indexWriteTicks != _lastUiIndexWriteUtcTicks)
                    {
                        _lastUiIndexWriteUtcTicks = indexWriteTicks;
                        var v = DateTime.UtcNow.Ticks.ToString();
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            try { BuilderFigmaWebView.Source = new Uri($"https://atlas-ui/index.html?mode=autonomous&v={v}"); } catch { }
                        });
                    }
                }
            }
            catch
            {
            }
        }

        private void BuilderFigmaWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(raw)) raw = e.WebMessageAsJson;
                if (string.IsNullOrWhiteSpace(raw)) return;

                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != System.Text.Json.JsonValueKind.String) return;
                var type = typeProp.GetString() ?? "";
                if (string.IsNullOrEmpty(type)) return;

                // Local helper: safely get a string property value
                string? Str(string key)
                {
                    if (!root.TryGetProperty(key, out var p)) return null;
                    return p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() : p.ToString();
                }

                void Post(object payload)
                {
                    try
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(payload);
                        BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                    }
                    catch { }
                }

                void RefreshFileTree()
                {
                    try
                    {
                        var wsRoot = BuilderControl?.WorkspaceRoot ?? System.Environment.CurrentDirectory;
                        var tree = BuildFileTreeForBuilder(wsRoot);
                        Post(new { type = "files_tree", root = tree });
                    }
                    catch { }
                }

                // React app signals it's ready
                if (type == "ready")
                {
                    return;
                }

                if (type == "list_files")
                {
                    var wsRoot = BuilderControl?.WorkspaceRoot ?? System.Environment.CurrentDirectory;
                    var tree = BuildFileTreeForBuilder(wsRoot);
                    var payload = new { type = "files_tree", root = tree };
                    var json = System.Text.Json.JsonSerializer.Serialize(payload);
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                    return;
                }
                if (type == "open_file")
                {
                    var filePath = Str("path");
                    if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                    {
                        var content = System.IO.File.ReadAllText(filePath);
                        var payload = new { type = "file_content", path = filePath, content };
                        var json = System.Text.Json.JsonSerializer.Serialize(payload);
                        BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                    }
                    return;
                }
                if (type == "run_command")
                {
                    var command = Str("command");
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"")
                            {
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true,
                                WorkingDirectory = System.Environment.CurrentDirectory
                            };
                            new System.Diagnostics.Process { StartInfo = psi }.Start();
                        }
                        catch { }
                    }
                    return;
                }
                if (type == "start_build")
                {
                    _ = RunDotnetBuildAndStreamToBuilderAsync();
                    return;
                }
                if (type == "start_rebuild")
                {
                    _ = RunDotnetRebuildAndStreamToBuilderAsync();
                    return;
                }
                if (type == "start_tests" || type == "start_test")
                {
                    _ = RunDotnetTestAndStreamToBuilderAsync();
                    return;
                }
                if (type == "cancel_build" || type == "cancel_tests" || type == "cancel_test")
                {
                    CancelBuildAndTestsForBuilder();
                    return;
                }
                if (type == "navigate_back")
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (Window.GetWindow(this) is CommandCenterWindow ccw)
                                ccw.NavigateToView("AI CHAT");
                        }
                        catch { }
                    });
                    return;
                }
                if (type == "chat_message")
                {
                    var text = Str("text");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Prevent double-sends / parallel agent loops (helps avoid rate-limit TPM spikes)
                        var nowTicks = DateTime.UtcNow.Ticks;
                        var lastTicks = System.Threading.Interlocked.Read(ref _lastChatStartUtcTicks);
                        if (lastTicks != 0 && (nowTicks - lastTicks) < TimeSpan.FromMilliseconds(750).Ticks)
                            return;

                        if (System.Threading.Interlocked.Exchange(ref _chatInFlight, 1) == 1)
                        {
                            try
                            {
                                var busy = System.Text.Json.JsonSerializer.Serialize(new { type = "chat_error", text = "BUSY · REQUEST ALREADY IN PROGRESS" });
                                Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(busy));
                            }
                            catch { }
                            return;
                        }

                        System.Threading.Interlocked.Exchange(ref _lastChatStartUtcTicks, nowTicks);

                        var agentType = Str("agent") ?? "builder";
                        var provider = Str("provider");
                        var model = Str("model");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await RunAgentChatToBuilderAsync(text, agentType, provider, model);
                            }
                            finally
                            {
                                System.Threading.Interlocked.Exchange(ref _chatInFlight, 0);
                            }
                        });
                    }
                    return;
                }

                // === OPEN FOLDER ===
                if (type == "open_folder")
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var dialog = new Microsoft.Win32.OpenFolderDialog
                            {
                                Title = "Select Project Folder",
                            };
                            if (dialog.ShowDialog() == true)
                            {
                                var folder = dialog.FolderName;
                                AddRecentFolder(folder);
                                var tree = BuildFileTreeForBuilder(folder);
                                var payload = new { type = "folder_opened", root = tree };
                                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                                BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                            }
                        }
                        catch { }
                    });
                    return;
                }

                // === SET MODEL ===
                if (type == "set_model")
                {
                    try
                    {
                        var providerName = Str("provider");
                        var modelId = (Str("model") ?? "").Trim();
                        if (!string.IsNullOrEmpty(providerName))
                        {
                            var provType = providerName.ToLowerInvariant() switch
                            {
                                "claude" => AI.AIProviderType.Claude,
                                "openai" => AI.AIProviderType.OpenAI,
                                "gemini" => AI.AIProviderType.Gemini,
                                _ => AI.AIProviderType.Claude
                            };

                            // Set provider, then (optionally) apply the chosen model for that provider.
                            _ = ApplyProviderSelectionAsync(provType, modelId);
                        }
                        else
                        {
                            // Provider omitted: apply model to whatever provider is currently active.
                            if (!string.IsNullOrWhiteSpace(modelId) && !string.Equals(modelId, "auto", StringComparison.OrdinalIgnoreCase))
                                AI.AIManager.SetSelectedModel(modelId);
                        }
                    }
                    catch { }
                    return;
                }

                // === WINDOW CONTROLS (TopNav) ===
                if (type == "window_control")
                {
                    try
                    {
                        var action = (Str("action") ?? "").Trim().ToLowerInvariant();
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                var win = System.Windows.Window.GetWindow(this) ?? System.Windows.Application.Current?.MainWindow;
                                if (win == null) return;

                                if (action == "minimize")
                                {
                                    win.WindowState = System.Windows.WindowState.Minimized;
                                }
                                else if (action == "maximize")
                                {
                                    win.WindowState = win.WindowState == System.Windows.WindowState.Maximized
                                        ? System.Windows.WindowState.Normal
                                        : System.Windows.WindowState.Maximized;
                                }
                                else if (action == "close")
                                {
                                    try { System.Windows.SystemCommands.CloseWindow(win); } catch { }
                                    try { win.Close(); } catch { }
                                    try { System.Windows.Application.Current?.Shutdown(); } catch { }

                                    // Fallback: some windows/tray setups swallow Close(); force an exit.
                                    _ = ExitApplicationAfterDelayAsync(TimeSpan.FromMilliseconds(750));
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }
                    return;
                }

                // === RUN TERMINAL COMMAND ===
                if (type == "run_terminal")
                {
                    var termCmd = Str("command");
                    if (!string.IsNullOrWhiteSpace(termCmd))
                        _ = RunTerminalCommandAsync(termCmd);
                    return;
                }

                // === NEW PROJECT ===
                if (type == "new_project")
                {
                    var templateId = Str("template");
                    if (!string.IsNullOrEmpty(templateId))
                        _ = CreateNewProjectAsync(templateId, Str("name"));
                    return;
                }

                // === SAVE FILE ===
                if (type == "save_file")
                {
                    var savePath = Str("path");
                    var saveContent = Str("content") ?? "";
                    if (!string.IsNullOrEmpty(savePath))
                    {
                        try
                        {
                            System.IO.File.WriteAllText(savePath, saveContent);
                            Post(new { type = "chat_status", text = $"Saved: {System.IO.Path.GetFileName(savePath)}" });
                            RefreshFileTree();
                        }
                        catch (Exception ex)
                        {
                            Post(new { type = "chat_error", text = $"Save failed: {ex.Message}" });
                        }
                    }
                    return;
                }

                // === CREATE FILE ===
                if (type == "create_file")
                {
                    var createPath = Str("path");
                    if (!string.IsNullOrEmpty(createPath))
                    {
                        try
                        {
                            var dir = System.IO.Path.GetDirectoryName(createPath);
                            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                                System.IO.Directory.CreateDirectory(dir);
                            System.IO.File.WriteAllText(createPath, Str("content") ?? "");
                            Post(new { type = "chat_status", text = $"Created file: {System.IO.Path.GetFileName(createPath)}" });
                            RefreshFileTree();
                        }
                        catch (Exception ex)
                        {
                            Post(new { type = "chat_error", text = $"Create file failed: {ex.Message}" });
                        }
                    }
                    return;
                }

                // === CREATE FOLDER ===
                if (type == "create_folder")
                {
                    var folderPath = Str("path");
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        try
                        {
                            System.IO.Directory.CreateDirectory(folderPath);
                            Post(new { type = "chat_status", text = $"Created folder: {System.IO.Path.GetFileName(folderPath.TrimEnd('\\', '/'))}" });
                            RefreshFileTree();
                        }
                        catch (Exception ex)
                        {
                            Post(new { type = "chat_error", text = $"Create folder failed: {ex.Message}" });
                        }
                    }
                    return;
                }

                // === DELETE FILE ===
                if (type == "delete_file")
                {
                    var deletePath = Str("path");
                    if (!string.IsNullOrEmpty(deletePath))
                    {
                        try
                        {
                            if (System.IO.File.Exists(deletePath)) System.IO.File.Delete(deletePath);
                            else if (System.IO.Directory.Exists(deletePath)) System.IO.Directory.Delete(deletePath, true);
                            Post(new { type = "chat_status", text = $"Deleted: {System.IO.Path.GetFileName(deletePath.TrimEnd('\\', '/'))}" });
                            RefreshFileTree();
                        }
                        catch (Exception ex)
                        {
                            Post(new { type = "chat_error", text = $"Delete failed: {ex.Message}" });
                        }
                    }
                    return;
                }

                // === GET MODELS INFO ===
                if (type == "get_models")
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Use the *effective* provider instance so the UI doesn't drift from
                            // what the host will actually use (AIManager can auto-fallback).
                            var activeProviderInstance = AI.AIManager.GetActiveProviderInstance();
                            var activeProviderType = activeProviderInstance?.ProviderType ?? AI.AIManager.GetActiveProvider();
                            var providerName = activeProviderType switch
                            {
                                AI.AIProviderType.Claude => "claude",
                                AI.AIProviderType.OpenAI => "openai",
                                AI.AIProviderType.Gemini => "gemini",
                                _ => "claude"
                            };

                            var selectedModel = AI.AIManager.GetSelectedModel();

                            static List<object> MapModels(List<AI.AIModel>? models)
                            {
                                var list = models ?? new List<AI.AIModel>();
                                return list
                                    .Where(m => !string.IsNullOrWhiteSpace(m?.Id))
                                    .Select(m => (object)new
                                    {
                                        id = m.Id,
                                        name = string.IsNullOrWhiteSpace(m.DisplayName) ? m.Id : m.DisplayName,
                                        description = m.Description ?? ""
                                    })
                                    .ToList();
                            }

                            var claudeProvider = AI.AIManager.GetProvider(AI.AIProviderType.Claude);
                            var openAiProvider = AI.AIManager.GetProvider(AI.AIProviderType.OpenAI);
                            var geminiProvider = AI.AIManager.GetProvider(AI.AIProviderType.Gemini);

                            var claudeModelsTask = claudeProvider?.GetModelsAsync() ?? Task.FromResult(new List<AI.AIModel>());
                            var openAiModelsTask = openAiProvider?.GetModelsAsync() ?? Task.FromResult(new List<AI.AIModel>());
                            var geminiModelsTask = geminiProvider?.GetModelsAsync() ?? Task.FromResult(new List<AI.AIModel>());

                            await Task.WhenAll(claudeModelsTask, openAiModelsTask, geminiModelsTask);

                            var payload = new
                            {
                                type = "models_info",
                                provider = providerName,
                                model = selectedModel,
                                models = new
                                {
                                    claude = MapModels(claudeModelsTask.Result),
                                    openai = MapModels(openAiModelsTask.Result),
                                    gemini = MapModels(geminiModelsTask.Result)
                                }
                            };

                            var json = System.Text.Json.JsonSerializer.Serialize(payload);
                            _ = Dispatcher.BeginInvoke(() =>
                            {
                                try { BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
                            });
                        }
                        catch
                        {
                        }
                    });
                    return;
                }
            }
            catch
            {
            }
        }

        private object BuildFileTreeForBuilder(string path)
        {
            try
            {
                var dir = new System.IO.DirectoryInfo(path);
                var node = new
                {
                    id = path,
                    name = dir.Name,
                    type = "folder",
                    isOpen = true,
                    children = System.IO.Directory.GetDirectories(path)
                        .Where(d => !System.IO.Path.GetFileName(d).StartsWith(".") && System.IO.Path.GetFileName(d) != "node_modules" && System.IO.Path.GetFileName(d) != "bin" && System.IO.Path.GetFileName(d) != "obj")
                        .Select(d => BuildFileTreeForBuilder(d))
                        .Concat(System.IO.Directory.GetFiles(path)
                            .Where(f => !System.IO.Path.GetFileName(f).StartsWith("."))
                            .Select(f => new
                            {
                                id = f,
                                name = System.IO.Path.GetFileName(f),
                                type = "file",
                                extension = System.IO.Path.GetExtension(f).Trim('.'),
                                content = (string?)null
                            }))
                        .ToList()
                };
                return node;
            }
            catch
            {
                return new { id = path, name = System.IO.Path.GetFileName(path), type = "folder", isOpen = true, children = Array.Empty<object>() };
            }
        }

        private async Task<bool> RunDotnetBuildAndStreamToBuilderAsync()
        {
            try
            {
                try
                {
                    if (_buildProcess != null && !_buildProcess.HasExited)
                        _buildProcess.Kill(true);
                }
                catch { }

                var root = BuilderControl?.WorkspaceRoot ?? System.Environment.CurrentDirectory;

                string? target = null;
                try
                {
                    // Prefer solution files, then project file
                    target = System.IO.Directory.GetFiles(root, "*.slnx", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? System.IO.Directory.GetFiles(root, "*.sln", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? System.IO.Directory.GetFiles(root, "*.csproj", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault();
                }
                catch { }

                // Build into an isolated output folder so validation can run even while Atlas is running
                // (prevents locked bin/obj outputs from failing the build).
                string? isolatedProps = null;
                try
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                    var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AtlasAI", "validation", stamp);
                    var outDir = System.IO.Path.Combine(basePath, "out") + System.IO.Path.DirectorySeparatorChar;
                    var objDir = System.IO.Path.Combine(basePath, "obj") + System.IO.Path.DirectorySeparatorChar;
                    System.IO.Directory.CreateDirectory(outDir);
                    System.IO.Directory.CreateDirectory(objDir);
                    isolatedProps = $" -p:BaseOutputPath=\"{outDir}\" -p:BaseIntermediateOutputPath=\"{objDir}\"";
                }
                catch
                {
                    isolatedProps = null;
                }

                var args = string.IsNullOrWhiteSpace(target) ? "build" : $"build \"{target}\"";
                if (!string.IsNullOrWhiteSpace(isolatedProps)) args += isolatedProps;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = args,
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                var p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
                _buildProcess = p;
                p.OutputDataReceived += (s, e) =>
                {
                    var line = e.Data;
                    if (string.IsNullOrEmpty(line)) return;
                    var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "build_log", kind = "dotnet", line });
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    var line = e.Data;
                    if (string.IsNullOrEmpty(line)) return;
                    var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "build_log", kind = "dotnet", line });
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload);
                };
                var started = System.Text.Json.JsonSerializer.Serialize(new { type = "build_started", kind = "dotnet" });
                BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(started);

                try
                {
                    var infoLine = string.IsNullOrWhiteSpace(target)
                        ? $"> dotnet {args}"
                        : $"> dotnet {args}\n> cwd: {root}";
                    var info = System.Text.Json.JsonSerializer.Serialize(new { type = "build_log", kind = "dotnet", line = infoLine });
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(info);
                }
                catch { }

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await Task.Run(() => p.WaitForExit());
                var success = p.ExitCode == 0;
                var done = System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success });
                BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(done);

                return success;
            }
            catch (Exception ex)
            {
                try
                {
                    var err = System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success = false, error = ex.Message });
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(err);
                }
                catch
                {
                }
                return false;
            }
            finally
            {
                try { _buildProcess = null; } catch { }
            }
        }

        private void CancelBuildAndTestsForBuilder()
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
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success = false, error = "Cancelled" }));
                }
                catch { }

                try
                {
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "test_complete", success = false, error = "Cancelled" }));
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

        private async Task<bool> RunDotnetRebuildAndStreamToBuilderAsync()
        {
            try
            {
                try
                {
                    if (_buildProcess != null && !_buildProcess.HasExited)
                        _buildProcess.Kill(true);
                }
                catch { }

                var root = BuilderControl?.WorkspaceRoot ?? System.Environment.CurrentDirectory;

                string? target = null;
                try
                {
                    target = System.IO.Directory.GetFiles(root, "*.slnx", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? System.IO.Directory.GetFiles(root, "*.sln", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? System.IO.Directory.GetFiles(root, "*.csproj", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault();
                }
                catch { }

                var started = System.Text.Json.JsonSerializer.Serialize(new { type = "build_started", kind = "dotnet" });
                BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(started);

                // Use isolated output to avoid file locks while Atlas is running.
                string? isolatedProps = null;
                try
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                    var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AtlasAI", "validation", "rebuild_" + stamp);
                    var outDir = System.IO.Path.Combine(basePath, "out") + System.IO.Path.DirectorySeparatorChar;
                    var objDir = System.IO.Path.Combine(basePath, "obj") + System.IO.Path.DirectorySeparatorChar;
                    System.IO.Directory.CreateDirectory(outDir);
                    System.IO.Directory.CreateDirectory(objDir);
                    isolatedProps = $" -p:BaseOutputPath=\"{outDir}\" -p:BaseIntermediateOutputPath=\"{objDir}\"";
                }
                catch
                {
                    isolatedProps = null;
                }

                var cleanArgs = string.IsNullOrWhiteSpace(target) ? "clean" : $"clean \"{target}\"";
                var buildArgs = string.IsNullOrWhiteSpace(target) ? "build" : $"build \"{target}\"";
                if (!string.IsNullOrWhiteSpace(isolatedProps))
                {
                    cleanArgs += isolatedProps;
                    buildArgs += isolatedProps;
                }

                void Push(string line)
                {
                    if (string.IsNullOrEmpty(line)) return;
                    try
                    {
                        BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "build_log", kind = "dotnet", line }));
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
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = args,
                        WorkingDirectory = root,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    var p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
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
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success = false }));
                    return false;
                }

                var buildExit = await RunStepAsync(buildArgs);
                var success = buildExit == 0;
                BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success }));

                return success;
            }
            catch (Exception ex)
            {
                try
                {
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", kind = "dotnet", success = false, error = ex.Message }));
                }
                catch { }

                return false;
            }
            finally
            {
                try { _buildProcess = null; } catch { }
            }
        }

        private async Task<bool> RunDotnetTestAndStreamToBuilderAsync()
        {
            try
            {
                try
                {
                    if (_testProcess != null && !_testProcess.HasExited)
                        _testProcess.Kill(true);
                }
                catch { }

                var root = BuilderControl?.WorkspaceRoot ?? System.Environment.CurrentDirectory;

                string? target = null;
                try
                {
                    // Prefer solution files, then project file
                    target = System.IO.Directory.GetFiles(root, "*.slnx", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? System.IO.Directory.GetFiles(root, "*.sln", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? System.IO.Directory.GetFiles(root, "*.csproj", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault();
                }
                catch { }

                // Test into isolated output to avoid collisions/locks with the running app.
                string? isolatedProps = null;
                try
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                    var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AtlasAI", "validation", "test_" + stamp);
                    var outDir = System.IO.Path.Combine(basePath, "out") + System.IO.Path.DirectorySeparatorChar;
                    var objDir = System.IO.Path.Combine(basePath, "obj") + System.IO.Path.DirectorySeparatorChar;
                    System.IO.Directory.CreateDirectory(outDir);
                    System.IO.Directory.CreateDirectory(objDir);
                    isolatedProps = $" -p:BaseOutputPath=\"{outDir}\" -p:BaseIntermediateOutputPath=\"{objDir}\"";
                }
                catch
                {
                    isolatedProps = null;
                }

                var args = string.IsNullOrWhiteSpace(target) ? "test" : $"test \"{target}\"";
                if (!string.IsNullOrWhiteSpace(isolatedProps)) args += isolatedProps;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = args,
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                var p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
                _testProcess = p;
                p.OutputDataReceived += (s, e) =>
                {
                    var line = e.Data;
                    if (string.IsNullOrEmpty(line)) return;
                    var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "test_log", line });
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    var line = e.Data;
                    if (string.IsNullOrEmpty(line)) return;
                    var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "test_log", line });
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload);
                };

                var started = System.Text.Json.JsonSerializer.Serialize(new { type = "test_started" });
                BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(started);

                try
                {
                    var infoLine = string.IsNullOrWhiteSpace(target)
                        ? $"> dotnet {args}"
                        : $"> dotnet {args}\n> cwd: {root}";
                    var info = System.Text.Json.JsonSerializer.Serialize(new { type = "test_log", line = infoLine });
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(info);
                }
                catch { }

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await Task.Run(() => p.WaitForExit());
                var success = p.ExitCode == 0;
                var done = System.Text.Json.JsonSerializer.Serialize(new { type = "test_complete", success });
                BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(done);

                return success;
            }
            catch (Exception ex)
            {
                try
                {
                    var err = System.Text.Json.JsonSerializer.Serialize(new { type = "test_complete", success = false, error = ex.Message });
                    BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(err);
                }
                catch { }

                return false;
            }
            finally
            {
                try { _testProcess = null; } catch { }
            }
        }

        private async Task RunAgentChatToBuilderAsync(string text, string agentType = "builder", string? provider = null, string? model = null)
        {
            try
            {
                void PostJson(object payload)
                {
                    try
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(payload);
                        Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(json));
                    }
                    catch { }
                }

                bool isAuditLike = !string.IsNullOrWhiteSpace(text) &&
                                   (text.Contains("audit", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("review", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("analy", StringComparison.OrdinalIgnoreCase));

                var trimmed = (text ?? string.Empty).Trim();
                bool isContinueLike = !string.IsNullOrWhiteSpace(trimmed) &&
                                      (trimmed.StartsWith("continue", StringComparison.OrdinalIgnoreCase) ||
                                       trimmed.StartsWith("resume", StringComparison.OrdinalIgnoreCase));

                bool isBuildLike = !string.IsNullOrWhiteSpace(text) &&
                                   (text.Contains("fix build", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("build", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("compile", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("test", StringComparison.OrdinalIgnoreCase));

                bool shouldValidate = string.Equals(agentType, "builder", StringComparison.OrdinalIgnoreCase) &&
                                      (isAuditLike || isContinueLike || isBuildLike);

                string? lastToolName = null;

                var trace = new System.Collections.Generic.List<string>(64);
                var originalText = text;
                var prompt = text;
                const int TimeoutMsPerAttempt = 120_000;
                var phase = "thinking";
                var action = "";

                object[] BuildInitialPlanItems()
                {
                    if (agentType == "planner")
                    {
                        return new object[]
                        {
                            new { id = "1", title = "Understand request", status = "active" },
                            new { id = "2", title = "Draft plan", status = "pending" },
                            new { id = "3", title = "Confirm next steps", status = "pending" },
                        };
                    }
                    if (agentType == "designer")
                    {
                        return new object[]
                        {
                            new { id = "1", title = "Inspect current UI", status = "active" },
                            new { id = "2", title = "Propose minimal tweaks", status = "pending" },
                            new { id = "3", title = "Implement diffs", status = "pending" },
                            new { id = "4", title = "Validate visuals", status = "pending" },
                        };
                    }
                    return new object[]
                    {
                        new { id = "1", title = "Understand request", status = "active" },
                        new { id = "2", title = "Gather context", status = "pending" },
                        new { id = "3", title = "Apply changes", status = "pending" },
                        new { id = "4", title = "Validate (build/tests)", status = "pending" },
                    };
                }

                object[] SetPlanStatus(object[] items, string id, string status)
                {
                    try
                    {
                        var list = new System.Collections.Generic.List<object>(items.Length);
                        foreach (var it in items)
                        {
                            var json = System.Text.Json.JsonSerializer.Serialize(it);
                            var dict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                            if (dict != null && dict.TryGetValue("id", out var vid) && (vid?.ToString() ?? "") == id)
                            {
                                dict["status"] = status;
                                list.Add(dict);
                            }
                            else
                            {
                                list.Add(it);
                            }
                        }
                        return list.ToArray();
                    }
                    catch
                    {
                        return items;
                    }
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

                // Set provider if specified
                if (!string.IsNullOrEmpty(provider))
                {
                    var provType = provider.ToLowerInvariant() switch
                    {
                        "claude" => AI.AIProviderType.Claude,
                        "openai" => AI.AIProviderType.OpenAI,
                        "gemini" => AI.AIProviderType.Gemini,
                        _ => AI.AIProviderType.Claude
                    };
                    await AI.AIManager.SetActiveProviderAsync(provType);
                }
                // Apply the model the user selected in the IDE UI (null / "auto" = use AIManager default)
                if (!string.IsNullOrEmpty(model) && model != "auto")
                    AI.AIManager.SetSelectedModel(model);

                var root = BuilderControl?.WorkspaceRoot ?? System.Environment.CurrentDirectory;

                // Run up to 2 attempts: if attempt #1 times out, auto-continue with carried context.
                var planItems = BuildInitialPlanItems();
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    phase = "thinking";
                    action = "";
                    PostJson(new { type = "agent_started", attempt, timeoutMs = TimeoutMsPerAttempt, phase, action });
                    PostJson(new { type = "plan_update", items = planItems });

                    using var timeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(TimeoutMsPerAttempt));
                    var ct = timeoutCts.Token;

                    using var heartbeatCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
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
                                PostJson(new { type = "agent_tick", attempt, elapsedMs, remainingMs, phase, action });
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch { }
                    });

                    try
                    {
                        var agent = new AgentOrchestrator(root);

                        // Set role-specific system prompt prefix based on agent type
                        if (agentType == "designer")
                        {
                            agent.SystemPromptPrefix =
                                "You are Atlas Designer Agent — a world-class UI/UX designer and WPF/XAML stylist.\n" +
                                "Goal: make the existing UI feel premium, consistent, and readable without adding new pages or unrelated features.\n" +
                                "Hard rules: reuse the existing design system (Theme/*.xaml resources, existing styles/templates). Do NOT hard-code new colors, fonts, or shadows unless they already exist in Theme resources.\n" +
                                "Process: 1) Inspect current XAML/resources, 2) propose a small set of high-impact tweaks, 3) implement minimal diffs.\n" +
                                "Output format: start with a short 'Design Plan' (3-6 bullets), then 'Edits' listing the files you changed and why.";
                        }
                        else if (agentType == "planner")
                        {
                            agent.SystemPromptPrefix = "You are Atlas Planner Agent — a senior software architect and project planner. " +
                                "When the user describes what they want to build, create a detailed plan. " +
                                "Start your response with 'PLAN:' then list numbered steps. " +
                                "Include: 1) Project structure, 2) Key files to create, 3) Technologies to use, " +
                                "4) Data models, 5) API endpoints if needed, 6) UI components. " +
                                "Be specific and actionable. After planning, ask if the user wants to proceed with building.";
                        }
                        else
                        {
                            agent.SystemPromptPrefix =
                                "You are Atlas Builder Agent — a senior full-stack engineer for this repo (WPF/.NET + embedded UI).\n" +
                                "Goal: ship working improvements with minimal, safe diffs. Fix root causes.\n" +
                                "Tooling discipline: avoid broad list_directory / recursive scans; prefer targeted search_files/read_file.\n" +
                                "Validation: after making code changes, build and run the app to confirm fixes.\n";

                            // Update plan/progress and mirror tool execution into the UI.
                            agent.OnToolExecuting += (_, tool) =>
                            {
                                try
                                {
                                    lastToolName = tool;
                                    phase = "working";
                                    action = tool ?? "";

                                    if (planItems.Length >= 4)
                                    {
                                        planItems = SetPlanStatus(planItems, "1", "done");
                                        planItems = SetPlanStatus(planItems, "2", "active");

                                        if (!string.IsNullOrEmpty(tool) && (tool.Contains("apply", StringComparison.OrdinalIgnoreCase) || tool.Contains("write", StringComparison.OrdinalIgnoreCase) || tool.Contains("patch", StringComparison.OrdinalIgnoreCase)))
                                        {
                                            planItems = SetPlanStatus(planItems, "2", "done");
                                            planItems = SetPlanStatus(planItems, "3", "active");
                                        }
                                        if (!string.IsNullOrEmpty(tool) && (tool.Contains("build", StringComparison.OrdinalIgnoreCase) || tool.Contains("test", StringComparison.OrdinalIgnoreCase)))
                                        {
                                            planItems = SetPlanStatus(planItems, "3", "done");
                                            planItems = SetPlanStatus(planItems, "4", "active");
                                        }

                                        PostJson(new { type = "plan_update", items = planItems });
                                    }

                                    PostJson(new { type = "agent_progress", phase, action });

                                    if (isAuditLike && !string.IsNullOrEmpty(tool))
                                        PostJson(new { type = "chat_status", text = $"Running audit: {tool}…" });

                                    PostJson(new { type = "tool_start", tool });
                                }
                                catch { }
                            };
                        }
                        agent.OnToolResult += (_, result) =>
                        {
                            try
                            {
                                var output = result.Output ?? "";
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
                                        PostJson(new { type = "build_log", kind = "agent", line = $"…(truncated after {MaxLines} lines)…" });
                                        break;
                                    }

                                    if (ln.Length > MaxCharsPerLine)
                                        ln = ln.Substring(0, MaxCharsPerLine) + "…(truncated)…";

                                    PostJson(new { type = "build_log", kind = "agent", line = ln });
                                }

                                if (!string.IsNullOrWhiteSpace(firstNonEmpty))
                                {
                                    trace.Add($"[out] {firstNonEmpty.Replace("\r", "").Replace("\n", " ")}");
                                }
                            }
                            catch { }
                        };
                        agent.OnResponse += (_, resp) =>
                        {
                            try
                            {
                                phase = "complete";
                                action = "";
                                PostJson(new { type = "agent_progress", phase, action });
                                PostJson(new { type = "chat_response", text = resp });
                            }
                            catch { }
                        };
                        agent.OnError += (_, err) =>
                        {
                            try
                            {
                                phase = "error";
                                action = "";
                                PostJson(new { type = "agent_progress", phase, action });
                                PostJson(new { type = "chat_error", text = err });
                            }
                            catch { }
                        };

                        // Start an "agent action" run once (don't spam build_started for every tool).
                        PostJson(new { type = "build_started", kind = "agent" });

                        await agent.RunAsync(prompt).WaitAsync(ct);

                        // If this was an audit-like request, automatically validate with a real build/test
                        // so the user sees concrete results (and not just an "action complete").
                        var validationOk = true;
                        if (shouldValidate)
                        {
                            try
                            {
                                PostJson(new { type = "chat_status", text = "Action complete — running validation build…" });
                                var buildOk = await RunDotnetBuildAndStreamToBuilderAsync();
                                validationOk = buildOk;

                                if (buildOk)
                                {
                                    PostJson(new { type = "chat_status", text = "Build finished — running tests…" });
                                    var testOk = await RunDotnetTestAndStreamToBuilderAsync();
                                    validationOk = validationOk && testOk;
                                }
                            }
                            catch
                            {
                                validationOk = false;
                            }
                        }

                        PostJson(new { type = "build_complete", kind = "agent", success = validationOk });
                        break;
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && attempt == 1)
                    {
                        // Auto-continue once.
                        trace.Add("[timeout] attempt 1 timed out");
                        phase = "continuing";
                        action = "Starting follow-up…";
                        PostJson(new { type = "agent_progress", phase, action });
                        PostJson(new { type = "chat_status", text = "Request timed out — continuing in a new follow-up with prior context…" });

                        prompt = BuildContinuationPrompt(originalText, trace);
                        continue;
                    }
                    catch (OperationCanceledException)
                    {
                        PostJson(new { type = "chat_error", text = "Request timed out. Try again (or switch model/provider), or ask in smaller chunks." });
                        PostJson(new { type = "build_complete", kind = "agent", success = false });
                        break;
                    }
                    catch (Exception ex)
                    {
                        PostJson(new { type = "chat_error", text = ex.Message });
                        PostJson(new { type = "build_complete", kind = "agent", success = false });
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
                try
                {
                    var msg2 = "Request timed out. Try again (or switch model/provider), or ask in smaller chunks.";
                    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "chat_error", text = msg2 });
                    var done = System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", success = false });
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                        BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(done);
                    });
                }
                catch { }
            }
            catch (Exception ex)
            {
                try
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "chat_error", text = ex.Message });
                    var done = System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", success = false });
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(json);
                        BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(done);
                    });
                }
                catch { }
            }
        }

        private async Task RunTerminalCommandAsync(string command)
        {
            try
            {
                var root = BuilderControl?.WorkspaceRoot ?? System.Environment.CurrentDirectory;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"").Replace("\n", ";")}\"",
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                var p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    try
                    {
                        var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "terminal_output", text = e.Data, isError = false });
                        _ = Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload));
                    }
                    catch { }
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    try
                    {
                        var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "terminal_output", text = e.Data, isError = true });
                        Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(payload));
                    }
                    catch { }
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await Task.Run(() => p.WaitForExit());
                var exitPayload = System.Text.Json.JsonSerializer.Serialize(new { type = "terminal_output", text = $"Process exited with code {p.ExitCode}", isError = p.ExitCode != 0 });
                _ = Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(exitPayload));
            }
            catch (Exception ex)
            {
                try
                {
                    var err = System.Text.Json.JsonSerializer.Serialize(new { type = "terminal_output", text = $"Error: {ex.Message}", isError = true });
                    _ = Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(err));
                }
                catch { }
            }
        }

        private async Task CreateNewProjectAsync(string templateId, string? projectNameHint)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var dialog = new System.Windows.Forms.FolderBrowserDialog
                        {
                            Description = "Select folder for new project",
                            UseDescriptionForTitle = true
                        };
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            var folder = dialog.SelectedPath;
                            var projectName = (!string.IsNullOrWhiteSpace(projectNameHint) ? projectNameHint : "MyProject").Replace(" ", "");
                            var projectPath = System.IO.Path.Combine(folder, projectName);
                            System.IO.Directory.CreateDirectory(projectPath);
                            AddRecentFolder(projectPath);
                            try { BuilderControl?.SetWorkspaceRoot(projectPath); } catch { }

                            // Scaffold based on template — ask the agent to do the heavy lifting
                            _ = RunNewProjectAgentAsync(templateId, projectPath, projectName);
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private async Task RunNewProjectAgentAsync(string templateId, string projectPath, string projectName)
        {
            try
            {
                var agent = new AgentOrchestrator(projectPath);
                agent.SystemPromptPrefix = $"You are Atlas Project Scaffolder. Create a new {templateId} project named '{projectName}' in the workspace. " +
                    "Create all necessary files for a working starter project. Include: project config files, entry point, " +
                    "README.md, .gitignore, and any framework-specific boilerplate. Make the project immediately runnable.";

                try
                {
                    var s = System.Text.Json.JsonSerializer.Serialize(new { type = "build_started", kind = "agent" });
                    _ = Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(s));
                }
                catch { }

                agent.OnThinking += (_, m) =>
                {
                    try
                    {
                        var j = System.Text.Json.JsonSerializer.Serialize(new { type = "chat_status", text = m });
                        _ = Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(j));
                    }
                    catch { }
                };

                agent.OnToolExecuting += (_, tool) =>
                {
                    try
                    {
                        var j = System.Text.Json.JsonSerializer.Serialize(new { type = "tool_start", tool });
                        Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(j));
                    }
                    catch { }
                };

                agent.OnToolResult += (_, result) =>
                {
                    try
                    {
                        var j = System.Text.Json.JsonSerializer.Serialize(new { type = "build_log", kind = "agent", line = result.Output?.Trim() ?? "" });
                        Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(j));
                    }
                    catch { }
                };

                agent.OnResponse += (_, resp) =>
                {
                    try
                    {
                        var j = System.Text.Json.JsonSerializer.Serialize(new { type = "chat_response", text = resp });
                        var d = System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", kind = "agent", success = true });
                        var tree = BuildFileTreeForBuilder(projectPath);
                        var t = System.Text.Json.JsonSerializer.Serialize(new { type = "folder_opened", root = tree });
                        Dispatcher.BeginInvoke(() =>
                        {
                            BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(j);
                            BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(d);
                            BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(t);
                        });
                    }
                    catch { }
                };

                agent.OnError += (_, err) =>
                {
                    try
                    {
                        var j = System.Text.Json.JsonSerializer.Serialize(new { type = "chat_error", text = err });
                        var d = System.Text.Json.JsonSerializer.Serialize(new { type = "build_complete", kind = "agent", success = false });
                        Dispatcher.BeginInvoke(() =>
                        {
                            BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(j);
                            BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(d);
                        });
                    }
                    catch { }
                };

                await agent.RunAsync($"Create a new {templateId} project. Set up all boilerplate files for a working starter that can be immediately built and run.");
            }
            catch (Exception ex)
            {
                try
                {
                    var j = System.Text.Json.JsonSerializer.Serialize(new { type = "chat_error", text = ex.Message });
                    _ = Dispatcher.BeginInvoke(() => BuilderFigmaWebView.CoreWebView2?.PostWebMessageAsJson(j));
                }
                catch { }
            }
        }

        private async Task ApplyProviderSelectionAsync(AI.AIProviderType providerType, string? modelId)
        {
            try
            {
                await AI.AIManager.SetActiveProviderAsync(providerType);

                if (!string.IsNullOrWhiteSpace(modelId) && !string.Equals(modelId, "auto", StringComparison.OrdinalIgnoreCase))
                    AI.AIManager.SetSelectedModel(modelId);
            }
            catch
            {
            }
        }

        private static async Task ExitApplicationAfterDelayAsync(TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay);
                Environment.Exit(0);
            }
            catch
            {
            }
        }

        private static string TryFindWorkspaceRoot()
        {
            try
            {
                try
                {
                    var prefs = AtlasAI.Core.PreferencesStore.Instance.Current;
                    var saved = (prefs.CodeWorkspaceFolder ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
                        return saved;
                }
                catch
                {
                }

                var start = AppDomain.CurrentDomain.BaseDirectory;
                var dir = new DirectoryInfo(start);
                for (var i = 0; i < 10 && dir != null; i++)
                {
                    var csproj = Path.Combine(dir.FullName, "AtlasAI.csproj");
                    if (File.Exists(csproj))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch
            {
            }
            return "";
        }

        private static void SaveWorkspacePreference(string folder)
        {
            try
            {
                var f = (folder ?? "").Trim();
                if (string.IsNullOrWhiteSpace(f) || !Directory.Exists(f)) return;
                var prefs = AtlasAI.Core.PreferencesStore.Instance.Current;
                prefs.CodeWorkspaceFolder = f;
                AtlasAI.Core.PreferencesStore.Instance.SavePreferences(prefs);
            }
            catch
            {
            }
        }

    }
}
