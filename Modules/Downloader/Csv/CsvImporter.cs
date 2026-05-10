using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AtlasAI.Modules.Downloader.Csv
{
    public static class CsvImporter
    {
        public static List<string> ExtractUrls(string csvPath)
        {
            var lines = File.ReadAllLines(csvPath);
            var urls = new List<string>();
            foreach (var line in lines.Skip(0))
            {
                var cells = ParseLine(line);
                foreach (var c in cells)
                {
                    var t = (c ?? "").Trim();
                    if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        urls.Add(t);
                    }
                }
            }
            return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

