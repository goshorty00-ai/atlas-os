using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.Agent.Macros
{
    /// <summary>
    /// Startup Inventory - List startup items (read-only)
    /// </summary>
    public class StartupInventoryMacro : AgentMacroDefinition
    {
        public override string Id => "startup-inventory";
        public override string Title => "Startup Inventory";
        public override string Description => "List all startup programs (read-only)";
        public override string Icon => "🚀";
        public override MacroRiskLevel Risk => MacroRiskLevel.SafeReadOnly;
        public override string[] Keywords => new[] { "startup", "boot", "autostart", "autorun", "start with windows" };

        public override async Task<MacroResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                var result = new MacroResult { Success = true };
                var cards = new List<MacroResultCard>();
                var totalItems = 0;

                try
                {
                    // User startup items
                    var userCard = new MacroResultCard
                    {
                        Title = "User Startup Items",
                        Icon = "👤",
                        StatusColor = "cyan"
                    };

                    var userItems = GetStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                    foreach (var item in userItems)
                    {
                        userCard.Rows.Add(new MacroResultRow
                        {
                            Label = item.Name,
                            Value = TruncatePath(item.Path, 50),
                            Icon = "▸"
                        });
                        totalItems++;
                    }

                    if (userCard.Rows.Count == 0)
                        userCard.Rows.Add(new MacroResultRow { Label = "No items", Value = "", Icon = "○" });

                    userCard.Footer = $"{userItems.Count} items";
                    cards.Add(userCard);

                    // System startup items
                    var sysCard = new MacroResultCard
                    {
                        Title = "System Startup Items",
                        Icon = "🖥️",
                        StatusColor = "violet"
                    };

                    var sysItems = GetStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    foreach (var item in sysItems)
                    {
                        sysCard.Rows.Add(new MacroResultRow
                        {
                            Label = item.Name,
                            Value = TruncatePath(item.Path, 50),
                            Icon = "▸"
                        });
                        totalItems++;
                    }

                    if (sysCard.Rows.Count == 0)
                        sysCard.Rows.Add(new MacroResultRow { Label = "No items", Value = "", Icon = "○" });

                    sysCard.Footer = $"{sysItems.Count} items";
                    cards.Add(sysCard);

                    // Startup folder items
                    var folderCard = new MacroResultCard
                    {
                        Title = "Startup Folder",
                        Icon = "📁",
                        StatusColor = "green"
                    };

                    var folderItems = GetStartupFolderItems();
                    foreach (var item in folderItems)
                    {
                        folderCard.Rows.Add(new MacroResultRow
                        {
                            Label = item,
                            Value = "",
                            Icon = "▸"
                        });
                        totalItems++;
                    }

                    if (folderCard.Rows.Count == 0)
                        folderCard.Rows.Add(new MacroResultRow { Label = "No items", Value = "", Icon = "○" });

                    folderCard.Footer = $"{folderItems.Count} items";
                    cards.Add(folderCard);

                    result.Cards = cards;
                    result.Summary = $"{totalItems} startup items found";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private List<StartupItem> GetStartupItems(string keyPath, bool machineWide)
        {
            var items = new List<StartupItem>();
            try
            {
                var baseKey = machineWide ? Registry.LocalMachine : Registry.CurrentUser;
                using var key = baseKey.OpenSubKey(keyPath);
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        var value = key.GetValue(name)?.ToString() ?? "";
                        items.Add(new StartupItem { Name = name, Path = value });
                    }
                }
            }
            catch { }
            return items;
        }

        private List<string> GetStartupFolderItems()
        {
            var items = new List<string>();
            try
            {
                var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (System.IO.Directory.Exists(startupPath))
                {
                    foreach (var file in System.IO.Directory.GetFiles(startupPath))
                    {
                        items.Add(System.IO.Path.GetFileName(file));
                    }
                }
            }
            catch { }
            return items;
        }

        private string TruncatePath(string path, int maxLen)
        {
            if (path.Length <= maxLen) return path;
            return "..." + path.Substring(path.Length - maxLen + 3);
        }

        private class StartupItem
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
        }
    }
}
