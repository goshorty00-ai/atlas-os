using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;

namespace AtlasAI.InAppAssistant
{
    /// <summary>
    /// Manages global hotkeys for the In-App Assistant overlay
    /// </summary>
    public class GlobalHotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly Dictionary<int, Action> _hotkeyActions = new();
        private HwndSource? _hwndSource;
        private int _nextId = 9000; // Start high to avoid conflicts

        public event EventHandler? OverlayToggleRequested;
        public event EventHandler? QuickActionRequested;

        /// <summary>
        /// Initialize with a window handle
        /// </summary>
        public void Initialize(IntPtr windowHandle)
        {
            _hwndSource = HwndSource.FromHwnd(windowHandle);
            _hwndSource?.AddHook(WndProc);
            Debug.WriteLine("[GlobalHotkey] Initialized");
        }

        /// <summary>
        /// Register the default Atlas overlay hotkey (Ctrl+Alt+A)
        /// </summary>
        public bool RegisterOverlayHotkey()
        {
            return RegisterHotkey(
                ModifierKeys.Control | ModifierKeys.Alt,
                Keys.A,
                () => OverlayToggleRequested?.Invoke(this, EventArgs.Empty)
            );
        }

        /// <summary>
        /// Register a custom hotkey
        /// </summary>
        public bool RegisterHotkey(ModifierKeys modifiers, Keys key, Action callback)
        {
            if (_hwndSource == null)
            {
                Debug.WriteLine("[GlobalHotkey] Not initialized");
                return false;
            }

            var id = _nextId++;
            var success = RegisterHotKey(_hwndSource.Handle, id, (uint)modifiers, (uint)key);

            if (success)
            {
                _hotkeyActions[id] = callback;
                Debug.WriteLine($"[GlobalHotkey] Registered: {modifiers}+{key} (ID: {id})");
            }
            else
            {
                Debug.WriteLine($"[GlobalHotkey] Failed to register: {modifiers}+{key}");
            }

            return success;
        }

        /// <summary>
        /// Unregister all hotkeys
        /// </summary>
        public void UnregisterAll()
        {
            if (_hwndSource == null) return;

            foreach (var id in _hotkeyActions.Keys)
            {
                UnregisterHotKey(_hwndSource.Handle, id);
            }
            _hotkeyActions.Clear();
            Debug.WriteLine("[GlobalHotkey] Unregistered all hotkeys");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                var id = wParam.ToInt32();
                if (_hotkeyActions.TryGetValue(id, out var action))
                {
                    Debug.WriteLine($"[GlobalHotkey] Hotkey pressed: ID {id}");
                    action?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
        }

        #region Win32 Imports
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        #endregion
    }

    [Flags]
    public enum ModifierKeys : uint
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8
    }
}
