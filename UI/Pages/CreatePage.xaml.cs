using System.Windows;
using System.Windows.Controls;
using AtlasAI.SocialMedia.UI;

namespace AtlasAI.UI.Pages
{
    public partial class CreatePage : UserControl
    {
        private SocialMediaPanel? _socialPanel;

        public CreatePage()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Create the SocialMediaPanel and embed it
            if (_socialPanel == null)
            {
                _socialPanel = new SocialMediaPanel();
                ContentHost.Child = _socialPanel;
            }
        }
    }
}
