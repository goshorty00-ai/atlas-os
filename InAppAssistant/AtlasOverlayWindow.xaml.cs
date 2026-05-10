using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AtlasAI.InAppAssistant.Models;
using AtlasAI.InAppAssistant.Services;

namespace AtlasAI.InAppAssistant
{
    /// <summary>
    /// Always-on-top overlay window for Atlas In-App Assistant
    /// </summary>
    public partial class AtlasOverlayWindow : Window
    {
        private readonly WindowsContextService _contextService;
        private readonly TextCaptureService _textCapture;
        private readonly GlobalHotkeyManager _hotkeyManager;
        
        private bool _isListening = false;
        private bool _isDisabled = false; // When true, hotkey won't show the window

        public event EventHandler? VoiceCommandRequested;
        public event EventHandler<string>? TextCaptured;
        public event EventHandler? ActionsRequested;
        
        /// <summary>
        /// Gets or sets whether the overlay is disabled (won't respond to hotkey)
        /// </summary>
        public bool IsDisabled
        {
            get => _isDisabled;
            set => _isDisabled = value;
        }

        public AtlasOverlayWindow()
        {
            InitializeComponent();
            
            _contextService = new WindowsContextService();
            _textCapture = new TextCaptureService();
            _hotkeyManager = new GlobalHotkeyManager();
            
            // Position in bottom-right corner
            Loaded += OnLoaded;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            
            // Start context polling
            _contextService.ActiveAppChanged += OnActiveAppChanged;
            _contextService.StartPolling(500);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Position window
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;
            
            // Initialize hotkey
            var handle = new WindowInteropHelper(this).Handle;
            _hotkeyManager.Initialize(handle);
            _hotkeyManager.OverlayToggleRequested += (s, args) => ToggleVisibility();
            _hotkeyManager.RegisterOverlayHotkey();
            
            // Update initial context
            UpdateActiveAppDisplay(_contextService.GetActiveAppContext());
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OnActiveAppChanged(object? sender, ActiveAppContext context)
        {
            Dispatcher.Invoke(() => UpdateActiveAppDisplay(context));
        }

        private void UpdateActiveAppDisplay(ActiveAppContext context)
        {
            var categoryIcon = WindowsContextService.GetCategoryDisplayName(context.Category);
            ActiveAppText.Text = $"{categoryIcon} {context.ProcessName}";
            
            var title = context.IsBrowser && !string.IsNullOrEmpty(context.BrowserTabTitle) 
                ? context.BrowserTabTitle 
                : context.WindowTitle;
            
            WindowTitleText.Text = title.Length > 40 ? title.Substring(0, 37) + "..." : title;
        }

        public void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible)
            {
                Hide();
                Debug.WriteLine("[Overlay] Hidden");
            }
            else if (!_isDisabled) // Only show if not disabled
            {
                Show();
                Activate();
                Debug.WriteLine("[Overlay] Shown");
            }
            else
            {
                Debug.WriteLine("[Overlay] Toggle ignored - overlay is disabled");
            }
        }

        public void SetListeningState(bool isListening)
        {
            _isListening = isListening;
            
            if (isListening)
            {
                MicIcon.Text = "🔴";
                MicStatusText.Text = "Listening...";
                AvatarIcon.Text = "👂";
            }
            else
            {
                MicIcon.Text = "🎤";
                MicStatusText.Text = "Say 'Atlas' or press Ctrl+Alt+A";
                AvatarIcon.Text = "🤖";
            }
        }

        public void SetProcessingState()
        {
            MicIcon.Text = "⏳";
            MicStatusText.Text = "Processing...";
            AvatarIcon.Text = "🧠";
        }

        public void SetSpeakingState()
        {
            MicIcon.Text = "🔊";
            MicStatusText.Text = "Speaking...";
            AvatarIcon.Text = "🗣️";
        }

        private void Voice_Click(object sender, RoutedEventArgs e)
        {
            VoiceCommandRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void Capture_Click(object sender, RoutedEventArgs e)
        {
            _textCapture.IsEnabled = true;
            var text = await _textCapture.CaptureSelectedTextAsync(useClipboardFallback: true);
            
            if (!string.IsNullOrEmpty(text))
            {
                TextCaptured?.Invoke(this, text);
                MicStatusText.Text = $"Captured {text.Length} chars";
            }
            else
            {
                MicStatusText.Text = "No text selected";
            }
        }

        private void Actions_Click(object sender, RoutedEventArgs e)
        {
            ActionsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            _contextService?.StopPolling();
            _hotkeyManager?.Dispose();
            base.OnClosed(e);
        }
    }
}
