using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.AI
{
    public enum AIProviderType
    {
        Claude,
        OpenAI,
        Gemini,
        Local
    }

    public class AIModel
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsAvailable { get; set; } = true;
        public int MaxTokens { get; set; } = 4000;
    }

    public class AIResponse
    {
        public string Content { get; set; } = "";
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public int TokensUsed { get; set; }
        public string Model { get; set; } = "";
        public AIProviderType Provider { get; set; }
        public string TaskBucket { get; set; } = "";
        public string RouteSummary { get; set; } = "";
    }

    public interface IAIProvider
    {
        string DisplayName { get; }
        AIProviderType ProviderType { get; }
        bool IsConfigured { get; }
        
        Task<bool> ConfigureAsync(Dictionary<string, string> config);
        Task<List<AIModel>> GetModelsAsync();
        Task<AIResponse> SendMessageAsync(List<object> messages, string model = "", int maxTokens = 500, CancellationToken ct = default);
        Task<bool> TestConnectionAsync();
    }
}
