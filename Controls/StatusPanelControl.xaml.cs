using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AtlasAI.Controls
{
    public partial class StatusPanelControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        private DispatcherTimer? _updateTimer;
        private DateTime _startTime;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        
        // Observable collection for active tools
        public ObservableCollection<ActiveToolItem> ActiveTools { get; } = new();
        
        public StatusPanelControl()
        {
            InitializeComponent();
            DataContext = this;
            
            _startTime = DateTime.Now;
            ActiveToolsList.ItemsSource = ActiveTools;
            
            // Initialize performance counters
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                _cpuCounter.NextValue(); // First call returns 0, need to prime it
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusPanel] Could not initialize performance counters: {ex.Message}");
            }
            
            // Clear sample tools - will be populated by actual running tasks
            ActiveTools.Clear();
            
            Loaded += StatusPanelControl_Loaded;
            Unloaded += StatusPanelControl_Unloaded;
        }
        
        private void StatusPanelControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Start update timer for dynamic values - increased to 5 seconds to reduce judder
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();
            
            UpdateValues();
        }
        
        private void StatusPanelControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _updateTimer?.Stop();
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
        }
        
        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateValues();
        }
        
        private void UpdateValues()
        {
            try
            {
                // Update uptime (app session time)
                var uptime = DateTime.Now - _startTime;
                UptimeValue.Text = $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
                
                // Get REAL CPU usage
                float cpuUsage = 0;
                try
                {
                    cpuUsage = _cpuCounter?.NextValue() ?? 0;
                }
                catch { cpuUsage = 0; }
                
                ProcessingBar.Value = cpuUsage;
                ProcessingValue.Text = $"{(int)cpuUsage}%";
                
                // Get REAL RAM usage
                float ramUsage = 0;
                try
                {
                    ramUsage = _ramCounter?.NextValue() ?? 0;
                }
                catch { ramUsage = 0; }
                
                CapacityBar.Value = ramUsage;
                CapacityValue.Text = $"{(int)ramUsage}%";
                
                // Get REAL network latency (ping to Google DNS)
                try
                {
                    using var ping = new Ping();
                    var reply = ping.Send("8.8.8.8", 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        var latency = reply.RoundtripTime;
                        LatencyValue.Text = $"{latency}ms";
                        LatencyValue.Foreground = latency < 50 
                            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
                            : latency < 100
                                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 115, 22))
                                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    }
                    else
                    {
                        LatencyValue.Text = "N/A";
                        LatencyValue.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    }
                }
                catch
                {
                    LatencyValue.Text = "N/A";
                }
                
                // Estimate bandwidth (just show connection status for now)
                BandwidthValue.Text = NetworkInterface.GetIsNetworkAvailable() ? "Connected" : "Offline";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusPanel] Update error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update the AI model name displayed
        /// </summary>
        public void SetModel(string modelName)
        {
            ModelValue.Text = modelName;
        }
        
        /// <summary>
        /// Update the AI state
        /// </summary>
        public void SetState(string state, bool isActive = true)
        {
            StateValue.Text = state.ToUpper();
            StateValue.Foreground = isActive 
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 115, 22));
        }
        
        /// <summary>
        /// Add an active tool to the list
        /// </summary>
        public void AddActiveTool(string name, int progress = 0)
        {
            Dispatcher.Invoke(() =>
            {
                ActiveTools.Add(new ActiveToolItem { Name = name, Progress = progress });
            });
        }
        
        /// <summary>
        /// Remove an active tool
        /// </summary>
        public void RemoveActiveTool(string name)
        {
            Dispatcher.Invoke(() =>
            {
                var tool = ActiveTools.FirstOrDefault(t => t.Name == name);
                if (tool != null)
                    ActiveTools.Remove(tool);
            });
        }
        
        /// <summary>
        /// Update tool progress
        /// </summary>
        public void UpdateToolProgress(string name, int progress)
        {
            var tool = ActiveTools.FirstOrDefault(t => t.Name == name);
            if (tool != null)
                tool.Progress = progress;
        }
        
        private void QuickAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string action)
            {
                QuickActionRequested?.Invoke(this, action);
            }
        }
        
        /// <summary>
        /// Event fired when a quick action button is clicked
        /// </summary>
        public event EventHandler<string>? QuickActionRequested;
        
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// Model for active tool items
    /// </summary>
    public class ActiveToolItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        private string _name = "";
        private int _progress;
        
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }
        
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }
        
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
