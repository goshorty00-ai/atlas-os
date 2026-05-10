using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using AtlasAI.InAppAssistant.Models;
using AtlasAI.InAppAssistant.Services;

namespace AtlasAI.InAppAssistant.Skills
{
    /// <summary>
    /// Skills for File Explorer automation
    /// </summary>
    public class FileExplorerSkill
    {
        private readonly ActionRunner _runner;
        private readonly WindowsContextService _contextService;

        public FileExplorerSkill(ActionRunner runner, WindowsContextService contextService)
        {
            _runner = runner;
            _contextService = contextService;
        }

        /// <summary>
        /// Create a new folder in the current location
        /// </summary>
        public async Task<ActionResult> CreateNewFolderAsync(string folderName)
        {
            var context = _contextService.GetActiveAppContext();
            
            var action = new InAppAction
            {
                Name = "Create New Folder",
                Description = $"Create folder: {folderName}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.FileExplorer,
                RequiresConfirmation = false,
                Steps = new List<string>
                {
                    "Press Ctrl+Shift+N to create new folder",
                    $"Type folder name: {folderName}",
                    "Press Enter to confirm"
                },
                Parameters = new Dictionary<string, object> { ["keys"] = "^+n" }
            };

            // First create the folder
            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(300); // Wait for dialog

            // Type the name
            action = new InAppAction
            {
                Name = "Type Folder Name",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = folderName }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(100);

            // Press Enter
            action = new InAppAction
            {
                Name = "Confirm",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Rename the selected file/folder
        /// </summary>
        public async Task<ActionResult> RenameSelectedAsync(string newName)
        {
            var context = _contextService.GetActiveAppContext();

            // Press F2 to rename
            var action = new InAppAction
            {
                Name = "Rename",
                Description = $"Rename to: {newName}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.FileExplorer,
                Steps = new List<string>
                {
                    "Press F2 to enter rename mode",
                    $"Type new name: {newName}",
                    "Press Enter to confirm"
                },
                Parameters = new Dictionary<string, object> { ["keys"] = "{F2}" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(200);

            // Select all and type new name
            action = new InAppAction
            {
                Name = "Type New Name",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "^a" }
            };
            await _runner.ExecuteAsync(action, context);

            await Task.Delay(50);

            action = new InAppAction
            {
                Name = "Type Name",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = newName }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(100);

            // Confirm
            action = new InAppAction
            {
                Name = "Confirm Rename",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Delete the selected file/folder
        /// </summary>
        public async Task<ActionResult> DeleteSelectedAsync(bool permanent = false)
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = permanent ? "Permanently Delete" : "Delete",
                Description = permanent ? "Permanently delete selected item" : "Move selected item to Recycle Bin",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.FileExplorer,
                IsDestructive = true,
                RequiresConfirmation = true,
                Parameters = new Dictionary<string, object> 
                { 
                    ["keys"] = permanent ? "+{DELETE}" : "{DELETE}" 
                }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Open search in File Explorer
        /// </summary>
        public async Task<ActionResult> SearchAsync(string query)
        {
            var context = _contextService.GetActiveAppContext();

            // Focus search box
            var action = new InAppAction
            {
                Name = "Search",
                Description = $"Search for: {query}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.FileExplorer,
                Parameters = new Dictionary<string, object> { ["keys"] = "^e" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(200);

            // Type search query
            action = new InAppAction
            {
                Name = "Type Search Query",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = query }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(100);

            // Execute search
            action = new InAppAction
            {
                Name = "Execute Search",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Navigate to a specific path
        /// </summary>
        public async Task<ActionResult> NavigateToAsync(string path)
        {
            var context = _contextService.GetActiveAppContext();

            // Focus address bar
            var action = new InAppAction
            {
                Name = "Navigate",
                Description = $"Navigate to: {path}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.FileExplorer,
                Parameters = new Dictionary<string, object> { ["keys"] = "^l" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(150);

            // Type path
            action = new InAppAction
            {
                Name = "Type Path",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = path }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(100);

            // Navigate
            action = new InAppAction
            {
                Name = "Go",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Zip selected files (uses direct file system)
        /// </summary>
        public async Task<ActionResult> ZipSelectedAsync(string zipName)
        {
            // This uses direct file system operations rather than UI automation
            // Would need to get selected files from Explorer - simplified version
            var result = new ActionResult
            {
                Success = false,
                Message = "Zip operation requires file path. Use 'zip [folder path] to [zip name]' format."
            };

            return await Task.FromResult(result);
        }

        /// <summary>
        /// Zip a folder directly
        /// </summary>
        public async Task<ActionResult> ZipFolderAsync(string folderPath, string zipPath)
        {
            var result = new ActionResult();

            try
            {
                if (!Directory.Exists(folderPath))
                {
                    result.Message = $"Folder not found: {folderPath}";
                    return result;
                }

                await Task.Run(() =>
                {
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);
                    ZipFile.CreateFromDirectory(folderPath, zipPath);
                });

                result.Success = true;
                result.Message = $"Created zip: {zipPath}";
                result.CanUndo = true;
            }
            catch (Exception ex)
            {
                result.Message = $"Zip failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Copy selected items
        /// </summary>
        public async Task<ActionResult> CopySelectedAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Copy",
                Description = "Copy selected items",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.FileExplorer,
                Parameters = new Dictionary<string, object> { ["keys"] = "^c" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Paste items
        /// </summary>
        public async Task<ActionResult> PasteAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Paste",
                Description = "Paste items",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.FileExplorer,
                Parameters = new Dictionary<string, object> { ["keys"] = "^v" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Select all items
        /// </summary>
        public async Task<ActionResult> SelectAllAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Select All",
                Description = "Select all items",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.FileExplorer,
                Parameters = new Dictionary<string, object> { ["keys"] = "^a" }
            };

            return await _runner.ExecuteAsync(action, context);
        }
    }
}
