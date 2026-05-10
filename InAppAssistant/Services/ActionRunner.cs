using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using AtlasAI.InAppAssistant.Models;

namespace AtlasAI.InAppAssistant.Services
{
    /// <summary>
    /// Executes in-app actions with confirmation and logging
    /// </summary>
    public class ActionRunner
    {
        private readonly ActionLogger _logger;
        private readonly AppPermissionManager _permissionManager;
        
        public bool DryRunMode { get; set; } = false;
        
        public event EventHandler<ActionResult>? ActionExecuted;
        public event EventHandler<InAppAction>? ActionRequiresConfirmation;

        public ActionRunner(ActionLogger logger, AppPermissionManager permissionManager)
        {
            _logger = logger;
            _permissionManager = permissionManager;
        }

        /// <summary>
        /// Execute an action with permission checking and logging
        /// </summary>
        public async Task<ActionResult> ExecuteAsync(InAppAction action, ActiveAppContext context)
        {
            var result = new ActionResult
            {
                ActionId = action.Id,
                TargetApp = context.ProcessName,
                TargetWindow = context.WindowTitle
            };

            try
            {
                // Check permissions
                if (!_permissionManager.HasPermission(context.ProcessName, action.Type))
                {
                    result.Success = false;
                    result.Message = $"Permission denied for {action.Type} on {context.ProcessName}";
                    _logger.Log(action, context, result, false);
                    return result;
                }

                // Dry run mode - just preview
                if (DryRunMode)
                {
                    result.Success = true;
                    result.Message = $"[DRY RUN] Would execute: {action.Name}\n{GetActionPreview(action)}";
                    return result;
                }

                // Request confirmation for destructive actions
                if (action.RequiresConfirmation || action.IsDestructive)
                {
                    ActionRequiresConfirmation?.Invoke(this, action);
                }

                // Execute based on action type
                result = action.Type switch
                {
                    ActionType.SendKeys => await ExecuteSendKeysAsync(action, context),
                    ActionType.TypeText => await ExecuteTypeTextAsync(action, context),
                    ActionType.Click => await ExecuteClickAsync(action, context),
                    ActionType.RunCommand => await ExecuteRunCommandAsync(action, context),
                    ActionType.ClipboardOperation => await ExecuteClipboardAsync(action, context),
                    ActionType.UIAutomation => await ExecuteUIAutomationAsync(action, context),
                    _ => new ActionResult { Success = false, Message = $"Unknown action type: {action.Type}" }
                };

                _logger.Log(action, context, result, true);
                ActionExecuted?.Invoke(this, result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
                result.Error = ex;
                _logger.Log(action, context, result, false);
            }

            return result;
        }

        /// <summary>
        /// Get a preview of what an action will do
        /// </summary>
        public string GetActionPreview(InAppAction action)
        {
            var preview = new List<string> { $"Action: {action.Name}", $"Type: {action.Type}" };
            
            if (action.Steps.Count > 0)
            {
                preview.Add("Steps:");
                foreach (var step in action.Steps)
                    preview.Add($"  • {step}");
            }

            if (action.IsDestructive)
                preview.Add("⚠️ This action may be destructive");

            return string.Join("\n", preview);
        }

        private async Task<ActionResult> ExecuteSendKeysAsync(InAppAction action, ActiveAppContext context)
        {
            var result = new ActionResult { ActionId = action.Id, TargetApp = context.ProcessName };

            if (!action.Parameters.TryGetValue("keys", out var keysObj) || keysObj is not string keys)
            {
                result.Message = "No keys specified";
                return result;
            }

            await Task.Run(() => SendKeys(keys));
            result.Success = true;
            result.Message = $"Sent keys: {keys}";
            return result;
        }

        private async Task<ActionResult> ExecuteTypeTextAsync(InAppAction action, ActiveAppContext context)
        {
            var result = new ActionResult { ActionId = action.Id, TargetApp = context.ProcessName };

            if (!action.Parameters.TryGetValue("text", out var textObj) || textObj is not string text)
            {
                result.Message = "No text specified";
                return result;
            }

            await Task.Run(() =>
            {
                foreach (char c in text)
                {
                    // Use SendInput for more reliable text entry
                    TypeCharacter(c);
                    System.Threading.Thread.Sleep(10); // Small delay between chars
                }
            });

            result.Success = true;
            result.Message = $"Typed: {text}";
            return result;
        }

        private async Task<ActionResult> ExecuteClickAsync(InAppAction action, ActiveAppContext context)
        {
            var result = new ActionResult { ActionId = action.Id, TargetApp = context.ProcessName };

            if (action.Parameters.TryGetValue("x", out var xObj) && 
                action.Parameters.TryGetValue("y", out var yObj))
            {
                var x = Convert.ToInt32(xObj);
                var y = Convert.ToInt32(yObj);

                await Task.Run(() =>
                {
                    SetCursorPos(x, y);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);
                });

                result.Success = true;
                result.Message = $"Clicked at ({x}, {y})";
            }
            else
            {
                result.Message = "No coordinates specified";
            }

            return result;
        }

        private async Task<ActionResult> ExecuteRunCommandAsync(InAppAction action, ActiveAppContext context)
        {
            var result = new ActionResult { ActionId = action.Id, TargetApp = context.ProcessName };

            if (!action.Parameters.TryGetValue("command", out var cmdObj) || cmdObj is not string command)
            {
                result.Message = "No command specified";
                return result;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    result.Success = process.ExitCode == 0;
                    result.Message = string.IsNullOrEmpty(output) ? "Command executed" : output;
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Command failed: {ex.Message}";
            }

            return result;
        }

        private async Task<ActionResult> ExecuteClipboardAsync(InAppAction action, ActiveAppContext context)
        {
            var result = new ActionResult { ActionId = action.Id, TargetApp = context.ProcessName };

            var operation = action.Parameters.GetValueOrDefault("operation", "copy")?.ToString() ?? "copy";

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (operation.ToLower())
                {
                    case "copy":
                        SendKeys("^c");
                        result.Message = "Copied to clipboard";
                        break;
                    case "paste":
                        SendKeys("^v");
                        result.Message = "Pasted from clipboard";
                        break;
                    case "cut":
                        SendKeys("^x");
                        result.Message = "Cut to clipboard";
                        break;
                    case "clear":
                        Clipboard.Clear();
                        result.Message = "Clipboard cleared";
                        break;
                    default:
                        result.Message = $"Unknown clipboard operation: {operation}";
                        return;
                }
                result.Success = true;
            });

            return result;
        }

        private async Task<ActionResult> ExecuteUIAutomationAsync(InAppAction action, ActiveAppContext context)
        {
            var result = new ActionResult { ActionId = action.Id, TargetApp = context.ProcessName };

            try
            {
                var automationId = action.Parameters.GetValueOrDefault("automationId")?.ToString();
                var name = action.Parameters.GetValueOrDefault("name")?.ToString();
                var controlType = action.Parameters.GetValueOrDefault("controlType")?.ToString();

                AutomationElement? element = null;
                var root = AutomationElement.FromHandle(context.WindowHandle);

                // Find element by automation ID or name
                if (!string.IsNullOrEmpty(automationId))
                {
                    element = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
                }
                else if (!string.IsNullOrEmpty(name))
                {
                    element = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, name));
                }

                if (element == null)
                {
                    result.Message = "UI element not found";
                    return result;
                }

                // Try to invoke or click the element
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokeObj))
                {
                    await Task.Run(() => ((InvokePattern)invokeObj).Invoke());
                    result.Success = true;
                    result.Message = $"Invoked element: {element.Current.Name}";
                }
                else
                {
                    // Fallback to click
                    var rect = element.Current.BoundingRectangle;
                    var x = (int)(rect.X + rect.Width / 2);
                    var y = (int)(rect.Y + rect.Height / 2);

                    await Task.Run(() =>
                    {
                        SetCursorPos(x, y);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
                        mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);
                    });

                    result.Success = true;
                    result.Message = $"Clicked element: {element.Current.Name}";
                }
            }
            catch (Exception ex)
            {
                result.Message = $"UI Automation error: {ex.Message}";
            }

            return result;
        }

        private void SendKeys(string keys)
        {
            // Parse and send key combinations
            var modifiers = new List<byte>();
            var i = 0;

            while (i < keys.Length)
            {
                if (keys[i] == '^') { modifiers.Add(VK_CONTROL); i++; }
                else if (keys[i] == '+') { modifiers.Add(VK_SHIFT); i++; }
                else if (keys[i] == '%') { modifiers.Add(VK_MENU); i++; }
                else break;
            }

            // Press modifiers
            foreach (var mod in modifiers)
                keybd_event(mod, 0, 0, UIntPtr.Zero);

            // Send remaining keys
            for (; i < keys.Length; i++)
            {
                var vk = VkKeyScan(keys[i]);
                keybd_event((byte)(vk & 0xFF), 0, 0, UIntPtr.Zero);
                keybd_event((byte)(vk & 0xFF), 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }

            // Release modifiers
            foreach (var mod in modifiers)
                keybd_event(mod, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void TypeCharacter(char c)
        {
            var vk = VkKeyScan(c);
            var needsShift = (vk >> 8 & 1) != 0;

            if (needsShift)
                keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);

            keybd_event((byte)(vk & 0xFF), 0, 0, UIntPtr.Zero);
            keybd_event((byte)(vk & 0xFF), 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            if (needsShift)
                keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        #region Win32 Imports
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_MENU = 0x12; // Alt
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);
        #endregion
    }
}
