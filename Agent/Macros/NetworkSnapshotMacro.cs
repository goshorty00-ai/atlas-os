using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AtlasAI.Agent.Macros
{
    /// <summary>
    /// Network Snapshot - IP, DNS, gateway, Wi-Fi info
    /// </summary>
    public class NetworkSnapshotMacro : AgentMacroDefinition
    {
        public override string Id => "network-snapshot";
        public override string Title => "Network Snapshot";
        public override string Description => "IP addresses, DNS, gateway, and connection info";
        public override string Icon => "🌐";
        public override MacroRiskLevel Risk => MacroRiskLevel.SafeReadOnly;
        public override string[] Keywords => new[] { "network", "ip", "dns", "gateway", "wifi", "internet", "connection", "lan" };

        public override async Task<MacroResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                var result = new MacroResult { Success = true };
                var cards = new List<MacroResultCard>();

                try
                {
                    // Connection Status Card
                    var statusCard = new MacroResultCard
                    {
                        Title = "Connection Status",
                        Icon = "📡",
                        StatusColor = NetworkInterface.GetIsNetworkAvailable() ? "green" : "red"
                    };

                    var isConnected = NetworkInterface.GetIsNetworkAvailable();
                    statusCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Internet",
                        Value = isConnected ? "Connected" : "Disconnected",
                        Icon = isConnected ? "✓" : "✗",
                        ValueColor = isConnected ? "green" : "red"
                    });

                    // Get active network interface
                    var activeInterface = GetActiveNetworkInterface();
                    if (activeInterface != null)
                    {
                        statusCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Interface",
                            Value = activeInterface.Name,
                            Icon = "🔌"
                        });

                        statusCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Type",
                            Value = activeInterface.NetworkInterfaceType.ToString(),
                            Icon = activeInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "📶" : "🔗"
                        });

                        statusCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Speed",
                            Value = FormatSpeed(activeInterface.Speed),
                            Icon = "⚡"
                        });
                    }

                    cards.Add(statusCard);

                    // IP Configuration Card
                    var ipCard = new MacroResultCard
                    {
                        Title = "IP Configuration",
                        Icon = "🔢",
                        StatusColor = "cyan"
                    };

                    // Local IP
                    var localIp = GetLocalIPAddress();
                    ipCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Local IP",
                        Value = localIp ?? "Unknown",
                        Icon = "📍"
                    });

                    // Gateway
                    var gateway = GetDefaultGateway();
                    ipCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Gateway",
                        Value = gateway ?? "Unknown",
                        Icon = "🚪"
                    });

                    // DNS Servers
                    var dnsServers = GetDnsServers();
                    ipCard.Rows.Add(new MacroResultRow
                    {
                        Label = "DNS",
                        Value = dnsServers.Any() ? string.Join(", ", dnsServers.Take(2)) : "Unknown",
                        Icon = "🔍"
                    });

                    // Subnet Mask
                    var subnet = GetSubnetMask();
                    if (!string.IsNullOrEmpty(subnet))
                    {
                        ipCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Subnet",
                            Value = subnet,
                            Icon = "🎭"
                        });
                    }

                    cards.Add(ipCard);

                    // Network Adapters Card
                    var adaptersCard = new MacroResultCard
                    {
                        Title = "Network Adapters",
                        Icon = "🔌",
                        StatusColor = "violet"
                    };

                    var adapters = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                   n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .Take(5);

                    foreach (var adapter in adapters)
                    {
                        var status = adapter.OperationalStatus == OperationalStatus.Up ? "✓" : "✗";
                        adaptersCard.Rows.Add(new MacroResultRow
                        {
                            Label = TruncateName(adapter.Name, 25),
                            Value = adapter.NetworkInterfaceType.ToString(),
                            Icon = status,
                            ValueColor = adapter.OperationalStatus == OperationalStatus.Up ? "green" : "red"
                        });
                    }

                    if (!adaptersCard.Rows.Any())
                        adaptersCard.Rows.Add(new MacroResultRow { Label = "No active adapters", Value = "", Icon = "○" });

                    cards.Add(adaptersCard);

                    result.Cards = cards;
                    result.Summary = $"IP: {localIp ?? "Unknown"} | Gateway: {gateway ?? "Unknown"}";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private NetworkInterface? GetActiveNetworkInterface()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                    n.GetIPProperties().GatewayAddresses.Any());
        }

        private string? GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                return host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?
                    .ToString();
            }
            catch { return null; }
        }

        private string? GetDefaultGateway()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?
                    .Address.ToString();
            }
            catch { return null; }
        }

        private List<string> GetDnsServers()
        {
            var servers = new List<string>();
            try
            {
                var activeInterface = GetActiveNetworkInterface();
                if (activeInterface != null)
                {
                    servers = activeInterface.GetIPProperties().DnsAddresses
                        .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                        .Select(d => d.ToString())
                        .ToList();
                }
            }
            catch { }
            return servers;
        }

        private string? GetSubnetMask()
        {
            try
            {
                var activeInterface = GetActiveNetworkInterface();
                if (activeInterface != null)
                {
                    var unicast = activeInterface.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                    return unicast?.IPv4Mask?.ToString();
                }
            }
            catch { }
            return null;
        }

        private string FormatSpeed(long bitsPerSecond)
        {
            if (bitsPerSecond >= 1_000_000_000)
                return $"{bitsPerSecond / 1_000_000_000.0:F1} Gbps";
            if (bitsPerSecond >= 1_000_000)
                return $"{bitsPerSecond / 1_000_000.0:F0} Mbps";
            return $"{bitsPerSecond / 1_000.0:F0} Kbps";
        }

        private string TruncateName(string name, int maxLen)
        {
            if (name.Length <= maxLen) return name;
            return name.Substring(0, maxLen - 3) + "...";
        }
    }
}
