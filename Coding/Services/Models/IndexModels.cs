using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AtlasAI.Coding.Services.Models
{
    /// <summary>
    /// Root index structure for persistence.
    /// </summary>
    public class ProjectIndex
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;
        
        [JsonPropertyName("workspaceHash")]
        public string WorkspaceHash { get; set; } = "";
        
        [JsonPropertyName("projectPath")]
        public string ProjectPath { get; set; } = "";
        
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }
        
        [JsonPropertyName("files")]
        public List<FileIndexEntry> Files { get; set; } = new();
        
        [JsonPropertyName("symbols")]
        public List<SymbolEntry> Symbols { get; set; } = new();
        
        [JsonPropertyName("chunks")]
        public List<ChunkEntry> Chunks { get; set; } = new();
    }
    
    /// <summary>
    /// Index entry for a single file.
    /// </summary>
    public class FileIndexEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";
        
        [JsonPropertyName("relativePath")]
        public string RelativePath { get; set; } = "";
        
        [JsonPropertyName("size")]
        public long Size { get; set; }
        
        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }
        
        [JsonPropertyName("language")]
        public string Language { get; set; } = "";
        
        [JsonPropertyName("hash")]
        public string ContentHash { get; set; } = "";
    }
    
    /// <summary>
    /// Index entry for a code symbol (class, method, etc).
    /// </summary>
    public class SymbolEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("kind")]
        public SymbolKind Kind { get; set; }
        
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = "";
        
        [JsonPropertyName("line")]
        public int Line { get; set; }
        
        [JsonPropertyName("signature")]
        public string Signature { get; set; } = "";
        
        [JsonPropertyName("parentSymbol")]
        public string? ParentSymbol { get; set; }
        
        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();
    }
    
    /// <summary>
    /// Text chunk for semantic retrieval.
    /// </summary>
    public class ChunkEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = "";
        
        [JsonPropertyName("startLine")]
        public int StartLine { get; set; }
        
        [JsonPropertyName("endLine")]
        public int EndLine { get; set; }
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
        
        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();
        
        [JsonPropertyName("symbolContext")]
        public string? SymbolContext { get; set; }
    }
}
