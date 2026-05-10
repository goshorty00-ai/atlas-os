using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.ITManagement
{
    /// <summary>
    /// Discovers devices on the local network
    /// </summary>
    public class NetworkDiscovery
    {
        private readonly List<NetworkDevice> _discoveredDevices = new();
        private CancellationTokenSource? _scanCts;
        
        public event Action<NetworkDevice>? OnDeviceDiscovered;
        public event Action<int, int>? OnScanProgress; // current, total
        public event Action<List<NetworkDevice>>? OnScanComplete;
        
        public IReadOnlyList<NetworkDevice> DiscoveredDevices => _discoveredDevices;
        public bool IsScanning { get; private set; }
        
        /// <summary>
        /// Get the local network information
        /// </summary>
        public NetworkInfo GetLocalNetworkInfo()
        {
            var info = new NetworkInfo();
            
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                
                var ipProps = adapter.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                
                if (ipv4 != null)
                {
                    info.LocalIP = ipv4.Address.ToString();
                    info.SubnetMask = ipv4.IPv4Mask?.ToString() ?? "255.255.255.0";
                    info.AdapterName = adapter.Name;
                    info.MacAddress = adapter.GetPhysicalAddress().ToString();
                    
                    var gateway = ipProps.GatewayAddresses.FirstOrDefault();
                    if (gateway != null)
                        info.Gateway = gateway.Address.ToString();
                    
                    var dns = ipProps.DnsAddresses.FirstOrDefault();
                    if (dns != null)
                        info.DnsServer = dns.ToString();
                    
                    break;
                }
            }
            
            return info;
        }
        
        /// <summary>
        /// Scan the local network for devices
        /// </summary>
        public async Task<List<NetworkDevice>> ScanNetworkAsync(CancellationToken ct = default)
        {
            if (IsScanning)
            {
                Debug.WriteLine("[NetworkDiscovery] Scan already in progress");
                return _discoveredDevices.ToList();
            }
            
            IsScanning = true;
            _discoveredDevices.Clear();
            _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            
            try
            {
                var networkInfo = GetLocalNetworkInfo();
                if (string.IsNullOrEmpty(networkInfo.LocalIP))
                {
                    Debug.WriteLine("[NetworkDiscovery] No network connection found");
                    return new List<NetworkDevice>();
                }
                
                // Get network range
                var baseIp = GetBaseIP(networkInfo.LocalIP);
                var ipsToScan = Enumerable.Range(1, 254).Select(i => $"{baseIp}.{i}").ToList();
                
                Debug.WriteLine($"[NetworkDiscovery] Scanning {ipsToScan.Count} addresses on {baseIp}.x");
                
                int completed = 0;
                var tasks = new List<Task>();
                var semaphore = new SemaphoreSlim(50); // Limit concurrent pings
                
                foreach (var ip in ipsToScan)
                {
                    if (_scanCts.Token.IsCancellationRequested) break;
                    
                    await semaphore.WaitAsync(_scanCts.Token);
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var device = await ProbeDeviceAsync(ip, _scanCts.Token);
                            if (device != null)
                            {
                                lock (_discoveredDevices)
                                {
                                    _discoveredDevices.Add(device);
                                }
                                OnDeviceDiscovered?.Invoke(device);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                            Interlocked.Increment(ref completed);
                            OnScanProgress?.Invoke(completed, ipsToScan.Count);
                        }
                    }, _scanCts.Token));
                }
                
                await Task.WhenAll(tasks);
                
                // Sort by IP
                var sorted = _discoveredDevices.OrderBy(d => 
                    IPAddress.Parse(d.IPAddress).GetAddressBytes().Aggregate(0L, (acc, b) => acc * 256 + b)).ToList();
                
                _discoveredDevices.Clear();
                _discoveredDevices.AddRange(sorted);
                
                OnScanComplete?.Invoke(_discoveredDevices.ToList());
                Debug.WriteLine($"[NetworkDiscovery] Scan complete. Found {_discoveredDevices.Count} devices");
                
                return _discoveredDevices.ToList();
            }
            finally
            {
                IsScanning = false;
            }
        }
        
        public void CancelScan()
        {
            _scanCts?.Cancel();
        }
        
        private async Task<NetworkDevice?> ProbeDeviceAsync(string ip, CancellationToken ct)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 500);
                
                if (reply.Status != IPStatus.Success)
                    return null;
                
                var device = new NetworkDevice
                {
                    IPAddress = ip,
                    ResponseTime = (int)reply.RoundtripTime,
                    IsOnline = true,
                    LastSeen = DateTime.Now
                };
                
                // Try to get hostname
                try
                {
                    var hostEntry = await Dns.GetHostEntryAsync(ip);
                    device.Hostname = hostEntry.HostName;
                }
                catch { device.Hostname = "Unknown"; }
                
                // Try to get MAC address via ARP
                device.MacAddress = GetMacAddress(ip);
                
                // Identify device type based on open ports
                device.DeviceType = await IdentifyDeviceTypeAsync(ip, ct);
                
                // Check common ports
                device.OpenPorts = await ScanCommonPortsAsync(ip, ct);
                
                return device;
            }
            catch
            {
                return null;
            }
        }
        
        private string GetMacAddress(string ip)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = $"-a {ip}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return "";
                
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                // Parse ARP output for MAC address
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains(ip))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var mac = parts.FirstOrDefault(p => p.Contains('-') && p.Length >= 17);
                            if (mac != null) return mac.ToUpper();
                        }
                    }
                }
            }
            catch { }
            return "";
        }
        
        private async Task<DeviceType> IdentifyDeviceTypeAsync(string ip, CancellationToken ct)
        {
            var openPorts = new List<int>();
            
            // Quick port check for device identification
            int[] identifyPorts = { 80, 443, 22, 3389, 445, 554, 631, 9100, 9999, 5000, 8080 };
            
            foreach (var port in identifyPorts)
            {
                if (await IsPortOpenAsync(ip, port, 200, ct))
                    openPorts.Add(port);
            }
            
            // Identify based on open ports
            if (openPorts.Contains(9100) || openPorts.Contains(631))
                return DeviceType.Printer;
            if (openPorts.Contains(3389))
                return DeviceType.WindowsPC;
            if (openPorts.Contains(22) && !openPorts.Contains(3389))
                return DeviceType.LinuxServer;
            if (openPorts.Contains(445))
                return DeviceType.WindowsPC;
            if (openPorts.Contains(554))
                return DeviceType.IoTDevice;
            if (openPorts.Contains(9999))
                return DeviceType.IoTDevice;
            if (openPorts.Contains(80) || openPorts.Contains(443))
                return DeviceType.NetworkDevice;
            
            return DeviceType.Unknown;
        }
        
        private async Task<List<int>> ScanCommonPortsAsync(string ip, CancellationToken ct)
        {
            var openPorts = new List<int>();
            int[] commonPorts = { 21, 22, 23, 25, 53, 80, 110, 143, 443, 445, 554, 993, 995, 3306, 3389, 5432, 8080, 9999 };
            
            var tasks = commonPorts.Select(async port =>
            {
                if (await IsPortOpenAsync(ip, port, 300, ct))
                {
                    lock (openPorts) openPorts.Add(port);
                }
            });
            
            await Task.WhenAll(tasks);
            return openPorts.OrderBy(p => p).ToList();
        }
        
        private async Task<bool> IsPortOpenAsync(string ip, int port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(timeoutMs, ct);
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                return completedTask == connectTask && client.Connected;
            }
            catch
            {
                return false;
            }
        }
        
        private string GetBaseIP(string ip)
        {
            var parts = ip.Split('.');
            return $"{parts[0]}.{parts[1]}.{parts[2]}";
        }
        
        /// <summary>
        /// Get a summary of discovered devices
        /// </summary>
        public string GetDiscoverySummary()
        {
            if (_discoveredDevices.Count == 0)
                return "No devices discovered. Run a network scan first.";
            
            var byType = _discoveredDevices.GroupBy(d => d.DeviceType)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            
            return $"""
                Network Scan Results:
                Found {_discoveredDevices.Count} devices
                
                By Type: {string.Join(", ", byType)}
                
                Devices:
                {string.Join("\n", _discoveredDevices.Select(d => $"  • {d.IPAddress} - {d.Hostname} ({d.DeviceType})"))}
                """;
        }
    }

    
    #region Data Models
    
    public class NetworkInfo
    {
        public string LocalIP { get; set; } = "";
        public string SubnetMask { get; set; } = "";
        public string Gateway { get; set; } = "";
        public string DnsServer { get; set; } = "";
        public string AdapterName { get; set; } = "";
        public string MacAddress { get; set; } = "";
    }
    
    public class NetworkDevice
    {
        public string IPAddress { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public DeviceType DeviceType { get; set; }
        public bool IsOnline { get; set; }
        public int ResponseTime { get; set; }
        public DateTime LastSeen { get; set; }
        public List<int> OpenPorts { get; set; } = new();
        public string Vendor { get; set; } = "";
        
        public string GetPortServices()
        {
            var services = new Dictionary<int, string>
            {
                { 21, "FTP" }, { 22, "SSH" }, { 23, "Telnet" }, { 25, "SMTP" },
                { 53, "DNS" }, { 80, "HTTP" }, { 110, "POP3" }, { 143, "IMAP" },
                { 443, "HTTPS" }, { 445, "SMB" }, { 3306, "MySQL" }, { 3389, "RDP" },
                { 5432, "PostgreSQL" }, { 8080, "HTTP-Alt" }, { 9100, "Printer" }
            };
            
            return string.Join(", ", OpenPorts.Select(p => 
                services.TryGetValue(p, out var name) ? name : p.ToString()));
        }
    }
    
    public enum DeviceType
    {
        Unknown,
        WindowsPC,
        MacOS,
        LinuxServer,
        Router,
        Printer,
        NetworkDevice,
        IoTDevice,
        MobileDevice,
        SmartTV,
        GameConsole
    }
    
    #endregion
}
