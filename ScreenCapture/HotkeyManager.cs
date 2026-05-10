using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;

namespace AtlasAI.ScreenCapture
{
    public class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly Dictionary<int, HotkeyInfo> registeredHotkeys = new();
        private readonly HwndSource? hwndSource;
        private int nextHotkeyId = 1000;

        public event Action<string>? HotkeyPressed;

        public HotkeyManager(IntPtr windowHandle)
        {
            hwndSource = HwndSource.FromHwnd(windowHandle);
            hwndSource?.AddHook(WndProc);
        }

        public bool RegisterHotkey(string name, ModifierKeys modifiers, Keys key)
        {
            try
            {
                var hotkeyId = nextHotkeyId++;
                var success = RegisterHotKey(hwndSource?.Handle ?? IntPtr.Zero, hotkeyId, (uint)modifiers, (uint)key);
                
                if (success)
                {
                    registeredHotkeys[hotkeyId] = new HotkeyInfo
                    {
                        Id = hotkeyId,
                        Name = name,
                        Modifiers = modifiers,
                        Key = key
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"Registered hotkey: {name} ({modifiers}+{key})");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to register hotkey: {name}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkey registration error: {ex.Message}");
                return false;
            }
        }

        public void UnregisterHotkey(string name)
        {
            var hotkeyToRemove = -1;
            foreach (var kvp in registeredHotkeys)
            {
                if (kvp.Value.Name == name)
                {
                    hotkeyToRemove = kvp.Key;
                    break;
                }
            }

            if (hotkeyToRemove != -1)
            {
                UnregisterHotKey(hwndSource?.Handle ?? IntPtr.Zero, hotkeyToRemove);
                registeredHotkeys.Remove(hotkeyToRemove);
                System.Diagnostics.Debug.WriteLine($"Unregistered hotkey: {name}");
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                var hotkeyId = wParam.ToInt32();
                if (registeredHotkeys.TryGetValue(hotkeyId, out var hotkeyInfo))
                {
                    HotkeyPressed?.Invoke(hotkeyInfo.Name);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void RegisterDefaultHotkeys()
        {
            // Register common screen capture hotkeys
            RegisterHotkey("screenshot", ModifierKeys.Control | ModifierKeys.Shift, Keys.S);
            RegisterHotkey("fullscreen", ModifierKeys.Alt, Keys.PrintScreen);
            RegisterHotkey("quickcapture", ModifierKeys.Control | ModifierKeys.Alt, Keys.C);
        }

        public List<HotkeyInfo> GetRegisteredHotkeys()
        {
            return new List<HotkeyInfo>(registeredHotkeys.Values);
        }

        public void Dispose()
        {
            // Unregister all hotkeys
            foreach (var hotkeyId in registeredHotkeys.Keys)
            {
                UnregisterHotKey(hwndSource?.Handle ?? IntPtr.Zero, hotkeyId);
            }
            registeredHotkeys.Clear();

            hwndSource?.RemoveHook(WndProc);
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
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

    public class HotkeyInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ModifierKeys Modifiers { get; set; }
        public Keys Key { get; set; }

        public override string ToString()
        {
            var parts = new List<string>();
            
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            
            parts.Add(Key.ToString());
            
            return string.Join(" + ", parts);
        }
    }
}