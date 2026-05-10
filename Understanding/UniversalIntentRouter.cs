using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Intent categories for routing user requests to appropriate execution modes
    /// </summary>
    public enum IntentCategory
    {
        GeneralChat,        // Answer/explain questions
        CreativeWriting,    // Story/script/marketing copy
        CodingTask,         // Write/modify code, explain code, refactor
        DocumentTask,       // Create/modify Word doc
        FileTask,           // Read/write files, search project
        Troubleshooting     // Errors, crashes, logs
    }

    /// <summary>
    /// Required tools for executing the intent
    /// </summary>
    [Flags]
    public enum RequiredTools
    {
        None = 0,
        FileSystem = 1,
        WordProcessor = 2,
        CodeEditor = 4,
        MultiStep = 8
    }

    /// <summary>
    /// Response style preference
    /// </summary>
    public enum ResponseStyle
    {
        Concise,    // Brief, to the point
        Normal,     // Standard detail level
        Detailed    // Comprehensive explanation
    }

    /// <summary>
    /// Safety gate level for destructive operations
    /// </summary>
    public enum SafetyGate
    {
        None,               // No confirmation needed
        ConfirmDestructive  // Require confirmation for destructive ops
    }

    /// <summary>
    /// Result of intent routing analysis
    /// </summary>
    public class IntentRouting
    {
        public IntentCategory Category { get; set; }
        public double Confidence { get; set; }
        public RequiredTools Tools { get; set; }
        public ResponseStyle Style { get; set; }
        public SafetyGate Safety { get; set; }
        public string Reasoning { get; set; } = "";
    }

    /// <summary>
    /// Universal Intent Router - classifies user requests into execution modes
    /// before LLM processing to ensure correct capability routing and tool selection.
    /// </summary>
    public class UniversalIntentRouter
    {
        /// <summary>
        /// Route a user message to the appropriate intent category with confidence and tool requirements
        /// </summary>
        public static IntentRouting Route(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return new IntentRouting
                {
                    Category = IntentCategory.GeneralChat,
                    Confidence = 1.0,
                    Tools = RequiredTools.None,
                    Style = ResponseStyle.Normal,
                    Safety = SafetyGate.None,
                    Reasoning = "Empty message"
                };
            }

            var msg = userMessage.ToLower().Trim();
            
            // === CREATIVE WRITING ===
            // Stories, scripts, marketing copy, creative content
            if (IsCreativeWriting(msg))
            {
                var routing = new IntentRouting
                {
                    Category = IntentCategory.CreativeWriting,
                    Confidence = 0.9,
                    Tools = RequiredTools.None,
                    Style = ResponseStyle.Detailed,
                    Safety = SafetyGate.None,
                    Reasoning = "Creative writing request detected"
                };
                
                Debug.WriteLine($"[Router] category=CreativeWriting, tools=None, confidence=0.9");
                return routing;
            }

            // === DOCUMENT TASK ===
            // Word docs, letters, CVs, reports
            if (IsDocumentTask(msg))
            {
                var routing = new IntentRouting
                {
                    Category = IntentCategory.DocumentTask,
                    Confidence = 0.95,
                    Tools = RequiredTools.WordProcessor | RequiredTools.FileSystem,
                    Style = ResponseStyle.Normal,
                    Safety = SafetyGate.None,
                    Reasoning = "Document creation/modification request"
                };
                
                Debug.WriteLine($"[Router] category=DocumentTask, tools=WordProcessor|FileSystem, confidence=0.95");
                return routing;
            }

            // === TROUBLESHOOTING ===
            // Errors, crashes, logs, debugging
            if (IsTroubleshooting(msg))
            {
                var routing = new IntentRouting
                {
                    Category = IntentCategory.Troubleshooting,
                    Confidence = 0.9,
                    Tools = RequiredTools.FileSystem | RequiredTools.CodeEditor,
                    Style = ResponseStyle.Detailed,
                    Safety = SafetyGate.None,
                    Reasoning = "Error/troubleshooting request"
                };
                
                Debug.WriteLine($"[Router] category=Troubleshooting, tools=FileSystem|CodeEditor, confidence=0.9");
                return routing;
            }

            // === CODING TASK ===
            // Write/modify code, refactor, explain code
            if (IsCodingTask(msg))
            {
                var isMultiStep = msg.Contains("add") || msg.Contains("create") || msg.Contains("build") || 
                                  msg.Contains("implement") || msg.Contains("refactor");
                
                var tools = RequiredTools.CodeEditor | RequiredTools.FileSystem;
                if (isMultiStep) tools |= RequiredTools.MultiStep;
                
                var routing = new IntentRouting
                {
                    Category = IntentCategory.CodingTask,
                    Confidence = 0.85,
                    Tools = tools,
                    Style = ResponseStyle.Normal,
                    Safety = SafetyGate.ConfirmDestructive,
                    Reasoning = isMultiStep ? "Multi-step coding task" : "Single-step coding task"
                };
                
                Debug.WriteLine($"[Router] category=CodingTask, tools={tools}, confidence=0.85");
                return routing;
            }

            // === FILE TASK ===
            // Read/write files, search project, file operations
            if (IsFileTask(msg))
            {
                var isDestructive = msg.Contains("delete") || msg.Contains("remove") || 
                                   msg.Contains("clear") || msg.Contains("wipe");
                
                var routing = new IntentRouting
                {
                    Category = IntentCategory.FileTask,
                    Confidence = 0.9,
                    Tools = RequiredTools.FileSystem,
                    Style = ResponseStyle.Normal,
                    Safety = isDestructive ? SafetyGate.ConfirmDestructive : SafetyGate.None,
                    Reasoning = "File operation request"
                };
                
                Debug.WriteLine($"[Router] category=FileTask, tools=FileSystem, confidence=0.9");
                return routing;
            }

            // === GENERAL CHAT (default) ===
            // Questions, explanations, general conversation
            var chatRouting = new IntentRouting
            {
                Category = IntentCategory.GeneralChat,
                Confidence = 0.7,
                Tools = RequiredTools.None,
                Style = ResponseStyle.Normal,
                Safety = SafetyGate.None,
                Reasoning = "General conversation/question"
            };
            
            Debug.WriteLine($"[Router] category=GeneralChat, tools=None, confidence=0.7");
            return chatRouting;
        }

        /// <summary>
        /// Detect creative writing requests (stories, scripts, marketing)
        /// </summary>
        private static bool IsCreativeWriting(string msg)
        {
            // Story/narrative keywords
            if (Regex.IsMatch(msg, @"\b(tell|write|create|make).*\b(story|tale|narrative|fiction|novel|script|screenplay|poem|song|lyrics)\b"))
                return true;
            
            // Marketing/creative copy
            if (Regex.IsMatch(msg, @"\b(write|create|draft).*\b(marketing|ad|advertisement|copy|slogan|tagline|pitch)\b"))
                return true;
            
            // Explicit story request
            if (msg.Contains("tell me a story") || msg.Contains("write a story"))
                return true;
            
            return false;
        }

        /// <summary>
        /// Detect document creation requests (Word, letters, CVs)
        /// </summary>
        private static bool IsDocumentTask(string msg)
        {
            // Word document keywords
            if (Regex.IsMatch(msg, @"\b(word|doc|docx|document)\b"))
                return true;
            
            // Letter/formal document types
            if (Regex.IsMatch(msg, @"\b(letter|cv|resume|report|memo|proposal|contract|invoice)\b"))
                return true;
            
            // Document creation verbs
            if (Regex.IsMatch(msg, @"\b(write|create|draft|generate).*\b(letter|document|report|cv|resume)\b"))
                return true;
            
            return false;
        }

        /// <summary>
        /// Detect troubleshooting requests (errors, crashes, debugging)
        /// </summary>
        private static bool IsTroubleshooting(string msg)
        {
            // Error keywords
            if (Regex.IsMatch(msg, @"\b(error|exception|crash|bug|issue|problem|fail|broken|not working)\b"))
                return true;
            
            // Debugging keywords
            if (Regex.IsMatch(msg, @"\b(debug|diagnose|troubleshoot|investigate|analyze)\b"))
                return true;
            
            // Fix requests
            if (Regex.IsMatch(msg, @"\b(fix|repair|resolve|solve).*\b(error|bug|issue|problem)\b"))
                return true;
            
            // Compile errors
            if (msg.Contains("compile error") || msg.Contains("build error") || msg.Contains("syntax error"))
                return true;
            
            return false;
        }

        /// <summary>
        /// Detect coding tasks (write/modify code, refactor)
        /// </summary>
        private static bool IsCodingTask(string msg)
        {
            // Code modification keywords
            if (Regex.IsMatch(msg, @"\b(write|create|add|modify|update|change|refactor|optimize).*\b(code|function|method|class|component|module|api)\b"))
                return true;
            
            // Code explanation
            if (Regex.IsMatch(msg, @"\b(explain|show|describe).*\b(code|implementation|algorithm)\b"))
                return true;
            
            // Specific coding actions
            if (Regex.IsMatch(msg, @"\b(implement|build|develop|program|code)\b"))
                return true;
            
            // Settings/feature additions
            if (msg.Contains("add a settings") || msg.Contains("create a page") || msg.Contains("build a feature"))
                return true;
            
            return false;
        }

        /// <summary>
        /// Detect file operation requests (read/write/search files)
        /// </summary>
        private static bool IsFileTask(string msg)
        {
            // File operations
            if (Regex.IsMatch(msg, @"\b(read|write|open|save|delete|remove|move|copy|rename).*\b(file|folder|directory)\b"))
                return true;
            
            // Search operations
            if (Regex.IsMatch(msg, @"\b(search|find|locate|look for).*\b(file|project|code|in the)\b"))
                return true;
            
            // Specific file search
            if (msg.Contains("search the project") || msg.Contains("find in project"))
                return true;
            
            return false;
        }
    }
}
