#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AtlasAI.Memory.Models;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Injects project memory context into AI prompts.
    /// </summary>
    public class MemoryContextInjector
    {
        private static MemoryContextInjector? _instance;
        private static readonly object _instanceLock = new();

        public const int MaxEntries = 20;
        public const int MaxContextLength = 2000;

        public static MemoryContextInjector Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new MemoryContextInjector();
                    }
                }
                return _instance;
            }
        }

        private MemoryContextInjector() { }

        private ProjectMemoryStore Store => ProjectMemoryStore.Instance;

        /// <summary>
        /// Build full project context from all memories.
        /// </summary>
        public string BuildProjectContext()
        {
            var memories = Store.GetAllMemories();
            if (memories.Count == 0)
                return string.Empty;

            var prioritized = PrioritizeByConfidence(memories);
            return FormatContext(prioritized, "Project Context Summary");
        }

        /// <summary>
        /// Build context relevant to a specific task.
        /// </summary>
        public string BuildTaskRelevantContext(string taskDescription)
        {
            var memories = Store.GetAllMemories();
            if (memories.Count == 0)
                return string.Empty;

            var relevant = FilterByRelevance(memories, taskDescription);
            var prioritized = PrioritizeByConfidence(relevant);
            return FormatContext(prioritized, "Relevant Project Context");
        }

        private List<MemoryEntry> FilterByRelevance(IReadOnlyList<MemoryEntry> memories, string task)
        {
            return memories.Where(m => 
                m.Note.Contains(task, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        private List<MemoryEntry> PrioritizeByConfidence(IReadOnlyList<MemoryEntry> memories)
        {
            return memories.OrderByDescending(m => m.Confidence)
                          .ThenByDescending(m => m.CreatedAt)
                          .Take(MaxEntries)
                          .ToList();
        }

        private string FormatContext(List<MemoryEntry> memories, string header)
        {
            if (memories.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"=== {header} ===");
            
            foreach (var memory in memories)
            {
                sb.AppendLine($"[{memory.Type}] {memory.Note}");
                // Note: MemoryEntry doesn't have Tags property
            }

            var result = sb.ToString();
            if (result.Length > MaxContextLength)
                result = result.Substring(0, MaxContextLength) + "... (truncated)";

            return result;
        }
    }
}
