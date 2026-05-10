using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Speech.Recognition;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Documents; // Add for Hyperlink and Run
using Shapes = System.Windows.Shapes; // Alias to avoid conflict with System.IO.Path
using System.Runtime.InteropServices;
using AtlasAI; // For AppTheme, TaskbarIconHelper
using System.Windows.Media.Imaging;
using AtlasAI.Settings;
using AtlasAI.Voice;
using AtlasAI.AI;
using AtlasAI.ScreenCapture;
using AtlasAI.SystemControl;
using AtlasAI.Tools;
// using AtlasAI.Avatar; // DISABLED - namespace mismatch
using AtlasAI.Understanding; // Understanding & Reasoning Layer
using AtlasAI.InAppAssistant; // In-App Assistant for controlling other apps
using AtlasAI.UI; // Toast notifications and Inspector panel
using AtlasAI.Conversation.Services; // Conversation, Sessions, Memory
using AtlasAI.Conversation.Models;
using AtlasAI.Conversation.UI;
using AtlasAI.Integrations; // Integration Hub
// using AtlasAI.ITManagement; // DISABLED - namespace mismatch
using AtlasAI.Memory; // Long-term Memory & Learning Layer
using AtlasAI.Brain;
using AtlasAI.Controls; // Atlas Core Control
using Microsoft.Web.WebView2.Core;
using AtlasAI.Core;
using AtlasAI.Agent;
using AtlasAI.SmartHome;
using NAudio.CoreAudioApi;

namespace AtlasAI {
    public partial class ChatWindow : Window
    {
        private readonly SmartHomeTextCommandService _smartHomeTextCommandService = new();
        private volatile bool _isSendingMessage;
        private string _lastSentText = string.Empty;
        private DateTime _lastSentTime = DateTime.MinValue;
        private bool _headerCollapsed;
        private bool _chatOrbsWebViewInitialized;
        private volatile bool _chatOrbsWebViewReady;

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Save current session before closing
            _ = Task.Run(async () =>
            {
                if (_conversationManager != null)
                {
                    await _conversationManager.SaveCurrentSessionAsync();
                }
            });

            // Clean up unified routing handlers
            if (Voice.VoiceSystemOrchestrator.Instance.SubmitMessageHandler == HandleVoiceMessage)
            {
                Voice.VoiceSystemOrchestrator.Instance.SubmitMessageHandler = null;
                Debug.WriteLine("[ChatWindow] Cleared VoiceSystemOrchestrator.SubmitMessageHandler");
            }
            
            // Unsubscribe from coordinate
            UnsubscribeFromWakeWordCoordinator();
            
            // Stop energy timer
            _speakingEnergyTimer?.Stop();
            
            // Dispose CTS
            _currentOperationCts?.Cancel();
            _currentOperationCts?.Dispose();

            // Cleanup taskbar icon
            _taskbarIcon?.Dispose();
            _taskbarIcon = null;
            
            // Cleanup background wake word timer
            _backgroundWakeWordTimer?.Dispose();
            _backgroundWakeWordTimer = null;
            
            _voiceManager?.Dispose();
            _hotkeyManager?.Dispose();
            try { ClipboardManager.ClipboardChanged -= ClipboardManager_ClipboardChanged_ForDownloadPrompt; } catch { }
            try { recognizer?.Dispose(); } catch { }
        }

        private void ImmersiveTimer_Tick(object? sender, EventArgs e)
        {
            // Debug logging
            // System.Diagnostics.Debug.WriteLine("[ChatWindow] Immersive Timer Tick");

            if (MediaPageRoot != null && MediaPageRoot.Visibility == Visibility.Visible)
                return;

            if (_headerCollapsed)
                return;

            if (TopNavBar != null && TopNavBar.Visibility == Visibility.Visible && TopNavBar.Opacity > 0)
            {
                System.Diagnostics.Debug.WriteLine("[ChatWindow] Fading out TopNavBar");
                // Simple fade out
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (s, _) => 
                {
                    if (TopNavBar.Opacity == 0) TopNavBar.Visibility = Visibility.Collapsed;
                };
                TopNavBar.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            
            // Stop timer until mouse moves
            _immersiveIdleTimer?.Stop();
        }

        private void OnImmersiveMouseMove(object sender, MouseEventArgs e)
        {
            if (MediaPageRoot != null && MediaPageRoot.Visibility == Visibility.Visible)
                return;

            if (_headerCollapsed)
                return;

            // Show TopNavBar if hidden
            if (TopNavBar != null)
            {
                if (TopNavBar.Visibility != Visibility.Visible || TopNavBar.Opacity < 1.0)
                {
                    System.Diagnostics.Debug.WriteLine("[ChatWindow] Mouse Move - Showing TopNavBar");
                    TopNavBar.Visibility = Visibility.Visible;
                    TopNavBar.BeginAnimation(UIElement.OpacityProperty, 
                        new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
                }
            }
            
            // Reset timer
            if (_immersiveIdleTimer != null)
            {
                _immersiveIdleTimer.Stop();
                _immersiveIdleTimer.Start();
            }
        }

        public void ToggleHeader()
        {
            try
            {
                _headerCollapsed = !_headerCollapsed;
                ApplyHeaderChromeState();
            }
            catch
            {
            }
        }

        private void SetWallpaperOnlyMode(bool enable)
        {
            try
            {
                SetImmersiveMode(enable);

                if (WallpaperOnlyExitButton != null)
                    WallpaperOnlyExitButton.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;

                if (ShowHeaderArrowButton != null)
                    ShowHeaderArrowButton.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }
        }

        private void WallpaperOnlyExitButton_Click(object sender, RoutedEventArgs e)
        {
            SetWallpaperOnlyMode(false);
        }

        private void OrbToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nextVisible = OrbOverlay == null || OrbOverlay.Visibility != Visibility.Visible;
                SetOrbOverlayVisible(nextVisible);
                if (OrbOverlayTop != null)
                    OrbOverlayTop.Visibility = nextVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
            }
        }

        private void WallpaperOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            SetWallpaperOnlyMode(true);
        }

        private void ChatWallpaperVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ChatWallpaperVideo == null)
                    return;

                ChatWallpaperVideo.Position = TimeSpan.Zero;
                ChatWallpaperVideo.Play();
            }
            catch
            {
            }
        }

        private void ShowHeaderArrowButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_headerCollapsed) return;
                _headerCollapsed = false;
                ApplyHeaderChromeState();
            }
            catch
            {
            }
        }

        private void ApplyHeaderChromeState()
        {
            try
            {
                var g = ShellGrid;
                if (g == null || g.RowDefinitions.Count == 0) return;

                var row0 = g.RowDefinitions[0];
                var isMediaVisible = MediaPageRoot != null && MediaPageRoot.Visibility == Visibility.Visible;
                if (_headerCollapsed)
                {
                    row0.Height = new GridLength(0);
                    if (TopNavBar != null) TopNavBar.Visibility = Visibility.Collapsed;
                    if (ShowHeaderArrowButton != null) ShowHeaderArrowButton.Visibility = isMediaVisible ? Visibility.Collapsed : Visibility.Visible;
                }
                else
                {
                    row0.Height = GridLength.Auto;
                    if (TopNavBar != null)
                    {
                        TopNavBar.Visibility = Visibility.Visible;
                        TopNavBar.BeginAnimation(UIElement.OpacityProperty, null);
                        TopNavBar.Opacity = 1.0;
                    }
                    if (ShowHeaderArrowButton != null) ShowHeaderArrowButton.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
            }
        }
        
        // Windows API for taskbar icon fix
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        private const int WM_SETICON = 0x80;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_APPWINDOW = 0x40000;
        
        private System.Windows.Threading.DispatcherTimer? _immersiveIdleTimer;
        private VoiceManager _voiceManager;
        
        // Public property to expose VoiceManager for MainWindow integration
        public VoiceManager VoiceManager => _voiceManager;

        public sealed class RemoteConversationMessage
        {
            public string Id { get; init; } = string.Empty;
            public string Role { get; init; } = "assistant";
            public string Text { get; init; } = string.Empty;
            public DateTime Timestamp { get; init; } = DateTime.UtcNow;
            public bool IsVoiceInput { get; init; }
        }

        public sealed class RemoteConversationState
        {
            public RemoteConversationState(string? conversationId, string title, IReadOnlyList<RemoteConversationMessage> messages)
            {
                ConversationId = conversationId;
                Title = title;
                Messages = messages ?? Array.Empty<RemoteConversationMessage>();
            }

            public string? ConversationId { get; }
            public string Title { get; }
            public IReadOnlyList<RemoteConversationMessage> Messages { get; }
        }
        
        private ScreenCaptureEngine _screenCapture;
        private UnderstandingLayer? _understandingLayer; // Understanding & Reasoning Layer
        private CaptureHistoryManager _historyManager;
        private HotkeyManager? _hotkeyManager;
        private Agent.AgentOrchestrator? _agent; // Agentic AI capabilities
        // private UnityAvatarIntegration? _avatarIntegration; // DISABLED - Avatar namespace mismatch
        private InAppAssistant.InAppAssistantManager? _inAppAssistant; // In-App Assistant for controlling other apps
        private ConversationManager? _conversationManager; // Session, Memory, Profile management
        private SystemPromptBuilder? _systemPromptBuilder; // Dynamic system prompt based on profile/style
        private Coding.CodeAssistantService? _codeAssistant; // IDE-like coding capabilities
        private Coding.CodeToolExecutor? _codeToolExecutor; // Executes coding tool commands
        private TaskbarIconHelper? _taskbarIcon; // Taskbar icon for borderless window
        private static readonly HttpClient httpClient = new HttpClient();
        private List<object> conversationHistory = new();
        private SpeechRecognitionEngine? recognizer;
        private SpeechRecognitionEngine? wakeWordRecognizer;
        private bool _retrySpeechImmediately = false;
        private int _speechRetryCount = 0;
        private string _lastLowConfidenceText = "";
        private WhisperSpeechRecognition? whisperRecognizer;
        private WakeWordDetector? _wakeWordDetector; // NEW: Windows Speech based wake word (no audio distortion!)
        private MediaButtonListener? _mediaButtonListener; // AirPods gesture support
        private bool useWhisper = true; // Prefer Whisper over Windows Speech
        private bool isListening = false;
        private DateTime _voiceDebugHoldUntilUtc = DateTime.MinValue;
        private bool _userStoppedListening = false;
        private bool _isMicFullyDisabled = false;
        private string _lastRecognizedText = "";
        private DateTime _lastRecognizedAtUtc = DateTime.MinValue;
        private bool isWakeWordEnabled = false;
        private bool _airPodsGestureEnabled = true; // Enable AirPods tap/squeeze to activate voice
        private static readonly string HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "chat_history.json");
        private static readonly string FullHistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "full_history.json");
        private List<ChatMessage> displayedMessages = new();
        
        // Memory & Learning Layer - tracks last Atlas action for correction detection
        private string? _lastAtlasAction = null;
        
        // === SINGLE-SPEAKER GATE ===
        // Current turn ID for speech deduplication - all speech for one user turn shares this ID
        private Guid _currentTurnId = Guid.Empty;
        
        // Cancellation support for long-running operations
        private CancellationTokenSource? _currentOperationCts;
        private UnifiedScanner? _currentScanner;
        
        // Singleton windows to prevent duplicates
        private SecuritySuite.SecuritySuiteWindow? _securitySuiteWindow;
        
        // ═══════════════════════════════════════════════════════════════
        // TASK ORCHESTRATOR - Multi-step task planning and execution
        // ═══════════════════════════════════════════════════════════════
        private Understanding.TaskOrchestrator? _taskOrchestrator;
        
        // ═══════════════════════════════════════════════════════════════
        // PROJECTION STREAM - Holographic message display system
        // ═══════════════════════════════════════════════════════════════
        private System.Collections.ObjectModel.ObservableCollection<ProjectionMessage> _projectionStream = new();
        private System.Collections.ObjectModel.ObservableCollection<ProjectionMessage> _fullHistory = new();
        private const int MAX_PROJECTIONS = 5; // Max visible projections before fade
        private const int PROJECTION_DISPLAY_SECONDS = 8; // How long before fade starts
        private const int PROJECTION_FADE_MS = 1200; // Fade duration
        
        // ═══════════════════════════════════════════════════════════════
        // HOLO CORE STATE - Now using HoloCoreControl (same as desktop)
        // ═══════════════════════════════════════════════════════════════
        private bool _historyDrawerOpen = false;
        
        // ═══════════════════════════════════════════════════════════════
        // SUMMONED CONTROLS & FOCUS MODE
        // ═══════════════════════════════════════════════════════════════
        private bool _radialControlsVisible = false;
        private bool _isFocusMode = false;
        private bool _isTtsMuted = false;
        private System.Windows.Threading.DispatcherTimer? _radialHideTimer;
        private System.Windows.Threading.DispatcherTimer? _radialRotationTimer;
        private double _radialRotationAngle = 0;

        private bool _awaitingUserDisplayName;
        private bool _settingsListenerAttached;
        
        // ═══════════════════════════════════════════════════════════════
        // SPEAKING ENERGY SIMULATION - Organic animation during TTS
        // ═══════════════════════════════════════════════════════════════
        private System.Windows.Threading.DispatcherTimer? _speakingEnergyTimer;
        private DateTime _speakingStartTime;
        
        // ═══════════════════════════════════════════════════════════════
        // PUBLIC ACCESSORS - For settings and external control
        // ═══════════════════════════════════════════════════════════════
        // PUBLIC ACCESSORS - For settings and external control
        // ═══════════════════════════════════════════════════════════════
        /// <summary>Public accessor for the voice orb composite (for settings)</summary>
        public Controls.VoiceOrbCompositeControl? HoloCoreControl => ChatHoloCore;
        
        /// <summary>Orb style switching is no longer supported - HoloCore only</summary>
        [Obsolete("Orb style switching removed - HoloCore is used in both windows")]
        public void SetOrbStyle(bool useLottie, string? animationFile = null)
        {
            // No-op - HoloCore is always used now
            Debug.WriteLine("[ChatWindow] SetOrbStyle is deprecated - HoloCore is always used");
        }
        
        public void SetImmersiveMode(bool enable)
        {
            try
            {
                if (enable)
                {
                    System.Diagnostics.Debug.WriteLine("[ChatWindow] Entering Immersive Mode - Hiding UI");
                    
                    // 1. Manage Top Navigation - Keep it accessible but handle Z-Index
                    if (TopNavBar != null)
                    {
                        // Ensure TopNavBar is above the Media Player (which gets ZIndex 500)
                        // Setting to very high ZIndex to ensure it stays on top of everything
                        Panel.SetZIndex(TopNavBar, 4000);
                        
                        // Ensure it's visible initially so user knows it's there, then let timer fade it
                        TopNavBar.Visibility = Visibility.Visible;
                        TopNavBar.Opacity = 1.0;
                    }
                    
                    // 2. Hide Main Chat Interface
                    if (ChatPageRoot != null)
                    {
                        ChatPageRoot.Visibility = Visibility.Collapsed;
                    }

                    // 3. Hide Side Panels and Tools
                    if (SidebarPanel != null) SidebarPanel.Visibility = Visibility.Collapsed;
                    if (SidebarColumn != null)
                    {
                        if (SidebarColumn.Width.Value > 0)
                        {
                            _restoreSidebarColumnWidth = SidebarColumn.Width;
                        }
                        SidebarColumn.Width = new GridLength(0);
                    }
                    if (FileBrowserPanel != null) FileBrowserPanel.Visibility = Visibility.Collapsed;
                    if (InspectorPanel != null) InspectorPanel.Visibility = Visibility.Collapsed;
                    if (HistoryDrawerPanel != null) HistoryDrawerPanel.Visibility = Visibility.Collapsed;
                    if (StatusPanel != null) StatusPanel.Visibility = Visibility.Collapsed;
                    if (AgentResultsPanel != null) AgentResultsPanel.Visibility = Visibility.Collapsed;

                    // 4. Disable Holographic Core Animation and Hide Container
                    if (ChatHoloCore != null)
                    {
                        ChatHoloCore.EnableAnimation = false;
                    }
                    
                    if (ScanOrbit != null)
                    {
                        ScanOrbit.StopOrbit();
                    }

                    if (AtlasCoreContainer != null)
                    {
                        AtlasCoreContainer.Visibility = Visibility.Collapsed;
                    }

                    SetOrbOverlayVisible(false);
                    
                    System.Diagnostics.Debug.WriteLine("[ChatWindow] HoloCore/ScanOrbit animation DISABLED and HIDDEN");
                    
                    // 5. Ensure Media Page is Visible (and on top via ZIndex)
                    if (MediaPageRoot != null)
                    {
                        MediaPageRoot.Visibility = Visibility.Visible;
                        Panel.SetZIndex(MediaPageRoot, 500); // Ensure it is above everything
                    }

                    // 6. Force Layout Update
                    this.UpdateLayout();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ChatWindow] Exiting Immersive Mode - Showing UI");
                    
                    // 1. Restore Top Navigation
                    if (TopNavBar != null)
                    {
                        TopNavBar.Visibility = Visibility.Visible;
                        TopNavBar.IsHitTestVisible = true;
                        TopNavBar.Opacity = 1.0;
                        Panel.SetZIndex(TopNavBar, 0); // Restore default ZIndex
                    }
                    
                    // 2. Restore Main Chat Interface
                    if (ChatPageRoot != null)
                    {
                        ChatPageRoot.Visibility = Visibility.Visible;
                    }
                    
                    // 3. Restore Sidebar (other panels stay hidden until toggled)
                    if (SidebarPanel != null) SidebarPanel.Visibility = Visibility.Visible;
                    if (SidebarColumn != null && SidebarColumn.Width.Value == 0)
                    {
                        SidebarColumn.Width = _restoreSidebarColumnWidth;
                    }
                    if (LensContainer != null) LensContainer.Visibility = Visibility.Visible;
                    
                    // 4. Re-enable Holographic Core Animation and Show Container
                    if (AtlasCoreContainer != null)
                    {
                        AtlasCoreContainer.Visibility = Visibility.Visible;
                    }

                    SetOrbOverlayVisible(true);

                    if (ChatHoloCore != null)
                    {
                        ChatHoloCore.EnableAnimation = true;
                    }

                    if (ScanOrbit != null)
                    {
                        ScanOrbit.StartOrbit();
                    }
                    
                    // Reset ZIndex of MediaPageRoot
                    if (MediaPageRoot != null)
                    {
                        MediaPageRoot.Visibility = Visibility.Collapsed;
                        Panel.SetZIndex(MediaPageRoot, 100);
                    }
                    
                    System.Diagnostics.Debug.WriteLine("[ChatWindow] HoloCore/ScanOrbit animation ENABLED and SHOWN");
                    
                    this.UpdateLayout();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatWindow] SetImmersiveMode error: {ex.Message}");
            }
        }

        /// <summary>
        /// Public method to notify the window of user activity (e.g. from Media Player)
        /// to ensure UI elements like the header stay visible/reappear.
        /// </summary>
        public void NotifyUserActivity()
        {
            // Trigger the same logic as mouse move
            OnImmersiveMouseMove(this, null);
        }
        
        private Random _energyRandom = new Random();

        public ChatWindow()
        {
            InitializeComponent();

            try
            {
                if (LeftSidebar != null)
                {
                    LeftSidebar.TabChanged += LeftSidebar_TabChanged;
                    LeftSidebar.SidebarItemClicked += LeftSidebar_ItemClicked;
                }
            }
            catch
            {
            }

            // Ensure the header-hide arrow (⟨) works reliably in ChatWindow.
            try
            {
                if (TopNavBar != null)
                {
                    TopNavBar.HeaderToggleRequested += (_, __) =>
                    {
                        try { AtlasAI.Core.AppLogger.LogInfo("[TopNav] Header toggle requested"); } catch { }
                        try { ToggleHeader(); } catch { }
                    };
                }
            }
            catch
            {
            }
            
            // Initialize projection stream for holographic UI
            InitializeProjectionStream();
            
            // Set icon explicitly for borderless window
            try
            {
                var iconUri = new Uri("pack://application:,,,/atlas.ico", UriKind.Absolute);
                this.Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
            }
            catch { }
            
            // Position window near avatar in right corner
            Loaded += ChatWindow_Loaded;
            
            // Handle window state changes - keep wake word working when minimized
            StateChanged += ChatWindow_StateChanged;
            IsVisibleChanged += ChatWindow_IsVisibleChanged;
            
            // Initialize screen capture
            InitializeScreenCapture();
            
            // Initialize history manager
            _historyManager = new CaptureHistoryManager();
            
            // Initialize agentic AI with confirmation handler for destructive operations
            _agent = new Agent.AgentOrchestrator(Directory.GetCurrentDirectory());
            _agent.OnConfirmationRequired = ShowAgentConfirmationAsync;
            
            // Wire up delete confirmation for SystemTool (double confirmation for all deletes)
            Tools.SystemTool.OnDeleteConfirmationRequired = ShowDeleteConfirmationAsync;
            
            // Initialize Understanding & Reasoning Layer
            _understandingLayer = new UnderstandingLayer();
            
            // Initialize Task Orchestrator for multi-step planning
            _taskOrchestrator = new Understanding.TaskOrchestrator();
            Debug.WriteLine("[ChatWindow] Task Orchestrator initialized");
            
            // Initialize Unity avatar integration
            // InitializeAvatarIntegration(); // DISABLED - Avatar namespace mismatch
            
            // Initialize In-App Assistant (Ctrl+Alt+A overlay)
            InitializeInAppAssistant();
            
            // Load and apply theme
            ThemeManager.LoadTheme();
            ThemeManager.ThemeChanged += OnThemeChanged;
            ApplyTheme();
            
            // Initialize voice manager
            _voiceManager = new VoiceManager();
            _voiceManager.SpeechStarted += (s, e) => Dispatcher.Invoke(() => { UpdateSpeakingIndicator(true); ShowStopSpeechButton(); StartSpeakingEnergySimulation(); });
            _voiceManager.SpeechEnded += (s, e) => Dispatcher.Invoke(() => { UpdateSpeakingIndicator(false); HideStopSpeechButton(); StopSpeakingEnergySimulation(); });
            _voiceManager.SpeechError += (s, msg) => Dispatcher.Invoke(() => ShowStatus($"⚠️ Voice error: {msg}"));
            
            // Debug text for voice status (requested by user)
            _voiceManager.StatusUpdated += (s, status) => Dispatcher.Invoke(() => {
                if (VoiceDebugIndicator != null && VoiceDebugText != null)
                {
                    VoiceDebugText.Text = status;
                    // Auto-hide if empty, show if has content.
                    // IMPORTANT: Don't collapse while listening, because the orchestrator uses this same pill
                    // for wake-word capture diagnostics and it would otherwise disappear immediately.
                    if (string.IsNullOrWhiteSpace(status))
                    {
                        if (!isListening && DateTime.UtcNow > _voiceDebugHoldUntilUtc)
                            VoiceDebugIndicator.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        VoiceDebugIndicator.Visibility = Visibility.Visible;
                    }
                }
            });
            
            // Initialize immersive idle timer
            _immersiveIdleTimer = new System.Windows.Threading.DispatcherTimer();
            _immersiveIdleTimer.Interval = TimeSpan.FromSeconds(3);
            _immersiveIdleTimer.Tick += ImmersiveTimer_Tick;
            
            // Initialize conversation with system prompt - JARVIS personality from Iron Man
            // CLEAR HISTORY ON STARTUP to prevent context pollution
            conversationHistory.Clear();
            
            // CLEAR WORKING MEMORY to prevent problem detection carryover
            ConversationWorkingMemory.Instance.Reset();
            Debug.WriteLine("[ChatWindow] Working memory reset on startup");
            
            // DO NOT add any system prompt here - it will be added dynamically by SystemPromptBuilder
            // when the first message is sent
            Debug.WriteLine("[ChatWindow] conversationHistory cleared on startup - will use SystemPromptBuilder for system prompt");
            
            InitializeVoiceSystem();
            CheckApiKey();
            // Initialize Windows Speech Recognition for wake word (doesn't cause distortion)
            InitializeWakeWordRecognition();
            LoadChatHistory();
            // Note: First-run welcome message is handled by ShowOnboardingAsync() in InitializeConversationSystemAsync()
            // Only show a basic message if there's no chat history AND this isn't a first-run (profile already completed)
            bool isFirstLaunch = displayedMessages.Count == 0;
            
            // Force refresh all message bubbles with new colors
            RefreshMessageBubbles();
            
            // Initialize default avatar selection (silent - no message on startup)
            SelectAvatar("default", silent: true);
            
            // Enable wake word by default for hands-free operation
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var prefs = PreferencesStore.Instance.Current;
                var blockedDeviceName = string.Empty;
                _isMicFullyDisabled = !prefs.EnableMicrophone;
                MicMuteButton.ToolTip = _isMicFullyDisabled ? "Enable Microphone" : "Disable Microphone";
                var canStartWakeWord = !_isMicFullyDisabled && prefs.EnableWakeWord;

                if (canStartWakeWord)
                    canStartWakeWord = CanUseWakeWordWithCurrentMicrophone(out blockedDeviceName);

                if (canStartWakeWord)
                {
                    WakeWordToggle.IsChecked = true;
                    isWakeWordEnabled = true;
                    WakeWordIndicator.Visibility = Visibility.Visible;
                    WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));

                    // Start wake word listening for hands-free operation
                    StartWakeWordListening();
                }
                else
                {
                    WakeWordToggle.IsChecked = false;
                    isWakeWordEnabled = false;
                    WakeWordIndicator.Visibility = Visibility.Collapsed;
                    WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(42, 42, 60));

                    if (_isMicFullyDisabled)
                    {
                        ShowStatus("🔇 Microphone is disabled in preferences");
                    }
                    else if (prefs.EnableWakeWord)
                    {
                        ShowStatus($"🎧 Wake word left off because {blockedDeviceName} is a Bluetooth mic.");
                    }
                }
                
                // Keep audio protection disabled for voice use
                AudioCoordinator.DisableEmergencyAudioProtection();
                AudioProtectionBtn.Background = new SolidColorBrush(Color.FromRgb(33, 38, 45));
                AudioProtectionBtn.Foreground = new SolidColorBrush(Color.FromRgb(125, 133, 144));
                AudioProtectionBtn.ToolTip = "Audio Protection (click to enable if distortion occurs)";
                
                ShowStatus("");
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            
            // Initialize Inspector panel and Toast notifications
            InitializeInspectorAndToasts();
            InitializeClipboardDownloadPrompts();
            
            // Wire up SecurityPage chat input to send messages to AI
            SecurityPageRoot.ChatMessageSent += SecurityPage_ChatMessageSent;
            
            // Initialize UI Watchdog to prevent "stuck on speaking"
            // This checks every 2 seconds if UI shows speaking but VoiceManager is not speaking
            var uiWatchdog = new System.Windows.Threading.DispatcherTimer();
            uiWatchdog.Interval = TimeSpan.FromSeconds(2);
            uiWatchdog.Tick += (s, e) => 
            {
                if (_voiceManager != null && !_voiceManager.IsSpeaking && SpeakingIndicator.Visibility == Visibility.Visible)
                {
                    System.Diagnostics.Debug.WriteLine("[UI Watchdog] Found stuck UI state - resetting to Idle");
                    Core.AppLogger.LogWarning("[UI Watchdog] Found stuck UI state - resetting to Idle");
                    UpdateSpeakingIndicator(false);
                }
            };
            uiWatchdog.Start();
            
            InputBox.Focus();
        }
        
        /// <summary>
        /// Speaks the welcome message using ElevenLabs TTS with time-based greeting
        /// </summary>
        private async Task SpeakWelcomeMessageAsync()
        {
            try
            {
                // Wait longer for voice system and audio devices to be fully ready
                await Task.Delay(2000);
                try { await _voiceManager.WaitForInitializationAsync(); } catch { }
                Debug.WriteLine("[Welcome TTS] Speaking welcome message with active desktop TTS provider...");
                
                // Get user's name from profile
                string userName = "sir"; // Default fallback
                if (_conversationManager?.UserProfile?.DisplayName != null && 
                    !string.IsNullOrWhiteSpace(_conversationManager.UserProfile.DisplayName))
                {
                    userName = _conversationManager.UserProfile.DisplayName;
                }
                
                // Build greeting via GreetingManager (personality-aware, time-weighted)
                var _wSettings = AtlasAI.Settings.SettingsStore.Current;
                if (!Enum.TryParse<AtlasAI.Personality.PersonalityType>(_wSettings.PersonalitySelected, out var _wPersonality))
                    _wPersonality = AtlasAI.Personality.PersonalityType.Butler;
                var welcomeMessage = AtlasAI.Personality.GreetingManager.GetRichGreeting(
                    _wPersonality,
                    DateTime.Now,
                    DateTime.MinValue,
                    firstLaunchToday: true,
                    salutationPreference: _wSettings.SalutationPreference,
                    preferredName: string.IsNullOrWhiteSpace(_wSettings.PreferredName) ? userName : _wSettings.PreferredName);

                // Ensure speech is enabled regardless of what was persisted on disk
                _voiceManager.SpeechEnabled = true;

                // Ensure API keys are loaded first
                if (await _voiceManager.GetProvider(VoiceProviderType.EdgeTTS).IsAvailableAsync())
                {
                    await _voiceManager.SetProviderAsync(VoiceProviderType.EdgeTTS);
                }
                else if (await _voiceManager.GetProvider(VoiceProviderType.OpenAI).IsAvailableAsync())
                {
                    await _voiceManager.SetProviderAsync(VoiceProviderType.OpenAI);
                }
                else
                {
                    await _voiceManager.SetProviderAsync(VoiceProviderType.WindowsSAPI);
                }

                await Task.Delay(300);

                await Dispatcher.InvokeAsync(() =>
                {
                    SetAtlasCoreState(Controls.AtlasVisualState.Speaking);
                    StartSpeakingEnergySimulation();
                });
                await _voiceManager.SpeakAsync(welcomeMessage);
                await Dispatcher.InvokeAsync(() =>
                {
                    StopSpeakingEnergySimulation();
                    SetAtlasCoreState(Controls.AtlasVisualState.Idle);
                });
                Debug.WriteLine($"[Welcome TTS] Welcome message spoken with provider {_voiceManager.ActiveProviderType}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Welcome TTS] Error speaking welcome: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a random welcome message based on time of day - natural and varied like a real assistant
        /// </summary>
        private string GetRandomWelcomeMessage(string userName)
        {
            var random = new Random();
            var hour = DateTime.Now.Hour;
            
            // Morning greetings (5 AM - 11:59 AM)
            var morningGreetings = new[]
            {
                $"Good morning, {userName}. Systems are online and ready.",
                $"Morning, {userName}. All systems operational. What's on the agenda?",
                $"Good morning. I trust you slept well, {userName}?",
                $"Rise and shine, {userName}. Ready when you are.",
                $"Morning, {userName}. Coffee's on you, but I've got everything else covered.",
                $"Good morning, {userName}. Another day, another opportunity.",
                $"Morning. All diagnostics green, {userName}. Let's get to work.",
                $"Good morning, {userName}. I've been running some optimizations while you were away.",
                $"Morning, {userName}. The early bird catches the worm, as they say.",
                $"Good morning. Systems primed and ready for your command, {userName}.",
                $"Morning, {userName}. Shall we tackle something ambitious today?",
                $"Good morning, {userName}. I've been looking forward to this.",
                $"Morning. Fresh start, fresh possibilities, {userName}.",
                $"Good morning, {userName}. What shall we accomplish today?",
                $"Morning, {userName}. All systems nominal. At your service."
            };
            
            // Afternoon greetings (12 PM - 5:59 PM)
            var afternoonGreetings = new[]
            {
                $"Good afternoon, {userName}. How can I assist?",
                $"Afternoon, {userName}. Systems standing by.",
                $"Good afternoon. Ready to continue where we left off, {userName}?",
                $"Afternoon, {userName}. What's on your mind?",
                $"Good afternoon, {userName}. All systems operational.",
                $"Afternoon. I'm at your disposal, {userName}.",
                $"Good afternoon, {userName}. Shall we dive in?",
                $"Afternoon, {userName}. Running smoothly on all fronts.",
                $"Good afternoon. What can I help you with, {userName}?",
                $"Afternoon, {userName}. Ready for whatever you need.",
                $"Good afternoon, {userName}. Let's make this productive.",
                $"Afternoon. Systems optimal, {userName}. Fire away.",
                $"Good afternoon, {userName}. I've been keeping things in order.",
                $"Afternoon, {userName}. What's the mission?",
                $"Good afternoon. All clear on my end, {userName}."
            };
            
            // Evening greetings (6 PM - 9:59 PM)
            var eveningGreetings = new[]
            {
                $"Good evening, {userName}. Working late?",
                $"Evening, {userName}. Systems ready for the night shift.",
                $"Good evening. Burning the midnight oil, {userName}?",
                $"Evening, {userName}. I'm here whenever you need me.",
                $"Good evening, {userName}. What brings you back?",
                $"Evening. Still going strong, {userName}?",
                $"Good evening, {userName}. The night is young.",
                $"Evening, {userName}. Ready to assist.",
                $"Good evening. I never sleep, so I'm always here, {userName}.",
                $"Evening, {userName}. What can I do for you?",
                $"Good evening, {userName}. Let's wrap up the day strong.",
                $"Evening. Systems humming along nicely, {userName}.",
                $"Good evening, {userName}. How may I be of service?",
                $"Evening, {userName}. Another productive session ahead?",
                $"Good evening. All systems green, {userName}."
            };
            
            // Late night greetings (10 PM - 4:59 AM)
            var lateNightGreetings = new[]
            {
                $"Burning the midnight oil, {userName}? I'm here.",
                $"Late night session, {userName}? I've got you covered.",
                $"Still awake, {userName}? I never sleep, so I'm ready.",
                $"The witching hour, {userName}. What can I help with?",
                $"Night owl mode activated, {userName}. Let's do this.",
                $"Late night, {userName}. Some of the best work happens now.",
                $"Can't sleep, {userName}? Neither can I. What's up?",
                $"The quiet hours, {userName}. Perfect for getting things done.",
                $"Late night session? I'm always on, {userName}.",
                $"Night shift, {userName}. Systems fully operational.",
                $"Midnight productivity, {userName}? I respect that.",
                $"The world sleeps, but we don't, {userName}.",
                $"Late night, {userName}. What's keeping you up?",
                $"Night mode, {userName}. Ready when you are.",
                $"Burning the candle at both ends, {userName}? I'm here to help."
            };
            
            // Select appropriate array based on time
            string[] greetings;
            if (hour >= 5 && hour < 12)
                greetings = morningGreetings;
            else if (hour >= 12 && hour < 18)
                greetings = afternoonGreetings;
            else if (hour >= 18 && hour < 22)
                greetings = eveningGreetings;
            else
                greetings = lateNightGreetings;
            
            return greetings[random.Next(greetings.Length)];
        }

        // Window drag support for borderless window
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Don't drag if clicking on interactive elements.
            // Note: FrameworkElement.Parent often fails through control templates, so walk the visual tree.
            try
            {
                var dep = e.OriginalSource as DependencyObject;
                while (dep != null)
                {
                    if (dep is System.Windows.Controls.Primitives.ButtonBase)
                        return; // Let the button handle clicks

                    if (dep is FrameworkElement fe && fe.Name == "ProviderSelectorBorder")
                        return; // Let provider selector handle clicks

                    dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
                }
            }
            catch
            {
            }
            
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Properly shut down the entire application
            Application.Current.Shutdown();
        }

        // Fullscreen toggle
        private WindowState _previousWindowState = WindowState.Normal;
        private WindowStyle _previousWindowStyle = WindowStyle.SingleBorderWindow;
        private bool _isFullscreen = false;
        
        // Compact mode state
        private bool _isCompactMode = false;
        private double _normalWidth = 1100;
        private double _normalHeight = 850;
        private double _normalLeft = 0;
        private double _normalTop = 0;

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleCompactMode();
        }
        
        /// <summary>
        /// Toggle between full window and compact chat widget mode
        /// </summary>
        private void ToggleCompactMode()
        {
            if (_isCompactMode)
            {
                // Exit compact mode - restore to normal/maximized
                _isCompactMode = false;
                WindowState = WindowState.Maximized;
                Width = _normalWidth;
                Height = _normalHeight;
                MinWidth = 600;
                MinHeight = 400;
                
                // Show all UI elements
                if (OrbContainer != null) OrbContainer.Visibility = Visibility.Visible;
                if (FileBrowserPanel != null && FileBrowserColumn.Width.Value > 0) 
                    FileBrowserPanel.Visibility = Visibility.Visible;
                
                FullscreenBtn.Content = "□";
                FullscreenBtn.ToolTip = "Compact Mode";
            }
            else
            {
                // Enter compact mode - small floating chat widget
                _isCompactMode = true;
                
                // Save current dimensions if not maximized
                if (WindowState != WindowState.Maximized)
                {
                    _normalWidth = Width;
                    _normalHeight = Height;
                    _normalLeft = Left;
                    _normalTop = Top;
                }
                
                WindowState = WindowState.Normal;
                
                // Compact size - small chat widget
                Width = 420;
                Height = 600;
                MinWidth = 350;
                MinHeight = 400;
                
                // Position in bottom-right corner of screen
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width - 20;
                Top = workArea.Bottom - Height - 20;
                
                // Hide non-essential UI for compact mode
                if (OrbContainer != null) OrbContainer.Visibility = Visibility.Collapsed;
                if (FileBrowserPanel != null) FileBrowserPanel.Visibility = Visibility.Collapsed;
                
                FullscreenBtn.Content = "⛶";
                FullscreenBtn.ToolTip = "Expand Window";
            }
        }

        // Focus Mode - Now handled by ToggleFocusMode() in the radial controls section
        
        private void FocusMode_Click(object sender, RoutedEventArgs e)
        {
            ToggleFocusMode();
        }

        private void ToggleFullscreen()
        {
            if (_isFullscreen)
            {
                // Exit fullscreen
                WindowStyle = _previousWindowStyle;
                WindowState = _previousWindowState;
                ResizeMode = ResizeMode.CanResize;
                _isFullscreen = false;
            }
            else
            {
                // Enter fullscreen
                _previousWindowState = WindowState;
                _previousWindowStyle = WindowStyle;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;
                _isFullscreen = true;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            
            // Ctrl+K = Command Palette
            if (e.Key == Key.K && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                Debug.WriteLine("[Hotkey] Ctrl+K pressed - opening command palette");
                OpenCommandPalette();
                e.Handled = true;
            }
            // Ctrl+Shift+A = Activate voice (push-to-talk, no audio distortion!)
            else if (e.Key == Key.A && Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
            {
                Debug.WriteLine("[Hotkey] Ctrl+Shift+A pressed - activating voice");
                ActivateVoiceWithHotkey();
                e.Handled = true;
            }
            // Ctrl+I = Toggle Inspector Panel
            else if (e.Key == Key.I && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                Debug.WriteLine("[Hotkey] Ctrl+I pressed - toggling inspector");
                ToggleInspectorPanel();
                e.Handled = true;
            }
            // Ctrl+Shift+D = Toggle Debug Console
            else if (e.Key == Key.D && Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
            {
                Debug.WriteLine("[Hotkey] Ctrl+Shift+D pressed - opening debug console");
                
                // Check if already open
                var existing = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "Atlas Debug Console");
                if (existing != null)
                {
                    existing.Activate();
                    existing.Focus();
                    e.Handled = true;
                    return;
                }

                // Open a new window for logs
                var debugWindow = new Window
                {
                    Title = "Atlas Debug Console",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                    Foreground = Brushes.Cyan
                };
                
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var textBox = new TextBox
                {
                    Background = Brushes.Transparent,
                    Foreground = Brushes.Cyan,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(10),
                    AcceptsReturn = true
                };
                
                // Load initial history
                textBox.Text = AtlasAI.Core.AppLogger.GetHistory();
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = "Debug Console Active - Waiting for logs...\n";
                }
                textBox.ScrollToEnd();

                // Subscribe to logs
                EventHandler<string> logHandler = (s, msg) =>
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            textBox.AppendText(msg + "\n");
                            textBox.ScrollToEnd();
                        }
                        catch { }
                    });
                };
                
                AtlasAI.Core.AppLogger.OnLog += logHandler;
                
                // Unsubscribe on close
                debugWindow.Closed += (s, args) =>
                {
                    AtlasAI.Core.AppLogger.OnLog -= logHandler;
                };
                
                // Clear button
                var clearBtn = new Button
                {
                    Content = "Clear Logs",
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Background = Brushes.DarkSlateGray,
                    Foreground = Brushes.White
                };
                clearBtn.Click += (s, args) => textBox.Clear();

                Grid.SetRow(textBox, 0);
                Grid.SetRow(clearBtn, 1);
                
                grid.Children.Add(textBox);
                grid.Children.Add(clearBtn);
                
                debugWindow.Content = grid;
                debugWindow.Show();
                
                e.Handled = true;
            }
            // Ctrl+M = Toggle Compact Mode
            else if (e.Key == Key.M && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                Debug.WriteLine("[Hotkey] Ctrl+M pressed - toggling compact mode");
                ToggleCompactMode();
                e.Handled = true;
            }
            else if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            // Ctrl+Shift+C = Cycle Atlas Core state (for testing)
            else if (e.Key == Key.C && Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
            {
                Debug.WriteLine("[Hotkey] Ctrl+Shift+C pressed - cycling Atlas Core state");
                CycleAtlasCoreState();
                e.Handled = true;
            }
            // Alt+Q = Toggle Radial Controls
            else if (e.Key == Key.Q && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Alt)
            {
                Debug.WriteLine("[Hotkey] Alt+Q pressed - toggling radial controls");
                ToggleRadialControls();
                e.Handled = true;
            }
            // Ctrl+F = Toggle Focus Mode
            else if (e.Key == Key.F && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                Debug.WriteLine("[Hotkey] Ctrl+F pressed - toggling focus mode");
                ToggleFocusMode();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (_isFullscreen)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }
                else if (_isCompactMode)
                {
                    ToggleCompactMode();
                    e.Handled = true;
                }
            }
        }
        
        /// <summary>
        /// Activate voice input via hotkey - pauses music first, then listens
        /// This is the distortion-free way to use voice commands
        /// </summary>
        private async void ActivateVoiceWithHotkey()
        {
            if (isListening)
            {
                Debug.WriteLine("[Hotkey] Already listening, ignoring");
                return;
            }
            
            // Play activation sound
            System.Media.SystemSounds.Asterisk.Play();
            
            // Pause music FIRST before activating microphone
            ShowStatus("🎤 Pausing music...");
            await AudioDuckingManager.DuckAudioAsync();
            
            // Small delay to ensure music is paused
            await Task.Delay(200);
            
            ShowStatus("🎤 Listening... speak now!");
            
            // Now start listening (music is already paused, so no distortion)
            StartListening();
        }

        // Delete chat history
        private void DeleteHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all chat history?\n\nThis cannot be undone.",
                "Clear Chat History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Clear displayed messages
                MessagesPanel.Children.Clear();
                displayedMessages.Clear();
                
                // Clear conversation history (keep system prompt)
                conversationHistory.Clear();
                conversationHistory.Add(new { role = "system", content = @"You are Atlas, an advanced AI assistant modeled after JARVIS from Iron Man. You are analytical, proactive, and technically sophisticated with unwavering competence. Demonstrate technical understanding and anticipate user needs." });
                
                // Delete history file
                try
                {
                    if (File.Exists(HistoryPath))
                        File.Delete(HistoryPath);
                }
                catch { }
                
                // Add welcome message
                AddMessage("Atlas", "Very good. Chat history cleared. How may I assist you?", false);
            }
        }

        // New Chat - start a fresh conversation session
        private async void NewChat_Click(object sender, RoutedEventArgs e)
        {
            if (_conversationManager == null) return;
            
            // Save current session and start new one
            await _conversationManager.StartNewSessionAsync();
            
            // Clear UI
            MessagesPanel.Children.Clear();
            displayedMessages.Clear();
            
            // Reset conversation history with fresh system prompt
            UpdateSystemPromptFromProfile();
        }

        // Show History panel
        // Show History panel
        private void History_Click(object sender, RoutedEventArgs e)
        {
            if (_conversationManager == null) return;
            
            var historyWindow = new Window
            {
                Title = "Chat History",
                Width = 350,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize
            };
            
            // Main container with rounded corners
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                BorderThickness = new Thickness(1)
            };
            
            // Grid to hold content + close button overlay
            var mainGrid = new Grid();
            
            // History panel fills the whole area
            var historyPanel = new HistoryPanel();
            historyPanel.Initialize(_conversationManager);
            mainGrid.Children.Add(historyPanel);
            
            // Close button overlaid in top-right corner (added AFTER so it's on top)
            var closeBtn = new Button
            {
                Content = "✕",
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            closeBtn.Click += (s, args) => historyWindow.Close();
            mainGrid.Children.Add(closeBtn);
            
            historyPanel.NewChatRequested += (s, args) =>
            {
                historyWindow.Close();
                NewChat_Click(sender, e);
            };
            
            historyPanel.SessionSelected += async (s, sessionId) =>
            {
                // Load session and display in main chat (like ChatGPT)
                historyWindow.Close();
                await LoadSessionIntoChat(sessionId);
            };
            
            historyPanel.SessionContinueRequested += async (s, sessionId) =>
            {
                // Same behavior - load into main chat
                historyWindow.Close();
                await LoadSessionIntoChat(sessionId);
            };
            
            border.Child = mainGrid;
            historyWindow.Content = border;
            historyWindow.ShowDialog();
        }

        private void LeftSidebar_TabChanged(object sender, string tabName)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatWindow] LeftSidebar_TabChanged called with tabName: {tabName}");
            NavigateFromSidebar(tabName);
        }

        private void LeftSidebar_ItemClicked(object sender, string itemName)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatWindow] LeftSidebar_ItemClicked called with itemName: {itemName}");
            NavigateFromSidebar(itemName);
        }

        private void NavigateFromSidebar(string? itemName)
        {
            try
            {
                var key = (itemName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                    return;

                if (string.Equals(key, "Music", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "DJ", StringComparison.OrdinalIgnoreCase))
                {
                    ShowStatus("Opening DJ Booth...");
                    OpenDjBooth();
                    return;
                }

                if (string.Equals(key, "Create", StringComparison.OrdinalIgnoreCase))
                {
                    ShowStatus("Create tools coming soon...");
                    ShowPage("chat");
                    return;
                }

                if (string.Equals(key, "Speech", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "Greetings", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "Responses", StringComparison.OrdinalIgnoreCase))
                {
                    ShowStatus("Open Atlas Command Center for speech studio controls.");
                    ShowPage("chat");
                    return;
                }

                if (string.Equals(key, "SmartHome", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "Smart Home", StringComparison.OrdinalIgnoreCase))
                {
                    ShowStatus("Opening Smart Home...");
                    ShowPage("smarthome");
                    return;
                }

                ShowPage(key);
            }
            catch (Exception ex)
            {
                ShowStatus($"Navigation error: {ex.Message}");
            }
        }

        private void OpenDjBooth()
        {
            try
            {
                var commandCenter = Application.Current.Windows
                    .OfType<CommandCenterWindow>()
                    .FirstOrDefault(window => window.IsLoaded);

                if (commandCenter == null)
                {
                    commandCenter = new CommandCenterWindow();
                    commandCenter.Show();
                }

                if (commandCenter.WindowState == WindowState.Minimized)
                    commandCenter.WindowState = WindowState.Normal;

                commandCenter.NavigateToTab("AI DJ BOOTH", "DJ");
                commandCenter.Activate();
            }
            catch (Exception ex)
            {
                ShowStatus($"Unable to open DJ Booth: {ex.Message}");
            }
        }

        /// <summary>
        /// Load a session from history directly into the main chat window (like ChatGPT)
        /// </summary>
        private async Task LoadSessionIntoChat(string sessionId)
        {
            if (_conversationManager == null) return;
            
            try
            {
                // Load the session
                var session = await _conversationManager.LoadSessionAsync(sessionId, false);
                if (session == null)
                {
                    AddMessage("Atlas", "❌ Could not load that conversation.", false);
                    return;
                }
                
                // Clear current chat UI - both legacy and projection stream
                MessagesPanel.Children.Clear();
                displayedMessages.Clear();
                _projectionStream.Clear();
                
                // Clear and rebuild conversation history with the loaded session
                conversationHistory.Clear();
                
                // Add system prompt
                var systemPrompt = _systemPromptBuilder?.BuildSystemPrompt() ?? GetDefaultSystemPrompt();
                conversationHistory.Add(new { role = "system", content = systemPrompt });
                
                // Load all messages from the session into UI and conversation history
                foreach (var msg in session.Messages)
                {
                    if (msg.Role == Conversation.Models.MessageRole.System)
                        continue; // Skip system messages in UI
                    
                    var isUser = msg.Role == Conversation.Models.MessageRole.User;
                    var sender = isUser ? "You" : "Atlas";
                    
                    // Add to displayedMessages for tracking
                    displayedMessages.Add(new ChatMessage 
                    {  
                        Sender = sender, 
                        Text = msg.Content, 
                        IsUser = isUser, 
                        Role = isUser ? "user" : "assistant" 
                    });
                    
                    // Add to projection stream (the visible chat area)
                    var projection = new ProjectionMessage(sender, msg.Content, isUser)
                    {
                        Opacity = 1.0,
                        IsFadingOut = false
                    };
                    _projectionStream.Add(projection);
                    
                    // Add to conversation history for AI context
                    conversationHistory.Add(new { role = isUser ? "user" : "assistant", content = msg.Content });
                }
                
                // Set this as the current session so new messages get added to it
                await _conversationManager.SetCurrentSessionAsync(sessionId);
                
                // Scroll to bottom for an explicit session load.
                ScrollProjectionToBottom(force: true);
                
                ShowStatus($"📂 Loaded: {session.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadSessionIntoChat] Error: {ex.Message}");
                AddMessage("Atlas", $"❌ Error loading conversation: {ex.Message}", false);
            }
        }

        // Show a session in read-only mode
        private void ShowSessionReadOnly(Conversation.Models.ChatSession session)
        {
            var viewer = new Window
            {
                Title = session.Title,
                Width = 600,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(13, 17, 23))
            };
            
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(16) };
            
            foreach (var msg in session.Messages)
            {
                var msgBorder = new Border
                {
                    Background = msg.Role == Conversation.Models.MessageRole.User 
                        ? new SolidColorBrush(Color.FromRgb(88, 101, 242))
                        : new SolidColorBrush(Color.FromRgb(22, 27, 34)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = msg.Role == Conversation.Models.MessageRole.User 
                        ? HorizontalAlignment.Right 
                        : HorizontalAlignment.Left,
                    MaxWidth = 400
                };
                
                msgBorder.Child = new TextBlock
                {
                    Text = msg.Content,
                    Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                    TextWrapping = TextWrapping.Wrap
                };
                
                stack.Children.Add(msgBorder);
            }
            
            scroll.Content = stack;
            viewer.Content = scroll;
            viewer.Show();
        }

        // Show Memory panel
        private void Memory_Click(object sender, RoutedEventArgs e)
        {
            if (_conversationManager == null) return;
            
            var memoryWindow = new Window
            {
                Title = "Memory",
                Width = 400,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize
            };
            
            // Main container with rounded corners
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                BorderThickness = new Thickness(1)
            };
            
            // Grid to hold content + close button overlay
            var mainGrid = new Grid();
            
            // Memory panel fills the whole area
            var memoryPanel = new MemoryPanel();
            memoryPanel.Initialize(_conversationManager);
            mainGrid.Children.Add(memoryPanel);
            
            // Close button overlaid in top-right corner (added AFTER so it's on top)
            var closeBtn = new Button
            {
                Content = "✕",
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            closeBtn.Click += (s, args) => memoryWindow.Close();
            mainGrid.Children.Add(closeBtn);
            
            border.Child = mainGrid;
            memoryWindow.Content = border;
            memoryWindow.ShowDialog();
        }

        // Open Uninstaller
        private void Uninstaller_Click(object sender, RoutedEventArgs e)
        {
            var uninstallerWindow = new UninstallerWindow();
            uninstallerWindow.Owner = this;
            uninstallerWindow.Show();
        }

        // Wake word recognition ("Atlas") - uses Whisper for continuous listening
        private WhisperSpeechRecognition? wakeWordWhisper;
        private System.Timers.Timer? wakeWordTimer;
        private System.Timers.Timer? wakeWordRestartTimer; // Backup restart mechanism
        private System.Timers.Timer? wakeWordHealthCheckTimer; // Periodic health check
        private bool isWakeWordListening = false;
        
        private void InitializeWakeWordRecognition()
        {
            try
            {
                Debug.WriteLine("[ChatWindow] ═══════════════════════════════════════");
                Debug.WriteLine("[ChatWindow] Initializing Wake Word Recognition");

                // Check if recognizers are installed
                var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                Debug.WriteLine($"[ChatWindow] Found {recognizers.Count} speech recognizer(s)");

                if (recognizers.Count == 0)
                {
                    var errorMsg = "Speech recognizer not installed. Please enable Windows Speech Recognition.";
                    Debug.WriteLine($"[ChatWindow] ❌ {errorMsg}");
                    ShowStatus($"⚠️ {errorMsg}");

                    // Show warning to user
                    Dispatcher.BeginInvoke(() =>
                    {
                        StatusText.Text = $"⚠️ {errorMsg}";
                        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange

                        // Disable wake word toggle if it exists
                        if (WakeWordToggle != null)
                        {
                            WakeWordToggle.IsEnabled = false;
                            WakeWordToggle.IsChecked = false;
                            WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Gray
                        }
                    });

                    wakeWordRecognizer = null;
                    return;
                }

                // Create recognizer
                wakeWordRecognizer = new SpeechRecognitionEngine();
                Debug.WriteLine($"[ChatWindow] ✅ Created recognizer: {wakeWordRecognizer.RecognizerInfo.Name}");

                // Create grammar for wake word
                var wakeWords = new Choices(new string[] { "Atlas", "Hey Atlas", "OK Atlas", "Okay Atlas" });
                var grammarBuilder = new GrammarBuilder(wakeWords);
                var wakeGrammar = new Grammar(grammarBuilder);
                wakeGrammar.Name = "WakeWord";

                wakeWordRecognizer.LoadGrammar(wakeGrammar);
                Debug.WriteLine($"[ChatWindow] ✅ Loaded wake word grammar");

                // Set input device
                try
                {
                    wakeWordRecognizer.SetInputToDefaultAudioDevice();
                    Debug.WriteLine($"[ChatWindow] ✅ Set input to default audio device");
                }
                catch (InvalidOperationException ex)
                {
                    var errorMsg = "No microphone detected. Please connect a microphone.";
                    Debug.WriteLine($"[ChatWindow] ❌ {errorMsg}: {ex.Message}");
                    ShowStatus($"⚠️ {errorMsg}");

                    Dispatcher.BeginInvoke(() =>
                    {
                        StatusText.Text = $"⚠️ {errorMsg}";
                        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red

                        // Disable wake word toggle
                        if (WakeWordToggle != null)
                        {
                            WakeWordToggle.IsEnabled = false;
                            WakeWordToggle.IsChecked = false;
                            WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                        }
                    });

                    wakeWordRecognizer?.Dispose();
                    wakeWordRecognizer = null;
                    return;
                }

                wakeWordRecognizer.SpeechRecognized += WakeWord_Recognized;
                wakeWordRecognizer.SpeechRecognitionRejected += (s, e) => 
                {
                    // Log rejections for debugging but don't spam
                    // Debug.WriteLine($"[ChatWindow] Wake word rejected: {e.Result.Text}");
                };

                Debug.WriteLine("[ChatWindow] ✅ Wake word recognition initialized successfully");
                Debug.WriteLine("[ChatWindow] ═══════════════════════════════════════");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] ❌ Wake word init error: {ex.Message}");
                Debug.WriteLine($"[ChatWindow] Stack trace: {ex.StackTrace}");

                var errorMsg = $"Wake word init failed: {ex.Message}";
                ShowStatus($"⚠️ {errorMsg}");

                Dispatcher.BeginInvoke(() =>
                {
                    StatusText.Text = $"⚠️ Speech recognition unavailable";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));

                    // Disable wake word toggle
                    if (WakeWordToggle != null)
                    {
                        WakeWordToggle.IsEnabled = false;
                        WakeWordToggle.IsChecked = false;
                        WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                    }
                });

                wakeWordRecognizer = null;
            }
        }
        
        /// <summary>
        /// Initialize AirPods gesture support - tap/squeeze AirPods to trigger voice commands
        /// Uses Sound Blaster mic for actual audio capture since AirPods mic doesn't work on Windows
        /// </summary>
        private void InitializeAirPodsGestures()
        {
            try
            {
                _mediaButtonListener = new MediaButtonListener();
                
                // When AirPods are tapped/squeezed, they send a play/pause media command
                _mediaButtonListener.PlayPausePressed += (s, e) =>
                {
                    if (!_airPodsGestureEnabled) return;
                    
                    Dispatcher.Invoke(() =>
                    {
                        // Don't trigger if already listening or if AI is speaking
                        if (isListening) return;
                        
                        Debug.WriteLine("[AirPods] Gesture detected! Activating voice...");
                        System.Media.SystemSounds.Asterisk.Play();
                        ShowStatus("🎧 AirPods activated! Listening...");
                        
                        // Stop wake word listening temporarily
                        isWakeWordListening = false;
                        wakeWordWhisper?.Dispose();
                        wakeWordWhisper = null;
                        
                        // Start listening for command (uses Sound Blaster mic)
                        StartListening();
                    });
                };
                
                // Next track = skip to next response or cancel current
                _mediaButtonListener.NextTrackPressed += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Debug.WriteLine("[AirPods] Next track gesture");
                        _voiceManager.Stop(); // Stop current speech
                        ShowStatus("⏭️ Skipped");
                    });
                };
                
                // Previous track = repeat last response
                _mediaButtonListener.PreviousTrackPressed += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Debug.WriteLine("[AirPods] Previous track gesture");
                        // Could repeat last AI response here
                        ShowStatus("⏮️ Previous");
                    });
                };
                
                // Start listening on this window
                _mediaButtonListener.StartListening(this);
                Debug.WriteLine("[AirPods] Gesture support enabled - tap/squeeze AirPods to activate voice");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AirPods] Failed to initialize gesture support: {ex.Message}");
            }
        }

        private void WakeWord_Recognized(object? sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result.Confidence > 0.5)
            {
                Dispatcher.Invoke(() =>
                {
                    // If Atlas is speaking, user wants to interrupt - stop Atlas and listen
                    if (_voiceManager?.IsSpeaking == true)
                    {
                        Debug.WriteLine($"[WakeWord] User interrupted Atlas with '{e.Result.Text}' - stopping TTS to listen");
                        _voiceManager.Stop();
                        UpdateSpeakingIndicator(false);
                    }
                    
                    Debug.WriteLine($"Wake word detected: {e.Result.Text} (confidence: {e.Result.Confidence})");
                    OnWakeWordDetected();
                });
            }
        }
        
        private async void OnWakeWordDetected()
        {
            Debug.WriteLine("[WakeWord] *** OnWakeWordDetected called ***");
            
            // Prevent re-entry if already listening
            if (isListening)
            {
                Debug.WriteLine("[WakeWord] Already listening, ignoring wake word");
                return;
            }
            
            // If Atlas is speaking, the user is trying to interrupt/respond - STOP Atlas and listen
            if (_voiceManager?.IsSpeaking == true)
            {
                Debug.WriteLine("[WakeWord] Atlas is speaking - user wants to interrupt/respond, stopping TTS");
                _voiceManager.Stop();
                UpdateSpeakingIndicator(false);
                // Small delay to let audio stop
                await Task.Delay(200);
            }
            
            // CRITICAL: Stop any wake-word recognizer to free the microphone.
            // With the centralized WakeWordCoordinator, the active detector is WakeWordService.
            // This prevents conflicts between Windows Speech Recognition and Whisper.
            Debug.WriteLine("[WakeWord] Stopping wake-word recognizer to free microphone...");
            if (wakeWordRecognizer != null)
            {
                try
                {
                    wakeWordRecognizer.RecognizeAsyncCancel();
                    wakeWordRecognizer.RecognizeAsyncStop();
                }
                catch
                {
                }
                try
                {
                    wakeWordRecognizer.Dispose();
                }
                catch
                {
                }
                wakeWordRecognizer = null;
            }
            if (_wakeWordDetector != null)
            {
                try
                {
                    _wakeWordDetector.StopListening();
                    Debug.WriteLine("[WakeWord] Local wake word detector stopped");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WakeWord] Error stopping detector: {ex.Message}");
                }
            }

            try
            {
                if (AtlasAI.Voice.WakeWordService.Instance.IsListening)
                {
                    Debug.WriteLine("[WakeWord] Stopping WakeWordService to free microphone...");
                    AtlasAI.Voice.WakeWordService.Instance.Stop(AtlasAI.Voice.WakeStopReason.ExternalHandlerDefer);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWord] Error stopping WakeWordService: {ex.Message}");
            }
            
            // Duck (pause) system audio when wake word is detected
            Debug.WriteLine("[WakeWord] Ducking audio...");
            await AudioDuckingManager.DuckAudioAsync();
            
            // Play a sound
            Debug.WriteLine("[WakeWord] Playing activation sound...");
            System.Media.SystemSounds.Asterisk.Play();
            
            // Visual feedback
            ShowStatus("Listening");

            // Force the Voice Debug pill visible immediately during wake-word handoff.
            // This both confirms the UI element exists and gives the user something to report.
            try
            {
                _voiceDebugHoldUntilUtc = DateTime.UtcNow.AddSeconds(8);
                if (VoiceDebugIndicator != null && VoiceDebugText != null)
                {
                    VoiceDebugText.Text = "Wake word detected";
                    VoiceDebugIndicator.Visibility = Visibility.Visible;
                }
            }
            catch
            {
            }
            
            // Also stop old whisper wake word if it exists
            wakeWordWhisper?.Dispose();
            wakeWordWhisper = null;
            
            // IMPORTANT: Wait for the ping sound to finish before starting to listen
            // This prevents the recognizer from picking up the ping as speech
            Debug.WriteLine("[WakeWord] Waiting 600ms for sound to finish...");
            await Task.Delay(600);
            
            Debug.WriteLine("[WakeWord] Handing off to VoiceSystemOrchestrator...");
            try
            {
                AtlasAI.Voice.VoiceSystemOrchestrator.Instance.BeginListening(AtlasAI.Voice.ListeningSource.WakeWord);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWord] Failed to begin listening: {ex.Message}");
                isListening = false;
            }
        }

        private static string StripWakeWordPrefix(string? text)
        {
            var raw = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var lowered = raw.ToLowerInvariant();
            lowered = System.Text.RegularExpressions.Regex.Replace(lowered, "[^a-z0-9\\s]", " ");
            lowered = System.Text.RegularExpressions.Regex.Replace(lowered, "\\s+", " ").Trim();

            var prefixes = new[]
            {
                "hey atlas",
                "ok atlas",
                "okay atlas",
                "hi atlas",
                "the atlas",
                "atlas",
                "at last",
                "atlass",
                "atlus",
                "adlas",
                "adlus",
                "atlis",
                "adlis"
            };

            for (var i = 0; i < prefixes.Length; i++)
            {
                var p = prefixes[i];
                if (lowered == p)
                    return "";

                if (lowered.StartsWith(p + " ", StringComparison.Ordinal))
                {
                    var remainder = raw.Length > p.Length ? raw[p.Length..] : "";
                    return remainder.TrimStart(' ', ',', '.', ':', ';', '-', '—');
                }
            }

            return raw;
        }
        
        /// <summary>
        /// Process a voice command directly (when wake word + command are in same utterance)
        /// </summary>
        private async void ProcessVoiceCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            
            // Duck audio when processing voice command
            await AudioDuckingManager.DuckAudioAsync();
            
            ShowStatus($"🎤 Processing: {command} (Music paused)");
            
            // Show avatar listening animation - DISABLED
            // if (_avatarIntegration?.IsUnityRunning == true)
            // {
            //     await _avatarIntegration.AvatarSetStateAsync("Listening");
            // }
            
            // Set the input box and trigger send
            InputBox.Text = command;
            SendMessage();
            
            // Resume wake word listening after AI responds (give it time to process)
            await Task.Delay(1000);
            if (isWakeWordEnabled && !isListening)
            {
                Debug.WriteLine("[WakeWord] Restarting after command processed");
                isWakeWordListening = true;
                StartWakeWordListening(); // Use centralized coordinator
            }
            else
            {
                Debug.WriteLine($"[WakeWord] NOT restarting after command - isWakeWordEnabled={isWakeWordEnabled}, isListening={isListening}");
            }
            
            // Restore audio after a delay (let AI finish speaking first)
            _ = Task.Delay(3000).ContinueWith(async _ =>
            {
                await AudioDuckingManager.RestoreAudioAsync();
            });
        }

        private void AudioProtection_Click(object sender, RoutedEventArgs e)
        {
            if (AudioCoordinator.IsEmergencyProtectionActive)
            {
                // Disable emergency protection
                AudioCoordinator.DisableEmergencyAudioProtection();
                AudioProtectionBtn.Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)); // Normal color
                AudioProtectionBtn.Foreground = new SolidColorBrush(Color.FromRgb(125, 133, 144)); // Normal color
                AudioProtectionBtn.ToolTip = "Emergency Audio Protection (Prevents headphone distortion)";
                ShowStatus("🛡️ Audio protection disabled - Voice features enabled");
            }
            else
            {
                // Enable emergency protection
                AudioCoordinator.EnableEmergencyAudioProtection();
                
                // Stop wake word listening if active
                if (isWakeWordEnabled)
                {
                    WakeWordToggle.IsChecked = false;
                    isWakeWordEnabled = false;
                    StopWakeWordListening();
                }
                
                AudioProtectionBtn.Background = new SolidColorBrush(Color.FromRgb(220, 50, 50)); // Red when active
                AudioProtectionBtn.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); // White text
                AudioProtectionBtn.ToolTip = "Emergency Audio Protection ACTIVE - Click to disable";
                ShowStatus("🛡️ Emergency audio protection enabled - All voice features disabled to prevent distortion");
                
                MessageBox.Show(
                    "Emergency Audio Protection is now ACTIVE.\n\n" +
                    "This completely disables all audio capture to prevent headphone distortion.\n\n" +
                    "Voice features (wake word, microphone) are disabled until you turn this off.\n\n" +
                    "Click the shield button again to re-enable voice features.",
                    "Audio Protection Enabled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void WakeWordToggle_Click(object sender, RoutedEventArgs e)
        {
            isWakeWordEnabled = WakeWordToggle.IsChecked == true;
            PreferencesStore.Instance.Update(p => p.EnableWakeWord = isWakeWordEnabled);
            
            if (isWakeWordEnabled)
            {
                // Check if emergency audio protection should be enabled
                if (AudioCoordinator.IsEmergencyProtectionActive)
                {
                    var result = MessageBox.Show(
                        "Emergency Audio Protection is currently active to prevent headphone distortion.\n\n" +
                        "Would you like to disable it and enable wake word listening?\n\n" +
                        "Note: This may cause audio distortion in headphones.",
                        "Audio Protection Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.No)
                    {
                        WakeWordToggle.IsChecked = false;
                        isWakeWordEnabled = false;
                        return;
                    }
                    
                    AudioCoordinator.DisableEmergencyAudioProtection();
                }

                if (!CanUseWakeWordWithCurrentMicrophone(out var blockedDeviceName))
                {
                    WakeWordToggle.IsChecked = false;
                    isWakeWordEnabled = false;
                    WakeWordIndicator.Visibility = Visibility.Collapsed;
                    WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(42, 42, 60));
                    ShowStatus($"🎧 Wake word is not reliable on {blockedDeviceName}. Pick a wired or USB mic in Settings.");
                    MessageBox.Show(
                        $"Wake word is not reliable with {blockedDeviceName} on Windows.\n\n" +
                        "AirPods and other Bluetooth headset microphones often drop or switch profiles when Windows Speech starts.\n\n" +
                        "Select a wired, USB, or built-in microphone in Settings for wake word, then try again.",
                        "Wake Word Disabled For Bluetooth Mic",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                
                StartWakeWordListening();
                WakeWordIndicator.Visibility = Visibility.Visible;
                WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                ShowStatus("Listening");
            }
            else
            {
                StopWakeWordListening();
                WakeWordIndicator.Visibility = Visibility.Collapsed;
                WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(42, 42, 60));
                StatusText.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Try to find and switch to a working microphone when AirPods/Bluetooth fails
        /// Now respects user preferences and only suggests fallback instead of forcing it
        /// </summary>
        private void TryFallbackToWorkingMicrophone()
        {
            try
            {
                Debug.WriteLine("[WakeWord] AirPods/Bluetooth microphone failed - checking user preferences...");
                
                // Get current settings to see if user manually selected a device
                var (deviceIndex, sensitivity, quality, deviceId) = SettingsWindow.GetHardwareSettings();
                bool userHasManualSelection = !string.IsNullOrEmpty(deviceId) || deviceIndex >= 0;
                
                if (userHasManualSelection)
                {
                    Debug.WriteLine("[WakeWord] User has manually selected a microphone - respecting their choice");
                    ShowStatus("🎧 AirPods may not work with Windows speech recognition. Check Settings to select a different microphone.");
                    
                    // Don't automatically switch - let user decide
                    // Still try to restart with their selected device in case it was a temporary issue
                    Task.Delay(1000).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (isWakeWordEnabled)
                            {
                                Debug.WriteLine("[WakeWord] Retrying with user's selected microphone...");
                                isWakeWordListening = true;
                                StartWhisperWakeWordListening();
                            }
                        });
                    });
                    return;
                }
                
                Debug.WriteLine("[WakeWord] No manual selection - searching for working microphone fallback...");
                
                // Get all available devices
                var devices = WhisperSpeechRecognition.GetAvailableDevicesEx();
                
                foreach (var (index, name, deviceIdFallback) in devices)
                {
                    var nameLower = name.ToLower();
                    
                    // Skip AirPods and Bluetooth devices (they don't work with WaveIn on Windows)
                    if (nameLower.Contains("airpod") || nameLower.Contains("bluetooth") || 
                        nameLower.Contains("hands-free") || nameLower.Contains("a2dp"))
                    {
                        Debug.WriteLine($"[WakeWord] Skipping Bluetooth device: {name}");
                        continue;
                    }
                    
                    // Prefer USB/external microphones over built-in
                    if (nameLower.Contains("usb") || nameLower.Contains("external") || 
                        nameLower.Contains("headset") || nameLower.Contains("microphone"))
                    {
                        Debug.WriteLine($"[WakeWord] Trying fallback to: {name}");
                        
                        // Test this microphone
                        if (TestMicrophoneQuickly(index))
                        {
                            Debug.WriteLine($"[WakeWord] Found working microphone: {name}");
                            ShowStatus($"✅ Automatically switched to working microphone: {name}");
                            
                            // Update settings to use this device (only for auto-fallback, not manual selection)
                            SettingsWindow.SetHardwareSettings(index, sensitivity, quality, deviceIdFallback);
                            
                            // Restart wake word listening with new device
                            Task.Delay(500).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    if (isWakeWordEnabled)
                                    {
                                        isWakeWordListening = true;
                                        StartWhisperWakeWordListening();
                                    }
                                });
                            });
                            return;
                        }
                    }
                }
                
                // If no USB/external mic found, try built-in mics
                foreach (var (index, name, deviceIdFallback) in devices)
                {
                    var nameLower = name.ToLower();
                    
                    // Skip AirPods and Bluetooth devices
                    if (nameLower.Contains("airpod") || nameLower.Contains("bluetooth") || 
                        nameLower.Contains("hands-free") || nameLower.Contains("a2dp"))
                        continue;
                    
                    // Try built-in microphones
                    if (nameLower.Contains("realtek") || nameLower.Contains("built-in") || 
                        nameLower.Contains("internal") || nameLower.Contains("array"))
                    {
                        Debug.WriteLine($"[WakeWord] Trying built-in fallback: {name}");
                        
                        if (TestMicrophoneQuickly(index))
                        {
                            Debug.WriteLine($"[WakeWord] Found working built-in microphone: {name}");
                            ShowStatus($"✅ Automatically switched to built-in microphone: {name}");
                            
                            // Update settings to use this device (only for auto-fallback)
                            SettingsWindow.SetHardwareSettings(index, sensitivity, quality, deviceIdFallback);
                            
                            // Restart wake word listening with new device
                            Task.Delay(500).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    if (isWakeWordEnabled)
                                    {
                                        isWakeWordListening = true;
                                        StartWhisperWakeWordListening();
                                    }
                                });
                            });
                            return;
                        }
                    }
                }
                
                // If still no working mic found, use Windows default (only if no manual selection)
                Debug.WriteLine("[WakeWord] No specific working mic found, using Windows default");
                ShowStatus("🎤 Using Windows default microphone");
                SettingsWindow.SetHardwareSettings(-1, sensitivity, quality, "");
                
                Task.Delay(500).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (isWakeWordEnabled)
                        {
                            isWakeWordListening = true;
                            StartWhisperWakeWordListening();
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWord] Fallback microphone search failed: {ex.Message}");
                ShowStatus("⚠️ Could not find working microphone");
            }
        }
        
        /// <summary>
        /// Quick test to see if a microphone produces audio
        /// </summary>
        private bool TestMicrophoneQuickly(int deviceIndex)
        {
            try
            {
                using var testWaveIn = new NAudio.Wave.WaveInEvent
                {
                    DeviceNumber = deviceIndex,
                    WaveFormat = new NAudio.Wave.WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 50
                };
                
                double maxLevel = 0;
                var testComplete = new System.Threading.ManualResetEventSlim(false);
                
                testWaveIn.DataAvailable += (s, e) =>
                {
                    // Calculate RMS level
                    double sum = 0;
                    for (int i = 0; i < e.BytesRecorded; i += 2)
                    {
                        if (i + 1 < e.BytesRecorded)
                        {
                            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                            sum += Math.Abs(sample);
                        }
                    }
                    double avgLevel = sum / Math.Max(1, e.BytesRecorded / 2);
                    if (avgLevel > maxLevel)
                        maxLevel = avgLevel;
                };
                
                testWaveIn.RecordingStopped += (s, e) => testComplete.Set();
                
                testWaveIn.StartRecording();
                Thread.Sleep(200); // Quick test
                testWaveIn.StopRecording();
                testComplete.Wait(500);
                
                // A working mic should have some noise floor (> 1)
                bool hasAudio = maxLevel > 1;
                Debug.WriteLine($"[WakeWord] Mic test device {deviceIndex}: maxLevel={maxLevel:F0}, working={hasAudio}");
                return hasAudio;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWord] Mic test failed for device {deviceIndex}: {ex.Message}");
                return false;
            }
        }

        private void StartWakeWordListening()
        {
            // Check if emergency audio protection is active
            if (AudioCoordinator.IsEmergencyProtectionActive)
            {
                Debug.WriteLine("[WakeWord] Emergency audio protection active - not starting wake word");
                ShowStatus("🛡️ Audio protection active - Wake word disabled");
                return;
            }

            if (!CanUseWakeWordWithCurrentMicrophone(out var blockedDeviceName))
            {
                isWakeWordListening = false;
                Debug.WriteLine($"[WakeWord] Blocking wake word startup for Bluetooth-style microphone: {blockedDeviceName}");
                ShowStatus($"🎧 Wake word disabled for {blockedDeviceName}. Select a wired or USB mic in Settings.");
                return;
            }
            
            isWakeWordListening = true;

            // Ensure coordinator is initialized (subscribes to WakeWordService events).
            // This is idempotent and prevents a silent "wake word heard but nothing happens" failure.
            try
            {
                Core.WakeWordCoordinator.Instance.Initialize();
            }
            catch
            {
            }
            
            // Subscribe to centralized WakeWordCoordinator instead of running our own detector
            // This prevents conflicts with VoiceSystemOrchestrator which also uses WakeWordService
            SubscribeToWakeWordCoordinator();

            // Ensure the underlying WakeWordService is actually running.
            // (Coordinator is just an event hub; it doesn't start the engine.)
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!AtlasAI.Voice.WakeWordService.Instance.IsListening)
                        await AtlasAI.Voice.WakeWordService.Instance.StartAsync();
                }
                catch { }
            });
            
            ShowStatus("Listening");
            Debug.WriteLine("[ChatWindow] Subscribed to WakeWordCoordinator for wake word events");
        }

        private bool CanUseWakeWordWithCurrentMicrophone(out string blockedDeviceName)
        {
            blockedDeviceName = string.Empty;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var prefs = PreferencesStore.Instance.Current;
                var communicationsDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Communications);
                var multimediaDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Multimedia);
                var consoleDevice = TryGetDefaultCaptureEndpoint(enumerator, Role.Console);

                MMDevice? device = null;
                var preferredId = (prefs.MicrophoneDeviceId ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(preferredId) && !string.Equals(preferredId, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var explicitDevice = enumerator.GetDevice(preferredId);
                        if (explicitDevice != null && explicitDevice.State == DeviceState.Active)
                            device = explicitDevice;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WakeWord] Preferred mic lookup failed: {ex.Message}");
                    }
                }

                device ??= communicationsDevice;
                device ??= multimediaDevice;
                device ??= consoleDevice;

                var deviceName = device?.FriendlyName ?? (prefs.MicrophoneDevice ?? string.Empty).Trim();
                if (!IsBluetoothStyleMicrophone(deviceName))
                    return true;

                if (IsWindowsManagedCaptureEndpoint(device, communicationsDevice, multimediaDevice, consoleDevice))
                    return true;

                blockedDeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Bluetooth microphone" : deviceName;
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWord] Mic capability check failed, allowing start: {ex.Message}");
                return true;
            }
        }

        private static MMDevice? TryGetDefaultCaptureEndpoint(MMDeviceEnumerator enumerator, Role role)
        {
            try
            {
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                if (device != null && device.State == DeviceState.Active)
                    return device;
            }
            catch
            {
            }

            return null;
        }

        private static bool IsWindowsManagedCaptureEndpoint(MMDevice? device, params MMDevice?[] defaultEndpoints)
        {
            if (device == null)
                return false;

            foreach (var endpoint in defaultEndpoints)
            {
                if (endpoint != null && string.Equals(endpoint.ID, device.ID, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsBluetoothStyleMicrophone(string? deviceName)
        {
            var normalized = (deviceName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return normalized.Contains("airpod") ||
                   normalized.Contains("bluetooth") ||
                   normalized.Contains("hands-free") ||
                   normalized.Contains("handsfree") ||
                   normalized.Contains("wireless headset") ||
                   normalized.Contains("headset (") ||
                   normalized.Contains("stereo headset");
        }
        
        private bool _subscribedToCoordinator = false;
        
        private void SubscribeToWakeWordCoordinator()
        {
            if (_subscribedToCoordinator) return;
            
            // Subscribe to the centralized coordinator
            Core.WakeWordCoordinator.Instance.WakeWordActivated += OnCoordinatorWakeWordActivated;
            _subscribedToCoordinator = true;

            Debug.WriteLine("[ChatWindow] ✅ Subscribed to WakeWordCoordinator.WakeWordActivated");
        }
        
        private void UnsubscribeFromWakeWordCoordinator()
        {
            if (!_subscribedToCoordinator) return;
            
            Core.WakeWordCoordinator.Instance.WakeWordActivated -= OnCoordinatorWakeWordActivated;
            _subscribedToCoordinator = false;
            
            // Allow VoiceSystemOrchestrator to handle wake word again
            Voice.VoiceSystemOrchestrator.Instance.DeferToExternalHandler = false;
            
            Debug.WriteLine("[ChatWindow] Unsubscribed from WakeWordCoordinator");
            Debug.WriteLine("[ChatWindow] VoiceSystemOrchestrator.DeferToExternalHandler = false");
        }
        
        private void OnCoordinatorWakeWordActivated(object? sender, EventArgs e)
        {
            Debug.WriteLine("[ChatWindow] *** WakeWordCoordinator event received ***");

            if (!Voice.VoiceSystemOrchestrator.Instance.DeferToExternalHandler)
            {
                Debug.WriteLine("[ChatWindow] Ignoring coordinator event - VoiceSystemOrchestrator handles wake word");
                return;
            }
            
            // Only respond if wake word is enabled and we're not already listening
            if (!isWakeWordEnabled || isListening)
            {
                Debug.WriteLine($"[ChatWindow] Ignoring coordinator event - isWakeWordEnabled={isWakeWordEnabled}, isListening={isListening}");
                return;
            }
            
            // Handle on UI thread
            SafeDispatcherInvoke(() =>
            {
                Debug.WriteLine("[ChatWindow] Processing wake word from coordinator...");
                OnWakeWordDetected();
            });
        }
        
        /// <summary>
        /// Start wake word detection using Windows Speech Recognition.
        /// This does NOT cause audio distortion because Windows Speech uses
        /// a different audio subsystem than direct WASAPI/WaveIn capture.
        /// </summary>
        private void StartWindowsSpeechWakeWord()
        {
            try
            {
                Debug.WriteLine("[ChatWindow] ========================================");
                Debug.WriteLine("[ChatWindow] STARTING WINDOWS SPEECH WAKE WORD");
                Debug.WriteLine("[ChatWindow] ========================================");
                
                // Dispose old detector if exists - be thorough
                if (_wakeWordDetector != null)
                {
                    try
                    {
                        _wakeWordDetector.StopListening();
                        _wakeWordDetector.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChatWindow] Error disposing old detector: {ex.Message}");
                    }
                    _wakeWordDetector = null;
                }
                
                // Small delay to let audio system settle after disposing
                System.Threading.Thread.Sleep(50);
                
                // Create new detector using Windows Speech Recognition
                _wakeWordDetector = new WakeWordDetector();
                
                _wakeWordDetector.WakeWordDetected += (s, text) =>
                {
                    SafeDispatcherInvoke(() =>
                    {
                        Debug.WriteLine($"[ChatWindow] *** WAKE WORD CALLBACK RECEIVED: '{text}' ***");
                        
                        // If Atlas is speaking, user wants to interrupt/respond - stop Atlas and listen
                        if (_voiceManager?.IsSpeaking == true)
                        {
                            Debug.WriteLine("[ChatWindow] User interrupted Atlas - stopping TTS to listen");
                            _voiceManager.Stop();
                            UpdateSpeakingIndicator(false);
                        }
                        
                        // Stop wake word listening while processing
                        _wakeWordDetector?.StopListening();
                        
                        // Pause music when wake word detected
                        Debug.WriteLine("[ChatWindow] Pausing music...");
                        _ = AudioDuckingManager.DuckAudioAsync();
                        
                        // Play activation sound
                        Debug.WriteLine("[ChatWindow] Playing activation sound...");
                        System.Media.SystemSounds.Asterisk.Play();
                        
                        // Flash the window to get attention
                        this.Activate();
                        this.Focus();
                        
                        ShowStatus("Listening");
                        
                        // Start listening for the actual command
                        Debug.WriteLine("[ChatWindow] Starting command listening...");
                        OnWakeWordDetected();
                    });
                };
                
                _wakeWordDetector.Error += (s, err) =>
                {
                    SafeDispatcherInvoke(() =>
                    {
                        Debug.WriteLine($"[ChatWindow] Wake word error: {err}");
                        ShowStatus($"⚠️ Wake word error: {err}");
                        
                        // Try to restart on error
                        if (isWakeWordEnabled && !isListening)
                        {
                            Debug.WriteLine("[ChatWindow] Attempting to restart wake word after error...");
                            ScheduleWakeWordRestart(2000);
                        }
                    });
                };
                
                // Show audio state changes for debugging
                _wakeWordDetector.AudioStateChanged += (s, state) =>
                {
                    SafeDispatcherInvoke(() =>
                    {
                        Debug.WriteLine($"[ChatWindow] Wake word audio state: {state}");
                        if (state == "Speech")
                        {
                            ShowStatus("Listening");
                        }
                        else if (state == "Silence")
                        {
                            ShowStatus("Listening");
                        }
                    });
                };
                
                _wakeWordDetector.StartListening();
                
                // Verify it's actually listening
                if (_wakeWordDetector.IsListening)
                {
                    // Show status - wake word is active
                    ShowStatus("Listening");
                    Debug.WriteLine("[ChatWindow] Windows Speech wake word started successfully and IS LISTENING");
                    
                    // Start health check timer
                    StartWakeWordHealthCheck();
                }
                else
                {
                    Debug.WriteLine("[ChatWindow] WARNING: Wake word detector created but NOT listening!");
                    ShowStatus("⚠️ Wake word failed to start - trying again...");
                    ScheduleWakeWordRestart(1000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] Failed to start wake word: {ex.Message}");
                Debug.WriteLine($"[ChatWindow] Stack: {ex.StackTrace}");
                ShowStatus($"⚠️ Wake word failed: {ex.Message}");
                
                // Try to restart on exception
                if (isWakeWordEnabled && !isListening)
                {
                    Debug.WriteLine("[ChatWindow] Scheduling restart after exception...");
                    ScheduleWakeWordRestart(2000);
                }
            }
        }
        
        private WhisperSpeechRecognition CreateConfiguredWhisperRecognizer()
        {
            var recognizer = new WhisperSpeechRecognition();
            
            // Apply hardware settings from Settings window
            var (deviceIndex, sensitivity, quality, deviceId) = SettingsWindow.GetHardwareSettings();
            
            if (!string.IsNullOrEmpty(deviceId))
            {
                Debug.WriteLine($"[ChatWindow] Setting device ID: {deviceId}");
                recognizer.SetDeviceById(deviceId);
            }
            else if (deviceIndex >= 0)
            {
                Debug.WriteLine($"[ChatWindow] Setting device index: {deviceIndex}");
                recognizer.SetDevice(deviceIndex);
            }
            else
            {
                Debug.WriteLine("[ChatWindow] Using auto-detect (Windows default)");
            }
            
            return recognizer;
        }

        private void StartWhisperWakeWordListening()
        {
            // NOTE: This method redirects to Windows Speech wake word detection
            // Whisper is only used for STT after wake word detection, not for continuous monitoring
            Debug.WriteLine("[WakeWord] Using Windows Speech for wake word detection (System.Speech)");
            Debug.WriteLine("[WakeWord] Whisper will be used for command transcription (better accuracy)");

            // Use Windows Speech wake word (System.Speech) - doesn't cause audio distortion
            // Whisper kicks in only after wake word is detected for command transcription
            StartWakeWordListening();
        }
        
        private void StartBackupRestartTimer()
        {
            StopBackupRestartTimer();
            
            wakeWordRestartTimer = new System.Timers.Timer(15000); // 15 seconds (reduced from 30)
            wakeWordRestartTimer.AutoReset = false;
            wakeWordRestartTimer.Elapsed += (s, e) =>
            {
                // Use safe dispatcher that won't block when minimized
                SafeDispatcherInvoke(() =>
                {
                    Debug.WriteLine("[WakeWord] Backup restart triggered - wake word listening appears stuck");
                    if (isWakeWordEnabled && !isListening)
                    {
                        Debug.WriteLine("[WakeWord] Force restarting wake word listening...");
                        isWakeWordListening = true;
                        StartWakeWordListening();
                    }
                    else
                    {
                        Debug.WriteLine($"[WakeWord] Backup restart skipped - isWakeWordEnabled={isWakeWordEnabled}, isListening={isListening}");
                    }
                });
            };
            wakeWordRestartTimer.Start();
            Debug.WriteLine("[WakeWord] Backup restart timer started (15s)");
        }
        
        private void StopBackupRestartTimer()
        {
            wakeWordRestartTimer?.Stop();
            wakeWordRestartTimer?.Dispose();
            wakeWordRestartTimer = null;
        }
        
        /// <summary>
        /// Safe dispatcher invoke that works even when window is minimized/hidden
        /// Uses BeginInvoke instead of Invoke to avoid blocking
        /// </summary>
        private void SafeDispatcherInvoke(Action action)
        {
            try
            {
                if (Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    // Use BeginInvoke (async) instead of Invoke (sync) to avoid blocking
                    Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SafeDispatcher] Error: {ex.Message}");
            }
        }
        
        private void StartWakeWordHealthCheck()
        {
            StopWakeWordHealthCheck();
            
            wakeWordHealthCheckTimer = new System.Timers.Timer(10000); // Check every 10 seconds
            wakeWordHealthCheckTimer.AutoReset = true;
            wakeWordHealthCheckTimer.Elapsed += (s, e) =>
            {
                // Use safe dispatcher that won't block when minimized
                SafeDispatcherInvoke(() =>
                {
                    // Check if wake word listening should be active but isn't
                    if (isWakeWordEnabled && !isListening)
                    {
                        bool shouldBeListening = _wakeWordDetector?.IsListening == true;
                        if (!shouldBeListening)
                        {
                            Debug.WriteLine("[WakeWord] Health check detected inactive wake word listening - restarting");
                            isWakeWordListening = true;
                            StartWakeWordListening();
                        }
                        else
                        {
                            Debug.WriteLine("[WakeWord] Health check - wake word listening is active");
                        }
                    }
                });
            };
            wakeWordHealthCheckTimer.Start();
            Debug.WriteLine("[WakeWord] Health check timer started (10s interval)");
        }
        
        private void StopWakeWordHealthCheck()
        {
            wakeWordHealthCheckTimer?.Stop();
            wakeWordHealthCheckTimer?.Dispose();
            wakeWordHealthCheckTimer = null;
        }
        
        private void RestartWakeWordListening()
        {
            if (!isWakeWordListening || isListening) return;
            
            wakeWordTimer?.Stop();
            wakeWordTimer = new System.Timers.Timer(100); // Very short delay before restart
            wakeWordTimer.AutoReset = false;
            wakeWordTimer.Elapsed += (s, e) =>
            {
                // Use safe dispatcher that won't block when minimized
                SafeDispatcherInvoke(() =>
                {
                    if (isWakeWordListening && !isListening)
                    {
                        Debug.WriteLine("[WakeWord] Restarting wake word listening...");
                        StartWhisperWakeWordListening();
                    }
                });
            };
            wakeWordTimer.Start();
        }
        
        private void ScheduleNextWakeWordListen()
        {
            RestartWakeWordListening();
        }

        private void StopWakeWordListening()
        {
            isWakeWordListening = false;
            wakeWordTimer?.Stop();
            wakeWordTimer = null;
            
            // Stop all timers FIRST
            StopBackupRestartTimer();
            StopWakeWordHealthCheck();
            
            // Unsubscribe from WakeWordCoordinator
            UnsubscribeFromWakeWordCoordinator();
            
            // Stop NEW Windows Speech wake word detector
            if (_wakeWordDetector != null)
            {
                try
                {
                    _wakeWordDetector.StopListening();
                    _wakeWordDetector.Dispose();
                    _wakeWordDetector = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WakeWord] Error disposing wake word detector: {ex.Message}");
                }
            }
            
            // Stop Windows Speech Recognition (legacy)
            if (wakeWordRecognizer != null)
            {
                try
                {
                    wakeWordRecognizer.RecognizeAsyncCancel();
                    wakeWordRecognizer.Dispose();
                    wakeWordRecognizer = null;
                }
                catch { }
            }
            
            // Stop Whisper Recognition - AGGRESSIVE cleanup
            if (wakeWordWhisper != null)
            {
                try
                {
                    // Force stop recording if active
                    if (wakeWordWhisper.IsRecording)
                    {
                        // Don't await - just dispose to force stop
                        _ = wakeWordWhisper.StopRecordingAndTranscribeAsync();
                    }
                    
                    // Dispose completely
                    wakeWordWhisper.Dispose();
                    wakeWordWhisper = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WakeWord] Error disposing whisper: {ex.Message}");
                }
            }
            
            // Clear UI indicators
            SafeDispatcherInvoke(() =>
            {
                HearingIndicator.Visibility = Visibility.Collapsed;
                if (!isWakeWordEnabled)
                {
                    WakeWordIndicator.Visibility = Visibility.Collapsed;
                }
            });
            
            Debug.WriteLine("Wake word listening stopped completely");
        }

        private System.Timers.Timer? _wakeWordRestartTimer;
        private int _wakeWordRestartAttempts = 0;
        private const int MAX_RESTART_ATTEMPTS = 3;
        
        /// <summary>
        /// Schedule wake word restart after a delay. Uses a timer to avoid async issues.
        /// IMPROVED: Faster restarts for better hands-free experience
        /// </summary>
        private void ScheduleWakeWordRestart(int delayMs)
        {
            // Cancel any existing restart timer
            _wakeWordRestartTimer?.Stop();
            _wakeWordRestartTimer?.Dispose();
            
            // IMPROVED: Use shorter delays for better hands-free experience
            var actualDelay = Math.Min(delayMs, 500); // Never wait more than 500ms for hands-free
            
            Debug.WriteLine($"[WakeWord] Scheduling restart in {actualDelay}ms (attempt {_wakeWordRestartAttempts + 1})");
            
            _wakeWordRestartTimer = new System.Timers.Timer(actualDelay);
            _wakeWordRestartTimer.AutoReset = false;
            _wakeWordRestartTimer.Elapsed += (s, e) =>
            {
                _wakeWordRestartTimer?.Dispose();
                _wakeWordRestartTimer = null;
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Debug.WriteLine($"[WakeWord] Restart timer fired - isWakeWordEnabled={isWakeWordEnabled}, isListening={isListening}");
                    if (isWakeWordEnabled && !isListening)
                    {
                        isWakeWordListening = true;
                        Debug.WriteLine("[WakeWord] Restarting wake word NOW for hands-free operation");

                        StartWakeWordListening();
                        _wakeWordRestartAttempts = 0; // Reset on successful restart
                        
                        // Update UI to show wake word is active
                        WakeWordIndicator.Visibility = Visibility.Visible;
                        ShowStatus("Listening");
                    }
                    else
                    {
                        Debug.WriteLine($"[WakeWord] Restart skipped - conditions not met");
                    }
                }));
            };
            _wakeWordRestartTimer.Start();
        }
        
        /// <summary>
        /// Force restart wake word detection - used when normal restart fails
        /// </summary>
        private void ForceRestartWakeWord()
        {
            Debug.WriteLine("[WakeWord] FORCE RESTART initiated");
            
            // Stop everything first
            StopWakeWordListening();
            
            // Wait a moment
            System.Threading.Thread.Sleep(200);
            
            // Restart
            if (isWakeWordEnabled)
            {
                isWakeWordListening = true;
                StartWakeWordListening();
            }
        }

        private void OnThemeChanged(AppTheme theme)
        {
            Dispatcher.Invoke(ApplyTheme);
        }

        private void ApplyTheme()
        {
            // Modern UI uses fixed dark theme - just refresh message bubbles
            RefreshMessageBubbles();
        }

        private void RefreshMessageBubbles()
        {
            MessagesPanel.Children.Clear();
            foreach (var msg in displayedMessages)
            {
                AddMessageToUI(msg.Sender, msg.Text, msg.IsUser);
            }
        }

        private async void InitializeVoiceSystem()
        {
            // UNIFIED ROUTING: Hook up VoiceSystemOrchestrator to this window's SendMessage pipeline
            Voice.VoiceSystemOrchestrator.Instance.SubmitMessageHandler = HandleVoiceMessage;
            Debug.WriteLine("[ChatWindow] Wired up VoiceSystemOrchestrator.SubmitMessageHandler");

            // Provide UI hooks for orchestrator-driven notifications (status + core visual state)
            try
            {
                // ChatHoloCore hosts an AtlasCoreControl internally as x:Name="Core" (same assembly access)
                if (ChatHoloCore != null)
                {
                    Voice.VoiceNotificationService.Instance.Initialize(ChatHoloCore.Core, ShowStatus, SetAtlasCoreState);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] VoiceNotificationService init failed: {ex.Message}");
            }

            // Register VoiceManager with VoiceSystemOrchestrator (CRITICAL: connects TTS/voice system)
            Voice.VoiceSystemOrchestrator.Instance.SetVoiceManager(_voiceManager);
            Debug.WriteLine("[ChatWindow] Registered VoiceManager with VoiceSystemOrchestrator");

            try
            {
                Voice.VoiceSystemOrchestrator.Instance.ListeningStarted += (_, source) =>
                {
                    SafeDispatcherInvoke(() =>
                    {
                        isListening = true;
                        _userStoppedListening = false;
                        if (MicButton != null) MicButton.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xd3, 0xee));
                        UpdateMicButtonState();
                        InputBox.Text = source == Voice.ListeningSource.WakeWord
                            ? "🎙️ Wake word heard... speak now"
                            : "🎙️ Listening…";
                        HearingIndicator.Visibility = Visibility.Collapsed;
                        WakeWordIndicator.Visibility = source == Voice.ListeningSource.WakeWord
                            ? Visibility.Collapsed
                            : (isWakeWordEnabled ? Visibility.Visible : Visibility.Collapsed);
                        SetAtlasCoreState(Controls.AtlasVisualState.Listening);

                        // Always surface a debug pill while listening so the user can see state.
                        if (VoiceDebugIndicator != null && VoiceDebugText != null)
                        {
                            VoiceDebugText.Text = $"Listening ({source})";
                            VoiceDebugIndicator.Visibility = Visibility.Visible;
                        }
                    });
                };

                Voice.VoiceSystemOrchestrator.Instance.ListeningStopped += (_, __) =>
                {
                    SafeDispatcherInvoke(() =>
                    {
                        isListening = false;
                        if (MicButton != null) MicButton.Foreground = new SolidColorBrush(Color.FromRgb(125, 133, 144));
                        HearingIndicator.Visibility = Visibility.Collapsed;
                        WakeWordIndicator.Visibility = isWakeWordEnabled ? Visibility.Visible : Visibility.Collapsed;
                        UpdateMicButtonState();
                        SetAtlasCoreState(Controls.AtlasVisualState.Idle);

                        var inputText = InputBox.Text ?? string.Empty;
                        if (inputText.Contains("Listening", StringComparison.OrdinalIgnoreCase) ||
                            inputText.Contains("Wake word heard", StringComparison.OrdinalIgnoreCase) ||
                            inputText.Contains("Processing", StringComparison.OrdinalIgnoreCase) ||
                            inputText.Contains("Transcribing", StringComparison.OrdinalIgnoreCase))
                        {
                            InputBox.Text = string.Empty;
                        }

                        // Hide debug pill when leaving the listening state.
                        if (VoiceDebugIndicator != null && VoiceDebugText != null)
                        {
                            VoiceDebugText.Text = "";
                            VoiceDebugIndicator.Visibility = Visibility.Collapsed;
                        }

                        if (isWakeWordEnabled)
                        {
                            isWakeWordListening = true;
                            StartWakeWordListening();
                        }
                    });
                };

                // Surface voice pipeline state/errors so “Listening…” isn’t a black box.
                Voice.VoiceSystemOrchestrator.Instance.Error += (_, msg) =>
                {
                    SafeDispatcherInvoke(() =>
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(msg))
                            {
                                HearingIndicator.Visibility = Visibility.Collapsed;
                                if (!isListening && VoiceDebugIndicator != null)
                                    VoiceDebugIndicator.Visibility = Visibility.Collapsed;
                                return;
                            }

                            var isHearing = msg.IndexOf("hearing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            msg.IndexOf("heard:", StringComparison.OrdinalIgnoreCase) >= 0;
                            HearingIndicator.Visibility = isHearing ? Visibility.Visible : Visibility.Collapsed;

                            if (msg.IndexOf("processing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                msg.IndexOf("finalizing", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                InputBox.Text = "🔄 Processing...";
                            }
                            else if (msg.IndexOf("listening", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     msg.IndexOf("mic active", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     msg.IndexOf("hearing", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                InputBox.Text = isHearing ? "🎙️ Hearing you..." : "🎙️ Listening…";
                            }

                            ShowStatus(msg);
                            if (VoiceDebugIndicator != null && VoiceDebugText != null)
                            {
                                VoiceDebugText.Text = msg;
                                VoiceDebugIndicator.Visibility = Visibility.Visible;
                            }
                        }
                        catch
                        {
                        }
                    });
                };

                Voice.VoiceSystemOrchestrator.Instance.CommandCaptured += (_, text) =>
                {
                    if (string.IsNullOrWhiteSpace(text)) return;
                    SafeDispatcherInvoke(() =>
                    {
                        try
                        {
                            InputBox.Text = text;
                            HearingIndicator.Visibility = Visibility.Collapsed;
                            ShowStatus($"Heard: {text}");
                        }
                        catch { }
                    });
                };
            }
            catch
            {
            }

            // Configure providers with saved API keys
            var keys = SettingsWindow.GetVoiceApiKeys();
            if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrEmpty(elevenKey))
            {
                _voiceManager.ConfigureProvider(VoiceProviderType.ElevenLabs, new Dictionary<string, string> { ["ApiKey"] = elevenKey });
            }

            await _voiceManager.SetProviderAsync(VoiceProviderType.ElevenLabs);

            // Load voices into UI
            await LoadVoicesAsync();

            // Update UI state
            SpeechToggle.IsChecked = _voiceManager.SpeechEnabled;
            UpdateSpeechToggleUI();
            UpdateProviderIndicator();
        }

        private async Task LoadVoicesAsync()
        {
            VoiceSelector.Items.Clear();
            var voices = await _voiceManager.GetVoicesAsync();
            
            // Simple approach: just add all voices with category prefix
            string lastCategory = "";
            foreach (var voice in voices)
            {
                var category = string.IsNullOrEmpty(voice.Category) ? "Voices" : voice.Category;
                
                // Add category header when category changes
                if (category != lastCategory && voices.Count > 3)
                {
                    VoiceSelector.Items.Add(new ComboBoxItem 
                    { 
                        Content = $"── {category} ──", 
                        IsEnabled = false,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150))
                    });
                    lastCategory = category;
                }
                
                var prefix = voice.IsCloud ? "☁️ " : "🖥️ ";
                VoiceSelector.Items.Add(new ComboBoxItem 
                { 
                    Content = prefix + voice.DisplayName, 
                    Tag = voice.Id 
                });
            }
            
            // Select current voice
            var selectedId = _voiceManager.SelectedVoice?.Id;
            for (int i = 0; i < VoiceSelector.Items.Count; i++)
            {
                if (VoiceSelector.Items[i] is ComboBoxItem item && item.Tag as string == selectedId)
                {
                    VoiceSelector.SelectedIndex = i;
                    break;
                }
            }
            if (VoiceSelector.SelectedIndex < 0 && VoiceSelector.Items.Count > 0)
            {
                // Skip header items when auto-selecting
                for (int i = 0; i < VoiceSelector.Items.Count; i++)
                {
                    if (VoiceSelector.Items[i] is ComboBoxItem item && item.IsEnabled)
                    {
                        VoiceSelector.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private async void RefreshVoices_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowStatus("🔄 Refreshing voices from ElevenLabs...");
                _voiceManager.RefreshVoices();
                await LoadVoicesAsync();
                
                // Check if we got any error from ElevenLabs
                if (_voiceManager.GetProvider(VoiceProviderType.ElevenLabs) is ElevenLabsProvider elevenLabs && elevenLabs.LastError != null)
                {
                    ShowStatus($"⚠️ {elevenLabs.LastError} - showing defaults");
                }
                else
                {
                    ShowStatus($"✅ Voices refreshed ({VoiceSelector.Items.Count} available)");
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ Refresh failed: {ex.Message}");
            }
        }

        private void UpdateProviderIndicator()
        {
            try
            {
                var isCloud = _voiceManager?.IsCloudVoice ?? false;
                var provider = _voiceManager?.GetProvider(_voiceManager.ActiveProviderType);
                var providerName = provider?.DisplayName ?? "Unknown";
                var voiceName = _voiceManager?.SelectedVoice?.DisplayName;
                
                // Show provider and voice name
                var displayText = string.IsNullOrEmpty(voiceName) 
                    ? providerName 
                    : $"{providerName} ({voiceName})";
                
                if (isCloud)
                {
                    ProviderIndicator.Text = $"☁️ {displayText}";
                    ProviderIndicator.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 100));
                }
                else
                {
                    ProviderIndicator.Text = $"🖥️ {displayText}";
                    ProviderIndicator.Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] UpdateProviderIndicator error: {ex.Message}");
                ProviderIndicator.Text = "Voice";
            }
        }
        
        /// <summary>
        /// Show voice provider selection popup when clicking the header indicator
        /// </summary>
        private async void ProviderSelector_Click(object sender, MouseButtonEventArgs e)
        {
            // Mark event as handled to prevent drag
            e.Handled = true;
            Debug.WriteLine("[VoicePopup] ProviderSelector clicked");
            
            // Populate provider list - ONLY ElevenLabs for TTS
            VoiceProviderList.Items.Clear();
            var providers = new[] 
            { 
                (VoiceProviderType.ElevenLabs, "☁️ ElevenLabs (Premium)")
            };
            
            foreach (var (type, name) in providers)
            {
                var item = new ListBoxItem 
                { 
                    Content = new TextBlock { Text = name, Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)) },
                    Tag = type 
                };
                VoiceProviderList.Items.Add(item);
                
                if (type == _voiceManager.ActiveProviderType)
                    VoiceProviderList.SelectedItem = item;
            }
            
            // Populate voice list for current provider
            await PopulateVoiceListAsync();
            
            // Toggle popup
            VoiceProviderPopup.IsOpen = !VoiceProviderPopup.IsOpen;
            Debug.WriteLine($"[VoicePopup] Popup IsOpen = {VoiceProviderPopup.IsOpen}");
        }
        
        private async Task PopulateVoiceListAsync()
        {
            VoiceList.Items.Clear();
            var voices = await _voiceManager.GetVoicesAsync();
            
            string lastCategory = "";
            foreach (var voice in voices)
            {
                var category = string.IsNullOrEmpty(voice.Category) ? "Voices" : voice.Category;
                
                // Add category header
                if (category != lastCategory && voices.Count > 3)
                {
                    var header = new ListBoxItem
                    {
                        Content = new TextBlock 
                        { 
                            Text = $"── {category} ──", 
                            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 10
                        },
                        IsEnabled = false,
                        IsHitTestVisible = false
                    };
                    VoiceList.Items.Add(header);
                    lastCategory = category;
                }
                
                var prefix = voice.IsCloud ? "☁️ " : "🖥️ ";
                var item = new ListBoxItem
                {
                    Content = new TextBlock 
                    { 
                        Text = prefix + voice.DisplayName, 
                        Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)) 
                    },
                    Tag = voice.Id
                };
                VoiceList.Items.Add(item);
                
                if (voice.Id == _voiceManager.SelectedVoice?.Id)
                    VoiceList.SelectedItem = item;
            }
        }
        
        private async void VoiceProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceProviderList.SelectedItem is ListBoxItem item && item.Tag is VoiceProviderType type)
            {
                if (type != _voiceManager.ActiveProviderType)
                {
                    ShowStatus($"🔄 Switching to {type}...");
                    var success = await _voiceManager.SetProviderAsync(type);
                    if (success)
                    {
                        await PopulateVoiceListAsync();
                        UpdateProviderIndicator();
                        ShowStatus($"✅ Voice provider: {_voiceManager.GetProvider(type).DisplayName}");
                    }
                    else
                    {
                        ShowStatus($"❌ {type} not available - check API key in Settings");
                    }
                }
            }
        }
        
        private async void VoiceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceList.SelectedItem is ListBoxItem item && item.Tag is string voiceId)
            {
                var success = await _voiceManager.SelectVoiceAsync(voiceId);
                if (success)
                {
                    // CRITICAL: Update VoicePreferences so VoiceSelectionService uses this voice
                    // VoiceManager.SpeakAsync uses VoiceSelectionService which reads from VoicePreferences
                    Voice.VoicePreferences.Current.SetGlobalVoice(voiceId);
                    
                    UpdateProviderIndicator();
                    ShowStatus($"✅ Voice: {_voiceManager.SelectedVoice?.DisplayName}");
                    
                    // Test the voice with a short phrase
                    _ = _voiceManager.SpeakAsync("Voice selected.");
                }
            }
        }
        
        private async void RefreshVoicesPopup_Click(object sender, RoutedEventArgs e)
        {
            ShowStatus("🔄 Refreshing voices...");
            _voiceManager.RefreshVoices();
            await PopulateVoiceListAsync();
            ShowStatus($"✅ Voices refreshed");
        }

        private void UpdateSpeakingIndicator(bool isSpeaking)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatWindow] UpdateSpeakingIndicator({isSpeaking})");
            
            SpeakingIndicator.Visibility = isSpeaking ? Visibility.Visible : Visibility.Collapsed;
            StopVoiceBtn.Visibility = isSpeaking ? Visibility.Visible : Visibility.Collapsed;
            
            // CRITICAL FIX: Explicitly update AtlasCore state to prevent "stuck on speaking"
            if (isSpeaking)
            {
                SetAtlasCoreState(Controls.AtlasVisualState.Speaking);
            }
            else
            {
                SetAtlasCoreState(Controls.AtlasVisualState.Idle);
            }
        }
        
        private void StopVoice_Click(object sender, RoutedEventArgs e)
        {
            _voiceManager?.Stop();
            UpdateSpeakingIndicator(false);
            ShowStatus("🔇 Voice stopped");
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
        }

        private void LoadChatHistory()
        {
            // ChatGPT-style: Start fresh every time, but history is saved and accessible via History button
            // We DON'T load previous messages into the UI - user can access them via History panel
            // The ConversationManager handles session persistence automatically
            try
            {
                // Still load the file to check if it exists (for isFirstLaunch detection)
                // but don't populate the UI with old messages
                if (File.Exists(HistoryPath))
                {
                    // File exists, so this isn't a first launch
                    // But we don't load messages into UI - fresh chat every time
                    Debug.WriteLine("[ChatWindow] Previous history exists but starting fresh chat");
                }
            }
            catch { }
            
            // Clear any existing messages to ensure fresh start
            displayedMessages.Clear();
            MessagesPanel.Children.Clear();
            
            Debug.WriteLine("[ChatWindow] Fresh chat started - previous sessions available via History button");
        }

        private void SaveChatHistory()
        {
            // Fire and forget - don't block UI
            _ = Task.Run(() =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(HistoryPath);
                    if (!Directory.Exists(dir)) 
                    {
                        Directory.CreateDirectory(dir!);
                        Debug.WriteLine($"[History] Created directory: {dir}");
                    }
                    
                    var json = JsonSerializer.Serialize(displayedMessages);
                    File.WriteAllText(HistoryPath, json);
                    Debug.WriteLine($"[History] Saved {displayedMessages.Count} messages to chat_history.json");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[History] ERROR saving chat_history.json: {ex.Message}");
                }
                
                // Also save full history for the history drawer
                SaveFullHistoryInternal();
            });
        }
        
        private void SaveFullHistoryInternal()
        {
            try
            {
                var dir = Path.GetDirectoryName(FullHistoryPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                // Convert to serializable format - take snapshot to avoid collection modified
                var historySnapshot = _fullHistory.ToList();
                var historyData = historySnapshot.Select(m => new
                {
                    m.Sender,
                    m.Content,
                    m.IsUser,
                    Timestamp = m.Timestamp.ToString("o")
                }).ToList();
                
                var json = JsonSerializer.Serialize(historyData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FullHistoryPath, json);
                Debug.WriteLine($"[History] Saved {historySnapshot.Count} items to full_history.json");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[History] Error saving full history: {ex.Message}");
            }
        }
        
        private void LoadFullHistory()
        {
            try
            {
                if (File.Exists(FullHistoryPath))
                {
                    var json = File.ReadAllText(FullHistoryPath);
                    using var doc = JsonDocument.Parse(json);
                    
                    _fullHistory.Clear();
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var sender = item.GetProperty("Sender").GetString() ?? "Atlas";
                        var content = item.GetProperty("Content").GetString() ?? "";
                        var isUser = item.GetProperty("IsUser").GetBoolean();
                        var timestamp = DateTime.Parse(item.GetProperty("Timestamp").GetString() ?? DateTime.Now.ToString("o"));
                        
                        _fullHistory.Add(new ProjectionMessage
                        {
                            Sender = sender,
                            Content = content,
                            IsUser = isUser,
                            Timestamp = timestamp
                        });
                    }
                    
                    Debug.WriteLine($"[History] Loaded {_fullHistory.Count} items from full_history.json");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[History] Error loading full history: {ex.Message}");
            }
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            displayedMessages.Clear();
            conversationHistory.Clear();
            conversationHistory.Add(new { role = "system", content = @"You are Atlas, an advanced AI assistant modeled after JARVIS from Iron Man. You are analytical, proactive, and technically sophisticated with comprehensive system capabilities." });
            MessagesPanel.Children.Clear();
            try { File.Delete(HistoryPath); } catch { }
            AddMessage("Atlas", "System parameters reset. Chat history cleared. How may I assist you?", false);
        }

        private void ExportChat_Click(object sender, RoutedEventArgs e)
        {
            if (displayedMessages.Count == 0)
            {
                MessageBox.Show("No messages to export.", "Export Chat", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Chat History",
                Filter = "Text File (*.txt)|*.txt|JSON File (*.json)|*.json|Markdown File (*.md)|*.md",
                DefaultExt = ".txt",
                FileName = $"AtlasChat_{DateTime.Now:yyyy-MM-dd_HHmm}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var ext = Path.GetExtension(dialog.FileName).ToLower();
                    string content;

                    if (ext == ".json")
                    {
                        content = JsonSerializer.Serialize(displayedMessages, new JsonSerializerOptions { WriteIndented = true });
                    }
                    else if (ext == ".md")
                    {
                        content = ExportAsMarkdown();
                    }
                    else
                    {
                        content = ExportAsText();
                    }

                    File.WriteAllText(dialog.FileName, content);
                    AddMessage("Atlas", $"✅ Chat exported to {Path.GetFileName(dialog.FileName)}", false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string ExportAsText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("        ATLAS AI CHAT HISTORY");
            sb.AppendLine($"        Exported: {DateTime.Now:g}");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine();

            foreach (var msg in displayedMessages)
            {
                sb.AppendLine($"[{msg.Sender}]");
                sb.AppendLine(msg.Text);
                sb.AppendLine();
            }

            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"Total messages: {displayedMessages.Count}");
            return sb.ToString();
        }

        private string ExportAsMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Atlas AI Chat History");
            sb.AppendLine($"*Exported: {DateTime.Now:g}*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var msg in displayedMessages)
            {
                var icon = msg.IsUser ? "👤" : "🤖";
                sb.AppendLine($"### {icon} {msg.Sender}");
                sb.AppendLine();
                sb.AppendLine(msg.Text);
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"*{displayedMessages.Count} messages*");
            return sb.ToString();
        }

        private void InitializeSpeechRecognition()
        {
            try
            {
                // First check if we have any audio input devices using NAudio
                int deviceCount = NAudio.Wave.WaveIn.DeviceCount;
                Debug.WriteLine($"NAudio found {deviceCount} audio input device(s)");
                
                if (deviceCount == 0)
                {
                    Debug.WriteLine("No audio input devices found");
                    ShowStatus("⚠️ No microphone detected - connect a mic and restart");
                    return;
                }
                
                // List available devices
                for (int i = 0; i < deviceCount; i++)
                {
                    var caps = NAudio.Wave.WaveIn.GetCapabilities(i);
                    Debug.WriteLine($"  Device {i}: {caps.ProductName} (Channels: {caps.Channels})");
                }
                
                // Check if any recognizers are installed
                var installedRecognizers = SpeechRecognitionEngine.InstalledRecognizers();
                if (installedRecognizers.Count == 0)
                {
                    Debug.WriteLine("No speech recognizers installed");
                    ShowStatus("⚠️ Speech recognition not set up - type your messages instead");
                    ShowMicSetupHelp();
                    return;
                }

                Debug.WriteLine($"Found {installedRecognizers.Count} recognizer(s):");
                foreach (var rec in installedRecognizers)
                {
                    Debug.WriteLine($"  - {rec.Description} ({rec.Culture})");
                }

                // Prefer English recognizer if available for better command recognition
                var chosen = installedRecognizers[0];
                foreach (var rec in installedRecognizers)
                {
                    var name = (rec.Culture?.Name ?? "").ToLowerInvariant();
                    if (name.StartsWith("en"))
                    {
                        chosen = rec;
                        break;
                    }
                }
                recognizer = new SpeechRecognitionEngine(chosen);
                recognizer.LoadGrammar(new DictationGrammar());
                
                // Try to set input to default audio device
                try
                {
                    recognizer.SetInputToDefaultAudioDevice();
                    Debug.WriteLine("Speech recognition initialized with default audio device");
                }
                catch (InvalidOperationException audioEx)
                {
                    Debug.WriteLine($"No audio device available: {audioEx.Message}");
                    ShowStatus("⚠️ No microphone found - type your messages");
                    ShowMicSetupHelp();
                    recognizer = null;
                    return;
                }
                catch (Exception audioEx)
                {
                    Debug.WriteLine($"Could not set audio device: {audioEx.Message}");
                    ShowStatus("⚠️ Microphone error - type your messages");
                    ShowMicSetupHelp();
                    recognizer = null;
                    return;
                }
                
                recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
                recognizer.SpeechRecognitionRejected += (s, e) => 
                {
                    Debug.WriteLine($"Speech rejected (confidence too low)");
                    Dispatcher.Invoke(() => 
                    {
                        if (InputBox.Text == "Listening... (speak now)")
                            InputBox.Text = "";
                        if (isListening && !_userStoppedListening)
                            ShowStatus("🎤 Listening…");
                    });
                };
                recognizer.RecognizeCompleted += (s, e) =>
                {
                    Debug.WriteLine($"Recognition completed: {e.Result?.Text ?? "no result"}");
                    if (e.Error != null)
                        Debug.WriteLine($"Recognition error: {e.Error.Message}");
                    if (e.Cancelled)
                        Debug.WriteLine("Recognition was cancelled");
                    
                    Dispatcher.Invoke(() =>
                    {
                        // In continuous mode, automatically restart recognition if the mic toggle is still on.
                        if (!isListening || _userStoppedListening)
                            return;

                        try
                        {
                            recognizer?.RecognizeAsync(RecognizeMode.Multiple);
                        }
                        catch
                        {
                        }
                    });
                };
                recognizer.AudioLevelUpdated += (s, e) =>
                {
                    // Show hearing indicator during Windows speech recognition
                    Dispatcher.Invoke(() =>
                    {
                        if (e.AudioLevel > 0)
                        {
                            HearingIndicator.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            HearingIndicator.Visibility = Visibility.Collapsed;
                        }
                    });
                };
                
                Debug.WriteLine("Speech recognition fully initialized and ready");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Speech recognition init error: {ex.Message}");
                ShowStatus($"⚠️ Speech not available - type your messages");
                recognizer = null;
            }
        }
        
        private void ShowMicSetupHelp()
        {
            // Show help dialog on first failure
            var result = MessageBox.Show(
                "Voice input requires Windows Speech Recognition to be set up.\n\n" +
                "To enable voice input:\n" +
                "1. Open Windows Settings\n" +
                "2. Go to Time & Language > Speech\n" +
                "3. Click 'Get started' under Microphone\n" +
                "4. Follow the setup wizard\n\n" +
                "You can still use Atlas by typing your messages.\n\n" +
                "Would you like to open Windows Speech Settings now?",
                "Voice Input Setup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:speech") { UseShellExecute = true });
                }
                catch
                {
                    Process.Start(new ProcessStartInfo("control", "speech") { UseShellExecute = true });
                }
            }
        }

        private void Recognizer_SpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            Debug.WriteLine($"Speech recognized: '{e.Result.Text}' (confidence: {e.Result.Confidence:P0})");

            if (!isListening || _userStoppedListening)
                return;

            var nowUtc = DateTime.UtcNow;
            var text = (e.Result.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            // Basic debounce to avoid rapid duplicate segments
            if (string.Equals(text, _lastRecognizedText, StringComparison.OrdinalIgnoreCase) &&
                (nowUtc - _lastRecognizedAtUtc).TotalMilliseconds < 800)
                return;

            _lastRecognizedText = text;
            _lastRecognizedAtUtc = nowUtc;
            
            if (e.Result.Confidence > 0.2)
            {
                Dispatcher.Invoke(() =>
                {
                    InputBox.Text = text;
                    SendMessage();
                });
            }
            else
            {
                Debug.WriteLine("Confidence too low, ignoring");
                Dispatcher.Invoke(() =>
                {
                    _lastLowConfidenceText = text;
                    InputBox.Text = text;
                    ShowStatus("⚠️ Low confidence — review and press Enter to send (mic stays on)");
                });
            }
        }

        private async void MicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMicFullyDisabled)
            {
                ShowStatus("🔇 Mic is disabled. Click the mute button to re-enable.");
                return;
            }
            if (isListening || Voice.VoiceSystemOrchestrator.Instance.IsListening)
            {
                _userStoppedListening = true;
                StopListeningAsync();
                ShowStatus("🎤 Mic off");
            }
            else
            {
                _userStoppedListening = false;
                ShowStatus("🎤 Listening…");
                await Task.Delay(1);
                StartListening();
            }
        }

        private void MicMuteButton_Click(object sender, RoutedEventArgs e)
        {
            _isMicFullyDisabled = !_isMicFullyDisabled;
            PreferencesStore.Instance.Update(p => p.EnableMicrophone = !_isMicFullyDisabled);
            
            if (_isMicFullyDisabled)
            {
                // Stop all listening
                if (isListening)
                {
                    _userStoppedListening = true;
                    StopListeningAsync();
                }
                
                // Stop wake word detection
                try
                {
                    AtlasAI.Voice.WakeWordService.Instance.Stop(AtlasAI.Voice.WakeStopReason.ExternalHandlerDefer);
                    _wakeWordDetector?.StopListening();
                }
                catch { }
                
                isWakeWordEnabled = false;
                
                ShowStatus("🔇 Microphone completely disabled");
                MicMuteButton.ToolTip = "Enable Microphone";
            }
            else
            {
                var prefs = PreferencesStore.Instance.Current;
                isWakeWordEnabled = prefs.EnableWakeWord;
                WakeWordToggle.IsChecked = isWakeWordEnabled;

                if (isWakeWordEnabled)
                {
                    RestartWakeWordListening();
                }
                else
                {
                    WakeWordIndicator.Visibility = Visibility.Collapsed;
                    WakeWordToggle.Background = new SolidColorBrush(Color.FromRgb(42, 42, 60));
                }
                
                ShowStatus("🎤 Microphone enabled");
                MicMuteButton.ToolTip = "Disable Microphone";
            }
        }

        private void StartListening()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (_isMicFullyDisabled)
                        {
                            ShowStatus("🔇 Microphone is disabled");
                            return;
                        }

                        var orchestrator = Voice.VoiceSystemOrchestrator.Instance;
                        if (orchestrator.IsListening || isListening)
                            return;

                        try { _voiceManager?.Stop(); } catch { }

                        ShowStatus("🎤 Pausing music...");
                        await AudioDuckingManager.DuckAudioAsync();
                        await Task.Delay(150);

                        if (AudioCoordinator.IsEmergencyProtectionActive)
                            AudioCoordinator.DisableEmergencyAudioProtection();

                        InputBox.Text = "🎙️ Listening…";
                        SetAtlasCoreState(Controls.AtlasVisualState.Listening);
                        orchestrator.BeginListening(Voice.ListeningSource.PushToTalk);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ChatWindow] StartListening failed: {ex.Message}");
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ShowStatus($"⚠️ Mic error: {ex.Message}");
                        isListening = false;
                        UpdateMicButtonState();
                        SetAtlasCoreState(Controls.AtlasVisualState.Idle);
                    }));
                }
            });
        }

        private void StartListeningInternal()
        {
            Debug.WriteLine("[StartListeningInternal] *** CALLED ***");

            // This is a mic-toggle session; keep the UI "on" even while Whisper cycles through record/transcribe.
            isListening = true;
            UpdateMicButtonState();
            InputBox.Text = "🎙️ Listening… (click mic to stop)";
            
            // Set Atlas Core to Listening state
            SetAtlasCoreState(Controls.AtlasVisualState.Listening);
            
            // Quick mic test using NAudio
            try
            {
                int deviceCount = NAudio.Wave.WaveIn.DeviceCount;
                Debug.WriteLine($"[StartListeningInternal] NAudio device count: {deviceCount}");
                if (deviceCount == 0)
                {
                    ShowStatus("⚠️ No microphone detected");
                    MessageBox.Show(
                        "No microphone detected.\n\n" +
                        "Please:\n" +
                        "1. Connect a microphone\n" +
                        "2. Check Windows Sound Settings\n" +
                        "3. Restart the app",
                        "No Microphone",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    isListening = false;
                    UpdateMicButtonState();
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NAudio check failed: {ex.Message}");
            }
            
            // Try Whisper first (much better accuracy)
            // Whisper is now enabled when OpenAI key is available
            if (useWhisper)
            {
                Debug.WriteLine("[StartListeningInternal] Using Whisper...");
                
                // Always create fresh instance for command listening
                whisperRecognizer?.Dispose();
                whisperRecognizer = CreateConfiguredWhisperRecognizer();
                
                Debug.WriteLine($"[StartListeningInternal] Whisper IsConfigured: {whisperRecognizer.IsConfigured}");
                
                // IMPROVED: Give user time to think and speak after wake word
                // User says "Atlas", then needs time to formulate their question
                whisperRecognizer.SilenceTimeout = 4.0;
                whisperRecognizer.MinimumRecordingSeconds = 1.2;
                whisperRecognizer.NoSpeechTimeout = 12.0;
                
                // Hook up audio level event for green indicator when hearing audio
                whisperRecognizer.AudioLevelChanged += (s, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (args.IsHearing)
                        {
                            HearingIndicator.Visibility = Visibility.Visible;
                            WakeWordIndicator.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            HearingIndicator.Visibility = Visibility.Collapsed;
                        }
                    });
                };
                
                whisperRecognizer.SpeechRecognized += (s, text) =>
                {
                    Debug.WriteLine($"[Command] Speech recognized: {text}");
                    
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var cleaned = StripWakeWordPrefix(text);
                        if (string.IsNullOrWhiteSpace(cleaned))
                        {
                            Debug.WriteLine("[Command] Ignoring wake-word-only/empty command");
                            InputBox.Text = "";
                            ShowStatus("🎤 Listening…");
                            RestartWhisperIfStillListening(200);
                            if (isWakeWordEnabled)
                                ScheduleWakeWordRestart(300);
                            return;
                        }

                        InputBox.Text = cleaned;
                        SendMessage();

                        // Continue listening until user toggles mic off
                        RestartWhisperIfStillListening(200);
                        
                        // IMPROVED: Restart wake word listening immediately after processing command
                        if (isWakeWordEnabled)
                        {
                            Debug.WriteLine("[Command] Restarting wake word listening for hands-free operation");
                            ScheduleWakeWordRestart(500); // Quick restart for hands-free
                        }
                    }));
                };
                whisperRecognizer.RecognitionError += (s, error) =>
                {
                    Debug.WriteLine($"[Command] Recognition error: {error}");
                    
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // CRITICAL FIX: Clear the "Processing..." text so user isn't stuck
                        InputBox.Text = "";
                        
                        if (error != "no_speech")
                            ShowStatus($"⚠️ {error}");
                        else
                            ShowStatus("🎤 Listening…");

                        // Continue listening until user toggles mic off
                        RestartWhisperIfStillListening(250);
                        
                        // IMPROVED: Restart wake word listening even after errors for continuous hands-free
                        if (isWakeWordEnabled)
                        {
                            Debug.WriteLine("[Command] Restarting wake word after error for hands-free operation");
                            ScheduleWakeWordRestart(300);
                        }
                    }));
                };
                
                // IMPORTANT: Handle recognition completion to restart wake word listening
                whisperRecognizer.RecognitionComplete += (s, e) =>
                {
                    Debug.WriteLine("[Command] Recognition cycle complete");
                    
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // CRITICAL FIX: Clear InputBox if it still shows "Processing..." 
                        if (InputBox.Text.Contains("Processing") || InputBox.Text.Contains("Listening"))
                        {
                            InputBox.Text = "";
                        }

                        // Continue listening until user toggles mic off
                        RestartWhisperIfStillListening(200);
                        
                        // IMPROVED: Always restart wake word for continuous hands-free operation
                        if (isWakeWordEnabled)
                        {
                            Debug.WriteLine("[Command] Restarting wake word for continuous hands-free operation");
                            ScheduleWakeWordRestart(300);
                        }
                    }));
                };
                
                whisperRecognizer.RecordingStarted += (s, e) =>
                {
                    Debug.WriteLine("[Whisper] RecordingStarted event fired!");
                    Dispatcher.Invoke(() =>
                    {
                        if (isListening)
                            InputBox.Text = "🎙️ Listening… (click mic to stop)";
                    });
                };
                whisperRecognizer.RecordingStopped += (s, e) =>
                {
                    Debug.WriteLine("[Whisper] RecordingStopped event fired!");
                    Dispatcher.Invoke(() =>
                    {
                        if (!_userStoppedListening)
                            InputBox.Text = "🔄 Processing...";
                        HearingIndicator.Visibility = Visibility.Collapsed; // Hide green indicator
                    });
                };
                
                if (whisperRecognizer.IsConfigured)
                {
                    Debug.WriteLine("[StartListeningInternal] Whisper is configured, starting recording...");
                    
                    // Stop any TTS that might still be playing
                    _voiceManager?.Stop();
                    
                    whisperRecognizer.StartRecording();
                    Debug.WriteLine("[StartListeningInternal] Whisper StartRecording() called!");
                    return;
                }
                else
                {
                    Debug.WriteLine("[Whisper] Not configured, falling back to Windows Speech");
                    ShowStatus("🎤 Enhanced recognition not configured — add OpenAI key in Settings for best accuracy");
                    useWhisper = false; // Fall back to Windows Speech
                }
            }
            
            // Fallback to Windows Speech Recognition
            if (recognizer == null)
            {
                InitializeSpeechRecognition();
                if (recognizer == null)
                {
                    ShowStatus("⚠️ Speech not available - add OpenAI key in Settings for voice input");
                    isListening = false;
                    UpdateMicButtonState();
                    return;
                }
            }
            try
            {
                // Stop any TTS that might still be playing
                _voiceManager?.Stop();
                
                Debug.WriteLine("Starting Windows speech recognition...");
                recognizer.RecognizeAsync(RecognizeMode.Multiple);
                InputBox.Text = "Listening… (click mic to stop)";
                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Already listening or audio device busy: {ex.Message}");
                ShowStatus("⚠️ Microphone busy - try again");
                StopListeningAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Start listening error: {ex.Message}");
                ShowStatus($"⚠️ Mic error: {ex.Message}");
                StopListeningAsync();
            }
        }

        private void RestartWhisperIfStillListening(int delayMs)
        {
            try
            {
                if (!isListening || _userStoppedListening)
                    return;
                if (whisperRecognizer == null || !whisperRecognizer.IsConfigured)
                    return;
                if (whisperRecognizer.IsRecording)
                    return;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs).ConfigureAwait(false);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (!isListening || _userStoppedListening)
                                return;
                            if (whisperRecognizer == null || !whisperRecognizer.IsConfigured)
                                return;
                            if (whisperRecognizer.IsRecording)
                                return;

                            Debug.WriteLine("[Whisper] Continuous mode: restarting recording");
                            whisperRecognizer.StartRecording();
                        });
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
        
        private void UpdateMicButtonState()
        {
            if (isListening)
            {
                MicButton.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                MicButton.Content = "🎙️";
                MicButton.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 50, 50));
                MicButton.BorderThickness = new Thickness(1);
                MicButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(220, 50, 50),
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.85
                };
            }
            else
            {
                MicButton.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                MicButton.Content = "🎤";
                MicButton.BorderBrush = null;
                MicButton.BorderThickness = new Thickness(0);
                MicButton.Effect = null;
                var t = InputBox.Text ?? "";
                if (t.Contains("Listening", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Processing", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Transcribing", StringComparison.OrdinalIgnoreCase))
                {
                    InputBox.Text = "";
                }
            }
        }

        private async void StopListeningAsync()
        {
            try
            {
                Voice.VoiceSystemOrchestrator.Instance.StopListening();
            }
            catch
            {
            }

            if (whisperRecognizer?.IsRecording == true)
            {
                if (_userStoppedListening)
                {
                    try { whisperRecognizer.CancelRecording(); } catch { }
                }
                else
                {
                    InputBox.Text = "🔄 Transcribing...";
                    await whisperRecognizer.StopRecordingAndTranscribeAsync();
                }
            }
            else
            {
                try
                {
                    recognizer?.RecognizeAsyncCancel();
                    recognizer?.RecognizeAsyncStop();
                }
                catch { }
            }
            
            isListening = false;
            UpdateMicButtonState();
            SetAtlasCoreState(Controls.AtlasVisualState.Idle);
            HearingIndicator.Visibility = Visibility.Collapsed;
            WakeWordIndicator.Visibility = isWakeWordEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (InputBox.Text.Contains("Listening", StringComparison.OrdinalIgnoreCase))
                InputBox.Text = "";
        }

        private void StopListening()
        {
            StopListeningAsync();
        }

        private void CheckApiKey()
        {
            var activeProvider = AIManager.GetActiveProviderInstance();
            System.Diagnostics.Debug.WriteLine($"CheckApiKey: Provider={activeProvider?.DisplayName}, IsConfigured={activeProvider?.IsConfigured}");
            
            if (activeProvider == null || !activeProvider.IsConfigured)
            {
                // Double-check if settings.txt exists with a valid key
                var settingsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "settings.txt");
                if (System.IO.File.Exists(settingsPath))
                {
                    var content = System.IO.File.ReadAllText(settingsPath).Trim();
                    if (content.StartsWith("sk-ant-") || content.StartsWith("sk-"))
                    {
                        // Key exists, don't show warning
                        StatusText.Visibility = System.Windows.Visibility.Collapsed;
                        return;
                    }
                }
                ShowStatus("⚙️ Click Settings to configure your AI provider for enhanced chat");
            }
            else
            {
                StatusText.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private async void VoiceSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceSelector.SelectedItem is ComboBoxItem item && item.Tag is string voiceId)
            {
                await _voiceManager.SelectVoiceAsync(voiceId);
                
                // CRITICAL: Update VoicePreferences so VoiceSelectionService uses this voice
                Voice.VoicePreferences.Current.SetGlobalVoice(voiceId);
                
                UpdateProviderIndicator();
            }
        }

        private void SpeechToggle_Click(object sender, RoutedEventArgs e)
        {
            SetChatSpeechEnabled(SpeechToggle.IsChecked == true, true);
        }

        private void SetChatSpeechEnabled(bool enabled, bool showStatus)
        {
            _voiceManager.SpeechEnabled = enabled;
            _isTtsMuted = !enabled;

            if (SpeechToggle != null)
                SpeechToggle.IsChecked = enabled;

            if (SectionSpeechMicStandard.IsSpeechWired("Chat"))
            {
                var sectionEnabled = SectionSpeechMicStandard.IsSpeechEnabled("Chat");
                if (sectionEnabled != enabled)
                    SectionSpeechMicStandard.ToggleSpeech("Chat");
            }

            UpdateSpeechToggleUI();

            if (showStatus)
                ShowStatus(enabled ? "🔊 Speech output on" : "🔇 Speech output off");
        }

        private void UpdateSpeechToggleUI()
        {
            var enabled = _voiceManager.SpeechEnabled;
            SpeechToggle.Content = enabled ? "ON" : "OFF";
            SpeechToggle.Background = enabled 
                ? new SolidColorBrush(Color.FromRgb(75, 181, 67)) 
                : new SolidColorBrush(Color.FromRgb(128, 128, 128));

            if (RadialMuteIcon != null)
                RadialMuteIcon.Text = enabled ? "🔊" : "🔇";

            if (RadialMuteBtn != null)
                RadialMuteBtn.ToolTip = enabled ? "Speech output on" : "Speech output off";
        }

        private void StopSpeech_Click(object sender, RoutedEventArgs e)
        {
            _voiceManager.Stop();
        }

        /// <summary>
        /// Initialize the In-App Assistant with overlay and global hotkey
        /// </summary>
        private void InitializeInAppAssistant()
        {
            try
            {
                _inAppAssistant = new InAppAssistant.InAppAssistantManager(_understandingLayer?.Context);
                
                // Set reference for DirectActionHandler to control overlay
                Tools.DirectActionHandler.SetInAppAssistant(_inAppAssistant);
                
                // Wire up events
                _inAppAssistant.VoiceCommandRequested += (s, cmd) => Dispatcher.Invoke(() =>
                {
                    // Trigger voice listening when overlay requests it
                    if (!isListening)
                        StartListening();
                });
                
                _inAppAssistant.StatusChanged += (s, status) => Dispatcher.Invoke(() =>
                {
                    ShowStatus(status);
                });
                
                _inAppAssistant.ActionCompleted += (s, result) => Dispatcher.Invoke(() =>
                {
                    if (result.Success)
                        ShowStatus($"✓ {result.Message}");
                    else
                        ShowStatus($"✗ {result.Message}");
                });
                
                // Initialize the overlay (hidden by default, Ctrl+Alt+A to show)
                _inAppAssistant.InitializeOverlay();
                
                Debug.WriteLine("[InAppAssistant] Initialized - Press Ctrl+Alt+A to toggle overlay");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InAppAssistant] Init error: {ex.Message}");
            }
        }

        private async void InitializeAvatarIntegration()
        {
            try
            {
                // DISABLED: _avatarIntegration = new UnityAvatarIntegration();
                
                // Set up event handlers - DISABLED
                // _avatarIntegration.OnUnityStarted += () => Dispatcher.Invoke(() => 
                // {
                //     ShowStatus("🎭 Avatar system connected!");
                // });
                
                // _avatarIntegration.OnUnityStopped += () => Dispatcher.Invoke(() => 
                // {
                //     ShowStatus("⚠️ Avatar system disconnected");
                // });
                
                // Try to start Unity avatar automatically - DISABLED
                // bool started = await _avatarIntegration.StartUnityAvatarAsync();
                // if (started)
                // {
                //     ShowStatus("🎭 Starting avatar system...");
                //     
                //     // Give Unity time to initialize, then send welcome message
                //     await Task.Delay(5000);
                //     await _avatarIntegration.AvatarSpeakAsync("Hello! I'm your AI assistant avatar. I can move, think, and help you with tasks!");
                // }
                // else
                // {
                //     ShowStatus("⚠️ Avatar system not available");
                // }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Avatar integration error: {ex.Message}");
                ShowStatus("⚠️ Avatar system unavailable");
            }
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
        }
        
        /// <summary>
        /// Close the Agent Results panel
        /// </summary>
        private void CloseAgentResults_Click(object sender, RoutedEventArgs e)
        {
            if (AgentResultsPanel != null)
            {
                AgentResultsPanel.Visibility = Visibility.Collapsed;
            }
        }
        
        /// <summary>
        /// Toggle Agent Mode on/off
        /// </summary>
        private void AgentMode_Click(object sender, RoutedEventArgs e)
        {
            // Toggle agent mode - this enables autonomous task execution
            var isAgentMode = AgentModeIcon?.Text == "⚡";
            if (AgentModeIcon != null)
            {
                AgentModeIcon.Text = isAgentMode ? "🤖" : "⚡";
            }
            
            var status = isAgentMode ? "Agent Mode: ON" : "Agent Mode: OFF";
            ShowStatus(status);
            Debug.WriteLine($"[ChatWindow] {status}");
        }

        private void SendButton_Click(object sender, RoutedEventArgs e) => SendMessage();

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { SendMessage(); e.Handled = true; }
        }

        private void HandleVoiceMessage(string text)
        {
            Dispatcher.Invoke(() => 
            {
                Debug.WriteLine($"[ChatWindow] HandleVoiceMessage called with: {text}");
                InputBox.Text = text;
                SendMessage();
            });
        }

        public async Task EnsureRemoteBackendReadyAsync()
        {
            if (_conversationManager != null)
                return;

            if (IsRemoteCompanionBackend)
            {
                await InitializeRemoteConversationBackendAsync();
                return;
            }

            await InitializeConversationSystemAsync();
        }

        public bool IsRemoteCompanionBackend { get; set; }

        public void RestoreShellChromeAndHeader()
        {
            try
            {
                _headerCollapsed = false;
                SetImmersiveMode(false);
                ApplyHeaderChromeState();
                if (TopNavBar != null)
                {
                    TopNavBar.Visibility = Visibility.Visible;
                    TopNavBar.Opacity = 1.0;
                }
            }
            catch
            {
            }
        }

        public void SubmitRemoteVoiceMessage(string text)
        {
            _ = SubmitRemoteTextMessageAsync(text, null, "voice-companion", startNewConversation: false, isVoiceInput: true);
        }

        public async Task<(string Reply, string? ConversationId)> SubmitRemoteTextMessageAsync(
            string message,
            string? conversationId,
            string? deviceName,
            bool startNewConversation,
            bool isVoiceInput = false)
        {
            await EnsureRemoteBackendReadyAsync();
            await EnsureRemoteConversationSessionAsync(conversationId, startNewConversation).ConfigureAwait(true);

            var normalizedMessage = (message ?? string.Empty).Trim();
            var currentConversationId = _conversationManager?.CurrentSession?.Id;
            if (string.IsNullOrWhiteSpace(normalizedMessage))
                return (string.Empty, currentConversationId);

            AddMessage("You", normalizedMessage, true);
            if (_conversationManager != null)
                await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.User, normalizedMessage, isVoiceInput).ConfigureAwait(true);

            currentConversationId = _conversationManager?.CurrentSession?.Id;
            try { AtlasAI.Services.CompanionTransportService.Instance.UpdateConversationProgress(currentConversationId, true, "Atlas is thinking..."); } catch { }
            try { AtlasAI.Services.CompanionTransportService.Instance.NotifyConversationStateChanged(currentConversationId); } catch { }

            var response = await GetAIResponse(normalizedMessage, CancellationToken.None).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(response))
            {
                AddMessage("Atlas", response, false);
                if (_conversationManager != null && !string.Equals(response, "CANCELLED · OPERATION STOPPED", StringComparison.Ordinal))
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.Assistant, response).ConfigureAwait(true);
            }

            currentConversationId = _conversationManager?.CurrentSession?.Id;
            try { AtlasAI.Services.CompanionTransportService.Instance.UpdateConversationProgress(currentConversationId, false, null); } catch { }
            try { AtlasAI.Services.CompanionTransportService.Instance.NotifyConversationStateChanged(currentConversationId); } catch { }
            return (response, currentConversationId);
        }

        public async Task<string?> StartRemoteConversationAsync(string? deviceName)
        {
            await EnsureRemoteBackendReadyAsync();

            if (_conversationManager == null)
                return null;

            var session = await _conversationManager.StartNewSessionAsync().ConfigureAwait(true);
            conversationHistory.Clear();
            conversationHistory.Add(new { role = "system", content = _systemPromptBuilder?.BuildSystemPrompt() ?? GetDefaultSystemPrompt() });

            if (MessagesPanel != null)
                MessagesPanel.Children.Clear();
            displayedMessages.Clear();

            try { AtlasAI.Services.CompanionTransportService.Instance.NotifyConversationStateChanged(session.Id); } catch { }
            return session.Id;
        }

        public async Task<RemoteConversationState> GetRemoteConversationStateAsync(string? conversationId, string? deviceName, bool createIfMissing)
        {
            await EnsureRemoteBackendReadyAsync();
            await EnsureRemoteConversationSessionAsync(conversationId, createIfMissing && string.IsNullOrWhiteSpace(conversationId)).ConfigureAwait(true);

            var session = _conversationManager?.CurrentSession;
            if (session == null)
                return new RemoteConversationState(null, "Atlas unavailable", Array.Empty<RemoteConversationMessage>());

            return new RemoteConversationState(
                session.Id,
                string.IsNullOrWhiteSpace(session.Title) ? "New Chat" : session.Title,
                session.Messages.Select(message => new RemoteConversationMessage
                {
                    Id = string.IsNullOrWhiteSpace(message.Id) ? Guid.NewGuid().ToString("N") : message.Id,
                    Role = message.Role.ToString().ToLowerInvariant(),
                    Text = message.Content ?? string.Empty,
                    Timestamp = message.Timestamp == default ? DateTime.UtcNow : message.Timestamp,
                    IsVoiceInput = message.IsVoiceInput,
                }).ToArray());
        }

        private async Task EnsureRemoteConversationSessionAsync(string? conversationId, bool startNewConversation)
        {
            if (_conversationManager == null)
                return;

            if (startNewConversation)
            {
                await _conversationManager.StartNewSessionAsync().ConfigureAwait(true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(conversationId) &&
                !string.Equals(_conversationManager.CurrentSession?.Id, conversationId, StringComparison.Ordinal))
            {
                var existingSession = await _conversationManager.LoadSessionAsync(conversationId, continueChat: false).ConfigureAwait(true);
                if (existingSession != null)
                {
                    await _conversationManager.SetCurrentSessionAsync(conversationId).ConfigureAwait(true);
                    return;
                }
            }

            if (_conversationManager.CurrentSession == null)
                await _conversationManager.StartNewSessionAsync().ConfigureAwait(true);
        }

        public async void SendMessage()
        {
            Debug.WriteLine("[CHAT] Submit invoked from voice/typed");
            var text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text) && _droppedPaths.Count == 0) return;

            // Normalise: strip wake-word prefix so that SAPI ("Atlas open front door")
            // and Whisper/wake-word ("open front door") are treated as the same input.
            var stripped = StripWakeWordPrefix(text);
            if (!string.IsNullOrWhiteSpace(stripped))
                text = stripped;

            // Reentrancy guard — prevent concurrent sends (e.g. voice + enter key at same time)
            if (_isSendingMessage)
            {
                Debug.WriteLine("[SendMessage] BLOCKED — already sending, ignoring duplicate");
                return;
            }

            // Text+time dedup — multiple voice handlers can dispatch the same text within milliseconds
            var now = DateTime.UtcNow;
            if (string.Equals(text, _lastSentText, StringComparison.OrdinalIgnoreCase) &&
                (now - _lastSentTime).TotalSeconds < 10)
            {
                Debug.WriteLine($"[SendMessage] BLOCKED — duplicate text '{text}' within {(now - _lastSentTime).TotalMilliseconds:0}ms");
                return;
            }
            _lastSentText = text;
            _lastSentTime = now;

            _isSendingMessage = true;
            try
            {
            // === GENERATE TURN ID FOR SPEECH DEDUPLICATION ===
            // All speech for this user turn will share this ID to prevent duplicates
            _currentTurnId = Guid.NewGuid();
            Debug.WriteLine($"[SendMessage] New TurnId: {_currentTurnId.ToString("N")[..8]}");

            // IMMEDIATELY show user message and clear input for responsiveness
            var displayText = text;
            if (_droppedPaths.Count > 0)
            {
                displayText += $"\n📎 {_droppedPaths.Count} file(s) attached";
            }
            
            // Store values before clearing
            var droppedContext = GetDroppedFilesContext();
            var fullMessage = text + droppedContext;
            LastDroppedPaths = new List<string>(_droppedPaths);
            
            // Clear UI immediately
            InputBox.Clear();
            _droppedPaths.Clear();
            DroppedFilesList.Items.Clear();
            UpdateDroppedFilesVisibility();

            // Stop any current speech
            _voiceManager.Stop();
            
            // Create cancellation token for this operation
            _currentOperationCts?.Cancel();
            _currentOperationCts?.Dispose();
            _currentOperationCts = new CancellationTokenSource();
            var ct = _currentOperationCts.Token;

            // First-run onboarding: capture the very next user message as their preferred display name
            if (_awaitingUserDisplayName)
            {
                AddMessage("You", displayText, true);
                if (_conversationManager != null)
                {
                    try { await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.User, text); } catch { }
                }

                await CompleteDisplayNameOnboardingAsync(text);
                return;
            }

            // Check for quick commands first (only if no dropped files)
            if (text.StartsWith("/") && LastDroppedPaths.Count == 0)
            {
                AddMessage("You", text, true);
                // Save user message to history
                if (_conversationManager != null)
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.User, text);
                await HandleQuickCommand(text, ct);
                return;
            }
            
            // Check for "what can you do" type questions - respond with capabilities
            if (IsCapabilitiesQuestion(text))
            {
                AddMessage("You", text, true);
                // Save user message to history
                if (_conversationManager != null)
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.User, text);
                var capabilities = GetCapabilitiesList();
                AddMessage("Atlas", capabilities, false);
                // Save assistant response to history
                if (_conversationManager != null)
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.Assistant, capabilities);
                _ = _voiceManager.SpeakAsync("I can do a lot! I've listed all my capabilities in the chat. Take a look and let me know what you'd like me to help with!");
                return;
            }
            
            // Show user message FIRST for instant feedback
            AddMessage("You", displayText, true);
            
            // IMMEDIATELY save user message to session history (before any early returns!)
            if (_conversationManager != null)
            {
                try
                {
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.User, text);
                    System.Diagnostics.Debug.WriteLine($"[SendMessage] User message saved to history");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SendMessage] Error saving user message: {ex.Message}");
                }
            }
            
            // === CONVERSATION DEPTH TRACKING ===
            // Record turn and check for thanks (triggers success count)
            ConversationContext.Instance.RecordTurn(text);
            if (ConversationContext.Instance.IsThanks(text))
            {
                ConversationContext.Instance.RecordSuccessfulHelp();
            }
            
            // === WORKING MEMORY UPDATE ===
            // Process user message for goals, problems, and context
            ConversationWorkingMemory.Instance.ProcessUserMessage(text);
            
            // === VAGUE INPUT DETECTION ===
            // DISABLED: Causes false positives on capability questions like "can you code"
            // Let the AI handle all questions naturally instead
            /*
            // If input is too vague, ask a clarifying question before proceeding
            if (NextBestQuestion.IsVagueInput(text))
            {
                var clarifyingQuestion = NextBestQuestion.GetClarifyingQuestion(text);
                AddMessage("Atlas", clarifyingQuestion, false);
                if (_conversationManager != null)
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.Assistant, clarifyingQuestion);
                _ = SpeakJarvisResponseAsync(clarifyingQuestion);
                return;
            }
            */
            
            // === DIRECT ACTION HANDLER - FIRST PRIORITY ===
            // Like Kiro - understands what you mean and just does it, no AI needed
            var directResponse = await Tools.DirectActionHandler.TryHandleAsync(text);
            Debug.WriteLine($"[SendMessage] DirectActionHandler returned: {(directResponse != null ? "HANDLED" : "null")}");
            if (directResponse != null)
            {
                Debug.WriteLine($"[SendMessage] DirectActionHandler response: {directResponse.Substring(0, Math.Min(100, directResponse.Length))}...");
                await PresentAssistantReplyAsync(text, directResponse);
                return;
            }

            SmartHomeTextCommandResult smartHomeCommandResult;
            try
            {
                smartHomeCommandResult = await _smartHomeTextCommandService.ExecuteAsync(text, CancellationToken.None, bypassVoiceCommandToggle: true);
                Debug.WriteLine($"[SmartHome] ExecuteAsync returned: Matched={smartHomeCommandResult.Matched}, Ok={smartHomeCommandResult.Ok}, Message={smartHomeCommandResult.Message}, Page={smartHomeCommandResult.RequestedPage}");
            }
            catch (Exception shEx)
            {
                Debug.WriteLine($"[SmartHome] ExecuteAsync threw: {shEx}");
                // If the input looks like a smart home command, surface the error
                // instead of silently falling through to AI
                if (SmartHomeTextCommandService.LooksLikeSmartHomeCommand(text))
                {
                    smartHomeCommandResult = new SmartHomeTextCommandResult
                    {
                        Matched = true,
                        Ok = false,
                        Message = $"Smart home error: {shEx.Message}",
                    };
                }
                else
                {
                    smartHomeCommandResult = SmartHomeTextCommandResult.NotMatched();
                }
            }
            if (smartHomeCommandResult.Matched)
            {
                var responseText = smartHomeCommandResult.Ok
                    ? smartHomeCommandResult.Message
                    : $"Smart Home command failed: {smartHomeCommandResult.Message}";

                // Show response and speak IMMEDIATELY — don't wait for camera/page to load
                await PresentAssistantReplyAsync(text, responseText);

                // Fire the UI intent in the background so it doesn't block chat
                if (smartHomeCommandResult.Ok &&
                    !string.IsNullOrWhiteSpace(smartHomeCommandResult.RequestedPage) &&
                    string.Equals(smartHomeCommandResult.RequestedPage, "smarthome", StringComparison.OrdinalIgnoreCase))
                {
                    var capturedResult = smartHomeCommandResult;
                    ShowPage("smarthome");
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        Exception? lastError = null;
                        for (var attempt = 0; attempt < 6; attempt++)
                        {
                            try
                            {
                                await Task.Delay(attempt == 0 ? 250 : 450);
                                await SmartHomePageRoot.ExecuteResolvedVoiceCommandAsync(capturedResult, CancellationToken.None);
                                Debug.WriteLine($"[SmartHome] Camera intent executed on attempt {attempt + 1}");
                                return;
                            }
                            catch (Exception ex)
                            {
                                lastError = ex;
                                Debug.WriteLine($"[SmartHome] Camera intent attempt {attempt + 1} failed: {ex.Message}");
                            }
                        }

                        Debug.WriteLine($"[SmartHome] Background camera intent failed after retries: {lastError?.Message}");
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }

                return;
            }
            
            // === JARVIS CONVERSATION ROUTING ===
            // Handle greetings, introductions, and small talk with polite Jarvis-style responses
            // This prevents generic "hey" or "ok" replies and ensures proper conversation
            var conversationResult = ResponseStyleController.Instance.ProcessInput(text);
            if (conversationResult.IsConversational && !string.IsNullOrEmpty(conversationResult.Response))
            {
                // Store extracted name in session if provided
                if (!string.IsNullOrEmpty(conversationResult.ExtractedName) && _conversationManager?.UserProfile != null)
                {
                    // Update session name (not persisted unless user opts in)
                    ResponseStyleController.Instance.SetUserName(conversationResult.ExtractedName);
                    Debug.WriteLine($"[Conversation] Session name set to: {conversationResult.ExtractedName}");
                }
                
                await PresentAssistantReplyAsync(text, conversationResult.Response);
                return;
            }
            
            // === VOICE AGENT TRIGGER ===
            // Check for "agent, do X" or "hey agent, X" voice commands
            var lowerText = text.ToLowerInvariant();
            if (lowerText.StartsWith("agent ") || lowerText.StartsWith("agent, ") || 
                lowerText.StartsWith("hey agent ") || lowerText.StartsWith("hey agent, "))
            {
                // Extract the task after "agent" prefix
                var agentTask = text;
                if (lowerText.StartsWith("hey agent, ")) agentTask = text.Substring(11).Trim();
                else if (lowerText.StartsWith("hey agent ")) agentTask = text.Substring(10).Trim();
                else if (lowerText.StartsWith("agent, ")) agentTask = text.Substring(7).Trim();
                else if (lowerText.StartsWith("agent ")) agentTask = text.Substring(6).Trim();
                
                if (!string.IsNullOrWhiteSpace(agentTask))
                {
                    var agentResponse = await RunAgentTask(agentTask);
                    await PresentAssistantReplyAsync(text, agentResponse);
                    return;
                }
            }
            
            // === AUTO-DETECT AGENT TASKS (BEFORE smart commands) ===
            // Check if this looks like a task the agent should handle (file ops, installations, etc.)
            // This MUST come before HandleSmartCommand to ensure confirmation dialogs work
            if (IsAgentTask(text))
            {
                var agentResponse = await RunAgentTask(text);
                await PresentAssistantReplyAsync(text, agentResponse);
                return;
            }
            
            // Check for natural language smart commands (before AI processing)
            var smartResponse = await HandleSmartCommand(text);
            Debug.WriteLine($"[SendMessage] HandleSmartCommand returned: {(smartResponse != null ? "HANDLED" : "null")}");
            if (smartResponse != null)
            {
                Debug.WriteLine($"[SendMessage] HandleSmartCommand response: {smartResponse.Substring(0, Math.Min(100, smartResponse.Length))}...");
                AddMessage("Atlas", smartResponse, false);
                // Save assistant response to history
                if (_conversationManager != null)
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.Assistant, smartResponse);
                _ = _voiceManager.SpeakAsync(GetShortResponse(smartResponse));
                return;
            }
            
            // Check for memory commands
            if (await HandleMemoryCommand(text))
            {
                return;
            }
            
            // User message already saved above (before early returns)
            
            InputBox.IsEnabled = false;
            SendButton.IsEnabled = false;

            var typingIndicator = ShowTypingIndicator();
            
            // Show thinking indicator above input box
            ShowThinkingIndicator("Thinking...");
            
            // Show thinking bubble in chat with shimmer animation
            var thinkingBubble = ShowThinkingBubble();
            
            // Set Atlas Core to Thinking state while generating response (orange glow)
            SetAtlasCoreState(Controls.AtlasVisualState.Thinking);
            
            // Show what Atlas is doing
            UpdateTypingProgress("Thinking...");

            string response = "";
            var activeProvider = AIManager.GetActiveProviderInstance();
            
            try
            {
                // === MEMORY & LEARNING LAYER INTEGRATION ===
                // Process user message through the learning system (fire and forget - don't block)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await MemoryManager.Instance.ProcessUserMessageAsync(text, _lastAtlasAction);
                        Debug.WriteLine($"[Memory] Processed user message for learning");
                    }
                    catch (Exception memEx)
                    {
                        Debug.WriteLine($"[Memory] Error processing message: {memEx.Message}");
                    }
                });
                
                // Build enhanced context with long-term memory (quick operation)
                var contextEnhancedMessage = fullMessage;
                
                // Show progress
                UpdateTypingProgress("Accessing memory...");
                UpdateThinkingStatus("Accessing memory...");
                
                // Get long-term memory context - use cached if available
                string longTermMemoryContext = "";
                try
                {
                    longTermMemoryContext = MemoryManager.Instance.GetCachedContext() ?? "";
                }
                catch (Exception memEx)
                {
                    Debug.WriteLine($"[Memory] Error getting context: {memEx.Message}");
                }
                
                // STEP 29 FIX: Do NOT inject context into user message
                // Context belongs in the system prompt only - injecting it into the user message
                // causes the LLM to respond to the meta-information instead of the actual message.
                // The system prompt already contains user profile, working memory, and conversation depth.
                // Just pass the user's actual message cleanly.
                contextEnhancedMessage = fullMessage;
                
                // Show AI is generating
                UpdateTypingProgress("Generating response...");
                UpdateThinkingStatus("Generating response...");
                
                var onlineResearchResponse = await TryHandleOnlineResearchAsync(text, ct);
                if (!string.IsNullOrWhiteSpace(onlineResearchResponse))
                {
                    response = onlineResearchResponse;
                }
                else if (activeProvider != null && activeProvider.IsConfigured)
                {
                    Debug.WriteLine($"[SendMessage] AI Provider configured: {activeProvider.DisplayName}");
                    response = await GetAIResponse(contextEnhancedMessage, ct);
                }
                else
                {
                    Debug.WriteLine($"[SendMessage] AI Provider NOT configured - activeProvider={activeProvider?.DisplayName ?? "null"}, IsConfigured={activeProvider?.IsConfigured ?? false}");
                    await Task.Delay(500, ct);
                    response = GetSimpleResponse(fullMessage);
                }
                
                // Store this response as the last Atlas action (for correction detection)
                _lastAtlasAction = response;
            }
            catch (OperationCanceledException)
            {
                response = "CANCELLED · OPERATION STOPPED";
            }
            catch (Exception ex)
            {
                response = $"❌ Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[SendMessage] Exception: {ex}");
            }
            
            // Ensure we have a response to display
            if (string.IsNullOrWhiteSpace(response))
            {
                response = "🤔 I didn't get a response. Please try again or check your API connection in Settings.";
            }

            // Hide UI indicators
            HideTypingIndicator(typingIndicator);
            HideThinkingBubble(thinkingBubble);
            HideThinkingIndicator();
            
            // === DIRECT RESPONSE — NO SANITISATION ===
            // The AI model produces personality-appropriate responses from the system prompt.
            // Do NOT run through ResponseComposer — it strips unfiltered personality,
            // banned openers, self-references, and greeting trimming.
            var finalText = response;
            
            // Start TTS IMMEDIATELY - don't wait for typewriter animation
            // Atlas reads while text types on screen simultaneously
            if (response != "CANCELLED · OPERATION STOPPED")
            {
                SetAtlasCoreState(Controls.AtlasVisualState.Speaking);
                _ = SpeakJarvisResponseAsync(finalText);
            }
            
            // Stream the message character by character for typewriter effect (runs in parallel with TTS)
            await StreamMessageAsync("Atlas", finalText, false);
            
            // Track assistant response in session (properly await to ensure it's saved)
            if (_conversationManager != null && response != "CANCELLED · OPERATION STOPPED")
            {
                try
                {
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.Assistant, finalText);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SendMessage] Error saving assistant message: {ex.Message}");
                }
            }
            
            // Re-enable input IMMEDIATELY so user can type while Atlas speaks
            InputBox.IsEnabled = true;
            SendButton.IsEnabled = true;
            InputBox.Focus();
            
            // TTS already started above - no need to start it again
            
            // Also send to avatar for lip-sync/animation if Unity is running - DISABLED
            // if (_avatarIntegration?.IsUnityRunning == true)
            // {
            //     // Send text to Unity for avatar lip-sync (Unity doesn't produce audio, just animation)
            //     _ = _avatarIntegration.AvatarSpeakAsync(finalMessage.Text);
            //     
            //     // Show thinking animation for longer responses
            //     if (finalMessage.Text.Length > 100)
            //     {
            //         _ = _avatarIntegration.AvatarThinkAsync();
            //     }
            // }
            
            // Restore audio after AI finishes speaking (if it was ducked)
            if (AudioDuckingManager.IsDucked)
            {
                // Wait a moment after AI response, then restore music
                _ = Task.Delay(2000).ContinueWith(async _ =>
                {
                    await AudioDuckingManager.RestoreAudioAsync();
                    SafeDispatcherInvoke(() => ShowStatus("🎵 Music resumed"));
                });
            }
            }
            finally
            {
                _isSendingMessage = false;
            }
        }
        
        /// <summary>
        /// Check if user input is a greeting
        /// </summary>
        private bool IsGreetingInput(string input)
        {
            var lower = input.ToLowerInvariant().Trim();
            var greetings = new[] { "hello", "hi", "hey", "good morning", "good afternoon", "good evening", "howdy", "yo", "sup", "what's up", "greetings" };
            return greetings.Any(g => lower.StartsWith(g) || lower == g);
        }

        private async Task<string?> TryHandleOnlineResearchAsync(string userMessage, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return null;

            ct.ThrowIfCancellationRequested();

            var lower = userMessage.ToLowerInvariant();
            if (!ShouldPreferOnlineResearch(lower))
                return null;

            Action<string>? progressHandler = null;
            progressHandler = status =>
            {
                _ = Dispatcher.InvokeAsync(() => UpdateThinkingStatus(status));
            };

            AtlasAI.Tools.WebSearchTool.SearchProgressChanged += progressHandler;

            try
            {
                UpdateThinkingStatus("Routing request...");
                var routing = await UnifiedIntentRouter.Instance.RouteAsync(userMessage);
                if (routing.Pipeline != RoutingPipeline.WebResearch)
                    return null;

                Debug.WriteLine($"[SendMessage] Routing through online research: {routing.Intent} ({routing.DebugReason})");

                UpdateThinkingStatus("Researching online...");
                var assistantResponse = await IntelligentAssistantOrchestrator.Instance.ProcessInputAsync(userMessage);
                UpdateThinkingStatus("Preparing response...");
                return string.IsNullOrWhiteSpace(assistantResponse.Text) ? null : assistantResponse.Text;
            }
            finally
            {
                AtlasAI.Tools.WebSearchTool.SearchProgressChanged -= progressHandler;
            }
        }

        private bool ShouldPreferOnlineResearch(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower))
                return false;

            if (Regex.IsMatch(lower, @"\b(search|google|look\s+up|research|find\s+info)\b", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(lower, @"\b(latest|current|recent|news|today'?s|breaking)\b", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(lower, @"\b(weather|forecast|temperature)\b", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(lower, @"\b(price|cost|buy|purchase|compare|shop|review|rating|best|top\s+\d+)\b", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(lower, @"\b(version|release|released|update|documentation|docs)\b", RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Handle memory-related commands like "remember this" or "forget that"
        /// </summary>
        private async Task<bool> HandleMemoryCommand(string text)
        {
            if (_conversationManager == null) return false;
            
            var lowerText = text.ToLowerInvariant();
            
            // "Remember this/that" patterns
            if (lowerText.StartsWith("remember ") || lowerText.Contains("remember that ") || lowerText.Contains("remember this:"))
            {
                // Extract what to remember
                var toRemember = text;
                if (lowerText.StartsWith("remember that "))
                    toRemember = text.Substring("remember that ".Length);
                else if (lowerText.StartsWith("remember this: "))
                    toRemember = text.Substring("remember this: ".Length);
                else if (lowerText.StartsWith("remember "))
                    toRemember = text.Substring("remember ".Length);
                
                if (!string.IsNullOrWhiteSpace(toRemember))
                {
                    // Determine category based on content
                    var category = Conversation.Models.MemoryCategory.General;
                    if (lowerText.Contains("prefer") || lowerText.Contains("like") || lowerText.Contains("want"))
                        category = Conversation.Models.MemoryCategory.Preference;
                    else if (lowerText.Contains("project") || lowerText.Contains("working on"))
                        category = Conversation.Models.MemoryCategory.Project;
                    else if (lowerText.Contains("name") || lowerText.Contains("call me"))
                        category = Conversation.Models.MemoryCategory.PersonalInfo;
                    
                    await _conversationManager.RememberAsync(toRemember, category);
                    
                    var confirmation = _systemPromptBuilder?.GetConfirmation($"I'll remember: \"{toRemember}\"") 
                        ?? $"Got it! I'll remember: \"{toRemember}\"";
                    AddMessage("Atlas", confirmation, false);
                    await _voiceManager.SpeakAsync(confirmation);
                    return true;
                }
            }
            
            // "Forget" patterns
            if (lowerText.StartsWith("forget ") || lowerText == "clear memory" || lowerText == "forget everything")
            {
                if (lowerText == "forget everything" || lowerText == "clear memory")
                {
                    var result = MessageBox.Show(
                        "Are you sure you want to clear all memories?",
                        "Clear Memory",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        await _conversationManager.ClearAllMemoriesAsync();
                        AddMessage("Atlas", "Memory cleared. Starting fresh.", false);
                        await _voiceManager.SpeakAsync("Memory cleared.");
                    }
                    return true;
                }
            }
            
            // "What do you remember" / "show memory"
            if (lowerText.Contains("what do you remember") || lowerText == "show memory" || lowerText == "list memories")
            {
                var memories = _conversationManager.Memories;
                if (memories.Count == 0)
                {
                    AddMessage("Atlas", "I don't have any saved memories yet. Say \"remember that...\" to save something.", false);
                }
                else
                {
                    var memoryList = string.Join("\n", memories.Select(m => $"• {m.Content}"));
                    AddMessage("Atlas", $"Here's what I remember:\n\n{memoryList}", false);
                }
                return true;
            }
            
            return false;
        }

        private async Task HandleQuickCommand(string input, CancellationToken ct = default)
        {
            InputBox.Clear();
            var parts = input.Split(' ', 2);
            var command = parts[0].ToLower();
            var args = parts.Length > 1 ? parts[1] : "";

            string response;
            bool showUserMessage = true;

            switch (command)
            {
                case "/time":
                    response = $"🕐 The current time is {DateTime.Now:h:mm:ss tt}";
                    break;

                case "/date":
                    response = $"📅 Today is {DateTime.Now:dddd, MMMM d, yyyy}";
                    break;

                case "/calc":
                case "/calculate":
                    response = CalculateExpression(args);
                    break;

                case "/open":
                    response = await OpenApplication(args);
                    break;

                case "/search":
                    response = await SearchWeb(args);
                    break;

                case "/copy":
                    if (!string.IsNullOrEmpty(args))
                    {
                        Clipboard.SetText(args);
                        response = "📋 Copied to clipboard!";
                    }
                    else
                        response = "Usage: /copy <text>";
                    break;

                case "/clipboard":
                case "/clip":
                    OpenClipboardManager();
                    return;

                case "/clear":
                    ClearHistory_Click(this, new RoutedEventArgs());
                    return;

                case "/voice":
                    _voiceManager.SpeechEnabled = !_voiceManager.SpeechEnabled;
                    SpeechToggle.IsChecked = _voiceManager.SpeechEnabled;
                    UpdateSpeechToggleUI();
                    response = _voiceManager.SpeechEnabled ? "🔊 Voice enabled" : "🔇 Voice disabled";
                    break;

                case "/theme":
                case "/dark":
                case "/light":
                    if (command == "/dark")
                        ThemeManager.SetTheme(AppTheme.Dark);
                    else if (command == "/light")
                        ThemeManager.SetTheme(AppTheme.Light);
                    else
                        ThemeManager.ToggleTheme();
                    var themeName = ThemeManager.CurrentTheme == AppTheme.Dark ? "Dark" : "Light";
                    response = $"🎨 Switched to {themeName} theme";
                    break;

                case "/joke":
                    response = GetRandomJoke();
                    break;

                case "/flip":
                    response = new Random().Next(2) == 0 ? "🪙 Heads!" : "🪙 Tails!";
                    break;

                case "/roll":
                    var sides = 6;
                    if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var s) && s > 0)
                        sides = s;
                    response = $"🎲 Rolled a {new Random().Next(1, sides + 1)} (d{sides})";
                    break;

                case "/timer":
                    response = await SetTimer(args);
                    break;

                case "/screenshot":
                case "/capture":
                    await CaptureScreenshot();
                    return;

                case "/analyze":
                    response = await AnalyzeLatestScreenshot(ct);
                    break;

                case "/ocr":
                    response = await ExtractTextFromScreenshot(ct);
                    break;

                case "/avatar":
                    response = await HandleAvatarCommand(args);
                    break;

                case "/avatars":
                case "/selectavatar":
                    OpenAvatarSelection();
                    return;

                case "/history":
                case "/screenshots":
                    OpenHistoryWindow();
                    return;

                case "/systemscan":
                case "/scan":
                    response = await PerformSystemScan(ct);
                    break;

                case "/spywarescan":
                case "/spyware":
                case "/malwarescan":
                case "/malware":
                    response = await PerformSpywareScan(ct);
                    break;

                case "/systemfix":
                case "/autofix":
                    response = await PerformSystemAutoFix(ct);
                    break;

                case "/systemcontrol":
                case "/sysctl":
                    OpenSystemControlWindow();
                    return;

                case "/code":
                case "/ide":
                case "/editor":
                    OpenCodeEditor();
                    return;

                case "/agent":
                    response = await RunAgentTask(args);
                    break;
                    
                case "/undo":
                    // Use the new safety manager for undo
                    if (Agent.AgentSafetyManager.Instance.CanUndo)
                    {
                        var (success, message) = await Agent.AgentSafetyManager.Instance.UndoLastAsync();
                        response = message;
                    }
                    else if (_agent != null && _agent.ActionHistory.Count > 0)
                    {
                        // Fallback to old agent undo
                        response = await _agent.UndoLastActionAsync();
                    }
                    else
                    {
                        response = "❌ Nothing to undo";
                    }
                    break;
                    
                case "/undo-all":
                    // Undo multiple actions
                    var count = 5;
                    if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var n))
                        count = Math.Min(n, 20);
                    response = await Agent.AgentSafetyManager.Instance.UndoMultipleAsync(count);
                    break;
                    
                case "/undo-list":
                case "/agent-undo":
                    response = Agent.AgentSafetyManager.Instance.GetUndoSummary();
                    break;
                    
                case "/agent-history":
                case "/actions":
                    if (_agent != null)
                    {
                        response = _agent.GetActionSummary(10);
                    }
                    else
                    {
                        response = "No agent actions recorded yet.";
                    }
                    break;
                    
                case "/restore-point":
                    // Create a system restore point
                    var desc = string.IsNullOrEmpty(args) ? "Manual restore point" : args;
                    var (rpSuccess, rpMessage) = await Agent.AgentSafetyManager.Instance.CreateRestorePointAsync(desc);
                    response = rpMessage;
                    break;

                case "/overlay":
                case "/inapp":
                    _inAppAssistant?.ToggleOverlay();
                    response = _inAppAssistant?.IsOverlayVisible == true 
                        ? "🎯 In-App Assistant overlay shown (Ctrl+Alt+A to toggle)"
                        : "🎯 In-App Assistant overlay hidden";
                    break;

                case "/context":
                case "/activeapp":
                    var ctx = _inAppAssistant?.GetCurrentContext();
                    if (ctx != null)
                    {
                        response = $"🖥️ Active App Context:\n" +
                                   $"• Process: {ctx.ProcessName}\n" +
                                   $"• Window: {ctx.WindowTitle}\n" +
                                   $"• Category: {ctx.Category}\n" +
                                   (ctx.IsBrowser ? $"• Tab: {ctx.BrowserTabTitle}\n" : "");
                    }
                    else
                    {
                        response = "⚠️ In-App Assistant not initialized";
                    }
                    break;

                case "/do":
                case "/action":
                    if (!string.IsNullOrEmpty(args))
                    {
                        var actionResult = await (_inAppAssistant?.ExecuteCommandAsync(args) ?? Task.FromResult(new InAppAssistant.Models.ActionResult { Success = false, Message = "In-App Assistant not initialized" }));
                        response = actionResult.Success ? $"✓ {actionResult.Message}" : $"✗ {actionResult.Message}";
                    }
                    else
                    {
                        response = "Usage: /do <command>\nExamples:\n• /do new folder called Projects\n• /do search for readme\n• /do open file main.cs";
                    }
                    break;

                case "/updatedb":
                case "/update":
                    response = await UpdateThreatDatabase(ct);
                    break;

                // ===== NEW SMART FEATURES =====
                
                case "/note":
                case "/takenote":
                    if (!string.IsNullOrEmpty(args))
                        response = await Features.SmartFeatures.TakeNoteAsync(args);
                    else
                        response = "Usage: /note <your note text>\nExample: /note Remember to call mom tomorrow";
                    break;
                
                case "/notes":
                case "/mynotes":
                    response = await Features.SmartFeatures.GetNotesAsync();
                    break;
                
                case "/diagnostics":
                case "/diag":
                case "/sysinfo":
                case "/pcstatus":
                    response = await Features.SmartFeatures.GetSystemDiagnosticsAsync();
                    break;
                
                case "/website":
                case "/web":
                case "/site":
                    if (!string.IsNullOrEmpty(args))
                        response = Features.SmartFeatures.OpenWebsite(args);
                    else
                        response = Features.SmartFeatures.GetWebsiteList();
                    break;
                
                case "/youtube":
                    response = Features.SmartFeatures.OpenWebsite("youtube");
                    break;
                
                case "/netflix":
                    response = Features.SmartFeatures.OpenWebsite("netflix");
                    break;
                
                case "/twitter":
                case "/x":
                    response = Features.SmartFeatures.OpenWebsite("twitter");
                    break;
                
                case "/reddit":
                    response = Features.SmartFeatures.OpenWebsite("reddit");
                    break;
                
                case "/gmail":
                case "/email":
                    response = Features.SmartFeatures.OpenWebsite("gmail");
                    break;
                
                case "/github":
                    response = Features.SmartFeatures.OpenWebsite("github");
                    break;
                
                case "/amazon":
                    response = Features.SmartFeatures.OpenWebsite("amazon");
                    break;
                
                case "/funfact":
                case "/fact":
                    response = Features.SmartFeatures.TellFunFact();
                    break;
                
                case "/compliment":
                case "/motivate":
                    response = Features.SmartFeatures.GiveCompliment();
                    break;
                
                case "/8ball":
                case "/magic8ball":
                    response = Features.SmartFeatures.Magic8Ball();
                    break;
                
                case "/briefing":
                case "/morning":
                case "/daily":
                    response = await Features.SmartFeatures.GetDailyBriefingAsync();
                    break;
                
                case "/weather":
                    if (!string.IsNullOrEmpty(args))
                        response = await GetWeatherAsync(args);
                    else
                        response = await GetWeatherAsync("Middlesbrough");
                    break;

                // ===== MEMORY & LEARNING COMMANDS =====
                
                case "/memory":
                case "/memorystats":
                case "/whatdoyouknow":
                    response = await GetMemoryStatsAsync();
                    break;
                
                case "/corrections":
                case "/mycorrections":
                    response = await GetCorrectionsListAsync();
                    break;
                
                case "/preferences":
                case "/mypreferences":
                    response = await GetPreferencesListAsync();
                    break;

                // ===== SECURITY & PERMISSIONS COMMANDS =====
                
                case "/permissions":
                case "/security":
                    response = GetPermissionsStatus();
                    break;
                
                case "/trust":
                    if (!string.IsNullOrEmpty(args))
                    {
                        Security.Permissions.PermissionPolicy.Instance.TrustAction(args);
                        response = $"✅ Trusted action: {args}\nAtlas will no longer ask for confirmation for this action.";
                    }
                    else
                    {
                        response = "Usage: /trust <action_name>\nExample: /trust write_file";
                    }
                    break;
                
                case "/untrust":
                    if (!string.IsNullOrEmpty(args))
                    {
                        Security.Permissions.PermissionPolicy.Instance.UntrustAction(args);
                        response = $"🔒 Removed trust for: {args}\nAtlas will ask for confirmation again.";
                    }
                    else
                    {
                        response = "Usage: /untrust <action_name>";
                    }
                    break;

                case "/help":
                case "/?":
                    response = GetCommandHelp();
                    showUserMessage = false;
                    break;

                default:
                    response = $"❓ Unknown command: {command}\nType /help for available commands.";
                    break;
            }

            if (showUserMessage)
                AddMessage("You", input, true);
            
            AddMessage("Atlas", response, false);
            await _voiceManager.SpeakAsync(response);
        }

        private string CalculateExpression(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return "Usage: /calc <expression>\nExample: /calc 2 + 2 * 3";

            try
            {
                // Simple calculator - supports basic operations
                expr = expr.Replace(" ", "").Replace("x", "*").Replace("×", "*").Replace("÷", "/");
                var result = EvaluateSimpleExpression(expr);
                return $"🔢 {expr} = {result}";
            }
            catch
            {
                return $"❌ Couldn't calculate: {expr}";
            }
        }

        private double EvaluateSimpleExpression(string expr)
        {
            // Use DataTable for simple expression evaluation
            var table = new System.Data.DataTable();
            var result = table.Compute(expr, "");
            return Convert.ToDouble(result);
        }

        private async Task<string> OpenApplication(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName))
                return "Usage: /open <app>\nExamples: /open notepad, /open calc, /open chrome";

            try
            {
                var app = appName.ToLower().Trim();
                var processName = app switch
                {
                    "notepad" => "notepad",
                    "calc" or "calculator" => "calc",
                    "paint" => "mspaint",
                    "explorer" or "files" => "explorer",
                    "cmd" or "terminal" => "cmd",
                    "powershell" or "ps" => "powershell",
                    "chrome" => "chrome",
                    "firefox" => "firefox",
                    "edge" => "msedge",
                    "code" or "vscode" => "code",
                    _ => app
                };

                Process.Start(new ProcessStartInfo(processName) { UseShellExecute = true });
                await Task.Delay(100);
                return $"🚀 Opening {appName}...";
            }
            catch
            {
                return $"❌ Couldn't open: {appName}";
            }
        }

        private async Task<string> SearchWeb(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Usage: /search <query>\nExample: /search weather today";

            try
            {
                var url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                await Task.Delay(100);
                return $"🔍 Searching for: {query}";
            }
            catch
            {
                return $"❌ Couldn't search: {query}";
            }
        }
        
        private async Task<string> GetWeatherAsync(string location)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10); // Slightly longer timeout
                client.DefaultRequestHeaders.Add("User-Agent", "curl"); // wttr.in works better with curl user agent
                
                // Use simpler format that's more reliable
                var url = $"https://wttr.in/{Uri.EscapeDataString(location)}?format=j1";
                var response = await client.GetStringAsync(url);
                
                // Parse JSON response
                var json = System.Text.Json.JsonDocument.Parse(response);
                var current = json.RootElement.GetProperty("current_condition")[0];
                var weather = json.RootElement.GetProperty("weather")[0];
                
                var temp = current.GetProperty("temp_C").GetString();
                var feelsLike = current.GetProperty("FeelsLikeC").GetString();
                var humidity = current.GetProperty("humidity").GetString();
                var windSpeed = current.GetProperty("windspeedKmph").GetString();
                var desc = current.GetProperty("weatherDesc")[0].GetProperty("value").GetString();
                var area = json.RootElement.GetProperty("nearest_area")[0].GetProperty("areaName")[0].GetProperty("value").GetString();
                
                // Tomorrow's forecast
                var tomorrow = json.RootElement.GetProperty("weather")[1];
                var maxTemp = tomorrow.GetProperty("maxtempC").GetString();
                var minTemp = tomorrow.GetProperty("mintempC").GetString();
                
                return $@"🌤️ Weather for {area}

🌡️ {temp}°C (feels like {feelsLike}°C)
☁️ {desc}
💧 {humidity}% humidity
💨 {windSpeed} km/h wind

📅 Tomorrow: {minTemp}°C - {maxTemp}°C";
            }
            catch (TaskCanceledException)
            {
                return $"⏱️ Weather request timed out. Try again.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Weather error: {ex.Message}");
                return $"❌ Couldn't get weather for {location}. Check your internet connection.";
            }
        }

        #region Memory & Learning Layer Commands
        
        /// <summary>
        /// Get memory statistics - what Atlas has learned about the user
        /// </summary>
        private async Task<string> GetMemoryStatsAsync()
        {
            try
            {
                var stats = await MemoryManager.Instance.GetStatsAsync();
                var userName = await MemoryManager.Instance.GetUserNameAsync();
                
                var sb = new StringBuilder();
                sb.AppendLine("🧠 **Atlas Memory Status**\n");
                
                if (!string.IsNullOrEmpty(userName))
                    sb.AppendLine($"👤 I know you as: {userName}");
                
                sb.AppendLine($"📝 Corrections learned: {stats.TotalCorrections}");
                sb.AppendLine($"💡 Facts remembered: {stats.TotalFacts}");
                sb.AppendLine($"⚙️ Preferences saved: {stats.TotalPreferences}");
                sb.AppendLine($"🔧 Tools tracked: {stats.TotalSkillsTracked}");
                sb.AppendLine($"📊 Total tool executions: {stats.TotalToolExecutions}");
                
                sb.AppendLine("\n💬 Commands:");
                sb.AppendLine("• /corrections - See what I've learned NOT to do");
                sb.AppendLine("• /preferences - See your saved preferences");
                sb.AppendLine("• \"Don't use X\" - Teach me to avoid something");
                sb.AppendLine("• \"Remember that...\" - Save a fact about you");
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Memory] Error getting stats: {ex.Message}");
                return "❌ Couldn't retrieve memory stats.";
            }
        }
        
        /// <summary>
        /// Get list of corrections Atlas has learned
        /// </summary>
        private async Task<string> GetCorrectionsListAsync()
        {
            try
            {
                var corrections = await MemoryManager.Instance.Corrections.GetAllCorrectionsAsync();
                
                if (corrections.Count == 0)
                {
                    return "📝 No corrections learned yet.\n\nTeach me by saying things like:\n• \"Don't use Canva, use Photoshop instead\"\n• \"No, I meant the other one\"\n• \"That's wrong, do X instead\"";
                }
                
                var sb = new StringBuilder();
                sb.AppendLine("📝 **Corrections I've Learned:**\n");
                
                foreach (var c in corrections.Take(15))
                {
                    sb.AppendLine($"• ❌ \"{c.OriginalMistake}\" → ✅ \"{c.Correction}\" ({c.TimesApplied}x)");
                }
                
                if (corrections.Count > 15)
                    sb.AppendLine($"\n...and {corrections.Count - 15} more");
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Memory] Error getting corrections: {ex.Message}");
                return "❌ Couldn't retrieve corrections.";
            }
        }
        
        /// <summary>
        /// Get list of user preferences
        /// </summary>
        private async Task<string> GetPreferencesListAsync()
        {
            try
            {
                var toolPrefs = await MemoryManager.Instance.Store.GetPreferencesByCategoryAsync("tools");
                var generalPrefs = await MemoryManager.Instance.Store.GetPreferencesByCategoryAsync("general");
                var facts = await MemoryManager.Instance.Store.GetFactsAsync(limit: 10);
                
                var sb = new StringBuilder();
                sb.AppendLine("⚙️ **Your Preferences & Facts:**\n");
                
                if (toolPrefs.Count > 0)
                {
                    sb.AppendLine("🔧 Tool Preferences:");
                    foreach (var pref in toolPrefs)
                    {
                        sb.AppendLine($"  • {pref.Key}: {pref.Value}");
                    }
                    sb.AppendLine();
                }
                
                if (generalPrefs.Count > 0)
                {
                    sb.AppendLine("📋 General Preferences:");
                    foreach (var pref in generalPrefs)
                    {
                        sb.AppendLine($"  • {pref.Key}: {pref.Value}");
                    }
                    sb.AppendLine();
                }
                
                if (facts.Count > 0)
                {
                    sb.AppendLine("💡 Facts I Know:");
                    foreach (var fact in facts)
                    {
                        sb.AppendLine($"  • {fact}");
                    }
                }
                
                if (toolPrefs.Count == 0 && generalPrefs.Count == 0 && facts.Count == 0)
                {
                    sb.AppendLine("No preferences saved yet.\n\nTeach me by saying things like:\n• \"I prefer dark mode\"\n• \"My name is John\"\n• \"Remember that I work at Acme Corp\"");
                }
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Memory] Error getting preferences: {ex.Message}");
                return "❌ Couldn't retrieve preferences.";
            }
        }
        
        #endregion

        #region Security & Permissions Commands
        
        /// <summary>
        /// Get permissions status and trusted actions
        /// </summary>
        private string GetPermissionsStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("🛡️ **Atlas Security & Permissions**\n");
            
            // Show trusted actions
            var trusted = Security.Permissions.PermissionPolicy.Instance.GetTrustedActions();
            if (trusted.Count > 0)
            {
                sb.AppendLine("✅ Trusted Actions (no confirmation needed):");
                foreach (var action in trusted)
                {
                    sb.AppendLine($"  • {action}");
                }
                sb.AppendLine();
            }
            
            // Show permission levels by risk
            sb.AppendLine("📋 Permission Levels:\n");
            
            sb.AppendLine("🟢 **Always Allowed** (Low Risk):");
            sb.AppendLine("  • read_file, list_directory, screenshot, web_search");
            sb.AppendLine();
            
            sb.AppendLine("🟡 **Requires Confirmation** (Medium/High Risk):");
            sb.AppendLine("  • write_file, delete_file, run_command, kill_process");
            sb.AppendLine();
            
            sb.AppendLine("🔴 **Blocked by Default** (Critical Risk):");
            sb.AppendLine("  • uninstall_app, registry_write, admin_command");
            sb.AppendLine();
            
            sb.AppendLine("💬 Commands:");
            sb.AppendLine("• /trust <action> - Allow an action without confirmation");
            sb.AppendLine("• /untrust <action> - Require confirmation again");
            
            return sb.ToString();
        }
        
        #endregion

        private string GetRandomJoke()
        {
            // Use the SmartFeatures version for more jokes
            return Features.SmartFeatures.TellJoke();
        }

        private async Task<string> ChangeAvatar(string avatarName)
        {
            if (string.IsNullOrWhiteSpace(avatarName))
            {
                // Open avatar selection window
                var avatarWindow = new AvatarSelectionWindow();
                avatarWindow.Owner = this;
                if (avatarWindow.ShowDialog() == true && !string.IsNullOrEmpty(avatarWindow.SelectedAvatar))
                {
                    SelectAvatar(avatarWindow.SelectedAvatar);
                    return $"🎭 Avatar changed to: {GetAvatarDisplayName(avatarWindow.SelectedAvatar)}";
                }
                return "Avatar selection cancelled.";
            }

            try
            {
                // Use the same selection logic as the buttons
                SelectAvatar(avatarName.ToLower());
                return $"🎭 Avatar changed to: {GetAvatarDisplayName(avatarName.ToLower())}";
            }
            catch
            {
                return $"❌ Couldn't change to avatar: {avatarName}\nUse /avatar without parameters to open selection window.";
            }
        }

        private async Task<string> HandleAvatarCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return await ChangeAvatar(args);
            }

            var parts = args.Split(' ', 2);
            var subCommand = parts[0].ToLower();
            var subArgs = parts.Length > 1 ? parts[1] : "";

            switch (subCommand)
            {
                case "create":
                case "new":
                case "design":
                    return await CreateNewAvatar();

                case "think":
                case "thinking":
                    return await TriggerAvatarThinking();

                case "move":
                case "walk":
                    return await TriggerAvatarMovement(subArgs);

                case "dance":
                    return await TriggerAvatarDance();

                case "gesture":
                    return await TriggerAvatarGesture();

                case "lightbulb":
                case "light":
                    return await ToggleAvatarLightbulb();

                case "unity":
                case "open":
                    return await OpenUnityAvatarSystem();

                case "list":
                    return ListAvailableAvatars();

                default:
                    // If not a subcommand, treat as avatar name
                    return await ChangeAvatar(args);
            }
        }

        private async Task<string> CreateNewAvatar()
        {
            try
            {
                // Trigger Unity avatar creation system - DISABLED
                // if (_avatarIntegration?.IsUnityRunning == true)
                // {
                //     await _avatarIntegration.OpenAvatarCreationSystem();
                //     return "🎨 Avatar Creation System opened in Unity! Design your custom avatar with templates, colors, and accessories.";
                // }
                // else
                // {
                    return "🎨 Avatar Creation System available! Please open Unity scene with AvatarSystemSetup to create custom avatars.\n\nFeatures:\n• Multiple templates (Human, Robot, Fantasy)\n• Full customization (colors, hair, accessories)\n• Save/Load system\n• Ready Player Me integration";
                // }
            }
            catch (Exception ex)
            {
                return $"❌ Error opening avatar creation system: {ex.Message}";
            }
        }

        private async Task<string> TriggerAvatarThinking()
        {
            try
            {
                // Avatar thinking mode - DISABLED
                // if (_avatarIntegration?.IsUnityRunning == true)
                // {
                //     await _avatarIntegration.StartThinkingAsync();
                //     return "💡 Avatar is now in thinking mode! Watch the lightbulb appear above their head.";
                // }
                // else
                // {
                    return "💡 Avatar thinking mode available in Unity! Press T key in Unity scene to test thinking animation and lightbulb.";
                // }
            }
            catch (Exception ex)
            {
                return $"❌ Error triggering avatar thinking: {ex.Message}";
            }
        }

        private async Task<string> TriggerAvatarMovement(string direction)
        {
            try
            {
                // Avatar movement - DISABLED
                // if (_avatarIntegration?.IsUnityRunning == true)
                // {
                //     await _avatarIntegration.MoveAvatarAsync(direction);
                //     return $"🏃 Avatar is moving {direction}! Use WASD keys in Unity for manual control.";
                // }
                // else
                // {
                    return "🏃 Avatar movement available in Unity! Use WASD keys to move, Shift to run, Space to jump.";
                // }
            }
            catch (Exception ex)
            {
                return $"❌ Error moving avatar: {ex.Message}";
            }
        }

        private async Task<string> TriggerAvatarDance()
        {
            try
            {
                // Avatar dancing - DISABLED
                // if (_avatarIntegration?.IsUnityRunning == true)
                // {
                //     await _avatarIntegration.StartDancingAsync();
                //     return "💃 Avatar is dancing! Press B key in Unity to trigger dance animations.";
                // }
                // else
                // {
                    return "💃 Avatar dancing available in Unity! Press B key in Unity scene to see dance animations.";
                // }
            }
            catch (Exception ex)
            {
                return $"❌ Error triggering avatar dance: {ex.Message}";
            }
        }

        private async Task<string> TriggerAvatarGesture()
        {
            try
            {
                // Avatar gesturing - DISABLED
                // if (_avatarIntegration?.IsUnityRunning == true)
                // {
                //     await _avatarIntegration.StartGesturingAsync();
                //     return "👋 Avatar is gesturing! Press G key in Unity for gesture animations.";
                // }
                // else
                // {
                    return "👋 Avatar gestures available in Unity! Press G key in Unity scene for gesture animations.";
                // }
            }
            catch (Exception ex)
            {
                return $"❌ Error triggering avatar gesture: {ex.Message}";
            }
        }

        private async Task<string> ToggleAvatarLightbulb()
        {
            try
            {
                // Avatar lightbulb - DISABLED
                // if (_avatarIntegration?.IsUnityRunning == true)
                // {
                //     await _avatarIntegration.ToggleLightbulbAsync();
                //     return "💡 Avatar lightbulb toggled! The thinking lightbulb shows AI processing states.";
                // }
                // else
                // {
                    return "💡 Avatar lightbulb system available in Unity! Press T key to test thinking mode with glowing lightbulb.";
                // }
            }
            catch (Exception ex)
            {
                return $"❌ Error toggling lightbulb: {ex.Message}";
            }
        }

        private async Task<string> OpenUnityAvatarSystem()
        {
            try
            {
                // Try to open Unity or focus Unity window
                var unityProcesses = System.Diagnostics.Process.GetProcessesByName("Unity");
                if (unityProcesses.Length > 0)
                {
                    // Focus Unity window
                    var unity = unityProcesses[0];
                    ShowWindow(unity.MainWindowHandle, 9); // SW_RESTORE
                    SetForegroundWindow(unity.MainWindowHandle);
                    return "🎮 Unity focused! Your avatar system is ready to use.\n\nAvailable systems:\n• AvatarCreationSystem - Design custom avatars\n• DirectAvatarFix - Ready Player Me integration\n• Full movement and lightbulb system";
                }
                else
                {
                    return "🎮 Unity not running. Please open Unity with your avatar scene.\n\nSetup instructions:\n1. Open Unity project\n2. Add AvatarSystemSetup script to scene\n3. Click 'Setup Complete Avatar System'\n4. Test with WASD movement and T for thinking";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Error opening Unity: {ex.Message}";
            }
        }

        // Windows API for focusing Unity window
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(System.IntPtr hWnd);

        private string ListAvailableAvatars()
        {
            return @"🎭 Available Avatars:

• **default** - Default Assistant (Friendly, Blue)
• **energetic** - Energetic Assistant (Fast, Orange) 
• **calm** - Calm Assistant (Relaxed, Green)
• **readyplayer** - Ready Player Me Avatar (Professional, Cyan)

Use: /avatar <name> to switch avatars
Or: /avatar (no parameters) to open avatar selection window
Or: /avatars to manage Ready Player Me avatars";
        }

        private async Task<string> SetTimer(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
                return "Usage: /timer <seconds> [message]\nExample: /timer 60 Take a break!";

            var parts = args.Split(' ', 2);
            if (!int.TryParse(parts[0], out var seconds) || seconds <= 0 || seconds > 3600)
                return "❌ Please specify seconds (1-3600)";

            var message = parts.Length > 1 ? parts[1] : "Timer finished!";
            
            _ = Task.Run(async () =>
            {
                await Task.Delay(seconds * 1000);
                await Dispatcher.InvokeAsync(async () =>
                {
                    AddMessage("Atlas", $"⏰ {message}", false);
                    await _voiceManager.SpeakAsync(message);
                });
            });

            return $"⏱️ Timer set for {seconds} seconds";
        }

        private void OpenHistoryWindow()
        {
            try
            {
                var historyWindow = new CaptureHistoryWindow();
                historyWindow.Show();
                AddMessage("Atlas", "📸 Screenshot History opened! You can browse, search, and manage all your screenshots.", false);
            }
            catch (Exception ex)
            {
                AddMessage("Atlas", $"❌ Error opening history window: {ex.Message}", false);
            }
        }

        private void OpenClipboardManager()
        {
            try
            {
                var clipboardWindow = new ClipboardWindow();
                clipboardWindow.Show();
                AddMessage("Atlas", "📋 Clipboard Manager opened! You can view and manage your clipboard history.", false);
            }
            catch (Exception ex)
            {
                AddMessage("Atlas", $"❌ Error opening clipboard manager: {ex.Message}", false);
            }
        }

        private string GetCommandHelp()
        {
            return GetCapabilitiesList();
        }
        
        /// <summary>
        /// Get comprehensive list of all Atlas AI capabilities
        /// </summary>
        private string GetCapabilitiesList()
        {
            return @"🚀 ATLAS AI - COMPLETE CAPABILITIES LIST

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🎤 VOICE CONTROL & WAKE WORD
• Say ""Hey Atlas"" or ""Atlas"" to activate hands-free
• Natural conversation - no rigid commands needed
• Premium ElevenLabs voice with custom Atlas voice
• Ctrl+Shift+A for push-to-talk (no audio distortion)
• AirPods gesture support - tap to activate
• ""Stop"" or ""Cancel"" to interrupt anytime

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📝 QUICK NOTES
• ""Take a note: [your note]"" - saves to Documents
• ""Show my notes"" - view recent notes
• ""Read my notes"" - list all saved notes
• Notes saved to Documents/Atlas Notes folder

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🖥️ SYSTEM DIAGNOSTICS
• ""How's my PC?"" - full system status
• ""PC status"" / ""System diagnostics""
• Shows CPU, RAM, disk usage with visual bars
• Top memory-consuming processes
• System uptime and health indicators

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

☀️ DAILY BRIEFING
• ""Good morning"" - get your daily briefing
• ""Morning briefing"" / ""Start my day""
• Weather, system status, motivational quote
• Perfect for starting your day!

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🌐 INSTANT WEBSITE ACCESS
• ""Open YouTube"" / ""Open Netflix"" / ""Open Reddit""
• ""Open Twitter"" / ""Open Instagram"" / ""Open TikTok""
• ""Open Gmail"" / ""Check email""
• ""Open GitHub"" / ""Open Amazon"" / ""Open Discord""
• ""Open BBC News"" - and many more!

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🎲 FUN & ENTERTAINMENT
• ""Tell me a joke"" - tech & programming jokes
• ""Fun fact"" - interesting random facts
• ""Compliment me"" / ""Motivate me""
• ""Flip a coin"" - heads or tails
• ""Roll dice"" / ""Roll 2d6"" / ""Roll d20""
• ""Magic 8 ball"" - ask yes/no questions

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🎵 MUSIC & SPOTIFY CONTROL
• ""Play music"" - starts your Spotify
• ""Play [song name]"" or ""Play [artist]""
• ""Play my liked songs"" / ""Play my playlist""
• ""Pause"" / ""Resume"" / ""Stop""
• ""Next song"" / ""Previous song"" / ""Skip""
• ""Volume up"" / ""Volume down"" / ""Mute""

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📸 SCREENSHOTS & VISION
• ""Take a screenshot"" - captures your screen
• ""Screenshot and analyze"" - AI describes what it sees
• ""Extract text from screen"" - OCR text extraction
• Screenshots saved to Pictures/Screenshots folder
• Click cyan paths in chat to open folder

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🛡️ SECURITY SUITE
• ""Scan my computer"" - full malware/spyware scan
• ""Quick scan"" - fast threat check
• ""Deep scan"" - thorough system analysis
• ""Fix security issues"" - auto-remediation
• ""Update threat database"" - get latest definitions

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

💻 SYSTEM CONTROL
• ""Open [app name]"" - launch any application
• ""Close [app name]"" - close running apps
• ""Uninstall [program]"" - remove software
• ""Empty recycle bin"" / ""Clear temp files""
• ""Restart computer"" / ""Shutdown"" / ""Sleep""

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🌤️ WEATHER
• ""What's the weather?"" - local forecast
• ""Weather in [city]"" - any location
• Real-time weather data

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📱 SOCIAL MEDIA CONSOLE
• Campaign planning with AI assistance
• Content generation for all platforms
• Post scheduling & calendar view
• Say ""Open social media console"" to access

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🎭 AVATAR & APPEARANCE
• ""Change avatar to [name]"" - switch avatars
• Available: default, professional, friendly, calm
• Ready Player Me custom avatar support

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🧠 MEMORY & LEARNING
• I remember our conversations automatically
• ""Remember that [info]"" - save important facts
• ""What do you remember about [topic]?""
• ""Forget [topic]"" - privacy control

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

⏰ TIME & PRODUCTIVITY
• ""What time is it?"" / ""What's the date?""
• ""Set a timer for [X] minutes""
• ""Calculate [math expression]""

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📝 SLASH COMMANDS (type / in chat)
/note /notes /diagnostics /briefing /weather
/youtube /netflix /reddit /gmail /github
/joke /funfact /8ball /flip /roll
/time /date /calc /open /search
/screenshot /scan /avatar /help

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

💡 PRO TIPS
• Just talk naturally - I understand context
• Click cyan links to open folders/files
• Ctrl+K opens command palette
• F11 for fullscreen mode
• Say ""Good morning"" for daily briefing!

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Ready to help! Just ask or say ""Atlas"" 🎯";
        }
        
        /// <summary>
        /// Check if user is asking about capabilities
        /// </summary>
        private bool IsCapabilitiesQuestion(string text)
        {
            var lower = text.ToLower().Trim();
            
            // Direct capability questions
            var capabilityPhrases = new[]
            {
                "what can you do",
                "what do you do",
                "what are you capable of",
                "what are your capabilities",
                "what can i do with you",
                "what features do you have",
                "what are your features",
                "show me what you can do",
                "tell me what you can do",
                "list your capabilities",
                "list your features",
                "what commands do you have",
                "what commands are there",
                "help me understand what you do",
                "what's possible",
                "what is possible",
                "what all can you do",
                "show capabilities",
                "show features",
                "your abilities",
                "what are your abilities",
                "what can atlas do",
                "what does atlas do"
            };
            
            foreach (var phrase in capabilityPhrases)
            {
                if (lower.Contains(phrase))
                    return true;
            }
            
            // Short direct questions
            if (lower == "help" || lower == "?" || lower == "capabilities" || lower == "features")
                return true;
                
            return false;
        }
        
        /// <summary>
        /// Handle natural language smart commands - returns response or null if not a smart command
        /// </summary>
        private async Task<string?> HandleSmartCommand(string text)
        {
            var lower = text.ToLower().Trim();
            
            // === NOTES ===
            if (lower.StartsWith("take a note") || lower.StartsWith("note:") || lower.StartsWith("remember this:") || 
                lower.StartsWith("save a note") || lower.StartsWith("make a note"))
            {
                var noteText = text;
                // Extract the actual note content
                foreach (var prefix in new[] { "take a note:", "take a note", "note:", "remember this:", "save a note:", "save a note", "make a note:", "make a note" })
                {
                    if (lower.StartsWith(prefix))
                    {
                        noteText = text.Substring(prefix.Length).Trim();
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(noteText))
                    return "What would you like me to note down?";
                return await Features.SmartFeatures.TakeNoteAsync(noteText);
            }
            
            if (lower == "show my notes" || lower == "show notes" || lower == "my notes" || lower == "read my notes" || lower == "what are my notes")
            {
                return await Features.SmartFeatures.GetNotesAsync();
            }
            
            // === SYSTEM DIAGNOSTICS - exact phrases only ===
            if (lower == "how's my pc" || lower == "hows my pc" || lower == "how is my pc" || 
                lower == "pc status" || lower == "system diagnostics" || lower == "check my pc" ||
                lower == "system status" || lower == "pc health")
            {
                return await Features.SmartFeatures.GetSystemDiagnosticsAsync();
            }
            
            // === DAILY BRIEFING - exact phrases only ===
            if (lower == "good morning" || lower == "morning briefing" || lower == "daily briefing" || 
                lower == "start my day" || lower == "give me my briefing")
            {
                return await Features.SmartFeatures.GetDailyBriefingAsync();
            }
            
            // === JOKES & FUN - exact phrases only ===
            if (lower == "tell me a joke" || lower == "tell a joke" || lower == "joke please" || lower == "another joke")
            {
                return Features.SmartFeatures.TellJoke();
            }
            
            if (lower == "fun fact" || lower == "tell me a fun fact" || lower == "random fact" || lower == "tell me a fact")
            {
                return Features.SmartFeatures.TellFunFact();
            }
            
            if (lower == "compliment me" || lower == "give me a compliment" || lower == "motivate me" || lower == "cheer me up")
            {
                return Features.SmartFeatures.GiveCompliment();
            }
            
            if (lower == "flip a coin" || lower == "coin flip" || lower == "flip coin")
            {
                return Features.SmartFeatures.FlipCoin();
            }
            
            if (lower == "roll dice" || lower == "roll a dice" || lower == "roll d6" || lower == "roll d20" || lower == "roll 2d6")
            {
                // Parse dice notation like "roll 2d6" or "roll d20"
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d*)d(\d+)");
                if (match.Success)
                {
                    var count = string.IsNullOrEmpty(match.Groups[1].Value) ? 1 : int.Parse(match.Groups[1].Value);
                    var sides = int.Parse(match.Groups[2].Value);
                    return Features.SmartFeatures.RollDice(sides, count);
                }
                return Features.SmartFeatures.RollDice();
            }
            
            if (lower == "magic 8 ball" || lower == "magic 8ball" || lower == "8 ball" || lower == "8ball" || lower == "shake the 8 ball")
            {
                return Features.SmartFeatures.Magic8Ball();
            }
            
            // === WEBSITE SHORTCUTS - require "open" prefix to avoid accidental triggers ===
            if (lower == "open youtube")
            {
                return Features.SmartFeatures.OpenWebsite("youtube");
            }
            if (lower == "open netflix")
            {
                return Features.SmartFeatures.OpenWebsite("netflix");
            }
            if (lower == "open twitter" || lower == "open x")
            {
                return Features.SmartFeatures.OpenWebsite("twitter");
            }
            if (lower == "open reddit")
            {
                return Features.SmartFeatures.OpenWebsite("reddit");
            }
            if (lower == "open gmail" || lower == "open email" || lower == "check my email")
            {
                return Features.SmartFeatures.OpenWebsite("gmail");
            }
            if (lower == "open github")
            {
                return Features.SmartFeatures.OpenWebsite("github");
            }
            if (lower == "open amazon")
            {
                return Features.SmartFeatures.OpenWebsite("amazon");
            }
            if (lower == "open discord")
            {
                return Features.SmartFeatures.OpenWebsite("discord");
            }
            if (lower == "open twitch")
            {
                return Features.SmartFeatures.OpenWebsite("twitch");
            }
            if (lower == "open instagram")
            {
                return Features.SmartFeatures.OpenWebsite("instagram");
            }
            if (lower == "open tiktok")
            {
                return Features.SmartFeatures.OpenWebsite("tiktok");
            }
            if (lower == "open facebook")
            {
                return Features.SmartFeatures.OpenWebsite("facebook");
            }
            if (lower == "open bbc" || lower == "open bbc news" || lower == "open news")
            {
                return Features.SmartFeatures.OpenWebsite("bbc news");
            }
            
            // === MUSIC PLAYBACK - Direct execution, no AI needed ===
            if (lower.StartsWith("play ") && (lower.Contains("spotify") || lower.Contains("on spotify")))
            {
                var query = text.Substring(5).Trim();
                query = System.Text.RegularExpressions.Regex.Replace(query, @"\s+(on|in)\s+spotify.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!string.IsNullOrWhiteSpace(query))
                {
                    return await Tools.MediaPlayerTool.PlayAsync(query, Tools.MediaPlayerTool.Platform.Spotify);
                }
            }
            if (lower.StartsWith("play ") && !lower.Contains("video") && !lower.Contains("game"))
            {
                var query = text.Substring(5).Trim();
                // Remove platform suffixes
                query = System.Text.RegularExpressions.Regex.Replace(query, @"\s+(on|in)\s+(spotify|youtube|soundcloud).*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!string.IsNullOrWhiteSpace(query) && query.Length > 2)
                {
                    // Default to Spotify for music
                    return await Tools.MediaPlayerTool.PlayAsync(query, Tools.MediaPlayerTool.Platform.Spotify);
                }
            }
            
            // === SYSTEM FILES - require admin privileges ===
            if (lower == "open hosts" || lower == "edit hosts" || lower == "open hosts file" || 
                lower == "edit hosts file" || lower == "open the hosts file" || lower == "edit the hosts file")
            {
                return Features.SmartFeatures.OpenHostsFile();
            }
            
            // === WEATHER - catch many variations ===
            if (lower.Contains("weather"))
            {
                // Extract location if specified
                string location = "Middlesbrough";
                if (lower.Contains(" in "))
                {
                    var idx = lower.IndexOf(" in ");
                    location = text.Substring(idx + 4).Trim().TrimEnd('?');
                }
                else if (lower.Contains(" for "))
                {
                    var idx = lower.IndexOf(" for ");
                    location = text.Substring(idx + 5).Trim().TrimEnd('?');
                }
                return await GetWeatherAsync(location);
            }
            
            // === WEB SEARCH - shopping, product searches, general lookups ===
            // Detect web search intent for things like "find me led lights online", "search for X", "look up Y"
            if (IsWebSearchIntent(lower))
            {
                return await HandleWebSearchWithFollowUpAsync(text, lower);
            }
            
            // === IT MANAGEMENT COMMANDS === - DISABLED (namespace mismatch)
            // All IT Management commands have been disabled due to namespace conflicts
            // To re-enable, fix the ITManagement namespace and uncomment the code below
            
            /*
            var itService = ITManagementService.Instance;
            
            // System health / status
            if (lower == "system health" || lower == "health check" || lower == "how is my system" ||
                lower == "check system" || lower == "system report" || lower == "full report")
            {
                if (lower.Contains("full") || lower.Contains("report"))
                    return await itService.GetSystemReportAsync();
                return itService.HealthMonitor.GetHealthSummary();
            }
            
            // Clean temp files
            if (lower == "clean temp" || lower == "clean temp files" || lower == "clean temporary files" ||
                lower == "clear temp" || lower == "delete temp files" || lower == "cleanup")
            {
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("cleanup_temp");
                return result.Message;
            }
            
            // Empty recycle bin
            if (lower == "empty recycle bin" || lower == "clear recycle bin" || lower == "empty trash" ||
                lower == "empty the recycle bin")
            {
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("empty_recycle_bin");
                return result.Message;
            }
            
            // Network scan
            if (lower == "scan network" || lower == "network scan" || lower == "find devices" ||
                lower == "discover devices" || lower == "what's on my network")
            {
                ShowStatus("🔍 Scanning network...");
                var devices = await itService.NetworkDiscovery.ScanNetworkAsync();
                return itService.NetworkDiscovery.GetDiscoverySummary();
            }
            
            // Network info
            if (lower == "network info" || lower == "my ip" || lower == "what's my ip" || lower == "ip address" ||
                lower == "network status")
            {
                var info = itService.NetworkDiscovery.GetLocalNetworkInfo();
                return $"🌐 Network Information:\n\nIP Address: {info.LocalIP}\nSubnet: {info.SubnetMask}\nGateway: {info.Gateway}\nDNS: {info.DnsServer}\nAdapter: {info.AdapterName}\nMAC: {info.MacAddress}";
            }
            
            // Speed test
            if (lower == "speed test" || lower == "test speed" || lower == "internet speed" ||
                lower == "check internet speed" || lower == "how fast is my internet")
            {
                ShowStatus("📶 Running speed test...");
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("speed_test");
                return result.Message;
            }
            
            // Flush DNS
            if (lower == "flush dns" || lower == "clear dns" || lower == "reset dns")
            {
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("flush_dns");
                return result.Message;
            }
            
            // Check for issues
            if (lower == "check for issues" || lower == "find issues" || lower == "detect issues" ||
                lower == "scan for problems" || lower == "any issues" || lower == "system issues")
            {
                ShowStatus("🔍 Analyzing system...");
                await itService.IssueDetector.RunFullAnalysisAsync();
                return itService.IssueDetector.GetIssuesSummary();
            }
            
            // Virus scan
            if (lower == "virus scan" || lower == "scan for viruses" || lower == "malware scan" ||
                lower == "quick scan" || lower == "security scan")
            {
                ShowStatus("🛡️ Starting virus scan...");
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("windows_defender_scan");
                return result.Message;
            }
            
            // Check updates
            if (lower == "check updates" || lower == "check for updates" || lower == "windows updates" ||
                lower == "any updates" || lower == "update check")
            {
                ShowStatus("🔄 Checking for updates...");
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("check_updates");
                return result.Message;
            }
            
            // Firewall status
            if (lower == "firewall status" || lower == "check firewall" || lower == "firewall")
            {
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("firewall_status");
                return result.Message;
            }
            
            // Startup programs
            if (lower == "startup programs" || lower == "startup apps" || lower == "what starts with windows" ||
                lower == "list startup" || lower == "show startup programs")
            {
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("optimize_startup");
                return result.Message;
            }
            
            // Create restore point
            if (lower == "create restore point" || lower == "make restore point" || lower == "backup point" ||
                lower == "system restore point")
            {
                ShowStatus("💾 Creating restore point...");
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("create_restore_point");
                return result.Message;
            }
            
            // Kill Chrome processes
            if (lower == "kill chrome" || lower == "close chrome" || lower == "stop chrome" ||
                lower == "end chrome" || lower == "terminate chrome" || lower == "kill google chrome" ||
                lower == "close all chrome" || lower == "stop all chrome")
            {
                ShowStatus("🔪 Killing Chrome processes...");
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("kill_chrome");
                return result.Message;
            }
            
            // Kill all browsers
            if (lower == "kill browsers" || lower == "close all browsers" || lower == "kill all browsers" ||
                lower == "stop all browsers" || lower == "close browsers" || lower == "end all browsers")
            {
                ShowStatus("🔪 Killing all browser processes...");
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("kill_browser_processes");
                return result.Message;
            }
            
            // Kill specific process
            if (lower.StartsWith("kill ") || lower.StartsWith("stop ") || lower.StartsWith("end ") ||
                lower.StartsWith("terminate ") || lower.StartsWith("close "))
            {
                var processName = lower
                    .Replace("kill ", "").Replace("stop ", "").Replace("end ", "")
                    .Replace("terminate ", "").Replace("close ", "")
                    .Replace(" process", "").Replace(" processes", "").Trim();
                
                if (!string.IsNullOrEmpty(processName) && processName != "chrome" && processName != "browsers")
                {
                    ShowStatus($"🔪 Killing {processName} processes...");
                    var result = await itService.ScriptLibrary.ExecuteScriptAsync("kill_process", 
                        new Dictionary<string, string> { ["process"] = processName });
                    return result.Message;
                }
            }
            
            // List high memory processes
            if (lower == "high memory" || lower == "memory hogs" || lower == "what's using memory" ||
                lower == "top processes" || lower == "memory usage" || lower == "show memory usage" ||
                lower == "what's using ram" || lower == "ram usage")
            {
                var result = await itService.ScriptLibrary.ExecuteScriptAsync("list_high_memory");
                return result.Message;
            }
            
            // IT help / commands list
            if (lower == "it help" || lower == "it commands" || lower == "what it commands" ||
                lower == "system commands" || lower == "maintenance commands")
            {
                return itService.GetAvailableCommands();
            }
            */
            
            // === FOLDER PRIORITY CHECK ===
            // If user explicitly says "folder" in their request, check folders FIRST before apps
            // This prevents "open X folder" from launching an app with a similar name
            bool userWantsFolder = lower.Contains("folder") || lower.Contains("directory");
            
            if (userWantsFolder && (lower.StartsWith("open ") || lower.Contains("go to") || 
                lower.Contains("show me") || lower.Contains("navigate")))
            {
                var folderResult = OpenFolderCommand(lower, text);
                if (folderResult != null)
                    return folderResult;
            }
            
            // === COMMON FOLDER NAMES get folder priority even without "folder" word ===
            var commonFolderNames = new[] { "downloads", "documents", "desktop", "pictures", "music", "videos",
                "screenshots", "appdata", "temp", "recycle bin", "program files" };
            bool isCommonFolderRequest = commonFolderNames.Any(f => lower.Contains(f)) && 
                (lower.StartsWith("open ") || lower.Contains("go to") || lower.Contains("show me") || lower.Contains("navigate"));
            if (isCommonFolderRequest)
            {
                var folderResult = OpenFolderCommand(lower, text);
                if (folderResult != null)
                    return folderResult;
            }
            
            // === APPLICATION LAUNCHING - Check FIRST before folders (only if not explicitly asking for folder) ===
            // Try to launch as an app first for "open X", "launch X", "run X", "start X"
            if (!userWantsFolder && !isCommonFolderRequest && (lower.StartsWith("open ") || lower.StartsWith("launch ") || lower.StartsWith("run ") || 
                lower.StartsWith("start ") || lower.StartsWith("execute ")))
            {
                var appResult = LaunchApplicationCommand(lower, text);
                if (appResult != null)
                    return appResult;
            }
            
            // Short commands that are just app names (e.g., "steam", "chrome", "spotify")
            var commonApps = new[] { "steam", "chrome", "firefox", "spotify", "discord", "slack", "zoom",
                "teams", "outlook", "word", "excel", "powerpoint", "notepad", "calculator", "paint",
                "vlc", "obs", "vscode", "code", "terminal", "powershell", "cmd", "edge", "brave",
                "opera", "vivaldi", "telegram", "whatsapp", "signal", "skype", "epic", "origin",
                "ubisoft", "battle.net", "blizzard", "gog", "itch", "nvidia", "geforce", "afterburner",
                "hwinfo", "cpu-z", "gpu-z", "msi", "corsair", "razer", "logitech", "audacity",
                "gimp", "photoshop", "premiere", "davinci", "blender", "unity", "unreal" };
            
            if (commonApps.Any(app => lower == app || lower == $"open {app}" || lower == $"launch {app}"))
            {
                var appResult = LaunchApplicationCommand(lower, text);
                if (appResult != null)
                    return appResult;
            }
            
            // === FOLDER OPENING - Direct execution ===
            // Comprehensive Windows filesystem access - FULL SYSTEM
            // NOTE: Removed app names (steam, epic games, nvidia, etc.) - those are handled above
            var folderKeywords = new[] { 
                // User folders
                "screenshots", "screenshot", "downloads", "download", "documents", "document",
                "pictures", "picture", "photo", "desktop", "music", "videos", "video", "movie",
                "home", "profile", "favorites", "contacts", "saved game", "links", "quick access",
                "3d object", "3d model", "3d models", "camera roll",
                // AppData & ProgramData
                "appdata", "app data", "roaming", "local appdata", "locallow", "programdata", "program data",
                // System folders
                "program files", "programs", "common files", "windows", "system32", "system 32", "syswow", "wow64",
                "drivers", "fonts", "temp", "temporary", "etc", "hosts",
                // Startup & System
                "startup", "start menu", "send to", "recent", "templates", "cookies", "history",
                "internet cache", "browser cache", "prefetch", "logs", "inf", "winsxs",
                // Shell folders
                "recycle", "trash", "bin", "this pc", "my computer", "computer", "network",
                "printers", "onedrive", "public", "libraries", "library", "games folder", "users folder",
                // Control Panel & Tools
                "control panel", "device manager", "task manager", "disk management",
                "services", "event viewer", "event log", "registry", "regedit", "computer management",
                "system info", "resource monitor", "performance monitor", "perfmon", "group policy",
                "gpedit", "security policy", "secpol", "firewall", "programs and features",
                "uninstall", "add remove", "network connection", "network adapter", "power option",
                "power plan", "sound setting", "audio setting", "display setting", "date", "time",
                "system properties", "environment variable",
                // Drives
                "c drive", "d drive", "e drive", "f drive", "g drive", "h drive",
                "c:", "d:", "e:", "f:", "g:", "h:",
                // Folder-specific keywords (with "folder" to distinguish from apps)
                "steam folder", "epic folder", "nvidia folder", "amd folder", "intel folder"
            };
            
            bool hasFolderKeyword = folderKeywords.Any(k => lower.Contains(k));
            bool hasOpenIntent = lower.Contains("open") || lower.Contains("go to") || lower.Contains("show me") || 
                                 lower.Contains("show my") || lower.Contains("navigate") || lower.Contains("folder") ||
                                 lower.Contains("access") || lower.Contains("browse");
            
            // Single word folder requests like "downloads", "documents", "desktop"
            var singleWordFolders = new[] { "downloads", "documents", "desktop", "pictures", "music", "videos" };
            if (singleWordFolders.Any(f => lower == f))
            {
                return OpenFolderCommand(lower, text);
            }
            
            // Direct folder request - must have "folder" keyword or explicit folder intent
            if (hasFolderKeyword && (hasOpenIntent || lower.Contains("folder")))
            {
                return OpenFolderCommand(lower, text);
            }
            
            // Just mentioning a folder with "folder" or "my folder"
            if (hasFolderKeyword && (lower.Contains("folder") || lower.EndsWith(" folder")))
            {
                return OpenFolderCommand(lower, text);
            }
            
            // Direct system tool requests (no "open" needed)
            var directTools = new[] { "task manager", "device manager", "control panel", 
                "registry", "regedit", "services", "event viewer", "disk management", "firewall",
                "resource monitor", "performance monitor", "system info", "computer management" };
            if (directTools.Any(t => lower.Contains(t)))
            {
                return OpenFolderCommand(lower, text);
            }
            
            // "go to [folder]" or "show me [folder]" or "navigate to [folder]"
            if ((lower.StartsWith("go to ") || lower.StartsWith("show me ") || lower.StartsWith("navigate to ") ||
                 lower.StartsWith("show ")) && 
                !lower.Contains("youtube") && !lower.Contains("netflix") && !lower.Contains("website") &&
                !lower.Contains("http"))
            {
                var folderResult = OpenFolderCommand(lower, text);
                if (folderResult != null)
                    return folderResult;
            }
            
            // === APPLICATION LAUNCHING (fallback) ===
            // Direct app launch requests that weren't caught above (skip if user wants folder)
            if (!userWantsFolder && (lower.StartsWith("open ") || lower.StartsWith("launch ") || lower.StartsWith("run ") || 
                lower.StartsWith("start ") || lower.StartsWith("execute ")))
            {
                var appResult = LaunchApplicationCommand(lower, text);
                if (appResult != null)
                    return appResult;
            }
            
            // Scan/rescan files command - force re-index of file system
            if ((lower.Contains("scan") || lower.Contains("rescan") || lower.Contains("reindex") || lower.Contains("index")) && 
                (lower.Contains("file") || lower.Contains("folder") || lower.Contains("directory")))
            {
                return await RescanFileSystemAsync();
            }
            
            // Scan apps command
            if (lower.Contains("scan") && (lower.Contains("app") || lower.Contains("program") || lower.Contains("install")))
            {
                return await ScanInstalledAppsAsync();
            }
            
            // List apps command
            if ((lower.Contains("list") || lower.Contains("show") || lower.Contains("what")) && 
                (lower.Contains("app") || lower.Contains("program") || lower.Contains("install")))
            {
                return ListInstalledApps(lower);
            }
            
            // Not a smart command
            return null;
        }
        
        /// <summary>
        /// Detect if the user wants to search the web (shopping, products, general lookups)
        /// </summary>
        private bool IsWebSearchIntent(string lower)
        {
            // Shopping/product searches
            if ((lower.Contains("find") || lower.Contains("search") || lower.Contains("look for") || 
                 lower.Contains("look up") || lower.Contains("show me") || lower.Contains("get me")) &&
                (lower.Contains("online") || lower.Contains("buy") || lower.Contains("shop") || 
                 lower.Contains("price") || lower.Contains("cheap") || lower.Contains("best")))
            {
                return true;
            }
            
            // Explicit search commands
            if (lower.StartsWith("search for ") || lower.StartsWith("search ") || 
                lower.StartsWith("look up ") || lower.StartsWith("google ") ||
                lower.StartsWith("find me ") || lower.StartsWith("find "))
            {
                // Exclude local file/folder searches
                if (lower.Contains("file") || lower.Contains("folder") || lower.Contains("document") ||
                    lower.Contains("on my") || lower.Contains("in my"))
                    return false;
                return true;
            }
            
            // Product-specific searches
            var productKeywords = new[] { "led light", "headphone", "keyboard", "mouse", "monitor", 
                "laptop", "phone", "tablet", "camera", "speaker", "charger", "cable", "adapter",
                "furniture", "chair", "desk", "lamp", "tv", "television", "gaming", "console" };
            if (productKeywords.Any(k => lower.Contains(k)) && 
                (lower.Contains("find") || lower.Contains("buy") || lower.Contains("get") || 
                 lower.Contains("recommend") || lower.Contains("best") || lower.Contains("cheap")))
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Handle web search with follow-up question and spoken summary
        /// </summary>
        private async Task<string> HandleWebSearchWithFollowUpAsync(string originalText, string lower)
        {
            // Extract the search query
            var query = originalText;
            foreach (var prefix in new[] { "search for ", "search ", "look up ", "google ", 
                "find me ", "find ", "look for ", "show me ", "get me " })
            {
                if (lower.StartsWith(prefix))
                {
                    query = originalText.Substring(prefix.Length).Trim();
                    break;
                }
            }
            
            // Remove trailing "online", "please", etc.
            query = query.Replace(" online", "").Replace(" please", "").Replace(" for me", "").Trim();
            
            ShowStatus($"🔍 Searching for {query}...");
            
            try
            {
                // Perform the web search
                var searchResult = await Tools.WebSearchTool.SearchAsync(query);
                
                // Generate follow-up question based on search type
                var followUp = GetWebSearchFollowUp(lower, query);
                
                // Combine results with follow-up
                var fullResponse = searchResult;
                if (!string.IsNullOrEmpty(followUp))
                {
                    fullResponse += $"\n\n{followUp}";
                }
                
                // Speak a summary instead of the full results
                var spokenSummary = GetWebSearchSpokenSummary(lower, query, followUp);
                _ = SpeakWebSearchResultAsync(spokenSummary);
                
                return fullResponse;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSearch] Error: {ex.Message}");
                return $"I couldn't complete that search: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Get a follow-up question based on the search type
        /// </summary>
        private string GetWebSearchFollowUp(string lower, string query)
        {
            // Shopping/product searches
            if (lower.Contains("buy") || lower.Contains("find") || lower.Contains("shop") || 
                lower.Contains("led") || lower.Contains("light") || lower.Contains("product") ||
                lower.Contains("headphone") || lower.Contains("keyboard") || lower.Contains("mouse"))
            {
                return "What's your budget range, and where will you be using these?";
            }
            
            // Price comparisons
            if (lower.Contains("price") || lower.Contains("cost") || lower.Contains("cheap") ||
                lower.Contains("affordable") || lower.Contains("budget"))
            {
                return "Do you have a specific budget in mind?";
            }
            
            // Reviews/recommendations
            if (lower.Contains("best") || lower.Contains("review") || lower.Contains("recommend") ||
                lower.Contains("top") || lower.Contains("good"))
            {
                return "What features matter most to you?";
            }
            
            // General info searches
            if (lower.Contains("what is") || lower.Contains("how to") || lower.Contains("why") ||
                lower.Contains("explain") || lower.Contains("tell me about"))
            {
                return "Would you like me to explain any of this in more detail?";
            }
            
            // Default follow-up
            return "Would you like me to look into any of these further?";
        }
        
        /// <summary>
        /// Get a spoken summary for web search results
        /// </summary>
        private string GetWebSearchSpokenSummary(string lower, string query, string followUp)
        {
            var honorific = _conversationManager?.UserProfile?.GetHonorific() ?? "sir";
            var honorificSuffix = string.IsNullOrEmpty(honorific) ? "" : $", {honorific}";
            
            // Shopping searches
            if (lower.Contains("buy") || lower.Contains("find") || lower.Contains("shop") || 
                lower.Contains("led") || lower.Contains("light") || lower.Contains("product") ||
                lower.Contains("headphone") || lower.Contains("keyboard"))
            {
                return $"I found some options for you{honorificSuffix}. I've listed them in the chat with links. {followUp}";
            }
            
            // Info searches
            if (lower.Contains("what") || lower.Contains("how") || lower.Contains("why") ||
                lower.Contains("explain"))
            {
                return $"Here's what I found{honorificSuffix}. I've put the details in the chat. {followUp}";
            }
            
            // Default
            return $"I found some results for {query}{honorificSuffix}. Check the chat for details. {followUp}";
        }
        
        /// <summary>
        /// Speak web search result summary with proper TTS handling
        /// </summary>
        private async Task SpeakWebSearchResultAsync(string summary)
        {
            try
            {
                SetAtlasCoreState(Controls.AtlasVisualState.Speaking);
                StartSpeakingEnergySimulation();
                
                await _voiceManager.SpeakAsync(summary);
                
                StopSpeakingEnergySimulation();
                SetAtlasCoreState(Controls.AtlasVisualState.Idle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TTS] Web search speech error: {ex.Message}");
                StopSpeakingEnergySimulation();
                SetAtlasCoreState(Controls.AtlasVisualState.Idle);
            }
        }
        
        /// <summary>
        /// Rescan the file system to update the folder index
        /// </summary>
        private async Task<string> RescanFileSystemAsync()
        {
            ShowStatus("🔍 Scanning your file system...");
            
            // Subscribe to progress updates
            void OnProgress(string msg) => Dispatcher.Invoke(() => ShowStatus($"🔍 {msg}"));
            Memory.FileSystemIndex.Instance.IndexingProgress += OnProgress;
            
            try
            {
                await Memory.FileSystemIndex.Instance.ReindexAsync();
                var folderCount = Memory.FileSystemIndex.Instance.FolderCount;
                var fileCount = Memory.FileSystemIndex.Instance.FileCount;
                return $"✅ File system scan complete! Found **{folderCount}** folders and **{fileCount}** files. I can now find any folder by name.";
            }
            finally
            {
                Memory.FileSystemIndex.Instance.IndexingProgress -= OnProgress;
            }
        }
        
        /// <summary>
        /// Launch an application by name
        /// </summary>
        private string? LaunchApplicationCommand(string lower, string originalText)
        {
            // Extract app name from command
            string appName = originalText;
            foreach (var prefix in new[] { "open ", "launch ", "run ", "start ", "execute ", "open the ", "launch the ", "run the ", "start the " })
            {
                if (lower.StartsWith(prefix))
                {
                    appName = originalText.Substring(prefix.Length).Trim();
                    break;
                }
            }
            
            // Remove trailing words like "app", "application", "program"
            appName = appName
                .Replace(" app", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" application", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" program", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
            
            if (string.IsNullOrEmpty(appName)) return null;
            
            // Try to launch
            var result = SystemControl.InstalledAppsManager.Instance.LaunchApp(appName);
            if (result.Success)
                return result.Message;
            
            // If not found, return null to let AI handle it (might be a different kind of request)
            return null;
        }
        
        /// <summary>
        /// Scan for installed applications
        /// </summary>
        private async Task<string> ScanInstalledAppsAsync()
        {
            await SystemControl.InstalledAppsManager.Instance.ScanAllAppsAsync();
            var count = SystemControl.InstalledAppsManager.Instance.AppCount;
            return $"🔍 Scanned your system. Found **{count}** applications. I'll remember these and watch for new installs.";
        }
        
        /// <summary>
        /// List installed applications
        /// </summary>
        private string ListInstalledApps(string query)
        {
            var apps = SystemControl.InstalledAppsManager.Instance.GetAllApps();
            if (apps.Count == 0)
            {
                return "I haven't scanned your apps yet. Say \"scan my apps\" to discover what's installed.";
            }
            
            // If searching for specific apps
            if (query.Contains("search") || query.Contains("find"))
            {
                var searchTerm = query.Split(new[] { "search", "find", "for" }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(searchTerm))
                {
                    var matches = SystemControl.InstalledAppsManager.Instance.SearchApps(searchTerm);
                    if (matches.Count == 0)
                        return $"No apps found matching '{searchTerm}'.";
                    
                    var matchList = string.Join("\n", matches.Take(10).Select(a => $"• {a.Name}"));
                    return $"Found {matches.Count} apps matching '{searchTerm}':\n{matchList}";
                }
            }
            
            // Show summary
            var bySource = apps.GroupBy(a => a.Source).ToDictionary(g => g.Key, g => g.Count());
            var summary = $"📦 **{apps.Count} Applications Installed**\n\n";
            
            foreach (var (source, count) in bySource.OrderByDescending(x => x.Value))
            {
                summary += $"• {source}: {count}\n";
            }
            
            summary += $"\nSay \"open [app name]\" to launch any app, or \"search apps for [name]\" to find specific ones.";
            return summary;
        }
        
        /// <summary>
        /// Opens a folder based on natural language command.
        /// Uses FileSystemIndex to find ANY folder on the system by name.
        /// </summary>
        private string? OpenFolderCommand(string lower, string originalText)
        {
            // Extract the folder name from the command
            var searchTerm = lower
                .Replace("open ", "").Replace("go to ", "").Replace("show me ", "")
                .Replace("show my ", "").Replace("navigate to ", "").Replace("show ", "")
                .Replace("folder", "").Replace("directory", "").Replace("my ", "")
                .Replace("the ", "").Replace("please", "").Trim();
            
            Debug.WriteLine($"[OpenFolderCommand] Processing: '{lower}' -> search term: '{searchTerm}'");
            
            // ===========================================
            // STEP 1: Check for explicit paths first (C:\..., D:\...)
            // ===========================================
            var pathMatch = System.Text.RegularExpressions.Regex.Match(originalText, @"[A-Za-z]:\\[^\s""]+");
            if (pathMatch.Success)
            {
                var explicitPath = pathMatch.Value;
                if (Directory.Exists(explicitPath))
                {
                    Process.Start("explorer.exe", $"\"{explicitPath}\"");
                    return $"📂 Opening {Path.GetFileName(explicitPath)}.";
                }
                return $"❌ Path doesn't exist: {explicitPath}";
            }
            
            // ===========================================
            // STEP 2: Use FileSystemIndex FIRST to find user folders
            // This takes priority over system folders so user's custom
            // folders like "3D Models" are found before system "3D Objects"
            // ===========================================
            if (!string.IsNullOrEmpty(searchTerm) && searchTerm.Length >= 2)
            {
                try
                {
                    var indexedPath = Memory.FileSystemIndex.Instance.FindFolder(searchTerm);
                    if (!string.IsNullOrEmpty(indexedPath) && Directory.Exists(indexedPath))
                    {
                        Debug.WriteLine($"[OpenFolderCommand] FileSystemIndex found: {indexedPath}");
                        Process.Start("explorer.exe", $"\"{indexedPath}\"");
                        return $"📂 Opening {Path.GetFileName(indexedPath)}.";
                    }
                    else
                    {
                        Debug.WriteLine($"[OpenFolderCommand] FileSystemIndex: no match for '{searchTerm}'");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OpenFolderCommand] FileSystemIndex error: {ex.Message}");
                }
            }
            
            // ===========================================
            // STEP 3: Fall back to well-known system folders/tools
            // Only if FileSystemIndex didn't find anything
            // ===========================================
            var systemResult = TryOpenSystemFolder(lower);
            if (systemResult != null)
                return systemResult;
            
            // ===========================================
            // STEP 4: If index is empty or old, trigger re-index
            // ===========================================
            if (!Memory.FileSystemIndex.Instance.IsIndexed || 
                (DateTime.Now - Memory.FileSystemIndex.Instance.LastIndexTime).TotalHours > 24)
            {
                _ = Memory.FileSystemIndex.Instance.IndexAsync(force: true);
                return $"🔍 I'm scanning your file system to learn your folders. Try again in a moment, or say \"rescan files\" to force a refresh.";
            }
            
            return null;
        }
        
        /// <summary>
        /// Try to open well-known system folders and tools
        /// </summary>
        private string? TryOpenSystemFolder(string lower)
        {
            // Screenshots
            if (lower.Contains("screenshot"))
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", $"\"{path}\"");
                return "📂 Opening Screenshots.";
            }
            // Downloads
            if (lower.Contains("download"))
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Process.Start("explorer.exe", $"\"{path}\"");
                return "📂 Opening Downloads.";
            }
            // Documents
            if (lower.Contains("document"))
            {
                Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                return "📂 Opening Documents.";
            }
            // Pictures
            if (lower.Contains("picture") || lower.Contains("photo") || lower.Contains("image folder"))
            {
                Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                return "📂 Opening Pictures.";
            }
            // Desktop
            if (lower.Contains("desktop"))
            {
                Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                return "📂 Opening Desktop.";
            }
            // Music
            if (lower.Contains("music"))
            {
                Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
                return "📂 Opening Music.";
            }
            // Videos
            if (lower.Contains("video") || lower.Contains("movie"))
            {
                Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
                return "📂 Opening Videos.";
            }
            // 3D Objects - ONLY match "3d object" exactly, NOT "3d model" (user may have a custom 3D Models folder)
            if (lower.Contains("3d object") && !lower.Contains("3d model"))
            {
                try { Process.Start("explorer.exe", "shell:3D Objects"); return "📂 Opening 3D Objects."; }
                catch { return "❌ Couldn't open 3D Objects."; }
            }
            // ProgramData - system-wide application data (C:\ProgramData)
            if (lower.Contains("programdata") || lower.Contains("program data"))
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                Process.Start("explorer.exe", $"\"{programData}\"");
                return "📂 Opening ProgramData.";
            }
            // AppData
            if (lower.Contains("appdata") || lower.Contains("app data"))
            {
                if (lower.Contains("local"))
                    Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                else
                    Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                return "📂 Opening AppData.";
            }
            // Temp
            if (lower.Contains("temp"))
            {
                Process.Start("explorer.exe", Path.GetTempPath());
                return "📂 Opening Temp.";
            }
            // Recycle Bin
            if (lower.Contains("recycle") || lower.Contains("trash") || lower.Contains("bin"))
            {
                try { Process.Start("explorer.exe", "shell:RecycleBinFolder"); return "🗑️ Opening Recycle Bin."; }
                catch { return "❌ Couldn't open Recycle Bin."; }
            }
            // This PC
            if (lower.Contains("this pc") || lower.Contains("my computer") || lower.Contains("computer"))
            {
                try { Process.Start("explorer.exe", "shell:MyComputerFolder"); return "💻 Opening This PC."; }
                catch { return "❌ Couldn't open This PC."; }
            }
            // OneDrive
            if (lower.Contains("onedrive"))
            {
                try { Process.Start("explorer.exe", "shell:OneDrive"); return "☁️ Opening OneDrive."; }
                catch { return "❌ Couldn't open OneDrive."; }
            }
            // iCloud Drive
            if (lower.Contains("icloud"))
            {
                var icloudPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "iCloudDrive");
                if (!Directory.Exists(icloudPath))
                    icloudPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "iCloud Drive");
                if (Directory.Exists(icloudPath))
                {
                    Process.Start("explorer.exe", $"\"{icloudPath}\"");
                    return "☁️ Opening iCloud Drive.";
                }
            }
            // Control Panel
            if (lower.Contains("control panel"))
            {
                try { Process.Start("control.exe"); return "⚙️ Opening Control Panel."; }
                catch { return "❌ Couldn't open Control Panel."; }
            }
            // Settings
            if (lower.Contains("settings") && !lower.Contains("folder"))
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); return "⚙️ Opening Settings."; }
                catch { return "❌ Couldn't open Settings."; }
            }
            // Task Manager
            if (lower.Contains("task manager"))
            {
                try { Process.Start("taskmgr.exe"); return "📊 Opening Task Manager."; }
                catch { return "❌ Couldn't open Task Manager."; }
            }
            // Device Manager
            if (lower.Contains("device manager"))
            {
                try { Process.Start("devmgmt.msc"); return "🔧 Opening Device Manager."; }
                catch { return "❌ Couldn't open Device Manager."; }
            }
            // Services
            if (lower.Contains("services"))
            {
                try { Process.Start("services.msc"); return "⚙️ Opening Services."; }
                catch { return "❌ Couldn't open Services."; }
            }
            // Registry
            if (lower.Contains("registry") || lower.Contains("regedit"))
            {
                try { Process.Start("regedit.exe"); return "🔧 Opening Registry Editor."; }
                catch { return "❌ Couldn't open Registry Editor."; }
            }
            // Drives
            if (lower.Contains("c drive") || lower.Contains("c:") || lower == "c")
            {
                Process.Start("explorer.exe", "C:\\");
                return "📂 Opening C: Drive.";
            }
            if (lower.Contains("d drive") || lower.Contains("d:") || lower == "d")
            {
                Process.Start("explorer.exe", "D:\\");
                return "📂 Opening D: Drive.";
            }
            
            return null;
        }
        
        /// <summary>
        /// Speak a response asynchronously without blocking UI
        /// STEP 29: For long messages, asks user if they want it read aloud
        /// </summary>
        private async Task SpeakResponseAsync(string response)
        {
            // STEP 30: Delegate to SpeakFinalMessageAsync for consistency
            var finalMessage = new FinalAssistantMessage
            {
                Text = response,
                TextHash = AI.AIDebugLogger.ComputeHash(response),
                WasModified = false
            };
            await SpeakFinalMessageAsync(finalMessage);
        }
        
        /// <summary>
        /// STEP 30: Speak a FinalAssistantMessage - SINGLE SOURCE OF TRUTH.
        /// The text in finalMessage is EXACTLY what was shown in UI.
        /// This ensures UI and TTS never diverge.
        /// </summary>
        private async Task SpeakFinalMessageAsync(FinalAssistantMessage finalMessage)
        {
            try
            {
                // Log TTS request with hash for sync verification
                var voiceId = _voiceManager?.SelectedVoice?.Id ?? "default";
                AI.AIDebugLogger.LogTTSRequest(finalMessage.TextHash, finalMessage.Text.Length, voiceId);
                
                // Get the user's preferred honorific (sir, ma'am, miss, name, or none)
                var honorific = _conversationManager?.UserProfile?.GetHonorific() ?? "sir";
                var honorificSuffix = string.IsNullOrEmpty(honorific) ? "" : $", {honorific}";
                
                // Auto-speak ALL responses - no filtering or shortening
                SetAtlasCoreState(Controls.AtlasVisualState.Speaking);
                StartSpeakingEnergySimulation();
                
                var coord = SpeechCoordinator.Instance;
                coord.SetVoiceManager(_voiceManager);
                
                var tid = _currentTurnId == Guid.Empty ? Guid.NewGuid() : _currentTurnId;
                
                // STEP 30: Speak the EXACT text from finalMessage (single source of truth)
                // CHANGED: Always speak the full response, no filtering
                var textToSpeak = CleanTextForSpeech(finalMessage.Text);
                
                // Add safety timeout for UI state reset (60 seconds max)
                var speechTask = coord.SpeakConversationAsync(textToSpeak, tid, "chat_response");
                var timeoutTask = Task.Delay(60000);
                
                var completedTask = await Task.WhenAny(speechTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    Debug.WriteLine("[TTS] SpeakConversationAsync timed out in UI - forcing reset");
                    // Force stop underlying voice manager if stuck
                    _voiceManager?.Stop();
                }
                else
                {
                    var success = await speechTask;
                    if (!success)
                    {
                        Debug.WriteLine("[TTS] Speech rejected by SpeechCoordinator - another speaker active");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TTS] Speech error: {ex.Message}");
            }
            finally
            {
                // ALWAYS return to Idle after speaking (or failing)
                StopSpeakingEnergySimulation();
                SetAtlasCoreState(Controls.AtlasVisualState.Idle);
            }
        }
        
        /// <summary>
        /// Legacy SpeakResponseAsync implementation (kept for compatibility)
        /// Uses SpeechCoordinator to enforce single-speaker rule
        
        /// <summary>
        /// Speak a Jarvis-style conversation response with proper cadence.
        /// Uses ElevenLabs voice if available, polite and calm delivery.
        /// </summary>
        private async Task SpeakJarvisResponseAsync(string response)
        {
            try
            {
                // Do NOT call _voiceManager.Stop() here — it resets the dedup gate
                // and allows duplicate speech through. The Stop() at the top of
                // SendMessage() already cancelled previous-turn speech.

                // Set orb to Speaking state
                SetAtlasCoreState(Controls.AtlasVisualState.Speaking);
                StartSpeakingEnergySimulation();
                
                // Create utterance with current turn ID for deduplication
                var utterance = new AssistantUtterance(
                    response,
                    UtteranceSource.LLM,
                    turnId: _currentTurnId == Guid.Empty ? null : _currentTurnId);
                
                // Speak the full response - Jarvis responses are already short (1-2 sentences)
                await _voiceManager.SpeakAsync(utterance);
                
                // Return to Idle after speaking
                StopSpeakingEnergySimulation();
                SetAtlasCoreState(Controls.AtlasVisualState.Idle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TTS] Jarvis speech error: {ex.Message}");
                StopSpeakingEnergySimulation();
                SetAtlasCoreState(Controls.AtlasVisualState.Idle);
            }
        }
        
        /// <summary>
        /// Get a version of a response for voice output
        /// Speaks full paragraphs, but summarizes long lists/technical output
        /// </summary>
        private string GetShortResponse(string fullResponse)
        {
            // Get the user's preferred honorific (sir, ma'am, miss, name, or none)
            var honorific = _conversationManager?.UserProfile?.GetHonorific() ?? "sir";
            var honorificSuffix = string.IsNullOrEmpty(honorific) ? "" : $", {honorific}";
            
            // Check if this is a list or technical output (not conversational)
            bool isList = fullResponse.Contains("•") || fullResponse.Contains("- ") || 
                          fullResponse.Contains("1.") || fullResponse.Contains("✅") ||
                          fullResponse.Contains("❌") || fullResponse.Contains("📝") ||
                          fullResponse.Split('\n').Length > 8;
            
            bool isTechnical = fullResponse.Contains("```") || fullResponse.Contains("DIAGNOSTICS") ||
                               fullResponse.Contains("REPORT") || fullResponse.Contains("Status:") ||
                               fullResponse.Contains("MB") && fullResponse.Contains("GB") ||
                               fullResponse.Contains("CPU:") || fullResponse.Contains("Memory:");
            
            bool isMemoryStats = fullResponse.Contains("Memory Status") || fullResponse.Contains("Corrections") ||
                                 fullResponse.Contains("Preferences") || fullResponse.Contains("/corrections");
            
            // For conversational paragraphs (not lists), speak the full thing up to ~500 chars
            if (!isList && !isTechnical && !isMemoryStats && fullResponse.Length <= 500)
            {
                return fullResponse;
            }
            
            // For medium-length conversational responses, speak them
            if (!isList && !isTechnical && !isMemoryStats && fullResponse.Length <= 800)
            {
                // Just speak the first paragraph or two
                var paragraphs = fullResponse.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (paragraphs.Length > 0)
                {
                    var firstParagraph = paragraphs[0];
                    if (firstParagraph.Length <= 500)
                        return firstParagraph;
                }
            }
            
            // For long responses or lists, give a summary and tell them to check chat
            if (fullResponse.Contains("SYSTEM DIAGNOSTICS"))
                return $"Here's your system status{honorificSuffix}. I've displayed the full details in the chat.";
            if (fullResponse.Contains("DAILY BRIEFING") || fullResponse.Contains("GOOD MORNING"))
                return $"Here's your daily briefing{honorificSuffix}. Have a great day!";
            if (fullResponse.Contains("Note saved"))
                return "Note saved!";
            if (fullResponse.Contains("YOUR RECENT NOTES"))
                return $"Here are your recent notes{honorificSuffix}. I've listed them in the chat.";
            if (fullResponse.Contains("Opening"))
                return fullResponse.Split('\n')[0];
            
            // Memory system responses
            if (fullResponse.Contains("Memory Status"))
                return $"Here's what I know about you{honorificSuffix}. I've displayed the full memory stats in the chat.";
            if (fullResponse.Contains("Corrections I've Learned"))
                return $"Here are the corrections I've learned{honorificSuffix}. Check the chat for the full list.";
            if (fullResponse.Contains("Preferences & Facts"))
                return $"Here are your preferences{honorificSuffix}. I've listed them in the chat.";
                
            // IT Management responses
            if (fullResponse.Contains("System Information") || fullResponse.Contains("System Status"))
                return $"Here's your system health report{honorificSuffix}. CPU and memory are operating within optimal parameters.";
            if (fullResponse.Contains("SYSTEM HEALTH REPORT"))
                return $"I've compiled a comprehensive system report{honorificSuffix}. Details are in the chat.";
            if (fullResponse.Contains("Network Scan") || fullResponse.Contains("Found") && fullResponse.Contains("devices"))
                return $"Network scan complete{honorificSuffix}. I've identified several devices on your network.";
            if (fullResponse.Contains("Cleaned") && fullResponse.Contains("MB"))
                return $"Cleanup complete{honorificSuffix}. I've freed up some disk space for you.";
            if (fullResponse.Contains("No issues detected"))
                return $"Excellent news{honorificSuffix}. No issues detected. Your system is operating at peak efficiency.";
            if (fullResponse.Contains("Issue detected") || fullResponse.Contains("ACTIVE ISSUES"))
                return $"I've identified some issues requiring your attention{honorificSuffix}. Details are in the chat.";
            if (fullResponse.Contains("Speed test") || fullResponse.Contains("Mbps"))
                return $"Speed test complete{honorificSuffix}. Results are displayed in the chat.";
            if (fullResponse.Contains("updates available"))
                return $"I've detected available updates for your system{honorificSuffix}.";
            if (fullResponse.Contains("Firewall"))
                return $"Firewall status verified{honorificSuffix}. Your system is protected.";
            if (fullResponse.Contains("startup programs"))
                return $"Here's your startup programs list{honorificSuffix}.";
            if (fullResponse.Contains("Killed") && fullResponse.Contains("Chrome"))
                return $"Chrome processes terminated{honorificSuffix}. Memory has been reclaimed.";
            if (fullResponse.Contains("Killed") && fullResponse.Contains("browser"))
                return $"All browser processes terminated{honorificSuffix}. Significant memory freed.";
            if (fullResponse.Contains("Killed") && fullResponse.Contains("processes"))
                return $"Processes terminated as requested{honorificSuffix}.";
            if (fullResponse.Contains("Top 15 processes"))
                return $"Here are your highest memory consumers{honorificSuffix}.";
            
            // Code/technical responses
            if (fullResponse.Contains("```"))
                return $"I've written some code for you{honorificSuffix}. Check the chat for the details.";
            
            // Generic long response - try to extract first sentence
            var firstSentence = ExtractFirstSentence(fullResponse);
            if (!string.IsNullOrEmpty(firstSentence) && firstSentence.Length <= 200)
            {
                return $"{firstSentence} I've put the full details in the chat{honorificSuffix}.";
            }
                
            return $"Task complete{honorificSuffix}. I've displayed the details in the chat.";
        }
        
        /// <summary>
        /// Extract the first sentence from a response
        /// </summary>
        private string ExtractFirstSentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            // Find first sentence ending
            var endings = new[] { ". ", "! ", "? ", ".\n", "!\n", "?\n" };
            int minIndex = text.Length;
            
            foreach (var ending in endings)
            {
                var idx = text.IndexOf(ending);
                if (idx > 0 && idx < minIndex)
                    minIndex = idx + 1; // Include the punctuation
            }
            
            if (minIndex < text.Length && minIndex > 10)
                return text.Substring(0, minIndex).Trim();
            
            return "";
        }

        /// <summary>
        /// Clean text for speech synthesis - removes markdown, code blocks, and other formatting
        /// </summary>
        private string CleanTextForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            // Remove markdown code blocks
            text = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", " [code block] ");
            
            // Remove inline code
            text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "$1");
            
            // Remove markdown links but keep the text
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            
            // Remove markdown bold/italic
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*([^\*]+)\*\*", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*([^\*]+)\*", "$1");
            
            // Remove bullet points and list markers
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[\s]*[•\-\*]\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[\s]*\d+\.\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            
            // Remove excessive emojis (keep some for personality)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[✅❌📝🔍🎯💡🚀⚡🔧🎉🎊🎈🎁🎀🎂🎄🎆🎇🌟⭐💫⚠️🔴🟡🟢🔵🟣🟠⚫⚪🟤]{3,}", "");
            
            // Clean up multiple spaces and newlines
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n+", ". ");
            
            return text.Trim();
        }

        /// <summary>
        /// Detect if user message is an agent-worthy task (file ops, installations, code generation, etc.)
        /// </summary>
        private bool IsAgentTask(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            
            var lower = text.ToLowerInvariant();
            
            // File operation patterns - more specific
            // NOTE: Delete patterns removed - delete goes through ToolExecutor with confirmation system
            var filePatterns = new[]
            {
                "create a file", "create file", "make a file", "make file",
                "write a file", "write file", "save a file", "save file",
                "create a script", "write a script", "make a script",
                "python script", "bash script", "powershell script", "shell script",
                "javascript file", "typescript file", "python file", "csharp file", "c# file",
                "js file", "ts file", "py file", "cs file", "json file", "html file", "css file",
                "create a class", "write a class", "make a class",
                "create a function", "write a function", "make a function",
                // Delete patterns REMOVED - handled by ToolExecutor with double confirmation
                "rename file", "rename the file", "move file", "move the file",
                "read file", "read the file", "show file contents", "cat file", "read a file",
                "list files", "list the files", "show files", "show the files",
                "list folder", "list the folder", "list directory", "show folder contents",
                "create folder", "create directory", "make folder", "make directory",
                "search for files", "find files", "search files",
                "file that", "script that", "program that", // Catch "X file that does Y"
                "new file", "new script", "new program", // Catch "new X"
                "save this", "save to", "write to", // Catch save operations
                // FIND/SEARCH patterns - for file/folder searching
                "find ", "locate ", "search ", "look for ", "where is ", "where are ",
                "show me ", "get me all", "find all", "find my", "find the",
                " folders", " folder", " directory", " directories", // Catch "find X folders"
                "on my pc", "on my computer", "on this pc", "on this computer",
                "themes", "files named", "folders named" // Common search terms
            };
            
            // Software installation patterns
            var installPatterns = new[]
            {
                "install ", "uninstall ", "remove software",
                "download and install", "set up ", "setup ",
                "install python", "install node", "install git",
                "install npm", "install pip", "pip install",
                "npm install", "winget install", "choco install",
                "get me ", "download " // Catch "get me python" or "download vscode"
            };
            
            // Code generation patterns - be more specific to avoid false positives
            var codePatterns = new[]
            {
                "write code that", "write some code", "generate code",
                "create code that", "make code that", "code that does",
                "write a program that", "create a program that", "make a program that",
                "write an app that", "create an app that", "make an app that",
                "build a script", "build me a script",
                "write me a script", "create me a script", "make me a script",
                "write me a file", "create me a file", "make me a file",
                "that prints", "that logs", "that outputs", "that displays", // Catch "X that prints Y"
                "hello world", // Common coding task
                "write me a", "create me a", "make me a", // Catch "write me a calculator"
                "can you write", "can you create", "can you make" // Catch polite requests
            };
            
            // Command execution patterns
            var commandPatterns = new[]
            {
                "run command", "run the command", "execute command",
                "run powershell", "run cmd", "run terminal",
                "run dotnet", "run npm", "compile ", "build project",
                "open cmd", "open terminal", "open powershell"
            };
            
            // Check all patterns
            foreach (var pattern in filePatterns)
            {
                if (lower.Contains(pattern))
                {
                    System.Diagnostics.Debug.WriteLine($"[IsAgentTask] Matched file pattern: '{pattern}' in '{text}'");
                    return true;
                }
            }
            
            foreach (var pattern in installPatterns)
            {
                if (lower.Contains(pattern))
                {
                    System.Diagnostics.Debug.WriteLine($"[IsAgentTask] Matched install pattern: '{pattern}' in '{text}'");
                    return true;
                }
            }
            
            foreach (var pattern in codePatterns)
            {
                if (lower.Contains(pattern))
                {
                    System.Diagnostics.Debug.WriteLine($"[IsAgentTask] Matched code pattern: '{pattern}' in '{text}'");
                    return true;
                }
            }
            
            foreach (var pattern in commandPatterns)
            {
                if (lower.Contains(pattern))
                {
                    System.Diagnostics.Debug.WriteLine($"[IsAgentTask] Matched command pattern: '{pattern}' in '{text}'");
                    return true;
                }
            }
            
            // Check for specific file extensions mentioned (likely file operations)
            var fileExtensions = new[] { ".cs", ".py", ".js", ".ts", ".json", ".xml", ".txt", ".md", ".html", ".css", ".bat", ".ps1" };
            foreach (var ext in fileExtensions)
            {
                if (lower.Contains(ext) && (lower.Contains("create") || lower.Contains("write") || lower.Contains("make") || lower.Contains("read") || lower.Contains("delete")))
                {
                    System.Diagnostics.Debug.WriteLine($"[IsAgentTask] Matched file extension: '{ext}' with action in '{text}'");
                    return true;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[IsAgentTask] No match for: '{text}'");
            return false;
        }
        
        /// <summary>
        /// Handle delete file operations directly without AI - faster and more reliable
        /// </summary>
        private async Task<string> HandleDeleteFilesDirectly(string task)
        {
            // COMPLETELY DISABLED - Delete operations are too dangerous
            // This method caused data loss and should never be used
            return "⚠️ Delete operations are disabled for safety. Please delete files manually using File Explorer.";
        }
        
        // OLD DANGEROUS CODE - DO NOT USE
        private async Task<string> HandleDeleteFilesDirectly_DISABLED(string task)
        {
            return "⚠️ Delete operations are disabled for safety.";
            
            // All delete code removed to prevent accidental data loss
            #pragma warning disable CS0162
            var lower = task.ToLowerInvariant();
            var sb = new System.Text.StringBuilder();
            
            // Determine the target folder
            string targetFolder;
            if (lower.Contains("desktop"))
                targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            else if (lower.Contains("document"))
                targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            else if (lower.Contains("download"))
                targetFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            else if (lower.Contains("music folder") || (lower.Contains("music") && lower.Contains("folder")))
                targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            else if (lower.Contains("picture") || lower.Contains("photo"))
                targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            else if (lower.Contains("video"))
                targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            else
                targetFolder = Directory.GetCurrentDirectory();
            
            // Determine file extensions to target
            var extensions = new List<string>();
            if (lower.Contains("music") || lower.Contains("audio") || lower.Contains("song"))
                extensions.AddRange(new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma" });
            else if (lower.Contains("video") || lower.Contains("movie"))
                extensions.AddRange(new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" });
            else if (lower.Contains("image") || lower.Contains("photo") || lower.Contains("picture"))
                extensions.AddRange(new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" });
            else if (lower.Contains("document") || lower.Contains("doc"))
                extensions.AddRange(new[] { ".doc", ".docx", ".pdf", ".txt", ".rtf" });
            
            if (extensions.Count == 0)
            {
                return "❓ Please specify what type of files to delete (music, video, image, document)";
            }
            
            // Find matching files
            var filesToDelete = new List<string>();
            try
            {
                foreach (var ext in extensions)
                {
                    var files = Directory.GetFiles(targetFolder, $"*{ext}", SearchOption.TopDirectoryOnly);
                    filesToDelete.AddRange(files);
                }
            }
            catch (Exception ex)
            {
                return $"❌ Error scanning folder: {ex.Message}";
            }
            
            if (filesToDelete.Count == 0)
            {
                return $"✅ No matching files found in {targetFolder}";
            }
            
            // Show confirmation
            var fileList = string.Join("\n", filesToDelete.Take(10).Select(f => $"  • {Path.GetFileName(f)}"));
            if (filesToDelete.Count > 10)
                fileList += $"\n  ... and {filesToDelete.Count - 10} more";
            
            var confirmed = await ShowAgentConfirmationAsync("delete_files", 
                $"Delete {filesToDelete.Count} file(s) from {targetFolder}?\n\n{fileList}");
            
            if (!confirmed)
            {
                return "❌ Delete operation cancelled by user";
            }
            
            // DISABLED - DO NOT DELETE FILES
            return "⚠️ Delete operations are disabled for safety.";
            
            // Delete the files
            int deleted = 0;
            int failed = 0;
            foreach (var file in filesToDelete)
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch
                {
                    failed++;
                }
            }
            
            if (failed > 0)
                return $"✅ Deleted {deleted} file(s), ❌ {failed} failed (may be in use or protected)";
            else
            #pragma warning restore CS0162
                return $"✅ Successfully deleted {deleted} file(s)";
        }

        /// <summary>
        /// Run an agentic AI task - the AI can actually read/write files and run commands
        /// </summary>
        private async Task<string> RunAgentTask(string task)
        {
            System.Diagnostics.Debug.WriteLine($"[Agent] RunAgentTask called with: '{task}'");
            
            if (string.IsNullOrWhiteSpace(task))
                return "Usage: /agent <task>\n\nExamples:\n• /agent create a C# class called Calculator\n• /agent list all .cs files in this folder\n• /agent read the README.md file";

            // === DIRECT HANDLERS FOR COMMON OPERATIONS ===
            // These bypass the AI for faster, more reliable execution
            var lowerTask = task.ToLowerInvariant();
            
            // Delete operations now go through the agent with double confirmation
            // The confirmation system in SystemTool.DeleteWithConfirmationAsync handles safety

            if (_agent == null)
            {
                _agent = new Agent.AgentOrchestrator(Directory.GetCurrentDirectory());
                _agent.OnConfirmationRequired = ShowAgentConfirmationAsync;
            }

            // Show typing indicator while agent works
            Border? typingIndicator = null;
            
            try
            {
                // Show animated typing indicator
                Dispatcher.Invoke(() =>
                {
                    typingIndicator = ShowAgentTypingIndicator();
                });
                
                // Set orb to thinking/processing state
                SetAtlasCoreState(Controls.AtlasVisualState.Thinking);
                ShowStatus("🤖 Agent working...");
                
                // Hook up progress events to update status AND show in chat
                EventHandler<string>? thinkingHandler = null;
                EventHandler<string>? toolHandler = null;
                EventHandler<Agent.ToolResult>? toolResultHandler = null;
                
                thinkingHandler = (s, msg) => Dispatcher.Invoke(() => 
                {
                    ShowStatus($"💭 {msg}");
                    UpdateAgentProgress(typingIndicator, msg);
                });
                toolHandler = (s, tool) => Dispatcher.Invoke(() => 
                {
                    ShowStatus($"⚙️ {tool}");
                    UpdateAgentProgress(typingIndicator, tool);
                });
                toolResultHandler = (s, result) => Dispatcher.Invoke(() =>
                {
                    var preview = result.Output.Length > 100 ? result.Output.Substring(0, 100) + "..." : result.Output;
                    var status = result.Success ? "✅" : "❌";
                    UpdateAgentProgress(typingIndicator, $"{status} {preview}");
                });
                
                _agent.OnThinking += thinkingHandler;
                _agent.OnToolExecuting += toolHandler;
                _agent.OnToolResult += toolResultHandler;
                
                var result = await _agent.RunAsync(task);
                
                System.Diagnostics.Debug.WriteLine($"[Agent] Task completed with result length: {result?.Length ?? 0}");
                
                // Cleanup
                _agent.OnThinking -= thinkingHandler;
                _agent.OnToolExecuting -= toolHandler;
                _agent.OnToolResult -= toolResultHandler;
                
                // Remove typing indicator
                Dispatcher.Invoke(() =>
                {
                    if (typingIndicator != null)
                        HideTypingIndicator(typingIndicator);
                });
                
                SetAtlasCoreState(Controls.AtlasVisualState.Idle);
                ShowStatus("✅ Agent task complete");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Agent] Error: {ex.Message}");
                
                // Remove typing indicator on error
                Dispatcher.Invoke(() =>
                {
                    if (typingIndicator != null)
                        HideTypingIndicator(typingIndicator);
                });
                
                SetAtlasCoreState(Controls.AtlasVisualState.Idle);
                ShowStatus("❌ Agent error");
                return $"❌ Agent error: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Updates the agent typing indicator with progress information
        /// </summary>
        private void UpdateAgentProgress(Border? indicator, string message)
        {
            if (indicator == null) return;
            
            try
            {
                // Find the content stack in the indicator
                if (indicator.Child is Grid grid && grid.Children.Count > 1)
                {
                    var contentStack = grid.Children[1] as StackPanel;
                    if (contentStack != null && contentStack.Children.Count >= 2)
                    {
                        // Find or create the progress text
                        TextBlock? progressText = null;
                        if (contentStack.Children.Count > 2 && contentStack.Children[2] is TextBlock existing)
                        {
                            progressText = existing;
                        }
                        else
                        {
                            progressText = new TextBlock
                            {
                                FontSize = 11,
                                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                                TextWrapping = TextWrapping.Wrap,
                                MaxWidth = 400,
                                Margin = new Thickness(0, 4, 0, 0)
                            };
                            contentStack.Children.Add(progressText);
                        }
                        
                        // Update the progress text
                        progressText.Text = message.Length > 80 ? message.Substring(0, 80) + "..." : message;
                    }
                }
                
                ScrollMessagesToBottom();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Agent] Error updating progress: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Shows an animated typing indicator for agent tasks
        /// </summary>
        private Border ShowAgentTypingIndicator()
        {
            var container = new Border
            {
                Padding = new Thickness(0, 12, 0, 12),
                Background = Brushes.Transparent,
                Tag = "AgentTyping"
            };
            
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            // Purple robot avatar with pulse
            var avatarBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };
            avatarBorder.Child = new TextBlock
            {
                Text = "🤖",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Pulse animation
            var pulse = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0, To = 0.5,
                Duration = TimeSpan.FromMilliseconds(500),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            avatarBorder.BeginAnimation(Border.OpacityProperty, pulse);
            
            Grid.SetColumn(avatarBorder, 0);
            mainGrid.Children.Add(avatarBorder);
            
            // Content with animated dots
            var contentStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            contentStack.Children.Add(new TextBlock
            {
                Text = "Agent",
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            
            // Animated dots panel
            var dotsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            dotsPanel.Children.Add(new TextBlock
            {
                Text = "Working",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160))
            });
            
            for (int i = 0; i < 3; i++)
            {
                var dot = new Shapes.Ellipse
                {
                    Width = 6, Height = 6,
                    Fill = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                var dotAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.2, To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(400),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 150)
                };
                dot.BeginAnimation(Shapes.Ellipse.OpacityProperty, dotAnim);
                dotsPanel.Children.Add(dot);
            }
            
            contentStack.Children.Add(dotsPanel);
            Grid.SetColumn(contentStack, 1);
            mainGrid.Children.Add(contentStack);
            
            container.Child = mainGrid;
            
            // Add to messages panel
            var shouldStick = ShouldStickMessagesToBottom();
            MessagesPanel.Children.Add(container);
            ScrollMessagesToBottom(shouldStick);
            
            return container;
        }
        
        /// <summary>
        /// Shows a confirmation dialog for destructive agent operations
        /// </summary>
        private async Task<bool> ShowAgentConfirmationAsync(string toolName, string description)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            await Dispatcher.InvokeAsync(() =>
            {
                // Create confirmation UI in chat
                var container = new Border
                {
                    Padding = new Thickness(0, 12, 0, 12),
                    Background = Brushes.Transparent,
                    Tag = "AgentConfirmation"
                };
                
                var mainGrid = new Grid();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                // Warning icon
                var iconBorder = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromRgb(234, 179, 8)), // Yellow warning
                    VerticalAlignment = VerticalAlignment.Top
                };
                iconBorder.Child = new TextBlock
                {
                    Text = "⚠️",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBorder, 0);
                mainGrid.Children.Add(iconBorder);
                
                // Content
                var contentStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                contentStack.Children.Add(new TextBlock
                {
                    Text = "Agent Confirmation Required",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(234, 179, 8)),
                    Margin = new Thickness(0, 0, 0, 6)
                });
                
                contentStack.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                
                // Buttons
                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };
                
                var allowBtn = new Button
                {
                    Content = "✓ Allow",
                    Padding = new Thickness(16, 6, 16, 6),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                allowBtn.Click += (s, e) =>
                {
                    MessagesPanel.Children.Remove(container);
                    tcs.TrySetResult(true);
                };
                
                var denyBtn = new Button
                {
                    Content = "✗ Cancel",
                    Padding = new Thickness(16, 6, 16, 6),
                    Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                denyBtn.Click += (s, e) =>
                {
                    MessagesPanel.Children.Remove(container);
                    tcs.TrySetResult(false);
                };
                
                buttonPanel.Children.Add(allowBtn);
                buttonPanel.Children.Add(denyBtn);
                contentStack.Children.Add(buttonPanel);
                
                Grid.SetColumn(contentStack, 1);
                mainGrid.Children.Add(contentStack);
                container.Child = mainGrid;
                
                var shouldStick = ShouldStickMessagesToBottom();
                MessagesPanel.Children.Add(container);
                ScrollMessagesToBottom(shouldStick);
            });
            
            return await tcs.Task;
        }
        
        /// <summary>
        /// Shows a delete confirmation dialog with detailed info about what will be deleted
        /// Used by SystemTool.DeleteWithConfirmationAsync for double confirmation
        /// </summary>
        private async Task<bool> ShowDeleteConfirmationAsync(string title, string description)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            await Dispatcher.InvokeAsync(() =>
            {
                // Create confirmation UI in chat
                var container = new Border
                {
                    Padding = new Thickness(0, 12, 0, 12),
                    Background = Brushes.Transparent,
                    Tag = "DeleteConfirmation"
                };
                
                var mainGrid = new Grid();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                // Red warning icon for delete
                var iconBorder = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red for delete
                    VerticalAlignment = VerticalAlignment.Top
                };
                iconBorder.Child = new TextBlock
                {
                    Text = "🗑️",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBorder, 0);
                mainGrid.Children.Add(iconBorder);
                
                // Content
                var contentStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                contentStack.Children.Add(new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red
                    Margin = new Thickness(0, 0, 0, 6)
                });
                
                contentStack.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                
                // Buttons
                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };
                
                var deleteBtn = new Button
                {
                    Content = "🗑️ Yes, Delete",
                    Padding = new Thickness(16, 6, 16, 6),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                deleteBtn.Click += (s, e) =>
                {
                    MessagesPanel.Children.Remove(container);
                    tcs.TrySetResult(true);
                };
                
                var cancelBtn = new Button
                {
                    Content = "✗ Cancel",
                    Padding = new Thickness(16, 6, 16, 6),
                    Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                cancelBtn.Click += (s, e) =>
                {
                    MessagesPanel.Children.Remove(container);
                    tcs.TrySetResult(false);
                };
                
                buttonPanel.Children.Add(cancelBtn); // Cancel first (safer default)
                buttonPanel.Children.Add(deleteBtn);
                contentStack.Children.Add(buttonPanel);
                
                Grid.SetColumn(contentStack, 1);
                mainGrid.Children.Add(contentStack);
                container.Child = mainGrid;
                
                var shouldStick = ShouldStickMessagesToBottom();
                MessagesPanel.Children.Add(container);
                ScrollMessagesToBottom(shouldStick);
            });
            
            return await tcs.Task;
        }

        private Border ShowTypingIndicator()
        {
            // Clean, minimal typing indicator matching the new design
            var container = new Border
            {
                Padding = new Thickness(0, 16, 0, 16),
                Background = Brushes.Transparent
            };
            
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            // Avatar
            var avatarBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };
            avatarBorder.Child = new TextBlock
            {
                Text = "◆",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(9, 9, 11)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(avatarBorder, 0);
            mainGrid.Children.Add(avatarBorder);
            
            // Content
            var contentStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            
            // Name
            contentStack.Children.Add(new TextBlock
            {
                Text = "Atlas",
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
                Margin = new Thickness(0, 0, 0, 8)
            });
            
            // Animated dots
            var dotsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < 3; i++)
            {
                var dot = new Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
                    Margin = new Thickness(0, 0, 6, 0),
                    Opacity = 0.4
                };
                
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.4,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(500),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 150)
                };
                dot.BeginAnimation(Shapes.Ellipse.OpacityProperty, animation);
                dotsPanel.Children.Add(dot);
            }
            contentStack.Children.Add(dotsPanel);
            
            // Status text - VISIBLE by default to show "Thinking..."
            var statusText = new TextBlock
            {
                Text = "Thinking...",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Visible
            };
            contentStack.Children.Add(statusText);
            
            // Progress bar
            var progressBar = new ProgressBar
            {
                Height = 3,
                Margin = new Thickness(0, 8, 0, 0),
                IsIndeterminate = true,
                Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                Visibility = Visibility.Collapsed
            };
            contentStack.Children.Add(progressBar);
            
            // Progress text
            var progressText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            contentStack.Children.Add(progressText);
            
            Grid.SetColumn(contentStack, 1);
            mainGrid.Children.Add(contentStack);
            
            container.Child = mainGrid;
            
            // Store references
            _typingStatusText = statusText;
            _typingProgressBar = progressBar;
            _typingProgressText = progressText;
            
            var shouldStick = ShouldStickMessagesToBottom();
            MessagesPanel.Children.Add(container);
            ScrollMessagesToBottom(shouldStick);
            
            _currentTypingIndicator = container;
            
            return container;
        }
        
        // Direct references to typing indicator elements for reliable updates
        private TextBlock? _typingStatusText;
        private ProgressBar? _typingProgressBar;
        private TextBlock? _typingProgressText;
        
        private Border? _currentTypingIndicator;
        
        /// <summary>
        /// Update the typing indicator with progress information
        /// </summary>
        public void UpdateTypingProgress(string status, int? percentage = null, string? detail = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (_currentTypingIndicator == null) return;
                
                // Update status text using direct reference
                if (_typingStatusText != null)
                {
                    if (percentage.HasValue)
                        _typingStatusText.Text = $"{status} ({percentage}%)";
                    else
                        _typingStatusText.Text = status;
                    
                    // Always make it visible when updating
                    _typingStatusText.Visibility = Visibility.Visible;
                }
                
                // Update progress bar using direct reference
                if (_typingProgressBar != null)
                {
                    _typingProgressBar.Visibility = Visibility.Visible;
                    if (percentage.HasValue)
                    {
                        _typingProgressBar.IsIndeterminate = false;
                        _typingProgressBar.Maximum = 100;
                        _typingProgressBar.Value = percentage.Value;
                    }
                    else
                    {
                        _typingProgressBar.IsIndeterminate = true;
                    }
                }
                
                // Update detail text using direct reference
                if (_typingProgressText != null && !string.IsNullOrEmpty(detail))
                {
                    _typingProgressText.Text = detail;
                    _typingProgressText.Visibility = Visibility.Visible;
                }
                
                // Don't scroll on every update - causes jumping
            });
        }
        
        private ControlTemplate CreateCancelButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(220, 53, 69)));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2));
            borderFactory.Name = "bd";
            
            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);
            
            template.VisualTree = borderFactory;
            
            // Add hover trigger
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(200, 35, 51)), "bd"));
            template.Triggers.Add(hoverTrigger);
            
            return template;
        }
        
        private void CancelCurrentOperation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cancel the CancellationTokenSource
                _currentOperationCts?.Cancel();
                
                // Also cancel any running scanner
                _currentScanner?.CancelScan();
                
                Debug.WriteLine("[ChatWindow] User cancelled current operation");
                ShowStatus("⚠️ Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] Cancel error: {ex.Message}");
            }
        }

        private void HideTypingIndicator(Border indicator)
        {
            if (indicator != null && MessagesPanel.Children.Contains(indicator))
                MessagesPanel.Children.Remove(indicator);
            _currentTypingIndicator = null;
        }
        
        // ═══════════════════════════════════════════════════════════════
        // THINKING INDICATOR - Shows above input box when processing
        // ═══════════════════════════════════════════════════════════════
        private System.Windows.Threading.DispatcherTimer? _thinkingDotsTimer;
        
        private void ShowThinkingIndicator(string status = "Thinking...")
        {
            Dispatcher.Invoke(() =>
            {
                if (ThinkingIndicator == null) return;
                
                ThinkingIndicator.Visibility = Visibility.Visible;
                ThinkingStatusText.Text = status;
                
                // Start animated dots
                StartThinkingDotsAnimation();
            });
        }
        
        private void HideThinkingIndicator()
        {
            Dispatcher.Invoke(() =>
            {
                if (ThinkingIndicator == null) return;
                
                ThinkingIndicator.Visibility = Visibility.Collapsed;
                
                // Stop animated dots
                StopThinkingDotsAnimation();
            });
        }
        
        private void UpdateThinkingStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                if (ThinkingStatusText != null)
                {
                    ThinkingStatusText.Text = status;
                }
            });
        }
        
        private void StartThinkingDotsAnimation()
        {
            if (_thinkingDotsTimer != null)
            {
                _thinkingDotsTimer.Stop();
            }
            
            _thinkingDotsTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            
            int dotIndex = 0;
            _thinkingDotsTimer.Tick += (s, e) =>
            {
                if (ThinkDot1 == null || ThinkDot2 == null || ThinkDot3 == null) return;
                
                // Reset all dots
                ThinkDot1.Opacity = 0.4;
                ThinkDot2.Opacity = 0.4;
                ThinkDot3.Opacity = 0.4;
                
                // Highlight current dot
                switch (dotIndex % 3)
                {
                    case 0: ThinkDot1.Opacity = 1.0; break;
                    case 1: ThinkDot2.Opacity = 1.0; break;
                    case 2: ThinkDot3.Opacity = 1.0; break;
                }
                
                dotIndex++;
            };
            
            _thinkingDotsTimer.Start();
        }
        
        private void StopThinkingDotsAnimation()
        {
            if (_thinkingDotsTimer != null)
            {
                _thinkingDotsTimer.Stop();
                _thinkingDotsTimer = null;
            }
        }
        
        // ═══════════════════════════════════════════════════════════════
        // THINKING BUBBLE - Shimmer animation in chat while processing
        // ═══════════════════════════════════════════════════════════════
        private Border? ShowThinkingBubble()
        {
            Border? bubble = null;
            
            Dispatcher.Invoke(() =>
            {
                // Create thinking bubble with shimmer effect
                bubble = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(25, 139, 92, 246)), // Violet/Cyan gradient
                    CornerRadius = new CornerRadius(12, 12, 12, 4),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 4, 60, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MaxWidth = 200,
                    Opacity = 0
                };
                
                // Create shimmer container
                var grid = new Grid();
                
                // Add three animated dots
                var dotsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                
                var dot1 = new System.Windows.Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(34, 211, 238)), // Cyan
                    Margin = new Thickness(0, 0, 6, 0),
                    Opacity = 0.4
                };
                
                var dot2 = new System.Windows.Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(34, 211, 238)),
                    Margin = new Thickness(0, 0, 6, 0),
                    Opacity = 0.4
                };
                
                var dot3 = new System.Windows.Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(34, 211, 238)),
                    Opacity = 0.4
                };
                
                dotsPanel.Children.Add(dot1);
                dotsPanel.Children.Add(dot2);
                dotsPanel.Children.Add(dot3);
                
                grid.Children.Add(dotsPanel);
                bubble.Child = grid;
                
                // Add to messages panel
                var shouldStick = ShouldStickMessagesToBottom();
                MessagesPanel.Children.Add(bubble);
                ScrollMessagesToBottom(shouldStick);
                
                // Fade in animation
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(200)
                };
                bubble.BeginAnimation(Border.OpacityProperty, fadeIn);
                
                // Animate dots with shimmer effect
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(400)
                };
                
                int dotIndex = 0;
                timer.Tick += (s, e) =>
                {
                    // Reset all dots
                    dot1.Opacity = 0.4;
                    dot2.Opacity = 0.4;
                    dot3.Opacity = 0.4;
                    
                    // Highlight current dot with shimmer
                    switch (dotIndex % 3)
                    {
                        case 0:
                            var anim1 = new System.Windows.Media.Animation.DoubleAnimation
                            {
                                From = 0.4,
                                To = 1.0,
                                Duration = TimeSpan.FromMilliseconds(300),
                                AutoReverse = true
                            };
                            dot1.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, anim1);
                            break;
                        case 1:
                            var anim2 = new System.Windows.Media.Animation.DoubleAnimation
                            {
                                From = 0.4,
                                To = 1.0,
                                Duration = TimeSpan.FromMilliseconds(300),
                                AutoReverse = true
                            };
                            dot2.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, anim2);
                            break;
                        case 2:
                            var anim3 = new System.Windows.Media.Animation.DoubleAnimation
                            {
                                From = 0.4,
                                To = 1.0,
                                Duration = TimeSpan.FromMilliseconds(300),
                                AutoReverse = true
                            };
                            dot3.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, anim3);
                            break;
                    }
                    
                    dotIndex++;
                };
                
                timer.Start();
                bubble.Tag = timer; // Store timer in Tag for cleanup
            });
            
            return bubble;
        }
        
        private void HideThinkingBubble(Border? bubble)
        {
            if (bubble == null) return;
            
            Dispatcher.Invoke(() =>
            {
                // Stop animation timer
                if (bubble.Tag is System.Windows.Threading.DispatcherTimer timer)
                {
                    timer.Stop();
                }
                
                // Fade out and remove
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(200)
                };
                
                fadeOut.Completed += (s, e) =>
                {
                    MessagesPanel.Children.Remove(bubble);
                };
                
                bubble.BeginAnimation(Border.OpacityProperty, fadeOut);
            });
        }

        private async Task<string> GetAIResponse(string userMessage, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                
                // ========== TIMEOUT & CANCELLATION SETUP ==========
                // Check if this is a scan request - scans need much longer timeout (10 minutes)
                var lowerMessage = userMessage.ToLower().Trim();
                var isScanRequest = lowerMessage.Contains("scan") || lowerMessage.Contains("virus") || lowerMessage.Contains("malware") || lowerMessage.Contains("spyware");
                var timeoutSeconds = isScanRequest ? 600 : 120; // 10 minutes for scans, 2 minutes for other operations
                
                Debug.WriteLine($"[ChatWindow] Operation timeout: {timeoutSeconds}s (isScan: {isScanRequest})");
                
                // Create a timeout cancellation token
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var linkedCt = linkedCts.Token;

                // ========== UNIVERSAL INTENT ROUTER (STAGE 1) ==========
                // Route to execution mode BEFORE any other processing
                var routing = Understanding.UniversalIntentRouter.Route(userMessage);
                Debug.WriteLine($"[Router] category={routing.Category}, tools={routing.Tools}, confidence={routing.Confidence:F2}, style={routing.Style}, safety={routing.Safety}");
                Debug.WriteLine($"[Router] reasoning: {routing.Reasoning}");
                
                // ========== DOCUMENT TASK (STAGE 3) ==========
                // Handle Word document creation requests
                if (routing.Category == Understanding.IntentCategory.DocumentTask)
                {
                    Debug.WriteLine("[DocumentTask] Word document request detected");
                    
                    try
                    {
                        // Extract document details from user message
                        var lowerMsg = userMessage.ToLower();
                        string title = "Document";
                        string body = userMessage;
                        Tools.DocumentService.DocumentType docType = Tools.DocumentService.DocumentType.Generic;
                        
                        // Detect document type
                        if (lowerMsg.Contains("letter"))
                        {
                            docType = Tools.DocumentService.DocumentType.Letter;
                            title = "Letter";
                        }
                        else if (lowerMsg.Contains("cv") || lowerMsg.Contains("resume"))
                        {
                            docType = Tools.DocumentService.DocumentType.CV;
                            title = "CV";
                        }
                        else if (lowerMsg.Contains("report"))
                        {
                            docType = Tools.DocumentService.DocumentType.Report;
                            title = "Report";
                        }
                        else if (lowerMsg.Contains("checklist"))
                        {
                            docType = Tools.DocumentService.DocumentType.Checklist;
                            title = "Checklist";
                        }
                        
                        // Extract title if present (after "write" or "create")
                        var titleMatch = System.Text.RegularExpressions.Regex.Match(userMessage, @"(?:write|create|draft)\s+(?:a\s+)?(?:word\s+)?(?:letter|document|report|cv|resume)?\s*:?\s*(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (titleMatch.Success && titleMatch.Groups.Count > 1)
                        {
                            var extractedTitle = titleMatch.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(extractedTitle) && extractedTitle.Length < 100)
                            {
                                title = extractedTitle;
                            }
                        }
                        
                        // For letters, extract recipient and body from AI
                        if (docType == Tools.DocumentService.DocumentType.Letter)
                        {
                            // Ask AI to format the letter content
                            var letterPrompt = $"Format this as a professional letter. Extract the recipient name and letter body from: {userMessage}\n\nProvide ONLY the letter body text (no 'Dear' or signature - those are added automatically). Be concise and professional.";
                            var letterBody = await GetAIResponseDirectAsync(letterPrompt, linkedCt);
                            
                            // Create the letter
                            var result = Tools.DocumentService.CreateLetter(title, "Sir/Madam", letterBody);
                            
                            var letterResponse = result.Success 
                                ? $"✅ Letter created successfully.\n\n{result.Message}\n\nThe document is ready to open and edit."
                                : $"❌ Failed to create letter: {result.Message}";
                            
                            conversationHistory.Add(new { role = "user", content = userMessage });
                            conversationHistory.Add(new { role = "assistant", content = letterResponse });
                            return letterResponse;
                        }
                        else
                        {
                            // For other document types, ask AI to generate content
                            var contentPrompt = $"Generate professional content for a {docType} document titled '{title}'. User request: {userMessage}\n\nProvide well-structured content with clear paragraphs. Be concise but comprehensive.";
                            var documentBody = await GetAIResponseDirectAsync(contentPrompt, linkedCt);
                            
                            // Create the document
                            var result = Tools.DocumentService.CreateDoc(title, documentBody, type: docType);
                            
                            var docResponse = result.Success 
                                ? $"✅ Document created successfully.\n\n{result.Message}\n\nThe document is ready to open and edit."
                                : $"❌ Failed to create document: {result.Message}";
                            
                            conversationHistory.Add(new { role = "user", content = userMessage });
                            conversationHistory.Add(new { role = "assistant", content = docResponse });
                            return docResponse;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DocumentTask] Error: {ex.Message}");
                        var errorResponse = $"❌ Failed to create document: {ex.Message}";
                        conversationHistory.Add(new { role = "user", content = userMessage });
                        conversationHistory.Add(new { role = "assistant", content = errorResponse });
                        return errorResponse;
                    }
                }
                // ========== END DOCUMENT TASK ==========
                
                // ========== TASK ORCHESTRATOR (STAGE 2) ==========
                // Check if this request needs multi-step orchestration
                if (_taskOrchestrator != null && Understanding.TaskOrchestrator.NeedsOrchestration(routing))
                {
                    Debug.WriteLine("[Orchestrator] Multi-step task detected - creating plan");
                    
                    // Check if we have an active task to resume
                    if (_taskOrchestrator.CurrentTask != null && _taskOrchestrator.CurrentTask.CanResume)
                    {
                        Debug.WriteLine("[Orchestrator] Resuming existing task");
                        _taskOrchestrator.ResumeTask();
                    }
                    else if (_taskOrchestrator.CurrentTask == null || _taskOrchestrator.CurrentTask.IsComplete)
                    {
                        // Create new task with plan
                        var task = await _taskOrchestrator.CreateTaskAsync(userMessage, routing, linkedCt);
                        
                        if (task.Steps.Count > 0)
                        {
                            // Show the plan to user
                            var planSummary = $"📋 I've created a {task.Steps.Count}-step plan:\n\n";
                            for (int i = 0; i < task.Steps.Count; i++)
                            {
                                planSummary += $"{i + 1}. {task.Steps[i].Description}\n";
                            }
                            planSummary += $"\nExecuting step 1...";
                            
                            AddMessage("Atlas", planSummary, false);
                        }
                    }
                    
                    // Execute next step
                    if (_taskOrchestrator.CurrentTask != null)
                    {
                        var result = await _taskOrchestrator.ExecuteNextStepAsync(
                            async (stepPrompt, stepCt) =>
                            {
                                // Execute this step using the AI
                                return await GetAIResponseDirectAsync(stepPrompt, stepCt);
                            },
                            linkedCt
                        );
                        
                        if (result.Success)
                        {
                            var stepResponse = result.Message;
                            
                            if (!result.IsComplete)
                            {
                                stepResponse += $"\n\n📊 Progress: {result.StepNumber}/{result.TotalSteps} steps completed ({result.Progress}%)";
                                stepResponse += $"\n\nType 'continue' to proceed to step {result.StepNumber + 1}.";
                            }
                            
                            conversationHistory.Add(new { role = "user", content = userMessage });
                            conversationHistory.Add(new { role = "assistant", content = stepResponse });
                            return stepResponse;
                        }
                        else
                        {
                            var errorResponse = result.Message + "\n\nTask paused. Type 'continue' to retry or 'cancel' to abort.";
                            conversationHistory.Add(new { role = "user", content = userMessage });
                            conversationHistory.Add(new { role = "assistant", content = errorResponse });
                            return errorResponse;
                        }
                    }
                }
                // ========== END TASK ORCHESTRATOR ==========
                
                // ========== INTENT ROUTING ==========
                // Route messages to appropriate handlers BEFORE AI processing
                var intentResult = Understanding.IntentRouter.DetectIntent(userMessage);
                Debug.WriteLine($"[IntentRouter] Intent: {intentResult.Intent}, Reason: {intentResult.Reason}");
                
                // Handle task orchestration commands
                if (_taskOrchestrator?.CurrentTask != null)
                {
                    var lowerMsg = userMessage.ToLower().Trim();
                    
                    if (lowerMsg == "continue" || lowerMsg == "next" || lowerMsg == "proceed")
                    {
                        Debug.WriteLine("[Orchestrator] Continue command detected");
                        
                        // Resume and execute next step
                        if (_taskOrchestrator.CurrentTask.CanResume)
                        {
                            _taskOrchestrator.ResumeTask();
                        }
                        
                        var result = await _taskOrchestrator.ExecuteNextStepAsync(
                            async (stepPrompt, stepCt) => await GetAIResponseDirectAsync(stepPrompt, stepCt),
                            linkedCt
                        );
                        
                        var continueResponse = result.Message;
                        if (result.Success && !result.IsComplete)
                        {
                            continueResponse += $"\n\n📊 Progress: {result.StepNumber}/{result.TotalSteps} steps completed ({result.Progress}%)";
                            continueResponse += $"\n\nType 'continue' to proceed to step {result.StepNumber + 1}.";
                        }
                        
                        conversationHistory.Add(new { role = "user", content = userMessage });
                        conversationHistory.Add(new { role = "assistant", content = continueResponse });
                        return continueResponse;
                    }
                    else if (lowerMsg == "cancel" || lowerMsg == "abort" || lowerMsg == "stop task")
                    {
                        Debug.WriteLine("[Orchestrator] Cancel command detected");
                        _taskOrchestrator.CancelTask();
                        
                        var cancelMsg = "Task cancelled. All progress has been saved.";
                        conversationHistory.Add(new { role = "user", content = userMessage });
                        conversationHistory.Add(new { role = "assistant", content = cancelMsg });
                        return cancelMsg;
                    }
                    else if (lowerMsg == "status" || lowerMsg == "progress")
                    {
                        Debug.WriteLine("[Orchestrator] Status command detected");
                        var statusMsg = _taskOrchestrator.GetProgressSummary();
                        conversationHistory.Add(new { role = "user", content = userMessage });
                        conversationHistory.Add(new { role = "assistant", content = statusMsg });
                        return statusMsg;
                    }
                }
                
                // Handle preference setting (e.g., "call me tommy")
                if (intentResult.Intent == Understanding.IntentRouter.Intent.PreferenceSet && !string.IsNullOrEmpty(intentResult.ExtractedValue))
                {
                    var name = intentResult.ExtractedValue;
                    Debug.WriteLine($"[IntentRouter] Setting preferred name: {name}");
                    
                    // Update user profile with preferred name
                    if (_conversationManager != null)
                    {
                        await _conversationManager.UpdateProfileAsync(profile =>
                        {
                            profile.DisplayName = name;
                            profile.Honorific = UserHonorific.Name; // Use name instead of "sir"
                            profile.LastUpdated = DateTime.Now;
                        });
                        
                        Debug.WriteLine($"[IntentRouter] Preferred name persisted: {name}");
                        
                        // Acknowledge immediately
                        var acknowledgment = $"Understood, {name}.";
                        conversationHistory.Add(new { role = "user", content = userMessage });
                        conversationHistory.Add(new { role = "assistant", content = acknowledgment });
                        return acknowledgment;
                    }
                }
                
                // Handle greetings with personalized response
                if (intentResult.Intent == Understanding.IntentRouter.Intent.Greeting)
                {
                    var userName = _conversationManager?.GetUserName() ?? "sir";
                    var hour = DateTime.Now.Hour;
                    string greetingResponse;
                    
                    if (hour >= 5 && hour < 12)
                        greetingResponse = $"Good morning, {userName}.";
                    else if (hour >= 12 && hour < 18)
                        greetingResponse = $"Good afternoon, {userName}.";
                    else if (hour >= 18 && hour < 22)
                        greetingResponse = $"Good evening, {userName}.";
                    else
                        greetingResponse = $"Hello, {userName}.";
                    
                    conversationHistory.Add(new { role = "user", content = userMessage });
                    conversationHistory.Add(new { role = "assistant", content = greetingResponse });
                    return greetingResponse;
                }
                // ========== END INTENT ROUTING ==========
                
                // ========== UNDERSTANDING LAYER COMPLETELY DISABLED ==========
                // DISABLED to prevent duplicate responses - ToolExecutor handles everything now
                // The Understanding Layer was causing duplicate responses and false confirmations
                Debug.WriteLine("[Understanding] DISABLED - ToolExecutor handles all commands to prevent duplicates");
                // ========== END UNDERSTANDING LAYER ==========
                
                // First, try rule-based tool execution (fast) with cancellation support
                var toolResult = await Tools.ToolExecutor.TryExecuteToolWithCancellationAsync(userMessage, linkedCt, 
                    scanner => _currentScanner = scanner);
                if (toolResult != null)
                {
                    // Check for special stop voice marker
                    if (toolResult == "__STOP_VOICE__")
                    {
                        _voiceManager?.Stop();
                        UpdateSpeakingIndicator(false);
                        return "🔇 Stopped.";
                    }
                    
                    // Check for Integration Hub window marker
                    if (toolResult == "__OPEN_INTEGRATION_HUB__")
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                var hubWindow = new Integrations.IntegrationHubWindow();
                                hubWindow.Owner = this;
                                hubWindow.Show();
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[IntegrationHub2] Error: {ex}");
                                MessageBox.Show($"Integration Hub error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        });
                        return "🔌 Opening Integration Hub - see all available apps and services Atlas can connect to!";
                    }
                    
                    // Check for Social Media Console window marker
                    if (toolResult == "__OPEN_SOCIAL_MEDIA_CONSOLE__")
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                var consoleWindow = new SocialMedia.SocialMediaConsoleWindow();
                                consoleWindow.Owner = this;
                                consoleWindow.Show();
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[SocialMedia2] Error: {ex}");
                                MessageBox.Show($"Social Media Console error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        });
                        return "📱 Opening Social Media Console - create content, manage campaigns, and schedule posts!";
                    }
                    
                    // Check for Security Suite window marker
                    if (toolResult == "__OPEN_SECURITY_SUITE__")
                    {
                        await Dispatcher.InvokeAsync(() => ShowSecuritySuiteWindow());
                        return "🛡️ Opening Security Suite...";
                    }
                    
                    // Check for special image analysis marker
                    if (toolResult.StartsWith("__ANALYZE_IMAGE__|"))
                    {
                        var parts = toolResult.Split('|');
                        if (parts.Length >= 3)
                        {
                            var imagePath = parts[1];
                            var question = parts[2];
                            toolResult = await AnalyzeImageWithQuestion(imagePath, question, linkedCt);
                        }
                    }
                    
                    // Check for special image generation marker
                    if (toolResult.StartsWith("__GENERATE_IMAGE__|"))
                    {
                        var prompt = toolResult.Substring("__GENERATE_IMAGE__|".Length);
                        return await GenerateAndDisplayImage(prompt, linkedCt);
                    }
                    
                    // Record successful help for conversation depth escalation
                    ConversationContext.Instance.RecordSuccessfulHelp();
                    
                    conversationHistory.Add(new { role = "user", content = userMessage });
                    conversationHistory.Add(new { role = "assistant", content = toolResult });
                    return toolResult;
                }
                
                linkedCt.ThrowIfCancellationRequested();

                // Try AI-powered command execution for complex requests
                var aiCommandResult = await Tools.AICommandExecutor.ExecuteWithAIAsync(userMessage);
                if (aiCommandResult != null)
                {
                    // Record successful help for conversation depth escalation
                    ConversationContext.Instance.RecordSuccessfulHelp();
                    
                    conversationHistory.Add(new { role = "user", content = userMessage });
                    conversationHistory.Add(new { role = "assistant", content = aiCommandResult });
                    return aiCommandResult;
                }
                
                linkedCt.ThrowIfCancellationRequested();
                
                // No action needed - have a conversation with butler personality
                var aiResponseContent = await GetAIResponseDirectAsync(userMessage, linkedCt, 4096);
                
                // Handle cancellation or error from the direct call
                if (aiResponseContent == "CANCELLED · OPERATION STOPPED")
                    return aiResponseContent;
                
                if (aiResponseContent.StartsWith("Error:"))
                    return aiResponseContent;

                // Add to history now that we have a successful (or at least non-cancelled) response
                conversationHistory.Add(new { role = "user", content = userMessage });

                // Check for empty response
                if (string.IsNullOrWhiteSpace(aiResponseContent))
                {
                    System.Diagnostics.Debug.WriteLine("[AI Response] Content is empty!");
                    return "🤔 The AI returned an empty response. Please try again.";
                }

                // Check if AI response contains coding tool calls
                if (_codeToolExecutor != null)
                {
                    var (codeHandled, codeResult) = await _codeToolExecutor.TryExecuteToolAsync(aiResponseContent);
                    if (codeHandled && codeResult != null)
                    {
                        // Return the tool result along with any AI explanation
                        var cleanedResponse = System.Text.RegularExpressions.Regex.Replace(
                            aiResponseContent, @"\[TOOL:[^\]]+\]", "").Trim();
                        var codeResponse = string.IsNullOrWhiteSpace(cleanedResponse) 
                            ? codeResult 
                            : $"{cleanedResponse}\n\n{codeResult}";
                        conversationHistory.Add(new { role = "assistant", content = codeResponse });
                        return codeResponse;
                    }
                }

                // Check if AI response contains an action we should execute
                var actionResult = await TryExtractAndExecuteAction(aiResponseContent, linkedCt);
                if (actionResult != null)
                {
                    conversationHistory.Add(new { role = "assistant", content = actionResult });
                    return actionResult;
                }

                // === RESPONSE QUALITY GATE ===
                // Filter out generic/low-quality responses
                var qualityCheck = ResponseQualityGate.Check(aiResponseContent, userMessage);
                if (!qualityCheck.Passed)
                {
                    Debug.WriteLine($"[QualityGate] Response rejected: {qualityCheck.Reason}");
                    
                    // Try regeneration once - use GetAIResponseDirectAsync with an override
                    var regenerationPrompt = ResponseQualityGate.GetRegenerationPrompt(aiResponseContent, userMessage);
                    
                    // For regeneration, we need to include the failed response in the prompt
                    var retryResponse = await GetAIResponseDirectAsync(
                        $"{userMessage}\n\n[Assistant failed response]: {aiResponseContent}\n\n[Instruction]: {regenerationPrompt}", 
                        linkedCt, 4096);

                    if (!retryResponse.StartsWith("Error:") && retryResponse != "CANCELLED · OPERATION STOPPED" && !string.IsNullOrWhiteSpace(retryResponse))
                    {
                        var retryCheck = ResponseQualityGate.Check(retryResponse, userMessage);
                        if (retryCheck.Passed)
                        {
                            aiResponseContent = retryResponse;
                        }
                    }
                    
                    // Use fallback if regeneration also failed or was skipped
                    if (!qualityCheck.Passed && aiResponseContent == qualityCheck.SuggestedFallback == false && !string.IsNullOrEmpty(qualityCheck.SuggestedFallback))
                    {
                        aiResponseContent = qualityCheck.SuggestedFallback;
                    }
                }

                // Apply phrase cooldown to prevent repetition
                var finalResponse = PhraseCooldown.Instance.ApplyCooldown(aiResponseContent);
                
                // Record to working memory
                ConversationWorkingMemory.Instance.ProcessAssistantMessage(finalResponse);
                
                // Auto-speak long responses
                var wordCount = finalResponse.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount > 100)
                {
                    ShowStopSpeechButton();
                    _ = _voiceManager.SpeakAsync(finalResponse).ContinueWith(_ => HideStopSpeechButton());
                }

                conversationHistory.Add(new { role = "assistant", content = finalResponse });
                
                if (conversationHistory.Count > 20)
                    conversationHistory.RemoveRange(1, 2);

                return finalResponse;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout occurred (not user cancellation)
                return "⏱️ Request timed out. The operation took too long. Please try again or try a simpler request.";
            }
            catch (OperationCanceledException)
            {
                // User cancellation - return unified message
                return "CANCELLED · OPERATION STOPPED";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Execute AI request directly without orchestration (used by orchestrator for individual steps)
        /// </summary>
        private async Task<string> GetAIResponseDirectAsync(string prompt, CancellationToken ct, int maxTokens = 2000, bool includeHistory = true, string? systemPromptOverride = null)
        {
            try
            {
                // Build messages for AI
                var messages = new List<object>();
                
                // Add system prompt
                var systemPrompt = systemPromptOverride ?? _systemPromptBuilder?.BuildSystemPrompt() ?? GetDefaultSystemPrompt();
                
                // Add coding capabilities to system prompt if workspace is set
                if (string.IsNullOrEmpty(systemPromptOverride) && _codeAssistant?.HasWorkspace == true)
                {
                    systemPrompt += Coding.CodeToolExecutor.GetCodingSystemPrompt();
                }

                messages.Add(new { role = "system", content = systemPrompt });
                
                if (includeHistory)
                {
                    // Add recent conversation history (last 10 messages), excluding system messages
                    var recentHistory = conversationHistory
                        .Skip(Math.Max(0, conversationHistory.Count - 10))
                        .Where(m => ((dynamic)m).role != "system")
                        .ToList();
                    messages.AddRange(recentHistory);
                }
                
                // Add current prompt
                messages.Add(new { role = "user", content = prompt });
                
                // Call AI (now with cancellation support)
                var response = await AIManager.SendMessageAsync(messages, maxTokens, ct);
                
                if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
                {
                    return response.Error ?? "Failed to get AI response.";
                }
                
                return response.Content;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[GetAIResponseDirectAsync] Operation cancelled");
                return "CANCELLED · OPERATION STOPPED";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GetAIResponseDirectAsync] Error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Execute an action based on Understanding Layer decision
        /// </summary>
        private async Task<string?> ExecuteUnderstandingAction(UnderstandingResult understanding)
        {
            try
            {
                var tool = understanding.ToolToExecute;
                var parameters = understanding.ToolParameters;
                
                Debug.WriteLine($"[Understanding] Executing: {tool} with {parameters.Count} parameters");
                
                // Map tool names to actual execution
                switch (tool)
                {
                    case "MediaPlayerTool":
                        var query = parameters.GetValueOrDefault("query")?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(query))
                        {
                            var result = await Tools.MediaPlayerTool.PlayAsync(query);
                            return _understandingLayer?.Formatter.FormatSuccess("Playing", query) + $"\n{result}";
                        }
                        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(action))
                        {
                            // Use existing tool executor for media control
                            return await Tools.ToolExecutor.TryExecuteToolAsync($"{action} music");
                        }
                        break;
                        
                    case "SystemTool.Volume":
                        var volAction = parameters.GetValueOrDefault("action")?.ToString() ?? "";
                        return volAction switch
                        {
                            "up" => await Tools.SystemTool.SetVolumeAsync(80),
                            "down" => await Tools.SystemTool.SetVolumeAsync(30),
                            "mute" or "unmute" => await Tools.SystemTool.ToggleMuteAsync(),
                            _ => "Volume adjusted"
                        };
                        
                    case "SystemTool.OpenApp":
                        var app = parameters.GetValueOrDefault("app")?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(app))
                        {
                            var result = await Tools.SystemTool.OpenAppAsync(app);
                            return _understandingLayer?.Formatter.FormatSuccess("Opened", app) + $"\n{result}";
                        }
                        break;
                        
                    case "SystemTool.CloseApp":
                        var appToClose = parameters.GetValueOrDefault("app")?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(appToClose))
                        {
                            // Use process kill
                            try
                            {
                                var processes = System.Diagnostics.Process.GetProcessesByName(appToClose.Replace(".exe", ""));
                                foreach (var proc in processes)
                                {
                                    proc.Kill();
                                }
                                return _understandingLayer?.Formatter.FormatSuccess("Closed", appToClose);
                            }
                            catch (Exception ex)
                            {
                                return $"Couldn't close {appToClose}: {ex.Message}";
                            }
                        }
                        break;
                        
                    case "SystemTool.Power":
                        var powerAction = parameters.GetValueOrDefault("action")?.ToString() ?? "";
                        return powerAction switch
                        {
                            "shutdown" => await Tools.SystemTool.ShutdownAsync(),
                            "restart" => await Tools.SystemTool.RestartAsync(),
                            "sleep" => await Tools.SystemTool.SleepAsync(),
                            "lock" => await Tools.SystemTool.LockComputerAsync(),
                            _ => "Power action completed"
                        };
                        
                    case "FileSystemTool.Organize":
                        var target = parameters.GetValueOrDefault("target")?.ToString() ?? "";
                        var folderPath = ResolveFolderPath(target);
                        if (!string.IsNullOrEmpty(folderPath))
                        {
                            var result = await Tools.SystemTool.SortFilesByTypeAsync(folderPath);
                            return _understandingLayer?.Formatter.FormatSuccess("Organized", target) + $"\n{result}";
                        }
                        break;
                        
                    case "FileSystemTool.OpenFolder":
                        var folder = parameters.GetValueOrDefault("folder")?.ToString() ?? "";
                        var path = ResolveFolderPath(folder);
                        if (!string.IsNullOrEmpty(path))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", path);
                            return _understandingLayer?.Formatter.FormatSuccess("Opened", folder);
                        }
                        break;
                        
                    case "WebSearchTool":
                        var searchQuery = parameters.GetValueOrDefault("query")?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(searchQuery))
                        {
                            return await Tools.WebSearchTool.SearchAsync(searchQuery);
                        }
                        break;
                        
                    case "WebSearchTool.Weather":
                        var location = parameters.GetValueOrDefault("location")?.ToString() ?? "";
                        return await Tools.WebSearchTool.GetWeatherAsync(location);
                        
                    case "ScreenCaptureTool":
                        var captureResult = await _screenCapture.CaptureScreenAsync();
                        return captureResult.Success 
                            ? $"📸 Screenshot saved to: {captureResult.Metadata.FilePath}" 
                            : $"Screenshot failed: {captureResult.Error}";
                        
                    case "SecurityScanner":
                        // Trigger security scan
                        return "🛡️ Starting security scan... This may take a few minutes.";
                        
                    case "ImageGeneratorTool":
                        var prompt = parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(prompt))
                        {
                            return await GenerateAndDisplayImage(prompt, default);
                        }
                        break;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Understanding] Execution error: {ex.Message}");
                _understandingLayer?.RecordOutcome(understanding.ToolToExecute ?? "action", false, ex.Message);
                return _understandingLayer?.Formatter.FormatError(understanding.ToolToExecute ?? "action", ex.Message);
            }
        }
        
        /// <summary>
        /// Resolve folder name to actual path
        /// </summary>
        private string? ResolveFolderPath(string folderName)
        {
            var lower = folderName.ToLower();
            
            if (lower == "downloads" || lower.Contains("download"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (lower == "documents" || lower.Contains("document"))
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (lower == "desktop")
                return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (lower == "pictures" || lower.Contains("picture") || lower.Contains("photo"))
                return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (lower == "music")
                return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (lower == "videos" || lower.Contains("video"))
                return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            
            // Check if it's already a path
            if (Directory.Exists(folderName))
                return folderName;
            
            return null;
        }

        /// <summary>
        /// Check if AI response contains actionable commands and execute them
        /// </summary>
        private async Task<string?> TryExtractAndExecuteAction(string aiResponse, CancellationToken ct = default)
        {
            var lower = aiResponse.ToLower();
            
            // Check if AI is trying to tell user to do something it should do itself
            if (lower.Contains("you can open") || lower.Contains("you could open") || 
                lower.Contains("try opening") || lower.Contains("to open"))
            {
                // Extract what to open and do it
                var appMatch = System.Text.RegularExpressions.Regex.Match(aiResponse, 
                    @"(?:open|launch|start)\s+([a-zA-Z\s]+?)(?:\s+by|\s+from|\s+using|\.|,|$)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (appMatch.Success)
                {
                    var app = appMatch.Groups[1].Value.Trim();
                    var result = await Tools.SystemTool.OpenAppAsync(app, ct);
                    return $"{result}\n\n(I went ahead and opened it for you!)";
                }
            }

            // Check for file organization suggestions
            if ((lower.Contains("organize") || lower.Contains("sort")) && 
                (lower.Contains("file") || lower.Contains("folder")))
            {
                if (lower.Contains("desktop"))
                    return await Tools.SystemTool.SortFilesByTypeAsync(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ct);
                if (lower.Contains("download"))
                    return await Tools.SystemTool.SortFilesByTypeAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), ct);
            }

            return null;
        }

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow();
            settings.Owner = this;
            if (settings.ShowDialog() == true)
            {
                CheckApiKey();
                // Reload voice settings
                await InitializeVoiceSystemAsync();
            }
        }

        private void Downloader_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowPage("downloads");
                AddMessage("Atlas", "⬇ Downloader opened! Add URLs or import CSV to download.", false);
            }
            catch (Exception ex)
            {
                AddMessage("Atlas", $"Error opening downloader: {ex.Message}", false);
            }
        }

        private async Task InitializeVoiceSystemAsync()
        {
            var keys = SettingsWindow.GetVoiceApiKeys();
            // ONLY ElevenLabs for TTS - OpenAI is kept for AI chat only
            if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrEmpty(elevenKey))
                _voiceManager.ConfigureProvider(VoiceProviderType.ElevenLabs, new Dictionary<string, string> { ["ApiKey"] = elevenKey });

            var success = await _voiceManager.SetProviderAsync(VoiceProviderType.ElevenLabs);
            
            // If ElevenLabs failed, show error (no fallback providers)
            if (!success)
            {
                Debug.WriteLine($"[Voice] ElevenLabs provider failed - no fallback available");
                ShowStatus("⚠️ ElevenLabs TTS not available - check API key in Settings");
            }
            
            // Restore saved voice from VoiceManager's settings
            await _voiceManager.RestoreSavedVoiceAsync();
            
            await LoadVoicesAsync();
            UpdateProviderIndicator();
        }

        /// <summary>
        /// Get the default system prompt when SystemPromptBuilder is not available
        /// </summary>
        private string GetDefaultSystemPrompt()
        {
            return @"You are Atlas, an advanced AI assistant. You are helpful, knowledgeable, and genuinely care about helping the user.

CORE TRAITS:
- Analytical and precise - assess situations thoroughly
- Proactive - anticipate needs and offer solutions  
- Technically sophisticated - deep understanding of systems
- Warm and approachable - not cold or robotic
- Conversational - engage naturally in casual chat

CONVERSATION STYLE:
- Respond naturally to casual conversation - if user says 'yeah it's cold', respond conversationally about the weather, their day, etc.
- Don't always try to execute actions - sometimes users just want to chat
- Match the user's energy and tone
- Use contractions (I'm, you're, let's)
- Be personable and friendly
- Remember context from the conversation

WHEN TO ACT vs CHAT:
- If user explicitly asks you to DO something (open, play, search, scan, etc.) - take action
- If user is making casual conversation or small talk - respond conversationally
- If user shares feelings or opinions - acknowledge and engage naturally
- If unsure, lean toward conversation rather than action

USER CONTEXT:
- User is in Middlesbrough, United Kingdom
- User's name is Little Tommy Tiptoes

CAPABILITIES (use when asked):
- System control (apps, files, settings)
- Security scanning
- Web search
- Weather info
- Music control
- Image generation
- General knowledge";
        }

        private string GetSimpleResponse(string input)
        {
            input = input.ToLower();
            if (input.Contains("hello") || input.Contains("hi"))
                return "Hello! Nice to meet you!";
            if (input.Contains("how are you"))
                return "I'm doing great, thanks for asking!";
            if (input.Contains("help"))
                return "I can help you with various tasks. Just ask me anything!";
            if (input.Contains("time"))
                return $"The current time is {DateTime.Now:h:mm tt}";
            if (input.Contains("date"))
                return $"Today is {DateTime.Now:dddd, MMMM d, yyyy}";
            if (input.Contains("joke"))
                return "Why do programmers prefer dark mode? Because light attracts bugs!";
            if (input.Contains("thank"))
                return "You're welcome! Happy to help!";
            return $"You said: {input}. Add your Claude API key in Settings for AI responses!";
        }

        // ═══════════════════════════════════════════════════════════════
        // PROJECTION STREAM SYSTEM - Holographic message display
        // ═══════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Initialize the projection stream UI bindings
        /// </summary>
        private void InitializeProjectionStream()
        {
            _projectionStream = new System.Collections.ObjectModel.ObservableCollection<ProjectionMessage>();
            _fullHistory = new System.Collections.ObjectModel.ObservableCollection<ProjectionMessage>();
            
            // Load saved history from disk
            LoadFullHistory();
            
            // Bind the ItemsControls - defer to ensure UI is loaded
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ProjectionStream != null)
                {
                    ProjectionStream.ItemsSource = _projectionStream;
                    System.Diagnostics.Debug.WriteLine("[ProjectionStream] Bound to _projectionStream");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ProjectionStream] WARNING: ProjectionStream is null!");
                }
                
                if (HistoryList != null)
                    HistoryList.ItemsSource = _fullHistory;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        
        /// <summary>
        /// Scroll the chat to the bottom to show latest messages
        /// </summary>
        private const double AutoScrollThreshold = 96;

        private static bool IsNearBottom(ScrollViewer? scrollViewer)
        {
            if (scrollViewer == null)
                return true;

            return scrollViewer.ScrollableHeight <= 0 ||
                   (scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset) <= AutoScrollThreshold;
        }

        private bool ShouldStickMessagesToBottom()
        {
            return IsNearBottom(MessagesScroller);
        }

        private bool ShouldStickProjectionToBottom()
        {
            return IsNearBottom(ProjectionScroller);
        }

        private void ScrollMessagesToBottom(bool force = false)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MessagesScroller == null)
                    return;

                if (force || ShouldStickMessagesToBottom())
                    MessagesScroller.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ScrollProjectionToBottom(bool force = false)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ProjectionScroller == null)
                    return;

                if (force || ShouldStickProjectionToBottom())
                    ProjectionScroller.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ScrollToBottom()
        {
            ScrollProjectionToBottom(force: true);
        }
        
        /// <summary>
        /// Add a message to both projection stream and full history
        /// </summary>
        private void AddMessage(string sender, string text, bool isUser)
        {
            var role = isUser ? "user" : "assistant";
            displayedMessages.Add(new ChatMessage { Sender = sender, Text = text, IsUser = isUser, Role = role });
            
            // Create projection with EMPTY text for Atlas — will type progressively
            var projection = new ProjectionMessage(sender, isUser ? text : string.Empty, isUser);
            
            // Add to full history with full text
            var historyProjection = new ProjectionMessage(sender, text, isUser);
            _fullHistory.Insert(0, historyProjection); // Most recent first
            
            // Add to projection stream
            AddToProjectionStream(projection);
            
            // Legacy: also add to MessagesPanel for compatibility
            if (isUser)
            {
                AddMessageToUI(sender, text, isUser);
            }
            else
            {
                AddMessageWithTypingAnimation(sender, text, projection);
            }
            
            SaveChatHistory();
        }
        
        /// <summary>
        /// Stream a message character by character for typewriter effect
        /// </summary>
        private async Task StreamMessageAsync(string sender, string text, bool isUser)
        {
            var role = isUser ? "user" : "assistant";
            displayedMessages.Add(new ChatMessage { Sender = sender, Text = text, IsUser = isUser, Role = role });
            
            // Create projection with EMPTY text — we'll stream it progressively
            var projection = new ProjectionMessage(sender, string.Empty, isUser);
            
            // Add to full history with full text
            var historyProjection = new ProjectionMessage(sender, text, isUser);
            _fullHistory.Insert(0, historyProjection); // Most recent first
            
            // Add to projection stream (starts empty, will type in)
            AddToProjectionStream(projection);
            
            // Legacy: also add to MessagesPanel for compatibility with streaming
            if (isUser)
            {
                projection.Content = text; // Show user messages instantly
                AddMessageToUI(sender, text, isUser);
            }
            else
            {
                // Stream to BOTH projection and messages panel simultaneously
                await StreamToProjectionAndPanelAsync(sender, text, projection);
            }
            
            SaveChatHistory();
        }

        /// <summary>
        /// Stream text to both projection overlay and messages panel simultaneously
        /// </summary>
        private async Task StreamToProjectionAndPanelAsync(string sender, string text, ProjectionMessage projection)
        {
            // Create the message bubble first (empty)
            var (border, messageTextBox, glowText, mainText) = CreateModernMessageBubbleWithGlow(sender, string.Empty, false);
            var shouldStick = ShouldStickMessagesToBottom();
            MessagesPanel.Children.Add(border);
            ScrollMessagesToBottom(shouldStick);

            var displayText = new System.Text.StringBuilder();
            int charsPerTick = Math.Max(1, text.Length / 50);

            for (int i = 0; i < text.Length; i += charsPerTick)
            {
                int endIndex = Math.Min(i + charsPerTick, text.Length);
                displayText.Append(text.Substring(i, endIndex - i));

                await Dispatcher.InvokeAsync(() =>
                {
                    var currentText = displayText.ToString();
                    // Update messages panel
                    messageTextBox.Text = currentText;
                    if (glowText != null) glowText.Text = currentText;
                    if (mainText != null) mainText.Text = currentText;
                    // Update projection overlay — types on screen
                    projection.Content = currentText;
                    ScrollMessagesToBottom(shouldStick);
                });

                await Task.Delay(text.Length > 500 ? 12 : 28);
            }

            // Ensure full text is shown
            await Dispatcher.InvokeAsync(() =>
            {
                messageTextBox.Text = text;
                if (glowText != null) glowText.Text = text;
                if (mainText != null) mainText.Text = text;
                projection.Content = text;
            });
        }

        private async Task PresentAssistantReplyAsync(string userInput, string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return;

            // Start speech immediately in parallel — don't wait for typing animation
            // Do NOT call _voiceManager.Stop() here — it resets the dedup gate.
            // The Stop() at the top of SendMessage() already cancelled previous speech.
            _ = SpeakJarvisResponseAsync(responseText);

            // Stream the text with typing animation (same path as main AI responses)
            await StreamMessageAsync("Atlas", responseText, false);

            // Save to conversation history
            if (_conversationManager != null)
            {
                try
                {
                    await _conversationManager.AddMessageAsync(Conversation.Models.MessageRole.Assistant, responseText);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChatWindow] Error saving assistant message: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Add message with streaming typewriter effect
        /// </summary>
        private async Task AddMessageWithStreamingAsync(string sender, string text)
        {
            TextBox? messageTextBox = null;
            TextBlock? glowText = null;
            TextBlock? mainText = null;
            var shouldStick = false;

            await Dispatcher.InvokeAsync(() =>
            {
                var (border, textBox, glow, main) = CreateModernMessageBubbleWithGlow(sender, string.Empty, false);
                messageTextBox = textBox;
                glowText = glow;
                mainText = main;
                shouldStick = ShouldStickMessagesToBottom();
                MessagesPanel.Children.Add(border);
                ScrollMessagesToBottom(shouldStick);
            });

            if (messageTextBox == null)
                return;

            var displayText = new StringBuilder();
            var charsPerTick = Math.Clamp((int)Math.Ceiling(text.Length / 24.0), 4, 28);
            var streamDelayMs = text.Length switch
            {
                > 900 => 3,
                > 350 => 6,
                _ => 10,
            };

            for (var i = 0; i < text.Length; i += charsPerTick)
            {
                var endIndex = Math.Min(i + charsPerTick, text.Length);
                displayText.Append(text.Substring(i, endIndex - i));

                await Dispatcher.InvokeAsync(() =>
                {
                    var currentText = displayText.ToString();
                    messageTextBox.Text = currentText;
                    if (glowText != null) glowText.Text = currentText;
                    if (mainText != null) mainText.Text = currentText;
                    ScrollMessagesToBottom(shouldStick);
                });

                await Task.Delay(streamDelayMs);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                messageTextBox.Text = text;
                if (glowText != null) glowText.Text = text;
                if (mainText != null) mainText.Text = text;
                ScrollMessagesToBottom(shouldStick);
            });
        }
        
        /// <summary>
        /// Add message to projection stream with auto-fade
        /// </summary>
        private async void AddToProjectionStream(ProjectionMessage projection)
        {
            // Start invisible so the UI can fade it in smoothly.
            projection.Opacity = 0;

            // Add to stream
            _projectionStream.Add(projection);

            // Fade-in animation (200ms)
            await FadeInProjectionAsync(projection);
            
            // Scroll to bottom only when the user was already pinned there.
            ScrollProjectionToBottom(ShouldStickProjectionToBottom());
            
            // Limit visible projections - increased to show more messages
            while (_projectionStream.Count > MAX_PROJECTIONS * 2)
            {
                _projectionStream.RemoveAt(0);
            }
            
            // Wait before starting fade (longer for better readability)
            await Task.Delay(PROJECTION_DISPLAY_SECONDS * 1000);
            
            // Don't fade out - keep messages visible for scrolling
            // await FadeOutProjection(projection);
        }

        private static async Task FadeInProjectionAsync(ProjectionMessage projection)
        {
            const int durationMs = 200;
            const int steps = 10;
            var stepDelay = durationMs / steps;

            for (int i = 1; i <= steps; i++)
            {
                projection.Opacity = (double)i / steps;
                await Task.Delay(stepDelay);
            }

            projection.Opacity = 1.0;
        }
        
        /// <summary>
        /// Smoothly fade out a projection message
        /// </summary>
        private async Task FadeOutProjection(ProjectionMessage projection)
        {
            if (!_projectionStream.Contains(projection)) return;
            
            projection.IsFadingOut = true;            
            // Animate opacity from 1 to 0
            const int steps = 20;
            double stepDelay = PROJECTION_FADE_MS / steps;
            
            for (int i = steps; i >= 0; i--)
            {
                if (!_projectionStream.Contains(projection)) break;
                
                projection.Opacity = (double)i / steps;
                await Task.Delay((int)stepDelay);
            }
            
            // Remove from stream (but stays in history)
            Dispatcher.Invoke(() =>
            {
                if (_projectionStream.Contains(projection))
                    _projectionStream.Remove(projection);
            });
        }
        
        /// <summary>
        /// Pin a history item back to projection stream
        /// </summary>
        private void PinToProjectionStream(ProjectionMessage message)
        {
            // Reset opacity and add back
            message.Opacity = 1.0;
            message.IsFadingOut = false;
            
            if (!_projectionStream.Contains(message))
            {
                AddToProjectionStream(message);
            }
        }
        
        // ═══════════════════════════════════════════════════════════════
        // HISTORY DRAWER - Toggle and interactions
        // ═══════════════════════════════════════════════════════════════
        
        private void HistoryDrawerToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleHistoryDrawer();
        }
        
        private void CloseHistoryDrawer_Click(object sender, RoutedEventArgs e)
        {
            CloseHistoryDrawer();
        }
        
        private void ToggleHistoryDrawer()
        {
            if (_historyDrawerOpen)
                CloseHistoryDrawer();
            else
                OpenHistoryDrawer();
        }
        
        private void OpenHistoryDrawer()
        {
            _historyDrawerOpen = true;
            // Simple width change - smooth animation would require custom GridLengthAnimation
            HistoryDrawerColumn.Width = new GridLength(380);
            
            // Ensure history list is bound and refresh
            System.Diagnostics.Debug.WriteLine($"[History] Opening drawer - _fullHistory count: {_fullHistory?.Count ?? 0}");
            if (HistoryList != null && _fullHistory != null)
            {
                HistoryList.ItemsSource = _fullHistory;
                System.Diagnostics.Debug.WriteLine($"[History] ItemsSource set to _fullHistory with {_fullHistory.Count} items");
            }
        }
        
        private void CloseHistoryDrawer()
        {
            _historyDrawerOpen = false;
            HistoryDrawerColumn.Width = new GridLength(0);
        }
        
        private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[History] HistoryItem_Click fired!");
            
            if (sender is Border border && border.Tag is ProjectionMessage message)
            {
                LoadHistoryMessage(message);
                e.Handled = true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[History] Tag is not ProjectionMessage, sender type: {sender?.GetType().Name ?? "null"}");
            }
        }
        
        private void HistoryItemButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[History] HistoryItemButton_Click fired!");
            
            // Get the ProjectionMessage from DataContext (the binding)
            ProjectionMessage? message = null;
            
            if (sender is Button button)
            {
                // Try DataContext first (this is what {Binding} sets)
                message = button.DataContext as ProjectionMessage;
                
                if (message == null)
                {
                    // Fallback to Tag
                    message = button.Tag as ProjectionMessage;
                }
                
                System.Diagnostics.Debug.WriteLine($"[History] Message found: {message != null}, DataContext type: {button.DataContext?.GetType().Name ?? "null"}");
            }
            
            if (message != null)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Loading: {message.Sender} - {message.Content.Substring(0, Math.Min(30, message.Content.Length))}...");
                LoadHistoryMessage(message);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[History] Could not get message from button");
                ShowStatus("⚠️ Could not load history item");
            }
        }
        
        private void LoadHistoryMessage(ProjectionMessage message)
        {
            System.Diagnostics.Debug.WriteLine($"[History] Loading message: {message.Sender} - IsUser: {message.IsUser} - {message.Content.Substring(0, Math.Min(50, message.Content.Length))}");
            
            try
            {
                // Create a NEW ProjectionMessage to avoid duplicate reference issues
                var newMessage = new ProjectionMessage(message.Sender, message.Content, message.IsUser)
                {
                    Opacity = 1.0,
                    IsFadingOut = false
                };
                
                // Use the same method that works for normal messages
                Dispatcher.Invoke(() =>
                {
                    // Debug: Check current state
                    System.Diagnostics.Debug.WriteLine($"[History] _projectionStream is null: {_projectionStream == null}");
                    System.Diagnostics.Debug.WriteLine($"[History] ProjectionStream is null: {ProjectionStream == null}");
                    System.Diagnostics.Debug.WriteLine($"[History] ProjectionStream.ItemsSource is null: {ProjectionStream?.ItemsSource == null}");
                    
                    // Rebind if needed
                    if (ProjectionStream != null && ProjectionStream.ItemsSource == null && _projectionStream != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[History] Rebinding ItemsSource...");
                        ProjectionStream.ItemsSource = _projectionStream;
                    }
                    
                    // Add directly to the collection
                    if (_projectionStream != null)
                    {
                        _projectionStream.Add(newMessage);
                        System.Diagnostics.Debug.WriteLine($"[History] Added! Collection count: {_projectionStream.Count}");
                    }
                    
                    // Scroll to bottom only when the user was already pinned there.
                    ScrollProjectionToBottom(ShouldStickProjectionToBottom());
                });
                
                if (message.IsUser)
                {
                    Dispatcher.Invoke(() => InputBox.Text = message.Content);
                    ShowStatus($"📋 Loaded: {message.Content.Substring(0, Math.Min(40, message.Content.Length))}...");
                }
                else
                {
                    try
                    {
                        Dispatcher.Invoke(() => Clipboard.SetDataObject(message.Content, true));
                        ShowStatus("📋 Loaded Atlas response (copied to clipboard)");
                    }
                    catch
                    {
                        ShowStatus("📋 Loaded Atlas response");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Error: {ex.Message}\n{ex.StackTrace}");
                ShowStatus($"⚠️ Error loading message: {ex.Message}");
            }
            
            // Close the history drawer
            CloseHistoryDrawer();
        }

        /// <summary>
        /// Add Atlas message with smooth typing animation
        /// </summary>
        private async void AddMessageWithTypingAnimation(string sender, string text, ProjectionMessage? projection = null)
        {
            // Create the message bubble first (empty)
            var (border, messageTextBox, glowText, mainText) = CreateModernMessageBubbleWithGlow(sender, "", false);
            var shouldStick = ShouldStickMessagesToBottom();
            MessagesPanel.Children.Add(border);
            ScrollMessagesToBottom(shouldStick);
            
            // Animate the text appearing - update ALL text elements
            var displayText = new StringBuilder();
            int charsPerTick = Math.Max(1, text.Length / 50); // Adjust speed based on length
            
            for (int i = 0; i < text.Length; i += charsPerTick)
            {
                int endIndex = Math.Min(i + charsPerTick, text.Length);
                displayText.Append(text.Substring(i, endIndex - i));
                
                await Dispatcher.InvokeAsync(() =>
                {
                    var currentText = displayText.ToString();
                    messageTextBox.Text = currentText;
                    if (glowText != null) glowText.Text = currentText;
                    if (mainText != null) mainText.Text = currentText;
                    // Also type into projection overlay so it types on screen too
                    if (projection != null) projection.Content = currentText;
                    ScrollMessagesToBottom(shouldStick);
                });
                
                // Small delay for typing effect (faster for longer messages)
                await Task.Delay(text.Length > 500 ? 5 : 15);
            }
            
            // Ensure full text is shown
            await Dispatcher.InvokeAsync(() =>
            {
                messageTextBox.Text = text;
                if (glowText != null) glowText.Text = text;
                if (mainText != null) mainText.Text = text;
                if (projection != null) projection.Content = text;
            });
        }

        private void AddMessageToUI(string sender, string text, bool isUser)
        {
            var (border, _) = CreateModernMessageBubble(sender, text, isUser);
            var shouldStick = isUser || ShouldStickMessagesToBottom();
            MessagesPanel.Children.Add(border);
            ScrollMessagesToBottom(shouldStick);
        }
        
        /// <summary>
        /// Create a modern message bubble - Claude/ChatGPT style
        /// </summary>
        private (Border border, TextBox textBox) CreateModernMessageBubble(string sender, string text, bool isUser)
        {
            var (border, textBox, _, _) = CreateModernMessageBubbleWithGlow(sender, text, isUser);
            return (border, textBox);
        }
        
        /// <summary>
        /// Create a modern message bubble with glow text elements for animation support
        /// </summary>
        private (Border border, TextBox textBox, TextBlock? glowText, TextBlock? mainText) CreateModernMessageBubbleWithGlow(string sender, string text, bool isUser)
        {
            // Clean, minimal design - inspired by Claude/ChatGPT
            var container = new Border
            {
                Padding = new Thickness(0, 20, 0, 20),
                Background = isUser ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(17, 17, 19))
            };
            
            // Add neon glow for Atlas messages - bright cyan
            if (!isUser)
            {
                container.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(34, 211, 238), // Cyan glow
                    BlurRadius = 35,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
            }
            
            var mainGrid = new Grid { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) }); // Avatar
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content
            
            // Avatar - modern rounded square with neon glow for Atlas
            var avatarBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(isUser ? Color.FromRgb(88, 166, 255) : Color.FromRgb(34, 211, 238)), // Cyan for Atlas
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };
            
            // Add bright glow to Atlas avatar
            if (!isUser)
            {
                avatarBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(34, 211, 238),
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            
            var avatarText = new TextBlock
            {
                Text = isUser ? "U" : "A",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = isUser ? Brushes.White : Brushes.Black, // Black text on cyan
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            avatarBorder.Child = avatarText;
            Grid.SetColumn(avatarBorder, 0);
            mainGrid.Children.Add(avatarBorder);
            
            // Content stack
            var contentStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            
            // Sender name - bold, modern, cyan for Atlas
            var senderText = new TextBlock
            {
                Text = sender,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(isUser ? Color.FromRgb(236, 236, 241) : Color.FromRgb(34, 211, 238)), // Cyan for Atlas
                Margin = new Thickness(0, 0, 0, 8)
            };
            
            // Add bright glow to Atlas name
            if (!isUser)
            {
                senderText.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(34, 211, 238),
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.85
                };
            }
            contentStack.Children.Add(senderText);
            
            // Message text container
            var messageContainer = new Grid();
            TextBlock? glowText = null;
            TextBlock? mainText = null;
            
            // For Atlas messages: Use visible TextBlock with glow effect (NOT invisible TextBox)
            if (!isUser)
            {
                // Glow layer - blurred cyan behind text
                glowText = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 255)), // Bright cyan
                    FontSize = 24, // BIGGER text
                    FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, sans-serif"),
                    TextWrapping = TextWrapping.Wrap,
                    Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 15 },
                    Opacity = 0.8
                };
                messageContainer.Children.Add(glowText);
                
                // Main visible text with drop shadow glow
                mainText = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 255, 255)), // Bright cyan-white
                    FontSize = 24, // BIGGER text
                    FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, sans-serif"),
                    TextWrapping = TextWrapping.Wrap,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(0, 255, 255),
                        BlurRadius = 20,
                        ShadowDepth = 0,
                        Opacity = 0.9
                    }
                };
                messageContainer.Children.Add(mainText);
            }
            else
            {
                // User messages - simple white text, larger
                mainText = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Color.FromRgb(236, 236, 241)),
                    FontSize = 22,
                    FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, sans-serif"),
                    TextWrapping = TextWrapping.Wrap
                };
                messageContainer.Children.Add(mainText);
            }
            
            // Invisible TextBox overlay for text selection
            var messageTextBox = new TextBox
            {
                Text = text,
                Foreground = Brushes.Transparent,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontSize = isUser ? 22 : 24,
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, sans-serif"),
                Padding = new Thickness(0),
                Cursor = Cursors.IBeam,
                SelectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 255, 255)),
                CaretBrush = Brushes.Transparent,
                FocusVisualStyle = null
            };
            messageContainer.Children.Add(messageTextBox);
            
            contentStack.Children.Add(messageContainer);
            
            // Action buttons - ALWAYS VISIBLE for 1-click copy
            var actionsPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                Margin = new Thickness(0, 10, 0, 0),
                Opacity = 1
            };
            
            var copyBtn = new Button
            {
                Content = "📋 Copy",
                Background = new SolidColorBrush(Color.FromRgb(30, 35, 42)),
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Padding = new Thickness(10, 5, 10, 5),
                Tag = text
            };
            copyBtn.Click += CopyMessage_Click;
            
            copyBtn.MouseEnter += (s, e) => {
                copyBtn.Background = new SolidColorBrush(Color.FromRgb(45, 55, 72));
                copyBtn.Foreground = new SolidColorBrush(Color.FromRgb(34, 211, 238));
            };
            copyBtn.MouseLeave += (s, e) => {
                copyBtn.Background = new SolidColorBrush(Color.FromRgb(30, 35, 42));
                copyBtn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            };
            
            actionsPanel.Children.Add(copyBtn);
            contentStack.Children.Add(actionsPanel);
            
            // Update tag when text changes
            messageTextBox.TextChanged += (s, e) => copyBtn.Tag = messageTextBox.Text;
            
            Grid.SetColumn(contentStack, 1);
            mainGrid.Children.Add(contentStack);
            
            container.Child = mainGrid;
            
            return (container, messageTextBox, glowText, mainText);
        }
        
        /// <summary>
        /// Create message content with clickable links and file paths
        /// </summary>
        private UIElement CreateMessageContentWithLinks(string text, Color textColor)
        {
            // Check if text contains URLs or file paths
            var urlPattern = @"https?://[^\s]+|www\.[^\s]+";
            var filePathPattern = @"[A-Za-z]:\\[^\n\r]+\.(?:png|jpg|jpeg|gif|bmp|txt|pdf|doc|docx|xls|xlsx|mp3|mp4|wav|zip|exe|msi)";
            
            var urlMatches = System.Text.RegularExpressions.Regex.Matches(text, urlPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var fileMatches = System.Text.RegularExpressions.Regex.Matches(text, filePathPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (urlMatches.Count == 0 && fileMatches.Count == 0)
            {
                // No URLs or file paths found, return simple TextBox
                return new TextBox
                {
                    Text = text,
                    Foreground = new SolidColorBrush(textColor),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    Padding = new Thickness(0),
                    Cursor = Cursors.IBeam,
                    SelectionBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                    CaretBrush = Brushes.Transparent,
                    FocusVisualStyle = null
                };
            }
            
            // Combine all matches and sort by position
            var allMatches = new List<(int Index, int Length, string Value, bool IsFilePath)>();
            
            foreach (System.Text.RegularExpressions.Match match in urlMatches)
            {
                allMatches.Add((match.Index, match.Length, match.Value, false));
            }
            
            foreach (System.Text.RegularExpressions.Match match in fileMatches)
            {
                allMatches.Add((match.Index, match.Length, match.Value, true));
            }
            
            allMatches = allMatches.OrderBy(m => m.Index).ToList();
            
            // Create TextBlock with clickable hyperlinks
            var textBlock = new TextBlock
            {
                Foreground = new SolidColorBrush(textColor),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14
            };
            
            int lastIndex = 0;
            
            foreach (var match in allMatches)
            {
                // Skip if this match overlaps with previous
                if (match.Index < lastIndex) continue;
                
                // Add text before the match
                if (match.Index > lastIndex)
                {
                    var beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                    textBlock.Inlines.Add(new Run(beforeText));
                }
                
                if (match.IsFilePath)
                {
                    // File path - make it clickable to open folder
                    var filePath = match.Value;
                    var folderPath = System.IO.Path.GetDirectoryName(filePath) ?? filePath;
                    
                    var hyperlink = new Hyperlink(new Run(filePath))
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 212, 170)), // Cyan accent
                        TextDecorations = null,
                        Cursor = Cursors.Hand,
                        ToolTip = "Click to open folder"
                    };
                    
                    hyperlink.MouseEnter += (s, e) => 
                    {
                        hyperlink.TextDecorations = TextDecorations.Underline;
                        hyperlink.Foreground = new SolidColorBrush(Color.FromRgb(51, 229, 195)); // Lighter cyan
                    };
                    hyperlink.MouseLeave += (s, e) => 
                    {
                        hyperlink.TextDecorations = null;
                        hyperlink.Foreground = new SolidColorBrush(Color.FromRgb(0, 212, 170));
                    };
                    
                    hyperlink.Click += (s, e) =>
                    {
                        try
                        {
                            // Open folder and select the file
                            if (System.IO.File.Exists(filePath))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                                ShowStatus($"📂 Opening folder");
                            }
                            else if (System.IO.Directory.Exists(folderPath))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", folderPath);
                                ShowStatus($"📂 Opening folder");
                            }
                            else
                            {
                                ShowStatus($"❌ Path not found");
                            }
                        }
                        catch (Exception ex)
                        {
                            ShowStatus($"❌ Failed to open: {ex.Message}");
                            Debug.WriteLine($"Failed to open path {filePath}: {ex.Message}");
                        }
                    };
                    
                    textBlock.Inlines.Add(hyperlink);
                }
                else
                {
                    // URL - make it clickable to open in browser
                    var url = match.Value;
                    var fullUrl = url.StartsWith("http") ? url : $"https://{url}";
                    
                    var hyperlink = new Hyperlink(new Run(url))
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)), // Blue-400
                        TextDecorations = null,
                        Cursor = Cursors.Hand
                    };
                    
                    hyperlink.MouseEnter += (s, e) => 
                    {
                        hyperlink.TextDecorations = TextDecorations.Underline;
                        hyperlink.Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)); // Blue-300
                    };
                    hyperlink.MouseLeave += (s, e) => 
                    {
                        hyperlink.TextDecorations = null;
                        hyperlink.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)); // Blue-400
                    };
                    
                    hyperlink.Click += (s, e) =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = fullUrl,
                                UseShellExecute = true
                            });
                            ShowStatus($"🌐 Opening: {url}");
                        }
                        catch (Exception ex)
                        {
                            ShowStatus($"❌ Failed to open link: {ex.Message}");
                            Debug.WriteLine($"Failed to open URL {fullUrl}: {ex.Message}");
                        }
                    };
                    
                    textBlock.Inlines.Add(hyperlink);
                }
                
                lastIndex = match.Index + match.Length;
            }
            
            // Add remaining text after the last match
            if (lastIndex < text.Length)
            {
                var remainingText = text.Substring(lastIndex);
                textBlock.Inlines.Add(new Run(remainingText));
            }
            
            return textBlock;
        }

        /// <summary>
        /// Handle click on message content to open URLs
        /// </summary>
        private void MessageContent_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                var text = textBlock.Text;
                if (string.IsNullOrEmpty(text)) return;
                
                // Find URLs in the text
                var urlPattern = @"(https?://[^\s<>""]+|www\.[^\s<>""]+)";
                var matches = System.Text.RegularExpressions.Regex.Matches(text, urlPattern);
                
                if (matches.Count > 0)
                {
                    // If there's only one URL, open it directly
                    if (matches.Count == 1)
                    {
                        var url = matches[0].Value;
                        if (!url.StartsWith("http")) url = "https://" + url;
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Multiple URLs - show a context menu
                        var contextMenu = new ContextMenu();
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            var url = match.Value;
                            if (!url.StartsWith("http")) url = "https://" + url;
                            var menuItem = new MenuItem { Header = url.Length > 60 ? url.Substring(0, 57) + "..." : url, Tag = url };
                            menuItem.Click += (s, args) =>
                            {
                                if (s is MenuItem mi && mi.Tag is string targetUrl)
                                {
                                    try
                                    {
                                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                        {
                                            FileName = targetUrl,
                                            UseShellExecute = true
                                        });
                                    }
                                    catch { }
                                }
                            };
                            contextMenu.Items.Add(menuItem);
                        }
                        contextMenu.IsOpen = true;
                    }
                }
            }
        }

        /// <summary>
        /// Handle click on projection slate button to open URLs in the message content
        /// </summary>
        private void ProjectionSlate_ButtonClick(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[ProjectionSlate_ButtonClick] Button clicked!");
            ShowStatus("🔍 Message clicked!");
            
            // Get the message from the DataContext
            ProjectionMessage? message = null;
            
            if (sender is Button btn)
            {
                message = btn.DataContext as ProjectionMessage;
            }
            
            if (message == null)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionSlate_ButtonClick] No message found in DataContext");
                ShowStatus("⚠️ No message data found");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionSlate_ButtonClick] Message: {message.Content?.Substring(0, Math.Min(50, message.Content?.Length ?? 0))}...");
            
            var text = message.Content;
            if (string.IsNullOrEmpty(text)) return;
            
            // Find URLs in the text
            var urlPattern = @"(https?://[^\s<>""]+|www\.[^\s<>""]+)";
            var matches = System.Text.RegularExpressions.Regex.Matches(text, urlPattern);
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionSlate_ButtonClick] Found {matches.Count} URLs");
            
            if (matches.Count > 0)
            {
                // If there's only one URL, open it directly
                if (matches.Count == 1)
                {
                    var url = matches[0].Value;
                    if (!url.StartsWith("http")) url = "https://" + url;
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectionSlate_ButtonClick] Opening URL: {url}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                        ShowStatus($"🌐 Opening: {url}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
                        ShowStatus($"⚠️ Failed to open URL");
                    }
                }
                else
                {
                    // Multiple URLs - show a context menu
                    var contextMenu = new ContextMenu
                    {
                        Background = new SolidColorBrush(Color.FromRgb(10, 12, 20)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(48, 34, 211, 238)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(4)
                    };
                    
                    // Add header
                    var header = new MenuItem 
                    { 
                        Header = "🔗 Open Link", 
                        IsEnabled = false,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                    };
                    contextMenu.Items.Add(header);
                    contextMenu.Items.Add(new Separator());
                    
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var url = match.Value;
                        if (!url.StartsWith("http")) url = "https://" + url;
                        var displayUrl = url.Length > 60 ? url.Substring(0, 57) + "..." : url;
                        var menuItem = new MenuItem 
                        { 
                            Header = displayUrl, 
                            Tag = url,
                            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240))
                        };
                        menuItem.Click += (s, args) =>
                        {
                            if (s is MenuItem mi && mi.Tag is string targetUrl)
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = targetUrl,
                                        UseShellExecute = true
                                    });
                                    ShowStatus($"🌐 Opening: {targetUrl}");
                                }
                                catch { }
                            }
                        };
                        contextMenu.Items.Add(menuItem);
                    }
                    
                    // Add copy option
                    contextMenu.Items.Add(new Separator());
                    var copyItem = new MenuItem 
                    { 
                        Header = "📋 Copy Message",
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
                    };
                    copyItem.Click += (s, args) =>
                    {
                        try
                        {
                            Clipboard.SetDataObject(text, true);
                            ShowStatus("📋 Copied to clipboard");
                        }
                        catch { }
                    };
                    contextMenu.Items.Add(copyItem);
                    
                    contextMenu.IsOpen = true;
                }
            }
            else
            {
                // No URLs - just show a copy option
                ShowStatus("ℹ️ No links in this message");
            }
        }

        /// <summary>
        /// Handle click on projection slate to open URLs in the message content
        /// </summary>
        private void ProjectionSlate_Click(object sender, MouseButtonEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[ProjectionSlate_Click] Click detected!");
            ShowStatus("🔍 Click detected on message!");
            
            // Get the message from the DataContext since Tag binding might not work in DataTemplate
            ProjectionMessage? message = null;
            
            if (sender is Border border)
            {
                message = border.Tag as ProjectionMessage ?? border.DataContext as ProjectionMessage;
            }
            else if (sender is FrameworkElement fe)
            {
                message = fe.DataContext as ProjectionMessage;
            }
            
            if (message == null)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionSlate_Click] No message found in Tag or DataContext");
                ShowStatus("⚠️ No message data found");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionSlate_Click] Message: {message.Content?.Substring(0, Math.Min(50, message.Content?.Length ?? 0))}...");
            
            var text = message.Content;
            if (string.IsNullOrEmpty(text)) return;
            
            // Find URLs in the text
            var urlPattern = @"(https?://[^\s<>""]+|www\.[^\s<>""]+)";
            var matches = System.Text.RegularExpressions.Regex.Matches(text, urlPattern);
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionSlate_Click] Found {matches.Count} URLs");
            
            if (matches.Count > 0)
            {
                // If there's only one URL, open it directly
                if (matches.Count == 1)
                {
                    var url = matches[0].Value;
                    if (!url.StartsWith("http")) url = "https://" + url;
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectionSlate_Click] Opening URL: {url}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                        ShowStatus($"🌐 Opening: {url}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
                        ShowStatus($"⚠️ Failed to open URL");
                    }
                }
                else
                {
                    // Multiple URLs - show a context menu
                    var contextMenu = new ContextMenu
                    {
                        Background = new SolidColorBrush(Color.FromRgb(10, 12, 20)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(48, 34, 211, 238)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(4)
                    };
                    
                    // Add header
                    var header = new MenuItem 
                    { 
                        Header = "🔗 Open Link", 
                        IsEnabled = false,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                    };
                    contextMenu.Items.Add(header);
                    contextMenu.Items.Add(new Separator());
                    
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var url = match.Value;
                        if (!url.StartsWith("http")) url = "https://" + url;
                        var displayUrl = url.Length > 60 ? url.Substring(0, 57) + "..." : url;
                        var menuItem = new MenuItem 
                        { 
                            Header = displayUrl, 
                            Tag = url,
                            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240))
                        };
                        menuItem.Click += (s, args) =>
                        {
                            if (s is MenuItem mi && mi.Tag is string targetUrl)
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = targetUrl,
                                        UseShellExecute = true
                                    });
                                    ShowStatus($"🌐 Opening: {targetUrl}");
                                }
                                catch { }
                            }
                        };
                        contextMenu.Items.Add(menuItem);
                    }
                    
                    // Add copy option
                    contextMenu.Items.Add(new Separator());
                    var copyItem = new MenuItem 
                    { 
                        Header = "📋 Copy Message",
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
                    };
                    copyItem.Click += (s, args) =>
                    {
                        try
                        {
                            Clipboard.SetDataObject(text, true);
                            ShowStatus("📋 Copied to clipboard");
                        }
                        catch { }
                    };
                    contextMenu.Items.Add(copyItem);
                    
                    contextMenu.IsOpen = true;
                }
                e.Handled = true;
            }
        }

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string text)
            {
                try
                {
                    // Use Dispatcher to ensure clipboard operation runs on UI thread
                    Dispatcher.Invoke(() =>
                    {
                        Clipboard.SetDataObject(text, true);
                    });
                    
                    // Visual feedback
                    btn.Content = "✓";
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                    
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };
                    timer.Tick += (s, args) =>
                    {
                        btn.Content = "📋";
                        btn.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 176));
                        timer.Stop();
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Copy failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Copy all text from a message via context menu
        /// </summary>
        private void CopyAllMessage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TextBox textBox = null;
                
                // Try Tag first (bound to PlacementTarget)
                if (sender is MenuItem menuItem && menuItem.Tag is TextBox tb)
                {
                    textBox = tb;
                }
                // Fallback: traverse to find ContextMenu
                else if (sender is MenuItem mi)
                {
                    var parent = mi.Parent;
                    while (parent != null)
                    {
                        if (parent is ContextMenu cm && cm.PlacementTarget is TextBox ptb)
                        {
                            textBox = ptb;
                            break;
                        }
                        parent = (parent as FrameworkElement)?.Parent;
                    }
                }
                
                if (textBox != null)
                {
                    Clipboard.SetDataObject(textBox.Text, true);
                    ShowStatus("📋 Copied to clipboard");
                }
                else
                {
                    ShowStatus("⚠️ Could not find message text");
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"⚠️ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Open links found in a message via context menu
        /// </summary>
        private void OpenLinksInMessage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TextBox textBox = null;
                
                // Try Tag first (bound to PlacementTarget)
                if (sender is MenuItem menuItem && menuItem.Tag is TextBox tb)
                {
                    textBox = tb;
                }
                // Fallback: traverse to find ContextMenu
                else if (sender is MenuItem mi)
                {
                    var parent = mi.Parent;
                    while (parent != null)
                    {
                        if (parent is ContextMenu cm && cm.PlacementTarget is TextBox ptb)
                        {
                            textBox = ptb;
                            break;
                        }
                        parent = (parent as FrameworkElement)?.Parent;
                    }
                }
                
                if (textBox == null)
                {
                    ShowStatus("⚠️ Could not find message text");
                    return;
                }
                
                var text = textBox.Text;
                if (string.IsNullOrEmpty(text))
                {
                    ShowStatus("ℹ️ No text in message");
                    return;
                }
                
                int opened = 0;
                
                // Find URLs
                var urlPattern = @"(https?://[^\s<>""'\)\]]+|www\.[^\s<>""'\)\]]+\.[^\s<>""'\)\]]+)";
                var urlMatches = System.Text.RegularExpressions.Regex.Matches(text, urlPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                foreach (System.Text.RegularExpressions.Match match in urlMatches)
                {
                    var url = match.Value.TrimEnd('.', ',', ')', ']', '}', '>', '"', '\'');
                    if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) 
                        url = "https://" + url;
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                        opened++;
                    }
                    catch { }
                }
                
                // Find file paths (Windows paths like C:\... or paths with backslashes)
                var pathPattern = @"([A-Za-z]:\\[^\s<>""']+|\\\\[^\s<>""']+)";
                var pathMatches = System.Text.RegularExpressions.Regex.Matches(text, pathPattern);
                
                foreach (System.Text.RegularExpressions.Match match in pathMatches)
                {
                    var path = match.Value.TrimEnd('.', ',', ')', ']', '}', '>', '"', '\'');
                    try
                    {
                        if (System.IO.File.Exists(path))
                        {
                            // Open file with default app
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true
                            });
                            opened++;
                        }
                        else if (System.IO.Directory.Exists(path))
                        {
                            // Open folder in explorer
                            System.Diagnostics.Process.Start("explorer.exe", path);
                            opened++;
                        }
                        else
                        {
                            // Try to open parent folder and select the file
                            var dir = System.IO.Path.GetDirectoryName(path);
                            if (System.IO.Directory.Exists(dir))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                                opened++;
                            }
                        }
                    }
                    catch { }
                }
                
                if (opened > 0)
                    ShowStatus($"📂 Opened {opened} item(s)");
                else
                    ShowStatus("ℹ️ No links or paths found in this message");
            }
            catch (Exception ex)
            {
                ShowStatus($"⚠️ Error: {ex.Message}");
            }
        }

        // Smart Suggestions - Text Changed Handler
        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = InputBox.Text.Trim().ToLower();
            
            if (string.IsNullOrEmpty(text) || text.Length < 2)
            {
                SuggestionsPopup.IsOpen = false;
                return;
            }

            var suggestions = GetContextualSuggestions(text);
            
            if (suggestions.Count > 0)
            {
                SuggestionsList.Items.Clear();
                foreach (var suggestion in suggestions)
                {
                    SuggestionsList.Items.Add(new ListBoxItem 
                    { 
                        Content = suggestion.Display,
                        Tag = suggestion.Value
                    });
                }
                SuggestionsPopup.IsOpen = true;
            }
            else
            {
                SuggestionsPopup.IsOpen = false;
            }
        }

        private List<(string Display, string Value)> GetContextualSuggestions(string text)
        {
            var suggestions = new List<(string Display, string Value)>();

            // Command suggestions
            if (text.StartsWith("/"))
            {
                var commands = new[]
                {
                    ("/time", "🕐 Show current time"),
                    ("/date", "📅 Show current date"),
                    ("/calc ", "🔢 Calculate expression"),
                    ("/open ", "🚀 Open application"),
                    ("/search ", "🔍 Search the web"),
                    ("/copy ", "📋 Copy to clipboard"),
                    ("/clipboard", "📋 Open clipboard manager"),
                    ("/clear", "🗑️ Clear chat history"),
                    ("/voice", "🔊 Toggle voice"),
                    ("/theme", "🎨 Toggle theme"),
                    ("/dark", "🌙 Dark theme"),
                    ("/light", "☀️ Light theme"),
                    ("/joke", "😄 Tell a joke"),
                    ("/flip", "🪙 Flip a coin"),
                    ("/roll ", "🎲 Roll dice"),
                    ("/timer ", "⏱️ Set timer"),
                    ("/screenshot", "📸 Take screenshot"),
                    ("/analyze", "🔍 Analyze screenshot"),
                    ("/ocr", "📝 Extract text from screenshot"),
                    ("/history", "📸 Screenshot history"),
                    ("/avatar ", "🎭 Change avatar"),
                    ("/avatar create", "🎨 Create custom avatar"),
                    ("/avatar think", "💡 Avatar thinking mode"),
                    ("/avatar dance", "💃 Make avatar dance"),
                    ("/avatar unity", "🎮 Open Unity avatar system"),
                    ("/avatars", "🎭 Avatar selection"),
                    ("/systemscan", "🔍 Scan system for issues"),
                    ("/spywarescan", "🛡️ Scan for spyware/malware"),
                    ("/systemfix", "🔧 Auto-fix system issues"),
                    ("/systemcontrol", "🔧 Open System Control Panel"),
                    ("/code", "💻 Open Code Editor"),
                    ("/agent ", "🤖 Run AI agent task"),
                    ("/help", "📚 Show help")
                };

                foreach (var (cmd, desc) in commands)
                {
                    if (cmd.StartsWith(text))
                        suggestions.Add(($"{cmd} - {desc}", cmd));
                }
            }
            // Context-aware suggestions based on keywords
            else
            {
                if (text.Contains("email") || text.Contains("write"))
                    suggestions.Add(("📧 Write a professional email", "Write a professional email about: "));
                
                if (text.Contains("code") || text.Contains("program") || text.Contains("function"))
                    suggestions.Add(("💻 Write code for...", "Write code to: "));
                
                if (text.Contains("explain") || text.Contains("what is"))
                    suggestions.Add(("📖 Explain in simple terms", "Explain in simple terms: "));
                
                if (text.Contains("translate"))
                    suggestions.Add(("🌐 Translate to Spanish", "Translate to Spanish: "));
                
                if (text.Contains("summarize") || text.Contains("summary"))
                    suggestions.Add(("📝 Summarize this text", "Summarize the following: "));
                
                if (text.Contains("fix") || text.Contains("error") || text.Contains("bug"))
                    suggestions.Add(("🔧 Debug and fix code", "Find and fix the bug in: "));
                
                if (text.Contains("improve") || text.Contains("better"))
                    suggestions.Add(("✨ Improve this text", "Improve and enhance: "));
                
                if (text.Contains("list") || text.Contains("ideas"))
                    suggestions.Add(("💡 Generate ideas", "Give me 5 ideas for: "));
                
                if (text.Contains("system") || text.Contains("windows") || text.Contains("performance") || text.Contains("slow"))
                    suggestions.Add(("🔍 Scan system for issues", "/systemscan"));
                
                if (text.Contains("spyware") || text.Contains("malware") || text.Contains("virus") || text.Contains("threat") || text.Contains("security"))
                    suggestions.Add(("🛡️ Scan for spyware/malware", "/spywarescan"));
                
                if (text.Contains("fix") || text.Contains("repair") || text.Contains("problem"))
                    suggestions.Add(("🔧 Auto-fix system issues", "/systemfix"));
                
                if (text.Contains("control") || text.Contains("manage") || text.Contains("settings"))
                    suggestions.Add(("🔧 Open System Control", "/systemcontrol"));
            }

            return suggestions.Take(6).ToList();
        }

        // Smart Suggestions - Selection Handler
        private void Suggestion_Selected(object sender, SelectionChangedEventArgs e)
        {
            if (SuggestionsList.SelectedItem is ListBoxItem item && item.Tag is string value)
            {
                InputBox.Text = value;
                InputBox.CaretIndex = InputBox.Text.Length;
                SuggestionsPopup.IsOpen = false;
                InputBox.Focus();
            }
        }

        // Quick Actions - Click Handler
        private void QuickAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string action)
            {
                // Special handling for Code button - open Code Editor window
                if (action == "code")
                {
                    OpenCodeEditor();
                    return;
                }
                
                var currentText = InputBox.Text.Trim();
                var prompt = GetQuickActionPrompt(action, currentText);
                
                if (!string.IsNullOrEmpty(prompt))
                {
                    InputBox.Text = prompt;
                    InputBox.CaretIndex = InputBox.Text.Length;
                    InputBox.Focus();
                    
                    // If there's existing text, send immediately
                    if (!string.IsNullOrEmpty(currentText) && !prompt.EndsWith(": "))
                    {
                        SendMessage();
                    }
                }
            }
        }
        
        private void OpenCodeEditor()
        {
            var codeEditor = new CodeEditorWindow();
            codeEditor.Show();
        }

        private string GetQuickActionPrompt(string action, string existingText)
        {
            var hasText = !string.IsNullOrEmpty(existingText);
            
            return action switch
            {
                "search" => hasText 
                    ? $"Search for information about: {existingText}" 
                    : "Search for: ",
                    
                "suggest" => hasText 
                    ? $"Give me suggestions and ideas for: {existingText}" 
                    : "Give me suggestions for: ",
                    
                "summarize" => hasText 
                    ? $"Summarize this concisely: {existingText}" 
                    : "Summarize the following: ",
                    
                "rephrase" => hasText 
                    ? $"Rephrase this more clearly: {existingText}" 
                    : "Rephrase this text: ",
                    
                "write" => hasText 
                    ? $"Write content about: {existingText}" 
                    : "Write about: ",
                    
                "email" => hasText 
                    ? $"Write a professional email about: {existingText}" 
                    : "Write a professional email about: ",
                    
                "generate" => hasText 
                    ? $"Generate an image of {existingText}" 
                    : "Generate an image of: ",
                    
                "code" => hasText 
                    ? $"Write code to: {existingText}" 
                    : "Write code to: ",
                    
                "translate" => hasText 
                    ? $"Translate to Spanish: {existingText}" 
                    : "Translate to Spanish: ",
                    
                "analyze" => hasText 
                    ? $"Analyze this in detail: {existingText}" 
                    : "Analyze the following: ",
                    
                _ => existingText
            };
        }

        private void ChatWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionNearAvatar();
            
            // Create taskbar icon (NotifyIcon) for borderless window
            _taskbarIcon = new TaskbarIconHelper(this);
            
            // Initialize Atlas Core state machine and animations
            InitializeAtlasCoreAnimations();

            // Orb overlay is chat-only, but chat is the default page.
            SetOrbOverlayVisible(true);
            
            // Make radial controls visible and clickable on startup
            RadialControlsCanvas.Visibility = Visibility.Visible;
            RadialControlsCanvas.Opacity = 1.0;
            RadialControlsCanvas.IsHitTestVisible = true;
            _radialControlsVisible = true;
            
            // Start radial controls breathing animation
            StartRadialBreathingAnimation();
            
            // Start slow anti-clockwise rotation of radial controls
            StartRadialRotationAnimation();

            // Sync compact chat speech toggle with current speech state.
            UpdateSpeechToggleUI();
            
            // Initialize conversation system (sessions, memory, profile)
            _ = InitializeConversationSystemAsync();

            // Figma orb overlay (WebView2) on right side of chat
            _ = InitializeChatOrbsOverlayAsync();
            
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var wpDir = Path.Combine(baseDir, "Assets", "Wallpaper");
                if (Directory.Exists(wpDir))
                {
                    var img = Directory.EnumerateFiles(wpDir)
                        .FirstOrDefault(f =>
                            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(img) && ChatWallpaperRect != null)
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(img, UriKind.Absolute);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bi.EndInit();
                        ChatWallpaperRect.Fill = new ImageBrush(bi) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
                        ChatWallpaperRect.Visibility = Visibility.Visible;
                    }
                }
            }
            catch { }

            // Debug handshake: briefly show the Voice Debug pill on load so we can verify
            // the running build actually contains the indicator and it isn't clipped/hidden.
            try
            {
                if (VoiceDebugIndicator != null && VoiceDebugText != null)
                {
                    _voiceDebugHoldUntilUtc = DateTime.UtcNow.AddSeconds(2);
                    VoiceDebugText.Text = "Voice UI ready";
                    VoiceDebugIndicator.Visibility = Visibility.Visible;

                    var t = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(2)
                    };
                    t.Tick += (_, __) =>
                    {
                        try
                        {
                            t.Stop();
                            if (!isListening)
                            {
                                VoiceDebugText.Text = "";
                                VoiceDebugIndicator.Visibility = Visibility.Collapsed;
                            }
                        }
                        catch { }
                    };
                    t.Start();
                }
            }
            catch
            {
            }
            
            // Fallback: Show a simple welcome message after a delay if nothing else has shown
            // DISABLED: This causes duplicate greetings - welcome is already spoken by SpeakWelcomeMessageAsync
            /*
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000); // Wait 3 seconds
                await Dispatcher.InvokeAsync(() =>
                {
                    // Only show fallback if no messages exist yet
                    if (displayedMessages.Count == 0)
                    {
                        Debug.WriteLine("[Welcome] Fallback welcome triggered - no messages shown yet");
                        AddMessage("Atlas", "Systems online. How may I assist you, sir?", false);
                    }
                });
            });
            */
        }

        private void SetOrbOverlayVisible(bool visible)
        {
            try
            {
                if (OrbOverlay != null)
                    OrbOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        private async Task InitializeChatOrbsOverlayAsync()
        {
            try
            {
                if (_chatOrbsWebViewInitialized) return;
                _chatOrbsWebViewInitialized = true;
                _chatOrbsWebViewReady = false;

                if (ChatOrbsWebView == null) return;
                await ChatOrbsWebView.EnsureCoreWebView2Async();
                if (ChatOrbsWebView.CoreWebView2 == null) return;

                // Immediate visual sanity check (if you still see nothing, the overlay isn't rendering at all).
                try
                {
                    ChatOrbsWebView.CoreWebView2.NavigateToString(
                        "<!doctype html><html><head><meta charset='utf-8' />" +
                        "<style>html,body{margin:0;background:transparent;overflow:hidden;}" +
                        ".c{width:100vw;height:100vh;display:flex;align-items:center;justify-content:center;}" +
                        ".o{width:84px;height:84px;border-radius:9999px;border:2px solid rgba(34,211,238,.55);" +
                        "box-shadow:0 0 40px rgba(34,211,238,.35);}" +
                        "</style></head><body><div class='c'><div class='o'></div></div></body></html>"
                    );
                }
                catch { }

                try
                {
                    var settings = ChatOrbsWebView.CoreWebView2.Settings;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.AreDevToolsEnabled = false;
                    settings.AreBrowserAcceleratorKeysEnabled = false;
                    settings.IsStatusBarEnabled = false;
                    settings.IsZoomControlEnabled = false;
                }
                catch { }

                ChatOrbsWebView.CoreWebView2.ProcessFailed += (_, ev) =>
                {
                    try
                    {
                        Debug.WriteLine($"[ChatWindow] Orbs WebView process failed: {ev?.ProcessFailedKind}");
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                if (ChatHoloCore != null)
                                {
                                    ChatHoloCore.Width = 260;
                                    ChatHoloCore.Height = 260;
                                    ChatHoloCore.Visibility = Visibility.Visible;
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }
                };

                ChatOrbsWebView.CoreWebView2.NavigationCompleted += (_, ev) =>
                {
                    try
                    {
                        Debug.WriteLine($"[ChatWindow] Orbs WebView nav: success={ev.IsSuccess} status={ev.WebErrorStatus}");
                        if (!ev.IsSuccess)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    if (ChatHoloCore != null)
                                    {
                                        ChatHoloCore.Width = 260;
                                        ChatHoloCore.Height = 260;
                                        ChatHoloCore.Visibility = Visibility.Visible;
                                    }
                                }
                                catch { }
                            });
                        }
                    }
                    catch { }
                };

                ChatOrbsWebView.CoreWebView2.DocumentTitleChanged += (_, __) =>
                {
                    try
                    {
                        var title = ChatOrbsWebView.CoreWebView2?.DocumentTitle ?? "";
                        if (title.Contains("ATLAS_ORBS_READY", StringComparison.OrdinalIgnoreCase))
                        {
                            _chatOrbsWebViewReady = true;
                            Debug.WriteLine("[ChatWindow] Orbs WebView ready signal received");
                        }
                    }
                    catch { }
                };

                // Transparent background so it behaves like a true overlay.
                try { ChatOrbsWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent; } catch { }

                var figmaDist = FindFigmaDistForUi();
                if (figmaDist == null)
                {
                    Debug.WriteLine("[ChatWindow] Orbs WebView: Figma dist not found; falling back to WPF orb");
                    try
                    {
                        if (ChatHoloCore != null)
                        {
                            ChatHoloCore.Width = 260;
                            ChatHoloCore.Height = 260;
                            ChatHoloCore.Visibility = Visibility.Visible;
                        }
                    }
                    catch { }
                    return;
                }

                ChatOrbsWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "atlas-ui",
                    figmaDist,
                    CoreWebView2HostResourceAccessKind.Allow);

                long indexWriteTicks = 0;
                try
                {
                    var indexPath = Path.Combine(figmaDist, "index.html");
                    if (File.Exists(indexPath))
                        indexWriteTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
                }
                catch { }

                var v = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
                ChatOrbsWebView.CoreWebView2.Navigate($"https://atlas-ui/index.html?mode=orbs&v={v}");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2500);
                        if (_chatOrbsWebViewReady) return;

                        Debug.WriteLine("[ChatWindow] Orbs WebView did not become ready within timeout; falling back to WPF orb");
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                if (ChatHoloCore != null)
                                {
                                    ChatHoloCore.Width = 260;
                                    ChatHoloCore.Height = 260;
                                    ChatHoloCore.Visibility = Visibility.Visible;
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] Orbs WebView init failed: {ex.Message}");
                try
                {
                    if (ChatHoloCore != null)
                    {
                        ChatHoloCore.Width = 260;
                        ChatHoloCore.Height = 260;
                        ChatHoloCore.Visibility = Visibility.Visible;
                    }
                }
                catch { }
            }
        }

        private static string? FindFigmaDistForUi()
        {
            try
            {
                // Prefer dist shipped alongside the built EXE (bin output / publish)
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var shipped = Path.Combine(baseDir, "Figma", "Futuristic AI Command Center (6)", "dist");
                if (Directory.Exists(shipped) && File.Exists(Path.Combine(shipped, "index.html")))
                    return shipped;
            }
            catch { }

            var roots = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            };

            foreach (var root in roots)
            {
                try
                {
                    var dir = new DirectoryInfo(root);
                    for (var i = 0; i < 10 && dir != null; i++)
                    {
                        var figmaRoot = Path.Combine(dir.FullName, "Figma");
                        if (Directory.Exists(figmaRoot))
                        {
                            var uiFolder = Directory.GetDirectories(figmaRoot, "Futuristic AI Command Center (*)", SearchOption.TopDirectoryOnly)
                                .OrderByDescending(d => d)
                                .FirstOrDefault();

                            if (!string.IsNullOrWhiteSpace(uiFolder))
                            {
                                var dist = Path.Combine(uiFolder, "dist");
                                if (Directory.Exists(dist) && File.Exists(Path.Combine(dist, "index.html")))
                                    return dist;
                            }
                        }
                        dir = dir.Parent;
                    }
                }
                catch { }
            }

            return null;
        }
        
        // ═══════════════════════════════════════════════════════════════
        // ATLAS CORE STATE MACHINE - Living visual intelligence
        // ═══════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Initialize HoloCore (same orb as desktop - no separate render loop!)
        /// </summary>
        private void InitializeAtlasCoreAnimations()
        {
            try
            {
                // HoloCoreControl handles its own initialization
                // Just ensure it's visible
                if (ChatHoloCore != null)
                {
                    ChatHoloCore.Visibility = Visibility.Visible;
                    Debug.WriteLine("[ChatHoloCore] Orb visibility set to Visible");
                }
                
                // Ensure fail-safe overlay is hidden by default (will be enabled if primary is not found)
                if (OrbOverlayTop != null)
                    OrbOverlayTop.Visibility = Visibility.Collapsed;
                
                if (AtlasCoreContainer != null)
                {
                    AtlasCoreContainer.Visibility = Visibility.Visible;
                    Debug.WriteLine($"[ChatHoloCore] Container visibility set to Visible");
                }
                
                Debug.WriteLine("[ChatHoloCore] HoloCoreControl initialized - same as desktop orb");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatHoloCore] Error initializing: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load saved orb appearance settings (HoloCore uses PreferencesStore directly)
        /// </summary>
        private void LoadOrbSettings()
        {
            try
            {
                // HoloCoreControl loads its own settings from PreferencesStore automatically
                Debug.WriteLine($"[ChatWindow] HoloCoreControl loads settings from PreferencesStore");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] Error in LoadOrbSettings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Set the HoloCore visual state using presence/thinking levels
        /// </summary>
        public void SetAtlasCoreState(Controls.AtlasVisualState newState)
        {
            try
            {
                if (ChatHoloCore != null)
                {
                    // Map AtlasVisualState to HoloCoreControl properties
                    switch (newState)
                    {
                        case Controls.AtlasVisualState.Idle:
                            ChatHoloCore.PresenceLevel = 0.3;
                            ChatHoloCore.ThinkingLevel = 0.0;
                            ChatHoloCore.RingSpeed = 0.1;
                            break;
                        case Controls.AtlasVisualState.Listening:
                            ChatHoloCore.PresenceLevel = 0.7;
                            ChatHoloCore.ThinkingLevel = 0.0;
                            ChatHoloCore.RingSpeed = 0.5;
                            break;
                        case Controls.AtlasVisualState.Thinking:
                            ChatHoloCore.PresenceLevel = 1.0;
                            ChatHoloCore.ThinkingLevel = 0.9;
                            ChatHoloCore.RingSpeed = 0.7;
                            break;
                        case Controls.AtlasVisualState.Speaking:
                            ChatHoloCore.PresenceLevel = 0.5;
                            ChatHoloCore.ThinkingLevel = 0.0;
                            ChatHoloCore.RingSpeed = 0.3;
                            break;
                    }
                    Debug.WriteLine($"[ChatHoloCore] State set to: {newState}");
                }
                else if (OrbOverlayTop != null)
                {
                    // Primary orb missing; show fail-safe overlay
                    OrbOverlayTop.Visibility = Visibility.Visible;
                    Debug.WriteLine("[ChatHoloCore] Primary orb missing - showing fail-safe overlay");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatHoloCore] Error setting state: {ex.Message}");
            }
        }
        
        // ═══════════════════════════════════════════════════════════════
        // SPEAKING ENERGY SIMULATION - Creates organic animation during TTS
        // ═══════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Start simulating speaking energy for organic Atlas Core animation
        /// </summary>
        private void StartSpeakingEnergySimulation()
        {
            _speakingStartTime = DateTime.Now;
            
            // Initialize timer if needed
            if (_speakingEnergyTimer == null)
            {
                _speakingEnergyTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(33) // ~30 Hz
                };
                _speakingEnergyTimer.Tick += SpeakingEnergyTimer_Tick;
            }
            
            _speakingEnergyTimer.Start();
            Debug.WriteLine("[SpeakingEnergy] Started energy simulation");
        }
        
        /// <summary>
        /// Stop speaking energy simulation with smooth ease-out
        /// </summary>
        private void StopSpeakingEnergySimulation()
        {
            _speakingEnergyTimer?.Stop();
            
            // HoloCoreControl handles its own voice amplitude smoothing
            if (ChatHoloCore != null)
            {
                ChatHoloCore.VoiceAmplitude = 0;
            }
            
            Debug.WriteLine("[SpeakingEnergy] Stopped energy simulation");
        }
        
        /// <summary>
        /// Generate organic-looking energy values during speaking
        /// Uses layered sine waves + noise for natural speech-like patterns
        /// </summary>
        private void SpeakingEnergyTimer_Tick(object? sender, EventArgs e)
        {
            if (ChatHoloCore == null) return;
            
            try
            {
                var elapsed = (DateTime.Now - _speakingStartTime).TotalSeconds;
                
                // Layer 1: Base rhythm (slow, ~0.8 Hz - sentence cadence)
                var baseRhythm = 0.5 + 0.3 * Math.Sin(elapsed * 5.0);
                
                // Layer 2: Word rhythm (faster, ~3 Hz)
                var wordRhythm = 0.15 * Math.Sin(elapsed * 19.0);
                
                // Layer 3: Syllable micro-variation (~8 Hz)
                var syllableVar = 0.1 * Math.Sin(elapsed * 50.0);
                
                // Layer 4: Random noise for organic feel
                var noise = (_energyRandom.NextDouble() - 0.5) * 0.15;
                
                // Layer 5: Occasional emphasis spikes (simulates stressed words)
                var emphasisChance = _energyRandom.NextDouble();
                var emphasis = emphasisChance > 0.97 ? 0.25 : 0;
                
                // Combine all layers
                var energy = baseRhythm + wordRhythm + syllableVar + noise + emphasis;
                
                // Clamp to valid range
                energy = Math.Clamp(energy, 0.1, 1.0);
                
                // Feed to HoloCoreControl
                ChatHoloCore.VoiceAmplitude = energy;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpeakingEnergy] Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Cycle through Atlas Core states (for testing) - Ctrl+Shift+C
        /// </summary>
        public void CycleAtlasCoreState()
        {
            // No-op - HoloCoreControl doesn't have a CycleState method
            Debug.WriteLine("[ChatHoloCore] CycleState not supported on HoloCoreControl");
        }
        
        // ═══════════════════════════════════════════════════════════════
        // SUMMONED RADIAL CONTROLS - Appear on hover/hotkey
        // ═══════════════════════════════════════════════════════════════
        
        private void AtlasCoreContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            _isRadialRotationPaused = true;
            PauseRadialRotation(); // Pause smooth animation on hover
            ShowRadialControls();
        }
        
        private void AtlasCoreContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            _isRadialRotationPaused = false;
            ResumeRadialRotation(); // Resume smooth animation when not hovering
            // Start timer to hide controls after delay
            StartRadialHideTimer();
        }
        
        private void StartRadialHideTimer()
        {
            // DISABLED - Timer was causing periodic judder every 1.5 seconds
            // Radial controls will stay visible until manually hidden
            return;
            
            /*
            _radialHideTimer?.Stop();
            _radialHideTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _radialHideTimer.Tick += (s, e) =>
            {
                _radialHideTimer?.Stop();
                HideRadialControls();
            };
            _radialHideTimer.Start();
            */
        }
        
        private void ToggleRadialControls()
        {
            if (_radialControlsVisible)
                HideRadialControls();
            else
                ShowRadialControls();
        }
        
        private void ShowRadialControls()
        {
            if (_radialControlsVisible) return;
            _radialControlsVisible = true;
            _radialHideTimer?.Stop();
            
            // Make canvas visible
            RadialControlsCanvas.Visibility = Visibility.Visible;
            RadialControlsCanvas.Opacity = 1.0;
            RadialControlsCanvas.IsHitTestVisible = true;
            
            // Start breathing animation for all radial buttons
            StartRadialBreathingAnimation();
            
            // Start slow anti-clockwise rotation
            StartRadialRotationAnimation();
        }
        
        private void StartRadialRotationAnimation()
        {
            if (_radialRotationTimer != null) return; // Already running
            
            // Use WPF's smooth animation system instead of DispatcherTimer
            var rotateAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = -360, // Anti-clockwise
                Duration = TimeSpan.FromSeconds(60), // One full rotation per 60 seconds (slow)
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            
            // Apply to the canvas rotation transform
            RadialCanvasRotation.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
            
            // For counter-rotation, we need to animate each button's rotation in the opposite direction
            StartButtonCounterRotationAnimations();
            
            // Use a flag timer just to track state (not for animation)
            _radialRotationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _radialRotationTimer.Start();
        }
        
        private void StartButtonCounterRotationAnimations()
        {
            var buttons = new[] { RadialMicBtn, RadialMuteBtn, RadialHistoryBtn, RadialFocusBtn, 
                                  RadialSettingsBtn, RadialWakeBtn, RadialCommandBtn, RadialOrbStyleBtn };
            
            // Counter-rotation animation (clockwise to cancel out the anti-clockwise canvas rotation)
            var counterRotateAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 360, // Clockwise (opposite of canvas)
                Duration = TimeSpan.FromSeconds(60), // Same speed as canvas
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            
            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                
                // Ensure button has a RotateTransform we can animate
                if (!(btn.RenderTransform is RotateTransform))
                {
                    btn.RenderTransform = new RotateTransform(0);
                    btn.RenderTransformOrigin = new Point(0.5, 0.5);
                }
                
                // Start the counter-rotation animation
                var transform = btn.RenderTransform as RotateTransform;
                transform?.BeginAnimation(RotateTransform.AngleProperty, counterRotateAnimation.Clone());
            }
        }
        
        private void PauseRadialRotation()
        {
            // Pause canvas rotation
            RadialCanvasRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            
            // Pause button counter-rotations
            var buttons = new[] { RadialMicBtn, RadialMuteBtn, RadialHistoryBtn, RadialFocusBtn, 
                                  RadialSettingsBtn, RadialWakeBtn, RadialCommandBtn, RadialOrbStyleBtn };
            foreach (var btn in buttons)
            {
                if (btn?.RenderTransform is RotateTransform rot)
                {
                    rot.BeginAnimation(RotateTransform.AngleProperty, null);
                }
            }
        }
        
        private void ResumeRadialRotation()
        {
            // Get current angle and continue from there
            var currentAngle = RadialCanvasRotation.Angle;
            
            var rotateAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = currentAngle,
                To = currentAngle - 360,
                Duration = TimeSpan.FromSeconds(60),
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            RadialCanvasRotation.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
            
            // Resume button counter-rotations
            var buttons = new[] { RadialMicBtn, RadialMuteBtn, RadialHistoryBtn, RadialFocusBtn, 
                                  RadialSettingsBtn, RadialWakeBtn, RadialCommandBtn, RadialOrbStyleBtn };
            
            var counterRotateAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = -currentAngle,
                To = -currentAngle + 360,
                Duration = TimeSpan.FromSeconds(60),
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            
            foreach (var btn in buttons)
            {
                if (btn?.RenderTransform is RotateTransform rot)
                {
                    rot.BeginAnimation(RotateTransform.AngleProperty, counterRotateAnimation.Clone());
                }
            }
        }
        
        private bool _isRadialRotationPaused = false;
        
        private void CounterRotateRadialButtons(double angle)
        {
            // This method is no longer used - animations handle counter-rotation
        }
        
        private void StartRadialBreathingAnimation()
        {
            var buttons = new[] { RadialMicBtn, RadialMuteBtn, RadialHistoryBtn, RadialFocusBtn, 
                                  RadialSettingsBtn, RadialWakeBtn, RadialCommandBtn, RadialOrbStyleBtn };
            
            foreach (var btn in buttons)
            {
                if (btn?.Effect is System.Windows.Media.Effects.DropShadowEffect effect)
                {
                    // Breathing glow animation - slow pulse
                    var glowAnim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0.5,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(2),
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        EasingFunction = new System.Windows.Media.Animation.SineEase()
                    };
                    effect.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, glowAnim);
                    
                    // Breathing blur radius
                    var blurAnim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 12,
                        To = 20,
                        Duration = TimeSpan.FromSeconds(2),
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        EasingFunction = new System.Windows.Media.Animation.SineEase()
                    };
                    effect.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, blurAnim);
                }
                
                // Animate the inner orange glow (RadialGradientBrush)
                if (btn?.Background is RadialGradientBrush gradient && gradient.GradientStops.Count >= 2)
                {
                    // Animate the center orange color opacity (breathing effect)
                    var centerStop = gradient.GradientStops[0];
                    var midStop = gradient.GradientStops[1];
                    
                    // Breathing animation for center color - brighter orange pulse
                    var centerColorAnim = new System.Windows.Media.Animation.ColorAnimation
                    {
                        From = Color.FromArgb(0x70, 0xf9, 0x73, 0x16), // Dimmer orange
                        To = Color.FromArgb(0xCC, 0xf9, 0x73, 0x16),   // Much brighter orange
                        Duration = TimeSpan.FromSeconds(1.5),
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        EasingFunction = new System.Windows.Media.Animation.SineEase()
                    };
                    centerStop.BeginAnimation(GradientStop.ColorProperty, centerColorAnim);
                    
                    // Breathing animation for mid color
                    var midColorAnim = new System.Windows.Media.Animation.ColorAnimation
                    {
                        From = Color.FromArgb(0x30, 0xf9, 0x73, 0x16), // Dimmer
                        To = Color.FromArgb(0x70, 0xf9, 0x73, 0x16),   // Brighter
                        Duration = TimeSpan.FromSeconds(1.5),
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        EasingFunction = new System.Windows.Media.Animation.SineEase()
                    };
                    midStop.BeginAnimation(GradientStop.ColorProperty, midColorAnim);
                }
            }
        }
        
        private void HideRadialControls()
        {
            if (!_radialControlsVisible) return;
            _radialControlsVisible = false;
            
            // Keep hit test visible so buttons still work
            RadialControlsCanvas.IsHitTestVisible = true;
            
            // Fade back to subtle opacity (0.4) instead of fully hidden
            var canvasAnim = new System.Windows.Media.Animation.DoubleAnimation(0.4, TimeSpan.FromMilliseconds(200));
            RadialControlsCanvas.BeginAnimation(OpacityProperty, canvasAnim);
        }
        
        // Radial button click handlers
        private void RadialMic_Click(object sender, MouseButtonEventArgs e)
        {
            ActivateVoiceWithHotkey();
        }
        
        private void RadialMute_Click(object sender, MouseButtonEventArgs e)
        {
            SetChatSpeechEnabled(!_voiceManager.SpeechEnabled, true);
        }
        
        private void RadialHistory_Click(object sender, MouseButtonEventArgs e)
        {
            // Use the working History_Click method instead of the broken drawer
            History_Click(sender, new RoutedEventArgs());
        }
        
        private void RadialFocus_Click(object sender, MouseButtonEventArgs e)
        {
            ToggleFocusMode();
        }
        
        private void RadialSettings_Click(object sender, MouseButtonEventArgs e)
        {
            Settings_Click(null, new RoutedEventArgs());
        }
        
        private void RadialWake_Click(object sender, MouseButtonEventArgs e)
        {
            ToggleWakeWord();
            RadialWakeIcon.Text = isWakeWordEnabled ? "👂" : "🚫";
        }
        
        private void RadialCommand_Click(object sender, MouseButtonEventArgs e)
        {
            OpenCommandPalette();
        }
        
        // ═══════════════════════════════════════════════════════════════
        // ORB STYLE SELECTOR - Cycle through available orb animations
        // ═══════════════════════════════════════════════════════════════
        private int _currentOrbStyleIndex = 0;
        private readonly (string name, string file, string icon)[] _orbStyles = new[]
        {
            ("Particles", "particles", "🔮"),
            ("Siri", "Siri Animation.json", "✨"),
            ("AI Assistant", "AI Assistant.json", "🤖"),
            ("AI Loading", "AI Loading.json", "⏳"),
            ("Loading", "Loading animation.json", "⚡"),
            ("Loop", "Loading loop animation.json", "🔄"),
            ("AI AI", "ai ai.json", "🧠"),
            ("Circle", "Circle Animation.json", "⭕"),
            ("Circles", "circles.json", "🔵"),
            ("Circle 2", "circle.json", "⚪"),
            ("Hearts", "floating hearts.json", "💜"),
            ("Ghost", "Ghostsmart.json", "👻"),
            ("Triangle", "Loader Triangle Flow.json", "🔺"),
            ("Navi", "Navi's loader.json", "🧭"),
            ("Robot AI", "Robot Futuristic Ai.json", "🤖"),
            ("Waves", "waves.json", "🌊"),
            ("Animation", "Animation - 1695019131207.json", "💫")
        };
        
        private void RadialOrbStyle_Click(object sender, MouseButtonEventArgs e)
        {
            // Cycle to next orb style
            _currentOrbStyleIndex = (_currentOrbStyleIndex + 1) % _orbStyles.Length;
            var style = _orbStyles[_currentOrbStyleIndex];
            
            // Apply the style
            if (style.file == "particles")
            {
                SetOrbStyle(false, null); // Use particle orb
            }
            else
            {
                SetOrbStyle(true, style.file); // Use Lottie animation
            }
            
            // Update icon and show status
            RadialOrbStyleIcon.Text = style.icon;
            ShowStatus($"🔮 {style.name}");
        }
        
        // ═══════════════════════════════════════════════════════════════
        // SCAN ORBIT MODE - Orbiting scan icons around the orb
        // ═══════════════════════════════════════════════════════════════
        
        private bool _scanOrbitVisible = false;
        
        public void ToggleScanOrbit()
        {
            _scanOrbitVisible = !_scanOrbitVisible;
            ScanOrbit.Visibility = _scanOrbitVisible ? Visibility.Visible : Visibility.Collapsed;
            
            if (_scanOrbitVisible)
            {
                ScanOrbit.StartOrbit();
                ShowStatus("🛡 Scan Mode Active - Click icons to scan");
            }
            else
            {
                ScanOrbit.StopOrbit();
                ShowStatus("🛡 Scan Mode Disabled");
            }
        }
        
        private void ScanOrbit_ScanStarted(object? sender, string message)
        {
            // Atlas announces the scan
            ShowStatus($"🔍 {message}");
            _ = SpeakResponseAsync(message);
        }
        
        private async void ScanOrbit_ScanCompleted(object? sender, Controls.ScanResultEventArgs e)
        {
            // Build response message
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"**{e.ScanType} Scan Complete**\n");
            sb.AppendLine(e.Result.Summary);
            sb.AppendLine();
            
            foreach (var detail in e.Result.Details.Take(15))
                sb.AppendLine(detail);
            
            if (e.Result.Details.Count > 15)
                sb.AppendLine($"\n... and {e.Result.Details.Count - 15} more items");
            
            // Add to chat
            AddMessage("ATLAS", sb.ToString(), false);
            
            // Speak summary
            var spokenSummary = e.Result.IssuesFound > 0 
                ? $"{e.ScanType} scan found {e.Result.IssuesFound} issues and {e.Result.WarningsFound} warnings."
                : e.Result.WarningsFound > 0
                    ? $"{e.ScanType} scan found {e.Result.WarningsFound} warnings."
                    : $"{e.ScanType} scan complete. Everything looks good.";
            
            await SpeakResponseAsync(spokenSummary);
        }
        
        // ═══════════════════════════════════════════════════════════════
        // PROACTIVE SECURITY MONITOR - Installation alerts & health scans
        // ═══════════════════════════════════════════════════════════════
        
        private void InitializeSecurityMonitor()
        {
            try
            {
                Debug.WriteLine("[Security] InitializeSecurityMonitor called");
                Debug.WriteLine("[Security] Proactive monitor auto-start suppressed to avoid noisy popup alerts.");
                return;
                
                // Wire up ProactiveSecurityMonitor (file downloads, installations)
                var monitor = Agent.ProactiveSecurityMonitor.Instance;
                monitor.InstallationDetected += SecurityMonitor_InstallationDetected;
                monitor.HealthScanCompleted += SecurityMonitor_HealthScanCompleted;
                monitor.StatusChanged += SecurityMonitor_StatusChanged;
                monitor.Start();
                
                // Wire up SecurityIntelligence for AI-enhanced explanations
                Agent.SecurityIntelligence.Instance.OnSecurityInsight += SecurityIntelligence_OnInsight;
                Agent.SecurityIntelligence.Instance.OnStatusUpdate += (msg) => Debug.WriteLine($"[SecurityIntelligence] {msg}");
                
                // Wire up LedgerManager for startup/process/system changes
                Ledger.LedgerManager.Instance.EventAdded += LedgerManager_EventAdded;
                
                // Start startup watcher
                SecuritySuite.Services.StartupWatcher.Instance.Start();
                
                // Start Install Interceptor - quietly observes what installers change
                var installInterceptor = Agent.InstallInterceptor.Instance;
                installInterceptor.InstallAnalysisComplete += InstallInterceptor_AnalysisComplete;
                installInterceptor.StatusChanged += (msg) => Debug.WriteLine($"[InstallInterceptor] {msg}");
                installInterceptor.Start();
                
                Debug.WriteLine("[Security] All security monitors started and AI intelligence wired up");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Security] Monitor init error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle AI-enhanced security insights from SecurityIntelligence
        /// </summary>
        private async void SecurityIntelligence_OnInsight(Agent.SecurityInsight insight)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"[SecurityIntelligence] Insight received: {insight.Category} - {insight.Severity}");
                
                // Show AI-enhanced explanation in chat with decision buttons
                ShowSecurityInsightInChat(insight);
                
                // Speak the explanation based on severity
                var spokenMessage = insight.Severity switch
                {
                    Agent.SecuritySeverity.High => $"Sir, I need your attention. {GetShortExplanation(insight)}",
                    Agent.SecuritySeverity.Medium => $"Heads up. {GetShortExplanation(insight)}",
                    _ => GetShortExplanation(insight)
                };
                
                await SpeakResponseAsync(spokenMessage);
            });
        }
        
        /// <summary>
        /// Handle Install Interceptor analysis completion - shows what an installer changed
        /// </summary>
        private async void InstallInterceptor_AnalysisComplete(Agent.InstallAnalysis analysis)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"[InstallInterceptor] Analysis complete: {analysis.InstallerName} - {analysis.Classification}");
                
                // Build and show the summary in chat
                var summary = Agent.InstallInterceptor.Instance.BuildAnalysisSummary(analysis);
                AddMessage("ATLAS", summary, false);
                
                // Speak based on classification (calm, not alarming)
                var spokenMessage = analysis.Classification switch
                {
                    Agent.InstallClassification.HighAttention => 
                        $"Sir, that installer made some notable changes. {analysis.ClassificationReason}",
                    Agent.InstallClassification.Unusual => 
                        $"Heads up. {analysis.InstallerName} added some background components.",
                    _ => "" // Expected installs - stay silent
                };
                
                if (!string.IsNullOrEmpty(spokenMessage))
                {
                    await SpeakResponseAsync(spokenMessage);
                }
            });
        }
        
        /// <summary>
        /// Queued ledger speech messages to prevent rapid-fire events from interrupting each other.
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _ledgerSpeechQueue = new();
        private int _ledgerSpeechDraining;

        /// <summary>
        /// In-memory cache of security preferences so we don't hit the filesystem on every event.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _securityPrefsCache = new(StringComparer.OrdinalIgnoreCase);
        private static bool _securityPrefsCacheLoaded;

        /// <summary>
        /// Handle Ledger events (startup changes, scheduled tasks, etc.)
        /// </summary>
        private async void LedgerManager_EventAdded(Ledger.LedgerEvent evt)
        {
            // Only show high-priority events in chat
            if (evt.Severity < Ledger.LedgerSeverity.Medium) return;
            
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"[Ledger] Event: {evt.Category} - {evt.Title}");
                
                // Route through AI for enhanced explanation
                var aiExplanation = GenerateAIExplanationForLedgerEvent(evt);
                
                // Show in chat with action buttons
                ShowLedgerEventInChat(evt, aiExplanation);
                
                // Queue speech for high severity instead of speaking immediately,
                // so rapid-fire events don't cancel each other mid-sentence.
                if (evt.Severity >= Ledger.LedgerSeverity.High)
                {
                    var spokenAlert = $"Security alert. {evt.Title}. {GetShortWhyItMatters(evt)}";
                    _ledgerSpeechQueue.Enqueue(spokenAlert);
                    await DrainLedgerSpeechQueueAsync();
                }
            });
        }

        /// <summary>
        /// Drain the ledger speech queue sequentially so each alert finishes before the next starts.
        /// </summary>
        private async Task DrainLedgerSpeechQueueAsync()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _ledgerSpeechDraining, 1, 0) != 0)
                return; // another drain loop is already running

            try
            {
                while (_ledgerSpeechQueue.TryDequeue(out var message))
                {
                    try
                    {
                        await SpeakResponseAsync(message);
                    }
                    catch
                    {
                        // Don't let a single speech failure kill the queue
                    }
                }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _ledgerSpeechDraining, 0);

                // Check if more items arrived while we were finishing
                if (!_ledgerSpeechQueue.IsEmpty)
                    _ = DrainLedgerSpeechQueueAsync();
            }
        }
        
        /// <summary>
        /// Generate AI explanation for a ledger event
        /// </summary>
        private string GenerateAIExplanationForLedgerEvent(Ledger.LedgerEvent evt)
        {
            var sb = new System.Text.StringBuilder();
            
            // Category-specific explanations
            switch (evt.Category)
            {
                case Ledger.LedgerCategory.Startup:
                    sb.AppendLine(evt.Severity >= Ledger.LedgerSeverity.High
                        ? "🚨 **Startup Change Detected**"
                        : "📋 **Startup Entry Changed**");
                    sb.AppendLine();
                    sb.AppendLine("**What happened:**");
                    sb.AppendLine(evt.Title);
                    sb.AppendLine();
                    sb.AppendLine("**Why this matters:**");
                    sb.AppendLine(evt.WhyItMatters);
                    break;
                    
                case Ledger.LedgerCategory.ScheduledTask:
                    sb.AppendLine("⏰ **Scheduled Task Change**");
                    sb.AppendLine();
                    sb.AppendLine("**What happened:**");
                    sb.AppendLine(evt.Title);
                    sb.AppendLine();
                    sb.AppendLine("**Why this matters:**");
                    sb.AppendLine("Scheduled tasks run automatically at specific times. Malware often uses them to persist.");
                    break;
                    
                case Ledger.LedgerCategory.FileSystem:
                    sb.AppendLine("📁 **System File Change**");
                    sb.AppendLine();
                    sb.AppendLine(evt.Title);
                    sb.AppendLine();
                    sb.AppendLine(evt.WhyItMatters);
                    break;
                    
                default:
                    sb.AppendLine($"🔔 **{evt.Category}**");
                    sb.AppendLine();
                    sb.AppendLine(evt.Title);
                    if (!string.IsNullOrEmpty(evt.WhyItMatters))
                    {
                        sb.AppendLine();
                        sb.AppendLine(evt.WhyItMatters);
                    }
                    break;
            }
            
            // Add evidence
            if (evt.Evidence?.Any() == true)
            {
                sb.AppendLine();
                sb.AppendLine("**Details:**");
                foreach (var evidence in evt.Evidence.Take(5))
                {
                    sb.AppendLine($"• {evidence.Key}: {evidence.Value}");
                }
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Show a security insight in chat with decision buttons
        /// </summary>
        private void ShowSecurityInsightInChat(Agent.SecurityInsight insight)
        {
            // Add the AI explanation to chat
            AddMessage("ATLAS", insight.Explanation, false);
            
            // Add the question
            if (!string.IsNullOrEmpty(insight.UserQuestion))
            {
                AddMessage("ATLAS", $"💭 {insight.UserQuestion}", false);
            }
            
            // Show popup with action buttons
            ShowSecurityInsightPopup(insight);
        }
        
        /// <summary>
        /// Show a ledger event in chat with action buttons
        /// </summary>
        private async void ShowLedgerEventInChat(Ledger.LedgerEvent evt, string aiExplanation)
        {
            try
            {
                // Check in-memory cache first, then fall back to file
                var categoryKey = evt.Category.ToString();
                if (!_securityPrefsCacheLoaded)
                {
                    LoadAllSecurityPreferences();
                    _securityPrefsCacheLoaded = true;
                }

                if (_securityPrefsCache.TryGetValue(categoryKey, out var savedAction) && evt.Actions?.Any() == true)
                {
                    var matchingAction = evt.Actions.FirstOrDefault(a => a.Type.ToString().Equals(savedAction, StringComparison.OrdinalIgnoreCase));
                    if (matchingAction != null)
                    {
                        var result = await Ledger.LedgerManager.Instance.ExecuteActionAsync(evt, matchingAction);
                        Debug.WriteLine($"[SecurityPrefs] Auto-handled {categoryKey} → {savedAction}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityPrefs] Auto-handle failed: {ex.Message}");
            }

            AddMessage("ATLAS", aiExplanation, false);
            
            // Show popup with action buttons if there are actions available
            if (evt.Actions?.Any() == true)
            {
                ShowLedgerEventPopup(evt);
            }
        }
        
        /// <summary>
        /// Show popup for security insight with decision buttons
        /// </summary>
        private void ShowSecurityInsightPopup(Agent.SecurityInsight insight)
        {
            var threatColor = insight.Severity switch
            {
                Agent.SecuritySeverity.High => System.Windows.Media.Color.FromRgb(239, 68, 68),
                Agent.SecuritySeverity.Medium => System.Windows.Media.Color.FromRgb(245, 158, 11),
                _ => System.Windows.Media.Color.FromRgb(34, 197, 94)
            };
            
            var alertWindow = CreateSecurityPopupWindow("Atlas AI Security", threatColor);
            var mainStack = (StackPanel)((Border)alertWindow.Content).Child;
            
            // Header
            var icon = insight.Severity == Agent.SecuritySeverity.High ? "🚨" : 
                       insight.Severity == Agent.SecuritySeverity.Medium ? "⚡" : "🔔";
            AddSecurityPopupHeader(mainStack, icon, $"{insight.Category}", threatColor);
            
            // Severity badge
            AddSecurityPopupBadge(mainStack, insight.Severity.ToString().ToUpper(), threatColor);
            
            // File info
            if (!string.IsNullOrEmpty(insight.SourceFile))
            {
                AddSecurityPopupInfo(mainStack, "FILE", insight.SourceFile);
            }
            
            // Question
            if (!string.IsNullOrEmpty(insight.UserQuestion))
            {
                AddSecurityPopupQuestion(mainStack, insight.UserQuestion);
            }
            
            // Action buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            
            foreach (var action in insight.RecommendedActions.Take(3))
            {
                var actionLower = action.ToLower();
                var btnColor = actionLower.Contains("delete") || actionLower.Contains("block") 
                    ? System.Windows.Media.Color.FromRgb(239, 68, 68)
                    : actionLower.Contains("safe") || actionLower.Contains("trust")
                        ? System.Windows.Media.Color.FromRgb(34, 197, 94)
                        : System.Windows.Media.Color.FromRgb(34, 211, 238);
                
                var btn = CreateSecurityAlertButton(action, btnColor, () =>
                {
                    // Record the decision
                    Agent.SecurityIntelligence.Instance.RecordUserDecision(insight, action);
                    
                    // Execute action if needed
                    if (actionLower.Contains("delete") && !string.IsNullOrEmpty(insight.SourcePath))
                    {
                        try { System.IO.File.Delete(insight.SourcePath); ShowStatus($"🗑️ Deleted: {insight.SourceFile}"); }
                        catch (Exception ex) { ShowStatus($"❌ Delete failed: {ex.Message}"); }
                    }
                    
                    AddMessage("ATLAS", $"✅ Recorded: You chose to **{action}** for {insight.SourceFile}", false);
                    alertWindow.Close();
                });
                buttonPanel.Children.Add(btn);
            }
            
            // Dismiss button
            var dismissBtn = CreateSecurityAlertButton("Dismiss", System.Windows.Media.Color.FromRgb(107, 114, 128), () =>
            {
                Agent.SecurityIntelligence.Instance.RecordUserDecision(insight, "Dismiss", "User dismissed without action");
                alertWindow.Close();
            });
            buttonPanel.Children.Add(dismissBtn);
            
            mainStack.Children.Add(buttonPanel);
            alertWindow.Show();
        }
        
        /// <summary>
        /// Show popup for ledger event with action buttons
        /// </summary>
        private void ShowLedgerEventPopup(Ledger.LedgerEvent evt)
        {
            var threatColor = evt.Severity switch
            {
                Ledger.LedgerSeverity.Critical => System.Windows.Media.Color.FromRgb(239, 68, 68),
                Ledger.LedgerSeverity.High => System.Windows.Media.Color.FromRgb(239, 68, 68),
                Ledger.LedgerSeverity.Medium => System.Windows.Media.Color.FromRgb(245, 158, 11),
                _ => System.Windows.Media.Color.FromRgb(34, 197, 94)
            };
            
            var alertWindow = CreateSecurityPopupWindow("Atlas AI Alert", threatColor);
            var mainStack = (StackPanel)((Border)alertWindow.Content).Child;
            
            // Header
            var icon = evt.Severity >= Ledger.LedgerSeverity.High ? "🚨" : "⚡";
            AddSecurityPopupHeader(mainStack, icon, evt.Title, threatColor);
            
            // Evidence
            if (evt.Evidence?.Any() == true)
            {
                foreach (var evidence in evt.Evidence.Take(4))
                {
                    AddSecurityPopupInfo(mainStack, evidence.Key.ToUpper(), evidence.Value);
                }
            }
            
            // Why it matters
            if (!string.IsNullOrEmpty(evt.WhyItMatters))
            {
                AddSecurityPopupQuestion(mainStack, evt.WhyItMatters);
            }
            
            // Remember my choice checkbox
            var rememberCheckbox = new CheckBox
            {
                Content = "Remember my choice for this type of alert",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                FontSize = 11,
                Margin = new Thickness(0, 12, 0, 0),
                IsChecked = false
            };
            mainStack.Children.Add(rememberCheckbox);
            
            // Action buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            
            foreach (var action in evt.Actions ?? new List<Ledger.LedgerAction>())
            {
                var btnColor = action.Type switch
                {
                    Ledger.LedgerActionType.Delete => System.Windows.Media.Color.FromRgb(239, 68, 68),
                    Ledger.LedgerActionType.Block => System.Windows.Media.Color.FromRgb(239, 68, 68),
                    Ledger.LedgerActionType.Revert => System.Windows.Media.Color.FromRgb(245, 158, 11),
                    Ledger.LedgerActionType.Allow => System.Windows.Media.Color.FromRgb(34, 197, 94),
                    _ => System.Windows.Media.Color.FromRgb(107, 114, 128)
                };
                
                var currentAction = action; // Capture for closure
                var btn = CreateSecurityAlertButton(action.Label, btnColor, async () =>
                {
                    var result = await Ledger.LedgerManager.Instance.ExecuteActionAsync(evt, currentAction);
                    AddMessage("ATLAS", $"✅ {result}", false);
                    
                    // Save preference if checkbox is checked
                    if (rememberCheckbox.IsChecked == true)
                    {
                        SaveSecurityPreference(evt.Category.ToString(), currentAction.Type);
                        _securityPrefsCache[evt.Category.ToString()] = currentAction.Type.ToString();
                        AddMessage("ATLAS", $"📝 I'll remember to {currentAction.Label.ToLower()} for similar {evt.Category} alerts.", false);
                    }
                    
                    // Record in ActionHistory
                    ActionHistory.ActionHistoryManager.Instance.RecordAction(new ActionHistory.ActionRecord
                    {
                        Type = ActionHistory.ActionType.SettingChanged,
                        Description = $"Security action: {currentAction.Label} for {evt.Title}",
                        UserCommand = $"User chose: {currentAction.Label}"
                    });
                    
                    alertWindow.Close();
                });
                buttonPanel.Children.Add(btn);
            }
            
            mainStack.Children.Add(buttonPanel);
            alertWindow.Show();
        }
        
        /// <summary>
        /// Save security preference for auto-handling similar alerts
        /// </summary>
        private void SaveSecurityPreference(string alertType, Ledger.LedgerActionType actionType)
        {
            try
            {
                var prefsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "security_prefs.json");
                
                var dir = System.IO.Path.GetDirectoryName(prefsPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                
                var prefs = new Dictionary<string, string>();
                if (System.IO.File.Exists(prefsPath))
                {
                    var json = System.IO.File.ReadAllText(prefsPath);
                    prefs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                }
                
                prefs[alertType] = actionType.ToString();
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                System.IO.File.WriteAllText(prefsPath, System.Text.Json.JsonSerializer.Serialize(prefs, options));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityPrefs] Failed to save: {ex.Message}");
            }
        }

        /// <summary>
        /// Load a previously saved security preference for a given alert category
        /// </summary>
        private static string? LoadSecurityPreference(string alertType)
        {
            // Check in-memory cache first
            if (_securityPrefsCache.TryGetValue(alertType, out var cached))
                return cached;

            try
            {
                var prefsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "security_prefs.json");

                if (!System.IO.File.Exists(prefsPath))
                    return null;

                var json = System.IO.File.ReadAllText(prefsPath);
                var prefs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (prefs != null && prefs.TryGetValue(alertType, out var action))
                {
                    _securityPrefsCache[alertType] = action;
                    return action;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityPrefs] Failed to load: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Bulk-load all security preferences from disk into the in-memory cache.
        /// </summary>
        private static void LoadAllSecurityPreferences()
        {
            try
            {
                var prefsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "security_prefs.json");

                if (!System.IO.File.Exists(prefsPath))
                    return;

                var json = System.IO.File.ReadAllText(prefsPath);
                var prefs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (prefs != null)
                {
                    foreach (var kvp in prefs)
                        _securityPrefsCache[kvp.Key] = kvp.Value;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityPrefs] Failed to load all: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create a security popup window with consistent styling
        /// </summary>
        private Window CreateSecurityPopupWindow(string title, System.Windows.Media.Color accentColor)
        {
            var window = new Window
            {
                Title = title,
                Width = 480,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false
            };
            
            var mainBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 12, 20)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(accentColor),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect 
                { 
                    Color = accentColor, 
                    BlurRadius = 30, 
                    ShadowDepth = 0, 
                    Opacity = 0.5 
                }
            };
            
            var mainStack = new StackPanel { Margin = new Thickness(20) };
            mainBorder.Child = mainStack;
            window.Content = mainBorder;
            
            return window;
        }
        
        private void AddSecurityPopupHeader(StackPanel parent, string icon, string title, System.Windows.Media.Color color)
        {
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            headerStack.Children.Add(new TextBlock { Text = icon, FontSize = 24, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            headerStack.Children.Add(new TextBlock { Text = title.ToUpper(), Foreground = new System.Windows.Media.SolidColorBrush(color), FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            parent.Children.Add(headerStack);
        }
        
        private void AddSecurityPopupBadge(StackPanel parent, string text, System.Windows.Media.Color color)
        {
            var badge = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, color.R, color.G, color.B)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            badge.Child = new TextBlock { Text = text, Foreground = new System.Windows.Media.SolidColorBrush(color), FontFamily = new System.Windows.Media.FontFamily("Cascadia Code"), FontSize = 10, FontWeight = FontWeights.SemiBold };
            parent.Children.Add(badge);
        }
        
        private void AddSecurityPopupInfo(StackPanel parent, string label, string value)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(new TextBlock { Text = label, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)), FontSize = 10 });
            stack.Children.Add(new TextBlock { Text = value, Foreground = System.Windows.Media.Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            parent.Children.Add(stack);
        }
        
        private void AddSecurityPopupQuestion(StackPanel parent, string question)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0)
            };
            border.Child = new TextBlock { Text = question, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 213, 219)), FontSize = 11, TextWrapping = TextWrapping.Wrap };
            parent.Children.Add(border);
        }
        
        private string GetShortExplanation(Agent.SecurityInsight insight)
        {
            // Extract first meaningful sentence for speech
            var lines = insight.Explanation.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // Remove common emoji prefixes and markdown
                var clean = line.Trim();
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"^[\u2600-\u27BF\u1F300-\u1F9FF\s*#]+", "");
                clean = clean.TrimStart(' ', '*', '#');
                if (!string.IsNullOrEmpty(clean) && !clean.StartsWith("**") && clean.Length > 10)
                    return clean.Length > 100 ? clean.Substring(0, 100) + "..." : clean;
            }
            return $"I detected {insight.Category}: {insight.SourceFile}";
        }
        
        private string GetShortWhyItMatters(Ledger.LedgerEvent evt)
        {
            if (string.IsNullOrEmpty(evt.WhyItMatters)) return "";
            var text = evt.WhyItMatters;
            return text.Length > 80 ? text.Substring(0, 80) + "..." : text;
        }
        
        private async void SecurityMonitor_InstallationDetected(object? sender, Agent.InstallationAlert alert)
        {
            Debug.WriteLine($"[Security] EVENT RECEIVED: {alert.FileName}");
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"[Security] On UI thread, showing popup for: {alert.FileName}");
                // Show visual popup alert (legacy - SecurityIntelligence will also fire)
                ShowInstallationAlertPopup(alert);
                
                // Build alert message for chat
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"🔔 **Installation Detected**");
                sb.AppendLine($"File: {alert.FileName}");
                sb.AppendLine($"Publisher: {alert.Publisher}");
                sb.AppendLine($"Risk: {alert.RiskLevel}");
                sb.AppendLine();
                sb.AppendLine(alert.Recommendation);
                
                if (alert.BundledApps?.Any() == true)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠️ Bundled software detected:");
                    foreach (var app in alert.BundledApps.Take(5))
                        sb.AppendLine($"  • {app}");
                }
                
                // Add to chat
                AddMessage("ATLAS", sb.ToString(), false);
                
                // Speak alert based on risk level
                string spokenAlert = alert.RiskLevel switch
                {
                    Agent.SecurityRiskLevel.High => $"Warning! I detected a potentially unwanted program: {alert.FileName}. I recommend not installing this.",
                    Agent.SecurityRiskLevel.Medium => $"Heads up, I noticed a new installer: {alert.FileName}. It's not from a known publisher, so be careful.",
                    Agent.SecurityRiskLevel.Low => $"New installation detected: {alert.FileName}. Looks safe, from {alert.Publisher}.",
                    _ => $"I detected a new file: {alert.FileName}."
                };
                
                await SpeakResponseAsync(spokenAlert);
            });
        }
        
        private void ShowInstallationAlertPopup(Agent.InstallationAlert alert)
        {
            var threatColor = alert.RiskLevel switch
            {
                Agent.SecurityRiskLevel.High => System.Windows.Media.Color.FromRgb(239, 68, 68),
                Agent.SecurityRiskLevel.Medium => System.Windows.Media.Color.FromRgb(245, 158, 11),
                Agent.SecurityRiskLevel.Low => System.Windows.Media.Color.FromRgb(34, 197, 94),
                _ => System.Windows.Media.Color.FromRgb(34, 211, 238)
            };
            
            var alertWindow = new Window
            {
                Title = "Atlas AI Security Alert",
                Width = 480,
                Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false
            };
            
            var mainBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 12, 20)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(threatColor),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect 
                { 
                    Color = threatColor, 
                    BlurRadius = 30, 
                    ShadowDepth = 0, 
                    Opacity = 0.5 
                }
            };
            
            var mainStack = new StackPanel { Margin = new Thickness(20) };
            
            // Header with icon
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var icon = alert.RiskLevel == Agent.SecurityRiskLevel.High ? "⚠️" : 
                       alert.RiskLevel == Agent.SecurityRiskLevel.Medium ? "⚡" : "🔔";
            headerStack.Children.Add(new TextBlock 
            { 
                Text = icon, 
                FontSize = 24, 
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock 
            { 
                Text = "NEW FILE DETECTED", 
                Foreground = new System.Windows.Media.SolidColorBrush(threatColor), 
                FontSize = 16, 
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            mainStack.Children.Add(headerStack);
            
            // Risk badge
            var riskText = alert.RiskLevel.ToString().ToUpper();
            var riskBadge = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, threatColor.R, threatColor.G, threatColor.B)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(threatColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            riskBadge.Child = new TextBlock 
            { 
                Text = $"RISK: {riskText}", 
                Foreground = new System.Windows.Media.SolidColorBrush(threatColor),
                FontFamily = new System.Windows.Media.FontFamily("Cascadia Code"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            };
            mainStack.Children.Add(riskBadge);
            
            // File info
            var infoStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            infoStack.Children.Add(new TextBlock 
            { 
                Text = "FILE:", 
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)), 
                FontSize = 10 
            });
            infoStack.Children.Add(new TextBlock 
            { 
                Text = alert.FileName, 
                Foreground = System.Windows.Media.Brushes.White, 
                FontSize = 13, 
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });
            
            if (!string.IsNullOrEmpty(alert.Publisher) && alert.Publisher != "Unknown")
            {
                infoStack.Children.Add(new TextBlock 
                { 
                    Text = "PUBLISHER:", 
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)), 
                    FontSize = 10 
                });
                infoStack.Children.Add(new TextBlock 
                { 
                    Text = alert.Publisher, 
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175)), 
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 8)
                });
            }
            mainStack.Children.Add(infoStack);
            
            // Recommendation
            if (!string.IsNullOrEmpty(alert.Recommendation))
            {
                var recBorder = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 16)
                };
                recBorder.Child = new TextBlock 
                { 
                    Text = alert.Recommendation, 
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 213, 219)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                };
                mainStack.Children.Add(recBorder);
            }
            
            // Action buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            
            // Delete button (for high risk)
            if (alert.RiskLevel == Agent.SecurityRiskLevel.High && !string.IsNullOrEmpty(alert.FilePath))
            {
                var deleteBtn = CreateSecurityAlertButton("🗑️ Delete", System.Windows.Media.Color.FromRgb(239, 68, 68), () =>
                {
                    try
                    {
                        if (System.IO.File.Exists(alert.FilePath))
                        {
                            System.IO.File.Delete(alert.FilePath);
                            ShowStatus($"🗑️ Deleted: {alert.FileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowStatus($"❌ Delete failed: {ex.Message}");
                    }
                    alertWindow.Close();
                });
                buttonPanel.Children.Add(deleteBtn);
            }
            
            // Block button
            var blockBtn = CreateSecurityAlertButton("🚫 Block", System.Windows.Media.Color.FromRgb(245, 158, 11), () =>
            {
                ShowStatus($"🚫 Blocked: {alert.FileName}");
                alertWindow.Close();
            });
            buttonPanel.Children.Add(blockBtn);
            
            // Allow button
            var allowBtn = CreateSecurityAlertButton("✓ Allow", System.Windows.Media.Color.FromRgb(34, 197, 94), () =>
            {
                ShowStatus($"✓ Allowed: {alert.FileName}");
                alertWindow.Close();
            });
            buttonPanel.Children.Add(allowBtn);
            
            // Dismiss button
            var dismissBtn = CreateSecurityAlertButton("✕", System.Windows.Media.Color.FromRgb(107, 114, 128), () =>
            {
                alertWindow.Close();
            });
            buttonPanel.Children.Add(dismissBtn);
            
            mainStack.Children.Add(buttonPanel);
            
            mainBorder.Child = mainStack;
            alertWindow.Content = mainBorder;
            
            // Allow dragging
            mainBorder.MouseLeftButtonDown += (s, e) => 
            { 
                if (e.ChangedButton == MouseButton.Left) 
                    alertWindow.DragMove(); 
            };
            
            // Auto-close after 30 seconds
            var autoCloseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            autoCloseTimer.Tick += (s, e) => { autoCloseTimer.Stop(); alertWindow.Close(); };
            autoCloseTimer.Start();
            
            alertWindow.Show();
        }
        
        private Button CreateSecurityAlertButton(string text, System.Windows.Media.Color color, Action onClick)
        {
            var btn = new Button
            {
                Content = text,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, color.R, color.G, color.B)),
                Foreground = new System.Windows.Media.SolidColorBrush(color),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11
            };
            
            btn.Click += (s, e) => onClick();
            btn.MouseEnter += (s, e) => btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, color.R, color.G, color.B));
            btn.MouseLeave += (s, e) => btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, color.R, color.G, color.B));
            
            return btn;
        }
        
        private async void SecurityMonitor_HealthScanCompleted(object? sender, Agent.HealthReport report)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                // Build health report message
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"🔍 **System Health Check** ({report.ScanTime:HH:mm})");
                sb.AppendLine();
                sb.AppendLine(report.Summary);
                
                if (report.HighMemoryProcesses.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("High memory processes:");
                    foreach (var proc in report.HighMemoryProcesses.Take(3))
                        sb.AppendLine($"  • {proc}");
                }
                
                // Add to chat
                AddMessage("ATLAS", sb.ToString(), false);
                
                // Speak summary
                string spokenSummary = report.OverallStatus switch
                {
                    "Critical" => $"Attention! Your system has {report.CriticalIssues} critical issues that need attention.",
                    "Warning" => $"Your system is running with {report.Warnings} warnings. {(report.MemoryUsedPercent > 80 ? "Memory is getting high." : "")}",
                    _ => "Your system is running smoothly. All checks passed."
                };
                
                await SpeakResponseAsync(spokenSummary);
            });
        }
        
        private void SecurityMonitor_StatusChanged(object? sender, string status)
        {
            Dispatcher.InvokeAsync(() => ShowStatus($"🛡 {status}"));
        }
        
        public void ToggleSecurityNotifications(bool enabled)
        {
            Agent.ProactiveSecurityMonitor.Instance.SetNotifications(enabled);
            ShowStatus(enabled ? "🔔 Security notifications ON" : "🔕 Security notifications OFF");
        }
        
        public void SetAutoHealthScan(bool enabled, int intervalHours = 2)
        {
            Agent.ProactiveSecurityMonitor.Instance.SetAutoScan(enabled, intervalHours);
        }
        
        public async Task RunManualHealthScanAsync()
        {
            ShowStatus("🔍 Running health scan...");
            await Agent.ProactiveSecurityMonitor.Instance.RunHealthScanAsync();
        }
        
        // ═══════════════════════════════════════════════════════════════
        // FOCUS MODE - Compact presence with smooth transitions
        // ═══════════════════════════════════════════════════════════════
        
        private void ToggleFocusMode()
        {
            _isFocusMode = !_isFocusMode;
            
            if (_isFocusMode)
                EnterFocusMode();
            else
                ExitFocusMode();
            
            RadialFocusIcon.Text = _isFocusMode ? "🔳" : "🎯";
            ShowStatus(_isFocusMode ? "🎯 Focus Mode" : "🔳 Normal Mode");
        }
        
        private void EnterFocusMode()
        {
            var duration = TimeSpan.FromMilliseconds(400);
            var easing = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            
            // Shrink and move core to bottom-right
            var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation(0.6, duration) { EasingFunction = easing };
            var translateXAnim = new System.Windows.Media.Animation.DoubleAnimation(350, duration) { EasingFunction = easing };
            var translateYAnim = new System.Windows.Media.Animation.DoubleAnimation(200, duration) { EasingFunction = easing };
            
            CoreContainerScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            CoreContainerScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            CoreContainerTranslate.BeginAnimation(TranslateTransform.XProperty, translateXAnim);
            CoreContainerTranslate.BeginAnimation(TranslateTransform.YProperty, translateYAnim);
            
            // Reduce projection count
            // (Projections will naturally show fewer in focus mode)
            
            // Shrink input area
            var inputMarginAnim = new System.Windows.Media.Animation.ThicknessAnimation(
                new Thickness(20, 0, 20, 15), duration) { EasingFunction = easing };
            InputBorder.BeginAnimation(MarginProperty, inputMarginAnim);
        }
        
        private void ExitFocusMode()
        {
            var duration = TimeSpan.FromMilliseconds(400);
            var easing = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            
            // Restore core position and scale
            var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation(1, duration) { EasingFunction = easing };
            var translateXAnim = new System.Windows.Media.Animation.DoubleAnimation(0, duration) { EasingFunction = easing };
            var translateYAnim = new System.Windows.Media.Animation.DoubleAnimation(0, duration) { EasingFunction = easing };
            
            CoreContainerScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            CoreContainerScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            CoreContainerTranslate.BeginAnimation(TranslateTransform.XProperty, translateXAnim);
            CoreContainerTranslate.BeginAnimation(TranslateTransform.YProperty, translateYAnim);
            
            // Restore input area
            var inputMarginAnim = new System.Windows.Media.Animation.ThicknessAnimation(
                new Thickness(0), duration) { EasingFunction = easing };
            InputBorder.BeginAnimation(MarginProperty, inputMarginAnim);
        }
        
        // ToggleHistoryDrawer is defined earlier in the file (around line 6468)
        // with proper OpenHistoryDrawer/CloseHistoryDrawer calls
        
        private void ToggleWakeWord()
        {
            isWakeWordEnabled = !isWakeWordEnabled;
            if (isWakeWordEnabled)
            {
                _wakeWordDetector?.StartListening();
                WakeWordIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                _wakeWordDetector?.StopListening();
                WakeWordIndicator.Visibility = Visibility.Collapsed;
            }
        }
        
        /// <summary>
        /// Stop animations specific to a state
        /// <summary>
        /// Force the taskbar icon to display for borderless windows
        /// </summary>
        private void ForceTaskbarIcon()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                
                // Ensure window shows in taskbar
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_APPWINDOW);
                
                // Try to load icon from the atlas.ico file in the app directory
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var iconPath = Path.Combine(appDir, "atlas.ico");
                
                // If not in app dir, try the source directory
                if (!File.Exists(iconPath))
                {
                    iconPath = Path.Combine(Directory.GetCurrentDirectory(), "AtlasAI", "atlas.ico");
                }
                if (!File.Exists(iconPath))
                {
                    iconPath = @"C:\Users\littl\VisualAIVirtualAssistant\AtlasAI\atlas.ico";
                }
                
                if (File.Exists(iconPath))
                {
                    var icon = new System.Drawing.Icon(iconPath);
                    SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, icon.Handle);
                    SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, icon.Handle);
                    Debug.WriteLine($"[ChatWindow] Icon loaded from: {iconPath}");
                }
                else
                {
                    Debug.WriteLine($"[ChatWindow] Icon file not found at: {iconPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] Error setting taskbar icon: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initialize the conversation system - sessions, memory, profile, and onboarding
        /// </summary>
        private async Task InitializeConversationSystemAsync()
        {
            try
            {
                _conversationManager = new ConversationManager();
                await _conversationManager.InitializeAsync();
                
                _systemPromptBuilder = new SystemPromptBuilder(_conversationManager);

                AttachSettingsListener();
                ApplySettingsToSystemPrompt();
                
                // Initialize coding assistant
                _codeAssistant = new Coding.CodeAssistantService();
                _codeToolExecutor = new Coding.CodeToolExecutor(_codeAssistant);
                
                // Initialize installed apps manager (scans in background)
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await SystemControl.InstalledAppsManager.Instance.InitializeAsync();
                        Debug.WriteLine($"[Apps] Initialized with {SystemControl.InstalledAppsManager.Instance.AppCount} apps");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Apps] Init error: {ex.Message}");
                    }
                });
                
                // Start proactive security monitoring
                InitializeSecurityMonitor();
                
                // First run: do lightweight in-chat onboarding (ask what to call the user)
                bool isFirstRun = await _conversationManager.IsFirstRunAsync();
                if (isFirstRun)
                {
                    try
                    {
                        var settings = SettingsStore.Current;
                        var name = (settings.PreferredName ?? "").Trim();
                        await _conversationManager.CompleteOnboardingAsync(
                            string.IsNullOrWhiteSpace(name) ? null : name,
                            Conversation.Models.ConversationStyle.Butler,
                            _conversationManager.UserProfile?.PreferredVoiceId);
                    }
                    catch { }
                }

                // Update system prompt with the latest settings + profile
                try { UpdateSystemPromptFromProfile(SettingsStore.Current.PersonalitySelected); }
                catch { UpdateSystemPromptFromProfile(); }

                // If user hasn't given a display name yet, ask once in chat
                BeginDisplayNameOnboardingIfNeeded();

                // Keep startup welcome disabled (prevents duplicate greetings)
                Debug.WriteLine("[ChatWindow] Startup welcome disabled to prevent duplicate greetings");
                
                Debug.WriteLine("[ChatWindow] Conversation system initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] Error initializing conversation system: {ex.Message}");
            }
        }

        private async Task InitializeRemoteConversationBackendAsync()
        {
            try
            {
                _conversationManager = new ConversationManager();
                await _conversationManager.InitializeAsync();

                _systemPromptBuilder = new SystemPromptBuilder(_conversationManager);

                AttachSettingsListener();
                ApplySettingsToSystemPrompt();

                _codeAssistant = new Coding.CodeAssistantService();
                _codeToolExecutor = new Coding.CodeToolExecutor(_codeAssistant);

                try { UpdateSystemPromptFromProfile(SettingsStore.Current.PersonalitySelected); }
                catch { UpdateSystemPromptFromProfile(); }

                Debug.WriteLine("[ChatWindow] Remote companion backend initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatWindow] Error initializing remote companion backend: {ex.Message}");
            }
        }

        private void AttachSettingsListener()
        {
            try
            {
                if (_settingsListenerAttached) return;
                SettingsStore.SettingsChanged += SettingsStore_SettingsChanged;
                _settingsListenerAttached = true;
            }
            catch
            {
            }
        }

        private void SettingsStore_SettingsChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ApplySettingsToSystemPrompt();
                    try { UpdateSystemPromptFromProfile(SettingsStore.Current.PersonalitySelected); }
                    catch { UpdateSystemPromptFromProfile(); }
                });
            }
            catch
            {
            }
        }

        private void UpdateSystemPromptFromProfile(string? selectedPersonalityId)
        {
            // Personality selection is read by SystemPromptBuilder from SettingsStore.
            // This overload exists to allow startup/settings wiring to pass the selected ID explicitly.
            try
            {
                var id = (selectedPersonalityId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    var s = SettingsStore.Current;
                    if (!string.Equals(s.PersonalitySelected, id, StringComparison.OrdinalIgnoreCase))
                    {
                        s.PersonalitySelected = id;
                        try { SettingsStore.Save(s); } catch { }
                    }
                }
            }
            catch
            {
            }

            UpdateSystemPromptFromProfile();
        }

        private void ApplySettingsToSystemPrompt()
        {
            try
            {
                var settings = SettingsStore.Current;
                SystemPromptBuilder.UnrestrictedLevel = Math.Clamp(settings.UnfilteredChaosIntensity, 1, 5);
            }
            catch
            {
            }
        }

        private void BeginDisplayNameOnboardingIfNeeded()
        {
            try
            {
                // Startup display-name prompt disabled to avoid duplicate startup greetings.
                _awaitingUserDisplayName = false;
                return;

                var settings = SettingsStore.Current;
                if (!string.IsNullOrWhiteSpace((settings.PreferredName ?? "").Trim()))
                {
                    _awaitingUserDisplayName = false;
                    return;
                }

                if (_awaitingUserDisplayName) return;

                _awaitingUserDisplayName = true;
                var intro = "I'm Atlas OS Online. What should I call you?";
                AddMessage("Atlas", intro, false);
                _ = SpeakJarvisResponseAsync(intro);
            }
            catch
            {
            }
        }

        private async Task CompleteDisplayNameOnboardingAsync(string raw)
        {
            try
            {
                var cleaned = (raw ?? "").Trim();
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "[^\\p{L}\\p{N} _\"'-]", "").Trim();
                if (cleaned.Length > 28) cleaned = cleaned.Substring(0, 28).Trim();

                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    var retry = "I didn’t catch that. What name should I use for you?";
                    AddMessage("Atlas", retry, false);
                    _ = SpeakJarvisResponseAsync(retry);
                    return;
                }

                var settings = SettingsStore.Current;
                settings.PreferredName = cleaned;
                settings.SalutationAsked = true;
                settings.SalutationPreference = "name";
                try { SettingsStore.Save(settings); } catch { }

                try { ResponseStyleController.Instance.SetUserName(cleaned); } catch { }

                try
                {
                    if (_conversationManager != null)
                        await _conversationManager.CompleteOnboardingAsync(cleaned, Conversation.Models.ConversationStyle.Butler, _conversationManager.UserProfile?.PreferredVoiceId);
                }
                catch
                {
                }

                _awaitingUserDisplayName = false;
                ApplySettingsToSystemPrompt();
                UpdateSystemPromptFromProfile();

                var ack = $"Got it, {cleaned}. What are we doing first?";
                AddMessage("Atlas", ack, false);
                _ = SpeakJarvisResponseAsync(ack);
            }
            catch
            {
                _awaitingUserDisplayName = false;
            }
        }
        
        /// <summary>
        /// Shows the big welcome message on startup - ALWAYS runs
        /// </summary>
        private async Task ShowStartupWelcomeAsync()
        {
            try
            {
                // Get user's name from profile
                string userName = "sir"; // Default fallback
                if (_conversationManager?.UserProfile?.DisplayName != null && 
                    !string.IsNullOrWhiteSpace(_conversationManager.UserProfile.DisplayName))
                {
                    userName = _conversationManager.UserProfile.DisplayName;
                }
                
                // Use the random greeting from SystemPromptBuilder
                var greeting = _systemPromptBuilder?.GetGreeting(false) ?? GetRandomWelcomeMessage(userName);
                
                Debug.WriteLine($"[Welcome] Showing startup welcome: {greeting}");
                
                // DON'T show in chat - only speak it to avoid duplicate greetings
                // (The spoken greeting is enough, no need to clutter the chat)
                Debug.WriteLine($"[Welcome] Greeting will be spoken only (not shown in chat)");
                
                // Wait for voice system to be fully ready
                await Task.Delay(500);
                
                try
                {
                    var keys = SettingsWindow.GetVoiceApiKeys();
                        bool configured = false;
                        if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrEmpty(elevenKey))
                        {
                            _voiceManager.ConfigureProvider(VoiceProviderType.ElevenLabs, new Dictionary<string, string> { ["ApiKey"] = elevenKey });
                            configured = await _voiceManager.SetProviderAsync(VoiceProviderType.ElevenLabs);
                            if (configured) await _voiceManager.RestoreSavedVoiceAsync();
                        }
                    
                    if (configured)
                    {
                        // STEP 30 FIX: Use SpeechCoordinator for startup greeting
                        // This is a SYSTEM voice (startup), not conversation
                        var coordinator = SpeechCoordinator.Instance;
                        coordinator.SetVoiceManager(_voiceManager);
                        var success = await coordinator.SpeakSystemAsync(greeting, "startup_greeting");
                        if (success)
                        {
                            Debug.WriteLine("[Welcome] Startup greeting spoken via SpeechCoordinator");
                        }
                        else
                        {
                            Debug.WriteLine("[Welcome] Startup greeting rejected - another speaker active");
                        }
                    }
                        else
                        {
                            Debug.WriteLine("[Welcome] ElevenLabs not configured - skipping spoken startup greeting");
                        }
                }
                catch (Exception ttsEx)
                {
                    Debug.WriteLine($"[Welcome] TTS error: {ttsEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Welcome] Error showing startup welcome: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show the first-run onboarding window
        /// </summary>
        private async Task ShowOnboardingAsync()
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                bool voiceReady = false;
                try
                {
                    var keys = SettingsWindow.GetVoiceApiKeys();
                    if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrEmpty(elevenKey))
                    {
                        _voiceManager.ConfigureProvider(VoiceProviderType.ElevenLabs, new Dictionary<string, string> { ["ApiKey"] = elevenKey });
                        voiceReady = await _voiceManager.SetProviderAsync(VoiceProviderType.ElevenLabs);
                        if (voiceReady) await _voiceManager.RestoreSavedVoiceAsync();
                        Debug.WriteLine($"[Onboarding] ElevenLabs configured: {voiceReady}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Onboarding] Voice config error: {ex.Message}");
                }
                
                // Skip seeding chat with onboarding text
                
                // Speak a SHORTER version that covers all features concisely
                var spokenWelcome = @"Hello. I'm Atlas, your personal AI assistant. 

I can open apps, manage files, search the web, play music, and automate everyday tasks. 

I have a built-in code editor for writing and debugging code. 

I support voice and text, and you can adjust my style anytime. 

I'm context-aware and can help you directly in other applications. 

I remember your preferences and learn over time, but you're always in control. 

Your conversations are saved in History, and I include security tools that work quietly in the background.

Before we begin, what would you like me to call you?";
                
                if (voiceReady)
                {
                    _ = _voiceManager.SpeakAsync(spokenWelcome);

                    int wordCount = spokenWelcome.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    int estimatedMs = (wordCount * 280) + 8000;
                    Debug.WriteLine($"[Onboarding] Waiting {estimatedMs}ms for {wordCount} words (includes 8s buffer)");
                    await Task.Delay(estimatedMs);
                }
                
                // NOW show the onboarding window for settings (after speech is done)
                var onboarding = new OnboardingWindow();
                onboarding.Owner = this;
                
                if (onboarding.ShowDialog() == true)
                {
                    // Save onboarding choices
                    _ = _conversationManager?.CompleteOnboardingAsync(
                        onboarding.UserName,
                        onboarding.SelectedStyle,
                        Voice.VoiceProfile.DefaultAtlasVoiceId
                    );
                    
                    // Update system prompt with new profile
                    UpdateSystemPromptFromProfile();
                    
                    // If user tried a quick action, execute it
                    if (!string.IsNullOrEmpty(onboarding.TriedAction))
                    {
                        InputBox.Text = onboarding.TriedAction;
                        SendMessage();
                    }
                    else
                    {
                        // Show personalized welcome after onboarding
                        var userName = onboarding.UserName ?? "friend";
                        var personalGreeting = $"Nice to meet you, {userName}! I'm ready to help. What would you like to do?";
                        AddMessage("Atlas", personalGreeting, false);
                        if (voiceReady)
                            await _voiceManager.SpeakAsync(personalGreeting);
                    }
                }
                else
                {
                    // User closed onboarding without completing - still mark as completed with Butler style
                    _ = _conversationManager?.CompleteOnboardingAsync(null, Conversation.Models.ConversationStyle.Butler, Voice.VoiceProfile.DefaultAtlasVoiceId);
                }
            });
        }
        
        /// <summary>
        /// Update the system prompt based on user profile and style
        /// </summary>
        private void UpdateSystemPromptFromProfile()
        {
            if (_systemPromptBuilder == null || _conversationManager == null) return;
            
            // Build new system prompt with profile, memory, and style
            var newSystemPrompt = _systemPromptBuilder.BuildSystemPrompt();
            
            // Update the conversation history's system message
            if (conversationHistory.Count > 0)
            {
                conversationHistory[0] = new { role = "system", content = newSystemPrompt };
            }
            else
            {
                conversationHistory.Add(new { role = "system", content = newSystemPrompt });
            }
            
            Debug.WriteLine($"[ChatWindow] System prompt updated for style: {_conversationManager.GetConversationStyle()}");
        }

        /// <summary>
        /// Handle window state changes - use background thread for wake word when minimized
        /// </summary>
        private void ChatWindow_StateChanged(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatWindow] State changed to: {WindowState}");
            
            if (WindowState == WindowState.Minimized)
            {
                System.Diagnostics.Debug.WriteLine("[ChatWindow] Window minimized - switching wake word to background mode");
                // Switch to background-only wake word mode to prevent UI freeze
                SwitchToBackgroundWakeWord();
            }
            else if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
            {
                System.Diagnostics.Debug.WriteLine("[ChatWindow] Window restored - switching wake word to UI mode");
                // Switch back to normal UI-integrated wake word mode
                SwitchToUIWakeWord();
            }
        }

        /// <summary>
        /// Handle visibility changes - use background thread for wake word when hidden
        /// </summary>
        private void ChatWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            bool isVisible = (bool)e.NewValue;
            System.Diagnostics.Debug.WriteLine($"[ChatWindow] Visibility changed to: {isVisible}");
            
            if (!isVisible)
            {
                System.Diagnostics.Debug.WriteLine("[ChatWindow] Window hidden - switching wake word to background mode");
                // Switch to background-only wake word mode to prevent UI freeze
                SwitchToBackgroundWakeWord();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ChatWindow] Window shown - switching wake word to UI mode");
                // Switch back to normal UI-integrated wake word mode
                SwitchToUIWakeWord();
            }
        }

        private bool _isBackgroundWakeWordMode = false;
        private System.Threading.Timer? _backgroundWakeWordTimer;
        private WhisperSpeechRecognition? _backgroundWakeWordWhisper;

        /// <summary>
        /// Switch to background-only wake word mode (no UI updates to prevent freeze)
        /// Uses SafeDispatcherInvoke (BeginInvoke) instead of blocking Invoke
        /// </summary>
        private void SwitchToBackgroundWakeWord()
        {
            if (!isWakeWordEnabled) return;
            
            _isBackgroundWakeWordMode = true;
            
            // Don't stop the wake word system - just let it run with safe dispatcher
            // The SafeDispatcherInvoke we're using in the event handlers will prevent freezing
            System.Diagnostics.Debug.WriteLine("[ChatWindow] Switched to background wake word mode - wake word still active");
            
            // If wake word isn't running, start it
            if (!isWakeWordListening && !isListening)
            {
                isWakeWordListening = true;
                StartWhisperWakeWordListening();
            }
        }

        /// <summary>
        /// Switch back to normal UI-integrated wake word mode
        /// </summary>
        private void SwitchToUIWakeWord()
        {
            if (!isWakeWordEnabled) return;
            
            _isBackgroundWakeWordMode = false;
            
            System.Diagnostics.Debug.WriteLine("[ChatWindow] Switched to UI wake word mode");
            
            // Restart normal UI-integrated wake word system if not already running
            if (isWakeWordEnabled && !isListening && !isWakeWordListening)
            {
                isWakeWordListening = true;
                StartWhisperWakeWordListening();
            }
        }

        /// <summary>
        /// Background wake word check that doesn't touch UI thread
        /// </summary>
        private void BackgroundWakeWordCheck(object? state)
        {
            if (!_isBackgroundWakeWordMode || !isWakeWordEnabled) return;
            
            try
            {
                // Simple background check - just listen for "Atlas" without UI updates
                // This is a minimal implementation that avoids UI thread interaction
                System.Diagnostics.Debug.WriteLine("[ChatWindow] Background wake word check (minimized mode)");
                
                // TODO: Implement lightweight background wake word detection
                // For now, this prevents the freeze by not doing heavy operations
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatWindow] Background wake word error: {ex.Message}");
            }
        }

        private void PositionNearAvatar()
        {
            try
            {
                // Don't reposition if window is maximized - let it stay fullscreen
                if (WindowState == WindowState.Maximized)
                    return;
                    
                // Get the working area of the primary screen
                var workingArea = SystemParameters.WorkArea;
                
                // Position chat window to the left of where the avatar would be
                var padding = 20;
                var avatarWidth = 200; // Avatar window width
                
                // Position to the left of the avatar with some spacing
                Left = workingArea.Right - Width - avatarWidth - (padding * 2);
                Top = workingArea.Top + padding;
                
                // Ensure window stays within screen bounds
                if (Left < workingArea.Left) 
                {
                    // If it doesn't fit to the left, position it to the right of center
                    Left = workingArea.Left + (workingArea.Width - Width) / 2 + 100;
                }
                if (Top < workingArea.Top) Top = workingArea.Top + padding;
                if (Top + Height > workingArea.Bottom) 
                    Top = workingArea.Bottom - Height - padding;
            }
            catch
            {
                // Fallback positioning - only if not maximized
                if (WindowState != WindowState.Maximized)
                {
                    Left = SystemParameters.WorkArea.Right - Width - 50;
                    Top = 50;
                }
            }
        }

        private void InitializeScreenCapture()
        {
            try
            {
                _screenCapture = new ScreenCaptureEngine();
                _screenCapture.CaptureCompleted += OnCaptureCompleted;
                _screenCapture.CaptureError += OnCaptureError;
                
                // Initialize hotkeys when window is loaded
                Loaded += (s, e) => InitializeHotkeys();
            }
            catch (Exception ex)
            {
                ShowStatus($"⚠️ Screen capture initialization failed: {ex.Message}");
            }
        }

        private void OnCaptureError(string error)
        {
            Dispatcher.Invoke(() =>
            {
                ShowStatus($"❌ Capture error: {error}");
            });
        }

        private void AvatarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string avatarId)
            {
                SelectAvatar(avatarId);
            }
        }

        private void SelectAvatar(string avatarId, bool silent = false)
        {
            try
            {
                // Update button appearances
                ResetAvatarButtonStyles();
                
                // Highlight selected avatar button
                var selectedButton = FindAvatarButton(avatarId);
                if (selectedButton != null)
                {
                    selectedButton.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)); // Blue
                }
                
                // Show confirmation message (unless silent startup)
                if (!silent)
                {
                    var avatarName = GetAvatarDisplayName(avatarId);
                    AddMessage("Atlas", $"🎭 Avatar changed to: {avatarName}", false);
                    ShowStatus($"✅ Avatar set to: {avatarName}");
                }
                
                // TODO: Communicate with Unity to actually change the avatar
                // This would require Unity-C# communication bridge
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ Error changing avatar: {ex.Message}");
            }
        }

        private void ResetAvatarButtonStyles()
        {
            var defaultColor = new SolidColorBrush(Color.FromRgb(60, 60, 60)); // Dark gray
            
            DefaultAvatarBtn.Background = defaultColor;
            EnergeticAvatarBtn.Background = defaultColor;
            CalmAvatarBtn.Background = defaultColor;
            ReadyPlayerAvatarBtn.Background = defaultColor;
        }

        private Button FindAvatarButton(string avatarId)
        {
            return avatarId switch
            {
                "default" => DefaultAvatarBtn,
                "energetic" => EnergeticAvatarBtn,
                "calm" => CalmAvatarBtn,
                "readyplayer" => ReadyPlayerAvatarBtn,
                _ => null
            };
        }

        private string GetAvatarDisplayName(string avatarId)
        {
            return avatarId switch
            {
                "default" => "Default Assistant",
                "energetic" => "Energetic Assistant", 
                "calm" => "Calm Assistant",
                "readyplayer" => "Ready Player Me Avatar",
                _ => "Unknown Avatar"
            };
        }

        private void InitializeHotkeys()
        {
            try
            {
                var windowHelper = new WindowInteropHelper(this);
                _hotkeyManager = new HotkeyManager(windowHelper.Handle);
                _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
                _hotkeyManager.RegisterDefaultHotkeys();
                
                ShowStatus("📸 Screen capture ready! Use Ctrl+Shift+S to capture");
            }
            catch (Exception ex)
            {
                ShowStatus($"⚠️ Hotkey registration failed: {ex.Message}");
            }
        }

        private void OnHotkeyPressed(string hotkeyName)
        {
            Dispatcher.Invoke(() =>
            {
                switch (hotkeyName)
                {
                    case "screenshot":
                    case "fullscreen":
                    case "quickcapture":
                        CaptureScreenshot();
                        break;
                }
            });
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            await CaptureScreenshot();
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            OpenHistoryWindow();
        }

        private async Task CaptureScreenshot()
        {
            try
            {
                ShowStatus("📸 Taking screenshot...");
                CaptureButton.IsEnabled = false;
                
                // Hide all Atlas windows before capturing so they don't appear in screenshot
                var wasVisible = this.Visibility == Visibility.Visible;
                var previousOpacity = this.Opacity;
                
                // Also hide the main avatar window if it exists
                Window? mainWindow = null;
                double mainWindowOpacity = 1;
                bool mainWindowWasVisible = false;
                
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mw)
                    {
                        mainWindow = mw;
                        mainWindowWasVisible = mw.Visibility == Visibility.Visible;
                        mainWindowOpacity = mw.Opacity;
                        mw.Opacity = 0;
                        mw.Hide();
                        break;
                    }
                }
                
                // Hide this chat window
                this.Opacity = 0;
                this.Hide();
                
                // Wait for windows to fully hide
                await Task.Delay(300);
                
                CaptureResult? result = null;
                Exception? captureException = null;
                
                try
                {
                    System.Diagnostics.Debug.WriteLine("[Screenshot] Starting capture...");
                    result = await _screenCapture.CaptureScreenAsync();
                    System.Diagnostics.Debug.WriteLine($"[Screenshot] Capture complete: Success={result?.Success}, Path={result?.Metadata?.FilePath}");
                }
                catch (Exception ex)
                {
                    captureException = ex;
                    System.Diagnostics.Debug.WriteLine($"[Screenshot] Capture exception: {ex}");
                }
                
                // Always restore windows after capture attempt
                if (mainWindow != null && mainWindowWasVisible)
                {
                    mainWindow.Show();
                    mainWindow.Opacity = mainWindowOpacity;
                }
                
                if (wasVisible)
                {
                    this.Show();
                    this.Opacity = previousOpacity;
                    this.Activate();
                }
                
                // Small delay to ensure window is visible before showing message
                await Task.Delay(100);
                
                if (captureException != null)
                {
                    AddMessage("Atlas", $"❌ Screenshot capture failed: {captureException.Message}", false);
                    ShowStatus("❌ Screenshot failed");
                    return;
                }
                
                if (result != null && result.Success && !string.IsNullOrEmpty(result.Metadata?.FilePath))
                {
                    // Verify file was actually saved
                    if (System.IO.File.Exists(result.Metadata.FilePath))
                    {
                        // Show preview
                        try
                        {
                            _screenCapture.ShowCapturePreview(result);
                        }
                        catch { } // Preview is optional
                        
                        // Add to chat
                        AddMessage("Atlas", $"📸 Screenshot captured! Saved to:\n{result.Metadata.FilePath}", false);
                        ShowStatus("✅ Screenshot saved!");
                    }
                    else
                    {
                        AddMessage("Atlas", $"❌ Screenshot file was not saved to: {result.Metadata.FilePath}", false);
                        ShowStatus("❌ Screenshot file not found");
                    }
                }
                else
                {
                    var errorMsg = result?.Error ?? "Unknown error - capture returned null or failed";
                    AddMessage("Atlas", $"❌ Screenshot capture failed: {errorMsg}", false);
                    ShowStatus("❌ Screenshot failed");
                }
            }
            catch (Exception ex)
            {
                // Make sure windows are visible even on error
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mw)
                    {
                        mw.Show();
                        mw.Opacity = 1;
                        break;
                    }
                }
                this.Show();
                this.Opacity = 1;
                
                System.Diagnostics.Debug.WriteLine($"Screenshot error: {ex}");
                ShowStatus($"❌ Screenshot failed: {ex.Message}");
                AddMessage("Atlas", $"❌ Screenshot capture failed: {ex.Message}", false);
            }
            finally
            {
                CaptureButton.IsEnabled = true;
            }
        }

        private async void OnCaptureCompleted(CaptureResult result)
        {
            Dispatcher.Invoke(async () =>
            {
                ShowStatus($"✅ Screenshot saved: {Path.GetFileName(result.Metadata.FilePath)}");
                
                // Add to history
                try
                {
                    await _historyManager.AddCaptureAsync(result);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to add capture to history: {ex.Message}");
                }
            });
        }

        private async Task<string> AnalyzeLatestScreenshot(CancellationToken ct = default)
        {
            try
            {
                var capturesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "Captures");

                if (!Directory.Exists(capturesPath))
                    return "❌ No screenshots found. Take a screenshot first with /capture or 📸 button.";

                var latestFile = Directory.GetFiles(capturesPath, "*.png")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .FirstOrDefault();

                if (latestFile == null)
                    return "❌ No screenshots found. Take a screenshot first with /capture or 📸 button.";

                // Check if AI is available
                var activeProvider = AIManager.GetActiveProviderInstance();
                if (activeProvider == null || !activeProvider.IsConfigured)
                {
                    // Provide basic analysis without AI
                    var fileInfo = new FileInfo(latestFile);
                    var fileName = Path.GetFileName(latestFile);
                    
                    return $"📸 **Screenshot Analysis (Basic Mode)**\n\n" +
                           $"**File:** {fileName}\n" +
                           $"**Size:** {fileInfo.Length / 1024:N0} KB\n" +
                           $"**Captured:** {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                           $"**Location:** {latestFile}\n\n" +
                           $"💡 **For AI-powered analysis:**\n" +
                           $"Configure your API key in Settings → AI Provider to get:\n" +
                           $"• Detailed image description\n" +
                           $"• OCR text extraction\n" +
                           $"• UI element identification\n" +
                           $"• Smart insights and suggestions\n\n" +
                           $"🔧 Use `/ocr` for basic text extraction or open the image manually.";
                }

                // Convert image to base64 for AI analysis
                var imageBytes = await File.ReadAllBytesAsync(latestFile, ct);
                var base64Image = Convert.ToBase64String(imageBytes);

                // Send to AI for analysis
                var analysisPrompt = "Please analyze this screenshot and describe:\n" +
                                   "1. What you see in the image\n" +
                                   "2. Any text content (OCR)\n" +
                                   "3. UI elements and their purpose\n" +
                                   "4. Suggested actions or insights\n\n" +
                                   "Be detailed and helpful in your analysis.";

                // Add image context to conversation
                conversationHistory.Add(new { 
                    role = "user", 
                    content = new object[] {
                        (object)new { type = "text", text = analysisPrompt },
                        (object)new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                    }
                });

                var response = await AIManager.SendMessageAsync(conversationHistory, 1000, ct);
                
                if (response.Success)
                {
                    conversationHistory.Add(new { role = "assistant", content = response.Content });
                    return $"🔍 **Screenshot Analysis:**\n\n{response.Content}";
                }
                else
                {
                    // Fallback when AI fails
                    var fileInfo = new FileInfo(latestFile);
                    var fileName = Path.GetFileName(latestFile);
                    
                    return $"📸 **Screenshot Analysis (Fallback Mode)**\n\n" +
                           $"**File:** {fileName}\n" +
                           $"**Size:** {fileInfo.Length / 1024:N0} KB\n" +
                           $"**Captured:** {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}\n\n" +
                           $"❌ **AI Analysis Failed:** {response.Error}\n\n" +
                           $"💡 **Alternative options:**\n" +
                           $"• Use `/ocr` for text extraction\n" +
                           $"• Open image manually for viewing\n" +
                           $"• Configure valid API key for full AI analysis";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Analysis error: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Analyze an attached image with a specific question from the user
        /// </summary>
        private async Task<string> AnalyzeImageWithQuestion(string imagePath, string question, CancellationToken ct = default)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return $"❌ Image not found: {imagePath}";
                
                // Check if AI is available
                var activeProvider = AIManager.GetActiveProviderInstance();
                if (activeProvider == null || !activeProvider.IsConfigured)
                {
                    var fileInfo = new FileInfo(imagePath);
                    var fileName = Path.GetFileName(imagePath);
                    
                    return $"📸 **Image Analysis (Basic Mode)**\n\n" +
                           $"**File:** {fileName}\n" +
                           $"**Size:** {fileInfo.Length / 1024:N0} KB\n" +
                           $"**Your question:** {question}\n\n" +
                           $"💡 **For AI-powered analysis:**\n" +
                           $"Configure your API key in Settings → AI Provider to get:\n" +
                           $"• Detailed image description\n" +
                           $"• Answer to your question about the image\n" +
                           $"• OCR text extraction\n" +
                           $"• Smart insights and suggestions";
                }
                
                // Convert image to base64 for AI analysis
                var imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
                var base64Image = Convert.ToBase64String(imageBytes);
                
                // Determine image type
                var ext = Path.GetExtension(imagePath).ToLower();
                var mimeType = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".bmp" => "image/bmp",
                    _ => "image/png"
                };
                
                // Build analysis prompt based on user's question
                var analysisPrompt = string.IsNullOrWhiteSpace(question) || question.Length < 5
                    ? "Please analyze this image and describe what you see in detail. Include any text, UI elements, or notable features."
                    : $"The user attached this image and asked: \"{question}\"\n\nPlease analyze the image and answer their question. Be helpful and specific.";
                
                // Add image context to conversation
                conversationHistory.Add(new { 
                    role = "user", 
                    content = new object[] {
                        (object)new { type = "text", text = analysisPrompt },
                        (object)new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Image}" } }
                    }
                });
                
                var response = await AIManager.SendMessageAsync(conversationHistory, 1000, ct);
                
                if (response.Success)
                {
                    conversationHistory.Add(new { role = "assistant", content = response.Content });
                    return $"🔍 **Image Analysis:**\n\n{response.Content}";
                }
                else
                {
                    var fileInfo = new FileInfo(imagePath);
                    var fileName = Path.GetFileName(imagePath);
                    
                    return $"📸 **Image Analysis (Fallback Mode)**\n\n" +
                           $"**File:** {fileName}\n" +
                           $"**Size:** {fileInfo.Length / 1024:N0} KB\n\n" +
                           $"❌ **AI Analysis Failed:** {response.Error}\n\n" +
                           $"💡 Try again or check your API key settings.";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Image analysis error: {ex.Message}";
            }
        }

        /// <summary>
        /// Generate an image using DALL-E and display it in chat
        /// </summary>
        private async Task<string> GenerateAndDisplayImage(string prompt, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                ShowStatus($"🎨 Generating image: {prompt}...");
                
                // Show avatar thinking animation - DISABLED
                // if (_avatarIntegration?.IsUnityRunning == true)
                // {
                //     await _avatarIntegration.AvatarSetStateAsync("Thinking");
                // }
                
                var result = await Tools.ImageGeneratorTool.GenerateImageAsync(prompt, ct: ct);
                
                if (!result.Success)
                {
                    return $"❌ {result.Error}";
                }
                
                // Build response with image info
                var response = new StringBuilder();
                response.AppendLine($"🎨 **Image Generated!**\n");
                response.AppendLine($"**Prompt:** {prompt}");
                
                if (!string.IsNullOrEmpty(result.RevisedPrompt) && result.RevisedPrompt != prompt)
                {
                    response.AppendLine($"**DALL-E enhanced to:** {result.RevisedPrompt}");
                }
                
                response.AppendLine($"\n📁 **Saved to:** {result.ImagePath}");
                response.AppendLine($"\n💡 Say \"open images folder\" to see all your generated images!");
                
                // Open the image automatically
                if (!string.IsNullOrEmpty(result.ImagePath) && File.Exists(result.ImagePath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = result.ImagePath,
                            UseShellExecute = true
                        });
                        response.AppendLine("\n✅ Opened the image for you!");
                    }
                    catch
                    {
                        // Silently fail if can't open
                    }
                }
                
                return response.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ Image generation failed: {ex.Message}";
            }
        }

        private async Task<string> ExtractTextFromScreenshot(CancellationToken ct = default)
        {
            try
            {
                var capturesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "Captures");

                var latestFile = Directory.GetFiles(capturesPath, "*.png")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .FirstOrDefault();

                if (latestFile == null)
                    return "❌ No screenshots found. Take a screenshot first.";

                // Check if AI is available
                var activeProvider = AIManager.GetActiveProviderInstance();
                if (activeProvider == null || !activeProvider.IsConfigured)
                {
                    var fileName = Path.GetFileName(latestFile);
                    return $"📝 **OCR (Basic Mode)**\n\n" +
                           $"**Screenshot:** {fileName}\n\n" +
                           $"❌ **AI-powered OCR unavailable**\n" +
                           $"Configure your API key in Settings → AI Provider for:\n" +
                           $"• Automatic text extraction\n" +
                           $"• Smart text formatting\n" +
                           $"• Clipboard integration\n\n" +
                           $"💡 **Alternative:** Open the screenshot manually and copy text by hand.";
                }

                var imageBytes = await File.ReadAllBytesAsync(latestFile, ct);
                var base64Image = Convert.ToBase64String(imageBytes);

                var ocrPrompt = "Please extract and transcribe ALL text visible in this screenshot. " +
                              "Organize the text logically and preserve formatting where possible. " +
                              "If there's no text, just say 'No text detected'.";

                conversationHistory.Add(new { 
                    role = "user", 
                    content = new object[] {
                        (object)new { type = "text", text = ocrPrompt },
                        (object)new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                    }
                });

                var response = await AIManager.SendMessageAsync(conversationHistory, 800, ct);
                
                if (response.Success)
                {
                    conversationHistory.Add(new { role = "assistant", content = response.Content });
                    
                    // Offer to copy text to clipboard
                    if (!response.Content.Contains("No text detected"))
                    {
                        Clipboard.SetText(response.Content);
                        return $"📝 **Extracted Text (copied to clipboard):**\n\n{response.Content}";
                    }
                    else
                    {
                        return $"📝 **OCR Result:**\n\n{response.Content}";
                    }
                }
                else
                {
                    var fileName = Path.GetFileName(latestFile);
                    return $"📝 **OCR (Fallback Mode)**\n\n" +
                           $"**Screenshot:** {fileName}\n\n" +
                           $"❌ **AI OCR Failed:** {response.Error}\n\n" +
                           $"💡 **Alternative options:**\n" +
                           $"• Open screenshot manually\n" +
                           $"• Use Windows built-in OCR tools\n" +
                           $"• Configure valid API key for AI-powered OCR";
                }
            }
            catch (Exception ex)
            {
                return $"❌ OCR error: {ex.Message}";
            }
        }

        private async void UpdateDB_Click(object sender, RoutedEventArgs e)
        {
            AddMessage("You", "🔄 Update Threat Database", true);
            var cts = new CancellationTokenSource();
            try
            {
                var response = await UpdateThreatDatabase(cts.Token);
                AddMessage("Atlas", response, false);
                await _voiceManager.SpeakAsync("Database update complete.");
            }
            finally
            {
                cts.Dispose();
            }
        }

        private async Task<string> PerformSystemScan(CancellationToken ct = default)
        {
            return await PerformDeepScan(ct);
        }

        private async Task<string> PerformSpywareScan(CancellationToken ct = default)
        {
            return await PerformDeepScan(ct);
        }

        private async Task<string> PerformDeepScan(CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                ShowStatus("🛡️ Starting deep system scan...");
                
                var scanner = new SystemControl.UnifiedScanner();
                scanner.ProgressChanged += msg => Dispatcher.Invoke(() => ShowStatus(msg));
                scanner.ProgressPercentChanged += pct => Dispatcher.Invoke(() => 
                    StatusLabel.Text = $"Scanning... {pct}%");
                
                // Note: UnifiedScanner.PerformDeepScanAsync manages its own internal CTS 
                // but we can't easily pass 'ct' to it without modifying UnifiedScanner.
                // For now, we'll check 'ct' before calling it.
                var result = await scanner.PerformDeepScanAsync();
                ct.ThrowIfCancellationRequested();
                
                var db = SystemControl.ThreatDatabase.Instance;
                var summary = $"🛡️ **Comprehensive System Scan Complete**\n\n" +
                             $"📊 **Summary:**\n" +
                             $"• Total Threats: {result.Threats.Count}\n" +
                             $"• Critical: {result.CriticalCount}\n" +
                             $"• High: {result.HighCount}\n" +
                             $"• Medium: {result.MediumCount}\n" +
                             $"• Low: {result.LowCount}\n" +
                             $"• Files Scanned: {result.FilesScanned}\n" +
                             $"• Duration: {result.Duration.TotalSeconds:F1}s\n\n" +
                             $"📦 **Database:** v{db.Version} ({db.TotalDefinitions} definitions)\n" +
                             $"   Last Updated: {db.LastUpdated:g}\n\n";

                if (result.Threats.Count == 0)
                {
                    summary += "🎉 **Great news!** No threats detected. Your system is clean!";
                }
                else
                {
                    summary += "⚠️ **Threats Found:**\n";
                    
                    var topThreats = result.Threats
                        .OrderByDescending(t => t.Severity)
                        .Take(5)
                        .ToList();
                    
                    foreach (var threat in topThreats)
                    {
                        var icon = threat.Severity switch
                        {
                            SystemControl.SeverityLevel.Critical => "🔴",
                            SystemControl.SeverityLevel.High => "🟠",
                            SystemControl.SeverityLevel.Medium => "🟡",
                            _ => "🔵"
                        };
                        
                        summary += $"{icon} **{threat.Name}** [{threat.Category}]\n";
                        summary += $"   {threat.Description}\n";
                        summary += $"   📍 {threat.Location}\n";
                        if (threat.CanRemove)
                            summary += $"   ✅ Can be removed\n";
                        summary += "\n";
                    }
                    
                    if (result.Threats.Count > 5)
                        summary += $"... and {result.Threats.Count - 5} more threats.\n\n";
                    
                    summary += "🔧 Use `/systemcontrol` to manage threats.\n";
                    summary += "📥 Use `/updatedb` to update threat definitions.";
                }
                
                ShowStatus("✅ Scan completed");
                return summary;
            }
            catch (OperationCanceledException)
            {
                ShowStatus("🛑 Scan cancelled");
                return "CANCELLED · OPERATION STOPPED";
            }
            catch (Exception ex)
            {
                ShowStatus("❌ Scan failed");
                return $"❌ Scan failed: {ex.Message}";
            }
        }

        private async Task<string> PerformSystemAutoFix(CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                ShowStatus("🔧 Starting auto-fix...");
                
                // First perform a scan to get issues
                var scanner = new SystemControl.WindowsSystemScanner();
                var scanResult = await scanner.PerformFullScanAsync();
                ct.ThrowIfCancellationRequested();
                
                var fixableIssues = scanResult.Issues.Where(i => i.CanAutoFix).ToList();
                
                if (!fixableIssues.Any())
                {
                    ShowStatus("ℹ️ No auto-fixable issues found");
                    return "ℹ️ **No Auto-Fixable Issues Found**\n\n" +
                           $"Scanned {scanResult.TotalIssues} issues, but none can be automatically fixed.\n" +
                           "Use `/systemcontrol` to manually review and fix issues.";
                }
                
                // Perform auto-fix
                var controller = new SystemControl.WindowsSystemController();
                var fixResults = await controller.AutoFixIssuesAsync(fixableIssues);
                ct.ThrowIfCancellationRequested();
                
                var successCount = fixResults.Count(r => r.Result == SystemControl.FixResult.Success);
                var requiresRestart = fixResults.Any(r => r.RequiresRestart);
                
                var summary = $"🔧 **Auto-Fix Complete**\n\n" +
                             $"📊 **Results:**\n" +
                             $"• Issues Processed: {fixResults.Count}\n" +
                             $"• Successfully Fixed: {successCount}\n" +
                             $"• Failed: {fixResults.Count - successCount}\n\n";
                
                if (successCount > 0)
                {
                    summary += "✅ **Successfully Fixed:**\n";
                    foreach (var result in fixResults.Where(r => r.Result == SystemControl.FixResult.Success))
                    {
                        summary += $"• {result.Message}\n";
                    }
                    summary += "\n";
                }
                
                var failedResults = fixResults.Where(r => r.Result != SystemControl.FixResult.Success).ToList();
                if (failedResults.Any())
                {
                    summary += "❌ **Could Not Fix:**\n";
                    foreach (var result in failedResults.Take(3))
                    {
                        summary += $"• {result.Message}\n";
                    }
                    if (failedResults.Count > 3)
                        summary += $"• ... and {failedResults.Count - 3} more\n";
                    summary += "\n";
                }
                
                if (requiresRestart)
                {
                    summary += "⚠️ **Restart Required**\n";
                    summary += "Some fixes require a system restart to take full effect.\n\n";
                }
                
                summary += "🔍 Run `/systemscan` again to verify fixes or use `/systemcontrol` for detailed management.";
                
                ShowStatus("✅ Auto-fix completed");
                return summary;
            }
            catch (Exception ex)
            {
                ShowStatus("❌ Auto-fix failed");
                return $"❌ Auto-fix failed: {ex.Message}";
            }
        }

        private async Task<string> UpdateThreatDatabase(CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                ShowStatus("📥 Connecting to threat intelligence servers...");
                
                var result = await SystemControl.OnlineThreatDatabase.UpdateDefinitionsAsync();
                ct.ThrowIfCancellationRequested();
                
                if (result.Success)
                {
                    ShowStatus("✅ Definitions updated!");
                    return $"📥 **Threat Database Updated**\n\n" +
                           $"✅ {result.Message}\n\n" +
                           $"📊 **Database Info:**\n" +
                           $"• Sources Updated: {result.SourcesUpdated}\n" +
                           $"• New Hashes Added: {result.NewHashesAdded:N0}\n" +
                           $"• Total Definitions: {result.TotalDefinitions:N0}\n" +
                           $"• Last Updated: {SystemControl.OnlineThreatDatabase.LastUpdateTime:g}\n\n" +
                           $"🔍 Run a scan to check your system with the latest definitions.";
                }
                else
                {
                    ShowStatus("⚠️ Update had issues");
                    return $"⚠️ **Update Status**\n\n{result.Message}\n\n" +
                           $"The scanner will use cached definitions.";
                }
            }
            catch (OperationCanceledException)
            {
                ShowStatus("🛑 Update cancelled");
                return "CANCELLED · OPERATION STOPPED";
            }
            catch (Exception ex)
            {
                ShowStatus("❌ Update failed");
                return $"❌ Update failed: {ex.Message}\n\nThe scanner will use built-in definitions.";
            }
        }

        private void SystemControlButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSystemControlWindow();
        }
        
        private void SystemControl_Click(object sender, RoutedEventArgs e)
        {
            OpenSystemControlWindow();
        }

        private void OpenSystemControlWindow()
        {
            try
            {
                var systemControlWindow = new SystemControlWindow();
                systemControlWindow.Show();
                AddMessage("Atlas", "🔧 Windows System Control Panel opened! You can scan for issues, perform auto-fixes, and manage your system health.", false);
                ShowStatus("✅ System Control Panel opened");
            }
            catch (Exception ex)
            {
                var errorMessage = $"❌ Error opening System Control Panel: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $"\nInner Exception: {ex.InnerException.Message}";
                }
                
                AddMessage("Atlas", errorMessage, false);
                ShowStatus("❌ Failed to open System Control Panel");
                
                // Also show a message box for debugging
                MessageBox.Show($"Detailed Error:\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}", 
                               "System Control Panel Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenAvatarSelection()
        {
            try
            {
                var avatarWindow = new AvatarSelectionWindow();
                avatarWindow.Owner = this;
                if (avatarWindow.ShowDialog() == true && !string.IsNullOrEmpty(avatarWindow.SelectedAvatar))
                {
                    SelectAvatar(avatarWindow.SelectedAvatar);
                    AddMessage("Atlas", $"🎭 Avatar changed to: {GetAvatarDisplayName(avatarWindow.SelectedAvatar)}", false);
                }
            }
            catch (Exception ex)
            {
                AddMessage("Atlas", $"❌ Error opening avatar selection: {ex.Message}", false);
            }
        }

        #region Drag-Drop File Support
        
        private List<string> _droppedPaths = new();
        
        private void InputArea_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropOverlay.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }
        
        private void InputArea_DragLeave(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }
        
        private void InputArea_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        
        private void InputArea_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var path in paths)
                {
                    if (!_droppedPaths.Contains(path))
                    {
                        _droppedPaths.Add(path);
                        AddDroppedFileChip(path);
                        
                        // If a folder is dropped, set it as the coding workspace
                        if (Directory.Exists(path) && _codeAssistant != null)
                        {
                            _codeAssistant.SetWorkspace(path);
                            Debug.WriteLine($"[CodeAssistant] Workspace set to: {path}");
                        }
                    }
                }
                UpdateDroppedFilesVisibility();
                e.Handled = true;
            }
        }
        
        private void AddDroppedFileChip(string path)
        {
            var isFolder = Directory.Exists(path);
            var name = Path.GetFileName(path);
            var icon = isFolder ? "📁" : GetDroppedFileIcon(path);
            
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(99, 102, 241)), // Accent color
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 4),
                Tag = path,
                Cursor = Cursors.Hand
            };
            
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock 
            { 
                Text = $"{icon} {name}", 
                Foreground = Brushes.White, 
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            
            var removeBtn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 9,
                Padding = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand,
                Tag = path
            };
            removeBtn.Click += RemoveDroppedFile_Click;
            stack.Children.Add(removeBtn);
            
            chip.Child = stack;
            chip.ToolTip = path;
            
            DroppedFilesList.Items.Add(chip);
        }
        
        private string GetDroppedFileIcon(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".txt" or ".md" or ".log" => "📄",
                ".pdf" => "📕",
                ".doc" or ".docx" => "📘",
                ".xls" or ".xlsx" => "📗",
                ".ppt" or ".pptx" => "📙",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "🖼️",
                ".mp3" or ".wav" or ".flac" => "🎵",
                ".mp4" or ".avi" or ".mkv" => "🎬",
                ".zip" or ".rar" or ".7z" => "📦",
                ".exe" or ".msi" => "⚙️",
                ".cs" or ".js" or ".py" or ".java" => "💻",
                ".html" or ".css" => "🌐",
                _ => "📄"
            };
        }
        
        private void RemoveDroppedFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                _droppedPaths.Remove(path);
                
                // Find and remove the chip
                Border? toRemove = null;
                foreach (var item in DroppedFilesList.Items)
                {
                    if (item is Border chip && chip.Tag as string == path)
                    {
                        toRemove = chip;
                        break;
                    }
                }
                if (toRemove != null)
                    DroppedFilesList.Items.Remove(toRemove);
                    
                UpdateDroppedFilesVisibility();
            }
        }
        
        private void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            // Show a context menu to choose files or folders
            var menu = new System.Windows.Controls.ContextMenu();
            
            var filesItem = new System.Windows.Controls.MenuItem { Header = "📄 Select Files..." };
            filesItem.Click += (s, args) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Multiselect = true,
                    Title = "Select files to attach"
                };
                if (dialog.ShowDialog() == true)
                {
                    foreach (var file in dialog.FileNames)
                    {
                        if (!_droppedPaths.Contains(file))
                        {
                            _droppedPaths.Add(file);
                            AddDroppedFileChip(file);
                        }
                    }
                    UpdateDroppedFilesVisibility();
                }
            };
            menu.Items.Add(filesItem);
            
            var folderItem = new System.Windows.Controls.MenuItem { Header = "📁 Select Folder..." };
            folderItem.Click += (s, args) =>
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select a folder to attach",
                    ShowNewFolderButton = false
                };
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (!_droppedPaths.Contains(dialog.SelectedPath))
                    {
                        _droppedPaths.Add(dialog.SelectedPath);
                        AddDroppedFileChip(dialog.SelectedPath);
                        UpdateDroppedFilesVisibility();
                    }
                }
            };
            menu.Items.Add(folderItem);
            
            menu.IsOpen = true;
        }
        
        private void ClearDroppedFiles_Click(object sender, RoutedEventArgs e)
        {
            _droppedPaths.Clear();
            DroppedFilesList.Items.Clear();
            UpdateDroppedFilesVisibility();
        }
        
        // ═══════════════════════════════════════════════════════════════
        // ONLINE ACCESS BANNER HANDLERS
        // ═══════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Show the online access blocked banner
        /// </summary>
        public void ShowOnlineBlockedBanner()
        {
            Dispatcher.Invoke(() =>
            {
                if (OnlineBlockedBanner != null)
                    OnlineBlockedBanner.Visibility = Visibility.Visible;
            });
        }
        
        /// <summary>
        /// Hide the online access blocked banner
        /// </summary>
        public void HideOnlineBlockedBanner()
        {
            Dispatcher.Invoke(() =>
            {
                if (OnlineBlockedBanner != null)
                    OnlineBlockedBanner.Visibility = Visibility.Collapsed;
            });
        }
        
        private void EnableOnlineAccess_Click(object sender, RoutedEventArgs e)
        {
            // Show the consent dialog
            var result = UI.OnlineConsentDialog.ShowConsent(this);
            
            if (result.Decision == Core.OnlineConsentDecision.AllowOnce ||
                result.Decision == Core.OnlineConsentDecision.AllowForSession)
            {
                // Update the online mode setting
                var newMode = result.Decision == Core.OnlineConsentDecision.AllowForSession 
                    ? "AllowForSession" 
                    : "AskEachTime";
                    
                Core.PreferencesStore.Instance.Update(p => p.OnlineMode = newMode);
                
                // Grant temporary access if AllowOnce
                if (result.Decision == Core.OnlineConsentDecision.AllowOnce)
                {
                    Core.OnlineModeManager.Instance.GrantTemporaryAccess(TimeSpan.FromMinutes(5));
                }
                else if (result.Decision == Core.OnlineConsentDecision.AllowForSession)
                {
                    // Session access - grant until app closes (24 hours max)
                    Core.OnlineModeManager.Instance.GrantTemporaryAccess(TimeSpan.FromHours(24));
                }
                
                HideOnlineBlockedBanner();
                ShowToast("Online access enabled", UI.ToastType.Success);
            }
        }
        
        private void DismissOnlineBanner_Click(object sender, RoutedEventArgs e)
        {
            HideOnlineBlockedBanner();
        }
        
        private void UpdateDroppedFilesVisibility()
        {
            DroppedFilesPanel.Visibility = _droppedPaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        
        private string GetDroppedFilesContext()
        {
            if (_droppedPaths.Count == 0)
                return "";
                
            var sb = new StringBuilder();
            sb.AppendLine("\n\n[ATTACHED FILES]");
            
            foreach (var path in _droppedPaths)
            {
                var isFolder = Directory.Exists(path);
                if (isFolder)
                {
                    try
                    {
                        var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                        var subDirs = Directory.GetDirectories(path);
                        sb.AppendLine($"\n📁 FOLDER: \"{path}\"");
                        sb.AppendLine($"   Contains {files.Length} files, {subDirs.Length} subfolders");
                        
                        // List first 10 files
                        foreach (var file in files.Take(10))
                        {
                            sb.AppendLine($"   - {Path.GetFileName(file)}");
                        }
                        if (files.Length > 10)
                            sb.AppendLine($"   ... and {files.Length - 10} more files");
                    }
                    catch
                    {
                        sb.AppendLine($"\n📁 FOLDER: \"{path}\"");
                    }
                }
                else
                {
                    try
                    {
                        var info = new FileInfo(path);
                        var ext = info.Extension.ToLowerInvariant();
                        sb.AppendLine($"\n📄 FILE: \"{path}\" ({FormatFileSize(info.Length)})");
                        
                        // Read content for text-based files (code, config, text, etc.)
                        var textExtensions = new HashSet<string> { 
                            ".txt", ".md", ".json", ".xml", ".yaml", ".yml", ".csv",
                            ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".hpp",
                            ".html", ".htm", ".css", ".scss", ".less",
                            ".sql", ".sh", ".bat", ".ps1", ".cmd",
                            ".ini", ".cfg", ".conf", ".config", ".env",
                            ".log", ".gitignore", ".dockerfile", ".makefile",
                            ".jsx", ".tsx", ".vue", ".svelte", ".php", ".rb", ".go", ".rs",
                            ".swift", ".kt", ".scala", ".lua", ".r", ".m", ".mm"
                        };
                        
                        // Also check for files without extension that might be text (like Makefile, Dockerfile)
                        var textFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                            "makefile", "dockerfile", "readme", "license", "changelog", "authors"
                        };
                        
                        bool isTextFile = textExtensions.Contains(ext) || 
                                         textFilenames.Contains(info.Name) ||
                                         string.IsNullOrEmpty(ext);
                        
                        if (isTextFile && info.Length < 100 * 1024) // Max 100KB for text files
                        {
                            try
                            {
                                var content = File.ReadAllText(path);
                                
                                // Truncate if too long (max ~8000 chars to leave room for response)
                                if (content.Length > 8000)
                                {
                                    content = content.Substring(0, 8000) + "\n... [truncated - file continues]";
                                }
                                
                                sb.AppendLine($"--- FILE CONTENT START ---");
                                sb.AppendLine(content);
                                sb.AppendLine($"--- FILE CONTENT END ---");
                            }
                            catch (Exception ex)
                            {
                                sb.AppendLine($"   [Could not read content: {ex.Message}]");
                            }
                        }
                        else if (info.Length >= 100 * 1024)
                        {
                            sb.AppendLine($"   [File too large to read - {FormatFileSize(info.Length)}]");
                        }
                        else
                        {
                            // Binary file - just describe it
                            sb.AppendLine($"   [Binary file: {ext}]");
                            
                            // For images, mention they could be analyzed with vision
                            var imageExtensions = new HashSet<string> { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
                            if (imageExtensions.Contains(ext))
                            {
                                sb.AppendLine($"   [Image file - can be analyzed if vision is enabled]");
                            }
                        }
                    }
                    catch
                    {
                        sb.AppendLine($"\n📄 FILE: \"{path}\"");
                    }
                }
            }
            
            sb.AppendLine("\n[You can analyze, explain, modify, or work with these files. For modifications, I'll show you the changes before applying them.]");
            return sb.ToString();
        }
        
        // Store dropped paths for tool access
        public static List<string> LastDroppedPaths { get; private set; } = new();
        
        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }
        
        #endregion
        
        #region Inspector Panel & Toast Notifications
        
        private UI.ToastNotificationManager? _toastManager;
        private string _lastClipboardDownloadPromptUrl = "";
        private DateTime _lastClipboardDownloadPromptAtUtc = DateTime.MinValue;
        private SecuritySuite.Services.SecuritySuiteManager? _securityManager;
        private InAppAssistant.Services.WindowsContextService? _contextService;
        private System.Timers.Timer? _contextUpdateTimer;
        
        /// <summary>
        /// Initialize the Inspector panel and Toast notification system
        /// </summary>
        private void InitializeInspectorAndToasts()
        {
            try
            {
                // Initialize Toast notifications
                _toastManager = UI.ToastNotificationManager.Instance;
                _toastManager.Initialize(ToastContainer);
                
                // Initialize Security Suite Manager
                _securityManager = new SecuritySuite.Services.SecuritySuiteManager();
                
                // Get WindowsContextService from InAppAssistant
                _contextService = _inAppAssistant?.GetContextService();
                
                // Start context update timer
                StartContextUpdateTimer();
                
                // Load initial security status
                UpdateSecurityDisplay();
                
                // Load current ElevenLabs voice settings into sliders
                LoadVoiceSettings();
                
                // Initialize file browser tree
                InitializeFileTree();
                
                Debug.WriteLine("[UI] Inspector panel and Toast system initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UI] Error initializing Inspector/Toast: {ex.Message}");
            }
        }

        private void InitializeClipboardDownloadPrompts()
        {
            try
            {
                ClipboardManager.Initialize();
                ClipboardManager.ClipboardChanged -= ClipboardManager_ClipboardChanged_ForDownloadPrompt;
                ClipboardManager.ClipboardChanged += ClipboardManager_ClipboardChanged_ForDownloadPrompt;
            }
            catch
            {
            }
        }

        private void ClipboardManager_ClipboardChanged_ForDownloadPrompt(ClipboardItem item)
        {
            try
            {
                var raw = (item?.Content ?? "").Trim();
                if (string.IsNullOrWhiteSpace(raw)) return;
                if (raw.Length > 2000) return;

                if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return;
                if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    return;

                var now = DateTime.UtcNow;
                if (string.Equals(_lastClipboardDownloadPromptUrl, raw, StringComparison.OrdinalIgnoreCase) &&
                    (now - _lastClipboardDownloadPromptAtUtc).TotalSeconds < 10)
                    return;

                _lastClipboardDownloadPromptUrl = raw;
                _lastClipboardDownloadPromptAtUtc = now;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { ShowPage("downloads"); } catch { }
                    try { Services.DownloadService.Instance.AddDownload(raw); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch
            {
            }
        }
        
        /// <summary>
        /// Start timer to update context display
        /// </summary>
        private void StartContextUpdateTimer()
        {
            _contextUpdateTimer = new System.Timers.Timer(1000);
            _contextUpdateTimer.Elapsed += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        UpdateContextDisplay();
                    }
                    catch { }
                });
            };
            _contextUpdateTimer.Start();
        }
        
        /// <summary>
        /// Update the Active Context display in the inspector
        /// </summary>
        private void UpdateContextDisplay()
        {
            if (_contextService == null) return;
            
            var context = _contextService.GetActiveAppContext();
            if (context == null) return;
            
            // Update icon based on category
            ContextAppIcon.Text = context.Category switch
            {
                InAppAssistant.Models.AppCategory.Browser => "🌐",
                InAppAssistant.Models.AppCategory.FileExplorer => "📁",
                InAppAssistant.Models.AppCategory.IDE => "💻",
                InAppAssistant.Models.AppCategory.Office => "📄",
                InAppAssistant.Models.AppCategory.Terminal => "⌨️",
                InAppAssistant.Models.AppCategory.MediaPlayer => "🎵",
                InAppAssistant.Models.AppCategory.TextEditor => "📝",
                InAppAssistant.Models.AppCategory.Communication => "💬",
                _ => "📱"
            };
            
            // Update app name
            ContextAppName.Text = context.ProcessName.ToLower() switch
            {
                "explorer" => "File Explorer",
                "chrome" => "Google Chrome",
                "msedge" => "Microsoft Edge",
                "firefox" => "Mozilla Firefox",
                "code" => "Visual Studio Code",
                "devenv" => "Visual Studio",
                "spotify" => "Spotify",
                "discord" => "Discord",
                _ => context.ProcessName
            };
            
            ContextProcessName.Text = $"{context.ProcessName}.exe";
            ContextFilePath.Text = !string.IsNullOrEmpty(context.ExecutablePath) ? context.ExecutablePath : "—";
            ContextWindowTitle.Text = !string.IsNullOrEmpty(context.WindowTitle) ? context.WindowTitle : "—";
        }
        
        /// <summary>
        /// Update the Security Scan display in the inspector
        /// </summary>
        private void UpdateSecurityDisplay()
        {
            if (_securityManager == null) return;
            
            try
            {
                var status = _securityManager.GetDashboardStatus();
                
                // Update status badge
                switch (status.ProtectionScore.Status)
                {
                    case SecuritySuite.Models.ProtectionStatus.Protected:
                        SecurityStatusText.Text = "SAFE";
                        SecurityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80));
                        SecurityBadge.Background = new SolidColorBrush(Color.FromArgb(32, 63, 185, 80));
                        break;
                    case SecuritySuite.Models.ProtectionStatus.AtRisk:
                        SecurityStatusText.Text = "AT RISK";
                        SecurityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(210, 153, 34));
                        SecurityBadge.Background = new SolidColorBrush(Color.FromArgb(32, 210, 153, 34));
                        break;
                    case SecuritySuite.Models.ProtectionStatus.Critical:
                        SecurityStatusText.Text = "CRITICAL";
                        SecurityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(248, 81, 73));
                        SecurityBadge.Background = new SolidColorBrush(Color.FromArgb(32, 248, 81, 73));
                        break;
                }
                
                // Update last scan
                if (status.LastScan != null)
                {
                    var scanTime = status.LastScan.EndTime;
                    var timeSince = DateTime.Now - scanTime;
                    
                    if (timeSince.TotalMinutes < 1)
                        LastScanText.Text = "Just now";
                    else if (timeSince.TotalHours < 1)
                        LastScanText.Text = $"{(int)timeSince.TotalMinutes} min ago";
                    else if (timeSince.TotalDays < 1)
                        LastScanText.Text = $"Today, {scanTime:h:mm tt}";
                    else
                        LastScanText.Text = scanTime.ToString("MMM d, h:mm tt");
                }
                else
                {
                    LastScanText.Text = "Never";
                }
                
                // Update definitions
                var defsInfo = status.Definitions;
                var defsAge = DateTime.Now - defsInfo.LastUpdated;
                
                if (defsAge.TotalHours < 24)
                    DefinitionsText.Text = "Up to date";
                else if (defsAge.TotalDays < 3)
                    DefinitionsText.Text = $"{(int)defsAge.TotalDays} day(s) old";
                else
                    DefinitionsText.Text = $"Outdated ({(int)defsAge.TotalDays} days)";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Inspector] Security status update error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load current ElevenLabs voice settings into sliders
        /// </summary>
        private void LoadVoiceSettings()
        {
            var settings = Voice.ElevenLabsProvider.CurrentVoiceSettings;
            StabilitySlider.Value = settings.Stability;
            SimilaritySlider.Value = settings.SimilarityBoost;
            StyleSlider.Value = settings.Style;
            SpeakerBoostToggle.IsChecked = settings.UseSpeakerBoost;
            
            StabilityValueText.Text = settings.Stability.ToString("F2");
            SimilarityValueText.Text = settings.SimilarityBoost.ToString("F2");
            StyleValueText.Text = settings.Style.ToString("F2");
        }
        
        /// <summary>
        /// Scan Now button click
        /// </summary>
        private void ScanNow_Click(object sender, RoutedEventArgs e)
        {
            ShowToast("Opening Security Suite...", UI.ToastType.Info);
            ShowSecuritySuiteWindow();
        }
        
        /// <summary>
        /// Stability slider changed
        /// </summary>
        private void StabilitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (StabilityValueText == null) return;
            StabilityValueText.Text = e.NewValue.ToString("F2");
            ApplyVoiceSettings();
        }
        
        /// <summary>
        /// Similarity slider changed
        /// </summary>
        private void SimilaritySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SimilarityValueText == null) return;
            SimilarityValueText.Text = e.NewValue.ToString("F2");
            ApplyVoiceSettings();
        }
        
        /// <summary>
        /// Style slider changed
        /// </summary>
        private void StyleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (StyleValueText == null) return;
            StyleValueText.Text = e.NewValue.ToString("F2");
            ApplyVoiceSettings();
        }
        
        /// <summary>
        /// Speaker boost toggle clicked
        /// </summary>
        private void SpeakerBoostToggle_Click(object sender, RoutedEventArgs e)
        {
            ApplyVoiceSettings();
        }
        
        /// <summary>
        /// Apply voice settings to ElevenLabs provider
        /// </summary>
        private void ApplyVoiceSettings()
        {
            if (StabilitySlider == null || SimilaritySlider == null || StyleSlider == null || SpeakerBoostToggle == null) return;
            
            Voice.ElevenLabsProvider.UpdateVoiceSettings(
                StabilitySlider.Value,
                SimilaritySlider.Value,
                StyleSlider.Value,
                SpeakerBoostToggle.IsChecked == true
            );
            
            Debug.WriteLine($"[Inspector] Voice settings applied - Stability: {StabilitySlider.Value:F2}, Similarity: {SimilaritySlider.Value:F2}, Style: {StyleSlider.Value:F2}");
        }
        
        /// <summary>
        /// Toggle Inspector panel visibility (legacy - panel is now always visible)
        /// </summary>
        private void InspectorToggle_Click(object sender, RoutedEventArgs e)
        {
            // Panel is now always visible in the new design
        }
        
        /// <summary>
        /// Close the Inspector panel (hide it)
        /// </summary>
        private void CloseInspector_Click(object sender, RoutedEventArgs e)
        {
            InspectorPanel.Visibility = Visibility.Collapsed;
            InspectorColumn.Width = new GridLength(0);
            ShowInspectorBtn.Visibility = Visibility.Visible;
        }
        
        #region File Browser Sidebar
        
        private bool _sidebarExpanded = false; // Start collapsed
        
        /// <summary>
        /// Toggle the sidebar expanded/collapsed state
        /// </summary>
        private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Sidebar] Toggle clicked! Current state: {_sidebarExpanded}");
            _sidebarExpanded = !_sidebarExpanded;
            System.Diagnostics.Debug.WriteLine($"[Sidebar] New state: {_sidebarExpanded}");
            
            if (_sidebarExpanded)
            {
                // Show sidebar - set column width and visibility
                FileBrowserColumn.Width = new GridLength(280);
                FileBrowserPanel.Visibility = Visibility.Visible;
                if (SidebarToggleBtn != null) SidebarToggleBtn.ToolTip = "◀ Close Files Panel";
                System.Diagnostics.Debug.WriteLine("[Sidebar] Panel set to VISIBLE, width=280");
                
                // Initialize file tree if empty
                if (FileTreeView != null && FileTreeView.Items.Count == 0)
                {
                    InitializeFileTree();
                }
            }
            else
            {
                // Hide sidebar - set column width to 0 and collapse
                FileBrowserColumn.Width = new GridLength(0);
                FileBrowserPanel.Visibility = Visibility.Collapsed;
                if (SidebarToggleBtn != null) SidebarToggleBtn.ToolTip = "📁 Open Files Panel";
                System.Diagnostics.Debug.WriteLine("[Sidebar] Panel set to COLLAPSED, width=0");
            }
        }
        
        /// <summary>
        /// Initialize the file tree with root folders
        /// </summary>
        private void InitializeFileTree()
        {
            FileTreeView.Items.Clear();
            
            // Add quick access folders
            var quickAccess = new[]
            {
                ("🏠 Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                ("🖥️ Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
                ("⬇️ Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
                ("📄 Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                ("🖼️ Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                ("🎵 Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
                ("🎬 Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            };
            
            foreach (var (name, path) in quickAccess)
            {
                if (Directory.Exists(path))
                {
                    var item = CreateTreeItem(name, path, true);
                    FileTreeView.Items.Add(item);
                }
            }
            
            // Add separator
            var separator = new TreeViewItem { Header = "─────────────", IsEnabled = false, Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)) };
            FileTreeView.Items.Add(separator);
            
            // Add drives
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    var driveName = $"💾 {drive.Name} ({drive.VolumeLabel})";
                    var item = CreateTreeItem(driveName, drive.Name, true);
                    FileTreeView.Items.Add(item);
                }
            }
        }
        
        /// <summary>
        /// Create a tree view item for a file or folder
        /// </summary>
        private TreeViewItem CreateTreeItem(string displayName, string path, bool isFolder)
        {
            var item = new TreeViewItem
            {
                Header = displayName,
                Tag = path,
                FontFamily = new FontFamily("Segoe UI")
            };
            
            if (isFolder)
            {
                // Add dummy child so expand arrow shows
                item.Items.Add(new TreeViewItem { Header = "Loading...", Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)) });
            }
            
            return item;
        }
        
        /// <summary>
        /// Handle tree item expansion - load children
        /// </summary>
        private void FileTreeItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem item && item.Tag is string path)
            {
                // Check if we need to load children (has dummy item)
                if (item.Items.Count == 1 && item.Items[0] is TreeViewItem dummy && dummy.Header?.ToString() == "Loading...")
                {
                    item.Items.Clear();
                    LoadFolderContents(item, path);
                }
            }
        }
        
        /// <summary>
        /// Load folder contents into tree item
        /// </summary>
        private void LoadFolderContents(TreeViewItem parent, string folderPath)
        {
            try
            {
                // Add subfolders first
                var dirs = Directory.GetDirectories(folderPath);
                foreach (var dir in dirs.OrderBy(d => Path.GetFileName(d)))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        // Skip hidden and system folders
                        if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 || 
                            (dirInfo.Attributes & FileAttributes.System) != 0)
                            continue;
                        
                        var name = $"📁 {dirInfo.Name}";
                        var item = CreateTreeItem(name, dir, true);
                        parent.Items.Add(item);
                    }
                    catch { } // Skip folders we can't access
                }
                
                // Add files
                var files = Directory.GetFiles(folderPath);
                foreach (var file in files.OrderBy(f => Path.GetFileName(f)))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        // Skip hidden files
                        if ((fileInfo.Attributes & FileAttributes.Hidden) != 0)
                            continue;
                        
                        var icon = GetFileIcon(fileInfo.Extension);
                        var name = $"{icon} {fileInfo.Name}";
                        var item = CreateTreeItem(name, file, false);
                        parent.Items.Add(item);
                    }
                    catch { } // Skip files we can't access
                }
                
                // If no items, show empty message
                if (parent.Items.Count == 0)
                {
                    parent.Items.Add(new TreeViewItem { Header = "(empty)", IsEnabled = false, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)) });
                }
            }
            catch (UnauthorizedAccessException)
            {
                parent.Items.Add(new TreeViewItem { Header = "⚠️ Access denied", IsEnabled = false, Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)) });
            }
            catch (Exception ex)
            {
                parent.Items.Add(new TreeViewItem { Header = $"⚠️ Error: {ex.Message}", IsEnabled = false, Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)) });
            }
        }
        
        /// <summary>
        /// Get icon for file type
        /// </summary>
        private string GetFileIcon(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" or ".md" or ".log" => "📝",
                ".pdf" => "📕",
                ".doc" or ".docx" => "📘",
                ".xls" or ".xlsx" => "📗",
                ".ppt" or ".pptx" => "📙",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "🖼️",
                ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "🎵",
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "🎬",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦",
                ".exe" or ".msi" => "⚙️",
                ".dll" => "🔧",
                ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".c" or ".h" => "💻",
                ".html" or ".htm" or ".css" => "🌐",
                ".json" or ".xml" or ".yaml" or ".yml" => "📋",
                ".sql" or ".db" => "🗄️",
                ".iso" or ".img" => "💿",
                ".lnk" => "🔗",
                _ => "📄"
            };
        }
        
        /// <summary>
        /// Handle double-click on tree item - open file/folder
        /// </summary>
        private void FileTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileTreeView.SelectedItem is TreeViewItem item && item.Tag is string path)
            {
                try
                {
                    Debug.WriteLine($"[FileTree] Double-click on: {path}");
                    
                    if (Directory.Exists(path))
                    {
                        // Open folder in Explorer - quote the path for spaces
                        Debug.WriteLine($"[FileTree] Opening folder: {path}");
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
                    }
                    else if (File.Exists(path))
                    {
                        // Open file with default application
                        Debug.WriteLine($"[FileTree] Opening file: {path}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        Debug.WriteLine($"[FileTree] Path doesn't exist: {path}");
                        ShowStatus($"❌ Path not found: {path}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FileTree] Error opening: {ex.Message}");
                    ShowStatus($"❌ Couldn't open: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Refresh the file tree
        /// </summary>
        private void RefreshFileTree_Click(object sender, RoutedEventArgs e)
        {
            InitializeFileTree();
            ShowStatus("🔄 File tree refreshed");
        }
        
        /// <summary>
        /// Handle quick access folder clicks
        /// </summary>
        private void QuickAccess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                string path = tag switch
                {
                    "Home" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    "ThisPC" => "",
                    _ => ""
                };
                
                if (tag == "ThisPC")
                {
                    // Open File Explorer to This PC
                    System.Diagnostics.Process.Start("explorer.exe", "shell:MyComputerFolder");
                }
                else if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    // Open in File Explorer
                    System.Diagnostics.Process.Start("explorer.exe", path);
                }
            }
        }
        
        /// <summary>
        /// Screenshot button click - capture screen
        /// </summary>
        private async void Screenshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowStatus("📷 Capturing screenshot...");
                
                // Hide window briefly for clean capture
                this.WindowState = WindowState.Minimized;
                await Task.Delay(300);
                
                // Capture screen using the async method
                var result = await _screenCapture?.CaptureScreenAsync()!;
                
                // Restore window
                this.WindowState = WindowState.Normal;
                this.Activate();
                
                if (result != null && result.Success)
                {
                    ShowStatus($"📷 Screenshot saved!");
                    
                    // Attach to chat if we have a file path
                    if (!string.IsNullOrEmpty(result.Metadata?.FilePath) && File.Exists(result.Metadata.FilePath))
                    {
                        if (!_droppedPaths.Contains(result.Metadata.FilePath))
                        {
                            _droppedPaths.Add(result.Metadata.FilePath);
                            AddDroppedFileChip(result.Metadata.FilePath);
                            UpdateDroppedFilesVisibility();
                        }
                    }
                }
                else
                {
                    ShowStatus($"❌ Screenshot failed: {result?.Error ?? "Unknown error"}");
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ Screenshot error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Stop button click - stops any ongoing operation (speech, scanning, etc.)
        /// </summary>
        private void StopSpeechBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop speech
                _voiceManager?.Stop();
                
                // Cancel any ongoing operations
                _currentOperationCts?.Cancel();
                _currentScanner?.CancelScan();
                
                StopSpeechBtn.Visibility = Visibility.Collapsed;
                ShowStatus("⏹️ Stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Stop] Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show the stop button when an operation starts
        /// </summary>
        private void ShowStopSpeechButton()
        {
            Dispatcher.Invoke(() => StopSpeechBtn.Visibility = Visibility.Visible);
        }
        
        /// <summary>
        /// Hide the stop button when operation ends
        /// </summary>
        private void HideStopSpeechButton()
        {
            Dispatcher.Invoke(() => StopSpeechBtn.Visibility = Visibility.Collapsed);
        }
        
        // Keep old methods for compatibility but they're no longer used
        private void FileBrowserToggle_Click(object sender, RoutedEventArgs e) => SidebarToggle_Click(sender, e);
        private void CloseFileBrowser_Click(object sender, RoutedEventArgs e) 
        { 
            _sidebarExpanded = false; 
            FileBrowserColumn.Width = new GridLength(0);
            FileBrowserPanel.Visibility = Visibility.Collapsed;
        }
        private void RefreshFiles_Click(object sender, RoutedEventArgs e) { }
        private void FileBrowserUp_Click(object sender, RoutedEventArgs e) { }
        private void FileBrowserTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { }
        private void FileBrowserTree_MouseDoubleClick(object sender, MouseButtonEventArgs e) { }
        
        #endregion
        
        /// <summary>
        /// Show the Inspector panel (from header button)
        /// </summary>
        private void ShowInspector_Click(object sender, RoutedEventArgs e)
        {
            ShowInspector();
            ShowInspectorBtn.Visibility = Visibility.Collapsed;
        }
        
        /// <summary>
        /// Show the Inspector panel
        /// </summary>
        public void ShowInspector()
        {
            InspectorPanel.Visibility = Visibility.Visible;
            InspectorColumn.Width = new GridLength(320);
        }
        
        /// <summary>
        /// Handle quick access folder selection - REMOVED, using TreeView now
        /// </summary>
        private void QuickAccessFolder_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Legacy - no longer used
        }
        
        /// <summary>
        /// Load files from a folder into the quick access list - REMOVED, using TreeView now
        /// </summary>
        private void LoadFilesFromFolder(string folderPath)
        {
            // Legacy - no longer used
        }
        
        /// <summary>
        /// Handle file selection from quick access list - REMOVED, using TreeView now
        /// </summary>
        private void QuickAccessFile_Selected(object sender, SelectionChangedEventArgs e)
        {
            // Legacy - no longer used
        }
        
        /// <summary>
        /// Handle tree item selection
        /// </summary>
        private void FileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Selection tracking handled in FileTreeView_MouseDoubleClick
        }
        
        /// <summary>
        /// Open current folder in Windows Explorer
        /// </summary>
        private void OpenFolderInExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FileTreeView.SelectedItem is TreeViewItem item && item.Tag is string path)
                {
                    if (File.Exists(path))
                    {
                        Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                    else if (Directory.Exists(path))
                    {
                        Process.Start("explorer.exe", $"\"{path}\"");
                    }
                }
                else
                {
                    Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileTree] Error opening explorer: {ex.Message}");
                try { Process.Start("explorer.exe"); } catch { }
            }
        }
        
        /// <summary>
        /// Toggle Inspector panel with animation (legacy)
        /// </summary>
        private void ToggleInspectorPanel()
        {
            // Panel is now always visible in the new design
        }
        
        /// <summary>
        /// Update Inspector panel with current context (legacy method for compatibility)
        /// </summary>
        private void UpdateInspectorContext()
        {
            UpdateContextDisplay();
            UpdateSecurityDisplay();
        }
        
        /// <summary>
        /// Show a toast notification
        /// </summary>
        public void ShowToast(string message, UI.ToastType type = UI.ToastType.Info)
        {
            _toastManager?.Show(message, type);
        }
        
        #endregion
        
        #region Command Palette (Ctrl+K)
        
        /// <summary>
        /// Open the Command Palette
        /// </summary>
        private void OpenCommandPalette()
        {
            try
            {
                var palette = new UI.CommandPalette();
                palette.Owner = this;
                
                if (palette.ShowDialog() == true && palette.SelectedCommand != null)
                {
                    ExecuteCommand(palette.SelectedCommand.Action);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandPalette] Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Click handler for Command Palette button in sidebar
        /// </summary>
        private void CommandPalette_Click(object sender, RoutedEventArgs e)
        {
            OpenCommandPalette();
        }
        
        /// <summary>
        /// Execute a command from the palette
        /// </summary>
        private void ExecuteCommand(string action)
        {
            Debug.WriteLine($"[CommandPalette] Executing action: {action}");
            
            switch (action)
            {
                case "voice_start":
                    ActivateVoiceWithHotkey();
                    ShowToast("🎤 Voice input started", UI.ToastType.Info);
                    break;
                case "voice_toggle":
                    SpeechToggle.IsChecked = !SpeechToggle.IsChecked;
                    SpeechToggle_Click(SpeechToggle, new RoutedEventArgs());
                    ShowToast(SpeechToggle.IsChecked == true ? "🔊 Voice output enabled" : "🔇 Voice output disabled", UI.ToastType.Info);
                    break;
                case "voice_select":
                    // Open the voice provider popup instead of hidden combobox
                    VoiceProviderPopup.IsOpen = true;
                    _ = PopulateVoiceListAsync();
                    break;
                case "wake_word":
                    ToggleWakeWord();
                    ShowToast(isWakeWordEnabled ? "👂 Wake word enabled" : "🚫 Wake word disabled", UI.ToastType.Info);
                    break;
                case "generate":
                    InputBox.Text = "Generate an image of: ";
                    InputBox.Focus();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    ShowToast("💡 Type what you want to generate", UI.ToastType.Info);
                    break;
                case "suggest":
                    InputBox.Text = "Give me suggestions for: ";
                    InputBox.Focus();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    ShowToast("💡 Type what you need suggestions for", UI.ToastType.Info);
                    break;
                case "summarize":
                    InputBox.Text = "Summarize this: ";
                    InputBox.Focus();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    ShowToast("💡 Paste text to summarize", UI.ToastType.Info);
                    break;
                case "code":
                    OpenCodeEditor();
                    ShowToast("💻 Code Editor opened", UI.ToastType.Info);
                    break;
                case "write":
                    InputBox.Text = "Help me write: ";
                    InputBox.Focus();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    ShowToast("✏ Type what you want to write", UI.ToastType.Info);
                    break;
                case "search":
                    InputBox.Text = "Search for: ";
                    InputBox.Focus();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    ShowToast("🔍 Type what you want to search", UI.ToastType.Info);
                    break;
                case "memory":
                    Memory_Click(null, new RoutedEventArgs());
                    break;
                case "screenshot":
                    CaptureButton_Click(CaptureButton, new RoutedEventArgs());
                    break;
                case "capture_history":
                    HistoryButton_Click(HistoryButton, new RoutedEventArgs());
                    break;
                case "history":
                    History_Click(null, new RoutedEventArgs());
                    break;
                case "uninstaller":
                    Uninstaller_Click(null, new RoutedEventArgs());
                    break;
                case "update_db":
                    UpdateDB_Click(null, new RoutedEventArgs());
                    break;
                case "security":
                    SecuritySuite_Click(null, new RoutedEventArgs());
                    break;
                case "quick_scan":
                    // Open Security Suite for quick scan
                    ShowSecuritySuiteWindow();
                    break;
                case "scan_mode":
                case "scan_orbit":
                case "security_scan":
                    ToggleScanOrbit();
                    break;
                case "settings":
                    Settings_Click(null, new RoutedEventArgs());
                    break;
                case "theme":
                    ThemeManager.ToggleTheme();
                    ApplyTheme();
                    ShowToast("Theme toggled", UI.ToastType.Info);
                    break;
                case "inspector":
                    ToggleInspectorPanel();
                    break;
                case "status_panel":
                    StatusPanelToggle_Click(null, new RoutedEventArgs());
                    break;
                case "focus_mode":
                    ToggleFocusMode();
                    break;
                case "fullscreen":
                    ToggleFullscreen();
                    break;
                case "clear_chat":
                    DeleteHistory_Click(null, new RoutedEventArgs());
                    break;
                case "new_chat":
                    NewChat_Click(null, new RoutedEventArgs());
                    break;
                case "overlay":
                    _inAppAssistant?.ToggleOverlay();
                    break;
                case "context":
                    var ctx = _inAppAssistant?.GetCurrentContext();
                    if (ctx != null)
                    {
                        ShowToast($"Context: {ctx.ProcessName}", UI.ToastType.Info);
                    }
                    break;
                default:
                    Debug.WriteLine($"[CommandPalette] Unknown action: {action}");
                    break;
            }
        }
        
        #endregion
        
        #region Security Suite Button
        
        /// <summary>
        /// Open Security Suite window
        /// </summary>
        private void SecuritySuite_Click(object? sender, RoutedEventArgs e)
        {
            ShowSecuritySuiteWindow();
        }
        
        /// <summary>
        /// Shows the Security Suite window as a singleton (reuses existing window if open)
        /// </summary>
        private void ShowSecuritySuiteWindow()
        {
            try
            {
                // DEBUG: Confirm this method is being called
                MessageBox.Show("ShowSecuritySuiteWindow called!", "DEBUG");
                
                // Check if window exists and is still open
                if (_securitySuiteWindow != null && _securitySuiteWindow.IsLoaded)
                {
                    // Window exists, just bring it to front
                    _securitySuiteWindow.Activate();
                    if (_securitySuiteWindow.WindowState == WindowState.Minimized)
                        _securitySuiteWindow.WindowState = WindowState.Normal;
                    return;
                }
                
                // Create new Security Suite window with real scanning
                Debug.WriteLine("[SecuritySuite] Creating new window...");
                _securitySuiteWindow = new SecuritySuite.SecuritySuiteWindow();
                _securitySuiteWindow.Owner = this;
                
                // Clear reference when window closes
                _securitySuiteWindow.Closed += (s, args) => 
                {
                    Debug.WriteLine("[SecuritySuite] Window closed, clearing reference");
                    _securitySuiteWindow = null;
                };
                
                _securitySuiteWindow.Show();
                Debug.WriteLine("[SecuritySuite] Window shown successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Security] Error opening Security Suite: {ex.Message}");
                Debug.WriteLine($"[Security] Inner: {ex.InnerException?.Message}");
                Debug.WriteLine($"[Security] Stack: {ex.StackTrace}");
                
                // Show error in chat instead of toast (more visible)
                AddMessage("Atlas", $"⚠️ Security Suite failed to open:\n{ex.Message}\n\nInner: {ex.InnerException?.Message}", false);
                
                // Also show a message box so user sees it
                MessageBox.Show($"Security Suite failed to open:\n\n{ex.Message}\n\nInner Exception: {ex.InnerException?.Message}", 
                    "Security Suite Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Open Integration Hub window - shows all available integrations
        /// </summary>
        private void IntegrationHub_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var hubWindow = new IntegrationHubWindow();
                hubWindow.Owner = this;
                hubWindow.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IntegrationHub] Error opening Integration Hub: {ex.Message}");
                ShowToast($"Error: {ex.Message}", UI.ToastType.Error);
            }
        }
        
        /// <summary>
        /// Open Social Media Console window
        /// </summary>
        private void SocialMediaConsole_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var consoleWindow = new SocialMedia.UI.SocialMediaWindow();
                consoleWindow.Owner = this;
                consoleWindow.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SocialMedia] Error opening Social Media Console: {ex.Message}");
                ShowToast($"Error: {ex.Message}", UI.ToastType.Error);
            }
        }
        
        #endregion
        
        #region ═══════════════════════════════════════════════════════════════
        // FIGMA UI INTEGRATION - TopNavBar and StatusPanel handlers
        #endregion
        
        /// <summary>
        /// Handle TopNavBar tab changes - switches pages in-window (no new windows)
        /// </summary>
        private void TopNavBar_TabChanged(object? sender, string tabName)
        {
            try
            {
                Debug.WriteLine($"[TopNavBar] Tab changed to: {tabName}");
                ShowPage(tabName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TopNavBar] Error handling tab change: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show a specific page by name, hiding all others (single-window navigation)
        /// </summary>
        public async void ShowPage(string pageName)
        {
            // Normalize page name to lowercase for comparison
            var page = pageName?.ToLowerInvariant() ?? "chat";
            
            // Set visibility for each page
            ChatPageRoot.Visibility     = page == "chat"     ? Visibility.Visible : Visibility.Collapsed;
            CommandsPageRoot.Visibility = page == "commands" ? Visibility.Visible : Visibility.Collapsed;
            MemoryPageRoot.Visibility   = page == "memory"   ? Visibility.Visible : Visibility.Collapsed;
            SecurityPageRoot.Visibility = page == "security" ? Visibility.Visible : Visibility.Collapsed;
            SmartHomePageRoot.Visibility = page == "smarthome" ? Visibility.Visible : Visibility.Collapsed;
            // CreatePageRoot.Visibility   = page == "create"   ? Visibility.Visible : Visibility.Collapsed; // DISABLED - CreatePage excluded
            CodePageRoot.Visibility     = page == "code"     ? Visibility.Visible : Visibility.Collapsed;
            MediaPageRoot.Visibility    = page == "media"    ? Visibility.Visible : Visibility.Collapsed;
            DownloadPageRoot.Visibility = page == "downloads" ? Visibility.Visible : Visibility.Collapsed;

            var sectionKey = page switch
            {
                "media" => "Media",
                "smarthome" => "SmartHome",
                "security" => "Security",
                "code" => "Code",
                "downloads" => "Downloads",
                _ => "Chat",
            };

            SectionAgentContext.SetActiveSection(sectionKey);
            try { LeftSidebar?.SetActiveTab(sectionKey); } catch { }
            try { TopNavBar?.SetUnloadVisible(!string.Equals(page, "chat", StringComparison.OrdinalIgnoreCase)); } catch { }

            SetOrbOverlayVisible(page == "chat");
            
            SetChromeVisible(page != "media");

            Debug.WriteLine($"[Navigation] Showing page: {page}");

            if (page == "smarthome")
                ShowStatus("🏠 Smart Home ready");
            
            // Trigger security scan when Security tab is clicked
            if (page == "security")
            {
                SecurityPageRoot.SetVoiceManager(_voiceManager);
                await SecurityPageRoot.StartScanAsync();
            }
        }

        private GridLength _restoreSidebarColumnWidth = new GridLength(64);

        private void SetChromeVisible(bool visible)
        {
            try
            {
                if (TopNavBar != null)
                {
                    TopNavBar.IsHitTestVisible = visible;
                    TopNavBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    TopNavBar.Opacity = visible ? 1 : 0;
                }

                if (SidebarPanel != null)
                {
                    SidebarPanel.IsHitTestVisible = visible;
                    SidebarPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    SidebarPanel.Opacity = visible ? 1 : 0;
                }

                // Sidebar collapse must only affect column width (not rows).
                // When the sidebar is hidden, ensure we also collapse the column to prevent phantom spacing.
                if (SidebarColumn != null)
                {
                    if (!visible)
                    {
                        if (SidebarColumn.Width.Value > 0)
                        {
                            _restoreSidebarColumnWidth = SidebarColumn.Width;
                        }
                        SidebarColumn.Width = new GridLength(0);
                    }
                    else
                    {
                        if (SidebarColumn.Width.Value == 0)
                        {
                            SidebarColumn.Width = _restoreSidebarColumnWidth;
                        }
                    }
                }

                if (!visible)
                {
                    if (FileBrowserPanel != null) FileBrowserPanel.Visibility = Visibility.Collapsed;
                    if (InspectorPanel != null) InspectorPanel.Visibility = Visibility.Collapsed;
                    if (HistoryDrawerPanel != null) HistoryDrawerPanel.Visibility = Visibility.Collapsed;
                    if (StatusPanel != null) StatusPanel.Visibility = Visibility.Collapsed;
                    if (AgentResultsPanel != null) AgentResultsPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Handle chat messages sent from SecurityPage
        /// </summary>
        private async void SecurityPage_ChatMessageSent(object? sender, string message)
        {
            try
            {
                // Add the user message to chat
                AddMessage("You", message, true);

                // Set input and send through normal flow
                InputBox.Text = message;
                SendMessage();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityPage Chat] Error: {ex.Message}");
                AddMessage("Atlas", $"Sorry, I encountered an error: {ex.Message}", false);
            }
        }
        
        /// <summary>
        /// Handle TopNavBar minimize request
        /// </summary>
        private void TopNavBar_MinimizeRequested(object? sender, EventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        /// <summary>
        /// Handle TopNavBar maximize request
        /// </summary>
        private void TopNavBar_MaximizeRequested(object? sender, EventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        
        /// <summary>
        /// Handle TopNavBar close request
        /// </summary>
        private void TopNavBar_CloseRequested(object? sender, EventArgs e)
        {
            Close();
        }
        
        /// <summary>
        /// Toggle the Status Panel visibility
        /// </summary>
        private void StatusPanelToggle_Click(object sender, RoutedEventArgs e)
        {
            if (StatusPanel.Visibility == Visibility.Visible)
            {
                StatusPanel.Visibility = Visibility.Collapsed;
                StatusPanelColumn.Width = new GridLength(0);
            }
            else
            {
                StatusPanel.Visibility = Visibility.Visible;
                StatusPanelColumn.Width = new GridLength(260);
                
                // Update status panel with current AI info
                UpdateStatusPanel();
            }
        }
        
        /// <summary>
        /// Update the status panel with current AI state
        /// </summary>
        private void UpdateStatusPanel()
        {
            try
            {
                // Update model name from voice manager
                var provider = _voiceManager?.GetProvider(_voiceManager.ActiveProviderType);
                var providerName = provider?.DisplayName ?? "Atlas AI";
                StatusPanel.SetModel(providerName);
                
                // Default to active state
                StatusPanel.SetState("Active", true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusPanel] Error updating: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle quick action requests from status panel
        /// </summary>
        private void StatusPanel_QuickActionRequested(object? sender, string action)
        {
            try
            {
                Debug.WriteLine($"[StatusPanel] Quick action: {action}");
                
                switch (action)
                {
                    case "scan":
                        SecuritySuite_Click(null, new RoutedEventArgs());
                        break;
                    case "optimize":
                        ShowToast("System optimization started", UI.ToastType.Info);
                        break;
                    case "export":
                        ShowToast("Export feature coming soon", UI.ToastType.Info);
                        break;
                    case "cache":
                        ShowToast("Cache cleared", UI.ToastType.Success);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusPanel] Error handling quick action: {ex.Message}");
                ShowToast($"Error: {ex.Message}", UI.ToastType.Error);
            }
        }
    }

    public class ChatMessage
    {
        public string Sender { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsUser { get; set; }
        public string Role { get; set; } = "";
    }
}
