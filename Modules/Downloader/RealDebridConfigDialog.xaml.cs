using System.Windows;

namespace AtlasAI.Modules.Downloader
{
    public partial class RealDebridConfigDialog : Window
    {
        public string Token { get; private set; } = "";

        public RealDebridConfigDialog()
        {
            InitializeComponent();
            TokenBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Token = TokenBox.Password.Trim();
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
