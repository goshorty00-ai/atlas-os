using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace AtlasAI.InAppAssistant.Services
{
    /// <summary>
    /// Captures selected text from the active application using UI Automation
    /// with clipboard fallback (requires user permission)
    /// </summary>
    public class TextCaptureService
    {
        private bool _isEnabled = false;
        private string? _savedClipboard;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                Debug.WriteLine($"[TextCapture] Text capture {(value ? "enabled" : "disabled")}");
            }
        }

        /// <summary>
        /// Attempt to capture selected text from the active window
        /// </summary>
        public async Task<string?> CaptureSelectedTextAsync(bool useClipboardFallback = false)
        {
            if (!_isEnabled)
            {
                Debug.WriteLine("[TextCapture] Text capture is disabled");
                return null;
            }

            // First try UI Automation
            var text = TryUIAutomationCapture();
            if (!string.IsNullOrEmpty(text))
            {
                Debug.WriteLine($"[TextCapture] Got text via UI Automation: {text.Length} chars");
                return text;
            }

            // Fallback to clipboard method if allowed
            if (useClipboardFallback)
            {
                text = await TryClipboardCaptureAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    Debug.WriteLine($"[TextCapture] Got text via clipboard: {text.Length} chars");
                    return text;
                }
            }

            Debug.WriteLine("[TextCapture] No text captured");
            return null;
        }

        /// <summary>
        /// Try to get selected text using Windows UI Automation
        /// </summary>
        private string? TryUIAutomationCapture()
        {
            try
            {
                var focusedElement = AutomationElement.FocusedElement;
                if (focusedElement == null)
                    return null;

                // Try TextPattern first (for text controls)
                if (focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObj))
                {
                    var textPattern = (TextPattern)textPatternObj;
                    var selection = textPattern.GetSelection();
                    
                    if (selection != null && selection.Length > 0)
                    {
                        var selectedText = selection[0].GetText(-1);
                        if (!string.IsNullOrEmpty(selectedText))
                            return selectedText;
                    }
                }

                // Try ValuePattern (for edit controls)
                if (focusedElement.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObj))
                {
                    var valuePattern = (ValuePattern)valuePatternObj;
                    var value = valuePattern.Current.Value;
                    
                    // This gets the whole value, not just selection
                    // Return null to try clipboard fallback instead
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TextCapture] UI Automation error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Capture selected text by simulating Ctrl+C and reading clipboard
        /// Saves and restores original clipboard content
        /// </summary>
        private async Task<string?> TryClipboardCaptureAsync()
        {
            try
            {
                // Save current clipboard
                _savedClipboard = null;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (Clipboard.ContainsText())
                        _savedClipboard = Clipboard.GetText();
                    Clipboard.Clear();
                });

                // Small delay to ensure clipboard is clear
                await Task.Delay(50);

                // Send Ctrl+C
                SendCtrlC();

                // Wait for clipboard to be populated
                await Task.Delay(150);

                // Read clipboard
                string? capturedText = null;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (Clipboard.ContainsText())
                        capturedText = Clipboard.GetText();
                });

                // Restore original clipboard
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(_savedClipboard))
                        Clipboard.SetText(_savedClipboard);
                    else
                        Clipboard.Clear();
                });

                return capturedText;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TextCapture] Clipboard capture error: {ex.Message}");
                
                // Try to restore clipboard on error
                try
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!string.IsNullOrEmpty(_savedClipboard))
                            Clipboard.SetText(_savedClipboard);
                    });
                }
                catch (Exception restoreEx)
                {
                    Debug.WriteLine($"[TextCapture] Clipboard restore error: {restoreEx.Message}");
                }

                return null;
            }
        }

        /// <summary>
        /// Send Ctrl+C keystroke to copy selection
        /// </summary>
        private void SendCtrlC()
        {
            // Key down Ctrl
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            // Key down C
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            // Key up C
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            // Key up Ctrl
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        #region Win32 Imports
        private const byte VK_CONTROL = 0x11;
        private const byte VK_C = 0x43;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        #endregion
    }
}
