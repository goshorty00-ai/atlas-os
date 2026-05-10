using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AtlasAI.Agent.UI;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Manages the Unified Command Palette lifecycle and global hotkey (Ctrl+Space)
    /// </summary>
    public class CommandPaletteManager : IDisposable
    {
        private static CommandPaletteManager? _instance;
        public static CommandPaletteManager Instance => _instance ??= new CommandPaletteManager();

        private const int HOTKEY_ID = 9001; // Unique ID for command palette hotkey
        private const uint MOD_CTRL = 0x0002;
        private const uint VK_SPACE = 0x20;

        private IntPtr _windowHandle;
        private HwndSource? _hwndSource;
        private UnifiedCommandPalette? _activePalette;
        private bool _isInitialized;

        public event EventHandler<CommandExecutionResult>? CommandExecuted;
        public event EventHandler<string>? NavigationRequested;
        public event EventHandler<string>? FallbackToChat;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private CommandPaletteManager() { }

        /// <summary>
        /// Initialize with a window handle for hotkey registration
        /// </summary>
        public void Initialize(Window window)
        {
            if (_isInitialized) return;

            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.Handle;
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(WndProc);

            // Register Ctrl+Space hotkey
            var success = RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CTRL, VK_SPACE);
            if (success)
            {
                Debug.WriteLine("[CommandPalette] Registered Ctrl+Space hotkey");
            }
            else
            {
                Debug.WriteLine("[CommandPalette] Failed to register Ctrl+Space hotkey - may be in use by another app");
            }

            // Build command index
            CommandIndexService.Instance.BuildIndex();

            _isInitialized = true;
            Debug.WriteLine("[CommandPalette] Manager initialized");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Debug.WriteLine("[CommandPalette] Hotkey triggered");
                Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Toggle the command palette visibility
        /// </summary>
        public void Toggle()
        {
            if (_activePalette != null && _activePalette.IsLoaded)
            {
                // Close existing palette
                _activePalette.Close();
                _activePalette = null;
            }
            else
            {
                // Open new palette
                ShowPalette();
            }
        }

        /// <summary>
        /// Show the command palette
        /// </summary>
        public void ShowPalette(Window? owner = null)
        {
            if (_activePalette != null && _activePalette.IsLoaded)
            {
                _activePalette.Activate();
                return;
            }

            _activePalette = new UnifiedCommandPalette();
            
            // Wire up events
            _activePalette.CommandExecuted += (s, result) => CommandExecuted?.Invoke(this, result);
            _activePalette.NavigationRequested += (s, target) => NavigationRequested?.Invoke(this, target);
            _activePalette.FallbackToChat += (s, query) => FallbackToChat?.Invoke(this, query);
            _activePalette.Closed += (s, e) => _activePalette = null;

            if (owner != null)
            {
                _activePalette.Owner = owner;
                _activePalette.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            _activePalette.Show();
            Debug.WriteLine("[CommandPalette] Palette shown");
        }

        /// <summary>
        /// Close the command palette if open
        /// </summary>
        public void Hide()
        {
            if (_activePalette != null && _activePalette.IsLoaded)
            {
                _activePalette.Close();
                _activePalette = null;
            }
        }

        /// <summary>
        /// Check if palette is currently visible
        /// </summary>
        public bool IsVisible => _activePalette != null && _activePalette.IsLoaded;

        public void Dispose()
        {
            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_ID);
                Debug.WriteLine("[CommandPalette] Unregistered hotkey");
            }

            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
            _activePalette?.Close();
            _activePalette = null;
        }
    }
}
