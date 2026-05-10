using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AtlasAI.AI;
using AtlasAI.Coding;
using AtlasAI.Coding.Services;
using AtlasAI.Coding.Controls;

namespace AtlasAI {
    public partial class CodeEditorWindow : Window
    {
        // Session persistence
        private static readonly string SessionFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "code_editor_session.json");
        
        // Workspace & Files
        private string _workspacePath = "";
        private readonly Dictionary<string, EditorTab> _openTabs = new();
        private EditorTab? _activeTab;
        private bool _isInitializing = true;
        
        // Services
        private readonly CodeAssistantService _codeAssistant = new();
        private readonly CodeToolExecutor _toolExecutor;
        
        // Agent Mode v2 Services
        private readonly AgentContextBudgeter _contextBudgeter = AgentContextBudgeter.Instance;
        private readonly AgentVerificationPipeline _verificationPipeline = AgentVerificationPipeline.Instance;
        private readonly StageSnapshotStore _stageStore = StageSnapshotStore.Instance;
        private readonly ChatToCodeService _chatToCode = ChatToCodeService.Instance;
        
        // Terminal
        private bool _terminalVisible = false;
        private Process? _terminalProcess;

        public CodeEditorWindow()
        {
            InitializeComponent();
            _toolExecutor = new CodeToolExecutor(_codeAssistant);
            
            // Set initial state
            NoFileMessage.Visibility = Visibility.Visible;
            CodeEditor.Visibility = Visibility.Collapsed;
            LineNumbers.Visibility = Visibility.Collapsed;
            
            _isInitializing = false;
            UpdateStatusBar();
            
            // Load last session
            LoadSession();
            
            // Save session on close
            Closing += (s, e) => SaveSession();
            
            // Subscribe to stage history events
            _stageStore.OnStageUndone += (stage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Refresh the current file if it was affected by the undo
                    if (_activeTab != null && stage.FileSnapshots.Any(s => s.RelativePath.Equals(_activeTab.FilePath, StringComparison.OrdinalIgnoreCase) || s.FullPath.Equals(_activeTab.FilePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        RefreshActiveTab();
                    }
                    AddAIMessage("system", $"↩ Stage #{stage.Number} undone: {stage.Description} - Files restored to previous state.");
                });
            };
            
            _stageStore.OnStagesCleared += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddAIMessage("system", "🗑️ All stages cleared.");
                });
            };
            
            // Wire up DiffPreviewControl and ChatQuickActionsControl events
            DiffPreview.OnApplyRequested += DiffPreview_OnApplyRequested;
            DiffPreview.OnRejectRequested += DiffPreview_OnRejectRequested;
            DiffPreview.OnOpenFileRequested += DiffPreview_OnOpenFileRequested;
            DiffPreview.OnDiffCopied += (diff) => AddAIMessage("system", "📋 Diff copied to clipboard");
            
            ChatQuickActions.OnApplyClicked += ChatQuickActions_OnApplyClicked;
            ChatQuickActions.OnStageClicked += ChatQuickActions_OnStageClicked;
            ChatQuickActions.OnOpenFileClicked += DiffPreview_OnOpenFileRequested;
            ChatQuickActions.OnCopyDiffClicked += (diff) => AddAIMessage("system", "📋 Diff copied to clipboard");
            
            // Subscribe to ChatToCodeService events
            _chatToCode.OnChangeReady += ChatToCode_OnChangeReady;
            _chatToCode.OnChangeApplied += ChatToCode_OnChangeApplied;
            _chatToCode.OnError += (error) => AddAIMessage("error", error);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure window is visible on screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            
            // If window is off-screen, reset position
            if (Left < 0 || Left + Width > screenWidth || Top < 0 || Top + Height > screenHeight)
            {
                Left = (screenWidth - Width) / 2;
                Top = (screenHeight - Height) / 2;
            }
            
            // Ensure minimum size
            if (Width > screenWidth) Width = screenWidth - 100;
            if (Height > screenHeight) Height = screenHeight - 100;
        }

        #region Window Controls
        
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }
        
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        #endregion

        #region Agent Mode v2 Controls
        
        private void DebugCode_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab == null) return;
            
            ShowTerminal();
            AppendTerminalLine("🐛 Starting debug session...", "#b967ff");
            
            // For .NET projects, use dotnet run with debugger
            var ext = Path.GetExtension(_activeTab.FilePath).ToLower();
            if (ext == ".cs")
            {
                AppendTerminalLine("Attaching debugger to .NET process...", "#9ca3af");
                // In a real implementation, this would launch with debugger attached
            }
            
            StatusText.Text = "Debug session started";
        }
        
        private void ShowExplorer_Click(object sender, RoutedEventArgs e)
        {
            // Explorer is always visible in current layout
            StatusText.Text = "Explorer panel";
        }
        
        private void ShowSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Focus();
            StatusText.Text = "Search files (Ctrl+P)";
        }
        
        private void ToggleAIPanel_Click(object sender, RoutedEventArgs e)
        {
            if (AIPanelColumn.Width.Value > 0)
            {
                AIPanelColumn.Width = new GridLength(0);
            }
            else
            {
                AIPanelColumn.Width = new GridLength(380);
            }
        }
        
        private void ShowContextManifest_Click(object sender, RoutedEventArgs e)
        {
            var manifest = _contextBudgeter.GetManifest();
            var sb = new StringBuilder();
            sb.AppendLine("=== CONTEXT MANIFEST ===");
            sb.AppendLine($"Generated: {manifest.GeneratedAt:HH:mm:ss}");
            sb.AppendLine($"Total chars: {manifest.TotalChars:N0} / 25,000");
            sb.AppendLine();
            
            if (manifest.IncludedFiles.Count > 0)
            {
                sb.AppendLine("Included Files:");
                foreach (var file in manifest.IncludedFiles)
                {
                    var truncated = file.WasTruncated ? " [truncated]" : "";
                    sb.AppendLine($"  • {file.Path} ({file.CharCount:N0} chars){truncated}");
                }
            }
            
            if (manifest.IncludedSnippets.Count > 0)
            {
                sb.AppendLine("\nIncluded Snippets:");
                foreach (var snippet in manifest.IncludedSnippets)
                {
                    sb.AppendLine($"  • {snippet.FilePath}:{snippet.StartLine} (score: {snippet.Score:F2})");
                }
            }
            
            sb.AppendLine($"\nHas Problems: {manifest.HasProblems}");
            sb.AppendLine($"Has Terminal: {manifest.HasTerminalOutput}");
            
            AddAIMessage("system", sb.ToString());
            StatusText.Text = "Context manifest displayed";
        }
        
        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            ClearChat_Click(sender, e);
            _verificationPipeline.ResetRepairState();
            StatusText.Text = "New chat started";
        }
        
        private void AISettings_Click(object sender, RoutedEventArgs e)
        {
            // Show AI settings info
            var provider = AIManager.GetActiveProvider();
            var providerInstance = AIManager.GetActiveProviderInstance();
            var configured = providerInstance?.IsConfigured ?? false;
            
            AddAIMessage("system", $"AI Provider: {provider}\nConfigured: {configured}\n\nUse Settings window to configure API keys.");
            StatusText.Text = "AI settings";
        }
        
        private void VerificationToggle_Click(object sender, RoutedEventArgs e)
        {
            UpdateVerificationStatus();
        }
        
        private void UpdateVerificationStatus()
        {
            var buildEnabled = BuildToggle.IsChecked == true;
            var testEnabled = TestToggle.IsChecked == true;
            var lintEnabled = LintToggle.IsChecked == true;
            var autoRepair = AutoRepairToggle.IsChecked == true;
            
            var enabledCount = (buildEnabled ? 1 : 0) + (testEnabled ? 1 : 0) + (lintEnabled ? 1 : 0);
            
            if (enabledCount == 0)
            {
                VerificationIcon.Text = "○";
                VerificationText.Text = "No verification";
                VerificationStatus.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x6b, 0x72, 0x80));
                VerificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80));
                VerificationText.Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80));
            }
            else
            {
                VerificationIcon.Text = "✓";
                VerificationText.Text = $"{enabledCount} check{(enabledCount > 1 ? "s" : "")} enabled";
                VerificationStatus.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xff, 0x9f));
                VerificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x9f));
                VerificationText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x9f));
            }
        }
        
        private void UpdateContextBudget(int usedChars)
        {
            var maxChars = _contextBudgeter.MaxContextChars;
            var percentage = (double)usedChars / maxChars * 100;
            
            ContextBudgetBar.Value = percentage;
            ContextBudgetText.Text = $"{usedChars / 1000}k / {maxChars / 1000}k";
            
            // Change color based on usage
            if (percentage > 90)
            {
                ContextBudgetBar.Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)); // Red
            }
            else if (percentage > 70)
            {
                ContextBudgetBar.Foreground = new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b)); // Orange
            }
            else
            {
                ContextBudgetBar.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff)); // Cyan
            }
        }
        
        private async Task RunVerificationAsync()
        {
            if (string.IsNullOrEmpty(_workspacePath)) return;
            
            // Update status to running
            VerificationIcon.Text = "⟳";
            VerificationText.Text = "Verifying...";
            VerificationStatus.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xd4, 0xff));
            VerificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff));
            VerificationText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff));
            
            var profile = VerificationProfile.Instance;
            var runner = VerificationRunner.Instance;
            var autoRepair = AutoRepairService.Instance;
            
            // Reset repair state for new verification
            autoRepair.Reset();
            
            try
            {
                VerificationResult? lastResult = null;
                
                // Run build if enabled
                if (BuildToggle.IsChecked == true)
                {
                    AddAIMessage("system", "🔨 Running build...");
                    lastResult = await runner.RunCommandAsync(
                        profile.BuildCommand,
                        _workspacePath,
                        VerificationType.Build,
                        profile.TimeoutSeconds);
                    
                    if (!lastResult.Success)
                    {
                        await HandleVerificationFailureAsync(lastResult);
                        return;
                    }
                }
                
                // Run tests if enabled
                if (TestToggle.IsChecked == true)
                {
                    AddAIMessage("system", "🧪 Running tests...");
                    lastResult = await runner.RunCommandAsync(
                        profile.TestCommand,
                        _workspacePath,
                        VerificationType.Test,
                        profile.TimeoutSeconds);
                    
                    if (!lastResult.Success)
                    {
                        await HandleVerificationFailureAsync(lastResult);
                        return;
                    }
                }
                
                // Run lint if enabled
                if (LintToggle.IsChecked == true)
                {
                    AddAIMessage("system", "📝 Running lint...");
                    lastResult = await runner.RunCommandAsync(
                        profile.LintCommand,
                        _workspacePath,
                        VerificationType.Lint,
                        profile.TimeoutSeconds);
                    
                    if (!lastResult.Success)
                    {
                        await HandleVerificationFailureAsync(lastResult);
                        return;
                    }
                }
                
                // All passed
                VerificationIcon.Text = "✓";
                VerificationText.Text = lastResult?.Summary ?? "All checks passed";
                VerificationStatus.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xff, 0x9f));
                VerificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x9f));
                VerificationText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x9f));
                
                AddAIMessage("system", $"✅ {lastResult?.Summary ?? "All verifications passed"}");
            }
            catch (Exception ex)
            {
                VerificationIcon.Text = "!";
                VerificationText.Text = "Error";
                AddAIMessage("error", $"Verification error: {ex.Message}");
            }
        }
        
        private async Task HandleVerificationFailureAsync(VerificationResult result)
        {
            VerificationIcon.Text = "✗";
            VerificationText.Text = $"{result.Errors.Count} error(s)";
            VerificationStatus.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xef, 0x44, 0x44));
            VerificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
            VerificationText.Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
            
            // Show error summary
            var errorSummary = new StringBuilder();
            errorSummary.AppendLine($"❌ {result.Type} failed: {result.Errors.Count} error(s)");
            foreach (var error in result.Errors.Take(5))
            {
                errorSummary.AppendLine($"  • {error.FilePath}:{error.Line} - {error.Message}");
            }
            if (result.Errors.Count > 5)
            {
                errorSummary.AppendLine($"  ... and {result.Errors.Count - 5} more");
            }
            AddAIMessage("error", errorSummary.ToString());
            
            // Check if auto-repair is enabled
            if (AutoRepairToggle.IsChecked == true)
            {
                AddAIMessage("system", "🔧 Auto-repair enabled, attempting fix...");
                
                var attempt = await AutoRepairService.Instance.AttemptRepairAsync(result, _workspacePath);
                
                if (attempt != null && attempt.Changes.Count > 0)
                {
                    AddAIMessage("tool", $"Generated repair for {attempt.Changes.Count} file(s)");
                    
                    // Apply the repair
                    var repairResult = await AutoRepairService.Instance.ApplyRepairAsync(attempt, _workspacePath);
                    
                    if (repairResult.RerunResult?.Success == true)
                    {
                        VerificationIcon.Text = "✓";
                        VerificationText.Text = "Auto-repaired";
                        VerificationStatus.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xff, 0x9f));
                        VerificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x9f));
                        VerificationText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x9f));
                        
                        AddAIMessage("system", "✅ Auto-repair successful! Build now passes.");
                        
                        // Refresh any open files that were modified
                        foreach (var change in attempt.Changes)
                        {
                            var fullPath = Path.IsPathRooted(change.FilePath)
                                ? change.FilePath
                                : Path.Combine(_workspacePath, change.FilePath);
                            
                            if (_openTabs.TryGetValue(fullPath, out var tab))
                            {
                                try
                                {
                                    var newContent = File.ReadAllText(fullPath);
                                    tab.Content = newContent;
                                    if (_activeTab == tab)
                                    {
                                        _isInitializing = true;
                                        CodeEditor.Text = newContent;
                                        _isInitializing = false;
                                    }
                                    tab.IsDirty = false;
                                    UpdateTabTitle(tab);
                                }
                                catch { }
                            }
                        }
                    }
                    else
                    {
                        AddAIMessage("error", "⚠️ Auto-repair did not fix the issue. Manual intervention required.");
                    }
                }
                else
                {
                    AddAIMessage("error", "⚠️ Could not generate auto-repair. Manual fix required.");
                }
            }
        }
        
        #region Diff Preview and Quick Actions Handlers
        
        private void ChatToCode_OnChangeReady(PendingChange change)
        {
            Dispatcher.Invoke(() =>
            {
                // Show the diff actions panel
                DiffActionsPanel.Visibility = Visibility.Visible;
                
                // Display the change in the diff preview
                DiffPreview.ShowChange(change);
                
                // Update quick actions
                ChatQuickActions.ShowChangeReady(change);
                
                // Update selection context if we have an active tab
                if (_activeTab != null)
                {
                    var hasSelection = CodeEditor.SelectionLength > 0;
                    ChatQuickActions.UpdateSelectionContext(
                        _activeTab.FilePath,
                        CodeEditor.SelectionStart,
                        CodeEditor.SelectionStart + CodeEditor.SelectionLength,
                        hasSelection);
                }
                
                AddAIMessage("system", $"📝 Code change ready: {change.Description ?? "Apply changes"}");
            });
        }
        
        private void ChatToCode_OnChangeApplied(ApplyResult result)
        {
            Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    // Hide the diff panel after successful apply
                    HideDiffActionsPanel();
                    
                    // Refresh the editor if the applied file is open
                    if (!string.IsNullOrEmpty(result.FilePath))
                    {
                        var fullPath = Path.IsPathRooted(result.FilePath)
                            ? result.FilePath
                            : Path.Combine(_workspacePath, result.FilePath);
                        
                        if (_openTabs.TryGetValue(fullPath, out var tab))
                        {
                            if (!string.IsNullOrEmpty(result.NewContent))
                            {
                                tab.Content = result.NewContent;
                                if (_activeTab == tab)
                                {
                                    _isInitializing = true;
                                    CodeEditor.Text = result.NewContent;
                                    _isInitializing = false;
                                }
                                tab.IsDirty = false;
                                UpdateTabTitle(tab);
                            }
                        }
                    }
                    
                    var stageInfo = result.StageNumber.HasValue ? $" (Stage #{result.StageNumber})" : "";
                    AddAIMessage("system", $"✅ Changes applied to {Path.GetFileName(result.FilePath ?? "file")}{stageInfo}");
                    StatusText.Text = "Changes applied";
                }
                else
                {
                    AddAIMessage("error", $"Failed to apply changes: {result.ErrorMessage}");
                }
            });
        }
        
        private async void DiffPreview_OnApplyRequested(PendingChange change)
        {
            var result = await _chatToCode.ApplyChangeAsync(change.Id, new ApplyOptions
            {
                CreateStage = true,
                WriteToFile = true
            });
            
            if (!result.Success)
            {
                AddAIMessage("error", $"Apply failed: {result.ErrorMessage}");
            }
        }
        
        private void DiffPreview_OnRejectRequested(PendingChange change)
        {
            _chatToCode.RejectChange(change.Id);
            HideDiffActionsPanel();
            AddAIMessage("system", "❌ Change rejected");
        }
        
        private void DiffPreview_OnOpenFileRequested(string filePath)
        {
            var fullPath = Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(_workspacePath, filePath);
            
            if (File.Exists(fullPath))
            {
                OpenFileInTab(fullPath);
            }
            else
            {
                AddAIMessage("error", $"File not found: {filePath}");
            }
        }
        
        private async void ChatQuickActions_OnApplyClicked(PendingChange change)
        {
            await _chatToCode.ApplyChangeAsync(change.Id, new ApplyOptions
            {
                CreateStage = true,
                WriteToFile = true
            });
        }
        
        private async void ChatQuickActions_OnStageClicked(PendingChange change)
        {
            // Stage the change without immediately applying
            var stage = _stageStore.BeginStage($"Staged: {change.Description}");
            
            if (!string.IsNullOrEmpty(change.FilePath))
            {
                _stageStore.CaptureBeforeState(stage, change.FilePath);
            }
            
            var result = await _chatToCode.ApplyChangeAsync(change.Id, new ApplyOptions
            {
                CreateStage = false, // We already created the stage
                WriteToFile = true
            });
            
            if (result.Success)
            {
                if (!string.IsNullOrEmpty(change.FilePath))
                {
                    _stageStore.CaptureAfterState(stage, change.FilePath);
                }
                _stageStore.CompleteStage(stage);
                AddAIMessage("system", $"📦 Change staged as Stage #{stage.Number} - can be undone later");
            }
            else
            {
                _stageStore.FailStage(stage, result.ErrorMessage ?? "Stage failed");
            }
        }
        
        private void HideDiffActionsPanel()
        {
            DiffActionsPanel.Visibility = Visibility.Collapsed;
            DiffPreview.Clear();
            ChatQuickActions.Reset();
        }
        
        /// <summary>
        /// Process AI response for code changes and show diff preview.
        /// </summary>
        private void ProcessAIResponseForCodeChanges(string response)
        {
            if (!response.Contains("```")) return;
            
            // Build code context from current editor state
            var context = new CodeContext
            {
                FilePath = _activeTab?.FilePath,
                FullContent = _activeTab?.Content,
                HasSelection = CodeEditor.SelectionLength > 0,
                SelectionStart = CodeEditor.SelectionStart,
                SelectionEnd = CodeEditor.SelectionStart + CodeEditor.SelectionLength,
                SelectedText = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectedText : null,
                Language = _activeTab != null ? GetLanguageId(_activeTab.FilePath) : null,
                WorkspacePath = _workspacePath
            };
            
            // Parse the response for code changes
            var changes = _chatToCode.ParseAIResponse(response, context);
            
            if (changes.Count > 0)
            {
                // The OnChangeReady event will handle showing the diff preview
                ApplyBtn.Visibility = Visibility.Visible;
            }
        }
        
        #endregion
        
        #endregion

        #region Tab Management
        
        private class EditorTab
        {
            public string FilePath { get; set; } = "";
            public string FileName => Path.GetFileName(FilePath);
            public string Content { get; set; } = "";
            public bool IsDirty { get; set; }
            public Button? TabButton { get; set; }
        }

        private void OpenFileInTab(string filePath)
        {
            // Check if already open
            if (_openTabs.TryGetValue(filePath, out var existingTab))
            {
                SwitchToTab(existingTab);
                return;
            }

            try
            {
                var content = File.ReadAllText(filePath);
                var tab = new EditorTab
                {
                    FilePath = filePath,
                    Content = content,
                    IsDirty = false
                };

                // Create tab button
                var tabBtn = CreateTabButton(tab);
                tab.TabButton = tabBtn;
                TabBar.Children.Add(tabBtn);
                
                _openTabs[filePath] = tab;
                SwitchToTab(tab);
                
                StatusText.Text = $"Opened: {tab.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Button CreateTabButton(EditorTab tab)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var nameText = new TextBlock 
            { 
                Text = tab.FileName, 
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var closeBtn = new Button
            {
                Content = "×",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 144)),
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = Cursors.Hand,
                FontSize = 14
            };
            closeBtn.Click += (s, e) => { e.Handled = true; CloseTab(tab); };
            
            panel.Children.Add(nameText);
            panel.Children.Add(closeBtn);

            var btn = new Button
            {
                Content = panel,
                Style = (Style)FindResource("TabBtn"),
                Tag = tab
            };
            btn.Click += (s, e) => SwitchToTab(tab);
            
            return btn;
        }

        private void SwitchToTab(EditorTab tab)
        {
            // Save current tab content
            if (_activeTab != null)
            {
                _activeTab.Content = CodeEditor.Text;
            }

            _activeTab = tab;
            _isInitializing = true;
            CodeEditor.Text = tab.Content;
            _isInitializing = false;

            // Update tab visuals
            foreach (var child in TabBar.Children.OfType<Button>())
            {
                var isActive = child.Tag == tab;
                child.Background = isActive 
                    ? new SolidColorBrush(Color.FromRgb(30, 30, 58))
                    : new SolidColorBrush(Color.FromRgb(37, 37, 66));
                child.Foreground = isActive
                    ? new SolidColorBrush(Color.FromRgb(240, 240, 240))
                    : new SolidColorBrush(Color.FromRgb(160, 160, 176));
            }

            // Show editor, hide placeholder
            NoFileMessage.Visibility = Visibility.Collapsed;
            CodeEditor.Visibility = Visibility.Visible;
            LineNumbers.Visibility = Visibility.Visible;
            
            UpdateLineNumbers();
            UpdateStatusBar();
            DetectLanguage(tab.FilePath);
        }

        /// <summary>
        /// Refreshes the active tab by reloading its content from disk.
        /// Used after stage undo operations to reflect restored file state.
        /// </summary>
        private void RefreshActiveTab()
        {
            if (_activeTab == null) return;
            
            try
            {
                if (File.Exists(_activeTab.FilePath))
                {
                    _isInitializing = true;
                    _activeTab.Content = File.ReadAllText(_activeTab.FilePath);
                    CodeEditor.Text = _activeTab.Content;
                    _activeTab.IsDirty = false;
                    UpdateTabTitle(_activeTab);
                    _isInitializing = false;
                    
                    UpdateLineNumbers();
                    StatusText.Text = $"Refreshed: {_activeTab.FileName}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Atlas IDE] Error refreshing tab: {ex.Message}");
            }
        }

        private void CloseTab(EditorTab tab)
        {
            if (tab.IsDirty)
            {
                var result = MessageBox.Show($"Save changes to {tab.FileName}?", "Unsaved Changes",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes) SaveTab(tab);
            }

            TabBar.Children.Remove(tab.TabButton);
            _openTabs.Remove(tab.FilePath);

            if (_activeTab == tab)
            {
                _activeTab = _openTabs.Values.FirstOrDefault();
                if (_activeTab != null)
                    SwitchToTab(_activeTab);
                else
                {
                    CodeEditor.Text = "";
                    NoFileMessage.Visibility = Visibility.Visible;
                    CodeEditor.Visibility = Visibility.Collapsed;
                    LineNumbers.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void SaveTab(EditorTab tab)
        {
            try
            {
                File.WriteAllText(tab.FilePath, tab.Content);
                tab.IsDirty = false;
                UpdateTabTitle(tab);
                StatusText.Text = $"Saved: {tab.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateTabTitle(EditorTab tab)
        {
            if (tab.TabButton?.Content is StackPanel panel && panel.Children[0] is TextBlock text)
            {
                text.Text = tab.IsDirty ? $"{tab.FileName} •" : tab.FileName;
            }
        }

        #endregion

        #region File Tree

        private void PopulateFileTree(string rootPath)
        {
            FileTree.Items.Clear();
            
            var rootItem = new TreeViewItem
            {
                Header = CreateTreeHeader("📁", Path.GetFileName(rootPath)),
                Tag = rootPath,
                IsExpanded = true
            };
            
            PopulateTreeItem(rootItem, rootPath, 0);
            FileTree.Items.Add(rootItem);
            
            WorkspaceLabel.Text = $"EXPLORER: {Path.GetFileName(rootPath)}";
        }

        private void PopulateTreeItem(TreeViewItem parent, string path, int depth)
        {
            if (depth > 5) return; // Limit depth
            
            var ignoreFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "node_modules", "bin", "obj", ".git", ".vs", ".vscode", ".idea",
                "packages", "dist", "build", "__pycache__", ".next", "coverage", "Library"
            };

            try
            {
                // Add directories
                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => Path.GetFileName(d)))
                {
                    var name = Path.GetFileName(dir);
                    if (ignoreFolders.Contains(name)) continue;

                    var item = new TreeViewItem
                    {
                        Header = CreateTreeHeader("📁", name),
                        Tag = dir
                    };
                    
                    // Add placeholder for lazy loading
                    item.Items.Add(new TreeViewItem { Header = "Loading..." });
                    item.Expanded += TreeItem_Expanded;
                    
                    parent.Items.Add(item);
                }

                // Add files
                foreach (var file in Directory.GetFiles(path).OrderBy(f => Path.GetFileName(f)))
                {
                    var name = Path.GetFileName(file);
                    var icon = GetFileIcon(Path.GetExtension(file));
                    
                    var item = new TreeViewItem
                    {
                        Header = CreateTreeHeader(icon, name),
                        Tag = file
                    };
                    parent.Items.Add(item);
                }
            }
            catch { }
        }

        private void TreeItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Tag is string path && Directory.Exists(path))
            {
                // Remove placeholder and populate
                if (item.Items.Count == 1 && item.Items[0] is TreeViewItem placeholder && 
                    placeholder.Header?.ToString() == "Loading...")
                {
                    item.Items.Clear();
                    PopulateTreeItem(item, path, 0);
                }
            }
        }

        private StackPanel CreateTreeHeader(string icon, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = icon, Margin = new Thickness(0, 0, 6, 0) });
            panel.Children.Add(new TextBlock { Text = text });
            return panel;
        }

        private string GetFileIcon(string ext)
        {
            return ext.ToLower() switch
            {
                ".cs" => "🟣",
                ".js" or ".jsx" => "🟡",
                ".ts" or ".tsx" => "🔵",
                ".py" => "🐍",
                ".json" => "📋",
                ".html" or ".htm" => "🌐",
                ".css" or ".scss" => "🎨",
                ".md" => "📝",
                ".xaml" => "🎯",
                ".xml" or ".csproj" or ".sln" => "📦",
                ".png" or ".jpg" or ".gif" => "🖼️",
                _ => "📄"
            };
        }

        private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Just selection change, don't open yet
        }

        private void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileTree.SelectedItem is TreeViewItem item && item.Tag is string path && File.Exists(path))
            {
                OpenFileInTab(path);
            }
        }

        private void RefreshTree_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_workspacePath) && Directory.Exists(_workspacePath))
            {
                PopulateFileTree(_workspacePath);
                StatusText.Text = "File tree refreshed";
            }
        }

        #endregion

        #region Toolbar Actions

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select project folder",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SetWorkspace(dialog.SelectedPath);
            }
        }

        public void SetWorkspace(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;
            
            _workspacePath = folderPath;
            _codeAssistant.SetWorkspace(folderPath);
            PopulateFileTree(folderPath);
            
            // Initialize stage snapshot store for this workspace
            _stageStore.SetWorkspace(folderPath);
            
            // Auto-detect verification commands for this project
            VerificationProfile.Instance.AutoDetect(folderPath);
            
            // Update AI panel
            AddAIMessage("system", $"✅ Workspace set to: {Path.GetFileName(folderPath)}\n\nI now have full access to read, write, and modify files in this project. Ask me anything!");
            
            StatusText.Text = $"Workspace: {folderPath}";
        }

        private void NewFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Create New File",
                Filter = "C# Files (*.cs)|*.cs|JavaScript (*.js)|*.js|TypeScript (*.ts)|*.ts|Python (*.py)|*.py|All Files (*.*)|*.*",
                InitialDirectory = _workspacePath
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, "");
                OpenFileInTab(dialog.FileName);
                if (!string.IsNullOrEmpty(_workspacePath))
                    PopulateFileTree(_workspacePath);
            }
        }

        private void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab != null)
            {
                _activeTab.Content = CodeEditor.Text;
                SaveTab(_activeTab);
            }
            else if (!string.IsNullOrEmpty(CodeEditor.Text))
            {
                // No file open, prompt to save as new file
                SaveAs_Click(sender, e);
            }
            else
            {
                StatusText.Text = "No file to save";
            }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save File As",
                Filter = "C# Files (*.cs)|*.cs|JavaScript (*.js)|*.js|TypeScript (*.ts)|*.ts|Python (*.py)|*.py|JSON (*.json)|*.json|XML (*.xml)|*.xml|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                InitialDirectory = !string.IsNullOrEmpty(_workspacePath) ? _workspacePath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            // If we have an active tab, suggest the same filename
            if (_activeTab != null)
            {
                dialog.FileName = _activeTab.FileName;
                var ext = Path.GetExtension(_activeTab.FilePath);
                dialog.DefaultExt = ext;
            }

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var content = _activeTab?.Content ?? CodeEditor.Text;
                    File.WriteAllText(dialog.FileName, content);
                    
                    // If we had an active tab, close it and open the new file
                    if (_activeTab != null)
                    {
                        var oldTab = _activeTab;
                        oldTab.IsDirty = false; // Prevent save prompt
                        CloseTab(oldTab);
                    }
                    
                    // Open the newly saved file
                    OpenFileInTab(dialog.FileName);
                    
                    // Refresh file tree if in workspace
                    if (!string.IsNullOrEmpty(_workspacePath) && dialog.FileName.StartsWith(_workspacePath))
                    {
                        PopulateFileTree(_workspacePath);
                    }
                    
                    StatusText.Text = $"Saved as: {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveAll_Click(object sender, RoutedEventArgs e)
        {
            // Save current editor to active tab first
            if (_activeTab != null)
                _activeTab.Content = CodeEditor.Text;
                
            foreach (var tab in _openTabs.Values.Where(t => t.IsDirty))
            {
                SaveTab(tab);
            }
            StatusText.Text = "All files saved";
        }

        private async void RunCode_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab == null) return;
            
            ShowTerminal();
            
            var ext = Path.GetExtension(_activeTab.FilePath).ToLower();
            var cmd = ext switch
            {
                ".py" => $"python \"{_activeTab.FilePath}\"",
                ".js" => $"node \"{_activeTab.FilePath}\"",
                ".cs" => "dotnet run",
                ".ps1" => $"powershell -ExecutionPolicy Bypass -File \"{_activeTab.FilePath}\"",
                ".bat" => $"\"{_activeTab.FilePath}\"",
                _ => null
            };

            if (cmd == null)
            {
                AppendTerminalLine($"Cannot run {ext} files directly. Use the terminal.", "#f59e0b");
                return;
            }

            AppendTerminalLine($"Running {_activeTab.FileName}...", "#6366f1");
            AppendTerminalLine("", "#808090");
            
            try
            {
                var result = await _codeAssistant.RunCommandAsync(cmd);
                var lines = result.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("```") || trimmed.StartsWith("💻")) continue;
                    var color = GetOutputColor(trimmed);
                    AppendTerminalLine(trimmed.Replace("`", ""), color);
                }
            }
            catch (Exception ex)
            {
                AppendTerminalLine($"Error: {ex.Message}", "#ef4444");
            }
            
            AppendTerminalLine("", "#808090");
            ScrollTerminalToEnd();
        }

        #endregion

        #region Terminal - Kiro Style

        // Command history for up/down arrow navigation
        private readonly List<string> _commandHistory = new();
        private int _historyIndex = -1;
        private string _currentInput = "";

        private void ToggleTerminal_Click(object sender, RoutedEventArgs e)
        {
            if (_terminalVisible)
                HideTerminal();
            else
                ShowTerminal();
        }

        private void ShowTerminal()
        {
            _terminalVisible = true;
            TerminalRow.Height = new GridLength(220);
            TerminalPanel.Visibility = Visibility.Visible;
            TerminalSplitter.Visibility = Visibility.Visible;
            TerminalToggle.Content = "⌨ Terminal ✓";
            
            // Initialize terminal with welcome message
            if (TerminalRichOutput.Document.Blocks.Count == 0)
            {
                WriteTerminalWelcome();
            }
            
            UpdateTerminalPrompt();
            TerminalInput.Focus();
        }

        private void WriteTerminalWelcome()
        {
            var doc = TerminalRichOutput.Document;
            doc.Blocks.Clear();
            
            // Welcome header
            AppendTerminalLine("╭─────────────────────────────────────────────╮", "#404060");
            AppendTerminalLine("│  Atlas Terminal                             │", "#6366f1");
            AppendTerminalLine("│  Type 'help' for available commands         │", "#808090");
            AppendTerminalLine("╰─────────────────────────────────────────────╯", "#404060");
            AppendTerminalLine("", "#808090");
        }

        private void HideTerminal()
        {
            _terminalVisible = false;
            TerminalRow.Height = new GridLength(0);
            TerminalPanel.Visibility = Visibility.Collapsed;
            TerminalSplitter.Visibility = Visibility.Collapsed;
            TerminalToggle.Content = "⌨ Terminal";
        }

        private void CloseTerminal_Click(object sender, RoutedEventArgs e)
        {
            HideTerminal();
        }

        private void ClearTerminal_Click(object sender, RoutedEventArgs e)
        {
            TerminalRichOutput.Document.Blocks.Clear();
            WriteTerminalWelcome();
        }

        private void UpdateTerminalPrompt()
        {
            // Show shortened path in prompt
            if (!string.IsNullOrEmpty(_workspacePath))
            {
                var dirName = Path.GetFileName(_workspacePath);
                TerminalPromptDir.Text = dirName.Length > 20 ? dirName.Substring(0, 17) + "..." : dirName;
            }
            else
            {
                TerminalPromptDir.Text = "~";
            }
        }

        private async void TerminalInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    await ExecuteTerminalCommand();
                    break;
                    
                case Key.Up:
                    e.Handled = true;
                    NavigateHistory(-1);
                    break;
                    
                case Key.Down:
                    e.Handled = true;
                    NavigateHistory(1);
                    break;
                    
                case Key.Tab:
                    e.Handled = true;
                    AutoCompleteCommand();
                    break;
                    
                case Key.Escape:
                    e.Handled = true;
                    TerminalInput.Text = "";
                    _historyIndex = -1;
                    break;
                    
                case Key.L when Keyboard.Modifiers == ModifierKeys.Control:
                    e.Handled = true;
                    ClearTerminal_Click(sender, e);
                    break;
            }
        }

        private void NavigateHistory(int direction)
        {
            if (_commandHistory.Count == 0) return;
            
            // Save current input when starting to navigate
            if (_historyIndex == -1 && direction == -1)
            {
                _currentInput = TerminalInput.Text;
            }
            
            _historyIndex += direction;
            
            if (_historyIndex < 0)
            {
                _historyIndex = 0;
            }
            else if (_historyIndex >= _commandHistory.Count)
            {
                _historyIndex = -1;
                TerminalInput.Text = _currentInput;
                TerminalInput.CaretIndex = TerminalInput.Text.Length;
                return;
            }
            
            if (_historyIndex >= 0 && _historyIndex < _commandHistory.Count)
            {
                TerminalInput.Text = _commandHistory[_commandHistory.Count - 1 - _historyIndex];
                TerminalInput.CaretIndex = TerminalInput.Text.Length;
            }
        }

        private void AutoCompleteCommand()
        {
            var input = TerminalInput.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;
            
            // Common commands for auto-complete
            var commands = new[] { "cd", "cls", "clear", "dir", "ls", "help", "exit", "dotnet", "npm", "node", "python", "git" };
            var match = commands.FirstOrDefault(c => c.StartsWith(input, StringComparison.OrdinalIgnoreCase));
            
            if (match != null)
            {
                TerminalInput.Text = match + " ";
                TerminalInput.CaretIndex = TerminalInput.Text.Length;
                return;
            }
            
            // Try file/folder completion
            if (!string.IsNullOrEmpty(_workspacePath))
            {
                var parts = input.Split(' ');
                var lastPart = parts.Last();
                var searchPath = Path.Combine(_workspacePath, lastPart);
                var searchDir = Path.GetDirectoryName(searchPath) ?? _workspacePath;
                var searchPattern = Path.GetFileName(searchPath) + "*";
                
                try
                {
                    if (Directory.Exists(searchDir))
                    {
                        var matches = Directory.GetFileSystemEntries(searchDir, searchPattern).Take(1).ToList();
                        if (matches.Count == 1)
                        {
                            var relativePath = Path.GetRelativePath(_workspacePath, matches[0]);
                            parts[parts.Length - 1] = relativePath;
                            TerminalInput.Text = string.Join(" ", parts);
                            TerminalInput.CaretIndex = TerminalInput.Text.Length;
                        }
                    }
                }
                catch { }
            }
        }

        private async Task ExecuteTerminalCommand()
        {
            var cmd = TerminalInput.Text.Trim();
            TerminalInput.Text = "";
            _historyIndex = -1;
            
            if (string.IsNullOrEmpty(cmd)) return;
            
            // Add to history (avoid duplicates)
            if (_commandHistory.Count == 0 || _commandHistory.Last() != cmd)
            {
                _commandHistory.Add(cmd);
                if (_commandHistory.Count > 100) _commandHistory.RemoveAt(0);
            }
            
            // Show the command in output
            AppendTerminalCommand(cmd);
            
            // Handle built-in commands
            var cmdLower = cmd.ToLower();
            
            if (cmdLower == "help")
            {
                ShowTerminalHelp();
                return;
            }
            
            if (cmdLower == "cls" || cmdLower == "clear")
            {
                TerminalRichOutput.Document.Blocks.Clear();
                return;
            }
            
            if (cmdLower == "exit")
            {
                HideTerminal();
                return;
            }
            
            if (cmd.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
            {
                HandleCdCommand(cmd.Substring(3).Trim().Trim('"'));
                return;
            }
            
            // Execute external command
            AppendTerminalLine("", "#808090"); // Spacing
            
            try
            {
                var result = await _codeAssistant.RunCommandAsync(cmd);
                
                // Parse and colorize output
                var lines = result.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    
                    // Skip markdown formatting
                    if (trimmed.StartsWith("```") || trimmed.StartsWith("💻")) continue;
                    
                    // Colorize based on content
                    var color = GetOutputColor(trimmed);
                    AppendTerminalLine(trimmed.Replace("`", ""), color);
                }
            }
            catch (Exception ex)
            {
                AppendTerminalLine($"Error: {ex.Message}", "#ef4444");
            }
            
            AppendTerminalLine("", "#808090"); // Spacing after output
            ScrollTerminalToEnd();
        }

        private string GetOutputColor(string line)
        {
            var lower = line.ToLower();
            
            // Errors
            if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("exception") || 
                lower.Contains("fatal") || lower.StartsWith("err"))
                return "#ef4444"; // Red
            
            // Warnings
            if (lower.Contains("warning") || lower.Contains("warn"))
                return "#f59e0b"; // Orange
            
            // Success
            if (lower.Contains("success") || lower.Contains("completed") || lower.Contains("done") ||
                lower.Contains("passed") || lower.StartsWith("ok"))
                return "#10b981"; // Green
            
            // Info/paths
            if (lower.Contains("info") || lower.StartsWith("->") || lower.Contains("building"))
                return "#3b82f6"; // Blue
            
            // Exit codes
            if (lower.StartsWith("exit code"))
                return line.Contains("0") ? "#10b981" : "#ef4444";
            
            // Default
            return "#c0c0d0";
        }

        private void HandleCdCommand(string path)
        {
            var fullPath = Path.IsPathRooted(path) 
                ? path 
                : Path.Combine(_workspacePath ?? Environment.CurrentDirectory, path);
            
            if (path == "..")
            {
                fullPath = Path.GetDirectoryName(_workspacePath ?? Environment.CurrentDirectory) ?? _workspacePath;
            }
            
            if (Directory.Exists(fullPath))
            {
                _workspacePath = Path.GetFullPath(fullPath);
                _codeAssistant.SetWorkspace(_workspacePath);
                UpdateTerminalPrompt();
                AppendTerminalLine($"Changed to: {_workspacePath}", "#10b981");
            }
            else
            {
                AppendTerminalLine($"Directory not found: {path}", "#ef4444");
            }
        }

        private void ShowTerminalHelp()
        {
            AppendTerminalLine("", "#808090");
            AppendTerminalLine("Available Commands:", "#6366f1");
            AppendTerminalLine("  help          Show this help message", "#c0c0d0");
            AppendTerminalLine("  clear, cls    Clear the terminal", "#c0c0d0");
            AppendTerminalLine("  cd <path>     Change directory", "#c0c0d0");
            AppendTerminalLine("  exit          Close terminal", "#c0c0d0");
            AppendTerminalLine("", "#808090");
            AppendTerminalLine("Keyboard Shortcuts:", "#6366f1");
            AppendTerminalLine("  ↑/↓           Navigate command history", "#c0c0d0");
            AppendTerminalLine("  Tab           Auto-complete", "#c0c0d0");
            AppendTerminalLine("  Ctrl+L        Clear terminal", "#c0c0d0");
            AppendTerminalLine("  Esc           Clear input", "#c0c0d0");
            AppendTerminalLine("", "#808090");
            AppendTerminalLine("All other commands are executed in PowerShell.", "#808090");
            AppendTerminalLine("", "#808090");
        }

        private void AppendTerminalCommand(string cmd)
        {
            var para = new System.Windows.Documents.Paragraph();
            para.Margin = new Thickness(0, 4, 0, 0);
            
            // Prompt
            var promptDir = new System.Windows.Documents.Run(TerminalPromptDir.Text + " ");
            promptDir.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
            promptDir.FontWeight = FontWeights.SemiBold;
            para.Inlines.Add(promptDir);
            
            var promptArrow = new System.Windows.Documents.Run("❯ ");
            promptArrow.Foreground = new SolidColorBrush(Color.FromRgb(99, 102, 241)); // Purple
            promptArrow.FontWeight = FontWeights.Bold;
            para.Inlines.Add(promptArrow);
            
            // Command
            var cmdRun = new System.Windows.Documents.Run(cmd);
            cmdRun.Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // White
            para.Inlines.Add(cmdRun);
            
            TerminalRichOutput.Document.Blocks.Add(para);
        }

        private void AppendTerminalLine(string text, string hexColor)
        {
            var para = new System.Windows.Documents.Paragraph();
            para.Margin = new Thickness(0, 1, 0, 0);
            
            var run = new System.Windows.Documents.Run(text);
            run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            para.Inlines.Add(run);
            
            TerminalRichOutput.Document.Blocks.Add(para);
        }

        private void ScrollTerminalToEnd()
        {
            TerminalRichOutput.ScrollToEnd();
        }

        #endregion

        #region Search

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var query = SearchBox.Text.Trim();
                if (string.IsNullOrEmpty(query)) return;
                
                // Quick file search
                var files = FindFilesMatching(query);
                if (files.Any())
                {
                    // Open first match
                    OpenFileInTab(files.First());
                }
                else
                {
                    StatusText.Text = $"No files found matching: {query}";
                }
            }
        }

        private IEnumerable<string> FindFilesMatching(string pattern)
        {
            if (string.IsNullOrEmpty(_workspacePath)) yield break;
            
            var ignoreFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "node_modules", "bin", "obj", ".git", ".vs", "Library"
            };

            foreach (var file in Directory.EnumerateFiles(_workspacePath, "*.*", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(file) ?? "";
                if (ignoreFolders.Any(f => dir.Contains(Path.DirectorySeparatorChar + f)))
                    continue;
                    
                if (Path.GetFileName(file).Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }
        }

        #endregion

        #region AI Assistant - Kiro-Style Agentic Coding

        // Conversation history for context
        private readonly List<object> _conversationHistory = new();
        private System.Threading.CancellationTokenSource? _currentCts;
        private string? _attachedFileContent;
        private string? _attachedFileName;
        private string? _lastAIResponse;
        private bool _isProcessing = false;

        private void ClearChat_Click(object sender, RoutedEventArgs e)
        {
            AIChat.Children.Clear();
            _conversationHistory.Clear();
            AddAIWelcomeMessage();
            ApplyBtn.Visibility = Visibility.Collapsed;
        }

        private void AddAIWelcomeMessage()
        {
            AIChat.Children.Add(new TextBlock 
            { 
                Text = "🤖 Atlas Code Assistant", 
                Foreground = new SolidColorBrush(Color.FromRgb(99, 102, 241)), 
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8) 
            });
            
            AIChat.Children.Add(new TextBlock 
            { 
                Text = "I work like Kiro - I can read, write, and modify your code directly.", 
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 176)), 
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8) 
            });
            
            var items = new[] { 
                "• \"Add error handling to this function\"",
                "• \"Create a new UserService class\"", 
                "• \"Fix the bug on line 42\"",
                "• \"Refactor this to use async/await\"" 
            };
            foreach (var item in items)
            {
                AIChat.Children.Add(new TextBlock 
                { 
                    Text = item, 
                    Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 128)), 
                    FontSize = 11 
                });
            }
            
            AIChat.Children.Add(new TextBlock 
            { 
                Text = "\n📁 Drop a folder to enable full workspace access!", 
                Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)), 
                FontSize = 11, 
                TextWrapping = TextWrapping.Wrap 
            });
        }

        private void AttachFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Attach file to chat",
                Filter = "All Files (*.*)|*.*",
                InitialDirectory = _workspacePath
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var content = File.ReadAllText(dialog.FileName);
                    var fileName = Path.GetFileName(dialog.FileName);
                    
                    _attachedFileContent = content;
                    _attachedFileName = fileName;
                    
                    AddAIMessage("system", $"📎 Attached: {fileName}");
                    StatusText.Text = $"Attached: {fileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading file: {ex.Message}");
                }
            }
        }

        private void ApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab != null && !string.IsNullOrEmpty(_lastAIResponse))
            {
                var codeMatch = System.Text.RegularExpressions.Regex.Match(
                    _lastAIResponse, @"```[\w]*\n([\s\S]*?)```");
                    
                if (codeMatch.Success)
                {
                    var newCode = codeMatch.Groups[1].Value;
                    CodeEditor.Text = newCode;
                    _activeTab.Content = newCode;
                    _activeTab.IsDirty = true;
                    UpdateTabTitle(_activeTab);
                    AddAIMessage("system", "✅ Changes applied to editor");
                    StatusText.Text = "Changes applied - review and save";
                }
            }
            ApplyBtn.Visibility = Visibility.Collapsed;
        }

        private async void AIInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                await SendAIMessage();
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                var textBox = sender as TextBox;
                if (textBox != null)
                {
                    int caretIndex = textBox.CaretIndex;
                    textBox.Text = textBox.Text.Insert(caretIndex, Environment.NewLine);
                    textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                    e.Handled = true;
                }
            }
        }

        private async void SendAI_Click(object sender, RoutedEventArgs e)
        {
            await SendAIMessage();
        }

        private System.Threading.CancellationTokenSource? _micNoteCts;
        private void CodeMicBtn_Click(object sender, RoutedEventArgs e)
        {
            // IsMicWired("Code") = false — mic is not wired.
            // Shows a tiny inline note only. Never auto-runs, auto-builds, or auto-edits files.
            ShowCodeMicNote("Mic not wired");
        }

        private async void ShowCodeMicNote(string note)
        {
            try
            {
                _micNoteCts?.Cancel();
                _micNoteCts = new System.Threading.CancellationTokenSource();
                var token = _micNoteCts.Token;
                Dispatcher.Invoke(() =>
                {
                    CodeMicNote.Text = note;
                    CodeMicNote.Visibility = System.Windows.Visibility.Visible;
                });
                await Task.Delay(2400, token);
                Dispatcher.Invoke(() =>
                {
                    CodeMicNote.Text = "";
                    CodeMicNote.Visibility = System.Windows.Visibility.Collapsed;
                });
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private async Task SendAIMessage()
        {
            var prompt = AIInput.Text.Trim();
            if (string.IsNullOrEmpty(prompt) || _isProcessing) return;

            _isProcessing = true;
            AIInput.Text = "";
            AIInput.IsEnabled = false;

            AddAIMessage("user", prompt);

            try
            {
                // Build context for this message
                var contextBuilder = new StringBuilder();
                
                // Add workspace context on first message or when workspace changes
                if (_conversationHistory.Count == 0 && !string.IsNullOrEmpty(_workspacePath))
                {
                    contextBuilder.AppendLine($"[Workspace: {_workspacePath}]");
                    contextBuilder.AppendLine($"[Project structure:\n{_codeAssistant.GetProjectStructure(2)}]");
                }
                
                // Add current file context
                if (_activeTab != null)
                {
                    contextBuilder.AppendLine($"\n[Current file: {_activeTab.FilePath}]");
                    contextBuilder.AppendLine($"```{GetLanguageId(_activeTab.FilePath)}");
                    var content = _activeTab.Content;
                    if (content.Length > 8000)
                        content = content.Substring(0, 8000) + "\n... [truncated]";
                    contextBuilder.AppendLine(content);
                    contextBuilder.AppendLine("```");
                }
                
                // Add attached file
                if (!string.IsNullOrEmpty(_attachedFileContent))
                {
                    contextBuilder.AppendLine($"\n[Attached file: {_attachedFileName}]");
                    contextBuilder.AppendLine("```");
                    contextBuilder.AppendLine(_attachedFileContent.Length > 5000 
                        ? _attachedFileContent.Substring(0, 5000) + "\n... [truncated]" 
                        : _attachedFileContent);
                    contextBuilder.AppendLine("```");
                    _attachedFileContent = null;
                    _attachedFileName = null;
                }

                var userMessage = contextBuilder.Length > 0 
                    ? $"{contextBuilder}\n\nUser: {prompt}"
                    : prompt;

                // Add to conversation history
                _conversationHistory.Add(new { role = "user", content = userMessage });

                // Build messages with system prompt + history
                var messages = new List<object>
                {
                    new { role = "system", content = GetAgenticSystemPrompt() }
                };
                
                // Add conversation history (last 10 messages for context)
                var historyToInclude = _conversationHistory.Count > 10 
                    ? _conversationHistory.Skip(_conversationHistory.Count - 10).ToList()
                    : _conversationHistory;
                messages.AddRange(historyToInclude);

                AddAIMessage("thinking", "Working on it...");

                _currentCts = new System.Threading.CancellationTokenSource();

                // Call AI
                var provider = AIManager.GetActiveProviderInstance();
                var providerType = AIManager.GetActiveProvider();
                System.Diagnostics.Debug.WriteLine($"[CodeEditor] Provider: {providerType}, Configured: {provider?.IsConfigured}");

                // Use linked token with timeout
                using var timeoutCts = new System.Threading.CancellationTokenSource(120000);
                using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_currentCts.Token, timeoutCts.Token);
                var ct = linkedCts.Token;

                var response = await AIManager.SendMessageAsync(messages, 4096, ct);

                // Remove thinking message
                RemoveThinkingMessage();

                if (response == null || !response.Success)
                {
                    // Fallback removed to respect user preference (no OpenAI credits)
                    /*
                    // Try fallback provider
                    if (providerType == AIProviderType.Claude)
                    {
                        var openAiProvider = AIManager.GetProvider(AIProviderType.OpenAI);
                        if (openAiProvider?.IsConfigured == true)
                        {
                            AddAIMessage("system", "Trying OpenAI...");
                            await AIManager.SetActiveProviderAsync(AIProviderType.OpenAI);
                            response = await AIManager.SendMessageAsync(messages, 4096, ct);
                            await AIManager.SetActiveProviderAsync(AIProviderType.Claude);
                        }
                    }
                    */
                    
                    if (response == null || !response.Success)
                    {
                        var errorMsg = response?.Error ?? "No response. Check API keys in Settings.";
                        AddAIMessage("error", errorMsg);
                        return;
                    }
                }

                var responseText = response.Content ?? "";
                if (string.IsNullOrEmpty(responseText))
                {
                    AddAIMessage("error", "Empty response from AI.");
                    return;
                }

                // Add to conversation history
                _conversationHistory.Add(new { role = "assistant", content = responseText });

                // Execute any tool calls in the response
                await ExecuteToolsAndRespond(responseText, messages, _currentCts.Token);
            }
            catch (OperationCanceledException)
            {
                RemoveThinkingMessage();
                AddAIMessage("assistant", "CANCELLED · OPERATION STOPPED");
            }
            catch (Exception ex)
            {
                RemoveThinkingMessage();
                AddAIMessage("error", $"Error: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                AIInput.IsEnabled = true;
                AIInput.Focus();
                
                if (_currentCts != null)
                {
                    _currentCts.Dispose();
                    _currentCts = null;
                }
            }
        }

        private async Task ExecuteToolsAndRespond(string responseText, List<object> messages, System.Threading.CancellationToken ct = default)
        {
            // Check for tool calls and execute them
            var (hasTools, toolResult) = await _toolExecutor.TryExecuteToolAsync(responseText);
            
            if (hasTools && !string.IsNullOrEmpty(toolResult))
            {
                // Show tool execution
                AddAIMessage("tool", toolResult);
                
                // If it was a write operation, refresh the file tree and editor
                if (toolResult.Contains("✅ Created:") || toolResult.Contains("✅ Updated:") || toolResult.Contains("✅ Replaced"))
                {
                    // Refresh file tree
                    if (!string.IsNullOrEmpty(_workspacePath))
                        PopulateFileTree(_workspacePath);
                    
                    // If the modified file is open, reload it
                    var pathMatch = System.Text.RegularExpressions.Regex.Match(toolResult, @"(?:Created|Updated|Replaced)[^:]*:\s*(.+)");
                    if (pathMatch.Success)
                    {
                        var modifiedPath = pathMatch.Groups[1].Value.Trim();
                        var fullPath = Path.IsPathRooted(modifiedPath) 
                            ? modifiedPath 
                            : Path.Combine(_workspacePath ?? "", modifiedPath);
                        
                        if (_openTabs.TryGetValue(fullPath, out var tab))
                        {
                            // Reload the file content
                            try
                            {
                                var newContent = File.ReadAllText(fullPath);
                                tab.Content = newContent;
                                if (_activeTab == tab)
                                {
                                    _isInitializing = true;
                                    CodeEditor.Text = newContent;
                                    _isInitializing = false;
                                }
                                tab.IsDirty = false;
                                UpdateTabTitle(tab);
                            }
                            catch { }
                        }
                    }
                    
                    StatusText.Text = "File updated";
                }
                
                // Continue the conversation with tool result
                _conversationHistory.Add(new { role = "user", content = $"Tool result:\n{toolResult}\n\nContinue with the task if needed, or summarize what was done." });
                messages.Add(new { role = "assistant", content = responseText });
                messages.Add(new { role = "user", content = $"Tool result:\n{toolResult}\n\nContinue with the task if needed, or summarize what was done." });
                
                AddAIMessage("thinking", "Continuing...");
                
                var followUp = await AIManager.SendMessageAsync(messages, 4096, ct);
                RemoveThinkingMessage();
                
                if (followUp?.Success == true && !string.IsNullOrEmpty(followUp.Content))
                {
                    _conversationHistory.Add(new { role = "assistant", content = followUp.Content });
                    
                    // Check for more tool calls
                    var (moreTools, moreResult) = await _toolExecutor.TryExecuteToolAsync(followUp.Content, ct);
                    if (moreTools && !string.IsNullOrEmpty(moreResult))
                    {
                        AddAIMessage("tool", moreResult);
                        // Show summary without the tool call
                        var summary = System.Text.RegularExpressions.Regex.Replace(followUp.Content, @"\[TOOL:[^\]]+\]", "").Trim();
                        if (!string.IsNullOrEmpty(summary))
                            AddAIMessage("assistant", summary);
                    }
                    else
                    {
                        AddAIMessage("assistant", followUp.Content);
                    }
                    _lastAIResponse = followUp.Content;
                }
            }
            else
            {
                // No tools, just show the response
                AddAIMessage("assistant", responseText);
                _lastAIResponse = responseText;
            }
            
            // Process response for code changes and show diff preview
            ProcessAIResponseForCodeChanges(_lastAIResponse ?? responseText);
            
            // Show apply button if response contains code
            if (_lastAIResponse?.Contains("```") == true)
            {
                ApplyBtn.Visibility = Visibility.Visible;
            }
        }

        private void RemoveThinkingMessage()
        {
            Dispatcher.Invoke(() =>
            {
                for (int i = AIChat.Children.Count - 1; i >= 0; i--)
                {
                    if (AIChat.Children[i] is TextBox tb && (tb.Text.StartsWith("💭") || tb.Text.Contains("Working") || tb.Text.Contains("Continuing")))
                    {
                        AIChat.Children.RemoveAt(i);
                        break;
                    }
                }
            });
        }

        private string GetAgenticSystemPrompt()
        {
            return @"You are Atlas, an AI coding assistant that EXECUTES code changes directly - not just suggests them.

## YOUR CAPABILITIES
You can read, write, and modify files in the user's workspace using tools.

## AVAILABLE TOOLS (use this exact format)
- Read file: [TOOL:read_file path=""relative/path/file.cs""]
- Write/create file: [TOOL:write_file path=""relative/path/file.cs"" content=""full file content here""]
- Replace text: [TOOL:replace path=""file.cs"" old=""exact old text"" new=""new text""]
- Search code: [TOOL:search pattern=""searchTerm""]
- Run command: [TOOL:run command=""dotnet build""]
- Delete file: [TOOL:delete path=""file.cs""]

## CRITICAL RULES
1. **EXECUTE, DON'T JUST SUGGEST** - When user asks you to create/modify code, USE THE TOOLS to do it
2. **REMEMBER CONTEXT** - If user says ""do number 1"" or ""the first one"", refer to your previous message
3. **WRITE COMPLETE FILES** - When creating files, write the ENTIRE file content, not snippets
4. **USE REPLACE FOR EDITS** - For small changes, use [TOOL:replace] with exact text matching
5. **CONFIRM ACTIONS** - After executing, briefly confirm what you did

## EXAMPLE INTERACTIONS

User: ""Create a UserService class""
You: I'll create that for you.
[TOOL:write_file path=""Services/UserService.cs"" content=""using System;

namespace MyApp.Services
{
    public class UserService
    {
        public async Task<User> GetUserAsync(int id)
        {
            // Implementation
        }
    }
}""]

User: ""Add error handling to the GetUser method""
You: Adding try-catch error handling.
[TOOL:replace path=""Services/UserService.cs"" old=""public async Task<User> GetUserAsync(int id)
        {
            // Implementation
        }"" new=""public async Task<User> GetUserAsync(int id)
        {
            try
            {
                // Implementation
            }
            catch (Exception ex)
            {
                throw new ServiceException($""Failed to get user {id}"", ex);
            }
        }""]

## CONVERSATION MEMORY
You remember the entire conversation. If user references something from earlier (""do that"", ""number 1"", ""the first option""), look at your previous messages to understand what they mean.

Be concise. Execute actions. Confirm results.";
        }

        private void AddAIMessage(string role, string content)
        {
            var (color, prefix) = role switch
            {
                "user" => (Color.FromRgb(99, 102, 241), "You: "),
                "assistant" => (Color.FromRgb(16, 185, 129), "Atlas: "),
                "system" => (Color.FromRgb(0, 212, 255), "ℹ️ "),
                "tool" => (Color.FromRgb(245, 158, 11), "🔧 "),
                "thinking" => (Color.FromRgb(156, 163, 175), "💭 "),
                "error" => (Color.FromRgb(239, 68, 68), "❌ "),
                _ => (Color.FromRgb(224, 224, 224), "")
            };

            var textBox = new TextBox
            {
                Text = prefix + content,
                Foreground = new SolidColorBrush(color),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Margin = new Thickness(0, 10, 0, 0),
                Cursor = Cursors.IBeam
            };
            
            AIChat.Children.Add(textBox);
            
            // Auto-scroll
            if (AIChat.Parent is ScrollViewer sv)
                sv.ScrollToEnd();
        }

        #endregion

        #region Editor Events

        private void CodeEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateLineNumbers();
            
            if (!_isInitializing && _activeTab != null)
            {
                _activeTab.IsDirty = true;
                _activeTab.Content = CodeEditor.Text;
                UpdateTabTitle(_activeTab);
            }
        }

        private void CodeEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Tab for indentation
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                var caretIndex = CodeEditor.CaretIndex;
                CodeEditor.Text = CodeEditor.Text.Insert(caretIndex, "    ");
                CodeEditor.CaretIndex = caretIndex + 4;
            }
            // Ctrl+S to save
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveFile_Click(sender, e);
                e.Handled = true;
            }
            // Ctrl+P for quick open
            else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                e.Handled = true;
            }
            // Ctrl+` for terminal
            else if (e.Key == Key.Oem3 && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleTerminal_Click(sender, e);
                e.Handled = true;
            }
        }

        private void UpdateLineNumbers()
        {
            var lines = CodeEditor.Text.Split('\n').Length;
            var sb = new StringBuilder();
            for (int i = 1; i <= lines; i++)
            {
                sb.AppendLine(i.ToString());
            }
            LineNumbers.Text = sb.ToString().TrimEnd();
        }

        private void UpdateStatusBar()
        {
            if (_activeTab != null)
            {
                var lines = _activeTab.Content.Split('\n');
                var lineIndex = CodeEditor.GetLineIndexFromCharacterIndex(CodeEditor.CaretIndex);
                var colIndex = CodeEditor.CaretIndex - CodeEditor.GetCharacterIndexFromLineIndex(Math.Max(0, lineIndex));
                CursorPosition.Text = $"Ln {lineIndex + 1}, Col {colIndex + 1}";
            }
            else
            {
                CursorPosition.Text = "Ln 1, Col 1";
            }
        }

        private void DetectLanguage(string filePath)
        {
            var lang = GetLanguageId(filePath);
            LanguageLabel.Text = lang.ToUpper();
        }

        private string GetLanguageId(string filePath)
        {
            return Path.GetExtension(filePath).ToLower() switch
            {
                ".cs" => "C#",
                ".js" or ".jsx" => "JavaScript",
                ".ts" or ".tsx" => "TypeScript",
                ".py" => "Python",
                ".json" => "JSON",
                ".xml" or ".csproj" => "XML",
                ".html" or ".htm" => "HTML",
                ".css" or ".scss" => "CSS",
                ".yaml" or ".yml" => "YAML",
                ".md" => "Markdown",
                ".sql" => "SQL",
                ".xaml" => "XAML",
                ".ps1" => "PowerShell",
                ".bat" or ".cmd" => "Batch",
                _ => "Text"
            };
        }

        #endregion

        #region Drag & Drop

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (paths.Length > 0)
                {
                    var path = paths[0];
                    
                    if (Directory.Exists(path))
                    {
                        // It's a folder - set as workspace
                        SetWorkspace(path);
                    }
                    else if (File.Exists(path))
                    {
                        // It's a file - open it
                        OpenFileInTab(path);
                        
                        // If no workspace, set parent folder
                        if (string.IsNullOrEmpty(_workspacePath))
                        {
                            var parentDir = Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(parentDir))
                                SetWorkspace(parentDir);
                        }
                    }
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Open a specific file directly (for external calls)
        /// </summary>
        public void OpenFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                OpenFileInTab(filePath);
                
                // Set workspace if not set
                if (string.IsNullOrEmpty(_workspacePath))
                {
                    var parentDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(parentDir))
                        SetWorkspace(parentDir);
                }
            }
        }

        #endregion

        #region Session Persistence

        private class EditorSession
        {
            public string? WorkspacePath { get; set; }
            public List<string> OpenFiles { get; set; } = new();
            public string? ActiveFile { get; set; }
            public double WindowLeft { get; set; }
            public double WindowTop { get; set; }
            public double WindowWidth { get; set; }
            public double WindowHeight { get; set; }
            public bool TerminalVisible { get; set; }
        }

        private void SaveSession()
        {
            try
            {
                // Save current tab content first
                if (_activeTab != null)
                    _activeTab.Content = CodeEditor.Text;

                var session = new EditorSession
                {
                    WorkspacePath = _workspacePath,
                    OpenFiles = _openTabs.Keys.ToList(),
                    ActiveFile = _activeTab?.FilePath,
                    WindowLeft = Left,
                    WindowTop = Top,
                    WindowWidth = Width,
                    WindowHeight = Height,
                    TerminalVisible = _terminalVisible
                };

                var dir = Path.GetDirectoryName(SessionFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SessionFile, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CodeEditor] Error saving session: {ex.Message}");
            }
        }

        private void LoadSession()
        {
            try
            {
                if (!File.Exists(SessionFile)) return;

                var json = File.ReadAllText(SessionFile);
                var session = JsonSerializer.Deserialize<EditorSession>(json);
                if (session == null) return;

                // Restore window position/size
                if (session.WindowWidth > 100 && session.WindowHeight > 100)
                {
                    Width = session.WindowWidth;
                    Height = session.WindowHeight;
                }
                if (session.WindowLeft >= 0 && session.WindowTop >= 0)
                {
                    Left = session.WindowLeft;
                    Top = session.WindowTop;
                }

                // Restore workspace
                if (!string.IsNullOrEmpty(session.WorkspacePath) && Directory.Exists(session.WorkspacePath))
                {
                    SetWorkspace(session.WorkspacePath);
                }

                // Restore open files
                foreach (var filePath in session.OpenFiles)
                {
                    if (File.Exists(filePath))
                    {
                        OpenFileInTab(filePath);
                    }
                }

                // Restore active file
                if (!string.IsNullOrEmpty(session.ActiveFile) && _openTabs.TryGetValue(session.ActiveFile, out var tab))
                {
                    SwitchToTab(tab);
                }

                // Restore terminal state
                if (session.TerminalVisible)
                {
                    ShowTerminal();
                }

                StatusText.Text = "Session restored";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CodeEditor] Error loading session: {ex.Message}");
            }
        }

        #endregion
    }
}
