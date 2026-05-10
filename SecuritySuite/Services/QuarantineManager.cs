using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.SecuritySuite.Models;

namespace AtlasAI.SecuritySuite.Services
{
    /// <summary>
    /// Manages quarantined files - secure storage with restore/delete capability
    /// Enhanced with better error handling and logging
    /// </summary>
    public class QuarantineManager
    {
        private static readonly string QuarantineDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AtlasAI", "SecuritySuite", "quarantine");
        
        private static readonly string IndexPath = Path.Combine(QuarantineDir, "index.json");
        private static readonly string LogPath = Path.Combine(QuarantineDir, "quarantine_log.txt");
        
        private List<QuarantinedItem> _items = new();
        private readonly object _lock = new();
        
        public event Action<string>? StatusChanged;
        public event Action<QuarantinedItem>? ItemQuarantined;
        public event Action<QuarantinedItem>? ItemRestored;
        public event Action<QuarantinedItem>? ItemDeleted;
        
        public IReadOnlyList<QuarantinedItem> Items => _items.AsReadOnly();
        public int ActiveCount => _items.Count(i => i.Status == QuarantineStatus.Active);
        
        public QuarantineManager()
        {
            EnsureDirectoryExists();
            LoadIndex();
        }
        
        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(QuarantineDir))
                {
                    Directory.CreateDirectory(QuarantineDir);
                    Log("Quarantine directory created");
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: Failed to create quarantine directory: {ex.Message}");
            }
        }
        
        private void LoadIndex()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(IndexPath))
                    {
                        var json = File.ReadAllText(IndexPath);
                        _items = JsonSerializer.Deserialize<List<QuarantinedItem>>(json) ?? new();
                        Log($"Loaded {_items.Count} quarantine entries");
                    }
                }
                catch (Exception ex)
                {
                    Log($"ERROR: Failed to load quarantine index: {ex.Message}");
                    _items = new();
                }
            }
        }
        
        private void SaveIndex()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(IndexPath, json);
                }
                catch (Exception ex)
                {
                    Log($"ERROR: Failed to save quarantine index: {ex.Message}");
                }
            }
        }
        
        private void Log(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(LogPath, logMessage + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine($"[QuarantineManager] {message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuarantineManager] Log failure: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Quarantine a file - moves it to secure storage
        /// </summary>
        public async Task<(bool Success, string Message)> QuarantineFileAsync(string filePath, SecurityFinding finding)
        {
            StatusChanged?.Invoke($"Quarantining: {Path.GetFileName(filePath)}...");
            
            try
            {
                // Validate file exists
                if (!File.Exists(filePath))
                {
                    Log($"File not found for quarantine: {filePath}");
                    return (false, "File not found - it may have been moved or deleted");
                }
                
                // Check if already quarantined
                if (_items.Any(i => i.OriginalPath.Equals(filePath, StringComparison.OrdinalIgnoreCase) && i.Status == QuarantineStatus.Active))
                {
                    Log($"File already quarantined: {filePath}");
                    return (false, "File is already in quarantine");
                }
                
                var fileInfo = new FileInfo(filePath);
                
                // Check file size (limit to 100MB for quarantine)
                if (fileInfo.Length > 100 * 1024 * 1024)
                {
                    Log($"File too large for quarantine: {filePath} ({fileInfo.Length} bytes)");
                    return (false, "File is too large to quarantine (max 100MB). Consider deleting instead.");
                }
                
                // Check if file is in use
                if (IsFileLocked(filePath))
                {
                    Log($"File is locked/in use: {filePath}");
                    return (false, "File is currently in use. Close any programs using it and try again.");
                }
                
                var hash = await ComputeHashAsync(filePath);
                var quarantineName = $"{Guid.NewGuid()}.qtn";
                var quarantinePath = Path.Combine(QuarantineDir, quarantineName);
                
                Log($"Quarantining file: {filePath} -> {quarantinePath}");
                
                // Encrypt and move file
                await EncryptAndMoveAsync(filePath, quarantinePath);
                
                var item = new QuarantinedItem
                {
                    OriginalPath = filePath,
                    QuarantinePath = quarantinePath,
                    FileHash = hash,
                    FileSizeBytes = fileInfo.Length,
                    ThreatCategory = finding.Category,
                    Severity = finding.Severity,
                    ThreatName = finding.Title,
                    Status = QuarantineStatus.Active
                };
                
                lock (_lock)
                {
                    _items.Add(item);
                }
                SaveIndex();
                
                ItemQuarantined?.Invoke(item);
                StatusChanged?.Invoke($"✅ Quarantined: {Path.GetFileName(filePath)}");
                Log($"Successfully quarantined: {filePath}");
                
                return (true, $"✅ Quarantined: {Path.GetFileName(filePath)}");
            }
            catch (UnauthorizedAccessException)
            {
                Log($"Access denied quarantining: {filePath}");
                return (false, "Access denied. Try running as Administrator.");
            }
            catch (IOException ioEx)
            {
                Log($"IO error quarantining {filePath}: {ioEx.Message}");
                return (false, $"File operation failed: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                Log($"Error quarantining {filePath}: {ex.Message}");
                return (false, $"Failed to quarantine: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if a file is locked by another process
        /// </summary>
        private bool IsFileLocked(string filePath)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Restore a quarantined file to its original location
        /// </summary>
        public async Task<(bool Success, string Message)> RestoreAsync(string itemId)
        {
            var item = _items.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
            {
                Log($"Restore failed - item not found: {itemId}");
                return (false, "Quarantine item not found");
            }
            
            if (item.Status != QuarantineStatus.Active)
            {
                Log($"Restore failed - item not active: {itemId}");
                return (false, "Item is not active in quarantine");
            }
            
            StatusChanged?.Invoke($"Restoring: {Path.GetFileName(item.OriginalPath)}...");
            
            try
            {
                if (!File.Exists(item.QuarantinePath))
                {
                    Log($"Quarantine file missing: {item.QuarantinePath}");
                    item.Status = QuarantineStatus.Deleted;
                    SaveIndex();
                    return (false, "Quarantine file is missing - cannot restore");
                }
                
                // Check if original location already has a file
                if (File.Exists(item.OriginalPath))
                {
                    Log($"Original path already has file: {item.OriginalPath}");
                    return (false, "A file already exists at the original location. Delete it first or restore to a different location.");
                }
                
                Log($"Restoring file: {item.QuarantinePath} -> {item.OriginalPath}");
                
                // Decrypt and restore
                await DecryptAndRestoreAsync(item.QuarantinePath, item.OriginalPath);
                
                // Delete quarantine file
                File.Delete(item.QuarantinePath);
                
                item.Status = QuarantineStatus.Restored;
                item.RestoredAt = DateTime.Now;
                SaveIndex();
                
                ItemRestored?.Invoke(item);
                StatusChanged?.Invoke($"✅ Restored: {Path.GetFileName(item.OriginalPath)}");
                Log($"Successfully restored: {item.OriginalPath}");
                
                return (true, $"✅ Restored: {Path.GetFileName(item.OriginalPath)}");
            }
            catch (UnauthorizedAccessException)
            {
                Log($"Access denied restoring: {item.OriginalPath}");
                return (false, "Access denied. Try running as Administrator.");
            }
            catch (Exception ex)
            {
                Log($"Error restoring {item.OriginalPath}: {ex.Message}");
                return (false, $"Failed to restore: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Permanently delete a quarantined file
        /// </summary>
        public async Task<(bool Success, string Message)> DeletePermanentlyAsync(string itemId)
        {
            var item = _items.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
            {
                Log($"Delete failed - item not found: {itemId}");
                return (false, "Quarantine item not found");
            }
            
            StatusChanged?.Invoke($"Deleting: {Path.GetFileName(item.OriginalPath)}...");
            
            // SAFETY GATE: Check before permanent delete
            var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                OperationType.FileDelete,
                OperationRisk.High,
                $"Permanently delete quarantined file: {Path.GetFileName(item.OriginalPath)}",
                new Dictionary<string, object>
                {
                    ["path"] = item.QuarantinePath,
                    ["originalPath"] = item.OriginalPath,
                    ["reason"] = "permanent quarantine deletion"
                });
            
            if (safetyCheck.Decision == SafetyDecision.Blocked)
            {
                return (false, safetyCheck.Message);
            }
            
            try
            {
                await Task.Run(() =>
                {
                    if (File.Exists(item.QuarantinePath))
                    {
                        Log($"Secure deleting: {item.QuarantinePath}");
                        // Secure delete - overwrite with random data
                        SecureDelete(item.QuarantinePath);
                    }
                    else
                    {
                        Log($"Quarantine file already gone: {item.QuarantinePath}");
                    }
                });
                
                item.Status = QuarantineStatus.Deleted;
                item.DeletedAt = DateTime.Now;
                SaveIndex();
                
                ItemDeleted?.Invoke(item);
                StatusChanged?.Invoke($"✅ Permanently deleted: {Path.GetFileName(item.OriginalPath)}");
                Log($"Successfully deleted: {item.OriginalPath}");
                
                return (true, $"✅ Permanently deleted: {Path.GetFileName(item.OriginalPath)}");
            }
            catch (Exception ex)
            {
                Log($"Error deleting {item.QuarantinePath}: {ex.Message}");
                return (false, $"Failed to delete: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get all active quarantined items
        /// </summary>
        public List<QuarantinedItem> GetActiveItems()
        {
            lock (_lock)
            {
                return _items.Where(i => i.Status == QuarantineStatus.Active).ToList();
            }
        }
        
        /// <summary>
        /// Refresh the quarantine list from disk
        /// </summary>
        public void Refresh()
        {
            LoadIndex();
            StatusChanged?.Invoke($"Quarantine refreshed: {ActiveCount} active items");
        }
        
        /// <summary>
        /// Clean up old deleted/restored items from index
        /// </summary>
        public void CleanupIndex(int daysOld = 30)
        {
            var cutoff = DateTime.Now.AddDays(-daysOld);
            lock (_lock)
            {
                var removed = _items.RemoveAll(i => 
                    (i.Status == QuarantineStatus.Deleted && i.DeletedAt < cutoff) ||
                    (i.Status == QuarantineStatus.Restored && i.RestoredAt < cutoff));
                
                if (removed > 0)
                {
                    Log($"Cleaned up {removed} old quarantine entries");
                    SaveIndex();
                }
            }
        }
        
        #region Encryption Helpers
        
        private async Task EncryptAndMoveAsync(string sourcePath, string destPath)
        {
            // Simple XOR encryption for quarantine (not cryptographically secure, but prevents accidental execution)
            var key = GetQuarantineKey();
            var data = await File.ReadAllBytesAsync(sourcePath);
            
            for (int i = 0; i < data.Length; i++)
                data[i] ^= key[i % key.Length];
            
            await File.WriteAllBytesAsync(destPath, data);
            File.Delete(sourcePath);
        }
        
        private async Task DecryptAndRestoreAsync(string sourcePath, string destPath)
        {
            var key = GetQuarantineKey();
            var data = await File.ReadAllBytesAsync(sourcePath);
            
            for (int i = 0; i < data.Length; i++)
                data[i] ^= key[i % key.Length];
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            await File.WriteAllBytesAsync(destPath, data);
        }
        
        private byte[] GetQuarantineKey()
        {
            // Generate a machine-specific key
            var machineId = Environment.MachineName + Environment.UserName;
            using var sha = SHA256.Create();
            return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(machineId));
        }
        
        private async Task<string> ComputeHashAsync(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        
        private void SecureDelete(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                var length = fi.Length;
                
                // Overwrite with random data
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write))
                {
                    var random = new byte[4096];
                    using var rng = RandomNumberGenerator.Create();
                    
                    long written = 0;
                    while (written < length)
                    {
                        rng.GetBytes(random);
                        var toWrite = (int)Math.Min(random.Length, length - written);
                        fs.Write(random, 0, toWrite);
                        written += toWrite;
                    }
                }
                
                // Delete the file
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Log($"SecureDelete failed: {ex.Message}");
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception fallbackEx)
                {
                    Log($"Fallback delete failed: {fallbackEx.Message}");
                }
            }
        }
        
        #endregion
    }
}
