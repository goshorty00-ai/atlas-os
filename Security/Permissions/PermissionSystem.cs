using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Security.Permissions
{
    #region Enums

    /// <summary>
    /// Defines permission levels for different action types
    /// </summary>
    public enum PermissionLevel
    {
        Allow,    // Action is always allowed without confirmation
        Confirm,  // Action requires user confirmation before execution
        Block,    // Action is blocked and cannot be executed
        DryRun    // Action requires dry-run preview first
    }

    /// <summary>
    /// Risk level for actions
    /// </summary>
    public enum RiskLevel
    {
        Low,      // Read-only, no system changes
        Medium,   // Creates/modifies files, opens apps
        High,     // System settings, registry, network
        Critical  // Uninstall, delete system files, admin actions
    }

    /// <summary>
    /// Categories of actions for permission grouping
    /// </summary>
    public enum ActionCategory
    {
        FileRead, FileWrite, FileDelete,
        AppLaunch, AppClose, AppUninstall,
        SystemSettings, Registry, Network,
        ProcessControl, ScriptExecution,
        WebAccess, Clipboard, Screenshot
    }

    #endregion

    #region Models

    /// <summary>
    /// Represents a permission policy for a specific action type
    /// </summary>
    public class ActionPermission
    {
        public ActionCategory Category { get; set; }
        public string ActionName { get; set; } = "";
        public PermissionLevel DefaultLevel { get; set; }
        public RiskLevel Risk { get; set; }
        public string Description { get; set; } = "";
        public string WhyDangerous { get; set; } = "";
        public bool RequiresAdmin { get; set; }
        public TimeSpan? TimeLimit { get; set; }
    }

    /// <summary>
    /// Result of a risk assessment
    /// </summary>
    public class RiskAssessment
    {
        public string ActionName { get; set; } = "";
        public RiskLevel OverallRisk { get; set; }
        public PermissionLevel RecommendedAction { get; set; }
        public List<string> RiskFactors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string Explanation { get; set; } = "";
        public bool IsSafe => OverallRisk <= RiskLevel.Medium && Warnings.Count == 0;
    }

    /// <summary>
    /// Result of a consent request
    /// </summary>
    public class ConsentResult
    {
        public bool Approved { get; set; }
        public bool RememberChoice { get; set; }
        public string? Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Request for user consent
    /// </summary>
    public class ConsentRequest
    {
        public string ActionName { get; set; } = "";
        public string Description { get; set; } = "";
        public ActionPermission? Policy { get; set; }
        public RiskAssessment Assessment { get; set; } = new();
        public string WhyAsking { get; set; } = "";
    }

    /// <summary>
    /// Result of a safe tool execution
    /// </summary>
    public class SafeExecutionResult
    {
        public bool Executed { get; set; }
        public bool WasBlocked { get; set; }
        public bool WasDenied { get; set; }
        public string? Result { get; set; }
        public string? BlockReason { get; set; }
        public RiskAssessment? Assessment { get; set; }
    }

    #endregion

    #region PermissionPolicy

    /// <summary>
    /// Central permission policy manager
    /// </summary>
    public class PermissionPolicy
    {
        private static PermissionPolicy? _instance;
        public static PermissionPolicy Instance => _instance ??= new PermissionPolicy();

        private readonly Dictionary<string, ActionPermission> _policies = new();
        private readonly Dictionary<string, PermissionLevel> _userOverrides = new();
        private readonly HashSet<string> _trustedActions = new();

        public PermissionPolicy() => InitializeDefaultPolicies();

        private void InitializeDefaultPolicies()
        {
            // LOW RISK - Always Allow
            AddPolicy("read_file", ActionCategory.FileRead, PermissionLevel.Allow, RiskLevel.Low, "Read file contents", "");
            AddPolicy("list_directory", ActionCategory.FileRead, PermissionLevel.Allow, RiskLevel.Low, "List directory contents", "");
            AddPolicy("get_clipboard", ActionCategory.Clipboard, PermissionLevel.Allow, RiskLevel.Low, "Read clipboard contents", "");
            AddPolicy("screenshot", ActionCategory.Screenshot, PermissionLevel.Allow, RiskLevel.Low, "Take a screenshot", "");
            AddPolicy("web_search", ActionCategory.WebAccess, PermissionLevel.Allow, RiskLevel.Low, "Search the web", "");

            // MEDIUM RISK - Confirm
            AddPolicy("write_file", ActionCategory.FileWrite, PermissionLevel.Confirm, RiskLevel.Medium, "Create or modify a file", "Could overwrite important data");
            AddPolicy("create_directory", ActionCategory.FileWrite, PermissionLevel.Allow, RiskLevel.Medium, "Create a new folder", "");
            AddPolicy("open_app", ActionCategory.AppLaunch, PermissionLevel.Allow, RiskLevel.Medium, "Launch an application", "");
            AddPolicy("open_url", ActionCategory.WebAccess, PermissionLevel.Allow, RiskLevel.Medium, "Open a URL in browser", "");

            // HIGH RISK - Always Confirm
            AddPolicy("delete_file", ActionCategory.FileDelete, PermissionLevel.Confirm, RiskLevel.High, "Delete a file", "Permanently removes the file");
            AddPolicy("delete_directory", ActionCategory.FileDelete, PermissionLevel.Confirm, RiskLevel.High, "Delete a folder", "Permanently removes folder and contents");
            AddPolicy("close_app", ActionCategory.AppClose, PermissionLevel.Confirm, RiskLevel.High, "Close an application", "May cause unsaved work to be lost");
            AddPolicy("kill_process", ActionCategory.ProcessControl, PermissionLevel.Confirm, RiskLevel.High, "Force-terminate a process", "May cause data loss");
            AddPolicy("run_command", ActionCategory.ScriptExecution, PermissionLevel.Confirm, RiskLevel.High, "Execute a shell command", "Commands can modify your system");
            AddPolicy("run_powershell", ActionCategory.ScriptExecution, PermissionLevel.Confirm, RiskLevel.High, "Execute PowerShell script", "Scripts can make system-wide changes");
            AddPolicy("network_scan", ActionCategory.Network, PermissionLevel.Confirm, RiskLevel.High, "Scan network for devices", "Accesses your local network");

            // CRITICAL RISK - Block by default
            AddPolicy("uninstall_app", ActionCategory.AppUninstall, PermissionLevel.Block, RiskLevel.Critical, "Uninstall an application", "Permanently removes software", true);
            AddPolicy("registry_write", ActionCategory.Registry, PermissionLevel.Block, RiskLevel.Critical, "Modify Windows Registry", "Can cause system instability", true);
            AddPolicy("registry_delete", ActionCategory.Registry, PermissionLevel.Block, RiskLevel.Critical, "Delete Registry keys", "Can break Windows", true);
            AddPolicy("delete_system_file", ActionCategory.FileDelete, PermissionLevel.Block, RiskLevel.Critical, "Delete system files", "Can make Windows unbootable", true);
            AddPolicy("admin_command", ActionCategory.ScriptExecution, PermissionLevel.Block, RiskLevel.Critical, "Run as Administrator", "Full system access", true);
        }

        private void AddPolicy(string name, ActionCategory cat, PermissionLevel level, RiskLevel risk, string desc, string why, bool admin = false)
        {
            _policies[name.ToLower()] = new ActionPermission
            {
                ActionName = name, Category = cat, DefaultLevel = level,
                Risk = risk, Description = desc, WhyDangerous = why, RequiresAdmin = admin
            };
        }

        public PermissionLevel GetPermissionLevel(string actionName)
        {
            var key = actionName.ToLower();
            if (_userOverrides.TryGetValue(key, out var level)) return level;
            if (_trustedActions.Contains(key)) return PermissionLevel.Allow;
            if (_policies.TryGetValue(key, out var policy)) return policy.DefaultLevel;
            return PermissionLevel.Confirm;
        }

        public ActionPermission? GetPolicy(string actionName) => _policies.GetValueOrDefault(actionName.ToLower());
        public bool IsAllowed(string actionName) => GetPermissionLevel(actionName) == PermissionLevel.Allow;
        public bool IsBlocked(string actionName) => GetPermissionLevel(actionName) == PermissionLevel.Block;
        public bool RequiresConfirmation(string actionName) => GetPermissionLevel(actionName) is PermissionLevel.Confirm or PermissionLevel.DryRun;
        public void TrustAction(string actionName) => _trustedActions.Add(actionName.ToLower());
        public void UntrustAction(string actionName) => _trustedActions.Remove(actionName.ToLower());
        public void SetOverride(string actionName, PermissionLevel level) => _userOverrides[actionName.ToLower()] = level;
        public IReadOnlyDictionary<string, ActionPermission> GetAllPolicies() => _policies;
        public IReadOnlyCollection<string> GetTrustedActions() => _trustedActions;
    }

    #endregion

    #region RiskAssessmentEngine

    /// <summary>
    /// Analyzes actions to determine their risk level
    /// </summary>
    public class RiskAssessmentEngine
    {
        private static RiskAssessmentEngine? _instance;
        public static RiskAssessmentEngine Instance => _instance ??= new RiskAssessmentEngine();

        private readonly HashSet<string> _systemPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData"
        };

        private readonly HashSet<string> _dangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".reg", ".msi"
        };

        private readonly List<(Regex Pattern, string Warning)> _dangerousCommands = new()
        {
            (new Regex(@"rm\s+-rf|rmdir\s+/s", RegexOptions.IgnoreCase), "Recursive delete"),
            (new Regex(@"format\s+[a-z]:", RegexOptions.IgnoreCase), "Format drive"),
            (new Regex(@"del\s+/[fqs]", RegexOptions.IgnoreCase), "Force delete"),
            (new Regex(@"reg\s+(add|delete)", RegexOptions.IgnoreCase), "Registry modification"),
            (new Regex(@"shutdown|restart", RegexOptions.IgnoreCase), "System shutdown/restart"),
            (new Regex(@"taskkill\s+/f", RegexOptions.IgnoreCase), "Force kill process"),
        };

        public RiskAssessment AssessFileOperation(string operation, string path)
        {
            var assessment = new RiskAssessment { ActionName = $"{operation}: {path}", OverallRisk = RiskLevel.Low };

            foreach (var sysPath in _systemPaths)
                if (path.StartsWith(sysPath, StringComparison.OrdinalIgnoreCase))
                {
                    assessment.RiskFactors.Add($"System directory: {sysPath}");
                    assessment.OverallRisk = RiskLevel.Critical;
                    assessment.Warnings.Add("⚠️ Protected system location");
                }

            var ext = Path.GetExtension(path);
            if (_dangerousExtensions.Contains(ext))
            {
                assessment.RiskFactors.Add($"Dangerous file type: {ext}");
                if (assessment.OverallRisk < RiskLevel.High) assessment.OverallRisk = RiskLevel.High;
            }

            assessment.RecommendedAction = operation.ToLower() switch
            {
                "read" => PermissionLevel.Allow,
                "delete" => PermissionLevel.Confirm,
                _ => PermissionLevel.Confirm
            };

            assessment.Explanation = BuildExplanation(assessment);
            return assessment;
        }

        public RiskAssessment AssessCommand(string command)
        {
            var assessment = new RiskAssessment
            {
                ActionName = $"Execute: {command}",
                OverallRisk = RiskLevel.Medium,
                RecommendedAction = PermissionLevel.Confirm
            };

            foreach (var (pattern, warning) in _dangerousCommands)
                if (pattern.IsMatch(command))
                {
                    assessment.RiskFactors.Add(warning);
                    assessment.Warnings.Add($"⚠️ {warning}");
                    assessment.OverallRisk = RiskLevel.High;
                }

            if (command.Contains("runas") || command.Contains("admin"))
            {
                assessment.OverallRisk = RiskLevel.Critical;
                assessment.RecommendedAction = PermissionLevel.Block;
            }

            assessment.Explanation = BuildExplanation(assessment);
            return assessment;
        }

        public RiskAssessment AssessAppOperation(string operation, string appName)
        {
            var assessment = new RiskAssessment { ActionName = $"{operation}: {appName}" };

            (assessment.OverallRisk, assessment.RecommendedAction) = operation.ToLower() switch
            {
                "launch" or "open" => (RiskLevel.Low, PermissionLevel.Allow),
                "close" => (RiskLevel.Medium, PermissionLevel.Confirm),
                "kill" or "terminate" => (RiskLevel.High, PermissionLevel.Confirm),
                "uninstall" => (RiskLevel.Critical, PermissionLevel.Confirm),
                _ => (RiskLevel.Medium, PermissionLevel.Confirm)
            };

            assessment.Explanation = BuildExplanation(assessment);
            return assessment;
        }

        private string BuildExplanation(RiskAssessment a)
        {
            var risk = a.OverallRisk switch
            {
                RiskLevel.Low => "✅ Low-risk action.",
                RiskLevel.Medium => "⚡ This action makes changes.",
                RiskLevel.High => "⚠️ High-risk action. Review carefully.",
                RiskLevel.Critical => "🛑 Critical action that could affect your system.",
                _ => ""
            };
            return a.RiskFactors.Count > 0
                ? $"{risk}\n\nWhy I'm asking:\n• {string.Join("\n• ", a.RiskFactors)}"
                : risk;
        }

        public string GetWhyAsking(string actionName)
        {
            var policy = PermissionPolicy.Instance.GetPolicy(actionName);
            if (policy == null) return "I'm asking because this action could affect your system.";
            return string.IsNullOrEmpty(policy.WhyDangerous) ? policy.Description : policy.WhyDangerous;
        }
    }

    #endregion

    #region UserConsentManager

    /// <summary>
    /// Manages user consent for actions
    /// </summary>
    public class UserConsentManager
    {
        private static UserConsentManager? _instance;
        public static UserConsentManager Instance => _instance ??= new UserConsentManager();

        private readonly Dictionary<string, DateTime> _recentApprovals = new();
        private readonly TimeSpan _approvalCacheDuration = TimeSpan.FromMinutes(5);

        public event Func<ConsentRequest, Task<ConsentResult>>? ConsentRequested;

        public async Task<ConsentResult> RequestConsentAsync(string actionName, string description, RiskAssessment? assessment = null)
        {
            var level = PermissionPolicy.Instance.GetPermissionLevel(actionName);

            if (level == PermissionLevel.Allow)
                return new ConsentResult { Approved = true, Reason = "Allowed by policy" };

            if (level == PermissionLevel.Block)
                return new ConsentResult { Approved = false, Reason = "🛑 Blocked for safety" };

            if (_recentApprovals.TryGetValue(actionName.ToLower(), out var expiry) && DateTime.Now < expiry)
                return new ConsentResult { Approved = true, Reason = "Recently approved" };

            var request = new ConsentRequest
            {
                ActionName = actionName,
                Description = description,
                Assessment = assessment ?? RiskAssessmentEngine.Instance.AssessCommand(description),
                WhyAsking = RiskAssessmentEngine.Instance.GetWhyAsking(actionName)
            };

            var result = ConsentRequested != null
                ? await ConsentRequested.Invoke(request)
                : await ShowFallbackDialogAsync(request);

            if (result.Approved)
            {
                if (result.RememberChoice) PermissionPolicy.Instance.TrustAction(actionName);
                else _recentApprovals[actionName.ToLower()] = DateTime.Now.Add(_approvalCacheDuration);
            }

            return result;
        }

        public bool NeedsConsent(string actionName)
        {
            var level = PermissionPolicy.Instance.GetPermissionLevel(actionName);
            if (level == PermissionLevel.Allow) return false;
            if (_recentApprovals.TryGetValue(actionName.ToLower(), out var expiry) && DateTime.Now < expiry) return false;
            return true;
        }

        private Task<ConsentResult> ShowFallbackDialogAsync(ConsentRequest request)
        {
            var emoji = request.Assessment.OverallRisk switch
            {
                RiskLevel.Low => "✅", RiskLevel.Medium => "⚡",
                RiskLevel.High => "⚠️", RiskLevel.Critical => "🛑", _ => "❓"
            };

            var result = MessageBox.Show(
                $"{emoji} {request.Description}\n\nWhy I'm asking:\n{request.WhyAsking}\n\nProceed?",
                $"Confirm: {request.ActionName}",
                MessageBoxButton.YesNo,
                request.Assessment.OverallRisk >= RiskLevel.High ? MessageBoxImage.Warning : MessageBoxImage.Question
            );

            return Task.FromResult(new ConsentResult { Approved = result == MessageBoxResult.Yes });
        }

        public void ClearApprovalCache() => _recentApprovals.Clear();
    }

    #endregion

    #region SafeToolExecutor

    /// <summary>
    /// Wraps tool execution with permission checks
    /// </summary>
    public class SafeToolExecutor
    {
        private static SafeToolExecutor? _instance;
        public static SafeToolExecutor Instance => _instance ??= new SafeToolExecutor();

        public async Task<SafeExecutionResult> ExecuteFileOperationAsync(string operation, string path, Func<Task<string>> executeFunc)
        {
            var actionName = operation.ToLower() switch
            {
                "read" => "read_file", "write" or "create" => "write_file",
                "delete" => "delete_file", _ => operation
            };

            var assessment = RiskAssessmentEngine.Instance.AssessFileOperation(operation, path);

            if (PermissionPolicy.Instance.IsBlocked(actionName) || assessment.OverallRisk == RiskLevel.Critical)
                return new SafeExecutionResult { WasBlocked = true, BlockReason = $"🛑 Blocked.\n{assessment.Explanation}", Assessment = assessment };

            if (UserConsentManager.Instance.NeedsConsent(actionName))
            {
                var consent = await UserConsentManager.Instance.RequestConsentAsync(actionName, $"{operation} {path}", assessment);
                if (!consent.Approved)
                    return new SafeExecutionResult { WasDenied = true, BlockReason = consent.Reason, Assessment = assessment };
            }

            try
            {
                return new SafeExecutionResult { Executed = true, Result = await executeFunc(), Assessment = assessment };
            }
            catch (Exception ex)
            {
                return new SafeExecutionResult { BlockReason = $"Error: {ex.Message}", Assessment = assessment };
            }
        }

        public async Task<SafeExecutionResult> ExecuteCommandAsync(string command, Func<Task<string>> executeFunc)
        {
            var assessment = RiskAssessmentEngine.Instance.AssessCommand(command);
            var actionName = command.Contains("powershell", StringComparison.OrdinalIgnoreCase) ? "run_powershell" : "run_command";

            if (assessment.OverallRisk == RiskLevel.Critical && !PermissionPolicy.Instance.IsAllowed(actionName))
                return new SafeExecutionResult { WasBlocked = true, BlockReason = $"🛑 Blocked.\n{assessment.Explanation}", Assessment = assessment };

            var consent = await UserConsentManager.Instance.RequestConsentAsync(actionName, $"Execute: {command}", assessment);
            if (!consent.Approved)
                return new SafeExecutionResult { WasDenied = true, BlockReason = consent.Reason, Assessment = assessment };

            try
            {
                return new SafeExecutionResult { Executed = true, Result = await executeFunc(), Assessment = assessment };
            }
            catch (Exception ex)
            {
                return new SafeExecutionResult { BlockReason = $"Error: {ex.Message}", Assessment = assessment };
            }
        }

        public async Task<SafeExecutionResult> ExecuteAppOperationAsync(string operation, string appName, Func<Task<string>> executeFunc)
        {
            var actionName = operation.ToLower() switch
            {
                "launch" or "open" => "open_app", "close" => "close_app",
                "kill" => "kill_process", "uninstall" => "uninstall_app", _ => operation
            };

            var assessment = RiskAssessmentEngine.Instance.AssessAppOperation(operation, appName);

            if (PermissionPolicy.Instance.IsBlocked(actionName))
                return new SafeExecutionResult { WasBlocked = true, BlockReason = $"🛑 Blocked.\n{assessment.Explanation}", Assessment = assessment };

            if (UserConsentManager.Instance.NeedsConsent(actionName))
            {
                var consent = await UserConsentManager.Instance.RequestConsentAsync(actionName, $"{operation} {appName}", assessment);
                if (!consent.Approved)
                    return new SafeExecutionResult { WasDenied = true, BlockReason = consent.Reason, Assessment = assessment };
            }

            try
            {
                return new SafeExecutionResult { Executed = true, Result = await executeFunc(), Assessment = assessment };
            }
            catch (Exception ex)
            {
                return new SafeExecutionResult { BlockReason = $"Error: {ex.Message}", Assessment = assessment };
            }
        }

        public string GetActionExplanation(string actionName)
        {
            var policy = PermissionPolicy.Instance.GetPolicy(actionName);
            if (policy == null) return "Unknown action - requires confirmation.";
            var level = PermissionPolicy.Instance.GetPermissionLevel(actionName);
            return level switch
            {
                PermissionLevel.Allow => $"✅ {policy.Description}",
                PermissionLevel.Confirm => $"⚡ {policy.Description} - Requires confirmation",
                PermissionLevel.Block => $"🛑 {policy.Description} - Blocked",
                _ => policy.Description
            };
        }
    }

    #endregion
}
