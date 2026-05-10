using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.Agent.Macros
{
    /// <summary>
    /// Installed Apps - Count and list recent installs (read-only)
    /// </summary>
    public class InstalledAppsMacro : AgentMacroDefinition
    {
        public override string Id => "installed-apps";
        public override string Title => "Installed Apps";
        public override string Description => "List installed applications";
        public override string Icon => "📦";
        public override MacroRiskLevel Risk => MacroRiskLevel.SafeReadOnly;
        public override string[] Keywords => new[] { "apps", "programs", "installed", "software", "applications", "uninstall" };

        public override async Task<MacroResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                var result = new MacroResult { Success = true };
                var cards = new List<MacroResultCard>();

                try
                {
                    var apps = GetInstalledApps();

                    // Summary card
                    var summaryCard = new MacroResultCard
                    {
                        Title = "Applications Summary",
                        Icon = "📊",
                        StatusColor = "cyan"
                    };

                    summaryCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Total Installed",
                        Value = apps.Count.ToString(),
                        Icon = "📦"
                    });

                    var withSize = apps.Where(a => a.Size > 0).ToList();
                    if (withSize.Any())
                    {
                        var totalSize = withSize.Sum(a => a.Size);
                        summaryCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Total Size",
                            Value = FormatSize(totalSize),
                            Icon = "💾"
                        });
                    }

                    var publishers = apps.Where(a => !string.IsNullOrEmpty(a.Publisher))
                        .GroupBy(a => a.Publisher)
                        .OrderByDescending(g => g.Count())
                        .Take(3);

                    summaryCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Top Publishers",
                        Value = string.Join(", ", publishers.Select(p => p.Key)),
                        Icon = "🏢"
                    });

                    cards.Add(summaryCard);

                    // Recent installs card
                    var recentCard = new MacroResultCard
                    {
                        Title = "Recently Installed",
                        Icon = "🕐",
                        StatusColor = "green"
                    };

                    var recent = apps
                        .Where(a => a.InstallDate.HasValue)
                        .OrderByDescending(a => a.InstallDate)
                        .Take(8);

                    foreach (var app in recent)
                    {
                        recentCard.Rows.Add(new MacroResultRow
                        {
                            Label = TruncateName(app.Name, 30),
                            Value = app.InstallDate?.ToString("MMM dd, yyyy") ?? "",
                            Icon = "▸"
                        });
                    }

                    if (!recentCard.Rows.Any())
                        recentCard.Rows.Add(new MacroResultRow { Label = "No install dates available", Value = "", Icon = "○" });

                    cards.Add(recentCard);

                    // Largest apps card
                    var largestCard = new MacroResultCard
                    {
                        Title = "Largest Applications",
                        Icon = "📈",
                        StatusColor = "violet"
                    };

                    var largest = apps
                        .Where(a => a.Size > 0)
                        .OrderByDescending(a => a.Size)
                        .Take(8);

                    foreach (var app in largest)
                    {
                        largestCard.Rows.Add(new MacroResultRow
                        {
                            Label = TruncateName(app.Name, 30),
                            Value = FormatSize(app.Size),
                            Icon = "▸"
                        });
                    }

                    if (!largestCard.Rows.Any())
                        largestCard.Rows.Add(new MacroResultRow { Label = "Size data unavailable", Value = "", Icon = "○" });

                    cards.Add(largestCard);

                    result.Cards = cards;
                    result.Summary = $"{apps.Count} applications installed";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private List<AppInfo> GetInstalledApps()
        {
            var apps = new Dictionary<string, AppInfo>();

            // Check both 32-bit and 64-bit registry locations
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var path in registryPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var name = subKey.GetValue("DisplayName")?.ToString();
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            if (apps.ContainsKey(name)) continue;

                            var app = new AppInfo { Name = name };

                            app.Publisher = subKey.GetValue("Publisher")?.ToString() ?? "";
                            app.Version = subKey.GetValue("DisplayVersion")?.ToString() ?? "";

                            var sizeStr = subKey.GetValue("EstimatedSize");
                            if (sizeStr != null)
                                app.Size = Convert.ToInt64(sizeStr) * 1024; // KB to bytes

                            var dateStr = subKey.GetValue("InstallDate")?.ToString();
                            if (!string.IsNullOrEmpty(dateStr) && dateStr.Length == 8)
                            {
                                if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, 
                                    System.Globalization.DateTimeStyles.None, out var date))
                                    app.InstallDate = date;
                            }

                            apps[name] = app;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Also check current user
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var name = subKey.GetValue("DisplayName")?.ToString();
                            if (string.IsNullOrWhiteSpace(name) || apps.ContainsKey(name)) continue;

                            apps[name] = new AppInfo
                            {
                                Name = name,
                                Publisher = subKey.GetValue("Publisher")?.ToString() ?? "",
                                Version = subKey.GetValue("DisplayVersion")?.ToString() ?? ""
                            };
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return apps.Values.OrderBy(a => a.Name).ToList();
        }

        private string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        private string TruncateName(string name, int maxLen)
        {
            if (name.Length <= maxLen) return name;
            return name.Substring(0, maxLen - 3) + "...";
        }

        private class AppInfo
        {
            public string Name { get; set; } = "";
            public string Publisher { get; set; } = "";
            public string Version { get; set; } = "";
            public long Size { get; set; }
            public DateTime? InstallDate { get; set; }
        }
    }
}
