using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AtlasAI.Modules.Downloader.Csv
{
    public class SpotifyExportTrack
    {
        public string TrackUri { get; set; } = "";
        public string Title { get; set; } = "";
        public string Album { get; set; } = "";
        public string Artists { get; set; } = "";
        public int Year { get; set; }
        public int DurationMs { get; set; }
    }

    public static class SpotifyExportCsvParser
    {
        public static List<SpotifyExportTrack> Parse(string csvPath)
        {
            var lines = File.ReadAllLines(csvPath);
            var result = new List<SpotifyExportTrack>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = ParseLine(line);
                if (cells.Count < 4) continue;

                var first = (cells[0] ?? "").Trim();
                if (first.Equals("track uri", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("uri", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("spotify uri", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var uri = first;
                var title = (cells.ElementAtOrDefault(1) ?? "").Trim().Trim('"');
                var album = (cells.ElementAtOrDefault(2) ?? "").Trim().Trim('"');
                var artists = (cells.ElementAtOrDefault(3) ?? "").Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(uri) ||
                    (!uri.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase) &&
                     !uri.Contains("open.spotify.com/track", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var year = 0;
                var durationMs = 0;

                var dateCell = (cells.ElementAtOrDefault(4) ?? "").Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(dateCell))
                {
                    if (dateCell.Length >= 4 && int.TryParse(dateCell.AsSpan(0, 4), out var y)) year = y;
                }

                var durCell = (cells.ElementAtOrDefault(5) ?? "").Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(durCell))
                {
                    if (int.TryParse(durCell, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)) durationMs = ms;
                }

                if (string.IsNullOrWhiteSpace(title)) continue;

                result.Add(new SpotifyExportTrack
                {
                    TrackUri = uri,
                    Title = title,
                    Album = album,
                    Artists = artists,
                    Year = year,
                    DurationMs = durationMs
                });
            }

            return result;
        }

        private static List<string> ParseLine(string line)
        {
            var result = new List<string>();
            if (line == null) return result;

            var current = new System.Text.StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }
            result.Add(current.ToString());
            return result;
        }
    }
}

