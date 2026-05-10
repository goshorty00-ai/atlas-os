using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using AtlasAI.Core;

namespace AtlasAI.UI
{
    public partial class ProcessManagerWindow : Window
    {
        private List<ProcessInfo> _allProcesses = new();
        private List<StartupInfo> _startupItems = new();
        private List<TaskInfo> _scheduledTasks = new();

        public ProcessManagerWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) => await RefreshAllAsync();
        }

        private async void RefreshAll_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

        private async Task RefreshAllAsync()
        {
            StatusText.Text = "Loading...";
            await Task.WhenAll(
                LoadProcessesAsync(),
                LoadStartupItemsAsync(),
                LoadScheduledTasksAsync()
            );
            UpdateDisplay();
            StatusText.Text = $"{_allProcesses.Count} processes, {_startupItems.Count} startup items, {_scheduledTasks.Count} tasks";
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            // Uncheck all tabs first
            TabProcesses.IsChecked = false;
            TabStartup.IsChecked = false;
            TabTasks.IsChecked = false;
            
            // Check the clicked tab
            if (sender is ToggleButton btn)
                btn.IsChecked = true;
            
            ProcessesPanel.Visibility = TabProcesses.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            StartupPanel.Visibility = TabStartup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            TasksPanel.Visibility = TabTasks.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            
            BtnKill.Visibility = TabProcesses.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            BtnDisable.Visibility = TabProcesses.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
            BtnEnable.Visibility = TabProcesses.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ProcessSearch_TextChanged(object sender, TextChangedEventArgs e) => UpdateDisplay();
        private void ShowAllProcesses_Changed(object sender, RoutedEventArgs e) => UpdateDisplay();

        private void UpdateDisplay()
        {
            var search = ProcessSearch.Text?.ToLower() ?? "";
            var showAll = ShowAllProcesses.IsChecked == true;
            
            var filtered = _allProcesses
                .Where(p => showAll || !string.IsNullOrEmpty(p.WindowTitle))
                .Where(p => string.IsNullOrEmpty(search) || 
                           p.Name.ToLower().Contains(search) || 
                           p.WindowTitle?.ToLower().Contains(search) == true)
                .OrderByDescending(p => p.MemoryBytes)
                .ToList();
            
            ProcessGrid.ItemsSource = filtered;
            StartupGrid.ItemsSource = _startupItems;
            TasksGrid.ItemsSource = _scheduledTasks;
        }

        #region Load Data

        private async Task LoadProcessesAsync()
        {
            _allProcesses = await Task.Run(() =>
            {
                var list = new List<ProcessInfo>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        list.Add(new ProcessInfo
                        {
                            Name = p.ProcessName,
                            PID = p.Id,
                            MemoryBytes = p.WorkingSet64,
                            Memory = FormatSize(p.WorkingSet64),
                            CPU = "—",
                            WindowTitle = p.MainWindowTitle,
                            Path = TryGetPath(p)
                        });
                    }
                    catch { }
                }
                return list;
            });
        }

        private string TryGetPath(Process p)
        {
            try { return p.MainModule?.FileName ?? ""; }
            catch { return ""; }
        }

        private async Task LoadStartupItemsAsync()
        {
            _startupItems = await Task.Run(() =>
            {
                var list = new List<StartupInfo>();
                
                // Registry Run keys
                var keys = new[] {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
                };
                
                foreach (var keyPath in keys)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                        if (key != null)
                        {
                            foreach (var name in key.GetValueNames())
                            {
                                list.Add(new StartupInfo
                                {
                                    Name = name,
                                    Command = key.GetValue(name)?.ToString() ?? "",
                                    Location = "HKLM\\" + keyPath,
                                    Status = "Enabled",
                                    RegistryKey = "HKLM\\" + keyPath,
                                    ValueName = name
                                });
                            }
                        }
                    }
                    catch { }
                    
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
                        if (key != null)
                        {
                            foreach (var name in key.GetValueNames())
                            {
                                list.Add(new StartupInfo
                                {
                                    Name = name,
                                    Command = key.GetValue(name)?.ToString() ?? "",
                                    Location = "HKCU\\" + keyPath,
                                    Status = "Enabled",
                                    RegistryKey = "HKCU\\" + keyPath,
                                    ValueName = name
                                });
                            }
                        }
                    }
                    catch { }
                }
                
                // Startup folder
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    foreach (var file in Directory.GetFiles(startupFolder))
                    {
                        list.Add(new StartupInfo
                        {
                            Name = Path.GetFileNameWithoutExtension(file),
                            Command = file,
                            Location = "Startup Folder",
                            Status = "Enabled",
                            FilePath = file
                        });
                    }
                }
                
                return list;
            });
        }

        private async Task LoadScheduledTasksAsync()
        {
            _scheduledTasks = await Task.Run(() =>
            {
                var list = new List<TaskInfo>();
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/query /fo CSV /v",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var proc = Process.Start(psi);
                    if (proc == null) return list;
                    
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(10000);
                    
                    var lines = output.Split('\n').Skip(1);
                    foreach (var line in lines)
                    {
                        var parts = ParseCsvLine(line);
                        if (parts.Length >= 6)
                        {
                            list.Add(new TaskInfo
                            {
                                Name = parts[1].Trim('"'),
                                State = parts[3].Trim('"'),
                                NextRun = parts[2].Trim('"'),
                                LastRun = parts[5].Trim('"'),
                                FullName = parts[1].Trim('"')
                            });
                        }
                    }
                }
                catch { }
                return list.Where(t => !string.IsNullOrEmpty(t.Name)).Take(100).ToList();
            });
        }

        private string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = "";
            var inQuotes = false;
            
            foreach (var c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(current); current = ""; }
                else current += c;
            }
            result.Add(current);
            return result.ToArray();
        }

        #endregion

        #region Actions

        private async void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProcessGrid.SelectedItems.Cast<ProcessInfo>().ToList();
            if (!selected.Any())
            {
                MessageBox.Show("Select process(es) to kill", "No Selection", MessageBoxButton.OK);
                return;
            }

            var names = string.Join(", ", selected.Select(p => p.Name).Distinct());
            if (MessageBox.Show($"Kill {selected.Count} process(es)?\n\n{names}", "Confirm Kill", 
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            int killed = 0;
            int blocked = 0;
            foreach (var proc in selected)
            {
                try
                {
                    // SAFETY GATE: Check before process kill
                    var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                        OperationType.ProcessKillCritical,
                        OperationRisk.High,
                        $"Kill process: {proc.Name}",
                        new Dictionary<string, object>
                        {
                            ["processName"] = proc.Name,
                            ["pid"] = proc.PID
                        });
                    
                    if (safetyCheck.Decision == SafetyDecision.Blocked)
                    {
                        blocked++;
                        continue;
                    }
                    
                    var p = Process.GetProcessById(proc.PID);
                    p.Kill();
                    killed++;
                }
                catch { }
            }

            StatusText.Text = blocked > 0 
                ? $"Killed {killed} process(es), {blocked} blocked by Safety Mode"
                : $"Killed {killed} process(es)";
            await Task.Delay(500);
            await LoadProcessesAsync();
            UpdateDisplay();
        }

        private async void DisableItem_Click(object sender, RoutedEventArgs e)
        {
            if (TabStartup.IsChecked == true)
            {
                var selected = StartupGrid.SelectedItem as StartupInfo;
                if (selected == null) { StatusText.Text = "Select a startup item first"; return; }
                
                try
                {
                    if (!string.IsNullOrEmpty(selected.FilePath))
                    {
                        // SAFETY GATE: Check before startup file move
                        var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                            OperationType.StartupEntryChange,
                            OperationRisk.Medium,
                            $"Disable startup item: {selected.Name}",
                            new Dictionary<string, object>
                            {
                                ["entryName"] = selected.Name,
                                ["path"] = selected.FilePath,
                                ["action"] = "disable"
                            });
                        
                        if (safetyCheck.Decision == SafetyDecision.Blocked)
                        {
                            StatusText.Text = safetyCheck.Message;
                            return;
                        }
                        
                        // Move to disabled folder
                        var disabledFolder = Path.Combine(Path.GetDirectoryName(selected.FilePath)!, "_Disabled");
                        Directory.CreateDirectory(disabledFolder);
                        var dest = Path.Combine(disabledFolder, Path.GetFileName(selected.FilePath));
                        File.Move(selected.FilePath, dest);
                        StatusText.Text = $"✓ Disabled: {selected.Name}";
                    }
                    else if (!string.IsNullOrEmpty(selected.RegistryKey))
                    {
                        var success = await DisableRegistryStartupAsync(selected);
                        StatusText.Text = success ? $"✓ Disabled: {selected.Name}" : $"❌ Failed - may need admin rights or blocked by Safety Mode";
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"❌ Error: {ex.Message}";
                }
                
                await LoadStartupItemsAsync();
                UpdateDisplay();
            }
            else if (TabTasks.IsChecked == true)
            {
                var selected = TasksGrid.SelectedItem as TaskInfo;
                if (selected == null) { StatusText.Text = "Select a task first"; return; }
                
                // SAFETY GATE: Check before scheduled task change
                var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                    OperationType.ScheduledTaskChange,
                    OperationRisk.Medium,
                    $"Disable scheduled task: {selected.Name}",
                    new Dictionary<string, object>
                    {
                        ["taskName"] = selected.FullName,
                        ["action"] = "disable"
                    });
                
                if (safetyCheck.Decision == SafetyDecision.Blocked)
                {
                    StatusText.Text = safetyCheck.Message;
                    return;
                }
                
                // Run with admin privileges
                var result = await RunElevatedCommandAsync($"schtasks /change /tn \"{selected.FullName}\" /disable");
                StatusText.Text = result.Contains("SUCCESS") ? $"✓ Disabled: {selected.Name}" : $"❌ {result}";
                
                await LoadScheduledTasksAsync();
                UpdateDisplay();
            }
        }

        private async void EnableItem_Click(object sender, RoutedEventArgs e)
        {
            if (TabStartup.IsChecked == true)
            {
                var selected = StartupGrid.SelectedItem as StartupInfo;
                if (selected == null) { StatusText.Text = "Select a startup item first"; return; }
                
                // Check for disabled folder items
                var disabledFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "_Disabled");
                var disabledFile = Path.Combine(disabledFolder, selected.Name + ".lnk");
                if (File.Exists(disabledFile))
                {
                    // SAFETY GATE: Check before startup file restore
                    var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                        OperationType.StartupEntryChange,
                        OperationRisk.Medium,
                        $"Enable startup item: {selected.Name}",
                        new Dictionary<string, object>
                        {
                            ["entryName"] = selected.Name,
                            ["path"] = disabledFile,
                            ["action"] = "enable"
                        });
                    
                    if (safetyCheck.Decision == SafetyDecision.Blocked)
                    {
                        StatusText.Text = safetyCheck.Message;
                        return;
                    }
                    
                    var dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), selected.Name + ".lnk");
                    File.Move(disabledFile, dest);
                    StatusText.Text = $"✓ Enabled: {selected.Name}";
                    await LoadStartupItemsAsync();
                    UpdateDisplay();
                }
                else
                {
                    StatusText.Text = "❌ Can't re-enable registry items (delete was permanent)";
                }
            }
            else if (TabTasks.IsChecked == true)
            {
                var selected = TasksGrid.SelectedItem as TaskInfo;
                if (selected == null) { StatusText.Text = "Select a task first"; return; }
                
                // SAFETY GATE: Check before scheduled task change
                var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                    OperationType.ScheduledTaskChange,
                    OperationRisk.Medium,
                    $"Enable scheduled task: {selected.Name}",
                    new Dictionary<string, object>
                    {
                        ["taskName"] = selected.FullName,
                        ["action"] = "enable"
                    });
                
                if (safetyCheck.Decision == SafetyDecision.Blocked)
                {
                    StatusText.Text = safetyCheck.Message;
                    return;
                }
                
                var result = await RunElevatedCommandAsync($"schtasks /change /tn \"{selected.FullName}\" /enable");
                StatusText.Text = result.Contains("SUCCESS") ? $"✓ Enabled: {selected.Name}" : $"❌ {result}";
                
                await LoadScheduledTasksAsync();
                UpdateDisplay();
            }
        }

        private async Task<bool> DisableRegistryStartupAsync(StartupInfo item)
        {
            // SAFETY GATE: Check before registry delete
            var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                OperationType.StartupEntryChange,
                OperationRisk.High,
                $"Remove startup registry entry: {item.Name}",
                new Dictionary<string, object>
                {
                    ["entryName"] = item.Name ?? "",
                    ["registryPath"] = item.RegistryKey ?? "",
                    ["action"] = "remove"
                });
            
            if (safetyCheck.Decision == SafetyDecision.Blocked)
            {
                return false;
            }
            
            return await Task.Run(() =>
            {
                try
                {
                    var isHKLM = item.RegistryKey?.StartsWith("HKLM") == true;
                    var keyPath = item.RegistryKey?.Replace("HKLM\\", "").Replace("HKCU\\", "") ?? "";
                    
                    using var key = isHKLM 
                        ? Registry.LocalMachine.OpenSubKey(keyPath, true)
                        : Registry.CurrentUser.OpenSubKey(keyPath, true);
                    
                    if (key != null)
                    {
                        key.DeleteValue(item.ValueName ?? "", false);
                        return true;
                    }
                    return false;
                }
                catch { return false; }
            });
        }

        private void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            string? path = null;
            
            if (TabProcesses.IsChecked == true)
            {
                var selected = ProcessGrid.SelectedItem as ProcessInfo;
                path = selected?.Path;
            }
            else if (TabStartup.IsChecked == true)
            {
                var selected = StartupGrid.SelectedItem as StartupInfo;
                path = selected?.FilePath ?? selected?.Command;
            }
            
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    // Extract path from command if needed
                    if (path.StartsWith("\""))
                        path = path.Split('"')[1];
                    else if (path.Contains(" "))
                        path = path.Split(' ')[0];
                    
                    if (File.Exists(path))
                        Process.Start("explorer.exe", $"/select,\"{path}\"");
                    else if (Directory.Exists(Path.GetDirectoryName(path)))
                        Process.Start("explorer.exe", Path.GetDirectoryName(path)!);
                }
                catch { }
            }
        }

        #endregion

        #region Helpers

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1048576) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1073741824) return $"{bytes / 1048576.0:F1} MB";
            return $"{bytes / 1073741824.0:F1} GB";
        }

        private static async Task<string> RunCommandAsync(string command)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {command}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) return "Failed";
                    var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);
                    return output;
                }
                catch (Exception ex) { return ex.Message; }
            });
        }
        
        private static async Task<string> RunElevatedCommandAsync(string command)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Create a temp batch file to run the command
                    var tempBat = Path.Combine(Path.GetTempPath(), $"atlas_cmd_{Guid.NewGuid():N}.bat");
                    var tempOut = Path.Combine(Path.GetTempPath(), $"atlas_out_{Guid.NewGuid():N}.txt");
                    
                    File.WriteAllText(tempBat, $"@echo off\n{command} > \"{tempOut}\" 2>&1");
                    
                    var psi = new ProcessStartInfo
                    {
                        FileName = tempBat,
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    
                    using var proc = Process.Start(psi);
                    if (proc == null) return "Failed to start";
                    proc.WaitForExit(15000);
                    
                    var output = File.Exists(tempOut) ? File.ReadAllText(tempOut) : "No output";
                    
                    // Cleanup
                    try { File.Delete(tempBat); } catch { }
                    try { File.Delete(tempOut); } catch { }
                    
                    return output.Trim();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return "ERROR: User cancelled admin prompt";
                }
                catch (Exception ex) { return $"ERROR: {ex.Message}"; }
            });
        }

        #endregion
    }

    #region Data Models

    public class ProcessInfo
    {
        public string Name { get; set; } = "";
        public int PID { get; set; }
        public long MemoryBytes { get; set; }
        public string Memory { get; set; } = "";
        public string CPU { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public string Path { get; set; } = "";
    }

    public class StartupInfo
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Location { get; set; } = "";
        public string Status { get; set; } = "";
        public string? RegistryKey { get; set; }
        public string? ValueName { get; set; }
        public string? FilePath { get; set; }
    }

    public class TaskInfo
    {
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public string NextRun { get; set; } = "";
        public string LastRun { get; set; } = "";
        public string FullName { get; set; } = "";
    }

    #endregion
}
