using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AtlasAI.InAppAssistant.Models;

namespace AtlasAI.InAppAssistant.Services
{
    /// <summary>
    /// Manages per-app permissions for in-app actions
    /// </summary>
    public class AppPermissionManager
    {
        private readonly string _permissionsPath;
        private Dictionary<string, AppPermission> _permissions = new();

        public event EventHandler<AppPermission>? PermissionChanged;

        public AppPermissionManager()
        {
            _permissionsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "app_permissions.json");
            Load();
        }

        /// <summary>
        /// Check if an action type is permitted for a process
        /// </summary>
        public bool HasPermission(string processName, ActionType actionType)
        {
            var key = processName.ToLower();
            if (!_permissions.TryGetValue(key, out var perm))
                return false; // No permission by default

            return actionType switch
            {
                ActionType.SendKeys => perm.AllowKeystrokes,
                ActionType.TypeText => perm.AllowKeystrokes,
                ActionType.Click => perm.AllowClicks,
                ActionType.UIAutomation => perm.AllowClicks,
                ActionType.ClipboardOperation => perm.AllowTextCapture,
                ActionType.FileOperation => perm.AllowFileOperations,
                ActionType.RunCommand => perm.AllowFileOperations,
                ActionType.OpenMenu => perm.AllowKeystrokes,
                _ => false
            };
        }

        /// <summary>
        /// Check if confirmation is required for a process
        /// </summary>
        public bool RequiresConfirmation(string processName)
        {
            var key = processName.ToLower();
            return !_permissions.TryGetValue(key, out var perm) || perm.RequireConfirmation;
        }

        /// <summary>
        /// Get permission settings for a process
        /// </summary>
        public AppPermission? GetPermission(string processName)
        {
            var key = processName.ToLower();
            return _permissions.GetValueOrDefault(key);
        }

        /// <summary>
        /// Get all configured permissions
        /// </summary>
        public IReadOnlyList<AppPermission> GetAllPermissions()
        {
            return _permissions.Values.ToList();
        }

        /// <summary>
        /// Set permission for a process
        /// </summary>
        public void SetPermission(AppPermission permission)
        {
            var key = permission.ProcessName.ToLower();
            _permissions[key] = permission;
            permission.LastUsed = DateTime.Now;
            Save();
            PermissionChanged?.Invoke(this, permission);
            Debug.WriteLine($"[Permissions] Updated permissions for {permission.ProcessName}");
        }

        /// <summary>
        /// Grant all permissions for a process (trusted app)
        /// </summary>
        public void GrantAllPermissions(string processName, string displayName)
        {
            SetPermission(new AppPermission
            {
                ProcessName = processName,
                DisplayName = displayName,
                AllowKeystrokes = true,
                AllowClicks = true,
                AllowTextCapture = true,
                AllowFileOperations = true,
                RequireConfirmation = false
            });
        }

        /// <summary>
        /// Revoke all permissions for a process
        /// </summary>
        public void RevokeAllPermissions(string processName)
        {
            var key = processName.ToLower();
            if (_permissions.Remove(key))
            {
                Save();
                Debug.WriteLine($"[Permissions] Revoked all permissions for {processName}");
            }
        }

        /// <summary>
        /// Record usage of a permission
        /// </summary>
        public void RecordUsage(string processName)
        {
            var key = processName.ToLower();
            if (_permissions.TryGetValue(key, out var perm))
            {
                perm.LastUsed = DateTime.Now;
                perm.UsageCount++;
                Save();
            }
        }

        /// <summary>
        /// Create default permissions for common apps
        /// </summary>
        public void CreateDefaultPermissions()
        {
            // File Explorer - allow file operations
            if (!_permissions.ContainsKey("explorer"))
            {
                SetPermission(new AppPermission
                {
                    ProcessName = "explorer",
                    DisplayName = "File Explorer",
                    AllowKeystrokes = true,
                    AllowClicks = true,
                    AllowFileOperations = true,
                    RequireConfirmation = true
                });
            }

            // Notepad - allow text operations
            if (!_permissions.ContainsKey("notepad"))
            {
                SetPermission(new AppPermission
                {
                    ProcessName = "notepad",
                    DisplayName = "Notepad",
                    AllowKeystrokes = true,
                    AllowTextCapture = true,
                    RequireConfirmation = false
                });
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_permissionsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_permissions, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_permissionsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Permissions] Save error: {ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_permissionsPath))
                {
                    CreateDefaultPermissions();
                    return;
                }

                var json = File.ReadAllText(_permissionsPath);
                _permissions = JsonSerializer.Deserialize<Dictionary<string, AppPermission>>(json) ?? new();
                Debug.WriteLine($"[Permissions] Loaded {_permissions.Count} app permissions");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Permissions] Load error: {ex.Message}");
                _permissions = new();
                CreateDefaultPermissions();
            }
        }
    }
}
