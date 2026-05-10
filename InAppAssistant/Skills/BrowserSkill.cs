using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AtlasAI.InAppAssistant.Models;
using AtlasAI.InAppAssistant.Services;

namespace AtlasAI.InAppAssistant.Skills
{
    /// <summary>
    /// Skills for browser automation (Chrome, Edge, Firefox, etc.)
    /// </summary>
    public class BrowserSkill
    {
        private readonly ActionRunner _runner;
        private readonly WindowsContextService _contextService;

        public BrowserSkill(ActionRunner runner, WindowsContextService contextService)
        {
            _runner = runner;
            _contextService = contextService;
        }

        /// <summary>
        /// Open a new tab
        /// </summary>
        public async Task<ActionResult> NewTabAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "New Tab",
                Description = "Open a new browser tab",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "^t" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Close current tab
        /// </summary>
        public async Task<ActionResult> CloseTabAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Close Tab",
                Description = "Close current browser tab",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "^w" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Navigate to a URL
        /// </summary>
        public async Task<ActionResult> NavigateToAsync(string url)
        {
            var context = _contextService.GetActiveAppContext();

            // Ensure URL has protocol
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;

            // Focus address bar
            var action = new InAppAction
            {
                Name = "Navigate",
                Description = $"Navigate to: {url}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "^l" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(150);

            // Type URL
            action = new InAppAction
            {
                Name = "Type URL",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = url }
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
        /// Search in browser
        /// </summary>
        public async Task<ActionResult> SearchAsync(string query)
        {
            var context = _contextService.GetActiveAppContext();

            // Focus address bar (also works as search)
            var action = new InAppAction
            {
                Name = "Search",
                Description = $"Search for: {query}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "^l" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(150);

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

            // Search
            action = new InAppAction
            {
                Name = "Search",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "{ENTER}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Copy current URL
        /// </summary>
        public async Task<ActionResult> CopyUrlAsync()
        {
            var context = _contextService.GetActiveAppContext();

            // Focus address bar
            var action = new InAppAction
            {
                Name = "Copy URL",
                Description = "Copy current page URL",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "^l" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(100);

            // Copy
            action = new InAppAction
            {
                Name = "Copy",
                Type = ActionType.SendKeys,
                Parameters = new Dictionary<string, object> { ["keys"] = "^c" }
            };

            result = await _runner.ExecuteAsync(action, context);
            result.Message = "URL copied to clipboard";
            return result;
        }

        /// <summary>
        /// Refresh current page
        /// </summary>
        public async Task<ActionResult> RefreshAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Refresh",
                Description = "Refresh current page",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "{F5}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Go back
        /// </summary>
        public async Task<ActionResult> GoBackAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Go Back",
                Description = "Navigate back",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "%{LEFT}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Go forward
        /// </summary>
        public async Task<ActionResult> GoForwardAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Go Forward",
                Description = "Navigate forward",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "%{RIGHT}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Find on page
        /// </summary>
        public async Task<ActionResult> FindOnPageAsync(string text)
        {
            var context = _contextService.GetActiveAppContext();

            // Open find dialog
            var action = new InAppAction
            {
                Name = "Find",
                Description = $"Find on page: {text}",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "^f" }
            };

            var result = await _runner.ExecuteAsync(action, context);
            if (!result.Success) return result;

            await Task.Delay(200);

            // Type search text
            action = new InAppAction
            {
                Name = "Type Search",
                Type = ActionType.TypeText,
                Parameters = new Dictionary<string, object> { ["text"] = text }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Switch to next tab
        /// </summary>
        public async Task<ActionResult> NextTabAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Next Tab",
                Description = "Switch to next tab",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "^{TAB}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Switch to previous tab
        /// </summary>
        public async Task<ActionResult> PreviousTabAsync()
        {
            var context = _contextService.GetActiveAppContext();

            var action = new InAppAction
            {
                Name = "Previous Tab",
                Description = "Switch to previous tab",
                Type = ActionType.SendKeys,
                TargetApp = AppCategory.Browser,
                Parameters = new Dictionary<string, object> { ["keys"] = "^+{TAB}" }
            };

            return await _runner.ExecuteAsync(action, context);
        }

        /// <summary>
        /// Open browser in a new window with URL
        /// </summary>
        public async Task<ActionResult> OpenInNewWindowAsync(string url)
        {
            var result = new ActionResult();

            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                result.Success = true;
                result.Message = $"Opened: {url}";
            }
            catch (Exception ex)
            {
                result.Message = $"Failed to open URL: {ex.Message}";
            }

            return await Task.FromResult(result);
        }
    }
}
