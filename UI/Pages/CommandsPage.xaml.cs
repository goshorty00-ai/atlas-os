using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Controls;

namespace AtlasAI.UI.Pages
{
    public partial class CommandsPage : UserControl
    {
        public CommandsPage()
        {
            InitializeComponent();
        }

        private void MediaBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Navigate to Media Centre with commands context
                var chatWindow = Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault();
                if (chatWindow != null)
                {
                    // Switch to Media tab
                    chatWindow.ShowPage("media");
                    
                    // Pass commands context to Media Centre
                    var mediaControl = chatWindow.FindName("MediaPageRoot") as MediaCenterControl;
                    mediaControl?.NavigateWithContext("commands");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandsPage] Media navigation error: {ex.Message}");
            }
        }
    }
}
