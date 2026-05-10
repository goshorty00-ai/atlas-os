using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.AI;
using AtlasAI.Views.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtlasAI.MediaScanner
{
    public class LyricLine : INotifyPropertyChanged
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; } = "";
        
        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent != value)
                {
                    _isCurrent = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class LyricsService
    {
        private static LyricsService? _instance;
        public static LyricsService Instance => _instance ??= new LyricsService();

        private LyricsService() { }

        public async Task<List<LyricLine>> GetLyricsAsync(MediaItem item, System.Threading.CancellationToken ct = default)
        {
            if (item == null) return new List<LyricLine>();

            var localLrc = await TryLoadLocalLrcAsync(item.FilePath);
            if (localLrc.Count > 0) return localLrc;

            var title = !string.IsNullOrWhiteSpace(item.DisplayName) ? item.DisplayName : Path.GetFileNameWithoutExtension(item.FilePath);
            var artist = !string.IsNullOrWhiteSpace(item.Artist) ? item.Artist : "";
            var album = !string.IsNullOrWhiteSpace(item.Album) ? item.Album : "";

            var online = await LrcLibClient.Instance.TryGetLyricsAsync(artist, title, album, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(online))
            {
                var parsedOnline = ParseLrc(online);
                if (parsedOnline.Count > 0) return parsedOnline;
                return ParsePlainText(online);
            }

            var ovh = await LyricsOvhClient.Instance.TryGetLyricsAsync(artist, title, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(ovh))
            {
                var parsedOvh = ParseLrc(ovh);
                if (parsedOvh.Count > 0) return parsedOvh;
                return ParsePlainText(ovh);
            }

            var aiLyrics = await FetchFromAiAsync(item, ct);
            if (!string.IsNullOrWhiteSpace(aiLyrics))
            {
                var parsed = ParseLrc(aiLyrics);
                if (parsed.Count > 0) return parsed;
                
                return ParsePlainText(aiLyrics);
            }

            return new List<LyricLine>();
        }

        private async Task<List<LyricLine>> TryLoadLocalLrcAsync(string mediaPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mediaPath)) return new List<LyricLine>();

                var directory = Path.GetDirectoryName(mediaPath);
                var filename = Path.GetFileNameWithoutExtension(mediaPath);
                if (directory == null) return new List<LyricLine>();

                var lrcPath = Path.Combine(directory, filename + ".lrc");
                if (File.Exists(lrcPath))
                {
                    var content = await File.ReadAllTextAsync(lrcPath);
                    return ParseLrc(content);
                }
            }
            catch
            {
                // Ignore IO errors
            }
            return new List<LyricLine>();
        }

        private async Task<string> FetchFromAiAsync(MediaItem item, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var title = !string.IsNullOrWhiteSpace(item.DisplayName) ? item.DisplayName : Path.GetFileNameWithoutExtension(item.FilePath);
                var artist = !string.IsNullOrWhiteSpace(item.Artist) ? item.Artist : "Unknown Artist";
                
                var prompt = $"Please provide the lyrics for the song '{title}' by '{artist}'. " +
                             "If possible, format them in LRC format with timestamps like [mm:ss.xx]. " +
                             "If timestamps are impossible, just return the lines of text. " +
                             "Do NOT include any conversational text, just the lyrics.";

                var messages = new List<object>
                {
                    new { role = "system", content = "You are a music lyrics database. Provide accurate lyrics in LRC format if possible." },
                    new { role = "user", content = prompt }
                };

                var response = await AIManager.SendMessageAsync("LyricsFetcher", messages, 600, ct).ConfigureAwait(false);
                if (response != null && response.Success && !string.IsNullOrWhiteSpace(response.Content))
                    return response.Content ?? "";
                return "";
            }
            catch (OperationCanceledException)
            {
                return "CANCELLED · OPERATION STOPPED";
            }
            catch
            {
                return "";
            }
        }

        public List<LyricLine> ParseLrc(string content)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrWhiteSpace(content)) return lines;

            // Regex for [mm:ss.xx] or [mm:ss]
            var regex = new Regex(@"\[(\d{2}):(\d{2})(?:\.(\d{2,3}))?\](.*)");

            var rawLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in rawLines)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    var minutes = int.Parse(match.Groups[1].Value);
                    var seconds = int.Parse(match.Groups[2].Value);
                    var milliseconds = 0;
                    
                    if (match.Groups[3].Success)
                    {
                        var msPart = match.Groups[3].Value;
                        if (msPart.Length == 2) milliseconds = int.Parse(msPart) * 10;
                        else milliseconds = int.Parse(msPart);
                    }

                    var text = match.Groups[4].Value.Trim();
                    
                    lines.Add(new LyricLine
                    {
                        Timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds),
                        Text = text
                    });
                }
            }

            return lines.OrderBy(l => l.Timestamp).ToList();
        }

        private List<LyricLine> ParsePlainText(string content)
        {
            var lines = new List<LyricLine>();
            var rawLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            
            // Without timestamps, we can't really sync, but we can display them.
            // We'll assign dummy timestamps just to keep the type consistent, 
            // but the UI might need to handle "unsynced" mode or just show them as a static list.
            foreach (var line in rawLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                lines.Add(new LyricLine { Timestamp = TimeSpan.Zero, Text = line.Trim() });
            }
            return lines;
        }
    }
}
