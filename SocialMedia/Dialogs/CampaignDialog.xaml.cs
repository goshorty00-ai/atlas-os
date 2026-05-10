using System;
using System.Collections.Generic;
using System.Windows;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia
{
    public partial class CampaignDialog : Window
    {
        public string BrandId { get; private set; } = "";
        public string CampaignName { get; private set; } = "";
        public CampaignGoal Goal { get; private set; }
        public List<SocialPlatform> Platforms { get; private set; } = new();
        public DateTime StartDate { get; private set; }
        public DateTime? EndDate { get; private set; }
        
        public CampaignDialog()
        {
            InitializeComponent();
        }
        
        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Please enter a campaign name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            CampaignName = NameBox.Text.Trim();
            Goal = (CampaignGoal)GoalBox.SelectedIndex;
            
            Platforms.Clear();
            if (TikTokCheck.IsChecked == true) Platforms.Add(SocialPlatform.TikTok);
            if (InstagramCheck.IsChecked == true) Platforms.Add(SocialPlatform.Instagram);
            if (FacebookCheck.IsChecked == true) Platforms.Add(SocialPlatform.Facebook);
            if (YouTubeCheck.IsChecked == true) Platforms.Add(SocialPlatform.YouTube);
            
            if (Platforms.Count == 0)
            {
                MessageBox.Show("Please select at least one platform.", "No Platform", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            StartDate = StartDatePicker.SelectedDate ?? DateTime.Today;
            EndDate = EndDatePicker.SelectedDate;
            
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
