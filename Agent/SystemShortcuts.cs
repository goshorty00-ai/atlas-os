using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AtlasAI.Agent
{
    /// <summary>
    /// System shortcuts - quick access to common system functions.
    /// </summary>
    public static class SystemShortcuts
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        
        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();
        
        private const byte VK_LWIN = 0x5B;
        private const byte VK_D = 0x44;
        private const byte VK_E = 0x45;
        private const byte VK_I = 0x49;
        private const byte VK_L = 0x4C;
        private const byte VK_R = 0x52;
        private const byte VK_S = 0x53;
        private const byte VK_TAB = 0x09;
        private const byte VK_SNAPSHOT = 0x2C;
        private const byte VK_SHIFT = 0x10;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        
        /// <summary>
        /// Show desktop (Win+D)
        /// </summary>
        public static async Task<string> ShowDesktopAsync()
        {
            SendWinKey(VK_D);
            return "✓ Showing desktop";
        }
        
        /// <summary>
        /// Open File Explorer (Win+E)
        /// </summary>
        public static async Task<string> OpenExplorerAsync()
        {
            SendWinKey(VK_E);
            return "✓ Opened File Explorer";
        }
        
        /// <summary>
        /// Open Settings (Win+I)
        /// </summary>
        public static async Task<string> OpenSettingsAsync()
        {
            SendWinKey(VK_I);
            return "✓ Opened Settings";
        }
        
        /// <summary>
        /// Lock computer (Win+L)
        /// </summary>
        public static async Task<string> LockComputerAsync()
        {
            LockWorkStation();
            return "✓ Computer locked";
        }
        
        /// <summary>
        /// Open Run dialog (Win+R)
        /// </summary>
        public static async Task<string> OpenRunDialogAsync()
        {
            SendWinKey(VK_R);
            return "✓ Opened Run dialog";
        }
        
        /// <summary>
        /// Open Search (Win+S)
        /// </summary>
        public static async Task<string> OpenSearchAsync()
        {
            SendWinKey(VK_S);
            return "✓ Opened Search";
        }
        
        /// <summary>
        /// Open Task View (Win+Tab)
        /// </summary>
        public static async Task<string> OpenTaskViewAsync()
        {
            SendWinKey(VK_TAB);
            return "✓ Opened Task View";
        }
        
        /// <summary>
        /// Take screenshot (Print Screen)
        /// </summary>
        public static async Task<string> TakeScreenshotAsync()
        {
            // Win+Shift+S for Snip & Sketch
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
            keybd_event(VK_S, 0, 0, UIntPtr.Zero);
            await Task.Delay(50);
            keybd_event(VK_S, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            
            return "✓ Screenshot tool opened";
        }
        
        /// <summary>
        /// Open Action Center (Win+A)
        /// </summary>
        public static async Task<string> OpenActionCenterAsync()
        {
            SendWinKey(0x41); // A
            return "✓ Opened Action Center";
        }
        
        /// <summary>
        /// Open Clipboard History (Win+V)
        /// </summary>
        public static async Task<string> OpenClipboardHistoryAsync()
        {
            SendWinKey(0x56); // V
            return "✓ Opened Clipboard History";
        }
        
        /// <summary>
        /// Open Emoji Picker (Win+.)
        /// </summary>
        public static async Task<string> OpenEmojiPickerAsync()
        {
            SendWinKey(0xBE); // Period
            return "✓ Opened Emoji Picker";
        }
        
        /// <summary>
        /// Open Game Bar (Win+G)
        /// </summary>
        public static async Task<string> OpenGameBarAsync()
        {
            SendWinKey(0x47); // G
            return "✓ Opened Game Bar";
        }
        
        /// <summary>
        /// Open Magnifier (Win++)
        /// </summary>
        public static async Task<string> OpenMagnifierAsync()
        {
            SendWinKey(0xBB); // Plus
            return "✓ Opened Magnifier";
        }
        
        /// <summary>
        /// Open Narrator (Win+Ctrl+Enter)
        /// </summary>
        public static async Task<string> ToggleNarratorAsync()
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(0x11, 0, 0, UIntPtr.Zero); // Ctrl
            keybd_event(0x0D, 0, 0, UIntPtr.Zero); // Enter
            await Task.Delay(50);
            keybd_event(0x0D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            
            return "✓ Toggled Narrator";
        }
        
        /// <summary>
        /// Open Device Manager
        /// </summary>
        public static async Task<string> OpenDeviceManagerAsync()
        {
            Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
            return "✓ Opened Device Manager";
        }
        
        /// <summary>
        /// Open Disk Management
        /// </summary>
        public static async Task<string> OpenDiskManagementAsync()
        {
            Process.Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true });
            return "✓ Opened Disk Management";
        }
        
        /// <summary>
        /// Open Event Viewer
        /// </summary>
        public static async Task<string> OpenEventViewerAsync()
        {
            Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true });
            return "✓ Opened Event Viewer";
        }
        
        /// <summary>
        /// Open Services
        /// </summary>
        public static async Task<string> OpenServicesAsync()
        {
            Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });
            return "✓ Opened Services";
        }
        
        /// <summary>
        /// Open System Information
        /// </summary>
        public static async Task<string> OpenSystemInfoAsync()
        {
            Process.Start(new ProcessStartInfo("msinfo32") { UseShellExecute = true });
            return "✓ Opened System Information";
        }
        
        /// <summary>
        /// Open Network Connections
        /// </summary>
        public static async Task<string> OpenNetworkConnectionsAsync()
        {
            Process.Start(new ProcessStartInfo("ncpa.cpl") { UseShellExecute = true });
            return "✓ Opened Network Connections";
        }
        
        /// <summary>
        /// Open Programs and Features
        /// </summary>
        public static async Task<string> OpenProgramsAndFeaturesAsync()
        {
            Process.Start(new ProcessStartInfo("appwiz.cpl") { UseShellExecute = true });
            return "✓ Opened Programs and Features";
        }
        
        /// <summary>
        /// Empty Recycle Bin
        /// </summary>
        public static async Task<string> EmptyRecycleBinAsync()
        {
            try
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x00000001); // SHERB_NOCONFIRMATION
                return "✓ Recycle Bin emptied";
            }
            catch
            {
                return "❌ Couldn't empty Recycle Bin";
            }
        }
        
        [DllImport("shell32.dll")]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
        
        private static void SendWinKey(byte key)
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        
        /// <summary>
        /// Handle system shortcut commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string lower)
        {
            return lower switch
            {
                "show desktop" or "desktop" => await ShowDesktopAsync(),
                "open explorer" or "file explorer" or "files" => await OpenExplorerAsync(),
                "open settings" or "settings" => await OpenSettingsAsync(),
                "lock" or "lock computer" or "lock pc" or "lock screen" => await LockComputerAsync(),
                "run" or "open run" or "run dialog" => await OpenRunDialogAsync(),
                "search" or "open search" => await OpenSearchAsync(),
                "task view" or "open task view" => await OpenTaskViewAsync(),
                "action center" or "notifications" => await OpenActionCenterAsync(),
                "clipboard" or "clipboard history" => await OpenClipboardHistoryAsync(),
                "emoji" or "emojis" or "emoji picker" => await OpenEmojiPickerAsync(),
                "game bar" or "xbox game bar" => await OpenGameBarAsync(),
                "magnifier" or "zoom in" => await OpenMagnifierAsync(),
                "narrator" or "toggle narrator" => await ToggleNarratorAsync(),
                "device manager" => await OpenDeviceManagerAsync(),
                "disk management" => await OpenDiskManagementAsync(),
                "event viewer" or "events" => await OpenEventViewerAsync(),
                "services" => await OpenServicesAsync(),
                "system info" or "system information" => await OpenSystemInfoAsync(),
                "network connections" or "network adapters" => await OpenNetworkConnectionsAsync(),
                "programs and features" or "uninstall programs" or "add remove programs" => await OpenProgramsAndFeaturesAsync(),
                "empty recycle bin" or "empty trash" or "clear recycle bin" => await EmptyRecycleBinAsync(),
                _ => null
            };
        }
    }
}
