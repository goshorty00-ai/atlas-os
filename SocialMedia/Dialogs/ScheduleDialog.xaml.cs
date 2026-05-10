using System;
using System.Windows;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia
{
    public partial class ScheduleDialog : Window
    {
        public SocialPlatform Platform { get; private set; }
        public DateTime ScheduledTime { get; private set; }
        
        public ScheduleDialog()
        {
            InitializeComponent();
        }
        
        private void Schedule_Click(object sender, RoutedEventArgs e)
        {
            Platform = (SocialPlatform)PlatformBox.SelectedIndex;
            
            var date = DatePicker.SelectedDate ?? DateTime.Today;
            var hour = HourBox.SelectedIndex;
            var minute = MinuteBox.SelectedIndex * 15;
            
            ScheduledTime = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0);
            
            if (ScheduledTime <= DateTime.Now)
            {
                MessageBox.Show("Please select a future date and time.", "Invalid Time", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
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
