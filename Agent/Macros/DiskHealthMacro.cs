using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AtlasAI.Agent.Macros
{
    /// <summary>
    /// Disk Health - Drive list, free space
    /// </summary>
    public class DiskHealthMacro : AgentMacroDefinition
    {
        public override string Id => "disk-health";
        public override string Title => "Disk Health";
        public override string Description => "Drive space and health status";
        public override string Icon => "💾";
        public override MacroRiskLevel Risk => MacroRiskLevel.SafeReadOnly;
        public override string[] Keywords => new[] { "disk", "drive", "storage", "space", "hdd", "ssd", "free space", "hard drive" };

        public override async Task<MacroResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                var result = new MacroResult { Success = true };
                var cards = new List<MacroResultCard>();

                try
                {
                    var drives = DriveInfo.GetDrives();
                    long totalSpace = 0;
                    long totalFree = 0;

                    foreach (var drive in drives)
                    {
                        if (!drive.IsReady) continue;

                        var driveCard = new MacroResultCard
                        {
                            Title = $"Drive {drive.Name.TrimEnd('\\')}",
                            Icon = GetDriveIcon(drive.DriveType)
                        };

                        var totalGB = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                        var freeGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                        var usedGB = totalGB - freeGB;
                        var usedPercent = (usedGB / totalGB) * 100;

                        totalSpace += drive.TotalSize;
                        totalFree += drive.AvailableFreeSpace;

                        // Determine status color based on usage
                        driveCard.StatusColor = usedPercent > 90 ? "red" : usedPercent > 75 ? "yellow" : "green";

                        driveCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Type",
                            Value = drive.DriveType.ToString(),
                            Icon = "📀"
                        });

                        if (!string.IsNullOrEmpty(drive.VolumeLabel))
                        {
                            driveCard.Rows.Add(new MacroResultRow
                            {
                                Label = "Label",
                                Value = drive.VolumeLabel,
                                Icon = "🏷️"
                            });
                        }

                        driveCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Format",
                            Value = drive.DriveFormat,
                            Icon = "📋"
                        });

                        driveCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Total",
                            Value = FormatSize(drive.TotalSize),
                            Icon = "📊"
                        });

                        driveCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Used",
                            Value = $"{FormatSize(drive.TotalSize - drive.AvailableFreeSpace)} ({usedPercent:F0}%)",
                            Icon = "📈",
                            ValueColor = usedPercent > 90 ? "red" : usedPercent > 75 ? "yellow" : null
                        });

                        driveCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Free",
                            Value = FormatSize(drive.AvailableFreeSpace),
                            Icon = "✓",
                            ValueColor = freeGB < 10 ? "red" : freeGB < 50 ? "yellow" : "green"
                        });

                        // Visual bar
                        driveCard.Footer = GenerateUsageBar(usedPercent);

                        cards.Add(driveCard);
                    }

                    // Summary card
                    if (cards.Count > 1)
                    {
                        var summaryCard = new MacroResultCard
                        {
                            Title = "Total Storage",
                            Icon = "📊",
                            StatusColor = "cyan"
                        };

                        var totalUsedPercent = ((totalSpace - totalFree) / (double)totalSpace) * 100;

                        summaryCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Total Capacity",
                            Value = FormatSize(totalSpace),
                            Icon = "💾"
                        });

                        summaryCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Total Free",
                            Value = FormatSize(totalFree),
                            Icon = "✓",
                            ValueColor = "green"
                        });

                        summaryCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Overall Usage",
                            Value = $"{totalUsedPercent:F0}%",
                            Icon = "📈"
                        });

                        cards.Insert(0, summaryCard);
                    }

                    result.Cards = cards;
                    result.Summary = $"{cards.Count - 1} drives | {FormatSize(totalFree)} free of {FormatSize(totalSpace)}";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private string GetDriveIcon(DriveType type)
        {
            return type switch
            {
                DriveType.Fixed => "💾",
                DriveType.Removable => "💿",
                DriveType.Network => "🌐",
                DriveType.CDRom => "📀",
                _ => "📁"
            };
        }

        private string FormatSize(long bytes)
        {
            if (bytes >= 1_099_511_627_776)
                return $"{bytes / 1_099_511_627_776.0:F1} TB";
            if (bytes >= 1_073_741_824)
                return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)
                return $"{bytes / 1_048_576.0:F1} MB";
            return $"{bytes / 1024.0:F1} KB";
        }

        private string GenerateUsageBar(double percent)
        {
            var filled = (int)(percent / 5);
            var empty = 20 - filled;
            var bar = new string('█', Math.Min(filled, 20)) + new string('░', Math.Max(empty, 0));
            return $"[{bar}] {percent:F0}%";
        }
    }
}
