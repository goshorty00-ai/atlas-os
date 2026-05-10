using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AtlasAI
{
    public class ClipboardItem
    {
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = "Text";
        public string Preview => Content.Length > 50 ? Content.Substring(0, 50) + "..." : Content;
    }

    public static class ClipboardManager
    {
        private static readonly List<ClipboardItem> _clipboardHistory = new();
        private static readonly int MaxHistoryItems = 50;
        private static string? _lastClipboardContent;
        private static DispatcherTimer? _clipboardTimer;
        
        private static readonly string HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "clipboard_history.json");

        public static event Action<ClipboardItem>? ClipboardChanged;

        public static void Initialize()
        {
            LoadHistory();
            StartMonitoring();
        }

        public static void StartMonitoring()
        {
            _clipboardTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _clipboardTimer.Tick += CheckClipboard;
            _clipboardTimer.Start();
        }

        public static void StopMonitoring()
        {
            _clipboardTimer?.Stop();
        }

        private static void CheckClipboard(object? sender, EventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var currentContent = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(currentContent) && 
                        currentContent != _lastClipboardContent)
                    {
                        AddToHistory(currentContent);
                        _lastClipboardContent = currentContent;
                    }
                }
            }
            catch
            {
                // Clipboard access can fail, ignore
            }
        }

        private static void AddToHistory(string content)
        {
            // Don't add duplicates or very short content
            if (content.Length < 3 || _clipboardHistory.Any(item => item.Content == content))
                return;

            var clipboardItem = new ClipboardItem
            {
                Content = content,
                Timestamp = DateTime.Now,
                Type = "Text"
            };

            _clipboardHistory.Insert(0, clipboardItem);

            // Keep only the most recent items
            if (_clipboardHistory.Count > MaxHistoryItems)
            {
                _clipboardHistory.RemoveRange(MaxHistoryItems, _clipboardHistory.Count - MaxHistoryItems);
            }

            ClipboardChanged?.Invoke(clipboardItem);
            SaveHistory();
        }

        public static List<ClipboardItem> GetHistory()
        {
            return _clipboardHistory.ToList();
        }

        public static void CopyToClipboard(string content)
        {
            try
            {
                _lastClipboardContent = content;
                Clipboard.SetText(content);
            }
            catch
            {
                // Clipboard access can fail
            }
        }

        public static void ClearHistory()
        {
            _clipboardHistory.Clear();
            SaveHistory();
        }

        public static List<ClipboardItem> SearchHistory(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GetHistory();

            return _clipboardHistory
                .Where(item => item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static void LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryPath))
                {
                    var json = File.ReadAllText(HistoryPath);
                    var items = JsonSerializer.Deserialize<List<ClipboardItem>>(json);
                    if (items != null)
                    {
                        _clipboardHistory.Clear();
                        _clipboardHistory.AddRange(items.Take(MaxHistoryItems));
                    }
                }
            }
            catch
            {
                // If loading fails, start with empty history
            }
        }

        private static void SaveHistory()
        {
            try
            {
                var dir = Path.GetDirectoryName(HistoryPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_clipboardHistory, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryPath, json);
            }
            catch
            {
                // If saving fails, continue without persistence
            }
        }

        public static void Dispose()
        {
            StopMonitoring();
            SaveHistory();
        }
    }
}