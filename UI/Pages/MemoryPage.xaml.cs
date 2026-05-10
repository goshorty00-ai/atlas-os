using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Controls;

namespace AtlasAI.UI.Pages
{
    public partial class MemoryPage : UserControl
    {
        public MemoryPage()
        {
            InitializeComponent();
        }

        private void MediaBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Navigate to Media Centre with memory context
                var chatWindow = Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault();
                if (chatWindow != null)
                {
                    // Switch to Media tab
                    chatWindow.ShowPage("media");
                    
                    // Pass memory context to Media Centre (defaults to Images section)
                    var mediaControl = chatWindow.FindName("MediaPageRoot") as MediaCenterControl;
                    mediaControl?.NavigateWithContext("memory");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MemoryPage] Media navigation error: {ex.Message}");
            }
        }
    }
}
