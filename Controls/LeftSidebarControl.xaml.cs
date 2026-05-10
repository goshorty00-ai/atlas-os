using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AtlasAI.Controls
{
    public partial class LeftSidebarControl : UserControl
    {
        public event EventHandler<string>? TabChanged;
        public event EventHandler<string>? SidebarItemClicked;

        private RadioButton? _activeButton;
        private bool _suppressSelectionEvents;
        private bool _skipNextSmartHomeCheckedEvent;

        public LeftSidebarControl()
        {
            InitializeComponent();
            SetActiveButton("Chat");
        }
        
        private RadioButton[] Buttons => new[]
        {
            ChatButton,
            EmailButton,
            InternetButton,
            DownloadButton,
            FileExplorerButton,
            MediaButton,
            SmartHomeButton,
            MusicButton,
            SpeechButton,
            AiChefButton,
            QuizButton,
            ApiButton,
            SecurityButton,
            CreateButton,
            CodeButton
        };

        public void SetActiveButton(RadioButton button)
        {
            if (button == null) return;
            if (ReferenceEquals(_activeButton, button) && button.IsChecked == true) return;

            _suppressSelectionEvents = true;
            try
            {
                if (_activeButton != null && !ReferenceEquals(_activeButton, button))
                    _activeButton.IsChecked = false;

                _activeButton = button;
                _activeButton.IsChecked = true;
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
        }

        public void SetActiveButton(string buttonName)
        {
            switch (buttonName)
            {
                case "Chat":
                    SetActiveButton(ChatButton);
                    break;
                case "Media":
                case "Media Centre":
                    SetActiveButton(MediaButton);
                    break;
                case "Speech":
                case "Greetings":
                case "Responses":
                    SetActiveButton(SpeechButton);
                    break;
                case "SmartHome":
                case "Smart Home":
                    SetActiveButton(SmartHomeButton);
                    break;
                case "DJ":
                case "Music":
                    SetActiveButton(MusicButton);
                    break;
                case "Downloads":
                    SetActiveButton(DownloadButton);
                    break;
                case "API":
                    SetActiveButton(ApiButton);
                    break;
                case "Security":
                    SetActiveButton(SecurityButton);
                    break;
                case "FileExplorer":
                case "File Explorer":
                case "AI File Explorer":
                case "AI Files":
                    SetActiveButton(FileExplorerButton);
                    break;
                case "Email":
                    SetActiveButton(EmailButton);
                    break;
                case "Internet":
                case "AI Browser Hub":
                    SetActiveButton(InternetButton);
                    break;
                case "AiChef":
                case "AI Chef":
                    SetActiveButton(AiChefButton);
                    break;
                case "Quiz":
                case "AI Quiz Night":
                    SetActiveButton(QuizButton);
                    break;
                case "Create":
                    SetActiveButton(CreateButton);
                    break;
                case "Code":
                    SetActiveButton(CodeButton);
                    break;
            }
        }

        private void SidebarItem_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            if (sender is not RadioButton rb) return;

            _activeButton = rb;
            var key = (rb.Tag?.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) return;

            if (_skipNextSmartHomeCheckedEvent && string.Equals(key, "SmartHome", StringComparison.OrdinalIgnoreCase))
            {
                _skipNextSmartHomeCheckedEvent = false;
                return;
            }

            NotifySidebarSelection(key);
        }

        private void SmartHomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _activeButton = SmartHomeButton;
                _skipNextSmartHomeCheckedEvent = true;
                NotifySidebarSelection("SmartHome");
                OpenSmartHomeDirectly();
            }
            catch
            {
            }
        }

        private void SmartHomeButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                _activeButton = SmartHomeButton;
                _skipNextSmartHomeCheckedEvent = true;
                NotifySidebarSelection("SmartHome");
                OpenSmartHomeDirectly();
            }
            catch
            {
            }
        }

        private void OpenSmartHomeDirectly()
        {
            try
            {
                if (Window.GetWindow(this) is ChatWindow chatWindow)
                {
                    chatWindow.ShowPage("smarthome");
                    return;
                }

                if (Window.GetWindow(this) is CommandCenterWindow commandCenterWindow)
                {
                    commandCenterWindow.NavigateToView("AI SMART HOME");
                }
            }
            catch
            {
            }
        }

        private void NotifySidebarSelection(string key)
        {
            var tabName = string.Equals(key, "DJ", StringComparison.OrdinalIgnoreCase) ? "Music" : key;
            TabChanged?.Invoke(this, tabName);
            SidebarItemClicked?.Invoke(this, key);
        }

        public void SetActiveTab(string tabName)
        {
            switch (tabName)
            {
                case "Chat":
                    SetActiveButton(ChatButton);
                    break;
                case "Media":
                case "Media Centre":
                    SetActiveButton(MediaButton);
                    break;
                case "Speech":
                case "Greetings":
                case "Responses":
                    SetActiveButton(SpeechButton);
                    break;
                case "SmartHome":
                case "Smart Home":
                    SetActiveButton(SmartHomeButton);
                    break;
                case "Music":
                case "DJ":
                    SetActiveButton(MusicButton);
                    break;
                case "Downloads":
                    SetActiveButton(DownloadButton);
                    break;
                case "API":
                    SetActiveButton(ApiButton);
                    break;
                case "Security":
                    SetActiveButton(SecurityButton);
                    break;
                case "FileExplorer":
                case "File Explorer":
                case "AI File Explorer":
                case "AI Files":
                    SetActiveButton(FileExplorerButton);
                    break;
                case "Email":
                    SetActiveButton(EmailButton);
                    break;
                case "Internet":
                case "AI Browser Hub":
                    SetActiveButton(InternetButton);
                    break;
                case "AiChef":
                case "AI Chef":
                    SetActiveButton(AiChefButton);
                    break;
                case "Quiz":
                case "AI Quiz Night":
                    SetActiveButton(QuizButton);
                    break;
                case "Create":
                    SetActiveButton(CreateButton);
                    break;
                case "Code":
                    SetActiveButton(CodeButton);
                    break;
            }
        }

        private void SidebarScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                if (SidebarScrollViewer == null) return;
                if (e.Delta < 0) SidebarScrollViewer.LineDown();
                else SidebarScrollViewer.LineUp();
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void SidebarScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key != Key.Down && e.Key != Key.Up) return;

                var buttons = Buttons.Where(b => b != null).ToArray();
                if (buttons.Length == 0) return;

                var current = _activeButton ?? buttons.FirstOrDefault(b => b.IsChecked == true) ?? buttons[0];
                var idx = Array.IndexOf(buttons, current);
                if (idx < 0) idx = 0;

                var next = e.Key == Key.Down ? Math.Min(idx + 1, buttons.Length - 1) : Math.Max(idx - 1, 0);
                if (next == idx) return;

                var rb = buttons[next];
                rb.Focus();
                rb.IsChecked = true;
                rb.BringIntoView();

                e.Handled = true;
            }
            catch
            {
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is SettingsWindow sw && sw.IsLoaded)
                    {
                        sw.Activate();
                        if (sw.WindowState == WindowState.Minimized)
                            sw.WindowState = WindowState.Normal;
                        return;
                    }
                }

                var win = new SettingsWindow();
                win.Owner = Window.GetWindow(this);
				win.ShowDialog();
            }
            catch
            {
            }
        }
    }
}
