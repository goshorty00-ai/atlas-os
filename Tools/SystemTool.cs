using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.ActionHistory;

namespace AtlasAI.Tools
{
    public static class SystemTool
    {
        /// <summary>
        /// Open a URL in the default browser
        /// </summary>
        public static Task<string> OpenUrlAsync(string url)
        {
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;
                    
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return Task.FromResult($"✅ Opened {url} in browser");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to open URL: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Open an application
        /// </summary>
        public static async Task<string> OpenAppAsync(string appName, System.Threading.CancellationToken ct = default)
        {
			var displayName = appName;
            try
            {
                ct.ThrowIfCancellationRequested();
                var appLower = appName.ToLower().Trim();
                var addonAppAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "stremio",
                    "serveraddons",
                    "addons",
                    "addonservers",
                    "server addons",
                    "addon servers"
                };
                const string legacyAddonHostExecutable = "stremio";
                displayName = addonAppAliases.Contains(appLower)
					? "Server Addons"
					: appName;
                
                // UWP/Store apps - use shell: protocol or start command
                var uwpApps = new Dictionary<string, string>
                {
                    ["spotify"] = "spotify:",
                    ["netflix"] = "netflix:",
                    ["xbox"] = "xbox:",
                    ["xbox game bar"] = "xbox:",
                    ["store"] = "ms-windows-store:",
                    ["microsoft store"] = "ms-windows-store:",
                    ["mail"] = "outlookmail:",
                    ["calendar"] = "outlookcal:",
                    ["photos"] = "ms-photos:",
                    ["camera"] = "microsoft.windows.camera:",
                    ["maps"] = "bingmaps:",
                    ["weather"] = "bingweather:",
                    ["news"] = "bingnews:",
                    ["groove"] = "mswindowsmusic:",
                    ["movies"] = "mswindowsvideo:",
                    ["onenote"] = "onenote:",
                    ["skype"] = "skype:",
                    ["teams"] = "msteams:",
                    ["whatsapp"] = "whatsapp:",
                    ["telegram"] = "tg:",
                    ["your phone"] = "ms-phone:",
                    ["phone link"] = "ms-phone:",
                    ["clock"] = "ms-clock:",
                    ["alarms"] = "ms-clock:",
                    ["calculator"] = "calculator:",
                    ["snipping tool"] = "ms-screenclip:",
                    ["screen snip"] = "ms-screenclip:",
                };

                // Check if it's a UWP app
                if (uwpApps.TryGetValue(appLower, out var uwpUri))
                {
                    Process.Start(new ProcessStartInfo(uwpUri) { UseShellExecute = true });
                    // Record action for undo
					ActionHistoryManager.Instance.RecordAction(ActionRecord.AppOpened(displayName, appLower));
					return $"✅ Opened {displayName}\n\n💡 Say 'undo' to close it.";
                }

                // Common desktop app mappings
                var desktopApps = new Dictionary<string, string>
                {
                    ["notepad"] = "notepad.exe",
                    ["calc"] = "calc.exe",
                    ["paint"] = "mspaint.exe",
                    ["explorer"] = "explorer.exe",
                    ["file explorer"] = "explorer.exe",
                    ["cmd"] = "cmd.exe",
                    ["command prompt"] = "cmd.exe",
                    ["terminal"] = "wt.exe",
                    ["powershell"] = "powershell.exe",
                    ["settings"] = "ms-settings:",
                    ["control panel"] = "control.exe",
                    ["task manager"] = "taskmgr.exe",
                    ["chrome"] = "chrome",
                    ["google chrome"] = "chrome",
                    ["firefox"] = "firefox",
                    ["edge"] = "msedge",
                    ["microsoft edge"] = "msedge",
                    ["brave"] = "brave",
                    ["opera"] = "opera",
                    ["word"] = "winword",
                    ["excel"] = "excel",
                    ["powerpoint"] = "powerpnt",
                    ["outlook"] = "outlook",
                    ["discord"] = "discord",
                    ["steam"] = "steam",
                    ["vscode"] = "code",
                    ["visual studio code"] = "code",
                    ["visual studio"] = "devenv",
                    ["obs"] = "obs64",
                    ["obs studio"] = "obs64",
                    ["vlc"] = "vlc",
                    ["audacity"] = "audacity",
                    ["gimp"] = "gimp",
                    ["blender"] = "blender",
                    ["unity"] = "Unity Hub",
                    ["slack"] = "slack",
                    ["zoom"] = "zoom",
                    ["notion"] = "Notion",
                    ["postman"] = "Postman",
                    ["git bash"] = "git-bash",
                    ["notepad++"] = "notepad++",
                    ["sublime"] = "sublime_text",
                    ["sublime text"] = "sublime_text",
                    ["winrar"] = "WinRAR",
                    ["7zip"] = "7zFM",
                    ["7-zip"] = "7zFM",
                    ["itunes"] = "iTunes",
                    ["apple music"] = "iTunes",
                    ["serveraddons"] = legacyAddonHostExecutable,
                    ["server addons"] = legacyAddonHostExecutable,
                    ["addons"] = legacyAddonHostExecutable,
                    ["addonservers"] = legacyAddonHostExecutable,
                    ["addon servers"] = legacyAddonHostExecutable,
                    ["stremio"] = legacyAddonHostExecutable,
                    ["plex"] = "Plex",
                    ["kodi"] = "kodi",
                    ["qbittorrent"] = "qbittorrent",
                    ["utorrent"] = "uTorrent",
                    ["handbrake"] = "HandBrake",
                    ["davinci"] = "Resolve",
                    ["davinci resolve"] = "Resolve",
                    ["premiere"] = "Adobe Premiere Pro",
                    ["after effects"] = "AfterFX",
                    ["photoshop"] = "Photoshop",
                    ["illustrator"] = "Illustrator",
                };

                string appToLaunch;
                string processName;
                if (desktopApps.TryGetValue(appLower, out var desktopApp))
                {
                    appToLaunch = desktopApp;
                    processName = Path.GetFileNameWithoutExtension(desktopApp);
                }
                else
                {
                    appToLaunch = appName;
                    processName = appName;
                }

                // Try to start the app
                try
                {
                    Process.Start(new ProcessStartInfo(appToLaunch) { UseShellExecute = true });
                    // Record action for undo
					ActionHistoryManager.Instance.RecordAction(ActionRecord.AppOpened(displayName, processName));
					return $"✅ Opened {displayName}\n\n💡 Say 'undo' to close it.";
                }
                catch
                {
                    // If direct launch fails, try searching in Start Menu
                    var startMenuPaths = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs)),
                    };

                    foreach (var startPath in startMenuPaths)
                    {
                        if (!Directory.Exists(startPath)) continue;
                        
                        var shortcuts = Directory.GetFiles(startPath, "*.lnk", SearchOption.AllDirectories);
                        foreach (var shortcut in shortcuts)
                        {
                            var shortcutName = Path.GetFileNameWithoutExtension(shortcut).ToLower();
                            // Require exact match or the shortcut name starts with the app name
                            // This prevents "soundcloud" from matching "CLO Standalone" 
                            if (shortcutName == appLower || 
                                shortcutName.StartsWith(appLower + " ") || 
                                shortcutName.StartsWith(appLower + "-") ||
                                shortcutName == appLower.Replace(" ", "") ||
                                (appLower.Length >= 4 && shortcutName.StartsWith(appLower)))
                            {
                                Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true });
								var shortcutDisplayName = Path.GetFileNameWithoutExtension(shortcut);
								ActionHistoryManager.Instance.RecordAction(ActionRecord.AppOpened(displayName, shortcutDisplayName));
								return $"✅ Opened {displayName}\n\n💡 Say 'undo' to close it.";
                            }
                        }
                    }

                    // Last resort: try shell:AppsFolder for UWP apps
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-Command \"Start-Process 'shell:AppsFolder\\*{appName}*'\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(psi);
							ActionHistoryManager.Instance.RecordAction(ActionRecord.AppOpened(displayName, appName));
							return $"✅ Opened {displayName}\n\n💡 Say 'undo' to close it.";
                    }
                    catch
                    {
							return $"❌ Could not find {displayName}. Try the exact app name.";
                    }
                }
            }
            catch (Exception ex)
            {
				return $"❌ Failed to open {displayName}: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Run a shell command and return output
        /// </summary>
        public static async Task<string> RunCommandAsync(string command)
        {
            // SAFETY GATE: Check with SafetyKernel before executing commands
            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                AtlasAI.Core.OperationType.CommandExecution,
                AtlasAI.Core.OperationRisk.High,
                $"Execute command: {command}",
                new Dictionary<string, object>
                {
                    ["command"] = command
                });

            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
            {
                System.Diagnostics.Debug.WriteLine($"[SystemTool] Command blocked: {command}");
                return safetyCheck.Message + "\n\n💡 Command execution is disabled in Safety Mode.";
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                    return "❌ Failed to start process";
                    
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                var result = "";
                if (!string.IsNullOrEmpty(output))
                    result += output;
                if (!string.IsNullOrEmpty(error))
                    result += $"\n⚠️ {error}";
                    
                return string.IsNullOrEmpty(result) ? "✅ Command completed (no output)" : result;
            }
            catch (Exception ex)
            {
                return $"❌ Command error: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Get system information
        /// </summary>
        public static Task<string> GetSystemInfoAsync()
        {
            try
            {
                var info = $"System Information:\n\n";
                info += $"🖥️ Computer: {Environment.MachineName}\n";
                info += $"👤 User: {Environment.UserName}\n";
                info += $"💻 OS: {Environment.OSVersion}\n";
                info += $"🔧 Processors: {Environment.ProcessorCount}\n";
                info += $"📁 Current Directory: {Environment.CurrentDirectory}\n";
                info += $"🕐 System Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64)}\n";
                
                // Memory info
                var gcInfo = GC.GetGCMemoryInfo();
                var totalMemoryMB = gcInfo.TotalAvailableMemoryBytes / 1024 / 1024;
                info += $"💾 Available Memory: {totalMemoryMB:N0} MB\n";
                
                return Task.FromResult(info);
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error getting system info: {ex.Message}");
            }
        }
        
        /// <summary>
        /// List files in a directory
        /// </summary>
        public static Task<string> ListFilesAsync(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    
                if (!Directory.Exists(path))
                    return Task.FromResult($"❌ Directory not found: {path}");
                    
                var result = $"Contents of {path}:\n\n";
                
                // Directories
                var dirs = Directory.GetDirectories(path);
                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    result += $"📁 {name}/\n";
                }
                
                // Files
                var files = Directory.GetFiles(path);
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var size = new FileInfo(file).Length;
                    var sizeStr = size < 1024 ? $"{size} B" : 
                                  size < 1024 * 1024 ? $"{size / 1024} KB" : 
                                  $"{size / 1024 / 1024} MB";
                    result += $"📄 {name} ({sizeStr})\n";
                }
                
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error listing files: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Read a text file
        /// </summary>
        public static async Task<string> ReadFileAsync(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return $"❌ File not found: {path}";
                    
                var content = await File.ReadAllTextAsync(path);
                if (content.Length > 5000)
                    content = content.Substring(0, 5000) + "\n\n... (truncated)";
                    
                return $"Contents of {Path.GetFileName(path)}:\n\n```\n{content}\n```";
            }
            catch (Exception ex)
            {
                return $"❌ Error reading file: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Write to a text file
        /// </summary>
        public static async Task<string> WriteFileAsync(string path, string content)
        {
            try
            {
                await File.WriteAllTextAsync(path, content);
                return $"✅ Written to {path}";
            }
            catch (Exception ex)
            {
                return $"❌ Error writing file: {ex.Message}";
            }
        }

        /// <summary>
        /// Sort/organize files by type into folders
        /// </summary>
        public static async Task<string> SortFilesByTypeAsync(string path, System.Threading.CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(path))
                    path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!Directory.Exists(path))
                    return $"❌ Directory not found: {path}";

                var categories = new Dictionary<string, string[]>
                {
                    ["Images"] = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff", ".tif", ".raw", ".heic", ".heif", ".avif" },
                    ["Documents"] = new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".epub", ".mobi", ".csv" },
                    ["Videos"] = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpeg", ".mpg", ".3gp", ".ts" },
                    ["Music"] = new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".aiff", ".alac" },
                    ["Archives"] = new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".dmg", ".cab" },
                    ["Code"] = new[] { ".cs", ".js", ".py", ".html", ".css", ".java", ".cpp", ".h", ".json", ".xml", ".ts", ".jsx", ".tsx", ".vue", ".php", ".rb", ".go", ".rs", ".swift", ".kt", ".sql", ".sh", ".ps1", ".yaml", ".yml", ".md", ".ini", ".cfg", ".conf" },
                    ["Executables"] = new[] { ".exe", ".msi", ".bat", ".cmd", ".app", ".apk", ".deb", ".rpm" },
                    ["Torrents"] = new[] { ".torrent" },
                    ["Fonts"] = new[] { ".ttf", ".otf", ".woff", ".woff2", ".eot" },
                    ["3D Models"] = new[] { ".obj", ".fbx", ".stl", ".blend", ".3ds", ".dae", ".gltf", ".glb" },
                    ["Design"] = new[] { ".psd", ".ai", ".sketch", ".fig", ".xd", ".indd", ".cdr", ".eps", ".svg" },
                    ["Ebooks"] = new[] { ".epub", ".mobi", ".azw", ".azw3", ".fb2", ".djvu" },
                    ["Subtitles"] = new[] { ".srt", ".sub", ".ass", ".ssa", ".vtt" },
                    ["Databases"] = new[] { ".db", ".sqlite", ".sqlite3", ".mdb", ".accdb" }
                };

                int moved = 0;
                var movedByCategory = new Dictionary<string, int>();
                var files = Directory.GetFiles(path);
                var errors = new List<string>();
                
                // Track moves for undo
                var moveRecords = new List<ActionRecord>();
                var createdFolders = new List<string>();
                
                foreach (var file in files)
                {
                    try
                    {
                        var ext = Path.GetExtension(file).ToLower();
                        foreach (var cat in categories)
                        {
                            if (cat.Value.Contains(ext))
                            {
                                var destFolder = Path.Combine(path, cat.Key);
                                if (!Directory.Exists(destFolder))
                                {
                                    Directory.CreateDirectory(destFolder);
                                    createdFolders.Add(destFolder);
                                }
                                var destFile = Path.Combine(destFolder, Path.GetFileName(file));
                                
                                // Handle duplicate filenames
                                if (File.Exists(destFile))
                                {
                                    var baseName = Path.GetFileNameWithoutExtension(file);
                                    var extension = Path.GetExtension(file);
                                    var counter = 1;
                                    while (File.Exists(destFile))
                                    {
                                        destFile = Path.Combine(destFolder, $"{baseName}_{counter}{extension}");
                                        counter++;
                                    }
                                }
                                
                                // Record the move for undo
                                moveRecords.Add(ActionRecord.FileMoved(file, destFile));
                                
                                File.Move(file, destFile);
                                moved++;
                                if (!movedByCategory.ContainsKey(cat.Key))
                                    movedByCategory[cat.Key] = 0;
                                movedByCategory[cat.Key]++;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                if (moved == 0)
                    return $"Already tidy!";

                // Record the organization action for undo
                var organizeAction = ActionRecord.FolderOrganized(path, moveRecords, createdFolders);
                ActionHistoryManager.Instance.RecordAction(organizeAction);

                // SHORT, casual response
                var result = $"✅ Sorted {moved} files - all done!";
                
                if (errors.Count > 0 && errors.Count <= 5)
                    result += $" ({errors.Count} files skipped)";
                
                return result;
            }
            catch (Exception ex)
            {
                return $"❌ Error sorting files: {ex.Message}";
            }
        }

        /// <summary>
        /// Create a new folder
        /// </summary>
        public static Task<string> CreateFolderAsync(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    return Task.FromResult($"📁 Folder already exists: {path}");
                Directory.CreateDirectory(path);
                return Task.FromResult($"✅ Created folder: {path}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error creating folder: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete a file or folder - REQUIRES CONFIRMATION via DeleteWithConfirmationAsync
        /// Direct calls to this method are blocked. Use DeleteWithConfirmationAsync instead.
        /// </summary>
        public static Task<string> DeleteAsync(string path)
        {
            // SAFETY: Direct delete calls are blocked - must go through confirmation system
            System.Diagnostics.Debug.WriteLine($"[BLOCKED] Direct delete blocked for: {path} - use DeleteWithConfirmationAsync");
            return Task.FromResult("🚫 Delete requires confirmation. This operation was blocked for safety.");
        }
        
        /// <summary>
        /// Callback for delete confirmation - set this from the UI layer
        /// </summary>
        public static Func<string, string, Task<bool>>? OnDeleteConfirmationRequired { get; set; }
        
        /// <summary>
        /// Delete a file or folder WITH double confirmation
        /// Shows exactly what will be deleted and requires user to confirm twice
        /// </summary>
        public static async Task<string> DeleteWithConfirmationAsync(string path)
        {
            try
            {
                // Validate path exists
                bool isFile = File.Exists(path);
                bool isDir = Directory.Exists(path);
                
                if (!isFile && !isDir)
                    return $"❌ Not found: {path}";
                
                // Build detailed info about what will be deleted
                string itemType = isFile ? "FILE" : "FOLDER";
                string itemName = Path.GetFileName(path);
                string fullPath = Path.GetFullPath(path);
                long size = 0;
                int fileCount = 0;
                int folderCount = 0;
                
                if (isFile)
                {
                    size = new FileInfo(path).Length;
                    fileCount = 1;
                }
                else
                {
                    try
                    {
                        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                        var dirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);
                        fileCount = files.Length;
                        folderCount = dirs.Length;
                        foreach (var f in files)
                        {
                            try { size += new FileInfo(f).Length; } catch { }
                        }
                    }
                    catch { }
                }
                
                string sizeStr = size < 1024 ? $"{size} bytes" :
                                 size < 1024 * 1024 ? $"{size / 1024:N0} KB" :
                                 size < 1024 * 1024 * 1024 ? $"{size / 1024 / 1024:N1} MB" :
                                 $"{size / 1024 / 1024 / 1024:N2} GB";
                
                // Build confirmation message
                var details = $"⚠️ DELETE {itemType}: {itemName}\n\n";
                details += $"📍 Full path: {fullPath}\n";
                details += $"📊 Size: {sizeStr}\n";
                if (isDir)
                {
                    details += $"📁 Contains: {fileCount} files, {folderCount} subfolders\n";
                }
                details += $"\n🗑️ This will be PERMANENTLY deleted!";
                
                // Check if confirmation callback is available
                if (OnDeleteConfirmationRequired == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Delete] No confirmation callback - returning info only");
                    return $"⚠️ DELETE CONFIRMATION REQUIRED\n\n{details}\n\n" +
                           $"To delete this, please confirm in the chat window.";
                }
                
                // First confirmation
                System.Diagnostics.Debug.WriteLine("[Delete] Requesting first confirmation...");
                bool firstConfirm;
                try
                {
                    firstConfirm = await OnDeleteConfirmationRequired(
                        "Confirm Delete (1/2)", 
                        details + "\n\nAre you sure you want to delete this?"
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Delete] First confirmation error: {ex.Message}");
                    return $"❌ Confirmation failed: {ex.Message}";
                }
                
                if (!firstConfirm)
                    return "❌ Delete cancelled by user (first confirmation)";
                
                // Second confirmation with stronger warning
                System.Diagnostics.Debug.WriteLine("[Delete] Requesting second confirmation...");
                bool secondConfirm;
                try
                {
                    secondConfirm = await OnDeleteConfirmationRequired(
                        "⚠️ FINAL CONFIRMATION (2/2)", 
                        $"You are about to PERMANENTLY DELETE:\n\n{itemName}\n\n" +
                        $"This action CANNOT be undone!\n\n" +
                        $"Click 'Yes, Delete' to confirm."
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Delete] Second confirmation error: {ex.Message}");
                    return $"❌ Confirmation failed: {ex.Message}";
                }
                
                if (!secondConfirm)
                    return "❌ Delete cancelled by user (second confirmation)";
                
                // Actually delete
                System.Diagnostics.Debug.WriteLine($"[Delete] User confirmed - deleting {path}");
                if (isFile)
                {
                    File.Delete(path);
                    return $"✅ Deleted file: {itemName}";
                }
                else
                {
                    Directory.Delete(path, true);
                    return $"✅ Deleted folder: {itemName} ({fileCount} files, {folderCount} subfolders)";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Delete] Error: {ex.Message}");
                return $"❌ Error deleting: {ex.Message}";
            }
        }

        /// <summary>
        /// Move a file or folder
        /// </summary>
        public static Task<string> MoveAsync(string source, string dest)
        {
            try
            {
                if (File.Exists(source))
                {
                    File.Move(source, dest);
                    return Task.FromResult($"✅ Moved file to: {dest}");
                }
                if (Directory.Exists(source))
                {
                    Directory.Move(source, dest);
                    return Task.FromResult($"✅ Moved folder to: {dest}");
                }
                return Task.FromResult($"❌ Source not found: {source}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error moving: {ex.Message}");
            }
        }

        /// <summary>
        /// Copy a file or folder
        /// </summary>
        public static Task<string> CopyAsync(string source, string dest)
        {
            try
            {
                if (File.Exists(source))
                {
                    File.Copy(source, dest, true);
                    return Task.FromResult($"✅ Copied file to: {dest}");
                }
                if (Directory.Exists(source))
                {
                    CopyDirectory(source, dest);
                    return Task.FromResult($"✅ Copied folder to: {dest}");
                }
                return Task.FromResult($"❌ Source not found: {source}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error copying: {ex.Message}");
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        /// <summary>
        /// Rename a file or folder
        /// </summary>
        public static Task<string> RenameAsync(string path, string newName)
        {
            try
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                var newPath = Path.Combine(dir, newName);
                
                if (File.Exists(path))
                {
                    File.Move(path, newPath);
                    return Task.FromResult($"✅ Renamed to: {newName}");
                }
                if (Directory.Exists(path))
                {
                    Directory.Move(path, newPath);
                    return Task.FromResult($"✅ Renamed to: {newName}");
                }
                return Task.FromResult($"❌ Not found: {path}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error renaming: {ex.Message}");
            }
        }

        /// <summary>
        /// Find files by name pattern - safely handles protected folders
        /// </summary>
        public static Task<string> FindFilesAsync(string path, string pattern)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!Directory.Exists(path))
                    return Task.FromResult($"❌ Directory not found: {path}");

                var files = new List<string>();
                SearchFilesRecursive(path, $"*{pattern}*", files, maxResults: 100);
                
                if (files.Count == 0)
                    return Task.FromResult($"🔍 No files found matching '{pattern}'");

                var result = $"Found {files.Count} files matching '{pattern}':\n\n";
                var count = 0;
                foreach (var file in files)
                {
                    if (count++ >= 20) { result += $"\n... and {files.Count - 20} more"; break; }
                    result += $"📄 {file}\n";
                }
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error searching: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Recursively search for files, skipping protected/inaccessible folders
        /// </summary>
        private static void SearchFilesRecursive(string path, string pattern, List<string> results, int maxResults)
        {
            if (results.Count >= maxResults) return;
            
            // Skip known protected/problematic folders by name
            var folderName = Path.GetFileName(path);
            var protectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Application Data", "Local Settings", "Cookies", "NetHood", "PrintHood",
                "Recent", "SendTo", "Templates", "Start Menu", "My Documents",
                "$Recycle.Bin", "System Volume Information", "Recovery", "ProgramData",
                "node_modules", ".git", "obj", "bin", "packages", ".vs", ".vscode",
                "Windows", "Program Files", "Program Files (x86)"
            };
            
            if (protectedFolders.Contains(folderName)) return;
            
            // Skip junction points and reparse points FIRST before any enumeration
            try
            {
                var dirInfo = new DirectoryInfo(path);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0) return;
                if ((dirInfo.Attributes & FileAttributes.System) != 0) return;
            }
            catch { return; }
            
            // Search files in current directory
            try
            {
                foreach (var file in Directory.GetFiles(path, pattern))
                {
                    results.Add(file);
                    if (results.Count >= maxResults) return;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
            
            // Recurse into subdirectories
            try
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    if (results.Count >= maxResults) return;
                    
                    // Check if subdirectory is a junction/reparse point before recursing
                    try
                    {
                        var subDirInfo = new DirectoryInfo(dir);
                        if ((subDirInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((subDirInfo.Attributes & FileAttributes.System) != 0) continue;
                    }
                    catch { continue; }
                    
                    SearchFilesRecursive(dir, pattern, results, maxResults);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
        }

        // ==================== ADVANCED PC CONTROL ====================

        /// <summary>
        /// Set system volume (0-100)
        /// </summary>
        public static async Task<string> SetVolumeAsync(int level)
        {
            try
            {
                level = Math.Clamp(level, 0, 100);
                // Use nircmd for volume control (common utility) or PowerShell
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"$obj = New-Object -ComObject WScript.Shell; " +
                               $"1..50 | ForEach-Object {{ $obj.SendKeys([char]174) }}; " + // Mute first
                               $"1..{level / 2} | ForEach-Object {{ $obj.SendKeys([char]175) }}\"", // Then set level
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var process = Process.Start(psi);
                if (process != null) await process.WaitForExitAsync();
                return $"🔊 Volume set to approximately {level}%";
            }
            catch (Exception ex)
            {
                return $"❌ Error setting volume: {ex.Message}";
            }
        }

        /// <summary>
        /// Mute/unmute system volume
        /// </summary>
        public static Task<string> ToggleMuteAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-Command \"$obj = New-Object -ComObject WScript.Shell; $obj.SendKeys([char]173)\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return Task.FromResult("🔇 Toggled mute");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error toggling mute: {ex.Message}");
            }
        }

        /// <summary>
        /// Lock the computer
        /// </summary>
        public static Task<string> LockComputerAsync()
        {
            try
            {
                Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
                return Task.FromResult("🔒 Computer locked");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error locking: {ex.Message}");
            }
        }

        /// <summary>
        /// Shutdown the computer
        /// </summary>
        public static Task<string> ShutdownAsync(int delaySeconds = 60)
        {
            try
            {
                Process.Start("shutdown", $"/s /t {delaySeconds}");
                return Task.FromResult($"⚠️ Computer will shutdown in {delaySeconds} seconds. Run 'shutdown /a' to cancel.");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Restart the computer
        /// </summary>
        public static Task<string> RestartAsync(int delaySeconds = 60)
        {
            try
            {
                Process.Start("shutdown", $"/r /t {delaySeconds}");
                return Task.FromResult($"⚠️ Computer will restart in {delaySeconds} seconds. Run 'shutdown /a' to cancel.");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel scheduled shutdown/restart
        /// </summary>
        public static Task<string> CancelShutdownAsync()
        {
            try
            {
                Process.Start("shutdown", "/a");
                return Task.FromResult("✅ Shutdown/restart cancelled");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Put computer to sleep
        /// </summary>
        public static Task<string> SleepAsync()
        {
            try
            {
                Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                return Task.FromResult("😴 Computer going to sleep...");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Empty recycle bin
        /// </summary>
        public static Task<string> EmptyRecycleBinAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return Task.FromResult("🗑️ Recycle bin emptied");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Take a screenshot
        /// </summary>
        public static Task<string> TakeScreenshotAsync()
        {
            try
            {
                var screenshotPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                
                // Use snippingtool or PowerShell
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Add-Type -AssemblyName System.Windows.Forms; " +
                               $"[System.Windows.Forms.Screen]::PrimaryScreen | ForEach-Object {{ " +
                               $"$bitmap = New-Object System.Drawing.Bitmap($_.Bounds.Width, $_.Bounds.Height); " +
                               $"$graphics = [System.Drawing.Graphics]::FromImage($bitmap); " +
                               $"$graphics.CopyFromScreen($_.Bounds.Location, [System.Drawing.Point]::Empty, $_.Bounds.Size); " +
                               $"$bitmap.Save('{screenshotPath}'); }}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return Task.FromResult($"📸 Screenshot saved to: {screenshotPath}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get battery status
        /// </summary>
        public static Task<string> GetBatteryStatusAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-Command \"(Get-WmiObject Win32_Battery | Select-Object EstimatedChargeRemaining, BatteryStatus) | Format-List\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var process = Process.Start(psi);
                var output = process?.StandardOutput.ReadToEnd() ?? "";
                process?.WaitForExit();
                
                if (string.IsNullOrWhiteSpace(output))
                    return Task.FromResult("🔌 No battery detected (desktop PC)");
                    
                return Task.FromResult($"🔋 Battery Status:\n{output}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get running processes
        /// </summary>
        public static Task<string> GetProcessesAsync()
        {
            try
            {
                var processes = Process.GetProcesses()
                    .OrderByDescending(p => p.WorkingSet64)
                    .Take(15)
                    .Select(p => $"• {p.ProcessName} - {p.WorkingSet64 / 1024 / 1024} MB");
                
                return Task.FromResult($"Top 15 Processes by Memory:\n\n{string.Join("\n", processes)}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Kill a process by name
        /// </summary>
        public static Task<string> KillProcessAsync(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName.Replace(".exe", ""));
                if (processes.Length == 0)
                    return Task.FromResult($"❌ No process found: {processName}");
                
                foreach (var p in processes)
                {
                    p.Kill();
                }
                return Task.FromResult($"✅ Killed {processes.Length} instance(s) of {processName}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get disk space info
        /// </summary>
        public static Task<string> GetDiskSpaceAsync()
        {
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => $"💾 {d.Name} - {d.AvailableFreeSpace / 1024 / 1024 / 1024} GB free of {d.TotalSize / 1024 / 1024 / 1024} GB ({d.DriveFormat})");
                
                return Task.FromResult($"Disk Space:\n\n{string.Join("\n", drives)}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get network info
        /// </summary>
        public static async Task<string> GetNetworkInfoAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-Command \"Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notlike '*Loopback*' } | Select-Object InterfaceAlias, IPAddress | Format-Table -AutoSize\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var process = Process.Start(psi);
                var output = await (process?.StandardOutput.ReadToEndAsync() ?? Task.FromResult(""));
                process?.WaitForExit();
                
                return $"Network Info:\n\n```\n{output}\n```";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Set a reminder/alarm
        /// </summary>
        public static Task<string> SetReminderAsync(string message, int minutes)
        {
            try
            {
                var time = DateTime.Now.AddMinutes(minutes);
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"$action = New-ScheduledTaskAction -Execute 'msg' -Argument '* {message}'; " +
                               $"$trigger = New-ScheduledTaskTrigger -Once -At '{time:HH:mm}'; " +
                               $"Register-ScheduledTask -TaskName 'AtlasReminder_{DateTime.Now.Ticks}' -Action $action -Trigger $trigger -Force\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return Task.FromResult($"⏰ Reminder set for {time:h:mm tt}: {message}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Open Windows settings page - uses comprehensive Windows knowledge base
        /// </summary>
        public static Task<string> OpenSettingsAsync(string page = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(page))
                {
                    Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });
                    return Task.FromResult("⚙️ Opened Settings");
                }
                
                // Use the comprehensive Windows knowledge base
                var uri = WindowsKnowledgeBase.FindSettingsPage(page);
                
                if (uri != null)
                {
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    return Task.FromResult($"⚙️ Opened {page} settings");
                }
                
                // Try control panel items
                var cpCmd = WindowsKnowledgeBase.FindControlPanelItem(page);
                if (cpCmd != null)
                {
                    Process.Start(new ProcessStartInfo(cpCmd) { UseShellExecute = true });
                    return Task.FromResult($"⚙️ Opened {page}");
                }
                
                // Try system commands
                var sysCmd = WindowsKnowledgeBase.FindSystemCommand(page);
                if (sysCmd != null)
                {
                    Process.Start(new ProcessStartInfo(sysCmd) { UseShellExecute = true });
                    return Task.FromResult($"⚙️ Opened {page}");
                }
                
                // Fallback - open main settings
                Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });
                return Task.FromResult($"⚙️ Opened Settings (couldn't find specific page for '{page}')");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Type text (simulate keyboard)
        /// </summary>
        public static Task<string> TypeTextAsync(string text)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{text.Replace("'", "''")}')\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return Task.FromResult($"⌨️ Typed: {text}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Press a key combination
        /// </summary>
        public static Task<string> PressKeyAsync(string keys)
        {
            try
            {
                // Convert common key names to SendKeys format
                var sendKeys = keys.ToLower() switch
                {
                    "enter" => "{ENTER}",
                    "escape" or "esc" => "{ESC}",
                    "tab" => "{TAB}",
                    "space" => " ",
                    "backspace" => "{BACKSPACE}",
                    "delete" or "del" => "{DELETE}",
                    "home" => "{HOME}",
                    "end" => "{END}",
                    "pageup" => "{PGUP}",
                    "pagedown" => "{PGDN}",
                    "up" => "{UP}",
                    "down" => "{DOWN}",
                    "left" => "{LEFT}",
                    "right" => "{RIGHT}",
                    "ctrl+c" or "copy" => "^c",
                    "ctrl+v" or "paste" => "^v",
                    "ctrl+x" or "cut" => "^x",
                    "ctrl+z" or "undo" => "^z",
                    "ctrl+a" or "select all" => "^a",
                    "ctrl+s" or "save" => "^s",
                    "alt+tab" => "%{TAB}",
                    "alt+f4" or "close" => "%{F4}",
                    "win+d" or "desktop" => "^{ESC}d",
                    "win+e" or "explorer" => "^{ESC}e",
                    "win+l" or "lock" => "^{ESC}l",
                    "printscreen" or "screenshot" => "{PRTSC}",
                    _ => keys
                };
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{sendKeys}')\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return Task.FromResult($"⌨️ Pressed: {keys}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Consolidate files from subfolders into the parent folder and delete empty folders
        /// </summary>
        public static Task<string> ConsolidateFilesAsync(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult("❌ Please specify a folder path");
                    
                // Expand Desktop path if needed
                if (path.ToLower().Contains("desktop"))
                {
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (path.ToLower() == "desktop")
                        path = desktopPath;
                    else
                    {
                        // Extract folder name after "desktop"
                        var parts = path.Split(new[] { "desktop", "Desktop" }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            var folderName = parts[parts.Length - 1].Trim('\\', '/', ' ');
                            path = Path.Combine(desktopPath, folderName);
                        }
                    }
                }
                
                if (!Directory.Exists(path))
                    return Task.FromResult($"❌ Folder not found: {path}");

                int filesMoved = 0;
                int foldersDeleted = 0;
                var errors = new List<string>();

                // Get all files from all subdirectories
                var allFiles = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                
                foreach (var file in allFiles)
                {
                    var fileDir = Path.GetDirectoryName(file);
                    // Skip if file is already in the root folder
                    if (fileDir == path) continue;
                    
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        var destPath = Path.Combine(path, fileName);
                        
                        // Handle duplicate filenames
                        if (File.Exists(destPath))
                        {
                            var baseName = Path.GetFileNameWithoutExtension(file);
                            var extension = Path.GetExtension(file);
                            var counter = 1;
                            while (File.Exists(destPath))
                            {
                                destPath = Path.Combine(path, $"{baseName}_{counter}{extension}");
                                counter++;
                            }
                        }
                        
                        File.Move(file, destPath);
                        filesMoved++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                // Delete empty subdirectories (from deepest to shallowest)
                var subdirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length); // Longer paths first (deeper folders)
                    
                foreach (var dir in subdirs)
                {
                    try
                    {
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir);
                            foldersDeleted++;
                        }
                    }
                    catch { }
                }

                var result = $"✅ Consolidated {filesMoved} files!";
                if (foldersDeleted > 0)
                    result += $" Cleaned {foldersDeleted} empty folders.";
                    
                if (errors.Count > 0)
                    result += $" ({errors.Count} skipped)";

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Error consolidating files: {ex.Message}");
            }
        }

        /// <summary>
        /// Open a folder or special Windows location
        /// Handles both regular paths and special shell paths
        /// </summary>
        public static Task<string> OpenFolderAsync(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult("❌ No path specified");
                
                // Handle special shell paths
                if (path.StartsWith("shell:"))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                    var friendlyName = GetShellPathFriendlyName(path);
                    return Task.FromResult($"📂 Opened {friendlyName}");
                }
                
                // Handle registry
                if (path == "regedit")
                {
                    Process.Start("regedit.exe");
                    return Task.FromResult("📝 Opened Registry Editor");
                }
                
                // Handle regular paths
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                    return Task.FromResult($"📂 Opened {Path.GetFileName(path) ?? path}");
                }
                
                return Task.FromResult($"❌ Folder not found: {path}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to open folder: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get friendly name for shell paths
        /// </summary>
        private static string GetShellPathFriendlyName(string shellPath)
        {
            return shellPath switch
            {
                "shell:RecycleBinFolder" => "Recycle Bin",
                "shell:ControlPanelFolder" => "Control Panel",
                "shell:NetworkPlacesFolder" => "Network",
                "shell:MyComputerFolder" => "This PC",
                "shell:Desktop" => "Desktop",
                "shell:Documents" => "Documents",
                "shell:Downloads" => "Downloads",
                "shell:Pictures" => "Pictures",
                "shell:Music" => "Music",
                "shell:Videos" => "Videos",
                "shell:Startup" => "Startup Folder",
                "shell:SendTo" => "Send To Folder",
                "shell:Recent" => "Recent Files",
                "shell:Fonts" => "Fonts",
                "shell:StartMenu" => "Start Menu",
                "shell:Programs" => "Programs",
                "shell:Administrative Tools" => "Administrative Tools",
                _ => shellPath.Replace("shell:", "")
            };
        }

        /// <summary>
        /// Open Windows system utilities and control panels
        /// </summary>
        public static Task<string> OpenSystemUtilityAsync(string utility)
        {
            try
            {
                var command = utility.ToLower() switch
                {
                    "device manager" => "devmgmt.msc",
                    "disk management" => "diskmgmt.msc",
                    "event viewer" => "eventvwr.msc",
                    "services" => "services.msc",
                    "system configuration" or "msconfig" => "msconfig",
                    "system information" => "msinfo32",
                    "registry editor" or "regedit" => "regedit",
                    "task scheduler" => "taskschd.msc",
                    "local group policy" or "gpedit" => "gpedit.msc",
                    "computer management" => "compmgmt.msc",
                    "performance monitor" => "perfmon.msc",
                    "windows memory diagnostic" => "mdsched",
                    "system file checker" => "cmd /c sfc /scannow",
                    "disk cleanup" => "cleanmgr",
                    "defragment" => "dfrgui",
                    "resource monitor" => "resmon",
                    "windows firewall" => "wf.msc",
                    "certificate manager" => "certmgr.msc",
                    "local security policy" => "secpol.msc",
                    "component services" => "dcomcnfg",
                    "odbc data sources" => "odbcad32",
                    "print management" => "printmanagement.msc",
                    "shared folders" => "fsmgmt.msc",
                    "windows features" => "optionalfeatures",
                    "programs and features" => "appwiz.cpl",
                    "user accounts" => "netplwiz",
                    "system properties" => "sysdm.cpl",
                    "power options" => "powercfg.cpl",
                    "network connections" => "ncpa.cpl",
                    "sound" => "mmsys.cpl",
                    "display settings" => "desk.cpl",
                    "mouse properties" => "main.cpl",
                    "keyboard properties" => "main.cpl keyboard",
                    "date and time" => "timedate.cpl",
                    "region settings" => "intl.cpl",
                    "internet options" => "inetcpl.cpl",
                    "add hardware" => "hdwwiz.cpl",
                    "game controllers" => "joy.cpl",
                    "phone and modem" => "telephon.cpl",
                    "speech properties" => "sapi.cpl",
                    _ => ""
                };
                
                if (string.IsNullOrEmpty(command))
                    return Task.FromResult($"❌ Unknown system utility: {utility}");
                
                if (command.StartsWith("cmd /c"))
                {
                    // Run command in elevated mode for system commands
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = command.Substring(6), // Remove "cmd /c"
                        UseShellExecute = true,
                        Verb = "runas" // Run as administrator
                    };
                    Process.Start(psi);
                }
                else
                {
                    Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
                }
                
                return Task.FromResult($"⚙️ Opened {utility}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to open {utility}: {ex.Message}");
            }
        }
    }
}
