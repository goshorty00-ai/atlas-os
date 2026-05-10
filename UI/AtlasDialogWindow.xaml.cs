using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AtlasAI.Services;

namespace AtlasAI.UI
{
    public partial class AtlasDialogWindow : Window
    {
        public AtlasDialogResult Result { get; private set; } = AtlasDialogResult.None;
        public bool DontShowAgain { get; private set; }

        private readonly AtlasDialogButtons _buttons;
        private readonly AtlasDialogButton _defaultButton;

        public AtlasDialogWindow(
            string title,
            string message,
            AtlasDialogButtons buttons,
            AtlasDialogIcon icon,
            AtlasDialogButton defaultButton,
            bool showDontShowAgain)
        {
            InitializeComponent();

            _buttons = buttons;
            _defaultButton = defaultButton;

            TitleText.Text = title;
            MessageText.Text = message;

            SetupIcon(icon);
            SetupButtons();

            if (showDontShowAgain)
            {
                DontShowAgainCheckbox.Visibility = Visibility.Visible;
            }

            // Ensure window is loaded before setting focus
            Loaded += AtlasDialogWindow_Loaded;
        }

        private void AtlasDialogWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Set focus to default button
            if (ButtonPanel.Children.Count > 0)
            {
                int index = _defaultButton switch
                {
                    AtlasDialogButton.Button1 => 0,
                    AtlasDialogButton.Button2 => Math.Min(1, ButtonPanel.Children.Count - 1),
                    AtlasDialogButton.Button3 => Math.Min(2, ButtonPanel.Children.Count - 1),
                    _ => 0
                };

                if (ButtonPanel.Children[index] is Button btn)
                {
                    btn.Focus();
                }
            }
        }

        private void SetupIcon(AtlasDialogIcon icon)
        {
            if (icon == AtlasDialogIcon.None)
            {
                IconText.Visibility = Visibility.Collapsed;
                return;
            }

            IconText.Visibility = Visibility.Visible;

            switch (icon)
            {
                case AtlasDialogIcon.Info:
                    IconText.Text = "ℹ️";
                    IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00d4ff"));
                    break;
                case AtlasDialogIcon.Warning:
                    IconText.Text = "⚠️";
                    IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b"));
                    break;
                case AtlasDialogIcon.Error:
                    IconText.Text = "❌";
                    IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ef4444"));
                    break;
                case AtlasDialogIcon.Question:
                    IconText.Text = "❓";
                    IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8b5cf6"));
                    break;
                case AtlasDialogIcon.Success:
                    IconText.Text = "✅";
                    IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22c55e"));
                    break;
            }
        }

        private void SetupButtons()
        {
            ButtonPanel.Children.Clear();

            switch (_buttons)
            {
                case AtlasDialogButtons.OK:
                    AddButton("OK", AtlasDialogResult.OK, true);
                    break;

                case AtlasDialogButtons.OKCancel:
                    AddButton("Cancel", AtlasDialogResult.Cancel, false);
                    AddButton("OK", AtlasDialogResult.OK, true);
                    break;

                case AtlasDialogButtons.YesNo:
                    AddButton("No", AtlasDialogResult.No, false);
                    AddButton("Yes", AtlasDialogResult.Yes, true);
                    break;

                case AtlasDialogButtons.YesNoCancel:
                    AddButton("Cancel", AtlasDialogResult.Cancel, false);
                    AddButton("No", AtlasDialogResult.No, false);
                    AddButton("Yes", AtlasDialogResult.Yes, true);
                    break;

                case AtlasDialogButtons.RetryCancel:
                    AddButton("Cancel", AtlasDialogResult.Cancel, false);
                    AddButton("Retry", AtlasDialogResult.Retry, true);
                    break;

                case AtlasDialogButtons.AbortRetryIgnore:
                    AddButton("Ignore", AtlasDialogResult.Ignore, false);
                    AddButton("Retry", AtlasDialogResult.Retry, false);
                    AddButton("Abort", AtlasDialogResult.Abort, true);
                    break;
            }

            // Reverse if default is not the last button
            if (_defaultButton == AtlasDialogButton.Button1 && ButtonPanel.Children.Count > 1)
            {
                var buttons = new UIElement[ButtonPanel.Children.Count];
                ButtonPanel.Children.CopyTo(buttons, 0);
                ButtonPanel.Children.Clear();
                foreach (var btn in buttons)
                {
                    ButtonPanel.Children.Add(btn);
                }
            }
        }

        private void AddButton(string text, AtlasDialogResult result, bool isDefault)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)FindResource("DialogButton"),
                IsDefault = isDefault && _defaultButton == AtlasDialogButton.Button1,
                Margin = new Thickness(8, 0, 0, 0)
            };

            button.Click += (s, e) =>
            {
                Result = result;
                DontShowAgain = DontShowAgainCheckbox.IsChecked == true;
                DialogResult = true;
            };

            // Handle Escape key for Cancel/No buttons
            if (result == AtlasDialogResult.Cancel || result == AtlasDialogResult.No)
            {
                button.IsCancel = true;
            }

            ButtonPanel.Children.Add(button);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Treat close button as Cancel or No, depending on button configuration
            Result = _buttons switch
            {
                AtlasDialogButtons.YesNo => AtlasDialogResult.No,
                AtlasDialogButtons.YesNoCancel => AtlasDialogResult.Cancel,
                AtlasDialogButtons.OKCancel => AtlasDialogResult.Cancel,
                AtlasDialogButtons.RetryCancel => AtlasDialogResult.Cancel,
                _ => AtlasDialogResult.None
            };

            DialogResult = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Handle Escape key
            if (e.Key == Key.Escape)
            {
                CloseButton_Click(this, new RoutedEventArgs());
            }
        }
    }
}
