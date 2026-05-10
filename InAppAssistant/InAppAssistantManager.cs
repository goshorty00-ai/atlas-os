using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using AtlasAI.InAppAssistant.Models;
using AtlasAI.InAppAssistant.Services;
using AtlasAI.InAppAssistant.Skills;
using AtlasAI.Understanding;

namespace AtlasAI.InAppAssistant
{
    /// <summary>
    /// Main manager for the In-App Assistant feature
    /// Coordinates overlay, context capture, skills, and actions
    /// </summary>
    public class InAppAssistantManager : IDisposable
    {
        private readonly WindowsContextService _contextService;
        private readonly TextCaptureService _textCapture;
        private readonly AppPermissionManager _permissionManager;
        private readonly ActionLogger _actionLogger;
        private readonly ActionRunner _actionRunner;
        
        // Skills
        private readonly FileExplorerSkill _fileExplorerSkill;
        private readonly BrowserSkill _browserSkill;
        private readonly IDESkill _ideSkill;
        
        // Overlay
        private AtlasOverlayWindow? _overlayWindow;
        private bool _overlayEnabled = true;

        // Integration with existing context store
        private readonly ContextStore? _contextStore;

        public event EventHandler<string>? VoiceCommandRequested;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<ActionResult>? ActionCompleted;

        public WindowsContextService ContextService => _contextService;
        public AppPermissionManager PermissionManager => _permissionManager;
        public ActionLogger ActionLogger => _actionLogger;
        public bool IsOverlayVisible => _overlayWindow?.IsVisible ?? false;

        /// <summary>
        /// Get the WindowsContextService for external use (e.g., Inspector panel)
        /// </summary>
        public WindowsContextService GetContextService() => _contextService;

        public InAppAssistantManager(ContextStore? contextStore = null)
        {
            _contextStore = contextStore;
            
            // Initialize services
            _contextService = new WindowsContextService();
            _textCapture = new TextCaptureService();
            _permissionManager = new AppPermissionManager();
            _actionLogger = new ActionLogger();
            _actionRunner = new ActionRunner(_actionLogger, _permissionManager);
            
            // Initialize skills
            _fileExplorerSkill = new FileExplorerSkill(_actionRunner, _contextService);
            _browserSkill = new BrowserSkill(_actionRunner, _contextService);
            _ideSkill = new IDESkill(_actionRunner, _contextService);
            
            // Wire up events
            _actionRunner.ActionExecuted += OnActionExecuted;
            _contextService.ActiveAppChanged += OnActiveAppChanged;
            
            Debug.WriteLine("[InAppAssistant] Manager initialized");
        }

        /// <summary>
        /// Initialize and show the overlay window
        /// </summary>
        public void InitializeOverlay()
        {
            if (_overlayWindow != null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _overlayWindow = new AtlasOverlayWindow();
                _overlayWindow.VoiceCommandRequested += (s, e) => VoiceCommandRequested?.Invoke(this, "");
                _overlayWindow.TextCaptured += OnTextCaptured;
                _overlayWindow.ActionsRequested += OnActionsRequested;
                
                if (_overlayEnabled)
                    _overlayWindow.Show();
                    
                Debug.WriteLine("[InAppAssistant] Overlay initialized");
            });
        }

        /// <summary>
        /// Toggle overlay visibility (respects disabled state)
        /// </summary>
        public void ToggleOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_overlayWindow == null) return;
                
                if (_overlayWindow.IsVisible)
                {
                    _overlayWindow.Hide();
                    // Don't change _overlayEnabled here - toggle should just hide/show
                }
                else if (_overlayEnabled) // Only show if enabled
                {
                    _overlayWindow.Show();
                    _overlayWindow.Activate();
                }
            });
        }

        /// <summary>
        /// Show the overlay and enable it
        /// </summary>
        public void ShowOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _overlayEnabled = true;
                if (_overlayWindow != null)
                {
                    _overlayWindow.IsDisabled = false; // Allow hotkey to work again
                    _overlayWindow.Show();
                }
            });
        }

        /// <summary>
        /// Hide the overlay and disable it completely
        /// </summary>
        public void HideOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_overlayWindow != null)
                {
                    _overlayWindow.IsDisabled = true; // Prevent hotkey from re-showing
                    _overlayWindow.Hide();
                    Debug.WriteLine("[InAppAssistant] Overlay hidden and disabled");
                }
                _overlayEnabled = false; // Keep it hidden until explicitly shown
            });
        }
        
        /// <summary>
        /// Disable the overlay completely (won't show on toggle)
        /// </summary>
        public void DisableOverlay()
        {
            _overlayEnabled = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_overlayWindow != null)
                {
                    _overlayWindow.IsDisabled = true;
                    _overlayWindow.Close();
                    _overlayWindow = null;
                    Debug.WriteLine("[InAppAssistant] Overlay closed and disposed");
                }
            });
        }

        /// <summary>
        /// Get current active app context
        /// </summary>
        public ActiveAppContext GetCurrentContext()
        {
            return _contextService.GetActiveAppContext();
        }

        /// <summary>
        /// Capture selected text from active app
        /// </summary>
        public async Task<string?> CaptureSelectedTextAsync()
        {
            var context = GetCurrentContext();
            
            if (!_permissionManager.HasPermission(context.ProcessName, ActionType.ClipboardOperation))
            {
                StatusChanged?.Invoke(this, $"Text capture not permitted for {context.ProcessName}");
                return null;
            }

            _textCapture.IsEnabled = true;
            return await _textCapture.CaptureSelectedTextAsync(useClipboardFallback: true);
        }

        /// <summary>
        /// Execute an action based on user command
        /// </summary>
        public async Task<ActionResult> ExecuteCommandAsync(string command)
        {
            var context = GetCurrentContext();
            var lowerCommand = command.ToLower();

            // Route to appropriate skill based on context and command
            if (context.Category == AppCategory.FileExplorer || lowerCommand.Contains("folder") || lowerCommand.Contains("file"))
            {
                return await ExecuteFileExplorerCommandAsync(command, context);
            }
            else if (context.Category == AppCategory.Browser || lowerCommand.Contains("tab") || lowerCommand.Contains("url"))
            {
                return await ExecuteBrowserCommandAsync(command, context);
            }
            else if (context.Category == AppCategory.IDE || lowerCommand.Contains("code") || lowerCommand.Contains("format"))
            {
                return await ExecuteIDECommandAsync(command, context);
            }

            return new ActionResult
            {
                Success = false,
                Message = $"No matching skill for '{command}' in {context.ProcessName}"
            };
        }

        private async Task<ActionResult> ExecuteFileExplorerCommandAsync(string command, ActiveAppContext context)
        {
            var lower = command.ToLower();

            if (lower.Contains("new folder") || lower.Contains("create folder"))
            {
                var name = ExtractParameter(command, new[] { "called", "named", "folder" }) ?? "New Folder";
                return await _fileExplorerSkill.CreateNewFolderAsync(name);
            }
            else if (lower.Contains("rename"))
            {
                var name = ExtractParameter(command, new[] { "to", "as" }) ?? "Renamed";
                return await _fileExplorerSkill.RenameSelectedAsync(name);
            }
            else if (lower.Contains("delete"))
            {
                var permanent = lower.Contains("permanent");
                return await _fileExplorerSkill.DeleteSelectedAsync(permanent);
            }
            else if (lower.Contains("search") || lower.Contains("find"))
            {
                var query = ExtractParameter(command, new[] { "for", "search", "find" }) ?? "";
                return await _fileExplorerSkill.SearchAsync(query);
            }
            else if (lower.Contains("go to") || lower.Contains("navigate") || lower.Contains("open"))
            {
                var path = ExtractParameter(command, new[] { "to", "open" }) ?? "";
                return await _fileExplorerSkill.NavigateToAsync(path);
            }
            else if (lower.Contains("copy"))
            {
                return await _fileExplorerSkill.CopySelectedAsync();
            }
            else if (lower.Contains("paste"))
            {
                return await _fileExplorerSkill.PasteAsync();
            }
            else if (lower.Contains("select all"))
            {
                return await _fileExplorerSkill.SelectAllAsync();
            }

            return new ActionResult { Success = false, Message = "Unknown File Explorer command" };
        }

        private async Task<ActionResult> ExecuteBrowserCommandAsync(string command, ActiveAppContext context)
        {
            var lower = command.ToLower();

            if (lower.Contains("new tab"))
            {
                return await _browserSkill.NewTabAsync();
            }
            else if (lower.Contains("close tab"))
            {
                return await _browserSkill.CloseTabAsync();
            }
            else if (lower.Contains("go to") || lower.Contains("navigate") || lower.Contains("open"))
            {
                var url = ExtractParameter(command, new[] { "to", "open" }) ?? "";
                return await _browserSkill.NavigateToAsync(url);
            }
            else if (lower.Contains("search"))
            {
                var query = ExtractParameter(command, new[] { "for", "search" }) ?? "";
                return await _browserSkill.SearchAsync(query);
            }
            else if (lower.Contains("copy url") || lower.Contains("copy link"))
            {
                return await _browserSkill.CopyUrlAsync();
            }
            else if (lower.Contains("refresh") || lower.Contains("reload"))
            {
                return await _browserSkill.RefreshAsync();
            }
            else if (lower.Contains("back"))
            {
                return await _browserSkill.GoBackAsync();
            }
            else if (lower.Contains("forward"))
            {
                return await _browserSkill.GoForwardAsync();
            }
            else if (lower.Contains("find"))
            {
                var text = ExtractParameter(command, new[] { "find" }) ?? "";
                return await _browserSkill.FindOnPageAsync(text);
            }
            else if (lower.Contains("next tab"))
            {
                return await _browserSkill.NextTabAsync();
            }
            else if (lower.Contains("previous tab"))
            {
                return await _browserSkill.PreviousTabAsync();
            }

            return new ActionResult { Success = false, Message = "Unknown browser command" };
        }

        private async Task<ActionResult> ExecuteIDECommandAsync(string command, ActiveAppContext context)
        {
            var lower = command.ToLower();

            if (lower.Contains("open file") || lower.Contains("open"))
            {
                var file = ExtractParameter(command, new[] { "file", "open" }) ?? "";
                return await _ideSkill.OpenFileAsync(file);
            }
            else if (lower.Contains("search") || lower.Contains("find in files"))
            {
                var query = ExtractParameter(command, new[] { "for", "search", "find" }) ?? "";
                return await _ideSkill.SearchInFilesAsync(query);
            }
            else if (lower.Contains("format"))
            {
                return await _ideSkill.FormatDocumentAsync();
            }
            else if (lower.Contains("go to line"))
            {
                var lineStr = ExtractParameter(command, new[] { "line" }) ?? "1";
                if (int.TryParse(lineStr, out var line))
                    return await _ideSkill.GoToLineAsync(line);
                return new ActionResult { Success = false, Message = "Invalid line number" };
            }
            else if (lower.Contains("comment"))
            {
                return await _ideSkill.ToggleCommentAsync();
            }
            else if (lower.Contains("save all"))
            {
                return await _ideSkill.SaveAllAsync();
            }
            else if (lower.Contains("save"))
            {
                return await _ideSkill.SaveFileAsync();
            }
            else if (lower.Contains("command palette") || lower.Contains("commands"))
            {
                return await _ideSkill.OpenCommandPaletteAsync();
            }
            else if (lower.Contains("terminal"))
            {
                return await _ideSkill.OpenTerminalAsync();
            }
            else if (lower.Contains("sidebar"))
            {
                return await _ideSkill.ToggleSidebarAsync();
            }
            else if (lower.Contains("replace"))
            {
                // Extract find and replace values
                var parts = command.Split(new[] { " with ", " to " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var find = ExtractParameter(parts[0], new[] { "replace" }) ?? "";
                    var replace = parts[1].Trim();
                    return await _ideSkill.FindAndReplaceAsync(find, replace);
                }
                return new ActionResult { Success = false, Message = "Use format: replace X with Y" };
            }

            return new ActionResult { Success = false, Message = "Unknown IDE command" };
        }

        private string? ExtractParameter(string command, string[] keywords)
        {
            var lower = command.ToLower();
            foreach (var keyword in keywords)
            {
                var idx = lower.IndexOf(keyword);
                if (idx >= 0)
                {
                    var start = idx + keyword.Length;
                    if (start < command.Length)
                    {
                        var param = command.Substring(start).Trim();
                        // Remove common trailing words
                        param = param.Split(new[] { " please", " now", " for me" }, StringSplitOptions.None)[0];
                        if (!string.IsNullOrWhiteSpace(param))
                            return param.Trim();
                    }
                }
            }
            return null;
        }

        private void OnActiveAppChanged(object? sender, ActiveAppContext context)
        {
            // Update context store if available
            if (_contextStore != null)
            {
                _contextStore.AddEntry(new ContextEntry
                {
                    UserInput = "",
                    ActiveFeature = "InAppAssistant",
                    ReferencedApps = new List<string> { context.ProcessName }
                });
            }

            StatusChanged?.Invoke(this, $"Active: {context.ProcessName}");
        }

        private void OnActionExecuted(object? sender, ActionResult result)
        {
            ActionCompleted?.Invoke(this, result);
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.Success)
                    _overlayWindow?.SetProcessingState();
            });
        }

        private void OnTextCaptured(object? sender, string text)
        {
            StatusChanged?.Invoke(this, $"Captured: {text.Length} characters");
        }

        private void OnActionsRequested(object? sender, EventArgs e)
        {
            var context = GetCurrentContext();
            var actions = GetAvailableActions(context);
            StatusChanged?.Invoke(this, $"Available actions for {context.ProcessName}: {actions}");
        }

        private string GetAvailableActions(ActiveAppContext context)
        {
            return context.Category switch
            {
                AppCategory.FileExplorer => "new folder, rename, delete, search, copy, paste",
                AppCategory.Browser => "new tab, close tab, navigate, search, copy url, refresh",
                AppCategory.IDE => "open file, search, format, save, terminal, comment",
                _ => "voice commands available"
            };
        }

        public void Dispose()
        {
            _contextService.StopPolling();
            _overlayWindow?.Close();
        }
    }
}
