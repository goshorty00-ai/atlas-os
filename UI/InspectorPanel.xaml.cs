using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AtlasAI.Voice;
using AtlasAI.InAppAssistant.Models;
using AtlasAI.InAppAssistant.Services;
using AtlasAI.SecuritySuite.Services;

namespace AtlasAI.UI
{
    /// <summary>
    /// Inspector Panel - Right-side collapsible panel showing:
    /// - Active Context (current app, window, path)
    /// - Security Scan status and controls
    /// - Text-to-Speech ElevenLabs voice settings
    /// </summary>
    public partial class InspectorPanel : UserControl
    {
        private WindowsContextService? _contextService;
        private SecuritySuiteManager? _securityManager;
        private System.Timers.Timer? _contextUpdateTimer;
        private bool _isUpdatingSliders = false;

        // Events for parent window to handle
        public event Action? OnCloseRequested;
        public event Action? OnScanRequested;
        public event Action<double, double, double, bool>? OnVoiceSettingsChanged;

        public InspectorPanel()
        {
            InitializeComponent();
            
            // Load current ElevenLabs voice settings
            LoadCurrentVoiceSettings();
        }

        /// <summary>
        /// Initialize with services from parent window
        /// </summary>
        public void Initialize(WindowsContextService? contextService, SecuritySuiteManager? securityManager)
        {
            _contextService = contextService;
            _securityManager = securityManager;

            // Subscribe to context changes
            if (_contextService != null)
            {
                _contextService.ActiveAppChanged += OnActiveAppChanged;
            }

            // Subscribe to security events
            if (_securityManager != null)
            {
                _securityManager.DashboardUpdated += OnDashboardUpdated;
                
                // Load initial security status
                UpdateSecurityStatus();
            }

            // Start periodic context updates
            StartContextUpdates();
            
            Debug.WriteLine("[InspectorPanel] Initialized with services");
        }

        /// <summary>
        /// Load current ElevenLabs voice settings into sliders
        /// </summary>
        private void LoadCurrentVoiceSettings()
        {
            _isUpdatingSliders = true;
            try
            {
                var settings = ElevenLabsProvider.CurrentVoiceSettings;
                StabilitySlider.Value = settings.Stability;
                SimilaritySlider.Value = settings.SimilarityBoost;
                StyleSlider.Value = settings.Style;
                SpeakerBoostToggle.IsChecked = settings.UseSpeakerBoost;
                
                UpdateSliderValueTexts();
            }
            finally
            {
                _isUpdatingSliders = false;
            }
        }

        /// <summary>
        /// Update the value display texts for sliders
        /// </summary>
        private void UpdateSliderValueTexts()
        {
            StabilityValueText.Text = StabilitySlider.Value.ToString("F2");
            SimilarityValueText.Text = SimilaritySlider.Value.ToString("F2");
            StyleValueText.Text = StyleSlider.Value.ToString("F2");
        }

        /// <summary>
        /// Start periodic context updates
        /// </summary>
        private void StartContextUpdates()
        {
            _contextUpdateTimer = new System.Timers.Timer(1000); // Update every second
            _contextUpdateTimer.Elapsed += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (_contextService != null)
                        {
                            var context = _contextService.GetActiveAppContext();
                            UpdateActiveContext(context);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[InspectorPanel] Context update error: {ex.Message}");
                    }
                });
            };
            _contextUpdateTimer.Start();
        }

        /// <summary>
        /// Update Active Context section
        /// </summary>
        public void UpdateActiveContext(ActiveAppContext? context)
        {
            if (context == null)
            {
                AppIcon.Text = "❓";
                AppNameText.Text = "None";
                ProcessNameText.Text = "—";
                FilePathText.Text = "—";
                WindowTitleText.Text = "—";
                return;
            }

            // Set icon based on category
            AppIcon.Text = GetCategoryIcon(context.Category);
            
            // Set app name (friendly name from process)
            AppNameText.Text = GetFriendlyAppName(context.ProcessName);
            ProcessNameText.Text = $"{context.ProcessName}.exe";
            
            // Set path
            FilePathText.Text = !string.IsNullOrEmpty(context.ExecutablePath) 
                ? context.ExecutablePath 
                : "—";
            
            // Set window title
            WindowTitleText.Text = !string.IsNullOrEmpty(context.WindowTitle) 
                ? context.WindowTitle 
                : "—";
        }

        /// <summary>
        /// Update Security Scan section
        /// </summary>
        public void UpdateSecurityStatus()
        {
            if (_securityManager == null)
            {
                SecurityStatusText.Text = "UNKNOWN";
                LastScanText.Text = "Never";
                DefinitionsText.Text = "Not available";
                return;
            }

            try
            {
                var status = _securityManager.GetDashboardStatus();
                
                // Update status badge
                switch (status.ProtectionScore.Status)
                {
                    case SecuritySuite.Models.ProtectionStatus.Protected:
                        SecurityStatusText.Text = "SAFE";
                        SecurityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80));
                        SecurityBadge.Background = new SolidColorBrush(Color.FromArgb(32, 63, 185, 80));
                        break;
                    case SecuritySuite.Models.ProtectionStatus.AtRisk:
                        SecurityStatusText.Text = "AT RISK";
                        SecurityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(210, 153, 34));
                        SecurityBadge.Background = new SolidColorBrush(Color.FromArgb(32, 210, 153, 34));
                        break;
                    case SecuritySuite.Models.ProtectionStatus.Critical:
                        SecurityStatusText.Text = "CRITICAL";
                        SecurityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(248, 81, 73));
                        SecurityBadge.Background = new SolidColorBrush(Color.FromArgb(32, 248, 81, 73));
                        break;
                }

                // Update last scan
                if (status.LastScan != null)
                {
                    var scanTime = status.LastScan.EndTime;
                    var timeSince = DateTime.Now - scanTime;
                    
                    if (timeSince.TotalMinutes < 1)
                        LastScanText.Text = "Just now";
                    else if (timeSince.TotalHours < 1)
                        LastScanText.Text = $"{(int)timeSince.TotalMinutes} min ago";
                    else if (timeSince.TotalDays < 1)
                        LastScanText.Text = $"Today, {scanTime:h:mm tt}";
                    else
                        LastScanText.Text = scanTime.ToString("MMM d, h:mm tt");
                }
                else
                {
                    LastScanText.Text = "Never";
                }

                // Update definitions
                var defsInfo = status.Definitions;
                var defsAge = DateTime.Now - defsInfo.LastUpdated;
                
                if (defsAge.TotalHours < 24)
                    DefinitionsText.Text = "Up to date";
                else if (defsAge.TotalDays < 3)
                    DefinitionsText.Text = $"{(int)defsAge.TotalDays} day(s) old";
                else
                    DefinitionsText.Text = $"Outdated ({(int)defsAge.TotalDays} days)";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectorPanel] Security status update error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get icon for app category
        /// </summary>
        private string GetCategoryIcon(AppCategory category)
        {
            return category switch
            {
                AppCategory.Browser => "🌐",
                AppCategory.FileExplorer => "📁",
                AppCategory.IDE => "💻",
                AppCategory.Office => "📄",
                AppCategory.Terminal => "⌨️",
                AppCategory.MediaPlayer => "🎵",
                AppCategory.TextEditor => "📝",
                AppCategory.Communication => "💬",
                AppCategory.System => "⚙️",
                _ => "📱"
            };
        }

        /// <summary>
        /// Get friendly app name from process name
        /// </summary>
        private string GetFriendlyAppName(string processName)
        {
            return processName.ToLower() switch
            {
                "explorer" => "File Explorer",
                "chrome" => "Google Chrome",
                "msedge" => "Microsoft Edge",
                "firefox" => "Mozilla Firefox",
                "code" => "Visual Studio Code",
                "devenv" => "Visual Studio",
                "spotify" => "Spotify",
                "discord" => "Discord",
                "slack" => "Slack",
                "teams" => "Microsoft Teams",
                "notepad" => "Notepad",
                "notepad++" => "Notepad++",
                "winword" => "Microsoft Word",
                "excel" => "Microsoft Excel",
                "powerpnt" => "PowerPoint",
                "outlook" => "Outlook",
                "cmd" => "Command Prompt",
                "powershell" => "PowerShell",
                "windowsterminal" => "Windows Terminal",
                _ => processName
            };
        }

        #region Event Handlers

        private void OnActiveAppChanged(object? sender, ActiveAppContext context)
        {
            Dispatcher.Invoke(() => UpdateActiveContext(context));
        }

        private void OnDashboardUpdated(SecuritySuite.Models.DashboardStatus status)
        {
            Dispatcher.Invoke(() => UpdateSecurityStatus());
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            OnCloseRequested?.Invoke();
        }

        private void ScanNowBtn_Click(object sender, RoutedEventArgs e)
        {
            OnScanRequested?.Invoke();
        }

        private void StabilitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSliders) return;
            
            StabilityValueText.Text = e.NewValue.ToString("F2");
            ApplyVoiceSettings();
        }

        private void SimilaritySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSliders) return;
            
            SimilarityValueText.Text = e.NewValue.ToString("F2");
            ApplyVoiceSettings();
        }

        private void StyleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSliders) return;
            
            StyleValueText.Text = e.NewValue.ToString("F2");
            ApplyVoiceSettings();
        }

        private void SpeakerBoostToggle_Click(object sender, RoutedEventArgs e)
        {
            ApplyVoiceSettings();
        }

        /// <summary>
        /// Apply voice settings to ElevenLabs provider
        /// </summary>
        private void ApplyVoiceSettings()
        {
            var stability = StabilitySlider.Value;
            var similarity = SimilaritySlider.Value;
            var style = StyleSlider.Value;
            var speakerBoost = SpeakerBoostToggle.IsChecked == true;

            // Update ElevenLabs provider settings
            ElevenLabsProvider.UpdateVoiceSettings(stability, similarity, style, speakerBoost);
            
            // Notify parent
            OnVoiceSettingsChanged?.Invoke(stability, similarity, style, speakerBoost);
            
            Debug.WriteLine($"[InspectorPanel] Voice settings applied - Stability: {stability:F2}, Similarity: {similarity:F2}, Style: {style:F2}, Boost: {speakerBoost}");
        }

        #endregion

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            _contextUpdateTimer?.Stop();
            _contextUpdateTimer?.Dispose();
            
            if (_contextService != null)
            {
                _contextService.ActiveAppChanged -= OnActiveAppChanged;
            }
            
            if (_securityManager != null)
            {
                _securityManager.DashboardUpdated -= OnDashboardUpdated;
            }
        }
    }

    /// <summary>
    /// Converter for slider track width (not used with standard slider, but available for custom templates)
    /// </summary>
    public class SliderWidthConverter : IValueConverter
    {
        public static readonly SliderWidthConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double val)
            {
                // Assuming slider width of ~250px, scale value (0-1) to width
                return val * 250;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
