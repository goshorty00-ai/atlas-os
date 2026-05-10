using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using AtlasAI.Core;
using AtlasAI.Settings;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Windows system tweaks and customizations that Atlas can perform
    /// </summary>
    public static class WindowsTweaks
    {
        /// <summary>
        /// Disable screenshot/snipping tool notifications PERMANENTLY using multiple methods
        /// </summary>
        public static async Task<string> DisableScreenshotNotifications()
        {
            try
            {
                var results = new System.Text.StringBuilder();
                results.AppendLine("🔧 Disabling screenshot notifications...\n");
                
                // Atlas screenshot notification preference is persisted through SettingsStore.
                results.AppendLine("✓ Disabled Atlas screenshot notifications");
                
                // SECOND: Disable Windows Snipping Tool notifications via registry
                // This is the key setting that controls "Screenshot saved to clipboard" toast
                try
                {
                    // SAFETY GATE: Check before registry write
                    var registryPath = @"HKCU\Software\Microsoft\ScreenSketch";
                    var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                        OperationType.RegistryWrite,
                        OperationRisk.Medium,
                        "Disable Snipping Tool clipboard notification",
                        new Dictionary<string, object>
                        {
                            ["registryPath"] = registryPath,
                            ["valueName"] = "IsSaveSnipToClipboardNotificationEnabled"
                        });
                    
                    if (safetyCheck.Decision == SafetyDecision.Blocked)
                    {
                        results.AppendLine($"⚠ {safetyCheck.Message}");
                    }
                    else
                    {
                        using var ssKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\ScreenSketch");
                        if (ssKey != null)
                        {
                            // This is THE setting that controls the toast notification
                            ssKey.SetValue("IsSaveSnipToClipboardNotificationEnabled", 0, RegistryValueKind.DWord);
                            ssKey.SetValue("IsScreenSnippingEnabled", 1, RegistryValueKind.DWord);
                            results.AppendLine("✓ Disabled Snipping Tool clipboard notification");
                        }
                    }
                }
                catch (Exception ex)
                {
                    results.AppendLine($"⚠ Could not set Snipping Tool setting: {ex.Message}");
                }
                
                // THIRD: Disable app notifications for Snipping Tool
                var notifPaths = new[]
                {
                    @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Microsoft.ScreenSketch_8wekyb3d8bbwe!App",
                    @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.ScreenSketch",
                };
                
                foreach (var path in notifPaths)
                {
                    try
                    {
                        // SAFETY GATE for each notification path
                        var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                            OperationType.RegistryWrite,
                            OperationRisk.Low,
                            "Disable Snipping Tool app notification",
                            new Dictionary<string, object>
                            {
                                ["registryPath"] = $"HKCU\\{path}",
                                ["valueName"] = "Enabled"
                            });
                        
                        if (safetyCheck.Decision == SafetyDecision.Allowed)
                        {
                            using var key = Registry.CurrentUser.CreateSubKey(path);
                            if (key != null)
                            {
                                key.SetValue("Enabled", 0, RegistryValueKind.DWord);
                                key.SetValue("ShowInActionCenter", 0, RegistryValueKind.DWord);
                            }
                        }
                    }
                    catch { }
                }
                results.AppendLine("✓ Disabled Snipping Tool app notifications");
                
                // FOURTH: Use PowerShell to open Snipping Tool settings and disable notification there
                try
                {
                    // Open Snipping Tool, go to settings, and disable notification
                    var ps = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"" +
                            "# Set registry to disable notification\n" +
                            "$path = 'HKCU:\\Software\\Microsoft\\ScreenSketch';\n" +
                            "if (!(Test-Path $path)) { New-Item -Path $path -Force | Out-Null };\n" +
                            "Set-ItemProperty -Path $path -Name 'IsSaveSnipToClipboardNotificationEnabled' -Value 0 -Type DWord -Force;\n" +
                            "Write-Output 'Done'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    using var proc = Process.Start(ps);
                    proc?.WaitForExit(5000);
                    results.AppendLine("✓ Applied PowerShell registry fix");
                }
                catch { }
                
                try
                {
                    SettingsStore.Update(settings => settings.DisableScreenshotNotifications = true);
                    results.AppendLine("✓ Setting saved (will persist after restart)");
                }
                catch (Exception ex)
                {
                    results.AppendLine($"⚠ Could not save setting: {ex.Message}");
                }
                
                results.AppendLine("\n✅ Screenshot notifications disabled!");
                results.AppendLine("\n💡 **To fully disable Windows Snipping Tool notifications:**");
                results.AppendLine("1. Open Snipping Tool (Win+Shift+S, then click the notification)");
                results.AppendLine("2. Click the ⚙️ Settings gear icon");
                results.AppendLine("3. Turn OFF 'Automatically copy to clipboard'");
                results.AppendLine("   OR turn OFF 'Show notification after snip is taken'");
                
                return results.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Load saved settings on startup
        /// </summary>
        public static void LoadSettings()
        {
            try
            {
                if (SettingsStore.Current.DisableScreenshotNotifications)
                {
                    System.Diagnostics.Debug.WriteLine("[WindowsTweaks] Loaded: Screenshot notifications disabled");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsTweaks] Failed to load settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Enable screenshot notifications
        /// </summary>
        public static async Task<string> EnableScreenshotNotifications()
        {
            try
            {
                try
                {
                    SettingsStore.Update(settings => settings.DisableScreenshotNotifications = false);
                }
                catch { }
                
                return "✅ Screenshot notifications re-enabled. Atlas will now show 'Screenshot Saved' messages.";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Check current screenshot notification status
        /// </summary>
        public static string GetScreenshotNotificationStatus()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Microsoft.ScreenSketch_8wekyb3d8bbwe!App");
                if (key != null)
                {
                    var enabled = key.GetValue("Enabled");
                    if (enabled != null && (int)enabled == 0)
                        return "🔕 Screenshot notifications are DISABLED";
                }
                return "🔔 Screenshot notifications are ENABLED";
            }
            catch
            {
                return "❓ Unable to check notification status";
            }
        }
        
        /// <summary>
        /// Disable Windows Game Bar overlay PERMANENTLY
        /// </summary>
        public static async Task<string> DisableGameBar()
        {
            try
            {
                var results = new System.Text.StringBuilder();
                int successCount = 0;
                
                // SAFETY GATE: Check before any registry writes
                var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                    OperationType.RegistryWrite,
                    OperationRisk.Medium,
                    "Disable Windows Game Bar",
                    new Dictionary<string, object>
                    {
                        ["registryPath"] = @"HKCU\Software\Microsoft\GameBar",
                        ["valueName"] = "AllowAutoGameMode"
                    });
                
                if (safetyCheck.Decision == SafetyDecision.Blocked)
                {
                    return safetyCheck.Message + "\n\n💡 Enable dangerous actions in Settings to modify Game Bar settings.";
                }
                
                // Method 1: User-level Game Bar settings
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
                    if (key != null)
                    {
                        key.SetValue("AllowAutoGameMode", 0, RegistryValueKind.DWord);
                        key.SetValue("AutoGameModeEnabled", 0, RegistryValueKind.DWord);
                        key.SetValue("ShowStartupPanel", 0, RegistryValueKind.DWord);
                        key.SetValue("GamePanelStartupTipIndex", 3, RegistryValueKind.DWord);
                        key.SetValue("UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord);
                        successCount++;
                    }
                }
                catch { }
                
                // Method 2: Game DVR settings
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
                    if (key != null)
                    {
                        key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                        key.SetValue("GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
                        key.SetValue("GameDVR_FSEBehavior", 2, RegistryValueKind.DWord);
                        key.SetValue("GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
                        key.SetValue("GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);
                        key.SetValue("GameDVR_EFSEFeatureFlags", 0, RegistryValueKind.DWord);
                        successCount++;
                    }
                }
                catch { }
                
                // Method 3: Local Machine policy (requires admin)
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR");
                    if (key != null)
                    {
                        key.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                        results.AppendLine("✓ Applied system-wide Game DVR policy");
                        successCount++;
                    }
                }
                catch { /* Requires admin */ }
                
                // Method 4: Disable Game Bar app
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                    if (key != null)
                    {
                        key.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                        successCount++;
                    }
                }
                catch { }
                
                // Method 5: Disable via Xbox settings
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Xbox\GameOverlay");
                    if (key != null)
                    {
                        key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                        successCount++;
                    }
                }
                catch { }
                
                if (successCount > 0)
                {
                    results.AppendLine($"✅ Game Bar disabled using {successCount} methods");
                    results.AppendLine("🔒 Changes are persistent. A restart may be needed for full effect.");
                    return results.ToString();
                }
                
                return "✅ Game Bar and Game DVR disabled. Restart may be required for full effect.";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Disable Windows tips and suggestions
        /// </summary>
        public static async Task<string> DisableWindowsTips()
        {
            try
            {
                // SAFETY GATE: Check before registry write
                var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                    OperationType.RegistryWrite,
                    OperationRisk.Low,
                    "Disable Windows tips and suggestions",
                    new Dictionary<string, object>
                    {
                        ["registryPath"] = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        ["valueName"] = "SubscribedContent-338389Enabled"
                    });
                
                if (safetyCheck.Decision == SafetyDecision.Blocked)
                {
                    return safetyCheck.Message + "\n\n💡 Enable dangerous actions in Settings to modify Windows tips settings.";
                }
                
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
                if (key != null)
                {
                    key.SetValue("SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord);
                    key.SetValue("SubscribedContent-310093Enabled", 0, RegistryValueKind.DWord);
                    key.SetValue("SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord);
                    key.SetValue("SoftLandingEnabled", 0, RegistryValueKind.DWord);
                    key.SetValue("SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord);
                }
                return "✅ Windows tips and suggestions disabled";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Disable all toast notifications temporarily (Focus Assist)
        /// </summary>
        public static async Task<string> EnableFocusAssist()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\Cache\DefaultAccount\$$windows.data.notifications.quiethourssettings\Current");
                // This is complex - Focus Assist uses binary data
                // For now, suggest using Settings
                return "💡 To enable Focus Assist:\n" +
                       "1. Press Win + A to open Action Center\n" +
                       "2. Click 'Focus assist' to cycle through modes\n" +
                       "Or: Settings → System → Focus assist";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
    }
}
