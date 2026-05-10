using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Network Tools - Ping, DNS lookup, port check, network info.
    /// </summary>
    public static class NetworkTools
    {
        /// <summary>
        /// Handle network commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Ping
            if (lower.StartsWith("ping "))
            {
                var host = input.Substring(5).Trim();
                return await PingHostAsync(host);
            }
            
            // DNS lookup
            if (lower.StartsWith("dns ") || lower.StartsWith("lookup ") || lower.StartsWith("resolve "))
            {
                var host = input.Substring(input.IndexOf(' ') + 1).Trim();
                return await DnsLookupAsync(host);
            }
            
            // Port check
            if (lower.Contains("port") && (lower.Contains("check") || lower.Contains("open") || lower.Contains("scan")))
            {
                var match = System.Text.RegularExpressions.Regex.Match(input, @"(\S+)\s+(?:port\s+)?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var host = match.Groups[1].Value;
                    var port = int.Parse(match.Groups[2].Value);
                    return await CheckPortAsync(host, port);
                }
                return "Usage: `check port hostname 80` or `is port 443 open on google.com`";
            }
            
            // Network info
            if (lower == "network info" || lower == "network status" || lower == "connection info")
            {
                return GetNetworkInfo();
            }
            
            // Speed test (simple latency test)
            if (lower.Contains("speed test") || lower.Contains("latency test") || lower.Contains("connection test"))
            {
                return await SimpleLatencyTestAsync();
            }
            
            // Flush DNS
            if (lower.Contains("flush dns") || lower.Contains("clear dns"))
            {
                return await FlushDnsAsync();
            }
            
            return null;
        }
        
        private static async Task<string> PingHostAsync(string host)
        {
            try
            {
                using var ping = new Ping();
                var results = new long[4];
                var success = 0;
                
                for (int i = 0; i < 4; i++)
                {
                    var reply = await ping.SendPingAsync(host, 3000);
                    if (reply.Status == IPStatus.Success)
                    {
                        results[i] = reply.RoundtripTime;
                        success++;
                    }
                    else
                    {
                        results[i] = -1;
                    }
                    await Task.Delay(100);
                }
                
                if (success == 0)
                    return $"❌ **Ping failed:** {host} is unreachable";
                
                var validResults = results.Where(r => r >= 0).ToArray();
                var avg = validResults.Average();
                var min = validResults.Min();
                var max = validResults.Max();
                
                return $"🏓 **Ping {host}:**\n\n" +
                       $"Packets: {success}/4 received\n" +
                       $"Min: {min}ms\n" +
                       $"Max: {max}ms\n" +
                       $"Avg: {avg:F1}ms";
            }
            catch (Exception ex)
            {
                return $"❌ **Ping error:** {ex.Message}";
            }
        }
        
        private static async Task<string> DnsLookupAsync(string host)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                
                var sb = new StringBuilder();
                sb.AppendLine($"🔍 **DNS Lookup: {host}**\n");
                
                var ipv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
                var ipv6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
                
                if (ipv4.Any())
                {
                    sb.AppendLine("**IPv4:**");
                    foreach (var ip in ipv4)
                        sb.AppendLine($"  • {ip}");
                }
                
                if (ipv6.Any())
                {
                    sb.AppendLine("\n**IPv6:**");
                    foreach (var ip in ipv6.Take(3))
                        sb.AppendLine($"  • {ip}");
                }
                
                // Copy first IP to clipboard
                if (addresses.Any())
                {
                    Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(addresses[0].ToString()));
                    sb.AppendLine("\n✓ First IP copied to clipboard!");
                }
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ **DNS lookup failed:** {ex.Message}";
            }
        }
        
        private static async Task<string> CheckPortAsync(string host, int port)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(5000);
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                    return $"⏱️ **Port {port} on {host}:** Timeout (possibly filtered)";
                
                if (client.Connected)
                    return $"✅ **Port {port} on {host}:** Open";
                
                return $"❌ **Port {port} on {host}:** Closed";
            }
            catch (SocketException)
            {
                return $"❌ **Port {port} on {host}:** Closed or refused";
            }
            catch (Exception ex)
            {
                return $"❌ **Error:** {ex.Message}";
            }
        }
        
        private static string GetNetworkInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("🌐 **Network Information:**\n");
            
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                               n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToArray();
                
                foreach (var ni in interfaces.Take(3))
                {
                    sb.AppendLine($"**{ni.Name}** ({ni.NetworkInterfaceType})");
                    
                    var props = ni.GetIPProperties();
                    var ipv4 = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                    
                    if (ipv4 != null)
                        sb.AppendLine($"  IP: {ipv4.Address}");
                    
                    var gateway = props.GatewayAddresses.FirstOrDefault();
                    if (gateway != null)
                        sb.AppendLine($"  Gateway: {gateway.Address}");
                    
                    var dns = props.DnsAddresses.FirstOrDefault();
                    if (dns != null)
                        sb.AppendLine($"  DNS: {dns}");
                    
                    sb.AppendLine($"  Speed: {ni.Speed / 1_000_000} Mbps");
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error: {ex.Message}");
            }
            
            return sb.ToString();
        }
        
        private static async Task<string> SimpleLatencyTestAsync()
        {
            var targets = new[] { "google.com", "cloudflare.com", "microsoft.com" };
            var sb = new StringBuilder();
            sb.AppendLine("🚀 **Connection Test:**\n");
            
            using var ping = new Ping();
            
            foreach (var target in targets)
            {
                try
                {
                    var reply = await ping.SendPingAsync(target, 3000);
                    if (reply.Status == IPStatus.Success)
                        sb.AppendLine($"✅ {target}: {reply.RoundtripTime}ms");
                    else
                        sb.AppendLine($"❌ {target}: Failed");
                }
                catch
                {
                    sb.AppendLine($"❌ {target}: Error");
                }
            }
            
            return sb.ToString();
        }
        
        private static async Task<string> FlushDnsAsync()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return "✅ DNS cache flushed successfully!";
                }
                return "❌ Failed to flush DNS";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
    }
}
