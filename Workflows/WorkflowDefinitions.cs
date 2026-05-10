using System.Collections.Generic;

namespace AtlasAI.Workflows
{
    /// <summary>
    /// Built-in workflow chain definitions.
    /// All workflows use ONLY read-only macros and low-risk actions.
    /// No destructive operations, no registry writes, no deletions.
    /// </summary>
    public static class WorkflowDefinitions
    {
        /// <summary>
        /// Get all built-in workflow definitions
        /// </summary>
        public static List<WorkflowChainDefinition> GetAll()
        {
            return new List<WorkflowChainDefinition>
            {
                SystemHealthReview(),
                PcFeelsSlow(),
                InternetIssues(),
                LowDiskSpace(),
                SecurityCheck(),
                BeforeGamingSession()
            };
        }

        /// <summary>
        /// 1. System Health Review - General health check
        /// </summary>
        public static WorkflowChainDefinition SystemHealthReview()
        {
            return new WorkflowChainDefinition
            {
                Id = "workflow-health-review",
                Title = "System Health Review",
                Description = "Quick 3-step diagnostic to check overall system health",
                Icon = "🏥",
                Category = "Diagnostics",
                TriggerKeywords = new[] { "health", "health check", "system health", "checkup", "diagnostic", "how is my pc", "is my pc ok" },
                EstimatedTotalDuration = "~15s",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        StepNumber = 1,
                        Type = WorkflowStepType.Macro,
                        TargetId = "system-overview",
                        Description = "Check CPU, RAM, and system status",
                        Icon = "🖥️",
                        EstimatedDuration = "~5s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 2,
                        Type = WorkflowStepType.Macro,
                        TargetId = "performance-diagnostics",
                        Description = "Analyze performance metrics",
                        Icon = "📈",
                        EstimatedDuration = "~8s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 3,
                        Type = WorkflowStepType.Insight,
                        Description = "Health assessment",
                        Icon = "💡",
                        EstimatedDuration = "instant",
                        InsightTemplate = "{health_summary}"
                    }
                }
            };
        }

        /// <summary>
        /// 2. PC Feels Slow - Diagnose slowness
        /// </summary>
        public static WorkflowChainDefinition PcFeelsSlow()
        {
            return new WorkflowChainDefinition
            {
                Id = "workflow-slow-pc",
                Title = "PC Feels Slow",
                Description = "Diagnose why your PC might be running slowly",
                Icon = "🐢",
                Category = "Troubleshooting",
                TriggerKeywords = new[] { "slow", "sluggish", "laggy", "pc slow", "computer slow", "running slow", "feels slow", "taking forever" },
                EstimatedTotalDuration = "~25s",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        StepNumber = 1,
                        Type = WorkflowStepType.Macro,
                        TargetId = "system-overview",
                        Description = "Check current resource usage",
                        Icon = "🖥️",
                        EstimatedDuration = "~5s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 2,
                        Type = WorkflowStepType.Macro,
                        TargetId = "startup-inventory",
                        Description = "Review startup programs",
                        Icon = "🚀",
                        EstimatedDuration = "~8s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 3,
                        Type = WorkflowStepType.Macro,
                        TargetId = "performance-diagnostics",
                        Description = "Deep performance analysis",
                        Icon = "📈",
                        EstimatedDuration = "~10s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 4,
                        Type = WorkflowStepType.Insight,
                        Description = "Slowness diagnosis",
                        Icon = "💡",
                        EstimatedDuration = "instant",
                        InsightTemplate = "{slowness_diagnosis}"
                    }
                }
            };
        }

        /// <summary>
        /// 3. Internet Issues - Network troubleshooting
        /// </summary>
        public static WorkflowChainDefinition InternetIssues()
        {
            return new WorkflowChainDefinition
            {
                Id = "workflow-internet-issues",
                Title = "Internet Issues",
                Description = "Diagnose network and connectivity problems",
                Icon = "🌐",
                Category = "Troubleshooting",
                TriggerKeywords = new[] { "internet", "network", "wifi", "connection", "no internet", "slow internet", "can't connect", "offline", "disconnected" },
                EstimatedTotalDuration = "~12s",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        StepNumber = 1,
                        Type = WorkflowStepType.Macro,
                        TargetId = "network-snapshot",
                        Description = "Check network status and connectivity",
                        Icon = "📡",
                        EstimatedDuration = "~10s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 2,
                        Type = WorkflowStepType.Insight,
                        Description = "Network diagnosis",
                        Icon = "💡",
                        EstimatedDuration = "instant",
                        InsightTemplate = "{network_diagnosis}"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 3,
                        Type = WorkflowStepType.Action,
                        TargetId = "open-network-settings",
                        Description = "Open Network Settings (if needed)",
                        Icon = "⚙️",
                        EstimatedDuration = "~1s"
                    }
                }
            };
        }

        /// <summary>
        /// 4. Low Disk Space - Storage analysis
        /// </summary>
        public static WorkflowChainDefinition LowDiskSpace()
        {
            return new WorkflowChainDefinition
            {
                Id = "workflow-low-disk",
                Title = "Low Disk Space",
                Description = "Analyze disk usage and identify space consumers",
                Icon = "💾",
                Category = "Troubleshooting",
                TriggerKeywords = new[] { "disk", "storage", "space", "low disk", "full disk", "no space", "disk full", "running out of space", "hard drive" },
                EstimatedTotalDuration = "~20s",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        StepNumber = 1,
                        Type = WorkflowStepType.Macro,
                        TargetId = "disk-health",
                        Description = "Check disk health and usage",
                        Icon = "💿",
                        EstimatedDuration = "~8s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 2,
                        Type = WorkflowStepType.Macro,
                        TargetId = "installed-apps",
                        Description = "Review installed applications (read-only)",
                        Icon = "📦",
                        EstimatedDuration = "~10s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 3,
                        Type = WorkflowStepType.Insight,
                        Description = "Storage recommendations",
                        Icon = "💡",
                        EstimatedDuration = "instant",
                        InsightTemplate = "{disk_recommendations}"
                    }
                }
            };
        }

        /// <summary>
        /// 5. Security Check - Security status review
        /// </summary>
        public static WorkflowChainDefinition SecurityCheck()
        {
            return new WorkflowChainDefinition
            {
                Id = "workflow-security-check",
                Title = "Security Check",
                Description = "Review security status and recent system events",
                Icon = "🛡️",
                Category = "Security",
                TriggerKeywords = new[] { "security", "secure", "virus", "malware", "protection", "firewall", "defender", "am i safe", "security check" },
                EstimatedTotalDuration = "~18s",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        StepNumber = 1,
                        Type = WorkflowStepType.Macro,
                        TargetId = "security-status",
                        Description = "Check Windows Security status",
                        Icon = "🛡️",
                        EstimatedDuration = "~8s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 2,
                        Type = WorkflowStepType.Macro,
                        TargetId = "event-viewer",
                        Description = "Review recent security events",
                        Icon = "📋",
                        EstimatedDuration = "~8s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 3,
                        Type = WorkflowStepType.Insight,
                        Description = "Security assessment",
                        Icon = "💡",
                        EstimatedDuration = "instant",
                        InsightTemplate = "{security_assessment}"
                    }
                }
            };
        }

        /// <summary>
        /// 6. Before Gaming Session - Pre-game optimization check
        /// </summary>
        public static WorkflowChainDefinition BeforeGamingSession()
        {
            return new WorkflowChainDefinition
            {
                Id = "workflow-before-gaming",
                Title = "Before Gaming Session",
                Description = "Quick check to ensure your system is ready for gaming",
                Icon = "🎮",
                Category = "Gaming",
                TriggerKeywords = new[] { "gaming", "game", "before gaming", "play", "ready to game", "gaming session", "optimize for gaming", "game ready" },
                EstimatedTotalDuration = "~15s",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        StepNumber = 1,
                        Type = WorkflowStepType.Macro,
                        TargetId = "system-overview",
                        Description = "Check system resources",
                        Icon = "🖥️",
                        EstimatedDuration = "~5s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 2,
                        Type = WorkflowStepType.Macro,
                        TargetId = "startup-inventory",
                        Description = "Review background processes",
                        Icon = "🚀",
                        EstimatedDuration = "~8s"
                    },
                    new WorkflowStep
                    {
                        StepNumber = 3,
                        Type = WorkflowStepType.Insight,
                        Description = "Gaming readiness",
                        Icon = "💡",
                        EstimatedDuration = "instant",
                        InsightTemplate = "{gaming_readiness}"
                    }
                }
            };
        }
    }
}
