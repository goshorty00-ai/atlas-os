using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AtlasAI.Core;

namespace AtlasAI.UI
{
    /// <summary>
    /// View model for test result display
    /// </summary>
    public class TestResultViewModel
    {
        public string TestName { get; set; } = "";
        public string Message { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string StatusIcon { get; set; } = "";
        public Brush TypeBackground { get; set; } = Brushes.Gray;
        public bool Passed { get; set; }
    }
    
    /// <summary>
    /// Safety Self-Test Window - runs and displays safety validation tests
    /// </summary>
    public partial class SafetySelfTestWindow : Window
    {
        private readonly SafetySelfTest _selfTest;
        private List<SelfTestResult> _results = new();
        
        public SafetySelfTestWindow()
        {
            InitializeComponent();
            _selfTest = new SafetySelfTest();
        }
        
        private async void RunTests_Click(object sender, RoutedEventArgs e)
        {
            RunTestsButton.IsEnabled = false;
            RunTestsButton.Content = "⏳ Running...";
            StatusText.Text = "Running safety tests...";
            ResultsList.ItemsSource = null;
            PassedCount.Text = "0";
            FailedCount.Text = "0";
            
            try
            {
                _results = await _selfTest.RunAllTestsAsync();
                
                var viewModels = _results.Select(r => new TestResultViewModel
                {
                    TestName = r.TestName,
                    Message = r.Message,
                    OperationType = r.OperationType.ToString(),
                    StatusIcon = r.Passed ? "✅" : "❌",
                    Passed = r.Passed,
                    TypeBackground = GetTypeBackground(r.OperationType)
                }).ToList();
                
                ResultsList.ItemsSource = viewModels;
                
                var passed = _results.Count(r => r.Passed);
                var failed = _results.Count(r => !r.Passed);
                
                PassedCount.Text = passed.ToString();
                FailedCount.Text = failed.ToString();
                
                if (failed == 0)
                {
                    StatusText.Text = $"✅ All {passed} tests passed - Safety system is working correctly";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                }
                else
                {
                    StatusText.Text = $"⚠️ {failed} test(s) failed - Safety system may have issues!";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error running tests: {ex.Message}";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                Debug.WriteLine($"[SafetySelfTest] Error: {ex}");
            }
            finally
            {
                RunTestsButton.IsEnabled = true;
                RunTestsButton.Content = "▶ Run Tests";
            }
        }
        
        private Brush GetTypeBackground(OperationType type)
        {
            return type switch
            {
                OperationType.CommandExecution => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                OperationType.RegistryWrite or OperationType.RegistryDelete => new SolidColorBrush(Color.FromRgb(249, 115, 22)),
                OperationType.ProcessKillCritical => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                OperationType.CleanupLeftovers => new SolidColorBrush(Color.FromRgb(234, 179, 8)),
                OperationType.StartupEntryChange => new SolidColorBrush(Color.FromRgb(168, 85, 247)),
                OperationType.ServiceChange => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                OperationType.ScheduledTaskChange => new SolidColorBrush(Color.FromRgb(14, 165, 233)),
                OperationType.SystemFileDelete or OperationType.FileDelete or OperationType.FolderDelete => new SolidColorBrush(Color.FromRgb(236, 72, 153)),
                OperationType.Uninstall => new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                _ => new SolidColorBrush(Color.FromRgb(100, 100, 100))
            };
        }
        
        private void ViewLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logDir = SafetyKernel.Instance.LogDirectory;
                if (System.IO.Directory.Exists(logDir))
                {
                    Process.Start("explorer.exe", logDir);
                }
                else
                {
                    MessageBox.Show($"Log directory not found:\n{logDir}", "Logs", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
