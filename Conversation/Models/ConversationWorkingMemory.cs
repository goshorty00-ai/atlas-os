using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AtlasAI.Conversation.Models
{
    /// <summary>
    /// Session-only working memory for conversation context.
    /// Tracks goals, problems, and recent exchanges to enable specific, context-aware responses.
    /// </summary>
    public class ConversationWorkingMemory
    {
        private static ConversationWorkingMemory? _instance;
        private static readonly object _lock = new();

        public static ConversationWorkingMemory Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ConversationWorkingMemory();
                    }
                }
                return _instance;
            }
        }

        // User goals (last 3 high-level goals inferred)
        private readonly List<string> _userGoals = new(3);
        public IReadOnlyList<string> UserGoals => _userGoals;

        // Current active problem being worked on
        public string? ActiveProblem { get; private set; }

        // Last user issue summary (1-2 lines)
        public string? LastUserIssueSummary { get; private set; }

        // Last assistant plan (max 4 bullets)
        private readonly List<string> _lastAssistantPlan = new(4);
        public IReadOnlyList<string> LastAssistantPlan => _lastAssistantPlan;

        // === SCAN SUMMARY (for security scan context) ===
        
        /// <summary>
        /// Last security scan summary for context-aware responses
        /// </summary>
        public ScanSummary? LastScanSummary { get; private set; }

        /// <summary>
        /// Store scan results for conversation context
        /// </summary>
        public void StoreScanSummary(int filesScanned, int threatCount, List<string>? detections, bool isPlaceholder, string? placeholderReason = null)
        {
            LastScanSummary = new ScanSummary
            {
                FilesScanned = filesScanned,
                ThreatCount = threatCount,
                Detections = detections?.Take(10).ToList() ?? new List<string>(),
                IsPlaceholder = isPlaceholder,
                PlaceholderReason = placeholderReason,
                Timestamp = DateTime.Now
            };
            
            Debug.WriteLine($"[WorkingMemory] Scan summary stored: {filesScanned} files, {threatCount} threats, placeholder={isPlaceholder}");
        }

        /// <summary>
        /// Clear scan summary
        /// </summary>
        public void ClearScanSummary()
        {
            LastScanSummary = null;
            Debug.WriteLine("[WorkingMemory] Scan summary cleared");
        }

        // Last 3 user messages
        private readonly Queue<string> _lastUserMessages = new(3);
        public IReadOnlyList<string> LastUserMessages => _lastUserMessages.ToList();

        // Last 3 assistant messages
        private readonly Queue<string> _lastAssistantMessages = new(3);
        public IReadOnlyList<string> LastAssistantMessages => _lastAssistantMessages.ToList();

        // Problem detection patterns - EXTREMELY specific to avoid false positives
        // "can you code" or "can you help with X" should NOT trigger problem detection
        // Only trigger on actual problem statements like "can't get it to work" or "it's broken"
        // MUST contain explicit problem indicators like "not working", "broken", "error", "failed"
        private static readonly string[] ProblemPatterns = new[]
        {
            @"(?:having (?:trouble|issues?|problems?) with .* (?:not working|broken|failing|error))",
            @"(?:(?:not working|doesn't work|won't work|can't get .* to work|broken|failing) .* (?:error|failed|issue))",
            @"(?:how do i fix .* (?:error|issue|problem|broken))",
            @"(?:what's wrong with .* (?:not working|broken|failing))"
        };

        // Success patterns
        private static readonly string[] SuccessPatterns = new[]
        {
            @"(?:that worked|fixed|thanks|thank you|perfect|great|awesome|nice|good job|well done|sorted|cheers)"
        };

        // Goal extraction patterns
        private static readonly string[] GoalPatterns = new[]
        {
            @"i (?:need|want) to (.+?)(?:\.|$)",
            @"(?:help me|can you) (.+?)(?:\.|$)",
            @"i'm trying to (.+?)(?:\.|$)"
        };

        private ConversationWorkingMemory()
        {
            Debug.WriteLine("[WorkingMemory] Initialized");
        }

        /// <summary>
        /// Process a user message and update working memory.
        /// </summary>
        public void ProcessUserMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var lower = message.ToLowerInvariant();

            // Add to recent messages
            EnqueueMessage(_lastUserMessages, message, 3);

            // Check for success (clears active problem)
            if (DetectsSuccess(lower))
            {
                Debug.WriteLine("[WorkingMemory] Success detected, clearing ActiveProblem");
                ActiveProblem = null;
                LastUserIssueSummary = null;
                _lastAssistantPlan.Clear();
                ConversationContext.Instance.RecordSuccessfulHelp();
                return;
            }

            // Check for problem/help request
            if (DetectsProblem(lower))
            {
                ActiveProblem = ExtractProblemSummary(message);
                LastUserIssueSummary = ActiveProblem;
                Debug.WriteLine($"[WorkingMemory] ActiveProblem set: {ActiveProblem}");
            }

            // Extract goals
            var goal = ExtractGoal(message);
            if (!string.IsNullOrEmpty(goal))
            {
                AddGoal(goal);
            }
        }

        /// <summary>
        /// Process an assistant message and update working memory.
        /// </summary>
        public void ProcessAssistantMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            EnqueueMessage(_lastAssistantMessages, message, 3);

            // Extract plan bullets if present
            var bullets = ExtractBullets(message);
            if (bullets.Count > 0)
            {
                _lastAssistantPlan.Clear();
                _lastAssistantPlan.AddRange(bullets.Take(4));
            }
        }

        /// <summary>
        /// Build a context snippet for the system prompt.
        /// </summary>
        public string BuildContextSnippet()
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(ActiveProblem))
            {
                sb.AppendLine($"ActiveProblem: {ActiveProblem}");
            }

            if (_userGoals.Count > 0)
            {
                sb.AppendLine($"UserGoals: {string.Join("; ", _userGoals)}");
            }

            if (_lastAssistantPlan.Count > 0)
            {
                sb.AppendLine($"LastPlan: {string.Join(" | ", _lastAssistantPlan)}");
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Get keywords from recent user messages for specificity checking.
        /// </summary>
        public List<string> GetRecentKeywords()
        {
            var stopWords = new HashSet<string>
            {
                "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
                "have", "has", "had", "do", "does", "did", "will", "would", "could",
                "should", "may", "might", "must", "shall", "can", "need", "i", "you",
                "me", "my", "your", "it", "this", "that", "what", "how", "why", "when",
                "where", "who", "please", "just", "want", "to", "and", "or", "but"
            };

            var keywords = new List<string>();
            foreach (var msg in _lastUserMessages)
            {
                var words = Regex.Split(msg.ToLowerInvariant(), @"\W+")
                    .Where(w => w.Length > 2 && !stopWords.Contains(w));
                keywords.AddRange(words);
            }

            return keywords.Distinct().ToList();
        }

        /// <summary>
        /// Reset working memory.
        /// </summary>
        public void Reset()
        {
            _userGoals.Clear();
            ActiveProblem = null;
            LastUserIssueSummary = null;
            _lastAssistantPlan.Clear();
            _lastUserMessages.Clear();
            _lastAssistantMessages.Clear();
            Debug.WriteLine("[WorkingMemory] Reset");
        }

        private bool DetectsProblem(string lower)
        {
            foreach (var pattern in ProblemPatterns)
            {
                if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        private bool DetectsSuccess(string lower)
        {
            foreach (var pattern in SuccessPatterns)
            {
                if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        private string ExtractProblemSummary(string message)
        {
            // Truncate to first sentence or 100 chars
            var firstSentence = Regex.Match(message, @"^[^.!?]+[.!?]?");
            var summary = firstSentence.Success ? firstSentence.Value : message;
            return summary.Length > 100 ? summary.Substring(0, 100) + "..." : summary;
        }

        private string? ExtractGoal(string message)
        {
            foreach (var pattern in GoalPatterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    var goal = match.Groups[1].Value.Trim();
                    if (goal.Length > 5 && goal.Length < 80)
                        return goal;
                }
            }
            return null;
        }

        private void AddGoal(string goal)
        {
            // Remove duplicates
            _userGoals.RemoveAll(g => g.Equals(goal, StringComparison.OrdinalIgnoreCase));
            _userGoals.Insert(0, goal);
            
            // Keep only last 3
            while (_userGoals.Count > 3)
                _userGoals.RemoveAt(_userGoals.Count - 1);

            Debug.WriteLine($"[WorkingMemory] Goal added: {goal}");
        }

        private List<string> ExtractBullets(string message)
        {
            var bullets = new List<string>();
            var lines = message.Split('\n');
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("-") || trimmed.StartsWith("•") || 
                    Regex.IsMatch(trimmed, @"^\d+[\.\)]"))
                {
                    var bullet = Regex.Replace(trimmed, @"^[-•\d\.\)]+\s*", "").Trim();
                    if (bullet.Length > 3)
                        bullets.Add(bullet);
                }
            }

            return bullets;
        }

        private void EnqueueMessage(Queue<string> queue, string message, int maxSize)
        {
            queue.Enqueue(message);
            while (queue.Count > maxSize)
                queue.Dequeue();
        }
    }

    /// <summary>
    /// Security scan summary for conversation context
    /// </summary>
    public class ScanSummary
    {
        /// <summary>Number of files scanned</summary>
        public int FilesScanned { get; set; }
        
        /// <summary>Number of threats detected</summary>
        public int ThreatCount { get; set; }
        
        /// <summary>Top 10 detection names/descriptions</summary>
        public List<string> Detections { get; set; } = new();
        
        /// <summary>True if this is placeholder/demo data, not real scan results</summary>
        public bool IsPlaceholder { get; set; }
        
        /// <summary>Reason why data is placeholder (e.g., "demo data", "engine not wired")</summary>
        public string? PlaceholderReason { get; set; }
        
        /// <summary>When the scan was completed</summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Get a brief summary for LLM context
        /// </summary>
        public string GetBriefSummary()
        {
            if (IsPlaceholder)
            {
                return $"[PLACEHOLDER SCAN DATA: {PlaceholderReason ?? "detection engine not wired"}. " +
                       $"UI shows {ThreatCount} threats but actual detection list unavailable.]";
            }
            
            if (ThreatCount == 0)
            {
                return $"Scan completed: {FilesScanned} files checked, no threats detected.";
            }
            
            var topThreats = Detections.Count > 0 
                ? string.Join(", ", Detections.Take(3)) 
                : "details unavailable";
            
            return $"Scan completed: {FilesScanned} files, {ThreatCount} threat(s) found. Top items: {topThreats}";
        }
    }
}
