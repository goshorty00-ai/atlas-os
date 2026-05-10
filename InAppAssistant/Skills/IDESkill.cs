using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AtlasAI.InAppAssistant.Models;
using AtlasAI.InAppAssistant.Services;

namespace AtlasAI.InAppAssistant.Skills
{
    /// <summary>
    /// Skills for IDE automation (VS Code, Visual Studio, etc.)
    /// </summary>
    public class IDESkill
    {
        private readonly ActionRunner _runner;
        private readonly WindowsContextService _contextService;

        public IDESkill(ActionRunner runner, WindowsContextService contextService)
        {
            _runner = runner;
            _contextService = contextService;
        }

        /// <summary>
        /// Open file by name (Ctrl+P in VS Code, Ctrl+, in VS)
        /// </summary>
        public async Task<ActionResult> OpenFileAsync(string fileName)
        {
            var context = _contextService.GetActiveAppContext();
            var isVSCode = context.ProcessName.Equals("code", StringComparison.OrdinalIgnoreCase);

            // Quick open shortcut differs between IDEs
            var shortcut = isVSCode ? "^p" : "^,";

            var action = new InAppAction
            {
                Name = "Open File",
                Description = $"Open file: {fileName}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = shortcut }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(200);

            // Type filename
            action = new InAppAction
            {
                Name = "Type Filename",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = fileName }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(300);

            // Select first result
            action = new InAppAction
            {
                Name = "Select",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Search in files (Ctrl+Shift+F)
        /// </summary>
        public async Task<ActionResult> SearchInFilesAsync(string query)
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Search in Files",
                Description = $"Search for: {query}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^+f" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(200);

            // Type search query
            action = new InAppAction
            {
                Name = "Type Query",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = query }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(100);

            // Execute search
            action = new InAppAction
            {
                Name = "Search",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Format document
        /// </summary>
        public async Task<ActionResult> FormatDocumentAsync()
        {
            var context = _contextService.GetActiveAppContext();
            var isVSCode = context.ProcessName.Equals("code", StringComparison.OrdinalIgnoreCase);

            // Format shortcut differs
            var shortcut = isVSCode ? "+%f" : "^k^d"; // Shift+Alt+F for VS Code, Ctrl+K Ctrl+D for VS

            var action = new InAppAction
            {
                Name = "Format Document",
                Description = "Format current document",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = shortcut }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Go to line
        /// </summary>
        public async Task<ActionResult> GoToLineAsync(int lineNumber)
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Go to Line",
                Description = $"Go to line {lineNumber}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^g" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(150);

            // Type line number
            action = new InAppAction
            {
                Name = "Type Line Number",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = lineNumber.ToString() }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(100);

            // Go
            action = new InAppAction
            {
                Name = "Go",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Toggle comment
        /// </summary>
        public async Task<ActionResult> ToggleCommentAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Toggle Comment",
                Description = "Toggle line comment",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^/" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Save current file
        /// </summary>
        public async Task<ActionResult> SaveFileAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Save",
                Description = "Save current file",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^s" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Save all files
        /// </summary>
        public async Task<ActionResult> SaveAllAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Save All",
                Description = "Save all open files",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^+s" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Open command palette (VS Code)
        /// </summary>
        public async Task<ActionResult> OpenCommandPaletteAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Command Palette",
                Description = "Open command palette",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^+p" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Run command in command palette
        /// </summary>
        public async Task<ActionResult> RunCommandAsync(string command)
        {
            var result = await OpenCommandPaletteAsync();
            if (!result.Success) return result;

            await Task.Delay(200);

            var context = _contextService.GetActiveAppContext();

            // Type command
            var action = new InAppAction
            {
                Name = "Type Command",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = command }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(200);

            // Execute
            action = new InAppAction
            {
                Name = "Execute",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Toggle sidebar
        /// </summary>
        public async Task<ActionResult> ToggleSidebarAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Toggle Sidebar",
                Description = "Toggle sidebar visibility",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^b" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Open terminal
        /// </summary>
        public async Task<ActionResult> OpenTerminalAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Open Terminal",
                Description = "Open integrated terminal",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^`" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Find and replace
        /// </summary>
        public async Task<ActionResult> FindAndReplaceAsync(string find, string replace)
        {
            var context = _contextService.GetActiveAppContext();

            // Open find/replace
            var action = new InAppAction
            {
                Name = "Find and Replace",
                Description = $"Replace '{find}' with '{replace}'",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.IDE,
                Parameters = new Dictionary<string, object> { ["keys"] = "^h" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(200);

            // Type find text
            action = new InAppAction
            {
                Name = "Type Find",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = find }
            };
            result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            // Tab to replace field
            action = new InAppAction
            {
                Name = "Tab",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{TAB}" }
            };
            await _runner.ExecuteAsync(action, context);

            await Task.Delay(100);

            // Type replace text
            action = new InAppAction
            {
                Name = "Type Replace",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = replace }
            };

            return await _runner.ExecuteAsync(action, context);
        }
    }
}
