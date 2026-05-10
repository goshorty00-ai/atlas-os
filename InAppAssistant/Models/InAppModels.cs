using System;
using System.Collections.Generic;

namespace AtlasAI.InAppAssistant.Models
{
    /// <summary>
    /// Represents the context of the currently active Windows application
    /// </summary>
    public class ActiveAppContext
    {
        public IntPtr WindowHandle { get; set; }
        public string ProcessName { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public int ProcessId { get; set; }
        public DateTime CapturedAt { get; set; } = DateTime.Now;
        
        // Browser-specific (best effort from title parsing)
        public bool IsBrowser { get; set; }
        public string BrowserUrl { get; set; } = "";
        public string BrowserTabTitle { get; set; } = "";
        
        // App category for skill matching
        public AppCategory Category { get; set; } = AppCategory.Unknown;
        
        // Selected text (if captured with permission)
        public string SelectedText { get; set; } = "";
        public bool HasSelectedText => !string.IsNullOrEmpty(SelectedText);
        
        public override string ToString() => 
            $"{ProcessName}: {WindowTitle}" + (HasSelectedText ? $" [Selected: {SelectedText.Length} chars]" : "");
    }
    
    public enum AppCategory
    {
        Unknown,
        Browser,
        FileExplorer,
        TextEditor,
        IDE,
        Office,
        Terminal,
        MediaPlayer,
        Communication,
        System
    }
    
    /// <summary>
    /// Represents an action that can be performed in an app
    /// </summary>
    public class InAppAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ActionType Type { get; set; }
        public AppCategory TargetApp { get; set; }
        public string TargetProcessName { get; set; } = "";
        public bool RequiresConfirmation { get; set; } = true;
        public bool IsDestructive { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        
        // For dry run preview
        public string PreviewDescription { get; set; } = "";
        public List<string> Steps { get; set; } = new();
    }
    
    public enum ActionType
    {
        SendKeys,
        Click,
        TypeText,
        OpenMenu,
        RunCommand,
        FileOperation,
        ClipboardOperation,
        UIAutomation
    }
    
    /// <summary>
    /// Result of executing an in-app action
    /// </summary>
    public class ActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string ActionId { get; set; } = "";
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
        public string TargetApp { get; set; } = "";
        public string TargetWindow { get; set; } = "";
        public bool CanUndo { get; set; }
        public InAppAction? UndoAction { get; set; }
        public Exception? Error { get; set; }
    }
    
    /// <summary>
    /// Per-app permission settings
    /// </summary>
    public class AppPermission
    {
        public string ProcessName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool AllowKeystrokes { get; set; } = false;
        public bool AllowClicks { get; set; } = false;
        public bool AllowTextCapture { get; set; } = false;
        public bool AllowFileOperations { get; set; } = false;
        public bool RequireConfirmation { get; set; } = true;
        public DateTime LastUsed { get; set; }
        public int UsageCount { get; set; }
    }
    
    /// <summary>
    /// Action log entry for audit trail
    /// </summary>
    public class ActionLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string ActionName { get; set; } = "";
        public string TargetApp { get; set; } = "";
        public string TargetWindow { get; set; } = "";
        public ActionType ActionType { get; set; }
        public bool Success { get; set; }
        public string Details { get; set; } = "";
        public bool WasConfirmed { get; set; }
        public string UserCommand { get; set; } = "";
    }
}
