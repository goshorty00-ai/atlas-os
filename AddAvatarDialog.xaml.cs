using System.Windows;
using System.Windows.Media;
using AtlasAI.Avatar;

namespace AtlasAI {
    public partial class AddAvatarDialog : Window
    {
        public string? AvatarName => string.IsNullOrWhiteSpace(NameTextBox.Text) ? null : NameTextBox.Text.Trim();
        public string? AvatarUrl => string.IsNullOrWhiteSpace(UrlTextBox.Text) ? null : UrlTextBox.Text.Trim();

        public AddAvatarDialog()
        {
            InitializeComponent();
        }

        private void UrlTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var url = UrlTextBox.Text.Trim();
            var isValid = ReadyPlayerMeManager.IsValidReadyPlayerMeUrl(url);
            
            if (string.IsNullOrEmpty(url))
            {
                UrlValidationText.Visibility = Visibility.Collapsed;
                AddButton.IsEnabled = false;
            }
            else if (isValid)
            {
                UrlValidationText.Text = "✅ Valid Ready Player Me URL";
                UrlValidationText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                UrlValidationText.Visibility = Visibility.Visible;
                AddButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);
            }
            else
            {
                UrlValidationText.Text = "❌ Invalid URL - must be a Ready Player Me .glb file";
                UrlValidationText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                UrlValidationText.Visibility = Visibility.Visible;
                AddButton.IsEnabled = false;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}