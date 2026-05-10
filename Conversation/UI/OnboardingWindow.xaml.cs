using System;
using System.Windows;
using System.Windows.Media;
using AtlasAI.Conversation.Models;
using AtlasAI.Conversation.Services;

namespace AtlasAI.Conversation.UI
{
    public partial class OnboardingWindow : Window
    {
        private int _currentStep = 1;
        private const int TotalSteps = 4;
        
        public string? UserName { get; private set; }
        public ConversationStyle SelectedStyle { get; private set; } = ConversationStyle.Butler; // Always JARVIS
        public string? TriedAction { get; private set; }

        public OnboardingWindow()
        {
            InitializeComponent();
            UpdateStepVisibility();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            // Save current step data
            SaveCurrentStepData();

            if (_currentStep < TotalSteps)
            {
                _currentStep++;
                UpdateStepVisibility();
            }
            else
            {
                // Complete onboarding
                DialogResult = true;
                Close();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepVisibility();
            }
        }

        private void SaveCurrentStepData()
        {
            switch (_currentStep)
            {
                case 1:
                    UserName = string.IsNullOrWhiteSpace(NameInput.Text) ? null : NameInput.Text.Trim();
                    break;
                case 2:
                    // Always use Butler style (JARVIS)
                    SelectedStyle = ConversationStyle.Butler;
                    break;
            }
        }

        private void UpdateStepVisibility()
        {
            // Hide all panels
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Collapsed;
            Step3Panel.Visibility = Visibility.Collapsed;
            Step4Panel.Visibility = Visibility.Collapsed;

            // Show current panel
            switch (_currentStep)
            {
                case 1:
                    Step1Panel.Visibility = Visibility.Visible;
                    break;
                case 2:
                    Step2Panel.Visibility = Visibility.Visible;
                    break;
                case 3:
                    Step3Panel.Visibility = Visibility.Visible;
                    break;
                case 4:
                    Step4Panel.Visibility = Visibility.Visible;
                    NextButton.Content = "Get Started";
                    break;
            }

            // Update step dots
            Step1Dot.Fill = new SolidColorBrush(_currentStep >= 1 ? Color.FromRgb(0, 212, 170) : Color.FromRgb(48, 54, 61));
            Step2Dot.Fill = new SolidColorBrush(_currentStep >= 2 ? Color.FromRgb(0, 212, 170) : Color.FromRgb(48, 54, 61));
            Step3Dot.Fill = new SolidColorBrush(_currentStep >= 3 ? Color.FromRgb(0, 212, 170) : Color.FromRgb(48, 54, 61));
            Step4Dot.Fill = new SolidColorBrush(_currentStep >= 4 ? Color.FromRgb(0, 212, 170) : Color.FromRgb(48, 54, 61));

            // Show/hide back button
            BackButton.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Quick action handlers - these will trigger actions in the main window
        private void TryWeather_Click(object sender, RoutedEventArgs e)
        {
            TriedAction = "What's the weather in Middlesbrough?";
            DialogResult = true;
            Close();
        }

        private void TryScreenshot_Click(object sender, RoutedEventArgs e)
        {
            TriedAction = "Take a screenshot";
            DialogResult = true;
            Close();
        }

        private void TrySearch_Click(object sender, RoutedEventArgs e)
        {
            TriedAction = "Search the web for latest tech news";
            DialogResult = true;
            Close();
        }

        private void TryScan_Click(object sender, RoutedEventArgs e)
        {
            TriedAction = "Run a quick security scan";
            DialogResult = true;
            Close();
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
