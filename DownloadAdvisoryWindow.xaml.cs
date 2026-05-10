using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AtlasAI.Voice;

namespace AtlasAI
{
    public enum DownloadRiskLevel { Safe, Unknown, Elevated, High }

    public partial class DownloadAdvisoryWindow : Window
    {
        public DownloadAdvisoryWindow(
            string fileName,
            string ext,
            string sourceHost,
            string sizeText,
            DownloadRiskLevel riskLevel,
            string riskExplanation)
        {
            InitializeComponent();

            // Populate metadata labels
            FileNameLabel.Text = fileName;
            ExtLabel.Text      = string.IsNullOrWhiteSpace(ext) || ext == "(none)" ? "unknown" : ext;
            SourceLabel.Text   = string.IsNullOrWhiteSpace(sourceHost) ? "unknown" : sourceHost;
            SizeLabel.Text     = string.IsNullOrWhiteSpace(sizeText)   ? "unknown" : sizeText;
            ExplanationLabel.Text = riskExplanation;

            // Risk level → accent color, badge tag, header icon
            var (accentColor, riskTag, icon) = riskLevel switch
            {
                DownloadRiskLevel.High     => (Color.FromRgb(239, 68,  68),  "HIGH",     "⚠️"),
                DownloadRiskLevel.Elevated => (Color.FromRgb(245, 158, 11),  "ELEVATED", "⚡"),
                DownloadRiskLevel.Unknown  => (Color.FromRgb(34,  211, 238), "UNKNOWN",  "🔍"),
                _                          => (Color.FromRgb(34,  197, 94),  "SAFE",     "✓"),
            };

            var accentBrush = new SolidColorBrush(accentColor);

            // Header
            HeaderIcon.Text        = icon;
            HeaderTitle.Foreground = accentBrush;

            // Card border + glow
            MainCard.BorderBrush = accentBrush;
            MainCard.Effect = new DropShadowEffect
            {
                Color       = accentColor,
                BlurRadius  = 34,
                ShadowDepth = 0,
                Opacity     = 0.45
            };

            // Risk badge
            RiskLabel.Text       = $"RISK: {riskTag}";
            RiskLabel.Foreground = accentBrush;
            RiskBadge.BorderBrush    = accentBrush;
            RiskBadge.BorderThickness = new Thickness(1);
            RiskBadge.Background = new SolidColorBrush(
                Color.FromArgb(35, accentColor.R, accentColor.G, accentColor.B));

            // Confirm button accent
            ConfirmBtn.Background = new SolidColorBrush(
                Color.FromArgb(30, accentColor.R, accentColor.G, accentColor.B));
            ConfirmBtn.Foreground    = accentBrush;
            ConfirmBtn.BorderBrush   = new SolidColorBrush(
                Color.FromArgb(100, accentColor.R, accentColor.G, accentColor.B));
            ConfirmBtn.BorderThickness = new Thickness(1);

            // Button / close handlers
            ConfirmBtn.Click += (_, _) => { DialogResult = true; };
            CancelBtn.Click  += (_, _) => { DialogResult = false; };
            CloseX.MouseLeftButtonDown += (_, _) => { DialogResult = false; };

            // Cancel advisory speech on any close path (buttons, X, Alt+F4)
            Closed += (_, _) => SpeechCoordinator.Instance.CancelCurrentSpeech();

            // Allow window drag from card background
            MainCard.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                    DragMove();
            };

            // Ensure SpeechCoordinator has a VoiceManager in Command Center scenarios.
            try
            {
                var commandCenterWindow = Application.Current?.Windows.OfType<CommandCenterWindow>().FirstOrDefault();
                var voiceManager = commandCenterWindow?.VoiceManager;
                if (voiceManager != null)
                    SpeechCoordinator.Instance.SetVoiceManager(voiceManager);
            }
            catch
            {
                // Keep advisory flow non-blocking even if voice binding fails.
            }

            // Speak advisory once, non-blocking, when window opens
            var extDisplay  = string.IsNullOrWhiteSpace(ext) || ext == "(none)" ? "unknown"
                              : ext.TrimStart('.').ToUpperInvariant();
            var spokenText  = $"Atlas download guard. File detected: {fileName} from {sourceHost}. " +
                              $"File type: {extDisplay}. Risk level: {riskTag.ToLowerInvariant()}. " +
                              $"{riskExplanation}";
            var reason = riskLevel is DownloadRiskLevel.Elevated or DownloadRiskLevel.High
                ? "download_security_advisory"
                : "download_advisory";
            _ = SpeechCoordinator.Instance.SpeakSystemAsync(spokenText, reason);
        }
    }
}
