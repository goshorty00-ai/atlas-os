#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Memory.Models;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Captures decisions made during agent operations and stores them as memories.
    /// </summary>
    public class DecisionCaptureEngine
    {
        private static DecisionCaptureEngine? _instance;
        private static readonly object _instanceLock = new();

        public static DecisionCaptureEngine Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new DecisionCaptureEngine();
                    }
                }
                return _instance;
            }
        }

        private DecisionCaptureEngine() { }

        private ProjectMemoryStore Store => ProjectMemoryStore.Instance;

        public async Task<MemoryEntry?> CaptureRefactoringDecisionAsync(string description, double confidence = 0.7)
        {
            if (!await IsReusableDecisionAsync(description, MemoryEntryType.Pattern))
                return null;

            var adjustedConfidence = await CalculateConfidenceAsync(description, MemoryEntryType.Pattern, confidence);
            return await Store.AddMemoryAsync(MemoryEntryType.Pattern, MemorySource.Agent, description, adjustedConfidence);
        }

        public async Task<MemoryEntry?> CaptureNamingConventionAsync(string description, double confidence = 0.8)
        {
            if (!await IsReusableDecisionAsync(description, MemoryEntryType.Convention))
                return null;

            var adjustedConfidence = await CalculateConfidenceAsync(description, MemoryEntryType.Convention, confidence);
            return await Store.AddMemoryAsync(MemoryEntryType.Convention, MemorySource.Agent, description, adjustedConfidence);
        }

        public async Task<MemoryEntry?> CapturePatternUsageAsync(string description, double confidence = 0.7)
        {
            if (!await IsReusableDecisionAsync(description, MemoryEntryType.Pattern))
                return null;

            var adjustedConfidence = await CalculateConfidenceAsync(description, MemoryEntryType.Pattern, confidence);
            return await Store.AddMemoryAsync(MemoryEntryType.Pattern, MemorySource.Agent, description, adjustedConfidence);
        }


        public async Task<MemoryEntry?> CaptureRejectionAsync(string description, double confidence = 0.9)
        {
            var adjustedConfidence = await CalculateConfidenceAsync(description, MemoryEntryType.Rejection, confidence);
            return await Store.AddMemoryAsync(MemoryEntryType.Rejection, MemorySource.Agent, description, adjustedConfidence);
        }

        public async Task<MemoryEntry?> CaptureToolingChoiceAsync(string description, double confidence = 0.8)
        {
            if (!await IsReusableDecisionAsync(description, MemoryEntryType.Tooling))
                return null;

            var adjustedConfidence = await CalculateConfidenceAsync(description, MemoryEntryType.Tooling, confidence);
            return await Store.AddMemoryAsync(MemoryEntryType.Tooling, MemorySource.Agent, description, adjustedConfidence);
        }

        public async Task<MemoryEntry?> CaptureArchitectureDecisionAsync(string description, double confidence = 0.8)
        {
            if (!await IsReusableDecisionAsync(description, MemoryEntryType.Architecture))
                return null;

            var adjustedConfidence = await CalculateConfidenceAsync(description, MemoryEntryType.Architecture, confidence);
            return await Store.AddMemoryAsync(MemoryEntryType.Architecture, MemorySource.Agent, description, adjustedConfidence);
        }

        public async Task<MemoryEntry?> CaptureSafetyConstraintAsync(string description, double confidence = 0.95)
        {
            var adjustedConfidence = await CalculateConfidenceAsync(description, MemoryEntryType.Safety, confidence);
            return await Store.AddMemoryAsync(MemoryEntryType.Safety, MemorySource.Agent, description, adjustedConfidence);
        }

        public async Task LearnFromAcceptanceAsync(string entryId)
        {
            var memories = Store.GetAllMemories();
            var entry = memories.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return;

            var newConfidence = Math.Min(1.0, entry.Confidence + 0.1);
            await Store.UpdateConfidenceAsync(entryId, newConfidence);
            await Store.MarkAppliedAsync(entryId);

            System.Diagnostics.Debug.WriteLine($"[DecisionCapture] Learned from acceptance: {entryId}, new confidence: {newConfidence:F2}");
        }

        public async Task LearnFromRejectionAsync(string entryId, string? rejectionReason = null)
        {
            var memories = Store.GetAllMemories();
            var entry = memories.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return;

            var newConfidence = entry.Confidence - 0.2;

            if (newConfidence <= 0.3)
            {
                await Store.RemoveMemoryAsync(entryId);
                System.Diagnostics.Debug.WriteLine($"[DecisionCapture] Removed low-confidence entry: {entryId}");

                if (!string.IsNullOrEmpty(rejectionReason))
                {
                    await CaptureRejectionAsync($"Avoid: {entry.Note}. Reason: {rejectionReason}");
                }
            }
            else
            {
                await Store.UpdateConfidenceAsync(entryId, newConfidence);
                System.Diagnostics.Debug.WriteLine($"[DecisionCapture] Decreased confidence: {entryId}, new: {newConfidence:F2}");
            }
        }


        private async Task<double> CalculateConfidenceAsync(string description, MemoryEntryType type, double baseConfidence)
        {
            var existingMemories = Store.GetMemoriesByType(type);
            var similarCount = existingMemories.Count(m => IsSimilar(m.Note, description));

            if (similarCount > 0)
            {
                return Math.Min(1.0, baseConfidence + (similarCount * 0.05));
            }

            return baseConfidence;
        }

        private async Task<bool> IsReusableDecisionAsync(string description, MemoryEntryType type)
        {
            if (string.IsNullOrWhiteSpace(description) || description.Length < 10)
                return false;

            var existingMemories = Store.GetMemoriesByType(type);
            var isDuplicate = existingMemories.Any(m => 
                string.Equals(m.Note, description, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                System.Diagnostics.Debug.WriteLine($"[DecisionCapture] Skipping duplicate: {description[..Math.Min(50, description.Length)]}...");
                return false;
            }

            return true;
        }

        private static bool IsSimilar(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            var wordsA = a.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var wordsB = b.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var commonWords = wordsA.Intersect(wordsB).Count();
            var totalWords = Math.Max(wordsA.Length, wordsB.Length);

            return totalWords > 0 && (double)commonWords / totalWords > 0.5;
        }

        public async Task<MemoryEntry?> CaptureUserDecisionAsync(MemoryEntryType type, string description)
        {
            return await Store.AddMemoryAsync(type, MemorySource.User, description, 1.0);
        }
    }
}
