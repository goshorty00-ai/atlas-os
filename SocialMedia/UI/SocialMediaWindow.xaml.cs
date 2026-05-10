using System;
using System.Windows;
using System.Windows.Input;

namespace AtlasAI.SocialMedia.UI
{
    public partial class SocialMediaWindow : Window
    {
        public SocialMediaWindow()
        {
            InitializeComponent();
        }
        
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }
        
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeBtn.Content = "□";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeBtn.Content = "❐";
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
