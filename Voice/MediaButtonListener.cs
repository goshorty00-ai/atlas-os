using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Listens for media button events (play/pause, etc.) from Bluetooth devices like AirPods.
    /// Uses BOTH window-level hooks AND global low-level keyboard hooks to capture media keys
    /// even when the app is not focused. This allows AirPods gestures to trigger voice commands.
    /// </summary>
    public class MediaButtonListener : IDisposable
    {
        // Windows message constants
        private const int WM_APPCOMMAND = 0x0319;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_SHELL = 10;
        
        // App command values (from WinUser.h)
        private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
        private const int APPCOMMAND_MEDIA_PLAY = 46;
        private const int APPCOMMAND_MEDIA_PAUSE = 47;
        private const int APPCOMMAND_MEDIA_STOP = 13;
        private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
        private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
        private const int APPCOMMAND_MIC_ON_OFF_TOGGLE = 44;
        
        // Virtual key codes for media keys
        private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const int VK_MEDIA_STOP = 0xB2;
        private const int VK_MEDIA_NEXT_TRACK = 0xB0;
        private const int VK_MEDIA_PREV_TRACK = 0xB1;
        
        // P/Invoke for global keyboard hook
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        
        private HwndSource? _hwndSource;
        private IntPtr _hwnd;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private LowLevelKeyboardProc? _keyboardProc;
        private bool _isListening = false;
        private DateTime _lastTriggerTime = DateTime.MinValue;
        private readonly TimeSpan _debounceTime = TimeSpan.FromMilliseconds(500);
        
        // Hotkey IDs
        private const int HOTKEY_PLAY_PAUSE = 1;
        private const int HOTKEY_NEXT = 2;
        private const int HOTKEY_PREV = 3;
        private const int WM_HOTKEY = 0x0312;
        
        /// <summary>
        /// Fired when play/pause media button is pressed (AirPods single tap, squeeze, etc.)
        /// </summary>
        public event EventHandler? PlayPausePressed;
        
        /// <summary>
        /// Fired when next track button is pressed (AirPods double tap on right)
        /// </summary>
        public event EventHandler? NextTrackPressed;
        
        /// <summary>
        /// Fired when previous track button is pressed (AirPods double tap on left)
        /// </summary>
        public event EventHandler? PreviousTrackPressed;
        
        /// <summary>
        /// Fired when any media button is pressed
        /// </summary>
        public event EventHandler<string>? MediaButtonPressed;
        
        public bool IsListening => _isListening;

        /// <summary>
        /// Start listening for media button events on the specified window
        /// </summary>
        public void StartListening(Window window)
        {
            if (_isListening) return;
            
            try
            {
                var helper = new WindowInteropHelper(window);
                _hwnd = helper.Handle;
                
                if (_hwnd == IntPtr.Zero)
                {
                    // Window not yet loaded, wait for it
                    window.Loaded += (s, e) =>
                    {
                        _hwnd = new WindowInteropHelper(window).Handle;
                        AttachHooks();
                    };
                }
                else
                {
                    AttachHooks();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaButton] Failed to start: {ex.Message}");
            }
        }
        
        private void AttachHooks()
        {
            if (_hwnd == IntPtr.Zero) return;
            
            // 1. Window-level hook for WM_APPCOMMAND (works when focused)
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(WndProc);
                Debug.WriteLine("[MediaButton] Window hook attached");
            }
            
            // 2. Global low-level keyboard hook for media keys (works even when not focused)
            InstallGlobalKeyboardHook();
            
            // 3. Register global hotkeys for media keys (alternative method)
            try
            {
                RegisterHotKey(_hwnd, HOTKEY_PLAY_PAUSE, 0, (uint)VK_MEDIA_PLAY_PAUSE);
                RegisterHotKey(_hwnd, HOTKEY_NEXT, 0, (uint)VK_MEDIA_NEXT_TRACK);
                RegisterHotKey(_hwnd, HOTKEY_PREV, 0, (uint)VK_MEDIA_PREV_TRACK);
                Debug.WriteLine("[MediaButton] Global hotkeys registered");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaButton] Hotkey registration failed: {ex.Message}");
            }
            
            _isListening = true;
            Debug.WriteLine("[MediaButton] Now listening for media button events (AirPods gestures) - GLOBAL");
        }
        
        private void InstallGlobalKeyboardHook()
        {
            try
            {
                _keyboardProc = LowLevelKeyboardCallback;
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                if (curModule != null)
                {
                    _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, 
                        GetModuleHandle(curModule.ModuleName), 0);
                    
                    if (_keyboardHook != IntPtr.Zero)
                    {
                        Debug.WriteLine("[MediaButton] Global keyboard hook installed");
                    }
                    else
                    {
                        Debug.WriteLine($"[MediaButton] Failed to install keyboard hook: {Marshal.GetLastWin32Error()}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaButton] Keyboard hook error: {ex.Message}");
            }
        }
        
        private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)hookStruct.vkCode;
                
                // Check for media keys
                if (vk == VK_MEDIA_PLAY_PAUSE || vk == VK_MEDIA_NEXT_TRACK || vk == VK_MEDIA_PREV_TRACK)
                {
                    // Debounce
                    if (DateTime.Now - _lastTriggerTime >= _debounceTime)
                    {
                        _lastTriggerTime = DateTime.Now;
                        
                        // Fire event on UI thread
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            switch (vk)
                            {
                                case VK_MEDIA_PLAY_PAUSE:
                                    Debug.WriteLine("[MediaButton] GLOBAL: Play/Pause detected!");
                                    MediaButtonPressed?.Invoke(this, "PlayPause");
                                    PlayPausePressed?.Invoke(this, EventArgs.Empty);
                                    break;
                                case VK_MEDIA_NEXT_TRACK:
                                    Debug.WriteLine("[MediaButton] GLOBAL: Next Track detected!");
                                    MediaButtonPressed?.Invoke(this, "NextTrack");
                                    NextTrackPressed?.Invoke(this, EventArgs.Empty);
                                    break;
                                case VK_MEDIA_PREV_TRACK:
                                    Debug.WriteLine("[MediaButton] GLOBAL: Previous Track detected!");
                                    MediaButtonPressed?.Invoke(this, "PreviousTrack");
                                    PreviousTrackPressed?.Invoke(this, EventArgs.Empty);
                                    break;
                            }
                        }));
                    }
                }
            }
            
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }
        
        /// <summary>
        /// Stop listening for media button events
        /// </summary>
        public void StopListening()
        {
            if (!_isListening) return;
            
            try
            {
                // Remove window hook
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;
                
                // Remove global keyboard hook
                if (_keyboardHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyboardHook);
                    _keyboardHook = IntPtr.Zero;
                    Debug.WriteLine("[MediaButton] Global keyboard hook removed");
                }
                
                // Unregister hotkeys
                if (_hwnd != IntPtr.Zero)
                {
                    UnregisterHotKey(_hwnd, HOTKEY_PLAY_PAUSE);
                    UnregisterHotKey(_hwnd, HOTKEY_NEXT);
                    UnregisterHotKey(_hwnd, HOTKEY_PREV);
                }
                
                _isListening = false;
                Debug.WriteLine("[MediaButton] Stopped listening for media buttons");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaButton] Failed to stop: {ex.Message}");
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                // Handle global hotkey messages
                if (msg == WM_HOTKEY)
                {
                    int id = (int)wParam;
                    
                    if (DateTime.Now - _lastTriggerTime >= _debounceTime)
                    {
                        _lastTriggerTime = DateTime.Now;
                        
                        switch (id)
                        {
                            case HOTKEY_PLAY_PAUSE:
                                Debug.WriteLine("[MediaButton] HOTKEY: Play/Pause detected!");
                                MediaButtonPressed?.Invoke(this, "PlayPause");
                                PlayPausePressed?.Invoke(this, EventArgs.Empty);
                                break;
                            case HOTKEY_NEXT:
                                Debug.WriteLine("[MediaButton] HOTKEY: Next Track detected!");
                                MediaButtonPressed?.Invoke(this, "NextTrack");
                                NextTrackPressed?.Invoke(this, EventArgs.Empty);
                                break;
                            case HOTKEY_PREV:
                                Debug.WriteLine("[MediaButton] HOTKEY: Previous Track detected!");
                                MediaButtonPressed?.Invoke(this, "PreviousTrack");
                                PreviousTrackPressed?.Invoke(this, EventArgs.Empty);
                                break;
                        }
                    }
                    return IntPtr.Zero;
                }
                
                if (msg == WM_APPCOMMAND)
                {
                    // Extract the command from lParam
                    int cmd = (int)((uint)lParam >> 16) & 0xFFF;
                    
                    // Debounce to prevent double triggers
                    if (DateTime.Now - _lastTriggerTime < _debounceTime)
                    {
                        Debug.WriteLine($"[MediaButton] Debounced command: {cmd}");
                        return IntPtr.Zero;
                    }
                    
                    switch (cmd)
                    {
                        case APPCOMMAND_MEDIA_PLAY_PAUSE:
                        case APPCOMMAND_MEDIA_PLAY:
                        case APPCOMMAND_MEDIA_PAUSE:
                            _lastTriggerTime = DateTime.Now;
                            Debug.WriteLine("[MediaButton] APPCOMMAND: Play/Pause detected!");
                            MediaButtonPressed?.Invoke(this, "PlayPause");
                            PlayPausePressed?.Invoke(this, EventArgs.Empty);
                            break;
                            
                        case APPCOMMAND_MEDIA_NEXTTRACK:
                            _lastTriggerTime = DateTime.Now;
                            Debug.WriteLine("[MediaButton] APPCOMMAND: Next Track detected!");
                            MediaButtonPressed?.Invoke(this, "NextTrack");
                            NextTrackPressed?.Invoke(this, EventArgs.Empty);
                            break;
                            
                        case APPCOMMAND_MEDIA_PREVIOUSTRACK:
                            _lastTriggerTime = DateTime.Now;
                            Debug.WriteLine("[MediaButton] APPCOMMAND: Previous Track detected!");
                            MediaButtonPressed?.Invoke(this, "PreviousTrack");
                            PreviousTrackPressed?.Invoke(this, EventArgs.Empty);
                            break;
                            
                        case APPCOMMAND_MIC_ON_OFF_TOGGLE:
                            _lastTriggerTime = DateTime.Now;
                            Debug.WriteLine("[MediaButton] APPCOMMAND: Mic toggle detected!");
                            MediaButtonPressed?.Invoke(this, "MicToggle");
                            PlayPausePressed?.Invoke(this, EventArgs.Empty);
                            break;
                    }
                }
                else if (msg == WM_KEYDOWN)
                {
                    int vk = (int)wParam;
                    
                    // Debounce
                    if (DateTime.Now - _lastTriggerTime < _debounceTime)
                        return IntPtr.Zero;
                    
                    switch (vk)
                    {
                        case VK_MEDIA_PLAY_PAUSE:
                            _lastTriggerTime = DateTime.Now;
                            Debug.WriteLine("[MediaButton] KEYDOWN: Play/Pause detected!");
                            MediaButtonPressed?.Invoke(this, "PlayPause");
                            PlayPausePressed?.Invoke(this, EventArgs.Empty);
                            break;
                            
                        case VK_MEDIA_NEXT_TRACK:
                            _lastTriggerTime = DateTime.Now;
                            Debug.WriteLine("[MediaButton] KEYDOWN: Next Track detected!");
                            MediaButtonPressed?.Invoke(this, "NextTrack");
                            NextTrackPressed?.Invoke(this, EventArgs.Empty);
                            break;
                            
                        case VK_MEDIA_PREV_TRACK:
                            _lastTriggerTime = DateTime.Now;
                            Debug.WriteLine("[MediaButton] KEYDOWN: Previous Track detected!");
                            MediaButtonPressed?.Invoke(this, "PreviousTrack");
                            PreviousTrackPressed?.Invoke(this, EventArgs.Empty);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaButton] WndProc error: {ex.Message}");
            }
            
            return IntPtr.Zero;
        }
        
        public void Dispose()
        {
            StopListening();
        }
    }
}
