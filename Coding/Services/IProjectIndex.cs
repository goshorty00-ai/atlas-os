using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Interface for project indexing service that enables fast file and symbol search.
    /// </summary>
    public interface IProjectIndex
    {
        /// <summary>
        /// Build index for entire project.
        /// </summary>
        Task BuildIndexAsync(string projectPath);
        
        /// <summary>
        /// Incrementally update index for a single file.
        /// </summary>
        Task UpdateFileAsync(string filePath);
        
        /// <summary>
        /// Remove file from index.
        /// </summary>
        Task RemoveFileAsync(string filePath);
        
        /// <summary>
        /// Search the index for relevant files and snippets.
        /// </summary>
        Task<List<SearchResult>> SearchAsync(string query, int topK = 10, SearchFilters? filters = null);
        
        /// <summary>
        /// Save index to disk for persistence.
        /// </summary>
        Task SaveIndexAsync();
        
        /// <summary>
        /// Load index from disk.
        /// </summary>
        Task<bool> LoadIndexAsync();
        
        /// <summary>
        /// Get all indexed file paths.
        /// </summary>
        List<string> GetIndexedFiles();
        
        /// <summary>
        /// Get index statistics.
        /// </summary>
        IndexStats GetStats();
    }
    
    public class SearchResult
    {
        public string FilePath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string Snippet { get; set; } = "";
        public double Score { get; set; }
        public string Reason { get; set; } = ""; // "keyword match", "symbol match", "semantic match"
        public int Line { get; set; }
    }
    
    public class SearchFilters
    {
        public List<string>? FileExtensions { get; set; }
        public List<string>? ExcludePaths { get; set; }
        public SymbolKind? SymbolKind { get; set; }
    }
    
    public class IndexStats
    {
        public int FileCount { get; set; }
        public int SymbolCount { get; set; }
        public int ChunkCount { get; set; }
        public DateTime LastUpdated { get; set; }
        public long IndexSizeBytes { get; set; }
    }
    
    public enum SymbolKind
    {
        Class,
        Interface,
        Struct,
        Enum,
        Method,
        Property,
        Field,
        Event,
        Namespace
    }
}
