using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Autopilot.Models;
using AtlasAI.Autopilot.Services;

namespace AtlasAI.Autopilot
{
    /// <summary>
    /// Central manager for the Autopilot system - coordinates all autopilot services
    /// </summary>
    public class AutopilotManager
    {
        private static AutopilotManager? _instance;
        public static AutopilotManager Instance => _instance ??= new AutopilotManager();
        
        private readonly string _configPath;
        private readonly string _rulesPath;
        private readonly string _workflowsPath;
        
        // Services
        public RuleParser RuleParser { get; }
        public ActionAuditLog AuditLog { get; }
        public AutopilotEngine Engine { get; }
        public AwayModeManager AwayMode { get; }
        public SystemMonitor SystemMonitor { get; }
        public SecurityAutopilot Security { get; }

        // Configuration
        private AutopilotConfig _config = new();
        private List<AutopilotRule> _rules = new();
        private List<AutopilotWorkflow> _workflows = new();
        
        // Events
        public event EventHandler<AutopilotAction>? ActionPending;
        public event EventHandler<AutopilotAction>? ActionExecuted;
        public event EventHandler<AutopilotSuggestion>? SuggestionReady;
        public event EventHandler<AwaySessionSummary>? AwaySummaryReady;
        public event EventHandler<string>? StatusChanged;
        
        // Delegate for executing actions
        public Func<string, Dictionary<string, object>, Task<string>>? ActionExecutor
        {
            get => Engine.ActionExecutor;
            set => Engine.ActionExecutor = value;
        }
        
        public AutopilotConfig Config => _config;
        public IReadOnlyList<AutopilotRule> Rules => _rules.AsReadOnly();
        public IReadOnlyList<AutopilotWorkflow> Workflows => _workflows.AsReadOnly();
        public bool IsEnabled => _config.IsEnabled;
        public bool IsAway => AwayMode.IsAway;
        
        private bool _isInitialized;
        
        private AutopilotManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var basePath = Path.Combine(appData, "AtlasAI", "Autopilot");
            _configPath = Path.Combine(basePath, "config.json");
            _rulesPath = Path.Combine(basePath, "rules.json");
            _workflowsPath = Path.Combine(basePath, "workflows.json");
            
            // Initialize services
            RuleParser = new RuleParser();
            AuditLog = new ActionAuditLog();
            Engine = new AutopilotEngine(RuleParser, AuditLog);
            AwayMode = new AwayModeManager(Engine, AuditLog);
            SystemMonitor = new SystemMonitor(Engine);
            Security = new SecurityAutopilot(Engine, AuditLog);
            
            WireUpEvents();
        }

        /// <summary>
        /// Initialize the autopilot system
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;
            
            Debug.WriteLine("[Autopilot] Initializing...");
            
            try
            {
                await AuditLog.InitializeAsync();
                await LoadConfigAsync();
                await LoadRulesAsync();
                await LoadWorkflowsAsync();
                
                Engine.SetConfig(_config);
                Engine.SetRules(_rules);
                
                AwayMode.Initialize();
                
                if (_config.IsEnabled)
                {
                    SystemMonitor.Start();
                }
                
                _isInitialized = true;
                Debug.WriteLine("[Autopilot] Initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Autopilot] Initialization error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Enable or disable autopilot
        /// </summary>
        public async Task SetEnabledAsync(bool enabled)
        {
            _config.IsEnabled = enabled;
            _config.LastModified = DateTime.Now;
            Engine.SetConfig(_config);
            
            if (enabled)
            {
                SystemMonitor.Start();
                Engine.ResetSession();
            }
            else
            {
                SystemMonitor.Stop();
                if (AwayMode.IsAway)
                {
                    await AwayMode.EndAwayModeAsync();
                }
            }
            
            await SaveConfigAsync();
            StatusChanged?.Invoke(this, enabled ? "Autopilot enabled" : "Autopilot disabled");
        }
        
        /// <summary>
        /// Set the default autonomy level
        /// </summary>
        public async Task SetAutonomyLevelAsync(AutonomyLevel level)
        {
            _config.DefaultAutonomyLevel = level;
            _config.LastModified = DateTime.Now;
            Engine.SetConfig(_config);
            await SaveConfigAsync();
        }

        /// <summary>
        /// Add a new rule from plain English
        /// </summary>
        public async Task<AutopilotRule> AddRuleAsync(string plainEnglishRule)
        {
            var rule = RuleParser.ParseRule(plainEnglishRule);
            
            var (isValid, error) = RuleParser.ValidateRule(rule);
            if (!isValid)
            {
                throw new ArgumentException(error);
            }
            
            _rules.Add(rule);
            Engine.SetRules(_rules);
            await SaveRulesAsync();
            
            await AuditLog.LogCustomAsync("rule_added", $"Added rule: {rule.Name}", plainEnglishRule);
            
            return rule;
        }
        
        /// <summary>
        /// Remove a rule
        /// </summary>
        public async Task RemoveRuleAsync(string ruleId)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                _rules.Remove(rule);
                Engine.SetRules(_rules);
                await SaveRulesAsync();
                await AuditLog.LogCustomAsync("rule_removed", $"Removed rule: {rule.Name}", "User requested removal");
            }
        }
        
        /// <summary>
        /// Enable or disable a rule
        /// </summary>
        public async Task SetRuleEnabledAsync(string ruleId, bool enabled)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                rule.IsEnabled = enabled;
                await SaveRulesAsync();
            }
        }
        
        /// <summary>
        /// Add a workflow
        /// </summary>
        public async Task<AutopilotWorkflow> AddWorkflowAsync(AutopilotWorkflow workflow)
        {
            _workflows.Add(workflow);
            await SaveWorkflowsAsync();
            return workflow;
        }
        
        /// <summary>
        /// Start away mode
        /// </summary>
        public void StartAwayMode(string? reason = null)
        {
            if (!_config.IsEnabled) return;
            AwayMode.StartAwayMode(reason);
        }
        
        /// <summary>
        /// End away mode and get summary
        /// </summary>
        public async Task<AwaySessionSummary?> EndAwayModeAsync()
        {
            return await AwayMode.EndAwayModeAsync();
        }
        
        /// <summary>
        /// Record user activity (for idle detection)
        /// </summary>
        public void RecordActivity()
        {
            AwayMode.RecordActivity();
        }

        /// <summary>
        /// Approve a pending action
        /// </summary>
        public async Task<bool> ApproveActionAsync(string actionId, string? note = null)
        {
            return await Engine.ApproveActionAsync(actionId, note);
        }
        
        /// <summary>
        /// Reject a pending action
        /// </summary>
        public async Task RejectActionAsync(string actionId, string? reason = null)
        {
            await Engine.RejectActionAsync(actionId, reason);
        }
        
        /// <summary>
        /// Get pending actions
        /// </summary>
        public IReadOnlyList<AutopilotAction> GetPendingActions()
        {
            return Engine.PendingActions;
        }
        
        /// <summary>
        /// Get recent audit entries
        /// </summary>
        public List<AuditLogEntry> GetRecentAuditEntries(int count = 50)
        {
            return AuditLog.GetRecentEntries(count);
        }
        
        /// <summary>
        /// Get system observations requiring attention
        /// </summary>
        public List<SystemObservation> GetAttentionRequired()
        {
            return SystemMonitor.GetAttentionRequired();
        }
        
        /// <summary>
        /// Block an action type
        /// </summary>
        public async Task BlockActionAsync(string actionType)
        {
            if (!_config.BlockedActions.Contains(actionType, StringComparer.OrdinalIgnoreCase))
            {
                _config.BlockedActions.Add(actionType);
                await SaveConfigAsync();
            }
        }
        
        /// <summary>
        /// Unblock an action type
        /// </summary>
        public async Task UnblockActionAsync(string actionType)
        {
            _config.BlockedActions.RemoveAll(a => a.Equals(actionType, StringComparison.OrdinalIgnoreCase));
            await SaveConfigAsync();
        }
        
        /// <summary>
        /// Shutdown the autopilot system
        /// </summary>
        public void Shutdown()
        {
            SystemMonitor.Stop();
            AwayMode.Shutdown();
            Debug.WriteLine("[Autopilot] Shutdown complete");
        }

        private void WireUpEvents()
        {
            Engine.ActionPending += (s, action) => ActionPending?.Invoke(this, action);
            Engine.ActionExecuted += (s, action) =>
            {
                ActionExecuted?.Invoke(this, action);
                if (AwayMode.IsAway)
                {
                    AwayMode.RecordAction(action);
                }
            };
            Engine.SuggestionGenerated += (s, suggestion) =>
            {
                SuggestionReady?.Invoke(this, suggestion);
                if (AwayMode.IsAway)
                {
                    AwayMode.RecordSuggestion(suggestion);
                }
            };
            Engine.StatusChanged += (s, status) => StatusChanged?.Invoke(this, status);
            
            AwayMode.SummaryReady += (s, summary) => AwaySummaryReady?.Invoke(this, summary);
            
            SystemMonitor.ObservationMade += (s, obs) =>
            {
                if (AwayMode.IsAway)
                {
                    AwayMode.RecordObservation(obs);
                }
            };
            
            Security.SecurityAlert += (s, suggestion) => SuggestionReady?.Invoke(this, suggestion);
        }
        
        private async Task LoadConfigAsync()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = await File.ReadAllTextAsync(_configPath);
                    _config = JsonSerializer.Deserialize<AutopilotConfig>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Autopilot] Error loading config: {ex.Message}");
            }
        }
        
        private async Task SaveConfigAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_configPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Autopilot] Error saving config: {ex.Message}");
            }
        }

        private async Task LoadRulesAsync()
        {
            try
            {
                if (File.Exists(_rulesPath))
                {
                    var json = await File.ReadAllTextAsync(_rulesPath);
                    _rules = JsonSerializer.Deserialize<List<AutopilotRule>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Autopilot] Error loading rules: {ex.Message}");
            }
        }
        
        private async Task SaveRulesAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_rulesPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_rules, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_rulesPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Autopilot] Error saving rules: {ex.Message}");
            }
        }
        
        private async Task LoadWorkflowsAsync()
        {
            try
            {
                if (File.Exists(_workflowsPath))
                {
                    var json = await File.ReadAllTextAsync(_workflowsPath);
                    _workflows = JsonSerializer.Deserialize<List<AutopilotWorkflow>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Autopilot] Error loading workflows: {ex.Message}");
            }
        }
        
        private async Task SaveWorkflowsAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_workflowsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_workflows, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_workflowsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Autopilot] Error saving workflows: {ex.Message}");
            }
        }
    }
}
