using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// JSON Formatter - Format, minify, and validate JSON.
    /// </summary>
    public static class JsonFormatter
    {
        /// <summary>
        /// Handle JSON commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("json") && !lower.Contains("format") && !lower.Contains("minify"))
                return Task.FromResult<string?>(null);
            
            // Get clipboard content
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
            });
            
            if (string.IsNullOrEmpty(text))
                return Task.FromResult<string?>("📋 Copy some JSON to clipboard first!");
            
            // Format/prettify JSON
            if (lower.Contains("format") || lower.Contains("prettify") || lower.Contains("pretty"))
            {
                return Task.FromResult<string?>(FormatJson(text));
            }
            
            // Minify JSON
            if (lower.Contains("minify") || lower.Contains("compress") || lower.Contains("compact"))
            {
                return Task.FromResult<string?>(MinifyJson(text));
            }
            
            // Validate JSON
            if (lower.Contains("validate") || lower.Contains("check") || lower.Contains("valid"))
            {
                return Task.FromResult<string?>(ValidateJson(text));
            }
            
            // Default: format
            if (lower.Contains("json"))
            {
                return Task.FromResult<string?>(FormatJson(text));
            }
            
            return Task.FromResult<string?>(null);
        }
        
        private static string FormatJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var formatted = JsonSerializer.Serialize(doc.RootElement, options);
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(formatted));
                
                var preview = formatted.Length > 500 ? formatted.Substring(0, 497) + "..." : formatted;
                
                return $"✅ **JSON Formatted:**\n```json\n{preview}\n```\n✓ Copied to clipboard!";
            }
            catch (JsonException ex)
            {
                return $"❌ **Invalid JSON:**\n{ex.Message}\n\nLine: {ex.LineNumber}, Position: {ex.BytePositionInLine}";
            }
        }
        
        private static string MinifyJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var minified = JsonSerializer.Serialize(doc.RootElement);
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(minified));
                
                var originalSize = json.Length;
                var newSize = minified.Length;
                var saved = originalSize - newSize;
                var percent = (saved * 100.0 / originalSize);
                
                var preview = minified.Length > 200 ? minified.Substring(0, 197) + "..." : minified;
                
                return $"✅ **JSON Minified:**\n```\n{preview}\n```\n" +
                       $"📊 {originalSize:N0} → {newSize:N0} bytes ({percent:F1}% smaller)\n" +
                       $"✓ Copied to clipboard!";
            }
            catch (JsonException ex)
            {
                return $"❌ **Invalid JSON:**\n{ex.Message}";
            }
        }
        
        private static string ValidateJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                
                var stats = AnalyzeJson(doc.RootElement);
                
                return $"✅ **Valid JSON!**\n\n" +
                       $"📊 **Stats:**\n" +
                       $"• Type: {doc.RootElement.ValueKind}\n" +
                       $"• Size: {json.Length:N0} bytes\n" +
                       $"• Objects: {stats.Objects}\n" +
                       $"• Arrays: {stats.Arrays}\n" +
                       $"• Properties: {stats.Properties}\n" +
                       $"• Depth: {stats.MaxDepth}";
            }
            catch (JsonException ex)
            {
                return $"❌ **Invalid JSON:**\n\n" +
                       $"Error: {ex.Message}\n" +
                       $"Line: {ex.LineNumber}\n" +
                       $"Position: {ex.BytePositionInLine}";
            }
        }
        
        private static (int Objects, int Arrays, int Properties, int MaxDepth) AnalyzeJson(JsonElement element, int depth = 0)
        {
            int objects = 0, arrays = 0, properties = 0, maxDepth = depth;
            
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    objects = 1;
                    foreach (var prop in element.EnumerateObject())
                    {
                        properties++;
                        var (o, a, p, d) = AnalyzeJson(prop.Value, depth + 1);
                        objects += o;
                        arrays += a;
                        properties += p;
                        maxDepth = Math.Max(maxDepth, d);
                    }
                    break;
                    
                case JsonValueKind.Array:
                    arrays = 1;
                    foreach (var item in element.EnumerateArray())
                    {
                        var (o, a, p, d) = AnalyzeJson(item, depth + 1);
                        objects += o;
                        arrays += a;
                        properties += p;
                        maxDepth = Math.Max(maxDepth, d);
                    }
                    break;
            }
            
            return (objects, arrays, properties, maxDepth);
        }
    }
}
