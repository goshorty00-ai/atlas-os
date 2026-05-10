using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;

namespace AtlasAI.Agent.Macros
{
    /// <summary>
    /// Security Status - Windows Defender and security info (read-only)
    /// </summary>
    public class SecurityStatusMacro : AgentMacroDefinition
    {
        public override string Id => "security-status";
        public override string Title => "Security Status";
        public override string Description => "Windows Defender and security overview";
        public override string Icon => "🛡️";
        public override MacroRiskLevel Risk => MacroRiskLevel.SafeReadOnly;
        public override string[] Keywords => new[] { "security", "defender", "antivirus", "firewall", "protection", "virus" };

        public override async Task<MacroResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                var result = new MacroResult { Success = true };
                var cards = new List<MacroResultCard>();

                try
                {
                    // Windows Defender Status
                    var defenderCard = new MacroResultCard
                    {
                        Title = "Windows Defender",
                        Icon = "🛡️"
                    };

                    var defenderStatus = GetDefenderStatus();
                    defenderCard.StatusColor = defenderStatus.IsEnabled ? "green" : "red";

                    defenderCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Real-time Protection",
                        Value = defenderStatus.IsEnabled ? "Enabled" : "Disabled",
                        Icon = defenderStatus.IsEnabled ? "✓" : "✗",
                        ValueColor = defenderStatus.IsEnabled ? "green" : "red"
                    });

                    if (!string.IsNullOrEmpty(defenderStatus.LastScan))
                    {
                        defenderCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Last Scan",
                            Value = defenderStatus.LastScan,
                            Icon = "🔍"
                        });
                    }

                    if (!string.IsNullOrEmpty(defenderStatus.DefinitionVersion))
                    {
                        defenderCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Definitions",
                            Value = defenderStatus.DefinitionVersion,
                            Icon = "📋"
                        });
                    }

                    cards.Add(defenderCard);

                    // Firewall Status
                    var firewallCard = new MacroResultCard
                    {
                        Title = "Windows Firewall",
                        Icon = "🔥"
                    };

                    var firewallStatus = GetFirewallStatus();
                    firewallCard.StatusColor = firewallStatus.AllEnabled ? "green" : "yellow";

                    firewallCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Domain Profile",
                        Value = firewallStatus.DomainEnabled ? "Enabled" : "Disabled",
                        Icon = firewallStatus.DomainEnabled ? "✓" : "✗",
                        ValueColor = firewallStatus.DomainEnabled ? "green" : "red"
                    });

                    firewallCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Private Profile",
                        Value = firewallStatus.PrivateEnabled ? "Enabled" : "Disabled",
                        Icon = firewallStatus.PrivateEnabled ? "✓" : "✗",
                        ValueColor = firewallStatus.PrivateEnabled ? "green" : "red"
                    });

                    firewallCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Public Profile",
                        Value = firewallStatus.PublicEnabled ? "Enabled" : "Disabled",
                        Icon = firewallStatus.PublicEnabled ? "✓" : "✗",
                        ValueColor = firewallStatus.PublicEnabled ? "green" : "red"
                    });

                    cards.Add(firewallCard);

                    // UAC Status
                    var uacCard = new MacroResultCard
                    {
                        Title = "User Account Control",
                        Icon = "👤",
                        StatusColor = "cyan"
                    };

                    var uacEnabled = IsUacEnabled();
                    uacCard.Rows.Add(new MacroResultRow
                    {
                        Label = "UAC Status",
                        Value = uacEnabled ? "Enabled" : "Disabled",
                        Icon = uacEnabled ? "✓" : "⚠",
                        ValueColor = uacEnabled ? "green" : "yellow"
                    });

                    cards.Add(uacCard);

                    // Security Summary
                    var allGood = defenderStatus.IsEnabled && firewallStatus.AllEnabled && uacEnabled;
                    result.Summary = allGood 
                        ? "✓ All security features enabled" 
                        : "⚠ Some security features may need attention";

                    result.Cards = cards;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private DefenderStatus GetDefenderStatus()
        {
            var status = new DefenderStatus();

            try
            {
                // Try WMI query for Windows Defender
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Defender",
                    "SELECT * FROM MSFT_MpComputerStatus");

                foreach (var obj in searcher.Get())
                {
                    status.IsEnabled = Convert.ToBoolean(obj["RealTimeProtectionEnabled"]);
                    
                    var lastScan = obj["FullScanEndTime"];
                    if (lastScan != null)
                    {
                        var scanTime = ManagementDateTimeConverter.ToDateTime(lastScan.ToString());
                        status.LastScan = scanTime.ToString("MMM dd, yyyy HH:mm");
                    }

                    var defVersion = obj["AntivirusSignatureVersion"];
                    if (defVersion != null)
                        status.DefinitionVersion = defVersion.ToString();

                    break;
                }
            }
            catch
            {
                // Defender WMI not available, try alternative
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        @"root\SecurityCenter2",
                        "SELECT * FROM AntiVirusProduct");

                    foreach (var obj in searcher.Get())
                    {
                        var productState = Convert.ToUInt32(obj["productState"]);
                        // Bit 12 indicates if real-time protection is on
                        status.IsEnabled = ((productState >> 12) & 1) == 1;
                        break;
                    }
                }
                catch { }
            }

            return status;
        }

        private FirewallStatus GetFirewallStatus()
        {
            var status = new FirewallStatus();

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy");

                if (key != null)
                {
                    using var domainKey = key.OpenSubKey("DomainProfile");
                    using var privateKey = key.OpenSubKey("StandardProfile");
                    using var publicKey = key.OpenSubKey("PublicProfile");

                    status.DomainEnabled = GetFirewallEnabled(domainKey);
                    status.PrivateEnabled = GetFirewallEnabled(privateKey);
                    status.PublicEnabled = GetFirewallEnabled(publicKey);
                }
            }
            catch { }

            return status;
        }

        private bool GetFirewallEnabled(Microsoft.Win32.RegistryKey? key)
        {
            if (key == null) return false;
            var value = key.GetValue("EnableFirewall");
            return value != null && Convert.ToInt32(value) == 1;
        }

        private bool IsUacEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");

                if (key != null)
                {
                    var value = key.GetValue("EnableLUA");
                    return value != null && Convert.ToInt32(value) == 1;
                }
            }
            catch { }
            return true; // Assume enabled if can't read
        }

        private class DefenderStatus
        {
            public bool IsEnabled { get; set; }
            public string? LastScan { get; set; }
            public string? DefinitionVersion { get; set; }
        }

        private class FirewallStatus
        {
            public bool DomainEnabled { get; set; }
            public bool PrivateEnabled { get; set; }
            public bool PublicEnabled { get; set; }
            public bool AllEnabled => DomainEnabled && PrivateEnabled && PublicEnabled;
        }
    }
}
