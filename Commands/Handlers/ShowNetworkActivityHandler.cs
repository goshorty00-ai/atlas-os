using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    public class ShowNetworkActivityHandler : ICommandHandler
    {
        public string CommandName => "show_network_activity";

        public string GetDescription() => "Display active network connections and statistics";

        public bool CanExecute(CommandContext context) => true;

        public async Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            return await Task.Run(() =>
            {
                var results = new Dictionary<string, object>();

                try
                {
                    var properties = IPGlobalProperties.GetIPGlobalProperties();
                    var tcpConnections = properties.GetActiveTcpConnections();
                    var tcpListeners = properties.GetActiveTcpListeners();
                    var udpListeners = properties.GetActiveUdpListeners();

                    results["total_tcp_connections"] = tcpConnections.Length;
                    results["tcp_listeners"] = tcpListeners.Length;
                    results["udp_listeners"] = udpListeners.Length;

                    // Group by state
                    var byState = tcpConnections
                        .GroupBy(c => c.State)
                        .ToDictionary(g => g.Key.ToString(), g => g.Count());
                    results["connections_by_state"] = byState;

                    // Active connections
                    var activeConnections = tcpConnections
                        .Where(c => c.State == TcpState.Established)
                        .Select(c => new Dictionary<string, object>
                        {
                            ["local"] = $"{c.LocalEndPoint.Address}:{c.LocalEndPoint.Port}",
                            ["remote"] = $"{c.RemoteEndPoint.Address}:{c.RemoteEndPoint.Port}",
                            ["state"] = c.State.ToString()
                        })
                        .Take(20)
                        .ToList();

                    if (activeConnections.Any())
                        results["active_connections"] = activeConnections;

                    // Network interfaces
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                        .Select(ni => new Dictionary<string, object>
                        {
                            ["name"] = ni.Name,
                            ["type"] = ni.NetworkInterfaceType.ToString(),
                            ["speed_mbps"] = ni.Speed / 1_000_000,
                            ["bytes_sent"] = ni.GetIPv4Statistics().BytesSent,
                            ["bytes_received"] = ni.GetIPv4Statistics().BytesReceived
                        })
                        .ToList();

                    results["network_interfaces"] = interfaces;

                    var message = $"Network activity: {tcpConnections.Length} TCP connections, {tcpListeners.Length} TCP listeners, {udpListeners.Length} UDP listeners";

                    return CommandResult.Success(CommandName, message, results);
                }
                catch (Exception ex)
                {
                    return CommandResult.Error(CommandName, $"Failed to retrieve network activity: {ex.Message}");
                }
            });
        }
    }
}
