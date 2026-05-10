using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AtlasAI.SmartAssistant.UI
{
    /// <summary>
    /// Floating overlay for quick Atlas access via global hotkey
    /// </summary>
    public partial class FloatingOverlay : Window
    {
        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_SPACE = 0x20;
        
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        public event EventHandler<string>? CommandSubmitted;
        public event EventHandler? VoiceInputRequested;
        
        private bool _isHotkeyRegistered;
        
        public FloatingOverlay()
        {
            InitializeComponent();
            
            // Position at top center of screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - Width) / 2;
            Top = 100;
            
            Loaded += FloatingOverlay_Loaded;
            Closing += FloatingOverlay_Closing;
        }
        
        private void FloatingOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            RegisterGlobalHotkey();
            
            // Initially hidden
            Hide();
        }
        
        private void FloatingOverlay_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            UnregisterGlobalHotkey();
        }
        
        /// <summary>
        /// Register global hotkey (Ctrl+Alt+Space)
        /// </summary>
        public void RegisterGlobalHotkey()
        {
            if (_isHotkeyRegistered) return;
            
            try
            {
                var helper = new WindowInteropHelper(this);
                var hwnd = helper.EnsureHandle();
                
                var source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(HwndHook);
                
                _isHotkeyRegistered = RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_SPACE);
                
                if (_isHotkeyRegistered)
                {
                    Debug.WriteLine("[FloatingOverlay] Global hotkey registered: Ctrl+Alt+Space");
                }
                else
                {
                    Debug.WriteLine("[FloatingOverlay] Failed to register global hotkey");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingOverlay] Error registering hotkey: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Unregister global hotkey
        /// </summary>
        public void UnregisterGlobalHotkey()
        {
            if (!_isHotkeyRegistered) return;
            
            try
            {
                var helper = new WindowInteropHelper(this);
                UnregisterHotKey(helper.Handle, HOTKEY_ID);
                _isHotkeyRegistered = false;
                Debug.WriteLine("[FloatingOverlay] Global hotkey unregistered");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingOverlay] Error unregistering hotkey: {ex.Message}");
            }
        }
        
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleVisibility();
                handled = true;
            }
            
            return IntPtr.Zero;
        }
        
        /// <summary>
        /// Toggle overlay visibility
        /// </summary>
        public void ToggleVisibility()
        {
            if (IsVisible)
            {
                HideOverlay();
            }
            else
            {
                ShowOverlay();
            }
        }
        
        /// <summary>
        /// Show the overlay
        /// </summary>
        public void ShowOverlay()
        {
            Show();
            Activate();
            CommandInput.Clear();
            CommandInput.Focus();
            Debug.WriteLine("[FloatingOverlay] Shown");
        }
        
        /// <summary>
        /// Hide the overlay
        /// </summary>
        public void HideOverlay()
        {
            Hide();
            Debug.WriteLine("[FloatingOverlay] Hidden");
        }
        
        private void CommandInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var command = CommandInput.Text.Trim();
                if (!string.IsNullOrEmpty(command))
                {
                    CommandSubmitted?.Invoke(this, command);
                    HideOverlay();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HideOverlay();
                e.Handled = true;
            }
        }
        
        private void CommandInput_GotFocus(object sender, RoutedEventArgs e)
        {
            // Visual feedback when focused
        }
        
        private void CommandInput_LostFocus(object sender, RoutedEventArgs e)
        {
            // Auto-hide after losing focus (with small delay)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsKeyboardFocusWithin)
                {
                    HideOverlay();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        
        private void VoiceButton_Click(object sender, RoutedEventArgs e)
        {
            VoiceInputRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
